using System;

namespace Mischpult.Audio
{
    /// <summary>
    /// Stereo-Ringpuffer zwischen zwei Takten (z. B. Aufnahme -> anderes Ausgabegeraet).
    /// Fuellt sich vor dem ersten Lesen etwas vor und kuerzt bei Uhrendrift auf den Zielwert.
    /// </summary>
    internal sealed class AudioRing
    {
        private readonly float[] _buf;
        private readonly int _cap;
        private int _read, _count;
        private bool _primed;
        public int PrimeFrames;
        private readonly int _maxBacklog;

        public AudioRing(int capacityFrames, int primeFrames, int maxBacklogFrames)
        {
            _cap = capacityFrames;
            _buf = new float[capacityFrames * 2];
            PrimeFrames = primeFrames;
            _maxBacklog = maxBacklogFrames;
        }

        public int Count => _count;

        public void Clear() { _read = 0; _count = 0; _primed = false; }

        public void Write(float[] src, int offsetFrames, int frames, float gain = 1f)
        {
            if (frames <= 0) return;
            if (frames > _cap) { offsetFrames += frames - _cap; frames = _cap; }
            if (_count + frames > _cap)
            {
                int drop = _count + frames - _cap;
                _read = (_read + drop) % _cap;
                _count -= drop;
            }
            int wp = (_read + _count) % _cap;
            for (int i = 0; i < frames; i++)
            {
                int k = ((wp + i) % _cap) * 2, s = (offsetFrames + i) * 2;
                if (src == null) { _buf[k] = 0; _buf[k + 1] = 0; }
                else { _buf[k] = src[s] * gain; _buf[k + 1] = src[s + 1] * gain; }
            }
            _count += frames;
        }

        public void WriteSilence(int frames) => Write(null, 0, frames);

        /// <summary>Addiert bis zu n Frames (mal gain) in dst. Liefert die Zahl gelesener Frames.</summary>
        public int ReadAdd(float[] dst, int n, float gain = 1f)
        {
            if (!_primed)
            {
                if (_count < PrimeFrames) return 0;
                _primed = true;
            }
            if (_count > _maxBacklog)
            {
                int drop = _count - PrimeFrames;
                _read = (_read + drop) % _cap;
                _count -= drop;
            }
            int m = Math.Min(n, _count);
            if (gain != 0f)
                for (int i = 0; i < m; i++)
                {
                    int k = ((_read + i) % _cap) * 2;
                    dst[i * 2] += _buf[k] * gain;
                    dst[i * 2 + 1] += _buf[k + 1] * gain;
                }
            _read = (_read + m) % _cap;
            _count -= m;
            if (m < n) _primed = false;
            return m;
        }
    }

    /// <summary>Fortlaufende Abtastraten-Umwandlung (linear, Stereo) fuer Mikro-Formate ungleich 48 kHz.</summary>
    internal sealed class StreamResampler
    {
        private readonly double _step;     // Eingangs-Frames pro Ausgangs-Frame
        private double _pos;               // Position relativ zum gemerkten letzten Frame
        private float _lastL, _lastR;
        private bool _has;

        public StreamResampler(int inRate, int outRate) { _step = inRate / (double)outRate; }

        /// <summary>Wandelt frames Stereo-Frames aus src; schreibt nach dst und liefert die Zahl der Ausgangs-Frames.</summary>
        public int Process(float[] src, int frames, ref float[] dst)
        {
            int maxOut = (int)(frames / _step) + 4;
            if (dst.Length < maxOut * 2) dst = new float[maxOut * 2];
            int o = 0;
            if (frames <= 0) return 0;
            if (!_has) { _lastL = src[0]; _lastR = src[1]; _has = true; _pos = 1; }
            // Index -1 = letzter Frame des vorigen Blocks
            while (true)
            {
                int i = (int)Math.Floor(_pos);
                if (i >= frames) break;
                double f = _pos - i;
                float l0 = i == 0 ? _lastL : src[(i - 1) * 2], r0 = i == 0 ? _lastR : src[(i - 1) * 2 + 1];
                float l1 = src[i * 2], r1 = src[i * 2 + 1];
                dst[o * 2] = (float)(l0 + (l1 - l0) * f);
                dst[o * 2 + 1] = (float)(r0 + (r1 - r0) * f);
                o++;
                _pos += _step;
            }
            _lastL = src[(frames - 1) * 2]; _lastR = src[(frames - 1) * 2 + 1];
            _pos -= frames;
            return o;
        }
    }

    /// <summary>Rauschsperre fuers Mikro: unter der Schwelle wird stummgeschaltet (mit Haltezeit und weichem Ausblenden).</summary>
    internal sealed class NoiseGate
    {
        private float _env, _gain;
        private int _hold;
        private readonly float _envRel, _att, _rel;
        private readonly int _holdFrames;

        public NoiseGate(int rate)
        {
            _envRel = (float)Math.Exp(-1.0 / (rate * 0.050));
            _att = 1f - (float)Math.Exp(-1.0 / (rate * 0.002));
            _rel = 1f - (float)Math.Exp(-1.0 / (rate * 0.080));
            _holdFrames = rate / 5;
        }

        public bool Open => _gain > 0.5f;

        public void Process(float[] buf, int frames, float threshold)
        {
            float env = _env, g = _gain;
            int hold = _hold;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float a = Math.Max(Math.Abs(buf[k]), Math.Abs(buf[k + 1]));
                env = a > env ? a : env * _envRel;
                if (env > threshold) hold = _holdFrames;
                else if (hold > 0) hold--;
                float target = hold > 0 ? 1f : 0f;
                g += (target - g) * (target > g ? _att : _rel);
                buf[k] *= g; buf[k + 1] *= g;
            }
            if (g < 1e-5f) g = 0;
            _env = env; _gain = g; _hold = hold;
        }
    }

    /// <summary>
    /// Lautstaerke-Ausgleich: haelt den mittleren Pegel eines Kanals langsam bei etwa -20 dBFS (±12 dB).
    /// In Pausen (sehr leise) bleibt die Verstaerkung stehen, damit nach der Pause nichts hochschiesst.
    /// </summary>
    /// <summary>
    /// 3.4.6 Knall-Bremse: Wird ein Kanal ploetzlich viel lauter als eben noch (Werbung, Jumpscare, Notification),
    /// wird er sofort abgefangen und gleitet dann in gut einer Sekunde wieder hoch. Normale Spitzen bleiben unberuehrt:
    /// die Huelle haelt die Spitzen der letzten Sekunden, erst mehr als 12 dB darueber (und lauter als -20 dBFS) greift sie.
    /// </summary>
    internal sealed class PopGuard
    {
        public const float JumpFactor = 4f;      // +12 dB
        public const float Floor = 0.1f;         // -20 dBFS: leiser greift sie nie
        private readonly float _envAtt, _envRel, _gAtt, _gRel;
        private float _env = -1f, _g = 1f;
        public float Gain => _g;

        public PopGuard(int rate)
        {
            _envAtt = 1f - (float)Math.Exp(-1.0 / (rate * 0.5));      // Huelle steigt nur gemaechlich – ein Sprung bleibt ein Sprung
            _envRel = (float)Math.Exp(-1.0 / (rate * 3.0));            // Spitzen-Huelle faellt langsam (3 s)
            _gAtt = 1f - (float)Math.Exp(-1.0 / (rate * 0.002));      // in 2 ms herunter
            _gRel = 1f - (float)Math.Exp(-1.0 / (rate * 1.2));        // in gut 1 s wieder hoch
        }

        public void Reset() { _env = -1f; _g = 1f; }

        public void Process(float[] buf, int frames)
        {
            float env = _env, g = _g, ea = _envAtt, er = _envRel, ga = _gAtt, gr = _gRel;
            if (env < 0f)
            {
                // eben eingeschaltet: was gerade laeuft, gilt als normal (kein Abfangen mitten im Lied)
                env = 0f;
                for (int k = 0; k < frames * 2; k++) { float v = buf[k] < 0 ? -buf[k] : buf[k]; if (v > env) env = v; }
            }
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float l = buf[k], r = buf[k + 1];
                float a = Math.Max(l < 0 ? -l : l, r < 0 ? -r : r);
                float limit = Math.Max(Floor, env * JumpFactor);
                float want = a > limit ? limit / a : 1f;
                g += (want < g ? ga : gr) * (want - g);
                buf[k] = l * g; buf[k + 1] = r * g;
                float o = a * g;                       // die Huelle folgt dem, was herauskommt
                env = o > env ? env + (o - env) * ea : env * er;
                if (env < 1e-9f) env = 0f;
            }
            _env = env; _g = g;
        }
    }

    /// <summary>
    /// 3.5 Stimmen nach vorne: Sprache liegt fast immer in der Mitte und zwischen etwa 200 Hz und 4 kHz. Der Ton wird in
    /// diesen Bereich und den Rest (Bass, Hoehen, Seite) geteilt. Ist der Rest deutlich lauter als der Sprachbereich
    /// (Musik, Explosionen), wird nur der Rest leiser, hoechstens 8 dB – die Stimme selbst wird nie lauter gemacht.
    /// Ist alles ruhig, ist es genau der Originalton (Sprache + Rest = vorher).
    /// </summary>
    internal sealed class DialogLift
    {
        public const float MaxCut = 0.4f;        // -8 dB
        private readonly double _hb0, _hb1, _hb2, _ha1, _ha2, _lb0, _lb1, _lb2, _la1, _la2;
        private double _h1, _h2, _l1, _l2;
        private readonly float _envA, _envR, _gA, _gR;
        private float _ev, _er, _g = 1f;
        public float Gain => _g;

        public DialogLift(int rate)
        {
            // Hochpass 200 Hz und Tiefpass 4 kHz (Butterworth) auf der Mitte
            double w = 2 * Math.PI * 200.0 / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071), a0 = 1 + al;
            _hb0 = (1 + cs) / 2 / a0; _hb1 = -(1 + cs) / a0; _hb2 = _hb0; _ha1 = -2 * cs / a0; _ha2 = (1 - al) / a0;
            w = 2 * Math.PI * 4000.0 / rate; cs = Math.Cos(w); al = Math.Sin(w) / (2 * 0.7071); a0 = 1 + al;
            _lb0 = (1 - cs) / 2 / a0; _lb1 = (1 - cs) / a0; _lb2 = _lb0; _la1 = -2 * cs / a0; _la2 = (1 - al) / a0;
            _envA = 1f - (float)Math.Exp(-1.0 / (rate * 0.03));     // Huellen: 30 ms hoch
            _envR = 1f - (float)Math.Exp(-1.0 / (rate * 0.3));      // 300 ms runter
            _gA = 1f - (float)Math.Exp(-1.0 / (rate * 0.05));       // Absenkung in 50 ms
            _gR = 1f - (float)Math.Exp(-1.0 / (rate * 0.5));        // in 0,5 s zurueck
        }

        public void Reset() { _h1 = _h2 = _l1 = _l2 = 0; _ev = _er = 0; _g = 1f; }

        public void Process(float[] buf, int frames)
        {
            double h1 = _h1, h2 = _h2, l1 = _l1, l2 = _l2;
            float ev = _ev, er = _er, g = _g;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                double m = (buf[k] + buf[k + 1]) * 0.5, s = (buf[k] - buf[k + 1]) * 0.5;
                double y = _hb0 * m + h1; h1 = _hb1 * m - _ha1 * y + h2; h2 = _hb2 * m - _ha2 * y;
                double v = _lb0 * y + l1; l1 = _lb1 * y - _la1 * v + l2; l2 = _lb2 * y - _la2 * v;
                double rest = m - v;                                  // Bass + Hoehen der Mitte
                float av = (float)Math.Abs(v), ar = (float)(Math.Abs(rest) + Math.Abs(s));
                ev += (av > ev ? _envA : _envR) * (av - ev);
                er += (ar > er ? _envA : _envR) * (ar - er);
                float want = 1f;
                if (ev > 0.003f && er > ev * 1.2f) want = Math.Max(MaxCut, (float)Math.Sqrt(ev * 1.2f / er));
                g += (want < g ? _gA : _gR) * (want - g);
                double om = v + rest * g, os = s * g;
                buf[k] = (float)(om + os); buf[k + 1] = (float)(om - os);
            }
            if (Math.Abs(h1) < 1e-25) h1 = 0; if (Math.Abs(h2) < 1e-25) h2 = 0; if (Math.Abs(l1) < 1e-25) l1 = 0; if (Math.Abs(l2) < 1e-25) l2 = 0;
            _h1 = h1; _h2 = h2; _l1 = l1; _l2 = l2; _ev = ev; _er = er; _g = g;
        }
    }

    internal sealed class Leveler
    {
        public const float TargetDb = -20f, MaxDb = 12f, GateDb = -55f, UpDbPerSec = 3f, DownDbPerSec = 8f;
        private double _ms;                 // geglaettetes Quadratmittel
        private float _gainDb;
        private readonly int _rate;

        public Leveler(int rate) { _rate = rate; }

        public float GainDb => _gainDb;

        public void Reset() { _ms = 0; _gainDb = 0; }

        public void Process(float[] buf, int frames)
        {
            if (frames <= 0) return;
            double sum = 0;
            for (int i = 0; i < frames * 2; i++) sum += buf[i] * buf[i];
            double blockMs = sum / (frames * 2);
            double dt = frames / (double)_rate;
            double a = 1 - Math.Exp(-dt / 0.4);
            _ms += (blockMs - _ms) * a;
            float levelDb = _ms > 1e-12 ? (float)(10 * Math.Log10(_ms)) : -120f;

            float g0 = _gainDb, g1 = g0;
            if (levelDb > GateDb)
            {
                float want = Math.Max(-MaxDb, Math.Min(MaxDb, TargetDb - levelDb));
                float maxStep = (float)((want > g0 ? UpDbPerSec : DownDbPerSec) * dt);
                g1 = want > g0 ? Math.Min(want, g0 + maxStep) : Math.Max(want, g0 - maxStep);
            }
            _gainDb = g1;
            float l0 = (float)Math.Pow(10, g0 / 20.0), l1 = (float)Math.Pow(10, g1 / 20.0);
            float step = (l1 - l0) / frames;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float g = l0 + step * (i + 1);
                buf[k] *= g; buf[k + 1] *= g;
            }
        }
    }

    /// <summary>
    /// Spektrum des Ausgangs: bekommt den Loopback-Ton (eigener Thread) und rechnet auf Abruf (Engine-Thread) das Spektrum.
    /// </summary>
    internal sealed class SpectrumSink : ILoopbackSink
    {
        private readonly object _lock = new object();
        private SpectrumAnalyzer _an = new SpectrumAnalyzer(DspMixer.Rate);
        private int _rate = DspMixer.Rate;
        private float[] _zero = new float[0];
        private long _lastData;   // Zeitpunkt (ms) der letzten Daten
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        public void SetRate(int rate)
        {
            if (rate <= 0) return;
            lock (_lock) { if (rate != _rate) { _rate = rate; _an = new SpectrumAnalyzer(rate); } }
        }

        public void Write(float[] stereo, int frames)
        {
            if (frames <= 0) return;
            lock (_lock)
            {
                _an.Add(stereo, frames);
                _lastData = Clock.ElapsedMilliseconds;
                // fuers Goniometer: die letzten Stereo-Werte aufheben
                for (int i = 0; i < frames; i++)
                {
                    _scope[_scopePos * 2] = stereo[i * 2];
                    _scope[_scopePos * 2 + 1] = stereo[i * 2 + 1];
                    _scopePos = (_scopePos + 1) % ScopeFrames;
                }
            }
        }

        public const int ScopeFrames = 2048;
        private readonly float[] _scope = new float[ScopeFrames * 2];
        private int _scopePos;

        /// <summary>Die letzten 'frames' Stereo-Werte (L, R abwechselnd) und die Phasen-Korrelation (−1 … +1); null, wenn nichts ankam.</summary>
        public float[] Scope(int frames, out float correlation)
        {
            correlation = 0f;
            lock (_lock)
            {
                if (Clock.ElapsedMilliseconds - _lastData > 1500) return null;
                frames = Math.Max(1, Math.Min(ScopeFrames, frames));
                var r = new float[frames * 2];
                double lr = 0, ll = 0, rr = 0;
                for (int i = 0; i < frames; i++)
                {
                    int k = (_scopePos - frames + i + ScopeFrames) % ScopeFrames;
                    float l = _scope[k * 2], rv = _scope[k * 2 + 1];
                    r[i * 2] = l; r[i * 2 + 1] = rv;
                    lr += l * rv; ll += l * l; rr += rv * rv;
                }
                correlation = ll > 1e-9 && rr > 1e-9 ? (float)(lr / Math.Sqrt(ll * rr)) : 0f;
                return r;
            }
        }

        public void WriteSilence(int frames)
        {
            if (frames <= 0) return;
            frames = Math.Min(frames, SpectrumAnalyzer.Size);
            if (_zero.Length < frames * 2) _zero = new float[frames * 2];
            lock (_lock) { _an.Add(_zero, frames); _lastData = Clock.ElapsedMilliseconds; }
        }

        /// <summary>dB pro Band – oder null, wenn seit einer Weile nichts mehr ankam.</summary>
        public float[] Compute()
        {
            lock (_lock)
            {
                if (Clock.ElapsedMilliseconds - _lastData > 1500) return null;
                return _an.Compute();
            }
        }
    }

    /// <summary>Spektrum-Anzeige: 4096-Punkte-FFT, verdichtet auf logarithmisch verteilte Baender von 20 Hz bis 20 kHz.</summary>
    internal sealed class SpectrumAnalyzer
    {
        public const int Size = 4096;
        public const int Bands = 96;
        private readonly float[] _ring = new float[Size];
        private int _pos;
        private readonly double[] _re = new double[Size], _im = new double[Size], _win = new double[Size];
        private readonly int[] _bandLo = new int[Bands], _bandHi = new int[Bands];
        private readonly int _rate;

        public SpectrumAnalyzer(int rate)
        {
            _rate = rate;
            for (int i = 0; i < Size; i++) _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));
            for (int b = 0; b < Bands; b++)
            {
                double f0 = BandFreq(b), f1 = BandFreq(b + 1);
                _bandLo[b] = Math.Max(1, (int)Math.Floor(f0 * Size / rate));
                _bandHi[b] = Math.Max(_bandLo[b], Math.Min(Size / 2 - 1, (int)Math.Ceiling(f1 * Size / rate)));
            }
        }

        /// <summary>Untere Grenzfrequenz von Band b (b = Bands ergibt 20 kHz).</summary>
        public static double BandFreq(double b) => 20.0 * Math.Pow(1000.0, b / Bands);

        public void Add(float[] stereo, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _ring[_pos] = 0.5f * (stereo[i * 2] + stereo[i * 2 + 1]);
                _pos = (_pos + 1) % Size;
            }
        }

        /// <summary>2.17: Stille (die App spielt gerade nichts).</summary>
        public void AddSilence(int frames)
        {
            frames = Math.Min(frames, Size);
            for (int i = 0; i < frames; i++) { _ring[_pos] = 0f; _pos = (_pos + 1) % Size; }
        }

        /// <summary>Pegel pro Band in dBFS (Sinus mit Vollaussteuerung ~ 0 dB).</summary>
        public float[] Compute()
        {
            for (int i = 0; i < Size; i++) { _re[i] = _ring[(_pos + i) % Size] * _win[i]; _im[i] = 0; }
            Fft(_re, _im);
            var outDb = new float[Bands];
            double norm = 2.0 / (Size * 0.5); // Hann-Fenster: Mittelwert 0,5
            for (int b = 0; b < Bands; b++)
            {
                double mx = 0;
                for (int k = _bandLo[b]; k <= _bandHi[b]; k++)
                {
                    double m = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * norm;
                    if (m > mx) mx = m;
                }
                outDb[b] = mx > 1e-7 ? (float)(20 * Math.Log10(mx)) : -140f;
            }
            return outDb;
        }

        /// <summary>Radix-2-FFT (in place).</summary>
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                    }
                }
            }
        }
    }

    /// <summary>Ein laufender Soundboard-Sound mit eigener Leseposition pro Ziel (eigene Uhr je Ausgabegeraet).</summary>
    internal sealed class SoundVoice
    {
        public SoundClip Clip;
        public float Gain;
        public bool Me, Stream, Voice;
        /// <summary>3.2: Ausgang fuer "Me" ("" = Standardgeraet, sonst Geraete-ID).</summary>
        public string MeKey = "";
        public int PosMe, PosStream, PosVoice;
        public long Started;

        public bool Done =>
            (!Me || PosMe >= Clip.Frames) && (!Stream || PosStream >= Clip.Frames) && (!Voice || PosVoice >= Clip.Frames);

        /// <summary>Addiert die naechsten n Frames ab pos in dst.</summary>
        public void MixInto(float[] dst, int n, ref int pos)
        {
            int m = Math.Min(n, Clip.Frames - pos);
            var d = Clip.Data;
            float g = Gain;
            for (int i = 0, k = pos * 2; i < m; i++, k += 2)
            {
                dst[i * 2] += d[k] * g;
                dst[i * 2 + 1] += d[k + 1] * g;
            }
            pos += Math.Max(0, m);
        }
    }

    /// <summary>10-Band-Grafik-EQ (Oktavbaender).</summary>
    internal static class GraphicEq
    {
        public static readonly float[] Freqs = { 31f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        public static readonly string[] Labels = { "31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
        public const double Q = 1.41;

        public static bool IsFlat(float[] g)
        {
            if (g == null) return true;
            foreach (var v in g) if (Math.Abs(v) > 0.01f) return false;
            return true;
        }

        public static void Setup(Biquad[] bands, float[] gains, int rate)
        {
            for (int i = 0; i < bands.Length; i++)
            {
                float db = gains != null && i < gains.Length ? gains[i] : 0f;
                double f = Math.Min(Freqs[i], rate * 0.45);
                bands[i].Peaking(rate, f, db, Q);
            }
        }
    }

    /// <summary>
    /// Kompressor fuer die Stimme: gleicht leise und laute Stellen an (Stereo verkoppelt, weiches Knie).
    /// Stufe 1 leicht, 2 stark – jeweils mit Aufholverstaerkung.
    /// </summary>
    internal sealed class VoiceComp
    {
        private readonly float _att, _rel;
        private float _env, _thr = -26f, _ratio = 2.5f, _makeup = 1f;
        public const float KneeDb = 6f;
        /// <summary>Staerkste Absenkung seit dem letzten Abholen (dB, positiv).</summary>
        public float Reduction;

        public VoiceComp(int rate)
        {
            _att = (float)Math.Exp(-1.0 / (rate * 0.003));
            _rel = (float)Math.Exp(-1.0 / (rate * 0.120));
        }

        public static void Params(int level, out float thr, out float ratio, out float makeupDb)
        {
            if (level >= 2) { thr = -32f; ratio = 4f; makeupDb = 9f; }
            else { thr = -26f; ratio = 2.5f; makeupDb = 5f; }
        }

        public void Reset() { _env = 0; }

        public void Set(int level)
        {
            Params(level, out _thr, out _ratio, out float mk);
            _makeup = (float)Math.Pow(10, mk / 20.0);
        }

        /// <summary>Pegelaenderung in dB (ohne Aufholverstaerkung).</summary>
        public static float CurveDb(float x, float thr, float ratio)
        {
            float d = x - thr;
            if (2 * d < -KneeDb) return 0f;
            if (2 * Math.Abs(d) <= KneeDb) { float t = d + KneeDb / 2; return (1f / ratio - 1f) * t * t / (2 * KneeDb); }
            return d / ratio - d;
        }

        public void Process(float[] buf, int frames)
        {
            float env = _env, red = Reduction;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float l = buf[k], r = buf[k + 1];
                float a = Math.Max(l < 0 ? -l : l, r < 0 ? -r : r);
                env = a > env ? _att * env + (1 - _att) * a : _rel * env + (1 - _rel) * a;
                float c = CurveDb(env > 1e-6f ? 20f * (float)Math.Log10(env) : -120f, _thr, _ratio);
                if (-c > red) red = -c;
                float g = (float)Math.Pow(10, c / 20.0) * _makeup;
                buf[k] = l * g; buf[k + 1] = r * g;
            }
            _env = env < 1e-9f ? 0f : env;
            Reduction = red;
        }
    }

    /// <summary>Mikro-Effekte fuer den Sprach-Ausgang: Rumpelfilter (vor der Rauschsperre), danach EQ, Klarheit, Kompressor, Pegel.</summary>
    internal sealed class VoiceFxChain
    {
        public const double HpHz = 90, LowHz = 150, MidHz = 1200, HighHz = 5000;
        private readonly int _rate;
        private readonly Biquad _hp = new Biquad(), _low = new Biquad(), _mid = new Biquad(), _high = new Biquad();
        private readonly Biquad _mud = new Biquad(), _pres = new Biquad(), _air = new Biquad();
        private readonly VoiceComp _comp;
        private VoiceFx _p;
        private float _gain = 1f;

        public VoiceFxChain(int rate) { _rate = rate; _comp = new VoiceComp(rate); }

        public float CompReduction { get { float r = _comp.Reduction; _comp.Reduction = 0; return r; } }

        public void Configure(VoiceFx fx)
        {
            if (fx == null || fx.SameAs(_p)) return;
            // Was gerade eingeschaltet wird, beginnt ohne altes Gedaechtnis (sonst knackt es)
            var o = _p ?? VoiceFx.Off;
            if (fx.HighPass && !o.HighPass) _hp.Reset();
            if (fx.Low != 0 && o.Low == 0) _low.Reset();
            if (fx.Mid != 0 && o.Mid == 0) _mid.Reset();
            if (fx.High != 0 && o.High == 0) _high.Reset();
            if (fx.Clarity && !o.Clarity) { _mud.Reset(); _pres.Reset(); _air.Reset(); }
            if (fx.Comp > 0 && o.Comp == 0) _comp.Reset();
            _hp.HighPass(_rate, HpHz);
            _low.LowShelf(_rate, LowHz, fx.Low);
            _mid.Peaking(_rate, MidHz, fx.Mid, 0.8);
            _high.HighShelf(_rate, HighHz, fx.High);
            _mud.Peaking(_rate, 300, -2.5, 1.0);
            _pres.Peaking(_rate, 3200, 4.0, 0.9);
            _air.HighShelf(_rate, 10000, 2.0);
            if (fx.Comp > 0 && (_p == null || _p.Comp != fx.Comp)) _comp.Set(fx.Comp);
            _gain = (float)Math.Pow(10, fx.GainDb / 20.0);
            _p = fx;
        }

        /// <summary>Vor der Rauschsperre: tiefes Brummen raus, damit es die Sperre nicht offen haelt.</summary>
        public void ProcessPre(float[] buf, int n)
        {
            var p = _p;
            if (p != null && p.HighPass) _hp.Process(buf, n);
        }

        /// <summary>Nach der Rauschsperre.</summary>
        public void ProcessPost(float[] buf, int n)
        {
            var p = _p;
            if (p == null) return;
            if (p.Low != 0) _low.Process(buf, n);
            if (p.Mid != 0) _mid.Process(buf, n);
            if (p.High != 0) _high.Process(buf, n);
            if (p.Clarity) { _mud.Process(buf, n); _pres.Process(buf, n); _air.Process(buf, n); }
            if (p.Comp > 0) _comp.Process(buf, n);
            if (_gain != 1f) for (int i = 0; i < n * 2; i++) buf[i] *= _gain;
        }
    }
}
