using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4.6 Song-Einblendung: kommt ein neuer Titel, erscheint kurz eine kleine Karte in einer Bildschirmecke –
    /// Cover (falls da), Titel und Interpret. Klicks gehen hindurch, sie nimmt nie den Fokus.
    /// </summary>
    internal sealed class SongToast : Form
    {
        private string _title = "", _artist = "", _app = "";
        private Bitmap _cover;
        private readonly Timer _timer = new Timer { Interval = 30 };
        private readonly System.Diagnostics.Stopwatch _shown = new System.Diagnostics.Stopwatch();
        public const int ShowMs = 4500;

        public SongToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            Size = new Size(Theme.Px(330), Theme.Px(78));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer.Tick += (s, e) =>
            {
                long ms = _shown.ElapsedMilliseconds;
                if (ms < 250) { Opacity = 0.96 * ms / 250.0; return; }   // weich einblenden
                if (ms < ShowMs) { if (Opacity < 0.96) Opacity = 0.96; return; }
                double o = Opacity - 0.07;
                if (o <= 0.05) { _timer.Stop(); Hide(); }
                else Opacity = o;
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT */ | 0x8 /* TOPMOST */;
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.F(12)))
                Region = new Region(p);
        }

        public string ShownText => string.IsNullOrEmpty(_artist) ? _title : _artist + " – " + _title;

        /// <summary>Karte zeigen. corner: 0 unten rechts, 1 oben rechts, 2 unten links, 3 oben links.</summary>
        public void ShowSong(NowPlayingInfo np, int corner)
        {
            if (np == null) return;
            _title = np.Title ?? ""; _artist = np.Artist ?? ""; _app = np.App ?? "";
            var old = _cover;
            _cover = np.Cover != null ? CopyCover(np.Cover) : null;
            old?.Dispose();
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int m = Theme.Px(18);
            int x = corner >= 2 ? wa.X + m : wa.Right - Width - m;
            int y = corner == 1 || corner == 3 ? wa.Y + m : wa.Bottom - Height - m;
            Location = new Point(x, y);
            Opacity = 0.0;
            if (!Visible) Show();
            Invalidate();
            _shown.Restart();
            _timer.Start();
        }

        private static Bitmap CopyCover(Bitmap src)
        {
            try { lock (src) return new Bitmap(src); }   // eigenes Bild: das Original kann sich aendern
            catch { return null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.StrokeRound(g, Theme.MenuBorder, Theme.F(1), r, Theme.F(12));
            float pad = Theme.F(10), cs = Height - 2 * pad;
            var coverRect = new RectangleF(pad, pad, cs, cs);
            if (_cover != null)
            {
                var im = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var path = Theme.Round(coverRect, Theme.F(6)))
                {
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    g.DrawImage(_cover, coverRect);
                    g.Restore(st);
                }
                g.InterpolationMode = im;
            }
            else
            {
                Theme.FillRound(g, Theme.Track, coverRect, Theme.F(6));
                // Note
                using (var b = new SolidBrush(Theme.Accent))
                    Theme.Str(g, "♪", Theme.Ui(26f, FontStyle.Bold), b, coverRect, Theme.Center);
            }
            float tx = coverRect.Right + Theme.F(12), tw = Width - tx - Theme.F(12);
            Theme.Label(g, ("Jetzt läuft" + (string.IsNullOrEmpty(_app) ? "" : " · " + _app)).ToUpperInvariant(),
                new RectangleF(tx, Theme.F(9), tw, Theme.F(16)), Theme.Left, Theme.TextDim, 9f);
            using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                using (var b = new SolidBrush(Theme.Text))
                    Theme.Str(g, _title, Theme.Ui(15f, FontStyle.Bold), b, new RectangleF(tx, Theme.F(25), tw, Theme.F(24)), sf);
                using (var b = new SolidBrush(Theme.TextDim))
                    Theme.Str(g, _artist, Theme.Ui(12f), b, new RectangleF(tx, Theme.F(49), tw, Theme.F(20)), sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _cover?.Dispose(); _cover = null; }
            base.Dispose(disposing);
        }
    }
}
