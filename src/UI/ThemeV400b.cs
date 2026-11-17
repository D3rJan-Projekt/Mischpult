using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 Optik: Nixie-Balken (Pegel wie IN-13-Balkenroehren), Wortuhr und Binaeruhr.
    /// </summary>
    internal static partial class Theme
    {
        // ================================================================ 200 Nixie-Balken

        /// <summary>Pegel leuchten wie Nixie-Balkenroehren: orange Neon-Glimmen hinter Glas statt LEDs.</summary>
        public static bool NixieBar;

        private static readonly Color NixieLow = Color.FromArgb(0xff, 0x7a, 0x26), NixieMid = Color.FromArgb(0xff, 0x96, 0x3a), NixieTop = Color.FromArgb(0xff, 0x4a, 0x28);
        /// <summary>Unbeleuchtete Kathode hinter dem Glas.</summary>
        public static readonly Color NixieOff = Color.FromArgb(0x24, 0x14, 0x0c);

        /// <summary>Farbe eines Segments im Nixie-Balken (unten etwas dunkler, oben heller, ganz oben roetlich).</summary>
        private static Color NixieSeg(float frac, float midTo)
        {
            if (frac > midTo) return LedLevel(NixieTop);
            return LedLevel(Mix(NixieLow, NixieMid, Math.Max(0f, Math.Min(1f, frac / Math.Max(0.05f, midTo)))));
        }

        /// <summary>Glasroehre hinter den Pegeln (abgerundet, braunes Glas, Halterung oben und unten).</summary>
        private static void DrawNixieBezel(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(r.Width / 2f, F(4));
            FillRound(g, Color.FromArgb(0x0c, 0x07, 0x05), r, rad);
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.FromArgb(60, 0x6a, 0x40, 0x22), Color.FromArgb(10, 0x6a, 0x40, 0x22), LinearGradientMode.Horizontal))
                FillRoundBrush(g, b, r, rad);
            using (var pen = new Pen(Color.FromArgb(70, 0xff, 0xd8, 0xb0), Math.Max(1f, F(0.8f)))) g.DrawPath(pen, Round(r, rad));
            // Sockel unten, Spitze oben
            using (var b = new SolidBrush(Color.FromArgb(0x30, 0x2a, 0x26)))
                g.FillRectangle(b, r.X + F(1), r.Bottom - F(1.5f), r.Width - F(2), F(1.5f));
            g.SmoothingMode = sm;
        }

        /// <summary>Spiegelung auf dem Glas (nach dem Glimmen).</summary>
        private static void DrawNixieGlass(Graphics g, RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var b = new SolidBrush(Color.FromArgb(26, 255, 236, 210)))
                g.FillRectangle(b, r.X + F(1), r.Y + F(2), Math.Max(1f, F(1.2f)), r.Height - F(4));
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, F(14) + 2), Color.FromArgb(40, 255, 240, 220), Color.FromArgb(0, 255, 240, 220), LinearGradientMode.Vertical))
                g.FillRectangle(b, r.X, r.Y, r.Width, F(14));
        }

        private static void FillRoundBrush(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) g.FillPath(b, p);
        }

        // ================================================================ 182 Kraeftige Schrift

        /// <summary>Barrierefreiheit: graue Beschriftungen heller (nach dem Farbschema angewendet).</summary>
        public static bool StrongText;

        private static void StrongTextColors()
        {
            TextDim = Mix(TextDim, Text, 0.55f);
            TextFaint = Mix(TextFaint, Text, 0.6f);
            ScaleLabel = Mix(ScaleLabel, Text, 0.55f);
            ScaleTick = Mix(ScaleTick, Text, 0.35f);
            EngraveText = Mix(EngraveText, Text, 0.45f);
        }

        // ================================================================ 216 Wortuhr

        private static readonly string[] HourWords = { "ZWÖLF", "EINS", "ZWEI", "DREI", "VIER", "FÜNF", "SECHS", "SIEBEN", "ACHT", "NEUN", "ZEHN", "ELF" };

        /// <summary>Wortuhr wie an der Wand: "ES IST HALB ZEHN" (auf 5 Minuten, die Minuten dazwischen als Punkte).</summary>
        public static string WordClockText(DateTime t) => string.Join(" ", WordClockWords(t, out _));

        public static List<string> WordClockWords(DateTime t, out int dots)
        {
            int m5 = t.Minute / 5;
            dots = t.Minute % 5;
            int h = t.Hour % 12;
            int next = (h + 1) % 12;
            var w = new List<string> { "ES", "IST" };
            switch (m5)
            {
                case 0: w.Add(h == 1 ? "EIN" : HourWords[h]); w.Add("UHR"); break;
                case 1: w.Add("FÜNF"); w.Add("NACH"); w.Add(HourWords[h]); break;
                case 2: w.Add("ZEHN"); w.Add("NACH"); w.Add(HourWords[h]); break;
                case 3: w.Add("VIERTEL"); w.Add("NACH"); w.Add(HourWords[h]); break;
                case 4: w.Add("ZWANZIG"); w.Add("NACH"); w.Add(HourWords[h]); break;
                case 5: w.Add("FÜNF"); w.Add("VOR"); w.Add("HALB"); w.Add(HourWords[next]); break;
                case 6: w.Add("HALB"); w.Add(HourWords[next]); break;
                case 7: w.Add("FÜNF"); w.Add("NACH"); w.Add("HALB"); w.Add(HourWords[next]); break;
                case 8: w.Add("ZWANZIG"); w.Add("VOR"); w.Add(HourWords[next]); break;
                case 9: w.Add("VIERTEL"); w.Add("VOR"); w.Add(HourWords[next]); break;
                case 10: w.Add("ZEHN"); w.Add("VOR"); w.Add(HourWords[next]); break;
                default: w.Add("FÜNF"); w.Add("VOR"); w.Add(HourWords[next]); break;
            }
            return w;
        }

        private const string WordFill = "ESKISTAFÜNFZEHNZWANZIGDREIVIERTELVORFUNKNACHHALBAELFÜNFEINSXAMZWEIDREIPMJVIERSECHSNLACHTSIEBENZWÖLFZEHNEUNKUHR";

        /// <summary>Raster der Wortuhr: Zeilen mit je 'cols' Buchstaben, lit[i] = Buchstabe leuchtet.</summary>
        public static List<KeyValuePair<string, bool[]>> WordClockGrid(DateTime t, int cols, int rows)
        {
            var words = WordClockWords(t, out _);
            var lines = new List<List<string>>();
            var cur = new List<string>();
            int len = 0;
            foreach (var w in words)
            {
                int add = cur.Count == 0 ? w.Length : len + 1 + w.Length;
                if (add > cols && cur.Count > 0) { lines.Add(cur); cur = new List<string>(); len = 0; add = w.Length; }
                cur.Add(w); len = add;
            }
            if (cur.Count > 0) lines.Add(cur);
            var grid = new List<KeyValuePair<string, bool[]>>();
            int fill = (t.Hour * 7 + t.Minute / 5 * 3) % WordFill.Length;
            Func<char> next = () => { char c = WordFill[fill]; fill = (fill + 1) % WordFill.Length; return c; };
            int first = Math.Max(0, (rows - lines.Count) / 2);
            for (int row = 0; row < rows; row++)
            {
                int li = row - first;
                var sb = new StringBuilder();
                var lit = new bool[cols];
                if (li >= 0 && li < lines.Count)
                {
                    var l = lines[li];
                    int textLen = l.Sum(x => x.Length) + l.Count - 1;
                    int pad = Math.Max(0, (cols - textLen) / 2);
                    for (int i = 0; i < pad; i++) sb.Append(next());
                    for (int k = 0; k < l.Count; k++)
                    {
                        if (k > 0) sb.Append(next());
                        int at = sb.Length;
                        sb.Append(l[k]);
                        for (int i = at; i < sb.Length && i < cols; i++) lit[i] = true;
                    }
                    while (sb.Length < cols) sb.Append(next());
                }
                else for (int i = 0; i < cols; i++) sb.Append(next());
                string s = sb.ToString();
                if (s.Length > cols) s = s.Substring(0, cols);
                grid.Add(new KeyValuePair<string, bool[]>(s, lit));
            }
            return grid;
        }

        private static Color ClockGlow => EffLcd == 1 ? Color.FromArgb(0xe8, 0xff, 0xb8) : LcdText;

        /// <summary>Wortuhr in der Meterbridge: Buchstabenraster, die Zeit leuchtet, die Minuten dazwischen als Punkte in den Ecken.</summary>
        public static void DrawWordClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0x0b, 0x0b, 0x0d), r, F(5));
                StrokeRound(g, Color.FromArgb(0x2a, 0x2c, 0x30), F(1), r, F(5));
                g.SmoothingMode = sm;
                return;
            }
            var now = DateTime.Now;
            const int cols = 11;
            int rows = r.Height / r.Width > 0.55f ? 5 : 4;
            var grid = WordClockGrid(now, cols, rows);
            WordClockWords(now, out int dots);
            float padX = F(9), padY = F(6);
            float cw = (r.Width - padX * 2) / cols, ch = (r.Height - padY * 2) / rows;
            var font = Ui(Math.Max(6f, Math.Min(cw, ch) / S * 0.78f), FontStyle.Bold);
            Color on = ClockGlow, off = Color.FromArgb(0x2c, 0x2e, 0x33);
            using (var bOn = new SolidBrush(on))
            using (var bGlow = new SolidBrush(Color.FromArgb(55, on)))
            using (var bOff = new SolidBrush(off))
                for (int row = 0; row < rows; row++)
                {
                    var line = grid[row];
                    for (int i = 0; i < cols; i++)
                    {
                        var cell = new RectangleF(r.X + padX + i * cw, r.Y + padY + row * ch, cw, ch);
                        string c = line.Key[i].ToString();
                        if (line.Value[i])
                        {
                            Str(g, c, font, bGlow, new RectangleF(cell.X - F(0.8f), cell.Y, cell.Width, cell.Height), Center);
                            Str(g, c, font, bGlow, new RectangleF(cell.X + F(0.8f), cell.Y, cell.Width, cell.Height), Center);
                            Str(g, c, font, bOn, cell, Center);
                        }
                        else Str(g, c, font, bOff, cell, Center);
                    }
                }
            // Minuten-Punkte in den vier Ecken (im Uhrzeigersinn ab oben links)
            float d = Math.Max(2f, F(2.4f)), o = F(4);
            var corners = new[] { new PointF(r.X + o, r.Y + o), new PointF(r.Right - o, r.Y + o), new PointF(r.Right - o, r.Bottom - o), new PointF(r.X + o, r.Bottom - o) };
            for (int i = 0; i < 4; i++)
                FillEllipse(g, i < dots ? on : off, corners[i].X, corners[i].Y, d / 2f);
            g.SmoothingMode = sm;
        }

        /// <summary>Wortuhr klein in der Kopfleiste: nur die Worte ohne "ES IST".</summary>
        public static void DrawWordClockSmall(Graphics g, RectangleF r)
        {
            var now = DateTime.Now;
            var w = WordClockWords(now, out int dots);
            string t = string.Join(" ", w.GetRange(2, w.Count - 2));
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, Color.FromArgb(0x0b, 0x0b, 0x0d), r, F(4));
            StrokeRound(g, Color.FromArgb(0x2a, 0x2c, 0x30), F(1), r, F(4));
            float px = 10f;
            var inner = RectangleF.Inflate(r, -F(4), -F(1));
            while (px > 6f && TextWidth(g, t, Ui(px, FontStyle.Bold)) > inner.Width) px -= 0.5f;
            using (var b = new SolidBrush(ClockGlow)) Str(g, t, Ui(px, FontStyle.Bold), b, new RectangleF(inner.X, inner.Y, inner.Width, inner.Height - F(2)), Center);
            float d = Math.Max(1.5f, F(1.6f));
            for (int i = 0; i < 4; i++)
                FillEllipse(g, i < dots ? ClockGlow : Color.FromArgb(0x2c, 0x2e, 0x33), r.X + r.Width / 2f + (i - 1.5f) * F(5), r.Bottom - F(2.5f), d / 2f);
            g.SmoothingMode = sm;
        }

        // ================================================================ 216 Binaeruhr

        /// <summary>Binaeruhr (BCD): 6 Spalten (Stunde, Minute, Sekunde je Zehner und Einer), Bit 0 = Wert 1 … Bit 3 = Wert 8.</summary>
        public static int[] BinaryClockDigits(DateTime t) => new[] { t.Hour / 10, t.Hour % 10, t.Minute / 10, t.Minute % 10, t.Second / 10, t.Second % 10 };
        private static readonly int[] BinaryRows = { 2, 4, 3, 4, 3, 4 };   // so viele Bits braucht jede Spalte

        public static void DrawBinaryClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0x0b, 0x0b, 0x0d), r, F(5));
                StrokeRound(g, Color.FromArgb(0x2a, 0x2c, 0x30), F(1), r, F(5));
                g.SmoothingMode = sm;
                return;
            }
            DrawBinaryDots(g, r, true);
            g.SmoothingMode = sm;
        }

        private static void DrawBinaryDots(Graphics g, RectangleF r, bool labels)
        {
            var dg = BinaryClockDigits(DateTime.Now);
            float padX = F(labels ? 10 : 4), padY = F(labels ? 6 : 2.5f), labelH = labels ? F(11) : 0f, groupGap = F(labels ? 7 : 3);
            float colW = (r.Width - padX * 2 - groupGap * 2) / 6f, rowH = (r.Height - padY * 2 - labelH) / 4f;
            float d = Math.Max(2f, Math.Min(colW, rowH) * 0.68f);
            Color on = ClockGlow, off = Color.FromArgb(0x26, 0x28, 0x2c);
            var font = Ui(7.5f, FontStyle.Bold);
            for (int c = 0; c < 6; c++)
            {
                float cx = r.X + padX + c * colW + (c / 2) * groupGap + colW / 2f;
                for (int bit = 0; bit < 4; bit++)
                {
                    if (bit >= BinaryRows[c]) continue;
                    float cy = r.Y + padY + (3 - bit) * rowH + rowH / 2f;
                    bool lit = ((dg[c] >> bit) & 1) != 0;
                    if (lit) FillEllipse(g, Color.FromArgb(60, on), cx, cy, d * 0.85f);
                    FillEllipse(g, lit ? on : off, cx, cy, d / 2f);
                }
                if (labels && c % 2 == 0)
                    using (var b = new SolidBrush(Color.FromArgb(0x7a, 0x80, 0x88)))
                        Str(g, c == 0 ? "STD" : c == 2 ? "MIN" : "SEK", font, b, new RectangleF(cx - colW / 2f, r.Bottom - padY - labelH, colW * 2, labelH), Center);
            }
        }

        public static void DrawBinaryClockSmall(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, Color.FromArgb(0x0b, 0x0b, 0x0d), r, F(4));
            StrokeRound(g, Color.FromArgb(0x2a, 0x2c, 0x30), F(1), r, F(4));
            DrawBinaryDots(g, r, false);
            g.SmoothingMode = sm;
        }
    }
}
