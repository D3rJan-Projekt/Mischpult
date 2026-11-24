using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Mischpult.Native
{
    /// <summary>
    /// 4.4: kleine Webseite im Heimnetz, damit Handy, Tablet oder ein anderer PC das Pult im Browser fernsteuern koennen.
    /// Nur Geraete aus dem eigenen Netz (private Adressen), nur mit Zugangscode, nach falschen Codes kurz gesperrt.
    /// Der Zustand wird im UI-Takt fertig gebaut (State), Befehle gehen per Command an das Pult.
    /// </summary>
    internal sealed class RemoteServer440 : IDisposable
    {
        public Func<string> State;
        public Func<string, Dictionary<string, string>, string> Command;
        public Func<string, byte[]> Icon;
        public byte[] AppIcon;
        public string Page = "";
        public volatile string Code;
        /// <summary>Ein Geraet hat sich zum ersten Mal (seit dem Start) mit richtigem Code gemeldet.</summary>
        public event Action<string> NewClient;
        /// <summary>4.4.1: Protokollzeile (fuer die Datei fernbedienung.log), aus dem Server-Thread.</summary>
        public event Action<string> Logged;

        /// <summary>4.4.1: die letzten Verbindungsversuche (fuer das Fenster „Fernbedienung“).</summary>
        public sealed class Entry { public DateTime When; public string Ip, Text; public bool Ok; }
        private readonly List<Entry> _recent = new List<Entry>();
        private readonly Dictionary<string, string> _lastText = new Dictionary<string, string>();

        public int Port { get; private set; }
        /// <summary>4.7: nur dieser PC (127.0.0.1) – fuer das Studio-Fenster, ohne Heimnetz und ohne Firewall-Frage.</summary>
        public bool LoopbackOnly;
        public string Error { get; private set; }
        public DateTime LastRequestUtc => new DateTime(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc);

        private TcpListener _listener;
        private volatile bool _stop;
        private long _lastTicks;
        private int _conns;
        private readonly ConcurrentDictionary<string, DateTime> _seen = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, Fail> _fails = new ConcurrentDictionary<string, Fail>();
        private sealed class Fail { public int N; public DateTime Until; }

        public const int MaxFails = 5;
        public static readonly TimeSpan Block = TimeSpan.FromSeconds(60);

        public RemoteServer440(int port) { Port = port; }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, Port);
                // 4.4.1: kein „exklusiv“ mehr – damit blieb der Port nach einem Neustart bis zu 4 Minuten belegt.
                // Unter Linux/Mono darf der Port gleich wieder benutzt werden (unter Windows bedeutet das etwas anderes).
                if (!Win32.IsWindows) try { _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); } catch { }
                _listener.Start(16);
                if (Port == 0) Port = ((IPEndPoint)_listener.LocalEndpoint).Port;   // 4.7: freier Port vom System
            }
            catch (SocketException ex)
            {
                Error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse || ex.SocketErrorCode == SocketError.AccessDenied
                    ? "Port " + Port + " ist schon belegt – bitte einen anderen Port wählen."
                    : "Die Fernbedienung konnte nicht starten: " + ex.Message;
                try { _listener?.Stop(); } catch { }
                _listener = null;
                return false;
            }
            catch (Exception ex)
            {
                Error = "Die Fernbedienung konnte nicht starten: " + ex.Message;
                _listener = null;
                return false;
            }
            new Thread(Loop) { IsBackground = true, Name = "Mischpult-Fernbedienung" }.Start();
            return true;
        }

        public void Dispose()
        {
            _stop = true;
            try { _listener?.Stop(); } catch { }
        }

        public List<Entry> Recent()
        {
            lock (_recent) return _recent.ToList();
        }

        /// <summary>Ereignis merken (dieselbe Meldung vom selben Geraet nur einmal hintereinander).</summary>
        private void Note(string ip, string text, bool ok)
        {
            lock (_recent)
            {
                if (_lastText.TryGetValue(ip, out var last) && last == text)
                {
                    var e = _recent.LastOrDefault(x => x.Ip == ip);
                    if (e != null) e.When = DateTime.Now;
                    return;
                }
                _lastText[ip] = text;
                _recent.Add(new Entry { When = DateTime.Now, Ip = ip, Text = text, Ok = ok });
                while (_recent.Count > 12) _recent.RemoveAt(0);
            }
            try { Logged?.Invoke(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + ip + "  " + text); } catch { }
        }

        /// <summary>Geraete (IP), die sich in der letzten Zeit gemeldet haben.</summary>
        public List<string> ClientsSince(TimeSpan span)
        {
            var now = DateTime.UtcNow;
            return _seen.Where(kv => now - kv.Value <= span).OrderBy(kv => kv.Key).Select(kv => kv.Key).ToList();
        }

        private void Loop()
        {
            while (!_stop)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { if (_stop) return; Thread.Sleep(200); continue; }
                if (Interlocked.Increment(ref _conns) > 32)
                {
                    Interlocked.Decrement(ref _conns);
                    try { c.Close(); } catch { }
                    continue;
                }
                // 4.4.1: eigener Faden pro Verbindung (nicht der ThreadPool) – Browser halten Verbindungen offen
                var t = new Thread(() =>
                {
                    try { Serve(c); }
                    catch { }
                    finally { Interlocked.Decrement(ref _conns); CloseGently(c); }
                }, 256 * 1024) { IsBackground = true, Name = "Mischpult-Fernbedienung-Verbindung" };
                try { t.Start(); } catch { Interlocked.Decrement(ref _conns); CloseGently(c); }
            }
        }

        /// <summary>Sauber schliessen (erst senden beenden), damit der Browser kein „Verbindung zurueckgesetzt“ sieht.</summary>
        private static void CloseGently(TcpClient c)
        {
            try { c.Client.Shutdown(SocketShutdown.Send); } catch { }
            try
            {
                // was noch ankommt, kurz abholen – sonst schickt Windows beim Schliessen ein RST
                c.Client.ReceiveTimeout = 300;
                var buf = new byte[1024];
                for (int i = 0; i < 16 && c.Client.Available > 0; i++) c.Client.Receive(buf);
            }
            catch { }
            try { c.Close(); } catch { }
        }

        // ================================================================ Netz

        private static List<KeyValuePair<uint, uint>> _subnets;
        private static DateTime _subnetsAt;

        /// <summary>4.4.1: auch Geraete aus demselben Teilnetz wie dieser PC zulassen (falls das Heimnetz ungewoehnliche Adressen nutzt).</summary>
        public static bool InLocalSubnet(IPAddress ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var list = _subnets;
            if (list == null || DateTime.UtcNow - _subnetsAt > TimeSpan.FromSeconds(60))
            {
                list = new List<KeyValuePair<uint, uint>>();
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                            IPAddress mask = null;
                            try { mask = ua.IPv4Mask; } catch { }
                            if (mask == null) continue;
                            uint m = U(mask);
                            if (m == 0 || m == 0xFFFFFFFF) continue;
                            list.Add(new KeyValuePair<uint, uint>(U(ua.Address) & m, m));
                        }
                    }
                }
                catch { }
                _subnets = list; _subnetsAt = DateTime.UtcNow;
            }
            uint a = U(ip);
            return list.Any(kv => (a & kv.Value) == kv.Key);
        }

        private static uint U(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 4 ? (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) : 0u;
        }

        /// <summary>Nur das eigene Netz: Loopback, 10.x, 172.16–31.x, 192.168.x, 169.254.x (IPv6: Link-lokal und fc00::/7).</summary>
        public static bool IsLocal(IPAddress ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = ip.GetAddressBytes();
                return ip.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
            }
            return false;
        }

        /// <summary>Host-Kopfzeile pruefen (gegen fremde Webseiten, die auf eine Adresse im Heimnetz umgebogen werden).</summary>
        public static bool HostOk(string host, string machine)
        {
            if (string.IsNullOrEmpty(host)) return true;
            string h = host.Trim();
            if (h.StartsWith("["))
            {
                int e = h.IndexOf(']');
                return e > 0 && IPAddress.TryParse(h.Substring(1, e - 1), out _);
            }
            int colon = h.LastIndexOf(':');
            if (colon > 0) h = h.Substring(0, colon);
            if (IPAddress.TryParse(h, out _)) return true;
            if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (h.IndexOf('.') < 0) return true;   // nur der Rechnername
            string first = h.Substring(0, h.IndexOf('.'));
            return !string.IsNullOrEmpty(machine) && first.Equals(machine, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adressen dieses PCs im Heimnetz (IPv4), die mit Router zuerst.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<KeyValuePair<string, int>>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); } catch { continue; }
                    bool gw = false;
                    try { gw = props.GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any)); } catch { }
                    string desc = ((ni.Description ?? "") + " " + (ni.Name ?? "")).ToLowerInvariant();
                    bool virt = desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("virtualbox") || desc.Contains("wsl") || desc.Contains("docker");
                    foreach (var ua in props.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || !IsLocal(ip)) continue;
                        var b = ip.GetAddressBytes();
                        int score = (gw ? 0 : 10) + (virt ? 20 : 0) + (b[0] == 192 ? 0 : b[0] == 10 ? 1 : b[0] == 172 ? 2 : 5);
                        list.Add(new KeyValuePair<string, int>(ip.ToString(), score));
                    }
                }
            }
            catch { }
            return list.OrderBy(kv => kv.Value).Select(kv => kv.Key).Distinct().ToList();
        }

        // ================================================================ HTTP

        private sealed class Req
        {
            public string Method, Path, Query;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public byte[] Body;
            public bool Close;
            public string H(string k) => Headers.TryGetValue(k, out var v) ? v : null;
        }

        private sealed class Resp
        {
            public int Status = 200;
            public string Type = "application/json; charset=utf-8";
            public byte[] Body = new byte[0];
            public bool Html;
        }

        private void Serve(TcpClient c)
        {
            var ep = c.Client.RemoteEndPoint as IPEndPoint;
            if (ep == null) return;
            var a = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address;
            string ip = a.ToString();
            bool local = IsLocal(a) || InLocalSubnet(a);
            c.NoDelay = true;
            c.SendTimeout = 10000;
            var s = c.GetStream();
            var pend = new List<byte>(2048);
            for (int k = 0; k < 1000 && !_stop; k++)
            {
                // erste Anfrage: Browser bauen Verbindungen oft vorab auf – laenger warten; danach 30 s Leerlauf
                c.ReceiveTimeout = !local ? 5000 : k == 0 ? 90000 : 30000;
                if (k == 0 && IsTls(s, pend)) { Note(ip, "HTTPS-Versuch – die Fernbedienung nutzt http:// (Browser versucht es gleich ohne)", false); return; }
                var r = ReadRequest(s, pend);
                if (r == null) { if (k == 0 && pend.Count > 0) Note(ip, "Unverständliche Anfrage", false); return; }
                if (!local)
                {
                    Note(ip, "abgelehnt – nicht im Heimnetz", false);
                    Write(s, Page403(ip), true);
                    return;
                }
                Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
                Resp resp;
                try { resp = Handle(r, ip); }
                catch { resp = Json(500, "{\"ok\":false,\"err\":\"Fehler im Pult\"}"); }
                if (r.Path == "/" || r.Path == "/index.html") Note(ip, "Seite geöffnet", true);
                else if (resp.Status == 401) Note(ip, "falscher Code", false);
                else if (resp.Status == 429) Note(ip, "gesperrt (zu viele falsche Codes)", false);
                else if (resp.Status == 403) Note(ip, "abgelehnt – falsche Adresse (" + (r.H("Host") ?? "?") + ")", false);
                else if (resp.Status == 200 && r.Path == "/api/state") Note(ip, "verbunden", true);
                Write(s, resp, r.Close);
                if (r.Close) return;
            }
        }

        /// <summary>Beginnt die Verbindung mit einem TLS-Handschlag (https://)? Liest dafuer das erste Byte.</summary>
        private static bool IsTls(NetworkStream s, List<byte> pend)
        {
            var one = new byte[1];
            int n;
            try { n = s.Read(one, 0, 1); } catch { return false; }
            if (n <= 0) return false;
            pend.Add(one[0]);
            return one[0] == 0x16;
        }

        private static Resp Page403(string ip) => new Resp
        {
            Status = 403, Type = "text/html; charset=utf-8", Html = true,
            Body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>Mischpult</title>" +
                "<body style=\"font:16px system-ui;background:#121418;color:#eef1f5;padding:24px\"><h2>Nur im Heimnetz</h2><p>Die Fernbedienung des Mischpults antwortet nur Geräten aus dem eigenen WLAN. " +
                "Dieses Gerät kommt von " + WebUtility.HtmlEncode(ip) + ".</p><p>Ist das Handy im selben WLAN wie der PC (nicht im Gäste-WLAN) und ist kein VPN an?</p></body>"),
        };

        private static int HeaderEnd(List<byte> b)
        {
            for (int i = 3; i < b.Count; i++)
                if (b[i] == '\n' && b[i - 1] == '\r' && b[i - 2] == '\n' && b[i - 3] == '\r') return i - 3;
            return -1;
        }

        private static Req ReadRequest(NetworkStream s, List<byte> pend)
        {
            var tmp = new byte[4096];
            int end;
            while ((end = HeaderEnd(pend)) < 0)
            {
                if (pend.Count > 8192) return null;
                int n;
                try { n = s.Read(tmp, 0, tmp.Length); } catch { return null; }
                if (n <= 0) return null;
                for (int i = 0; i < n; i++) pend.Add(tmp[i]);
            }
            string head = Encoding.ASCII.GetString(pend.GetRange(0, end).ToArray());
            pend.RemoveRange(0, end + 4);
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 3) return null;
            var r = new Req { Method = first[0].ToUpperInvariant() };
            string target = first[1];
            int q = target.IndexOf('?');
            r.Path = q >= 0 ? target.Substring(0, q) : target;
            r.Query = q >= 0 ? target.Substring(q + 1) : "";
            for (int i = 1; i < lines.Length; i++)
            {
                int k = lines[i].IndexOf(':');
                if (k > 0) r.Headers[lines[i].Substring(0, k).Trim()] = lines[i].Substring(k + 1).Trim();
            }
            string conn = r.H("Connection") ?? "";
            r.Close = first[2] == "HTTP/1.0" ? !conn.Equals("keep-alive", StringComparison.OrdinalIgnoreCase) : conn.Equals("close", StringComparison.OrdinalIgnoreCase);
            int len = 0;
            if (r.H("Content-Length") != null && !int.TryParse(r.H("Content-Length"), out len)) return null;
            if (len < 0 || len > 4096) return null;
            while (pend.Count < len)
            {
                int n;
                try { n = s.Read(tmp, 0, tmp.Length); } catch { return null; }
                if (n <= 0) return null;
                for (int i = 0; i < n; i++) pend.Add(tmp[i]);
            }
            r.Body = pend.GetRange(0, len).ToArray();
            pend.RemoveRange(0, len);
            return r;
        }

        private static readonly Dictionary<int, string> Reasons = new Dictionary<int, string>
        {
            { 200, "OK" }, { 204, "No Content" }, { 400, "Bad Request" }, { 401, "Unauthorized" }, { 403, "Forbidden" }, { 404, "Not Found" },
            { 405, "Method Not Allowed" }, { 429, "Too Many Requests" }, { 500, "Internal Server Error" },
        };

        private static void Write(NetworkStream s, Resp r, bool close)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(r.Status).Append(' ').Append(Reasons.TryGetValue(r.Status, out var why) ? why : "OK").Append("\r\n");
            sb.Append("Content-Type: ").Append(r.Type).Append("\r\n");
            sb.Append("Content-Length: ").Append(r.Body.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\n");
            if (r.Html) sb.Append("Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'\r\n");
            sb.Append("Connection: ").Append(close ? "close" : "keep-alive").Append("\r\n\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            try
            {
                s.Write(head, 0, head.Length);
                if (r.Body.Length > 0) s.Write(r.Body, 0, r.Body.Length);
                s.Flush();
            }
            catch { }
        }

        private static Resp Json(int status, string json) => new Resp { Status = status, Body = Encoding.UTF8.GetBytes(json) };

        private Resp Handle(Req r, string ip)
        {
            if (!HostOk(r.H("Host"), Environment.MachineName)) return Json(403, "{\"ok\":false,\"err\":\"Bitte die Adresse des PCs benutzen (z. B. 192.168.178.20).\"}");
            switch (r.Path)
            {
                case "/":
                case "/index.html":
                    return new Resp { Type = "text/html; charset=utf-8", Body = Encoding.UTF8.GetBytes(Page ?? ""), Html = true };
                case "/manifest.json":
                    return new Resp
                    {
                        Type = "application/manifest+json; charset=utf-8",
                        Body = Encoding.UTF8.GetBytes("{\"name\":\"Mischpult\",\"short_name\":\"Mischpult\",\"start_url\":\"/\",\"display\":\"standalone\",\"background_color\":\"#14161a\",\"theme_color\":\"#14161a\",\"icons\":[{\"src\":\"/app.png\",\"sizes\":\"256x256\",\"type\":\"image/png\"}]}"),
                    };
                case "/app.png":
                    return AppIcon != null ? new Resp { Type = "image/png", Body = AppIcon } : new Resp { Status = 404 };
                case "/favicon.ico":
                    return new Resp { Status = 404 };
            }
            if (!r.Path.StartsWith("/api/")) return Json(404, "{\"ok\":false}");
            // Zugangscode
            string code = r.H("X-Code") ?? Arg(ParseQuery(r.Query), "c");
            var fail = _fails.GetOrAdd(ip, _ => new Fail());
            lock (fail)
            {
                if (fail.Until > DateTime.UtcNow) return Json(429, "{\"ok\":false,\"err\":\"wait\"}");
                if (!CodeOk(code))
                {
                    if (++fail.N >= MaxFails) { fail.N = 0; fail.Until = DateTime.UtcNow + Block; }
                    return Json(401, "{\"ok\":false,\"err\":\"code\"}");
                }
                fail.N = 0;
            }
            bool first = !_seen.ContainsKey(ip);
            _seen[ip] = DateTime.UtcNow;
            if (first) { try { NewClient?.Invoke(ip); } catch { } }
            switch (r.Path)
            {
                case "/api/state":
                    return Json(200, State?.Invoke() ?? "{}");
                case "/api/cmd":
                {
                    if (r.Method != "POST") return Json(405, "{\"ok\":false}");
                    var args = ParseQuery(r.Query);
                    if (r.Body != null && r.Body.Length > 0)
                        foreach (var kv in ParseQuery(Encoding.UTF8.GetString(r.Body))) args[kv.Key] = kv.Value;
                    string name = Arg(args, "do");
                    if (string.IsNullOrEmpty(name)) return Json(400, "{\"ok\":false}");
                    return Json(200, Command?.Invoke(name, args) ?? "{\"ok\":false}");
                }
                case "/api/icon":
                {
                    var png = Icon?.Invoke(Arg(ParseQuery(r.Query), "k"));
                    return png != null ? new Resp { Type = "image/png", Body = png } : Json(404, "{\"ok\":false}");
                }
            }
            return Json(404, "{\"ok\":false}");
        }

        /// <summary>Code vergleichen, ohne dass die Laufzeit verraet, wie viele Ziffern stimmen.</summary>
        private bool CodeOk(string given)
        {
            string want = Code;
            if (string.IsNullOrEmpty(want) || given == null) return false;
            int diff = want.Length ^ given.Length;
            for (int i = 0; i < want.Length; i++) diff |= want[i] ^ (i < given.Length ? given[i] : 0);
            return diff == 0;
        }

        private static string Arg(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;

        public static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(q)) return d;
            foreach (var part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                int e = part.IndexOf('=');
                string k = e >= 0 ? part.Substring(0, e) : part, v = e >= 0 ? part.Substring(e + 1) : "";
                try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
            }
            return d;
        }
    }
}
