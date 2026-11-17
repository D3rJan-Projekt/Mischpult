using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>3.4.6 – Pegel, Optik, Bedienung. Alles einzeln an/aus (Standard aus).</summary>
    internal sealed partial class MixerForm
    {
        private static bool DemoFlag(bool demo, string name) => demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_" + name) == "1";
        private static int DemoNum(bool demo, string name, int def) => demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_" + name), out int v) ? v : def;

        /// <summary>Beim Start und nach jeder Aenderung: statische Schalter setzen.</summary>
        private void ApplyFeatures346Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                if (DemoFlag(demo, "TRUEPEAK")) s.TruePeakOn = true;
                s.KScale = DemoNum(demo, "KSCALE", s.KScale);
                if (DemoFlag(demo, "DYNAMICS")) s.DynamicsOn = true;
                s.LufsTarget = DemoNum(demo, "LUFSTARGET", s.LufsTarget);
                if (DemoFlag(demo, "BALANCE")) s.BalanceOn = true;
                if (DemoFlag(demo, "HEADROOM")) s.HeadroomOn = true;
                if (DemoFlag(demo, "LUPE")) s.MeterLupeOn = true;
            }
            ApplyMeterStatics346(s);
            ApplyLookStatics346(s, demo);
            ApplyControlStatics346(s, demo);
        }

        private void ApplyMeterStatics346(MixerSettings s)
        {
            OutputStrip.TruePeakOn = s.TruePeakOn;
            OutputStrip.DynamicsOn = s.DynamicsOn;
            OutputStrip.HeadroomOn = s.HeadroomOn;
            OutputStrip.KScaleMode = s.KScale;
            OutputStrip.LufsTargetDb = s.LufsTargetDb;
            LoudnessSink.TruePeakOn = s.TruePeakOn || s.DynamicsOn;
            StripBase.BalanceOn = s.BalanceOn;
            StripBase.MeterLupeOn = s.MeterLupeOn;
        }

        /// <summary>True-Peak oder Dynamik brauchen die Lautheits-Messung des Standardgeraets.</summary>
        private bool MeasureNeedsLoudness => _settings.TruePeakOn || _settings.DynamicsOn;

        private void SetMeter346(Action change)
        {
            change();
            ApplyMeterStatics346(_settings);
            _master?.InvalidateChrome();
            foreach (var os in _outputStrips.Values) os.InvalidateChrome();
            foreach (var s in _channelStrips) s.InvalidateChrome();
            if (_master != null) _master.SetMasterPeaks(_snap?.MasterPeaks);
            ScheduleSave();
        }

        // ---------------------------------------------------------------- 109 Pegel-Lupe
        private MeterLupe _lupe;

        public void ShowMeterLupe(StripBase s)
        {
            if (s == null) return;
            if (_lupe == null || _lupe.IsDisposed) _lupe = new MeterLupe();
            _lupe.ShowFor(s, Cursor.Position);
        }

        private void TickLupe(float dt, double now)
        {
            if (_lupe != null && !_lupe.IsDisposed && _lupe.Visible) _lupe.Tick(dt, now);
        }

        private void AddMeters346(SPage p)
        {
            p.Section("Messen wie im Studio");
            p.Toggle("True-Peak am MASTER", "Statt des LCDs mit den Apps zeigt der MASTER den True-Peak der letzten 3 s (dBTP): die Spitzen zwischen den Samples, die ein normaler Pegel übersieht (4-fach überabgetastet nach ITU BS.1770). Rot ab −1 dBTP.",
                () => _settings.TruePeakOn, v => SetMeter346(() => _settings.TruePeakOn = v));
            p.Choice("K-Skala an den Ausgängen", () => _settings.KScale == 0 ? "Aus (Standard): normale dB-Skala." : "Pegel von MASTER und Ausgängen nach " + (_settings.KScale == 1 ? "K-14: 0 bei −14 dBFS" : "K-20: 0 bei −20 dBFS") + " – grün bis 0, gelb bis +4, darüber rot (wie im Studio).",
                new[] { "Aus", "K-14", "K-20" }, () => _settings.KScale, i => SetMeter346(() => _settings.KScale = i));
            p.Toggle("Dynamik-Anzeige", "Zahl „DYN“ im MASTER: wie weit die Spitzen über der Lautheit liegen (in dB). Groß = lebendig, klein = stark zusammengedrückt (z. B. mit COMP).",
                () => _settings.DynamicsOn, v => SetMeter346(() => _settings.DynamicsOn = v));
            p.Choice("Lautheits-Ziel", () => _settings.LufsTarget == 0 ? "Aus (Standard)." : "Die LUFS-Anzeige wird grün, wenn die Lautheit im Ziel ist (±1), gelb, wenn es lauter ist. Ein Strich darunter zeigt, wie weit sie davon weg ist.",
                MixerSettings.LufsTargetNames, () => _settings.LufsTarget, i => SetMeter346(() => _settings.LufsTarget = i));
            p.Toggle("Balance-Anzeige", "Ein kleiner Punkt unter jedem Pegel zeigt, ob der Ton eher links oder rechts liegt (gelb bei mehr als 6 dB Unterschied).",
                () => _settings.BalanceOn, v => SetMeter346(() => _settings.BalanceOn = v));
            p.Toggle("Reserve bis zum Übersteuern", "Die Ausgänge zeigen „LUFT“: so viele dB fehlen der lautesten Stelle der letzten 3 s bis 0 dBFS. Rot unter 1 dB.",
                () => _settings.HeadroomOn, v => SetMeter346(() => _settings.HeadroomOn = v));
            p.Toggle("Pegel-Lupe", "Doppelklick auf einen Pegel öffnet ihn groß in einem kleinen Fenster: feine Skala von −30 bis 0 dB, die lauteste Stelle der letzten 3 s als Zahl.",
                () => _settings.MeterLupeOn, v => SetMeter346(() => _settings.MeterLupeOn = v));
        }

        private void TickFeatures346b(double now)
        {
            float dt = (float)Math.Max(0, Math.Min(0.2, now - _last346));
            _last346 = now;
            TickLupe(dt, now);
            TickLook346(now, dt);
            TickControl346(now, dt);
        }
        private double _last346;
    }
}
