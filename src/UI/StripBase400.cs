using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>4.0: Drehknopf-Ansicht – ein grosser Drehknopf statt des Faders (Pegel bleiben daneben).</summary>
    internal abstract partial class StripBase
    {
        /// <summary>Grosse Drehknoepfe statt Fader (OPTIONEN → Aussehen).</summary>
        public static bool KnobView;
        /// <summary>4.0 Kanalzug anpassen: dB-Skala bzw. Wert unter dem Fader ausblenden (alle Zuege).</summary>
        public static bool HideScale, HideReadout;
        /// <summary>4.0 Barrierefreiheit: weniger Bewegung (kein Lauflicht, keine Motorfahrt …), deutlicher Fokus-Rahmen.</summary>
        public static bool Calm, A11yFocus;

        private PointF _knobC;
        private float _knobR;

        /// <summary>Aus DoLayout: Knopf in die Mitte links der Pegel, die ganze Flaeche dort greift.</summary>
        private void LayoutKnob400()
        {
            if (!KnobView) { _knobR = 0f; return; }
            float left = PanelRect.X + Theme.F(4), right = MeterL.X - Theme.F(Narrow ? 4 : 6);
            float w = Math.Max(Theme.F(20), right - left);
            float ringPad = Theme.F(Narrow ? 9 : 12), labelPad = Theme.F(Narrow ? 3 : 6);   // Platz fuer LED-Kranz und 0-dB-Strich
            _knobR = (float)Math.Floor(Math.Max(Theme.F(10), Math.Min(w / 2f - ringPad - labelPad, Math.Min(Theme.F(48), FaderArea.Height * 0.3f))));
            float cy = FaderArea.Y + Math.Max(Theme.F(18) + _knobR + ringPad + labelPad * 0.5f, FaderArea.Height * 0.45f);   // etwa mittig neben den Pegeln
            _knobC = new PointF((float)Math.Round(left + w / 2f), (float)Math.Round(cy));
            FaderHit = new RectangleF(left, FaderArea.Y, w, FaderArea.Height);
            SlotRect = RectangleF.Empty;   // kein Schlitz: Leuchten am Schlitz fallen weg
        }

        /// <summary>Statt Skala, Schlitz und Kappe: der Knopf mit dB-Werten am Rand.</summary>
        private void DrawKnobSection(Graphics g, bool liveCap)
        {
            // 0 dB als heller Strich am Kranz (der genaue Wert steht unten in der Anzeige)
            foreach (var m in ScaleMarks)
                if (m.Value == "0") { var sm0 = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; KnobTick(g, m.Key, Theme.Text, 1.4f); g.SmoothingMode = sm0; }
            // Pegel-Hintergrund (wie beim Fader)
            Theme.DrawMeterBezel(g, RectangleF.Inflate(MeterArea, Theme.F(2), Theme.F(2)));
            if (GrShown) DrawGrSegments(g, true, 0);
            DrawSegments(g, MeterL, _segCount, true, 0, -1);
            DrawSegments(g, MeterR, _segCount, true, 0, -1);
            if (!PeakDb) Theme.FillRound(g, Theme.Mix(Theme.SegOff, Theme.MeterRed, 0.18f), ClipRect, Theme.F(1.5f));
            else if (!Narrow) DrawMeterScale(g);
            if (!liveCap) DrawKnob400(g, ShownFaderPos);
        }

        /// <summary>Winkel (Bogenmass) fuer eine Faderposition: -135° (ganz leise) bis +135° (ganz laut), 0 = oben.</summary>
        internal static double KnobAngle(float pos) => (-135.0 + 270.0 * Math.Max(0f, Math.Min(1f, pos)) - 90.0) * Math.PI / 180.0;

        private void DrawKnob400(Graphics g, float pos)
        {
            if (_knobR <= 0f) return;
            bool hover = HoverZone == "fader" || _dragging;
            Color led = Dimmed ? Theme.Darken(AccentColor, 0.4f) : AccentColor;
            if (MasterCap) led = Color.FromArgb(0xff, 0x5a, 0x48);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawKnob(g, _knobC, _knobR, Math.Max(0f, Math.Min(1f, pos)), led, false, hover, Narrow ? 6f : 8f, Narrow ? 19 : 27);
            // Fader-Marke und Deckel als Striche am Kranz
            float mk = FaderMarkPos;
            if (!float.IsNaN(mk)) KnobTick(g, mk, AccentColor, 1.6f);
            float lim = FaderLimitPos;
            if (!float.IsNaN(lim) && lim < 0.999f) KnobTick(g, lim, Theme.MeterRed, 2f);
            g.SmoothingMode = sm;
        }

        /// <summary>4.0: Ueberlagerungen obendrauf (Kanal-Suche, Ziel beim App-Ziehen).</summary>
        protected virtual void PaintOverlays400(Graphics g) { }

        private void KnobTick(Graphics g, float pos, Color c, float w)
        {
            double a = KnobAngle(pos);
            float r1 = _knobR + Theme.F(Narrow ? 9 : 11), r2 = r1 + Theme.F(5);
            using (var pen = new Pen(c, Math.Max(1.2f, Theme.F(w))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, _knobC.X + (float)Math.Cos(a) * r1, _knobC.Y + (float)Math.Sin(a) * r1, _knobC.X + (float)Math.Cos(a) * r2, _knobC.Y + (float)Math.Sin(a) * r2);
        }
    }
}
