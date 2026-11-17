using System;
using System.Diagnostics;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.15.3: misst, wie viel Rechenzeit das Pult pro Bild braucht (Takt = Pegel/Automatik berechnen, Zeichnen = Zuege,
    /// Meterbridge, Kopfleiste) – steht in OPTIONEN → Allgemein → Leistung. Nur Zaehler, kostet praktisch nichts.
    /// 3.0.2: dazu Abstand der Bilder (gleichmaessig?), zu spaete Bilder, laengste Bildzeit und wie oft neue Pegel kamen –
    /// fuer die Bild-Statistik und "Bildrate gleichmaessig halten".
    /// </summary>
    internal static class FrameStats
    {
        private static long _tick, _paint, _start = Stopwatch.GetTimestamp();
        private static int _frames;

        /// <summary>Mittelwerte der letzten Sekunde.</summary>
        public static double TickMs { get; private set; }
        public static double PaintMs { get; private set; }
        public static double Fps { get; private set; }
        public static bool HasData { get; private set; }

        // ---- 3.0.2: je Bild
        /// <summary>Geplanter Abstand zweier Bilder (ms) – setzt das Pult, wenn sich die Bildrate aendert.</summary>
        public static double TargetMs = 1000.0 / 60.0;
        private static long _frameStart, _curTick, _curPaint;
        private static int _late, _over, _intervals, _work;
        private static double _ivSum, _ivSq, _ivMin = double.MaxValue, _ivMax, _workSum, _workMax;
        private static int _data, _dataFrames;
        private static long _lastData;
        private static double _gapMax;

        /// <summary>Werte der letzten vollen Sekunde (3.0.2).</summary>
        public static double IntervalMs { get; private set; }
        public static double IntervalJitter { get; private set; }
        public static double IntervalMin { get; private set; }
        public static double IntervalMax { get; private set; }
        /// <summary>Bilder, die mehr als 1,5 Bildabstaende nach dem vorigen kamen.</summary>
        public static int LateFrames { get; private set; }
        /// <summary>Bilder, deren Rechen- und Zeichenzeit mehr als 85 % des Bildabstands brauchte.</summary>
        public static int OverFrames { get; private set; }
        public static int Frames { get; private set; }
        public static double WorkAvgMs { get; private set; }
        public static double WorkMaxMs { get; private set; }
        /// <summary>Wie oft pro Sekunde neue Pegel von der Engine kamen.</summary>
        public static double DataPerSec { get; private set; }
        /// <summary>Laengste Zeit ohne neue Pegel (ms).</summary>
        public static double DataGapMs { get; private set; }
        /// <summary>Zaehlt jede fertige Sekunde hoch.</summary>
        public static int WindowSeq { get; private set; }
        /// <summary>Insgesamt zu spaete Bilder seit dem Start (bzw. seit Reset).</summary>
        public static long LateTotal { get; private set; }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public static void AddPaint(long ticks) { if (ticks > 0) { _paint += ticks; _curPaint += ticks; } }

        /// <summary>3.0.2: ein neues Bild beginnt (vor dem Takt) – das vorige ist damit fertig.</summary>
        public static void BeginFrame()
        {
            long now = Stopwatch.GetTimestamp();
            if (_frameStart != 0)
            {
                double iv = Ms(now - _frameStart);
                double work = Ms(_curTick + _curPaint);
                if (iv < 1000)
                {
                    _intervals++;
                    _ivSum += iv; _ivSq += iv * iv;
                    if (iv < _ivMin) _ivMin = iv;
                    if (iv > _ivMax) _ivMax = iv;
                    if (iv > TargetMs * 1.5) { _late++; LateTotal++; }
                }
                _work++;
                _workSum += work;
                if (work > _workMax) _workMax = work;
                if (work > TargetMs * 0.85) _over++;
            }
            _frameStart = now;
            _curTick = 0; _curPaint = 0;
        }

        /// <summary>3.0.2: pro Bild – kamen neue Pegel (neuer Schnappschuss der Engine)?</summary>
        public static void AddData(bool fresh)
        {
            long now = Stopwatch.GetTimestamp();
            _dataFrames++;
            if (!fresh) return;
            _data++;
            if (_lastData != 0)
            {
                double gap = Ms(now - _lastData);
                if (gap < 2000 && gap > _gapMax) _gapMax = gap;
            }
            _lastData = now;
        }

        /// <summary>Ein Takt ist durch (ticks = seine Dauer).</summary>
        public static void AddTick(long ticks)
        {
            if (ticks > 0) { _tick += ticks; _curTick += ticks; }
            _frames++;
            long now = Stopwatch.GetTimestamp();
            double sec = (now - _start) / (double)Stopwatch.Frequency;
            if (sec < 1.0) return;
            double perMs = 1000.0 / Stopwatch.Frequency;
            int f = Math.Max(1, _frames);
            TickMs = _tick * perMs / f;
            PaintMs = _paint * perMs / f;
            Fps = _frames / sec;
            HasData = true;
            _tick = _paint = 0; _frames = 0; _start = now;

            int n = Math.Max(1, _intervals);
            IntervalMs = _ivSum / n;
            IntervalJitter = Math.Sqrt(Math.Max(0, _ivSq / n - IntervalMs * IntervalMs));
            IntervalMin = _intervals > 0 ? _ivMin : 0;
            IntervalMax = _ivMax;
            LateFrames = _late;
            OverFrames = _over;
            Frames = _work;
            WorkAvgMs = _workSum / Math.Max(1, _work);
            WorkMaxMs = _workMax;
            DataPerSec = _data / sec;
            DataGapMs = _gapMax;
            _intervals = _late = _over = _work = 0;
            _ivSum = _ivSq = _ivMax = _workSum = _workMax = 0; _ivMin = double.MaxValue;
            _data = _dataFrames = 0; _gapMax = 0;
            WindowSeq++;
        }

        /// <summary>z. B. "1,4 ms pro Bild (Takt 0,5 · Zeichnen 0,9)".</summary>
        public static string Text()
        {
            if (!HasData) return null;
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            return (TickMs + PaintMs).ToString("0.0", de) + " ms pro Bild (Takt " + TickMs.ToString("0.0", de) + " · Zeichnen " + PaintMs.ToString("0.0", de) + ")";
        }

        /// <summary>3.0.2: Bilder gleichmaessig? z. B. "Abstand 16,7 ms ± 0,3 · 0 zu spät".</summary>
        public static string PacingText()
        {
            if (!HasData || Frames == 0) return null;
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            return "Abstand " + IntervalMs.ToString("0.0", de) + " ms ± " + IntervalJitter.ToString("0.0", de) + " · " + LateFrames + " zu spät";
        }

        /// <summary>Nur fuer Tests: alles zuruecksetzen.</summary>
        internal static void Reset()
        {
            _tick = _paint = 0; _frames = 0; _start = Stopwatch.GetTimestamp();
            _frameStart = 0; _curTick = _curPaint = 0;
            _intervals = _late = _over = _work = 0;
            _ivSum = _ivSq = _ivMax = _workSum = _workMax = 0; _ivMin = double.MaxValue;
            _data = _dataFrames = 0; _gapMax = 0; _lastData = 0;
            HasData = false; LateTotal = 0;
        }
    }
}
