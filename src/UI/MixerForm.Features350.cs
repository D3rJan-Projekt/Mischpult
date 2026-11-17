using System;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>3.5 – Schaltzentrale: statische Schalter, Takt und Haken fuer alle 3.5-Funktionen.</summary>
    internal sealed partial class MixerForm
    {
        private void ApplyFeatures350Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                foreach (var f in new[] { "PhaseWarnOn", "ClipLogOn", "LatencyOn", "DevFormatOn", "HearTimeOn", "ChanStatsOn", "NoSoundWarnOn", "TubesOn",
                                          "SceneSweepOn", "ShakeOn", "SpecBgOn", "DevIconOn", "TrebleCutOn", "DialogOn", "AdMuteOn", "BassMonoOn", "MirrorOn",
                                          "NoiseOn", "TypeValueOn", "MultiFaderOn", "CopyPasteOn", "MiddleMuteOn", "CheatSheetOn", "GamepadOn", "StartSceneOn",
                                          "SceneXfadeOwnOn", "VolCapOn", "FgHotkeysOn", "DigitKeysOn", "PinOn", "WheelInvert", "TemplatesOn", "ScenePreviewOn" })
                    if (DemoFlag(demo, f.ToUpperInvariant())) typeof(MixerSettings).GetField(f)?.SetValue(s, true);
                if (DemoFlag(demo, "NOISEPLAY")) { s.NoiseOn = true; s.NoisePlaying = true; s.NoiseKind = DemoNum(demo, "NOISEKIND", 1); }
            }
            StripBase.ClipLogOn = s.ClipLogOn;
            StripBase.MultiFaderOn = s.MultiFaderOn;
            StripBase.MiddleMuteOn = s.MiddleMuteOn;
            StripBase.TypeValueOn = s.TypeValueOn;
            StripBase.WheelInvert = s.WheelInvert;
            StripBase.ScenePreviewOn = s.ScenePreviewOn;
            StripBase.SweepOn = s.SceneSweepOn;
            ChannelStrip.LatencyOn = s.LatencyOn;
            ChannelStrip.StatsOn = s.ChanStatsOn;
            OutputStrip.PhaseWarnOn = s.PhaseWarnOn;
            OutputStrip.DevIconOn = s.DevIconOn;
            OutputStrip.DevFormatOn = s.DevFormatOn;
            MeterBridge.TubesOn = s.TubesOn;
            LoudnessSink.CorrOn = s.PhaseWarnOn;
            LoudnessSink.BassOn = s.ShakeOn;
            BassMonoGlobal = s.BassMonoOn;
            if (!s.MultiFaderOn) StripBase.ClearSelection();
            if (_padTimer != null || s.GamepadOn) ApplyGamepad350();
        }

        /// <summary>Einmal nach dem Anzeigen (aus ShownFeatures340).</summary>
        private void HookFeatures350()
        {
            _channelHost.IsPinned = IsPinnedStrip;
            _top.SceneKeyHover += i => { if (i >= 0 && i < _settings.Scenes.Count) PreviewScene(_settings.Scenes[i]); else ClearScenePreview(); };
            ApplyGamepad350();
            PushNoise();
            PushMirror();
            LoadStartScene();
            if (_demo)
            {
                if (DemoFlag(true, "SWEEP")) DemoLater(() => StripBase.StartSweep());
                if (DemoFlag(true, "PREVIEW") && _settings.Scenes.Count > 1) DemoLater(() => PreviewScene(_settings.Scenes[1]));
                if (DemoFlag(true, "CHEAT")) DemoLater(ShowCheatSheet);
                if (DemoFlag(true, "SELECT") && _channelStrips.Count > 2) DemoLater(() => { _channelStrips[1].ToggleSelected(); _channelStrips[2].ToggleSelected(); });
                if (DemoFlag(true, "CLIPMENU") && _channelStrips.Count > 1)
                    DemoLater(() =>
                    {
                        NoteClip(_channelStrips[1], 1.02f); NoteClip(_channelStrips[0], 0.99f);
                        ShowClipMenu(_channelStrips[1], _channelStrips[1].PointToScreen(new System.Drawing.Point(20, Theme.Px(560))));
                    });
            }
        }

        private double _last350 = -1, _nextSlow350;

        private void TickFeatures350(double now)
        {
            float dt = _last350 < 0 ? 0f : (float)Math.Min(0.5, now - _last350);
            _last350 = now;
            _xfOverride = -1f;   // gilt nur waehrend ApplyScene
            TickTon350(now);
            TickPhase(now);
            TickTubes();
            TickSweep(now);
            TickShake(now, dt);
            TickSpecBg(now);
            TickVolCap(now);
            TickHearTime(dt);
            if (now >= _nextSlow350)
            {
                _nextSlow350 = now + 0.5;
                UpdateLatency350();
                TickNoSound(now);
            }
        }
    }
}
