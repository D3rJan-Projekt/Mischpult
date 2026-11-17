using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.3: Symbol im Namensschild, Notizzettel und Gruppen-Klammer.</summary>
    internal sealed partial class ChannelStrip
    {
        public static bool SymbolsOn, NotesOn, GroupBracketsOn;
        private RectangleF _noteRect;

        /// <summary>Symbol dieses Kanals (0 = keins).</summary>
        public int SymbolNow
        {
            get
            {
                if (!SymbolsOn || Config.Symbol == 99) return 0;
                if (Config.Symbol > 0) return Config.Symbol;
                int s = Theme.SymbolFor(Config.Name);
                if (s == 0 && Apps.Count > 0) s = Theme.SymbolFor(Apps[0].Name);
                return s;
            }
        }

        private bool NoteShown => NotesOn && !string.IsNullOrEmpty(Config.Note);

        protected override string WearKey => "ch:" + Config.Id;

        private void LayoutTop330()
        {
            _noteRect = NoteShown ? new RectangleF(_nameRect.Right - Theme.F(12), Math.Max(0f, _nameRect.Y - Theme.F(11)), Theme.F(15), Theme.F(14)) : RectangleF.Empty;
            if (!_noteRect.IsEmpty) AddZone(_noteRect, "note");
        }

        private void DrawSymbol330(Graphics g, RectangleF nr, Color bg, ref RectangleF textRect, ref float fitWidth)
        {
            int sym = SymbolNow;
            if (sym <= 0) return;
            float s = Math.Min(Theme.F(14), nr.Height - Theme.F(8));
            if (s < 6 || nr.Width < Theme.F(60)) return;
            var sr = new RectangleF((float)Math.Round(nr.Right - Theme.F(5) - s), (float)Math.Round(nr.Y + (nr.Height - s) / 2f), s, s);
            Theme.DrawSymbol(g, sr, sym, Theme.Alpha(Theme.PlateText(bg), 215));
            float cut = s + Theme.F(3);
            textRect = new RectangleF(textRect.X, textRect.Y, Math.Max(Theme.F(10), textRect.Width - cut), textRect.Height);
            fitWidth = Math.Max(Theme.F(8), fitWidth - cut);
        }

        private void DrawNote330(Graphics g)
        {
            if (!NoteShown || _noteRect.IsEmpty) return;
            var r = _noteRect;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.RotateTransform(7f);
            var p = new RectangleF(-r.Width / 2f, -r.Height / 2f, r.Width, r.Height);
            using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(b, p.X + Theme.F(1), p.Y + Theme.F(1.5f), p.Width, p.Height);
            float fold = Theme.F(4);
            var paper = new[] { new PointF(p.X, p.Y), new PointF(p.Right, p.Y), new PointF(p.Right, p.Bottom - fold), new PointF(p.Right - fold, p.Bottom), new PointF(p.X, p.Bottom) };
            using (var b = new LinearGradientBrush(new RectangleF(p.X, p.Y - 1, p.Width, p.Height + 2), Color.FromArgb(0xff, 0xf0, 0x7a), Color.FromArgb(0xf2, 0xd6, 0x3c), LinearGradientMode.Vertical))
                g.FillPolygon(b, paper);
            using (var b = new SolidBrush(Color.FromArgb(0xc8, 0xa8, 0x22)))
                g.FillPolygon(b, new[] { new PointF(p.Right, p.Bottom - fold), new PointF(p.Right - fold, p.Bottom - fold), new PointF(p.Right - fold, p.Bottom) });
            using (var pen = new Pen(Color.FromArgb(120, 0x6a, 0x5a, 0x20), Math.Max(1f, Theme.F(0.8f))))
                for (int i = 0; i < 3; i++)
                {
                    float y = p.Y + Theme.F(4) + i * Theme.F(3);
                    g.DrawLine(pen, p.X + Theme.F(2.5f), y, p.Right - Theme.F(i == 2 ? 7 : 3), y);
                }
            g.Restore(st);
        }

        private void DrawGroupBracket330(Graphics g)
        {
            if (!GroupBracketsOn) return;
            if (!Mixer.GroupBracketOf(Config, out Color col, out bool first, out bool last)) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = PanelRect.Y + Theme.F(7f), th = Math.Max(2f, Theme.F(3));
            float x0 = first ? PanelRect.X + Theme.F(6) : 0f, x1 = last ? PanelRect.Right - Theme.F(6) : Width;
            using (var glow = new Pen(Theme.Alpha(col, Dimmed ? 30 : 60), th * 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(glow, x0, y, x1, y);
            using (var pen = new Pen(Theme.Alpha(col, Dimmed ? 140 : 240), th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, x0, y, x1, y);
                if (first) g.DrawLine(pen, x0, y, x0, y + Theme.F(4));
                if (last) g.DrawLine(pen, x1, y, x1, y + Theme.F(4));
            }
            g.SmoothingMode = sm;
        }
    }
}
