using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>
    /// Laedt Audiodateien fuers Soundboard und wandelt sie in 48 kHz Stereo Float.
    /// WAV wird direkt gelesen, alles andere (MP3, M4A/AAC, WMA, FLAC ...) ueber Windows Media Foundation.
    /// </summary>
    internal static class SoundDecoder
    {
        public const int MaxSeconds = 180;
        public static readonly string[] Extensions = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".mp4" };

        public static SoundClip Load(string path, string name)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Datei nicht gefunden", path);
            float[] data; int channels, rate;
            bool wav = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            if (wav && TryReadWav(File.ReadAllBytes(path), out data, out channels, out rate)) { }
            else if (Win32.IsWindows) DecodeWithMediaFoundation(path, out data, out channels, out rate);
            else throw new NotSupportedException("Dieses Format wird hier nicht unterstützt");
            return ToClip(data, channels, rate, name);
        }

        /// <summary>Interleavte Samples beliebiger Kanalzahl/Rate -> 48 kHz Stereo.</summary>
        public static SoundClip ToClip(float[] data, int channels, int rate, string name)
        {
            if (channels < 1 || rate < 1000) throw new InvalidDataException("Ungültiges Audioformat");
            int frames = data.Length / channels;
            frames = Math.Min(frames, rate * MaxSeconds);
            var stereo = new float[frames * 2];
            if (channels == 2) Array.Copy(data, stereo, frames * 2);
            else LoopbackRecorder.Downmix(data, channels, stereo, frames);
            float[] outData = rate == DspMixer.Rate ? stereo : Resample(stereo, frames, rate, DspMixer.Rate);
            return new SoundClip { Name = name, Data = outData, Frames = outData.Length / 2 };
        }

        /// <summary>Abtastraten-Umwandlung mit kubischer Interpolation (Catmull-Rom), Stereo.</summary>
        public static float[] Resample(float[] src, int frames, int inRate, int outRate)
        {
            if (frames <= 0) return new float[0];
            int outFrames = (int)((long)frames * outRate / inRate);
            var dst = new float[outFrames * 2];
            double step = inRate / (double)outRate;
            for (int o = 0; o < outFrames; o++)
            {
                double p = o * step;
                int i = (int)p;
                float t = (float)(p - i);
                for (int c = 0; c < 2; c++)
                {
                    float y0 = src[Math.Max(0, i - 1) * 2 + c], y1 = src[i * 2 + c];
                    float y2 = src[Math.Min(frames - 1, i + 1) * 2 + c], y3 = src[Math.Min(frames - 1, i + 2) * 2 + c];
                    dst[o * 2 + c] = y1 + 0.5f * t * (y2 - y0 + t * (2f * y0 - 5f * y1 + 4f * y2 - y3 + t * (3f * (y1 - y2) + y3 - y0)));
                }
            }
            return dst;
        }

        /// <summary>WAV lesen: PCM 8/16/24/32 Bit, Float 32/64 Bit, auch WAVE_FORMAT_EXTENSIBLE.</summary>
        public static bool TryReadWav(byte[] b, out float[] data, out int channels, out int rate)
        {
            data = null; channels = 0; rate = 0;
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return false;
            int pos = 12, fmtTag = 0, bits = 0, block = 0;
            int dataOff = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (len < 0) break;
                if (id == "fmt " && len >= 16 && body + 16 <= b.Length)
                {
                    fmtTag = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    block = BitConverter.ToUInt16(b, body + 12);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (fmtTag == 0xFFFE && len >= 40 && body + 26 <= b.Length) fmtTag = BitConverter.ToUInt16(b, body + 24); // SubFormat
                }
                else if (id == "data")
                {
                    dataOff = body;
                    dataLen = Math.Min(len, b.Length - body);
                    break;
                }
                pos = body + len + (len & 1);
            }
            if (dataOff < 0 || channels < 1 || rate < 1000 || block < 1) return false;
            int frames = dataLen / block;
            int bytesPer = bits / 8;
            if (bytesPer < 1 || block < bytesPer * channels) return false;
            data = new float[frames * channels];
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++)
                {
                    int o = dataOff + f * block + c * bytesPer;
                    float v;
                    if (fmtTag == 3 && bits == 32) v = BitConverter.ToSingle(b, o);
                    else if (fmtTag == 3 && bits == 64) v = (float)BitConverter.ToDouble(b, o);
                    else if (fmtTag == 1 && bits == 16) v = BitConverter.ToInt16(b, o) / 32768f;
                    else if (fmtTag == 1 && bits == 24) v = ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f;
                    else if (fmtTag == 1 && bits == 32) v = BitConverter.ToInt32(b, o) / 2147483648f;
                    else if (fmtTag == 1 && bits == 8) v = (b[o] - 128) / 128f;
                    else return false;
                    data[f * channels + c] = v;
                }
            return true;
        }

        private static readonly object MfLock = new object();

        /// <summary>Dekodiert mit Media Foundation zu Float (Kanalzahl und Rate wie in der Datei).</summary>
        private static void DecodeWithMediaFoundation(string path, out float[] data, out int channels, out int rate)
        {
            lock (MfLock)
            {
                int hr = MF.MFStartup(MF.MF_VERSION, 0);
                if (hr < 0) throw new COMException("Media Foundation nicht verfügbar", hr);
                IMFSourceReader reader = null;
                IMFMediaType type = null, cur = null;
                try
                {
                    hr = MF.MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader);
                    if (hr < 0 || reader == null) throw new COMException("Datei kann nicht gelesen werden", hr);
                    reader.SetStreamSelection(MF.MF_SOURCE_READER_ALL_STREAMS, 0);
                    hr = reader.SetStreamSelection(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 1);
                    if (hr < 0) throw new COMException("Kein Ton in der Datei", hr);

                    hr = MF.MFCreateMediaType(out type);
                    if (hr < 0) throw new COMException("MFCreateMediaType", hr);
                    Guid major = MF.MF_MT_MAJOR_TYPE, audio = MF.MFMediaType_Audio, sub = MF.MF_MT_SUBTYPE, flt = MF.MFAudioFormat_Float;
                    type.SetGUID(ref major, ref audio);
                    type.SetGUID(ref sub, ref flt);
                    hr = reader.SetCurrentMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, type);
                    if (hr < 0) throw new COMException("Format nicht unterstützt", hr);
                    hr = reader.GetCurrentMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out cur);
                    if (hr < 0) throw new COMException("GetCurrentMediaType", hr);
                    Guid kc = MF.MF_MT_AUDIO_NUM_CHANNELS, kr = MF.MF_MT_AUDIO_SAMPLES_PER_SECOND;
                    cur.GetUINT32(ref kc, out channels);
                    cur.GetUINT32(ref kr, out rate);
                    if (channels < 1 || rate < 1000) throw new InvalidDataException("Ungültiges Audioformat");

                    var chunks = new List<float[]>();
                    long total = 0, max = (long)rate * channels * MaxSeconds;
                    while (total < max)
                    {
                        hr = reader.ReadSample(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out _, out int flags, out _, out IMFSample sample);
                        if (hr < 0) throw new COMException("Lesefehler", hr);
                        if ((flags & 0x1) != 0) { ComUtil.Release(sample); break; }  // MF_SOURCE_READERF_ERROR
                        if ((flags & 0x20) != 0)                                     // CURRENTMEDIATYPECHANGED (z. B. HE-AAC)
                        {
                            ComUtil.Release(cur); cur = null;
                            if (reader.GetCurrentMediaType(MF.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out cur) >= 0 && cur != null)
                            {
                                cur.GetUINT32(ref kc, out int c2);
                                cur.GetUINT32(ref kr, out int r2);
                                if (c2 != channels || r2 != rate)
                                {
                                    if (total == 0 && c2 > 0 && r2 >= 1000) { channels = c2; rate = r2; max = (long)rate * channels * MaxSeconds; }
                                    else { ComUtil.Release(sample); break; } // Formatwechsel mitten in der Datei: bis hier verwenden
                                }
                            }
                        }
                        if (sample != null)
                        {
                            IMFMediaBuffer buf = null;
                            try
                            {
                                if (sample.ConvertToContiguousBuffer(out buf) >= 0 && buf != null &&
                                    buf.Lock(out IntPtr p, out _, out int len) >= 0)
                                {
                                    try
                                    {
                                        var f = new float[len / 4];
                                        Marshal.Copy(p, f, 0, f.Length);
                                        chunks.Add(f);
                                        total += f.Length;
                                    }
                                    finally { buf.Unlock(); }
                                }
                            }
                            finally { ComUtil.Release(buf); ComUtil.Release(sample); }
                        }
                        if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    }
                    data = new float[Math.Min(total, max)];
                    long at = 0;
                    foreach (var c in chunks)
                    {
                        int n = (int)Math.Min(c.Length, data.Length - at);
                        if (n <= 0) break;
                        Array.Copy(c, 0, data, at, n);
                        at += n;
                    }
                }
                finally
                {
                    ComUtil.Release(cur);
                    ComUtil.Release(type);
                    ComUtil.Release(reader);
                    MF.MFShutdown();
                }
            }
        }
    }
}
