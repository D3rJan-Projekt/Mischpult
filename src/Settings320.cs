using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>3.2: ein Makro – mehrere Aktionen (wie bei den Tastenkuerzeln) nacheinander auf einen Knopf.</summary>
    [DataContract]
    internal sealed class MacroConfig
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public List<string> Steps;   // Aktionen wie bei den Tastenkuerzeln, z. B. "mic.mute", "scene:<id>"
    }

    /// <summary>3.2: Breite, Lautstaerke-Deckel, Mono/getauscht, Verzoegerung und "im Hintergrund leiser" pro Kanal.</summary>
    internal sealed partial class ChannelConfig
    {
        [DataMember] public int Width;          // 0 wie alle, 1 schmal, 2 normal, 3 breit
        [DataMember] public float? MaxDb;       // Lautstaerke-Deckel (dB, z. B. -6), null = keiner
        [DataMember] public int Stereo;         // 0 normal, 1 mono, 2 links/rechts getauscht
        [DataMember] public int DelayMs;        // Verzoegerung 0..1000 ms
        [DataMember] public bool BgQuiet;       // leiser, wenn seine App nicht im Vordergrund ist
    }

    /// <summary>3.2: Ausgang mono bzw. links/rechts getauscht.</summary>
    internal sealed partial class OutputFxConfig
    {
        [DataMember] public bool Mono;
        [DataMember] public bool Swap;
    }

    /// <summary>3.2: ein eigenes Design kann auch Schrift, Pegel-Farben und Material mitnehmen (Design-Datei).</summary>
    internal sealed partial class CustomDesign
    {
        [DataMember] public string Font;
        [DataMember] public int? MeterColors, MeterLow, MeterMid, MeterTop;
        [DataMember] public float? MeterMidDb, MeterTopDb;
        [DataMember] public int? PanelMaterial, KnobStyle, CapStyle, CheekStyle, LcdStyle, CapShape, KeyShape;
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.2 Aussehen
        [DataMember] public bool FontOn;             // eigene Schrift fuers ganze Pult
        [DataMember] public string FontName;         // Schriftart (null = Bahnschrift)
        [DataMember] public int? MeterLowColor, MeterMidColor, MeterTopColor;   // eigene Pegel-Farben (MeterColors == 4), ARGB
        [DataMember] public float? MeterMidDb, MeterTopDb;   // ab hier Mitte bzw. oben (fehlt = -18 / -6 dB)
        [DataMember] public bool ChannelWidthOn;     // eigene Breite pro Kanal
        [DataMember] public bool TimeDesignOn;       // Design nach Uhrzeit
        [DataMember] public int? TimeDayStyle, TimeNightStyle;   // fehlt = Studio / Neon
        [DataMember] public int? TimeDayFrom, TimeNightFrom;     // Stunden (fehlt = 7 / 20)
        [DataMember] public int AccentSource;        // Akzentfarbe von: 0 Auswahl, 1 Windows, 2 Album-Cover
        [DataMember] public bool CoverAmbiOn;        // Cover-Farbe auch fuers Ambilight
        [DataMember] public int? Opacity;            // Deckkraft des Fensters in % (fehlt = 100)
        [DataMember] public bool OpacityIdleOnly;    // nur durchsichtig, wenn das Pult nicht aktiv ist
        [DataMember] public int? Zoom;               // Groesse in % (fehlt = 100), wirkt nach Neustart
        [DataMember] public bool ShotClipOff;        // true = Bild vom Pult nicht in die Zwischenablage
        [DataMember] public bool ShotButtonOn;       // Kamera-Knopf in der Kopfleiste

        // ---- 3.2 Bedienung
        [DataMember] public bool MacrosOn;
        [DataMember] public List<MacroConfig> Macros;
        [DataMember] public bool CountdownOn;
        [DataMember] public string CountdownText;
        [DataMember] public int? CountdownMin;       // fehlt = 5
        [DataMember] public bool CountdownDisplaysOff;   // true = nicht in den Anzeigen der Kanaele
        [DataMember] public bool CountdownBeepOff;   // true = kein Ton am Ende
        [DataMember] public int FaderCurve;          // 0 Standard, 1 fein oben, 2 gleichmaessig, 3 fein unten
        [DataMember] public int SceneXfade;          // Szenen ueberblenden: Index in SceneXfadeValues (0 = aus)
        [DataMember] public bool CapsOff;            // true = Lautstaerke-Deckel der Kanaele nicht beachten
        [DataMember] public bool TestToneOn;         // Testton links/rechts verfuegbar (Knoepfe/Menue)
        [DataMember] public bool OutputNamesOff;     // true = eigene Namen der Ausgaenge nicht zeigen
        [DataMember] public Dictionary<string, string> OutputNames;   // Geraete-ID -> eigener Name
        [DataMember] public bool PultLocked;         // Pult gesperrt (Klicks und Mausrad tun nichts)
        [DataMember] public bool LockFeatureOn;      // Sperren verfuegbar (Tastenkuerzel, Doppelklick zum Entsperren)
        [DataMember] public int MotorSpeed;          // Motorfader: 0 normal, 1 langsam, 2 schnell
        [DataMember] public int AutoSort;            // Kanaele automatisch sortieren: 0 aus, 1 Name, 2 Farbe, 3 spielende zuerst
        [DataMember] public bool BgAppsQuietOn;      // Hintergrund-Apps leiser (Kanaele mit Haken)
        [DataMember] public float? BgAppsQuietDb;    // so viel leiser (fehlt = -10)
        [DataMember] public bool AutoPauseOn;        // Musik pausieren, wenn ein Video anfaengt
        [DataMember] public string AutoPauseTrigger; // Kanal, der ausloest (z. B. BROWSER)
        [DataMember] public string AutoPauseMusic;   // Kanal mit der Musik (z. B. MUSIK)
        [DataMember] public bool ChannelSoundOff;    // true = Mono/getauscht/Verzoegerung der Kanaele aus

        public static readonly int[] SceneXfadeValues = { 0, 1, 2, 3, 5, 10 };
        public static readonly int[] CountdownMinutes = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };
        public static readonly int[] OpacityValues = { 100, 95, 90, 85, 80, 70, 60 };
        public static readonly int[] ZoomValues = { 80, 90, 100, 110, 125, 150 };
        public static readonly int[] DelayValues = { 0, 20, 50, 100, 150, 200, 300, 500, 1000 };
        public static readonly float[] CapValues = { -1f, -3f, -6f, -10f, -15f, -20f };

        private void Normalize320()
        {
            if (string.IsNullOrWhiteSpace(FontName)) FontName = null; else FontName = FontName.Trim();
            if (MeterMidDb.HasValue) MeterMidDb = Math.Max(-40f, Math.Min(-3f, float.IsNaN(MeterMidDb.Value) ? -18f : MeterMidDb.Value));
            if (MeterTopDb.HasValue) MeterTopDb = Math.Max(-30f, Math.Min(0f, float.IsNaN(MeterTopDb.Value) ? -6f : MeterTopDb.Value));
            if (MeterMidDb.HasValue && MeterTopDb.HasValue && MeterTopDb <= MeterMidDb) MeterTopDb = Math.Min(0f, MeterMidDb.Value + 3f);
            if (TimeDayStyle.HasValue && (TimeDayStyle < 0 || TimeDayStyle > MaxStyle)) TimeDayStyle = null;
            if (TimeNightStyle.HasValue && (TimeNightStyle < 0 || TimeNightStyle > MaxStyle)) TimeNightStyle = null;
            if (TimeDayFrom.HasValue) TimeDayFrom = Math.Max(0, Math.Min(23, TimeDayFrom.Value));
            if (TimeNightFrom.HasValue) TimeNightFrom = Math.Max(0, Math.Min(23, TimeNightFrom.Value));
            AccentSource = Math.Max(0, Math.Min(2, AccentSource));
            if (Opacity.HasValue && Array.IndexOf(OpacityValues, Opacity.Value) < 0) Opacity = null;
            if (Opacity == 100) Opacity = null;
            if (Zoom.HasValue && Array.IndexOf(ZoomValues, Zoom.Value) < 0) Zoom = null;
            if (Zoom == 100) Zoom = null;
            if (Macros != null)
            {
                Macros.RemoveAll(m => m == null || string.IsNullOrWhiteSpace(m.Name));
                foreach (var m in Macros)
                {
                    if (string.IsNullOrEmpty(m.Id)) m.Id = Guid.NewGuid().ToString("N");
                    if (m.Steps == null) m.Steps = new List<string>();
                    m.Steps.RemoveAll(s => string.IsNullOrEmpty(s) || s.StartsWith("macro:", StringComparison.Ordinal));
                    if (m.Steps.Count > 20) m.Steps.RemoveRange(20, m.Steps.Count - 20);
                }
                if (Macros.Count > 10) Macros.RemoveRange(10, Macros.Count - 10);
                if (Macros.Count == 0) Macros = null;
            }
            if (CountdownMin.HasValue && Array.IndexOf(CountdownMinutes, CountdownMin.Value) < 0) CountdownMin = null;
            if (string.IsNullOrWhiteSpace(CountdownText)) CountdownText = null;
            else if (CountdownText.Length > 24) CountdownText = CountdownText.Substring(0, 24);
            FaderCurve = Math.Max(0, Math.Min(3, FaderCurve));
            SceneXfade = Math.Max(0, Math.Min(SceneXfadeValues.Length - 1, SceneXfade));
            if (OutputNames != null)
            {
                foreach (var k in OutputNames.Keys.ToList())
                    if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(OutputNames[k])) OutputNames.Remove(k);
                    else if (OutputNames[k].Length > 32) OutputNames[k] = OutputNames[k].Substring(0, 32).Trim();
                if (OutputNames.Count == 0) OutputNames = null;
            }
            MotorSpeed = Math.Max(0, Math.Min(2, MotorSpeed));
            AutoSort = Math.Max(0, Math.Min(3, AutoSort));
            if (BgAppsQuietDb.HasValue) BgAppsQuietDb = Math.Max(-40f, Math.Min(-3f, float.IsNaN(BgAppsQuietDb.Value) ? -10f : BgAppsQuietDb.Value));
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    c.Width = Math.Max(0, Math.Min(3, c.Width));
                    if (c.WidthPx != 0) c.WidthPx = Math.Max(DragWidthMin, Math.Min(DragWidthMax, c.WidthPx));   // 3.4.5
                    if (c.MaxDb.HasValue && (float.IsNaN(c.MaxDb.Value) || c.MaxDb >= 0f || c.MaxDb < -40f)) c.MaxDb = null;
                    c.Stereo = Math.Max(0, Math.Min(2, c.Stereo));
                    c.DelayMs = Math.Max(0, Math.Min(1000, c.DelayMs));
                }
            if (AutoPauseTrigger != null && Channels != null && Channels.Find(c => c.Id == AutoPauseTrigger) == null) AutoPauseTrigger = null;
            if (AutoPauseMusic != null && Channels != null && Channels.Find(c => c.Id == AutoPauseMusic) == null) AutoPauseMusic = null;
        }
    }
}
