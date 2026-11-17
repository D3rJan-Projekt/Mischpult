using System;
using System.Runtime.InteropServices;
using System.Threading;

// WASAPI-Aufnahme/Wiedergabe + Prozess-Loopback (Windows 10 2004+).
// Reihenfolge der Methoden entspricht audioclient.h / mmdeviceapi.h.

namespace Mischpult.Native
{
    internal static class Wasapi
    {
        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        public static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");

        public const int AUDCLNT_SHAREMODE_SHARED = 0;
        public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        public const string ProcessLoopbackDevice = "VAD\\Process_Loopback";

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void ActivateAudioInterfaceAsync(
            [In, MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [In] IntPtr activationParams,
            [In] IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        public static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

        /// <summary>WAVEFORMATEXTENSIBLE fuer 32-Bit-Float Stereo in unmanaged Speicher (mit FreeHGlobal freigeben).</summary>
        public static IntPtr AllocFloatStereoFormat(int sampleRate)
        {
            var wf = new WaveFormatExtensible
            {
                wFormatTag = 0xFFFE,
                nChannels = 2,
                nSamplesPerSec = (uint)sampleRate,
                nAvgBytesPerSec = (uint)sampleRate * 8,
                nBlockAlign = 8,
                wBitsPerSample = 32,
                cbSize = 22,
                wValidBitsPerSample = 32,
                dwChannelMask = 3, // FRONT_LEFT | FRONT_RIGHT
                SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
            };
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveFormatExtensible)));
            Marshal.StructureToPtr(wf, p, false);
            return p;
        }

        /// <summary>Liest ein Geraeteformat (WAVEFORMATEX oder WAVEFORMATEXTENSIBLE) sicher aus.</summary>
        public static void ReadFormat(IntPtr p, out int channels, out int rate, out int bits, out bool isFloat)
        {
            int tag = (ushort)Marshal.ReadInt16(p, 0);
            channels = (ushort)Marshal.ReadInt16(p, 2);
            rate = Marshal.ReadInt32(p, 4);
            bits = (ushort)Marshal.ReadInt16(p, 14);
            int cbSize = (ushort)Marshal.ReadInt16(p, 16);
            isFloat = tag == 3;
            if (tag == 0xFFFE && cbSize >= 22)
            {
                var wf = (WaveFormatExtensible)Marshal.PtrToStructure(p, typeof(WaveFormatExtensible));
                isFloat = wf.SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            }
        }

        /// <summary>Windows-Build (ohne Kompatibilitaets-Luege).</summary>
        public static int WindowsBuild
        {
            get
            {
                try
                {
                    var v = new OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX)) };
                    if (RtlGetVersion(ref v) == 0) return (int)v.dwBuildNumber;
                }
                catch { }
                return Environment.OSVersion.Version.Build;
            }
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public uint dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor, wServicePackMinor, wSuiteMask;
            public byte wProductType, wReserved;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveFormatExtensible
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioClientActivationParams
    {
        public int ActivationType;        // 1 = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
        public uint TargetProcessId;
        public int ProcessLoopbackMode;   // 0 = INCLUDE_TARGET_PROCESS_TREE
    }

    // PROPVARIANT mit VT_BLOB (Zeiger liegt auf x86 bei Offset 12, auf x64 bei 16)
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariantBlob
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public uint cbSize;
        public IntPtr pBlobData;
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint numBufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint numFramesRequested, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint numFramesWritten, uint flags);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAgileObject { }

    // IPolicyConfig (nicht offiziell dokumentiert) – Standard-Ausgabegeraet wechseln, wie EarTrumpet.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        void Unused1(); void Unused2(); void Unused3(); void Unused4();
        void Unused5(); void Unused6(); void Unused7(); void Unused8();
        void GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr pv);
        void SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr pv);
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.I2)] short isVisible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient { }

    /// <summary>
    /// Asynchrone Aktivierung eines Prozess-Loopback-Clients. Der Callback laeuft in einem
    /// Windows-Arbeitsthread; dort wird der Client auch gleich initialisiert und gestartet.
    /// </summary>
    [ComVisible(true)]
    internal sealed class ProcessLoopbackActivation : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly uint Pid;
        public volatile bool Done;
        public string Error;
        public IAudioClient Client;
        public IAudioCaptureClient Capture;
        public AutoResetEvent Event;

        private IntPtr _params, _propVariant;
        private IActivateAudioInterfaceAsyncOperation _op;
        private readonly int _sampleRate;

        private ProcessLoopbackActivation(uint pid, int sampleRate) { Pid = pid; _sampleRate = sampleRate; }

        public static ProcessLoopbackActivation Begin(uint pid, int sampleRate)
        {
            var a = new ProcessLoopbackActivation(pid, sampleRate);
            try
            {
                var ap = new AudioClientActivationParams { ActivationType = 1, TargetProcessId = pid, ProcessLoopbackMode = 0 };
                a._params = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AudioClientActivationParams)));
                Marshal.StructureToPtr(ap, a._params, false);
                var pv = new PropVariantBlob { vt = 65 /* VT_BLOB */, cbSize = (uint)Marshal.SizeOf(typeof(AudioClientActivationParams)), pBlobData = a._params };
                a._propVariant = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PropVariantBlob)));
                Marshal.StructureToPtr(pv, a._propVariant, false);

                Wasapi.ActivateAudioInterfaceAsync(Wasapi.ProcessLoopbackDevice, Wasapi.IID_IAudioClient, a._propVariant, a, out a._op);
            }
            catch (Exception ex)
            {
                a.Error = ex.Message;
                a.FreeParams();
                a.Done = true;
            }
            return a;
        }

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
        {
            IntPtr fmt = IntPtr.Zero;
            try
            {
                int hr = op.GetActivateResult(out int activateHr, out object iface);
                if (hr < 0) throw new COMException("GetActivateResult", hr);
                if (activateHr < 0) throw new COMException("Prozess-Loopback nicht verfügbar", activateHr);
                var client = (IAudioClient)iface;

                fmt = Wasapi.AllocFloatStereoFormat(_sampleRate);
                hr = client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED,
                    Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK | Wasapi.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                    10000000 /* 1 s Puffer */, 0, fmt, IntPtr.Zero);
                if (hr < 0) throw new COMException("Initialize", hr);

                var ev = new AutoResetEvent(false);
                hr = client.SetEventHandle(ev.SafeWaitHandle.DangerousGetHandle());
                if (hr < 0) throw new COMException("SetEventHandle", hr);

                Guid iid = Wasapi.IID_IAudioCaptureClient;
                hr = client.GetService(ref iid, out object svc);
                if (hr < 0) throw new COMException("GetService", hr);

                hr = client.Start();
                if (hr < 0) throw new COMException("Start", hr);

                Client = client;
                Capture = (IAudioCaptureClient)svc;
                Event = ev;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                if (fmt != IntPtr.Zero) Marshal.FreeHGlobal(fmt);
                Done = true; // Parameter werden erst in Close() freigegeben (sicher gegen Wettlaeufe)
            }
            return 0;
        }

        private void FreeParams()
        {
            if (_propVariant != IntPtr.Zero) { Marshal.FreeHGlobal(_propVariant); _propVariant = IntPtr.Zero; }
            if (_params != IntPtr.Zero) { Marshal.FreeHGlobal(_params); _params = IntPtr.Zero; }
        }

        public void Close()
        {
            try { Client?.Stop(); } catch { }
            ComUtil.Release(Capture);
            ComUtil.Release(Client);
            ComUtil.Release(_op);
            Capture = null; Client = null; _op = null;
            FreeParams();
            try { Event?.Dispose(); } catch { }
            Event = null;
        }
    }
}
