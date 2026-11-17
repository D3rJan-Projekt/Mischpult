using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>4.0: bewegte GIF-Bilder pro Kanal – alle Bilder einmal verkleinert vorbereitet (die Datei bleibt frei).</summary>
    internal static partial class ChannelImages
    {
        /// <summary>Bewegte GIFs abspielen (OPTIONEN → Aussehen).</summary>
        public static bool GifOn;

        internal sealed class Anim
        {
            public Bitmap[] Frames;
            public double[] Ends;   // Ende jedes Bilds in Sekunden
            public double Total;
        }

        private static readonly Dictionary<string, Anim> _anims = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
        private const int MaxFrames = 150, MaxSide = 240;

        /// <summary>Hat die Datei mehrere Bilder (bewegtes GIF)?</summary>
        public static bool IsAnimated(string path) => GetAnim(path) != null;

        private static Anim GetAnim(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return null;
            if (_anims.TryGetValue(path, out var a)) return a;
            a = null;
            try
            {
                using (var fs = System.IO.File.OpenRead(path))
                using (var img = Image.FromStream(fs))
                    a = Decode(img);
            }
            catch { a = null; }
            _anims[path] = a;
            return a;
        }

        /// <summary>Alle Bilder eines GIFs verkleinert herausholen (null = nur ein Bild).</summary>
        internal static Anim Decode(Image img) => Decode(img, MaxSide, MaxFrames);

        /// <summary>4.0 finale: mit eigener Groesse (z. B. bewegtes Hintergrundbild).</summary>
        internal static Anim Decode(Image img, int maxSide, int maxFrames)
        {
            var dim = new FrameDimension(img.FrameDimensionsList[0]);
            int count = img.GetFrameCount(dim);
            if (count < 2) return null;
            count = Math.Min(count, maxFrames);
            int[] delays = null;
            try
            {
                var prop = img.GetPropertyItem(0x5100);   // FrameDelay (1/100 s je Bild)
                delays = new int[prop.Value.Length / 4];
                for (int i = 0; i < delays.Length; i++) delays[i] = BitConverter.ToInt32(prop.Value, i * 4);
            }
            catch { }
            float k = Math.Min(1f, maxSide / (float)Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
            var a = new Anim { Frames = new Bitmap[count], Ends = new double[count] };
            double t = 0;
            for (int i = 0; i < count; i++)
            {
                img.SelectActiveFrame(dim, i);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(img, new Rectangle(0, 0, w, h));
                }
                a.Frames[i] = bmp;
                int d = delays != null && i < delays.Length ? delays[i] : 10;
                if (d < 2) d = 10;   // wie die Browser: 0/1 = 0,1 s
                t += d / 100.0;
                a.Ends[i] = t;
            }
            a.Total = t;
            return a;
        }

        /// <summary>Bildnummer zur Zeit t (Sekunden, laeuft im Kreis).</summary>
        public static int FrameIndex(string path, double t)
        {
            var a = GetAnim(path);
            if (a == null || a.Total <= 0) return 0;
            double u = t % a.Total;
            for (int i = 0; i < a.Ends.Length; i++) if (u < a.Ends[i]) return i;
            return a.Ends.Length - 1;
        }

        public static Bitmap Frame(string path, int index)
        {
            var a = GetAnim(path);
            if (a == null) return Get(path);
            return a.Frames[Math.Max(0, Math.Min(a.Frames.Length - 1, index))];
        }

        public static void ForgetAnim(string path)
        {
            if (string.IsNullOrEmpty(path) || !_anims.TryGetValue(path, out var a)) return;
            _anims.Remove(path);
            if (a?.Frames != null) foreach (var b in a.Frames) b?.Dispose();
        }

        private static readonly System.Diagnostics.Stopwatch GifClock = System.Diagnostics.Stopwatch.StartNew();
        public static double GifTime => GifClock.Elapsed.TotalSeconds;
    }
}
