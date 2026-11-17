using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.4: Aufkleber auf den Zuegen und Auswahl per Tastatur.</summary>
    internal abstract partial class StripBase
    {
        // ---- Aufkleber
        public static bool StickersOn;
        public static List<StickerConfig> Stickers;
        /// <summary>Wem die Aufkleber auf diesem Zug gehoeren (Kanal-ID, "master", "out:&lt;Geraet&gt;"; null = keine).</summary>
        public virtual string StickerOwner => null;
        public static float StickerSize => Theme.F(30);

        private void DrawStickers340(Graphics g)
        {
            var list = Stickers;
            string owner = StickerOwner;
            if (!StickersOn || list == null || owner == null) return;
            foreach (var k in list)
                if (k != null && k.Owner == owner)
                    Theme.DrawSticker(g, k.Kind, new PointF(k.X * Theme.S, k.Y * Theme.S), StickerSize, k.Angle);
        }

        /// <summary>Aufkleber unter dieser Stelle (eigene Koordinaten) oder null.</summary>
        public StickerConfig StickerAt(Point p)
        {
            var list = Stickers;
            string owner = StickerOwner;
            if (list == null || owner == null) return null;
            float r = StickerSize * 0.6f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var k = list[i];
                if (k == null || k.Owner != owner) continue;
                float dx = p.X - k.X * Theme.S, dy = p.Y - k.Y * Theme.S;
                if (dx * dx + dy * dy <= r * r) return k;
            }
            return null;
        }

        /// <summary>3.4: zweite Farbe fuer die Faderkappe (leer = keine).</summary>
        protected virtual Color CapColor2 => Color.Empty;

        // ---- Auswahl per Tastatur
        private static StripBase _keyFocus;
        /// <summary>Zug, der per Tastatur gewaehlt ist (Rahmen in der Akzentfarbe), oder null.</summary>
        public static StripBase KeyFocus
        {
            get => _keyFocus;
            set
            {
                if (ReferenceEquals(_keyFocus, value)) return;
                var old = _keyFocus;
                _keyFocus = value;
                try { old?.Invalidate(); } catch { }
                try { value?.Invalidate(); } catch { }
                KeyFocusChanged?.Invoke(value);   // 4.0: Bildschirmleser
            }
        }
        /// <summary>4.0: Auswahl per Tastatur hat gewechselt.</summary>
        public static event System.Action<StripBase> KeyFocusChanged;

        private void DrawKeyFocus(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = RectangleF.Inflate(PanelRect, -Theme.F(1), -Theme.F(1));
            if (A11yFocus)   // 4.0: deutlicher Rahmen (weiss und Akzent, dick)
            {
                Theme.StrokeRound(g, Theme.IsLight ? Color.Black : Color.White, Theme.F(4f), RectangleF.Inflate(r, -Theme.F(1.5f), -Theme.F(1.5f)), Theme.PanelRad);
                Theme.StrokeRound(g, Theme.Accent, Theme.F(2.5f), RectangleF.Inflate(r, -Theme.F(1.5f), -Theme.F(1.5f)), Theme.PanelRad);
                g.SmoothingMode = sm;
                return;
            }
            for (int i = 3; i >= 1; i--) Theme.StrokeRound(g, Theme.Alpha(Theme.Accent, 30), Theme.F(1.4f * i), r, Theme.PanelRad);
            Theme.StrokeRound(g, Theme.Lighten(Theme.Accent, 0.15f), Theme.F(1.6f), r, Theme.PanelRad);
            g.SmoothingMode = sm;
        }
    }
}
