using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.2 – Optik &amp; Extras: spiegelnder Tisch, RGB-Welle, leuchtender Fensterrand, Startbild, App-Symbol auf der Kappe,
    /// Klang-Kunst, Schalter im "Was ist neu"-Fenster, Funktions-Pakete, Doppelklick auf Mischpult-Dateien.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Alle 4.2-Schalter (Standard aus) – fuer die Vorschau per MISCHPULT_DEMO_&lt;NAME&gt;=1.</summary>
        internal static readonly string[] Bools420 =
        {
            "NewsTogglesOn", "PacksOn", "FileAssocOn", "DeskMirrorOn", "RgbWaveOn", "RgbWaveBeat", "FrameGlowOn", "SplashOn", "CapIconOn", "SoundArtOn",
            "DuckLampsOn", "DuckMinTalkOn", "DuckFadeOn",   // 4.2.3
            "DuckWhoOn", "DuckHoldKeyOn", "DuckPresetsOn", "DuckWhenOn", "AppProfilesOn", "ChanTimerOn", "SysQuietGameOn", "BatteryOn",   // 4.3
            "RemoteOn",   // 4.4
        };

        private void ApplyLook420(MixerSettings s, bool demo)
        {
            if (demo)
            {
                foreach (var f in Bools420)
                    if (DemoFlag(demo, f.ToUpperInvariant())) typeof(MixerSettings).GetField(f)?.SetValue(s, true);
                s.FrameGlowMode = DemoInt("FRAMEGLOWMODE", s.FrameGlowMode);
            }
            ChannelStrip.CapIcon = s.CapIconOn;
            StripBase.WaveOverlay = s.RgbWaveOn ? (Action<StripBase, Graphics>)RgbOverlay420 : null;
        }

        /// <summary>Einmal nach dem Anzeigen.</summary>
        private void HookLook420()
        {
            ApplyCables346();   // Platz fuer den Tisch
            if (_settings.SplashOn && (_demo || _clock.Elapsed.TotalSeconds < 8)) ShowSplash420();
            if (_settings.FileAssocOn && !_demo) EnsureFileAssoc420();
            if (_demo) DemoShown420();
        }

        private double _nextWave420, _nextDesk420, _nextFrame420;

        private void TickLook420(double now, float dt)
        {
            if (_settings.SoundArtOn) TickArt420(now);
            if (!PultShown) return;
            Theme.MatrixSpeed = 1f + 2.5f * Math.Min(1f, _master?.LevelNow ?? 0f);
            if (_settings.RgbWaveOn && now >= _nextWave420)
            {
                _nextWave420 = now + 0.045;
                float lvl = Math.Min(1f, _master?.LevelNow ?? 0f);
                _wavePhase420 += (float)(0.045 * (_settings.RgbWaveBeat ? 0.06 + 0.5 * lvl : 0.08));
                foreach (var s in _channelStrips) if (s.Visible) s.InvalidateEdges420();
            }
            if (_settings.DeskMirrorOn && now >= _nextDesk420)
            {
                _nextDesk420 = now + 0.07;
                foreach (var h in new[] { _channelHost, _outputHost })
                    if (h != null && h.ClientSize.Height > h.StripBottom) h.Invalidate(new Rectangle(0, h.StripBottom, h.ClientSize.Width, h.ClientSize.Height - h.StripBottom));
            }
            if (now >= _nextFrame420) { _nextFrame420 = now + 0.05; TickFrame420(); }
        }

        private void CleanupLook420()
        {
            try { _splash420?.Close(); } catch { }
            try { _artWin420?.Close(); } catch { }
            SetFrameColor420(-1);
            if (_settings.SoundArtOn && !_demo) FlushArt420(true);
        }

        // ================================================================ 283 spiegelnder Tisch

        /// <summary>Unter den Zuegen: glaenzende Tischplatte, darin spiegeln sich die Zuege und ihr Licht.</summary>
        private void PaintDesk420(StripHost h, Graphics g)
        {
            if (!_settings.DeskMirrorOn) return;
            int top = h.StripBottom + Theme.Px(2), bottom = h.ClientSize.Height;
            if (bottom - top < Theme.Px(10)) return;
            var desk = new Rectangle(0, top, h.ClientSize.Width, bottom - top);
            using (var b = new LinearGradientBrush(new Rectangle(desk.X, desk.Y - 1, desk.Width, desk.Height + 2), Color.FromArgb(170, 0x10, 0x11, 0x14), Color.FromArgb(235, 0x02, 0x02, 0x03), LinearGradientMode.Vertical))
                g.FillRectangle(b, desk);
            var st = g.Save();
            g.SetClip(desk, CombineMode.Intersect);
            foreach (Control c in h.Controls)
            {
                if (!(c is StripBase sb) || !c.Visible || c.Right < 0 || c.Left > desk.Right) continue;
                var bmp = sb.Reflection420(desk.Height - Theme.Px(2));
                if (bmp != null) g.DrawImage(bmp, c.Left + sb.ReflectionX420, top + Theme.Px(2), bmp.Width, bmp.Height);
                float lv = Math.Min(1f, Math.Max(sb.LastPeakL, sb.LastPeakR));
                if (lv > 0.02f)
                {
                    var col = sb.DisplayColor;
                    var e = new RectangleF(c.Left + c.Width * 0.08f, top - desk.Height * 0.25f, c.Width * 0.84f, desk.Height * 0.9f);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(e);
                        using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(20 + 70 * lv), col), SurroundColors = new[] { Color.FromArgb(0, col) } }) g.FillPath(pb, path);
                    }
                }
            }
            // Glanzkante vorne und ein schraeger Lichtstreif
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawLine(p, 0, top, desk.Right, top);
            float sx = desk.Width * 0.62f;
            using (var b = new SolidBrush(Color.FromArgb(10, 255, 255, 255)))
                g.FillPolygon(b, new[] { new PointF(sx, top), new PointF(sx + Theme.F(90), top), new PointF(sx + Theme.F(40), bottom), new PointF(sx - Theme.F(50), bottom) });
            g.Restore(st);
        }

        // ================================================================ 284 RGB-Welle

        private float _wavePhase420;

        /// <summary>Farbe der Welle an einer Stelle im Fenster (0..1 von links nach rechts).</summary>
        internal static Color WaveColor420(float phase, float x01)
        {
            float h = ((phase + x01 * 0.75f) % 1f + 1f) % 1f * 360f;
            float k = 1f - Math.Abs(h / 60f % 2f - 1f);
            float r, gg, b;
            if (h < 60) { r = 1; gg = k; b = 0; } else if (h < 120) { r = k; gg = 1; b = 0; } else if (h < 180) { r = 0; gg = 1; b = k; }
            else if (h < 240) { r = 0; gg = k; b = 1; } else if (h < 300) { r = k; gg = 0; b = 1; } else { r = 1; gg = 0; b = k; }
            return Color.FromArgb(255, (int)(r * 255), (int)(gg * 255), (int)(b * 255));
        }

        private void RgbOverlay420(StripBase s, Graphics g)
        {
            if (!(s is ChannelStrip) || s.Parent != _channelHost || StripBase.Calm) return;
            float w = Math.Max(1f, _channelHost.ClientSize.Width);
            var pr = s.PanelRect420;
            if (pr.Width < 4) return;
            float lvl = _settings.RgbWaveBeat ? Math.Min(1f, Math.Max(s.LastPeakL, s.LastPeakR)) : 1f;
            int a = _settings.RgbWaveBeat ? (int)(70 + 185 * lvl) : 220;
            Color top = WaveColor420(_wavePhase420, (s.Left + pr.Left) / w), bot = WaveColor420(_wavePhase420, (s.Left + pr.Right) / w);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Theme.PanelRad;
            try
            {
                for (int i = 3; i >= 1; i--)
                    using (var b = new LinearGradientBrush(new RectangleF(pr.X - 1, pr.Y, pr.Width + 2, pr.Height), Color.FromArgb(a / (3 + i), top), Color.FromArgb(a / (3 + i), bot), LinearGradientMode.Horizontal))
                    using (var p = new Pen(b, Theme.F(1.2f + i * 1.3f)))
                        using (var path = Theme.Round(RectangleF.Inflate(pr, -Theme.F(0.5f), -Theme.F(0.5f)), rad)) g.DrawPath(p, path);
                using (var b = new LinearGradientBrush(new RectangleF(pr.X - 1, pr.Y, pr.Width + 2, pr.Height), Color.FromArgb(a, Theme.Lighten(top, 0.3f)), Color.FromArgb(a, Theme.Lighten(bot, 0.3f)), LinearGradientMode.Horizontal))
                using (var p = new Pen(b, Math.Max(1f, Theme.F(1.2f))))
                    using (var path = Theme.Round(RectangleF.Inflate(pr, -Theme.F(0.9f), -Theme.F(0.9f)), Math.Max(0f, rad - Theme.F(0.9f)))) g.DrawPath(p, path);
            }
            catch { }
            g.SmoothingMode = sm;
        }

        // ================================================================ 285 leuchtender Fensterrand (Windows 11)

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private int _frameColor420 = -1;
        private bool _frameSet420;

        /// <summary>Rand-Farbe setzen (COLORREF), -1 = Windows-Standard.</summary>
        private void SetFrameColor420(int colorref)
        {
            if (!Win32.IsWindows || !IsHandleCreated || _demo) return;
            if (colorref < 0 && !_frameSet420) return;
            if (colorref == _frameColor420) return;
            _frameColor420 = colorref;
            int v = colorref < 0 ? unchecked((int)0xFFFFFFFF) : colorref;
            try { DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref v, 4); _frameSet420 = colorref >= 0; } catch { }
        }

        private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>Farbe des Rands jetzt (null = Standard).</summary>
        internal Color? FrameColorNow420()
        {
            if (!_settings.FrameGlowOn) return null;
            if (_settings.FrameGlowMode == 2)
            {
                float best = 0.02f; Color? c = null;
                foreach (var ch in _settings.Channels)
                    if (!ch.Hidden && !ch.Mute && _channelPeak.TryGetValue(ch.Id, out var pk) && Math.Max(pk[0], pk[1]) > best) { best = Math.Max(pk[0], pk[1]); c = Color.FromArgb(255, Color.FromArgb(ch.Color)); }
                return c ?? Theme.Darken(Theme.Accent, 0.6f);
            }
            if (_settings.FrameGlowMode == 1)
            {
                float lv = Math.Min(1f, _master?.LevelNow ?? 0f);
                return Theme.Darken(Theme.Accent, 0.75f * (1f - lv));
            }
            return Theme.Accent;
        }

        private void TickFrame420()
        {
            var c = FrameColorNow420();
            SetFrameColor420(c.HasValue ? ColorRef(c.Value) : -1);
        }

        // ================================================================ 286 Startbild

        private SplashForm420 _splash420;

        private void ShowSplash420()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized || !Visible) return;
                var cols = _settings.Channels.Where(c => !c.Hidden).Select(c => Color.FromArgb(255, Color.FromArgb(c.Color))).Take(8).ToArray();
                var f = new SplashForm420(cols);
                var area = RectangleToScreen(ClientRectangle);
                f.Bounds = area;
                if (_demo && double.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPLASHAT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double at)) f.HoldAt = at;
                f.FormClosed += (s, e) => { if (_splash420 == f) _splash420 = null; };
                _splash420 = f;
                f.Show(this);
            }
            catch { }
        }

        // ================================================================ 288 Klang-Kunst

        private readonly Dictionary<string, double> _artSum420 = new Dictionary<string, double>();
        private int _artN420;
        private double _nextArtSec420, _nextArtFold420, _nextArtSave420;
        private bool _artDirty420;
        private ArtGalleryWindow _artWin420;

        private static string Today420 => DateTime.Now.ToString("yyyy-MM-dd");

        private void TickArt420(double now)
        {
            if (_demo || now < _nextArtSec420) return;
            _nextArtSec420 = now + 1.0;
            if (_settings.ArtDay != Today420) NewArtDay420();
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden) continue;
                float lv = 0f;
                if (!c.Mute && _channelPeak.TryGetValue(c.Id, out var pk)) lv = Math.Max(pk[0], pk[1]);
                _artSum420.TryGetValue(c.Id, out double s);
                _artSum420[c.Id] = s + lv * lv;
            }
            _artN420++;
            if (now >= _nextArtFold420) { _nextArtFold420 = now + 10; FoldArt420(); }
            if (_artDirty420 && now >= _nextArtSave420) { _nextArtSave420 = now + 60; _artDirty420 = false; ScheduleSave(); }
        }

        /// <summary>Die letzten Sekunden in die Viertelstunde eintragen (Maximum der 10-s-Mittel – passt auch nach einem Neustart).</summary>
        private void FoldArt420()
        {
            if (_artN420 == 0) return;
            int slot = Math.Max(0, Math.Min(MixerSettings.ArtSlots - 1, (int)(DateTime.Now.TimeOfDay.TotalMinutes / 15)));
            foreach (var kv in _artSum420)
            {
                byte v = SoundArt420.Level((float)Math.Sqrt(kv.Value / _artN420));
                if (v == 0) continue;
                var c = _settings.Channels.FirstOrDefault(x => x.Id == kv.Key);
                if (c == null) continue;
                if (_settings.ArtBins == null) _settings.ArtBins = new Dictionary<string, string>();
                byte[] bins;
                try { bins = _settings.ArtBins.TryGetValue(kv.Key, out string b64) ? Convert.FromBase64String(b64) : new byte[MixerSettings.ArtSlots]; } catch { bins = new byte[MixerSettings.ArtSlots]; }
                if (bins.Length != MixerSettings.ArtSlots) bins = new byte[MixerSettings.ArtSlots];
                if (v <= bins[slot]) continue;
                bins[slot] = v;
                _settings.ArtBins[kv.Key] = Convert.ToBase64String(bins);
                if (_settings.ArtColors == null) _settings.ArtColors = new Dictionary<string, int>();
                if (_settings.ArtNames == null) _settings.ArtNames = new Dictionary<string, string>();
                _settings.ArtColors[kv.Key] = c.Color;
                _settings.ArtNames[kv.Key] = c.Name;
                _artDirty420 = true;
            }
            _artSum420.Clear();
            _artN420 = 0;
            if (_settings.ArtDay == null) _settings.ArtDay = Today420;
        }

        /// <summary>Neuer Tag: das Bild vom letzten Tag speichern, dann neu sammeln.</summary>
        private void NewArtDay420()
        {
            FoldArt420();
            if (_settings.ArtDay != null && _settings.ArtBins != null)
            {
                string day = _settings.ArtDay;
                var snap = new MixerSettings { ArtBins = _settings.ArtBins, ArtColors = _settings.ArtColors, ArtNames = _settings.ArtNames, Channels = _settings.Channels };
                System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { SoundArt420.Save(snap, day); } catch { } });
            }
            _settings.ArtDay = Today420;
            _settings.ArtBins = null; _settings.ArtColors = null; _settings.ArtNames = null;
            _artDirty420 = true;
        }

        private void FlushArt420(bool save)
        {
            FoldArt420();
            if (save && _artDirty420) { _artDirty420 = false; }
        }

        private void ArtToday420()
        {
            if (_demo) { DemoArt420(); }
            else FoldArt420();
            string f = SoundArt420.Save(_settings, _settings.ArtDay ?? Today420);
            if (f == null) { ShowOsd("Klang-Kunst", "Heute lief noch nichts", -1, Theme.SoloAmber); return; }
            ShowOsd("Klang-Kunst", "Bild gespeichert", -1, Theme.LedGreen);
            if (_artWin420 != null && !_artWin420.IsDisposed) { _artWin420.Close(); ShowArtGallery420(); }
        }

        private void ShowArtGallery420()
        {
            if (_artWin420 != null && !_artWin420.IsDisposed) { _artWin420.Activate(); return; }
            var w = new ArtGalleryWindow(SoundArt420.List());
            w.OpenFolder += () => { try { Directory.CreateDirectory(SoundArt420.Folder); Process.Start("explorer.exe", SoundArt420.Folder); } catch { } };
            w.MakeToday += () => BeginInvoke(new Action(ArtToday420));
            w.FormClosed += (s, e) => { if (_artWin420 == w) _artWin420 = null; };
            _artWin420 = w;
            w.Location = OnScreen400(new Point(Left + Theme.Px(80), Top + Theme.Px(90)), w.Size);
            w.Show(this);
        }

        /// <summary>Nur Vorschau: einen Tag voller Musik erfinden.</summary>
        private void DemoArt420()
        {
            var rnd = new Random(7);
            _settings.ArtDay = Today420;
            _settings.ArtBins = new Dictionary<string, string>();
            int k = 0;
            foreach (var c in _settings.Channels.Where(x => !x.Hidden))
            {
                var b = new byte[MixerSettings.ArtSlots];
                double center = 30 + k * 9, width = 8 + k * 3;
                for (int i = 0; i < b.Length; i++)
                {
                    double v = Math.Exp(-Math.Pow((i - center) / width, 2)) * 230 + (i > 36 && i < 92 ? rnd.Next(0, 90) : 0);
                    if (k == 3 && i > 70 && i < 90) v = 200 + rnd.Next(0, 55);
                    b[i] = (byte)Math.Max(0, Math.Min(255, (int)v));
                }
                _settings.ArtBins[c.Id] = Convert.ToBase64String(b);
                k++;
            }
        }

        // ================================================================ 278 Funktions-Pakete

        internal static readonly string[] PackNames420 = { "Gamer", "Streamer", "Musik hören", "Show", "Schlicht" };
        internal static readonly string[][] Packs420 =
        {
            new[] { "Spiel-Overlay", "Spiele automatisch erkennen", "Spiele-Modus", "Hintergrund-Apps leiser", "Neues Gerät startet leise", "Panik-Taste", "Knall-Bremse pro Kanal" },
            new[] { "Stream-Limiter", "Fader-Ebene „Stream“", "Mute-Gruppen", "Szenen-Tasten in der Kopfleiste", "Ducking sichtbar", "True-Peak am MASTER", "Lautheit (LUFS) im MASTER" },
            new[] { "Loudness", "Song-Fortschritt", "„Jetzt läuft“ mit Cover in der Meterbridge", "Cover als Hintergrund", "Mehr Musik-Tasten", "Lautstärke pro Interpret merken", "Hintergrund tanzt zur Musik", "Song-Einblendung" },
            new[] { "Lichtshow", "Neon-Schild", "Himmel nach Tageszeit", "Animierte Deko", "Leuchtspur beim Fader-Ziehen", "Motor-Potis", "Kanäle leuchten im Takt", "Ambilight am Pultrand", "RGB-Welle", "Spiegelnder Tisch", "Startbild", "App-Symbol auf der Faderkappe" },
            new string[0],
        };
        internal static readonly string[] PackHints420 =
        {
            "Overlay im Spiel, Spiele erkennen, leise im Hintergrund, Knall-Bremse, Panik-Taste.",
            "Stream-Limiter, Fader-Ebene Stream, Mute-Gruppen, Szenen-Tasten, Lautheit und True-Peak.",
            "Loudness, Song-Fortschritt, Cover, mehr Musik-Tasten, Lautstärke pro Interpret.",
            "Lichtshow, Neon-Schild, Himmel, Deko, RGB-Welle, spiegelnder Tisch, Startbild …",
            "Alle Extras für Optik, Fenster und Profi-Ton aus, dazu „Nur das Wichtigste“ in den Einstellungen.",
        };

        private void ShowPacks420(Rectangle at)
        {
            var m = NewMenu();
            m.ShowImageMargin = false;
            for (int i = 0; i < PackNames420.Length; i++)
            {
                int k = i;
                m.Items.Add(new ToolStripMenuItem(PackNames420[i] + "  –  " + PackHints420[i], null, (s, e) => BeginInvoke(new Action(() => ApplyPack420(k)))));
            }
            ShowMenuAt(m, at);
        }

        private void ApplyPack420(int k)
        {
            var pages = (IReadOnlyList<SPage>)_settingsWin?.Pages ?? BuildSettingsPages();
            if (k == 4)
            {
                AllExtrasOff410();
                if (!_settings.SimpleSettings) SetSimple400(true);
                return;
            }
            var rows = pages.SelectMany(p => p.Rows.OfType<SToggle>()).ToList();
            var todo = new List<SToggle>();
            foreach (var label in Packs420[k])
            {
                var t = rows.FirstOrDefault(r => r.Label == label);
                if (t != null && t.IsEnabled && Now410(t) is bool on && !on) todo.Add(t);
            }
            if (todo.Count == 0) { ShowOsd("Paket " + PackNames420[k], "Ist schon alles an", -1, Theme.Accent); return; }
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Paket „" + PackNames420[k] + "“ einschalten?\n\n" + string.Join("\n", todo.Select(t => "• " + t.Label)) +
                    "\n\nVorher wird gesichert (OPTIONEN → Sichern → Sicherung zurückholen). Ausschalten geht jederzeit einzeln.", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!_demo && !Program.SafeMode) { RememberWindow(); _settings.Save(); Update410.BackupSettings("vor-paket"); }
            int n = 0;
            foreach (var t in todo) { try { t.Set(true); n++; } catch { } }
            ScheduleSave();
            ShowOsd("Paket " + PackNames420[k], n + " Schalter eingeschaltet", -1, Theme.LedGreen);
        }

        // ================================================================ 279 Doppelklick auf Mischpult-Dateien

        private void EnsureFileAssoc420()
        {
            string exe = FileAssoc420.ExePath;
            if (!FileAssoc420.IsRegistered(exe)) FileAssoc420.Register(exe);
        }

        private void SetFileAssoc420(bool on)
        {
            _settings.FileAssocOn = on;
            ScheduleSave();
            if (_demo) return;
            if (on) { string err = FileAssoc420.Register(FileAssoc420.ExePath); if (err != null) ShowOsd("Doppelklick", "Ging nicht: " + err, -1, Theme.MuteRed); }
            else FileAssoc420.Remove();
        }

        /// <summary>Datei von aussen (Doppelklick): Umzugs-Paket oder EQ-Presets.</summary>
        private void OpenFile420(string path)
        {
            if (!FileAssoc420.Allowed(path) || !File.Exists(path)) return;
            RestoreFromTray();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == Package410.Ext) ImportPackage410(path);
            else if (ext == ".mpeq") ImportPresetsFile420(path);
        }

        private void ImportPresetsFile420(string path)
        {
            List<EqPreset> l = null;
            try { l = PresetsFromJson(File.ReadAllText(path)); } catch { }
            if (l == null || l.Count == 0) { MessageBox.Show(this, "In der Datei stehen keine EQ-Presets.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, l.Count + " EQ-Presets aus „" + Path.GetFileName(path) + "“ übernehmen?", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int n = MergePresets400(_settings.EqPresets, l);
            ScheduleSave();
            ShowOsd("EQ-Presets", n + " geladen", -1, Theme.Accent);
        }

        // ================================================================ 277 Schalter im "Was ist neu"-Fenster

        /// <summary>Neuheit -> Name des Schalters in den Einstellungen (sonst gleicher Name).</summary>
        internal static readonly Dictionary<string, string> NewsRows420 = new Dictionary<string, string>
        {
            // 4.3
            { "Plattenspieler", "Animierte Deko" },
            // 4.2
            { "Leuchtender Fensterrand", "Leuchtender Fensterrand (Windows 11)" }, { "Schalter im Was-ist-neu-Fenster", "Schalter im Fenster „Was ist neu“" },
            // 4.1
            { "Ein-Klick-Fenster", "Ein-Klick-Fenster beim Start" }, { "Download-Ordner im Blick", "Neue Version im Download-Ordner erkennen" },
            { "Sicherung vor dem Update", "Sicherung vor jedem Update" }, { "Sauber deinstallieren", "Beim Deinstallieren nach den Einstellungen fragen" },
            { "Abgesicherter Start", "Abgesicherter Start mit der Umschalt-Taste" }, { "Was ist eingeschaltet", "Übersicht „Was ist eingeschaltet“" },
            // 4.0
            { "Kanal-Suche", "Kanal-Suche (Strg+F)" }, { "Lautstärke pro Interpret", "Lautstärke pro Interpret merken" }, { "Was läuft – pro Kanal", "Was läuft, pro Kanal" },
            { "OBS-Steuerung", "OBS Studio" }, { "WLED-Raumlicht", "WLED-Lichter" }, { "Geräte-Wechsel-Schutz", "Neues Gerät startet leise" },
            { "EQ-Presets teilen", "EQ-Presets als Datei" }, { "Design folgt Windows", "Design folgt Windows (hell/dunkel)" }, { "Apps ziehen", "Apps zwischen Kanälen ziehen" },
            { "Eigene Tasten", "Eigene Tasten in der Kopfleiste" }, { "Was ist neu", "Nach einem Update zeigen, was neu ist" }, { "Hörtest mit EQ", "Hör-EQ" },
        };

        /// <summary>Schalter zu einer Neuheit (null = keiner, z. B. bei Auswahlen).</summary>
        private Func<string, SToggle> NewsResolver420()
        {
            if (!_settings.NewsTogglesOn) return null;
            var pages = BuildSettingsPages();
            var rows = pages.SelectMany(p => p.Rows.OfType<SToggle>()).GroupBy(r => r.Label).ToDictionary(g => g.Key, g => g.First());
            return name =>
            {
                string label = NewsRows420.TryGetValue(name, out string l) ? l : name;
                return rows.TryGetValue(label, out var t) ? t : null;
            };
        }

        // ================================================================ Einstellungen

        /// <summary>Seite Aussehen: 4.2 – Optik.</summary>
        private void AddLook420(SPage p)
        {
            p.Section("4.2 – Optik");
            p.Info("Neue Designs, Hintergründe und Wände", () => "Synthwave, Retro-Game und Cockpit stehen oben unter „Design“, Nordlicht, Unterwasser, Kaminfeuer und Matrix-Regen unter „Bewegter Hintergrund“, Samtvorhang, Sichtbeton und Kacheln unter „Wand hinter dem Pult“.");
            p.Toggle("Spiegelnder Tisch", "Das Pult steht auf einer glänzenden Fläche: Unten spiegeln sich die Kanalzüge, und ihr Licht fällt auf den Tisch.",
                () => _settings.DeskMirrorOn, v => { _settings.DeskMirrorOn = v; ApplyCables346(); _channelHost?.Invalidate(); _outputHost?.Invalidate(); ScheduleSave(); });
            p.Toggle("RGB-Welle", "Ein Regenbogen läuft wie bei Gaming-Tastaturen über die Ränder aller Kanäle.",
                () => _settings.RgbWaveOn, v => { _settings.RgbWaveOn = v; ApplyLook420(_settings, false); _channelHost?.Invalidate(true); ScheduleSave(); });
            p.Toggle("… im Takt der Musik", "Die Welle läuft schneller, wenn es laut wird, und jeder Kanal leuchtet so hell, wie er gerade spielt.",
                () => _settings.RgbWaveBeat, v => { _settings.RgbWaveBeat = v; ScheduleSave(); }).Enabled = () => _settings.RgbWaveOn;
            p.Toggle("Leuchtender Fensterrand (Windows 11)", "Der Rahmen des Pult-Fensters leuchtet in Farbe. Geht nur mit Windows 11 und der normalen Titelleiste.",
                () => _settings.FrameGlowOn, v => { _settings.FrameGlowOn = v; TickFrame420(); ScheduleSave(); });
            p.Choice("Farbe des Fensterrands", () => _settings.FrameGlowMode == 1 ? "Akzentfarbe, wird mit der Musik heller und dunkler." : _settings.FrameGlowMode == 2 ? "Farbe des Kanals, der gerade am lautesten ist." : "Immer in der Akzentfarbe.",
                MixerSettings.FrameGlowModeNames, () => _settings.FrameGlowMode, i => { _settings.FrameGlowMode = i; TickFrame420(); ScheduleSave(); }).Visible = () => _settings.FrameGlowOn;
            p.Toggle("Startbild", "Beim Start fahren kurz die Fader hoch und das Mischpult-Logo leuchtet auf (etwa 1,5 Sekunden). Ein Klick schließt es sofort.",
                () => _settings.SplashOn, v => { _settings.SplashOn = v; ScheduleSave(); });
            p.Toggle("App-Symbol auf der Faderkappe", "Das Symbol der App, die gerade spielt, sitzt klein auf der Faderkappe – so siehst du sofort, welcher Fader was ist.",
                () => _settings.CapIconOn, v => { _settings.CapIconOn = v; ApplyLook420(_settings, false); foreach (var s in _channelStrips) s.InvalidateChrome(); ScheduleSave(); });
        }

        /// <summary>Seite Fenster &amp; Verbindungen: Klang-Kunst.</summary>
        private void AddExtras420(SPage p)
        {
            p.Section("4.2 – Klang-Kunst");
            p.Toggle("Klang-Kunst", "Jeden Abend entsteht aus dem, was du gehört hast, ein Bild: Ringe wie Jahresringe, jede Farbe ein Kanal, einmal rund um den Tag. Die Bilder landen in " + MixerSettings.ShotFolderName + "\\Klang-Kunst.",
                () => _settings.SoundArtOn, v => { _settings.SoundArtOn = v; if (v && _settings.ArtDay == null) _settings.ArtDay = Today420; ScheduleSave(); });
            p.Button("Klang-Kunst-Galerie", "Alle Bilder auf einen Blick – Klick öffnet das Bild.", "Öffnen", r => ShowArtGallery420(), () => { int n = SoundArt420.List().Count; return n == 0 ? "noch keine" : n + (n == 1 ? " Bild" : " Bilder"); })
                .Visible = () => _settings.SoundArtOn;
            p.Button("Heute als Bild", "Das Bild von heute schon jetzt speichern (es wird um Mitternacht noch einmal fertig gemalt).", "Speichern", r => ArtToday420())
                .Visible = () => _settings.SoundArtOn;
        }

        /// <summary>Seite Allgemein: 4.2 – Komfort.</summary>
        private void AddComfort420(SPage p)
        {
            p.Section("4.2 – Komfort");
            p.Toggle("Schalter im Fenster „Was ist neu“", "Im Fenster „Was ist neu“ steht neben jeder Neuheit gleich ihr Schalter – kein Suchen in den Einstellungen.",
                () => _settings.NewsTogglesOn, v => { _settings.NewsTogglesOn = v; ScheduleSave(); });
            p.Toggle("Funktions-Pakete", "Mit einem Klick passende Funktionen einschalten: Gamer, Streamer, Musik hören, Show oder Schlicht. Vorher wird gesichert.",
                () => _settings.PacksOn, v => { _settings.PacksOn = v; ScheduleSave(); });
            p.Button("Paket einschalten", "Zeigt vorher, was eingeschaltet wird.", "Auswählen ▾", r => ShowPacks420(r)).Visible = () => _settings.PacksOn;
            p.Toggle("Doppelklick auf Mischpult-Dateien", "Umzugs-Pakete (.mischpult) und EQ-Presets (.mpeq) öffnen sich per Doppelklick direkt im Mischpult.",
                () => _settings.FileAssocOn, v => SetFileAssoc420(v)).Enabled = () => Win32.IsWindows || _demo;
        }

        // ================================================================ Vorschau

        private void DemoShown420()
        {
            if (DemoOn("ARTWIN")) DemoLater(() => { DemoArt420(); ShowArtGallery420(); _artWin420.Location = DemoXY("ARTXY", new Point(700, 120)); });
            string artFile = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_ARTFILE");
            if (!string.IsNullOrEmpty(artFile)) DemoLater(() =>
            {
                DemoArt420();
                using (var b = SoundArt420.Render(SoundArt420.Rings(_settings), Today420)) b.Save(artFile, System.Drawing.Imaging.ImageFormat.Png);
            });
            if (DemoOn("PACKMENU")) DemoLater(() => { ShowSettings("Allgemein"); });
            if (DemoOn("DUCKTRIGGERS") && _settings.Channels.Count > 3)   // 4.2.1: zwei Ausloeser
            {
                _settings.DuckTriggers = new List<string> { _settings.Channels[1].Id, _settings.Channels[2].Id };
                _settings.DuckTrigger = _settings.Channels[1].Id;
                _settings.Normalize();
            }
            if (DemoOn("DUCKMIC"))   // 4.2.2: das Mikrofon als Ausloeser
            {
                _settings.DuckTriggers = null;
                _settings.DuckTrigger = MixerSettings.DuckMicId;
                _settings.Normalize();
                if (DemoOn("MICLINES")) { _settings.DuckLinesOn = true; MeterBridge.DuckLinesOn = true; }
            }
            DemoShown423();
            DemoShown430();
            if (DemoOn("DUCKDLG")) DemoLater(ShowDuckDialog);
        }
    }
}
