using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Mischpult.Audio
{
    /// <summary>Simulierte Audio-Engine fuer den Vorschau-Modus (Start mit --demo).</summary>
    internal sealed class DemoEngine : IAudioBackend
    {
        private sealed class FakeApp
        {
            public string Key, Name, Path; public bool Active; public float Level; public double Rate; public double StartAt;
        }

        private readonly object _lock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Random _rnd = new Random(7);
        private PolicyTable _policy = PolicyTable.Empty;
        private readonly Dictionary<string, float> _devVol = new Dictionary<string, float>();
        private readonly Dictionary<string, bool> _devMute = new Dictionary<string, bool>();

        private readonly string[][] _devices =
        {
            new[] { "demo-spk", "Lautsprecher", "Lautsprecher (Realtek(R) Audio)" },
            new[] { "demo-hp", "Headset", "Headset (USB Audio Device)" },
            new[] { "demo-cable", "CABLE Input", "CABLE Input (VB-Audio Virtual Cable)" },
        };

        private readonly List<FakeApp> _apps = new List<FakeApp>
        {
            new FakeApp { Key = "game.exe",     Name = "Spiel",       Active = true,  Level = 0.85f, Rate = 1.7 },
            new FakeApp { Key = "discord.exe",  Name = "Discord",     Active = true,  Level = 0.55f, Rate = 0.6 },
            new FakeApp { Key = "spotify.exe",  Name = "Spotify",     Active = true,  Level = 0.7f,  Rate = 2.3 },
            new FakeApp { Key = "firefox.exe",  Name = "Firefox",     Active = true,  Level = 0.35f, Rate = 1.1 },
            new FakeApp { Key = "chrome.exe",   Name = "Google Chrome", Active = false, Level = 0.3f, Rate = 1.0 },
            new FakeApp { Key = "obs64.exe",    Name = "OBS Studio",  Active = false, Level = 0f,    Rate = 1.0 },
            new FakeApp { Key = AppKeys.System, Name = "Systemklänge", Active = true, Level = 0.2f,  Rate = 0.3 },
            // startet nach ein paar Sekunden (Spiele-Erkennung testen)
            new FakeApp { Key = "eldenring.exe", Name = "ELDEN RING", Active = true, Level = 0.8f, Rate = 1.3, StartAt = 4,
                          Path = @"D:\SteamLibrary\steamapps\common\ELDEN RING\Game\eldenring.exe" },
        };

        public DemoEngine()
        {
            _devVol["demo-spk"] = 0.62f;
            _devVol["demo-hp"] = 0.45f;
            _devVol["demo-cable"] = 1f;
        }

        public EngineSnapshot Snapshot
        {
            get
            {
                lock (_lock) return Build();
            }
        }

        public void SetPolicy(PolicyTable table) { lock (_lock) _policy = table ?? PolicyTable.Empty; }
        public void SetDeviceVolume(string id, float v) { lock (_lock) _devVol[id] = v; }
        /// <summary>3.4.3: wie Windows ungefaehr: -65,25 … 0 dB, 50 % ≈ -10 dB.</summary>
        public void SetDeviceVolumeDb(string id, float db) { lock (_lock) _devVol[id] = db <= -65.25f ? 0f : (float)Math.Pow(10, Math.Min(0f, db) / 33.22); }
        /// <summary>4.0: 5.1-Pegel fuer die Vorschau (L, R, C, LFE, SL, SR).</summary>
        private static float[] DemoSurround(float[] lr, double t)
        {
            float l = Math.Min(1f, lr[0]) * DemoQuiet, r = Math.Min(1f, lr[1]) * DemoQuiet;
            return new[] { l, r, (l + r) * 0.42f, (float)(0.5 + 0.35 * Math.Abs(Math.Sin(t * 2.1))) * DemoQuiet, l * 0.55f, r * 0.5f };
        }

        internal static float DemoDb(float v) => v <= 0.0001f ? -65.25f : Math.Max(-65.25f, (float)(33.22 * Math.Log10(v)));
        public void SetDeviceMute(string id, bool m) { lock (_lock) _devMute[id] = m; }
        public void ResetAllAppRouting() { }
        private string _default = "demo-spk";
        public void SetDefaultDevice(string id) { lock (_lock) { if (id.StartsWith("demo-mic")) _micId = id; else _default = id; } }
        private string _micId = "demo-mic1";
        private float _micVol = 0.8f;
        private bool _micMute, _micLive;
        public void SetMicVolume(float v) { lock (_lock) _micVol = v; }
        public void SetMicMute(bool m) { lock (_lock) _micMute = m; }
        public void SetMicLiveMeter(bool on) { lock (_lock) _micLive = on; }
        public void Flush(int timeoutMs) { }
        public void SetSoftFade(bool on) { }
        private float _cap;
        public void SetHearingCap(float db) { lock (_lock) _cap = db < 0 ? (float)Math.Pow(10, db / 20) : 0f; }

        private System.Threading.Thread _rec;
        private volatile bool _recRun;

        public string StartRecording(string channelId, string deviceId, WavWriter writer)
        {
            StopRecording();
            _recRun = true;
            _rec = new System.Threading.Thread(() =>
            {
                var buf = new float[960 * 2];
                long pos = 0;
                var sw = Stopwatch.StartNew();
                long written = 0;
                while (_recRun)
                {
                    System.Threading.Thread.Sleep(10);
                    long due = sw.ElapsedMilliseconds * 48 - written;
                    while (due >= 960)
                    {
                        for (int i = 0; i < 960; i++, pos++)
                        {
                            double tt = pos / 48000.0;
                            float v = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * tt) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.5 * tt)));
                            buf[i * 2] = v; buf[i * 2 + 1] = v * 0.8f;
                        }
                        writer.Write(buf, 960);
                        written += 960; due -= 960;
                    }
                }
            }) { IsBackground = true };
            _rec.Start();
            return null;
        }

        public string RecordingError => null;

        private StreamMix _stream;
        private VoiceOut _voice;
        private string _specChannel;
        private readonly List<KeyValuePair<double, double>> _playing = new List<KeyValuePair<double, double>>(); // Start, Ende
        public void SetStreamMix(StreamMix mix) { lock (_lock) _stream = mix; }
        // 2.19: in der Vorschau nur merken
        public Dictionary<string, OutputFx> DemoOutputFx;
        public CueMix DemoCue;
        public Dictionary<string, WavWriter> DemoTracks;
        public void SetOutputFx(Dictionary<string, OutputFx> fx) { DemoOutputFx = fx; }
        public void SetCue(CueMix cue) { DemoCue = cue; }
        public string DemoMirror; public int DemoMirrorDelay; public int DemoNoiseKind; public float DemoNoiseGain;   // 3.5
        public void SetMirror(string targetDeviceId, int delayMs) { DemoMirror = targetDeviceId; DemoMirrorDelay = delayMs; }
        public void SetNoise(int kind, float gain) { DemoNoiseKind = kind; DemoNoiseGain = gain; }
        private System.Threading.Thread _multiThread;
        private volatile bool _multiRun;
        public string StartMultitrack(Dictionary<string, WavWriter> writers)
        {
            StopMultitrack();
            DemoTracks = writers;
            var list = new List<WavWriter>(writers.Values);
            _multiRun = true;
            _multiThread = new System.Threading.Thread(() =>
            {
                // Vorschau: jede Spur ein eigener Ton (die zweite bleibt still – wie ein stummer Kanal)
                var buf = new float[960 * 2];
                var sw = Stopwatch.StartNew();
                long written = 0;
                while (_multiRun)
                {
                    System.Threading.Thread.Sleep(10);
                    long due = sw.ElapsedMilliseconds * 48 - written;
                    while (due >= 960)
                    {
                        for (int t = 0; t < list.Count; t++)
                        {
                            if (t == 1) { list[t].WriteSilence(960); continue; }
                            double f = 220 * (t + 1);
                            for (int i = 0; i < 960; i++)
                            {
                                double tt = (written + i) / 48000.0;
                                float v = (float)(0.2 * Math.Sin(2 * Math.PI * f * tt));
                                buf[i * 2] = v; buf[i * 2 + 1] = v;
                            }
                            list[t].Write(buf, 960);
                        }
                        written += 960; due -= 960;
                    }
                }
            }) { IsBackground = true };
            _multiThread.Start();
            return null;
        }
        public void StopMultitrack()
        {
            _multiRun = false;
            _multiThread?.Join(500);
            _multiThread = null;
            DemoTracks = null;
        }
        public void SetVoiceOut(VoiceOut v) { lock (_lock) _voice = v; }
        public void PlaySound(SoundClip clip, float gain, bool me, bool stream, bool voice)
        {
            lock (_lock) { double t = _clock.Elapsed.TotalSeconds; _playing.Add(new KeyValuePair<double, double>(t, t + clip.Seconds)); }
        }
        public void PlaySoundOn(SoundClip clip, float gain, string deviceId) => PlaySound(clip, gain, true, false, false);
        public void StopSounds() { lock (_lock) _playing.Clear(); }
        public void SetSpectrumChannel(string channelId) { lock (_lock) _specChannel = channelId; }

        private bool _masterSpec;
        public void SetMasterSpectrum(string[] deviceIds) { lock (_lock) _masterSpec = deviceIds != null && deviceIds.Length > 0; }
        private bool _lufsOn;
        private double _lufsSum, _lufsN;
        public void SetLoudness(bool on) { lock (_lock) _lufsOn = on; }
        private bool _scopeOn;
        private readonly Random _scopeRnd = new Random(3);
        public void SetScope(bool on) { lock (_lock) _scopeOn = on; }

        /// <summary>Demo: Stereo-Ton mit etwas Breite fuers Goniometer.</summary>
        private float[] FakeScope(double t, out float corr)
        {
            corr = 0f;
            if (!_scopeOn || !_masterSpec) return null;
            int n = 1024; var r = new float[n * 2];
            double amp = 0.35 + 0.2 * Math.Sin(t * 0.9);
            for (int i = 0; i < n; i++)
            {
                double x = t * 48000 + i;
                // wie Musik: Mitte kraeftig, Seiten schwaecher – beides eher normalverteilt (ergibt eine senkrechte Ellipse)
                double g1 = (_scopeRnd.NextDouble() + _scopeRnd.NextDouble() + _scopeRnd.NextDouble() - 1.5) * 0.8;
                double g2 = (_scopeRnd.NextDouble() + _scopeRnd.NextDouble() + _scopeRnd.NextDouble() - 1.5) * 0.8;
                double mid = g1 * 0.7 + Math.Sin(x * 0.031) * 0.25;
                double side = g2 * 0.3 + Math.Sin(x * 0.0123 + 2) * 0.08;
                r[i * 2] = (float)((mid + side) * amp);
                r[i * 2 + 1] = (float)((mid - side) * amp);
            }
            corr = (float)(0.55 + 0.25 * Math.Sin(t * 0.5));
            return r;
        }
        public void SetPerf(bool fastLevels, bool boost) { lock (_lock) { DemoFastLevels = fastLevels; DemoBoost = boost; } }
        public bool DemoFastLevels, DemoBoost;
        public void RefreshLevels(EngineSnapshot snap) { }
        public bool DemoRealtime = true;
        public string[] DemoScopeIds;
        public void SetRealtime(bool on) { DemoRealtime = on; }
        public void SetScopeSource(string[] deviceIds) { DemoScopeIds = deviceIds; }
        public void ResetLoudness() { lock (_lock) { _lufsSum = 0; _lufsN = 0; _devLufs.Clear(); } }
        public string[] DemoLoudIds;
        private readonly Dictionary<string, double[]> _devLufs = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        public void SetLoudnessDevices(string[] deviceIds) { lock (_lock) DemoLoudIds = deviceIds; }

        /// <summary>Demo: Lautheit weiterer Ausgaenge – das Headset leiser als die Lautsprecher.</summary>
        private Dictionary<string, float[]> FakeDeviceLoudness(double t)
        {
            var ids = DemoLoudIds;
            if (ids == null || ids.Length == 0) return null;
            var d = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                double off = id == "demo-hp" ? -4.2 : id == "demo-spk" ? 0.4 : -2.0, ph = (id.GetHashCode() & 0xff) / 40.0;
                float m = (float)(-15.5 + off + 2.8 * Math.Sin(t * 1.1 + ph) + 1.1 * Math.Sin(t * 2.7 + ph));
                float st = (float)(-16.2 + off + 1.2 * Math.Sin(t * 0.35 + ph));
                if (!_devLufs.TryGetValue(id, out var acc)) _devLufs[id] = acc = new double[2];
                acc[0] += Math.Pow(10, (st + 0.691) / 10); acc[1]++;
                d[id] = new[] { m, st, (float)(-0.691 + 10 * Math.Log10(acc[0] / acc[1])) };
            }
            return d;
        }

        // 2.17: Lautheit/Spektrum pro Kanal (Demo: jeder Kanal etwas anders)
        public string[] DemoLoudCh, DemoSpecCh;
        private readonly Dictionary<string, double[]> _chLufs = new Dictionary<string, double[]>();
        public void SetChannelAnalysis(string[] loudChannels, string[] specChannels) { lock (_lock) { DemoLoudCh = loudChannels; DemoSpecCh = specChannels; } }

        private Dictionary<string, float[]> FakeChannelLoudness(double t)
        {
            var ids = DemoLoudCh;
            if (ids == null || ids.Length == 0) return null;
            var d = new Dictionary<string, float[]>();
            foreach (var id in ids)
            {
                double ph = (id.GetHashCode() & 0xffff) / 9000.0, off = ((id.GetHashCode() >> 4) & 15) - 7.5;
                float m = (float)(-17 + off + 3.2 * Math.Sin(t * 1.2 + ph) + 1.4 * Math.Sin(t * 2.9 + ph * 2));
                float st = (float)(-17.5 + off + 1.3 * Math.Sin(t * 0.4 + ph));
                if (!_chLufs.TryGetValue(id, out var acc)) _chLufs[id] = acc = new double[2];
                acc[0] += Math.Pow(10, (st + 0.691) / 10); acc[1]++;
                d[id] = new[] { m, st, (float)(-0.691 + 10 * Math.Log10(acc[0] / acc[1])) };
            }
            return d;
        }

        private Dictionary<string, float[]> FakeChannelSpectra(double t)
        {
            var ids = DemoSpecCh;
            if (ids == null || ids.Length == 0) return null;
            var d = new Dictionary<string, float[]>();
            foreach (var id in ids)
            {
                double ph = (id.GetHashCode() & 0xffff) / 5000.0;
                var s = FakeMasterSpectrum(t + ph);
                double tilt = ((id.GetHashCode() >> 3) & 7) - 3.5;   // manche Kanaele heller, manche bassiger
                for (int b = 0; b < s.Length; b++) s[b] += (float)(tilt * Math.Log(SpectrumAnalyzer.BandFreq(b + 0.5) / 1000, 2) - 2);
                d[id] = s;
            }
            return d;
        }

        /// <summary>Demo: Lautheit um −16 LUFS, leicht schwankend.</summary>
        private float[] FakeLoudness(double t)
        {
            if (!_lufsOn) return null;
            float m = (float)(-15.5 + 2.5 * Math.Sin(t * 1.3) + 1.2 * Math.Sin(t * 3.1));
            float st = (float)(-16.2 + 1.0 * Math.Sin(t * 0.4));
            _lufsSum += Math.Pow(10, (st + 0.691) / 10); _lufsN++;
            float i = (float)(-0.691 + 10 * Math.Log10(_lufsSum / _lufsN));
            return new[] { m, st, i };
        }

        /// <summary>Demo: Spektrum wie Musik – kraeftiger Bass, nach oben abfallend, mit Bewegung und "Schlag".</summary>
        private float[] FakeMasterSpectrum(double t)
        {
            var r = new float[SpectrumAnalyzer.Bands];
            double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2.1)), 6);   // Bassdrum
            for (int b = 0; b < r.Length; b++)
            {
                double f = SpectrumAnalyzer.BandFreq(b + 0.5);
                double oct = Math.Log(f / 1000, 2);
                double baseDb = -40 - 4.5 * oct - (f < 40 ? (40 - f) * 0.4 : 0) - (f > 14000 ? (f - 14000) / 400 : 0);
                double move = 5 * Math.Sin(t * 1.7 + b * 0.31) * Math.Sin(t * 0.53 + b * 0.07) + 3 * (_rnd.NextDouble() - 0.5);
                double kick = f < 150 ? 10 * beat : f < 400 ? 4 * beat : 0;
                double voice = f > 300 && f < 3500 ? 4 * Math.Max(0, Math.Sin(t * 0.9)) : 0;
                r[b] = (float)(baseDb + move + kick + voice);
            }
            return r;
        }

        private float[] FakeSpectrum(double t)
        {
            AppPolicy p = null;
            foreach (var a in _apps) { var q = _policy.Get(a.Key); if (q?.ChannelId == _specChannel) { p = q; break; } }
            if (p == null || p.Eq == null) return null;
            var low = new Biquad(); low.LowShelf(48000, DspMixer.FreqLow, p.Eq.Low);
            var mid = new Biquad(); mid.Peaking(48000, DspMixer.FreqMid, p.Eq.Mid, 0.7);
            var high = new Biquad(); high.HighShelf(48000, DspMixer.FreqHigh, p.Eq.High);
            var geq = new Biquad[10];
            for (int i = 0; i < 10; i++) { geq[i] = new Biquad(); geq[i].Peaking(48000, GraphicEq.Freqs[i], p.Eq.Graphic?[i] ?? 0, GraphicEq.Q); }
            var r = new float[SpectrumAnalyzer.Bands];
            for (int b = 0; b < r.Length; b++)
            {
                double f = SpectrumAnalyzer.BandFreq(b + 0.5);
                double baseDb = -24 - 9 * Math.Log(f / 300, 2) * (f > 300 ? 1 : -0.3) + 6 * Math.Sin(t * 2.1 + b * 0.37) * Math.Sin(t * 0.7 + b * 0.11) + 3 * (_rnd.NextDouble() - 0.5);
                double g = low.MagnitudeAt(48000, f) * mid.MagnitudeAt(48000, f) * high.MagnitudeAt(48000, f);
                foreach (var q in geq) g *= q.MagnitudeAt(48000, f);
                r[b] = (float)(baseDb + 20 * Math.Log10(g) - (f > 15000 ? (f - 15000) / 300 : 0));
            }
            return r;
        }

        public void StopRecording()
        {
            _recRun = false;
            _rec?.Join(500);
            _rec = null;
        }

        public void Dispose() { StopRecording(); }

        /// <summary>Nur Demo: Ausgang leiser (MISCHPULT_DEMO_QUIET = dB, z. B. -25), um das VU-Meter mit leisem Ton zu zeigen.</summary>
        private static readonly float DemoQuiet = float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_QUIET"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float q) ? (float)Math.Pow(10, Math.Min(0f, q) / 20f) : 1f;
        private static readonly bool Hotplug = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_HOTPLUG") == "1";

        /// <summary>Test: Headset ist zwischen 8 und 16 s (und alle 16 s erneut) abgesteckt.</summary>
        private bool HeadsetPresent(double t) => !Hotplug || ((int)(t / 8)) % 2 == 0;

        private EngineSnapshot Build()
        {
            double t = _clock.Elapsed.TotalSeconds;
            bool hs = HeadsetPresent(t);
            if (!hs && _default == "demo-hp") _default = "demo-spk";   // wie Windows: anderes Geraet wird Standard
            if (!hs && _micId == "demo-mic2") _micId = "demo-mic1";
            var apps = new List<AppState>();
            var devPeak = new Dictionary<string, float[]>();
            foreach (var d in _devices) devPeak[d[0]] = new float[2];

            var chPeaks = new Dictionary<string, float[]>();
            foreach (var a in _apps)
            {
                if (a.StartAt > 0 && t < a.StartAt) continue;
                var p = _policy.Get(a.Key);
                string dev = p?.DeviceId ?? _default;
                float gain = p == null ? 1f : (p.Mute ? 0f : p.Volume * p.AppGain);
                float l = 0, r = 0, preL = 0, preR = 0;
                if (a.Active && a.Level > 0)
                {
                    double env = 0.55 + 0.45 * Math.Sin(t * a.Rate + a.Key.Length);
                    double beat = Math.Pow(Math.Abs(Math.Sin(t * 3.1 * a.Rate)), 6);
                    float basev = (float)(a.Level * (0.35 + 0.45 * env + 0.25 * beat) * (0.85 + 0.15 * _rnd.NextDouble()));
                    float pan = p?.Pan ?? 0f;
                    preL = Math.Min(1f, basev * (0.9f + 0.1f * (float)Math.Sin(t * 5)));
                    preR = Math.Min(1f, basev * (0.9f + 0.1f * (float)Math.Cos(t * 4)));
                    l = preL * (pan > 0 ? 1 - pan : 1) * gain;
                    r = preR * (pan < 0 ? 1 + pan : 1) * gain;
                    if (_cap > 0) { l = Math.Min(l, _cap); r = Math.Min(r, _cap); }
                }
                bool eq = p?.Eq != null && a.Key != AppKeys.System;
                if (eq)
                {
                    // grobe Simulation: Anhebungen machen lauter
                    float boost = (float)Math.Pow(10, Math.Max(0, Math.Max(p.Eq.Low, Math.Max(p.Eq.Mid, p.Eq.High))) / 40.0);
                    if (!chPeaks.TryGetValue(p.Eq.ChannelId, out var cp)) chPeaks[p.Eq.ChannelId] = cp = new float[4];
                    cp[0] = Math.Max(cp[0], Math.Min(1f, l * boost));
                    cp[1] = Math.Max(cp[1], Math.Min(1f, r * boost));
                    cp[2] = Math.Max(cp[2], Math.Min(1f, preL * boost));
                    cp[3] = Math.Max(cp[3], Math.Min(1f, preR * boost));
                }
                apps.Add(new AppState
                {
                    Key = a.Key, Name = a.Name, Path = a.Path, Active = a.Active, PeakL = l, PeakR = r,       // wie die echte Engine: auch EQ-Apps haben einen App-Pegel
                    PreL = preL, PreR = preR,
                    DeviceIds = a.Active ? new[] { dev } : new string[0], Captured = eq
                });
                if (a.Active)
                {
                    devPeak[dev][0] = Math.Max(devPeak[dev][0], l);
                    devPeak[dev][1] = Math.Max(devPeak[dev][1], r);
                }
            }

            var devs = new List<DeviceState>();
            foreach (var d in _devices)
            {
                if (d[0] == "demo-hp" && !hs) continue;
                float v = _devVol.TryGetValue(d[0], out var vv) ? vv : 0.5f;
                bool m = _devMute.TryGetValue(d[0], out var mm) && mm;
                devs.Add(new DeviceState
                {
                    Id = d[0], Name = d[1], FullName = d[2], IsDefault = d[0] == _default,
                    Volume = v, VolumeDb = DemoDb(v), MinDb = -65.25f, MaxDb = 0f, Mute = m,
                    PeakL = Math.Min(1f, devPeak[d[0]][0]) * DemoQuiet, PeakR = Math.Min(1f, devPeak[d[0]][1]) * DemoQuiet,
                    FormFactor = d[0] == "demo-spk" ? 1 : d[0] == "demo-hp" ? 5 : 2, Usb = d[0] == "demo-hp",   // 3.5
                    FormatText = d[0] == "demo-spk" ? "48 kHz · 24 Bit" : d[0] == "demo-hp" ? "48 kHz · 16 Bit" : "44,1 kHz · 16 Bit",
                    ChanPeaks = AudioEngine.SurroundWanted && d[0] == "demo-spk" && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SURROUND") == "1" ? DemoSurround(devPeak[d[0]], t) : null,   // 4.0
                });
            }

            double talk = Math.Max(0, Math.Sin(t * 0.7)) * (0.5 + 0.5 * Math.Abs(Math.Sin(t * 9)));
            float mp = (float)(talk * 0.6 * _micVol * (0.8 + 0.2 * _rnd.NextDouble()));
            var caps = new List<DeviceState>
            {
                new DeviceState { Id = "demo-mic1", Name = "Mikrofon", FullName = "Mikrofon (Webcam C920)", IsDefault = _micId == "demo-mic1" },
            };
            if (hs) caps.Add(new DeviceState { Id = "demo-mic2", Name = "Headset-Mikrofon", FullName = "Headset-Mikrofon (USB Audio Device)", IsDefault = _micId == "demo-mic2" });
            var micDev = _micId == "demo-mic1" ? caps[0] : caps[1];
            var mic = new DeviceState
            {
                Id = micDev.Id, Name = micDev.Name, FullName = micDev.FullName, IsDefault = true,
                Volume = _micVol, VolumeDb = (float)(20 * Math.Log10(Math.Max(0.0001, _micVol * _micVol))), Mute = _micMute,
                PeakL = mp, PeakR = mp
            };

            var sb = new StringBuilder("R|" + _default + "|" + _micId + "|" + _micLive + "|" + hs + "|");
            foreach (var a in apps) { sb.Append(a.Key).Append(a.Active ? '+' : '-').Append(a.Captured ? 'E' : 'e'); foreach (var id in a.DeviceIds) sb.Append(id); sb.Append('|'); }
            // Stream-Mix: Apps nach dem Fader mal Anteil
            var sp = new float[2];
            if (_stream != null && !_stream.Mute)
                foreach (var a in apps)
                {
                    var p = _policy.Get(a.Key);
                    float send = _stream.SendOf(p?.ChannelId) * _stream.Gain;
                    float l = a.Captured && chPeaks.TryGetValue(p.ChannelId, out var cp) ? cp[0] : a.PeakL;
                    float r = a.Captured && chPeaks.TryGetValue(p.ChannelId, out cp) ? cp[1] : a.PeakR;
                    sp[0] = Math.Max(sp[0], Math.Min(1f, l * send)); sp[1] = Math.Max(sp[1], Math.Min(1f, r * send));
                }
            _playing.RemoveAll(kv => kv.Value < t);
            float vp = _voice != null ? (float)(talk * 0.6 * _micVol) : 0f;
            var lev = new Dictionary<string, float>();
            foreach (var a in _apps) { var q = _policy.Get(a.Key); if (q?.Eq != null && q.Eq.Leveler) lev[q.ChannelId] = (float)(3.5 + 2 * Math.Sin(t * 0.2)); }
            var comp = new Dictionary<string, float>();
            foreach (var a in _apps)
            {
                var q = _policy.Get(a.Key);
                if (q?.Eq != null && q.Eq.Comp) comp[q.ChannelId] = (float)Math.Max(0, 4.5 + 3.5 * Math.Sin(t * 2.3 + q.ChannelId.Length));
            }
            return new EngineSnapshot
            {
                Apps = apps.ToArray(), Devices = devs.ToArray(), RoutingSupported = true, Structure = sb.ToString(),
                ChannelPeaks = chPeaks, EqSupported = true,
                StreamPeak = sp, VoicePeak = vp, VoiceOutPeak = _voice != null && _voice.MicMuted ? 0f : vp, VoiceActive = _voice != null,
                SoundsPlaying = _playing.Count, Spectrum = _specChannel != null ? FakeSpectrum(t) : null, LevelerGain = lev, CompReduction = comp,
                MasterSpectrum = _masterSpec ? FakeMasterSpectrum(t) : null,
                Loudness = FakeLoudness(t),
                Beat = BeatTracker.On ? new[] { 124f, (float)((t * 1000.0) % (60000.0 / 124.0)), 0.7f } : null,
                Correlation = LoudnessSink.CorrOn ? (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PHASEBAD") == "1" ? -0.45f : 0.62f + 0.1f * (float)Math.Sin(t * 0.4)) : float.NaN,   // 3.5
                MasterBass = LoudnessSink.BassOn ? (float)(Math.Max(0, Math.Sin(t * Math.PI * 2 * 124 / 60.0)) * 0.5 * DemoQuiet) : -1f,
                NoisePeak = DemoNoiseGain > 0 ? new[] { DemoNoiseGain * 0.5f, DemoNoiseGain * 0.48f } : new[] { 0f, 0f },
                EqLatencyMs = 38,
                MasterPeaks = LoudnessSink.TruePeakOn ? new[] { -0.6f + 0.4f * (float)Math.Sin(t * 0.3), -1.2f + 0.4f * (float)Math.Sin(t * 0.3) } : null,
                LoudnessByDevice = FakeDeviceLoudness(t),
                ChannelLoudness = FakeChannelLoudness(t),
                ChannelSpectra = FakeChannelSpectra(t),
                Scope = FakeScope(t, out float fakeCorr),
                ScopeCorr = fakeCorr,
                Mic = mic, CaptureDevices = caps.ToArray(), MicLive = _micLive
            };
        }
    }
}
