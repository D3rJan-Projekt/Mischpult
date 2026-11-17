using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.Audio
{
    [ComVisible(true)]
    internal sealed class SessionNotifier : IAudioSessionNotification
    {
        private readonly Action<IntPtr> _onCreated;
        public SessionNotifier(Action<IntPtr> onCreated) { _onCreated = onCreated; }

        public int OnSessionCreated(IntPtr newSession)
        {
            try { _onCreated(newSession); } catch { }
            return 0;
        }
    }

    /// <summary>
    /// Echte Windows-Audio-Engine. Laeuft komplett in einem eigenen MTA-Thread:
    /// erkennt Ausgabegeraete und App-Sessions, liest Pegel, setzt Lautstaerke/Mute/Balance
    /// und leitet Apps auf andere Ausgabegeraete um. Die Oberflaeche liest nur Snapshots.
    /// </summary>
    internal sealed partial class AudioEngine : IAudioBackend
    {
        private static Guid EventContext = new Guid("5E7A1C3B-2D4F-4A6B-9C8D-0E1F2A3B4C5D");
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        private static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        private sealed class DeviceEntry
        {
            public string Id, Name, FullName;
            public IMMDevice Device;
            public IAudioEndpointVolume Volume;
            public IAudioMeterInformation Meter;
            public float[] MeterBuf;
            public IAudioSessionManager2 Manager;
            public SessionNotifier Notifier;
            public float Vol, VolDb;
            public float MinDb = -96f, MaxDb;   // 3.4.3
            public bool Mute;
            public float PeakL, PeakR;
            public int FormFactor = -1;   // 3.5
            public bool Bluetooth, Usb;
            public string FormatText;
            public float[] SurBuf;   // 4.0: Surround-Pegel
        }

        private sealed class MicEntry
        {
            public string Id, Name, FullName;
            public IMMDevice Device;
            public IAudioEndpointVolume Volume;
            public IAudioMeterInformation Meter;
            public float[] MeterBuf;
            public float Vol, VolDb;
            public bool Mute;
            public float PeakL, PeakR;
            public IAudioClient LiveClient;
            public IAudioCaptureClient LiveCapture;
            public long LiveRetryAt;
        }

        private sealed class SessionEntry
        {
            public string InstanceId, DeviceId, AppKey, Name, Path;
            public uint Pid;
            public IAudioSessionControl2 Control;
            public ISimpleAudioVolume Volume;
            public IChannelAudioVolume Channels;
            public IAudioMeterInformation Meter;
            public uint ChannelCount;
            public float[] MeterBuf;
            public AudioSessionState State;
            public float PeakL, PeakR;  // hoerbar
            public float PreL, PreR;    // vor Lautstaerke und Stumm
            public AppPolicy Applied;
            // Fuer den Vorher-Pegel: Lautstaerken der letzten 160 ms (die Messung hinkt Aenderungen hinterher)
            public readonly float[] HistL = new float[16], HistR = new float[16];   // Lautstaerke x Balance je Seite
            public readonly long[] HistMs = new long[16];
            public int HistPos;
            public long GainChangedMs = -10000;
            public int PreVotes;        // Hinweise, dass die Prozess-Messung vor der Lautstaerke misst
            public bool MeterPreVolume;
            public bool RoutingDirty;
            public bool HasApplied;
            public float AppliedVolume;
            public bool AppliedMute;
            public float AppliedPan;
            public bool Dsp;            // laeuft ueber den EQ-Mischer (Session auf -80 dB)
            public long FadeInUntil;    // 3.4: blendet gerade ein (neue App)
            public long LastActiveMs;   // zuletzt aktiv (fuer die Pegel-Messung)
            public bool CaptureMeter;   // Pegel kommt aus der Prozess-Messung statt vom Windows-Session-Pegel
            public readonly VolumeFade Fade = new VolumeFade(); // hoerbare Lautstaerke 0..1 (ohne Gehoerschutz)
            public SessionGuard Guard;  // 3.4.1: meldet Aenderungen von aussen
        }

        private sealed class ProcInfo { public string Key, Name, Path; }

        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly ConcurrentDictionary<string, float> _pendingDeviceVolume = new ConcurrentDictionary<string, float>();
        private volatile bool _running = true;
        private volatile bool _sessionsDirty = true;
        private volatile EngineSnapshot _snapshot = EngineSnapshot.Empty;
        private volatile PolicyTable _policy = PolicyTable.Empty;

        // Nur im Engine-Thread benutzt:
        private IMMDeviceEnumerator _enumerator;
        private AppRouting _routing;
        private bool _routingSupported;
        private string _defaultId;
        private string _error;
        private readonly Dictionary<string, DeviceEntry> _devices = new Dictionary<string, DeviceEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionEntry> _sessions = new Dictionary<string, SessionEntry>(StringComparer.Ordinal);
        private readonly Dictionary<uint, ProcInfo> _procCache = new Dictionary<uint, ProcInfo>();
        private readonly Dictionary<uint, string> _routedPid = new Dictionary<uint, string>();
        private readonly Dictionary<string, string> _exeNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly uint _ownPid = (uint)Process.GetCurrentProcess().Id;
        private DspMixer _dsp;
        private PolicyTable _lastTable;
        private Dictionary<uint, string> _lastWanted = new Dictionary<uint, string>();
        private volatile bool _forceDeviceScan;
        private MicEntry _mic;
        private volatile bool _micLive;
        private readonly Dictionary<string, string[]> _captureNames = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private List<string> _captureIds = new List<string>();
        private const string MicKey = "#mic";
        /// <summary>Session-Prozesse, unter denen weitere Session-Prozesse laufen. Deren Prozess-Loopback
        /// wuerde den Ton der Kindprozesse mit aufnehmen (doppelt) – sie laufen deshalb immer direkt.</summary>
        private volatile HashSet<uint> _treeConflicts = new HashSet<uint>();
        private volatile Dictionary<uint, uint> _parentMap = new Dictionary<uint, uint>();
        private HashSet<uint> _lastMeterPids = new HashSet<uint>();
        private long _nextMeterUpdate;
        private readonly Stopwatch _engineClock = Stopwatch.StartNew();
        private volatile bool _softFade = true;
        private bool _final;                           // nach Flush: Werte ohne Blende und ohne Gehoerschutz
        private volatile float _capDb;                 // Gehoerschutz in dBFS, 0 = aus
        private readonly Dictionary<string, CapLimiter> _cap = new Dictionary<string, CapLimiter>(StringComparer.Ordinal);
        private long _lastCapMs;
        // Aufnahme
        private volatile string _recChannel;           // aufgenommener Kanal (Aufnahme ueber den EQ-Mischer)
        private HashSet<uint> _lastRecPids = new HashSet<uint>();
        private LoopbackRecorder _loopRec;

        public AudioEngine()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Mischpult Audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public EngineSnapshot Snapshot => _snapshot;

        public void SetPolicy(PolicyTable table)
        {
            _policy = table ?? PolicyTable.Empty;
            _policyKick = true;
            _wake.Set();
        }

        // ---- 2.16 Pegel in Echtzeit: Pegel alle 10 ms lesen und weitergeben (Rest wie bisher alle ~20 ms)
        private volatile bool _realtime = true, _policyKick = true;
        private long _heavyAtMs = -1000;
        private float[] _heavySpec, _heavyScope, _heavyLoud, _heavyPeaks, _heavyBeat;
        private float _heavyCorr350 = float.NaN, _heavyBass = -1f;
        private float _heavyCorr;

        public void SetRealtime(bool on)
        {
            _realtime = on;
            _wake.Set();
        }

        public void SetDeviceVolume(string deviceId, float scalar)
        {
            if (deviceId == null) return;
            _pendingDeviceDb.TryRemove(deviceId, out _);
            _pendingDeviceVolume[deviceId] = Clamp01(scalar);
            _wake.Set();
        }

        public void SetDeviceMute(string deviceId, bool mute)
        {
            Post(() =>
            {
                if (deviceId != null && _devices.TryGetValue(deviceId, out var d) && d.Volume != null)
                    d.Volume.SetMute(mute, ref EventContext);
            });
        }

        private readonly ConcurrentDictionary<string, float> _pendingDeviceDb = new ConcurrentDictionary<string, float>();

        /// <summary>3.4.3: Lautstaerke in dB (Ausgaenge in dB wie am Mischpult).</summary>
        public void SetDeviceVolumeDb(string deviceId, float db)
        {
            if (deviceId == null || float.IsNaN(db)) return;
            _pendingDeviceVolume.TryRemove(deviceId, out _);
            _pendingDeviceDb[deviceId] = db;
            _wake.Set();
        }

        public void SetMicVolume(float scalar)
        {
            _pendingDeviceVolume[MicKey] = Clamp01(scalar);
            _wake.Set();
        }

        /// <summary>
        /// Mikro stumm/an – fuer das Standard-Mikro und (falls anders) das Standard-Kommunikationsmikro, das
        /// Sprach-Apps wie Discord oft nutzen. Beim Einschalten werden auch frueher stumm geschaltete Mikros
        /// wieder angeschaltet (z. B. nach einem Geraetewechsel waehrend Push-to-Talk).
        /// </summary>
        public void SetMicMute(bool mute)
        {
            Post(() =>
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_mic != null) ids.Add(_mic.Id);
                string comm = DefaultCaptureId(ERole.eCommunications);
                if (comm != null) ids.Add(comm);
                if (!mute) foreach (var id in _mutedMics) ids.Add(id);
                foreach (var id in ids) SetCaptureMute(id, mute);
                if (mute) foreach (var id in ids) _mutedMics.Add(id);
                else _mutedMics.Clear();
            });
        }

        private readonly HashSet<string> _mutedMics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string DefaultCaptureId(ERole role)
        {
            try
            {
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, role, out IMMDevice d) != 0 || d == null) return null;
                d.GetId(out string id);
                ComUtil.Release(d);
                return id;
            }
            catch { return null; }
        }

        private void SetCaptureMute(string id, bool mute)
        {
            if (_mic != null && string.Equals(_mic.Id, id, StringComparison.OrdinalIgnoreCase) && _mic.Volume != null)
            {
                try { _mic.Volume.SetMute(mute, ref EventContext); _mic.Mute = mute; } catch { }
                return;
            }
            IMMDevice d = null;
            IAudioEndpointVolume v = null;
            try
            {
                if (_enumerator.GetDevice(id, out d) != 0 || d == null) return;
                v = Activate<IAudioEndpointVolume>(d, IID_IAudioEndpointVolume);
                v?.SetMute(mute, ref EventContext);
            }
            catch { }
            finally { ComUtil.Release(v); ComUtil.Release(d); }
        }

        public void SetMicLiveMeter(bool on)
        {
            _micLive = on;
            _wake.Set();
        }

        public void SetSoftFade(bool on)
        {
            _softFade = on;
            _wake.Set();
        }

        public void SetHearingCap(float db)
        {
            _capDb = db < 0 ? Math.Max(db, -40f) : 0f;
            _wake.Set();
        }

        // ------------------------------------------------------------------ Stream-Mix, Sprach-Ausgang, Soundboard

        private volatile StreamMix _streamMix;
        private volatile VoiceOut _voiceOut;
        private StreamMix _appliedStream;
        private VoiceOut _appliedVoice;
        private Dictionary<uint, string> _lastStreamPids = new Dictionary<uint, string>();

        public void SetStreamMix(StreamMix mix)
        {
            _streamMix = mix;
            _nextMeterUpdate = 0;
            _wake.Set();
        }

        public void SetVoiceOut(VoiceOut voice)
        {
            _voiceOut = voice;
            // in Befehlsreihenfolge anwenden: "erst stumm, dann ..." bleibt so erhalten
            Post(() => { if (_dsp != null && !_final) { _appliedVoice = voice; _dsp.SetVoice(voice); } });
        }

        public void PlaySound(SoundClip clip, float gain, bool me, bool stream, bool voice) => _dsp?.PlaySound(clip, gain, me, stream, voice);
        public void PlaySoundOn(SoundClip clip, float gain, string deviceId) => _dsp?.PlaySoundOn(clip, gain, deviceId);

        // 2.19: Ausgangs-Klang, Vorhoeren, Mehrspur-Aufnahme (wirken auf den EQ-Weg)
        public void SetOutputFx(Dictionary<string, OutputFx> fx) => _dsp?.SetOutputFx(fx);
        public void SetCue(CueMix cue) => _dsp?.SetCue(cue);
        public void SetNoise(int kind, float gain) => _dsp?.SetNoise(kind, gain);

        public string StartMultitrack(Dictionary<string, WavWriter> writers)
        {
            if (_dsp == null)
                return DspMixer.IsSupported ? "Mehrspur-Aufnahme nicht verfügbar" : "Mehrspur-Aufnahme benötigt Windows 10 Version 2004 oder neuer";
            _dsp.SetMultitrack(writers);
            return null;
        }

        public void StopMultitrack() => _dsp?.SetMultitrack(null);

        public void StopSounds() => _dsp?.StopSounds();

        public void SetSpectrumChannel(string channelId) => _dsp?.SetSpectrumChannel(channelId);

        // Spektrum der Ausgaenge: je Geraet ein eigener Loopback (Eintrag null = folgt dem Windows-Standardgeraet).
        // Starten/Stoppen im Hintergrund, weil das Oeffnen bzw. Schliessen eines Geraets kurz dauern kann.
        private sealed class SpecTap
        {
            public string Id;
            public LoopbackRecorder Rec;
            public SpectrumSink Sink;
        }

        private readonly object _specLock = new object();
        private volatile SpecTap[] _specTaps = new SpecTap[0];
        private volatile string _specKey = "";        // gewuenschte Geraete (vom UI), "" = aus
        private int _specRetryMs = Environment.TickCount;
        private int _specWorking;

        /// <summary>Schluessel fuer eine Geraete-Liste ("" = aus, "\u0001" = Standardgeraet).</summary>
        private static string SpecKey(string[] ids) =>
            ids == null || ids.Length == 0 ? "" : string.Join("\n", ids.Select(d => d ?? "\u0001").Distinct());

        private volatile string _specApplied = "";    // zuletzt angewendet (Hintergrund), null = neu anwenden
        private string[] _specLastIds, _scopeLastIds;
        private string _specLastKey;
        private volatile string[] _specIds;           // Spektrum aus diesen Geraeten (null = aus)
        private volatile string[] _scopeIds;          // 2.16: Goniometer aus diesen Geraeten (null = wie Spektrum)

        /// <summary>2.16: Goniometer (Stereo-Bild) aus diesen Geraeten – Eintrag null = Standardgeraet, null = wie das Spektrum.</summary>
        public void SetScopeSource(string[] deviceIds) => _scopeIds = deviceIds;

        private static string[] UnionIds(string[] a, string[] b)
        {
            if (a == null || a.Length == 0) return b;
            if (b == null || b.Length == 0 || ReferenceEquals(a, b)) return a;
            return a.Concat(b).ToArray();
        }

        private static bool HasId(string[] ids, string id)
        {
            if (ids == null) return false;
            foreach (var x in ids) if (x == null ? id == null : string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void SetMasterSpectrum(string[] deviceIds)
        {
            // nur der Wunsch wird hier gesetzt (UI-Thread blockiert nie) – Oeffnen/Schliessen macht ein Hintergrund-Thread
            _specIds = deviceIds;
            var scope = _scopeOn ? (_scopeIds ?? deviceIds) : null;   // Goniometer braucht evtl. weitere Geraete
            string key = _specLastKey != null && ReferenceEquals(deviceIds, _specLastIds) && ReferenceEquals(scope, _scopeLastIds) ? _specLastKey : SpecKey(UnionIds(deviceIds, scope));
            _specLastIds = deviceIds; _scopeLastIds = scope; _specLastKey = key;
            if (key == _specKey && key == _specApplied)
            {
                // laeuft eins nicht mehr (z. B. Geraet abgesteckt, exklusiv belegt)? Alle paar Sekunden neu versuchen
                if (key.Length == 0 || Environment.TickCount - _specRetryMs < 5000) return;
                if (!_specTaps.Any(t => t.Rec == null || t.Rec.Stopped)) return;
                _specRetryMs = Environment.TickCount;
                _specApplied = null;
            }
            else if (key != _specKey)
            {
                _specKey = key;
                _specRetryMs = Environment.TickCount;
            }
            // laeuft schon einer? Der nimmt den neuen Wunsch mit (sonst beim naechsten Aufruf, weil noch nicht angewendet)
            if (System.Threading.Interlocked.Exchange(ref _specWorking, 1) == 1) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    while (true)
                    {
                        string want = _specKey;
                        ApplySpectrumTaps(want);
                        _specApplied = want;
                        if (want == _specKey) break;
                    }
                }
                catch { _specApplied = null; }
                finally { System.Threading.Interlocked.Exchange(ref _specWorking, 0); }
            });
        }

        private void ApplySpectrumTaps(string key)
        {
            lock (_specLock)
            {
                var want = key.Length == 0 ? new string[0] : key.Split('\n');
                var keep = new List<SpecTap>();
                foreach (var t in _specTaps)
                {
                    if (Array.IndexOf(want, t.Id ?? "\u0001") >= 0 && t.Rec != null && !t.Rec.Stopped) keep.Add(t);
                    else { try { t.Rec?.Dispose(); } catch { } }
                }
                // erst die schon laufenden behalten (Anzeige bleibt), dann die neuen oeffnen
                _specTaps = keep.ToArray();
                foreach (var id in want)
                {
                    if (keep.Any(t => (t.Id ?? "\u0001") == id)) continue;
                    var t = new SpecTap { Id = id == "\u0001" ? null : id, Sink = new SpectrumSink() };
                    try { t.Rec = new LoopbackRecorder(t.Id, t.Sink); } catch { t.Rec = null; }
                    keep.Add(t);   // auch wenn es nicht ging: wird beim naechsten Versuch neu geoeffnet
                    _specTaps = keep.ToArray();
                }
            }
        }

        // Lautheit (LUFS) des Standardgeraets: eigener Loopback, Oeffnen/Schliessen im Hintergrund
        private readonly object _lufsLock = new object();
        private LoopbackRecorder _lufsRec;
        private volatile LoudnessSink _lufsSink;
        private volatile bool _lufsWanted;
        private int _lufsWorking, _lufsRetryMs = Environment.TickCount;

        public void SetLoudness(bool on)
        {
            if (on == _lufsWanted)
            {
                // laeuft nicht mehr? alle paar Sekunden neu versuchen
                var rec = _lufsRec;
                if (!on || (rec != null && !rec.Stopped) || Environment.TickCount - _lufsRetryMs < 5000) return;
            }
            _lufsWanted = on;
            _lufsRetryMs = Environment.TickCount;
            if (System.Threading.Interlocked.Exchange(ref _lufsWorking, 1) == 1) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (_lufsLock)
                    {
                        if (_lufsRec != null && (!_lufsWanted || _lufsRec.Stopped))
                        {
                            var old = _lufsRec;
                            _lufsRec = null; _lufsSink = null;
                            try { old.Dispose(); } catch { }
                        }
                        if (_lufsWanted && _lufsRec == null)
                        {
                            var sink = new LoudnessSink();
                            try { _lufsRec = new LoopbackRecorder(null, sink); _lufsSink = sink; }
                            catch { _lufsRec = null; _lufsSink = null; }
                        }
                    }
                }
                finally { System.Threading.Interlocked.Exchange(ref _lufsWorking, 0); }
            });
        }

        public void ResetLoudness() { _lufsSink?.ResetIntegrated(); ResetDeviceLoudness(); }

        private volatile bool _scopeOn;
        public void SetScope(bool on) => _scopeOn = on;

        /// <summary>Goniometer-Daten der gewaehlten Ausgaenge (mehrere: zusammengemischt).</summary>
        private float[] ComputeScope(out float corr)
        {
            corr = 0f;
            if (!_scopeOn) return null;
            var ids = _scopeIds ?? _specIds;
            float[] sum = null;
            int n = 0;
            float c1 = 0f;
            foreach (var t in _specTaps)
            {
                if (ids != null && !HasId(ids, t.Id)) continue;
                var sink = t.Sink;
                if (sink == null) continue;
                var s = sink.Scope(1024, out float c);
                if (s == null) continue;
                n++;
                if (sum == null) { sum = s; c1 = c; continue; }
                if (n == 2) sum = (float[])sum.Clone();
                for (int i = 0; i < sum.Length && i < s.Length; i++) sum[i] += s[i];
            }
            if (sum == null) return null;
            if (n == 1) { corr = c1; return sum; }
            // Korrelation der Mischung
            double lr = 0, ll = 0, rr = 0;
            for (int i = 0; i + 1 < sum.Length; i += 2) { double l = sum[i], r = sum[i + 1]; lr += l * r; ll += l * l; rr += r * r; }
            corr = ll > 1e-12 && rr > 1e-12 ? (float)(lr / Math.Sqrt(ll * rr)) : 0f;
            return sum;
        }

        /// <summary>Spektrum aller gewaehlten Ausgaenge (je Band der lauteste) oder null, wenn nichts ankommt.</summary>
        private float[] ComputeMasterSpectrum()
        {
            float[] res = null;
            var ids = _specIds;
            if (ids == null) return null;   // nur das Goniometer laeuft
            foreach (var t in _specTaps)
            {
                if (!HasId(ids, t.Id)) continue;
                var s = t.Sink?.Compute();
                if (s == null) continue;
                if (res == null) { res = s; continue; }
                for (int i = 0; i < res.Length && i < s.Length; i++) if (s[i] > res[i]) res[i] = s[i];
            }
            return res;
        }

        public string StartRecording(string channelId, string deviceId, WavWriter writer)
        {
            StopRecording();
            if (writer == null) return "Keine Datei";
            if (channelId == null)
            {
                var rec = new LoopbackRecorder(deviceId, writer);
                if (rec.Error != null) { string e = rec.Error; rec.Dispose(); return e; }
                _loopRec = rec;
                return null;
            }
            if (_dsp == null)
                return DspMixer.IsSupported ? "Kanal-Aufnahme nicht verfügbar" : "Kanal-Aufnahme benötigt Windows 10 Version 2004 oder neuer";
            _recChannel = channelId;
            _dsp.SetRecording(channelId, writer);
            Post(() => { _nextMeterUpdate = 0; });
            return null;
        }

        public void StopRecording()
        {
            var rec = _loopRec;
            _loopRec = null;
            rec?.Dispose();
            if (_recChannel != null)
            {
                _recChannel = null;
                _dsp?.SetRecording(null, null);
                Post(() => { _nextMeterUpdate = 0; });
            }
        }

        public string RecordingError
        {
            get
            {
                var r = _loopRec;
                return r != null && r.Stopped ? (r.Error ?? "Aufnahme beendet") : null;
            }
        }

        public void SetDefaultDevice(string deviceId)
        {
            Post(() =>
            {
                object pc = null;
                try
                {
                    pc = new PolicyConfigClient();
                    var cfg = (IPolicyConfig)pc;
                    cfg.SetDefaultEndpoint(deviceId, ERole.eConsole);
                    cfg.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                    if (_captureIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase))
                        cfg.SetDefaultEndpoint(deviceId, ERole.eCommunications); // Mikro auch fuer Sprach-Apps
                }
                catch (Exception ex) { _error = "Standardgerät konnte nicht gesetzt werden: " + ex.Message; }
                finally { ComUtil.Release(pc); }
                _forceDeviceScan = true;
            });
        }

        public void ResetAllAppRouting()
        {
            Post(() =>
            {
                if (_routing == null) return;
                _routing.ClearAll();
                _routedPid.Clear();
                foreach (var s in _sessions.Values) s.RoutingDirty = true;
            });
        }

        public void Flush(int timeoutMs)
        {
            if (!_thread.IsAlive) return;
            using (var done = new ManualResetEventSlim(false))
            {
                Post(() =>
                {
                    FinalState();
                    try { done.Set(); } catch { }
                });
                done.Wait(timeoutMs);
            }
        }

        public void Dispose()
        {
            DisposeMirror();   // 3.5
            _running = false;
            _wake.Set();
            try { _thread.Join(2500); } catch { }
        }

        /// <summary>
        /// Endzustand beim Beenden: EQ-Weg stoppen und alle Apps direkt mit ihrer normalen Lautstaerke setzen –
        /// ohne Blenden und ohne Gehoerschutz (sonst blieben Apps in Windows leiser bzw. bei -80 dB).
        /// </summary>
        private void FinalState()
        {
            _final = true;
            _cap.Clear();
            try { ApplyPendingDeviceVolumes(); } catch { }
            var dsp = _dsp;
            _dsp = null;                       // erst den EQ-Mischer anhalten (kurz Stille statt doppeltem Ton) ...
            _lastWanted = new Dictionary<uint, string>();
            try { dsp?.Dispose(); } catch { }
            try { ApplyPolicies(); } catch { } // ... dann laufen alle Apps wieder direkt
            foreach (var s in _sessions.Values)
            {
                if (!s.Dsp || s.Volume == null || s.Applied == null) continue;   // Rueckfallebene
                try
                {
                    if (s.Applied.Mute) s.Volume.SetMute(true, ref EventContext);
                    s.Volume.SetMasterVolume(Clamp01(s.Applied.Volume * s.Applied.AppGain), ref EventContext);
                    s.Dsp = false;
                }
                catch { }
            }
        }

        private void Post(Action a)
        {
            _queue.Enqueue(a);
            _wake.Set();
        }

        // ------------------------------------------------------------------ Hauptschleife

        private void Run()
        {
            ThreadCpu.Register("Ton");   // 3.4.1
            try
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            }
            catch (Exception ex)
            {
                _snapshot = new EngineSnapshot
                {
                    Devices = new DeviceState[0], Apps = new AppState[0], Structure = "error",
                    Error = "Windows-Audio konnte nicht initialisiert werden: " + ex.Message
                };
                return;
            }

            try { _routing = new AppRouting(); _routingSupported = _routing.IsSupported; }
            catch { _routingSupported = false; }

            try { if (DspMixer.IsSupported) _dsp = new DspMixer(); }
            catch { _dsp = null; }

            var clock = Stopwatch.StartNew();
            long nextDeviceScan = 0, nextSessionScan = 0, nextFull = 0;

            while (_running)
            {
                long now = clock.ElapsedMilliseconds;

                while (_queue.TryDequeue(out var action))
                {
                    try { action(); } catch { }
                }
                ApplyPendingDeviceVolumes();

                if (now >= nextDeviceScan || _forceDeviceScan)
                {
                    _forceDeviceScan = false;
                    Safe(ScanDevices);
                    Safe(ScanCapture);
                    Safe(SyncFastMeters);
                    nextDeviceScan = now + 2000;
                }
                if (_sessionsDirty || now >= nextSessionScan)
                {
                    _sessionsDirty = false;
                    Safe(ScanSessions);
                    nextSessionScan = now + 1000;
                }

                bool rt = _realtime;
                bool fastPoll = rt && LevelFlow.FastPoll;   // 3.1.1: Pegel alle 5 ms
                if (_dsp != null) _dsp.PeakMs = fastPoll ? 5 : rt ? 10 : 30;
                // Echtzeit: Pegel jedes Mal (alle 10 ms), Lautstaerken/Blenden/Messungen wie bisher alle ~20 ms
                // (sofort, wenn sich etwas geaendert hat – z. B. ein Fader bewegt wurde)
                if (!rt || now >= nextFull || _policyKick)
                {
                    _policyKick = false;
                    nextFull = now + 20;
                    Safe(ApplyPolicies);
                    Safe(GuardPoll);   // 3.4.1: Lautstaerke der Apps festhalten
                    Safe(UpdateMeterCaptures);
                    Safe(() => UpdateMicLive(now));
                }
                Safe(ReadLevels);
                Safe(UpdateHearingCap);
                Safe(ReadMicLevels);
                Safe(Publish);

                ApplyEngineBoost();
                PerfNative.MmcssFollow(FramePacer.GameBoost, ref _engineMmcss);   // 3.0.2: Spiele-Vorrang auch fuer die Pegel
                _wake.WaitOne(fastPoll ? 5 : rt ? 10 : EngineWaitMs);
            }

            PerfNative.MmcssEnd(_engineMmcss); _engineMmcss = IntPtr.Zero;
            Shutdown();
        }

        private IntPtr _engineMmcss;

        private void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { _error = ex.Message; }
        }

        private void ApplyPendingDeviceVolumes()
        {
            if (!_pendingDeviceDb.IsEmpty)
                foreach (var key in new List<string>(_pendingDeviceDb.Keys))
                {
                    if (!_pendingDeviceDb.TryRemove(key, out float db)) continue;
                    if (!_devices.TryGetValue(key, out var d) || d.Volume == null) continue;
                    try
                    {
                        if (db <= d.MinDb) d.Volume.SetMasterVolumeLevelScalar(0f, ref EventContext);
                        else d.Volume.SetMasterVolumeLevel(Math.Min(d.MaxDb, db), ref EventContext);
                    }
                    catch { }
                    _nextDevVolMs = 0;
                }
            if (_pendingDeviceVolume.IsEmpty) return;
            foreach (var key in new List<string>(_pendingDeviceVolume.Keys))
            {
                if (!_pendingDeviceVolume.TryRemove(key, out float v)) continue;
                if (key == MicKey)
                {
                    if (_mic?.Volume != null) try { _mic.Volume.SetMasterVolumeLevelScalar(v, ref EventContext); } catch { }
                }
                else if (_devices.TryGetValue(key, out var d) && d.Volume != null)
                {
                    try { d.Volume.SetMasterVolumeLevelScalar(v, ref EventContext); } catch { }
                }
            }
            _nextDevVolMs = 0;   // 3.4.1: gleich zuruecklesen
        }

        private long _nextDevVolMs;   // 3.4.1: Lautstaerke/Stumm der Geraete hoechstens alle 50 ms lesen

        // ------------------------------------------------------------------ Geraete

        private void ScanDevices()
        {
            string defaultId = null;
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice def) == 0 && def != null)
            {
                def.GetId(out defaultId);
                ComUtil.Release(def);
            }
            _defaultId = defaultId;

            if (_enumerator.EnumAudioEndpoints(EDataFlow.eRender, CoreAudioConst.DEVICE_STATE_ACTIVE, out IMMDeviceCollection coll) != 0 || coll == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            try
            {
                coll.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (coll.Item(i, out IMMDevice dev) != 0 || dev == null) continue;
                    string id = null;
                    dev.GetId(out id);
                    if (id == null) { ComUtil.Release(dev); continue; }
                    seen.Add(id);
                    if (_devices.ContainsKey(id)) { ComUtil.Release(dev); continue; }

                    var e = CreateDevice(id, dev);
                    _devices[id] = e;
                    changed = true;
                }
            }
            finally { ComUtil.Release(coll); }

            foreach (var id in new List<string>(_devices.Keys))
            {
                if (seen.Contains(id)) continue;
                DestroyDevice(_devices[id]);
                _devices.Remove(id);
                changed = true;
            }

            if (changed)
            {
                _sessionsDirty = true;
                foreach (var s in _sessions.Values) s.RoutingDirty = true;
            }
        }

        private DeviceEntry CreateDevice(string id, IMMDevice dev)
        {
            var e = new DeviceEntry { Id = id, Device = dev };

            if (dev.OpenPropertyStore(CoreAudioConst.STGM_READ, out IPropertyStore ps) == 0 && ps != null)
            {
                try
                {
                    e.FullName = ReadString(ps, PropertyKey.DeviceFriendlyName);
                    e.Name = ReadString(ps, PropertyKey.DeviceDesc);
                    ReadDeviceKind(ps, e);   // 3.5
                }
                finally { ComUtil.Release(ps); }
            }
            if (string.IsNullOrEmpty(e.FullName)) e.FullName = e.Name ?? "Ausgabegerät";
            if (string.IsNullOrEmpty(e.Name)) e.Name = e.FullName;

            e.Volume = Activate<IAudioEndpointVolume>(dev, IID_IAudioEndpointVolume);
            try { if (e.Volume != null && e.Volume.GetVolumeRange(out float mn, out float mx, out _) == 0 && mx > mn) { e.MinDb = mn; e.MaxDb = mx; } } catch { }   // 3.4.3
            e.Meter = Activate<IAudioMeterInformation>(dev, IID_IAudioMeterInformation);
            if (e.Meter != null && e.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0)
                e.MeterBuf = new float[mc];

            e.Manager = Activate<IAudioSessionManager2>(dev, IID_IAudioSessionManager2);
            if (e.Manager != null)
            {
                // Registrierung sorgt dafuer, dass der Session-Enumerator neue Sessions zuverlaessig liefert.
                var n = new SessionNotifier(ptr => { QueueFastApply(ptr); _sessionsDirty = true; _wake.Set(); });   // 3.4.1: sofort einstellen
                try
                {
                    if (e.Manager.RegisterSessionNotification(n) == 0) e.Notifier = n;
                }
                catch { }
            }
            return e;
        }

        private void DestroyDevice(DeviceEntry e)
        {
            foreach (var key in new List<string>(_sessions.Keys))
            {
                var s = _sessions[key];
                if (string.Equals(s.DeviceId, e.Id, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseSession(s);
                    _sessions.Remove(key);
                }
            }
            if (e.Manager != null && e.Notifier != null)
            {
                try { e.Manager.UnregisterSessionNotification(e.Notifier); } catch { }
            }
            ComUtil.Release(e.Manager);
            DropFast(e.Meter);
            ComUtil.Release(e.Meter);
            ComUtil.Release(e.Volume);
            ComUtil.Release(e.Device);
        }

        private static T Activate<T>(IMMDevice dev, Guid iid) where T : class
        {
            try
            {
                Guid g = iid;
                if (dev.Activate(ref g, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out object o) != 0 || o == null) return null;
                return o as T;
            }
            catch { return null; }
        }

        private static string ReadString(IPropertyStore ps, PropertyKey key)
        {
            PropertyKey k = key;
            if (ps.GetValue(ref k, out PropVariant pv) != 0) return null;
            try { return pv.GetString(); }
            finally { PropVariant.PropVariantClear(ref pv); }
        }

        // ------------------------------------------------------------------ Mikrofon

        private void ScanCapture()
        {
            string micId = null;
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out IMMDevice dev) == 0 && dev != null)
            {
                dev.GetId(out micId);
                if (_mic == null || !string.Equals(_mic.Id, micId, StringComparison.OrdinalIgnoreCase))
                {
                    DestroyMic();
                    _mic = CreateMic(micId, dev);
                }
                else ComUtil.Release(dev);
            }
            else DestroyMic();

            var ids = new List<string>();
            if (_enumerator.EnumAudioEndpoints(EDataFlow.eCapture, CoreAudioConst.DEVICE_STATE_ACTIVE, out IMMDeviceCollection coll) == 0 && coll != null)
            {
                try
                {
                    coll.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (coll.Item(i, out IMMDevice d) != 0 || d == null) continue;
                        try
                        {
                            d.GetId(out string id);
                            if (id == null) continue;
                            ids.Add(id);
                            if (!_captureNames.ContainsKey(id)) _captureNames[id] = ReadNames(d);
                        }
                        finally { ComUtil.Release(d); }
                    }
                }
                finally { ComUtil.Release(coll); }
            }
            _captureIds = ids;
        }

        private static string[] ReadNames(IMMDevice d)
        {
            string name = null, full = null;
            if (d.OpenPropertyStore(CoreAudioConst.STGM_READ, out IPropertyStore ps) == 0 && ps != null)
            {
                try
                {
                    full = ReadString(ps, PropertyKey.DeviceFriendlyName);
                    name = ReadString(ps, PropertyKey.DeviceDesc);
                }
                finally { ComUtil.Release(ps); }
            }
            if (string.IsNullOrEmpty(full)) full = name ?? "Mikrofon";
            if (string.IsNullOrEmpty(name)) name = full;
            return new[] { name, full };
        }

        private MicEntry CreateMic(string id, IMMDevice dev)
        {
            var names = ReadNames(dev);
            var m = new MicEntry { Id = id, Device = dev, Name = names[0], FullName = names[1] };
            m.Volume = Activate<IAudioEndpointVolume>(dev, IID_IAudioEndpointVolume);
            m.Meter = Activate<IAudioMeterInformation>(dev, IID_IAudioMeterInformation);
            if (m.Meter != null && m.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0) m.MeterBuf = new float[mc];
            return m;
        }

        private void DestroyMic()
        {
            var m = _mic;
            _mic = null;
            if (m == null) return;
            CloseMicLive(m);
            DropFast(m.Meter);
            ComUtil.Release(m.Meter);
            ComUtil.Release(m.Volume);
            ComUtil.Release(m.Device);
        }

        /// <summary>Haelt auf Wunsch einen Aufnahme-Stream offen, damit der Mikro-Pegel immer sichtbar ist.</summary>
        private void UpdateMicLive(long now)
        {
            var m = _mic;
            if (m == null) return;
            if (_micLive && m.LiveClient == null && now >= m.LiveRetryAt)
            {
                IntPtr fmt = IntPtr.Zero;
                IAudioClient client = null;
                try
                {
                    Guid iid = Wasapi.IID_IAudioClient;
                    if (m.Device.Activate(ref iid, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out object o) != 0) throw new Exception("Activate");
                    client = (IAudioClient)o;
                    if (client.GetMixFormat(out fmt) < 0) throw new Exception("GetMixFormat");
                    if (client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED, 0, 2000000, 0, fmt, IntPtr.Zero) < 0) throw new Exception("Initialize");
                    Guid cid = Wasapi.IID_IAudioCaptureClient;
                    if (client.GetService(ref cid, out object svc) < 0) throw new Exception("GetService");
                    if (client.Start() < 0) throw new Exception("Start");
                    m.LiveClient = client;
                    m.LiveCapture = (IAudioCaptureClient)svc;
                    client = null;
                }
                catch { m.LiveRetryAt = now + 5000; }
                finally
                {
                    if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt);
                    ComUtil.Release(client);
                }
            }
            else if (!_micLive && m.LiveClient != null)
            {
                CloseMicLive(m);
            }

            var cap = m.LiveCapture;
            if (cap == null) return;
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    if (cap.GetNextPacketSize(out uint packet) < 0 || packet == 0) break;
                    if (cap.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) < 0) break;
                    cap.ReleaseBuffer(frames); // Daten werden nicht gebraucht, nur der Pegel
                }
            }
            catch { CloseMicLive(m); m.LiveRetryAt = now + 5000; }
        }

        private static void CloseMicLive(MicEntry m)
        {
            try { m.LiveClient?.Stop(); } catch { }
            ComUtil.Release(m.LiveCapture);
            ComUtil.Release(m.LiveClient);
            m.LiveCapture = null;
            m.LiveClient = null;
        }

        // ------------------------------------------------------------------ Sessions

        private void ScanSessions()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            _procCache.Clear(); // Windows vergibt Prozess-IDs neu – nie alte Zuordnungen weiterverwenden

            foreach (var dev in _devices.Values)
            {
                if (dev.Manager == null) continue;
                IAudioSessionEnumerator en = null;
                try
                {
                    if (dev.Manager.GetSessionEnumerator(out en) != 0 || en == null) continue;
                    en.GetCount(out int count);
                    for (int i = 0; i < count; i++)
                    {
                        IAudioSessionControl ctl = null;
                        bool keep = false;
                        try
                        {
                            if (en.GetSession(i, out ctl) != 0 || ctl == null) continue;
                            var c2 = ctl as IAudioSessionControl2;
                            if (c2 == null) continue;
                            if (c2.GetSessionInstanceIdentifier(out string inst) != 0 || string.IsNullOrEmpty(inst)) continue;
                            c2.GetState(out AudioSessionState state);
                            if (state == AudioSessionState.Expired) continue;

                            seen.Add(inst);
                            if (_sessions.TryGetValue(inst, out var existing))
                            {
                                existing.State = state;
                                if (state == AudioSessionState.Active) existing.LastActiveMs = _engineClock.ElapsedMilliseconds;
                                continue;
                            }

                            var s = CreateSession(inst, dev.Id, ctl, c2, state);
                            if (s == null) continue;
                            if (state == AudioSessionState.Active) s.LastActiveMs = _engineClock.ElapsedMilliseconds;
                            _sessions[inst] = s;
                            keep = true;
                            PreApply(s);   // 3.0.3: sofort auf die richtige Lautstaerke, nicht erst nach der Prozess-Suche
                            RegisterGuard(s);   // 3.4.1: Aenderungen von aussen sofort zuruecknehmen
                        }
                        catch { }
                        finally
                        {
                            if (!keep) ComUtil.Release(ctl);
                        }
                    }
                }
                catch { }
                finally { ComUtil.Release(en); }
            }

            foreach (var key in new List<string>(_sessions.Keys))
            {
                if (seen.Contains(key)) continue;
                ReleaseSession(_sessions[key]);
                _sessions.Remove(key);
            }

            // Prozess-Caches aufraeumen (PIDs koennen wiederverwendet werden)
            var livePids = new HashSet<uint>();
            foreach (var s in _sessions.Values) livePids.Add(s.Pid);
            _dsp?.ForgetDeadPids(livePids);

            // Eltern/Kind-Beziehungen zwischen Audio-Prozessen ermitteln
            if (_dsp != null)
            {
                // 3.0.2: die Liste aller Prozesse zu holen dauert je nach PC 5–30 ms – bisher jede Sekunde, und so lange
                // kamen keine Kanal-Pegel (die Pegel stockten kurz). Jetzt nur, wenn sich die Audio-Prozesse geaendert
                // haben (sofort, wie bisher), sonst alle 10 s nebenher im Hintergrund.
                long nowMs = _engineClock.ElapsedMilliseconds;
                bool changed = _parentPids == null || !_parentPids.SetEquals(livePids);
                if (!LevelFlow.Seamless || changed)
                {
                    var parents = Win32.GetParentMap();
                    _parentMap = parents;
                    _treeConflicts = TreeConflicts(parents, livePids);
                    _parentPids = livePids;
                    _parentNextMs = nowMs + 10000;
                }
                else if (nowMs >= _parentNextMs && !_parentBusy)
                {
                    _parentNextMs = nowMs + 10000;
                    _parentBusy = true;
                    var pids = new HashSet<uint>(livePids);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var parents = Win32.GetParentMap();
                            var conflicts = TreeConflicts(parents, pids);
                            if (_parentPids != null && _parentPids.SetEquals(pids)) { _parentMap = parents; _treeConflicts = conflicts; }
                        }
                        catch { }
                        finally { _parentBusy = false; }
                    });
                }
            }
            foreach (var pid in new List<uint>(_procCache.Keys)) if (!livePids.Contains(pid)) _procCache.Remove(pid);
            foreach (var pid in new List<uint>(_routedPid.Keys)) if (!livePids.Contains(pid)) _routedPid.Remove(pid);
        }

        /// <summary>
        /// 3.0.3: Eine neue Session spielt mit der Lautstaerke, die Windows sich gemerkt hat (oft 100 %), bis das Pult sie
        /// einstellt. Bisher kam das erst nach der Prozess-Suche und im naechsten Durchgang – ein paar Dutzend ms voller
        /// Lautstaerke (Knacken, Pegel springt hoch). Jetzt sofort: auf dem EQ-Weg -80 dB, sonst die Kanal-Lautstaerke.
        /// Den Rest (Balance, Geraet, weiches Blenden) macht ApplyPolicies gleich danach.
        /// </summary>
        private void PreApply(SessionEntry s)
        {
            if (!CleanStart.On || _final || s.Volume == null) return;
            var p = _policy.Get(s.AppKey);
            if (p == null) return;
            try
            {
                bool dsp = p.Eq != null && _dsp != null && !_dsp.IsChannelBroken(p.Eq.ChannelId) && s.Pid != 0 && s.Pid != _ownPid &&
                           s.AppKey != AppKeys.System && !_dsp.IsFailed(s.Pid);
                float vol = dsp ? DspMixer.Attenuation : (p.Mute ? 0f : Clamp01(p.Volume * p.AppGain)) * CapGain(s.AppKey);
                if (!dsp && CleanStart.NewAppFadeMs > 0) vol = 0f;   // 3.4: neue App beginnt still und blendet ein
                s.Volume.SetMasterVolume(vol, ref EventContext);
                if (dsp) _dsp.NoteAttenuated(s.Pid);
                _policyKick = true;   // Rest noch in diesem Durchgang
            }
            catch { }
        }

        private volatile HashSet<uint> _parentPids;
        private long _parentNextMs;
        private volatile bool _parentBusy;

        /// <summary>Audio-Prozesse, unter denen noch ein anderer Audio-Prozess laeuft (Eltern in derselben Kette).</summary>
        private static HashSet<uint> TreeConflicts(Dictionary<uint, uint> parents, HashSet<uint> livePids)
        {
            var conflicts = new HashSet<uint>();
            foreach (var pid in livePids)
            {
                if (pid == 0) continue;
                uint cur = pid;
                var visited = new HashSet<uint> { pid };
                for (int depth = 0; depth < 32; depth++)
                {
                    if (!parents.TryGetValue(cur, out uint parent) || parent == 0 || !visited.Add(parent)) break;
                    if (livePids.Contains(parent)) conflicts.Add(parent);
                    cur = parent;
                }
            }
            return conflicts;
        }

        private SessionEntry CreateSession(string inst, string deviceId, IAudioSessionControl ctl, IAudioSessionControl2 c2, AudioSessionState state)
        {
            var s = new SessionEntry
            {
                InstanceId = inst,
                DeviceId = deviceId,
                Control = c2,
                State = state,
                Volume = ctl as ISimpleAudioVolume,
                Meter = ctl as IAudioMeterInformation,
            };
            try { s.Channels = ctl as IChannelAudioVolume; } catch { s.Channels = null; }

            c2.GetProcessId(out uint pid);
            if (pid == _ownPid) return null; // eigene EQ-Ausgabe nicht als App fuehren
            s.Pid = pid;

            bool isSystem = c2.IsSystemSoundsSession() == 0;
            if (isSystem)
            {
                s.AppKey = AppKeys.System;
                s.Name = "Systemklänge";
            }
            else if (pid == 0)
            {
                c2.GetDisplayName(out string dn);
                s.AppKey = "#session:" + (c2.GetSessionIdentifier(out string sid) == 0 ? sid : inst);
                s.Name = string.IsNullOrEmpty(dn) || dn.StartsWith("@") ? "Unbekannte Quelle" : dn;
            }
            else
            {
                var pi = GetProcInfo(pid);
                s.AppKey = pi.Key;
                s.Name = pi.Name;
                s.Path = pi.Path;
            }

            if (s.Meter != null && s.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0)
                s.MeterBuf = new float[mc];
            if (s.Channels != null && s.Channels.GetChannelCount(out uint cc) == 0)
                s.ChannelCount = cc;

            return s;
        }

        private static void ReleaseSession(SessionEntry s)
        {
            UnregisterGuard(s);   // 3.4.1: erst abmelden, dann freigeben
            // Volume/Meter/Channels sind derselbe RCW wie Control -> einmal freigeben genuegt.
            ComUtil.Release(s.Control);
        }

        private ProcInfo GetProcInfo(uint pid)
        {
            if (_procCache.TryGetValue(pid, out var pi)) return pi;

            string path = Win32.GetProcessPath(pid);
            string key, name;
            if (path != null)
            {
                key = AppKeys.FromExe(path);
                name = DescribeExe(path);
            }
            else
            {
                string pn = null;
                try { using (var p = Process.GetProcessById((int)pid)) pn = p.ProcessName; } catch { }
                key = pn != null ? AppKeys.FromExe(pn) : "#pid:" + pid;
                name = pn ?? ("Prozess " + pid);
            }
            pi = new ProcInfo { Key = key, Name = name, Path = path };
            _procCache[pid] = pi;
            return pi;
        }

        private string DescribeExe(string path)
        {
            if (_exeNameCache.TryGetValue(path, out var cached)) return cached;
            string result = null;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                string d = vi.FileDescription?.Trim();
                string p = vi.ProductName?.Trim();
                if (!string.IsNullOrEmpty(d) && d.Length <= 40) result = d;
                else if (!string.IsNullOrEmpty(p) && p.Length <= 40) result = p;
            }
            catch { }
            if (string.IsNullOrEmpty(result)) result = Path.GetFileNameWithoutExtension(path);
            _exeNameCache[path] = result;
            return result;
        }

        // ------------------------------------------------------------------ Policies anwenden

        private bool Capturable(SessionEntry s, AppPolicy p) =>
            _dsp != null && !_dsp.IsChannelBroken(p.Eq.ChannelId) && s.Pid != 0 && s.Pid != _ownPid &&
            s.AppKey != AppKeys.System && !_dsp.IsFailed(s.Pid) && !_treeConflicts.Contains(s.Pid);

        private void ApplyPolicies()
        {
            var table = _policy;
            if (!ReferenceEquals(table, _lastTable))
            {
                _lastTable = table;
                if (_dsp != null)
                {
                    var ch = new Dictionary<string, ChannelDsp>();
                    foreach (var p in table.EqChannels)
                        ch[p.Eq.ChannelId] = new ChannelDsp(p.Mute ? 0f : Clamp01(p.Volume), p.Eq.Low, p.Eq.Mid, p.Eq.High, p.DeviceId, p.Eq.Comp, p.Eq.Leveler, p.Eq.Graphic, p.Eq.Fx, p.Eq.Cue, p.Eq.Stereo, p.Eq.DelayMs, p.Eq.Extra);
                    _dsp.SetChannels(ch);
                }
            }
            if (_dsp != null)
            {
                var sm = _final ? null : _streamMix;
                if (!ReferenceEquals(sm, _appliedStream)) { _appliedStream = sm; _dsp.SetStream(sm); }
                var vo = _final ? null : _voiceOut;
                if (!ReferenceEquals(vo, _appliedVoice)) { _appliedVoice = vo; _dsp.SetVoice(vo); }
                _dsp.SoftGain = _softFade;
                float capDb = _capDb;
                _dsp.Ceiling = capDb < 0 ? (float)Math.Pow(10, capDb / 20.0) : 0f;
            }

            long nowMs = _engineClock.ElapsedMilliseconds;
            bool softFade = _softFade && !_final;
            var wanted = new Dictionary<uint, string>();
            var appGains = new Dictionary<uint, float>();
            foreach (var s in _sessions.Values)
            {
                var p = table.Get(s.AppKey);
                if (p == null) continue;

                bool dsp = p.Eq != null && Capturable(s, p);
                bool isNew = !s.HasApplied;
                float level = Clamp01(p.Volume * p.AppGain);
                float vol;
                bool mute;
                if (dsp)
                {
                    // Stummschalten uebernimmt der EQ-Mischer (weich); die Session bleibt bei -80 dB.
                    // Die eigene App-Lautstaerke wendet der Mischer an (SetCaptureGains).
                    vol = DspMixer.Attenuation;
                    mute = false;
                    s.Fade.Jump(p.Mute ? 0f : level);
                    // 3.0.3: wird die Session jetzt erst abgesenkt, ist das, was in den naechsten ms noch laut ankommt, ein Uebergang
                    if (CleanStart.On && (isNew || !s.Dsp || s.AppliedVolume != DspMixer.Attenuation || s.AppliedMute)) _dsp.NoteAttenuated(s.Pid);
                }
                else
                {
                    // Weiche Ueberblendung: Ziel ist die hoerbare Lautstaerke (stumm = 0)
                    float goal = p.Mute ? 0f : level;
                    int fadeMs = CleanStart.NewAppFadeMs;
                    if (isNew && fadeMs > 0 && !_final)
                    {
                        // 3.4: neue App blendet ein statt sofort voll da zu sein
                        s.Fade.Jump(0f);
                        s.Fade.Ramp(goal, nowMs, fadeMs);
                        s.FadeInUntil = nowMs + fadeMs;
                    }
                    else if ((!softFade || isNew || s.Dsp) && nowMs >= s.FadeInUntil) s.Fade.Jump(goal);
                    float cur = s.Fade.Update(goal, nowMs);
                    float cap = CapGain(s.AppKey);
                    if (s.CaptureMeter && !_final && !s.MeterPreVolume && s.State == AudioSessionState.Active)
                    {
                        // Gemessene App: "stumm" = -80 dB statt Windows-Stumm. Die Messung enthaelt die Absenkung
                        // und laesst sich zurueckrechnen – so bleibt sichtbar, was die App eigentlich abspielt.
                        // Nur waehrend die App spielt: hoert sie auf, gilt wieder echtes Stumm (das merkt sich Windows).
                        mute = false;
                        vol = Math.Max(cur, DspMixer.Attenuation) * cap;
                    }
                    else
                    {
                        // Stumm erst, wenn ausgeblendet; dann wieder die normale Lautstaerke (fuer den Windows-Mixer)
                        mute = p.Mute && cur <= 0f;
                        vol = mute ? level * cap : cur * cap;
                    }
                }
                try
                {
                    if (s.Volume != null)
                    {
                        bool muteChanged = isNew || mute != s.AppliedMute;
                        bool volChanged = isNew || vol != s.AppliedVolume;
                        // Reihenfolge: erst stumm, dann Lautstaerke hoch – bzw. erst Lautstaerke runter, dann laut
                        if (muteChanged && mute) s.Volume.SetMute(true, ref EventContext);
                        if (volChanged) s.Volume.SetMasterVolume(vol, ref EventContext);
                        if (muteChanged && !mute) s.Volume.SetMute(false, ref EventContext);
                    }
                    if (isNew || p.Pan != s.AppliedPan) ApplyPan(s, p.Pan, isNew);

                    var prev = s.Applied;
                    bool routingChanged = isNew || s.RoutingDirty || prev == null ||
                        !string.Equals(prev.DeviceId, p.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                        prev.EnforceDevice != p.EnforceDevice;
                    if (routingChanged) ApplyRouting(s, p);
                }
                catch { }
                float newGain = mute ? 0f : vol, oldGain = s.AppliedMute ? 0f : s.AppliedVolume;
                float nl = newGain * PanL(p.Pan), nr = newGain * PanR(p.Pan);
                float ol = oldGain * PanL(s.AppliedPan), or = oldGain * PanR(s.AppliedPan);
                if (!isNew && (Math.Abs(nl - ol) > 1e-7f || Math.Abs(nr - or) > 1e-7f))
                {
                    // die alten Werte galten bis jetzt
                    s.HistL[s.HistPos] = ol; s.HistR[s.HistPos] = or; s.HistMs[s.HistPos] = nowMs;
                    s.HistPos = (s.HistPos + 1) % s.HistMs.Length;
                    s.GainChangedMs = nowMs;
                }
                s.AppliedVolume = vol;
                s.AppliedMute = mute;
                s.AppliedPan = p.Pan;
                s.HasApplied = true;
                s.Applied = p;
                s.RoutingDirty = false;
                s.Dsp = dsp;
                if (dsp)
                {
                    wanted[s.Pid] = p.Eq.ChannelId;
                    if (p.AppGain < 1f) appGains[s.Pid] = p.AppGain;
                }
            }

            if (_dsp != null && !SameMap(wanted, _lastWanted))
            {
                _lastWanted = wanted;
                _dsp.SetCaptures(wanted);
            }
            if (_dsp != null && !SameGains(appGains, _lastAppGains))
            {
                _lastAppGains = appGains;
                _dsp.SetCaptureGains(appGains);
            }
        }

        /// <summary>
        /// Waehlt die Prozesse, deren Pegel per Prozess-Loopback gemessen wird. Prozesse, unter denen Audio-Prozesse
        /// anderer Apps laufen, werden nicht gemessen (deren Ton waere mit drin) – dort bleibt der Windows-Pegel.
        /// Laufen mehrere Audio-Prozesse derselben App untereinander, genuegt die Messung des obersten.
        /// </summary>
        private void UpdateMeterCaptures()
        {
            if (_dsp == null) return;
            long now = _engineClock.ElapsedMilliseconds;
            if (now < _nextMeterUpdate) return;
            _nextMeterUpdate = now + 250;

            var keyOf = new Dictionary<uint, string>();
            var candidates = new HashSet<uint>();
            var recordable = new HashSet<uint>();
            var streamable = new Dictionary<uint, string>();
            string recChannel = _recChannel;
            var sm = _streamMix;
            foreach (var s in _sessions.Values)
            {
                if (s.Pid == 0 || s.Pid == _ownPid) continue;
                keyOf[s.Pid] = s.AppKey;
                if (s.Dsp || s.AppKey == AppKeys.System) continue;
                bool rec = recChannel != null && s.Applied != null && s.Applied.ChannelId == recChannel;
                if (rec) recordable.Add(s.Pid);
                bool str = sm != null && sm.DeviceId != null && s.Applied != null && sm.SendOf(s.Applied.ChannelId) > 0f;
                if (str) streamable[s.Pid] = s.Applied.ChannelId;
                // 3.0.3: nach einer Pause bleibt die Messung laenger bereit (10 min statt 10 s) – sonst startet sie genau dann
                // neu, wenn die Musik wieder anfaengt (kurzer Aussetzer/Knacken, Pegel springt beim Umschalten der Messung)
                long keep = CleanStart.MeterKeep && s.LastActiveMs > 0 ? CleanStart.KeepMs : 10000;
                if (rec || str || s.State == AudioSessionState.Active || now - s.LastActiveMs < keep) candidates.Add(s.Pid);
            }

            var parents = _parentMap;
            var excluded = new HashSet<uint>();
            foreach (var kv in keyOf)
            {
                uint cur = kv.Key;
                var visited = new HashSet<uint> { cur };
                for (int depth = 0; depth < 32; depth++)
                {
                    if (!parents.TryGetValue(cur, out uint parent) || parent == 0 || !visited.Add(parent)) break;
                    if (keyOf.TryGetValue(parent, out string pkey))
                    {
                        if (pkey != kv.Value) excluded.Add(parent);              // anderer App-Ton waere mit drin
                        else if (candidates.Contains(parent)) excluded.Add(kv.Key); // Elternprozess misst schon mit
                    }
                    cur = parent;
                }
            }

            var pids = new HashSet<uint>();
            foreach (var pid in candidates) if (!excluded.Contains(pid) && !_dsp.IsFailed(pid)) pids.Add(pid);
            var recPids = new HashSet<uint>();
            foreach (var pid in recordable) if (pids.Contains(pid)) recPids.Add(pid);
            var streamPids = new Dictionary<uint, string>();
            foreach (var kv in streamable) if (pids.Contains(kv.Key)) streamPids[kv.Key] = kv.Value;
            if (!SameMap(streamPids, _lastStreamPids))
            {
                _lastStreamPids = streamPids;
                _dsp.SetStreamPids(new Dictionary<uint, string>(streamPids));
            }
            if (!recPids.SetEquals(_lastRecPids))
            {
                _lastRecPids = recPids;
                _dsp.SetRecordPids(new HashSet<uint>(recPids));
            }
            if (!pids.SetEquals(_lastMeterPids))
            {
                _lastMeterPids = pids;
                _dsp.SetMeterPids(new HashSet<uint>(pids));
            }
            UpdateAnalysis();   // 2.17: Lautheit/Spektrum pro Kanal
        }

        private Dictionary<uint, float> _lastAppGains = new Dictionary<uint, float>();

        private static bool SameGains(Dictionary<uint, float> a, Dictionary<uint, float> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        private static bool SameMap(Dictionary<uint, string> a, Dictionary<uint, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        private static void ApplyPan(SessionEntry s, float pan, bool isNew)
        {
            if (s.Channels == null || s.ChannelCount < 2) return;
            uint n = s.ChannelCount;
            float left = pan > 0 ? 1f - pan : 1f;
            float right = pan < 0 ? 1f + pan : 1f;
            var levels = new float[n];
            for (int i = 0; i < n; i++) levels[i] = 1f;
            levels[0] = left; levels[1] = right;
            if (n >= 6) { levels[4] = left; levels[5] = right; }
            if (n >= 8) { levels[6] = left; levels[7] = right; }

            if (isNew)
            {
                // Nur schreiben, wenn es wirklich etwas zu aendern gibt
                var cur = new float[n];
                if (s.Channels.GetAllVolumes(n, cur) == 0)
                {
                    bool same = true;
                    for (int i = 0; i < n; i++) if (Math.Abs(cur[i] - levels[i]) > 0.001f) { same = false; break; }
                    if (same) return;
                }
            }
            s.Channels.SetAllVolumes(n, levels, ref EventContext);
        }

        private void ApplyRouting(SessionEntry s, AppPolicy p)
        {
            if (!_routingSupported || s.Pid == 0 || s.AppKey == AppKeys.System) return;

            string want = p.DeviceId;
            if (want != null && !_devices.ContainsKey(want))
                return; // Geraet gerade nicht angeschlossen – wird erneut versucht, sobald es auftaucht

            if (want == null && !p.EnforceDevice)
            {
                // "Standard" ohne Zwang: nur zuruecksetzen, was wir selbst umgeleitet haben
                if (_routedPid.TryGetValue(s.Pid, out var mine) && mine.Length > 0)
                {
                    _routing.SetAppDevice(s.Pid, null);
                    _routedPid[s.Pid] = "";
                }
                return;
            }

            string target = want ?? "";
            if (_routedPid.TryGetValue(s.Pid, out var done) && string.Equals(done, target, StringComparison.OrdinalIgnoreCase))
                return;

            string current = _routing.GetAppDevice(s.Pid) ?? "";
            if (!string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
                _routing.SetAppDevice(s.Pid, want);
            _routedPid[s.Pid] = target;
        }

        // ------------------------------------------------------------------ Pegel

        private long _lastWakeScanMs = -10000;

        private long _nextInactivePoll;

        private void ReadLevels()
        {
            bool wakeScan = false;
            // 3.4.1: pausierte Sessions melden den Start jetzt selbst (OnStateChanged) – ihre Pegel nur noch alle 100 ms
            // nachsehen statt bei jedem Durchgang (bei vielen pausierten Apps tausende Abfragen pro Sekunde)
            long nowPoll = _engineClock.ElapsedMilliseconds;
            bool inactiveTurn = nowPoll >= _nextInactivePoll;
            if (inactiveTurn) _nextInactivePoll = nowPoll + 100;
            foreach (var s in _sessions.Values)
            {
                float l = 0, r = 0;
                // 3.0.3: auch Sessions messen, die beim letzten Suchen noch pausiert waren – sonst blieb der Pegel nach dem
                // Start bis zu 1 s leer und sprang dann hoch. Kommt dort Ton, gleich neu suchen (Status, Messung).
                bool known = s.State == AudioSessionState.Active;
                if (s.Meter != null && (known || (CleanStart.On && s.State == AudioSessionState.Inactive && (s.Guard == null || inactiveTurn))))
                {
                    try
                    {
                        if (s.MeterBuf != null && s.Meter.GetChannelsPeakValues((uint)s.MeterBuf.Length, s.MeterBuf) == 0)
                            SplitLR(s.MeterBuf, out l, out r);
                        else
                        {
                            if (s.Meter.GetPeakValue(out float pk) == 0) l = r = pk;
                            if (s.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0 && (s.MeterBuf == null || s.MeterBuf.Length != mc))
                                s.MeterBuf = new float[mc];
                        }
                    }
                    catch { }
                    if (!known && Math.Max(l, r) > 0.0001f) wakeScan = true;
                }
                s.PeakL = l; s.PeakR = r;
                s.CaptureMeter = false;
            }
            if (wakeScan && _engineClock.ElapsedMilliseconds - _lastWakeScanMs > 250)
            {
                _lastWakeScanMs = _engineClock.ElapsedMilliseconds;
                _sessionsDirty = true;
            }

            // Gemessener Prozess-Pegel ersetzt den Windows-Session-Pegel (der bei Desktop-Mitschnitt falsch ist)
            var pidPeaks = DspPidPeaks(false);
            if (pidPeaks != null && pidPeaks.Count > 0)
            {
                // Pegel gilt fuer die ganze App (gemessener Prozess inkl. Unterprozesse derselben App)
                var appPeak = new Dictionary<string, float[]>(StringComparer.Ordinal);
                foreach (var s in _sessions.Values)
                {
                    if (s.Dsp || !pidPeaks.TryGetValue(s.Pid, out var pk)) continue;
                    if (!appPeak.TryGetValue(s.AppKey, out var ap)) appPeak[s.AppKey] = ap = new float[2];
                    ap[0] = Math.Max(ap[0], pk[0]); ap[1] = Math.Max(ap[1], pk[1]);
                }
                foreach (var s in _sessions.Values)
                {
                    if (s.Dsp || !appPeak.TryGetValue(s.AppKey, out var ap)) continue;
                    s.PeakL = ap[0]; s.PeakR = ap[1];
                    s.CaptureMeter = true;
                }
            }

            long nowMs = _engineClock.ElapsedMilliseconds;
            foreach (var s in _sessions.Values)
            {
                // Bei -80 dB: hat jemand anderes (Windows-Mixer, die App selbst) die Lautstaerke veraendert?
                // Dann wieder setzen – sonst saehe es so aus, als messe Windows vor der Lautstaerke.
                if (s.CaptureMeter && !s.MeterPreVolume && s.HasApplied && !s.AppliedMute && s.AppliedVolume <= 0.0002f && s.Volume != null)
                {
                    try
                    {
                        if (s.Volume.GetMasterVolume(out float cv) == 0 && s.Volume.GetMute(out bool cm) == 0 &&
                            (cm || Math.Abs(cv - s.AppliedVolume) > Math.Max(1e-5f, s.AppliedVolume * 0.05f)))
                        {
                            s.Volume.SetMasterVolume(s.AppliedVolume, ref EventContext);
                            if (cm) s.Volume.SetMute(false, ref EventContext);
                            s.PreVotes = 0;
                            s.GainChangedMs = nowMs;   // Messung erst wieder werten, wenn sie sich beruhigt hat
                        }
                    }
                    catch { }
                }
                SplitPrePost(s, nowMs);
            }

            // Apps auf dem EQ-Weg: Pegel der Aufnahme (enthaelt die App-Lautstaerke, nicht den Kanal-Fader)
            var eqPeaks = DspPidPeaks(true);
            if (eqPeaks != null)
                foreach (var s in _sessions.Values)
                {
                    if (!s.Dsp || s.Applied == null || !eqPeaks.TryGetValue(s.Pid, out var pk)) continue;
                    // Messung vor der eigenen App-Lautstaerke (die wendet der Mischer an)
                    float ag = s.Applied.AppGain, ch = s.Applied.Mute ? 0f : Clamp01(s.Applied.Volume);
                    s.PreL = pk[0]; s.PreR = pk[1];
                    s.PeakL = pk[0] * ag * ch; s.PeakR = pk[1] * ag * ch;
                }

            // 3.4.1: Geraete-Lautstaerke aendert sich selten – hoechstens alle 50 ms lesen (bisher bei jedem Durchgang, alle 5 ms,
            // drei Abfragen pro Geraet). Die Geraete-Pegel misst der schnelle Pegel-Faden ohnehin – dann hier nicht noch einmal.
            bool devVol = nowMs >= _nextDevVolMs;
            if (devVol) _nextDevVolMs = nowMs + 50;
            bool devMeter = !FastLevelsRunning;
            bool sur = SurroundDue(nowMs);   // 4.0
            foreach (var d in _devices.Values)
            {
                try
                {
                    if (sur) ReadSurround400(d.Id, d.Meter, ref d.SurBuf);
                    if (d.Volume != null && devVol)
                    {
                        if (d.Volume.GetMasterVolumeLevelScalar(out float v) == 0) d.Vol = v;
                        if (d.Volume.GetMasterVolumeLevel(out float db) == 0) d.VolDb = db;
                        if (d.Volume.GetMute(out bool m) == 0) d.Mute = m;
                    }
                    if (!devMeter) continue;
                    float l = 0, r = 0;
                    if (d.Meter != null)
                    {
                        if (d.MeterBuf != null && d.Meter.GetChannelsPeakValues((uint)d.MeterBuf.Length, d.MeterBuf) == 0)
                            SplitLR(d.MeterBuf, out l, out r);
                        else if (d.Meter.GetPeakValue(out float pk) == 0)
                        {
                            l = r = pk;
                            if (d.Meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0) d.MeterBuf = new float[mc];
                        }
                    }
                    d.PeakL = l; d.PeakR = r;
                }
                catch { }
            }
        }

        private Dictionary<uint, float[]> _lastPidPeaks, _lastEqPidPeaks;

        /// <summary>Pegel der mitgehoerten Prozesse (eq = EQ-Weg): lueckenlos oder wie bisher der letzte Messwert.</summary>
        private Dictionary<uint, float[]> DspPidPeaks(bool eq)
        {
            if (_dsp == null) return null;
            if (!LevelFlow.Seamless) return eq ? _dsp.EqPidPeaks : _dsp.PidPeaks;
            var d = _dsp.TakePidPeaks(eq);
            if (d != null) { if (eq) _lastEqPidPeaks = d; else _lastPidPeaks = d; }
            return eq ? _lastEqPidPeaks ?? _dsp.EqPidPeaks : _lastPidPeaks ?? _dsp.PidPeaks;
        }

        private void ReadMicLevels()
        {
            var m = _mic;
            if (m == null) return;
            try
            {
                if (m.Volume != null)
                {
                    if (m.Volume.GetMasterVolumeLevelScalar(out float v) == 0) m.Vol = v;
                    if (m.Volume.GetMasterVolumeLevel(out float db) == 0) m.VolDb = db;
                    if (m.Volume.GetMute(out bool mu) == 0) m.Mute = mu;
                }
                float l = 0, r = 0;
                if (m.Meter != null)
                {
                    if (m.MeterBuf != null && m.Meter.GetChannelsPeakValues((uint)m.MeterBuf.Length, m.MeterBuf) == 0)
                        SplitLR(m.MeterBuf, out l, out r);
                    else if (m.Meter.GetPeakValue(out float pk) == 0) l = r = pk;
                }
                m.PeakL = l; m.PeakR = r;
            }
            catch { }
        }

        /// <summary>
        /// Teilt die Messung in "eigentlich" (vor Lautstaerke/Stumm) und "hoerbar". Die Prozess-Messung enthaelt
        /// normalerweise die Session-Lautstaerke -> zurueckrechnen. Kurz nach einer Aenderung wird mit dem
        /// hoeheren Wert gerechnet (die Messung hinkt hinterher), damit der Vorher-Pegel nicht hochspringt.
        /// </summary>
        private static void SplitPrePost(SessionEntry s, long nowMs)
        {
            float ml = s.PeakL, mr = s.PeakR;
            if (!s.CaptureMeter || !s.HasApplied)
            {
                // Windows-Session-Pegel: misst er trotz Stumm etwas, ist das der Vorher-Pegel
                s.PreL = ml; s.PreR = mr;
                if (s.HasApplied && s.AppliedMute) { s.PeakL = 0; s.PeakR = 0; }
                return;
            }
            float g = s.AppliedMute ? 0f : s.AppliedVolume;
            float gl = PanL(s.AppliedPan), gr = PanR(s.AppliedPan);

            // Liefert Windows die Messung ungeachtet der Lautstaerke? (bei -80 dB deutlich hoerbarer Pegel)
            if (!s.MeterPreVolume && g > 0f && g <= 0.0002f && nowMs - s.GainChangedMs > 500)
            {
                if (Math.Max(ml, mr) > 0.003f) { if (++s.PreVotes >= 10) s.MeterPreVolume = true; }   // 3.4.1: nicht mehr fuer den EQ-Weg uebernehmen (ungeprueft)
                else if (Math.Max(ml, mr) > 0f) s.PreVotes = 0;
            }
            if (s.MeterPreVolume)
            {
                s.PreL = ml; s.PreR = mr;
                s.PeakL = ml * g * gl; s.PeakR = mr * g * gr;
                return;
            }
            float dl = g * gl, dr = g * gr;
            for (int i = 0; i < s.HistMs.Length; i++)
                if (nowMs - s.HistMs[i] < 160)
                {
                    if (s.HistL[i] > dl) dl = s.HistL[i];
                    if (s.HistR[i] > dr) dr = s.HistR[i];
                }
            const float Min = 1e-6f;
            float pl = dl > Min ? ml / dl : -1f, pr = dr > Min ? mr / dr : -1f;
            if (pl < 0) pl = pr; if (pr < 0) pr = pl;   // hart gepannt: andere Seite nehmen
            s.PreL = Math.Min(4f, Math.Max(0f, pl));
            s.PreR = Math.Min(4f, Math.Max(0f, pr));
        }

        private static float PanL(float pan) => pan > 0 ? 1f - pan : 1f;
        private static float PanR(float pan) => pan < 0 ? 1f + pan : 1f;

        private static void SplitLR(float[] b, out float l, out float r)
        {
            int n = b.Length;
            if (n == 1) { l = r = b[0]; return; }
            l = b[0]; r = b[1];
            if (n >= 3) { l = Math.Max(l, b[2]); r = Math.Max(r, b[2]); } // Center auf beide Seiten
            if (n >= 6) { l = Math.Max(l, b[4]); r = Math.Max(r, b[5]); }
            if (n >= 8) { l = Math.Max(l, b[6]); r = Math.Max(r, b[7]); }
        }

        // ------------------------------------------------------------------ Gehoerschutz

        private float CapGain(string appKey) =>
            !_final && appKey != null && _cap.TryGetValue(appKey, out var c) ? c.Gain : 1f;

        /// <summary>
        /// Gehoerschutz fuer Apps auf dem direkten Weg: Ueberschreitet der gemessene Pegel einer App die Grenze,
        /// wird sie sofort um den Ueberschuss abgesenkt und danach langsam (8 dB/s) wieder freigegeben.
        /// Apps ueber den EQ-Weg begrenzt der Mischer selbst (exakt, ohne Verzoegerung).
        /// </summary>
        private void UpdateHearingCap()
        {
            long now = _engineClock.ElapsedMilliseconds;
            float dt = Math.Min(0.2f, (now - _lastCapMs) / 1000f);
            _lastCapMs = now;
            float capDb = _capDb;
            if (capDb >= 0f || _final) { if (_cap.Count > 0) _cap.Clear(); return; }
            float limit = (float)Math.Pow(10, capDb / 20.0);

            var level = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var s in _sessions.Values)
            {
                if (s.Dsp || s.AppKey == null) continue;
                float pk = Math.Max(s.PeakL, s.PeakR);
                level[s.AppKey] = level.TryGetValue(s.AppKey, out float o) ? Math.Max(o, pk) : pk;
            }
            foreach (var kv in level)
            {
                if (!_cap.TryGetValue(kv.Key, out var st)) { if (kv.Value <= limit) continue; _cap[kv.Key] = st = new CapLimiter(); }
                st.Update(kv.Value, limit, now, dt);
            }
            foreach (var key in new List<string>(_cap.Keys))
                if (!level.ContainsKey(key) || _cap[key].Gain >= 1f) _cap.Remove(key);
        }

        // ------------------------------------------------------------------ Snapshot

        /// <summary>Spektrum, Goniometer und Lautheit hoechstens alle 25 ms neu rechnen (die Pegel kommen oefter).</summary>
        private float[] HeavySpec()
        {
            long now = _engineClock.ElapsedMilliseconds;
            if (now - _heavyAtMs >= 25 || !_realtime)
            {
                _heavyAtMs = now;
                _heavySpec = ComputeMasterSpectrum();
                _heavyLoud = _lufsSink?.Compute();
                _heavyPeaks = LoudnessSink.TruePeakOn ? _lufsSink?.Peaks() : null;   // 3.4.6
                _heavyBeat = BeatTracker.On ? _lufsSink?.Beat() : null;
                _heavyCorr350 = LoudnessSink.CorrOn ? (_lufsSink?.Correlation() ?? float.NaN) : float.NaN;   // 3.5
                _heavyBass = LoudnessSink.BassOn ? (_lufsSink?.Bass() ?? -1f) : -1f;
                _heavyLoudDev = ComputeDeviceLoudness();
                _heavyScope = ComputeScope(out _heavyCorr);
            }
            return _heavySpec;
        }

        private void Publish()
        {
            CombineAppAnalysis();
            var devices = new List<DeviceState>(_devices.Count);
            foreach (var d in _devices.Values)
            {
                devices.Add(new DeviceState
                {
                    Id = d.Id, Name = d.Name, FullName = d.FullName,
                    IsDefault = string.Equals(d.Id, _defaultId, StringComparison.OrdinalIgnoreCase),
                    Volume = d.Vol, VolumeDb = d.VolDb, Mute = d.Mute, PeakL = d.PeakL, PeakR = d.PeakR,
                    MinDb = d.MinDb, MaxDb = d.MaxDb,
                    FormFactor = d.FormFactor, Bluetooth = d.Bluetooth, Usb = d.Usb, FormatText = d.FormatText,   // 3.5
                    ChanPeaks = TakeSurround400(d.Id)   // 4.0
                });
            }
            devices.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase));

            var apps = new Dictionary<string, AppState>(StringComparer.Ordinal);
            var appDevices = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var s in _sessions.Values)
            {
                if (!apps.TryGetValue(s.AppKey, out var a))
                {
                    a = new AppState { Key = s.AppKey, Name = s.Name, Path = s.Path };
                    apps[s.AppKey] = a;
                    appDevices[s.AppKey] = new List<string>();
                }
                if (s.State == AudioSessionState.Active)
                {
                    a.Active = true;
                    var list = appDevices[s.AppKey];
                    if (!list.Contains(s.DeviceId)) list.Add(s.DeviceId);
                }
                if (a.Path == null && s.Path != null) a.Path = s.Path;
                if (s.Dsp) a.Captured = true;
                if (s.CaptureMeter) a.Metered = true;
                a.PeakL = Math.Max(a.PeakL, s.PeakL);
                a.PeakR = Math.Max(a.PeakR, s.PeakR);
                a.PreL = Math.Max(a.PreL, s.PreL);
                a.PreR = Math.Max(a.PreR, s.PreR);
            }
            var appArr = new List<AppState>(apps.Values);
            foreach (var a in appArr)
            {
                var list = appDevices[a.Key];
                list.Sort(StringComparer.OrdinalIgnoreCase);
                a.DeviceIds = list.ToArray();
            }
            appArr.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            var mic = _mic;
            DeviceState micState = mic == null ? null : new DeviceState
            {
                Id = mic.Id, Name = mic.Name, FullName = mic.FullName, IsDefault = true,
                Volume = mic.Vol, VolumeDb = mic.VolDb, Mute = mic.Mute, PeakL = mic.PeakL, PeakR = mic.PeakR
            };
            var caps = new List<DeviceState>();
            foreach (var id in _captureIds)
            {
                _captureNames.TryGetValue(id, out var n);
                caps.Add(new DeviceState
                {
                    Id = id, Name = n?[0] ?? "Mikrofon", FullName = n?[1] ?? "Mikrofon",
                    IsDefault = mic != null && string.Equals(mic.Id, id, StringComparison.OrdinalIgnoreCase)
                });
            }

            var sb = new StringBuilder();
            sb.Append(mic?.Id).Append(mic?.Name).Append(mic?.LiveClient != null ? 'L' : 'l').Append('|');
            foreach (var c in caps) sb.Append(c.Id.GetHashCode()).Append(',');
            sb.Append(_routingSupported ? 'R' : 'r').Append(_error != null ? 'E' : 'e').Append(_dsp?.Status ?? "").Append('|');
            foreach (var d in devices) sb.Append(d.Id).Append(d.IsDefault ? "*" : "").Append(d.Name).Append('|');
            sb.Append('#');
            foreach (var a in appArr)
            {
                sb.Append(a.Key).Append(':').Append(a.Name).Append(a.Active ? '+' : '-').Append(a.Captured ? 'E' : 'e');
                foreach (var id in a.DeviceIds) sb.Append(id.GetHashCode()).Append(',');
                sb.Append('|');
            }

            var ns = new EngineSnapshot
            {
                Devices = devices.ToArray(),
                Apps = appArr.ToArray(),
                RoutingSupported = _routingSupported,
                Error = _devices.Count == 0 && _error != null ? _error : null,
                Structure = sb.ToString(),
                ChannelPeaks = DspChannelPeaks(),
                Mic = micState,
                CaptureDevices = caps.ToArray(),
                MicLive = mic?.LiveClient != null,
                EqSupported = _dsp != null,
                StreamPeak = _dsp?.StreamPeak ?? new float[2],
                VoicePeak = _dsp?.VoicePeak ?? 0f,
                VoiceOutPeak = _dsp?.VoiceOutPeak ?? 0f,
                VoiceActive = _dsp?.VoiceActive ?? false,
                VoiceStatus = _voiceOut == null ? null : (_dsp == null ? "Braucht Windows 10 Version 2004 oder neuer" : _dsp.VoiceStatus),
                SoundsPlaying = _dsp?.SoundsPlaying ?? 0,
                Spectrum = _dsp?.Spectrum,
                MasterSpectrum = HeavySpec(),
                Loudness = _heavyLoud,
                MasterPeaks = _heavyPeaks,
                Beat = _heavyBeat,
                Correlation = _heavyCorr350, MasterBass = _heavyBass,   // 3.5
                NoisePeak = _dsp?.NoisePeak, EqLatencyMs = _dsp?.LatencyMs ?? 0,
                LoudnessByDevice = _heavyLoudDev,
                ChannelLoudness = _dsp?.ChannelLoudness,
                ChannelSpectra = _dsp?.ChannelSpectra,
                AppLoudness = _anAppLoud,
                AppSpectra = _anAppSpec,
                Scope = _heavyScope,
                ScopeCorr = _heavyCorr,
                LevelerGain = _dsp?.LevelerGain ?? new Dictionary<string, float>(),
                CompReduction = _dsp?.CompReduction ?? new Dictionary<string, float>(),
                EqStatus = _dsp == null
                    ? (DspMixer.IsSupported ? "EQ konnte nicht gestartet werden" : "EQ benötigt Windows 10 Version 2004 oder neuer")
                    : _dsp.Status
            };
            // 3.0.2: hat die Oberflaeche den vorigen Schnappschuss noch nicht abgeholt, dessen Spitzen mitnehmen
            var prev = _snapshot;
            ns.Seq = (prev?.Seq ?? 0) + 1;
            LevelFlow.Merge(prev, ns, _engineClock.ElapsedMilliseconds);
            _snapshot = ns;
        }

        private Dictionary<string, float[]> _lastDspPeaks = new Dictionary<string, float[]>();

        /// <summary>Kanal-Pegel des EQ-Mischers: lueckenlos (alles seit dem letzten Abholen) oder wie bisher der letzte Messwert.</summary>
        private Dictionary<string, float[]> DspChannelPeaks()
        {
            if (_dsp == null) return new Dictionary<string, float[]>();
            if (!LevelFlow.Seamless) return _dsp.Peaks ?? new Dictionary<string, float[]>();
            var d = _dsp.TakePeaks();
            if (d != null) _lastDspPeaks = d;
            return _lastDspPeaks;
        }

        private void Shutdown()
        {
            try { _loopRec?.Dispose(); } catch { }
            try { _lufsWanted = false; lock (_lufsLock) { _lufsRec?.Dispose(); _lufsRec = null; _lufsSink = null; } } catch { }
            try { StopLoudTaps(); } catch { }
            try { _specKey = ""; lock (_specLock) { foreach (var t in _specTaps) { try { t.Rec?.Dispose(); } catch { } } _specTaps = new SpecTap[0]; } } catch { }
            // noch anstehende Befehle ausfuehren (z. B. Mikro wieder an), dann Endzustand setzen
            while (_queue.TryDequeue(out var action)) { try { action(); } catch { } }
            FinalState();
            try { DestroyMic(); } catch { }
            try
            {
                foreach (var s in _sessions.Values) ReleaseSession(s);
                _sessions.Clear();
                foreach (var d in _devices.Values) DestroyDevice(d);
                _devices.Clear();
                ComUtil.Release(_enumerator);
            }
            catch { }
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
