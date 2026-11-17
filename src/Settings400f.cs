using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>4.0 (finale): eigene Kompressor-Werte eines Kanals.</summary>
    [DataContract]
    internal sealed class CompConfig
    {
        [DataMember] public float Thr, Ratio, Att, Rel, Makeup, Knee;
        public static CompConfig Defaults() => new CompConfig { Thr = -24f, Ratio = 4f, Att = 5f, Rel = 200f, Makeup = 8f, Knee = 6f };
        public CompConfig Clone() => (CompConfig)MemberwiseClone();
        public void Clamp()
        {
            Thr = Fix(Thr, -60f, 0f, -24f); Ratio = Fix(Ratio, 1f, 20f, 4f); Att = Fix(Att, 0.1f, 200f, 5f);
            Rel = Fix(Rel, 10f, 3000f, 200f); Makeup = Fix(Makeup, 0f, 24f, 8f); Knee = Fix(Knee, 0f, 18f, 6f);
        }
        private static float Fix(float v, float lo, float hi, float d) => float.IsNaN(v) || float.IsInfinity(v) || (v == 0f && lo > 0f) ? d : Math.Max(lo, Math.Min(hi, v));
    }

    /// <summary>4.0 (finale): eine Ducking-Regel (Ausloeser -> Ziele).</summary>
    [DataContract]
    internal sealed class DuckRule
    {
        [DataMember] public string Trigger;          // Kanal-ID
        [DataMember] public List<string> Targets;    // Kanal-IDs
        [DataMember] public int Db;                  // Absenkung (0 = −12)
        [DataMember] public int Thr;                 // Ausloese-Pegel dBFS (0 = −40)
        [DataMember] public bool Freq;               // nur den Sprachbereich absenken
        [DataMember] public bool Off;
        public int DbNow => Db < 0 ? Db : -12;
        public int ThrNow => Thr < 0 ? Thr : -40;
    }

    /// <summary>4.0 (finale): Mute-Gruppe (eine Taste schaltet mehrere Kanaele stumm).</summary>
    [DataContract]
    internal sealed class MuteGroupConfig
    {
        [DataMember] public string Name;             // hoechstens 6 Zeichen auf der Taste
        [DataMember] public List<string> Members;
        [DataMember] public bool On;                 // gerade stumm
    }

    internal sealed partial class ChannelConfig
    {
        [DataMember] public CompConfig Comp400;      // 231 eigene Kompressor-Werte (null = feste Werte)
        [DataMember] public int DeEss;               // 235 0 aus, 1 leicht, 2 mittel, 3 stark
        [DataMember] public int SatType;             // 236 0 aus, 1 Roehre, 2 Band, 3 Konsole
        [DataMember] public int SatAmt;              //     0 mittel, 1 leicht, 2 stark
        [DataMember] public int GateDb;              // 237 0 aus, sonst Schwelle (dBFS)
        [DataMember] public int HumHz;               // 261 0 aus, 50, 60
        [DataMember] public bool Invert;             // 240 Polaritaet gedreht
        [DataMember] public int Punch;               // 243 −2..2
        [DataMember] public int HallSend;            // 242 Prozent 0..100
        [DataMember] public string FxOrder;          // 239 null = Standard
        [DataMember] public bool AutoMix;            // 241 im Auto-Mix (Sprache)
    }

    internal sealed partial class MixerSettings
    {
        // ---- 4.0 finale: Ton-Technik
        [DataMember] public bool ProCompOn;          // 231 Kompressor-Fenster
        [DataMember] public bool DuckFreqOn;         // 232 Frequenz-Ducking
        [DataMember] public bool DuckRulesOn;        // 233 mehrere Ducking-Regeln
        [DataMember] public List<DuckRule> DuckRules;
        [DataMember] public bool DynEqOn;            // 234 dynamischer EQ
        [DataMember] public bool DeEssOn;            // 235 De-Esser
        [DataMember] public bool WarmOn;             // 236 analoge Waerme
        [DataMember] public bool NoiseGateOn;        // 237 Rauschsperre
        [DataMember] public bool MsEqOn;             // 238 Mitte/Seite-EQ
        [DataMember] public bool FxOrderOn;          // 239 Effekt-Reihenfolge
        [DataMember] public bool InvertOn;           // 240 Polaritaet drehen
        [DataMember] public bool AutoMixOn;          // 241 Auto-Mix
        [DataMember] public bool HallBusOn;          // 242 Hall-Bus
        [DataMember] public int HallPreset;          //     0 Halle, 1 kleiner Raum, 2 Kathedrale
        [DataMember] public int HallReturnDb;        //     Rueckweg (dB, 0 = 0 dB)
        [DataMember] public bool PunchOn;            // 243 Transienten-Former
        // ---- 4.0 finale: Mischen
        [DataMember] public bool CrossOn;            // 257 Crossfader
        [DataMember] public string CrossA, CrossB;   //     Kanal-IDs
        [DataMember] public float? CrossPos;         //     0 = ganz A, 1 = ganz B (fehlt = Mitte)
        [DataMember] public int CrossCurve;          //     0 gleiche Leistung, 1 DJ (scharf), 2 linear
        [DataMember] public int? CrossX, CrossY;
        [DataMember] public bool MuteGroupsOn;       // 258 Mute-Gruppen
        [DataMember] public List<MuteGroupConfig> MuteGroups;
        [DataMember] public bool StreamLayerOn;      // 259 Fader-Ebene Stream
        [DataMember] public bool StreamLimitOn;      // 260 Stream-Limiter
        [DataMember] public bool HumOn;              // 261 Brumm-Entferner
        [DataMember] public bool GenOn;              // 262 Signalgenerator
        [DataMember] public int GenKind;             //     0 Sinus, 1 Gleitton, 2 rosa, 3 weiss
        [DataMember] public float GenHz;             //     0 = 1000
        [DataMember] public int GenLevel;            //     dBFS (0 = −24)
        [DataMember] public string GenDevice;        //     null = Standardgeraet
        [DataMember] public int GenSide;             //     0 beide, 1 links, 2 rechts

        // ---- 4.0 finale: Optik
        [DataMember] public bool LightShowOn;        // 222 Lichtshow
        [DataMember] public bool NeonOn;             // 224 Neon-Schild
        [DataMember] public string NeonText;         //     null = "Mischpult"
        [DataMember] public int NeonColor;           //     0 Pink, 1 Blau, 2 Gruen, 3 Rot, 4 Warmweiss, 5 Lila, 6 Orange
        [DataMember] public bool NeonPulse;          //     pulsiert zur Musik
        [DataMember] public bool SkyOn;              // 225 Himmel nach Tageszeit
        [DataMember] public bool DesignFadeOn;       // 227 weich umblenden
        [DataMember] public bool SongProgressOn;     // 228 Song-Fortschritt
        [DataMember] public bool ImageWashOn;        // 229 Kanal-Bild ueber den ganzen Zug
        [DataMember] public bool WallpaperOn;        // 230 Musik-Wallpaper
        [DataMember] public int WallpaperStyle;      //     0 Balken, 1 Skyline
        [DataMember] public bool AnimDecoOn;         // 250 animierte Deko
        [DataMember] public bool BgGifOn;            // 251 bewegtes Hintergrundbild
        [DataMember] public bool MotorPotsOn;        // 252 Motor-Potis
        [DataMember] public bool FaderTrailOn;       // 253 Leuchtspur
        // ---- 4.0 finale: Fenster & Bedienung
        [DataMember] public bool SceneGalleryOn;     // 256 Szenen-Galerie
        [DataMember] public bool EqOverviewOn;       // 244 EQ-Uebersicht
        [DataMember] public bool MaskingOn;          // 245 Maskierungs-Anzeige
        [DataMember] public bool BugReportOn;        // 246 Fehlerbericht-Knopf
        [DataMember] public bool EggOff;             // 247 versteckte Ueberraschung aus (Standard: an)
        [DataMember] public bool CmdSearchOn;        // 263 Befehlssuche (Strg+K)

        public static readonly string[] NeonColorNames = { "Pink", "Blau", "Grün", "Rot", "Warmweiß", "Lila", "Orange" };
        public static readonly int[] NeonColorValues = { 0xff3c9a, 0x3cb4ff, 0x3cff8c, 0xff3c3c, 0xffd8a0, 0xb45cff, 0xff8c28 };
        public static readonly string[] WallpaperStyleNames = { "Leuchtende Balken", "Skyline" };
        public string NeonTextNow => string.IsNullOrWhiteSpace(NeonText) ? "Mischpult" : NeonText.Trim();

        public static readonly string[] HallPresetNames = { "Halle", "Kleiner Raum", "Kathedrale" };
        public static readonly int[] HallReturnValues = { -12, -9, -6, -3, 0, 3 };
        public static readonly string[] CrossCurveNames = { "Gleiche Leistung (weich)", "DJ (scharf, Mitte voll)", "Linear" };
        public static readonly int[] GateValues = { -70, -60, -55, -50, -45, -40, -35, -30 };
        public static readonly int[] HallSendValues = { 0, 5, 10, 15, 25, 35, 50, 75, 100 };
        public static readonly string[] SatAmtNames = { "Mittel", "Leicht", "Stark" };
        public static readonly string[] OrderPresets = { "EKWF", "KEWF", "EWKF", "WEKF", "FEKW", "KWEF" };
        public static readonly int[] DuckRuleDbValues = { -3, -6, -9, -12, -15, -20, -25, -30 };
        public static readonly int[] DuckRuleThrValues = { -60, -50, -45, -40, -35, -30, -25, -20 };

        public float CrossPosNow => CrossPos.HasValue && !float.IsNaN(CrossPos.Value) ? Math.Max(0f, Math.Min(1f, CrossPos.Value)) : 0.5f;
        public float GenHzNow => GenHz >= 20f && GenHz <= 20000f ? GenHz : 1000f;
        public int GenLevelNow => GenLevel < 0 ? Math.Max(-80, Math.Min(-6, GenLevel)) : -24;
        public int HallPresetNow => HallPreset == 1 ? 0 : HallPreset == 2 ? 2 : 1;   // Reverb: 0 klein, 1 Halle, 2 Kathedrale
        public static float SatAmtOf(int i) => i == 1 ? 0.25f : i == 2 ? 0.85f : 0.5f;

        private void Normalize400f()
        {
            HallPreset = Math.Max(0, Math.Min(2, HallPreset));
            NeonColor = Math.Max(0, Math.Min(NeonColorValues.Length - 1, NeonColor));
            if (NeonText != null) { NeonText = NeonText.Replace("\r", " ").Replace("\n", " ").Trim(); if (NeonText.Length > 24) NeonText = NeonText.Substring(0, 24); if (NeonText.Length == 0) NeonText = null; }
            WallpaperStyle = Math.Max(0, Math.Min(1, WallpaperStyle));
            HallReturnDb = Math.Max(-30, Math.Min(6, HallReturnDb));
            CrossCurve = Math.Max(0, Math.Min(2, CrossCurve));
            if (CrossPos.HasValue && (float.IsNaN(CrossPos.Value) || float.IsInfinity(CrossPos.Value))) CrossPos = null;
            GenKind = Math.Max(0, Math.Min(3, GenKind));
            GenSide = Math.Max(0, Math.Min(2, GenSide));
            if (float.IsNaN(GenHz) || GenHz < 0f) GenHz = 0f;
            GenLevel = Math.Max(-80, Math.Min(0, GenLevel));
            if (DuckRules != null)
            {
                DuckRules.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Trigger));
                foreach (var r in DuckRules)
                {
                    r.Targets = r.Targets?.Where(t => !string.IsNullOrEmpty(t) && t != r.Trigger).Distinct().ToList();
                    r.Db = Math.Max(-40, Math.Min(0, r.Db));
                    r.Thr = Math.Max(-80, Math.Min(0, r.Thr));
                }
                if (DuckRules.Count > 12) DuckRules.RemoveRange(12, DuckRules.Count - 12);
                if (DuckRules.Count == 0) DuckRules = null;
            }
            if (MuteGroups != null)
            {
                MuteGroups.RemoveAll(g => g == null);
                foreach (var g in MuteGroups)
                {
                    g.Name = string.IsNullOrWhiteSpace(g.Name) ? "MUTE" : g.Name.Trim();
                    if (g.Name.Length > 12) g.Name = g.Name.Substring(0, 12);
                    g.Members = g.Members?.Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList() ?? new List<string>();
                }
                if (MuteGroups.Count > 4) MuteGroups.RemoveRange(4, MuteGroups.Count - 4);
                if (MuteGroups.Count == 0) MuteGroups = null;
            }
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    c.Comp400?.Clamp();
                    c.DeEss = Math.Max(0, Math.Min(3, c.DeEss));
                    c.SatType = Math.Max(0, Math.Min(3, c.SatType));
                    c.SatAmt = Math.Max(0, Math.Min(2, c.SatAmt));
                    c.GateDb = Math.Max(-90, Math.Min(0, c.GateDb));
                    if (c.HumHz != 50 && c.HumHz != 60) c.HumHz = 0;
                    c.Punch = Math.Max(-2, Math.Min(2, c.Punch));
                    c.HallSend = Math.Max(0, Math.Min(100, c.HallSend));
                    c.FxOrder = Audio.ChannelPro.CleanOrder(c.FxOrder);
                }
        }
    }
}
