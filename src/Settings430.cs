using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Mischpult
{
    /// <summary>4.3: eine gespeicherte Ducking-Einstellung ("Zocken", "Stream", "Film" …).</summary>
    [DataContract]
    internal sealed class DuckPreset
    {
        [DataMember] public string Name;
        [DataMember] public bool On;
        [DataMember] public string Trigger;
        [DataMember] public List<string> Triggers;
        [DataMember] public List<string> Targets;
        [DataMember] public float Amount, Threshold, Hold;
        [DataMember] public bool NoLeak, MinTalkOn, FadeOn;
        [DataMember] public int MinTalkMs, DownMs, UpMs;
    }

    internal sealed partial class MixerSettings
    {
        // ---- 4.3 (alles Standard aus)
        [DataMember] public bool DuckWhoOn;          // 14 DUCKING-Taste und Kanal zeigen, wer gerade ausloest
        [DataMember] public bool DuckHoldKeyOn;      // 10 Taste zum Halten (Tastenkuerzel "Ducking halten")
        [DataMember] public bool DuckPresetsOn;      // 12 Ducking-Vorlagen (Rechtsklick auf DUCKING)
        [DataMember] public List<DuckPreset> DuckPresets;
        [DataMember] public bool DuckWhenOn;         // 13 Ducking nur, wenn …
        [DataMember] public int DuckWhenMode;        //     0 ein Spiel laeuft, 1 zu bestimmten Uhrzeiten, 2 beides
        [DataMember] public int DuckWhenFrom;        //     Stunde 0..23 (Standard 18)
        [DataMember] public int DuckWhenTo;          //     Stunde 0..23 (Standard 2), ueber Mitternacht erlaubt
        [DataMember] public bool DuckWhenSet;        //     Uhrzeiten schon einmal gesetzt
        [DataMember] public bool AppProfilesOn;      // 1 Profil auch fuer andere Apps (nicht nur Spiele)
        [DataMember] public bool ChanTimerOn;        // 2 Timer pro Kanal
        [DataMember] public bool SysQuietGameOn;     // 3 Windows-Toene stumm, solange ein Spiel laeuft
        [DataMember] public bool BatteryOn;          // 5 Akku des Bluetooth-Kopfhoerers in der Kopfleiste

        /// <summary>4.3: das Soundboard als Ausloeser des Duckings.</summary>
        public const string DuckSbId = "@sb";

        public static readonly string[] DuckWhenNames = { "Wenn ein Spiel läuft", "Zu bestimmten Uhrzeiten", "Beides" };
        public static readonly int[] ChanTimerMinutes = { 15, 30, 45, 60, 90, 120 };

        /// <summary>Ist es gerade in der Zeit von DuckWhenFrom bis DuckWhenTo (ueber Mitternacht erlaubt)?</summary>
        public bool InDuckHours(DateTime now)
        {
            int h = now.Hour, f = DuckWhenFrom, t = DuckWhenTo;
            if (f == t) return true;
            return f < t ? h >= f && h < t : h >= f || h < t;
        }

        /// <summary>Aktuelle Ducking-Einstellung als Vorlage.</summary>
        public DuckPreset CaptureDuck(string name) => new DuckPreset
        {
            Name = name, On = DuckOn, Trigger = DuckTrigger, Triggers = DuckTriggers?.ToList(), Targets = DuckTargets?.ToList(),
            Amount = DuckAmount, Threshold = DuckThreshold, Hold = DuckHold, NoLeak = DuckNoLeakFilter,
            MinTalkOn = DuckMinTalkOn, MinTalkMs = DuckMinTalkMs, FadeOn = DuckFadeOn, DownMs = DuckDownMs, UpMs = DuckUpMs,
        };

        /// <summary>Vorlage uebernehmen (danach Normalize aufrufen).</summary>
        public void ApplyDuck(DuckPreset p)
        {
            if (p == null) return;
            DuckOn = p.On; DuckTrigger = p.Trigger; DuckTriggers = p.Triggers?.ToList(); DuckTargets = p.Targets?.ToList() ?? new List<string>();
            DuckAmount = p.Amount; DuckThreshold = p.Threshold; DuckHold = p.Hold; DuckNoLeakFilter = p.NoLeak;
            DuckMinTalkOn = p.MinTalkOn; DuckMinTalkMs = p.MinTalkMs; DuckFadeOn = p.FadeOn; DuckDownMs = p.DownMs; DuckUpMs = p.UpMs;
        }

        private void Normalize430()
        {
            DuckWhenMode = Math.Max(0, Math.Min(DuckWhenNames.Length - 1, DuckWhenMode));
            if (!DuckWhenSet) { DuckWhenFrom = 18; DuckWhenTo = 2; DuckWhenSet = true; }
            DuckWhenFrom = Math.Max(0, Math.Min(23, DuckWhenFrom));
            DuckWhenTo = Math.Max(0, Math.Min(23, DuckWhenTo));
            if (DuckPresets != null)
            {
                DuckPresets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
                foreach (var p in DuckPresets) p.Name = p.Name.Trim().Length > 30 ? p.Name.Trim().Substring(0, 30) : p.Name.Trim();
                DuckPresets = DuckPresets.GroupBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.Last()).Take(12).ToList();
                if (DuckPresets.Count == 0) DuckPresets = null;
            }
        }
    }
}
