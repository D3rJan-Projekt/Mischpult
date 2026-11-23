using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.4: QR-Code fuer die Fernbedienung (Byte-Modus, Fehlerkorrektur M, Version 1–10). Nur das Noetigste, ohne fremde Bibliothek.
    /// </summary>
    internal static class Qr440
    {
        // Version 1..10, Stufe M: Codewoerter gesamt, Fehlerkorrektur je Block, Bloecke
        private static readonly int[] Total = { 0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };
        private static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        private static readonly int[] Blocks = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
        private static readonly int[][] Align =
        {
            new int[0], new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        private static int DataBytes(int v) => Total[v] - EccPerBlock[v] * Blocks[v];
        /// <summary>Nur fuer Tests: feste Maske (−1 = die beste waehlen).</summary>
        internal static int ForceMask = -1;

        /// <summary>QR-Code als Module [y, x] (true = dunkel), null wenn der Text zu lang ist.</summary>
        public static bool[,] Encode(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            int ver = 0;
            for (int v = 1; v <= 10; v++)
            {
                int bits = 4 + (v < 10 ? 8 : 16) + bytes.Length * 8;
                if (bits <= DataBytes(v) * 8) { ver = v; break; }
            }
            if (ver == 0) return null;

            // Datenbits
            var bb = new List<bool>();
            void Put(int val, int n) { for (int i = n - 1; i >= 0; i--) bb.Add(((val >> i) & 1) != 0); }
            Put(0x4, 4);
            Put(bytes.Length, ver < 10 ? 8 : 16);
            foreach (var b in bytes) Put(b, 8);
            int cap = DataBytes(ver) * 8;
            Put(0, Math.Min(4, cap - bb.Count));
            while (bb.Count % 8 != 0) bb.Add(false);
            for (int pad = 0xEC; bb.Count < cap; pad ^= 0xEC ^ 0x11) Put(pad, 8);
            var data = new byte[bb.Count / 8];
            for (int i = 0; i < bb.Count; i++) if (bb[i]) data[i >> 3] |= (byte)(0x80 >> (i & 7));

            var all = AddEcc(data, ver);
            int size = 17 + 4 * ver;
            var mod = new bool[size, size];
            var fn = new bool[size, size];
            DrawFunction(mod, fn, ver, size);
            DrawData(mod, fn, all, size);

            int best = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(mod, fn, m, size);
                DrawFormat(mod, fn, m, size);
                int sc = Penalty(mod, size);
                if (sc < bestScore) { bestScore = sc; best = m; }
                ApplyMask(mod, fn, m, size);   // zuruecknehmen (XOR)
            }
            if (ForceMask >= 0 && ForceMask < 8) best = ForceMask;
            ApplyMask(mod, fn, best, size);
            DrawFormat(mod, fn, best, size);
            return mod;
        }

        // ================================================================ Fehlerkorrektur

        private static byte[] AddEcc(byte[] data, int ver)
        {
            int nb = Blocks[ver], ecc = EccPerBlock[ver], raw = Total[ver];
            int shortCount = nb - raw % nb, shortLen = raw / nb;
            var div = RsDivisor(ecc);
            var blocks = new byte[nb][];
            for (int i = 0, k = 0; i < nb; i++)
            {
                int len = shortLen - ecc + (i < shortCount ? 0 : 1);
                var dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                var block = new byte[shortLen + 1];
                Array.Copy(dat, block, len);
                var rem = RsRemainder(dat, div);
                Array.Copy(rem, 0, block, block.Length - ecc, ecc);
                blocks[i] = block;
            }
            var res = new byte[raw];
            for (int i = 0, k = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < nb; j++)
                    if (i != shortLen - ecc || j >= shortCount) res[k++] = blocks[j][i];
            return res;
        }

        private static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        private static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 2);
            }
            return r;
        }

        private static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (var b in data)
            {
                int f = (b ^ r[0]) & 0xFF;
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], f);
            }
            return r;
        }

        // ================================================================ Muster

        private static void Set(bool[,] mod, bool[,] fn, int x, int y, bool dark) { mod[y, x] = dark; fn[y, x] = true; }

        private static void DrawFunction(bool[,] mod, bool[,] fn, int ver, int size)
        {
            for (int i = 0; i < size; i++) { Set(mod, fn, 6, i, i % 2 == 0); Set(mod, fn, i, 6, i % 2 == 0); }
            Finder(mod, fn, 3, 3, size); Finder(mod, fn, size - 4, 3, size); Finder(mod, fn, 3, size - 4, size);
            var al = Align[ver];
            for (int i = 0; i < al.Length; i++)
                for (int j = 0; j < al.Length; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == al.Length - 1) || (i == al.Length - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(mod, fn, al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormat(mod, fn, 0, size);   // Platz reservieren
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                long bits = (long)ver << 12 | (long)rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(mod, fn, a, b, bit);
                    Set(mod, fn, b, a, bit);
                }
            }
        }

        private static void Finder(bool[,] mod, bool[,] fn, int cx, int cy, int size)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(mod, fn, x, y, d != 2 && d != 4);
                }
        }

        private static void DrawFormat(bool[,] mod, bool[,] fn, int mask, int size)
        {
            int data = (0 << 3) | mask;   // Stufe M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            bool B(int i) => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(mod, fn, 8, i, B(i));
            Set(mod, fn, 8, 7, B(6));
            Set(mod, fn, 8, 8, B(7));
            Set(mod, fn, 7, 8, B(8));
            for (int i = 9; i < 15; i++) Set(mod, fn, 14 - i, 8, B(i));
            for (int i = 0; i < 8; i++) Set(mod, fn, size - 1 - i, 8, B(i));
            for (int i = 8; i < 15; i++) Set(mod, fn, 8, size - 15 + i, B(i));
            Set(mod, fn, 8, size - 8, true);
        }

        private static void DrawData(bool[,] mod, bool[,] fn, byte[] data, int size)
        {
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool up = ((right + 1) & 2) == 0;
                        int y = up ? size - 1 - vert : vert;
                        if (!fn[y, x] && i < data.Length * 8)
                        {
                            mod[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        private static void ApplyMask(bool[,] mod, bool[,] fn, int m, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (fn[y, x]) continue;
                    bool inv;
                    switch (m)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) mod[y, x] = !mod[y, x];
                }
        }

        /// <summary>Strafpunkte nach der Norm (Laeufe, 2x2-Bloecke, Finder-aehnliche Folgen, Verhaeltnis hell/dunkel).</summary>
        private static int Penalty(bool[,] mod, int size)
        {
            int score = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool cur = pass == 0 ? mod[a, b] : mod[b, a], prev = pass == 0 ? mod[a, b - 1] : mod[b - 1, a];
                        if (cur == prev) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                        else run = 1;
                    }
                    // 1:1:3:1:1 mit 4 hellen Modulen davor oder danach
                    for (int b = 0; b + 10 < size; b++)
                    {
                        bool M(int k) => pass == 0 ? mod[a, b + k] : mod[b + k, a];
                        bool core = M(0) && !M(1) && M(2) && M(3) && M(4) && !M(5) && M(6);
                        bool core2 = M(4) && !M(5) && M(6) && M(7) && M(8) && !M(9) && M(10);
                        if (core && !M(7) && !M(8) && !M(9) && !M(10)) score += 40;
                        if (core2 && !M(0) && !M(1) && !M(2) && !M(3)) score += 40;
                    }
                }
            int dark = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (mod[y, x]) dark++;
                    if (x + 1 < size && y + 1 < size && mod[y, x] == mod[y, x + 1] && mod[y, x] == mod[y + 1, x] && mod[y, x] == mod[y + 1, x + 1]) score += 3;
                }
            int total = size * size;
            int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return score + Math.Max(0, k2) * 10;
        }

        // ================================================================ Zeichnen

        /// <summary>QR-Code mit Ruhezone (4 Module) in das Rechteck zeichnen – dunkel auf weiss.</summary>
        public static void Draw(Graphics g, bool[,] mod, RectangleF r)
        {
            if (mod == null) return;
            int n = mod.GetLength(0), full = n + 8;
            float m = (float)Math.Floor(Math.Min(r.Width, r.Height) / full);
            if (m < 1) m = Math.Min(r.Width, r.Height) / full;
            float side = m * full, x0 = r.X + (r.Width - side) / 2f, y0 = r.Y + (r.Height - side) / 2f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.FillRectangle(Brushes.White, x0, y0, side, side);
            using (var b = new SolidBrush(Color.FromArgb(0x10, 0x12, 0x16)))
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (mod[y, x]) g.FillRectangle(b, x0 + (x + 4) * m, y0 + (y + 4) * m, m, m);
            g.SmoothingMode = sm;
        }

        /// <summary>QR-Code als Bild (fuer Tests und zum Kopieren).</summary>
        public static Bitmap ToBitmap(bool[,] mod, int scale)
        {
            int n = mod.GetLength(0), full = (n + 8) * scale;
            var bmp = new Bitmap(full, full);
            using (var g = Graphics.FromImage(bmp)) Draw(g, mod, new RectangleF(0, 0, full, full));
            return bmp;
        }
    }
}
