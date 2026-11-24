using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.5 Fernbedienung als Pult (Tablet): EQ, Balance, SOLO und COMP im Browser regeln
        [DataMember] public bool RemotePultOff;   // aus = am Tablet nur die Liste wie am Handy (kein Umschalter „Pult | Liste“)
        [DataMember] public bool RemoteEqOff;   // aus = EQ, Balance, SOLO und COMP am Handy/Tablet ausblenden (gilt nur, wenn die Fernbedienung an ist)
    }
}
