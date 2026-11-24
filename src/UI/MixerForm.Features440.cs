using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.4 – Fernbedienung im Browser: Handy, Tablet oder ein anderer PC im selben WLAN oeffnen eine Adresse und regeln
    /// Kanaele, Apps, Ausgang, Szenen und DUCKING. Ein Schalter (Standard aus), nur im Heimnetz, nur mit Zugangscode.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private RemoteServer440 _remote;
        private volatile string _remoteState = "{}";
        private volatile bool _remoteLocked;
        private double _nextRemoteState, _nextRemoteCheck, _remoteNow, _remoteRetryAt;
        private string _remoteErr, _remoteFailedSig;
        private readonly ConcurrentDictionary<string, byte[]> _remoteIcons = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private IconCache _remoteIconCache;
        private RemoteWindow440 _remoteWin;
        private static string _remotePage;
        private static byte[] _remoteAppIcon;

        // ================================================================ Server an/aus

        private void TickRemote440(double now)
        {
            _remoteNow = now;
            if (now >= _nextRemoteCheck) { _nextRemoteCheck = now + 1.0; SyncRemote440(); }
            if (_settings.RemoteOn && now >= _nextFw440) { _nextFw440 = now + 15.0; CheckFirewall440(); }
            if (_remote == null) return;
            _remoteLocked = PultLockedNow;
            if ((DateTime.UtcNow - _remote.LastRequestUtc).TotalSeconds > 6) return;   // nur rechnen, wenn gerade jemand zuschaut
            if (now < _nextRemoteState) return;
            _nextRemoteState = now + 0.1;
            try { _remoteState = BuildRemoteState440(); } catch { }
        }

        private void SyncRemote440()
        {
            if (!_settings.RemoteOn)
            {
                if (_remote != null) StopRemote440();
                _remoteErr = null; _remoteFailedSig = null;
                return;
            }
            if (!MixerSettings.ValidRemoteCode(_settings.RemoteCode)) { _settings.RemoteCode = MixerSettings.NewRemoteCode(); ScheduleSave(); }
            if (_remote != null)
            {
                if (_remote.Port == _settings.RemotePort) { _remote.Code = _settings.RemoteCode; return; }
                StopRemote440();
            }
            string sig = _settings.RemotePort.ToString(CultureInfo.InvariantCulture);
            if (sig == _remoteFailedSig && _remoteNow < _remoteRetryAt) return;   // 4.4.1: nach einem Fehler alle 10 s neu versuchen
            var srv = new RemoteServer440(_settings.RemotePort)
            {
                Page = RemotePage440(), AppIcon = RemoteAppIcon440(), Code = _settings.RemoteCode,
                State = () => _remoteState, Command = RemoteCommand440, Icon = RemoteIcon440,
            };
            srv.Logged += RemoteLog440;
            srv.NewClient += ip => { try { BeginInvoke(new Action(() => ShowOsd("Fernbedienung", "Neues Gerät verbunden (" + ip + ")", -1, Theme.LedGreen))); } catch { } };
            if (!srv.Start()) { _remoteErr = srv.Error; _remoteFailedSig = sig; _remoteRetryAt = _remoteNow + 10.0; srv.Dispose(); return; }
            _remote = srv;
            _remoteErr = null; _remoteFailedSig = null;
            _nextRemoteState = 0;
        }

        private void StopRemote440()
        {
            try { _remote?.Dispose(); } catch { }
            _remote = null;
            _remoteState = "{}";
        }

        private static string RemotePage440()
        {
            if (_remotePage != null) return _remotePage;
            try
            {
                using (var s = typeof(MixerForm).Assembly.GetManifestResourceStream("Mischpult.remote.html"))
                using (var r = new StreamReader(s, Encoding.UTF8)) _remotePage = r.ReadToEnd();
            }
            catch { _remotePage = "<!doctype html><meta charset=utf-8><p>Mischpult</p>"; }
            return _remotePage;
        }

        private static byte[] RemoteAppIcon440()
        {
            if (_remoteAppIcon != null) return _remoteAppIcon;
            try
            {
                using (var s = typeof(MixerForm).Assembly.GetManifestResourceStream("Mischpult.app.ico"))
                using (var ico = new Icon(s, 256, 256))
                using (var bmp = ico.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    _remoteAppIcon = ms.ToArray();
                }
            }
            catch { _remoteAppIcon = null; }
            if (_remoteAppIcon == null)
                try
                {
                    // Ersatz: drei Fader auf dunklem Grund (wie oben auf der Handy-Seite)
                    using (var bmp = new Bitmap(256, 256))
                    using (var g = Graphics.FromImage(bmp))
                    using (var ms = new MemoryStream())
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(Color.FromArgb(0x12, 0x14, 0x18));
                        var caps = new[] { Color.FromArgb(0xff, 0x5a, 0x5a), Color.FromArgb(0x3d, 0xdc, 0x84), Color.FromArgb(0xff, 0xb3, 0x47) };
                        var ys = new[] { 120, 64, 150 };
                        for (int i = 0; i < 3; i++)
                        {
                            int x = 64 + i * 64;
                            using (var b = new SolidBrush(Color.FromArgb(0x6c, 0x74, 0x80))) g.FillRectangle(b, x - 5, 40, 10, 176);
                            using (var b = new SolidBrush(caps[i])) g.FillRectangle(b, x - 22, ys[i], 44, 26);
                        }
                        bmp.Save(ms, ImageFormat.Png);
                        _remoteAppIcon = ms.ToArray();
                    }
                }
                catch { }
            return _remoteAppIcon;
        }

        /// <summary>Adresse fuer Handy und Co. (erste Adresse im Heimnetz), null = kein Netz.</summary>
        private string RemoteUrl440()
        {
            var ips = RemoteServer440.LocalAddresses();
            string demoIp = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_REMOTEIP") : null;   // nur Vorschau
            if (!string.IsNullOrEmpty(demoIp)) ips.Insert(0, demoIp);
            return ips.Count == 0 ? null : "http://" + ips[0] + ":" + _settings.RemotePort.ToString(CultureInfo.InvariantCulture);
        }

        private string RemoteStatus440()
        {
            if (!_settings.RemoteOn) return "Aus.";
            if (_remoteErr != null) return _remoteErr;
            if (_remote == null) return "Startet …";
            var cl = _remote.ClientsSince(TimeSpan.FromSeconds(10));
            return cl.Count == 0 ? "Bereit – noch kein Gerät verbunden." : (cl.Count == 1 ? "Verbunden: " : cl.Count + " Geräte verbunden: ") + string.Join(", ", cl);
        }

        // ================================================================ 4.4.1 Protokoll und Firewall

        private static readonly object RemoteLogLock = new object();

        /// <summary>Verbindungsversuche in Dokumente\Mischpult\Protokolle\fernbedienung.log (klein gehalten).</summary>
        private static void RemoteLog440(string line)
        {
            try
            {
                lock (RemoteLogLock)
                {
                    Directory.CreateDirectory(MixerSettings.LogFolder);
                    string f = Path.Combine(MixerSettings.LogFolder, "fernbedienung.log");
                    if (File.Exists(f) && new FileInfo(f).Length > 200 * 1024) { File.Delete(f + ".alt"); File.Move(f, f + ".alt"); }
                    File.AppendAllText(f, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        private double _nextFw440;
        private volatile int _fwState440 = (int)Firewall440.State.Unknown, _fwProfile440;
        private bool _fwBusy440;

        private void CheckFirewall440()
        {
            if (_fwBusy440 || !Win32.IsWindows) return;
            _fwBusy440 = true;
            string exe = Application.ExecutablePath;
            int port = _settings.RemotePort;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var st = Firewall440.Check(exe, port, out int prof);
                    _fwState440 = (int)st; _fwProfile440 = prof;
                }
                catch { }
                finally { _fwBusy440 = false; }
            });
        }

        private bool FirewallFixable440 => _fwState440 == (int)Firewall440.State.Blocked || _fwState440 == (int)Firewall440.State.NoRule;

        private string FirewallText440()
        {
            string prof = Firewall440.ProfileName(_fwProfile440);
            string net = prof == null ? "" : " Das Netzwerk ist als „" + prof + "“ eingestellt.";
            switch ((Firewall440.State)_fwState440)
            {
                case Firewall440.State.Allowed: return "Windows-Firewall: Fernbedienung erlaubt." + net;
                case Firewall440.State.Off: return "Windows-Firewall: aus.";
                case Firewall440.State.Blocked: return "Windows-Firewall blockiert das Pult." + net + " Unten „Firewall freigeben“ klicken.";
                case Firewall440.State.NoRule: return "Windows-Firewall: noch keine Freigabe." + net + " Unten „Firewall freigeben“ klicken.";
                default: return null;
            }
        }

        private void FixFirewall440()
        {
            int port = _settings.RemotePort;
            ShowOsd("Fernbedienung", "Windows fragt gleich nach Admin-Rechten …", -1, Theme.Accent);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = Installer.RunElevated("--admin firewall --port " + port.ToString(CultureInfo.InvariantCulture));
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (err == null) ShowOsd("Fernbedienung", "Firewall freigegeben – nur fürs Heimnetz", -1, Theme.LedGreen);
                        else if (err != Installer.Cancelled) ShowOsd("Fernbedienung", err, -1, Theme.MuteRed);
                        _nextFw440 = 0;
                        _remoteWin?.Invalidate();
                    }));
                }
                catch { }
            });
        }

        // ================================================================ Zustand (im UI-Takt gebaut)

        private static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == '&' || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static string F4(float v) => (float.IsNaN(v) || float.IsInfinity(v) ? 0f : v).ToString("0.####", CultureInfo.InvariantCulture);
        private static string Hex(Color c) => "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");

        /// <summary>Wer gerade das Ducking ausloest (auch ohne den Schalter „DUCKING-Taste zeigt, wer auslöst“).</summary>
        private string RemoteDuckWho440(List<string> src)
        {
            if (src.Count > 0)
            {
                float db = _duckApplied < -0.25f ? _duckApplied : 0f;
                if (db == 0f && _ruleCur.Length > 0) db = Math.Min(0f, _ruleCur.Min());
                var names = src.Select(DuckTriggerName).ToList();
                string who = names.Count > 2 ? names[0] + " +" + (names.Count - 1) : string.Join("+", names);
                return who + (db < -0.25f ? " " + ((int)Math.Round(db)).ToString(CultureInfo.InvariantCulture).Replace("-", "−") : "");
            }
            return _duckWaits430 ? "WARTET" : null;
        }

        internal string BuildRemoteState440()
        {
            var sb = new StringBuilder(4096);
            bool locked = PultLockedNow;
            sb.Append("{\"v\":1,\"ver\":").Append(J(Program.Version)).Append(",\"pc\":").Append(J(Environment.MachineName))
              .Append(",\"locked\":").Append(locked ? "true" : "false").Append(",\"law\":[");
            var law = FaderLaw.CurvePos;
            for (int i = 0; i < law.Length; i++) { if (i > 0) sb.Append(','); sb.Append(F4(law[i])); }
            sb.Append("],\"rows\":[");
            var src = _settings.DuckOn ? DuckSources430(_remoteNow) : new List<string>();
            var rows = MiniRows().Where(r => r.Id != "mic").ToList();
            RemoteOutputs451(rows);   // 4.5.1: alle Ausgaenge
            var ordered = rows.Where(r => r.Id == "master").Concat(rows.Where(r => r.Id.StartsWith("out:"))).Concat(rows.Where(r => r.Id.StartsWith("ch:"))).Concat(rows.Where(r => r.Id.StartsWith("grp:"))).Concat(rows.Where(r => r.Id == "stream")).ToList();
            bool firstRow = true;
            foreach (var r in ordered)
            {
                if (!firstRow) sb.Append(',');
                firstRow = false;
                string badge = r.Badge;
                ChannelStrip strip = null;
                if (r.Id.StartsWith("ch:"))
                {
                    strip = _channelStrips.FirstOrDefault(s => "ch:" + s.Config.Id == r.Id);
                    if (strip != null && src.Contains(strip.Config.Id)) badge = "LÖST AUS";
                }
                sb.Append("{\"id\":").Append(J(r.Id)).Append(",\"name\":").Append(J(r.Name)).Append(",\"color\":").Append(J(Hex(r.Color)))
                  .Append(",\"pos\":").Append(F4(r.Pos)).Append(",\"mute\":").Append(r.Mute ? "true" : "false")
                  .Append(",\"pk\":").Append(F4(Math.Max(r.PeakL, r.PeakR))).Append(",\"val\":").Append(J(r.Value)).Append(",\"badge\":").Append(J(badge))
                  .Append(",\"pl\":").Append(F4(r.PeakL)).Append(",\"pr\":").Append(F4(r.PeakR));   // 4.5.1: Pegel links/rechts
                if (r.Sub != null) sb.Append(",\"sub\":").Append(J(r.Sub));
                if (strip != null) { AppendStrip450(sb, strip.Config); AppendStrip460(sb, strip.Config); }   // 4.5/4.6: EQ, Balance, SOLO, COMP, AUTO, Stream
                if (strip != null && !_settings.RemoteAppsOff)
                {
                    sb.Append(",\"apps\":[");
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool firstApp = true;
                    foreach (var a in strip.Apps.OrderByDescending(x => x.Running).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(a.Key) || !seen.Add(a.Key)) continue;
                        if (!firstApp) sb.Append(',');
                        firstApp = false;
                        sb.Append("{\"k\":").Append(J(a.Key)).Append(",\"n\":").Append(J(string.IsNullOrEmpty(a.Name) ? a.Key : a.Name))
                          .Append(",\"pos\":").Append(F4(AppPos(a.Key))).Append(",\"run\":").Append(a.Running ? "true" : "false").Append('}');
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append(']');
            AppendGlobal450(sb);
            AppendGlobal460(sb);
            if (!_settings.RemoteScenesOff)
            {
                sb.Append(",\"scenes\":[");
                bool firstScene = true;
                foreach (var sc in _settings.Scenes)
                {
                    if (sc == null || string.IsNullOrEmpty(sc.Id)) continue;
                    if (!firstScene) sb.Append(',');
                    firstScene = false;
                    sb.Append("{\"id\":").Append(J(sc.Id)).Append(",\"name\":").Append(J(sc.Name ?? "Szene")).Append('}');
                }
                sb.Append("],\"duck\":{\"on\":").Append(_settings.DuckOn ? "true" : "false").Append(",\"active\":").Append(_duckActive ? "true" : "false")
                  .Append(",\"who\":").Append(J(_settings.DuckOn ? RemoteDuckWho440(src) : null)).Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ================================================================ Befehle (vom Server-Thread)

        private static bool RemoteRowId(string id) => id == "master" || id == "stream" || (id != null && (id.StartsWith("ch:") || id.StartsWith("grp:")));

        private string RemoteCommand440(string name, Dictionary<string, string> a)
        {
            if (_remoteLocked) return "{\"ok\":false,\"err\":\"locked\"}";
            a.TryGetValue("id", out string id);
            a.TryGetValue("k", out string key);
            float v = float.NaN, raw = float.NaN;
            if (a.TryGetValue("v", out string vs) && float.TryParse(vs, NumberStyles.Float, CultureInfo.InvariantCulture, out float pv) && !float.IsNaN(pv) && !float.IsInfinity(pv))
            {
                raw = pv;
                v = Math.Max(0f, Math.Min(1f, pv));
            }
            Action act = RemoteCommand450(name, id, raw, a);   // 4.5: EQ, Balance, SOLO, COMP
            if (act == null)
            switch (name)
            {
                case "pos": if (RemoteRowId(id) && !float.IsNaN(v)) act = () => MiniSetLevel(id, v); break;
                case "mute": if (RemoteRowId(id) && !float.IsNaN(v)) act = () => RemoteMute440(id, v >= 0.5f); break;
                case "app": if (!string.IsNullOrEmpty(key) && !float.IsNaN(v)) act = () => RemoteApp440(key, v); break;
                case "scene": if (!string.IsNullOrEmpty(id)) act = () => RemoteScene440(id); break;
                case "duck": if (!float.IsNaN(v)) act = () => { if (!_settings.RemoteScenesOff && _settings.DuckOn != (v >= 0.5f)) ToggleDuck(true); }; break;
            }
            if (act == null) return "{\"ok\":false}";
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!_settings.RemoteOn || PultLockedNow) return;
                    try { act(); } catch { }
                    _nextRemoteState = 0;   // gleich neu melden
                }));
            }
            catch { return "{\"ok\":false}"; }
            return "{\"ok\":true}";
        }

        private void RemoteMute440(string id, bool on)
        {
            var row = MiniRows().FirstOrDefault(r => r.Id == id);
            if (row != null && row.Mute != on) MiniToggleMute(id);
        }

        private void RemoteApp440(string key, float v)
        {
            if (_settings.RemoteAppsOff) return;
            if (!_channelStrips.Any(s => s.Apps.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))) return;   // nur Apps aus den Kanaelen
            SetAppPos(key, v);
        }

        private void RemoteScene440(string id)
        {
            if (_settings.RemoteScenesOff) return;
            var sc = _settings.Scenes.FirstOrDefault(x => x != null && x.Id == id);
            if (sc != null) ApplyScene(sc, "Szene");
        }

        /// <summary>App-Symbol als PNG (im UI-Thread erzeugt, danach aus dem Zwischenspeicher).</summary>
        private byte[] RemoteIcon440(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 200) return null;
            if (_remoteIcons.TryGetValue(key, out var png)) return png;
            try
            {
                png = (byte[])Invoke(new Func<byte[]>(() =>
                {
                    var app = _channelStrips.SelectMany(s => s.Apps).FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (app == null) return null;
                    if (_remoteIconCache == null) _remoteIconCache = new IconCache(48);
                    var e = _remoteIconCache.Get(app.Key, app.Name, app.Path);
                    using (var ms = new MemoryStream()) { e.Normal.Save(ms, ImageFormat.Png); return ms.ToArray(); }
                }));
            }
            catch { png = null; }
            if (png != null && _remoteIcons.Count < 300) _remoteIcons[key] = png;
            return png;
        }

        // ================================================================ Einstellungen und Fenster

        private void AddRemote440(SPage p)
        {
            p.Section("Fernbedienung (Handy, Tablet, anderer PC)");
            p.Toggle("Fernbedienung im Browser", () => "Handy, Tablet oder ein anderer PC im selben WLAN öffnen eine Adresse im Browser (keine App nötig) und regeln dort Kanäle, Apps, Ausgang, Szenen und DUCKING. " +
                    "Nur im eigenen Heimnetz und nur mit Zugangscode. Beim ersten Einschalten fragt die Windows-Firewall einmal – dort „Private Netzwerke“ erlauben.",
                () => _settings.RemoteOn, v =>
                {
                    _settings.RemoteOn = v;
                    if (v && !MixerSettings.ValidRemoteCode(_settings.RemoteCode)) _settings.RemoteCode = MixerSettings.NewRemoteCode();
                    _remoteFailedSig = null;
                    SyncRemote440();
                    ScheduleSave();
                    if (v) BeginInvoke(new Action(ShowRemoteWindow440));
                });
            p.Info("Adresse und Code", () =>
            {
                string url = RemoteUrl440();
                return (url != null ? "Im Browser öffnen: " + url : "Kein Heimnetz gefunden – ist der PC mit dem WLAN oder per Kabel verbunden?") +
                       "  ·  Code: " + FormatCode440(_settings.RemoteCode) + "  ·  " + RemoteStatus440();
            }).Visible = () => _settings.RemoteOn;
            p.Button("Handy verbinden", "Zeigt einen QR-Code: mit der Handy-Kamera scannen – fertig, auch der Code ist dann schon drin.", "QR-Code zeigen …", r => ShowRemoteWindow440())
                .Visible = () => _settings.RemoteOn;
            p.Button("Neuer Zugangscode", "Alle verbundenen Geräte müssen danach den neuen Code eingeben (oder den QR-Code neu scannen).", "Neuer Code", r => NewRemoteCode440(),
                () => FormatCode440(_settings.RemoteCode)).Visible = () => _settings.RemoteOn;
            p.Button("Port", "Nur ändern, wenn ein anderes Programm denselben Port benutzt. Standard ist 8765.", "Ändern …", r => AskRemotePort440(),
                () => _settings.RemotePort.ToString(CultureInfo.InvariantCulture)).Visible = () => _settings.RemoteOn;
            p.Toggle("Szenen und DUCKING am Handy", "Am Handy oben die DUCKING-Taste (mit „wer löst aus“) und die Szenen zum Antippen.",
                () => !_settings.RemoteScenesOff, v => { _settings.RemoteScenesOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Lautstärke einzelner Apps am Handy", "Unter jedem Kanal lassen sich die Apps aufklappen und einzeln lauter oder leiser stellen.",
                () => !_settings.RemoteAppsOff, v => { _settings.RemoteAppsOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            AddRemote450(p);   // 4.5: EQ, Balance, SOLO und COMP am Tablet
        }

        private static string FormatCode440(string c) => MixerSettings.ValidRemoteCode(c) ? c.Substring(0, 3) + " " + c.Substring(3) : "–";

        private void NewRemoteCode440()
        {
            _settings.RemoteCode = MixerSettings.NewRemoteCode();
            if (_remote != null) _remote.Code = _settings.RemoteCode;
            ScheduleSave();
            _remoteWin?.Invalidate();
            ShowOsd("Fernbedienung", "Neuer Code: " + FormatCode440(_settings.RemoteCode), -1, Theme.Accent);
        }

        private void AskRemotePort440()
        {
            using (var d = new InputDialog("Fernbedienung", "Port (1024 bis 65535, Standard 8765):", _settings.RemotePort.ToString(CultureInfo.InvariantCulture), 5))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!int.TryParse(d.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 1024 || port > 65535)
                {
                    ShowOsd("Fernbedienung", "Ungültiger Port", -1, Theme.MuteRed);
                    return;
                }
                _settings.RemotePort = port;
                _remoteFailedSig = null;
                SyncRemote440();
                _remoteWin?.Invalidate();
                ScheduleSave();
            }
        }

        private void ShowRemoteWindow440()
        {
            if (_remoteWin != null && !_remoteWin.IsDisposed) { _remoteWin.Activate(); return; }
            var w = new RemoteWindow440
            {
                TopMost = TopMost,
                Url = RemoteUrl440, Code = () => _settings.RemoteCode, Status = RemoteStatus440,
                NewCode = NewRemoteCode440, On = () => _settings.RemoteOn,
                Recent = () => _remote?.Recent() ?? new List<RemoteServer440.Entry>(),
                Firewall = FirewallText440, FirewallFixable = () => FirewallFixable440, FixFirewall = FixFirewall440,
                TestHere = () => OpenUri("http://localhost:" + _settings.RemotePort.ToString(CultureInfo.InvariantCulture) + "/#c=" + _settings.RemoteCode),
            };
            _nextFw440 = 0;
            w.FormClosed += (s, e) => { if (_remoteWin == w) _remoteWin = null; };
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            _remoteWin = w;
            w.Show(this);
        }

        private void DemoShown440()
        {
            string rc = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_REMOTECODE");
            if (MixerSettings.ValidRemoteCode(rc)) { _settings.RemoteCode = rc; if (_remote != null) _remote.Code = rc; }
            if (DemoOn("REMOTEWIN")) DemoLater(() => { SyncRemote440(); ShowRemoteWindow440(); });
        }
    }

    /// <summary>4.4: QR-Code, Adresse und Zugangscode zum Verbinden von Handy und Co. 4.4.1: Firewall und letzte Verbindungen.</summary>
    internal sealed class RemoteWindow440 : PultWin
    {
        public Func<string> Url, Code, Status, Firewall;
        public Func<bool> On, FirewallFixable;
        public Func<List<RemoteServer440.Entry>> Recent;
        public Action NewCode, FixFirewall, TestHere;
        private bool[,] _qr;
        private string _qrFor;
        private RectangleF _newR, _copyR, _fwR, _testR;
        private readonly Timer _timer = new Timer { Interval = 1000 };

        public RemoteWindow440() : base("Fernbedienung", 660, 470)
        {
            _timer.Tick += (s, e) => Invalidate();
            _timer.Start();
            FormClosed += (s, e) => _timer.Dispose();
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            string url = Url?.Invoke(), code = Code?.Invoke();
            string full = url != null && code != null ? url + "/#c=" + code : null;
            if (full != _qrFor) { _qrFor = full; _qr = full != null ? Qr440.Encode(full) : null; }
            float pad = Theme.F(8);
            var qrR = new RectangleF(r.X + pad, r.Y + pad, Theme.F(250), Theme.F(250));
            if (_qr != null) Qr440.Draw(g, _qr, qrR);
            else
            {
                Theme.FillRound(g, Theme.Well, qrR, Theme.F(8));
                Label(g, "Kein Heimnetz gefunden", qrR, Theme.TextDim, 11f, false, Theme.Center);
            }
            Label(g, "Mit der Handy-Kamera scannen", new RectangleF(qrR.X, qrR.Bottom + Theme.F(6), qrR.Width, Theme.F(20)), Theme.TextDim, 10f, false, Theme.Center);
            _testR = new RectangleF(qrR.X + Theme.F(35), qrR.Bottom + Theme.F(32), qrR.Width - Theme.F(70), Theme.F(28));
            Btn(g, _testR, "Am PC testen", Hover == "#test");

            float x = qrR.Right + Theme.F(22), w = r.Right - x - pad, y = r.Y + pad;
            Label(g, "So geht's", new RectangleF(x, y, w, Theme.F(22)), Theme.Text, 13f, true);
            y += Theme.F(28);
            string[] steps =
            {
                "Handy, Tablet oder anderen PC ins selbe WLAN wie diesen PC (nicht ins Gäste-WLAN, VPN aus).",
                "QR-Code scannen – oder im Browser diese Adresse öffnen:",
            };
            for (int i = 0; i < steps.Length; i++)
            {
                Theme.FillEllipse(g, Theme.Accent, x + Theme.F(9), y + Theme.F(9), Theme.F(9));
                Label(g, (i + 1).ToString(CultureInfo.InvariantCulture), new RectangleF(x, y, Theme.F(18), Theme.F(18)), Color.FromArgb(0x1a, 0x14, 0x0a), 9.5f, true, Theme.Center);
                Label(g, steps[i], new RectangleF(x + Theme.F(26), y - Theme.F(1), w - Theme.F(26), Theme.F(36)), Theme.TextDim, 10.5f, false, Theme.Wrap);
                y += Theme.F(38);
            }
            using (var b = new SolidBrush(Theme.Accent)) Theme.Str(g, url ?? "–", Theme.Mono(13.5f), b, new RectangleF(x + Theme.F(26), y - Theme.F(6), w, Theme.F(24)), Theme.Left);
            y += Theme.F(30);
            Theme.FillEllipse(g, Theme.Accent, x + Theme.F(9), y + Theme.F(9), Theme.F(9));
            Label(g, "3", new RectangleF(x, y, Theme.F(18), Theme.F(18)), Color.FromArgb(0x1a, 0x14, 0x0a), 9.5f, true, Theme.Center);
            Label(g, "Zugangscode (nur beim ersten Mal):", new RectangleF(x + Theme.F(26), y - Theme.F(1), w - Theme.F(26), Theme.F(20)), Theme.TextDim, 10.5f, false, Theme.Left);
            y += Theme.F(22);
            string cd = MixerSettings.ValidRemoteCode(code) ? code.Substring(0, 3) + " " + code.Substring(3) : "–";
            using (var b = new SolidBrush(Theme.Text)) Theme.Str(g, cd, Theme.Mono(22f), b, new RectangleF(x + Theme.F(26), y, w, Theme.F(34)), Theme.Left);
            y += Theme.F(42);
            string st = On != null && !On() ? "Die Fernbedienung ist aus (OPTIONEN → Fenster & Verbindungen)." : Status?.Invoke() ?? "";
            Label(g, st, new RectangleF(x, y, w, Theme.F(32)), Theme.TextFaint, 9.5f, false, Theme.Wrap);
            y += Theme.F(32);
            string fw = Firewall?.Invoke();
            if (!string.IsNullOrEmpty(fw))
            {
                bool warn = FirewallFixable != null && FirewallFixable();
                Label(g, fw, new RectangleF(x, y, w, Theme.F(32)), warn ? Theme.SoloAmber : Theme.TextFaint, 9.5f, warn, Theme.Wrap);
                y += Theme.F(32);
            }

            // letzte Verbindungsversuche (zur Fehlersuche)
            float ly = _testR.Bottom + Theme.F(16), lx = r.X + pad, lw = r.Width - pad * 2;
            Label(g, "LETZTE VERBINDUNGEN", new RectangleF(lx, ly, lw, Theme.F(18)), Theme.TextFaint, 9f, true);
            ly += Theme.F(20);
            var rec = Recent?.Invoke() ?? new List<RemoteServer440.Entry>();
            if (rec.Count == 0)
                Label(g, "Noch kein Gerät hat sich gemeldet. Kommt hier auch nichts an, wenn das Handy es versucht, erreicht es den PC nicht (WLAN, VPN oder Firewall).",
                    new RectangleF(lx, ly, lw, Theme.F(34)), Theme.TextFaint, 9.5f, false, Theme.Wrap);
            else
                foreach (var e in rec.Skip(Math.Max(0, rec.Count - 3)))
                {
                    var col = e.Ok ? Theme.LedGreen : Theme.SoloAmber;
                    Theme.FillEllipse(g, col, lx + Theme.F(4), ly + Theme.F(8), Theme.F(3));
                    Label(g, e.When.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "   " + e.Ip + "   " + e.Text, new RectangleF(lx + Theme.F(14), ly, lw - Theme.F(14), Theme.F(18)), Theme.TextDim, 9.5f);
                    ly += Theme.F(18);
                }

            float bh = Theme.F(28);
            _newR = new RectangleF(x, r.Bottom - bh - pad, Theme.F(110), bh);
            _copyR = new RectangleF(_newR.Right + Theme.F(8), _newR.Y, Theme.F(130), bh);
            bool fix = FirewallFixable != null && FirewallFixable();
            _fwR = fix ? new RectangleF(_copyR.Right + Theme.F(8), _newR.Y, Theme.F(140), bh) : RectangleF.Empty;
            Btn(g, _newR, "Neuer Code", Hover == "#new");
            Btn(g, _copyR, "Adresse kopieren", Hover == "#copy");
            if (fix) Btn(g, _fwR, "Firewall freigeben …", Hover == "#fw");
        }

        private static void Btn(Graphics g, RectangleF r, string t, bool hover)
        {
            Theme.FillRound(g, hover ? Theme.ButtonHover : Theme.Button, r, Theme.F(6));
            Theme.StrokeRound(g, Theme.ButtonEdge, Theme.F(1), r, Theme.F(6));
            Label(g, t, r, Theme.Text, 10f, true, Theme.Center);
        }

        protected override string ContentZone(Point p) =>
            _newR.Contains(p) ? "#new" : _copyR.Contains(p) ? "#copy" : !_fwR.IsEmpty && _fwR.Contains(p) ? "#fw" : _testR.Contains(p) ? "#test" : null;

        protected override bool ContentDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;
            if (_newR.Contains(e.Location)) { NewCode?.Invoke(); Invalidate(); return true; }
            if (!_fwR.IsEmpty && _fwR.Contains(e.Location)) { FixFirewall?.Invoke(); return true; }
            if (_testR.Contains(e.Location)) { TestHere?.Invoke(); return true; }
            if (_copyR.Contains(e.Location))
            {
                string url = Url?.Invoke();
                if (url != null) try { Clipboard.SetText(url); } catch { }
                return true;
            }
            return false;
        }
    }
}
