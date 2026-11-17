using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>3.5 Spickzettel (F1): alle Maus-Griffe, Tasten und Hotkeys auf einen Blick. Taste, Klick oder Wegklicken schliesst.</summary>
    internal sealed class CheatSheet : Form
    {
        private List<KeyValuePair<string, List<string>>> _blocks = new List<KeyValuePair<string, List<string>>>();

        public CheatSheet()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            KeyDown += (s, e) => Hide();
            MouseDown += (s, e) => Hide();
            Deactivate += (s, e) => Hide();
        }

        public int RowCount { get; private set; }

        public void ShowFor(Form owner, List<KeyValuePair<string, List<string>>> blocks)
        {
            _blocks = blocks ?? new List<KeyValuePair<string, List<string>>>();
            int rows = 0;
            foreach (var b in _blocks) rows += 1 + b.Value.Count;
            RowCount = rows;
            int w = Theme.Px(640), h = Theme.Px(56) + (int)(rows * Theme.F(19)) + _blocks.Count * Theme.Px(10);
            var ob = owner.Bounds;
            var wa = Screen.FromControl(owner).WorkingArea;
            h = Math.Min(h, wa.Height - Theme.Px(20));
            Bounds = new Rectangle(Math.Max(wa.X, Math.Min(wa.Right - w, ob.X + (ob.Width - w) / 2)), Math.Max(wa.Y, Math.Min(wa.Bottom - h, ob.Y + (ob.Height - h) / 2)), w, h);
            using (var p = Theme.Round(new RectangleF(0, 0, w, h), Theme.F(12))) Region = new Region(p);
            if (!Visible) Show(owner);
            Activate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            Theme.StrokeRound(g, Theme.MenuBorder, Theme.F(1), new RectangleF(0, 0, Width - 1, Height - 1), Theme.F(12));
            using (var b = new SolidBrush(Theme.Text))
                Theme.Str(g, "Spickzettel", Theme.Ui(15f, FontStyle.Bold), b, new RectangleF(Theme.F(18), Theme.F(10), Width - Theme.F(36), Theme.F(26)), Theme.Left);
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "Taste oder Klick schließt", Theme.Ui(9.5f), b, new RectangleF(Theme.F(18), Theme.F(14), Width - Theme.F(36), Theme.F(20)), Theme.Right);
            float y = Theme.F(44), keyW = Theme.F(230);
            foreach (var block in _blocks)
            {
                Theme.Label(g, block.Key, new RectangleF(Theme.F(18), y, Width - Theme.F(36), Theme.F(16)), Theme.Left, Theme.Accent, 9.5f);
                y += Theme.F(19);
                foreach (var line in block.Value)
                {
                    int bar = line.IndexOf('|');
                    string k = bar >= 0 ? line.Substring(0, bar) : line, v = bar >= 0 ? line.Substring(bar + 1) : "";
                    using (var b = new SolidBrush(Theme.Text))
                        Theme.Str(g, k, Theme.Ui(10.5f, FontStyle.Bold), b, new RectangleF(Theme.F(26), y, keyW, Theme.F(18)), Theme.Left);
                    using (var b = new SolidBrush(Theme.TextDim))
                        Theme.Str(g, v, Theme.Ui(10.5f), b, new RectangleF(Theme.F(26) + keyW, y, Width - keyW - Theme.F(44), Theme.F(18)), Theme.Left);
                    y += Theme.F(19);
                    if (y > Height - Theme.F(20)) return;
                }
                y += Theme.F(10);
            }
        }
    }
}
