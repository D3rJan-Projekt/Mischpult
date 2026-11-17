using System;
using System.Collections.Generic;
using System.Linq;

namespace Mischpult.Audio
{
    /// <summary>4.0: ein Filter des parametrischen EQ (unveraenderlich).</summary>
    internal struct PeqSpec
    {
        public int Type;      // 0 Glocke, 1 Bass-Kuhschwanz, 2 Hoehen-Kuhschwanz, 3 Hochpass, 4 Tiefpass
        public float Hz, Db, Q;
        public PeqSpec(int type, float hz, float db, float q) { Type = type; Hz = hz; Db = db; Q = q; }
        public bool IsFlat => (Type <= 2 && Math.Abs(Db) < 0.01f) || Hz <= 0f;
        public bool Same(PeqSpec o) => o.Type == Type && o.Hz == Hz && o.Db == Db && o.Q == Q;
    }

    /// <summary>
    /// 4.0: parametrischer EQ (Kanal oder Ausgang), unveraenderlich: Baender fuer beide Ohren, dazu je Ohr eigene
    /// Baender (Hoer-EQ) und eine Vorverstaerkung (AutoEq-Profile senken vorher ab, damit nichts uebersteuert).
    /// </summary>
    internal sealed class PeqSet
    {
        public readonly PeqSpec[] Both, Left, Right;
        public readonly PeqSpec[] Mid, Side;   // 4.0 (finale): Mitte/Seite-EQ
        public readonly float PreampDb;

        public PeqSet(IEnumerable<PeqSpec> both, IEnumerable<PeqSpec> left, IEnumerable<PeqSpec> right, float preampDb) : this(both, left, right, null, null, preampDb) { }

        public PeqSet(IEnumerable<PeqSpec> both, IEnumerable<PeqSpec> left, IEnumerable<PeqSpec> right, IEnumerable<PeqSpec> mid, IEnumerable<PeqSpec> side, float preampDb)
        {
            Both = Clean(both); Left = Clean(left); Right = Clean(right); Mid = Clean(mid); Side = Clean(side);
            PreampDb = float.IsNaN(preampDb) ? 0f : Math.Max(-30f, Math.Min(12f, preampDb));
        }

        private static PeqSpec[] Clean(IEnumerable<PeqSpec> s) =>
            s == null ? new PeqSpec[0] : s.Where(x => !x.IsFlat).Select(x => new PeqSpec(Math.Max(0, Math.Min(4, x.Type)), Math.Max(10f, Math.Min(22000f, x.Hz)), Math.Max(-30f, Math.Min(30f, x.Db)), x.Q > 0.05f ? Math.Min(20f, x.Q) : 0.71f)).ToArray();

        public bool IsFlat => Both.Length == 0 && Left.Length == 0 && Right.Length == 0 && Mid.Length == 0 && Side.Length == 0 && Math.Abs(PreampDb) < 0.01f;

        public bool SameAs(PeqSet o)
        {
            if (o == null || o.PreampDb != PreampDb || o.Both.Length != Both.Length || o.Left.Length != Left.Length || o.Right.Length != Right.Length || o.Mid.Length != Mid.Length || o.Side.Length != Side.Length) return false;
            for (int i = 0; i < Mid.Length; i++) if (!Mid[i].Same(o.Mid[i])) return false;
            for (int i = 0; i < Side.Length; i++) if (!Side[i].Same(o.Side[i])) return false;
            for (int i = 0; i < Both.Length; i++) if (!Both[i].Same(o.Both[i])) return false;
            for (int i = 0; i < Left.Length; i++) if (!Left[i].Same(o.Left[i])) return false;
            for (int i = 0; i < Right.Length; i++) if (!Right[i].Same(o.Right[i])) return false;
            return true;
        }

        public static bool Same(PeqSet a, PeqSet b)
        {
            bool na = a == null || a.IsFlat, nb = b == null || b.IsFlat;
            if (na || nb) return na && nb;
            return a.SameAs(b);
        }

        /// <summary>Aus den gespeicherten Baendern (abgeschaltete und flache fallen weg). null = nichts zu tun.</summary>
        public static PeqSet From(List<PeqBand> bands, float preamp = 0f)
        {
            if ((bands == null || bands.Count == 0) && Math.Abs(preamp) < 0.01f) return null;
            var s = new PeqSet(bands?.Where(b => b != null && !b.Off).Select(b => new PeqSpec(b.Type, b.Hz, b.Db, b.QNow)), null, null, preamp);
            return s.IsFlat ? null : s;
        }

        /// <summary>
        /// 4.0 (finale): wie From, dazu Mitte/Seite (ms) – Baender mit Mode 1/2 wirken nur auf die Mitte bzw. die Seite –
        /// und ohne dynamische Baender (dyn: die laufen getrennt im Profi-Ton).
        /// </summary>
        public static PeqSet From(List<PeqBand> bands, bool ms, bool dyn)
        {
            if (!ms && !dyn) return From(bands);
            if (bands == null || bands.Count == 0) return null;
            var use = bands.Where(b => b != null && !b.Off && !(dyn && b.IsDyn)).ToList();
            Func<PeqBand, PeqSpec> spec = b => new PeqSpec(b.Type, b.Hz, b.Db, b.QNow);
            var s = new PeqSet(use.Where(b => !ms || b.Mode == 0).Select(spec), null, null,
                               ms ? use.Where(b => b.Mode == 1).Select(spec) : null, ms ? use.Where(b => b.Mode == 2).Select(spec) : null, 0f);
            return s.IsFlat ? null : s;
        }

        /// <summary>Mehrere zusammenlegen (Ausgang: eigener EQ + Kopfhoerer-Profil + Hoer-EQ).</summary>
        public static PeqSet Combine(params PeqSet[] sets)
        {
            var list = sets.Where(s => s != null && !s.IsFlat).ToList();
            if (list.Count == 0) return null;
            if (list.Count == 1) return list[0];
            return new PeqSet(list.SelectMany(s => s.Both), list.SelectMany(s => s.Left), list.SelectMany(s => s.Right), list.SelectMany(s => s.Mid), list.SelectMany(s => s.Side), list.Sum(s => s.PreampDb));
        }

        public static void Configure(Biquad b, PeqSpec s, int rate)
        {
            double f = Math.Min(s.Hz, rate * 0.45);
            switch (s.Type)
            {
                case 1: b.LowShelfQ(rate, f, s.Db, s.Q); break;
                case 2: b.HighShelfQ(rate, f, s.Db, s.Q); break;
                case 3: b.HighPass(rate, f, s.Q); break;
                case 4: b.LowPass(rate, f, s.Q); break;
                default: b.Peaking(rate, f, s.Db, s.Q); break;
            }
        }

        /// <summary>Gesamtwirkung in dB bei Frequenz f (ear 0 links, 1 rechts) – fuer die Kurve.</summary>
        public double MagnitudeDb(double f, int ear, int rate = 48000)
        {
            double db = PreampDb;
            var b = new Biquad();
            foreach (var s in Both) { Configure(b, s, rate); db += 20 * Math.Log10(Math.Max(1e-9, b.MagnitudeAt(rate, f))); }
            foreach (var s in ear == 0 ? Left : Right) { Configure(b, s, rate); db += 20 * Math.Log10(Math.Max(1e-9, b.MagnitudeAt(rate, f))); }
            foreach (var s in Mid) { Configure(b, s, rate); db += 20 * Math.Log10(Math.Max(1e-9, b.MagnitudeAt(rate, f))); }   // Anzeige: Mitte wie beide Ohren
            return db;
        }

        /// <summary>Wirkung eines einzelnen Bandes in dB (fuer die Kurve im EQ-Fenster).</summary>
        public static double BandDb(PeqSpec s, double f, int rate = 48000)
        {
            var b = new Biquad();
            Configure(b, s, rate);
            return 20 * Math.Log10(Math.Max(1e-9, b.MagnitudeAt(rate, f)));
        }
    }

    /// <summary>4.0: Zustand des parametrischen EQ; Aenderungen werden in 30 ms uebergeblendet (kein Knacken).</summary>
    internal sealed class PeqState
    {
        private readonly int _rate;
        private PeqSet _p;
        private Biquad[] _both = new Biquad[0], _l = new Biquad[0], _r = new Biquad[0], _m = new Biquad[0], _s = new Biquad[0];
        private Biquad[] _oBoth, _oL, _oR, _oM, _oS;
        private float _oGain, _gain = 1f;
        private bool _oDry;          // alt = ohne EQ
        private int _fade;
        private float[] _old = new float[0];
        public const int FadeFrames = 1440;

        public PeqState(int rate) { _rate = rate; }

        public bool Active => _p != null || _fade > 0;

        public void Process(float[] buf, int n, PeqSet p)
        {
            if (p != null && p.IsFlat) p = null;
            if (!ReferenceEquals(p, _p) && !(p != null && _p != null && p.SameAs(_p)))
            {
                // alten Zustand behalten und ueberblenden
                _oDry = _p == null;
                _oBoth = Clone(_both); _oL = Clone(_l); _oR = Clone(_r); _oM = Clone(_m); _oS = Clone(_s); _oGain = _gain;
                _fade = FadeFrames;
                if (p != null)
                {
                    _both = Rebuild(_both, p.Both);
                    _l = Rebuild(_l, p.Left);
                    _r = Rebuild(_r, p.Right);
                    _m = Rebuild(_m, p.Mid);
                    _s = Rebuild(_s, p.Side);
                    _gain = (float)Math.Pow(10, p.PreampDb / 20.0);
                }
                _p = p;
            }
            else if (p != null && !ReferenceEquals(p, _p)) _p = p;
            if (_p == null && _fade <= 0) return;

            if (_fade > 0)
            {
                if (_old.Length < n * 2) _old = new float[n * 2];
                Array.Copy(buf, _old, n * 2);
                if (!_oDry) Run(_old, n, _oBoth, _oL, _oR, _oM, _oS, _oGain);
            }
            if (_p != null) Run(buf, n, _both, _l, _r, _m, _s, _gain);
            if (_fade > 0)
            {
                int left = _fade;
                for (int i = 0, k = 0; i < n; i++, k += 2)
                {
                    float m = left - i <= 0 ? 1f : 1f - (left - i) / (float)FadeFrames;
                    if (_p == null) { /* zum trockenen Ton: buf ist schon trocken */ }
                    buf[k] = _old[k] + (buf[k] - _old[k]) * m;
                    buf[k + 1] = _old[k + 1] + (buf[k + 1] - _old[k + 1]) * m;
                }
                _fade = Math.Max(0, left - n);
                if (_fade == 0) { _oBoth = _oL = _oR = _oM = _oS = null; if (_p == null) { _both = new Biquad[0]; _l = new Biquad[0]; _r = new Biquad[0]; _m = new Biquad[0]; _s = new Biquad[0]; } }
            }
        }

        private static void Run(float[] buf, int n, Biquad[] both, Biquad[] l, Biquad[] r, Biquad[] m, Biquad[] s, float gain)
        {
            foreach (var b in both) b.Process(buf, n);
            foreach (var b in l) b.ProcessChannel(buf, n, 0);
            foreach (var b in r) b.ProcessChannel(buf, n, 1);
            if (m.Length > 0 || s.Length > 0)
            {
                // 4.0 (finale): Mitte/Seite – links = Mitte + Seite, rechts = Mitte − Seite (verlustfrei hin und zurueck)
                for (int k = 0; k < n * 2; k += 2) { float a = buf[k], c = buf[k + 1]; buf[k] = (a + c) * 0.5f; buf[k + 1] = (a - c) * 0.5f; }
                foreach (var b in m) b.ProcessChannel(buf, n, 0);
                foreach (var b in s) b.ProcessChannel(buf, n, 1);
                for (int k = 0; k < n * 2; k += 2) { float a = buf[k], c = buf[k + 1]; buf[k] = a + c; buf[k + 1] = a - c; }
            }
            if (Math.Abs(gain - 1f) > 1e-6f) for (int i = 0; i < n * 2; i++) buf[i] *= gain;
        }

        private Biquad[] Rebuild(Biquad[] cur, PeqSpec[] specs)
        {
            var nb = new Biquad[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                nb[i] = i < cur.Length ? cur[i].Clone() : new Biquad();   // Gedaechtnis gleicher Plaetze behalten
                PeqSet.Configure(nb[i], specs[i], _rate);
            }
            return nb;
        }

        private static Biquad[] Clone(Biquad[] a)
        {
            var c = new Biquad[a.Length];
            for (int i = 0; i < a.Length; i++) c[i] = a[i].Clone();
            return c;
        }
    }

    /// <summary>4.0: EQ und Kompressor einer Gruppe – wirken auf jeden Kanal der Gruppe (unveraenderlich).</summary>
    internal sealed class GroupFx
    {
        public readonly float Low, Mid, High;
        public readonly bool Comp;
        public GroupFx(float low, float mid, float high, bool comp)
        {
            Low = Clamp(low); Mid = Clamp(mid); High = Clamp(high); Comp = comp;
        }
        private static float Clamp(float v) => float.IsNaN(v) ? 0f : Math.Max(-12f, Math.Min(12f, v));
        public bool EqFlat => Math.Abs(Low) < 0.01f && Math.Abs(Mid) < 0.01f && Math.Abs(High) < 0.01f;
        public bool IsOff => EqFlat && !Comp;
        public static bool Same(GroupFx a, GroupFx b)
        {
            bool na = a == null || a.IsOff, nb = b == null || b.IsOff;
            if (na || nb) return na && nb;
            return a.Low == b.Low && a.Mid == b.Mid && a.High == b.High && a.Comp == b.Comp;
        }
    }
}

namespace Mischpult.Audio
{
    /// <summary>
    /// 4.0: Hoer-EQ aus dem Hoertest. Die gemessene Hoerschwelle je Frequenz wird um die normale Hoerschwelle des
    /// Menschen bereinigt (Terhardt), dann hebt der EQ Frequenzen an, die schlechter gehoert werden als die besten –
    /// nur die Haelfte des Unterschieds (Standard), hoechstens +10 dB, pro Ohr getrennt.
    /// </summary>
    internal static class HearingEq
    {
        /// <summary>Normale Hoerschwelle in dB SPL (Naeherung nach Terhardt).</summary>
        public static double Ath(double f)
        {
            double k = Math.Max(0.02, f / 1000.0);
            return 3.64 * Math.Pow(k, -0.8) - 6.5 * Math.Exp(-0.6 * (k - 3.3) * (k - 3.3)) + 1e-3 * Math.Pow(k, 4);
        }

        public static readonly string[] StrengthNames = { "Mittel (Hälfte)", "Leicht (Drittel)", "Stark (zwei Drittel)" };
        public static float StrengthFactor(int s) => s == 1 ? 0.33f : s == 2 ? 0.67f : 0.5f;

        /// <summary>Anhebung je Frequenz (dB, 0..10).</summary>
        public static float[] Gains(float[] thr, int strength)
        {
            var f = HearProfile.Freqs;
            var g = new float[f.Length];
            if (thr == null || thr.Length != f.Length) return g;
            var n = new double[f.Length];
            for (int i = 0; i < f.Length; i++) n[i] = thr[i] - Ath(f[i]);
            double best = n.OrderBy(x => x).Take(3).Average();
            float k = StrengthFactor(strength);
            for (int i = 0; i < f.Length; i++)
            {
                double v = Math.Max(0, Math.Min(10, (n[i] - best) * k));
                g[i] = v < 1 ? 0f : (float)(Math.Round(v * 2) / 2);
            }
            return g;
        }

        private static PeqSpec SpecFor(int i, float db)
        {
            var f = HearProfile.Freqs;
            if (i == 0) return new PeqSpec(1, 180f, db, 0.7f);
            if (i == f.Length - 1) return new PeqSpec(2, 10000f, db, 0.7f);
            return new PeqSpec(0, f[i], db, 1.4f);
        }

        public static PeqSet Build(HearProfile h)
        {
            if (h == null || !h.Valid) return null;
            var gl = Gains(h.Left, h.Strength);
            var gr = Gains(h.Right, h.Strength);
            var l = new List<PeqSpec>(); var r = new List<PeqSpec>();
            for (int i = 0; i < gl.Length; i++)
            {
                if (gl[i] > 0) l.Add(SpecFor(i, gl[i]));
                if (gr[i] > 0) r.Add(SpecFor(i, gr[i]));
            }
            float max = Math.Max(gl.Length == 0 ? 0 : gl.Max(), gr.Length == 0 ? 0 : gr.Max());
            var s = new PeqSet(null, l, r, -0.6f * max);
            return s.IsFlat ? null : s;
        }

        /// <summary>Schnelltest: fehlende Frequenzen aus den Nachbarn (NaN = nicht gemessen).</summary>
        public static float[] FillGaps(float[] thr)
        {
            var r = (float[])thr.Clone();
            for (int i = 0; i < r.Length; i++)
            {
                if (!float.IsNaN(r[i])) continue;
                int a = i - 1; while (a >= 0 && float.IsNaN(thr[a])) a--;
                int b = i + 1; while (b < r.Length && float.IsNaN(thr[b])) b++;
                r[i] = a < 0 && b >= r.Length ? -60f : a < 0 ? thr[b] : b >= r.Length ? thr[a] : (thr[a] + thr[b]) / 2f;
            }
            return r;
        }

        /// <summary>Kurze Ton-Folge (3 Pieptoene) auf einem Ohr fuer den Hoertest. Pegel = Amplitude 1 (Lautstaerke ueber gain).</summary>
        public static SoundClip Pulses(double hz, int ear)
        {
            const int rate = DspMixer.Rate;
            int tone = rate * 220 / 1000, gap = rate * 160 / 1000, ramp = rate * 15 / 1000;
            int frames = 3 * tone + 2 * gap;
            var data = new float[frames * 2];
            for (int p = 0; p < 3; p++)
            {
                int start = p * (tone + gap);
                for (int i = 0; i < tone; i++)
                {
                    double env = Math.Min(1.0, Math.Min(i / (double)ramp, (tone - i) / (double)ramp));
                    env = env * env * (3 - 2 * env);
                    data[(start + i) * 2 + ear] = (float)(Math.Sin(2 * Math.PI * hz * i / rate) * env);
                }
            }
            return new SoundClip { Name = "Hörtest", Data = data, Frames = frames };
        }
    }
}
