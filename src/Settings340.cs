using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>3.4: Aufkleber auf einem Zug (Lage in logischen Pixeln im Zug).</summary>
    [DataContract]
    internal sealed class StickerConfig
    {
        [DataMember] public string Owner;     // Kanal-ID, "master" oder "out:<Geraet>"
        [DataMember] public int Kind;         // 0..StickerNames-1
        [DataMember] public float X, Y;       // Mitte, logische Pixel im Zug
        [DataMember] public float Angle;      // Grad
    }

    internal sealed partial class ChannelConfig
    {
        [DataMember] public int Color2;          // 3.4: zweite Kanalfarbe (0 = keine)
        [DataMember] public float[] EqAlt;       // 3.4: EQ B (LO, MID, HI, dann 10 Baender) – null = noch keiner
        [DataMember] public bool EqIsB;          // 3.4: gerade ist EQ B aktiv
    }

    internal sealed partial class MixerSettings
    {
        // ---- 3.4 Ton & Komfort (alles Standard aus)
        [DataMember] public int SoftStartMode;     // Sanft einblenden: 0 aus, 1 beim Start des Pults, 2 auch wenn eine App anfaengt
        [DataMember] public int SoftStartSec;      // Dauer: 0 = 2 s (Standard), sonst 1..3
        [DataMember] public bool QuietHoursOn;     // Leise-Stunden
        [DataMember] public int QuietFrom, QuietTo;    // Uhrzeit (Stunde); 0/0 = 22 bis 7 Uhr
        [DataMember] public int QuietMax;          // hoechste Lautstaerke der Ausgaenge in % (0 = 30 %)
        [DataMember] public bool PhonesPauseOn;    // Kopfhoerer abgezogen -> Musik pausieren
        [DataMember] public bool PhonesResumeOn;   // ... und beim Einstecken weiter
        [DataMember] public bool LevelMatchOn;     // Lautheit angleichen auf Knopfdruck
        [DataMember] public bool EqAbOn;           // EQ A/B
        [DataMember] public bool EqPaintOn;        // EQ malen
        [DataMember] public bool MonoKeyOn;        // MONO-Taste am Ausgang
        // ---- 3.4 Bedienung
        [DataMember] public bool VolKeysOn;        // Lautstaerketasten der Tastatur steuern einen Kanal
        [DataMember] public string VolKeysTarget;  // Kanal-ID (null = MASTER)
        [DataMember] public bool TrayWheelOn;      // Mausrad ueber dem Symbol im Infobereich
        [DataMember] public string TrayWheelTarget;    // Kanal-ID (null = MASTER)
        [DataMember] public bool QuickPopupOn;     // Schnell-Regler beim Klick aufs Symbol
        [DataMember] public bool KeyNavOn;         // Tastatur-Bedienung im Pult
        [DataMember] public bool DuplicateOn;      // Kanal duplizieren (Menue)
        [DataMember] public bool SoundResetOn;     // Klang zuruecksetzen (Menue)
        // ---- 3.4 Anzeigen & Optik
        [DataMember] public bool PcMeterOn;        // PC-Auslastung in der Meterbridge
        [DataMember] public bool PaperLookOn;      // Lautheits-Verlauf als Papierschreiber
        [DataMember] public bool SpeakersOn;       // Studio-Boxen links und rechts
        [DataMember] public bool SunLightOn;       // Licht nach Uhrzeit
        [DataMember] public bool DustCoverOn;      // Staubschutzhaube im Ruhemodus
        [DataMember] public bool NamePlateOn;      // eigenes Typenschild
        [DataMember] public string NamePlateText;  // z. B. "Jans Studio"
        [DataMember] public bool TwoToneOn;        // zweifarbige Kanalfarben
        [DataMember] public bool StickersOn;       // Aufkleber
        [DataMember] public List<StickerConfig> Stickers;

        public static readonly int[] SoftStartSecValues = { 1, 2, 3 };
        public static readonly int[] QuietMaxValues = { 15, 20, 30, 40, 50, 60 };

        public int SoftStartSecNow => SoftStartSec >= 1 && SoftStartSec <= 3 ? SoftStartSec : 2;
        public int QuietMaxNow => Array.IndexOf(QuietMaxValues, QuietMax) >= 0 ? QuietMax : 30;
        public int QuietFromNow => QuietFrom == 0 && QuietTo == 0 ? 22 : QuietFrom;
        public int QuietToNow => QuietFrom == 0 && QuietTo == 0 ? 7 : QuietTo;

        private void Normalize340()
        {
            SoftStartMode = Math.Max(0, Math.Min(2, SoftStartMode));
            if (SoftStartSec < 0 || SoftStartSec > 3) SoftStartSec = 0;
            QuietFrom = Math.Max(0, Math.Min(23, QuietFrom));
            QuietTo = Math.Max(0, Math.Min(23, QuietTo));
            if (QuietMax != 0 && Array.IndexOf(QuietMaxValues, QuietMax) < 0) QuietMax = 0;
            if (NamePlateText != null)
            {
                NamePlateText = NamePlateText.Trim();
                if (NamePlateText.Length == 0) NamePlateText = null;
                else if (NamePlateText.Length > 28) NamePlateText = NamePlateText.Substring(0, 28);
            }
            if (Stickers != null)
            {
                Stickers.RemoveAll(k => k == null || string.IsNullOrEmpty(k.Owner) || float.IsNaN(k.X) || float.IsNaN(k.Y));
                foreach (var k in Stickers) k.Kind = Math.Max(0, Math.Min(StickerKinds - 1, k.Kind));
                if (Stickers.Count > 40) Stickers.RemoveRange(0, Stickers.Count - 40);
                if (Stickers.Count == 0) Stickers = null;
            }
            if (Channels != null)
                foreach (var c in Channels)
                {
                    if (c == null) continue;
                    if (c.EqAlt != null && c.EqAlt.Length != 13) c.EqAlt = null;
                    if (c.EqAlt != null) for (int i = 0; i < c.EqAlt.Length; i++) { float v = c.EqAlt[i]; c.EqAlt[i] = float.IsNaN(v) ? 0f : Math.Max(-EqRange, Math.Min(EqRange, v)); }
                    if (c.EqAlt == null) c.EqIsB = false;
                }
        }

        public const int StickerKinds = 9;
    }
}
