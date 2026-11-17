using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>
    /// 3.4.1: Rechenzeit pro Faden fuer die Bild-Statistik (Oberflaeche, Ton-Engine, EQ-Mischer, Pegel …). Jeder Faden meldet
    /// sich einmal an; gemessen wird nur, solange die Statistik zu sehen ist (hoechstens einmal pro Sekunde).
    /// </summary>
    internal static class ThreadCpu
    {
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private static readonly object _lock = new object();
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private static readonly Dictionary<int, TimeSpan> _last = new Dictionary<int, TimeSpan>();
        private static TimeSpan _lastTotal;
        private static long _lastAt;
        private static string _text;

        /// <summary>Aus dem Faden selbst aufrufen.</summary>
        public static void Register(string name)
        {
            if (!Win32.IsWindows) return;
            try { lock (_lock) _names[(int)GetCurrentThreadId()] = name; } catch { }
        }

        /// <summary>Text wie "CPU 18 % eines Kerns (Task-Manager 2 %) · Oberfläche 12 · Ton 2 · EQ 3 · Pegel 1" – null, solange noch nichts gemessen ist.</summary>
        public static string Text()
        {
            long now = Stopwatch.GetTimestamp();
            if (_text != null && now - _lastAt < Stopwatch.Frequency) return _text;
            if (!Win32.IsWindows) return null;
            try
            {
                using (var p = Process.GetCurrentProcess())
                {
                    var total = p.TotalProcessorTime;
                    var per = new Dictionary<string, double>();
                    double wall = _lastAt == 0 ? 0 : (now - _lastAt) / (double)Stopwatch.Frequency;
                    lock (_lock)
                    {
                        foreach (ProcessThread t in p.Threads)
                        {
                            try
                            {
                                if (!_names.TryGetValue(t.Id, out string n)) continue;
                                var cpu = t.TotalProcessorTime;
                                if (wall > 0 && _last.TryGetValue(t.Id, out var old))
                                    per[n] = (per.TryGetValue(n, out double v) ? v : 0) + (cpu - old).TotalSeconds / wall * 100.0;
                                _last[t.Id] = cpu;
                            }
                            catch { }
                        }
                    }
                    string res = null;
                    if (wall > 0)
                    {
                        double all = (total - _lastTotal).TotalSeconds / wall * 100.0;
                        var parts = new List<string>();
                        foreach (var n in new[] { "Oberfläche", "Ton", "EQ", "Pegel", "Takt" })
                            if (per.TryGetValue(n, out double v)) parts.Add(n + " " + Math.Round(v).ToString("0"));
                        res = "CPU " + Math.Round(all).ToString("0") + " % eines Kerns (Task-Manager " + Math.Round(all / Math.Max(1, Environment.ProcessorCount)).ToString("0") + " %)"
                              + (parts.Count > 0 ? "\n" + string.Join(" · ", parts) : "");
                    }
                    _lastTotal = total;
                    _lastAt = now;
                    _text = res;
                    return res;
                }
            }
            catch { return null; }
        }
    }
}
