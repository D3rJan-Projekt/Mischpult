using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Fundament: alles im Ordner Dokumente, Einstellungen in der Cloud, Benutzer-Profile, "Was ist neu",
    /// Installieren/Deinstallieren und einfache Einstellungen.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyStatics400a(MixerSettings s)
        {
            if (MixerSettings.Profile != null) Text += " – Profil " + MixerSettings.Profile;
        }

        private void HookFundament400()
        {
            // 189 Was ist neu
            if (_demo)
            {
                if (DemoOn("WHATSNEW")) DemoLater(ShowWhatsNew);
            }
            else if (_settings.LastVersion != Program.Version)
            {
                bool show = !MixerSettings.WasFresh && !_settings.WhatsNewOff && !_startHidden;
                _settings.LastVersion = Program.Version;
                ScheduleSave();
                if (show)
                {
                    var t = new Timer { Interval = 2500 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { ShowWhatsNew(); } catch { } };
                    t.Start();
                }
            }
            if (!_demo && MixerSettings.CloudFolder != null && !MixerSettings.CloudActive)
                _cloudNote = "Cloud-Ordner nicht erreichbar – Einstellungen liegen lokal";
            NoteSaved400();
        }

        // ================================================================ 189 Was ist neu

        private WhatsNewWindow _whatsNew;

        public void ShowWhatsNew()
        {
            if (_whatsNew != null && !_whatsNew.IsDisposed) { _whatsNew.Activate(); return; }
            _whatsNew = new WhatsNewWindow { ToggleFor = NewsResolver420() };   // 4.2: Schalter direkt im Fenster
            _whatsNew.OpenSettings += () => BeginInvoke(new Action(() => ShowSettings()));
            _whatsNew.FormClosed += (s, e) => _whatsNew = null;
            if (Visible) _whatsNew.Show(this); else _whatsNew.Show();
        }

        // ================================================================ 183 Dokumente-Ordner

        private void SetDocsMode400(bool on)
        {
            if (on == MixerSettings.DocsMode || MixerSettings.Portable || _demo) return;
            string docs = Path.Combine(MixerSettings.DocsRoot, "Einstellungen");
            try
            {
                _settings.Save();
                if (on)
                {
                    CopyFolder346(MixerSettings.Folder, docs);
                    string oldBackups = Path.Combine(MixerSettings.AppDataFolder, "Sicherungen");
                    if (Directory.Exists(oldBackups)) MixerSettings.CopyTree(oldBackups, Path.Combine(MixerSettings.DocsRoot, "Sicherungen"));
                    if (Directory.Exists(Path.Combine(docs, "Sicherungen"))) { try { Directory.Delete(Path.Combine(docs, "Sicherungen"), true); } catch { } }
                    if (File.Exists(MixerSettings.LocationFile)) File.Delete(MixerSettings.LocationFile);
                }
                else
                {
                    Directory.CreateDirectory(MixerSettings.AppDataFolder);
                    CopyFolder346(MixerSettings.Folder, MixerSettings.AppDataFolder);
                    string docBackups = Path.Combine(MixerSettings.DocsRoot, "Sicherungen");
                    if (Directory.Exists(docBackups)) MixerSettings.CopyTree(docBackups, Path.Combine(MixerSettings.AppDataFolder, "Sicherungen"));
                    File.WriteAllText(MixerSettings.LocationFile, "appdata\r\nDiese Datei sorgt dafür, dass das Mischpult alles hier unter %AppData%\\Mischpult speichert statt in Dokumente\\Mischpult.\r\nLöschen = wieder Dokumente.\r\n");
                    if (_settings.RecordFolder != null && _settings.RecordFolder.StartsWith(MixerSettings.DocsRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        _settings.RecordFolder = MixerSettings.OldRecordFolder;
                        _settings.DocsRecMoved = false;
                        _settings.SaveTo(Path.Combine(MixerSettings.AppDataFolder, Path.GetFileName(MixerSettings.FilePath)));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das hat nicht geklappt: " + ex.Message, "Speicherort", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _importing = true;   // nichts mehr in den alten Ordner schreiben
            Program.RestartRequested = true;
            ExitApp();
        }

        private static void OpenFolder400(string path)
        {
            try { Directory.CreateDirectory(path); Process.Start("explorer.exe", path); } catch { }
        }

        // ================================================================ 181 Cloud-Ordner

        private string _cloudNote;
        private DateTime _cloudLastWrite;
        private long _cloudLastLen = -1;
        private bool _cloudAsking, _cloudBlocked;

        /// <summary>Nach dem eigenen Speichern: Stand merken (damit fremde Aenderungen auffallen).</summary>
        private void NoteSaved400()
        {
            if (!MixerSettings.CloudActive) return;
            try
            {
                var fi = new FileInfo(MixerSettings.FilePath);
                if (fi.Exists) { _cloudLastWrite = fi.LastWriteTimeUtc; _cloudLastLen = fi.Length; }
            }
            catch { }
        }

        /// <summary>Darf jetzt gespeichert werden? (nicht, solange eine fremde Aenderung auf Antwort wartet)</summary>
        private bool CanSave400 => !_cloudBlocked;

        private void TickCloud400()
        {
            if (_demo || !MixerSettings.CloudActive || _cloudAsking || _importing || _cloudLastLen < 0) return;
            FileInfo fi;
            try { fi = new FileInfo(MixerSettings.FilePath); if (!fi.Exists) return; } catch { return; }
            if (fi.LastWriteTimeUtc == _cloudLastWrite && fi.Length == _cloudLastLen) return;
            if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds < 3) return;   // wird vielleicht noch geschrieben
            _cloudAsking = true;
            _cloudBlocked = true;
            BeginInvoke(new Action(() =>
            {
                try
                {
                    var r = MessageBox.Show(this, "Die Einstellungen im Cloud-Ordner wurden gerade woanders geändert (z. B. auf deinem anderen PC).\n\nJetzt neu laden? Das Mischpult startet dafür kurz neu.\n\nNein = deine Einstellungen hier behalten (sie überschreiben dann die anderen).",
                        "Mischpult – Cloud", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes)
                    {
                        _importing = true;
                        Program.RestartRequested = true;
                        ExitApp();
                        return;
                    }
                    _cloudBlocked = false;
                    _settings.Save();
                    NoteSaved400();
                }
                finally { _cloudAsking = false; }
            }));
        }

        private void SetCloud400(bool on, Rectangle at)
        {
            if (_demo) return;
            try
            {
                if (!on)
                {
                    if (MixerSettings.CloudFolder == null) return;
                    _settings.Save();
                    if (MixerSettings.CloudActive) CopyFolder346(MixerSettings.Folder, MixerSettings.BaseFolder);
                    File.Delete(MixerSettings.CloudFile);
                }
                else
                {
                    string suggest = Environment.GetEnvironmentVariable("OneDrive");
                    using (var dlg = new FolderBrowserDialog { Description = "Ordner für die Einstellungen wählen (z. B. in OneDrive oder Dropbox). Am besten einen eigenen Ordner „Mischpult“.", ShowNewFolderButton = true })
                    {
                        if (!string.IsNullOrEmpty(suggest) && Directory.Exists(suggest)) dlg.SelectedPath = suggest;
                        if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath)) return;
                        string target = dlg.SelectedPath;
                        if (string.Equals(Path.GetFileName(target.TrimEnd('\\', '/')), "OneDrive", StringComparison.OrdinalIgnoreCase) || target.TrimEnd('\\', '/').Length <= 3)
                            target = Path.Combine(target, "Mischpult");
                        Directory.CreateDirectory(target);
                        _settings.Save();
                        bool has = File.Exists(Path.Combine(target, "settings.json"));
                        bool takeTheirs = false;
                        if (has)
                        {
                            var r = MessageBox.Show(_settingsWin ?? (IWin32Window)this, "In diesem Ordner liegen schon Einstellungen (vielleicht von deinem anderen PC).\n\nJa = diese übernehmen (deine jetzigen kommen in die Sicherungen)\nNein = mit deinen jetzigen überschreiben",
                                "Mischpult – Cloud", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                            if (r == DialogResult.Cancel) return;
                            takeTheirs = r == DialogResult.Yes;
                        }
                        if (takeTheirs)
                            try { _settings.SaveTo(Path.Combine(BackupFolder, "einstellungen-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + "-vor-cloud.json")); } catch { }
                        else
                            foreach (var f in Directory.GetFiles(MixerSettings.Folder))
                            {
                                string n = Path.GetFileName(f);
                                if (n.Equals(MixerSettings.CloudFileName, StringComparison.OrdinalIgnoreCase) || n.Equals(MixerSettings.LocationFileName, StringComparison.OrdinalIgnoreCase)) continue;
                                if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || n.Equals(MixerSettings.ProfileFileName, StringComparison.OrdinalIgnoreCase)) File.Copy(f, Path.Combine(target, n), true);
                            }
                        if (!takeTheirs && Directory.Exists(MixerSettings.ProfileFolder)) MixerSettings.CopyTree(MixerSettings.ProfileFolder, Path.Combine(target, "Profile"));
                        Directory.CreateDirectory(MixerSettings.BaseFolder);
                        File.WriteAllText(MixerSettings.CloudFile, target + "\r\n");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Das hat nicht geklappt: " + ex.Message, "Cloud-Ordner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _importing = true;
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 168 Benutzer-Profile

        private void AddProfileMenu400(List<ToolStripItem> items)
        {
            if (!MixerSettings.ProfilesOn) return;
            var sub = Sub("Profil: " + MixerSettings.ProfileDisplay(MixerSettings.Profile));
            FillProfileMenu400(sub.DropDownItems);
            items.Add(sub);
        }

        private void FillProfileMenu400(ToolStripItemCollection items)
        {
            string cur = MixerSettings.Profile;
            var names = new List<string> { null };
            names.AddRange(MixerSettings.ListProfiles());
            foreach (var n in names)
            {
                string name = n;
                items.Add(new ToolStripMenuItem(MixerSettings.ProfileDisplay(n), null, (s, e) => { if (name != MixerSettings.Profile) BeginInvoke(new Action(() => SwitchProfile400(name))); })
                { Checked = n == cur });
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Neues Profil (Kopie von jetzt) …", null, (s, e) => BeginInvoke(new Action(() => NewProfile400(true)))));
            items.Add(new ToolStripMenuItem("Neues Profil (leer) …", null, (s, e) => BeginInvoke(new Action(() => NewProfile400(false)))));
            items.Add(new ToolStripMenuItem("Profil umbenennen …", null, (s, e) => BeginInvoke(new Action(RenameProfile400))) { Enabled = cur != null });
            items.Add(new ToolStripMenuItem("Profil löschen …", null, (s, e) => BeginInvoke(new Action(DeleteProfile400))) { Enabled = cur != null });
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Beim Start fragen", null, (s, e) => MixerSettings.WriteProfilePointer(true, MixerSettings.Profile, !MixerSettings.ProfileAsk))
            { Checked = MixerSettings.ProfileAsk });
            items.Add(Hint("Wechseln startet das Mischpult kurz neu."));
        }

        private void SetProfilesOn400(bool on)
        {
            if (_demo) return;
            if (on) { MixerSettings.WriteProfilePointer(true, MixerSettings.Profile, false); return; }
            string cur = MixerSettings.Profile;
            if (cur != null && MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Profile ausschalten? Das Mischpult wechselt dafür zurück auf das Profil „Standard“ und startet neu.\n\nDie anderen Profile bleiben gespeichert.",
                    "Profile", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SaveNow();
            MixerSettings.WriteProfilePointer(false, null, false);
            if (cur != null)
            {
                _importing = true;
                MixerSettings.ProfileArg = null;
                Program.RestartRequested = true;
                ExitApp();
            }
        }

        private void SwitchProfile400(string name)
        {
            if (_demo) return;
            SaveNow();
            if (!MixerSettings.WriteProfilePointer(true, name, MixerSettings.ProfileAsk)) return;
            _importing = true;   // die jetzigen Einstellungen sind gespeichert – nicht ins neue Profil schreiben
            MixerSettings.ProfileArg = null;
            Program.RestartRequested = true;
            ExitApp();
        }

        private void NewProfile400(bool copy)
        {
            if (_demo) return;
            string name;
            using (var dlg = new InputDialog("Neues Profil", copy ? "Name (das Profil startet mit deinem jetzigen Pult):" : "Name (das Profil startet mit einem frischen Pult):", "", 24))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                name = MixerSettings.CleanProfileName(dlg.Value);
            }
            if (name == null) { MessageBox.Show(this, "Bitte einen Namen eingeben (nicht „Standard“).", "Profile"); return; }
            if (MixerSettings.ListProfiles().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "Das Profil „" + name + "“ gibt es schon.", "Profile"); return; }
            MixerSettings s;
            if (copy) { SaveNow(); s = _settings; }
            else { s = MixerSettings.CreateDefault(); s.Normalize(); s.WizardDone = true; s.LastVersion = Program.Version; }
            Directory.CreateDirectory(MixerSettings.ProfileFolder);
            if (!s.SaveTo(MixerSettings.ProfileFile(name))) { MessageBox.Show(this, "Konnte das Profil nicht speichern.", "Profile"); return; }
            if (!MixerSettings.ProfilesOn) MixerSettings.WriteProfilePointer(true, MixerSettings.Profile, false);
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Profil „" + name + "“ ist angelegt. Jetzt dorthin wechseln?", "Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SwitchProfile400(name);
        }

        private void RenameProfile400()
        {
            string cur = MixerSettings.Profile;
            if (cur == null || _demo) return;
            string name;
            using (var dlg = new InputDialog("Profil umbenennen", "Neuer Name:", cur, 24))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                name = MixerSettings.CleanProfileName(dlg.Value);
            }
            if (name == null || name == cur) return;
            if (MixerSettings.ListProfiles().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "Das Profil „" + name + "“ gibt es schon.", "Profile"); return; }
            try
            {
                SaveNow();
                File.Move(MixerSettings.ProfileFile(cur), MixerSettings.ProfileFile(name));
                MixerSettings.WriteProfilePointer(true, name, MixerSettings.ProfileAsk);
                MixerSettings.ProfileArg = null;
                Text = Text.Replace(" – Profil " + cur, " – Profil " + name);
            }
            catch (Exception ex) { MessageBox.Show(this, "Das hat nicht geklappt: " + ex.Message, "Profile"); }
        }

        private void DeleteProfile400()
        {
            string cur = MixerSettings.Profile;
            if (cur == null || _demo) return;
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Profil „" + cur + "“ löschen? Das Mischpult wechselt danach auf „Standard“ und startet neu.\n\n(Eine Kopie kommt in die Sicherungen.)",
                    "Profile", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                SaveNow();
                try { File.Copy(MixerSettings.ProfileFile(cur), Path.Combine(BackupFolder, "profil-" + cur + "-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".json"), true); } catch { }
                File.Delete(MixerSettings.ProfileFile(cur));
            }
            catch (Exception ex) { MessageBox.Show(this, "Das hat nicht geklappt: " + ex.Message, "Profile"); return; }
            MixerSettings.WriteProfilePointer(true, null, MixerSettings.ProfileAsk);
            _importing = true;
            MixerSettings.ProfileArg = null;
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ 188 Installieren

        private void ShowInstallMenu400(Rectangle at)
        {
            var m = NewMenu();
            m.ShowImageMargin = false;
            m.Items.Add(new ToolStripMenuItem("Installieren – mit Desktop-Verknüpfung", null, (s, e) => BeginInvoke(new Action(() => Install400(true)))));
            m.Items.Add(new ToolStripMenuItem("Installieren – nur Startmenü", null, (s, e) => BeginInvoke(new Action(() => Install400(false)))));
            m.Items.Add(Hint("Nach " + Installer.InstallDir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%UserProfile%") + " – keine Admin-Rechte nötig."));
            ShowMenuAt(m, at);
        }

        private void Install400(bool desktop)
        {
            string err = Installer.Install(desktop);
            if (err != null) { MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Installieren hat nicht geklappt: " + err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (Installer.RunningInstalled)
            {
                MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Fertig: Verknüpfungen und Windows-Eintrag sind erneuert.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Mischpult ist installiert – im Startmenü und unter Windows-Einstellungen → Apps.\n\nJetzt die installierte Version starten? (Diese hier wird beendet; die heruntergeladene Datei kannst du danach löschen.)",
                    "Mischpult", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Program.RestartPath = Installer.InstallExe;
                Program.RestartRequested = true;
                ExitApp();
            }
        }

        private void Uninstall400()
        {
            bool wipe = false;
            if (_settings.UninstallAskOn && !_demo)   // 4.1: Einstellungen auf Wunsch mit loeschen
            {
                var r = MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Mischpult deinstallieren? Startmenü, Desktop-Verknüpfung und Windows-Eintrag werden entfernt, das Mischpult beendet sich.\n\nSollen auch deine Einstellungen gelöscht werden?\n\n" + Installer.WipeQuestion,
                    "Mischpult", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.Cancel) return;
                wipe = r == DialogResult.Yes;
            }
            else if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Mischpult deinstallieren? Startmenü, Desktop-Verknüpfung und Windows-Eintrag werden entfernt, das Mischpult beendet sich.\n\nDeine Einstellungen bleiben erhalten.",
                    "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            string err = Installer.Uninstall();
            if (err != null) { MessageBox.Show(this, "Das hat nicht geklappt: " + err, "Mischpult"); return; }
            if (wipe) { _importing = true; Program.WipeOnExit = true; }   // nach dem Beenden – nichts mehr speichern
            if (Installer.RunningInstalled) Installer.ScheduleFolderDelete();
            else { try { if (Directory.Exists(Installer.InstallDir)) Directory.Delete(Installer.InstallDir, true); } catch { } }
            ExitApp();
        }

        // ================================================================ 187 Einfache Einstellungen

        /// <summary>Was in den einfachen Einstellungen bleibt (Name der Zeile).</summary>
        internal static readonly HashSet<string> SimpleLabels = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            // Allgemein
            "Immer im Vordergrund", "Mit Windows starten", "Beim Schließen im Infobereich weiterlaufen", "Mini-Ansicht", "Windows-Lautstärkemixer",
            "Einfache Einstellungen", "Über Mischpult", "Mischpult beenden", "Mischpult installieren", "Was ist neu in 4.2", "Einrichtungs-Assistent", "Ein-Klick-Fenster beim Start",
            // Aussehen
            "Design", "Farbschema", "Akzentfarbe", "Größe (Zoom)", "Uhr", "Art der Uhr", "Meterbridge (große Pegel oben)", "Kompakt-Ansicht", "Saison-Deko",
            "Design folgt Windows (hell/dunkel)", "Anzeigefeld der Kanäle", "Bewegter Hintergrund", "Eigenes Hintergrundbild", "Pegel-Farben",
            "Spektrum in der Meterbridge",
            // Leuchten & Anzeigen
            "Lautheit (LUFS) im MASTER", "VU-Meter mit Zeiger am Ausgang", "Tasten klicken", "Motorfader",
            // Ton
            "Nachtmodus", "Loudness", "Gehörschutz", "Sleep-Timer", "Sanft stummschalten", "Leise-Stunden", "Abendkurve", "Wecker", "Ausgang einstellen",
            "Hör-Tagebuch", "Hörtest",
            // Kanaele & Ausgaenge
            "Mehrere Ausgänge", "Stream-Mix", "Neuer Kanal", "Neue Gruppe", "Alle Kanäle auf Werkseinstellung",
            // Automatik
            "Spiele automatisch erkennen", "Ducking", "Ducking einstellen",
            // Mikrofon & Discord (die Seite soll nicht leer sein)
            "Push-to-Talk / Push-to-Mute", "Soundboard",
            // Steuerung
            "Hotkeys", "MIDI-Controller",
            // Sichern
            "Einstellungen exportieren", "Einstellungen importieren", "Einstellungsordner", "Täglich automatisch sichern", "Alles im Ordner Dokumente", "Benutzer-Profile",
        };

        /// <summary>Zeilen in den einfachen Einstellungen ausblenden (Abschnitte ohne sichtbare Zeile ebenfalls).</summary>
        private void ApplySimple400(List<SPage> pages)
        {
            string dump = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DUMPLABELS") : null;   // nur Vorschau: alle Zeilen auflisten
            if (!string.IsNullOrEmpty(dump))
                try { File.WriteAllLines(dump, pages.SelectMany(pg => pg.Rows.Select(r => pg.Title + "|" + r.GetType().Name + "|" + r.Label + "|" + (SimpleLabels.Contains(r.Label ?? "") ? "S" : "")))); } catch { }
            foreach (var p in pages)
            {
                SSection section = null;
                var sectionRows = new Dictionary<SSection, List<SRow>>();
                foreach (var r in p.Rows)
                {
                    if (r is SHeader) continue;
                    if (r is SSection sec) { section = sec; sectionRows[sec] = new List<SRow>(); continue; }
                    var old = r.Visible;
                    var row = r;
                    bool simple = SimpleLabels.Contains(r.Label ?? "");
                    r.Visible = () => (!_settings.SimpleSettings || simple) && (old == null || old());
                    if (section != null) sectionRows[section].Add(row);
                }
                foreach (var kv in sectionRows)
                {
                    var rows = kv.Value;
                    var oldS = kv.Key.Visible;
                    kv.Key.Visible = () => (oldS == null || oldS()) && (!_settings.SimpleSettings || rows.Any(x => x.IsVisible));
                }
            }
        }

        private void SetSimple400(bool v)
        {
            _settings.SimpleSettings = v;
            ScheduleSave();
            _settingsWin?.Invalidate(true);
        }

        // ================================================================ Einstellungen

        /// <summary>Seite Allgemein: einfache Einstellungen, Installieren, Was ist neu.</summary>
        private void AddGeneral400(SPage p)
        {
            p.Section("4.0 Mischpult");
            p.Toggle("Einfache Einstellungen", "Zeigt hier nur die wichtigsten Einstellungen. Alles andere bleibt, wie es ist – nur ausgeblendet. Umschalten geht auch links unten.",
                () => _settings.SimpleSettings, v => SetSimple400(v));
            p.Button("Mischpult installieren", () => Installer.IsInstalled
                    ? "Installiert unter " + Installer.InstallDir + (Installer.RunningInstalled ? " (läuft gerade)." : ". Diese EXE hier ist eine andere Datei – „Aktualisieren“ kopiert sie dorthin.")
                    : "Kopiert die EXE in deinen Programme-Ordner, legt eine Verknüpfung ins Startmenü (und auf Wunsch auf den Desktop) und trägt das Mischpult unter Windows-Einstellungen → Apps ein – dort lässt es sich auch wieder entfernen. Keine Admin-Rechte nötig.",
                () => Installer.IsInstalled ? (Installer.RunningInstalled ? "Erneuern …" : "Aktualisieren …") : "Installieren …", r => ShowInstallMenu400(r),
                () => Installer.IsInstalled ? "installiert" + (Installer.InstalledVersion != null ? " (" + Installer.InstalledVersion + ")" : "") : "nicht installiert")
                .Enabled = () => Win32.IsWindows && !_demo;
            var un = p.Button("Mischpult deinstallieren", "Entfernt Startmenü, Desktop-Verknüpfung und den Windows-Eintrag und löscht den Programm-Ordner. Deine Einstellungen bleiben erhalten.",
                "Deinstallieren …", r => BeginInvoke(new Action(Uninstall400)));
            un.Visible = () => Installer.IsInstalled;
            un.Danger = true;
            p.Toggle("Nach einem Update zeigen, was neu ist", "Startet eine neue Version zum ersten Mal, erscheint einmal ein Fenster mit allen Neuheiten.",
                () => !_settings.WhatsNewOff, v => { _settings.WhatsNewOff = !v; ScheduleSave(); });
            p.Button("Was ist neu in 4.2", "Die " + WhatsNewWindow.Count42 + " Neuheiten von Version 4.2 – darunter die " + WhatsNewWindow.Count41 + " aus 4.1 und alle " + WhatsNewWindow.Count + " aus 4.0.", "Anzeigen", r => ShowWhatsNew());
        }

        /// <summary>Seite Sichern: Dokumente-Ordner, Cloud, Profile.</summary>
        private void AddStorage400(SPage p)
        {
            p.Section("4.0 Speicherort");
            p.Toggle("Alles im Ordner Dokumente", () => MixerSettings.Portable ? "Geht nicht im portablen Modus (dort liegt alles neben der EXE)."
                    : MixerSettings.DocsMode ? "An: Einstellungen, Sicherungen, Aufnahmen, Bilder und Protokolle liegen in " + MixerSettings.DocsRoot + " – leicht zu finden und zu sichern. Aus = wie bis 3.5 unter %AppData% (startet neu)."
                    : "Speichert alles im Ordner Dokumente\\Mischpult (Einstellungen, Sicherungen, Aufnahmen, Bilder, Protokolle) statt versteckt unter %AppData%. Deine Einstellungen ziehen mit um, das Mischpult startet neu.",
                () => MixerSettings.DocsMode, v => BeginInvoke(new Action(() => SetDocsMode400(v))))
                .Enabled = () => !_demo && !MixerSettings.Portable;
            p.Button("Dokumente-Ordner", MixerSettings.DocsRoot, "Öffnen", r => OpenFolder400(MixerSettings.DocsRoot)).Visible = () => MixerSettings.DocsMode;
            p.Toggle("Einstellungen in der Cloud", () => MixerSettings.CloudFolder == null
                    ? "Speichert die Einstellungen in einem Ordner von OneDrive, Dropbox, Google Drive & Co. – so hast du auf jedem PC dasselbe Pult. Ändert ein anderer PC etwas, fragt das Mischpult, ob es neu laden soll. (Startet neu.)"
                    : (MixerSettings.CloudActive ? "An: " : "Eingestellt, aber gerade nicht erreichbar: ") + MixerSettings.CloudFolder + ". Aus = Einstellungen wieder nur auf diesem PC (der neueste Stand wird mitgenommen).",
                () => MixerSettings.CloudFolder != null, v => BeginInvoke(new Action(() => SetCloud400(v, Rectangle.Empty))))
                .Enabled = () => !_demo;
            p.Section("4.0 Benutzer-Profile");
            p.Toggle("Benutzer-Profile", () => "Mehrere Profile, jedes mit eigenem Pult (Kanäle, Design, Szenen …) – z. B. für jeden in der Familie oder „Arbeit“ und „Zocken“. Wechseln über das Schnellmenü (Rechtsklick auf OPTIONEN). Tipp: Eine Verknüpfung mit „--profil Name“ startet gleich ein bestimmtes Profil.",
                () => MixerSettings.ProfilesOn, v => SetProfilesOn400(v)).Enabled = () => !_demo;
            p.Button("Profile", "Wechseln, neu anlegen, umbenennen, löschen – und ob beim Start gefragt wird.", "Verwalten …", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                FillProfileMenu400(m.Items);
                ShowMenuAt(m, r);
            }, () => MixerSettings.ProfileDisplay(MixerSettings.Profile) + (MixerSettings.ProfileAsk ? " · fragt beim Start" : ""))
                .Enabled = () => !_demo && MixerSettings.ProfilesOn;
        }
    }
}
