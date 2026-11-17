using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>Eine Zeile der Mini-Ansicht.</summary>
    internal sealed class MiniRow
    {
        public string Id;          // "ch:<id>", "mic", "master"
        public string Name;
        public Color Color;
        public float Pos;          // 0..1 (Fader bzw. Lautstaerke)
        public bool Mute;
        public float PeakL, PeakR;
        public float PreL = -1, PreR = -1;   // vor Fader/Stumm (-1 = wie PeakL/R)
        public string Value;       // z. B. "-6.0 dB" oder "62 %"
        public string Badge;       // z. B. "DUCK", "LEISER", "PTT"
    }

    /// <summary>
    /// Kleines Fenster, immer im Vordergrund: pro Kanal ein waagerechter Regler mit Pegel und Stummschalter,
    /// dazu Mikro und Ausgang.
    /// </summary>
    internal sealed class MiniForm : Form
    {
        private readonly MixerForm _mixer;
        private List<MiniRow> _rows = new List<MiniRow>();
        private readonly Dictionary<string, MeterBallistics> _meters = new Dictionary<string, MeterBallistics>();
        private readonly Dictionary<string, MeterBallistics> _preMeters = new Dictionary<string, MeterBallistics>();
        private string _hoverRow, _hoverPart, _dragRow;
        private bool _moving;
        private Point _moveOffset;
        private readonly ToolTip _tip = PultTip.Make(new ToolTip());
        private string _tipFor;

        private int RowH => Theme.Px(30);
        private int HeadH => Theme.Px(28);

        public MiniForm(MixerForm mixer)
        {
            _mixer = mixer;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            Text = "Mischpult – Mini";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.Px(300), HeadH + Theme.Px(8));
            try { Icon = mixer.Icon; } catch { }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // TOOLWINDOW: nicht in Alt+Tab
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.F(10)))
                Region = new Region(p);
        }

        /// <summary>Neue Werte vom Mischpult (aus dessen Timer).</summary>
        public void UpdateRows(List<MiniRow> rows, float dt)
        {
            _rows = rows;
            foreach (var r in rows)
            {
                if (!_meters.TryGetValue(r.Id, out var m)) _meters[r.Id] = m = new MeterBallistics();
                m.Update(r.PeakL, r.PeakR, dt);
                if (!_preMeters.TryGetValue(r.Id, out var pm)) _preMeters[r.Id] = pm = new MeterBallistics();
                pm.Update(Math.Max(r.PeakL, r.PreL < 0 ? r.PeakL : r.PreL), Math.Max(r.PeakR, r.PreR < 0 ? r.PeakR : r.PreR), dt);
            }
            int h = HeadH + rows.Count * RowH + Theme.Px(8);
            if (Height != h)
            {
                // unten verankert bleiben, wenn das Fenster am unteren Rand sitzt
                var wa = Screen.FromControl(this).WorkingArea;
                bool bottom = Bottom >= wa.Bottom - Theme.Px(4);
                int y = bottom ? Bottom - h : Top;
                Bounds = new Rectangle(Left, Math.Max(wa.Top, y), Width, h);
            }
            Invalidate();
        }

        private Rectangle RowRect(int i) => new Rectangle(0, HeadH + i * RowH, Width, RowH);
        private RectangleF TrackRect(Rectangle row) => new RectangleF(Theme.F(96), row.Y + Theme.F(9), Width - Theme.F(96) - Theme.F(46), Theme.F(10));
        private RectangleF MuteRect(Rectangle row) => new RectangleF(Width - Theme.F(38), row.Y + Theme.F(5), Theme.F(30), Theme.F(20));
        private RectangleF CloseRect => new RectangleF(Width - Theme.F(28), Theme.F(4), Theme.F(22), Theme.F(20));
        private RectangleF ExpandRect => new RectangleF(Width - Theme.F(54), Theme.F(4), Theme.F(22), Theme.F(20));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.PopupBg);
            Theme.StrokeRound(g, Theme.MenuBorder, Theme.F(1), new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.F(10));

            // Kopf
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, HeadH), Theme.PopupHeadTop, Theme.PopupHeadBottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, 1, 1, Width - 2, HeadH - 1);
            Theme.DrawSpaced(g, "MISCHPULT", Theme.Ui(10f, FontStyle.Bold), Theme.Text, Theme.F(12), Theme.F(7), Theme.F(1.4f));
            using (var b = new SolidBrush(Theme.TextFaint))
                Theme.Str(g, "made by " + Program.Author, Theme.Ui(9f), b, new RectangleF(Theme.F(104), 0, Theme.F(120), HeadH), Theme.Left);
            DrawHeadButton(g, ExpandRect, "expand");
            DrawHeadButton(g, CloseRect, "close");

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var rr = RowRect(i);
                bool hover = _hoverRow == row.Id || _dragRow == row.Id;
                if (hover) using (var b = new SolidBrush(Theme.PopupHover)) g.FillRectangle(b, 1, rr.Y, Width - 2, rr.Height);
                if (i > 0) using (var p = new Pen(Theme.PopupLine)) g.DrawLine(p, Theme.F(10), rr.Y, Width - Theme.F(10), rr.Y);

                // Farbe + Name
                Color col = row.Mute ? Theme.Darken(row.Color, 0.5f) : row.Color;
                Theme.FillRound(g, col, new RectangleF(Theme.F(10), rr.Y + Theme.F(7), Theme.F(4), rr.Height - Theme.F(14)), Theme.F(2));
                var nameRect = new RectangleF(Theme.F(19), rr.Y, Theme.F(74), rr.Height);
                using (var b = new SolidBrush(row.Mute ? Theme.TextFaint : Theme.Text))
                using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, row.Name, Theme.Fit(g, row.Name, nameRect.Width, 10.5f, 7f), b, nameRect, sf);

                // Regler mit Pegel darunter
                var tr = TrackRect(rr);
                Theme.FillRound(g, Theme.Deep, tr, Theme.F(5));
                float pos = Math.Max(0, Math.Min(1, row.Pos));
                if (pos > 0)
                    Theme.FillRound(g, Theme.Alpha(col, row.Mute ? 90 : 170), new RectangleF(tr.X, tr.Y, Math.Max(Theme.F(6), tr.Width * pos), tr.Height), Theme.F(5));
                var mr = new RectangleF(tr.X, tr.Bottom + Theme.F(3), tr.Width, Theme.F(3));
                using (var b = new SolidBrush(Theme.Deep)) g.FillRectangle(b, mr);
                if (Theme.GhostOn && _preMeters.TryGetValue(row.Id, out var pmeter))
                {
                    float pf = Math.Max(pmeter.FracL, pmeter.FracR);
                    if (pf > 0) using (var b = new SolidBrush(Theme.MeterGhost)) g.FillRectangle(b, mr.X, mr.Y, mr.Width * pf, mr.Height);
                }
                if (_meters.TryGetValue(row.Id, out var m))
                {
                    float f = Math.Max(m.FracL, m.FracR);
                    if (f > 0)
                    {
                        Color mc = f > 0.92f ? Theme.MeterRed : f > 0.75f ? Theme.MeterYellow : Theme.MeterGreen;
                        if (row.Mute) mc = Theme.Darken(mc, 0.5f);
                        using (var b = new SolidBrush(mc)) g.FillRectangle(b, mr.X, mr.Y, mr.Width * f, mr.Height);
                    }
                }
                float hx = tr.X + tr.Width * pos;
                Theme.FillEllipse(g, Color.FromArgb(0x05, 0x06, 0x07), hx, tr.Y + tr.Height / 2, Theme.F(7));
                Theme.FillEllipse(g, hover ? Color.White : Theme.IsLight ? Color.FromArgb(0xf4, 0xf6, 0xf8) : Color.FromArgb(0xd7, 0xdb, 0xe0), hx, tr.Y + tr.Height / 2, Theme.F(5.5f));

                if (hover && row.Value != null)
                {
                    using (var b = new SolidBrush(Theme.ValueText))
                        Theme.Str(g, row.Value, Theme.Mono(9f), b, new RectangleF(tr.X, rr.Y - Theme.F(1), tr.Width, Theme.F(10)), Theme.Right);
                }
                else if (row.Badge != null)
                {
                    using (var b = new SolidBrush(Theme.WarnText))
                        Theme.Str(g, row.Badge, Theme.Ui(7.5f, FontStyle.Bold), b, new RectangleF(tr.X, rr.Y - Theme.F(1), tr.Width, Theme.F(10)), Theme.Right);
                }

                Theme.DrawKey(g, MuteRect(rr), "M", row.Mute, Theme.MuteRed, _hoverRow == row.Id && _hoverPart == "mute");
            }
        }

        private void DrawHeadButton(Graphics g, RectangleF r, string id)
        {
            bool hover = _hoverPart == id;
            if (hover) Theme.FillRound(g, Theme.HoverFill, r, Theme.F(4));
            Color c = hover ? Theme.TextHover : Theme.TextDim;
            using (var pen = new Pen(c, Theme.F(1.4f)))
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Theme.F(4.5f);
                if (id == "close")
                {
                    g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                    g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                }
                else
                {
                    g.DrawRectangle(pen, cx - s - Theme.F(1), cy - s, (s + Theme.F(1)) * 2, s * 2);
                    g.DrawLine(pen, cx - s - Theme.F(1), cy - s + Theme.F(2.5f), cx + s + Theme.F(1), cy - s + Theme.F(2.5f));
                }
            }
        }

        private int RowAt(Point p)
        {
            if (p.Y < HeadH) return -1;
            int i = (p.Y - HeadH) / RowH;
            return i >= 0 && i < _rows.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_moving)
            {
                var p = PointToScreen(e.Location);
                Location = new Point(p.X - _moveOffset.X, p.Y - _moveOffset.Y);
                return;
            }
            if (_dragRow != null)
            {
                int i = _rows.FindIndex(r => r.Id == _dragRow);
                if (i >= 0) SetFromX(_rows[i], e.X, RowRect(i));
                return;
            }
            string row = null, part = null;
            if (CloseRect.Contains(e.Location)) part = "close";
            else if (ExpandRect.Contains(e.Location)) part = "expand";
            else
            {
                int i = RowAt(e.Location);
                if (i >= 0)
                {
                    row = _rows[i].Id;
                    var rr = RowRect(i);
                    part = MuteRect(rr).Contains(e.Location) ? "mute" : TrackHit(rr).Contains(e.Location) ? "track" : null;
                }
            }
            if (row != _hoverRow || part != _hoverPart)
            {
                _hoverRow = row; _hoverPart = part;
                Cursor = part != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            string tip = part == "close" ? "Mini-Ansicht schließen" : part == "expand" ? "Großes Mischpult öffnen"
                       : part == "mute" ? "Stumm an/aus" : part == "track" ? "Ziehen oder Mausrad = Lautstärke" : null;
            if (tip != _tipFor) { _tip.SetToolTip(this, tip); _tipFor = tip; }
        }

        private RectangleF TrackHit(Rectangle rr)
        {
            var t = TrackRect(rr);
            return new RectangleF(t.X - Theme.F(6), rr.Y, t.Width + Theme.F(12), rr.Height);
        }

        private void SetFromX(MiniRow row, int x, Rectangle rr)
        {
            var t = TrackRect(rr);
            float pos = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
            row.Pos = pos;
            _mixer.MiniSetLevel(row.Id, pos);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                _mixer.ShowMiniMenu(PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (CloseRect.Contains(e.Location)) { _mixer.SetMini(false); return; }
            if (ExpandRect.Contains(e.Location)) { _mixer.ShowFromMini(); return; }
            int i = RowAt(e.Location);
            if (i < 0)
            {
                _moving = true;
                _moveOffset = e.Location;
                Capture = true;
                return;
            }
            var rr = RowRect(i);
            if (MuteRect(rr).Contains(e.Location)) { _mixer.MiniToggleMute(_rows[i].Id); return; }
            if (TrackHit(rr).Contains(e.Location))
            {
                _dragRow = _rows[i].Id;
                Capture = true;
                SetFromX(_rows[i], e.X, rr);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_moving)
            {
                _moving = false;
                _mixer.MiniMoved(Location);
            }
            _dragRow = null;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y < HeadH && !CloseRect.Contains(e.Location) && !ExpandRect.Contains(e.Location)) _mixer.ShowFromMini();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int i = RowAt(e.Location);
            if (i >= 0) _mixer.MiniWheel(_rows[i].Id, e.Delta);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragRow == null && (_hoverRow != null || _hoverPart != null)) { _hoverRow = null; _hoverPart = null; Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
