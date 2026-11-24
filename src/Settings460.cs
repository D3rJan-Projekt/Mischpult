using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.6 Fernbedienung: Studio-Ansicht (Tablet) mit EDIT-Seite, Pegel-Uebersicht und Musik-Tasten
        [DataMember] public bool RemoteStudioOff;   // aus = keine Studio-Ansicht (dann Pult bzw. Liste)
        [DataMember] public bool RemoteMediaOff;    // aus = keine Musik-Tasten (zurueck, Play/Pause, weiter) und kein „Jetzt läuft“ am Handy
    }
}
