using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>4.0 – Klang: parametrischer EQ, Kopfhoerer-Profile (AutoEq), EQ/Kompressor fuer Gruppen, Hoertest mit Hoer-EQ.</summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Schalter fuer OutFxIsActive/BuildOutputFx (statisch, wie Bass mono).</summary>
        internal static bool PeqGlobal, HpEqGlobal, HearGlobal, HearTestRunning;
        internal static HearProfile HearNow;

        private void ApplyKlang400(MixerSettings s)
        {
            PeqGlobal = s.PeqOn;
            HpEqGlobal = s.HpEqOn;
            HearGlobal = s.HearEqOn;
            HearNow = s.Hear;
        }

        // ================================================================ 167 Parametrischer EQ

        /// <summary>PEQ eines Kanals fuer den Mischer (null = keiner).</summary>
        private PeqSet PeqFor(ChannelConfig c) => _settings.PeqOn && !c.PeqBypass ? PeqSet.From(c.Peq, _settings.MsEqOn, _settings.DynEqOn) : null;

        private readonly Dictionary<string, PeqWindow> _peqWins = new Dictionary<string, PeqWindow>();

        private void ShowPeq(PeqWindow.Target t)
        {
            if (_peqWins.TryGetValue(t.Key, out var w) && !w.IsDisposed) { w.Activate(); return; }
            w = new PeqWindow(t);
            w.FormClosed += (s, e) => _peqWins.Remove(t.Key);
            _peqWins[t.Key] = w;
            w.Show(this);
        }

        public void ShowChannelPeq(ChannelConfig c)
        {
            if (!_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "EQ nicht verfügbar") + "\n\nDer parametrische EQ braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            ShowPeq(new PeqWindow.Target
            {
                Key = "ch:" + c.Id, Title = c.Name, Color = Color.FromArgb(c.Color),
                Get = () => c.Peq,
                Set = (l, final) => { c.Peq = l != null && l.Count > 0 ? l : null; OnChannelEdited(c); if (final) ScheduleSave(); },
                Bypass = () => c.PeqBypass,
                SetBypass = v => { c.PeqBypass = v; OnChannelEdited(c); ScheduleSave(); },
                Pro = true,   // 4.0 finale: Mitte/Seite und dynamische Baender
            });
        }

        public void ShowOutputPeq(string deviceId, string name)
        {
            if (deviceId == null) return;
            if (!_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "EQ nicht verfügbar") + "\n\nDer parametrische EQ braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            var o = OutFxConfig(deviceId, true, name);
            ShowPeq(new PeqWindow.Target
            {
                Key = "out:" + deviceId, Title = OutputDisplayName(deviceId, name), Color = Theme.Accent,
                Get = () => o.Peq,
                Set = (l, final) => { o.Peq = l != null && l.Count > 0 ? l : null; _settings.OutFxOff = false; PushPolicy(); if (final) ScheduleSave(); },
                Bypass = () => o.PeqBypass,
                SetBypass = v => { o.PeqBypass = v; PushPolicy(); ScheduleSave(); },
                Extra = () => PeqSet.Combine(HpEqGlobal && !o.HpOff ? PeqSet.From(o.HpBands, o.HpPreamp) : null, HearSetFor(deviceId)),
                ExtraLabel = "Kopfhörer-Profil / Hör-EQ (zusätzlich)",
            });
        }

        private string OutputDisplayName(string deviceId, string name) =>
            _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.Name ?? name ?? "Ausgang";

        /// <summary>Ist auf dem Ausgang ein 4.0-Klang aktiv (PEQ, Profil, Hoer-EQ)?</summary>
        internal static bool Peq400Active(OutputFxConfig o) =>
            o != null && ((PeqGlobal && !o.PeqBypass && PeqSet.From(o.Peq) != null) || (HpEqGlobal && !o.HpOff && o.HpBands != null) || HearSetFor(o.DeviceId) != null);

        /// <summary>Alles zusammen fuer den Ausgang (null = nichts).</summary>
        internal static PeqSet OutputPeq400(MixerSettings s, OutputFxConfig o)
        {
            if (o == null) return null;
            return PeqSet.Combine(
                s.PeqOn && !o.PeqBypass ? PeqSet.From(o.Peq) : null,
                s.HpEqOn && !o.HpOff ? PeqSet.From(o.HpBands, o.HpPreamp) : null,
                HearSetFor(o.DeviceId));
        }

        /// <summary>Hoer-EQ fuer dieses Geraet (null = keiner, auch waehrend des Hoertests).</summary>
        internal static PeqSet HearSetFor(string deviceId)
        {
            var h = HearNow;
            if (!HearGlobal || HearTestRunning || h == null || deviceId == null || !string.Equals(h.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) return null;
            return HearingEq.Build(h);
        }

        // ================================================================ 171 Kopfhoerer-Profile (AutoEq)

        /// <summary>
        /// Liest eine AutoEq-Datei ("ParametricEQ.txt" fuer Equalizer APO): "Preamp: -6.2 dB" und Zeilen wie
        /// "Filter 1: ON PK Fc 105 Hz Gain -2.3 dB Q 0.70" (auch LSC/HSC/LS/HS, HP/LP). Liefert null bei Fehler.
        /// </summary>
        internal static List<PeqBand> ParseAutoEq(string text, out float preamp, out string error)
        {
            preamp = 0f; error = null;
            var bands = new List<PeqBand>();
            if (string.IsNullOrWhiteSpace(text)) { error = "Die Datei ist leer."; return null; }
            var inv = CultureInfo.InvariantCulture;
            bool graphic = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("GraphicEQ", StringComparison.OrdinalIgnoreCase)) { graphic = true; continue; }
                if (line.StartsWith("Preamp", StringComparison.OrdinalIgnoreCase))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"(-?\d+(?:[.,]\d+)?)\s*dB", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) float.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, inv, out preamp);
                    continue;
                }
                if (!line.StartsWith("Filter", StringComparison.OrdinalIgnoreCase)) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                var tok = line.Substring(colon + 1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length < 2 || !tok[0].Equals("ON", StringComparison.OrdinalIgnoreCase)) continue;
                string kind = tok[1].ToUpperInvariant();
                int type;
                switch (kind)
                {
                    case "PK": case "PEQ": case "MODAL": type = PeqBand.Bell; break;
                    case "LS": case "LSC": case "LSQ": type = PeqBand.LowShelf; break;
                    case "HS": case "HSC": case "HSQ": type = PeqBand.HighShelf; break;
                    case "HP": case "HPQ": type = PeqBand.HighPass; break;
                    case "LP": case "LPQ": type = PeqBand.LowPass; break;
                    default: continue;
                }
                float fc = 0, gain = 0, q = 0;
                for (int i = 2; i + 1 < tok.Length; i++)
                {
                    string key = tok[i].ToUpperInvariant();
                    float.TryParse(tok[i + 1].Replace(',', '.'), NumberStyles.Float, inv, out float v);
                    if (key == "FC") fc = v; else if (key == "GAIN") gain = v; else if (key == "Q") q = v;
                }
                if (fc <= 0) continue;
                var b = new PeqBand { Type = type, Hz = fc, Db = gain, Q = q > 0 ? q : 0.71f };
                b.Clamp();
                bands.Add(b);
                if (bands.Count >= 20) break;
            }
            if (bands.Count == 0)
            {
                error = graphic ? "Das ist eine „GraphicEQ“-Datei. Bitte bei autoeq.app die Datei „ParametricEQ.txt“ (Equalizer APO, parametrisch) herunterladen."
                                : "In der Datei stehen keine Filter. Gesucht sind Zeilen wie „Filter 1: ON PK Fc 105 Hz Gain -2.3 dB Q 0.70“.";
                return null;
            }
            preamp = Math.Max(-30f, Math.Min(6f, float.IsNaN(preamp) ? 0f : preamp));
            return bands;
        }

        private void LoadHpProfile(string deviceId, string name)
        {
            if (!_snap.EqSupported && !_demo) { MessageBox.Show(this, "Kopfhörer-Profile brauchen Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            using (var dlg = new OpenFileDialog { Title = "Kopfhörer-Profil (AutoEq, ParametricEQ.txt) laden", Filter = "AutoEq-Profil (*.txt)|*.txt|Alle Dateien|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string text;
                try { text = File.ReadAllText(dlg.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "Die Datei ließ sich nicht lesen: " + ex.Message, "Mischpult"); return; }
                var bands = ParseAutoEq(text, out float pre, out string err);
                if (bands == null) { MessageBox.Show(this, err, "Kopfhörer-Profil", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                string prof = Path.GetFileNameWithoutExtension(dlg.FileName);
                if (prof.EndsWith(" ParametricEQ", StringComparison.OrdinalIgnoreCase)) prof = prof.Substring(0, prof.Length - 13);
                if (prof.Equals("ParametricEQ", StringComparison.OrdinalIgnoreCase)) prof = Path.GetFileName(Path.GetDirectoryName(dlg.FileName)) ?? "Profil";
                SetHpProfile(deviceId, name, prof, bands, pre);
            }
        }

        private void SetHpProfile(string deviceId, string name, string prof, List<PeqBand> bands, float preamp)
        {
            var o = OutFxConfig(deviceId, true, name);
            o.HpBands = bands; o.HpPreamp = preamp; o.HpName = bands == null ? null : prof; o.HpOff = false;
            if (bands != null) { _settings.OutFxOff = false; if (!_settings.HpEqOn) { _settings.HpEqOn = true; HpEqGlobal = true; } }
            PushPolicy();
            ScheduleSave();
            ShowOsd("Kopfhörer-Profil · " + OutputDisplayName(deviceId, name), bands == null ? "entfernt" : prof + " (" + bands.Count + " Filter)", -1, CueColor);
        }

        // ================================================================ 206 Hoertest

        private HearTestWindow _hearWin;

        private void ShowHearTest()
        {
            if (!_snap.EqSupported && !_demo) { MessageBox.Show(this, "Der Hörtest braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            if (_hearWin != null && !_hearWin.IsDisposed) { _hearWin.Activate(); return; }
            var def = DefaultDevice;
            HearTestRunning = true;
            PushOutputFx();   // Hoer-EQ waehrend des Tests aus
            _hearWin = new HearTestWindow((clip, gain) => { try { Backend.PlaySoundOn(clip, gain, null); } catch { } }, ApplyHear, _settings.Hear) { DeviceName = def?.Name };
            _hearWin.FormClosed += (s, e) => { _hearWin = null; HearTestRunning = false; PushOutputFx(); };
            _hearWin.Show(this);
        }

        private void ApplyHear(HearProfile p)
        {
            var def = DefaultDevice;
            p.DeviceId = def?.Id ?? _settings.Hear?.DeviceId;
            p.DeviceName = def?.Name ?? p.DeviceName;
            _settings.Hear = p;
            _settings.HearEqOn = true;
            HearGlobal = true; HearNow = p;
            if (p.DeviceId != null) { OutFxConfig(p.DeviceId, true, p.DeviceName); _settings.OutFxOff = false; }
            HearTestRunning = false;
            PushPolicy();
            ScheduleSave();
            ShowOsd("Hör-EQ", "an · " + (p.DeviceName ?? "Ausgang"), -1, CueColor);
        }

        private void SetHearEq(bool on)
        {
            if (on && _settings.Hear == null) { ShowHearTest(); return; }
            _settings.HearEqOn = on;
            HearGlobal = on;
            if (on && _settings.Hear?.DeviceId != null) { OutFxConfig(_settings.Hear.DeviceId, true, _settings.Hear.DeviceName); _settings.OutFxOff = false; }
            PushPolicy();
            ScheduleSave();
        }

        // ================================================================ 190 Gruppen mit EQ/Kompressor

        /// <summary>EQ/Kompressor aller Gruppen, in denen der Kanal ist (zusammengezaehlt).</summary>
        private GroupFx GroupFxFor(ChannelConfig c)
        {
            if (!_settings.GroupFxOn || _settings.Groups == null) return null;
            float lo = 0, mi = 0, hi = 0; bool comp = false, any = false;
            foreach (var g in _settings.Groups)
            {
                if (g.Members == null || !g.Members.Contains(c.Id)) continue;
                if (g.EqOn) { lo += g.EqLow; mi += g.EqMid; hi += g.EqHigh; any = true; }
                if (g.CompOn) { comp = true; any = true; }
            }
            if (!any) return null;
            var fx = new GroupFx(lo, mi, hi, comp);
            return fx.IsOff ? null : fx;
        }

        private void AddGroupMenu400(ToolStripItemCollection items, GroupConfig g, GroupStrip strip)
        {
            if (!_settings.GroupFxOn) return;
            var sub = Sub("Klang der Gruppe: " + (g.EqOn || g.CompOn ? (g.EqOn ? "EQ" : "") + (g.EqOn && g.CompOn ? " + " : "") + (g.CompOn ? "Kompressor" : "") : "aus"));
            sub.DropDownItems.Add(new ToolStripMenuItem("EQ an", null, (s, e) => { g.EqOn = !g.EqOn; OnGroupFxChanged(g, strip); }) { Checked = g.EqOn });
            sub.DropDownItems.Add(new ToolStripMenuItem("Kompressor an (leise lauter, laute leiser)", null, (s, e) => { g.CompOn = !g.CompOn; OnGroupFxChanged(g, strip); }) { Checked = g.CompOn });
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(Header("EQ-Preset"));
            foreach (var p in EqPreset.BuiltIn.Concat(_settings.EqPresets ?? new List<EqPreset>()))
            {
                var pr = p;
                bool cur = g.EqOn && Math.Abs(g.EqLow - p.Low) < 0.05f && Math.Abs(g.EqMid - p.Mid) < 0.05f && Math.Abs(g.EqHigh - p.High) < 0.05f;
                sub.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (s, e) =>
                {
                    g.EqLow = pr.Low; g.EqMid = pr.Mid; g.EqHigh = pr.High; g.EqOn = true; if (pr.Comp) g.CompOn = true;
                    OnGroupFxChanged(g, strip);
                }) { Checked = cur });
            }
            foreach (var band in new[] { "Bass", "Mitten", "Höhen" })
            {
                string bn = band;
                var bs = Sub(band + ": " + DbStr(bn == "Bass" ? g.EqLow : bn == "Mitten" ? g.EqMid : g.EqHigh));
                foreach (var v in new[] { -9f, -6f, -3f, 0f, 3f, 6f, 9f })
                {
                    float vv = v;
                    float curV = bn == "Bass" ? g.EqLow : bn == "Mitten" ? g.EqMid : g.EqHigh;
                    bs.DropDownItems.Add(new ToolStripMenuItem(DbStr(v), null, (s, e) =>
                    {
                        if (bn == "Bass") g.EqLow = vv; else if (bn == "Mitten") g.EqMid = vv; else g.EqHigh = vv;
                        g.EqOn = true;
                        OnGroupFxChanged(g, strip);
                    }) { Checked = Math.Abs(curV - v) < 0.05f });
                }
                sub.DropDownItems.Add(bs);
            }
            sub.DropDownItems.Add(Hint("Wirkt auf jeden Kanal der Gruppe (zusätzlich zu dessen eigenem Klang)."));
            items.Add(sub);
        }

        private static string DbStr(float v) => (v > 0.05f ? "+" : v < -0.05f ? "−" : "") + Math.Abs(v).ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " dB";

        private void OnGroupFxChanged(GroupConfig g, GroupStrip strip)
        {
            if ((g.EqOn || g.CompOn) && !_snap.EqSupported && !_demo) { MessageBox.Show(this, "EQ und Kompressor für Gruppen brauchen Windows 10 Version 2004 oder neuer.", "Mischpult"); g.EqOn = g.CompOn = false; return; }
            PushPolicy();
            strip?.InvalidateChrome();
            ScheduleSave();
            ShowOsd(g.Name + " · Klang", (g.EqOn ? "EQ " + DbStr(g.EqLow) + " / " + DbStr(g.EqMid) + " / " + DbStr(g.EqHigh) : "EQ aus") + (g.CompOn ? " · Kompressor" : ""), -1, Color.FromArgb(g.Color));
        }

        // ================================================================ Menues

        private void AddChannelKlang400(ToolStripItemCollection items, ChannelConfig c)
        {
            if (!_settings.PeqOn) return;
            int n = c.Peq?.Count(b => !b.Off) ?? 0;
            items.Add(new ToolStripMenuItem("Parametrischer EQ …" + (n > 0 ? "  (" + n + (c.PeqBypass ? ", aus)" : ")") : ""), null, (s, e) => BeginInvoke(new Action(() => ShowChannelPeq(c)))));
        }

        private void AddOutput400Items(ToolStripItemCollection items, string deviceId, string name)
        {
            if (deviceId == null) return;
            var o = OutFxConfig(deviceId, false);
            if (_settings.PeqOn)
            {
                int n = o?.Peq?.Count(b => !b.Off) ?? 0;
                items.Add(new ToolStripMenuItem("Parametrischer EQ …" + (n > 0 ? "  (" + n + ")" : ""), null, (s, e) => BeginInvoke(new Action(() => ShowOutputPeq(deviceId, name)))));
            }
            if (_settings.HpEqOn)
            {
                var sub = Sub("Kopfhörer-Profil: " + (o?.HpBands != null ? o.HpName + (o.HpOff ? " (aus)" : "") : "keins"));
                sub.DropDownItems.Add(new ToolStripMenuItem("Profil laden (AutoEq, ParametricEQ.txt) …", null, (s, e) => BeginInvoke(new Action(() => LoadHpProfile(deviceId, name)))));
                if (o?.HpBands != null)
                {
                    sub.DropDownItems.Add(new ToolStripMenuItem("Profil kurz aus (zum Vergleichen)", null, (s, e) => { o.HpOff = !o.HpOff; PushPolicy(); ScheduleSave(); ShowOsd("Kopfhörer-Profil", o.HpOff ? "aus" : "an", -1, CueColor); }) { Checked = o.HpOff });
                    sub.DropDownItems.Add(new ToolStripMenuItem("Profil entfernen", null, (s, e) => SetHpProfile(deviceId, name, null, null, 0f)));
                }
                sub.DropDownItems.Add(Hint("Profil für deinen Kopfhörer bei autoeq.app wählen → „Equalizer APO“ → ParametricEQ.txt."));
                items.Add(sub);
            }
            if (_settings.HearEqOn && _settings.Hear != null && string.Equals(_settings.Hear.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                items.Add(Hint("Hör-EQ ist auf diesem Ausgang an."));
        }

        // ================================================================ Einstellungen

        private void AddKlang400(SPage p)
        {
            p.Section("4.0 Klang");
            p.Toggle("Parametrischer EQ", "Pro Kanal und Ausgang ein EQ mit bis zu 8 Bändern: Frequenz, Stärke und Güte frei wählbar – Punkte direkt in der Kurve ziehen. Öffnen: Rechtsklick auf einen Kanal bzw. Ausgang → „Parametrischer EQ …“. Läuft durchs Mischpult (wie der EQ).",
                () => _settings.PeqOn, v => { _settings.PeqOn = v; PeqGlobal = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Kopfhörer-Profile (AutoEq)", "Lädt ein Messprofil für deinen Kopfhörer, damit er neutraler klingt. Datei bei autoeq.app holen (Kopfhörer wählen → „Equalizer APO“ → ParametricEQ.txt), dann Rechtsklick auf den Ausgang → „Kopfhörer-Profil“.",
                () => _settings.HpEqOn, v => { _settings.HpEqOn = v; HpEqGlobal = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("EQ und Kompressor für Gruppen", "Eine Gruppe bekommt einen eigenen EQ (Bass, Mitten, Höhen) und einen Kompressor – sie wirken auf alle Kanäle der Gruppe. Einstellen: Rechtsklick auf die Gruppe → „Klang der Gruppe“.",
                () => _settings.GroupFxOn, v => { _settings.GroupFxOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Hör-EQ", () => _settings.Hear == null
                    ? "Nach einem kurzen Hörtest hebt das Pult Töne an, die du schlechter hörst – pro Ohr getrennt. Einschalten startet den Test."
                    : "Gemessen am " + _settings.Hear.Date + " mit " + (_settings.Hear.DeviceName ?? "Kopfhörer") + ". Wirkt nur auf diesem Ausgang (Stärke: " + HearingEq.StrengthNames[_settings.Hear.Strength] + ").",
                () => _settings.HearEqOn, v => SetHearEq(v));
            p.Button("Hörtest", "Kurzer Test mit Tönen (2–4 Minuten). Kein Ersatz für einen Hörtest beim Arzt.", "Starten …", r => ShowHearTest(),
                () => _settings.Hear == null ? "noch nicht gemacht" : "zuletzt " + _settings.Hear.Date);
        }
    }
}
