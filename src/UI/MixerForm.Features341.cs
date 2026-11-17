using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>3.4.1: Ton-Protokoll zeigen; weniger Rechenzeit (Bildrate, Hintergrund).</summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 3.4.2 Pegel beim Umschalten direkt <-> EQ-Weg

        private readonly HashSet<ChannelConfig> _eqPathNow = new HashSet<ChannelConfig>(), _directNow = new HashSet<ChannelConfig>();
        private readonly Dictionary<string, int> _pathWas = new Dictionary<string, int>();   // 0 spielt nicht, 1 direkt, 2 EQ-Weg
        private readonly Dictionary<string, double> _pathHoldUntil = new Dictionary<string, double>();
        private readonly Dictionary<string, float[]> _pathHold = new Dictionary<string, float[]>();
        internal const double PathHoldSec = 0.35;

        /// <summary>
        /// Wechselt ein Kanal den Weg (EQ/COMP/AUTO an bzw. alles aus), kommt sein Pegel ab sofort aus der anderen Quelle – und die
        /// liefert erst nach einigen Hundertstelsekunden. Der Ton laeuft weich weiter, die Anzeige fiel aber kurz weg und sprang zurueck.
        /// Jetzt bleibt der letzte Pegel so lange stehen (hoechstens 0,35 s), bis die neue Quelle etwas liefert.
        /// </summary>
        private void HoldAcrossPathSwitch(ChannelConfig c, double now, ref float l, ref float r, ref float pl, ref float pr)
        {
            int path = _eqPathNow.Contains(c) ? 2 : _directNow.Contains(c) ? 1 : 0;
            bool switched = _pathWas.TryGetValue(c.Id, out int was) && was != 0 && path != 0 && was != path;   // nur Wegwechsel, nicht Pause
            _pathWas[c.Id] = path;
            if (!CleanStart.On) return;
            if (switched && _channelPeak.TryGetValue(c.Id, out var last) && last != null && Math.Max(last[0], last[1]) > 0.001f)
            {
                _pathHoldUntil[c.Id] = now + PathHoldSec;
                _pathHold[c.Id] = (float[])last.Clone();
            }
            if (!_pathHoldUntil.TryGetValue(c.Id, out double until)) return;
            var h = _pathHold[c.Id];
            if (now >= until || Math.Max(l, r) >= Math.Max(h[0], h[1]) * 0.25f)
            {
                _pathHoldUntil.Remove(c.Id);   // neue Quelle liefert (oder Zeit um)
                _pathHold.Remove(c.Id);
                return;
            }
            l = Math.Max(l, h[0]); r = Math.Max(r, h[1]); pl = Math.Max(pl, h[2]); pr = Math.Max(pr, h[3]);
        }

        // ================================================================ Im Hintergrund sparen

        private bool _bgSaving;
        private static bool Vis(Control c) => c != null && !c.IsDisposed && c.Visible;

        /// <summary>Zeigt gerade irgendetwas Pegel an? (Pult, Meterbridge-/VU-Fenster, Mini-Ansicht, Schnell-Regler …)</summary>
        private bool LevelsShown =>
            PultShown || BridgeWinShown || VuWindowOpen || Vis(_mini) || Vis(_quick) || Vis(_appMix) || Vis(_saverWin) ||
            Vis(_eqWin) || Vis(_sbForm) || Vis(_osd) || Vis(_settingsWin);

        /// <summary>3.4.1: Pult versteckt/minimiert und nichts zeigt Pegel: weniger Bilder, Pegel seltener messen (Ton laeuft normal).</summary>
        private bool BackgroundSaving => !_settings.BackgroundFullOff && !_demo && !LevelsShown;

        /// <summary>Aus dem Takt: Hintergrund-Sparen an/aus, sobald sich etwas aendert (jedes Bild, kostet fast nichts).</summary>
        private void TickBackground()
        {
            bool bg = BackgroundSaving;
            if (bg == _bgSaving) return;
            _bgSaving = bg;
            bool precise = !_settings.PacerOff && Native.Win32.IsWindows && !_demo;
            Backend.SetPerf(precise && !bg, !_settings.PerfPriorityOff);
            Backend.SetRealtime(!_settings.MeterRealtimeOff && !bg);
            UpdateFps();
        }

        private void AddPerf341(SPage p)
        {
            p.Toggle("Hohe Bildrate bei „Passend zum Bildschirm“", "Bis 165 Bilder pro Sekunde wie bis 3.4, statt höchstens 75. Auf schnellen Bildschirmen noch flüssiger, braucht aber etwa doppelt so viel Rechenzeit. Standard aus.",
                () => _settings.FpsHighAuto, v => { _settings.FpsHighAuto = v; UpdateFps(); ScheduleSave(); }).Enabled = () => _settings.PerfFps == 0;
            p.Toggle("Im Hintergrund sparen", "Ist das Pult versteckt oder minimiert und zeigt kein anderes Fenster Pegel an, rechnet es nur 30-mal pro Sekunde und misst die Pegel seltener. Ton, Tastenkürzel, Push-to-Talk und Automatik laufen normal weiter. Aus = wie bis 3.4.",
                () => !_settings.BackgroundFullOff, v => { _settings.BackgroundFullOff = !v; TickBackground(); ScheduleSave(); });
        }

        /// <summary>3.4.1: Kanal immer ueber den EQ-Weg – dann wechselt beim Ein-/Ausschalten von EQ, COMP oder AUTO der Weg nicht (kein kurzer Aussetzer).</summary>
        private void SetAlwaysDsp(ChannelConfig c, bool on)
        {
            c.AlwaysDsp = on;
            PushPolicy();
            ScheduleSave();
            ShowOsd(c.Name, on ? "Immer über den EQ-Weg – EQ/COMP ohne Aussetzer (Ton ca. 30–50 ms später)" : "Ohne EQ/COMP wieder direkt", -1, Color.FromArgb(c.Color));
        }

        // ================================================================ 3.4.5 Kanaele breiter ziehen

        public bool DragWidthEnabled => !_settings.DragWidthOff;
        /// <summary>Gezogene Breite des Kanals (logische Pixel), 0 = keine bzw. Schalter aus.</summary>
        public int DragWidthOf(ChannelConfig c) => !_settings.DragWidthOff && c != null && c.WidthPx > 0 ? c.WidthPx : 0;

        private void SetDragWidthOn(bool on) { _settings.DragWidthOff = !on; RelayoutWidths(); ScheduleSave(); }

        /// <summary>Breite setzen (0 = zuruecksetzen). all: alle Kanaele gleich. Waehrend des Ziehens ohne Animation.</summary>
        public void SetDragWidth(ChannelConfig c, int w, bool all, bool final)
        {
            if (c == null) return;
            if (w != 0) w = Math.Max(MixerSettings.DragWidthMin, Math.Min(MixerSettings.DragWidthMax, w));
            foreach (var x in all ? _settings.Channels : new List<ChannelConfig> { c })
            {
                if (x == null) continue;
                x.WidthPx = w;
                if (w != 0) x.Width = 0;   // gezogen ersetzt schmal/normal/breit
            }
            foreach (var s in _channelStrips) if (all || s.Config == c) s.Relayout();
            _channelHost.Relayout();
            RestyleBridges();
            if (final) ScheduleSave();
        }

        private void AddDragWidth345(SPage p)
        {
            p.Toggle("Kanäle mit der Maus breiter ziehen", "Die rechte Kante eines Kanals (oder die Lücke rechts daneben) mit der Maus packen – der Zeiger wird zum Doppelpfeil – und ziehen: breiter oder schmaler (80 bis 260). Mit Strg werden alle Kanäle gleich breit. Doppelklick auf die Kante: wieder normal. Standard an. Aus = die gezogenen Breiten gelten nicht (sie bleiben gespeichert).",
                () => !_settings.DragWidthOff, v => SetDragWidthOn(v));
        }

        // ================================================================ 3.4.3 Ausgaenge in dB

        private void AddOutputs343(SPage p)
        {
            p.Toggle("Ausgänge in dB (wie am Mischpult)", "MASTER und die Ausgänge zeigen dB statt Prozent und haben dieselbe Skala wie die Kanäle: 0 dB oben = volle Lautstärke des Geräts, darunter −5, −10, −20 … −60, ganz unten aus. Der Fader stellt die Geräte-Lautstärke direkt in dB ein. Windows zeigt weiter Prozent – das ist dieselbe Lautstärke, nur anders gezählt. Standard aus.",
                () => _settings.OutputDbOn, v => SetOutputDb(v));
        }

        private void SetOutputDb(bool on)
        {
            _settings.OutputDbOn = on;
            OutputStrip.DbMode = on;
            _master?.InvalidateChrome();
            foreach (var os in _outputStrips.Values) os.InvalidateChrome();
            if (BridgeWinShown) _bridgeWin.Invalidate(true);
            ScheduleSave();
        }

        private void ShowSoundLog()
        {
            try
            {
                string p = SoundLog.FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                if (File.Exists(p)) Process.Start("explorer.exe", "/select,\"" + p + "\"");
                else
                {
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(p) + "\"");
                    ShowOsd("Ton-Protokoll", _settings.SoundLogOn ? "Noch leer – startet eine App, steht es hier" : "Erst einschalten, dann füllt es sich", -1, Theme.Accent);
                }
            }
            catch { }
        }
    }
}
