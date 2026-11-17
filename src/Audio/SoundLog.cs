using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace Mischpult.Audio
{
    /// <summary>
    /// 3.4.1: Ton-Protokoll zur Fehlersuche (Standard aus). Schreibt mit, wann eine App anfaengt, mit welcher Lautstaerke
    /// Windows sie startet, wann das Pult sie einstellt, wer die Lautstaerke von aussen aendert und wie der EQ-Weg die
    /// ersten Bloecke verstaerkt. Geschrieben wird gesammelt aus einem eigenen Faden – nie aus dem Ton-Faden.
    /// </summary>
    internal static class SoundLog
    {
        private static volatile bool _on;
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static Thread _writer;
        private static readonly object _startLock = new object();
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private const long MaxBytes = 512 * 1024;

        public static string FilePath => Path.Combine(MixerSettings.LogFolder, "ton-protokoll.txt");   // 4.0: Ordner Protokolle

        public static bool On
        {
            get => _on;
            set
            {
                if (value == _on) return;
                _on = value;
                if (value) { EnsureWriter(); Write("Protokoll an – Mischpult " + Program.Version + ", Windows " + Environment.OSVersion.Version); }
            }
        }

        public static void Write(string line)
        {
            if (!_on || line == null) return;
            _queue.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line);
            if (_queue.Count > 2000) { while (_queue.Count > 1500 && _queue.TryDequeue(out _)) { } }   // nie unbegrenzt sammeln
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;
            lock (_startLock)
            {
                if (_writer != null) return;
                var t = new Thread(Loop) { IsBackground = true, Name = "Mischpult Protokoll", Priority = ThreadPriority.BelowNormal };
                t.Start();
                _writer = t;
            }
        }

        private static void Loop()
        {
            var sb = new StringBuilder();
            while (true)
            {
                _wake.WaitOne(250);
                if (_queue.IsEmpty) continue;
                sb.Clear();
                while (_queue.TryDequeue(out var l)) sb.Append(l).Append("\r\n");
                try
                {
                    string p = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = p + ".alt.txt";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(p, old);
                    }
                    File.AppendAllText(p, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        public static string F(float v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
