using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0: Lautheits-Radar in der Meterbridge – ein Zeiger kreist einmal pro Minute und malt die Kurzzeit-Lautheit
    /// als Tortenstuecke: innen leise, aussen laut, gruen ums Ziel, gelb/rot darueber. In der Mitte die Momentan-Lautheit.
    /// </summary>
    internal sealed class RadarView
    {
        public RectangleF Bounds { get; private set; }
        public int Version { get; private set; }
        /// <summary>Lautheit von aussen (LUFS, NaN = still / unbekannt).</summary>
        public static float Momentary = float.NaN, ShortTerm = float.NaN;
        /// <summary>Ziel (LUFS) – vom Lautheits-Ziel, sonst -18.</summary>
        public static float Target = -18f;
        /// <summary>Sekunden pro Umdrehung.</summary>
        public const float Turn = 60f;
        public const int Slots = 120;

        private readonly float[] _hist = new float[Slots];
        private int _slot = -1;
        private double _t;
        private int _q = int.MinValue, _angQ = -1;
        private PointF _c;
        private float _r;

        public RadarView() { for (int i = 0; i < Slots; i++) _hist[i] = float.NaN; }

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float size = Math.Min(r.Width - Theme.F(6), r.Height - Theme.F(6));
            _r = Math.Max(10f, size / 2f);
            _c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        }

        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(new RectangleF(_c.X - _r, _c.Y - _r, _r * 2, _r * 2)), 2, 2);

        /// <summary>Abstand vom Ziel (LU) auf den Radius: -24 LU innen, +9 LU aussen.</summary>
        public static float Frac(float lufs, float target)
        {
            if (float.IsNaN(lufs)) return 0f;
            float lu = lufs - target;
            return Math.Max(0f, Math.Min(1f, (lu + 24f) / 33f));
        }

        public static Color ColorOf(float lufs, float target)
        {
            float lu = lufs - target;
            if (lu > 3f) return Color.FromArgb(0xff, 0x4a, 0x3a);
            if (lu > 1f) return Color.FromArgb(0xff, 0xc2, 0x3a);
            if (lu >= -3f) return Color.FromArgb(0x4c, 0xd9, 0x6a);
            return Color.FromArgb(0x3a, 0x9a, 0xff);
        }

        /// <summary>Aus dem Takt: Zeiger weiter, aktuelles Stueck fuellen. true = neu zeichnen.</summary>
        public bool Animate(float dt)
        {
            _t += Math.Min(0.25f, Math.Max(0f, dt));
            int slot = (int)(_t / Turn * Slots) % Slots;
            float st = ShortTerm;
            bool changed = false;
            if (slot != _slot)
            {
                _slot = slot;
                _hist[slot] = st;
                changed = true;
            }
            else if (!float.IsNaN(st) && (float.IsNaN(_hist[slot]) || st > _hist[slot])) { _hist[slot] = st; }
            int q = float.IsNaN(Momentary) ? int.MinValue : (int)Math.Round(Momentary * 10);
            if (q != _q) { _q = q; changed = true; }
            int aq = (int)(_t / Turn * 360 * 2) % 720;   // Zeiger alle halben Grad
            if (aq != _angQ) { _angQ = aq; changed = true; }
            if (changed) Version++;
            return changed;
        }

        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 40 || r.Height < 40) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(_c.X - _r, _c.Y - _r, _r * 2, _r * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x07, 0x09, 0x0b))) g.FillEllipse(b, box);
            using (var pen = new Pen(Color.FromArgb(0x30, 0x34, 0x3a), Math.Max(1f, Theme.F(1)))) g.DrawEllipse(pen, box);
            g.SmoothingMode = sm;
        }

        public void DrawLive(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 40 || r.Height < 40) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(_c.X - _r, _c.Y - _r, _r * 2, _r * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x07, 0x09, 0x0b))) g.FillEllipse(b, box);
            float tg = Target;
            float inner = _r * 0.30f;
            // Stuecke (der Zeiger loescht vor sich: die naechsten 6 Stuecke bleiben dunkel)
            for (int i = 0; i < Slots; i++)
            {
                int age = (_slot - i + Slots) % Slots;
                if (age > Slots - 6) continue;
                float v = _hist[i];
                if (float.IsNaN(v)) continue;
                float f = Frac(v, tg);
                if (f <= 0f) continue;
                float rr = inner + (_r - inner) * f;
                var col = ColorOf(v, tg);
                int alpha = 120 + (int)(110 * (1f - age / (float)Slots));
                using (var b = new SolidBrush(Color.FromArgb(alpha, col)))
                    g.FillPie(b, _c.X - rr, _c.Y - rr, rr * 2, rr * 2, -90f + i * 360f / Slots, 360f / Slots + 0.4f);
            }
            // Ringe: Ziel (gruen), -10 LU, +6 LU
            DrawRing(g, inner + (_r - inner) * Frac(tg, tg), Color.FromArgb(120, 0x4c, 0xd9, 0x6a), 1.2f);
            DrawRing(g, inner + (_r - inner) * Frac(tg - 10f, tg), Color.FromArgb(50, 255, 255, 255), 0.8f);
            DrawRing(g, inner + (_r - inner) * Frac(tg + 6f, tg), Color.FromArgb(70, 0xff, 0x6a, 0x5a), 0.8f);
            // Zeiger
            double ang = (_t / Turn * 360.0 - 90.0) * Math.PI / 180.0;
            using (var pen = new Pen(Color.FromArgb(150, 0xd8, 0xf0, 0xff), Math.Max(1f, Theme.F(1.2f))))
                g.DrawLine(pen, _c.X + (float)Math.Cos(ang) * inner, _c.Y + (float)Math.Sin(ang) * inner, _c.X + (float)Math.Cos(ang) * _r, _c.Y + (float)Math.Sin(ang) * _r);
            // Mitte: Momentan-Lautheit
            var hub = new RectangleF(_c.X - inner, _c.Y - inner, inner * 2, inner * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x10, 0x13, 0x17))) g.FillEllipse(b, hub);
            float m = Momentary;
            string t = float.IsNaN(m) || m < -70f ? "–" : (m - tg >= 0 ? "+" : "−") + Math.Abs(m - tg).ToString("0");
            Color tc = float.IsNaN(m) || m < -70f ? Theme.TextFaint : ColorOf(m, tg);
            var font = Theme.Ui(Math.Max(7f, inner / Theme.S * 0.62f), FontStyle.Bold);
            using (var b = new SolidBrush(tc)) Theme.Str(g, t, font, b, new RectangleF(hub.X - Theme.F(4), hub.Y - Theme.F(1), hub.Width + Theme.F(8), hub.Height * 0.75f), Theme.Center);
            var small = Theme.Ui(Math.Max(6f, inner / Theme.S * 0.28f), FontStyle.Bold);
            using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, "LU", small, b, new RectangleF(hub.X, hub.Y + hub.Height * 0.55f, hub.Width, hub.Height * 0.4f), Theme.Center);
            using (var pen = new Pen(Color.FromArgb(0x30, 0x34, 0x3a), Math.Max(1f, Theme.F(1)))) g.DrawEllipse(pen, box);
            g.SmoothingMode = sm;
        }

        private void DrawRing(Graphics g, float rr, Color c, float w)
        {
            using (var pen = new Pen(c, Math.Max(1f, Theme.F(w)))) g.DrawEllipse(pen, _c.X - rr, _c.Y - rr, rr * 2, rr * 2);
        }
    }
}
