using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Optik: Design folgt Windows (hell/dunkel), Lautheits-Radar, Hintergrund tanzt zur Musik, Cover als
    /// Hintergrund, Drehknoepfe, Nixie-Balken, bewegte GIFs, Motor-Surren, mehr Saison-Deko, Klick-Arten.
    /// Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyOptik400(MixerSettings s, bool demo)
        {
            Theme.NixieBar = s.NixieBarOn;
            StripBase.KnobView = s.KnobViewOn;
            MeterBridge.RadarOn = s.RadarOn;
            ChannelImages.GifOn = s.GifOn;
            SeasonDeco.MoreOn = s.MoreDecoOn;
            SeasonDeco.BirthDay = s.BirthDay;
            SeasonDeco.BirthMonth = s.BirthMonth;
            if (demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CLICKKIND"), out int ck)) s.ClickKind = Math.Max(0, Math.Min(3, ck));
            if (s.FollowWinTheme)
            {
                bool light = WindowsLight(demo);
                _winThemeLight = light ? 1 : 0;
                s.UiStyle = StyleForWindows(s, light);
            }
            StripBase.MotorStarted -= OnMotorStarted400;
            StripBase.MotorStarted += OnMotorStarted400;
        }

        private double _nextOptik400, _nextWinTheme;

        private void TickOptik400(double now, float dt)
        {
            LoudnessSink.BassOn = _settings.ShakeOn || _settings.MusicBgOn || (_settings.WledOn && _settings.WledMode == 2) || _settings.LightShowOn || (_settings.AnimDecoOn && _settings.DekoOn) || Storm430;   // 4.0 finale: Lichtshow, Deko; 4.3: Gewitter
            TickMotorSound400();
            if (now >= _nextWinTheme) { _nextWinTheme = now + 2.0; TickWinTheme400(false); }
            if (_settings.RadarOn)
            {
                var v = _snap.Loudness;
                RadarView.Momentary = v != null && v.Length > 0 && !float.IsInfinity(v[0]) ? v[0] : float.NaN;
                RadarView.ShortTerm = v != null && v.Length > 1 && !float.IsInfinity(v[1]) && v[1] > -70f ? v[1] : float.NaN;
                RadarView.Target = OutputStrip.LufsTargetDb < 0f ? OutputStrip.LufsTargetDb : -18f;
            }
            if (now < _nextOptik400) return;
            _nextOptik400 = now + 0.033;
            TickMusicBg400(dt);
            TickCoverBg400();
            if (_settings.GifOn && PultShown) foreach (var s in _channelStrips) s.TickGif();
        }

        /// <summary>Album-Cover wird gebraucht (Hintergrund, Mini-Player, Widget).</summary>
        private bool CoverWanted400 => (_settings.CoverBgOn && PultShown) || CoverWantedWindows400 || TurntableShown430;   // 4.3: Plattenspieler
        /// <summary>"Jetzt laeuft" wird gebraucht (Interpret-Lautstaerke, Musik-Tasten, Fenster, OBS …).</summary>
        private bool NowPlayingWanted400 => _settings.ArtistVolOn || _settings.MediaMoreOn || CoverWantedWindows400;

        /// <summary>Lautheit muss fuer Radar bzw. Musik-Hintergrund gemessen werden.</summary>
        private bool NeedsLoudness400 => PultShown && (_settings.RadarOn || _settings.MusicBgOn || _settings.LightShowOn || (_settings.AnimDecoOn && _settings.DekoOn) || (_settings.NeonOn && _settings.NeonPulse) || Storm430);   // 4.3: Gewitter

        // ================================================================ 177 Design folgt Windows

        private int _winThemeLight = -1;

        /// <summary>Windows: "Standard-App-Modus" hell? (Registrierung; Vorschau: MISCHPULT_DEMO_WINLIGHT=1)</summary>
        internal static bool WindowsLight(bool demo)
        {
            if (demo) return Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WINLIGHT") == "1";
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
            }
            catch { return false; }
        }

        internal static int StyleForWindows(MixerSettings s, bool light) =>
            light ? (s.WinLightStyle ?? Theme.StyleLight) : (s.WinDarkStyle ?? Theme.StyleStudio);

        private void TickWinTheme400(bool force)
        {
            if (!_settings.FollowWinTheme) { _winThemeLight = -1; return; }
            bool light = WindowsLight(_demo);
            int now = light ? 1 : 0;
            if (now == _winThemeLight && !force) return;
            _winThemeLight = now;
            int style = StyleForWindows(_settings, light);
            if (style != _settings.UiStyle) SetScheme(_settings.ColorScheme, _settings.AccentColor, style);
        }

        // ================================================================ 192 Hintergrund tanzt zur Musik

        private float _mbGlow, _mbAvg;
        private int _mbQ = -1;

        private void TickMusicBg400(float dt)
        {
            if (!_settings.MusicBgOn || !PultShown || _channelHost == null) { if (_mbQ >= 0) { _mbQ = -1; _mbGlow = 0f; _channelHost?.Invalidate(false); } return; }
            float bass = _snap.MasterBass;
            float lvl = bass >= 0f ? Math.Min(1f, bass * 3.2f) : Math.Min(1f, Math.Max(_master.LastPeakL, _master.LastPeakR));
            _mbAvg += (lvl - _mbAvg) * Math.Min(1f, 0.033f * 0.8f);
            float target = Math.Max(0f, Math.Min(1f, lvl * 0.75f + Math.Max(0f, lvl - _mbAvg) * 1.5f));
            if (A11yCalm) target = Math.Min(target, 0.35f);   // weniger Bewegung: nur sanftes Gluehen
            float k = target > _mbGlow ? (A11yCalm ? 0.1f : 0.55f) : (A11yCalm ? 0.03f : 0.09f);
            _mbGlow += (target - _mbGlow) * k;
            int q = (int)Math.Round(_mbGlow * 40);
            if (q == _mbQ) return;
            _mbQ = q;
            _channelHost.Invalidate(new Rectangle(0, 0, _channelHost.ClientSize.Width, _channelHost.ClientSize.Height), false);
        }

        private void PaintMusicBg400(StripHost h, Graphics g)
        {
            if (!_settings.MusicBgOn || h != _channelHost || _mbQ <= 0) return;
            float v = _mbGlow;
            var rc = h.ClientRectangle;
            if (rc.Width < 10 || rc.Height < 10) return;
            Color col = Theme.Accent;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = rc.Width * (0.9f + 0.5f * v), hh = rc.Height * (0.55f + 0.6f * v);
            var ell = new RectangleF(rc.Width / 2f - w / 2f, rc.Height - hh * 0.55f, w, hh);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ell);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(20 + 110 * v), col), SurroundColors = new[] { Color.FromArgb(0, col) } })
                    g.FillPath(b, path);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ 210 Cover als Hintergrund

        private Bitmap _cbSource, _cbBmp;
        private Size _cbSize;

        private void TickCoverBg400()
        {
            if (!_settings.CoverBgOn || _channelHost == null) { if (_cbBmp != null) { _cbBmp.Dispose(); _cbBmp = null; _cbSource = null; _channelHost?.Invalidate(false); } return; }
            var cover = _nowPlaying.Current?.Cover;
            var size = _channelHost.ClientSize;
            if (ReferenceEquals(cover, _cbSource) && size == _cbSize) return;
            _cbSource = cover; _cbSize = size;
            _cbBmp?.Dispose();
            _cbBmp = cover == null || size.Width < 10 || size.Height < 10 ? null : BlurCover(cover, size);
            _channelHost.Invalidate(false);
        }

        /// <summary>Cover stark weichgezeichnet (winzig verkleinert, dann gross gezogen), abgedunkelt und halb durchsichtig.</summary>
        internal static Bitmap BlurCover(Bitmap src, Size size)
        {
            try
            {
                int sw = 20, sh = Math.Max(4, (int)Math.Round(20.0 * size.Height / Math.Max(1, size.Width)));
                using (var tiny = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(tiny))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        float k = Math.Max(sw / (float)src.Width, sh / (float)src.Height);
                        float iw = src.Width * k, ih = src.Height * k;
                        g.DrawImage(src, new RectangleF((sw - iw) / 2f, (sh - ih) / 2f, iw, ih));
                    }
                    var big = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(big))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            var cm = new ColorMatrix { Matrix33 = 0.42f, Matrix00 = 0.8f, Matrix11 = 0.8f, Matrix22 = 0.8f };
                            ia.SetColorMatrix(cm);
                            g.DrawImage(tiny, new Rectangle(0, 0, size.Width, size.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, ia);
                        }
                    }
                    return big;
                }
            }
            catch { return null; }
        }

        private void PaintCoverBg400(StripHost h, Graphics g)
        {
            if (!_settings.CoverBgOn || h != _channelHost || _cbBmp == null) return;
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(_cbBmp, 0, 0);
            g.InterpolationMode = im;
        }

        /// <summary>Aus PaintHost346: Hintergruende von 4.0 (vor dem Spektrum).</summary>
        private void PaintHost400(StripHost h, Graphics g)
        {
            PaintGifBg400(h, g);     // 4.0 finale: bewegtes Hintergrundbild
            PaintCoverBg400(h, g);
            PaintMusicBg400(h, g);
            PaintWall400(h, g);      // 4.0 finale: Neon-Schild, Scheinwerfer
        }

        // ================================================================ 202 Motor-Surren

        private float _motorPending = -1f, _motorDist;
        private long _motorLastMs;

        private void OnMotorStarted400(float seconds, float dist)
        {
            if (!_settings.MotorSoundOn) return;
            _motorPending = Math.Max(_motorPending, seconds);
            _motorDist = Math.Max(_motorDist, dist);
        }

        private void TickMotorSound400()
        {
            if (_motorPending < 0f) return;
            float len = _motorPending, dist = _motorDist;
            _motorPending = -1f; _motorDist = 0f;
            if (!_settings.MotorSoundOn || _top.RecOn || !PultShown) return;   // nicht in eine Aufnahme surren
            long now = Environment.TickCount;
            if (now - _motorLastMs < 120) return;
            _motorLastMs = now;
            try
            {
                var clip = Beeps.MotorWhirr(len, dist);
                float gain = Beeps.MotorGainOf(_settings.MotorSoundLevel);
                if (_snap.EqSupported || _demo) Backend.PlaySound(clip, gain, true, false, false);
                else if (Win32.IsWindows)
                {
                    _clickPlayer?.Dispose();
                    _clickPlayer = new System.Media.SoundPlayer(new System.IO.MemoryStream(Beeps.ToWav(clip, gain)));
                    _clickPlayer.Play();
                }
            }
            catch { }
        }

        // ================================================================ Einstellungen

        private void SetOptik400(Action change, bool restyle = false, bool relayout = false)
        {
            change();
            ApplyOptik400(_settings, false);
            if (restyle) SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);
            if (relayout) RelayoutAllStrips();
            RestyleBridges();
            _top.RefreshDeco();
            _channelHost?.Invalidate(false);
            ScheduleSave();
        }

        private void AskBirthday400()
        {
            string cur = _settings.BirthMonth > 0 ? _settings.BirthDay.ToString("00") + "." + _settings.BirthMonth.ToString("00") + "." : "";
            using (var d = new InputDialog("Geburtstag", "Tag und Monat, z. B. 24.12. (leer = keiner):", cur, 6))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!ParseDayMonth(d.Value, out int day, out int month)) { if (d.Value.Length > 0) ShowOsd("Geburtstag", "Bitte so eingeben: 24.12.", -1, Theme.MuteRed); else SetOptik400(() => { _settings.BirthDay = 0; _settings.BirthMonth = 0; }); return; }
                SetOptik400(() => { _settings.BirthDay = day; _settings.BirthMonth = month; _settings.MoreDecoOn = true; });
            }
        }

        /// <summary>"24.12." / "24.12" / "24 12" → Tag und Monat.</summary>
        internal static bool ParseDayMonth(string s, out int day, out int month)
        {
            day = month = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Trim().TrimEnd('.').Split(new[] { '.', ' ', '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[0], out day) || !int.TryParse(parts[1], out month)) return false;
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month)) { day = month = 0; return false; }
            return true;
        }

        private void AddLook400(SPage p)
        {
            p.Section("4.0 Optik");
            p.Toggle("Design folgt Windows (hell/dunkel)", () => "Stellst du Windows auf hell, wechselt das Pult auf ein helles Design, bei dunkel auf ein dunkles. Welche, stellst du darunter ein. " +
                    (_settings.FollowWinTheme ? "Gerade ist Windows " + (WindowsLight(_demo) ? "hell." : "dunkel.") : "") + (_settings.TimeDesignOn ? " „Design nach Uhrzeit“ ruht, solange das hier an ist." : ""),
                () => _settings.FollowWinTheme, v => { _settings.FollowWinTheme = v; ScheduleSave(); TickWinTheme400(true); });
            p.Choice("Design bei hellem Windows", () => Theme.StyleHints[StyleForWindows(_settings, true)], Theme.StyleNames, () => StyleForWindows(_settings, true),
                i => { _settings.WinLightStyle = i; ScheduleSave(); TickWinTheme400(true); }).Enabled = () => _settings.FollowWinTheme;
            p.Choice("Design bei dunklem Windows", () => Theme.StyleHints[StyleForWindows(_settings, false)], Theme.StyleNames, () => StyleForWindows(_settings, false),
                i => { _settings.WinDarkStyle = i; ScheduleSave(); TickWinTheme400(true); }).Enabled = () => _settings.FollowWinTheme;
            p.Toggle("Lautheits-Radar", "Rundes Radar in der Meterbridge: Ein Zeiger kreist einmal pro Minute und malt die Lautheit als Tortenstücke – innen leise, außen laut, grün ums Ziel, gelb und rot darüber. In der Mitte steht, wie weit es gerade vom Ziel weg ist (LU). Ziel: das Lautheits-Ziel, sonst −18 LUFS.",
                () => _settings.RadarOn, v => SetOptik400(() => _settings.RadarOn = v));
            p.Toggle("Hintergrund tanzt zur Musik", "Hinter den Kanälen glüht es von unten in der Akzentfarbe – stärker, wenn der Bass kommt.",
                () => _settings.MusicBgOn, v => SetOptik400(() => _settings.MusicBgOn = v));
            p.Toggle("Cover als Hintergrund", "Das Album-Cover von dem, was gerade läuft, liegt weich verschwommen und abgedunkelt hinter den Kanälen.",
                () => _settings.CoverBgOn, v => SetOptik400(() => _settings.CoverBgOn = v));
            p.Toggle("Drehknöpfe statt Fader", "Jeder Zug bekommt einen großen Drehknopf statt des Schiebereglers – mit der Maus nach oben/unten ziehen oder am Mausrad drehen. Die Pegel bleiben daneben.",
                () => _settings.KnobViewOn, v => SetOptik400(() => _settings.KnobViewOn = v, false, true));
            p.Toggle("Nixie-Balken", "Die Pegel leuchten wie Nixie-Balkenröhren: orange glimmendes Neon hinter braunem Glas, statt LEDs.",
                () => _settings.NixieBarOn, v => SetOptik400(() => _settings.NixieBarOn = v, true));
            p.Toggle("Bewegte GIF-Bilder", "Ist das Bild eines Kanals ein bewegtes GIF (Rechtsklick → Bild …), läuft es im App-Feld ab. Ruht hinter einem Vollbild-Spiel und wenn das Pult minimiert ist.",
                () => _settings.GifOn, v => SetOptik400(() => _settings.GifOn = v));
            p.Toggle("Motor-Surren", "Fahren die Motorfader beim Laden einer Szene, surren sie leise wie echte Motoren – nur auf deinem Ausgang, nicht im Stream-Mix und nicht während einer Aufnahme.",
                () => _settings.MotorSoundOn, v => { _settings.MotorSoundOn = v; ScheduleSave(); });
            p.Choice("Lautstärke des Surrens", () => "Wie laut die Motorfader surren.", new[] { "Leise", "Mittel", "Laut" }, () => _settings.MotorSoundLevel,
                i => { _settings.MotorSoundLevel = i; ScheduleSave(); OnMotorStarted400(0.5f, 0.6f); }).Enabled = () => _settings.MotorSoundOn;
            p.Toggle("Mehr Saison-Deko", () => "Zur Saison-Deko kommen Ostern (bunte Eier), Frühling (Blumen), Sommer (Sonne), Herbst (fallende Blätter) und dein Geburtstag (Torte mit Kerze und Konfetti)." +
                    (_settings.MoreDecoOn ? " Gerade: " + SeasonDeco.Name(SeasonDeco.For(_settings.SeasonMode, DateTime.Now)) + "." : ""),
                () => _settings.MoreDecoOn, v => SetOptik400(() => _settings.MoreDecoOn = v));
            p.Button("Geburtstag", "An diesem Tag gibt es Torte und Konfetti in der Kopfleiste (nur mit „Mehr Saison-Deko“).", "Datum …", r => AskBirthday400(),
                () => _settings.BirthMonth > 0 ? _settings.BirthDay + "." + _settings.BirthMonth + "." : "keiner").Enabled = () => _settings.MoreDecoOn;
            p.Choice("Klick-Art", () => "So klingt das Tasten-Klicken (Leuchten & Anzeigen → Tasten klicken).", MixerSettings.ClickKindNames, () => _settings.ClickKind,
                i => { _settings.ClickKind = i; _settings.KeyClickOff = false; ScheduleSave(); _lastClickMs = 0; KeyClick(); });
        }
    }
}
