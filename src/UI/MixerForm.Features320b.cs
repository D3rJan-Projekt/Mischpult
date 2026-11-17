using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.2 – Bedienung: Makros, Countdown, Fader-Kurve, Szenen ueberblenden, Lautstaerke-Deckel, Testton links/rechts,
    /// Ausgaenge umbenennen, Pult sperren, Tempo der Motorfader, Kanaele sortieren, Hintergrund-Apps leiser, Auto-Pause,
    /// mono/getauscht und Verzoegerung pro Kanal. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyFeatures320bStatics(MixerSettings s, bool demo)
        {
            if (!demo) return;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COUNTDOWN") == "1") { s.CountdownOn = true; s.CountdownText = "PAUSE"; s.CountdownMin = 5; }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOCKED") == "1") { s.LockFeatureOn = true; s.PultLocked = true; }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CAPS") == "1" && s.Channels.Count > 2) s.Channels[2].MaxDb = -10f;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SHOTBTN") == "1") s.ShotButtonOn = true;
            string on = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTNAME");
            if (!string.IsNullOrEmpty(on)) { s.OutputNames = new Dictionary<string, string> { { "demo-spk", on } }; }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MACROS") == "1")
            {
                s.MacrosOn = true;
                s.Macros = new List<MacroConfig>
                {
                    new MacroConfig { Id = "m1", Name = "Stream-Start", Steps = new List<string> { "mic.mute", "dim.toggle", "ticker.toggle" } },
                    new MacroConfig { Id = "m2", Name = "Gute Nacht", Steps = new List<string> { "night.toggle", "duck.toggle" } },
                };
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_AUTOSORT"), out int asort)) s.AutoSort = asort;
        }

        private void ShownFeatures320b()
        {
            _top.LockButton = _settings.LockFeatureOn;
            _top.Locked = PultLockedNow;
            _top.LockClicked += ToggleLock;
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COUNTDOWN") == "1") DemoLater(StartCountdown);
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_XFADE") == "1") { _settings.SceneXfade = 3; }
            if (_demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHMENU"), out int cm) && cm >= 1 && cm <= _settings.Channels.Count)
                DemoLater(() =>
                {
                    var strip = _channelStrips.FirstOrDefault(x => x.Config == _settings.Channels[cm - 1]);
                    if (strip != null) ShowChannelMenu(strip, strip.PointToScreen(new Point(Theme.Px(40), Theme.Px(40))));
                });
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTMENU320") == "1")
                DemoLater(() => { var os = _outputStrips.Values.FirstOrDefault(); if (os != null) ShowOutputMenu(os, os.PointToScreen(new Point(Theme.Px(10), Theme.Px(40)))); else ShowDeviceMenu(_master.PointToScreen(new Point(Theme.Px(10), Theme.Px(40)))); });
        }

        private double _next320b;

        private void TickFeatures320b(double now, float dt)
        {
            TickSceneXfade(now);
            TickCountdown(now);
            if (now < _next320b) return;
            _next320b = now + 0.25;
            TickBgApps(now);
            TickAutoPause(now);
            TickAutoSort(now);
        }

        // ================================================================ Makros

        private MacroConfig MacroAt(int i) => _settings.Macros != null && i >= 0 && i < _settings.Macros.Count ? _settings.Macros[i] : null;

        private void RunMacro(MacroConfig m)
        {
            if (m == null || !_settings.MacrosOn) return;
            foreach (var step in m.Steps.ToList())
            {
                if (step.StartsWith("macro:", StringComparison.Ordinal)) continue;   // keine Makros in Makros (keine Schleifen)
                try { ExecuteHotkey(step); } catch { }
            }
            ShowOsd("Makro", m.Name + (m.Steps.Count == 0 ? " (noch leer)" : ""), -1, Theme.Accent);
        }

        private string MacroSteps(MacroConfig m)
        {
            if (m == null || m.Steps.Count == 0) return "Noch keine Schritte – „Ändern ▾“ → Schritt hinzufügen.";
            var labels = HotkeyActions().ToDictionary(a => a.Id, a => a.Label);
            return string.Join(" → ", m.Steps.Select(s => labels.TryGetValue(s, out var l) ? l : "(fehlt)"));
        }

        private void NewMacro()
        {
            var list = _settings.Macros ?? new List<MacroConfig>();
            if (list.Count >= 10) { ShowOsd("Makros", "Höchstens 10 Makros", -1, Theme.SoloAmber); return; }
            using (var dlg = new InputDialog("Neues Makro", "Name (z. B. Stream-Start, Zocken):", "Makro " + (list.Count + 1)))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK || dlg.Value.Length == 0) return;
                list.Add(new MacroConfig { Id = Guid.NewGuid().ToString("N"), Name = dlg.Value, Steps = new List<string>() });
            }
            _settings.Macros = list;
            _settings.MacrosOn = true;
            ScheduleSave();
        }

        private void ShowMacroMenu(int i, Rectangle r)
        {
            var mc = MacroAt(i);
            if (mc == null) return;
            var m = NewMenu();
            m.ShowCheckMargin = false; m.ShowImageMargin = false;
            m.Items.Add(new ToolStripMenuItem("Jetzt ausführen", null, (s, e) => RunMacro(mc)) { Enabled = _settings.MacrosOn && mc.Steps.Count > 0 });
            m.Items.Add(new ToolStripSeparator());
            var add = Sub("Schritt hinzufügen", false);
            foreach (var grp in HotkeyActions().Where(a => !a.Id.StartsWith("macro:", StringComparison.Ordinal)).GroupBy(a => a.Group))
            {
                var g = Sub(grp.Key, false);
                foreach (var a in grp)
                {
                    string id = a.Id;
                    g.DropDownItems.Add(new ToolStripMenuItem(a.Label, null, (s, e) => { if (mc.Steps.Count < 20) mc.Steps.Add(id); ScheduleSave(); }));
                }
                add.DropDownItems.Add(g);
            }
            m.Items.Add(add);
            m.Items.Add(new ToolStripMenuItem("Letzten Schritt entfernen", null, (s, e) => { if (mc.Steps.Count > 0) mc.Steps.RemoveAt(mc.Steps.Count - 1); ScheduleSave(); }) { Enabled = mc.Steps.Count > 0 });
            m.Items.Add(new ToolStripMenuItem("Alle Schritte löschen", null, (s, e) => { mc.Steps.Clear(); ScheduleSave(); }) { Enabled = mc.Steps.Count > 0 });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Makro umbenennen", "Neuer Name:", mc.Name))
                    if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) == DialogResult.OK && dlg.Value.Length > 0) { mc.Name = dlg.Value; ScheduleSave(); }
            }))));
            m.Items.Add(new ToolStripMenuItem("Makro löschen", null, (s, e) =>
            {
                _settings.Macros?.Remove(mc);
                if (_settings.Macros != null && _settings.Macros.Count == 0) _settings.Macros = null;
                _settings.Hotkeys.RemoveAll(h => h.Action == "macro:" + mc.Id);
                ApplyHotkeys();
                ScheduleSave();
            }));
            ShowMenuAt(m, r);
        }

        private void AddMacros320(SPage p)
        {
            p.Section("Makros");
            p.Toggle("Makros", "Ein Knopf oder Tastenkürzel löst mehrere Sachen nacheinander aus – z. B. „Stream-Start“: Mikro an, Laufschrift an, Musik leiser. Tastenkürzel für ein Makro: Hotkeys → Makros.",
                () => _settings.MacrosOn, v => { _settings.MacrosOn = v; ScheduleSave(); });
            for (int i = 0; i < 10; i++)
            {
                int k = i;
                var row = p.Button("Makro " + (i + 1), null, "Ändern ▾", r => ShowMacroMenu(k, r), () => MacroAt(k)?.Name);
                row.HintFunc = () => MacroSteps(MacroAt(k));
                row.Visible = () => MacroAt(k) != null;
                row.Enabled = () => _settings.MacrosOn;
            }
            p.Button("Neues Makro", null, "Anlegen …", r => NewMacro()).Enabled = () => _settings.MacrosOn && (_settings.Macros?.Count ?? 0) < 10;
        }

        // ================================================================ Countdown

        private double _cdEnd = -1, _cdDoneUntil = -1;

        private int CountdownMinutes => _settings.CountdownMin ?? 5;
        private string CountdownLabel => string.IsNullOrWhiteSpace(_settings.CountdownText) ? "PAUSE" : _settings.CountdownText.Trim().ToUpperInvariant();
        private bool CountdownRunning => _cdEnd > 0;

        private void StartCountdown()
        {
            if (!_settings.CountdownOn) return;
            _cdEnd = Now + CountdownMinutes * 60;
            _cdDoneUntil = -1;
            ShowOsd("Countdown", CountdownLabel + " · " + CountdownMinutes + " min", -1, Theme.SoloAmber);
        }

        private void StopCountdown()
        {
            _cdEnd = -1; _cdDoneUntil = -1;
            StripBase.SetBanner(null, 0, 0);
            if (_top.CountdownLeft != -1) { _top.CountdownLeft = -1; _top.Invalidate(); }
        }

        private void ToggleCountdown() { if (CountdownRunning || _cdDoneUntil > 0) StopCountdown(); else StartCountdown(); }

        private void TickCountdown(double now)
        {
            if (_cdEnd <= 0 && _cdDoneUntil <= 0) return;
            if (!_settings.CountdownOn) { StopCountdown(); return; }
            int left;
            if (_cdEnd > 0)
            {
                left = (int)Math.Ceiling(Math.Max(0, _cdEnd - now));
                if (left <= 0)
                {
                    _cdEnd = -1;
                    _cdDoneUntil = now + 6;   // "0:00" noch kurz stehen lassen
                    if (!_settings.CountdownBeepOff)
                    {
                        try
                        {
                            if (_snap.EqSupported || _demo) Backend.PlaySound(Beeps.Chime, 0.3f, true, false, false);
                            else if (Win32.IsWindows) { _beepPlayer?.Dispose(); _beepPlayer = new System.Media.SoundPlayer(new System.IO.MemoryStream(Beeps.ToWav(Beeps.Chime, 0.3f))); _beepPlayer.Play(); }
                        }
                        catch { }
                    }
                    ShowOsd("Countdown", CountdownLabel + " ist vorbei", -1, Theme.SoloAmber);
                }
            }
            else
            {
                left = 0;
                if (now >= _cdDoneUntil) { StopCountdown(); return; }
            }
            if (_top.CountdownLeft != left) { _top.CountdownLeft = left; _top.Invalidate(); }
            if (!_settings.CountdownDisplaysOff)
            {
                try
                {
                    var area = RectangleToScreen(ClientRectangle);
                    StripBase.SetBanner(CountdownLabel + " " + (left / 60) + ":" + (left % 60).ToString("00"), area.Left, area.Right);
                }
                catch { }
            }
            else StripBase.SetBanner(null, 0, 0);
        }

        private void AddCountdown320(SPage p)
        {
            p.Toggle("Countdown", "Ein Countdown wie „PAUSE 4:59“ läuft in der Uhr oben und in den Anzeigen aller Kanäle – z. B. für eine Stream-Pause. Starten und stoppen mit dem Knopf darunter oder einem Tastenkürzel.",
                () => _settings.CountdownOn, v => { _settings.CountdownOn = v; if (!v) StopCountdown(); ScheduleSave(); });
            p.Button("Text des Countdowns", null, "Ändern …", r => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Countdown", "Text vor der Zeit (kurz, z. B. PAUSE, START, ESSEN):", CountdownLabel, 12))
                    if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) == DialogResult.OK) { _settings.CountdownText = dlg.Value; ScheduleSave(); }
            })), () => CountdownLabel).Enabled = () => _settings.CountdownOn;
            p.Choice("Dauer", () => "So lange läuft der Countdown.", MixerSettings.CountdownMinutes.Select(v => v + " min").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.CountdownMinutes, CountdownMinutes)), i => { _settings.CountdownMin = MixerSettings.CountdownMinutes[i]; ScheduleSave(); })
                .Enabled = () => _settings.CountdownOn;
            p.Button("Countdown", null, "Starten / Stoppen", r => ToggleCountdown(), () => CountdownRunning ? "läuft" : "steht").Enabled = () => _settings.CountdownOn;
            p.Toggle("Countdown in den Anzeigen der Kanäle", "Die Zeit steht in jeder Anzeige über den Fadern. Aus = nur oben in der Uhr.",
                () => !_settings.CountdownDisplaysOff, v => { _settings.CountdownDisplaysOff = !v; if (!v) StripBase.SetBanner(null, 0, 0); ScheduleSave(); }).Enabled = () => _settings.CountdownOn;
            p.Toggle("Gong am Ende", "Kurzer Dreiklang, wenn der Countdown abgelaufen ist – nur für dich.",
                () => !_settings.CountdownBeepOff, v => { _settings.CountdownBeepOff = !v; ScheduleSave(); }).Enabled = () => _settings.CountdownOn;
        }

        // ================================================================ Fader-Kurve

        private void SetFaderCurve(int curve)
        {
            int old = FaderLaw.Curve;
            curve = Math.Max(0, Math.Min(FaderLaw.CurveNames.Length - 1, curve));
            if (curve == old) return;
            float conv(float p) => FaderLaw.Convert(p, old, curve);
            // gespeicherte Stellungen so umrechnen, dass die Lautstaerke gleich bleibt
            foreach (var c in _settings.Channels) { c.Fader = conv(c.Fader); if (c.FaderMark.HasValue) c.FaderMark = conv(c.FaderMark.Value); }
            foreach (var g in _settings.Groups) { g.Fader = conv(g.Fader); if (g.FaderMark.HasValue) g.FaderMark = conv(g.FaderMark.Value); }
            foreach (var sc in _settings.Scenes) foreach (var ch in sc.Channels) ch.Fader = conv(ch.Fader);
            foreach (var v in _settings.AppVolumes) v.Pos = conv(v.Pos);
            foreach (var sp in _settings.Sounds) sp.Volume = Math.Max(0.01f, conv(sp.Volume));
            FaderLaw.SetCurve(curve);
            _settings.FaderCurve = curve;
            foreach (var s in AllStrips()) s.InvalidateChrome();
            _mini?.Invalidate();
            PushPolicy();
            ScheduleSave();
        }

        private void AddFaderCurve320(SPage p)
        {
            p.Choice("Fader-Kurve", () => _settings.FaderCurve == 1 ? "Fein oben: mehr Weg im lauten Bereich (−20 … 0 dB) – feiner einstellen, wo man meistens hört."
                    : _settings.FaderCurve == 2 ? "Gleichmäßig: jeder Zentimeter Fader ist gleich viel dB (−60 … 0 dB)."
                    : _settings.FaderCurve == 3 ? "Fein unten: mehr Weg bei leisem Ton – gut, wenn du eher leise hörst."
                    : "Standard: wie an einem Mischpult (oben fein, unten grob). Beim Umschalten bleibt die Lautstärke gleich – nur die Fader stehen anders.",
                FaderLaw.CurveNames, () => _settings.FaderCurve, i => SetFaderCurve(i));
        }

        // ================================================================ Szenen ueberblenden

        private sealed class Xfade
        {
            public float From, To;
            public bool Group;
        }

        private readonly Dictionary<object, Xfade> _xf = new Dictionary<object, Xfade>();
        private double _xfStart = -1, _xfLen;

        private float XfadeSeconds => _xfOverride >= 0f ? _xfOverride   // 3.5: eigene Zeit der Szene
            : MixerSettings.SceneXfadeValues[Math.Max(0, Math.Min(MixerSettings.SceneXfadeValues.Length - 1, _settings.SceneXfade))];

        /// <summary>Vor dem Laden einer Szene: wo stehen die Fader gerade (wirksam, stumm = 0)?</summary>
        private Dictionary<object, float> CaptureXfadeFrom()
        {
            if (XfadeSeconds <= 0f) return null;
            var d = new Dictionary<object, float>();
            foreach (var c in _settings.Channels) d[c] = XfadeMute(c) ? 0f : XfadePos(c);
            foreach (var g in _settings.Groups) d[g] = XfadeGroupMute(g) ? 0f : XfadeGroupPos(g);
            return d;
        }

        /// <summary>Nach dem Laden: weich von den alten zu den neuen Stellungen. Liefert die Dauer (0 = kein Ueberblenden).</summary>
        private float StartSceneXfade(Dictionary<object, float> from)
        {
            _xf.Clear();
            _xfStart = -1;
            if (from == null) return 0f;
            float sec = XfadeSeconds;
            foreach (var c0 in _settings.Channels) ClampToCap(c0);
            foreach (var kv in from)
            {
                float to = kv.Key is ChannelConfig c ? (c.Mute ? 0f : c.Fader) : kv.Key is GroupConfig g ? (g.Mute ? 0f : g.Fader) : -1f;
                if (to < 0f || Math.Abs(to - kv.Value) < 0.002f) continue;
                _xf[kv.Key] = new Xfade { From = kv.Value, To = to, Group = kv.Key is GroupConfig };
            }
            if (_xf.Count == 0) return 0f;
            _xfStart = Now; _xfLen = sec;
            return sec;
        }

        private float XfadeT()
        {
            if (_xfStart < 0 || _xfLen <= 0) return 1f;
            float u = (float)Math.Min(1.0, (Now - _xfStart) / _xfLen);
            return u * u * (3f - 2f * u);   // sanft anfahren und abbremsen (wie die Motorfader)
        }

        /// <summary>Wirksame Faderstellung des Kanals (beim Ueberblenden die Zwischenstellung).</summary>
        private float XfadePos(ChannelConfig c)
        {
            if (_xf.Count == 0 || !_xf.TryGetValue(c, out var x)) return c.Fader;
            float p = x.From + (x.To - x.From) * XfadeT();
            float cap = CapPosOf(c);   // auch beim Ueberblenden nie ueber den Deckel
            return float.IsNaN(cap) ? p : Math.Min(p, cap);
        }
        private bool XfadeMute(ChannelConfig c) => _xf.Count > 0 && _xf.ContainsKey(c) ? false : c.Mute;
        private float XfadeGroupPos(GroupConfig g) => _xf.Count > 0 && _xf.TryGetValue(g, out var x) ? x.From + (x.To - x.From) * XfadeT() : g.Fader;
        private bool XfadeGroupMute(GroupConfig g) => _xf.Count > 0 && _xf.ContainsKey(g) ? false : g.Mute;

        private double _xfNextPush;

        private void TickSceneXfade(double now)
        {
            if (_xf.Count == 0) return;
            bool done = now - _xfStart >= _xfLen;
            if (done) { _xf.Clear(); _xfStart = -1; }
            else if (now < _xfNextPush) return;
            _xfNextPush = now + 0.03;   // etwa 30x pro Sekunde reicht (die Engine blendet dazwischen weich)
            PushPolicy();
        }

        private void AddSceneXfade320(SPage p)
        {
            p.Choice("Szenen weich überblenden", () => XfadeSeconds <= 0f ? "Aus: eine Szene ist sofort da (Standard)."
                    : "Beim Laden einer Szene gleiten alle Kanäle in " + XfadeSeconds + " s zur neuen Lautstärke – auch per Hotkey, Uhrzeit und Profil pro Spiel. Mit Motorfadern fahren die Fader genauso lange.",
                MixerSettings.SceneXfadeValues.Select(v => v == 0 ? "Aus" : v + " s").ToArray(), () => _settings.SceneXfade,
                i => { _settings.SceneXfade = i; ScheduleSave(); });
        }

        // ================================================================ Lautstaerke-Deckel

        /// <summary>Hoechste erlaubte Faderstellung des Kanals (NaN = kein Deckel).</summary>
        public float CapPosOf(ChannelConfig c) => _settings == null || _settings.CapsOff || c?.MaxDb == null ? float.NaN : FaderLaw.DbToPos(c.MaxDb.Value);

        private void ClampToCap(ChannelConfig c)
        {
            float cap = CapPosOf(c);
            if (!float.IsNaN(cap) && c.Fader > cap + 0.0005f) c.Fader = cap;
        }

        private void SetChannelCap(ChannelConfig c, float? db)
        {
            c.MaxDb = db;
            if (db.HasValue) _settings.CapsOff = false;
            ClampToCap(c);
            OnChannelChanged(c, false);
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
            if (db.HasValue) ShowOsd("Lautstärke-Deckel · " + c.Name, "höchstens " + StripBase.DbText(db.Value), -1, Theme.MeterRed);
        }

        private ToolStripMenuItem CapSub(ChannelConfig c)
        {
            var sub = Sub("Lautstärke-Deckel: " + (c.MaxDb.HasValue ? StripBase.DbText(c.MaxDb.Value) : "keiner"));
            sub.DropDownItems.Add(new ToolStripMenuItem("Kein Deckel", null, (s, e) => SetChannelCap(c, null)) { Checked = !c.MaxDb.HasValue });
            foreach (var v in MixerSettings.CapValues)
            {
                float vv = v;
                sub.DropDownItems.Add(new ToolStripMenuItem("Höchstens " + StripBase.DbText(v), null, (s, e) => SetChannelCap(c, vv)) { Checked = c.MaxDb.HasValue && Math.Abs(c.MaxDb.Value - v) < 0.01f });
            }
            sub.DropDownItems.Add(Hint("Der Fader geht nie höher – roter Strich am Fader."));
            return sub;
        }

        private void AddCaps320(SPage p)
        {
            p.Toggle("Lautstärke-Deckel", () =>
                {
                    int n = _settings.Channels.Count(c => c.MaxDb.HasValue);
                    return "Ein Kanal wird nie lauter als sein Deckel – auch nicht per MIDI, Hotkey oder Szene. Einstellen per Rechtsklick auf den Kanal → Lautstärke-Deckel." + (n > 0 ? " Gerade " + n + " Kanal/Kanäle mit Deckel." : "");
                },
                () => !_settings.CapsOff, v => { _settings.CapsOff = !v; RefreshStrips(); PushPolicy(); ScheduleSave(); });
        }

        // ================================================================ Testton links/rechts

        private Timer _testToneTimer;

        private void PlayTestTone(string deviceId, string name)
        {
            if (!_settings.TestToneOn) return;
            var def = DefaultDevice;
            string dev = deviceId == null || (def != null && string.Equals(def.Id, deviceId, StringComparison.OrdinalIgnoreCase)) ? null : deviceId;
            string title = "Testton · " + (name ?? def?.Name ?? "Ausgang");
            try
            {
                if (_snap.EqSupported || _demo) Backend.PlaySoundOn(Beeps.TestLR, 0.3f, dev);
                else if (Win32.IsWindows && dev == null) { _beepPlayer?.Dispose(); _beepPlayer = new System.Media.SoundPlayer(new System.IO.MemoryStream(Beeps.ToWav(Beeps.TestLR, 0.3f))); _beepPlayer.Play(); }
                else { ShowOsd(title, "Geht nur mit Windows 10 Version 2004 oder neuer", -1, Theme.MuteRed); return; }
            }
            catch { return; }
            ShowOsd(title, "◀  LINKS", -1, Theme.Accent);
            _testToneTimer?.Dispose();
            _testToneTimer = new Timer { Interval = (int)(Beeps.TestRightAt * 1000) };
            _testToneTimer.Tick += (s, e) => { _testToneTimer.Stop(); ShowOsd(title, "RECHTS  ▶", -1, Theme.Accent); };
            _testToneTimer.Start();
        }

        private void ShowTestToneMenu(Rectangle r)
        {
            var m = NewMenu();
            m.ShowCheckMargin = false; m.ShowImageMargin = false;
            foreach (var d in _snap.Devices)
            {
                var id = d.Id; var name = d.Name;
                m.Items.Add(new ToolStripMenuItem(d.FullName + (d.IsDefault ? "  (Standard)" : ""), null, (s, e) => PlayTestTone(id, name)));
            }
            if (_snap.Devices.Length == 0) m.Items.Add(Hint("Kein Ausgabegerät gefunden"));
            ShowMenuAt(m, r);
        }

        private void AddTestTone320(SPage p)
        {
            p.Toggle("Testton links/rechts", "Ein kurzer Ton erst nur links, dann nur rechts – so prüfst du, ob Lautsprecher oder Headset richtig herum angeschlossen sind. Abspielen mit dem Knopf darunter oder per Rechtsklick auf einen Ausgang.",
                () => _settings.TestToneOn, v => { _settings.TestToneOn = v; ScheduleSave(); });
            p.Button("Testton abspielen", null, "Gerät wählen ▾", r => ShowTestToneMenu(r)).Enabled = () => _settings.TestToneOn;
        }

        // ================================================================ Ausgaenge umbenennen

        private EngineSnapshot _namedSnap;
        private readonly Dictionary<string, KeyValuePair<string, string>> _origDevNames = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);

        private string OutputNameOf(string id) =>
            _settings.OutputNamesOff || _settings.OutputNames == null || id == null ? null : _settings.OutputNames.TryGetValue(id, out var n) ? n : null;

        /// <summary>Eigene Namen in den neuen Schnappschuss eintragen (jeder Schnappschuss nur einmal).</summary>
        private void ApplyOutputNames(EngineSnapshot snap)
        {
            if (ReferenceEquals(snap, _namedSnap) || snap?.Devices == null) return;
            _namedSnap = snap;
            foreach (var d in snap.Devices)
            {
                if (d?.Id == null) continue;
                _origDevNames[d.Id] = new KeyValuePair<string, string>(d.Name, d.FullName);
                string n = OutputNameOf(d.Id);
                if (n == null) continue;
                string full = d.FullName ?? d.Name ?? "";
                int par = full.IndexOf('(');
                d.FullName = par > 0 ? n + " " + full.Substring(par) : n + " (" + full + ")";   // "Headset (USB Audio Device)"
                d.Name = n;
            }
        }

        private string OrigFullName(string id) => id != null && _origDevNames.TryGetValue(id, out var kv) ? kv.Value : null;
        private string OrigName(string id) => id != null && _origDevNames.TryGetValue(id, out var kv) ? kv.Key : null;

        private void RenameOutput(string id)
        {
            if (id == null) return;
            string orig = OrigName(id) ?? "Ausgang";
            using (var dlg = new InputDialog("Ausgang umbenennen", "Name für „" + (OrigFullName(id) ?? orig) + "“ (leer = wie in Windows):", OutputNameOf(id) ?? orig, 32))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                string v = dlg.Value;
                if (_settings.OutputNames == null) _settings.OutputNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(v) || v == orig) _settings.OutputNames.Remove(id); else _settings.OutputNames[id] = v;
                if (_settings.OutputNames.Count == 0) _settings.OutputNames = null;
                _settings.OutputNamesOff = false;
                _namedSnap = null;
                ScheduleSave();
            }
        }

        private void ShowRenameOutputsMenu(Rectangle r)
        {
            var m = NewMenu();
            m.ShowCheckMargin = false; m.ShowImageMargin = false;
            foreach (var d in _snap.Devices)
            {
                var id = d.Id;
                m.Items.Add(new ToolStripMenuItem((OrigFullName(id) ?? d.FullName) + (OutputNameOf(id) != null ? "  →  " + OutputNameOf(id) : ""), null, (s, e) => BeginInvoke(new Action(() => RenameOutput(id)))));
            }
            if (_snap.Devices.Length == 0) m.Items.Add(Hint("Kein Ausgabegerät gefunden"));
            ShowMenuAt(m, r);
        }

        private void AddOutputNames320(SPage p)
        {
            p.Toggle("Eigene Namen der Ausgänge", "Z. B. „Headset“ statt „Lautsprecher (Realtek(R) Audio)“ – im MASTER, an den Ausgängen und in allen Menüs. Umbenennen mit dem Knopf darunter oder per Rechtsklick auf einen Ausgang. Aus = Namen wie in Windows (deine bleiben gespeichert).",
                () => !_settings.OutputNamesOff, v => { _settings.OutputNamesOff = !v; _namedSnap = null; ScheduleSave(); });
            p.Button("Ausgänge umbenennen", null, "Wählen ▾", r => ShowRenameOutputsMenu(r), () => { int n = _settings.OutputNames?.Count ?? 0; return n == 0 ? "keine" : n + " umbenannt"; })
                .Enabled = () => !_settings.OutputNamesOff;
        }

        // ================================================================ Pult sperren

        private bool PultLockedNow => _settings.LockFeatureOn && _settings.PultLocked;
        private double _lockNoteAt = -10;

        private void SetLocked(bool on)
        {
            _settings.PultLocked = on;
            if (on) _settings.LockFeatureOn = true;
            _top.LockButton = _settings.LockFeatureOn;
            _top.Locked = PultLockedNow;
            _top.Invalidate();
            ShowOsd("Pult", on ? "gesperrt" : "entsperrt", -1, on ? Theme.SoloAmber : Theme.LedGreen);
            ScheduleSave();
        }

        private void ToggleLock() { if (_settings.LockFeatureOn || !_settings.PultLocked) SetLocked(!PultLockedNow); }

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);

        /// <summary>Faengt Klicks und Mausrad auf Kanaelen, Ausgaengen und Meterbridge ab, solange das Pult gesperrt ist.</summary>
        private sealed class LockFilter : IMessageFilter
        {
            private readonly MixerForm _f;
            public LockFilter(MixerForm f) { _f = f; }
            public bool PreFilterMessage(ref Message m) => _f.FilterLocked(ref m);
        }

        private bool FilterLocked(ref Message m)
        {
            int msg = m.Msg;
            bool wheel = msg == 0x020A || msg == 0x020E;
            bool button = msg >= 0x0201 && msg <= 0x0209;
            if ((!wheel && !button) || !PultLockedNow || _closing) return false;
            Control under = null;
            try
            {
                var pt = Cursor.Position;
                if (Win32.IsWindows) under = Control.FromChildHandle(WindowFromPoint(pt));
                if (under == null && !wheel) under = Control.FromHandle(m.HWnd);
            }
            catch { under = wheel ? null : Control.FromHandle(m.HWnd); }
            if (!InLockedArea(under)) return false;
            if (msg == 0x0201 || msg == 0x0204 || msg == 0x020A)
            {
                double now = Now;
                if (now - _lockNoteAt > 1.5) { _lockNoteAt = now; ShowOsd("Pult gesperrt", "Schloss oben anklicken zum Entsperren", -1, Theme.SoloAmber); }
            }
            return true;
        }

        private bool InLockedArea(Control c)
        {
            for (var x = c; x != null; x = x.Parent)
            {
                if (x == _top) return false;
                if (x == _channelHost || x == _outputHost || x == _bridge) return true;
                if (x == this) return false;
            }
            return false;
        }

        private void AddLock320(SPage p)
        {
            p.Section("Pult sperren");
            p.Toggle("Pult sperren (Schloss oben)", "Oben neben den Musik-Tasten erscheint ein Schloss. Gesperrt tun Klicks und Mausrad auf Fadern, Tasten und Meterbridge nichts – nichts verstellt sich aus Versehen. Tastenkürzel und MIDI gehen weiter.",
                () => _settings.LockFeatureOn, v => { _settings.LockFeatureOn = v; if (!v) _settings.PultLocked = false; _top.LockButton = v; _top.Locked = PultLockedNow; _top.Invalidate(); ScheduleSave(); });
            p.Toggle("Jetzt gesperrt", "Auch mit dem Schloss oben oder einem Tastenkürzel.", () => PultLockedNow, v => SetLocked(v)).Enabled = () => _settings.LockFeatureOn;
        }

        // ================================================================ Tempo der Motorfader

        private static float MotorFactor(int speed) => speed == 1 ? 2f : speed == 2 ? 0.5f : 1f;
        private static readonly string[] MotorSpeedNames = { "Langsam", "Normal", "Schnell" };
        private int MotorSpeedChoice => _settings.MotorSpeed == 1 ? 0 : _settings.MotorSpeed == 2 ? 2 : 1;

        private void AddMotorSpeed320(SPage p)
        {
            p.Choice("Tempo der Motorfader", () => MotorSpeedChoice == 1 ? "Normal (Standard): ganz hoch oder runter in gut einer halben Sekunde." : MotorSpeedChoice == 0 ? "Langsam: doppelt so lange – schön zum Zusehen." : "Schnell: halb so lange.",
                MotorSpeedNames, () => MotorSpeedChoice, i => { _settings.MotorSpeed = i == 0 ? 1 : i == 2 ? 2 : 0; StripBase.MotorSpeedFactor = MotorFactor(_settings.MotorSpeed); ScheduleSave(); })
                .Enabled = () => !_settings.MotorOff;
        }

        // ================================================================ Kanaele sortieren

        private readonly Dictionary<string, double> _lastPlayed = new Dictionary<string, double>();
        private double _nextSort;

        private static float HueOf(int argb) { var c = Color.FromArgb(argb); return c.GetSaturation() < 0.12f ? 400f + c.GetBrightness() : c.GetHue(); }

        /// <summary>Kanaele neu ordnen; SYSTEM (Sammelkanal) behaelt seinen Platz.</summary>
        private bool SortChannels(Func<List<ChannelConfig>, List<ChannelConfig>> order)
        {
            var list = _settings.Channels;
            int ca = list.FindIndex(c => c.IsCatchAll);
            var others = list.Where(c => !c.IsCatchAll).ToList();
            var sorted = order(others);
            if (ca >= 0) sorted.Insert(Math.Min(ca, sorted.Count), list[ca]);
            if (sorted.SequenceEqual(list)) return false;
            _settings.Channels = sorted;
            _channelHost.AnimateNextLayout();
            BuildChannelStrips();
            ScheduleSave();
            return true;
        }

        private void SortNow(int mode)
        {
            if (mode == 0) SortChannels(l => l.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            else if (mode == 1) SortChannels(l => l.OrderByDescending(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            else SortChannels(l => l.OrderBy(c => HueOf(c.Color)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        }

        private void TickAutoSort(double now)
        {
            foreach (var c in _settings.Channels)
                if (_channelPeak.TryGetValue(c.Id, out var pk) && Math.Max(pk[2], pk[3]) > 0.0018f) _lastPlayed[c.Id] = now;   // ab etwa −55 dB
            if (_settings.AutoSort == 0 || now < _nextSort) return;
            _nextSort = now + 2;
            if (Control.MouseButtons != MouseButtons.None || ChannelStrip.Renaming != null) return;   // nicht mitten im Ziehen umsortieren
            switch (_settings.AutoSort)
            {
                case 1: SortChannels(l => l.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList()); break;
                case 2: SortChannels(l => l.OrderBy(c => HueOf(c.Color)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList()); break;
                case 3:
                    // spielende zuerst (bleiben 8 s "spielend"), sonst die bisherige Reihenfolge
                    SortChannels(l => l.Select((c, i) => new { c, i, on = _lastPlayed.TryGetValue(c.Id, out var t) && now - t < 8 })
                                       .OrderBy(x => x.on ? 0 : 1).ThenBy(x => x.i).Select(x => x.c).ToList());
                    break;
            }
        }

        private static readonly string[] AutoSortNames = { "Aus", "Nach Name", "Nach Farbe", "Spielende zuerst" };

        private void AddSort320(SPage p)
        {
            p.Button("Kanäle sortieren", "Einmal neu ordnen – SYSTEM bleibt, wo es ist.", "Sortieren ▾", r =>
            {
                var m = NewMenu(); m.ShowCheckMargin = false; m.ShowImageMargin = false;
                m.Items.Add(new ToolStripMenuItem("Nach Name (A–Z)", null, (s, e) => SortNow(0)));
                m.Items.Add(new ToolStripMenuItem("Nach Name (Z–A)", null, (s, e) => SortNow(1)));
                m.Items.Add(new ToolStripMenuItem("Nach Farbe", null, (s, e) => SortNow(2)));
                ShowMenuAt(m, r);
            });
            p.Choice("Automatisch sortieren", () => _settings.AutoSort == 3 ? "Kanäle, die gerade spielen, rücken nach vorne (gleiten weich) – nach 8 s Stille wieder an ihren Platz in der Reihe."
                    : _settings.AutoSort == 0 ? "Aus: die Reihenfolge bleibt, wie du sie ziehst (Standard)." : "Die Kanäle bleiben immer sortiert – auch neue.",
                AutoSortNames, () => _settings.AutoSort, i => { _settings.AutoSort = i; _nextSort = 0; ScheduleSave(); });
        }

        // ================================================================ Hintergrund-Apps leiser

        private readonly HashSet<string> _bgApps = new HashSet<string>();

        private float BgAppsDbFor(ChannelConfig c) => _bgApps.Count > 0 && _bgApps.Contains(c.Id) ? (_settings.BgAppsQuietDb ?? -10f) : 0f;
        private bool BgAppsQuiet(ChannelConfig c) => _bgApps.Count > 0 && _bgApps.Contains(c.Id);

        private void TickBgApps(double now)
        {
            var want = new HashSet<string>();
            if (_settings.BgAppsQuietOn && _settings.Channels.Any(c => c.BgQuiet))
            {
                string fgKey = null; bool own = false, known = false;
                if (_demo) known = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BGAPPS") == "1";
                else if (Win32.IsWindows)
                {
                    uint pid = Win32.ForegroundPid();
                    own = pid == OwnPid || (_mini != null && _mini.Visible && _mini.ContainsFocus);
                    if (pid != 0 && !own) { fgKey = KeyOfPid(pid); known = true; }
                }
                if (own) want.UnionWith(_bgApps);   // am Pult selbst: nichts aendern
                else if (known)
                    foreach (var c in _settings.Channels)
                    {
                        if (!c.BgQuiet) continue;
                        bool playing = _channelPeak.TryGetValue(c.Id, out var pk) && Math.Max(pk[2], pk[3]) > 0.0005f;
                        bool background = fgKey == null || ChannelOf(fgKey) != c;
                        if (playing && background) want.Add(c.Id);
                        else if (_bgApps.Contains(c.Id) && background && _lastPlayed.TryGetValue(c.Id, out var t) && now - t < 2) want.Add(c.Id);   // kurze Pausen: bleibt leiser
                    }
            }
            if (want.SetEquals(_bgApps)) return;
            _bgApps.Clear();
            _bgApps.UnionWith(want);
            PushPolicy();
            foreach (var s in _channelStrips) s.InvalidateChrome();
        }

        private static readonly float[] BgAppsSteps = { -6f, -10f, -15f, -20f, -30f };

        private void AddBgApps320(SPage p)
        {
            p.Section("Hintergrund und Pause");
            p.Toggle("Hintergrund-Apps leiser", () => "Kanäle mit Haken werden leiser, solange ihre App nicht im Vordergrund ist – z. B. ein Video im Browser, während du in einem anderen Fenster arbeitest. Haken setzen per Rechtsklick auf den Kanal oder mit dem Knopf darunter."
                    + (_settings.Channels.Any(c => c.BgQuiet) ? " Gerade: " + string.Join(", ", _settings.Channels.Where(c => c.BgQuiet).Select(c => c.Name)) + "." : ""),
                () => _settings.BgAppsQuietOn, v => { _settings.BgAppsQuietOn = v; ScheduleSave(); });
            p.Choice("So viel leiser", () => "Im Hintergrund wird der Kanal um so viel leiser.", BgAppsSteps.Select(d => StripBase.DbText(d)).ToArray(),
                () => Math.Max(0, Array.IndexOf(BgAppsSteps, _settings.BgAppsQuietDb ?? -10f)), i => { _settings.BgAppsQuietDb = BgAppsSteps[i]; PushPolicy(); ScheduleSave(); })
                .Enabled = () => _settings.BgAppsQuietOn;
            p.Button("Welche Kanäle", null, "Wählen ▾", r =>
            {
                var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
                foreach (var c in _settings.Channels)
                {
                    var ch = c;
                    m.Items.Add(new ToolStripMenuItem(c.Name, null, (s, e) => { ch.BgQuiet = !ch.BgQuiet; ScheduleSave(); }) { Checked = c.BgQuiet });
                }
                ShowMenuAt(m, r);
            }, () => { int n = _settings.Channels.Count(c => c.BgQuiet); return n == 0 ? "keine" : n + " Kanal/Kanäle"; }).Enabled = () => _settings.BgAppsQuietOn;
        }

        // ================================================================ Auto-Pause

        private double _apLoudFor, _apQuietFor;
        private bool _apPaused, _apBusy;
        private string[] _apMusicKeys;

        private ChannelConfig ChannelById(string id) => id == null ? null : _settings.Channels.FirstOrDefault(c => c.Id == id);

        private void TickAutoPause(double now)
        {
            var trig = ChannelById(_settings.AutoPauseTrigger);
            var music = ChannelById(_settings.AutoPauseMusic);
            if (!_settings.AutoPauseOn || trig == null || music == null || trig == music)
            {
                if (_apPaused && !_apBusy) ResumeMusic(false);
                _apLoudFor = _apQuietFor = 0;
                return;
            }
            float tl = _channelPeak.TryGetValue(trig.Id, out var tp) ? Math.Max(tp[2], tp[3]) : 0f;
            float ml = _channelPeak.TryGetValue(music.Id, out var mp) ? Math.Max(mp[2], mp[3]) : 0f;
            bool trigOn = tl > 0.006f;   // etwa −45 dB
            if (trigOn) { _apLoudFor += 0.25; _apQuietFor = 0; } else { _apQuietFor += 0.25; _apLoudFor = 0; }
            if (_apBusy) return;
            if (!_apPaused && _apLoudFor >= 1.0 && ml > 0.003f)
            {
                var keys = music.Apps.ToArray();
                if (keys.Length == 0) return;
                _apBusy = true;
                _apMusicKeys = keys;
                string tname = trig.Name;
                Task.Run(() => NowPlayingWatcher.ControlApp(keys, true)).ContinueWith(t =>
                {
                    try { BeginInvoke(new Action(() => { _apBusy = false; if (t.Result) { _apPaused = true; ShowOsd("Auto-Pause", "Musik pausiert – " + tname + " spielt", -1, Theme.SoloAmber); } })); } catch { }
                });
            }
            else if (_apPaused)
            {
                if (ml > 0.003f && !trigOn) { _apPaused = false; return; }   // Musik laeuft schon wieder (selbst gestartet)
                if (_apQuietFor >= 3.0) ResumeMusic(true);
            }
        }

        private void ResumeMusic(bool osd)
        {
            var keys = _apMusicKeys;
            _apPaused = false;
            if (keys == null) return;
            _apBusy = true;
            Task.Run(() => NowPlayingWatcher.ControlApp(keys, false)).ContinueWith(t =>
            {
                try { BeginInvoke(new Action(() => { _apBusy = false; if (osd && t.Result) ShowOsd("Auto-Pause", "Musik läuft weiter", -1, Theme.LedGreen); })); } catch { }
            });
        }

        private void ShowChannelPick(Rectangle r, Func<string> get, Action<string> set)
        {
            var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = true;
            foreach (var c in _settings.Channels.Where(x => !x.IsCatchAll))
            {
                var ch = c;
                m.Items.Add(new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color)), (s, e) => { set(ch.Id); ScheduleSave(); }) { Checked = get() == c.Id });
            }
            ShowMenuAt(m, r);
        }

        private void AddAutoPause320(SPage p)
        {
            p.Toggle("Auto-Pause", "Fängt im Auslöser-Kanal etwas an zu spielen (z. B. ein Video im Browser), pausiert die Musik im Musik-Kanal – und läuft weiter, wenn das Video 3 Sekunden still ist. Über die Windows-Mediensteuerung (Spotify, Media Player & Co.).",
                () => _settings.AutoPauseOn, v => { _settings.AutoPauseOn = v; ScheduleSave(); });
            p.Button("Auslöser-Kanal", "Wenn hier Ton kommt …", "Wählen ▾", r => ShowChannelPick(r, () => _settings.AutoPauseTrigger, id => _settings.AutoPauseTrigger = id),
                () => ChannelById(_settings.AutoPauseTrigger)?.Name ?? "keiner").Enabled = () => _settings.AutoPauseOn;
            p.Button("Musik-Kanal", "… pausiert die App in diesem Kanal.", "Wählen ▾", r => ShowChannelPick(r, () => _settings.AutoPauseMusic, id => _settings.AutoPauseMusic = id),
                () => ChannelById(_settings.AutoPauseMusic)?.Name ?? "keiner").Enabled = () => _settings.AutoPauseOn;
        }

        // ================================================================ Mono / getauscht, Verzoegerung pro Kanal

        private int StereoOf(ChannelConfig c) => _settings.ChannelSoundOff ? 0 : c.Stereo;
        private int DelayOf(ChannelConfig c) => _settings.ChannelSoundOff ? 0 : c.DelayMs;
        private static readonly string[] StereoNames = { "Stereo (normal)", "Mono", "Links/rechts getauscht" };

        private void SetChannelStereo(ChannelConfig c, int mode)
        {
            if (mode != 0 && !_snap.EqSupported && !_demo) { MessageBox.Show(this, "Mono und Tauschen brauchen Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            c.Stereo = Math.Max(0, Math.Min(2, mode));
            if (mode != 0) _settings.ChannelSoundOff = false;
            OnChannelEdited(c);
            ShowOsd("Ton · " + c.Name, StereoNames[c.Stereo], -1, CueColor);
        }

        private void SetChannelDelay(ChannelConfig c, int ms)
        {
            if (ms > 0 && !_snap.EqSupported && !_demo) { MessageBox.Show(this, "Verzögerung braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            c.DelayMs = Math.Max(0, Math.Min(1000, ms));
            if (ms > 0) _settings.ChannelSoundOff = false;
            OnChannelEdited(c);
            ShowOsd("Verzögerung · " + c.Name, c.DelayMs == 0 ? "aus" : c.DelayMs + " ms", -1, CueColor);
        }

        private ToolStripMenuItem SoundSub(ChannelConfig c)
        {
            string state = (c.Stereo == 1 ? "mono" : c.Stereo == 2 ? "getauscht" : "stereo") + (c.DelayMs > 0 ? ", +" + c.DelayMs + " ms" : "");
            var sub = Sub("Ton: " + state);
            for (int i = 0; i < StereoNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(StereoNames[i], null, (s, e) => SetChannelStereo(c, k)) { Checked = c.Stereo == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            var del = Sub("Verzögerung: " + (c.DelayMs == 0 ? "aus" : c.DelayMs + " ms"));
            foreach (var v in MixerSettings.DelayValues)
            {
                int vv = v;
                del.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : v + " ms", null, (s, e) => SetChannelDelay(c, vv)) { Checked = c.DelayMs == v });
            }
            del.DropDownItems.Add(Hint("Spielt den Kanal später ab – z. B. damit Ton und Bild zusammenpassen."));
            sub.DropDownItems.Add(del);
            sub.DropDownItems.Add(Hint("Läuft dafür durchs Mischpult (wie beim EQ)."));
            return sub;
        }

        private void AddChannelSound320(SPage p)
        {
            p.Toggle("Mono, getauscht und Verzögerung pro Kanal", () =>
                {
                    var on = _settings.Channels.Where(c => c.Stereo != 0 || c.DelayMs > 0).Select(c => c.Name).ToList();
                    return "Per Rechtsklick auf einen Kanal → Ton: Mono, links/rechts tauschen oder den Ton bis 1 Sekunde später abspielen (damit Ton und Bild zusammenpassen). Die Kanäle laufen dafür durchs Mischpult (wie beim EQ). Aus = alles normal (deine Auswahl bleibt gespeichert)."
                        + (on.Count > 0 ? " Gerade: " + string.Join(", ", on) + "." : "");
                },
                () => !_settings.ChannelSoundOff, v => { _settings.ChannelSoundOff = !v; PushPolicy(); ScheduleSave(); });
        }

        // ================================================================ Menues, Tastenkuerzel

        /// <summary>Rechtsklick auf einen Kanal: Breite, Deckel, Ton, im Hintergrund leiser.</summary>
        private void AddChannel320Items(ToolStripItemCollection items, ChannelConfig c)
        {
            if (_settings.ChannelWidthOn) items.Add(WidthSub(c));
            if (!_settings.CapsOff) items.Add(CapSub(c));
            if (!_settings.ChannelSoundOff) items.Add(SoundSub(c));
            if (_settings.BgAppsQuietOn)
                items.Add(new ToolStripMenuItem("Leiser, wenn nicht im Vordergrund", null, (s, e) => { c.BgQuiet = !c.BgQuiet; ScheduleSave(); }) { Checked = c.BgQuiet });
        }

        /// <summary>Rechtsklick auf einen Ausgang: umbenennen, Testton.</summary>
        private void AddOutput320Items(ToolStripItemCollection items, string deviceId, string name)
        {
            if (deviceId == null) return;
            if (!_settings.OutputNamesOff) items.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() => RenameOutput(deviceId)))));
            if (_settings.TestToneOn) items.Add(new ToolStripMenuItem("Testton links/rechts", null, (s, e) => PlayTestTone(deviceId, name)));
        }

        /// <summary>Ausgangs-Klang: mono bzw. links/rechts tauschen (im Menue des Ausgangs).</summary>
        private void AddOutputMono320(ToolStripItemCollection items, string deviceId, string name)
        {
            var o = OutFxConfig(deviceId, false);
            items.Add(new ToolStripMenuItem("Mono", null, (s, e) => SetOutputMono(deviceId, name, !(o?.Mono ?? false), false)) { Checked = o?.Mono ?? false });
            items.Add(new ToolStripMenuItem("Links/rechts tauschen", null, (s, e) => SetOutputMono(deviceId, name, !(o?.Swap ?? false), true)) { Checked = o?.Swap ?? false, Enabled = !(o?.Mono ?? false) });
        }

        private void SetOutputMono(string deviceId, string name, bool on, bool swap)
        {
            if (on && !_snap.EqSupported && !_demo) { MessageBox.Show(this, "Mono und Tauschen brauchen Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            var o = OutFxConfig(deviceId, true, name);
            if (swap) o.Swap = on; else { o.Mono = on; if (on) o.Swap = false; }
            if (on) _settings.OutFxOff = false;
            PushPolicy();
            ScheduleSave();
            ShowOsd("Ausgang · " + (name ?? "Gerät"), o.Mono ? "Mono" : o.Swap ? "Links/rechts getauscht" : "Stereo", -1, CueColor);
        }

        private void AddHotkeyActions320(List<HotkeyAction> list)
        {
            list.Add(new HotkeyAction("pult.shot", "Bild vom Pult speichern", "Aussehen"));
            if (_settings.CountdownOn) list.Add(new HotkeyAction("countdown.toggle", "Countdown starten/stoppen", "Allgemein"));
            if (_settings.LockFeatureOn) list.Add(new HotkeyAction("pult.lock", "Pult sperren/entsperren", "Allgemein"));
            if (_settings.TestToneOn) list.Add(new HotkeyAction("testtone", "Testton links/rechts (Standard-Ausgang)", "Allgemein"));
            if (_settings.MacrosOn && _settings.Macros != null)
                foreach (var m in _settings.Macros) list.Add(new HotkeyAction("macro:" + m.Id, "Makro: " + m.Name, "Makros"));
        }

        private bool ExecuteHotkey320(string action)
        {
            switch (action)
            {
                case "pult.shot": SavePultShot(); return true;
                case "countdown.toggle": ToggleCountdown(); return true;
                case "pult.lock": ToggleLock(); return true;
                case "testtone": PlayTestTone(null, null); return true;
            }
            if (action != null && action.StartsWith("macro:", StringComparison.Ordinal))
            {
                string id = action.Substring(6);
                RunMacro(_settings.Macros?.FirstOrDefault(m => m.Id == id));
                return true;
            }
            return false;
        }
    }
}
