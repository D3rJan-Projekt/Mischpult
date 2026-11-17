using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0 (finale): weiches Umblenden beim Design-Wechsel – das alte Bild liegt kurz obendrauf und wird durchsichtig.</summary>
    internal sealed class FadeForm : Form
    {
        private readonly Bitmap _shot;
        private readonly Timer _timer = new Timer { Interval = 16 };
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        public const double Seconds = 0.6;

        public FadeForm(Bitmap shot, Rectangle screen)
        {
            _shot = shot;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = screen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Opacity = 1.0;
            _timer.Tick += (s, e) =>
            {
                double t = _clock.Elapsed.TotalSeconds / Seconds;
                if (t >= 1) { _timer.Stop(); Close(); return; }
                double u = t * t * (3 - 2 * t);
                try { Opacity = Math.Max(0.0, 1.0 - u); } catch { }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;   // LAYERED, TRANSPARENT, NOACTIVATE, TOOLWINDOW
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e) { base.OnShown(e); _clock.Restart(); _timer.Start(); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(_shot, 0, 0); }
        protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Dispose(); _shot.Dispose(); base.OnFormClosed(e); }
    }

    /// <summary>
    /// 4.0 (finale): Musik-Wallpaper – ein Fenster hinter den Desktop-Symbolen (im "WorkerW" von Windows), das das eigene
    /// Hintergrundbild zeigt und darueber das Spektrum der Musik. Beim Schliessen zeichnet Windows den Desktop neu.
    /// </summary>
    internal sealed class WallpaperForm : Form
    {
        private Bitmap _bg;
        private float[] _lvl = new float[0], _pk = new float[0];
        private int _style;
        private Rectangle _barsArea;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        public WallpaperForm(int style)
        {
            _style = style;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }   // NOACTIVATE, TOOLWINDOW
        }
        protected override bool ShowWithoutActivation => true;

        public int Style { get => _style; set { _style = value; Invalidate(); } }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint param, System.Text.StringBuilder pv, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfoSet(uint action, uint param, string pv, uint flags);

        /// <summary>Fenster hinter die Desktop-Symbole haengen. false = ging nicht.</summary>
        public bool AttachToDesktop()
        {
            if (!Native.Win32.IsWindows) return false;
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero) return false;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);   // WorkerW hinter den Symbolen erzeugen
                IntPtr worker = IntPtr.Zero;
                EnumWindows((top, l) =>
                {
                    if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                        worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    return true;
                }, IntPtr.Zero);
                if (worker == IntPtr.Zero) worker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);   // neuere Windows-11-Versionen
                if (worker == IntPtr.Zero) return false;
                var ps = Screen.PrimaryScreen.Bounds; var vs = SystemInformation.VirtualScreen;
                LoadWallpaper(ps.Size);
                CreateHandle();
                SetParent(Handle, worker);
                Bounds = new Rectangle(ps.X - vs.X, ps.Y - vs.Y, ps.Width, ps.Height);
                Show();
                return true;
            }
            catch { return false; }
        }

        private void LoadWallpaper(Size size)
        {
            try
            {
                var sb = new System.Text.StringBuilder(520);
                if (SystemParametersInfo(0x0073, (uint)sb.Capacity, sb, 0) && System.IO.File.Exists(sb.ToString()))
                    using (var img = Image.FromFile(sb.ToString())) _bg = Fit(img, size);
            }
            catch { _bg = null; }
        }

        private static Bitmap Fit(Image img, Size size)
        {
            var b = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float k = Math.Max(size.Width / (float)img.Width, size.Height / (float)img.Height);
                g.DrawImage(img, new RectangleF((size.Width - img.Width * k) / 2f, (size.Height - img.Height * k) / 2f, img.Width * k, img.Height * k));
            }
            return b;
        }

        /// <summary>Neue Pegel (0..1 je Band) – zeichnet nur den unteren Bereich neu.</summary>
        public void Feed(float[] lvl, float[] pk)
        {
            _lvl = lvl; _pk = pk;
            if (!_barsArea.IsEmpty) Invalidate(_barsArea); else Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_bg != null) g.DrawImageUnscaled(_bg, 0, 0);
            else using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(0x10, 0x14, 0x22), Color.FromArgb(0x04, 0x05, 0x0a), LinearGradientMode.Vertical)) g.FillRectangle(b, ClientRectangle);
            var area = new RectangleF(ClientSize.Width * 0.06f, ClientSize.Height * 0.68f, ClientSize.Width * 0.88f, ClientSize.Height * 0.22f);
            _barsArea = Rectangle.Inflate(Rectangle.Ceiling(area), 4, 30);
            DrawMusic(g, area, _lvl, _pk, _style, Clock.Elapsed.TotalSeconds);
        }

        /// <summary>Spektrum fuer den Schreibtisch: leuchtende Balken mit Spiegelung oder eine kleine Skyline.</summary>
        internal static void DrawMusic(Graphics g, RectangleF area, float[] lvl, float[] pk, int style, double t)
        {
            int n = lvl?.Length ?? 0;
            if (n == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float slot = area.Width / n, w = slot * (style == 1 ? 0.86f : 0.62f);
            for (int i = 0; i < n; i++)
            {
                float v = Math.Max(0f, Math.Min(1f, lvl[i]));
                float h = Math.Max(2f, area.Height * v), x = area.X + i * slot + (slot - w) / 2f;
                Color c = style == 1 ? Color.FromArgb(0x24, 0x2a, 0x44) : Theme.Mix(Theme.Accent, Color.FromArgb(0x7c, 0xd8, 0xff), i / (float)Math.Max(1, n - 1));
                var r = new RectangleF(x, area.Bottom - h, w, h);
                if (style == 1)
                {
                    using (var b = new SolidBrush(Color.FromArgb(220, c))) g.FillRectangle(b, r);
                    int rows = (int)(h / 9);
                    for (int k = 0; k < rows; k++)
                        for (int m = 0; m < Math.Max(1, (int)(w / 8)); m++)
                            if (((i * 31 + k * 7 + m * 13) % 10) < 3 + (int)(6 * v))
                                using (var b = new SolidBrush(Color.FromArgb(200, 0xff, 0xd6, 0x7a))) g.FillRectangle(b, x + 3 + m * 8, r.Y + 4 + k * 9, 3, 4);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, c))) g.FillRectangle(b, RectangleF.Inflate(r, 3, 3));
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.Lighten(c, 0.35f), Color.FromArgb(200, c), LinearGradientMode.Vertical)) g.FillRectangle(b, r);
                    // Spiegelung
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, area.Bottom, r.Width, h * 0.4f + 1), Color.FromArgb(70, c), Color.FromArgb(0, c), LinearGradientMode.Vertical))
                        g.FillRectangle(b, r.X, area.Bottom + 2, r.Width, h * 0.4f);
                    if (pk != null && i < pk.Length && pk[i] > v + 0.01f)
                        using (var b = new SolidBrush(Color.FromArgb(220, Theme.Lighten(c, 0.5f)))) g.FillRectangle(b, x, area.Bottom - area.Height * Math.Min(1f, pk[i]) - 3, w, 3);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _bg?.Dispose();
            // Desktop neu zeichnen lassen (sonst bleibt das letzte Bild stehen)
            try
            {
                if (Native.Win32.IsWindows)
                {
                    var sb = new System.Text.StringBuilder(520);
                    if (SystemParametersInfo(0x0073, (uint)sb.Capacity, sb, 0)) SystemParametersInfoSet(0x0014, 0, sb.ToString(), 0);
                }
            }
            catch { }
        }
    }
}
