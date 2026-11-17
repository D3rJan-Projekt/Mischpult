using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;
using Mischpult.UI;

namespace Mischpult
{
    internal static class Program
    {
        public static int ShowMessage;
        /// <summary>3.4.6: "Befehl liegt bereit" (aus der Sprungliste der Taskleiste) – an die laufende Version.</summary>
        public static int CommandMessage;
        /// <summary>4.0: "bitte beenden" (zum Deinstallieren).</summary>
        public static int QuitMessage;
        /// <summary>4.0: nach dem Beenden diese EXE starten (z. B. die gerade installierte) statt sich selbst.</summary>
        public static string RestartPath;
        /// <summary>3.4.6: Befehl aus der Sprungliste, der beim Start ausgefuehrt wird (lief noch keine Version).</summary>
        public static string StartCommand;
        public static string CommandFile => System.IO.Path.Combine(MixerSettings.Folder, "befehl.txt");

        /// <summary>Wert hinter "--cmd" (oder null).</summary>
        internal static string CommandArg(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("--cmd", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        /// <summary>Nach dem Beenden neu starten (z. B. nach dem Import von Einstellungen).</summary>
        public static bool RestartRequested;
        /// <summary>4.1: nach dem Beenden die Einstellungen loeschen (Deinstallieren mit "alles weg").</summary>
        public static bool WipeOnExit;
        /// <summary>4.1: abgesicherter Start – alle Extras aus, nichts wird gespeichert.</summary>
        public static bool SafeMode;
        /// <summary>4.1: zusaetzliche Argumente beim Neustart (z. B. "--abgesichert").</summary>
        public static string RestartArgs;
        public const string Author = "D3r_Jan";
        public const string Version = "4.3";
        /// <summary>4.1: GitHub-Projekt fuer "online nach Updates suchen" ("Benutzer/Projekt", leer = keins eingebaut).</summary>
        public const string UpdateRepoDefault = "D3rJan-Projekt/Mischpult";

        /// <summary>Bauzeit in Minuten seit 2024 (UTC) – groesser = neuer. 0 = unbekannt.</summary>
        public static readonly int BuildStamp = ReadBuildStamp();
        private const string HandoverEventName = @"Local\Mischpult_Handover_7F3A";

        /// <summary>Laufende Version macht Platz fuer eine neuere: der neuen Bescheid geben.</summary>
        public static void SignalHandover()
        {
            try { using (var e = EventWaitHandle.OpenExisting(HandoverEventName)) e.Set(); } catch { }
        }

        private static int ReadBuildStamp()
        {
            try
            {
                foreach (var a in typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
                    if (a is System.Reflection.AssemblyMetadataAttribute m && m.Key == "BuildStamp" &&
                        DateTime.TryParseExact(m.Value, "yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
                        return (int)Math.Max(1, (t - new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes);
            }
            catch { }
            return 0;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            bool demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
            bool minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            bool shiftAtStart = false;   // 4.1: abgesicherter Start mit gedrueckter Umschalt-Taste
            try { shiftAtStart = (Control.ModifierKeys & Keys.Shift) != 0; } catch { }
            bool safeArg = args.Any(a => a.Equals("--abgesichert", StringComparison.OrdinalIgnoreCase));
            bool forcedUpdate = args.Any(a => a.Equals("--update", StringComparison.OrdinalIgnoreCase));
            bool autoUpdate = args.Any(a => a.Equals("--auto-update", StringComparison.OrdinalIgnoreCase));   // 4.2.5: ohne Fenster
            forcedUpdate |= autoUpdate;

            string cmd = CommandArg(args);
            string openFile = UI.FileAssoc420.OpenArg(args);   // 4.2: Doppelklick auf eine Mischpult-Datei
            if (cmd == null && openFile != null && UI.FileAssoc420.Allowed(openFile)) { try { openFile = System.IO.Path.GetFullPath(openFile); } catch { } cmd = "open:" + openFile; }
            for (int i = 0; i + 1 < args.Length; i++)   // 4.0: "--profil Name" (z. B. in einer eigenen Verknuepfung)
                if (args[i].Equals("--profil", StringComparison.OrdinalIgnoreCase)) MixerSettings.ProfileArg = MixerSettings.CleanProfileName(args[i + 1]) ?? "";
            if (Win32.IsWindows && !demo) { try { QuitMessage = Win32.RegisterWindowMessage("MISCHPULT_QUIT_7F3A"); } catch { } }
            if (!demo) UI.Update410.CleanLater(UI.Update410.CleanArgOf(args));   // 4.2.4: heruntergeladene EXE nach dem Installieren wegraeumen
            if (!demo && Win32.IsWindows && args.Any(a => a.Equals("--admin", StringComparison.OrdinalIgnoreCase))) return UI.Installer.RunAdmin(args);   // 4.2.7
            if (!demo && args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Application.EnableVisualStyles();
                return UI.Installer.RunUninstall();
            }
            // 4.1: mit Windows starten – auf Wunsch etwas spaeter
            int delay = demo ? 0 : Installer.DelayArg(args);
            if (delay > 0) { try { Thread.Sleep(delay * 1000); } catch { } }
            // 4.1: heruntergeladene EXE gestartet – gleich fragen: installieren / aktualisieren?
            if (!demo && Win32.IsWindows && cmd == null && !minimized && !safeArg && !Installer.RunningInstalled && !MixerSettings.Portable)
            {
                MixerSettings pre = null;
                try { pre = MixerSettings.Load(); } catch { }
                if (autoUpdate && UI.InstallFlow410.RunAuto(pre)) return 0;   // 4.2.5: geklappt – die installierte Version laeuft schon
                if (pre != null && (forcedUpdate || !pre.InstallPromptOff))
                {
                    InitUi();
                    Theme.S = DetectScale(pre);
                    try { Theme.ApplyScheme(pre.ColorScheme, pre.AccentColor, pre.UiStyle); } catch { }
                    bool go;
                    try { go = UI.InstallFlow410.Run(pre, forcedUpdate); } catch (Exception ex) { Report(ex); go = true; }
                    if (!go) return 0;
                }
            }
            Mutex mutex = null;
            if (Win32.IsWindows && !demo)
            {
                try { ShowMessage = Win32.RegisterWindowMessage("MISCHPULT_SHOW_WINDOW_7F3A"); } catch { }
                try { CommandMessage = Win32.RegisterWindowMessage("MISCHPULT_COMMAND_7F3A"); } catch { }
                mutex = new Mutex(true, @"Local\Mischpult_SingleInstance_7F3A", out bool created);
                if (!created && cmd != null)
                {
                    // 3.4.6: Aufgabe aus der Sprungliste – der laufenden Version Bescheid geben, selbst nichts starten
                    try
                    {
                        System.IO.Directory.CreateDirectory(MixerSettings.Folder);
                        System.IO.File.WriteAllText(CommandFile, cmd);
                        Win32.PostMessage(Win32.HWND_BROADCAST, CommandMessage, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }
                    mutex.Dispose();
                    return 0;
                }
                if (!created && delay > 0) { mutex.Dispose(); return 0; }   // 4.1: verzoegerter Autostart, laeuft schon: nichts tun
                if (!created)
                {
                    // Schon gestartet: vorhandenes Fenster nach vorne holen. Die Nachricht traegt unsere Bauzeit –
                    // ist diese EXE neuer, meldet die laufende Version das ueber ein Ereignis, beendet sich,
                    // und diese hier startet stattdessen. Sonst ist hier sofort Schluss.
                    bool got = false;
                    try
                    {
                        using (var handover = new EventWaitHandle(false, EventResetMode.ManualReset, HandoverEventName))
                        {
                            handover.Reset();
                            try { Win32.PostMessage(Win32.HWND_BROADCAST, ShowMessage, (IntPtr)BuildStamp, IntPtr.Zero); } catch { }
                            // Ereignis da: die laufende Version beendet sich gerade. Ohne Ereignis kurz nachsehen,
                            // ob eine aeltere Version (ohne dieses Ereignis) trotzdem Platz macht.
                            int wait = handover.WaitOne(2000) ? 15000 : 3000;
                            try { got = mutex.WaitOne(wait); }
                            catch (AbandonedMutexException) { got = true; }
                        }
                    }
                    catch { }
                    if (!got) { mutex.Dispose(); return 0; }
                }
            }

            InitUi();
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);

            StartCommand = cmd;
            if (!demo && MixerSettings.ProfilesOn && MixerSettings.ProfileAsk && MixerSettings.ProfileArg == null)
            {
                // 4.0: beim Start fragen, welches Profil
                string pick = UI.ProfilePicker.Ask();
                if (pick == UI.ProfilePicker.Cancel) { GC.KeepAlive(mutex); mutex?.Dispose(); return 0; }
                MixerSettings.WriteProfilePointer(true, pick, true);
            }
            var settings = demo ? DemoSettings() : MixerSettings.Load();
            Theme.S = DetectScale(settings);
            if (settings.Zoom.HasValue && settings.Zoom.Value >= 50 && settings.Zoom.Value <= 200)
            {
                // 3.2: eigene Groesse – aber nie breiter als der Bildschirm (die Kopfleiste braucht ihre Mindestbreite)
                float baseS = Theme.S, z = settings.Zoom.Value / 100f;
                Theme.S = baseS * z;
                try
                {
                    float maxS = Screen.PrimaryScreen.WorkingArea.Width / 990f;
                    if (z > 1f && Theme.S > maxS) Theme.S = Math.Max(baseS, maxS);
                }
                catch { }
            }
            Theme.DetectTextHint();
            if (!demo && !StartChecks410(settings, args, shiftAtStart && !minimized, safeArg, ref mutex))   // 4.1: Start-Schutz, abgesicherter Start
            {
                try { mutex?.ReleaseMutex(); } catch { }
                mutex?.Dispose();
                return 0;
            }
            if (settings.GdiTextOff && Environment.GetEnvironmentVariable("MISCHPULT_GDI_TEXT") == null) Theme.GdiText = false;
            IAudioBackend backend = demo ? (IAudioBackend)new DemoEngine() : new AudioEngine();
            try
            {
                using (var form = new MixerForm(backend, settings, minimized, demo))
                    Application.Run(form);
            }
            finally
            {
                backend.Dispose();
                GC.KeepAlive(mutex);
                try { mutex?.ReleaseMutex(); } catch { }
                mutex?.Dispose();
            }
            if (WipeOnExit) UI.Update410.WipeSettings();   // 4.1: deinstalliert, "alles weg"
            if (RestartRequested)
            {
                string ra = (MixerSettings.ProfileArg != null ? "--profil \"" + MixerSettings.ProfileArg + "\"" : "") + (RestartArgs != null ? " " + RestartArgs : "");
                try { System.Diagnostics.Process.Start(RestartPath ?? Application.ExecutablePath, ra.Trim()); } catch { }
            }
            return 0;
        }

        private static bool _uiInit;

        /// <summary>Einmal: scharf auf jedem Bildschirm, Windows-Optik fuer Knoepfe, GDI-Text als Standard.</summary>
        private static void InitUi()
        {
            if (_uiInit) return;
            _uiInit = true;
            Win32.EnsureDpiAware();
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); } catch { }
        }

        /// <summary>
        /// 4.1: Start-Schutz (zweimal hintereinander beim Start ausgestiegen? dann fragen) und abgesicherter Start
        /// (alle Extras aus, nichts wird gespeichert). false = nicht weiterstarten.
        /// </summary>
        private static bool StartChecks410(MixerSettings settings, string[] args, bool shift, bool safeArg, ref Mutex mutex)
        {
            bool safe = safeArg || (shift && settings.SafeStartOn);
            if (settings.StartGuardOn)
            {
                int fails = UI.Update410.GuardBegin();
                if (fails >= 2 && !safe)
                {
                    bool canRestore = UI.Installer.RunningInstalled && UI.Update410.HasPrev;
                    try { Theme.ApplyScheme(settings.ColorScheme, settings.AccentColor, settings.UiStyle); } catch { }
                    string r;
                    try { r = UI.InstallFlow410.AskGuard(fails, canRestore); } catch { r = "normal"; }
                    if (r == null) return false;
                    if (r == "safe") safe = true;
                    else if (r == "prev")
                    {
                        try { mutex?.ReleaseMutex(); } catch { }
                        mutex?.Dispose(); mutex = null;
                        string err = UI.Update410.RestorePrevious();
                        if (err == null)
                        {
                            UI.Update410.GuardEnd();
                            try { System.Diagnostics.Process.Start(UI.Installer.InstallExe); } catch { }
                            return false;
                        }
                        MessageBox.Show("Die vorige Version ließ sich nicht zurückholen:\n" + err + "\n\nDas Mischpult startet jetzt abgesichert.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        safe = true;
                        mutex = new Mutex(true, UI.Update410.MutexName, out _);
                    }
                }
            }
            if (safe)
            {
                SafeMode = true;
                MixerSettings.SaveBlocked = true;
                UI.Update410.MakeSafe(settings);
            }
            return true;
        }

        /// <summary>Skalierung des Bildschirms, auf dem das Fenster aufgeht (wie in Windows eingestellt, z. B. 125 %).</summary>
        private static float DetectScale(MixerSettings settings)
        {
            string env = Environment.GetEnvironmentVariable("MISCHPULT_SCALE");
            if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0.5f && f < 4f)
                return f;
            try
            {
                Point at;
                if (settings != null && settings.WinX != int.MinValue && settings.WinW > 0)
                    at = new Point(settings.WinX + settings.WinW / 2, settings.WinY + Math.Max(10, Math.Min(200, settings.WinH / 3)));
                else
                {
                    // ohne gespeicherte Position geht das Fenster auf dem Bildschirm mit der Maus auf
                    var b = Screen.FromPoint(Cursor.Position).Bounds;
                    at = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
                }
                int dpi = Win32.MonitorDpi(at.X, at.Y);
                if (dpi > 0) return Math.Max(1f, dpi / 96f);
            }
            catch { }
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch { return 1f; }
        }

        private static MixerSettings DemoSettings()
        {
            var s = MixerSettings.CreateDefault();
            s.Channels[1].Apps.Add("game.exe");
            s.Channels[1].Fader = FaderLaw.DbToPos(-4f);
            s.Channels[2].Fader = FaderLaw.DbToPos(-9f);
            s.Channels[2].EqOn = true;
            s.Channels[2].EqLow = -6f;
            s.Channels[2].EqMid = 3f;
            s.Channels[2].EqHigh = 2f;
            s.Channels[3].Fader = FaderLaw.DbToPos(-18f);
            s.Channels[3].Pan = -0.2f;
            s.Channels[3].EqLow = 4.5f;
            s.Channels[3].EqHigh = 3f;
            s.Channels[4].Fader = FaderLaw.DbToPos(-12f);
            s.Channels[4].Mute = true;
            s.KnownApps.Add(new KnownApp { Key = "chrome.exe", Name = "Google Chrome" });
            s.MultiOutput = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MULTI") == "1";
            s.DuckOn = true;
            s.DuckThreshold = -30f;
            s.Normalize();
            s.Scenes.Add(new Scene { Id = "demo-zocken", Name = "Zocken", Channels = new System.Collections.Generic.List<SceneChannel>(), MasterVolume = -1f, DuckOn = true });
            s.Scenes.Add(new Scene { Id = "demo-musik", Name = "Musik hören", Channels = new System.Collections.Generic.List<SceneChannel>(), MasterVolume = -1f, DuckOn = false });
            foreach (var c in s.Channels)
            {
                s.Scenes[0].Channels.Add(new SceneChannel { Id = c.Id, Fader = c.Fader, Mute = c.Mute, Pan = c.Pan, EqOn = c.EqOn, EqLow = c.EqLow, EqMid = c.EqMid, EqHigh = c.EqHigh });
                bool music = c.Name == "MUSIK";
                s.Scenes[1].Channels.Add(new SceneChannel { Id = c.Id, Fader = music ? 1f : FaderLaw.DbToPos(-20f), Mute = c.Name == "SPIEL", EqOn = music, EqLow = music ? 4f : 0f, EqHigh = music ? 3f : 0f, EqMid = music ? -2f : 0f });
            }
            s.GameProfiles.Add(new GameProfile { AppKey = "game.exe", SceneId = "demo-zocken" });
            s.Channels[2].CompOn = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COMP") == "1";
            s.NightMode = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_NIGHT") == "1";
            s.MiniOn = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MINI") == "1";
            s.PttMode = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PTT") == "1" ? 1 : 0;
            s.PttKeys = (int)System.Windows.Forms.Keys.XButton1;
            s.RecordFolder = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_RECDIR");
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_V21") == "1")
                s.AppVolumes.Add(new AppVolume { Key = "game.exe", Pos = FaderLaw.DbToPos(-8f) });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_V21") == "1")
            {
                s.GameProfiles.Clear();
                s.StreamOn = true; s.StreamDevice = "demo-cable"; s.StreamDeviceName = "CABLE Input (VB-Audio Virtual Cable)";
                s.Channels[3].StreamOff = true;          // MUSIK nicht im Stream
                s.Channels[2].StreamDb = 3f;
                s.Channels[1].AutoOn = true;
                s.Channels[2].Geq = new float[] { 0, 0, -3, -2, 0, 2, 4, 3, 1, 0 };
                s.Groups.Add(new GroupConfig { Id = "demo-grp", Name = "ALLES AUSSER CHAT", Color = MixerSettings.Palette[4], Members = new System.Collections.Generic.List<string> { s.Channels[0].Id, s.Channels[1].Id, s.Channels[3].Id, s.Channels[4].Id }, Fader = FaderLaw.DbToPos(-6f) });
                s.VoiceOn = true; s.VoiceDevice = "demo-cable"; s.VoiceDeviceName = "CABLE Input (VB-Audio Virtual Cable)"; s.VoiceGateOn = true;
                string sb = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SOUNDS");
                if (!string.IsNullOrEmpty(sb) && System.IO.Directory.Exists(sb))
                    foreach (var f in System.IO.Directory.GetFiles(sb, "*.wav"))
                        s.Sounds.Add(new SoundPad { Id = System.IO.Path.GetFileNameWithoutExtension(f), Name = System.IO.Path.GetFileNameWithoutExtension(f), File = f, Volume = 0.8f });
            }
            s.Narrow = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_NARROW") == "1";
            s.BridgeOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGE") == "0";
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WINH"), out int wh) && wh > 300)
            {
                s.WinX = int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WINX"), out int wx) ? wx : 300; s.WinY = 40; s.WinW = int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WINW"), out int ww) && ww > 600 ? ww : 1100; s.WinH = wh;
                s.UiLevel = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OLD") == "1" ? 27 : 28;
            }
            s.VuOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VU") == "0";
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUFACE"), out s.VuFace);
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUSTART"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vs))
                s.VuStartDb = vs;
            s.VuScaleVu = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUSCALE") == "vu";
            s.LoudOn = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOUD") == "1";
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_METERSTYLE"), out s.MeterColors);
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LCDSTYLE"), out s.LcdStyle);
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CLOCKSTYLE"), out s.ClockStyle);
            s.ColorCapsOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CAPS") == "0";
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOCK") == "1" && s.Channels.Count > 2)
            {
                s.Channels[0].FaderLock = true;
                s.Channels[1].FaderMark = FaderLaw.DbToPos(-10f);
                s.Channels[2].FaderMark = 1f;
            }
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_GREET"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gt))
                UI.StripBase.FrozenGreetTime = gt;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PULTDIM"), out int pd) && pd > 0) { s.PultDimMode = 1; s.PultBright = pd; }
            s.SpecOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPEC") == "0";
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FADESEC"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fs)) s.FadeSec = fs;
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MOTOR_FREEZE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mf))
                UI.StripBase.FrozenMotorT = mf;
            s.SignalLedOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LEDS") == "0" || Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SIG") == "0";
            s.PeakLedOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LEDS") == "0" || Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PEAK") == "0";
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PEAKDB"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pdb))
                s.PeakLedDb = pdb;
            if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SELFTEST"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float st))
                UI.StripBase.FrozenTestTime = st;
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SCHEME"), out s.ColorScheme);
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_ACCENT"), out s.AccentColor);
            int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_STYLE"), out s.UiStyle);
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SCHED") == "1" && s.Scenes.Count > 1)
            {
                var t = DateTime.Now.AddMinutes(1);
                s.Schedules = new System.Collections.Generic.List<SceneSchedule>
                {
                    new SceneSchedule { SceneId = s.Scenes[1].Id, Minute = t.Hour * 60 + t.Minute, Days = SceneSchedule.AllDays },
                    new SceneSchedule { SceneId = s.Scenes[0].Id, Minute = 22 * 60, Days = 31 },
                    new SceneSchedule { SceneId = s.Scenes[0].Id, Minute = 7 * 60 + 30, Days = 96, Off = true },
                };
            }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_HOTPLUG") == "1")
            {
                s.AutoSwitchOn = true;
                s.AutoDevice = "demo-hp"; s.AutoDeviceName = "Headset (USB Audio Device)";
                s.AutoMic = "demo-mic2"; s.AutoMicName = "Headset-Mikrofon (USB Audio Device)";
                s.AutoFallback = "demo-spk"; s.AutoFallbackName = "Lautsprecher (Realtek(R) Audio)";
                s.AutoMicFallback = "demo-mic1"; s.AutoMicFallbackName = "Mikrofon (Webcam C920)";
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LAMP"), out int lm)) s.LampMode = lm;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEBAR"), out int bb)) s.BridgeBarWidth = bb;
            // 2.15 Optik
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_MATERIAL"), out int dm)) s.PanelMaterial = dm;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHEEK"), out int dch)) s.CheekStyle = dch;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_KNOB"), out int dk)) s.KnobStyle = dk;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CAP"), out int dcp)) s.CapStyle = dcp;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LEDSHAPE"), out int dls)) s.LedShape = dls;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LEDSIZE"), out int dlz)) s.LedSize = dlz;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VULIGHT"), out int dvl)) s.VuLight = dvl;
            s.DigitalPlates = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PLATES") == "1";
            s.LedAfterglowOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_AFTERGLOW") == "0";
            s.LedHaloOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_HALO") == "0";
            s.FaderGlowOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FADERGLOW") == "0";
            s.AmbilightOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_AMBI") == "0";
            s.GonioOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_GONIO") == "0";
            s.FastDrawOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FASTDRAW") == "0";
            // 2.16
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPECSTYLE"), out int dss)) s.SpecStyle = dss;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPECPALETTE"), out int dsp)) s.SpecPalette = dsp;   // 3.1
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGESTYLE"), out int dbs)) s.BridgeStyle = dbs;
            s.Compact = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COMPACT") == "1";
            s.LoudHistOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOUDHIST") == "0";
            string dgs = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_GONIOSRC");
            if (!string.IsNullOrEmpty(dgs)) s.GonioSource = dgs;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_METERDELAY"), out int dmd)) s.MeterDelay = dmd;
            s.MeterRealtimeOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_REALTIME") == "0";
            string dci = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHIMG");
            if (!string.IsNullOrEmpty(dci) && s.Channels.Count > 1) s.Channels[1].ImagePath = dci;
            string bgImg = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BGIMG");
            if (!string.IsNullOrEmpty(bgImg)) s.BgImagePath = bgImg;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LAMPPOS"), out int lpos)) s.LampPos = lpos;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_METERMODE"), out int mm)) s.MeterMode = mm;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_METERHOLD"), out int mh)) s.MeterHold = mh;
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LUFSMODE"), out int lum)) s.LufsMode = lum;
            s.LufsOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LUFS") == "0";
            s.LufsEachOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LUFSEACH") == "0";
            // 2.16.2: Meterbridge nach Wunsch – Kanal-Nummern (1 = erster Kanal) bzw. mic, master, out:<id>
            string dbh = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEHIDE");
            if (!string.IsNullOrEmpty(dbh))
            {
                s.BridgeHidden = new System.Collections.Generic.List<string>();
                foreach (var part in dbh.Split(','))
                    s.BridgeHidden.Add(int.TryParse(part, out int ci) && ci >= 1 && ci <= s.Channels.Count ? "ch:" + s.Channels[ci - 1].Id : part);
            }
            s.BridgePackOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEPACK") == "0";
            s.BridgeClockOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGECLOCK") == "0";
            string dbo = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEORDER");
            if (!string.IsNullOrEmpty(dbo)) s.BridgeOrder = dbo;
            // 2.16.3: frei angeordnet – Kanal-Nummern bzw. Schluessel, Komma-getrennt
            string dba = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEARRANGE");
            if (!string.IsNullOrEmpty(dba))
            {
                s.BridgeArrange = true;
                s.BridgeFreeOrder = new System.Collections.Generic.List<string>();
                foreach (var part in dba.Split(','))
                    s.BridgeFreeOrder.Add(int.TryParse(part, out int ci) && ci >= 1 && ci <= s.Channels.Count ? "ch:" + s.Channels[ci - 1].Id : part);
            }
            s.BridgeDragOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEDRAGON") == "0";
            // 2.17: Anzeigefeld (fuer alle bzw. "Nr:Feld,Nr:Feld"), Oszilloskop/Cover aus
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHFIELD"), out int dcf)) s.ChannelField = dcf;
            string dcfs = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHFIELDS");
            if (!string.IsNullOrEmpty(dcfs))
                foreach (var part in dcfs.Split(','))
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[0], out int ci) && int.TryParse(kv[1], out int cf) && ci >= 1 && ci <= s.Channels.Count) s.Channels[ci - 1].Field = cf;
                }
            s.OscOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OSC") == "0";
            s.TapePlates = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_TAPE") == "1";
            s.CoverOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COVERTILE") == "0";
            string dls2 = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOUDSRC");   // Geraete-ID fuer den Lautheits-Verlauf
            if (!string.IsNullOrEmpty(dls2)) { s.LoudHistSource = dls2; s.LoudHistSourceName = dls2 == "demo-hp" ? "Headset" : dls2 == "demo-spk" ? "Lautsprecher" : "Gerät"; }
            s.EqCurveOff = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_EQCURVE") == "0";
            string specSrc = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPECSRC");   // "*" = alle Ausgaben, sonst Geraete-ID
            if (!string.IsNullOrEmpty(specSrc)) { s.SpecSource = specSrc; s.SpecSourceName = specSrc == "demo-hp" ? "Headset" : specSrc == "demo-spk" ? "Lautsprecher" : "Gerät"; }
            if (s.MultiOutput) { s.Channels[2].OutputDevice = "demo-hp"; s.Channels[2].OutputDeviceName = "Headset (USB Audio Device)"; }
            UI.MixerForm.DemoFeatures12(s);   // 2.18: Baenke, Ansichten
            UI.MixerForm.DemoFeatures13(s);   // 2.19: Effekte, Vorhoeren
            UI.MixerForm.DemoFeatures14(s);   // 2.20: Design, 3D
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SAVERMIN"), out int dsm)) s.SaverMinutes = dsm;
            s.Normalize();
            return s;
        }

        private static int _reporting;

        private static void Report(Exception ex)
        {
            if (ex == null || Interlocked.Exchange(ref _reporting, 1) == 1) return;
            try
            {
                Directory.CreateDirectory(MixerSettings.LogFolder);
                File.AppendAllText(Path.Combine(MixerSettings.LogFolder, "fehler.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            try
            {
                MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten:\n\n" + ex.Message +
                                "\n\nDetails stehen in:\n" + Path.Combine(MixerSettings.LogFolder, "fehler.log"),
                    "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
            finally { Interlocked.Exchange(ref _reporting, 0); }
        }
    }
}
