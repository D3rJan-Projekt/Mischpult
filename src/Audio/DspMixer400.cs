using System;

namespace Mischpult.Audio
{
    /// <summary>4.0: EQ und Kompressor der Gruppe – je Kanal, weich zu- und abgeschaltet.</summary>
    internal sealed partial class DspMixer
    {
        private void GroupStage(ChannelState st, GroupFx g, int n)
        {
            bool eq = g != null && !g.EqFlat, comp = g != null && g.Comp;
            if (!eq && st.GEqMix <= 0f && !comp && st.GCompMix <= 0f) { st.GEqRun = false; st.GCompRun = false; st.GParams = null; return; }
            if (eq && (!st.GEqRun || st.GParams == null || st.GParams.Low != g.Low || st.GParams.Mid != g.Mid || st.GParams.High != g.High))
            {
                if (!st.GEqRun) { st.GLow.Reset(); st.GMid.Reset(); st.GHigh.Reset(); }
                st.GLow.LowShelf(Rate, FreqLow, g.Low);
                st.GMid.Peaking(Rate, FreqMid, g.Mid, 0.7);
                st.GHigh.HighShelf(Rate, FreqHigh, g.High);
                st.GParams = g;
            }
            if (eq || st.GEqMix > 0f)
            {
                st.GEqRun = true;
                Array.Copy(_chan, _dryBuf, n * 2);
                st.GLow.Process(_chan, n); st.GMid.Process(_chan, n); st.GHigh.Process(_chan, n);
                st.GEqMix = MixToward(_chan, _dryBuf, n, st.GEqMix, eq ? 1f : 0f, 1f / SwitchFadeFrames);
                if (!eq && st.GEqMix <= 0f) st.GEqRun = false;
            }
            if (comp || st.GCompMix > 0f)
            {
                if (!st.GCompRun) { st.GComp.Prime(_chan, n); st.GCompRun = true; }
                Array.Copy(_chan, _dryBuf, n * 2);
                st.GComp.Process(_chan, n);
                st.GCompMix = MixToward(_chan, _dryBuf, n, st.GCompMix, comp ? 1f : 0f, 1f / CompFadeFrames);
                if (!comp && st.GCompMix <= 0f) st.GCompRun = false;
            }
        }
    }
}
