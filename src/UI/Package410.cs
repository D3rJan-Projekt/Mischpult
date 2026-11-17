using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.1: Umzugs-Paket – Einstellungen samt Soundboard-Sounds, Kanal-Bildern und Hintergrundbild in einer Datei,
    /// damit auf einem neuen PC alles wieder da ist.
    /// </summary>
    internal static class Package410
    {
        public const string Ext = ".mischpult";
        private const string SettingsEntry = "einstellungen.json";
        private const string InfoEntry = "paket.txt";
        private const string FilePrefix = "dateien/";
        private const string Marker = "paket:";
        private const long MaxFile = 100L * 1024 * 1024;

        public sealed class Info
        {
            public MixerSettings Settings;
            public int Files;
            public string Version;
            public DateTime Created;
            public string Error;
            public string Summary => Settings == null ? "" :
                Settings.Channels.Count + " Kanäle, " + Settings.Scenes.Count + " Szenen, " + Settings.Sounds.Count + " Sounds, " + Files + (Files == 1 ? " Datei" : " Dateien");
        }

        /// <summary>Alle Dateien, auf die die Einstellungen zeigen (Sounds, Kanal-Bilder, Hintergrund).</summary>
        private static IEnumerable<Func<MixerSettings, string>> Getters(MixerSettings s)
        {
            if (s.Channels != null) for (int i = 0; i < s.Channels.Count; i++) { int k = i; yield return x => x.Channels[k].ImagePath; }
            if (s.Sounds != null) for (int i = 0; i < s.Sounds.Count; i++) { int k = i; yield return x => x.Sounds[k].File; }
            yield return x => x.BgImagePath;
        }

        private static void SetAt(MixerSettings s, int index, string value)
        {
            int nc = s.Channels?.Count ?? 0, ns = s.Sounds?.Count ?? 0;
            if (index < nc) s.Channels[index].ImagePath = value;
            else if (index < nc + ns) s.Sounds[index - nc].File = value;
            else s.BgImagePath = value;
        }

        private static string SafeName(string n)
        {
            n = Path.GetFileName(n ?? "datei");
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            if (n.Length > 80) n = n.Substring(n.Length - 80);
            return n.Length == 0 ? "datei" : n;
        }

        private static MixerSettings Copy(MixerSettings s)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "mischpult-paket-" + Guid.NewGuid().ToString("N") + ".json");
            try { return s.SaveTo(tmp) ? MixerSettings.LoadFrom(tmp) : null; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>Paket schreiben. Liefert null bei Erfolg (files = mitgenommene Dateien), sonst den Fehler.</summary>
        public static string Export(MixerSettings live, string zipPath, out int files)
        {
            files = 0;
            try
            {
                var s = Copy(live);
                if (s == null) return "Die Einstellungen ließen sich nicht lesen.";
                var getters = Getters(s).ToList();
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // Pfad -> Eintrag
                string tmpZip = zipPath + ".tmp";
                if (File.Exists(tmpZip)) File.Delete(tmpZip);
                using (var fs = File.Create(tmpZip))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    for (int i = 0; i < getters.Count; i++)
                    {
                        string p = getters[i](s);
                        if (string.IsNullOrEmpty(p)) continue;
                        if (!map.TryGetValue(p, out string entry))
                        {
                            entry = null;
                            try
                            {
                                var fi = new FileInfo(p);
                                if (fi.Exists && fi.Length <= MaxFile)
                                {
                                    entry = FilePrefix + (map.Count + 1).ToString("000") + "_" + SafeName(p);
                                    zip.CreateEntryFromFile(p, entry, CompressionLevel.Optimal);
                                    files++;
                                }
                            }
                            catch { entry = null; }
                            map[p] = entry;
                        }
                        if (entry != null) SetAt(s, i, Marker + entry);
                    }
                    string json;
                    string tmp = Path.Combine(Path.GetTempPath(), "mischpult-paket-" + Guid.NewGuid().ToString("N") + ".json");
                    try { if (!s.SaveTo(tmp)) return "Die Einstellungen ließen sich nicht schreiben."; json = File.ReadAllText(tmp, Encoding.UTF8); }
                    finally { try { File.Delete(tmp); } catch { } }
                    using (var w = new StreamWriter(zip.CreateEntry(SettingsEntry).Open(), new UTF8Encoding(false))) w.Write(json);
                    using (var w = new StreamWriter(zip.CreateEntry(InfoEntry).Open(), new UTF8Encoding(false)))
                        w.Write("Mischpult-Umzugspaket\r\nVersion=" + Program.Version + "\r\nErstellt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                                "\r\nDateien=" + files + "\r\n\r\nÖffnen: Mischpult starten, dann OPTIONEN → Sichern → „Einstellungen importieren“ – oder die Datei aufs Pult ziehen.\r\n");
                }
                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(tmpZip, zipPath);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Paket lesen und pruefen (ohne etwas zu veraendern).</summary>
        public static Info Read(string zipPath)
        {
            var info = new Info();
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var se = zip.GetEntry(SettingsEntry);
                    if (se == null) { info.Error = "Das ist kein Mischpult-Umzugs-Paket."; return info; }
                    info.Settings = LoadEntry(se);
                    if (info.Settings == null) { info.Error = "Die Einstellungen im Paket sind beschädigt."; return info; }
                    info.Files = zip.Entries.Count(e => e.FullName.StartsWith(FilePrefix, StringComparison.Ordinal) && e.Length > 0);
                    var ie = zip.GetEntry(InfoEntry);
                    if (ie != null)
                        using (var r = new StreamReader(ie.Open(), Encoding.UTF8))
                            foreach (var line in r.ReadToEnd().Split('\n'))
                            {
                                var l = line.Trim();
                                if (l.StartsWith("Version=")) info.Version = l.Substring(8);
                                else if (l.StartsWith("Erstellt=") && DateTime.TryParseExact(l.Substring(9), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) info.Created = t;
                            }
                }
            }
            catch (Exception ex) { info.Error = ex is InvalidDataException ? "Die Datei ist beschädigt oder kein Paket." : ex.Message; }
            return info;
        }

        private static MixerSettings LoadEntry(ZipArchiveEntry e)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "mischpult-paket-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                using (var src = e.Open()) using (var dst = File.Create(tmp)) src.CopyTo(dst);
                return MixerSettings.LoadFrom(tmp);
            }
            catch { return null; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>Hierhin kommen die Dateien aus einem Paket.</summary>
        public static string TargetFolder =>
            MixerSettings.DocsMode && !MixerSettings.Portable ? Path.Combine(MixerSettings.DocsRoot, "Umzug") : Path.Combine(MixerSettings.Folder, "Umzug");

        /// <summary>
        /// Paket uebernehmen: Dateien auspacken, Pfade anpassen, Einstellungen speichern (die alten vorher gesichert).
        /// Liefert null bei Erfolg, sonst den Fehler. Danach muss das Mischpult neu starten.
        /// </summary>
        public static string Import(string zipPath, string target = null, string settingsFile = null)
        {
            try
            {
                target = target ?? TargetFolder;
                MixerSettings s;
                var extracted = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var se = zip.GetEntry(SettingsEntry);
                    if (se == null) return "Das ist kein Mischpult-Umzugs-Paket.";
                    s = LoadEntry(se);
                    if (s == null) return "Die Einstellungen im Paket sind beschädigt.";
                    foreach (var e in zip.Entries)
                    {
                        if (!e.FullName.StartsWith(FilePrefix, StringComparison.Ordinal) || e.Length <= 0 || e.Length > MaxFile) continue;
                        string name = SafeName(e.Name);   // nie ausserhalb des Zielordners
                        Directory.CreateDirectory(target);
                        string dst = Path.Combine(target, name);
                        e.ExtractToFile(dst, true);
                        extracted[e.FullName] = dst;
                    }
                }
                var getters = Getters(s).ToList();
                for (int i = 0; i < getters.Count; i++)
                {
                    string p = getters[i](s);
                    if (p == null || !p.StartsWith(Marker, StringComparison.Ordinal)) continue;
                    SetAt(s, i, extracted.TryGetValue(p.Substring(Marker.Length), out string dst) ? dst : null);
                }
                s.Normalize();
                if (settingsFile == null) Update410.BackupSettings("vor-umzug");
                if (!s.SaveTo(settingsFile ?? MixerSettings.FilePath)) return "Die Einstellungen konnten nicht gespeichert werden.";
                return null;
            }
            catch (Exception ex) { return ex is InvalidDataException ? "Die Datei ist beschädigt oder kein Paket." : ex.Message; }
        }
    }
}
