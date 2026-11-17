using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Mischpult.UI
{
    /// <summary>Was gerade laeuft (Titel, Interpret, App).</summary>
    internal sealed class NowPlayingInfo
    {
        public string Title, Artist, App;
        public bool Playing;
        /// <summary>4.0: Zufall an (null = unbekannt), Wiederholen 0 aus, 1 Titel, 2 Liste (-1 = unbekannt).</summary>
        public bool? Shuffle;
        public int Repeat = -1;
        /// <summary>4.0: Kennung der App in der Mediensteuerung (z. B. "Spotify.exe").</summary>
        public string AppId;
        /// <summary>2.17: Album-Cover (klein, null = keins). Bleibt fuer denselben Titel dasselbe Bild.</summary>
        public System.Drawing.Bitmap Cover;
        /// <summary>4.0 (finale): Laufzeit – Position und Laenge in Sekunden (-1 = unbekannt), Stand zu diesem Zeitpunkt (UTC).</summary>
        public double PosSec = -1, LenSec = -1;
        public DateTime PosAt;

        /// <summary>Position jetzt (bei laufender Wiedergabe weitergerechnet), -1 = unbekannt.</summary>
        public double PosNow(DateTime utcNow)
        {
            if (PosSec < 0 || LenSec <= 0) return -1;
            double p = PosSec + (Playing ? (utcNow - PosAt).TotalSeconds : 0);
            return Math.Max(0, Math.Min(LenSec, p));
        }

        /// <summary>"Interpret – Titel" (oder nur der Titel).</summary>
        public string Text => string.IsNullOrEmpty(Artist) ? Title ?? "" : Artist + " – " + Title;

        public bool SameAs(NowPlayingInfo o) => o != null && o.Title == Title && o.Artist == Artist && o.App == App && o.Playing == Playing;
    }

    /// <summary>
    /// Fragt im Hintergrund (alle 1,5 s) ab, was gerade laeuft: ueber die Windows-Mediensteuerung (dieselbe wie bei den
    /// Medientasten – Spotify, YouTube im Browser, Media Player …). Geht das nicht (aelteres Windows), aus den Fenstertiteln
    /// von Spotify und YouTube-Tabs. Alles per Reflection, damit das Programm ohne Windows-SDK baut und ueberall startet.
    /// </summary>
    internal sealed class NowPlayingWatcher : IDisposable
    {
        private Thread _thread;
        private volatile bool _run;
        private volatile NowPlayingInfo _current;
        /// <summary>Zuletzt gefunden (null = nichts).</summary>
        public NowPlayingInfo Current => _current;
        /// <summary>Feste Anzeige (Demo).</summary>
        public NowPlayingInfo Fixed;

        public bool Running => _run;

        public void Start()
        {
            if (_run) return;
            _run = true;
            if (Fixed != null) { _current = Fixed; return; }
            _thread = new Thread(Loop) { IsBackground = true, Name = "Mischpult Jetzt läuft", Priority = ThreadPriority.BelowNormal };
            try { _thread.SetApartmentState(ApartmentState.MTA); } catch { }
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            _current = null;
        }

        public void Dispose() => Stop();

        private void Loop()
        {
            while (_run)
            {
                NowPlayingInfo info = null;
                try { info = Poll(); } catch { }
                if (_run) _current = info;
                for (int i = 0; i < 15 && _run; i++) Thread.Sleep(100);
            }
        }

        private NowPlayingInfo Poll()
        {
            if (!Native.Win32.IsWindows) return null;
            var g = FromMediaControls(out bool available);
            if (g != null) return g;
            return FromWindowTitles();
        }

        // ------------------------------------------------------------ Windows-Mediensteuerung (ab Windows 10 1809)

        private static Type _mgrType;
        private static object _mgr;
        private static bool _gsmtcBroken;
        private static MethodInfo _asTask;
        private static int _fails;

        private NowPlayingInfo FromMediaControls(out bool available)
        {
            available = false;
            if (_gsmtcBroken) return null;
            try
            {
                if (_mgr == null)
                {
                    _mgrType = Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime", false);
                    if (_mgrType == null) { _gsmtcBroken = true; return null; }
                    var op = _mgrType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    _mgr = Await(op, _mgrType);
                    if (_mgr == null) { if (++_fails > 5) _gsmtcBroken = true; return null; }
                }
                available = true;
                var session = _mgrType.GetMethod("GetCurrentSession").Invoke(_mgr, null);
                if (session == null) return null;
                var st = session.GetType();
                string app = st.GetProperty("SourceAppUserModelId")?.GetValue(session, null) as string;
                var propsType = Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType=WindowsRuntime", false);
                if (propsType == null) return null;
                var props = Await(st.GetMethod("TryGetMediaPropertiesAsync").Invoke(session, null), propsType);
                if (props == null) return null;
                var pt = props.GetType();
                string title = pt.GetProperty("Title")?.GetValue(props, null) as string;
                string artist = pt.GetProperty("Artist")?.GetValue(props, null) as string;
                if (string.IsNullOrWhiteSpace(title)) return null;
                bool playing = true;
                bool? shuffle = null; int repeat = -1;
                double posSec = -1, lenSec = -1; DateTime posAt = DateTime.UtcNow;
                try
                {
                    var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null);
                    var status = pb?.GetType().GetProperty("PlaybackStatus")?.GetValue(pb, null);
                    if (status != null) playing = status.ToString() == "Playing";
                    if (WantModes && pb != null)   // 4.0: Zufall/Wiederholen
                    {
                        var sh = pb.GetType().GetProperty("IsShuffleActive")?.GetValue(pb, null);
                        if (sh is bool b) shuffle = b;
                        var rp = pb.GetType().GetProperty("AutoRepeatMode")?.GetValue(pb, null);
                        if (rp != null) repeat = Convert.ToInt32(rp);
                    }
                    if (WantTimeline)   // 4.0 finale: Song-Fortschritt
                    {
                        var tl = st.GetMethod("GetTimelineProperties")?.Invoke(session, null);
                        if (tl != null)
                        {
                            var tt = tl.GetType();
                            var p0 = (TimeSpan)tt.GetProperty("Position").GetValue(tl, null);
                            var st0 = (TimeSpan)tt.GetProperty("StartTime").GetValue(tl, null);
                            var e0 = (TimeSpan)tt.GetProperty("EndTime").GetValue(tl, null);
                            var upd = tt.GetProperty("LastUpdatedTime")?.GetValue(tl, null);
                            if (e0 > st0)
                            {
                                lenSec = (e0 - st0).TotalSeconds; posSec = Math.Max(0, (p0 - st0).TotalSeconds);
                                posAt = upd is DateTimeOffset dto && dto.Year > 2000 ? dto.UtcDateTime : DateTime.UtcNow;
                            }
                        }
                    }
                }
                catch { }
                _fails = 0;
                var info = new NowPlayingInfo { Title = Clean(title), Artist = Clean(artist), App = AppName(app), Playing = playing, Shuffle = shuffle, Repeat = repeat, AppId = app, PosSec = posSec, LenSec = lenSec, PosAt = posAt };
                if (WantCover) info.Cover = CoverFor(info, props);
                return info;
            }
            catch
            {
                _mgr = null;
                if (++_fails > 5) _gsmtcBroken = true;
                return null;
            }
        }

        // ------------------------------------------------------------ 3.2 Auto-Pause: eine bestimmte App pausieren/fortsetzen

        private static readonly object _ctlLock = new object();

        /// <summary>Passt die Kennung der Mediensteuerung zu einer der Apps (z. B. "spotify.exe")?</summary>
        internal static bool AppMatches(string appId, string[] appKeys)
        {
            if (string.IsNullOrEmpty(appId) || appKeys == null) return false;
            string id = appId.ToLowerInvariant();
            foreach (var k in appKeys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                string b = k.ToLowerInvariant();
                if (b.EndsWith(".exe")) b = b.Substring(0, b.Length - 4);
                if (b.Length >= 3 && id.Contains(b)) return true;
            }
            return false;
        }

        /// <summary>
        /// Wiedergabe der passenden App pausieren (pause = true; nur wenn sie gerade spielt) bzw. fortsetzen (nur wenn pausiert).
        /// Liefert true, wenn etwas umgeschaltet wurde. Blockiert bis zu ein paar Sekunden – nicht im UI-Faden aufrufen.
        /// </summary>
        internal static bool ControlApp(string[] appKeys, bool pause)
        {
            if (!Native.Win32.IsWindows || _gsmtcBroken || appKeys == null || appKeys.Length == 0) return false;
            lock (_ctlLock)
            {
                try
                {
                    if (_mgr == null)
                    {
                        _mgrType = _mgrType ?? Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime", false);
                        if (_mgrType == null) return false;
                        var op = _mgrType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                        _mgr = Await(op, _mgrType);
                        if (_mgr == null) return false;
                    }
                    var sessions = _mgrType.GetMethod("GetSessions")?.Invoke(_mgr, null) as System.Collections.IEnumerable;
                    if (sessions == null) return false;
                    bool any = false;
                    foreach (var session in sessions)
                    {
                        if (session == null) continue;
                        var st = session.GetType();
                        string app = st.GetProperty("SourceAppUserModelId")?.GetValue(session, null) as string;
                        if (!AppMatches(app, appKeys)) continue;
                        string status = null;
                        try { var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null); status = pb?.GetType().GetProperty("PlaybackStatus")?.GetValue(pb, null)?.ToString(); } catch { }
                        if (pause ? status != "Playing" : status != "Paused") continue;
                        var res = Await(st.GetMethod(pause ? "TryPauseAsync" : "TryPlayAsync")?.Invoke(session, null), typeof(bool));
                        if (res is bool b && b) any = true;
                    }
                    return any;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// 3.4: alle spielenden Apps pausieren (pause = true) bzw. genau die genannten fortsetzen. Liefert die Kennungen,
        /// die umgeschaltet wurden. Blockiert ein paar Sekunden – nicht im UI-Faden aufrufen.
        /// </summary>
        internal static List<string> ControlAll(bool pause, ICollection<string> onlyIds)
        {
            var done = new List<string>();
            if (!Native.Win32.IsWindows || _gsmtcBroken) return done;
            lock (_ctlLock)
            {
                try
                {
                    if (_mgr == null)
                    {
                        _mgrType = _mgrType ?? Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime", false);
                        if (_mgrType == null) return done;
                        var op = _mgrType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                        _mgr = Await(op, _mgrType);
                        if (_mgr == null) return done;
                    }
                    var sessions = _mgrType.GetMethod("GetSessions")?.Invoke(_mgr, null) as System.Collections.IEnumerable;
                    if (sessions == null) return done;
                    foreach (var session in sessions)
                    {
                        if (session == null) continue;
                        var st = session.GetType();
                        string app = st.GetProperty("SourceAppUserModelId")?.GetValue(session, null) as string;
                        if (onlyIds != null && (app == null || !onlyIds.Contains(app))) continue;
                        string status = null;
                        try { var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null); status = pb?.GetType().GetProperty("PlaybackStatus")?.GetValue(pb, null)?.ToString(); } catch { }
                        if (pause ? status != "Playing" : status != "Paused") continue;
                        var res = Await(st.GetMethod(pause ? "TryPauseAsync" : "TryPlayAsync")?.Invoke(session, null), typeof(bool));
                        if (res is bool b && b && app != null) done.Add(app);
                    }
                }
                catch { }
            }
            return done;
        }

        // ------------------------------------------------------------ 4.0 Spulen, Zufall, Wiederholen

        /// <summary>Zufall/Wiederholen mit abfragen (nur wenn die Musik-Tasten erweitert sind).</summary>
        public static volatile bool WantModes;
        /// <summary>4.0 (finale): Laufzeit mit abfragen (Song-Fortschritt).</summary>
        public static volatile bool WantTimeline;

        /// <summary>4.0 (finale): an diese Stelle springen (Sekunden ab Songanfang). Blockiert kurz – nicht im UI-Faden.</summary>
        internal static bool SeekTo(double seconds)
        {
            if (!Native.Win32.IsWindows || _gsmtcBroken) return false;
            lock (_ctlLock)
            {
                try
                {
                    var session = CurrentSession();
                    if (session == null) return false;
                    var st = session.GetType();
                    var tl = st.GetMethod("GetTimelineProperties")?.Invoke(session, null);
                    if (tl == null) return false;
                    var tt = tl.GetType();
                    var st0 = (TimeSpan)tt.GetProperty("StartTime").GetValue(tl, null);
                    var e0 = (TimeSpan)tt.GetProperty("EndTime").GetValue(tl, null);
                    var target = st0 + TimeSpan.FromSeconds(Math.Max(0, seconds));
                    if (e0 > st0 && target > e0) target = e0 - TimeSpan.FromSeconds(1);
                    var res = Await(st.GetMethod("TryChangePlaybackPositionAsync")?.Invoke(session, new object[] { target.Ticks }), typeof(bool));
                    return res is bool b && b;
                }
                catch { return false; }
            }
        }

        private static object CurrentSession()
        {
            if (_mgr == null)
            {
                _mgrType = _mgrType ?? Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime", false);
                if (_mgrType == null) return null;
                var op = _mgrType.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                _mgr = Await(op, _mgrType);
                if (_mgr == null) return null;
            }
            return _mgrType.GetMethod("GetCurrentSession").Invoke(_mgr, null);
        }

        /// <summary>Um so viele Sekunden spulen (negativ = zurueck). Blockiert kurz – nicht im UI-Faden.</summary>
        internal static bool Seek(double seconds)
        {
            if (!Native.Win32.IsWindows || _gsmtcBroken) return false;
            lock (_ctlLock)
            {
                try
                {
                    var session = CurrentSession();
                    if (session == null) return false;
                    var st = session.GetType();
                    var tl = st.GetMethod("GetTimelineProperties")?.Invoke(session, null);
                    if (tl == null) return false;
                    var tt = tl.GetType();
                    var pos = (TimeSpan)tt.GetProperty("Position").GetValue(tl, null);
                    var end = (TimeSpan)tt.GetProperty("EndTime").GetValue(tl, null);
                    var upd = tt.GetProperty("LastUpdatedTime")?.GetValue(tl, null);
                    // Position ist der Stand beim letzten Update – bei laufender Wiedergabe die Zeit seitdem dazurechnen
                    try
                    {
                        var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null);
                        if (pb?.GetType().GetProperty("PlaybackStatus")?.GetValue(pb, null)?.ToString() == "Playing" && upd is DateTimeOffset dto && dto.Year > 2000)
                            pos += DateTimeOffset.Now - dto;
                    }
                    catch { }
                    var target = pos + TimeSpan.FromSeconds(seconds);
                    if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                    if (end > TimeSpan.Zero && target > end) target = end - TimeSpan.FromSeconds(1);
                    var res = Await(st.GetMethod("TryChangePlaybackPositionAsync")?.Invoke(session, new object[] { target.Ticks }), typeof(bool));
                    return res is bool b && b;
                }
                catch { return false; }
            }
        }

        /// <summary>Zufall umschalten. Liefert den neuen Zustand (null = geht nicht).</summary>
        internal static bool? ToggleShuffle()
        {
            if (!Native.Win32.IsWindows || _gsmtcBroken) return null;
            lock (_ctlLock)
            {
                try
                {
                    var session = CurrentSession();
                    if (session == null) return null;
                    var st = session.GetType();
                    var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null);
                    bool cur = pb?.GetType().GetProperty("IsShuffleActive")?.GetValue(pb, null) is bool b && b;
                    var res = Await(st.GetMethod("TryChangeShuffleActiveAsync")?.Invoke(session, new object[] { !cur }), typeof(bool));
                    return res is bool ok && ok ? !cur : (bool?)null;
                }
                catch { return null; }
            }
        }

        /// <summary>Wiederholen weiterschalten: aus → Liste → Titel → aus. Liefert den neuen Modus (-1 = geht nicht).</summary>
        internal static int CycleRepeat()
        {
            if (!Native.Win32.IsWindows || _gsmtcBroken) return -1;
            lock (_ctlLock)
            {
                try
                {
                    var session = CurrentSession();
                    if (session == null) return -1;
                    var st = session.GetType();
                    var pb = st.GetMethod("GetPlaybackInfo").Invoke(session, null);
                    var cur = pb?.GetType().GetProperty("AutoRepeatMode")?.GetValue(pb, null);
                    int c = cur == null ? 0 : Convert.ToInt32(cur);
                    int next = c == 0 ? 2 : c == 2 ? 1 : 0;
                    var mode = Type.GetType("Windows.Media.MediaPlaybackAutoRepeatMode, Windows.Media, ContentType=WindowsRuntime", false)
                               ?? Type.GetType("Windows.Media.MediaPlaybackAutoRepeatMode, Windows.Foundation.UniversalApiContract, ContentType=WindowsRuntime", false);
                    if (mode == null) return -1;
                    var res = Await(st.GetMethod("TryChangeAutoRepeatModeAsync")?.Invoke(session, new object[] { Enum.ToObject(mode, next) }), typeof(bool));
                    return res is bool ok && ok ? next : -1;
                }
                catch { return -1; }
            }
        }

        // ------------------------------------------------------------ 2.17 Album-Cover

        /// <summary>Cover mit abfragen (nur wenn es irgendwo angezeigt wird).</summary>
        public static volatile bool WantCover;
        private static string _coverKey;
        private static System.Drawing.Bitmap _cover;

        /// <summary>Cover zum Titel: nur bei einem neuen Titel neu laden (das Bild bleibt sonst dasselbe Objekt).</summary>
        private static System.Drawing.Bitmap CoverFor(NowPlayingInfo info, object props)
        {
            string key = info.App + "|" + info.Artist + "|" + info.Title;
            if (key == _coverKey) return _cover;
            var bmp = LoadThumbnail(props);
            if (bmp == null && _coverKey != null && _cover != null && Environment.TickCount - _coverTry < 4000) return _cover;   // Spotify liefert das Bild manchmal etwas spaeter
            _coverTry = Environment.TickCount;
            if (bmp == null) { _coverKey = null; return null; }   // naechstes Mal noch einmal versuchen
            _coverKey = key;
            _cover = bmp;   // das alte Bild nicht freigeben – die Oberflaeche zeichnet es vielleicht noch
            return bmp;
        }
        private static int _coverTry;

        private static Type _rasType, _inputType;
        private static MethodInfo _asStream;

        /// <summary>Thumbnail der Mediensteuerung als kleines Bitmap (hoechstens 320 px) – oder null.</summary>
        private static System.Drawing.Bitmap LoadThumbnail(object props)
        {
            object ras = null;
            try
            {
                var thumb = props.GetType().GetProperty("Thumbnail")?.GetValue(props, null);
                if (thumb == null) return null;
                if (_rasType == null)
                {
                    _rasType = Type.GetType("Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType=WindowsRuntime", false);
                    _inputType = Type.GetType("Windows.Storage.Streams.IInputStream, Windows.Storage.Streams, ContentType=WindowsRuntime", false);
                    Assembly asm = null;
                    try { asm = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"); } catch { }
                    _asStream = _inputType == null ? null : asm?.GetType("System.IO.WindowsRuntimeStreamExtensions")?.GetMethod("AsStreamForRead", new[] { _inputType });
                }
                if (_rasType == null || _asStream == null) return null;
                var open = thumb.GetType().GetMethod("OpenReadAsync")?.Invoke(thumb, null);
                ras = Await(open, _rasType);
                if (ras == null) return null;
                using (var s = (System.IO.Stream)_asStream.Invoke(null, new[] { ras }))
                using (var ms = new System.IO.MemoryStream())
                {
                    s.CopyTo(ms);
                    if (ms.Length == 0) return null;
                    ms.Position = 0;
                    using (var img = System.Drawing.Image.FromStream(ms)) return Shrink(img, 320);
                }
            }
            catch { return null; }
            finally { (ras as IDisposable)?.Dispose(); }
        }

        /// <summary>Verkleinerte Kopie (vormultipliert, zeichnet schnell).</summary>
        internal static System.Drawing.Bitmap Shrink(System.Drawing.Image img, int max)
        {
            float k = Math.Min(1f, max / (float)Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new System.Drawing.Rectangle(0, 0, w, h));
            }
            return bmp;
        }

        /// <summary>Wartet auf eine WinRT-Operation (IAsyncOperation&lt;T&gt;) und liefert ihr Ergebnis.</summary>
        private static object Await(object op, Type resultType)
        {
            if (op == null) return null;
            if (_asTask == null)
            {
                Assembly asm = null;
                try { asm = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"); } catch { }
                var ext = asm?.GetType("System.WindowsRuntimeSystemExtensions");
                _asTask = ext?.GetMethods().FirstOrDefault(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                                                               m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
            }
            if (_asTask != null)
            {
                var task = (Task)_asTask.MakeGenericMethod(resultType).Invoke(null, new[] { op });
                if (!task.Wait(3000)) return null;
                return task.GetType().GetProperty("Result").GetValue(task, null);
            }
            // ohne Hilfsbibliothek: auf "fertig" warten und das Ergebnis abholen
            var info = Type.GetType("Windows.Foundation.IAsyncInfo, Windows.Foundation, ContentType=WindowsRuntime", false);
            var opType = Type.GetType("Windows.Foundation.IAsyncOperation`1, Windows.Foundation, ContentType=WindowsRuntime", false);
            if (info == null || opType == null) return null;
            var statusProp = info.GetProperty("Status");
            for (int i = 0; i < 60; i++)
            {
                var s = statusProp.GetValue(op, null)?.ToString();
                if (s == "Completed") return opType.MakeGenericType(resultType).GetMethod("GetResults").Invoke(op, null);
                if (s == "Error" || s == "Canceled") return null;
                Thread.Sleep(50);
            }
            return null;
        }

        /// <summary>App-Kennung der Mediensteuerung als lesbarer Name.</summary>
        internal static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string s = id.ToLowerInvariant();
            if (s.Contains("spotify")) return "Spotify";
            if (s.Contains("msedge") || s.Contains("microsoftedge")) return "Edge";
            if (s.Contains("chrome")) return "Chrome";
            if (s.Contains("firefox") || s.Contains("308046b0af4a39cb")) return "Firefox";
            if (s.Contains("opera")) return "Opera";
            if (s.Contains("brave")) return "Brave";
            if (s.Contains("zunemusic") || s.Contains("mediaplayer")) return "Media Player";
            if (s.Contains("vlc")) return "VLC";
            if (s.Contains("applemusic") || s.Contains("itunes")) return "Apple Music";
            if (s.Contains("deezer")) return "Deezer";
            if (s.Contains("amazon")) return "Amazon Music";
            if (s.Contains("tidal")) return "TIDAL";
            if (s.Contains("discord")) return "Discord";
            int bang = id.IndexOf('!');
            string n = bang > 0 ? id.Substring(0, bang) : id;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            int us = n.IndexOf('_');
            if (us > 0) n = n.Substring(0, us);
            int dot = n.LastIndexOf('.');
            if (dot >= 0 && dot < n.Length - 1) n = n.Substring(dot + 1);
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : null;
        }

        // ------------------------------------------------------------ Fenstertitel (Notloesung)

        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };

        private static NowPlayingInfo FromWindowTitles()
        {
            foreach (var p in SafeProcesses("Spotify"))
            {
                var info = ParseSpotify(p);
                if (info != null) return info;
            }
            foreach (var b in Browsers)
                foreach (var p in SafeProcesses(b))
                {
                    var info = ParseYouTube(p, b);
                    if (info != null) return info;
                }
            return null;
        }

        private static string[] SafeProcesses(string name)
        {
            try
            {
                var list = Process.GetProcessesByName(name);
                var titles = list.Select(x => { try { return x.MainWindowTitle; } catch { return null; } }).Where(t => !string.IsNullOrEmpty(t)).ToArray();
                foreach (var x in list) x.Dispose();
                return titles;
            }
            catch { return new string[0]; }
        }

        /// <summary>Spotify-Fenster: "Interpret - Titel" beim Abspielen, sonst "Spotify (Premium/Free)".</summary>
        internal static NowPlayingInfo ParseSpotify(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase)) return null;
            int i = title.IndexOf(" - ", StringComparison.Ordinal);
            if (i <= 0) return new NowPlayingInfo { Title = title.Trim(), App = "Spotify", Playing = true };
            return new NowPlayingInfo { Artist = title.Substring(0, i).Trim(), Title = title.Substring(i + 3).Trim(), App = "Spotify", Playing = true };
        }

        /// <summary>Browser-Fenster mit YouTube-Tab vorne: "(3) Titel - YouTube - Google Chrome".</summary>
        internal static NowPlayingInfo ParseYouTube(string title, string browser)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            int i = title.IndexOf(" - YouTube", StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = title.IndexOf(" – YouTube", StringComparison.OrdinalIgnoreCase);
            if (i <= 0) return null;
            string t = title.Substring(0, i).Trim();
            if (t.StartsWith("(") && t.IndexOf(')') > 0 && t.IndexOf(')') < 6) t = t.Substring(t.IndexOf(')') + 1).Trim();
            if (t.Length == 0 || t.Equals("YouTube", StringComparison.OrdinalIgnoreCase)) return null;
            return new NowPlayingInfo { Title = t, App = "YouTube", Playing = true };
        }

        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
