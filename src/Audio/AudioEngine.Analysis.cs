using System;
using System.Collections.Generic;
using System.Linq;

namespace Mischpult.Audio
{
    /// <summary>2.17: Lautheit und Spektrum pro Kanal fuer das Anzeigefeld (Wunsch vom UI, Messung im Mischer).</summary>
    internal sealed partial class AudioEngine
    {
        private volatile string[] _anLoud, _anSpec;
        private string _anKey = "";
        private Dictionary<uint, float[]> _anPidLoudSrc, _anPidSpecSrc;
        private Dictionary<string, float[]> _anAppLoud = new Dictionary<string, float[]>(), _anAppSpec = new Dictionary<string, float[]>();

        /// <summary>Kanaele, deren Lautheit bzw. Spektrum gemessen werden soll (null = keine).</summary>
        public void SetChannelAnalysis(string[] loudChannels, string[] specChannels)
        {
            _anLoud = loudChannels;
            _anSpec = specChannels;
        }

        /// <summary>Engine-Thread (mit der Pegel-Messung, alle 250 ms): Wunsch in Prozesse uebersetzen.</summary>
        private void UpdateAnalysis()
        {
            if (_dsp == null) return;
            var lc = new HashSet<string>(_anLoud ?? new string[0], StringComparer.Ordinal);
            var sc = new HashSet<string>(_anSpec ?? new string[0], StringComparer.Ordinal);
            var lp = new HashSet<uint>();
            var sp = new HashSet<uint>();
            if (lc.Count + sc.Count > 0)
                foreach (var s in _sessions.Values)
                {
                    if (s.Pid == 0 || s.Dsp || s.Applied?.ChannelId == null || !_lastMeterPids.Contains(s.Pid)) continue;
                    if (lc.Contains(s.Applied.ChannelId)) lp.Add(s.Pid);
                    if (sc.Contains(s.Applied.ChannelId)) sp.Add(s.Pid);
                }
            string key = string.Join(",", lc.OrderBy(x => x)) + "|" + string.Join(",", sc.OrderBy(x => x)) + "|" +
                         string.Join(",", lp.OrderBy(x => x)) + "|" + string.Join(",", sp.OrderBy(x => x));
            if (key == _anKey) return;
            _anKey = key;
            _dsp.SetAnalysis(lc, sc, lp, sp);
        }

        /// <summary>Zwei Lautheiten zusammen (Leistung addiert) – fuer mehrere Prozesse einer App bzw. mehrere Apps eines Kanals.</summary>
        internal static float[] AddLoudness(float[] a, float[] b)
        {
            if (a == null) return b;
            if (b == null) return a;
            var r = new float[Math.Min(a.Length, b.Length)];
            for (int i = 0; i < r.Length; i++)
            {
                double pa = float.IsNaN(a[i]) || float.IsNegativeInfinity(a[i]) ? 0 : Math.Pow(10, a[i] / 10.0);
                double pb = float.IsNaN(b[i]) || float.IsNegativeInfinity(b[i]) ? 0 : Math.Pow(10, b[i] / 10.0);
                double p = pa + pb;
                r[i] = p > 0 ? (float)(10 * Math.Log10(p)) : float.NegativeInfinity;
            }
            return r;
        }

        /// <summary>Zwei Spektren zusammen (je Band der lautere).</summary>
        internal static float[] MaxSpectrum(float[] a, float[] b)
        {
            if (a == null) return b;
            if (b == null) return a;
            var r = (float[])a.Clone();
            for (int i = 0; i < r.Length && i < b.Length; i++) if (b[i] > r[i]) r[i] = b[i];
            return r;
        }

        /// <summary>Lautheit/Spektrum je App (aus den Prozessen) – nur neu, wenn der Mischer Neues hat.</summary>
        private void CombineAppAnalysis()
        {
            var dsp = _dsp;
            if (dsp == null) return;
            var pl = dsp.PidLoudness;
            if (!ReferenceEquals(pl, _anPidLoudSrc))
            {
                _anPidLoudSrc = pl;
                var d = new Dictionary<string, float[]>(StringComparer.Ordinal);
                if (pl.Count > 0)
                    foreach (var s in _sessions.Values)
                        if (pl.TryGetValue(s.Pid, out var v)) d[s.AppKey] = d.TryGetValue(s.AppKey, out var cur) ? AddLoudness(cur, v) : v;
                _anAppLoud = d;
            }
            var ps = dsp.PidSpectra;
            if (!ReferenceEquals(ps, _anPidSpecSrc))
            {
                _anPidSpecSrc = ps;
                var d = new Dictionary<string, float[]>(StringComparer.Ordinal);
                if (ps.Count > 0)
                    foreach (var s in _sessions.Values)
                        if (ps.TryGetValue(s.Pid, out var v)) d[s.AppKey] = d.TryGetValue(s.AppKey, out var cur) ? MaxSpectrum(cur, v) : v;
                _anAppSpec = d;
            }
        }
    }
}
