using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Mischpult
{
    /// <summary>2.17: ein Eintrag im Song-Verlauf.</summary>
    [DataContract]
    internal sealed class SongEntry
    {
        [DataMember] public string Title, Artist, App;
        [DataMember] public string Time;   // yyyy-MM-dd HH:mm
    }

    /// <summary>2.20: ein gespeichertes eigenes Design (Design-Editor).</summary>
    [DataContract]
    internal sealed partial class CustomDesign
    {
        [DataMember] public string Name;
        [DataMember] public int Style, Scheme, Accent;
        [DataMember] public Dictionary<string, int> Colors;
    }

    /// <summary>2.19: Klang eines Ausgabegeraets: 10-Band-EQ und Kopfhoerer-Raumklang.</summary>
    [DataContract]
    internal sealed partial class OutputFxConfig
    {
        [DataMember] public string DeviceId;
        [DataMember] public string Name;
        [DataMember] public bool EqOn;
        [DataMember] public float[] Geq;     // 10 Baender (dB) oder null
        [DataMember] public int Space;       // 0 aus, 1 leicht, 2 mittel, 3 stark, 4 Lautsprecher im Raum
    }

    /// <summary>2.18: gespeicherte Ansicht (nur Aussehen und Anzeigen, kein Ton).</summary>
    [DataContract]
    internal sealed class ViewPreset
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public Dictionary<string, string> Values;     // Einstellung -> Wert (JSON)
        [DataMember] public Dictionary<string, string> Channels;   // Kanal-Id -> "ausgeblendet|Feld|Bank"
    }

    [DataContract]
    internal sealed partial class ChannelConfig
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public int Color;
        [DataMember] public List<string> Apps;
        [DataMember] public float Fader = 1f;       // Faderposition 0..1 (1 = 0 dB)
        [DataMember] public bool Mute;
        [DataMember] public bool Solo;
        [DataMember] public float Pan;
        [DataMember] public string OutputDevice;    // null = Windows-Standard
        [DataMember] public string OutputDeviceName;
        [DataMember] public bool IsCatchAll;        // enthaelt alle nicht zugeordneten Apps
        [DataMember] public bool EqOn;
        [DataMember] public float EqLow, EqMid, EqHigh; // dB, -12..+12
        [DataMember] public bool CompOn;            // Nachtmodus / Kompressor (laeuft ueber den EQ-Weg)
        [DataMember] public bool AutoOn;            // Lautstaerke-Ausgleich (laeuft ueber den EQ-Weg)
        [DataMember] public float[] Geq;            // 10-Band-EQ (dB) oder null
        [DataMember] public float StreamDb;         // Anteil im Stream-Mix (dB, 0 = wie im eigenen Mix)
        [DataMember] public bool StreamOff;         // nicht im Stream-Mix
        [DataMember] public bool Hidden;            // in der Ansicht ausgeblendet (laeuft weiter)
        [DataMember] public bool FaderLock;         // Fader gesperrt (Maus und Mausrad aendern nichts)
        [DataMember] public float? FaderMark;       // Fader-Marke (Lieblingsstellung 0..1) oder null
        [DataMember] public string ImagePath;       // 2.16: eigenes Bild/Logo im Kanal (null = keins)
        [DataMember] public int? Field;             // 2.17: Anzeigefeld (0 Apps, 1 Lautheit, 2 Spektrum, 3 Verlauf, 4 Cover), null = wie alle
        [DataMember] public int Bank;               // 2.18: Bank 0 = in allen Baenken, 1..4 = A..D
        [DataMember] public int FxType;             // 2.19: Effekt 0 aus, 1 Hall, 2 Echo
        [DataMember] public int FxPreset;           // 2.19: Hall Raum/Halle/Kathedrale bzw. Echo kurz/mittel/lang
        [DataMember] public float? FxMix;           // 2.19: Anteil 0..1 (fehlt = 0,25)
        [DataMember] public bool Cue;               // 2.19: vorhoeren

        public static ChannelConfig Create(string name, int color, params string[] apps) => new ChannelConfig
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, Color = color, Apps = new List<string>(apps), Fader = 1f
        };
    }

    /// <summary>Gruppe (VCA): ein Fader, der mehrere Kanaele gemeinsam lauter/leiser macht.</summary>
    [DataContract]
    internal sealed partial class GroupConfig
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public int Color;
        [DataMember] public List<string> Members;
        [DataMember] public float Fader = 1f;
        [DataMember] public bool Mute;
        [DataMember] public bool FaderLock;
        [DataMember] public float? FaderMark;
    }

    /// <summary>Ein Sound im Soundboard.</summary>
    [DataContract]
    internal sealed class SoundPad
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string File;
        [DataMember] public float Volume = 0.8f;   // Faderposition
        [DataMember] public int Color;
    }

    [DataContract]
    internal sealed class HotkeyBinding
    {
        [DataMember] public string Action;   // z. B. "mic.mute" oder "ch:<id>:up"
        [DataMember] public int Keys;        // System.Windows.Forms.Keys (Taste | Modifizierer)
    }

    /// <summary>Gespeicherter Reglerzustand eines Kanals in einer Szene.</summary>
    [DataContract]
    internal sealed class SceneChannel
    {
        [DataMember] public string Id;
        [DataMember] public float Fader;
        [DataMember] public bool Mute;
        [DataMember] public float Pan;
        [DataMember] public bool EqOn, CompOn, AutoOn;
        [DataMember] public float EqLow, EqMid, EqHigh;
        [DataMember] public float[] Geq;
        [DataMember] public float StreamDb;
        [DataMember] public bool StreamOff;
    }

    [DataContract]
    internal sealed partial class Scene
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public List<SceneChannel> Channels;
        [DataMember] public float MasterVolume = -1f;   // -1 = nicht gespeichert
        [DataMember] public bool DuckOn;
    }

    /// <summary>Startet diese App, wird die Szene geladen (und danach wiederhergestellt).</summary>
    [DataContract]
    internal sealed class GameProfile
    {
        [DataMember] public string AppKey;
        [DataMember] public string SceneId;
    }

    /// <summary>Szene automatisch zu einer Uhrzeit laden.</summary>
    [DataContract]
    internal sealed class SceneSchedule
    {
        [DataMember] public string SceneId;
        [DataMember] public int Minute;      // Minuten seit Mitternacht (0..1439)
        [DataMember] public int Days = 127;  // Bit 0 = Montag ... Bit 6 = Sonntag
        [DataMember] public bool Off;        // false = aktiv

        public const int AllDays = 127;
        public static readonly string[] DayNames = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

        /// <summary>Bit fuer einen Wochentag (Montag = Bit 0).</summary>
        public static int DayBit(DayOfWeek d) => 1 << (((int)d + 6) % 7);

        public string TimeText => (Minute / 60).ToString("00") + ":" + (Minute % 60).ToString("00");

        public string DaysText
        {
            get
            {
                if ((Days & AllDays) == AllDays) return "täglich";
                if ((Days & AllDays) == 31) return "Mo–Fr";
                if ((Days & AllDays) == 96) return "Sa + So";
                var parts = new List<string>();
                for (int i = 0; i < 7; i++) if ((Days & (1 << i)) != 0) parts.Add(DayNames[i]);
                return parts.Count == 0 ? "nie" : string.Join(", ", parts);
            }
        }
    }

    /// <summary>Zuordnung eines MIDI-Reglers zu einer Funktion.</summary>
    [DataContract]
    internal sealed class MidiBinding
    {
        [DataMember] public string Device;  // Name des MIDI-Eingangs
        [DataMember] public int Kind;       // 0 = Controller (CC), 1 = Note, 2 = Pitchbend
        [DataMember] public int Channel;    // 0..15
        [DataMember] public int Number;     // CC- bzw. Notennummer
        [DataMember] public string Target;  // z. B. "ch:<id>:fader", "master:mute"
    }

    /// <summary>EQ-Voreinstellung (eingebaut oder selbst gespeichert).</summary>
    [DataContract]
    internal sealed class EqPreset
    {
        [DataMember] public string Name;
        [DataMember] public float Low, Mid, High;
        [DataMember] public bool Comp;

        public static readonly EqPreset[] BuiltIn =
        {
            new EqPreset { Name = "Neutral (alles 0 dB)" },
            new EqPreset { Name = "Stimmen klarer", Low = -6f, Mid = 3f, High = 4f },
            new EqPreset { Name = "Schritte hören", Low = -5f, Mid = 2f, High = 6f },
            new EqPreset { Name = "Mehr Bass", Low = 6f, Mid = 0f, High = 1f },
            new EqPreset { Name = "Weniger Bass", Low = -8f, Mid = 0f, High = 0f },
            new EqPreset { Name = "Musik (V-Form)", Low = 4f, Mid = -2f, High = 3f },
            new EqPreset { Name = "Wärmer", Low = 3f, Mid = 0f, High = -4f },
            new EqPreset { Name = "Nachtmodus (Kompressor)", Low = -3f, Mid = 2f, High = 0f, Comp = true },
            // 2.18: mehr fuers Zocken und Zuhoeren
            new EqPreset { Name = "Gegner orten (Schritte + Stimmen)", Low = -7f, Mid = 3f, High = 5f },
            new EqPreset { Name = "Explosionen zähmen", Low = -5f, Mid = 1f, High = -1f, Comp = true },
            new EqPreset { Name = "Dialoge (Filme, Serien)", Low = -3f, Mid = 4f, High = 2f, Comp = true },
            new EqPreset { Name = "Podcast / Stimme", Low = -6f, Mid = 3f, High = 1f, Comp = true },
            new EqPreset { Name = "Laptop-Lautsprecher", Low = -8f, Mid = 2f, High = 3f },
            new EqPreset { Name = "Kopfhörer brillanter", Low = 1f, Mid = -1f, High = 4f },
        };
    }

    [DataContract]
    internal sealed class KnownApp
    {
        [DataMember] public string Key;
        [DataMember] public string Name;
        [DataMember] public string Path;
    }

    /// <summary>Eigene Lautstaerke einer App innerhalb ihres Kanals (Faderposition, 1 = 0 dB).</summary>
    [DataContract]
    internal sealed class AppVolume
    {
        [DataMember] public string Key;
        [DataMember] public float Pos = 1f;
    }

    [DataContract]
    internal sealed partial class MixerSettings
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<ChannelConfig> Channels;
        [DataMember] public List<KnownApp> KnownApps;
        /// <summary>Apps mit eigener Lautstaerke im Kanal (fehlt = 0 dB, also wie der Kanal).</summary>
        [DataMember] public List<AppVolume> AppVolumes;
        [DataMember] public List<string> HiddenOutputs;
        [DataMember] public bool AlwaysOnTop;
        [DataMember] public bool CloseToTray = true;
        [DataMember] public bool TrayHintShown;
        [DataMember] public int WinX = int.MinValue, WinY = int.MinValue, WinW, WinH;
        [DataMember] public bool WinMax;
        [DataMember] public int UiLevel;           // Version der Oberflaeche beim letzten Start (28 = 2.8: Fenster einmal fuer die Meterbridge vergroessert)
        /// <summary>false = ein MASTER-Ausgang (Standard), true = ein Ausgangszug pro Geraet + Ausgangswahl pro Kanal.</summary>
        [DataMember] public bool MultiOutput;

        // Mikrofon
        [DataMember] public bool HideMic;
        [DataMember] public bool MicLiveMeter;
        /// <summary>false = MIKRO-Zug steht bei den Kanaelen (Standard), true = bei den Ausgaengen.</summary>
        [DataMember] public bool MicWithOutputs;
        /// <summary>Platz bei den Kanaelen: hinter diesem Kanal (Id), "^" = ganz links, null = hinter dem letzten Kanal.</summary>
        [DataMember] public string MicAfter;

        // Hotkeys (null = Standardbelegung)
        [DataMember] public List<HotkeyBinding> Hotkeys;

        // Ducking: Ausloeser-Kanal senkt Ziel-Kanaele ab
        [DataMember] public bool DuckOn;
        [DataMember] public string DuckTrigger;
        [DataMember] public List<string> DuckTargets;
        [DataMember] public float DuckAmount;     // dB, negativ
        [DataMember] public float DuckThreshold;  // dBFS
        [DataMember] public float DuckHold;       // Sekunden
        /// <summary>false (Standard) = Ton im Ausloeser, der nur den uebrigen PC-Ton spiegelt, wird ignoriert.</summary>
        [DataMember] public bool DuckNoLeakFilter;

        // Szenen und Spiel-Profile
        [DataMember] public List<Scene> Scenes;
        [DataMember] public List<GameProfile> GameProfiles;
        [DataMember] public List<SceneSchedule> Schedules;   // Szenen nach Uhrzeit

        // Spiele
        [DataMember] public bool NoAutoGames;      // false = Spiele automatisch in den Spiel-Kanal
        [DataMember] public string GameChannel;    // Kanal-ID fuer erkannte Spiele (auch fuer "Spiel im Hintergrund leiser")
        [DataMember] public List<string> NotGames; // Apps, die nie als Spiel gelten sollen
        [DataMember] public bool BgQuietOff;       // false = Spiel leiser, wenn es nicht im Vordergrund ist
        [DataMember] public float BgQuietDb;       // dB, negativ

        // Mikrofon: 0 = aus, 1 = Push-to-Talk, 2 = Push-to-Mute
        [DataMember] public int PttMode;
        [DataMember] public int PttKeys;

        [DataMember] public bool ProfileNoRestore; // false = nach dem Spiel vorherigen Mix wiederherstellen

        // Ausgang
        [DataMember] public float HearingCap;      // Gehoerschutz: hoechster Pegel in dBFS, 0 = aus
        [DataMember] public bool AutoSwitchOn;     // Headset automatisch umschalten
        [DataMember] public string AutoDevice, AutoDeviceName;       // bevorzugtes Geraet (z. B. Headset)
        [DataMember] public string AutoFallback, AutoFallbackName;   // zurueck auf dieses Geraet, wenn das bevorzugte weg ist
        [DataMember] public string AutoMic, AutoMicName;             // Mikro, das mit dem Headset kommt
        [DataMember] public string AutoMicFallback, AutoMicFallbackName;
        [DataMember] public bool NoSoftMute;       // false = beim Stummschalten sanft ausblenden
        [DataMember] public bool NightMode;        // Kompressor auf allen Kanaelen
        [DataMember] public List<EqPreset> EqPresets;

        // Mini-Ansicht
        [DataMember] public bool MiniOn;
        [DataMember] public int MiniX = int.MinValue, MiniY = int.MinValue;

        // MIDI
        [DataMember] public List<MidiBinding> Midi;

        // Gruppen
        [DataMember] public List<GroupConfig> Groups;

        // Stream-Mix
        [DataMember] public bool StreamOn;
        [DataMember] public string StreamDevice, StreamDeviceName;
        [DataMember] public float StreamMasterDb;  // 0 = 0 dB
        [DataMember] public bool StreamMute;

        // Soundboard und Sprach-Ausgang
        [DataMember] public List<SoundPad> Sounds;
        [DataMember] public bool SbNotMe, SbNotStream, SbNotVoice;  // false = Ziel an
        [DataMember] public bool VoiceOn;
        [DataMember] public string VoiceDevice, VoiceDeviceName;
        [DataMember] public bool VoiceGateOn;
        [DataMember] public float VoiceGateDb;
        [DataMember] public bool VoiceMuteInPath;  // Stumm/Push-to-Talk nur im Sprach-Ausgang
        // Mikro-Effekte im Sprach-Ausgang
        [DataMember] public bool VoiceFxHp, VoiceFxClear;
        [DataMember] public float VoiceFxLow, VoiceFxMid, VoiceFxHigh, VoiceFxGain;
        [DataMember] public int VoiceFxComp;       // 0 aus, 1 leicht, 2 stark
        [DataMember] public bool VoicePathMuted;   // Sprach-Ausgang ist stummgeschaltet (bleibt nach Neustart)
        [DataMember] public bool MuteWarnOff;      // false = "Du bist stumm"-Hinweis an
        [DataMember] public int? GhostPct;         // Helligkeit grauer Pegel 0..100 % (0 = aus, fehlt = Standard)
        public const int GhostDefault = 30;
        [DataMember] public bool MuteBeepOff;      // false = Piepton beim Stummschalten
        [DataMember] public int MuteBeepLevel;     // 1 leise, 2 mittel, 3 laut (0 = Standard)

        // Ansicht
        [DataMember] public bool Narrow;
        [DataMember] public int ColorScheme;       // 0 Dunkel, 1 Mitternacht, 2 Graphit
        [DataMember] public int AccentColor;       // 0 Bernstein, 1 Gruen, 2 Cyan, 3 Pink, 4 Weiss
        [DataMember] public int UiStyle;           // 0 Studio (Metall), 1 Klassisch, 2 Modern, 3 Neon, 4 Hell, 5 Vintage
        [DataMember] public bool SignalLedOff;     // false = SIG-Leuchte (Ton kommt an) an den Kanaelen
        [DataMember] public bool PeakLedOff;       // false = PEAK-Leuchte (gleich zu laut) an den Kanaelen
        [DataMember] public int VuFace;            // VU-Skala: 0 passend zum Design, 1 Creme, 2 Schwarz, 3 Weiss
        [DataMember] public int? VuFall;           // VU-Zeiger zurueck: 0 schnell (echt), 1 langsam, 2 sehr langsam (fehlt = 1)
        [DataMember] public bool GdiTextOff;       // true = Schrift geglaettet (GDI+) statt Windows-Schrift (GDI) – Notschalter
        // 2.11
        [DataMember] public float? DimDb;          // DIM: so viel leiser (dB, 6..40, fehlt = 20)
        [DataMember] public bool LoudOn;           // Loudness an
        [DataMember] public int? LoudLevel;        // Loudness-Staerke: 0 leicht, 1 mittel, 2 stark (fehlt = 1)
        [DataMember] public bool LoudFixed;        // true = immer volle Anhebung (nicht nach der Windows-Lautstaerke)
        [DataMember] public float? FadeSec;        // FADE: Dauer in Sekunden (1..30, fehlt = 5)
        [DataMember] public bool FadeKeyOff;       // true = keine FADE-Taste in den Kanaelen
        [DataMember] public bool MotorOff;         // true = Fader springen beim Szenenwechsel (statt zu fahren)
        [DataMember] public bool VuPeakNeedleOff;  // true = kein Schleppzeiger im VU-Meter
        [DataMember] public bool VuPeakLedOff;     // true = keine PEAK-Leuchte im VU-Meter
        public int LoudStrength { get => LoudLevel ?? 1; set => LoudLevel = value; }
        // 2.12
        [DataMember] public bool SpecOff;          // true = kein Spektrum in der Meterbridge
        [DataMember] public bool VuWinOpen;        // grosses VU-Fenster war offen (beim Start wieder oeffnen)
        [DataMember] public int VuWinX, VuWinY, VuWinW, VuWinH;
        [DataMember] public bool VuWinTop;         // VU-Fenster immer im Vordergrund
        [DataMember] public bool VuWinSpecOff;     // VU-Fenster ohne Spektrum
        [DataMember] public int PultDimMode;       // Pult dunkler: 0 aus, 1 immer, 2 abends, 3 mit Nachtmodus
        [DataMember] public int? PultBright;       // so hell (%) wenn dunkler (30..90, fehlt = 60)
        [DataMember] public int? PultDimFrom;      // abends ab (Stunde, fehlt = 20)
        [DataMember] public int? PultDimTo;        // bis (Stunde, fehlt = 7)
        [DataMember] public bool ClockOff;         // true = keine Uhr
        [DataMember] public int ClockStyle;        // Uhr: 0 Studio-Uhr (rot, Meterbridge/Kopfleiste), 1 LCD in der Kopfleiste, 2 analog (3.1)
        [DataMember] public int MeterColors;       // Pegel-Farben: 0 klassisch, 1 blau, 2 bernstein, 3 Kanalfarbe
        [DataMember] public int LcdStyle;          // Anzeigen: 0 passend, 1 gruenes LCD, 2 rote LED, 3 Nixie, 4 VFD
        [DataMember] public bool ColorCapsOff;     // true = Faderkappen/Knoepfe ohne Kanalfarbe
        [DataMember] public bool GreetingOff;      // true = kein Einschalt-Gruss
        // 2.13
        [DataMember] public string SpecSource;     // Spektrum & grosses VU-Meter zeigen: null = Standard-Ausgabe, "*" = alle Ausgaben, sonst Geraete-ID
        [DataMember] public string SpecSourceName; // Name des gewaehlten Geraets (fuer die Anzeige, wenn es gerade fehlt)
        [DataMember] public bool VuWinVuOff;       // grosses Fenster ohne VU-Meter (nur Spektrum)
        public const string SpecAll = "*";
        // 2.14
        [DataMember] public int? MeterFall;        // Pegel-Ruecklauf: 0 schnell, 1 normal, 2 langsam, 3 sofort (fehlt = 1)
        // 2.16
        [DataMember] public bool MeterRealtimeOff; // true = Pegel wie bis 2.15 nur ca. alle 30 ms
        [DataMember] public int MeterDelay;        // Pegel verzoegern (Index in MeterDelayValues), passend zum Ton bei Funk/Bluetooth
        public static readonly int[] MeterDelayValues = { 0, 40, 80, 150, 250 };
        [DataMember] public int? MeterHold;        // Spitze halten: 0 aus, 1 kurz, 2 normal, 3 lang, 4 bis Klick (fehlt = 2)
        [DataMember] public int MeterMode;         // Pegel zeigt: 0 Spitze, 1 Mittelwert, 2 beides
        [DataMember] public bool LufsOff;          // true = keine Lautheit (LUFS) im MASTER
        [DataMember] public int? LufsMode;         // LUFS: 0 Momentan, 1 Kurzzeit, 2 Integriert (fehlt = 1)
        [DataMember] public bool NowPlayingOff;    // true = keine "Jetzt laeuft"-Laufschrift
        [DataMember] public int? LampMode;         // Pultlampe: 0 aus, 1 an, 2 wenn das Pult dunkler ist (fehlt = 1)
        [DataMember] public int? LampPos;          // Lampe haengt: 0 links, 1 Mitte, 2 rechts (fehlt = 1)
        [DataMember] public int? LampStrength;     // Licht: 0 schwach, 1 mittel, 2 hell (fehlt = 1)
        [DataMember] public bool EqCurveOff;       // true = keine EQ-Kurve im PRESET-Feld
        [DataMember] public bool KeyClickOff;      // true = Tasten klicken nicht
        [DataMember] public int? KeyClickLevel;    // Klicken: 0 leise, 1 mittel, 2 laut (fehlt = 0)
        // 2.14.1
        [DataMember] public int BridgeBarWidth;    // Pegel in der Meterbridge: 0 schmal, 1 mittel, 2 breit
        // 2.15.2 Pegel im MASTER und an den Ausgaengen (fehlt = wie die Kanaele)
        [DataMember] public int? MeterOutFall;     // 0 schnell, 1 normal, 2 langsam
        [DataMember] public int? MeterOutHold;     // 0 aus, 1 kurz, 2 normal, 3 lang, 4 bis Klick
        [DataMember] public int? MeterOutMode;     // 0 Spitze, 1 Mittelwert, 2 beides
        // 2.15 Optik
        [DataMember] public int PanelMaterial;     // Oberflaeche der Zuege: 0 passend, 1 Carbon, 2 Alu, 3 Holz, 4 Leder
        [DataMember] public int CheekStyle;        // Seitenteile: 0 passend, 1 keine, 2 Holz, 3 Carbon, 4 Alu
        [DataMember] public int KnobStyle;         // Knoepfe: 0 passend, 1 Raendel, 2 Chrom, 3 Gummi, 4 Bakelit
        [DataMember] public int CapStyle;          // Faderkappen: 0 passend, 1 Chrom, 2 schwarz, 3 weiss, 4 Holz
        [DataMember] public bool DigitalPlates;    // Namensschilder als LCD
        [DataMember] public int LedShape;          // Pegel-LEDs: 0 eckig, 1 rund, 2 Striche
        [DataMember] public int? LedSize;          // 0 fein, 1 normal, 2 grob (fehlt = 1)
        [DataMember] public bool LedAfterglowOff;  // true = kein Nachgluehen
        [DataMember] public bool LedHaloOff;       // true = kein Lichtschein um die LEDs
        [DataMember] public int VuLight;           // Licht hinter der VU-Skala: 0 warm, 1 blau, 2 gruen, 3 weiss
        [DataMember] public bool FaderGlowOff;     // true = keine leuchtende Fader-Spur
        [DataMember] public bool AmbilightOff;     // true = kein Ambilight am Pultrand
        [DataMember] public int? IdleMinutes;      // Ruhemodus nach Minuten ohne Bedienung (0 = aus, fehlt = 10)
        [DataMember] public bool GonioOff;         // true = kein Goniometer in der Meterbridge
        [DataMember] public string BgImagePath;    // eigenes Hintergrundbild (null = keins)
        [DataMember] public int? BgDim;            // so stark abgedunkelt (%), fehlt = 55
        // 2.15.1 Leistung
        [DataMember] public int PerfFps;           // Bilder pro Sekunde: 0 passend zum Bildschirm, sonst 30/45/60/90
        [DataMember] public bool PacerOff;         // true = einfacher Windows-Timer statt genauem Takt
        [DataMember] public bool PerfPriorityOff;  // true = Pult bekommt beim Zeichnen keinen Vorrang
        [DataMember] public bool GameModeOff;      // true = kein Spiele-Modus
        [DataMember] public bool GamePauseOff;     // true = auch hinter einem Vollbild-Spiel weiterzeichnen
        [DataMember] public bool GameFxOff;        // true = Leucht-Effekte auch beim Spielen
        [DataMember] public int GameFps;           // Bilder pro Sekunde beim Spielen: 0 wie sonst, sonst 60/45/30
        // 2.15.3
        [DataMember] public bool FastDrawOff;      // true = LEDs einzeln zeichnen, Meterbridge jedes Bild ganz (wie bis 2.15.2)
        // 2.16
        [DataMember] public string GonioSource;    // Stereo-Bild: null = wie das Spektrum, "#std" = Standard, "*" = alle, sonst Geraete-ID
        [DataMember] public string GonioSourceName;
        [DataMember] public int SpecStyle;         // Spektrum: 0 LED-Balken, 1 Wasserfall, 2 Kurve, 3 gespiegelt, 4 rund (3.1)
        [DataMember] public bool VuDual;
        [DataMember] public int BridgeHeight;      // 3.1: Meterbridge-Hoehe 0 normal, 1 niedrig, 2 hoch
        [DataMember] public int? LedBright;        // 3.1: LED-Helligkeit 40..150 % (fehlt = 100)
        [DataMember] public bool ChannelFastOff;   // 3.1.2: true = Kanal-Pegel wie bis 3.1.1 (Mess-Aufnahmen alle 15 ms abholen)
        [DataMember] public bool LowLatencyOff;    // 3.1.1: true = Pegel wie bis 3.1 (Messung alle 10 ms, weicher Anstieg, Bild kurz nach dem Bildaufbau)
        [DataMember] public bool MuteDimOff;       // 3.1: true = stumme Kanaele nicht ganz abdunkeln
        [DataMember] public bool SoloDimOff;       // 3.1: true = bei SOLO die anderen nicht abdunkeln
        [DataMember] public bool ClipFlashOff;     // 3.1: true = Rand blinkt nicht bei Uebersteuerung
        [DataMember] public bool AutoFoldOn;       // 3.1: inaktive Kanaele einklappen
        [DataMember] public bool AnimStripsOff;    // 3.1: true = Kanaele springen (keine Gleit-Animation)
        [DataMember] public int OsdPos;            // 3.1: Lautstaerke-Einblendung: 0 unten Mitte … 6 oben links
        [DataMember] public int OsdSize;           // 3.1: 0 normal, 1 klein, 2 gross
        [DataMember] public int? FavDesign;        // 3.1: Lieblingsdesign fuer die Taste (fehlt = keins)
        [DataMember] public bool RandomDesignOn;   // 3.1: Zufalls-Design beim Start
        [DataMember] public List<int> RandomPool;  // 3.1: Designs fuer den Zufall (leer = alle)
        [DataMember] public bool BigShowOff;       // 3.1: true = Selbsttest wie bis 3.0 (Fader nur von unten an ihre Stelle, Spektrum ohne Welle)
        [DataMember] public int SeasonMode;        // 3.1: Saison-Deko 0 automatisch, 1 aus, 2 immer Halloween, 3 immer Weihnachten, 4 immer Silvester
        [DataMember] public bool OwnTitleBar;      // 3.1: eigene Titelleiste (Kopfleiste statt Windows-Leiste)
        [DataMember] public bool AutoIconColor;    // 3.1: Kanalfarbe automatisch aus dem App-Symbol (nur Anzeige)
        [DataMember] public bool TickerOn;         // 3.1: eigene Laufschrift verfuegbar (Taste/Knopf)
        [DataMember] public string TickerText;     // 3.1: Text der eigenen Laufschrift
        [DataMember] public int CapShape;          // 3.1: Faderkappen-Form 0 wie im Design, 1 kurz, 2 lang, 3 rund, 4 flach
        [DataMember] public int KeyShape;          // 3.1: Tasten-Form 0 wie im Design, 1 eckig, 2 rund, 3 Leuchtring
        [DataMember] public bool KeyColorsOn;      // 3.1: eigene Tasten-Farben an
        [DataMember] public Dictionary<string, int> KeyColors;   // 3.1: Taste (MUTE, SOLO …) -> Farbe (ARGB)           // 3.1: Doppelzeiger-VU (L und R in einem Instrument)
        [DataMember] public int SpecPalette;       // 3.1: Spektrum-Farben 0 klassisch, 1 Regenbogen, 2 Akzentfarbe, 3 Feuer, 4 Eis
        [DataMember] public bool LoudHistOff;      // true = kein Lautheits-Verlauf in der Meterbridge
        [DataMember] public int LoudHistMinutes;   // 1, 5 oder 15 (sonst 5)
        [DataMember] public bool BridgeWinOpen;    // Meterbridge im eigenen Fenster offen
        [DataMember] public bool BridgeWinTop;     // ... immer im Vordergrund
        [DataMember] public int BridgeWinX = int.MinValue, BridgeWinY, BridgeWinW, BridgeWinH;
        [DataMember] public bool Compact;          // Kompakt-Ansicht der Kanalzuege
        [DataMember] public bool ChannelImagesOff; // true = eigene Kanal-Bilder nicht zeigen
        [DataMember] public int BridgeStyle;       // Meterbridge: 0 LED-Saeulen, 1 Zeigerinstrumente (VU)
        // 2.16.1
        [DataMember] public string LoudHistSource; // Lautheits-Verlauf misst: null = Standard-Ausgabe, sonst Geraete-ID
        [DataMember] public string LoudHistSourceName;
        [DataMember] public bool LufsEachOff;      // true = Lautheit nur im MASTER/Standard-Ausgang (wie bis 2.16)
        // 2.16.2: Meterbridge nach Wunsch
        [DataMember] public List<string> BridgeHidden;   // Pegel, die in der Meterbridge nicht stehen (Schluessel der Zuege)
        [DataMember] public bool BridgePackOff;    // true = ausgeblendete Pegel lassen eine Luecke (sonst ruecken die anderen zusammen)
        [DataMember] public string BridgeOrder;    // Reihenfolge der Anzeigen von links: s Spektrum, l Lautheit, g Stereo-Bild, c Uhr (null = "slgc")
        [DataMember] public bool BridgeClockOff;   // true = Uhr nie in der Meterbridge (nur oben)
        public const string BridgeOrderDefault = "aslgoc";   // 2.17: a Cover, o Oszilloskop dazu
        // 2.16.3: frei anordnen
        [DataMember] public bool BridgeArrange;    // Meterbridge frei angeordnet (eigene Reihenfolge statt ueber den Kanaelen)
        [DataMember] public List<string> BridgeFreeOrder;   // Reihenfolge: Schluessel der Zuege, "#s" "#l" "#g" "#c" Anzeigen, "#_" freier Platz
        [DataMember] public bool BridgeDragOff;    // true = in der Meterbridge nichts mit der Maus verschieben
        // 2.17: Anzeigen
        [DataMember] public int ChannelField;      // Anzeigefeld der Kanaele: 0 Apps, 1 Lautheit, 2 Spektrum, 3 Pegel-Verlauf, 4 Album-Cover
        [DataMember] public bool OscOff;           // true = kein Oszilloskop in der Meterbridge
        [DataMember] public bool CoverOff;         // true = kein "Jetzt laeuft" mit Cover in der Meterbridge
        [DataMember] public int? SpecPeak;         // Spitzen im Spektrum: 0 aus, 1 kurz, 2 lang (fehlt = 1)
        [DataMember] public bool SpecHoverOff;     // true = keine Frequenz-Anzeige unter der Maus
        [DataMember] public bool SongsOff;         // true = kein Song-Verlauf
        [DataMember] public List<SongEntry> Songs; // zuletzt gespielt, neuester zuerst (hoechstens 100)
        [DataMember] public bool DiaryOff;         // true = kein Hoer-Tagebuch
        [DataMember] public int? DiaryLoud;        // "laut" ab: 0 empfindlich, 1 normal, 2 tolerant (fehlt = 1)
        [DataMember] public int? DiaryWarn;        // Warnung nach lautem Hoeren: 0 nie, 1/2/3 = ab 1/2/4 Std. (fehlt = 2)
        // 2.18: Bedienung
        [DataMember] public bool TapePlates;       // Namensschilder als Klebeband mit Handschrift
        [DataMember] public bool BanksOn;          // Kanal-Baenke: Reiter ueber den Kanaelen, pro Bank andere Kanaele
        [DataMember] public int? BankCount;        // 2..4 Baenke (fehlt = 2)
        [DataMember] public int CurrentBank;       // gezeigte Bank: 0 = alle, 1..4 = A..D
        [DataMember] public List<string> BankNames; // eigene Namen der Baenke (leer/null = A, B, C, D)
        [DataMember] public bool ViewsOff;         // true = kein Ansichten-Menue
        [DataMember] public List<ViewPreset> Views; // gespeicherte Ansichten
        [DataMember] public bool AutoBackupOff;    // true = keine taegliche Sicherung
        [DataMember] public int? BackupKeep;       // so viele Sicherungen behalten (fehlt = 10)
        [DataMember] public int SaverMinutes;      // Bildschirmschoner nach so vielen Minuten ohne Maus/Tastatur (0 = aus)
        [DataMember] public int SaverShows;        // 0 Meterbridge, 1 VU-Meter + Spektrum, 2 nur Spektrum
        [DataMember] public int SaverScreen;       // 0 Bildschirm des Pults, 1 Hauptbildschirm, 2 anderer Bildschirm
        [DataMember] public bool WizardDone;       // Einrichtungs-Assistent wurde gezeigt
        // 2.19: Ton
        [DataMember] public bool FxOff;            // true = Hall/Echo in den Kanaelen aus
        [DataMember] public bool OutFxOff;         // true = Ausgangs-EQ und Kopfhoerer-Raumklang aus
        [DataMember] public List<OutputFxConfig> OutputFx; // EQ/Raumklang je Ausgabegeraet
        [DataMember] public bool CueOn;            // Vorhoeren (CUE) an
        [DataMember] public string CueDevice, CueDeviceName; // Geraet zum Vorhoeren (z. B. Headset)
        [DataMember] public float? CueDb;          // Lautstaerke beim Vorhoeren (dB, -30..+6, fehlt = 0)
        [DataMember] public int RecFormat;         // Aufnahme-Format: 0 WAV, 1 FLAC, 2 MP3
        [DataMember] public int? Mp3Kbps;          // MP3: 192, 256 oder 320 kbit/s (fehlt = 320)
        // 2.20: Aussehen
        [DataMember] public bool CustomOff;        // true = eigene Farben aus (bleiben gespeichert)
        [DataMember] public Dictionary<string, int> CustomColors; // Design-Editor: Bereich -> Farbe (ARGB)
        [DataMember] public List<CustomDesign> CustomDesigns;     // gespeicherte eigene Designs
        [DataMember] public int Tilt;              // Schraeglage: 0 aus, 1 leicht, 2 stark
        [DataMember] public int ArmRest;           // Armauflage: 0 keine, 1 Leder, 2 Holz, 3 Gummi, 4 Alu
        [DataMember] public bool RgbOn;            // RGB-Beleuchtung ueber OpenRGB
        [DataMember] public string RgbHost;        // null = 127.0.0.1
        [DataMember] public int RgbPort;           // 0 = 6742
        [DataMember] public int RgbMode;           // 0 Pegel, 1 Spektrum, 2 Akzentfarbe, 3 lautester Kanal
        [DataMember] public int? RgbBright;        // Helligkeit 10..100 % (fehlt = 80)
        // 3.0
        [DataMember] public bool GpuOn;            // Zeichnen ueber die Grafikkarte (Direct2D) fuer grosses VU-Meter/Spektrum/Wasserfall
        [DataMember] public bool AccelOff;         // true = Hardware-Beschleunigung ganz aus (VSync, hohe Bildrate, sofort zeichnen, Grafikkarte)
        [DataMember] public bool VSyncOff;         // true = Takt nicht vom Bildaufbau des Bildschirms
        [DataMember] public bool HighFpsOff;       // true = hoechstens 90/120 Bilder/s wie bis 2.20
        [DataMember] public bool PaintNowOff;      // true = Windows entscheidet, wann gezeichnet wird (wie bis 2.20)
        // 3.0.2 (alles unter Hardware-Beschleunigung, nur die Bild-Statistik ist unabhaengig)
        [DataMember] public bool NoThrottleOff;    // true = Windows darf das Pult drosseln (Energiesparen)
        [DataMember] public bool GameBoostOff;     // true = kein Spiele-Vorrang (MMCSS) fuer die Zeichen-Faeden
        [DataMember] public bool VPhaseOff;        // true = direkt beim Bildaufbau zeichnen statt kurz danach
        [DataMember] public bool SteadyOff;        // true = Bildrate nicht automatisch gleichmaessig halten
        [DataMember] public bool SeamlessOff;      // true = Pegel wie bis 3.0.1 (nur der jeweils letzte Messwert, Prozess-Suche jede Sekunde)
        [DataMember] public bool SmoothMeterOff;   // true = Pegel springen sofort (wie bis 3.0.1)
        [DataMember] public bool StatsOverlay;     // Bild-Statistik im Pult einblenden
        // 3.0.3
        [DataMember] public bool SoftStartOff;     // true = wie bis 3.0.2 (neue Sessions erst spaeter eingestellt, harte Puffer-Schnitte, immer +80 dB)
        [DataMember] public bool MeterKeepOff;     // true = Pegel-Messung nach 10 s Pause beenden (wie bis 3.0.2)
        [DataMember] public bool SmartFxOff;       // true = Leucht-Effekte beim Spielen immer sparen (wie bis 3.0.1), sonst nur wenn der PC nicht hinterherkommt
        public static readonly int[] SaverMinuteValues = { 0, 5, 10, 20, 30 };
        public static readonly string[] CustomColorSlots = { "body", "panel", "keys", "disp", "text", "accent", "bar", "menu" };
        public static readonly int[] BackupKeepValues = { 5, 10, 30 };
        public const int BackupKeepDefault = 10;
        /// <summary>Gab es beim Laden noch keine Einstellungsdatei (Neuinstallation)?</summary>
        public static bool WasFresh;
        public const string GonioStd = "#std";
        /// <summary>Gueltige Reihenfolge: nur bekannte Anzeigen, keine doppelt, mindestens die vier von 2.16.2 (fehlende neue kommen an ihren Platz).</summary>
        public static bool ValidBridgeOrder(string o) =>
            o != null && o.Length >= 4 && o.Distinct().Count() == o.Length && o.All(ch => BridgeOrderDefault.IndexOf(ch) >= 0) && "slgc".All(ch => o.IndexOf(ch) >= 0);

        /// <summary>Vollstaendige Reihenfolge: fehlende (neue) Anzeigen hinter ihren Vorgaenger aus der Standard-Reihenfolge.</summary>
        public static string FullBridgeOrder(string o)
        {
            if (!ValidBridgeOrder(o)) return BridgeOrderDefault;
            var res = new List<char>(o);
            for (int i = 0; i < BridgeOrderDefault.Length; i++)
            {
                char d = BridgeOrderDefault[i];
                if (res.Contains(d)) continue;
                int at = 0;
                for (int j = i - 1; j >= 0; j--) { int p = res.IndexOf(BridgeOrderDefault[j]); if (p >= 0) { at = p + 1; break; } }
                res.Insert(at, d);
            }
            return new string(res.ToArray());
        }
        public static readonly int[] LoudHistMinuteValues = { 1, 5, 15 };
        public static readonly int[] PerfFpsValues = { 0, 30, 45, 60, 90, 120, 144 };
        public static readonly int[] GameFpsValues = { 0, 60, 45, 30 };
        public const int PultBrightDefault = 60, PultBrightMin = 30, PultBrightMax = 90;
        public const float DimDefault = 20f, DimMin = 6f, DimMax = 40f;
        public const float FadeDefault = 5f, FadeMin = 1f, FadeMax = 30f;
        [DataMember] public bool VuScaleVu;        // true = klassische VU-Zahlen (20 … 0 … +3), false = dB passend zur Empfindlichkeit
        [DataMember] public float? VuStartDb;      // VU-Zeiger reagiert ab diesem Pegel (dBFS am Skalenende "20", -60..-24, fehlt = -28)
        public const float VuStartDefault = -28f, VuStartMin = -60f, VuStartMax = -24f;
        [DataMember] public bool GrOff;            // false = orange Absenkungs-Anzeige (GR) neben dem Pegel
        [DataMember] public bool GlowOff;          // false = Namensschild leuchtet mit dem Pegel
        [DataMember] public bool PeakCountOn;      // true = PEAK zaehlt mit, wie oft es zu laut war (seit 2.13 Standard aus; altes "PeakCountOff" wird ignoriert)
        [DataMember] public float? SigLedDb;       // SIG leuchtet ab diesem Pegel (dBFS, -70..-20, fehlt = -50)
        [DataMember] public float? PeakLedDb;      // PEAK leuchtet ab diesem Pegel (dBFS, -20..0, fehlt = -2)

        public const float SigLedDefault = -50f, PeakLedDefault = -2f;
        public const float SigLedMin = -70f, SigLedMax = -20f, PeakLedMin = -20f, PeakLedMax = 0f;
        [DataMember] public bool MuteBlinkOff;     // false = MUTE blinkt, solange stumm (egal ob Ton ankommt)
        [DataMember] public bool PeakDbOff;        // false = dB-Spitzenwert und dB-Skala an den Pegeln
        [DataMember] public bool NumbersOff;       // false = Kanalnummern im Namensschild
        [DataMember] public bool HoverLightOff;    // false = Zug unter der Maus leicht beleuchtet
        [DataMember] public bool VuOff;            // false = VU-Meter (Zeiger) in den Ausgangszuegen
        [DataMember] public bool SelfTestOff;      // false = Selbsttest beim Start
        [DataMember] public bool BridgeOff;        // false = Meterbridge (grosse Pegel oben) an

        // Aufnahme
        [DataMember] public string RecordFolder;
        [DataMember] public string RecordTarget;   // null = alles (Standardgeraet), "dev:<id>", "ch:<id>"

        public const int KeyCtrl = 0x20000, KeyAlt = 0x40000, KeyShift = 0x10000;

        public static List<HotkeyBinding> DefaultHotkeys() => new List<HotkeyBinding>
        {
            new HotkeyBinding { Action = "mic.mute", Keys = KeyCtrl | KeyAlt | 'M' },
            new HotkeyBinding { Action = "app.toggle", Keys = KeyCtrl | KeyAlt | 'P' },
            new HotkeyBinding { Action = "duck.toggle", Keys = KeyCtrl | KeyAlt | 'D' },
        };

        public static readonly int[] Palette =
        {
            unchecked((int)0xFFFF5A4F), // Rot
            unchecked((int)0xFFFF9F1C), // Orange
            unchecked((int)0xFFF7D038), // Gelb
            unchecked((int)0xFF3DDC84), // Gruen
            unchecked((int)0xFF2EC4B6), // Tuerkis
            unchecked((int)0xFF3D9BFF), // Blau
            unchecked((int)0xFF9B6BFF), // Violett
            unchecked((int)0xFFFF5DA2), // Pink
            unchecked((int)0xFFA7B0BA), // Grau
        };

        public static readonly string[] PaletteNames =
            { "Rot", "Orange", "Gelb", "Grün", "Türkis", "Blau", "Violett", "Pink", "Grau" };

        /// <summary>Einstellungsordner – im portablen Modus (3.4.6) neben der EXE.</summary>
        // Folder (4.0: Dokumente/Cloud) steht in Settings400.cs

        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mischpult");

        // FilePath (4.0: Profile) steht in Settings400.cs

        public static MixerSettings CreateDefault()
        {
            var s = new MixerSettings
            {
                Channels = new List<ChannelConfig>
                {
                    new ChannelConfig
                    {
                        Id = "system", Name = "SYSTEM", Color = Palette[8], Apps = new List<string>(),
                        Fader = 1f, IsCatchAll = true
                    },
                    ChannelConfig.Create("SPIEL", Palette[0]),
                    ChannelConfig.Create("CHAT", Palette[3],
                        "discord.exe", "discordptb.exe", "discordcanary.exe", "teamspeak.exe", "ts3client_win64.exe",
                        "ts3client_win32.exe", "skype.exe", "zoom.exe", "ms-teams.exe", "teams.exe", "slack.exe",
                        "mumble.exe", "telegram.exe", "signal.exe", "whatsapp.exe", "element.exe", "guilded.exe"),
                    ChannelConfig.Create("MUSIK", Palette[6],
                        "spotify.exe", "applemusic.exe", "itunes.exe", "tidal.exe", "deezer.exe", "amazon music.exe",
                        "foobar2000.exe", "aimp.exe", "winamp.exe", "musicbee.exe", "vlc.exe", "music.ui.exe"),
                    ChannelConfig.Create("BROWSER", Palette[5],
                        "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "opera_gx.exe", "brave.exe",
                        "vivaldi.exe", "librewolf.exe", "zen.exe", "arc.exe", "waterfox.exe"),
                },
                KnownApps = new List<KnownApp>(),
                HiddenOutputs = new List<string>(),
                CloseToTray = true,
            };
            return s;
        }

        public void Normalize()
        {
            if (MuteBeepLevel < 1 || MuteBeepLevel > 3) MuteBeepLevel = 2;
            GhostPct = GhostPct.HasValue ? Math.Max(0, Math.Min(100, GhostPct.Value)) : GhostDefault;
            VuFall = VuFall.HasValue ? Math.Max(0, Math.Min(2, VuFall.Value)) : 1;
            DimDb = DimDb.HasValue && !float.IsNaN(DimDb.Value) && !float.IsInfinity(DimDb.Value) ? (float)Math.Round(Math.Max(DimMin, Math.Min(DimMax, DimDb.Value))) : DimDefault;
            LoudLevel = LoudLevel.HasValue ? Math.Max(0, Math.Min(2, LoudLevel.Value)) : 1;
            if (PultDimMode < 0 || PultDimMode > 3) PultDimMode = 0;
            if (ClockStyle < 0 || ClockStyle > 8) ClockStyle = 0;   // 8 Oszilloskop (4.0 finale)   // 3 Fallblatt, 4 Nixie (3.3), 5 Rollzaehlwerk (3.4), 6 Wortuhr, 7 Binaeruhr (4.0)
            if (string.IsNullOrEmpty(SpecSource)) SpecSource = null;
            if (MeterFall.HasValue) MeterFall = Math.Max(0, Math.Min(3, MeterFall.Value));
            if (MeterDelay < 0 || MeterDelay >= MeterDelayValues.Length) MeterDelay = 0;
            if (MeterHold.HasValue) MeterHold = Math.Max(0, Math.Min(4, MeterHold.Value));
            if (MeterMode < 0 || MeterMode > 2) MeterMode = 0;
            if (MeterOutFall.HasValue && (MeterOutFall < 0 || MeterOutFall > 3)) MeterOutFall = null;
            if (MeterOutHold.HasValue && (MeterOutHold < 0 || MeterOutHold > 4)) MeterOutHold = null;
            if (MeterOutMode.HasValue && (MeterOutMode < 0 || MeterOutMode > 2)) MeterOutMode = null;
            if (LufsMode.HasValue) LufsMode = Math.Max(0, Math.Min(2, LufsMode.Value));
            if (LampMode.HasValue) LampMode = Math.Max(0, Math.Min(2, LampMode.Value));
            if (LampPos.HasValue) LampPos = Math.Max(0, Math.Min(2, LampPos.Value));
            if (LampStrength.HasValue) LampStrength = Math.Max(0, Math.Min(2, LampStrength.Value));
            if (KeyClickLevel.HasValue) KeyClickLevel = Math.Max(0, Math.Min(2, KeyClickLevel.Value));
            if (BridgeBarWidth < 0 || BridgeBarWidth > 2) BridgeBarWidth = 0;
            if (PanelMaterial < 0 || PanelMaterial > 4) PanelMaterial = 0;
            if (CheekStyle < 0 || CheekStyle > 4) CheekStyle = 0;
            if (KnobStyle < 0 || KnobStyle > 4) KnobStyle = 0;
            if (CapStyle < 0 || CapStyle > 5) CapStyle = 0;   // 5 Glas (4.0 finale)
            if (LedShape < 0 || LedShape > 2) LedShape = 0;
            if (LedSize.HasValue) LedSize = Math.Max(0, Math.Min(2, LedSize.Value));
            if (VuLight < 0 || VuLight > 3) VuLight = 0;
            if (IdleMinutes.HasValue && Array.IndexOf(new[] { 0, 2, 5, 10, 30 }, IdleMinutes.Value) < 0) IdleMinutes = 10;
            if (BgDim.HasValue) BgDim = Math.Max(0, Math.Min(95, BgDim.Value));
            if (string.IsNullOrWhiteSpace(BgImagePath)) BgImagePath = null;
            if (Array.IndexOf(PerfFpsValues, PerfFps) < 0) PerfFps = 0;
            if (SpecStyle < 0 || SpecStyle > 7) SpecStyle = 0;   // 7 Skyline (4.0 finale)   // 5 3D, 6 Lichtorgel (3.3)
            if (BridgeHeight < 0 || BridgeHeight > 2) BridgeHeight = 0;
            if (LedBright.HasValue) LedBright = Math.Max(40, Math.Min(150, LedBright.Value));
            if (OsdPos < 0 || OsdPos > 6) OsdPos = 0;
            if (OsdSize < 0 || OsdSize > 2) OsdSize = 0;
            if (FavDesign.HasValue && (FavDesign < 0 || FavDesign > MaxStyle)) FavDesign = null;
            if (SeasonMode < 0 || SeasonMode > 9) SeasonMode = 0;   // 4.0: 5 Ostern, 6 Fruehling, 7 Sommer, 8 Herbst, 9 Geburtstag
            if (CapShape < 0 || CapShape > 5) CapShape = 0;   // 3.4.6: 5 Griffmulde
            if (KeyShape < 0 || KeyShape > 6) KeyShape = 0;   // 6 Pads (4.0 finale)   // 4 Kippschalter, 5 Bakelit (3.3)
            if (SpecPalette < 0 || SpecPalette > 4) SpecPalette = 0;
            if (Array.IndexOf(LoudHistMinuteValues, LoudHistMinutes) < 0) LoudHistMinutes = 5;
            if (BridgeStyle < 0 || BridgeStyle > 1) BridgeStyle = 0;
            if (string.IsNullOrEmpty(GonioSource)) GonioSource = null;
            if (string.IsNullOrEmpty(LoudHistSource) || LoudHistSource == GonioStd || LoudHistSource == SpecAll) { LoudHistSource = null; LoudHistSourceName = null; }
            if (BridgeHidden != null) BridgeHidden = BridgeHidden.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!ValidBridgeOrder(BridgeOrder)) BridgeOrder = null;
            ChannelField = Math.Max(0, Math.Min(4, ChannelField));
            if (SpecPeak.HasValue) SpecPeak = Math.Max(0, Math.Min(2, SpecPeak.Value));
            if (DiaryLoud.HasValue) DiaryLoud = Math.Max(0, Math.Min(2, DiaryLoud.Value));
            if (DiaryWarn.HasValue) DiaryWarn = Math.Max(0, Math.Min(3, DiaryWarn.Value));
            if (Songs != null) { Songs.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Title)); if (Songs.Count > 100) Songs.RemoveRange(100, Songs.Count - 100); }
            if (BridgeFreeOrder != null)
            {
                BridgeFreeOrder = BridgeFreeOrder.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (BridgeFreeOrder.Count == 0) BridgeFreeOrder = null;
            }
            if (Array.IndexOf(GameFpsValues, GameFps) < 0) GameFps = 0;
            if (VuWinVuOff && VuWinSpecOff) VuWinSpecOff = false;   // nie ganz leer
            if (MeterColors < 0 || MeterColors > 4) MeterColors = 0;   // 4 = eigene (3.2)
            if (LcdStyle < 0 || LcdStyle > 5) LcdStyle = 0;   // 5 Punktmatrix (3.3)
            PultBright = PultBright.HasValue ? Math.Max(PultBrightMin, Math.Min(PultBrightMax, PultBright.Value)) : PultBrightDefault;
            PultDimFrom = PultDimFrom.HasValue ? Math.Max(0, Math.Min(23, PultDimFrom.Value)) : 20;
            PultDimTo = PultDimTo.HasValue ? Math.Max(0, Math.Min(23, PultDimTo.Value)) : 7;
            if (VuWinW < 0 || VuWinH < 0 || VuWinW > 20000 || VuWinH > 20000) { VuWinW = 0; VuWinH = 0; }
            FadeSec = FadeSec.HasValue && !float.IsNaN(FadeSec.Value) && !float.IsInfinity(FadeSec.Value) ? (float)Math.Round(Math.Max(FadeMin, Math.Min(FadeMax, FadeSec.Value))) : FadeDefault;
            VuStartDb = VuStartDb.HasValue && !float.IsNaN(VuStartDb.Value) && !float.IsInfinity(VuStartDb.Value)
                ? (float)Math.Round(Math.Max(VuStartMin, Math.Min(VuStartMax, VuStartDb.Value))) : VuStartDefault;
            if (VuFace < 0 || VuFace > 3) VuFace = 0;
            SigLedDb = SigLedDb.HasValue && !float.IsNaN(SigLedDb.Value) ? (float)Math.Round(Math.Max(SigLedMin, Math.Min(SigLedMax, SigLedDb.Value))) : SigLedDefault;
            PeakLedDb = PeakLedDb.HasValue && !float.IsNaN(PeakLedDb.Value) ? (float)Math.Round(Math.Max(PeakLedMin, Math.Min(PeakLedMax, PeakLedDb.Value))) : PeakLedDefault;
            if (Channels == null || Channels.Count == 0) Channels = CreateDefault().Channels;
            foreach (var c in Channels) if (c != null && c.Field.HasValue) c.Field = Math.Max(0, Math.Min(4, c.Field.Value));   // 2.17
            foreach (var c in Channels) if (c != null) c.Bank = Math.Max(0, Math.Min(4, c.Bank));   // 2.18
            foreach (var c in Channels)   // 2.19
            {
                if (c == null) continue;
                c.FxType = Math.Max(0, Math.Min(2, c.FxType));
                c.FxPreset = Math.Max(0, Math.Min(2, c.FxPreset));
                if (c.FxMix.HasValue) c.FxMix = float.IsNaN(c.FxMix.Value) ? 0.25f : Math.Max(0.05f, Math.Min(1f, c.FxMix.Value));
            }
            if (OutputFx != null)
            {
                OutputFx.RemoveAll(o => o == null || string.IsNullOrEmpty(o.DeviceId));
                foreach (var o in OutputFx)
                {
                    o.Space = Math.Max(0, Math.Min(4, o.Space));
                    if (o.Geq != null && o.Geq.Length != 10) o.Geq = null;
                    if (o.Geq != null) for (int i = 0; i < 10; i++) o.Geq[i] = ClampEq(o.Geq[i]);
                }
                OutputFx = OutputFx.GroupBy(o => o.DeviceId, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
                if (OutputFx.Count == 0) OutputFx = null;
            }
            if (string.IsNullOrEmpty(CueDevice)) { CueDevice = null; CueDeviceName = null; }
            if (CueDb.HasValue) CueDb = float.IsNaN(CueDb.Value) ? 0f : (float)Math.Round(Math.Max(-30f, Math.Min(6f, CueDb.Value)));
            RecFormat = Math.Max(0, Math.Min(2, RecFormat));
            // 2.20
            if (CustomColors != null)
            {
                CustomColors = CustomColors.Where(kv => kv.Key != null && Array.IndexOf(CustomColorSlots, kv.Key) >= 0)
                                           .ToDictionary(kv => kv.Key, kv => kv.Value | unchecked((int)0xFF000000));
                if (CustomColors.Count == 0) CustomColors = null;
            }
            if (CustomDesigns != null)
            {
                CustomDesigns.RemoveAll(d => d == null || string.IsNullOrWhiteSpace(d.Name));
                foreach (var d in CustomDesigns) if (d.Colors == null) d.Colors = new Dictionary<string, int>();
                if (CustomDesigns.Count > 30) CustomDesigns.RemoveRange(30, CustomDesigns.Count - 30);
                if (CustomDesigns.Count == 0) CustomDesigns = null;
            }
            Tilt = Math.Max(0, Math.Min(2, Tilt));
            ArmRest = Math.Max(0, Math.Min(4, ArmRest));
            if (string.IsNullOrWhiteSpace(RgbHost)) RgbHost = null;
            if (RgbPort < 0 || RgbPort > 65535) RgbPort = 0;
            RgbMode = Math.Max(0, Math.Min(3, RgbMode));
            if (RgbBright.HasValue) RgbBright = Math.Max(10, Math.Min(100, RgbBright.Value));
            if (Mp3Kbps.HasValue && Mp3Kbps != 192 && Mp3Kbps != 256 && Mp3Kbps != 320) Mp3Kbps = null;
            if (BankCount.HasValue) BankCount = Math.Max(2, Math.Min(4, BankCount.Value));
            CurrentBank = Math.Max(0, Math.Min(BankCount ?? 2, CurrentBank));
            if (BankNames != null)
            {
                BankNames = BankNames.Take(4).Select(n => string.IsNullOrWhiteSpace(n) ? null : n.Trim()).ToList();
                if (BankNames.All(n => n == null)) BankNames = null;
            }
            if (Views != null)
            {
                Views.RemoveAll(v => v == null || string.IsNullOrWhiteSpace(v.Name));
                foreach (var v in Views)
                {
                    if (string.IsNullOrEmpty(v.Id)) v.Id = Guid.NewGuid().ToString("N");
                    if (v.Values == null) v.Values = new Dictionary<string, string>();
                    if (v.Channels == null) v.Channels = new Dictionary<string, string>();
                }
                if (Views.Count > 20) Views.RemoveRange(20, Views.Count - 20);
                if (Views.Count == 0) Views = null;
            }
            if (BackupKeep.HasValue) BackupKeep = Math.Max(1, Math.Min(60, BackupKeep.Value));
            if (Array.IndexOf(SaverMinuteValues, SaverMinutes) < 0) SaverMinutes = 0;
            SaverShows = Math.Max(0, Math.Min(2, SaverShows));
            SaverScreen = Math.Max(0, Math.Min(2, SaverScreen));
            if (KnownApps == null) KnownApps = new List<KnownApp>();
            if (AppVolumes == null) AppVolumes = new List<AppVolume>();
            AppVolumes.RemoveAll(v => v == null || string.IsNullOrEmpty(v.Key) || float.IsNaN(v.Pos) || v.Pos >= 0.9995f);
            foreach (var v in AppVolumes) { v.Key = v.Key.ToLowerInvariant(); v.Pos = Math.Max(0f, Math.Min(1f, v.Pos)); }
            if (HiddenOutputs == null) HiddenOutputs = new List<string>();
            bool hasCatchAll = false;
            foreach (var c in Channels)
            {
                if (c.Id == null) c.Id = Guid.NewGuid().ToString("N");
                if (c.Apps == null) c.Apps = new List<string>();
                if (string.IsNullOrEmpty(c.Name)) c.Name = "KANAL";
                if (c.Color == 0) c.Color = Palette[8];
                if (float.IsNaN(c.Fader)) c.Fader = 1f;
                c.Fader = Math.Max(0f, Math.Min(1f, c.Fader));
                c.Pan = Math.Max(-1f, Math.Min(1f, float.IsNaN(c.Pan) ? 0f : c.Pan));
                c.Solo = false; // Solo ist nur zum Abhoeren gedacht und wird nicht gespeichert
                c.EqLow = ClampEq(c.EqLow); c.EqMid = ClampEq(c.EqMid); c.EqHigh = ClampEq(c.EqHigh);
                if (c.IsCatchAll)
                {
                    if (hasCatchAll) c.IsCatchAll = false; else { hasCatchAll = true; c.Apps.Clear(); }
                }
            }
            if (!hasCatchAll)
                Channels.Insert(0, new ChannelConfig { Id = "system", Name = "SYSTEM", Color = Palette[8], Apps = new List<string>(), Fader = 1f, IsCatchAll = true });

            // Szenen, Profile, Spiele, MIDI
            if (Scenes == null) Scenes = new List<Scene>();
            Scenes.RemoveAll(sc => sc == null || sc.Id == null);
            foreach (var sc in Scenes) { if (sc.Channels == null) sc.Channels = new List<SceneChannel>(); if (string.IsNullOrEmpty(sc.Name)) sc.Name = "Szene"; }
            if (GameProfiles == null) GameProfiles = new List<GameProfile>();
            if (Schedules == null) Schedules = new List<SceneSchedule>();
            Schedules.RemoveAll(e => e == null || e.SceneId == null || Scenes.Find(sc => sc.Id == e.SceneId) == null);
            foreach (var e in Schedules) { e.Minute = Math.Max(0, Math.Min(1439, e.Minute)); e.Days &= SceneSchedule.AllDays; }
            GameProfiles.RemoveAll(g => g == null || g.AppKey == null || Scenes.Find(sc => sc.Id == g.SceneId) == null);
            if (NotGames == null) NotGames = new List<string>();
            if (Midi == null) Midi = new List<MidiBinding>();
            if (BgQuietDb >= 0 || BgQuietDb < -40) BgQuietDb = -12f;
            if (PttMode < 0 || PttMode > 2) PttMode = 0;
            if (float.IsNaN(HearingCap) || HearingCap > 0 || HearingCap < -40) HearingCap = 0;
            if (EqPresets == null) EqPresets = new List<EqPreset>();
            EqPresets.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Name));
            foreach (var e in EqPresets) { e.Low = ClampEq(e.Low); e.Mid = ClampEq(e.Mid); e.High = ClampEq(e.High); }
            if (MiniX == 0 && MiniY == 0) { MiniX = int.MinValue; MiniY = int.MinValue; } // aeltere Einstellungen
            foreach (var c in Channels)
            {
                if (c.Geq != null && c.Geq.Length != 10) c.Geq = null;
                if (c.Geq != null) for (int i = 0; i < 10; i++) c.Geq[i] = ClampEq(c.Geq[i]);
                if (float.IsNaN(c.StreamDb) || c.StreamDb > 6f) c.StreamDb = 0f;
                if (c.StreamDb < -60f) c.StreamDb = -60f;
            }
            if (Groups == null) Groups = new List<GroupConfig>();
            Groups.RemoveAll(g => g == null);
            foreach (var g in Groups)
            {
                if (g.Id == null) g.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(g.Name)) g.Name = "GRUPPE";
                if (g.Members == null) g.Members = new List<string>();
                g.Members.RemoveAll(m => Channels.Find(c => c.Id == m) == null);
                if (g.Color == 0) g.Color = Palette[4];
                g.Fader = float.IsNaN(g.Fader) ? 1f : Math.Max(0f, Math.Min(1f, g.Fader));
            }
            if (Sounds == null) Sounds = new List<SoundPad>();
            Sounds.RemoveAll(sp => sp == null || string.IsNullOrEmpty(sp.File));
            foreach (var sp in Sounds)
            {
                if (sp.Id == null) sp.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(sp.Name)) sp.Name = Path.GetFileNameWithoutExtension(sp.File);
                if (float.IsNaN(sp.Volume) || sp.Volume <= 0f || sp.Volume > 1f) sp.Volume = 0.8f;
            }
            if (float.IsNaN(VoiceGateDb) || VoiceGateDb >= -10f || VoiceGateDb < -80f) VoiceGateDb = -45f;
            float FxClamp(float v, float r) => float.IsNaN(v) ? 0f : Math.Max(-r, Math.Min(r, v));
            VoiceFxLow = FxClamp(VoiceFxLow, 12f); VoiceFxMid = FxClamp(VoiceFxMid, 12f); VoiceFxHigh = FxClamp(VoiceFxHigh, 12f);
            VoiceFxGain = FxClamp(VoiceFxGain, 12f);
            VoiceFxComp = Math.Max(0, Math.Min(2, VoiceFxComp));
            if (float.IsNaN(StreamMasterDb) || StreamMasterDb > 6f || StreamMasterDb < -60f) StreamMasterDb = 0f;
            if (ColorScheme < 0 || ColorScheme > 2) ColorScheme = 0;
            if (AccentColor < 0 || AccentColor > 4) AccentColor = 0;
            if (UiStyle < 0 || UiStyle > MaxStyle) UiStyle = 0;   // 5 Vintage, 6 Schwarz-Gold, 7 Broadcast-Blau, 8 Retro-Hi-Fi, 9 Carbon, 10 Holz & Messing (4.0)
            foreach (var sc in Scenes)
            {
                if (sc.MasterVolume > 1f) sc.MasterVolume = -1f;
                foreach (var ch in sc.Channels)
                {
                    if (ch == null) continue;
                    if (ch.Geq != null && ch.Geq.Length != 10) ch.Geq = null;
                    if (ch.Geq != null) for (int i = 0; i < 10; i++) ch.Geq[i] = ClampEq(ch.Geq[i]);
                }
                sc.Channels.RemoveAll(ch => ch == null);
            }
            if (GameChannel == null || Channels.Find(c => c.Id == GameChannel) == null)
            {
                var gc = Channels.Find(c => string.Equals(c.Name, "SPIEL", StringComparison.OrdinalIgnoreCase) && !c.IsCatchAll)
                      ?? Channels.Find(c => string.Equals(c.Name, "GAME", StringComparison.OrdinalIgnoreCase) && !c.IsCatchAll);
                GameChannel = gc?.Id;
            }
            if (string.IsNullOrEmpty(RecordFolder))
                RecordFolder = DefaultRecordFolder;   // 4.0: im Dokumente-Ordner

            // Hotkeys
            if (Hotkeys == null) Hotkeys = DefaultHotkeys();
            Hotkeys.RemoveAll(h => h == null || string.IsNullOrEmpty(h.Action) || h.Keys == 0);

            // Ducking
            if (DuckAmount >= 0 || DuckAmount < -40) DuckAmount = -15f;
            if (DuckThreshold >= 0 || DuckThreshold < -80) DuckThreshold = -45f;
            if (DuckHold <= 0 || DuckHold > 10) DuckHold = 0.8f;
            ChannelConfig byName(string n) => Channels.Find(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase) && !c.IsCatchAll);
            if (DuckTrigger == null || (DuckTrigger != DuckMicId && DuckTrigger != DuckSbId && Channels.Find(c => c.Id == DuckTrigger) == null))   // 4.2.2: auch das Mikrofon
                DuckTrigger = (byName("CHAT") ?? Channels.Find(c => !c.IsCatchAll) ?? Channels[0]).Id;
            if (DuckTargets == null)
            {
                DuckTargets = new List<string>();
                foreach (var n in new[] { "MUSIK", "BROWSER" }) { var c = byName(n); if (c != null) DuckTargets.Add(c.Id); }
            }
            DuckTargets.RemoveAll(id => id == DuckTrigger || Channels.Find(c => c.Id == id) == null);

            // Jede App darf nur in einem Kanal sein
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Channels)
            {
                var clean = new List<string>();
                foreach (var a in c.Apps)
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    var k = a.Trim().ToLowerInvariant();
                    if (seen.Add(k)) clean.Add(k);
                }
                c.Apps = clean;
            }
            Normalize320();   // 3.2
            Normalize330();   // 3.3
            Normalize340();   // 3.4
            Normalize346();   // 3.4.6
            Normalize350();   // 3.5
            Normalize400();   // 4.0
            Normalize400f();  // 4.0 finale
            Normalize410();   // 4.1
            Normalize420();   // 4.2
            Normalize430();   // 4.3
            Normalize440();   // 4.4: Fernbedienung
        }

        public const float EqRange = 12f;
        private static float ClampEq(float v) => float.IsNaN(v) ? 0f : Math.Max(-EqRange, Math.Min(EqRange, v));

        public static MixerSettings Load()
        {
            try
            {
                MigrateToDocs();   // 4.0: einmal aus %AppData% nach Dokumente\Mischpult
                WasFresh = !File.Exists(FilePath) && !File.Exists(ProfileFile(null));
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var s = (MixerSettings)CreateSerializer().ReadObject(fs);
                        if (s != null) { s.Normalize(); return s; }
                    }
                }
            }
            catch
            {
                try { File.Copy(FilePath, FilePath + ".defekt", true); } catch { }
            }
            var d = CreateDefault();
            d.Normalize();
            return d;
        }

        public void Save() { if (!SaveBlocked) SaveTo(FilePath); }
        /// <summary>4.1: abgesicherter Start – nichts speichern.</summary>
        public static bool SaveBlocked;

        /// <summary>Speichert die Einstellungen in eine Datei (ueber eine Zwischendatei). Liefert false bei Fehlern.</summary>
        public bool SaveTo(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                using (var fs = File.Create(tmp))
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true, "  "))
                {
                    CreateSerializer().WriteObject(w, this);
                    w.Flush();
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Einstellungen aus einer Datei laden (z. B. Import). Liefert null, wenn die Datei nicht passt.</summary>
        public static MixerSettings LoadFrom(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var s = (MixerSettings)CreateSerializer().ReadObject(fs);
                    if (s == null || s.Channels == null || s.Channels.Count == 0) return null;
                    s.Normalize();
                    return s;
                }
            }
            catch { return null; }
        }

        private static DataContractJsonSerializer CreateSerializer() =>
            new DataContractJsonSerializer(typeof(MixerSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
    }
}
