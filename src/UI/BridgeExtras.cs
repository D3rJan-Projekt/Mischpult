using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.17: Oszilloskop fuer die Meterbridge – die Wellenform live (Mitte aus links und rechts), an einem Nulldurchgang
    /// festgehalten, damit sie ruhig steht. Daten wie das Stereo-Bild (gleicher Ausgang).
    /// </summary>
    internal sealed partial class OscView
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _plot;
        private PointF[] _pts = new PointF[0];
        private float _gain = 3f;
        private bool _any;
        public int Version { get; private set; }

        public void Layout(RectangleF r)
        {
            Bounds = r;
            _plot = new RectangleF((float)Math.Round(r.X + Theme.F(8)), (float)Math.Round(r.Y + Theme.F(20)), (float)Math.Round(r.Width - Theme.F(16)), (float)Math.Round(Math.Max(8f, r.Height - Theme.F(28))));
            int w = Math.Max(8, (int)_plot.Width);
            if (_pts.Length != w) _pts = new PointF[w];
            _any = false;
        }

        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(_plot), 2, 2);

        /// <summary>Neue Werte (L, R abwechselnd). true = neu zeichnen.</summary>
        public bool Update(float[] lr, float dt)
        {
            if (lr == null || lr.Length < 64 || _pts.Length == 0)
            {
                if (!_any) return false;
                _any = false; ClearTraces(); Version++; return true;
            }
            int frames = lr.Length / 2, win = frames / 2;
            // Nulldurchgang (steigend) in der ersten Haelfte suchen
            int start = 0;
            for (int i = 1; i < frames - win; i++)
            {
                float a = lr[(i - 1) * 2] + lr[(i - 1) * 2 + 1], b = lr[i * 2] + lr[i * 2 + 1];
                if (a < 0f && b >= 0f) { start = i; break; }
            }
            float peak = 0f;
            for (int i = 0; i < frames; i++) { float m = Math.Abs(0.5f * (lr[i * 2] + lr[i * 2 + 1])); if (m > peak) peak = m; }
            // Verstaerkung folgt dem Pegel (leise Stellen bleiben sichtbar, laute laufen nicht raus)
            float want = peak > 1e-4f ? Math.Min(12f, 0.85f / peak) : 3f;
            _gain += (want - _gain) * Math.Min(1f, (want < _gain ? 10f : 2f) * Math.Max(0.005f, dt));
            int w = _pts.Length;
            float mid = _plot.Y + _plot.Height / 2f, half = _plot.Height / 2f;
            for (int x = 0; x < w; x++)
            {
                int i = start + (int)((long)x * win / w);
                float m = 0.5f * (lr[i * 2] + lr[i * 2 + 1]) * _gain;
                m = Math.Max(-1f, Math.Min(1f, m));
                _pts[x] = new PointF(_plot.X + x, mid - m * half);
            }
            _any = peak > 1e-4f;
            if (ScopeLook) PushTrace();   // 3.3: Nachleuchten
            Version++;
            return true;
        }

        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 30 || r.Height < 30) return;
            if (ScopeLook) { DrawScopeStatic(g); return; }
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
                Theme.Str(g, "OSZILLOSKOP", Theme.Fit(g, "OSZILLOSKOP", r.Width - Theme.F(16), 9f, 7f), b, new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(16), Theme.F(14)), Theme.Left);
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Color.FromArgb(0x1c, 0x1f, 0x25), 1f))
            {
                for (int i = 1; i < 8; i++) { float x = (float)Math.Round(_plot.X + _plot.Width * i / 8f) + 0.5f; g.DrawLine(pen, x, _plot.Y, x, _plot.Bottom); }
                for (int i = 1; i < 4; i++) { float y = (float)Math.Round(_plot.Y + _plot.Height * i / 4f) + 0.5f; g.DrawLine(pen, _plot.X, y, _plot.Right, y); }
            }
            using (var pen = new Pen(Color.FromArgb(0x2c, 0x31, 0x39), 1f))
            {
                float y = (float)Math.Round(_plot.Y + _plot.Height / 2f) + 0.5f;
                g.DrawLine(pen, _plot.X, y, _plot.Right, y);
            }
            g.SmoothingMode = sm;
        }

        public static readonly Color Phosphor = Color.FromArgb(0x5c, 0xf2, 0x9a);

        public void DrawLive(Graphics g) { DrawLiveCore(g); if (Theme.CrtOn) Theme.CrtOverlay(g, _plot); }   // 3.4.6: Roehrenbild

        private void DrawLiveCore(Graphics g)
        {
            if (ScopeLook) { DrawScopeLive(g); return; }
            if (!_any || _pts.Length < 2) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new Pen(Color.FromArgb(55, Phosphor), Math.Max(2f, Theme.F(3.5f))) { LineJoin = LineJoin.Round })
                g.DrawLines(glow, _pts);
            using (var pen = new Pen(Phosphor, Math.Max(1f, Theme.F(1.2f))) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, _pts);
            g.SmoothingMode = sm;
        }
    }

    /// <summary>2.17: "Jetzt laeuft" mit Album-Cover fuer die Meterbridge – links das Cover, rechts Interpret und Titel.</summary>
    internal sealed partial class CoverView
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _art, _text;
        private Bitmap _cover;
        private string _title, _artist, _app;
        private bool _playing;
        public int Version { get; private set; }

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float side = Math.Min(r.Height - Theme.F(12), r.Width * 0.55f);
            _art = new RectangleF((float)Math.Round(r.X + Theme.F(6)), (float)Math.Round(r.Y + (r.Height - side) / 2f), (float)Math.Round(side), (float)Math.Round(side));
            _text = new RectangleF(_art.Right + Theme.F(8), r.Y + Theme.F(6), r.Right - _art.Right - Theme.F(14), r.Height - Theme.F(12));
        }

        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(Bounds), -1, -1);

        /// <summary>Was gerade laeuft (null = nichts). true = neu zeichnen.</summary>
        public bool Set(NowPlayingInfo np)
        {
            var c = np?.Cover; string t = np?.Title, a = np?.Artist, app = np?.App; bool p = np != null && np.Playing;
            if (ReferenceEquals(c, _cover) && t == _title && a == _artist && app == _app && p == _playing) return false;
            _cover = c; _title = t; _artist = a; _app = app; _playing = p;
            Version++;
            return true;
        }

        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 30 || r.Height < 30) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            g.SmoothingMode = sm;
        }

        public void DrawLive(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 30 || r.Height < 30) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(0x0b, 0x0c, 0x0f))) Theme.FillRound(g, b, RectangleF.Inflate(r, -Theme.F(1), -Theme.F(1)), Theme.F(4));
            if (ArtStyle == 1) DrawRecord(g);
            else if (ArtStyle == 2) DrawTape(g);
            else if (ArtStyle == 3) DrawCassette(g);   // 3.4
            else if (_cover != null) ChannelImages.DrawCover(g, _art, _cover, Theme.F(4), _playing ? 0 : 110);
            else
            {
                Theme.FillRound(g, Color.FromArgb(0x16, 0x18, 0x1c), _art, Theme.F(4));
                using (var b = new SolidBrush(Color.FromArgb(0x4a, 0x50, 0x5a)))
                    Theme.Str(g, "♪", Theme.Ui(Math.Max(10f, _art.Height / Theme.S * 0.45f)), b, _art, Theme.Center);
            }
            bool none = string.IsNullOrEmpty(_title);
            using (var cap = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                Theme.Str(g, (none ? "JETZT LÄUFT" : (_playing ? "▶  " : "❚❚  ") + (_app ?? "JETZT LÄUFT").ToUpperInvariant()), Theme.Ui(7.5f, FontStyle.Bold), cap,
                    new RectangleF(_text.X, _text.Y, _text.Width, Theme.F(12)), Theme.Left);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Near })
            {
                var tr = new RectangleF(_text.X, _text.Y + Theme.F(15), _text.Width, _text.Height - Theme.F(15));
                if (none)
                    using (var b = new SolidBrush(Color.FromArgb(0x5a, 0x62, 0x6e))) Theme.Str(g, "Nichts läuft", Theme.Ui(10f), b, tr, sf);
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(0xf2, 0xf4, 0xf6)))
                        Theme.Str(g, _title, Theme.Ui(10.5f, FontStyle.Bold), b, new RectangleF(tr.X, tr.Y, tr.Width, Math.Min(tr.Height * 0.62f, Theme.F(34))), sf);
                    if (!string.IsNullOrEmpty(_artist))
                        using (var b = new SolidBrush(Color.FromArgb(0xa8, 0xb0, 0xbb)))
                            Theme.Str(g, _artist, Theme.Ui(9f), b, new RectangleF(tr.X, tr.Y + Math.Min(tr.Height * 0.62f, Theme.F(34)), tr.Width, Theme.F(26)), sf);
                }
            }
            g.SmoothingMode = sm;
        }
    }
}
