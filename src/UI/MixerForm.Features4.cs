using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.12: grosses VU-Fenster, Spektrum, Pult-Helligkeit (abends dunkler) und Uhr in der Kopfleiste.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private VuWindow _vuWin;
        private readonly SpectrumModel _spectrum = new SpectrumModel();

        // ================================================================ Grosses VU-Fenster

        public bool VuWindowOpen => _vuWin != null && !_vuWin.IsDisposed;

        /// <summary>Grosses VU-Paar oeffnen (bzw. nach vorne holen). wantVu: VU-Meter auf jeden Fall zeigen (sonst wie zuletzt).</summary>
        public void ShowVuWindow() => ShowVuWindow(true);

        private void ShowVuWindow(bool wantVu)
        {
            if (VuWindowOpen)
            {
                if (wantVu && !_vuWin.ShowVu) { _vuWin.ShowVu = true; _vuWin.Restyle(); VuWindowChanged(_vuWin); }
                if (_vuWin.WindowState == FormWindowState.Minimized) _vuWin.WindowState = FormWindowState.Normal;
                _vuWin.Activate();
                return;
            }
            if (wantVu && _settings.VuWinVuOff) { _settings.VuWinVuOff = false; _settings.VuWinSpecOff = false; }   // war "nur Spektrum": beides zeigen
            var w = new VuWindow(this, _spectrum) { TopMost = _settings.VuWinTop, ShowSpectrum = !_settings.VuWinSpecOff || _settings.VuWinVuOff, ShowVu = !_settings.VuWinVuOff };
            w.Caption = SpecCaption();
            w.UpdateTitle();
            var wa = Screen.FromControl(this).WorkingArea;
            if (_settings.VuWinW > 0 && _settings.VuWinH > 0)
            {
                var r = new Rectangle(_settings.VuWinX, _settings.VuWinY, _settings.VuWinW, _settings.VuWinH);
                // nur, wenn die Stelle noch auf einem Bildschirm liegt (Bildschirm abgesteckt?)
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(10, r.Width - 80), 40))))
                    w.Bounds = r;
                else w.Location = new Point(wa.X + (wa.Width - w.Width) / 2, wa.Y + (wa.Height - w.Height) / 2);
            }
            else w.Location = new Point(Math.Max(wa.X, Right - w.Width - Theme.Px(30)), Math.Max(wa.Y, Top + Theme.Px(80)));
            w.FormClosed += (s, e) =>
            {
                RememberVuWindow(w);
                if (_vuWin == w) _vuWin = null;
                bool appEnds = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing || e.CloseReason == CloseReason.ApplicationExitCall;
                if (!_closing && !appEnds) { _settings.VuWinOpen = false; ScheduleSave(); }
            };
            w.ResizeEnd += (s, e) => { RememberVuWindow(w); ScheduleSave(); };
            _vuWin = w;
            w.Show();
            _settings.VuWinOpen = true;
            ScheduleSave();
        }

        private bool _closing;

        public void CloseVuWindow()
        {
            if (VuWindowOpen) _vuWin.Close();
        }

        private void ToggleVuWindow()
        {
            if (VuWindowOpen) CloseVuWindow(); else ShowVuWindow();
        }

        private void RememberVuWindow(VuWindow w)
        {
            if (w.WindowState != FormWindowState.Normal || FullScreen.IsOn(w)) return;
            var b = w.Bounds;
            _settings.VuWinX = b.X; _settings.VuWinY = b.Y; _settings.VuWinW = b.Width; _settings.VuWinH = b.Height;
        }

        /// <summary>Vom VU-Fenster: immer im Vordergrund / Spektrum geaendert.</summary>
        public void VuWindowChanged(VuWindow w)
        {
            _settings.VuWinTop = w.TopMost;
            _settings.VuWinSpecOff = !w.ShowSpectrum;
            _settings.VuWinVuOff = !w.ShowVu;
            ScheduleSave();
        }

        public void ToggleVuScaleFromWindow() => SetVuScale(!Theme.VuDbScale);

        public void ShowVuWindowMenu(VuWindow w, Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header(!w.ShowVu ? "Großes Spektrum" : "Großes VU-Meter"));
            m.Items.Add(new ToolStripSeparator());
            int mode = VuWinMode(w);
            for (int i = 0; i < VuWinModeNames.Length; i++)
            {
                int k = i;
                m.Items.Add(new ToolStripMenuItem(VuWinModeNames[i], null, (s, e) => SetVuWinMode(w, k)) { Checked = mode == i });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(SpecSourceSub());
            m.Items.Add(new ToolStripMenuItem("Immer im Vordergrund", null, (s, e) => { w.TopMost = !w.TopMost; VuWindowChanged(w); }) { Checked = w.TopMost });
            if (w.ShowVu)
            {
                var start = Sub("Zeiger reagiert ab");
                AddVuStartItems(start.DropDownItems);
                m.Items.Add(start);
            }
            m.Items.Add(new ToolStripMenuItem("Vollbild  (F11)", null, (s, e) => FullScreen.Toggle(w)) { Checked = FullScreen.IsOn(w) });
            m.Items.Add(new ToolStripMenuItem("Einstellungen zum VU-Meter …", null, (s, e) => ShowSettings("Leuchten & Anzeigen")));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Schließen", null, (s, e) => w.Close()));
            m.Show(screen);
        }

        // ================================================================ Spektrum

        private void SetSpectrumOn(bool on)
        {
            _settings.SpecOff = !on;
            _bridge.SpectrumOn = on;
            _bridge.Restyle();
            ScheduleSave();
        }

        /// <summary>Zug, ueber dem das Spektrum steht, wenn in der Meterbridge keine Luecke frei ist.</summary>
        private StripBase SpectrumHostStrip()
        {
            if (!MultiOutput) return _master;
            var def = DefaultDevice;
            if (def != null && _outputStrips.TryGetValue(def.Id, out var os)) return os;
            return _outputStrips.Values.FirstOrDefault();
        }

        // ================================================================ Pult-Helligkeit

        public static readonly string[] PultDimModes = { "Aus", "Immer", "Abends", "Mit NACHT" };
        private static readonly int[] DimFromHours = { 18, 19, 20, 21, 22 };
        private static readonly int[] DimToHours = { 5, 6, 7, 8, 9 };

        /// <summary>Wie dunkel das Pult jetzt sein soll (0 = normal).</summary>
        private float PultDimTarget()
        {
            float dim = (100 - (_settings.PultBright ?? MixerSettings.PultBrightDefault)) / 100f;
            switch (_settings.PultDimMode)
            {
                case 1: return dim;
                case 2:
                    {
                        int h = DateTime.Now.Hour, from = _settings.PultDimFrom ?? 20, to = _settings.PultDimTo ?? 7;
                        bool night = from > to ? (h >= from || h < to) : (h >= from && h < to);
                        return night ? dim : 0f;
                    }
                case 3: return _settings.NightMode ? dim : 0f;
            }
            return 0f;
        }

        private double _nextDimCheck;

        /// <summary>Pult-Helligkeit anwenden, wenn sie sich geaendert hat (alle Flaechen neu zeichnen).</summary>
        private void UpdatePultDim(bool force = false)
        {
            if (!force && Now < _nextDimCheck) return;
            _nextDimCheck = Now + 1.0;
            float t = Math.Max(PultDimTarget(), IdleDimTarget());   // Ruhemodus: nach einer Weile ohne Bedienung dunkel
            if (Math.Abs(t - Theme.NightDim) < 0.001f) return;   // nur bei echter Aenderung alles neu zeichnen
            Theme.NightDim = t;
            SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);   // alles neu zeichnen
            foreach (var c in new Control[] { _channelHost, _outputHost, _divider, _addButton }) c.Invalidate(true);
        }

        private void SetPultDimMode(int mode)
        {
            _settings.PultDimMode = Math.Max(0, Math.Min(PultDimModes.Length - 1, mode));
            UpdatePultDim(true);
            ScheduleSave();
        }

        private void SetPultBright(int pct, bool final)
        {
            _settings.PultBright = Math.Max(MixerSettings.PultBrightMin, Math.Min(MixerSettings.PultBrightMax, pct));
            if (final) { UpdatePultDim(true); ScheduleSave(); }
        }

        // ================================================================ Uhr

        private void SetClockOn(bool on)
        {
            _settings.ClockOff = !on;
            ApplyClock();
            ScheduleSave();
        }

        private void SetClockStyle(int style)
        {
            _settings.ClockStyle = Math.Max(0, Math.Min(8, style));   // 4.0: 6 Wortuhr, 7 Binaeruhr, 8 Oszilloskop
            ApplyClock();
            ScheduleSave();
        }

        /// <summary>Uhr in der Meterbridge (wenn Platz und gewuenscht – 2.16.2 auch im LCD-Look), sonst oben in der Kopfleiste.</summary>
        private void ApplyClock()
        {
            _top.ClockStudio = _settings.ClockStyle == 0;
            _top.ClockAnalog = _settings.ClockStyle == 2;
            Theme.AnalogClock = _settings.ClockStyle == 2;
            Theme.ClockKind = _settings.ClockStyle;      // 3.3: Fallblatt, Nixie
            _top.ClockKind = _settings.ClockStyle;
            _bridge.ClockOn = !_settings.ClockOff && !_settings.BridgeClockOff;
            MeterBridge.LcdClock = _settings.ClockStyle == 1;
            MeterBridge.ConfigVersion++;
            RestyleBridges();
            UpdateTopClock();
            _top.Invalidate();
        }

        /// <summary>Uhr in der Kopfleiste nur, wenn sie nicht schon in der Meterbridge steht (Sleep-Timer-Restzeit immer oben).</summary>
        private void UpdateTopClock()
        {
            bool on = !_settings.ClockOff && (!_bridge.ClockShown || _sleepEnd > 0);
            if (_top.ClockOn != on) { _top.ClockOn = on; _top.Invalidate(); }
        }

        private void ShowClockMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = true;
            m.Items.Add(Header(DateTime.Now.ToString("dddd, d. MMMM", new System.Globalization.CultureInfo("de-DE"))));
            m.Items.Add(new ToolStripSeparator());
            var sleep = BuildSleepMenu();
            foreach (var it in sleep.DropDownItems.Cast<ToolStripItem>().ToList()) m.Items.Add(it);
            m.Items.Add(new ToolStripSeparator());
            if (_settings.FocusOn) m.Items.Add(new ToolStripMenuItem(_focusRun ? "Fokus-Timer stoppen" : "Fokus-Timer starten", null, (s, e) => ToggleFocus()) { Checked = _focusRun });   // 4.0
            m.Items.Add(new ToolStripMenuItem("Uhr ausblenden", null, (s, e) => SetClockOn(false)));
            m.Show(screen);
        }

        // ================================================================ Takt

        private void TickFeatures4(float dt)
        {
            // Stereo-Bild und Spektrum nur messen, wenn sie irgendwo zu sehen sind (auch in der Meterbridge im Fenster)
            bool win = BridgeWinShown;
            bool gonio = (_bridge.GonioShown && PultShown) || (win && _bridgeWin.Bridge.GonioShown)
                         || (_bridge.OscShown && PultShown) || (win && _bridgeWin.Bridge.OscShown);   // 2.17: Oszilloskop braucht dieselben Daten
            Backend.SetScope(gonio);
            Backend.SetScopeSource(gonio ? GonioDeviceIds() : null);
            bool spec = (!_settings.SpecOff && ((_bridge.SpectrumShown && PultShown) || (win && _bridgeWin.Bridge.SpectrumShown)))
                        || (VuWindowOpen && _vuWin.ShowSpectrum && _vuWin.Visible && _vuWin.WindowState != FormWindowState.Minimized)
                        || RgbWantsSpectrum   // 2.20: RGB-Beleuchtung nach dem Spektrum
                        || (_settings.SpecBgOn && PultShown)   // 3.5: Spektrum hinter den Kanaelen
                        || WledWantsSpectrum400                // 4.0: WLED nach dem Spektrum
                        || WallpaperWantsSpectrum400           // 4.0 finale: Musik-Wallpaper
                        || EqOverviewWantsSpectrum400;         // 4.0 finale: EQ-Uebersicht
            Backend.SetMasterSpectrum(spec ? SpectrumDeviceIds() : null);
            if (spec) _spectrum.Update(DelayedSpectrum(_snap.MasterSpectrum), dt);
            else if (_spectrum.Active) _spectrum.Reset();
            ApplySpecCaption();

            if (VuWindowOpen && _vuWin.Visible && _vuWin.WindowState != FormWindowState.Minimized)
            {
                var pk = DelayedVuPeak(SpecSourcePeak(out string name));   // 2.16: wie die Pegel, passend zum Ton
                _vuWin.Feed(pk[0], pk[1], name, dt);
            }

            UpdatePultDim();

            int left = _sleepEnd > 0 ? (int)Math.Ceiling(Math.Max(0, _sleepEnd - Now)) : -1;
            if (left != _top.SleepLeft) { _top.SleepLeft = left; _top.Invalidate(); }
            UpdateTopClock();
            _top.TickClock();
            _top.TickDeco();   // 3.1: Saison-Deko
        }
    }
}
