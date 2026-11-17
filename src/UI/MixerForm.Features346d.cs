using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4.6 – Bedienung: Fader rasten ein, Mausrad-Schritte, Szenen-Tasten, Schublade am Bildschirmrand, Taskleiste
    /// (Knoepfe, Lautstaerke-Balken, Sprungliste), Lautstaerke im Infobereich-Symbol, Song-Einblendung, Takt-Lampe,
    /// portabler Modus. Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyControlStatics346(MixerSettings s, bool demo)
        {
            if (demo)
            {
                if (DemoFlag(demo, "SNAP")) s.SnapOn = true;
                s.WheelStep = DemoNum(demo, "WHEEL", s.WheelStep);
                if (DemoFlag(demo, "SCENEKEYS")) s.SceneKeysOn = true;
                if (DemoFlag(demo, "BPM")) s.BpmOn = true;
                s.TrayIconMode = DemoNum(demo, "TRAYICON", s.TrayIconMode);
                if (DemoFlag(demo, "SONGTOAST")) s.SongToastOn = true;
                if (DemoFlag(demo, "DRAWER")) s.DrawerOn = true;
                s.DrawerEdge = DemoNum(demo, "DRAWEREDGE", s.DrawerEdge);
                if (DemoFlag(demo, "SCENES4") && s.Scenes != null && s.Scenes.Count == 2)
                {
                    s.Scenes.Add(new Scene { Id = "demo-film", Name = "Film", Channels = s.Scenes[1].Channels, MasterVolume = -1f });
                    s.Scenes.Add(new Scene { Id = "demo-nacht", Name = "Nacht", Channels = s.Scenes[0].Channels, MasterVolume = -1f });
                }
            }
            if (s.WheelStep < 0 || s.WheelStep >= MixerSettings.WheelStepDb.Length) s.WheelStep = 0;
            StripBase.SnapOn = s.SnapOn;
            StripBase.WheelDb = MixerSettings.WheelStepDb[s.WheelStep];
            StripBase.WheelPct = MixerSettings.WheelStepPct[s.WheelStep];
            BeatTracker.On = s.BpmOn;
        }

        /// <summary>Einmal nach dem Anzeigen (ShownFeatures340).</summary>
        private void HookControl346()
        {
            _top.SceneKeyClicked += LoadSceneKey346;
            ApplyControlUi346();
            if (_settings.DrawerOn) StartDrawer346(true);
            TaskbarApply346();
            if (Program.StartCommand != null)
            {
                string c = Program.StartCommand;
                Program.StartCommand = null;
                var t = new Timer { Interval = 1500 };   // erst wenn Geraete und Apps da sind
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); RunCommand346(c); };
                t.Start();
            }
            if (_demo && DemoFlag(true, "SONGTOAST"))
                DemoLater(() => ShowSongToast346(new NowPlayingInfo { Title = "Midnight Drive", Artist = "Neon Coast", App = "Spotify", Playing = true }));
        }

        private void ApplyControlUi346()
        {
            if (_top == null) return;
            _top.BeatShown = _settings.BpmOn;
            if (!_settings.BpmOn) { _bpm346 = 0; _beatT0 = -1; }
            UpdateSceneKeys346(true);
            _top.Invalidate();
        }

        private void SetControl346(Action change)
        {
            change();
            ApplyControlStatics346(_settings, false);
            ApplyControlUi346();
            ScheduleSave();
        }

        private void TickControl346(double now, float dt)
        {
            if (now >= _nextControlSlow346)
            {
                _nextControlSlow346 = now + 0.5;
                UpdateSceneKeys346(false);
                TaskbarApply346();
                TickJumpList346(now);
            }
            TickBeat346(now);
            TickSongToast346(now);
            TickTrayIcon346(now);
        }

        private double _nextControlSlow346;

        // ================================================================ 124 Szenen-Tasten

        private string _sceneLit346, _sceneKeySig346;

        private void NoteSceneApplied346(Scene sc)
        {
            NoteScene350(sc);   // 3.5: Ueberblendzeit, Lauflicht, Vorschau aus
            _sceneLit346 = sc?.Id;
            if (_top != null) UpdateSceneKeys346(true);
        }

        private void UpdateSceneKeys346(bool force)
        {
            if (_top == null) return;
            string[] names = null;
            int lit = -1;
            if (_settings.SceneKeysOn && _settings.Scenes != null && _settings.Scenes.Count > 0)
            {
                int n = Math.Min(4, _settings.Scenes.Count);
                names = new string[n];
                for (int i = 0; i < n; i++)
                {
                    names[i] = _settings.Scenes[i].Name ?? "";
                    if (_settings.Scenes[i].Id == _sceneLit346) lit = i;
                }
            }
            string sig = names == null ? "" : string.Join("\u0001", names) + "|" + lit;
            if (!force && sig == _sceneKeySig346) return;
            _sceneKeySig346 = sig;
            _top.SceneKeys = names;
            _top.SceneKeyLit = lit;
            _top.Invalidate();
        }

        private void LoadSceneKey346(int i)
        {
            if (_settings.Scenes == null || i < 0 || i >= _settings.Scenes.Count) return;
            ApplyScene(_settings.Scenes[i], null);
        }

        // ================================================================ 129 Takt-Lampe

        private float _bpm346;
        private double _beatT0 = -1, _beatPeriod;

        private void TickBeat346(double now)
        {
            if (!_settings.BpmOn || _top == null) return;
            var b = _snap?.Beat;
            if (b != null && b.Length >= 2 && b[0] > 0)
            {
                double period = 60.0 / b[0];
                double t0 = now - b[1] / 1000.0;
                if (_beatT0 < 0 || Math.Abs(period - _beatPeriod) > 0.02 * period) _beatT0 = t0;
                else
                {
                    // die Phase nur sanft nachfuehren, sonst flackert die Lampe bei jeder neuen Schaetzung
                    double pred = _beatT0 + Math.Round((t0 - _beatT0) / period) * period;
                    _beatT0 = pred + (t0 - pred) * 0.15;
                }
                _beatPeriod = period;
                _bpm346 = b[0];
            }
            else { _bpm346 = 0; _beatT0 = -1; }
            _top.SetBeat(_bpm346, BeatGlow(now, _beatT0, _beatPeriod, _bpm346));
        }

        /// <summary>Helligkeit der Takt-LED: blitzt auf jedem Schlag auf und klingt in etwa 0,2 s ab.</summary>
        internal static float BeatGlow(double now, double t0, double period, float bpm)
        {
            if (bpm <= 0 || t0 < 0 || period <= 0) return 0f;
            double ph = ((now - t0) % period + period) % period;
            return (float)Math.Exp(-ph / 0.09);
        }

        // ================================================================ 128 Song-Einblendung

        private SongToast _songToast;
        private string _toastKey346;

        private void TickSongToast346(double now)
        {
            if (!_settings.SongToastOn) { _toastKey346 = null; return; }
            var np = _nowPlaying.Current;
            if (np == null || !np.Playing || string.IsNullOrEmpty(np.Title)) return;
            string key = (np.Artist ?? "") + "\u0001" + np.Title;
            if (key == _toastKey346) return;
            bool first = _toastKey346 == null;
            _toastKey346 = key;
            if (first && now < 10) return;   // gleich nach dem Start nicht
            if (PultInFront346) return;      // Pult vorne: steht ja oben in der Laufschrift
            ShowSongToast346(np);
        }

        private bool PultInFront346
        {
            get
            {
                if (!Visible || WindowState == FormWindowState.Minimized) return false;
                if (!Win32.IsWindows) return ContainsFocus;
                try { return Win32.GetForegroundWindow() == Handle; } catch { return false; }
            }
        }

        private void ShowSongToast346(NowPlayingInfo np)
        {
            try
            {
                if (_songToast == null || _songToast.IsDisposed) _songToast = new SongToast();
                _songToast.ShowSong(np, _settings.SongToastPos);
            }
            catch (Exception ex) { if (_demo) Console.Error.WriteLine("SongToast: " + ex); }
        }

        // ================================================================ 127 Lautstaerke im Infobereich-Symbol

        private Icon _trayIconOrig, _trayIconCur;
        private IntPtr _trayIconHandle;
        private string _trayIconSig;
        private double _nextTrayIcon346;

        private void TickTrayIcon346(double now)
        {
            if (_tray == null) return;
            int mode = _settings.TrayIconMode;
            if (mode == 0) { RestoreTrayIcon346(); return; }
            if (now < _nextTrayIcon346) return;
            _nextTrayIcon346 = now + (mode == 2 ? 0.1 : 0.3);
            var d = DefaultDevice;
            float vol = d?.Volume ?? 0f, l = 0f, r = 0f;
            bool mute = d?.Mute ?? false;
            if (mode == 2 && d != null) { l = d.PeakL; r = d.PeakR; }
            int ql = (int)Math.Round(TrayLevel(l) * 8), qr = (int)Math.Round(TrayLevel(r) * 8);
            string sig = mode + "|" + (int)Math.Round(vol * 100) + "|" + mute + "|" + (mode == 2 ? ql + "," + qr : "");
            if (sig == _trayIconSig) return;
            _trayIconSig = sig;
            if (!Win32.IsWindows) return;   // nur Windows hat das Symbol wirklich
            try
            {
                int px = Math.Max(16, SystemInformation.SmallIconSize.Width);
                using (var bmp = DrawTrayIcon(px, mode, vol, mute, ql / 8f, qr / 8f))
                {
                    IntPtr h = bmp.GetHicon();
                    var ic = Icon.FromHandle(h);
                    if (_trayIconOrig == null) _trayIconOrig = _tray.Icon;
                    _tray.Icon = ic;
                    var oldH = _trayIconHandle;
                    var oldIc = _trayIconCur;
                    _trayIconCur = ic;
                    _trayIconHandle = h;
                    oldIc?.Dispose();
                    if (oldH != IntPtr.Zero) Taskbar.DestroyIcon(oldH);
                }
            }
            catch { }
        }

        private void RestoreTrayIcon346()
        {
            if (_trayIconCur == null) return;
            try
            {
                _tray.Icon = _trayIconOrig ?? Icon ?? SystemIcons.Application;
                _trayIconCur.Dispose();
                if (_trayIconHandle != IntPtr.Zero && Win32.IsWindows) Taskbar.DestroyIcon(_trayIconHandle);
            }
            catch { }
            _trayIconCur = null;
            _trayIconHandle = IntPtr.Zero;
            _trayIconSig = null;
        }

        /// <summary>Pegel (linear) als Anteil 0..1 einer Skala von −48 bis 0 dB.</summary>
        internal static float TrayLevel(float peak)
        {
            if (peak <= 0.0001f) return 0f;
            float db = 20f * (float)Math.Log10(peak);
            return Math.Max(0f, Math.Min(1f, (db + 48f) / 48f));
        }

        /// <summary>
        /// Symbol fuer den Infobereich: Lautstaerke (Fuellstand und Zahl) oder Pegel (zwei Balken). Stumm = rot mit Kreuz.
        /// </summary>
        internal static Bitmap DrawTrayIcon(int px, int mode, float vol, bool mute, float l, float r)
        {
            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float s = px / 16f;
                var box = new RectangleF(0.5f * s, 0.5f * s, px - 1f * s, px - 1f * s);
                using (var path = Theme.Round(box, 3f * s))
                {
                    using (var b = new SolidBrush(mute ? Color.FromArgb(0xb0, 0x22, 0x1a) : Color.FromArgb(0x1d, 0x20, 0x25))) g.FillPath(b, path);
                    if (!mute)
                    {
                        var st = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        if (mode == 1)
                        {
                            float h = box.Height * Math.Max(0f, Math.Min(1f, vol));
                            using (var b = new SolidBrush(Color.FromArgb(0x2f, 0x8f, 0xff))) g.FillRectangle(b, box.X, box.Bottom - h, box.Width, h);
                        }
                        else
                        {
                            float bw = box.Width * 0.32f, gap = box.Width * 0.12f, x0 = box.X + (box.Width - 2 * bw - gap) / 2f;
                            DrawTrayBar(g, new RectangleF(x0, box.Y + 1.5f * s, bw, box.Height - 3f * s), l);
                            DrawTrayBar(g, new RectangleF(x0 + bw + gap, box.Y + 1.5f * s, bw, box.Height - 3f * s), r);
                        }
                        g.Restore(st);
                    }
                }
                if (mute)
                {
                    using (var p = new Pen(Color.White, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(p, 4.5f * s, 4.5f * s, px - 4.5f * s, px - 4.5f * s);
                        g.DrawLine(p, px - 4.5f * s, 4.5f * s, 4.5f * s, px - 4.5f * s);
                    }
                }
                else if (mode == 1)
                {
                    int v = (int)Math.Round(vol * 100);
                    string t = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    float fs = (v >= 100 ? 6.2f : 8.4f) * s;
                    using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(t, f.FontFamily, (int)f.Style, fs, new RectangleF(0, 0.5f * s, px, px), sf);
                        using (var pen = new Pen(Color.FromArgb(200, 0, 0, 0), 2.2f * s) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                        g.FillPath(Brushes.White, path);
                    }
                }
            }
            return bmp;
        }

        private static void DrawTrayBar(Graphics g, RectangleF r, float v)
        {
            using (var b = new SolidBrush(Color.FromArgb(0x0b, 0x0c, 0x0e))) g.FillRectangle(b, r);
            float h = r.Height * Math.Max(0f, Math.Min(1f, v));
            if (h <= 0.5f) return;
            var col = v > 0.92f ? Color.FromArgb(0xff, 0x45, 0x3a) : v > 0.75f ? Color.FromArgb(0xff, 0xc4, 0x2e) : Color.FromArgb(0x3c, 0xd2, 0x5a);
            using (var b = new SolidBrush(col)) g.FillRectangle(b, r.X, r.Bottom - h, r.Width, h);
        }

        // ================================================================ 126 Taskleiste

        private static readonly int TbCreatedMsg = Win32.IsWindows ? SafeRegister("TaskbarButtonCreated") : 0;
        private static int SafeRegister(string name) { try { return Win32.RegisterWindowMessage(name); } catch { return 0; } }
        private bool _tbButtonsAdded;
        private string _tbSig, _tbProgSig;
        private IntPtr[] _tbIcons;

        private bool WndProc346(ref Message m)
        {
            if (TbCreatedMsg != 0 && m.Msg == TbCreatedMsg)
            {
                // Taskleisten-Knopf (neu) angelegt – z. B. nach dem Verstecken im Infobereich: Knoepfe neu setzen
                _tbButtonsAdded = false; _tbSig = null; _tbProgSig = null;
                BeginInvoke(new Action(TaskbarApply346));
                return false;
            }
            if (m.Msg == Taskbar.WM_COMMAND && ((m.WParam.ToInt64() >> 16) & 0xFFFF) == Taskbar.THBN_CLICKED)
            {
                ThumbClicked346((int)(m.WParam.ToInt64() & 0xFFFF));
                return true;
            }
            if (Program.CommandMessage != 0 && m.Msg == Program.CommandMessage)
            {
                string c = null;
                try
                {
                    if (File.Exists(Program.CommandFile)) { c = File.ReadAllText(Program.CommandFile).Trim(); File.Delete(Program.CommandFile); }
                }
                catch { }
                if (!string.IsNullOrEmpty(c)) BeginInvoke(new Action(() => RunCommand346(c)));
                return true;
            }
            return false;
        }

        /// <summary>Nur diese Befehle duerfen von aussen (Sprungliste) kommen.</summary>
        internal static bool CommandAllowed(string c) =>
            c != null && (c.StartsWith("open:") ? FileAssoc420.Allowed(c.Substring(5)) :   // 4.2: Doppelklick auf eine Mischpult-Datei
            c.Length < 200 && (c.StartsWith("scene:") || c == "media.play" || c == "media.next" || c == "media.prev" ||
                                            c == "master.mute" || c == "mini.toggle" || c == "night.toggle" || c == "show"));

        private void RunCommand346(string c)
        {
            if (!CommandAllowed(c)) return;
            try
            {
                if (c == "show") { RestoreFromTray(); return; }
                if (c.StartsWith("open:")) { OpenFile420(c.Substring(5)); return; }   // 4.2
                ExecuteHotkey(c);
            }
            catch { }
        }

        private void ThumbClicked346(int id)
        {
            switch (id)
            {
                case 1: SendMedia("prev", false); break;
                case 2: SendMedia("play", false); break;
                case 3: SendMedia("next", false); break;
                case 4: ExecuteHotkey("master.mute"); break;
            }
            TaskbarApply346();
        }

        private void TaskbarApply346()
        {
            if (!Win32.IsWindows || _demo || !IsHandleCreated) return;
            try
            {
                var tl = Taskbar.List;
                if (tl == null) return;
                var d = DefaultDevice;
                bool mute = d?.Mute ?? false;
                bool want = _settings.TaskbarButtonsOn;
                string sig = want + "|" + mute;
                if (sig != _tbSig && (want || _tbButtonsAdded))
                {
                    var btns = ThumbButtons346(want, mute);
                    int hr = _tbButtonsAdded ? tl.ThumbBarUpdateButtons(Handle, (uint)btns.Length, btns) : tl.ThumbBarAddButtons(Handle, (uint)btns.Length, btns);
                    if (hr >= 0) { _tbButtonsAdded = true; _tbSig = sig; }
                }
                string prog = _settings.TaskbarVolumeOn && d != null ? (mute ? "m" : "") + (int)Math.Round(d.Volume * 100) : "";
                if (prog != _tbProgSig && (prog != "" || _tbProgSig != null))
                {
                    if (prog == "") tl.SetProgressState(Handle, Taskbar.TBPF_NOPROGRESS);
                    else
                    {
                        tl.SetProgressState(Handle, mute ? Taskbar.TBPF_ERROR : Taskbar.TBPF_NORMAL);
                        tl.SetProgressValue(Handle, (ulong)Math.Max(1, Math.Round(d.Volume * 100)), 100);   // 0 % wuerde ganz verschwinden
                    }
                    _tbProgSig = prog;
                }
            }
            catch { }
        }

        private Taskbar.THUMBBUTTON[] ThumbButtons346(bool show, bool mute)
        {
            if (_tbIcons == null)
            {
                _tbIcons = new IntPtr[5];
                int px = Math.Max(16, SystemInformation.SmallIconSize.Width);
                string[] kinds = { "prev", "play", "next", "vol", "volmute" };
                for (int i = 0; i < kinds.Length; i++)
                    using (var bmp = DrawThumbIcon(px, kinds[i])) _tbIcons[i] = bmp.GetHicon();
            }
            uint flags = show ? Taskbar.THBF_ENABLED : Taskbar.THBF_HIDDEN;
            Taskbar.THUMBBUTTON B(uint id, int icon, string tip) => new Taskbar.THUMBBUTTON
            {
                dwMask = Taskbar.THB_ICON | Taskbar.THB_TOOLTIP | Taskbar.THB_FLAGS,
                iId = id, hIcon = _tbIcons[icon], szTip = tip, dwFlags = flags
            };
            return new[]
            {
                B(1, 0, "Vorheriger Titel"),
                B(2, 1, "Play / Pause"),
                B(3, 2, "Nächster Titel"),
                B(4, mute ? 4 : 3, mute ? "Ausgang ist stumm – Ton an" : "Ausgang stumm"),
            };
        }

        /// <summary>Weisses Zeichen fuer die Taskleisten-Vorschau (prev, play, next, vol, volmute).</summary>
        internal static Bitmap DrawThumbIcon(int px, string kind)
        {
            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = px / 16f;
                PointF P(float x, float y) => new PointF(x * s, y * s);
                switch (kind)
                {
                    case "play":
                        g.FillPolygon(b, new[] { P(2.5f, 3), P(8.5f, 8), P(2.5f, 13) });
                        g.FillRectangle(b, 10 * s, 3.5f * s, 1.8f * s, 9 * s);
                        g.FillRectangle(b, 13 * s, 3.5f * s, 1.8f * s, 9 * s);
                        break;
                    case "next":
                        g.FillPolygon(b, new[] { P(2.5f, 3.5f), P(8, 8), P(2.5f, 12.5f) });
                        g.FillPolygon(b, new[] { P(7.5f, 3.5f), P(13, 8), P(7.5f, 12.5f) });
                        g.FillRectangle(b, 13 * s, 3.5f * s, 1.6f * s, 9 * s);
                        break;
                    case "prev":
                        g.FillRectangle(b, 1.4f * s, 3.5f * s, 1.6f * s, 9 * s);
                        g.FillPolygon(b, new[] { P(8.5f, 3.5f), P(3, 8), P(8.5f, 12.5f) });
                        g.FillPolygon(b, new[] { P(13.5f, 3.5f), P(8, 8), P(13.5f, 12.5f) });
                        break;
                    default:   // Lautsprecher
                        g.FillPolygon(b, new[] { P(1.5f, 5.5f), P(4.5f, 5.5f), P(8.5f, 2), P(8.5f, 14), P(4.5f, 10.5f), P(1.5f, 10.5f) });
                        using (var p = new Pen(Color.White, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            if (kind == "volmute")
                            {
                                g.DrawLine(p, 10.5f * s, 5.5f * s, 15 * s, 10.5f * s);
                                g.DrawLine(p, 15 * s, 5.5f * s, 10.5f * s, 10.5f * s);
                            }
                            else
                            {
                                g.DrawArc(p, 7 * s, 5 * s, 5 * s, 6 * s, -55, 110);
                                g.DrawArc(p, 7.5f * s, 2.5f * s, 7.5f * s, 11 * s, -55, 110);
                            }
                        }
                        break;
                }
            }
            return bmp;
        }

        // ---------------------------------------------------------------- Sprungliste

        private string _jumpSig;

        private void TickJumpList346(double now)
        {
            if (!Win32.IsWindows || _demo) return;
            string sig = JumpSig346();
            if (sig == _jumpSig) return;
            bool firstOff = _jumpSig == null && sig == "";
            _jumpSig = sig;
            if (firstOff) return;   // war nie an: nichts anfassen
            var tasks = _settings.JumpListOn ? JumpTasks346() : null;
            try { Taskbar.SetJumpList(Application.ExecutablePath, tasks); } catch { }
        }

        private string JumpSig346() => !_settings.JumpListOn ? ""
            : "1|" + string.Join("\u0001", (_settings.Scenes ?? new List<Scene>()).Take(8).Select(s => s.Id + "=" + s.Name));

        internal Taskbar.JumpTask[] JumpTasks346()
        {
            var list = new List<Taskbar.JumpTask>();
            foreach (var sc in (_settings.Scenes ?? new List<Scene>()).Take(8))
                list.Add(new Taskbar.JumpTask { Title = "Szene: " + sc.Name, Arguments = "--cmd \"scene:" + sc.Id + "\"", Description = "Szene „" + sc.Name + "“ laden" });
            if (list.Count > 0) list.Add(new Taskbar.JumpTask { Title = null });
            list.Add(new Taskbar.JumpTask { Title = "Musik: Play / Pause", Arguments = "--cmd media.play" });
            list.Add(new Taskbar.JumpTask { Title = "Musik: nächster Titel", Arguments = "--cmd media.next" });
            list.Add(new Taskbar.JumpTask { Title = "Ausgang stumm / an", Arguments = "--cmd master.mute" });
            list.Add(new Taskbar.JumpTask { Title = "Nachtmodus an / aus", Arguments = "--cmd night.toggle" });
            list.Add(new Taskbar.JumpTask { Title = "Mini-Ansicht", Arguments = "--cmd mini.toggle" });
            return list.ToArray();
        }

        // ================================================================ 125 Schublade am Bildschirmrand

        private Timer _drawerTimer;
        private int _drawerState;            // 0 aus, 1 offen, 2 faehrt zu, 3 zu (versteckt), 4 faehrt auf
        private double _drawerAnimStart, _drawerOutsideSince = -1, _drawerEdgeSince = -1, _drawerGraceUntil;
        private float _drawerFrom, _drawerTo, _drawerT;
        private Rectangle _drawerRestore, _drawerScreen, _drawerWork;
        private bool _drawerHidden;
        private int _menusOpen346;
        private const double DrawerAnimSec = 0.18;
        private static readonly System.Diagnostics.Stopwatch DrawerClock = System.Diagnostics.Stopwatch.StartNew();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT346 rect);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT346 { public int Left, Top, Right, Bottom; }

        public bool DrawerActive => _drawerState != 0;

        private void SetDrawer346(bool on)
        {
            _settings.DrawerOn = on;
            if (on) StartDrawer346(false); else StopDrawer346();
            ScheduleSave();
        }

        private void StartDrawer346(bool startup)
        {
            if (_drawerState != 0) { PlaceDrawer346(1f); return; }
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            _drawerRestore = Bounds;
            _drawerState = 1;
            _drawerT = 1f;
            TopMost = true;
            UpdateDrawerScreen346();
            PlaceDrawer346(1f);
            _drawerOutsideSince = -1;
            _drawerGraceUntil = DrawerClock.Elapsed.TotalSeconds + (startup ? 2.5 : 1.5);   // erst zeigen, wo sie ist
            if (_drawerTimer == null)
            {
                _drawerTimer = new Timer();
                _drawerTimer.Tick += (s, e) => DrawerTick346();
            }
            _drawerTimer.Interval = 50;
            _drawerTimer.Start();
        }

        private void StopDrawer346()
        {
            if (_drawerState == 0) return;
            _drawerTimer?.Stop();
            _drawerState = 0;
            TopMost = _settings.AlwaysOnTop;
            if (_drawerHidden) { _drawerHidden = false; Show(); }
            var r = _drawerRestore;
            if (r.Width > 100 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) Bounds = r;
            else CenterToScreen();
        }

        /// <summary>Bildschirm, an dessen Rand die Schublade sitzt (der, auf dem das Fenster gerade ist).</summary>
        private void UpdateDrawerScreen346()
        {
            var scr = Screen.FromRectangle(Bounds);
            _drawerScreen = scr.Bounds;
            _drawerWork = scr.WorkingArea;
        }

        /// <summary>Lage fuer "offen" (t = 1) bzw. "zu" (t = 0).</summary>
        internal static Point DrawerPos(int edge, Rectangle screen, Rectangle work, Size size, Point cur, float t)
        {
            int w = size.Width, h = size.Height;
            switch (edge)
            {
                case 1:   // links
                {
                    int y = Math.Max(work.Top, Math.Min(work.Bottom - h, cur.Y));
                    int open = work.Left, closed = screen.Left - w;
                    return new Point((int)Math.Round(closed + (open - closed) * t), y);
                }
                case 2:   // oben
                {
                    int x = Math.Max(work.Left, Math.Min(work.Right - w, cur.X));
                    int open = work.Top, closed = screen.Top - h;
                    return new Point(x, (int)Math.Round(closed + (open - closed) * t));
                }
                default:  // rechts
                {
                    int y = Math.Max(work.Top, Math.Min(work.Bottom - h, cur.Y));
                    int open = work.Right - w, closed = screen.Right;
                    return new Point((int)Math.Round(closed + (open - closed) * t), y);
                }
            }
        }

        private void PlaceDrawer346(float t)
        {
            var sz = Size;
            if (_settings.DrawerEdge == 2) sz.Height = Math.Min(sz.Height, _drawerWork.Height);
            else sz.Height = Math.Min(sz.Height, _drawerWork.Height);
            sz.Width = Math.Min(sz.Width, _drawerWork.Width);
            if (sz != Size) Size = sz;
            Location = DrawerPos(_settings.DrawerEdge, _drawerScreen, _drawerWork, Size, Location, t);
        }

        /// <summary>Mauszeiger am Rand (im Bereich der Schublade)?</summary>
        internal static bool AtDrawerEdge(int edge, Rectangle screen, Rectangle shown, Point p)
        {
            switch (edge)
            {
                case 1: return p.X <= screen.Left + 1 && p.Y >= shown.Top && p.Y < shown.Bottom;
                case 2: return p.Y <= screen.Top + 1 && p.X >= shown.Left && p.X < shown.Right;
                default: return p.X >= screen.Right - 2 && p.Y >= shown.Top && p.Y < shown.Bottom;
            }
        }

        private bool FullscreenAppInFront346()
        {
            if (_gaming) return true;
            if (!Win32.IsWindows) return false;
            try
            {
                var fg = Win32.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle) return false;
                if (!GetWindowRect(fg, out var r)) return false;
                var sb = Screen.FromHandle(fg).Bounds;
                return r.Left <= sb.Left && r.Top <= sb.Top && r.Right >= sb.Right && r.Bottom >= sb.Bottom;
            }
            catch { return false; }
        }

        private void DrawerTick346()
        {
            if (_drawerState == 0) { _drawerTimer?.Stop(); return; }
            double now = DrawerClock.Elapsed.TotalSeconds;
            if (_mini != null && _mini.Visible) return;          // Mini-Ansicht: Schublade ruht
            if (!Visible && !_drawerHidden) return;               // ins Infobereich-Symbol geschlossen
            var p = Cursor.Position;
            switch (_drawerState)
            {
                case 1:   // offen: zufahren, wenn die Maus eine Weile weg ist
                {
                    if (WindowState != FormWindowState.Normal) return;
                    UpdateDrawerScreen346();
                    bool inside = Rectangle.Inflate(Bounds, Theme.Px(10), Theme.Px(10)).Contains(p);
                    bool busy = Control.MouseButtons != MouseButtons.None || _menusOpen346 > 0 ||
                                (Form.ActiveForm != null && Form.ActiveForm != this) || now < _drawerGraceUntil;
                    if (inside || busy) { _drawerOutsideSince = -1; return; }
                    if (_drawerOutsideSince < 0) { _drawerOutsideSince = now; return; }
                    if (now - _drawerOutsideSince < 0.7) return;
                    BeginDrawerAnim346(2, 1f, 0f);
                    return;
                }
                case 3:   // zu: am Rand aufmachen
                {
                    var shownRect = new Rectangle(DrawerPos(_settings.DrawerEdge, _drawerScreen, _drawerWork, Size, Location, 1f), Size);
                    bool edge = AtDrawerEdge(_settings.DrawerEdge, _drawerScreen, shownRect, p) && Control.MouseButtons == MouseButtons.None;
                    if (!edge || FullscreenAppInFront346()) { _drawerEdgeSince = -1; return; }
                    if (_drawerEdgeSince < 0) { _drawerEdgeSince = now; return; }
                    if (now - _drawerEdgeSince < 0.15) return;
                    _drawerEdgeSince = -1;
                    OpenDrawer346();
                    return;
                }
                case 2:
                case 4:
                {
                    float k = (float)Math.Min(1.0, (now - _drawerAnimStart) / DrawerAnimSec);
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);   // weich auslaufen
                    _drawerT = _drawerFrom + (_drawerTo - _drawerFrom) * e;
                    PlaceDrawer346(_drawerT);
                    if (k < 1f) return;
                    if (_drawerState == 2)
                    {
                        _drawerState = 3;
                        _drawerHidden = true;
                        Hide();
                    }
                    else
                    {
                        _drawerState = 1;
                        _drawerOutsideSince = -1;
                        _drawerGraceUntil = now + 0.6;
                    }
                    _drawerTimer.Interval = 50;
                    return;
                }
            }
        }

        private void BeginDrawerAnim346(int state, float from, float to)
        {
            _drawerState = state;
            _drawerFrom = from; _drawerTo = to;
            _drawerAnimStart = DrawerClock.Elapsed.TotalSeconds;
            _drawerTimer.Interval = 15;
        }

        private void OpenDrawer346()
        {
            if (_drawerState == 1 || _drawerState == 4) return;
            PlaceDrawer346(0f);
            if (_drawerHidden) { _drawerHidden = false; Show(); }
            TopMost = true;
            try { Activate(); } catch { }
            BeginDrawerAnim346(4, _drawerState == 2 ? _drawerT : 0f, 1f);
        }

        /// <summary>Aus RestoreFromTray: Schublade aufmachen statt das Fenster irgendwo zu zeigen. true = erledigt.</summary>
        private bool DrawerRestore346()
        {
            if (_drawerState == 0) return false;
            if (_mini != null && _mini.Visible) return false;
            if (!Visible && !_drawerHidden)
            {
                // war im Infobereich (geschlossen) – an den Rand legen und aufmachen
                _drawerHidden = true;
                _drawerState = 3;
            }
            if (_drawerState == 3 || _drawerState == 2) { OpenDrawer346(); return true; }
            Activate();
            return true;
        }

        // ================================================================ 130 Portabler Modus

        /// <summary>Portabel an/aus: Einstellungen umziehen und neu starten. Fehler (z. B. kein Schreibrecht) → Meldung.</summary>
        private void SetPortable346(bool on)
        {
            if (on == MixerSettings.Portable) return;
            string from = MixerSettings.Folder, to = on ? MixerSettings.PortableFolder : MixerSettings.DocsWanted ? System.IO.Path.Combine(MixerSettings.DocsRoot, "Einstellungen") : MixerSettings.AppDataFolder;   // 4.0: ohne portabel in Dokumente
            try
            {
                _settings.Save();
                Directory.CreateDirectory(to);
                CopyFolder346(from, to);
                if (on) File.WriteAllText(MixerSettings.PortableMarker, "Diese Datei sorgt dafür, dass das Mischpult seine Einstellungen im Ordner „Mischpult-Daten“ daneben speichert (portabler Modus).\r\nLöschen = Einstellungen wieder unter %AppData%\\Mischpult.\r\n");
                else File.Delete(MixerSettings.PortableMarker);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Das hat nicht geklappt: " + ex.Message + "\n\nLiegt die EXE in einem Ordner, in den du schreiben darfst (z. B. nicht unter „Programme“)?",
                    "Portabler Modus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Program.RestartRequested = true;
            ExitApp();
        }

        /// <summary>Alle Dateien (mit Unterordnern) kopieren und vorhandene ersetzen.</summary>
        internal static void CopyFolder346(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(f);
                if (name.Equals(MixerSettings.PortableMarkerName, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(to, name), true);
            }
            foreach (var d in Directory.GetDirectories(from))
                CopyFolder346(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // ================================================================ Einstellungen

        /// <summary>Seite Steuerung: Einrasten, Mausrad, Szenen-Tasten, Taskleiste.</summary>
        private void AddControl346(SPage p)
        {
            p.Section("Noch bequemer");
            p.Toggle("Fader rasten ein", "Beim Ziehen bleiben die Fader kurz an den Skalen-Strichen hängen (0, −5, −10, −20 … dB, bei %-Fadern 25/50/75/100) und an der Fader-Marke. Shift gedrückt = frei ziehen.",
                () => _settings.SnapOn, v => SetControl346(() => _settings.SnapOn = v));
            p.Choice("Mausrad-Schrittweite", () => _settings.WheelStep == 0 ? "Wie bisher: kleine Schritte nach Faderweg."
                    : "Ein Rad-Schritt = genau " + MixerSettings.WheelStepDb[_settings.WheelStep].ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " dB (Fader in Prozent: " + MixerSettings.WheelStepPct[_settings.WheelStep] + " %), der Fader springt aufs Raster. Strg = halber Schritt.",
                MixerSettings.WheelStepNames, () => _settings.WheelStep, i => SetControl346(() => _settings.WheelStep = i));
            p.Toggle("Szenen-Tasten in der Kopfleiste", "Links neben SZENEN die Tasten 1–4 für die ersten vier Szenen: ein Klick lädt sie. Die zuletzt geladene leuchtet.",
                () => _settings.SceneKeysOn, v => SetControl346(() => _settings.SceneKeysOn = v));
            p.Section("Taskleiste");
            p.Toggle("Knöpfe in der Vorschau", "Fährt die Maus über das Mischpult in der Taskleiste, erscheinen unter dem Vorschaubild: zurück, Play/Pause, weiter und Ausgang stumm.",
                () => _settings.TaskbarButtonsOn, v => { _settings.TaskbarButtonsOn = v; TaskbarApply346(); ScheduleSave(); }).Enabled = () => Win32.IsWindows;
            p.Toggle("Lautstärke als Balken im Symbol", "Das Taskleisten-Symbol zeigt die Lautstärke des Ausgangs als grünen Balken (wie ein Download-Fortschritt) – rot, wenn er stumm ist.",
                () => _settings.TaskbarVolumeOn, v => { _settings.TaskbarVolumeOn = v; TaskbarApply346(); ScheduleSave(); }).Enabled = () => Win32.IsWindows;
            p.Toggle("Sprungliste (Rechtsklick)", "Rechtsklick auf das Mischpult in der Taskleiste: die ersten 8 Szenen, Play/Pause, nächster Titel, Ausgang stumm, Nachtmodus und Mini-Ansicht – geht auch, wenn das Mischpult angeheftet und gerade zu ist.",
                () => _settings.JumpListOn, v => { _settings.JumpListOn = v; _nextControlSlow346 = 0; ScheduleSave(); }).Enabled = () => Win32.IsWindows;
        }

        /// <summary>Seite Allgemein (Fenster): Schublade, Infobereich-Symbol, Song-Einblendung.</summary>
        private void AddWindow346(SPage p)
        {
            p.Section("Schublade und Einblendungen");
            p.Toggle("Schublade am Bildschirmrand", "Das Pult versteckt sich am Rand des Bildschirms und fährt heraus, sobald die Maus ganz an den Rand stößt. Ist die Maus eine Weile weg, fährt es wieder hinein. Bei Vollbild-Spielen bleibt es zu.",
                () => _settings.DrawerOn, v => SetDrawer346(v));
            p.Choice("Rand für die Schublade", () => "An diesem Rand des Bildschirms sitzt die Schublade.",
                MixerSettings.DrawerEdgeNames, () => _settings.DrawerEdge, i => { _settings.DrawerEdge = i; if (_drawerState != 0) { UpdateDrawerScreen346(); PlaceDrawer346(_drawerState == 3 ? 0f : 1f); } ScheduleSave(); })
                .Enabled = () => _settings.DrawerOn;
            p.Choice("Symbol im Infobereich", () => _settings.TrayIconMode == 0 ? "Normal: das Mischpult-Symbol."
                    : _settings.TrayIconMode == 1 ? "Zeigt die Lautstärke des Ausgangs als Zahl mit Füllstand – rot mit Kreuz, wenn stumm."
                    : "Zwei kleine Pegel (links/rechts), die mit dem Ton tanzen – rot mit Kreuz, wenn stumm.",
                MixerSettings.TrayIconNames, () => _settings.TrayIconMode, i => { _settings.TrayIconMode = i; _trayIconSig = null; ScheduleSave(); });
            p.Toggle("Song-Einblendung", "Kommt ein neuer Titel (Spotify, YouTube …), erscheint kurz eine kleine Karte mit Cover, Titel und Interpret in einer Bildschirmecke. Nicht, wenn das Pult gerade vorne ist.",
                () => _settings.SongToastOn, v => { _settings.SongToastOn = v; _toastKey346 = null; ScheduleSave(); });
            p.Choice("Ecke für die Song-Einblendung", () => "Hier erscheint die Karte.",
                MixerSettings.SongToastPosNames, () => _settings.SongToastPos, i => { _settings.SongToastPos = i; ScheduleSave(); })
                .Enabled = () => _settings.SongToastOn;
            p.Button("Song-Einblendung ausprobieren", "Zeigt die Karte einmal mit dem Titel, der gerade läuft (oder einem Beispiel).", "Zeigen",
                r => ShowSongToast346(_nowPlaying.Current != null && !string.IsNullOrEmpty(_nowPlaying.Current.Title) ? _nowPlaying.Current
                    : new NowPlayingInfo { Title = "Beispiel-Titel", Artist = "Mischpult", Playing = true }));
        }

        /// <summary>Seite Leuchten &amp; Anzeigen: Takt-Lampe.</summary>
        private void AddBpm346(SPage p)
        {
            p.Toggle("Takt-Lampe (BPM)", "In der Kopfleiste blinkt eine Lampe im Takt der Musik, daneben das Tempo in Schlägen pro Minute – geschätzt aus dem, was gerade am Ausgang läuft (Bass und Hi-Hats). Braucht ein paar Sekunden Musik mit klarem Rhythmus.",
                () => _settings.BpmOn, v => SetControl346(() => _settings.BpmOn = v));
        }

        /// <summary>Seite Sichern: portabler Modus.</summary>
        private void AddPortable346(SPage p)
        {
            p.Toggle("Portabler Modus", () => MixerSettings.Portable
                    ? "An: alle Einstellungen liegen im Ordner „Mischpult-Daten“ neben der EXE – zusammen auf einen USB-Stick kopieren und mitnehmen. Aus = zurück nach %AppData% (startet neu)."
                    : "Speichert alle Einstellungen im Ordner „Mischpult-Daten“ direkt neben der EXE statt unter %AppData% – praktisch für einen USB-Stick. Die jetzigen Einstellungen ziehen mit um, das Mischpult startet neu.",
                () => MixerSettings.Portable, v => BeginInvoke(new Action(() => SetPortable346(v))))
                .Enabled = () => !_demo && MixerSettings.ExeFolder.Length > 0;
        }
    }
}
