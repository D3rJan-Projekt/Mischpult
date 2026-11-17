using System;
using System.Collections.Generic;

namespace Mischpult.Audio
{
    /// <summary>2.19: Hall oder Echo eines Kanals (unveraenderlich).</summary>
    internal sealed class ChannelFx
    {
        public const int None = 0, Hall = 1, Echo = 2;
        public static readonly string[] HallNames = { "Kleiner Raum", "Halle", "Kathedrale" };
        public static readonly string[] EchoNames = { "Kurz (Slapback)", "Mittel", "Lang (Ping-Pong)" };

        public readonly int Type;     // 1 Hall, 2 Echo
        public readonly int Preset;   // 0..2
        public readonly float Mix;    // Anteil 0..1

        public ChannelFx(int type, int preset, float mix)
        {
            Type = Math.Max(0, Math.Min(2, type));
            Preset = Math.Max(0, Math.Min(2, preset));
            Mix = float.IsNaN(mix) ? 0.25f : Math.Max(0f, Math.Min(1f, mix));
        }

        public bool IsOff => Type == None || Mix <= 0f;
        public bool SameAs(ChannelFx o) => o != null && o.Type == Type && o.Preset == Preset && o.Mix == Mix;
        public static bool Same(ChannelFx a, ChannelFx b) => a == null ? b == null : a.SameAs(b);
    }

    /// <summary>2.19: Klang eines Ausgangs: 10-Band-EQ und Kopfhoerer-Raumklang (unveraenderlich).</summary>
    internal sealed class OutputFx
    {
        public static readonly string[] SpaceNames = { "Aus", "Leicht", "Mittel", "Stark", "Lautsprecher im Raum" };
        public readonly float[] Graphic;   // 10 Baender (dB) oder null
        public readonly int Space;         // 0 aus, 1 leicht, 2 mittel, 3 stark, 4 Lautsprecher im Raum
        public readonly bool Mono, Swap;   // 3.2: Ausgang mono bzw. links/rechts getauscht
        public readonly int BassMonoHz;    // 3.5: Baesse darunter auf beiden Seiten gleich, 0 = aus
        public readonly PeqSet Peq;        // 4.0: parametrischer EQ, Kopfhoerer-Profil, Hoer-EQ (null = nichts)

        public OutputFx(float[] graphic, int space) : this(graphic, space, false, false) { }
        public OutputFx(float[] graphic, int space, bool mono, bool swap) : this(graphic, space, mono, swap, 0) { }
        public OutputFx(float[] graphic, int space, bool mono, bool swap, int bassMonoHz) : this(graphic, space, mono, swap, bassMonoHz, null) { }

        public OutputFx(float[] graphic, int space, bool mono, bool swap, int bassMonoHz, PeqSet peq)
        {
            Peq = peq == null || peq.IsFlat ? null : peq;
            Graphic = graphic == null || graphic.Length != 10 || GraphicEq.IsFlat(graphic) ? null : (float[])graphic.Clone();
            Space = Math.Max(0, Math.Min(4, space));
            Mono = mono; Swap = swap && !mono;
            BassMonoHz = mono || bassMonoHz <= 0 ? 0 : Math.Max(40, Math.Min(300, bassMonoHz));
        }

        public bool IsOff => Graphic == null && Space == 0 && !Mono && !Swap && BassMonoHz == 0 && Peq == null;
        public bool SameAs(OutputFx o)
        {
            if (o == null || o.Space != Space || o.Mono != Mono || o.Swap != Swap || o.BassMonoHz != BassMonoHz) return false;
            if (!PeqSet.Same(Peq, o.Peq)) return false;
            if (Graphic == null || o.Graphic == null) return Graphic == o.Graphic;
            for (int i = 0; i < 10; i++) if (Graphic[i] != o.Graphic[i]) return false;
            return true;
        }
    }

    /// <summary>2.19: Vorhoeren (CUE): vorgehoerte Kanaele (vor dem Fader) auf einem eigenen Geraet (unveraenderlich).</summary>
    internal sealed class CueMix
    {
        public readonly string DeviceId;
        public readonly float Gain;
        public CueMix(string deviceId, float gain) { DeviceId = deviceId; Gain = Math.Max(0f, Math.Min(2f, gain)); }
        public bool SameAs(CueMix o) => o != null && string.Equals(o.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase) && o.Gain == Gain;
    }

    /// <summary>Hall nach dem bekannten "Freeverb" (8 Kammfilter + 4 Allpaesse je Seite), auf 48 kHz umgerechnet.</summary>
    internal sealed class Reverb
    {
        private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
        private const int Spread = 23;
        private const float FixedGain = 0.015f;

        private sealed class Comb
        {
            private readonly float[] _buf; private int _i; private float _store;
            public float Feedback, Damp1, Damp2;
            public Comb(int size) { _buf = new float[size]; }
            public float Process(float x)
            {
                float y = _buf[_i];
                _store = y * Damp2 + _store * Damp1;
                if (_store < 1e-20f && _store > -1e-20f) _store = 0f;   // keine Denormalen
                _buf[_i] = x + _store * Feedback;
                if (++_i >= _buf.Length) _i = 0;
                return y;
            }
            public void Clear() { Array.Clear(_buf, 0, _buf.Length); _store = 0; }
        }

        private sealed class Allpass
        {
            private readonly float[] _buf; private int _i;
            public Allpass(int size) { _buf = new float[size]; }
            public float Process(float x)
            {
                float b = _buf[_i];
                float y = -x + b;
                float w = x + b * 0.5f;
                if (w < 1e-20f && w > -1e-20f) w = 0f;
                _buf[_i] = w;
                if (++_i >= _buf.Length) _i = 0;
                return y;
            }
            public void Clear() => Array.Clear(_buf, 0, _buf.Length);
        }

        private readonly Comb[] _cl = new Comb[8], _cr = new Comb[8];
        private readonly Allpass[] _al = new Allpass[4], _ar = new Allpass[4];
        private float _width = 1f;

        private int _warm, _warmLen;   // 3.4.1: Eingang beim Einschalten kurz einblenden (sonst setzt der Nachhall hart ein)
        public void WarmUp(int frames) { _warmLen = _warm = Math.Max(0, frames); }

        public Reverb(int rate)
        {
            double k = rate / 44100.0;
            for (int i = 0; i < 8; i++) { _cl[i] = new Comb((int)(CombTuning[i] * k)); _cr[i] = new Comb((int)((CombTuning[i] + Spread) * k)); }
            for (int i = 0; i < 4; i++) { _al[i] = new Allpass((int)(AllpassTuning[i] * k)); _ar[i] = new Allpass((int)((AllpassTuning[i] + Spread) * k)); }
            SetRoom(0.5f, 0.5f, 1f);
        }

        /// <summary>room 0..1 (Groesse), damp 0..1 (Hoehen schlucken), width 0..1 (Stereo-Breite).</summary>
        public void SetRoom(float room, float damp, float width)
        {
            float fb = room * 0.28f + 0.7f, d = damp * 0.4f;
            foreach (var c in _cl) { c.Feedback = fb; c.Damp1 = d; c.Damp2 = 1 - d; }
            foreach (var c in _cr) { c.Feedback = fb; c.Damp1 = d; c.Damp2 = 1 - d; }
            _width = width;
        }

        /// <summary>Hall-Voreinstellung: 0 kleiner Raum, 1 Halle, 2 Kathedrale.</summary>
        public void SetPreset(int p)
        {
            if (p <= 0) SetRoom(0.45f, 0.55f, 0.8f);
            else if (p == 1) SetRoom(0.78f, 0.35f, 1f);
            else SetRoom(0.94f, 0.2f, 1f);
        }

        public void Clear()
        {
            foreach (var c in _cl) c.Clear(); foreach (var c in _cr) c.Clear();
            foreach (var a in _al) a.Clear(); foreach (var a in _ar) a.Clear();
        }

        /// <summary>4.0 (finale): nur der Hall (Hall-Bus) – buf wird durch den Nachhall ersetzt.</summary>
        public void ProcessWet(float[] buf, int frames, float gain)
        {
            float wet = gain * 1.6f;
            float wet1 = wet * (_width / 2f + 0.5f), wet2 = wet * ((1f - _width) / 2f);
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float input = (buf[k] + buf[k + 1]) * FixedGain;
                float ol = 0f, or = 0f;
                for (int c = 0; c < 8; c++) { ol += _cl[c].Process(input); or += _cr[c].Process(input); }
                for (int a = 0; a < 4; a++) { ol = _al[a].Process(ol); or = _ar[a].Process(or); }
                buf[k] = ol * wet1 + or * wet2;
                buf[k + 1] = or * wet1 + ol * wet2;
            }
        }

        /// <summary>Hall dazumischen: dry bleibt (etwas leiser bei viel Hall), wet = Anteil.</summary>
        public void Process(float[] buf, int frames, float mix) => Process(buf, frames, mix, mix, 1);

        /// <summary>3.4.1: Anteil gleitet in den ersten rampFrames von mix0 nach mix1 (weich ein- und ausschalten).</summary>
        public void Process(float[] buf, int frames, float mix0, float mix1, int rampFrames)
        {
            float wet = mix0 * 1.6f, dry = 1f - mix0 * 0.45f;
            float wet1 = wet * (_width / 2f + 0.5f), wet2 = wet * ((1f - _width) / 2f);
            bool ramp = mix0 != mix1;
            rampFrames = Math.Max(1, Math.Min(frames, rampFrames));
            float dm = ramp ? (mix1 - mix0) / rampFrames : 0f;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                if (ramp && i <= rampFrames)
                {
                    float m = i < rampFrames ? mix0 + dm * (i + 1) : mix1;
                    wet = m * 1.6f; dry = 1f - m * 0.45f;
                    wet1 = wet * (_width / 2f + 0.5f); wet2 = wet * ((1f - _width) / 2f);
                }
                float l = buf[k], r = buf[k + 1];
                float input = (l + r) * FixedGain;
                if (_warm > 0) { input *= 1f - _warm / (float)(_warmLen + 1); _warm--; }
                float ol = 0f, or = 0f;
                for (int c = 0; c < 8; c++) { ol += _cl[c].Process(input); or += _cr[c].Process(input); }
                for (int a = 0; a < 4; a++) { ol = _al[a].Process(ol); or = _ar[a].Process(or); }
                buf[k] = l * dry + ol * wet1 + or * wet2;
                buf[k + 1] = r * dry + or * wet1 + ol * wet2;
            }
        }
    }

    /// <summary>Echo (Delay) mit Rueckkopplung, dumpfer werdenden Wiederholungen und auf Wunsch Ping-Pong.</summary>
    internal sealed class Echo
    {
        private readonly float[] _l, _r;
        private int _w, _delay;
        private float _fb = 0.3f, _lpL, _lpR;
        private bool _pingPong;
        private readonly int _rate;

        private int _warm, _warmLen;   // 3.4.1: Eingang beim Einschalten kurz einblenden (sonst setzt die erste Wiederholung hart ein)
        public void WarmUp(int frames) { _warmLen = _warm = Math.Max(0, frames); }

        public Echo(int rate)
        {
            _rate = rate;
            _l = new float[rate + 16];
            _r = new float[rate + 16];
            SetPreset(1);
        }

        /// <summary>0 kurz (Slapback), 1 mittel, 2 lang (Ping-Pong).</summary>
        public void SetPreset(int p)
        {
            int ms = p <= 0 ? 95 : p == 1 ? 270 : 420;
            _delay = Math.Max(1, Math.Min(_l.Length - 1, ms * _rate / 1000));
            _fb = p <= 0 ? 0.18f : p == 1 ? 0.36f : 0.45f;
            _pingPong = p >= 2;
        }

        public int DelayFrames => _delay;

        public void Clear() { Array.Clear(_l, 0, _l.Length); Array.Clear(_r, 0, _r.Length); _lpL = _lpR = 0; }

        public void Process(float[] buf, int frames, float mix) => Process(buf, frames, mix, mix, 1);

        /// <summary>3.4.1: Anteil gleitet in den ersten rampFrames von mix0 nach mix1.</summary>
        public void Process(float[] buf, int frames, float mix0, float mix1, int rampFrames)
        {
            float wet = mix0 * 1.2f, dry = 1f - mix0 * 0.3f;
            bool ramp = mix0 != mix1;
            rampFrames = Math.Max(1, Math.Min(frames, rampFrames));
            float dm = ramp ? (mix1 - mix0) / rampFrames : 0f;
            int len = _l.Length;
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                if (ramp && i <= rampFrames) { float m = i < rampFrames ? mix0 + dm * (i + 1) : mix1; wet = m * 1.2f; dry = 1f - m * 0.3f; }
                int rp = _w - _delay; if (rp < 0) rp += len;
                float dl = _l[rp], dr = _r[rp];
                _lpL += (dl - _lpL) * 0.45f;   // jede Wiederholung etwas dumpfer
                _lpR += (dr - _lpR) * 0.45f;
                if (_lpL < 1e-20f && _lpL > -1e-20f) _lpL = 0f;
                if (_lpR < 1e-20f && _lpR > -1e-20f) _lpR = 0f;
                float l = buf[k], r = buf[k + 1];
                float wl = l, wr = r;
                if (_warm > 0) { float wg = 1f - _warm / (float)(_warmLen + 1); wl *= wg; wr *= wg; _warm--; }   // 3.4.1
                if (_pingPong) { _l[_w] = (wl + wr) * 0.5f + _lpR * _fb; _r[_w] = _lpL * _fb; }
                else { _l[_w] = wl + _lpL * _fb; _r[_w] = wr + _lpR * _fb; }
                if (++_w >= len) _w = 0;
                buf[k] = l * dry + dl * wet;
                buf[k + 1] = r * dry + dr * wet;
            }
        }
    }

    /// <summary>
    /// Kopfhoerer-Raumklang ("Crossfeed" nach Bauer, wie bs2b): ein Teil jeder Seite kommt leicht verzoegert und dumpfer
    /// auf dem anderen Ohr an – wie bei Lautsprechern. Das Klangbild wandert aus dem Kopf nach vorne, ermuedet weniger.
    /// </summary>
    internal sealed class Crossfeed
    {
        private double _a0Lo, _b1Lo, _a0Hi, _a1Hi, _b1Hi, _gain;
        private double _loL, _loR, _hiL, _hiR, _asisL, _asisR;

        public Crossfeed(int rate, int level = 1) { Set(rate, level); }

        /// <summary>1 leicht (650 Hz, 9,5 dB Abstand), 2 mittel (700 Hz, 6 dB), 3 stark (700 Hz, 3,5 dB) – weniger Abstand = mehr aufs andere Ohr.</summary>
        public void Set(int rate, int level)
        {
            double fc = level <= 1 ? 650 : 700, feed = level <= 1 ? 9.5 : level == 2 ? 6.0 : 3.5;
            double gbLo = feed * -5.0 / 6.0 - 3.0, gbHi = feed / 6.0 - 3.0;
            double gLo = Math.Pow(10, gbLo / 20.0), gHi = 1.0 - Math.Pow(10, gbHi / 20.0);
            double fcHi = fc * Math.Pow(2.0, (gbLo - 20.0 * Math.Log10(gHi)) / 12.0);
            double x = Math.Exp(-2.0 * Math.PI * fc / rate);
            _b1Lo = x; _a0Lo = gLo * (1.0 - x);
            x = Math.Exp(-2.0 * Math.PI * fcHi / rate);
            _b1Hi = x; _a0Hi = 1.0 - gHi * (1.0 - x); _a1Hi = -x;
            _gain = 1.0 / (1.0 - gHi + gLo);
        }

        public void Clear() { _loL = _loR = _hiL = _hiR = _asisL = _asisR = 0; }

        public void Process(float[] buf, int frames)
        {
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                double l = buf[k], r = buf[k + 1];
                _loL = _a0Lo * l + _b1Lo * _loL;
                _loR = _a0Lo * r + _b1Lo * _loR;
                _hiL = _a0Hi * l + _a1Hi * _asisL + _b1Hi * _hiL;
                _hiR = _a0Hi * r + _a1Hi * _asisR + _b1Hi * _hiR;
                _asisL = l; _asisR = r;
                buf[k] = (float)((_hiL + _loR) * _gain);
                buf[k + 1] = (float)((_hiR + _loL) * _gain);
            }
            if (Math.Abs(_loL) < 1e-20) _loL = 0; if (Math.Abs(_loR) < 1e-20) _loR = 0;
            if (Math.Abs(_hiL) < 1e-20) _hiL = 0; if (Math.Abs(_hiR) < 1e-20) _hiR = 0;
        }
    }

    /// <summary>Hall bzw. Echo eines Kanals (Zustand im Mischer-Faden).</summary>
    internal sealed class ChannelFxState
    {
        private readonly int _rate;
        private Reverb _rev;
        private Echo _echo;
        private int _preset = -1;

        public ChannelFxState(int rate) { _rate = rate; }

        private float _mixCur;      // 3.4.1: tatsaechlicher Anteil (gleitet)
        private int _type = -1;     // laufender Effekt (-1 = keiner)
        // 3.4.1: mit "Kein Knacken" 50 ms von aus nach voll (vorher: Nachhall beim Ausschalten hart abgeschnitten -> Knacken)

        public void Process(float[] buf, int frames, ChannelFx fx)
        {
            bool off = fx == null || fx.IsOff;
            if (!CleanStart.On)
            {
                _mixCur = off ? 0f : fx.Mix; _type = off ? -1 : fx.Type;
                if (off) { _rev = null; _echo = null; return; }
                ProcessType(buf, frames, fx, fx.Mix, fx.Mix);
                return;
            }
            int want = off ? -1 : fx.Type;
            float target = off ? 0f : fx.Mix;
            if (want != _type && _type != -1) target = 0f;           // anderer Effekt: erst den laufenden ausblenden
            if (_type == -1 || (want != _type && _mixCur <= 0f))
            {
                _type = want; _mixCur = 0f; _preset = -1;
                if (_type == -1) { _rev = null; _echo = null; return; }
                target = fx.Mix;
            }
            float maxStep = frames / (_rate * 0.05f);
            float m0 = _mixCur;
            float m1 = target > m0 ? Math.Min(target, m0 + maxStep) : Math.Max(target, m0 - maxStep);
            int ramp = (int)Math.Ceiling(Math.Abs(m1 - m0) * _rate * 0.05f);   // so viele Frames braucht die Aenderung
            ProcessType(buf, frames, want == _type ? fx : null, m0, m1, ramp);
            _mixCur = m1;
            if (m1 <= 0f && want != _type) { _type = -1; _rev = null; _echo = null; }
        }

        private void ProcessType(float[] buf, int frames, ChannelFx fx, float m0, float m1, int ramp = 1)
        {
            if (_type == ChannelFx.Hall)
            {
                if (_rev == null) { _rev = new Reverb(_rate); _preset = -1; if (CleanStart.On) _rev.WarmUp(_rate / 100); }
                _echo = null;
                if (fx != null && _preset != fx.Preset) { _rev.SetPreset(fx.Preset); _preset = fx.Preset; }
                _rev.Process(buf, frames, m0, m1, ramp);
            }
            else
            {
                if (_echo == null) { _echo = new Echo(_rate); _preset = -1; if (CleanStart.On) _echo.WarmUp(_rate / 100); }
                _rev = null;
                if (fx != null && _preset != fx.Preset) { _echo.SetPreset(fx.Preset); _preset = fx.Preset; }
                _echo.Process(buf, frames, m0, m1, ramp);
            }
        }
    }

    /// <summary>EQ und Raumklang eines Ausgangs (Zustand im Mischer-Faden).</summary>
    internal sealed partial class OutputFxState
    {
        private float[] _bassBuf = new float[0];

        /// <summary>
        /// 3.5: Bass unter der Grenze links und rechts gleich: Weiche in Tief und Hoch (Linkwitz-Riley), der Tiefton wird
        /// mono gemischt und wieder dazugegeben. Tief + Hoch ergibt im Betrag genau den vollen Ton (keine Delle an der Grenze).
        /// </summary>
        private void BassMono(float[] buf, int frames)
        {
            if (_bassBuf.Length < frames * 2) _bassBuf = new float[frames * 2];
            Array.Copy(buf, _bassBuf, frames * 2);
            _bassLp.Process(_bassBuf, frames); _bassLp2.Process(_bassBuf, frames);
            _bassHp.Process(buf, frames); _bassHp2.Process(buf, frames);
            for (int i = 0, k = 0; i < frames; i++, k += 2)
            {
                float m = (_bassBuf[k] + _bassBuf[k + 1]) * 0.5f;
                buf[k] += m; buf[k + 1] += m;
            }
        }
    }


    internal sealed partial class OutputFxState
    {
        private readonly int _rate;
        private readonly Biquad[] _geq = new Biquad[10];
        private Crossfeed _cross;
        private Reverb _room;
        private OutputFx _p;
        // 3.5: Bass mono – Linkwitz-Riley-Weiche 4. Ordnung (Tief + Hoch ergibt wieder den vollen Ton)
        private readonly Biquad _bassLp = new Biquad(), _bassLp2 = new Biquad(), _bassHp = new Biquad(), _bassHp2 = new Biquad();
        private int _bassHz;

        public OutputFxState(int rate)
        {
            _rate = rate;
            for (int i = 0; i < 10; i++) _geq[i] = new Biquad();
        }

        private PeqState _peq;   // 4.0

        public void Process(float[] buf, int frames, OutputFx fx)
        {
            if (fx == null || fx.IsOff)
            {
                if (_peq != null && _peq.Active) _peq.Process(buf, frames, null);   // 4.0: weich aus
                _p = null; _cross = null; _room = null; _bassHz = 0; return;
            }
            if (!ReferenceEquals(fx, _p))
            {
                if (fx.Graphic != null)
                {
                    if (_p?.Graphic == null) foreach (var b in _geq) b.Reset();
                    GraphicEq.Setup(_geq, fx.Graphic, _rate);
                }
                if (fx.Space > 0)
                {
                    int level = fx.Space >= 4 ? 2 : fx.Space;
                    if (_cross == null) _cross = new Crossfeed(_rate, level); else _cross.Set(_rate, level);
                }
                else _cross = null;
                if (fx.Space >= 4) { if (_room == null) { _room = new Reverb(_rate); _room.SetRoom(0.35f, 0.6f, 0.6f); } }
                else _room = null;
                if (fx.BassMonoHz != _bassHz)
                {
                    if (_bassHz == 0) { _bassLp.Reset(); _bassLp2.Reset(); _bassHp.Reset(); _bassHp2.Reset(); }
                    _bassHz = fx.BassMonoHz;
                    if (_bassHz > 0)
                    {
                        _bassLp.LowPass(_rate, _bassHz); _bassLp2.LowPass(_rate, _bassHz);
                        _bassHp.HighPass(_rate, _bassHz); _bassHp2.HighPass(_rate, _bassHz);
                    }
                }
                _p = fx;
            }
            if (_bassHz > 0) BassMono(buf, frames);
            if (fx.Graphic != null)
                for (int b = 0; b < 10; b++) if (Math.Abs(fx.Graphic[b]) > 0.01f) _geq[b].Process(buf, frames);
            if (fx.Peq != null || (_peq != null && _peq.Active))   // 4.0: parametrischer EQ / Kopfhoerer-Profil / Hoer-EQ
            {
                if (_peq == null) _peq = new PeqState(_rate);
                _peq.Process(buf, frames, fx.Peq);
            }
            if (_room != null) _room.Process(buf, frames, 0.12f);
            if (_cross != null) _cross.Process(buf, frames);
        }
    }

    /// <summary>Mehrspur-Aufnahme: eine Spur (Kanal) mit Stille in Luecken, damit alle Spuren gleich lang und synchron bleiben.</summary>
    internal sealed class TrackTap
    {
        public readonly WavWriter Writer;
        private readonly int _rate;
        private long _lastMs;
        private const long MaxGapMs = 5000;

        public TrackTap(WavWriter w, int rate, long startMs) { Writer = w; _rate = rate; _lastMs = startMs; }

        /// <summary>Block nach dem Fader schreiben (Luecke davor mit Stille fuellen).</summary>
        public void Write(float[] buf, int frames, long now)
        {
            long gapMs = now - _lastMs - frames * 1000L / _rate;
            if (gapMs > 30) Writer.WriteSilence((int)(Math.Min(gapMs, MaxGapMs) * _rate / 1000));
            _lastMs = now;
            Writer.Write(buf, frames);
        }

        /// <summary>Kanal still/ohne Ausgabe: die Zeit mit Stille fuellen.</summary>
        public void Pump(long now)
        {
            long gap = now - _lastMs;
            if (gap < 120) return;
            _lastMs = now;
            Writer.WriteSilence((int)(Math.Min(gap, MaxGapMs) * _rate / 1000));
        }
    }

    /// <summary>
    /// 3.2: Verzoegerung pro Kanal (z. B. damit Ton und Bild zusammenpassen), bis 1 s. Aendert sich die Verzoegerung,
    /// wird 10 ms weich von der alten zur neuen Stelle uebergeblendet (kein Knacken).
    /// </summary>
    internal sealed class DelayLine
    {
        private const int Size = 65536;          // Frames (Zweierpotenz), reicht fuer 1 s bei 48 kHz plus Puffer
        private const int FadeLen = 480;         // 10 ms
        private readonly float[] _buf = new float[Size * 2];
        private int _w, _cur = -1, _old, _fade;

        public DelayLine(int rate) { }

        /// <summary>Keine Verzoegerung und nichts mehr am Ueberblenden – kann weg.</summary>
        public bool Idle => _cur == 0 && _fade == 0;

        public void Clear() { Array.Clear(_buf, 0, _buf.Length); }

        public void Process(float[] x, int n, int delayFrames)
        {
            int d = Math.Max(0, Math.Min(Size - 8192, delayFrames));
            if (_cur < 0) _cur = d;
            if (d != _cur) { _old = _cur; _cur = d; _fade = FadeLen; }
            const int mask = Size - 1;
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                int wi = (_w & mask) * 2;
                _buf[wi] = x[k]; _buf[wi + 1] = x[k + 1];
                int ri = ((_w - _cur) & mask) * 2;
                float l = _buf[ri], r = _buf[ri + 1];
                if (_fade > 0)
                {
                    int ro = ((_w - _old) & mask) * 2;
                    float t = _fade / (float)FadeLen;
                    l = l * (1f - t) + _buf[ro] * t;
                    r = r * (1f - t) + _buf[ro + 1] * t;
                    _fade--;
                }
                x[k] = l; x[k + 1] = r;
                _w++;
            }
            _w &= mask;
        }
    }
}
