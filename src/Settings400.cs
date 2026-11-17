using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>4.0: ein Band des parametrischen EQ (auch Kopfhoerer-Profile aus AutoEq).</summary>
    [DataContract]
    internal sealed class PeqBand
    {
        public const int Bell = 0, LowShelf = 1, HighShelf = 2, HighPass = 3, LowPass = 4;
        public static readonly string[] TypeNames = { "Glocke", "Bass-Kuhschwanz", "Höhen-Kuhschwanz", "Hochpass (Bass weg)", "Tiefpass (Höhen weg)" };
        [DataMember] public int Type;
        [DataMember] public float Hz;
        [DataMember] public float Db;
        [DataMember] public float Q;
        [DataMember] public bool Off;
        [DataMember] public int Mode;         // 4.0 (finale): 0 beide, 1 nur Mitte, 2 nur Seite
        [DataMember] public bool Dyn;         // 4.0 (finale): dynamisch – greift nur, wenn das Band zu laut ist
        [DataMember] public float DynThr;     //               Schwelle dBFS (0 = −30)
        public static readonly string[] ModeNames = { "Beide (normal)", "Nur Mitte", "Nur Seite" };
        public static readonly int[] DynThrValues = { -50, -40, -35, -30, -25, -20, -15, -10 };
        public float DynThrNow => DynThr < -0.5f ? DynThr : -30f;
        public bool IsDyn => Dyn && Type == Bell;

        public PeqBand Clone() => (PeqBand)MemberwiseClone();
        public bool SameAs(PeqBand o) => o != null && o.Type == Type && o.Hz == Hz && o.Db == Db && o.Q == Q && o.Off == Off && o.Mode == Mode && o.Dyn == Dyn && o.DynThr == DynThr;
        public float QNow => Q > 0.05f ? Q : 0.71f;

        public void Clamp()
        {
            Type = Math.Max(0, Math.Min(4, Type));
            Hz = float.IsNaN(Hz) ? 1000f : Math.Max(20f, Math.Min(20000f, Hz));
            Db = float.IsNaN(Db) ? 0f : Math.Max(-24f, Math.Min(24f, Db));
            Q = float.IsNaN(Q) || Q <= 0f ? 0.71f : Math.Max(0.1f, Math.Min(12f, Q));
            Mode = Math.Max(0, Math.Min(2, Mode));
            DynThr = float.IsNaN(DynThr) || DynThr > 0f ? 0f : Math.Max(-70f, DynThr);
        }

        internal static List<PeqBand> CloneList(List<PeqBand> l) => l?.Where(b => b != null).Select(b => b.Clone()).ToList();
        internal static bool SameList(List<PeqBand> a, List<PeqBand> b)
        {
            if (a == null || b == null) return (a?.Count ?? 0) == 0 && (b?.Count ?? 0) == 0;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].SameAs(b[i])) return false;
            return true;
        }
        internal static void ClampList(ref List<PeqBand> l, int max = 12)
        {
            if (l == null) return;
            l.RemoveAll(b => b == null);
            foreach (var b in l) b.Clamp();
            if (l.Count > max) l.RemoveRange(max, l.Count - max);
            if (l.Count == 0) l = null;
        }
    }

    /// <summary>4.0: Ergebnis des Hoertests (Hoerschwelle je Frequenz und Ohr, dB relativ).</summary>
    [DataContract]
    internal sealed class HearProfile
    {
        public static readonly int[] Freqs = { 125, 250, 500, 1000, 2000, 3000, 4000, 6000, 8000, 12000 };
        [DataMember] public float[] Left, Right;     // Schwelle in dBFS je Frequenz (NaN/0 = nicht gemessen)
        [DataMember] public string DeviceId, DeviceName;
        [DataMember] public string Date;             // yyyy-MM-dd
        [DataMember] public int Strength;            // 0 = halb (Standard), 1 = ein Drittel, 2 = zwei Drittel

        public bool Valid => Left != null && Right != null && Left.Length == Freqs.Length && Right.Length == Freqs.Length;
    }

    /// <summary>4.0: eine aufgenommene Fader-Bewegung (Automation).</summary>
    [DataContract]
    internal sealed class AutoTake
    {
        [DataMember] public string Id, Name;
        [DataMember] public List<AutoEvent> Events;
        [DataMember] public float Seconds;
        [DataMember] public bool Loop;
    }

    [DataContract]
    internal sealed class AutoEvent
    {
        [DataMember] public float T;          // Sekunden ab Start
        [DataMember] public string Ch;        // Kanal-ID, "master" bzw. "grp:<id>"
        [DataMember] public float Pos;        // Faderposition 0..1 (NaN = unveraendert)
        [DataMember] public int Mute;         // 0 unveraendert, 1 stumm, 2 laut
    }

    /// <summary>4.0: eigene Taste in der Kopfleiste.</summary>
    [DataContract]
    internal sealed class TopKeyConfig
    {
        [DataMember] public string Label;     // hoechstens 6 Zeichen
        [DataMember] public string Action;    // Aktion wie bei den Tastenkuerzeln (oder "run:<Pfad>")
    }

    /// <summary>4.0: offenes Kanal-Fenster.</summary>
    [DataContract]
    internal sealed class ChanWinConfig
    {
        [DataMember] public string Id;        // Kanal-ID
        [DataMember] public int X, Y;
    }

    /// <summary>4.0: gesammelte Zahlen fuer das Statistik-Fenster.</summary>
    [DataContract]
    internal sealed class StatsData
    {
        [DataMember] public string Since;                       // yyyy-MM-dd
        [DataMember] public Dictionary<string, int> SceneUse;   // Szenen-Name -> wie oft geladen
        [DataMember] public Dictionary<string, int> ChannelSec; // Kanal-Name -> Sekunden hoerbar (gesamt)
        [DataMember] public Dictionary<string, int> DaySec;     // yyyy-MM-dd -> Sekunden mit Ton
        [DataMember] public double VolSum; [DataMember] public int VolCount;   // Mittelwert der Ausgangs-Lautstaerke
        [DataMember] public float MaxLufs; [DataMember] public string MaxLufsWhen;
        [DataMember] public int Starts;
    }

    internal sealed partial class ChannelConfig
    {
        [DataMember] public List<PeqBand> Peq;    // 4.0: parametrischer EQ (null = keiner)
        [DataMember] public int TimeLimitMin;     // 4.0: Zeit-Limit pro Tag (Minuten, 0 = keins)
        [DataMember] public bool PeqBypass;       // 4.0: parametrischer EQ gerade aus (A/B)
    }

    internal sealed partial class OutputFxConfig
    {
        [DataMember] public List<PeqBand> Peq;        // 4.0: parametrischer EQ des Ausgangs
        [DataMember] public string HpName;            // 4.0: Kopfhoerer-Profil (AutoEq): Name
        [DataMember] public List<PeqBand> HpBands;    //      Filter
        [DataMember] public float HpPreamp;           //      Vorverstaerkung (dB, meist negativ)
        [DataMember] public bool HpOff;               //      Profil gerade aus (A/B)
        [DataMember] public bool PeqBypass;           // 4.0: parametrischer EQ gerade aus (A/B)
    }

    internal sealed partial class GroupConfig
    {
        [DataMember] public bool EqOn;                // 4.0: EQ der Gruppe
        [DataMember] public float EqLow, EqMid, EqHigh;
        [DataMember] public bool CompOn;              // 4.0: Kompressor der Gruppe
    }

    internal sealed partial class Scene
    {
        [DataMember] public List<string> Only;        // 4.0: Teil-Szene – nur diese Kanaele (null = alle)
        [DataMember] public bool OnlyNoMaster;        //      Ausgangs-Lautstaerke nicht mitladen
    }

    internal sealed partial class MixerSettings
    {
        // ---- 4.0 Fundament
        [DataMember] public string LastVersion;       // 189 zuletzt gestartete Version
        [DataMember] public bool WhatsNewOff;         // 189 true = "Was ist neu" nicht zeigen (Standard: zeigen)
        [DataMember] public bool SimpleSettings;      // 187 einfache Einstellungen
        [DataMember] public bool DocsRecMoved;        // 183 Aufnahme-Ordner einmal nach Dokumente umgestellt
        // ---- 4.0 Klang
        [DataMember] public bool PeqOn;               // 167 parametrischer EQ
        [DataMember] public bool HpEqOn;              // 171 Kopfhoerer-Profile (AutoEq)
        [DataMember] public bool GroupFxOn;           // 190 EQ/Kompressor fuer Gruppen
        [DataMember] public bool HearEqOn;            // 206 Hoer-EQ
        [DataMember] public HearProfile Hear;
        // ---- 4.0 Ton-Komfort
        [DataMember] public bool EveningOn;           // 179 Abendkurve
        [DataMember] public int EveningFrom, EveningTo, EveningRampMin, EveningPct;   // 0 = 21 Uhr / 7 Uhr / 60 min / 35 %
        [DataMember] public bool DevGuardOn;          // 205 neues Geraet: Lautstaerke begrenzen
        [DataMember] public int DevGuardPct;          //     0 = 30 %
        [DataMember] public bool DevHoldOn;           // 205 Standardgeraet festhalten
        [DataMember] public bool CoupleOn;            // 195 Ausgaenge koppeln
        [DataMember] public List<string> Couples;     //     "IdA|IdB"
        [DataMember] public bool PanicOn;             // 214 Panik-Taste
        [DataMember] public bool TimeLimitOn;         // 221 Zeit-Limit pro Kanal
        [DataMember] public string TimeDate;          //     Tag der Zaehlung
        [DataMember] public Dictionary<string, int> TimeUsed;   // Kanal-ID -> Sekunden heute
        [DataMember] public bool SurroundOn;          // 213 Surround-Pegel
        [DataMember] public bool EqShareOn;           // 220 EQ-Presets als Datei
        [DataMember] public int SleepEnd;             // 215 0 nichts, 1 Bildschirm aus, 2 Energiesparen, 3 Herunterfahren
        // ---- 4.0 Optik
        [DataMember] public bool FollowWinTheme;      // 177 Design folgt Windows (hell/dunkel)
        [DataMember] public int? WinLightStyle, WinDarkStyle;
        [DataMember] public bool RadarOn;             // 178 Lautheits-Radar
        [DataMember] public bool KnobViewOn;          // 193 Drehknoepfe statt Fader
        [DataMember] public bool NixieBarOn;          // 200 Nixie-Balken
        [DataMember] public bool GifOn;               // 201 bewegte GIF-Bilder
        [DataMember] public bool MotorSoundOn;        // 202 Motor-Surren
        [DataMember] public int MotorSoundLevel;      //     0 leise, 1 mittel, 2 laut
        [DataMember] public bool CoverBgOn;           // 210 Cover als Hintergrund
        [DataMember] public bool MoreDecoOn;          // 217 mehr Saison-Deko
        [DataMember] public int BirthDay, BirthMonth; //     Geburtstag (0 = keiner)
        [DataMember] public int ClickKind;            // 218 0 Standard, 1 Studio-Taste, 2 Schreibmaschine, 3 Retro-Schalter
        [DataMember] public bool MusicBgOn;           // 192 Hintergrund tanzt zur Musik
        // ---- 4.0 Aufbau
        [DataMember] public bool StripCustomOn;       // 174 Kanalzug anpassen
        [DataMember] public List<string> StripHide;   //     ausgeblendete Teile
        [DataMember] public bool TwoRowsOn;           // 175 zwei Reihen
        [DataMember] public bool A11yReaderOn;        // 182 Bildschirmleser
        [DataMember] public bool A11yFocusOn;         //     deutlicher Fokus-Rahmen
        [DataMember] public bool A11yContrastOn;      //     kraeftige Schrift
        [DataMember] public bool A11yCalmOn;          //     weniger Bewegung
        // ---- 4.0 Bedienung
        [DataMember] public bool AutomationOn;        // 172 Fader-Automation
        [DataMember] public List<AutoTake> Takes;
        [DataMember] public bool WinWheelOn;          // 173 Mausrad ueber einem Fenster
        [DataMember] public int WinWheelMod;          //     0 Alt, 1 Strg+Alt, 2 Umschalt+Alt, 3 Strg+Umschalt
        [DataMember] public bool HotCornerOn;         // 219 heisse Ecke
        [DataMember] public int HotCorner;            //     0 unten rechts, 1 unten links, 2 oben rechts, 3 oben links
        [DataMember] public string HotCornerTarget;   //     Kanal-ID (null = MASTER)
        [DataMember] public bool AppDragOn;           // 184 App zwischen Kanaelen ziehen
        [DataMember] public bool ChanWinOn;           // 185 Kanal-Fenster
        [DataMember] public List<ChanWinConfig> ChanWins;
        [DataMember] public bool TopKeysOn;           // 186 eigene Tasten in der Kopfleiste
        [DataMember] public List<TopKeyConfig> TopKeys;
        [DataMember] public bool SearchOn;            // 194 Kanal-Suche
        [DataMember] public bool PartialScenesOn;     // 196 Teil-Szenen
        // ---- 4.0 Fenster
        [DataMember] public bool OverlayOn;           // 169 Spiel-Overlay
        [DataMember] public int OverlayPos;           //     0 oben links, 1 oben rechts, 2 unten links, 3 unten rechts
        [DataMember] public bool WidgetOn;            // 176 Desktop-Widget
        [DataMember] public int? WidgetX, WidgetY;
        [DataMember] public bool RoomOn;              // 191 Raum-Ansicht
        [DataMember] public bool TimelineOn;          // 203 Ton-Zeitleiste
        [DataMember] public bool StatsOn;             // 204 Statistik
        [DataMember] public StatsData Stats;
        [DataMember] public bool PlayerOn;            // 209 Mini-Player
        [DataMember] public int? PlayerX, PlayerY;
        [DataMember] public bool PlayerNotTop;        //     true = nicht immer im Vordergrund
        [DataMember] public bool DoctorOn;            // 180 Ton-Doktor
        [DataMember] public bool FocusOn;             // 199 Fokus-Timer
        [DataMember] public int FocusMin, BreakMin;   //     0 = 25 / 5
        [DataMember] public int FocusMode;            //     0 stumm, 1 leiser (-20 dB)
        [DataMember] public List<string> FocusChannels;
        [DataMember] public bool FocusGongOff;
        [DataMember] public bool RecPlanOn;           // 197 geplante Aufnahme
        [DataMember] public int RecPlanAt;            //     Minute des Tages + 1 (0 = nichts geplant)
        [DataMember] public int RecPlanDur;           //     Minuten (0 = 60)
        [DataMember] public bool RecPlanDaily;        //     jeden Tag
        // ---- 4.0 Musik & Extern
        [DataMember] public bool MediaMoreOn;         // 208 mehr Musik-Tasten
        [DataMember] public bool ArtistVolOn;         // 211 Lautstaerke pro Interpret
        [DataMember] public Dictionary<string, float> ArtistVols;
        [DataMember] public bool ChanTitleOn;         // 212 was laeuft pro Kanal
        [DataMember] public bool MidiFeedbackOn;      // 170 MIDI-Rueckmeldung
        [DataMember] public string MidiOutDevice;     //     null = gleicher Name wie der Eingang
        [DataMember] public bool ObsOn;               // 198 OBS
        [DataMember] public string ObsHost;           //     null = 127.0.0.1
        [DataMember] public int ObsPort;              //     0 = 4455
        [DataMember] public string ObsPass;           //     geschuetzt (Windows-Schluessel des Benutzers)
        [DataMember] public bool ObsSceneLink;        //     Szenen mit OBS verbinden
        [DataMember] public bool ObsOnAir;            //     ON-AIR-Lampe folgt OBS
        [DataMember] public bool WledOn;              // 207 WLED
        [DataMember] public string WledHost;
        [DataMember] public int WledLeds;             //     0 = 30
        [DataMember] public int WledMode;             //     0 Pegel, 1 Spektrum, 2 Takt, 3 Akzentfarbe, 4 lautester Kanal
        [DataMember] public int? WledBright;          //     10..100 (fehlt = 70)

        public static readonly int[] EveningPctValues = { 15, 20, 25, 30, 35, 40, 50, 60 };
        public static readonly int[] EveningRampValues = { 15, 30, 60, 90, 120, 180 };
        public static readonly int[] DevGuardValues = { 15, 20, 25, 30, 40, 50 };
        public static readonly int[] TimeLimitValues = { 0, 15, 30, 45, 60, 90, 120, 180, 240 };
        public static readonly string[] SleepEndNames = { "Nichts weiter", "Bildschirm aus", "PC in den Energiesparmodus", "PC herunterfahren" };
        public static readonly string[] ClickKindNames = { "Standard", "Studio-Taste", "Schreibmaschine", "Retro-Schalter" };
        public static readonly string[] WinWheelModNames = { "Alt", "Strg + Alt", "Umschalt + Alt", "Strg + Umschalt" };
        public static readonly string[] CornerNames = { "Unten rechts", "Unten links", "Oben rechts", "Oben links" };
        public static readonly string[] OverlayPosNames = { "Oben links", "Oben rechts", "Unten links", "Unten rechts" };
        public static readonly int[] FocusValues = { 15, 20, 25, 30, 45, 50, 60, 90 };
        public static readonly int[] BreakValues = { 3, 5, 10, 15, 20 };
        public static readonly int[] RecPlanDurValues = { 5, 10, 15, 30, 45, 60, 90, 120, 180, 240 };
        public static readonly int[] WledLedValues = { 10, 20, 30, 50, 60, 100, 144, 150, 200, 300 };
        public static readonly string[] WledModeNames = { "Pegel", "Spektrum", "Takt (blinkt)", "Akzentfarbe", "Farbe des lautesten Kanals" };
        public static readonly string[] StripPartNames = { "pan", "eq", "keys", "field", "scale", "fx", "leds", "readout" };
        public static readonly string[] StripPartLabels = { "Panorama-Knopf (L/R)", "EQ-Knöpfe", "Tasten (MUTE, SOLO …)", "Anzeigefeld (Apps, Lautheit …)", "dB-Skala am Fader", "FX- und CUE-Schildchen", "SIG-/PEAK-Leuchten", "Wert unter dem Fader" };

        public int EveningFromNow => EveningFrom == 0 && EveningTo == 0 ? 21 : EveningFrom;
        public int EveningToNow => EveningFrom == 0 && EveningTo == 0 ? 7 : EveningTo;
        public int EveningRampNow => Array.IndexOf(EveningRampValues, EveningRampMin) >= 0 ? EveningRampMin : 60;
        public int EveningPctNow => Array.IndexOf(EveningPctValues, EveningPct) >= 0 ? EveningPct : 35;
        public int DevGuardNow => Array.IndexOf(DevGuardValues, DevGuardPct) >= 0 ? DevGuardPct : 30;
        public int FocusMinNow => Array.IndexOf(FocusValues, FocusMin) >= 0 ? FocusMin : 25;
        public int BreakMinNow => Array.IndexOf(BreakValues, BreakMin) >= 0 ? BreakMin : 5;
        public int RecPlanDurNow => Array.IndexOf(RecPlanDurValues, RecPlanDur) >= 0 ? RecPlanDur : 60;
        public int WledLedsNow => WledLeds >= 1 && WledLeds <= 490 ? WledLeds : 30;
        public int ObsPortNow => ObsPort > 0 && ObsPort < 65536 ? ObsPort : 4455;
        public bool StripHidden(string part) => StripCustomOn && StripHide != null && StripHide.Contains(part);

        private void Normalize400()
        {
            if (LastVersion != null && LastVersion.Length > 20) LastVersion = null;
            if (Hear != null && !Hear.Valid) Hear = null;
            if (Hear != null)
            {
                Hear.Strength = Math.Max(0, Math.Min(2, Hear.Strength));
                for (int i = 0; i < Hear.Left.Length; i++)
                {
                    if (float.IsNaN(Hear.Left[i]) || float.IsInfinity(Hear.Left[i])) Hear.Left[i] = 0f;
                    if (float.IsNaN(Hear.Right[i]) || float.IsInfinity(Hear.Right[i])) Hear.Right[i] = 0f;
                    Hear.Left[i] = Math.Max(-100f, Math.Min(0f, Hear.Left[i]));
                    Hear.Right[i] = Math.Max(-100f, Math.Min(0f, Hear.Right[i]));
                }
            }
            EveningFrom = Math.Max(0, Math.Min(23, EveningFrom));
            EveningTo = Math.Max(0, Math.Min(23, EveningTo));
            if (EveningRampMin != 0 && Array.IndexOf(EveningRampValues, EveningRampMin) < 0) EveningRampMin = 0;
            if (EveningPct != 0 && Array.IndexOf(EveningPctValues, EveningPct) < 0) EveningPct = 0;
            if (DevGuardPct != 0 && Array.IndexOf(DevGuardValues, DevGuardPct) < 0) DevGuardPct = 0;
            if (Couples != null)
            {
                Couples = Couples.Where(c => !string.IsNullOrEmpty(c) && c.Split('|').Length == 2 && c.Split('|').All(x => x.Length > 0)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
                if (Couples.Count == 0) Couples = null;
            }
            if (TimeUsed != null)
            {
                foreach (var k in TimeUsed.Keys.ToList()) if (k == null || TimeUsed[k] < 0) TimeUsed.Remove(k); else TimeUsed[k] = Math.Min(86400, TimeUsed[k]);
                if (TimeUsed.Count == 0) TimeUsed = null;
            }
            SleepEnd = Math.Max(0, Math.Min(3, SleepEnd));
            if (WinLightStyle.HasValue && (WinLightStyle < 0 || WinLightStyle > MaxStyle)) WinLightStyle = null;
            if (WinDarkStyle.HasValue && (WinDarkStyle < 0 || WinDarkStyle > MaxStyle)) WinDarkStyle = null;
            MotorSoundLevel = Math.Max(0, Math.Min(2, MotorSoundLevel));
            BirthMonth = Math.Max(0, Math.Min(12, BirthMonth));
            BirthDay = BirthMonth == 0 ? 0 : Math.Max(1, Math.Min(DateTime.DaysInMonth(2024, BirthMonth), BirthDay == 0 ? 1 : BirthDay));
            ClickKind = Math.Max(0, Math.Min(3, ClickKind));
            if (StripHide != null)
            {
                StripHide = StripHide.Where(p => Array.IndexOf(StripPartNames, p) >= 0).Distinct().ToList();
                if (StripHide.Count == 0) StripHide = null;
            }
            if (Takes != null)
            {
                Takes.RemoveAll(t => t == null || t.Events == null || t.Events.Count == 0);
                foreach (var t in Takes)
                {
                    if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                    if (string.IsNullOrWhiteSpace(t.Name)) t.Name = "Aufnahme";
                    t.Events.RemoveAll(e => e == null || string.IsNullOrEmpty(e.Ch) || float.IsNaN(e.T) || e.T < 0);
                    if (t.Events.Count > 20000) t.Events.RemoveRange(20000, t.Events.Count - 20000);
                    t.Events.Sort((a, b) => a.T.CompareTo(b.T));
                    t.Seconds = t.Events.Count == 0 ? 0f : Math.Max(t.Seconds, t.Events[t.Events.Count - 1].T);
                }
                Takes.RemoveAll(t => t.Events.Count == 0);
                if (Takes.Count > 20) Takes.RemoveRange(20, Takes.Count - 20);
                if (Takes.Count == 0) Takes = null;
            }
            WinWheelMod = Math.Max(0, Math.Min(3, WinWheelMod));
            HotCorner = Math.Max(0, Math.Min(3, HotCorner));
            if (ChanWins != null)
            {
                ChanWins = ChanWins.Where(w => w != null && !string.IsNullOrEmpty(w.Id) && Channels != null && Channels.Any(c => c != null && c.Id == w.Id))
                                   .GroupBy(w => w.Id).Select(g => g.Last()).Take(12).ToList();
                if (ChanWins.Count == 0) ChanWins = null;
            }
            if (TopKeys != null)
            {
                TopKeys.RemoveAll(k => k == null || string.IsNullOrWhiteSpace(k.Label) || string.IsNullOrEmpty(k.Action));
                foreach (var k in TopKeys) { k.Label = k.Label.Trim(); if (k.Label.Length > 6) k.Label = k.Label.Substring(0, 6); }
                if (TopKeys.Count > 6) TopKeys.RemoveRange(6, TopKeys.Count - 6);
                if (TopKeys.Count == 0) TopKeys = null;
            }
            OverlayPos = Math.Max(0, Math.Min(3, OverlayPos));
            FocusMode = Math.Max(0, Math.Min(1, FocusMode));
            if (FocusMin != 0 && Array.IndexOf(FocusValues, FocusMin) < 0) FocusMin = 0;
            if (BreakMin != 0 && Array.IndexOf(BreakValues, BreakMin) < 0) BreakMin = 0;
            if (FocusChannels != null)
            {
                FocusChannels = FocusChannels.Where(id => Channels != null && Channels.Any(c => c != null && c.Id == id)).Distinct().ToList();
                if (FocusChannels.Count == 0) FocusChannels = null;
            }
            RecPlanAt = Math.Max(0, Math.Min(1440, RecPlanAt));
            if (RecPlanDur != 0 && Array.IndexOf(RecPlanDurValues, RecPlanDur) < 0) RecPlanDur = 0;
            if (ArtistVols != null)
            {
                foreach (var k in ArtistVols.Keys.ToList())
                    if (string.IsNullOrWhiteSpace(k) || float.IsNaN(ArtistVols[k])) ArtistVols.Remove(k); else ArtistVols[k] = Math.Max(0f, Math.Min(1f, ArtistVols[k]));
                if (ArtistVols.Count > 300) ArtistVols = ArtistVols.Take(300).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (ArtistVols.Count == 0) ArtistVols = null;
            }
            if (string.IsNullOrWhiteSpace(MidiOutDevice)) MidiOutDevice = null;
            if (string.IsNullOrWhiteSpace(ObsHost)) ObsHost = null; else ObsHost = ObsHost.Trim();
            if (ObsPort < 0 || ObsPort > 65535) ObsPort = 0;
            if (string.IsNullOrWhiteSpace(WledHost)) WledHost = null; else WledHost = WledHost.Trim();
            if (WledLeds < 0 || WledLeds > 490) WledLeds = 0;
            WledMode = Math.Max(0, Math.Min(4, WledMode));
            if (WledBright.HasValue) WledBright = Math.Max(10, Math.Min(100, WledBright.Value));
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    PeqBand.ClampList(ref c.Peq, PeqMax);
                    if (Array.IndexOf(TimeLimitValues, c.TimeLimitMin) < 0) c.TimeLimitMin = 0;
                }
            if (OutputFx != null)
                foreach (var o in OutputFx)
                {
                    if (o == null) continue;
                    PeqBand.ClampList(ref o.Peq, PeqMax);
                    PeqBand.ClampList(ref o.HpBands, 20);
                    o.HpPreamp = float.IsNaN(o.HpPreamp) ? 0f : Math.Max(-30f, Math.Min(6f, o.HpPreamp));
                    if (o.HpBands == null) { o.HpName = null; o.HpPreamp = 0f; o.HpOff = false; }
                }
            if (Groups != null)
                foreach (var g in Groups)
                {
                    if (g == null) continue;
                    g.EqLow = ClampEq(g.EqLow); g.EqMid = ClampEq(g.EqMid); g.EqHigh = ClampEq(g.EqHigh);
                }
            if (Scenes != null)
                foreach (var sc in Scenes)
                {
                    if (sc?.Only == null) continue;
                    sc.Only = sc.Only.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                    if (sc.Only.Count == 0) sc.Only = null;
                }
            if (Stats != null)
            {
                if (Stats.DaySec != null && Stats.DaySec.Count > 400)
                    Stats.DaySec = Stats.DaySec.OrderByDescending(kv => kv.Key, StringComparer.Ordinal).Take(400).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (float.IsNaN(Stats.MaxLufs)) Stats.MaxLufs = 0f;
            }
        }

        /// <summary>Hoechste Design-Nummer (4.0: 9 Carbon, 10 Holz &amp; Messing).</summary>
        public const int MaxStyle = 17;   // 4.0 finale: bis Weltraum; 4.2: Synthwave, Retro-Game, Cockpit
        /// <summary>Baender im parametrischen EQ.</summary>
        public const int PeqMax = 8;

        // ================================================================ 183 / 181 / 168: Wo alles liegt

        /// <summary>Dokumente\Mischpult (mit Einstellungen, Sicherungen, Aufnahmen, Bilder, Protokolle).</summary>
        public static string DocsRoot
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("MISCHPULT_DOCS");   // nur fuer Tests
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mischpult");
            }
        }

        public const string LocationFileName = "speicherort.txt";
        /// <summary>Liegt hier "appdata" drin, bleibt alles unter %AppData% (Dokumente-Ordner aus).</summary>
        public static string LocationFile => Path.Combine(AppDataFolder, LocationFileName);

        private static int _docsMode = -1;
        /// <summary>183: alles im Ordner Dokumente\Mischpult (Standard ab 4.0; aus = wie bis 3.5 unter %AppData%).</summary>
        public static bool DocsMode
        {
            get
            {
                if (_docsMode < 0)
                {
                    bool on = true;
                    try
                    {
                        if (Portable) on = false;
                        else if (File.Exists(LocationFile) && File.ReadAllText(LocationFile).Trim().StartsWith("appdata", StringComparison.OrdinalIgnoreCase)) on = false;
                        else if (string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))) on = false;
                    }
                    catch { on = false; }
                    _docsMode = on ? 1 : 0;
                }
                return _docsMode == 1;
            }
        }

        /// <summary>Grund-Ordner der Einstellungen (ohne Cloud): portabel, Dokumente oder %AppData%.</summary>
        public static string BaseFolder => Portable ? PortableFolder : DocsMode ? Path.Combine(DocsRoot, "Einstellungen") : AppDataFolder;

        public const string CloudFileName = "cloud-ordner.txt";
        public static string CloudFile => Path.Combine(BaseFolder, CloudFileName);
        private static string _cloud;
        private static bool _cloudRead;
        /// <summary>181: Ordner in der Cloud (OneDrive, Dropbox …), in dem die Einstellungen liegen – null = keiner.</summary>
        public static string CloudFolder
        {
            get
            {
                if (!_cloudRead)
                {
                    _cloudRead = true;
                    try
                    {
                        if (File.Exists(CloudFile))
                        {
                            string p = File.ReadAllLines(CloudFile).FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
                            if (!string.IsNullOrEmpty(p) && Path.IsPathRooted(p)) _cloud = p;
                        }
                    }
                    catch { }
                }
                return _cloud;
            }
        }
        /// <summary>Ist der Cloud-Ordner gerade erreichbar? (sonst laeuft alles lokal weiter)</summary>
        public static bool CloudReachable
        {
            get
            {
                string c = CloudFolder;
                if (c == null) return false;
                try { Directory.CreateDirectory(c); return Directory.Exists(c); } catch { return false; }
            }
        }
        private static int _cloudOk = -1;
        public static bool CloudActive
        {
            get
            {
                if (_cloudOk < 0) _cloudOk = CloudFolder != null && CloudReachable ? 1 : 0;
                return _cloudOk == 1;
            }
        }

        /// <summary>Ordner mit settings.json (portabel / Dokumente / %AppData% / Cloud).</summary>
        public static string Folder => CloudActive ? CloudFolder : BaseFolder;

        /// <summary>Sicherungen: im Dokumente-Ordner eigener Unterordner, sonst unter den Einstellungen.</summary>
        public static string BackupFolder => DocsMode && !CloudActive ? Path.Combine(DocsRoot, "Sicherungen") : Path.Combine(Folder, "Sicherungen");
        /// <summary>Fehler- und Ton-Protokoll.</summary>
        public static string LogFolder => DocsMode ? Path.Combine(DocsRoot, "Protokolle") : Folder;
        /// <summary>Bilder vom Pult.</summary>
        public static string ShotFolderPath => DocsMode ? Path.Combine(DocsRoot, "Bilder") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Mischpult");
        public static string ShotFolderName => DocsMode ? "Dokumente\\Mischpult\\Bilder" : "Bilder\\Mischpult";
        /// <summary>Dokumente-Ordner gewuenscht (ohne Ruecksicht auf portabel): keine Datei "speicherort.txt" mit "appdata".</summary>
        public static bool DocsWanted
        {
            get
            {
                try { return !(File.Exists(LocationFile) && File.ReadAllText(LocationFile).Trim().StartsWith("appdata", StringComparison.OrdinalIgnoreCase)); }
                catch { return false; }
            }
        }
        /// <summary>Standard-Ordner fuer Aufnahmen.</summary>
        public static string DefaultRecordFolder => DocsMode ? Path.Combine(DocsRoot, "Aufnahmen") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Mischpult-Aufnahmen");
        public static string OldRecordFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Mischpult-Aufnahmen");

        /// <summary>
        /// 183: Beim ersten Start mit dem Dokumente-Ordner einmal alles aus %AppData%\Mischpult herueberholen
        /// (die alten Dateien bleiben als Sicherheit liegen). Liefert true, wenn etwas umgezogen ist.
        /// </summary>
        public static bool MigrateToDocs()
        {
            if (!DocsMode) return false;
            try
            {
                string target = Path.Combine(DocsRoot, "Einstellungen");
                string old = AppDataFolder;
                if (File.Exists(Path.Combine(target, "settings.json")) || !File.Exists(Path.Combine(old, "settings.json"))) return false;
                Directory.CreateDirectory(target);
                foreach (var f in Directory.GetFiles(old))
                {
                    string name = Path.GetFileName(f);
                    if (name.Equals(LocationFileName, StringComparison.OrdinalIgnoreCase) || name.StartsWith("HINWEIS", StringComparison.OrdinalIgnoreCase)) continue;
                    bool log = name.Equals("fehler.log", StringComparison.OrdinalIgnoreCase) || name.Equals("ton-protokoll.txt", StringComparison.OrdinalIgnoreCase);
                    string dir = log ? Path.Combine(DocsRoot, "Protokolle") : target;
                    Directory.CreateDirectory(dir);
                    File.Copy(f, Path.Combine(dir, name), true);
                }
                foreach (var d in Directory.GetDirectories(old))
                {
                    string name = Path.GetFileName(d);
                    CopyTree(d, name.Equals("Sicherungen", StringComparison.OrdinalIgnoreCase) ? Path.Combine(DocsRoot, "Sicherungen") : Path.Combine(target, name));
                }
                try
                {
                    File.WriteAllText(Path.Combine(old, "HINWEIS - Einstellungen liegen jetzt in Dokumente.txt"),
                        "Ab Version 4.0 speichert das Mischpult alles im Ordner Dokumente\\Mischpult.\r\n" +
                        "Die Dateien hier sind nur noch eine Sicherheitskopie (Stand beim Umzug).\r\n" +
                        "Zurück nach %AppData%: OPTIONEN → Sichern → „Alles im Ordner Dokumente“ ausschalten.\r\n");
                }
                catch { }
                return true;
            }
            catch { return false; }
        }

        internal static void CopyTree(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // ---------------------------------------------------------------- 168 Profile

        public const string ProfileFileName = "profil.txt";
        public static string ProfilePointer => Path.Combine(Folder, ProfileFileName);
        public static string ProfileFolder => Path.Combine(Folder, "Profile");
        /// <summary>Name aus "--profil Name" (oder null).</summary>
        public static string ProfileArg;
        private static bool _profRead;
        private static string _profile;
        private static bool _profOn, _profAsk;

        private static void ReadProfilePointer()
        {
            if (_profRead) return;
            _profRead = true;
            try
            {
                if (!File.Exists(ProfilePointer)) return;
                var lines = File.ReadAllLines(ProfilePointer);
                _profOn = true;
                _profile = CleanProfileName(lines.Length > 0 ? lines[0] : null);
                _profAsk = lines.Skip(1).Any(l => l.Trim().Equals("fragen", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            if (_profile != null && !File.Exists(ProfileFile(_profile))) _profile = null;
        }

        /// <summary>Profile an (die Zeiger-Datei gibt es).</summary>
        public static bool ProfilesOn { get { ReadProfilePointer(); return _profOn; } }
        /// <summary>Beim Start fragen, welches Profil.</summary>
        public static bool ProfileAsk { get { ReadProfilePointer(); return _profAsk; } }
        /// <summary>Aktuelles Profil (null = "Standard", also settings.json).</summary>
        public static string Profile
        {
            get
            {
                ReadProfilePointer();
                if (ProfileArg != null) return ProfileArg.Length == 0 || !File.Exists(ProfileFile(ProfileArg)) ? null : ProfileArg;
                return _profOn ? _profile : null;
            }
        }
        public static string ProfileDisplay(string name) => name ?? "Standard";

        public static string CleanProfileName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return null;
            n = n.Trim();
            if (n.Equals("Standard", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c.ToString(), "");
            foreach (var c in "<>:\"/\\|?*") n = n.Replace(c.ToString(), "");   // wie unter Windows
            n = n.Replace(".", "").Trim();
            if (n.Length > 24) n = n.Substring(0, 24).Trim();
            return n.Length == 0 ? null : n;
        }

        public static string ProfileFile(string name) => name == null ? Path.Combine(Folder, "settings.json") : Path.Combine(ProfileFolder, name + ".json");

        public static List<string> ListProfiles()
        {
            var l = new List<string>();
            try
            {
                if (Directory.Exists(ProfileFolder))
                    l.AddRange(Directory.GetFiles(ProfileFolder, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => CleanProfileName(n) == n).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
            }
            catch { }
            return l;
        }

        /// <summary>Zeiger-Datei schreiben (an/aus, aktuelles Profil, beim Start fragen).</summary>
        public static bool WriteProfilePointer(bool on, string current, bool ask)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (!on) { if (File.Exists(ProfilePointer)) File.Delete(ProfilePointer); }
                else File.WriteAllLines(ProfilePointer, new[] { current ?? "", ask ? "fragen" : "" });
                _profRead = false; _profOn = false; _profile = null; _profAsk = false;
                ReadProfilePointer();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Datei mit den Einstellungen (Profil beachtet).</summary>
        public static string FilePath => ProfileFile(Profile);
    }
}
