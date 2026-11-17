using System;

namespace Mischpult.UI
{
    internal abstract partial class StripBase
    {
        /// <summary>3.3: Zeiger-Tempo nach Instrument – VU traege, Spitzenpegel schnell, Magisches Auge dazwischen.</summary>
        public static float VuOmega => Theme.VuKind == 1 || Theme.VuKind == 2 ? 45f : Theme.VuKind == 3 ? 30f : 15f;

        /// <summary>Ruecklauf: eingestellte Zeit, beim Spitzenpegel-Instrument mindestens 1,2 s (wie im Rundfunk).</summary>
        public static float VuTau()
        {
            float tau = VuFallTau[Math.Max(0, Math.Min(VuFallTau.Length - 1, VuFall))];
            if (Theme.VuKind == 1 || Theme.VuKind == 2) tau = Math.Max(tau, 1.2f);
            return tau;
        }
    }
}

namespace Mischpult.UI
{
    using System.Drawing;
    using System.Drawing.Drawing2D;

    internal abstract partial class StripBase
    {
        /// <summary>3.3: Faderkappe leuchtet in der Kanalfarbe – heller, je lauter der Kanal gerade ist.</summary>
        public static bool CapGlowOn;

        private RectangleF CapNow()
        {
            float pos = LiveFaderPos >= 0f ? LiveFaderPos : ShownFaderPos;
            float capY = PosToY(pos);
            return new RectangleF(SlotX - CapW / 2, capY - CapH / 2, CapW, CapH);
        }

        private Rectangle CapGlowBounds => SlotRect.IsEmpty ? Rectangle.Empty : Rectangle.Ceiling(RectangleF.Inflate(CapNow(), Theme.F(7), Theme.F(7)));

        private void DrawCapGlow(Graphics g)
        {
            if (SlotRect.IsEmpty) return;
            var cap = CapNow();
            float lv = Math.Max(0f, Math.Min(1f, _faderGlow));
            Color c = MasterCap ? Color.FromArgb(0xff, 0x5a, 0x48) : (Dimmed ? Theme.Darken(MeterTint, 0.4f) : MeterTint);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Schein um die Kappe
            if (lv > 0.03f)
                using (var path = new GraphicsPath())
                {
                    var halo = RectangleF.Inflate(cap, Theme.F(6), Theme.F(6));
                    path.AddEllipse(halo);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(110 * lv), c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                        g.FillPath(pb, path);
                }
            // leuchtender Streifen in der Mitte (wie eine beleuchtete Kappe)
            float cy = cap.Y + cap.Height / 2f;
            float h = Math.Max(2f, Theme.F(3));
            var strip = new RectangleF(cap.X + Theme.F(2), cy - h / 2f, cap.Width - Theme.F(4), h);
            using (var b = new SolidBrush(Color.FromArgb((int)(60 + 195 * lv), Theme.Lighten(c, 0.15f + 0.35f * lv))))
                Theme.FillRound(g, b, strip, h / 2f);
            g.SmoothingMode = sm;
        }
    }
}

namespace Mischpult.UI
{
    using System.Drawing;
    using System.Drawing.Drawing2D;

    internal abstract partial class StripBase
    {
        // ---- Ausschalt-Show
        /// <summary>3.3: Ausschalt-Show laeuft (Sekunden seit dem Start), sonst -1.</summary>
        public static float PowerOffT = -1f;

        // ---- Gebrauchsspuren und Roehren
        /// <summary>Immer gleiche Kratzer fuer denselben Zug.</summary>
        protected virtual string WearKey => GetType().Name;
        private int WearSeed
        {
            get
            {
                unchecked
                {
                    uint h = 2166136261;
                    foreach (char ch in WearKey ?? "") { h ^= ch; h *= 16777619; }
                    return (int)(h & 0x7fffffff);
                }
            }
        }

        /// <summary>Wie laut der Zug gerade ist (0..1, wie die Pegel).</summary>
        public float LevelNow => Math.Max(Meter.FracL, Meter.FracR);

        // ---- Funken bei Uebersteuerung
        public static bool SparksOn;
        private const int SparkMax = 14;
        private readonly float[] _spX = new float[SparkMax], _spY = new float[SparkMax], _spVx = new float[SparkMax], _spVy = new float[SparkMax], _spLife = new float[SparkMax];
        private int _sparkN;
        private static readonly Random SparkRnd = new Random();

        private RectangleF SparkSource => PeakDb ? PeakRect : ClipRect;
        private Rectangle SparkBounds => Rectangle.Ceiling(RectangleF.Inflate(SparkSource, Theme.F(26), Theme.F(22)));

        /// <summary>Nur Vorschau: Funken vorfuehren.</summary>
        public void DemoSparks() => SpawnSparks();

        private void SpawnSparks()
        {
            var src = SparkSource;
            if (src.IsEmpty) return;
            float cx = src.X + src.Width / 2f, cy = src.Y + src.Height / 2f;
            _sparkN = SparkMax;
            for (int i = 0; i < SparkMax; i++)
            {
                double a = -Math.PI / 2 + (SparkRnd.NextDouble() - 0.5) * Math.PI * 1.3;
                float sp = Theme.F(60f + (float)SparkRnd.NextDouble() * 90f);
                _spX[i] = cx; _spY[i] = cy;
                _spVx[i] = (float)Math.Cos(a) * sp; _spVy[i] = (float)Math.Sin(a) * sp;
                _spLife[i] = 0.35f + (float)SparkRnd.NextDouble() * 0.3f;
            }
            Invalidate(SparkBounds);
        }

        private void TickSparks(float dt)
        {
            if (dt <= 0f) return;
            dt = Math.Min(dt, 0.05f);
            int alive = 0;
            for (int i = 0; i < SparkMax; i++)
            {
                if (_spLife[i] <= 0f) continue;
                _spLife[i] -= dt;
                _spVy[i] += Theme.F(260f) * dt;   // fallen
                _spX[i] += _spVx[i] * dt; _spY[i] += _spVy[i] * dt;
                if (_spLife[i] > 0f) alive++;
            }
            var b = SparkBounds;
            if (alive == 0) _sparkN = 0;
            Invalidate(b);
        }

        private void DrawSparks(Graphics g)
        {
            var bounds = (RectangleF)SparkBounds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            g.SetClip(bounds, CombineMode.Intersect);
            for (int i = 0; i < SparkMax; i++)
            {
                float life = _spLife[i];
                if (life <= 0f) continue;
                float f = Math.Min(1f, life / 0.35f);
                var c = Theme.Mix(Color.FromArgb(0xff, 0x40, 0x10), Color.FromArgb(0xff, 0xf0, 0xa0), f);
                float len = 0.02f;
                using (var pen = new Pen(Color.FromArgb((int)(255 * f), c), Math.Max(1f, Theme.F(1.4f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, _spX[i], _spY[i], _spX[i] - _spVx[i] * len, _spY[i] - _spVy[i] * len);
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
