using System;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>3.3: Symbol und Notizzettel pro Kanal.</summary>
    internal sealed partial class ChannelConfig
    {
        [DataMember] public int Symbol;     // 0 automatisch (nach dem Namen), 1..10 fest, 99 keins
        [DataMember] public string Note;    // Notizzettel (null = keiner)
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.3 Optik (alles Standard aus)
        [DataMember] public int VuKind;            // Zeigerinstrument: 0 VU, 1 BBC-Spitzenpegel, 2 DIN-Spitzenpegel, 3 Magisches Auge
        [DataMember] public int CoverArt;          // "Jetzt laeuft": 0 Cover, 1 Schallplatte, 2 Bandmaschine
        [DataMember] public bool ScopeLookOn;      // Oszilloskop wie ein Messgeraet (Nachleuchten, Raster)
        [DataMember] public int OnAirMode;         // ON-AIR-Lampe: 0 aus, 1 bei Aufnahme, 2 bei Aufnahme oder Stream-Mix
        [DataMember] public bool KnobRingOn;       // LED-Kranz um die Drehknoepfe
        [DataMember] public bool CapGlowOn;        // leuchtende Faderkappen
        [DataMember] public bool DymoPlates;       // Namensschilder als Praegeband
        [DataMember] public bool SymbolsOn;        // Symbol im Namensschild
        [DataMember] public int Wear;              // Gebrauchsspuren: 0 aus, 1 leicht, 2 stark
        [DataMember] public bool TubeGlowOn;       // Roehrengluehen hinter Lueftungsschlitzen (Seitenteile)
        [DataMember] public bool WarmUpOn;         // Lampen und LEDs gluehen beim Start langsam auf
        [DataMember] public bool PowerOffShowOn;   // Ausschalt-Show beim Beenden
        [DataMember] public int Wall;              // Wand hinter dem Pult: 0 keine, 1 Schaumstoff, 2 Ziegel, 3 Holzlamellen
        [DataMember] public int LiveBg;            // bewegter Hintergrund: 0 aus, 1 Sterne, 2 Farbverlauf, 3 Buehnennebel
        [DataMember] public bool SparksOn;         // Funken bei Uebersteuerung
        [DataMember] public bool DuckLinesOn;      // Ducking sichtbar (Linie in der Meterbridge)
        [DataMember] public bool PatchbayOn;       // Kabel-Ansicht verfuegbar
        [DataMember] public bool GroupBracketsOn;  // Gruppen-Klammer ueber den Kanaelen
        [DataMember] public bool NotesOn;          // Notizzettel an den Kanaelen
        [DataMember] public bool PultTipsOn;       // Hinweise (Tooltips) im Pult-Design

        private void Normalize330()
        {
            VuKind = Math.Max(0, Math.Min(3, VuKind));
            CoverArt = Math.Max(0, Math.Min(3, CoverArt));
            OnAirMode = Math.Max(0, Math.Min(2, OnAirMode));
            Wear = Math.Max(0, Math.Min(2, Wear));
            Wall = Math.Max(0, Math.Min(9, Wall));   // 4.3: bis Serverraum; 4.2: bis Kacheln
            LiveBg = Math.Max(0, Math.Min(12, LiveBg));   // 4.3: bis Glühwürmchen; 4.2: bis Matrix-Regen   // 3.4.6: 4 Regen, 5 Schnee (4.0: Musik-Hintergrund ist ein eigener Schalter)
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    if (c.Symbol < 0 || (c.Symbol > 10 && c.Symbol != 99)) c.Symbol = 0;
                    if (string.IsNullOrWhiteSpace(c.Note)) c.Note = null;
                    else if (c.Note.Length > 120) c.Note = c.Note.Substring(0, 120);
                }
        }
    }
}
