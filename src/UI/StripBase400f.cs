using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>4.0 (finale): Leuchtspur beim Fader-Ziehen, Ueberlagerung fuer die Lichtshow, Bild ueber den Zug.</summary>
    internal abstract partial class StripBase
    {
        /// <summary>Leuchtspur beim Ziehen (Einstellungen → Aussehen).</summary>
        public static bool TrailOn;
        /// <summary>Ueber jeden Zug zeichnen (Lichtshow) – null = nichts.</summary>
        public static Action<StripBase, Graphics> FinalOverlay;

        private const int TrailMax = 40;
        private const double TrailLife = 0.45;
        private readonly float[] _trailPos = new float[TrailMax];
        private readonly double[] _trailAt = new double[TrailMax];
        private int _trailHead, _trailCount;
        private static readonly System.Diagnostics.Stopwatch TrailClock = System.Diagnostics.Stopwatch.StartNew();

        private void AddTrail(float pos, double t)
        {
            _trailPos[_trailHead] = pos; _trailAt[_trailHead] = t;
            _trailHead = (_trailHead + 1) % TrailMax;
            if (_trailCount < TrailMax) _trailCount++;
        }

        /// <summary>Fader wurde von from nach to gezogen: Zwischenstellen merken (damit die Spur auch bei schnellen Bewegungen geschlossen ist).</summary>
        internal void NoteTrail400(float from, float to)
        {
            if (!TrailOn || Calm || CapH <= 0f) return;
            double t = TrailClock.Elapsed.TotalSeconds;
            float px = Math.Abs(PosToY(from) - PosToY(to));
            int steps = Math.Max(1, Math.Min(10, (int)(px / Math.Max(2f, CapH * 0.45f))));
            for (int i = 0; i < steps; i++) AddTrail(from + (to - from) * i / steps, t - (steps - i) * 0.004);
        }

        private bool TrailLive => _trailCount > 0 && TrailClock.Elapsed.TotalSeconds - _trailAt[(_trailHead - 1 + TrailMax) % TrailMax] < TrailLife + 0.05;

        private Rectangle TrailBounds => Rectangle.Ceiling(new RectangleF(SlotX - CapW / 2 - Theme.F(6), TravelTop - CapH, CapW + Theme.F(12), TravelBottom - TravelTop + CapH * 2));

        private void DrawTrail(Graphics g)
        {
            if (_trailCount == 0) return;
            double now = TrailClock.Elapsed.TotalSeconds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = AccentColor;
            for (int k = 0; k < _trailCount; k++)
            {
                int i = (_trailHead - 1 - k + TrailMax * 2) % TrailMax;
                double age = now - _trailAt[i];
                if (age < 0 || age > TrailLife) continue;
                float f = (float)(1 - age / TrailLife);
                int a = (int)(120 * f * f);
                if (a < 3) continue;
                float y = PosToY(_trailPos[i]);
                var r = new RectangleF(SlotX - CapW / 2 + Theme.F(2) * (1 - f), y - CapH / 2, CapW - Theme.F(4) * (1 - f), CapH);
                Theme.FillRound(g, Color.FromArgb(a / 3, c), RectangleF.Inflate(r, Theme.F(4), Theme.F(3)), Theme.F(5));
                Theme.FillRound(g, Color.FromArgb(a, Theme.Lighten(c, 0.25f)), r, Theme.F(3));
            }
            g.SmoothingMode = sm;
        }

        private bool _trailWasLive;

        private void PaintFinal400(Graphics g)
        {
            if (TrailOn && _trailCount > 0) DrawTrail(g);
            FinalOverlay?.Invoke(this, g);
            WaveOverlay?.Invoke(this, g);   // 4.2: RGB-Welle
        }

        /// <summary>Aus dem UI-Takt: Leuchtspur ausklingen lassen.</summary>
        public void TickFinal400(float dt)
        {
            bool live = TrailOn && TrailLive;
            if (live || _trailWasLive) Invalidate(TrailBounds);
            if (!live && _trailWasLive) _trailCount = 0;
            _trailWasLive = live;
            TickFinalExtra400(dt);
        }

        protected virtual void TickFinalExtra400(float dt) { }

        /// <summary>Nach dem Grund des Zugs (im Bild-Cache) – z. B. das Kanal-Bild.</summary>
        protected virtual void DrawPanelExtra400(Graphics g) { }
    }

    internal sealed partial class ChannelStrip
    {
        /// <summary>252: EQ- und Panorama-Knoepfe drehen beim Szenenwechsel sichtbar.</summary>
        public static bool MotorPots;
        /// <summary>229: Kanal-Bild blass ueber den ganzen Zug.</summary>
        public static bool ImageWash;

        private float[] _potFrom;
        private float _potT = -1f;
        private const float PotLen = 0.7f;

        /// <summary>Stellung von LO, MID, HI und Panorama (vor einem Szenenwechsel merken).</summary>
        public float[] PotValues => new[] { Config.EqLow, Config.EqMid, Config.EqHigh, Config.Pan };

        public void StartPots(float[] from)
        {
            if (!MotorPots || Calm || from == null || from.Length < 4 || !Visible) return;
            var to = PotValues;
            bool any = false;
            for (int i = 0; i < 4; i++) if (Math.Abs(to[i] - from[i]) > (i == 3 ? 0.01f : 0.1f)) any = true;
            if (!any) return;
            _potFrom = (float[])from.Clone();
            _potT = 0f;
            InvalidateChrome();
        }

        private float PotEase => _potT < 0f ? 1f : _potT * _potT * (3f - 2f * _potT);
        private float ShownEq(int i) => _potT < 0f || _potFrom == null ? GetEq(i) : _potFrom[i] + (GetEq(i) - _potFrom[i]) * PotEase;
        private float ShownPan => _potT < 0f || _potFrom == null ? Config.Pan : _potFrom[3] + (Config.Pan - _potFrom[3]) * PotEase;
        /// <summary>Gerade in Bewegung (fuer Tests).</summary>
        public bool PotsMoving => _potT >= 0f;

        protected override void TickFinalExtra400(float dt)
        {
            if (_potT < 0f) return;
            _potT += Math.Min(0.1f, dt) / PotLen;
            if (_potT >= 1f) { _potT = -1f; _potFrom = null; }
            InvalidateChrome();
        }

        protected override void DrawPanelExtra400(Graphics g)
        {
            if (!ImageWash || !ImagesOn || string.IsNullOrEmpty(Config.ImagePath)) return;
            var img = ChannelImages.Get(Config.ImagePath);
            if (img == null) return;
            var r = RectangleF.Inflate(PanelRect, -Theme.F(2), -Theme.F(2));
            var st = g.Save();
            using (var path = Theme.Round(r, Theme.PanelRad)) g.SetClip(path, CombineMode.Intersect);
            float k = Math.Max(r.Width / img.Width, r.Height / img.Height);
            float w = img.Width * k, h = img.Height * k;
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = Dimmed ? 0.08f : 0.16f };
                ia.SetColorMatrix(cm);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, Rectangle.Round(new RectangleF(r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h)), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
            g.Restore(st);
        }
    }
}
