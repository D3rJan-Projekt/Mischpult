using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    internal sealed partial class MixerForm : Form
    {
        public static Color BodyColor => Theme.DimColor(Theme.Body);

        public readonly IAudioBackend Backend;
        public readonly IconCache Icons;
        private MixerSettings _settings;   // 4.1: nicht readonly – die Uebersicht vergleicht kurz mit den Standardwerten
        private readonly bool _demo;

        private readonly TopBar _top;
        private readonly MeterBridge _bridge;
        private readonly StripHost _channelHost, _outputHost;
        private readonly Divider _divider;
        private readonly AddStripButton _addButton;
        private readonly List<ChannelStrip> _channelStrips = new List<ChannelStrip>();
        private readonly OutputStrip _master;
        private readonly Dictionary<string, OutputStrip> _outputStrips = new Dictionary<string, OutputStrip>(StringComparer.OrdinalIgnoreCase);
        private bool _lastEqSupported;

        /// <summary>Modus "Mehrere Ausgaenge": ein Zug pro Geraet und Ausgangswahl pro Kanal.</summary>
        public bool MultiOutput => _settings.MultiOutput;

        private readonly MicStrip _mic;
        private readonly HotkeyManager _hotkeys = new HotkeyManager();
        private OsdForm _osd;

        // Ducking
        private float _duckDb;            // aktuelle Absenkung (<= 0)
        private float _duckApplied;       // in der Policy verwendete (gerundete) Absenkung
        private double _duckHoldUntil;
        private bool _duckActive;
        private readonly Dictionary<string, float> _channelLevelDb = new Dictionary<string, float>();

        // Erkennung "Ausloeser spiegelt nur den uebrigen Ton" (z. B. Discord nimmt den PC-Ton mit)
        private readonly LeakDetector _leak = new LeakDetector();
        private int _leakTick;
        private bool _duckVoice;
        private bool _leakPresent => _leak.Present;
        private float _leakOffset => _leak.Offset;


        private readonly Timer _uiTimer = new Timer { Interval = 15 };
        private readonly Timer _saveTimer = new Timer { Interval = 800 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastTick;

        private EngineSnapshot _snap = EngineSnapshot.Empty;
        private string _lastStructure;
        private readonly Dictionary<string, AppState> _appsByKey = new Dictionary<string, AppState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChannelConfig> _appToChannel = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AppPolicy> _policyCache = new Dictionary<string, AppPolicy>();

        private NotifyIcon _tray;
        private bool _exiting;
        private bool _startHidden;
        private bool _keepMenuOpen;
        private string _dpiNote;
        private readonly DarkRenderer _renderer = new DarkRenderer();

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public MixerForm(IAudioBackend backend, MixerSettings settings, bool startHidden, bool demo)
        {
            Backend = backend;
            _settings = settings;
            ApplyFeatures14Statics(settings);   // 2.20: eigene Farben, Schraeglage, Armauflage (vor dem Farbschema)
            ApplyGpuStatics(settings, demo);    // 3.0: Grafikkarte (mit Absturz-Erkennung)
            ApplyPerf302Statics(settings, demo); // 3.0.2: Pegel lueckenlos/weich
            ApplyFeatures310Statics(settings, demo); // 3.1: Optik
            ApplyFeatures310bStatics(settings, demo); // 3.1: Optik (Teil 2)
            ApplyFeatures320Statics(settings, demo);  // 3.2: Aussehen und Bedienung
            ApplyFeatures330Statics(settings, demo);  // 3.3: Optik
            ApplyFeatures340Statics(settings, demo);  // 3.4: Ton, Bedienung, Optik
            OutputStrip.DbMode = settings.OutputDbOn || (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTDB") == "1");   // 3.4.3
            ApplyFeatures346Statics(settings, demo);   // 3.4.6
            ApplyFeatures350Statics(settings, demo);   // 3.5
            ApplyFeatures400Statics(settings, demo);   // 4.0
            if (demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DRAGW") is string dw && dw.Length > 0)   // nur Vorschau: "1:180,3:230"
            {
                settings.DragWidthOff = false;
                foreach (var part in dw.Split(','))
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[0], out int ci) && int.TryParse(kv[1], out int px) && ci >= 0 && ci < settings.Channels.Count)
                        settings.Channels[ci].WidthPx = px;
                }
            }
            Theme.ApplyScheme(settings.ColorScheme, settings.AccentColor, settings.UiStyle);
            StripBase.SignalLeds = !settings.SignalLedOff;
            StripBase.PeakLeds = !settings.PeakLedOff;
            StripBase.GrOn = !settings.GrOff;
            StripBase.VuFall = settings.VuFall ?? 1;
            StripBase.VuStartDb = settings.VuStartDb ?? MixerSettings.VuStartDefault;
            StripBase.VuPeakNeedle = !settings.VuPeakNeedleOff;
            StripBase.VuPeakLed = !settings.VuPeakLedOff;
            StripBase.MotorFaders = !settings.MotorOff;
            ChannelStrip.FadeKeys = !settings.FadeKeyOff;
            Theme.MeterStyle = settings.MeterColors;
            Theme.LcdStyle = settings.LcdStyle;
            StripBase.ColorCaps = !settings.ColorCapsOff;
            StripBase.GreetingOn = !settings.GreetingOff;
            Theme.VuDbScale = !settings.VuScaleVu;
            Theme.VuFace = settings.VuFace;
            StripBase.GlowOn = !settings.GlowOff;
            StripBase.PeakCountOn = settings.PeakCountOn;
            StripBase.SetLedThresholds(settings.SigLedDb ?? MixerSettings.SigLedDefault, settings.PeakLedDb ?? MixerSettings.PeakLedDefault);
            StripBase.MuteBlink = !settings.MuteBlinkOff;
            ApplyFeatures7Statics();
            ApplyFeatures8Statics();
            ApplyFeatures9Statics();
            ApplyFeatures10Statics();
            ApplyFeatures11Statics();
            ApplyFeatures13Statics();
            StripBase.PeakDb = !settings.PeakDbOff;
            StripBase.ShowNumbers = !settings.NumbersOff;
            StripBase.HoverLight = !settings.HoverLightOff;
            StripBase.VuOn = !settings.VuOff;
            StripBase.SelfTestOn = !settings.SelfTestOff;
            StripBase.ArmSelfTest();
            if (Win32.DpiAwareness() == 0)
                _dpiNote = "Unscharf: Windows rechnet das Fenster hoch – Abhilfe unter OPTIONEN → Allgemein";
            Theme.ApplyGhost(settings.GhostPct ?? Theme.GhostDefault);
            _startHidden = startHidden || settings.MiniOn;
            _demo = demo;
            if (demo)
            {
                // Demo: feste "Jetzt laeuft"-Anzeige (erfundener Titel)
                string np = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_NOWPLAYING");
                if (np != "0")
                {
                    var parts = (string.IsNullOrEmpty(np) ? "Neon Harbor|Midnight Drive (Extended Mix)" : np).Split('|');
                    _nowPlaying.Fixed = new NowPlayingInfo { Artist = parts.Length > 1 ? parts[0] : null, Title = parts[parts.Length - 1], App = "Spotify",
                        Playing = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_NPPAUSE") != "1" };
                }
            }
            Icons = new IconCache(Theme.Px(20));

            Text = demo ? "Mischpult (Demo) – made by " + Program.Author : "Mischpult – made by " + Program.Author;
            BackColor = BodyColor;
            ForeColor = Theme.Text;
            Font = Theme.Ui(12f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            MinimumSize = new Size(TopBar.MinWidth, Theme.Px(620));
            try { Icon = LoadAppIcon(); } catch { }

            _top = new TopBar { Dock = DockStyle.Top };
            _top.OptionsClicked += () => ShowSettings();
            _top.OptionsMenu += ShowOptionsMenu;
            _top.DuckClicked += () => ToggleDuck(false);
            _top.DuckMenu += () => { if (_settings.DuckPresetsOn) ShowDuckMenu430(); else ShowDuckDialog(); };   // 4.3: Vorlagen
            _top.ScenesClicked += ShowScenesMenu;
            _top.RecClicked += ToggleRecording;
            _top.RecMenu += ShowRecordMenu;
            _top.NightClicked += () => ToggleNight(false);
            _top.MiniClicked += () => SetMini(true);
            _top.MediaClicked += id => SendMedia(id, false);
            _top.KeyPressed += KeyClick;
            _top.DuckOn = _settings.DuckOn;
            _top.NightOn = _settings.NightMode;

            _channelHost = new StripHost { Dock = DockStyle.Fill, Title = "KANÄLE", WidthOf = WidthOf };
            _channelHost.KindOf = c => c is ChannelStrip || c is MicStrip ? "ch" : c is GroupStrip ? "grp" : null;
            _channelHost.Numbered = true;
            _channelHost.Reordered = OnChannelsReordered;
            _outputHost = new StripHost { Dock = DockStyle.Right, Title = "AUSGANG", WidthOf = WidthOf };
            _master = new OutputStrip(this);
            _mic = new MicStrip(this);
            _streamStrip = new StreamStrip(this);
            _noiseStrip = new NoiseStrip(this);   // 3.5
            _outputHost.SetItems(new List<Control> { _master });
            _divider = new Divider { Dock = DockStyle.Right, Width = Theme.Px(12) };
            _addButton = new AddStripButton();
            _addButton.Click += (s, e) => { if (!AddChannelMenu350()) AddChannel(); };   // 3.5: mit Vorlagen ein kleines Menue
            _bridge = new MeterBridge { Dock = DockStyle.Top, Visible = !_settings.BridgeOff, Strips = BridgeStrips };
            _bridgeShown = !_settings.BridgeOff;
            _bridge.MenuRequested += ShowBridgeMenu;
            _bridge.SpectrumClicked += ShowSpecSourceMenu;
            _bridge.Spectrum = _spectrum;
            _bridge.SpectrumOn = !settings.SpecOff;
            _bridge.SpectrumHost = SpectrumHostStrip;
            _top.ClockOn = !settings.ClockOff;
            _top.ClockStudio = settings.ClockStyle == 0;
            _top.ClockAnalog = settings.ClockStyle == 2;
            Theme.AnalogClock = settings.ClockStyle == 2;
            Theme.ClockKind = settings.ClockStyle;
            _top.ClockKind = settings.ClockStyle;
            _bridge.ClockOn = !settings.ClockOff && !settings.BridgeClockOff;
            _top.ClockClicked += ShowClockMenu;

            // Holz-Seitenteile (nur Design Vintage): ueber die ganze Hoehe unter der Kopfleiste, links und rechts
            _cheekL = new WoodCheek(true) { Dock = DockStyle.Left, Width = Theme.Px(18), Visible = Theme.EffCheek != 0 };
            _cheekR = new WoodCheek(false) { Dock = DockStyle.Right, Width = Theme.Px(18), Visible = Theme.EffCheek != 0 };
            CreateAmbilight();
            _bridge.GonioOn = !_settings.GonioOff;
            InitFeatures9();
            InitFeatures11();
            InitFeatures12();
            InitFeatures14();

            // Reihenfolge wichtig fuer das Andocken: Fill zuerst, dann rechts, dann oben
            Controls.Add(_channelHost);
            Controls.Add(_divider);
            Controls.Add(_outputHost);
            Controls.Add(_bridge);
            Controls.Add(_cheekL);
            Controls.Add(_cheekR);
            Controls.Add(_arm);     // 2.20: Armauflage ueber die ganze Breite, unter den Seitenteilen
            Controls.Add(_ambiB);   // Leuchtleisten ganz aussen (Ambilight)
            Controls.Add(_ambiL);
            Controls.Add(_ambiR);
            Controls.Add(_top);
            StartActivityWatch();
            if (!string.IsNullOrEmpty(_settings.BgImagePath) && LoadBgSource(_settings.BgImagePath)) MarkBgDirty();

            RestoreWindow();
            TopMost = _settings.AlwaysOnTop;

            RebuildAppMap();
            BuildChannelStrips();
            PushPolicy();

            Backend.SetMicLiveMeter(_settings.MicLiveMeter);
            Backend.SetSoftFade(!_settings.NoSoftMute);
            Backend.SetHearingCap(_settings.HearingCap);
            _pathMuted = _settings.PttMode == 1 || (_settings.PttMode == 0 && _settings.VoicePathMuted);
            PushVoice();

            _uiTimer.Tick += (s, e) => Tick();
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveNow(); };

            CreateTray();
            if (Win32.IsWindows)
            {
                try { Application.AddMessageFilter(new LockFilter(this)); } catch { }   // 3.2: Pult sperren (vor dem Mausrad-Umleiter)
                try { Application.AddMessageFilter(new WheelRedirector(this)); } catch { }
            }
        }

        private static int WidthOf(Control c)
        {
            if (c is StripBase s) return s.StripWidth;
            if (c is AddStripButton a) return a.StripWidth;
            return c.Width;
        }

        private static Icon LoadAppIcon()
        {
            using (var st = typeof(MixerForm).Assembly.GetManifestResourceStream("Mischpult.app.ico"))
                return st != null ? new Icon(st) : null;
        }

        // ================================================================ Fenster

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
            ApplyHotkeys();
            ApplyPerf();   // Takt: genauer Zeitgeber (sonst der normale Timer), Vorrang, schnelle Pegel
            if (_settings.MiniOn) BeginInvoke(new Action(() => SetMini(true)));
            foreach (var sp in _settings.Sounds) Decode(sp); // Sounds im Hintergrund laden
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _hotkeys.UnregisterAll();
            base.OnHandleDestroyed(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateOutputHostWidth();
            StripBase.StartSelfTest();
            try
            {
                // Einschalt-Gruss: Laufschrift durch alle Anzeigen
                var area = RectangleToScreen(ClientRectangle);
                StripBase.StartGreeting("MISCHPULT " + Program.Version + "   ·   MADE BY " + Program.Author.ToUpperInvariant(), area.Left, area.Right);
            }
            catch { }
            string sp = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SETTINGS") : null;   // nur Demo: Einstellungen gleich zeigen
            if (!string.IsNullOrEmpty(sp)) BeginInvoke(new Action(() =>
            {
                ShowSettings(sp == "1" ? null : sp);
                string q = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SEARCH");
                if (!string.IsNullOrEmpty(q)) _settingsWin?.SetSearch(q);
            }));
            if (_settings.VuWinOpen && !_demo) BeginInvoke(new Action(() => ShowVuWindow(false)));   // war beim Beenden offen
            if ((_settings.BridgeWinOpen && !_demo) || (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEWIN") == "1"))
                BeginInvoke(new Action(ShowBridgeWindow));
            UpdatePultDim(true);
            if (_demo)
            {
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUWIN") == "1") BeginInvoke(new Action(ShowVuWindow));
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUWIN") == "2") BeginInvoke(new Action(ShowSpectrumWindow));
                // nur Demo/Vorschau: DIM an, FADE auf einem Kanal, Szene laden (Motorfader)
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DIM") == "1") BeginInvoke(new Action(() => ToggleDim(false)));
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FADE"), out int fi) && fi >= 0 && fi < _settings.Channels.Count)
                {
                    var ft = new Timer { Interval = 3000 };   // nach dem Selbsttest und der Spiel-Erkennung (die laedt evtl. eine Szene)
                    ft.Tick += (s2, e2) => { ft.Stop(); ft.Dispose(); ToggleFade(_settings.Channels[fi], false); };
                    ft.Start();
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SCENE_AT"), out int sat) && sat > 0 && _settings.Scenes.Count > 1)
                {
                    var t = new Timer { Interval = sat };
                    t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ApplyScene(_settings.Scenes[1], "Szene"); };
                    t.Start();
                }
            }
            string vm = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VUMENU") : null;   // nur Demo: VU-Menue zeigen
            if (vm == "1") BeginInvoke(new Action(() => _master.ShowVuMenuDemo()));
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OUTMETERMENU") == "1") BeginInvoke(new Action(() => _master.ShowOutMeterMenuDemo()));
            string lm = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LUFSMENU") : null;   // nur Demo: LUFS-Menue eines Ausgangs (Geraete-ID) bzw. des MASTER ("1")
            if (!string.IsNullOrEmpty(lm))
                DemoLater(() => { var os = lm != "1" && _outputStrips.TryGetValue(lm, out var o) ? o : _master; os.ShowLufsMenuDemo(); });
            string bm = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEMENU") : null;   // nur Demo: Rechtsklick in die Meterbridge
            if (!string.IsNullOrEmpty(bm)) DemoLater(() => DemoBridgeRightClick(bm));
            if (_demo)
            {
                // 2.17 Vorschau: Hoer-Tagebuch, Spektrum unter der Maus, Song-Menue
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DIARYWIN") == "1") DemoLater(ShowDiaryWindow);
                if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SPECHOVER"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sh))
                    DemoLater(() => _bridge.SpecHoverDemo(sh));
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SONGMENU") == "1")
                    DemoLater(() => { var r = _bridge.ExtraRect('a'); ShowSongMenu(_bridge.PointToScreen(r.IsEmpty ? new Point(400, 60) : new Point((int)r.X, (int)r.Bottom))); });
            }
            string bd = _demo ? Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BRIDGEDRAG") : null;   // nur Demo: "was,x[,drop]" – wie mit der Maus ziehen
            if (!string.IsNullOrEmpty(bd)) DemoLater(() => DemoBridgeDrag(bd));
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOUDMENU") == "1")
                DemoLater(() => { if (_bridge.LoudShown) ShowLoudHistMenu(_bridge.PointToScreen(_bridge.LoudMenuPoint)); });
            ShownFeatures12();
            ShownFeatures13();
            ShownFeatures14();
            ShownGpu();
            EnsureStatsOverlay();   // 3.0.2: Bild-Statistik
            ShownFeatures310();     // 3.1
            ShownFeatures320();     // 3.2
            ShownFeatures330();     // 3.3
            ShownFeatures340();     // 3.4
        }

        /// <summary>Nur Vorschau: etwas spaeter ausfuehren (wenn Zuege und Meterbridge stehen).</summary>
        private void DemoLater(Action a)
        {
            var t = new Timer { Interval = 1500 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { a(); } catch { } };
            t.Start();
        }

        private void RestoreWindow()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            if (_settings.WinX == int.MinValue || _settings.WinW <= 0)
                try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; } catch { }   // dort geht das Fenster auf (wie beim Ermitteln der Skalierung)
            int w = _settings.WinW > 0 ? _settings.WinW : Math.Min(wa.Width - 80, Theme.Px(1100));
            int h = _settings.WinH > 0 ? _settings.WinH : Math.Min(wa.Height - 60, Theme.Px(860));
            if (_settings.UiLevel < 28 && _settings.WinH > 0 && !_settings.BridgeOff)
            {
                // erster Start mit 2.8: Platz fuer Meterbridge und SIG/PEAK-Zeile schaffen, wenn der Bildschirm es hergibt
                var scr = Screen.AllScreens.FirstOrDefault(sc => sc.WorkingArea.Contains(_settings.WinX + 40, _settings.WinY + 10))?.WorkingArea ?? wa;
                h = Math.Min(scr.Height - 20, h + Theme.Px(MeterBridge.HeightPx + StripBase.LedRowH));
                if (_settings.WinY != int.MinValue && _settings.WinY + h > scr.Bottom) _settings.WinY = Math.Max(scr.Y, scr.Bottom - h);
            }
            _settings.UiLevel = 28;
            var bounds = new Rectangle(_settings.WinX, _settings.WinY, w, h);
            bool onScreen = _settings.WinX != int.MinValue && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(bounds.X + 40, bounds.Y + 10, 100, 40)));
            if (onScreen)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(w, h);
            }
            if (_settings.WinMax) WindowState = FormWindowState.Maximized;
        }

        private void RememberWindow()
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (_drawerState != 0 && _drawerRestore.Width >= 100) b = new Rectangle(_drawerRestore.Location, Size);   // 3.4.6: Schublade – normale Lage merken
            if (b.Width < 100) return;
            _settings.WinX = b.X; _settings.WinY = b.Y; _settings.WinW = b.Width; _settings.WinH = b.Height;
            _settings.WinMax = WindowState == FormWindowState.Maximized;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RememberWindow();
            ScheduleSave();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_top != null && _top.WindowButtons && _top.WindowMaximized != (WindowState == FormWindowState.Maximized))
            {
                _top.WindowMaximized = WindowState == FormWindowState.Maximized;
                _top.Invalidate();
            }
            MarkBgDirty();
            UpdateBridgeVisibility();
            UpdateOutputHostWidth();
            if (WindowState == FormWindowState.Minimized && _settings.CloseToTray && _tray != null && !_demo)
                Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (TryPowerOffShow(e)) return;   // 3.3: Ausschalt-Show
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray && _tray != null)
            {
                e.Cancel = true;
                RememberWindow();
                Hide();
                if (!_settings.TrayHintShown)
                {
                    _settings.TrayHintShown = true;
                    ScheduleSave();
                    try { _tray.ShowBalloonTip(4000, "Mischpult läuft weiter", "Das Mischpult ist jetzt im Infobereich (unten rechts). Beenden über Rechtsklick auf das Symbol.", ToolTipIcon.Info); } catch { }
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPerf();
            try { StopSaver(); } catch { }   // 2.18: Bildschirmschoner-Fenster nicht als "offen" merken
            try { CleanupFeatures14(); } catch { }   // 2.20: OpenRGB trennen
            GpuDraw.Disarm();                          // 3.0: sauber beendet = kein Absturz
            try { CleanupPerf302(); } catch { }        // 3.0.2: Spiele-Vorrang zuruecknehmen
            try { CleanupFeatures340(); } catch { }    // 3.4: Maus-Haken, Messung, Fenster
            _closing = true;   // VU-Fenster bleibt fuer den naechsten Start als "offen" gemerkt
            try { CloseVuWindow(); } catch { }
            try { CloseBridgeWindow(); } catch { }
            try { SaveDiary(); } catch { }
            try { Backend.SetMasterSpectrum(null); } catch { }
            CleanupFeatures2();
            CleanupFeatures();
            if (_sleepDb != 0 || _bgQuietApplied != 0 || _duckApplied != 0 || _dimOn || _fades.Count > 0)
            {
                _sleepDb = 0; _bgQuietApplied = 0; _duckApplied = 0;
                _dimOn = false;
                FinishAllFades();   // ausblendende Kanaele stumm, einblendende voll
                PushPolicy(); // keine Absenkung in Windows zuruecklassen
            }
            // Solo aufheben, damit nach dem Beenden keine App "versehentlich" stumm bleibt
            if (_settings.Channels.Any(c => c.Solo))
            {
                foreach (var c in _settings.Channels) c.Solo = false;
                PushPolicy();
            }
            try { Backend.Flush(800); } catch { }
            RememberWindow();
            SaveNow();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            base.OnFormClosed(e);
        }

        public void ExitApp()
        {
            _exiting = true;
            Close();
        }

        public void RestoreFromTray()
        {
            if (DrawerRestore346()) return;   // 3.4.6: Schublade – herausfahren statt einfach zeigen
            if (_mini != null && _mini.Visible)
            {
                _mini.Hide();
                _settings.MiniOn = false;
                ScheduleSave();
            }
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            if (Win32.IsWindows) { try { Win32.SetForegroundWindow(Handle); } catch { } }
        }

        protected override void WndProc(ref Message m)
        {
            if (WndProcTitle(ref m)) return;   // 3.1: eigene Titelleiste
            if (WndProc346(ref m)) return;     // 3.4.6: Taskleisten-Knoepfe, Befehle aus der Sprungliste
            if (Program.ShowMessage != 0 && m.Msg == Program.ShowMessage)
            {
                long other = m.WParam.ToInt64();
                if (Program.BuildStamp > 0 && other > Program.BuildStamp && !_demo)
                {
                    // Eine neuere Version wurde gestartet: Platz machen (sauber beenden, sie startet dann selbst)
                    Program.SignalHandover();
                    BeginInvoke(new Action(ExitApp));
                    return;
                }
                RestoreFromTray();
                return;
            }
            if (Program.QuitMessage != 0 && m.Msg == Program.QuitMessage && !_demo)   // 4.0: Deinstallieren
            {
                BeginInvoke(new Action(ExitApp));
                return;
            }
            if (m.Msg == Win32.WM_DPICHANGED)
            {
                // Fenster auf einen Bildschirm mit anderer Skalierung gezogen: bleibt scharf, nur die Groesse passt erst nach einem Neustart.
                // Nicht an Windows/WinForms weitergeben, damit nichts halb umskaliert wird.
                m.Result = IntPtr.Zero;
                int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                int cur = (int)Math.Round(Theme.S / Math.Max(0.5f, (_settings.Zoom ?? 100) / 100f) * 96f);   // 3.2: ohne den eigenen Zoom
                _dpiNote = dpi > 0 && Math.Abs(dpi - cur) > 2
                    ? "Bildschirm mit " + (int)Math.Round(dpi * 100f / 96f) + " % Skalierung – Größe passt nach Neustart"
                    : null;
                return;
            }
            if (m.Msg == 0x007E /* WM_DISPLAYCHANGE */) _displayChanged = true;   // 3.0.2: Bildwiederholrate neu lesen
            if (m.Msg == Win32.WM_HOTKEY)
            {
                var action = _hotkeys.ActionFor(m.WParam.ToInt32());
                if (action != null) try { ExecuteHotkey(action); } catch { }
                return;
            }
            base.WndProc(ref m);
        }

        private void CreateTray()
        {
            try
            {
                var menu = NewMenu(false);
                BuildTrayMenu(menu);
                menu.Opening += (s, e) => { try { BuildTrayMenu(menu); } catch { } };
                _tray = new NotifyIcon
                {
                    Icon = Icon ?? SystemIcons.Application,
                    Text = "Mischpult – made by " + Program.Author,
                    ContextMenuStrip = menu,
                    Visible = true
                };
                _tray.MouseClick += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    if (TrayClick340()) return;   // 3.4: Schnell-Regler
                    if (Visible && WindowState != FormWindowState.Minimized) Hide();
                    else if (_mini != null && _mini.Visible) _mini.Activate();
                    else RestoreFromTray();
                };
            }
            catch { _tray = null; }
        }

        private void UpdateOutputHostWidth()
        {
            if (_outputHost == null) return;
            int want = _outputHost.ContentWidth;
            int max = Math.Max(Theme.Px(140), ClientSize.Width / 2);
            int w = Math.Min(want, max);
            if (_outputHost.Items.Count == 0) w = Theme.Px(130);
            if (_outputHost.Width != w) _outputHost.Width = w;
        }

        // ================================================================ Kanaele

        private void BuildChannelStrips()
        {
            var old = new Dictionary<ChannelConfig, ChannelStrip>();
            foreach (var s in _channelStrips) old[s.Config] = s;
            _channelStrips.Clear();
            var items = new List<Control>();
            bool micHere = MicInChannels, micPlaced = false;
            if (micHere && _settings.MicAfter == "^") { items.Add(_mic); micPlaced = true; }
            foreach (var c in _settings.Channels)
            {
                if (ChannelShown(c))
                {
                    if (!old.TryGetValue(c, out var strip)) strip = new ChannelStrip(this, c);
                    else old.Remove(c);
                    _channelStrips.Add(strip);
                    items.Add(strip);
                }
                if (micHere && !micPlaced && c.Id == _settings.MicAfter) { items.Add(_mic); micPlaced = true; }
            }
            if (micHere && !micPlaced) items.Add(_mic);
            var oldGroups = new Dictionary<GroupConfig, GroupStrip>();
            foreach (var g in _groupStrips) oldGroups[g.Config] = g;
            _groupStrips.Clear();
            foreach (var g in _settings.Groups)
            {
                if (!oldGroups.TryGetValue(g, out var gs)) gs = new GroupStrip(this, g);
                else oldGroups.Remove(g);
                _groupStrips.Add(gs);
                items.Add(gs);
            }
            if (_settings.NoiseOn) items.Add(_noiseStrip);   // 3.5: Einschlaf-Geraeusche
            items.Add(_addButton);
            _channelHost.SetItems(items);
            foreach (var s in old.Values) s.Dispose();
            foreach (var g in oldGroups.Values) g.Dispose();
            RefreshChannelViews();
        }

        private void RebuildAppMap()
        {
            _appToChannel.Clear();
            foreach (var c in _settings.Channels)
                if (!c.IsCatchAll)
                    foreach (var a in c.Apps) _appToChannel[a] = c;
        }

        private ChannelConfig _catchAll;
        private ChannelConfig CatchAll => _catchAll != null && _settings.Channels.Contains(_catchAll)
            ? _catchAll : (_catchAll = _settings.Channels.First(c => c.IsCatchAll));

        private ChannelConfig ChannelOf(string appKey) =>
            appKey != null && _appToChannel.TryGetValue(appKey, out var c) ? c : CatchAll;

        /// <summary>Wird von Kanalzuegen aufgerufen, wenn sich etwas geaendert hat.</summary>
        public void OnChannelChanged(ChannelConfig c, bool structural)
        {
            if (structural)
            {
                RebuildAppMap();
                RefreshChannelViews();
            }
            PushPolicy();
            ScheduleSave();
        }

        private void PushPolicy()
        {
            bool anySolo = _settings.Channels.Any(c => c.Solo);
            var map = new Dictionary<string, AppPolicy>(StringComparer.OrdinalIgnoreCase);
            var eqChannels = new List<AppPolicy>();
            AppPolicy fallback = null;
            foreach (var c in _settings.Channels)
            {
                // automatische Absenkungen: Ducking, Spiel im Hintergrund, Sleep-Timer, FADE, DIM
                float extraDb = 0f;
                if (_duckApplied < 0 && !_settings.DuckFreqOn && _settings.DuckTargets.Contains(c.Id) && !_settings.IsDuckTrigger(c.Id)) extraDb += _duckApplied;   // 4.0 finale: Frequenz-Ducking senkt nur den Sprachbereich
                if (_bgQuietApplied < 0 && c.Id == _settings.GameChannel) extraDb += _bgQuietApplied;
                if (_sleepDb < 0) extraDb += _sleepDb;
                extraDb += FadeDbFor(c);
                extraDb += BgAppsDbFor(c);   // 3.2: Hintergrund-Apps leiser
                if (_dimOn) extraDb -= DimDb;
                float loudBass = LoudOn ? LoudBassFor(c) : 0f;
                extraDb -= loudBass * 0.6f;   // Loudness: Platz nach oben lassen, sonst verzerren laute Stellen (Bass wird angehoben)
                ClampToCap(c);   // 3.2: Lautstaerke-Deckel (auch wenn MIDI oder eine Szene hoeher stellt)
                extraDb += ExtraDb400(c);   // 4.0: Zeit-Limit, Fokus-Timer
                extraDb += ExtraDbPro400(c);   // 4.0 finale: Ducking-Regeln, Auto-Mix, Crossfader
                float vol = FaderLaw.PosToGain(XfadePos(c)) * GroupGain(c, out bool groupMute) * SoftStartGain();   // 3.2: Szenen ueberblenden; 3.4: sanfter Start
                if (extraDb < 0) vol *= (float)Math.Pow(10, extraDb / 20.0);
                bool mute = XfadeMute(c) || groupMute || (anySolo && !c.Solo) || MuteGroupHit400(c);   // 4.0 finale: Mute-Gruppen
                bool comp = c.CompOn || _settings.NightMode;
                bool lev = c.AutoOn;
                float[] geq = c.EqOn && !GraphicEq.IsFlat(c.Geq) ? c.Geq : null;
                // Loudness: solange sie an ist, laufen alle Kanaele ueber den EQ-Weg (kein Umschalten beim Lautstaerke-Drehen)
                bool loud = LoudOn;
                float loudTreble = LoudTreble(loudBass);
                bool force = ForceDsp(c);   // 2.19: Hall/Echo, Vorhoeren, Ausgangs-Klang, Mehrspur-Aufnahme (3.2: mono, getauscht, Verzoegerung)
                EqSettings eq = (c.EqOn || comp || lev || loud || force) && _snap.EqSupported
                    ? new EqSettings(c.Id, Math.Min(18f, (c.EqOn ? c.EqLow : 0f) + loudBass), c.EqOn ? c.EqMid : 0f,
                                     Math.Min(18f, (c.EqOn ? c.EqHigh : 0f) + loudTreble), comp, lev, geq, FxFor(c), CueFor(c), StereoOf(c), DelayOf(c)) : null;
                if (eq != null) eq = eq.WithExtra(ExtraFor(c));   // 3.4.6: Stereo-Breite, Bass-Filter, Knall-Bremse
                // Ein Ausgang: alle Apps aufs Standardgeraet. Mehrere: Ausgang des Kanals.
                string device = MultiOutput ? c.OutputDevice : null;
                bool enforce = MultiOutput ? (!c.IsCatchAll || c.OutputDevice != null) : true;
                if (!_policyCache.TryGetValue(c.Id, out var p) || !p.SameAs(vol, mute, c.Pan, device, enforce, eq, c.Id))
                {
                    p = new AppPolicy(vol, mute, c.Pan, device, enforce, eq, c.Id);
                    _policyCache[c.Id] = p;
                }
                if (p.Eq != null) eqChannels.Add(p);
                if (c.IsCatchAll) fallback = p;
                else foreach (var a in c.Apps) map[a] = PerApp(a, p);
            }
            // Apps im Sammelkanal (SYSTEM) mit eigener Lautstaerke
            if (fallback != null)
            {
                foreach (var v in _settings.AppVolumes)
                    if (!map.ContainsKey(v.Key)) map[v.Key] = PerApp(v.Key, fallback);
                if (_adKey != null && !map.ContainsKey(_adKey)) map[_adKey] = PerApp(_adKey, fallback);   // 3.5
                if (_sysQuiet430 && !map.ContainsKey(AppKeys.System)) map[AppKeys.System] = PerApp(AppKeys.System, fallback);   // 4.3: Windows-Toene stumm beim Zocken
            }
            Backend.SetPolicy(new PolicyTable(map, fallback, eqChannels.ToArray()));
            PushStream();
            PushVoice();
            PushOutputFx();   // 2.19
            PushCue();

            foreach (var s in _channelStrips)
                UpdateStripState(s, anySolo);
        }

        private readonly Dictionary<string, KeyValuePair<AppPolicy, AppPolicy>> _appPolicyCache =
            new Dictionary<string, KeyValuePair<AppPolicy, AppPolicy>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Kanal-Policy mit der eigenen Lautstaerke der App (gleiches Objekt, solange sich nichts aendert).</summary>
        private AppPolicy PerApp(string key, AppPolicy channel)
        {
            float pos = AppPos(key);
            if (AdMuted(key) || SysQuiet430(key)) pos = 0f;   // 3.5: Werbung stumm; 4.3: Windows-Toene beim Zocken
            if (pos >= 0.9995f) return channel;
            float gain = FaderLaw.PosToGain(pos);
            if (_appPolicyCache.TryGetValue(key, out var kv) && ReferenceEquals(kv.Key, channel) && kv.Value.AppGain == Math.Max(0f, Math.Min(1f, gain)))
                return kv.Value;
            var p = channel.WithAppGain(gain);
            _appPolicyCache[key] = new KeyValuePair<AppPolicy, AppPolicy>(channel, p);
            return p;
        }

        /// <summary>Eigene Lautstaerke der App im Kanal als Faderposition (1 = 0 dB = wie der Kanal).</summary>
        public float AppPos(string key)
        {
            if (key == null) return 1f;
            foreach (var v in _settings.AppVolumes)
                if (string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)) return v.Pos;
            return 1f;
        }

        public void SetAppPos(string key, float pos, bool save = true)
        {
            if (key == null) return;
            key = key.ToLowerInvariant();
            pos = Math.Max(0f, Math.Min(1f, pos));
            var v = _settings.AppVolumes.FirstOrDefault(x => x.Key == key);
            if (pos >= 0.9995f) { if (v != null) _settings.AppVolumes.Remove(v); }
            else if (v == null) _settings.AppVolumes.Add(new AppVolume { Key = key, Pos = pos });
            else v.Pos = pos;
            PushPolicy();
            foreach (var s in _channelStrips) if (s.Apps.Any(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))) s.InvalidateChrome();
            if (save) ScheduleSave();
        }

        /// <summary>App lauter/leiser per Mausrad (1 dB, mit Strg 0,25 dB). Gibt die neue Position zurueck.</summary>
        public float NudgeApp(string key, int delta, bool fine)
        {
            float pos = AppPos(key);
            float db = FaderLaw.PosToDb(pos);
            if (float.IsNegativeInfinity(db)) db = -60f;
            db += delta / 120f * (fine ? 0.25f : 1f);
            float np = db < -59.9f && delta < 0 ? 0f : FaderLaw.DbToPos(Math.Min(0f, db));
            SetAppPos(key, np);
            return np;
        }

        /// <summary>Pegel einer App: [hoerbar L, R, eigentlich L, R].</summary>
        public float[] AppLevels(string key)
        {
            if (key != null && _appsByKey.TryGetValue(key, out var a))
                return new[] { a.PeakL, a.PeakR, Math.Max(a.PreL, a.PeakL), Math.Max(a.PreR, a.PeakR) };
            return new float[4];
        }

        private AppMixPopup _appMix;

        /// <summary>Kleines Mischfenster mit einem Regler pro App des Kanals.</summary>
        public void ShowAppMix(ChannelStrip strip)
        {
            bool same = _appMix != null && !_appMix.IsDisposed && _appMix.Channel == strip.Config;
            try { _appMix?.Close(); } catch { }
            _appMix = null;
            if (same) return; // zweiter Klick schliesst
            var p = new AppMixPopup(this, strip);
            var anchor = strip.PointToScreen(new Point(Theme.Px(4), Theme.Px(88)));
            var wa = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(wa.Left + 4, Math.Min(anchor.X, wa.Right - p.Width - 4));
            int y = Math.Max(wa.Top + 4, Math.Min(anchor.Y, wa.Bottom - p.Height - 4));
            p.Location = new Point(x, y);
            p.FormClosed += (o, e) => { if (_appMix == p) _appMix = null; };
            _appMix = p;
            p.UpdateLevels(0f);
            p.Show(this);
        }

        private void UpdateStripState(ChannelStrip s, bool anySolo)
        {
            var c = s.Config;
            string label, tip;
            bool missing = false;
            if (c.OutputDevice == null)
            {
                var def = _snap.Devices.FirstOrDefault(d => d.IsDefault);
                label = "Standard";
                tip = "Ausgang: Windows-Standardgerät" + (def != null ? "\n(zurzeit: " + def.FullName + ")" : "") + "\nKlicken, um ein anderes Gerät zu wählen";
            }
            else
            {
                var dev = _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, c.OutputDevice, StringComparison.OrdinalIgnoreCase));
                if (dev != null)
                {
                    bool ambiguous = _snap.Devices.Count(d => string.Equals(d.Name, dev.Name, StringComparison.CurrentCultureIgnoreCase)) > 1;
                    label = ambiguous ? dev.FullName : dev.Name;
                    tip = "Ausgang: " + dev.FullName + "\nDie Apps dieses Kanals werden auf dieses Gerät umgeleitet.";
                }
                else
                {
                    label = (c.OutputDeviceName ?? "Gerät") + " ✕";
                    tip = "Ausgang: " + (c.OutputDeviceName ?? "unbekanntes Gerät") + "\nGerät ist gerade nicht angeschlossen – Windows nutzt solange das Standardgerät.";
                    missing = true;
                }
            }
            if (!_snap.RoutingSupported && c.OutputDevice != null) tip += "\n\nHinweis: Umleitung wird auf diesem System nicht unterstützt.";
            s.SetState((anySolo && !c.Solo && !c.Mute) || (!c.Mute && MuteGroupHit400(c)), _snap.EqSupported, _snap.EqStatus, label, tip, missing);
        }

        private void RefreshChannelViews()
        {
            foreach (var s in _channelStrips)
            {
                var list = new List<AppView>();
                if (s.Config.IsCatchAll)
                {
                    foreach (var a in _snap.Apps)
                        if (!_appToChannel.ContainsKey(a.Key))
                            list.Add(new AppView { Key = a.Key, Name = a.Name, Path = a.Path, Running = true, Active = a.Active });
                }
                else
                {
                    foreach (var key in s.Config.Apps)
                    {
                        if (_appsByKey.TryGetValue(key, out var a))
                            list.Add(new AppView { Key = a.Key, Name = a.Name, Path = a.Path, Running = true, Active = a.Active });
                        else
                        {
                            var k = FindKnown(key);
                            if (k != null) list.Add(new AppView { Key = key, Name = k.Name, Path = k.Path, Running = false });
                        }
                    }
                }
                s.Apps = list;
                s.RefreshTint();   // 3.1: Kanalfarbe aus dem App-Symbol
                s.InvalidateChrome();
            }
        }

        private KnownApp FindKnown(string key) =>
            _settings.KnownApps.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

        private void Remember(string key, string name, string path)
        {
            if (key == null) return;
            var k = FindKnown(key);
            if (k == null)
            {
                _settings.KnownApps.Add(new KnownApp { Key = key.ToLowerInvariant(), Name = name, Path = path });
                ScheduleSave();
            }
            else if ((name != null && k.Name != name) || (path != null && k.Path != path))
            {
                if (name != null) k.Name = name;
                if (path != null) k.Path = path;
                ScheduleSave();
            }
        }

        private static string PrettyKey(string key)
        {
            if (key == AppKeys.System) return "Systemklänge";
            string n = key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? key.Substring(0, key.Length - 4) : key;
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : key;
        }

        private void AddChannel()
        {
            int idx = _settings.Channels.Count;
            int color = MixerSettings.Palette[(idx + 3) % (MixerSettings.Palette.Length - 1)];
            var c = ChannelConfig.Create("KANAL " + idx, color);
            if (_settings.BanksOn) c.Bank = _settings.CurrentBank;   // 2.18: in die gerade gezeigte Bank
            _settings.Channels.Add(c);
            BuildChannelStrips();
            PushPolicy();
            ScheduleSave();
            var strip = _channelStrips.Last();
            _channelHost.ScrollIntoView(_addButton);
            BeginInvoke(new Action(() => strip.BeginRename()));
        }

        private void DeleteChannel(ChannelConfig c)
        {
            if (c.IsCatchAll) return;
            if (MessageBox.Show(this, "Kanal »" + c.Name + "« löschen?\nDie zugeordneten Apps landen wieder im Kanal »" + CatchAll.Name + "«.",
                    "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int idx = _settings.Channels.IndexOf(c);
            if (_settings.MicAfter == c.Id) _settings.MicAfter = idx > 0 ? _settings.Channels[idx - 1].Id : "^"; // Mikro bleibt an seinem Platz
            _settings.Channels.Remove(c);
            _policyCache.Remove(c.Id);
            if (_settings.GameChannel == c.Id) _settings.GameChannel = null;
            foreach (var g in _settings.Groups) g.Members.Remove(c.Id);
            if (_settings.RecordTarget == "ch:" + c.Id) _settings.RecordTarget = null;
            _settings.Midi.RemoveAll(b => b.Target != null && b.Target.StartsWith("ch:" + c.Id + ":"));
            RebuildAppMap();
            BuildChannelStrips();
            PushPolicy();
            ScheduleSave();
        }

        /// <summary>Neue Reihenfolge nach dem Ziehen per Maus uebernehmen (Kanaele, Mikro, Gruppen).</summary>
        private void OnChannelsReordered(List<Control> order)
        {
            ChannelStrip.Renaming?.EndRename(true);
            var oldList = _settings.Channels.ToList();

            // Ausgeblendete Kanaele bleiben hinter dem sichtbaren Kanal, hinter dem sie vorher standen
            var lead = new List<ChannelConfig>();
            var behind = new Dictionary<ChannelConfig, List<ChannelConfig>>();
            ChannelConfig prev = null;
            foreach (var c in oldList)
            {
                if (ChannelShown(c)) { prev = c; continue; }
                if (prev == null) lead.Add(c);
                else
                {
                    if (!behind.TryGetValue(prev, out var l)) behind[prev] = l = new List<ChannelConfig>();
                    l.Add(c);
                }
            }

            var newList = new List<ChannelConfig>(lead);
            var visible = order.Where(c => c is ChannelStrip || c == _mic).ToList();
            bool micSeen = false;
            string micAfter = null;
            ChannelConfig last = null;
            foreach (var item in visible)
            {
                if (item == _mic) { micSeen = true; micAfter = last == null ? "^" : last.Id; continue; }
                var cfg = ((ChannelStrip)item).Config;
                if (newList.Contains(cfg)) continue;
                newList.Add(cfg);
                if (behind.TryGetValue(cfg, out var hs)) newList.AddRange(hs);
                last = cfg;
            }
            foreach (var c in oldList) if (!newList.Contains(c)) newList.Add(c); // nichts verlieren
            _settings.Channels.Clear();
            _settings.Channels.AddRange(newList);
            if (micSeen) _settings.MicAfter = visible.Count > 0 && visible[visible.Count - 1] == _mic ? null : micAfter;

            var groups = order.OfType<GroupStrip>().Select(g => g.Config).Where(g => _settings.Groups.Contains(g)).ToList();
            foreach (var g in _settings.Groups) if (!groups.Contains(g)) groups.Add(g);
            _settings.Groups.Clear();
            _settings.Groups.AddRange(groups);

            BuildChannelStrips();
            ScheduleSave();
        }

        private void MoveChannel(ChannelConfig c, int dir)
        {
            int i = _settings.Channels.IndexOf(c);
            int j = i + dir;
            if (i < 0 || j < 0 || j >= _settings.Channels.Count) return;
            _settings.Channels.RemoveAt(i);
            _settings.Channels.Insert(j, c);
            BuildChannelStrips();
            ScheduleSave();
        }

        private void AssignApp(string key, ChannelConfig target, string name = null, string path = null)
        {
            key = key.ToLowerInvariant();
            NoteGameAssignment(key, ChannelOf(key), target);
            foreach (var c in _settings.Channels) c.Apps.RemoveAll(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            if (target != null && !target.IsCatchAll) target.Apps.Add(key);
            if (_appsByKey.TryGetValue(key, out var st)) Remember(key, st.Name, st.Path);
            else if (name != null || path != null) Remember(key, name ?? PrettyKey(key), path);
            OnChannelChanged(target ?? CatchAll, true);
        }

        // ================================================================ Timer / Snapshot

        private void Tick()
        {
            if (_closing || IsDisposed) return;   // ein schon angestossener Takt nach dem Schliessen
            FrameStats.BeginFrame();   // 3.0.2: Abstand der Bilder messen
            long t0 = Stopwatch.GetTimestamp();
            try { TickCore(); }
            finally { FrameStats.AddTick(Stopwatch.GetTimestamp() - t0); }   // Rechenzeit pro Bild (Einstellungen → Leistung)
            if (PaintNowOn) PaintNowAll();   // 3.0: das neue Bild gleich zeichnen, nicht erst wenn Windows Zeit hat
            TickFrames302();
        }

        private void TickCore()
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)(now - _lastTick);
            _lastTick = now;

            var snap = Backend.Snapshot;
            FrameStats.AddData(snap != _snap);
            TickBackground();   // 3.4.1
            snap.Taken = true;   // 3.0.2: abgeholt – die Engine sammelt die Spitzen ab jetzt fuer den naechsten
            Backend.RefreshLevels(snap);   // Ausgaenge und Mikro: frisch gemessen (schneller Mess-Faden)
            _snap = snap;
            ApplyOutputNames(snap);   // 3.2: eigene Namen der Ausgaenge
            _appsByKey.Clear();
            foreach (var a in snap.Apps) _appsByKey[a.Key] = a;

            bool structural = snap.Structure != _lastStructure;
            if (structural)
            {
                _lastStructure = snap.Structure;
                OnStructureChanged();
            }
            if (snap.EqSupported != _lastEqSupported)
            {
                _lastEqSupported = snap.EqSupported;
                PushPolicy();
            }
            CheckSchedules();

            TickPerf(now);
            bool visible = PultShown;   // hinter einem Vollbild-Spiel wird nicht gezeichnet (Ton und Automatik laufen weiter)
            bool bridgeWin = BridgeWinShown;   // 2.16: Meterbridge im Fenster braucht die Pegel auch, wenn das Pult nicht zu sehen ist
            StripBase.Offscreen = !visible;     // dann rechnen die Zuege, zeichnen aber nicht
            bool meters = visible || bridgeWin;

            // Kanalpegel = Maximum der direkt laufenden Apps + Pegel des EQ-Wegs.
            // [0..1] hoerbar (nach Fader/Stumm), [2..3] was eigentlich laeuft (davor)
            var peaks = new Dictionary<ChannelConfig, float[]>();
            _eqPathNow.Clear(); _directNow.Clear();
            foreach (var a in snap.Apps)
            {
                if (a.Captured) { if (a.Active) _eqPathNow.Add(ChannelOf(a.Key)); continue; } // Session laeuft bei -80 dB, Pegel kommt vom EQ-Mischer
                var c = ChannelOf(a.Key);
                if (a.Active) _directNow.Add(c);
                if (!peaks.TryGetValue(c, out var p)) peaks[c] = p = new float[4];
                p[0] = Math.Max(p[0], a.PeakL);
                p[1] = Math.Max(p[1], a.PeakR);
                p[2] = Math.Max(p[2], Math.Max(a.PreL, a.PeakL));
                p[3] = Math.Max(p[3], Math.Max(a.PreR, a.PeakR));
            }
            foreach (var c in _settings.Channels)
            {
                peaks.TryGetValue(c, out var p);
                float l = p?[0] ?? 0f, r = p?[1] ?? 0f, pl = p?[2] ?? 0f, pr = p?[3] ?? 0f;
                if (snap.ChannelPeaks != null && snap.ChannelPeaks.TryGetValue(c.Id, out var e))
                {
                    l = Math.Max(l, e[0]);
                    r = Math.Max(r, e[1]);
                    pl = Math.Max(pl, e.Length >= 4 ? e[2] : e[0]);
                    pr = Math.Max(pr, e.Length >= 4 ? e[3] : e[1]);
                }
                HoldAcrossPathSwitch(c, now, ref l, ref r, ref pl, ref pr);   // 3.4.2
                _channelLevelDb[c.Id] = MeterBallistics.ToDb(Math.Max(l, r));
                _channelPeak[c.Id] = new[] { l, r, pl, pr };
            }
            if (meters)
            {
                foreach (var s in _channelStrips)
                {
                    var pk = _channelPeak[s.Config.Id];
                    s.UpdateMeter(pk[0], pk[1], pk[2], pk[3], dt);
                    s.CheckLcdTimeout();
                }
                foreach (var gs in _groupStrips)
                {
                    var g4 = GroupPeak(gs.Config);
                    var members = new List<KeyValuePair<string, Color>>();
                    foreach (var c in _settings.Channels)
                        if (gs.Config.Members.Contains(c.Id)) members.Add(new KeyValuePair<string, Color>(c.Name, Color.FromArgb(c.Color)));
                    gs.SetMembers(members);
                    gs.UpdateMeter(g4[0], g4[1], g4[2], g4[3], dt);
                }
            }

            if (_appMix != null && !_appMix.IsDisposed && _appMix.Visible) _appMix.UpdateLevels(dt);
            UpdateDucking(now, dt);
            Tick430(now, dt);   // 4.3: wer loest aus, Halte-Taste, Timer, Windows-Toene, Akku
            TickFeatures(now, dt);
            TickFeatures2(now, dt);
            TickFeatures4(dt);
            TickFeatures7(dt);
            TickFeatures8(dt);
            TickFeatures9(dt);
            TickFeatures11(dt);
            TickFeatures12(now);
            TickFeatures13(now);
            TickFeatures14(now, dt);
            TickFeatures310(now, dt);   // 3.1
            TickFeatures320(now, dt);   // 3.2
            TickFeatures330();          // 3.3
            TickFeatures340();          // 3.4
            TickFeatures346(now);       // 3.4.6
            TickFeatures350(now);       // 3.5
            TickFeatures400(now);       // 4.0
            if (!meters) return;

            if (_settings.StreamOn)
            {
                UpdateStreamStrip();
                _streamStrip.UpdateMeter(snap.StreamPeak?[0] ?? 0f, snap.StreamPeak?[1] ?? 0f, dt);
            }
            if (_settings.NoiseOn) _noiseStrip.UpdateMeter(snap.NoisePeak?[0] ?? 0f, snap.NoisePeak?[1] ?? 0f, dt);   // 3.5
            _mic.SetExtras(AnyPadPlaying,
                _settings.VoiceOn && _settings.VoiceDevice != null ? (snap.Devices.FirstOrDefault(d => string.Equals(d.Id, _settings.VoiceDevice, StringComparison.OrdinalIgnoreCase))?.Name ?? _settings.VoiceDeviceName ?? "Sprach-Ausgang") : null,
                CurrentVoiceFx().IsOff ? 0 : _settings.VoiceOn && _settings.VoiceDevice != null ? 2 : 1);
            _mic.SetPathMute(PathMute ? (bool?)_pathMuted : null);

            _mic.SetMic(snap.Mic, snap.MicLive, HotkeyManager.Format(HotkeyKeys("mic.mute")));
            _mic.SetPtt(_settings.PttMode, PttDialog.KeyName(_settings.PttKeys), _pttHeld, _micHardMute);
            var m4 = MicPeak();
            _mic.UpdateMeter(m4[0], m4[1], m4[2], m4[3], dt);

            if (MultiOutput)
            {
                foreach (var d in snap.Devices)
                {
                    if (_outputStrips.TryGetValue(d.Id, out var os))
                    {
                        os.SetDevice(d, null, false);
                        var d4 = DevicePeak(d);
                        os.UpdateMeter(d4[0], d4[1], d4[2], d4[3], dt);
                    }
                }
            }
            else
            {
                var def = DefaultDevice;
                _master.SetDevice(def, null, false);
                var d4 = DevicePeak(def);
                _master.UpdateMeter(d4[0], d4[1], d4[2], d4[3], dt);
            }
            if (visible && _bridge.Visible) _bridge.Tick();
            if (bridgeWin) _bridgeWin.Bridge.Tick();
        }

        /// <summary>Alle Zuege fuer die Meterbridge (Kanaele, dann Ausgaenge).</summary>
        private IEnumerable<StripBase> BridgeStrips()
        {
            foreach (var c in _channelHost.Items) if (c is StripBase s) yield return s;
            foreach (var c in _outputHost.Items) if (c is StripBase s) yield return s;
        }

        /// <summary>Gruppe: [hoerbar L, R, eigentlich L, R] = Maximum der Kanaele.</summary>
        private float[] GroupPeak(GroupConfig g)
        {
            var r = new float[4];
            foreach (var id in g.Members)
                if (_channelPeak.TryGetValue(id, out var pk))
                    for (int i = 0; i < 4 && i < pk.Length; i++) r[i] = Math.Max(r[i], pk[i]);
            return r;
        }

        /// <summary>Mikro: Windows misst vor der Stummschaltung -> farbig nur, wenn das Mikro wirklich sendet.</summary>
        private float[] MicPeak()
        {
            var m = _snap.Mic;
            if (m == null) return new float[4];
            bool off = PathMute ? _pathMuted : m.Mute;
            return new[] { off ? 0f : m.PeakL, off ? 0f : m.PeakR, m.PeakL, m.PeakR };
        }

        /// <summary>Ausgang: Windows misst vor der Geraete-Lautstaerke -> farbig = mal Lautstaerke (stumm = 0).</summary>
        private static float[] DevicePeak(DeviceState d)
        {
            if (d == null) return new float[4];
            float g = d.Mute ? 0f : (float)Math.Pow(10, Math.Max(-100f, Math.Min(0f, d.VolumeDb)) / 20.0);
            return new[] { d.PeakL * g, d.PeakR * g, d.PeakL, d.PeakR };
        }

        private DeviceState DefaultDevice => _snap.Devices.FirstOrDefault(d => d.IsDefault) ?? _snap.Devices.FirstOrDefault();

        private void OnStructureChanged()
        {
            DetectGames();

            // bekannte Namen/Pfade fuer zugeordnete Apps merken
            foreach (var a in _snap.Apps)
                if (_appToChannel.ContainsKey(a.Key) && a.Path != null)
                    Remember(a.Key, a.Name, a.Path);

            RefreshChannelViews();
            SyncOutputs();
            bool anySolo = _settings.Channels.Any(c => c.Solo);
            foreach (var s in _channelStrips) UpdateStripState(s, anySolo);

            if (_snap.Error != null) { _baseStatus = _snap.Error; _baseWarning = true; }
            else if (_snap.Devices.Length == 0 && _snap.Structure != "") { _baseStatus = "Kein Ausgabegerät gefunden"; _baseWarning = true; }
            else if (_snap.EqStatus != null && (_settings.LoudOn || _settings.Channels.Any(c => c.EqOn || c.CompOn || c.AutoOn || _settings.NightMode)) && _snap.Structure != "")
            { _baseStatus = _snap.EqStatus; _baseWarning = true; }
            else if (MultiOutput && !_snap.RoutingSupported && _snap.Structure != "" && _settings.Channels.Any(c => c.OutputDevice != null))
            { _baseStatus = "App-Umleitung auf diesem Windows nicht verfügbar"; _baseWarning = true; }
            else
            {
                int active = _snap.Apps.Count(a => a.Active);
                bool demoNote = _demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_QUIETSTATUS") != "1";
                _baseStatus = demoNote ? "Demo-Modus – kein echtes Audio" : active + (active == 1 ? " App spielt" : " Apps spielen");
                _baseWarning = demoNote;
            }
            UpdateStatus();
            OnStructureFeatures();
        }

        private List<AppView> AppsOn(DeviceState d, bool includeCaptured)
        {
            var apps = new List<AppView>();
            foreach (var a in _snap.Apps)
            {
                if (!a.Active) continue;
                bool here = (includeCaptured && a.Captured) || d == null ||
                            (a.DeviceIds != null && a.DeviceIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase));
                if (here) apps.Add(new AppView { Key = a.Key, Name = a.Name, Path = a.Path, Running = true, Active = true });
            }
            return apps;
        }

        private void SyncOutputs()
        {
            var items = new List<Control>();
            bool micHere = !_settings.HideMic && _settings.MicWithOutputs;
            if (micHere) items.Add(_mic);
            if (!MultiOutput)
            {
                foreach (var os in _outputStrips.Values) os.Dispose();
                _outputStrips.Clear();
                items.Add(_master);
                var def = DefaultDevice;
                _master.SetDevice(def, AppsOn(def, true), true);
            }
            else
            {
                var visible = _snap.Devices.Where(d => !_settings.HiddenOutputs.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in visible)
                {
                    if (!_outputStrips.TryGetValue(d.Id, out var os))
                    {
                        os = new OutputStrip(this, d.Id);
                        _outputStrips[d.Id] = os;
                    }
                    keep.Add(d.Id);
                    items.Add(os);
                    os.SetDevice(d, AppsOn(d, false), true);
                }
                foreach (var id in _outputStrips.Keys.ToList())
                {
                    if (keep.Contains(id)) continue;
                    var os = _outputStrips[id];
                    _outputStrips.Remove(id);
                    os.Dispose();
                }
            }

            if (_settings.StreamOn) { items.Add(_streamStrip); UpdateStreamStrip(); }

            bool same = items.Count == _outputHost.Items.Count;
            for (int i = 0; same && i < items.Count; i++) same = items[i] == _outputHost.Items[i];
            if (!same)
            {
                _outputHost.Title = (micHere ? "MIKRO · " : "") + (MultiOutput ? "AUSGÄNGE" : "AUSGANG") + (_settings.StreamOn ? " · STREAM" : "");
                _outputHost.SetItems(items);
                UpdateOutputHostWidth();
            }
        }

        /// <summary>Zwischen "ein Master" und "mehrere Ausgaenge" umschalten.</summary>
        private void SetMultiOutput(bool on)
        {
            _settings.MultiOutput = on;
            foreach (var s in _channelStrips) s.Relayout();
            _master.Relayout();
            _mic.Relayout();
            SyncOutputs();
            foreach (var os in _outputStrips.Values) os.Relayout();
            UpdateBridgeVisibility();
            PushPolicy();
            ScheduleSave();
        }

        // ================================================================ Ducking

        private void UpdateDucking(double now, float dt)
        {
            if (dt > 0.2f) dt = 0.2f;
            float target = 0f;
            _duckVoice = false;
            bool any = false, evaluate = ++_leakTick % 15 == 0;   // ~4x pro Sekunde neu bewerten
            foreach (var trig in _settings.DuckTriggerList)       // 4.2.1: auch mehrere Ausloeser – einer reicht
            {
                if (trig == MixerSettings.DuckSbId)   // 4.3: Soundboard – loest aus, solange ein Sound spielt
                {
                    any = true;
                    if (AnyPadPlaying) { _duckVoice = true; _duckFrom = trig; _duckFired423[trig] = now; }
                    continue;
                }
                float lvl = DuckLevelDb(trig);
                if (lvl <= -999f) continue;
                any = true;
                if (_dimOn && trig != MixerSettings.DuckMicId) lvl += DimDb;   // DIM macht nur leiser – Ducking soll trotzdem auf Stimmen reagieren
                float others = -100f;
                foreach (var kv in _channelLevelDb)
                    if (!_settings.IsDuckTrigger(kv.Key) && kv.Value > others) others = kv.Value;
                var leak = LeakFor(trig);
                leak.Add(lvl, others);
                if (evaluate) leak.Evaluate();

                // Nur echte Stimmen zaehlen – nicht der gespiegelte PC-Ton
                bool voice = leak.IsVoice(lvl, others, _settings.DuckThreshold, !_settings.DuckNoLeakFilter);
                voice = TalkGate423(trig, voice, dt);   // 4.2.3: kurze Geraeusche ignorieren
                if (voice) { _duckVoice = true; _duckFrom = trig; _duckFired423[trig] = now; }
            }
            bool allowed = DuckAllowed430();   // 4.3: nur, wenn ein Spiel laeuft / zu bestimmten Uhrzeiten
            _duckWaits430 = any && _settings.DuckOn && !allowed && _duckVoice;
            if (any && _settings.DuckOn && allowed)
            {
                if (_duckVoice) _duckHoldUntil = now + _settings.DuckHold;
                if (now < _duckHoldUntil) target = _settings.DuckAmount;
            }
            if (HoldDuck430) target = Math.Min(target, _settings.DuckAmount);   // 4.3: Taste gedrueckt halten
            DuckRates423(_settings.DuckAmount, out float down, out float up);           // 4.2.3: Uebergang einstellbar
            if (_duckDb > target) _duckDb = Math.Max(target, _duckDb - down * dt);      // schnell leiser
            else if (_duckDb < target) _duckDb = Math.Min(target, _duckDb + up * dt);   // langsam wieder lauter

            float q = (float)Math.Round(_duckDb * 2) / 2f;
            if (q != _duckApplied)
            {
                _duckApplied = q;
                PushPolicy();
            }
            bool active = q < -0.25f;
            if (active != _duckActive)
            {
                _duckActive = active;
                _top.DuckActive = active;
                _top.Invalidate();
                RefreshDuckLeds400();   // 4.0 finale: auch Ducking-Regeln
            }
        }

        /// <summary>
        /// Pegel eines Ausloesers in dB (-1000 = gibt es nicht). 4.2.2: auch das Mikrofon – nur wenn es wirklich sendet
        /// (stumm oder Push-to-Talk nicht gedrueckt = still). Es wird nur der Pegel gelesen, nichts aufgenommen.
        /// </summary>
        private float DuckLevelDb(string trig)
        {
            if (trig == MixerSettings.DuckSbId) return AnyPadPlaying ? 0f : -100f;   // 4.3: Soundboard
            if (trig == MixerSettings.DuckMicId)
            {
                if (_snap?.Mic == null) return -1000f;
                var m = MicPeak();
                return MeterBallistics.ToDb(Math.Max(m[0], m[1]));
            }
            return _channelLevelDb.TryGetValue(trig ?? "", out float lvl) ? lvl : -1000f;
        }

        /// <summary>Name eines Ausloesers fuer Anzeigen.</summary>
        private string DuckTriggerName(string trig) => trig == MixerSettings.DuckMicId ? "MIKRO" : trig == MixerSettings.DuckSbId ? "SOUNDBOARD" : trig == HoldId430 ? "TASTE" : ChannelById(trig)?.Name ?? "?";

        /// <summary>4.2.1: Ausloeser, der zuletzt eine Stimme hatte (fuer die Ducking-Linien).</summary>
        private string _duckFrom;
        private readonly Dictionary<string, LeakDetector> _leaks = new Dictionary<string, LeakDetector>();

        /// <summary>Jeder Ausloeser misst seinen eigenen PC-Ton-Anteil (der erste wie bisher).</summary>
        private LeakDetector LeakFor(string trig)
        {
            if (trig == _settings.DuckTrigger) return _leak;
            if (!_leaks.TryGetValue(trig, out var l)) _leaks[trig] = l = new LeakDetector();
            return l;
        }

        /// <summary>Kurzinfo fuer das Ducking-Fenster.</summary>
        private string DuckInfo()
        {
            if (_settings.DuckNoLeakFilter) return "";
            var trigs = _settings.DuckTriggerList;
            foreach (var t in trigs)
            {
                var l = t == _settings.DuckTrigger ? _leak : _leaks.TryGetValue(t ?? "", out var x) ? x : null;
                if (l == null || !l.Present) continue;
                string name = trigs.Count > 1 ? " „" + DuckTriggerName(t) + "“" : "";
                return "Im Auslöser" + name + " steckt PC-Ton (" + l.Offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                       " dB) – wird ignoriert" + (_duckVoice ? " · Stimme erkannt" : "");
            }
            return "";
        }

        /// <summary>Pegel und Messart einer App fuer Tooltips.</summary>
        public string AppLevelInfo(string key)
        {
            if (!_appsByKey.TryGetValue(key, out var a) || !a.Active) return "";
            float db = MeterBallistics.ToDb(Math.Max(Math.Max(a.PreL, a.PreR), Math.Max(a.PeakL, a.PeakR)));
            string lv = a.Captured ? "über EQ" : db <= -99f ? "still" : db.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
            return "  [" + lv + (a.Captured ? "" : a.Metered ? ", gemessen" : ", Windows-Pegel") + "]";
        }

        private void ToggleDuck(bool osd)
        {
            _settings.DuckOn = !_settings.DuckOn;
            _top.DuckOn = _settings.DuckOn;
            _top.Invalidate();
            ScheduleSave();
            if (osd) ShowOsd("Ducking", _settings.DuckOn ? "AN" : "AUS", -1, _settings.DuckOn ? Theme.LedGreen : Theme.TextDim);
        }

        public void ShowDuckDialog()
        {
            using (var dlg = new DuckDialog(_settings, id => Math.Max(-100f, DuckLevelDb(id)), DuckInfo, DuckLamp423))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var trigs = dlg.Triggers;
                if (!trigs.SequenceEqual(_settings.DuckTriggerList)) { _leak.Reset(); _leaks.Clear(); }
                _settings.DuckNoLeakFilter = !dlg.LeakFilter;
                _settings.DuckOn = dlg.DuckOn;
                _settings.DuckTrigger = trigs[0];
                _settings.DuckTriggers = trigs.Count > 1 ? trigs : null;   // 4.2.1: mehrere Ausloeser
                _settings.DuckTargets = dlg.Targets;
                _settings.DuckAmount = dlg.Amount;
                _settings.DuckThreshold = dlg.Threshold;
                _settings.DuckHold = dlg.Hold;
                // 4.2.3: Feinschliff
                _settings.DuckLampsOn = dlg.LampsOn;
                _settings.DuckMinTalkOn = dlg.MinTalkOn;
                _settings.DuckMinTalkMs = dlg.MinTalkMs;
                _settings.DuckFadeOn = dlg.FadeOn;
                _settings.DuckDownMs = dlg.DownMs;
                _settings.DuckUpMs = dlg.UpMs;
                _top.DuckOn = _settings.DuckOn;
                _top.Invalidate();
                _duckActive = !_duckActive; // Anzeige im naechsten Tick neu setzen
                PushPolicy();
                ScheduleSave();
            }
        }

        // ================================================================ Hotkeys

        private int HotkeyKeys(string action) => _settings.Hotkeys.FirstOrDefault(h => h.Action == action)?.Keys ?? 0;

        private void ApplyHotkeys()
        {
            if (!IsHandleCreated) return;
            _hotkeys.Apply(Handle, _settings.Hotkeys.Concat(HotkeyExtras340()).ToList());   // 3.4: Lautstaerketasten
        }

        private List<HotkeyAction> HotkeyActions()
        {
            var list = new List<HotkeyAction>
            {
                new HotkeyAction("app.toggle", "Mischpult zeigen / verstecken", "Allgemein"),
                new HotkeyAction("mic.mute", "Mikrofon stumm an/aus", "Allgemein"),
                new HotkeyAction("master.mute", "Ausgang stumm an/aus", "Allgemein"),
                new HotkeyAction("master.up", "Ausgang lauter", "Allgemein"),
                new HotkeyAction("master.down", "Ausgang leiser", "Allgemein"),
                new HotkeyAction("duck.toggle", "Ducking an/aus", "Allgemein"),
                new HotkeyAction("night.toggle", "Nachtmodus an/aus", "Allgemein"),
                new HotkeyAction("dim.toggle", "DIM: alles leiser an/aus", "Allgemein"),
                new HotkeyAction("loud.toggle", "Loudness an/aus", "Allgemein"),
                new HotkeyAction("vu.window", "Großes VU-Meter zeigen/schließen", "Allgemein"),
                new HotkeyAction("rec.toggle", "Aufnahme starten/stoppen", "Allgemein"),
                new HotkeyAction("mini.toggle", "Mini-Ansicht an/aus", "Allgemein"),
                new HotkeyAction("sb.stop", "Soundboard: alles stoppen", "Allgemein"),
            };
            if (_settings.StreamOn) list.Add(new HotkeyAction("stream.mute", "Stream-Mix stumm an/aus", "Allgemein"));
            list.Add(new HotkeyAction("media.play", "Musik: Play / Pause", "Musik"));
            list.Add(new HotkeyAction("media.next", "Musik: nächster Titel", "Musik"));
            list.Add(new HotkeyAction("media.prev", "Musik: vorheriger Titel", "Musik"));
            foreach (var sp in _settings.Sounds)
                list.Add(new HotkeyAction("sb:" + sp.Id, "Sound: " + sp.Name, "Soundboard"));
            foreach (var g in _settings.Groups)
            {
                list.Add(new HotkeyAction("grp:" + g.Id + ":mute", g.Name + " stumm an/aus", "Gruppen"));
                list.Add(new HotkeyAction("grp:" + g.Id + ":up", g.Name + " lauter", "Gruppen"));
                list.Add(new HotkeyAction("grp:" + g.Id + ":down", g.Name + " leiser", "Gruppen"));
            }
            foreach (var sc in _settings.Scenes)
                list.Add(new HotkeyAction("scene:" + sc.Id, "Szene laden: " + sc.Name, "Szenen"));
            foreach (var c in _settings.Channels)
            {
                list.Add(new HotkeyAction("ch:" + c.Id + ":mute", c.Name + " stumm an/aus", "Kanäle"));
                list.Add(new HotkeyAction("ch:" + c.Id + ":fade", c.Name + " langsam aus/ein (FADE)", "Kanäle"));
                list.Add(new HotkeyAction("ch:" + c.Id + ":up", c.Name + " lauter", "Kanäle"));
                list.Add(new HotkeyAction("ch:" + c.Id + ":down", c.Name + " leiser", "Kanäle"));
            }
            AddHotkeyActions12(list);
            AddHotkeyActions310(list);   // 3.1
            AddHotkeyActions310b(list);
            AddHotkeyActions320(list);   // 3.2
            AddHotkeyActions330(list);   // 3.3
            AddHotkeyActions340(list);   // 3.4
            AddHotkeyActions350(list);   // 3.5
            AddHotkeyActions400(list);   // 4.0
            return list;
        }

        public void ShowHotkeyDialog()
        {
            var failed = _hotkeys.Failed.ToList();
            _hotkeys.UnregisterAll(); // waehrend der Eingabe nichts abfangen
            try
            {
                using (var dlg = new HotkeyDialog(HotkeyActions(), _settings.Hotkeys, failed))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                    {
                        _settings.Hotkeys = dlg.Result;
                        ScheduleSave();
                    }
                }
            }
            finally
            {
                ApplyHotkeys();
                _mic.InvalidateChrome();
                if (_hotkeys.Failed.Count > 0)
                    MessageBox.Show(this, "Diese Tastenkürzel sind schon von einem anderen Programm belegt:\n\n" +
                        string.Join("\n", _hotkeys.Failed.Select(a => "• " + (HotkeyActions().FirstOrDefault(x => x.Id == a)?.Label ?? a))),
                        "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowOsd(string title, string value, float bar, Color accent)
        {
            try
            {
                if (_osd == null || _osd.IsDisposed) _osd = new OsdForm();
                _osd.ShowMessage(title, value, bar, accent);
            }
            catch { }
        }

        private long _mediaSentMs = -10000;

        /// <summary>Medientaste senden ("play", "next", "prev"). osd = kurze Einblendung (bei Hotkey/MIDI).</summary>
        public void SendMedia(string id, bool osd)
        {
            byte vk = id == "next" ? Win32.VK_MEDIA_NEXT_TRACK : id == "prev" ? Win32.VK_MEDIA_PREV_TRACK : Win32.VK_MEDIA_PLAY_PAUSE;
            // Ist genau diese Medientaste als Hotkey belegt, wuerde das Mischpult sie selbst wieder abfangen:
            // dann kurz abmelden (und doppelte Ausloesung vermeiden)
            bool bound = _settings.Hotkeys.Any(h => (h.Keys & 0xFF) == vk);
            long now = Environment.TickCount;
            if (bound)
            {
                if (Math.Abs(now - _mediaSentMs) < 250) return;
                _mediaSentMs = now;
                if (IsHandleCreated) _hotkeys.Apply(Handle, _settings.Hotkeys.Where(h => (h.Keys & 0xFF) != vk).ToList());
                Win32.SendMediaKey(vk);
                var t = new Timer { Interval = 300 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); ApplyHotkeys(); };
                t.Start();
            }
            else Win32.SendMediaKey(vk);
            if (osd) ShowOsd("Musik", id == "next" ? "Nächster Titel" : id == "prev" ? "Vorheriger Titel" : "Play / Pause", -1, Theme.Accent);
        }

        private void ExecuteHotkey(string action)
        {
            if (ExecuteHotkey12(action)) return;
            if (ExecuteHotkey310(action)) return;   // 3.1
            if (ExecuteHotkey310b(action)) return;
            if (ExecuteHotkey320(action)) return;   // 3.2
            if (ExecuteHotkey330(action)) return;   // 3.3
            if (ExecuteHotkey340(action)) return;   // 3.4
            if (ExecuteHotkey350(action)) return;   // 3.5
            if (ExecuteHotkey400(action)) return;   // 4.0
            switch (action)
            {
                case "media.play": SendMedia("play", true); return;
                case "media.next": SendMedia("next", true); return;
                case "media.prev": SendMedia("prev", true); return;
                case "app.toggle":
                    if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus) Hide();
                    else RestoreFromTray();
                    return;
                case "mic.mute":
                    ToggleMicMute(true);
                    return;
                case "night.toggle":
                    ToggleNight(true);
                    return;
                case "rec.toggle":
                    ToggleRecording();
                    return;
                case "mini.toggle":
                    SetMini(!(_mini != null && _mini.Visible));
                    return;
                case "master.mute":
                case "master.up":
                case "master.down":
                {
                    var d = DefaultDevice;
                    if (d == null) return;
                    if (action == "master.mute")
                    {
                        d.Mute = !d.Mute;
                        Backend.SetDeviceMute(d.Id, d.Mute);
                        ShowOsd("Ausgang · " + d.Name, d.Mute ? "STUMM" : "AN", -1, d.Mute ? Theme.MuteRed : Theme.LedGreen);
                    }
                    else
                    {
                        float v = Math.Max(0, Math.Min(1, (float)Math.Round((d.Volume + (action == "master.up" ? 0.02f : -0.02f)) * 50) / 50f));
                        d.Volume = v;
                        Backend.SetDeviceVolume(d.Id, v);
                        ShowOsd("Ausgang · " + d.Name, (int)Math.Round(v * 100) + " %", v, Theme.Accent);
                    }
                    return;
                }
                case "duck.toggle":
                    ToggleDuck(true);
                    return;
                case "dim.toggle":
                    ToggleDim(true);
                    return;
                case "loud.toggle":
                    ToggleLoud(true);
                    return;
                case "vu.window":
                    ToggleVuWindow();
                    return;
            }

            if (action == "sb.stop") { StopAllSounds(); return; }
            if (action == "stream.mute")
            {
                ToggleStreamMute();
                ShowOsd("Stream-Mix", _settings.StreamMute ? "STUMM" : "AN", -1, _settings.StreamMute ? Theme.MuteRed : ChannelStrip.StreamColor);
                return;
            }
            if (action.StartsWith("sb:"))
            {
                var pad = _settings.Sounds.FirstOrDefault(x => "sb:" + x.Id == action);
                if (pad != null) PlayPad(pad);
                return;
            }
            if (action.StartsWith("grp:"))
            {
                var gp = action.Split(':');
                var g = gp.Length == 3 ? _settings.Groups.FirstOrDefault(x => x.Id == gp[1]) : null;
                if (g == null) return;
                if (gp[2] == "mute") { g.Mute = !g.Mute; OnGroupChanged(g); ShowOsd(g.Name, g.Mute ? "STUMM" : "AN", -1, g.Mute ? Theme.MuteRed : Color.FromArgb(g.Color)); }
                else
                {
                    float db = FaderLaw.PosToDb(g.Fader);
                    if (float.IsNegativeInfinity(db)) db = -62f;
                    db += gp[2] == "up" ? 2f : -2f;
                    g.Fader = db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db));
                    OnGroupChanged(g);
                    ShowOsd(g.Name, FaderLaw.FormatDb(g.Fader), g.Fader, Color.FromArgb(g.Color));
                }
                return;
            }
            if (action.StartsWith("scene:"))
            {
                ApplyScene(_settings.Scenes.FirstOrDefault(x => "scene:" + x.Id == action), "Szene");
                return;
            }

            if (action.StartsWith("ch:"))
            {
                var parts = action.Split(':');
                if (parts.Length != 3) return;
                var c = _settings.Channels.FirstOrDefault(x => x.Id == parts[1]);
                if (c == null) return;
                var strip = _channelStrips.FirstOrDefault(s => s.Config == c);
                Color col = Color.FromArgb(c.Color);
                if (parts[2] == "fade")
                {
                    ToggleFade(c, true);
                }
                else if (parts[2] == "mute")
                {
                    _fades.Remove(c.Id);
                    c.Mute = !c.Mute;
                    OnChannelChanged(c, false);
                    strip?.InvalidateChrome();
                    ShowOsd(c.Name, c.Mute ? "STUMM" : "AN", -1, c.Mute ? Theme.MuteRed : col);
                }
                else if (parts[2] == "solo")
                {
                    c.Solo = !c.Solo;
                    OnChannelChanged(c, false);
                    foreach (var s in _channelStrips) s.InvalidateChrome();
                    ShowOsd(c.Name, c.Solo ? "SOLO" : "SOLO AUS", -1, c.Solo ? Theme.SoloAmber : col);
                }
                else
                {
                    float db = FaderLaw.PosToDb(c.Fader);
                    if (float.IsNegativeInfinity(db)) db = -62f;
                    db += parts[2] == "up" ? 2f : -2f;
                    c.Fader = db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db));
                    OnChannelChanged(c, false);
                    strip?.InvalidateChrome();
                    ShowOsd(c.Name, FaderLaw.FormatDb(c.Fader), c.Fader, col);
                }
            }
        }

        // ================================================================ Mikrofon

        public void SetMicLive(bool on)
        {
            _settings.MicLiveMeter = on;
            Backend.SetMicLiveMeter(on);
            ScheduleSave();
        }

        public void ShowMicDeviceMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Mikrofon (Windows-Standard)"));
            m.Items.Add(new ToolStripSeparator());
            if (_snap.CaptureDevices.Length == 0) m.Items.Add(Hint("Kein Aufnahmegerät gefunden"));
            foreach (var d in _snap.CaptureDevices)
            {
                var id = d.Id;
                m.Items.Add(new ToolStripMenuItem(d.FullName, null, (s, e) => Backend.SetDefaultDevice(id)) { Checked = d.IsDefault });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Windows-Soundeinstellungen öffnen", null, (s, e) => OpenUri("ms-settings:sound")));
            m.Show(screen);
        }

        public void ShowMicMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Mikrofon"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Live-Pegel (Mikro offen halten)", null, (s, e) => SetMicLive(!_settings.MicLiveMeter)) { Checked = _settings.MicLiveMeter });
            m.Items.Add(new ToolStripMenuItem("Hotkey ändern …", null, (s, e) => BeginInvoke(new Action(ShowHotkeyDialog))));
            m.Items.Add(new ToolStripMenuItem("Push-to-Talk …", null, (s, e) => BeginInvoke(new Action(ShowPttDialog))) { Checked = _settings.PttMode != 0 });
            m.Items.Add(new ToolStripMenuItem("Mikro-Effekte für Discord …", null, (s, e) => BeginInvoke(new Action(ShowMicFxDialog))) { Checked = !CurrentVoiceFx().IsOff });
            m.Items.Add(new ToolStripSeparator());
            if (MicInChannels)
            {
                var order = VisibleChannelOrder();
                int at = order.IndexOf(null);
                m.Items.Add(new ToolStripMenuItem("Nach links schieben", null, (s, e) => MoveMic(-1)) { Enabled = at > 0 });
                m.Items.Add(new ToolStripMenuItem("Nach rechts schieben", null, (s, e) => MoveMic(+1)) { Enabled = at >= 0 && at < order.Count - 1 });
                m.Items.Add(new ToolStripMenuItem("Zu den Ausgängen stellen", null, (s, e) => SetMicWithOutputs(true)));
            }
            else m.Items.Add(new ToolStripMenuItem("Zu den Kanälen stellen", null, (s, e) => SetMicWithOutputs(false)));
            m.Items.Add(new ToolStripMenuItem("Mikro-Kanal ausblenden", null, (s, e) => SetMicVisible(false)));
            AddBridgeShownItem(m.Items, _mic);
            m.Show(screen);
        }

        /// <summary>MIKRO-Zug steht im Kanal-Bereich.</summary>
        private bool MicInChannels => !_settings.HideMic && !_settings.MicWithOutputs;

        private void SetMicVisible(bool on)
        {
            _settings.HideMic = !on;
            PlaceMic();
            ScheduleSave();
        }

        private void SetMicWithOutputs(bool on)
        {
            _settings.MicWithOutputs = on;
            PlaceMic();
            if (!on) _channelHost.ScrollIntoView(_mic);
            ScheduleSave();
        }

        /// <summary>MIKRO-Zug in den richtigen Bereich setzen (beide Bereiche zusammen aktualisieren).</summary>
        private void PlaceMic()
        {
            BuildChannelStrips();
            SyncOutputs();
            UpdateOutputHostWidth();
        }

        /// <summary>Sichtbare Kanaele in Anzeige-Reihenfolge, null = MIKRO.</summary>
        private List<ChannelConfig> VisibleChannelOrder()
        {
            var list = new List<ChannelConfig>();
            bool placed = false;
            if (_settings.MicAfter == "^") { list.Add(null); placed = true; }
            foreach (var c in _settings.Channels)
            {
                if (ChannelShown(c)) list.Add(c);
                if (!placed && c.Id == _settings.MicAfter) { list.Add(null); placed = true; }
            }
            if (!placed) list.Add(null);
            return list;
        }

        private void MoveMic(int dir)
        {
            var order = VisibleChannelOrder();
            int at = order.IndexOf(null);
            int to = at + dir;
            if (at < 0 || to < 0 || to >= order.Count) return;
            order.RemoveAt(at);
            order.Insert(to, null);
            // Anker = sichtbarer Kanal direkt links vom Mikro
            _settings.MicAfter = to == 0 ? "^" : to == order.Count - 1 ? null : order[to - 1].Id;
            BuildChannelStrips();
            _channelHost.ScrollIntoView(_mic);
            ScheduleSave();
        }

        // ================================================================ Menues

        private ContextMenuStrip NewMenu(bool disposeOnClose = true)
        {
            var m = new ContextMenuStrip
            {
                Renderer = _renderer,
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Font = Theme.Ui(12f),
                ImageScalingSize = new Size(Theme.Px(18), Theme.Px(18)),
                BackColor = Theme.MenuBg,
                ForeColor = Theme.Text,
            };
            m.Closing += (s, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepMenuOpen) e.Cancel = true;
                _keepMenuOpen = false;
            };
            m.Opened += (s, e) => _menusOpen346++;   // 3.4.6: Schublade bleibt offen, solange ein Menue offen ist
            m.Closed += (s, e) => _menusOpen346 = Math.Max(0, _menusOpen346 - 1);
            if (disposeOnClose)
                m.Closed += (s, e) => BeginInvoke(new Action(() => { try { m.Dispose(); } catch { } }));
            return m;
        }

        private static ToolStripMenuItem Header(string text) =>
            new ToolStripMenuItem(text) { Enabled = false, Tag = "header", Font = Theme.Ui(11f, FontStyle.Bold) };

        private static ToolStripMenuItem Hint(string text) =>
            new ToolStripMenuItem(text) { Enabled = false, Tag = "hint", Font = Theme.Ui(11f) };

        private Image IconFor(string key, string name, string path) => Icons.Get(key, name, path).Normal;

        public void ShowAppMenu(ChannelStrip strip, Point screen)
        {
            var c = strip.Config;
            var m = NewMenu();
            m.ShowCheckMargin = !c.IsCatchAll;

            if (c.IsCatchAll)
            {
                m.Items.Add(Header("»" + c.Name + "« – automatisch"));
                m.Items.Add(Hint("Enthält alle Apps ohne eigenen Kanal."));
                m.Items.Add(new ToolStripSeparator());
                var apps = _snap.Apps.Where(a => !_appToChannel.ContainsKey(a.Key)).ToList();
                if (apps.Count == 0) m.Items.Add(Hint("Gerade keine weiteren Apps aktiv."));
                foreach (var a in apps)
                {
                    var item = new ToolStripMenuItem(a.Name + (a.Active ? "" : "  (still)"), IconFor(a.Key, a.Name, a.Path));
                    var move = Header("Verschieben nach …");
                    item.DropDown.Renderer = _renderer;
                    item.DropDownItems.Add(move);
                    foreach (var target in _settings.Channels.Where(x => !x.IsCatchAll))
                    {
                        var t = target;
                        var key = a.Key; var nm = a.Name; var pth = a.Path;
                        item.DropDownItems.Add(new ToolStripMenuItem(t.Name, Swatch(Color.FromArgb(t.Color)), (s, e) => AssignApp(key, t, nm, pth)));
                    }
                    if (item.DropDownItems.Count == 1) item.DropDownItems.Add(Hint("Erst einen Kanal anlegen"));
                    m.Items.Add(item);
                }
            }
            else
            {
                m.Items.Add(Header("Apps für »" + c.Name + "«"));
                m.Items.Add(new ToolStripSeparator());

                var running = _snap.Apps.ToList();
                if (running.Count == 0) m.Items.Add(Hint("Gerade gibt keine App Ton aus."));
                foreach (var a in running)
                {
                    var key = a.Key; var nm = a.Name; var pth = a.Path;
                    var owner = ChannelOf(key);
                    string suffix = owner == c ? "" : owner.IsCatchAll ? "" : "   · " + owner.Name;
                    var item = new ToolStripMenuItem(nm + suffix, IconFor(key, nm, pth)) { Checked = owner == c };
                    item.Click += (s, e) =>
                    {
                        _keepMenuOpen = true;
                        bool nowIn = ChannelOf(key) == c;
                        AssignApp(key, nowIn ? null : c, nm, pth);
                        item.Checked = !nowIn;
                        item.Text = nm;
                    };
                    m.Items.Add(item);
                }

                // Zugeordnet, aber gerade nicht gestartet
                var offline = c.Apps.Where(k => !_appsByKey.ContainsKey(k)).ToList();
                var knownOffline = offline.Where(k => FindKnown(k) != null).ToList();
                var autoOffline = offline.Where(k => FindKnown(k) == null).ToList();
                if (knownOffline.Count > 0)
                {
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(Header("Nicht gestartet"));
                    foreach (var k in knownOffline)
                    {
                        var key = k; var known = FindKnown(k);
                        var item = new ToolStripMenuItem(known.Name, IconFor(key, known.Name, known.Path)) { Checked = true };
                        item.Click += (s, e) =>
                        {
                            _keepMenuOpen = true;
                            bool nowIn = c.Apps.Contains(key);
                            AssignApp(key, nowIn ? null : c);
                            item.Checked = !nowIn;
                        };
                        m.Items.Add(item);
                    }
                }
                if (autoOffline.Count > 0)
                {
                    var sub = new ToolStripMenuItem("Automatisch erkannt (" + autoOffline.Count + ")");
                    sub.DropDown.Renderer = _renderer;
                    sub.DropDownItems.Add(Hint("Werden diesem Kanal zugeordnet, sobald sie laufen:"));
                    foreach (var k in autoOffline)
                    {
                        var key = k;
                        var item = new ToolStripMenuItem(PrettyKey(key) + "  (" + key + ")") { Checked = true };
                        item.Click += (s, e) =>
                        {
                            _keepMenuOpen = true;
                            bool nowIn = c.Apps.Contains(key);
                            AssignApp(key, nowIn ? null : c);
                            item.Checked = !nowIn;
                        };
                        sub.DropDownItems.Add(item);
                    }
                    ((ToolStripDropDownMenu)sub.DropDown).ShowImageMargin = false;
                    ((ToolStripDropDownMenu)sub.DropDown).ShowCheckMargin = true;
                    sub.DropDown.Closing += (s, e) =>
                    {
                        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepMenuOpen) { e.Cancel = true; _keepMenuOpen = false; }
                    };
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(sub);
                }

                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Programm (.exe) auswählen …", null, (s, e) => PickExe(c)));
            }

            m.Show(screen);
        }

        private void PickExe(ChannelConfig c)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Programm für Kanal »" + c.Name + "« auswählen",
                Filter = "Programme (*.exe)|*.exe",
                CheckFileExists = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.FileName;
                string key = AppKeys.FromExe(path);
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription) && vi.FileDescription.Length <= 40) name = vi.FileDescription.Trim();
                }
                catch { }
                Remember(key, name, path);
                AssignApp(key, c, name, path);
            }
        }

        public void ShowChannelMenu(ChannelStrip strip, Point screen)
        {
            var c = strip.Config;
            var m = NewMenu();
            m.Items.Add(Header(c.Name + (c.IsCatchAll ? "  (automatisch)" : "")));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Umbenennen", null, (s, e) => BeginInvoke(new Action(strip.BeginRename))));
            AddFaderItems(m.Items, () => c.FaderLock, v => c.FaderLock = v, () => c.FaderMark, v => c.FaderMark = v, () => c.Fader,
                pos => { c.Fader = pos; OnChannelChanged(c, false); }, strip);

            var colors = new ToolStripMenuItem("Farbe", Swatch(Color.FromArgb(c.Color)));
            colors.DropDown.Renderer = _renderer;
            for (int i = 0; i < MixerSettings.Palette.Length; i++)
            {
                int col = MixerSettings.Palette[i];
                colors.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.PaletteNames[i], Swatch(Color.FromArgb(col)), (s, e) =>
                {
                    c.Color = col;
                    strip.InvalidateChrome();
                    ScheduleSave();
                }) { Checked = c.Color == col });
            }
            m.Items.Add(colors);
            AddChannelImageItems(m.Items, c, strip);
            AddBridgeShownItem(m.Items, strip);
            AddChannelFieldItems(m.Items, c, strip);

            int idx = _settings.Channels.IndexOf(c);
            m.Items.Add(new ToolStripMenuItem("Nach links schieben", null, (s, e) => MoveChannel(c, -1)) { Enabled = idx > 0 });
            m.Items.Add(new ToolStripMenuItem("Nach rechts schieben", null, (s, e) => MoveChannel(c, +1)) { Enabled = idx < _settings.Channels.Count - 1 });
            m.Items.Add(new ToolStripSeparator());
            var presets = Sub("EQ-Presets");
            AddPresetItems(presets.DropDownItems, strip);
            m.Items.Add(presets);
            m.Items.Add(new ToolStripMenuItem("EQ zurücksetzen (flat)", null, (s, e) =>
            {
                c.EqLow = c.EqMid = c.EqHigh = 0f;
                OnChannelChanged(c, false);
                strip.InvalidateChrome();
            }));
            m.Items.Add(new ToolStripMenuItem("Alle Regler zurücksetzen (0 dB, Mitte, EQ/COMP aus)", null, (s, e) =>
            {
                _fades.Remove(c.Id);
                c.Fader = 1f; c.Pan = 0f; c.Mute = false; c.Solo = false;
                c.EqOn = false; c.CompOn = false; c.EqLow = c.EqMid = c.EqHigh = 0f;
                OnChannelChanged(c, false);
                strip.InvalidateChrome();
            }));
            m.Items.Add(new ToolStripMenuItem("Großer EQ (10 Bänder + Spektrum) …", null, (s, e) => ShowBigEq(c)));
            var grp = Sub("Gruppe");
            foreach (var g in _settings.Groups)
            {
                var gg = g;
                grp.DropDownItems.Add(new ToolStripMenuItem(g.Name, Swatch(Color.FromArgb(g.Color)), (s, e) =>
                {
                    if (gg.Members.Contains(c.Id)) gg.Members.Remove(c.Id); else gg.Members.Add(c.Id);
                    OnGroupChanged(gg);
                }) { Checked = g.Members.Contains(c.Id) });
            }
            if (_settings.Groups.Count > 0) grp.DropDownItems.Add(new ToolStripSeparator());
            grp.DropDownItems.Add(new ToolStripMenuItem("Neue Gruppe mit diesem Kanal …", null, (s, e) => AddGroup(new[] { c.Id })));
            m.Items.Add(grp);
            if (_settings.StreamOn)
                m.Items.Add(new ToolStripMenuItem("Im Stream-Mix", null, (s, e) =>
                {
                    c.StreamOff = !c.StreamOff;
                    if (!c.StreamOff && c.StreamDb <= ChannelStrip.StreamOffDb) c.StreamDb = 0f;
                    OnChannelEdited(c);
                }) { Checked = !c.StreamOff && c.StreamDb > ChannelStrip.StreamOffDb });
            if (!_settings.FxOff) m.Items.Add(FxSub(c));   // 2.19
            if (_settings.CueOn) m.Items.Add(new ToolStripMenuItem("Vorhören (CUE)" + (CueActive ? "  → " + (_settings.CueDeviceName ?? "Kopfhörer") : ""), null, (s, e) => SetChannelCue(c, !c.Cue))
            { Checked = c.Cue, Enabled = CueActive || c.Cue });
            if (_settings.BanksOn) m.Items.Add(BankSub(c));   // 2.18
            AddChannel320Items(m.Items, c);   // 3.2: Breite, Deckel, Ton, im Hintergrund leiser
            AddChannel330Items(m.Items, c);   // 3.3: Symbol, Notiz
            AddChannel340Items(m.Items, c, strip, screen);   // 3.4: EQ A/B, zweite Farbe, duplizieren, zuruecksetzen, Aufkleber
            AddChannel430Items(m.Items, c);   // 4.3: Timer
            m.Items.Add(new ToolStripMenuItem("Ausblenden (läuft weiter)", null, (s, e) => SetChannelHidden(c, true)));
            if (!c.IsCatchAll && GameChannel != c)
                m.Items.Add(new ToolStripMenuItem("Als Kanal für Spiele verwenden", null, (s, e) =>
                {
                    _settings.GameChannel = c.Id;
                    ScheduleSave();
                    DetectGames();
                    RefreshChannelViews();
                }));
            if (!c.IsCatchAll)
                m.Items.Add(new ToolStripMenuItem("Kanal löschen", null, (s, e) => BeginInvoke(new Action(() => DeleteChannel(c)))));
            m.Show(screen);
        }

        public void ShowDeviceMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Ausgabegerät (Windows-Standard)"));
            m.Items.Add(new ToolStripSeparator());
            if (_snap.Devices.Length == 0) m.Items.Add(Hint("Kein Ausgabegerät gefunden"));
            foreach (var d in _snap.Devices)
            {
                var id = d.Id;
                var item = new ToolStripMenuItem(d.FullName, null, (s, e) => Backend.SetDefaultDevice(id)) { Checked = d.IsDefault };
                m.Items.Add(item);
            }
            var defDev = _snap.Devices.FirstOrDefault(d => d.IsDefault);   // 2.19: Klang des Standardgeraets
            if (defDev != null && !_settings.OutFxOff)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Header("Klang · " + defDev.Name));
                AddOutputFxItems(m.Items, defDev.Id, defDev.Name);
            }
            if (defDev != null && (!_settings.OutputNamesOff || _settings.TestToneOn)) { m.Items.Add(new ToolStripSeparator()); AddOutput320Items(m.Items, defDev.Id, defDev.Name); }   // 3.2
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Windows-Soundeinstellungen öffnen", null, (s, e) => OpenUri("ms-settings:sound")));
            m.Show(screen);
        }

        public void ShowRouteMenu(ChannelStrip strip, Point screen)
        {
            var c = strip.Config;
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.Items.Add(Header("Ausgang für »" + c.Name + "«"));
            m.Items.Add(new ToolStripSeparator());

            bool supported = _snap.RoutingSupported;
            var def = _snap.Devices.FirstOrDefault(d => d.IsDefault);
            var std = new ToolStripMenuItem("Standard" + (def != null ? "  (" + def.Name + ")" : ""), Glyph("★"))
            {
                Checked = c.OutputDevice == null
            };
            std.Click += (s, e) => SetRoute(c, null, null);
            m.Items.Add(std);
            m.Items.Add(new ToolStripSeparator());
            foreach (var d in _snap.Devices)
            {
                var dev = d;
                var item = new ToolStripMenuItem(d.FullName, Glyph(d.IsDefault ? "◉" : "○"))
                {
                    Checked = string.Equals(c.OutputDevice, d.Id, StringComparison.OrdinalIgnoreCase),
                    Enabled = supported
                };
                item.Click += (s, e) => SetRoute(c, dev.Id, dev.FullName);
                m.Items.Add(item);
            }
            if (c.OutputDevice != null && !_snap.Devices.Any(d => string.Equals(d.Id, c.OutputDevice, StringComparison.OrdinalIgnoreCase)))
                m.Items.Add(new ToolStripMenuItem((c.OutputDeviceName ?? "Gerät") + "  (nicht angeschlossen)") { Checked = true, Enabled = false });

            m.Items.Add(new ToolStripSeparator());
            if (!supported)
                m.Items.Add(Hint("App-Umleitung wird von diesem Windows nicht unterstützt."));
            else
                m.Items.Add(Hint(c.IsCatchAll ? "Gilt für alle Apps ohne eigenen Kanal." : "Windows merkt sich die Umleitung pro App."));
            if (c.EqOn) m.Items.Add(Hint("Mit EQ spielt das Mischpult den Kanal auf diesem Gerät aus."));
            m.Show(screen);
        }

        private void SetRoute(ChannelConfig c, string id, string name)
        {
            c.OutputDevice = id;
            c.OutputDeviceName = name;
            OnChannelChanged(c, false);
            bool anySolo = _settings.Channels.Any(x => x.Solo);
            foreach (var s in _channelStrips) if (s.Config == c) UpdateStripState(s, anySolo);
        }

        public void ShowOutputMenu(OutputStrip strip, Point screen)
        {
            var m = NewMenu();
            var dev = _snap.Devices.FirstOrDefault(d => d.Id == strip.DeviceId);
            m.Items.Add(Header(dev?.FullName ?? "Ausgang"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Als Windows-Standardgerät setzen", null, (s, e) => Backend.SetDefaultDevice(strip.DeviceId))
            { Enabled = dev != null && !dev.IsDefault });
            m.Items.Add(new ToolStripMenuItem("Ausblenden", null, (s, e) =>
            {
                _settings.HiddenOutputs.Add(strip.DeviceId);
                SyncOutputs();
                ScheduleSave();
            }));
            AddBridgeShownItem(m.Items, strip);
            AddOutput320Items(m.Items, strip.DeviceId, dev?.Name);   // 3.2: umbenennen, Testton
            AddCoupleItems400(m.Items, strip.DeviceId);   // 4.0: Ausgaenge koppeln
            AddSticker340Items(m.Items, strip, screen);   // 3.4: Aufkleber
            if (dev != null && !_settings.OutFxOff) { m.Items.Add(new ToolStripSeparator()); AddOutputFxItems(m.Items, dev.Id, dev.Name); }   // 2.19
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Windows-Soundeinstellungen öffnen", null, (s, e) => OpenUri("ms-settings:sound")));
            m.Show(screen);
        }

        private void ShowOptionsMenu()
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;

            m.Items.Add(new ToolStripMenuItem("Einstellungen …", null, (s, e) => BeginInvoke(new Action(() => ShowSettings()))) { Font = Theme.Ui(12f, FontStyle.Bold) });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Immer im Vordergrund", null, (s, e) =>
            {
                _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
                TopMost = _settings.AlwaysOnTop;
                ScheduleSave();
            }) { Checked = _settings.AlwaysOnTop });

            m.Items.Add(new ToolStripMenuItem("Mit Windows starten", null, (s, e) => SetAutostart(!IsAutostart())) { Checked = IsAutostart(), Enabled = Win32.IsWindows && !_demo });

            m.Items.Add(new ToolStripMenuItem("Beim Schließen im Infobereich weiterlaufen", null, (s, e) =>
            {
                _settings.CloseToTray = !_settings.CloseToTray;
                ScheduleSave();
            }) { Checked = _settings.CloseToTray });


            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Neuer Kanal", null, (s, e) => AddChannel()));
            m.Items.Add(new ToolStripMenuItem("Hotkeys …", null, (s, e) => BeginInvoke(new Action(ShowHotkeyDialog))));
            m.Items.Add(new ToolStripMenuItem("Ducking …", null, (s, e) => BeginInvoke(new Action(ShowDuckDialog))));
            m.Items.Add(new ToolStripMenuItem("Push-to-Talk …", null, (s, e) => BeginInvoke(new Action(ShowPttDialog))) { Checked = _settings.PttMode != 0 });
            m.Items.Add(new ToolStripMenuItem("MIDI-Controller …", null, (s, e) => BeginInvoke(new Action(ShowMidiDialog))) { Checked = _settings.Midi.Count > 0 });
            m.Items.Add(new ToolStripMenuItem("Soundboard …", null, (s, e) => ShowSoundboard()));
            m.Items.Add(new ToolStripMenuItem("Sprach-Ausgang (Mikro für Discord) …", null, (s, e) => BeginInvoke(new Action(ShowVoiceDialog)))
            { Checked = _settings.VoiceOn && _settings.VoiceDevice != null });
            m.Items.Add(new ToolStripMenuItem("Mikro-Effekte (EQ, Kompressor …) …", null, (s, e) => BeginInvoke(new Action(ShowMicFxDialog)))
            { Checked = !CurrentVoiceFx().IsOff });
            m.Items.Add(new ToolStripMenuItem("Stream-Mix (zweiter Mix, z. B. für OBS)", null, (s, e) => SetStreamOn(!_settings.StreamOn)) { Checked = _settings.StreamOn });
            m.Items.Add(new ToolStripMenuItem("Neue Gruppe …", null, (s, e) => AddGroup(null)));
            m.Items.Add(new ToolStripMenuItem("Mehrere Ausgänge (Apps auf Geräte aufteilen)", null, (s, e) => SetMultiOutput(!MultiOutput))
            { Checked = MultiOutput, Enabled = _snap.RoutingSupported || MultiOutput || _demo });

            if (MultiOutput)
            {
                var outs = new ToolStripMenuItem("Ausgänge anzeigen");
                outs.DropDown.Renderer = _renderer;
                ((ToolStripDropDownMenu)outs.DropDown).ShowImageMargin = false;
                ((ToolStripDropDownMenu)outs.DropDown).ShowCheckMargin = true;
                foreach (var d in _snap.Devices)
                {
                    var id = d.Id;
                    bool shown = !_settings.HiddenOutputs.Contains(id, StringComparer.OrdinalIgnoreCase);
                    outs.DropDownItems.Add(new ToolStripMenuItem(d.FullName, null, (s, e) =>
                    {
                        if (shown) _settings.HiddenOutputs.Add(id);
                        else _settings.HiddenOutputs.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                        SyncOutputs();
                        ScheduleSave();
                    }) { Checked = shown });
                }
                if (_snap.Devices.Length == 0) outs.DropDownItems.Add(Hint("Keine Geräte"));
                m.Items.Add(outs);
            }

            if (!_settings.ViewsOff) m.Items.Add(ViewsSub());   // 2.18
            m.Items.Add(BanksSub());
            m.Items.Add(new ToolStripSeparator());
            AddFeatureOptions(m);
            AddOptions400(m);   // 4.0: Fenster, Profile

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Windows-Lautstärkemixer öffnen", null, (s, e) => OpenUri("ms-settings:apps-volume")));
            m.Items.Add(new ToolStripMenuItem("App-Geräteeinstellungen in Windows zurücksetzen …", null, (s, e) => ResetAppRouting()) { Enabled = _snap.RoutingSupported });
            m.Items.Add(new ToolStripMenuItem("Alle Kanäle auf Werkseinstellung …", null, (s, e) => ResetChannels()));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Einstellungen exportieren …", null, (s, e) => BeginInvoke(new Action(ExportSettings))));
            m.Items.Add(new ToolStripMenuItem("Einstellungen importieren …", null, (s, e) => BeginInvoke(new Action(ImportSettings))));
            m.Items.Add(new ToolStripMenuItem("Einstellungsordner öffnen", null, (s, e) =>
            {
                try { Directory.CreateDirectory(MixerSettings.Folder); Process.Start("explorer.exe", MixerSettings.Folder); } catch { }
            }));
            m.Items.Add(new ToolStripMenuItem("Über Mischpult", null, (s, e) => ShowAbout()));
            m.Items.Add(new ToolStripMenuItem("Beenden", null, (s, e) => ExitApp()));

            var b = _top.OptionsButtonBounds;
            m.Show(_top.PointToScreen(new Point(b.Right - m.PreferredSize.Width, b.Bottom + Theme.Px(4))));
        }

        private void ShowAbout()
        {
            ShowAboutWindow400(   // 4.0 finale: eigenes Fenster mit Versions-Chronik
                "Mischpult " + Program.Version + "\nApp-Audio-Mixer für Windows 10/11\n" +
                "made by " + Program.Author + "\n\n" +
                "• Jede App lässt sich einem Kanal zuordnen\n" +
                "• Fader, Balance, Mute/Solo und Pegel pro Kanal\n" +
                "• 3-Band-EQ mit Presets und Kompressor pro Kanal, Nachtmodus\n" +
                "• Szenen und Profil pro Spiel, Spiele automatisch erkennen\n" +
                "• Mikrofon-Kanal mit Push-to-Talk, globale Hotkeys, Ducking\n" +
                "• Gehörschutz, Sleep-Timer, Headset automatisch umschalten\n" +
                "• Mini-Ansicht, MIDI-Controller, WAV-Aufnahme\n" +
                "• Stream-Mix, Gruppen, großer EQ mit Spektrum, Lautstärke-Ausgleich\n" +
                "• Soundboard und Sprach-Ausgang (Mikro für Discord) mit Rauschsperre und Mikro-Effekten\n" +
                "• Lautstärke pro App, Piepton beim Stummschalten, Pegel vor/nach Fader\n" +
                "• Fünf Designs, Meterbridge, VU-Meter, SIG/PEAK-Leuchten, dB-Anzeige, MUTE-Blinken\n" +
                "• Absenkungs-Anzeige (GR), Kanäle leuchten im Takt, PEAK-Zähler\n" +
                "• Musik-Tasten, Szenen nach Uhrzeit, Selbsttest beim Start\n" +
                "• Ein Master-Ausgang oder mehrere Ausgänge\n• Einstellungsfenster mit Suche (Klick auf OPTIONEN)\n\n" +
                "Tipps: Mausrad über einem Kanal = Lautstärke, Shift beim Ziehen = fein,\n" +
                "Doppelklick auf Fader = 0 dB, Doppelklick auf Namen = umbenennen.\n\n" +
                "Anzeige: " + DisplayInfo() + "\n\n" +
                "Einstellungen: " + MixerSettings.FilePath);
        }

        private static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        private Image Swatch(Color c)
        {
            int s = Theme.Px(16);
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Setup(g);
                Theme.FillRound(g, c, new RectangleF(1, 1, s - 2, s - 2), Theme.F(3));
                Theme.StrokeRound(g, Color.FromArgb(80, 0, 0, 0), 1, new RectangleF(1, 1, s - 2, s - 2), Theme.F(3));
            }
            return bmp;
        }

        private Image Glyph(string ch)
        {
            int s = Theme.Px(16);
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Setup(g);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;   // durchsichtiger Grund: kein ClearType
                using (var b = new SolidBrush(Theme.AccentText))
                    g.DrawString(ch, Theme.Ui(12f), b, new RectangleF(0, 0, s, s), Theme.Center);
            }
            return bmp;
        }

        // ================================================================ Autostart / Speichern

        private static bool IsAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k?.GetValue("Mischpult") != null;
            }
            catch { return false; }
        }

        private void SetAutostart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("Mischpult", Installer.RunValue(Application.ExecutablePath, _settings.AutostartDelay));   // 4.1: auf Wunsch verzoegert
                    else k.DeleteValue("Mischpult", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Autostart konnte nicht geändert werden:\n" + ex.Message, "Mischpult");
            }
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            if (_demo || _importing || !CanSave400) return;
            _settings.Save();
            NoteSaved400();   // 4.0: Cloud – eigener Stand
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _uiTimer.Dispose();
                _pacer?.Dispose();
                _saveTimer.Dispose();
                _nowPlaying.Stop();
                _clickPlayer?.Dispose();
                if (_activity != null) Application.RemoveMessageFilter(_activity);
                _bgSource?.Dispose();
            }
            base.Dispose(disposing);
        }

        // ================================================================ Hilfsklassen

        /// <summary>Senkrechte Trennfuge zwischen Kanaelen und Ausgaengen.</summary>
        private WoodCheek _cheekL, _cheekR;

        /// <summary>Holz-Seitenteil am Pult (Design Vintage).</summary>
        private sealed class WoodCheek : Control
        {
            private readonly bool _left;
            public WoodCheek(bool left)
            {
                _left = left;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(0x3a, 0x24, 0x14);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Theme.DrawCheek(e.Graphics, ClientRectangle, _left);   // Holz, Carbon oder Alu
                if (Theme.TubeGlow) Theme.DrawTubeVents(e.Graphics, ClientRectangle);   // 3.3: Roehren gluehen
                if (Theme.NightOn) Theme.DimOverlay(e.Graphics, ClientRectangle, 0f);
            }
        }

        private sealed class Divider : Control
        {
            public Divider()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = BodyColor;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.FillBody(g, ClientRectangle, Theme.FormOrigin(this), BodyColor);
                float x = Width / 2f;
                using (var dark = new Pen(Theme.DivDark, Theme.F(2)))
                using (var light = new Pen(Theme.DivLight, Theme.F(1)))
                {
                    g.DrawLine(dark, x, Theme.F(10), x, Height - Theme.F(10));
                    g.DrawLine(light, x + Theme.F(1.5f), Theme.F(10), x + Theme.F(1.5f), Height - Theme.F(10));
                }
                Theme.TiltShade(g, ClientRectangle, Theme.FormOrigin(this));   // 2.20
                Theme.LampLight(g, ClientRectangle, Theme.FormOrigin(this));
            }
        }

        /// <summary>Leitet Mausrad-Nachrichten an das Steuerelement unter dem Mauszeiger weiter.</summary>
        private sealed class WheelRedirector : IMessageFilter
        {
            private readonly Form _form;
            public WheelRedirector(Form f) { _form = f; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == Win32.WM_LBUTTONDOWN || m.Msg == Win32.WM_RBUTTONDOWN)
                {
                    var r = ChannelStrip.Renaming;
                    if (r != null && m.HWnd != r.RenameHandle) r.EndRename(true);
                    return false;
                }
                if (m.Msg != Win32.WM_MOUSEWHEEL && m.Msg != Win32.WM_MOUSEHWHEEL) return false;
                try
                {
                    long lp = m.LParam.ToInt64();
                    var pt = new Win32.POINT((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                    IntPtr target = Win32.WindowFromPoint(pt);
                    if (target == IntPtr.Zero || target == m.HWnd) return false;
                    var c = Control.FromHandle(target);
                    if (c == null) return false;
                    var f = c.FindForm();
                    if (f != _form && !(f is SettingsWindow)) return false;   // Mausrad dorthin, wo die Maus steht
                    Win32.SendMessage(target, m.Msg, m.WParam, m.LParam);
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
