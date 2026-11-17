using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.1: Installieren und Aktualisieren leicht gemacht – Infos aus einer EXE lesen, eine laufende Version beenden,
    /// die vorige Version aufheben und zurueckholen, Start-Schutz, abgesicherter Start, Reparieren, Einstellungen loeschen.
    /// </summary>
    internal static class Update410
    {
        public const string MutexName = @"Local\Mischpult_SingleInstance_7F3A";

        // ================================================================ Infos aus einer EXE

        internal sealed class ExeInfo
        {
            public string Path;
            public bool IsMischpult;
            public string Version;   // z. B. "4.1"
            public int Stamp;        // Bauzeit (Minuten seit 2024), 0 = unbekannt
            public DateTime Built => Stamp > 0 ? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Stamp).ToLocalTime() : DateTime.MinValue;
            /// <summary>"4.1 vom 31.10.2026"</summary>
            public string Text => (Version ?? "?") + (Stamp > 0 ? " vom " + Built.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")) : "");
        }

        /// <summary>Liest Name, Version und Bauzeit aus einer EXE, ohne sie zu starten. null = nicht lesbar.</summary>
        public static ExeInfo Read(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var fi = new FileInfo(path);
                if (fi.Length < 4096 || fi.Length > 200L * 1024 * 1024) return null;
                byte[] bytes;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { bytes = new byte[fs.Length]; int n = 0; while (n < bytes.Length) { int r = fs.Read(bytes, n, bytes.Length - n); if (r <= 0) break; n += r; } }
                if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') return null;
                var info = new ExeInfo { Path = path, Stamp = StampOfBytes(bytes) };
                string product = null, ver = null;
                try { var v = FileVersionInfo.GetVersionInfo(path); product = v.ProductName; ver = v.ProductVersion ?? v.FileVersion; } catch { }
                info.IsMischpult = string.Equals(product?.Trim(), "Mischpult", StringComparison.OrdinalIgnoreCase)
                                   || (info.Stamp > 0 && Contains(bytes, Encoding.Unicode.GetBytes("Mischpult")));
                info.Version = CleanVersion(ver);
                return info;
            }
            catch { return null; }
        }

        /// <summary>"4.1.0.0" / "4.1+abc" → "4.1"</summary>
        public static string CleanVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim().TrimStart('v', 'V');
            int plus = v.IndexOfAny(new[] { '+', ' ', '-' });
            if (plus > 0) v = v.Substring(0, plus);
            var parts = v.Split('.').ToList();
            while (parts.Count > 2 && parts[parts.Count - 1] == "0") parts.RemoveAt(parts.Count - 1);
            return parts.Count == 0 ? null : string.Join(".", parts);
        }

        /// <summary>Versionen vergleichen ("4.1" &lt; "4.1.2" &lt; "4.10"). Unlesbares zaehlt als 0.</summary>
        public static int CompareVersion(string a, string b)
        {
            var pa = (CleanVersion(a) ?? "0").Split('.');
            var pb = (CleanVersion(b) ?? "0").Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out int xa) ? xa : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out int yb) ? yb : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        /// <summary>Bauzeit aus den Metadaten (AssemblyMetadata "BuildStamp" = yyyyMMddHHmm).</summary>
        public static int StampOfBytes(byte[] b)
        {
            var key = Encoding.ASCII.GetBytes("BuildStamp");
            for (int i = IndexOf(b, key, 0); i >= 0; i = IndexOf(b, key, i + 1))
            {
                int p = i + key.Length;
                if (p + 13 > b.Length || b[p] != 12) continue;
                var s = Encoding.ASCII.GetString(b, p + 1, 12);
                int st = StampOf(s);
                if (st > 0) return st;
            }
            return 0;
        }

        public static int StampOf(string yyyyMMddHHmm)
        {
            if (yyyyMMddHHmm == null || !DateTime.TryParseExact(yyyyMMddHHmm, "yyyyMMddHHmm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) return 0;
            return (int)Math.Max(1, (t - new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes);
        }

        private static int IndexOf(byte[] b, byte[] key, int from)
        {
            for (int i = Math.Max(0, from); i <= b.Length - key.Length; i++)
            {
                if (b[i] != key[0]) continue;
                int k = 1;
                while (k < key.Length && b[i + k] == key[k]) k++;
                if (k == key.Length) return i;
            }
            return -1;
        }

        private static bool Contains(byte[] b, byte[] key) => IndexOf(b, key, 0) >= 0;

        /// <summary>Ist diese EXE neuer als die laufende? (Bauzeit, sonst Version)</summary>
        public static bool IsNewer(ExeInfo e)
        {
            if (e == null || !e.IsMischpult) return false;
            if (e.Stamp > 0 && Program.BuildStamp > 0) return e.Stamp > Program.BuildStamp;
            return CompareVersion(e.Version, Program.Version) > 0;
        }

        // ================================================================ Laufende Version beenden

        public static bool OtherRunning()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; }
            catch { return false; }
        }

        /// <summary>Eine laufende Version bitten, sich zu beenden, und kurz warten. true = keine laeuft mehr.</summary>
        public static bool QuitRunning(int ms)
        {
            if (!Win32.IsWindows || !OtherRunning()) return true;
            try { if (Program.QuitMessage != 0) Win32.PostMessage(Win32.HWND_BROADCAST, Program.QuitMessage, IntPtr.Zero, IntPtr.Zero); } catch { }
            try
            {
                using (var m = new Mutex(false, MutexName))
                {
                    bool got;
                    try { got = m.WaitOne(ms); } catch (AbandonedMutexException) { got = true; }
                    if (got) m.ReleaseMutex();
                    return got;
                }
            }
            catch { return false; }
        }

        // ================================================================ Installieren / aktualisieren (aus dieser EXE)

        public static string PrevDir => Path.Combine(Installer.InstallDir, "Vorige Version");
        public static string PrevExe => Path.Combine(PrevDir, "Mischpult.exe");
        public static bool HasPrev { get { try { return File.Exists(PrevExe); } catch { return false; } } }
        private static string OldExe => Path.Combine(Installer.InstallDir, "Mischpult.alt.exe");
        private static string SwapExe => Path.Combine(Installer.InstallDir, "Mischpult.wechsel.exe");

        /// <summary>
        /// Diese EXE installieren bzw. die installierte ersetzen. Laeuft die alte noch, wird sie nur beiseitegeschoben
        /// (das erlaubt Windows auch bei laufenden Programmen) – sie macht der neuen beim Start Platz.
        /// backup = die alte als "Vorige Version" aufheben. Liefert null bei Erfolg, sonst den Fehler.
        /// </summary>
        public static string InstallSelf(bool desktop, bool backup)
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            if (Installer.RunningInstalled) return Installer.Install(desktop);
            try
            {
                Directory.CreateDirectory(Installer.InstallDir);
                if (File.Exists(Installer.InstallExe))
                {
                    if (backup)
                    {
                        Directory.CreateDirectory(PrevDir);
                        try { if (File.Exists(PrevExe)) File.Delete(PrevExe); } catch { }
                        try { File.Move(Installer.InstallExe, PrevExe); }
                        catch { try { File.Copy(Installer.InstallExe, PrevExe, true); } catch { } }
                    }
                    if (File.Exists(Installer.InstallExe))
                    {
                        try { if (File.Exists(OldExe)) File.Delete(OldExe); } catch { }
                        try { File.Move(Installer.InstallExe, OldExe); } catch { }
                    }
                }
            }
            catch (Exception ex) { return ex.Message; }
            return Installer.Install(desktop);
        }

        /// <summary>Reste eines Updates wegraeumen (beim Start der installierten Version).</summary>
        public static void CleanupLeftovers()
        {
            if (!Win32.IsWindows) return;
            foreach (var f in new[] { OldExe, SwapExe })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            // 4.2.4: Rest einer frueheren Deinstallation im Temp-Ordner
            try { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Installer.RemovedPrefix + "*.exe")) try { File.Delete(f); } catch { } } catch { }
        }

        /// <summary>Die Einstellungen vor einem Update sichern (in den Ordner "Sicherungen").</summary>
        public static string BackupSettings(string why)
        {
            try
            {
                string src = MixerSettings.FilePath;
                if (!File.Exists(src)) return null;
                Directory.CreateDirectory(MixerSettings.BackupFolder);
                string dst = Path.Combine(MixerSettings.BackupFolder, "einstellungen-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + "-" + why + ".json");
                File.Copy(src, dst, true);
                try { File.SetLastWriteTime(dst, DateTime.Now); } catch { }
                return dst;
            }
            catch { return null; }
        }

        /// <summary>4.2.4: Argument fuer die installierte Version: "diese heruntergeladene EXE wegraeumen".</summary>
        public const string CleanArg = "--aufraeumen";

        /// <summary>
        /// Die installierte Version starten. Auf Wunsch raeumt sie die heruntergeladene EXE selbst weg, sobald die
        /// nicht mehr laeuft (4.2.4: ohne Eingabeaufforderung im Hintergrund).
        /// </summary>
        public static void StartInstalled(string deleteAfter)
        {
            try
            {
                string args = string.IsNullOrEmpty(deleteAfter) ? "" : CleanArg + " \"" + deleteAfter + "\"";
                Process.Start(new ProcessStartInfo(Installer.InstallExe, args) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>Datei hinter "--aufraeumen" (oder null).</summary>
        public static string CleanArgOf(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals(CleanArg, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// <summary>
        /// Nur heruntergeladene Mischpult-EXEs (Download- oder Temp-Ordner, Produkt "Mischpult"), nie die laufende.
        /// </summary>
        public static bool MayClean(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || file.Length > 500) return false;
                file = Path.GetFullPath(file);
                if (!string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(file, Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) return false;
                if (!IsDownloadedFile(file) || !File.Exists(file)) return false;
                var vi = FileVersionInfo.GetVersionInfo(file);
                return string.Equals(vi.ProductName, "Mischpult", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        /// <summary>Im Hintergrund warten, bis die heruntergeladene EXE beendet ist, dann loeschen (hoechstens 30 s).</summary>
        public static void CleanLater(string file)
        {
            if (!Win32.IsWindows || !MayClean(file)) return;
            var t = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    try { if (!File.Exists(file)) return; File.Delete(file); return; } catch { }
                    System.Threading.Thread.Sleep(500);
                }
            }) { IsBackground = true, Name = "Aufraeumen" };
            t.Start();
        }

        /// <summary>Liegt die Datei im Download-Ordner (oder Temp)? Nur dann bieten wir das Loeschen an.</summary>
        public static bool IsDownloadedFile(string path)
        {
            try
            {
                string dir = Path.GetFullPath(Path.GetDirectoryName(path) ?? "").TrimEnd('\\', '/');
                foreach (var d in new[] { DownloadsFolder, Path.GetTempPath() })
                    if (!string.IsNullOrEmpty(d) && dir.StartsWith(Path.GetFullPath(d).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        // ================================================================ Vorige Version

        public static ExeInfo PrevInfo => HasPrev ? Read(PrevExe) : null;

        /// <summary>Installierte und vorige Version tauschen (die laufende EXE darf beiseitegeschoben werden). null = ok.</summary>
        public static string RestorePrevious()
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            if (!HasPrev) return "Es ist keine vorige Version aufgehoben.";
            try
            {
                try { if (File.Exists(SwapExe)) File.Delete(SwapExe); } catch { }
                bool moved = false;
                if (File.Exists(Installer.InstallExe)) { File.Move(Installer.InstallExe, SwapExe); moved = true; }
                try { File.Copy(PrevExe, Installer.InstallExe, true); }
                catch { if (moved) File.Move(SwapExe, Installer.InstallExe); throw; }
                try { File.Delete(PrevExe); } catch { }
                if (moved) try { File.Move(SwapExe, PrevExe); } catch { }
                var now = Read(Installer.InstallExe);
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mischpult", true))
                        if (k != null && now?.Version != null) k.SetValue("DisplayVersion", now.Version);
                }
                catch { }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ================================================================ Start-Schutz

        public static string GuardFile => Path.Combine(MixerSettings.Folder, "startschutz.txt");

        /// <summary>Ein Start beginnt: liefert, wie viele Starts dieser Version vorher nicht fertig wurden.</summary>
        public static int GuardBegin()
        {
            int prev = 0;
            try
            {
                if (File.Exists(GuardFile))
                {
                    var p = File.ReadAllText(GuardFile).Trim().Split(';');
                    if (p.Length == 2 && int.TryParse(p[0], out int st) && st == Program.BuildStamp && int.TryParse(p[1], out int n)) prev = Math.Max(0, n);
                }
                Directory.CreateDirectory(MixerSettings.Folder);
                File.WriteAllText(GuardFile, Program.BuildStamp + ";" + (prev + 1));
            }
            catch { }
            return prev;
        }

        /// <summary>Der Start hat geklappt (oder das Mischpult wurde normal beendet).</summary>
        public static void GuardEnd()
        {
            try { if (File.Exists(GuardFile)) File.Delete(GuardFile); } catch { }
        }

        // ================================================================ Abgesicherter Start

        /// <summary>Bleibt im abgesicherten Start erhalten: Inhalte, Sperren, Fenster, Update-Schalter.</summary>
        internal static readonly HashSet<string> SafeKeep = new HashSet<string>(StringComparer.Ordinal)
        {
            "Version", "UiLevel", "WizardDone", "TrayHintShown", "LastVersion", "DocsRecMoved", "WinX", "WinY", "WinW", "WinH", "WinMax", "CloseToTray",
            "VolCapOn", "VolCapPct", "VolCapHash", "VolCapSalt", "TimeLimitOn", "TimeDate", "HearingCap", "DevGuardOn", "DevGuardPct",
            "RecordFolder", "DuckTrigger", "GameChannel", "MicAfter", "HearDate", "HearSec", "HearLoudSec", "BirthDay", "BirthMonth",
            // Wege des Tons laufen weiter: Ausgaenge, Stream-Mix, Sprach-Ausgang, Push-to-Talk, Ducking
            "MultiOutput", "StreamOn", "StreamMute", "StreamDevice", "StreamDeviceName", "StreamMasterDb", "VoiceOn", "VoiceDevice", "VoiceDeviceName",
            "VoiceGateOn", "VoiceGateDb", "VoiceMuteInPath", "PttMode", "PttKeys", "DuckOn", "DuckAmount", "DuckThreshold", "DuckHold",
            "InstallPromptOff", "DropUpdateOn", "DownloadWatchOn", "DownloadIgnore", "UpdateBackupOn", "StartGuardOn", "UninstallAskOn",
            "OnlineUpdateOn", "AutoUpdateOn", "UpdateRepo", "UpdateLastCheck", "UpdateSkip", "PackageOn", "UpdateNewsOn", "SafeStartOn", "RepairOn",
            "AutostartDelay", "OverviewOn",
        };

        private static bool IsScalar(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || Nullable.GetUnderlyingType(t) != null;

        private static IEnumerable<FieldInfo> DataFields() =>
            typeof(MixerSettings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.GetCustomAttributes(typeof(DataMemberAttribute), false).Length > 0);

        /// <summary>
        /// Alle Schalter, Designs und Extras auf Werkseinstellung – nur im Speicher (es wird nichts gespeichert).
        /// Kanaele, Apps, Szenen, Hotkeys, Sounds und Sperren bleiben.
        /// </summary>
        public static int MakeSafe(MixerSettings s)
        {
            var d = MixerSettings.CreateDefault();
            d.Normalize();
            int n = 0;
            foreach (var f in DataFields())
            {
                if (SafeKeep.Contains(f.Name) || !IsScalar(f.FieldType)) continue;
                var a = f.GetValue(s); var b = f.GetValue(d);
                if (Equals(a, b)) continue;
                f.SetValue(s, b);
                n++;
            }
            s.ChannelImagesOff = true;   // Bilder in den Kanaelen
            s.AccelOff = true;           // Hardware-Beschleunigung
            s.SelfTestOff = true;
            s.GreetingOff = true;
            s.Normalize();
            return n;
        }

        // ================================================================ Reparieren

        /// <summary>Fensterpositionen und -groessen.</summary>
        internal static bool IsWindowField(string name) =>
            name == "WinMax" || System.Text.RegularExpressions.Regex.IsMatch(name, "^(Win|Mini|VuWin|BridgeWin|Widget|Player|Cross)[XYWH]$");

        /// <summary>
        /// Leistung, Aussehen und Fensterpositionen zurueck auf Werkseinstellung. Kanaele, Apps, Szenen, Hotkeys,
        /// Ton und Automatik bleiben. Liefert, wie viele Werte sich geaendert haben.
        /// </summary>
        public static int Repair(MixerSettings s)
        {
            var d = MixerSettings.CreateDefault();
            d.Normalize();
            var set = new HashSet<string>(RepairFields, StringComparer.Ordinal);
            int n = 0;
            foreach (var f in DataFields())
            {
                bool win = IsWindowField(f.Name);
                if (!(win || set.Contains(f.Name)) || (SafeKeep.Contains(f.Name) && !win)) continue;   // Sperren & Co. nie
                var a = f.GetValue(s); var b = f.GetValue(d);
                if (IsScalar(f.FieldType) ? Equals(a, b) : a == null && b == null) continue;
                f.SetValue(s, b);
                n++;
            }
            s.Normalize();
            return n;
        }

        /// <summary>Werte der Seiten "Aussehen" und "Leuchten &amp; Anzeigen" und der Leistung (aus den Schaltern ermittelt).</summary>
        internal static readonly string[] RepairFields =
        {
            // 4.2: Optik
            "DeskMirrorOn", "RgbWaveOn", "RgbWaveBeat", "FrameGlowOn", "FrameGlowMode", "SplashOn", "CapIconOn",
            // aus den Schaltern ermittelt (Vorschau: MISCHPULT_DEMO_FIELDMAP), dazu einige Farben und die Hardware-Beschleunigung
            "AccelOff", "AccentColor", "AccentSource", "AmbilightOff", "AnimDecoOn", "AnimStripsOff", "ArmRest", "AutoFoldOn", "AutoIconColor",
            "BackgroundFullOff", "BalanceOn", "BgDim", "BgGifOn", "BgImagePath", "BigShowOff", "BpmOn", "BridgeArrange", "BridgeBarWidth",
            "BridgeClockOff", "BridgeDragOff", "BridgeFreeOrder", "BridgeHeight", "BridgeHidden", "BridgeOff", "BridgeOrder", "BridgePackOff",
            "BridgeStyle", "CablesOn", "CapGlowOn", "CapShape", "CapStyle", "ChanStatsOn", "ChannelFastOff", "ChannelField", "ChannelImagesOff",
            "ChannelWidthOn", "CheekStyle", "ClickKind", "ClipFlashOff", "ClipLogOn", "ClockOff", "ClockStyle", "ColorCapsOff", "ColorScheme",
            "Compact", "CountdownBeepOff", "CountdownDisplaysOff", "CountdownMin", "CountdownOn", "CoverAmbiOn", "CoverArt", "CoverBgOn", "CoverOff",
            "CrtOn", "DekoOn", "DesignFadeOn", "DevFormatOn", "DevIconOn", "DigitalPlates", "DragWidthOff", "DuckLinesOn", "DustCoverOn",
            "DymoPlates", "DynamicsOn", "EqCurveOff", "FaderGlowOff", "FaderTrailOn", "FastDrawOff", "FavDesign", "FollowWinTheme", "FontName",
            "FontOn", "FpsHighAuto", "GameFps", "GameFxOff", "GameModeOff", "GamePauseOff", "GdiTextOff", "GhostPct", "GifOn", "GlowOff", "GonioOff",
            "GpuOn", "GrOff", "GreetingOff", "GroupBracketsOn", "HeadroomOn", "HearTimeOn", "HiddenOutputs", "HideMic", "HighFpsOff", "HoverLightOff",
            "IdleMinutes", "ImageWashOn", "KScale", "KeyClickLevel", "KeyClickOff", "KeyColorsOn", "KeyShape", "KnobRingOn", "KnobStyle",
            "KnobViewOn", "LampMode", "LampPos", "LampStrength", "LatencyOn", "LcdStyle", "LedAfterglowOff", "LedBright", "LedHaloOff", "LedShape",
            "LedSize", "LightShowOn", "LiveBg", "LoudHistMinutes", "LoudHistOff", "LowLatencyOff", "LufsEachOff", "LufsMode", "LufsOff", "LufsTarget",
            "MeterColors", "MeterDelay", "MeterFall", "MeterHold", "MeterLowColor", "MeterLupeOn", "MeterMidColor", "MeterMidDb", "MeterMode",
            "MeterOutFall", "MeterOutHold", "MeterOutMode", "MeterRealtimeOff", "MeterTopColor", "MeterTopDb", "MicWithOutputs", "MonoKeyOn",
            "MoreDecoOn", "MotorOff", "MotorPotsOn", "MotorSoundLevel", "MotorSoundOn", "MotorSpeed", "MusicBgOn", "MuteBlinkOff", "MuteDimOff",
            "NamePlateOn", "Narrow", "NeonColor", "NeonOn", "NeonPulse", "NixieBarOn", "NoSoundWarnOn", "NotesOn", "NowPlayingOff", "NumbersOff",
            "OnAirMode", "Opacity", "OpacityIdleOnly", "OscOff", "OsdPos", "OsdSize", "OutDragOn", "OutWidths", "OutputDbOn", "PacerOff",
            "PaintNowOff", "PanelMaterial", "PaperLookOn", "PcMeterOn", "PeakCountOn", "PeakDbOff", "PeakLedDb", "PeakLedOff", "PerfFps",
            "PerfPriorityOff", "PhaseWarnOn", "PowerOffShowOn", "PultBright", "PultDimFrom", "PultDimMode", "PultDimTo", "PultTipsOn", "RadarOn",
            "RandomDesignOn", "RandomPool", "RgbBright", "RgbMode", "RgbOn", "SaverMinutes", "SaverScreen", "SaverShows", "SceneSweepOn",
            "ScopeLookOn", "SeasonMode", "SelfTestOff", "ShakeOn", "ShotButtonOn", "SigLedDb", "SignalLedOff", "SkyOn", "SmartFxOff", "SoloDimOff",
            "SongProgressOn", "SongsOff", "SparksOn", "SpeakersOn", "SpecBgOn", "SpecHoverOff", "SpecOff", "SpecPalette", "SpecPeak", "SpecStyle",
            "StickersOn", "StripCustomOn", "SunLightOn", "SymbolsOn", "TapePlates", "TickerOn", "Tilt", "TimeDayFrom", "TimeDayStyle", "TimeDesignOn",
            "TimeNightFrom", "TimeNightStyle", "TruePeakOn", "TubeGlowOn", "TubesOn", "TwoRowsOn", "TwoToneOn", "UiStyle", "VSyncOff", "ViewsOff",
            "VuDual", "VuFace", "VuFall", "VuKind", "VuLight", "VuOff", "VuPeakLedOff", "VuPeakNeedleOff", "VuScaleVu", "VuStartDb", "Wall",
            "WallpaperOn", "WallpaperStyle", "WarmUpOn", "Wear", "WinDarkStyle", "WinLightStyle", "Zoom",
        };

        // ================================================================ Einstellungen loeschen (Deinstallieren)

        /// <summary>Ordner mit Einstellungen, Sicherungen und Protokollen – Aufnahmen, Bilder und Fehlerberichte bleiben.</summary>
        public static List<string> WipeFolders()
        {
            var l = new List<string>();
            if (MixerSettings.Portable) return l;
            if (MixerSettings.CloudActive) l.Add(MixerSettings.BaseFolder);
            else { l.Add(MixerSettings.Folder); l.Add(MixerSettings.BackupFolder); }
            if (MixerSettings.DocsMode) l.Add(MixerSettings.LogFolder);
            l.Add(MixerSettings.AppDataFolder);
            return l.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void WipeSettings()
        {
            foreach (var d in WipeFolders())
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            try
            {
                string root = MixerSettings.DocsRoot;
                if (MixerSettings.DocsMode && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            }
            catch { }
        }

        // ================================================================ Download-Ordner

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        private static string _downloads;
        public static string DownloadsFolder
        {
            get
            {
                if (_downloads != null) return _downloads;
                string p = null;
                if (Win32.IsWindows)
                    try
                    {
                        if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr ptr) == 0)
                        {
                            p = Marshal.PtrToStringUni(ptr);
                            Marshal.FreeCoTaskMem(ptr);
                        }
                    }
                    catch { }
                if (string.IsNullOrEmpty(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                return _downloads = p;
            }
        }

        /// <summary>Neueste Mischpult-EXE im Download-Ordner, die neuer ist als die laufende (null = keine).</summary>
        public static ExeInfo NewestDownload(string folder = null)
        {
            try
            {
                folder = folder ?? DownloadsFolder;
                if (!Directory.Exists(folder)) return null;
                ExeInfo best = null;
                foreach (var f in Directory.GetFiles(folder, "*.exe").Where(f => Path.GetFileName(f).IndexOf("ischpult", StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).Take(8))
                {
                    var e = Read(f);
                    if (!IsNewer(e)) continue;
                    if (best == null || e.Stamp > best.Stamp) best = e;
                }
                return best;
            }
            catch { return null; }
        }
    }
}
