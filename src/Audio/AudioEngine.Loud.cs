using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Mischpult.Audio
{
    /// <summary>
    /// 2.16.1: Lautheit (LUFS) weiterer Ausgaenge – je Geraet ein eigener Loopback mit eigener Messung
    /// (fuer die LUFS-Anzeige in jedem Ausgang und den Lautheits-Verlauf eines gewaehlten Geraets).
    /// Das Standardgeraet misst weiterhin SetLoudness (folgt dem Wechsel des Standardgeraets).
    /// Oeffnen/Schliessen im Hintergrund, der UI-Thread setzt nur den Wunsch.
    /// </summary>
    internal sealed partial class AudioEngine
    {
        private sealed class LoudTap
        {
            public string Id;
            public LoopbackRecorder Rec;
            public LoudnessSink Sink;
        }

        private readonly object _loudTapLock = new object();
        private volatile LoudTap[] _loudTaps = new LoudTap[0];
        private volatile string _loudKey = "";          // gewuenscht ("" = keine)
        private volatile string _loudApplied = "";      // angewendet (null = neu anwenden)
        private int _loudWorking, _loudRetryMs = Environment.TickCount;
        private string[] _loudLastIds;
        private string _loudLastKey;
        private Dictionary<string, float[]> _heavyLoudDev;

        /// <summary>Schluessel einer Geraete-Liste ("" = keine), ohne leere und doppelte.</summary>
        internal static string LoudKey(string[] ids) =>
            ids == null || ids.Length == 0 ? "" : string.Join("\n", ids.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase));

        public void SetLoudnessDevices(string[] deviceIds)
        {
            string key = _loudLastKey != null && ReferenceEquals(deviceIds, _loudLastIds) ? _loudLastKey : LoudKey(deviceIds);
            _loudLastIds = deviceIds; _loudLastKey = key;
            if (key == _loudKey && key == _loudApplied)
            {
                // laeuft eins nicht mehr (abgesteckt, exklusiv belegt)? Alle paar Sekunden neu versuchen
                if (key.Length == 0 || Environment.TickCount - _loudRetryMs < 5000) return;
                _loudRetryMs = Environment.TickCount;
                if (!_loudTaps.Any(t => t.Rec == null || t.Rec.Stopped)) return;
                _loudApplied = null;
            }
            else if (key != _loudKey)
            {
                _loudKey = key;
                _loudRetryMs = Environment.TickCount;
            }
            if (Interlocked.Exchange(ref _loudWorking, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    while (true)
                    {
                        string want = _loudKey;
                        ApplyLoudTaps(want);
                        _loudApplied = want;
                        if (want == _loudKey) break;
                    }
                }
                catch { _loudApplied = null; }
                finally { Interlocked.Exchange(ref _loudWorking, 0); }
            });
        }

        private void ApplyLoudTaps(string key)
        {
            lock (_loudTapLock)
            {
                var want = key.Length == 0 ? new string[0] : key.Split('\n');
                var keep = new List<LoudTap>();
                foreach (var t in _loudTaps)
                {
                    // laufende Messungen behalten (der integrierte Wert zaehlt weiter)
                    if (want.Contains(t.Id, StringComparer.OrdinalIgnoreCase) && t.Rec != null && !t.Rec.Stopped) keep.Add(t);
                    else { try { t.Rec?.Dispose(); } catch { } }
                }
                _loudTaps = keep.ToArray();
                foreach (var id in want)
                {
                    if (keep.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
                    var t = new LoudTap { Id = id, Sink = new LoudnessSink() };
                    try { t.Rec = new LoopbackRecorder(id, t.Sink); } catch { t.Rec = null; }
                    keep.Add(t);   // auch wenn es nicht ging: wird beim naechsten Versuch neu geoeffnet
                    _loudTaps = keep.ToArray();
                }
            }
        }

        /// <summary>Lautheit je gemessenem Geraet (null = keins gemessen).</summary>
        private Dictionary<string, float[]> ComputeDeviceLoudness()
        {
            var taps = _loudTaps;
            if (taps.Length == 0) return null;
            var d = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in taps)
            {
                var v = t.Sink?.Compute();
                if (v != null && t.Id != null) d[t.Id] = v;
            }
            return d;
        }

        private void ResetDeviceLoudness()
        {
            foreach (var t in _loudTaps) { try { t.Sink?.ResetIntegrated(); } catch { } }
        }

        private void StopLoudTaps()
        {
            _loudKey = "";
            lock (_loudTapLock)
            {
                foreach (var t in _loudTaps) { try { t.Rec?.Dispose(); } catch { } }
                _loudTaps = new LoudTap[0];
            }
        }
    }
}
