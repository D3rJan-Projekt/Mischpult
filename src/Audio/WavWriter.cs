using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace Mischpult.Audio
{
    /// <summary>
    /// Schreibt Stereo-Audio als WAV (16 Bit PCM). Write() ist threadsicher und blockiert nicht –
    /// das eigentliche Schreiben auf die Platte macht ein eigener Thread. Sehr lange Aufnahmen werden
    /// automatisch in Teile aufgeteilt (WAV-Dateien duerfen nicht groesser als 4 GB werden).
    /// </summary>
    internal sealed class WavWriter : IDisposable, ILoopbackSink
    {
        internal static long MaxBytesPerFile = 2000000000L; // ~2,9 Stunden bei 48 kHz (fuer Tests aenderbar)

        public readonly string Folder;
        public readonly string BaseName;
        public int Rate { get; private set; }

        private readonly ConcurrentQueue<short[]> _queue = new ConcurrentQueue<short[]>();
        private static readonly short[] SilenceMarker = new short[0];
        private readonly ConcurrentQueue<int> _silence = new ConcurrentQueue<int>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _thread;
        private volatile bool _closing;
        private long _frames;              // insgesamt geschrieben (alle Teile)
        private float _peakL, _peakR;
        private readonly object _peakLock = new object();
        private FileStream _file;
        private long _dataBytes;
        private int _part = 1;
        private volatile string _currentFile;
        private volatile string _error;

        public WavWriter(string folder, string baseName, int rate)
        {
            Folder = folder; BaseName = baseName; Rate = rate;
            Directory.CreateDirectory(folder);
            OpenPart(); // Fehler hier direkt melden (z. B. Ordner nicht beschreibbar)
            _thread = new Thread(Run) { IsBackground = true, Name = "Mischpult Aufnahme", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>Abtastrate aendern – nur bevor Ton geschrieben wurde (z. B. wenn das Geraet 44,1 kHz liefert).</summary>
        public void SetRate(int rate)
        {
            if (rate > 0 && Frames == 0) Rate = rate;
        }

        public string CurrentFile => _currentFile;
        private readonly System.Collections.Generic.List<string> _files = new System.Collections.Generic.List<string>();
        /// <summary>2.19: alle Teile dieser Aufnahme (fuers Umwandeln nach FLAC/MP3).</summary>
        public string[] Files { get { lock (_files) return _files.ToArray(); } }
        public int Parts => _part;
        public string Error => _error;
        public long Frames => Interlocked.Read(ref _frames);
        public double Seconds => Frames / (double)Rate;

        /// <summary>Hoechster Pegel seit dem letzten Aufruf (fuer die Anzeige).</summary>
        public void TakePeak(out float l, out float r)
        {
            lock (_peakLock) { l = _peakL; r = _peakR; _peakL = 0; _peakR = 0; }
        }

        /// <summary>Stereo-Float (L, R, L, R ...), frames Frames ab Index 0.</summary>
        public void Write(float[] stereo, int frames) => Write(stereo, 0, frames);

        public void Write(float[] stereo, int offsetFrames, int frames)
        {
            if (_closing || frames <= 0) return;
            var pcm = new short[frames * 2];
            float pl = 0, pr = 0;
            for (int i = 0, k = offsetFrames * 2; i < frames * 2; i += 2, k += 2)
            {
                float l = stereo[k], r = stereo[k + 1];
                float al = l < 0 ? -l : l, ar = r < 0 ? -r : r;
                if (al > pl) pl = al;
                if (ar > pr) pr = ar;
                pcm[i] = ToPcm(l);
                pcm[i + 1] = ToPcm(r);
            }
            lock (_peakLock) { if (pl > _peakL) _peakL = pl; if (pr > _peakR) _peakR = pr; }
            Interlocked.Add(ref _frames, frames);
            _queue.Enqueue(pcm);
            _wake.Set();
        }

        public void WriteSilence(int frames)
        {
            if (_closing || frames <= 0) return;
            Interlocked.Add(ref _frames, frames);
            // nur eine Markierung: der Schreib-Thread schreibt die Nullen aus einem kleinen Puffer
            lock (_silence) { _silence.Enqueue(frames); _queue.Enqueue(SilenceMarker); }
            _wake.Set();
        }

        private static short ToPcm(float x)
        {
            if (float.IsNaN(x)) return 0;
            float v = x * 32767f;
            if (v > 32767f) v = 32767f; else if (v < -32768f) v = -32768f;
            return (short)Math.Round(v);
        }

        private void Run()
        {
            long nextHeader = Environment.TickCount + 2000;
            byte[] buf = new byte[0];
            byte[] zeros = new byte[16384];
            while (true)
            {
                bool closing = _closing;
                _wake.WaitOne(200);
                try
                {
                    while (_queue.TryDequeue(out var pcm))
                    {
                        if (ReferenceEquals(pcm, SilenceMarker))
                        {
                            int frames;
                            lock (_silence) { if (!_silence.TryDequeue(out frames)) continue; }
                            long left = (long)frames * 4;
                            while (left > 0)
                            {
                                int chunk = (int)Math.Min(left, zeros.Length);
                                if (_dataBytes + chunk > MaxBytesPerFile) { FinishPart(); _part++; OpenPart(); }
                                _file.Write(zeros, 0, chunk);
                                _dataBytes += chunk;
                                left -= chunk;
                            }
                            continue;
                        }
                        int bytes = pcm.Length * 2;
                        if (_dataBytes + bytes > MaxBytesPerFile)
                        {
                            FinishPart();
                            _part++;
                            OpenPart();
                        }
                        if (buf.Length < bytes) buf = new byte[bytes];
                        Buffer.BlockCopy(pcm, 0, buf, 0, bytes); // Windows ist Little-Endian wie WAV
                        _file.Write(buf, 0, bytes);
                        _dataBytes += bytes;
                    }
                    if (Environment.TickCount - nextHeader >= 0)
                    {
                        // Kopf regelmaessig aktualisieren: bricht der PC ab, bleibt die Datei abspielbar
                        nextHeader = Environment.TickCount + 2000;
                        WriteHeader(true);
                    }
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                    lock (_silence) { while (_queue.TryDequeue(out _)) { } while (_silence.TryDequeue(out _)) { } }
                    Thread.Sleep(200);
                }
                if (closing && _queue.IsEmpty) break;
            }
            try { FinishPart(); } catch (Exception ex) { _error = ex.Message; }
        }

        private void OpenPart()
        {
            string name = BaseName + (_part > 1 ? " (Teil " + _part + ")" : "") + ".wav";
            string path = Path.Combine(Folder, name);
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            lock (_files) _files.Add(path);
            _dataBytes = 0;
            _currentFile = path;
            WriteHeader(false);
        }

        private void FinishPart()
        {
            if (_file == null) return;
            WriteHeader(true);
            _file.Dispose();
            _file = null;
        }

        private void WriteHeader(bool keepPosition)
        {
            if (_file == null) return;
            long pos = _file.Position;
            _file.Position = 0;
            var h = new byte[44];
            using (var ms = new MemoryStream(h))
            using (var w = new BinaryWriter(ms, Encoding.ASCII))
            {
                uint data = (uint)Math.Min(_dataBytes, uint.MaxValue - 36);
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36u + data);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16u);
                w.Write((ushort)1);          // PCM
                w.Write((ushort)2);          // Stereo
                w.Write((uint)Rate);
                w.Write((uint)(Rate * 4));   // Bytes pro Sekunde
                w.Write((ushort)4);          // Block-Groesse
                w.Write((ushort)16);         // Bit
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data);
            }
            _file.Write(h, 0, h.Length);
            if (keepPosition) _file.Position = Math.Max(pos, 44);
            _file.Flush();
        }

        public void Dispose()
        {
            if (_closing) return;
            _closing = true;
            _wake.Set();
            try { _thread.Join(5000); } catch { }
        }
    }
}
