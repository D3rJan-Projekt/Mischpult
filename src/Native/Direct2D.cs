using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

// 3.0: Zeichnen ueber die Grafikkarte mit Direct2D (d2d1.dll, ab Windows 7). Nur das Noetigste.
// Reihenfolge der Methoden wie in d2d1.h – nicht benutzte Methoden sind Platzhalter (nur ihre Position in der Tabelle zaehlt).

namespace Mischpult.Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct D2DColor { public float R, G, B, A; public D2DColor(Color c) { R = c.R / 255f; G = c.G / 255f; B = c.B / 255f; A = c.A / 255f; } }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DPoint { public float X, Y; public D2DPoint(float x, float y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DRect { public float Left, Top, Right, Bottom; public D2DRect(RectangleF r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; } }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DSizeU { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DRectU { public uint Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DEllipse { public D2DPoint Center; public float RadiusX, RadiusY; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DRoundedRect { public D2DRect Rect; public float RadiusX, RadiusY; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DPixelFormat { public int Format, AlphaMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DRenderTargetProps { public int Type; public D2DPixelFormat PixelFormat; public float DpiX, DpiY; public int Usage, MinLevel; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DHwndProps { public IntPtr Hwnd; public D2DSizeU PixelSize; public int PresentOptions; }
    [StructLayout(LayoutKind.Sequential)] internal struct D2DBitmapProps { public D2DPixelFormat PixelFormat; public float DpiX, DpiY; }

    [ComImport, Guid("06152247-6f50-465a-9245-118bfd3b6007"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ID2D1Factory
    {
        [PreserveSig] int ReloadSystemMetrics();
        [PreserveSig] void GetDesktopDpi(out float x, out float y);
        [PreserveSig] int CreateRectangleGeometry();
        [PreserveSig] int CreateRoundedRectangleGeometry();
        [PreserveSig] int CreateEllipseGeometry();
        [PreserveSig] int CreateGeometryGroup();
        [PreserveSig] int CreateTransformedGeometry();
        [PreserveSig] int CreatePathGeometry();
        [PreserveSig] int CreateStrokeStyle();
        [PreserveSig] int CreateDrawingStateBlock();
        [PreserveSig] int CreateWicBitmapRenderTarget();
        [PreserveSig] int CreateHwndRenderTarget(ref D2DRenderTargetProps props, ref D2DHwndProps hwndProps, out ID2D1HwndRenderTarget target);
        [PreserveSig] int CreateDxgiSurfaceRenderTarget();
        [PreserveSig] int CreateDCRenderTarget();
    }

    [ComImport, Guid("2cd90698-12e2-11dc-9fed-001143a055f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ID2D1HwndRenderTarget
    {
        // ID2D1Resource
        [PreserveSig] void GetFactory(out IntPtr factory);
        // ID2D1RenderTarget
        [PreserveSig] int CreateBitmap(D2DSizeU size, IntPtr srcData, uint pitch, ref D2DBitmapProps props, out ID2D1Bitmap bitmap);
        [PreserveSig] int CreateBitmapFromWicBitmap();
        [PreserveSig] int CreateSharedBitmap();
        [PreserveSig] int CreateBitmapBrush();
        [PreserveSig] int CreateSolidColorBrush(ref D2DColor color, IntPtr brushProps, out ID2D1SolidColorBrush brush);
        [PreserveSig] int CreateGradientStopCollection();
        [PreserveSig] int CreateLinearGradientBrush();
        [PreserveSig] int CreateRadialGradientBrush();
        [PreserveSig] int CreateCompatibleRenderTarget();
        [PreserveSig] int CreateLayer();
        [PreserveSig] int CreateMesh();
        [PreserveSig] void DrawLine(D2DPoint p0, D2DPoint p1, ID2D1SolidColorBrush brush, float strokeWidth, IntPtr strokeStyle);
        [PreserveSig] void DrawRectangle(ref D2DRect rect, ID2D1SolidColorBrush brush, float strokeWidth, IntPtr strokeStyle);
        [PreserveSig] void FillRectangle(ref D2DRect rect, ID2D1SolidColorBrush brush);
        [PreserveSig] void DrawRoundedRectangle(ref D2DRoundedRect rect, ID2D1SolidColorBrush brush, float strokeWidth, IntPtr strokeStyle);
        [PreserveSig] void FillRoundedRectangle(ref D2DRoundedRect rect, ID2D1SolidColorBrush brush);
        [PreserveSig] void DrawEllipse(ref D2DEllipse ellipse, ID2D1SolidColorBrush brush, float strokeWidth, IntPtr strokeStyle);
        [PreserveSig] void FillEllipse(ref D2DEllipse ellipse, ID2D1SolidColorBrush brush);
        [PreserveSig] void DrawGeometry();
        [PreserveSig] void FillGeometry();
        [PreserveSig] void FillMesh();
        [PreserveSig] void FillOpacityMask();
        [PreserveSig] void DrawBitmap(ID2D1Bitmap bitmap, ref D2DRect dest, float opacity, int interpolation, ref D2DRect src);
        [PreserveSig] void DrawText();
        [PreserveSig] void DrawTextLayout();
        [PreserveSig] void DrawGlyphRun();
        [PreserveSig] void SetTransform();
        [PreserveSig] void GetTransform();
        [PreserveSig] void SetAntialiasMode(int mode);
        [PreserveSig] int GetAntialiasMode();
        [PreserveSig] void SetTextAntialiasMode();
        [PreserveSig] int GetTextAntialiasMode();
        [PreserveSig] void SetTextRenderingParams();
        [PreserveSig] void GetTextRenderingParams();
        [PreserveSig] void SetTags();
        [PreserveSig] void GetTags();
        [PreserveSig] void PushLayer();
        [PreserveSig] void PopLayer();
        [PreserveSig] int Flush(IntPtr tag1, IntPtr tag2);
        [PreserveSig] void SaveDrawingState();
        [PreserveSig] void RestoreDrawingState();
        [PreserveSig] void PushAxisAlignedClip(ref D2DRect clip, int antialias);
        [PreserveSig] void PopAxisAlignedClip();
        [PreserveSig] void Clear(ref D2DColor color);
        [PreserveSig] void BeginDraw();
        [PreserveSig] int EndDraw(IntPtr tag1, IntPtr tag2);
        [PreserveSig] void GetPixelFormat();
        [PreserveSig] void SetDpi(float x, float y);
        [PreserveSig] void GetDpi(out float x, out float y);
        [PreserveSig] void GetSize();
        [PreserveSig] void GetPixelSize();
        [PreserveSig] uint GetMaximumBitmapSize();
        [PreserveSig] int IsSupported();
        // ID2D1HwndRenderTarget
        [PreserveSig] int CheckWindowState();
        [PreserveSig] int Resize(ref D2DSizeU size);
        [PreserveSig] IntPtr GetHwnd();
    }

    [ComImport, Guid("2cd906a9-12e2-11dc-9fed-001143a055f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ID2D1SolidColorBrush
    {
        [PreserveSig] void GetFactory(out IntPtr factory);
        [PreserveSig] void SetOpacity(float opacity);
        [PreserveSig] void SetTransform();
        [PreserveSig] float GetOpacity();
        [PreserveSig] void GetTransform();
        [PreserveSig] void SetColor(ref D2DColor color);
        [PreserveSig] void GetColor();
    }

    [ComImport, Guid("a2296057-ea42-4099-983b-539fb6505426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ID2D1Bitmap
    {
        [PreserveSig] void GetFactory(out IntPtr factory);
        [PreserveSig] void GetSize();
        [PreserveSig] void GetPixelSize();
        [PreserveSig] void GetPixelFormat();
        [PreserveSig] void GetDpi(out float x, out float y);
        [PreserveSig] int CopyFromBitmap();
        [PreserveSig] int CopyFromRenderTarget();
        [PreserveSig] int CopyFromMemory(ref D2DRectU dstRect, IntPtr srcData, uint pitch);
    }

    internal static class D2DNative
    {
        [DllImport("d2d1.dll", ExactSpelling = true)]
        public static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr options, out ID2D1Factory factory);

        public const int RecreateTarget = unchecked((int)0x8899000C);
        public const int FormatB8G8R8A8 = 87, AlphaPremultiplied = 1, AlphaIgnore = 3;
    }

    /// <summary>Ein Bild auf der Grafikkarte (geht beim Neuanlegen der Zeichenflaeche verloren).</summary>
    internal sealed class D2DImage : IDisposable
    {
        internal ID2D1Bitmap Bitmap;
        public readonly int Width, Height;
        internal readonly int Generation;
        internal D2DImage(ID2D1Bitmap b, int w, int h, int gen) { Bitmap = b; Width = w; Height = h; Generation = gen; }
        public void Dispose() { var b = Bitmap; Bitmap = null; if (b != null) try { Marshal.ReleaseComObject(b); } catch { } }
    }

    /// <summary>
    /// 3.0: Zeichenflaeche auf der Grafikkarte fuer ein Steuerelement (Direct2D, Fenster-Ziel). Jede Methode faengt Fehler ab:
    /// geht etwas schief, liefert Begin/End false und der Aufrufer zeichnet wieder wie bisher (GDI+).
    /// </summary>
    internal sealed class D2DCanvas : IDisposable
    {
        private static ID2D1Factory _factory;
        private static bool _factoryFailed;
        private readonly Control _owner;
        private ID2D1HwndRenderTarget _rt;
        private ID2D1SolidColorBrush _brush;
        private D2DSizeU _size;
        private bool _drawing;
        /// <summary>Zaehlt hoch, wenn die Zeichenflaeche neu angelegt wurde (Bilder neu hochladen).</summary>
        public int Generation { get; private set; }
        public string Error { get; private set; }

        public static bool Available => Win32.IsWindows && !_factoryFailed;

        public D2DCanvas(Control owner) { _owner = owner; }

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private bool EnsureTarget(int w, int h)
        {
            try
            {
                if (_factory == null)
                {
                    if (_factoryFailed) return false;
                    var iid = typeof(ID2D1Factory).GUID;
                    int hr = D2DNative.D2D1CreateFactory(0, ref iid, IntPtr.Zero, out _factory);
                    if (hr < 0 || _factory == null) { _factoryFailed = true; Error = "Direct2D nicht verfügbar (0x" + hr.ToString("X8") + ")"; return false; }
                }
                var size = new D2DSizeU { Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h) };
                if (_rt == null)
                {
                    var props = new D2DRenderTargetProps { DpiX = 96f, DpiY = 96f };
                    var hp = new D2DHwndProps { Hwnd = _owner.Handle, PixelSize = size, PresentOptions = 2 /* sofort, nicht auf den Bildaufbau warten */ };
                    int hr = _factory.CreateHwndRenderTarget(ref props, ref hp, out _rt);
                    if (hr < 0 || _rt == null) { Error = "Zeichenfläche auf der Grafikkarte geht nicht (0x" + hr.ToString("X8") + ")"; _rt = null; return false; }
                    var c = new D2DColor(Color.White);
                    hr = _rt.CreateSolidColorBrush(ref c, IntPtr.Zero, out _brush);
                    if (hr < 0 || _brush == null) { Error = "Pinsel (0x" + hr.ToString("X8") + ")"; ReleaseTarget(); return false; }
                    _rt.SetAntialiasMode(0);   // weiche Kanten
                    _size = size;
                    Generation++;
                }
                else if (size.Width != _size.Width || size.Height != _size.Height)
                {
                    int hr = _rt.Resize(ref size);
                    if (hr < 0) { ReleaseTarget(); return EnsureTarget(w, h); }
                    _size = size;
                }
                return true;
            }
            catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; ReleaseTarget(); return false; }
        }

        private void ReleaseTarget()
        {
            var b = _brush; _brush = null;
            var t = _rt; _rt = null;
            if (b != null) try { Marshal.ReleaseComObject(b); } catch { }
            if (t != null) try { Marshal.ReleaseComObject(t); } catch { }
        }

        /// <summary>Bild beginnen (Groesse in Pixeln). false = Grafikkarte geht nicht – wie bisher zeichnen.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public bool Begin(int w, int h)
        {
            if (!EnsureTarget(w, h)) return false;
            try { _rt.BeginDraw(); _drawing = true; return true; }
            catch (Exception ex) { Error = ex.Message; ReleaseTarget(); return false; }
        }

        /// <summary>Bild fertig. false = Fehler (dann wie bisher zeichnen). Ist die Grafikkarte "verloren", wird die Flaeche beim naechsten Mal neu angelegt.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public bool End()
        {
            if (!_drawing || _rt == null) return false;
            _drawing = false;
            try
            {
                int hr = _rt.EndDraw(IntPtr.Zero, IntPtr.Zero);
                if (hr == D2DNative.RecreateTarget) { ReleaseTarget(); return true; }
                if (hr < 0) { Error = "EndDraw 0x" + hr.ToString("X8"); return false; }
                return true;
            }
            catch (Exception ex) { Error = ex.Message; ReleaseTarget(); return false; }
        }

        private void SetBrush(Color c) { var d = new D2DColor(c); _brush.SetColor(ref d); }

        public void Clear(Color c) { var d = new D2DColor(c); _rt.Clear(ref d); }

        public void FillRect(RectangleF r, Color c) { if (r.Width <= 0 || r.Height <= 0) return; SetBrush(c); var d = new D2DRect(r); _rt.FillRectangle(ref d, _brush); }

        public void FillRoundRect(RectangleF r, float rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SetBrush(c);
            var d = new D2DRoundedRect { Rect = new D2DRect(r), RadiusX = rad, RadiusY = rad };
            _rt.FillRoundedRectangle(ref d, _brush);
        }

        public void FillEllipse(RectangleF r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SetBrush(c);
            var d = new D2DEllipse { Center = new D2DPoint(r.X + r.Width / 2f, r.Y + r.Height / 2f), RadiusX = r.Width / 2f, RadiusY = r.Height / 2f };
            _rt.FillEllipse(ref d, _brush);
        }

        public void Line(PointF a, PointF b, Color c, float width)
        {
            SetBrush(c);
            _rt.DrawLine(new D2DPoint(a.X, a.Y), new D2DPoint(b.X, b.Y), _brush, width, IntPtr.Zero);
        }

        public void PushClip(RectangleF r) { var d = new D2DRect(r); _rt.PushAxisAlignedClip(ref d, 1); }
        public void PopClip() => _rt.PopAxisAlignedClip();

        /// <summary>Bild zeichnen (Pixel 1:1 bzw. auf dest gestreckt), optional nur der Ausschnitt src.</summary>
        public void Image(D2DImage img, RectangleF dest, RectangleF? src = null, float opacity = 1f)
        {
            if (img?.Bitmap == null || img.Generation != Generation) return;
            var d = new D2DRect(dest);
            var s = new D2DRect(src ?? new RectangleF(0, 0, img.Width, img.Height));
            _rt.DrawBitmap(img.Bitmap, ref d, opacity, 0 /* naechster Nachbar: scharf */, ref s);
        }

        /// <summary>System.Drawing-Bild hochladen (vormultipliertes ARGB). null bei Fehler.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public D2DImage Upload(Bitmap bmp)
        {
            if (_rt == null || bmp == null) return null;
            try
            {
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var props = new D2DBitmapProps { PixelFormat = new D2DPixelFormat { Format = D2DNative.FormatB8G8R8A8, AlphaMode = D2DNative.AlphaPremultiplied }, DpiX = 96f, DpiY = 96f };
                    var size = new D2DSizeU { Width = (uint)bmp.Width, Height = (uint)bmp.Height };
                    int hr = _rt.CreateBitmap(size, data.Scan0, (uint)data.Stride, ref props, out ID2D1Bitmap b);
                    if (hr < 0 || b == null) { Error = "Bild hochladen 0x" + hr.ToString("X8"); return null; }
                    return new D2DImage(b, bmp.Width, bmp.Height, Generation);
                }
                finally { bmp.UnlockBits(data); }
            }
            catch (Exception ex) { Error = ex.Message; return null; }
        }

        /// <summary>Leeres Bild (z. B. fuer den Wasserfall), danach mit UpdateRows fuellen.</summary>
        public D2DImage Create(int w, int h)
        {
            using (var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb)) return Upload(bmp);
        }

        /// <summary>Zeilen ins Bild kopieren (px = deckende ARGB-Pixel, Zeilenlaenge = Breite des Bilds).</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public bool UpdateRows(D2DImage img, int[] px, int srcRow, int dstRow, int rows)
        {
            if (img?.Bitmap == null || rows <= 0) return false;
            var h = GCHandle.Alloc(px, GCHandleType.Pinned);
            try
            {
                var rect = new D2DRectU { Left = 0, Top = (uint)dstRow, Right = (uint)img.Width, Bottom = (uint)(dstRow + rows) };
                IntPtr p = h.AddrOfPinnedObject() + srcRow * img.Width * 4;
                return img.Bitmap.CopyFromMemory(ref rect, p, (uint)(img.Width * 4)) >= 0;
            }
            catch (Exception ex) { Error = ex.Message; return false; }
            finally { h.Free(); }
        }

        public void Dispose() => ReleaseTarget();
    }
}
