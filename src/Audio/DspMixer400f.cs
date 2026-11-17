using System;

namespace Mischpult.Audio
{
    /// <summary>4.0 (finale): Kanal-Stufen einzeln (fuer die Effekt-Reihenfolge), Hall-Bus, Signalgenerator.</summary>
    internal sealed partial class DspMixer
    {
        private TruePeakLimiter _tpLimiter;

        /// <summary>EQ-Stufe: Bass-/Hoehen-Filter, 3-Band, 10-Band, parametrisch, Brumm-Entferner, dynamische Baender.</summary>
        private void StageEq(ChannelState st, ChannelDsp p, int n)
        {
            bool eqFade = st.EqFade > 0 && st.OldLow != null;
            if (eqFade)
            {
                Array.Copy(_chan, _oldBuf, n * 2);
                if (st.OldHpOn && st.OldHp != null) st.OldHp.Process(_oldBuf, n);
                if (st.OldLpOn && st.OldLp != null) st.OldLp.Process(_oldBuf, n);
                st.OldLow.Process(_oldBuf, n); st.OldMid.Process(_oldBuf, n); st.OldHigh.Process(_oldBuf, n);
                for (int b = 0; b < st.OldGeq.Length; b++) if (st.OldGeqOn[b]) st.OldGeq[b].Process(_oldBuf, n);
            }
            if ((p.Extra?.HpHz ?? 0) > 0) st.Hp.Process(_chan, n);   // 3.4.6: Bass-Filter
            if ((p.Extra?.LpHz ?? 0) > 0) st.Lp.Process(_chan, n);   // 3.5: Hoehen-Filter
            st.Low.Process(_chan, n);
            st.Mid.Process(_chan, n);
            st.High.Process(_chan, n);
            if (p.Graphic != null)
                for (int b = 0; b < st.Geq.Length; b++) if (Math.Abs(p.Graphic[b]) > 0.01f) st.Geq[b].Process(_chan, n);
            if (eqFade)
            {
                // 3.4.1: vom alten zum neuen EQ gleiten
                int left = st.EqFade;
                for (int i = 0, k = 0; i < n; i++, k += 2)
                {
                    float m = left - i <= 0 ? 1f : 1f - (left - i) / (float)SwitchFadeFrames;
                    _chan[k] = _oldBuf[k] + (_chan[k] - _oldBuf[k]) * m;
                    _chan[k + 1] = _oldBuf[k + 1] + (_chan[k + 1] - _oldBuf[k + 1]) * m;
                }
                st.EqFade = Math.Max(0, left - n);
                if (st.EqFade == 0) { st.OldLow = st.OldMid = st.OldHigh = st.OldHp = st.OldLp = null; }
            }
            else st.EqFade = 0;
            if (p.Extra?.Peq != null || st.Peq.Active) st.Peq.Process(_chan, n, p.Extra?.Peq);   // 4.0: parametrischer EQ
            st.Pro?.Eq(_chan, n, p.Extra?.Pro);
        }

        /// <summary>Dynamik-Stufe: AUTO, Kompressor (auf Wunsch mit eigenen Werten), De-Esser, Punch.</summary>
        private void StageDyn(ChannelState st, ChannelDsp p, int n)
        {
            var cs = p.Extra?.Pro?.Comp;
            if (!CompSpec.Same(cs, st.CompSpec)) st.Comp.Configure(cs);
            st.CompSpec = cs;
            if (!CleanStart.On)
            {
                if (p.Leveler) st.Lev.Process(_chan, n);
                if (p.Comp) st.Comp.Process(_chan, n);
                st.CompRun = p.Comp; st.CompMix = p.Comp ? 1f : 0f; st.LevRun = p.Leveler; st.LevMix = p.Leveler ? 1f : 0f;
            }
            else
            {
                // 3.4.1: AUTO und COMP weich zu- und abschalten (vorher: harter Sprung bis +8 dB bzw. bis ±12 dB -> Knacken)
                if (p.Leveler || st.LevMix > 0f)
                {
                    if (!st.LevRun) { st.Lev.Reset(); st.LevRun = true; }
                    Array.Copy(_chan, _dryBuf, n * 2);
                    st.Lev.Process(_chan, n);
                    st.LevMix = MixToward(_chan, _dryBuf, n, st.LevMix, p.Leveler ? 1f : 0f, 1f / SwitchFadeFrames);
                    if (!p.Leveler && st.LevMix <= 0f) st.LevRun = false;
                }
                if (p.Comp || st.CompMix > 0f)
                {
                    if (!st.CompRun) { st.Comp.Prime(_chan, n); st.CompRun = true; }
                    Array.Copy(_chan, _dryBuf, n * 2);
                    st.Comp.Process(_chan, n);
                    st.CompMix = MixToward(_chan, _dryBuf, n, st.CompMix, p.Comp ? 1f : 0f, 1f / CompFadeFrames);
                    if (!p.Comp && st.CompMix <= 0f) st.CompRun = false;
                }
            }
            st.Pro?.Dyn(_chan, n, p.Extra?.Pro);
        }

        /// <summary>Gruppe, Stereo-Breite, Stimmen nach vorne, Knall-Bremse.</summary>
        private void StageAfter(ChannelState st, ChannelDsp p, int n)
        {
            GroupStage(st, p.Extra?.Group, n);   // 4.0: EQ/Kompressor der Gruppe
            // 3.4.6: Stereo-Breite (Mitte/Seite), gleitet in 30 ms zum neuen Wert
            {
                float wT = p.Extra?.Width ?? 1f, w = st.WidthCur;
                if (w != wT || wT != 1f)
                {
                    float step = 1f / SwitchFadeFrames;
                    for (int k = 0; k < n * 2; k += 2)
                    {
                        if (w < wT) { w += step; if (w > wT) w = wT; } else if (w > wT) { w -= step; if (w < wT) w = wT; }
                        float m = (_chan[k] + _chan[k + 1]) * 0.5f, sd = (_chan[k] - _chan[k + 1]) * 0.5f * w;
                        _chan[k] = m + sd; _chan[k + 1] = m - sd;
                    }
                    st.WidthCur = w;
                }
            }
            // 3.5: Stimmen nach vorne – weich zu- und abschalten
            {
                bool dl = p.Extra?.Dialog ?? false;
                if (dl || st.DialogMix > 0f)
                {
                    if (!st.DialogRun) { st.Dialog.Reset(); st.DialogRun = true; }
                    Array.Copy(_chan, _dryBuf, n * 2);
                    st.Dialog.Process(_chan, n);
                    st.DialogMix = MixToward(_chan, _dryBuf, n, st.DialogMix, dl ? 1f : 0f, 1f / SwitchFadeFrames);
                    if (!dl && st.DialogMix <= 0f) st.DialogRun = false;
                }
            }
            // 3.4.6: Knall-Bremse – weich zu- und abschalten
            {
                bool pg = p.Extra?.PopGuard ?? false;
                if (pg || st.PopMix > 0f)
                {
                    if (!st.PopRun) { st.Pop.Reset(); st.PopRun = true; }
                    Array.Copy(_chan, _dryBuf, n * 2);
                    st.Pop.Process(_chan, n);
                    st.PopMix = MixToward(_chan, _dryBuf, n, st.PopMix, pg ? 1f : 0f, 1f / SwitchFadeFrames);
                    if (!pg && st.PopMix <= 0f) st.PopRun = false;
                }
            }
        }

        // ------------------------------------------------------------ Hall-Bus

        private readonly System.Collections.Generic.Dictionary<string, HallBus> _hallBuses = new System.Collections.Generic.Dictionary<string, HallBus>();

        /// <summary>Hall-Bus des Ausgangs fuer diesen Block vorbereiten (null = keiner da).</summary>
        private HallBus HallStart(string key, int n)
        {
            if (!_hallBuses.TryGetValue(key, out var hb)) return null;
            if (hb.Buf.Length < n * 2) hb.Buf = new float[n * 2];
            Array.Clear(hb.Buf, 0, n * 2);
            hb.Fed = false;
            return hb;
        }

        private HallBus HallFor(string key, int n)
        {
            if (!_hallBuses.TryGetValue(key, out var hb)) { hb = new HallBus(Rate); _hallBuses[key] = hb; }
            if (hb.Buf.Length < n * 2) hb.Buf = new float[n * 2];
            Array.Clear(hb.Buf, 0, n * 2);
            return hb;
        }

        private void MixHall(string key, HallBus hb, HallSpec spec, int n)
        {
            if (hb.Fed) hb.Quiet = 0;
            else
            {
                hb.Quiet += n;
                if (hb.Quiet > Rate * 7) { _hallBuses.Remove(key); return; }   // Nachhall ist ausgeklungen
            }
            int preset = spec?.Preset ?? Math.Max(0, hb.Preset);
            if (preset != hb.Preset) { hb.Rev.SetPreset(preset); hb.Preset = preset; }
            if (spec != null) hb.Return = spec.Return;
            hb.Rev.ProcessWet(hb.Buf, n, 1f);
            float ret = hb.Return, pk = 0f;
            for (int k = 0; k < n * 2; k++)
            {
                float v = hb.Buf[k] * ret;
                _mix[k] += v;
                if (v < 0) v = -v; if (v > pk) pk = v;
            }
            Pro400.NoteHall(pk);
        }

        // ------------------------------------------------------------ Signalgenerator

        private GenSpec _genLast;
        private float _genLevel;
        private double _genPhase, _genT;
        private uint _genRnd = 0x9E3779B9u;
        private float _pb0, _pb1, _pb2, _pb3, _pb4, _pb5, _pb6;

        private float GenWhite()
        {
            uint x = _genRnd; x ^= x << 13; x ^= x >> 17; x ^= x << 5; _genRnd = x;
            return (x / 4294967296f) * 2f - 1f;
        }

        private void MixGen(string key, int n)
        {
            var g = Pro400.Gen;
            bool on = g != null && g.DeviceKey == key;
            if (!on && !(_genLast != null && _genLast.DeviceKey == key && _genLevel > 0f)) return;
            var use = on ? g : _genLast;
            if (on && (_genLast == null || _genLast.Kind != g.Kind || _genLevel <= 0f)) { _genT = 0; if (_genLevel <= 0f) _genPhase = 0; }
            _genLast = use;
            float target = on ? use.Gain : 0f;
            float c = 1f - (float)Math.Exp(-1.0 / (Rate * 0.015));
            float lvl = _genLevel;
            const double Sweep = 10.0;
            float stopStep = use.Gain / (Rate * 0.02f);   // Stopp: in 20 ms aus
            for (int i = 0, k = 0; i < n; i++, k += 2)
            {
                if (on) lvl += (target - lvl) * c;
                else { lvl -= stopStep; if (lvl < 0f) lvl = 0f; }
                float s;
                switch (use.Kind)
                {
                    case 1:
                        {
                            double f = 20.0 * Math.Pow(1000.0, _genT / Sweep);
                            _genPhase += 2 * Math.PI * f / Rate;
                            _genT += 1.0 / Rate; if (_genT >= Sweep) _genT = 0;
                            s = (float)Math.Sin(_genPhase);
                            break;
                        }
                    case 2:
                        {
                            float w = GenWhite();   // rosa nach P. Kellet
                            _pb0 = 0.99886f * _pb0 + w * 0.0555179f; _pb1 = 0.99332f * _pb1 + w * 0.0750759f; _pb2 = 0.96900f * _pb2 + w * 0.1538520f;
                            _pb3 = 0.86650f * _pb3 + w * 0.3104856f; _pb4 = 0.55000f * _pb4 + w * 0.5329522f; _pb5 = -0.7616f * _pb5 - w * 0.0168980f;
                            s = (_pb0 + _pb1 + _pb2 + _pb3 + _pb4 + _pb5 + _pb6 + w * 0.5362f) * 0.11f;
                            _pb6 = w * 0.115926f;
                            break;
                        }
                    case 3: s = GenWhite() * 0.6f; break;
                    default:
                        _genPhase += 2 * Math.PI * use.Hz / Rate;
                        s = (float)Math.Sin(_genPhase);
                        break;
                }
                if (_genPhase > 2 * Math.PI) _genPhase -= 2 * Math.PI;
                s *= lvl;
                if (use.Side != 2) _mix[k] += s;
                if (use.Side != 1) _mix[k + 1] += s;
            }
            _genLevel = lvl;
            if (_genLevel == 0f && !on) _genLast = null;
        }
    }
}
