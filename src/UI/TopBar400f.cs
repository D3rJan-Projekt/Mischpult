using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>4.0 (finale): Song-Fortschritt unten im "Jetzt laeuft"-Feld – Klick springt an die Stelle.</summary>
    internal sealed partial class TopBar
    {
        /// <summary>Anteil des Songs (0..1), -1 = keine Anzeige.</summary>
        public float SongFrac = -1f;
        /// <summary>"1:23 / −2:10" (Tooltip).</summary>
        public string SongTimes;
        public event Action<float> SongSeek;

        private RectangleF SongBarRect => new RectangleF(_npRect.X + Theme.F(20), _npRect.Bottom - Theme.F(5), _npRect.Width - Theme.F(26), Theme.F(3));

        private bool SongBarHit400(Point p) =>
            SongFrac >= 0f && !_npRect.IsEmpty && p.X >= _npRect.X + Theme.F(16) && p.X <= _npRect.Right - Theme.F(4) && p.Y >= _npRect.Bottom - Theme.F(8) && p.Y <= _npRect.Bottom + Theme.F(2);

        private float SongFracAt400(int x)
        {
            var r = SongBarRect;
            return Math.Max(0f, Math.Min(1f, (x - r.X) / Math.Max(1f, r.Width)));
        }

        private void DrawSongBar400(Graphics g, Color c)
        {
            if (SongFrac < 0f || _npRect.IsEmpty) return;
            var r = SongBarRect;
            using (var b = new SolidBrush(Theme.Alpha(c, 50))) g.FillRectangle(b, r);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r.X, r.Y, r.Width * Math.Max(0f, Math.Min(1f, SongFrac)), r.Height);
        }

        /// <summary>Neuer Stand (nur neu zeichnen, wenn sich etwas sichtbar aendert).</summary>
        public void SetSong400(float frac, string times)
        {
            int q0 = SongFrac < 0f ? -1 : (int)(SongFrac * 400), q1 = frac < 0f ? -1 : (int)(frac * 400);
            SongTimes = times;
            if (q0 == q1) return;
            SongFrac = frac;
            if (!_npRect.IsEmpty) Invalidate(Rectangle.Ceiling(RectangleF.Inflate(_npRect, 2, 2)));
        }
    }
}
