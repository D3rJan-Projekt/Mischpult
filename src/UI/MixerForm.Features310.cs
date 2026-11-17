using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.1 – Optik: grosse Einschalt-Show, analoge Uhr, Spektrum-Arten und -Farben, Doppelzeiger-VU, eigene Tasten-Farben,
    /// eigene Titelleiste, Saison-Deko, Kappen- und Tasten-Form, Kanalfarbe aus dem App-Symbol, eigene Laufschrift,
    /// Meterbridge-Hoehe, LED-Helligkeit und mehr. Alles einzeln an/aus in den Einstellungen.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Beim Start (vor dem ersten Zeichnen): statische Schalter aus den Einstellungen.</summary>
        private void ApplyFeatures310Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUDUAL") == "1") s.VuDual = true;
            }
            Theme.VuDual = s.VuDual;
            StripBase.BigShow = !s.BigShowOff;
            if (demo)
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_KEYSHAPE"), out int ks)) s.KeyShape = ks;
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_KEYCOLORS") == "1")
                {
                    s.KeyColorsOn = true;
                    s.KeyColors = new Dictionary<string, int> { { "MUTE", KeyPalette[9].ToArgb() }, { "SOLO", KeyPalette[7].ToArgb() }, { "SIG", KeyPalette[6].ToArgb() } };
                }
            }
            Theme.KeyShape = s.KeyShape;
            ApplyKeyColors(s);
            if (demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CAPSHAPE"), out int cs)) s.CapShape = cs;
            Theme.CapShape = s.CapShape;
            if (demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEHEIGHT"), out int bh)) s.BridgeHeight = bh;
            if (demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LEDBRIGHT"), out int lb)) s.LedBright = lb;
            MeterBridge.HeightPx = BridgeHeightPx(s.BridgeHeight);
            Theme.LedBright = (s.LedBright ?? 100) / 100f;
            if (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_ICONCOLOR") == "1") s.AutoIconColor = true;
            ChannelStrip.AutoIconColor = s.AutoIconColor;
            IconCache.AllowLetters = demo;
            if (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_TICKER") == "1") { s.TickerOn = true; s.TickerText = "Stream startet gleich"; }
        }

        /// <summary>Nach dem ersten Anzeigen (Titelleiste; nur Vorschau: Laufschrift vorfuehren).</summary>
        private void ShownFeatures310()
        {
            WireOwnTitle();
            if (_demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SEASON"), out int sm)) _settings.SeasonMode = sm;
            _top.DecoMode = _settings.SeasonMode;
            _top.RefreshDeco();
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OWNTITLE") == "1") _settings.OwnTitleBar = true;
            ApplyOwnTitle();
            if (!_demo) return;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_TICKER") == "1") DemoLater(ToggleTicker);
        }

        // ================================================================ Kanalfarbe aus dem App-Symbol

        private void SetAutoIconColor(bool on)
        {
            _settings.AutoIconColor = on;
            ChannelStrip.AutoIconColor = on;
            foreach (var s in _channelStrips) s.RefreshTint();
            RefreshStrips();
            RestyleBridges();
            ScheduleSave();
        }

        private void AddIconColor310(SPage p)
        {
            p.Toggle("Kanalfarbe aus dem App-Symbol", "Jeder Kanal nimmt die kräftigste Farbe aus dem Symbol seiner ersten App – Spotify grün, Discord lila, ein Spiel in seiner Farbe. Hat das Symbol keine deutliche Farbe, bleibt deine. Aus = wieder deine eigenen Farben (die bleiben gespeichert).",
                () => _settings.AutoIconColor, v => SetAutoIconColor(v));
        }

        // ================================================================ Eigene Laufschrift

        private const string DefaultTicker = "STREAM STARTET GLEICH";
        private string TickerText => string.IsNullOrWhiteSpace(_settings.TickerText) ? DefaultTicker : _settings.TickerText.Trim();

        private void ToggleTicker()
        {
            if (StripBase.TickerRunning) { StripBase.StopTicker(); return; }
            if (!_settings.TickerOn) return;
            try
            {
                var area = RectangleToScreen(ClientRectangle);
                StripBase.StartTicker(TickerText.ToUpperInvariant() + "     ·     ", area.Left, area.Right);
            }
            catch { }
        }

        private void EditTickerText()
        {
            using (var dlg = new InputDialog("Eigene Laufschrift", "Text, der durch die Anzeigen läuft:", TickerText, 80))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.TickerText = dlg.Value;
                ScheduleSave();
                if (StripBase.TickerRunning) { StripBase.StopTicker(); ToggleTicker(); }
            }
        }

        private void AddTicker310(SPage p)
        {
            p.Toggle("Eigene Laufschrift", "Ein eigener Text läuft immer wieder durch die Anzeigen aller Kanäle – z. B. „Stream startet gleich“ oder „Bin gleich zurück“. Starten und stoppen mit dem Knopf darunter oder einem Tastenkürzel (Steuerung → Tastenkürzel → „Eigene Laufschrift an/aus“).",
                () => _settings.TickerOn, v => { _settings.TickerOn = v; if (!v) StripBase.StopTicker(); ScheduleSave(); });
            p.Button("Text der Laufschrift", null, "Ändern …", r => EditTickerText(), () => "„" + TickerText + "“").Enabled = () => _settings.TickerOn;
            p.Button("Laufschrift", null, "Starten / Stoppen", r => ToggleTicker(), () => StripBase.TickerRunning ? "läuft" : "steht").Enabled = () => _settings.TickerOn;
        }

        // ================================================================ Grosse Einschalt-Show

        private void AddBigShow310(SPage p)
        {
            p.Toggle("Große Einschalt-Show", "Zum Selbsttest dazu: die Fader fahren einmal ganz hoch und dann an ihre Stelle, das Spektrum läuft als Welle von links nach rechts durch – wie ein großes Pult beim Hochfahren. Nur Anzeige, am Ton ändert sich nichts.",
                () => !_settings.BigShowOff, v => { _settings.BigShowOff = !v; StripBase.BigShow = v; ScheduleSave(); })
                .Enabled = () => !_settings.SelfTestOff;
        }

        // ================================================================ Saison-Deko

        private void SetSeasonMode(int i)
        {
            _settings.SeasonMode = Math.Max(0, Math.Min(SeasonDeco.AllModes - 1, i));   // 4.0: mehr Deko
            _top.DecoMode = _settings.SeasonMode;
            _top.RefreshDeco();
            ScheduleSave();
        }

        private void AddSeason310(SPage p)
        {
            p.Choice("Saison-Deko", () =>
                {
                    int now = SeasonDeco.For(_settings.SeasonMode, DateTime.Now);
                    string what = _settings.SeasonMode == 1 ? "Keine Deko." : "Halloween (24.10.–1.11.): leuchtender Kürbis und ein Spinnennetz · Weihnachten (1.–26.12.): blinkende Lichterkette · Silvester (31.12.–1.1.): kleine Feuerwerke – alles in der Kopfleiste.";
                    return what + (now != SeasonDeco.None ? " Gerade: " + SeasonDeco.Name(now) + "." : _settings.SeasonMode == 0 ? " Gerade ist keine Deko-Zeit." : "");
                },
                () => SeasonDeco.ModeNamesNow, () => Math.Min(_settings.SeasonMode, SeasonDeco.ModeNamesNow.Length - 1), i => SetSeasonMode(i));   // 4.0: mit mehr Deko
        }

        // ================================================================ Eigene Titelleiste

        [StructLayout(LayoutKind.Sequential)] private struct NcRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NcCalcParams { public NcRect R0, R1, R2; public IntPtr Pos; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);

        /// <summary>Eigene Titelleiste aktiv (nur Windows, nicht im Vollbild)?</summary>
        private bool OwnTitleActive => _settings != null && _settings.OwnTitleBar && Win32.IsWindows && !_demo && IsHandleCreated && !FullScreen.IsOn(this);

        /// <summary>
        /// Windows-Titelleiste weg, Rahmen bleibt (Andocken, Groesse aendern, Schatten, runde Ecken wie gewohnt).
        /// Die Kopfleiste des Pults ist dann die Titelleiste: freie Stellen ziehen, Doppelklick maximiert.
        /// </summary>
        private bool WndProcTitle(ref Message m)
        {
            if (!OwnTitleActive) return false;
            if (m.Msg == 0x0083 /* WM_NCCALCSIZE */ && m.WParam != IntPtr.Zero)
            {
                var p = (NcCalcParams)Marshal.PtrToStructure(m.LParam, typeof(NcCalcParams));
                int top = p.R0.Top;
                base.WndProc(ref m);   // Windows zieht Rahmen und Titelleiste ab …
                p = (NcCalcParams)Marshal.PtrToStructure(m.LParam, typeof(NcCalcParams));
                // … die Titelleiste geben wir zurueck. Maximiert ragt das Fenster um den Rahmen ueber den Bildschirm: dann oben so viel einruecken.
                int frame = WindowState == FormWindowState.Maximized ? GetSystemMetrics(33 /* SM_CYFRAME */) + GetSystemMetrics(92 /* SM_CXPADDEDBORDER */) : 0;
                p.R0.Top = top + frame;
                Marshal.StructureToPtr(p, m.LParam, false);
                m.Result = IntPtr.Zero;
                return true;
            }
            if (m.Msg == 0x0084 /* WM_NCHITTEST */)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt64() != 1 /* HTCLIENT */) return true;
                long lp = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool max = WindowState == FormWindowState.Maximized;
                int edge = Theme.Px(5), corner = Theme.Px(12);
                if (!max && pt.Y < edge)
                    m.Result = new IntPtr(pt.X < corner ? 13 /* HTTOPLEFT */ : pt.X > ClientSize.Width - corner ? 14 /* HTTOPRIGHT */ : 12 /* HTTOP */);
                else if (_top != null && _top.Visible && _top.Bounds.Contains(pt))
                    m.Result = new IntPtr(2 /* HTCAPTION */);
                return true;
            }
            return false;
        }

        /// <summary>Rahmen neu berechnen lassen (nach dem Umschalten) und Schatten behalten.</summary>
        private void ApplyOwnTitle()
        {
            bool on = _settings.OwnTitleBar && !FullScreen.IsOn(this);
            _top.WindowButtons = on;
            _top.WindowMaximized = WindowState == FormWindowState.Maximized;
            _top.Invalidate();
            if (!Win32.IsWindows || _demo || !IsHandleCreated) return;
            try
            {
                var mg = new Margins { Bottom = on ? 1 : 0 };
                DwmExtendFrameIntoClientArea(Handle, ref mg);
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020 /* NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED */);
            }
            catch { }
        }

        private void SetOwnTitleBar(bool on)
        {
            _settings.OwnTitleBar = on;
            ApplyOwnTitle();
            ScheduleSave();
        }

        private void WireOwnTitle()
        {
            _top.WinMinimize += () => WindowState = FormWindowState.Minimized;
            _top.WinMaxRestore += () => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            _top.WinClose += () => Close();
        }

        private void AddOwnTitle310(SPage p)
        {
            p.Toggle("Eigene Titelleiste", "Die Windows-Leiste oben verschwindet – die Kopfleiste des Pults ist die Titelleiste: an freien Stellen ziehen verschiebt das Fenster, Doppelklick maximiert, rechts sitzen Minimieren, Maximieren und Schließen im Pult-Design. Andocken an den Bildschirmrand und Größe ändern gehen wie gewohnt.",
                () => _settings.OwnTitleBar, v => SetOwnTitleBar(v));
        }

        // ================================================================ Tastenkuerzel

        private void AddHotkeyActions310(List<HotkeyAction> list)
        {
            list.Add(new HotkeyAction("ticker.toggle", "Eigene Laufschrift an/aus", "Aussehen"));
        }

        private bool ExecuteHotkey310(string action)
        {
            switch (action)
            {
                case "ticker.toggle": ToggleTicker(); return true;
            }
            return false;
        }

        // ================================================================ Meterbridge-Hoehe, LED-Helligkeit

        /// <summary>Gespeichert: 0 normal, 1 niedrig, 2 hoch – in der Auswahl: niedrig, normal, hoch.</summary>
        private static float BridgeHeightPx(int stored) => stored == 1 ? MeterBridge.HeightValues[0] : stored == 2 ? MeterBridge.HeightValues[2] : MeterBridge.HeightValues[1];
        private int BridgeHeightChoice => _settings.BridgeHeight == 1 ? 0 : _settings.BridgeHeight == 2 ? 2 : 1;

        private void SetBridgeHeight(int choice)
        {
            _settings.BridgeHeight = choice == 0 ? 1 : choice == 2 ? 2 : 0;
            MeterBridge.HeightPx = BridgeHeightPx(_settings.BridgeHeight);
            _bridge.Height = Theme.Px(MeterBridge.HeightPx);
            _bridge.Restyle();
            PerformLayout();
            ScheduleSave();
        }

        private void SetLedBright(float pct, bool fin)
        {
            int v = (int)Math.Round(Math.Max(40f, Math.Min(150f, pct)) / 10f) * 10;
            _settings.LedBright = v == 100 ? (int?)null : v;
            Theme.LedBright = v / 100f;
            RefreshStrips();
            _bridge.Restyle();
            _mini?.Invalidate();
            if (VuWindowOpen) _vuWin.Restyle();
            if (fin) ScheduleSave();
        }

        private void AddBridgeHeight310(SPage p)
        {
            p.Choice("Höhe der Meterbridge", () => BridgeHeightChoice == 0 ? "Niedrig – mehr Platz für die Kanäle."
                    : BridgeHeightChoice == 2 ? "Hoch – große Pegel, Spektrum und Uhr gut zu sehen." : "Normal (Standard).",
                MeterBridge.HeightNames, () => BridgeHeightChoice, i => SetBridgeHeight(i)).Enabled = () => !_settings.BridgeOff;
        }

        private void AddLedBright310(SPage p)
        {
            p.Slider("LED-Helligkeit", "Wie hell die leuchtenden Pegel-LEDs (und das Spektrum) sind – von gedimmt bis grell. Unabhängig von „Abends dunkler“.",
                40, 150, 10, () => (_settings.LedBright ?? 100), (v, fin) => SetLedBright(v, fin),
                v => ((int)Math.Round(v)) + " %" + ((int)Math.Round(v) == 100 ? " (Standard)" : ""));
        }

        // ================================================================ Faderkappen-Form

        private void SetCapShape(int i)
        {
            _settings.CapShape = Math.Max(0, Math.Min(Theme.CapShapeNames.Length - 1, i));
            Theme.CapShape = _settings.CapShape;
            RelayoutAllStrips();
            ScheduleSave();
        }

        private void AddCapShape310(SPage p)
        {
            p.Choice("Form der Faderkappen", () => _settings.CapShape == 0 ? "Wie das Design es vorsieht (Standard)."
                    : _settings.CapShape == 1 ? "Kurze Kappen – der Fader hat etwas mehr Weg."
                    : _settings.CapShape == 2 ? "Lange Kappen – gut zu greifen, wie an großen Studiopulten."
                    : _settings.CapShape == 3 ? "Runde Kappen mit Halbkreis-Enden."
                    : _settings.CapShape == 5 ? "Gummierte Kappen mit Griffmulde und Strich in der Kanalfarbe – wie an großen Konsolen."
                    : "Flache, schmale Kappen wie an modernen Digitalpulten.",
                Theme.CapShapeNames, () => _settings.CapShape, i => SetCapShape(i));
        }

        // ================================================================ Tasten-Farben und -Form

        /// <summary>Farben zum Auswaehlen (0 = wie im Design).</summary>
        private static readonly Color[] KeyPalette =
        {
            Color.Empty, Color.FromArgb(0xff, 0x3b, 0x30), Color.FromArgb(0xff, 0x8a, 0x1f), Color.FromArgb(0xff, 0xc2, 0x33), Color.FromArgb(0xff, 0xe1, 0x4a),
            Color.FromArgb(0x3b, 0xdb, 0x6f), Color.FromArgb(0x2e, 0xd3, 0xb7), Color.FromArgb(0x31, 0xc6, 0xff), Color.FromArgb(0x4f, 0x7d, 0xff),
            Color.FromArgb(0xa3, 0x5c, 0xff), Color.FromArgb(0xff, 0x4f, 0xa3), Color.FromArgb(0xf2, 0xf2, 0xf2),
        };
        private static readonly string[] KeyPaletteNames = { "Wie im Design", "Rot", "Orange", "Bernstein", "Gelb", "Grün", "Türkis", "Cyan", "Blau", "Lila", "Pink", "Weiß" };

        /// <summary>Farbe, die die Taste im Design hat (fuer das erste Feld der Auswahl).</summary>
        private static Color KeyDefault(string key)
        {
            switch (key)
            {
                case "MUTE": return Theme.MuteRed;
                case "SOLO": case "DIM": return Theme.SoloAmber;
                case "FADE": return ChannelStrip.FadeColor;
                case "AUTO": return ChannelStrip.AutoColor;
                case "COMP": return Theme.Accent;
                case "LOUD": return OutputStrip.LoudColor;
                case "PEAK": return Theme.MeterRed;
                default: return Theme.LedGreen;   // EQ, SIG
            }
        }

        private static void ApplyKeyColors(MixerSettings s)
        {
            if (!s.KeyColorsOn || s.KeyColors == null || s.KeyColors.Count == 0) { Theme.KeyColorMap = null; return; }
            var m = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (var kv in s.KeyColors) if (kv.Value != 0) m[kv.Key] = Color.FromArgb(kv.Value);
            Theme.KeyColorMap = m.Count > 0 ? m : null;
        }

        private int KeyColorIndex(string key)
        {
            if (_settings.KeyColors == null || !_settings.KeyColors.TryGetValue(key, out int argb) || argb == 0) return 0;
            for (int i = 1; i < KeyPalette.Length; i++) if (KeyPalette[i].ToArgb() == argb) return i;
            return 0;
        }

        private void SetKeyColor(string key, int i)
        {
            if (_settings.KeyColors == null) _settings.KeyColors = new Dictionary<string, int>();
            if (i <= 0) _settings.KeyColors.Remove(key); else _settings.KeyColors[key] = KeyPalette[Math.Min(i, KeyPalette.Length - 1)].ToArgb();
            ApplyKeyColors(_settings);
            RefreshKeys();
            ScheduleSave();
        }

        private void SetKeyColorsOn(bool on)
        {
            _settings.KeyColorsOn = on;
            ApplyKeyColors(_settings);
            RefreshKeys();
            ScheduleSave();
        }

        private void SetKeyShape(int i)
        {
            _settings.KeyShape = Math.Max(0, Math.Min(Theme.KeyShapeNames.Length - 1, i));
            Theme.KeyShape = _settings.KeyShape;
            RefreshKeys();
            ScheduleSave();
        }

        /// <summary>Tasten neu zeichnen (Zuege, Meterbridge).</summary>
        private void RefreshKeys()
        {
            RefreshStrips();
            RestyleBridges();
        }

        private void AddKeys310Settings(SPage p)
        {
            p.Section("Tasten");
            p.Choice("Tasten-Form", () => _settings.KeyShape == 0 ? "So, wie das Design es vorsieht (Standard)."
                    : _settings.KeyShape == 1 ? "Eckige Tasten mit scharfen Kanten."
                    : _settings.KeyShape == 2 ? "Runde Tasten (Pillenform)."
                    : _settings.KeyShape == 4 ? "Kippschalter: beleuchtete Wippe im Chromrahmen wie an einer Steckdosenleiste – an kippt sie und leuchtet in der Tastenfarbe."
                    : _settings.KeyShape == 5 ? "Bakelit: dunkelbraune, glänzende Drucktasten wie an alten Radios – an leuchten sie warm von innen."
                    : "Leuchtring: die Taste bleibt dunkel, nur Rand und Schrift leuchten.",
                Theme.KeyShapeNames, () => _settings.KeyShape, i => SetKeyShape(i));
            p.Toggle("Eigene Tasten-Farben", "MUTE, SOLO, FADE, EQ, COMP, AUTO, SIG, PEAK, DIM und LOUD leuchten in der Farbe, die du darunter auswählst. Aus = Farben wie im Design (deine Auswahl bleibt gespeichert).",
                () => _settings.KeyColorsOn, v => SetKeyColorsOn(v));
            foreach (var key in Theme.ColorKeys)
            {
                string k = key;
                var cols = (Color[])KeyPalette.Clone();
                cols[0] = KeyDefault(k);
                var sw = p.Swatches(k, cols, KeyPaletteNames, () => KeyColorIndex(k), i => SetKeyColor(k, i));
                sw.Visible = () => _settings.KeyColorsOn;
            }
        }

        // ================================================================ Doppelzeiger-VU

        private void SetVuDual(bool on)
        {
            _settings.VuDual = on;
            Theme.VuDual = on;
            RefreshVuStrips();
            RestyleBridges();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private void AddVu310Settings(SPage p)
        {
            p.Toggle("Doppelzeiger (L und R in einem)", "Links und rechts als zwei Zeiger in einem Instrument – schwarz (bzw. in Zeigerfarbe) für links, rot für rechts, wie bei alten Rundfunk-Geräten. Gilt für das VU-Meter im MASTER, die Zeigerinstrumente der Meterbridge und das große VU-Meter (dann ein großes Instrument statt zwei).",
                () => _settings.VuDual, v => SetVuDual(v));
        }
    }
}
