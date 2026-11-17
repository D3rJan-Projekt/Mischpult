using System;
using System.Collections.Generic;

namespace Mischpult.UI
{
    /// <summary>3.4.6: Fader rasten an den Skalen-Strichen ein, feste Mausrad-Schritte.</summary>
    internal abstract partial class StripBase
    {
        /// <summary>122: beim Ziehen an den Skalen-Strichen (0, −5, −10 … dB) kurz einrasten. Shift = frei.</summary>
        public static bool SnapOn;
        /// <summary>123: ein Mausrad-Schritt in dB (0 = wie bisher).</summary>
        public static float WheelDb;
        /// <summary>123: ein Mausrad-Schritt in Prozent fuer Fader, die in % gehen (0 = wie bisher).</summary>
        public static float WheelPct;

        /// <summary>Geht dieser Fader nach der dB-Kennlinie (sonst Prozent)?</summary>
        protected virtual bool DbFader => false;

        /// <summary>Wie nah (in Faderweg 0..1) eine Raststelle ziehen muss: etwa 5 Pixel.</summary>
        private float SnapRange => Math.Max(0.004f, Theme.F(5) / Math.Max(10f, TravelBottom - TravelTop));

        /// <summary>Raststellen: Skalen-Striche, die Fader-Marke und die Grundstellung.</summary>
        internal float SnapPos(float p, float range)
        {
            if (!SnapOn) return p;
            float best = p, bd = range;
            void Try(float m)
            {
                if (float.IsNaN(m) || m < 0f || m > 1f) return;
                float d = Math.Abs(m - p);
                if (d < bd) { bd = d; best = m; }
            }
            foreach (var m in ScaleMarks) Try(m.Key);
            Try(FaderMarkPos);
            Try(DefaultFaderPos);
            return best;
        }

        private float DragSnap(float p, bool fine) => fine ? p : SnapPos(p, SnapRange);

        /// <summary>Neue Stellung nach einem Mausrad-Schritt mit fester Schrittweite (springt auf das naechste Raster).</summary>
        internal float WheelTarget(float pos, int delta, bool ctrl)
        {
            int ticks = delta / 120;
            if (ticks == 0) ticks = Math.Sign(delta);
            if (ticks == 0) return pos;
            if (DbFader)
            {
                float step = WheelDb * (ctrl ? 0.5f : 1f);
                return Clamp(DbWheel(pos, ticks, step));
            }
            float ps = WheelPct * (ctrl ? 0.5f : 1f) / 100f;
            if (ps <= 0f) return pos;
            double k = ticks > 0 ? Math.Floor(pos / ps + 1e-4) + ticks : Math.Ceiling(pos / ps - 1e-4) + ticks;
            return Clamp((float)(k * ps));
        }

        internal static float DbWheel(float pos, int ticks, float step)
        {
            if (step <= 0f) return pos;
            float db = FaderLaw.PosToDb(pos);
            const float bottom = -60f;
            if (float.IsNegativeInfinity(db) || db < bottom)
            {
                if (ticks < 0) return 0f;
                db = bottom - step;   // von ganz unten: erster Schritt landet bei −60 dB
                ticks = 1;
            }
            double k = ticks > 0 ? Math.Floor(db / step + 1e-4) + ticks : Math.Ceiling(db / step - 1e-4) + ticks;
            double nd = k * step;
            if (nd >= 0) return 1f;
            if (nd < bottom - 1e-4) return 0f;
            return FaderLaw.DbToPos((float)nd);
        }
    }
}
