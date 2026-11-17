using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>Ziel fuer den Loopback-Ton: WAV-Datei (Aufnahme) oder Spektrum-Analyse.</summary>
    internal interface ILoopbackSink
    {
        void SetRate(int rate);
        void Write(float[] stereo, int frames);
        void WriteSilence(int frames);
    }

    /// <summary>
    /// Nimmt auf, was auf einem Ausgabegeraet laeuft (WASAPI-Loopback), und schreibt es in eine WAV-Datei.
    /// Beim Standardgeraet wird einem Wechsel automatisch gefolgt. Pausen (Windows liefert dann keine Daten)
    /// werden mit Stille aufgefuellt, damit die Aufnahme zeitlich stimmt.
    /// </summary>
    internal sealed class LoopbackRecorder : IDisposable
    {
        private const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);

        private readonly string _deviceId;
        private readonly ILoopbackSink _writer;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _opened = new ManualResetEventSlim(false);
        private volatile bool _running = true;
        private volatile string _error;

        public string Error => _error;
        /// <summary>2.15.1: Mess-Mitschnitte (Spektrum, Lautheit, Goniometer) bekommen Vorrang.</summary>
        public static volatile bool Boost;
        /// <summary>Aufnahme-Thread ist beendet (Fehler oder Ende).</summary>
        public bool Stopped { get; private set; }

        public LoopbackRecorder(string deviceId, ILoopbackSink writer)
        {
            _deviceId = deviceId;
            _writer = writer;
            _thread = new Thread(Run) { IsBackground = true, Name = writer is WavWriter ? "Mischpult Loopback" : "Mischpult Spektrum", Priority = writer is WavWriter ? ThreadPriority.AboveNormal : ThreadPriority.Normal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            _opened.Wait(3000);
        }

        public void Dispose()
        {
            _running = false;
            try { _thread.Join(2000); } catch { }
        }

        private sealed class Stream
        {
            public IMMDevice Device;
            public IAudioClient Client;
            public IAudioCaptureClient Capture;
            public string DeviceId;
            public int Channels, Rate;
            public bool Float;          // sonst 16-Bit-PCM
        }

        private void Run()
        {
            IMMDeviceEnumerator en = null;
            var clock = Stopwatch.StartNew();
            long written = 0;           // Frames in der Datei
            long startMs = 0;
            long lastPacketMs = 0;
            bool first = true;
            float[] stereo = new float[0];
            float[] raw = new float[0];
            short[] raw16 = new short[0];
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                while (_running)
                {
                    Stream st = null;
                    try { st = Open(en); }
                    catch (Exception ex)
                    {
                        _error = "Aufnahme konnte nicht gestartet werden: " + ex.Message;
                        _opened.Set();
                        if (first) return;
                        Thread.Sleep(1000);
                        continue;
                    }
                    if (first)
                    {
                        _writer.SetRate(st.Rate);
                        startMs = clock.ElapsedMilliseconds;
                        lastPacketMs = startMs;
                        first = false;
                    }
                    _error = null;
                    _opened.Set();

                    long nextDefaultCheck = clock.ElapsedMilliseconds + 1000;
                    bool boosted = _writer is WavWriter;
                    try
                    {
                        while (_running)
                        {
                            Thread.Sleep(10);
                            bool b = Boost || _writer is WavWriter;   // Spektrum/Lautheit: beim Spielen nicht hintenanstellen
                            if (b != boosted)
                            {
                                boosted = b;
                                try { Thread.CurrentThread.Priority = b ? ThreadPriority.AboveNormal : ThreadPriority.Normal; } catch { }
                            }
                            long now = clock.ElapsedMilliseconds;
                            bool got = false;
                            for (int guard = 0; guard < 100; guard++)
                            {
                                int hr = st.Capture.GetNextPacketSize(out uint packet);
                                if (hr < 0) throw new COMException("GetNextPacketSize", hr);
                                if (packet == 0) break;
                                hr = st.Capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                                if (hr < 0) throw new COMException("GetBuffer", hr);
                                int n = (int)frames;
                                if (n > 0)
                                {
                                    got = true;
                                    if ((flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == IntPtr.Zero)
                                        _writer.WriteSilence(n);
                                    else
                                    {
                                        if (stereo.Length < n * 2) stereo = new float[n * 2];
                                        int samples = n * st.Channels;
                                        if (st.Float)
                                        {
                                            if (raw.Length < samples) raw = new float[samples];
                                            Marshal.Copy(data, raw, 0, samples);
                                        }
                                        else
                                        {
                                            if (raw16.Length < samples) raw16 = new short[samples];
                                            if (raw.Length < samples) raw = new float[samples];
                                            Marshal.Copy(data, raw16, 0, samples);
                                            for (int i = 0; i < samples; i++) raw[i] = raw16[i] / 32768f;
                                        }
                                        Downmix(raw, st.Channels, stereo, n);
                                        _writer.Write(stereo, n);
                                    }
                                    written += n;
                                }
                                st.Capture.ReleaseBuffer(frames);
                            }

                            if (got) lastPacketMs = now;
                            else if (now - lastPacketMs > 60)
                            {
                                // Windows liefert in Pausen nichts -> mit Stille auffuellen
                                long expected = (now - startMs) * st.Rate / 1000;
                                long missing = expected - written - st.Rate / 50;
                                if (missing > st.Rate * 5L)
                                {
                                    // sehr lange Luecke (z. B. Energiesparmodus): Zeitbasis neu setzen statt Stille zu schreiben
                                    startMs = now - written * 1000 / st.Rate;
                                }
                                else if (missing > 0)
                                {
                                    _writer.WriteSilence((int)missing);
                                    written += missing;
                                }
                            }

                            if (_deviceId == null && now >= nextDefaultCheck)
                            {
                                nextDefaultCheck = now + 1000;
                                if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice d) == 0 && d != null)
                                {
                                    d.GetId(out string id);
                                    ComUtil.Release(d);
                                    if (id != null && !string.Equals(id, st.DeviceId, StringComparison.OrdinalIgnoreCase))
                                        break; // Standardgeraet gewechselt -> neu oeffnen
                                }
                            }
                        }
                    }
                    catch (COMException ex) when (ex.ErrorCode == AUDCLNT_E_DEVICE_INVALIDATED)
                    {
                        if (_deviceId != null) { _error = "Das Ausgabegerät ist nicht mehr verfügbar – Aufnahme angehalten."; _running = false; }
                    }
                    catch (Exception ex)
                    {
                        _error = "Aufnahme-Fehler: " + ex.Message;
                        Thread.Sleep(500);
                    }
                    finally { Close(st); }
                }
            }
            catch (Exception ex) { _error = "Aufnahme-Fehler: " + ex.Message; }
            finally
            {
                Stopped = true;
                _opened.Set();
                ComUtil.Release(en);
            }
        }

        private Stream Open(IMMDeviceEnumerator en)
        {
            var st = new Stream();
            IntPtr fmt = IntPtr.Zero;
            bool ok = false;
            try
            {
                int hr = _deviceId == null
                    ? en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out st.Device)
                    : en.GetDevice(_deviceId, out st.Device);
                if (hr != 0 || st.Device == null) throw new COMException("Ausgabegerät nicht gefunden", hr);
                st.Device.GetId(out st.DeviceId);

                // 1. Versuch: 48 kHz Stereo Float, Windows rechnet um
                st.Client = ActivateClient(st.Device);
                fmt = Wasapi.AllocFloatStereoFormat(DspMixer.Rate);
                hr = st.Client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED,
                    Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK | Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    2000000, 0, fmt, IntPtr.Zero);
                Marshal.FreeHGlobal(fmt);
                fmt = IntPtr.Zero;
                if (hr >= 0)
                {
                    st.Channels = 2; st.Rate = DspMixer.Rate; st.Float = true;
                }
                else
                {
                    // 2. Versuch: Geraeteformat
                    ComUtil.Release(st.Client);
                    st.Client = ActivateClient(st.Device);
                    if (st.Client.GetMixFormat(out IntPtr mix) < 0 || mix == IntPtr.Zero) throw new Exception("Geräteformat unbekannt");
                    try
                    {
                        Wasapi.ReadFormat(mix, out st.Channels, out st.Rate, out int bits, out bool isFloat);
                        if (isFloat && bits == 32) st.Float = true;
                        else if (!isFloat && bits == 16) st.Float = false;
                        else throw new Exception("Geräteformat wird nicht unterstützt (" + bits + " Bit)");
                        hr = st.Client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED, Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK, 2000000, 0, mix, IntPtr.Zero);
                        if (hr < 0) throw new COMException("Initialize", hr);
                    }
                    finally { Marshal.FreeCoTaskMem(mix); }
                }
                if (st.Channels < 1) throw new Exception("Keine Kanäle");

                Guid iid = Wasapi.IID_IAudioCaptureClient;
                hr = st.Client.GetService(ref iid, out object svc);
                if (hr < 0) throw new COMException("GetService", hr);
                st.Capture = (IAudioCaptureClient)svc;
                hr = st.Client.Start();
                if (hr < 0) throw new COMException("Start", hr);
                ok = true;
                return st;
            }
            finally
            {
                if (fmt != IntPtr.Zero) Marshal.FreeHGlobal(fmt);
                if (!ok) Close(st);
            }
        }

        private static IAudioClient ActivateClient(IMMDevice dev)
        {
            Guid iid = Wasapi.IID_IAudioClient;
            int hr = dev.Activate(ref iid, CoreAudioConst.CLSCTX_ALL, IntPtr.Zero, out object o);
            if (hr < 0 || o == null) throw new COMException("Activate", hr);
            return (IAudioClient)o;
        }

        private static void Close(Stream st)
        {
            if (st == null) return;
            try { st.Client?.Stop(); } catch { }
            ComUtil.Release(st.Capture);
            ComUtil.Release(st.Client);
            ComUtil.Release(st.Device);
        }

        /// <summary>Beliebig viele Kanaele -> Stereo (Surround: Center und hintere Kanaele anteilig auf L/R).</summary>
        internal static void Downmix(float[] src, int ch, float[] dst, int frames)
        {
            const float h = 0.7071f;
            for (int i = 0; i < frames; i++)
            {
                int b = i * ch;
                float l, r;
                if (ch == 1) { l = r = src[b]; }
                else if (ch == 2) { l = src[b]; r = src[b + 1]; }
                else if (ch < 6)
                {
                    // 3: L R C · 4: L R BL BR · 5: L R C BL BR
                    if (ch == 3) { l = src[b] + h * src[b + 2]; r = src[b + 1] + h * src[b + 2]; }
                    else if (ch == 4) { l = src[b] + h * src[b + 2]; r = src[b + 1] + h * src[b + 3]; }
                    else { l = src[b] + h * src[b + 2] + h * src[b + 3]; r = src[b + 1] + h * src[b + 2] + h * src[b + 4]; }
                    l *= h; r *= h;
                }
                else
                {
                    // 5.1: L R C LFE BL BR · 7.1: L R C LFE BL BR SL SR (LFE wird weggelassen)
                    l = src[b] + h * src[b + 2] + h * src[b + 4];
                    r = src[b + 1] + h * src[b + 2] + h * src[b + 5];
                    if (ch >= 8) { l += h * src[b + 6]; r += h * src[b + 7]; }
                    l *= 0.5f; r *= 0.5f;
                }
                dst[i * 2] = l;
                dst[i * 2 + 1] = r;
            }
        }
    }
}
