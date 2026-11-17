using System;

namespace Mischpult.Audio
{
    /// <summary>
    /// 3.5 Zwei Geraete gleichzeitig: nimmt per Loopback auf, was am Standardgeraet laeuft, und gibt es auf einem zweiten
    /// Geraet aus (z. B. Kopfhoerer und Lautsprecher). Andere Abtastrate wird umgerechnet, der Puffer gleicht den Takt aus.
    /// </summary>
    internal sealed class MirrorFeed : ILoopbackSink
    {
        public readonly string TargetId;
        public volatile int DelayMs;
        private readonly object _lock = new object();
        private readonly AudioRing _ring = new AudioRing(DspMixer.Rate, 2400, 9600);   // 50 ms vorlegen, hoechstens 200 ms Rueckstand
        private int _rate = DspMixer.Rate;
        private StreamResampler _rs;
        private float[] _tmp = new float[0];

        public MirrorFeed(string targetId, int delayMs) { TargetId = targetId; DelayMs = Math.Max(0, Math.Min(500, delayMs)); }

        public void SetRate(int rate)
        {
            lock (_lock)
            {
                _rate = rate > 0 ? rate : DspMixer.Rate;
                _rs = _rate == DspMixer.Rate ? null : new StreamResampler(_rate, DspMixer.Rate);
            }
        }

        public void Write(float[] stereo, int frames)
        {
            if (frames <= 0) return;
            lock (_lock)
            {
                if (_rs == null) _ring.Write(stereo, 0, frames);
                else { int m = _rs.Process(stereo, frames, ref _tmp); _ring.Write(_tmp, 0, m); }
            }
        }

        public void WriteSilence(int frames)
        {
            if (frames <= 0) return;
            frames = Math.Min(frames, _rate);
            lock (_lock)
            {
                int m = _rate == DspMixer.Rate ? frames : (int)((long)frames * DspMixer.Rate / _rate);
                _ring.WriteSilence(m);
            }
        }

        /// <summary>Aus dem Mischer-Faden: n Frames dazumischen (fehlt etwas, bleibt es still).</summary>
        public int ReadAdd(float[] dst, int n)
        {
            lock (_lock) return _ring.ReadAdd(dst, n, 1f);
        }
    }

    /// <summary>
    /// 3.5 Einschlaf-Geraeusche, vom Pult selbst erzeugt (keine Dateien): 0 Rauschen, 1 Regen, 2 Meer, 3 Kaminfeuer.
    /// Alles mit festem Zufall, damit es sich nicht wiederholt, und leise genug (um −20 dBFS).
    /// </summary>
    internal sealed class NoiseGen
    {
        public static readonly string[] KindNames = { "Rauschen", "Regen", "Meer", "Kaminfeuer" };
        private readonly int _rate;
        private uint _seed = 0x9E3779B9u;
        // Rosa Rauschen (Paul Kellet) je Seite
        private float _b0L, _b1L, _b2L, _b3L, _b4L, _b5L, _b6L, _b0R, _b1R, _b2R, _b3R, _b4R, _b5R, _b6R;
        private float _brL, _brR;                 // braunes Rauschen
        private float _lpL, _lpR, _hpL, _hpR, _hpxL, _hpxR;
        // Ereignisse (Tropfen, Knistern)
        private float _evAmp, _evDecay, _evPan, _evLp, _evHp, _evX;
        private int _evLeft;
        // Meer: Wellen
        private double _wavePhase, _waveLen = 8.0, _waveAmp = 0.8;
        private float _waveEnv;

        public NoiseGen(int rate) { _rate = rate; }

        private float Rnd()   // −1..1
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            return (_seed & 0xFFFFFF) / 8388608f - 1f;
        }

        private float Rnd01() => (Rnd() + 1f) * 0.5f;

        private static float Pink(float w, ref float b0, ref float b1, ref float b2, ref float b3, ref float b4, ref float b5, ref float b6)
        {
            b0 = 0.99886f * b0 + w * 0.0555179f; b1 = 0.99332f * b1 + w * 0.0750759f; b2 = 0.96900f * b2 + w * 0.1538520f;
            b3 = 0.86650f * b3 + w * 0.3104856f; b4 = 0.55000f * b4 + w * 0.5329522f; b5 = -0.7616f * b5 - w * 0.0168980f;
            float p = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
            b6 = w * 0.115926f;
            return p * 0.11f;
        }

        /// <summary>n Frames dazumischen, Lautstaerke gleitet von g0 nach g1.</summary>
        public void Add(float[] dst, int n, int kind, float g0, float g1)
        {
            float step = n > 0 ? (g1 - g0) / n : 0f, g = g0;
            float aLp = kind == 2 ? 0.12f : kind == 3 ? 0.03f : 0.5f;      // Tiefpass je nach Art
            float aHp = kind == 1 ? 0.06f : 0.002f;                         // Regen: ohne Brummen
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                g += step;
                float wl = Rnd(), wr = Rnd();
                float l, r;
                switch (kind)
                {
                    case 1:   // Regen: helles Rauschen, leicht flatternd, dazu Tropfen
                    {
                        l = Pink(wl, ref _b0L, ref _b1L, ref _b2L, ref _b3L, ref _b4L, ref _b5L, ref _b6L);
                        r = Pink(wr, ref _b0R, ref _b1R, ref _b2R, ref _b3R, ref _b4R, ref _b5R, ref _b6R);
                        _hpL += aHp * (l - _hpL); _hpR += aHp * (r - _hpR);
                        l = (l - _hpL) * 0.9f; r = (r - _hpR) * 0.9f;
                        if (_evLeft <= 0 && Rnd01() < 40f / _rate) StartEvent(0.05f + 0.25f * Rnd01() * Rnd01(), 0.003f, 2);
                        float d = Event(); l += d * (1f - _evPan); r += d * _evPan;
                        break;
                    }
                    case 2:   // Meer: braunes Rauschen, das in langsamen Wellen an- und abschwillt
                    {
                        _brL = (_brL + wl * 0.02f) * 0.997f; _brR = (_brR + wr * 0.02f) * 0.997f;
                        _lpL += aLp * (_brL - _lpL); _lpR += aLp * (_brR - _lpR);
                        if (i % 64 == 0) WaveStep(64);
                        float pinkL = Pink(wl, ref _b0L, ref _b1L, ref _b2L, ref _b3L, ref _b4L, ref _b5L, ref _b6L);
                        float pinkR = Pink(wr, ref _b0R, ref _b1R, ref _b2R, ref _b3R, ref _b4R, ref _b5R, ref _b6R);
                        float e = _waveEnv;
                        l = (_lpL * 2.2f + pinkL * 0.35f * e) * (0.25f + 0.75f * e);
                        r = (_lpR * 2.2f + pinkR * 0.35f * e) * (0.25f + 0.75f * e);
                        break;
                    }
                    case 3:   // Kaminfeuer: tiefes Rauschen, dazu Knistern und ab und zu ein Knacken
                    {
                        _brL = (_brL + wl * 0.02f) * 0.995f; _brR = (_brR + wr * 0.02f) * 0.995f;
                        _lpL += aLp * (_brL - _lpL); _lpR += aLp * (_brR - _lpR);
                        l = _lpL * 1.2f; r = _lpR * 1.2f;
                        if (_evLeft <= 0)
                        {
                            float p = Rnd01();
                            if (p < 9f / _rate) StartEvent(0.05f + 0.25f * Rnd01() * Rnd01(), 0.0015f + 0.004f * Rnd01(), 3);
                            else if (p < 9.6f / _rate) StartEvent(0.3f + 0.2f * Rnd01(), 0.008f, 3);   // lautes Knacken
                        }
                        float d = Event(); l += d * (1f - _evPan); r += d * _evPan;
                        break;
                    }
                    default:  // Rauschen: weich (rosa, oben etwas abgerundet)
                    {
                        l = Pink(wl, ref _b0L, ref _b1L, ref _b2L, ref _b3L, ref _b4L, ref _b5L, ref _b6L);
                        r = Pink(wr, ref _b0R, ref _b1R, ref _b2R, ref _b3R, ref _b4R, ref _b5R, ref _b6R);
                        _lpL += aLp * (l - _lpL); _lpR += aLp * (r - _lpR);
                        l = _lpL; r = _lpR;
                        break;
                    }
                }
                dst[k] += l * g; dst[k + 1] += r * g;
            }
        }

        private void StartEvent(float amp, float decaySec, int kind)
        {
            _evAmp = amp; _evDecay = (float)Math.Exp(-1.0 / (_rate * decaySec));
            _evLeft = (int)(_rate * decaySec * 6);
            _evPan = 0.15f + 0.7f * Rnd01();
            _evLp = 0; _evHp = 0; _evX = 0;
        }

        private float Event()
        {
            if (_evLeft <= 0) return 0f;
            _evLeft--;
            float w = Rnd() * _evAmp;
            _evAmp *= _evDecay;
            _evLp += 0.45f * (w - _evLp);            // Band: oben etwas weicher, unten ohne Rumpeln
            float y = _evLp - _evX; _evX += 0.08f * (_evLp - _evX);
            return y;
        }

        private void WaveStep(int frames)
        {
            double dt = frames / (double)_rate;
            _wavePhase += dt / _waveLen;
            if (_wavePhase >= 1.0)
            {
                _wavePhase -= 1.0;
                _waveLen = 6.0 + 5.0 * Rnd01();
                _waveAmp = 0.55 + 0.45 * Rnd01();
            }
            // Anlaufen (35 %), Brechen und langsames Auslaufen
            double ph = _wavePhase, e = ph < 0.35 ? Math.Sin(ph / 0.35 * Math.PI / 2) : Math.Exp(-(ph - 0.35) * 3.2);
            _waveEnv = (float)(e * _waveAmp);
        }
    }

    internal sealed partial class DspMixer
    {
        public const string MirrorPrefix = "#mirror:";
        private volatile MirrorFeed _mirrorFeed;
        private DelayLine _mirrorDelay;
        private readonly Limiter _mirrorLimiter = new Limiter(Rate);
        private float _mirrorPkL, _mirrorPkR;
        private long _mirrorDefCheck;
        private string _mirrorDefId;

        /// <summary>3.5: zweites Geraet (null = aus).</summary>
        public void SetMirror(MirrorFeed f) { _mirrorFeed = f; _wake.Set(); }

        private void MixMirror(int n)
        {
            if (_mix.Length < n * 2) { _mix = new float[n * 2]; _chan = new float[n * 2]; }
            Array.Clear(_mix, 0, n * 2);
            var f = _mirrorFeed;
            if (f == null) return;
            f.ReadAdd(_mix, n);
            int d = f.DelayMs;
            if (d > 0 || _mirrorDelay != null)
            {
                if (_mirrorDelay == null) _mirrorDelay = new DelayLine(Rate);
                _mirrorDelay.Process(_mix, n, d * Rate / 1000);
                if (d == 0 && _mirrorDelay.Idle) _mirrorDelay = null;
            }
            float pl = _mirrorPkL * 0.9f, pr = _mirrorPkR * 0.9f;
            for (int k = 0; k < n * 2; k += 2) { float a = Math.Abs(_mix[k]), b = Math.Abs(_mix[k + 1]); if (a > pl) pl = a; if (b > pr) pr = b; }
            _mirrorPkL = pl; _mirrorPkR = pr;
            _mirrorLimiter.Process(_mix, n, 0.98f);
        }

        // ---------------------------------------------------------------- Verzoegerung des EQ-Wegs (Schaetzung)

        private volatile int _latencyMs;
        private long _nextLatencyMs;

        /// <summary>3.5: so viel spaeter kommt der Ton eines Kanals auf dem EQ-Weg (Aufnahme-Puffer + Ausgabe-Puffer + Windows), in ms.</summary>
        public int LatencyMs => _latencyMs;

        private void NoteLatency(Output o, long now)
        {
            if (now < _nextLatencyMs) return;
            _nextLatencyMs = now + 500;
            int prime = 0;
            foreach (var c in _captures.Values) if (c.ChannelId != null && c.Primed && c.PrimeFrames > prime) prime = c.PrimeFrames;
            if (prime == 0) prime = 480;
            // Aufnahme-Paket (10 ms) + Vorlauf + Ausgabe-Puffer + Takt von Windows (etwa 10 ms)
            _latencyMs = (int)Math.Round((prime + o.Target) * 1000.0 / Rate) + 20;
        }

        // ---------------------------------------------------------------- Einschlaf-Geraeusche

        private volatile int _noiseKind;
        private volatile float _noiseGain;          // Ziel (linear), 0 = aus
        private float _noiseCur;
        private NoiseGen _noiseGen;
        private float _noisePkL, _noisePkR;

        /// <summary>3.5: Geraeusch und Lautstaerke (linear, 0 = aus – blendet in 1 s aus).</summary>
        public void SetNoise(int kind, float gain) { _noiseKind = Math.Max(0, Math.Min(NoiseGen.KindNames.Length - 1, kind)); _noiseGain = Math.Max(0f, Math.Min(1f, gain)); _wake.Set(); }

        public float[] NoisePeak => new[] { _noisePkL, _noisePkR };
        private bool NoiseWanted => _noiseGain > 0f || _noiseCur > 0f;

        /// <summary>Aus MixBlock fuer das Standardgeraet.</summary>
        private void MixNoise(int n)
        {
            float target = _noiseGain;
            if (target <= 0f && _noiseCur <= 0f) { _noiseGen = null; _noisePkL = _noisePkR = 0f; return; }
            if (_noiseGen == null) _noiseGen = new NoiseGen(Rate);
            float maxStep = n / (float)Rate;          // ganz an/aus in etwa 1 s
            float g0 = _noiseCur, g1 = target > g0 ? Math.Min(target, g0 + maxStep) : Math.Max(target, g0 - maxStep);
            if (_chan.Length < n * 2) _chan = new float[n * 2];
            Array.Clear(_chan, 0, n * 2);
            _noiseGen.Add(_chan, n, _noiseKind, g0, g1);
            float pl = _noisePkL * 0.9f, pr = _noisePkR * 0.9f;
            for (int k = 0; k < n * 2; k += 2)
            {
                _mix[k] += _chan[k]; _mix[k + 1] += _chan[k + 1];
                float a = Math.Abs(_chan[k]), b = Math.Abs(_chan[k + 1]); if (a > pl) pl = a; if (b > pr) pr = b;
            }
            _noisePkL = pl; _noisePkR = pr;
            _noiseCur = g1;
        }
    }
}
