using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.7 – Studio-Fenster am PC: die Studio-Ansicht der Fernbedienung (MIX, EDIT mit EQ-Kurve, PEGEL …) als eigenes Fenster
    /// hier am PC – ohne Handy und auch, wenn die Fernbedienung aus ist. Ein kleiner Server nur fuer diesen PC (127.0.0.1),
    /// geoeffnet als App-Fenster von Microsoft Edge (bzw. Chrome) ohne Adressleiste. Schalter „Studio-Fenster am PC“ (Standard aus).
    /// Im Fenster gilt alles als eingeschaltet, was unter der Fernbedienung nur fuers Handy abschaltbar ist.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private RemoteServer440 _studio470;
        private volatile string _studioState470 = "{}";
        private double _nextStudioState470, _studioRetry470;
        private string _studioErr470;

        /// <summary>true, solange Zustand oder Befehl fuer das Studio-Fenster am PC laufen (pro Thread).</summary>
        [ThreadStatic] private static bool _rl470;

        /// <summary>Schalter der Fernbedienung („… am Handy“ aus) – im Studio-Fenster am PC immer an.</summary>
        private static bool ROff470(bool off) => off && !_rl470;

        private void TickStudio470(double now)
        {
            if (!_settings.StudioWinOn) { if (_studio470 != null) StopStudio470(); return; }
            if (_studio470 == null)
            {
                if (now < _studioRetry470) return;
                _studioRetry470 = now + 10.0;
                StartStudio470();   // nach einem Neustart: ein offenes Fenster verbindet sich von selbst wieder
                if (_studio470 == null) return;
            }
            _remoteLocked = PultLockedNow;
            if ((DateTime.UtcNow - _studio470.LastRequestUtc).TotalSeconds > 6) return;   // nur rechnen, wenn das Fenster offen ist
            if (now < _nextStudioState470) return;
            _nextStudioState470 = now + 0.1;
            _rl470 = true;
            try { _studioState470 = BuildRemoteState440(); } catch { }
            finally { _rl470 = false; }
        }

        private bool StartStudio470()
        {
            if (_studio470 != null) return true;
            if (!MixerSettings.ValidRemoteCode(_settings.StudioCode)) { _settings.StudioCode = MixerSettings.NewRemoteCode(); ScheduleSave(); }
            int pref = _settings.RemotePort == 8766 ? 8767 : 8766;
            foreach (int port in new[] { pref, 0 })
            {
                var srv = new RemoteServer440(port)
                {
                    LoopbackOnly = true, Page = RemotePage440(), AppIcon = RemoteAppIcon440(), Code = _settings.StudioCode,
                    State = () => _studioState470, Icon = RemoteIcon440,
                };
                srv.Command = (n, a) => { _rl470 = true; try { return RemoteCommand440(n, a); } finally { _rl470 = false; } };
                if (srv.Start()) { _studio470 = srv; _studioErr470 = null; _nextStudioState470 = 0; return true; }
                _studioErr470 = srv.Error;
                srv.Dispose();
            }
            return false;
        }

        private void StopStudio470()
        {
            try { _studio470?.Dispose(); } catch { }
            _studio470 = null;
            _studioState470 = "{}";
        }

        internal string StudioUrl470() =>
            _studio470 == null ? null : "http://127.0.0.1:" + _studio470.Port.ToString(CultureInfo.InvariantCulture) + "/#c=" + _settings.StudioCode + "&v=studio";

        /// <summary>Edge oder Chrome (fuer ein App-Fenster ohne Adressleiste), sonst null.</summary>
        internal static string FindAppBrowser470()
        {
            if (!Win32.IsWindows) return null;
            foreach (var exe in new[] { "msedge.exe", "chrome.exe", "brave.exe" })
                foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        {
                            string p = (k?.GetValue(null) as string)?.Trim().Trim('"');
                            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                        }
                    }
                    catch { }
                }
            foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string p = Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private void OpenStudio470()
        {
            if (!_settings.StudioWinOn) { _settings.StudioWinOn = true; ScheduleSave(); }
            if (!StartStudio470())
            {
                MessageBox.Show(this, (_studioErr470 ?? "Das Studio-Fenster konnte nicht starten.") + "\n\nBitte das Mischpult neu starten.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string url = StudioUrl470();
            if (_demo) { ShowOsd("Studio-Fenster", url, -1, Theme.Accent); return; }   // Vorschau: kein Browser
            string br = FindAppBrowser470();
            try
            {
                if (br != null)
                    Process.Start(new ProcessStartInfo(br, "--app=\"" + url + "\" --window-size=1280,820") { UseShellExecute = false });
                else
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    ShowOsd("Studio-Fenster", "Im Browser geöffnet – mit Edge oder Chrome kommt es als eigenes Fenster ohne Adressleiste.", -1, Theme.Accent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Das Studio-Fenster ließ sich nicht öffnen: " + ex.Message, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddStudio470(SPage p)
        {
            p.Section("Studio-Fenster (am PC)");
            p.Toggle("Studio-Fenster am PC", "Die Studio-Ansicht als eigenes Fenster hier am PC – ohne Handy und auch, wenn die Fernbedienung aus ist: MIX mit allen Kanalzügen, EDIT mit EQ-Kurve zum Ziehen, PEGEL, Szenen, DUCKING und Musik-Tasten. " +
                    "Öffnen über den Knopf darunter, per Rechtsklick auf OPTIONEN oder über das Mischpult-Symbol unten rechts in der Taskleiste. Nur auf diesem PC, nicht im Heimnetz. Im Fenster: F11 = Vollbild.",
                () => _settings.StudioWinOn, v =>
                {
                    _settings.StudioWinOn = v;
                    if (v) BeginInvoke(new Action(OpenStudio470)); else StopStudio470();
                    ScheduleSave();
                });
            p.Button("Studio-Fenster öffnen", "Öffnet die Studio-Ansicht als eigenes Fenster (über Microsoft Edge oder Chrome, ohne Adressleiste).", "Öffnen", r => OpenStudio470())
                .Visible = () => _settings.StudioWinOn;
        }

        private void DemoShown470()
        {
            string rc = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_REMOTECODE");
            if (MixerSettings.ValidRemoteCode(rc)) _settings.StudioCode = rc;
            if (DemoOn("STUDIOWIN")) DemoLater(() => { _settings.StudioWinOn = true; StartStudio470(); });
        }
    }
}
