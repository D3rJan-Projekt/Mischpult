using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4: Auslastung von Prozessor und Grafikkarte (0..100 %), einmal pro Sekunde in einem eigenen Faden gemessen –
    /// nur solange jemand sie anzeigt. NaN = nicht verfuegbar.
    /// </summary>
    internal static class PcLoad
    {
        public static volatile float Cpu = float.NaN, Gpu = float.NaN;
        public static bool Demo;
        private static Thread _thread;
        private static volatile bool _run;
        private static long _lastIdle, _lastKernel, _lastUser;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low, High; public long Value => ((long)High << 32) | Low; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        public static void Start()
        {
            if (_run) return;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PC-Auslastung", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public static void Stop() { _run = false; }

        private static void Loop()
        {
            var gpuCounters = new List<PerformanceCounter>();
            long nextScan = 0;
            bool gpuBroken = false;
            var sw = Stopwatch.StartNew();
            while (_run)
            {
                try
                {
                    if (Demo)
                    {
                        double t = sw.Elapsed.TotalSeconds;
                        Cpu = (float)(28 + 18 * Math.Sin(t * 0.7) + 8 * Math.Sin(t * 2.3));
                        Gpu = (float)(55 + 25 * Math.Sin(t * 0.45 + 1));
                    }
                    else
                    {
                        Cpu = ReadCpu();
                        if (!gpuBroken)
                        {
                            try
                            {
                                if (sw.ElapsedMilliseconds >= nextScan)
                                {
                                    nextScan = sw.ElapsedMilliseconds + 10000;
                                    foreach (var c in gpuCounters) c.Dispose();
                                    gpuCounters.Clear();
                                    var cat = new PerformanceCounterCategory("GPU Engine");
                                    foreach (var inst in cat.GetInstanceNames())
                                        if (inst.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0)
                                            gpuCounters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true));
                                    foreach (var c in gpuCounters) { try { c.NextValue(); } catch { } }   // erster Wert ist immer 0
                                }
                                float sum = 0f;
                                foreach (var c in gpuCounters) { try { sum += c.NextValue(); } catch { } }
                                Gpu = gpuCounters.Count == 0 ? float.NaN : Math.Min(100f, sum);
                            }
                            catch { gpuBroken = true; Gpu = float.NaN; }
                        }
                    }
                }
                catch { }
                Thread.Sleep(Demo ? 120 : 1000);
            }
            foreach (var c in gpuCounters) { try { c.Dispose(); } catch { } }
        }

        private static float ReadCpu()
        {
            try
            {
                if (!GetSystemTimes(out var i, out var k, out var u)) return float.NaN;
                long idle = i.Value, kernel = k.Value, user = u.Value;
                long di = idle - _lastIdle, dk = kernel - _lastKernel, du = user - _lastUser;
                bool first = _lastKernel == 0;
                _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
                long total = dk + du;   // Kernel enthaelt die Leerlaufzeit
                if (first || total <= 0) return float.NaN;
                return Math.Max(0f, Math.Min(100f, (float)(100.0 * (total - di) / total)));
            }
            catch { return float.NaN; }
        }
    }
}
