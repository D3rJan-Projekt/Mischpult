using System;
using System.Drawing;

namespace Mischpult.UI
{
    internal sealed partial class MeterBridge
    {
        // ---- 4.0: Lautheits-Radar (gleich gebaut wie die Roehren)
        public static bool RadarOn;
        private readonly RadarView _radarV = new RadarView();
        private RectangleF _radarRect;
        private float _radarW;
        private int _radarVer = -1;
        private double _radarLast;
        public bool RadarShown => !_radarRect.IsEmpty;

        private float RadarDt()
        {
            double t = _pcClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.25, t - _radarLast);
            _radarLast = t;
            return dt;
        }

        private void PlaceRadar(float x)
        {
            float pad = Theme.F(6), top = (float)Math.Round(Theme.F(5)), hh = (float)Math.Round(Height - Theme.F(10));
            var r = new RectangleF((float)Math.Round(x + Theme.F(3)), top, _radarW, hh);
            AddItem("#r", new RectangleF(x, top, _radarW + pad, hh));
            _radarRect = r;
            _radarV.Layout(r);
            _sb.Append("|ra:").Append(r.X).Append(',').Append(r.Width);
        }
    }
}
