using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.2 – Aussehen: eigene Schrift, eigene Pegel-Farben, Designs als Datei teilen, Breite pro Kanal, Design nach Uhrzeit,
    /// Akzentfarbe von Windows oder vom Album-Cover, durchsichtiges Fenster, Zoom und Bild vom Pult. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Beim Start (vor dem Farbschema): statische Schalter.</summary>
        private void ApplyFeatures320Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                string f = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FONT");
                if (!string.IsNullOrEmpty(f)) { s.FontOn = true; s.FontName = f; }
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_METERCUSTOM") == "1")
                {
                    s.MeterColors = 4; s.MeterLowColor = Theme.MeterPalette[2].ToArgb(); s.MeterMidColor = Theme.MeterPalette[4].ToArgb(); s.MeterTopColor = Theme.MeterPalette[5].ToArgb();
                    s.MeterMidDb = -20f; s.MeterTopDb = -9f;
                }
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WIDTHS") == "1" && s.Channels.Count > 3)
                {
                    s.ChannelWidthOn = true; s.Channels[1].Width = 3; s.Channels[3].Width = 1;
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_ACCENTSRC"), out int asrc)) s.AccentSource = asrc;
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FADERCURVE"), out int fc)) s.FaderCurve = fc;
            }
            _zoomStarted = s.Zoom ?? 100;
            Theme.UiFamily = UiFontOf(s);
            ApplyMeterCustom(s);
            FaderLaw.SetCurve(s.FaderCurve);
            StripBase.MotorSpeedFactor = MotorFactor(s.MotorSpeed);
            // Design nach Uhrzeit: schon beim Start das passende Design
            if (s.TimeDesignOn)
            {
                bool night = IsNightNow(s, DateTime.Now);
                _timeDesignPeriod = night ? 1 : 0;
                s.UiStyle = night ? (s.TimeNightStyle ?? 3) : (s.TimeDayStyle ?? 0);
            }
            Theme.AccentOverride = s.AccentSource == 1 ? WindowsAccent() : null;
            _winAccentSeen = Theme.AccentOverride;
            ApplyFeatures320bStatics(s, demo);
        }

        /// <summary>Nach dem ersten Anzeigen: Fenster durchsichtig, Knoepfe in der Kopfleiste.</summary>
        private void ShownFeatures320()
        {
            _top.ShotButton = _settings.ShotButtonOn;
            _top.ShotClicked += SavePultShot;
            ShownFeatures320b();
            ApplyOpacity(true);
            if (_demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OPACITY"), out int op)) { _settings.Opacity = op; ApplyOpacity(true); }
        }

        private double _next320;

        private void TickFeatures320(double now, float dt)
        {
            TickFeatures320b(now, dt);
            if (now < _next320) return;
            _next320 = now + 0.25;
            TickTimeDesign(false);
            TickAccentSource(now);
            ApplyOpacity(false);
        }

        // ================================================================ Eigene Schrift

        private const string DefaultUiFont = "Bahnschrift";

        private static string UiFontOf(MixerSettings s)
        {
            if (!s.FontOn) return null;
            string f = s.FontName ?? DefaultUiFont;
            return Theme.FontExists(f) ? f : null;
        }

        private void ApplyUiFont()
        {
            Theme.UiFamily = UiFontOf(_settings);
            Font = Theme.Ui(12f);
            SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);   // alles neu zeichnen (speichert auch)
            RelayoutAllStrips();
            RestyleBridges();
        }

        private void SetFontOn(bool on) { _settings.FontOn = on; ApplyUiFont(); }

        private void SetFontName(string name)
        {
            _settings.FontName = string.IsNullOrWhiteSpace(name) || name == DefaultUiFont ? null : name;
            _settings.FontOn = true;
            ApplyUiFont();
        }

        private string FontText()
        {
            string f = _settings.FontName ?? DefaultUiFont;
            return Theme.FontExists(f) || _demo ? f : f + " (fehlt auf diesem PC)";
        }

        private void ShowFontMenu(Rectangle r)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            string cur = _settings.FontName ?? DefaultUiFont;
            foreach (var f in Theme.FontChoices)
            {
                string ff = f;
                bool has = Theme.FontExists(f) || _demo;
                var it = new ToolStripMenuItem(f + (f == DefaultUiFont ? "  (Vorschlag)" : ""), null, (s, e) => SetFontName(ff)) { Checked = cur == f, Enabled = has };
                try { if (has && !_demo) it.Font = new Font(f, Theme.F(12f), FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Andere Schrift …", null, (s, e) => BeginInvoke(new Action(PickOtherFont))));
            ShowMenuAt(m, r);
        }

        private void PickOtherFont()
        {
            try
            {
                using (var dlg = new FontDialog { ShowEffects = false, ShowColor = false, FontMustExist = true, AllowVerticalFonts = false })
                {
                    try { dlg.Font = new Font(_settings.FontName ?? DefaultUiFont, 12f); } catch { }
                    if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) == DialogResult.OK && dlg.Font != null) SetFontName(dlg.Font.FontFamily.Name);
                }
            }
            catch { }
        }

        // ================================================================ Eigene Pegel-Farben

        private static Color MeterCol(int? argb, Color def) => argb.HasValue ? Color.FromArgb(argb.Value | unchecked((int)0xFF000000)) : def;

        private static void ApplyMeterCustom(MixerSettings s) =>
            Theme.SetCustomMeter(MeterCol(s.MeterLowColor, Theme.MeterPalette[0]), MeterCol(s.MeterMidColor, Theme.MeterPalette[6]), MeterCol(s.MeterTopColor, Theme.MeterPalette[8]),
                s.MeterMidDb ?? -18f, s.MeterTopDb ?? -6f);

        private static readonly float[] MeterMidSteps = { -30f, -24f, -20f, -18f, -15f, -12f, -9f };
        private static readonly float[] MeterTopSteps = { -12f, -9f, -6f, -3f, -1f };

        private void RefreshMeterColors()
        {
            ApplyMeterCustom(_settings);
            RefreshStrips();
            RestyleBridges();
            _mini?.Invalidate();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private int MeterSwatch(int? argb, int def)
        {
            if (!argb.HasValue) return def;
            for (int i = 0; i < Theme.MeterPalette.Length; i++) if (Theme.MeterPalette[i].ToArgb() == (argb.Value | unchecked((int)0xFF000000))) return i;
            return def;
        }

        private void AddMeterColors320(SPage p)
        {
            Func<bool> own = () => _settings.MeterColors == 4;
            p.Swatches("Pegel unten", Theme.MeterPalette, Theme.MeterPaletteNames, () => MeterSwatch(_settings.MeterLowColor, 0),
                i => { _settings.MeterLowColor = Theme.MeterPalette[i].ToArgb(); RefreshMeterColors(); }).Visible = own;
            p.Swatches("Pegel Mitte", Theme.MeterPalette, Theme.MeterPaletteNames, () => MeterSwatch(_settings.MeterMidColor, 6),
                i => { _settings.MeterMidColor = Theme.MeterPalette[i].ToArgb(); RefreshMeterColors(); }).Visible = own;
            p.Swatches("Pegel oben", Theme.MeterPalette, Theme.MeterPaletteNames, () => MeterSwatch(_settings.MeterTopColor, 8),
                i => { _settings.MeterTopColor = Theme.MeterPalette[i].ToArgb(); RefreshMeterColors(); }).Visible = own;
            p.Choice("Mitte ab", () => "Ab diesem Pegel leuchtet die mittlere Farbe (Standard −18 dB).",
                MeterMidSteps.Select(d => StripBase.DbText(d)).ToArray(), () => Math.Max(0, Array.IndexOf(MeterMidSteps, _settings.MeterMidDb ?? -18f)),
                i => { _settings.MeterMidDb = MeterMidSteps[i]; if ((_settings.MeterTopDb ?? -6f) <= MeterMidSteps[i]) _settings.MeterTopDb = MeterTopSteps.First(t => t > MeterMidSteps[i]); RefreshMeterColors(); }).Visible = own;
            p.Choice("Oben ab", () => "Ab diesem Pegel leuchtet die obere Farbe (Standard −6 dB).",
                MeterTopSteps.Select(d => StripBase.DbText(d)).ToArray(), () => Math.Max(0, Array.IndexOf(MeterTopSteps, _settings.MeterTopDb ?? -6f)),
                i => { _settings.MeterTopDb = Math.Max(MeterTopSteps[i], (_settings.MeterMidDb ?? -18f) + 3f); RefreshMeterColors(); }).Visible = own;
        }

        // ================================================================ Designs als Datei teilen

        private CustomDesign CaptureFullDesign(string name)
        {
            var s = _settings;
            return new CustomDesign
            {
                Name = name, Style = s.UiStyle, Scheme = s.ColorScheme, Accent = s.AccentColor,
                Colors = s.CustomColors == null || s.CustomOff ? new Dictionary<string, int>() : new Dictionary<string, int>(s.CustomColors),
                Font = s.FontOn ? (s.FontName ?? DefaultUiFont) : null,
                MeterColors = s.MeterColors, MeterLow = s.MeterLowColor, MeterMid = s.MeterMidColor, MeterTop = s.MeterTopColor,
                MeterMidDb = s.MeterMidDb, MeterTopDb = s.MeterTopDb,
                PanelMaterial = s.PanelMaterial, KnobStyle = s.KnobStyle, CapStyle = s.CapStyle, CheekStyle = s.CheekStyle, LcdStyle = s.LcdStyle,
                CapShape = s.CapShape, KeyShape = s.KeyShape,
            };
        }

        /// <summary>Design-Datei schreiben (JSON, Endung .mpdesign).</summary>
        internal static bool WriteDesignFile(CustomDesign d, string file)
        {
            try
            {
                var ser = new DataContractJsonSerializer(typeof(CustomDesign));
                using (var fs = File.Create(file)) ser.WriteObject(fs, d);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Design-Datei lesen (null = keine gueltige Datei).</summary>
        internal static CustomDesign ReadDesignFile(string file)
        {
            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists || fi.Length > 256 * 1024) return null;
                var ser = new DataContractJsonSerializer(typeof(CustomDesign));
                CustomDesign d;
                using (var fs = File.OpenRead(file)) d = ser.ReadObject(fs) as CustomDesign;
                if (d == null) return null;
                if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Path.GetFileNameWithoutExtension(file);
                d.Name = d.Name.Trim();
                if (d.Name.Length > 40) d.Name = d.Name.Substring(0, 40);
                d.Style = Math.Max(0, Math.Min(Theme.StyleNames.Length - 1, d.Style));
                d.Scheme = Math.Max(0, Math.Min(Theme.SchemeNames.Length - 1, d.Scheme));
                d.Accent = Math.Max(0, Math.Min(Theme.AccentNames.Length - 1, d.Accent));
                if (d.Colors == null) d.Colors = new Dictionary<string, int>();
                if (d.Colors.Count > 20) d.Colors = d.Colors.Take(20).ToDictionary(kv => kv.Key, kv => kv.Value);
                return d;
            }
            catch { return null; }
        }

        private void ExportDesign()
        {
            string name;
            using (var dlg = new InputDialog("Design als Datei speichern", "Name des Designs:", "Mein Design", 40))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK || dlg.Value.Length == 0) return;
                name = dlg.Value;
            }
            using (var sfd = new SaveFileDialog { Filter = "Mischpult-Design (*.mpdesign)|*.mpdesign", FileName = string.Join("_", name.Split(Path.GetInvalidFileNameChars())) + ".mpdesign", Title = "Design speichern" })
            {
                if (sfd.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                if (WriteDesignFile(CaptureFullDesign(name), sfd.FileName)) ShowOsd("Design gespeichert", Path.GetFileName(sfd.FileName), -1, Theme.LedGreen);
                else MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Die Datei konnte nicht gespeichert werden.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImportDesign()
        {
            using (var ofd = new OpenFileDialog { Filter = "Mischpult-Design (*.mpdesign)|*.mpdesign|Alle Dateien (*.*)|*.*", Title = "Design laden" })
            {
                if (ofd.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                var d = ReadDesignFile(ofd.FileName);
                if (d == null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das ist keine gültige Mischpult-Design-Datei.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                var list = _settings.CustomDesigns ?? new List<CustomDesign>();
                list.RemoveAll(x => string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                if (list.Count >= 30) list.RemoveAt(0);
                list.Add(d);
                _settings.CustomDesigns = list;
                ApplyFullDesign(d);
            }
        }

        /// <summary>Design laden – mit Schrift, Pegel-Farben und Material, wenn die Datei sie mitbringt.</summary>
        private void ApplyFullDesign(CustomDesign d)
        {
            if (d.Font != null) { _settings.FontOn = true; _settings.FontName = d.Font == DefaultUiFont ? null : d.Font; Theme.UiFamily = UiFontOf(_settings); }
            if (d.MeterColors.HasValue) { _settings.MeterColors = Math.Max(0, Math.Min(4, d.MeterColors.Value)); Theme.MeterStyle = _settings.MeterColors; }
            if (d.MeterLow.HasValue) _settings.MeterLowColor = d.MeterLow;
            if (d.MeterMid.HasValue) _settings.MeterMidColor = d.MeterMid;
            if (d.MeterTop.HasValue) _settings.MeterTopColor = d.MeterTop;
            if (d.MeterMidDb.HasValue) _settings.MeterMidDb = d.MeterMidDb;
            if (d.MeterTopDb.HasValue) _settings.MeterTopDb = d.MeterTopDb;
            _settings.Normalize();
            ApplyMeterCustom(_settings);
            if (d.PanelMaterial.HasValue) SetPanelMaterial(d.PanelMaterial.Value);
            if (d.KnobStyle.HasValue) SetKnobStyle(d.KnobStyle.Value);
            if (d.CapStyle.HasValue) SetCapStyle(d.CapStyle.Value);
            if (d.CheekStyle.HasValue) SetCheekStyle(d.CheekStyle.Value);
            if (d.LcdStyle.HasValue) SetLcdStyle(d.LcdStyle.Value);
            if (d.CapShape.HasValue) SetCapShape(d.CapShape.Value);
            if (d.KeyShape.HasValue) SetKeyShape(d.KeyShape.Value);
            LoadCustomDesign(d);   // Design, Farbschema, Akzent, eigene Farben – zeichnet alles neu
            Font = Theme.Ui(12f);
            RelayoutAllStrips();
        }

        private void AddDesignFile320(SPage p)
        {
            p.Button("Design als Datei speichern", "Dein jetziges Aussehen (Design, Farben, Schrift, Pegel-Farben, Material) als kleine Datei – zum Weitergeben an Freunde oder als Sicherung.",
                "Speichern …", r => ExportDesign());
            p.Button("Design-Datei laden", "Eine .mpdesign-Datei (z. B. von einem Freund) laden. Sie kommt zu deinen eigenen Designs und wird gleich angezeigt.",
                "Laden …", r => ImportDesign());
        }

        // ================================================================ Breite pro Kanal

        /// <summary>Eigene Breite des Kanals (0 = wie alle).</summary>
        public int ChannelWidthOf(ChannelConfig c) => _settings.ChannelWidthOn && c != null ? c.Width : 0;

        public static readonly string[] WidthNames = { "Wie alle", "Schmal", "Normal", "Breit" };

        private void SetChannelWidth(ChannelConfig c, int w)
        {
            c.Width = Math.Max(0, Math.Min(3, w));
            c.WidthPx = 0;   // 3.4.5: feste Stufe ersetzt die gezogene Breite
            if (c.Width != 0) _settings.ChannelWidthOn = true;
            RelayoutWidths();
            ScheduleSave();
        }

        private void RelayoutWidths()
        {
            _channelHost.AnimateNextLayout();
            foreach (var s in _channelStrips) s.Relayout();
            _channelHost.Relayout();
            RestyleBridges();
        }

        private void AddChannelWidth320(SPage p)
        {
            p.Toggle("Eigene Breite pro Kanal", "Jeder Kanal kann schmal, normal oder breit sein – z. B. MUSIK breit, SYSTEM schmal. Einstellen per Rechtsklick auf den Kanal → Breite. Aus = alle gleich breit (deine Auswahl bleibt gespeichert).",
                () => _settings.ChannelWidthOn, v => { _settings.ChannelWidthOn = v; RelayoutWidths(); ScheduleSave(); });
        }

        private ToolStripMenuItem WidthSub(ChannelConfig c)
        {
            var sub = Sub("Breite: " + WidthNames[ChannelWidthOf(c)]);
            for (int i = 0; i < WidthNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(WidthNames[i], null, (s, e) => SetChannelWidth(c, k)) { Checked = ChannelWidthOf(c) == i });
            }
            return sub;
        }

        // ================================================================ Design nach Uhrzeit

        private int _timeDesignPeriod = -1;   // 0 Tag, 1 Nacht

        private static bool IsNightNow(MixerSettings s, DateTime now)
        {
            int day = s.TimeDayFrom ?? 7, night = s.TimeNightFrom ?? 20, h = now.Hour;
            if (day == night) return false;
            return day < night ? (h >= night || h < day) : (h >= night && h < day);
        }

        private void TickTimeDesign(bool force)
        {
            if (!_settings.TimeDesignOn || _settings.FollowWinTheme) { _timeDesignPeriod = -1; return; }   // 4.0: Windows hell/dunkel geht vor
            bool night = IsNightNow(_settings, DateTime.Now);
            int period = night ? 1 : 0;
            if (period == _timeDesignPeriod && !force) return;
            _timeDesignPeriod = period;
            int style = night ? (_settings.TimeNightStyle ?? 3) : (_settings.TimeDayStyle ?? 0);
            if (style != _settings.UiStyle) SetScheme(_settings.ColorScheme, _settings.AccentColor, style);
        }

        private static readonly int[] TimeHours = { 5, 6, 7, 8, 9, 10, 16, 17, 18, 19, 20, 21, 22, 23 };

        private void AddTimeDesign320(SPage p)
        {
            p.Section("Design nach Uhrzeit");
            p.Toggle("Design nach Uhrzeit", () => "Tagsüber ein Design, abends ein anderes – das Pult wechselt von selbst." + (_settings.TimeDesignOn ? " Gerade: " + (IsNightNow(_settings, DateTime.Now) ? "abends" : "tagsüber") + "." : ""),
                () => _settings.TimeDesignOn, v => { _settings.TimeDesignOn = v; ScheduleSave(); TickTimeDesign(true); });
            p.Choice("Tagsüber", () => "Design am Tag.", Theme.StyleNames, () => _settings.TimeDayStyle ?? 0,
                i => { _settings.TimeDayStyle = i; ScheduleSave(); TickTimeDesign(true); }).Enabled = () => _settings.TimeDesignOn;
            p.Choice("Abends", () => "Design am Abend und in der Nacht.", Theme.StyleNames, () => _settings.TimeNightStyle ?? 3,
                i => { _settings.TimeNightStyle = i; ScheduleSave(); TickTimeDesign(true); }).Enabled = () => _settings.TimeDesignOn;
            p.Choice("Abends ab", () => "Ab dieser Uhrzeit gilt das Abend-Design …", TimeHours.Select(h => h + " Uhr").ToArray(),
                () => Math.Max(0, Array.IndexOf(TimeHours, _settings.TimeNightFrom ?? 20)), i => { _settings.TimeNightFrom = TimeHours[i]; ScheduleSave(); TickTimeDesign(true); })
                .Enabled = () => _settings.TimeDesignOn;
            p.Choice("Morgens ab", () => "… und ab dieser Uhrzeit wieder das Tag-Design.", TimeHours.Select(h => h + " Uhr").ToArray(),
                () => Math.Max(0, Array.IndexOf(TimeHours, _settings.TimeDayFrom ?? 7)), i => { _settings.TimeDayFrom = TimeHours[i]; ScheduleSave(); TickTimeDesign(true); })
                .Enabled = () => _settings.TimeDesignOn;
        }

        // ================================================================ Akzentfarbe von Windows oder vom Album-Cover

        private Color? _winAccentSeen, _coverAccent;
        private Bitmap _coverSeen;
        private double _nextWinAccent;

        /// <summary>Akzentfarbe von Windows (Einstellungen → Personalisierung → Farben) oder null.</summary>
        internal static Color? WindowsAccent()
        {
            if (!Win32.IsWindows) return null;
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int abgr) return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "ColorizationColor", null);
                if (v is int argb) return Color.FromArgb(255, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
            }
            catch { }
            return null;
        }

        /// <summary>Wird das Album-Cover gebraucht (fuer die Akzentfarbe oder das Ambilight)?</summary>
        private bool CoverColorWanted => _settings.AccentSource == 2 || (_settings.CoverAmbiOn && !_settings.AmbilightOff);

        /// <summary>Kraeftigste Farbe des Covers fuers Ambilight (null = keins).</summary>
        private Color? CoverAmbiColor => _settings.CoverAmbiOn ? _coverAccent : null;

        private void TickAccentSource(double now)
        {
            // Cover-Farbe merken (fuer Akzent und/oder Ambilight)
            if (CoverColorWanted)
            {
                var cov = _nowPlaying.Current?.Cover;
                if (cov != null && !ReferenceEquals(cov, _coverSeen))
                {
                    _coverSeen = cov;
                    Color? c = null;
                    try { c = IconCache.DominantOf(cov); } catch { }
                    if (c.HasValue) _coverAccent = Theme.Readable(c.Value);
                }
            }
            Color? want = null;
            if (_settings.AccentSource == 1)
            {
                if (now >= _nextWinAccent) { _nextWinAccent = now + 15; _winAccentSeen = WindowsAccent(); }
                want = _winAccentSeen;
            }
            else if (_settings.AccentSource == 2) want = _coverAccent ?? Theme.AccentOverride;
            if (want?.ToArgb() != Theme.AccentOverride?.ToArgb())
            {
                Theme.AccentOverride = want;
                SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);
            }
        }

        private void SetAccentSource(int i)
        {
            _settings.AccentSource = Math.Max(0, Math.Min(2, i));
            _nextWinAccent = 0;
            if (i != 2) _coverSeen = null;
            if (i == 0) Theme.AccentOverride = null;
            ScheduleSave();
            TickAccentSource(_clock.Elapsed.TotalSeconds);
            SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);
        }

        private static readonly string[] AccentSourceNames = { "Auswahl", "Windows", "Album-Cover" };

        private void AddAccent320(SPage p)
        {
            p.Choice("Akzentfarbe von", () => _settings.AccentSource == 1 ? "Die Akzentfarbe von Windows (Einstellungen → Personalisierung → Farben) – ändert sie sich dort, zieht das Pult mit."
                    : _settings.AccentSource == 2 ? "Die kräftigste Farbe aus dem Album-Cover von dem, was gerade läuft – bei jedem Song neu."
                    : "Die oben ausgewählte Akzentfarbe (Standard).",
                AccentSourceNames, () => _settings.AccentSource, i => SetAccentSource(i));
            p.Toggle("Cover-Farbe fürs Ambilight", "Das Ambilight am Pultrand leuchtet in der Farbe des Album-Covers statt in der Farbe des lautesten Kanals.",
                () => _settings.CoverAmbiOn, v => { _settings.CoverAmbiOn = v; ScheduleSave(); }).Enabled = () => !_settings.AmbilightOff;
        }

        // ================================================================ Durchsichtiges Fenster

        private double _opacityShown = -1;

        private void ApplyOpacity(bool force)
        {
            if (!IsHandleCreated || _closing || _powerOffStart >= 0) return;   // 3.3: Ausschalt-Show blendet selbst aus
            int pct = _settings.Opacity ?? 100;
            double want = pct >= 100 ? 1.0 : pct / 100.0;
            if (pct < 100 && _settings.OpacityIdleOnly)
            {
                bool active = ActiveForm == this || ClientRectangle.Contains(PointToClient(Cursor.Position));
                if (active) want = 0.995;   // knapp unter 1: Fenster bleibt "durchsichtig-faehig", kein Flackern beim Umschalten
            }
            if (!force && Math.Abs(want - _opacityShown) < 0.001) return;
            _opacityShown = want;
            try { if (Math.Abs(Opacity - want) > 0.0005) Opacity = want; } catch { }   // nur wenn noetig (Fenster nicht unnoetig umstellen)
        }

        private void AddOpacity320(SPage p)
        {
            p.Choice("Durchsichtig", () => (_settings.Opacity ?? 100) >= 100 ? "Aus: das Fenster ist ganz deckend (Standard)."
                    : "Das Pult ist leicht durchsichtig – der Hintergrund scheint durch (" + (_settings.Opacity ?? 100) + " % deckend).",
                MixerSettings.OpacityValues.Select(v => v == 100 ? "Aus" : v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.OpacityValues, _settings.Opacity ?? 100)),
                i => { int v = MixerSettings.OpacityValues[i]; _settings.Opacity = v >= 100 ? (int?)null : v; ApplyOpacity(true); ScheduleSave(); });
            p.Toggle("… nur, wenn das Pult nicht aktiv ist", "Arbeitest du am Pult (Fenster aktiv oder Maus darüber), ist es ganz deckend – sonst durchsichtig.",
                () => _settings.OpacityIdleOnly, v => { _settings.OpacityIdleOnly = v; ApplyOpacity(true); ScheduleSave(); }).Enabled = () => (_settings.Opacity ?? 100) < 100;
        }

        // ================================================================ Zoom

        private int _zoomStarted = 100;   // mit diesem Zoom ist das Pult gestartet (wirkt erst nach Neustart)

        private void RestartForZoom()
        {
            if (_demo) { ShowOsd("Zoom", "Vorschau: kein Neustart", -1, Theme.Accent); return; }
            ChannelStrip.Renaming?.EndRename(true);
            RememberWindow();
            SaveNow();
            Program.RestartRequested = true;
            ExitApp();
        }

        private void AddZoom320(SPage p)
        {
            p.Choice("Größe (Zoom)", () => ((_settings.Zoom ?? 100) == 100 ? "Normal (Standard) – wie Windows es vorgibt." : "Das ganze Pult " + (_settings.Zoom ?? 100) + " % groß.")
                    + ((_settings.Zoom ?? 100) != _zoomStarted ? " Wirkt nach einem Neustart des Pults." : ""),
                MixerSettings.ZoomValues.Select(v => v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.ZoomValues, _settings.Zoom ?? 100)),
                i => { int v = MixerSettings.ZoomValues[i]; _settings.Zoom = v == 100 ? (int?)null : v; ScheduleSave(); });
            p.Button("Neu starten für die neue Größe", null, "Jetzt neu starten", r => RestartForZoom()).Visible = () => (_settings.Zoom ?? 100) != _zoomStarted;
        }

        // ================================================================ Bild vom Pult

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        /// <summary>Das Pult (Fensterinhalt) als Bild – auch wenn etwas davor liegt.</summary>
        private Bitmap CapturePult()
        {
            var size = ClientSize;
            if (size.Width < 10 || size.Height < 10) return null;
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            bool ok = false;
            if (Win32.IsWindows && !_demo)
            {
                try
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { ok = PrintWindow(Handle, hdc, 0x1 | 0x2 /* PW_CLIENTONLY | PW_RENDERFULLCONTENT */); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                }
                catch { ok = false; }
            }
            if (!ok)
            {
                try { DrawToBitmap(bmp, new Rectangle(Point.Empty, size)); ok = true; } catch { ok = false; }
            }
            if (!ok) { bmp.Dispose(); return null; }
            return bmp;
        }

        internal static string ShotFolder => MixerSettings.ShotFolderPath;   // 4.0: Dokumente\Mischpult\Bilder

        private void SavePultShot()
        {
            Bitmap bmp = null;
            try
            {
                bmp = CapturePult();
                if (bmp == null) { ShowOsd("Bild vom Pult", "Ging gerade nicht", -1, Theme.MuteRed); return; }
                Directory.CreateDirectory(ShotFolder);
                string file = Path.Combine(ShotFolder, "Mischpult " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                bmp.Save(file, ImageFormat.Png);
                _lastShot = file;
                if (!_settings.ShotClipOff) { try { Clipboard.SetImage(bmp); } catch { } }
                ShowOsd("Bild gespeichert", Path.GetFileName(file) + (!_settings.ShotClipOff ? " · in der Zwischenablage" : ""), -1, Theme.LedGreen);
            }
            catch (Exception ex) { ShowOsd("Bild vom Pult", "Fehler: " + ex.Message, -1, Theme.MuteRed); }
            finally { bmp?.Dispose(); }
        }

        private string _lastShot;

        private void AddShot320(SPage p)
        {
            p.Button("Bild vom Pult", "Speichert ein Bild nur vom Pult (ohne den Rest des Bildschirms) im Ordner " + MixerSettings.ShotFolderName + ". Auch per Tastenkürzel oder Kamera-Knopf oben.",
                "Bild machen", r => SavePultShot(), () => _lastShot == null ? null : Path.GetFileName(_lastShot));
            p.Toggle("Bild auch in die Zwischenablage", "Zum direkten Einfügen, z. B. in Discord (Strg+V).",
                () => !_settings.ShotClipOff, v => { _settings.ShotClipOff = !v; ScheduleSave(); });
            p.Toggle("Kamera-Knopf in der Kopfleiste", "Ein kleiner Knopf oben neben den Musik-Tasten macht das Bild mit einem Klick.",
                () => _settings.ShotButtonOn, v => { _settings.ShotButtonOn = v; _top.ShotButton = v; _top.Invalidate(); ScheduleSave(); });
            p.Button("Bilder-Ordner", null, "Öffnen", r =>
            {
                try { Directory.CreateDirectory(ShotFolder); System.Diagnostics.Process.Start("explorer.exe", ShotFolder); } catch { }
            }).Enabled = () => Win32.IsWindows && !_demo;
        }

        // ================================================================ Schrift und Groesse (Einstellungen)

        private void AddLook320(SPage p)
        {
            p.Section("Schrift und Größe");
            p.Toggle("Eigene Schrift", "Eine andere Schriftart für alle Beschriftungen im Pult – z. B. Bahnschrift (schmal und technisch, wie an echten Geräten). Aus = Segoe UI wie bisher.",
                () => _settings.FontOn, v => SetFontOn(v));
            p.Button("Schriftart", "Steht eine Schrift nicht auf deinem PC, bleibt Segoe UI.", "Wählen ▾", r => ShowFontMenu(r), () => FontText())
                .Enabled = () => _settings.FontOn;
            AddZoom320(p);
            AddOpacity320(p);
        }
    }
}
