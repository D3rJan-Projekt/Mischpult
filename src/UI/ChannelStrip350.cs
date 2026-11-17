using System;
using System.Drawing;
using System.Globalization;

namespace Mischpult.UI
{
    /// <summary>3.5: Verzoegerung am Kanal, Kanal-Statistik, Mittelklick, mehrere Fader zusammen.</summary>
    internal sealed partial class ChannelStrip
    {
        public static bool LatencyOn, StatsOn;
        private int _latencyMs;

        protected override bool CanMultiSelect => true;

        protected override void OnMiddleClick()
        {
            Mixer.CancelFade(Config);
            Config.Mute = !Config.Mute;
            Mixer.OnChannelChanged(Config, false);
        }

        /// <summary>Verzoegerung des Kanals (0 = laeuft direkt, nichts anzeigen).</summary>
        public void SetLatency(int ms)
        {
            if (!LatencyOn) ms = 0;
            if (ms == _latencyMs) return;
            _latencyMs = ms;
            InvalidateChrome();
        }

        private void DrawExtras350(Graphics g)
        {
            if (_latencyMs <= 0 || _appsRect.IsEmpty) return;
            string t = "+" + _latencyMs + " ms";
            var font = Theme.Ui(7f, FontStyle.Bold);
            float w = Theme.TextWidth(g, t, font) + Theme.F(6), h = Theme.F(11);
            var r = new RectangleF(_appsRect.Right - w - Theme.F(2), _appsRect.Y + Theme.F(2), w, h);
            Theme.FillRound(g, Color.FromArgb(200, 0x12, 0x14, 0x18), r, Theme.F(3));
            using (var b = new SolidBrush(_latencyMs > 60 ? Theme.SoloAmber : Color.FromArgb(0x9c, 0xc8, 0xff)))
                Theme.Str(g, t, font, b, r, Theme.Center);
        }

        // ---------------------------------------------------------------- Statistik seit dem Start
        private double _statPlay, _statSumDb;
        private float _statMaxDb = float.NegativeInfinity;

        private void StatsSample(float peak, float dt)
        {
            if (!StatsOn || dt <= 0f || dt > 0.5f) return;
            if (peak > 0.00316f)   // lauter als −50 dB: spielt
            {
                float db = 20f * (float)Math.Log10(peak);
                _statPlay += dt;
                _statSumDb += db * dt;
                if (db > _statMaxDb) _statMaxDb = db;
            }
        }

        public void ResetStats() { _statPlay = 0; _statSumDb = 0; _statMaxDb = float.NegativeInfinity; }

        internal string StatsText()
        {
            if (_statPlay < 1.0) return "Statistik: hat seit dem Start noch nicht gespielt";
            double avg = _statSumDb / _statPlay;
            var ts = TimeSpan.FromSeconds(_statPlay);
            string time = ts.TotalHours >= 1 ? ((int)ts.TotalHours) + " h " + ts.Minutes + " min" : ts.TotalMinutes >= 1 ? ((int)ts.TotalMinutes) + " min " + ts.Seconds + " s" : ts.Seconds + " s";
            return "Statistik seit dem Start: spielte " + time + " · im Schnitt " + avg.ToString("0", CultureInfo.InvariantCulture).Replace("-", "−") + " dB · lautester Moment "
                 + (float.IsNegativeInfinity(_statMaxDb) ? "–" : _statMaxDb.ToString("0.0", CultureInfo.InvariantCulture).Replace("-", "−") + " dB");
        }
    }
}
