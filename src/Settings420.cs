using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.2 Optik & Extras (Designs, Hintergruende und Waende sind neue Eintraege in den bisherigen Auswahlen)
        [DataMember] public bool NewsTogglesOn;      // 277 Schalter direkt im Fenster "Was ist neu"
        [DataMember] public bool PacksOn;            // 278 Funktions-Pakete
        [DataMember] public bool FileAssocOn;        // 279 Doppelklick auf Mischpult-Dateien
        [DataMember] public bool DeskMirrorOn;       // 283 spiegelnder Tisch
        [DataMember] public bool RgbWaveOn;          // 284 RGB-Welle ueber die Kanaele
        [DataMember] public bool RgbWaveBeat;        //     im Takt der Musik
        [DataMember] public bool FrameGlowOn;        // 285 leuchtender Fensterrand (Windows 11)
        [DataMember] public int FrameGlowMode;       //     0 Akzentfarbe, 1 pulsiert mit der Musik, 2 Farbe des lautesten Kanals
        [DataMember] public bool SplashOn;           // 286 Startbild
        [DataMember] public bool CapIconOn;          // 287 App-Symbol auf der Faderkappe
        [DataMember] public bool SoundArtOn;         // 288 Klang-Kunst
        [DataMember] public string ArtDay;           //     Tag der gesammelten Werte (yyyy-MM-dd)
        [DataMember] public Dictionary<string, string> ArtBins;    // Kanal-ID -> 96 Viertelstunden (Base64, 0..255)
        [DataMember] public Dictionary<string, int> ArtColors;     //     Farbe je Kanal (falls er inzwischen geloescht ist)
        [DataMember] public Dictionary<string, string> ArtNames;   //     Name je Kanal
        // ---- 4.2.1
        [DataMember] public List<string> DuckTriggers;   // Ducking mit mehreren Ausloesern (null = nur DuckTrigger); DuckTrigger ist immer der erste
        // ---- 4.2.3 Ducking-Feinschliff (alles Standard aus)
        [DataMember] public bool DuckLampsOn;        // Laempchen bei den Ausloesern im Ducking-Fenster
        [DataMember] public bool DuckMinTalkOn;      // kurze Geraeusche ignorieren
        [DataMember] public int DuckMinTalkMs;       //     so lange Ton am Stueck (0 = 200 ms)
        [DataMember] public bool DuckFadeOn;         // Uebergang selbst einstellen
        [DataMember] public int DuckDownMs;          //     leiser werden dauert (0 = 100 ms, 1 = sofort)
        [DataMember] public int DuckUpMs;            //     lauter werden dauert (0 = 600 ms)
        // ---- 4.2.5
        [DataMember] public bool AutoUpdateOn;       // Updates beim Start automatisch installieren (braucht OnlineUpdateOn)

        public static readonly int[] DuckMinTalkValues = { 100, 200, 300, 400, 500, 700, 1000 };
        public static readonly int[] DuckDownValues = { 1, 50, 100, 200, 300, 500, 1000, 2000 };
        public static readonly int[] DuckUpValues = { 200, 400, 600, 1000, 1500, 2000, 3000, 5000 };

        /// <summary>Millisekunden als Text: 1 = sofort, sonst "0,2 s".</summary>
        public static string MsText(int ms) => ms <= 1 ? "sofort" : (ms / 1000.0).ToString(ms % 100 == 0 ? "0.0" : "0.00", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " s";

        /// <summary>Position des naechstliegenden Werts in einer Liste.</summary>
        public static int NearestIndex(int[] values, int v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
            return best;
        }

        /// <summary>Alle Ausloeser des Duckings (mindestens einer).</summary>
        public List<string> DuckTriggerList => DuckTriggers != null && DuckTriggers.Count > 0 ? DuckTriggers : new List<string> { DuckTrigger };

        /// <summary>Ist dieser Kanal ein Ausloeser des Duckings?</summary>
        public bool IsDuckTrigger(string id) => id != null && (id == DuckTrigger || (DuckTriggers != null && DuckTriggers.Contains(id)));

        /// <summary>4.2.2: das Mikrofon als Ausloeser des Duckings (kein Kanal – nur der Pegel wird gelesen).</summary>
        public const string DuckMicId = "@mic";

        /// <summary>Gibt es diesen Ausloeser (Kanal oder Mikrofon)?</summary>
        private bool DuckTriggerExists(string id) => id == DuckMicId || (Channels != null && Channels.Exists(c => c.Id == id));

        public static readonly string[] FrameGlowModeNames = { "Akzentfarbe", "Pulsiert mit der Musik", "Farbe des lautesten Kanals" };
        public const int ArtSlots = 96;   // Viertelstunden am Tag

        private void Normalize420()
        {
            // 4.2.1: mehrere Ausloeser – nur vorhandene Kanaele, keine doppelten; einer allein = wie bisher
            if (DuckTriggers != null)
            {
                // 4.2.2: Kanaele zuerst, das Mikrofon zuletzt (der erste Ausloeser bleibt ein Kanal, wenn es einen gibt)
                DuckTriggers = DuckTriggers.Where(id => !string.IsNullOrEmpty(id) && DuckTriggerExists(id)).Distinct()
                    .OrderBy(id => id == DuckMicId ? 1 : 0).ToList();
                if (DuckTriggers.Count > 0) DuckTrigger = DuckTriggers[0];
                if (DuckTriggers.Count <= 1) DuckTriggers = null;
            }
            if (DuckTriggers != null && DuckTargets != null) DuckTargets.RemoveAll(id => DuckTriggers.Contains(id));
            if (DuckTargets != null) DuckTargets.RemoveAll(id => id == DuckMicId);   // das Mikrofon wird nie leiser gemacht
            FrameGlowMode = Math.Max(0, Math.Min(FrameGlowModeNames.Length - 1, FrameGlowMode));
            // 4.2.3: Feinschliff – fehlende Werte = Standard
            DuckMinTalkMs = DuckMinTalkMs <= 0 ? 200 : Math.Max(100, Math.Min(1000, DuckMinTalkMs));
            DuckDownMs = DuckDownMs <= 0 ? 100 : Math.Max(1, Math.Min(2000, DuckDownMs));
            DuckUpMs = DuckUpMs <= 0 ? 600 : Math.Max(100, Math.Min(5000, DuckUpMs));
            if (ArtBins != null)
            {
                foreach (var k in ArtBins.Keys.ToList())
                {
                    bool ok = false;
                    try { ok = !string.IsNullOrEmpty(k) && Convert.FromBase64String(ArtBins[k] ?? "").Length == ArtSlots; } catch { }
                    if (!ok) ArtBins.Remove(k);
                }
                if (ArtBins.Count > 32) ArtBins = ArtBins.Take(32).ToDictionary(x => x.Key, x => x.Value);
                if (ArtBins.Count == 0) ArtBins = null;
            }
            if (ArtBins == null) { ArtColors = null; ArtNames = null; }
            if (ArtDay != null && !DateTime.TryParseExact(ArtDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) { ArtDay = null; ArtBins = null; ArtColors = null; ArtNames = null; }
        }
    }
}
