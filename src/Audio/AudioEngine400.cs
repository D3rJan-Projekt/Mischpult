using System;
using System.Collections.Generic;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>4.0: Surround-Pegel – jeder Kanal eines 5.1/7.1-Geraets einzeln (nur wenn eingeschaltet).</summary>
    internal sealed partial class AudioEngine
    {
        /// <summary>Surround-Pegel messen (Schalter in den Einstellungen).</summary>
        public static volatile bool SurroundWanted;
        private long _nextSurMs;
        private readonly Dictionary<string, float[]> _surMax = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        private readonly object _surLock = new object();

        private bool SurroundDue(long nowMs)
        {
            if (!SurroundWanted || nowMs < _nextSurMs) return false;
            _nextSurMs = nowMs + 20;
            return true;
        }

        private void ReadSurround400(string id, IAudioMeterInformation meter, ref float[] buf)
        {
            if (meter == null) return;
            try
            {
                if (buf == null && meter.GetMeteringChannelCount(out uint mc) == 0 && mc > 0 && mc <= 32) buf = new float[mc];
                if (buf == null || buf.Length <= 2) return;
                if (meter.GetChannelsPeakValues((uint)buf.Length, buf) != 0) return;
                lock (_surLock)
                {
                    if (!_surMax.TryGetValue(id, out var mx) || mx.Length != buf.Length) { mx = new float[buf.Length]; _surMax[id] = mx; }
                    for (int i = 0; i < buf.Length; i++) if (buf[i] > mx[i]) mx[i] = buf[i];
                }
            }
            catch { }
        }

        /// <summary>Hoechste Pegel seit dem letzten Abholen (null = Stereo oder aus).</summary>
        private float[] TakeSurround400(string id)
        {
            if (!SurroundWanted) return null;
            lock (_surLock)
            {
                if (!_surMax.TryGetValue(id, out var mx)) return null;
                var r = (float[])mx.Clone();
                Array.Clear(mx, 0, mx.Length);
                return r;
            }
        }
    }
}
