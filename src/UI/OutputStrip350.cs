using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>3.5: Phasen-Warnung am MASTER, Geraete-Bild und Format am Ausgang.</summary>
    internal sealed partial class OutputStrip
    {
        public static bool PhaseWarnOn, DevIconOn, DevFormatOn;
        /// <summary>Ton des Standardgeraets ist laenger gegenphasig (vom Pult gesetzt).</summary>
        public static bool PhaseBad;
        public static float PhaseCorr = float.NaN;

        private bool ShowsPhase => PhaseWarnOn && PhaseBad && (!Fixed || (_dev != null && _dev.IsDefault));

        /// <summary>Unterzeile im Geraete-Feld: Format statt Treibername (falls an und bekannt).</summary>
        private string DeviceSubText(string fallback) => DevFormatOn && _dev?.FormatText != null ? _dev.FormatText : fallback;

        /// <summary>Platz links im Geraete-Feld fuer das Bild (0 = kein Bild).</summary>
        private float DeviceIconSpace => DevIconOn && _dev != null ? Theme.F(18) : 0f;

        private void DrawDeviceIcon350(Graphics g, RectangleF ir)
        {
            if (!DevIconOn || _dev == null) return;
            var r = new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(4), Theme.F(16), Theme.F(16));
            Theme.DrawDeviceIcon(g, r, _dev.FormFactor, _dev.Bluetooth, _dev.Usb, Theme.Text);
        }

        private void DrawPhase350(Graphics g, RectangleF nr)
        {
            if (!ShowsPhase) return;
            float h = nr.Height - Theme.F(6), w = h * 1.35f;
            var r = new RectangleF(nr.Right - w - Theme.F(3), nr.Y + Theme.F(3), w, h);
            Theme.FillRound(g, Theme.MuteRed, r, h / 2f);
            using (var b = new SolidBrush(Color.White))
                Theme.Str(g, "Φ", Theme.Ui(10f, FontStyle.Bold), b, r, Theme.Center);
        }

        internal string PhaseTip => ShowsPhase
            ? "\n⚠ PHASE: Der Ton ist gegenphasig (Korrelation " + (float.IsNaN(PhaseCorr) ? "–" : PhaseCorr.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + "). Er klingt dann dünn, und in Mono (z. B. Handy-Lautsprecher, MONO-Taste) verschwinden Teile. Oft ist ein Kabel oder eine Einstellung „Phase umkehren“ schuld."
            : "";

        internal string DeviceKindTip => _dev == null || (!DevIconOn && !DevFormatOn) ? ""
            : "\n" + Theme.DeviceKindName(_dev.FormFactor, _dev.Bluetooth, _dev.Usb) + (_dev.FormatText != null ? " · " + _dev.FormatText : "");
    }
}
