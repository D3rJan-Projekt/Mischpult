using System;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>4.2.5 – Updates beim Start automatisch installieren (Schalter, Standard aus).</summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Darf jetzt ohne Nachfrage aktualisiert werden? Nur die installierte Version, kein Spiel vorne.</summary>
        private bool AutoInstallOk425 => !_demo && Win32.IsWindows && Installer.RunningInstalled && !MixerSettings.Portable && !_gaming;

        /// <summary>Neue Version im Hintergrund laden und ohne Fenster installieren. Klappt etwas nicht, kommt der normale Hinweis.</summary>
        private void AutoInstall425(OnlineUpdate410.Found f)
        {
            ShowOsd("Update", "Mischpult " + f.Version + " wird geladen …", -1, Theme.Accent);
            _onlineBusy410 = true;
            _onlineStatus410 = "Mischpult " + f.Version + " wird automatisch installiert …";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string path = OnlineUpdate410.Download(f, null, out string err);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _onlineBusy410 = false;
                        if (path == null) { _onlineStatus410 = "Automatisches Update hat nicht geklappt: " + err; ShowOnlineNotice410(f); return; }
                        if (_gaming) { ShowOnlineNotice410(f); return; }   // inzwischen ein Spiel vorne: lieber fragen
                        ShowOsd("Update", "Mischpult " + f.Version + " wird installiert …", -1, Theme.Accent);
                        UpdateFromExe410(path, true);
                    }));
                }
                catch { _onlineBusy410 = false; }
            });
        }
    }
}
