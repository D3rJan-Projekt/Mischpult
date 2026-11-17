using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Mischpult.Native
{
    /// <summary>
    /// 4.0: WLED-Lichter (ESP32/ESP8266) im WLAN – Farben per UDP "Realtime" (DRGB, Port 21324). Hoert das Pult auf,
    /// zu senden, kehrt WLED nach 2 Sekunden von selbst zu seinem eigenen Effekt zurueck.
    /// </summary>
    internal sealed class WledSender : IDisposable
    {
        public const int Port = 21324;
        private UdpClient _udp;
        private IPEndPoint _ep;
        private string _host;
        private volatile bool _resolving;
        public string Error;

        public bool Ready => _ep != null;

        public void SetHost(string host)
        {
            if (string.Equals(host, _host, StringComparison.OrdinalIgnoreCase)) return;
            _host = host; _ep = null; Error = null;
            if (string.IsNullOrWhiteSpace(host)) return;
            if (IPAddress.TryParse(host.Trim(), out var ip)) { _ep = new IPEndPoint(ip, Port); return; }
            if (_resolving) return;
            _resolving = true;
            string h = host.Trim();
            Task.Run(() =>
            {
                try
                {
                    var addrs = Dns.GetHostAddresses(h);
                    foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { if (h == _host?.Trim()) _ep = new IPEndPoint(a, Port); break; }
                    if (_ep == null) Error = "Name nicht gefunden";
                }
                catch (Exception ex) { Error = ex.Message; }
                finally { _resolving = false; }
            });
        }

        /// <summary>Paket bauen: [2 = DRGB, Sekunden bis WLED wieder selbst leuchtet, R, G, B, R, G, B …].</summary>
        internal static byte[] Packet(byte[] rgb, int leds, int timeoutSec = 2)
        {
            leds = Math.Max(0, Math.Min(490, leds));
            var p = new byte[2 + leds * 3];
            p[0] = 2; p[1] = (byte)Math.Max(1, Math.Min(255, timeoutSec));
            Buffer.BlockCopy(rgb, 0, p, 2, Math.Min(rgb.Length, leds * 3));
            return p;
        }

        public void Send(byte[] rgb, int leds)
        {
            var ep = _ep;
            if (ep == null) return;
            try
            {
                if (_udp == null) _udp = new UdpClient();
                var p = Packet(rgb, leds);
                _udp.Send(p, p.Length, ep);
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        public void Dispose() { try { _udp?.Close(); } catch { } _udp = null; }
    }
}
