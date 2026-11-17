using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.17: Hoer-Tagebuch – wie lange und wie laut pro Tag gehoert wurde und welche App wie lange Ton gemacht hat.
    /// "Laut" schaetzt aus der Lautheit des Standardgeraets (LUFS) und der Windows-Lautstaerke, wie laut es ankommt
    /// (ohne Messgeraet nur ein Richtwert). Gespeichert wird in einer eigenen Datei, die letzten 60 Tage.
    /// </summary>
    [DataContract]
    internal sealed class ListeningDiary
    {
        [DataContract]
        internal sealed class Day
        {
            [DataMember] public string Date;            // yyyy-MM-dd
            [DataMember] public double ListenSec;       // Ton lief
            [DataMember] public double LoudSec;         // davon laut
            [DataMember] public double LevelSum;        // fuer den Mittelwert (dB je Sekunde mit Ton)
            [DataMember] public Dictionary<string, double> Apps = new Dictionary<string, double>();   // App-Name -> Sekunden mit Ton
            [DataMember] public bool Warned;

            public double AvgLevel => ListenSec > 1 ? LevelSum / ListenSec : double.NaN;
        }

        [DataMember] public List<Day> Days = new List<Day>();

        /// <summary>"Laut" ab diesem Wert (dB, Lautheit + Windows-Lautstaerke): 0 empfindlich, 1 normal, 2 tolerant.</summary>
        public static readonly float[] LoudLevels = { -26f, -20f, -16f };
        public static readonly string[] LoudNames = { "Empfindlich", "Normal", "Tolerant" };
        public static readonly int[] WarnHours = { 0, 1, 2, 4 };
        public static readonly string[] WarnNames = { "Nie", "Ab 1 Std.", "Ab 2 Std.", "Ab 4 Std." };

        private double _saveIn = 60;
        public bool Dirty { get; private set; }
        public int Version { get; private set; }

        public static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public Day Get(DateTime date, bool create)
        {
            string k = Key(date);
            var d = Days.FirstOrDefault(x => x.Date == k);
            if (d == null && create)
            {
                d = new Day { Date = k };
                Days.Add(d);
                Days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
                while (Days.Count > 60) Days.RemoveAt(0);
            }
            return d;
        }

        public Day Today => Get(DateTime.Now, true);

        /// <summary>
        /// Aus dem Takt (einmal pro Sekunde reicht): level = Lautheit + Windows-Lautstaerke in dB (NaN = nichts gemessen),
        /// apps = Namen der Apps, die gerade hoerbar Ton machen. Liefert true, wenn heute die Warn-Grenze erreicht wurde.
        /// </summary>
        public bool Tick(double dt, float level, IEnumerable<string> apps, float loudFrom, int warnHours, DateTime now)
        {
            if (dt <= 0 || dt > 5) return false;
            var d = Get(now, true);
            bool sound = !float.IsNaN(level) && level > -60f;
            if (sound)
            {
                d.ListenSec += dt;
                d.LevelSum += level * dt;
                if (level >= loudFrom) d.LoudSec += dt;
            }
            if (apps != null)
                foreach (var a in apps.Distinct())
                {
                    if (string.IsNullOrEmpty(a)) continue;
                    d.Apps[a] = (d.Apps.TryGetValue(a, out var s) ? s : 0) + dt;
                }
            Dirty = true;
            Version++;
            _saveIn -= dt;
            if (warnHours > 0 && !d.Warned && d.LoudSec >= warnHours * 3600.0) { d.Warned = true; return true; }
            return false;
        }

        /// <summary>Faellig zum Speichern (etwa jede Minute)?</summary>
        public bool SaveDue()
        {
            if (!Dirty || _saveIn > 0) return false;
            _saveIn = 60;
            return true;
        }

        public void Clear() { Days.Clear(); Dirty = true; Version++; }

        public static string FilePath => Path.Combine(MixerSettings.Folder, "hoertagebuch.json");

        private static DataContractJsonSerializer Ser() =>
            new DataContractJsonSerializer(typeof(ListeningDiary), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static ListeningDiary Load(string path = null)
        {
            try
            {
                path = path ?? FilePath;
                if (File.Exists(path))
                    using (var fs = File.OpenRead(path))
                    {
                        var d = (ListeningDiary)Ser().ReadObject(fs);
                        if (d != null)
                        {
                            if (d.Days == null) d.Days = new List<Day>();
                            foreach (var x in d.Days) if (x.Apps == null) x.Apps = new Dictionary<string, double>();
                            d._saveIn = 60;
                            return d;
                        }
                    }
            }
            catch { }
            return new ListeningDiary();
        }

        public bool Save(string path = null)
        {
            try
            {
                path = path ?? FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                using (var fs = File.Create(tmp))
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true, "  "))
                {
                    Ser().WriteObject(w, this);
                    w.Flush();
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Dirty = false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>"2 Std. 15 Min." / "45 Min." / "unter 1 Min.".</summary>
        public static string Duration(double sec)
        {
            int min = (int)Math.Round(sec / 60.0);
            if (sec < 60) return sec < 1 ? "–" : "unter 1 Min.";
            if (min < 60) return min + " Min.";
            return (min / 60) + " Std." + (min % 60 > 0 ? " " + (min % 60) + " Min." : "");
        }

        /// <summary>Apps der letzten n Tage, meiste Zeit zuerst.</summary>
        public List<KeyValuePair<string, double>> TopApps(DateTime until, int days, int max)
        {
            var sum = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < days; i++)
            {
                var d = Get(until.AddDays(-i), false);
                if (d == null) continue;
                foreach (var kv in d.Apps) sum[kv.Key] = (sum.TryGetValue(kv.Key, out var s) ? s : 0) + kv.Value;
            }
            return sum.OrderByDescending(kv => kv.Value).Take(max).ToList();
        }

        /// <summary>Nur Vorschau: zwei Wochen erfundene Werte.</summary>
        public static ListeningDiary Demo(DateTime today)
        {
            var d = new ListeningDiary();
            var rnd = new Random(11);
            string[] apps = { "Spiel", "Spotify", "Discord", "Chrome", "Medal" };
            for (int i = 13; i >= 0; i--)
            {
                var day = d.Get(today.AddDays(-i), true);
                double listen = (1.5 + rnd.NextDouble() * 4.5) * 3600 * (i % 7 == 5 ? 1.6 : 1);
                day.ListenSec = listen;
                day.LoudSec = listen * (0.1 + rnd.NextDouble() * 0.45);
                day.LevelSum = listen * (-24 + rnd.NextDouble() * 6);
                double left = listen * 1.3;
                foreach (var a in apps)
                {
                    double s = left * (0.25 + rnd.NextDouble() * 0.4);
                    day.Apps[a] = s;
                    left -= s * 0.6;
                }
            }
            return d;
        }
    }
}
