using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>
    /// 4.3: Akkustand von Bluetooth-Geraeten, so wie Windows ihn kennt (Einstellungen → Bluetooth). Nur lesen.
    /// Geht nur bei Geraeten, die Windows den Akkustand melden – Kopfhoerer mit eigenem Funk-Stick meist nicht.
    /// </summary>
    internal static class Battery430
    {
        public sealed class Device { public string Name; public int Percent; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        private static readonly IntPtr Invalid = new IntPtr(-1);
        // Akkustand (Byte, 0..100) und Anzeigename
        private static readonly DEVPROPKEY BatteryKey = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
        private static readonly DEVPROPKEY FriendlyKey = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        private static readonly DEVPROPKEY NameKey = new DEVPROPKEY { fmtid = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), pid = 10 };

        /// <summary>Alle Bluetooth-Geraete mit Akkustand (leer, wenn nichts gemeldet wird oder nicht Windows).</summary>
        public static List<Device> Read()
        {
            var res = new List<Device>();
            if (!Win32.IsWindows) return res;
            foreach (var en in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
            {
                IntPtr set = Invalid;
                try
                {
                    set = SetupDiGetClassDevsW(IntPtr.Zero, en, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                    if (set == Invalid || set == IntPtr.Zero) continue;
                    var d = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    for (uint i = 0; i < 512 && SetupDiEnumDeviceInfo(set, i, ref d); i++)
                    {
                        var buf = new byte[8];
                        var bk = BatteryKey;
                        if (!SetupDiGetDevicePropertyW(set, ref d, ref bk, out uint type, buf, (uint)buf.Length, out uint req, 0) || req < 1) continue;
                        int pct = buf[0];
                        if (pct > 100) continue;
                        string name = Str(set, ref d, FriendlyKey) ?? Str(set, ref d, NameKey);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!res.Exists(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) res.Add(new Device { Name = name.Trim(), Percent = pct });
                    }
                }
                catch { }
                finally { if (set != Invalid && set != IntPtr.Zero) try { SetupDiDestroyDeviceInfoList(set); } catch { } }
            }
            return res;
        }

        private static string Str(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key)
        {
            try
            {
                var buf = new byte[512];
                var k = key;
                if (!SetupDiGetDevicePropertyW(set, ref d, ref k, out uint type, buf, (uint)buf.Length, out uint req, 0) || req < 2) return null;
                return System.Text.Encoding.Unicode.GetString(buf, 0, (int)Math.Min(req, (uint)buf.Length)).TrimEnd('\0');
            }
            catch { return null; }
        }

        /// <summary>Passendes Geraet zum Ausgabegeraet (Name taucht im Geraetenamen auf), sonst null.</summary>
        public static Device Match(List<Device> list, IEnumerable<string> outputNames)
        {
            if (list == null || list.Count == 0) return null;
            foreach (var o in outputNames)
            {
                if (string.IsNullOrEmpty(o)) continue;
                foreach (var d in list)
                    if (o.IndexOf(d.Name, StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            }
            return null;
        }
    }
}
