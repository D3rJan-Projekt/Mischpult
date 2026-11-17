using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 (finale) Fenster &amp; Bedienung: Szenen-Galerie, EQ-Uebersicht mit Maskierung, Befehlssuche (Strg+K),
    /// Fehlerbericht, versteckte Ueberraschung, Ueber-Fenster mit Versions-Chronik.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyFinalWin400(MixerSettings s)
        {
            if (!s.SceneGalleryOn) try { _gallery?.Close(); } catch { }
            if (!s.EqOverviewOn) try { _eqOv?.Close(); } catch { }
        }

        private void HookFinalWin400() { }

        private double _nextWin400F;

        private void TickFinalWin400(double now, float dt)
        {
            TickEgg400(now, dt);
            if (now < _nextWin400F) return;
            _nextWin400F = now + 0.1;
            if (_eqOv != null && !_eqOv.IsDisposed && _eqOv.Visible) { TickMaskAvg400(); _eqOv.Invalidate(); }
        }

        private void CleanupFinalWin400()
        {
            foreach (var f in new Form[] { _gallery, _eqOv, _palette, _about }) try { f?.Close(); } catch { }
        }

        // ================================================================ Tastatur: Strg+K, Konami-Code

        private static readonly Keys[] Konami = { Keys.Up, Keys.Up, Keys.Down, Keys.Down, Keys.Left, Keys.Right, Keys.Left, Keys.Right, Keys.B, Keys.A };
        private int _konamiPos;

        /// <summary>Aus HandleKeys400 (true = erledigt).</summary>
        private bool HandleKeysFinal400(Keys keyData)
        {
            if (_settings.CmdSearchOn && keyData == (Keys.Control | Keys.K)) { ShowPalette400(); return true; }
            if (!_settings.EggOff && !(ActiveControl is TextBoxBase))
            {
                Keys k = keyData & Keys.KeyCode;
                if ((keyData & Keys.Modifiers) == 0 && k == Konami[_konamiPos])
                {
                    if (++_konamiPos == Konami.Length) { _konamiPos = 0; StartEgg400(); return true; }
                }
                else _konamiPos = (keyData & Keys.Modifiers) != 0 || k != Keys.Up ? 0 : _konamiPos == 2 ? 2 : 1;   // dreimal hoch: weiter bei "hoch hoch"
            }
            return false;
        }

        // ================================================================ 247 versteckte Ueberraschung

        private double _eggUntil = -1;
        private readonly List<float[]> _confetti = new List<float[]>();   // x, y, vx, vy, farbe, drehung
        private readonly Random _eggRnd = new Random();

        public bool EggRunning => _eggUntil > 0;

        private void StartEgg400()
        {
            _eggUntil = Now + 10;
            _confetti.Clear();
            ShowOsd("↑↑↓↓←→←→ B A", "Party-Modus! (10 Sekunden)", -1, Theme.Accent);
            Announce400("Party-Modus");
            foreach (var s in _channelStrips) s.StartMotor(Math.Max(0f, s.MotorFrom - 0.25f));   // Fader wippen einmal
        }

        private void TickEgg400(double now, float dt)
        {
            if (_eggUntil < 0 || _channelHost == null) return;
            if (now > _eggUntil || _settings.EggOff) { _eggUntil = -1; _confetti.Clear(); _channelHost.Invalidate(true); return; }
            int w = _channelHost.ClientSize.Width;
            if (!StripBase.Calm && now < _eggUntil - 2)
                for (int i = 0; i < 3; i++)
                    _confetti.Add(new[] { (float)(_eggRnd.NextDouble() * w), -10f, (float)(_eggRnd.NextDouble() - 0.5) * 60f, 80f + (float)_eggRnd.NextDouble() * 120f, _eggRnd.Next(6), (float)_eggRnd.NextDouble() * 360f });
            foreach (var c in _confetti) { c[0] += c[2] * dt; c[1] += c[3] * dt; c[5] += 200f * dt; }
            _confetti.RemoveAll(c => c[1] > _channelHost.ClientSize.Height + 10);
            if (_confetti.Count > 400) _confetti.RemoveRange(0, _confetti.Count - 400);
            _channelHost.Invalidate(false);
        }

        /// <summary>Aus PaintWall400: Konfetti in den freien Flaechen und Disco-Licht.</summary>
        private void PaintEgg400(StripHost h, Graphics g)
        {
            if (_eggUntil < 0 || h != _channelHost) return;
            double t = Now;
            var spots = new Theme.Spot[4];
            for (int i = 0; i < 4; i++)
                spots[i] = new Theme.Spot { Src = new PointF(h.ClientSize.Width * (i + 0.5f) / 4f, 0), Angle = (float)(35 * Math.Sin(t * 3 + i)), Spread = 12f, Color = Theme.SpotColor(i, (int)(t * 4)) };
            Theme.DrawCones(g, spots, h.ClientSize.Height * 1.2f, 0.8f, 0f, h.ClientRectangle, PointF.Empty);
            foreach (var c in _confetti)
            {
                var st = g.Save();
                g.TranslateTransform(c[0], c[1]);
                g.RotateTransform(c[5]);
                using (var b = new SolidBrush(Theme.SpotColor((int)c[4], 0))) g.FillRectangle(b, -Theme.F(3), -Theme.F(1.5f), Theme.F(6), Theme.F(3));
                g.Restore(st);
            }
        }

        // ================================================================ 256 Szenen-Galerie

        private SceneGalleryWindow _gallery;

        private List<SceneGalleryWindow.Card> GalleryCards400()
        {
            var list = new List<SceneGalleryWindow.Card>();
            foreach (var sc in _settings.Scenes)
            {
                var card = new SceneGalleryWindow.Card { Id = sc.Id, Name = sc.Name, Master = sc.MasterVolume, Lit = sc.Id == _sceneLit346, Partial = sc.Only != null };
                foreach (var c in _settings.Channels)
                {
                    var ch = sc.Channels?.Find(x => x.Id == c.Id);
                    if (ch == null) continue;
                    card.Bars.Add(new KeyValuePair<Color, float>(Color.FromArgb(255, Color.FromArgb(c.Color)), ch.Mute ? -1f : ch.Fader));
                }
                list.Add(card);
            }
            return list;
        }

        private void ShowGallery400()
        {
            if (!_settings.SceneGalleryOn) return;
            if (_gallery != null && !_gallery.IsDisposed) { _gallery.SetCards(GalleryCards400()); _gallery.Activate(); return; }
            var w = new SceneGalleryWindow { TopMost = TopMost };
            w.SetCards(GalleryCards400());
            w.Load += id =>
            {
                var sc = _settings.Scenes.FirstOrDefault(x => x.Id == id);
                if (sc == null) return;
                ClearScenePreview();
                ApplyScene(sc, "Szene");
                w.SetCards(GalleryCards400());
            };
            w.Hover2 += id =>
            {
                var sc = id == null ? null : _settings.Scenes.FirstOrDefault(x => x.Id == id);
                if (sc == null) { ClearScenePreview(); return; }
                foreach (var s in _channelStrips)
                {
                    var ch = sc.Channels?.Find(x => x.Id == s.Config.Id);
                    s.SetPreview(ch == null ? float.NaN : ch.Mute ? 0f : Math.Max(0f, Math.Min(1f, ch.Fader)));
                }
            };
            w.FormClosed += (s, e) => { if (_gallery == w) _gallery = null; };
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            _gallery = w;
            w.Show(this);
        }

        private void AddGallery400(ToolStripItemCollection items)
        {
            if (_settings.SceneGalleryOn) items.Add(new ToolStripMenuItem("Szenen-Galerie …", null, (s, e) => BeginInvoke(new Action(ShowGallery400))));
        }

        // ================================================================ 244 EQ-Uebersicht, 245 Maskierung

        private EqOverviewWindow _eqOv;
        private bool EqOverviewWantsSpectrum400 => _eqOv != null && !_eqOv.IsDisposed && _eqOv.Visible;
        private bool MaskingWanted400 => _settings.MaskingOn && EqOverviewWantsSpectrum400;

        /// <summary>Gesamtwirkung des EQ eines Kanals (3-Band, 10-Band, parametrisch) in dB an den Punkten der Uebersicht.</summary>
        internal double[] ChannelCurve400(ChannelConfig c)
        {
            var f = EqOverviewWindow.Freqs;
            var db = new double[f.Length];
            const int rate = 48000;
            var bq = new Biquad();
            if (c.EqOn)
            {
                Action<Action> add = cfg => { cfg(); for (int i = 0; i < f.Length; i++) db[i] += 20 * Math.Log10(Math.Max(1e-9, bq.MagnitudeAt(rate, f[i]))); };
                if (Math.Abs(c.EqLow) > 0.01f) add(() => bq.LowShelf(rate, DspMixer.FreqLow, c.EqLow));
                if (Math.Abs(c.EqMid) > 0.01f) add(() => bq.Peaking(rate, DspMixer.FreqMid, c.EqMid, 0.7));
                if (Math.Abs(c.EqHigh) > 0.01f) add(() => bq.HighShelf(rate, DspMixer.FreqHigh, c.EqHigh));
                if (c.Geq != null && !GraphicEq.IsFlat(c.Geq) && c.Geq.Length == 10)
                {
                    var bands = new Biquad[10];
                    for (int b = 0; b < 10; b++) bands[b] = new Biquad();
                    GraphicEq.Setup(bands, c.Geq, rate);
                    for (int b = 0; b < 10; b++)
                        if (Math.Abs(c.Geq[b]) > 0.01f)
                            for (int i = 0; i < f.Length; i++) db[i] += 20 * Math.Log10(Math.Max(1e-9, bands[b].MagnitudeAt(rate, f[i])));
                }
            }
            var peq = PeqFor(c);
            if (peq != null) for (int i = 0; i < f.Length; i++) db[i] += peq.MagnitudeDb(f[i], 0);
            return db;
        }

        private double[] OutputCurve400()
        {
            var dev = DefaultDevice;
            if (dev == null || !_outFxSent.TryGetValue(dev.Id, out var fx) || fx == null) return null;
            var f = EqOverviewWindow.Freqs;
            var db = new double[f.Length];
            bool any = false;
            if (fx.Graphic != null && !GraphicEq.IsFlat(fx.Graphic))
            {
                var bands = new Biquad[10];
                for (int b = 0; b < 10; b++) bands[b] = new Biquad();
                GraphicEq.Setup(bands, fx.Graphic, 48000);
                for (int b = 0; b < 10; b++) if (Math.Abs(fx.Graphic[b]) > 0.01f) for (int i = 0; i < f.Length; i++) db[i] += 20 * Math.Log10(Math.Max(1e-9, bands[b].MagnitudeAt(48000, f[i])));
                any = true;
            }
            if (fx.Peq != null && !fx.Peq.IsFlat) { for (int i = 0; i < f.Length; i++) db[i] += (fx.Peq.MagnitudeDb(f[i], 0) + fx.Peq.MagnitudeDb(f[i], 1)) / 2; any = true; }
            return any ? db : null;
        }

        private void ShowEqOverview400()
        {
            if (!_settings.EqOverviewOn) return;
            if (_eqOv != null && !_eqOv.IsDisposed) { _eqOv.Activate(); return; }
            var w = new EqOverviewWindow
            {
                Curves = () => _settings.Channels.Select(c => new EqOverviewWindow.Curve { Name = c.Name, Color = Color.FromArgb(255, Color.FromArgb(c.Color)), Db = ChannelCurve400(c), Off = !c.EqOn && PeqFor(c) == null }).ToList(),
                Output = () => { var d = OutputCurve400(); return d == null ? null : new EqOverviewWindow.Curve { Name = "Ausgang (wirkt auf alles)", Color = Theme.Text, Db = d }; },
                Spectrum = () => _spectrum.Active ? _snap.MasterSpectrum : null,
                Masks = () => _settings.MaskingOn ? ComputeMasks400() : null,
            };
            w.ApplyMask += ApplyMaskDip400;
            w.FormClosed += (s, e) => { if (_eqOv == w) _eqOv = null; _maskAvg.Clear(); };
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            _eqOv = w;
            w.Show(this);
        }

        private readonly Dictionary<string, float[]> _maskAvg = new Dictionary<string, float[]>();

        private void TickMaskAvg400()
        {
            if (!_settings.MaskingOn) return;
            foreach (var c in _settings.Channels)
            {
                var sp = ChannelSpectrumHeard(c);
                if (!_maskAvg.TryGetValue(c.Id, out var avg))
                {
                    if (sp == null) continue;
                    avg = (float[])sp.Clone(); _maskAvg[c.Id] = avg; continue;
                }
                for (int i = 0; i < avg.Length; i++)
                {
                    float v = sp != null && i < sp.Length ? sp[i] : -100f;
                    avg[i] += (v - avg[i]) * 0.08f;   // ca. 1,2 s
                }
            }
        }

        /// <summary>Wer verdeckt wen? Beide laut (ueber −50 dB) und hoechstens 6 dB auseinander im Bereich 200 Hz – 8 kHz.</summary>
        internal static List<EqOverviewWindow.Mask> FindMasks(IList<KeyValuePair<ChannelConfig, float[]>> spectra, string voiceId, ICollection<string> voiceIds)
        {
            var res = new List<KeyValuePair<double, EqOverviewWindow.Mask>>();
            int bands = SpectrumAnalyzer.Bands;
            int lo = (int)Math.Ceiling(bands * Math.Log(200 / 20.0) / Math.Log(1000.0)), hi = (int)Math.Floor(bands * Math.Log(8000 / 20.0) / Math.Log(1000.0));
            const int Region = 6;   // Baender je Bereich (ca. 1/5 Oktave … 1/3)
            for (int a = 0; a < spectra.Count; a++)
                for (int b = a + 1; b < spectra.Count; b++)
                {
                    var sa = spectra[a].Value; var sb = spectra[b].Value;
                    if (sa == null || sb == null) continue;
                    double best = 0; int bestR = -1;
                    for (int r = lo; r + Region <= hi; r += Region / 2)
                    {
                        double ea = 0, eb = 0;
                        for (int i = r; i < r + Region && i < sa.Length && i < sb.Length; i++) { ea += sa[i]; eb += sb[i]; }
                        ea /= Region; eb /= Region;
                        if (ea < -50 || eb < -50) continue;
                        double diff = Math.Abs(ea - eb);
                        if (diff > 6) continue;
                        double score = (Math.Min(ea, eb) + 60) * (6 - diff);
                        if (score > best) { best = score; bestR = r; }
                    }
                    if (bestR < 0) continue;
                    // Wer bekommt die Delle? Nicht die Stimme (Ducking-Ausloeser, Auto-Mix), sonst der lautere
                    var ca = spectra[a].Key; var cb = spectra[b].Key;
                    bool va = ca.Id == voiceId || (voiceIds != null && voiceIds.Contains(ca.Id)), vb = cb.Id == voiceId || (voiceIds != null && voiceIds.Contains(cb.Id));
                    double la = sa.Skip(bestR).Take(Region).Average(), lb = sb.Skip(bestR).Take(Region).Average();
                    var masker = va && !vb ? cb : vb && !va ? ca : la >= lb ? ca : cb;
                    var other = masker == ca ? cb : ca;
                    double hz = SpectrumAnalyzer.BandFreq(bestR + Region / 2.0);
                    hz = hz < 1000 ? Math.Round(hz / 10) * 10 : Math.Round(hz / 100) * 100;
                    float dip = best > 200 ? -4f : -3f;
                    string hzText = hz >= 1000 ? (hz / 1000).ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " kHz" : hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
                    res.Add(new KeyValuePair<double, EqOverviewWindow.Mask>(best, new EqOverviewWindow.Mask
                    {
                        ChannelId = masker.Id, Hz = (float)hz, Db = dip, Color = Color.FromArgb(255, Color.FromArgb(masker.Color)),
                        Text = masker.Name + " verdeckt " + other.Name + " um " + hzText + " – Vorschlag: " + masker.Name + " dort " + dip.ToString("0", CultureInfo.InvariantCulture).Replace("-", "−") + " dB",
                    }));
                }
            return res.OrderByDescending(kv => kv.Key).Select(kv => kv.Value).Take(4).ToList();
        }

        private List<EqOverviewWindow.Mask> ComputeMasks400()
        {
            var sp = _settings.Channels.Where(c => _maskAvg.ContainsKey(c.Id)).Select(c => new KeyValuePair<ChannelConfig, float[]>(c, _maskAvg[c.Id])).ToList();
            var voices = new HashSet<string>(_settings.Channels.Where(c => c.AutoMix).Select(c => c.Id));
            return FindMasks(sp, _settings.DuckTrigger, voices);
        }

        private void ApplyMaskDip400(EqOverviewWindow.Mask m)
        {
            var c = ChannelById(m.ChannelId);
            if (c == null) return;
            if (!_settings.PeqOn) { _settings.PeqOn = true; ShowOsd("Parametrischer EQ", "eingeschaltet (für die Delle)", -1, Theme.Accent); }
            if (c.Peq == null) c.Peq = new List<PeqBand>();
            if (c.Peq.Count >= MixerSettings.PeqMax) { ShowOsd(c.Name, "Parametrischer EQ ist voll", -1, Theme.MuteRed); return; }
            c.Peq.Add(new PeqBand { Type = PeqBand.Bell, Hz = m.Hz, Db = m.Db, Q = 1.4f });
            c.PeqBypass = false;
            OnChannelEdited(c);
            ScheduleSave();
            ShowOsd(c.Name, "Delle gesetzt: " + PeqWindow.HzText(m.Hz) + " " + PeqWindow.DbText(m.Db), -1, Theme.Accent);
        }

        // ================================================================ 263 Befehlssuche

        private CommandPalette _palette;

        internal List<CommandPalette.Cmd> PaletteCommands400()
        {
            var list = new List<CommandPalette.Cmd>();
            foreach (var a in HotkeyActions())
            {
                var id = a.Id;
                list.Add(new CommandPalette.Cmd { Text = a.Label, Group = a.Group, Run = () => ExecuteHotkey(id) });
            }
            foreach (var sc in _settings.Scenes)
            {
                var s = sc;
                list.Add(new CommandPalette.Cmd { Text = "Szene laden: " + sc.Name, Group = "Szenen", Run = () => ApplyScene(s, "Szene") });
            }
            foreach (var c in _settings.Channels)
            {
                var ch = c;
                list.Add(new CommandPalette.Cmd { Text = c.Name + ": stumm an/aus", Group = "Kanäle", Run = () => { ch.Mute = !ch.Mute; OnChannelChanged(ch, false); _channelStrips.FirstOrDefault(x => x.Config == ch)?.InvalidateChrome(); } });
            }
            try
            {
                foreach (var page in BuildSettingsPages())
                    foreach (var row in page.Rows)
                    {
                        if (string.IsNullOrEmpty(row.Label) || row is SSection || row is SHeader) continue;
                        var r = row; var pt = page.Title;
                        if (row is SToggle tg)
                            list.Add(new CommandPalette.Cmd { Text = tg.Label + (tg.Get() ? " ausschalten" : " einschalten"), Group = "Einstellung · " + pt, Run = () => { if (tg.Enabled == null || tg.Enabled()) tg.Set(!tg.Get()); } });
                        else
                            list.Add(new CommandPalette.Cmd { Text = r.Label, Group = "Einstellung · " + pt, Run = () => { ShowSettings(pt); _settingsWin?.SearchFor(r.Label); } });
                    }
            }
            catch { }
            list.Add(new CommandPalette.Cmd { Text = "Einstellungen öffnen", Group = "Allgemein", Run = () => ShowSettings() });
            list.Add(new CommandPalette.Cmd { Text = "Über Mischpult", Group = "Allgemein", Run = () => ShowAbout() });
            list.Add(new CommandPalette.Cmd { Text = "Versions-Chronik", Group = "Allgemein", Run = () => { ShowAbout(); _about?.ShowChronicle(); } });
            if (_settings.SceneGalleryOn) list.Add(new CommandPalette.Cmd { Text = "Szenen-Galerie", Group = "Fenster", Run = ShowGallery400 });
            if (_settings.EqOverviewOn) list.Add(new CommandPalette.Cmd { Text = "EQ-Übersicht", Group = "Fenster", Run = ShowEqOverview400 });
            if (_settings.BugReportOn) list.Add(new CommandPalette.Cmd { Text = "Fehlerbericht erstellen", Group = "Hilfe", Run = () => MakeBugReport400(true) });
            if (_settings.CrossOn) list.Add(new CommandPalette.Cmd { Text = "Crossfader", Group = "Fenster", Run = ShowCross400 });
            if (_settings.GenOn) list.Add(new CommandPalette.Cmd { Text = "Signalgenerator", Group = "Fenster", Run = ShowGen400 });
            return list;
        }

        public void ShowPalette400()
        {
            if (!_settings.CmdSearchOn) return;
            try { _palette?.Close(); } catch { }
            if (!Visible) RestoreFromTray();
            var p = new CommandPalette(PaletteCommands400()) { Owner = this, TopMost = TopMost };
            var r = RectangleToScreen(ClientRectangle);
            p.Location = new Point(r.X + (r.Width - p.Width) / 2, r.Y + Theme.Px(70));
            p.FormClosed += (s, e) => { if (_palette == p) _palette = null; };
            _palette = p;
            p.Show(this);
            p.Activate();
        }

        // ================================================================ 246 Fehlerbericht

        /// <summary>Einstellungen als Text ohne Passwoerter und Schluessel.</summary>
        internal static string Scrub400(string json)
        {
            if (string.IsNullOrEmpty(json)) return json ?? "";
            json = Regex.Replace(json, "\"(\\w*(?:Pass|Password|Token|Secret|ApiKey|PrivateKey|AuthKey)\\w*)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"***\"", RegexOptions.IgnoreCase);
            try { string user = Environment.UserName; if (!string.IsNullOrEmpty(user) && user.Length > 1) json = json.Replace(user, "<Benutzer>"); } catch { }
            return json;
        }

        private string SystemInfo400()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mischpult " + Program.Version);
            sb.AppendLine("Zeit: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64 Bit)" : ""));
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine("Anzeige: " + DisplayInfo());
            sb.AppendLine("Bildschirme: " + string.Join("; ", Screen.AllScreens.Select(s => s.Bounds.Width + "x" + s.Bounds.Height + (s.Primary ? " (Haupt)" : ""))));
            sb.AppendLine("EQ-Weg: " + (_snap.EqSupported ? "ja" : "nein – " + _snap.EqStatus));
            sb.AppendLine("Umleitung: " + (_snap.RoutingSupported ? "ja" : "nein"));
            sb.AppendLine("Design: " + Theme.StyleNames[Math.Max(0, Math.Min(Theme.StyleNames.Length - 1, _settings.UiStyle))] + ", Zoom " + Theme.S.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("Kanäle: " + _settings.Channels.Count + ", Szenen: " + _settings.Scenes.Count);
            sb.AppendLine("Geräte:");
            foreach (var d in _snap.Devices) sb.AppendLine("  " + d.FullName + (d.IsDefault ? " (Standard)" : ""));
            sb.AppendLine("Apps gerade:");
            foreach (var a in _snap.Apps.Take(40)) sb.AppendLine("  " + a.Name + (a.Captured ? " [EQ-Weg]" : ""));
            return Scrub400(sb.ToString());
        }

        /// <summary>Zip mit Protokollen, Einstellungen (ohne Passwoerter) und Systeminfos – Pfad oder null.</summary>
        internal string MakeBugReport400(bool show)
        {
            try
            {
                string dir = MixerSettings.DocsMode ? Path.Combine(MixerSettings.DocsRoot, "Fehlerberichte") : Path.Combine(MixerSettings.Folder, "Fehlerberichte");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "Mischpult-Fehlerbericht-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                using (var fs = File.Create(path))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    void AddText(string name, string text)
                    {
                        var e = zip.CreateEntry(name);
                        using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text ?? "");
                    }
                    AddText("systeminfo.txt", SystemInfo400());
                    try { if (File.Exists(MixerSettings.FilePath)) AddText("einstellungen.json", Scrub400(File.ReadAllText(MixerSettings.FilePath))); } catch { }
                    try
                    {
                        if (Directory.Exists(MixerSettings.LogFolder))
                            foreach (var f in Directory.GetFiles(MixerSettings.LogFolder, "*.log").Concat(Directory.GetFiles(MixerSettings.LogFolder, "*.txt")).OrderByDescending(File.GetLastWriteTime).Take(10))
                            {
                                var fi = new FileInfo(f);
                                if (fi.Length > 4 * 1024 * 1024) continue;
                                AddText("protokolle/" + fi.Name, Scrub400(File.ReadAllText(f)));
                            }
                    }
                    catch { }
                    AddText("liesmich.txt", "Fehlerbericht von Mischpult " + Program.Version + ".\r\nEnthält: Systeminfos, Einstellungen (Passwörter und Schlüssel entfernt, Benutzername ersetzt) und die letzten Protokolle.\r\nKeine Aufnahmen, keine Bilder, nichts vom Mikrofon.\r\n");
                }
                if (show)
                {
                    ShowOsd("Fehlerbericht", "erstellt: " + Path.GetFileName(path), -1, Theme.LedGreen);
                    if (Win32.IsWindows) try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
                }
                return path;
            }
            catch (Exception ex)
            {
                if (show) ShowOsd("Fehlerbericht", "ging nicht: " + ex.Message, -1, Theme.MuteRed);
                return null;
            }
        }

        // ================================================================ 248 Ueber-Fenster mit Chronik

        private AboutWindow _about;

        private void ShowAboutWindow400(string text)
        {
            if (_about != null && !_about.IsDisposed) { _about.Activate(); return; }
            var w = new AboutWindow(text) { TopMost = TopMost };
            if (_settings.GitHubLinksOn && !string.IsNullOrEmpty(_settings.UpdateRepoNow)) { w.OpenGitHub = OpenGitHub426; w.ReportBug = ReportBug426; }   // 4.2.6
            w.FormClosed += (s, e) => { if (_about == w) _about = null; };
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            _about = w;
            w.Show(this);
        }

        // ================================================================ Tastenkuerzel, Einstellungen

        private void AddHotkeyActionsFinal400(List<HotkeyAction> list)
        {
            if (_settings.SceneGalleryOn) list.Add(new HotkeyAction("win.gallery", "Szenen-Galerie öffnen", "Fenster"));
            if (_settings.EqOverviewOn) list.Add(new HotkeyAction("win.eqov", "EQ-Übersicht öffnen", "Fenster"));
            if (_settings.CmdSearchOn) list.Add(new HotkeyAction("win.palette", "Befehlssuche öffnen", "Fenster"));
            if (_settings.LightShowOn) list.Add(new HotkeyAction("look.lights", "Lichtshow an/aus", "Aussehen"));
        }

        private bool ExecuteHotkeyFinal400(string action)
        {
            switch (action)
            {
                case "win.gallery": ShowGallery400(); return true;
                case "win.eqov": ShowEqOverview400(); return true;
                case "win.palette": ShowPalette400(); return true;
                case "look.lights": SetFinal400(() => _settings.LightShowOn = !_settings.LightShowOn); return true;
            }
            return false;
        }

        private void AddWindowsFinal400(SPage p)
        {
            p.Section("4.0 finale – Fenster & Bedienung");
            p.Toggle("Szenen-Galerie", "Alle Szenen als Karten mit einem kleinen Bild des Mixes: Klick lädt, Maus drüber zeigt die Vorschau im Pult. Öffnen: SZENEN → Szenen-Galerie.",
                () => _settings.SceneGalleryOn, v => { _settings.SceneGalleryOn = v; ApplyFinalWin400(_settings); ApplyHotkeys(); ScheduleSave(); });
            p.Button("Szenen-Galerie öffnen", "", "Öffnen", r => ShowGallery400()).Enabled = () => _settings.SceneGalleryOn;
            p.Toggle("EQ-Übersicht", "Alle EQ-Kurven der Kanäle in einem Bild, dazu der EQ des Ausgangs und das Spektrum dahinter.",
                () => _settings.EqOverviewOn, v => { _settings.EqOverviewOn = v; ApplyFinalWin400(_settings); ApplyHotkeys(); ScheduleSave(); });
            p.Button("EQ-Übersicht öffnen", "", "Öffnen", r => ShowEqOverview400()).Enabled = () => _settings.EqOverviewOn;
            p.Toggle("Maskierungs-Anzeige", "In der EQ-Übersicht: wo sich zwei Kanäle gegenseitig verdecken (z. B. Musik und Chat um 2 kHz) – mit Vorschlag und Knopf, der dort eine kleine Delle in den parametrischen EQ setzt.",
                () => _settings.MaskingOn, v => { _settings.MaskingOn = v; ScheduleSave(); }).Enabled = () => _settings.EqOverviewOn;
            p.Toggle("Befehlssuche (Strg+K)", "Strg+K öffnet ein Suchfeld: tippen, was du willst – Szene laden, Kanal stumm, eine Einstellung ein- oder ausschalten, Fenster öffnen – und Enter.",
                () => _settings.CmdSearchOn, v => { _settings.CmdSearchOn = v; ApplyHotkeys(); ScheduleSave(); });
            p.Toggle("Fehlerbericht-Knopf", "Ein Knopf, der eine Zip-Datei mit Protokollen, Einstellungen (ohne Passwörter, Benutzername ersetzt) und Systeminfos in den Dokumente-Ordner legt – zum Weitergeben, wenn etwas nicht geht.",
                () => _settings.BugReportOn, v => { _settings.BugReportOn = v; ScheduleSave(); });
            p.Button("Fehlerbericht erstellen", "", "Erstellen", r => MakeBugReport400(true)).Enabled = () => _settings.BugReportOn;
            p.Toggle("Versteckte Überraschung", "Es gibt eine kleine versteckte Überraschung (Tipp: ein alter Cheat-Code auf den Pfeiltasten). Aus = passiert nichts.",
                () => !_settings.EggOff, v => { _settings.EggOff = !v; ScheduleSave(); });
        }
    }
}
