using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.8 Mehr fuers Studio-Pult am Handy (alles zuerst aus; im Studio-Fenster am PC immer dabei)
        [DataMember] public bool RemoteRecOn;        // Aufnahme starten/stoppen
        [DataMember] public bool RemoteRtaOn;        // Spektrum hinter der EQ-Kurve
        [DataMember] public bool RemoteMuteGrpOn;    // Mute-Gruppen in der Seitenleiste
        [DataMember] public bool RemoteSceneSaveOn;  // Szenen speichern/ueberschreiben
        [DataMember] public bool RemoteEditOn;       // Name und Farbe eines Kanals aendern
        [DataMember] public bool RemoteLufsOn;       // Lautheit (LUFS) in PEGEL
    }
}
