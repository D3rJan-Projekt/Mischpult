using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>2.18: Einrichtungs-Assistent – ein paar Fragen mit grossen Karten zum Anklicken (selbst gezeichnet).</summary>
    internal sealed class SetupWizard : Form
    {
        public sealed class Step
        {
            public string Title, Text;
            public string[] Options, Hints;
            public int Choice;
            /// <summary>Sofort beim Anklicken ausfuehren (z. B. Design gleich am Pult zeigen).</summary>
            public Action<int> Live;
        }

        /// <summary>Nur Vorschau: mit diesem Schritt anfangen.</summary>
        public static int DemoStep = -1;

        private readonly List<Step> _steps;
        private readonly bool _first;
        private int _page;
        private Rectangle[] _cards = new Rectangle[0];
        private Rectangle _next, _back, _skip;
        private int _hoverCard = -1;
        private string _hoverBtn;

        public int Page => _page;
        public bool Skipped { get; private set; }

        public SetupWizard(List<Step> steps, bool first)
        {
            _steps = steps;
            _first = first;
            Text = "Mischpult einrichten";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = first;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(12f);
            KeyPreview = true;
            ClientSize = new Size(Theme.Px(780), Theme.Px(540));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            if (DemoStep >= 0) { _page = Math.Min(DemoStep, steps.Count - 1); DemoStep = -1; }
            try { Icon = Owner?.Icon; } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        private Step Cur => _steps[_page];
        private bool Last => _page == _steps.Count - 1;
        private int M => Theme.Px(30);

        // --------------------------------------------------------------- Aufbau

        private void Layout2()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            int by = h - Theme.Px(62), bh = Theme.Px(38);
            _next = new Rectangle(w - M - Theme.Px(170), by, Theme.Px(170), bh);
            _back = _page > 0 ? new Rectangle(_next.X - Theme.Px(12) - Theme.Px(120), by, Theme.Px(120), bh) : Rectangle.Empty;
            _skip = !Last ? new Rectangle(M - Theme.Px(8), by, Theme.Px(150), bh) : Rectangle.Empty;

            int n = Cur.Options?.Length ?? 0;
            var cards = new Rectangle[n];
            if (n > 0)
            {
                int cols = n >= 7 ? 3 : (n == 2 || n == 4) ? 2 : 1;
                int rows = (n + cols - 1) / cols;
                int top = Theme.Px(178), bottom = by - Theme.Px(22);
                int gap = Theme.Px(10);
                int cw = (w - 2 * M - (cols - 1) * gap) / cols;
                int ch = Math.Min(Theme.Px(cols == 1 ? 52 : 120), (bottom - top - (rows - 1) * gap) / rows);
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols, c = i % cols;
                    cards[i] = new Rectangle(M + c * (cw + gap), top + r * (ch + gap), cw, ch);
                }
            }
            _cards = cards;
        }

        // --------------------------------------------------------------- Zeichnen

        private static readonly StringFormat OneLine = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Near };
        private static readonly StringFormat Wrapped = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Near };

        protected override void OnPaint(PaintEventArgs e)
        {
            Layout2();
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            int w = ClientSize.Width;

            // Fortschritt: Punkte und "Schritt x von y"
            float dx = M;
            for (int i = 0; i < _steps.Count; i++)
            {
                bool done = i < _page, now = i == _page;
                var r = new RectangleF(dx, Theme.F(26), now ? Theme.F(26) : Theme.F(10), Theme.F(10));
                Theme.FillRound(g, now ? Theme.Accent : done ? Color.FromArgb(160, Theme.Accent) : Color.FromArgb(50, Theme.Text), r, r.Height / 2f);
                dx = r.Right + Theme.F(7);
            }
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, (_first && _page == 0 ? "Willkommen beim Mischpult · " : "") + "Schritt " + (_page + 1) + " von " + _steps.Count, Theme.Ui(11f), b, dx + Theme.F(8), Theme.F(22));

            // Titel und Text
            using (var b = new SolidBrush(Theme.Text))
                Theme.Str(g, Cur.Title, Theme.UiSemi(24f), b, new RectangleF(M, Theme.F(52), w - 2 * M, Theme.F(40)), OneLine);
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, Cur.Text, Theme.Ui(12.5f), b, new RectangleF(M, Theme.F(98), w - 2 * M, Theme.F(64)), Wrapped);

            // Karten
            for (int i = 0; i < _cards.Length; i++) DrawCard(g, i);
            if (_cards.Length == 0) DrawDone(g);

            // Knoepfe
            if (!_skip.IsEmpty) DrawButton(g, _skip, _first ? "Später" : "Abbrechen", 0, _hoverBtn == "skip");
            if (!_back.IsEmpty) DrawButton(g, _back, "Zurück", 1, _hoverBtn == "back");
            DrawButton(g, _next, Last ? "Los geht's" : "Weiter  →", 2, _hoverBtn == "next");
        }

        private void DrawCard(Graphics g, int i)
        {
            var r = _cards[i];
            bool sel = i == Cur.Choice, hov = i == _hoverCard;
            float rad = Theme.F(8);
            Theme.FillRound(g, sel ? Color.FromArgb(40, Theme.Accent) : Color.FromArgb(hov ? 26 : 14, Theme.Text), r, rad);
            Theme.StrokeRound(g, sel ? Theme.Accent : Color.FromArgb(hov ? 90 : 45, Theme.Text), sel ? Theme.F(2) : 1f, RectangleF.Inflate(r, -0.5f, -0.5f), rad);
            // Auswahlpunkt
            float d = Theme.F(16);
            var dot = new RectangleF(r.X + Theme.F(14), r.Y + (r.Height <= Theme.Px(60) ? (r.Height - d) / 2f : Theme.F(14)), d, d);
            Theme.StrokeRound(g, sel ? Theme.Accent : Color.FromArgb(120, Theme.Text), Theme.F(1.6f), dot, d / 2f);
            if (sel) Theme.FillRound(g, Theme.Accent, RectangleF.Inflate(dot, -Theme.F(4), -Theme.F(4)), d / 2f);

            float tx = dot.Right + Theme.F(12), tw = r.Right - tx - Theme.F(12);
            string title = Cur.Options[i];
            string hint = Cur.Hints != null && i < Cur.Hints.Length ? Cur.Hints[i] : null;
            var tf = Theme.UiSemi(14f);
            bool small = r.Height <= Theme.Px(60);
            using (var b = new SolidBrush(Theme.Text))
            using (var hb = new SolidBrush(Theme.TextDim))
            {
                if (small)
                {
                    // eine Zeile: Titel links, Hinweis daneben
                    float tws = Math.Min(tw * 0.4f, Cur.Options.Max(o => Theme.TextWidth(g, o, tf)) + Theme.F(4));   // Hinweise in einer Spalte
                    Theme.Str(g, title, tf, b, new RectangleF(tx, r.Y + (r.Height - Theme.F(22)) / 2f, tws, Theme.F(24)), OneLine);
                    if (hint != null)
                        Theme.Str(g, hint, Theme.Ui(11.5f), hb, new RectangleF(tx + tws + Theme.F(14), r.Y + (r.Height - Theme.F(18)) / 2f, tw - tws - Theme.F(14), Theme.F(20)), OneLine);
                }
                else
                {
                    Theme.Str(g, title, tf, b, new RectangleF(tx, r.Y + Theme.F(11), tw, Theme.F(24)), OneLine);
                    if (hint != null)
                        Theme.Str(g, hint, Theme.Ui(11f), hb, new RectangleF(tx, r.Y + Theme.F(36), tw, r.Height - Theme.F(42)), Wrapped);
                }
            }
        }

        private void DrawDone(Graphics g)
        {
            int w = ClientSize.Width;
            float d = Theme.F(96);
            var c = new RectangleF((w - d) / 2f, Theme.F(200), d, d);
            Theme.FillRound(g, Color.FromArgb(40, Theme.LedGreen), c, d / 2f);
            Theme.StrokeRound(g, Theme.LedGreen, Theme.F(3), c, d / 2f);
            using (var p = new Pen(Theme.LedGreen, Theme.F(7)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(c.X + d * 0.28f, c.Y + d * 0.52f), new PointF(c.X + d * 0.44f, c.Y + d * 0.68f), new PointF(c.X + d * 0.73f, c.Y + d * 0.35f) });
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "made by " + Program.Author, Theme.Ui(11f), b, new RectangleF(0, c.Bottom + Theme.F(18), w, Theme.F(20)), Theme.Center);
        }

        private void DrawButton(Graphics g, Rectangle r, string text, int kind, bool hover)
        {
            float rad = Theme.F(6);
            if (kind == 2) Theme.FillRound(g, hover ? Theme.PrimaryHover : Theme.Primary, r, rad);
            else if (kind == 1) { Theme.FillRound(g, hover ? Theme.ButtonHover : Theme.Button, r, rad); }
            else if (hover) Theme.FillRound(g, Color.FromArgb(20, Theme.Text), r, rad);
            using (var b = new SolidBrush(kind == 2 ? Color.White : kind == 1 ? Theme.ButtonText : Theme.TextDim))
                Theme.Str(g, text, Theme.UiSemi(13f), b, r, Theme.Center);
        }

        // --------------------------------------------------------------- Bedienung

        private void Select(int i)
        {
            if (i < 0 || i >= (Cur.Options?.Length ?? 0) || i == Cur.Choice) return;
            Cur.Choice = i;
            try { Cur.Live?.Invoke(i); } catch { }
            Invalidate();
        }

        private void Go(int dir)
        {
            if (dir > 0 && Last) { DialogResult = DialogResult.OK; Close(); return; }
            _page = Math.Max(0, Math.Min(_steps.Count - 1, _page + dir));
            _hoverCard = -1;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hc = Array.FindIndex(_cards, r => r.Contains(e.Location));
            string hb = _next.Contains(e.Location) ? "next" : _back.Contains(e.Location) ? "back" : _skip.Contains(e.Location) ? "skip" : null;
            if (hc != _hoverCard || hb != _hoverBtn) { _hoverCard = hc; _hoverBtn = hb; Cursor = hc >= 0 || hb != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverCard >= 0 || _hoverBtn != null) { _hoverCard = -1; _hoverBtn = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int hc = Array.FindIndex(_cards, r => r.Contains(e.Location));
            if (hc >= 0) { Select(hc); return; }
            if (_next.Contains(e.Location)) Go(1);
            else if (_back.Contains(e.Location)) Go(-1);
            else if (_skip.Contains(e.Location)) { Skipped = true; DialogResult = DialogResult.Cancel; Close(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Array.FindIndex(_cards, r => r.Contains(e.Location)) >= 0) Go(1);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int n = Cur.Options?.Length ?? 0;
            int cols = n >= 7 ? 3 : (n == 2 || n == 4) ? 2 : 1;
            switch (keyData)
            {
                case Keys.Enter: Go(1); return true;
                case Keys.Escape: Skipped = true; DialogResult = DialogResult.Cancel; Close(); return true;
                case Keys.Back: Go(-1); return true;
                case Keys.Left: Select(Cur.Choice - 1); return true;
                case Keys.Right: Select(Cur.Choice + 1); return true;
                case Keys.Up: Select(Cur.Choice - cols); return true;
                case Keys.Down: Select(Cur.Choice + cols); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
