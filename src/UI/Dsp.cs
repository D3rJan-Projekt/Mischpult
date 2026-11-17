using System;

namespace Mischpult.UI
{
    /// <summary>Fader-Kennlinie wie an einem Mischpult: oben 0 dB, darunter logarithmisch abgestuft.</summary>
    internal static class FaderLaw
    {
        private static readonly float[] Db  = { -90f, -60f, -40f, -30f, -20f, -10f, -5f, 0f };
        /// <summary>3.2: Fader-Kurven – 0 Standard, 1 fein oben (mehr Weg im lauten Bereich), 2 gleichmaessig in dB, 3 fein unten.</summary>
        private static readonly float[][] Curves =
        {
            new[] { 0.00f, 0.07f, 0.19f, 0.33f, 0.50f, 0.68f, 0.84f, 1.00f },
            new[] { 0.00f, 0.05f, 0.12f, 0.21f, 0.34f, 0.54f, 0.76f, 1.00f },
            new[] { 0.00f, 0.06f, 0.373f, 0.53f, 0.687f, 0.843f, 0.922f, 1.00f },
            new[] { 0.00f, 0.12f, 0.30f, 0.45f, 0.61f, 0.77f, 0.89f, 1.00f },
        };
        public static readonly string[] CurveNames = { "Standard", "Fein oben", "Gleichmäßig", "Fein unten" };
        private static int _curve;
        /// <summary>Gewaehlte Kurve (Einstellungen → Ton). Nur ueber SetCurve aendern, damit die Faderstellungen mitgehen.</summary>
        public static int Curve => _curve;
        private static float[] Pos = Curves[0];

        /// <summary>Kurve setzen (ohne die gespeicherten Stellungen umzurechnen – dafur Convert).</summary>
        public static void SetCurve(int c)
        {
            _curve = Math.Max(0, Math.Min(Curves.Length - 1, c));
            Pos = Curves[_curve];
        }

        /// <summary>Faderstellung von einer Kurve in eine andere umrechnen, sodass die Lautstaerke gleich bleibt.</summary>
        public static float Convert(float p, int from, int to)
        {
            if (from == to) return p;
            int old = _curve;
            SetCurve(from);
            float db = PosToDb(p);
            SetCurve(to);
            float np = float.IsNegativeInfinity(db) ? (p <= MinPos ? p : 0f) : DbToPos(db);
            SetCurve(old);
            return np;
        }

        public const float MinPos = 0.004f;

        public static float PosToDb(float p)
        {
            if (p <= MinPos) return float.NegativeInfinity;
            if (p >= 1f) return 0f;
            for (int i = 1; i < Pos.Length; i++)
            {
                if (p <= Pos[i])
                {
                    float t = (p - Pos[i - 1]) / (Pos[i] - Pos[i - 1]);
                    return Db[i - 1] + t * (Db[i] - Db[i - 1]);
                }
            }
            return 0f;
        }

        public static float DbToPos(float db)
        {
            if (float.IsNegativeInfinity(db) || db <= Db[0]) return 0f;
            if (db >= 0f) return 1f;
            for (int i = 1; i < Db.Length; i++)
            {
                if (db <= Db[i])
                {
                    float t = (db - Db[i - 1]) / (Db[i] - Db[i - 1]);
                    return Pos[i - 1] + t * (Pos[i] - Pos[i - 1]);
                }
            }
            return 1f;
        }

        public static float PosToGain(float p)
        {
            float db = PosToDb(p);
            return float.IsNegativeInfinity(db) ? 0f : (float)Math.Pow(10.0, db / 20.0);
        }

        public static string FormatDb(float p)
        {
            float db = PosToDb(p);
            if (float.IsNegativeInfinity(db)) return "-∞ dB";
            if (db > -0.05f) return "0.0 dB";
            return db.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
        }
    }

    /// <summary>
    /// Pegelanzeige mit Abfallzeit, Peak-Hold und Clip-LED (IEC-Skala). Seit 2.14 einstellbar (fuer alle Pegel gleich):
    /// Ruecklauf, wie lange die Spitze stehen bleibt, und ob Spitze, Mittelwert oder beides gezeigt wird.
    /// </summary>
    internal sealed class MeterBallistics
    {
        private float _dbL = -100f, _dbR = -100f, _holdL = -100f, _holdR = -100f;
        private float _holdTL, _holdTR, _clipT;
        // Mittelwert: geglaettete Leistung (wie ein VU-Meter, ca. 300 ms) und was davon gezeigt wird
        private double _powL, _powR;
        private float _avgL = -100f, _avgR = -100f;

        public float FracL { get; private set; }
        public float FracR { get; private set; }
        public float HoldFracL { get; private set; }
        public float HoldFracR { get; private set; }
        /// <summary>Nur bei "beides": Spitze (heller Bereich ueber dem Mittelwert), sonst 0.</summary>
        public float PeakFracL { get; private set; }
        public float PeakFracR { get; private set; }
        public bool Clip => _clipT > 0;
        /// <summary>Spitzenwert (gehalten) in dBFS, lauterer Kanal.</summary>
        public float HoldDb => Math.Max(_holdL, _holdR);

        // ---- Einstellungen (OPTIONEN → Leuchten & Anzeigen → Pegel)
        public static readonly string[] FallNames = { "schnell", "normal", "langsam", "sofort (Echtzeit)" };
        private static readonly float[] FallDb = { 45f, 26f, 12f, 90f };
        public static readonly string[] HoldNames = { "aus", "kurz", "normal", "lang", "bis Klick" };
        private static readonly float[] HoldSec = { 1.3f, 0.6f, 1.3f, 4f, float.PositiveInfinity };
        public static readonly string[] ModeNames = { "Spitze", "Mittelwert", "Beides" };

        /// <summary>Ruecklauf: 0 schnell, 1 normal, 2 langsam, 3 sofort (folgt dem Ton fast ohne Nachlauf).</summary>
        public static int Fall = 1;
        /// <summary>Spitze halten: 0 aus, 1 kurz, 2 normal, 3 lang, 4 bis Klick.</summary>
        public static int Hold = 2;
        /// <summary>Was der Balken zeigt: 0 Spitze, 1 Mittelwert, 2 Mittelwert mit Spitze darueber.</summary>
        public static int Mode = 0;
        // ---- 2.15.2: MASTER und Ausgaenge koennen eigene Einstellungen haben (-1 = wie die Kanaele)
        public static int OutFall = -1, OutHold = -1, OutMode = -1;
        /// <summary>Dieser Pegel gehoert zum MASTER bzw. einem Ausgang (nimmt dann dessen eigene Einstellung).</summary>
        public bool Output;
        private int EffFall => Output && OutFall >= 0 ? OutFall : Fall;
        private int EffHold => Output && OutHold >= 0 ? OutHold : Hold;
        /// <summary>Was dieser Pegel zeigt (0 Spitze, 1 Mittelwert, 2 beides).</summary>
        public int EffMode => Output && OutMode >= 0 ? OutMode : Mode;

        /// <summary>Haelt irgendein Pegel die Spitze, bis geklickt wird?</summary>
        public static bool HoldsUntilClick => Hold == 4 || OutHold == 4;
        /// <summary>Haelt dieser Pegel die Spitze, bis geklickt wird?</summary>
        public bool HoldsUntilClickHere => EffHold == 4;

        private float ReleaseDbPerSec => FallDb[Math.Max(0, Math.Min(FallDb.Length - 1, EffFall))];
        private float HoldSeconds => HoldSec[Math.Max(0, Math.Min(HoldSec.Length - 1, EffHold))];
        private const float HoldFallDbPerSec = 22f;
        private const double AvgTau = 0.3;

        /// <summary>3.0.2: Pegel steigen weich (ca. 12 ms) statt in einem Bild zu springen – gleicher Look, nur flüssiger.</summary>
        public static bool SmoothRise = true;
        public const float RiseTau = 0.012f;

        public void Update(float linL, float linR, float dt)
        {
            if (dt > 0.2f) dt = 0.2f;
            if (dt < 0f) dt = 0f;
            float rel = ReleaseDbPerSec;
            float inL = ToDb(linL), inR = ToDb(linR);
            // Spitze
            if (SmoothRise && dt > 0f)
            {
                float ka = 1f - (float)Math.Exp(-dt / RiseTau);
                _dbL = Rise(_dbL, inL, rel * dt, ka);
                _dbR = Rise(_dbR, inR, rel * dt, ka);
            }
            else
            {
                _dbL = Math.Max(inL, _dbL - rel * dt);
                _dbR = Math.Max(inR, _dbR - rel * dt);
            }
            // Mittelwert (Leistung, -3 dB: Sinus mit Spitze 0 dBFS = -3 dB)
            double k = 1.0 - Math.Exp(-dt / AvgTau);
            _powL += ((double)linL * linL - _powL) * k;
            _powR += ((double)linR * linR - _powR) * k;
            float aL = _powL > 1e-10 ? (float)(10.0 * Math.Log10(_powL)) - 3.01f : -100f;
            float aR = _powR > 1e-10 ? (float)(10.0 * Math.Log10(_powR)) - 3.01f : -100f;
            _avgL = Math.Max(aL, _avgL - rel * dt);
            _avgR = Math.Max(aR, _avgR - rel * dt);

            int mode = EffMode;
            float showL = mode == 0 ? _dbL : _avgL, showR = mode == 0 ? _dbR : _avgR;
            // die Spitze haelt bei "Mittelwert" den Balken fest, sonst die echte Spitze (auch beim weichen Anstieg sofort)
            float holdInL = mode == 1 ? _avgL : Math.Max(_dbL, inL), holdInR = mode == 1 ? _avgR : Math.Max(_dbR, inR);
            StepHold(ref _holdL, ref _holdTL, holdInL, showL, dt);
            StepHold(ref _holdR, ref _holdTR, holdInR, showR, dt);
            if (linL >= 0.998f || linR >= 0.998f) _clipT = 2.5f; else if (_clipT > 0) _clipT -= dt;
            FracL = Iec(showL); FracR = Iec(showR);
            PeakFracL = mode == 2 ? Iec(_dbL) : 0f;
            PeakFracR = mode == 2 ? Iec(_dbR) : 0f;
            bool marker = EffHold != 0;
            HoldFracL = marker ? Iec(_holdL) : 0f;
            HoldFracR = marker ? Iec(_holdR) : 0f;
        }

        /// <summary>Weich zum hoeheren Wert (im Pegel, nicht in dB – wie ein echter Spitzenspannungsmesser), sonst normal zuruecklaufen.</summary>
        private static float Rise(float cur, float target, float fall, float ka)
        {
            if (target > cur)
            {
                double c = cur <= -99.9f ? 0.0 : Math.Pow(10.0, cur / 20.0), t = Math.Pow(10.0, target / 20.0);
                double v = c + (t - c) * ka;
                float db = v <= 0.00001 ? -100f : (float)(20.0 * Math.Log10(v));
                return Math.Min(target, Math.Max(cur, db));
            }
            return Math.Max(target, cur - fall);
        }

        private void StepHold(ref float hold, ref float holdT, float input, float floor, float dt)
        {
            if (input >= hold) { hold = input; holdT = HoldSeconds; }
            else if (holdT > 0) holdT -= dt;
            else hold = Math.Max(Math.Max(floor, input), hold - HoldFallDbPerSec * dt);
        }

        /// <summary>Gehaltene Spitze und Clip loeschen (Klick auf den Pegel bei "bis Klick").</summary>
        public void ResetHold()
        {
            _holdL = _dbL; _holdR = _dbR; _holdTL = _holdTR = 0f; _clipT = 0f;
            if (EffMode == 1) { _holdL = _avgL; _holdR = _avgR; }
        }

        public static float ToDb(float lin) => lin <= 0.00001f ? -100f : (float)(20.0 * Math.Log10(lin));

        /// <summary>IEC 60268-18 Skala: dB -> 0..1</summary>
        public static float Iec(float db)
        {
            float d;
            if (db < -70f) d = 0f;
            else if (db < -60f) d = (db + 70f) * 0.25f;
            else if (db < -50f) d = (db + 60f) * 0.5f + 2.5f;
            else if (db < -40f) d = (db + 50f) * 0.75f + 7.5f;
            else if (db < -30f) d = (db + 40f) * 1.5f + 15f;
            else if (db < -20f) d = (db + 30f) * 2f + 30f;
            else if (db < 0f) d = (db + 20f) * 2.5f + 50f;
            else d = 100f;
            return d / 100f;
        }
    }
}

namespace Mischpult.UI
{
    /// <summary>
    /// Erkennt, ob ein Kanal (z. B. Discord) nur den restlichen PC-Ton mitspiegelt: Sein Pegel folgt dann dem
    /// Pegel der anderen Kanaele mit festem Abstand. Echte Stimmen sind davon unabhaengig.
    /// </summary>
    internal sealed class LeakDetector
    {
        private readonly float[] _t, _o;
        private int _count, _pos;
        public bool Present { get; private set; }
        public float Offset { get; private set; } = float.NaN;

        public LeakDetector(int size = 480) { _t = new float[size]; _o = new float[size]; }

        public void Reset() { _count = 0; _pos = 0; Present = false; Offset = float.NaN; _votes = 0; }

        private int _votes; // Bitmuster der letzten 8 Entscheidungen

        /// <summary>
        /// Echte Stimme im Ausloeser? Liegt der Pegel ueber der Schwelle und – falls eine Spiegelung erkannt
        /// wurde – deutlich ueber dem gespiegelten Ton, in mindestens 4 der letzten 8 Messungen.
        /// </summary>
        public bool IsVoice(float trigger, float others, float threshold, bool filter)
        {
            bool v = trigger > threshold;
            if (v && filter && Present && others > -70f) v = trigger - (others + Offset) > 8f;
            _votes = ((_votes << 1) | (v ? 1 : 0)) & 0xFF;
            if (!(filter && Present)) return v;
            int c = 0;
            for (int x = _votes; x != 0; x >>= 1) c += x & 1;
            return c >= 4;
        }

        public void Add(float trigger, float others)
        {
            if (others <= -60f || trigger <= -90f) return; // nur wenn beide Ton haben
            _t[_pos] = trigger; _o[_pos] = others;
            _pos = (_pos + 1) % _t.Length;
            if (_count < _t.Length) _count++;
        }

        public void Evaluate()
        {
            if (_count < 90) { Present = false; Offset = float.NaN; return; }
            var d = new float[_count];
            for (int i = 0; i < _count; i++) d[i] = _t[i] - _o[i];
            var sorted = (float[])d.Clone();
            Array.Sort(sorted);
            float p25 = sorted[_count / 4], p50 = sorted[_count / 2];

            // Korrelation nur ueber die "leise" Haelfte (dort dominiert eine Spiegelung, Stimmen liegen darueber)
            double st = 0, so = 0, stt = 0, soo = 0, sto = 0; int n = 0;
            for (int i = 0; i < _count; i++)
            {
                if (d[i] > p50) continue;
                double t = _t[i], o = _o[i];
                st += t; so += o; stt += t * t; soo += o * o; sto += t * o; n++;
            }
            if (n < 40) { Present = false; return; }
            double vt = stt / n - (st / n) * (st / n), vo = soo / n - (so / n) * (so / n);
            if (vo < 2.0 || vt < 1e-6) return; // zu wenig Bewegung im uebrigen Ton -> letzte Bewertung behalten
            double corr = (sto / n - (st / n) * (so / n)) / Math.Sqrt(vt * vo);

            int near = 0;
            foreach (var x in d) if (Math.Abs(x - p25) < 4f) near++;
            Present = corr > 0.75 && near >= _count * 0.35f && p25 > -45f;
            Offset = p25;
        }
    }
}
