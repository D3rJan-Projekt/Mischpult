using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>2.20: Design-Editor – eigene Farben fuer Pult-Grund, Zuege, Tasten, Anzeigen, Schrift, Akzent, Kopfleiste und Menues.</summary>
    internal sealed class DesignEditor : Form
    {
        /// <summary>Farbe setzen (null = wie im Design).</summary>
        public Action<string, Color?> SetSlot;
        /// <summary>Ist der Bereich eigen eingestellt?</summary>
        public Func<string, bool> IsCustom;
        public Action SaveAs, ResetAll;
        public Action<Point> LoadMenu;

        private readonly Rectangle[] _sw = new Rectangle[Theme.CustomSlots.Length];
        private readonly Rectangle[][] _pal = new Rectangle[Theme.CustomSlots.Length][];
        private readonly Rectangle[] _own = new Rectangle[Theme.CustomSlots.Length], _reset = new Rectangle[Theme.CustomSlots.Length];
        private Rectangle _save, _load, _resetAll, _close;
        private string _hover;

        public DesignEditor()
        {
            Text = "Design-Editor – Mischpult";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(Theme.Px(700), Theme.Px(560));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        /// <summary>Farbe, die der Bereich gerade hat.</summary>
        public static Color Current(string slot)
        {
            switch (slot)
            {
                case "body": return Theme.Body;
                case "panel": return Theme.PanelBottom;
                case "keys": return Theme.KeyTop;
                case "disp": return Theme.DispBg;
                case "text": return Theme.Text;
                case "accent": return Theme.Accent;
                case "bar": return Theme.BarBg;
                default: return Theme.MenuBg;
            }
        }

        private void Layout2()
        {
            int x0 = Theme.Px(24), y0 = Theme.Px(86), rowH = Theme.Px(46);
            int sw = Theme.Px(22), gap = Theme.Px(5);
            for (int i = 0; i < Theme.CustomSlots.Length; i++)
            {
                int y = y0 + i * rowH;
                _sw[i] = new Rectangle(x0 + Theme.Px(178), y + Theme.Px(6), Theme.Px(44), Theme.Px(28));
                var pal = Theme.CustomPalette(Theme.CustomSlots[i]);
                _pal[i] = new Rectangle[pal.Length];
                for (int k = 0; k < pal.Length; k++) _pal[i][k] = new Rectangle(_sw[i].Right + Theme.Px(14) + k * (sw + gap), y + Theme.Px(9), sw, sw);
                int bx = _pal[i][pal.Length - 1].Right + Theme.Px(12);
                _own[i] = new Rectangle(bx, y + Theme.Px(7), Theme.Px(78), Theme.Px(26));
                _reset[i] = new Rectangle(_own[i].Right + Theme.Px(6), y + Theme.Px(7), Theme.Px(28), Theme.Px(26));
            }
            int by = ClientSize.Height - Theme.Px(52), bh = Theme.Px(32);
            _save = new Rectangle(x0, by, Theme.Px(150), bh);
            _load = new Rectangle(_save.Right + Theme.Px(8), by, Theme.Px(150), bh);
            _resetAll = new Rectangle(_load.Right + Theme.Px(8), by, Theme.Px(150), bh);
            _close = new Rectangle(ClientSize.Width - x0 - Theme.Px(120), by, Theme.Px(120), bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Layout2();
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            int x0 = Theme.Px(24);
            using (var b = new SolidBrush(Theme.Text)) Theme.Str(g, "Eigene Farben", Theme.UiSemi(20f), b, x0, Theme.F(16));
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "Klick auf eine Farbe ändert sie sofort am ganzen Pult. ↺ = wieder wie im Design. Die Kanalfarben bleiben.", Theme.Ui(11.5f), b,
                    new RectangleF(x0, Theme.F(52), ClientSize.Width - 2 * x0, Theme.F(24)));
            for (int i = 0; i < Theme.CustomSlots.Length; i++)
            {
                string slot = Theme.CustomSlots[i];
                bool own = IsCustom?.Invoke(slot) ?? false;
                using (var b = new SolidBrush(Theme.Text)) Theme.Str(g, Theme.CustomSlotNames[i], Theme.UiSemi(12.5f), b, x0, _sw[i].Y + Theme.F(2));
                using (var b = new SolidBrush(own ? Theme.Accent : Theme.TextFaint)) Theme.Str(g, own ? "eigene Farbe" : "wie im Design", Theme.Ui(10f), b, x0, _sw[i].Y + Theme.F(18));
                var cur = Current(slot);
                Theme.FillRound(g, cur, _sw[i], Theme.F(5));
                Theme.StrokeRound(g, _hover == "sw" + i ? Theme.Text : Color.FromArgb(120, Theme.Text), 1f, _sw[i], Theme.F(5));
                var pal = Theme.CustomPalette(slot);
                for (int k = 0; k < pal.Length; k++)
                {
                    var r = _pal[i][k];
                    Theme.FillRound(g, pal[k], r, Theme.F(4));
                    bool sel = own && pal[k].ToArgb() == cur.ToArgb();
                    Theme.StrokeRound(g, sel ? Theme.Accent : _hover == "p" + i + "_" + k ? Theme.Text : Color.FromArgb(70, Theme.Text), sel ? Theme.F(2) : 1f, r, Theme.F(4));
                }
                DrawBtn(g, _own[i], "Eigene …", _hover == "own" + i, false);
                DrawBtn(g, _reset[i], "↺", _hover == "res" + i, false, !own);
            }
            // Lesbarkeit pruefen
            double k1 = Theme.Contrast(Theme.Text, Theme.PanelBottom), k2 = Theme.Contrast(Theme.Text, Theme.MenuBg);
            string warn = k1 < 4.5 ? "Achtung: Schrift auf den Zügen schwer lesbar (Kontrast " + k1.ToString("0.0") + " – gut ist ab 4,5)."
                        : k2 < 4.5 ? "Achtung: Schrift in Menüs schwer lesbar (Kontrast " + k2.ToString("0.0") + ")." : "Lesbarkeit: gut (Kontrast " + Math.Min(k1, k2).ToString("0.0") + ").";
            using (var b = new SolidBrush(k1 < 4.5 || k2 < 4.5 ? Color.FromArgb(0xff, 0x8a, 0x5c) : Theme.TextDim))
                Theme.Str(g, warn, Theme.Ui(11f), b, x0, _save.Y - Theme.F(30));
            DrawBtn(g, _save, "Speichern als …", _hover == "save", false);
            DrawBtn(g, _load, "Eigene Designs ▾", _hover == "load", false);
            DrawBtn(g, _resetAll, "Alles wie im Design", _hover == "resall", false);
            DrawBtn(g, _close, "Schließen", _hover == "close", true);
        }

        private static void DrawBtn(Graphics g, Rectangle r, string text, bool hover, bool primary, bool disabled = false)
        {
            Theme.FillRound(g, primary ? (hover ? Theme.PrimaryHover : Theme.Primary) : (hover && !disabled ? Theme.ButtonHover : Theme.Button), r, Theme.F(5));
            using (var b = new SolidBrush(disabled ? Color.FromArgb(90, Theme.ButtonText) : primary ? Color.White : Theme.ButtonText))
                Theme.Str(g, text, Theme.UiSemi(11.5f), b, r, Theme.Center);
        }

        private string HitTest(Point p)
        {
            for (int i = 0; i < Theme.CustomSlots.Length; i++)
            {
                if (_sw[i].Contains(p)) return "sw" + i;
                if (_own[i].Contains(p)) return "own" + i;
                if (_reset[i].Contains(p)) return "res" + i;
                if (_pal[i] != null) for (int k = 0; k < _pal[i].Length; k++) if (_pal[i][k].Contains(p)) return "p" + i + "_" + k;
            }
            if (_save.Contains(p)) return "save";
            if (_load.Contains(p)) return "load";
            if (_resetAll.Contains(p)) return "resall";
            if (_close.Contains(p)) return "close";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            ClickZone(HitTest(e.Location));
        }

        /// <summary>Klick auf einen Bereich (auch fuer Tests).</summary>
        public void ClickZone(string h)
        {
            if (h == null) return;
            if (h == "close") { Close(); return; }
            if (h == "save") { SaveAs?.Invoke(); Invalidate(); return; }
            if (h == "load") { LoadMenu?.Invoke(PointToScreen(new Point(_load.X, _load.Bottom + Theme.Px(2)))); return; }
            if (h == "resall") { ResetAll?.Invoke(); Invalidate(); return; }
            if (h.StartsWith("res")) { SetSlot?.Invoke(Theme.CustomSlots[int.Parse(h.Substring(3))], null); Invalidate(); return; }
            if (h.StartsWith("own") || h.StartsWith("sw"))
            {
                int i = int.Parse(h.Substring(h.StartsWith("own") ? 3 : 2));
                using (var dlg = new ColorDialog { Color = Current(Theme.CustomSlots[i]), FullOpen = true, AnyColor = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) SetSlot?.Invoke(Theme.CustomSlots[i], Color.FromArgb(255, dlg.Color));
                Invalidate();
                return;
            }
            if (h.StartsWith("p"))
            {
                var parts = h.Substring(1).Split('_');
                int i = int.Parse(parts[0]), k = int.Parse(parts[1]);
                SetSlot?.Invoke(Theme.CustomSlots[i], Theme.CustomPalette(Theme.CustomSlots[i])[k]);
                Invalidate();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
