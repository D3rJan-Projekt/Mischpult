using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>2.16: Lautheit (LUFS) der letzten Minuten – 300 Punkte, je nach Zeitraum alle 0,2 / 1 / 3 Sekunden einer.</summary>
    internal sealed class LoudnessHistory
    {
        public const int Points = 300;
        private readonly float[] _v = new float[Points];
        private int _head, _count;
        private double _acc;

        /// <summary>Zaehlt hoch, wenn ein Punkt dazukommt.</summary>
        public int Version { get; private set; }
        /// <summary>Zeitraum in Minuten (1, 5 oder 15).</summary>
        public int Minutes { get; private set; } = 5;
        public double SampleSec => Minutes * 60.0 / Points;
        public int Count => _count;
        /// <summary>Zuletzt gemessen (NaN = nichts).</summary>
        public float Last { get; private set; } = float.NaN;

        /// <summary>Punkt i, aeltester zuerst (NaN = keine Messung).</summary>
        public float this[int i] => _v[(_head + i) % Points];

        public void SetMinutes(int minutes)
        {
            minutes = minutes == 1 || minutes == 15 ? minutes : 5;
            if (minutes == Minutes) return;
            Minutes = minutes;
            Clear();
        }

        public void Clear()
        {
            _head = 0; _count = 0; _acc = 0; Last = float.NaN;
            Version++;
        }

        public void Add(float lufs)
        {
            if (float.IsInfinity(lufs) || lufs < -70f) lufs = float.NaN;   // Stille
            int w = (_head + _count) % Points;
            _v[w] = lufs;
            if (_count < Points) _count++; else _head = (_head + 1) % Points;
            Last = lufs;
            Version++;
        }

        /// <summary>Aus dem Takt: Zeit weiterzaehlen, faellig? Dann den Wert uebernehmen. [Momentan, Kurzzeit, Integriert] oder null.</summary>
        public bool Tick(float dt, float[] loudness)
        {
            _acc += Math.Max(0f, Math.Min(1f, dt));
            if (_acc < SampleSec) return false;
            _acc -= SampleSec;
            if (_acc > SampleSec) _acc = 0;
            float v = loudness == null || loudness.Length < 2 ? float.NaN : Minutes == 1 ? loudness[0] : loudness[1];
            Add(float.IsNaN(v) ? float.NaN : v);
            return true;
        }
    }

    /// <summary>Zeichnet den Lautheits-Verlauf: Kurve mit Farbverlauf (gruen → gelb → rot), Ziellinie bei −14 LUFS.</summary>
    internal sealed partial class LoudHistView
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _plot, _value;
        public const float Lo = -45f, Hi = -3f;
        private static readonly float[] Grid = { -9f, -14f, -23f, -36f };

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float top = r.Y + Theme.F(19), bottom = r.Bottom - Theme.F(7);
            _plot = new RectangleF((float)Math.Round(r.X + Theme.F(24)), (float)Math.Round(top), (float)Math.Round(r.Width - Theme.F(31)), (float)Math.Round(Math.Max(8f, bottom - top)));
            _value = new RectangleF(r.Right - Theme.F(56), r.Y + Theme.F(3), Theme.F(50), Theme.F(14));
        }

        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(_plot, _value)), 2, 2);

        private float Y(float lufs) => _plot.Bottom - (Math.Max(Lo, Math.Min(Hi, lufs)) - Lo) / (Hi - Lo) * _plot.Height;

        public void DrawStatic(Graphics g, int minutes, string caption = null)
        {
            var r = Bounds;
            if (r.Width < 40 || r.Height < 30) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (PaperLook) { DrawPaperStatic(g, minutes, caption); g.SmoothingMode = sm; return; }   // 3.4
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            // Ueberschrift (2.16.1 mit Geraet): passend verkleinern, notfalls mit "…" kuerzen
            string cap = string.IsNullOrEmpty(caption) ? "LAUTHEIT · " + minutes + " MIN" : caption;
            var capRect = new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(66), Theme.F(14));
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                var capFont = Theme.Fit(g, cap, capRect.Width, 9f, 7f, FontStyle.Bold);
                if (Theme.TextWidth(g, cap, capFont) > capRect.Width && cap.StartsWith("LAUTHEIT · ", StringComparison.Ordinal))
                {
                    cap = cap.Substring(11);   // zu lang: "HEADSET · 5 MIN" reicht
                    capFont = Theme.Fit(g, cap, capRect.Width, 9f, 7f, FontStyle.Bold);
                    // immer noch zu lang: den Namen kuerzen, der Zeitraum bleibt stehen
                    int cut = cap.LastIndexOf(" · ", StringComparison.Ordinal);
                    if (cut > 0 && Theme.TextWidth(g, cap, capFont) > capRect.Width)
                    {
                        string head = cap.Substring(0, cut), tail = cap.Substring(cut);
                        while (head.Length > 1 && Theme.TextWidth(g, head + "…" + tail, capFont) > capRect.Width) head = head.Substring(0, head.Length - 1);
                        cap = head.TrimEnd() + "…" + tail;
                    }
                }
                Theme.Str(g, cap, capFont, b, capRect, sf);
            }
            g.SmoothingMode = SmoothingMode.None;
            var font = Theme.Ui(7f, FontStyle.Bold);
            foreach (float db in Grid)
            {
                float y = (float)Math.Round(Y(db)) + 0.5f;
                bool target = db == -14f;
                using (var pen = new Pen(target ? Color.FromArgb(0x3c, 0x5a, 0x48) : Color.FromArgb(0x1e, 0x21, 0x27), 1f) { DashStyle = target ? DashStyle.Dash : DashStyle.Solid })
                    g.DrawLine(pen, _plot.X, y, _plot.Right, y);
                using (var b = new SolidBrush(target ? Color.FromArgb(0x6c, 0xb0, 0x84) : Color.FromArgb(0x5a, 0x62, 0x6e)))
                    Theme.Str(g, db.ToString("0").Replace("-", "−"), font, b, new RectangleF(r.X + Theme.F(2), y - Theme.F(5.5f), Theme.F(20), Theme.F(11)), Theme.Right);
            }
            g.SmoothingMode = sm;
        }

        private PointF[] _pts = new PointF[LoudnessHistory.Points + 2];

        public void DrawLive(Graphics g, LoudnessHistory h) { DrawLiveCore(g, h); if (Theme.CrtOn && !PaperLook) Theme.CrtOverlay(g, _plot); }   // 3.4.6: Roehrenbild

        private void DrawLiveCore(Graphics g, LoudnessHistory h)
        {
            if (h == null || _plot.Width < 10 || _plot.Height < 8) return;
            if (PaperLook) { DrawPaperLive(g, h); return; }   // 3.4
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float step = _plot.Width / (LoudnessHistory.Points - 1);
            int n = h.Count;
            using (var fill = new LinearGradientBrush(new RectangleF(_plot.X, _plot.Y - 1, _plot.Width, _plot.Height + 2), Color.Red, Color.Green, LinearGradientMode.Vertical))
            using (var line = new Pen(Color.FromArgb(220, 0xf2, 0xf4, 0xf6), Math.Max(1f, Theme.F(1.2f))) { LineJoin = LineJoin.Round })
            {
                // oben rot (zu laut), um −14 gelb/gruen, unten dunkel
                float Pos(float db) => 1f - (db - Lo) / (Hi - Lo);
                fill.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(150, 0xff, 0x4a, 0x3d), Color.FromArgb(140, 0xff, 0xc8, 0x3a), Color.FromArgb(130, 0x3d, 0xdc, 0x84), Color.FromArgb(70, 0x2a, 0x8a, 0x5a), Color.FromArgb(30, 0x2a, 0x8a, 0x5a) },
                    Positions = new[] { 0f, Pos(-9f), Pos(-16f), Pos(-30f), 1f },
                };
                // Kurve in Stuecken (Luecken = Stille)
                int i = 0;
                while (i < n)
                {
                    while (i < n && float.IsNaN(h[i])) i++;
                    int s = i;
                    while (i < n && !float.IsNaN(h[i])) i++;
                    int len = i - s;
                    if (len <= 0) continue;
                    for (int k = 0; k < len; k++)
                        _pts[k] = new PointF(_plot.Right - (n - 1 - (s + k)) * step, Y(h[s + k]));
                    if (len == 1) { _pts[1] = new PointF(_pts[0].X + Math.Max(1f, step), _pts[0].Y); len = 2; }
                    var poly = new PointF[len + 2];
                    Array.Copy(_pts, poly, len);
                    poly[len] = new PointF(_pts[len - 1].X, _plot.Bottom);
                    poly[len + 1] = new PointF(_pts[0].X, _plot.Bottom);
                    g.FillPolygon(fill, poly);
                    var ln = new PointF[len];
                    Array.Copy(_pts, ln, len);
                    g.DrawLines(line, ln);
                }
            }
            // aktueller Wert
            float last = h.Last;
            string t = float.IsNaN(last) ? "–" : last.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")).Replace("-", "−");
            Color c = float.IsNaN(last) ? Color.FromArgb(0x5a, 0x62, 0x6e) : last > -9f ? Theme.MeterRed : last > -16f ? Theme.MeterYellow : Theme.MeterGreen;
            using (var b = new SolidBrush(c))
                Theme.Str(g, t, Theme.Mono(9.5f), b, _value, Theme.Right);
            g.SmoothingMode = sm;
        }
    }
}
