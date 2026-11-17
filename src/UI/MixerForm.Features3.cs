using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.11: DIM (alles kurz leiser), Loudness (bei leisem Hoeren Bass und Hoehen anheben) und FADE (Kanal langsam aus/ein).
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ DIM

        private bool _dimOn;   // wird nicht gespeichert: nach dem Start ist DIM immer aus

        public bool DimOn => _dimOn;
        public float DimDb => _settings.DimDb ?? MixerSettings.DimDefault;

        /// <summary>DIM an/aus: alles um DimDb leiser (wie die DIM-Taste am Studio-Monitor-Controller).</summary>
        public void ToggleDim(bool osd)
        {
            _dimOn = !_dimOn;
            PushPolicy();
            RefreshOutputKeys();
            UpdateStatus();
            if (osd) ShowOsd("DIM", _dimOn ? "−" + ((int)Math.Round(DimDb)) + " dB" : "AUS", -1, _dimOn ? Theme.SoloAmber : Theme.TextDim);
        }

        private void SetDimDb(float db)
        {
            _settings.DimDb = Math.Max(MixerSettings.DimMin, Math.Min(MixerSettings.DimMax, (float)Math.Round(db)));
            if (_dimOn) PushPolicy();
            RefreshOutputKeys();
            UpdateStatus();
            ScheduleSave();
        }

        /// <summary>DIM, LOUD (und MUTE) in allen Ausgangszuegen neu zeichnen.</summary>
        private void RefreshOutputKeys()
        {
            _master?.InvalidateChrome();
            foreach (var o in _outputStrips.Values) o.InvalidateChrome();
            _top.Invalidate();
        }

        private static readonly float[] DimSteps = { 10f, 15f, 20f, 30f };

        public void ShowDimMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("DIM – alles kurz leiser"));
            m.Items.Add(Hint("Klick auf DIM: an/aus. Am Fader ändert sich nichts."));
            m.Items.Add(new ToolStripSeparator());
            foreach (float d in DimSteps)
            {
                float db = d;
                m.Items.Add(new ToolStripMenuItem("um " + (int)db + " dB leiser" + (db == MixerSettings.DimDefault ? "  – Standard" : ""), null, (s, e) => SetDimDb(db))
                { Checked = Math.Abs(DimDb - db) < 0.5f });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(_dimOn ? "DIM ausschalten" : "DIM einschalten", null, (s, e) => ToggleDim(false)));
            m.Show(screen);
        }

        // ================================================================ Loudness

        /// <summary>Aktuelle Anhebung (Bass) je Ausgabegeraet – um nur bei Aenderungen neu zu setzen.</summary>
        private readonly Dictionary<string, float> _loudApplied = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public bool LoudOn => _settings.LoudOn && _snap.EqSupported;

        private static readonly float[] LoudMax = { 6f, 9f, 12f };   // Bass-Anhebung bei ganz leise: leicht, mittel, stark
        private static readonly string[] LoudNames = { "Leicht", "Mittel", "Stark" };

        /// <summary>
        /// Wie viel Bass (dB) die Loudness bei dieser Windows-Lautstaerke anhebt. Wie bei alten Hi-Fi-Anlagen: je leiser, desto mehr
        /// (ab etwa −6 dB Geraetelautstaerke abwaerts, voll ab etwa −36 dB). Ohne "nach Lautstaerke" immer voll.
        /// Die Hoehen bekommen etwa 45 % davon.
        /// </summary>
        public static float LoudBass(float deviceDb, int strength, bool byVolume)
        {
            float max = LoudMax[Math.Max(0, Math.Min(LoudMax.Length - 1, strength))];
            float frac = byVolume ? Math.Max(0f, Math.Min(1f, (-deviceDb - 6f) / 30f)) : 1f;
            return (float)Math.Round(max * frac * 2f) / 2f;   // halbe dB – sonst wuerde bei jeder Kleinigkeit neu gesetzt
        }

        public static float LoudTreble(float bass) => (float)Math.Round(bass * 0.45f * 2f) / 2f;

        /// <summary>Anhebung fuer einen Kanal (nach seinem Ausgabegeraet).</summary>
        private float LoudBassFor(ChannelConfig c)
        {
            if (!LoudOn) return 0f;
            DeviceState d = null;
            if (MultiOutput && c.OutputDevice != null)
                d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, c.OutputDevice, StringComparison.OrdinalIgnoreCase));
            if (d == null) d = DefaultDevice;
            return LoudBass(d?.VolumeDb ?? 0f, _settings.LoudStrength, !_settings.LoudFixed);
        }

        /// <summary>Aus dem Takt: Lautstaerke geaendert -> Loudness nachfuehren (nur wenn sich die Anhebung wirklich aendert).</summary>
        private void UpdateLoudness()
        {
            if (!LoudOn) { if (_loudApplied.Count > 0) { _loudApplied.Clear(); RefreshOutputKeys(); } return; }
            bool changed = false;
            foreach (var d in _snap.Devices)
            {
                float b = LoudBass(d.VolumeDb, _settings.LoudStrength, !_settings.LoudFixed);
                if (!_loudApplied.TryGetValue(d.Id, out float old) || Math.Abs(old - b) > 0.01f) { _loudApplied[d.Id] = b; changed = true; }
            }
            if (changed) { PushPolicy(); RefreshOutputKeys(); }
        }

        /// <summary>Loudness gerade (fuer das Standardgeraet): Bass-Anhebung in dB.</summary>
        public float LoudNowDb => LoudNowDbFor(null);

        /// <summary>Loudness gerade fuer ein Geraet (null = Standardgeraet).</summary>
        public float LoudNowDbFor(string deviceId)
        {
            if (!LoudOn) return 0f;
            DeviceState d = deviceId == null ? null : _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (d == null) d = DefaultDevice;
            return LoudBass(d?.VolumeDb ?? 0f, _settings.LoudStrength, !_settings.LoudFixed);
        }

        /// <summary>Loudness eingeschaltet, aber hier nicht moeglich (Windows zu alt)?</summary>
        public bool LoudUnavailable => _settings.LoudOn && !_snap.EqSupported && _snap.Structure != "";

        public void ToggleLoud(bool osd)
        {
            if (!_snap.EqSupported && _snap.Structure != "")
            {
                string msg = _snap.EqStatus ?? "Loudness braucht Windows 10 Version 2004 oder neuer.";
                if (osd) ShowOsd("Loudness", "nicht verfügbar", -1, Theme.MuteRed);
                else MessageBox.Show(this, msg, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.LoudOn = !_settings.LoudOn;
            _loudApplied.Clear();
            PushPolicy();
            RefreshOutputKeys();
            foreach (var s in _channelStrips) s.InvalidateChrome();
            ScheduleSave();
            if (osd) ShowOsd("Loudness", _settings.LoudOn ? "AN" + LoudOsdSuffix() : "AUS", -1, _settings.LoudOn ? Theme.LedGreen : Theme.TextDim);
        }

        private string LoudOsdSuffix()
        {
            float b = LoudNowDb;
            return b >= 0.5f ? " · Bass +" + b.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " dB" : " · gerade laut genug";
        }

        /// <summary>Was die Loudness gerade macht, z. B. "Bass +4 dB, Höhen +2 dB" bzw. "keine Anhebung (laut genug)".</summary>
        public string LoudNowText(string deviceId = null)
        {
            float b = LoudNowDbFor(deviceId), t = LoudTreble(b);
            if (b < 0.5f) return "keine Anhebung (laut genug)";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return "Bass +" + b.ToString("0.#", ci) + " dB" + (t >= 0.5f ? ", Höhen +" + t.ToString("0.#", ci) + " dB" : "");
        }

        private void SetLoudStrength(int i)
        {
            _settings.LoudStrength = Math.Max(0, Math.Min(LoudMax.Length - 1, i));
            _loudApplied.Clear();
            if (LoudOn) PushPolicy();
            RefreshOutputKeys();
            ScheduleSave();
        }

        private void SetLoudFixed(bool fixedLevel)
        {
            _settings.LoudFixed = fixedLevel;
            _loudApplied.Clear();
            if (LoudOn) PushPolicy();
            RefreshOutputKeys();
            ScheduleSave();
        }

        public void ShowLoudMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Loudness – voller Klang bei leisem Hören"));
            m.Items.Add(Hint("Je leiser, desto mehr Bass und Höhen – wie bei alten Hi-Fi-Anlagen."));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Loudness an", null, (s, e) => ToggleLoud(false)) { Checked = _settings.LoudOn });
            m.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < LoudNames.Length; i++)
            {
                int k = i;
                m.Items.Add(new ToolStripMenuItem(LoudNames[i] + "  (bis +" + (int)LoudMax[i] + " dB Bass)" + (i == 1 ? "  – Standard" : ""), null, (s, e) => SetLoudStrength(k))
                { Checked = _settings.LoudStrength == i });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Nach der Windows-Lautstärke richten", null, (s, e) => SetLoudFixed(!_settings.LoudFixed))
            {
                Checked = !_settings.LoudFixed,
                ToolTipText = "An: je leiser Windows eingestellt ist, desto mehr Anhebung (laut = keine)\nAus: immer volle Anhebung – z. B. wenn du die Lautstärke am Verstärker/Headset-Regler einstellst",
            });
            m.Show(screen);
        }

        // ================================================================ FADE

        /// <summary>Laufendes Ein-/Ausblenden eines Kanals.</summary>
        private sealed class FadeState
        {
            public float Db;        // aktuelle Absenkung (0 = normal, FadeFloorDb = fast still)
            public float Applied;   // auf halbe dB gerundet (so wird nicht bei jedem Bild neu gesetzt)
            public bool Out;        // true = ausblenden, false = einblenden
        }

        private readonly Dictionary<string, FadeState> _fades = new Dictionary<string, FadeState>();
        public const float FadeFloorDb = -45f;
        public float FadeSec => _settings.FadeSec ?? MixerSettings.FadeDefault;

        public bool IsFading(ChannelConfig c) => c != null && _fades.ContainsKey(c.Id);
        public bool IsFadingOut(ChannelConfig c) => c != null && _fades.TryGetValue(c.Id, out var f) && f.Out;

        /// <summary>
        /// FADE: Kanal langsam ausblenden (danach stumm) bzw. – wenn er stumm ist – langsam einblenden.
        /// Nochmal druecken, waehrend es laeuft: kehrt um.
        /// </summary>
        public void ToggleFade(ChannelConfig c, bool osd)
        {
            if (c == null) return;
            if (_fades.TryGetValue(c.Id, out var f)) f.Out = !f.Out;
            else if (c.Mute) { c.Mute = false; _fades[c.Id] = new FadeState { Db = FadeFloorDb, Applied = FadeFloorDb, Out = false }; }
            else _fades[c.Id] = new FadeState { Db = 0f, Applied = 0f, Out = true };
            bool outNow = _fades[c.Id].Out;
            PushPolicy();
            RefreshChannelStrip(c);
            ScheduleSave();
            if (osd) ShowOsd(c.Name, (outNow ? "AUSBLENDEN" : "EINBLENDEN") + " · " + FadeSecText(FadeSec), -1, Color.FromArgb(c.Color));
        }

        /// <summary>
        /// Ein-/Ausblenden abbrechen (MUTE gedrueckt). Setzt nichts im Ton – der Aufrufer schaltet gleich danach MUTE
        /// und schickt beides zusammen (sonst koennte kurz der volle Pegel durchkommen).
        /// </summary>
        public void CancelFade(ChannelConfig c)
        {
            if (c == null || !_fades.Remove(c.Id)) return;
            RefreshChannelStrip(c);
        }

        /// <summary>
        /// Alle Blenden sofort zu Ende bringen (Szene laden, Beenden): ausblendende Kanaele werden stumm, einblendende voll.
        /// So springt kein Kanal, der gerade ausgeblendet wurde, auf einmal wieder auf volle Lautstaerke.
        /// </summary>
        private void FinishAllFades()
        {
            if (_fades.Count == 0) return;
            foreach (var kv in _fades)
            {
                if (!kv.Value.Out) continue;
                var c = _settings.Channels.FirstOrDefault(x => x.Id == kv.Key);
                if (c != null) c.Mute = true;
            }
            _fades.Clear();
            foreach (var s in _channelStrips) s.InvalidateChrome();
        }

        private void RefreshChannelStrip(ChannelConfig c)
        {
            foreach (var s in _channelStrips) if (s.Config == c) s.InvalidateChrome();
        }

        public static string FadeSecText(float sec) => ((int)Math.Round(sec)) + " s";

        /// <summary>Aus dem Takt: Blenden weiterfuehren (gleichmaessig in dB – klingt natuerlich).</summary>
        private void UpdateFades(float dt)
        {
            if (_fades.Count == 0) return;
            if (dt > 0.25f) dt = 0.25f;
            float rate = -FadeFloorDb / Math.Max(0.5f, FadeSec);   // dB pro Sekunde
            bool push = false;
            foreach (var id in _fades.Keys.ToList())
            {
                var f = _fades[id];
                var c = _settings.Channels.FirstOrDefault(x => x.Id == id);
                if (c == null) { _fades.Remove(id); continue; }
                f.Db += (f.Out ? -rate : rate) * dt;
                if (f.Out && f.Db <= FadeFloorDb)
                {
                    // ganz unten: stumm schalten und die Absenkung wieder weg (der Fader bleibt, wo er war)
                    c.Mute = true;
                    _fades.Remove(id);
                    push = true;
                    RefreshChannelStrip(c);
                    ScheduleSave();
                    continue;
                }
                if (!f.Out && f.Db >= 0f)
                {
                    _fades.Remove(id);
                    push = true;
                    RefreshChannelStrip(c);
                    continue;
                }
                float a = (float)Math.Round(f.Db * 2f) / 2f;
                if (a != f.Applied) { f.Applied = a; push = true; }
            }
            if (push) PushPolicy();
        }

        /// <summary>Zusaetzliche Absenkung durch FADE fuer diesen Kanal (dB, 0 = keine).</summary>
        private float FadeDbFor(ChannelConfig c) => _fades.TryGetValue(c.Id, out var f) ? f.Applied : 0f;

        private static readonly float[] FadeSteps = { 2f, 5f, 10f, 20f };

        public void ShowFadeMenu(ChannelStrip strip, Point screen)
        {
            var c = strip.Config;
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("FADE – " + c.Name + " langsam " + (c.Mute && !IsFading(c) ? "einblenden" : "aus-/einblenden")));
            m.Items.Add(Hint("Klick auf FADE: langsam aus (danach stumm) bzw. wieder ein."));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(IsFading(c) ? "Umkehren" : c.Mute ? "Jetzt langsam einblenden" : "Jetzt langsam ausblenden", null, (s, e) => ToggleFade(c, false)));
            m.Items.Add(new ToolStripSeparator());
            foreach (float d in FadeSteps)
            {
                float sec = d;
                m.Items.Add(new ToolStripMenuItem("Dauer " + FadeSecText(sec) + (sec == MixerSettings.FadeDefault ? "  – Standard" : ""), null, (s, e) => SetFadeSec(sec))
                { Checked = Math.Abs(FadeSec - sec) < 0.5f });
            }
            m.Show(screen);
        }

        private void SetFadeSec(float sec)
        {
            _settings.FadeSec = Math.Max(MixerSettings.FadeMin, Math.Min(MixerSettings.FadeMax, (float)Math.Round(sec)));
            ScheduleSave();
        }

        private void SetFadeKeys(bool on)
        {
            _settings.FadeKeyOff = !on;
            ChannelStrip.FadeKeys = on;
            RelayoutAllStrips();
            ScheduleSave();
        }
    }
}
