using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.15.1 Leistung: gleichmaessiger Takt (eigener genauer Zeitgeber statt des groben Windows-Timers), Vorrang fuers
    /// Pult, schnelle Pegel der Ausgaenge und ein Spiele-Modus (pausiert hinter Vollbild-Spielen, spart sonst Effekte).
    /// Alles einzeln in den Einstellungen (Allgemein → Leistung) an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private FramePacer _pacer;
        private bool _gaming;            // Spiel (oder Vollbild-Programm) im Vordergrund
        private bool _covered;           // ... und es verdeckt das Pult (gleicher Bildschirm)
        private double _nextGameCheck, _nextHzCheck;
        private int _monitorHz = 60;
        private readonly Dictionary<uint, bool> _pidGame = new Dictionary<uint, bool>();
        private double _pidGameClear;

        public static readonly string[] PerfFpsNames = { "Passend zum Bildschirm", "30", "45", "60", "90", "120", "144" };

        // ---- 3.0: Hardware-Beschleunigung (alles einzeln an/aus, dazu ein Hauptschalter)
        private bool AccelOn => !_settings.AccelOff;
        private bool VSyncOn => AccelOn && !_settings.VSyncOff;
        private bool HighFpsOn => AccelOn && !_settings.HighFpsOff;
        private bool PaintNowOn => AccelOn && !_settings.PaintNowOff;
        private string _monitorDevice;
        /// <summary>Hoechste Bildrate bei "Passend zum Bildschirm".</summary>
        private int AutoFpsMax => _settings.FpsHighAuto ? (HighFpsOn ? 165 : 90) : 75;   // 3.4.1: Standard hoechstens 75 (spart Rechenzeit)

        /// <summary>VSync an den Takt geben (nach jeder Aenderung von Bildschirm, Bildrate oder Einstellung).</summary>
        private void ApplyVSync()
        {
            if (_pacer == null || !_pacer.Running) return;
            if (VSyncOn && _monitorDevice != null)
            {
                _pacer.SetVSync(_monitorDevice, PerfNative.Divisor(_monitorHz, CurrentFps()));
                _pacer.PhaseMs = LowLatencyOn ? JitPhase() : VPhaseOn ? PerfNative.PhaseFor(_monitorHz) : 0;   // 3.1.1: kurz vor / 3.0.2: kurz nach dem Bildaufbau
            }
            else { _pacer.SetVSync(null, 1); _pacer.PhaseMs = 0; }
        }

        private void PaintNowAll()
        {
            PerfNative.PaintNow(this);
            if (BridgeWinShown) PerfNative.PaintNow(_bridgeWin);
            if (VuWindowOpen) PerfNative.PaintNow(_vuWin);
        }
        public static readonly string[] GameFpsNames = { "Wie sonst", "60", "45", "30" };

        /// <summary>Pult ist zu sehen: nicht versteckt, nicht minimiert, nicht von einem Vollbild-Spiel verdeckt.</summary>
        private bool PultShown => Visible && WindowState != FormWindowState.Minimized && !_covered;

        /// <summary>Spiele-Modus spart gerade die Leucht-Effekte.</summary>
        private bool GameFxSaving => _gaming && !_settings.GameModeOff && !_settings.GameFxOff && (_settings.SmartFxOff || _fxTight);

        /// <summary>Takt, Vorrang und schnelle Messung nach den Einstellungen setzen (UI-Faden).</summary>
        private void ApplyPerf()
        {
            bool win = Win32.IsWindows && !_demo;
            bool precise = !_settings.PacerOff;
            bool boost = !_settings.PerfPriorityOff;
            if (win && _nextHzCheck <= 0) ReadMonitor();

            PerfNative.SetFineTimer(win && precise);   // Wartezeiten auf 1 ms genau statt ~15,6 ms
            if (win && precise)
            {
                if (_pacer == null) _pacer = new FramePacer(this, Tick);
                _pacer.Fps = CurrentFps();
                if (_pacer.TryStart()) { _uiTimer.Stop(); _pacer.SetVSync(null, 1); ApplyVSync(); }
                else { StopPacer(); StartUiTimer(); }
            }
            else
            {
                StopPacer();
                StartUiTimer();
            }

            // Vorrang: das Pult kommt beim Zeichnen nicht hinter dem Spiel dran; Speicher wird ohne lange Pausen aufgeraeumt
            try { Thread.CurrentThread.Priority = win && boost ? ThreadPriority.AboveNormal : ThreadPriority.Normal; } catch { }
            try { GCSettings.LatencyMode = boost ? GCLatencyMode.SustainedLowLatency : GCLatencyMode.Interactive; } catch { }
            _bgSaving = BackgroundSaving;
            Backend.SetPerf(precise && !_bgSaving, boost);   // 3.4.1: versteckt -> Ausgangs-Pegel nicht alle 4 ms messen
            Backend.SetRealtime(!_settings.MeterRealtimeOff && !_bgSaving);   // 2.16: Pegel alle 10 ms
            ApplyPerf302(win);   // 3.0.2: Drosselung, Spiele-Vorrang, Pegel lueckenlos/weich
        }

        private void StartUiTimer()
        {
            int ms = (int)Math.Round(1000.0 / CurrentFps());
            _uiTimer.Interval = Math.Max(10, Math.Min(50, _settings.PerfFps == 0 && !GameFpsActive && !_covered ? 15 : ms));
            if (!_uiTimer.Enabled) _uiTimer.Start();
        }

        private void StopPacer()
        {
            if (_pacer == null) return;
            _pacer.Stop();
        }

        private bool GameFpsActive => _gaming && !_settings.GameModeOff && _settings.GameFps > 0;

        /// <summary>Bilder pro Sekunde gerade (Einstellung, Bildschirm, Spiele-Modus).</summary>
        private double CurrentFps()
        {
            double fps = _settings.PerfFps > 0 ? _settings.PerfFps : PerfNative.AutoFps(_monitorHz, AutoFpsMax);
            if (GameFpsActive) fps = Math.Min(fps, _settings.GameFps);
            fps = SteadyFps(fps);   // 3.0.2: zu viel Rechenzeit → gleichmaessig weniger statt unregelmaessig
            if (_covered || _bgSaving) fps = Math.Min(fps, 30);   // verdeckt/versteckt: nur Automatik (Push-to-Talk, Ducking …), 30x pro Sekunde reicht
            return Math.Max(20, Math.Min(HighFpsOn ? 240 : 120, fps));
        }

        private void UpdateFps()
        {
            if (_pacer != null && _pacer.Running) { _pacer.Fps = CurrentFps(); ApplyVSync(); }
            else if (_uiTimer.Enabled) StartUiTimer();
        }

        private void StopPerf()
        {
            StopPacer();
            _uiTimer.Stop();
            PerfNative.SetFineTimer(false);
        }

        // ================================================================ Spiele-Modus

        /// <summary>Aus dem Takt (zweimal pro Sekunde): Spiel im Vordergrund? Verdeckt es das Pult? Bildschirm gewechselt?</summary>
        private void TickPerf(double now)
        {
            if (now < _nextGameCheck) return;
            _nextGameCheck = now + 0.5;
            bool win = Win32.IsWindows && !_demo;
            if (win && now >= _nextHzCheck)
            {
                // 3.0.2: billig pruefen, ob das Pult auf einem anderen Bildschirm steht; die Bildwiederholrate selbst nur dann
                // (oder nach einer Aenderung der Anzeige) neu lesen – das Abfragen beim Grafiktreiber kann einige ms dauern
                _nextHzCheck = now + 1;
                IntPtr mon = PerfNative.MonitorOf(Handle);
                if (mon != _monitorHandle || _displayChanged || now >= _nextHzFull)
                {
                    int hz = _monitorHz; string dev = _monitorDevice;
                    ReadMonitor();
                    if (hz != _monitorHz || dev != _monitorDevice) UpdateFps();   // auch: Pult auf anderen Bildschirm gezogen
                }
            }

            bool gaming = false, covered = false;
            if (win && !_settings.GameModeOff)
            {
                IntPtr fg = IntPtr.Zero;
                uint pid = 0;
                try { fg = Win32.GetForegroundWindow(); if (fg != IntPtr.Zero) Win32.GetWindowThreadProcessId(fg, out pid); } catch { }
                if (fg != IntPtr.Zero && pid != 0 && pid != OwnPid)
                {
                    bool full = PerfNative.IsFullscreen(fg);
                    gaming = full || IsGamePid(pid, now);
                    covered = full && !_settings.GamePauseOff && !TopMost && PerfNative.SameMonitor(fg, Handle);
                }
            }
            else if (_demo)
            {
                // nur Vorschau: Spiele-Modus vorfuehren
                string dg = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_GAMING");
                gaming = (dg == "1" || dg == "2") && !_settings.GameModeOff;
                covered = dg == "2" && gaming && !_settings.GamePauseOff;
            }
            SetGameState(gaming, covered);
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_STATS") == "1" && FrameStats.HasData && now >= _nextStatsOut)
            {
                _nextStatsOut = now + 2;   // nur Vorschau: Rechenzeit ausgeben
                Console.WriteLine("STATS " + FrameStats.Text() + " · " + FrameStats.Fps.ToString("0") + " Bilder/s");
            }
        }
        private double _nextStatsOut;

        /// <summary>Gehoert der Prozess zu einem Spiel (Spiel-Kanal oder als Spiel erkannt)?</summary>
        private bool IsGamePid(uint pid, double now)
        {
            if (now > _pidGameClear) { _pidGame.Clear(); _pidGameClear = now + 30; }
            if (_pidGame.TryGetValue(pid, out bool g)) return g;
            string path = Win32.GetProcessPath(pid);
            string key = path != null ? AppKeys.FromExe(path) : null;
            var gc = GameChannel;
            g = key != null && ((gc != null && gc.Apps.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
                                (!_settings.NotGames.Contains(key, StringComparer.OrdinalIgnoreCase) && GameDetector.IsGame(key, path)));
            _pidGame[pid] = g;
            return g;
        }

        private void SetGameState(bool gaming, bool covered)
        {
            if (gaming == _gaming && covered == _covered) return;
            bool fxBefore = GameFxSaving, fpsBefore = GameFpsActive;
            bool wasCovered = _covered;
            _gaming = gaming;
            _covered = covered;
            if (GameFxSaving != fxBefore) ApplyFxStatics(true);
            if (GameFpsActive != fpsBefore || wasCovered != covered) UpdateFps();
            if (wasCovered && !covered)
            {
                // wieder zu sehen: Anzeigen sofort auf den aktuellen Stand
                foreach (var c in new Control[] { _channelHost, _outputHost, _bridge, _top }) c?.Invalidate(true);
            }
        }

        /// <summary>Leucht-Effekte aus Einstellungen und Spiele-Modus setzen. repaint: betroffene Anzeigen neu zeichnen.</summary>
        private void ApplyFxStatics(bool repaint)
        {
            bool save = GameFxSaving;
            bool halo = !_settings.LedHaloOff && !save, ag = !_settings.LedAfterglowOff && !save, glow = !_settings.FaderGlowOff && !save;
            bool changed = halo != Theme.LedHalo || ag != Theme.LedAfterglow || glow != StripBase.FaderGlowOn;
            Theme.LedHalo = halo;
            Theme.LedAfterglow = ag;
            StripBase.FaderGlowOn = glow;
            if (!changed || !repaint) return;
            foreach (var c in new Control[] { _channelHost, _outputHost }) c?.Invalidate(true);
            _bridge?.Restyle();
        }

        /// <summary>Was gerade gilt – fuer die Einstellungen.</summary>
        private string PerfStatusText()
        {
            var parts = new List<string>();
            parts.Add(((int)Math.Round(CurrentFps())) + " Bilder/s" + (_settings.PerfFps == 0 && !GameFpsActive ? " (Bildschirm: " + _monitorHz + " Hz)" : ""));
            if (_pacer != null && _pacer.Running && _pacer.VSyncActive) parts.Add("im Takt des Bildschirms (VSync)");
            else if (_pacer != null && _pacer.Running) parts.Add((_pacer.HighResolution ? "genauer Takt" : "Takt") + (VSyncOn && _pacer.VSyncError != null ? " (VSync geht hier nicht: " + _pacer.VSyncError + ")" : ""));
            else parts.Add(_demo ? "Vorschau" : "Windows-Timer");
            if (_covered) parts.Add("Vollbild-Spiel verdeckt das Pult → Anzeigen pausiert");
            else if (_gaming && !_settings.GameModeOff) parts.Add("Spiel im Vordergrund" + (GameFxSaving ? " → Leucht-Effekte gespart" : !_settings.GameFxOff && !_settings.SmartFxOff ? " → Leucht-Effekte bleiben (PC kommt hinterher)" : ""));
            string load = FrameStats.Text();
            if (load != null) parts.Add("Rechenzeit " + load);
            AddPerf302Status(parts);
            return "Gerade: " + string.Join(" · ", parts) + ".";
        }

        // ================================================================ Einstellen

        private void SetPerfFps(int i)
        {
            _settings.PerfFps = MixerSettings.PerfFpsValues[Math.Max(0, Math.Min(MixerSettings.PerfFpsValues.Length - 1, i))];
            UpdateFps();
            ScheduleSave();
        }

        private void SetGameFps(int i)
        {
            _settings.GameFps = MixerSettings.GameFpsValues[Math.Max(0, Math.Min(MixerSettings.GameFpsValues.Length - 1, i))];
            UpdateFps();
            ScheduleSave();
        }

        private void SetFastDraw(bool on)
        {
            _settings.FastDrawOff = !on;
            StripBase.FastDraw = on;
            RefreshStrips();
            _bridge.Restyle();
            ScheduleSave();
        }

        private void SetPacer(bool on) { _settings.PacerOff = !on; ApplyPerf(); ScheduleSave(); }
        private void SetPerfPriority(bool on) { _settings.PerfPriorityOff = !on; ApplyPerf(); ScheduleSave(); }

        private void SetGameMode(bool on)
        {
            _settings.GameModeOff = !on;
            if (!on) SetGameState(false, false);
            _nextGameCheck = 0;
            ScheduleSave();
        }

        private void SetGamePause(bool on) { _settings.GamePauseOff = !on; _nextGameCheck = 0; if (!on && _covered) SetGameState(_gaming, false); ScheduleSave(); }
        private void SetGameFx(bool on) { _settings.GameFxOff = !on; ApplyFxStatics(true); ScheduleSave(); }
        private void SetSmartFx(bool on) { _settings.SmartFxOff = !on; _fxBad = 0; ApplyFxStatics(true); ScheduleSave(); }

        /// <summary>Schnellmenue (Automatik): Spiele-Modus an/aus.</summary>
        private ToolStripMenuItem GameModeItem() =>
            new ToolStripMenuItem("Spiele-Modus (flüssig beim Spielen)", null, (s, e) => SetGameMode(_settings.GameModeOff)) { Checked = !_settings.GameModeOff };
    }
}
