using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mischpult.Native
{
    internal static class Win32
    {
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        public const int SW_RESTORE = 9;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        /// <summary>Prozess-ID -> Eltern-Prozess-ID fuer alle laufenden Prozesse.</summary>
        public static System.Collections.Generic.Dictionary<uint, uint> GetParentMap()
        {
            var map = new System.Collections.Generic.Dictionary<uint, uint>();
            if (!IsWindows) return map;
            IntPtr snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W)) };
                if (!Process32FirstW(snap, ref e)) return map;
                do { map[e.th32ProcessID] = e.th32ParentProcessID; }
                while (Process32NextW(snap, ref e));
            }
            catch { }
            finally { CloseHandle(snap); }
            return map;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const byte VK_MEDIA_NEXT_TRACK = 0xB0, VK_MEDIA_PREV_TRACK = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;

        /// <summary>Medientaste druecken (wirkt auf Spotify, YouTube im Browser usw. – wie die Taste auf der Tastatur).</summary>
        public static void SendMediaKey(byte vk)
        {
            if (!IsWindows) return;
            try
            {
                keybd_event(vk, 0, 0x1 /* EXTENDEDKEY */, UIntPtr.Zero);
                keybd_event(vk, 0, 0x1 | 0x2 /* KEYUP */, UIntPtr.Zero);
            }
            catch { }
        }

        /// <summary>Prozess-ID des Fensters im Vordergrund (0 = unbekannt).</summary>
        public static uint ForegroundPid()
        {
            if (!IsWindows) return 0;
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                GetWindowThreadProcessId(h, out uint pid);
                return pid;
            }
            catch { return 0; }
        }

        /// <summary>Ist die Taste (oder Maustaste) gerade gedrueckt?</summary>
        public static bool IsKeyDown(int vk)
        {
            if (!IsWindows || vk <= 0 || vk > 0xFF) return false;
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        public static string GetProcessPath(uint pid)
        {
            if (!IsWindows || pid == 0) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        // ---------------------------------------------------------------- Bildschirm-Skalierung (DPI)
        public const int WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);

        /// <summary>
        /// Sicherheitshalber auch per Programm "scharf auf jedem Bildschirm" einschalten (falls das Manifest nicht greift).
        /// Ist es schon gesetzt, lehnt Windows einfach ab – das ist in Ordnung.
        /// </summary>
        public static void EnsureDpiAware()
        {
            if (!IsWindows) return;
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */)) return; } catch { }
            try { SetProcessDpiAwareness(2 /* PER_MONITOR */); } catch { }
        }

        /// <summary>Wie das Mischpult mit der Skalierung umgeht: 0 = Windows rechnet hoch (unscharf), 1 = System, 2 = pro Bildschirm, -1 = unbekannt.</summary>
        public static int DpiAwareness()
        {
            if (!IsWindows) return -1;
            try { return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()); }
            catch { return -1; }
        }

        /// <summary>Echte Skalierung (DPI) des Bildschirms an diesem Punkt; 0 = unbekannt.</summary>
        public static int MonitorDpi(int x, int y)
        {
            if (!IsWindows) return 0;
            try
            {
                var mon = MonitorFromPoint(new POINT(x, y), 2 /* MONITOR_DEFAULTTONEAREST */);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx >= 72 && dx <= 960)
                    return (int)dx;
            }
            catch { }
            return 0;
        }
    }
}
