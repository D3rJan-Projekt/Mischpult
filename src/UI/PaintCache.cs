using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// Zwischenspeicher fuer die festen Teile eines Zugs (Schilder, Tasten, Skalen …).
    /// Unter Windows eine echte Windows-Zeichenflaeche (DIB mit eigenem DC) – genau wie der Doppelpuffer von WinForms,
    /// in den auch live gezeichnet wird. Nur so mischt die Windows-Schrift (ClearType) ihre Kanten richtig mit dem Hintergrund:
    /// in einem GDI+-Bitmap wurde dunkle Schrift schwarz und klobig und helle bekam bunte Raender (2.10.3).
    /// Sonst (Mono/Linux) ein normales Bitmap.
    /// </summary>
    internal sealed class PaintCache : IDisposable
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        private Bitmap _bmp;
        private IntPtr _dc, _dib, _old, _bits;
        private Bitmap _view;      // GDI+-Sicht auf dieselben Pixel (nur Notweg beim Kopieren und fuer die Vorschau)
        private bool _disposed;

        /// <summary>Echte Windows-Zeichenflaeche (sonst Bitmap)?</summary>
        public bool IsNative => _dc != IntPtr.Zero;

        /// <summary>Nur Test: Windows-Zeichenflaeche abschalten.</summary>
        public static bool AllowNative = true;

        public bool Fits(int w, int h) => w == Width && h == Height && (_bmp != null || _dc != IntPtr.Zero);

        /// <summary>Groesse sicherstellen. true = neu angelegt (Inhalt leer).</summary>
        public bool Ensure(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (_disposed || Fits(w, h)) return false;
            Free();
            Width = w; Height = h;
            if (AllowNative && Native.Win32.IsWindows && CreateNative(w, h)) return true;
            _bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            return true;
        }

        /// <summary>Hineinzeichnen. Im Bitmap-Notfall ohne Windows-Schrift (die wuerde dort falsch gemischt).</summary>
        public void Render(Action<Graphics> draw)
        {
            if (_disposed) return;
            if (_dc != IntPtr.Zero)
            {
                using (var g = Graphics.FromHdc(_dc)) draw(g);
                GdiFlush();
                return;
            }
            if (_bmp == null) return;
            Theme.NoGdiText++;
            try { using (var g = Graphics.FromImage(_bmp)) draw(g); }
            finally { Theme.NoGdiText--; }
        }

        /// <summary>
        /// Ausschnitt r (gleiche Koordinaten hier und dort) auf die Zielflaeche kopieren.
        /// Gedacht fuer OnPaint (r = ClipRectangle): BitBlt beachtet nur eine Verschiebung, keinen GDI+-Ausschnitt.
        /// </summary>
        public void Draw(Graphics target, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Width, Height));
            if (r.Width <= 0 || r.Height <= 0) return;
            if (_dc != IntPtr.Zero)
            {
                bool plain; int ox, oy;
                using (var m = target.Transform)
                {
                    var el = m.Elements;
                    plain = Math.Abs(el[0] - 1f) < 1e-5f && Math.Abs(el[1]) < 1e-5f && Math.Abs(el[2]) < 1e-5f && Math.Abs(el[3] - 1f) < 1e-5f;
                    ox = (int)Math.Round(el[4]); oy = (int)Math.Round(el[5]);
                }
                if (plain)
                {
                    IntPtr hdc = IntPtr.Zero;
                    bool ok = false;
                    try
                    {
                        hdc = target.GetHdc();   // liefert die Flaeche ohne Verschiebung – die setzen wir selbst
                        ok = BitBlt(hdc, r.X + ox, r.Y + oy, r.Width, r.Height, _dc, r.X, r.Y, SRCCOPY);
                    }
                    catch { }
                    finally { if (hdc != IntPtr.Zero) target.ReleaseHdc(hdc); }
                    if (ok) return;
                }
                // Notweg (gedreht/skaliert oder BitBlt ging nicht): dieselben Pixel ueber GDI+
                if (_view != null) DrawBitmap(target, _view, r);
                return;
            }
            if (_bmp != null) DrawBitmap(target, _bmp, r);
        }

        /// <summary>Bereit (angelegt und gezeichnet werden kann)?</summary>
        public bool Ready => !_disposed && (_bmp != null || _dc != IntPtr.Zero);

        /// <summary>
        /// Mehrere Ausschnitte auf einmal kopieren (gleiche Koordinaten) – fuer leuchtende LEDs aus dem Bildspeicher.
        /// Ein Windows-Zugriff fuer alle. Achtung: der GDI+-Ausschnitt (SetClip) gilt dabei nicht, darum wird jeder
        /// Ausschnitt auf <paramref name="clip"/> beschraenkt.
        /// </summary>
        public void DrawRects(Graphics target, Rectangle[] rects, int count, Rectangle clip)
        {
            if (_disposed || count <= 0) return;
            var bounds = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), clip);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (_dc != IntPtr.Zero)
            {
                bool plain; int ox, oy;
                using (var m = target.Transform)
                {
                    var el = m.Elements;
                    plain = Math.Abs(el[0] - 1f) < 1e-5f && Math.Abs(el[1]) < 1e-5f && Math.Abs(el[2]) < 1e-5f && Math.Abs(el[3] - 1f) < 1e-5f;
                    ox = (int)Math.Round(el[4]); oy = (int)Math.Round(el[5]);
                }
                if (plain)
                {
                    IntPtr hdc = IntPtr.Zero;
                    bool ok = true;
                    try
                    {
                        hdc = target.GetHdc();
                        for (int i = 0; i < count; i++)
                        {
                            var r = Rectangle.Intersect(rects[i], bounds);
                            if (r.Width <= 0 || r.Height <= 0) continue;
                            ok &= BitBlt(hdc, r.X + ox, r.Y + oy, r.Width, r.Height, _dc, r.X, r.Y, SRCCOPY);
                        }
                    }
                    catch { ok = false; }
                    finally { if (hdc != IntPtr.Zero) target.ReleaseHdc(hdc); }
                    if (ok) return;
                }
                if (_view == null) return;
                for (int i = 0; i < count; i++) { var r = Rectangle.Intersect(rects[i], bounds); if (r.Width > 0 && r.Height > 0) DrawBitmap(target, _view, r); }
                return;
            }
            if (_bmp == null) return;
            for (int i = 0; i < count; i++) { var r = Rectangle.Intersect(rects[i], bounds); if (r.Width > 0 && r.Height > 0) DrawBitmap(target, _bmp, r); }
        }

        private static void DrawBitmap(Graphics target, Bitmap b, Rectangle r)
        {
            var im = target.InterpolationMode; var pom = target.PixelOffsetMode;
            target.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            target.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
            try { target.DrawImage(b, r, r, GraphicsUnit.Pixel); } catch { }
            target.InterpolationMode = im; target.PixelOffsetMode = pom;
        }

        /// <summary>4.2: Ausschnitt als eigenes Bild (Kopie), z. B. fuer die Spiegelung im Tisch. null = nicht bereit.</summary>
        public Bitmap Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Width, Height));
            var src = _bmp ?? _view;
            if (src == null || r.Width <= 0 || r.Height <= 0) return null;
            if (src == _view) GdiFlush();
            var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            return b;
        }

        /// <summary>Nur Test/Vorschau: Inhalt als Bitmap (Kopie).</summary>
        public Bitmap Snapshot()
        {
            var src = _bmp ?? _view;
            if (src == null) return null;
            if (src == _view) GdiFlush();
            var b = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(b)) g.DrawImageUnscaled(src, 0, 0);
            return b;
        }

        private bool CreateNative(int w, int h)
        {
            try
            {
                _dc = CreateCompatibleDC(IntPtr.Zero);
                if (_dc == IntPtr.Zero) return false;
                var bi = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = w, biHeight = -h,   // von oben nach unten
                    biPlanes = 1, biBitCount = 32, biCompression = 0,
                };
                _dib = CreateDIBSection(_dc, ref bi, 0, out _bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero || _bits == IntPtr.Zero) { FreeNative(); return false; }
                _old = SelectObject(_dc, _dib);
                if (_old == IntPtr.Zero) { FreeNative(); return false; }   // sonst wuerde in ein 1x1-Bild gezeichnet
                _view = new Bitmap(w, h, w * 4, PixelFormat.Format32bppRgb, _bits);   // von oben nach unten: positiver Zeilenabstand
                return true;
            }
            catch
            {
                FreeNative();
                return false;
            }
        }

        private void FreeNative(bool finalizing = false)
        {
            if (!finalizing) { try { _view?.Dispose(); } catch { } }
            _view = null;
            try
            {
                if (_dc != IntPtr.Zero && _old != IntPtr.Zero) SelectObject(_dc, _old);
                if (_dib != IntPtr.Zero) DeleteObject(_dib);
                if (_dc != IntPtr.Zero) DeleteDC(_dc);
            }
            catch { }
            _dc = _dib = _old = _bits = IntPtr.Zero;
        }

        private void Free()
        {
            FreeNative();
            _bmp?.Dispose();
            _bmp = null;
            Width = Height = 0;
        }

        public void Dispose()
        {
            _disposed = true;
            Free();
            GC.SuppressFinalize(this);
        }

        ~PaintCache() { FreeNative(true); }

        // ------------------------------------------------------------ Windows
        private const int SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    }
}
