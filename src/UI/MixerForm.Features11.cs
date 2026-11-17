using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.17 (3.0 Schritt 1): Anzeigefeld pro Kanal (Apps, Lautheit, Spektrum, Pegel-Verlauf, Album-Cover), Oszilloskop und
    /// "Jetzt laeuft" mit Cover in der Meterbridge, Spektrum zum Anfassen, Song-Verlauf, Hoer-Tagebuch mit App-Statistik.
    /// Alles einzeln in den Einstellungen an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private ListeningDiary _diary;
        private DiaryWindow _diaryWin;

        private void ApplyFeatures11Statics()
        {
            ChannelStrip.GlobalField = ChannelField.Clamp(_settings.ChannelField);
            SpectrumModel.PeakMode = _settings.SpecPeak ?? 1;
            MeterBridge.SpecHover = !_settings.SpecHoverOff;
        }

        private void InitFeatures11()
        {
            _bridge.OscOn = !_settings.OscOff;
            _bridge.CoverOn = !_settings.CoverOff;
            _bridge.OscClicked += ShowOscMenu;
            _bridge.CoverClicked += ShowSongMenu;
            if (_demo)
            {
                _diary = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_DIARY") == "1" ? ListeningDiary.Demo(DateTime.Now) : new ListeningDiary();
                if (_nowPlaying.Fixed != null && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_COVER") != "0") _nowPlaying.Fixed.Cover = DemoCover(320);
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SONGS") == "1") _settings.Songs = DemoSongs();
            }
            else _diary = ListeningDiary.Load();
        }

        // ================================================================ Takt

        private string[] _anLoudReq = new string[0], _anSpecReq = new string[0];
        private float _diaryAcc;

        private void TickFeatures11(float dt)
        {
            bool shown = PultShown;
            // ---- Anzeigefeld der Kanaele: nur messen, was zu sehen ist
            var loud = new List<string>();
            var spec = new List<string>();
            bool coverField = false;
            bool fields = shown && !StripBase.Compact;
            if (fields)
                foreach (var s in _channelStrips)
                {
                    if (!s.Visible) continue;
                    int m = s.FieldMode;
                    if (m == ChannelField.Loud) loud.Add(s.Config.Id);
                    else if (m == ChannelField.Spec) spec.Add(s.Config.Id);
                    else if (m == ChannelField.Cover) coverField = true;
                }
            if (MaskingWanted400) foreach (var c in _settings.Channels) if (!spec.Contains(c.Id)) spec.Add(c.Id);   // 4.0 finale: Maskierung
            if (!loud.SequenceEqual(_anLoudReq) || !spec.SequenceEqual(_anSpecReq))
            {
                _anLoudReq = loud.ToArray();
                _anSpecReq = spec.ToArray();
                Backend.SetChannelAnalysis(_anLoudReq.Length > 0 ? _anLoudReq : null, _anSpecReq.Length > 0 ? _anSpecReq : null);
            }
            var np = _nowPlaying.Current;
            if (fields)
                foreach (var s in _channelStrips)
                {
                    if (!s.Visible) continue;
                    switch (s.FieldMode)
                    {
                        case ChannelField.Loud: s.SetFieldLoudness(ChannelLoudnessHeard(s.Config), OutputStrip.LufsMode); break;
                        case ChannelField.Spec: s.SetFieldSpectrum(ChannelSpectrumHeard(s.Config), dt); break;
                        case ChannelField.Cover: s.SetFieldCover(np?.Cover, np?.Title, np != null && np.Playing); break;
                    }
                }

            // ---- Meterbridge: Oszilloskop und Cover
            bool win = BridgeWinShown;
            if (shown && _bridge.OscShown) _bridge.FeedOsc(_snap.Scope, dt);
            if (win && _bridgeWin.Bridge.OscShown) _bridgeWin.Bridge.FeedOsc(_snap.Scope, dt);
            _bridge.SetNow(np);
            if (win) _bridgeWin.Bridge.SetNow(np);
            NowPlayingWatcher.WantCover = coverField || (shown && _bridge.CoverShown) || (win && _bridgeWin.Bridge.CoverShown) || CoverColorWanted || _settings.SongToastOn || CoverWanted400;   // 4.0: Cover-Hintergrund, Mini-Player   // 3.4.6: Cover in der Song-Einblendung   // 3.2: Farbe aus dem Cover

            // ---- Song-Verlauf, Hoer-Tagebuch
            TrackSong(np, dt);
            _diaryAcc += dt;
            if (_diaryAcc >= 1f) { TickDiary(_diaryAcc); _diaryAcc = 0f; }
            if (_diaryWin != null && !_diaryWin.IsDisposed && _diaryWin.Visible) _diaryWin.Tick();
        }

        /// <summary>Wird "Jetzt laeuft" gebraucht (Laufschrift, Cover, Song-Verlauf)?</summary>
        private bool NowPlayingNeeded => NowPlayingWatcher.WantCover || !_settings.SongsOff || _settings.SongToastOn || NowPlayingWanted400;

        // ================================================================ Anzeigefeld: Werte nach dem Fader

        /// <summary>Lautstaerke des Kanals in dB (Fader, Gruppe, DIM, Ducking …) – stumm/Solo-stumm = mute.</summary>
        private float ChannelGainDb(ChannelConfig c, out bool mute)
        {
            if (_policyCache.TryGetValue(c.Id, out var p))
            {
                mute = p.Mute;
                return p.Volume > 1e-6f ? (float)(20 * Math.Log10(p.Volume)) : float.NegativeInfinity;
            }
            mute = c.Mute;
            return FaderLaw.PosToDb(c.Fader);
        }

        /// <summary>Lautheit [M, S, I] des Kanals, wie man sie hoert (null = nichts gemessen oder stumm).</summary>
        private float[] ChannelLoudnessHeard(ChannelConfig c)
        {
            float[] v = null;
            if (_snap.ChannelLoudness != null && _snap.ChannelLoudness.TryGetValue(c.Id, out var cl)) v = cl;
            else if (_snap.AppLoudness != null && _snap.AppLoudness.Count > 0)
                foreach (var a in _snap.Apps)
                    if (!a.Captured && ChannelOf(a.Key) == c && _snap.AppLoudness.TryGetValue(a.Key, out var al)) v = AudioEngine.AddLoudness(v, al);
            if (v == null) return null;
            float g = ChannelGainDb(c, out bool mute);
            if (mute || float.IsNegativeInfinity(g)) return null;
            var r = new float[v.Length];
            for (int i = 0; i < r.Length; i++) r[i] = v[i] + g;
            return r;
        }

        /// <summary>Spektrum des Kanals nach dem Fader (null = still).</summary>
        private float[] ChannelSpectrumHeard(ChannelConfig c)
        {
            float[] v = null;
            if (_snap.ChannelSpectra != null && _snap.ChannelSpectra.TryGetValue(c.Id, out var cs)) v = cs;
            else if (_snap.AppSpectra != null && _snap.AppSpectra.Count > 0)
                foreach (var a in _snap.Apps)
                    if (!a.Captured && ChannelOf(a.Key) == c && _snap.AppSpectra.TryGetValue(a.Key, out var asp)) v = AudioEngine.MaxSpectrum(v, asp);
            if (v == null) return null;
            float g = ChannelGainDb(c, out bool mute);
            if (mute || float.IsNegativeInfinity(g)) return null;
            var r = new float[v.Length];
            for (int i = 0; i < r.Length; i++) r[i] = v[i] + g;
            return r;
        }

        // ================================================================ Anzeigefeld: Einstellen

        private void SetChannelField(ChannelConfig c, ChannelStrip strip, int? mode)
        {
            c.Field = mode.HasValue ? ChannelField.Clamp(mode.Value) : (int?)null;
            strip?.FieldChanged();
            ScheduleSave();
        }

        /// <summary>Anzeigefeld fuer alle Kanaele (eigene Wahl der Kanaele bleibt, ausser alle=true).</summary>
        private void SetGlobalField(int mode, bool all = false)
        {
            _settings.ChannelField = ChannelField.Clamp(mode);
            ChannelStrip.GlobalField = _settings.ChannelField;
            if (all) foreach (var c in _settings.Channels) c.Field = null;
            foreach (var s in _channelStrips) s.FieldChanged();
            ScheduleSave();
        }

        /// <summary>Eintrag "Anzeigefeld" fuers Kanal-Menue.</summary>
        private void AddChannelFieldItems(ToolStripItemCollection items, ChannelConfig c, ChannelStrip strip)
        {
            var sub = Sub("Anzeigefeld: " + ChannelField.Short[strip?.FieldMode ?? ChannelStrip.GlobalField]);
            sub.DropDownItems.Add(new ToolStripMenuItem("Wie alle Kanäle  (" + ChannelField.Short[ChannelStrip.GlobalField] + ")", null, (o, e) => SetChannelField(c, strip, null))
            { Checked = !c.Field.HasValue });
            sub.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < ChannelField.Names.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(ChannelField.Names[i], null, (o, e) => SetChannelField(c, strip, k))
                { Checked = c.Field == i, ToolTipText = ChannelField.Hints[i] });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(new ToolStripMenuItem("So für alle Kanäle", null, (o, e) => SetGlobalField(strip?.FieldMode ?? ChannelStrip.GlobalField, true)));
            sub.DropDownItems.Add(Hint("Steht dort, wo sonst die App-Symbole sind."));
            items.Add(sub);
        }

        // ================================================================ Meterbridge: Oszilloskop, Cover

        private void SetOscOn(bool on)
        {
            _settings.OscOff = !on;
            _bridge.OscOn = on;
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Bridge.OscOn = on;
            BridgeLayoutChanged();
        }

        private void SetCoverOn(bool on)
        {
            _settings.CoverOff = !on;
            _bridge.CoverOn = on;
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Bridge.CoverOn = on;
            BridgeLayoutChanged();
        }

        private void ShowOscMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Oszilloskop zeigt"));
            m.Items.Add(Hint("Gleicher Ausgang wie das Stereo-Bild."));
            m.Items.Add(new ToolStripSeparator());
            AddGonioSourceItems(m.Items);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Oszilloskop ausblenden", null, (s, e) => SetOscOn(false)));
            m.Show(screen);
        }

        private void SetSpecPeak(int mode)
        {
            _settings.SpecPeak = Math.Max(0, Math.Min(2, mode));
            SpectrumModel.PeakMode = _settings.SpecPeak.Value;
            ScheduleSave();
        }

        private void SetSpecHover(bool on)
        {
            _settings.SpecHoverOff = !on;
            MeterBridge.SpecHover = on;
            ScheduleSave();
        }

        // ================================================================ Song-Verlauf

        private string _songCand;
        private float _songCandSec;

        /// <summary>Ein Titel kommt in den Verlauf, wenn er 10 Sekunden lief (uebersprungene nicht).</summary>
        private void TrackSong(NowPlayingInfo np, float dt)
        {
            if (_settings.SongsOff || np == null || !np.Playing || string.IsNullOrEmpty(np.Title)) { _songCandSec = 0f; return; }
            string key = (np.Artist ?? "") + "\u0001" + np.Title;
            if (key != _songCand) { _songCand = key; _songCandSec = 0f; return; }
            if (_songCandSec < 0f) return;   // schon eingetragen
            _songCandSec += dt;
            if (_songCandSec < 10f) return;
            _songCandSec = -1f;
            if (_settings.Songs == null) _settings.Songs = new List<SongEntry>();
            var last = _settings.Songs.FirstOrDefault();
            if (last != null && last.Title == np.Title && last.Artist == np.Artist) return;
            _settings.Songs.Insert(0, new SongEntry { Title = np.Title, Artist = np.Artist, App = np.App, Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            if (_settings.Songs.Count > 100) _settings.Songs.RemoveRange(100, _settings.Songs.Count - 100);
            ScheduleSave();
        }

        private static string SongText(SongEntry s) => string.IsNullOrEmpty(s.Artist) ? s.Title : s.Artist + " – " + s.Title;

        private void SetSongsOn(bool on)
        {
            _settings.SongsOff = !on;
            ScheduleSave();
        }

        private void ShowSongMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Zuletzt gespielt"));
            m.Items.Add(new ToolStripSeparator());
            AddSongItems(m.Items);
            m.Show(screen);
        }

        private void AddSongItems(ToolStripItemCollection items)
        {
            var list = _settings.Songs ?? new List<SongEntry>();
            if (_settings.SongsOff) items.Add(Hint("Der Song-Verlauf ist aus."));
            else if (list.Count == 0) items.Add(Hint("Noch nichts – Titel kommen dazu, wenn sie 10 Sekunden laufen."));
            string today = DateTime.Now.ToString("yyyy-MM-dd"), lastDay = today;
            foreach (var s in list.Take(25))
            {
                string day = s.Time != null && s.Time.Length >= 10 ? s.Time.Substring(0, 10) : today;
                if (day != lastDay)
                {
                    DateTime d;
                    items.Add(Header(DateTime.TryParse(day, out d) ? (d.Date == DateTime.Now.Date.AddDays(-1) ? "Gestern" : d.ToString("dddd, d. MMMM", new System.Globalization.CultureInfo("de-DE"))) : day));
                    lastDay = day;
                }
                string time = s.Time != null && s.Time.Length >= 16 ? s.Time.Substring(11, 5) : "";
                var entry = s;
                items.Add(new ToolStripMenuItem(time + "   " + SongText(s) + (string.IsNullOrEmpty(s.App) ? "" : "   · " + s.App), null, (o, e) =>
                {
                    try { Clipboard.SetText(SongText(entry)); ShowOsd("Kopiert", SongText(entry), -1, Theme.Accent); } catch { }
                }) { ToolTipText = "Klicken: Titel kopieren" });
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Song-Verlauf aufzeichnen", null, (o, e) => SetSongsOn(_settings.SongsOff)) { Checked = !_settings.SongsOff });
            if (list.Count > 0) items.Add(new ToolStripMenuItem("Verlauf löschen", null, (o, e) => { _settings.Songs = null; ScheduleSave(); }));
        }

        // ================================================================ Hoer-Tagebuch

        private int DiaryLoudIdx => Math.Max(0, Math.Min(2, _settings.DiaryLoud ?? 1));
        private int DiaryWarnIdx => Math.Max(0, Math.Min(3, _settings.DiaryWarn ?? 2));
        private float DiaryLoudFrom => ListeningDiary.LoudLevels[DiaryLoudIdx];

        private void TickDiary(float dt)
        {
            if (_settings.DiaryOff || _diary == null) return;
            float level = float.NaN;
            var v = _snap.Loudness;
            var def = DefaultDevice;
            if (v != null && v.Length > 1 && !float.IsNaN(v[1]) && !float.IsInfinity(v[1]) && v[1] > -70f && def != null && !def.Mute)
                level = v[1] + def.VolumeDb;
            var apps = _snap.Apps.Where(a => Math.Max(a.PeakL, a.PeakR) > 0.01f && a.Key != AppKeys.System).Select(a => a.Name);
            if (_diary.Tick(dt, level, apps, DiaryLoudFrom, ListeningDiary.WarnHours[DiaryWarnIdx], DateTime.Now))
                ShowOsd("Hör-Tagebuch", "Heute schon " + ListeningDiary.Duration(_diary.Today.LoudSec) + " laut gehört – vielleicht etwas leiser?", -1, Theme.MeterYellow);
            if (!_demo && _diary.SaveDue()) _diary.Save();
        }

        private void SaveDiary()
        {
            if (!_demo && _diary != null && _diary.Dirty) _diary.Save();
        }

        private void SetDiaryOn(bool on)
        {
            _settings.DiaryOff = !on;
            ScheduleSave();
        }

        public void ShowDiaryWindow()
        {
            if (_diaryWin != null && !_diaryWin.IsDisposed) { _diaryWin.Activate(); return; }
            _diaryWin = new DiaryWindow(this, () => _diary, () => DiaryLoudFrom);
            _diaryWin.FormClosed += (s, e) => _diaryWin = null;
            _diaryWin.Show(this);
        }

        private void ClearDiary()
        {
            if (_diary == null) return;
            _diary.Clear();
            if (!_demo) _diary.Save();
            _diaryWin?.Invalidate(true);
            ShowOsd("Hör-Tagebuch", "gelöscht", -1, Theme.Accent);
        }

        // ================================================================ Vorschau

        /// <summary>Nur Vorschau: ein erfundenes Cover (Sonnenuntergang mit Gitter, ohne Schrift).</summary>
        internal static Bitmap DemoCover(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, size, size);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(0x1a, 0x0b, 0x3d), Color.FromArgb(0xf2, 0x5c, 0x8a), LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0x12, 0x08, 0x2e), Color.FromArgb(0x5a, 0x1a, 0x7a), Color.FromArgb(0xf2, 0x5c, 0x8a), Color.FromArgb(0x2a, 0x0a, 0x3a) },
                        Positions = new[] { 0f, 0.45f, 0.62f, 1f },
                    };
                    g.FillRectangle(b, r);
                }
                // Sonne mit Streifen
                float sr = size * 0.26f, cx = size / 2f, cy = size * 0.55f;
                using (var b = new LinearGradientBrush(new RectangleF(cx - sr, cy - sr, sr * 2, sr * 2), Color.FromArgb(0xff, 0xe0, 0x6a), Color.FromArgb(0xff, 0x4d, 0x8d), LinearGradientMode.Vertical))
                    g.FillPie(b, cx - sr, cy - sr, sr * 2, sr * 2, 180, 180);
                using (var b = new SolidBrush(Color.FromArgb(0x5a, 0x1a, 0x7a)))
                    for (int i = 0; i < 5; i++) { float y = cy - sr * 0.12f - i * sr * 0.18f; g.FillRectangle(b, cx - sr, y, sr * 2, sr * 0.05f + i * 0.4f); }
                // Gitter
                using (var pen = new Pen(Color.FromArgb(200, 0x3d, 0xe8, 0xff), Math.Max(1f, size / 200f)))
                {
                    for (int i = 0; i < 7; i++) { float y = cy + (size - cy) * (float)Math.Pow(i / 6.0, 1.7); g.DrawLine(pen, 0, y, size, y); }
                    for (int i = -8; i <= 8; i++) g.DrawLine(pen, cx + i * size * 0.02f, cy, cx + i * size * 0.18f, size);
                }
            }
            return bmp;
        }

        private static List<SongEntry> DemoSongs()
        {
            var now = DateTime.Now;
            var data = new[]
            {
                new[] { "Neon Harbor", "Midnight Drive (Extended Mix)", "Spotify", "2" },
                new[] { "Glass Pilots", "Afterglow City", "Spotify", "6" },
                new[] { "Mira Solenne", "Paper Satellites", "Spotify", "11" },
                new[] { "", "Lo-Fi Beats zum Zocken – 24/7", "YouTube", "40" },
                new[] { "Vektor 9", "Static Bloom", "Spotify", "65" },
                new[] { "The Quiet Engines", "Copper Skies", "Spotify", "1500" },
            };
            return data.Select(d => new SongEntry
            {
                Artist = d[0].Length == 0 ? null : d[0], Title = d[1], App = d[2],
                Time = now.AddMinutes(-int.Parse(d[3])).ToString("yyyy-MM-dd HH:mm"),
            }).ToList();
        }
    }
}
