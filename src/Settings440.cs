using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Mischpult
{
    internal sealed partial class MixerSettings
    {
        // ---- 4.4 Fernbedienung im Browser (Handy, Tablet, anderer PC im Heimnetz). Standard aus.
        [DataMember] public bool RemoteOn;
        [DataMember] public int RemotePort;            // Standard 8765
        [DataMember] public string RemoteCode;         // 6 Ziffern, beim ersten Einschalten erzeugt
        [DataMember] public bool RemoteScenesOff;      // Szenen und DUCKING am Handy ausblenden
        [DataMember] public bool RemoteAppsOff;        // Lautstaerke einzelner Apps am Handy ausblenden

        public const int RemotePortDefault = 8765;

        /// <summary>Neuer Zugangscode: 6 zufaellige Ziffern (kryptografischer Zufall).</summary>
        public static string NewRemoteCode()
        {
            var b = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            uint v = BitConverter.ToUInt32(b, 0) % 1000000u;
            return v.ToString("000000");
        }

        public static bool ValidRemoteCode(string c)
        {
            if (c == null || c.Length != 6) return false;
            foreach (char ch in c) if (ch < '0' || ch > '9') return false;
            return true;
        }

        private void Normalize440()
        {
            if (RemotePort == 0) RemotePort = RemotePortDefault;
            if (RemotePort < 1024 || RemotePort > 65535) RemotePort = RemotePortDefault;
            if (!ValidRemoteCode(RemoteCode)) RemoteCode = null;
            if (RemoteOn && RemoteCode == null) RemoteCode = NewRemoteCode();
        }
    }
}
