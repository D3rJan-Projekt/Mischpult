using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.0.2: kleine Bild-Statistik im Pult (an/aus in OPTIONEN → Allgemein → Hardware-Beschleunigung): Bilder pro Sekunde,
    /// wie gleichmaessig sie kommen, zu spaete Bilder, Rechenzeit und wie oft neue Pegel kamen. Wird einmal pro Sekunde neu
    /// gezeichnet und kostet praktisch nichts. Das × blendet sie wieder aus.
    /// </summary>
    internal sealed class StatsOverlay : Control
    {
        /// <summary>Eine Zeile: Text und Art (0 normal, 1 Ueberschrift, 2 gut, 3 Warnung, 4 Hinweis).</summary>
        public struct Line
        {
            public string Text; public int Kind;
            public Line(string text, int kind = 0) { Text = text; Kind = kind; }
        }

        private Line[] _lines = new Line[0];
        private Rectangle _close;
        private bool _hotClose;
        public event EventHandler CloseClicked;

        public StatsOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public Line[] Lines => _lines;

        /// <summary>Neue Zeilen; Groesse passt sich an.</summary>
        public void SetLines(Line[] lines)
        {
            if (lines == null) lines = new Line[0];
            bool same = lines.Length == _lines.Length;
            if (same) for (int i = 0; i < lines.Length; i++) if (lines[i].Text != _lines[i].Text || lines[i].Kind != _lines[i].Kind) { same = false; break; }
            if (same) return;
            _lines = lines;
            var sz = Measure();
            if (Size != sz) Size = sz;
            Invalidate();
        }

        private Font FontFor(int kind) => kind == 1 ? Theme.UiSemi(10.5f) : Theme.Ui(10.5f);
        private int LineH => Theme.Px(15);
        private int Pad => Theme.Px(8);

        private Size Measure()
        {
            int w = Theme.Px(150);
            using (var g = CreateGraphics())
                foreach (var l in _lines)
                    w = Math.Max(w, (int)Math.Ceiling(g.MeasureString(l.Text, FontFor(l.Kind), 2000, StringFormat.GenericTypographic).Width) + (l.Kind == 1 ? Theme.Px(18) : 0));
            return new Size(w + Pad * 2, Pad * 2 + LineH * Math.Max(1, _lines.Length));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var bg = Parent?.BackColor ?? Color.Black;
            g.Clear(bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.F(6f)))
            using (var b = new SolidBrush(Color.FromArgb(242, 16, 18, 22)))
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            int y = Pad;
            for (int i = 0; i < _lines.Length; i++)
            {
                var l = _lines[i];
                var col = l.Kind == 1 ? Color.FromArgb(235, 235, 240) : l.Kind == 2 ? Color.FromArgb(120, 220, 140)
                        : l.Kind == 3 ? Color.FromArgb(255, 175, 70) : l.Kind == 4 ? Color.FromArgb(150, 190, 255) : Color.FromArgb(200, 202, 210);
                using (var tb = new SolidBrush(col))
                    g.DrawString(l.Text, FontFor(l.Kind), tb, Pad, y, StringFormat.GenericTypographic);
                y += LineH;
            }
            // Schliessen
            int cs = Theme.Px(12);
            _close = new Rectangle(Width - Pad - cs, Pad + Theme.Px(1), cs, cs);
            using (var p = new Pen(_hotClose ? Color.White : Color.FromArgb(150, 255, 255, 255), Theme.F(1.4f)))
            {
                int k = Theme.Px(3);
                g.DrawLine(p, _close.Left + k, _close.Top + k, _close.Right - k, _close.Bottom - k);
                g.DrawLine(p, _close.Right - k, _close.Top + k, _close.Left + k, _close.Bottom - k);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = _close.Contains(e.Location);
            if (hot != _hotClose) { _hotClose = hot; Cursor = hot ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hotClose) { _hotClose = false; Cursor = Cursors.Default; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _close.Contains(e.Location)) CloseClicked?.Invoke(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }
    }
}
