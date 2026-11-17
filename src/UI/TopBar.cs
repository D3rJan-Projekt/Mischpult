using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>Kopfleiste mit Logo, Status und Knoepfen (Szenen, Aufnahme, Nacht, Ducking, Mini, Optionen).</summary>
    internal sealed partial class TopBar : Control
    {
        public event Action OptionsClicked, OptionsMenu;
        public event Action ScenesClicked;
        public event Action RecClicked, RecMenu;
        public event Action NightClicked;
        public event Action DuckClicked, DuckMenu;
        public event Action MiniClicked;
        /// <summary>Klick auf die Uhr (Bildschirmpunkt unter der Uhr): Sleep-Timer.</summary>
        public event Action<Point> ClockClicked;
        /// <summary>Musik-Tasten: "prev", "play", "next".</summary>
        public event Action<string> MediaClicked;
        /// <summary>4.0: Rechtsklick auf die Musik-Tasten bzw. "Jetzt laeuft".</summary>
        public event Action<Point> MediaMenu;
        /// <summary>Eine Taste wurde gedrueckt (fuer das Tasten-Klicken).</summary>
        public event Action KeyPressed;

        /// <summary>"Jetzt laeuft": Titel als LCD-Laufschrift neben den Musik-Tasten (null = nichts).</summary>
        public NowPlayingInfo NowPlaying;
        private RectangleF _npRect;
        private float _npOffset, _npPause = 1.5f, _npTextW, _npInnerW;
        private int _npShownOffset;
        public bool DuckOn, DuckActive, NightOn, RecOn, RecBlink;
        public string RecText;
        public string Status;
        public bool StatusWarning;
        /// <summary>Uhr (LCD) neben dem Schild – bei laufendem Sleep-Timer die Restzeit in Sekunden (sonst -1).</summary>
        public bool ClockOn = true;
        /// <summary>Uhr im Studio-Look (rote Segmente mit Laufzeit) statt im LCD-Look.</summary>
        public bool ClockStudio = true;
        // ---- 3.1: eigene Titelleiste – die Kopfleiste ist dann die Titelleiste (Ziehen verschiebt, Knoepfe rechts)
        public bool WindowButtons;
        public bool WindowMaximized;
        public event Action WinMinimize, WinMaxRestore, WinClose;
        private RectangleF _winMin, _winMax, _winClose;
        // ---- 3.1: Saison-Deko
        /// <summary>Einstellung (0 automatisch, 1 aus, 2–4 immer Halloween/Weihnachten/Silvester).</summary>
        public int DecoMode;
        /// <summary>Gerade gezeigte Deko (SeasonDeco.None …).</summary>
        public int Deco { get; private set; }
        private RectangleF _pumpkinRect, _fwArea;
        private int _decoStep = -1;
        private double _decoCheck;
        /// <summary>3.1: analoge Uhr (Zeiger).</summary>
        public bool ClockAnalog;
        /// <summary>3.3: Art der Uhr (3 Fallblatt, 4 Nixie – sonst wie bisher).</summary>
        public int ClockKind;
        public int SleepLeft = -1;
        // ---- 3.2: Countdown in der Uhr, Knoepfe fuer Bild vom Pult und Sperren
        /// <summary>Restzeit des Countdowns in Sekunden (sonst -1) – steht dann in der Uhr (bernstein).</summary>
        public int CountdownLeft = -1;
        public bool ShotButton, LockButton, Locked;
        public event Action ShotClicked, LockClicked;
        private RectangleF _shotBtn, _lockBtn;
        // 3.3: ON-AIR-Lampe (0 aus, 1 bei Aufnahme, 2 bei Aufnahme oder Stream-Mix); OnAir = leuchtet gerade
        public int OnAirMode;
        public bool OnAir;
        private RectangleF _onAirRect;
        private RectangleF _clockRect;
        private int _clockSec = -1;

        private RectangleF _optBtn, _miniBtn, _duckBtn, _nightBtn, _recBtn, _sceneBtn, _prevBtn, _playBtn, _nextBtn;
        // ---- 3.4.6: Szenen-Tasten (die ersten vier Szenen direkt neben SZENEN)
        /// <summary>Namen der Szenen fuer die Tasten 1–4 (null = aus).</summary>
        public string[] SceneKeys;
        /// <summary>Welche Taste leuchtet (zuletzt geladene Szene), -1 = keine.</summary>
        public int SceneKeyLit = -1;
        public event Action<int> SceneKeyClicked;
        /// <summary>3.5: Maus ueber Szenen-Taste i (−1 = keine mehr).</summary>
        public event Action<int> SceneKeyHover;
        private readonly RectangleF[] _skBtn = new RectangleF[4];
        // ---- 3.4.6: Takt-Lampe
        public bool BeatShown;
        // ---- 3.5: Hoerzeit heute
        public string HearText, HearTip;
        public bool HearLoud;
        private RectangleF _hearRect;
        private bool _hearFits = true;
        private bool _beatFits = true;
        private int _keysFit = 4;
        private float _bpm, _beatGlow;
        private RectangleF _beatRect;

        /// <summary>Takt-Lampe setzen (bpm 0 = nichts erkannt, glow 0..1); zeichnet nur die Lampe neu.</summary>
        public void SetBeat(float bpm, float glow)
        {
            int ob = (int)Math.Round(_bpm), nb = (int)Math.Round(bpm);
            bool glowChanged = Math.Abs(glow - _beatGlow) > 0.04f || (glow == 0f) != (_beatGlow == 0f);
            _bpm = bpm; _beatGlow = glow;
            if (_beatRect.IsEmpty) return;
            if (ob != nb) Invalidate(Rectangle.Inflate(Rectangle.Ceiling(_beatRect), 2, 2));
            else if (glowChanged) Invalidate(BeatLedArea);   // nur die LED (schneller Weg in PaintBar)
        }

        private RectangleF BeatLed
        {
            get
            {
                float d = Theme.F(9);
                return new RectangleF(_beatRect.X + Theme.F(7), _beatRect.Y + (_beatRect.Height - d) / 2f, d, d);
            }
        }

        private Rectangle BeatLedArea => Rectangle.Ceiling(RectangleF.Inflate(BeatLed, Theme.F(3.5f), Theme.F(3.5f)));
        private float _badgeRight;
        private string _hover;

        public TopBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Height = Theme.Px(46);
        }

        public Rectangle OptionsButtonBounds => Rectangle.Round(_optBtn);
        public Rectangle ScenesButtonBounds => Rectangle.Round(_sceneBtn);
        public Rectangle RecButtonBounds => Rectangle.Round(_recBtn);

        /// <summary>Platz, den Logo und Knoepfe mindestens brauchen.</summary>
        public static int MinWidth => Theme.Px(966);

        protected override void OnPaint(PaintEventArgs e)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { PaintBar(e); }
            finally { FrameStats.AddPaint(System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
        }

        private void PaintBar(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            if (!_beatRect.IsEmpty && BeatLedArea.Contains(e.ClipRectangle))
            {
                DrawBeat(g);   // 3.4.6: nur die Takt-LED hat sich geaendert
                return;
            }
            if (!_npRect.IsEmpty && Rectangle.Ceiling(NpInner).Contains(e.ClipRectangle))
            {
                // nur die Laufschrift hat sich bewegt
                DrawNowPlaying(g);   // leuchtet: auch abends hell (wie im ganzen Bild)
                return;
            }
            if (Theme.Style == Theme.StyleStudio)
            {
                // Frontplatte: gebuerstetes Metall mit Fase und Schrauben
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, Width, Height + 2), Theme.Lighten(Theme.PanelTop, 0.02f), Theme.BarBg, LinearGradientMode.Vertical))
                    g.FillRectangle(b, ClientRectangle);
                Theme.FillBrushed(g, new RectangleF(0, 0, Width, Height));
                using (var pen = new Pen(Theme.Alpha(Theme.PanelHighlight, 150), Theme.F(1)))
                    g.DrawLine(pen, 0, Theme.F(0.5f), Width, Theme.F(0.5f));
                using (var pen = new Pen(Color.FromArgb(0x03, 0x03, 0x04), Theme.F(1)))
                    g.DrawLine(pen, 0, Height - Theme.F(1), Width, Height - Theme.F(1));
                using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), Theme.F(1)))
                    g.DrawLine(pen, 0, Height - Theme.F(2), Width, Height - Theme.F(2));
                Theme.DrawScrew(g, Theme.F(7), Height / 2f, Theme.F(3.4f), 20f);
                Theme.DrawScrew(g, Width - Theme.F(6), Height / 2f, Theme.F(3.4f), 65f);
            }
            else Theme.DrawBarBackground(g, ClientRectangle);

            // Logo: drei Mini-Fader
            float lx = Theme.F(16), ly = Theme.F(11), lh = Theme.F(24);
            float[] caps = { 0.3f, 0.75f, 0.5f };
            for (int i = 0; i < 3; i++)
            {
                float x = lx + i * Theme.F(8);
                Theme.FillRound(g, Theme.Hole, new RectangleF(x - Theme.F(1.5f), ly, Theme.F(3), lh), Theme.F(1.5f));
                float cy = ly + lh - caps[i] * lh;
                Theme.FillRound(g, i == 1 ? Theme.Accent : Theme.LogoCap, new RectangleF(x - Theme.F(3.5f), cy - Theme.F(2.5f), Theme.F(7), Theme.F(5)), Theme.F(1.2f));
            }
            var title = Theme.Ui(15f, FontStyle.Bold);
            float tx = lx + Theme.F(30);
            Theme.DrawSpaced(g, "MISCHPULT", title, Theme.Text, tx, Theme.F(9), Theme.F(2.2f));
            float tw = Theme.MeasureSpaced(g, "MISCHPULT", title, Theme.F(2.2f));
            // "made by D3r_Jan"
            float mx = tx + tw + Theme.F(14);
            var small = Theme.Ui(10f);
            var author = Theme.Ui(11f, FontStyle.Bold);
            float w1 = Theme.TextWidth(g, "made by", small);
            float w2 = Theme.TextWidth(g, Program.Author, author);
            var badge = new RectangleF(mx, Theme.F(13), w1 + w2 + Theme.F(20), Theme.F(20));
            Theme.FillRound(g, Theme.BadgeBg, badge, Theme.F(10));
            Theme.StrokeRound(g, Theme.BadgeEdge, Theme.F(1), badge, Theme.F(10));
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "made by", small, b, badge.X + Theme.F(8), badge.Y + Theme.F(4), StringFormat.GenericTypographic);
            using (var b = new SolidBrush(Theme.AccentText))
                Theme.Str(g, Program.Author, author, b, badge.X + Theme.F(12) + w1, badge.Y + Theme.F(3), StringFormat.GenericTypographic);
            _badgeRight = badge.Right;
            if (Deco == SeasonDeco.Halloween)
            {
                _pumpkinRect = new RectangleF((float)Math.Round(_badgeRight + Theme.F(10)), (float)Math.Round((Height - Theme.F(22)) / 2f), Theme.F(22), Theme.F(22));
                SeasonDeco.DrawPumpkin(g, _pumpkinRect);
                _badgeRight = _pumpkinRect.Right;
            }
            else if (Deco >= SeasonDeco.Easter) _badgeRight = SeasonDeco.DrawNearBadge(g, Deco, _badgeRight, Height, out _pumpkinRect);   // 4.0
            else _pumpkinRect = RectangleF.Empty;

            // Knoepfe rechts
            float bh = Theme.F(28), by = (Height - bh) / 2 - Theme.F(0.5f), gap = Theme.F(6);
            float right = Width - Theme.F(12);
            if (WindowButtons)
            {
                // 3.1: Fenster-Knoepfe ganz rechts
                float ww = Theme.F(30), wg = Theme.F(3);
                _winClose = new RectangleF(Width - Theme.F(8) - ww, by, ww, bh);
                _winMax = new RectangleF(_winClose.X - wg - ww, by, ww, bh);
                _winMin = new RectangleF(_winMax.X - wg - ww, by, ww, bh);
                DrawWinButtons(g);
                right = _winMin.X - Theme.F(10);
            }
            else _winMin = _winMax = _winClose = RectangleF.Empty;
            _optBtn = new RectangleF(right - Theme.F(104), by, Theme.F(104), bh);
            _miniBtn = new RectangleF(_optBtn.X - gap - Theme.F(38), by, Theme.F(38), bh);
            _duckBtn = new RectangleF(_miniBtn.X - gap - Theme.F(104), by, Theme.F(104), bh);
            _nightBtn = new RectangleF(_duckBtn.X - gap - Theme.F(86), by, Theme.F(86), bh);
            _recBtn = new RectangleF(_nightBtn.X - gap - Theme.F(96), by, Theme.F(96), bh);
            _sceneBtn = new RectangleF(_recBtn.X - gap - Theme.F(96), by, Theme.F(96), bh);
            float mw = Theme.F(28), mg = Theme.F(2);
            // 3.4.6: Platz fuer Szenen-Tasten und Takt-Lampe? Sonst zuerst die Lampe, dann Tasten weglassen (Logo bleibt frei)
            {
                float room = _sceneBtn.X - (_badgeRight + Theme.F(16));
                float tkw = TopKeysWidth(gap, mg, room);   // 4.0: Zusatz-Tasten
                float fixedW = 3 * mw + 2 * mg + gap + (LockButton ? gap + mw : 0) + (ShotButton ? (LockButton ? mg : gap) + mw : 0) + (OnAirMode > 0 ? gap + Theme.F(62) : 0) + tkw;
                int want = SceneKeys == null ? 0 : Math.Min(_skBtn.Length, SceneKeys.Length);
                float KeysW(int n) => n == 0 ? 0 : gap + n * Theme.F(26) + (n - 1) * mg;
                _beatFits = BeatShown && fixedW + KeysW(want) + gap + Theme.F(70) <= room;
                _hearFits = HearText != null && fixedW + KeysW(want) + (_beatFits ? gap + Theme.F(70) : 0) + gap + Theme.F(64) <= room;   // 3.5
                _keysFit = want;
                while (_keysFit > 0 && fixedW + KeysW(_keysFit) > room) _keysFit--;
            }
            float mediaRight = LayoutSceneKeys(g, by, bh, gap, mg);
            _nextBtn = new RectangleF(mediaRight - gap - mw, by, mw, bh);
            _playBtn = new RectangleF(_nextBtn.X - mg - mw, by, mw, bh);
            _prevBtn = new RectangleF(_playBtn.X - mg - mw, by, mw, bh);
            DrawMediaButton(g, _prevBtn, "prev");
            DrawMediaButton(g, _playBtn, "play");
            DrawMediaButton(g, _nextBtn, "next");
            // 3.2: Schloss und Kamera links neben den Musik-Tasten
            float btnLeft = _prevBtn.X;
            btnLeft = LayoutTopKeys(g, btnLeft, by, bh, gap, mg);   // 4.0: Zusatz-Tasten (eigene, Panik, Spulen, OBS …)
            if (LockButton) { _lockBtn = new RectangleF(btnLeft - gap - mw, by, mw, bh); btnLeft = _lockBtn.X; DrawIconButton(g, _lockBtn, "lock"); }
            else _lockBtn = RectangleF.Empty;
            if (ShotButton) { _shotBtn = new RectangleF(btnLeft - (LockButton ? mg : gap) - mw, by, mw, bh); btnLeft = _shotBtn.X; DrawIconButton(g, _shotBtn, "shot"); }
            else _shotBtn = RectangleF.Empty;
            if (OnAirMode > 0)
            {
                float ow = Theme.F(62);
                _onAirRect = new RectangleF(btnLeft - gap - ow, by, ow, bh);
                btnLeft = _onAirRect.X;
                Theme.DrawOnAir(g, _onAirRect, OnAir);
            }
            else _onAirRect = RectangleF.Empty;
            if (BeatShown && _beatFits)
            {
                float bw = Theme.F(70);
                _beatRect = new RectangleF(btnLeft - gap - bw, by, bw, bh);
                btnLeft = _beatRect.X;
                DrawBeat(g);
            }
            else _beatRect = RectangleF.Empty;
            if (HearText != null && _hearFits)
            {
                float hw = Theme.F(64);
                _hearRect = new RectangleF(btnLeft - gap - hw, by, hw, bh);
                btnLeft = _hearRect.X;
                DrawHear(g);
            }
            else _hearRect = RectangleF.Empty;

            DrawBarButton(g, _sceneBtn, "SZENEN  ▾", _hover == "scene");

            DrawBarButton(g, _recBtn, "     " + (RecOn ? (RecText ?? "REC") : "AUFNAHME"), _hover == "rec", RecOn ? (Theme.IsLight ? Color.FromArgb(0xc8, 0x2a, 0x1e) : Color.FromArgb(0xff, 0x8a, 0x80)) : (Color?)null);
            Theme.DrawLed(g, _recBtn.X + Theme.F(16), _recBtn.Y + bh / 2, Theme.F(4), Theme.MuteRed, RecOn && RecBlink);

            DrawBarButton(g, _nightBtn, "     NACHT", _hover == "night");
            Theme.DrawLed(g, _nightBtn.X + Theme.F(16), _nightBtn.Y + bh / 2, Theme.F(4), Color.FromArgb(0x7a, 0xa8, 0xff), NightOn);

            DrawBarButton(g, _duckBtn, "     DUCKING", _hover == "duck");
            Theme.DrawLed(g, _duckBtn.X + Theme.F(16), _duckBtn.Y + bh / 2, Theme.F(4),
                DuckActive ? Theme.SoloAmber : Theme.LedGreen, DuckOn);

            DrawBarButton(g, _miniBtn, "", _hover == "mini");
            {
                // Symbol: kleines Fenster
                var r = new RectangleF(_miniBtn.X + _miniBtn.Width / 2 - Theme.F(8), _miniBtn.Y + bh / 2 - Theme.F(6), Theme.F(16), Theme.F(12));
                Color c = _hover == "mini" ? Theme.TextHover : Theme.Text;
                Theme.StrokeRound(g, c, Theme.F(1.4f), r, Theme.F(2));
                using (var b = new SolidBrush(c))
                {
                    g.FillRectangle(b, r.X + Theme.F(3), r.Y + Theme.F(3.5f), r.Width - Theme.F(6), Theme.F(1.6f));
                    g.FillRectangle(b, r.X + Theme.F(3), r.Y + Theme.F(7), (r.Width - Theme.F(6)) * 0.6f, Theme.F(1.6f));
                }
            }
            DrawBarButton(g, _optBtn, "OPTIONEN", _hover == "opt");

            // Uhr: zwischen Schild und Status. Wird es eng, hat eine Warnung (z. B. DIM, Fehler) Vorrang vor der Uhr,
            // sonst die Uhr vor dem normalen Status ("5 Apps spielen").
            float free = btnLeft - Theme.F(10) - (_badgeRight + Theme.F(12));
            float clockW = Theme.F(SleepLeft >= 0 || CountdownLeft >= 0 ? 74 : ClockKind == 6 ? 96 : ClockKind >= 3 ? 72 : ClockAnalog ? 26 : ClockStudio ? 92 : 60);
            bool statusWanted = !string.IsNullOrEmpty(Status);
            bool clock = (ClockOn || CountdownLeft >= 0) && free >= clockW && (!statusWanted || !StatusWarning || free >= clockW + Theme.F(110));
            _clockRect = clock ? new RectangleF((float)Math.Round(_badgeRight + Theme.F(12)), (float)Math.Round((Height - Theme.F(24)) / 2f), (float)Math.Round(clockW), (float)Math.Round(Theme.F(24))) : RectangleF.Empty;
            if (statusWanted && clock && !StatusWarning && free < clockW + Theme.F(70)) statusWanted = false;

            // "Jetzt laeuft": direkt links neben den Musik-Tasten. Eine Warnung behaelt ihren Platz, der normale Status weicht.
            _npRect = RectangleF.Empty;
            if (NowPlaying != null && !string.IsNullOrEmpty(NowPlaying.Title))
            {
                float npLeft = clock ? _clockRect.Right + Theme.F(12) : _badgeRight + Theme.F(14), npRight = btnLeft - Theme.F(8);
                float keep = statusWanted && StatusWarning ? Theme.F(170) : 0f;
                float w = (float)Math.Round(Math.Min(Theme.F(300), npRight - npLeft - keep));
                if (w >= Theme.F(110))
                    _npRect = new RectangleF((float)Math.Round(npRight - w), (float)Math.Round((Height - Theme.F(24)) / 2f), w, (float)Math.Round(Theme.F(24)));
            }

            if (statusWanted)
            {
                float sx = clock ? _clockRect.Right + Theme.F(12) : _badgeRight + Theme.F(14);
                float sxEnd = _npRect.IsEmpty ? btnLeft - Theme.F(10) : _npRect.X - Theme.F(10);
                var sr = new RectangleF(sx, 0, Math.Max(0, sxEnd - sx), Height);
                if (sr.Width > Theme.F(40))
                {
                    using (var b = new SolidBrush(StatusWarning ? Theme.WarnText : Theme.TextDim))
                    {
                        // passt der Text nicht, links ausrichten, damit am Ende "…" steht
                        bool fits = Theme.TextWidth(g, Status, Theme.Ui(10f)) <= sr.Width;
                        using (var sf = new StringFormat(fits ? Theme.Right : Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                            Theme.Str(g, Status, Theme.Ui(10f), b, sr, sf);
                    }
                }
            }

            // abends: Leiste dunkler, Leuchten (REC, NACHT, DUCKING) bleiben hell
            if (Theme.NightOn)
            {
                Theme.DimOverlay(g, new RectangleF(0, 0, Width, Height), 0f);
                if (RecOn && RecBlink) Theme.DrawLed(g, _recBtn.X + Theme.F(16), _recBtn.Y + bh / 2, Theme.F(4), Theme.MuteRed, true);
                if (NightOn) Theme.DrawLed(g, _nightBtn.X + Theme.F(16), _nightBtn.Y + bh / 2, Theme.F(4), Color.FromArgb(0x7a, 0xa8, 0xff), true);
                if (OnAir && !_onAirRect.IsEmpty) Theme.DrawOnAir(g, _onAirRect, true);
                if (DuckOn) Theme.DrawLed(g, _duckBtn.X + Theme.F(16), _duckBtn.Y + bh / 2, Theme.F(4), DuckActive ? Theme.SoloAmber : Theme.LedGreen, true);
            }
            if (!_clockRect.IsEmpty) DrawClock(g);
            if (!_npRect.IsEmpty) DrawNowPlaying(g);
            // 3.1: Saison-Deko obendrauf
            _fwArea = new RectangleF(_badgeRight + Theme.F(12), Theme.F(2), Math.Max(0f, btnLeft - _badgeRight - Theme.F(24)), Height - Theme.F(4));
            if (Deco == SeasonDeco.Halloween) SeasonDeco.DrawWeb(g, Theme.F(22));
            else if (Deco == SeasonDeco.Christmas) SeasonDeco.DrawLights(g, Width, Height);
            else if (Deco == SeasonDeco.NewYear) SeasonDeco.DrawFireworks(g, _fwArea);
            else if (Deco >= SeasonDeco.Easter) SeasonDeco.DrawOver(g, Deco, Width, Height, _fwArea);   // 4.0
        }

        /// <summary>3.1: Aus dem Takt – Deko nach Datum pruefen und Bewegtes neu zeichnen (Lichter, Kerze, Feuerwerk).</summary>
        public void TickDeco()
        {
            double now = SeasonDeco.Now;
            if (now >= _decoCheck)
            {
                _decoCheck = now + 10;
                int d = SeasonDeco.For(DecoMode, DateTime.Now);
                if (d != Deco) { Deco = d; Invalidate(); }
            }
            if (Deco == SeasonDeco.None || !Visible) return;
            int step;
            switch (Deco)
            {
                case SeasonDeco.Christmas:
                    step = SeasonDeco.LightsStep;
                    if (step != _decoStep) { _decoStep = step; Invalidate(new Rectangle(0, Height - Theme.Px(14), Width, Theme.Px(14))); }
                    break;
                case SeasonDeco.Halloween:
                    step = SeasonDeco.PumpkinStep;
                    if (step != _decoStep && !_pumpkinRect.IsEmpty) { _decoStep = step; Invalidate(Rectangle.Inflate(Rectangle.Ceiling(_pumpkinRect), Theme.Px(6), Theme.Px(5))); }
                    break;
                case SeasonDeco.Spring:   // 4.0: Blumen wiegen sich (nur der untere Rand)
                    step = SeasonDeco.StepOf(Deco);
                    if (step != _decoStep) { _decoStep = step; Invalidate(new Rectangle(0, Height - Theme.Px(16), Width, Theme.Px(16))); }
                    break;
                case SeasonDeco.Summer:
                case SeasonDeco.Birthday:
                case SeasonDeco.Autumn:
                    step = SeasonDeco.StepOf(Deco);
                    if (step != _decoStep)
                    {
                        _decoStep = step;
                        if (!_pumpkinRect.IsEmpty) Invalidate(Rectangle.Inflate(Rectangle.Ceiling(_pumpkinRect), Theme.Px(7), Theme.Px(6)));
                        if (Deco != SeasonDeco.Summer) Invalidate(Rectangle.Ceiling(_fwArea));
                    }
                    break;
                case SeasonDeco.NewYear:
                    step = (int)(now * 30);
                    bool any = SeasonDeco.TickFireworks(_fwArea);
                    if (step != _decoStep && (any || _decoStep >= 0)) { _decoStep = any ? step : -1; Invalidate(Rectangle.Ceiling(_fwArea)); }
                    break;
            }
        }

        /// <summary>Deko sofort neu bestimmen (nach Aenderung der Einstellung).</summary>
        public void RefreshDeco() { _decoCheck = 0; TickDeco(); Invalidate(); }

        // ------------------------------------------------------------ Jetzt laeuft

        private RectangleF NpInner => new RectangleF(_npRect.X + Theme.F(20), _npRect.Y + Theme.F(3), _npRect.Width - Theme.F(26), _npRect.Height - Theme.F(6));
        private float NpGap => Theme.F(40);

        private void DrawNowPlaying(Graphics g)
        {
            var np = NowPlaying;
            if (np == null) return;
            Theme.LcdColors(out var bg, out var fg);
            if (Theme.EffLcd == 0) fg = Theme.LcdText;
            Color c = np.Playing ? fg : Theme.Mix(bg, fg, 0.5f);
            Theme.DrawLcd(g, _npRect, "", null);
            // Symbol links: Note (laeuft) bzw. Pause
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float ix = _npRect.X + Theme.F(10), iy = _npRect.Y + _npRect.Height / 2f;
            using (var b = new SolidBrush(c))
            {
                if (np.Playing)
                {
                    g.FillEllipse(b, ix - Theme.F(4.5f), iy + Theme.F(1.2f), Theme.F(5), Theme.F(3.8f));
                    g.FillRectangle(b, ix - Theme.F(0.4f), iy - Theme.F(6), Theme.F(1.3f), Theme.F(9));
                    g.FillPolygon(b, new[] { new PointF(ix + Theme.F(0.6f), iy - Theme.F(6)), new PointF(ix + Theme.F(4.5f), iy - Theme.F(3.6f)), new PointF(ix + Theme.F(0.6f), iy - Theme.F(2.6f)) });
                }
                else
                {
                    g.FillRectangle(b, ix - Theme.F(3.5f), iy - Theme.F(4.5f), Theme.F(2.4f), Theme.F(9));
                    g.FillRectangle(b, ix + Theme.F(1), iy - Theme.F(4.5f), Theme.F(2.4f), Theme.F(9));
                }
            }
            g.SmoothingMode = sm;
            var inner = NpInner;
            string text = np.Text;
            var font = Theme.Mono(10.5f);
            _npTextW = Theme.TextWidth(g, text, font);
            _npInnerW = inner.Width;
            var st = g.Save();
            g.SetClip(inner, CombineMode.Intersect);
            float th = font.GetHeight(g), ty = (float)Math.Round(inner.Y + (inner.Height - th) / 2f);
            using (var b = new SolidBrush(c))
            {
                if (_npTextW <= inner.Width)
                    Theme.Str(g, text, font, b, (float)Math.Round(inner.X), ty, StringFormat.GenericTypographic);
                else
                {
                    float x = (float)Math.Round(inner.X - _npOffset);
                    Theme.Str(g, text, font, b, x, ty, StringFormat.GenericTypographic);
                    Theme.Str(g, text, font, b, (float)Math.Round(x + _npTextW + NpGap), ty, StringFormat.GenericTypographic);
                }
            }
            g.Restore(st);
            DrawSongBar400(g, c);   // 4.0 finale: Song-Fortschritt
        }

        /// <summary>Neuer Titel (oder null): von vorne anfangen.</summary>
        public void SetNowPlaying(NowPlayingInfo info)
        {
            var old = NowPlaying;
            if (info == null ? old == null : info.SameAs(old)) return;
            bool sameText = info != null && old != null && info.Text == old.Text;
            NowPlaying = info;
            if (!sameText) { _npOffset = 0; _npPause = 1.5f; _npShownOffset = 0; }
            Invalidate();
        }

        /// <summary>Aus dem UI-Takt: Laufschrift weiterschieben (nur der Text-Bereich wird neu gezeichnet).</summary>
        public void TickMarquee(float dt)
        {
            if (_npRect.IsEmpty || NowPlaying == null || !Visible || _npTextW <= _npInnerW) return;
            if (_npPause > 0f) { _npPause -= dt; return; }
            _npOffset += Theme.F(30) * Math.Min(dt, 0.1f);
            float cycle = _npTextW + NpGap;
            if (_npOffset >= cycle) { _npOffset -= cycle; _npPause = 2f; }
            int shown = (int)Math.Round(_npOffset);
            if (shown == _npShownOffset) return;
            _npShownOffset = shown;
            Invalidate(Rectangle.Ceiling(NpInner));
        }
        /// <summary>Uhrzeit (Doppelpunkt blinkt) bzw. Restzeit des Sleep-Timers, im LCD-Look.</summary>
        private void DrawClock(Graphics g)
        {
            var now = DateTime.Now;
            string t;
            Color? c = null;
            if (SleepLeft >= 0)
            {
                int m = SleepLeft / 60, s = SleepLeft % 60;
                t = "\u2212" + m + ":" + s.ToString("00");
                c = Color.FromArgb(0x7a, 0xa8, 0xff);
            }
            else if (CountdownLeft >= 0)
            {
                int m = CountdownLeft / 60, s = CountdownLeft % 60;   // 3.2: Countdown
                t = m + ":" + s.ToString("00");
                c = CountdownLeft == 0 ? Theme.MeterRed : Color.FromArgb(0xff, 0xb5, 0x47);
            }
            else if (ClockKind >= 3)
            {
                _clockSec = now.Second;
                Theme.DrawClockSmall(g, _clockRect, ClockKind);
                return;
            }
            else if (ClockAnalog)
            {
                _clockSec = now.Second;
                Theme.DrawAnalogClockSmall(g, _clockRect);
                return;
            }
            else if (ClockStudio)
            {
                _clockSec = now.Second;
                Theme.DrawStudioClockSmall(g, _clockRect);
                return;
            }
            else t = now.Hour.ToString("00") + (now.Second % 2 == 0 ? ":" : " ") + now.Minute.ToString("00");
            _clockSec = now.Second;
            Theme.DrawLcd(g, _clockRect, t, c);
        }

        /// <summary>Aus dem UI-Takt: Uhr jede Sekunde neu zeichnen (Doppelpunkt blinkt).</summary>
        public void TickClock()
        {
            if (_clockRect.IsEmpty || !Visible) return;
            if (DateTime.Now.Second != _clockSec) Invalidate(Rectangle.Inflate(Rectangle.Ceiling(_clockRect), 1, 1));
        }

        /// <summary>Uhr-Bereich (fuer Tooltip).</summary>
        public bool ClockHit(Point p) => !_clockRect.IsEmpty && _clockRect.Contains(p);

        /// <summary>Musik-Taste mit gezeichnetem Symbol (|◀◀, ▶‖, ▶▶|).</summary>
        private void DrawMediaButton(Graphics g, RectangleF r, string id)
        {
            bool hover = _hover == id;
            Theme.DrawBarButton(g, r, hover);
            Color c = hover ? Theme.TextHover : Theme.Text;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Theme.F(4.2f);
            using (var b = new SolidBrush(c))
            {
                if (id == "play")
                {
                    g.FillPolygon(b, new[] { new PointF(cx - s * 1.25f, cy - s), new PointF(cx + s * 0.15f, cy), new PointF(cx - s * 1.25f, cy + s) });
                    g.FillRectangle(b, cx + s * 0.45f, cy - s, s * 0.38f, s * 2);
                    g.FillRectangle(b, cx + s * 1.05f, cy - s, s * 0.38f, s * 2);
                }
                else
                {
                    float d = id == "next" ? 1f : -1f;
                    // zwei Dreiecke + Strich
                    g.FillPolygon(b, new[] { new PointF(cx - d * s * 1.2f, cy - s * 0.9f), new PointF(cx, cy), new PointF(cx - d * s * 1.2f, cy + s * 0.9f) });
                    g.FillPolygon(b, new[] { new PointF(cx, cy - s * 0.9f), new PointF(cx + d * s * 1.2f, cy), new PointF(cx, cy + s * 0.9f) });
                    float lx = cx + d * s * 1.2f - (d > 0 ? 0 : s * 0.32f);
                    g.FillRectangle(b, lx, cy - s * 0.9f, s * 0.32f, s * 1.8f);
                }
            }
        }

        /// <summary>3.2: Kamera (Bild vom Pult) bzw. Schloss (offen/zu) als Knopf.</summary>
        private void DrawIconButton(Graphics g, RectangleF r, string id)
        {
            bool hover = _hover == id;
            bool lit = id == "lock" && Locked;
            Theme.DrawBarButton(g, r, hover);
            Color c = lit ? Theme.SoloAmber : hover ? Theme.TextHover : Theme.Text;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Theme.F(5f);
            using (var pen = new Pen(c, Math.Max(1.2f, Theme.F(1.4f))))
            using (var b = new SolidBrush(c))
            {
                if (id == "shot")
                {
                    var body = new RectangleF(cx - s * 1.3f, cy - s * 0.7f, s * 2.6f, s * 1.8f);
                    Theme.StrokeRound(g, c, Math.Max(1.2f, Theme.F(1.4f)), body, Theme.F(1.5f));
                    g.FillRectangle(b, cx - s * 0.55f, cy - s * 1.05f, s * 1.1f, s * 0.4f);
                    g.DrawEllipse(pen, cx - s * 0.5f, cy - s * 0.3f, s, s);
                }
                else
                {
                    var body = new RectangleF(cx - s * 0.95f, cy - s * 0.1f, s * 1.9f, s * 1.3f);
                    Theme.FillRound(g, c, body, Theme.F(1.2f));
                    // Buegel: zu = geschlossen, offen = nach rechts hochgeklappt
                    if (Locked) g.DrawArc(pen, cx - s * 0.6f, cy - s * 1.1f, s * 1.2f, s * 1.6f, 180, 180);
                    else g.DrawArc(pen, cx - s * 0.05f, cy - s * 1.35f, s * 1.2f, s * 1.6f, 180, 180);
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>3.1: Minimieren, Maximieren/Wiederherstellen, Schliessen im Stil der Kopfleiste.</summary>
        private void DrawWinButtons(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (var id in new[] { "wmin", "wmax", "wclose" })
            {
                var r = id == "wmin" ? _winMin : id == "wmax" ? _winMax : _winClose;
                bool hover = _hover == id;
                Theme.DrawBarButton(g, r, hover);
                if (hover && id == "wclose") Theme.FillRound(g, Color.FromArgb(200, 0xd9, 0x2b, 0x22), RectangleF.Inflate(r, -Theme.F(1.5f), -Theme.F(1.5f)), Theme.F(3));
                Color c = hover ? (id == "wclose" ? Color.White : Theme.TextHover) : Theme.Text;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Theme.F(4.5f);
                using (var pen = new Pen(c, Math.Max(1f, Theme.F(1.3f))))
                {
                    if (id == "wmin") g.DrawLine(pen, cx - s, cy + s * 0.6f, cx + s, cy + s * 0.6f);
                    else if (id == "wclose") { g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s); g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s); }
                    else if (!WindowMaximized) g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                    else
                    {
                        float k = s * 0.45f;
                        g.DrawRectangle(pen, cx - s, cy - s + k, s * 2 - k, s * 2 - k);
                        g.DrawLines(pen, new[] { new PointF(cx - s + k, cy - s + k), new PointF(cx - s + k, cy - s), new PointF(cx + s, cy - s), new PointF(cx + s, cy + s - k), new PointF(cx + s - k, cy + s - k) });
                    }
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>3.1: Mit eigener Titelleiste gehoeren freie Stellen der Kopfleiste (und der obere Rand) zum Fenster – zum Ziehen und Verschieben.</summary>
        protected override void WndProc(ref Message m)
        {
            if (WindowButtons && m.Msg == 0x0084 /* WM_NCHITTEST */)
            {
                long lp = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (HitTest(pt) == null || (!WindowMaximized && pt.Y < Theme.F(5)))
                {
                    m.Result = new IntPtr(-1);   // HTTRANSPARENT: das Fenster entscheidet (Titelleiste/oberer Rand)
                    return;
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>3.5: Hoerzeit heute – kleines LCD mit Uhr-Zeichen.</summary>
        private void DrawHear(Graphics g)
        {
            var r = _hearRect;
            Theme.DrawLcd(g, r, "", null);
            Theme.LcdColors(out var bg, out var fg);
            Color c = HearLoud ? Theme.SoloAmber : fg;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Theme.F(10), cx = r.X + Theme.F(6) + d / 2f, cy = r.Y + r.Height / 2f;
            using (var pen = new Pen(c, Math.Max(1f, Theme.F(1.2f))))
            {
                g.DrawEllipse(pen, cx - d / 2f, cy - d / 2f, d, d);
                g.DrawLine(pen, cx, cy, cx, cy - d * 0.32f);
                g.DrawLine(pen, cx, cy, cx + d * 0.25f, cy + d * 0.1f);
            }
            g.SmoothingMode = sm;
            using (var b = new SolidBrush(c))
                Theme.Str(g, HearText, Theme.Mono(11f), b, new RectangleF(r.X + Theme.F(16), r.Y, r.Width - Theme.F(19), r.Height), Theme.Center);
        }

        /// <summary>3.4.6: Takt-Lampe – LED, die im Takt aufleuchtet, und das Tempo.</summary>
        private void DrawBeat(Graphics g)
        {
            var r = _beatRect;
            Theme.DrawLcd(g, r, "", null);
            Theme.LcdColors(out var bg, out var fg);
            var led = BeatLed;
            var on = Color.FromArgb(0xff, 0xb0, 0x30);
            float gl = _bpm > 0 ? Math.Max(0f, Math.Min(1f, _beatGlow)) : 0f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0x3a, 0x2a, 0x10), on, gl))) g.FillEllipse(b, led);
            if (gl > 0.05f)
                using (var b = new SolidBrush(Color.FromArgb((int)(90 * gl), on))) g.FillEllipse(b, RectangleF.Inflate(led, Theme.F(3), Theme.F(3)));
            g.SmoothingMode = sm;
            var tr = new RectangleF(led.Right + Theme.F(3), r.Y, r.Right - led.Right - Theme.F(6), r.Height);
            string txt = _bpm > 0 ? ((int)Math.Round(_bpm)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "– –";
            using (var b = new SolidBrush(fg))
            {
                Theme.Str(g, txt, Theme.Mono(11f), b, new RectangleF(tr.X, tr.Y, tr.Width - Theme.F(22), tr.Height), Theme.Right);
                Theme.Str(g, "BPM", Theme.Ui(7f, FontStyle.Bold), b, new RectangleF(tr.Right - Theme.F(22), tr.Y + Theme.F(1), Theme.F(22), tr.Height), Theme.Center);
            }
        }

        /// <summary>3.4.6: Szenen-Tasten links neben SZENEN anordnen und zeichnen; gibt die linke Kante zurueck.</summary>
        private float LayoutSceneKeys(Graphics g, float by, float bh, float gap, float mg)
        {
            for (int i = 0; i < _skBtn.Length; i++) _skBtn[i] = RectangleF.Empty;
            int n = SceneKeys == null ? 0 : Math.Min(Math.Min(_skBtn.Length, SceneKeys.Length), _keysFit);
            if (n == 0) return _sceneBtn.X;
            float kw = Theme.F(26), x = _sceneBtn.X - gap;
            for (int i = n - 1; i >= 0; i--)
            {
                x -= kw;
                _skBtn[i] = new RectangleF(x, by, kw, bh);
                x -= mg;
            }
            for (int i = 0; i < n; i++)
            {
                bool lit = i == SceneKeyLit;
                var r = _skBtn[i];
                DrawBarButton(g, r, (i + 1).ToString(), _hover == "sk" + i, lit ? Theme.Accent : (Color?)null);
                if (lit)
                    using (var b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, r.X + Theme.F(7), r.Bottom - Theme.F(4.5f), r.Width - Theme.F(14), Theme.F(2));
            }
            return _skBtn[0].X + mg;
        }

        private int SceneKeyAt(Point p)
        {
            for (int i = 0; i < _skBtn.Length; i++) if (!_skBtn[i].IsEmpty && _skBtn[i].Contains(p)) return i;
            return -1;
        }

        private static void DrawBarButton(Graphics g, RectangleF r, string text, bool hover, Color? textColor = null)
        {
            Theme.DrawBarButton(g, r, hover);
            if (text.Length > 0)
                Theme.Label(g, text, r, Theme.Center, textColor ?? (hover ? Theme.TextHover : Theme.Text), 10f);
        }

        private string HitTest(Point p) =>
            _winClose.Contains(p) ? "wclose" : _winMax.Contains(p) ? "wmax" : _winMin.Contains(p) ? "wmin" :
            _optBtn.Contains(p) ? "opt" : _miniBtn.Contains(p) ? "mini" : _duckBtn.Contains(p) ? "duck" :
            _nightBtn.Contains(p) ? "night" : _recBtn.Contains(p) ? "rec" : _sceneBtn.Contains(p) ? "scene" :
            SceneKeyAt(p) >= 0 ? "sk" + SceneKeyAt(p) :
            TopKeyAt(p) >= 0 ? "tk" + TopKeyAt(p) :
            _prevBtn.Contains(p) ? "prev" : _playBtn.Contains(p) ? "play" : _nextBtn.Contains(p) ? "next" :
            _lockBtn.Contains(p) ? "lock" : _shotBtn.Contains(p) ? "shot" : _onAirRect.Contains(p) ? "onair" :
            _beatRect.Contains(p) ? "beat" : _hearRect.Contains(p) ? "hear" :
            ClockHit(p) ? "clock" : SongBarHit400(p) ? "nps" : !_npRect.IsEmpty && _npRect.Contains(p) ? "np" : null;

        private string TipFor(string h)
        {
            if (h != null && h.StartsWith("tk")) return TopKeyTip(h);   // 4.0
            if (h == "nps") return SongTimes ?? "";                      // 4.0 finale
            switch (h)
            {
                case "clock": return SleepLeft >= 0 ? "Sleep-Timer: noch " + (SleepLeft / 60) + ":" + (SleepLeft % 60).ToString("00") + " Minuten – klicken: ändern oder stoppen"
                                                    : "Uhrzeit" + (ClockStudio ? " – rechts unten: Laufzeit (seit dem Start, " + Theme.RunTimeText() + " Std.)" : "") +
                                                      "\nKlicken: Sleep-Timer · Einstellen: OPTIONEN → Aussehen → Uhr";
                case "scene": return "Szenen: Mix speichern und mit einem Klick laden\nAuch: Profil pro Spiel";
                case "hear": return HearTip;
                case "beat": return _bpm > 0 ? "Takt-Lampe: etwa " + (int)Math.Round(_bpm) + " Schläge pro Minute – blinkt im Takt (geschätzt aus dem, was gerade läuft)"
                        : "Takt-Lampe: noch kein Takt erkannt – läuft Musik mit klarem Rhythmus, steht hier nach ein paar Sekunden das Tempo";
                case "sk0": case "sk1": case "sk2": case "sk3":
                    {
                        int i = h[2] - '0';
                        return SceneKeys != null && i < SceneKeys.Length ? "Szene " + (i + 1) + ": „" + SceneKeys[i] + "“ laden" : null;
                    }
                case "rec": return "Aufnahme als WAV starten/stoppen – Rechtsklick: was aufnehmen, Ordner";
                case "night": return "Nachtmodus: Kompressor auf allen Kanälen\n(leise Stellen lauter, laute leiser)";
                case "duck": return "Ducking an/aus – Rechtsklick: einstellen";
                case "mini": return "Mini-Ansicht: kleines Fenster, immer im Vordergrund";
                case "opt": return "Einstellungen öffnen – Rechtsklick: Schnellmenü";
                case "wmin": return "Minimieren";
                case "wmax": return WindowMaximized ? "Wiederherstellen" : "Maximieren";
                case "wclose": return "Schließen";
                case "prev": return "Musik: zurück (vorheriger Titel)";
                case "play": return "Musik: Play / Pause\n(Spotify, YouTube im Browser, Media Player …)";
                case "next": return "Musik: weiter (nächster Titel)";
                case "lock": return Locked ? "Pult ist gesperrt – klicken zum Entsperren" : "Pult sperren: Klicks und Mausrad auf Fadern und Tasten tun nichts mehr\n(Tastenkürzel und MIDI gehen weiter)";
                case "shot": return "Bild vom Pult speichern (Ordner " + MixerSettings.ShotFolderName + ")";
                case "onair": return (OnAir ? "ON AIR – " : "Aus – ") + (OnAirMode == 2 ? "leuchtet bei Aufnahme oder wenn der Stream-Mix läuft" : "leuchtet, solange aufgenommen wird");
                case "np":
                    {
                        var np = NowPlaying;
                        if (np == null) return null;
                        return (np.Playing ? "Jetzt läuft" : "Pausiert") + (string.IsNullOrEmpty(np.App) ? "" : " (" + np.App + ")") + ":\n" +
                               (string.IsNullOrEmpty(np.Artist) ? "" : np.Artist + "\n") + np.Title + "\nKlicken: Play / Pause";
                    }
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            string tip = TipFor(h);
            if (h != _tipShown)
            {
                _tip.SetToolTip(this, tip);
                _tipShown = h;
            }
            if (h != _hover)
            {
                bool skOld = _hover != null && _hover.StartsWith("sk"), skNew = h != null && h.StartsWith("sk");
                _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate();
                if (skOld || skNew) SceneKeyHover?.Invoke(skNew ? h[2] - '0' : -1);   // 3.5: Szenen-Vorschau
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { bool sk = _hover.StartsWith("sk"); _hover = null; Invalidate(); if (sk) SceneKeyHover?.Invoke(-1); }
        }

        private readonly ToolTip _tip = PultTip.Make(new ToolTip());
        private string _tipShown;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string h = HitTest(e.Location);
            if (h == null) return;
            bool right = e.Button == MouseButtons.Right;
            if (!right && e.Button != MouseButtons.Left) return;
            if (!right && (h == "duck" || h == "night" || h == "prev" || h == "play" || h == "next" || h == "np" || (h != null && h.StartsWith("sk")))) KeyPressed?.Invoke();
            if (h.StartsWith("tk")) { if (!right) KeyPressed?.Invoke(); TopKeyDown(h, right); return; }   // 4.0
            switch (h)
            {
                case "wmin": if (!right) WinMinimize?.Invoke(); break;
                case "wmax": if (!right) WinMaxRestore?.Invoke(); break;
                case "wclose": if (!right) WinClose?.Invoke(); break;
                case "nps": if (!right) SongSeek?.Invoke(SongFracAt400(e.X)); break;   // 4.0 finale: Klick springt
                case "np": if (!right) MediaClicked?.Invoke("play"); else MediaMenu?.Invoke(PointToScreen(new Point((int)_npRect.X, (int)_npRect.Bottom + 2))); break;
                case "duck": if (right) DuckMenu?.Invoke(); else DuckClicked?.Invoke(); break;
                case "rec": if (right) RecMenu?.Invoke(); else RecClicked?.Invoke(); break;
                case "night": if (!right) NightClicked?.Invoke(); break;
                case "mini": if (!right) MiniClicked?.Invoke(); break;
                case "clock": ClockClicked?.Invoke(PointToScreen(new Point((int)_clockRect.X, (int)_clockRect.Bottom + 2))); break;
                case "scene": ScenesClicked?.Invoke(); break;
                case "sk0": case "sk1": case "sk2": case "sk3": if (!right) SceneKeyClicked?.Invoke(h[2] - '0'); break;
                case "opt": if (right) OptionsMenu?.Invoke(); else OptionsClicked?.Invoke(); break;
                case "prev":
                case "play":
                case "next":
                    if (!right) MediaClicked?.Invoke(h);
                    else { var mr = h == "prev" ? _prevBtn : h == "play" ? _playBtn : _nextBtn; MediaMenu?.Invoke(PointToScreen(new Point((int)mr.X, (int)mr.Bottom + 2))); }   // 4.0
                    break;
                case "lock": if (!right) LockClicked?.Invoke(); break;
                case "shot": if (!right) ShotClicked?.Invoke(); break;
            }
        }
    }

    internal sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.MenuBg;
        public override Color ImageMarginGradientBegin => Theme.MenuBg;
        public override Color ImageMarginGradientMiddle => Theme.MenuBg;
        public override Color ImageMarginGradientEnd => Theme.MenuBg;
        public override Color MenuBorder => Theme.MenuBorder;
        public override Color MenuItemBorder => Theme.MenuHover;
        public override Color MenuItemSelected => Theme.MenuHover;
        public override Color MenuItemSelectedGradientBegin => Theme.MenuHover;
        public override Color MenuItemSelectedGradientEnd => Theme.MenuHover;
        public override Color MenuItemPressedGradientBegin => Theme.MenuHover;
        public override Color MenuItemPressedGradientEnd => Theme.MenuHover;
        public override Color SeparatorDark => Theme.MenuSep;
        public override Color SeparatorLight => Theme.MenuSepLight;
        public override Color CheckBackground => Theme.CheckBg;
        public override Color CheckSelectedBackground => Theme.CheckSelBg;
        public override Color CheckPressedBackground => Theme.CheckSelBg;
    }

    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Tag as string == "header")
                e.TextColor = Theme.TextFaint;
            else if (e.Item.Tag as string == "hint")
                e.TextColor = Theme.TextDim;
            else
                e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextDim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            if (e.Item.Image != null && !((e.Item.Owner as ToolStripDropDownMenu)?.ShowCheckMargin ?? false))
            {
                base.OnRenderItemCheck(e);
                return;
            }
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Math.Min(r.Width, r.Height) * 0.35f;
            using (var pen = new Pen(Theme.AccentText, Math.Max(1.5f, s * 0.35f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - s, cy), new PointF(cx - s * 0.3f, cy + s * 0.7f), new PointF(cx + s, cy - s * 0.7f) });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var r = e.Item.ContentRectangle;
            using (var pen = new Pen(Theme.MenuSep))
                e.Graphics.DrawLine(pen, r.X + 4, r.Y + r.Height / 2, r.Right - 4, r.Y + r.Height / 2);
        }
    }
}
