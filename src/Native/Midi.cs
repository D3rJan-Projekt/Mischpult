using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>Eine MIDI-Nachricht (Regler, Taste oder Pitchbend).</summary>
    internal struct MidiEvent
    {
        public string Device;
        public int Kind;     // 0 = Controller (CC), 1 = Note, 2 = Pitchbend
        public int Channel;  // 0..15
        public int Number;   // CC- bzw. Notennummer (Pitchbend: 0)
        public int Value;    // CC/Note: 0..127, Pitchbend: 0..16383
        public float Normalized => Kind == 2 ? Value / 16383f : Value / 127f;
    }

    /// <summary>
    /// MIDI-Eingaenge ueber winmm. Nachrichten landen in einer Warteschlange, die die Oberflaeche abholt.
    /// Hinweis: Windows-MIDI-Eingaenge koennen meist nur von einem Programm gleichzeitig geoeffnet werden.
    /// </summary>
    internal sealed class MidiHub : IDisposable
    {
        private const int CALLBACK_FUNCTION = 0x30000;
        private const int MIM_DATA = 0x3C3;

        private delegate void MidiInProc(IntPtr h, int msg, IntPtr instance, IntPtr p1, IntPtr p2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MIDIINCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] private static extern int midiInGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern int midiInGetDevCaps(UIntPtr id, ref MIDIINCAPS caps, int size);
        [DllImport("winmm.dll")] private static extern int midiInOpen(out IntPtr handle, int deviceId, MidiInProc proc, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int midiInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern int midiInStop(IntPtr h);
        [DllImport("winmm.dll")] private static extern int midiInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int midiInClose(IntPtr h);

        private static readonly MidiInProc Proc = Callback; // muss am Leben bleiben
        private static readonly ConcurrentDictionary<long, MidiHub> Hubs = new ConcurrentDictionary<long, MidiHub>();
        private static readonly ConcurrentDictionary<long, string> Names = new ConcurrentDictionary<long, string>();
        private static long _nextInstance = 1;

        private readonly Dictionary<string, IntPtr> _open = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _instances = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<MidiEvent> _queue = new ConcurrentQueue<MidiEvent>();
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool Available => Win32.IsWindows;

        /// <summary>Namen aller MIDI-Eingaenge (gleiche Namen werden durchnummeriert).</summary>
        public static List<string> DeviceNames()
        {
            var list = new List<string>();
            if (!Available) return list;
            try
            {
                int n = midiInGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var caps = new MIDIINCAPS();
                    string name = midiInGetDevCaps((UIntPtr)(uint)i, ref caps, Marshal.SizeOf(typeof(MIDIINCAPS))) == 0
                        ? (caps.szPname ?? "MIDI " + (i + 1)).Trim() : "MIDI " + (i + 1);
                    string unique = name;
                    for (int k = 2; list.Contains(unique); k++) unique = name + " #" + k;
                    list.Add(unique);
                }
            }
            catch { }
            return list;
        }

        public IEnumerable<string> OpenDevices => _open.Keys;
        public string ErrorFor(string device) => _errors.TryGetValue(device, out var e) ? e : null;

        /// <summary>Oeffnet genau die gewuenschten Geraete (per Name), schliesst alle anderen.</summary>
        public void Sync(ICollection<string> wanted)
        {
            if (!Available) return;
            var names = DeviceNames();
            foreach (var name in new List<string>(_open.Keys))
                if (!wanted.Contains(name) || !names.Contains(name)) Close(name); // nicht gewuenscht oder abgesteckt
            if (wanted.Count == 0) return;
            foreach (var name in wanted)
            {
                if (_open.ContainsKey(name)) continue;
                int idx = names.IndexOf(name);
                if (idx < 0) { _errors[name] = "nicht angeschlossen"; continue; }
                long inst = System.Threading.Interlocked.Increment(ref _nextInstance);
                try
                {
                    int r = midiInOpen(out IntPtr h, idx, Proc, new IntPtr(inst), CALLBACK_FUNCTION);
                    if (r != 0 || h == IntPtr.Zero) { _errors[name] = r == 4 ? "von einem anderen Programm belegt" : "Fehler " + r; continue; }
                    Hubs[inst] = this;
                    Names[inst] = name;
                    midiInStart(h);
                    _open[name] = h;
                    _instances[name] = inst;
                    _errors.Remove(name);
                }
                catch (Exception ex) { _errors[name] = ex.Message; }
            }
        }

        private void Close(string name)
        {
            if (!_open.TryGetValue(name, out var h)) return;
            _open.Remove(name);
            try { midiInStop(h); midiInReset(h); midiInClose(h); } catch { }
            if (_instances.TryGetValue(name, out long inst))
            {
                Hubs.TryRemove(inst, out _);
                Names.TryRemove(inst, out _);
                _instances.Remove(name);
            }
        }

        public bool TryDequeue(out MidiEvent e) => _queue.TryDequeue(out e);

        private static void Callback(IntPtr h, int msg, IntPtr instance, IntPtr p1, IntPtr p2)
        {
            if (msg != MIM_DATA) return;
            long inst = instance.ToInt64();
            if (!Hubs.TryGetValue(inst, out var hub) || !Names.TryGetValue(inst, out var name)) return;
            if (Parse(name, (int)(p1.ToInt64() & 0xFFFFFF), out var e))
            {
                if (hub._queue.Count < 2000) hub._queue.Enqueue(e);
            }
        }

        /// <summary>Kurze MIDI-Nachricht zerlegen (Status | Daten1 &lt;&lt; 8 | Daten2 &lt;&lt; 16).</summary>
        public static bool Parse(string device, int packed, out MidiEvent e)
        {
            e = default(MidiEvent);
            int status = packed & 0xFF, d1 = (packed >> 8) & 0x7F, d2 = (packed >> 16) & 0x7F;
            int type = status & 0xF0, ch = status & 0x0F;
            e.Device = device;
            e.Channel = ch;
            switch (type)
            {
                case 0xB0: e.Kind = 0; e.Number = d1; e.Value = d2; return true;
                case 0x90: e.Kind = 1; e.Number = d1; e.Value = d2; return true;      // Velocity 0 = losgelassen
                case 0x80: e.Kind = 1; e.Number = d1; e.Value = 0; return true;
                case 0xE0: e.Kind = 2; e.Number = 0; e.Value = d1 | (d2 << 7); return true;
            }
            return false;
        }

        public void Dispose()
        {
            foreach (var name in new List<string>(_open.Keys)) Close(name);
        }
    }
}
