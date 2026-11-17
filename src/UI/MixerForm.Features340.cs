using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4 – Ton &amp; Komfort, Bedienung, Anzeigen &amp; Optik: sanfter Start, Leise-Stunden, Kopfhoerer-Pause, Lautheit angleichen,
    /// EQ A/B, EQ malen, MONO-Taste, Lautstaerketasten, Mausrad am Symbol, Schnell-Regler, Tastatur-Bedienung, Kanal duplizieren,
    /// Klang zuruecksetzen, PC-Auslastung, Papierschreiber, Studio-Boxen, Licht nach Uhrzeit, Staubschutzhaube, Typenschild,
    /// zweifarbige Kanaele und Aufkleber. Alles einzeln an/aus – Standard: aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private readonly Stopwatch _clock340 = Stopwatch.StartNew();
        private double Now340 => _clock340.Elapsed.TotalSeconds;

        // ================================================================ Start

        private void ApplyFeatures340Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                s.SoftStartMode = DemoInt("SOFTSTART", s.SoftStartMode);
                if (DemoOn("QUIET")) s.QuietHoursOn = true;
                if (DemoOn("PHONESPAUSE")) { s.PhonesPauseOn = true; s.PhonesResumeOn = true; }
                if (DemoOn("LEVELMATCH")) s.LevelMatchOn = true;
                if (DemoOn("EQAB")) { s.EqAbOn = true; if (s.Channels.Count > 2) { s.Channels[2].EqIsB = true; s.Channels[2].EqAlt = new float[13]; } }
                if (DemoOn("EQPAINT")) s.EqPaintOn = true;
                if (DemoOn("MONOKEY")) s.MonoKeyOn = true;
                if (DemoOn("VOLKEYS")) s.VolKeysOn = true;
                if (DemoOn("TRAYWHEEL")) s.TrayWheelOn = true;
                if (DemoOn("QUICKPOPUP")) s.QuickPopupOn = true;
                if (DemoOn("KEYNAV")) s.KeyNavOn = true;
                if (DemoOn("DUPLICATE")) s.DuplicateOn = true;
                if (DemoOn("SOUNDRESET")) s.SoundResetOn = true;
                if (DemoOn("PCMETER")) s.PcMeterOn = true;
                if (DemoOn("PAPER")) s.PaperLookOn = true;
                if (DemoOn("SPEAKERS")) s.SpeakersOn = true;
                if (DemoOn("SUN")) s.SunLightOn = true;
                float sh; if (float.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SUNHOUR"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sh)) Theme.SunHourOverride = sh;
                if (DemoOn("DUSTCOVER")) s.DustCoverOn = true;
                if (DemoOn("TYPEPLATE")) { s.NamePlateOn = true; if (s.NamePlateText == null) s.NamePlateText = "Jans Studio"; }
                if (DemoOn("TWOTONE"))
                {
                    s.TwoToneOn = true; s.ColorCapsOff = false;
                    if (s.Channels.Count > 3) { s.Channels[1].Color2 = unchecked((int)0xffffc83a); s.Channels[3].Color2 = unchecked((int)0xff3fd0ff); }
                }
                if (DemoOn("STICKERS") && s.Channels.Count > 3)
                {
                    s.StickersOn = true;
                    s.Stickers = new List<StickerConfig>
                    {
                        new StickerConfig { Owner = s.Channels[1].Id, Kind = 0, X = 70, Y = 300, Angle = -12 },
                        new StickerConfig { Owner = s.Channels[3].Id, Kind = 3, X = 55, Y = 520, Angle = 8 },
                        new StickerConfig { Owner = "master", Kind = 2, X = 30, Y = 640, Angle = -20 },
                    };
                }
            }
            CleanStart.NewAppFadeMs = s.SoftStartMode == 2 ? s.SoftStartSecNow * 1000 : 0;
            ChannelStrip.EqAbOn = s.EqAbOn;
            EqWindow.PaintOn = s.EqPaintOn;
            OutputStrip.MonoKeyOn = s.MonoKeyOn;
            MeterBridge.PcOn = s.PcMeterOn;
            LoudHistView.PaperLook = s.PaperLookOn;
            Theme.SunOn = s.SunLightOn;
            StripHost.TypePlateText = s.NamePlateOn ? (s.NamePlateText ?? "MEIN STUDIO") : null;
            Theme.TwoTone = s.TwoToneOn;
            StripBase.StickersOn = s.StickersOn;
            StripBase.Stickers = s.Stickers;
            PcLoad.Demo = demo;
            if (s.PcMeterOn) PcLoad.Start();
        }

        private void ShownFeatures340()
        {
            Native.ThreadCpu.Register("Oberfläche");   // 3.4.1: Rechenzeit in der Bild-Statistik
            HookHosts346();   // 3.4.6: Deko, Kabel
            HookControl346(); // 3.4.6: Szenen-Tasten, Taskleiste, Schublade …
            HookFeatures350(); // 3.5
            HookFeatures400(); // 4.0
            // nur Vorschau: nach n Sekunden beenden (Messungen)
            if (_demo && int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_QUITAFTER"), out int quitSec) && quitSec > 0)
            {
                var qt = new Timer { Interval = quitSec * 1000 };
                qt.Tick += (s, e) => { qt.Stop(); Environment.Exit(0); };
                qt.Start();
            }
            _channelHost.ShowTypePlate = true;
            _channelHost.Invalidate(false);
            ApplySpeakers();
            ApplyTrayWheel();
            if (_demo)
            {
                if (DemoOn("QUICKPOPUPWIN")) DemoLater(() => ShowQuickPopup(new Point(Left + Width - 40, Top + Height - 40)));
                if (DemoOn("DUSTCOVERWIN")) DemoLater(() => { _demoDust = true; });
                if (DemoOn("KEYNAVSEL") && _channelStrips.Count > 1) DemoLater(() => StripBase.KeyFocus = _channelStrips[1]);
                if (DemoOn("CHMENU340") && _channelStrips.Count > 1)
                    DemoLater(() => { var st = _channelStrips[1]; ShowChannelMenu(st, st.PointToScreen(new Point(st.Width / 2, Theme.Px(320)))); });
            }
        }

        private double _next340Slow, _next340Fast;

        private void TickFeatures340()
        {
            double now = Now340;
            TickSoftStart(now);
            TickLevelMatch(now);
            if (now >= _next340Fast)
            {
                _next340Fast = now + 0.5;
                TickPhones(now);
            }
            if (now >= _next340Slow)
            {
                _next340Slow = now + 1.0;
                TickQuietHours(now);
                TickSun();
            }
            TickSpeakers();
            TickDustCover();
            if (_quick != null && _quick.Visible) _quick.Tick();
        }

        // ================================================================ 80 Sanfter Start

        private double _ssStart = -1;
        private bool _ssDone;
        private double _ssNextPush;

        /// <summary>Faktor fuer die Kanal-Lautstaerken beim Start des Pults (0 → 1 in der eingestellten Zeit).</summary>
        private float SoftStartGain()
        {
            if (_settings.SoftStartMode == 0 || _ssDone) return 1f;
            double now = Now340;
            if (_ssStart < 0) _ssStart = now;   // ab dem ersten Einstellen der Lautstaerken
            float u = (float)Math.Min(1.0, (now - _ssStart) / _settings.SoftStartSecNow);
            return u * u * (3f - 2f * u);
        }

        private void TickSoftStart(double now)
        {
            if (_settings.SoftStartMode == 0 || _ssDone || _ssStart < 0) return;
            bool done = now - _ssStart >= _settings.SoftStartSecNow;
            if (!done && now < _ssNextPush) return;
            _ssNextPush = now + 0.03;
            if (done) _ssDone = true;
            PushPolicy();
        }

        private void SetSoftStart(int mode, int sec)
        {
            _settings.SoftStartMode = Math.Max(0, Math.Min(2, mode));
            _settings.SoftStartSec = sec;
            CleanStart.NewAppFadeMs = _settings.SoftStartMode == 2 ? _settings.SoftStartSecNow * 1000 : 0;
            _ssDone = true;   // gilt beim naechsten Start
            ScheduleSave();
        }

        // ================================================================ 81 Leise-Stunden

        private bool _quietNotified;

        private bool InQuietHours(DateTime t)
        {
            int from = _settings.QuietFromNow, to = _settings.QuietToNow, h = t.Hour;
            if (from == to) return true;
            return from < to ? h >= from && h < to : h >= from || h < to;
        }

        private void TickQuietHours(double now)
        {
            if (!_settings.QuietHoursOn || !InQuietHours(DateTime.Now)) { _quietNotified = false; return; }
            float cap = _settings.QuietMaxNow / 100f;
            bool hit = false;
            foreach (var d in _snap.Devices)
            {
                if (d.Volume <= cap + 0.005f) continue;
                d.Volume = cap;
                try { Backend.SetDeviceVolume(d.Id, cap); } catch { }
                hit = true;
            }
            if (hit)
            {
                _master.InvalidateChrome();
                foreach (var s in _outputStrips.Values) s.InvalidateChrome();
                if (!_quietNotified) ShowOsd("Leise-Stunden", "höchstens " + _settings.QuietMaxNow + " % bis " + _settings.QuietToNow + " Uhr", cap, Color.FromArgb(0x7a, 0xa8, 0xff));
                _quietNotified = true;
            }
        }

        // ================================================================ 82 Kopfhoerer abgezogen

        private string _phonesLastDefault, _phonesGoneId;
        private double _phonesGoneAt;
        private List<string> _phonesPaused;

        private void TickPhones(double now)
        {
            var ids = new HashSet<string>(_snap.Devices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            string def = _snap.Devices.FirstOrDefault(d => d.IsDefault)?.Id;
            if (_settings.PhonesPauseOn && _phonesLastDefault != null && ids.Count > 0 && !ids.Contains(_phonesLastDefault))
            {
                // Standard-Geraet ist weg (Kopfhoerer abgezogen, Bluetooth aus): Musik anhalten
                string gone = _phonesLastDefault;
                _phonesGoneId = gone; _phonesGoneAt = now;
                Task.Run(() => NowPlayingWatcher.ControlAll(true, null)).ContinueWith(t =>
                {
                    var list = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _phonesPaused = list;
                            if (list != null && list.Count > 0) ShowOsd("Kopfhörer abgezogen", "Musik pausiert", -1, Theme.SoloAmber);
                        }));
                    }
                    catch { }
                });
            }
            else if (_settings.PhonesResumeOn && _phonesGoneId != null && ids.Contains(_phonesGoneId))
            {
                var list = _phonesPaused;
                _phonesGoneId = null;
                if (list != null && list.Count > 0 && now - _phonesGoneAt < 600)
                {
                    _phonesPaused = null;
                    Task.Run(() => NowPlayingWatcher.ControlAll(false, list));
                    ShowOsd("Kopfhörer wieder da", "Musik läuft weiter", -1, Theme.LedGreen);
                }
            }
            if (_phonesGoneId != null && now - _phonesGoneAt > 600) { _phonesGoneId = null; _phonesPaused = null; }
            if (def != null) _phonesLastDefault = def;
        }

        // ================================================================ 83 Lautheit angleichen

        private double _lmStart = -1;
        private readonly Dictionary<string, double> _lmSum = new Dictionary<string, double>();
        private readonly Dictionary<string, int> _lmCount = new Dictionary<string, int>();
        private const double LevelMatchSec = 10;

        public bool LevelMatchRunning => _lmStart >= 0;

        private void StartLevelMatch()
        {
            if (!_settings.LevelMatchOn || _lmStart >= 0) return;
            _lmSum.Clear(); _lmCount.Clear();
            _lmStart = Now340;
            ShowOsd("Lautheit angleichen", "hört 10 Sekunden zu – lass alles normal laufen …", -1, Theme.Accent);
        }

        private void TickLevelMatch(double now)
        {
            if (_lmStart < 0) return;
            foreach (var c in _settings.Channels)
            {
                if (!_channelPeak.TryGetValue(c.Id, out var pk) || pk == null || pk.Length < 4) continue;
                float pre = Math.Max(pk[2], pk[3]);
                if (pre <= 0.003f) continue;   // unter etwa −50 dB: Stille zaehlt nicht
                double db = 20 * Math.Log10(pre);
                _lmSum.TryGetValue(c.Id, out var s); _lmSum[c.Id] = s + db;
                _lmCount.TryGetValue(c.Id, out var n); _lmCount[c.Id] = n + 1;
            }
            if (now - _lmStart < LevelMatchSec) return;
            _lmStart = -1;
            var post = new List<KeyValuePair<ChannelConfig, double>>();
            foreach (var c in _settings.Channels)
            {
                if (c.FaderLock || c.Mute || !_lmCount.TryGetValue(c.Id, out var n) || n < 30) continue;
                double pre = _lmSum[c.Id] / n;
                float fdb = FaderLaw.PosToDb(c.Fader);
                if (float.IsNegativeInfinity(fdb) || fdb < -50f) continue;   // ganz unten: nicht anfassen
                post.Add(new KeyValuePair<ChannelConfig, double>(c, pre));
            }
            var r = LevelMatchPlan(post.Select(kv => new KeyValuePair<string, double>(kv.Key.Id, kv.Value)).ToList(),
                                   post.ToDictionary(kv => kv.Key.Id, kv => (double)FaderLaw.PosToDb(kv.Key.Fader)));
            int changed = 0;
            foreach (var kv in post)
            {
                if (!r.TryGetValue(kv.Key.Id, out var newDb)) continue;
                float pos = FaderLaw.DbToPos((float)newDb);
                if (Math.Abs(pos - kv.Key.Fader) < 0.002f) continue;
                kv.Key.Fader = pos;
                ClampToCap(kv.Key);
                changed++;
                _channelStrips.FirstOrDefault(s => s.Config == kv.Key)?.InvalidateChrome();
            }
            if (changed > 0) { PushPolicy(); ScheduleSave(); }
            ShowOsd("Lautheit angeglichen", post.Count < 2 ? "Zu wenig lief – mindestens zwei Kanäle müssen spielen" : changed + (changed == 1 ? " Kanal" : " Kanäle") + " nachgestellt", -1, post.Count < 2 ? Theme.SoloAmber : Theme.LedGreen);
        }

        /// <summary>
        /// Neue Faderstellungen (dB) fuer gleich laute Kanaele: Ziel ist der Median von (gemessen vor dem Fader + Fader),
        /// nie ueber 0 dB, nie unter −40 dB. pre = gemessener Pegel vor dem Fader (dB), fader = jetzige Stellung (dB).
        /// </summary>
        internal static Dictionary<string, double> LevelMatchPlan(List<KeyValuePair<string, double>> pre, Dictionary<string, double> fader)
        {
            var res = new Dictionary<string, double>();
            if (pre == null || pre.Count < 2) return res;
            var post = pre.Select(kv => kv.Value + fader[kv.Key]).OrderBy(v => v).ToList();
            double target = post.Count % 2 == 1 ? post[post.Count / 2] : (post[post.Count / 2 - 1] + post[post.Count / 2]) / 2;
            foreach (var kv in pre) res[kv.Key] = Math.Max(-40.0, Math.Min(0.0, target - kv.Value));
            return res;
        }

        // ================================================================ 84 EQ A/B

        public void ToggleEqAb(ChannelConfig c)
        {
            if (c == null || !_settings.EqAbOn) return;
            var cur = new float[13];
            cur[0] = c.EqLow; cur[1] = c.EqMid; cur[2] = c.EqHigh;
            if (c.Geq != null && c.Geq.Length == 10) Array.Copy(c.Geq, 0, cur, 3, 10);
            var other = c.EqAlt ?? new float[13];   // noch kein B: flach beginnen
            c.EqLow = other[0]; c.EqMid = other[1]; c.EqHigh = other[2];
            bool flatGeq = true;
            for (int i = 3; i < 13; i++) if (Math.Abs(other[i]) > 0.01f) flatGeq = false;
            if (flatGeq) c.Geq = null; else { c.Geq = new float[10]; Array.Copy(other, 3, c.Geq, 0, 10); }
            c.EqAlt = cur;
            c.EqIsB = !c.EqIsB;
            OnChannelEdited(c);
            if (_eqWin != null && !_eqWin.IsDisposed) _eqWin.Invalidate();
            ScheduleSave();
            ShowOsd(c.Name, c.EqIsB ? "EQ B" : "EQ A", -1, Color.FromArgb(c.Color));
        }

        // ================================================================ 86 MONO am MASTER

        public bool MasterMono
        {
            get
            {
                var d = DefaultDevice;
                return d != null && !_settings.OutFxOff && (OutFxConfig(d.Id, false)?.Mono ?? false);
            }
        }

        public void ToggleMasterMono()
        {
            var d = DefaultDevice;
            if (d == null) return;
            SetOutputMono(d.Id, d.Name, !MasterMono, false);
            _master.InvalidateChrome();
        }

        // ================================================================ 87 Lautstaerketasten

        /// <summary>Zusaetzliche systemweite Tasten: Lautstaerke hoch/runter/stumm der Tastatur (nur wenn eingeschaltet).</summary>
        private IEnumerable<HotkeyBinding> HotkeyExtras340()
        {
            if (!_settings.VolKeysOn) yield break;
            yield return new HotkeyBinding { Action = "volkey.up", Keys = (int)Keys.VolumeUp };
            yield return new HotkeyBinding { Action = "volkey.down", Keys = (int)Keys.VolumeDown };
            yield return new HotkeyBinding { Action = "volkey.mute", Keys = (int)Keys.VolumeMute };
        }

        private string TargetName(string id) => id == null ? "MASTER" : _settings.Channels.FirstOrDefault(c => c.Id == id)?.Name ?? "MASTER";

        private string TargetAction(string id, string what)
        {
            if (id == null || !_settings.Channels.Any(c => c.Id == id)) return "master." + what;
            return "ch:" + id + ":" + what;
        }

        private void ShowTargetMenu(Rectangle r, Func<string> get, Action<string> set)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = true;
            m.Items.Add(new ToolStripMenuItem("MASTER (Windows-Lautstärke)", null, (s, e) => set(null)) { Checked = get() == null });
            m.Items.Add(new ToolStripSeparator());
            foreach (var c in _settings.Channels)
            {
                var ch = c;
                m.Items.Add(new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color)), (s, e) => set(ch.Id)) { Checked = get() == c.Id });
            }
            ShowMenuAt(m, r);
        }

        // ================================================================ 88 Mausrad ueber dem Symbol

        private IntPtr _wheelHook = IntPtr.Zero;
        private LowLevelProc _wheelProc;
        private Rectangle _trayRect;
        private long _trayRectAt;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);

        private void ApplyTrayWheel()
        {
            bool want = ((_settings.TrayWheelOn && _tray != null) || _settings.WinWheelOn || _settings.HotCornerOn) && Win32.IsWindows && !_demo;   // 4.0: Mausrad ueber Fenstern, heisse Ecke
            if (want && _wheelHook == IntPtr.Zero)
            {
                _wheelProc = WheelHook;
                try { _wheelHook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _wheelProc, GetModuleHandle(null), 0); } catch { _wheelHook = IntPtr.Zero; }
            }
            else if (!want && _wheelHook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_wheelHook); } catch { }
                _wheelHook = IntPtr.Zero;
            }
        }

        private void CleanupFeatures340()
        {
            try { _obs?.Dispose(); _wled?.Dispose(); _midiOut?.Dispose(); CloseChanWins400(); _overlay?.Close(); } catch { }   // 4.0
            try { CleanupPro400(); CleanupFinal400(); } catch { }   // 4.0 finale
            try { CleanupUpdate410(); } catch { }                  // 4.1
            try { CleanupLook420(); } catch { }                    // 4.2
            if (_wheelHook != IntPtr.Zero) { try { UnhookWindowsHookEx(_wheelHook); } catch { } _wheelHook = IntPtr.Zero; }
            PcLoad.Stop();
            try { _dust?.Close(); } catch { }
            try { _quick?.Close(); } catch { }
        }

        /// <summary>Wo das Symbol im Infobereich steht (leer = nicht sichtbar, z. B. in der Ueberlauf-Ecke versteckt).</summary>
        private Rectangle TrayIconRect()
        {
            long t = Environment.TickCount;
            if (_trayRectAt != 0 && unchecked(t - _trayRectAt) < 2000) return _trayRect;
            _trayRectAt = t == 0 ? 1 : t;
            _trayRect = Rectangle.Empty;
            try
            {
                var ft = typeof(NotifyIcon);
                var win = ft.GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_tray) as NativeWindow;
                object idObj = ft.GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_tray);
                if (win == null || idObj == null) return _trayRect;
                var nid = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = win.Handle, uID = (uint)(int)idObj };
                if (Shell_NotifyIconGetRect(ref nid, out var r) == 0) _trayRect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch { }
            return _trayRect;
        }

        private IntPtr WheelHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt32() == 0x020A && (_settings.WinWheelOn || _settings.HotCornerOn))   // 4.0
                {
                    var i4 = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (WheelHook400(i4.pt.X, i4.pt.Y, (short)((i4.mouseData >> 16) & 0xffff))) return (IntPtr)1;
                }
                if (nCode >= 0 && wParam.ToInt32() == 0x020A && _settings.TrayWheelOn && _tray != null)
                {
                    var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    var r = TrayIconRect();
                    if (!r.IsEmpty && r.Contains(info.pt.X, info.pt.Y))
                    {
                        short delta = (short)((info.mouseData >> 16) & 0xffff);
                        string act = TargetAction(_settings.TrayWheelTarget, delta > 0 ? "up" : "down");
                        BeginInvoke(new Action(() => { try { ExecuteHotkey(act); } catch { } }));
                        return (IntPtr)1;   // nicht weiterreichen
                    }
                }
            }
            catch { }
            return CallNextHookEx(_wheelHook, nCode, wParam, lParam);
        }

        // ================================================================ 89 Schnell-Regler

        private QuickPopup _quick;

        /// <summary>Klick aufs Symbol im Infobereich: true = Schnell-Regler hat den Klick genommen.</summary>
        private bool TrayClick340()
        {
            if (!_settings.QuickPopupOn) return false;
            if (_quick != null && _quick.Visible) { _quick.Hide(); return true; }
            ShowQuickPopup(Cursor.Position);
            return true;
        }

        private void ShowQuickPopup(Point near)
        {
            if (_quick == null || _quick.IsDisposed) _quick = new QuickPopup(this);
            _quick.ShowNear(near);
        }

        /// <summary>Kleines Fenster mit allen Fadern am Symbol im Infobereich (wie der Windows-Lautstaerkeregler).</summary>
        private sealed class QuickPopup : Form
        {
            private readonly MixerForm _m;
            private readonly List<object> _items = new List<object>();   // ChannelConfig oder null (= MASTER)
            private int _drag = -1;
            private int _hover = -1;
            private string _sig;
            private readonly Dictionary<int, float> _shownLevel = new Dictionary<int, float>();

            private static int ColW => Theme.Px(58);
            private static int HeadH => Theme.Px(30);
            private static int FootH => Theme.Px(40);

            public QuickPopup(MixerForm m)
            {
                _m = m;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(0x17, 0x19, 0x1d);
                KeyPreview = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Deactivate += (s, e) => Hide();
            }

            protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) Hide(); }

            private void Rebuild()
            {
                _items.Clear();
                foreach (var c in _m._settings.Channels) if (_m.ChannelShown(c)) _items.Add(c);
                _items.Add(null);
                int w = Math.Max(Theme.Px(200), _items.Count * ColW + Theme.Px(16));
                Size = new Size(w, Theme.Px(310));
            }

            public void ShowNear(Point p)
            {
                Rebuild();
                var wa = Screen.FromPoint(p).WorkingArea;
                int x = Math.Max(wa.Left + 8, Math.Min(wa.Right - Width - 8, p.X - Width / 2));
                int y = p.Y > wa.Top + wa.Height / 2 ? wa.Bottom - Height - 8 : wa.Top + 8;
                Location = new Point(x, y);
                Show();
                Activate();
                Invalidate();
            }

            private Rectangle Col(int i) => new Rectangle(Theme.Px(8) + i * ColW, Theme.Px(8), ColW, ClientSize.Height - Theme.Px(16) - FootH);
            private Rectangle Track(int i) { var c = Col(i); return new Rectangle(c.X + c.Width / 2 - Theme.Px(4), c.Y + HeadH + Theme.Px(6), Theme.Px(8), c.Height - HeadH - Theme.Px(54)); }
            private Rectangle MuteRect(int i) { var c = Col(i); return new Rectangle(c.X + Theme.Px(6), c.Bottom - Theme.Px(26), c.Width - Theme.Px(12), Theme.Px(20)); }
            private Rectangle OpenRect => new Rectangle(Theme.Px(10), ClientSize.Height - FootH + Theme.Px(4), ClientSize.Width - Theme.Px(20), FootH - Theme.Px(12));

            private float PosOf(int i)
            {
                var o = _items[i];
                if (o is ChannelConfig c) return c.Fader;
                return _m.DefaultDevice?.Volume ?? 0f;
            }
            private bool MuteOf(int i) => _items[i] is ChannelConfig c ? c.Mute : _m.DefaultDevice?.Mute ?? false;
            private float LevelOf(int i)
            {
                if (_items[i] is ChannelConfig c) return _m._channelPeak.TryGetValue(c.Id, out var pk) && pk != null ? Math.Max(pk[0], pk[1]) : 0f;
                var d = _m.DefaultDevice;
                return d == null ? 0f : Math.Max(d.PeakL, d.PeakR);
            }

            public void Tick()
            {
                // neu zeichnen, wenn sich Pegel oder Stellungen sichtbar aendern
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _items.Count; i++)
                {
                    float lv = LevelOf(i);
                    _shownLevel.TryGetValue(i, out var old);
                    float sh = lv > old ? lv : old + (lv - old) * 0.25f;
                    _shownLevel[i] = sh;
                    sb.Append((int)(MeterBallistics.Iec(MeterBallistics.ToDb(sh)) * 30)).Append(',').Append((int)(PosOf(i) * 200)).Append(MuteOf(i) ? 'm' : '-');
                }
                string s = sb.ToString();
                if (s != _sig) { _sig = s; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                var all = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                using (var b = new LinearGradientBrush(new Rectangle(0, -1, ClientSize.Width, ClientSize.Height + 2), Color.FromArgb(0x22, 0x25, 0x2b), Color.FromArgb(0x14, 0x15, 0x18), LinearGradientMode.Vertical))
                    g.FillRectangle(b, all);
                using (var pen = new Pen(Theme.Alpha(Theme.Accent, 140))) g.DrawRectangle(pen, all);
                for (int i = 0; i < _items.Count; i++)
                {
                    var col = Col(i);
                    var c = _items[i] as ChannelConfig;
                    Color cc = c != null ? Color.FromArgb(255, Color.FromArgb(c.Color)) : Color.FromArgb(0xd8, 0xdc, 0xe1);
                    var plate = new RectangleF(col.X + Theme.F(3), col.Y, col.Width - Theme.F(6), HeadH - Theme.F(6));
                    Theme.FillRound(g, c != null ? Theme.Darken(cc, 0.1f) : Color.FromArgb(0x3a, 0x3d, 0x42), plate, Theme.F(3));
                    using (var b = new SolidBrush(c != null ? Theme.TextOn(cc) : Color.White))
                        Theme.Str(g, c?.Name ?? "MASTER", Theme.Fit(g, c?.Name ?? "MASTER", plate.Width - Theme.F(4), 8.5f, 6.5f, FontStyle.Bold), b, plate, Theme.Center);
                    var tr = Track(i);
                    Theme.FillRound(g, Color.FromArgb(0x08, 0x09, 0x0a), tr, Theme.F(3));
                    // Pegel neben dem Schlitz
                    _shownLevel.TryGetValue(i, out var lv);
                    float frac = MeterBallistics.Iec(MeterBallistics.ToDb(lv));
                    var mr = new RectangleF(tr.Right + Theme.F(6), tr.Y, Theme.F(4), tr.Height);
                    Theme.FillRound(g, Color.FromArgb(0x10, 0x11, 0x13), mr, Theme.F(1.5f));
                    if (frac > 0.01f)
                    {
                        var fill = new RectangleF(mr.X, mr.Bottom - mr.Height * frac, mr.Width, mr.Height * frac);
                        using (var b = new LinearGradientBrush(new RectangleF(mr.X, mr.Y - 1, mr.Width, mr.Height + 2), Theme.MeterRed, Theme.MeterGreen, LinearGradientMode.Vertical))
                            g.FillRectangle(b, fill);
                    }
                    // Kappe
                    float pos = PosOf(i);
                    float y = tr.Bottom - pos * tr.Height;
                    var cap = new RectangleF(tr.X + tr.Width / 2f - Theme.F(15), y - Theme.F(7), Theme.F(30), Theme.F(14));
                    Theme.DrawFaderCap(g, cap, c == null, _hover == i || _drag == i, cc);
                    if (c != null && Theme.TwoTone && c.Color2 != 0) Theme.DrawCapInsert2(g, cap, cc, Color.FromArgb(255, Color.FromArgb(c.Color2)));
                    else if (c != null) Theme.DrawCapInsert(g, cap, cc);
                    var md = c == null ? _m.DefaultDevice : null;
                    string val = c != null ? FaderLaw.FormatDb(c.Fader)
                               : OutputStrip.DbMode && md != null ? (OutputStrip.DbPos(md) <= 0f ? "-∞ dB" : OutputStrip.OutDbText(md.VolumeDb))   // 3.4.3
                               : (int)Math.Round(pos * 100) + " %";
                    using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
                        Theme.Str(g, val, Theme.Ui(7.5f), b, new RectangleF(col.X, tr.Bottom + Theme.F(8), col.Width, Theme.F(12)), Theme.Center);   // unter der Kappe, ueber MUTE
                    Theme.DrawKey(g, MuteRect(i), "MUTE", MuteOf(i), Theme.MuteRed, false);
                }
                Theme.DrawKey(g, OpenRect, "PULT ÖFFNEN", false, Theme.Accent, _hover == -2);
            }

            private int TrackAt(Point p)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var c = Col(i);
                    var tr = Track(i);
                    if (p.X >= c.X && p.X < c.Right && p.Y >= tr.Y - Theme.Px(10) && p.Y <= tr.Bottom + Theme.Px(10)) return i;
                }
                return -1;
            }

            private void SetPos(int i, float pos)
            {
                pos = Math.Max(0f, Math.Min(1f, pos));
                if (_items[i] is ChannelConfig c)
                {
                    if (c.FaderLock) return;
                    c.Fader = pos;
                    _m.ClampToCap(c);
                    _m.OnChannelEdited(c);
                }
                else
                {
                    var d = _m.DefaultDevice;
                    if (d == null) return;
                    d.Volume = pos;
                    _m.Backend.SetDeviceVolume(d.Id, pos);
                    _m._master.InvalidateChrome();
                }
                Invalidate();
            }

            private float PosAtY(int i, int y) { var tr = Track(i); return (tr.Bottom - y) / (float)Math.Max(1, tr.Height); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (OpenRect.Contains(e.Location)) { Hide(); _m.RestoreFromTray(); return; }
                for (int i = 0; i < _items.Count; i++)
                    if (MuteRect(i).Contains(e.Location))
                    {
                        if (_items[i] is ChannelConfig c) { c.Mute = !c.Mute; _m.OnChannelEdited(c); }
                        else { var d = _m.DefaultDevice; if (d != null) { d.Mute = !d.Mute; _m.Backend.SetDeviceMute(d.Id, d.Mute); _m._master.InvalidateChrome(); } }
                        Invalidate();
                        return;
                    }
                int t = TrackAt(e.Location);
                if (t >= 0) { _drag = t; Capture = true; SetPos(t, PosAtY(t, e.Y)); }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_drag >= 0) { SetPos(_drag, PosAtY(_drag, e.Y)); return; }
                int h = OpenRect.Contains(e.Location) ? -2 : TrackAt(e.Location);
                if (h != _hover) { _hover = h; Invalidate(); }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_drag >= 0) { _drag = -1; Capture = false; _m.ScheduleSave(); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int t = TrackAt(e.Location);
                if (t < 0) return;
                if (_items[t] is ChannelConfig c)
                {
                    float db = FaderLaw.PosToDb(c.Fader);
                    if (float.IsNegativeInfinity(db)) db = -62f;
                    db += e.Delta > 0 ? 1f : -1f;
                    SetPos(t, db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db)));
                }
                else SetPos(t, PosOf(t) + (e.Delta > 0 ? 0.02f : -0.02f));
                _m.ScheduleSave();
            }
        }

        // ================================================================ 90 Tastatur-Bedienung

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_settings != null && ChannelStrip.Renaming == null && HandleKeys400(keyData)) return true;   // 4.0: Strg+F
            if (_settings != null && ChannelStrip.Renaming == null && !(ActiveControl is TextBoxBase) && HandleKeys350(keyData)) return true;   // 3.5: F1, Zahlen, Esc
            if (_settings != null && (_settings.KeyNavOn || _settings.DigitKeysOn || _settings.A11yReaderOn) && ChannelStrip.Renaming == null && !(ActiveControl is TextBoxBase) && HandleKeyNav(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool HandleKeyNav(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var shown = _channelStrips.Where(s => s.Visible).ToList();
            if (shown.Count == 0) return false;
            var cur = StripBase.KeyFocus as ChannelStrip;
            int idx = cur == null ? -1 : shown.IndexOf(cur);
            switch (key)
            {
                case Keys.Left:
                case Keys.Right:
                    idx = idx < 0 ? 0 : Math.Max(0, Math.Min(shown.Count - 1, idx + (key == Keys.Right ? 1 : -1)));
                    StripBase.KeyFocus = shown[idx];
                    _channelHost.ScrollIntoView(shown[idx]);
                    return true;
                case Keys.Home: StripBase.KeyFocus = shown[0]; _channelHost.ScrollIntoView(shown[0]); return true;
                case Keys.End: StripBase.KeyFocus = shown[shown.Count - 1]; _channelHost.ScrollIntoView(shown[shown.Count - 1]); return true;
                case Keys.Escape:
                    if (cur == null) return false;
                    StripBase.KeyFocus = null;
                    return true;
            }
            if (cur == null) return false;
            var c = cur.Config;
            switch (key)
            {
                case Keys.Up:
                case Keys.Down:
                {
                    if (c.FaderLock) return true;
                    float step = (keyData & Keys.Shift) != 0 ? 0.5f : (keyData & Keys.Control) != 0 ? 3f : 1f;
                    float db = FaderLaw.PosToDb(c.Fader);
                    if (float.IsNegativeInfinity(db)) db = -61f;
                    db += key == Keys.Up ? step : -step;
                    c.Fader = db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db));
                    ClampToCap(c);
                    OnChannelEdited(c);
                    ScheduleSave();
                    Announce400(FaderLaw.FormatDb(c.Fader));   // 4.0: Bildschirmleser
                    return true;
                }
                case Keys.M:
                    if ((keyData & (Keys.Control | Keys.Alt)) != 0) return false;
                    _fades.Remove(c.Id);
                    c.Mute = !c.Mute;
                    OnChannelEdited(c);
                    ScheduleSave();
                    Announce400(c.Mute ? "stumm" : "laut");   // 4.0
                    return true;
                case Keys.S:
                    if ((keyData & (Keys.Control | Keys.Alt)) != 0) return false;
                    c.Solo = !c.Solo;
                    OnChannelChanged(c, false);
                    foreach (var s in _channelStrips) s.InvalidateChrome();
                    ScheduleSave();
                    Announce400(c.Solo ? "Solo an" : "Solo aus");   // 4.0
                    return true;
            }
            return false;
        }

        // ================================================================ 91/92 Kanal duplizieren, Klang zuruecksetzen

        private void DuplicateChannel(ChannelConfig c)
        {
            if (c == null) return;
            ChannelConfig copy;
            try
            {
                var ser = new DataContractSerializer(typeof(ChannelConfig));
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, c);
                    ms.Position = 0;
                    copy = (ChannelConfig)ser.ReadObject(ms);
                }
            }
            catch { return; }
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Apps = new List<string>();
            copy.IsCatchAll = false;
            copy.Solo = false;
            copy.Note = null;
            string baseName = c.Name.Length > 11 ? c.Name.Substring(0, 11) : c.Name;
            copy.Name = baseName + " 2";
            for (int n = 3; _settings.Channels.Any(x => x.Name == copy.Name) && n < 20; n++) copy.Name = baseName + " " + n;
            int idx = _settings.Channels.IndexOf(c);
            _settings.Channels.Insert(idx + 1, copy);
            BuildChannelStrips();
            PushPolicy();
            ScheduleSave();
            ShowOsd("Kanal dupliziert", copy.Name + " – ohne Apps", -1, Color.FromArgb(copy.Color));
        }

        private void ResetChannelSound(ChannelConfig c)
        {
            if (c == null) return;
            c.EqOn = false; c.EqLow = c.EqMid = c.EqHigh = 0f; c.Geq = null;
            c.CompOn = false; c.AutoOn = false;
            c.FxType = 0; c.FxMix = null;
            c.Pan = 0f;
            c.Stereo = 0; c.DelayMs = 0;
            c.EqAlt = null; c.EqIsB = false;
            c.Peq = null; c.PeqBypass = false;   // 4.0
            OnChannelEdited(c);
            ScheduleSave();
            ShowOsd(c.Name, "Klang zurückgesetzt – EQ, Effekte, Balance", -1, Color.FromArgb(c.Color));
        }

        private void AddChannel340Items(ToolStripItemCollection items, ChannelConfig c, ChannelStrip strip, Point screen)
        {
            // 3.4.6: Breite mit der Maus ziehen – direkt hier an/aus, gezogene Breite zuruecksetzen
            items.Add(new ToolStripMenuItem("Breite mit der Maus ziehen (rechte Kante)", null, (s, e) => SetDragWidthOn(!DragWidthEnabled)) { Checked = DragWidthEnabled });
            if (DragWidthEnabled && c.WidthPx > 0) items.Add(new ToolStripMenuItem("Gezogene Breite zurücksetzen", null, (s, e) => SetDragWidth(c, 0, false, true)));
            if (_snap.EqSupported)   // 3.4.1
                items.Add(new ToolStripMenuItem("Immer über den EQ-Weg (EQ/COMP ohne Aussetzer)", null, (s, e) => SetAlwaysDsp(c, !c.AlwaysDsp)) { Checked = c.AlwaysDsp });
            if (_settings.EqAbOn)
                items.Add(new ToolStripMenuItem("EQ B (umschalten zwischen A und B)", null, (s, e) => ToggleEqAb(c)) { Checked = c.EqIsB });
            if (_settings.TwoToneOn) items.Add(SecondColorSub(c));
            if (_settings.DuplicateOn && !c.IsCatchAll) items.Add(new ToolStripMenuItem("Kanal duplizieren", null, (s, e) => DuplicateChannel(c)));
            if (_settings.SoundResetOn) items.Add(new ToolStripMenuItem("Klang zurücksetzen (EQ, Effekte, Balance)", null, (s, e) => ResetChannelSound(c)));
            AddSticker340Items(items, strip, screen);
            AddChannel346Items(items, c);   // 3.4.6
            AddChannel350Menu(items, c);    // 3.5: kopieren/einfuegen, anpinnen, Vorlagen
            AddChannel400Menu(items, c, strip);   // 4.0
        }

        private ToolStripMenuItem SecondColorSub(ChannelConfig c)
        {
            var sub = Sub("Zweite Farbe", true, c.Color2 != 0 ? Swatch(Color.FromArgb(c.Color2)) : null);
            sub.DropDownItems.Add(new ToolStripMenuItem("Keine", null, (s, e) => SetColor2(c, 0)) { Checked = c.Color2 == 0 });
            foreach (int col in MixerSettings.Palette)
            {
                int k = col;
                sub.DropDownItems.Add(new ToolStripMenuItem("", Swatch(Color.FromArgb(col)), (s, e) => SetColor2(c, k)) { Checked = c.Color2 == col });
            }
            return sub;
        }

        private void SetColor2(ChannelConfig c, int col)
        {
            c.Color2 = col;
            if (col != 0 && !_settings.TwoToneOn) { _settings.TwoToneOn = true; Theme.TwoTone = true; }
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
            RestyleBridges();
            ScheduleSave();
        }

        // ================================================================ 102 Aufkleber

        private void AddSticker340Items(ToolStripItemCollection items, StripBase strip, Point screen)
        {
            if (!_settings.StickersOn || strip == null || strip.StickerOwner == null) return;
            var local = strip.PointToClient(screen);
            var hit = strip.StickerAt(local);
            var sub = Sub("Aufkleber hier", false);
            for (int i = 0; i < Theme.StickerNames.Length; i++)
            {
                int kind = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(Theme.StickerNames[i], StickerIcon(i), (s, e) => AddSticker(strip, local, kind)));
            }
            items.Add(sub);
            if (hit != null) items.Add(new ToolStripMenuItem("Diesen Aufkleber abziehen", null, (s, e) => RemoveSticker(strip, hit)));
            if (_settings.Stickers != null && _settings.Stickers.Any(k => k.Owner == strip.StickerOwner))
                items.Add(new ToolStripMenuItem("Alle Aufkleber hier abziehen", null, (s, e) =>
                {
                    _settings.Stickers.RemoveAll(k => k.Owner == strip.StickerOwner);
                    if (_settings.Stickers.Count == 0) _settings.Stickers = null;
                    StripBase.Stickers = _settings.Stickers;
                    strip.InvalidateChrome();
                    ScheduleSave();
                }));
        }

        private static Image StickerIcon(int kind)
        {
            int sz = Theme.Px(18);
            var bmp = new Bitmap(sz, sz);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Theme.DrawSticker(g, kind, new PointF(sz / 2f, sz / 2f), sz * 0.8f, 0f);
            }
            return bmp;
        }

        private static readonly Random StickerRnd = new Random();

        private void AddSticker(StripBase strip, Point local, int kind)
        {
            if (_settings.Stickers == null) _settings.Stickers = new List<StickerConfig>();
            if (_settings.Stickers.Count >= 40) _settings.Stickers.RemoveAt(0);
            _settings.Stickers.Add(new StickerConfig { Owner = strip.StickerOwner, Kind = kind, X = local.X / Theme.S, Y = local.Y / Theme.S, Angle = StickerRnd.Next(-18, 19) });
            StripBase.Stickers = _settings.Stickers;
            strip.InvalidateChrome();
            ScheduleSave();
        }

        private void RemoveSticker(StripBase strip, StickerConfig k)
        {
            _settings.Stickers?.Remove(k);
            if (_settings.Stickers != null && _settings.Stickers.Count == 0) _settings.Stickers = null;
            StripBase.Stickers = _settings.Stickers;
            strip.InvalidateChrome();
            ScheduleSave();
        }

        // ================================================================ 97 Studio-Boxen

        private SpeakerSide _spkL, _spkR;
        private int _spkQ = -1;
        private readonly float[] _spkBars = new float[8], _spkPk = new float[8];
        private float _spkLevel;

        private sealed class SpeakerSide : Control
        {
            private readonly bool _left;
            public float Level;
            public SpeakerSide(bool left)
            {
                _left = left;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = BodyColor;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                Theme.FillBody(g, ClientRectangle, Theme.FormOrigin(this), BodyColor);
                // Boxen stehen oben auf Hoehe des Pults, nicht ueber die ganze Hoehe
                int h = Math.Min(ClientSize.Height - Theme.Px(10), (int)(ClientSize.Width * 2.5f));
                var r = new Rectangle(0, Theme.Px(6), ClientSize.Width, h);
                Theme.DrawSpeaker(g, r, Level, _left);
                if (Theme.NightOn) Theme.DimOverlay(g, ClientRectangle, 0f);
            }
        }

        private void ApplySpeakers()
        {
            bool on = _settings.SpeakersOn;
            if (on && _spkL == null)
            {
                _spkL = new SpeakerSide(true) { Dock = DockStyle.Left, Width = Theme.Px(78) };
                _spkR = new SpeakerSide(false) { Dock = DockStyle.Right, Width = Theme.Px(78) };
                SuspendLayout();
                Controls.Add(_spkL);
                Controls.Add(_spkR);
                // ausserhalb der Seitenteile, unter Kopfleiste und Meterbridge-Rand, ueber der Armauflage
                int idx = Controls.GetChildIndex(_cheekR) + 1;
                Controls.SetChildIndex(_spkR, idx);
                Controls.SetChildIndex(_spkL, idx + 1);
                ResumeLayout(true);
            }
            if (_spkL != null) { _spkL.Visible = on; _spkR.Visible = on; }
        }

        private void TickSpeakers()
        {
            if (_spkL == null || !_spkL.Visible || !PultShown) return;
            float lv = 0f;
            var sp = _bridge.Spectrum;
            if (sp != null && sp.Active)
            {
                sp.Bars(_spkBars.Length, _spkBars, _spkPk);
                lv = Math.Max(_spkBars[0], _spkBars[1]);
            }
            else lv = _master.LevelNow;
            _spkLevel = lv > _spkLevel ? lv : _spkLevel + (lv - _spkLevel) * 0.35f;
            int q = (int)Math.Round(_spkLevel * 24);
            if (q == _spkQ) return;
            _spkQ = q;
            _spkL.Level = _spkR.Level = q / 24f;
            _spkL.Invalidate(); _spkR.Invalidate();
        }

        // ================================================================ 98 Licht nach Uhrzeit

        private int _sunKey = -1;

        private void TickSun()
        {
            if (!Theme.SunOn) { if (_sunKey != -1) { _sunKey = -1; } return; }
            Theme.SunState(Theme.SunHour, out var col, out float fx, out int a);
            int key = col.ToArgb() ^ (int)(fx * 7) ^ (a << 3);
            bool resized = Theme.SunWindowW != ClientSize.Width && ClientSize.Width > 100;
            if (resized) Theme.SunWindowW = ClientSize.Width;
            if (key == _sunKey && !resized) return;
            _sunKey = key;
            Theme.LampVersion++;
            foreach (var c in new Control[] { _channelHost, _outputHost, _divider, _addButton }) c?.Invalidate();
        }

        // ================================================================ 99 Staubschutzhaube

        private DustCoverForm _dust;
        private bool _demoDust;

        private sealed class DustCoverForm : Form
        {
            private readonly MixerForm _m;
            public DustCoverForm(MixerForm m)
            {
                _m = m;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Opacity = 0.0;
                BackColor = Color.FromArgb(0x30, 0x38, 0x44);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */; return cp; }
            }
            protected override void OnPaint(PaintEventArgs e) { Theme.Setup(e.Graphics); Theme.DrawDustCover(e.Graphics, ClientRectangle); }
            public Point ShownAt;
            // Windows/Mono schicken beim Erscheinen unter ruhender Maus ein "Maus bewegt" – das zaehlt nicht
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Cursor.Position != ShownAt) _m.LiftDustCover(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _m.LiftDustCover(); }
        }

        private void LiftDustCover()
        {
            _demoDust = false;
            OnUserActivity();
            if (_dust != null && _dust.Visible) _dust.Hide();
        }

        private void TickDustCover()
        {
            bool want = _settings.DustCoverOn && (_idle || _demoDust) && PultShown && !_powerOffDone && _powerOffStart < 0;
            if (want)
            {
                if (_dust == null || _dust.IsDisposed) _dust = new DustCoverForm(this);
                var r = RectangleToScreen(ClientRectangle);
                int top = _top.Bottom;
                var want0 = new Rectangle(r.X, r.Y + top, r.Width, r.Height - top);
                if (_dust.Bounds != want0) _dust.Bounds = want0;
                if (!_dust.Visible) { _dust.Opacity = 0.0; _dust.ShownAt = Cursor.Position; _dust.Show(this); }
                if (_dust.Opacity < 0.62) { try { _dust.Opacity = Math.Min(0.62, _dust.Opacity + 0.04); } catch { } }
            }
            else if (_dust != null && _dust.Visible) _dust.Hide();
        }

        // ================================================================ Hotkeys

        private void AddHotkeyActions340(List<HotkeyAction> list)
        {
            if (_settings.LevelMatchOn) list.Add(new HotkeyAction("levelmatch", "Lautheit angleichen (10 s zuhören)", "Allgemein"));
            if (_settings.QuickPopupOn) list.Add(new HotkeyAction("quick.toggle", "Schnell-Regler zeigen", "Allgemein"));
            if (_settings.EqAbOn)
                foreach (var c in _settings.Channels) list.Add(new HotkeyAction("eqab:" + c.Id, c.Name + ": EQ A/B umschalten", "Kanäle"));
        }

        private bool ExecuteHotkey340(string action)
        {
            switch (action)
            {
                case "levelmatch": StartLevelMatch(); return true;
                case "quick.toggle":
                    if (!_settings.QuickPopupOn) return true;
                    if (_quick != null && _quick.Visible) _quick.Hide(); else ShowQuickPopup(Cursor.Position);
                    return true;
                case "volkey.up": ExecuteHotkey(TargetAction(_settings.VolKeysTarget, "up")); return true;
                case "volkey.down": ExecuteHotkey(TargetAction(_settings.VolKeysTarget, "down")); return true;
                case "volkey.mute": ExecuteHotkey(TargetAction(_settings.VolKeysTarget, "mute")); return true;
            }
            if (action != null && action.StartsWith("eqab:", StringComparison.Ordinal))
            {
                ToggleEqAb(_settings.Channels.FirstOrDefault(c => c.Id == action.Substring(5)));
                return true;
            }
            return false;
        }

        // ================================================================ Einstellungen

        private void AddSound340(SPage p)
        {
            p.Section("Sanft und leise");
            p.Choice("Sanft einblenden", () => _settings.SoftStartMode == 0 ? "Aus (Standard): alles ist sofort da."
                    : _settings.SoftStartMode == 1 ? "Beim Start des Pults blenden alle Kanäle in " + _settings.SoftStartSecNow + " s ein, statt sofort da zu sein. Gilt beim nächsten Start."
                    : "Beim Start des Pults und jedes Mal, wenn eine App anfängt zu spielen, blendet sie in " + _settings.SoftStartSecNow + " s ein.",
                new[] { "Aus", "Beim Start", "Auch bei neuen Apps" }, () => _settings.SoftStartMode, i => SetSoftStart(i, _settings.SoftStartSec));
            p.Choice("Einblenden dauert", () => "So lange dauert das Einblenden.", MixerSettings.SoftStartSecValues.Select(v => v + " s").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.SoftStartSecValues, _settings.SoftStartSecNow)), i => SetSoftStart(_settings.SoftStartMode, MixerSettings.SoftStartSecValues[i]))
                .Enabled = () => _settings.SoftStartMode != 0;
            p.Toggle("Leise-Stunden", () => "Zwischen " + _settings.QuietFromNow + " und " + _settings.QuietToNow + " Uhr werden die Ausgänge (Windows-Lautstärke) nie lauter als " + _settings.QuietMaxNow + " % – auch nicht per Tastatur oder Fader.",
                () => _settings.QuietHoursOn, v => { _settings.QuietHoursOn = v; _quietNotified = false; ScheduleSave(); });
            var hours = Enumerable.Range(0, 24).Select(h => h + " Uhr").ToArray();
            p.Choice("Leise ab", () => "Ab dieser Uhrzeit …", hours, () => _settings.QuietFromNow, i => { int to = _settings.QuietToNow; _settings.QuietFrom = i; _settings.QuietTo = to; ScheduleSave(); })
                .Enabled = () => _settings.QuietHoursOn;
            p.Choice("Leise bis", () => "… bis zu dieser Uhrzeit.", hours, () => _settings.QuietToNow, i => { int from = _settings.QuietFromNow; _settings.QuietTo = i; _settings.QuietFrom = from; ScheduleSave(); })
                .Enabled = () => _settings.QuietHoursOn;
            p.Choice("Höchstens", () => "So laut dürfen die Ausgänge in dieser Zeit höchstens sein.", MixerSettings.QuietMaxValues.Select(v => v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.QuietMaxValues, _settings.QuietMaxNow)), i => { _settings.QuietMax = MixerSettings.QuietMaxValues[i]; ScheduleSave(); })
                .Enabled = () => _settings.QuietHoursOn;
            p.Toggle("Kopfhörer abgezogen = Pause", "Verschwindet das Standard-Gerät (Kopfhörer abgezogen, Bluetooth-Kopfhörer aus), pausiert die Musik – damit sie nicht plötzlich aus den Lautsprechern kommt. Über die Windows-Mediensteuerung.",
                () => _settings.PhonesPauseOn, v => { _settings.PhonesPauseOn = v; ScheduleSave(); });
            p.Toggle("Beim Einstecken weiter", "Kommt dasselbe Gerät innerhalb von 10 Minuten wieder, läuft die Musik weiter.",
                () => _settings.PhonesResumeOn, v => { _settings.PhonesResumeOn = v; ScheduleSave(); }).Enabled = () => _settings.PhonesPauseOn;
            p.Section("Angleichen und Vergleichen");
            p.Toggle("Lautheit angleichen", "Ein Knopf: das Pult hört 10 Sekunden zu und stellt die Fader so, dass alle spielenden Kanäle gleich laut sind (nur einmal, kein Dauer-Regler; nie über 0 dB).",
                () => _settings.LevelMatchOn, v => { _settings.LevelMatchOn = v; ScheduleSave(); });
            p.Button("Jetzt angleichen", "Lass dabei alles normal laufen (Musik, Spiel, Chat …).", "Angleichen", r => StartLevelMatch(), () => LevelMatchRunning ? "hört zu …" : null)
                .Visible = () => _settings.LevelMatchOn;
            p.Toggle("EQ A/B", "Zwei EQ-Einstellungen pro Kanal: per Rechtsklick auf den Kanal (oder Tastenkürzel) zwischen A und B umschalten und vergleichen. Auf der EQ-Taste steht dann „EQ B“.",
                () => _settings.EqAbOn, v => { _settings.EqAbOn = v; ChannelStrip.EqAbOn = v; RefreshStrips(); ApplyHotkeys(); });
            p.Toggle("EQ malen", "Im großen EQ mit gedrückter Maus über die Bänder oder die Kurve ziehen – jedes Band springt auf die Höhe der Maus.",
                () => _settings.EqPaintOn, v => { _settings.EqPaintOn = v; EqWindow.PaintOn = v; ScheduleSave(); });
            p.Toggle("MONO-Taste am MASTER", "Neben GEHÖRSCHUTZ eine Taste MONO: kurz in Mono gegenhören. Läuft dafür wie der Ausgangs-Klang durchs Mischpult.",
                () => _settings.MonoKeyOn, v => { _settings.MonoKeyOn = v; OutputStrip.MonoKeyOn = v; RelayoutAllStrips(); RefreshStrips(); });
        }

        private void AddControl340(SPage p)
        {
            p.Section("Mehr Bedienung");
            p.Toggle("Lautstärketasten der Tastatur", () => "Die Tasten Lauter/Leiser/Stumm der Tastatur steuern " + TargetName(_settings.VolKeysTarget) + " statt der Windows-Gesamtlautstärke.",
                () => _settings.VolKeysOn, v => { _settings.VolKeysOn = v; ApplyHotkeys(); ScheduleSave(); });
            p.Button("Tasten steuern", null, "Wählen ▾", r => ShowTargetMenu(r, () => _settings.VolKeysTarget, id => { _settings.VolKeysTarget = id; ScheduleSave(); }),
                () => TargetName(_settings.VolKeysTarget)).Enabled = () => _settings.VolKeysOn;
            p.Toggle("Mausrad über dem Symbol", () => "Mausrad über dem Mischpult-Symbol unten rechts (Infobereich) ändert " + TargetName(_settings.TrayWheelTarget) + ".",
                () => _settings.TrayWheelOn, v => { _settings.TrayWheelOn = v; ApplyTrayWheel(); ScheduleSave(); });
            p.Button("Mausrad steuert", null, "Wählen ▾", r => ShowTargetMenu(r, () => _settings.TrayWheelTarget, id => { _settings.TrayWheelTarget = id; ScheduleSave(); }),
                () => TargetName(_settings.TrayWheelTarget)).Enabled = () => _settings.TrayWheelOn;
            p.Toggle("Schnell-Regler am Symbol", "Klick auf das Symbol unten rechts öffnet ein kleines Fenster mit allen Fadern und MUTE (wie der Windows-Lautstärkeregler). „Pult öffnen“ zeigt das ganze Pult.",
                () => _settings.QuickPopupOn, v => { _settings.QuickPopupOn = v; if (!v) _quick?.Hide(); ApplyHotkeys(); ScheduleSave(); });
            p.Toggle("Tastatur-Bedienung im Pult", "Im Pult: ←/→ wählt den Kanal, ↑/↓ Fader (Shift fein, Strg grob), M stumm, S Solo, Esc hebt die Auswahl auf.",
                () => _settings.KeyNavOn, v => { _settings.KeyNavOn = v; if (!v) StripBase.KeyFocus = null; ScheduleSave(); });
        }

        private void AddChannels340(SPage p)
        {
            p.Toggle("Kanal duplizieren", "Rechtsklick auf den Kanal → Kanal duplizieren: ein neuer Kanal mit demselben EQ, Effekten und Farbe – nur ohne Apps.",
                () => _settings.DuplicateOn, v => { _settings.DuplicateOn = v; ScheduleSave(); });
            p.Toggle("Klang zurücksetzen", "Rechtsklick auf den Kanal → Klang zurücksetzen: EQ, Effekte, Balance, Mono und Verzögerung auf Standard. Fader, Name, Farbe und Apps bleiben.",
                () => _settings.SoundResetOn, v => { _settings.SoundResetOn = v; ScheduleSave(); });
            p.Toggle("Zweifarbige Kanäle", "Eine zweite Kanalfarbe als Verlauf im Namensschild und auf der Faderkappe (wenn die Kanalfarbe auf den Kappen an ist). Farbe wählen: Rechtsklick auf den Kanal → Zweite Farbe.",
                () => _settings.TwoToneOn, v => { _settings.TwoToneOn = v; Theme.TwoTone = v; RefreshStrips(); RestyleBridges(); });
        }

        private void AddBridge340(SPage p)
        {
            p.Toggle("PC-Auslastung in der Meterbridge", "Zwei kleine Zeigerinstrumente für Prozessor und Grafikkarte – praktisch beim Zocken. Steht in der freien Lücke der Meterbridge, wenn nach Uhr, Stereo-Bild, Lautheit, Oszilloskop und Cover noch Platz ist. Gemessen wird einmal pro Sekunde.",
                () => _settings.PcMeterOn, v => { _settings.PcMeterOn = v; MeterBridge.PcOn = v; if (v) PcLoad.Start(); else PcLoad.Stop(); MeterBridge.ConfigVersion++; RestyleBridges(); ScheduleSave(); })
                .Enabled = () => !_settings.BridgeOff;
            p.Toggle("Lautheits-Verlauf als Papierschreiber", "Der Lautheits-Verlauf läuft auf einem Papierstreifen mit Raster, ein Stift schreibt rechts mit.",
                () => _settings.PaperLookOn, v => { _settings.PaperLookOn = v; LoudHistView.PaperLook = v; RestyleBridges(); ScheduleSave(); })
                .Enabled = () => !_settings.BridgeOff;
        }

        private void AddLook340(SPage p)
        {
            p.Section("Rund ums Pult");
            p.Toggle("Studio-Boxen links und rechts", "Zwei Studio-Monitore neben dem Pult – die Membran schwingt mit dem Bass.",
                () => _settings.SpeakersOn, v => { _settings.SpeakersOn = v; ApplySpeakers(); ScheduleSave(); });
            p.Toggle("Licht nach Uhrzeit", "Morgens fällt warmes Licht von links aufs Pult, mittags von oben, abends orange von rechts, nachts kühles Mondlicht.",
                () => _settings.SunLightOn, v => { _settings.SunLightOn = v; Theme.SunOn = v; _sunKey = -1; Theme.LampVersion++; TickSun(); foreach (var c in new Control[] { _channelHost, _outputHost, _divider }) c?.Invalidate(); ScheduleSave(); });
            p.Toggle("Staubschutzhaube im Ruhemodus", "Im Ruhemodus legt sich eine durchsichtige Haube übers Pult – Maus bewegen, und sie geht wieder auf. Braucht den Ruhemodus (Aussehen → Licht).",
                () => _settings.DustCoverOn, v => { _settings.DustCoverOn = v; ScheduleSave(); });
            p.Toggle("Eigenes Typenschild", () => "Graviertes Metallschild oben rechts über den Kanälen: „" + (_settings.NamePlateText ?? "MEIN STUDIO") + "“.",
                () => _settings.NamePlateOn, v => { _settings.NamePlateOn = v; StripHost.TypePlateText = v ? (_settings.NamePlateText ?? "MEIN STUDIO") : null; _channelHost.Invalidate(false); ScheduleSave(); });
            p.Button("Text auf dem Schild", null, "Ändern …", r =>
            {
                using (var dlg = new InputDialog("Typenschild", "Text auf dem Schild (z. B. „Jans Studio“):", _settings.NamePlateText ?? "", 28))
                {
                    if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                    _settings.NamePlateText = string.IsNullOrWhiteSpace(dlg.Value) ? null : dlg.Value.Trim();
                    StripHost.TypePlateText = _settings.NamePlateOn ? (_settings.NamePlateText ?? "MEIN STUDIO") : null;
                    _channelHost.Invalidate(false);
                    ScheduleSave();
                }
            }, () => _settings.NamePlateText ?? "MEIN STUDIO").Enabled = () => _settings.NamePlateOn;
            p.Toggle("Aufkleber", "Sticker aufs Pult kleben: Rechtsklick auf einen Kanal oder Ausgang → Aufkleber hier. Abziehen genauso.",
                () => _settings.StickersOn, v => { _settings.StickersOn = v; StripBase.StickersOn = v; RefreshStrips(); });
        }
    }
}
