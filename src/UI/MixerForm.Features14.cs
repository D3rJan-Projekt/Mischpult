using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.20 (3.0 Schritt 4): Design-Editor (eigene Farben, eigene Designs speichern), Schraeglage/3D-Licht, Armauflage
    /// und RGB-Beleuchtung ueber OpenRGB. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Eigene Farben

        /// <summary>Eigene Farben aus den Einstellungen (null = keine/aus).</summary>
        internal static Dictionary<string, Color> CustomColorsOf(MixerSettings s)
        {
            if (s.CustomOff || s.CustomColors == null || s.CustomColors.Count == 0) return null;
            return s.CustomColors.ToDictionary(kv => kv.Key, kv => Color.FromArgb(kv.Value | unchecked((int)0xFF000000)));
        }

        private DesignEditor _designWin;

        private void ReapplyTheme()
        {
            Theme.CustomColors = CustomColorsOf(_settings);
            SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);
            _designWin?.Invalidate();
            _arm?.Invalidate();
        }

        private void SetCustomColor(string slot, Color? c)
        {
            if (c.HasValue)
            {
                if (_settings.CustomColors == null) _settings.CustomColors = new Dictionary<string, int>();
                _settings.CustomColors[slot] = c.Value.ToArgb();
                _settings.CustomOff = false;
            }
            else if (_settings.CustomColors != null)
            {
                _settings.CustomColors.Remove(slot);
                if (_settings.CustomColors.Count == 0) _settings.CustomColors = null;
            }
            ReapplyTheme();
            ScheduleSave();
        }

        private void SetCustomOn(bool on)
        {
            _settings.CustomOff = !on;
            ReapplyTheme();
            ScheduleSave();
        }

        private void ResetCustomColors()
        {
            _settings.CustomColors = null;
            ReapplyTheme();
            ScheduleSave();
        }

        public void ShowDesignEditor()
        {
            if (_designWin != null && !_designWin.IsDisposed) { _designWin.Activate(); return; }
            var w = new DesignEditor
            {
                SetSlot = SetCustomColor,
                IsCustom = slot => !_settings.CustomOff && _settings.CustomColors != null && _settings.CustomColors.ContainsKey(slot),
                SaveAs = SaveCustomDesign,
                ResetAll = ResetCustomColors,
                LoadMenu = p => { var m = NewMenu(); m.ShowCheckMargin = false; m.ShowImageMargin = false; AddCustomDesignItems(m.Items); m.Show(p); },
                TopMost = TopMost,
            };
            w.FormClosed += (s, e) => { if (_designWin == w) _designWin = null; };
            _designWin = w;
            w.Show(this);
        }

        private void SaveCustomDesign()
        {
            var list = _settings.CustomDesigns ?? new List<CustomDesign>();
            using (var dlg = new InputDialog("Eigenes Design speichern", "Name (z. B. Nachtblau, Studio Rot):", "Mein Design " + (list.Count + 1)))
            {
                if (dlg.ShowDialog(_designWin ?? (IWin32Window)this) != DialogResult.OK) return;
                var d = list.FirstOrDefault(x => string.Equals(x.Name, dlg.Value, StringComparison.OrdinalIgnoreCase));
                if (d == null) { if (list.Count >= 30) return; d = new CustomDesign { Name = dlg.Value }; list.Add(d); }
                d.Style = _settings.UiStyle; d.Scheme = _settings.ColorScheme; d.Accent = _settings.AccentColor;
                d.Colors = _settings.CustomColors == null ? new Dictionary<string, int>() : new Dictionary<string, int>(_settings.CustomColors);
                _settings.CustomDesigns = list;
                ScheduleSave();
                ShowOsd("Design gespeichert", d.Name, -1, Theme.LedGreen);
            }
        }

        private void LoadCustomDesign(CustomDesign d)
        {
            if (d == null) return;
            _settings.CustomColors = d.Colors != null && d.Colors.Count > 0 ? new Dictionary<string, int>(d.Colors) : null;
            _settings.CustomOff = false;
            _settings.UiStyle = Math.Max(0, Math.Min(Theme.StyleNames.Length - 1, d.Style));
            _settings.ColorScheme = Math.Max(0, Math.Min(Theme.SchemeNames.Length - 1, d.Scheme));
            _settings.AccentColor = Math.Max(0, Math.Min(Theme.AccentNames.Length - 1, d.Accent));
            ReapplyTheme();
            ScheduleSave();
            ShowOsd("Design", d.Name, -1, Theme.Accent);
        }

        private void AddCustomDesignItems(ToolStripItemCollection items)
        {
            var list = _settings.CustomDesigns ?? new List<CustomDesign>();
            foreach (var d in list)
            {
                var dd = d;
                items.Add(new ToolStripMenuItem(d.Name + "  (" + Theme.StyleNames[Math.Max(0, Math.Min(Theme.StyleNames.Length - 1, d.Style))] + ")", null, (s, e) => LoadCustomDesign(dd)));
            }
            if (list.Count == 0) items.Add(Hint("Noch keine – im Design-Editor „Speichern als …“"));
            else
            {
                var del = Sub("Löschen", false);
                foreach (var d in list)
                {
                    var dd = d;
                    del.DropDownItems.Add(new ToolStripMenuItem(d.Name, null, (s, e) => { _settings.CustomDesigns.Remove(dd); if (_settings.CustomDesigns.Count == 0) _settings.CustomDesigns = null; ScheduleSave(); }));
                }
                items.Add(new ToolStripSeparator());
                items.Add(del);
            }
        }

        // ================================================================ Schraeglage und Armauflage

        private ArmRestBar _arm;

        /// <summary>Armauflage vorne am Pult (Leder, Holz, Gummi oder Alu).</summary>
        private sealed class ArmRestBar : Control
        {
            public ArmRestBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(0x20, 0x1a, 0x18);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Theme.DrawArmRest(e.Graphics, ClientRectangle, Theme.ArmRest);
                if (Theme.NightOn) Theme.DimOverlay(e.Graphics, ClientRectangle, 0f);
            }
        }

        private void SetTilt(int t)
        {
            _settings.Tilt = Math.Max(0, Math.Min(2, t));
            Theme.Tilt = _settings.Tilt;
            ReapplyTheme();
            ScheduleSave();
        }

        private void SetArmRest(int a)
        {
            _settings.ArmRest = Math.Max(0, Math.Min(4, a));
            Theme.ArmRest = _settings.ArmRest;
            if (_arm != null) { _arm.Visible = Theme.ArmRest > 0; _arm.Invalidate(); }
            ScheduleSave();
        }

        // ================================================================ RGB ueber OpenRGB

        private OpenRgbClient _rgb;
        public static readonly string[] RgbModeNames = { "Pegel (grün → rot)", "Spektrum (Bass rot … Höhen blau)", "Akzentfarbe", "Farbe des lautesten Kanals" };
        private int RgbBright => _settings.RgbBright ?? 80;
        private bool RgbWantsSpectrum => _rgb != null && _settings.RgbOn && _settings.RgbMode == 1;
        private double _nextRgb;
        private float _rgbLevel;

        private void StartRgb()
        {
            StopRgb();
            if (!_settings.RgbOn || _demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RGB") == null) return;
            _rgb = new OpenRgbClient(_settings.RgbHost, _settings.RgbPort);
        }

        private void StopRgb()
        {
            var r = _rgb;
            _rgb = null;
            if (r != null) try { r.Dispose(); } catch { }
        }

        private void SetRgbOn(bool on)
        {
            _settings.RgbOn = on;
            if (on) StartRgb(); else StopRgb();
            ScheduleSave();
        }

        private string RgbStatus => !_settings.RgbOn ? "aus" : _rgb == null ? "nicht verbunden" : _rgb.Status;

        private void AskRgbAddress()
        {
            string cur = (_settings.RgbHost ?? "127.0.0.1") + (_settings.RgbPort > 0 && _settings.RgbPort != OpenRgbClient.DefaultPort ? ":" + _settings.RgbPort : "");
            using (var dlg = new InputDialog("OpenRGB-Adresse", "Rechner und Port (Standard 127.0.0.1:6742 = dieser PC):", cur))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                ParseRgbAddress(dlg.Value, out string host, out int port);
                _settings.RgbHost = host == "127.0.0.1" ? null : host;
                _settings.RgbPort = port == OpenRgbClient.DefaultPort ? 0 : port;
                if (_settings.RgbOn) StartRgb();
                ScheduleSave();
            }
        }

        internal static void ParseRgbAddress(string text, out string host, out int port)
        {
            host = "127.0.0.1"; port = OpenRgbClient.DefaultPort;
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            int k = text.LastIndexOf(':');
            if (k > 0 && int.TryParse(text.Substring(k + 1), out int p) && p > 0 && p < 65536) { port = p; text = text.Substring(0, k); }
            if (text.Length > 0) host = text;
        }

        /// <summary>Farben fuer die RGB-Beleuchtung (Modus, Pegel 0..1, Spektrum, Akzent, Kanalfarbe, Helligkeit %).</summary>
        internal static Color[] RgbFrame(int mode, float level, float[] bars, Color accent, Color loudest, int brightPct)
        {
            float k = Math.Max(0.1f, Math.Min(1f, brightPct / 100f));
            Color Scale(Color c, float f) { f = Math.Max(0f, Math.Min(1f, f)) * k; return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f)); }
            switch (mode)
            {
                case 1:
                    if (bars == null || bars.Length == 0) return new[] { Color.Black };
                    var r = new Color[bars.Length];
                    for (int i = 0; i < bars.Length; i++)
                    {
                        double hue = 360.0 * (i / (double)Math.Max(1, bars.Length - 1)) * 0.72;   // rot … violett
                        r[i] = Scale(Hsv(hue), 0.08f + 0.92f * bars[i]);
                    }
                    return r;
                case 2: return new[] { Scale(accent, 0.55f + 0.45f * level) };
                case 3: return new[] { Scale(loudest, 0.12f + 0.88f * level) };
                default:
                    var c = level < 0.6f ? Theme.Mix(Color.FromArgb(0x20, 0xe0, 0x50), Color.FromArgb(0xff, 0xd0, 0x20), level / 0.6f)
                                         : Theme.Mix(Color.FromArgb(0xff, 0xd0, 0x20), Color.FromArgb(0xff, 0x20, 0x10), Math.Min(1f, (level - 0.6f) / 0.3f));
                    return new[] { Scale(c, 0.12f + 0.88f * level) };
            }
        }

        private static Color Hsv(double h)
        {
            h = ((h % 360) + 360) % 360;
            double x = 1 - Math.Abs(h / 60 % 2 - 1);
            double r, g, b;
            if (h < 60) { r = 1; g = x; b = 0; } else if (h < 120) { r = x; g = 1; b = 0; } else if (h < 180) { r = 0; g = 1; b = x; }
            else if (h < 240) { r = 0; g = x; b = 1; } else if (h < 300) { r = x; g = 0; b = 1; } else { r = 1; g = 0; b = x; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private static float LevelOf(float lin)
        {
            if (lin <= 0.00001f) return 0f;
            double db = 20 * Math.Log10(lin);
            return (float)Math.Max(0, Math.Min(1, (db + 48) / 48));
        }

        private readonly float[] _rgbBars = new float[12], _rgbPk = new float[12];

        private void TickRgb(double now, float dt)
        {
            var rgb = _rgb;
            if (rgb == null || now < _nextRgb) return;
            _nextRgb = now + 1.0 / 30;
            var d = DefaultDevice;
            float lvl = d == null || d.Mute ? 0f : LevelOf(Math.Max(d.PeakL, d.PeakR));
            _rgbLevel = lvl > _rgbLevel ? lvl : Math.Max(lvl, _rgbLevel - 1.6f / 30f);   // schnell hoch, weich runter
            Color loud = Theme.Accent; float best = -1f;
            foreach (var c in _settings.Channels)
                if (_channelPeak.TryGetValue(c.Id, out var pk) && pk != null && pk.Length >= 2 && Math.Max(pk[0], pk[1]) > best && !c.Mute)
                { best = Math.Max(pk[0], pk[1]); loud = Color.FromArgb(c.Color); }
            float[] bars = null;
            if (_settings.RgbMode == 1) { _spectrum.Bars(12, _rgbBars, _rgbPk); bars = _rgbBars; }
            rgb.SetFrame(RgbFrame(_settings.RgbMode, _rgbLevel, bars, Theme.Accent, loud, RgbBright));
        }

        // ================================================================ Einbindung

        /// <summary>Vor dem ersten ApplyScheme: eigene Farben, Schraeglage, Armauflage.</summary>
        internal static void ApplyFeatures14Statics(MixerSettings s)
        {
            Theme.CustomColors = CustomColorsOf(s);
            Theme.Tilt = s.Tilt;
            Theme.ArmRest = s.ArmRest;
        }

        private void InitFeatures14()
        {
            _arm = new ArmRestBar { Dock = DockStyle.Bottom, Height = Theme.Px(26), Visible = Theme.ArmRest > 0 };
            StartRgb();
        }

        private void TickFeatures14(double now, float dt)
        {
            if (Theme.TiltWindowH != ClientSize.Height && ClientSize.Height > 100) { Theme.TiltWindowH = ClientSize.Height; if (Theme.TiltOn) RefreshStrips(); }
            TickRgb(now, dt);
        }

        private void CleanupFeatures14() => StopRgb();

        private void AddLookSettings20(SPage p)
        {
            p.Section("Eigenes Design");
            p.Toggle("Eigene Farben", "Farben für Pult-Grund, Kanalzüge, Tasten, Anzeigen, Schrift, Akzent, Kopfleiste und Menüs – im Design-Editor. Aus = wieder wie im Design (deine Farben bleiben gespeichert).",
                () => !_settings.CustomOff && _settings.CustomColors != null, v => SetCustomOn(v)).Enabled = () => _settings.CustomColors != null;
            p.Button("Design-Editor", "Farben anklicken – das Pult ändert sich sofort. Mit Lesbarkeits-Prüfung.", "Öffnen", r => ShowDesignEditor(),
                () => { int n = _settings.CustomColors?.Count ?? 0; return n == 0 ? "keine eigenen" : n + " eigene"; }).Primary = true;
            p.Button("Eigene Designs", "Gespeicherte Designs laden (Design, Farbschema, Akzent und eigene Farben).", "Laden ▾", r =>
            {
                var m = NewMenu(); m.ShowCheckMargin = false; m.ShowImageMargin = false;
                AddCustomDesignItems(m.Items);
                ShowMenuAt(m, r);
            }, () => { int n = _settings.CustomDesigns?.Count ?? 0; return n == 0 ? "keine" : n + " gespeichert"; });
            AddDesignFile320(p);   // 3.2: Design als Datei teilen
            p.Section("3D");
            p.Choice("Schräglage", () => _settings.Tilt == 0 ? "Aus: überall gleich hell." : "Das Pult ist wie ein echtes Mischpult schräg zum Licht geneigt: oben heller, vorne dunkler – wirkt räumlicher.",
                Theme.TiltNames, () => _settings.Tilt, i => SetTilt(i));
            p.Choice("Armauflage vorne", () => _settings.ArmRest == 0 ? "Keine." : "Eine Auflage für die Handgelenke an der Vorderkante – wie an großen Studiopulten.",
                Theme.ArmRestNames, () => _settings.ArmRest, i => SetArmRest(i));
        }

        private void AddRgbSettings20(SPage p)
        {
            p.Section("RGB-Beleuchtung");
            p.Toggle("RGB über OpenRGB", "Tastatur, Maus, Lüfter, Mainboard & Co. leuchten mit dem Pult – über das kostenlose Programm OpenRGB. Dort unter „SDK Server“ auf „Start Server“ klicken; das Mischpult verbindet sich dann von selbst (auch später).",
                () => _settings.RgbOn, v => SetRgbOn(v));
            p.Choice("Leuchtet nach", () => "Was die Beleuchtung zeigt.", RgbModeNames, () => _settings.RgbMode, i => { _settings.RgbMode = i; ScheduleSave(); })
                .Enabled = () => _settings.RgbOn;
            p.Slider("Helligkeit", "Wie hell die RGB-Geräte höchstens leuchten.", 10f, 100f, 5f, () => RgbBright, (v, fin) => { _settings.RgbBright = (int)v; if (fin) ScheduleSave(); },
                v => (int)v + " %").Enabled = () => _settings.RgbOn;
            p.Button("Verbindung", "Rechner und Port von OpenRGB (Standard: dieser PC, Port 6742).", "Adresse …", r => AskRgbAddress(), () => RgbStatus).Enabled = () => _settings.RgbOn;
        }

        /// <summary>Nur Vorschau.</summary>
        internal static void DemoFeatures14(MixerSettings s)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_TILT"), out int t)) s.Tilt = t;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_ARMREST"), out int a)) s.ArmRest = a;
            string cc = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CUSTOM");   // "slot=RRGGBB,…"
            if (!string.IsNullOrEmpty(cc))
            {
                s.CustomColors = new Dictionary<string, int>();
                foreach (var part in cc.Split(','))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && int.TryParse(kv[1], System.Globalization.NumberStyles.HexNumber, null, out int rgb)) s.CustomColors[kv[0]] = unchecked((int)0xFF000000) | rgb;
                }
            }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RGB") == "1") s.RgbOn = true;
        }

        // ================================================================ 3.0: Grafikkarte

        private bool _gpuCrashNote;

        /// <summary>Beim Start: Einstellung uebernehmen; lag noch die Merkdatei da, ist es beim letzten Mal abgestuerzt – dann aus.</summary>
        private void ApplyGpuStatics(MixerSettings s, bool demo)
        {
            if (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_GPU") == "1") s.GpuOn = true;
            if (!demo && GpuDraw.CrashedLastTime() && s.GpuOn)
            {
                s.GpuOn = false;
                _gpuCrashNote = true;
                try { s.Save(); } catch { }
            }
            GpuDraw.Wanted = s.GpuOn && !s.AccelOff;
        }

        private void SetGpuOn(bool on)
        {
            _settings.GpuOn = on;
            GpuDraw.Wanted = on && AccelOn;
            GpuDraw.Disabled = null;
            GpuDraw.FramesOk = 0;
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private void ShownGpu()
        {
            if (!_gpuCrashNote) return;
            DemoLater(() => MessageBox.Show(this, "Beim letzten Mal ist das Mischpult beim Zeichnen über die Grafikkarte abgestürzt.\n\n" +
                "Deshalb zeichnet es jetzt wieder wie bisher. Du kannst es unter OPTIONEN → Allgemein → Leistung wieder einschalten.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        private void SetAccel(bool on)
        {
            _settings.AccelOff = !on;
            GpuDraw.Wanted = _settings.GpuOn && on;
            if (VuWindowOpen) _vuWin.Restyle();
            ApplyPerf();
            UpdateFps();
            ScheduleSave();
        }

        private void SetVSyncOn(bool on) { _settings.VSyncOff = !on; UpdateFps(); ApplyVSync(); ScheduleSave(); }
        private void SetHighFps(bool on) { _settings.HighFpsOff = !on; UpdateFps(); ScheduleSave(); }
        private void SetPaintNow(bool on) { _settings.PaintNowOff = !on; ScheduleSave(); }

        private void AddAccelSettings(SPage p)
        {
            p.Section("Hardware-Beschleunigung");
            p.Toggle("Hardware-Beschleunigung", "Pegel und Anzeigen laufen so flüssig wie möglich – auch auf einem zweiten Bildschirm mit anderer Bildwiederholrate und neben einem Spiel. Hauptschalter für die Punkte darunter (außer der Bild-Statistik); aus = alles wie bis 2.20.",
                () => AccelOn, v => SetAccel(v));
            p.Toggle("Im Takt des Bildschirms (VSync)", "Das Pult wartet auf den Bildaufbau genau des Bildschirms, auf dem es steht – jedes Bild kommt im Gleichschritt, kein Ruckeln durch zwei verschiedene Takte. Ziehst du das Pult auf einen anderen Bildschirm, wechselt der Takt mit. Geht es auf einem PC nicht, läuft der genaue Zeitgeber wie bisher.",
                () => !_settings.VSyncOff, v => SetVSyncOn(v)).Enabled = () => AccelOn && !_settings.PacerOff;
            p.Toggle("Hohe Bildrate (bis 165 Bilder/s)", "Auf 120-, 144- oder 165-Hz-Bildschirmen bekommt jeder Bildaufbau ein neues Bild (sonst höchstens 90 Bilder/s, dann jedes zweite). Braucht etwas mehr Rechenzeit.",
                () => !_settings.HighFpsOff, v => SetHighFps(v)).Enabled = () => AccelOn;
            p.Toggle("Bild sofort zeichnen", "Ein neues Bild wird direkt nach dem Berechnen gezeichnet, statt zu warten, bis Windows gerade Zeit hat – gleichmäßigere Abstände zwischen den Bildern.",
                () => !_settings.PaintNowOff, v => SetPaintNow(v)).Enabled = () => AccelOn;
            p.Toggle("Zeichnen über die Grafikkarte (Direct2D)", "Großes VU-Meter, großes Spektrum, Wasserfall und der Bildschirmschoner (VU/Spektrum) kommen von der Grafikkarte – flüssiger auf großen Bildschirmen und im Vollbild. Klappt etwas nicht, zeichnet das Pult sofort wieder wie bisher; stürzt es sogar ab, schaltet es sich beim nächsten Start von selbst aus.",
                () => _settings.GpuOn, v => SetGpuOn(v)).Enabled = () => AccelOn;
            AddAccel302Settings(p);   // 3.0.2: gegen Ruckeln neben Spielen
            p.Info("Zustand", () => PerfStatusText() + (_settings.GpuOn && AccelOn ? " Grafikkarte: " + GpuDraw.Status + "." : ""));
        }

        private void ShownFeatures14()
        {
            if (!_demo) return;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DESIGNEDITOR") == "1") DemoLater(ShowDesignEditor);
        }
    }
}
