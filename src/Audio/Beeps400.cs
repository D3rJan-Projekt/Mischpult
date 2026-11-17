using System;

namespace Mischpult.Audio
{
    /// <summary>4.0: weitere Klick-Arten (Studio-Taste, Schreibmaschine, Retro-Schalter) und das Surren der Motorfader.</summary>
    internal static partial class Beeps
    {
        public static readonly SoundClip ClickStudio = MakeStudioClick();
        public static readonly SoundClip ClickTypewriter = MakeTypewriter();
        public static readonly SoundClip ClickRetro = MakeRetroSwitch();

        /// <summary>Klick je Art (0 Standard, 1 Studio-Taste, 2 Schreibmaschine, 3 Retro-Schalter).</summary>
        public static SoundClip ClickOf(int kind) => kind == 1 ? ClickStudio : kind == 2 ? ClickTypewriter : kind == 3 ? ClickRetro : Click;

        private static SoundClip Normalize(double[] v, string name, double level = 0.95)
        {
            int frames = v.Length;
            double peak = 1e-9;
            foreach (var x in v) peak = Math.Max(peak, Math.Abs(x));
            int fadeN = DspMixer.Rate / 400;
            var data = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                double fade = i > frames - fadeN ? (frames - i) / (double)fadeN : 1.0;
                data[i * 2] = data[i * 2 + 1] = (float)(v[i] / peak * level * fade);
            }
            return new SoundClip { Name = name, Data = data, Frames = frames };
        }

        /// <summary>Weiche, satte Studio-Taste: gedaempfter Anschlag, tiefer Koerper, kaum Hoehen.</summary>
        private static SoundClip MakeStudioClick()
        {
            const int rate = DspMixer.Rate;
            int n = rate * 45 / 1000;
            var v = new double[n];
            var rnd = new Random(11);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double noise = (rnd.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0011);
                lp += (noise - lp) * 0.18;   // gedaempft
                double body = Math.Sin(2 * Math.PI * 820 * t) * Math.Exp(-t / 0.0045) * 0.6;
                double thump = Math.Sin(2 * Math.PI * 140 * t) * Math.Exp(-t / 0.012) * 0.5;
                v[i] = (lp * 1.6 + body + thump) * Math.Min(1.0, i / (rate * 0.0004));
            }
            return Normalize(v, "Studio-Taste", 0.9);
        }

        /// <summary>Schreibmaschine: harter Typenanschlag mit metallischem Nachklang.</summary>
        private static SoundClip MakeTypewriter()
        {
            const int rate = DspMixer.Rate;
            int n = rate * 70 / 1000;
            var v = new double[n];
            var rnd = new Random(23);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double hit = (rnd.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0007);
                double plate = Math.Sin(2 * Math.PI * 3100 * t) * Math.Exp(-t / 0.006) * 0.35
                             + Math.Sin(2 * Math.PI * 4650 * t + 1.1) * Math.Exp(-t / 0.004) * 0.25;
                double bar = Math.Sin(2 * Math.PI * 610 * t) * Math.Exp(-t / 0.009) * 0.3;
                // zweiter, leiserer Anschlag (Typenhebel faellt zurueck)
                double t2 = t - 0.028;
                double back = t2 > 0 ? (rnd.NextDouble() * 2 - 1) * Math.Exp(-t2 / 0.0006) * 0.35 + Math.Sin(2 * Math.PI * 2300 * t2) * Math.Exp(-t2 / 0.003) * 0.15 : 0;
                v[i] = (hit + plate + bar + back) * Math.Min(1.0, i / (rate * 0.0001));
            }
            return Normalize(v, "Schreibmaschine", 0.95);
        }

        /// <summary>Retro-Kippschalter: zwei schnelle metallische Klacke (Schnappen und Einrasten).</summary>
        private static SoundClip MakeRetroSwitch()
        {
            const int rate = DspMixer.Rate;
            int n = rate * 55 / 1000;
            var v = new double[n];
            var rnd = new Random(31);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double a = (rnd.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0005) + Math.Sin(2 * Math.PI * 1800 * t) * Math.Exp(-t / 0.002) * 0.4;
                double t2 = t - 0.013;
                double b = t2 > 0 ? (rnd.NextDouble() * 2 - 1) * Math.Exp(-t2 / 0.0006) * 1.1 + Math.Sin(2 * Math.PI * 1250 * t2) * Math.Exp(-t2 / 0.004) * 0.55
                                    + Math.Sin(2 * Math.PI * 260 * t2) * Math.Exp(-t2 / 0.007) * 0.25 : 0;
                v[i] = (a * 0.55 + b) * Math.Min(1.0, i / (rate * 0.0002));
            }
            return Normalize(v, "Retro-Schalter", 0.95);
        }

        /// <summary>Lautstaerke des Motor-Surrens je Stufe (0 leise, 1 mittel, 2 laut).</summary>
        public static float MotorGainOf(int level) => level <= 0 ? 0.05f : level >= 2 ? 0.16f : 0.09f;

        /// <summary>
        /// Surren eines Motorfaders: Brummen mit Obertoenen und etwas Getriebe-Rauschen, Tonhoehe und Lautstaerke folgen
        /// der Geschwindigkeit (sanft anfahren und abbremsen wie die Fader).
        /// </summary>
        public static SoundClip MotorWhirr(float seconds, float dist)
        {
            const int rate = DspMixer.Rate;
            seconds = Math.Max(0.12f, Math.Min(10f, seconds));
            int n = (int)(rate * seconds);
            var v = new double[n];
            var rnd = new Random(41);
            double phase = 0, bp1 = 0, bp2 = 0;
            double strength = 0.55 + 0.45 * Math.Min(1.0, dist * 2.0);
            for (int i = 0; i < n; i++)
            {
                double u = i / (double)n;
                double speed = 6 * u * (1 - u);                 // Ableitung von 3u²-2u³ (0..1,5)
                double f = 95 + 120 * speed;                     // Tonhoehe steigt mit dem Tempo
                phase += 2 * Math.PI * f / rate;
                double tone = Math.Sin(phase) + 0.5 * Math.Sin(2 * phase + 0.3) + 0.28 * Math.Sin(3 * phase + 1.1) + 0.12 * Math.Sin(5 * phase);
                double noise = rnd.NextDouble() * 2 - 1;
                bp1 += (noise - bp1) * 0.08; bp2 += (bp1 - bp2) * 0.08;   // dumpfes Getriebe-Rauschen
                double env = Math.Min(1.0, speed / 1.2) * strength;
                env *= Math.Min(1.0, i / (rate * 0.01)) * Math.Min(1.0, (n - i) / (rate * 0.015));
                v[i] = (tone * 0.6 + (bp1 - bp2) * 3.0) * env;
            }
            return Normalize(v, "Motor", 0.8);
        }
    }
}
