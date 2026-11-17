using System;
using System.Collections.Generic;
using System.Linq;

namespace Mischpult.Audio
{
    /// <summary>4.0 (finale): eigene Kompressor-Werte eines Kanals (unveraenderlich). null = feste Werte wie bisher.</summary>
    internal sealed class CompSpec
    {
        public readonly float Thr, Ratio, AttMs, RelMs, Makeup, Knee;
        public CompSpec(float thr, float ratio, float attMs, float relMs, float makeup, float knee)
        {
            Thr = Clamp(thr, -60f, 0f, Compressor.ThresholdDb);
            Ratio = Clamp(ratio, 1f, 20f, Compressor.Ratio);
            AttMs = Clamp(attMs, 0.1f, 200f, Compressor.AttackMs);
            RelMs = Clamp(relMs, 10f, 3000f, Compressor.ReleaseMs);
            Makeup = Clamp(makeup, 0f, 24f, Compressor.MakeupDb);
            Knee = Clamp(knee, 0f, 18f, Compressor.KneeDb);
        }
        private static float Clamp(float v, float lo, float hi, float def) => float.IsNaN(v) || float.IsInfinity(v) ? def : Math.Max(lo, Math.Min(hi, v));
        public static readonly CompSpec Default = new CompSpec(Compressor.ThresholdDb, Compressor.Ratio, Compressor.AttackMs, Compressor.ReleaseMs, Compressor.MakeupDb, Compressor.KneeDb);
        public bool SameAs(CompSpec o) => o != null && o.Thr == Thr && o.Ratio == Ratio && o.AttMs == AttMs && o.RelMs == RelMs && o.Makeup == Makeup && o.Knee == Knee;
        public static bool Same(CompSpec a, CompSpec b) => a == null ? b == null : a.SameAs(b);

        /// <summary>Kennlinie: Pegelaenderung in dB (ohne Aufholverstaerkung).</summary>
        public float CurveDb(float x) => Curve(x, Thr, Ratio, Knee);

        public static float Curve(float x, float thr, float ratio, float knee)
        {
            float d = x - thr;
            if (knee < 0.01f) return d > 0 ? d / ratio - d : 0f;
            if (2 * d < -knee) return 0f;
            if (2 * Math.Abs(d) <= knee)
            {
                float t = d + knee / 2;
                return (1f / ratio - 1f) * t * t / (2 * knee);
            }
            return d / ratio - d;
        }
    }

    /// <summary>4.0 (finale): ein dynamisches EQ-Band – greift nur, wenn das Band lauter als die Schwelle ist.</summary>
    internal struct DynBand
    {
        public float Hz, Db, Q, Thr;
        public DynBand(float hz, float db, float q, float thr)
        {
            Hz = Math.Max(20f, Math.Min(20000f, hz)); Db = Math.Max(-24f, Math.Min(24f, db));
            Q = q > 0.05f ? Math.Min(12f, q) : 1f; Thr = Math.Max(-70f, Math.Min(0f, thr));
        }
        public bool Same(DynBand o) => o.Hz == Hz && o.Db == Db && o.Q == Q && o.Thr == Thr;
    }

    /// <summary>
    /// 4.0 (finale): Profi-Ton eines Kanals (unveraenderlich): eigener Kompressor, De-Esser, analoge Waerme, Rauschsperre,
    /// Brumm-Entferner, Polaritaet, Punch, Hall-Send, Effekt-Reihenfolge, Frequenz-Ducking und dynamische EQ-Baender.
    /// </summary>
    internal sealed class ChannelPro
    {
        public static readonly string[] SatNames = { "Aus", "Röhre", "Band", "Konsole" };
        public static readonly string[] DeEssNames = { "Aus", "Leicht", "Mittel", "Stark" };
        public const string DefaultOrder = "EKWF";

        public readonly CompSpec Comp;     // null = feste Werte
        public readonly int DeEss;         // 0 aus, 1..3
        public readonly int SatType;       // 0 aus, 1 Roehre, 2 Band, 3 Konsole
        public readonly float SatAmt;      // 0..1
        public readonly float GateDb;      // 0 = aus, sonst Schwelle (dBFS)
        public readonly int HumHz;         // 0, 50, 60
        public readonly bool Invert;
        public readonly int Punch;         // -2..2
        public readonly float Send;        // 0..1 (Hall-Bus)
        public readonly string Order;      // null = Standard (EQ, Kompressor, Waerme, Effekt)
        public readonly bool DuckArmed;    // Frequenz-Ducking moeglich (Kanal bleibt auf dem EQ-Weg)
        public readonly float DuckEqDb;    // aktuelle Absenkung im Sprachbereich (<= 0)
        public readonly DynBand[] Dyn;     // dynamische EQ-Baender (nie null)

        public ChannelPro(CompSpec comp, int deEss, int satType, float satAmt, float gateDb, int humHz, bool invert, int punch, float send, string order, bool duckArmed, float duckEqDb, IEnumerable<DynBand> dyn)
        {
            Comp = comp;
            DeEss = Math.Max(0, Math.Min(3, deEss));
            SatType = Math.Max(0, Math.Min(3, satType));
            SatAmt = float.IsNaN(satAmt) ? 0.5f : Math.Max(0f, Math.Min(1f, satAmt));
            GateDb = float.IsNaN(gateDb) || gateDb >= -1f ? 0f : Math.Max(-90f, gateDb);
            HumHz = humHz == 50 || humHz == 60 ? humHz : 0;
            Invert = invert;
            Punch = Math.Max(-2, Math.Min(2, punch));
            Send = float.IsNaN(send) ? 0f : Math.Max(0f, Math.Min(1f, send));
            Order = CleanOrder(order);
            DuckArmed = duckArmed;
            DuckEqDb = !duckArmed || float.IsNaN(duckEqDb) ? 0f : Math.Max(-30f, Math.Min(0f, duckEqDb));
            Dyn = dyn == null ? new DynBand[0] : dyn.Take(12).ToArray();
        }

        /// <summary>Reihenfolge pruefen: jede der Stufen E, K, W, F genau einmal – sonst Standard (null).</summary>
        public static string CleanOrder(string o)
        {
            if (string.IsNullOrEmpty(o) || o.Length != 4) return null;
            o = o.ToUpperInvariant();
            foreach (char c in DefaultOrder) if (o.IndexOf(c) < 0) return null;
            return o == DefaultOrder ? null : o;
        }

        public bool IsNeutral => Comp == null && DeEss == 0 && SatType == 0 && GateDb == 0f && HumHz == 0 && !Invert && Punch == 0 && Send <= 0f && Order == null && !DuckArmed && Dyn.Length == 0;

        public bool SameAs(ChannelPro o)
        {
            if (o == null || !CompSpec.Same(o.Comp, Comp) || o.DeEss != DeEss || o.SatType != SatType || o.SatAmt != SatAmt || o.GateDb != GateDb || o.HumHz != HumHz
                || o.Invert != Invert || o.Punch != Punch || o.Send != Send || o.Order != Order || o.DuckArmed != DuckArmed || o.DuckEqDb != DuckEqDb || o.Dyn.Length != Dyn.Length) return false;
            for (int i = 0; i < Dyn.Length; i++) if (!Dyn[i].Same(o.Dyn[i])) return false;
            return true;
        }

        public static bool Same(ChannelPro a, ChannelPro b)
        {
            bool na = a == null || a.IsNeutral, nb = b == null || b.IsNeutral;
            if (na || nb) return na && nb;
            return a.SameAs(b);
        }

        public static string OrderLabel(string o)
        {
            o = CleanOrder(o) ?? DefaultOrder;
            return string.Join(" → ", o.Select(c => c == 'E' ? "EQ" : c == 'K' ? "Kompressor" : c == 'W' ? "Wärme" : "Hall/Echo"));
        }
    }

    /// <summary>4.0 (finale): Hall-Bus (unveraenderlich): Groesse und Rueckweg-Pegel.</summary>
    internal sealed class HallSpec
    {
        public readonly int Preset;     // 0 kleiner Raum, 1 Halle, 2 Kathedrale
        public readonly float Return;   // linear
        public HallSpec(int preset, float returnDb)
        {
            Preset = Math.Max(0, Math.Min(2, preset));
            Return = (float)Math.Pow(10, Math.Max(-30f, Math.Min(6f, returnDb)) / 20.0);
        }
    }

    /// <summary>4.0 (finale): Signalgenerator (unveraenderlich).</summary>
    internal sealed class GenSpec
    {
        public static readonly string[] KindNames = { "Sinus", "Gleitton 20 Hz – 20 kHz", "Rosa Rauschen", "Weißes Rauschen" };
        public readonly int Kind;          // 0 Sinus, 1 Gleitton, 2 rosa, 3 weiss
        public readonly float Hz;
        public readonly float Gain;        // linear
        public readonly string DeviceKey;  // "" = Windows-Standardgeraet
        public readonly int Side;          // 0 beide, 1 links, 2 rechts
        public GenSpec(int kind, float hz, float levelDb, string deviceKey, int side = 0)
        {
            Kind = Math.Max(0, Math.Min(3, kind));
            Hz = float.IsNaN(hz) ? 1000f : Math.Max(20f, Math.Min(20000f, hz));
            Gain = (float)Math.Pow(10, Math.Max(-80f, Math.Min(-6f, levelDb)) / 20.0);
            DeviceKey = deviceKey ?? "";
            Side = Math.Max(0, Math.Min(2, side));
        }
    }

    /// <summary>4.0 (finale): globale Profi-Ton-Werte zwischen Oberflaeche und Mischer (ohne den Umweg ueber die Engine).</summary>
    internal static class Pro400
    {
        public static volatile HallSpec Hall;           // null = Hall-Bus aus
        public static volatile bool StreamLimitOn;      // True-Peak-Begrenzer im Stream-Mix
        public static volatile GenSpec Gen;             // null = Signalgenerator aus
        public static float StreamGrDb;                 // staerkste Absenkung seit dem letzten Abholen (dB, positiv)
        public static float HallPeak;                   // Pegel des Hall-Rueckwegs (linear)
        private static readonly object _lock = new object();

        public static void NoteStreamGr(float db) { lock (_lock) if (db > StreamGrDb) StreamGrDb = db; }
        public static float TakeStreamGr() { lock (_lock) { float r = StreamGrDb; StreamGrDb = 0f; return r; } }
        public static void NoteHall(float pk) { lock (_lock) if (pk > HallPeak) HallPeak = pk; }
        public static float TakeHall() { lock (_lock) { float r = HallPeak; HallPeak = 0f; return r; } }
    }

    /// <summary>4.0 (finale): Zustand des Profi-Tons je Kanal. Jede Stufe blendet beim Ein- und Ausschalten weich.</summary>
    internal sealed class ProState
    {
        private readonly int _rate;
        private float[] _dry = new float[0], _band = new float[0];
        public ProState(int rate) { _rate = rate; }

        private float Coef(double ms) => (float)Math.Exp(-1.0 / (_rate * ms / 1000.0));
        private void Ensure(int n)
        {
            if (_dry.Length < n * 2) _dry = new float[n * 2];
            if (_band.Length < n * 2) _band = new float[n * 2];
        }

        /// <summary>Alle Stufen sind aus und ausgeklungen (Zustand kann weg).</summary>
        public bool Idle => _sign >= 1f && _gateG >= 1f && _humMix <= 0f && _dyn.Length == 0 && _deG >= 1f && _punchA == 0f && _satMix <= 0f && _duckDb >= 0f && _send <= 0f;

        // ------------------------------------------------------------ Polaritaet, Rauschsperre (frueh)
        private float _sign = 1f;
        private float _gateEnv, _gateG = 1f; private int _gateHold; private bool _gateOpen = true;

        public void Pre(float[] buf, int n, ChannelPro p)
        {
            // Polaritaet: in 20 ms umdrehen (geht dabei kurz durch null statt zu knacken)
            float sT = p != null && p.Invert ? -1f : 1f;
            if (_sign != sT || sT < 0f)
            {
                float s = _sign, step = 2f / (_rate * 0.02f);
                for (int k = 0; k < n * 2; k += 2)
                {
                    if (s < sT) { s += step; if (s > sT) s = sT; } else if (s > sT) { s -= step; if (s < sT) s = sT; }
                    buf[k] *= s; buf[k + 1] *= s;
                }
                _sign = s;
            }
            // Rauschsperre: unter der Schwelle leise (−50 dB), mit Haltezeit und weichem Schliessen
            float thr = p == null ? 0f : p.GateDb;
            if (thr < 0f || _gateG < 1f)
            {
                float open = thr < 0f ? (float)Math.Pow(10, thr / 20.0) : 0f, close = open * 0.5f;
                float floor = 0.00316f;
                float eA = Coef(0.5), eR = Coef(60), gO = Coef(1.5);
                float closeMul = (float)Math.Pow(10, -330.0 / 20.0 / _rate);   // schliesst gleichmaessig: −50 dB in 150 ms
                int hold = (int)(_rate * 0.12);
                float env = _gateEnv, g = _gateG; int h = _gateHold; bool on = _gateOpen;
                for (int k = 0; k < n * 2; k += 2)
                {
                    float a = Math.Max(Math.Abs(buf[k]), Math.Abs(buf[k + 1]));
                    env = a > env ? eA * env + (1 - eA) * a : eR * env + (1 - eR) * a;
                    if (thr >= 0f) on = true;
                    else if (env > open) { on = true; h = hold; }
                    else if (env < close) { if (h > 0) h--; else on = false; }
                    float t = on ? 1f : floor;
                    g = t > g ? gO * g + (1 - gO) * t : Math.Max(t, g * closeMul);
                    if (g > 0.9999f && t >= 1f) g = 1f;
                    buf[k] *= g; buf[k + 1] *= g;
                }
                _gateEnv = env < 1e-9f ? 0f : env; _gateG = g; _gateHold = h; _gateOpen = on;
            }
        }

        // ------------------------------------------------------------ EQ-Stufe: Brumm-Entferner, dynamische Baender
        private readonly Biquad[] _hum = { new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad(), new Biquad() };
        private int _humCur; private float _humMix;
        private Biquad[] _dyn = new Biquad[0];
        private float[] _dynEnv = new float[0], _dynDb = new float[0];
        private DynBand[] _dynSpec = new DynBand[0];

        public void Eq(float[] buf, int n, ChannelPro p)
        {
            int humT = p?.HumHz ?? 0;
            if (humT != 0 || _humMix > 0f)
            {
                Ensure(n);
                if (humT != _humCur && _humMix <= 0f)
                {
                    _humCur = humT;
                    if (humT > 0)
                        for (int i = 0; i < _hum.Length; i++) { _hum[i].Reset(); _hum[i].Notch(_rate, humT * (i + 1), 8.0 + 4.0 * i); }
                }
                if (_humCur > 0)
                {
                    Array.Copy(buf, _dry, n * 2);
                    foreach (var b in _hum) b.Process(buf, n);
                    float target = humT == _humCur && humT > 0 ? 1f : 0f;
                    _humMix = DspMixer.MixToward(buf, _dry, n, _humMix, target, 1f / DspMixer.SwitchFadeFrames);
                }
                else _humMix = 0f;
            }

            var dyn = p?.Dyn ?? new DynBand[0];
            if (dyn.Length != _dynSpec.Length || !SameDyn(dyn))
            {
                // Plaetze mit gleicher Nummer behalten ihr Gedaechtnis
                var nb = new Biquad[dyn.Length]; var ne = new float[dyn.Length]; var nd = new float[dyn.Length];
                for (int i = 0; i < dyn.Length; i++)
                {
                    nb[i] = i < _dyn.Length ? _dyn[i] : new Biquad();
                    nb[i].BandPass(_rate, Math.Min(dyn[i].Hz, _rate * 0.45), dyn[i].Q);
                    if (i < _dyn.Length) { ne[i] = _dynEnv[i]; nd[i] = _dynDb[i]; }
                }
                _dyn = nb; _dynEnv = ne; _dynDb = nd; _dynSpec = (DynBand[])dyn.Clone();
            }
            if (_dyn.Length == 0) return;
            Ensure(n);
            float eA = Coef(3), eR = Coef(80), gA = (float)Math.Pow(Coef(4), 16), gR = (float)Math.Pow(Coef(150), 16);
            for (int b = 0; b < _dyn.Length; b++)
            {
                var s = _dynSpec[b];
                Array.Copy(buf, _band, n * 2);
                _dyn[b].Process(_band, n);
                float env = _dynEnv[b], gDb = _dynDb[b];
                float G = (float)Math.Pow(10, gDb / 20.0) - 1f;
                for (int i = 0, k = 0; i < n; i++, k += 2)
                {
                    float a = Math.Max(Math.Abs(_band[k]), Math.Abs(_band[k + 1]));
                    env = a > env ? eA * env + (1 - eA) * a : eR * env + (1 - eR) * a;
                    if ((i & 15) == 0)
                    {
                        float lv = env > 1e-6f ? 20f * (float)Math.Log10(env) : -120f;
                        float over = lv - s.Thr;
                        float tDb = over > 0f ? s.Db * Math.Min(1f, over / 6f) : 0f;
                        bool towards = Math.Abs(tDb) > Math.Abs(gDb);
                        float c = towards ? gA : gR;
                        gDb = c * gDb + (1 - c) * tDb;
                        G = (float)Math.Pow(10, gDb / 20.0) - 1f;
                    }
                    buf[k] += G * _band[k]; buf[k + 1] += G * _band[k + 1];
                }
                _dynEnv[b] = env < 1e-9f ? 0f : env; _dynDb[b] = Math.Abs(gDb) < 1e-4f ? 0f : gDb;
            }
        }

        private bool SameDyn(DynBand[] d)
        {
            for (int i = 0; i < d.Length; i++) if (!d[i].Same(_dynSpec[i])) return false;
            return true;
        }

        /// <summary>Wie stark die dynamischen Baender gerade eingreifen (dB je Band) – fuer die Anzeige.</summary>
        public float[] DynNow => (float[])_dynDb.Clone();

        // ------------------------------------------------------------ Dynamik-Stufe: De-Esser, Punch
        private readonly Biquad _deHp = new Biquad();
        private bool _deInit; private float _deEnvF, _deEnvS, _deG = 1f;
        private float _hxL, _hyL, _hxR, _hyR;   // Hochpass 1. Ordnung (3,5 kHz): 1 + (g−1)·HP = genauer Hoehen-Kuhschwanz
        public float DeEssReduction;   // dB, seit dem letzten Abholen
        private float _envF, _envS, _punchA;

        public void Dyn(float[] buf, int n, ChannelPro p)
        {
            int de = p?.DeEss ?? 0;
            if (de > 0 || _deG < 1f)
            {
                Ensure(n);
                if (!_deInit) { _deHp.HighPass(_rate, 5500, 0.7071); _deInit = true; }
                Array.Copy(buf, _band, n * 2);
                _deHp.Process(_band, n);
                float t = de == 1 ? 0.45f : de == 2 ? 0.33f : 0.24f;
                float minG = de == 1 ? 0.5f : de == 2 ? 0.32f : 0.15f;
                float eA = Coef(0.8), eR = Coef(50), gA = Coef(1.0), gR = Coef(60);
                float ef = _deEnvF, es = _deEnvS, g = _deG, low = 1f;
                double rc = 1.0 / (2 * Math.PI * 3500.0);
                float ha = (float)(rc / (rc + 1.0 / _rate));
                for (int k = 0; k < n * 2; k += 2)
                {
                    float a = Math.Max(Math.Abs(buf[k]), Math.Abs(buf[k + 1]));
                    float sb = Math.Max(Math.Abs(_band[k]), Math.Abs(_band[k + 1]));
                    ef = a > ef ? eA * ef + (1 - eA) * a : eR * ef + (1 - eR) * a;
                    es = sb > es ? eA * es + (1 - eA) * sb : eR * es + (1 - eR) * sb;
                    float target = 1f;
                    if (de > 0 && es > 0.003f)
                    {
                        float r = es / (ef + 1e-9f);
                        if (r > t) { float q = t / r; target = Math.Max(minG, q * q); }
                    }
                    g = target < g ? gA * g + (1 - gA) * target : gR * g + (1 - gR) * target;
                    if (g > 0.9999f && target >= 1f) g = 1f;
                    float xl = buf[k], xr = buf[k + 1];
                    float bl = ha * (_hyL + xl - _hxL), br = ha * (_hyR + xr - _hxR);
                    _hxL = xl; _hyL = bl; _hxR = xr; _hyR = br;
                    float cut = 1f - g;
                    buf[k] = xl - cut * bl; buf[k + 1] = xr - cut * br;
                    if (g < low) low = g;
                }
                _deEnvF = ef < 1e-9f ? 0f : ef; _deEnvS = es < 1e-9f ? 0f : es; _deG = g;
                if (Math.Abs(_hyL) < 1e-20f) _hyL = 0f; if (Math.Abs(_hyR) < 1e-20f) _hyR = 0f;
                if (low < 1f) DeEssReduction = Math.Max(DeEssReduction, -20f * (float)Math.Log10(Math.Max(1e-4f, low)));
            }

            float aT = (p?.Punch ?? 0) / 2f;
            if (aT != 0f || _punchA != 0f)
            {
                float fA = Coef(0.4), fR = Coef(35), sA = Coef(18), sR = Coef(180);
                float step = 1f / DspMixer.SwitchFadeFrames;
                float ef = _envF, es = _envS, am = _punchA, gain = 1f;
                for (int i = 0, k = 0; i < n; i++, k += 2)
                {
                    float a = Math.Max(Math.Abs(buf[k]), Math.Abs(buf[k + 1]));
                    ef = a > ef ? fA * ef + (1 - fA) * a : fR * ef + (1 - fR) * a;
                    es = a > es ? sA * es + (1 - sA) * a : sR * es + (1 - sR) * a;
                    if (am < aT) { am += step; if (am > aT) am = aT; } else if (am > aT) { am -= step; if (am < aT) am = aT; }
                    if ((i & 7) == 0)
                    {
                        float diff = ef > 1e-6f && es > 1e-6f ? 20f * (float)Math.Log10(ef / es) : 0f;
                        diff = Math.Max(0f, Math.Min(12f, diff));
                        float db = am * diff * 0.6f;
                        db = Math.Max(-9f, Math.Min(6f, db));
                        gain = (float)Math.Pow(10, db / 20.0);
                    }
                    buf[k] *= gain; buf[k + 1] *= gain;
                }
                _envF = ef < 1e-9f ? 0f : ef; _envS = es < 1e-9f ? 0f : es; _punchA = Math.Abs(am) < 1e-6f ? 0f : am;
            }
        }

        // ------------------------------------------------------------ Waerme (Saettigung)
        private int _satType; private float _satMix, _satAmt = 0.5f;
        private float _dcXl, _dcYl, _dcXr, _dcYr, _lpL, _lpR;

        public void Warm(float[] buf, int n, ChannelPro p)
        {
            int tT = p?.SatType ?? 0;
            if (tT == 0 && _satMix <= 0f) { _satType = 0; return; }
            Ensure(n);
            if (tT != _satType && _satMix <= 0f) { _satType = tT; _satAmt = p?.SatAmt ?? 0.5f; _dcXl = _dcYl = _dcXr = _dcYr = 0f; _lpL = _lpR = 0f; }
            if (_satType == 0) { _satMix = 0f; return; }
            float amtT = p?.SatAmt ?? _satAmt;
            Array.Copy(buf, _dry, n * 2);
            float amt = _satAmt, aStep = 1f / DspMixer.SwitchFadeFrames;
            float dc = 1f - (float)(2 * Math.PI * 12.0 / _rate);
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                if (amt < amtT) { amt += aStep; if (amt > amtT) amt = amtT; } else if (amt > amtT) { amt -= aStep; if (amt < amtT) amt = amtT; }
                float drive = 1f + amt * 5f;
                for (int c = 0; c < 2; c++)
                {
                    float x = buf[k + c], y;
                    switch (_satType)
                    {
                        case 1:   // Roehre: unsymmetrisch -> geradzahlige Obertoene
                            {
                                const float bias = 0.22f;
                                float tb = (float)Math.Tanh(bias);
                                y = ((float)Math.Tanh(drive * x + bias) - tb) / (drive * (1f - tb * tb));
                                // Gleichanteil weg
                                if (c == 0) { float o = y - _dcXl + dc * _dcYl; _dcXl = y; _dcYl = o; y = o; }
                                else { float o = y - _dcXr + dc * _dcYr; _dcXr = y; _dcYr = o; y = o; }
                                break;
                            }
                        case 2:   // Band: weich gesaettigt, Hoehen etwas runder
                            {
                                y = (float)Math.Tanh(drive * x) / drive;
                                float lpC = 0.55f + 0.35f * (1f - amt);
                                if (c == 0) { _lpL += lpC * (y - _lpL); y = _lpL; } else { _lpR += lpC * (y - _lpR); y = _lpR; }
                                break;
                            }
                        default:  // Konsole: ganz leichte ungeradzahlige Obertoene
                            {
                                float cc = 0.06f + 0.18f * amt;
                                float xc = Math.Max(-1.2f, Math.Min(1.2f, x));
                                y = xc - cc * xc * xc * xc;
                                break;
                            }
                    }
                    buf[k + c] = y;
                }
            }
            _satAmt = amt;
            float target = tT == _satType && tT != 0 ? 1f : 0f;
            _satMix = DspMixer.MixToward(buf, _dry, n, _satMix, target, 1f / DspMixer.SwitchFadeFrames);
            if (_satMix <= 0f && target <= 0f) _satType = 0;
        }

        // ------------------------------------------------------------ Frequenz-Ducking (nur Sprachbereich leiser)
        private readonly Biquad _duckBp = new Biquad();
        private bool _duckInit; private float _duckDb;

        public void Post(float[] buf, int n, ChannelPro p)
        {
            float tDb = p?.DuckEqDb ?? 0f;
            if (tDb >= 0f && _duckDb >= 0f) return;
            Ensure(n);
            if (!_duckInit) { _duckBp.BandPass(_rate, 1400, 0.55); _duckInit = true; }
            Array.Copy(buf, _band, n * 2);
            _duckBp.Process(_band, n);
            float d = _duckDb, down = 90f / _rate, up = 40f / _rate;
            float G = (float)Math.Pow(10, d / 20.0) - 1f;
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                if (d > tDb) { d -= down; if (d < tDb) d = tDb; } else if (d < tDb) { d += up; if (d > tDb) d = tDb; }
                if ((i & 15) == 0) G = (float)Math.Pow(10, d / 20.0) - 1f;
                buf[k] += G * _band[k]; buf[k + 1] += G * _band[k + 1];
            }
            _duckDb = d > -0.01f ? 0f : d;
        }

        // ------------------------------------------------------------ Hall-Send (nach dem Fader)
        private float _send;
        public bool Sending => _send > 0f;
        public void SendInto(float[] bus, float[] buf, int n, float target)
        {
            if (target <= 0f && _send <= 0f) return;
            float s = _send, step = 1f / DspMixer.SwitchFadeFrames;
            for (int k = 0; k < n * 2; k += 2)
            {
                if (s < target) { s += step; if (s > target) s = target; } else if (s > target) { s -= step; if (s < target) s = target; }
                bus[k] += buf[k] * s; bus[k + 1] += buf[k + 1] * s;
            }
            _send = s;
        }
    }

    /// <summary>4.0 (finale): Hall-Bus eines Ausgangs (Summe der SENDs -> Hall -> zurueck in den Mix).</summary>
    internal sealed class HallBus
    {
        public readonly Reverb Rev;
        public float[] Buf = new float[0];
        public int Preset = -1;
        public int Quiet;        // Frames ohne Eingang (Nachhall klingt aus)
        public bool Fed;
        public float Return = 1f;
        public HallBus(int rate) { Rev = new Reverb(rate); }
    }

    /// <summary>
    /// 4.0 (finale): True-Peak-Begrenzer mit Vorausschau fuer den Stream-Mix: −1 dBTP, auch Spitzen zwischen den Samples
    /// (4-fach geschaetzt). Min-Halten + Mittelwert ueber die Vorausschau -> greift rechtzeitig, ohne zu knacken.
    /// </summary>
    internal sealed class TruePeakLimiter
    {
        public const float CeilingDb = -1f;
        private readonly int _la;
        private readonly float _ceil, _rel;
        private readonly float[] _delay;          // Stereo-Ring (la+1 Frames)
        private readonly float[] _box;            // Ring der geglaetteten Werte
        private readonly long[] _qi; private readonly float[] _qv;   // aufsteigende Warteschlange (gleitendes Minimum)
        private int _qh, _qn, _pos;
        private long _idx;
        private double _sum;
        private float _r = 1f;
        private float _h0l, _h1l, _h2l, _h0r, _h1r, _h2r;   // letzte Samples (fuer die Zwischenwerte)

        public TruePeakLimiter(int rate)
        {
            _la = Math.Max(16, rate * 15 / 10000);   // 1,5 ms
            _ceil = (float)Math.Pow(10, CeilingDb / 20.0);
            _rel = 1f - (float)Math.Exp(-1.0 / (rate * 0.120));
            _delay = new float[(_la + 1) * 2];
            _box = new float[_la + 1];
            for (int i = 0; i < _box.Length; i++) _box[i] = 1f;
            _sum = _box.Length;
            _qi = new long[_la + 3]; _qv = new float[_la + 3];
        }

        public int LatencyFrames => _la + 1;

        private static float Cr(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        private static float SegPeak(float p0, float p1, float p2, float p3)
        {
            float m = Math.Abs(p3);
            float a = Math.Abs(Cr(p0, p1, p2, p3, 0.25f)); if (a > m) m = a;
            a = Math.Abs(Cr(p0, p1, p2, p3, 0.5f)); if (a > m) m = a;
            a = Math.Abs(Cr(p0, p1, p2, p3, 0.75f)); if (a > m) m = a;
            return Math.Max(m, Math.Abs(p2));
        }

        public void Process(float[] buf, int n)
        {
            int len = _la + 1;
            float maxRed = 0f;
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                float xl = buf[k], xr = buf[k + 1];
                float tp = Math.Max(SegPeak(_h0l, _h1l, _h2l, xl), SegPeak(_h0r, _h1r, _h2r, xr));
                _h0l = _h1l; _h1l = _h2l; _h2l = xl; _h0r = _h1r; _h1r = _h2r; _h2r = xr;
                float req = tp > _ceil ? _ceil / tp : 1f;

                // gleitendes Minimum ueber die letzten len Werte
                long id = _idx++;
                int cap = _qv.Length;
                while (_qn > 0 && _qv[(_qh + _qn - 1) % cap] >= req) _qn--;
                int tail = (_qh + _qn) % cap;
                _qi[tail] = id; _qv[tail] = req; _qn++;
                while (_qn > 1 && _qi[_qh] <= id - len) { _qh = (_qh + 1) % cap; _qn--; }
                float mn = _qv[_qh];

                // Loslassen
                float r = _r + (1f - _r) * _rel;
                if (mn < r) r = mn;
                _r = r;
                // Mittelwert ueber die Vorausschau
                _sum += r - _box[_pos];
                _box[_pos] = r;
                float g = (float)(_sum / len);
                if (g > 1f) g = 1f;

                // verzoegert ausgeben
                int dp = _pos * 2;
                float ol = _delay[dp], or = _delay[dp + 1];
                _delay[dp] = xl; _delay[dp + 1] = xr;
                if (++_pos >= len) _pos = 0;
                buf[k] = ol * g; buf[k + 1] = or * g;
                if (g < 1f) { float db = -20f * (float)Math.Log10(Math.Max(1e-5f, g)); if (db > maxRed) maxRed = db; }
            }
            if ((_idx & 0xFFFF) < n) { double s = 0; foreach (var v in _box) s += v; _sum = s; }   // Rundungsfehler einsammeln
            if (maxRed > 0f) Pro400.NoteStreamGr(maxRed);
        }
    }
}
