using System;
using System.Collections.Generic;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>
    /// 2.15.1 Schnelle Pegel: Windows meldet den Pegel eines Ausgabegeraets (und des Mikros) immer nur fuer die letzten
    /// ~10 ms. Bisher wurde er im Engine-Takt (alle ~30 ms) einmal abgefragt – dazwischen gingen Spitzen verloren, die
    /// Ausgangs-Pegel wirkten ruckelig und hingen hinterher. Jetzt fragt ein eigener kleiner Faden alle 4 ms ab und merkt
    /// sich die hoechste Spitze, bis die Oberflaeche sie fuers naechste Bild abholt.
    /// </summary>
    internal sealed partial class AudioEngine
    {
        private sealed class FastMeter
        {
            public string Id;                       // Geraete-ID bzw. MicKey
            public IAudioMeterInformation Meter;
            public float[] Buf;
            public float MaxL, MaxR;                // hoechste Spitze seit dem letzten Abholen
            public float LastL, LastR;              // zuletzt abgeholt (falls seitdem nichts gemessen wurde)
            public bool Fresh;
        }

        private readonly object _fastLock = new object();
        private FastMeter[] _fast = new FastMeter[0];   // nur unter _fastLock ersetzen
        private Thread _fastThread;
        private volatile bool _fastWanted;
        private volatile bool _boost;
        private bool _engineBoosted;

        /// <summary>Abfrage-Abstand des schnellen Pegel-Fadens.</summary>
        public const int FastMeterMs = 4;

        /// <summary>
        /// fastLevels: Pegel der Ausgaenge und des Mikros mit eigenem schnellen Faden messen.
        /// boost: Mess- und Engine-Faden bekommen Vorrang (laufen ruhig weiter, auch wenn ein Spiel die CPU auslastet).
        /// </summary>
        public void SetPerf(bool fastLevels, bool boost)
        {
            _boost = boost;
            LoopbackRecorder.Boost = boost;
            _fastWanted = fastLevels;
            if (fastLevels && _fastThread == null && Win32.IsWindows)
            {
                _fastThread = new Thread(FastLoop) { IsBackground = true, Name = "Mischpult Pegel" };
                _fastThread.SetApartmentState(ApartmentState.MTA);   // wie der Engine-Faden: die Mess-Objekte duerfen direkt benutzt werden
                _fastThread.Start();
            }
            _wake.Set();
        }

        /// <summary>Laeuft die schnelle Messung?</summary>
        public bool FastLevelsRunning => _fastWanted && _fastThread != null;

        /// <summary>Aus dem UI-Takt: frische Pegel der Ausgaenge und des Mikros in den Schnappschuss legen.</summary>
        public void RefreshLevels(EngineSnapshot snap)
        {
            if (snap == null || !FastLevelsRunning) return;
            // nie auf den Mess-Faden warten (falls Windows bei einer Messung haengt): dann eben dieses Bild die alten Werte
            if (!Monitor.TryEnter(_fastLock, 1)) return;
            try
            {
                var fast = _fast;
                if (fast.Length == 0) return;
                var devs = snap.Devices;
                if (devs != null)
                    foreach (var d in devs)
                    {
                        var f = FindFast(fast, d.Id);
                        if (f == null) continue;
                        Take(f);
                        d.PeakL = f.LastL; d.PeakR = f.LastR;
                    }
                if (snap.Mic != null)
                {
                    var f = FindFast(fast, MicKey);
                    if (f != null) { Take(f); snap.Mic.PeakL = f.LastL; snap.Mic.PeakR = f.LastR; }
                }
            }
            finally { Monitor.Exit(_fastLock); }
        }

        private static FastMeter FindFast(FastMeter[] fast, string id)
        {
            if (id == null) return null;
            foreach (var f in fast) if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        private static void Take(FastMeter f)
        {
            if (!f.Fresh) return;
            f.LastL = f.MaxL; f.LastR = f.MaxR;
            f.MaxL = 0f; f.MaxR = 0f;
            f.Fresh = false;
        }

        /// <summary>Engine-Faden: Liste der Mess-Objekte an Geraete und Mikro anpassen (nach dem Suchen der Geraete).</summary>
        private void SyncFastMeters()
        {
            FastMeter[] cur;
            lock (_fastLock) cur = _fast;
            var list = new List<FastMeter>();
            foreach (var d in _devices.Values)
                if (d.Meter != null) list.Add(Reuse(cur, d.Id, d.Meter) ?? new FastMeter { Id = d.Id, Meter = d.Meter });
            var m = _mic;
            if (m?.Meter != null) list.Add(Reuse(cur, MicKey, m.Meter) ?? new FastMeter { Id = MicKey, Meter = m.Meter });
            bool same = list.Count == cur.Length;
            if (same) foreach (var f in list) if (Array.IndexOf(cur, f) < 0) { same = false; break; }
            if (same) return;
            lock (_fastLock) _fast = list.ToArray();
        }

        private static FastMeter Reuse(FastMeter[] cur, string id, IAudioMeterInformation meter)
        {
            foreach (var f in cur) if (ReferenceEquals(f.Meter, meter) && string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>Vor dem Freigeben eines Mess-Objekts: aus der schnellen Messung nehmen (wartet, bis sie es nicht mehr benutzt).</summary>
        private void DropFast(IAudioMeterInformation meter)
        {
            if (meter == null) return;
            lock (_fastLock)
            {
                bool any = false;
                foreach (var f in _fast) if (ReferenceEquals(f.Meter, meter)) { any = true; break; }
                if (any) _fast = Array.FindAll(_fast, f => !ReferenceEquals(f.Meter, meter));
            }
        }

        /// <summary>Engine-Faden: Vorrang an/aus (wird im Engine-Takt geprueft).</summary>
        private void ApplyEngineBoost()
        {
            bool b = _boost;
            if (b == _engineBoosted) return;
            _engineBoosted = b;
            try { Thread.CurrentThread.Priority = b ? ThreadPriority.AboveNormal : ThreadPriority.Normal; } catch { }
        }

        /// <summary>Takt des Engine-Fadens (ms): mit schneller Messung etwas oefter (Kanal-Pegel kommen hier an).</summary>
        private int EngineWaitMs => _fastWanted ? 20 : 25;

        private void FastLoop()
        {
            ThreadCpu.Register("Pegel");   // 3.4.1
            IntPtr timer = IntPtr.Zero;
            try { timer = PerfNative.CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003); } catch { timer = IntPtr.Zero; }
            bool boosted = false;
            IntPtr mm = IntPtr.Zero;
            try
            {
                while (_running)
                {
                    PerfNative.MmcssFollow(FramePacer.GameBoost && _fastWanted, ref mm);   // 3.0.2: Spiele-Vorrang
                    if (_boost != boosted)
                    {
                        boosted = _boost;
                        try { Thread.CurrentThread.Priority = boosted ? ThreadPriority.AboveNormal : ThreadPriority.Normal; } catch { }
                    }
                    if (!_fastWanted) { Thread.Sleep(100); continue; }
                    lock (_fastLock)
                    {
                        foreach (var f in _fast)
                        {
                            try
                            {
                                float l, r;
                                if (f.Buf == null && f.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0 && mc <= 32) f.Buf = new float[mc];
                                if (f.Buf != null && f.Meter.GetChannelsPeakValues((uint)f.Buf.Length, f.Buf) == 0) SplitLR(f.Buf, out l, out r);
                                else if (f.Meter.GetPeakValue(out float pk) == 0) l = r = pk;
                                else continue;
                                if (l > f.MaxL) f.MaxL = l;
                                if (r > f.MaxR) f.MaxR = r;
                                f.Fresh = true;
                            }
                            catch { }
                        }
                    }
                    WaitFast(timer);
                }
            }
            finally
            {
                PerfNative.MmcssEnd(mm);
                if (timer != IntPtr.Zero) { try { PerfNative.CloseHandle(timer); } catch { } }
            }
        }

        private static void WaitFast(IntPtr timer)
        {
            if (timer != IntPtr.Zero)
            {
                long due = -FastMeterMs * 10000L;   // relativ, in 100 ns
                try
                {
                    if (PerfNative.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                    {
                        PerfNative.WaitForSingleObject(timer, 200);
                        return;
                    }
                }
                catch { }
            }
            Thread.Sleep(FastMeterMs);   // ohne genauen Zeitgeber: so genau, wie Windows gerade kann
        }
    }
}
