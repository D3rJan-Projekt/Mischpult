using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>2.16: eigene Bilder der Kanaele (verkleinert zwischengespeichert, die Datei bleibt frei).</summary>
    internal static partial class ChannelImages
    {
        private static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bild laden (null = gibt es nicht / geht nicht).</summary>
        public static Bitmap Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_cache.TryGetValue(path, out var b)) return b;
            Bitmap bmp = null;
            try
            {
                using (var fs = System.IO.File.OpenRead(path))
                using (var img = Image.FromStream(fs))
                {
                    float k = Math.Min(1f, 480f / Math.Max(img.Width, img.Height));
                    int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                    bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, new Rectangle(0, 0, w, h));
                    }
                }
            }
            catch { bmp?.Dispose(); bmp = null; }
            _cache[path] = bmp;   // auch "geht nicht" merken (nicht jedes Bild neu versuchen)
            return bmp;
        }

        public static void Forget(string path)
        {
            ForgetAnim(path);   // 4.0
            if (string.IsNullOrEmpty(path) || !_cache.TryGetValue(path, out var b)) return;
            _cache.Remove(path);
            b?.Dispose();
        }

        /// <summary>Bild fuellend (zugeschnitten) in ein abgerundetes Feld, leicht abgedunkelt, damit Symbole darauf lesbar bleiben.</summary>
        public static void DrawCover(Graphics g, RectangleF r, Bitmap img, float rad, int darken = 70)
        {
            if (img == null || r.Width < 4 || r.Height < 4) return;
            var st = g.Save();
            using (var path = Theme.Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float k = Math.Max(r.Width / img.Width, r.Height / img.Height);
            float iw = img.Width * k, ih = img.Height * k;
            g.DrawImage(img, new RectangleF(r.X + (r.Width - iw) / 2f, r.Y + (r.Height - ih) / 2f, iw, ih));
            if (darken > 0)
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(darken / 3, 0, 0, 0), Color.FromArgb(darken, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(b, r);
            g.Restore(st);
        }
    }
}
