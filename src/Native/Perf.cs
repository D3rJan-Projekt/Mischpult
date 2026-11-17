using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Mischpult.Native
{
    /// <summary>Windows-Aufrufe fuer gleichmaessige Bilder und den Spiele-Modus (2.15.1).</summary>
    internal static class PerfNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWaitableTimerExW(IntPtr attr, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr routine, IntPtr arg, bool resume);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern int GetRegionData(IntPtr rgn, int count, byte[] data);
        [DllImport("user32.dll")] private static extern int GetUpdateRgn(IntPtr hwnd, IntPtr rgn, bool erase);

        /// <summary>
        /// 2.15.3: welche Stellen eines Fensters gleich wirklich neu gezeichnet werden (vor WM_PAINT abfragen).
        /// Liefert die Anzahl Rechtecke in <paramref name="rects"/>, -1 = unbekannt/zu viele (dann alles im Ausschnitt zeichnen).
        /// </summary>
        public static int UpdateRects(IntPtr hwnd, System.Drawing.Rectangle[] rects)
        {
            if (!Win32.IsWindows || hwnd == IntPtr.Zero) return -1;
            IntPtr rgn = IntPtr.Zero;
            try
            {
                rgn = CreateRectRgn(0, 0, 0, 0);
                if (rgn == IntPtr.Zero) return -1;
                int type = GetUpdateRgn(hwnd, rgn, false);
                if (type != 2 /* SIMPLEREGION */ && type != 3 /* COMPLEXREGION */) return -1;
                int size = GetRegionData(rgn, 0, null);
                if (size < 32 || size > 32 + 16 * 64) return -1;
                var buf = new byte[size];
                if (GetRegionData(rgn, size, buf) == 0) return -1;
                int n = BitConverter.ToInt32(buf, 8);
                if (n <= 0 || n > rects.Length || 32 + n * 16 > size) return -1;
                for (int i = 0; i < n; i++)
                {
                    int o = 32 + i * 16;
                    rects[i] = System.Drawing.Rectangle.FromLTRB(BitConverter.ToInt32(buf, o), BitConverter.ToInt32(buf, o + 4), BitConverter.ToInt32(buf, o + 8), BitConverter.ToInt32(buf, o + 12));
                }
                return n;
            }
            catch { return -1; }
            finally { if (rgn != IntPtr.Zero) { try { DeleteObject(rgn); } catch { } } }
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

        private const int MONITOR_DEFAULTTONEAREST = 2;
        public const uint TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003;

        /// <summary>Fenster deckt seinen ganzen Bildschirm ab (Vollbild-Spiel oder -Video)? Desktop/Taskleiste zaehlen nicht.</summary>
        public static bool IsFullscreen(IntPtr hwnd)
        {
            if (!Win32.IsWindows) return false;
            try
            {
                if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return false;
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
                if (!GetWindowRect(hwnd, out var r)) return false;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi)) return false;
                return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            catch { return false; }
        }

        /// <summary>Liegen beide Fenster (ueberwiegend) auf demselben Bildschirm?</summary>
        public static bool SameMonitor(IntPtr a, IntPtr b)
        {
            if (!Win32.IsWindows || a == IntPtr.Zero || b == IntPtr.Zero) return false;
            try { return MonitorFromWindow(a, MONITOR_DEFAULTTONEAREST) == MonitorFromWindow(b, MONITOR_DEFAULTTONEAREST); }
            catch { return false; }
        }

        /// <summary>Bildwiederholrate des Bildschirms (Hz), 60 wenn unbekannt.</summary>
        public static int RefreshRate(Screen s)
        {
            if (!Win32.IsWindows || s == null) return 60;
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(s.DeviceName, -1 /* ENUM_CURRENT_SETTINGS */, ref dm) && dm.dmDisplayFrequency > 1)
                    return Math.Max(24, Math.Min(500, dm.dmDisplayFrequency));
            }
            catch { }
            return 60;
        }

        /// <summary>Bilder pro Sekunde passend zum Bildschirm: jedes 1., 2., 3. … Bild des Bildschirms, hoechstens 90.</summary>
        public static double AutoFps(int hz) => AutoFps(hz, 90);

        /// <summary>3.0: wie oben, aber hoechstens max (z. B. 165 bei "Hohe Bildrate").</summary>
        public static double AutoFps(int hz, int max)
        {
            if (hz < 24) hz = 60;
            return hz / (double)Divisor(hz, max);
        }

        /// <summary>Jedes wievielte Bild des Bildschirms gezeichnet wird, damit es hoechstens max Bilder/s sind.</summary>
        public static int Divisor(int hz, double max)
        {
            if (hz < 24) hz = 60;
            return Math.Max(1, (int)Math.Ceiling(hz / Math.Max(10.0, max) - 0.001));
        }

        // ---- 3.0: Bildaufbau (VSync) eines bestimmten Bildschirms abwarten
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_OPENADAPTERFROMHDC { public IntPtr hDc; public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint VidPnSourceId; }
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_WAITFORVERTICALBLANKEVENT { public uint hAdapter; public uint hDevice; public uint VidPnSourceId; }
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

        [DllImport("gdi32.dll")] public static extern int D3DKMTOpenAdapterFromHdc(ref D3DKMT_OPENADAPTERFROMHDC p);
        [DllImport("gdi32.dll")] public static extern int D3DKMTWaitForVerticalBlankEvent(ref D3DKMT_WAITFORVERTICALBLANKEVENT p);
        [DllImport("gdi32.dll")] public static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr devMode);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

        // ---- 3.0: sofort zeichnen (alle ungueltigen Bereiche samt Unterfenstern jetzt, nicht erst irgendwann)
        public const uint RDW_UPDATENOW = 0x0100, RDW_ALLCHILDREN = 0x0080;
        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

        /// <summary>Alles, was zum Neuzeichnen ansteht, jetzt zeichnen (Fenster und Unterfenster).</summary>
        public static void PaintNow(Control c)
        {
            if (!Win32.IsWindows || c == null || c.IsDisposed || !c.IsHandleCreated || !c.Visible) return;
            try { RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN); } catch { }
        }

        // ---- 3.0.2: nicht von Windows drosseln lassen (Energiesparen / EcoQoS)
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        private const int ProcessPowerThrottling = 4;
        private const uint THROTTLE_EXECUTION_SPEED = 0x1, THROTTLE_IGNORE_TIMER_RESOLUTION = 0x4;

        /// <summary>Was die Drosselung gerade macht (fuer den Zustand): null = nie eingestellt.</summary>
        public static string ThrottleState { get; private set; }

        /// <summary>
        /// on: Windows soll das Pult nicht zum Stromsparen bremsen (kein "Effizienzmodus", keine langsamen Kerne, genaue
        /// Zeitgeber auch im Hintergrund). off: wieder Windows entscheiden lassen. Gibt zurueck, ob es geklappt hat.
        /// </summary>
        public static bool SetNoThrottle(bool on)
        {
            if (!Win32.IsWindows) { ThrottleState = null; return false; }
            try
            {
                var st = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = on ? THROTTLE_EXECUTION_SPEED | THROTTLE_IGNORE_TIMER_RESOLUTION : 0u,
                    StateMask = 0
                };
                int size = Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE));
                bool ok = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref st, size);
                if (!ok && on)
                {
                    st.ControlMask = THROTTLE_EXECUTION_SPEED;   // Windows 10 kennt den Zeitgeber-Teil noch nicht
                    ok = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref st, size);
                }
                ThrottleState = !on ? (ok ? "Windows entscheidet" : null) : ok ? "abgeschaltet" : "geht hier nicht";
                return ok;
            }
            catch { ThrottleState = on ? "geht hier nicht (Windows zu alt)" : null; return false; }
        }

        // ---- 3.0.2: Spiele-Vorrang (MMCSS) – Windows behandelt den Faden wie den eines Spiels
        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);
        [DllImport("avrt.dll", SetLastError = true)] private static extern bool AvRevertMmThreadCharacteristics(IntPtr h);

        /// <summary>Aufrufenden Faden in den Spiele-Vorrang nehmen (IntPtr.Zero = ging nicht). Muss im selben Faden wieder beendet werden.</summary>
        public static IntPtr MmcssBegin()
        {
            if (!Win32.IsWindows) return IntPtr.Zero;
            try { uint idx = 0; return AvSetMmThreadCharacteristicsW("Games", ref idx); }
            catch { return IntPtr.Zero; }
        }

        public static void MmcssEnd(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            try { AvRevertMmThreadCharacteristics(h); } catch { }
        }

        [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr process, uint cls);
        private static bool _procBoost;
        /// <summary>Ganzes Programm eine Stufe hoeher ("hoeher als normal") – nur zuruecksetzen, wenn wir es selbst hochgesetzt haben.</summary>
        public static void SetProcessBoost(bool on)
        {
            if (!Win32.IsWindows || on == _procBoost) return;
            try { if (SetPriorityClass(GetCurrentProcess(), on ? 0x8000u /* ABOVE_NORMAL */ : 0x20u /* NORMAL */)) _procBoost = on; } catch { }
        }
        public static bool ProcessBoosted => _procBoost;

        /// <summary>Faden-Helfer: Vorrang an/aus, wenn sich der Wunsch geaendert hat (immer im eigenen Faden aufrufen).</summary>
        public static void MmcssFollow(bool want, ref IntPtr handle)
        {
            if (want && handle == IntPtr.Zero) handle = MmcssBegin();
            else if (!want && handle != IntPtr.Zero) { MmcssEnd(handle); handle = IntPtr.Zero; }
        }

        // ---- 3.0.2: Bildschirme und Grafikkarten (fuer den Zustand: haengt der zweite Bildschirm an einer anderen Karte?)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        /// <summary>Name der Grafikkarte, an der der Bildschirm (Screen.DeviceName) haengt – null wenn unbekannt.</summary>
        public static string AdapterOf(string deviceName)
        {
            if (!Win32.IsWindows || string.IsNullOrEmpty(deviceName)) return null;
            try
            {
                for (uint i = 0; i < 32; i++)
                {
                    var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                    if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                    if (string.Equals(dd.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(dd.DeviceString) ? null : dd.DeviceString.Trim();
                }
            }
            catch { }
            return null;
        }

        /// <summary>Bildschirm, auf dem das Fenster steht (Handle, billig – zum Vergleichen).</summary>
        public static IntPtr MonitorOf(IntPtr hwnd)
        {
            if (!Win32.IsWindows || hwnd == IntPtr.Zero) return IntPtr.Zero;
            try { return MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST); } catch { return IntPtr.Zero; }
        }

        /// <summary>Wie weit nach dem Bildaufbau gezeichnet wird (ms): ein knappes Drittel des Bildes, 1 … 5 ms.</summary>
        public static double PhaseFor(int hz)
        {
            if (hz < 24) hz = 60;
            return Math.Max(1.0, Math.Min(5.0, 1000.0 / hz * 0.3));
        }

        private static bool _fineTimer;
        /// <summary>Genaue Windows-Zeitgeber (1 ms) fuer dieses Programm an/aus – sonst sind Wartezeiten auf ~15,6 ms gerundet.</summary>
        public static void SetFineTimer(bool on)
        {
            if (on == _fineTimer || !Win32.IsWindows) return;
            try
            {
                if (on) timeBeginPeriod(1); else timeEndPeriod(1);
                _fineTimer = on;
            }
            catch { }
        }
        public static bool FineTimer => _fineTimer;
    }

    /// <summary>
    /// Gibt den Takt fuers Pult vor – gleichmaessig, auch wenn nebenbei ein Spiel laeuft: ein eigener Faden wartet mit
    /// einem genauen Windows-Zeitgeber (ab Windows 10 1803, sonst einem normalen) und stoesst dann einen Durchgang im
    /// UI-Faden an. Laeuft der vorige noch, wird keiner nachgeschoben (kein Stau). Nur unter Windows (sonst false).
    /// </summary>
    internal sealed class FramePacer : IDisposable
    {
        private readonly Control _owner;
        private readonly Action _tick;
        private readonly Action _run;
        private Thread _thread;
        private volatile bool _running;
        private volatile int _interval100us = 167;   // Abstand in 0,1 ms
        private int _pending;
        private IntPtr _timer;

        public FramePacer(Control owner, Action tick)
        {
            _owner = owner;
            _tick = tick;
            _run = () =>
            {
                try { _tick(); }
                finally { Interlocked.Exchange(ref _pending, 0); }
            };
        }

        /// <summary>Bilder pro Sekunde (20 … 240).</summary>
        public double Fps
        {
            get => 10000.0 / _interval100us;
            set => _interval100us = (int)Math.Round(10000.0 / Math.Max(20.0, Math.Min(240.0, value)));
        }

        // ---- 3.0: Takt vom Bildaufbau des Bildschirms (VSync)
        private VBlankWaiter _vb;
        private volatile int _vbDiv = 1;
        private int _vbCount;
        private string _vbDevice;

        /// <summary>Takt im Gleichschritt mit dem Bildaufbau dieses Bildschirms (device = Screen.DeviceName, null = aus), jedes divisor-te Bild.</summary>
        public void SetVSync(string device, int divisor)
        {
            _vbDiv = Math.Max(1, divisor);
            if (device == _vbDevice) return;   // gleicher Bildschirm: nichts tun (ging es nicht, bleibt der Zeitgeber)
            _vbDevice = device;
            var old = _vb;
            _vb = device != null && Win32.IsWindows ? new VBlankWaiter(device) : null;
            old?.Dispose();
        }

        /// <summary>Laeuft der Takt gerade vom Bildaufbau?</summary>
        public bool VSyncActive => _vb != null && _vb.Ok;
        public string VSyncError => _vb?.Error;

        // ---- 3.0.2
        private volatile int _phase100us;
        /// <summary>So lange nach dem Bildaufbau wird gezeichnet (ms, 0 = sofort) – damit das Bild nie gleichzeitig mit Windows fertig wird.</summary>
        public double PhaseMs
        {
            get => _phase100us / 10.0;
            set => _phase100us = (int)Math.Round(Math.Max(0.0, Math.Min(30.0, value)) * 10.0);
        }
        /// <summary>Takt- und VSync-Faden mit Spiele-Vorrang (MMCSS).</summary>
        public static volatile bool GameBoost;
        /// <summary>Laeuft der Takt-Faden gerade mit Spiele-Vorrang?</summary>
        public bool Boosted { get; private set; }

        public bool Running => _running;
        /// <summary>Genauer (hochaufloesender) Zeitgeber in Benutzung?</summary>
        public bool HighResolution { get; private set; }

        /// <summary>Startet den Takt-Faden – false, wenn es nicht geht (nicht Windows).</summary>
        public bool TryStart()
        {
            if (_running) return true;
            if (!Win32.IsWindows) return false;
            try
            {
                _timer = PerfNative.CreateWaitableTimerExW(IntPtr.Zero, null, PerfNative.TIMER_HIGH_RESOLUTION, PerfNative.TIMER_ALL_ACCESS);
                HighResolution = _timer != IntPtr.Zero;
                if (_timer == IntPtr.Zero) _timer = PerfNative.CreateWaitableTimerExW(IntPtr.Zero, null, 0, PerfNative.TIMER_ALL_ACCESS);
            }
            catch { _timer = IntPtr.Zero; HighResolution = false; }
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Mischpult Takt", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            return true;
        }

        private void Fire()
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;   // voriger Durchgang laeuft noch
            try
            {
                if (_owner.IsHandleCreated && !_owner.IsDisposed) _owner.BeginInvoke(_run);
                else Interlocked.Exchange(ref _pending, 0);
            }
            catch { Interlocked.Exchange(ref _pending, 0); }
        }

        private void Loop()
        {
            ThreadCpu.Register("Takt");   // 3.4.1
            var sw = Stopwatch.StartNew();
            double next = 0;
            IntPtr mm = IntPtr.Zero;
            try
            {
            while (_running)
            {
                PerfNative.MmcssFollow(GameBoost, ref mm);
                Boosted = mm != IntPtr.Zero;
                var vb = _vb;
                if (vb != null && vb.Ok)
                {
                    // VSync: auf den Bildaufbau warten; kommt keiner (Bildschirm aus), trotzdem weiter (Automatik laeuft)
                    bool got = vb.Signal.WaitOne(60);
                    if (!_running) break;
                    if (got && (++_vbCount % _vbDiv) != 0) continue;
                    // 3.0.2: kurz nach dem Bildaufbau zeichnen – sonst wird das Bild manchmal knapp vor, manchmal knapp nach
                    // Windows fertig und kommt mal ein Bild frueher, mal spaeter (Ruckeln trotz VSync)
                    int ph = _phase100us;
                    if (got && ph > 0) Wait(ph / 10.0);
                    if (!_running) break;
                    Fire();
                    next = sw.Elapsed.TotalMilliseconds;
                    continue;
                }
                double interval = _interval100us / 10.0;
                next += interval;
                double now = sw.Elapsed.TotalMilliseconds;
                if (next < now - interval * 2) next = now + interval;   // weit hinterher (z. B. Ruhezustand): neu anfangen
                double wait = next - now;
                if (wait > 0.3) Wait(wait);
                if (!_running) break;
                Fire();
            }
            }
            finally { PerfNative.MmcssEnd(mm); Boosted = false; }
        }

        private void Wait(double ms)
        {
            var t = _timer;
            if (t != IntPtr.Zero)
            {
                long due = -(long)(ms * 10000);   // relativ, in 100 ns
                try
                {
                    if (PerfNative.SetWaitableTimer(t, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                    {
                        PerfNative.WaitForSingleObject(t, 1000);
                        return;
                    }
                }
                catch { }
            }
            Thread.Sleep(Math.Max(1, (int)ms));
        }

        public void Stop()
        {
            _running = false;
            var vb = _vb; _vb = null; _vbDevice = null;
            vb?.Dispose();
            try { _thread?.Join(500); } catch { }
            _thread = null;
            if (_timer != IntPtr.Zero) { try { PerfNative.CloseHandle(_timer); } catch { } _timer = IntPtr.Zero; }
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// 3.0: wartet in einem eigenen Faden auf den Bildaufbau (VBlank) eines bestimmten Bildschirms und meldet ihn ueber Signal.
    /// Geht etwas nicht (aelteres Windows, Remote-Desktop, virtueller Bildschirm), ist Ok = false und der Takt laeuft wie bisher.
    /// </summary>
    internal sealed class VBlankWaiter : IDisposable
    {
        public readonly AutoResetEvent Signal = new AutoResetEvent(false);
        public volatile bool Ok;
        public volatile string Error;
        private uint _adapter, _source;
        private volatile bool _run = true;
        private readonly Thread _thread;

        public VBlankWaiter(string device)
        {
            try
            {
                IntPtr dc = PerfNative.CreateDC(null, device, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) { Error = "Bildschirm nicht gefunden"; return; }
                try
                {
                    var o = new PerfNative.D3DKMT_OPENADAPTERFROMHDC { hDc = dc };
                    int st = PerfNative.D3DKMTOpenAdapterFromHdc(ref o);
                    if (st != 0) { Error = "Grafiktreiber gibt den Bildtakt nicht her (0x" + st.ToString("X8") + ")"; return; }
                    _adapter = o.hAdapter; _source = o.VidPnSourceId;
                }
                finally { PerfNative.DeleteDC(dc); }
                Ok = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "Mischpult VSync", Priority = ThreadPriority.Highest };
                _thread.Start();
            }
            catch (Exception ex) { Ok = false; Error = ex.Message; }
        }

        private void Loop()
        {
            ThreadCpu.Register("Takt");   // 3.4.1
            var w = new PerfNative.D3DKMT_WAITFORVERTICALBLANKEVENT { hAdapter = _adapter, hDevice = 0, VidPnSourceId = _source };
            var sw = Stopwatch.StartNew();
            double last = 0;
            int fast = 0;
            IntPtr mm = IntPtr.Zero;
            try
            {
                while (_run)
                {
                    PerfNative.MmcssFollow(FramePacer.GameBoost, ref mm);
                    int st = PerfNative.D3DKMTWaitForVerticalBlankEvent(ref w);
                    if (!_run) break;
                    if (st != 0) { Error = "Bildtakt abgebrochen (0x" + st.ToString("X8") + ")"; Ok = false; break; }
                    double now = sw.Elapsed.TotalMilliseconds;
                    // kommt der "Bildaufbau" viel zu schnell, ist er nicht echt (z. B. virtueller Bildschirm): lieber der Zeitgeber
                    if (now - last < 2.0) { if (++fast > 30) { Error = "kein echter Bildtakt"; Ok = false; break; } }
                    else fast = 0;
                    last = now;
                    Signal.Set();
                }
            }
            catch (Exception ex) { Error = ex.Message; Ok = false; }
            finally { PerfNative.MmcssEnd(mm); }
            Signal.Set();
        }

        public void Dispose()
        {
            _run = false;
            try { _thread?.Join(200); } catch { }
            if (_adapter != 0)
            {
                try { var c = new PerfNative.D3DKMT_CLOSEADAPTER { hAdapter = _adapter }; PerfNative.D3DKMTCloseAdapter(ref c); } catch { }
                _adapter = 0;
            }
        }
    }
}
