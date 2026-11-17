using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.3: Hinweise (Tooltips) im Pult-Design – dunkles Kaestchen mit Akzent-Kante statt des Windows-Gelbs.
    /// Aus = Windows zeichnet die Hinweise wie immer.
    /// </summary>
    internal static class PultTip
    {
        private static bool _on;
        private static readonly List<WeakReference> Tips = new List<WeakReference>();

        public static bool On
        {
            get => _on;
            set
            {
                if (_on == value) return;
                _on = value;
                lock (Tips)
                    for (int i = Tips.Count - 1; i >= 0; i--)
                    {
                        if (Tips[i].Target is ToolTip t) { try { t.OwnerDraw = value; } catch { } }
                        else Tips.RemoveAt(i);
                    }
            }
        }

        /// <summary>Hinweis anmelden (zeichnet im Pult-Design, solange eingeschaltet).</summary>
        public static ToolTip Make(ToolTip t)
        {
            t.Popup += OnPopup;
            t.Draw += OnDraw;
            t.OwnerDraw = _on;
            lock (Tips) Tips.Add(new WeakReference(t));
            return t;
        }

        private static void OnPopup(object sender, PopupEventArgs e)
        {
            if (!_on) return;
            e.ToolTipSize = new Size(e.ToolTipSize.Width + Theme.Px(12), e.ToolTipSize.Height + Theme.Px(8));
        }

        private static void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            var g = e.Graphics;
            var r = e.Bounds;
            if (!_on) { e.DrawBackground(); e.DrawBorder(); e.DrawText(); return; }
            using (var b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0x2a, 0x2d, 0x33), Color.FromArgb(0x17, 0x19, 0x1c), LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y, Math.Max(2, Theme.Px(3)), r.Height);
            using (var pen = new Pen(Color.FromArgb(0x05, 0x05, 0x06))) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(pen, r.X + Theme.Px(3), r.Y + 1, r.Right - 2, r.Y + 1);
            var tr = new Rectangle(r.X + Theme.Px(9), r.Y + Theme.Px(4), r.Width - Theme.Px(12), r.Height - Theme.Px(6));
            TextRenderer.DrawText(g, e.ToolTipText, e.Font, tr, Color.FromArgb(0xe8, 0xea, 0xee),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
