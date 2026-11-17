using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>Parameter eines EQ-Kanals fuer den Mischer. Unveraenderlich.</summary>
    internal sealed class ChannelDsp
    {
        public readonly float Gain, Low, Mid, High;
        public readonly bool Comp, Leveler;
        public readonly float[] Graphic;  // 10-Band-EQ (dB) oder null
        public readonly string DeviceId; // null = Windows-Standardgeraet
        public readonly ChannelFx Fx;    // 2.19: Hall/Echo oder null
        public readonly bool Cue;        // 2.19: vorhoeren
        public readonly int Stereo;      // 3.2: 0 normal, 1 mono, 2 links/rechts getauscht
        public readonly int DelayMs;     // 3.2: Verzoegerung
        public readonly ChannelExtra Extra;   // 3.4.6: Stereo-Breite, Bass-Filter, Knall-Bremse (null = nichts)
        public ChannelDsp(float gain, float low, float mid, float high, string deviceId, bool comp = false, bool leveler = false, float[] graphic = null, ChannelFx fx = null, bool cue = false)
            : this(gain, low, mid, high, deviceId, comp, leveler, graphic, fx, cue, 0, 0) { }

        public ChannelDsp(float gain, float low, float mid, float high, string deviceId, bool comp, bool leveler, float[] graphic, ChannelFx fx, bool cue, int stereo, int delayMs)
            : this(gain, low, mid, high, deviceId, comp, leveler, graphic, fx, cue, stereo, delayMs, null) { }

        public ChannelDsp(float gain, float low, float mid, float high, string deviceId, bool comp, bool leveler, float[] graphic, ChannelFx fx, bool cue, int stereo, int delayMs, ChannelExtra extra)
        {
            Extra = extra == null || extra.IsNeutral ? null : extra;
            Gain = gain; Low = low; Mid = mid; High = high; DeviceId = deviceId; Comp = comp; Leveler = leveler;
            Graphic = graphic == null || graphic.Length != 10 || GraphicEq.IsFlat(graphic) ? null : graphic;
            Fx = fx; Cue = cue;
            Stereo = Math.Max(0, Math.Min(2, stereo)); DelayMs = Math.Max(0, Math.Min(1000, delayMs));
        }
        public string OutputKey => DeviceId ?? "";
    }

    /// <summary>
    /// Kompressor fuer den Nachtmodus: leise Stellen lauter, laute Stellen leiser.
    /// Stereo-gekoppelt, weiches Knie, feste Einstellungen.
    /// </summary>
    internal sealed class Compressor
    {
        public const float ThresholdDb = -24f, Ratio = 4f, KneeDb = 6f, MakeupDb = 8f;
        public const float AttackMs = 5f, ReleaseMs = 200f;
        private float _att, _rel;
        private float _env;
        private readonly int _rate;
        // 4.0 (finale): eigene Werte (Kompressor-Fenster)
        private float _thr = ThresholdDb, _ratio = Ratio, _knee = KneeDb, _makeup = MakeupDb;
        private bool _custom;

        /// <summary>Staerkste Pegelabsenkung seit dem letzten Abholen (dB, positiv).</summary>
        public float Reduction;

        /// <summary>4.0 (finale): eigene Werte setzen (null = feste Werte).</summary>
        public void Configure(CompSpec s)
        {
            var v = s ?? CompSpec.Default;
            _custom = s != null;
            _thr = v.Thr; _ratio = v.Ratio; _knee = v.Knee; _makeup = v.Makeup;
            _att = (float)Math.Exp(-1.0 / (_rate * v.AttMs / 1000.0));
            _rel = (float)Math.Exp(-1.0 / (_rate * v.RelMs / 1000.0));
        }

        public Compressor(int rate)
        {
            _rate = rate;
            _att = (float)Math.Exp(-1.0 / (rate * AttackMs / 1000.0));
            _rel = (float)Math.Exp(-1.0 / (rate * ReleaseMs / 1000.0));
        }

        public void Reset() { _env = 0; }

        /// <summary>
        /// 3.4.1: Beim Einschalten nicht "leer" anfangen: Mit Huelle 0 gab der Kompressor im ersten Moment die vollen +8 dB
        /// Aufholverstaerkung und regelte erst nach 5 ms herunter – ein Knacken und ein Pegelsprung. Jetzt beginnt die Huelle
        /// beim Pegel, der gerade anliegt.
        /// </summary>
        public void Prime(float[] buf, int frames)
        {
            float pk = 0f;
            int m = Math.Min(frames, 480) * 2;
            for (int i = 0; i < m; i++) { float a = buf[i] < 0 ? -buf[i] : buf[i]; if (a > pk) pk = a; }
            _env = pk;
        }

        /// <summary>Kennlinie: Pegelaenderung in dB (ohne Aufholverstaerkung) fuer einen Eingangspegel.</summary>
        public static float CurveDb(float x)
        {
            float d = x - ThresholdDb;
            if (2 * d < -KneeDb) return 0f;
            if (2 * Math.Abs(d) <= KneeDb)
            {
                float t = d + KneeDb / 2;
                return (1f / Ratio - 1f) * t * t / (2 * KneeDb);
            }
            return d / Ratio - d;
        }

        public void Process(float[] buf, int frames)
        {
            float env = _env, att = _att, rel = _rel, maxRed = Reduction;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float l = buf[k], r = buf[k + 1];
                float a = Math.Max(l < 0 ? -l : l, r < 0 ? -r : r);
                env = a > env ? att * env + (1 - att) * a : rel * env + (1 - rel) * a;
                float db = env > 1e-6f ? 20f * (float)Math.Log10(env) : -120f;
                float c = _custom ? CompSpec.Curve(db, _thr, _ratio, _knee) : CurveDb(db);
                if (-c > maxRed) maxRed = -c;
                float g = (float)Math.Pow(10.0, (c + _makeup) / 20.0);
                buf[k] = l * g;
                buf[k + 1] = r * g;
            }
            if (env < 1e-9f) env = 0;
            _env = env;
            Reduction = maxRed;
        }
    }

    /// <summary>Spitzenbegrenzer (Gehoerschutz): Ausgang ueberschreitet nie die Grenze, sofortiger Eingriff, weiches Loslassen.</summary>
    internal sealed class Limiter
    {
        private float _g = 1f;
        private readonly float _rel;

        public Limiter(int rate, float releaseMs = 150f)
        {
            _rel = 1f - (float)Math.Exp(-1.0 / (rate * releaseMs / 1000.0));
        }

        public float Gain => _g;

        public void Process(float[] buf, int frames, float ceiling)
        {
            float g = _g, rel = _rel;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float l = buf[k], r = buf[k + 1];
                float a = Math.Max(l < 0 ? -l : l, r < 0 ? -r : r);
                float gr = g + (1f - g) * rel;
                g = a * gr > ceiling ? ceiling / a : gr;
                buf[k] = l * g;
                buf[k + 1] = r * g;
            }
            _g = g;
        }
    }

    /// <summary>Biquad-Filter (RBJ-Kochbuch), Stereo, transponierte Direktform II.</summary>
    internal sealed class Biquad
    {
        private double _b0 = 1, _b1, _b2, _a1, _a2;
        private double _l1, _l2, _r1, _r2;

        public void LowShelf(double fs, double f0, double db) => Shelf(fs, f0, db, false);
        public void HighShelf(double fs, double f0, double db) => Shelf(fs, f0, db, true);
        /// <summary>4.0: Kuhschwanz mit Guete (AutoEq "LSC"/"HSC").</summary>
        public void LowShelfQ(double fs, double f0, double db, double q) => Shelf(fs, f0, db, false, q);
        public void HighShelfQ(double fs, double f0, double db, double q) => Shelf(fs, f0, db, true, q);

        private void Shelf(double fs, double f0, double db, bool high, double q = 0.70710678)
        {
            double A = Math.Pow(10, db / 40.0);
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * Math.Max(0.05, q)); // q = 0,707: wie bisher (S = 1)
            double sq = 2 * Math.Sqrt(A) * alpha;
            double b0, b1, b2, a0, a1, a2;
            if (!high)
            {
                b0 = A * ((A + 1) - (A - 1) * cs + sq);
                b1 = 2 * A * ((A - 1) - (A + 1) * cs);
                b2 = A * ((A + 1) - (A - 1) * cs - sq);
                a0 = (A + 1) + (A - 1) * cs + sq;
                a1 = -2 * ((A - 1) + (A + 1) * cs);
                a2 = (A + 1) + (A - 1) * cs - sq;
            }
            else
            {
                b0 = A * ((A + 1) + (A - 1) * cs + sq);
                b1 = -2 * A * ((A - 1) + (A + 1) * cs);
                b2 = A * ((A + 1) + (A - 1) * cs - sq);
                a0 = (A + 1) - (A - 1) * cs + sq;
                a1 = 2 * ((A - 1) - (A + 1) * cs);
                a2 = (A + 1) - (A - 1) * cs - sq;
            }
            Set(b0, b1, b2, a0, a1, a2);
        }

        public void Peaking(double fs, double f0, double db, double q)
        {
            double A = Math.Pow(10, db / 40.0);
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * q);
            Set(1 + alpha * A, -2 * cs, 1 - alpha * A, 1 + alpha / A, -2 * cs, 1 - alpha / A);
        }

        /// <summary>Hochpass 2. Ordnung (Butterworth bei q = 0,707).</summary>
        public void HighPass(double fs, double f0, double q = 0.7071)
        {
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * q);
            Set((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
        }

        /// <summary>3.5: Tiefpass 2. Ordnung (Butterworth bei q = 0,707).</summary>
        public void LowPass(double fs, double f0, double q = 0.7071)
        {
            f0 = Math.Min(f0, fs * 0.45);
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * q);
            Set((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
        }

        /// <summary>Durchlass (keine Wirkung).</summary>
        public void Flat() => Set(1, 0, 0, 1, 0, 0);

        /// <summary>4.0 (finale): Kerbfilter (Brumm-Entferner).</summary>
        public void Notch(double fs, double f0, double q)
        {
            f0 = Math.Min(f0, fs * 0.45);
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * q);
            Set(1, -2 * cs, 1, 1 + alpha, -2 * cs, 1 - alpha);
        }

        /// <summary>4.0 (finale): Bandpass mit 0 dB in der Mitte (x + (G-1)·BP(x) = Glocke mit Verstaerkung G).</summary>
        public void BandPass(double fs, double f0, double q)
        {
            f0 = Math.Min(f0, fs * 0.45);
            double w0 = 2 * Math.PI * f0 / fs, cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * q);
            Set(alpha, 0, -alpha, 1 + alpha, -2 * cs, 1 - alpha);
        }

        /// <summary>Gedaechtnis loeschen (vor dem Wiedereinschalten, damit es nicht knackt).</summary>
        public void Reset() { _l1 = _l2 = _r1 = _r2 = 0; }

        /// <summary>3.4.1: Kopie mit Einstellung und Gedaechtnis (fuer das Ueberblenden alter -> neuer EQ).</summary>
        public Biquad Clone() => (Biquad)MemberwiseClone();

        private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
        }

        /// <summary>Verstaerkung (Betrag) bei Frequenz f – fuer Tests.</summary>
        public double MagnitudeAt(double fs, double f)
        {
            double w = 2 * Math.PI * f / fs;
            double cr = Math.Cos(w), ci = -Math.Sin(w), c2r = Math.Cos(2 * w), c2i = -Math.Sin(2 * w);
            double nr = _b0 + _b1 * cr + _b2 * c2r, ni = _b1 * ci + _b2 * c2i;
            double dr = 1 + _a1 * cr + _a2 * c2r, di = _a1 * ci + _a2 * c2i;
            return Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
        }

        /// <summary>4.0: nur ein Ohr (0 links, 1 rechts) – fuer den Hoer-EQ.</summary>
        public void ProcessChannel(float[] buf, int frames, int ch)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            double s1 = ch == 0 ? _l1 : _r1, s2 = ch == 0 ? _l2 : _r2;
            for (int i = 0, k = ch; i < frames; i++, k += 2)
            {
                double x = buf[k];
                double y = b0 * x + s1;
                s1 = b1 * x - a1 * y + s2;
                s2 = b2 * x - a2 * y;
                buf[k] = (float)y;
            }
            if (Math.Abs(s1) < 1e-25) s1 = 0; if (Math.Abs(s2) < 1e-25) s2 = 0;
            if (ch == 0) { _l1 = s1; _l2 = s2; } else { _r1 = s1; _r2 = s2; }
        }

        public void Process(float[] buf, int frames)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            double l1 = _l1, l2 = _l2, r1 = _r1, r2 = _r2;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                double x = buf[k];
                double y = b0 * x + l1;
                l1 = b1 * x - a1 * y + l2;
                l2 = b2 * x - a2 * y;
                buf[k] = (float)y;

                x = buf[k + 1];
                y = b0 * x + r1;
                r1 = b1 * x - a1 * y + r2;
                r2 = b2 * x - a2 * y;
                buf[k + 1] = (float)y;
            }
            // Denormale vermeiden
            if (Math.Abs(l1) < 1e-25) l1 = 0; if (Math.Abs(l2) < 1e-25) l2 = 0;
            if (Math.Abs(r1) < 1e-25) r1 = 0; if (Math.Abs(r2) < 1e-25) r2 = 0;
            _l1 = l1; _l2 = l2; _r1 = r1; _r2 = r2;
        }
    }

    /// <summary>
    /// EQ-Mischer: nimmt den Ton einzelner Prozesse per Prozess-Loopback ab (Windows 10 2004+),
    /// schickt ihn pro Kanal durch einen 3-Band-EQ und Fader und gibt die Summe auf dem
    /// Windows-Standardgeraet aus.
    ///
    /// Trick: Die Original-Session der App wird auf -80 dB gesetzt (unhoerbar). Die Aufnahme
    /// enthaelt diese Absenkung, wird im Mischer aber exakt wieder angehoben (32-Bit-Float,
    /// kein Qualitaetsverlust). Falls Windows die Aufnahme unabgesenkt liefert, wird das pro
    /// Block erkannt und nicht angehoben.
    /// </summary>
    internal sealed partial class DspMixer : IDisposable
    {
        public const int Rate = 48000;
        public const float Attenuation = 0.0001f;      // -80 dB fuer die Original-Session
        private const float Makeup = 1f / Attenuation;
        private const float LoudThreshold = 0.002f;    // lauter als moeglich fuer eine abgesenkte Aufnahme
        /// <summary>3.0.3: lauter als das (+12 dB) kann eine richtig abgesenkte Aufnahme nach dem Anheben nicht sein.</summary>
        internal const float MaxPlausible = 4f;
        /// <summary>3.0.3: so lange nach dem Absenken einer Session gilt unabgesenkter Ton als Uebergang (ms).</summary>
        internal const int SettleMs = 150;
        public const float FreqLow = 120f, FreqMid = 1000f, FreqHigh = 6000f;

        public static bool IsSupported => Win32.IsWindows && Wasapi.WindowsBuild >= 19041;

        private sealed class Capture
        {
            public uint Pid;
            public string ChannelId;
            public ProcessLoopbackActivation Act;
            public long StartedMs;
            public bool Ready;
            public bool MeterOnly => ChannelId == null;
            public float PeakL, PeakR;                  // Pegel (nur Messung, ohne EQ)
            public float[] Ring;                        // 1 s Stereo, erst bei Bedarf
            public int ReadPos, Count;                  // in Frames
            public bool Primed;
            public int PrimeFrames = 480;
            public long LastPacketMs;
            public int LoudBlocks;
            public bool PreVolume;
            public bool SawLoud;                        // 3.3.1: seit dem (Neu-)Start ein Block, der fuer "abgesenkt" zu laut war
            public bool PreSuspect;                     // 3.3.1: ausserhalb eines Uebergangs laut -> Windows liefert wohl vor der Lautstaerke
            public int PostVotes;                       // 3.3.1: abgesenkte Bloecke direkt nach einem lauten (Windows senkt mit ab)
            public long LoudSince = -1;                 // 3.4.1: seit wann die Aufnahme ohne Unterbrechung zu laut fuer "abgesenkt" ist
            public int LogBlocks;                       // 3.4.1: so viele Bloecke nach dem Start werden noch protokolliert
            public string LogText;
            public float AppGainCur = 1f;               // eigene Lautstaerke der App im Kanal (weich nachgefuehrt)
            public bool Record;                         // Mess-Aufnahme liefert zusaetzlich Ton fuer die Kanal-Aufnahme
            public string StreamChannel;                // Ton geht (als Kanal ohne EQ) in den Stream-Mix
            public AudioRing StreamRing;
            public LoudnessSink Loud;                   // 2.17: Lautheit dieser App (nur Messung, auf Wunsch)
            public SpectrumAnalyzer Spec;               // 2.17: Spektrum dieser App (nur Messung, auf Wunsch)

            public void Write(float[] src, int frames, float gain)
            {
                if (Ring == null) Ring = new float[Rate * 2];
                int cap = Ring.Length / 2;
                if (frames <= 0 || frames > cap) return;
                if (Count + frames > cap)
                {
                    int drop = Count + frames - cap;
                    ReadPos = (ReadPos + drop) % cap;
                    Count -= drop;
                }
                int wp = (ReadPos + Count) % cap;
                for (int i = 0; i < frames; i++)
                {
                    int k = ((wp + i) % cap) * 2;
                    if (src == null) { Ring[k] = 0; Ring[k + 1] = 0; }
                    else { Ring[k] = src[i * 2] * gain; Ring[k + 1] = src[i * 2 + 1] * gain; }
                }
                Count += frames;
            }

            public void WriteSilence(int frames) => Write(null, frames, 0);

            /// <summary>Addiert bis zu n Frames in dst. Liefert true, wenn etwas gelesen wurde.</summary>
            public int FadeIn;                          // 3.0.3: so viele Frames wird nach einem (Neu-)Start noch eingeblendet
            public bool WasMuted;                       // 3.0.3: voriger Block wurde stumm gelassen (Uebergang)
            public const int FadeLen = 144;             // 3 ms
            public const int StartFadeLen = 480;        // 3.4.1: 10 ms beim (Neu-)Start (z. B. EQ/COMP eingeschaltet)
            public int FadeTotal = FadeLen;             // 3.4: Laenge der laufenden Einblende
            public bool FirstPrime = true;              // 3.4: noch nie gespielt (neue App)
            public void StartFade(int len) { FadeTotal = Math.Max(1, len); FadeIn = len; }
            /// <summary>3.4: so viele Frames blendet eine neue App auf dem EQ-Weg ein (0 = kurz wie bisher).</summary>
            public static int NewFadeFrames => CleanStart.NewAppFadeMs * 48;

            public bool ReadAdd(float[] dst, int n, long nowMs)
            {
                if (!Ready || Ring == null) return false;
                bool soft = CleanStart.On && !Record;
                if (!Primed)
                {
                    // 3.0.3: zum (Neu-)Start etwas mehr Vorrat (15 ms), dann weich einblenden statt hart einzusetzen
                    if (Count < (soft ? Math.Max(PrimeFrames, 720) : PrimeFrames)) return false;
                    Primed = true;
                    StartFade(FirstPrime && NewFadeFrames > FadeLen ? NewFadeFrames : soft ? StartFadeLen : 0);   // 3.4: neue App blendet laenger ein; 3.4.1: sonst 10 statt 3 ms
                    FirstPrime = false;
                }
                int cap = Ring.Length / 2;
                if (Count > 4800) // mehr als 100 ms Rueckstand -> Uhrendrift, auf Zielwert kuerzen
                {
                    int keep = PrimeFrames;
                    int drop = Count - keep;
                    ReadPos = (ReadPos + drop) % cap;
                    Count = keep;
                    if (soft) StartFade(FadeLen);   // 3.3.1: nach dem Sprung weich einsetzen statt hart (kein Knacken)
                }
                int m = Math.Min(n, Count);
                // 3.0.3: reicht der Vorrat nicht (Ton kommt gerade ungleichmaessig an), die letzten Frames ausblenden
                // statt hart auf Stille zu schneiden – sonst knackt es
                int fadeFrom = soft && m < n ? Math.Max(0, m - FadeLen) : int.MaxValue;
                if (FadeIn > 0 || fadeFrom < m)
                {
                    for (int i = 0; i < m; i++)
                    {
                        int k = ((ReadPos + i) % cap) * 2;
                        float g = 1f;
                        if (FadeIn > 0) { g = (FadeTotal - FadeIn + 1) / (float)FadeTotal; FadeIn--; }
                        if (i >= fadeFrom) g *= (m - i) / (float)(m - fadeFrom + 1);
                        dst[i * 2] += Ring[k] * g;
                        dst[i * 2 + 1] += Ring[k + 1] * g;
                    }
                }
                else
                    for (int i = 0; i < m; i++)
                    {
                        int k = ((ReadPos + i) % cap) * 2;
                        dst[i * 2] += Ring[k];
                        dst[i * 2 + 1] += Ring[k + 1];
                    }
                ReadPos = (ReadPos + m) % cap;
                Count -= m;
                if (m < n)
                {
                    Primed = false;
                    // echte Unterversorgung (Daten kamen gerade noch) -> etwas mehr Puffer
                    if (nowMs - LastPacketMs < 40 && PrimeFrames < 1920) PrimeFrames += 240;
                }
                return m > 0;
            }
        }

        private sealed class ChannelState
        {
            public readonly Biquad Low = new Biquad(), Mid = new Biquad(), High = new Biquad();
            public readonly Biquad[] Geq = { new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad() };
            public readonly Compressor Comp = new Compressor(Rate);
            public readonly Leveler Lev = new Leveler(Rate);
            public SpectrumAnalyzer Spectrum;
            public LoudnessSink Loud;       // 2.17: Lautheit des Kanals (vor dem Fader, auf Wunsch)
            public readonly ChannelFxState Fx = new ChannelFxState(Rate);   // 2.19: Hall/Echo
            public DelayLine Delay;         // 3.2: Verzoegerung (erst bei Bedarf)
            public AudioRing CueRing;       // 2.19: vorhoeren (vor dem Fader)
            public AudioRing StreamRing;
            public ChannelDsp Params;
            public float Gain;
            public float PeakL, PeakR;      // nach dem Fader
            public float PreL, PreR;        // vor dem Fader (nach EQ)
            // 3.4.1: weich umschalten statt knacken
            public float CompMix, LevMix;   // Anteil mit Kompressor / AUTO (0..1, gleitet)
            public bool CompRun, LevRun;
            public int StereoCur, StereoPrev;
            public float StereoMix = 1f;
            public Biquad OldLow, OldMid, OldHigh;
            public Biquad[] OldGeq;
            public bool[] OldGeqOn;
            public int EqFade;              // so viele Frames wird noch vom alten zum neuen EQ uebergeblendet
            // 3.4.6: Bass-Filter, Stereo-Breite, Knall-Bremse
            public readonly Biquad Hp = new Biquad();
            public Biquad OldHp; public bool OldHpOn;
            public float WidthCur = 1f;
            public readonly PopGuard Pop = new PopGuard(Rate);
            public float PopMix; public bool PopRun;
            // 3.5: Hoehen-Filter, Stimmen nach vorne
            public readonly Biquad Lp = new Biquad();
            public Biquad OldLp; public bool OldLpOn;
            public readonly DialogLift Dialog = new DialogLift(Rate);
            // 4.0: parametrischer EQ, EQ/Kompressor der Gruppe
            public readonly PeqState Peq = new PeqState(Rate);
            public readonly Biquad GLow = new Biquad(), GMid = new Biquad(), GHigh = new Biquad();
            public GroupFx GParams;
            public float GEqMix, GCompMix;
            public bool GEqRun, GCompRun;
            public readonly Compressor GComp = new Compressor(Rate);
            public float DialogMix; public bool DialogRun;
            // 4.0 (finale): Profi-Ton
            public ProState Pro;
            public CompSpec CompSpec;
        }

        /// <summary>3.4.1: Rest einer Aufnahme, die den EQ-Weg verlaesst – wird kurz ausgeblendet statt hart abgeschnitten.</summary>
        private sealed class Tail
        {
            public string ChannelId;
            public float[] Ring;
            public int ReadPos, Count, Total;
            public long Born;
        }
        private readonly List<Tail> _tails = new List<Tail>();
        internal const int SwitchFadeFrames = 1440;      // 30 ms: EQ, AUTO
        internal const int CompFadeFrames = 1920;        // 40 ms: Kompressor (bis +8 dB Aufholverstaerkung)
        internal const int StereoFadeFrames = 960;       // 20 ms: Mono / L-R getauscht
        internal const int TailFrames = 480;             // 10 ms: Rest beim Verlassen des EQ-Wegs
        private float[] _dryBuf = new float[Rate * 2], _oldBuf = new float[Rate * 2];

        /// <summary>Anteil m (0..1) gleitet je Frame um step Richtung target; wet = dry + (wet - dry) * m. Liefert das neue m.</summary>
        internal static float MixToward(float[] wet, float[] dry, int n, float m, float target, float step)
        {
            if (m == target && m >= 1f) return m;         // ganz an: wet bleibt
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                if (m < target) { m += step; if (m > target) m = target; }
                else if (m > target) { m -= step; if (m < target) m = target; }
                wet[k] = dry[k] + (wet[k] - dry[k]) * m;
                wet[k + 1] = dry[k + 1] + (wet[k + 1] - dry[k + 1]) * m;
            }
            return m;
        }

        private static void StereoTx(int mode, float l, float r, out float ol, out float or)
        {
            if (mode == 1) { ol = or = (l + r) * 0.5f; }
            else if (mode == 2) { ol = r; or = l; }
            else { ol = l; or = r; }
        }

        /// <summary>Grosse EQ-Aenderung (an/aus, A/B, Voreinstellung): mehr als 1,5 dB in einem Band.</summary>
        internal static bool BigEqChange(ChannelDsp a, ChannelDsp b)
        {
            if (a == null || b == null) return false;
            if ((a.Extra?.HpHz ?? 0) != (b.Extra?.HpHz ?? 0)) return true;   // 3.4.6: Bass-Filter an/aus/anders
            if ((a.Extra?.LpHz ?? 0) != (b.Extra?.LpHz ?? 0)) return true;   // 3.5: Hoehen-Filter
            if (Math.Abs(a.Low - b.Low) > 1.5f || Math.Abs(a.Mid - b.Mid) > 1.5f || Math.Abs(a.High - b.High) > 1.5f) return true;
            for (int i = 0; i < 10; i++)
            {
                float x = a.Graphic != null ? a.Graphic[i] : 0f, y = b.Graphic != null ? b.Graphic[i] : 0f;
                if (Math.Abs(x - y) > 1.5f) return true;
            }
            return false;
        }

        private static bool GeqOn(ChannelDsp p, int b) => p?.Graphic != null && Math.Abs(p.Graphic[b]) > 0.01f;

        private void AddTail(Capture c)
        {
            if (!CleanStart.On || c.ChannelId == null || c.Ring == null || c.Count <= 0 || !c.Primed) return;
            int n = Math.Min(c.Count, TailFrames);
            _tails.Add(new Tail { ChannelId = c.ChannelId, Ring = c.Ring, ReadPos = c.ReadPos, Count = n, Total = n, Born = _clock.ElapsedMilliseconds });
            c.Ring = null; c.Count = 0; c.ReadPos = 0; c.Primed = false;
        }

        /// <summary>Reste fuer diesen Kanal ausgeblendet dazugeben. true = etwas dazugegeben.</summary>
        private bool ReadTails(string channelId, float[] dst, int n)
        {
            bool any = false;
            for (int t = _tails.Count - 1; t >= 0; t--)
            {
                var tl = _tails[t];
                if (tl.ChannelId != channelId) continue;
                int cap = tl.Ring.Length / 2;
                int m = Math.Min(n, tl.Count);
                for (int i = 0; i < m; i++)
                {
                    int k = ((tl.ReadPos + i) % cap) * 2;
                    float g = (tl.Count - i) / (float)(tl.Total + 1);
                    dst[i * 2] += tl.Ring[k] * g;
                    dst[i * 2 + 1] += tl.Ring[k + 1] * g;
                }
                tl.ReadPos = (tl.ReadPos + m) % cap;
                tl.Count -= m;
                any |= m > 0;
                if (tl.Count <= 0) _tails.RemoveAt(t);
            }
            return any;
        }

        private sealed class Output
        {
            public string Key;      // "" = Standardgeraet, sonst Geraete-ID; "#stream:<id>" / "#voice:<id>"
            public string DeviceId;
            public bool IsStream => Key.StartsWith(StreamPrefix);
            public bool IsVoice => Key.StartsWith(VoicePrefix);
            public bool IsCue => Key.StartsWith(CuePrefix);
            public bool IsMirror => Key.StartsWith(MirrorPrefix);   // 3.5
            public readonly Limiter Limiter = new Limiter(Rate);
            public OutputFxState Fx;        // 2.19: EQ/Raumklang des Ausgangs
            public IMMDevice Device;
            public IAudioClient Client;
            public IAudioRenderClient Render;
            public AutoResetEvent Event;
            public uint BufferFrames;
            public int Target;
            public bool Started;
            public int FadeIn;              // 3.3.1: nach einem Aussetzer weich wieder einsetzen
        }

        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _running = true;
        private volatile Dictionary<uint, string> _wanted = new Dictionary<uint, string>();
        private volatile Dictionary<string, ChannelDsp> _channels = new Dictionary<string, ChannelDsp>();
        private volatile Dictionary<string, float[]> _peaks = new Dictionary<string, float[]>();
        private volatile HashSet<uint> _meterWanted = new HashSet<uint>();
        private volatile Dictionary<uint, float[]> _pidPeaks = new Dictionary<uint, float[]>();
        private readonly ConcurrentDictionary<uint, string> _failed = new ConcurrentDictionary<uint, string>();
        private volatile string _status;
        private readonly ConcurrentDictionary<string, long> _brokenOutputs = new ConcurrentDictionary<string, long>();
        private volatile bool _softGain = true;
        private volatile float _ceiling;                // Gehoerschutz (linear), 0 = aus
        private volatile Dictionary<string, float> _comp = new Dictionary<string, float>();

        // Kanal-Aufnahme
        private readonly object _recLock = new object();
        private string _recChannel;
        private WavWriter _recWriter;
        private volatile HashSet<uint> _recPids = new HashSet<uint>();
        private long _recLastMs, _recStartMs, _recFrames;
        private bool _recTapped;                        // Kanal lief zuletzt ueber den EQ-Weg
        private float[] _rec = new float[Rate * 2];

        // Stream-Mix, Sprach-Ausgang, Soundboard, Spektrum
        public const string StreamPrefix = "#stream:", VoicePrefix = "#voice:", StreamRecordKey = "#stream", CuePrefix = "#cue:";
        // 2.19: Ausgangs-Klang, Vorhoeren, Mehrspur-Aufnahme
        private volatile Dictionary<string, OutputFx> _outFx;
        private volatile CueMix _cue;
        private volatile Dictionary<string, TrackTap> _multi;
        private float _cueGain, _cuePkL, _cuePkR;
        private readonly Limiter _cueLimiter = new Limiter(Rate);
        private volatile StreamMix _stream;
        private volatile Dictionary<uint, string> _streamPids = new Dictionary<uint, string>();
        private volatile VoiceOut _voice;
        private readonly ConcurrentQueue<SoundVoice> _newSounds = new ConcurrentQueue<SoundVoice>();
        private volatile int _soundsPlaying;
        private volatile string _spectrumChannel;
        private volatile float[] _spectrum;
        private volatile Dictionary<string, float> _levGain = new Dictionary<string, float>();
        private volatile float[] _streamPeak = new float[2];
        private volatile float _voicePeak, _voiceOutPeak;
        private volatile string _voiceStatus;

        // nur im Mischer-Thread
        private readonly List<SoundVoice> _sounds = new List<SoundVoice>();
        private float _streamGain, _streamPkL, _streamPkR;
        private float _voiceMicGain, _voicePk, _voiceOutPk;   // Mikro im Sprach-Ausgang beginnt stumm und blendet ein
        private readonly NoiseGate _gate = new NoiseGate(Rate);
        private readonly VoiceFxChain _voiceFx = new VoiceFxChain(Rate);
        private readonly Limiter _streamLimiter = new Limiter(Rate);
        private IAudioClient _micClient;
        private IAudioCaptureClient _micCapture;
        private string _micDeviceId;
        private int _micChannels, _micRate;
        private bool _micFloat;
        private StreamResampler _micResampler;
        private long _micRetryAt, _micCheckAt;
        private readonly AudioRing _micRing = new AudioRing(Rate, 960, 4800);
        private float[] _micBuf = new float[Rate * 2], _micRaw = new float[Rate * 8], _micConv = new float[Rate * 2];
        private short[] _micRaw16 = new short[Rate * 8];
        private float[] _aux = new float[Rate * 2];
        private long _nextSpectrumMs;
        /// <summary>2.16: so oft (ms) werden die Pegel weitergegeben – Echtzeit 10, sonst 30.</summary>
        public volatile int PeakMs = 30;
        /// <summary>
        /// 3.1.2: Kanal-Pegel so schnell wie die Ausgaenge. Die Mess-Aufnahmen der Apps wurden nur alle 15 ms (bzw. im
        /// Takt der EQ-Ausgaenge) abgeholt und erst nach dem naechsten Warten weitergegeben – die Kanal-Pegel kamen so im
        /// Schnitt 15–20 ms nach den Ausgangs-Pegeln und in ungleichen Schueben. Jetzt weckt jedes neue Paket den Faden,
        /// und der Pegel geht sofort weiter. Braucht lueckenlose Pegel (sonst koennten Spitzen zwischen zwei Abholungen fehlen).
        /// </summary>
        public static bool FastMeters => LevelFlow.ChannelFast && LevelFlow.Seamless;
        private int _drained;            // Mess-Pakete seit dem letzten Weitergeben
        private long _lastMeterDrain;    // 3.4.1
        private long _nextPidPublish;    // 3.3.1: Mess-Pegel nicht oefter als alle 2 ms weitergeben
        private bool _meterWaitPartial;  // nicht alle Mess-Aufnahmen passten in die Warteliste

        private readonly Dictionary<uint, Capture> _captures = new Dictionary<uint, Capture>();
        private readonly Dictionary<string, ChannelState> _states = new Dictionary<string, ChannelState>();
        private readonly Dictionary<string, Output> _outputs = new Dictionary<string, Output>();
        private readonly Dictionary<string, int> _openFailures = new Dictionary<string, int>();
        private readonly Dictionary<string, long> _reopenAfter = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _idleSince = new Dictionary<string, long>();
        private float[] _tmp = new float[Rate * 2];
        private float[] _mix = new float[Rate * 2];
        private float[] _chan = new float[Rate * 2];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public DspMixer()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Mischpult EQ", Priority = ThreadPriority.Highest };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private long _statusAt;

        /// <summary>Statusmeldung fuer die Oberflaeche (null = alles gut). Meldungen verfallen nach 20 s.</summary>
        public string Status => _status != null && _clock.ElapsedMilliseconds - _statusAt < 20000 ? _status : null;

        private void SetStatus(string text)
        {
            _status = text;
            _statusAt = _clock.ElapsedMilliseconds;
        }

        /// <summary>Die Ausgabe dieses Kanals laesst sich nicht oeffnen -> Apps laufen auf dem direkten Weg.</summary>
        public bool IsChannelBroken(string channelId)
        {
            var ch = _channels;
            return channelId != null && ch.TryGetValue(channelId, out var p) && _brokenOutputs.ContainsKey(p.OutputKey);
        }

        public bool IsFailed(uint pid) => _failed.ContainsKey(pid);

        /// <summary>Fehlschlaege bleiben gemerkt, solange der Prozess lebt (sonst wuerde staendig neu versucht).</summary>
        public void ForgetDeadPids(HashSet<uint> alive)
        {
            foreach (var pid in new List<uint>(_failed.Keys))
                if (!alive.Contains(pid)) _failed.TryRemove(pid, out _);
        }

        public Dictionary<string, float[]> Peaks => _peaks;

        // ---- 3.0.2: lueckenlos – bis die Engine abholt, sammeln sich die Spitzen aller Messfenster (Maximum)
        private readonly object _takeLock = new object();
        private bool _peaksTaken = true, _pidTaken = true, _eqTaken = true;

        /// <summary>Kanal-Pegel seit dem letzten Abholen (null = seitdem kein neues Messfenster).</summary>
        public Dictionary<string, float[]> TakePeaks()
        {
            lock (_takeLock) { if (_peaksTaken) return null; _peaksTaken = true; return _peaks; }
        }

        /// <summary>Prozess-Pegel (eq: EQ-Weg, sonst Mess-Aufnahmen) seit dem letzten Abholen (null = nichts Neues).</summary>
        public Dictionary<uint, float[]> TakePidPeaks(bool eq)
        {
            lock (_takeLock)
            {
                if (eq) { if (_eqTaken) return null; _eqTaken = true; return _eqPidPeaks; }
                if (_pidTaken) return null; _pidTaken = true; return _pidPeaks;
            }
        }

        private static void MaxInto<TK>(Dictionary<TK, float[]> into, Dictionary<TK, float[]> old)
        {
            if (old == null) return;
            foreach (var kv in old)
            {
                if (kv.Value == null) continue;
                if (!into.TryGetValue(kv.Key, out var cur)) { into[kv.Key] = (float[])kv.Value.Clone(); continue; }
                for (int i = 0; i < cur.Length && i < kv.Value.Length; i++) if (kv.Value[i] > cur[i]) cur[i] = kv.Value[i];
            }
        }

        /// <summary>Gemessener Pegel pro Prozess (nur Mess-Aufnahmen): was die App wirklich abspielt.</summary>
        public Dictionary<uint, float[]> PidPeaks => _pidPeaks;

        /// <summary>Pegel pro Prozess auf dem EQ-Weg (nach Rueckverstaerkung, vor EQ und Fader).</summary>
        public Dictionary<uint, float[]> EqPidPeaks => _eqPidPeaks;
        private volatile Dictionary<uint, float[]> _eqPidPeaks = new Dictionary<uint, float[]>();

        /// <summary>
        /// Prozesse, deren Pegel per Prozess-Loopback gemessen werden soll. Windows' Session-Pegel zeigt bei Apps,
        /// die den Desktop-Ton mitschneiden (z. B. Discord, OBS), den kompletten Ausgabeton – die Messung hier nicht.
        /// </summary>
        public void SetMeterPids(HashSet<uint> pids)
        {
            _meterWanted = pids ?? new HashSet<uint>();
            _wake.Set();
        }

        public void SetCaptures(Dictionary<uint, string> pidToChannel)
        {
            _wanted = pidToChannel ?? new Dictionary<uint, string>();
            _wake.Set();
        }

        private volatile Dictionary<uint, float> _appGains = new Dictionary<uint, float>();

        private readonly ConcurrentDictionary<uint, long> _settleUntil = new ConcurrentDictionary<uint, long>();

        /// <summary>
        /// 3.0.3: Die Session dieses Prozesses wurde gerade auf -80 dB gesetzt. Windows blendet das ueber ein paar
        /// Millisekunden – was in der Zeit noch unabgesenkt ankommt, ist ein Uebergang (die App war kurz direkt zu hoeren)
        /// und wird weder angehoben noch doppelt abgespielt.
        /// </summary>
        public void NoteAttenuated(uint pid)
        {
            if (pid != 0) _settleUntil[pid] = _clock.ElapsedMilliseconds + SettleMs;
        }

        private readonly ConcurrentDictionary<uint, long> _verifiedSince = new ConcurrentDictionary<uint, long>();

        /// <summary>
        /// 3.4.1: Von der Engine nachgelesen (alle 100 ms): Sessions dieses Prozesses stehen wirklich auf -80 dB (true) bzw. nicht.
        /// Nur dann darf eine dauerhaft laute Aufnahme als "Windows liefert vor der Lautstaerke" gelten.
        /// </summary>
        public void NoteVerified(Dictionary<uint, bool> attenuated)
        {
            long now = _clock.ElapsedMilliseconds;
            foreach (var pid in new List<uint>(_verifiedSince.Keys))
                if (attenuated == null || !attenuated.TryGetValue(pid, out bool ok) || !ok) _verifiedSince.TryRemove(pid, out _);
            if (attenuated == null) return;
            foreach (var kv in attenuated) if (kv.Value) _verifiedSince.TryAdd(kv.Key, now);
        }

        /// <summary>3.4.1 Protokoll: die ersten Bloecke nach dem Start einer Aufnahme (Spitze -> Verstaerkung) in einer Zeile.</summary>
        private static void LogBlock(Capture c, float peak, float gain, bool settling)
        {
            string g = gain == 0f ? "still" : gain >= Makeup * 0.99f ? "x10000" : gain == 1f ? "x1" : "x" + SoundLog.F(gain);
            c.LogText = (c.LogText == null ? "" : c.LogText + ", ") + SoundLog.F(peak) + (settling ? "~" : "") + ">" + g;
            if (--c.LogBlocks == 0)
            {
                SoundLog.Write("EQ-Weg PID " + c.Pid + " erste Bloecke (Spitze>Verstaerkung, ~ = Uebergang): " + c.LogText);
                c.LogText = null;
            }
        }

        /// <summary>So lange (ms) steht die Session nachweislich auf -80 dB, -1 = nicht.</summary>
        private long VerifiedMs(uint pid, long now) => _verifiedSince.TryGetValue(pid, out long since) ? now - since : -1;

        /// <summary>
        /// 3.0.3: Verstaerkung fuer einen Block der EQ-Aufnahme. Bisher: leise Bloecke immer x10000 (+80 dB) – kam ein
        /// Block noch (teilweise) unabgesenkt an, z. B. beim Start oder waehrend Windows die Lautstaerke herunterfaehrt,
        /// wurde er viel zu laut (Knacken, Pegel springt hoch). Jetzt nur anheben, wenn das Ergebnis plausibel ist.
        /// </summary>
        internal static float CaptureGain(float peak, bool preVolume, bool soft, bool settling)
        {
            bool loud = peak > LoudThreshold;
            if (preVolume) return 1f;
            if (!soft) return loud ? 1f : Makeup;                // wie bis 3.0.2
            if (peak * Makeup <= MaxPlausible) return Makeup;     // richtig abgesenkt: exakt anheben
            if (settling) return 0f;                              // Uebergang: die App war gerade direkt zu hoeren
            return loud ? 1f : 0f;                                // unabgesenkt (Windows liefert vor der Lautstaerke) bzw. halb abgesenkt
        }

        /// <summary>
        /// 3.3.1: Wie liefert Windows die Prozess-Aufnahme auf diesem PC? 0 unbekannt, 1 nach der Session-Lautstaerke
        /// (abgesenkt, muss angehoben werden), 2 vor der Lautstaerke (kommt unabgesenkt). Einmal erkannt, gilt es fuer alle
        /// weiteren Aufnahmen – bisher wurde bei jedem Start und jedem EQ-Umschalten neu geraten, und leise Stellen wurden
        /// dabei bis zu 80 dB zu laut (Knacken, Pegel springt hoch).
        /// </summary>
        public static volatile int LoopbackKind;
        internal const float MaxPlausibleSoft = 1.25f;   // angehoben hoechstens knapp ueber Vollaussteuerung

        /// <summary>
        /// 3.3.1: Verstaerkung eines Blocks der EQ-Aufnahme ("Kein Knacken beim Abspielen" an). Strenger als 3.0.3:
        /// – im Uebergang wird nur angehoben, was danach hoechstens knapp ueber Vollaussteuerung liegt (bisher bis 4-fach = +12 dB),
        /// – war die Aufnahme ausserhalb eines Uebergangs schon laut, sind leise Stellen leise Musik (vor der Lautstaerke)
        ///   und werden nicht verzehnausendfacht,
        /// – faellt der Pegel nach dem Absenken sprunghaft ab, ist klar: Windows liefert abgesenkt (merken).
        /// </summary>
        private static float SoftCaptureGain(Capture c, float peak, bool loud, bool settling, long nowMs, long verifiedMs)
        {
            // 3.4.1: Windows liefert die Prozess-Aufnahme nach der App-Lautstaerke (wie bei OBS "Anwendungsaudio"). Bisher wurde
            // geraten: ein lauter Block ausserhalb eines Uebergangs galt als "Windows liefert vor der Lautstaerke" – und ab dann
            // wurde alles ohne Anheben abgespielt bzw. leise Stellen x10000. Laut wird es aber genau dann, wenn eine App ihre
            // Windows-Lautstaerke selbst hochsetzt (z. B. Spotify beim Abspielen) oder beim Start noch nicht abgesenkt ist:
            // dann ist sie direkt zu hoeren und darf nicht noch einmal (verstaerkt) dazukommen.
            if (c.PreVolume) return 1f;
            // im Uebergang (gerade abgesenkt bzw. Aufnahme gerade erst begonnen) streng: was dabei "halb abgesenkt" ankommt,
            // wuerde angehoben viel zu laut. Sonst darf heisse Musik (Spitzen ueber 0 dBFS) wie bisher bis +12 dB haben.
            float maxP = settling || nowMs - c.StartedMs < 300 ? MaxPlausibleSoft : MaxPlausible;
            if (peak * Makeup <= maxP)
            {
                c.LoudSince = -1;
                return Makeup;                       // richtig abgesenkt: exakt anheben
            }
            if (loud)
            {
                if (c.LoudSince < 0) c.LoudSince = nowMs;
                // nur mit Beweis: die Session steht nachweislich seit 1,5 s auf -80 dB und die Aufnahme ist trotzdem seit 1 s laut
                if (!settling && verifiedMs >= 1500 && nowMs - c.LoudSince >= 1000)
                {
                    c.PreVolume = true;
                    LoopbackKind = 2;
                    SoundLog.Write("EQ-Weg PID " + c.Pid + ": Session nachweislich auf -80 dB, Aufnahme trotzdem laut -> Windows liefert vor der Lautstaerke");
                    return 1f;
                }
            }
            return 0f;                               // nicht (ganz) abgesenkt: die App ist gerade direkt zu hoeren – nicht doppelt
        }

        /// <summary>Eigene Lautstaerke einzelner Apps auf dem EQ-Weg (Prozess -> 0..1; fehlt = 1).</summary>
        public void SetCaptureGains(Dictionary<uint, float> gains)
        {
            _appGains = gains ?? new Dictionary<uint, float>();
        }

        public void SetChannels(Dictionary<string, ChannelDsp> channels)
        {
            _channels = channels ?? new Dictionary<string, ChannelDsp>();
            _wake.Set();
        }

        /// <summary>Lautstaerke-Aenderungen der EQ-Kanaele weich (ca. 30 ms) statt pro Block.</summary>
        public bool SoftGain { set { _softGain = value; } }

        /// <summary>Gehoerschutz: hoechster Ausgangspegel (linear), 0 = aus.</summary>
        public float Ceiling { set { _ceiling = value; } }

        /// <summary>Pegelabsenkung der Kompressoren pro Kanal (dB, positiv).</summary>
        public Dictionary<string, float> CompReduction => _comp;

        /// <summary>Kanal-Aufnahme starten (writer == null beendet sie).</summary>
        public void SetRecording(string channelId, WavWriter writer)
        {
            lock (_recLock)
            {
                _recChannel = writer == null ? null : channelId;
                _recWriter = writer;
                _recFrames = 0;
                _recStartMs = _recLastMs = _clock.ElapsedMilliseconds;
                _recTapped = false;
            }
            _wake.Set();
        }

        /// <summary>Stream-Mix (null = aus).</summary>
        public void SetStream(StreamMix mix) { _stream = mix; _wake.Set(); }

        /// <summary>2.19: EQ/Raumklang je Ausgang ("" = Standardgeraet).</summary>
        public void SetOutputFx(Dictionary<string, OutputFx> fx) { _outFx = fx == null || fx.Count == 0 ? null : fx; }

        /// <summary>2.19: Vorhoeren (null = aus).</summary>
        public void SetCue(CueMix cue) { _cue = cue; _wake.Set(); }

        /// <summary>2.19: Mehrspur-Aufnahme (Kanal-ID -> Datei), null = aus.</summary>
        public void SetMultitrack(Dictionary<string, WavWriter> writers)
        {
            if (writers == null || writers.Count == 0) { _multi = null; return; }
            long now = _clock.ElapsedMilliseconds;
            var d = new Dictionary<string, TrackTap>();
            foreach (var kv in writers) if (kv.Value != null) d[kv.Key] = new TrackTap(kv.Value, Rate, now);
            _multi = d;
            _wake.Set();
        }

        /// <summary>Pegel des Vorhoerens [L, R].</summary>
        public float[] CuePeak => new[] { _cuePkL, _cuePkR };

        /// <summary>Prozesse ohne EQ, deren Ton in den Stream-Mix geht (Prozess -> Kanal). Muessen auch in SetMeterPids stehen.</summary>
        public void SetStreamPids(Dictionary<uint, string> pids) { _streamPids = pids ?? new Dictionary<uint, string>(); _wake.Set(); }

        /// <summary>Sprach-Ausgang (Mikro + Soundboard auf ein Geraet, z. B. VB-Cable), null = aus.</summary>
        public void SetVoice(VoiceOut v) { _voice = v; _wake.Set(); }

        public void PlaySound(SoundClip clip, float gain, bool me, bool stream, bool voice)
        {
            if (clip == null || clip.Frames <= 0) return;
            _newSounds.Enqueue(new SoundVoice { Clip = clip, Gain = gain, Me = me, Stream = stream, Voice = voice, Started = _clock.ElapsedMilliseconds });
            _wake.Set();
        }

        /// <summary>Alle Sounds stoppen (in der Reihenfolge der Befehle: danach gestartete Sounds spielen).</summary>
        public void StopSounds() { _newSounds.Enqueue(null); _wake.Set(); }

        /// <summary>3.2: Sound nur fuer mich auf einem bestimmten Geraet (null = Standardgeraet).</summary>
        public void PlaySoundOn(SoundClip clip, float gain, string deviceId)
        {
            if (clip == null || clip.Frames <= 0) return;
            _newSounds.Enqueue(new SoundVoice { Clip = clip, Gain = gain, Me = true, MeKey = deviceId ?? "", Started = _clock.ElapsedMilliseconds });
            _wake.Set();
        }

        /// <summary>Kanal, dessen Spektrum berechnet wird (null = keiner).</summary>
        public void SetSpectrumChannel(string channelId) { _spectrumChannel = channelId; }

        public float[] Spectrum => _spectrum;
        public string SpectrumChannel => _spectrumChannel;
        public Dictionary<string, float> LevelerGain => _levGain;
        public float[] StreamPeak => _streamPeak;
        public float VoicePeak => _voicePeak;
        public float VoiceOutPeak => _voiceOutPeak;
        public string VoiceStatus => _voiceStatus;
        public int SoundsPlaying => _soundsPlaying;
        public bool VoiceActive => _micCapture != null;

        /// <summary>Prozesse (ohne EQ), deren Ton fuer die Kanal-Aufnahme gebraucht wird. Muessen auch in SetMeterPids stehen.</summary>
        public void SetRecordPids(HashSet<uint> pids)
        {
            _recPids = pids ?? new HashSet<uint>();
            _wake.Set();
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();
            try { _thread.Join(1500); } catch { }
        }

        // ------------------------------------------------------------------ Thread

        private void Run()
        {
            ThreadCpu.Register("EQ");   // 3.4.1
            IntPtr mmcss = IntPtr.Zero;
            try { uint idx = 0; mmcss = Wasapi.AvSetMmThreadCharacteristics("Pro Audio", ref idx); } catch { }

            IMMDeviceEnumerator en = null;
            try { en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject(); }
            catch (Exception ex) { SetStatus("EQ: Windows-Audio nicht verfügbar (" + ex.Message + ")"); }

            long nextDefaultCheck = 0, nextPeaks = 0;
            var waitList = new List<WaitHandle>();

            while (_running)
            {
                long now = _clock.ElapsedMilliseconds;
                try
                {
                    Reconcile(now);

                    // Defekte Ausgaenge nach 30 s erneut versuchen
                    foreach (var kv in _brokenOutputs)
                        if (now - kv.Value > 30000) { _brokenOutputs.TryRemove(kv.Key, out _); _openFailures.Remove(kv.Key); }

                    // Soundboard: neue Sounds uebernehmen, beendete entfernen
                    {
                        var smx = _stream; var vo = _voice;
                        string sk = smx?.DeviceId == null ? null : StreamPrefix + smx.DeviceId;
                        string vk = vo?.DeviceId == null ? null : VoicePrefix + vo.DeviceId;
                        while (_newSounds.TryDequeue(out var sv))
                        {
                            if (sv == null) { _sounds.Clear(); continue; } // Stopp
                            _sounds.Add(sv);
                        }
                        // Ziele, die es nicht (mehr) gibt, nicht abwarten
                        foreach (var v in _sounds)
                        {
                            if (sk == null || _brokenOutputs.ContainsKey(sk)) v.Stream = false;
                            if (vk == null || _brokenOutputs.ContainsKey(vk)) v.Voice = false;
                            if (_brokenOutputs.ContainsKey(v.MeKey ?? "")) v.Me = false;
                        }
                        _sounds.RemoveAll(v => !v.Me && !v.Stream && !v.Voice);
                    }
                    _sounds.RemoveAll(v => v.Done || now - v.Started > v.Clip.Frames * 1000L / Rate + 8000);
                    if (_tails.Count > 0) _tails.RemoveAll(t => now - t.Born > 300);   // 3.4.1: Kanal spielt nicht mehr
                    _soundsPlaying = _sounds.Count;

                    // Welche Ausgaenge werden gebraucht? (Kanaele mit laufender/anstehender Aufnahme)
                    var needed = new HashSet<string>();
                    var channels = _channels;
                    foreach (var c in _captures.Values)
                        if (c.ChannelId != null && channels.TryGetValue(c.ChannelId, out var cp)) needed.Add(cp.OutputKey);
                    foreach (var v in _sounds) if (v.Me && v.PosMe < v.Clip.Frames) needed.Add(v.MeKey ?? "");
                    var stream = _stream;
                    if (stream != null && stream.DeviceId != null) needed.Add(StreamPrefix + stream.DeviceId);
                    var voice = _voice;
                    if (voice != null && voice.DeviceId != null) needed.Add(VoicePrefix + voice.DeviceId);
                    var cue = _cue;
                    if (cue != null && cue.DeviceId != null)
                        foreach (var cp2 in channels.Values) if (cp2.Cue) { needed.Add(CuePrefix + cue.DeviceId); break; }
                    var mirror = _mirrorFeed;   // 3.5: zweites Geraet
                    if (mirror != null && mirror.TargetId != null)
                    {
                        // nie auf das Geraet, von dem aufgenommen wird (sonst Rueckkopplung)
                        if (en != null && now >= _mirrorDefCheck) { _mirrorDefCheck = now + 1000; _mirrorDefId = DefaultDeviceId(en); }
                        if (_mirrorDefId != null && !string.Equals(_mirrorDefId, mirror.TargetId, StringComparison.OrdinalIgnoreCase))
                            needed.Add(MirrorPrefix + mirror.TargetId);
                    }
                    if (NoiseWanted) needed.Add("");   // 3.5: Einschlaf-Geraeusche aufs Standardgeraet
                    var gen400 = Pro400.Gen;   // 4.0 (finale): Signalgenerator
                    if (gen400 != null) needed.Add(gen400.DeviceKey ?? "");

                    // Mikro fuer den Sprach-Ausgang
                    if (en != null) UpdateMic(en, voice, now);

                    if (en != null)
                    {
                        foreach (var key in needed)
                        {
                            _idleSince.Remove(key);
                            if (_outputs.ContainsKey(key) || _brokenOutputs.ContainsKey(key)) continue;
                            if (_reopenAfter.TryGetValue(key, out long ra) && now < ra) continue;
                            if (OpenOutput(en, key))
                            {
                                _openFailures.Remove(key);
                            }
                            else
                            {
                                _openFailures.TryGetValue(key, out int f);
                                _openFailures[key] = ++f;
                                _reopenAfter[key] = now + 2000;
                                if (f >= 2) _brokenOutputs[key] = now;
                            }
                        }
                    }

                    // nicht mehr gebrauchte Ausgaenge nach 3 s schliessen (Stream/Sprach-Ausgang sofort, z. B. bei Geraetewechsel)
                    foreach (var key in new List<string>(_outputs.Keys))
                    {
                        if (needed.Contains(key)) continue;
                        if (key.StartsWith("#")) { CloseOutput(key); _idleSince.Remove(key); continue; }
                        if (!_idleSince.TryGetValue(key, out long since)) _idleSince[key] = now;
                        else if (now - since > 3000) { CloseOutput(key); _idleSince.Remove(key); }
                    }

                    // Standardgeraet gewechselt?
                    if (en != null && now >= nextDefaultCheck && _outputs.TryGetValue("", out var defOut))
                    {
                        nextDefaultCheck = now + 1000;
                        string def = DefaultDeviceId(en);
                        if (def != null && !string.Equals(def, defOut.DeviceId, StringComparison.OrdinalIgnoreCase))
                            CloseOutput(""); // wird beim naechsten Durchlauf neu geoeffnet
                    }

                    bool fastMeters = FastMeters;
                    bool meterWake = false;
                    if (_outputs.Count == 0)
                    {
                        if (fastMeters)
                        {
                            // 3.1.2: warten, bis ein Mess-Paket da ist, dann sofort abholen und weitergeben
                            waitList.Clear();
                            waitList.Add(_wake);
                            AddMeterEvents(waitList);
                            if (waitList.Count > 1) WaitHandle.WaitAny(waitList.ToArray(), _meterWaitPartial ? 5 : 15);
                            else _wake.WaitOne(15);
                            // 3.4.1: hoechstens alle 3 ms abholen – mit mehreren Mess-Aufnahmen kamen die Pakete sonst fast jede ms
                            long since = _clock.ElapsedMilliseconds - _lastMeterDrain;
                            if (since >= 0 && since < 3) _wake.WaitOne((int)(3 - since));
                            now = _clock.ElapsedMilliseconds;
                            _lastMeterDrain = now;
                            DrainCaptures(now, discard: true);
                        }
                        else
                        {
                            DrainCaptures(now, discard: true);
                            _wake.WaitOne(15);
                        }
                    }
                    else
                    {
                        waitList.Clear();
                        foreach (var o in _outputs.Values) waitList.Add(o.Event);
                        int outCount = waitList.Count;
                        // 3.3.1: laeuft der EQ-Weg, nicht zusaetzlich bei jedem Mess-Paket aufwachen – die Ausgaenge wecken ohnehin
                        // alle ~10 ms, die Pegel gehen dann mit. So bleibt der Takt der Tonausgabe wie bis 3.1.1 (kein Knacken).
                        if (fastMeters && !CleanStart.On) AddMeterEvents(waitList);
                        int idx = WaitHandle.WaitAny(waitList.ToArray(), 30);
                        now = _clock.ElapsedMilliseconds;
                        DrainCaptures(now, discard: false);
                        // 3.1.2: nur ein Mess-Paket – abholen und weitergeben, die Ausgaenge laufen weiter in ihrem eigenen Takt
                        meterWake = idx >= outCount && idx != WaitHandle.WaitTimeout;
                        if (!meterWake)
                        {
                            DrainMic();
                            foreach (var key in new List<string>(_outputs.Keys))
                            {
                                if (!RenderCycle(_outputs[key], now))
                                {
                                    CloseOutput(key);
                                    _reopenAfter[key] = now + 500;
                                }
                            }
                        }
                    }

                    PumpRecording(_clock.ElapsedMilliseconds);
                    var multi = _multi;   // 2.19: Mehrspur – stille Spuren mit Stille fuellen
                    if (multi != null) { long pm = _clock.ElapsedMilliseconds; foreach (var t in multi.Values) t.Pump(pm); }

                    if (now >= nextPeaks && !meterWake)
                    {
                        nextPeaks = now + Math.Max(5, PeakMs);
                        _drained = 0;
                        PublishPeaks();
                    }
                    else if (fastMeters && _drained > 0 && now >= _nextPidPublish)
                    {
                        _nextPidPublish = now + 2;   // 3.3.1: hoechstens alle 2 ms (weniger Speicher-Muell im Ton-Faden)
                        // 3.1.2: neue Mess-Pakete sofort weitergeben (nur die App-Pegel; Stream, Sprache, EQ im gewohnten Takt)
                        _drained = 0;
                        PublishPidPeaks();
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("EQ-Fehler: " + ex.Message);
                    Thread.Sleep(50);
                }
            }

            foreach (var c in _captures.Values) try { c.Act.Close(); } catch { }
            _captures.Clear();
            CloseMic();
            foreach (var key in new List<string>(_outputs.Keys)) CloseOutput(key);
            ComUtil.Release(en);
            if (mmcss != IntPtr.Zero) try { Wasapi.AvRevertMmThreadCharacteristics(mmcss); } catch { }
        }

        // 3.4.1: Reconcile laeuft bei jedem Aufwachen (mit Mess-Aufnahmen mehrere hundert Mal pro Sekunde) – hat sich nichts
        // geaendert und laufen alle Aufnahmen, gleich zurueck (vorher jedes Mal neue Listen und Woerterbuecher)
        private object _rcWant, _rcMeter, _rcRec, _rcStream;
        private int _rcFailed = -1;
        private bool _rcSettled;

        private void Reconcile(long now)
        {
            var want = _wanted;
            var meter = _meterWanted;
            if (_rcSettled && ReferenceEquals(want, _rcWant) && ReferenceEquals(meter, _rcMeter) && ReferenceEquals(_recPids, _rcRec) &&
                ReferenceEquals(_streamPids, _rcStream) && _failed.Count == _rcFailed) return;
            _rcWant = want; _rcMeter = meter; _rcRec = _recPids; _rcStream = _streamPids; _rcFailed = _failed.Count;
            ReconcileFull(now, want, meter);
            bool settled = true;
            foreach (var c in _captures.Values) if (!c.Ready) { settled = false; break; }
            _rcSettled = settled && _failed.Count == _rcFailed;
        }

        private void ReconcileFull(long now, Dictionary<uint, string> want, HashSet<uint> meter)
        {

            foreach (var pid in new List<uint>(_captures.Keys))
            {
                if (want.ContainsKey(pid) || meter.Contains(pid)) continue;
                AddTail(_captures[pid]);   // 3.4.1: Rest ausblenden statt hart abschneiden
                _captures[pid].Act.Close();
                _captures.Remove(pid);
            }

            // gewuenschte Aufnahmen: EQ (mit Kanal) hat Vorrang vor reiner Messung
            var all = new Dictionary<uint, string>(want);
            foreach (var pid in meter) if (!all.ContainsKey(pid)) all[pid] = null;

            var rec = _recPids;
            var spids = _streamPids;
            foreach (var kv in all)
            {
                if (_failed.ContainsKey(kv.Key)) continue;
                bool record = kv.Value == null && rec.Contains(kv.Key);
                string streamCh = kv.Value == null && spids.TryGetValue(kv.Key, out var sc) ? sc : null;
                if (_captures.TryGetValue(kv.Key, out var c))
                {
                    if (c.StreamChannel != streamCh)
                    {
                        c.StreamChannel = streamCh;
                        if (streamCh == null) c.StreamRing = null;
                        else c.StreamRing = new AudioRing(Rate, 2400, 7200);
                    }
                    if (c.Record != record)
                    {
                        c.Record = record;
                        c.Count = 0; c.ReadPos = 0; c.Primed = false; c.PrimeFrames = record ? 2400 : 480;
                        if (!record) c.Ring = null;
                    }
                    if (c.ChannelId != kv.Value)
                    {
                        // Wechsel Messung <-> EQ: Puffer und Erkennung neu beginnen
                        AddTail(c);   // 3.4.1: verlaesst den EQ-Weg -> Rest ausblenden
                        c.ChannelId = kv.Value;
                        c.Count = 0; c.ReadPos = 0; c.Primed = false; c.PrimeFrames = 480;
                        c.StartedMs = now; c.LoudBlocks = 0; c.PreVolume = CleanStart.On && LoopbackKind == 2;   // 3.3.1: schon Bekanntes nicht neu raten
                        c.SawLoud = false; c.PreSuspect = false; c.PostVotes = 0;
                    }
                    if (!c.Ready)
                    {
                        if (c.Act.Done)
                        {
                            if (c.Act.Error != null || c.Act.Capture == null) Fail(c, c.Act.Error ?? "unbekannt");
                            else
                        {
                            c.Ready = true; c.LastPacketMs = now; c.StartedMs = now;   // 3.3.1: Erkennung ab dem ersten Paket
                            if (!c.MeterOnly && SoundLog.On) { c.LogBlocks = 30; c.LogText = null; SoundLog.Write("EQ-Weg: Aufnahme von PID " + c.Pid + " laeuft" + (c.PreVolume ? " (Windows liefert vor der Lautstaerke)" : "")); }
                        }
                        }
                        else if (now - c.StartedMs > 5000) Fail(c, "Zeitüberschreitung");
                    }
                    continue;
                }
                c = new Capture { Pid = kv.Key, ChannelId = kv.Value, StartedMs = now, Record = record, PrimeFrames = record ? 2400 : 480, PreVolume = CleanStart.On && LoopbackKind == 2,
                                  StreamChannel = streamCh, StreamRing = streamCh != null ? new AudioRing(Rate, 2400, 7200) : null };
                c.Act = ProcessLoopbackActivation.Begin(kv.Key, Rate);
                _captures[kv.Key] = c;
            }
        }

        private void Fail(Capture c, string reason)
        {
            _failed[c.Pid] = reason;
            if (!c.MeterOnly)
                SetStatus("EQ: Eine App ließ sich nicht abgreifen und läuft ohne EQ weiter (" + reason + ")");
            try { c.Act.Close(); } catch { }
            _captures.Remove(c.Pid);
        }

        private void DrainCaptures(long now, bool discard)
        {
            var appGains = _appGains;
            List<Capture> dead = null;
            foreach (var c in _captures.Values)
            {
                if (!c.Ready) continue;
                var cap = c.Act.Capture;
                try
                {
                    for (int guard = 0; guard < 64; guard++)
                    {
                        int hr = cap.GetNextPacketSize(out uint packet);
                        if (hr < 0) throw new COMException("GetNextPacketSize", hr);
                        if (packet == 0) break;
                        hr = cap.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                        if (hr < 0) throw new COMException("GetBuffer", hr);
                        int n = (int)frames;
                        bool silent = (flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == IntPtr.Zero;
                        if (n > 0 && c.MeterOnly)
                        {
                            _drained++;
                            AnalysisFor(c);
                            if (!silent)
                            {
                                if (n * 2 > _tmp.Length) _tmp = new float[n * 2];
                                Marshal.Copy(data, _tmp, 0, n * 2);
                                c.Loud?.Write(_tmp, n);
                                c.Spec?.Add(_tmp, n);
                                float pl = c.PeakL, pr = c.PeakR;
                                for (int i = 0; i < n * 2; i += 2)
                                {
                                    float l = Math.Abs(_tmp[i]), r = Math.Abs(_tmp[i + 1]);
                                    if (l > pl) pl = l;
                                    if (r > pr) pr = r;
                                }
                                c.PeakL = pl; c.PeakR = pr;
                                if (c.Record) c.Write(_tmp, n, 1f);
                                c.StreamRing?.Write(_tmp, 0, n);
                            }
                            else
                            {
                                if (c.Record) c.WriteSilence(n);
                                c.StreamRing?.WriteSilence(n);
                                c.Loud?.WriteSilence(n);
                                c.Spec?.AddSilence(n);
                            }
                            c.LastPacketMs = now;
                        }
                        else if (n > 0 && !discard)
                        {
                            if (n * 2 > _tmp.Length) _tmp = new float[n * 2];
                            if (silent)
                                c.WriteSilence(n);
                            else
                            {
                                Marshal.Copy(data, _tmp, 0, n * 2);
                                float peak = 0, pkl = 0, pkr = 0;
                                for (int i = 0; i < n * 2; i += 2)
                                {
                                    float l = Math.Abs(_tmp[i]), r = Math.Abs(_tmp[i + 1]);
                                    if (l > pkl) pkl = l; if (r > pkr) pkr = r;
                                }
                                peak = Math.Max(pkl, pkr);
                                bool loud = peak > LoudThreshold;
                                bool soft = CleanStart.On;
                                bool settling = false;
                                if (soft && _settleUntil.TryGetValue(c.Pid, out long su))
                                {
                                    if (now < su) settling = true;
                                    else _settleUntil.TryRemove(c.Pid, out _);
                                }
                                if (!soft && loud && !settling && now - c.StartedMs > 500 && !c.PreVolume && ++c.LoudBlocks >= 10)
                                    c.PreVolume = true;   // wie bis 3.3 (nur ohne "Kein Knacken")
                                float gain = soft ? SoftCaptureGain(c, peak, loud, settling, now, VerifiedMs(c.Pid, now)) : CaptureGain(peak, c.PreVolume, soft, settling);
                                if (c.LogBlocks > 0 && SoundLog.On) LogBlock(c, peak, gain, settling);
                                if (soft && gain > 0f && c.WasMuted)
                                {
                                    // nach einem stumm gelassenen Block weich einsetzen
                                    int fl = Math.Min(n, Capture.FadeLen);
                                    for (int i = 0, k = 0; i < fl; i++, k += 2) { float gg = (i + 1) / (float)fl; _tmp[k] *= gg; _tmp[k + 1] *= gg; }
                                }
                                c.WasMuted = soft && gain == 0f;
                                // Pegel dieser App (nach Rueckverstaerkung, vor ihrer eigenen Lautstaerke) fuer die Anzeige
                                if (pkl * gain > c.PeakL) c.PeakL = Math.Min(4f, pkl * gain);
                                if (pkr * gain > c.PeakR) c.PeakR = Math.Min(4f, pkr * gain);
                                // eigene Lautstaerke der App: hier im Mischer (wirkt auch, wenn Windows unabgesenkt liefert)
                                float ag = appGains.TryGetValue(c.Pid, out var agv) ? agv : 1f;
                                if (ag != c.AppGainCur)
                                {
                                    float g0 = c.AppGainCur, step = (ag - g0) / n;
                                    for (int i = 0, k = 0; i < n; i++, k += 2) { float gg = g0 + step * (i + 1); _tmp[k] *= gg; _tmp[k + 1] *= gg; }
                                    c.AppGainCur = ag;
                                    c.Write(_tmp, n, gain);
                                }
                                else c.Write(_tmp, n, gain * ag);
                            }
                            c.LastPacketMs = now;
                        }
                        cap.ReleaseBuffer(frames);
                    }
                }
                catch (Exception ex)
                {
                    (dead ?? (dead = new List<Capture>())).Add(c);
                    _ = ex;
                }
            }
            if (dead != null) foreach (var c in dead) Fail(c, "Aufnahme abgebrochen");
        }

        // ------------------------------------------------------------------ Ausgabe

        private static string DefaultDeviceId(IMMDeviceEnumerator en)
        {
            try
            {
                if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice d) != 0 || d == null) return null;
                d.GetId(out string id);
                ComUtil.Release(d);
                return id;
            }
            catch { return null; }
        }

        private bool OpenOutput(IMMDeviceEnumerator en, string key)
        {
            var o = new Output { Key = key };
            IntPtr fmt = IntPtr.Zero;
            try
            {
                string devKey = key.StartsWith(StreamPrefix) ? key.Substring(StreamPrefix.Length)
                              : key.StartsWith(VoicePrefix) ? key.Substring(VoicePrefix.Length)
                              : key.StartsWith(CuePrefix) ? key.Substring(CuePrefix.Length)
                              : key.StartsWith(MirrorPrefix) ? key.Substring(MirrorPrefix.Length) : key;
                int hr = devKey.Length == 0
                    ? en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out o.Device)
                    : en.GetDevice(devKey, out o.Device);
                if (hr != 0 || o.Device == null) throw new COMException(key.Length == 0 ? "Kein Standard-Ausgabegerät" : "Gerät nicht gefunden", hr);
                o.Device.GetId(out o.DeviceId);

                Guid iid = Wasapi.IID_IAudioClient;
                hr = o.Device.Activate(ref iid, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out object obj);
                if (hr < 0) throw new COMException("Activate", hr);
                o.Client = (IAudioClient)obj;

                fmt = Wasapi.AllocFloatStereoFormat(Rate);
                hr = o.Client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED,
                    Wasapi.AUDCLNT_STREAMFLAGS_EVENTCALLBACK | Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    500000 /* 50 ms */, 0, fmt, IntPtr.Zero);
                if (hr < 0) throw new COMException("Initialize", hr);

                o.Client.GetBufferSize(out o.BufferFrames);
                long period = 100000;
                if (o.Client.GetDevicePeriod(out long def, out _) == 0 && def > 0) period = def;
                int periodFrames = (int)(period * Rate / 10000000L);
                o.Target = Math.Min((int)o.BufferFrames - 16, periodFrames * 2 + 240);

                o.Event = new AutoResetEvent(false);
                hr = o.Client.SetEventHandle(o.Event.SafeWaitHandle.DangerousGetHandle());
                if (hr < 0) throw new COMException("SetEventHandle", hr);

                Guid rid = Wasapi.IID_IAudioRenderClient;
                hr = o.Client.GetService(ref rid, out object svc);
                if (hr < 0) throw new COMException("GetService", hr);
                o.Render = (IAudioRenderClient)svc;

                // mit Stille vorfuellen
                if (o.Render.GetBuffer((uint)o.Target, out IntPtr p) == 0)
                    o.Render.ReleaseBuffer((uint)o.Target, Wasapi.AUDCLNT_BUFFERFLAGS_SILENT);

                hr = o.Client.Start();
                if (hr < 0) throw new COMException("Start", hr);
                o.Started = true;
                _outputs[key] = o;
                if (_status != null && _status.StartsWith("EQ-Ausgabe")) _status = null;
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("EQ-Ausgabe konnte nicht geöffnet werden: " + ex.Message);
                CloseOutput(o);
                return false;
            }
            finally
            {
                if (fmt != IntPtr.Zero) Marshal.FreeHGlobal(fmt);
            }
        }

        private void CloseOutput(string key)
        {
            if (_outputs.TryGetValue(key, out var o)) { _outputs.Remove(key); CloseOutput(o); }
        }

        private static void CloseOutput(Output o)
        {
            if (o == null) return;
            try { if (o.Started) o.Client?.Stop(); } catch { }
            ComUtil.Release(o.Render);
            ComUtil.Release(o.Client);
            ComUtil.Release(o.Device);
            try { o.Event?.Dispose(); } catch { }
        }

        private bool RenderCycle(Output o, long now)
        {
            if (o.Client.GetCurrentPadding(out uint padding) < 0) return false;
            if (padding == 0 && o.Started && o.Target < 3840) o.Target += 240; // Aussetzer -> mehr Puffer
            if (padding == 0 && o.Started && CleanStart.On) o.FadeIn = 96;      // 3.3.1: nach dem Aussetzer 2 ms einblenden statt hart einzusetzen
            int n = o.Target - (int)padding;
            if (n <= 0) return true;
            n = Math.Min(n, (int)o.BufferFrames - (int)padding);
            if (n <= 0) return true;

            if (o.IsStream) MixStream(n, now);
            else if (o.IsVoice) MixVoice(n);
            else if (o.IsCue) MixCue(n);
            else if (o.IsMirror) MixMirror(n);
            else { MixBlock(o.Key, n, now); if (o.Key.Length == 0) NoteLatency(o, now); }
            if (o.FadeIn > 0)
            {
                int f = Math.Min(n, o.FadeIn);
                for (int i = 0, k = 0; i < f; i++, k += 2) { float g = (i + 1) / (float)(f + 1); _mix[k] *= g; _mix[k + 1] *= g; }
                o.FadeIn = 0;
            }

            if (o.Render.GetBuffer((uint)n, out IntPtr ptr) < 0) return false;
            Marshal.Copy(_mix, 0, ptr, n * 2);
            return o.Render.ReleaseBuffer((uint)n, 0) >= 0;
        }

        private static readonly float SmoothCoef = 1f - (float)Math.Exp(-1.0 / (Rate * 0.030));
        private static readonly float MuteCoef = 1f - (float)Math.Exp(-1.0 / (Rate * 0.002));

        private void MixBlock(string outputKey, int n, long now)
        {
            if (_mix.Length < n * 2) { _mix = new float[n * 2]; _chan = new float[n * 2]; }
            Array.Clear(_mix, 0, n * 2);
            var channels = _channels;
            string recChannel; WavWriter recWriter;
            lock (_recLock) { recChannel = _recChannel; recWriter = _recWriter; }
            var multi = _multi;
            var cueMix = _cue;
            var hallSpec = Pro400.Hall;   // 4.0 (finale)
            var hall = HallStart(outputKey, n);

            foreach (var kv in channels)
            {
                var p = kv.Value;
                if (p.OutputKey != outputKey) continue;
                if (!_states.TryGetValue(kv.Key, out var st)) { st = new ChannelState(); _states[kv.Key] = st; }
                if (!ReferenceEquals(st.Params, p))
                {
                    var old = st.Params;
                    bool soft = CleanStart.On;
                    // 3.4.1: grosse EQ-Aenderung -> 30 ms vom alten zum neuen EQ ueberblenden (der alte laeuft mit seinem Gedaechtnis weiter)
                    if (soft && old != null && BigEqChange(old, p))
                    {
                        st.OldLow = st.Low.Clone(); st.OldMid = st.Mid.Clone(); st.OldHigh = st.High.Clone();
                        st.OldHp = st.Hp.Clone(); st.OldHpOn = (old.Extra?.HpHz ?? 0) > 0;
                        st.OldLp = st.Lp.Clone(); st.OldLpOn = (old.Extra?.LpHz ?? 0) > 0;
                        if (st.OldGeq == null) { st.OldGeq = new Biquad[st.Geq.Length]; st.OldGeqOn = new bool[st.Geq.Length]; }
                        for (int b = 0; b < st.Geq.Length; b++) { st.OldGeq[b] = st.Geq[b].Clone(); st.OldGeqOn[b] = GeqOn(old, b); }
                        st.EqFade = SwitchFadeFrames;
                    }
                    st.Low.LowShelf(Rate, FreqLow, p.Low);
                    st.Mid.Peaking(Rate, FreqMid, p.Mid, 0.7);
                    st.High.HighShelf(Rate, FreqHigh, p.High);
                    int hp = p.Extra?.HpHz ?? 0, hpOld = old?.Extra?.HpHz ?? 0;   // 3.4.6: Bass-Filter
                    if (hp > 0) { if (hpOld == 0) st.Hp.Reset(); st.Hp.HighPass(Rate, hp); }
                    int lp = p.Extra?.LpHz ?? 0, lpOld = old?.Extra?.LpHz ?? 0;   // 3.5: Hoehen-Filter
                    if (lp > 0) { if (lpOld == 0) st.Lp.Reset(); st.Lp.LowPass(Rate, lp); }
                    if (p.Graphic != null)
                    {
                        // ein Band, das gerade erst dazukommt, nicht mit altem Gedaechtnis starten
                        for (int b = 0; b < st.Geq.Length; b++) if (GeqOn(p, b) && !GeqOn(old, b)) st.Geq[b].Reset();
                        GraphicEq.Setup(st.Geq, p.Graphic, Rate);
                    }
                    if (!soft)
                    {
                        if (p.Comp && (old == null || !old.Comp)) st.Comp.Reset();
                        if (p.Leveler && (old == null || !old.Leveler)) st.Lev.Reset();
                    }
                    st.Params = p;
                }
                if (_dryBuf.Length < n * 2) { _dryBuf = new float[n * 2]; _oldBuf = new float[n * 2]; }

                Array.Clear(_chan, 0, n * 2);
                bool any = false;
                foreach (var c in _captures.Values)
                    if (c.ChannelId == kv.Key) any |= c.ReadAdd(_chan, n, now);
                if (_tails.Count > 0) any |= ReadTails(kv.Key, _chan, n);   // 3.4.1

                bool recording = recWriter != null && kv.Key == recChannel;
                float g0 = st.Gain, g1 = p.Gain;
                if (!any && g0 == 0 && g1 == 0) { st.Delay?.Clear(); continue; }

                // 3.2: mono bzw. links/rechts getauscht, Verzoegerung
                if (p.Stereo != st.StereoCur)
                {
                    st.StereoPrev = st.StereoCur; st.StereoCur = p.Stereo;
                    st.StereoMix = CleanStart.On ? 0f : 1f;   // 3.4.1: 20 ms ueberblenden statt hart umschalten
                }
                if (st.StereoMix < 1f)
                {
                    float m = st.StereoMix, step = 1f / StereoFadeFrames;
                    for (int k = 0; k < n * 2; k += 2)
                    {
                        StereoTx(st.StereoPrev, _chan[k], _chan[k + 1], out float al, out float ar);
                        StereoTx(st.StereoCur, _chan[k], _chan[k + 1], out float bl, out float br);
                        m += step; if (m > 1f) m = 1f;
                        _chan[k] = al + (bl - al) * m; _chan[k + 1] = ar + (br - ar) * m;
                    }
                    st.StereoMix = m;
                }
                else if (p.Stereo == 1)
                    for (int k = 0; k < n * 2; k += 2) { float m = (_chan[k] + _chan[k + 1]) * 0.5f; _chan[k] = m; _chan[k + 1] = m; }
                else if (p.Stereo == 2)
                    for (int k = 0; k < n * 2; k += 2) { float t = _chan[k]; _chan[k] = _chan[k + 1]; _chan[k + 1] = t; }
                if (p.DelayMs > 0 || st.Delay != null)
                {
                    if (st.Delay == null) st.Delay = new DelayLine(Rate);
                    st.Delay.Process(_chan, n, p.DelayMs * Rate / 1000);
                    if (p.DelayMs == 0 && st.Delay.Idle) st.Delay = null;
                }

                var pro = p.Extra?.Pro;   // 4.0 (finale): Profi-Ton
                if (pro != null && st.Pro == null) st.Pro = new ProState(Rate);
                st.Pro?.Pre(_chan, n, pro);   // Polaritaet, Rauschsperre
                string order = pro?.Order;
                if (order == null)
                {
                    StageEq(st, p, n);
                    StageDyn(st, p, n);
                    st.Pro?.Warm(_chan, n, pro);
                    StageAfter(st, p, n);
                    st.Fx.Process(_chan, n, p.Fx);   // 2.19: Hall/Echo
                }
                else
                {
                    // 4.0 (finale): eigene Effekt-Reihenfolge
                    foreach (char sc in order)
                    {
                        if (sc == 'E') StageEq(st, p, n);
                        else if (sc == 'K') StageDyn(st, p, n);
                        else if (sc == 'W') st.Pro?.Warm(_chan, n, pro);
                        else st.Fx.Process(_chan, n, p.Fx);
                    }
                    StageAfter(st, p, n);
                }
                st.Pro?.Post(_chan, n, pro);   // Frequenz-Ducking
                // 2.19: vorhoeren – vor dem Fader in einen eigenen Puffer (CUE-Geraet hat einen anderen Takt)
                if (p.Cue && cueMix != null && cueMix.DeviceId != null)
                {
                    if (st.CueRing == null) st.CueRing = new AudioRing(Rate, 2400, 7200);
                    st.CueRing.Write(_chan, 0, n);
                }
                else st.CueRing = null;
                var an = _analysis;
                if (_spectrumChannel == kv.Key || an.SpecCh.Contains(kv.Key))
                {
                    if (st.Spectrum == null) st.Spectrum = new SpectrumAnalyzer(Rate);
                    st.Spectrum.Add(_chan, n);
                }
                // 2.17: Lautheit des Kanals (nach EQ, vor dem Fader)
                if (an.LoudCh.Contains(kv.Key)) { if (st.Loud == null) st.Loud = new LoudnessSink(); st.Loud.Write(_chan, n); }
                else if (st.Loud != null) st.Loud = null;

                // Pegel vor dem Fader: so laut waere der Kanal ohne Fader/Stumm
                {
                    float ql = st.PreL, qr = st.PreR;
                    for (int k = 0; k < n * 2; k += 2)
                    {
                        float l = _chan[k], r = _chan[k + 1];
                        if (l < 0) l = -l; if (r < 0) r = -r;
                        if (l > ql) ql = l; if (r > qr) qr = r;
                    }
                    st.PreL = ql; st.PreR = qr;
                }

                float pl = st.PeakL, pr = st.PeakR;
                if (_softGain)
                {
                    // weiche Lautstaerke-Aenderung (Zeitkonstante 30 ms)
                    float g = g0;
                    for (int i = 0, k = 0; i < n; i++, k += 2)
                    {
                        g += (g1 - g) * SmoothCoef;
                        _chan[k] *= g; _chan[k + 1] *= g;
                    }
                    if (Math.Abs(g - g1) < 0.00002f) g = g1;
                    st.Gain = g;
                }
                else
                {
                    float step = (g1 - g0) / n;
                    for (int i = 0, k = 0; i < n; i++, k += 2)
                    {
                        float g = g0 + step * (i + 1);
                        _chan[k] *= g; _chan[k + 1] *= g;
                    }
                    st.Gain = g1;
                }
                for (int i = 0, k = 0; i < n; i++, k += 2)
                {
                    float l = _chan[k], r = _chan[k + 1];
                    _mix[k] += l; _mix[k + 1] += r;
                    if (l < 0) l = -l; if (r < 0) r = -r;
                    if (l > pl) pl = l; if (r > pr) pr = r;
                }
                st.PeakL = pl; st.PeakR = pr;
                if (recording) RecordTap(_chan, n, now);
                if (multi != null && multi.TryGetValue(kv.Key, out var tap)) tap.Write(_chan, n, now);   // 2.19: Mehrspur
                if (st.Pro != null)
                {
                    // 4.0 (finale): Hall-Bus (SEND nach dem Fader)
                    float snd = hallSpec != null ? (pro?.Send ?? 0f) : 0f;
                    if (snd > 0f || st.Pro.Sending)
                    {
                        if (hall == null) hall = HallFor(outputKey, n);
                        st.Pro.SendInto(hall.Buf, _chan, n, snd);
                        hall.Fed = true;
                    }
                    if (pro == null && st.Pro.Idle) st.Pro = null;
                }

                // Stream-Mix: Kanal nach dem Fader in einen eigenen Puffer (anderer Geraetetakt)
                var sm = _stream;
                if (sm != null && sm.DeviceId != null && sm.SendOf(kv.Key) > 0f)
                {
                    if (st.StreamRing == null) st.StreamRing = new AudioRing(Rate, 2400, 7200);
                    st.StreamRing.Write(_chan, 0, n);
                }
                else st.StreamRing = null;
            }

            // Soundboard fuer mich (Standardgeraet; 3.2: Testton auch auf einem bestimmten Geraet)
            foreach (var v in _sounds) if (v.Me && (v.MeKey ?? "") == outputKey && v.PosMe < v.Clip.Frames) v.MixInto(_mix, n, ref v.PosMe);
            if (outputKey.Length == 0) MixNoise(n);   // 3.5: Einschlaf-Geraeusche
            if (hall != null) MixHall(outputKey, hall, hallSpec, n);   // 4.0 (finale): Hall-Bus
            MixGen(outputKey, n);   // 4.0 (finale): Signalgenerator

            // 2.19: EQ und Raumklang des Ausgangs
            var ofx = _outFx;
            if (ofx != null && ofx.TryGetValue(outputKey, out var of) && of != null && _outputs.TryGetValue(outputKey, out var fo))
            {
                if (fo.Fx == null) fo.Fx = new OutputFxState(Rate);
                fo.Fx.Process(_mix, n, of);
                // 3.2: Ausgang mono bzw. links/rechts getauscht
                if (of.Mono)
                    for (int k = 0; k < n * 2; k += 2) { float m = (_mix[k] + _mix[k + 1]) * 0.5f; _mix[k] = m; _mix[k + 1] = m; }
                else if (of.Swap)
                    for (int k = 0; k < n * 2; k += 2) { float t = _mix[k]; _mix[k] = _mix[k + 1]; _mix[k + 1] = t; }
            }

            // Gehoerschutz: harte Obergrenze fuer alles, was ueber den EQ-Weg laeuft
            if (_outputs.TryGetValue(outputKey, out var outp))
            {
                float ceiling = _ceiling;
                if (ceiling > 0f && ceiling < 1f) outp.Limiter.Process(_mix, n, ceiling);
            }

            // Schutzbegrenzer: weich ab 0,9 (-0,9 dBFS), nie ueber 1,0
            for (int i = 0; i < n * 2; i++)
            {
                float x = _mix[i];
                float a = x < 0 ? -x : x;
                if (a > 0.9f)
                {
                    float y = 0.9f + 0.1f * (float)Math.Tanh((a - 0.9f) / 0.1f);
                    _mix[i] = x < 0 ? -y : y;
                }
            }

            // Zustaende entfernter Kanaele aufraeumen
            if (_states.Count > channels.Count)
                foreach (var key in new List<string>(_states.Keys))
                    if (!channels.ContainsKey(key)) _states.Remove(key);
        }

        // ------------------------------------------------------------------ Kanal-Aufnahme

        /// <summary>EQ-Kanal: Ton nach EQ/Kompressor/Fader direkt in die Aufnahme (Takt = Ausgabegeraet).</summary>
        private void RecordTap(float[] buf, int n, long now)
        {
            WavWriter w;
            long gapFrames = 0;
            lock (_recLock)
            {
                w = _recWriter;
                if (w == null) return;
                // Pause seit dem letzten Block (Kanal war still/ohne Ausgabe)? Mit Stille fuellen, damit die Zeit stimmt.
                long gapMs = now - _recLastMs - n * 1000L / Rate;
                if (gapMs > 30) gapFrames = Math.Min(gapMs, MaxGapMs) * Rate / 1000;
                _recTapped = true;
                _recLastMs = now;
                _recFrames += n + gapFrames;
            }
            if (gapFrames > 0) w.WriteSilence((int)gapFrames);
            w.Write(buf, n);
        }

        /// <summary>
        /// Kanal ohne EQ: Ton der Apps aus den Mess-Aufnahmen mischen (Takt = Uhr).
        /// EQ-Kanal: Luecken (Kanal gerade still/ohne Ausgabe) mit Stille fuellen.
        /// </summary>
        private const long MaxGapMs = 5000; // laengere Luecken (z. B. Energiesparmodus) nicht mit Stille fuellen

        private void PumpRecording(long now)
        {
            WavWriter w; string channel;
            lock (_recLock) { w = _recWriter; channel = _recChannel; }
            if (w == null) return;

            var channels = _channels;
            bool dspChannel = channel != null && (channels.ContainsKey(channel) || channel == StreamRecordKey);
            if (dspChannel)
            {
                long gap;
                lock (_recLock)
                {
                    gap = now - _recLastMs;
                    if (gap < 120) return;
                    _recLastMs = now;
                    gap = Math.Min(gap, MaxGapMs);
                    _recFrames += gap * Rate / 1000;
                }
                w.WriteSilence((int)(gap * Rate / 1000));
                return;
            }

            long due;
            lock (_recLock)
            {
                if (_recTapped) { _recTapped = false; _recStartMs = now; _recFrames = 0; } // Wechsel EQ -> direkt
                due = (now - _recStartMs) * Rate / 1000 - _recFrames;
                if (due < 240) return;
                if (due > MaxGapMs * Rate / 1000)
                {
                    // sehr lange Luecke: Zeitbasis neu setzen statt Minuten an Stille zu schreiben
                    _recStartMs = now - 20;
                    _recFrames = 0;
                    due = 20 * Rate / 1000;
                }
                else if (due > Rate) { _recFrames += due - Rate; w.WriteSilence((int)(due - Rate)); due = Rate; }
                _recFrames += due;
                _recLastMs = now;
            }
            int m = (int)due;
            if (_rec.Length < m * 2) _rec = new float[m * 2];
            Array.Clear(_rec, 0, m * 2);
            var pids = _recPids;
            foreach (var c in _captures.Values)
                if (c.Record && c.MeterOnly && pids.Contains(c.Pid)) c.ReadAdd(_rec, m, now);
            w.Write(_rec, m);
        }

        // ------------------------------------------------------------------ Stream-Mix

        private void MixStream(int n, long now)
        {
            if (_mix.Length < n * 2) { _mix = new float[n * 2]; _chan = new float[n * 2]; }
            if (_aux.Length < n * 2) _aux = new float[n * 2];
            Array.Clear(_mix, 0, n * 2);
            var sm = _stream;
            if (sm != null)
            {
                // Apps ohne EQ: Mess-Aufnahmen (nach der App-Lautstaerke, also nach dem Fader)
                foreach (var c in _captures.Values)
                {
                    if (c.StreamRing == null || !c.Ready) continue;
                    c.StreamRing.ReadAdd(_mix, n, sm.SendOf(c.StreamChannel));
                }
                // Kanaele mit EQ
                foreach (var kv in _states)
                    if (kv.Value.StreamRing != null) kv.Value.StreamRing.ReadAdd(_mix, n, sm.SendOf(kv.Key));
                // Soundboard
                foreach (var v in _sounds) if (v.Stream && v.PosStream < v.Clip.Frames) v.MixInto(_mix, n, ref v.PosStream);
            }
            float g0 = _streamGain, g1 = sm == null || sm.Mute ? 0f : sm.Gain;
            float g = g0, pl = _streamPkL, pr = _streamPkR;
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                g += (g1 - g) * SmoothCoef;
                float l = _mix[k] * g, r = _mix[k + 1] * g;
                _mix[k] = l; _mix[k + 1] = r;
                if (l < 0) l = -l; if (r < 0) r = -r;
                if (l > pl) pl = l; if (r > pr) pr = r;
            }
            _streamGain = Math.Abs(g - g1) < 0.00002f ? g1 : g;
            _streamPkL = pl; _streamPkR = pr;
            if (Pro400.StreamLimitOn) { if (_tpLimiter == null) _tpLimiter = new TruePeakLimiter(Rate); _tpLimiter.Process(_mix, n); }   // 4.0 (finale)
            else _tpLimiter = null;
            _streamLimiter.Process(_mix, n, 0.95f);
            WavWriter w; string rc;
            lock (_recLock) { w = _recWriter; rc = _recChannel; }
            if (w != null && rc == StreamRecordKey) RecordTap(_mix, n, now);
        }

        // ------------------------------------------------------------------ 2.19: Vorhoeren

        private void MixCue(int n)
        {
            if (_mix.Length < n * 2) { _mix = new float[n * 2]; _chan = new float[n * 2]; }
            Array.Clear(_mix, 0, n * 2);
            var cue = _cue;
            if (cue != null)
                foreach (var kv in _states)
                    if (kv.Value.CueRing != null) kv.Value.CueRing.ReadAdd(_mix, n, 1f);
            float g0 = _cueGain, g1 = cue == null ? 0f : cue.Gain;
            float g = g0, pl = _cuePkL, pr = _cuePkR;
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                g += (g1 - g) * SmoothCoef;
                float l = _mix[k] * g, r = _mix[k + 1] * g;
                _mix[k] = l; _mix[k + 1] = r;
                if (l < 0) l = -l; if (r < 0) r = -r;
                if (l > pl) pl = l; if (r > pr) pr = r;
            }
            _cueGain = Math.Abs(g - g1) < 0.00002f ? g1 : g;
            _cuePkL = pl * 0.9f; _cuePkR = pr * 0.9f;
            _cueLimiter.Process(_mix, n, 0.95f);
        }

        // ------------------------------------------------------------------ Sprach-Ausgang (Mikro + Soundboard)

        private void UpdateMic(IMMDeviceEnumerator en, VoiceOut voice, long now)
        {
            if (voice == null || voice.DeviceId == null)
            {
                if (_micClient != null) CloseMic();
                _voiceStatus = null;
                return;
            }
            if (_micClient != null && now >= _micCheckAt)
            {
                _micCheckAt = now + 1000;
                string def = DefaultCaptureId(en);
                if (def != null && !string.Equals(def, _micDeviceId, StringComparison.OrdinalIgnoreCase)) CloseMic(); // Mikro gewechselt
            }
            if (_micClient == null && now >= _micRetryAt)
            {
                if (!OpenMic(en)) _micRetryAt = now + 3000;
            }
        }

        private static string DefaultCaptureId(IMMDeviceEnumerator en)
        {
            try
            {
                if (en.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out IMMDevice d) != 0 || d == null) return null;
                d.GetId(out string id);
                ComUtil.Release(d);
                return id;
            }
            catch { return null; }
        }

        private bool OpenMic(IMMDeviceEnumerator en)
        {
            IMMDevice dev = null;
            IAudioClient client = null;
            IntPtr fmt = IntPtr.Zero;
            try
            {
                if (en.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out dev) != 0 || dev == null) throw new Exception("Kein Mikrofon");
                dev.GetId(out string id);
                Guid iid = Wasapi.IID_IAudioClient;
                if (dev.Activate(ref iid, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out object o) < 0) throw new Exception("Activate");
                client = (IAudioClient)o;
                fmt = Wasapi.AllocFloatStereoFormat(Rate);
                int hr = client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED,
                    Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, 2000000, 0, fmt, IntPtr.Zero);
                Marshal.FreeHGlobal(fmt); fmt = IntPtr.Zero;
                if (hr >= 0) { _micChannels = 2; _micRate = Rate; _micFloat = true; }
                else
                {
                    // Geraeteformat nehmen und selbst umrechnen
                    ComUtil.Release(client); client = null;
                    if (dev.Activate(ref iid, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out o) < 0) throw new Exception("Activate");
                    client = (IAudioClient)o;
                    if (client.GetMixFormat(out IntPtr mix) < 0 || mix == IntPtr.Zero) throw new Exception("Format");
                    try
                    {
                        Wasapi.ReadFormat(mix, out _micChannels, out _micRate, out int bits, out bool isFloat);
                        if (isFloat && bits == 32) _micFloat = true;
                        else if (!isFloat && bits == 16) _micFloat = false;
                        else throw new Exception("Mikro-Format wird nicht unterstützt");
                        hr = client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED, 0, 2000000, 0, mix, IntPtr.Zero);
                        if (hr < 0) throw new COMException("Initialize", hr);
                    }
                    finally { Marshal.FreeCoTaskMem(mix); }
                }
                Guid cid = Wasapi.IID_IAudioCaptureClient;
                if (client.GetService(ref cid, out object svc) < 0) throw new Exception("GetService");
                if (client.Start() < 0) { ComUtil.Release(svc); throw new Exception("Start"); }
                _micClient = client;
                _micCapture = (IAudioCaptureClient)svc;
                _micDeviceId = id;
                _micResampler = _micRate != Rate ? new StreamResampler(_micRate, Rate) : null;
                _micRing.Clear();
                _micCheckAt = _clock.ElapsedMilliseconds + 1000;
                _voiceStatus = null;
                client = null;
                return true;
            }
            catch (Exception ex)
            {
                _voiceStatus = "Mikro konnte nicht geöffnet werden: " + ex.Message;
                return false;
            }
            finally
            {
                if (fmt != IntPtr.Zero) Marshal.FreeHGlobal(fmt);
                ComUtil.Release(client);
                ComUtil.Release(dev);
            }
        }

        private void CloseMic()
        {
            try { _micClient?.Stop(); } catch { }
            ComUtil.Release(_micCapture);
            ComUtil.Release(_micClient);
            _micCapture = null; _micClient = null; _micDeviceId = null;
            _micRing.Clear();
        }

        private void DrainMic()
        {
            var cap = _micCapture;
            if (cap == null) return;
            try
            {
                for (int guard = 0; guard < 64; guard++)
                {
                    int hr = cap.GetNextPacketSize(out uint packet);
                    if (hr < 0) throw new COMException("GetNextPacketSize", hr);
                    if (packet == 0) break;
                    hr = cap.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    if (hr < 0) throw new COMException("GetBuffer", hr);
                    int n = (int)frames;
                    if (n > 0)
                    {
                        if ((flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == IntPtr.Zero)
                        {
                            int outN = _micResampler != null ? (int)((long)n * Rate / _micRate) : n;
                            _micRing.WriteSilence(outN);
                        }
                        else
                        {
                            int samples = n * _micChannels;
                            if (_micRaw.Length < samples) _micRaw = new float[samples];
                            if (_micFloat) Marshal.Copy(data, _micRaw, 0, samples);
                            else
                            {
                                if (_micRaw16.Length < samples) _micRaw16 = new short[samples];
                                Marshal.Copy(data, _micRaw16, 0, samples);
                                for (int i = 0; i < samples; i++) _micRaw[i] = _micRaw16[i] / 32768f;
                            }
                            if (_micBuf.Length < n * 2) _micBuf = new float[n * 2];
                            if (_micChannels == 2) Array.Copy(_micRaw, _micBuf, n * 2);
                            else LoopbackRecorder.Downmix(_micRaw, _micChannels, _micBuf, n);
                            float[] src = _micBuf; int m = n;
                            if (_micResampler != null) { m = _micResampler.Process(_micBuf, n, ref _micConv); src = _micConv; }
                            float pk = _voicePk;
                            for (int i = 0; i < m * 2; i++) { float a = Math.Abs(src[i]); if (a > pk) pk = a; }
                            _voicePk = pk;
                            _micRing.Write(src, 0, m);
                        }
                    }
                    cap.ReleaseBuffer(frames);
                }
            }
            catch (Exception ex)
            {
                _voiceStatus = "Mikro-Aufnahme abgebrochen: " + ex.Message;
                CloseMic();
                _micRetryAt = _clock.ElapsedMilliseconds + 2000;
            }
        }

        private void MixVoice(int n)
        {
            if (_mix.Length < n * 2) { _mix = new float[n * 2]; _chan = new float[n * 2]; }
            Array.Clear(_mix, 0, n * 2);
            var vo = _voice;
            // Mikro (Rauschsperre, Stummschaltung im Sprach-Ausgang)
            Array.Clear(_chan, 0, n * 2);
            _micRing.ReadAdd(_chan, n);
            var fx = vo?.Fx ?? VoiceFx.Off;
            _voiceFx.Configure(fx);           // auch "aus" merken, damit Filter beim Einschalten frisch starten
            bool useFx = !fx.IsOff;
            if (useFx) _voiceFx.ProcessPre(_chan, n);
            if (vo != null && vo.GateOn) _gate.Process(_chan, n, (float)Math.Pow(10, vo.GateDb / 20.0));
            if (useFx) _voiceFx.ProcessPost(_chan, n);
            float target = vo == null || vo.MicMuted ? 0f : 1f, g = _voiceMicGain;
            float coef = target < g ? MuteCoef : SmoothCoef; // stumm: sehr schnell, an: weich
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                g += (target - g) * coef;
                _mix[k] = _chan[k] * g; _mix[k + 1] = _chan[k + 1] * g;
            }
            _voiceMicGain = Math.Abs(g - target) < 0.00002f ? target : g;
            // Soundboard
            foreach (var v in _sounds) if (v.Voice && v.PosVoice < v.Clip.Frames) v.MixInto(_mix, n, ref v.PosVoice);
            float pk = _voiceOutPk;
            for (int i = 0; i < n * 2; i++)
            {
                float x = _mix[i], a = x < 0 ? -x : x;
                if (a > pk) pk = a;
                if (a > 0.9f) { float y = 0.9f + 0.1f * (float)Math.Tanh((a - 0.9f) / 0.1f); _mix[i] = x < 0 ? -y : y; }
            }
            _voiceOutPk = pk;
        }

        /// <summary>3.1.2: Mess-Aufnahmen der Apps in die Warteliste (hoechstens 64 Eintraege insgesamt).</summary>
        private void AddMeterEvents(List<WaitHandle> list)
        {
            _meterWaitPartial = false;
            foreach (var c in _captures.Values)
            {
                if (!c.Ready || !c.MeterOnly || c.Act?.Event == null) continue;
                if (list.Count >= 64) { _meterWaitPartial = true; break; }
                list.Add(c.Act.Event);
            }
        }

        /// <summary>
        /// 3.1.2: nur die gemessenen App-Pegel weitergeben, sobald ein Paket da ist. Noch nicht abgeholte Spitzen bleiben
        /// erhalten (hoechster Wert zaehlt), damit zwischen zwei Abholungen nichts verloren geht.
        /// </summary>
        private void PublishPidPeaks()
        {
            Dictionary<uint, float[]> pp = null;
            foreach (var c in _captures.Values)
            {
                if (!c.Ready || !c.MeterOnly) continue;
                (pp ?? (pp = new Dictionary<uint, float[]>()))[c.Pid] = new[] { c.PeakL, c.PeakR };
                c.PeakL = 0; c.PeakR = 0;
            }
            if (pp == null) return;
            lock (_takeLock)
            {
                if (!_pidTaken) MaxInto(pp, _pidPeaks);
                _pidPeaks = pp; _pidTaken = false;
            }
        }

        private void PublishPeaks()
        {
            _streamPeak = new[] { _streamPkL, _streamPkR };
            _streamPkL = 0; _streamPkR = 0;
            _voicePeak = _voicePk; _voicePk = 0;
            _voiceOutPeak = _voiceOutPk; _voiceOutPk = 0;

            var lev = new Dictionary<string, float>();
            foreach (var kv in _states)
                if (kv.Value.Params != null && kv.Value.Params.Leveler) lev[kv.Key] = kv.Value.Lev.GainDb;
            _levGain = lev;

            // Spektrum (ca. 15x pro Sekunde, unabhaengig vom Pegel-Takt)
            string sc = _spectrumChannel;
            long nowMs = _clock.ElapsedMilliseconds;
            if (sc != null && nowMs >= _nextSpectrumMs && (_nextSpectrumMs = nowMs + 60) > 0)
                _spectrum = _states.TryGetValue(sc, out var sst) && sst.Spectrum != null ? sst.Spectrum.Compute() : null;
            else if (sc == null && _spectrum != null) _spectrum = null;

            var pp = new Dictionary<uint, float[]>();
            var ep = new Dictionary<uint, float[]>();
            foreach (var c in _captures.Values)
            {
                if (!c.Ready) continue;
                (c.MeterOnly ? pp : ep)[c.Pid] = new[] { c.PeakL, c.PeakR };
                c.PeakL = 0; c.PeakR = 0;
            }
            bool acc = LevelFlow.Seamless;
            lock (_takeLock)
            {
                if (acc && !_pidTaken) MaxInto(pp, _pidPeaks);
                if (acc && !_eqTaken) MaxInto(ep, _eqPidPeaks);
                _pidPeaks = pp; _pidTaken = false;
                _eqPidPeaks = ep; _eqTaken = false;
            }

            var d = new Dictionary<string, float[]>(_states.Count);
            var comp = new Dictionary<string, float>();
            foreach (var kv in _states)
            {
                d[kv.Key] = new[] { kv.Value.PeakL, kv.Value.PeakR, kv.Value.PreL, kv.Value.PreR };
                kv.Value.PeakL = 0; kv.Value.PeakR = 0; kv.Value.PreL = 0; kv.Value.PreR = 0;
                if (kv.Value.Params != null && kv.Value.Params.Comp) comp[kv.Key] = kv.Value.Comp.Reduction;
                kv.Value.Comp.Reduction = 0;
            }
            lock (_takeLock)
            {
                if (acc && !_peaksTaken) MaxInto(d, _peaks);
                _peaks = d; _peaksTaken = false;
            }
            _comp = comp;
            PublishAnalysis(nowMs);
        }
    }
}
