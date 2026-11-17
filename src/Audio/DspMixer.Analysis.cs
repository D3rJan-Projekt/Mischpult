using System;
using System.Collections.Generic;

namespace Mischpult.Audio
{
    /// <summary>
    /// 2.17: Lautheit (LUFS) und Spektrum pro Kanal bzw. pro App – fuer das Anzeigefeld der Kanaele.
    /// Kanaele auf dem EQ-Weg werden nach EQ/Kompressor (vor dem Fader) gemessen, die anderen Apps aus ihrer Pegel-Messung.
    /// Gemessen wird nur, was gerade angezeigt werden soll.
    /// </summary>
    internal sealed partial class DspMixer
    {
        internal sealed class AnalysisWish
        {
            public static readonly AnalysisWish None = new AnalysisWish();
            public HashSet<string> LoudCh = new HashSet<string>(), SpecCh = new HashSet<string>();
            public HashSet<uint> LoudPids = new HashSet<uint>(), SpecPids = new HashSet<uint>();
            public bool Any => LoudCh.Count + SpecCh.Count + LoudPids.Count + SpecPids.Count > 0;
        }

        private volatile AnalysisWish _analysis = AnalysisWish.None;
        private long _nextLoudMs, _nextSpecAllMs;
        private volatile Dictionary<string, float[]> _chLoud = new Dictionary<string, float[]>(), _chSpec = new Dictionary<string, float[]>();
        private volatile Dictionary<uint, float[]> _pidLoud = new Dictionary<uint, float[]>(), _pidSpec = new Dictionary<uint, float[]>();

        /// <summary>Was gemessen werden soll (Kanaele auf dem EQ-Weg bzw. Prozesse mit Pegel-Messung).</summary>
        public void SetAnalysis(HashSet<string> loudCh, HashSet<string> specCh, HashSet<uint> loudPids, HashSet<uint> specPids)
        {
            _analysis = new AnalysisWish
            {
                LoudCh = loudCh ?? new HashSet<string>(), SpecCh = specCh ?? new HashSet<string>(),
                LoudPids = loudPids ?? new HashSet<uint>(), SpecPids = specPids ?? new HashSet<uint>(),
            };
        }

        /// <summary>Lautheit [Momentan, Kurzzeit, Integriert] je Kanal (EQ-Weg, vor dem Fader).</summary>
        public Dictionary<string, float[]> ChannelLoudness => _chLoud;
        /// <summary>Spektrum (dB je Band) je Kanal (EQ-Weg, vor dem Fader).</summary>
        public Dictionary<string, float[]> ChannelSpectra => _chSpec;
        /// <summary>Lautheit je Prozess (Pegel-Messung, so wie die App spielt).</summary>
        public Dictionary<uint, float[]> PidLoudness => _pidLoud;
        /// <summary>Spektrum je Prozess (Pegel-Messung).</summary>
        public Dictionary<uint, float[]> PidSpectra => _pidSpec;

        /// <summary>Messung einer App an den Wunsch anpassen (anlegen bzw. weglassen).</summary>
        private void AnalysisFor(Capture c)
        {
            var an = _analysis;
            if (an.LoudPids.Contains(c.Pid)) { if (c.Loud == null) c.Loud = new LoudnessSink(); }
            else if (c.Loud != null) c.Loud = null;
            if (an.SpecPids.Contains(c.Pid)) { if (c.Spec == null) c.Spec = new SpectrumAnalyzer(Rate); }
            else if (c.Spec != null) c.Spec = null;
        }

        /// <summary>Aus dem Pegel-Takt: Lautheit alle 100 ms, Spektren alle 60 ms neu.</summary>
        private void PublishAnalysis(long nowMs)
        {
            var an = _analysis;
            if (!an.Any)
            {
                if (_chLoud.Count > 0) _chLoud = new Dictionary<string, float[]>();
                if (_chSpec.Count > 0) _chSpec = new Dictionary<string, float[]>();
                if (_pidLoud.Count > 0) _pidLoud = new Dictionary<uint, float[]>();
                if (_pidSpec.Count > 0) _pidSpec = new Dictionary<uint, float[]>();
                return;
            }
            if (nowMs >= _nextLoudMs)
            {
                _nextLoudMs = nowMs + 100;
                var cl = new Dictionary<string, float[]>();
                foreach (var kv in _states)
                    if (kv.Value.Loud != null && an.LoudCh.Contains(kv.Key)) { var v = kv.Value.Loud.Compute(); if (v != null) cl[kv.Key] = v; }
                var pl = new Dictionary<uint, float[]>();
                foreach (var c in _captures.Values)
                    if (c.Loud != null && c.Ready) { var v = c.Loud.Compute(); if (v != null) pl[c.Pid] = v; }
                _chLoud = cl;
                _pidLoud = pl;
            }
            if (nowMs >= _nextSpecAllMs)
            {
                _nextSpecAllMs = nowMs + 60;
                var cs = new Dictionary<string, float[]>();
                foreach (var kv in _states)
                    if (kv.Value.Spectrum != null && an.SpecCh.Contains(kv.Key)) cs[kv.Key] = kv.Value.Spectrum.Compute();
                var ps = new Dictionary<uint, float[]>();
                foreach (var c in _captures.Values)
                    if (c.Spec != null && c.Ready) ps[c.Pid] = c.Spec.Compute();
                _chSpec = cs;
                _pidSpec = ps;
            }
        }
    }
}
