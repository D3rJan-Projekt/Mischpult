using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.1: online nach Updates suchen – fragt die neueste Version eines GitHub-Projekts ab ("Releases") und laedt die EXE.
    /// Nur wenn eingeschaltet; sonst verbindet sich das Mischpult nie mit dem Internet.
    /// </summary>
    internal static class OnlineUpdate410
    {
        [DataContract]
        private sealed class GhAsset
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string Url;
            [DataMember(Name = "size")] public long Size;
        }

        [DataContract]
        private sealed class GhRelease
        {
            [DataMember(Name = "tag_name")] public string Tag;
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "body")] public string Body;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Pre;
            [DataMember(Name = "html_url")] public string Page;
            [DataMember(Name = "assets")] public List<GhAsset> Assets;
        }

        public sealed class Found
        {
            public string Version, Title, Notes, Url, AssetName, Page;
            public long Size;
            public bool IsZip => AssetName != null && AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            public bool IsNewer => Update410.CompareVersion(Version, Program.Version) > 0;
            /// <summary>Die ersten Zeilen der Versionshinweise (ohne Markdown-Zeichen).</summary>
            public string ShortNotes(int lines)
            {
                if (string.IsNullOrWhiteSpace(Notes)) return null;
                var l = Notes.Replace("\r", "").Split('\n').Select(x => x.Trim().TrimStart('#', '*', '-', ' ').Trim()).Where(x => x.Length > 0).Take(lines).ToList();
                return l.Count == 0 ? null : string.Join("\n", l);
            }
        }

        /// <summary>Antwort der GitHub-API auswerten (null = keine passende EXE/ZIP oder Entwurf).</summary>
        internal static Found Parse(string json)
        {
            GhRelease r;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                    r = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);
            }
            catch { return null; }
            if (r == null || r.Draft || string.IsNullOrEmpty(r.Tag)) return null;
            var assets = r.Assets ?? new List<GhAsset>();
            var a = assets.FirstOrDefault(x => string.Equals(x.Name, "Mischpult.exe", StringComparison.OrdinalIgnoreCase))
                 ?? assets.FirstOrDefault(x => x.Name != null && x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && x.Name.IndexOf("ischpult", StringComparison.OrdinalIgnoreCase) >= 0)
                 ?? assets.FirstOrDefault(x => x.Name != null && x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                 ?? assets.FirstOrDefault(x => x.Name != null && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (a == null || string.IsNullOrEmpty(a.Url) || !a.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return new Found
            {
                Version = Update410.CleanVersion(r.Tag),
                Title = string.IsNullOrWhiteSpace(r.Name) ? "Mischpult " + Update410.CleanVersion(r.Tag) : r.Name.Trim(),
                Notes = r.Body, Url = a.Url, AssetName = a.Name, Size = a.Size, Page = r.Page,
            };
        }

        private static HttpWebRequest Request(string url)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mischpult/" + Program.Version;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            req.AllowAutoRedirect = true;
            return req;
        }

        /// <summary>Neueste Version abfragen (blockiert – im Hintergrund aufrufen). null + error = Problem, null ohne error = nichts gefunden.</summary>
        public static Found Check(string repo, out string error)
        {
            error = null;
            repo = MixerSettings.CleanRepo(repo);
            if (repo == null) { error = "Kein GitHub-Projekt eingetragen."; return null; }
            try
            {
                var req = Request("https://api.github.com/repos/" + repo + "/releases/latest");
                req.Accept = "application/vnd.github+json";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var f = Parse(r.ReadToEnd());
                    if (f == null) error = "Beim neuesten Release liegt keine Mischpult.exe.";
                    return f;
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse h && h.StatusCode == HttpStatusCode.NotFound)
            {
                error = "Projekt „" + repo + "“ nicht gefunden (oder es hat noch kein Release).";
                return null;
            }
            catch (Exception ex) { error = "Keine Verbindung: " + ex.Message; return null; }
        }

        /// <summary>EXE herunterladen (bzw. aus der ZIP holen) und pruefen. Liefert den Pfad oder null + error.</summary>
        public static string Download(Found f, Action<int> progress, out string error)
        {
            error = null;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Mischpult-Update", (f.Version ?? "neu").Replace('/', '_'));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, f.IsZip ? "Mischpult.zip" : "Mischpult.exe");
                var req = Request(f.Url);
                using (var resp = req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(file + ".part"))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : f.Size;
                    var buf = new byte[81920];
                    long done = 0; int last = -1;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        done += n;
                        int pct = total > 0 ? (int)Math.Min(100, done * 100 / total) : -1;
                        if (pct != last) { last = pct; progress?.Invoke(pct); }
                    }
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".part", file);
                string exe = file;
                if (f.IsZip)
                {
                    exe = Path.Combine(dir, "Mischpult.exe");
                    using (var zip = ZipFile.OpenRead(file))
                    {
                        var e = zip.Entries.FirstOrDefault(x => string.Equals(x.Name, "Mischpult.exe", StringComparison.OrdinalIgnoreCase));
                        if (e == null) { error = "In der ZIP liegt keine Mischpult.exe."; return null; }
                        e.ExtractToFile(exe, true);
                    }
                }
                var info = Update410.Read(exe);
                if (info == null || !info.IsMischpult) { error = "Die geladene Datei ist keine Mischpult-EXE."; return null; }
                if (!Update410.IsNewer(info)) { error = "Die geladene Version (" + info.Text + ") ist nicht neuer."; return null; }
                return exe;
            }
            catch (Exception ex) { error = "Laden hat nicht geklappt: " + ex.Message; return null; }
        }
    }
}
