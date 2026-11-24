using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.7 Studio-Fenster am PC: die Studio-Ansicht der Fernbedienung als eigenes Fenster, nur auf diesem PC
        [DataMember] public bool StudioWinOn;      // Standard aus
        [DataMember] public string StudioCode;     // Zugangscode fuer das Fenster (bleibt gleich, damit ein offenes Fenster nach einem Neustart weiterlaeuft)
    }
}
