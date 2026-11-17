using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0: Installieren ohne Setup-Programm. Die EXE kommt nach %LocalAppData%\Programs\Mischpult, dazu Verknuepfungen
    /// im Startmenue (und auf Wunsch auf dem Desktop) und ein Eintrag unter "Apps" in den Windows-Einstellungen
    /// zum Deinstallieren. Keine Admin-Rechte noetig. Die Einstellungen bleiben beim Deinstallieren liegen.
    /// </summary>
    internal static class Installer
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mischpult";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mischpult");
        public static string InstallExe => Path.Combine(InstallDir, "Mischpult.exe");
        public static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mischpult.lnk");
        public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mischpult.lnk");

        public static bool IsInstalled
        {
            get { try { return File.Exists(InstallExe); } catch { return false; } }
        }

        /// <summary>Laeuft gerade die installierte EXE?</summary>
        public static bool RunningInstalled
        {
            get
            {
                try { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstallExe), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }

        public static string InstalledVersion
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                        return k?.GetValue("DisplayVersion") as string;
                }
                catch { return null; }
            }
        }

        /// <summary>Installieren bzw. aktualisieren. Liefert null bei Erfolg, sonst den Fehler.</summary>
        public static string Install(bool desktop)
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            try
            {
                Directory.CreateDirectory(InstallDir);
                if (!RunningInstalled) File.Copy(Application.ExecutablePath, InstallExe, true);
                ShellLink.Create(StartMenuLink, InstallExe, null, "Mischpult – App-Audio-Mixer (made by " + Program.Author + ")");
                if (desktop) ShellLink.Create(DesktopLink, InstallExe, null, "Mischpult – App-Audio-Mixer");
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Mischpult");
                    k.SetValue("DisplayVersion", Program.Version);
                    k.SetValue("Publisher", Program.Author);
                    k.SetValue("DisplayIcon", InstallExe + ",0");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + InstallExe + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + InstallExe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    try { k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstallExe).Length / 1024), RegistryValueKind.DWord); } catch { }
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
                // Autostart auf die installierte EXE umbiegen (4.1: Verzoegerung bleibt)
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (k.GetValue("Mischpult") is string old) k.SetValue("Mischpult", RunValue(InstallExe, DelayOf(old)));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>4.1: Was beim Loeschen der Einstellungen wegkommt.</summary>
        public const string WipeQuestion = "Ja = alles weg: Einstellungen, Profile, Sicherungen und Protokolle.\nNein = Einstellungen behalten (für später oder einen anderen PC).\n\nAufnahmen, Bilder vom Pult und Fehlerberichte bleiben immer.";

        /// <summary>4.1: Befehl fuer "Mit Windows starten" (unsichtbar, auf Wunsch etwas spaeter).</summary>
        public static string RunValue(string exe, int delaySec) =>
            "\"" + exe + "\" --minimized" + (delaySec > 0 ? " --verzoegert " + delaySec : "");

        /// <summary>4.1: Verzoegerung aus einem Autostart-Befehl (0 = keine).</summary>
        public static int DelayOf(string runValue)
        {
            if (string.IsNullOrEmpty(runValue)) return 0;
            int i = runValue.IndexOf("--verzoegert", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            var rest = runValue.Substring(i + "--verzoegert".Length).Trim().Split(' ');
            return rest.Length > 0 && int.TryParse(rest[0], out int s) ? Math.Max(0, Math.Min(300, s)) : 0;
        }

        /// <summary>4.1: Verzoegerung aus den Startargumenten.</summary>
        public static int DelayArg(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("--verzoegert", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int s)) return Math.Max(0, Math.Min(300, s));
            return 0;
        }

        /// <summary>Verknuepfungen, Eintrag und Autostart entfernen; der Ordner wird nach dem Beenden geloescht.</summary>
        public static string Uninstall()
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            try
            {
                try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch { }
                FileAssoc420.Remove();   // 4.2: Doppelklick auf Mischpult-Dateien
                try { if (File.Exists(DesktopLink)) File.Delete(DesktopLink); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        string v = k.GetValue("Mischpult") as string;
                        if (v != null && v.IndexOf(InstallDir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue("Mischpult", false);
                    }
                }
                catch { }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>4.2.4: so heisst die beim Deinstallieren in den Temp-Ordner verschobene EXE.</summary>
        public const string RemovedPrefix = "Mischpult-entfernt-";

        /// <summary>
        /// Den Programm-Ordner entfernen. Die laufende EXE kann sich nicht selbst loeschen – sie wird in den Temp-Ordner
        /// verschoben (das geht, waehrend sie laeuft), dann wird der Ordner geloescht. Den Rest im Temp-Ordner raeumt
        /// Windows (Speicheroptimierung) weg – oder eine spaeter wieder installierte Version.
        /// 4.2.4: ohne Eingabeaufforderung im Hintergrund.
        /// </summary>
        public static void ScheduleFolderDelete()
        {
            try
            {
                if (!Directory.Exists(InstallDir)) return;
                try { Environment.CurrentDirectory = Path.GetTempPath(); } catch { }   // sonst haelt der Prozess den Ordner fest
                string self = null;
                try { self = Path.GetFullPath(Application.ExecutablePath); } catch { }
                string dir = Path.GetFullPath(InstallDir).TrimEnd('\\') + "\\";
                if (self != null && self.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    string tmp = Path.Combine(Path.GetTempPath(), RemovedPrefix + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                    try { File.Move(self, tmp); } catch { }
                }
                for (int i = 0; i < 10; i++)
                {
                    try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); return; }
                    catch { System.Threading.Thread.Sleep(300); }
                }
            }
            catch { }
        }

        /// <summary>"--uninstall": nachfragen, laufendes Mischpult beenden, entfernen.</summary>
        public static int RunUninstall()
        {
            bool ask = false;
            try { ask = MixerSettings.Load().UninstallAskOn; } catch { }   // 4.1: fragen, ob die Einstellungen mit weg sollen
            bool wipe = false;
            if (ask)
            {
                var r = MessageBox.Show("Mischpult deinstallieren?\n\nSollen auch deine Einstellungen gelöscht werden?\n\n" + WipeQuestion,
                    "Mischpult deinstallieren", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.Cancel) return 1;
                wipe = r == DialogResult.Yes;
            }
            else if (MessageBox.Show("Mischpult deinstallieren?\n\nDie Einstellungen bleiben erhalten (Ordner " + (MixerSettings.DocsMode ? "Dokumente\\Mischpult" : MixerSettings.Folder) + ").",
                    "Mischpult deinstallieren", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1;
            try
            {
                if (Program.QuitMessage != 0) Win32.PostMessage(Win32.HWND_BROADCAST, Program.QuitMessage, IntPtr.Zero, IntPtr.Zero);
                using (var m = new System.Threading.Mutex(false, @"Local\Mischpult_SingleInstance_7F3A"))
                {
                    bool got = false;
                    try { got = m.WaitOne(8000); } catch (System.Threading.AbandonedMutexException) { got = true; }
                    if (got) m.ReleaseMutex();
                }
            }
            catch { }
            string err = Uninstall();
            if (err == null && wipe) Update410.WipeSettings();
            MessageBox.Show(err == null ? "Mischpult wurde entfernt" + (wipe ? " – samt Einstellungen" : "") + ". Danke fürs Benutzen!" : "Das hat nicht ganz geklappt: " + err, "Mischpult", MessageBoxButtons.OK,
                err == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (err == null) ScheduleFolderDelete();
            return 0;
        }
    }
}
