using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 (finale) Optik: Lichtshow, Neon-Schild, Himmel, weiches Umblenden, Song-Fortschritt, Kanal-Bild ueber den Zug,
    /// Musik-Wallpaper, animierte Deko, bewegtes Hintergrundbild, Motor-Potis, Leuchtspur. Alles einzeln schaltbar.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Einstellungen anwenden

        private void ApplyFinal400(MixerSettings s, bool demo)
        {
            if (demo) DemoFinal400(s);
            int top = s.NeonOn || s.LightShowOn ? 34 : 0;
            bool relayout = top != StripHost.ExtraTopPx;
            StripHost.ExtraTopPx = top;
            Theme.MoreDeko = s.AnimDecoOn;
            NowPlayingWatcher.WantTimeline = s.SongProgressOn;
            ChannelStrip.ImageWash = s.ImageWashOn;
            ChannelStrip.MotorPots = s.MotorPotsOn;
            StripBase.TrailOn = s.FaderTrailOn;
            StripBase.FinalOverlay = s.LightShowOn ? (Action<StripBase, Graphics>)LightOverlay400 : null;
            if (relayout && _channelHost != null && IsHandleCreated) { RelayoutAllStrips(); _channelHost.Invalidate(true); _outputHost?.Invalidate(true); }
            ApplyFinalWin400(s);
        }

        private void DemoFinal400(MixerSettings s)
        {
            string nt = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_NEONTEXT");
            if (!string.IsNullOrEmpty(nt)) s.NeonText = nt;
            s.NeonColor = DemoInt("NEONCOLOR", s.NeonColor);
            if (DemoOn("NEONPULSE")) s.NeonPulse = true;
            s.WallpaperStyle = DemoInt("WALLSTYLE", s.WallpaperStyle);
            if (DemoOn("DEKO3")) { s.DekoOn = true; s.AnimDecoOn = true; s.Deko = new List<DekoConfig> { new DekoConfig { Kind = 7, X = 870, Y = 420 } }; }   // 4.3: Plattenspieler
            if (DemoOn("DEKO2")) { s.DekoOn = true; s.AnimDecoOn = true; s.Deko = new List<DekoConfig> { new DekoConfig { Kind = 4, X = 900, Y = 380 }, new DekoConfig { Kind = 5, X = 1060, Y = 600 }, new DekoConfig { Kind = 6, X = 860, Y = 640 } }; }
        }

        /// <summary>Uhrzeit fuer den Himmel (Vorschau: MISCHPULT_DEMO_SKYHOUR).</summary>
        private DateTime SkyTime400
        {
            get
            {
                if (_demo && double.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SKYHOUR"), NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                    return DateTime.Today.AddHours(Math.Max(0, Math.Min(23.99, h)));
                return DateTime.Now;
            }
        }

        private void HookFinal400()
        {
            _top.SongSeek += SeekFrac400;
            if (_settings.WallpaperOn) SetWallpaper400(true, false);
            LoadGifBg400();
            if (_settings.SkyOn || Theme.DesignBg) { _designBg400 = Theme.DesignBg; _designBgKind420 = Theme.Design; RebuildBgImage(); }
            HookFinalWin400();
            if (_demo) DemoShownFinal400();
        }

        /// <summary>Vorschau: neue Fenster auf Wunsch gleich oeffnen.</summary>
        private void DemoShownFinal400()
        {
            if (DemoOn("COMPWIN") && _settings.Channels.Count > 1) DemoLater(() =>
            {
                var c = _settings.Channels[1]; c.CompOn = true;
                c.Comp400 = new CompConfig { Thr = -30f, Ratio = 3.5f, Att = 8f, Rel = 250f, Makeup = 6f, Knee = 8f };
                PushPolicy(); ShowComp400(c);
                if (_compWins.TryGetValue(c.Id, out var w)) { w.Location = DemoXY("COMPXY", new Point(1000, 60)); var rnd = new Random(4); for (int i = 0; i < 180; i++) w.Feed((float)(4 + 4 * Math.Sin(i / 9.0) + rnd.NextDouble() * 2), -20f); }
            });
            if (DemoOn("XFWIN")) DemoLater(() =>
            {
                if (_settings.CrossA == null && _settings.Channels.Count > 3) { _settings.CrossA = _settings.Channels[3].Id; _settings.CrossB = _settings.Channels[4 % _settings.Channels.Count].Id; _settings.CrossPos = 0.35f; }
                ShowCross400(); _xfWin.Location = DemoXY("XFXY", new Point(300, 700));
            });
            if (DemoOn("GENWIN")) DemoLater(() => { ShowGen400(); _genWin.Location = DemoXY("GENXY", new Point(1100, 400)); });
            if (DemoOn("GALLERYWIN")) DemoLater(() => { ShowGallery400(); _gallery.Location = DemoXY("GALLERYXY", new Point(900, 120)); });
            if (DemoOn("EQOVWIN")) DemoLater(() =>
            {
                ShowEqOverview400(); _eqOv.Location = DemoXY("EQOVXY", new Point(820, 100));
                if (_settings.MaskingOn && _settings.Channels.Count > 3)
                {
                    // Vorschau: zwei Kanaele, die sich bei 2 kHz verdecken
                    var a = new float[SpectrumAnalyzer.Bands]; var b = new float[SpectrumAnalyzer.Bands];
                    for (int i = 0; i < a.Length; i++) { double f = SpectrumAnalyzer.BandFreq(i + 0.5); a[i] = (float)(-30 - 8 * Math.Abs(Math.Log(f / 2000, 2))); b[i] = (float)(-32 - 6 * Math.Abs(Math.Log(f / 1800, 2))); }
                    _maskAvg[_settings.Channels[3].Id] = a; _maskAvg[_settings.Channels[2].Id] = b;
                }
            });
            if (DemoOn("PALETTE")) DemoLater(() => { ShowPalette400(); string q = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PALETTEQ"); if (q != null && _palette != null) _palette.Query = q; });
            if (DemoOn("ABOUTWIN")) DemoLater(() => { ShowAbout(); if (DemoOn("CHRONIK")) _about?.ShowChronicle(); _about.Location = DemoXY("ABOUTXY", new Point(560, 160)); });
            if (DemoOn("EGG")) DemoLater(StartEgg400);
        }

        private double _nextFinal400, _nextSky400, _nextSong400, _nextWall400, _nextGif400, _nextGifCheck400;

        private void TickFinal400(double now, float dt)
        {
            foreach (var s in _channelStrips) s.TickFinal400(dt);
            if (_settings.LightShowOn) BeatTracker.On = true;
            else if (BeatTracker.On && !_settings.BpmOn) BeatTracker.On = false;
            TickDecoMusic400(dt);
            if (now >= _nextFinal400)
            {
                _nextFinal400 = now + 0.033;
                TickLight400(now, 0.033f);
                TickNeon400(now);
            }
            if (_settings.SkyOn && now >= _nextSky400) { bool first = _nextSky400 == 0; _nextSky400 = now + 300; if (!first) RebuildBgImage(); }   // alle 5 Minuten neu
            if (now >= _nextSong400) { _nextSong400 = now + 0.25; TickSong400(); }
            if (_wall != null && now >= _nextWall400) { _nextWall400 = now + 0.04; TickWallpaper400(); }
            if (_gifBg != null && now >= _nextGif400) { _nextGif400 = now + 0.05; TickGifBg400(); }
            if ((_settings.BgGifOn || _gifBg != null) && now >= _nextGifCheck400) { _nextGifCheck400 = now + 1.0; LoadGifBg400(); }   // anderes Bild gewaehlt?
            TickFinalWin400(now, dt);
        }

        // ================================================================ 222 Lichtshow

        private Theme.Spot[] _spots;
        private float _lsInt, _lsFlash, _lsBassAvg, _lsLen;
        private int _lsStep;
        private double _lsT, _lsQuiet, _lsBeatT0 = -1, _lsPeriod;

        private void TickLight400(double now, float dt)
        {
            if (!_settings.LightShowOn || !PultShown || _channelHost == null)
            {
                if (_spots != null) { _spots = null; _channelHost?.Invalidate(true); }
                return;
            }
            float lvl = Math.Min(1f, _master?.LevelNow ?? 0f);
            float bass = _snap.MasterBass >= 0f ? Math.Min(1f, _snap.MasterBass * 3.2f) : lvl;
            if (lvl > 0.02f) _lsQuiet = 0; else _lsQuiet += dt;
            float target = _lsQuiet > 2.5 ? 0f : 0.35f + 0.65f * lvl;
            if (A11yCalm) target *= 0.5f;
            _lsInt += (target - _lsInt) * Math.Min(1f, dt * 4f);
            _lsBassAvg += (bass - _lsBassAvg) * Math.Min(1f, dt * 1.5f);
            if (!A11yCalm && bass > 0.18f && bass > _lsBassAvg * 1.55f && _lsFlash < 0.2f) { _lsFlash = 1f; _lsStep++; }
            _lsFlash = Math.Max(0f, _lsFlash - dt / 0.14f);
            // im Takt schwenken (geschaetzter Schlag), sonst gemaechlich je nach Lautstaerke
            double phase;
            var b = _snap?.Beat;
            if (b != null && b.Length >= 2 && b[0] > 0)
            {
                double period = 60.0 / b[0], t0 = now - b[1] / 1000.0;
                if (_lsBeatT0 < 0 || Math.Abs(period - _lsPeriod) > 0.02 * period) _lsBeatT0 = t0;
                _lsPeriod = period;
                phase = (now - _lsBeatT0) / period;
            }
            else { _lsBeatT0 = -1; _lsT += dt * (0.25 + 0.6 * lvl); phase = _lsT; }
            if (A11yCalm) phase *= 0.3;
            int w = _channelHost.ClientSize.Width, h = _channelHost.ClientSize.Height;
            int n = Math.Max(2, Math.Min(6, w / Math.Max(1, Theme.Px(260))));
            if (n % 2 == 1 && _settings.NeonOn) n++;
            var spots = new Theme.Spot[n];
            var neon = _settings.NeonOn ? NeonRect400(_channelHost) : RectangleF.Empty;
            for (int i = 0; i < n; i++)
            {
                float swing = 28f * (float)Math.Sin(phase * Math.PI + i * 1.3);
                // mit Neon-Schild: Scheinwerfer links und rechts davon
                float sx = neon.IsEmpty ? w * (i + 0.5f) / n
                         : i < n / 2 ? neon.X * (i + 0.5f) / (n / 2) : neon.Right + (w - neon.Right) * (i - n / 2 + 0.5f) / (n - n / 2);
                spots[i] = new Theme.Spot
                {
                    Src = new PointF(sx, Theme.F(13)),
                    Angle = i % 2 == 0 ? swing : -swing,
                    Spread = 10f,
                    Color = Theme.SpotColor(i, _lsStep + (int)(phase / 4)),
                };
            }
            _spots = spots;
            _lsLen = h * 1.15f;
            if (_lsInt > 0.005f || _lsFlash > 0f) _channelHost.Invalidate(true);
        }

        private void LightOverlay400(StripBase s, Graphics g)
        {
            var sp = _spots;
            if (sp == null || s.Parent != _channelHost) return;
            try { Theme.DrawCones(g, sp, _lsLen, _lsInt, _lsFlash, new RectangleF(0, 0, s.Width, s.Height), new PointF(s.Left, s.Top)); } catch { }
        }

        // ================================================================ 224 Neon-Schild, Wand oben

        private double _neonStart = -1;
        private float _neonOn, _neonPulse;
        private int _neonQ = -1;

        /// <summary>Helligkeit beim Einschalten: flackert ein paar Mal, dann ruhig; ab und zu ein kurzes Zucken.</summary>
        internal static float NeonLevel(double t)
        {
            if (t < 0) return 0f;
            if (t < 1.4)
            {
                double[] at = { 0.05, 0.12, 0.2, 0.24, 0.45, 0.52, 0.7, 0.9 };
                float[] v = { 1f, 0f, 1f, 0.1f, 1f, 0f, 0.5f, 1f };
                float cur = 0f;
                for (int i = 0; i < at.Length; i++) if (t >= at[i]) cur = v[i];
                return cur;
            }
            double cyc = (t - 1.4) % 27.0;
            return cyc > 26.75 && cyc < 26.85 ? 0.35f : 1f;
        }

        private void TickNeon400(double now)
        {
            if (!_settings.NeonOn || !PultShown || _channelHost == null) { if (_neonStart >= 0) { _neonStart = -1; _neonQ = -1; } return; }
            if (_neonStart < 0) _neonStart = now;
            _neonOn = A11yCalm ? 1f : NeonLevel(now - _neonStart);
            float lvl = Math.Min(1f, _master?.LevelNow ?? 0f);
            _neonPulse = _settings.NeonPulse && !A11yCalm ? _neonPulse + (lvl - _neonPulse) * 0.35f : 0f;
            int q = (int)(_neonOn * 20) * 100 + (int)(_neonPulse * 30);
            if (q == _neonQ) return;
            _neonQ = q;
            if (!_settings.LightShowOn) _channelHost.Invalidate(Rectangle.Ceiling(NeonRect400(_channelHost)), false);
        }

        private RectangleF NeonRect400(StripHost h)
        {
            float w = h.ClientSize.Width, wall = h.WallH;
            float nw = Math.Min(Theme.F(420), w * 0.42f);
            return new RectangleF((w - nw) / 2f, Theme.F(2), nw, wall - Theme.F(4));
        }

        /// <summary>Aus PaintHost400: Neon-Schild und Scheinwerfer oben an der Wand, Lichtkegel in den freien Flaechen.</summary>
        private void PaintWall400(StripHost h, Graphics g)
        {
            if (h != _channelHost) return;
            try { PaintWallCore400(h, g); } catch { }   // Zeichnen darf nie das Pult stoppen
        }

        private void PaintWallCore400(StripHost h, Graphics g)
        {
            PaintEgg400(h, g);   // 247
            if (_settings.NeonOn && _neonStart >= 0)
                Theme.DrawNeon(g, NeonRect400(h), _settings.NeonTextNow, Color.FromArgb(MixerSettings.NeonColorValues[_settings.NeonColor] | unchecked((int)0xff000000)), _neonOn, _neonPulse);
            var sp = _spots;
            if (_settings.LightShowOn && sp != null)
            {
                Theme.DrawCones(g, sp, _lsLen, _lsInt, _lsFlash, new RectangleF(0, 0, h.ClientSize.Width, h.ClientSize.Height), PointF.Empty);
                foreach (var s in sp) Theme.DrawSpotHousing(g, s, _lsInt);
            }
        }

        // ================================================================ 250 Deko zur Musik

        private void TickDecoMusic400(float dt)
        {
            if (!_settings.AnimDecoOn) return;
            float lvl = Math.Min(1f, _master?.LevelNow ?? 0f);
            float bass = _snap.MasterBass >= 0f ? Math.Min(1f, _snap.MasterBass * 3.2f) : lvl;
            Theme.DekoLevel += (lvl - Theme.DekoLevel) * Math.Min(1f, dt * 6f);
            Theme.DekoBass = bass > Theme.DekoBass ? bass : Math.Max(0f, Theme.DekoBass - dt * 3f);
            Theme.DekoBeat = Math.Max(_lsFlash, Math.Max(0f, Theme.DekoBeat - dt * 4f));
            if (bass > 0.25f && bass > _lsBassAvg * 1.5f) Theme.DekoBeat = 1f;
            if (!_settings.LightShowOn) _lsBassAvg += (bass - _lsBassAvg) * Math.Min(1f, dt * 1.5f);
            Theme.DekoPhase += dt * (0.4 + 2.2 * Theme.DekoLevel) * (A11yCalm ? 0.3 : 1.0);
        }

        // ================================================================ 225 Himmel, Weltraum, 227 weich umblenden

        private bool _designBg400;
        private int _designBgKind420 = -1;   // 4.2: Weltraum und Synthwave haben verschiedene Hintergruende

        private Bitmap CaptureForFade400()
        {
            if (!_settings.DesignFadeOn || !Visible || WindowState == FormWindowState.Minimized || !IsHandleCreated || StripBase.Calm || ClientSize.Width < 50) return null;
            // nur wenn das Pult (oder eines seiner Fenster) gerade vorne ist – sonst fotografiert es fremde Fenster mit
            if (Form.ActiveForm == null || _gaming || FullscreenAppInFront346() || !PultShown) return null;
            try
            {
                var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize);
                return bmp;
            }
            catch { return null; }
        }

        private void AfterScheme400(Bitmap shot)
        {
            if (Theme.DesignBg != _designBg400 || (Theme.DesignBg && Theme.Design != _designBgKind420)) { _designBg400 = Theme.DesignBg; _designBgKind420 = Theme.Design; RebuildBgImage(); }
            if (shot == null) return;
            try
            {
                var f = new FadeForm(shot, RectangleToScreen(ClientRectangle));
                f.Show(this);
            }
            catch { shot.Dispose(); }
        }

        // ================================================================ 228 Song-Fortschritt

        private static string Clock400(double sec) { int s = (int)Math.Max(0, Math.Round(sec)); return s / 60 + ":" + (s % 60).ToString("00"); }

        private bool SongProgress400(out float frac, out string text)
        {
            frac = -1f; text = null;
            if (!_settings.SongProgressOn) return false;
            var np = _nowPlaying?.Current;
            if (np == null || string.IsNullOrEmpty(np.Title)) return false;
            double len = np.LenSec, pos = np.PosNow(DateTime.UtcNow);
            if (_demo && len <= 0) { len = 214; pos = 83 + Now % 90; }   // Vorschau
            if (len <= 0 || pos < 0) return false;
            frac = (float)Math.Max(0, Math.Min(1, pos / len));
            text = Clock400(pos) + " / −" + Clock400(len - pos);
            return true;
        }

        private void TickSong400()
        {
            if (SongProgress400(out float f, out string t)) _top.SetSong400(f, t);
            else if (_top.SongFrac >= 0f) _top.SetSong400(-1f, null);
        }

        private void SeekFrac400(float f)
        {
            var np = _nowPlaying?.Current;
            if (_demo) { ShowOsd("Song", "springt zu " + Math.Round(f * 100) + " %", -1, Theme.Accent); return; }
            if (np == null || np.LenSec <= 0) return;
            double target = np.LenSec * Math.Max(0f, Math.Min(1f, f));
            System.Threading.ThreadPool.QueueUserWorkItem(_ => NowPlayingWatcher.SeekTo(target));
            ShowOsd("Song", Clock400(target) + " / " + Clock400(np.LenSec), -1, Theme.Accent);
        }

        // ================================================================ 230 Musik-Wallpaper

        private WallpaperForm _wall;
        private bool WallpaperWantsSpectrum400 => _settings.WallpaperOn && _wall != null && !_wall.IsDisposed;
        private readonly float[] _wallLvl = new float[48], _wallPk = new float[48];

        private void SetWallpaper400(bool on, bool osd)
        {
            try { _wall?.Close(); } catch { }
            _wall = null;
            if (!on) return;
            if (!Win32.IsWindows || _demo) { if (osd) ShowOsd("Musik-Wallpaper", "geht nur unter Windows", -1, Theme.TextDim); return; }
            var w = new WallpaperForm(_settings.WallpaperStyle);
            if (!w.AttachToDesktop()) { try { w.Dispose(); } catch { } if (osd) ShowOsd("Musik-Wallpaper", "Windows lässt das gerade nicht zu", -1, Theme.MuteRed); return; }
            _wall = w;
        }

        private void TickWallpaper400()
        {
            if (_wall == null || _wall.IsDisposed) { _wall = null; return; }
            if (!_spectrum.Active) return;
            _spectrum.Bars(_wallLvl.Length, _wallLvl, _wallPk);
            _wall.Feed(_wallLvl, _wallPk);
        }

        // ================================================================ 251 bewegtes Hintergrundbild

        private ChannelImages.Anim _gifBg;
        private string _gifBgPath;
        private int _gifBgFrame = -1;
        private static readonly System.Diagnostics.Stopwatch GifBgClock = System.Diagnostics.Stopwatch.StartNew();

        private void LoadGifBg400()
        {
            string path = _settings.BgGifOn ? _settings.BgImagePath : null;
            if (path == _gifBgPath && (_gifBg != null || path == null)) return;
            if (_gifBg?.Frames != null) foreach (var f in _gifBg.Frames) f?.Dispose();
            _gifBg = null; _gifBgPath = path; _gifBgFrame = -1;
            if (path == null || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                using (var fs = System.IO.File.OpenRead(path))
                using (var img = Image.FromStream(fs))
                    _gifBg = ChannelImages.Decode(img, 640, 48);
            }
            catch { _gifBg = null; }
            _channelHost?.Invalidate(false); _outputHost?.Invalidate(false);
        }

        private int GifBgIndex()
        {
            var a = _gifBg;
            if (a == null || a.Total <= 0) return 0;
            double u = (StripBase.Calm ? 0 : GifBgClock.Elapsed.TotalSeconds) % a.Total;
            for (int i = 0; i < a.Ends.Length; i++) if (u < a.Ends[i]) return i;
            return a.Ends.Length - 1;
        }

        private void TickGifBg400()
        {
            if (!PultShown) return;
            int i = GifBgIndex();
            if (i == _gifBgFrame) return;
            _gifBgFrame = i;
            _channelHost?.Invalidate(false); _outputHost?.Invalidate(false);
        }

        private void PaintGifBg400(StripHost h, Graphics g)
        {
            var a = _gifBg;
            if (a == null || !_settings.BgGifOn) return;
            var frame = a.Frames[Math.Max(0, Math.Min(a.Frames.Length - 1, _gifBgFrame < 0 ? 0 : _gifBgFrame))];
            var o = Theme.FormOrigin(h);
            int fw = ClientSize.Width, fh = ClientSize.Height;
            float k = Math.Max(fw / (float)frame.Width, fh / (float)frame.Height);
            float iw = frame.Width * k, ih = frame.Height * k;
            var dest = new RectangleF((fw - iw) / 2f - o.X, (fh - ih) / 2f - o.Y, iw, ih);
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(frame, dest);
            g.InterpolationMode = im;
            int dim = Math.Max(0, Math.Min(95, _settings.BgDim ?? 55));
            using (var b = new SolidBrush(Color.FromArgb(dim * 255 / 100, 0, 0, 0))) g.FillRectangle(b, h.ClientRectangle);
        }

        // ================================================================ Einstellungen (Aussehen)

        private void SetFinal400(Action change)
        {
            change();
            ApplyFinal400(_settings, false);
            foreach (var s in _channelStrips) s.InvalidateChrome();
            _channelHost?.Invalidate(true);
            _top?.Invalidate();
            ScheduleSave();
        }

        private void AddLookFinal400(SPage p)
        {
            p.Section("4.0 finale – Optik");
            p.Toggle("Lichtshow", "Bühnenscheinwerfer oben an der Wand: Lichtkegel schwenken im Takt über die Kanäle, bei kräftigem Bass blitzt es. In der Stille gehen sie aus.",
                () => _settings.LightShowOn, v => SetFinal400(() => _settings.LightShowOn = v));
            p.Toggle("Neon-Schild", "Ein Neon-Schriftzug oben an der Wand – mit eigenem Text und eigener Farbe. Flackert beim Einschalten kurz an.",
                () => _settings.NeonOn, v => SetFinal400(() => { _settings.NeonOn = v; _neonStart = -1; }));
            p.Button("Text des Neon-Schilds", "Höchstens 24 Zeichen.", "Ändern …", r => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Neon-Schild", "Text (z. B. ON AIR, Jans Studio):", _settings.NeonTextNow, 24))
                    if (dlg.ShowDialog(this) == DialogResult.OK) SetFinal400(() => { _settings.NeonText = dlg.Value.Trim(); _neonStart = -1; });
            })), () => _settings.NeonTextNow).Enabled = () => _settings.NeonOn;
            p.Choice("Farbe des Neon-Schilds", () => "", MixerSettings.NeonColorNames, () => _settings.NeonColor, i => SetFinal400(() => _settings.NeonColor = i)).Enabled = () => _settings.NeonOn;
            p.Toggle("Neon pulsiert zur Musik", "Je lauter, desto heller.", () => _settings.NeonPulse, v => SetFinal400(() => _settings.NeonPulse = v)).Enabled = () => _settings.NeonOn;
            p.Toggle("Himmel nach Tageszeit", "Hinter dem Pult ein Himmel, der mit der Uhrzeit geht: Morgenrot, Tag mit Wolken, Sonnenuntergang, Nacht mit Sternen und dem Mond in seiner echten Phase. (Ein eigenes Hintergrundbild hat Vorrang.)",
                () => _settings.SkyOn, v => { _settings.SkyOn = v; _nextSky400 = 0; RebuildBgImage(); ScheduleSave(); });
            p.Toggle("Weich umblenden beim Design-Wechsel", "Das alte Design blendet in 0,6 Sekunden ins neue über – auch beim automatischen Wechsel.",
                () => _settings.DesignFadeOn, v => SetFinal400(() => _settings.DesignFadeOn = v));
            p.Toggle("Song-Fortschritt", "Laufzeit des Songs als feine Leiste im „Jetzt läuft“-Feld, im Mini-Player und im Overlay (Zeiten im Tooltip). Klick auf die Leiste springt an die Stelle.",
                () => _settings.SongProgressOn, v => SetFinal400(() => _settings.SongProgressOn = v));
            p.Toggle("Kanal-Bild über den ganzen Zug", "Das eigene Bild des Kanals liegt blass über dem ganzen Kanalzug (nicht nur im Anzeigefeld).",
                () => _settings.ImageWashOn, v => SetFinal400(() => _settings.ImageWashOn = v));
            p.Toggle("Musik-Wallpaper", "Das Spektrum der Musik als Windows-Hintergrund – hinter den Desktop-Symbolen, über deinem eigenen Hintergrundbild. Beim Ausschalten kommt der normale Hintergrund zurück.",
                () => _settings.WallpaperOn, v => { _settings.WallpaperOn = v; SetWallpaper400(v, true); ScheduleSave(); });
            p.Choice("Aussehen des Wallpapers", () => "", MixerSettings.WallpaperStyleNames, () => _settings.WallpaperStyle, i => { _settings.WallpaperStyle = i; if (_wall != null) _wall.Style = i; ScheduleSave(); }).Enabled = () => _settings.WallpaperOn;
            p.Toggle("Animierte Deko", "Neue Deko-Stücke: Diskokugel (Lichtpunkte im Takt), Lava-Lampe (schneller bei lauter Musik), schlafende Katze (Ohren zucken beim Bass) und ein Plattenspieler – die Platte dreht sich, solange Musik läuft, das Etikett ist das Album-Cover. Hinstellen: Rechtsklick in eine freie Fläche → Deko hierhin (Deko muss an sein).",
                () => _settings.AnimDecoOn, v => SetFinal400(() => _settings.AnimDecoOn = v));
            p.Toggle("Bewegtes Hintergrundbild (GIF)", "Ist dein Hintergrundbild ein bewegtes GIF, läuft es in den freien Flächen ab.",
                () => _settings.BgGifOn, v => { _settings.BgGifOn = v; LoadGifBg400(); ScheduleSave(); });
            p.Toggle("Motor-Potis", "Beim Szenenwechsel drehen sich die EQ- und Panorama-Knöpfe sichtbar an ihre neue Stelle – wie die Motorfader.",
                () => _settings.MotorPotsOn, v => SetFinal400(() => _settings.MotorPotsOn = v));
            p.Toggle("Leuchtspur beim Fader-Ziehen", "Hinter der Faderkappe bleibt beim Ziehen kurz eine leuchtende Spur in der Kanalfarbe.",
                () => _settings.FaderTrailOn, v => SetFinal400(() => _settings.FaderTrailOn = v));
        }

        private void CleanupFinal400()
        {
            try { _wall?.Close(); } catch { }
            _wall = null;
            CleanupFinalWin400();
        }
    }
}
