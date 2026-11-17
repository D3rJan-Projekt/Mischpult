using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5 Einschlaf-Geraeusche als eigener Kanal: Regen, Meer, Rauschen oder Kaminfeuer – vom Pult selbst erzeugt.
    /// Taste AN startet/stoppt, der Fader stellt die Lautstaerke, mit dem Sleep-Timer blendet es mit aus.
    /// </summary>
    internal sealed class NoiseStrip : StripBase
    {
        public static readonly Color NoiseColor = Color.FromArgb(0x5a, 0x8c, 0xd8);
        private RectangleF _nameRect, _kindRect, _kindBtn, _playBtn;

        public NoiseStrip(MixerForm mixer) : base(mixer) { }

        public override int StripWidth => Theme.Px(108);
        protected override Color AccentColor => NoiseColor;
        public override string DisplayName => "GERÄUSCH";
        protected override float MeterBarWidth => 6f;
        protected override float DefaultFaderPos => 0.6f;
        protected override bool Dimmed => !Mixer.NoisePlaying;
        protected override float FaderPos => Mixer.NoiseFader;
        protected override bool DbFader => true;
        protected override string ReadoutText => Mixer.NoisePlaying ? FaderLaw.FormatDb(FaderPos) : "AUS";

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                yield return new KeyValuePair<float, string>(1f, "0");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-10), "-10");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-20), "-20");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-40), "-40");
                yield return new KeyValuePair<float, string>(0f, "∞");
            }
        }

        protected override void OnFaderMoved(float pos, bool final) => Mixer.SetNoiseFader(pos, final);
        protected override void OnMiddleClick() => Mixer.ToggleNoise();

        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _kindRect = new RectangleF(x, Theme.F(42), iw, Theme.F(44));
            _kindBtn = new RectangleF(x, Theme.F(90), iw, Theme.F(20));
            _playBtn = new RectangleF(x, Theme.F(237 + DynRow + RowOffset), iw, Theme.F(22));
            AddZone(_nameRect, "kind");
            AddZone(_kindRect, "kind");
            AddZone(_kindBtn, "kind");
            AddZone(_playBtn, "play");
        }

        protected override void DrawTop(Graphics g)
        {
            Color col = NoiseColor;
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);
            Theme.DrawColorEdge(g, PanelRect, col);
            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Theme.Darken(col, 0.45f) : col;
            Theme.DrawNamePlate(g, nr, bg);
            using (var b = new SolidBrush(Theme.PlateText(bg)))
                Theme.Str(g, "GERÄUSCH", Theme.FitPlate(g, "GERÄUSCH", nr.Width - Theme.F(8), 12f, 7.5f), b, nr, Theme.Center);

            var kr = _kindRect;
            bool hover = HoverZone == "kind";
            Theme.DrawWell(g, kr, hover);
            string kind = NoiseGen.KindNames[Math.Max(0, Math.Min(NoiseGen.KindNames.Length - 1, Mixer.NoiseKind))];
            using (var b = new SolidBrush(Theme.Text))
                Theme.Str(g, kind, Theme.Fit(g, kind, kr.Width - Theme.F(8), 12f, 8.5f), b, new RectangleF(kr.X + Theme.F(3), kr.Y + Theme.F(4), kr.Width - Theme.F(6), Theme.F(18)), Theme.Center);
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, Mixer.NoisePlaying ? "läuft" : "aus", Theme.Ui(9f), b, new RectangleF(kr.X + Theme.F(3), kr.Y + Theme.F(23), kr.Width - Theme.F(6), Theme.F(16)), Theme.Center);
            Theme.DrawDisplayButton(g, _kindBtn, "GERÄUSCH ▾", hover, Theme.Text, false);

            Theme.Engrave(g, left, right, Theme.F(116));
            var hint = new RectangleF(_kindRect.X, Theme.F(124), _kindRect.Width, Theme.F(90 + RowOffset));
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "Zum Einschlafen.\nBlendet mit dem\nSleep-Timer aus.", Theme.Ui(8.5f), b, hint, Theme.Center);

            Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset));
            Theme.DrawKey(g, _playBtn, "AN", Mixer.NoisePlaying, Theme.LedGreen, HoverZone == "play");
            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || (zone == "kind" && e.Button == MouseButtons.Left))
            {
                Mixer.ShowNoiseMenu(PointToScreen(zone == "kind" ? new Point((int)_kindBtn.X, (int)_kindBtn.Bottom + 2) : e.Location));
                return false;
            }
            if (zone == "play" && e.Button == MouseButtons.Left) { Mixer.ToggleNoise(); InvalidateChrome(); }
            return false;
        }

        protected override string GetTooltip(string zone)
        {
            switch (zone)
            {
                case "kind": return "Geräusch wählen: Rauschen, Regen, Meer oder Kaminfeuer\nDas Pult erzeugt es selbst, es spielt auf dem Standard-Gerät.";
                case "play": return Mixer.NoisePlaying ? "Geräusch stoppen (blendet in 1 s aus)" : "Geräusch starten";
            }
            return null;
        }
    }
}
