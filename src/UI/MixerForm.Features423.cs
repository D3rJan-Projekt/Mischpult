using System;
using System.Collections.Generic;
using System.Linq;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.2.3 – Ducking-Feinschliff: Laempchen bei den Ausloesern, kurze Geraeusche ignorieren, Uebergang einstellbar,
    /// Mikro in den Ducking-Regeln. Alles einzeln schaltbar, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Wann hat welcher Ausloeser zuletzt wirklich ausgeloest (Uhr des Pults).</summary>
        private readonly Dictionary<string, double> _duckFired423 = new Dictionary<string, double>();
        private readonly Dictionary<string, float> _talk423 = new Dictionary<string, float>();
        private float[] _ruleTalk423 = new float[0];

        // ================================================================ Kurze Geraeusche ignorieren

        /// <summary>
        /// Zaehlt, wie lange schon Ton da ist. Ton fuellt auf, Stille leert halb so schnell – Sprechen (mit kleinen Pausen)
        /// kommt so drueber, einzelne Klicks und Tippen nicht.
        /// </summary>
        private bool TalkGate423(ref float acc, bool raw, float dt)
        {
            if (!_settings.DuckMinTalkOn) { acc = 0f; return raw; }
            float need = Math.Max(0.05f, _settings.DuckMinTalkMs / 1000f);
            acc = raw ? acc + dt : acc - dt * 0.5f;
            acc = Math.Max(0f, Math.Min(need, acc));
            return raw && acc >= need - 0.0001f;
        }

        private bool TalkGate423(string trig, bool raw, float dt)
        {
            if (!_settings.DuckMinTalkOn) { if (_talk423.Count > 0) _talk423.Clear(); return raw; }
            _talk423.TryGetValue(trig ?? "", out float acc);
            bool v = TalkGate423(ref acc, raw, dt);
            _talk423[trig ?? ""] = acc;
            return v;
        }

        // ================================================================ Uebergang

        /// <summary>Wie schnell (dB pro Sekunde) es leiser und wieder lauter wird. Aus = wie bisher.</summary>
        private void DuckRates423(float amountDb, out float down, out float up)
        {
            down = 120f; up = 25f;
            if (!_settings.DuckFadeOn) return;
            float a = Math.Max(1f, Math.Abs(amountDb));
            down = _settings.DuckDownMs <= 1 ? 100000f : a / (_settings.DuckDownMs / 1000f);
            up = a / (Math.Max(100, _settings.DuckUpMs) / 1000f);
        }

        // ================================================================ Laempchen

        /// <summary>
        /// Laempchen fuer das Ducking-Fenster: 2 = loest gerade aus, 1 = Ton ueber der Schwelle, zaehlt aber nicht
        /// (PC-Ton, zu kurz oder noch nicht uebernommen), 0 = still.
        /// </summary>
        private int DuckLamp423(string id, float thresholdDb)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            double now = _clock.Elapsed.TotalSeconds;
            if (_duckFired423.TryGetValue(id, out double t) && now - t < 0.2) return 2;
            return DuckLevelDb(id) > thresholdDb ? 1 : 0;
        }

        // ================================================================ Einstellungen

        /// <summary>Seite Automatik, Abschnitt Ducking.</summary>
        private void AddDuck423(SPage p)
        {
            p.Toggle("Lämpchen bei den Auslösern", "Im Ducking-Fenster leuchtet neben jedem Auslöser ein Punkt: hell, wenn er gerade auslöst – schwach, wenn Ton da ist, der nicht zählt (PC-Ton, zu kurz oder noch nicht mit OK übernommen).",
                () => _settings.DuckLampsOn, v => { _settings.DuckLampsOn = v; ScheduleSave(); });
            p.Toggle("Kurze Geräusche ignorieren", "Ein Auslöser zählt erst, wenn eine Weile am Stück Ton da ist – Tippen, Mausklicks und Pieptöne machen die Musik nicht leiser. Gilt auch für die Ducking-Regeln.",
                () => _settings.DuckMinTalkOn, v => { _settings.DuckMinTalkOn = v; ScheduleSave(); });
            p.Choice("Ton am Stück mindestens", () => "Kürzere Geräusche lösen nicht aus.", MixerSettings.DuckMinTalkValues.Select(MixerSettings.MsText).ToArray(),
                () => MixerSettings.NearestIndex(MixerSettings.DuckMinTalkValues, _settings.DuckMinTalkMs),
                i => { _settings.DuckMinTalkMs = MixerSettings.DuckMinTalkValues[i]; ScheduleSave(); }).Visible = () => _settings.DuckMinTalkOn;
            p.Toggle("Übergang selbst einstellen", "Wie schnell die Musik leiser wird und wie sanft sie wieder lauter wird. Aus = wie bisher (schnell leiser, in etwa 0,6 s wieder da). Gilt auch für die Ducking-Regeln.",
                () => _settings.DuckFadeOn, v => { _settings.DuckFadeOn = v; ScheduleSave(); });
            p.Choice("Leiser werden dauert", () => "", MixerSettings.DuckDownValues.Select(MixerSettings.MsText).ToArray(),
                () => MixerSettings.NearestIndex(MixerSettings.DuckDownValues, _settings.DuckDownMs),
                i => { _settings.DuckDownMs = MixerSettings.DuckDownValues[i]; ScheduleSave(); }).Visible = () => _settings.DuckFadeOn;
            p.Choice("Lauter werden dauert", () => "", MixerSettings.DuckUpValues.Select(MixerSettings.MsText).ToArray(),
                () => MixerSettings.NearestIndex(MixerSettings.DuckUpValues, _settings.DuckUpMs),
                i => { _settings.DuckUpMs = MixerSettings.DuckUpValues[i]; ScheduleSave(); }).Visible = () => _settings.DuckFadeOn;
        }

        // ================================================================ Vorschau

        private void DemoShown423()
        {
            if (DemoOn("MICRULE") && _settings.Channels.Count > 4)   // Regel: MIKRO -> BROWSER -6 dB
            {
                _settings.DuckRulesOn = true;
                _settings.DuckRules = new List<DuckRule> { new DuckRule { Trigger = MixerSettings.DuckMicId, Targets = new List<string> { _settings.Channels[4].Id }, Db = -6 } };
                _settings.Normalize();
                RulesChanged400();
            }
            if (DemoOn("RULESMENU")) DemoLater(() => ShowDuckRules400(new System.Drawing.Rectangle(DemoXY("RULESXY", new System.Drawing.Point(600, 200)), new System.Drawing.Size(10, 10))));
        }
    }
}
