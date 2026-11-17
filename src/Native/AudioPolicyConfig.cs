using System;
using System.Runtime.InteropServices;

// Pro-App-Ausgabegeraet ("App-Lautstaerke und Geraeteeinstellungen" in Windows).
// Windows bietet dafuer keine offizielle API. Wie EarTrumpet wird die interne WinRT-Fabrik
// "Windows.Media.Internal.AudioPolicyConfig" verwendet. Die Einstellung wird von Windows pro App
// dauerhaft gespeichert (auch nach Neustart) und ist in den Windows-Soundeinstellungen sichtbar.

namespace Mischpult.Native
{
    [Guid("ab3d4648-e242-459f-b02f-541c70306324")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    internal interface IAudioPolicyConfigFactory21H2
    {
        int __incomplete__add_CtxVolumeChange();
        int __incomplete__remove_CtxVolumeChanged();
        int __incomplete__add_RingerVibrateStateChanged();
        int __incomplete__remove_RingerVibrateStateChange();
        int __incomplete__SetVolumeGroupGainForId();
        int __incomplete__GetVolumeGroupGainForId();
        int __incomplete__GetActiveVolumeGroupForEndpointId();
        int __incomplete__GetVolumeGroupsForEndpoint();
        int __incomplete__GetCurrentVolumeContext();
        int __incomplete__SetVolumeGroupMuteForId();
        int __incomplete__GetVolumeGroupMuteForId();
        int __incomplete__SetRingerVibrateState();
        int __incomplete__GetRingerVibrateState();
        int __incomplete__SetPreferredChatApplication();
        int __incomplete__ResetPreferredChatApplication();
        int __incomplete__GetPreferredChatApplication();
        int __incomplete__GetCurrentChatApplications();
        int __incomplete__add_ChatContextChanged();
        int __incomplete__remove_ChatContextChanged();
        [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
        [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, [Out, MarshalAs(UnmanagedType.HString)] out string deviceId);
        [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
    }

    [Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    internal interface IAudioPolicyConfigFactoryDownlevel
    {
        int __incomplete__add_CtxVolumeChange();
        int __incomplete__remove_CtxVolumeChanged();
        int __incomplete__add_RingerVibrateStateChanged();
        int __incomplete__remove_RingerVibrateStateChange();
        int __incomplete__SetVolumeGroupGainForId();
        int __incomplete__GetVolumeGroupGainForId();
        int __incomplete__GetActiveVolumeGroupForEndpointId();
        int __incomplete__GetVolumeGroupsForEndpoint();
        int __incomplete__GetCurrentVolumeContext();
        int __incomplete__SetVolumeGroupMuteForId();
        int __incomplete__GetVolumeGroupMuteForId();
        int __incomplete__SetRingerVibrateState();
        int __incomplete__GetRingerVibrateState();
        int __incomplete__SetPreferredChatApplication();
        int __incomplete__ResetPreferredChatApplication();
        int __incomplete__GetPreferredChatApplication();
        int __incomplete__GetCurrentChatApplications();
        int __incomplete__add_ChatContextChanged();
        int __incomplete__remove_ChatContextChanged();
        [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
        [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, [Out, MarshalAs(UnmanagedType.HString)] out string deviceId);
        [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
    }

    internal static class Combase
    {
        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IInspectable)] out object factory);

        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, uint length, out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = false)]
        public static extern void WindowsDeleteString(IntPtr hstring);
    }

    /// <summary>Setzt/liest das Ausgabegeraet einzelner Prozesse. Nur im Audio-Thread verwenden.</summary>
    internal sealed class AppRouting
    {
        private const string ClassId = "Windows.Media.Internal.AudioPolicyConfig";
        private const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";

        private IAudioPolicyConfigFactory21H2 _new;
        private IAudioPolicyConfigFactoryDownlevel _old;
        private bool _probed;

        public bool IsSupported
        {
            get
            {
                if (!_probed) Probe();
                return _new != null || _old != null;
            }
        }

        private void Probe()
        {
            _probed = true;
            try
            {
                Guid iid = typeof(IAudioPolicyConfigFactory21H2).GUID;
                Combase.RoGetActivationFactory(ClassId, ref iid, out object f);
                _new = (IAudioPolicyConfigFactory21H2)f;
                return;
            }
            catch { _new = null; }
            try
            {
                Guid iid = typeof(IAudioPolicyConfigFactoryDownlevel).GUID;
                Combase.RoGetActivationFactory(ClassId, ref iid, out object f);
                _old = (IAudioPolicyConfigFactoryDownlevel)f;
            }
            catch { _old = null; }
        }

        /// <summary>deviceId = MMDevice-ID, oder null = zurueck auf "Standard".</summary>
        public bool SetAppDevice(uint pid, string deviceId)
        {
            if (!IsSupported || pid == 0) return false;
            IntPtr h = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrEmpty(deviceId))
                {
                    string full = MmDevApiToken + deviceId + RenderInterface;
                    Combase.WindowsCreateString(full, (uint)full.Length, out h);
                }
                int hr1, hr2;
                if (_new != null)
                {
                    hr1 = _new.SetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, h);
                    hr2 = _new.SetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eConsole, h);
                }
                else
                {
                    hr1 = _old.SetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, h);
                    hr2 = _old.SetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eConsole, h);
                }
                return hr1 >= 0 && hr2 >= 0;
            }
            catch { return false; }
            finally
            {
                if (h != IntPtr.Zero) { try { Combase.WindowsDeleteString(h); } catch { } }
            }
        }

        /// <summary>Liefert die aktuell von Windows gespeicherte Geraete-ID (ohne Praefix) oder null fuer Standard.</summary>
        public string GetAppDevice(uint pid)
        {
            if (!IsSupported || pid == 0) return null;
            try
            {
                string id;
                int hr = _new != null
                    ? _new.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, out id)
                    : _old.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, out id);
                if (hr < 0 || string.IsNullOrEmpty(id)) return null;
                if (id.StartsWith(MmDevApiToken, StringComparison.OrdinalIgnoreCase)) id = id.Substring(MmDevApiToken.Length);
                if (id.EndsWith(RenderInterface, StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - RenderInterface.Length);
                return id;
            }
            catch { return null; }
        }

        public bool ClearAll()
        {
            if (!IsSupported) return false;
            try
            {
                return (_new != null ? _new.ClearAllPersistedApplicationDefaultEndpoints()
                                     : _old.ClearAllPersistedApplicationDefaultEndpoints()) >= 0;
            }
            catch { return false; }
        }
    }
}
