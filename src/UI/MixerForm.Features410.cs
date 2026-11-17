using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Mischpult.Native;
using Timer = System.Windows.Forms.Timer;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.1 – Installieren &amp; Aktualisieren: Update per Ziehen, Download-Ordner, online nachsehen, vorige Version,
    /// Start-Schutz, abgesicherter Start, Umzugs-Paket, Reparieren, Autostart-Verzoegerung, Uebersicht "Was ist eingeschaltet".
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Alle 4.1-Schalter (Standard aus) – fuer die Vorschau per MISCHPULT_DEMO_&lt;NAME&gt;=1.</summary>
        internal static readonly string[] Bools410 =
        {
            "DropUpdateOn", "DownloadWatchOn", "UpdateBackupOn", "StartGuardOn", "UninstallAskOn", "OnlineUpdateOn", "PackageOn",
            "UpdateNewsOn", "SafeStartOn", "RepairOn", "OverviewOn", "AutoUpdateOn",
        };

        private double _now410;
        private bool _guardDone410;
        private AskWin410 _notice410, _safeWin410;

        private void ApplyUpdate410(MixerSettings s, bool demo)
        {
            if (!demo) return;
            foreach (var f in Bools410)
                if (DemoFlag(demo, f.ToUpperInvariant())) typeof(MixerSettings).GetField(f)?.SetValue(s, true);
            if (DemoFlag(demo, "PROMPTOFF")) s.InstallPromptOff = true;
            string repo = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_REPO");
            if (!string.IsNullOrEmpty(repo)) s.UpdateRepo = MixerSettings.CleanRepo(repo);
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_AUTODELAY"), out int ad)) s.AutostartDelay = ad;
        }

        /// <summary>Einmal nach dem Anzeigen.</summary>
        private void HookUpdate410()
        {
            SetDrop410();
            if (Program.SafeMode) DemoOrLater410(1200, ShowSafeNotice410);
            if (_demo) DemoShown410();
        }

        private void DemoOrLater410(int ms, Action a)
        {
            var t = new Timer { Interval = ms };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { a(); } catch { } };
            t.Start();
        }

        private bool _started410;

        /// <summary>Jede Sekunde – auch wenn das Pult unsichtbar im Infobereich startet.</summary>
        private void TickUpdate410(double now)
        {
            _now410 = now;
            if (!_started410)
            {
                _started410 = true;
                if (!_demo && Win32.IsWindows && Installer.RunningInstalled) Update410.CleanupLeftovers();
                SetDownloadWatch410(_settings.DownloadWatchOn);
            }
            if (!_guardDone410 && now > 15)
            {
                _guardDone410 = true;
                if (!_demo) Update410.GuardEnd();   // Start hat geklappt
            }
            if (_dlCheckAt410 > 0 && now >= _dlCheckAt410) { _dlCheckAt410 = -1; CheckDownloads410(); }
            if (_settings.OnlineUpdateOn && !_onlineBusy410 && _onlineAt410 >= 0 && now >= _onlineAt410)
            {
                _onlineAt410 = now + 6 * 3600;
                bool auto = _settings.AutoUpdateOn && now < 180;   // 4.2.5: kurz nach dem Start immer nachsehen und selbst installieren
                if (auto || DateTime.UtcNow.Ticks - _settings.UpdateLastCheck > TimeSpan.FromHours(20).Ticks) CheckOnline410(false, auto);
            }
        }

        private void CleanupUpdate410()
        {
            if (!_demo) Update410.GuardEnd();   // normal beendet = kein Absturz
            SetDownloadWatch410(false);
            try { _notice410?.Close(); } catch { }
            try { _safeWin410?.Close(); } catch { }
        }

        // ================================================================ Hinweis unten rechts

        private AskWin410 ShowNotice410(string title, string heading, string body)
        {
            try { _notice410?.Close(); } catch { }
            var w = new AskWin410(title, 440) { Toast = true, Heading = heading, Body = body };
            _notice410 = w;
            w.FormClosed += (s, e) => { if (_notice410 == w) _notice410 = null; };
            return w;
        }

        // ================================================================ 265 Update per Ziehen / 271 Paket aufs Pult

        private readonly HashSet<Control> _dropHooked410 = new HashSet<Control>();
        private bool DropWanted410 => _settings.DropUpdateOn || _settings.PackageOn;

        private void SetDrop410()
        {
            bool on = DropWanted410;
            try { AllowDrop = on; } catch { }
            if (on) HookDrop410(this);
        }

        private void HookDrop410(Control c)
        {
            if (c == null || _dropHooked410.Contains(c)) return;
            if (c != this && c is Form) return;   // eigene Fenster (Soundboard …) nicht
            _dropHooked410.Add(c);
            if (c != this)
            {
                try { c.AllowDrop = true; } catch { }
                c.DragEnter += OnDragEnter410;
                c.DragDrop += OnDragDrop410;
            }
            c.ControlAdded += (s, e) => { if (DropWanted410) HookDrop410(e.Control); };
            foreach (Control k in c.Controls) HookDrop410(k);
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnDragEnter410(this, e); }
        protected override void OnDragDrop(DragEventArgs e) { base.OnDragDrop(e); OnDragDrop410(this, e); }

        private string DroppedFile410(DragEventArgs e)
        {
            try
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length != 1) return null;
                string ext = Path.GetExtension(files[0]);
                if (_settings.DropUpdateOn && ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return files[0];
                if (_settings.PackageOn && ext.Equals(Package410.Ext, StringComparison.OrdinalIgnoreCase)) return files[0];
            }
            catch { }
            return null;
        }

        private void OnDragEnter410(object sender, DragEventArgs e)
        {
            if (DroppedFile410(e) != null) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop410(object sender, DragEventArgs e)
        {
            string f = DroppedFile410(e);
            if (f == null) return;
            BeginInvoke(new Action(() =>
            {
                if (Path.GetExtension(f).Equals(Package410.Ext, StringComparison.OrdinalIgnoreCase)) ImportPackage410(f);
                else UpdateFromExe410(f);
            }));
        }

        /// <summary>Neuere EXE starten – sie zeigt das Update-Fenster und loest diese Version ab.</summary>
        private void UpdateFromExe410(string path, bool auto = false)
        {
            var e = Update410.Read(path);
            if (e == null || !e.IsMischpult) { ShowOsd("Update", "Das ist keine Mischpult-Datei", -1, Theme.MuteRed); return; }
            if (!Update410.IsNewer(e)) { ShowOsd("Nicht neuer", e.Text + " – hier läuft " + Program.Version, -1, Theme.SoloAmber); return; }
            if (MixerSettings.Portable) { ShowOsd("Portabler Modus", "Bitte die EXE im Ordner selbst austauschen", -1, Theme.SoloAmber); return; }
            if (_demo) { ShowOsd("Update", "Vorschau: " + e.Text, -1, Theme.Accent); return; }
            RememberWindow();
            SaveNow();
            try { Process.Start(path, auto ? "--update --auto-update" : "--update"); }   // 4.2.5: ohne Fenster
            catch (Exception ex) { ShowOsd("Update", "Ließ sich nicht starten: " + ex.Message, -1, Theme.MuteRed); }
        }

        // ================================================================ 266 Download-Ordner

        private FileSystemWatcher _dlWatch410;
        private double _dlCheckAt410 = -1;
        private int _dlShown410;

        private void SetDownloadWatch410(bool on)
        {
            try { if (_dlWatch410 != null) { _dlWatch410.EnableRaisingEvents = false; _dlWatch410.Dispose(); } } catch { }
            _dlWatch410 = null;
            if (!on || _demo) { if (on && _demo) _dlCheckAt410 = _now410 + 2; return; }
            try
            {
                string dir = Update410.DownloadsFolder;
                if (!Directory.Exists(dir)) return;
                var w = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => NoteDownload410(e.Name);
                w.Created += h; w.Changed += h;
                w.Renamed += (s, e) => NoteDownload410(e.Name);
                w.EnableRaisingEvents = true;
                _dlWatch410 = w;
                _dlCheckAt410 = _now410 + 20;   // beim Start einmal nachsehen
            }
            catch { }
        }

        private void NoteDownload410(string name)
        {
            if (name == null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            try { if (IsHandleCreated) BeginInvoke(new Action(() => _dlCheckAt410 = _now410 + 3)); } catch { }
        }

        private void CheckDownloads410()
        {
            if (!_settings.DownloadWatchOn) return;
            if (_demo)
            {
                if (DemoOn("DLNOTICE")) ShowDownloadNotice410(new Update410.ExeInfo { IsMischpult = true, Version = "4.2", Stamp = Program.BuildStamp + 1000, Path = "Mischpult.exe" });
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var e = Update410.NewestDownload();
                if (e == null) return;
                try { BeginInvoke(new Action(() => ShowDownloadNotice410(e))); } catch { }
            });
        }

        private void ShowDownloadNotice410(Update410.ExeInfo e)
        {
            if (e.Stamp == _dlShown410 || e.Stamp.ToString() == _settings.DownloadIgnore || !_settings.DownloadWatchOn) return;
            _dlShown410 = e.Stamp;
            var w = ShowNotice410("Neue Version", "Mischpult " + (e.Version ?? "") + " ist da",
                "Im Download-Ordner liegt " + e.Text + " – hier läuft " + Program.Version + ". Mit einem Klick aktualisieren, deine Einstellungen bleiben.");
            w.AddKey("never", "IGNORIEREN").AddKey("later", "SPÄTER").AddKey("go", "JETZT AKTUALISIEREN", true);
            w.Clicked = id =>
            {
                if (id == "go") UpdateFromExe410(e.Path);
                else if (id == "never") { _settings.DownloadIgnore = e.Stamp.ToString(); ScheduleSave(); }
                return true;
            };
            w.Show(this);
        }

        // ================================================================ 270 online nach Updates suchen

        private double _onlineAt410 = 45;   // erst 45 s nach dem Start
        private bool _onlineBusy410;
        private string _onlineStatus410;

        private void CheckOnline410(bool manual, bool auto = false)
        {
            if (_onlineBusy410) return;
            string repo = _settings.UpdateRepoNow;
            if (string.IsNullOrEmpty(repo))
            {
                _onlineStatus410 = "Erst das GitHub-Projekt eintragen.";
                if (manual) EditRepo410();
                return;
            }
            if (_demo)
            {
                _onlineStatus410 = "Vorschau: zuletzt nachgesehen heute " + DateTime.Now.ToString("HH:mm") + " – Mischpult 4.2 ist da.";
                if (manual || DemoOn("ONLINENOTICE")) ShowOnlineNotice410(new OnlineUpdate410.Found { Version = "4.2", Title = "Mischpult 4.2", Notes = "Neue Designs\nSchneller beim Start\nFehler behoben" });
                return;
            }
            _onlineBusy410 = true;
            _onlineStatus410 = "Sucht …";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var f = OnlineUpdate410.Check(repo, out string err);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _onlineBusy410 = false;
                        _settings.UpdateLastCheck = DateTime.UtcNow.Ticks;
                        ScheduleSave();
                        string when = "Zuletzt nachgesehen: heute " + DateTime.Now.ToString("HH:mm") + " – ";
                        if (f == null) { _onlineStatus410 = when + (err ?? "nichts gefunden."); if (manual) ShowOsd("Updates", err ?? "Nichts gefunden", -1, Theme.SoloAmber); return; }
                        if (!f.IsNewer) { _onlineStatus410 = when + "du hast die neueste Version (" + Program.Version + ")."; if (manual) ShowOsd("Updates", "Du hast die neueste Version", -1, Theme.LedGreen); return; }
                        _onlineStatus410 = when + "Mischpult " + f.Version + " ist da.";
                        if (auto && !manual && f.Version != _settings.UpdateSkip && AutoInstallOk425) { AutoInstall425(f); return; }   // 4.2.5
                        if (manual || f.Version != _settings.UpdateSkip) ShowOnlineNotice410(f);
                    }));
                }
                catch { _onlineBusy410 = false; }
            });
        }

        private void ShowOnlineNotice410(OnlineUpdate410.Found f)
        {
            string notes = f.ShortNotes(4);
            var w = ShowNotice410("Update", f.Title + " ist da", "Hier läuft " + Program.Version + ". Mit einem Klick laden und aktualisieren – deine Einstellungen bleiben." + (notes != null ? "\n\n" + notes : ""));
            w.AddKey("skip", "ÜBERSPRINGEN").AddKey("later", "SPÄTER").AddKey("go", "JETZT AKTUALISIEREN", true);
            w.Clicked = id =>
            {
                if (id == "skip") { _settings.UpdateSkip = f.Version; ScheduleSave(); return true; }
                if (id != "go") return true;
                if (_demo) { w.ShowBusy("Vorschau: lädt … 42 %"); return false; }
                w.ShowBusy("Lädt …");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string path = OnlineUpdate410.Download(f, pct => { try { BeginInvoke(new Action(() => { if (!w.IsDisposed) w.ShowBusy(pct >= 0 ? "Lädt … " + pct + " %" : "Lädt …"); })); } catch { } }, out string err);
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (w.IsDisposed) return;
                            if (path == null) { w.Busy = null; w.Heading = "Das hat nicht geklappt"; w.Body = err; w.SetKeys("later", "SCHLIESSEN"); return; }
                            w.Close();
                            UpdateFromExe410(path);
                        }));
                    }
                    catch { }
                });
                return false;
            };
            w.Show(this);
        }

        private void EditRepo410()
        {
            using (var dlg = new InputDialog("GitHub-Projekt", "Benutzer/Projekt oder die GitHub-Adresse:", _settings.UpdateRepoNow, 200))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                string r = MixerSettings.CleanRepo(dlg.Value);
                if (r == null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das passt nicht. Beispiel: D3r_Jan/Mischpult", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _settings.UpdateRepo = r == Program.UpdateRepoDefault ? null : r;
                _settings.UpdateSkip = null;
                _onlineStatus410 = null;
                ScheduleSave();
            }
        }

        // ================================================================ 267 vorige Version

        private void RestorePrev410()
        {
            var p = Update410.PrevInfo;
            if (p == null) return;
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Die vorige Version (" + p.Text + ") zurückholen?\n\nDiese Version (" + Program.Version + ") wird dafür aufgehoben – du kannst also auch wieder zurück. Das Mischpult startet neu.",
                    "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (_demo) { ShowOsd("Vorige Version", "Vorschau: nichts geändert", -1, Theme.Accent); return; }
            RememberWindow();
            SaveNow();
            string err = Update410.RestorePrevious();
            if (err != null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das hat nicht geklappt: " + err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _importing = true;
            Program.RestartPath = Installer.InstallExe;
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 273 abgesicherter Start

        private void ShowSafeNotice410()
        {
            if (_safeWin410 != null && !_safeWin410.IsDisposed) { _safeWin410.Activate(); return; }
            var w = new AskWin410("Abgesicherter Start", 520)
            {
                Heading = "Abgesicherter Start",
                Body = "Designs, Effekte, Zusatzfenster und Extras sind aus – nur für diesen Start. Es wird nichts gespeichert: Beim nächsten normalen Start ist alles wieder wie vorher.\n\nKanäle, Apps, Szenen und Hotkeys laufen ganz normal.",
            };
            w.AddKey("normal", "NORMAL NEU STARTEN").AddKey("ok", "OK", true);
            w.Clicked = id => { if (id == "normal") BeginInvoke(new Action(RestartNormal410)); return true; };
            _safeWin410 = w;
            w.Show(this);
        }

        private void RestartNormal410()
        {
            if (_demo) { ShowOsd("Neustart", "Vorschau: kein Neustart", -1, Theme.Accent); return; }
            _importing = true;
            Program.RestartRequested = true;
            ExitApp();
        }

        private void RestartSafe410()
        {
            if (_demo) { ShowOsd("Abgesichert", "Vorschau: kein Neustart", -1, Theme.Accent); return; }
            RememberWindow();
            SaveNow();
            _importing = true;
            Program.RestartArgs = "--abgesichert";
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 275 Autostart-Verzoegerung

        private void SetAutostartDelay410(int i)
        {
            _settings.AutostartDelay = MixerSettings.AutostartDelayValues[Math.Max(0, Math.Min(MixerSettings.AutostartDelayValues.Length - 1, i))];
            if (!_demo && IsAutostart()) SetAutostart(true);   // Eintrag mit neuer Verzoegerung
            ScheduleSave();
        }

        // ================================================================ 271 Umzugs-Paket

        private void ExportPackage410()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Umzugs-Paket erstellen",
                Filter = "Mischpult-Umzugs-Paket (*" + Package410.Ext + ")|*" + Package410.Ext,
                FileName = "Mischpult-Umzug " + DateTime.Now.ToString("yyyy-MM-dd") + Package410.Ext,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                RememberWindow();
                string err = Package410.Export(_settings, dlg.FileName, out int files);
                if (err != null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das Paket ließ sich nicht erstellen: " + err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Umzugs-Paket erstellt – mit " + files + (files == 1 ? " Datei" : " Dateien") + " (Sounds, Bilder).\n\n" + dlg.FileName +
                    "\n\nAuf dem neuen PC: Mischpult starten und im Installieren-Fenster „Umzugs-Paket …“ wählen – oder die Datei aufs Pult ziehen.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportPackageDialog410()
        {
            using (var dlg = new OpenFileDialog { Title = "Umzugs-Paket laden", Filter = "Mischpult-Umzugs-Paket (*" + Package410.Ext + ")|*" + Package410.Ext + "|Alle Dateien|*.*" })
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) == DialogResult.OK) ImportPackage410(dlg.FileName);
        }

        private void ImportPackage410(string path)
        {
            var info = Package410.Read(path);
            if (info.Error != null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Dieses Paket passt nicht: " + info.Error, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Umzugs-Paket" + (info.Created > DateTime.MinValue ? " vom " + info.Created.ToString("dd.MM.yyyy") : "") + " übernehmen?\n\n" + info.Summary +
                    ".\n\nDie jetzigen Einstellungen werden vorher gesichert und dann ersetzt. Das Mischpult startet dafür neu.", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (_demo) { MessageBox.Show(this, "Im Demo-Modus wird nichts gespeichert.", "Mischpult"); return; }
            RememberWindow();
            SaveNow();
            string err = Package410.Import(path);
            if (err != null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das hat nicht geklappt: " + err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _importing = true;
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 274 Reparieren

        private void Repair410()
        {
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Mischpult reparieren?\n\nLeistung, Aussehen und Fensterpositionen gehen zurück auf Werkseinstellung. Kanäle, Apps, Szenen, Hotkeys, Sounds und Ton-Einstellungen bleiben.\n\n" +
                    "Vorher wird alles gesichert (OPTIONEN → Sichern → Sicherung zurückholen). Das Mischpult startet dafür neu.", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (_demo) { ShowOsd("Reparieren", "Vorschau: " + Update410.RepairFields.Length + " Werte", -1, Theme.Accent); return; }
            if (Program.SafeMode)
            {
                // abgesichert: die gespeicherten (echten) Einstellungen reparieren, nicht die abgesicherten im Speicher
                var real = MixerSettings.LoadFrom(MixerSettings.FilePath);
                if (real == null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Die gespeicherten Einstellungen ließen sich nicht lesen.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Update410.BackupSettings("vor-reparatur");
                Update410.Repair(real);
                real.SaveTo(MixerSettings.FilePath);
            }
            else
            {
                RememberWindow();
                _settings.Save();
                Update410.BackupSettings("vor-reparatur");
                Update410.Repair(_settings);
                _settings.Save();
            }
            _importing = true;
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 276 Uebersicht "Was ist eingeschaltet"

        /// <summary>Seiten, deren Schalter "Alle Extras aus" ausschaltet.</summary>
        internal static readonly string[] ExtraPages410 = { "Aussehen", "Leuchten & Anzeigen", "Fenster & Verbindungen", "Profi-Ton" };

        private object _defPages410;
        private Dictionary<SRow, object> _def410;

        /// <summary>Werte aller Schalter, Auswahlen und Regler bei Werkseinstellung (kurz mit Standard-Einstellungen gelesen, je Seiten-Satz einmal).</summary>
        private Dictionary<SRow, object> Defaults410(IEnumerable<SPage> pages)
        {
            if (ReferenceEquals(pages, _defPages410) && _def410 != null) return _def410;
            var map = new Dictionary<SRow, object>();
            _defPages410 = pages; _def410 = map;
            var keep = _settings;
            var d = MixerSettings.CreateDefault();
            d.Normalize();
            try
            {
                _settings = d;
                foreach (var p in pages)
                    foreach (var r in p.Rows)
                        try
                        {
                            if (r is SToggle t) map[r] = t.Get();
                            else if (r is SChoice c) map[r] = c.Get();
                            else if (r is SSlider sl) map[r] = sl.Get();
                        }
                        catch { }
            }
            finally { _settings = keep; }
            return map;
        }

        private static object Now410(SRow r)
        {
            try
            {
                if (r is SToggle t) return t.Get();
                if (r is SChoice c) return c.Get();
                if (r is SSlider s) return s.Get();
            }
            catch { }
            return null;
        }

        private static bool Changed410(SRow r, Dictionary<SRow, object> def)
        {
            if (!def.TryGetValue(r, out object d)) return false;
            object n = Now410(r);
            if (n == null) return false;
            if (n is float fn && d is float fd) return Math.Abs(fn - fd) > 0.0001f;
            return !Equals(n, d);
        }

        /// <summary>Zeilen fuer die Uebersicht: nur, was anders ist als bei der Werkseinstellung.</summary>
        private List<SRow> BuildOverview410(IReadOnlyList<SPage> pages)
        {
            var def = Defaults410(pages);
            var rows = new List<SRow>();
            var all = new List<SRow>();
            Func<int> count = () => all.Count(x => x.IsVisible);
            rows.Add(new SHeader { Label = "Was ist eingeschaltet", HintFunc = () => { int n = count(); return n == 0 ? "Alles wie beim ersten Start." : n + (n == 1 ? " Einstellung ist" : " Einstellungen sind") + " anders als beim ersten Start."; } });
            rows.Add(new SInfo { Label = "Nur, was du verändert hast", HintFunc = () => "Ein Klick auf einen Schalter stellt ihn zurück. Links eine Seite wählen, um zurückzukommen." });
            var off = new SButton { Label = "Alle Extras aus", HintFunc = () => "Schaltet alle eingeschalteten Extras auf den Seiten " + string.Join(", ", ExtraPages410) + " aus. Vorher wird gesichert.", Text = "Ausschalten …", Danger = true };
            off.Click = r => AllExtrasOff410();
            rows.Add(off);
            foreach (var p in pages)
            {
                var mine = new List<SRow>();
                string sectionLabel = null;
                foreach (var r in p.Rows)
                {
                    if (r is SSection) { sectionLabel = r.Label; continue; }
                    if (!def.ContainsKey(r)) continue;
                    var src = r;
                    SRow m;
                    if (r is SToggle t) m = new SToggle { Get = t.Get, Set = t.Set };
                    else if (r is SChoice c) m = new SChoice { Options = c.Options, OptionsFunc = c.OptionsFunc, Get = c.Get, Set = c.Set, Below = c.Below };
                    else if (r is SSlider s) m = new SSlider { Min = s.Min, Max = s.Max, Step = s.Step, Get = s.Get, Set = s.Set, Format = s.Format };
                    else continue;
                    m.Label = r.Label;
                    string sec = sectionLabel;
                    m.HintFunc = () => (sec != null ? sec + " · " : "") + src.Hint;
                    m.Enabled = r.Enabled;
                    m.PageTitle = p.Title;
                    m.Visible = () => Changed410(src, def);
                    mine.Add(m);
                }
                if (mine.Count == 0) continue;
                rows.Add(new SSection { Label = p.Title, PageTitle = p.Title, Visible = () => mine.Any(x => x.IsVisible) });
                rows.AddRange(mine);
                all.AddRange(mine);
            }
            rows.Add(new SInfo { Label = "Nichts verändert", HintFunc = () => "Alle Schalter stehen wie beim ersten Start.", Visible = () => count() == 0 });
            return rows;
        }

        private void ShowOverview410()
        {
            if (_settingsWin == null || _settingsWin.IsDisposed) ShowSettings("Allgemein");
            var w = _settingsWin;
            if (w == null) return;
            w.ShowCustom(BuildOverview410(w.Pages));
        }

        /// <summary>Wie viele Einstellungen anders sind als beim ersten Start (fuer die Zeile in den Einstellungen).</summary>
        private string OverviewCount410()
        {
            var pages = _settingsWin?.Pages;
            if (pages == null) return null;
            var def = Defaults410(pages);
            int n = def.Keys.Count(r => Changed410(r, def));
            return n == 0 ? "alles Standard" : n + " verändert";
        }

        private void AllExtrasOff410()
        {
            var pages = (IReadOnlyList<SPage>)_settingsWin?.Pages ?? BuildSettingsPages();
            var def = Defaults410(pages);
            var targets = pages.Where(p => ExtraPages410.Contains(p.Title)).SelectMany(p => p.Rows.OfType<SToggle>())
                .Where(t => def.TryGetValue(t, out object d) && d is bool db && !db && Now410(t) is bool on && on).ToList();
            if (Program.SafeMode) { ShowOsd("Abgesicherter Start", "Hier sind schon alle Extras aus", -1, Theme.SoloAmber); return; }
            if (targets.Count == 0) { ShowOsd("Extras", "Es ist kein Extra eingeschaltet", -1, Theme.Accent); return; }
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, targets.Count + (targets.Count == 1 ? " Extra" : " Extras") + " ausschalten?\n\n" +
                    string.Join("\n", targets.Take(12).Select(t => "• " + t.Label)) + (targets.Count > 12 ? "\n• … und " + (targets.Count - 12) + " weitere" : "") +
                    "\n\nVorher wird gesichert (OPTIONEN → Sichern → Sicherung zurückholen).", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!_demo) { RememberWindow(); _settings.Save(); Update410.BackupSettings("vor-extras-aus"); }
            int n = 0;
            foreach (var t in targets) { try { if (Now410(t) is bool b && b) { t.Set(false); n++; } } catch { } }
            ScheduleSave();
            ShowOsd("Extras aus", n + (n == 1 ? " Schalter" : " Schalter") + " ausgeschaltet", -1, Theme.LedGreen);
        }

        // ================================================================ Einstellungen

        /// <summary>Seite Allgemein: alles rund ums Installieren und Aktualisieren.</summary>
        private void AddUpdate410(SPage p)
        {
            IWin32Window owner() => _settingsWin ?? (IWin32Window)this;
            p.Section("4.1 Installieren & Aktualisieren");
            p.Toggle("Ein-Klick-Fenster beim Start", "Startest du eine heruntergeladene Mischpult.exe, fragt sie gleich: installieren – oder, ist schon eine ältere Version installiert, aktualisieren. Ein Klick genügt, die alte Version beendet sich von selbst.",
                () => !_settings.InstallPromptOff, v => { _settings.InstallPromptOff = !v; ScheduleSave(); });
            p.Toggle("Neuheiten im Update-Fenster", "Das Update-Fenster zeigt vorher, was in der neuen Version neu ist.",
                () => _settings.UpdateNewsOn, v => { _settings.UpdateNewsOn = v; ScheduleSave(); });
            p.Toggle("Update per Ziehen", "Neue Mischpult.exe einfach aufs Pult ziehen – das Update-Fenster erscheint.",
                () => _settings.DropUpdateOn, v => { _settings.DropUpdateOn = v; SetDrop410(); ScheduleSave(); });
            p.Toggle("Neue Version im Download-Ordner erkennen", "Landet eine neuere Mischpult.exe im Download-Ordner, erscheint unten rechts ein Hinweis mit „Jetzt aktualisieren“. Ganz ohne Internet.",
                () => _settings.DownloadWatchOn, v => { _settings.DownloadWatchOn = v; _settings.DownloadIgnore = null; _dlShown410 = 0; SetDownloadWatch410(v); ScheduleSave(); });
            p.Toggle("Online nach Updates suchen", () => "Schaut einmal am Tag beim GitHub-Projekt nach einer neuen Version und installiert sie auf Knopfdruck. Ohne diesen Schalter verbindet sich das Mischpult nie mit dem Internet." +
                    (string.IsNullOrEmpty(_settings.UpdateRepoNow) ? " Dafür das Projekt eintragen (darunter)." : ""),
                () => _settings.OnlineUpdateOn, v => { _settings.OnlineUpdateOn = v; if (v) { _onlineAt410 = _now410 + 3; _settings.UpdateLastCheck = 0; } ScheduleSave(); });
            p.Toggle("Updates automatisch installieren", "Findet das Mischpult beim Start eine neue Version, lädt und installiert es sie gleich selbst – ohne Klick, deine Einstellungen bleiben. Läuft gerade ein Spiel im Vordergrund, kommt stattdessen der Hinweis. Eine übersprungene Version bleibt übersprungen.",
                () => _settings.AutoUpdateOn, v => { _settings.AutoUpdateOn = v; ScheduleSave(); }).Visible = () => _settings.OnlineUpdateOn;   // 4.2.5
            p.Button("GitHub-Projekt", () => "Woher die Updates kommen: „Benutzer/Projekt“ (oder die Adresse von GitHub). Dort muss beim neuesten Release eine Mischpult.exe liegen.",
                () => "Ändern …", r => EditRepo410(), () => string.IsNullOrEmpty(_settings.UpdateRepoNow) ? "keins" : _settings.UpdateRepoNow).Visible = () => _settings.OnlineUpdateOn;
            p.Button("Jetzt nach Updates suchen", () => _onlineStatus410 ?? (_settings.UpdateLastCheck > 0 ? "Zuletzt nachgesehen: " + new DateTime(_settings.UpdateLastCheck, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm") + " Uhr." : "Noch nicht nachgesehen."),
                () => _onlineBusy410 ? "Sucht …" : "Suchen", r => CheckOnline410(true)).Visible = () => _settings.OnlineUpdateOn;
            p.Toggle("Sicherung vor jedem Update", "Vor einem Update werden deine Einstellungen gesichert und die alte Version aufgehoben. Macht die neue Probleme, holst du hier die vorige zurück.",
                () => _settings.UpdateBackupOn, v => { _settings.UpdateBackupOn = v; ScheduleSave(); });
            p.Button("Vorige Version wiederherstellen", () => { var pi = Update410.PrevInfo; return "Aufgehoben: " + (pi?.Text ?? "?") + ". Diese Version wird dafür aufgehoben – du kannst also auch wieder zurück."; },
                () => "Zurückholen …", r => RestorePrev410()).Visible = () => _settings.UpdateBackupOn && (_demo ? DemoOn("PREVVERSION") : Installer.RunningInstalled && Update410.HasPrev);
            p.Toggle("Start-Schutz", "Steigt das Mischpult zweimal hintereinander direkt beim Start aus, fragt es von selbst: abgesichert starten, vorige Version zurückholen oder normal versuchen.",
                () => _settings.StartGuardOn, v => { _settings.StartGuardOn = v; if (!v && !_demo) Update410.GuardEnd(); ScheduleSave(); });
            p.Toggle("Abgesicherter Start mit der Umschalt-Taste", "Umschalt gedrückt halten, während du das Mischpult startest: Es startet ohne Designs, Effekte und Zusatzfenster – nur für diesen Start, es wird nichts verändert.",
                () => _settings.SafeStartOn, v => { _settings.SafeStartOn = v; ScheduleSave(); });
            p.Button("Jetzt abgesichert neu starten", "Zum Ausprobieren – oder wenn gerade etwas spinnt.", "Neu starten", r => RestartSafe410())
                .Visible = () => _settings.SafeStartOn && !Program.SafeMode;
            p.Choice("Autostart-Verzögerung", () => _demo || IsAutostart() ? "So lange nach dem Windows-Start wartet das Mischpult – der PC fährt schneller hoch." : "Wirkt, wenn „Mit Windows starten“ an ist (oben).",
                MixerSettings.AutostartDelayNames, () => Math.Max(0, Array.IndexOf(MixerSettings.AutostartDelayValues, _settings.AutostartDelay)), i => SetAutostartDelay410(i));
            p.Toggle("Beim Deinstallieren nach den Einstellungen fragen", "Beim Deinstallieren fragt das Mischpult, ob auch deine Einstellungen gelöscht werden sollen. Aus = sie bleiben immer liegen (für später oder einen anderen PC).",
                () => _settings.UninstallAskOn, v => { _settings.UninstallAskOn = v; ScheduleSave(); });
            p.Section("4.1 Überblick & Reparieren");
            p.Toggle("Übersicht „Was ist eingeschaltet“", "Zeigt nur, was du gegenüber dem ersten Start verändert hast – mit Zurückstellen per Klick und „Alle Extras aus“.",
                () => _settings.OverviewOn, v => { _settings.OverviewOn = v; ScheduleSave(); });
            p.Button("Was ist eingeschaltet", "Alle veränderten Einstellungen auf einer Seite.", "Anzeigen", r => ShowOverview410(), () => OverviewCount410())
                .Visible = () => _settings.OverviewOn;
            p.Toggle("Mischpult reparieren", "Ein Knopf setzt Leistung, Aussehen und Fensterpositionen zurück, wenn etwas komisch aussieht oder ruckelt. Kanäle, Apps, Szenen, Hotkeys und Ton bleiben.",
                () => _settings.RepairOn, v => { _settings.RepairOn = v; ScheduleSave(); });
            var rep = p.Button("Jetzt reparieren", "Vorher wird alles gesichert. Das Mischpult startet dafür neu.", "Reparieren …", r => Repair410());
            rep.Visible = () => _settings.RepairOn;
            rep.Danger = true;
        }

        /// <summary>Seite Sichern: Umzugs-Paket.</summary>
        private void AddPackage410(SPage p)
        {
            p.Section("4.1 Umzugs-Paket");
            p.Toggle("Umzugs-Paket", "Alles in eine Datei: Einstellungen, Szenen, EQ-Presets, Soundboard-Sounds, Kanal-Bilder und Hintergrundbild. Auf dem neuen PC: im Installieren-Fenster „Umzugs-Paket …“ wählen, die Datei aufs Pult ziehen oder hier laden.",
                () => _settings.PackageOn, v => { _settings.PackageOn = v; SetDrop410(); ScheduleSave(); });
            p.Button("Paket erstellen", "Speichert das Paket (z. B. auf einen USB-Stick oder in die Cloud).", "Erstellen …", r => ExportPackage410()).Visible = () => _settings.PackageOn;
            p.Button("Paket laden", "Ersetzt alle Einstellungen (die jetzigen werden vorher gesichert) und startet neu.", "Laden …", r => ImportPackageDialog410()).Visible = () => _settings.PackageOn;
        }

        // ================================================================ Vorschau

        private void DemoShown410()
        {
            string iw = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_INSTALLWIN");   // "", "alt", "gleich", "neu"
            if (iw != null) DemoLater(() =>
            {
                InstallFlow410.DemoInstalled = iw == "neu-installieren" ? "" : iw;
                var w = InstallFlow410.Build(_settings, false, out _);
                w.Show(this);
                w.Location = DemoXY("INSTALLXY", new Point(560, 160));
            });
            if (DemoOn("GUARDWIN")) DemoLater(() => { var w = InstallFlow410.BuildGuard(2, true); w.Show(this); w.Location = DemoXY("GUARDXY", new Point(560, 200)); });
            if (DemoOn("SAFEWIN")) DemoLater(() => { ShowSafeNotice410(); _safeWin410.Location = DemoXY("SAFEXY", new Point(560, 200)); });
            if (DemoOn("ONLINENOTICE")) DemoLater(() => CheckOnline410(false));
            if (DemoOn("OVERVIEW")) DemoLater(() =>
            {
                ShowOverview410();
                if (_settingsWin != null) _settingsWin.Location = DemoXY("SETXY", new Point(300, 60));
            });
            string fm = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FIELDMAP");
            if (!string.IsNullOrEmpty(fm)) DemoLater(() => FieldMap410(fm));
        }

        /// <summary>
        /// Nur Entwicklung (Vorschau): welche Einstellungen die Schalter auf "Aussehen", "Leuchten &amp; Anzeigen" und bei der Leistung
        /// veraendern – daraus entsteht die Liste fuers Reparieren.
        /// </summary>
        private void FieldMap410(string file)
        {
            var pages = BuildSettingsPages();
            var fields = typeof(MixerSettings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(f => f.GetCustomAttributes(typeof(System.Runtime.Serialization.DataMemberAttribute), false).Length > 0).ToList();
            Func<string[]> snap = () => fields.Select(f => Json410(f.GetValue(_settings))).ToArray();
            var hit = new SortedSet<string>(StringComparer.Ordinal);
            var log = new List<string>();
            foreach (var p in pages)
            {
                bool look = p.Title == "Aussehen" || p.Title == "Leuchten & Anzeigen";
                bool inPerf = false;
                foreach (var r in p.Rows)
                {
                    if (r is SSection) { inPerf = p.Title == "Allgemein" && (r.Label == "Leistung" || r.Label == "Windows"); continue; }
                    if (!look && !inPerf) continue;
                    if (p.Title == "Allgemein" && r.Label != null && (r.Label.StartsWith("Windows-Lautst") || r.Label.StartsWith("App-Ger"))) continue;
                    var before = snap();
                    try
                    {
                        File.AppendAllText(file + ".log", p.Title + " | " + r.Label + "\n");
                        if (r is SToggle t) { bool v = t.Get(); t.Set(!v); Collect410(fields, before, snap(), hit, p.Title, r.Label, log); t.Set(v); }
                        else if (r is SChoice c)
                        {
                            int v = c.Get();
                            var opts = c.OptionsFunc != null ? c.OptionsFunc() : c.Options;
                            for (int i = 0; i < Math.Min(opts.Length, 16); i++) { if (i == v) continue; c.Set(i); Collect410(fields, before, snap(), hit, p.Title, r.Label, log); }
                            c.Set(v);
                        }
                        else if (r is SSlider s) { float v = s.Get(); s.Set(v == s.Max ? s.Min : s.Max, true); Collect410(fields, before, snap(), hit, p.Title, r.Label, log); s.Set(v, true); }
                    }
                    catch (Exception ex) { log.Add("FEHLER " + p.Title + " | " + r.Label + ": " + ex.Message); }
                }
            }
            File.WriteAllLines(file, hit);
            File.WriteAllLines(file + ".detail", log);
            ExitApp();
        }

        private static void Collect410(List<System.Reflection.FieldInfo> fields, string[] a, string[] b, SortedSet<string> hit, string page, string label, List<string> log)
        {
            var ch = new List<string>();
            for (int i = 0; i < fields.Count; i++) if (a[i] != b[i]) { hit.Add(fields[i].Name); ch.Add(fields[i].Name); }
            log.Add(page + " | " + label + " → " + string.Join(", ", ch));
        }

        private static string Json410(object v)
        {
            if (v == null) return "null";
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return v.ToString();
            try
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(v.GetType());
                using (var ms = new MemoryStream()) { ser.WriteObject(ms, v); return System.Text.Encoding.UTF8.GetString(ms.ToArray()); }
            }
            catch { return v.ToString(); }
        }
    }
}
