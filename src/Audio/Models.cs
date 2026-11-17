using System;
using System.Collections.Generic;

namespace Mischpult.Audio
{
    /// <summary>Klangbearbeitung eines Kanals: 3-Band-EQ (dB), 10-Band-EQ, Lautstaerke-Ausgleich, Kompressor. Unveraenderlich.</summary>
    /// <summary>3.4.6: weitere Klang-Einstellungen eines Kanals (Stereo-Breite, Bass-Filter, Knall-Bremse). Unveraenderlich.</summary>
    internal sealed class ChannelExtra
    {
        public readonly float Width;     // 1 normal, 0 mono … 2 extra breit
        public readonly int HpHz;        // Bass-Filter (Hochpass) in Hz, 0 = aus
        public readonly bool PopGuard;   // Knall-Bremse
        public readonly int LpHz;        // 3.5: Hoehen-Filter (Tiefpass) in Hz, 0 = aus
        public readonly bool Dialog;     // 3.5: Stimmen nach vorne
        public readonly PeqSet Peq;      // 4.0: parametrischer EQ (null = keiner)
        public readonly GroupFx Group;   // 4.0: EQ/Kompressor der Gruppe (null = keiner)
        public readonly ChannelPro Pro;  // 4.0 (finale): Profi-Ton (null = nichts davon)
        public ChannelExtra(float width, int hpHz, bool popGuard) : this(width, hpHz, popGuard, 0, false) { }
        public ChannelExtra(float width, int hpHz, bool popGuard, int lpHz, bool dialog) : this(width, hpHz, popGuard, lpHz, dialog, null, null) { }
        public ChannelExtra(float width, int hpHz, bool popGuard, int lpHz, bool dialog, PeqSet peq, GroupFx group) : this(width, hpHz, popGuard, lpHz, dialog, peq, group, null) { }
        public ChannelExtra(float width, int hpHz, bool popGuard, int lpHz, bool dialog, PeqSet peq, GroupFx group, ChannelPro pro)
        {
            Pro = pro == null || pro.IsNeutral ? null : pro;
            Width = float.IsNaN(width) ? 1f : Math.Max(0f, Math.Min(2f, width));
            HpHz = hpHz <= 0 ? 0 : Math.Max(20, Math.Min(400, hpHz));
            PopGuard = popGuard;
            LpHz = lpHz <= 0 ? 0 : Math.Max(1000, Math.Min(20000, lpHz));
            Dialog = dialog;
            Peq = peq == null || peq.IsFlat ? null : peq;
            Group = group == null || group.IsOff ? null : group;
        }
        public bool IsNeutral => Math.Abs(Width - 1f) < 0.001f && HpHz == 0 && !PopGuard && LpHz == 0 && !Dialog && Peq == null && Group == null && Pro == null;
        public static bool Same(ChannelExtra a, ChannelExtra b)
        {
            bool na = a == null || a.IsNeutral, nb = b == null || b.IsNeutral;
            if (na || nb) return na && nb;
            return a.Width == b.Width && a.HpHz == b.HpHz && a.PopGuard == b.PopGuard && a.LpHz == b.LpHz && a.Dialog == b.Dialog
                && PeqSet.Same(a.Peq, b.Peq) && GroupFx.Same(a.Group, b.Group) && ChannelPro.Same(a.Pro, b.Pro);
        }
    }

    internal sealed class EqSettings
    {
        public readonly string ChannelId;
        public readonly float Low, Mid, High;
        public readonly bool Comp, Leveler;
        public readonly float[] Graphic;   // 10 Baender (dB) oder null
        public readonly ChannelFx Fx;      // 2.19: Hall/Echo oder null
        public readonly bool Cue;          // 2.19: vorhoeren (vor dem Fader auf dem CUE-Geraet)
        public readonly int Stereo;        // 3.2: 0 normal, 1 mono, 2 links/rechts getauscht
        public readonly int DelayMs;       // 3.2: Verzoegerung (ms)
        public EqSettings(string channelId, float low, float mid, float high, bool comp = false, bool leveler = false, float[] graphic = null, ChannelFx fx = null, bool cue = false)
            : this(channelId, low, mid, high, comp, leveler, graphic, fx, cue, 0, 0) { }

        public EqSettings(string channelId, float low, float mid, float high, bool comp, bool leveler, float[] graphic, ChannelFx fx, bool cue, int stereo, int delayMs)
        {
            ChannelId = channelId; Low = low; Mid = mid; High = high; Comp = comp; Leveler = leveler;
            Graphic = graphic == null ? null : (float[])graphic.Clone();
            Fx = fx == null || fx.IsOff ? null : fx;
            Cue = cue;
            Stereo = Math.Max(0, Math.Min(2, stereo));
            DelayMs = Math.Max(0, Math.Min(1000, delayMs));
        }
        /// <summary>3.4.6: Stereo-Breite, Bass-Filter, Knall-Bremse (null = nichts davon).</summary>
        public ChannelExtra Extra { get; private set; }
        public EqSettings WithExtra(ChannelExtra x) { var e = (EqSettings)MemberwiseClone(); e.Extra = x == null || x.IsNeutral ? null : x; return e; }

        public bool SameAs(EqSettings o) =>
            o != null && o.ChannelId == ChannelId && o.Low == Low && o.Mid == Mid && o.High == High && o.Comp == Comp &&
            o.Leveler == Leveler && SameArray(o.Graphic, Graphic) && ChannelFx.Same(o.Fx, Fx) && o.Cue == Cue && o.Stereo == Stereo && o.DelayMs == DelayMs &&
            ChannelExtra.Same(o.Extra, Extra);

        private static bool SameArray(float[] a, float[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>Zweiter Mix (z. B. fuer OBS) auf einem eigenen Geraet. Unveraenderlich.</summary>
    internal sealed class StreamMix
    {
        public readonly string DeviceId;
        public readonly float Gain;            // Summe (linear)
        public readonly bool Mute;
        private readonly Dictionary<string, float> _sends; // Kanal-ID -> Anteil (linear, nach dem Fader)

        public StreamMix(string deviceId, float gain, bool mute, Dictionary<string, float> sends)
        {
            DeviceId = deviceId; Gain = gain; Mute = mute; _sends = sends ?? new Dictionary<string, float>();
        }

        public float SendOf(string channelId) => channelId != null && _sends.TryGetValue(channelId, out var v) ? v : 0f;
        public IEnumerable<string> Channels => _sends.Keys;

        public bool SameAs(StreamMix o)
        {
            if (o == null || o.DeviceId != DeviceId || o.Gain != Gain || o.Mute != Mute || o._sends.Count != _sends.Count) return false;
            foreach (var kv in _sends) if (!o._sends.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }

    /// <summary>Sprach-Ausgang: Mikro (+ Soundboard) auf ein Geraet, z. B. das virtuelle Kabel fuer Discord. Unveraenderlich.</summary>
    internal sealed class VoiceOut
    {
        public readonly string DeviceId;
        public readonly bool GateOn;
        public readonly float GateDb;
        public readonly bool MicMuted;         // Stummschaltung nur im Sprach-Ausgang
        public readonly VoiceFx Fx;            // Mikro-Effekte (nie null)

        public VoiceOut(string deviceId, bool gateOn, float gateDb, bool micMuted, VoiceFx fx = null)
        {
            DeviceId = deviceId; GateOn = gateOn; GateDb = gateDb; MicMuted = micMuted; Fx = fx ?? VoiceFx.Off;
        }

        public bool SameAs(VoiceOut o) =>
            o != null && o.DeviceId == DeviceId && o.GateOn == GateOn && o.GateDb == GateDb && o.MicMuted == MicMuted && Fx.SameAs(o.Fx);
    }

    /// <summary>Mikro-Effekte im Sprach-Ausgang (unveraenderlich).</summary>
    internal sealed class VoiceFx
    {
        public const float EqRange = 12f, GainRange = 12f;
        public static readonly VoiceFx Off = new VoiceFx(false, false, 0, 0, 0, 0, 0);

        public readonly bool HighPass;   // Rumpelfilter (Brummen, Wind, Tischklopfen unter ca. 90 Hz)
        public readonly bool Clarity;    // "Stimme klarer": Dröhnen absenken, Praesenz und Luft anheben
        public readonly float Low, Mid, High;   // EQ in dB
        public readonly int Comp;        // 0 aus, 1 leicht, 2 stark
        public readonly float GainDb;    // Pegel im Sprach-Ausgang

        public VoiceFx(bool highPass, bool clarity, float low, float mid, float high, int comp, float gainDb)
        {
            HighPass = highPass; Clarity = clarity;
            Low = Clamp(low, EqRange); Mid = Clamp(mid, EqRange); High = Clamp(high, EqRange);
            Comp = Math.Max(0, Math.Min(2, comp));
            GainDb = Clamp(gainDb, GainRange);
        }

        private static float Clamp(float v, float r) => float.IsNaN(v) ? 0f : Math.Max(-r, Math.Min(r, v));

        public bool IsOff => !HighPass && !Clarity && Low == 0 && Mid == 0 && High == 0 && Comp == 0 && GainDb == 0;
        public bool HasEq => Low != 0 || Mid != 0 || High != 0;

        public bool SameAs(VoiceFx o) =>
            o != null && o.HighPass == HighPass && o.Clarity == Clarity && o.Low == Low && o.Mid == Mid && o.High == High &&
            o.Comp == Comp && o.GainDb == GainDb;
    }

    /// <summary>Ein dekodierter Sound (48 kHz, Stereo, Float).</summary>
    internal sealed class SoundClip
    {
        public string Name;
        public float[] Data;
        public int Frames;
        public double Seconds => Frames / 48000.0;
    }

    /// <summary>Gewuenschter Zustand fuer alle Apps eines Kanals. Unveraenderlich – bei jeder
    /// Aenderung entsteht ein neues Objekt, damit die Engine per Referenzvergleich Aenderungen erkennt.</summary>
    internal sealed class AppPolicy
    {
        public readonly float Volume;        // linearer Faktor 0..1 (ISimpleAudioVolume)
        public readonly bool Mute;
        public readonly float Pan;           // -1 (links) .. 0 .. +1 (rechts)
        public readonly string DeviceId;     // null = Windows-Standardgeraet
        public readonly bool EnforceDevice;  // true: auch "Standard" aktiv durchsetzen
        public readonly EqSettings Eq;       // null = EQ aus (direkter Weg, ohne Verzoegerung)
        public readonly string ChannelId;    // Kanal, zu dem die Apps gehoeren (fuer Aufnahme)
        public readonly float AppGain;       // eigene Lautstaerke der App im Kanal (0..1, 1 = wie der Kanal)

        public AppPolicy(float volume, bool mute, float pan, string deviceId, bool enforceDevice, EqSettings eq = null, string channelId = null, float appGain = 1f)
        {
            Volume = volume; Mute = mute; Pan = pan; DeviceId = deviceId; EnforceDevice = enforceDevice; Eq = eq;
            ChannelId = channelId ?? eq?.ChannelId;
            AppGain = Math.Max(0f, Math.Min(1f, appGain));
        }

        /// <summary>Dieselben Kanalwerte mit eigener App-Lautstaerke.</summary>
        public AppPolicy WithAppGain(float gain) => new AppPolicy(Volume, Mute, Pan, DeviceId, EnforceDevice, Eq, ChannelId, gain);

        public bool SameAs(float volume, bool mute, float pan, string deviceId, bool enforce, EqSettings eq, string channelId) =>
            Volume == volume && Mute == mute && Pan == pan && EnforceDevice == enforce && ChannelId == channelId &&
            string.Equals(DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
            (eq == null ? Eq == null : eq.SameAs(Eq));
    }

    internal sealed class PolicyTable
    {
        public static readonly PolicyTable Empty = new PolicyTable(new Dictionary<string, AppPolicy>(), null, new AppPolicy[0]);

        private readonly Dictionary<string, AppPolicy> _map;
        public readonly AppPolicy Fallback;
        /// <summary>Alle Kanaele mit eingeschaltetem EQ (auch ohne laufende Apps).</summary>
        public readonly AppPolicy[] EqChannels;

        public PolicyTable(Dictionary<string, AppPolicy> map, AppPolicy fallback, AppPolicy[] eqChannels)
        {
            _map = map; Fallback = fallback; EqChannels = eqChannels ?? new AppPolicy[0];
        }

        public AppPolicy Get(string appKey) =>
            appKey != null && _map.TryGetValue(appKey, out var p) ? p : Fallback;
    }

    internal sealed class DeviceState
    {
        public string Id;
        public string Name;        // kurz, z. B. "Lautsprecher"
        public string FullName;    // z. B. "Lautsprecher (Realtek(R) Audio)"
        public bool IsDefault;
        public float Volume;       // Skalar 0..1 wie im Windows-Lautstaerkeregler
        public float VolumeDb;
        public float MinDb = -96f, MaxDb;   // 3.4.3: Bereich des Geraets (Windows: GetVolumeRange)
        public bool Mute;
        public float PeakL, PeakR;
        /// <summary>3.5: Bauform (Windows: 1 Lautsprecher, 3 Kopfhoerer, 5 Headset, 9 HDMI/Bildschirm …), -1 = unbekannt.</summary>
        public int FormFactor = -1;
        public bool Bluetooth, Usb;
        /// <summary>3.5: Format des Geraets, z. B. "48 kHz · 24 Bit" (null = unbekannt).</summary>
        public string FormatText;
        /// <summary>4.0: Pegel jedes Kanals (5.1/7.1: L, R, C, LFE, …), null = Stereo bzw. aus.</summary>
        public float[] ChanPeaks;
    }

    internal sealed class AppState
    {
        public string Key;         // z. B. "discord.exe" oder "#system"
        public string Name;
        public string Path;
        public bool Active;        // mindestens ein Stream geoeffnet
        public float PeakL, PeakR; // hoerbarer Pegel (nach Kanal-Lautstaerke und Stumm)
        public float PreL, PreR;   // Pegel, den die App eigentlich abspielt (vor Lautstaerke und Stumm)
        public string[] DeviceIds; // Geraete, auf denen die App gerade Sessions hat
        public bool Captured;      // laeuft ueber den EQ (Pegel kommt dann vom Kanal)
        public bool Metered;       // Pegel direkt gemessen (sonst Windows-Session-Pegel)
    }

    internal sealed class EngineSnapshot
    {
        public static readonly EngineSnapshot Empty = new EngineSnapshot
        {
            Devices = new DeviceState[0], Apps = new AppState[0], Structure = "",
            ChannelPeaks = new Dictionary<string, float[]>()
        };

        public DeviceState[] Devices;
        public AppState[] Apps;
        public bool RoutingSupported;
        public string Error;
        /// <summary>Aendert sich, wenn Apps/Geraete dazukommen, verschwinden oder den Status wechseln.</summary>
        public string Structure;
        /// <summary>Pegel der EQ-Kanaele, Schluessel = Kanal-ID, Wert = [L, R] nach dem Fader, [2..3] davor (nach EQ).</summary>
        public Dictionary<string, float[]> ChannelPeaks;
        public bool EqSupported;
        public string EqStatus;
        /// <summary>Standard-Aufnahmegeraet (Mikrofon) oder null.</summary>
        public DeviceState Mic;
        public DeviceState[] CaptureDevices = new DeviceState[0];
        /// <summary>Mischpult haelt das Mikro offen, damit der Pegel immer angezeigt wird.</summary>
        public bool MicLive;
        /// <summary>Pegel des Stream-Mix [L, R].</summary>
        public float[] StreamPeak = new float[2];
        /// <summary>Mikro-Pegel im Sprach-Ausgang (vor Rauschsperre und Stummschaltung) und am Ausgang.</summary>
        public float VoicePeak, VoiceOutPeak;
        public bool VoiceActive;
        public string VoiceStatus;
        public int SoundsPlaying;
        /// <summary>Spektrum (dB pro Band) des angeforderten Kanals oder null.</summary>
        public float[] Spectrum;
        /// <summary>Spektrum (dB pro Band) dessen, was am Ausgang laeuft (Loopback) – nur wenn angefordert, sonst null.</summary>
        public float[] MasterSpectrum;
        /// <summary>Goniometer: die letzten Stereo-Werte (L, R abwechselnd) des Spektrum-Ausgangs – nur wenn angefordert, sonst null.</summary>
        public float[] Scope;
        /// <summary>Phasen-Korrelation dazu (−1 gegenphasig … 0 breit … +1 mono).</summary>
        public float ScopeCorr;
        /// <summary>Lautheit des Standardgeraets [Momentan, Kurzzeit, Integriert] in LUFS – nur wenn angefordert, sonst null.</summary>
        public float[] Loudness;
        /// <summary>3.5: Phasen-Korrelation des Standardgeraets ueber 3 s (−1 … +1), NaN = nicht gemessen.</summary>
        public float Correlation = float.NaN;
        /// <summary>3.5: Bass-Pegel des Standardgeraets (linear, schnell), −1 = nicht gemessen.</summary>
        public float MasterBass = -1f;
        /// <summary>3.5: Pegel der Einschlaf-Geraeusche [L, R].</summary>
        public float[] NoisePeak;
        /// <summary>3.5: geschaetzte Verzoegerung des EQ-Wegs in ms (0 = unbekannt).</summary>
        public int EqLatencyMs;
        /// <summary>3.4.6 Takt-Lampe: [BPM, ms seit dem letzten Schlag, Sicherheit] (nur mit Takt-Lampe an), sonst null.</summary>
        public float[] Beat;
        /// <summary>3.4.6: [True-Peak dBTP, Spitze dBFS] des Standardgeraets ueber 3 s (nur mit True-Peak/Dynamik an), sonst null.</summary>
        public float[] MasterPeaks;
        /// <summary>2.16.1: Lautheit weiterer Ausgaenge (nach SetLoudnessDevices) je Geraete-ID – null = keins gemessen.</summary>
        public Dictionary<string, float[]> LoudnessByDevice;
        /// <summary>2.17: Lautheit je Kanal auf dem EQ-Weg (vor dem Fader) – nur angeforderte, sonst null/leer.</summary>
        public Dictionary<string, float[]> ChannelLoudness;
        /// <summary>2.17: Spektrum je Kanal auf dem EQ-Weg (vor dem Fader).</summary>
        public Dictionary<string, float[]> ChannelSpectra;
        /// <summary>2.17: Lautheit je App (aus der Pegel-Messung, so wie die App spielt).</summary>
        public Dictionary<string, float[]> AppLoudness;
        /// <summary>2.17: Spektrum je App (aus der Pegel-Messung).</summary>
        public Dictionary<string, float[]> AppSpectra;
        /// <summary>Aktuelle Verstaerkung des Lautstaerke-Ausgleichs pro Kanal (dB).</summary>
        public Dictionary<string, float> LevelerGain = new Dictionary<string, float>();
        /// <summary>Wie viel dB der Kompressor je Kanal gerade absenkt (positiv), nur Kanaele mit COMP.</summary>
        public Dictionary<string, float> CompReduction = new Dictionary<string, float>();

        // ---- 3.0.2: Pegel lueckenlos (siehe LevelFlow)
        /// <summary>Die Oberflaeche hat diesen Schnappschuss abgeholt.</summary>
        public volatile bool Taken;
        /// <summary>Seit wann (ms, Engine-Uhr) hier Pegel nicht abgeholter Schnappschuesse mit drinstecken.</summary>
        public long AccSinceMs;
        /// <summary>Laufende Nummer (zaehlt mit jedem neuen Schnappschuss hoch).</summary>
        public long Seq;
    }

    /// <summary>
    /// 3.0.3: kein Knacken, wenn ein Programm anfaengt zu spielen – neue Sessions sofort absenken, auf dem EQ-Weg nur
    /// plausibel anheben, Puffer weich aus- und einblenden; Pegel-Messung bleibt nach einer Pause bereit. An/aus in den Einstellungen.
    /// </summary>
    internal static class CleanStart
    {
        public static volatile bool On = true;
        public static volatile bool MeterKeep = true;
        /// <summary>3.4: neue Apps blenden so viele ms ein (0 = aus, wie bisher sofort).</summary>
        public static volatile int NewAppFadeMs;
        /// <summary>So lange nach dem letzten Abspielen bleibt die Pegel-Messung einer App bereit (ms).</summary>
        public const long KeepMs = 10 * 60 * 1000;
    }

    /// <summary>
    /// 3.0.2: Pegel lueckenlos weitergeben. Die Engine misst etwa alle 10 ms, die Oberflaeche zeichnet 60–165x pro Sekunde –
    /// bisher sah sie nur den jeweils letzten Messwert, alles dazwischen ging verloren (die Pegel zuckten unregelmaessig).
    /// Jetzt steckt in jedem Schnappschuss die hoechste Spitze seit dem letzten abgeholten. An/aus in den Einstellungen.
    /// </summary>
    internal static class LevelFlow
    {
        public static volatile bool Seamless = true;
        /// <summary>3.1.1: Pegel alle 5 statt 10 ms messen (weniger Verzoegerung zum Ton).</summary>
        public static volatile bool FastPoll = true;
        /// <summary>3.1.2: Kanal-Pegel sofort weitergeben, sobald Windows ein neues Stueck Ton der App liefert.</summary>
        public static volatile bool ChannelFast = true;
        /// <summary>Laenger als das wird nicht gesammelt (Oberflaeche haengt oder ist pausiert).</summary>
        public const long MaxAccMs = 150;

        /// <summary>Spitzen des nicht abgeholten Schnappschusses prev in next uebernehmen (Maximum). Nur next wird veraendert.</summary>
        public static void Merge(EngineSnapshot prev, EngineSnapshot next, long nowMs)
        {
            next.AccSinceMs = nowMs;
            if (!Seamless || prev == null || next == null || prev.Taken || nowMs - prev.AccSinceMs > MaxAccMs) return;
            next.AccSinceMs = prev.AccSinceMs;
            if (prev.Apps != null && next.Apps != null)
                foreach (var a in next.Apps)
                {
                    var o = Find(prev.Apps, a.Key);
                    if (o == null) continue;
                    a.PeakL = Math.Max(a.PeakL, o.PeakL); a.PeakR = Math.Max(a.PeakR, o.PeakR);
                    a.PreL = Math.Max(a.PreL, o.PreL); a.PreR = Math.Max(a.PreR, o.PreR);
                }
            if (prev.Devices != null && next.Devices != null)
                foreach (var d in next.Devices)
                {
                    DeviceState o = null;
                    foreach (var x in prev.Devices) if (string.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase)) { o = x; break; }
                    if (o == null) continue;
                    d.PeakL = Math.Max(d.PeakL, o.PeakL); d.PeakR = Math.Max(d.PeakR, o.PeakR);
                }
            if (prev.Mic != null && next.Mic != null && string.Equals(prev.Mic.Id, next.Mic.Id, StringComparison.OrdinalIgnoreCase))
            {
                next.Mic.PeakL = Math.Max(next.Mic.PeakL, prev.Mic.PeakL);
                next.Mic.PeakR = Math.Max(next.Mic.PeakR, prev.Mic.PeakR);
            }
            if (prev.ChannelPeaks != null && prev.ChannelPeaks.Count > 0)
                next.ChannelPeaks = MaxOf(next.ChannelPeaks, prev.ChannelPeaks);
            if (prev.StreamPeak != null && prev.StreamPeak.Length >= 2)
            {
                var s = next.StreamPeak != null && next.StreamPeak.Length >= 2 ? next.StreamPeak : new float[2];
                next.StreamPeak = new[] { Math.Max(s[0], prev.StreamPeak[0]), Math.Max(s[1], prev.StreamPeak[1]) };
            }
            next.VoicePeak = Math.Max(next.VoicePeak, prev.VoicePeak);
            next.VoiceOutPeak = Math.Max(next.VoiceOutPeak, prev.VoiceOutPeak);
        }

        private static AppState Find(AppState[] apps, string key)
        {
            foreach (var a in apps) if (a.Key == key) return a;
            return null;
        }

        /// <summary>Neues Woerterbuch mit dem Maximum je Eintrag (die beiden alten bleiben unveraendert – sie gehoeren evtl. noch jemand anderem).</summary>
        public static Dictionary<string, float[]> MaxOf(Dictionary<string, float[]> a, Dictionary<string, float[]> b)
        {
            var d = a == null ? new Dictionary<string, float[]>() : new Dictionary<string, float[]>(a);
            if (b == null) return d;
            foreach (var kv in b)
            {
                if (kv.Value == null) continue;
                if (!d.TryGetValue(kv.Key, out var cur) || cur == null) { d[kv.Key] = (float[])kv.Value.Clone(); continue; }
                var m = new float[Math.Max(cur.Length, kv.Value.Length)];
                for (int i = 0; i < m.Length; i++)
                    m[i] = Math.Max(i < cur.Length ? cur[i] : 0f, i < kv.Value.Length ? kv.Value[i] : 0f);
                d[kv.Key] = m;
            }
            return d;
        }
    }

    internal interface IAudioBackend : IDisposable
    {
        EngineSnapshot Snapshot { get; }
        void SetPolicy(PolicyTable table);
        void SetDeviceVolume(string deviceId, float scalar);
        /// <summary>3.4.3: Geraete-Lautstaerke in dB setzen (wird auf den Bereich des Geraets begrenzt).</summary>
        void SetDeviceVolumeDb(string deviceId, float db);
        void SetDeviceMute(string deviceId, bool mute);
        void ResetAllAppRouting();
        void SetDefaultDevice(string deviceId);
        void SetMicVolume(float scalar);
        void SetMicMute(bool mute);
        void SetMicLiveMeter(bool on);
        /// <summary>Wartet, bis alle anstehenden Aenderungen angewendet sind.</summary>
        void Flush(int timeoutMs);
        /// <summary>Lautstaerke-Aenderungen und Stummschalten weich ueberblenden statt springen.</summary>
        void SetSoftFade(bool on);
        /// <summary>Gehoerschutz: hoechster Pegel in dBFS (0 = aus).</summary>
        void SetHearingCap(float db);
        /// <summary>
        /// Startet eine Aufnahme in <paramref name="writer"/>. channelId == null: alles, was auf dem Ausgabegeraet
        /// laeuft (deviceId null = Windows-Standardgeraet). Liefert eine Fehlermeldung oder null.
        /// </summary>
        string StartRecording(string channelId, string deviceId, WavWriter writer);
        void StopRecording();
        /// <summary>Fehler, mit dem die laufende Aufnahme abgebrochen ist (null = laeuft bzw. keine).</summary>
        string RecordingError { get; }
        /// <summary>Stream-Mix (null = aus).</summary>
        void SetStreamMix(StreamMix mix);
        /// <summary>Sprach-Ausgang (null = aus).</summary>
        void SetVoiceOut(VoiceOut voice);
        void PlaySound(SoundClip clip, float gain, bool me, bool stream, bool voice);
        /// <summary>3.2: Sound nur fuer mich auf einem bestimmten Ausgabegeraet (null = Standardgeraet), z. B. Testton.</summary>
        void PlaySoundOn(SoundClip clip, float gain, string deviceId);
        void StopSounds();
        /// <summary>Spektrum dieses Kanals berechnen (null = keins).</summary>
        void SetSpectrumChannel(string channelId);
        /// <summary>
        /// Spektrum dieser Ausgaenge berechnen – fuer die Spektrum-Anzeige. null/leer = aus, Eintrag null = Windows-Standardgeraet;
        /// bei mehreren zaehlt je Band der lauteste.
        /// </summary>
        void SetMasterSpectrum(string[] deviceIds);
        /// <summary>Goniometer-Daten (vom Spektrum-Ausgang) in den Schnappschuss legen.</summary>
        void SetScope(bool on);
        /// <summary>Lautheit (LUFS) des Windows-Standardgeraets messen.</summary>
        void SetLoudness(bool on);
        /// <summary>Integrierte Lautheit neu beginnen.</summary>
        void ResetLoudness();
        /// <summary>2.16.1: Lautheit dieser Ausgaenge zusaetzlich messen (null/leer = keine weiteren).</summary>
        void SetLoudnessDevices(string[] deviceIds);
        /// <summary>2.17: Lautheit bzw. Spektrum dieser Kanaele messen (fuers Anzeigefeld), null = keine.</summary>
        void SetChannelAnalysis(string[] loudChannels, string[] specChannels);
        /// <summary>
        /// 2.15.1: fastLevels = Pegel der Ausgaenge und des Mikros mit eigenem schnellen Faden messen;
        /// boost = Mess- und Engine-Faden bekommen Vorrang (ruhige Pegel auch beim Spielen).
        /// </summary>
        void SetPerf(bool fastLevels, bool boost);
        /// <summary>Aus dem UI-Takt: frische Pegel (Ausgaenge, Mikro) in den Schnappschuss legen.</summary>
        void RefreshLevels(EngineSnapshot snap);
        /// <summary>2.16: Pegel in Echtzeit (alle 10 ms messen und weitergeben).</summary>
        void SetRealtime(bool on);
        /// <summary>2.16: Goniometer aus diesen Ausgaengen (Eintrag null = Standardgeraet), null = wie das Spektrum.</summary>
        void SetScopeSource(string[] deviceIds);
        /// <summary>2.19: EQ/Raumklang je Ausgang (Schluessel "" = Standardgeraet, sonst Geraete-ID), null = aus. Wirkt auf alles, was ueber den EQ-Weg laeuft.</summary>
        void SetOutputFx(Dictionary<string, OutputFx> fx);
        /// <summary>2.19: Vorhoeren (null = aus).</summary>
        void SetCue(CueMix cue);
        /// <summary>3.5: Ton des Standardgeraets zusaetzlich auf einem zweiten Geraet (null = aus).</summary>
        void SetMirror(string targetDeviceId, int delayMs);
        /// <summary>3.5: Einschlaf-Geraeusch (Art, Lautstaerke linear; 0 = aus).</summary>
        void SetNoise(int kind, float gain);
        /// <summary>2.19: Mehrspur-Aufnahme: je Kanal (auf dem EQ-Weg) eine Datei. Liefert eine Fehlermeldung oder null.</summary>
        string StartMultitrack(Dictionary<string, WavWriter> writers);
        void StopMultitrack();
    }

    internal static class AppKeys
    {
        public const string System = "#system";

        public static string FromExe(string exeNameOrPath)
        {
            if (string.IsNullOrEmpty(exeNameOrPath)) return null;
            string name = System_IO_GetFileName(exeNameOrPath).ToLowerInvariant();
            if (!name.EndsWith(".exe")) name += ".exe";
            return name;
        }

        private static string System_IO_GetFileName(string p)
        {
            int i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
            return i >= 0 ? p.Substring(i + 1) : p;
        }
    }
}
