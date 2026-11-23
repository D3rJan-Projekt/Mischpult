using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
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

        // ---- 4.2.7: Programme-Ordner (fuer alle, mit Admin-Abfrage) oder nur fuer mich (%LocalAppData%\Programs, ohne)
        public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mischpult");
        public static string MachineDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mischpult");
        public static bool MachineInstalled { get { try { return File.Exists(Path.Combine(MachineDir, "Mischpult.exe")); } catch { return false; } } }
        public static bool UserInstalled { get { try { return File.Exists(Path.Combine(UserDir, "Mischpult.exe")); } catch { return false; } } }

        /// <summary>Ziel der naechsten Installation: true = Programme-Ordner, false = nur fuer mich, null = wo es schon liegt.</summary>
        public static bool? Target;

        /// <summary>Liegt (bzw. kommt) das Mischpult im Programme-Ordner?</summary>
        public static bool Machine => Target ?? MachineInstalled;

        public static string InstallDir => Machine ? MachineDir : UserDir;
        public static string InstallExe => Path.Combine(InstallDir, "Mischpult.exe");
        private static string UserStartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mischpult.lnk");
        private static string MachineStartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Mischpult.lnk");
        public static string StartMenuLink => Machine ? MachineStartMenuLink : UserStartMenuLink;
        public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mischpult.lnk");
        private static RegistryKey UninstallRoot => Machine ? Registry.LocalMachine : Registry.CurrentUser;

        /// <summary>Laeuft dieses Programm mit Admin-Rechten?</summary>
        public static bool IsElevated
        {
            get { try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        }

        public const string NeedsAdmin = "Für den Programme-Ordner braucht es Admin-Rechte.";
        public const string Cancelled = "Abgebrochen – ohne Admin-Rechte geht der Programme-Ordner nicht. Tipp: „Nur für mich“ wählen, das geht ohne.";

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
                    using (var k = UninstallRoot.OpenSubKey(UninstallKey))
                        return k?.GetValue("DisplayVersion") as string;
                }
                catch { return null; }
            }
        }

        /// <summary>Installieren bzw. aktualisieren. Liefert null bei Erfolg, sonst den Fehler.</summary>
        public static string Install(bool desktop)
        {
            if (!Win32.IsWindows) return "Geht nur unter Windows.";
            if (Machine && !IsElevated) return NeedsAdmin;   // 4.2.7
            try
            {
                Directory.CreateDirectory(InstallDir);
                if (!RunningInstalled) File.Copy(Application.ExecutablePath, InstallExe, true);
                ShellLink.Create(StartMenuLink, InstallExe, null, "Mischpult – App-Audio-Mixer (made by " + Program.Author + ")");
                if (desktop) ShellLink.Create(DesktopLink, InstallExe, null, "Mischpult – App-Audio-Mixer");
                using (var k = UninstallRoot.CreateSubKey(UninstallKey))
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
                if (Machine) RemoveUserInstall();   // 4.2.7: Umzug in den Programme-Ordner – die alte Installation kommt weg
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>4.2.7: Nach dem Umzug die alte Installation "nur fuer mich" entfernen (Einstellungen bleiben).</summary>
        private static void RemoveUserInstall()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { if (File.Exists(UserStartMenuLink)) File.Delete(UserStartMenuLink); } catch { }
            try
            {
                if (!Directory.Exists(UserDir)) return;
                string exe = Path.Combine(UserDir, "Mischpult.exe");
                string self = Path.GetFullPath(Application.ExecutablePath);
                if (string.Equals(self, exe, StringComparison.OrdinalIgnoreCase))   // laeuft gerade von dort: beiseiteschieben
                    try { File.Move(exe, Path.Combine(Path.GetTempPath(), RemovedPrefix + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe")); } catch { }
                Directory.Delete(UserDir, true);
            }
            catch { }
        }

        /// <summary>Angezeigte Version im Windows-Eintrag setzen.</summary>
        public static void SetDisplayVersion(string v)
        {
            try { using (var k = UninstallRoot.OpenSubKey(UninstallKey, true)) if (k != null && v != null) k.SetValue("DisplayVersion", v); } catch { }
        }

        // ================================================================ 4.2.7 Admin-Rechte

        /// <summary>
        /// Diese EXE mit Admin-Rechten starten (Windows fragt) und warten. Liefert null bei Erfolg, Cancelled, wenn
        /// die Abfrage abgelehnt wurde, sonst den Fehler. Nur fuer Installieren, Aktualisieren und Zurueckholen.
        /// </summary>
        public static string RunElevated(string args, int timeoutMs = 180000)
        {
            string result = Path.Combine(Path.GetTempPath(), "mischpult-admin-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, args + " --ergebnis \"" + result + "\"") { Verb = "runas", UseShellExecute = true };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "Ließ sich nicht starten.";
                    if (!p.WaitForExit(timeoutMs)) return "Hat zu lange gedauert.";
                    if (p.ExitCode == 0) return null;
                    string msg = null;
                    try { if (File.Exists(result)) msg = File.ReadAllText(result).Trim(); } catch { }
                    return string.IsNullOrEmpty(msg) ? "Fehler " + p.ExitCode : msg;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return Cancelled; }   // Abfrage abgelehnt
            catch (Exception ex) { return ex.Message; }
            finally { try { if (File.Exists(result)) File.Delete(result); } catch { } }
        }

        /// <summary>Argumente fuer das Installieren mit Admin-Rechten.</summary>
        public static string AdminInstallArgs(bool desktop, bool backup, bool machine) =>
            "--admin install" + (desktop ? " --desk" : "") + (backup ? " --backup" : "") + (machine ? " --machine" : " --user");

        /// <summary>"--admin …": laeuft mit Admin-Rechten, tut genau eine Sache und beendet sich.</summary>
        public static int RunAdmin(string[] args)
        {
            string resultFile = null;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i].Equals("--ergebnis", StringComparison.OrdinalIgnoreCase)) resultFile = args[i + 1];
            bool has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
            string err;
            try
            {
                if (!IsElevated) err = NeedsAdmin;
                else if (has("install"))
                {
                    Target = has("--machine") ? true : has("--user") ? false : (bool?)null;
                    err = Update410.InstallSelf(has("--desk"), has("--backup"));
                }
                else if (has("restore")) err = Update410.RestorePrevious();
                else if (has("firewall"))   // 4.4.1: Fernbedienung in der Windows-Firewall freigeben (nur eigenes Teilnetz)
                {
                    int port = 0;
                    for (int i = 0; i + 1 < args.Length; i++)
                        if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out port);
                    err = port >= 1024 && port <= 65535 ? Firewall440.Allow(Application.ExecutablePath, port) : "Ungültiger Port.";
                }
                else err = "Unbekannter Auftrag.";
            }
            catch (Exception ex) { err = ex.Message; }
            if (err != null && resultFile != null)
                try { if (Path.GetFileName(resultFile).StartsWith("mischpult-admin-", StringComparison.Ordinal)) File.WriteAllText(resultFile, err); } catch { }
            return err == null ? 0 : 1;
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
            if (Machine && !IsElevated) return NeedsAdmin;   // 4.2.7
            try
            {
                try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch { }
                FileAssoc420.Remove();   // 4.2: Doppelklick auf Mischpult-Dateien
                try { if (File.Exists(DesktopLink)) File.Delete(DesktopLink); } catch { }
                try { UninstallRoot.DeleteSubKeyTree(UninstallKey, false); } catch { }
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
            if (Machine && !IsElevated)   // 4.2.7: Programme-Ordner – mit Admin-Rechten neu starten (Windows fragt)
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall") { Verb = "runas", UseShellExecute = true }); return 0; }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    MessageBox.Show("Ohne Admin-Rechte lässt sich das Mischpult nicht aus dem Programme-Ordner entfernen.", "Mischpult deinstallieren", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                catch (Exception ex) { MessageBox.Show("Das hat nicht geklappt: " + ex.Message, "Mischpult deinstallieren"); return 1; }
            }
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
