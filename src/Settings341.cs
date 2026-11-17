using System.Runtime.Serialization;

namespace Mischpult
{
    internal sealed partial class ChannelConfig
    {
        [DataMember] public bool AlwaysDsp;        // 3.4.1: Kanal laeuft immer ueber den EQ-Weg (EQ/COMP/AUTO ohne Aussetzer schalten)
        [DataMember] public int WidthPx;           // 3.4.5: mit der Maus gezogene Breite (logische Pixel, 0 = keine)
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.4.1
        [DataMember] public bool AppVolGuardOff;   // true = App-Lautstaerke nicht festhalten (wie bis 3.4)
        [DataMember] public bool SoundLogOn;       // Ton-Protokoll zur Fehlersuche (Standard aus)
        [DataMember] public bool FpsHighAuto;      // true = "Passend zum Bildschirm" bis 165 Bilder/s wie bis 3.4 (Standard: hoechstens 75)
        [DataMember] public bool BackgroundFullOff; // true = im Hintergrund nicht sparen (wie bis 3.4)
        [DataMember] public bool OutputDbOn;       // 3.4.3: Ausgaenge (MASTER) in dB wie am Mischpult statt in Prozent
        [DataMember] public bool DragWidthOff;     // 3.4.6: true = Kanaele NICHT an der Kante ziehen (Standard: ziehen geht)

        public const int DragWidthMin = 80, DragWidthMax = 260;
    }
}
