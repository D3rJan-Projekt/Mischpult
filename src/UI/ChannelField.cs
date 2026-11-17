using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.17: Anzeigefeld der Kanaele (wo sonst die App-Symbole stehen) – pro Kanal waehlbar:
    /// Apps, Lautheit (LUFS), Spektrum, Pegel-Verlauf oder Album-Cover.
    /// </summary>
    internal static class ChannelField
    {
        public const int Apps = 0, Loud = 1, Spec = 2, Hist = 3, Cover = 4;
        public static readonly string[] Names = { "Apps", "Lautheit (LUFS)", "Spektrum", "Pegel-Verlauf", "Album-Cover" };
        public static readonly string[] Short = { "Apps", "Lautheit", "Spektrum", "Verlauf", "Cover" };
        public static readonly string[] Hints =
        {
            "Die App-Symbole des Kanals (Standard).",
            "Wie laut sich der Kanal anfühlt (LUFS, nach dem Fader) – grün = ruhig, gelb = kräftig, rot = sehr laut.",
            "Kleines Spektrum nur dieses Kanals: Bass links, Höhen rechts.",
            "Pegel der letzten Minute als Kurve – so sieht man, wann es laut war.",
            "Das Cover von dem, was gerade läuft (z. B. Spotify) – gut für den Musik-Kanal.",
        };

        public static int Clamp(int m) => Math.Max(Apps, Math.Min(Cover, m));

        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>LUFS-Zahl mit Komma und echtem Minus ("−18,4").</summary>
        public static string LufsText(float v) => v.ToString("0.0", De).Replace("-", "−");

        public static Color LufsColor(float v) => v > -9f ? Theme.MeterRed : v > -16f ? Theme.MeterYellow : Theme.MeterGreen;

        /// <summary>Dunkler Anzeige-Grund (wie die LCDs des gewaehlten Stils) – auch bei hellen Designs gut lesbar.</summary>
        public static void DrawScreen(Graphics g, RectangleF r, out bool lightScreen, out Color fg)
        {
            Theme.LcdColors(out var bg, out fg);
            if (Theme.EffLcd == 0) { bg = Theme.LcdBg; fg = Theme.LcdText; }
            lightScreen = Theme.Luma(bg) > 0.5f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, bg, RectangleF.Inflate(r, -Theme.F(1.5f), -Theme.F(1.5f)), Theme.F(3.5f));
            g.SmoothingMode = sm;
        }

        /// <summary>Lautheit: grosse Zahl, darunter Balken von −40 bis −6 mit Marke bei −14.</summary>
        public static void DrawLoud(Graphics g, RectangleF r, float value, bool quiet, string modeLetter)
        {
            DrawScreen(g, r, out bool light, out Color fg);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color dim = Theme.LcdOffText();
            Color c = quiet ? dim : light ? fg : LufsColor(value);
            var numR = new RectangleF(r.X + Theme.F(2), r.Y + Theme.F(3), r.Width - Theme.F(4), r.Height * 0.52f);
            using (var b = new SolidBrush(c))
                Theme.Str(g, quiet ? "–" : LufsText(value), Theme.Mono(r.Width < Theme.F(80) ? 13f : 15f), b, numR, Theme.Center);
            float barY = (float)Math.Round(r.Bottom - Theme.F(9)), barX = r.X + Theme.F(6), barW = r.Width - Theme.F(12), barH = Theme.F(3);
            using (var b = new SolidBrush(light ? Theme.Alpha(fg, 50) : Color.FromArgb(0x20, 0x23, 0x29))) g.FillRectangle(b, barX, barY, barW, barH);
            if (!quiet)
            {
                float f = Math.Max(0f, Math.Min(1f, (value + 40f) / 34f));
                using (var b = new LinearGradientBrush(new RectangleF(barX - 1, barY, barW + 2, barH), Theme.MeterGreen, Theme.MeterRed, LinearGradientMode.Horizontal))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Theme.MeterGreen, Theme.MeterGreen, Theme.MeterYellow, Theme.MeterRed },
                        Positions = new[] { 0f, (40f - 16f) / 34f, (40f - 12f) / 34f, 1f },
                    };
                    g.FillRectangle(b, barX, barY, Math.Max(1f, barW * f), barH);
                }
            }
            float tx = barX + barW * (40f - 14f) / 34f;   // Ziel −14 (Spotify, YouTube)
            using (var pen = new Pen(light ? fg : Color.FromArgb(0xe8, 0xeb, 0xef), Math.Max(1f, Theme.F(1)))) g.DrawLine(pen, tx, barY - Theme.F(2), tx, barY + barH + Theme.F(1));
            using (var b = new SolidBrush(dim))
                Theme.Str(g, "LUFS " + modeLetter, Theme.Ui(7f, FontStyle.Bold), b, new RectangleF(r.X, barY - Theme.F(11), r.Width, Theme.F(10)), Theme.Center);
            g.SmoothingMode = sm;
        }

        private static readonly float[] _lvl = new float[64], _pk = new float[64];

        /// <summary>Kleines Spektrum (Balken in der Kanalfarbe, Spitzen hell).</summary>
        public static void DrawSpec(Graphics g, RectangleF r, SpectrumModel m, Color col)
        {
            if (m == null) return;
            DrawScreen(g, r, out bool light, out Color fg);
            if (light) col = fg;
            var area = RectangleF.Inflate(r, -Theme.F(4), -Theme.F(4));
            float gap = Math.Max(1f, (float)Math.Round(Theme.F(1)));
            int bars = Math.Max(6, Math.Min(32, (int)((area.Width + gap) / (Theme.F(3.5f) + gap))));
            float bw = (float)Math.Floor((area.Width - gap * (bars - 1)) / bars);
            float used = bw * bars + gap * (bars - 1), x0 = (float)Math.Round(area.X + (area.Width - used) / 2f);
            m.Bars(bars, _lvl, _pk);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            Color top = Theme.Lighten(col, 0.45f);
            using (var b = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1, area.Width, area.Height + 2), top, Theme.Darken(col, 0.15f), LinearGradientMode.Vertical))
            using (var pk = new SolidBrush(light ? fg : Color.FromArgb(230, 0xf4, 0xf6, 0xf8)))
                for (int i = 0; i < bars; i++)
                {
                    float x = x0 + i * (bw + gap);
                    float h = (float)Math.Round(area.Height * _lvl[i]);
                    if (h >= 1f) g.FillRectangle(b, x, area.Bottom - h, bw, h);
                    float p = (float)Math.Round(area.Height * _pk[i]);
                    if (p > h + 1f) g.FillRectangle(pk, x, area.Bottom - p, bw, 1f);
                }
            g.SmoothingMode = sm;
        }

        /// <summary>Pegel-Verlauf: Flaeche in der Kanalfarbe, oben hell die Kurve, gestrichelt −18 dB.</summary>
        public static void DrawHist(Graphics g, RectangleF r, LevelHistory h, Color col)
        {
            if (h == null) return;
            DrawScreen(g, r, out bool light, out Color fg);
            if (light) col = fg;
            var area = RectangleF.Inflate(r, -Theme.F(4), -Theme.F(4));
            var sm = g.SmoothingMode;
            float y18 = (float)Math.Round(area.Bottom - area.Height * LevelHistory.Norm(-18f)) + 0.5f;
            using (var pen = new Pen(light ? Theme.Alpha(fg, 90) : Color.FromArgb(0x2c, 0x30, 0x37), 1f) { DashStyle = DashStyle.Dot }) g.DrawLine(pen, area.X, y18, area.Right, y18);
            int n = LevelHistory.Points;
            var pts = new PointF[n + 2];
            float step = area.Width / (n - 1);
            for (int i = 0; i < n; i++) pts[i] = new PointF(area.X + i * step, area.Bottom - area.Height * LevelHistory.Norm(h[i]));
            pts[n] = new PointF(area.Right, area.Bottom);
            pts[n + 1] = new PointF(area.X, area.Bottom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1, area.Width, area.Height + 2), Color.FromArgb(150, col), Color.FromArgb(25, col), LinearGradientMode.Vertical))
                g.FillPolygon(b, pts);
            var line = new PointF[n];
            Array.Copy(pts, line, n);
            using (var pen = new Pen(Theme.Lighten(col, 0.4f), Math.Max(1f, Theme.F(1.1f))) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, line);
            g.SmoothingMode = sm;
        }

        /// <summary>Album-Cover (fuellend) mit Titel unten; ohne Cover eine Note und der Titel.</summary>
        public static void DrawCoverField(Graphics g, RectangleF r, Bitmap cover, string text, bool playing)
        {
            var inner = RectangleF.Inflate(r, -Theme.F(1), -Theme.F(1));
            if (cover != null) ChannelImages.DrawCover(g, inner, cover, Theme.F(4), playing ? 40 : 120);
            else
                using (var b = new SolidBrush(Theme.LcdOffText()))
                    Theme.Str(g, "♪", Theme.Ui(18f), b, new RectangleF(r.X, r.Y, r.Width, r.Height - Theme.F(10)), Theme.Center);
            string t = string.IsNullOrEmpty(text) ? "Nichts läuft" : text;
            var tr = new RectangleF(r.X + Theme.F(3), r.Bottom - Theme.F(13), r.Width - Theme.F(6), Theme.F(12));
            var font = Theme.Ui(7.5f, FontStyle.Bold);
            // passt der Titel nicht, linksbuendig mit "…" statt in der Mitte abgeschnitten
            bool fits = Theme.TextWidth(g, t, font) <= tr.Width;
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = fits ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                using (var sh = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    Theme.Str(g, t, font, sh, new RectangleF(tr.X + 1, tr.Y + 1, tr.Width, tr.Height), sf);
                using (var b = new SolidBrush(cover != null ? Color.White : Theme.TextDim))
                    Theme.Str(g, t, font, b, tr, sf);
            }
        }
    }

    /// <summary>2.17: Pegel der letzten Minute (120 Punkte, alle 0,5 s der lauteste Wert) in dB.</summary>
    internal sealed class LevelHistory
    {
        public const int Points = 120;
        public const float SampleSec = 0.5f, Floor = -60f;
        private readonly float[] _v = new float[Points];
        private int _head;
        private float _acc, _max;

        public int Version { get; private set; }

        public LevelHistory() { for (int i = 0; i < Points; i++) _v[i] = Floor; }

        /// <summary>Punkt i, aeltester zuerst.</summary>
        public float this[int i] => _v[(_head + i) % Points];

        public static float Norm(float db) => Math.Max(0f, Math.Min(1f, (db - Floor) / -Floor));

        /// <summary>Aus dem Pegel-Takt: Spitze (linear 0..1+) und vergangene Zeit. true = neuer Punkt.</summary>
        public bool Add(float peak, float dt)
        {
            if (peak > _max) _max = peak;
            _acc += Math.Max(0f, Math.Min(0.5f, dt));
            if (_acc < SampleSec) return false;
            _acc -= SampleSec;
            if (_acc > SampleSec) _acc = 0f;
            float db = _max > 1e-6f ? (float)(20 * Math.Log10(_max)) : Floor;
            _v[_head] = Math.Max(Floor, Math.Min(6f, db));
            _head = (_head + 1) % Points;
            _max = 0f;
            Version++;
            return true;
        }
    }
}
