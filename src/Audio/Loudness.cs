using System;

namespace Mischpult.Audio
{
    /// <summary>
    /// Lautheit nach EBU R128 / ITU-R BS.1770 (LUFS): K-Filter (Hoehenanhebung + Tiefen-Hochpass), Leistung je 100 ms,
    /// daraus Momentan (0,4 s), Kurzzeit (3 s) und Integriert (seit dem Zuruecksetzen, mit Gates bei −70 LUFS und −10 LU).
    /// Bekommt den Ton per Loopback (eigener Thread) und rechnet auf Abruf (Engine-Thread).
    /// </summary>
    internal sealed class LoudnessSink : ILoopbackSink
    {
        private readonly object _lock = new object();
        private int _rate = DspMixer.Rate;
        // K-Filter: zwei Biquads je Kanal (Koeffizienten wie libebur128, fuer jede Abtastrate)
        private double _pb0, _pb1, _pb2, _pa1, _pa2, _rb0, _rb1, _rb2, _ra1, _ra2;
        private double _s1L1, _s1L2, _s2L1, _s2L2, _s1R1, _s1R2, _s2R1, _s2R2;

        private const int Segments = 30;                 // 30 x 100 ms = 3 s
        private readonly double[] _seg = new double[Segments];
        private int _segPos, _segCount;
        private double _acc;
        private int _accN, _segLen;
        private long _lastData;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        // Integriert: Histogramm der 400-ms-Bloecke (0,1 LU je Fach von −70 bis +10 LUFS)
        private const double HistLo = -70.0, HistStep = 0.1;
        private const int HistBins = 800;
        private readonly long[] _hCount = new long[HistBins];
        private readonly double[] _hEnergy = new double[HistBins];

        public LoudnessSink() { SetupFilters(_rate); }

        public void SetRate(int rate)
        {
            if (rate <= 0) return;
            lock (_lock) { if (rate != _rate) { _rate = rate; SetupFilters(rate); _beat?.SetRate(rate); } }
        }

        private void SetupFilters(int fs)
        {
            // Stufe 1: Hoehen-Shelf (+4 dB ueber ca. 1,7 kHz)
            double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
            double K = Math.Tan(Math.PI * f0 / fs);
            double Vh = Math.Pow(10.0, G / 20.0), Vb = Math.Pow(Vh, 0.4996667741545416);
            double a0 = 1.0 + K / Q + K * K;
            _pb0 = (Vh + Vb * K / Q + K * K) / a0;
            _pb1 = 2.0 * (K * K - Vh) / a0;
            _pb2 = (Vh - Vb * K / Q + K * K) / a0;
            _pa1 = 2.0 * (K * K - 1.0) / a0;
            _pa2 = (1.0 - K / Q + K * K) / a0;
            // Stufe 2: Hochpass (RLB) bei ca. 38 Hz
            f0 = 38.13547087602444; Q = 0.5003270373238773;
            K = Math.Tan(Math.PI * f0 / fs);
            double d = 1.0 + K / Q + K * K;
            _rb0 = 1.0; _rb1 = -2.0; _rb2 = 1.0;
            _ra1 = 2.0 * (K * K - 1.0) / d;
            _ra2 = (1.0 - K / Q + K * K) / d;
            _s1L1 = _s1L2 = _s2L1 = _s2L2 = _s1R1 = _s1R2 = _s2R1 = _s2R2 = 0;
            _segLen = Math.Max(1, fs / 10);
            _acc = 0; _accN = 0;
        }

        public void Write(float[] stereo, int frames)
        {
            if (frames <= 0) return;
            lock (_lock)
            {
                bool tp = TruePeakOn;
                var bt = BeatTracker.On ? (_beat ?? (_beat = new BeatTracker(_rate))) : null;   // 3.4.6 Takt-Lampe
                bool corr = CorrOn, bass = BassOn;   // 3.5
                for (int i = 0; i < frames; i++)
                {
                    float xl = stereo[i * 2], xr = stereo[i * 2 + 1];
                    bt?.Add(xl, xr);
                    if (corr) CorrSample(xl, xr);
                    if (bass) BassSample(xl, xr);
                    double l = Filter(xl, ref _s1L1, ref _s1L2, ref _s2L1, ref _s2L2);
                    double r = Filter(xr, ref _s1R1, ref _s1R2, ref _s2R1, ref _s2R2);
                    if (tp) PeakSample(xl, xr);
                    AddSample(l * l + r * r);
                }
                _lastData = Clock.ElapsedMilliseconds;
            }
        }

        // ---------------------------------------------------------------- 3.4.6 True-Peak (4-fach ueberabgetastet, BS.1770 Anhang 2)
        /// <summary>True-Peak und Spitzenwert mitmessen (nur wenn jemand sie anzeigt – kostet etwas Rechenzeit).</summary>
        public static volatile bool TruePeakOn;
        private const int TpTaps = 12, TpPhases = 4;
        private static readonly float[][] TpCoef = MakeTpCoef();
        private readonly float[] _tpL = new float[TpTaps], _tpR = new float[TpTaps];
        private int _tpPos;
        private readonly float[] _tpSeg = new float[Segments], _spSeg = new float[Segments];
        private float _tpCur, _spCur;
        private int _tpN;

        /// <summary>Gefensterter Sinc, 48 Taps in 4 Phasen (Interpolation zwischen den Samples).</summary>
        private static float[][] MakeTpCoef()
        {
            var c = new float[TpPhases][];
            for (int ph = 0; ph < TpPhases; ph++)
            {
                c[ph] = new float[TpTaps];
                double sum = 0;
                for (int k = 0; k < TpTaps; k++)
                {
                    double x = (k - (TpTaps / 2 - 1)) - ph / (double)TpPhases;   // Abstand zum gesuchten Zwischenwert
                    double sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                    double n = (k + 1 - ph / (double)TpPhases) / (TpTaps + 1);   // Hann-Fenster ueber die Laenge
                    double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * Math.Max(0, Math.Min(1, n)));
                    c[ph][k] = (float)(sinc * w);
                    sum += c[ph][k];
                }
                for (int k = 0; k < TpTaps; k++) c[ph][k] = (float)(c[ph][k] / sum);   // Gleichanteil = 1
            }
            return c;
        }

        private void PeakSample(float l, float r)
        {
            _tpL[_tpPos] = l; _tpR[_tpPos] = r;
            _tpPos = (_tpPos + 1) % TpTaps;
            float sp = Math.Max(Math.Abs(l), Math.Abs(r));
            if (sp > _spCur) _spCur = sp;
            float tpk = sp;
            for (int ph = 1; ph < TpPhases; ph++)
            {
                var co = TpCoef[ph];
                float al = 0, ar = 0;
                for (int k = 0; k < TpTaps; k++) { int idx = (_tpPos + k) % TpTaps; al += co[k] * _tpL[idx]; ar += co[k] * _tpR[idx]; }
                float m = Math.Max(Math.Abs(al), Math.Abs(ar));
                if (m > tpk) tpk = m;
            }
            if (tpk > _tpCur) _tpCur = tpk;
            if (++_tpN >= _segLen)
            {
                _tpSeg[_segPos] = _tpCur; _spSeg[_segPos] = _spCur;   // gleiche 100-ms-Abschnitte wie die Lautheit
                _tpCur = 0; _spCur = 0; _tpN = 0;
            }
        }

        private BeatTracker _beat;

        // ---------------------------------------------------------------- 3.5 Phasen-Warnung, Bass fuer "Pult wackelt"
        public static volatile bool CorrOn, BassOn;
        private double _cLR, _cLL, _cRR;
        private int _cN;
        private readonly double[] _cSegLR = new double[Segments], _cSegLL = new double[Segments], _cSegRR = new double[Segments];
        private int _cSegPos;
        private double _bLp1, _bLp2;
        private float _bassEnv;

        private void CorrSample(float l, float r)
        {
            _cLR += l * r; _cLL += l * l; _cRR += r * r;
            if (++_cN >= _segLen)
            {
                _cSegLR[_cSegPos] = _cLR; _cSegLL[_cSegPos] = _cLL; _cSegRR[_cSegPos] = _cRR;
                _cSegPos = (_cSegPos + 1) % Segments;
                _cLR = _cLL = _cRR = 0; _cN = 0;
            }
        }

        private void BassSample(float l, float r)
        {
            double a = 2.0 * Math.PI * 120.0 / _rate;   // zweimal Tiefpass bei etwa 120 Hz
            double m = (l + r) * 0.5;
            _bLp1 += a * (m - _bLp1); _bLp2 += a * (_bLp1 - _bLp2);
            float v = (float)Math.Abs(_bLp2) * 1.6f;
            _bassEnv = v > _bassEnv ? v : _bassEnv * 0.9995f;   // schnell hoch, in gut 40 ms runter
        }

        /// <summary>3.5: Korrelation der letzten 3 s (−1 gegenphasig … +1 mono), NaN = zu leise oder nicht gemessen.</summary>
        public float Correlation()
        {
            if (!CorrOn) return float.NaN;
            lock (_lock)
            {
                if (Clock.ElapsedMilliseconds - _lastData > 1500) return float.NaN;
                double lr = 0, ll = 0, rr = 0;
                for (int i = 0; i < Segments; i++) { lr += _cSegLR[i]; ll += _cSegLL[i]; rr += _cSegRR[i]; }
                double n = Segments * (double)_segLen;
                if (ll / n < 1e-6 || rr / n < 1e-6) return float.NaN;   // leiser als etwa −60 dBFS
                return (float)Math.Max(-1.0, Math.Min(1.0, lr / Math.Sqrt(ll * rr)));
            }
        }

        /// <summary>3.5: Bass-Huelle (linear), −1 = nicht gemessen.</summary>
        public float Bass()
        {
            if (!BassOn) return -1f;
            lock (_lock) return Clock.ElapsedMilliseconds - _lastData > 500 ? 0f : _bassEnv;
        }

        /// <summary>3.4.6: [BPM, ms seit dem letzten Schlag, Sicherheit] oder null (Takt-Lampe aus).</summary>
        public float[] Beat()
        {
            if (!BeatTracker.On) return null;
            lock (_lock)
            {
                if (_beat == null) return new[] { 0f, 0f, 0f };
                if (Clock.ElapsedMilliseconds - _lastData > 1500) return new[] { 0f, 0f, 0f };
                return _beat.State();
            }
        }

        /// <summary>[True-Peak dBTP, Spitzenwert dBFS] der letzten 3 s (−∞ = nichts).</summary>
        public float[] Peaks()
        {
            lock (_lock)
            {
                float tp = _tpCur, sp = _spCur;
                for (int i = 0; i < Segments; i++) { if (_tpSeg[i] > tp) tp = _tpSeg[i]; if (_spSeg[i] > sp) sp = _spSeg[i]; }
                if (Clock.ElapsedMilliseconds - _lastData > 1500) { tp = 0; sp = 0; }
                return new[] { tp > 1e-7f ? 20f * (float)Math.Log10(tp) : float.NegativeInfinity, sp > 1e-7f ? 20f * (float)Math.Log10(sp) : float.NegativeInfinity };
            }
        }

        public void WriteSilence(int frames)
        {
            if (frames <= 0) return;
            lock (_lock)
            {
                frames = Math.Min(frames, _rate * 5);
                var bt = BeatTracker.On ? _beat : null;
                for (int i = 0; i < frames; i++)
                {
                    bt?.Add(0f, 0f);
                    Filter(0f, ref _s1L1, ref _s1L2, ref _s2L1, ref _s2L2);
                    Filter(0f, ref _s1R1, ref _s1R2, ref _s2R1, ref _s2R2);
                    AddSample(0);
                }
                _lastData = Clock.ElapsedMilliseconds;
            }
        }

        private double Filter(double x, ref double a1, ref double a2, ref double b1, ref double b2)
        {
            // transponierte Direktform II, zwei Stufen hintereinander
            double y = _pb0 * x + a1;
            a1 = _pb1 * x - _pa1 * y + a2;
            a2 = _pb2 * x - _pa2 * y;
            double z = _rb0 * y + b1;
            b1 = _rb1 * y - _ra1 * z + b2;
            b2 = _rb2 * y - _ra2 * z;
            return z;
        }

        private void AddSample(double e)
        {
            _acc += e;
            if (++_accN < _segLen) return;
            _seg[_segPos] = _acc / _accN;
            _segPos = (_segPos + 1) % Segments;
            if (_segCount < Segments) _segCount++;
            _acc = 0; _accN = 0;
            // 400-ms-Block (alle 100 ms) fuers integrierte Mittel
            if (_segCount >= 4)
            {
                double m = Mean(4);
                double lufs = ToLufs(m);
                if (lufs > HistLo)
                {
                    int bin = Math.Min(HistBins - 1, (int)((lufs - HistLo) / HistStep));
                    _hCount[bin]++;
                    _hEnergy[bin] += m;
                }
            }
        }

        private double Mean(int n)
        {
            n = Math.Min(n, _segCount);
            if (n <= 0) return 0;
            double s = 0;
            for (int i = 1; i <= n; i++) s += _seg[(_segPos - i + Segments) % Segments];
            return s / n;
        }

        private static double ToLufs(double meanSquare) => meanSquare > 1e-12 ? -0.691 + 10.0 * Math.Log10(meanSquare) : double.NegativeInfinity;

        /// <summary>[Momentan, Kurzzeit, Integriert] in LUFS (−inf = still) – oder null, wenn seit einer Weile nichts ankam.</summary>
        public float[] Compute()
        {
            lock (_lock)
            {
                if (Clock.ElapsedMilliseconds - _lastData > 2000) return null;
                return new[] { (float)ToLufs(Mean(4)), (float)ToLufs(Mean(Segments)), (float)Integrated() };
            }
        }

        private double Integrated()
        {
            long n = 0; double e = 0;
            for (int i = 0; i < HistBins; i++) { n += _hCount[i]; e += _hEnergy[i]; }
            if (n == 0) return double.NegativeInfinity;
            double gate = ToLufs(e / n) - 10.0;
            int from = Math.Max(0, (int)Math.Ceiling((gate - HistLo) / HistStep));
            n = 0; e = 0;
            for (int i = from; i < HistBins; i++) { n += _hCount[i]; e += _hEnergy[i]; }
            return n == 0 ? double.NegativeInfinity : ToLufs(e / n);
        }

        /// <summary>Integrierten Wert neu beginnen.</summary>
        public void ResetIntegrated()
        {
            lock (_lock)
            {
                Array.Clear(_hCount, 0, HistBins);
                Array.Clear(_hEnergy, 0, HistBins);
            }
        }
    }
}
