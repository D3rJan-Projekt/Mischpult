using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Mischpult.UI
{
    /// <summary>2.18: Namensschilder als Klebeband mit Handschrift – wie im Studio, wo die Kanaele mit Kreppband beschriftet werden.</summary>
    internal static partial class Theme
    {
        /// <summary>Namensschilder als Klebeband (geht vor "Digital").</summary>
        public static bool TapePlates;
        public static readonly Color TapeColor = Color.FromArgb(0xec, 0xe2, 0xc2), TapeInk = Color.FromArgb(0x1f, 0x26, 0x3a);

        private static string _handFamily;
        private static bool _handChecked;

        /// <summary>Handschrift-Schrift, die es auf diesem PC gibt (Ink Free, Segoe Print …) – oder null.</summary>
        public static string HandFamily
        {
            get
            {
                if (_handChecked) return _handFamily;
                _handChecked = true;
                try
                {
                    var names = FontFamily.Families.Select(f => f.Name).ToList();
                    _handFamily = new[] { "Ink Free", "Segoe Print", "Segoe Script", "Comic Sans MS", "Bradley Hand", "Chalkboard" }
                        .FirstOrDefault(n => names.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)));
                }
                catch { _handFamily = null; }
                return _handFamily;
            }
        }

        /// <summary>Schrift fuers Namensschild: bei Klebeband Handschrift (passend verkleinert), sonst wie bisher.</summary>
        public static Font FitPlate(Graphics g, string text, float width, float maxPx, float minPx)
        {
            if (!TapePlates || HandFamily == null) return Fit(g, text, width, maxPx, minPx);
            for (float px = maxPx + 1.5f; px > minPx; px -= 0.5f)
            {
                var f = Get(HandFamily, px, FontStyle.Bold, "Segoe UI", FontStyle.Bold);
                if (TextWidth(g, text, f) <= width) return f;
            }
            return Get(HandFamily, minPx, FontStyle.Bold, "Segoe UI", FontStyle.Bold);
        }

        /// <summary>Kreppband: leicht schraeg, eingerissene Enden, ein Streifen Kanalfarbe rechts, leichter Schatten.</summary>
        private static void DrawTapePlate(Graphics g, RectangleF r, Color bg)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-1.2f);
            g.TranslateTransform(-cx, -cy);
            var t = RectangleF.Inflate(r, -F(1), -F(1.5f));
            // eingerissene Enden (Zickzack), sonst gerade Kanten
            int teeth = 4;
            float tw = F(2.2f), th = t.Height / teeth;
            var pts = new System.Collections.Generic.List<PointF> { new PointF(t.X + tw, t.Y), new PointF(t.Right - tw, t.Y) };
            for (int i = 0; i < teeth; i++) { pts.Add(new PointF(t.Right - (i % 2 == 0 ? 0 : tw), t.Y + th * (i + 0.5f))); pts.Add(new PointF(t.Right - tw, t.Y + th * (i + 1))); }
            pts.Add(new PointF(t.X + tw, t.Bottom));
            for (int i = teeth - 1; i >= 0; i--) { pts.Add(new PointF(t.X + (i % 2 == 0 ? 0 : tw), t.Y + th * (i + 0.5f))); pts.Add(new PointF(t.X + tw, t.Y + th * i)); }
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts.ToArray());
                using (var sh = new GraphicsPath()) { sh.AddPolygon(pts.Select(p => new PointF(p.X + F(0.8f), p.Y + F(1.2f))).ToArray()); using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPath(b, sh); }
                using (var b = new LinearGradientBrush(new RectangleF(t.X, t.Y - 1, t.Width, t.Height + 2), Lighten(TapeColor, 0.06f), Darken(TapeColor, 0.06f), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                // Krepp-Struktur: feine Querriefen
                using (var pen = new Pen(Color.FromArgb(22, 0x5a, 0x4a, 0x2a), 1f))
                {
                    var clip = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    for (float x = t.X + F(2); x < t.Right; x += F(2.5f)) g.DrawLine(pen, x, t.Y, x + F(1.2f), t.Bottom);
                    // Kanalfarbe als kleiner Streifen rechts (links steht die Nummer)
                    using (var b = new SolidBrush(Color.FromArgb(200, bg))) g.FillRectangle(b, t.Right - F(4.5f), t.Y, F(4.5f), t.Height);
                    g.Clip = clip;
                }
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
