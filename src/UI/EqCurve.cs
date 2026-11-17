using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// Kleine EQ-Kurve im Kanal: berechnet aus denselben Filtern wie der Ton (3-Band-EQ und grosser EQ),
    /// wie stark welche Frequenz angehoben oder abgesenkt wird – Bass links, Hoehen rechts.
    /// </summary>
    internal static class EqCurve
    {
        public const int Points = 56;
        public const float RangeDb = 15f;
        private const double Fs = 48000.0;

        /// <summary>Frequenz von Punkt i (logarithmisch 25 Hz … 18 kHz).</summary>
        public static double FreqAt(int i) => 25.0 * Math.Pow(18000.0 / 25.0, i / (double)(Points - 1));

        /// <summary>Verstaerkung in dB an den Punkten.</summary>
        public static float[] Response(float low, float mid, float high, float[] geq)
        {
            var bands = new System.Collections.Generic.List<Biquad>();
            if (Math.Abs(low) > 0.01f) { var b = new Biquad(); b.LowShelf(Fs, DspMixer.FreqLow, low); bands.Add(b); }
            if (Math.Abs(mid) > 0.01f) { var b = new Biquad(); b.Peaking(Fs, DspMixer.FreqMid, mid, 0.7); bands.Add(b); }
            if (Math.Abs(high) > 0.01f) { var b = new Biquad(); b.HighShelf(Fs, DspMixer.FreqHigh, high); bands.Add(b); }
            if (geq != null)
                for (int i = 0; i < geq.Length && i < GraphicEq.Freqs.Length; i++)
                    if (Math.Abs(geq[i]) > 0.01f) { var b = new Biquad(); b.Peaking(Fs, Math.Min(GraphicEq.Freqs[i], Fs * 0.45), geq[i], GraphicEq.Q); bands.Add(b); }
            var r = new float[Points];
            for (int i = 0; i < Points; i++)
            {
                double f = FreqAt(i), mag = 1.0;
                foreach (var b in bands) mag *= b.MagnitudeAt(Fs, f);
                r[i] = (float)(20.0 * Math.Log10(Math.Max(1e-6, mag)));
            }
            return r;
        }

        /// <summary>Kurve zeichnen: Null-Linie, Flaeche bis zur Kurve, Kurve. on = EQ an (sonst grau).</summary>
        public static void Draw(Graphics g, RectangleF r, float[] db, Color color, bool on)
        {
            if (db == null || r.Width < 6 || r.Height < 4) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float mid = (float)Math.Round(r.Y + r.Height / 2f) + 0.5f, half = r.Height / 2f - Theme.F(1);
            using (var pen = new Pen(Color.FromArgb(on ? 60 : 35, Theme.TextDim), Math.Max(1f, Theme.F(0.8f))) { DashStyle = DashStyle.Dot })
                g.DrawLine(pen, r.X, mid, r.Right, mid);
            var pts = new PointF[db.Length];
            for (int i = 0; i < db.Length; i++)
            {
                float v = Math.Max(-RangeDb, Math.Min(RangeDb, db[i]));
                pts[i] = new PointF(r.X + r.Width * i / (db.Length - 1f), mid - v / RangeDb * half);
            }
            Color c = on ? color : Theme.TextFaint;
            using (var fill = new GraphicsPath())
            {
                fill.AddLine(r.X, mid, pts[0].X, pts[0].Y);
                fill.AddLines(pts);
                fill.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, r.Right, mid);
                fill.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(on ? 55 : 25, c))) g.FillPath(b, fill);
            }
            using (var pen = new Pen(c, Math.Max(1f, Theme.F(1.3f))) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts);
            g.SmoothingMode = sm;
        }
    }
}
