using System;
using System.IO;

namespace Mischpult.Audio
{
    /// <summary>
    /// Kurze Hinweistoene (48 kHz Stereo): Mikro stumm = absteigend, Mikro an = aufsteigend.
    /// Werden nur auf dem eigenen Ausgang gespielt – nie im Sprach-Ausgang oder Stream.
    /// </summary>
    internal static partial class Beeps
    {
        public static readonly SoundClip MicOff = TwoTone("Mikro stumm", 784.0, 523.25);
        public static readonly SoundClip MicOn = TwoTone("Mikro an", 523.25, 784.0);

        /// <summary>3.2: Gong am Ende des Countdowns (drei aufsteigende Toene).</summary>
        public static readonly SoundClip Chime = MakeChime();

        /// <summary>3.2: Testton – erst nur links, dann nur rechts (je 0,7 s, dazwischen 0,25 s Pause).</summary>
        public static readonly SoundClip TestLR = MakeTestLR();
        /// <summary>Wann der rechte Ton anfaengt (s) – fuer die Einblendung.</summary>
        public const double TestRightAt = 0.95;

        private static SoundClip MakeChime()
        {
            const int rate = DspMixer.Rate;
            int n = rate * 180 / 1000, gap = rate * 40 / 1000;
            int frames = 3 * n + 2 * gap + rate * 300 / 1000;
            var data = new float[frames * 2];
            Tone(data, 0, n, 523.25, rate);
            Tone(data, n + gap, n, 659.25, rate);
            Tone(data, 2 * (n + gap), n + rate * 300 / 1000, 783.99, rate);
            return new SoundClip { Name = "Countdown", Data = data, Frames = frames };
        }

        private static SoundClip MakeTestLR()
        {
            const int rate = DspMixer.Rate;
            int len = rate * 700 / 1000, start2 = (int)(rate * TestRightAt);
            int frames = start2 + len;
            var data = new float[frames * 2];
            int attack = rate * 8 / 1000;
            for (int side = 0; side < 2; side++)
            {
                int start = side == 0 ? 0 : start2;
                for (int i = 0; i < len; i++)
                {
                    double t = i / (double)rate;
                    double env = Math.Min(1.0, Math.Min(i / (double)attack, (len - i) / (double)attack));
                    double v = (Math.Sin(2 * Math.PI * 600.0 * t) + 0.15 * Math.Sin(4 * Math.PI * 600.0 * t)) / 1.15 * 0.8 * env;
                    data[(start + i) * 2 + side] = (float)v;
                }
            }
            return new SoundClip { Name = "Testton links/rechts", Data = data, Frames = frames };
        }

        /// <summary>Mechanisches Klicken einer Pult-Taste (ca. 30 ms).</summary>
        public static readonly SoundClip Click = MakeClick();

        /// <summary>Lautstaerke des Tasten-Klickens je Stufe (0 leise, 1 mittel, 2 laut).</summary>
        public static float ClickGainOf(int level) => level <= 0 ? 0.07f : level >= 2 ? 0.28f : 0.14f;

        /// <summary>
        /// Klick wie ein Taster: kurzer Rausch-Impuls (Anschlag), zwei schnell abklingende Resonanzen (Gehaeuse, Feder)
        /// und ein leiser tiefer "Plopp". Feste Zufallsfolge, damit es immer gleich klingt.
        /// </summary>
        private static SoundClip MakeClick()
        {
            const int rate = DspMixer.Rate;
            int frames = rate * 32 / 1000;
            var data = new float[frames * 2];
            var rnd = new Random(7);
            double peak = 0;
            var v = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                double t = i / (double)rate;
                double noise = (rnd.NextDouble() * 2 - 1) * Math.Exp(-t / 0.00045);
                double r1 = Math.Sin(2 * Math.PI * 2350 * t) * Math.Exp(-t / 0.0028) * 0.55;
                double r2 = Math.Sin(2 * Math.PI * 5200 * t) * Math.Exp(-t / 0.0012) * 0.3;
                double thump = Math.Sin(2 * Math.PI * 170 * t) * Math.Exp(-t / 0.006) * 0.22;
                double attack = Math.Min(1.0, i / (rate * 0.0002));
                v[i] = (noise * 0.7 + r1 + r2 + thump) * attack;
                if (Math.Abs(v[i]) > peak) peak = Math.Abs(v[i]);
            }
            for (int i = 0; i < frames; i++)
            {
                double fade = i > frames - rate / 500 ? (frames - i) / (double)(rate / 500) : 1.0;   // die letzten 2 ms ausblenden
                data[i * 2] = data[i * 2 + 1] = (float)(v[i] / peak * 0.95 * fade);
            }
            return new SoundClip { Name = "Taste", Data = data, Frames = frames };
        }

        /// <summary>Lautstaerke je Stufe (1 leise, 2 mittel, 3 laut).</summary>
        public static float GainOf(int level) => level <= 1 ? 0.12f : level >= 3 ? 0.5f : 0.25f;

        private static SoundClip TwoTone(string name, double f1, double f2)
        {
            const int rate = DspMixer.Rate;
            int n1 = rate * 80 / 1000, gap = rate * 25 / 1000, n2 = rate * 110 / 1000;
            int frames = n1 + gap + n2;
            var data = new float[frames * 2];
            Tone(data, 0, n1, f1, rate);
            Tone(data, n1 + gap, n2, f2, rate);
            return new SoundClip { Name = name, Data = data, Frames = frames };
        }

        /// <summary>Sinus mit etwas Oberton, 4 ms Einblenden, weiches Ausklingen (kein Knacken).</summary>
        private static void Tone(float[] data, int start, int len, double freq, int rate)
        {
            int attack = rate * 4 / 1000;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)rate;
                double env = i < attack ? i / (double)attack : Math.Exp(-3.2 * (i - attack) / (double)(len - attack));
                if (i > len - attack) env *= (len - i) / (double)attack;
                double v = (Math.Sin(2 * Math.PI * freq * t) + 0.18 * Math.Sin(4 * Math.PI * freq * t)) / 1.18 * env;
                int k = (start + i) * 2;
                data[k] = data[k + 1] = (float)v;
            }
        }

        /// <summary>Als 16-Bit-WAV (fuer die Wiedergabe ohne EQ-Mischer, z. B. aelteres Windows).</summary>
        public static byte[] ToWav(SoundClip clip, float gain)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int bytes = clip.Frames * 4;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(DspMixer.Rate); w.Write(DspMixer.Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(bytes);
                for (int i = 0; i < clip.Frames * 2; i++)
                    w.Write((short)Math.Max(-32767, Math.Min(32767, Math.Round(clip.Data[i] * gain * 32767))));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
