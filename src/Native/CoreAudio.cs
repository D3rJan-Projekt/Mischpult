using System;
using System.Runtime.InteropServices;

// Windows Core Audio (MMDevice API / WASAPI Session API) – COM-Deklarationen.
// Methodenreihenfolge entspricht mmdeviceapi.h, audiopolicy.h, audioclient.h, endpointvolume.h.
// Wichtig: Bei abgeleiteten COM-Interfaces muessen in .NET alle Basismethoden erneut deklariert werden.

namespace Mischpult.Native
{
    internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }

    internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    internal enum AudioSessionState { Inactive = 0, Active = 1, Expired = 2 }

    internal static class CoreAudioConst
    {
        public const uint DEVICE_STATE_ACTIVE = 0x1;
        public const uint CLSCTX_ALL = 0x17;
        public const uint STGM_READ = 0x0;
        public const int S_OK = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid f, uint p) { fmtid = f; pid = p; }

        public static readonly PropertyKey DeviceFriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        public static readonly PropertyKey DeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        public static readonly PropertyKey DeviceInterfaceFriendlyName = new PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
        // 3.5: Bauform (Kopfhoerer, Lautsprecher …), Format des Geraets, Geraetepfad (Bluetooth/USB)
        public static readonly PropertyKey EndpointFormFactor = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
        public static readonly PropertyKey EngineDeviceFormat = new PropertyKey(new Guid("f19f064d-082c-4e27-bc73-6882a1bb8e4c"), 0);
        public static readonly PropertyKey EndpointDevicePath = new PropertyKey(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);
    }

    // PROPVARIANT: 16 Byte (x86) bzw. 24 Byte (x64)
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p1;
        public IntPtr p2;

        public string GetString()
        {
            const ushort VT_LPWSTR = 31;
            return vt == VT_LPWSTR && p1 != IntPtr.Zero ? Marshal.PtrToStringUni(p1) : null;
        }

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant pvar);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification sessionNotification);
        [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionNotification
    {
        // Der Parameter wird bewusst als IntPtr entgegengenommen: im Callback-Thread wird das Objekt nicht benutzt,
        // nur festgehalten (AddRef) und an den Sofort-Faden weitergereicht (3.4.1).
        [PreserveSig] int OnSessionCreated(IntPtr newSession);
    }

    internal enum AudioSessionDisconnectReason { DeviceRemoval = 0, ServerShutdown, FormatChanged, SessionLogoff, SessionDisconnected, ExclusiveModeOverride }

    /// <summary>3.4.1: Meldungen einer einzelnen Session (Lautstaerke/Stumm von aussen geaendert, Zustand).</summary>
    [ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEvents
    {
        // Zeiger als IntPtr: Windows darf hier NULL schicken (ref Guid wuerde dann abstuerzen)
        [PreserveSig] int OnDisplayNameChanged(IntPtr newDisplayName, IntPtr eventContext);
        [PreserveSig] int OnIconPathChanged(IntPtr newIconPath, IntPtr eventContext);
        [PreserveSig] int OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, IntPtr eventContext);
        [PreserveSig] int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, IntPtr eventContext);
        [PreserveSig] int OnGroupingParamChanged(IntPtr newGroupingParam, IntPtr eventContext);
        [PreserveSig] int OnStateChanged(AudioSessionState newState);
        [PreserveSig] int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IChannelAudioVolume
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolume(uint index, out float level);
        [PreserveSig] int SetAllVolumes(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] levels, ref Guid eventContext);
        [PreserveSig] int GetAllVolumes(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] levels);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint count);
        [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    internal static class ComUtil
    {
        public static void Release(object o)
        {
            if (o == null) return;
            try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
        }
    }
}
