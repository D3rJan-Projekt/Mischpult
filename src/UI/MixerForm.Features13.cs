using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.19 (3.0 Schritt 3): Hall/Echo pro Kanal, Ausgangs-EQ und Kopfhoerer-Raumklang, Vorhoeren (CUE),
    /// Mehrspur-Aufnahme und Aufnahmen als FLAC/MP3. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Hall und Echo

        /// <summary>Effekt des Kanals fuer den Mischer (null = keiner).</summary>
        private ChannelFx FxFor(ChannelConfig c) =>
            !_settings.FxOff && c.FxType > 0 ? new ChannelFx(c.FxType, c.FxPreset, c.FxMix ?? 0.25f) : null;

        private bool CueActive => _settings.CueOn && _settings.CueDevice != null;
        private bool CueFor(ChannelConfig c) => CueActive && c.Cue;
        private bool _multiRec;

        /// <summary>Muss der Kanal durchs Mischpult laufen (Effekt, Vorhoeren, Ausgangs-Klang, Mehrspur-Aufnahme)?</summary>
        private bool ForceDsp(ChannelConfig c)
        {
            if (c.AlwaysDsp) return true;   // 3.4.1
            if (ExtraFor(c) != null) return true;   // 3.4.6
            if (FxFor(c) != null || CueFor(c) || _multiRec) return true;
            if (StereoOf(c) != 0 || DelayOf(c) > 0) return true;   // 3.2: mono/getauscht, Verzoegerung
            if (_settings.OutFxOff || _settings.OutputFx == null) return false;
            string dev = (MultiOutput ? c.OutputDevice : null) ?? DefaultDevice?.Id;
            return dev != null && OutFxActive(dev);
        }

        public static readonly float[] FxMixSteps = { 0.10f, 0.20f, 0.30f, 0.45f, 0.60f };

        private void SetChannelFx(ChannelConfig c, int type, int preset)
        {
            if (type > 0 && !_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "Effekte nicht verfügbar") + "\n\nHall und Echo brauchen Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            c.FxType = type;
            c.FxPreset = preset;
            OnChannelEdited(c);
            if (type > 0) ShowOsd("Effekt · " + c.Name, FxText(c), -1, FxColor);
        }

        private void SetChannelFxMix(ChannelConfig c, float mix)
        {
            c.FxMix = mix;
            OnChannelEdited(c);
        }

        public static readonly Color FxColor = Color.FromArgb(0xb4, 0x8c, 0xff), CueColor = Color.FromArgb(0x3c, 0xd6, 0xc8);

        internal static string FxText(int type, int preset, float mix)
        {
            if (type <= 0) return "aus";
            string n = type == ChannelFx.Hall ? "Hall · " + ChannelFx.HallNames[Math.Max(0, Math.Min(2, preset))] : "Echo · " + ChannelFx.EchoNames[Math.Max(0, Math.Min(2, preset))];
            return n + " · " + (int)Math.Round(mix * 100) + " %";
        }

        private string FxText(ChannelConfig c) => FxText(c.FxType, c.FxPreset, c.FxMix ?? 0.25f);

        /// <summary>Kanal-Menue: Effekt ▸</summary>
        private ToolStripMenuItem FxSub(ChannelConfig c)
        {
            var sub = Sub("Effekt: " + (c.FxType == 0 ? "aus" : c.FxType == ChannelFx.Hall ? "Hall" : "Echo"));
            sub.DropDownItems.Add(new ToolStripMenuItem("Kein Effekt", null, (s, e) => SetChannelFx(c, 0, c.FxPreset)) { Checked = c.FxType == 0 });
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(Header("Hall"));
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(ChannelFx.HallNames[i], null, (s, e) => SetChannelFx(c, ChannelFx.Hall, k)) { Checked = c.FxType == ChannelFx.Hall && c.FxPreset == i });
            }
            sub.DropDownItems.Add(Header("Echo"));
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(ChannelFx.EchoNames[i], null, (s, e) => SetChannelFx(c, ChannelFx.Echo, k)) { Checked = c.FxType == ChannelFx.Echo && c.FxPreset == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            var mix = Sub("Anteil: " + (int)Math.Round((c.FxMix ?? 0.25f) * 100) + " %");
            foreach (var v in FxMixSteps)
            {
                float vv = v;
                mix.DropDownItems.Add(new ToolStripMenuItem((int)Math.Round(v * 100) + " %" + (Math.Abs(v - 0.30f) < 0.001f ? "  (kräftig)" : Math.Abs(v - 0.10f) < 0.001f ? "  (dezent)" : ""), null, (s, e) => SetChannelFxMix(c, vv))
                { Checked = Math.Abs((c.FxMix ?? 0.25f) - v) < 0.026f });
            }
            mix.Enabled = c.FxType > 0;
            sub.DropDownItems.Add(mix);
            sub.DropDownItems.Add(Hint("Läuft durchs Mischpult (wie der EQ)."));
            return sub;
        }

        // ================================================================ Vorhoeren (CUE)

        private float CueDb => _settings.CueDb ?? 0f;
        private CueMix _cueSent;

        private void PushCue()
        {
            CueMix want = CueActive && _settings.Channels.Any(c => c.Cue) ? new CueMix(_settings.CueDevice, (float)Math.Pow(10, CueDb / 20.0)) : null;
            if (want == null ? _cueSent == null : want.SameAs(_cueSent)) return;
            _cueSent = want;
            Backend.SetCue(want);
        }

        private void SetChannelCue(ChannelConfig c, bool on)
        {
            if (on && !CueActive)
            {
                ShowOsd("Vorhören", "Erst unter OPTIONEN → Ton ein Gerät wählen", -1, Theme.TextDim);
                return;
            }
            if (on && !_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "Vorhören nicht verfügbar") + "\n\nVorhören braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            c.Cue = on;
            OnChannelEdited(c);
            ShowOsd("Vorhören · " + c.Name, on ? "auf " + (_settings.CueDeviceName ?? "Kopfhörer") : "aus", -1, on ? CueColor : Theme.TextDim);
        }

        private void SetCueOn(bool on)
        {
            _settings.CueOn = on;
            PushPolicy();
            RefreshFxPills();
            ScheduleSave();
        }

        private void SetCueDevice(string id, string name)
        {
            _settings.CueDevice = id;
            _settings.CueDeviceName = name;
            if (id != null) _settings.CueOn = true;
            PushPolicy();
            RefreshFxPills();
            ScheduleSave();
        }

        private void SetCueDb(float db, bool save)
        {
            _settings.CueDb = (float)Math.Round(Math.Max(-30f, Math.Min(6f, db)));
            PushCue();
            if (save) ScheduleSave();
        }

        private void AddCueDeviceItems(ToolStripItemCollection items)
        {
            foreach (var d in _snap.Devices)
            {
                var id = d.Id; var name = d.Name;
                items.Add(new ToolStripMenuItem(d.FullName + (d.IsDefault ? "  (Standard)" : ""), null, (s, e) => SetCueDevice(id, name))
                { Checked = string.Equals(_settings.CueDevice, id, StringComparison.OrdinalIgnoreCase) });
            }
            if (_snap.Devices.Length == 0) items.Add(Hint("Kein Ausgabegerät gefunden"));
            items.Add(Hint("Am besten ein anderes Gerät als dein Ausgang (z. B. Headset)."));
        }

        /// <summary>Kanalzuege: CUE- und FX-Schildchen neu zeichnen.</summary>
        private void RefreshFxPills()
        {
            ChannelStrip.FxShown = !_settings.FxOff;
            ChannelStrip.CueShown = CueActive;
            foreach (var s in _channelStrips) s.InvalidateChrome();
        }

        // ================================================================ Ausgangs-EQ und Raumklang

        private OutputFxConfig OutFxConfig(string deviceId, bool create, string name = null)
        {
            if (deviceId == null) return null;
            var o = _settings.OutputFx?.FirstOrDefault(x => string.Equals(x.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (o == null && create)
            {
                if (_settings.OutputFx == null) _settings.OutputFx = new List<OutputFxConfig>();
                o = new OutputFxConfig { DeviceId = deviceId, Name = name };
                _settings.OutputFx.Add(o);
            }
            if (o != null && name != null) o.Name = name;
            return o;
        }

        internal static bool OutFxIsActive(OutputFxConfig o) => o != null && ((o.EqOn && !GraphicEq.IsFlat(o.Geq)) || o.Space > 0 || o.Mono || o.Swap || (BassMonoGlobal && o.BassMonoHz > 0) || Peq400Active(o));   // 4.0: PEQ, Profil, Hoer-EQ
        private bool OutFxActive(string deviceId) => !_settings.OutFxOff && OutFxIsActive(OutFxConfig(deviceId, false));

        private readonly Dictionary<string, OutputFx> _outFxSent = new Dictionary<string, OutputFx>(StringComparer.OrdinalIgnoreCase);
        private string _outFxDefault;

        /// <summary>Ausgangs-Klang an den Mischer: je Geraet, dazu "" fuer das Standardgeraet.</summary>
        internal static Dictionary<string, OutputFx> BuildOutputFx(MixerSettings s, string defaultId, Dictionary<string, OutputFx> reuse)
        {
            var d = new Dictionary<string, OutputFx>(StringComparer.OrdinalIgnoreCase);
            if (s.OutFxOff || s.OutputFx == null) return d;
            foreach (var o in s.OutputFx)
            {
                if (!OutFxIsActive(o)) continue;
                var fx = new OutputFx(o.EqOn ? o.Geq : null, o.Space, o.Mono, o.Swap, s.BassMonoOn ? o.BassMonoHz : 0, OutputPeq400(s, o));   // 3.2: mono/getauscht; 3.5: Bass mono; 4.0: PEQ/Profil/Hoer-EQ
                if (reuse != null && reuse.TryGetValue(o.DeviceId, out var old) && old.SameAs(fx)) fx = old;
                d[o.DeviceId] = fx;
                if (defaultId != null && string.Equals(o.DeviceId, defaultId, StringComparison.OrdinalIgnoreCase)) d[""] = fx;
            }
            return d;
        }

        private void PushOutputFx()
        {
            var d = BuildOutputFx(_settings, DefaultDevice?.Id, _outFxSent);
            bool same = d.Count == _outFxSent.Count && d.All(kv => _outFxSent.TryGetValue(kv.Key, out var o) && ReferenceEquals(o, kv.Value));
            _outFxDefault = DefaultDevice?.Id;
            if (same) return;
            _outFxSent.Clear();
            foreach (var kv in d) _outFxSent[kv.Key] = kv.Value;
            Backend.SetOutputFx(d.Count == 0 ? null : d);
        }

        private void SetSpace(string deviceId, string name, int space)
        {
            if (space > 0 && !_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "Raumklang nicht verfügbar") + "\n\nRaumklang braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            var o = OutFxConfig(deviceId, true, name);
            o.Space = Math.Max(0, Math.Min(4, space));
            if (space > 0) _settings.OutFxOff = false;
            PushPolicy();
            ScheduleSave();
            ShowOsd("Raumklang · " + (name ?? "Ausgang"), OutputFx.SpaceNames[o.Space], -1, CueColor);
        }

        /// <summary>Grosses EQ-Fenster fuer einen Ausgang (10 Baender).</summary>
        public void ShowOutputEq(string deviceId, string name)
        {
            if (deviceId == null) return;
            var o = OutFxConfig(deviceId, true, name);
            if (o.Geq == null || o.Geq.Length != 10) o.Geq = new float[10];
            var view = new ChannelConfig
            {
                Id = OutEqPrefix + deviceId, Name = "AUSGANG · " + (name ?? "Gerät").ToUpperInvariant(), Color = CueColor.ToArgb(),
                Apps = new List<string>(), Geq = o.Geq, EqOn = o.EqOn, Fader = 1f,
            };
            if (_eqWin == null || _eqWin.IsDisposed) { _eqWin = new EqWindow(this, view); _eqWin.Show(); }
            else { _eqWin.SetChannel(view); _eqWin.Activate(); }
            Backend.SetSpectrumChannel(null);
            _settings.OutFxOff = false;
        }

        public const string OutEqPrefix = "out:";

        /// <summary>Aus dem EQ-Fenster: war es ein Ausgang? Dann uebernehmen (true).</summary>
        private bool OutputEqEdited(ChannelConfig c)
        {
            if (c?.Id == null || !c.Id.StartsWith(OutEqPrefix)) return false;
            var o = OutFxConfig(c.Id.Substring(OutEqPrefix.Length), true);
            o.EqOn = c.EqOn;
            o.Geq = c.Geq;
            PushPolicy();
            ScheduleSave();
            return true;
        }

        /// <summary>Menue eines Ausgangs: Ausgangs-EQ und Raumklang.</summary>
        private void AddOutputFxItems(ToolStripItemCollection items, string deviceId, string name)
        {
            if (deviceId == null || _settings.OutFxOff) return;
            var o = OutFxConfig(deviceId, false);
            items.Add(new ToolStripMenuItem("Ausgangs-EQ (10 Bänder) …" + (o != null && o.EqOn && !GraphicEq.IsFlat(o.Geq) ? "  ●" : ""), null,
                (s, e) => BeginInvoke(new Action(() => ShowOutputEq(deviceId, name)))));
            var sp = Sub("Kopfhörer-Raumklang: " + OutputFx.SpaceNames[o?.Space ?? 0]);
            for (int i = 0; i < OutputFx.SpaceNames.Length; i++)
            {
                int k = i;
                sp.DropDownItems.Add(new ToolStripMenuItem(OutputFx.SpaceNames[i], null, (s, e) => SetSpace(deviceId, name, k)) { Checked = (o?.Space ?? 0) == i });
            }
            sp.DropDownItems.Add(Hint("Für Kopfhörer: Klang kommt von vorne statt aus dem Kopf."));
            items.Add(sp);
            AddOutputMono320(items, deviceId, name);   // 3.2: mono, links/rechts tauschen
            AddOutput350Items(items, deviceId, name);  // 3.5: Bass mono
            AddOutput400Items(items, deviceId, name);  // 4.0: PEQ, Kopfhoerer-Profil
        }

        // ================================================================ Mehrspur-Aufnahme

        private List<WavWriter> _recTracks;

        private void StartMultitrackRecording()
        {
            if (!_snap.EqSupported && !_demo) { MessageBox.Show(this, "Mehrspur-Aufnahme braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            string folder = Path.Combine(_settings.RecordFolder, "Mischpult " + stamp + " Mehrspur");
            var tracks = new Dictionary<string, WavWriter>();
            var list = new List<WavWriter>();
            WavWriter master;
            try
            {
                master = new WavWriter(folder, "00 ALLES", DspMixer.Rate);
                int nr = 1;
                foreach (var c in _settings.Channels.Where(x => !x.Hidden))
                {
                    var w = new WavWriter(folder, (nr++).ToString("00") + " " + SafeName(c.Name), DspMixer.Rate);
                    tracks[c.Id] = w;
                    list.Add(w);
                }
            }
            catch (Exception ex)
            {
                foreach (var w in list) try { w.Dispose(); } catch { }
                MessageBox.Show(this, "Die Aufnahme konnte nicht gestartet werden:\n" + ex.Message + "\n\nOrdner: " + folder, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _multiRec = true;
            PushPolicy();   // alle Kanaele durchs Mischpult
            string err = Backend.StartRecording(null, null, master) ?? Backend.StartMultitrack(tracks);
            if (err != null)
            {
                try { Backend.StopRecording(); Backend.StopMultitrack(); } catch { }
                _multiRec = false;
                PushPolicy();
                master.Dispose();
                foreach (var w in list) w.Dispose();
                try { Directory.Delete(folder, true); } catch { }
                MessageBox.Show(this, err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _recWriter = master;
            _recTracks = list;
            _recLabel = "Mehrspur";
            _top.RecOn = true;
            _top.RecText = "REC 0:00";
            _top.Invalidate();
            _nextRecCheck = 0;
            ShowOsd("Mehrspur-Aufnahme läuft", (list.Count + 1) + " Spuren", -1, Theme.MuteRed);
        }

        /// <summary>Mehrspur beenden; liefert die Spuren (oder null, wenn es keine Mehrspur-Aufnahme war).</summary>
        private List<WavWriter> StopMultitrackTracks()
        {
            var t = _recTracks;
            if (t == null) return null;
            _recTracks = null;
            try { Backend.StopMultitrack(); } catch { }
            foreach (var w in t) try { w.Dispose(); } catch { }
            _multiRec = false;
            PushPolicy();
            return t;
        }

        // ================================================================ FLAC / MP3

        private int Mp3Kbps => _settings.Mp3Kbps ?? 320;
        private volatile int _converting;

        /// <summary>Nach dem Stoppen: Meldung und ggf. im Hintergrund nach FLAC/MP3 umwandeln.</summary>
        private void FinishRecording(WavWriter main, List<WavWriter> tracks, bool osd)
        {
            var files = new List<string>(main.Files);
            if (tracks != null) foreach (var t in tracks) files.AddRange(t.Files);
            int fmt = _settings.RecFormat;
            string shown = tracks != null ? Path.GetFileName(Path.GetDirectoryName(main.CurrentFile)) : Path.GetFileName(main.CurrentFile);
            if (fmt == 0 || files.Count == 0)
            {
                if (osd) ShowOsd("Aufnahme gespeichert", shown, -1, Theme.LedGreen);
                return;
            }
            if (osd) ShowOsd("Aufnahme gespeichert", "wird in " + RecordingExport.FormatNames[fmt] + " umgewandelt …", -1, Theme.LedGreen);
            int kbps = Mp3Kbps;
            Interlocked.Increment(ref _converting);
            var th = new Thread(() =>
            {
                string firstNew = null, err = null;
                foreach (var f in files)
                {
                    string r = RecordingExport.Convert(f, fmt, kbps, out string e);
                    if (firstNew == null) firstNew = r;
                    if (e != null && err == null) err = e;
                }
                Interlocked.Decrement(ref _converting);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (tracks == null && firstNew != null) _lastRecording = firstNew;
                        if (err != null) ShowOsd("Umwandeln fehlgeschlagen – WAV bleibt", err, -1, Theme.MuteRed);
                        else if (osd) ShowOsd("Aufnahme gespeichert (" + RecordingExport.FormatNames[fmt] + ")", tracks != null ? shown : Path.GetFileName(firstNew), -1, Theme.LedGreen);
                    }));
                }
                catch { }
            }) { IsBackground = false, Name = "Mischpult Umwandeln", Priority = ThreadPriority.BelowNormal };
            th.Start();
        }

        private ToolStripMenuItem RecFormatSub()
        {
            var sub = Sub("Format: " + RecordingExport.FormatNames[_settings.RecFormat]);
            string[] hints = { "  (unverändert, groß)", "  (verlustfrei, ca. halb so groß)", "  (klein, " + Mp3Kbps + " kbit/s)" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(RecordingExport.FormatNames[i] + hints[i], null, (s, e) => { _settings.RecFormat = k; ScheduleSave(); })
                { Checked = _settings.RecFormat == i, Enabled = i != 2 || Native.Win32.IsWindows || _demo });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            foreach (var kb in Mp3Encoder.Kbps)
            {
                int k = kb;
                sub.DropDownItems.Add(new ToolStripMenuItem("MP3 mit " + kb + " kbit/s", null, (s, e) => { _settings.Mp3Kbps = k; ScheduleSave(); })
                { Checked = Mp3Kbps == kb, Enabled = _settings.RecFormat == 2 });
            }
            sub.DropDownItems.Add(Hint("Umgewandelt wird nach dem Stoppen."));
            return sub;
        }

        // ================================================================ Einbindung

        private void ApplyFeatures13Statics()
        {
            ChannelStrip.FxShown = !_settings.FxOff;
            ChannelStrip.CueShown = _settings.CueOn && _settings.CueDevice != null;
        }

        private double _nextDefCheck13;

        private void TickFeatures13(double now)
        {
            if (now < _nextDefCheck13) return;
            _nextDefCheck13 = now + 1.0;
            // Standardgeraet gewechselt (z. B. Headset eingesteckt): Ausgangs-Klang folgt dem Geraet
            if (_settings.OutputFx != null && !_settings.OutFxOff && !string.Equals(DefaultDevice?.Id, _outFxDefault, StringComparison.OrdinalIgnoreCase))
                PushPolicy();
        }

        private void SetFxOn(bool on)
        {
            _settings.FxOff = !on;
            PushPolicy();
            RefreshFxPills();
            ScheduleSave();
        }

        private void SetOutFxOn(bool on)
        {
            _settings.OutFxOff = !on;
            PushPolicy();
            ScheduleSave();
        }

        private void AddSoundSettings19(SPage p)
        {
            p.Section("Effekte");
            p.Toggle("Hall und Echo in den Kanälen", "Pro Kanal ein Effekt: Rechtsklick auf den Kanal → Effekt (Hall: Raum, Halle, Kathedrale – Echo: kurz, mittel, lang; Anteil 10–60 %). Ein lila FX-Schild zeigt, wo einer an ist. Der Kanal läuft dann durchs Mischpult wie beim EQ (ca. 30–50 ms später). Aus = alle Effekte aus (die Einstellungen bleiben).",
                () => !_settings.FxOff, v => SetFxOn(v)).Enabled = () => _snap.EqSupported || !_settings.FxOff || _demo;
            p.Button("Effekte der Kanäle", "Welcher Kanal welchen Effekt hat.", "Einstellen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                foreach (var c in _settings.Channels.Where(x => !x.Hidden))
                {
                    var sub = FxSub(c);
                    sub.Text = c.Name + "  –  " + (c.FxType == 0 ? "kein Effekt" : FxText(c));
                    m.Items.Add(sub);
                }
                ShowMenuAt(m, r);
            }, () => { int n = _settings.Channels.Count(c => c.FxType > 0); return n == 0 ? "keine" : n + " Kanal" + (n == 1 ? "" : "e"); })
                .Enabled = () => !_settings.FxOff;
            p.Toggle("Ausgangs-EQ und Kopfhörer-Raumklang", "Pro Ausgabegerät ein 10-Band-EQ und Raumklang für Kopfhörer (der Ton kommt von vorne statt aus dem Kopf, wie bei Lautsprechern). Einstellen per Klick auf das Gerät im MASTER bzw. Ausgang. Kanäle auf so einem Gerät laufen dann durchs Mischpult (ca. 30–50 ms später, wie beim EQ). Aus = alles aus (Einstellungen bleiben).",
                () => !_settings.OutFxOff, v => SetOutFxOn(v)).Enabled = () => _snap.EqSupported || !_settings.OutFxOff || _demo;
            p.Button("Ausgang einstellen", "EQ und Raumklang für jedes Ausgabegerät.", "Gerät wählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                foreach (var d in _snap.Devices)
                {
                    var sub = Sub(d.FullName + (d.IsDefault ? "  (Standard)" : "") + (OutFxActive(d.Id) ? "  ●" : ""));
                    AddOutputFxItems(sub.DropDownItems, d.Id, d.Name);
                    m.Items.Add(sub);
                }
                if (_snap.Devices.Length == 0) m.Items.Add(Hint("Kein Ausgabegerät gefunden"));
                ShowMenuAt(m, r);
            }, () => { int n = _settings.OutputFx?.Count(OutFxIsActive) ?? 0; return n == 0 ? "keiner" : n + " Gerät" + (n == 1 ? "" : "e"); })
                .Enabled = () => !_settings.OutFxOff;

            p.Section("Vorhören (CUE)");
            p.Toggle("Vorhören", "Kanäle mit CUE laufen zusätzlich – vor dem Fader, also auch leise gedreht oder stumm – auf einem eigenen Gerät, z. B. dem Headset. Ein- und ausschalten: Rechtsklick auf den Kanal → Vorhören. Türkises CUE-Schild am Kanal.",
                () => _settings.CueOn, v => SetCueOn(v));
            p.Button("Gerät zum Vorhören", "Am besten ein anderes Gerät als dein Ausgang.", "Wählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                AddCueDeviceItems(m.Items);
                ShowMenuAt(m, r);
            }, () => _settings.CueDeviceName ?? "keins").Enabled = () => _settings.CueOn;
            p.Slider("Lautstärke beim Vorhören", "Nur auf dem Gerät zum Vorhören.", -30f, 6f, 1f, () => CueDb, (v, fin) => SetCueDb(v, fin),
                v => (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs((int)v) + " dB").Enabled = () => _settings.CueOn;

            p.Section("Aufnahme");
            p.Choice("Format", () => _settings.RecFormat == 0 ? "WAV: unverändert, aber groß (ca. 11 MB pro Minute)."
                    : _settings.RecFormat == 1 ? "FLAC: verlustfrei, ungefähr halb so groß wie WAV. Wird nach dem Stoppen umgewandelt."
                    : "MP3 mit dem Kodierer von Windows – klein, aber nicht verlustfrei. Wird nach dem Stoppen umgewandelt; klappt es nicht, bleibt die WAV.",
                RecordingExport.FormatNames, () => _settings.RecFormat, i => { _settings.RecFormat = i; ScheduleSave(); });
            p.Choice("MP3-Qualität", () => "Höher = besser und größer.", Array.ConvertAll(Mp3Encoder.Kbps, k => k + " kbit/s"),
                () => Math.Max(0, Array.IndexOf(Mp3Encoder.Kbps, Mp3Kbps)), i => { _settings.Mp3Kbps = Mp3Encoder.Kbps[i]; ScheduleSave(); })
                .Visible = () => _settings.RecFormat == 2;
            p.Info("Mehrspur-Aufnahme", () => "Taste AUFNAHME → Rechtsklick → „Mehrspur“: jeder Kanal in einer eigenen Datei, dazu eine Spur mit allem – alle gleich lang, zum Nachbearbeiten (z. B. in Audacity). Ordner: " + _settings.RecordFolder);
        }

        /// <summary>Nur Vorschau: Effekte/CUE fuer die Bilder.</summary>
        internal static void DemoFeatures13(MixerSettings s)
        {
            string fx = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FX");   // "Nr:Typ:Preset,…"
            if (!string.IsNullOrEmpty(fx))
                foreach (var part in fx.Split(','))
                {
                    var p = part.Split(':');
                    if (p.Length >= 3 && int.TryParse(p[0], out int ci) && int.TryParse(p[1], out int t) && int.TryParse(p[2], out int pr) && ci >= 1 && ci <= s.Channels.Count)
                    { s.Channels[ci - 1].FxType = t; s.Channels[ci - 1].FxPreset = pr; }
                }
            string cue = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CUE");   // "Nr,Nr"
            if (!string.IsNullOrEmpty(cue))
            {
                s.CueOn = true; s.CueDevice = "demo-hp"; s.CueDeviceName = "Headset";
                foreach (var part in cue.Split(',')) if (int.TryParse(part, out int ci) && ci >= 1 && ci <= s.Channels.Count) s.Channels[ci - 1].Cue = true;
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPACE"), out int sp))
                s.OutputFx = new List<OutputFxConfig> { new OutputFxConfig { DeviceId = "demo-spk", Name = "Lautsprecher", Space = sp, EqOn = true, Geq = new float[] { 3, 2, 1, 0, -1, 0, 1, 2, 3, 2 } } };
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RECFORMAT"), out int rf)) s.RecFormat = rf;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MULTIREC") == "1") s.RecordTarget = "multi";
        }

        private void ShownFeatures13()
        {
            if (!_demo) return;
            string m = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FXMENU");   // Kanal-Nr: Effekt-Menue zeigen
            if (int.TryParse(m, out int ci) && ci >= 1 && ci <= _settings.Channels.Count)
                DemoLater(() =>
                {
                    var c = _settings.Channels[ci - 1];
                    var strip = _channelStrips.FirstOrDefault(s => s.Config == c);
                    var menu = NewMenu();
                    menu.ShowCheckMargin = true; menu.ShowImageMargin = false;
                    var sub = FxSub(c);
                    menu.Items.Add(Header("Effekt für »" + c.Name + "«"));
                    menu.Items.Add(new ToolStripSeparator());
                    foreach (ToolStripItem it in sub.DropDownItems.Cast<ToolStripItem>().ToList()) menu.Items.Add(it);
                    var pt = strip != null ? strip.PointToScreen(new Point(Theme.Px(30), Theme.Px(120))) : PointToScreen(new Point(200, 200));
                    menu.Show(pt);
                });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTFXMENU") == "1")
                DemoLater(() =>
                {
                    var menu = NewMenu();
                    menu.ShowCheckMargin = true; menu.ShowImageMargin = false;
                    menu.Items.Add(Header("Lautsprecher (Realtek(R) Audio)"));
                    menu.Items.Add(new ToolStripSeparator());
                    AddOutputFxItems(menu.Items, "demo-spk", "Lautsprecher");
                    var b = _master.PointToScreen(new Point(Theme.Px(10), Theme.Px(100)));
                    menu.Show(new Point(b.X - Theme.Px(200), b.Y));
                });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTEQ") == "1") DemoLater(() => ShowOutputEq("demo-spk", "Lautsprecher"));
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RECMENU") == "1") DemoLater(ShowRecordMenu);
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RECFOR"), out int secs) && secs > 0)
                DemoLater(() =>
                {
                    StartRecording();
                    var t = new System.Windows.Forms.Timer { Interval = secs * 1000 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); StopRecording(true); };
                    t.Start();
                });
        }
    }
}
