using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0: ein Kanal im eigenen kleinen Fenster (immer im Vordergrund) – Name, Fader, Pegel und MUTE.
    /// Am Namen ziehen verschiebt das Fenster, Rechtsklick: schliessen. Gezeichnet wie das Pult.
    /// </summary>
    internal sealed class ChanWindow : Form
    {
        public readonly string RowId;   // "ch:<id>", "grp:<id>", "master"
        public event Action<string, float> LevelChanged;
        public event Action<string> MuteClicked;
        public event Action<ChanWindow> Moved;
        public event Action<ChanWindow> Opened;   // Klick aufs Symbol: Pult zeigen

        private MiniRow _row;
        private RectangleF _nameR, _slotR, _meterR, _muteR, _valueR, _closeR;
        private bool _drag, _moveWin;
        private Point _moveStart, _winStart;
        private float _dragVirtual;
        private int _lastY;
        private string _hover;
        private int _q = int.MinValue;

        public ChanWindow(string rowId)
        {
            RowId = rowId;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(Theme.Px(96), Theme.Px(300));
            BackColor = Theme.Body;
            Text = "Mischpult – Kanal";
            Layout2();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080;   // WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW: nimmt dem Spiel nicht den Fokus
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        private void Layout2()
        {
            float w = ClientSize.Width, h = ClientSize.Height, p = Theme.F(7);
            _nameR = new RectangleF(p, p, w - p * 2, Theme.F(22));
            _closeR = new RectangleF(w - p - Theme.F(16), p + Theme.F(3), Theme.F(16), Theme.F(16));
            _valueR = new RectangleF(p, h - p - Theme.F(20), w - p * 2, Theme.F(20));
            _muteR = new RectangleF(p, _valueR.Y - Theme.F(26), w - p * 2, Theme.F(22));
            float top = _nameR.Bottom + Theme.F(12), bottom = _muteR.Y - Theme.F(12);
            _slotR = new RectangleF((float)Math.Round(w * 0.36f) - Theme.F(3), top, Theme.F(6), bottom - top);
            _meterR = new RectangleF((float)Math.Round(w * 0.68f), top, Theme.F(14), bottom - top);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); Invalidate(); }

        /// <summary>Neue Werte (aus dem Takt) – nur neu zeichnen, wenn sich etwas sichtbar aendert.</summary>
        public void SetRow(MiniRow r)
        {
            if (r == null) return;
            int q = (int)(r.Pos * 400) * 7 + (r.Mute ? 1 : 0) + (int)(r.PeakL * 60) * 4001 + (int)(r.PeakR * 60) * 97001 + (r.Name?.GetHashCode() ?? 0);
            _row = r;
            if (q == _q) return;
            _q = q;
            Invalidate();
        }

        private float PosToY(float pos) => _slotR.Bottom - Theme.F(10) - pos * (_slotR.Height - Theme.F(20));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            var r = _row;
            var full = new RectangleF(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var b = new LinearGradientBrush(new RectangleF(0, -1, full.Width, full.Height + 2), Theme.PanelTop, Theme.PanelBottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, full);
            Theme.StrokeRound(g, Theme.PanelEdge, Theme.F(1), RectangleF.Inflate(full, -0.5f, -0.5f), Theme.F(5));
            if (r == null) return;
            // Name
            Theme.DrawNamePlate(g, _nameR, r.Color);
            using (var b = new SolidBrush(Theme.PlateText(r.Color)))
                Theme.Str(g, r.Name, Theme.FitPlate(g, r.Name, _nameR.Width - Theme.F(22), 11f, 7.5f), b, new RectangleF(_nameR.X + Theme.F(2), _nameR.Y, _nameR.Width - Theme.F(20), _nameR.Height), Theme.Center);
            using (var pen = new Pen(_hover == "close" ? Theme.MuteRed : Theme.PlateText(r.Color), Math.Max(1.2f, Theme.F(1.4f))))
            {
                float c = Theme.F(4);
                var cr = _closeR;
                g.DrawLine(pen, cr.X + c, cr.Y + c, cr.Right - c, cr.Bottom - c);
                g.DrawLine(pen, cr.Right - c, cr.Y + c, cr.X + c, cr.Bottom - c);
            }
            // Fader
            Theme.DrawSlot(g, _slotR);
            float y = PosToY(r.Pos);
            using (var b = new SolidBrush(Theme.Alpha(r.Color, 150)))
                g.FillRectangle(b, _slotR.X + _slotR.Width / 2f - Theme.F(1), y, Theme.F(2), _slotR.Bottom - y);
            var cap = new RectangleF(_slotR.X + _slotR.Width / 2f - Theme.F(14), y - Theme.F(8), Theme.F(28), Theme.F(16));
            Theme.DrawFaderCap(g, cap, RowId == "master", _hover == "fader" || _drag, r.Color);
            // Pegel
            DrawMeter(g, new RectangleF(_meterR.X, _meterR.Y, _meterR.Width / 2f - Theme.F(1), _meterR.Height), r.PeakL);
            DrawMeter(g, new RectangleF(_meterR.X + _meterR.Width / 2f + Theme.F(1), _meterR.Y, _meterR.Width / 2f - Theme.F(1), _meterR.Height), r.PeakR);
            // MUTE und Wert
            Theme.DrawKey(g, _muteR, "MUTE", r.Mute, Theme.MuteRed, _hover == "mute");
            Theme.DrawLcd(g, _valueR, r.Value ?? "", null);
        }

        private static void DrawMeter(Graphics g, RectangleF r, float peak)
        {
            using (var b = new SolidBrush(Theme.Hole)) g.FillRectangle(b, r);
            int n = Math.Max(8, (int)(r.Height / Theme.F(4)));
            float step = r.Height / n;
            float frac = MeterFrac(peak);
            int lit = (int)Math.Round(frac * n);
            for (int i = 0; i < n; i++)
            {
                var sr = new RectangleF(r.X, r.Bottom - (i + 1) * step + 1, r.Width, step - 1);
                Color c = i < lit ? Theme.SegColor((i + 1f) / n, Theme.Accent) : Theme.SegOff;
                using (var b = new SolidBrush(c)) g.FillRectangle(b, sr);
            }
        }

        /// <summary>-60 … 0 dBFS auf 0 … 1.</summary>
        internal static float MeterFrac(float peak)
        {
            if (peak <= 0.001f) return 0f;
            double db = 20 * Math.Log10(peak);
            return (float)Math.Max(0, Math.Min(1, (db + 60) / 60));
        }

        private string ZoneAt(Point p)
        {
            if (_closeR.Contains(p)) return "close";
            if (_nameR.Contains(p)) return "name";
            if (_muteR.Contains(p)) return "mute";
            if (new RectangleF(0, _slotR.Y - Theme.F(8), ClientSize.Width, _slotR.Height + Theme.F(16)).Contains(p)) return "fader";
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string z = ZoneAt(e.Location);
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (z == "close") { Close(); return; }
            if (z == "mute") { MuteClicked?.Invoke(RowId); return; }
            if (z == "fader" && _row != null)
            {
                _drag = true; Capture = true;
                float capY = PosToY(_row.Pos);
                if (Math.Abs(e.Y - capY) > Theme.F(10))
                {
                    float pos = Math.Max(0f, Math.Min(1f, (_slotR.Bottom - Theme.F(10) - e.Y) / (_slotR.Height - Theme.F(20))));
                    _row.Pos = pos;
                    LevelChanged?.Invoke(RowId, pos);
                }
                _dragVirtual = _row.Pos; _lastY = e.Y;
                Invalidate();
                return;
            }
            // sonst: Fenster verschieben
            _moveWin = true; Capture = true;
            _moveStart = Cursor.Position; _winStart = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag && _row != null)
            {
                float d = (e.Y - _lastY) / Math.Max(10f, _slotR.Height - Theme.F(20)) * ((ModifierKeys & Keys.Shift) != 0 ? 0.2f : 1f);
                _lastY = e.Y;
                _dragVirtual = Math.Max(0f, Math.Min(1f, _dragVirtual - d));
                _row.Pos = _dragVirtual;
                LevelChanged?.Invoke(RowId, _dragVirtual);
                Invalidate();
                return;
            }
            if (_moveWin)
            {
                var p = Cursor.Position;
                Location = new Point(_winStart.X + p.X - _moveStart.X, _winStart.Y + p.Y - _moveStart.Y);
                return;
            }
            string z = ZoneAt(e.Location);
            if (z != _hover) { _hover = z; Cursor = z == "name" || z == null ? Cursors.SizeAll : Cursors.Hand; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_moveWin) { _moveWin = false; Capture = false; Moved?.Invoke(this); }
            if (_drag) { _drag = false; Capture = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != null) { _hover = null; Invalidate(); } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_row == null) return;
            float pos = Math.Max(0f, Math.Min(1f, _row.Pos + (e.Delta > 0 ? 0.02f : -0.02f)));
            _row.Pos = pos;
            LevelChanged?.Invoke(RowId, pos);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (ZoneAt(e.Location) == "name") Opened?.Invoke(this);
        }
    }
}
