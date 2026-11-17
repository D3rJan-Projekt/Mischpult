using System;

namespace Mischpult.Audio
{
    /// <summary>
    /// 3.4.6 Takt-Lampe: schaetzt das Tempo (BPM) und wo der Schlag sitzt – aus dem, was am Ausgang laeuft.
    /// Alle 10 ms: Energie im Bass (unter 150 Hz) und in den Hoehen (ueber 3 kHz); wo sie sprunghaft steigt, ist ein
    /// Einsatz (Bassdrum, Snare, Hi-Hat). Alle 250 ms sucht eine Autokorrelation der letzten 6 s den Abstand, in dem sich
    /// die Einsaetze wiederholen (60–200 BPM, mit leichter Vorliebe fuer 120). Kostet fast nichts.
    /// Wird aus dem Loopback-Thread der Lautheits-Messung gefuettert (unter deren Sperre).
    /// </summary>
    internal sealed class BeatTracker
    {
        /// <summary>Nur rechnen, wenn die Takt-Lampe an ist.</summary>
        public static volatile bool On;

        public const int Fps = 100;                    // Einsatz-Werte pro Sekunde (10 ms)
        private const int Hist = 800;                  // 8 s
        private const int Window = 600;                // 6 s fuer die Autokorrelation
        private const int MinLag = 30, MaxLag = 100;   // 200 … 60 BPM
        private int _rate, _hop;
        private double _aB, _aH, _lp1, _lp2, _lpH;
        private double _accB, _accH;
        private int _n;
        private double _prevB = -100, _prevH = -100;
        private readonly float[] _onset = new float[Hist];
        private readonly float[] _energy = new float[Hist];
        private long _hops;                            // so viele 10-ms-Schritte bisher
        private int _sinceCalc;
        private readonly float[] _recent = new float[7];
        private int _recentN, _recentPos;
        private float _bpm, _conf;
        private double _beatHop = -1;                  // Schritt (mit Nachkomma) des letzten geschaetzten Schlags
        private double _period;                        // Schritte pro Schlag

        public BeatTracker(int rate) { SetRate(rate); }

        public void SetRate(int rate)
        {
            if (rate <= 0) rate = 48000;
            _rate = rate;
            _hop = Math.Max(1, rate / Fps);
            _aB = 1.0 - Math.Exp(-2.0 * Math.PI * 150.0 / rate);
            _aH = 1.0 - Math.Exp(-2.0 * Math.PI * 3000.0 / rate);
        }

        public float Bpm => _bpm;
        public float Confidence => _conf;
        public long Hops => _hops;

        /// <summary>Ein Stereo-Sample.</summary>
        public void Add(float l, float r)
        {
            double m = (l + r) * 0.5;
            _lp1 += _aB * (m - _lp1);
            _lp2 += _aB * (_lp1 - _lp2);
            _lpH += _aH * (m - _lpH);
            double h = m - _lpH;
            _accB += _lp2 * _lp2;
            _accH += h * h;
            if (++_n >= _hop) Step();
        }

        private void Step()
        {
            double eb = 10.0 * Math.Log10(_accB / _n + 1e-12), eh = 10.0 * Math.Log10(_accH / _n + 1e-12);
            _accB = _accH = 0; _n = 0;
            // nur Anstiege zaehlen, und erst ab hoerbarem Pegel (sonst springt Rauschen um Zehntel-dB herum)
            double ob = eb > -65 ? Math.Max(0.0, eb - Math.Max(_prevB, -65)) : 0.0;
            double oh = eh > -70 ? Math.Max(0.0, eh - Math.Max(_prevH, -70)) : 0.0;
            _prevB = eb; _prevH = eh;
            int p = (int)(_hops % Hist);
            _onset[p] = (float)Math.Min(30.0, ob + 0.5 * oh);
            _energy[p] = (float)Math.Max(eb, eh);
            _hops++;
            if (++_sinceCalc >= 25) { _sinceCalc = 0; Estimate(); }
        }

        private float At(long hop) => hop < 0 || hop <= _hops - Hist ? 0f : _onset[(int)(hop % Hist)];

        private readonly double[] _x = new double[Window];
        private readonly double[] _r = new double[2 * MaxLag + 2];

        private void Estimate()
        {
            if (_hops < 300) { _bpm = 0; _conf = 0; return; }   // erst nach 3 s
            int n = (int)Math.Min(Window, _hops);
            // still? (die letzten 2 s)
            double loud = -200;
            for (int i = 0; i < 200; i++) loud = Math.Max(loud, _energy[(int)((_hops - 1 - i) % Hist)]);
            if (loud < -55) { _bpm = 0; _conf = 0; _recentN = 0; return; }
            double mean = 0;
            for (int i = 0; i < n; i++) { _x[i] = At(_hops - n + i); mean += _x[i]; }
            mean /= n;
            for (int i = 0; i < n; i++) _x[i] -= mean;
            int maxL = Math.Min(2 * MaxLag + 1, n / 2);
            for (int lag = 0; lag <= maxL; lag++)
            {
                double s = 0;
                for (int i = lag; i < n; i++) s += _x[i] * _x[i - lag];
                _r[lag] = s;
            }
            if (_r[0] <= 1e-9) { _bpm = 0; _conf = 0; return; }
            int best = -1; double bestScore = double.NegativeInfinity;
            for (int lag = MinLag; lag <= MaxLag && lag <= maxL; lag++)
            {
                double bpm = 60.0 * Fps / lag;
                double oct = Math.Log(bpm / 120.0, 2.0);
                double w = Math.Exp(-0.5 * oct * oct / (0.9 * 0.9));
                double sc = _r[lag] + (2 * lag <= maxL ? 0.5 * _r[2 * lag] : 0.0);
                sc *= w;
                if (sc > bestScore) { bestScore = sc; best = lag; }
            }
            if (best < 0) return;
            double conf = _r[best] / _r[0];
            // feiner: Parabel durch die Nachbarn
            double lf = best;
            if (best > MinLag && best < MaxLag)
            {
                double a = _r[best - 1], b = _r[best], c = _r[best + 1], d = a - 2 * b + c;
                if (d < 0) lf = best + Math.Max(-0.5, Math.Min(0.5, 0.5 * (a - c) / d));
            }
            if (conf < 0.08) { _conf = (float)conf; if (_conf < 0.04f) _bpm = 0; return; }
            float est = (float)(60.0 * Fps / lf);
            _recent[_recentPos] = est; _recentPos = (_recentPos + 1) % _recent.Length;
            if (_recentN < _recent.Length) _recentN++;
            var sorted = new float[_recentN];
            Array.Copy(_recent, sorted, _recentN);
            Array.Sort(sorted);
            float med = sorted[_recentN / 2];
            if (_bpm <= 0 || Math.Abs(med - _bpm) / _bpm > 0.015f) _bpm = med;   // kleine Wackler nicht zeigen
            _conf = (float)conf;
            _period = 60.0 * Fps / _bpm;
            // Wo sitzt der Schlag? Kammfilter ueber die letzten 8 Schlaege
            int per = (int)Math.Ceiling(_period);
            double bestPh = 0, bestSum = double.NegativeInfinity;
            for (int ph = 0; ph < per; ph++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++)
                {
                    long hp = _hops - 1 - ph - (long)Math.Round(k * _period);
                    s += At(hp) + 0.5 * (At(hp - 1) + At(hp + 1));
                }
                if (s > bestSum) { bestSum = s; bestPh = ph; }
            }
            _beatHop = _hops - 1 - bestPh;
        }

        /// <summary>[BPM, ms seit dem letzten Schlag, Sicherheit 0..1] – BPM 0 = nichts erkannt.</summary>
        public float[] State()
        {
            if (_bpm <= 0 || _beatHop < 0 || _period <= 0) return new[] { 0f, 0f, _conf };
            double since = (_hops - _beatHop) % _period;
            if (since < 0) since += _period;
            return new[] { (float)Math.Round(_bpm, 1), (float)(since * 1000.0 / Fps), Math.Min(1f, _conf) };
        }
    }
}
