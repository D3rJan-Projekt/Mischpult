using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>2.19: WAV-Datei (16 Bit PCM) lesen – nur was das Mischpult selbst schreibt (und uebliche PCM-WAVs).</summary>
    internal sealed class WavReader : IDisposable
    {
        private readonly FileStream _fs;
        public readonly int Rate, Channels, Bits;
        public readonly long DataStart, DataBytes;

        public WavReader(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            var br = new BinaryReader(_fs);
            if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Keine WAV-Datei");
            br.ReadUInt32();
            if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Keine WAV-Datei");
            bool fmt = false;
            while (_fs.Position + 8 <= _fs.Length)
            {
                string id = Encoding.ASCII.GetString(br.ReadBytes(4));
                long size = br.ReadUInt32();
                long next = _fs.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    int tag = br.ReadUInt16();
                    Channels = br.ReadUInt16();
                    Rate = (int)br.ReadUInt32();
                    br.ReadUInt32(); br.ReadUInt16();
                    Bits = br.ReadUInt16();
                    if ((tag != 1 && tag != 0xFFFE) || Bits != 16 || Channels < 1 || Channels > 2) throw new InvalidDataException("Nur 16-Bit-PCM");
                    fmt = true;
                }
                else if (id == "data")
                {
                    if (!fmt) throw new InvalidDataException("WAV ohne Format");
                    DataStart = _fs.Position;
                    // Laenge 0 oder zu gross (Aufnahme abgebrochen): bis zum Dateiende
                    DataBytes = size == 0 || DataStart + size > _fs.Length ? _fs.Length - DataStart : size;
                    DataBytes -= DataBytes % (Channels * 2);
                    return;
                }
                _fs.Position = next;
            }
            throw new InvalidDataException("WAV ohne Daten");
        }

        public long Frames => DataBytes / (Channels * 2);

        /// <summary>Bis zu frames Frames lesen (Stereo; Mono wird verdoppelt). Liefert die Zahl gelesener Frames.</summary>
        public int Read(short[] dst, int frames, ref long pos)
        {
            long left = Frames - pos;
            int n = (int)Math.Min(frames, left);
            if (n <= 0) return 0;
            int bytes = n * Channels * 2;
            var raw = new byte[bytes];
            _fs.Position = DataStart + pos * Channels * 2;
            int got = 0;
            while (got < bytes) { int r = _fs.Read(raw, got, bytes - got); if (r <= 0) break; got += r; }
            n = got / (Channels * 2);
            for (int i = 0; i < n; i++)
            {
                if (Channels == 2) { dst[i * 2] = (short)(raw[i * 4] | raw[i * 4 + 1] << 8); dst[i * 2 + 1] = (short)(raw[i * 4 + 2] | raw[i * 4 + 3] << 8); }
                else { short v = (short)(raw[i * 2] | raw[i * 2 + 1] << 8); dst[i * 2] = v; dst[i * 2 + 1] = v; }
            }
            pos += n;
            return n;
        }

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>
    /// 2.19: eigener FLAC-Kodierer (verlustfrei, Stereo, 16 Bit): feste Vorhersage (Ordnung 0–4), Rice-Kodierung mit Partitionen,
    /// Mitte/Seite-Stereo und Stille als "konstant". Braucht keine Zusatzprogramme und keine Windows-Komponenten.
    /// </summary>
    internal static class FlacEncoder
    {
        public const int BlockSize = 4096;

        private sealed class BitWriter
        {
            public byte[] Buf = new byte[1 << 16];
            public int Len;          // Bytes
            private ulong _acc; private int _bits;

            public void Reset() { Len = 0; _acc = 0; _bits = 0; }

            public void Write(uint value, int bits)
            {
                if (bits == 0) return;
                _acc = (_acc << bits) | (value & (bits == 32 ? 0xFFFFFFFFu : ((1u << bits) - 1)));
                _bits += bits;
                while (_bits >= 8) { _bits -= 8; Put((byte)(_acc >> _bits)); }
            }

            public void WriteSigned(int value, int bits) => Write((uint)value, bits);

            public void WriteUnary(uint zeros)
            {
                while (zeros >= 32) { Write(0, 32); zeros -= 32; }
                Write(1, (int)zeros + 1);
            }

            public void Align() { if (_bits > 0) Write(0, 8 - _bits); }

            private void Put(byte b)
            {
                if (Len == Buf.Length) Array.Resize(ref Buf, Buf.Length * 2);
                Buf[Len++] = b;
            }
        }

        private static readonly byte[] Crc8Table = MakeCrc8();
        private static readonly ushort[] Crc16Table = MakeCrc16();

        private static byte[] MakeCrc8()
        {
            var t = new byte[256];
            for (int i = 0; i < 256; i++) { int c = i; for (int k = 0; k < 8; k++) c = (c & 0x80) != 0 ? (c << 1) ^ 0x07 : c << 1; t[i] = (byte)c; }
            return t;
        }

        private static ushort[] MakeCrc16()
        {
            var t = new ushort[256];
            for (int i = 0; i < 256; i++) { int c = i << 8; for (int k = 0; k < 8; k++) c = (c & 0x8000) != 0 ? (c << 1) ^ 0x8005 : c << 1; t[i] = (ushort)c; }
            return t;
        }

        internal static byte Crc8(byte[] b, int off, int len) { byte c = 0; for (int i = 0; i < len; i++) c = Crc8Table[c ^ b[off + i]]; return c; }
        internal static ushort Crc16(byte[] b, int off, int len) { int c = 0; for (int i = 0; i < len; i++) c = ((c << 8) ^ Crc16Table[(c >> 8) ^ b[off + i]]) & 0xFFFF; return (ushort)c; }

        /// <summary>WAV nach FLAC. Wirft bei Fehlern (die WAV bleibt dann unangetastet).</summary>
        public static void Encode(string wavPath, string flacPath)
        {
            using (var wav = new WavReader(wavPath))
            using (var fs = new FileStream(flacPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            using (var md5 = MD5.Create())
            {
                // Kopf: "fLaC" + STREAMINFO (wird am Ende mit Laenge und MD5 neu geschrieben)
                fs.Write(Encoding.ASCII.GetBytes("fLaC"), 0, 4);
                var info = StreamInfo(wav.Rate, 0, 0, 0, new byte[16]);
                fs.Write(info, 0, info.Length);

                var pcm = new short[BlockSize * 2];
                var raw = new byte[BlockSize * 4];
                var l = new int[BlockSize]; var r = new int[BlockSize]; var m = new int[BlockSize]; var s = new int[BlockSize];
                var bw = new BitWriter();
                var work = new Work();
                long pos = 0, frame = 0, total = 0;
                int minFrame = int.MaxValue, maxFrame = 0;
                while (true)
                {
                    int n = wav.Read(pcm, BlockSize, ref pos);
                    if (n <= 0) break;
                    for (int i = 0; i < n; i++)
                    {
                        int a = pcm[i * 2], b = pcm[i * 2 + 1];
                        l[i] = a; r[i] = b; m[i] = (a + b) >> 1; s[i] = a - b;
                        raw[i * 4] = (byte)a; raw[i * 4 + 1] = (byte)(a >> 8); raw[i * 4 + 2] = (byte)b; raw[i * 4 + 3] = (byte)(b >> 8);
                    }
                    md5.TransformBlock(raw, 0, n * 4, raw, 0);
                    bw.Reset();
                    WriteFrame(bw, frame, n, l, r, m, s, work);
                    fs.Write(bw.Buf, 0, bw.Len);
                    minFrame = Math.Min(minFrame, bw.Len); maxFrame = Math.Max(maxFrame, bw.Len);
                    total += n; frame++;
                }
                md5.TransformFinalBlock(new byte[0], 0, 0);
                fs.Position = 4;
                info = StreamInfo(wav.Rate, total, total == 0 ? 0 : minFrame, maxFrame, md5.Hash);
                fs.Write(info, 0, info.Length);
            }
        }

        private static byte[] StreamInfo(int rate, long totalSamples, int minFrame, int maxFrame, byte[] md5)
        {
            var bw = new BitWriter();
            bw.Write(1, 1); bw.Write(0, 7); bw.Write(34, 24);   // letzter Metadaten-Block, Typ STREAMINFO, 34 Bytes
            bw.Write(BlockSize, 16); bw.Write(BlockSize, 16);
            bw.Write((uint)minFrame, 24); bw.Write((uint)maxFrame, 24);
            bw.Write((uint)rate, 20); bw.Write(1, 3); bw.Write(15, 5);
            bw.Write((uint)(totalSamples >> 32) & 0xF, 4); bw.Write((uint)totalSamples, 32);
            foreach (var b in md5) bw.Write(b, 8);
            var r = new byte[bw.Len];
            Array.Copy(bw.Buf, r, bw.Len);
            return r;
        }

        // ---------------------------------------------------------------- Rahmen

        private sealed class Work
        {
            public readonly long[] Res = new long[BlockSize];
        }

        /// <summary>Beste Kodierung eines Kanals: Art (0 konstant, 1 roh, 2 fest), Ordnung, Partitionsordnung, Bits.</summary>
        private struct Plan { public int Kind, Order, PartOrder; public long Bits; public int[] Params; }

        private static void WriteFrame(BitWriter bw, long frameNo, int n, int[] l, int[] r, int[] m, int[] s, Work w)
        {
            var pl = Analyze(l, n, 16, w); var pr = Analyze(r, n, 16, w); var pm = Analyze(m, n, 16, w); var ps = Analyze(s, n, 17, w);
            long ind = pl.Bits + pr.Bits, ls = pl.Bits + ps.Bits, rs = pr.Bits + ps.Bits, ms = pm.Bits + ps.Bits;
            int assign; // 1 unabhaengig, 8 links/seite, 9 rechts/seite, 10 mitte/seite
            long best = Math.Min(Math.Min(ind, ls), Math.Min(rs, ms));
            assign = best == ind ? 1 : best == ms ? 10 : best == ls ? 8 : 9;

            // Kopf
            bw.Write(0x3FFE, 14); bw.Write(0, 1); bw.Write(0, 1);
            bool full = n == BlockSize;
            bw.Write(full ? 12u : 7u, 4);      // 4096 bzw. "16 Bit Laenge folgt"
            bw.Write(0, 4);                   // Abtastrate aus STREAMINFO
            bw.Write((uint)assign, 4);
            bw.Write(4, 3); bw.Write(0, 1);   // 16 Bit
            WriteUtf8(bw, frameNo);
            if (!full) bw.Write((uint)(n - 1), 16);
            bw.Align();
            bw.Write(Crc8(bw.Buf, 0, bw.Len), 8);

            switch (assign)
            {
                case 1: WriteSub(bw, l, n, 16, pl, w); WriteSub(bw, r, n, 16, pr, w); break;
                case 8: WriteSub(bw, l, n, 16, pl, w); WriteSub(bw, s, n, 17, ps, w); break;
                case 9: WriteSub(bw, s, n, 17, ps, w); WriteSub(bw, r, n, 16, pr, w); break;
                default: WriteSub(bw, m, n, 16, pm, w); WriteSub(bw, s, n, 17, ps, w); break;
            }
            bw.Align();
            ushort crc = Crc16(bw.Buf, 0, bw.Len);
            bw.Write(crc, 16);
        }

        private static void WriteUtf8(BitWriter bw, long v)
        {
            if (v < 0x80) { bw.Write((uint)v, 8); return; }
            int bytes = v < 0x800 ? 2 : v < 0x10000 ? 3 : v < 0x200000 ? 4 : v < 0x4000000 ? 5 : 6;
            int shift = (bytes - 1) * 6;
            uint first = (uint)((0xFF00 >> bytes) & 0xFF) | (uint)(v >> shift);
            bw.Write(first & 0xFF, 8);
            for (shift -= 6; shift >= 0; shift -= 6) bw.Write(0x80u | (uint)((v >> shift) & 0x3F), 8);
        }

        private static long Residual(int[] x, int n, int order, long[] res)
        {
            long sum = 0;
            for (int i = order; i < n; i++)
            {
                long e;
                switch (order)
                {
                    case 0: e = x[i]; break;
                    case 1: e = (long)x[i] - x[i - 1]; break;
                    case 2: e = (long)x[i] - 2L * x[i - 1] + x[i - 2]; break;
                    case 3: e = (long)x[i] - 3L * x[i - 1] + 3L * x[i - 2] - x[i - 3]; break;
                    default: e = (long)x[i] - 4L * x[i - 1] + 6L * x[i - 2] - 4L * x[i - 3] + x[i - 4]; break;
                }
                res[i] = e;
                sum += e < 0 ? -e : e;
            }
            return sum;
        }

        private static ulong Zig(long e) => e >= 0 ? (ulong)e << 1 : ((ulong)(-e) << 1) - 1;

        /// <summary>Rice-Parameter fuer eine Partition aus Summe und Anzahl (geschaetzte Bits: c·(k+1) + S/2^k).</summary>
        private static int ParamK(ulong sum, int cnt, out long bits)
        {
            int bk = 0; long best = long.MaxValue;
            for (int k = 0; k <= 14; k++)
            {
                long b = (long)cnt * (k + 1) + (long)(sum >> k);
                if (b < best) { best = b; bk = k; }
                else if (k > 0 && b > best) break;
            }
            bits = best;
            return bk;
        }

        private static Plan Analyze(int[] x, int n, int bps, Work w)
        {
            bool constant = true;
            for (int i = 1; i < n; i++) if (x[i] != x[0]) { constant = false; break; }
            if (constant) return new Plan { Kind = 0, Bits = 8 + bps };
            var best = new Plan { Kind = 1, Bits = 8 + (long)bps * n };
            int maxOrder = Math.Min(4, n - 1);
            var sums = new ulong[64];
            for (int order = 0; order <= maxOrder; order++)
            {
                Residual(x, n, order, w.Res);
                int maxPo = 0;
                while (maxPo < 6 && n % (1 << (maxPo + 1)) == 0 && (n >> (maxPo + 1)) > order) maxPo++;
                int parts = 1 << maxPo, size = n >> maxPo;
                for (int p = 0; p < parts; p++)
                {
                    ulong sm = 0;
                    int from = p == 0 ? order : p * size, to = (p + 1) * size;
                    for (int i = from; i < to; i++) sm += Zig(w.Res[i]);
                    sums[p] = sm;
                }
                for (int po = maxPo; po >= 0; po--)
                {
                    parts = 1 << po; size = n >> po;
                    long bits = 8 + (long)order * bps + 2 + 4;
                    var ks = new int[parts];
                    for (int p = 0; p < parts; p++)
                    {
                        int cnt = p == 0 ? size - order : size;
                        ks[p] = ParamK(sums[p], cnt, out long pb);
                        bits += 4 + pb;
                    }
                    if (bits < best.Bits) best = new Plan { Kind = 2, Order = order, PartOrder = po, Bits = bits, Params = ks };
                    for (int p = 0; p < parts / 2; p++) sums[p] = sums[2 * p] + sums[2 * p + 1];   // eine Stufe groeber
                }
            }
            return best;
        }

        private static void WriteSub(BitWriter bw, int[] x, int n, int bps, Plan p, Work w)
        {
            bw.Write(0, 1);
            if (p.Kind == 0) { bw.Write(0, 6); bw.Write(0, 1); bw.WriteSigned(x[0], bps); return; }
            if (p.Kind == 1)
            {
                bw.Write(1, 6); bw.Write(0, 1);
                for (int i = 0; i < n; i++) bw.WriteSigned(x[i], bps);
                return;
            }
            bw.Write((uint)(8 | p.Order), 6); bw.Write(0, 1);
            for (int i = 0; i < p.Order; i++) bw.WriteSigned(x[i], bps);
            Residual(x, n, p.Order, w.Res);
            bw.Write(0, 2);                       // Rice mit 4-Bit-Parametern
            bw.Write((uint)p.PartOrder, 4);
            int parts = 1 << p.PartOrder, size = n / parts;
            for (int q = 0; q < parts; q++)
            {
                int k = p.Params[q];
                bw.Write((uint)k, 4);
                int from = q == 0 ? p.Order : q * size, to = (q + 1) * size;
                for (int i = from; i < to; i++)
                {
                    ulong u = Zig(w.Res[i]);
                    bw.WriteUnary((uint)(u >> k));
                    if (k > 0) bw.Write((uint)(u & ((1UL << k) - 1)), k);
                }
            }
        }
    }

    /// <summary>2.19: WAV nach MP3 mit dem MP3-Kodierer von Windows (Media Foundation). Nur unter Windows.</summary>
    internal static class Mp3Encoder
    {
        public static readonly int[] Kbps = { 192, 256, 320 };

        /// <summary>Liefert null bei Erfolg, sonst eine Fehlermeldung (die WAV bleibt dann).</summary>
        public static string Encode(string wavPath, string mp3Path, int kbps)
        {
            if (!Win32.IsWindows) return "MP3 gibt es nur unter Windows";
            bool started = false;
            IMFSinkWriter writer = null;
            try
            {
                using (var wav = new WavReader(wavPath))
                {
                    if (wav.Rate != 48000 && wav.Rate != 44100 && wav.Rate != 32000) return "MP3: Abtastrate " + wav.Rate + " Hz geht nicht";
                    int hr = MF.MFStartup(MF.MF_VERSION, 0);
                    if (hr < 0) return "Media Foundation fehlt (Windows N ohne Media Feature Pack?)";
                    started = true;
                    try { File.Delete(mp3Path); } catch { }
                    hr = MFW.MFCreateSinkWriterFromURL(mp3Path, IntPtr.Zero, IntPtr.Zero, out writer);
                    if (hr < 0 || writer == null) return "MP3: Datei kann nicht angelegt werden (0x" + hr.ToString("X8") + ")";

                    MF.MFCreateMediaType(out IMFMediaType outType);
                    Set(outType, MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Audio);
                    Set(outType, MF.MF_MT_SUBTYPE, MFW.MFAudioFormat_MP3);
                    SetInt(outType, MF.MF_MT_AUDIO_NUM_CHANNELS, 2);
                    SetInt(outType, MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, wav.Rate);
                    SetInt(outType, MFW.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, Math.Max(16000, Math.Min(40000, kbps * 125)));
                    SetInt(outType, MFW.MF_MT_AUDIO_BLOCK_ALIGNMENT, 1);
                    hr = writer.AddStream(outType, out int stream);
                    Marshal.ReleaseComObject(outType);
                    if (hr < 0) return "MP3-Kodierer von Windows nicht gefunden (0x" + hr.ToString("X8") + ")";

                    MF.MFCreateMediaType(out IMFMediaType inType);
                    Set(inType, MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Audio);
                    Set(inType, MF.MF_MT_SUBTYPE, MFW.MFAudioFormat_PCM);
                    SetInt(inType, MF.MF_MT_AUDIO_NUM_CHANNELS, 2);
                    SetInt(inType, MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, wav.Rate);
                    SetInt(inType, MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                    SetInt(inType, MFW.MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
                    SetInt(inType, MFW.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, wav.Rate * 4);
                    SetInt(inType, MFW.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
                    hr = writer.SetInputMediaType(stream, inType, IntPtr.Zero);
                    Marshal.ReleaseComObject(inType);
                    if (hr < 0) return "MP3: Eingangsformat abgelehnt (0x" + hr.ToString("X8") + ")";
                    hr = writer.BeginWriting();
                    if (hr < 0) return "MP3: Start fehlgeschlagen (0x" + hr.ToString("X8") + ")";

                    int chunk = wav.Rate / 2;   // halbe Sekunde je Block
                    var pcm = new short[chunk * 2];
                    long pos = 0, time = 0;
                    while (true)
                    {
                        int n = wav.Read(pcm, chunk, ref pos);
                        if (n <= 0) break;
                        int bytes = n * 4;
                        hr = MFW.MFCreateMemoryBuffer(bytes, out IMFMediaBuffer buf);
                        if (hr < 0) return "MP3: kein Speicher";
                        buf.Lock(out IntPtr p, out _, out _);
                        Marshal.Copy(pcm, 0, p, n * 2);
                        buf.Unlock();
                        buf.SetCurrentLength(bytes);
                        MFW.MFCreateSample(out IMFSample sample);
                        sample.AddBuffer(buf);
                        long dur = n * 10000000L / wav.Rate;
                        sample.SetSampleTime(time);
                        sample.SetSampleDuration(dur);
                        time += dur;
                        hr = writer.WriteSample(stream, sample);
                        Marshal.ReleaseComObject(sample);
                        Marshal.ReleaseComObject(buf);
                        if (hr < 0) return "MP3: Schreiben fehlgeschlagen (0x" + hr.ToString("X8") + ")";
                    }
                    hr = writer.DoFinalize();
                    if (hr < 0) return "MP3: Abschluss fehlgeschlagen (0x" + hr.ToString("X8") + ")";
                }
                return File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0 ? null : "MP3: leere Datei";
            }
            catch (Exception ex) { return "MP3: " + ex.Message; }
            finally
            {
                if (writer != null) try { Marshal.ReleaseComObject(writer); } catch { }
                if (started) try { MF.MFShutdown(); } catch { }
            }
        }

        private static void Set(IMFMediaType t, Guid key, Guid value) { var k = key; var v = value; t.SetGUID(ref k, ref v); }
        private static void SetInt(IMFMediaType t, Guid key, int value) { var k = key; t.SetUINT32(ref k, value); }
    }

    /// <summary>2.19: Aufnahme nach dem Stoppen umwandeln (FLAC/MP3); die WAV wird nur geloescht, wenn das geklappt hat.</summary>
    internal static class RecordingExport
    {
        public static readonly string[] FormatNames = { "WAV", "FLAC", "MP3" };

        /// <summary>Datei umwandeln. Liefert die neue Datei (oder die alte bei WAV/Fehler); error = Fehlermeldung oder null.</summary>
        public static string Convert(string wav, int format, int kbps, out string error)
        {
            error = null;
            if (format <= 0 || wav == null || !File.Exists(wav)) return wav;
            string target = Path.ChangeExtension(wav, format == 1 ? ".flac" : ".mp3");
            try
            {
                if (format == 1) FlacEncoder.Encode(wav, target);
                else
                {
                    error = Mp3Encoder.Encode(wav, target, kbps);
                    if (error != null) { try { File.Delete(target); } catch { } return wav; }
                }
                if (!File.Exists(target) || new FileInfo(target).Length < 8) { error = "Umwandeln fehlgeschlagen"; return wav; }
                try { File.Delete(wav); } catch { }
                return target;
            }
            catch (Exception ex)
            {
                error = (format == 1 ? "FLAC: " : "MP3: ") + ex.Message;
                try { File.Delete(target); } catch { }
                return wav;
            }
        }
    }
}
