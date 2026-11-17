using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.16: Stereo-Bild von waehlbarem Ausgang, Wasserfall-Spektrum, Lautheits-Verlauf (2.16.1: von waehlbarem Ausgang), Meterbridge im eigenen Fenster,
    /// Meterbridge mit Zeigerinstrumenten, Kompakt-Ansicht, Bild pro Kanal, Pegel an den Ton anpassen.
    /// Alles einzeln in den Einstellungen an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Start

        private readonly LoudnessHistory _loudHist = new LoudnessHistory();
        private int LoudMinutes => Array.IndexOf(MixerSettings.LoudHistMinuteValues, _settings.LoudHistMinutes) >= 0 ? _settings.LoudHistMinutes : 5;
        private bool LoudHistOn => !_settings.LoudHistOff;

        /// <summary>Einstellungen von 2.16 auf die Anzeige uebertragen (vor dem Anlegen der Zuege).</summary>
        private void ApplyFeatures9Statics()
        {
            SpectrumView.Waterfall = _settings.SpecStyle == 1;
            SpectrumView.Shape = _settings.SpecStyle;      // 3.1
            SpectrumView.Palette = _settings.SpecPalette;  // 3.1
            MeterBridge.Style = _settings.BridgeStyle;
            StripBase.BridgeVuOn = _settings.BridgeStyle == 1;
            StripBase.Compact = _settings.Compact;
            ChannelStrip.ImagesOn = !_settings.ChannelImagesOff;
            _loudHist.SetMinutes(LoudMinutes);
        }

        /// <summary>Nach dem Anlegen der Meterbridge.</summary>
        private void InitFeatures9()
        {
            _bridge.LoudOn = LoudHistOn;
            _bridge.Loud = _loudHist;
            _bridge.GonioClicked += ShowGonioMenu;
            _bridge.LoudClicked += ShowLoudHistMenu;
            _bridge.ClockClicked += ShowClockMenu;
            _bridge.OrderChanged += OnBridgeOrderChanged;
            if (_demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_LOUDFILL") == "1")
            {
                // nur Vorschau: ein paar Minuten Verlauf vorgeben
                var rnd = new Random(7);
                for (int i = 0; i < LoudnessHistory.Points - 6; i++)
                {
                    double t = i / 300.0;
                    float v = (float)(-17 + 3.5 * Math.Sin(t * 9) + 2.5 * Math.Sin(t * 23 + 1) + rnd.NextDouble() * 2 - 1);
                    if (i > 118 && i < 136) v = float.NaN;   // Pause
                    if (i > 200 && i < 232) v += 7f;           // lauter Abschnitt
                    _loudHist.Add(v);
                }
            }
        }

        // ================================================================ Takt

        private void TickFeatures9(float dt)
        {
            if (LoudHistOn) _loudHist.Tick(dt, LoudnessOfDevice(_settings.LoudHistSource));   // Version zaehlt hoch → Meterbridge zeichnet die Kurve nach
            ApplyGonioCaption();
            ApplyLoudCaption();
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) SyncBridgeWin();
        }

        // ================================================================ Stereo-Bild: welcher Ausgang

        private string[] _gonioIds;
        private string _gonioIdsSrc, _gonioIdsStruct;
        private int _gonioIdsN = -1;

        /// <summary>Geraete fuers Stereo-Bild (Eintrag null = Standardgeraet).</summary>
        private string[] GonioDeviceIds()
        {
            string src = _settings.GonioSource;
            if (src == null) return SpectrumDeviceIds();
            if (_gonioIds != null && _gonioIdsSrc == src && _gonioIdsN == _snap.Devices.Length && string.Equals(_gonioIdsStruct, _snap.Structure))
                return _gonioIds;
            _gonioIdsSrc = src; _gonioIdsStruct = _snap.Structure; _gonioIdsN = _snap.Devices.Length;
            if (src == MixerSettings.GonioStd) _gonioIds = new string[] { null };
            else if (src == MixerSettings.SpecAll)
            {
                var ids = _snap.Devices.Select(d => d.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
                _gonioIds = ids.Length > 0 ? ids : new string[] { null };
            }
            else
            {
                var d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, src, StringComparison.OrdinalIgnoreCase));
                _gonioIds = d != null ? new[] { d.Id } : new string[0];
            }
            return _gonioIds;
        }

        private string GonioSourceText()
        {
            string src = _settings.GonioSource;
            if (src == null) return "Wie das Spektrum (" + SpecSourceText() + ")";
            if (src == MixerSettings.SpecAll) return "Alle Ausgaben";
            if (src == MixerSettings.GonioStd) { var d = DefaultDevice; return d == null ? "Standard-Ausgabe" : "Standard-Ausgabe (" + d.Name + ")"; }
            var dev = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, src, StringComparison.OrdinalIgnoreCase));
            if (dev != null) return dev.Name;
            return (string.IsNullOrEmpty(_settings.GonioSourceName) ? "Gerät" : _settings.GonioSourceName) + " (nicht angeschlossen)";
        }

        private string GonioCaptionText()
        {
            string src = _settings.GonioSource;
            if (src == null) return SpecAll ? "STEREO · ALLE" : _settings.SpecSource != null ? "STEREO · " + SpecSourceText().ToUpperInvariant() : "STEREO";
            if (src == MixerSettings.SpecAll) return "STEREO · ALLE";
            if (src == MixerSettings.GonioStd) return "STEREO";
            var dev = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, src, StringComparison.OrdinalIgnoreCase));
            return "STEREO · " + (dev?.Name ?? _settings.GonioSourceName ?? "?").ToUpperInvariant();
        }

        private void ApplyGonioCaption()
        {
            string cap = GonioCaptionText();
            _bridge.GonioCaption = cap;
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Bridge.GonioCaption = cap;
        }

        private void SetGonioSource(string id, string name)
        {
            _settings.GonioSource = string.IsNullOrEmpty(id) ? null : id;
            _settings.GonioSourceName = id == null || id == MixerSettings.SpecAll || id == MixerSettings.GonioStd ? null : name;
            _gonioIds = null;
            ScheduleSave();
            ShowOsd("Stereo-Bild", GonioSourceText(), -1, Theme.Accent);
        }

        private void AddGonioSourceItems(ToolStripItemCollection items)
        {
            string src = _settings.GonioSource;
            items.Add(new ToolStripMenuItem("Wie das Spektrum  (" + SpecSourceText() + ")", null, (s, e) => SetGonioSource(null, null)) { Checked = src == null });
            var def = DefaultDevice;
            items.Add(new ToolStripMenuItem("Standard-Ausgabe" + (def != null ? "  (" + def.Name + ")" : ""), null, (s, e) => SetGonioSource(MixerSettings.GonioStd, null))
            { Checked = src == MixerSettings.GonioStd });
            items.Add(new ToolStripMenuItem("Alle Ausgaben zusammen", null, (s, e) => SetGonioSource(MixerSettings.SpecAll, null))
            { Checked = src == MixerSettings.SpecAll, ToolTipText = "Der Ton aller Geräte zusammengemischt" });
            if (_snap.Devices.Length > 0) items.Add(new ToolStripSeparator());
            foreach (var d in _snap.Devices)
            {
                string id = d.Id, name = d.Name;
                items.Add(new ToolStripMenuItem(d.FullName ?? d.Name, null, (s, e) => SetGonioSource(id, name))
                { Checked = string.Equals(src, id, StringComparison.OrdinalIgnoreCase) });
            }
        }

        private void ShowGonioMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Stereo-Bild zeigt"));
            m.Items.Add(new ToolStripSeparator());
            AddGonioSourceItems(m.Items);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Stereo-Bild ausblenden", null, (s, e) => SetGonio(false)));
            m.Show(screen);
        }

        private ToolStripMenuItem GonioSourceSub()
        {
            var sub = Sub("Stereo-Bild zeigt: " + GonioSourceText());
            AddGonioSourceItems(sub.DropDownItems);
            return sub;
        }

        // ================================================================ Spektrum: Balken oder Wasserfall

        public static readonly string[] SpecStyleNames = SpectrumView.ShapeNames;

        private void SetSpecStyle(int i)
        {
            _settings.SpecStyle = Math.Max(0, Math.Min(SpectrumView.ShapeNames.Length - 1, i));
            SpectrumView.Waterfall = _settings.SpecStyle == 1;
            SpectrumView.Shape = _settings.SpecStyle;
            RestyleBridges();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        /// <summary>3.1: Spektrum-Farben.</summary>
        private void SetSpecPalette(int i)
        {
            _settings.SpecPalette = Math.Max(0, Math.Min(SpectrumView.PaletteNames.Length - 1, i));
            SpectrumView.Palette = _settings.SpecPalette;
            RestyleBridges();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private void RestyleBridges()
        {
            _bridge.Restyle();
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Bridge.Restyle();
        }

        // ---- Pegel an den Ton anpassen: auch das Spektrum spaeter zeigen
        private readonly double[] _sdT = new double[96];
        private readonly float[][] _sdV = new float[96][];
        private int _sdHead, _sdCount;

        private float[] DelayedSpectrum(float[] spec)
        {
            int ms = StripBase.MeterDelayMs;
            if (ms <= 0) { _sdCount = 0; return spec; }
            double now = _clock.Elapsed.TotalMilliseconds;
            int cap = _sdT.Length;
            int w = (_sdHead + _sdCount) % cap;
            if (_sdCount == cap) { _sdHead = (_sdHead + 1) % cap; _sdCount--; }
            _sdT[w] = now; _sdV[w] = spec; _sdCount++;
            int use = -1;
            for (int k = 0; k < _sdCount; k++) { int i = (_sdHead + k) % cap; if (_sdT[i] <= now - ms) use = k; else break; }
            if (use < 0) return null;
            int u = (_sdHead + use) % cap;
            var res = _sdV[u];
            _sdHead = u; _sdCount -= use;
            return res;
        }

        // ---- ... und das grosse VU-Meter
        private readonly double[] _vdT = new double[96];
        private readonly float[][] _vdV = new float[96][];
        private int _vdHead, _vdCount;

        private float[] DelayedVuPeak(float[] pk)
        {
            int ms = StripBase.MeterDelayMs;
            if (ms <= 0 || pk == null) { _vdCount = 0; return pk ?? new float[2]; }
            double now = _clock.Elapsed.TotalMilliseconds;
            int cap = _vdT.Length;
            int w = (_vdHead + _vdCount) % cap;
            if (_vdCount == cap) { _vdHead = (_vdHead + 1) % cap; _vdCount--; }
            _vdT[w] = now; _vdV[w] = pk; _vdCount++;
            int use = -1;
            for (int k = 0; k < _vdCount; k++) { int i = (_vdHead + k) % cap; if (_vdT[i] <= now - ms) use = k; else break; }
            if (use < 0) return new float[2];
            int u = (_vdHead + use) % cap;
            var res = _vdV[u];
            _vdHead = u; _vdCount -= use;
            return res;
        }

        // ================================================================ Lautheits-Verlauf

        private void SetLoudHist(bool on)
        {
            _settings.LoudHistOff = !on;
            _bridge.LoudOn = on;
            if (!on) _loudHist.Clear();
            RestyleBridges();
            ScheduleSave();
        }

        private void SetLoudMinutes(int minutes)
        {
            _settings.LoudHistMinutes = minutes;
            _loudHist.SetMinutes(LoudMinutes);
            RestyleBridges();
            ScheduleSave();
        }

        // ---- 2.16.1: welcher Ausgang

        private string LoudSourceText()
        {
            string src = _settings.LoudHistSource;
            if (src == null) { var d = DefaultDevice; return d == null ? "Standard-Ausgabe" : "Standard-Ausgabe (" + d.Name + ")"; }
            var dev = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, src, StringComparison.OrdinalIgnoreCase));
            if (dev != null) return dev.Name;
            return (string.IsNullOrEmpty(_settings.LoudHistSourceName) ? "Gerät" : _settings.LoudHistSourceName) + " (nicht angeschlossen)";
        }

        /// <summary>Ueberschrift im Verlauf: "LAUTHEIT · 5 MIN" bzw. "LAUTHEIT · HEADSET · 5 MIN".</summary>
        private string LoudCaptionText()
        {
            string src = _settings.LoudHistSource, min = LoudMinutes + " MIN";
            if (src == null) return "LAUTHEIT · " + min;
            var dev = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, src, StringComparison.OrdinalIgnoreCase));
            return "LAUTHEIT · " + (dev?.Name ?? _settings.LoudHistSourceName ?? "?").ToUpperInvariant() + " · " + min;
        }

        private string _loudCap, _loudCapSrc, _loudCapStruct;
        private int _loudCapMin;

        private void ApplyLoudCaption()
        {
            // nur neu bauen, wenn sich Geraet, Zeitraum oder die Geraete-Liste geaendert haben
            string src = _settings.LoudHistSource;
            if (_loudCap == null || _loudCapSrc != src || _loudCapMin != LoudMinutes || !string.Equals(_loudCapStruct, _snap.Structure))
            {
                _loudCapSrc = src; _loudCapMin = LoudMinutes; _loudCapStruct = _snap.Structure;
                _loudCap = LoudCaptionText();
            }
            string cap = _loudCap;
            _bridge.LoudCaption = cap;
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Bridge.LoudCaption = cap;
        }

        private void SetLoudHistSource(string id, string name)
        {
            string old = _settings.LoudHistSource;
            _settings.LoudHistSource = string.IsNullOrEmpty(id) ? null : id;
            _settings.LoudHistSourceName = _settings.LoudHistSource == null ? null : name;
            if (!string.Equals(old, _settings.LoudHistSource, StringComparison.OrdinalIgnoreCase)) _loudHist.Clear();   // anderes Geraet: neu beginnen
            ApplyLoudCaption();
            ScheduleSave();
            ShowOsd("Lautheits-Verlauf", LoudSourceText(), -1, Theme.Accent);
        }

        private void AddLoudSourceItems(ToolStripItemCollection items)
        {
            string src = _settings.LoudHistSource;
            var def = DefaultDevice;
            items.Add(new ToolStripMenuItem("Standard-Ausgabe" + (def != null ? "  (" + def.Name + ")" : ""), null, (s, e) => SetLoudHistSource(null, null))
            { Checked = src == null, ToolTipText = "Wie die Lautheit im MASTER – wechselt mit dem Windows-Standardgerät" });
            if (_snap.Devices.Length > 0) items.Add(new ToolStripSeparator());
            foreach (var d in _snap.Devices)
            {
                string id = d.Id, name = d.Name;
                items.Add(new ToolStripMenuItem(d.FullName ?? d.Name, null, (s, e) => SetLoudHistSource(id, name))
                { Checked = string.Equals(src, id, StringComparison.OrdinalIgnoreCase) });
            }
        }

        private ToolStripMenuItem LoudSourceSub()
        {
            var sub = Sub("Lautheits-Verlauf misst: " + LoudSourceText());
            AddLoudSourceItems(sub.DropDownItems);
            return sub;
        }

        private void ShowLoudHistMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Lautheits-Verlauf"));
            m.Items.Add(Hint("Wie laut es sich angefühlt hat (LUFS) – um −14 ist ideal."));
            m.Items.Add(new ToolStripSeparator());
            var src = Sub("Misst: " + LoudSourceText());
            AddLoudSourceItems(src.DropDownItems);
            m.Items.Add(src);
            m.Items.Add(new ToolStripSeparator());
            foreach (int min in MixerSettings.LoudHistMinuteValues)
            {
                int k = min;
                m.Items.Add(new ToolStripMenuItem("Letzte " + (min == 1 ? "Minute" : min + " Minuten"), null, (s, e) => SetLoudMinutes(k)) { Checked = LoudMinutes == min });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Verlauf löschen", null, (s, e) => _loudHist.Clear()));
            m.Items.Add(new ToolStripMenuItem("Ausblenden", null, (s, e) => SetLoudHist(false)));
            m.Show(screen);
        }

        // ================================================================ Meterbridge: LED oder Zeiger

        public static readonly string[] BridgeStyleNames = { "LED-Säulen", "Zeigerinstrumente (VU)" };

        private void SetBridgeStyle(int i)
        {
            _settings.BridgeStyle = Math.Max(0, Math.Min(1, i));
            MeterBridge.Style = _settings.BridgeStyle;
            StripBase.BridgeVuOn = _settings.BridgeStyle == 1;
            RestyleBridges();
            ScheduleSave();
        }

        private ToolStripMenuItem BridgeStyleSub()
        {
            var sub = Sub("Anzeige");
            for (int i = 0; i < BridgeStyleNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(BridgeStyleNames[i], null, (s, e) => SetBridgeStyle(k)) { Checked = _settings.BridgeStyle == i });
            }
            return sub;
        }

        // ================================================================ Meterbridge im eigenen Fenster

        private BridgeWindow _bridgeWin;
        private bool BridgeWinShown => _bridgeWin != null && _bridgeWin.IsOnScreen;

        private IEnumerable<StripBase> WindowBridgeStrips()
        {
            foreach (var c in _channelHost.Items) if (c is StripBase s) yield return s;
            foreach (var c in _outputHost.Items) if (c is StripBase s) yield return s;
        }

        public void ShowBridgeWindow()
        {
            if (_bridgeWin != null && !_bridgeWin.IsDisposed)
            {
                if (_bridgeWin.WindowState == FormWindowState.Minimized) _bridgeWin.WindowState = FormWindowState.Normal;
                _bridgeWin.Activate();
                return;
            }
            var w = new BridgeWindow(this, WindowBridgeStrips) { TopMost = _settings.BridgeWinTop };
            var b = w.Bridge;
            b.Spectrum = _spectrum;
            b.Loud = _loudHist;
            b.LoudCaption = LoudCaptionText();
            b.SpectrumHost = null;
            SyncBridgeWin(w);
            b.MenuRequested += p => ShowBridgeWinMenu(w, p);
            b.SpectrumClicked += ShowSpecSourceMenu;
            b.GonioClicked += ShowGonioMenu;
            b.LoudClicked += ShowLoudHistMenu;
            b.ClockClicked += ShowClockMenu;
            b.OrderChanged += OnBridgeOrderChanged;
            b.OscClicked += ShowOscMenu;
            b.CoverClicked += ShowSongMenu;
            var wa = Screen.FromControl(this).WorkingArea;
            if (_settings.BridgeWinW > 0 && _settings.BridgeWinH > 0 && _settings.BridgeWinX != int.MinValue)
            {
                var r = new Rectangle(_settings.BridgeWinX, _settings.BridgeWinY, _settings.BridgeWinW, _settings.BridgeWinH);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(10, r.Width - 80), 40))))
                    w.Bounds = r;
                else w.Location = new Point(wa.X + (wa.Width - w.Width) / 2, wa.Y + (wa.Height - w.Height) / 2);
            }
            else w.Location = new Point(Math.Max(wa.X, Left), Math.Max(wa.Y, Math.Min(wa.Bottom - w.Height, Bottom - Theme.Px(20))));
            w.FormClosed += (s, e) =>
            {
                RememberBridgeWin(w);
                if (_bridgeWin == w) _bridgeWin = null;
                bool appEnds = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing || e.CloseReason == CloseReason.ApplicationExitCall;
                if (!_closing && !appEnds) { _settings.BridgeWinOpen = false; ScheduleSave(); }
            };
            w.ResizeEnd += (s, e) => { RememberBridgeWin(w); ScheduleSave(); };
            _bridgeWin = w;
            w.Show();
            _settings.BridgeWinOpen = true;
            ScheduleSave();
        }

        private void CloseBridgeWindow()
        {
            if (_bridgeWin != null && !_bridgeWin.IsDisposed) _bridgeWin.Close();
        }

        private void RememberBridgeWin(BridgeWindow w)
        {
            if (w.WindowState != FormWindowState.Normal || FullScreen.IsOn(w)) return;   // Vollbild nicht als Groesse merken
            var b = w.Bounds;
            _settings.BridgeWinX = b.X; _settings.BridgeWinY = b.Y; _settings.BridgeWinW = b.Width; _settings.BridgeWinH = b.Height;
        }

        private void SyncBridgeWin() => SyncBridgeWin(_bridgeWin);

        private void SyncBridgeWin(BridgeWindow w)
        {
            if (w == null || w.IsDisposed) return;
            var b = w.Bridge;
            b.SpectrumOn = !_settings.SpecOff;
            b.ClockOn = _bridge.ClockOn;
            b.GonioOn = !_settings.GonioOff;
            b.LoudOn = LoudHistOn;
            b.OscOn = !_settings.OscOff;
            b.CoverOn = !_settings.CoverOff;
            b.SpectrumCaption = SpecCaption();
        }

        private void ShowBridgeWinMenu(BridgeWindow w, Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.Items.Add(Header("Meterbridge im Fenster"));
            m.Items.Add(new ToolStripSeparator());
            AddBridgeContextItems(m.Items, w.Bridge);
            m.Items.Add(new ToolStripMenuItem("Immer im Vordergrund", null, (s, e) => { w.TopMost = !w.TopMost; _settings.BridgeWinTop = w.TopMost; ScheduleSave(); }) { Checked = w.TopMost });
            m.Items.Add(new ToolStripMenuItem("Vollbild  (F11)", null, (s, e) => FullScreen.Toggle(w)) { Checked = FullScreen.IsOn(w) });
            m.Items.Add(BridgeStyleSub());
            m.Items.Add(BridgeBarSub());
            m.Items.Add(BridgeColumnsSub());
            AddBridgeArrangeItems(m.Items);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Spektrum anzeigen", null, (s, e) => SetSpectrumOn(_settings.SpecOff)) { Checked = !_settings.SpecOff });
            m.Items.Add(SpecSourceSub());
            m.Items.Add(new ToolStripMenuItem("Stereo-Bild anzeigen", null, (s, e) => SetGonio(_settings.GonioOff)) { Checked = !_settings.GonioOff });
            m.Items.Add(GonioSourceSub());
            m.Items.Add(new ToolStripMenuItem("Lautheits-Verlauf anzeigen", null, (s, e) => SetLoudHist(_settings.LoudHistOff)) { Checked = LoudHistOn });
            m.Items.Add(LoudSourceSub());
            m.Items.Add(new ToolStripMenuItem("Uhr anzeigen", null, (s, e) => SetBridgeClock(!ExtraOn('c'))) { Checked = ExtraOn('c') });
            m.Items.Add(new ToolStripMenuItem("Oszilloskop anzeigen", null, (s, e) => SetOscOn(_settings.OscOff)) { Checked = !_settings.OscOff });
            m.Items.Add(new ToolStripMenuItem("„Jetzt läuft“ mit Cover anzeigen", null, (s, e) => SetCoverOn(_settings.CoverOff)) { Checked = !_settings.CoverOff });
            if (!_settings.BridgeArrange) m.Items.Add(BridgeOrderSub());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Schließen", null, (s, e) => w.Close()));
            m.Show(screen);
        }

        // ================================================================ Kompakt-Ansicht

        private void SetCompact(bool on)
        {
            _settings.Compact = on;
            StripBase.Compact = on;
            RelayoutAllStrips();
            RefreshStrips();
            UpdateBridgeVisibility();
            ScheduleSave();
        }

        // ================================================================ Bild pro Kanal

        private void ChooseChannelImage(ChannelConfig c, ChannelStrip strip)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Bild für " + c.Name + " wählen",
                Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Alle Dateien (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ChannelImages.Forget(dlg.FileName);
                if (ChannelImages.Get(dlg.FileName) == null)
                {
                    ShowOsd("Bild", "Bild konnte nicht geladen werden", -1, Theme.MuteRed);
                    return;
                }
                c.ImagePath = dlg.FileName;
                if (_settings.ChannelImagesOff) { _settings.ChannelImagesOff = false; ChannelStrip.ImagesOn = true; }
                strip?.InvalidateChrome();
                ScheduleSave();
            }
        }

        private void ClearChannelImage(ChannelConfig c, ChannelStrip strip)
        {
            c.ImagePath = null;
            strip?.InvalidateChrome();
            ScheduleSave();
        }

        private void SetChannelImagesOn(bool on)
        {
            _settings.ChannelImagesOff = !on;
            ChannelStrip.ImagesOn = on;
            RefreshStrips();
        }

        /// <summary>Eintraege fuers Kanal-Menue.</summary>
        private void AddChannelImageItems(ToolStripItemCollection items, ChannelConfig c, ChannelStrip strip)
        {
            var sub = Sub("Bild im Kanal", false);
            sub.DropDownItems.Add(new ToolStripMenuItem("Bild wählen …", null, (s, e) => BeginInvoke(new Action(() => ChooseChannelImage(c, strip)))));
            if (!string.IsNullOrEmpty(c.ImagePath))
                sub.DropDownItems.Add(new ToolStripMenuItem("Bild entfernen", null, (s, e) => ClearChannelImage(c, strip)));
            sub.DropDownItems.Add(Hint("Erscheint hinter den App-Symbolen, z. B. ein Spiel-Cover."));
            items.Add(sub);
        }
    }
}
