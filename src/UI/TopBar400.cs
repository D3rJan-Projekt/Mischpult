using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Mischpult.UI
{
    /// <summary>4.0: eine zusaetzliche Taste in der Kopfleiste (eigene Taste, Panik, Spulen, OBS, Fokus-Timer …).</summary>
    internal sealed class TopKey
    {
        public string Id;          // z. B. "panic", "seek-", "user:2", "obs", "focus"
        public string Text;        // Beschriftung (kurz)
        public int Icon;           // 0 Text, 1 zurueckspulen, 2 vorspulen, 3 Zufall, 4 Wiederholen, 5 Panik
        public bool On;            // leuchtet
        public Color Lit;          // Farbe, wenn an
        public string Tip;
        public float Width = 28;   // logische Pixel
        public int Priority;       // hoeher = bleibt laenger, wenn es eng wird
        public string Sig => Id + "|" + Text + "|" + Icon + "|" + On + "|" + Lit.ToArgb() + "|" + Width;
    }

    internal sealed partial class TopBar
    {
        private List<TopKey> _keys400 = new List<TopKey>();
        private readonly List<RectangleF> _keyRects400 = new List<RectangleF>();
        private List<TopKey> _shownKeys400 = new List<TopKey>();
        private string _keysSig400 = "";

        /// <summary>4.0: Klick auf eine Zusatz-Taste (Id, Rechtsklick?, Bildschirm-Punkt unter der Taste).</summary>
        public event Action<string, bool, Point> TopKeyClicked;

        public void SetTopKeys(List<TopKey> keys)
        {
            keys = keys ?? new List<TopKey>();
            string sig = string.Join(";", keys.Select(k => k.Sig));
            if (sig == _keysSig400) { _keys400 = keys; return; }
            _keysSig400 = sig;
            _keys400 = keys;
            Invalidate();
        }

        public IReadOnlyList<TopKey> TopKeys => _keys400;

        /// <summary>Platz, den die Zusatz-Tasten brauchen (fuer die Platz-Rechnung in PaintBar).</summary>
        private float TopKeysWidth(float gap, float mg, float room)
        {
            // nach Prioritaet so viele, wie passen (hoechstens die Haelfte des freien Platzes)
            var list = _keys400.OrderByDescending(k => k.Priority).ToList();
            float w = 0; _shownKeys400 = new List<TopKey>();
            foreach (var k in list)
            {
                float kw = Theme.F(k.Width) + (_shownKeys400.Count == 0 ? gap : mg);
                if (w + kw > room * 0.5f) break;
                w += kw; _shownKeys400.Add(k);
            }
            _shownKeys400 = _keys400.Where(k => _shownKeys400.Contains(k)).ToList();   // urspruengliche Reihenfolge
            return w;
        }

        /// <summary>Zusatz-Tasten links von x anordnen und zeichnen; liefert die neue linke Kante.</summary>
        private float LayoutTopKeys(Graphics g, float x, float by, float bh, float gap, float mg)
        {
            _keyRects400.Clear();
            if (_shownKeys400.Count == 0) return x;
            x -= gap;
            var rects = new RectangleF[_shownKeys400.Count];
            for (int i = _shownKeys400.Count - 1; i >= 0; i--)
            {
                float w = Theme.F(_shownKeys400[i].Width);
                x -= w;
                rects[i] = new RectangleF(x, by, w, bh);
                if (i > 0) x -= mg;
            }
            _keyRects400.AddRange(rects);
            for (int i = 0; i < _shownKeys400.Count; i++) DrawTopKey(g, rects[i], _shownKeys400[i], _hover == "tk" + i);
            return x;
        }

        private void DrawTopKey(Graphics g, RectangleF r, TopKey k, bool hover)
        {
            Theme.DrawBarButton(g, r, hover);
            Color c = k.On ? k.Lit : hover ? Theme.TextHover : Theme.Text;
            if (k.On && k.Icon != 5)
                using (var b = new SolidBrush(k.Lit)) g.FillRectangle(b, r.X + Theme.F(7), r.Bottom - Theme.F(4.5f), r.Width - Theme.F(14), Theme.F(2));
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Theme.F(4.2f);
            using (var pen = new Pen(c, Math.Max(1.2f, Theme.F(1.5f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var b = new SolidBrush(c))
            {
                switch (k.Icon)
                {
                    case 1:
                    case 2:
                        {
                            // Kreispfeil mit "10"
                            float d = k.Icon == 2 ? 1f : -1f;
                            var rr = new RectangleF(cx - s * 1.7f, cy - s * 1.7f, s * 3.4f, s * 3.4f);
                            g.DrawArc(pen, rr, d > 0 ? -60 : 240, d > 0 ? 300 : -300);
                            float ax = d > 0 ? cx + s * 0.9f : cx - s * 0.9f, ay = cy - s * 1.5f;
                            g.FillPolygon(b, new[] { new PointF(ax + d * s * 0.9f, ay), new PointF(ax - d * s * 0.2f, ay - s * 0.75f), new PointF(ax - d * s * 0.2f, ay + s * 0.75f) });
                            Theme.Label(g, "10", new RectangleF(r.X, cy - Theme.F(6), r.Width, Theme.F(13)), Theme.Center, c, 7f);
                            break;
                        }
                    case 3:
                        {
                            // Zufall: zwei gekreuzte Pfeile
                            g.DrawBezier(pen, cx - s * 1.5f, cy - s, cx - s * 0.2f, cy - s, cx + s * 0.2f, cy + s, cx + s * 1.3f, cy + s);
                            g.DrawBezier(pen, cx - s * 1.5f, cy + s, cx - s * 0.2f, cy + s, cx + s * 0.2f, cy - s, cx + s * 1.3f, cy - s);
                            g.FillPolygon(b, new[] { new PointF(cx + s * 1.9f, cy - s), new PointF(cx + s * 1.1f, cy - s * 1.6f), new PointF(cx + s * 1.1f, cy - s * 0.4f) });
                            g.FillPolygon(b, new[] { new PointF(cx + s * 1.9f, cy + s), new PointF(cx + s * 1.1f, cy + s * 0.4f), new PointF(cx + s * 1.1f, cy + s * 1.6f) });
                            break;
                        }
                    case 4:
                        {
                            // Wiederholen: Rechteck-Schleife
                            var rr = new RectangleF(cx - s * 1.5f, cy - s * 0.9f, s * 3f, s * 1.8f);
                            g.DrawArc(pen, rr.X, rr.Y, s * 1.2f, rr.Height, 90, 180);
                            g.DrawArc(pen, rr.Right - s * 1.2f, rr.Y, s * 1.2f, rr.Height, 270, 180);
                            g.DrawLine(pen, rr.X + s * 0.6f, rr.Y, rr.Right - s * 0.9f, rr.Y);
                            g.DrawLine(pen, rr.X + s * 0.9f, rr.Bottom, rr.Right - s * 0.6f, rr.Bottom);
                            g.FillPolygon(b, new[] { new PointF(rr.Right - s * 0.3f, rr.Y), new PointF(rr.Right - s * 1.0f, rr.Y - s * 0.6f), new PointF(rr.Right - s * 1.0f, rr.Y + s * 0.6f) });
                            if (k.Text == "1") Theme.Label(g, "1", new RectangleF(r.X, cy - Theme.F(6), r.Width, Theme.F(13)), Theme.Center, c, 7f);
                            break;
                        }
                    case 5:
                        {
                            // Panik: rote Taste mit Ausrufezeichen
                            var rr = RectangleF.Inflate(r, -Theme.F(4), -Theme.F(4));
                            Color red = k.On ? Color.FromArgb(0xff, 0x3a, 0x2a) : Color.FromArgb(0xb0, 0x28, 0x1e);
                            using (var rb = new SolidBrush(red)) g.FillEllipse(rb, cx - rr.Height / 2f, rr.Y, rr.Height, rr.Height);
                            if (k.On) using (var glow = new SolidBrush(Color.FromArgb(90, 0xff, 0x40, 0x30))) g.FillEllipse(glow, cx - rr.Height / 2f - Theme.F(3), rr.Y - Theme.F(3), rr.Height + Theme.F(6), rr.Height + Theme.F(6));
                            Theme.Label(g, "!", new RectangleF(r.X, r.Y, r.Width, r.Height), Theme.Center, Color.White, 11f);
                            break;
                        }
                    default:
                        Theme.Label(g, k.Text ?? "", r, Theme.Center, c, k.Text != null && k.Text.Length > 4 ? 8.5f : 10f);
                        break;
                }
            }
            g.SmoothingMode = sm;
        }

        private int TopKeyAt(Point p)
        {
            for (int i = 0; i < _keyRects400.Count; i++) if (_keyRects400[i].Contains(p)) return i;
            return -1;
        }

        private string TopKeyTip(string h)
        {
            if (h == null || !h.StartsWith("tk")) return null;
            int i = int.Parse(h.Substring(2));
            return i >= 0 && i < _shownKeys400.Count ? _shownKeys400[i].Tip : null;
        }

        private void TopKeyDown(string h, bool right)
        {
            int i = int.Parse(h.Substring(2));
            if (i < 0 || i >= _shownKeys400.Count) return;
            var r = _keyRects400[i];
            TopKeyClicked?.Invoke(_shownKeys400[i].Id, right, PointToScreen(new Point((int)r.X, (int)r.Bottom + 2)));
        }
    }
}
