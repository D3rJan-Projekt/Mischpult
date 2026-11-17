using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.4: "Jetzt laeuft" als Kassettendeck – Kassette mit Cover-Etikett, Spulen drehen im Sichtfenster.</summary>
    internal sealed partial class CoverView
    {
        private string _trackKey;
        private double _trackStart;

        /// <summary>Wie weit der Titel ungefaehr ist (0..1, nach Laufzeit; ohne Laenge: in 4 Minuten einmal durch).</summary>
        private float TrackProgress()
        {
            string key = (_title ?? "") + "|" + (_artist ?? "");
            double t = _anim.Elapsed.TotalSeconds;
            if (key != _trackKey) { _trackKey = key; _trackStart = t; }
            return (float)Math.Min(1.0, (t - _trackStart) / 240.0);
        }

        private void DrawCassette(Graphics g)
        {
            var a = _art;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Deck-Schacht
            Theme.FillRound(g, Color.FromArgb(0x16, 0x17, 0x1a), a, Theme.F(4));
            float cw = a.Width - Theme.F(8), ch = Math.Min(a.Height - Theme.F(10), cw * 0.64f);
            var cas = new RectangleF(a.X + Theme.F(4), a.Y + (a.Height - ch) / 2f, cw, ch);
            // Gehaeuse
            using (var b = new LinearGradientBrush(new RectangleF(cas.X, cas.Y - 1, cas.Width, cas.Height + 2), Color.FromArgb(0x3a, 0x3c, 0x42), Color.FromArgb(0x1a, 0x1b, 0x1e), LinearGradientMode.Vertical))
                Theme.FillRound(g, b, cas, Theme.F(3));
            Theme.StrokeRound(g, Color.FromArgb(0x08, 0x08, 0x09), Theme.F(1), cas, Theme.F(3));
            // Etikett = Cover (oben, ueber die ganze Breite)
            var label = new RectangleF(cas.X + cas.Width * 0.07f, cas.Y + cas.Height * 0.08f, cas.Width * 0.86f, cas.Height * 0.6f);
            if (_cover != null) ChannelImages.DrawCover(g, label, _cover, Theme.F(2), 40);
            else
                using (var b = new LinearGradientBrush(label, Color.FromArgb(0xf2, 0xe8, 0xd0), Color.FromArgb(0xd8, 0xc8, 0xa4), LinearGradientMode.Vertical))
                    Theme.FillRound(g, b, label, Theme.F(2));
            // Sichtfenster mit den beiden Spulen
            var win = new RectangleF(label.X + label.Width * 0.2f, label.Y + label.Height * 0.32f, label.Width * 0.6f, label.Height * 0.46f);
            Theme.FillRound(g, Color.FromArgb(220, 0x10, 0x10, 0x12), win, win.Height / 2f);
            float p = TrackProgress();
            float hubR = win.Height * 0.26f, maxPack = win.Height * 0.48f;
            float packL = hubR + (maxPack - hubR) * (1f - p), packR = hubR + (maxPack - hubR) * p;
            var lc = new PointF(win.X + win.Height * 0.55f, win.Y + win.Height / 2f);
            var rc = new PointF(win.Right - win.Height * 0.55f, win.Y + win.Height / 2f);
            var clip = g.Save();
            using (var path = Theme.Round(win, win.Height / 2f)) g.SetClip(path, CombineMode.Intersect);
            using (var b = new SolidBrush(Color.FromArgb(0x4a, 0x30, 0x1e)))
            {
                g.FillEllipse(b, lc.X - packL, lc.Y - packL, packL * 2, packL * 2);
                g.FillEllipse(b, rc.X - packR, rc.Y - packR, packR * 2, packR * 2);
            }
            g.Restore(clip);
            DrawHub(g, lc, hubR, _spin);
            DrawHub(g, rc, hubR, _spin * 1.2f);
            Theme.StrokeRound(g, Color.FromArgb(120, 255, 255, 255), 1f, win, win.Height / 2f);
            // Unterteil mit Schraubenloechern und Kopf-Aussparung
            var foot = new RectangleF(cas.X + cas.Width * 0.22f, cas.Bottom - cas.Height * 0.22f, cas.Width * 0.56f, cas.Height * 0.18f);
            using (var b = new SolidBrush(Color.FromArgb(0x24, 0x25, 0x28))) Theme.FillRound(g, b, foot, Theme.F(2));
            using (var b = new SolidBrush(Color.FromArgb(0x0a, 0x0a, 0x0b)))
                foreach (var hx in new[] { foot.X + foot.Width * 0.2f, foot.X + foot.Width * 0.5f, foot.X + foot.Width * 0.8f })
                    g.FillEllipse(b, hx - Theme.F(1.5f), foot.Y + foot.Height / 2f - Theme.F(1.5f), Theme.F(3), Theme.F(3));
            using (var b = new SolidBrush(Color.FromArgb(0x60, 0x64, 0x6a)))
                foreach (var pt in new[] { new PointF(cas.X + Theme.F(4), cas.Y + Theme.F(4)), new PointF(cas.Right - Theme.F(4), cas.Y + Theme.F(4)), new PointF(cas.X + Theme.F(4), cas.Bottom - Theme.F(4)), new PointF(cas.Right - Theme.F(4), cas.Bottom - Theme.F(4)) })
                    g.FillEllipse(b, pt.X - Theme.F(1.3f), pt.Y - Theme.F(1.3f), Theme.F(2.6f), Theme.F(2.6f));
            if (!_playing)
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) Theme.FillRound(g, b, a, Theme.F(4));
            g.Restore(st);
        }

        private static void DrawHub(Graphics g, PointF c, float r, float angle)
        {
            var st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            using (var b = new SolidBrush(Color.FromArgb(0xf2, 0xf2, 0xee))) g.FillEllipse(b, -r, -r, r * 2, r * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x10, 0x10, 0x12)))
            {
                g.FillEllipse(b, -r * 0.55f, -r * 0.55f, r * 1.1f, r * 1.1f);
                for (int i = 0; i < 6; i++)
                {
                    var m = g.Save();
                    g.RotateTransform(i * 60f);
                    g.FillRectangle(b, -r * 0.1f, -r * 0.82f, r * 0.2f, r * 0.3f);
                    g.Restore(m);
                }
            }
            g.Restore(st);
        }
    }
}
