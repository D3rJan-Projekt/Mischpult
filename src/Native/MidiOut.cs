using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>4.0: MIDI-Ausgang (Rueckmeldung an den Controller: Motorfader, LED-Kraenze, Tasten-Lampen).</summary>
    internal sealed class MidiOut : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MIDIOUTCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public ushort wTechnology, wVoices, wNotes, wChannelMask;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] private static extern int midiOutGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)] private static extern int midiOutGetDevCaps(UIntPtr id, ref MIDIOUTCAPS caps, int size);
        [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, int msg);
        [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);

        private readonly Dictionary<string, IntPtr> _open = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static List<string> DeviceNames()
        {
            var l = new List<string>();
            if (!Win32.IsWindows) return l;
            try
            {
                int n = midiOutGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var caps = new MIDIOUTCAPS();
                    if (midiOutGetDevCaps((UIntPtr)i, ref caps, Marshal.SizeOf(typeof(MIDIOUTCAPS))) == 0 && !string.IsNullOrEmpty(caps.szPname)) l.Add(caps.szPname);
                }
            }
            catch { }
            return l;
        }

        /// <summary>Passenden Ausgang zum Eingang finden: gleicher Name, sonst "IN" → "OUT", sonst gleicher Anfang.</summary>
        public static string MatchOutput(string input, IList<string> outputs)
        {
            if (string.IsNullOrEmpty(input) || outputs == null || outputs.Count == 0) return null;
            foreach (var o in outputs) if (string.Equals(o, input, StringComparison.OrdinalIgnoreCase)) return o;
            string swapped = input.Replace("MIDIIN", "MIDIOUT").Replace("In", "Out").Replace("IN", "OUT").Replace("Input", "Output");
            foreach (var o in outputs) if (string.Equals(o, swapped, StringComparison.OrdinalIgnoreCase)) return o;
            string head = input.Length > 6 ? input.Substring(0, Math.Min(input.Length, 10)) : input;
            foreach (var o in outputs) if (o.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        private IntPtr Handle(string name)
        {
            if (name == null || _failed.Contains(name)) return IntPtr.Zero;
            if (_open.TryGetValue(name, out var h)) return h;
            var names = DeviceNames();
            int id = names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (id < 0 || midiOutOpen(out h, id, IntPtr.Zero, IntPtr.Zero, 0) != 0) { _failed.Add(name); return IntPtr.Zero; }
            _open[name] = h;
            return h;
        }

        /// <summary>Kurze MIDI-Nachricht (Status, Daten 1, Daten 2).</summary>
        public bool Send(string device, int status, int d1, int d2)
        {
            var h = Handle(device);
            if (h == IntPtr.Zero) return false;
            try { return midiOutShortMsg(h, (status & 0xff) | ((d1 & 0x7f) << 8) | ((d2 & 0x7f) << 16)) == 0; } catch { return false; }
        }

        /// <summary>Fehlgeschlagene Geraete spaeter noch einmal versuchen (z. B. nach dem Anstecken).</summary>
        public void Retry() => _failed.Clear();

        public void Dispose()
        {
            foreach (var h in _open.Values) { try { midiOutClose(h); } catch { } }
            _open.Clear();
        }
    }
}
