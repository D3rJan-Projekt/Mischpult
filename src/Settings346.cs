using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>3.4.6: Deko auf dem Pult (Lage in logischen Pixeln im Kanal-Bereich).</summary>
    [DataContract]
    internal sealed class DekoConfig
    {
        [DataMember] public int Kind;          // 0 Tasse, 1 Pflanze, 2 Kopfhoerer, 3 Gummiente
        [DataMember] public float X, Y;        // Mitte, logische Pixel im Kanal-Bereich
    }

    internal sealed partial class ChannelConfig
    {
        [DataMember] public int WidthPct;      // 3.4.6: Stereo-Breite in % (0 = 100 % normal, 1 = mono)
        [DataMember] public int HpHz;          // 3.4.6: Bass-Filter (Hochpass), 0 = aus
        [DataMember] public bool PopGuard;     // 3.4.6: Knall-Bremse
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.4.6 Pegel (alles Standard aus)
        [DataMember] public bool TruePeakOn;       // 103 True-Peak am MASTER
        [DataMember] public int KScale;            // 104 0 aus, 1 K-14, 2 K-20 (MASTER/Ausgaenge)
        [DataMember] public bool DynamicsOn;       // 105 Dynamik (Spitze minus Lautheit)
        [DataMember] public int LufsTarget;        // 106 0 aus, sonst Index in LufsTargetValues
        [DataMember] public bool BalanceOn;        // 107 L/R-Balance unter den Pegeln
        [DataMember] public bool HeadroomOn;       // 108 Reserve bis zum Uebersteuern
        [DataMember] public bool MeterLupeOn;      // 109 Doppelklick auf einen Pegel: gross
        // ---- 3.4.6 Optik
        [DataMember] public bool OutDragOn;        // 110 Ausgaenge breiter ziehen
        [DataMember] public Dictionary<string, int> OutWidths;   // "master" bzw. Geraete-ID -> logische Pixel
        [DataMember] public bool DekoOn;           // 111 Deko auf dem Pult
        [DataMember] public List<DekoConfig> Deko;
        [DataMember] public bool CablesOn;         // 112 Kabel unter dem Pult
        [DataMember] public bool CrtOn;            // 114 Roehrenbild
        // (113 Regen/Schnee: neue Auswahl beim bewegten Hintergrund, 115 Griffmulde: neue Kappen-Form)
        // ---- 3.4.6 Ton
        [DataMember] public bool StereoWidthOn;    // 116 Stereo-Breite pro Kanal
        [DataMember] public bool BassCutOn;        // 117 Bass-Filter pro Kanal
        [DataMember] public bool PopGuardOn;       // 118 Knall-Bremse pro Kanal
        [DataMember] public bool MixPerDeviceOn;   // 120 Mix pro Ausgabegeraet
        [DataMember] public Dictionary<string, Dictionary<string, float>> DeviceMixes;
        [DataMember] public bool AlarmOn;          // 121 Wecker
        [DataMember] public int AlarmHour, AlarmMinute;   // 0/0 = 7:00
        [DataMember] public int AlarmDays;         // 0 taeglich, 1 Mo–Fr, 2 Sa+So
        [DataMember] public int AlarmFadeMin;      // 0 = 5 Minuten
        [DataMember] public int AlarmVolume;       // 0 = 40 %
        [DataMember] public bool AlarmNoPlay;      // true = nicht auf Play druecken
        // ---- 3.4.6 Bedienung
        [DataMember] public bool SnapOn;           // 122 Fader rasten an den Skalen-Strichen ein
        [DataMember] public int WheelStep;         // 123 0 wie bisher, sonst Index in WheelStepDb
        [DataMember] public bool SceneKeysOn;      // 124 Szenen-Tasten in der Kopfleiste
        [DataMember] public bool DrawerOn;         // 125 Schublade am Bildschirmrand
        [DataMember] public bool TaskbarButtonsOn; // 126 Knoepfe in der Taskleisten-Vorschau
        [DataMember] public bool TaskbarVolumeOn;  // 126 Lautstaerke als Balken im Taskleisten-Symbol
        [DataMember] public bool JumpListOn;       // 126 Rechtsklick-Menue der Taskleiste mit Szenen
        [DataMember] public int TrayIconMode;      // 127 0 aus, 1 Lautstaerke, 2 Pegel
        [DataMember] public bool SongToastOn;      // 128 Song-Einblendung
        [DataMember] public bool BpmOn;            // 129 Takt-Lampe
        [DataMember] public int DrawerEdge;        // 125 0 rechts, 1 links, 2 oben
        [DataMember] public int SongToastPos;      // 128 0 unten rechts, 1 oben rechts, 2 unten links, 3 oben links
        // (130 Portabel: Datei neben der EXE, nicht in den Einstellungen)

        public static readonly string[] DrawerEdgeNames = { "Rechts", "Links", "Oben" };
        public static readonly string[] SongToastPosNames = { "Unten rechts", "Oben rechts", "Unten links", "Oben links" };
        public static readonly string[] TrayIconNames = { "Normal", "Lautstärke", "Pegel" };

        // ---------------------------------------------------------------- 130 Portabler Modus
        /// <summary>Ordner der EXE (leer, falls unbekannt).</summary>
        public static string ExeFolder
        {
            get
            {
                try { return System.IO.Path.GetDirectoryName(typeof(MixerSettings).Assembly.Location) ?? ""; }
                catch { return ""; }
            }
        }
        /// <summary>Liegt diese Datei neben der EXE, speichert das Mischpult alles im Ordner daneben.</summary>
        public const string PortableMarkerName = "portabel.txt";
        public static string PortableMarker => System.IO.Path.Combine(ExeFolder, PortableMarkerName);
        public static string PortableFolder => System.IO.Path.Combine(ExeFolder, "Mischpult-Daten");
        private static int _portable = -1;
        /// <summary>Beim ersten Zugriff festgelegt (gilt bis zum Neustart).</summary>
        public static bool Portable
        {
            get
            {
                if (_portable < 0)
                {
                    bool p = false;
                    try { p = ExeFolder.Length > 0 && System.IO.File.Exists(PortableMarker); } catch { }
                    _portable = p ? 1 : 0;
                }
                return _portable == 1;
            }
        }

        public static readonly int[] LufsTargetValues = { 0, -14, -16, -23, -24 };
        public static readonly string[] LufsTargetNames = { "Aus", "−14 (Spotify, YouTube)", "−16 (Apple Music)", "−23 (Fernsehen EU)", "−24 (Fernsehen USA)" };
        public static readonly float[] WheelStepDb = { 0f, 0.5f, 1f, 2f, 3f };
        public static readonly float[] WheelStepPct = { 0f, 1f, 2f, 5f, 10f };
        public static readonly string[] WheelStepNames = { "Wie bisher", "0,5 dB (1 %)", "1 dB (2 %)", "2 dB (5 %)", "3 dB (10 %)" };
        public static readonly int[] StereoWidthValues = { 1, 50, 100, 150, 200 };   // 1 = mono
        public static float WidthOf(ChannelConfig c) => c == null || c.WidthPct == 0 ? 1f : c.WidthPct == 1 ? 0f : c.WidthPct / 100f;
        public static readonly string[] StereoWidthNames = { "Mono", "Schmal (50 %)", "Normal", "Breit (150 %)", "Extra breit (200 %)" };
        public static readonly int[] HpValues = { 0, 80, 120, 200 };
        public static readonly int[] AlarmFadeValues = { 1, 3, 5, 10, 15 };
        public static readonly int[] AlarmVolumeValues = { 20, 30, 40, 50, 60, 80 };
        public static readonly string[] AlarmDayNames = { "Jeden Tag", "Montag bis Freitag", "Samstag und Sonntag" };
        public const int DekoKinds = 7;   // 4.0 finale: 4 Diskokugel, 5 Lava-Lampe, 6 Katze

        public int AlarmHourNow => AlarmHour == 0 && AlarmMinute == 0 ? 7 : AlarmHour;
        public int AlarmFadeNow => Array.IndexOf(AlarmFadeValues, AlarmFadeMin) >= 0 ? AlarmFadeMin : 5;
        public int AlarmVolumeNow => Array.IndexOf(AlarmVolumeValues, AlarmVolume) >= 0 ? AlarmVolume : 40;
        public float LufsTargetDb => LufsTarget > 0 && LufsTarget < LufsTargetValues.Length ? LufsTargetValues[LufsTarget] : 0f;

        private void Normalize346()
        {
            KScale = Math.Max(0, Math.Min(2, KScale));
            if (LufsTarget < 0 || LufsTarget >= LufsTargetValues.Length) LufsTarget = 0;
            if (WheelStep < 0 || WheelStep >= WheelStepDb.Length) WheelStep = 0;
            TrayIconMode = Math.Max(0, Math.Min(2, TrayIconMode));
            DrawerEdge = Math.Max(0, Math.Min(2, DrawerEdge));
            SongToastPos = Math.Max(0, Math.Min(3, SongToastPos));
            AlarmHour = Math.Max(0, Math.Min(23, AlarmHour));
            AlarmMinute = Math.Max(0, Math.Min(59, AlarmMinute));
            AlarmDays = Math.Max(0, Math.Min(2, AlarmDays));
            if (AlarmFadeMin != 0 && Array.IndexOf(AlarmFadeValues, AlarmFadeMin) < 0) AlarmFadeMin = 0;
            if (AlarmVolume != 0 && Array.IndexOf(AlarmVolumeValues, AlarmVolume) < 0) AlarmVolume = 0;
            if (OutWidths != null)
            {
                foreach (var k in new List<string>(OutWidths.Keys))
                    if (k == null) OutWidths.Remove(k); else OutWidths[k] = Math.Max(DragWidthMin, Math.Min(DragWidthMax, OutWidths[k]));
                if (OutWidths.Count == 0) OutWidths = null;
            }
            if (Deko != null)
            {
                Deko.RemoveAll(d => d == null || float.IsNaN(d.X) || float.IsNaN(d.Y));
                foreach (var d in Deko) d.Kind = Math.Max(0, Math.Min(DekoKinds - 1, d.Kind));
                if (Deko.Count > 20) Deko.RemoveRange(0, Deko.Count - 20);
                if (Deko.Count == 0) Deko = null;
            }
            if (DeviceMixes != null)
            {
                foreach (var k in new List<string>(DeviceMixes.Keys))
                    if (k == null || DeviceMixes[k] == null) DeviceMixes.Remove(k);
                if (DeviceMixes.Count > 30) DeviceMixes = null;   // Sicherheitsnetz
            }
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    if (c.WidthPct != 0 && Array.IndexOf(StereoWidthValues, c.WidthPct) < 0) c.WidthPct = 0;
                    if (c.WidthPct == 100) c.WidthPct = 0;
                    if (c.HpHz != 0 && Array.IndexOf(HpValues, c.HpHz) < 0) c.HpHz = 0;
                }
        }
    }
}
