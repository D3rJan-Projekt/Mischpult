using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.3: Ducking sichtbar – eine bernsteinfarbene Linie vom ausloesenden Kanal zu den leiser gemachten.</summary>
    internal sealed partial class MeterBridge
    {
        public static bool DuckLinesOn;
        /// <summary>Kanal, der gerade duckt (null = Ducking nicht aktiv), und die Kanaele, die leiser werden.</summary>
        public static string DuckFrom;
        public static readonly HashSet<string> DuckTo = new HashSet<string>();
        public static float DuckDb;
        public static int DuckVer;
        private int _duckVerShown = -1;

        /// <summary>3.4: wann die Uhr neu muss – jede Sekunde, beim Rollzaehlwerk in der letzten Viertelsekunde oefter (die Rolle dreht sichtbar).</summary>
        private static int ClockKeyNow
        {
            get
            {
                var now = DateTime.Now;
                if (Theme.ClockKind != 5) return now.Second;
                int roll = now.Millisecond >= 750 ? 1 + (now.Millisecond - 750) / 25 : 0;
                return now.Second * 100 + roll;
            }
        }

        private void DrawDuckLines(Graphics g)
        {
            Col from = null;
            var to = new List<Col>();
            foreach (var c in _cols)
            {
                if (c.Strip is MicStrip && !c.Win.IsEmpty && DuckFrom == MixerSettings.DuckMicId) { from = c; continue; }   // 4.2.2: du sprichst
                if (!(c.Strip is ChannelStrip cs) || c.Win.IsEmpty) continue;
                if (cs.Config.Id == DuckFrom) from = c;
                else if (DuckTo.Contains(cs.Config.Id)) to.Add(c);
            }
            if (from == null || to.Count == 0) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color amber = Theme.SoloAmber;
            float y = Math.Max(Theme.F(3), from.Win.Y - Theme.F(3));
            foreach (var c in to) y = Math.Min(y, Math.Max(Theme.F(3), c.Win.Y - Theme.F(3)));
            float fx = from.Win.X + from.Win.Width / 2f;
            float minX = fx, maxX = fx;
            foreach (var c in to) { float x = c.Win.X + c.Win.Width / 2f; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }
            using (var glow = new Pen(Theme.Alpha(amber, 60), Math.Max(3f, Theme.F(4))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(glow, minX, y, maxX, y);
            using (var pen = new Pen(amber, Math.Max(1.2f, Theme.F(1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, minX, y, maxX, y);
                float a = Theme.F(4);
                using (var ab = new SolidBrush(amber))
                    foreach (var c in to)
                    {
                        float x = c.Win.X + c.Win.Width / 2f;
                        g.FillPolygon(ab, new[] { new PointF(x - a, y + Theme.F(1)), new PointF(x + a, y + Theme.F(1)), new PointF(x, y + a + Theme.F(2)) });
                    }
            }
            float d = Theme.F(7);
            using (var b = new SolidBrush(amber)) g.FillEllipse(b, fx - d / 2f, y - d / 2f, d, d);
            using (var b = new SolidBrush(Theme.Alpha(amber, 70))) g.FillEllipse(b, fx - d, y - d, d * 2, d * 2);
            string label = DuckDb < -0.5f ? "DUCK " + DuckDb.ToString("0", System.Globalization.CultureInfo.InvariantCulture).Replace("-", "−") + " dB" : "DUCK";
            var font = Theme.Ui(7f, FontStyle.Bold);
            float tw = Theme.TextWidth(g, label, font) + Theme.F(8);
            // Beschriftung neben dem Punkt, auf der Seite ohne Ziele (verdeckt keine Spalten-Titel)
            bool rightSide = maxX <= fx + 1f || (minX < fx && fx - minX >= maxX - fx);
            var lr = new RectangleF(rightSide ? fx + d + Theme.F(2) : fx - d - Theme.F(2) - tw, y - Theme.F(6), tw, Theme.F(12));
            if (lr.X < 0) lr.X = 0;
            if (lr.Right > Width) lr.X = Width - lr.Width;
            if (lr.Y < 0) lr.Y = 0;
            {
                Theme.FillRound(g, Color.FromArgb(200, 0x10, 0x11, 0x14), lr, Theme.F(3));
                using (var b = new SolidBrush(amber)) Theme.Str(g, label, font, b, lr, Theme.Center);
            }
            g.SmoothingMode = sm;
        }
    }
}

namespace Mischpult.UI
{
    /// <summary>3.4: PC-Auslastung (Prozessor, Grafikkarte) als zwei kleine Zeigerinstrumente.</summary>
    internal sealed partial class MeterBridge
    {
        public static bool PcOn;
        private readonly PcView _pcV = new PcView();
        private RectangleF _pcRect;
        private float _pcW;
        private int _pcVer = -1;
        private readonly System.Diagnostics.Stopwatch _pcClock = System.Diagnostics.Stopwatch.StartNew();
        private double _pcLast;

        private float PcDt()
        {
            double t = _pcClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.2, t - _pcLast);
            _pcLast = t;
            return dt;
        }

        // ---- 3.5: Gluehende Roehren (gleich gebaut wie die PC-Auslastung)
        public static bool TubesOn;
        private readonly TubeView _tubeV = new TubeView();
        private RectangleF _tubeRect;
        private float _tubeW;
        private int _tubeVer = -1;
        private double _tubeLast;

        private float TubeDt()
        {
            double t = _pcClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.2, t - _tubeLast);
            _tubeLast = t;
            return dt;
        }

        private void PlaceTubes(float x)
        {
            float pad = Theme.F(6), top = (float)Math.Round(Theme.F(5)), hh = (float)Math.Round(Height - Theme.F(10));
            var r = new RectangleF((float)Math.Round(x + Theme.F(3)), top, _tubeW, hh);
            AddItem("#t", new RectangleF(x, top, _tubeW + pad, hh));
            _tubeRect = r;
            _tubeV.Layout(r);
            _sb.Append("|tu:").Append(r.X).Append(',').Append(r.Width);
        }

        private void PlacePc(float x)
        {
            float pad = Theme.F(6), top = (float)Math.Round(Theme.F(5)), hh = (float)Math.Round(Height - Theme.F(10));
            var r = new RectangleF((float)Math.Round(x + Theme.F(3)), top, _pcW, hh);
            AddItem("#p", new RectangleF(x, top, _pcW + pad, hh));
            _pcRect = r;
            _pcV.Layout(r);
            _sb.Append("|pc:").Append(r.X).Append(',').Append(r.Width);
        }
    }
}
