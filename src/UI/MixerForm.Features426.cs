using System;
using System.Diagnostics;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.2.6 – GitHub-Links: Projektseite und "Fehler melden" (vorausgefuelltes Formular im Browser).
    /// Es wird nichts automatisch verschickt – der Browser zeigt alles vorher. Schalter, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private string RepoUrl426 => "https://github.com/" + _settings.UpdateRepoNow;

        /// <summary>Windows-Version fuer das Formular, z. B. "Windows 11 (Build 22631)".</summary>
        internal static string WindowsText426()
        {
            try
            {
                var v = Environment.OSVersion.Version;
                if (v.Major == 10) return (v.Build >= 22000 ? "Windows 11" : "Windows 10") + " (Build " + v.Build + ")";
                return "Windows " + v.Major + "." + v.Minor + " (Build " + v.Build + ")";
            }
            catch { return ""; }
        }

        /// <summary>Adresse des vorausgefuellten Formulars "Fehler melden".</summary>
        internal static string BugUrl426(string repo) =>
            "https://github.com/" + repo + "/issues/new?template=fehler.yml&version=" + Uri.EscapeDataString(Program.Version) +
            "&windows=" + Uri.EscapeDataString(WindowsText426());

        private void OpenUrl426(string url, string what)
        {
            if (_demo) { ShowOsd(what, "Vorschau: " + url, -1, Theme.Accent); return; }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { ShowOsd(what, "Browser ließ sich nicht öffnen: " + ex.Message, -1, Theme.MuteRed); }
        }

        private void OpenGitHub426() => OpenUrl426(RepoUrl426, "GitHub");
        private void ReportBug426() => OpenUrl426(BugUrl426(_settings.UpdateRepoNow), "Fehler melden");

        /// <summary>Seite Allgemein, unter den Updates.</summary>
        private void AddGitHub426(SPage p)
        {
            p.Toggle("GitHub-Links", "Im Fenster „Über Mischpult“ und hier stehen „Auf GitHub“ (Projektseite mit allen Versionen) und „Fehler melden“ (ein Formular im Browser, Version und Windows sind schon eingetragen). Es wird nichts automatisch verschickt.",
                () => _settings.GitHubLinksOn, v => { _settings.GitHubLinksOn = v; ScheduleSave(); });
            p.Button("Mischpult auf GitHub", () => RepoUrl426, () => "Öffnen", r => OpenGitHub426()).Visible = () => _settings.GitHubLinksOn && !string.IsNullOrEmpty(_settings.UpdateRepoNow);
            p.Button("Fehler melden", "Öffnet das Formular im Browser. Tipp: Die Datei fehler.log aus Dokumente\\Mischpult\\Protokolle hilft beim Finden.", "Melden …", r => ReportBug426())
                .Visible = () => _settings.GitHubLinksOn && !string.IsNullOrEmpty(_settings.UpdateRepoNow);
        }
    }
}
