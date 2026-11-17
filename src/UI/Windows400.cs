using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    // ======================================================================== Grundfenster

    /// <summary>4.0: schlankes Fenster im Pult-Look – eigene Titelleiste (ziehen), Schliessen-Knopf, alles selbst gezeichnet.</summary>
    internal abstract class PultWin : Form
    {
        protected readonly string Title;
        private RectangleF _closeR;
        private bool _move;
        private Point _moveStart, _winStart;
        protected string Hover;
        public event Action<PultWin> Moved;
        protected virtual bool HasTitle => true;
        protected float TitleH => HasTitle ? Theme.F(30) : 0f;
        protected RectangleF Content => new RectangleF(Theme.F(10), TitleH + Theme.F(4), ClientSize.Width - Theme.F(20), ClientSize.Height - TitleH - Theme.F(12));

        protected PultWin(string title, int w, int h)
        {
            Title = title;
            Text = "Mischpult – " + title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(Theme.Px(w), Theme.Px(h));
            BackColor = Theme.MenuBg;
            KeyPreview = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape && HasTitle) Close(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            var full = new RectangleF(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var b = new LinearGradientBrush(new RectangleF(0, -1, full.Width, full.Height + 2), Theme.PanelTop, Theme.PanelBottom, LinearGradientMode.Vertical)) g.FillRectangle(b, full);
            if (HasTitle)
            {
                var tr = new RectangleF(0, 0, ClientSize.Width, TitleH);
                using (var b = new SolidBrush(Theme.Alpha(Theme.BarBg, 220))) g.FillRectangle(b, tr);
                using (var b = new SolidBrush(Theme.Text)) Theme.DrawSpaced(g, Title.ToUpperInvariant(), Theme.UiSemi(10f), Theme.Text, Theme.F(12), Theme.F(8), Theme.F(1.6f));
                _closeR = new RectangleF(ClientSize.Width - Theme.F(30), Theme.F(3), Theme.F(26), Theme.F(24));
                using (var pen = new Pen(Hover == "#close" ? Theme.MuteRed : Theme.TextDim, Math.Max(1.2f, Theme.F(1.5f))))
                {
                    float c = Theme.F(8);
                    g.DrawLine(pen, _closeR.X + c, _closeR.Y + c - Theme.F(1), _closeR.Right - c, _closeR.Bottom - c + Theme.F(1));
                    g.DrawLine(pen, _closeR.Right - c, _closeR.Y + c - Theme.F(1), _closeR.X + c, _closeR.Bottom - c + Theme.F(1));
                }
            }
            PaintContent(g, Content);
            Theme.StrokeRound(g, Theme.PanelEdge, Theme.F(1), RectangleF.Inflate(full, -0.5f, -0.5f), Theme.F(4));
        }

        protected abstract void PaintContent(Graphics g, RectangleF r);
        /// <summary>Klick in den Inhalt (true = erledigt).</summary>
        protected virtual bool ContentDown(MouseEventArgs e) => false;
        protected virtual void ContentMove(MouseEventArgs e) { }
        protected virtual void ContentUp(MouseEventArgs e) { }
        protected virtual string ContentZone(Point p) => null;
        /// <summary>Hier darf man das Fenster packen (ohne Titelleiste: ueberall, wo nichts ist).</summary>
        protected virtual bool CanGrab(Point p) => HasTitle ? p.Y < TitleH : ContentZone(p) == null;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (HasTitle && _closeR.Contains(e.Location) && e.Button == MouseButtons.Left) { Close(); return; }
            if (ContentDown(e)) return;
            if (e.Button == MouseButtons.Left && CanGrab(e.Location)) { _move = true; Capture = true; _moveStart = Cursor.Position; _winStart = Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_move) { var p = Cursor.Position; Location = new Point(_winStart.X + p.X - _moveStart.X, _winStart.Y + p.Y - _moveStart.Y); return; }
            string h = HasTitle && _closeR.Contains(e.Location) ? "#close" : ContentZone(e.Location);
            if (h != Hover) { Hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            ContentMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_move) { _move = false; Capture = false; Moved?.Invoke(this); return; }
            ContentUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (Hover != null) { Hover = null; Invalidate(); } }

        protected static void Label(Graphics g, string t, RectangleF r, Color c, float px, bool bold = false, StringFormat sf = null)
        {
            using (var b = new SolidBrush(c)) Theme.Str(g, t ?? "", bold ? Theme.Ui(px, FontStyle.Bold) : Theme.Ui(px), b, r, sf ?? Theme.Left);
        }

        /// <summary>Waagerechter Pegel (-60 … 0 dBFS).</summary>
        protected static void HBar(Graphics g, RectangleF r, float peak, Color c)
        {
            Theme.FillRound(g, Theme.Hole, r, Theme.F(2));
            float f = ChanWindow.MeterFrac(peak);
            if (f <= 0f) return;
            var fr = new RectangleF(r.X, r.Y, r.Width * f, r.Height);
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Theme.Darken(c, 0.25f), Theme.Lighten(c, 0.2f), LinearGradientMode.Horizontal))
                Theme.FillRound(g, b, fr, Theme.F(2));
        }

        protected static void MediaGlyph(Graphics g, RectangleF r, string id, Color c)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Math.Min(r.Width, r.Height) * 0.2f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c))
            using (var pen = new Pen(c, Math.Max(1.2f, s * 0.35f)))
            {
                switch (id)
                {
                    case "play":
                        g.FillPolygon(b, new[] { new PointF(cx - s * 1.25f, cy - s), new PointF(cx + s * 0.15f, cy), new PointF(cx - s * 1.25f, cy + s) });
                        g.FillRectangle(b, cx + s * 0.45f, cy - s, s * 0.38f, s * 2);
                        g.FillRectangle(b, cx + s * 1.05f, cy - s, s * 0.38f, s * 2);
                        break;
                    case "next":
                    case "prev":
                    {
                        float d = id == "next" ? 1f : -1f;
                        g.FillPolygon(b, new[] { new PointF(cx - d * s * 1.2f, cy - s * 0.9f), new PointF(cx, cy), new PointF(cx - d * s * 1.2f, cy + s * 0.9f) });
                        g.FillPolygon(b, new[] { new PointF(cx, cy - s * 0.9f), new PointF(cx + d * s * 1.2f, cy), new PointF(cx, cy + s * 0.9f) });
                        g.FillRectangle(b, cx + d * s * 1.2f - (d > 0 ? 0 : s * 0.32f), cy - s * 0.9f, s * 0.32f, s * 1.8f);
                        break;
                    }
                    case "seek-":
                    case "seek+":
                    {
                        float d = id == "seek+" ? 1f : -1f;
                        g.DrawArc(pen, cx - s * 1.1f, cy - s * 1.1f, s * 2.2f, s * 2.2f, d > 0 ? -60 : 240, d > 0 ? 300 : -300);
                        using (var f = new Font("Segoe UI", Math.Max(5f, s * 1.0f), FontStyle.Bold, GraphicsUnit.Pixel)) Theme.Str(g, "10", f, b, new RectangleF(cx - s * 1.5f, cy - s, s * 3f, s * 2f), Theme.Center);
                        break;
                    }
                    case "shuffle":
                        g.DrawBezier(pen, cx - s * 1.4f, cy - s * 0.8f, cx - s * 0.2f, cy - s * 0.8f, cx + s * 0.2f, cy + s * 0.8f, cx + s * 1.3f, cy + s * 0.8f);
                        g.DrawBezier(pen, cx - s * 1.4f, cy + s * 0.8f, cx - s * 0.2f, cy + s * 0.8f, cx + s * 0.2f, cy - s * 0.8f, cx + s * 1.3f, cy - s * 0.8f);
                        break;
                    case "repeat":
                        g.DrawRectangle(pen, cx - s * 1.2f, cy - s * 0.7f, s * 2.4f, s * 1.4f);
                        g.FillPolygon(b, new[] { new PointF(cx + s * 0.1f, cy - s * 1.1f), new PointF(cx + s * 0.6f, cy - s * 0.7f), new PointF(cx + s * 0.1f, cy - s * 0.3f) });
                        break;
                }
            }
            g.SmoothingMode = sm;
        }
    }

    // ======================================================================== 169 Spiel-Overlay

    /// <summary>Daten fuer Overlay, Widget und Mini-Player.</summary>
    internal sealed class OverlayData
    {
        public float Master; public bool MasterMute; public string Device;
        public List<MiniRow> Rows = new List<MiniRow>();
        public string Song; public bool Playing; public Bitmap Cover; public string App;
        public bool? Shuffle; public int Repeat = -1;
        public float SongFrac = -1f; public string SongTime;   // 4.0 finale: Song-Fortschritt (-1 = keiner)
    }

    /// <summary>
    /// Kleines Overlay in einer Bildschirmecke, waehrend ein Spiel vorne ist (Fenster- und randloser Vollbildmodus):
    /// Lautstaerke, die lautesten Kanaele und was gerade laeuft. Klicks gehen durch, es nimmt dem Spiel nie den Fokus.
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private OverlayData _d;
        private int _q = int.MinValue;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(0x0c, 0x0e, 0x12);
            Opacity = 0.88;
            ClientSize = new Size(Theme.Px(250), Theme.Px(80));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x00000008 | 0x08000000 | 0x00000080;   // LAYERED, TRANSPARENT (Klicks gehen durch), TOPMOST, NOACTIVATE, TOOLWINDOW
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        /// <summary>Hoehe fuer so viele Kanal-Zeilen.</summary>
        internal static int HeightFor(int rows, bool song) => Theme.Px(34 + rows * 18 + (song ? 20 : 0) + 8);

        public void SetData(OverlayData d, int corner)
        {
            _d = d;
            int h = HeightFor(d.Rows.Count, !string.IsNullOrEmpty(d.Song));
            var wa = Screen.PrimaryScreen.Bounds;
            int m = Theme.Px(16), w = ClientSize.Width;
            var p = corner == 1 ? new Point(wa.Right - w - m, wa.Top + m) : corner == 2 ? new Point(wa.Left + m, wa.Bottom - h - m) : corner == 3 ? new Point(wa.Right - w - m, wa.Bottom - h - m) : new Point(wa.Left + m, wa.Top + m);
            if (ClientSize.Height != h) ClientSize = new Size(w, h);
            if (Location != p) Location = p;
            int q = (int)(d.Master * 100) + (d.MasterMute ? 1000 : 0) + (d.Song?.GetHashCode() ?? 0) + (int)(d.SongFrac * 300) * 13 + d.Rows.Sum(r => (int)(ChanWindow.MeterFrac(Math.Max(r.PeakL, r.PeakR)) * 30) * 7 + (r.Name?.GetHashCode() ?? 0));
            if (q != _q) { _q = q; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            var d = _d;
            if (d == null) return;
            float x = Theme.F(10), w = ClientSize.Width - Theme.F(20), y = Theme.F(7);
            using (var b = new SolidBrush(Color.FromArgb(0xff, 0x8a, 0x3a))) Theme.Str(g, "MISCHPULT", Theme.UiSemi(8.5f), b, new RectangleF(x, y, w, Theme.F(12)), Theme.Left);
            using (var b = new SolidBrush(Color.FromArgb(0xc8, 0xcc, 0xd2))) Theme.Str(g, DateTime.Now.ToString("HH:mm"), Theme.UiSemi(8.5f), b, new RectangleF(x, y, w, Theme.F(12)), Theme.Right);
            y += Theme.F(14);
            // Ausgang
            using (var b = new SolidBrush(d.MasterMute ? Theme.MuteRed : Color.White))
                Theme.Str(g, d.MasterMute ? "STUMM" : (int)Math.Round(d.Master * 100) + " %", Theme.Ui(11f, FontStyle.Bold), b, new RectangleF(x, y, Theme.F(60), Theme.F(16)), Theme.Left);
            var vr = new RectangleF(x + Theme.F(62), y + Theme.F(5), w - Theme.F(62), Theme.F(6));
            Theme.FillRound(g, Color.FromArgb(0x26, 0x29, 0x2f), vr, Theme.F(3));
            Theme.FillRound(g, d.MasterMute ? Theme.Darken(Theme.MuteRed, 0.3f) : Theme.Accent, new RectangleF(vr.X, vr.Y, vr.Width * Math.Max(0f, Math.Min(1f, d.Master)), vr.Height), Theme.F(3));
            y += Theme.F(20);
            foreach (var r in d.Rows)
            {
                using (var b = new SolidBrush(r.Color)) g.FillEllipse(b, x, y + Theme.F(4), Theme.F(7), Theme.F(7));
                using (var b = new SolidBrush(r.Mute ? Color.FromArgb(0x80, 0x84, 0x8c) : Color.FromArgb(0xe8, 0xea, 0xee)))
                    Theme.Str(g, r.Name, Theme.Ui(9f, FontStyle.Bold), b, new RectangleF(x + Theme.F(11), y, Theme.F(80), Theme.F(15)), Theme.Left);
                var br = new RectangleF(x + Theme.F(94), y + Theme.F(4), w - Theme.F(94) - Theme.F(46), Theme.F(7));
                Theme.FillRound(g, Color.FromArgb(0x1a, 0x1c, 0x21), br, Theme.F(2));
                float f = ChanWindow.MeterFrac(Math.Max(r.PeakL, r.PeakR));
                if (f > 0f) Theme.FillRound(g, r.Mute ? Color.FromArgb(0x55, 0x58, 0x5e) : r.Color, new RectangleF(br.X, br.Y, br.Width * f, br.Height), Theme.F(2));
                using (var b = new SolidBrush(Color.FromArgb(0xa8, 0xad, 0xb5)))
                    Theme.Str(g, r.Mute ? "STUMM" : r.Value, Theme.Ui(8.5f), b, new RectangleF(br.Right + Theme.F(4), y, Theme.F(44), Theme.F(15)), Theme.Right);
                y += Theme.F(18);
            }
            if (!string.IsNullOrEmpty(d.Song))
                using (var b = new SolidBrush(Color.FromArgb(0xd0, 0xd4, 0xda)))
                    Theme.Str(g, (d.Playing ? "♪ " : "❚❚ ") + d.Song, Theme.Ui(9f), b, new RectangleF(x, y + Theme.F(2), w, Theme.F(15)), Theme.Left);
            if (!string.IsNullOrEmpty(d.Song) && d.SongFrac >= 0f)   // 4.0 finale: Song-Fortschritt
            {
                var pr = new RectangleF(x, y + Theme.F(17), w, Theme.F(2));
                using (var b = new SolidBrush(Color.FromArgb(0x30, 0x34, 0x3a))) g.FillRectangle(b, pr);
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, pr.X, pr.Y, pr.Width * d.SongFrac, pr.Height);
            }
        }
    }

    // ======================================================================== 176 Desktop-Widget

    /// <summary>
    /// Desktop-Widget: liegt auf dem Schreibtisch (hinter den Fenstern) – grosser Lautstaerke-Ring (ziehen/Mausrad),
    /// Klick in die Mitte = stumm, darunter was laeuft mit Musik-Tasten. Am Rand ziehen verschiebt es.
    /// </summary>
    internal sealed class WidgetForm : PultWin
    {
        private OverlayData _d;
        private int _q = int.MinValue;
        private RectangleF _ring, _hub, _prev, _play, _next;
        private bool _drag;
        private float _dragVirtual;
        private int _lastY;
        public event Action<float> VolumeChanged;
        public event Action MuteClicked;
        public event Action<string> Media;
        protected override bool HasTitle => false;

        public WidgetForm() : base("Widget", 190, 230) { Opacity = 0.96; }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080; return cp; }   // TOOLWINDOW (nicht bei Alt+Tab)
        }

        public void SetData(OverlayData d)
        {
            _d = d;
            int q = (int)(d.Master * 300) + (d.MasterMute ? 7 : 0) + (d.Song?.GetHashCode() ?? 0) + (d.Playing ? 3 : 0);
            if (q != _q) { _q = q; Invalidate(); }
        }

        protected override string ContentZone(Point p)
        {
            if (_hub.Contains(p)) return "mute";
            if (_ring.Contains(p)) return "ring";
            if (_prev.Contains(p)) return "prev";
            if (_play.Contains(p)) return "play";
            if (_next.Contains(p)) return "next";
            return null;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            var d = _d;
            float size = Math.Min(r.Width, Theme.F(140));
            _ring = new RectangleF(r.X + (r.Width - size) / 2f, r.Y + Theme.F(2), size, size);
            var c = new PointF(_ring.X + size / 2f, _ring.Y + size / 2f);
            float rad = size / 2f - Theme.F(14);
            _hub = new RectangleF(c.X - rad * 0.55f, c.Y - rad * 0.55f, rad * 1.1f, rad * 1.1f);
            float v = d?.Master ?? 0f;
            bool mute = d?.MasterMute ?? false;
            Theme.DrawKnob(g, c, rad, v, mute ? Theme.MuteRed : Theme.Accent, false, Hover == "ring" || _drag, 9f, 31);
            using (var b = new SolidBrush(mute ? Theme.MuteRed : Theme.Text))
                Theme.Str(g, mute ? "STUMM" : (int)Math.Round(v * 100) + " %", Theme.Ui(10.5f, FontStyle.Bold), b, new RectangleF(_ring.X, _ring.Bottom - Theme.F(4), _ring.Width, Theme.F(16)), Theme.Center);
            float by = _ring.Bottom + Theme.F(14);
            Label(g, string.IsNullOrEmpty(d?.Song) ? (d?.Device ?? "") : d.Song, new RectangleF(r.X, by, r.Width, Theme.F(16)), Theme.TextDim, 9f, false, Theme.Center);
            float bw = Theme.F(34), bh = Theme.F(24), bx = r.X + (r.Width - bw * 3 - Theme.F(8)) / 2f, y2 = by + Theme.F(18);
            _prev = new RectangleF(bx, y2, bw, bh); _play = new RectangleF(bx + bw + Theme.F(4), y2, bw, bh); _next = new RectangleF(bx + (bw + Theme.F(4)) * 2, y2, bw, bh);
            foreach (var kv in new[] { new KeyValuePair<string, RectangleF>("prev", _prev), new KeyValuePair<string, RectangleF>("play", _play), new KeyValuePair<string, RectangleF>("next", _next) })
            {
                Theme.DrawBarButton(g, kv.Value, Hover == kv.Key);
                MediaGlyph(g, kv.Value, kv.Key, Hover == kv.Key ? Theme.TextHover : Theme.Text);
            }
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            string z = ContentZone(e.Location);
            if (e.Button != MouseButtons.Left || z == null) return false;
            if (z == "mute") { MuteClicked?.Invoke(); return true; }
            if (z == "ring") { _drag = true; Capture = true; _dragVirtual = _d?.Master ?? 0f; _lastY = e.Y; return true; }
            Media?.Invoke(z);
            return true;
        }

        protected override void ContentMove(MouseEventArgs e)
        {
            if (!_drag) return;
            _dragVirtual = Math.Max(0f, Math.Min(1f, _dragVirtual - (e.Y - _lastY) / Theme.F(200)));
            _lastY = e.Y;
            if (_d != null) _d.Master = _dragVirtual;
            VolumeChanged?.Invoke(_dragVirtual);
            Invalidate();
        }

        protected override void ContentUp(MouseEventArgs e) { if (_drag) { _drag = false; Capture = false; } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_d == null) return;
            _d.Master = Math.Max(0f, Math.Min(1f, _d.Master + (e.Delta > 0 ? 0.02f : -0.02f)));
            VolumeChanged?.Invoke(_d.Master);
            Invalidate();
        }

        // auf den Schreibtisch: ganz nach hinten
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public void SendBack() { try { if (Native.Win32.IsWindows && IsHandleCreated) SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { } }   // HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SendBack(); }
    }

    // ======================================================================== 209 Mini-Player

    internal sealed class PlayerForm : PultWin
    {
        private OverlayData _d;
        private int _q = int.MinValue;
        private readonly Dictionary<string, RectangleF> _btn = new Dictionary<string, RectangleF>();
        public bool More;   // Spulen, Zufall, Wiederholen
        public event Action<string> Media;

        public PlayerForm() : base("Mini-Player", 380, 132) { }

        public void SetData(OverlayData d)
        {
            _d = d;
            int q = (d.Song?.GetHashCode() ?? 0) + (d.Playing ? 1 : 0) + (d.Cover?.GetHashCode() ?? 0) * 3 + (d.Shuffle == true ? 5 : 0) + d.Repeat * 11 + (int)(d.SongFrac * 500) * 17;
            if (q != _q) { _q = q; Invalidate(); }
        }

        protected override string ContentZone(Point p) { foreach (var kv in _btn) if (kv.Value.Contains(p)) return kv.Key; return null; }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            var d = _d;
            float cs = r.Height;
            var cover = new RectangleF(r.X, r.Y, cs, cs);
            if (d?.Cover != null) ChannelImages.DrawCover(g, cover, d.Cover, Theme.F(4), 0);
            else
            {
                Theme.FillRound(g, Theme.Well, cover, Theme.F(4));
                Label(g, "♪", cover, Theme.TextFaint, 26f, true, Theme.Center);
            }
            float tx = cover.Right + Theme.F(10), tw = r.Right - tx;
            string song = d?.Song;
            Label(g, string.IsNullOrEmpty(song) ? "Gerade läuft nichts" : song, new RectangleF(tx, r.Y, tw, Theme.F(20)), Theme.Text, 11f, true);
            Label(g, string.IsNullOrEmpty(song) ? "" : (d.Playing ? "spielt" : "pausiert") + (string.IsNullOrEmpty(d.App) ? "" : " · " + d.App), new RectangleF(tx, r.Y + Theme.F(20), tw, Theme.F(16)), Theme.TextDim, 9f);
            _songBar = RectangleF.Empty;
            if (d != null && !string.IsNullOrEmpty(song) && d.SongFrac >= 0f)   // 4.0 finale: Song-Fortschritt, Klick springt
            {
                _songBar = new RectangleF(tx, r.Y + Theme.F(42), tw, Theme.F(10));
                var bar = new RectangleF(tx, _songBar.Y + Theme.F(3), tw - Theme.F(70), Theme.F(4));
                Theme.FillRound(g, Theme.Hole, bar, Theme.F(2));
                Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, bar.Width * d.SongFrac, bar.Height), Theme.F(2));
                Label(g, d.SongTime ?? "", new RectangleF(bar.Right + Theme.F(4), _songBar.Y - Theme.F(3), Theme.F(66), Theme.F(14)), Theme.TextDim, 8f, false, Theme.Right);
            }
            _btn.Clear();
            var ids = More ? new[] { "shuffle", "prev", "seek-", "play", "seek+", "next", "repeat" } : new[] { "prev", "play", "next" };
            float bw = Math.Min(Theme.F(38), (tw - Theme.F(4) * (ids.Length - 1)) / ids.Length), bh = Theme.F(28), by = r.Bottom - bh;
            for (int i = 0; i < ids.Length; i++)
            {
                var br = new RectangleF(tx + i * (bw + Theme.F(4)), by, bw, bh);
                _btn[ids[i]] = br;
                bool lit = (ids[i] == "shuffle" && d?.Shuffle == true) || (ids[i] == "repeat" && d != null && d.Repeat > 0);
                Theme.DrawBarButton(g, br, Hover == ids[i]);
                MediaGlyph(g, br, ids[i], lit ? Theme.Accent : Hover == ids[i] ? Theme.TextHover : Theme.Text);
            }
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_songBar.IsEmpty && _songBar.Contains(e.Location))
            {
                float bw = _songBar.Width - Theme.F(70);
                Seek?.Invoke(Math.Max(0f, Math.Min(1f, (e.X - _songBar.X) / Math.Max(1f, bw))));
                return true;
            }
            string z = ContentZone(e.Location);
            if (e.Button != MouseButtons.Left || z == null) return false;
            Media?.Invoke(z);
            return true;
        }

        private RectangleF _songBar;
        /// <summary>4.0 finale: an diese Stelle des Songs springen (0..1).</summary>
        public event Action<float> Seek;
    }

    // ======================================================================== 191 Raum-Ansicht

    /// <summary>
    /// Raum-Ansicht von oben: du in der Mitte, jeder Kanal ein Punkt. Weiter weg = leiser, links/rechts = Panorama.
    /// Punkte ziehen aendert Lautstaerke und Panorama, Doppelklick = stumm.
    /// </summary>
    internal sealed class RoomWindow : PultWin
    {
        public sealed class Dot { public string Id; public string Name; public Color Color; public float Pos, Pan, Peak; public bool Mute; }
        private List<Dot> _dots = new List<Dot>();
        private string _dragId;
        public event Action<string, float, float> Moved2;   // id, pos, pan
        public event Action<string> MuteClicked;
        private int _q = int.MinValue;

        public RoomWindow() : base("Raum-Ansicht", 440, 470) { }

        public void SetDots(List<Dot> dots)
        {
            if (_dragId != null) { var d = dots.FirstOrDefault(x => x.Id == _dragId); var old = _dots.FirstOrDefault(x => x.Id == _dragId); if (d != null && old != null) { d.Pos = old.Pos; d.Pan = old.Pan; } }
            _dots = dots;
            int q = dots.Sum(d => (int)(d.Pos * 200) * 3 + (int)(d.Pan * 100) * 7 + (int)(ChanWindow.MeterFrac(d.Peak) * 12) * 13 + (d.Mute ? 1 : 0) + d.Id.GetHashCode() % 1000);
            if (q != _q) { _q = q; Invalidate(); }
        }

        private RectangleF Room => RectangleF.Inflate(Content, -Theme.F(6), -Theme.F(14));
        private PointF Center => new PointF(Room.X + Room.Width / 2f, Room.Y + Room.Height / 2f);
        private float Radius => Math.Min(Room.Width, Room.Height) / 2f - Theme.F(14);

        /// <summary>Abstand vom Hoerer (Anteil am Radius) fuer eine Faderposition: 0 dB nah, −60 dB am Rand.</summary>
        internal static float DistFrac(float pos)
        {
            float db = FaderLaw.PosToDb(Math.Max(0f, Math.Min(1f, pos)));
            if (float.IsNegativeInfinity(db) || db < -60f) db = -60f;
            return 0.18f + 0.82f * (float)Math.Sqrt(Math.Min(1f, Math.Max(0f, -db / 60f)));   // Wurzel: die lauten Kanaele liegen nicht alle auf einem Haufen
        }

        /// <summary>Lautstaerke/Panorama → Punkt (vorne-Halbkreis; laut = nah).</summary>
        internal static PointF DotPoint(PointF c, float radius, float pos, float pan)
        {
            float dist = radius * DistFrac(pos);
            double th = Math.Asin(Math.Max(-1f, Math.Min(1f, pan)));
            return new PointF(c.X + (float)Math.Sin(th) * dist, c.Y - (float)Math.Cos(th) * dist);
        }

        /// <summary>Punkt → Lautstaerke/Panorama.</summary>
        internal static void FromPoint(PointF c, float radius, PointF p, out float pos, out float pan)
        {
            float dx = p.X - c.X, dy = p.Y - c.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float f = Math.Max(0f, Math.Min(1f, (dist / Math.Max(1f, radius) - 0.18f) / 0.82f));
            float db = -60f * f * f;
            pos = f >= 0.999f ? 0f : FaderLaw.DbToPos(db);
            pan = dist < 1f ? 0f : Math.Max(-1f, Math.Min(1f, dx / dist));
        }

        /// <summary>Wo die Punkte gezeichnet werden: liegen zwei fast aufeinander, ruecken sie etwas zur Seite (nur Anzeige).</summary>
        private Dictionary<string, PointF> Places()
        {
            var res = new Dictionary<string, PointF>();
            var placed = new List<PointF>();
            foreach (var d in _dots)
            {
                var pt = DotPoint(Center, Radius, d.Pos, d.Pan);
                for (int k = 0; k < 8 && placed.Any(q => Math.Abs(q.X - pt.X) < Theme.F(20) && Math.Abs(q.Y - pt.Y) < Theme.F(20)); k++)
                    pt = new PointF(pt.X + (k % 2 == 0 ? 1 : -1) * Theme.F(22) * (k / 2 + 1), pt.Y);
                placed.Add(pt);
                res[d.Id] = pt;
            }
            return res;
        }

        protected override string ContentZone(Point p) => DotAt(p)?.Id;

        private Dot DotAt(Point p)
        {
            var places = Places();
            foreach (var d in _dots.AsEnumerable().Reverse())
            {
                var pt = places[d.Id];
                if (Math.Abs(p.X - pt.X) <= Theme.F(14) && Math.Abs(p.Y - pt.Y) <= Theme.F(14)) return d;
            }
            return null;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            var c = Center; float R = Radius;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Boden mit Abstandsringen
            using (var b = new SolidBrush(Theme.Alpha(Theme.Well, 230))) g.FillEllipse(b, c.X - R, c.Y - R, R * 2, R * 2);
            using (var pen = new Pen(Theme.Alpha(Theme.TextFaint, 60), 1f))
                foreach (float db in new[] { 0f, -10f, -20f, -40f })
                {
                    float rr = R * (0.18f + 0.82f * (float)Math.Sqrt(-db / 60f));
                    g.DrawEllipse(pen, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    Label(g, db == 0f ? "0 dB" : db.ToString("0").Replace("-", "−") + " dB", new RectangleF(c.X + Theme.F(3), c.Y - rr - Theme.F(1), Theme.F(50), Theme.F(12)), Theme.TextFaint, 7.5f);
                }
            Label(g, "LINKS", new RectangleF(r.X, c.Y - Theme.F(7), Theme.F(50), Theme.F(14)), Theme.TextFaint, 8f, true);
            Label(g, "RECHTS", new RectangleF(r.Right - Theme.F(50), c.Y - Theme.F(7), Theme.F(50), Theme.F(14)), Theme.TextFaint, 8f, true, Theme.Right);
            Label(g, "VORNE", new RectangleF(c.X - Theme.F(30), r.Y, Theme.F(60), Theme.F(14)), Theme.TextFaint, 8f, true, Theme.Center);
            // Hoerer in der Mitte (Kopf mit Kopfhoerer)
            using (var b = new SolidBrush(Theme.TextDim)) g.FillEllipse(b, c.X - Theme.F(9), c.Y - Theme.F(9), Theme.F(18), Theme.F(18));
            using (var pen = new Pen(Theme.Text, Math.Max(1.5f, Theme.F(2f)))) g.DrawArc(pen, c.X - Theme.F(12), c.Y - Theme.F(12), Theme.F(24), Theme.F(24), 200, 140);
            // Kanaele
            var places = Places();
            foreach (var d in _dots)
            {
                var pt = places[d.Id];
                using (var pen = new Pen(Theme.Alpha(d.Color, 50), 1f) { DashStyle = DashStyle.Dot }) g.DrawLine(pen, c, pt);
                float lv = d.Mute ? 0f : ChanWindow.MeterFrac(d.Peak);
                float rr = Theme.F(10) + Theme.F(7) * lv;
                if (lv > 0.05f) using (var b = new SolidBrush(Theme.Alpha(d.Color, (int)(40 + 80 * lv)))) g.FillEllipse(b, pt.X - rr, pt.Y - rr, rr * 2, rr * 2);
                Color fill = d.Mute ? Theme.Darken(d.Color, 0.6f) : d.Color;
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, pt.X - Theme.F(9), pt.Y - Theme.F(9), Theme.F(18), Theme.F(18));
                using (var pen = new Pen(Hover == d.Id || _dragId == d.Id ? Theme.TextHover : Theme.Alpha(Color.Black, 120), Math.Max(1f, Theme.F(1.5f)))) g.DrawEllipse(pen, pt.X - Theme.F(9), pt.Y - Theme.F(9), Theme.F(18), Theme.F(18));
                bool leftSide = pt.X > c.X + R * 0.35f;
                Label(g, d.Name + (d.Mute ? " (stumm)" : ""), leftSide ? new RectangleF(pt.X - Theme.F(132), pt.Y - Theme.F(7), Theme.F(120), Theme.F(14)) : new RectangleF(pt.X + Theme.F(12), pt.Y - Theme.F(7), Theme.F(120), Theme.F(14)),
                    d.Mute ? Theme.TextFaint : Theme.Text, 8.5f, true, leftSide ? Theme.Right : Theme.Left);
            }
            Label(g, "Punkt ziehen: näher = lauter, links/rechts = Panorama · Doppelklick = stumm", new RectangleF(r.X, r.Bottom - Theme.F(14), r.Width, Theme.F(14)), Theme.TextFaint, 8f, false, Theme.Center);
            g.SmoothingMode = sm;
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            var d = DotAt(e.Location);
            if (d == null || e.Button != MouseButtons.Left) return false;
            if (e.Clicks >= 2) { MuteClicked?.Invoke(d.Id); return true; }
            _dragId = d.Id; Capture = true;
            return true;
        }

        protected override void ContentMove(MouseEventArgs e)
        {
            if (_dragId == null) return;
            var d = _dots.FirstOrDefault(x => x.Id == _dragId);
            if (d == null) return;
            FromPoint(Center, Radius, e.Location, out float pos, out float pan);
            d.Pos = pos; d.Pan = pan;
            Moved2?.Invoke(d.Id, pos, pan);
            Invalidate();
        }

        protected override void ContentUp(MouseEventArgs e) { _dragId = null; Capture = false; }
    }

    // ======================================================================== 203 Ton-Zeitleiste

    /// <summary>Wer wann zu hoeren war: pro Kanal und Sekunde ein Bit (die letzten 2 Stunden), dazu Ereignisse (Szenen …).</summary>
    internal sealed class SoundTimeline
    {
        public const int Seconds = 7200;
        public sealed class Lane { public string Id, Name; public Color Color; public readonly bool[] On = new bool[Seconds]; }
        public readonly Dictionary<string, Lane> Lanes = new Dictionary<string, Lane>();
        public readonly List<KeyValuePair<DateTime, string>> Events = new List<KeyValuePair<DateTime, string>>();
        public DateTime Start = DateTime.Now;
        private long _lastSec = -1;

        /// <summary>Sekunden seit dem Start.</summary>
        public long SecNow => (long)(DateTime.Now - Start).TotalSeconds;

        public void Mark(string id, string name, Color color, bool on)
        {
            long s = SecNow;
            if (!Lanes.TryGetValue(id, out var l)) Lanes[id] = l = new Lane { Id = id };
            l.Name = name; l.Color = color;
            if (s != _lastSec)
            {
                // neue Sekunde(n): die naechsten Felder im Kreis leeren
                long from = Math.Max(_lastSec + 1, s - Seconds + 1);
                if (_lastSec >= 0) for (long t = from; t <= s; t++) foreach (var x in Lanes.Values) x.On[(int)(t % Seconds)] = false;
                _lastSec = s;
            }
            if (on) l.On[(int)(s % Seconds)] = true;
        }

        public void Event(string text)
        {
            Events.Add(new KeyValuePair<DateTime, string>(DateTime.Now, text));
            if (Events.Count > 300) Events.RemoveAt(0);
        }

        /// <summary>War der Kanal 'agoSec' Sekunden vor jetzt zu hoeren?</summary>
        public bool WasOn(string id, long agoSec)
        {
            long s = SecNow - agoSec;
            if (s < 0 || agoSec >= Seconds || !Lanes.TryGetValue(id, out var l)) return false;
            return l.On[(int)(s % Seconds)];
        }
    }

    internal sealed class TimelineWindow : PultWin
    {
        private readonly SoundTimeline _tl;
        public int Minutes = 30;
        private RectangleF[] _rangeR = new RectangleF[0];
        private static readonly int[] Ranges = { 10, 30, 60, 120 };

        public TimelineWindow(SoundTimeline tl) : base("Ton-Zeitleiste", 760, 380) { _tl = tl; }

        protected override string ContentZone(Point p) { for (int i = 0; i < _rangeR.Length; i++) if (_rangeR[i].Contains(p)) return "r" + i; return null; }

        protected override bool ContentDown(MouseEventArgs e)
        {
            string z = ContentZone(e.Location);
            if (z == null || e.Button != MouseButtons.Left) return false;
            Minutes = Ranges[int.Parse(z.Substring(1))];
            Invalidate();
            return true;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            // Zeitraum-Knoepfe
            _rangeR = new RectangleF[Ranges.Length];
            for (int i = 0; i < Ranges.Length; i++)
            {
                var br = new RectangleF(r.Right - (Ranges.Length - i) * Theme.F(58), r.Y, Theme.F(54), Theme.F(20));
                _rangeR[i] = br;
                bool on = Minutes == Ranges[i];
                Theme.FillRound(g, on ? Theme.Accent : Theme.Alpha(Theme.Text, Hover == "r" + i ? 50 : 25), br, Theme.F(10));
                Label(g, Ranges[i] < 60 ? Ranges[i] + " min" : Ranges[i] / 60 + " Std", br, on ? Color.White : Theme.Text, 8.5f, true, Theme.Center);
            }
            Label(g, "Wer war wann zu hören?", new RectangleF(r.X, r.Y, r.Width / 2f, Theme.F(20)), Theme.TextDim, 9.5f);
            float labelW = Theme.F(90), top = r.Y + Theme.F(28), axisH = Theme.F(16), evH = Theme.F(18);
            var lanes = _tl.Lanes.Values.ToList();
            float area = r.Bottom - top - axisH - evH;
            float laneH = lanes.Count == 0 ? Theme.F(18) : Math.Max(Theme.F(8), Math.Min(Theme.F(22), area / lanes.Count));
            float x0 = r.X + labelW, w = r.Right - x0;
            long span = Minutes * 60L;
            for (int i = 0; i < lanes.Count; i++)
            {
                var l = lanes[i];
                float y = top + i * laneH;
                Label(g, l.Name, new RectangleF(r.X, y, labelW - Theme.F(6), laneH), Theme.Text, Math.Min(9f, laneH / Theme.S * 0.7f + 2f), true);
                var lr = new RectangleF(x0, y + laneH * 0.18f, w, laneH * 0.64f);
                using (var b = new SolidBrush(Theme.Alpha(Theme.Well, 200))) g.FillRectangle(b, lr);
                int px = Math.Max(1, (int)w);
                using (var b = new SolidBrush(l.Color))
                {
                    int runStart = -1;
                    for (int k = 0; k <= px; k++)
                    {
                        bool on = false;
                        if (k < px)
                        {
                            long a0 = span - (long)((k + 1) * (double)span / px), a1 = span - (long)(k * (double)span / px);
                            for (long a = Math.Max(0, a0); a < a1 && !on; a++) on = _tl.WasOn(l.Id, a);
                        }
                        if (on && runStart < 0) runStart = k;
                        if (!on && runStart >= 0) { g.FillRectangle(b, x0 + runStart, lr.Y, k - runStart, lr.Height); runStart = -1; }
                    }
                }
            }
            if (lanes.Count == 0) Label(g, "Noch nichts aufgezeichnet – die Zeitleiste läuft, solange sie eingeschaltet ist.", new RectangleF(x0, top, w, Theme.F(20)), Theme.TextFaint, 9f);
            // Zeitachse
            float ay = r.Bottom - axisH - evH;
            using (var pen = new Pen(Theme.Alpha(Theme.TextFaint, 120), 1f)) g.DrawLine(pen, x0, ay, r.Right, ay);
            int stepMin = Minutes <= 10 ? 2 : Minutes <= 30 ? 5 : Minutes <= 60 ? 10 : 20;
            var now = DateTime.Now;
            for (int m = 0; m <= Minutes; m += stepMin)
            {
                float x = r.Right - m / (float)Minutes * w;
                using (var pen = new Pen(Theme.Alpha(Theme.TextFaint, 60), 1f)) g.DrawLine(pen, x, top, x, ay + Theme.F(3));
                Label(g, now.AddMinutes(-m).ToString("HH:mm"), new RectangleF(x - Theme.F(20), ay + Theme.F(2), Theme.F(40), Theme.F(13)), Theme.TextFaint, 7.5f, false, Theme.Center);
            }
            // Ereignisse
            float ey = r.Bottom - evH + Theme.F(3);
            foreach (var ev in _tl.Events)
            {
                double ago = (now - ev.Key).TotalSeconds;
                if (ago < 0 || ago > span) continue;
                float x = r.Right - (float)(ago / span) * w;
                using (var b = new SolidBrush(Theme.Accent)) g.FillPolygon(b, new[] { new PointF(x, ey), new PointF(x - Theme.F(4), ey + Theme.F(7)), new PointF(x + Theme.F(4), ey + Theme.F(7)) });
                Label(g, ev.Value, new RectangleF(x + Theme.F(5), ey - Theme.F(2), Theme.F(140), Theme.F(13)), Theme.TextDim, 7.5f);
            }
        }
    }

    // ======================================================================== 204 Statistik

    internal sealed class StatsWindow : PultWin
    {
        public Func<StatsData> Data;
        public Func<string> Extra;
        public StatsWindow() : base("Statistik", 560, 500) { }

        internal static string Hours(int sec) => sec >= 3600 ? (sec / 3600.0).ToString("0.0") + " Std" : (sec / 60) + " min";

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            var d = Data?.Invoke();
            if (d == null) { Label(g, "Noch keine Zahlen.", r, Theme.TextDim, 10f); return; }
            float y = r.Y, colW = r.Width / 2f - Theme.F(8);
            int total = d.DaySec?.Values.Sum() ?? 0;
            Label(g, "Seit " + (d.Since ?? "heute") + " · " + d.Starts + " Starts · " + Hours(total) + " mit Ton" + (d.VolCount > 0 ? " · Ausgang im Schnitt " + (int)Math.Round(d.VolSum / d.VolCount * 100) + " %" : ""), new RectangleF(r.X, y, r.Width, Theme.F(18)), Theme.TextDim, 9.5f);
            y += Theme.F(22);
            if (d.MaxLufs < 0f) { Label(g, "Am lautesten: " + d.MaxLufs.ToString("0.0").Replace("-", "−") + " LUFS (" + (d.MaxLufsWhen ?? "") + ")", new RectangleF(r.X, y, r.Width, Theme.F(16)), Theme.TextDim, 9f); y += Theme.F(20); }
            // Kanaele (meiste Zeit)
            Label(g, "KANÄLE – SO LANGE ZU HÖREN", new RectangleF(r.X, y, colW, Theme.F(16)), Theme.Text, 8.5f, true);
            Label(g, "SZENEN – SO OFT GELADEN", new RectangleF(r.X + colW + Theme.F(16), y, colW, Theme.F(16)), Theme.Text, 8.5f, true);
            y += Theme.F(18);
            var ch = (d.ChannelSec ?? new Dictionary<string, int>()).OrderByDescending(kv => kv.Value).Take(6).ToList();
            int chMax = ch.Count == 0 ? 1 : Math.Max(1, ch[0].Value);
            var sc = (d.SceneUse ?? new Dictionary<string, int>()).OrderByDescending(kv => kv.Value).Take(6).ToList();
            int scMax = sc.Count == 0 ? 1 : Math.Max(1, sc[0].Value);
            for (int i = 0; i < 6; i++)
            {
                float yy = y + i * Theme.F(22);
                if (i < ch.Count) Bar(g, new RectangleF(r.X, yy, colW, Theme.F(18)), ch[i].Key, Hours(ch[i].Value), ch[i].Value / (float)chMax);
                if (i < sc.Count) Bar(g, new RectangleF(r.X + colW + Theme.F(16), yy, colW, Theme.F(18)), sc[i].Key, sc[i].Value + "×", sc[i].Value / (float)scMax);
            }
            if (ch.Count == 0) Label(g, "noch nichts", new RectangleF(r.X, y, colW, Theme.F(16)), Theme.TextFaint, 9f);
            if (sc.Count == 0) Label(g, "noch keine Szene geladen", new RectangleF(r.X + colW + Theme.F(16), y, colW, Theme.F(16)), Theme.TextFaint, 9f);
            y += Theme.F(22) * 6 + Theme.F(10);
            // letzte 14 Tage
            Label(g, "LETZTE 14 TAGE – MINUTEN MIT TON", new RectangleF(r.X, y, r.Width, Theme.F(16)), Theme.Text, 8.5f, true);
            y += Theme.F(18);
            float chartH = r.Bottom - y - Theme.F(34);
            var days = Enumerable.Range(0, 14).Select(k => DateTime.Today.AddDays(k - 13)).ToList();
            int dayMax = Math.Max(60, days.Max(dd => d.DaySec != null && d.DaySec.TryGetValue(dd.ToString("yyyy-MM-dd"), out int v) ? v : 0));
            float bw = r.Width / 14f;
            for (int k = 0; k < 14; k++)
            {
                int v = d.DaySec != null && d.DaySec.TryGetValue(days[k].ToString("yyyy-MM-dd"), out int vv) ? vv : 0;
                float h = chartH * v / dayMax;
                var br = new RectangleF(r.X + k * bw + bw * 0.15f, y + chartH - h, bw * 0.7f, Math.Max(1f, h));
                Theme.FillRound(g, k == 13 ? Theme.Accent : Theme.Alpha(Theme.Accent, 150), br, Theme.F(2));
                Label(g, days[k].ToString("dd."), new RectangleF(r.X + k * bw, y + chartH + Theme.F(2), bw, Theme.F(12)), Theme.TextFaint, 7.5f, false, Theme.Center);
                if (v > 0) Label(g, (v / 60).ToString(), new RectangleF(r.X + k * bw, y + chartH - h - Theme.F(12), bw, Theme.F(11)), Theme.TextDim, 7f, false, Theme.Center);
            }
            string ex = Extra?.Invoke();
            if (!string.IsNullOrEmpty(ex)) Label(g, ex, new RectangleF(r.X, r.Bottom - Theme.F(14), r.Width, Theme.F(14)), Theme.TextFaint, 8f);
        }

        private static void Bar(Graphics g, RectangleF r, string name, string val, float frac)
        {
            Theme.FillRound(g, Theme.Alpha(Theme.Well, 200), r, Theme.F(3));
            Theme.FillRound(g, Theme.Alpha(Theme.Accent, 110), new RectangleF(r.X, r.Y, Math.Max(Theme.F(3), r.Width * frac), r.Height), Theme.F(3));
            Label(g, name, new RectangleF(r.X + Theme.F(6), r.Y, r.Width - Theme.F(70), r.Height), Theme.Text, 9f, true, Theme.Left);
            Label(g, val, new RectangleF(r.Right - Theme.F(70), r.Y, Theme.F(64), r.Height), Theme.TextDim, 9f, false, Theme.Right);
        }
    }

    // ======================================================================== 180 Ton-Doktor

    internal sealed class DoctorCheck
    {
        public string Title, Detail;
        public int Level;      // 0 gut, 1 Hinweis, 2 Problem
        public string FixText;
        public Action Fix;
    }

    internal sealed class DoctorWindow : PultWin
    {
        public Func<List<DoctorCheck>> Run;
        private List<DoctorCheck> _checks = new List<DoctorCheck>();
        private readonly List<KeyValuePair<RectangleF, DoctorCheck>> _fixR = new List<KeyValuePair<RectangleF, DoctorCheck>>();
        private RectangleF _again;

        public DoctorWindow() : base("Ton-Doktor", 600, 520) { }

        public void Recheck() { _checks = Run?.Invoke() ?? new List<DoctorCheck>(); Invalidate(); }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Recheck(); }

        protected override string ContentZone(Point p)
        {
            if (_again.Contains(p)) return "again";
            for (int i = 0; i < _fixR.Count; i++) if (_fixR[i].Key.Contains(p)) return "fix" + i;
            return null;
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            string z = ContentZone(e.Location);
            if (z == null || e.Button != MouseButtons.Left) return false;
            if (z == "again") { Recheck(); return true; }
            var c = _fixR[int.Parse(z.Substring(3))].Value;
            try { c.Fix?.Invoke(); } catch { }
            BeginInvoke(new Action(Recheck));
            return true;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            int bad = _checks.Count(c => c.Level == 2), hint = _checks.Count(c => c.Level == 1);
            Label(g, bad == 0 && hint == 0 ? "Alles in Ordnung – kein Grund, warum etwas nicht zu hören sein sollte." : bad > 0 ? bad + (bad == 1 ? " Problem" : " Probleme") + (hint > 0 ? ", " + hint + " Hinweis" + (hint == 1 ? "" : "e") : "") : hint + " Hinweis" + (hint == 1 ? "" : "e"),
                new RectangleF(r.X, r.Y, r.Width - Theme.F(110), Theme.F(20)), bad > 0 ? Theme.WarnText : Theme.OkText, 10f, true);
            _again = new RectangleF(r.Right - Theme.F(104), r.Y - Theme.F(1), Theme.F(104), Theme.F(22));
            Theme.DrawBarButton(g, _again, Hover == "again");
            Label(g, "Neu prüfen", _again, Hover == "again" ? Theme.TextHover : Theme.Text, 9f, true, Theme.Center);
            _fixR.Clear();
            float y = r.Y + Theme.F(30);
            foreach (var c in _checks.OrderByDescending(x => x.Level))
            {
                if (y > r.Bottom - Theme.F(30)) break;
                Color col = c.Level == 2 ? Theme.MuteRed : c.Level == 1 ? Theme.SoloAmber : Theme.LedGreen;
                var dot = new RectangleF(r.X, y + Theme.F(3), Theme.F(14), Theme.F(14));
                using (var b = new SolidBrush(col)) g.FillEllipse(b, dot);
                Label(g, c.Level == 0 ? "✓" : "!", dot, Color.Black, 8.5f, true, Theme.Center);
                float tw = r.Width - Theme.F(22) - (c.Fix != null ? Theme.F(110) : 0f);
                Label(g, c.Title, new RectangleF(r.X + Theme.F(22), y, tw, Theme.F(18)), Theme.Text, 9.5f, true);
                float dh = 0f;
                if (!string.IsNullOrEmpty(c.Detail))
                {
                    var f = Theme.Ui(8.5f);
                    dh = Math.Min(Theme.F(44), g.MeasureString(c.Detail, f, (int)tw).Height + Theme.F(2));
                    using (var b = new SolidBrush(Theme.TextDim)) g.DrawString(c.Detail, f, b, new RectangleF(r.X + Theme.F(22), y + Theme.F(18), tw, dh));
                }
                if (c.Fix != null)
                {
                    var fr = new RectangleF(r.Right - Theme.F(104), y, Theme.F(104), Theme.F(22));
                    int k = _fixR.Count;
                    _fixR.Add(new KeyValuePair<RectangleF, DoctorCheck>(fr, c));
                    Theme.FillRound(g, Hover == "fix" + k ? Theme.PrimaryHover : Theme.Primary, fr, Theme.F(4));
                    Label(g, c.FixText ?? "Beheben", fr, Color.White, 9f, true, Theme.Center);
                }
                y += Theme.F(22) + dh + Theme.F(6);
            }
        }
    }
}
