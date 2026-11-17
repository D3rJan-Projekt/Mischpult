using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.3: "Jetzt laeuft" als drehende Schallplatte (Cover als Etikett, Tonarm hebt bei Pause ab) oder als Bandmaschine.</summary>
    internal sealed partial class CoverView
    {
        /// <summary>0 Cover, 1 Schallplatte, 2 Bandmaschine, 3 Kassette (3.4).</summary>
        public static int ArtStyle;
        public static readonly string[] ArtStyleNames = { "Cover", "Schallplatte", "Bandmaschine", "Kassette" };

        private readonly Stopwatch _anim = Stopwatch.StartNew();
        private double _lastT, _lastBump;
        private float _spin;          // Drehwinkel in Grad
        private float _arm = 0f;      // Tonarm: 0 abgehoben, 1 auf der Platte

        /// <summary>Aus dem Takt der Meterbridge: dreht weiter, solange etwas laeuft (etwa 30 Bilder pro Sekunde).</summary>
        public void Animate()
        {
            double t = _anim.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, t - _lastT);
            _lastT = t;
            if (ArtStyle == 0) return;
            bool moving = false;
            if (_playing)
            {
                _spin = (_spin + dt * (ArtStyle == 1 ? 200f : 110f)) % 360f;
                moving = true;
            }
            float armTarget = _playing ? 1f : 0f;
            if (Math.Abs(_arm - armTarget) > 0.001f)
            {
                float step = dt * 1.6f;
                _arm = _arm < armTarget ? Math.Min(armTarget, _arm + step) : Math.Max(armTarget, _arm - step);
                moving = true;
            }
            if (moving && t - _lastBump >= 1.0 / 30.0) { _lastBump = t; Version++; }
        }

        public float Spin => _spin;
        public float Arm => _arm;

        private void DrawRecord(Graphics g)
        {
            var a = _art;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Plattenteller
            Theme.FillRound(g, Color.FromArgb(0x1a, 0x1b, 0x1e), a, Theme.F(4));
            float d = a.Height * 0.92f;
            var disc = new RectangleF(a.X + Theme.F(2), a.Y + (a.Height - d) / 2f, d, d);
            var c = new PointF(disc.X + d / 2f, disc.Y + d / 2f);
            using (var b = new SolidBrush(Color.FromArgb(0x0a, 0x0a, 0x0b))) g.FillEllipse(b, disc);
            // Rillen
            using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
                for (float rr = d * 0.48f; rr > d * 0.2f; rr -= Math.Max(1.6f, Theme.F(2.2f)))
                    g.DrawEllipse(pen, c.X - rr, c.Y - rr, rr * 2, rr * 2);
            // Lichtreflex (dreht nicht mit)
            using (var path = new GraphicsPath())
            {
                path.AddPie(disc.X, disc.Y, disc.Width, disc.Height, -60, 30);
                path.AddPie(disc.X, disc.Y, disc.Width, disc.Height, 120, 30);
                using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillPath(b, path);
            }
            // Etikett = Cover, dreht sich
            float lr = d * 0.19f;
            var label = new RectangleF(c.X - lr, c.Y - lr, lr * 2, lr * 2);
            var inner = g.Save();
            using (var clip = new GraphicsPath()) { clip.AddEllipse(label); g.SetClip(clip, CombineMode.Intersect); }
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(_spin);
            if (_cover != null)
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(_cover, new RectangleF(-lr, -lr, lr * 2, lr * 2));
            }
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(0xc8, 0x3a, 0x2a))) g.FillEllipse(b, -lr, -lr, lr * 2, lr * 2);
                using (var b = new SolidBrush(Color.FromArgb(0xf0, 0xe0, 0xc0))) g.FillRectangle(b, -lr * 0.6f, -lr * 0.5f, lr * 1.2f, lr * 0.16f);
            }
            g.Restore(inner);
            using (var b = new SolidBrush(Color.FromArgb(0xd8, 0xdb, 0xe0))) g.FillEllipse(b, c.X - Theme.F(1.6f), c.Y - Theme.F(1.6f), Theme.F(3.2f), Theme.F(3.2f));
            // Tonarm: Drehpunkt rechts oben, liegt auf der Platte, wenn sie laeuft
            var pivot = new PointF(Math.Min(a.Right - Theme.F(5), disc.Right + Theme.F(4)), a.Y + Theme.F(7));
            // auf der Platte liegt die Nadel in den aeusseren Rillen rechts, abgehoben steht der Arm senkrecht neben der Platte
            var playPt = new PointF(c.X + d * 0.27f, c.Y + d * 0.14f);
            float len = (float)Math.Sqrt((playPt.X - pivot.X) * (playPt.X - pivot.X) + (playPt.Y - pivot.Y) * (playPt.Y - pivot.Y));
            double play = Math.Atan2(playPt.Y - pivot.Y, playPt.X - pivot.X), rest = Math.PI / 2 - 0.06;
            double ang = rest + (play - rest) * _arm;
            var tip = new PointF(pivot.X + (float)Math.Cos(ang) * len, pivot.Y + (float)Math.Sin(ang) * len);
            float lift = (1f - _arm) * Theme.F(3);
            using (var pen = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(2f, Theme.F(2.4f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, pivot.X + lift, pivot.Y + lift + Theme.F(1.5f), tip.X + lift, tip.Y + lift + Theme.F(1.5f));   // Schatten wandert beim Anheben weg
            using (var pen = new Pen(Color.FromArgb(0xc6, 0xca, 0xd0), Math.Max(1.6f, Theme.F(2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, pivot, tip);
            using (var b = new SolidBrush(Color.FromArgb(0x30, 0x32, 0x36)))
            {
                var head = new RectangleF(tip.X - Theme.F(2.5f), tip.Y - Theme.F(2.5f), Theme.F(5), Theme.F(5));
                g.FillRectangle(b, head);
            }
            using (var b = new LinearGradientBrush(new RectangleF(pivot.X - Theme.F(5), pivot.Y - Theme.F(5), Theme.F(10), Theme.F(10)), Color.FromArgb(0xe2, 0xe4, 0xe8), Color.FromArgb(0x50, 0x54, 0x5a), LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(b, pivot.X - Theme.F(4.5f), pivot.Y - Theme.F(4.5f), Theme.F(9), Theme.F(9));
            if (!_playing)
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) Theme.FillRound(g, b, a, Theme.F(4));
            g.Restore(st);
        }

        private void DrawTape(Graphics g)
        {
            var a = _art;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new RectangleF(a.X, a.Y - 1, a.Width, a.Height + 2), Color.FromArgb(0x3a, 0x3c, 0x40), Color.FromArgb(0x1e, 0x1f, 0x22), LinearGradientMode.Vertical))
                Theme.FillRound(g, b, a, Theme.F(4));
            float rr = a.Width * 0.235f;
            var l = new PointF(a.X + a.Width * 0.27f, a.Y + a.Height * 0.36f);
            var r = new PointF(a.X + a.Width * 0.73f, a.Y + a.Height * 0.36f);
            // Band zwischen den Spulen ueber die Koepfe
            var headL = new PointF(a.X + a.Width * 0.38f, a.Y + a.Height * 0.8f);
            var headR = new PointF(a.X + a.Width * 0.62f, a.Y + a.Height * 0.8f);
            float packL = rr * 0.86f, packR = rr * 0.6f;
            using (var pen = new Pen(Color.FromArgb(0x5a, 0x3a, 0x22), Math.Max(1f, Theme.F(1.2f))))
            {
                g.DrawLine(pen, l.X - packL, l.Y, headL.X - Theme.F(4), headL.Y);
                g.DrawLine(pen, headL.X - Theme.F(4), headL.Y, headR.X + Theme.F(4), headR.Y);
                g.DrawLine(pen, headR.X + Theme.F(4), headR.Y, r.X + packR, r.Y);
            }
            using (var b = new SolidBrush(Color.FromArgb(0x9a, 0xa0, 0xa8)))
            {
                g.FillRectangle(b, headL.X - Theme.F(3), headL.Y - Theme.F(2), Theme.F(6), Theme.F(5));
                g.FillRectangle(b, headR.X - Theme.F(3), headR.Y - Theme.F(2), Theme.F(6), Theme.F(5));
            }
            DrawReel(g, l, rr, packL, _spin);
            DrawReel(g, r, rr, packR, _spin * 1.4f);
            // REC-Lampe: leuchtet, solange etwas laeuft
            var lamp = new RectangleF(a.X + a.Width / 2f - Theme.F(3), a.Bottom - Theme.F(9), Theme.F(6), Theme.F(6));
            if (_playing)
            {
                using (var b = new SolidBrush(Color.FromArgb(70, 0xff, 0x30, 0x20))) g.FillEllipse(b, RectangleF.Inflate(lamp, Theme.F(2), Theme.F(2)));
                using (var b = new SolidBrush(Color.FromArgb(0xff, 0x40, 0x30))) g.FillEllipse(b, lamp);
            }
            else using (var b = new SolidBrush(Color.FromArgb(0x4a, 0x14, 0x10))) g.FillEllipse(b, lamp);
            g.Restore(st);
        }

        private void DrawReel(Graphics g, PointF c, float rr, float pack, float angle)
        {
            using (var b = new SolidBrush(Color.FromArgb(0x3a, 0x26, 0x18))) g.FillEllipse(b, c.X - pack, c.Y - pack, pack * 2, pack * 2);   // Bandwickel
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                for (float k = pack - Theme.F(2); k > rr * 0.35f; k -= Theme.F(2.5f)) g.DrawEllipse(pen, c.X - k, c.Y - k, k * 2, k * 2);
            // Spule (Metall, drei Aussparungen) dreht sich
            var st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            using (var path = new GraphicsPath { FillMode = FillMode.Alternate })
            {
                path.AddEllipse(-rr, -rr, rr * 2, rr * 2);
                for (int i = 0; i < 3; i++) path.AddPie(-rr * 0.8f, -rr * 0.8f, rr * 1.6f, rr * 1.6f, i * 120 + 20, 70);
                using (var b = new SolidBrush(Color.FromArgb(150, 0xc4, 0xc8, 0xce))) g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(0xe0, 0xe2, 0xe6))) g.FillEllipse(b, -rr * 0.24f, -rr * 0.24f, rr * 0.48f, rr * 0.48f);
            using (var b = new SolidBrush(Color.FromArgb(0x30, 0x32, 0x36)))
                for (int i = 0; i < 3; i++)
                {
                    double ag = i * 2 * Math.PI / 3;
                    g.FillRectangle(b, (float)Math.Cos(ag) * rr * 0.12f - Theme.F(1), (float)Math.Sin(ag) * rr * 0.12f - Theme.F(1), Theme.F(2), Theme.F(2));
                }
            g.Restore(st);
        }
    }
}
