using System;
using System.Runtime.InteropServices;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>3.5: Geraete-Art und -Format, zweites Geraet, Geraeusche, Phase/Bass.</summary>
    internal sealed partial class AudioEngine
    {
        /// <summary>Bauform, Bluetooth/USB und Format aus den Eigenschaften des Geraets.</summary>
        private static void ReadDeviceKind(IPropertyStore ps, DeviceEntry e)
        {
            try
            {
                var k = PropertyKey.EndpointFormFactor;
                if (ps.GetValue(ref k, out PropVariant pv) == 0)
                {
                    try { if (pv.vt == 19 /* VT_UI4 */ || pv.vt == 3 /* VT_I4 */) e.FormFactor = (int)(pv.p1.ToInt64() & 0xFFFFFFFF); }
                    finally { PropVariant.PropVariantClear(ref pv); }
                }
            }
            catch { }
            try
            {
                string path = ReadString(ps, PropertyKey.EndpointDevicePath) ?? "";
                string up = path.ToUpperInvariant();
                e.Bluetooth = up.Contains("BTHENUM") || up.Contains("BTHHFENUM") || up.Contains("BTHLEDEVICE") || up.Contains("BTHLE");
                e.Usb = !e.Bluetooth && up.Contains("USB");
            }
            catch { }
            try
            {
                var k = PropertyKey.EngineDeviceFormat;
                if (ps.GetValue(ref k, out PropVariant pv) == 0)
                {
                    try
                    {
                        if (pv.vt == 65 /* VT_BLOB */)
                        {
                            int size = (int)(pv.p1.ToInt64() & 0xFFFFFFFF);
                            IntPtr data = pv.p2;   // BLOB: Groesse, dann (ausgerichtet) der Zeiger
                            if (size >= 16 && data != IntPtr.Zero)
                            {
                                var b = new byte[Math.Min(size, 40)];
                                Marshal.Copy(data, b, 0, b.Length);
                                e.FormatText = FormatText(b);
                            }
                        }
                    }
                    finally { PropVariant.PropVariantClear(ref pv); }
                }
            }
            catch { }
        }

        /// <summary>WAVEFORMATEX(TENSIBLE) → "48 kHz · 24 Bit".</summary>
        internal static string FormatText(byte[] wf)
        {
            if (wf == null || wf.Length < 16) return null;
            int tag = BitConverter.ToUInt16(wf, 0);
            int rate = BitConverter.ToInt32(wf, 4);
            int bits = BitConverter.ToUInt16(wf, 14);
            if (tag == 0xFFFE && wf.Length >= 20) { int valid = BitConverter.ToUInt16(wf, 18); if (valid > 0 && valid <= bits) bits = valid; }
            if (rate <= 0 || bits <= 0) return null;
            string khz = rate % 1000 == 0 ? (rate / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : (rate / 1000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            return khz + " kHz · " + bits + " Bit";
        }

        // ---------------------------------------------------------------- zweites Geraet
        private readonly object _mirrorLock = new object();
        private LoopbackRecorder _mirrorRec;
        private MirrorFeed _mirror;

        public void SetMirror(string targetDeviceId, int delayMs)
        {
            lock (_mirrorLock)
            {
                if (_mirror != null && targetDeviceId != null && string.Equals(_mirror.TargetId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _mirror.DelayMs = Math.Max(0, Math.Min(500, delayMs));
                    return;
                }
                var oldRec = _mirrorRec;
                _mirrorRec = null; _mirror = null;
                _dsp?.SetMirror(null);
                if (oldRec != null) System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { oldRec.Dispose(); } catch { } });
                if (targetDeviceId == null || _dsp == null) return;
                var feed = new MirrorFeed(targetDeviceId, delayMs);
                _mirror = feed;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    LoopbackRecorder rec = null;
                    try { rec = new LoopbackRecorder(null, feed); } catch { rec = null; }
                    lock (_mirrorLock)
                    {
                        if (!ReferenceEquals(_mirror, feed)) { try { rec?.Dispose(); } catch { } return; }
                        _mirrorRec = rec;
                        if (rec != null) _dsp?.SetMirror(feed);
                    }
                });
            }
        }

        private void DisposeMirror()
        {
            lock (_mirrorLock)
            {
                try { _mirrorRec?.Dispose(); } catch { }
                _mirrorRec = null; _mirror = null;
            }
        }
    }
}
