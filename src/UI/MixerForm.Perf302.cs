using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.0.2: gegen Ruckeln, wenn nebenbei ein Spiel laeuft (und auf einem zweiten Bildschirm): Windows darf das Pult nicht
    /// mehr drosseln, die Zeichen-Faeden bekommen Spiele-Vorrang (MMCSS), gezeichnet wird kurz nach dem Bildaufbau, die
    /// Bildrate bleibt gleichmaessig, Pegel kommen lueckenlos an und steigen weich. Dazu eine Bild-Statistik.
    /// Alles einzeln an/aus unter OPTIONEN → Allgemein → Hardware-Beschleunigung.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private bool NoThrottleOn => AccelOn && !_settings.NoThrottleOff;
        private bool GameBoostOn => AccelOn && !_settings.GameBoostOff;
        private bool VPhaseOn => AccelOn && !_settings.VPhaseOff;
        private bool SteadyOn => AccelOn && !_settings.SteadyOff;
        private bool SeamlessOn => AccelOn && !_settings.SeamlessOff;
        private bool SmoothOn => AccelOn && !_settings.SmoothMeterOff;

        private IntPtr _uiMmcss;
        private bool _throttleSet;

        // Bildschirm (billig vergleichen, teuer nur bei Aenderung lesen)
        private IntPtr _monitorHandle;
        private bool _displayChanged;
        private double _nextHzFull;
        private string _monitorAdapter, _primaryAdapter, _primaryDevice;
        private int _primaryHz = 60, _screenCount = 1;

        // gleichmaessige Bildrate
        private int _steadyDiv = 1, _badSecs, _goodSecs, _steadyBackoff = 1;
        private int _statsSeq = -1;

        // Leucht-Effekte beim Spielen nur sparen, wenn es eng wird
        private bool _fxTight;
        private int _fxBad;
        private double _fxTightUntil;

        private StatsOverlay _statsOverlay;
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Aus ApplyPerf: alles nach den Einstellungen setzen (UI-Faden).</summary>
        private void ApplyPerf302(bool win)
        {
            bool nt = win && NoThrottleOn;
            if (nt || _throttleSet) { PerfNative.SetNoThrottle(nt); _throttleSet = nt; }
            bool gb = win && GameBoostOn;
            FramePacer.GameBoost = gb;
            PerfNative.MmcssFollow(gb, ref _uiMmcss);   // UI-Faden: hier (im eigenen Faden) an/aus
            PerfNative.SetProcessBoost(gb);
            LevelFlow.Seamless = SeamlessOn;
            MeterBallistics.SmoothRise = SmoothOn && !LowLatencyOn;
            if (!SteadyOn && _steadyDiv != 1) { _steadyDiv = 1; _badSecs = _goodSecs = 0; }
        }

        /// <summary>Beim Schliessen: Vorrang des UI-Fadens zuruecknehmen.</summary>
        private void CleanupPerf302()
        {
            PerfNative.MmcssEnd(_uiMmcss);
            _uiMmcss = IntPtr.Zero;
        }

        /// <summary>Bildschirm des Pults lesen: Bildwiederholrate, Name, Grafikkarte – und den Hauptbildschirm zum Vergleich.</summary>
        private void ReadMonitor()
        {
            _displayChanged = false;
            _nextHzFull = _clock.Elapsed.TotalSeconds + 30;
            try
            {
                _monitorHandle = PerfNative.MonitorOf(Handle);
                var sc = Screen.FromHandle(Handle);
                _monitorHz = PerfNative.RefreshRate(sc);
                _monitorDevice = sc.DeviceName;
                _monitorAdapter = PerfNative.AdapterOf(sc.DeviceName);
                var prim = Screen.PrimaryScreen;
                _primaryDevice = prim?.DeviceName;
                _primaryHz = prim == null ? _monitorHz : PerfNative.RefreshRate(prim);
                _primaryAdapter = prim == null ? null : PerfNative.AdapterOf(prim.DeviceName);
                _screenCount = Screen.AllScreens.Length;
            }
            catch { }
        }

        /// <summary>Bildrate nach "gleichmaessig halten" (geteilt, aber nie unter 30).</summary>
        private double SteadyFps(double fps)
        {
            if (!SteadyOn || _steadyDiv <= 1) return fps;
            return Math.Max(Math.Min(fps, 30), fps / _steadyDiv);
        }

        /// <summary>Geplanter Abstand zweier Bilder (ms) – so, wie der Takt sie wirklich liefert.</summary>
        private double ExpectedFrameMs()
        {
            if (_pacer != null && _pacer.Running)
            {
                if (_pacer.VSyncActive) return 1000.0 * PerfNative.Divisor(_monitorHz, CurrentFps()) / Math.Max(24, _monitorHz);
                return 1000.0 / Math.Max(1.0, _pacer.Fps);
            }
            return Math.Max(_uiTimer.Interval, PerfNative.FineTimer || !Win32.IsWindows ? 1 : 15.6);
        }

        /// <summary>Nach jedem Bild: Messung an den Takt anpassen; einmal pro Sekunde Bildrate pruefen und Statistik zeigen.</summary>
        private void TickFrames302()
        {
            FrameStats.TargetMs = ExpectedFrameMs();
            if (FrameStats.WindowSeq == _statsSeq) return;
            _statsSeq = FrameStats.WindowSeq;
            JitStep();
            SteadyStep();
            FxTightStep();
            UpdateStatsOverlay();
        }

        /// <summary>
        /// Einmal pro Sekunde: brauchen zu viele Bilder zu lange (oder kommen zu spaet), wird die Bildrate gleichmaessig
        /// halbiert (jedes 2. Bild des Bildschirms) – lieber ruhig 72 als stotternd 144. Laeuft es lange gut, geht es wieder hoch.
        /// </summary>
        private void SteadyStep()
        {
            if (!SteadyOn) { if (_steadyDiv != 1) { _steadyDiv = 1; UpdateFps(); } return; }
            // nur mit dem genauen Takt: der einfache Windows-Timer ist ohnehin ungleichmaessig und laesst sich nicht teilen
            if (!PultShown || _covered || _pacer == null || !_pacer.Running)
            {
                _badSecs = _goodSecs = 0;
                if (_steadyDiv != 1 && (_pacer == null || !_pacer.Running)) { _steadyDiv = 1; UpdateFps(); }
                return;
            }
            int frames = FrameStats.Frames;
            if (frames < 10) return;
            bool bad = FrameStats.OverFrames > frames * 0.2 || FrameStats.LateFrames > frames * 0.2;
            bool good = FrameStats.OverFrames == 0 && FrameStats.LateFrames <= Math.Max(1, frames / 50) && FrameStats.WorkMaxMs < FrameStats.TargetMs * 0.4;
            _badSecs = bad ? _badSecs + 1 : 0;
            _goodSecs = good ? _goodSecs + 1 : 0;
            double baseFps = CurrentFps() * _steadyDiv;   // ohne Absenkung (ungefaehr)
            if (_badSecs >= 2 && _steadyDiv < 4 && baseFps / (_steadyDiv + 1) >= 29.5)
            {
                _steadyDiv++;
                _badSecs = _goodSecs = 0;
                UpdateFps();
            }
            else if (_steadyDiv > 1 && _goodSecs >= 15 * _steadyBackoff)
            {
                _steadyDiv--;
                _badSecs = _goodSecs = 0;
                _steadyBackoff = Math.Min(8, _steadyBackoff * 2);   // springt es gleich wieder runter, beim naechsten Mal laenger warten
                UpdateFps();
            }
            else if (_steadyDiv == 1 && _goodSecs >= 120) _steadyBackoff = 1;
        }

        /// <summary>
        /// Einmal pro Sekunde: beim Spielen bleiben die Leucht-Effekte an (Nachgluehen macht die Pegel weich) – erst wenn das
        /// Pult zwei Sekunden lang Bilder verliert, gehen sie fuer eine Minute aus. Ohne genauen Takt wie bis 3.0.1 (immer sparen).
        /// </summary>
        private void FxTightStep()
        {
            bool before = GameFxSaving;
            double now = _clock.Elapsed.TotalSeconds;
            if (!_gaming) { _fxTight = false; _fxBad = 0; }
            else if (_pacer == null || !_pacer.Running) _fxTight = true;   // kein genauer Takt: nicht beurteilbar
            else if (PultShown && !_covered)
            {
                int frames = FrameStats.Frames;
                bool bad = frames >= 10 && (FrameStats.LateFrames > frames * 0.1 || FrameStats.OverFrames > frames * 0.1);
                _fxBad = bad ? _fxBad + 1 : 0;
                if (_fxBad >= 2) { _fxTight = true; _fxTightUntil = now + 60; _fxBad = 0; }
                else if (_fxTight && now >= _fxTightUntil) _fxTight = false;   // nochmal mit Effekten versuchen
            }
            if (GameFxSaving != before) ApplyFxStatics(true);
        }

        // ================================================================ 3.1.1: Pegel ohne Verzoegerung

        private bool LowLatencyOn => !_settings.LowLatencyOff;
        private double _jitMargin = 6.0;   // so viel ms vor dem naechsten Bildaufbau soll das Bild fertig sein

        /// <summary>Wann nach dem Bildaufbau gezeichnet wird, damit das Bild knapp vor dem naechsten fertig ist (frischeste Pegel).</summary>
        private double JitPhase()
        {
            double period = 1000.0 / Math.Max(24, _monitorHz);
            return Math.Max(1.0, period - _jitMargin);
        }

        /// <summary>
        /// Einmal pro Sekunde: Abstand zum naechsten Bildaufbau an die gemessene Zeichenzeit anpassen. Wird es knapp
        /// (Bilder zu spaet), sofort mehr Luft; laeuft es lange ruhig, langsam wieder naeher heran.
        /// </summary>
        private void JitStep()
        {
            if (!LowLatencyOn || _pacer == null || !_pacer.Running || !_pacer.VSyncActive) return;
            double period = 1000.0 / Math.Max(24, _monitorHz);
            double want = Math.Max(3.5, Math.Min(period - 1.0, FrameStats.WorkMaxMs + 3.0));
            int frames = Math.Max(1, FrameStats.Frames);
            if (FrameStats.LateFrames > Math.Max(1, frames / 40)) _jitMargin = Math.Min(period - 1.0, _jitMargin + 2.0);
            else if (want > _jitMargin) _jitMargin = want;
            else _jitMargin += (want - _jitMargin) * 0.2;
            _pacer.PhaseMs = JitPhase();
        }

        private void SetLowLatency(bool on)
        {
            _settings.LowLatencyOff = !on;
            LevelFlow.FastPoll = on;
            MeterBallistics.SmoothRise = SmoothOn && !on;
            _jitMargin = 6.0;
            ApplyVSync();
            ScheduleSave();
        }

        private void AddLowLatency311(SPage p)
        {
            p.Toggle("Pegel ohne Verzögerung", "Damit die Pegel genau im Takt mit dem Ton zucken: gemessen wird alle 5 statt 10 ms, die LEDs springen sofort hoch (statt weich zu steigen), und jedes Bild wird so spät wie möglich gezeichnet – knapp bevor der Bildschirm es zeigt, mit den frischesten Werten. Aus = wie bis 3.1.",
                () => !_settings.LowLatencyOff, v => SetLowLatency(v));
            p.Toggle("Kanal-Pegel so schnell wie die Ausgänge", "Die Pegel der Kanäle kommen aus einer Messung der Apps. Die wurde bisher nur alle 15 ms abgeholt und erst danach weitergegeben – die Kanäle zuckten so im Schnitt 15–20 ms nach dem MASTER und in ungleichen Schüben. Jetzt geht jedes neue Stück Ton sofort weiter, die Kanäle laufen gleich schnell wie die Ausgänge. Braucht „Pegel lückenlos übernehmen“ (Hardware-Beschleunigung). Aus = wie bis 3.1.1.",
                () => !_settings.ChannelFastOff, v => SetChannelFast(v)).Enabled = () => SeamlessOn;
        }

        private void SetChannelFast(bool on) { _settings.ChannelFastOff = !on; LevelFlow.ChannelFast = on; ScheduleSave(); }

        /// <summary>Zusaetzliche Teile fuer "Zustand".</summary>
        private void AddPerf302Status(List<string> parts)
        {
            if (_pacer != null && _pacer.Running && _pacer.VSyncActive && _pacer.PhaseMs > 0)
                parts.Add(LowLatencyOn ? "Bild " + _jitMargin.ToString("0.0", De) + " ms vor dem Bildaufbau fertig (Pegel ohne Verzögerung)"
                                       : "Bild " + _pacer.PhaseMs.ToString("0.0", De) + " ms nach dem Bildaufbau");
            if (SteadyOn && _steadyDiv > 1) parts.Add("gleichmäßig gesenkt (sonst zu viel Rechenzeit)");
            if (NoThrottleOn && PerfNative.ThrottleState != null) parts.Add("Windows-Drosselung " + PerfNative.ThrottleState);
            if (GameBoostOn && Win32.IsWindows && !_demo)
                parts.Add(_uiMmcss != IntPtr.Zero || (_pacer != null && _pacer.Boosted) ? "Spiele-Vorrang an" : "Spiele-Vorrang geht hier nicht");
            string pace = FrameStats.PacingText();
            if (pace != null) parts.Add(pace);
            if (FrameStats.HasData && FrameStats.DataPerSec > 0)
                parts.Add("Pegel " + FrameStats.DataPerSec.ToString("0", De) + "x/s (längste Lücke " + FrameStats.DataGapMs.ToString("0", De) + " ms)");
        }

        /// <summary>Bildschirme und Grafikkarten, mit Hinweisen (fuer die Einstellungen).</summary>
        private string DisplayInfoText()
        {
            if (!Win32.IsWindows || _demo) return "Pult auf einem Bildschirm mit " + _monitorHz + " Hz (Vorschau).";
            var sb = new System.Text.StringBuilder();
            bool onPrimary = _primaryDevice == null || string.Equals(_primaryDevice, _monitorDevice, StringComparison.OrdinalIgnoreCase);
            sb.Append("Pult auf ").Append(onPrimary ? "dem Hauptbildschirm" : "einem zweiten Bildschirm").Append(": ").Append(_monitorHz).Append(" Hz");
            if (_monitorAdapter != null) sb.Append(", ").Append(_monitorAdapter);
            if (!onPrimary)
            {
                sb.Append(" · Hauptbildschirm: ").Append(_primaryHz).Append(" Hz");
                if (_primaryAdapter != null && !string.Equals(_primaryAdapter, _monitorAdapter, StringComparison.OrdinalIgnoreCase)) sb.Append(", ").Append(_primaryAdapter);
            }
            sb.Append('.');
            foreach (var t in DisplayTips()) sb.Append(' ').Append(t);
            return sb.ToString();
        }

        /// <summary>Hinweise, warum es trotz allem ruckeln kann (Windows/Grafikkarte, nicht das Pult).</summary>
        private List<string> DisplayTips()
        {
            var tips = new List<string>();
            if (!Win32.IsWindows || _demo) return tips;
            bool onPrimary = _primaryDevice == null || string.Equals(_primaryDevice, _monitorDevice, StringComparison.OrdinalIgnoreCase);
            if (!onPrimary && _primaryAdapter != null && _monitorAdapter != null && !string.Equals(_primaryAdapter, _monitorAdapter, StringComparison.OrdinalIgnoreCase))
                tips.Add("Achtung: dieser Bildschirm hängt an einer anderen Grafikkarte als der Hauptbildschirm – Windows muss jedes Bild umkopieren, das ruckelt oft. Besser beide Bildschirme an dieselbe Grafikkarte anschließen.");
            if (!onPrimary && _primaryHz != _monitorHz && Wasapi.WindowsBuild > 0 && Wasapi.WindowsBuild < 19041)
                tips.Add("Dieses Windows (vor Version 2004) zeichnet alle Bildschirme im Takt des Hauptbildschirms – ein Windows-Update hilft gegen Ruckeln bei unterschiedlichen Hz.");
            if (_gaming)
                tips.Add("Spiel läuft: Ruckelt auch ein Video auf diesem Bildschirm, ist die Grafikkarte am Limit – im Spiel die Bildrate begrenzen (z. B. 3 unter der Hz-Zahl des Spiel-Bildschirms).");
            return tips;
        }

        // ================================================================ Bild-Statistik

        private void EnsureStatsOverlay()
        {
            bool want = _settings.StatsOverlay;
            if (!want)
            {
                if (_statsOverlay != null) { Controls.Remove(_statsOverlay); _statsOverlay.Dispose(); _statsOverlay = null; }
                return;
            }
            if (_statsOverlay == null)
            {
                _statsOverlay = new StatsOverlay();
                _statsOverlay.CloseClicked += (s, e) => SetStatsOverlay(false);
                Controls.Add(_statsOverlay);
            }
            UpdateStatsOverlay();
        }

        private void UpdateStatsOverlay()
        {
            if (_statsOverlay == null) return;
            _statsOverlay.SetLines(StatsLines());
            int y = (_top != null && _top.Visible ? _top.Bottom : 0) + Theme.Px(10);
            var loc = new System.Drawing.Point(Theme.Px(14), y);
            if (_statsOverlay.Location != loc) _statsOverlay.Location = loc;
            if (Controls.GetChildIndex(_statsOverlay) != 0) _statsOverlay.BringToFront();
        }

        /// <summary>Was die Bild-Statistik zeigt.</summary>
        internal StatsOverlay.Line[] StatsLines()
        {
            var l = new List<StatsOverlay.Line> { new StatsOverlay.Line("BILD-STATISTIK", 1) };
            if (!FrameStats.HasData) { l.Add(new StatsOverlay.Line("misst …")); return l.ToArray(); }
            string takt = _pacer != null && _pacer.Running
                ? (_pacer.VSyncActive ? "VSync " + _monitorHz + " Hz" + (_pacer.PhaseMs > 0 ? (LowLatencyOn ? " · −" + _jitMargin.ToString("0.0", De) + " ms" : " · +" + _pacer.PhaseMs.ToString("0.0", De) + " ms") : "") : _pacer.HighResolution ? "genauer Takt" : "Takt")
                : _demo ? "Vorschau" : "Windows-Timer";
            l.Add(new StatsOverlay.Line(FrameStats.Fps.ToString("0", De) + " Bilder/s · " + takt + (SteadyOn && _steadyDiv > 1 ? " · gesenkt" : "")));
            l.Add(new StatsOverlay.Line("Abstand " + FrameStats.IntervalMs.ToString("0.0", De) + " ms ± " + FrameStats.IntervalJitter.ToString("0.0", De)
                + " (" + FrameStats.IntervalMin.ToString("0.0", De) + "–" + FrameStats.IntervalMax.ToString("0.0", De) + ")"));
            l.Add(new StatsOverlay.Line("Bildzeit Ø " + FrameStats.WorkAvgMs.ToString("0.0", De) + " · max " + FrameStats.WorkMaxMs.ToString("0.0", De) + " ms"));
            l.Add(new StatsOverlay.Line("Zu spät: " + FrameStats.LateFrames + " (seit Start " + FrameStats.LateTotal + ")"));
            l.Add(new StatsOverlay.Line("Pegel " + FrameStats.DataPerSec.ToString("0", De) + "x/s · längste Lücke " + FrameStats.DataGapMs.ToString("0", De) + " ms"));
            var cpu = ThreadCpu.Text();   // 3.4.1: Rechenzeit pro Faden
            if (cpu != null) foreach (var part in cpu.Split('\n')) l.Add(new StatsOverlay.Line(part));
            var flags = new List<string>();
            if (_gaming) flags.Add("Spiel läuft");
            if (GameBoostOn && (_uiMmcss != IntPtr.Zero || (_pacer != null && _pacer.Boosted))) flags.Add("Vorrang");
            if (NoThrottleOn && PerfNative.ThrottleState == "abgeschaltet") flags.Add("ungedrosselt");
            if (SeamlessOn) flags.Add("lückenlos");
            if (SmoothOn) flags.Add("weich");
            if (flags.Count > 0) l.Add(new StatsOverlay.Line(string.Join(" · ", flags)));
            bool ok = FrameStats.LateFrames <= Math.Max(1, FrameStats.Frames / 50) && FrameStats.OverFrames <= Math.Max(1, FrameStats.Frames / 50);
            if (_pacer == null || !_pacer.Running)
                l.Add(new StatsOverlay.Line(_demo || !Win32.IsWindows ? "→ Vorschau (ohne Windows-Takt)" : "→ einfacher Windows-Timer (genauer Takt ist aus)", 4));
            else if (ok)
            {
                l.Add(new StatsOverlay.Line("→ Pult läuft gleichmäßig", 2));
                if (_gaming) l.Add(new StatsOverlay.Line("Ruckelt es trotzdem: Grafikkarte am Limit,", 4));
                if (_gaming) l.Add(new StatsOverlay.Line("im Spiel die Bildrate begrenzen.", 4));
            }
            else l.Add(new StatsOverlay.Line("→ Pult verliert Bilder (PC ausgelastet)", 3));
            return l.ToArray();
        }

        private void SetStatsOverlay(bool on)
        {
            _settings.StatsOverlay = on;
            EnsureStatsOverlay();
            ScheduleSave();
        }

        // ================================================================ Einstellen

        private void SetNoThrottle(bool on) { _settings.NoThrottleOff = !on; ApplyPerf(); ScheduleSave(); }
        private void SetGameBoost(bool on) { _settings.GameBoostOff = !on; ApplyPerf(); ScheduleSave(); }
        private void SetVPhase(bool on) { _settings.VPhaseOff = !on; ApplyVSync(); ScheduleSave(); }
        private void SetSteady(bool on) { _settings.SteadyOff = !on; _steadyDiv = 1; _badSecs = _goodSecs = 0; _steadyBackoff = 1; UpdateFps(); ScheduleSave(); }
        private void SetSeamless(bool on) { _settings.SeamlessOff = !on; LevelFlow.Seamless = SeamlessOn; ScheduleSave(); }
        private void SetSmoothMeter(bool on) { _settings.SmoothMeterOff = !on; MeterBallistics.SmoothRise = SmoothOn && !LowLatencyOn; ScheduleSave(); }

        /// <summary>3.0.3: kein Knacken beim Abspielen / Messung bleibt bereit (gilt sofort in Engine und Mischer).</summary>
        private static void ApplyCleanStart(MixerSettings s)
        {
            CleanStart.On = !s.SoftStartOff;
            CleanStart.MeterKeep = !s.MeterKeepOff;
            AudioEngine.GuardOn = !s.AppVolGuardOff;   // 3.4.1
            SoundLog.On = s.SoundLogOn;                 // 3.4.1
        }

        private void SetSoftStart(bool on) { _settings.SoftStartOff = !on; ApplyCleanStart(_settings); ScheduleSave(); }
        private void SetMeterKeep(bool on) { _settings.MeterKeepOff = !on; ApplyCleanStart(_settings); ScheduleSave(); }

        private void AddCleanStartSettings(SPage p)
        {
            p.Toggle("Kein Knacken beim Abspielen", "Fängt ein Programm an zu spielen, stellt das Pult es innerhalb von etwa 1 ms richtig ein (vorher war es kurz so laut, wie Windows es sich gemerkt hatte). Auf dem EQ-Weg wird nur richtig abgesenkter Ton angehoben: Ist eine App gerade direkt zu hören, kommt sie nicht noch einmal verstärkt dazu. Der Puffer wird weich aus- und eingeblendet statt hart geschnitten, und nach einem Aussetzer setzt der Ton weich wieder ein. Aus = wie bis 3.0.2.",
                () => !_settings.SoftStartOff, v => SetSoftStart(v));
            p.Toggle("App-Lautstärke festhalten", "Manche Apps stellen ihre Lautstärke in Windows selbst ein, z. B. Spotify beim Abspielen. Dann waren sie kurz laut (und auf dem EQ-Weg doppelt zu hören). Jetzt stellt das Pult sofort wieder seinen Wert ein. Die Lautstärke regelst du dann nur im Pult, nicht mehr in der App oder im Windows-Mixer. Aus = wie bis 3.4.",
                () => !_settings.AppVolGuardOff, v => { _settings.AppVolGuardOff = !v; ApplyCleanStart(_settings); ScheduleSave(); }).Enabled = () => !_settings.SoftStartOff;
            p.Toggle("Pegel-Messung bleibt bereit", "Nach einer Pause bleibt die Messung eines Programms 10 Minuten bereit (bisher 10 Sekunden). So muss sie nicht genau dann neu starten, wenn die Musik wieder anfängt. Aus = wie bis 3.0.2.",
                () => !_settings.MeterKeepOff, v => SetMeterKeep(v));
            p.Toggle("Ton-Protokoll (Fehlersuche)", "Schreibt mit, wann eine App anfängt, wie laut Windows sie startet, wann das Pult sie einstellt, wer ihre Lautstärke ändert und wie der EQ-Weg die ersten Stücke Ton verstärkt. Falls es doch noch knackt: einschalten, das Knacken einmal auslösen und die Datei schicken. Standard aus.",
                () => _settings.SoundLogOn, v => { _settings.SoundLogOn = v; ApplyCleanStart(_settings); ScheduleSave(); });
            p.Button("Protokoll-Datei", MixerSettings.DocsMode ? "ton-protokoll.txt im Ordner Dokumente\\Mischpult\\Protokolle." : "ton-protokoll.txt im Einstellungs-Ordner.", "Zeigen", r => ShowSoundLog());
        }

        /// <summary>Beim Start (vor dem ersten Bild): statische Schalter setzen; Vorschau kann die Statistik einblenden.</summary>
        private void ApplyPerf302Statics(MixerSettings s, bool demo)
        {
            ApplyCleanStart(s);
            if (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_STATSOVERLAY") == "1") s.StatsOverlay = true;
            LevelFlow.Seamless = !s.AccelOff && !s.SeamlessOff;
            MeterBallistics.SmoothRise = !s.AccelOff && !s.SmoothMeterOff && s.LowLatencyOff;
            LevelFlow.FastPoll = !s.LowLatencyOff;
            LevelFlow.ChannelFast = !s.ChannelFastOff;
        }

        private void AddAccel302Settings(SPage p)
        {
            p.Toggle("Nicht von Windows drosseln lassen", "Windows bremst Programme im Hintergrund zum Stromsparen („Effizienzmodus“, langsame Prozessorkerne, ungenaue Zeitgeber) – genau dann, wenn ein Spiel im Vordergrund läuft. Das Pult nimmt sich davon aus und läuft gleichmäßig weiter.",
                () => !_settings.NoThrottleOff, v => SetNoThrottle(v)).Enabled = () => AccelOn;
            p.Toggle("Spiele-Vorrang fürs Zeichnen", "Windows behandelt das Zeichnen des Pults wie ein Spiel (Multimedia-Vorrang, MMCSS) und das Pult eine Stufe höher als normal – so kommt es auch neben einem Spiel, das den Prozessor auslastet, pünktlich dran. Kostet das Spiel praktisch nichts.",
                () => !_settings.GameBoostOff, v => SetGameBoost(v)).Enabled = () => AccelOn;
            p.Toggle("Kurz nach dem Bildaufbau zeichnen", "Mit VSync wird jedes Bild ein paar Millisekunden nach dem Bildaufbau gezeichnet. Sonst ist es manchmal knapp vor, manchmal knapp nach Windows fertig und kommt mal ein Bild früher, mal später – das sieht trotz VSync nach Ruckeln aus.",
                () => !_settings.VPhaseOff, v => SetVPhase(v)).Enabled = () => AccelOn && !_settings.VSyncOff && !_settings.PacerOff && _settings.LowLatencyOff;   // 3.1.1: sonst zeichnet „Pegel ohne Verzögerung“ kurz vor dem Bildaufbau
            p.Toggle("Bildrate gleichmäßig halten", "Schafft der PC gerade nicht jedes Bild (z. B. neben einem Spiel), zeichnet das Pult gleichmäßig jedes zweite Bild statt unregelmäßig welche auszulassen – nie unter 30 Bilder/s. Läuft es wieder gut, geht es von selbst zurück.",
                () => !_settings.SteadyOff, v => SetSteady(v)).Enabled = () => AccelOn;
            p.Toggle("Pegel lückenlos übernehmen", "Zwischen zwei Bildern misst das Pult den Ton mehrmals – jetzt zählt die höchste Spitze aus allen Messungen statt nur der letzten. Dazu sucht es die laufenden Programme nicht mehr jede Sekunde neu (das hielt die Pegel kurz an). Die Pegel zucken nicht mehr unregelmäßig.",
                () => !_settings.SeamlessOff, v => SetSeamless(v)).Enabled = () => AccelOn;
            p.Toggle("Pegel weich bewegen", "Pegel steigen in rund 12 ms auf den neuen Wert statt in einem Bild zu springen – gleicher Look, gleiche LEDs, nur flüssiger. Die Spitzen-Anzeige und CLIP reagieren weiter sofort.",
                () => !_settings.SmoothMeterOff, v => SetSmoothMeter(v)).Enabled = () => AccelOn && _settings.LowLatencyOff;   // 3.1.1: „Pegel ohne Verzögerung“ springt sofort
            p.Toggle("Bild-Statistik einblenden", "Kleines Feld oben links im Pult: Bilder pro Sekunde, wie gleichmäßig sie kommen, zu späte Bilder, Rechenzeit, wie oft neue Pegel kommen und wie viel CPU Oberfläche, Ton, EQ und Pegel-Messung brauchen. Zeigt, ob das Pult selbst Bilder verliert – oder ob Windows/die Grafikkarte ruckelt. Funktioniert auch, wenn die Hardware-Beschleunigung aus ist.",
                () => _settings.StatsOverlay, v => SetStatsOverlay(v));
            p.Info("Bildschirme", () => DisplayInfoText());
        }
    }
}
