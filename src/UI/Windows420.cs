using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Native;

namespace Mischpult.UI
{
    // ======================================================================== 279 Doppelklick auf Mischpult-Dateien

    /// <summary>4.2: Umzugs-Pakete (.mischpult) und EQ-Presets (.mpeq) per Doppelklick im Mischpult oeffnen (nur fuer diesen Benutzer).</summary>
    internal static class FileAssoc420
    {
        internal static readonly string[][] Types =
        {
            new[] { ".mischpult", "Mischpult.Umzugspaket", "Mischpult-Umzugs-Paket" },
            new[] { ".mpeq", "Mischpult.EqPresets", "Mischpult-EQ-Presets" },
        };

        [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);

        /// <summary>Welche EXE oeffnet die Dateien: die installierte, sonst diese.</summary>
        public static string ExePath => Installer.IsInstalled ? Installer.InstallExe : Application.ExecutablePath;

        public static string Command(string exe) => "\"" + exe + "\" --open \"%1\"";

        /// <summary>Eingetragen und zeigt auf diese EXE?</summary>
        public static bool IsRegistered(string exe)
        {
            if (!Win32.IsWindows) return false;
            try
            {
                foreach (var t in Types)
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + t[1] + @"\shell\open\command"))
                        if (!string.Equals(k?.GetValue("") as string, Command(exe), StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
            catch { return false; }
        }

        public static string Register(string exe)
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            try
            {
                foreach (var t in Types)
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + t[0])) k.SetValue("", t[1]);
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + t[1])) k.SetValue("", t[2]);
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + t[1] + @"\DefaultIcon")) k.SetValue("", "\"" + exe + "\",0");
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + t[1] + @"\shell\open\command")) k.SetValue("", Command(exe));
                }
                Notify();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static void Remove()
        {
            if (!Win32.IsWindows) return;
            try
            {
                foreach (var t in Types)
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + t[0], true))
                        if (k != null && string.Equals(k.GetValue("") as string, t[1], StringComparison.Ordinal)) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + t[0], false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + t[1], false);
                }
                Notify();
            }
            catch { }
        }

        private static void Notify() { try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { } }

        /// <summary>Pfad hinter "--open" (oder null).</summary>
        public static string OpenArg(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("--open", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// <summary>Darf diese Datei von aussen geoeffnet werden?</summary>
        public static bool Allowed(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 500) return false;
            string ext = Path.GetExtension(path);
            return Types.Any(t => string.Equals(t[0], ext, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ======================================================================== 288 Klang-Kunst

    /// <summary>
    /// 4.2: aus dem, was du an einem Tag gehoert hast, ein Bild – Ringe wie Jahresringe, jeder Ring ein Kanal,
    /// einmal rund um den Tag (oben Mitternacht, im Uhrzeigersinn), je lauter, desto kraeftiger.
    /// </summary>
    internal static class SoundArt420
    {
        public static string Folder => Path.Combine(MixerSettings.ShotFolderPath, "Klang-Kunst");

        public static string FileFor(string day) => Path.Combine(Folder, "Klang-Kunst " + day + ".png");

        /// <summary>Wert 0..255 aus einem Pegel (linear, 1 = 0 dBFS): −60 dB … 0 dB.</summary>
        public static byte Level(float peak)
        {
            if (peak <= 0.001f || float.IsNaN(peak)) return 0;
            double db = 20 * Math.Log10(Math.Min(1.0, peak));
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round((db + 60) / 60 * 255)));
        }

        public sealed class Ring { public string Name; public Color Color; public byte[] Bins; public int Sum; }

        /// <summary>Ringe aus den gespeicherten Werten (lauteste Kanaele zuerst, hoechstens 10).</summary>
        public static List<Ring> Rings(MixerSettings s)
        {
            var l = new List<Ring>();
            if (s.ArtBins == null) return l;
            foreach (var kv in s.ArtBins)
            {
                byte[] b;
                try { b = Convert.FromBase64String(kv.Value); } catch { continue; }
                if (b.Length != MixerSettings.ArtSlots) continue;
                int sum = b.Sum(x => (int)x);
                if (sum == 0) continue;
                var ch = s.Channels?.FirstOrDefault(c => c.Id == kv.Key);
                int col = ch != null ? ch.Color : s.ArtColors != null && s.ArtColors.TryGetValue(kv.Key, out int c2) ? c2 : unchecked((int)0xff8090a0);
                string name = ch?.Name ?? (s.ArtNames != null && s.ArtNames.TryGetValue(kv.Key, out string n2) ? n2 : "?");
                l.Add(new Ring { Name = name, Color = Color.FromArgb(255, Color.FromArgb(col)), Bins = b, Sum = sum });
            }
            return l.OrderByDescending(r => r.Sum).Take(10).ToList();
        }

        /// <summary>Das Bild zeichnen (size x size Pixel).</summary>
        public static Bitmap Render(List<Ring> rings, string day, int size = 1200)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(0x0b, 0x0c, 0x12), Color.FromArgb(0x16, 0x10, 0x1e), 45f)) g.FillRectangle(b, 0, 0, size, size);
                float cx = size / 2f, cy = size * 0.465f;
                float r0 = size * 0.095f, rMax = size * 0.355f;
                int n = Math.Max(1, rings.Count);
                float rw = (rMax - r0) / n;
                for (int k = 0; k < rings.Count; k++)
                {
                    var ring = rings[k];
                    float rin = r0 + k * rw + rw * 0.08f, rout = r0 + (k + 1) * rw - rw * 0.08f;
                    // Grundring ganz schwach
                    using (var p = new Pen(Color.FromArgb(22, ring.Color), rout - rin)) g.DrawEllipse(p, cx - (rin + rout) / 2, cy - (rin + rout) / 2, rin + rout, rin + rout);
                    for (int i = 0; i < MixerSettings.ArtSlots; i++)
                    {
                        int v = ring.Bins[i];
                        if (v == 0) continue;
                        float f = v / 255f;
                        float a0 = -90f + i * 360f / MixerSettings.ArtSlots, sweep = 360f / MixerSettings.ArtSlots + 0.4f;
                        float grow = (rout - rin) * (0.35f + 0.65f * f);
                        float rm = rin + grow / 2f + (rout - rin - grow) / 2f;
                        using (var p = new Pen(Color.FromArgb((int)(40 + 215 * f * f), Lighten(ring.Color, 0.25f * f)), grow))
                            g.DrawArc(p, cx - rm, cy - rm, rm * 2, rm * 2, a0, sweep);
                    }
                }
                // Stunden aussen
                using (var f = new Font("Segoe UI", size / 60f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                using (var tp = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f))
                {
                    for (int hr = 0; hr < 24; hr++)
                    {
                        double ang = (-90 + hr * 15) * Math.PI / 180;
                        float r1 = rMax + size * 0.012f, r2 = rMax + (hr % 6 == 0 ? size * 0.03f : size * 0.02f);
                        g.DrawLine(tp, cx + (float)Math.Cos(ang) * r1, cy + (float)Math.Sin(ang) * r1, cx + (float)Math.Cos(ang) * r2, cy + (float)Math.Sin(ang) * r2);
                        if (hr % 6 != 0) continue;
                        float rt = rMax + size * 0.05f;
                        string t = hr.ToString("00") + ":00";
                        var sz = g.MeasureString(t, f);
                        g.DrawString(t, f, tb, cx + (float)Math.Cos(ang) * rt - sz.Width / 2, cy + (float)Math.Sin(ang) * rt - sz.Height / 2);
                    }
                    // Mitte: Datum
                    DateTime d;
                    string ds = DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("dd.MM.yyyy") : day;
                    using (var big = new Font("Segoe UI", size / 38f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        var sz = g.MeasureString(ds, big);
                        using (var wb = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.DrawString(ds, big, wb, cx - sz.Width / 2, cy - sz.Height / 2);
                    }
                    // Legende unten
                    float ly = size * 0.905f, lx = size * 0.08f;
                    foreach (var ring in rings)
                    {
                        float w = g.MeasureString(ring.Name, f).Width + size * 0.045f;
                        if (lx + w > size * 0.92f) { lx = size * 0.08f; ly += size * 0.026f; }
                        using (var cb = new SolidBrush(ring.Color)) g.FillEllipse(cb, lx, ly + size * 0.004f, size * 0.014f, size * 0.014f);
                        g.DrawString(ring.Name, f, tb, lx + size * 0.02f, ly);
                        lx += w;
                    }
                    using (var sf = new Font("Segoe UI", size / 75f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fb = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                        g.DrawString("Mischpult · Klang-Kunst · made by " + Program.Author, sf, fb, size * 0.08f, size * 0.965f);
                }
            }
            return bmp;
        }

        private static Color Lighten(Color c, float f) => Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));

        /// <summary>Bild des Tages speichern. Liefert den Pfad oder null.</summary>
        public static string Save(MixerSettings s, string day)
        {
            var rings = Rings(s);
            if (rings.Count == 0) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                string f = FileFor(day);
                using (var bmp = Render(rings, day)) bmp.Save(f, ImageFormat.Png);
                return f;
            }
            catch { return null; }
        }

        /// <summary>Gespeicherte Bilder, neueste zuerst.</summary>
        public static List<string> List()
        {
            try { return Directory.Exists(Folder) ? Directory.GetFiles(Folder, "Klang-Kunst *.png").OrderByDescending(x => x, StringComparer.Ordinal).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }
    }

    /// <summary>4.2: kleine Galerie der Klang-Kunst-Bilder (Klick oeffnet das Bild).</summary>
    internal sealed class ArtGalleryWindow : PultWin
    {
        private readonly List<string> _files;
        private readonly Dictionary<string, Bitmap> _thumbs = new Dictionary<string, Bitmap>();
        private readonly List<KeyValuePair<RectangleF, string>> _hits = new List<KeyValuePair<RectangleF, string>>();
        private float _scroll, _contentH;
        public event Action OpenFolder, MakeToday;
        private RectangleF _folderR, _todayR;

        public ArtGalleryWindow(List<string> files) : base("Klang-Kunst", 640, 520) { _files = files; }

        private Bitmap Thumb(string f)
        {
            if (_thumbs.TryGetValue(f, out var b)) return b;
            try
            {
                using (var src = new Bitmap(f))
                {
                    b = new Bitmap(Theme.Px(180), Theme.Px(180));
                    using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height)); }
                }
            }
            catch { b = null; }
            _thumbs[f] = b;
            return b;
        }

        protected override string ContentZone(Point p)
        {
            if (_folderR.Contains(p)) return "folder";
            if (_todayR.Contains(p)) return "today";
            foreach (var h in _hits) if (h.Key.Contains(p)) return "f:" + h.Value;
            return null;
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;
            if (_folderR.Contains(e.Location)) { OpenFolder?.Invoke(); return true; }
            if (_todayR.Contains(e.Location)) { MakeToday?.Invoke(); return true; }
            foreach (var h in _hits)
                if (h.Key.Contains(e.Location)) { try { Process.Start(new ProcessStartInfo(h.Value) { UseShellExecute = true }); } catch { } return true; }
            return false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Max(0, Math.Min(Math.Max(0, _contentH - Content.Height + Theme.F(40)), _scroll - e.Delta / 120f * Theme.F(60)));
            Invalidate();
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            _hits.Clear();
            _todayR = new RectangleF(r.X, r.Y, Theme.F(170), Theme.F(28));
            _folderR = new RectangleF(r.X + Theme.F(178), r.Y, Theme.F(150), Theme.F(28));
            Theme.DrawKey(g, _todayR, "HEUTE ALS BILD", false, Theme.Accent, Hover == "today");
            Theme.DrawKey(g, _folderR, "ORDNER ÖFFNEN", false, Theme.Accent, Hover == "folder");
            var body = new RectangleF(r.X, r.Y + Theme.F(38), r.Width, r.Height - Theme.F(38));
            if (_files.Count == 0)
            {
                Label(g, "Noch keine Bilder. Das erste entsteht heute um Mitternacht – oder jetzt mit „Heute als Bild“.", body, Theme.TextDim, 10.5f, false, Theme.Wrap);
                return;
            }
            var st = g.Save();
            g.SetClip(body, CombineMode.Intersect);
            float cell = Theme.F(196), gap = Theme.F(10);
            int cols = Math.Max(1, (int)((body.Width + gap) / (cell + gap)));
            float x0 = body.X + (body.Width - (cols * cell + (cols - 1) * gap)) / 2f;
            for (int i = 0; i < _files.Count; i++)
            {
                float x = x0 + (i % cols) * (cell + gap), y = body.Y + (i / cols) * (cell + Theme.F(24) + gap) - _scroll;
                var rr = new RectangleF(x, y, cell, cell);
                if (rr.Bottom < body.Y || rr.Y > body.Bottom) continue;
                var t = Thumb(_files[i]);
                Theme.FillRound(g, Theme.Well, rr, Theme.F(4));
                if (t != null) g.DrawImage(t, RectangleF.Inflate(rr, -Theme.F(8), -Theme.F(8)));
                if (Hover == "f:" + _files[i]) Theme.StrokeRound(g, Theme.Accent, Theme.F(1.5f), rr, Theme.F(4));
                string name = Path.GetFileNameWithoutExtension(_files[i]).Replace("Klang-Kunst ", "");
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) name = day.ToString("dd.MM.yyyy");
                Label(g, name, new RectangleF(x, rr.Bottom + Theme.F(2), cell, Theme.F(20)), Theme.TextDim, 9.5f, false, Theme.Center);
                _hits.Add(new KeyValuePair<RectangleF, string>(rr, _files[i]));
            }
            _contentH = (int)Math.Ceiling(_files.Count / (double)cols) * (cell + Theme.F(24) + gap);
            g.Restore(st);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var b in _thumbs.Values) b?.Dispose();
            base.Dispose(disposing);
        }
    }

    // ======================================================================== 286 Startbild

    /// <summary>4.2: Startbild ueber dem Pult – Fader fahren hoch, das Logo leuchtet auf, dann blendet es weg (etwa 1,6 s).</summary>
    internal sealed class SplashForm420 : Form
    {
        private readonly Color[] _colors;
        private readonly Timer _timer = new Timer { Interval = 30 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        /// <summary>Nur Vorschau: bei dieser Zeit (s) anhalten.</summary>
        public double HoldAt = -1;
        public const double Duration = 1.6;

        public SplashForm420(Color[] colors)
        {
            _colors = colors.Length > 0 ? colors : new[] { Theme.Accent };
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Body;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer.Tick += (s, e) =>
            {
                double t = T;
                if (t >= Duration && HoldAt < 0) { _timer.Stop(); Close(); return; }
                if (t > Duration - 0.35 && HoldAt < 0) { try { Opacity = Math.Max(0, (Duration - t) / 0.35); } catch { } }
                Invalidate();
            };
            Click += (s, e) => Close();
        }

        private double T => HoldAt >= 0 ? HoldAt : _clock.Elapsed.TotalSeconds;

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e) { base.OnShown(e); _timer.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            double t = T;
            var r = ClientRectangle;
            using (var b = new LinearGradientBrush(new Rectangle(0, -1, r.Width, r.Height + 2), Theme.Lighten(Theme.Body, 0.05f), Theme.Darken(Theme.Body, 0.2f), LinearGradientMode.Vertical)) g.FillRectangle(b, r);
            float cx = r.Width / 2f, cy = r.Height / 2f;
            // Fader fahren hoch (versetzt)
            int n = Math.Min(8, Math.Max(5, _colors.Length));
            float fw = Theme.F(18), gap = Theme.F(22), fh = Math.Min(r.Height * 0.32f, Theme.F(170));
            float x0 = cx - (n * fw + (n - 1) * gap) / 2f, yb = cy - Theme.F(18);
            for (int i = 0; i < n; i++)
            {
                double k = Math.Max(0, Math.Min(1, (t - i * 0.06) / 0.7));
                double ease = 1 - Math.Pow(1 - k, 3);
                float target = (float)(0.45 + 0.45 * Math.Abs(Math.Sin(i * 1.7 + 0.5)));
                float pos = (float)(ease * target);
                float x = x0 + i * (fw + gap);
                var slot = new RectangleF(x + fw / 2 - Theme.F(1.5f), yb - fh, Theme.F(3), fh);
                Theme.FillRound(g, Theme.Darken(Theme.Body, 0.4f), slot, Theme.F(1.5f));
                var c = _colors[i % _colors.Length];
                float capY = yb - fh * pos;
                using (var fb = new SolidBrush(Color.FromArgb(170, c))) g.FillRectangle(fb, slot.X, capY, slot.Width, yb - capY);
                var cap = new RectangleF(x, capY - Theme.F(7), fw, Theme.F(14));
                Theme.DrawFaderCap(g, cap, false, false, c);
                Theme.DrawCapInsert(g, cap, c);
            }
            // Logo leuchtet auf
            float a = (float)Math.Max(0, Math.Min(1, (t - 0.45) / 0.45));
            if (a > 0)
            {
                var f = Theme.Ui(30f, FontStyle.Bold);
                string logo = "MISCHPULT";
                float sp = Theme.F(6);
                float w = Theme.MeasureSpaced(g, logo, f, sp);
                float lx = cx - w / 2f, ly = cy + Theme.F(4);
                for (int k = 3; k >= 1; k--)
                    Theme.DrawSpaced(g, logo, f, Color.FromArgb((int)(a * 26), Theme.Accent), lx - k * Theme.F(0.8f), ly - k * Theme.F(0.5f), sp);
                Theme.DrawSpaced(g, logo, f, Color.FromArgb((int)(a * 255), Theme.Text), lx, ly, sp);
                using (var sb = new SolidBrush(Color.FromArgb((int)(a * 200), Theme.TextDim)))
                    Theme.Str(g, "made by " + Program.Author + "  ·  " + Program.Version, Theme.Ui(12f), sb, new RectangleF(0, ly + Theme.F(46), r.Width, Theme.F(22)), Theme.Center);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
