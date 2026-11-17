using System;
using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.1 Installieren & Aktualisieren
        [DataMember] public bool InstallPromptOff;   // 264 Ein-Klick-Fenster beim Start (Standard an – sonst sieht es niemand)
        [DataMember] public bool DropUpdateOn;       // 265 Update per Ziehen (neue EXE aufs Pult)
        [DataMember] public bool DownloadWatchOn;    // 266 neue Version im Download-Ordner erkennen
        [DataMember] public string DownloadIgnore;   //     "Bauzeit" einer Datei, zu der nicht mehr gefragt wird
        [DataMember] public bool UpdateBackupOn;     // 267 Sicherung vor jedem Update, vorige Version aufheben
        [DataMember] public bool StartGuardOn;       // 268 Start-Schutz
        [DataMember] public bool UninstallAskOn;     // 269 beim Deinstallieren fragen, ob die Einstellungen mit weg sollen
        [DataMember] public bool OnlineUpdateOn;     // 270 online nach Updates suchen
        [DataMember] public string UpdateRepo;       //     GitHub "Benutzer/Projekt" (null = Program.UpdateRepoDefault)
        [DataMember] public long UpdateLastCheck;    //     zuletzt nachgesehen (UTC-Ticks)
        [DataMember] public string UpdateSkip;       //     diese Version ueberspringen
        [DataMember] public bool PackageOn;          // 271 Umzugs-Paket
        [DataMember] public bool UpdateNewsOn;       // 272 Neuheiten im Update-Fenster
        [DataMember] public bool SafeStartOn;        // 273 abgesicherter Start mit der Umschalt-Taste
        [DataMember] public bool RepairOn;           // 274 Mischpult reparieren
        [DataMember] public int AutostartDelay;      // 275 Sekunden nach dem Windows-Start (0 = sofort)
        [DataMember] public bool OverviewOn;         // 276 Uebersicht "Was ist eingeschaltet"

        public static readonly int[] AutostartDelayValues = { 0, 10, 30, 60 };
        public static readonly string[] AutostartDelayNames = { "Sofort (Standard)", "Nach 10 Sekunden", "Nach 30 Sekunden", "Nach 1 Minute" };

        /// <summary>GitHub-Projekt fuer die Update-Suche (eigenes oder das eingebaute).</summary>
        public string UpdateRepoNow => string.IsNullOrWhiteSpace(UpdateRepo) ? Program.UpdateRepoDefault : UpdateRepo.Trim();

        /// <summary>"Benutzer/Projekt" aus einer Eingabe (auch eine ganze GitHub-Adresse) – null, wenn es nicht passt.</summary>
        public static string CleanRepo(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            int gh = s.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
            if (gh >= 0) s = s.Substring(gh + "github.com/".Length);
            s = s.Trim('/', ' ');
            var parts = s.Split('/');
            if (parts.Length < 2) return null;
            string owner = parts[0].Trim(), repo = parts[1].Trim();
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
            if (!Ok(owner) || !Ok(repo)) return null;
            return owner + "/" + repo;
        }

        private static bool Ok(string p)
        {
            if (p.Length == 0 || p.Length > 100) return false;
            foreach (char c in p) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '-' && c != '_' && c != '.') return false;
            return true;
        }

        private void Normalize410()
        {
            if (Array.IndexOf(AutostartDelayValues, AutostartDelay) < 0) AutostartDelay = 0;
            if (UpdateRepo != null) UpdateRepo = CleanRepo(UpdateRepo);
            if (UpdateLastCheck < 0) UpdateLastCheck = 0;
            if (string.IsNullOrWhiteSpace(UpdateSkip)) UpdateSkip = null;
            if (string.IsNullOrWhiteSpace(DownloadIgnore)) DownloadIgnore = null;
        }
    }
}
