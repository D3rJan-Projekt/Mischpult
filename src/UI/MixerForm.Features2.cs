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
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>Funktionen ab Version 2.1: Stream-Mix, Gruppen, grosser EQ, Soundboard, Sprach-Ausgang, Stumm-Hinweis, Ansicht, Export/Import.</summary>
    internal sealed partial class MixerForm
    {
        private readonly List<GroupStrip> _groupStrips = new List<GroupStrip>();
        private StreamStrip _streamStrip;
        private StreamMix _appliedStream;
        private VoiceOut _appliedVoice;
        private bool _pathMuted;
        private EqWindow _eqWin;
        private SoundboardForm _sbForm;
        private readonly Dictionary<string, SoundClip> _clips = new Dictionary<string, SoundClip>();
        private readonly Dictionary<string, string> _clipErrors = new Dictionary<string, string>();
        private readonly HashSet<string> _decoding = new HashSet<string>();
        private readonly Dictionary<string, KeyValuePair<double, double>> _padPlaying = new Dictionary<string, KeyValuePair<double, double>>();
        private readonly HashSet<string> _playWhenReady = new HashSet<string>();
        private double _warnLoudSince = -1, _warnLastLoud, _warnNext;
        private bool _importing;

        public bool Narrow => _settings.Narrow;
        public bool StreamOn => _settings.StreamOn;
        public float StreamMasterDb => _settings.StreamMasterDb;
        public bool StreamMute => _settings.StreamMute;
        public float HearingCapDb => _settings.HearingCap;
        public float[] CurrentSpectrum => _snap.Spectrum;

        /// <summary>Was den Kanal gerade automatisch leiser macht (dB, positiv) – fuer die GR-Anzeige.</summary>
        public void ReductionOf(ChannelConfig c, out float comp, out float auto, out float duck, out float quiet, out float sleep)
        {
            comp = _snap.CompReduction != null && _snap.CompReduction.TryGetValue(c.Id, out float cr) && !float.IsNaN(cr) ? Math.Max(0f, cr) : 0f;
            float lg = LevelerGainOf(c.Id);
            auto = float.IsNaN(lg) ? 0f : Math.Max(0f, -lg);
            duck = (_duckApplied < 0 && _settings.DuckTargets.Contains(c.Id) && !_settings.IsDuckTrigger(c.Id) ? -_duckApplied : 0f) + ProDuckGr400(c);   // 4.0 finale: Regeln, Auto-Mix
            quiet = (_bgQuietApplied < 0 && c.Id == _settings.GameChannel ? -_bgQuietApplied : 0f) - BgAppsDbFor(c);   // 3.2: auch Hintergrund-Apps
            sleep = _sleepDb < 0 ? -_sleepDb : 0f;
        }

        public float LevelerGainOf(string channelId) =>
            _snap.LevelerGain != null && _snap.LevelerGain.TryGetValue(channelId, out var g) ? g : float.NaN;

        /// <summary>Einstellungen wurden in einem Fenster geaendert (Soundboard-Ziele usw.).</summary>
        public void SettingsChanged() => ScheduleSave();

        // ================================================================ Gruppen

        private float GroupGain(ChannelConfig c, out bool mute)
        {
            float g = 1f;
            mute = false;
            foreach (var grp in _settings.Groups)
            {
                if (!grp.Members.Contains(c.Id)) continue;
                g *= FaderLaw.PosToGain(XfadeGroupPos(grp));   // 3.2: Szenen ueberblenden
                mute |= XfadeGroupMute(grp);
            }
            return g;
        }

        public void OnGroupChanged(GroupConfig g)
        {
            PushPolicy();
            ScheduleSave();
            _groupStrips.FirstOrDefault(s => s.Config == g)?.InvalidateChrome();
            foreach (var s in _channelStrips) if (g.Members.Contains(s.Config.Id) || ChannelStrip.GroupBracketsOn) s.InvalidateChrome();   // 3.3: Klammer der Nachbarn auch
        }

        private void AddGroup(IEnumerable<string> members)
        {
            int n = _settings.Groups.Count + 1;
            var g = new GroupConfig
            {
                Id = Guid.NewGuid().ToString("N"), Name = "GRUPPE " + n, Color = MixerSettings.Palette[(n + 4) % (MixerSettings.Palette.Length - 1)],
                Members = new List<string>(members ?? new string[0]), Fader = 1f,
            };
            _settings.Groups.Add(g);
            BuildChannelStrips();
            PushPolicy();
            ScheduleSave();
            BeginInvoke(new Action(() => RenameGroup(g)));
        }

        public void RenameGroup(GroupConfig g)
        {
            using (var dlg = new InputDialog("Gruppe umbenennen", "Name der Gruppe:", g.Name))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                g.Name = dlg.Value.ToUpperInvariant();
                _groupStrips.FirstOrDefault(s => s.Config == g)?.InvalidateChrome();
                ScheduleSave();
            }
        }

        public void ShowGroupMembersMenu(GroupConfig g, Point screen)
        {
            var m = NewMenu();
            m.ShowImageMargin = true;
            m.ShowCheckMargin = true;
            m.Items.Add(Header("Kanäle in »" + g.Name + "«"));
            m.Items.Add(new ToolStripSeparator());
            foreach (var c in _settings.Channels)
            {
                var ch = c;
                var it = new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color))) { Checked = g.Members.Contains(c.Id) };
                it.Click += (s, e) =>
                {
                    _keepMenuOpen = true;
                    if (g.Members.Contains(ch.Id)) g.Members.Remove(ch.Id); else g.Members.Add(ch.Id);
                    it.Checked = g.Members.Contains(ch.Id);
                    OnGroupChanged(g);
                };
                m.Items.Add(it);
            }
            m.Show(screen);
        }

        public void ShowGroupMenu(GroupStrip strip, Point screen)
        {
            var g = strip.Config;
            var m = NewMenu();
            m.Items.Add(Header(g.Name + "  (Gruppe)"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Umbenennen", null, (s, e) => BeginInvoke(new Action(() => RenameGroup(g)))));
            m.Items.Add(new ToolStripMenuItem("Kanäle wählen …", null, (s, e) => BeginInvoke(new Action(() => ShowGroupMembersMenu(g, screen)))));
            AddFaderItems(m.Items, () => g.FaderLock, v => g.FaderLock = v, () => g.FaderMark, v => g.FaderMark = v, () => g.Fader,
                pos => { g.Fader = pos; OnGroupChanged(g); }, strip);
            var colors = Sub("Farbe", false, Swatch(Color.FromArgb(g.Color)));
            for (int i = 0; i < MixerSettings.Palette.Length; i++)
            {
                int col = MixerSettings.Palette[i];
                colors.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.PaletteNames[i], Swatch(Color.FromArgb(col)), (s, e) =>
                {
                    g.Color = col; strip.InvalidateChrome(); ScheduleSave();
                }) { Checked = g.Color == col });
            }
            m.Items.Add(colors);
            m.Items.Add(new ToolStripMenuItem("Fader auf 0 dB, Mute aus", null, (s, e) => { g.Fader = 1f; g.Mute = false; OnGroupChanged(g); }));
            AddGroupMenu400(m.Items, g, strip);   // 4.0: EQ/Kompressor der Gruppe
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Gruppe löschen (Kanäle bleiben)", null, (s, e) =>
            {
                _settings.Groups.Remove(g);
                _settings.Hotkeys.RemoveAll(h => h.Action.StartsWith("grp:" + g.Id + ":"));
                _settings.Midi.RemoveAll(b => b.Target != null && b.Target.StartsWith("grp:" + g.Id + ":"));
                ApplyHotkeys();
                BuildChannelStrips();
                PushPolicy();
                ScheduleSave();
            }));
            m.Show(screen);
        }

        // ================================================================ Stream-Mix

        private void PushStream()
        {
            StreamMix mix = null;
            if (_settings.StreamOn && _settings.StreamDevice != null)
            {
                var sends = new Dictionary<string, float>();
                foreach (var c in _settings.Channels)
                {
                    if (c.StreamOff || c.StreamDb <= ChannelStrip.StreamOffDb) continue;
                    sends[c.Id] = (float)Math.Pow(10, c.StreamDb / 20.0);
                }
                float gain = _settings.StreamMasterDb <= -60f ? 0f : (float)Math.Pow(10, _settings.StreamMasterDb / 20.0);
                mix = new StreamMix(_settings.StreamDevice, gain, _settings.StreamMute, sends);
            }
            if (mix == null ? _appliedStream == null : mix.SameAs(_appliedStream)) return;
            _appliedStream = mix;
            Backend.SetStreamMix(mix);
        }

        public void SetStreamMaster(float db)
        {
            _settings.StreamMasterDb = Math.Max(-60f, Math.Min(0f, db));
            PushStream();
            _streamStrip?.InvalidateChrome();
            ScheduleSave();
        }

        public void ToggleStreamMute()
        {
            _settings.StreamMute = !_settings.StreamMute;
            PushStream();
            _streamStrip?.InvalidateChrome();
            ScheduleSave();
        }

        private void SetStreamOn(bool on)
        {
            if (on && !_snap.EqSupported && !_demo)
            {
                MessageBox.Show(this, _snap.EqStatus ?? "Der Stream-Mix braucht Windows 10 Version 2004 oder neuer.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.StreamOn = on;
            if (on && _settings.StreamDevice == null)
            {
                // Vorschlag: virtuelles Kabel
                var cable = _snap.Devices.FirstOrDefault(d => (d.FullName ?? "").IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0 && !d.IsDefault);
                if (cable != null) { _settings.StreamDevice = cable.Id; _settings.StreamDeviceName = cable.FullName; }
            }
            foreach (var s in _channelStrips) s.Relayout();
            SyncOutputs();
            PushStream();
            ScheduleSave();
            if (on && _settings.StreamDevice == null)
                BeginInvoke(new Action(() => ShowStreamDeviceMenu(_streamStrip != null ? _streamStrip.PointToScreen(new Point(Theme.Px(8), Theme.Px(112))) : Cursor.Position)));
        }

        public void ShowStreamDeviceMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Stream-Mix ausgeben auf"));
            m.Items.Add(new ToolStripSeparator());
            foreach (var d in _snap.Devices)
            {
                var dev = d;
                bool cable = (d.FullName ?? "").IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0 || (d.FullName ?? "").IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0;
                m.Items.Add(new ToolStripMenuItem(d.FullName + (cable ? "   (virtuelles Kabel)" : d.IsDefault ? "   (dein Standardgerät!)" : ""), null, (s, e) =>
                {
                    _settings.StreamDevice = dev.Id;
                    _settings.StreamDeviceName = dev.FullName;
                    PushStream();
                    SyncOutputs();
                    ScheduleSave();
                }) { Checked = string.Equals(_settings.StreamDevice, d.Id, StringComparison.OrdinalIgnoreCase) });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Hint("In OBS: Audio-Eingabe »CABLE Output« (bei VB-Cable)."));
            m.Items.Add(Hint("Das Standardgerät hörst du sonst doppelt."));
            m.Items.Add(new ToolStripMenuItem("Stream-Mix ausschalten", null, (s, e) => SetStreamOn(false)));
            m.Show(screen);
        }

        private void UpdateStreamStrip()
        {
            if (_streamStrip == null) return;
            var dev = _settings.StreamDevice == null ? null : _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, _settings.StreamDevice, StringComparison.OrdinalIgnoreCase));
            string name = _settings.StreamDevice == null ? null : dev?.Name ?? _settings.StreamDeviceName ?? "Gerät";
            string sub = dev?.FullName;
            if (sub != null) { int p = sub.IndexOf('('); if (p > 0 && sub.EndsWith(")")) sub = sub.Substring(p + 1, sub.Length - p - 2); }
            var list = _settings.Channels.Where(c => !c.StreamOff && c.StreamDb > ChannelStrip.StreamOffDb)
                .Select(c => new KeyValuePair<string, Color>(c.Name + (Math.Abs(c.StreamDb) > 0.05f ? "  " + (c.StreamDb > 0 ? "+" : "") + c.StreamDb.ToString("0", CultureInfo.InvariantCulture) : ""), Color.FromArgb(c.Color))).ToList();
            _streamStrip.SetState(name, sub, _settings.StreamDevice != null && dev == null, list);
        }

        // ================================================================ Gehoerschutz-Menue (Ausgangszug)

        public void ShowHearingMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Gehörschutz"));
            m.Items.Add(Hint("Fängt plötzlich laute Geräusche ab."));
            m.Items.Add(new ToolStripSeparator());
            foreach (var kv in HearingLevels)
            {
                float db = kv.Key;
                m.Items.Add(new ToolStripMenuItem(kv.Value, null, (s, e) => { SetHearingCap(db); _master.InvalidateChrome(); foreach (var o in _outputStrips.Values) o.InvalidateChrome(); })
                { Checked = Math.Abs(_settings.HearingCap - db) < 0.1f });
            }
            m.Show(screen);
        }

        // ================================================================ VU-Meter: ab wann der Zeiger reagiert

        private static readonly KeyValuePair<float, string>[] VuStartPresets =
        {
            new KeyValuePair<float, string>(-28f, "normal"),
            new KeyValuePair<float, string>(-36f, "etwas empfindlicher"),
            new KeyValuePair<float, string>(-44f, "empfindlich"),
            new KeyValuePair<float, string>(-52f, "sehr empfindlich"),
            new KeyValuePair<float, string>(-60f, "ganz empfindlich"),
        };

        private void SetVuStart(float db, bool save = true)
        {
            db = (float)Math.Round(Math.Max(MixerSettings.VuStartMin, Math.Min(MixerSettings.VuStartMax, db)));
            _vuCalNote = null;
            _settings.VuStartDb = db;
            StripBase.VuStartDb = db;
            if (Theme.VuDbScale) RefreshVuStrips();   // Zahlen auf der Skala passen sich an
            if (save) ScheduleSave();
        }

        /// <summary>Zahlen auf der VU-Skala: dB passend zur Empfindlichkeit oder klassisch (20 … 0 … +3).</summary>
        private void SetVuScale(bool db)
        {
            _settings.VuScaleVu = !db;
            Theme.VuDbScale = db;
            RefreshVuStrips();
            ScheduleSave();
        }

        /// <summary>Nur die Zuege mit VU-Meter neu zeichnen (Ausgaenge).</summary>
        private void RefreshVuStrips()
        {
            _master?.InvalidateChrome();
            foreach (var o in _outputStrips.Values) o.InvalidateChrome();
            if (VuWindowOpen) _vuWin.Restyle();
        }

        /// <summary>Lautester mittlerer Pegel der Ausgaenge mit VU-Meter (letzte Sekunden), NaN wenn gerade nichts kommt.</summary>
        private float VuMeanNow()
        {
            float best = float.NaN;
            foreach (var s in AllStrips())
            {
                if (s == null) continue;
                float m = s.VuMeanDb;
                if (!float.IsNaN(m) && (float.IsNaN(best) || m > best)) best = m;
            }
            return best;
        }

        private string _vuCalNote;

        /// <summary>Empfindlichkeit so setzen, dass der Ton, der gerade laeuft, um 0 pendelt. Gibt eine kurze Meldung zurueck.</summary>
        private string CalibrateVu(out bool ok)
        {
            float m = VuMeanNow();
            ok = !float.IsNaN(m);
            if (!ok) return _vuCalNote = "kein Ton – erst etwas abspielen";
            float want = Theme.VuDbScale ? m * 2.07f : m - 16f, v = StripBase.VuStartFor(m);
            SetVuStart(v);
            string edge = want < MixerSettings.VuStartMin - 0.5f ? " (empfindlichste Stufe)"
                        : want > MixerSettings.VuStartMax + 0.5f ? " (unempfindlichste Stufe)" : "";
            return _vuCalNote = "reagiert jetzt ab " + StripBase.DbText(v) + edge;
        }

        /// <summary>Stufen + "Auf jetzigen Ton einstellen" – fuer Klick aufs VU-Meter und das OPTIONEN-Menue.</summary>
        private void AddVuStartItems(ToolStripItemCollection items)
        {
            float cur = StripBase.VuStartDb;
            bool any = false;
            foreach (var kv in VuStartPresets)
            {
                float v = kv.Key;
                bool on = Math.Abs(cur - v) < 0.5f;
                any |= on;
                items.Add(new ToolStripMenuItem(kv.Value + "  (ab " + StripBase.DbText(v) + ")" + (v == MixerSettings.VuStartDefault ? "  – Standard" : ""), null,
                    (s, e) => SetVuStart(v)) { Checked = on });
            }
            if (!any) items.Add(new ToolStripMenuItem("eigene  (ab " + StripBase.DbText(cur) + ")") { Checked = true });
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Auf jetzigen Ton einstellen", null, (s, e) =>
            {
                string note = CalibrateVu(out bool ok);
                ShowOsd("VU-Meter", note, -1, ok ? Theme.Accent : Theme.TextDim);
            }) { ToolTipText = "Spiel etwas ab, das du normal hörst, und klick hier:\nder Zeiger steht danach gut sichtbar etwa in der Mitte – auch wenn es leise ist" });
            items.Add(new ToolStripMenuItem("Skala in dB (passend zur Einstellung)", null, (s, e) => SetVuScale(!Theme.VuDbScale))
            { Checked = Theme.VuDbScale, ToolTipText = "An: die Skala geht von „reagiert ab“ bis 0 dB, z. B. −60 … 0\nAus: klassisch wie am Studio-VU-Meter (20 … 0 … +3, nur 23 dB breit)" });
            items.Add(new ToolStripMenuItem("Genauer einstellen …", null, (s, e) => ShowSettings("Leuchten & Anzeigen")));
            if (!VuWindowOpen) items.Add(new ToolStripMenuItem("Großes VU-Meter im Fenster …", null, (s, e) => ShowVuWindow()));
        }

        /// <summary>Klick aufs VU-Meter: einstellen, ab wann der Zeiger reagiert.</summary>
        public void ShowVuMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("VU-Meter reagiert ab"));
            m.Items.Add(Hint("Weiter unten = auch leiser Ton bewegt den Zeiger."));
            m.Items.Add(new ToolStripSeparator());
            AddVuStartItems(m.Items);
            m.Show(screen);
        }

        // ================================================================ Grosser EQ

        public void ShowBigEq(ChannelConfig c)
        {
            if (_eqWin == null || _eqWin.IsDisposed)
            {
                _eqWin = new EqWindow(this, c);
                _eqWin.Show();
            }
            else
            {
                _eqWin.SetChannel(c);
                _eqWin.Activate();
            }
            Backend.SetSpectrumChannel(c.Id);
            if (!c.EqOn && _snap.EqSupported)
                ShowOsd("EQ · " + c.Name, "Für das Spektrum EQ einschalten", -1, Theme.TextDim);
        }

        public void OnBigEqClosed()
        {
            Backend.SetSpectrumChannel(null);
            _eqWin = null;
        }

        public void SetGraphicBand(ChannelConfig c, int band, float db, bool push = true)
        {
            if (band < 0 || band >= 10) return;
            if (c.Geq == null || c.Geq.Length != 10) c.Geq = new float[10];
            c.Geq[band] = Math.Max(-MixerSettings.EqRange, Math.Min(MixerSettings.EqRange, db));
            if (Math.Abs(db) > 0.01f && !c.EqOn && _snap.EqSupported) c.EqOn = true;
            if (push) OnChannelEdited(c);
        }

        public void OnChannelEdited(ChannelConfig c)
        {
            if (OutputEqEdited(c)) return;   // 2.19: Ausgangs-EQ (im grossen EQ-Fenster)
            OnChannelChanged(c, false);
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
        }

        public void ToggleChannelEq(ChannelConfig c)
        {
            if (!c.EqOn && !_snap.EqSupported) { MessageBox.Show(this, _snap.EqStatus ?? "EQ nicht verfügbar", "Mischpult"); return; }
            c.EqOn = !c.EqOn;
            OnChannelEdited(c);
        }

        // ================================================================ Soundboard

        public void ShowSoundboard()
        {
            if (_sbForm == null || _sbForm.IsDisposed) _sbForm = new SoundboardForm(this, _settings);
            if (!_sbForm.Visible) _sbForm.Show();
            if (_sbForm.WindowState == FormWindowState.Minimized) _sbForm.WindowState = FormWindowState.Normal;
            _sbForm.Activate();
        }

        public bool AnyPadPlaying => _padPlaying.Count > 0;

        public double PadProgress(string id)
        {
            if (!_padPlaying.TryGetValue(id, out var t)) return -1;
            double now = Now;
            return t.Value <= t.Key ? 0 : (now - t.Key) / (t.Value - t.Key);
        }

        public string PadError(string id)
        {
            if (_clipErrors.TryGetValue(id, out var e)) return e;
            return _decoding.Contains(id) ? null : null;
        }

        public string PadHotkey(string id) => HotkeyManager.Format(HotkeyKeys("sb:" + id));

        public void AddSounds(IEnumerable<string> files)
        {
            int added = 0;
            foreach (var f in files)
            {
                if (_settings.Sounds.Any(p => string.Equals(p.File, f, StringComparison.OrdinalIgnoreCase))) continue;
                var pad = new SoundPad { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileNameWithoutExtension(f), File = f, Volume = 0.8f };
                _settings.Sounds.Add(pad);
                Decode(pad);
                added++;
            }
            if (added > 0) ScheduleSave();
            _sbForm?.Invalidate();
        }

        /// <summary>Datei im Hintergrund laden (MTA-Thread, Media Foundation).</summary>
        private void Decode(SoundPad pad)
        {
            if (_decoding.Contains(pad.Id)) return;
            _decoding.Add(pad.Id);
            _clipErrors.Remove(pad.Id);
            string id = pad.Id, file = pad.File, name = pad.Name;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SoundClip clip = null;
                string err = null;
                try { clip = SoundDecoder.Load(file, name); }
                catch (FileNotFoundException) { err = "Datei fehlt"; }
                catch (Exception ex) { err = "Fehler: " + ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _decoding.Remove(id);
                        var cur = _settings.Sounds.FirstOrDefault(x => x.Id == id);
                        if (cur == null) { _playWhenReady.Remove(id); return; }            // inzwischen entfernt
                        if (!string.Equals(cur.File, file, StringComparison.OrdinalIgnoreCase)) // inzwischen andere Datei
                        {
                            Decode(cur);
                            return;
                        }
                        if (clip != null) _clips[id] = clip; else _clipErrors[id] = err;
                        if (clip != null && _playWhenReady.Remove(id)) PlayPad(cur);
                        _sbForm?.Invalidate();
                    }));
                }
                catch { }
            });
        }

        public void PlayPad(SoundPad p)
        {
            if (!_snap.EqSupported && !_demo)
            {
                ShowOsd("Soundboard", "braucht Windows 10 Version 2004 oder neuer", -1, Theme.SoloAmber);
                return;
            }
            if (!_clips.TryGetValue(p.Id, out var clip))
            {
                _playWhenReady.Add(p.Id);
                if (!_decoding.Contains(p.Id)) Decode(p);
                return;
            }
            bool stream = _settings.StreamOn && _settings.StreamDevice != null && !_settings.SbNotStream;
            bool voice = _settings.VoiceOn && _settings.VoiceDevice != null && !_settings.SbNotVoice;
            bool me = !_settings.SbNotMe;
            if (!me && !stream && !voice) { ShowOsd("Soundboard", "Kein Ziel gewählt", -1, Theme.SoloAmber); return; }
            Backend.PlaySound(clip, FaderLaw.PosToGain(p.Volume), me, stream, voice);
            double now = Now;
            _padPlaying[p.Id] = new KeyValuePair<double, double>(now, now + clip.Seconds);
            _sbForm?.Invalidate();
        }

        public void StopAllSounds()
        {
            Backend.StopSounds();
            _padPlaying.Clear();
            _playWhenReady.Clear();
            _sbForm?.Invalidate();
        }

        public void ShowPadMenu(SoundPad p, Point screen, Form owner)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header(p.Name));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Abspielen", null, (s, e) => PlayPad(p)));
            m.Items.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Sound umbenennen", "Name:", p.Name))
                    if (dlg.ShowDialog(owner) == DialogResult.OK) { p.Name = dlg.Value; ScheduleSave(); owner.Invalidate(); }
            }))));
            var vol = Sub("Lautstärke");
            foreach (int pc in new[] { 100, 80, 60, 40, 20 })
            {
                float v = pc / 100f;
                vol.DropDownItems.Add(new ToolStripMenuItem(pc + " %", null, (s, e) => { p.Volume = v; ScheduleSave(); owner.Invalidate(); })
                { Checked = Math.Abs(p.Volume - v) < 0.01f });
            }
            m.Items.Add(vol);
            var colors = Sub("Farbe", false);
            for (int i = 0; i < MixerSettings.Palette.Length; i++)
            {
                int col = MixerSettings.Palette[i];
                colors.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.PaletteNames[i], Swatch(Color.FromArgb(col)), (s, e) => { p.Color = col; ScheduleSave(); owner.Invalidate(); }));
            }
            m.Items.Add(colors);
            m.Items.Add(new ToolStripMenuItem("Hotkey festlegen …", null, (s, e) => BeginInvoke(new Action(ShowHotkeyDialog))));
            m.Items.Add(new ToolStripMenuItem("Andere Datei wählen …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Audiodateien|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.mp4|Alle Dateien|*.*" })
                    if (dlg.ShowDialog(owner) == DialogResult.OK) { p.File = dlg.FileName; _clips.Remove(p.Id); Decode(p); ScheduleSave(); }
            }))));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Entfernen", null, (s, e) =>
            {
                _settings.Sounds.Remove(p);
                _clips.Remove(p.Id);
                _settings.Hotkeys.RemoveAll(h => h.Action == "sb:" + p.Id);
                _settings.Midi.RemoveAll(b => b.Target == "sb:" + p.Id);
                ApplyHotkeys();
                ScheduleSave();
                owner.Invalidate();
            }));
            m.Show(screen);
        }

        private void UpdateSounds(double now)
        {
            if (_padPlaying.Count == 0) return;
            foreach (var id in _padPlaying.Where(kv => kv.Value.Value < now).Select(kv => kv.Key).ToList()) _padPlaying.Remove(id);
        }

        // ================================================================ Sprach-Ausgang + Stumm-Hinweis

        /// <summary>Stummschaltung wirkt nur im Sprach-Ausgang (Windows-Mikro bleibt an).</summary>
        private bool PathMute => _settings.VoiceOn && _settings.VoiceMuteInPath && _settings.VoiceDevice != null;

        private void PushVoice()
        {
            VoiceOut v = null;
            if (_settings.VoiceOn && _settings.VoiceDevice != null)
                v = new VoiceOut(_settings.VoiceDevice, _settings.VoiceGateOn, _settings.VoiceGateDb, PathMute && _pathMuted, CurrentVoiceFx());
            if (v == null ? _appliedVoice == null : v.SameAs(_appliedVoice)) return;
            _appliedVoice = v;
            Backend.SetVoiceOut(v);
        }

        private VoiceFx _fxPreview;   // waehrend der Dialog offen ist

        private VoiceFx CurrentVoiceFx() => _fxPreview ?? new VoiceFx(_settings.VoiceFxHp, _settings.VoiceFxClear,
            _settings.VoiceFxLow, _settings.VoiceFxMid, _settings.VoiceFxHigh, _settings.VoiceFxComp, _settings.VoiceFxGain);

        public void ShowMicFxDialog()
        {
            var old = CurrentVoiceFx();
            Func<float> level = () => _snap.VoiceActive ? _snap.VoiceOutPeak : 0f;
            bool voiceOn = _settings.VoiceOn && _settings.VoiceDevice != null;
            using (var dlg = new MicFxDialog(old, voiceOn, level, fx => { _fxPreview = fx; PushVoice(); }))
            {
                var owner = _sbForm != null && _sbForm.Visible && _sbForm.ContainsFocus ? (IWin32Window)_sbForm : this;
                var res = dlg.ShowDialog(owner);
                _fxPreview = null;
                if (res == DialogResult.OK || res == DialogResult.Retry)
                {
                    var fx = dlg.Result;
                    _settings.VoiceFxHp = fx.HighPass; _settings.VoiceFxClear = fx.Clarity;
                    _settings.VoiceFxLow = fx.Low; _settings.VoiceFxMid = fx.Mid; _settings.VoiceFxHigh = fx.High;
                    _settings.VoiceFxComp = fx.Comp; _settings.VoiceFxGain = fx.GainDb;
                    ScheduleSave();
                }
                PushVoice();
                _mic.InvalidateChrome();
                if (res == DialogResult.Retry) BeginInvoke(new Action(ShowVoiceDialog)); // "Sprach-Ausgang einrichten"
            }
        }

        /// <summary>Mikro stumm/an – je nach Einstellung in Windows oder nur im Sprach-Ausgang.</summary>
        private void ApplyMicMute(bool mute)
        {
            if (PathMute) SetPathMuted(mute);
            else Backend.SetMicMute(mute);
        }

        /// <summary>Stumm im Sprach-Ausgang setzen; von Hand geschaltet wird es gespeichert (PTT nicht).</summary>
        private void SetPathMuted(bool mute)
        {
            _pathMuted = mute;
            PushVoice();
            bool keep = _settings.PttMode == 0 && mute;
            if (_settings.VoicePathMuted != keep) { _settings.VoicePathMuted = keep; ScheduleSave(); }
        }

        public void ShowVoiceDialog()
        {
            Func<float> level = () => _snap.VoiceActive ? _snap.VoicePeak : Math.Max(_snap.Mic?.PeakL ?? 0f, _snap.Mic?.PeakR ?? 0f);
            using (var dlg = new VoiceDialog(_settings, _snap.Devices, level, _snap.EqSupported || _demo))
            {
                if (dlg.ShowDialog(_sbForm != null && _sbForm.Visible && _sbForm.ContainsFocus ? (IWin32Window)_sbForm : this) != DialogResult.OK) return;
                bool wasPath = PathMute;
                _settings.VoiceOn = dlg.VoiceOn;
                _settings.VoiceDevice = dlg.Device;
                _settings.VoiceDeviceName = dlg.DeviceName;
                _settings.VoiceGateOn = dlg.GateOn;
                _settings.VoiceGateDb = dlg.GateDb;
                _settings.VoiceMuteInPath = dlg.PathMute;
                _settings.MuteWarnOff = !dlg.MuteWarn;
                if (wasPath != PathMute)
                {
                    // Stummschaltung wandert zwischen Windows und Sprach-Ausgang – ohne Luecke,
                    // in der das Mikro kurz offen waere.
                    bool muted = _settings.PttMode != 0 ? (_micHardMute || _settings.PttMode == 1 || _pttApplied == 1)
                               : wasPath ? _pathMuted : (_snap.Mic?.Mute ?? false);
                    if (PathMute)
                    {
                        SetPathMuted(muted);              // erst den Sprach-Ausgang stumm ...
                        Backend.SetMicMute(false);        // ... dann Windows-Mikro wieder freigeben
                        if (_snap.Mic != null) _snap.Mic.Mute = false;
                    }
                    else
                    {
                        if (muted) { Backend.SetMicMute(true); if (_snap.Mic != null) _snap.Mic.Mute = true; }
                        SetPathMuted(false);
                    }
                    _pttApplied = -1;
                }
                PushVoice();
                ScheduleSave();
                _mic.InvalidateChrome();
                _sbForm?.Invalidate();
            }
        }

        private void UpdateMuteWarning(double now)
        {
            if (_settings.MuteWarnOff) return;
            bool muted = _settings.PttMode != 0 ? _micHardMute : PathMute ? _pathMuted : (_snap.Mic?.Mute ?? false);
            if (!muted) { _warnLoudSince = -1; return; }
            float lvl = PathMute && _snap.VoiceActive ? _snap.VoicePeak : Math.Max(_snap.Mic?.PeakL ?? 0f, _snap.Mic?.PeakR ?? 0f);
            if (lvl > 0.04f) // ca. -28 dB: Sprache
            {
                if (_warnLoudSince < 0) _warnLoudSince = now;
                _warnLastLoud = now;
                if (now - _warnLoudSince > 0.6 && now >= _warnNext)
                {
                    ShowOsd("Du bist stummgeschaltet!", "Mikro ist aus", -1, Theme.MuteRed);
                    _warnNext = now + 15;
                    _warnLoudSince = -1;
                }
            }
            else if (_warnLoudSince >= 0 && now - _warnLastLoud > 0.5) _warnLoudSince = -1;
        }

        // ================================================================ Ansicht

        private void SetNarrow(bool on)
        {
            _settings.Narrow = on;
            foreach (var s in _channelStrips) s.Relayout();
            foreach (var s in _groupStrips) s.Relayout();
            _channelHost.Relayout();
            ScheduleSave();
        }

        private void SetScheme(int scheme, int accent) => SetScheme(scheme, accent, _settings.UiStyle);

        /// <summary>Farbschema, Akzentfarbe und Design-Stil setzen und alles neu zeichnen.</summary>
        private void SetScheme(int scheme, int accent, int style)
        {
            var fadeShot = style != _settings.UiStyle ? CaptureForFade400() : null;   // 4.0 finale: weich umblenden
            _settings.ColorScheme = scheme;
            _settings.AccentColor = accent;
            _settings.UiStyle = style;
            Theme.ApplyScheme(scheme, accent, style);
            BackColor = BodyColor;
            ForeColor = Theme.Text;
            if (IsHandleCreated) DarkTitle.Apply(Handle);
            foreach (Form f in Application.OpenForms)
            {
                if (f == this || !(f is EqWindow || f is SoundboardForm || f is MiniForm || f is AppMixPopup)) continue;
                f.BackColor = Theme.MenuBg;
                f.ForeColor = Theme.Text;
                if (f.IsHandleCreated) DarkTitle.Apply(f.Handle);
                f.Invalidate(true);
            }
            foreach (var c in new Control[] { _channelHost, _outputHost, _divider }) { c.BackColor = BodyColor; c.Invalidate(); }
            foreach (var s in _channelStrips) { s.BackColor = BodyColor; s.InvalidateChrome(); }
            foreach (var s in _groupStrips) { s.BackColor = BodyColor; s.InvalidateChrome(); }
            foreach (var s in _outputStrips.Values) { s.BackColor = BodyColor; s.InvalidateChrome(); }
            _master.BackColor = BodyColor; _master.InvalidateChrome();
            _mic.BackColor = BodyColor; _mic.InvalidateChrome();
            if (_streamStrip != null) { _streamStrip.BackColor = BodyColor; _streamStrip.InvalidateChrome(); }
            if (_noiseStrip != null) { _noiseStrip.BackColor = BodyColor; _noiseStrip.InvalidateChrome(); }   // 3.5
            _addButton.BackColor = BodyColor; _addButton.Invalidate();
            _bridge.BackColor = BodyColor; _bridge.Restyle();
            ApplyCheeks();
            if (VuWindowOpen) _vuWin.Restyle();
            _top.Invalidate();
            try { _osd?.Dispose(); } catch { }
            _osd = null;
            _mini?.Invalidate();
            if (_settingsWin != null && !_settingsWin.IsDisposed) _settingsWin.ApplyTheme();
            AfterScheme400(fadeShot);   // 4.0 finale: Design-Hintergrund, Umblenden
            ScheduleSave();
        }

        /// <summary>Windows-Schrift (GDI, am schaerfsten) oder geglaettete Schrift (GDI+) – Notschalter, falls Schrift komisch aussieht.</summary>
        private void SetGdiText(bool on)
        {
            _settings.GdiTextOff = !on;
            Theme.GdiText = on && Win32.IsWindows;
            SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);   // alles neu zeichnen (speichert auch)
            RelayoutAllStrips();
        }

        /// <summary>SIG- und PEAK-Leuchte einzeln an/aus (eigene Zeile ueber den Fadern -> alle Zuege neu aufbauen).</summary>
        private void SetLeds(bool sig, bool peak)
        {
            _settings.SignalLedOff = !sig;
            _settings.PeakLedOff = !peak;
            StripBase.SignalLeds = sig;
            StripBase.PeakLeds = peak;
            RelayoutAllStrips();
            _bridge.Invalidate();
            UpdateBridgeVisibility();
            ScheduleSave();
        }

        /// <summary>Alle Zuege (Kanaele, Gruppen, Mikro, Ausgaenge, Stream).</summary>
        private IEnumerable<StripBase> AllStrips()
        {
            foreach (var x in _channelStrips) yield return x;
            foreach (var x in _groupStrips) yield return x;
            yield return _mic;
            yield return _master;
            foreach (var x in _outputStrips.Values) yield return x;
            if (_streamStrip != null) yield return _streamStrip;
            if (_noiseStrip != null && _settings.NoiseOn) yield return _noiseStrip;   // 3.5
        }

        /// <summary>Alle Zuege neu anordnen (wenn sich die Hoehe der Bereiche aendert).</summary>
        private void RelayoutAllStrips()
        {
            foreach (var s in _channelStrips) s.Relayout();
            foreach (var s in _groupStrips) s.Relayout();
            foreach (var s in _outputStrips.Values) s.Relayout();
            _master.Relayout();
            _mic.Relayout();
            _streamStrip?.Relayout();
            _noiseStrip?.Relayout();
            _channelHost.Relayout();
            _outputHost.Relayout();
            RefreshStrips();
        }

        /// <summary>Meterbridge ein/aus.</summary>
        private void SetBridge(bool on)
        {
            _settings.BridgeOff = !on;
            UpdateBridgeVisibility();
            ScheduleSave();
        }

        /// <summary>Meterbridge ist an, aber das Fenster ist zu niedrig (die Fader haetten sonst kaum Weg).</summary>
        private bool _bridgeNoRoom;

        /// <summary>Meterbridge zeigen, wenn sie an ist und die Fader trotzdem genug Weg haben (sonst vorlaeufig ausblenden).</summary>
        private void UpdateBridgeVisibility()
        {
            if (_bridge == null || _top == null || WindowState == FormWindowState.Minimized || ClientSize.Height < 50) return;
            bool want = !_settings.BridgeOff;
            _bridgeNoRoom = false;
            if (want)
            {
                // Platz fuer die Zuege, wenn die Meterbridge zu sehen ist (Titel + Scrollleiste der Bereiche: ca. 34 px)
                int strips = ClientSize.Height - _top.Height - _bridge.Height - Theme.Px(34);
                int need = StripBase.HeightFor(MultiOutput, _bridgeShown ? 110f : 125f);   // etwas Abstand, damit sie nicht flackert
                if (strips < need) { want = false; _bridgeNoRoom = true; }
            }
            // eigener Merker: Control.Visible meldet false, solange das Fenster selbst noch nicht zu sehen ist
            if (_bridgeShown != want || _bridge.Visible != want && Visible)
            {
                _bridgeShown = want;
                _bridge.Visible = want;
            }
        }

        private bool _bridgeShown = true;

        private void ShowBridgeMenu(Point screen)
        {
            var m = NewMenu();
            m.Items.Add(Header("Meterbridge"));
            m.Items.Add(new ToolStripSeparator());
            m.ShowCheckMargin = true;
            AddBridgeContextItems(m.Items, _bridge);   // 2.16.2: angeklickten Pegel ausblenden bzw. Anzeige verschieben
            m.Items.Add(BridgeStyleSub());
            m.Items.Add(BridgeBarSub());
            m.Items.Add(BridgeColumnsSub());
            if (!_settings.BridgeArrange)
                m.Items.Add(new ToolStripMenuItem("Pegel zusammenrücken", null, (s, e) => SetBridgePack(_settings.BridgePackOff))
                { Checked = !_settings.BridgePackOff, ToolTipText = "Ausgeblendete lassen keine Lücke – die Mitte wird frei für die Anzeigen" });
            AddBridgeArrangeItems(m.Items);   // 2.16.3
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Spektrum anzeigen", null, (s, e) => SetSpectrumOn(_settings.SpecOff)) { Checked = !_settings.SpecOff });
            m.Items.Add(SpecSourceSub());
            var sst = Sub("Spektrum als");
            for (int i = 0; i < SpecStyleNames.Length; i++) { int k = i; sst.DropDownItems.Add(new ToolStripMenuItem(SpecStyleNames[i], null, (s, e) => SetSpecStyle(k)) { Checked = _settings.SpecStyle == i }); }
            m.Items.Add(sst);
            var spl = Sub("Spektrum-Farben");   // 3.1
            for (int i = 0; i < SpectrumView.PaletteNames.Length; i++) { int k = i; spl.DropDownItems.Add(new ToolStripMenuItem(SpectrumView.PaletteNames[i], null, (s, e) => SetSpecPalette(k)) { Checked = _settings.SpecPalette == i }); }
            m.Items.Add(spl);
            m.Items.Add(new ToolStripMenuItem("Stereo-Bild anzeigen", null, (s, e) => SetGonio(_settings.GonioOff)) { Checked = !_settings.GonioOff });
            m.Items.Add(GonioSourceSub());
            m.Items.Add(new ToolStripMenuItem("Lautheits-Verlauf anzeigen", null, (s, e) => SetLoudHist(_settings.LoudHistOff)) { Checked = LoudHistOn });
            m.Items.Add(LoudSourceSub());
            m.Items.Add(new ToolStripMenuItem("Uhr in der Meterbridge", null, (s, e) => SetBridgeClock(!ExtraOn('c'))) { Checked = ExtraOn('c') });
            m.Items.Add(new ToolStripMenuItem("Oszilloskop anzeigen", null, (s, e) => SetOscOn(_settings.OscOff)) { Checked = !_settings.OscOff });
            m.Items.Add(new ToolStripMenuItem("„Jetzt läuft“ mit Cover anzeigen", null, (s, e) => SetCoverOn(_settings.CoverOff)) { Checked = !_settings.CoverOff });
            if (!_settings.BridgeArrange) m.Items.Add(BridgeOrderSub());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Meterbridge im eigenen Fenster …", null, (s, e) => ShowBridgeWindow()));
            m.Items.Add(new ToolStripMenuItem("Großes Spektrum im Fenster …", null, (s, e) => ShowSpectrumWindow()));
            m.Items.Add(new ToolStripMenuItem("Großes VU-Meter im Fenster …", null, (s, e) => ShowVuWindow()));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Meterbridge ausblenden", null, (s, e) => SetBridge(false)));
            m.Items.Add(Hint("Wieder einblenden: OPTIONEN → Ansicht"));
            m.Show(screen);
        }

        /// <summary>Alle Zuege neu zeichnen (nach Aenderungen an der Ansicht).</summary>
        private void RefreshStrips()
        {
            foreach (var s in _channelStrips) s.InvalidateChrome();
            foreach (var s in _groupStrips) s.InvalidateChrome();
            foreach (var s in _outputStrips.Values) s.InvalidateChrome();
            _master.InvalidateChrome();
            _mic.InvalidateChrome();
            _streamStrip?.InvalidateChrome();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        /// <summary>Helligkeit des grauen Pegels (0 = aus). save=false: nur Vorschau.</summary>
        private void SetGhost(int pct, bool save = true)
        {
            Theme.ApplyGhost(pct);
            StripBase.GhostVersion++;
            _mini?.Invalidate();
            if (!save) return;
            _settings.GhostPct = pct;
            ScheduleSave();
        }

        /// <summary>Einstellen, ab wie viel dB SIG und PEAK leuchten (Vorschau sofort im Pult).</summary>
        public void ShowLedDialog(bool peakFirst = false)
        {
            float oldSig = _settings.SigLedDb ?? MixerSettings.SigLedDefault, oldPeak = _settings.PeakLedDb ?? MixerSettings.PeakLedDefault;
            using (var dlg = new LedThresholdDialog(oldSig, oldPeak, peakFirst, StripBase.SetLedThresholds))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.SigLedDb = dlg.SigDb;
                    _settings.PeakLedDb = dlg.PeakDb;
                    StripBase.SetLedThresholds(dlg.SigDb, dlg.PeakDb);
                    ScheduleSave();
                }
                else StripBase.SetLedThresholds(oldSig, oldPeak);   // Abbrechen: wie vorher
            }
        }

        private void ShowGhostDialog()
        {
            int old = _settings.GhostPct ?? Theme.GhostDefault;
            using (var dlg = new GhostDialog(old, pct => SetGhost(pct, false)))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) SetGhost(dlg.Value);
                else SetGhost(old, false);   // Abbrechen: wie vorher
            }
        }

        private void SetChannelHidden(ChannelConfig c, bool hidden)
        {
            c.Hidden = hidden;
            BuildChannelStrips();
            ScheduleSave();
        }

        private ToolStripMenuItem BuildViewMenu()
        {
            var view = Sub("Ansicht");
            var design = Sub("Design");
            for (int i = 0; i < Theme.StyleNames.Length; i++)
            {
                int k = i;
                design.DropDownItems.Add(new ToolStripMenuItem(Theme.StyleNames[i] + "   –  " + Theme.StyleHints[i], null,
                    (s, e) => SetScheme(_settings.ColorScheme, _settings.AccentColor, k)) { Checked = _settings.UiStyle == i });
            }
            view.DropDownItems.Add(design);
            view.DropDownItems.Add(MaterialSub());
            view.DropDownItems.Add(new ToolStripMenuItem("Schmale Kanäle", null, (s, e) => SetNarrow(!_settings.Narrow)) { Checked = _settings.Narrow });
            view.DropDownItems.Add(new ToolStripMenuItem("Meterbridge (große Pegel oben)" + (_bridgeNoRoom ? "  – Fenster zu niedrig" : ""), null, (s, e) => SetBridge(_settings.BridgeOff))
            {
                Checked = !_settings.BridgeOff,
                ToolTipText = _bridgeNoRoom ? "Die Meterbridge ist an, aber das Fenster ist zu niedrig.\nFenster höher ziehen, dann erscheint sie wieder."
                                            : "Leiste über dem Pult: großer Pegel über jedem Kanal, mit dB und SIG/PEAK",
            });
            // Leuchten und Anzeigen an den Kanaelen (eigenes Untermenue, sonst wird "Ansicht" zu lang)
            var lamps = Sub("Leuchten und Anzeigen");
            lamps.DropDownItems.Add(new ToolStripMenuItem("SIG-Leuchte (grün: Ton kommt an)", null, (s, e) => SetLeds(_settings.SignalLedOff, !_settings.PeakLedOff))
            { Checked = !_settings.SignalLedOff, ToolTipText = "Grün = hier kommt Ton an (auch wenn der Kanal stumm oder leise ist)" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("PEAK-Leuchte (rot: gleich zu laut)", null, (s, e) => SetLeds(!_settings.SignalLedOff, _settings.PeakLedOff))
            { Checked = !_settings.PeakLedOff, ToolTipText = "Rot = so laut, dass es gleich übersteuert\nIst nur eine der beiden Leuchten an, bekommt sie die ganze Breite." });
            lamps.DropDownItems.Add(new ToolStripMenuItem("SIG/PEAK: ab wie viel dB …   (SIG " + StripBase.DbText(StripBase.SigDb) + ", PEAK " + StripBase.DbText(StripBase.PeakDbLimit) + ")", null,
                (s, e) => BeginInvoke(new Action(() => ShowLedDialog())))
            { ToolTipText = "Einstellen, ab welchem Pegel SIG und PEAK leuchten\n(geht auch per Rechtsklick auf SIG oder PEAK im Kanal)" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("PEAK zählt mit, wie oft es zu laut war", null, (s, e) =>
            {
                _settings.PeakCountOn = !_settings.PeakCountOn; StripBase.PeakCountOn = _settings.PeakCountOn;
                foreach (var x in AllStrips()) x.ResetPeakCount();
                RelayoutAllStrips(); ScheduleSave();
            }) { Checked = _settings.PeakCountOn, ToolTipText = "Aus (Standard): PEAK leuchtet nur\nAn: die PEAK-Leuchte zeigt eine Zahl, z. B. „PEAK 3“ = 3× zu laut – Klick auf PEAK = Zähler auf 0" });
            if (_settings.PeakCountOn)
            {
                int total = AllStrips().Sum(x => x.PeakCount);
                lamps.DropDownItems.Add(new ToolStripMenuItem("PEAK-Zähler aller Kanäle auf 0" + (total > 0 ? "   (zusammen " + total + "×)" : ""), null, (s, e) =>
                {
                    foreach (var x in AllStrips()) x.ResetPeakCount();
                }) { Enabled = total > 0 });
            }
            lamps.DropDownItems.Add(new ToolStripSeparator());
            lamps.DropDownItems.Add(new ToolStripMenuItem("Absenkung (GR) neben dem Pegel", null, (s, e) =>
            {
                _settings.GrOff = !_settings.GrOff; StripBase.GrOn = !_settings.GrOff; RelayoutAllStrips(); ScheduleSave();
            }) { Checked = !_settings.GrOff, ToolTipText = "Orange LEDs neben dem Pegel jedes Kanals: so viel dB machen\nCOMP, AUTO, Ducking, „Spiel im Hintergrund leiser“ und Sleep-Timer ihn gerade leiser" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Kanäle leuchten im Takt", null, (s, e) =>
            {
                _settings.GlowOff = !_settings.GlowOff; StripBase.GlowOn = !_settings.GlowOff; RefreshStrips(); ScheduleSave();
            }) { Checked = !_settings.GlowOff, ToolTipText = "Das Namensschild glüht in der Kanalfarbe mit – je lauter, desto heller" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("dB-Anzeige an den Pegeln", null, (s, e) =>
            {
                _settings.PeakDbOff = !_settings.PeakDbOff; StripBase.PeakDb = !_settings.PeakDbOff; RefreshStrips(); ScheduleSave();
            }) { Checked = !_settings.PeakDbOff, ToolTipText = "Spitzenwert in dB über jedem Pegel (rot bei Übersteuerung) und dB-Skala daneben" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("MUTE blinkt", null, (s, e) =>
            {
                _settings.MuteBlinkOff = !_settings.MuteBlinkOff; StripBase.MuteBlink = !_settings.MuteBlinkOff; RefreshStrips(); ScheduleSave();
            }) { Checked = !_settings.MuteBlinkOff, ToolTipText = "Stumm geschaltet: die MUTE-Taste blinkt (egal, ob gerade Ton ankommt)\nAus: MUTE leuchtet dauerhaft" });
            lamps.DropDownItems.Add(new ToolStripSeparator());
            lamps.DropDownItems.Add(new ToolStripMenuItem("VU-Meter mit Zeiger am Ausgang", null, (s, e) =>
            {
                _settings.VuOff = !_settings.VuOff; StripBase.VuOn = !_settings.VuOff; RelayoutAllStrips(); ScheduleSave();
            }) { Checked = !_settings.VuOff, ToolTipText = "Analoges Zeigerinstrument im MASTER-Zug" });
            var vuFall = Sub("VU-Zeiger geht zurück");
            string[] fallNames = { "schnell (wie ein echtes VU-Meter, 0,3 s)", "langsam  (Standard)", "sehr langsam" };
            for (int i = 0; i < fallNames.Length; i++)
            {
                int k = i;
                vuFall.DropDownItems.Add(new ToolStripMenuItem(fallNames[i], null, (s, e) =>
                {
                    _settings.VuFall = k; StripBase.VuFall = k; ScheduleSave();
                }) { Checked = (_settings.VuFall ?? 1) == i });
            }
            vuFall.Enabled = !_settings.VuOff;
            var vuStart = Sub("VU-Meter reagiert ab");
            AddVuStartItems(vuStart.DropDownItems);
            vuStart.Enabled = !_settings.VuOff;
            lamps.DropDownItems.Add(vuStart);
            lamps.DropDownItems.Add(vuFall);
            var vuFace = Sub("VU-Meter Farbe");
            for (int i = 0; i < Theme.VuFaceNames.Length; i++)
            {
                int k = i;
                vuFace.DropDownItems.Add(new ToolStripMenuItem(Theme.VuFaceNames[i], null, (s, e) =>
                {
                    _settings.VuFace = k; Theme.VuFace = k; RefreshStrips(); ScheduleSave();
                }) { Checked = Theme.VuFace == i });
            }
            vuFace.Enabled = !_settings.VuOff;
            lamps.DropDownItems.Add(vuFace);
            lamps.DropDownItems.Add(new ToolStripMenuItem("Großes VU-Meter im Fenster", null, (s, e) => ToggleVuWindow()) { Checked = VuWindowOpen });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Spektrum in der Meterbridge", null, (s, e) => SetSpectrumOn(_settings.SpecOff)) { Checked = !_settings.SpecOff });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Großes Spektrum im Fenster", null, (s, e) =>
            {
                if (!VuWindowOpen || !_vuWin.ShowSpectrum) ShowSpectrumWindow();
                else if (!_vuWin.ShowVu) CloseVuWindow();
                else SetVuWinMode(_vuWin, 1);   // VU bleibt, Spektrum weg
            }) { Checked = VuWindowOpen && _vuWin.ShowSpectrum });
            lamps.DropDownItems.Add(SpecSourceSub());
            lamps.DropDownItems.Add(new ToolStripMenuItem("Schleppzeiger im VU-Meter", null, (s, e) =>
            {
                _settings.VuPeakNeedleOff = !_settings.VuPeakNeedleOff; StripBase.VuPeakNeedle = !_settings.VuPeakNeedleOff; RefreshVuStrips(); ScheduleSave();
            }) { Checked = !_settings.VuPeakNeedleOff, Enabled = !_settings.VuOff, ToolTipText = "Dünner roter Zeiger, der kurz am lautesten Punkt stehen bleibt" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("PEAK-Leuchte im VU-Meter", null, (s, e) =>
            {
                _settings.VuPeakLedOff = !_settings.VuPeakLedOff; StripBase.VuPeakLed = !_settings.VuPeakLedOff; RefreshVuStrips(); ScheduleSave();
            }) { Checked = !_settings.VuPeakLedOff, Enabled = !_settings.VuOff });
            AddFeatures7Items(lamps.DropDownItems);
            AddFeatures8Items(lamps.DropDownItems);
            lamps.DropDownItems.Add(new ToolStripMenuItem("Kanalnummern", null, (s, e) =>
            {
                _settings.NumbersOff = !_settings.NumbersOff; StripBase.ShowNumbers = !_settings.NumbersOff; RefreshStrips(); ScheduleSave();
            }) { Checked = !_settings.NumbersOff, ToolTipText = "1, 2, 3 … im Namensschild – wie am echten Pult" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Licht über dem Kanal unter der Maus", null, (s, e) =>
            {
                _settings.HoverLightOff = !_settings.HoverLightOff; StripBase.HoverLight = !_settings.HoverLightOff; RefreshStrips(); ScheduleSave();
            }) { Checked = !_settings.HoverLightOff });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Motorfader beim Szenenwechsel", null, (s, e) =>
            {
                _settings.MotorOff = !_settings.MotorOff; StripBase.MotorFaders = !_settings.MotorOff; ScheduleSave();
            }) { Checked = !_settings.MotorOff, ToolTipText = "Fader fahren beim Laden einer Szene sichtbar an ihre Stelle" });
            lamps.DropDownItems.Add(new ToolStripMenuItem("Selbsttest beim Start", null, (s, e) =>
            {
                _settings.SelfTestOff = !_settings.SelfTestOff; StripBase.SelfTestOn = !_settings.SelfTestOff; ScheduleSave();
                if (StripBase.SelfTestOn) StripBase.StartSelfTest();   // gleich einmal zeigen
            }) { Checked = !_settings.SelfTestOff, ToolTipText = "Beim Start laufen alle Pegel und Leuchten einmal durch, die Fader fahren an ihre Stelle" });
            view.DropDownItems.Add(lamps);
            var meters = Sub("Pegel-Farben");
            for (int i = 0; i < Theme.MeterStyleNames.Length; i++)
            {
                int k = i;
                meters.DropDownItems.Add(new ToolStripMenuItem(Theme.MeterStyleNames[i], null, (s, e) => SetMeterStyle(k)) { Checked = _settings.MeterColors == i });
            }
            view.DropDownItems.Add(meters);
            var lcds = Sub("Zahlen-Anzeigen");
            for (int i = 0; i < Theme.LcdStyleNames.Length; i++)
            {
                int k = i;
                lcds.DropDownItems.Add(new ToolStripMenuItem(Theme.LcdStyleNames[i], null, (s, e) => SetLcdStyle(k)) { Checked = _settings.LcdStyle == i });
            }
            view.DropDownItems.Add(lcds);
            view.DropDownItems.Add(new ToolStripMenuItem("Kanalfarbe auf Fadern und Knöpfen", null, (s, e) => SetColorCaps(_settings.ColorCapsOff)) { Checked = !_settings.ColorCapsOff });
            var scheme = Sub("Farbschema");
            for (int i = 0; i < Theme.SchemeNames.Length; i++)
            {
                int k = i;
                scheme.DropDownItems.Add(new ToolStripMenuItem(Theme.SchemeNames[i], null, (s, e) => SetScheme(k, _settings.AccentColor)) { Checked = _settings.ColorScheme == i });
            }
            view.DropDownItems.Add(scheme);
            var accent = Sub("Akzentfarbe", false);
            for (int i = 0; i < Theme.AccentNames.Length; i++)
            {
                int k = i;
                accent.DropDownItems.Add(new ToolStripMenuItem(Theme.AccentNames[i], Swatch(Theme.AccentPreview(i)), (s, e) => SetScheme(_settings.ColorScheme, k)) { Checked = _settings.AccentColor == i });
            }
            view.DropDownItems.Add(accent);
            var ghost = Sub("Grauer Pegel (was eigentlich läuft)");
            ghost.DropDownItems.Add(Hint("Hinter dem farbigen Pegel: so laut wäre es ohne Fader und Stumm."));
            int cur = _settings.GhostPct ?? Theme.GhostDefault;
            bool preset = false;
            for (int i = 0; i < Theme.GhostNames.Length; i++)
            {
                int pct = Theme.GhostPresets[i];
                bool on = cur == pct;
                preset |= on;
                ghost.DropDownItems.Add(new ToolStripMenuItem(Theme.GhostNames[i] + (pct == Theme.GhostDefault ? "  (Standard)" : ""), null, (s, e) => SetGhost(pct))
                { Checked = on });
            }
            ghost.DropDownItems.Add(new ToolStripSeparator());
            ghost.DropDownItems.Add(new ToolStripMenuItem("Selbst einstellen …" + (preset ? "" : "  (" + cur + " %)"), null, (s, e) => BeginInvoke(new Action(ShowGhostDialog)))
            { Checked = !preset });
            view.DropDownItems.Add(ghost);
            var hidden = _settings.Channels.Where(c => c.Hidden).ToList();
            var hid = Sub("Ausgeblendete Kanäle (" + hidden.Count + ")", false);
            if (hidden.Count == 0) hid.DropDownItems.Add(Hint("Keine – Rechtsklick auf einen Kanal → Ausblenden"));
            foreach (var c in hidden)
            {
                var ch = c;
                hid.DropDownItems.Add(new ToolStripMenuItem(c.Name + " wieder anzeigen", Swatch(Color.FromArgb(c.Color)), (s, e) => SetChannelHidden(ch, false)));
            }
            view.DropDownItems.Add(hid);
            view.DropDownItems.Add(new ToolStripMenuItem("Mikrofon-Kanal anzeigen", null, (s, e) => SetMicVisible(_settings.HideMic)) { Checked = !_settings.HideMic });
            view.DropDownItems.Add(new ToolStripMenuItem("Mikrofon bei den Kanälen", null, (s, e) => SetMicWithOutputs(!_settings.MicWithOutputs))
            { Checked = !_settings.MicWithOutputs, Enabled = !_settings.HideMic });
            return view;
        }

        // ================================================================ Export / Import

        private void ExportSettings()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Einstellungen exportieren",
                Filter = "Mischpult-Einstellungen (*.json)|*.json",
                FileName = "Mischpult-Einstellungen " + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                RememberWindow();
                if (_settings.SaveTo(dlg.FileName)) ShowOsd("Einstellungen exportiert", Path.GetFileName(dlg.FileName), -1, Theme.LedGreen);
                else MessageBox.Show(this, "Die Datei konnte nicht gespeichert werden.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImportSettings()
        {
            using (var dlg = new OpenFileDialog { Title = "Einstellungen importieren", Filter = "Mischpult-Einstellungen und Umzugs-Pakete (*.json;*" + Package410.Ext + ")|*.json;*" + Package410.Ext + "|Alle Dateien|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (Path.GetExtension(dlg.FileName).Equals(Package410.Ext, StringComparison.OrdinalIgnoreCase)) { ImportPackage410(dlg.FileName); return; }   // 4.1
                var imported = MixerSettings.LoadFrom(dlg.FileName);
                if (imported == null)
                {
                    MessageBox.Show(this, "Die Datei enthält keine gültigen Mischpult-Einstellungen.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MessageBox.Show(this, "Einstellungen aus »" + Path.GetFileName(dlg.FileName) + "« übernehmen?\n\n" +
                        imported.Channels.Count + " Kanäle, " + imported.Scenes.Count + " Szenen, " + imported.Sounds.Count + " Sounds.\n" +
                        "Die jetzigen Einstellungen werden ersetzt. Das Mischpult startet dafür neu.",
                        "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                if (_demo) { MessageBox.Show(this, "Im Demo-Modus wird nichts gespeichert.", "Mischpult"); return; }
                if (!imported.SaveTo(MixerSettings.FilePath))
                {
                    MessageBox.Show(this, "Die Einstellungen konnten nicht gespeichert werden.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _importing = true; // beim Beenden nicht ueberschreiben
                Program.RestartRequested = true;
                ExitApp();
            }
        }

        // ================================================================ Laufend

        private void TickFeatures2(double now, float dt)
        {
            UpdateSounds(now);
            UpdateMuteWarning(now);
        }

        private void CleanupFeatures2()
        {
            try { _eqWin?.Close(); } catch { }
            try { _sbForm?.Close(); } catch { }
            try { Backend.StopSounds(); } catch { }
            if (_appliedVoice != null) { Backend.SetVoiceOut(null); _appliedVoice = null; }
            if (_appliedStream != null) { Backend.SetStreamMix(null); _appliedStream = null; }
        }
    }
}
