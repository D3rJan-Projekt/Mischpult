using System;

namespace Mischpult.Audio
{
    /// <summary>
    /// Weiche Ueberblendung der hoerbaren Lautstaerke einer App (linear, 40–160 ms je nach Sprung).
    /// Stummschalten = auf 0 ausblenden, erst dann wirklich stumm.
    /// </summary>
    internal sealed class VolumeFade
    {
        public float Current { get; private set; }
        private float _from, _goal;
        private long _start;
        private float _ms;

        /// <summary>Sofort auf den Wert springen (neue App, EQ-Wechsel, weich aus).</summary>
        public void Jump(float value)
        {
            Current = _from = _goal = value;
        }

        /// <summary>Neues Ziel setzen (startet nur bei Aenderung eine neue Blende) und fortschreiben.</summary>
        public float Update(float goal, long nowMs)
        {
            if (goal != _goal)
            {
                _from = Current;
                _goal = goal;
                _start = nowMs;
                _ms = Math.Max(40f, Math.Min(160f, Math.Abs(goal - Current) * 320f));
            }
            if (Current != _goal)
            {
                float t = (nowMs - _start) / _ms;
                Current = t >= 1f ? _goal : _from + (_goal - _from) * Math.Max(0f, t);
            }
            return Current;
        }

        public bool Done => Current == _goal;

        /// <summary>3.4: zum Ziel in genau ms Millisekunden blenden (z. B. Einblenden einer neuen App).</summary>
        public void Ramp(float goal, long nowMs, float ms)
        {
            _from = Current;
            _goal = goal;
            _start = nowMs;
            _ms = Math.Max(1f, ms);
        }
    }

    /// <summary>
    /// Gehoerschutz fuer eine App auf dem direkten Weg (Regelung ueber die Windows-Lautstaerke):
    /// Ueberschreitet der Pegel die Grenze, sofort um den Ueberschuss absenken, nach 400 ms Ruhe
    /// mit 8 dB/s wieder freigeben – aber nie ueber die Grenze hinaus.
    /// Normalerweise enthaelt die Messung die eigene Absenkung. Zeigt sich nach zwei Absenkungen keine Wirkung
    /// in der Messung, misst Windows offenbar davor – dann wird ohne Rueckrechnung geregelt.
    /// </summary>
    internal sealed class CapLimiter
    {
        public const float MinGain = 0.01f;   // hoechstens -40 dB
        public float Gain { get; private set; } = 1f;
        public bool PreVolume { get; private set; }
        private float _oldGain = 1f, _anchorLevel = 1f, _anchorGain = 1f;
        private long _holdUntil, _changedAt = -1000, _checkAt = long.MaxValue;
        private int _preVotes;

        private static float Db(float v) => 20f * (float)Math.Log10(Math.Max(v, 1e-6f));

        /// <param name="measured">gemessener Spitzenpegel der App</param>
        /// <param name="limit">Grenze (linear)</param>
        public float Update(float measured, float limit, long nowMs, float dt)
        {
            // Ist die letzte Absenkung in der Messung angekommen?
            if (nowMs >= _checkAt)
            {
                _checkAt = long.MaxValue;
                float want = Db(_anchorGain) - Db(Gain);       // so viel haette die Messung sinken muessen
                float got = Db(_anchorLevel) - Db(measured);    // so viel ist sie gesunken
                if (want >= 3f)
                {
                    if (got < want * 0.3f) _preVotes++;
                    else if (got > want * 0.6f) _preVotes = 0;
                    PreVolume = _preVotes >= 2;
                }
            }

            // Die Messung hinkt der Absenkung etwas hinterher -> kurz nach einer Aenderung mit dem alten Wert rechnen
            float g = PreVolume ? 1f : Math.Max(nowMs - _changedAt < 80 ? Math.Max(Gain, _oldGain) : Gain, 0.001f);
            float raw = measured / g;                          // Pegel ohne Gehoerschutz
            float level = PreVolume ? raw * Gain : measured;   // hoerbarer Pegel
            if (level > limit)
            {
                bool pending = _checkAt != long.MaxValue;
                _holdUntil = nowMs + 400;
                // Wirkung der letzten Absenkung abwarten – ausser es wird deutlich lauter
                if (pending && !PreVolume && measured < _anchorLevel * 1.19f) return Gain;
                float ng = Math.Max(MinGain, limit / raw * 0.9f);
                if (!PreVolume && _preVotes == 1) ng = Math.Max(ng, Gain * 0.7f); // unsicher: nur vorsichtig weiter
                if (ng < Gain)
                {
                    if (!pending) { _anchorLevel = measured; _anchorGain = PreVolume ? Gain : g; _checkAt = nowMs + 120; }
                    _oldGain = PreVolume ? Gain : g;
                    _changedAt = nowMs;
                    Gain = ng;
                }
            }
            else if (nowMs >= _holdUntil && Gain < 1f)
            {
                float up = Gain * (float)Math.Pow(10, 8.0 * dt / 20.0);
                if (raw > 0 && raw * up > limit) up = limit / raw;
                Gain = Math.Min(1f, Math.Max(Gain, up));
            }
            return Gain;
        }
    }
}
