using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>4.2: Spiegelung im Tisch, RGB-Welle (Rand), App-Symbol auf der Faderkappe.</summary>
    internal abstract partial class StripBase
    {
        /// <summary>Ueber jeden Zug zeichnen (RGB-Welle) – null = nichts.</summary>
        public static Action<StripBase, Graphics> WaveOverlay;
        /// <summary>Zaehlt hoch, wenn das Bild des Zugs neu gezeichnet wurde.</summary>
        internal int ChromeVer420;
        private Bitmap _refl420;
        private int _reflVer420 = -1, _reflH420;

        /// <summary>Flaeche des Zugs (fuer Rand-Effekte von aussen).</summary>
        internal RectangleF PanelRect420 => PanelRect;

        /// <summary>Unterer Teil des Zugs, gespiegelt und nach unten ausgeblendet (fuer den Tisch). null = noch nicht bereit.</summary>
        internal Bitmap Reflection420(int h)
        {
            if (h < 4 || !_chrome.Ready || PanelRect.Width < 4) return null;
            if (_refl420 != null && _reflVer420 == ChromeVer420 && _reflH420 == h) return _refl420;
            var pr = Rectangle.Round(PanelRect);
            int bottom = Math.Min(Height, pr.Bottom), hh = Math.Min(h, bottom - pr.Top);
            if (hh < 2) return null;
            Bitmap crop = null;
            try { crop = _chrome.Crop(new Rectangle(pr.X, bottom - hh, pr.Width, hh)); } catch { }
            if (crop == null) return null;
            var outb = new Bitmap(crop.Width, h, PixelFormat.Format32bppArgb);
            try
            {
                var sd = crop.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var od = outb.LockBits(new Rectangle(0, 0, outb.Width, outb.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int w = crop.Width;
                    var row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        int sy = hh - 1 - y;
                        float f = 1f - y / (float)h;
                        int a = sy < 0 ? 0 : (int)(120 * f * f);
                        if (a > 0) Marshal.Copy(sd.Scan0 + sy * sd.Stride, row, 0, w);
                        for (int x = 0; x < w; x++) row[x] = a == 0 ? 0 : (row[x] & 0x00ffffff) | (a << 24);
                        Marshal.Copy(row, 0, od.Scan0 + y * od.Stride, w);
                    }
                }
                finally { crop.UnlockBits(sd); outb.UnlockBits(od); }
            }
            catch { outb.Dispose(); crop.Dispose(); return null; }
            crop.Dispose();
            _refl420?.Dispose();
            _refl420 = outb; _reflVer420 = ChromeVer420; _reflH420 = h;
            return outb;
        }

        /// <summary>Waagerechte Lage der Spiegelung im Zug.</summary>
        internal int ReflectionX420 => (int)Math.Round(PanelRect.X);

        /// <summary>Nur die Raender des Zugs neu zeichnen (RGB-Welle).</summary>
        internal void InvalidateEdges420()
        {
            var pr = Rectangle.Round(PanelRect);
            int e = Math.Max(4, Theme.Px(6));
            Invalidate(new Rectangle(pr.X - 2, pr.Y - 2, pr.Width + 4, e + 2));
            Invalidate(new Rectangle(pr.X - 2, pr.Bottom - e, pr.Width + 4, e + 2));
            Invalidate(new Rectangle(pr.X - 2, pr.Y, e + 2, pr.Height));
            Invalidate(new Rectangle(pr.Right - e, pr.Y, e + 2, pr.Height));
        }

        /// <summary>Nach der Faderkappe: z. B. das App-Symbol (ChannelStrip).</summary>
        protected virtual void DrawCapExtra420(Graphics g, RectangleF cap) { }
    }

    internal sealed partial class ChannelStrip
    {
        /// <summary>4.2: Symbol der App, die gerade spielt, klein auf der Faderkappe.</summary>
        public static bool CapIcon;

        protected override void DrawCapExtra420(Graphics g, RectangleF cap)
        {
            if (!CapIcon || StreamLayer || Apps == null || Apps.Count == 0) return;
            AppView pick = null;
            foreach (var a in Apps) if (a.Active) { pick = a; break; }
            if (pick == null) foreach (var a in Apps) if (a.Running) { pick = a; break; }
            if (pick == null) pick = Apps[0];
            IconCache.Entry e;
            try { e = Mixer.Icons.Get(pick.Key, pick.Name, pick.Path); } catch { return; }
            var img = pick.Active ? e?.Normal : e?.Dim;
            if (img == null) return;
            float s = Math.Max(8f, Math.Min(cap.Height - Theme.F(4), cap.Width * 0.5f));
            var r = new RectangleF(cap.X + (cap.Width - s) / 2f, cap.Y + (cap.Height - s) / 2f, s, s);
            var sm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillEllipse(b, RectangleF.Inflate(r, Theme.F(1.5f), Theme.F(1.5f)));
            var im = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, r);
            g.InterpolationMode = im;
            g.SmoothingMode = sm;
        }
    }
}
