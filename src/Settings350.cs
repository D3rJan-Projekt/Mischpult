using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>3.5: Kanal-Vorlage (Farbe, Klang, EQ … eines Kanals zum Wiederverwenden).</summary>
    [DataContract]
    internal sealed class ChannelTemplate
    {
        [DataMember] public string Name;
        [DataMember] public ChannelConfig Config;
    }

    internal sealed partial class ChannelConfig
    {
        [DataMember] public int LpHz;          // 3.5: Hoehen-Filter (Tiefpass), 0 = aus
        [DataMember] public bool Dialog;       // 3.5: Stimmen nach vorne
        [DataMember] public bool Pinned;       // 3.5: angepinnt (bleibt links stehen)
    }

    internal sealed partial class OutputFxConfig
    {
        [DataMember] public int BassMonoHz;    // 3.5: Bass mono, 0 = aus
    }

    internal sealed partial class Scene
    {
        [DataMember] public int XfadeIdx;      // 3.5: eigene Ueberblendzeit: Index in SceneXfadeOwnValues (0 = wie eingestellt)
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.5 Pegel und Anzeigen (alles Standard aus)
        [DataMember] public bool PhaseWarnOn;      // 131 Phasen-Warnung
        [DataMember] public bool ClipLogOn;        // 132 Uebersteuerungs-Liste
        [DataMember] public bool LatencyOn;        // 149 Verzoegerung am Kanal
        [DataMember] public bool DevFormatOn;      // 150 Geraete-Format am Ausgang
        [DataMember] public bool HearTimeOn;       // 151 Hoerzeit heute
        [DataMember] public string HearDate;       //     Tag der Zaehlung (yyyy-MM-dd)
        [DataMember] public int HearSec, HearLoudSec;
        [DataMember] public bool ChanStatsOn;      // 152 Kanal-Statistik
        [DataMember] public bool NoSoundWarnOn;    // 153 Kein-Ton-Hinweis
        // ---- 3.5 Optik
        [DataMember] public bool TubesOn;          // 135 Gluehende Roehren
        [DataMember] public bool SceneSweepOn;     // 136 Lauflicht beim Szenenwechsel
        [DataMember] public bool ShakeOn;          // 154 Pult wackelt mit dem Bass
        [DataMember] public bool SpecBgOn;         // 155 Spektrum hinter den Kanaelen
        [DataMember] public bool DevIconOn;        // 156 Geraete-Bild am Ausgang
        // ---- 3.5 Ton
        [DataMember] public bool TrebleCutOn;      // 137 Hoehen-Filter pro Kanal
        [DataMember] public bool DialogOn;         // 138 Stimmen nach vorne
        [DataMember] public bool AdMuteOn;         // 139 Werbung stumm
        [DataMember] public bool BassMonoOn;       // 157 Bass mono an den Ausgaengen
        [DataMember] public bool MirrorOn;         // 158 Zwei Geraete gleichzeitig
        [DataMember] public string MirrorDevice;
        [DataMember] public string MirrorDeviceName;
        [DataMember] public int MirrorDelayMs;
        [DataMember] public bool NoiseOn;          // 159 Einschlaf-Geraeusche (Kanal sichtbar)
        [DataMember] public int NoiseKind;
        [DataMember] public float? NoiseFader;     //     null = 0,6
        [DataMember] public bool NoisePlaying;     //     false = stumm (nichts spielt von selbst)
        // ---- 3.5 Bedienung
        [DataMember] public bool TypeValueOn;      // 140 Wert eintippen
        [DataMember] public bool MultiFaderOn;     // 141 Mehrere Fader zusammen
        [DataMember] public bool CopyPasteOn;      // 142 Kanal-Einstellungen kopieren/einfuegen
        [DataMember] public bool MiddleMuteOn;     // 143 Mittelklick = stumm
        [DataMember] public bool CheatSheetOn;     // 144 Spickzettel (F1)
        [DataMember] public bool GamepadOn;        // 145 Xbox-Controller
        [DataMember] public bool StartSceneOn;     // 146 Start-Szene
        [DataMember] public string StartScene;
        [DataMember] public bool SceneXfadeOwnOn;  // 147 Ueberblendzeit pro Szene
        [DataMember] public bool VolCapOn;         // 148 Lautstaerke-Obergrenze mit Passwort
        [DataMember] public int VolCapPct;         //     0 = 60 %
        [DataMember] public string VolCapHash;     //     Passwort (gesalzen, SHA-256), null = keins
        [DataMember] public string VolCapSalt;
        [DataMember] public bool FgHotkeysOn;      // 160 Hotkeys fuer die App im Vordergrund
        [DataMember] public bool DigitKeysOn;      // 161 Zahlentasten 1–9
        [DataMember] public bool PinOn;            // 162 Kanal anpinnen
        [DataMember] public bool WheelInvert;      // 163 Mausrad-Richtung umkehren
        [DataMember] public bool TemplatesOn;      // 164 Kanal-Vorlagen
        [DataMember] public List<ChannelTemplate> Templates;
        [DataMember] public bool ScenePreviewOn;   // 165 Szenen-Vorschau

        public static readonly int[] LpValues = { 0, 12000, 8000, 5000 };
        public static readonly int[] BassMonoValues = { 0, 80, 120, 160 };
        public static readonly int[] MirrorDelayValues = { 0, 20, 50, 100, 150, 200, 300 };
        public static readonly int[] VolCapValues = { 30, 40, 50, 60, 70, 80, 90 };
        public static readonly int[] SceneXfadeOwnValues = { -1, 0, 1, 2, 3, 5, 10, 30 };
        public static readonly string[] SceneXfadeOwnNames = { "Wie eingestellt", "Sofort", "1 s", "2 s", "3 s", "5 s", "10 s", "30 s" };
        public float NoiseFaderNow => NoiseFader ?? 0.6f;
        public int VolCapNow => Array.IndexOf(VolCapValues, VolCapPct) >= 0 ? VolCapPct : 60;

        private void Normalize350()
        {
            if (Array.IndexOf(MirrorDelayValues, MirrorDelayMs) < 0) MirrorDelayMs = 0;
            NoiseKind = Math.Max(0, Math.Min(3, NoiseKind));
            if (NoiseFader.HasValue) NoiseFader = float.IsNaN(NoiseFader.Value) ? 0.6f : Math.Max(0f, Math.Min(1f, NoiseFader.Value));
            if (VolCapPct != 0 && Array.IndexOf(VolCapValues, VolCapPct) < 0) VolCapPct = 0;
            HearSec = Math.Max(0, Math.Min(86400, HearSec));
            HearLoudSec = Math.Max(0, Math.Min(HearSec, HearLoudSec));
            if (Templates != null)
            {
                Templates.RemoveAll(t => t == null || t.Config == null || string.IsNullOrWhiteSpace(t.Name));
                if (Templates.Count > 30) Templates.RemoveRange(30, Templates.Count - 30);
                if (Templates.Count == 0) Templates = null;
            }
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    if (c.LpHz != 0 && Array.IndexOf(LpValues, c.LpHz) < 0) c.LpHz = 0;
                }
            if (OutputFx != null)
                foreach (var o in OutputFx)
                    if (o != null && o.BassMonoHz != 0 && Array.IndexOf(BassMonoValues, o.BassMonoHz) < 0) o.BassMonoHz = 0;
            if (Scenes != null)
                foreach (var sc in Scenes)
                    if (sc != null) sc.XfadeIdx = Math.Max(0, Math.Min(SceneXfadeOwnValues.Length - 1, sc.XfadeIdx));
        }
    }
}
