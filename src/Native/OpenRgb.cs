using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Mischpult.Native
{
    /// <summary>
    /// 2.20: RGB-Beleuchtung ueber OpenRGB (SDK-Server, Standard-Port 6742). Verbindet sich im Hintergrund, schaltet die Geraete
    /// in den "Direct"-Modus und schickt bis zu 30-mal pro Sekunde die Farben. Laeuft OpenRGB nicht, wird alle paar Sekunden neu versucht.
    /// Protokoll-Version 0 (versteht jeder OpenRGB-Server).
    /// </summary>
    internal sealed class OpenRgbClient : IDisposable
    {
        public const int DefaultPort = 6742;
        private const uint PktCount = 0, PktData = 1, PktClientName = 50, PktListUpdated = 100, PktUpdateLeds = 1050, PktCustomMode = 1100;

        public readonly string Host;
        public readonly int Port;
        private readonly Thread _thread;
        private volatile bool _run = true;
        private volatile Color[] _frame;
        private int _frameVer, _sentVer = -1;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        /// <summary>Verbunden mit so vielen Geraeten/LEDs (fuer die Anzeige in den Einstellungen).</summary>
        public volatile bool Connected;
        public volatile string Status = "Verbinde …";
        public volatile int DeviceCount, LedCount;
        public volatile string[] DeviceNames = new string[0];
        /// <summary>Fuer Tests: Wartezeit bis zum naechsten Versuch.</summary>
        public int RetryMs = 4000;

        public OpenRgbClient(string host, int port)
        {
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port > 0 && port < 65536 ? port : DefaultPort;
            _thread = new Thread(Run) { IsBackground = true, Name = "Mischpult OpenRGB", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        /// <summary>Neue Farben: 1 Farbe = alle LEDs gleich, mehrere = ueber die LEDs jedes Geraets verteilt.</summary>
        public void SetFrame(Color[] colors)
        {
            if (colors == null || colors.Length == 0) return;
            var old = _frame;
            if (old != null && old.Length == colors.Length)
            {
                bool same = true;
                for (int i = 0; i < colors.Length; i++) if (old[i].ToArgb() != colors[i].ToArgb()) { same = false; break; }
                if (same) return;
            }
            _frame = (Color[])colors.Clone();
            Interlocked.Increment(ref _frameVer);
            _wake.Set();
        }

        /// <summary>Farbe fuer LED i von n aus den Farben des Bilds.</summary>
        internal static Color Pick(Color[] f, int i, int n)
        {
            if (f.Length == 1 || n <= 1) return f[0];
            int k = (int)((long)i * f.Length / n);
            return f[Math.Max(0, Math.Min(f.Length - 1, k))];
        }

        private sealed class Device { public uint Index; public string Name; public int Leds; }

        private void Run()
        {
            while (_run)
            {
                try { Session(); }
                catch (Exception ex)
                {
                    Connected = false;
                    Status = ex is SocketException || ex is IOException || ex is TimeoutException
                        ? "OpenRGB nicht erreichbar (" + Host + ":" + Port + ") – läuft OpenRGB mit gestartetem SDK-Server?"
                        : "OpenRGB: " + ex.Message;
                }
                Connected = false;
                if (!_run) break;
                _wake.WaitOne(RetryMs);
            }
        }

        private void Session()
        {
            using (var tcp = new TcpClient())
            {
                var ar = tcp.BeginConnect(Host, Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1500)) throw new TimeoutException();
                tcp.EndConnect(ar);
                tcp.NoDelay = true;
                var s = tcp.GetStream();
                s.ReadTimeout = 3000; s.WriteTimeout = 3000;
                Send(s, 0, PktClientName, Encoding.ASCII.GetBytes("Mischpult\0"));
                Send(s, 0, PktCount, null);
                uint count = BitConverter.ToUInt32(Expect(s, PktCount), 0);
                var devs = new List<Device>();
                for (uint i = 0; i < count && i < 64; i++)
                {
                    Send(s, i, PktData, null);
                    var d = ParseController(Expect(s, PktData));
                    d.Index = i;
                    if (d.Leds > 0) devs.Add(d);
                }
                foreach (var d in devs) Send(s, d.Index, PktCustomMode, null);
                int leds = 0; var names = new List<string>();
                foreach (var d in devs) { leds += d.Leds; names.Add(d.Name + " (" + d.Leds + ")"); }
                DeviceCount = devs.Count; LedCount = leds; DeviceNames = names.ToArray();
                Connected = true;
                Status = devs.Count == 0 ? "Verbunden – aber OpenRGB meldet keine RGB-Geräte" : "Verbunden: " + devs.Count + " Gerät" + (devs.Count == 1 ? "" : "e") + ", " + leds + " LEDs";
                _sentVer = -1;
                s.ReadTimeout = 50;
                while (_run)
                {
                    _wake.WaitOne(33);
                    // Meldungen vom Server lesen (z. B. Geraeteliste geaendert -> neu verbinden)
                    while (tcp.Available >= 16)
                    {
                        var h = ReadExact(s, 16);
                        uint id = BitConverter.ToUInt32(h, 8), size = BitConverter.ToUInt32(h, 12);
                        if (size > 0) ReadExact(s, (int)Math.Min(size, 1 << 24));
                        if (id == PktListUpdated) return;
                    }
                    int ver = Volatile.Read(ref _frameVer);
                    var f = _frame;
                    if (f == null || ver == _sentVer) continue;
                    _sentVer = ver;
                    foreach (var d in devs) Send(s, d.Index, PktUpdateLeds, LedPayload(f, d.Leds));
                }
            }
        }

        /// <summary>Daten fuer UPDATELEDS: Groesse, Anzahl, Farben (R | G&lt;&lt;8 | B&lt;&lt;16).</summary>
        internal static byte[] LedPayload(Color[] f, int leds)
        {
            var b = new byte[4 + 2 + leds * 4];
            BitConverter.GetBytes((uint)b.Length).CopyTo(b, 0);
            BitConverter.GetBytes((ushort)leds).CopyTo(b, 4);
            for (int i = 0; i < leds; i++)
            {
                var c = Pick(f, i, leds);
                b[6 + i * 4] = c.R; b[7 + i * 4] = c.G; b[8 + i * 4] = c.B; b[9 + i * 4] = 0;
            }
            return b;
        }

        private static void Send(NetworkStream s, uint dev, uint id, byte[] data)
        {
            int n = data?.Length ?? 0;
            var p = new byte[16 + n];
            p[0] = (byte)'O'; p[1] = (byte)'R'; p[2] = (byte)'G'; p[3] = (byte)'B';
            BitConverter.GetBytes(dev).CopyTo(p, 4);
            BitConverter.GetBytes(id).CopyTo(p, 8);
            BitConverter.GetBytes((uint)n).CopyTo(p, 12);
            if (n > 0) Array.Copy(data, 0, p, 16, n);
            s.Write(p, 0, p.Length);
        }

        /// <summary>Auf ein bestimmtes Paket warten (andere ueberspringen) und seine Daten liefern.</summary>
        private static byte[] Expect(NetworkStream s, uint id)
        {
            for (int guard = 0; guard < 50; guard++)
            {
                var h = ReadExact(s, 16);
                if (h[0] != 'O' || h[1] != 'R' || h[2] != 'G' || h[3] != 'B') throw new InvalidDataException("kein OpenRGB-Server");
                uint pid = BitConverter.ToUInt32(h, 8), size = BitConverter.ToUInt32(h, 12);
                if (size > (1 << 24)) throw new InvalidDataException("Antwort zu gross");
                var data = size > 0 ? ReadExact(s, (int)size) : new byte[0];
                if (pid == id) return data;
            }
            throw new InvalidDataException("keine Antwort");
        }

        private static byte[] ReadExact(NetworkStream s, int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new IOException("Verbindung beendet");
                got += r;
            }
            return b;
        }

        /// <summary>Geraetebeschreibung (Protokoll 0) lesen – gebraucht werden nur Name und Zahl der LEDs.</summary>
        internal static (string Name, int Leds) ParseControllerData(byte[] d)
        {
            var dev = ParseController(d);
            return (dev.Name, dev.Leds);
        }

        private static Device ParseController(byte[] d)
        {
            int p = 0;
            uint U32() { if (p + 4 > d.Length) throw new InvalidDataException("kurz"); uint v = BitConverter.ToUInt32(d, p); p += 4; return v; }
            ushort U16() { if (p + 2 > d.Length) throw new InvalidDataException("kurz"); ushort v = BitConverter.ToUInt16(d, p); p += 2; return v; }
            string Str() { int len = U16(); if (p + len > d.Length) throw new InvalidDataException("kurz"); string s = Encoding.UTF8.GetString(d, p, Math.Max(0, len - 1)); p += len; return s; }
            void Skip(int n) { if (p + n > d.Length) throw new InvalidDataException("kurz"); p += n; }
            U32();                 // Groesse
            U32();                 // Typ
            string name = Str();
            Str(); Str(); Str(); Str();   // Beschreibung, Version, Seriennummer, Ort
            int modes = U16();
            U32();                 // aktiver Modus
            for (int m = 0; m < modes; m++)
            {
                Str();             // Name
                Skip(4 * 9);       // Wert, Flags, Tempo min/max, Farben min/max, Tempo, Richtung, Farbmodus
                int nc = U16();
                Skip(nc * 4);
            }
            int zones = U16();
            for (int z = 0; z < zones; z++)
            {
                Str();             // Name
                Skip(4 * 4);       // Typ, LEDs min/max/Anzahl
                int ml = U16();
                if (ml > 0) Skip(ml);
            }
            int leds = U16();
            return new Device { Name = name, Leds = leds };
        }

        public void Dispose()
        {
            _run = false;
            _wake.Set();
            try { _thread.Join(1000); } catch { }
        }
    }
}
