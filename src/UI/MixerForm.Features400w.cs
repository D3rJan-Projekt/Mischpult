using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Fenster: Spiel-Overlay, Desktop-Widget, Mini-Player, Raum-Ansicht, Ton-Zeitleiste, Statistik, Ton-Doktor.
    /// Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private OverlayForm _overlay;
        private WidgetForm _widget;
        private PlayerForm _player;
        private RoomWindow _room;
        private TimelineWindow _timelineWin;
        private StatsWindow _statsWin;
        private DoctorWindow _doctorWin;
        private readonly SoundTimeline _timeline = new SoundTimeline();
        private bool _overlayForce;
        private double _nextWin400, _nextWinSec400, _nextStatsSave;

        private static bool Open400(Form f) => f != null && !f.IsDisposed && f.Visible;
        private bool CoverWantedWindows400 => Open400(_player) || Open400(_widget);

        private void HookWindows400()
        {
            if (_settings.StatsOn) { EnsureStats400(); _settings.Stats.Starts++; ScheduleSave(); }
            if (_settings.WidgetOn) ShowWidget400();
            if (_settings.PlayerOn) ShowPlayer400();
        }

        private void TickWindows400(double now)
        {
            if (now >= _nextWinSec400)
            {
                _nextWinSec400 = now + 1.0;
                TickTimeline400();
                TickStats400(now);
            }
            if (now < _nextWin400) return;
            _nextWin400 = now + 0.066;
            TickOverlay400();
            bool w = Open400(_widget), pl = Open400(_player), ro = Open400(_room);
            if (w || pl || _overlay != null)
            {
                var d = OverlayData400(4);
                if (w) _widget.SetData(d);
                if (pl) { _player.More = _settings.MediaMoreOn; _player.SetData(d); }
            }
            if (ro) _room.SetDots(RoomDots400());
            if (Open400(_timelineWin) && (int)(now * 15) % 15 == 0) _timelineWin.Invalidate();
        }

        /// <summary>Was Overlay, Widget und Mini-Player zeigen.</summary>
        private OverlayData OverlayData400(int maxRows)
        {
            var d = new OverlayData();
            var dev = DefaultDevice;
            if (dev != null) { d.Master = dev.Volume; d.MasterMute = dev.Mute; d.Device = dev.Name; }
            var rows = MiniRows().Where(r => r.Id.StartsWith("ch:")).ToList();
            d.Rows = rows.OrderByDescending(r => Math.Max(r.PeakL, r.PeakR)).Take(maxRows).Where(r => Math.Max(r.PeakL, r.PeakR) > 0.0005f || rows.Count <= maxRows).ToList();
            if (d.Rows.Count == 0) d.Rows = rows.Take(Math.Min(3, maxRows)).ToList();
            var np = _nowPlaying.Current;
            if (np != null && !string.IsNullOrEmpty(np.Title)) { d.Song = np.Text; d.Playing = np.Playing; d.Cover = np.Cover; d.App = np.App; d.Shuffle = np.Shuffle; d.Repeat = np.Repeat; }
            if (d.Song != null && SongProgress400(out float sf, out string stt)) { d.SongFrac = sf; d.SongTime = stt; }   // 4.0 finale
            return d;
        }

        // ================================================================ 169 Spiel-Overlay

        private void TickOverlay400()
        {
            bool want = _settings.OverlayOn && (_overlayForce || _gaming || FullscreenAppInFront346() || (_demo && DemoOn("OVERLAYON"))) && Form.ActiveForm != this;
            if (!want)
            {
                if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible) _overlay.Hide();
                return;
            }
            if (_overlay == null || _overlay.IsDisposed) _overlay = new OverlayForm();
            _overlay.SetData(OverlayData400(4), _settings.OverlayPos);
            if (!_overlay.Visible) _overlay.Show();
        }

        public void ToggleOverlay400()
        {
            if (!_settings.OverlayOn) return;
            _overlayForce = !_overlayForce;
            ShowOsd("Overlay", _overlayForce ? "immer zeigen" : "nur bei Spielen", -1, Theme.Accent);
        }

        // ================================================================ 176 Desktop-Widget

        private void ShowWidget400()
        {
            if (Open400(_widget)) return;
            _widget = new WidgetForm();
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = _settings.WidgetX.HasValue && _settings.WidgetY.HasValue ? new Point(_settings.WidgetX.Value, _settings.WidgetY.Value) : new Point(wa.Right - _widget.Width - Theme.Px(24), wa.Bottom - _widget.Height - Theme.Px(24));
            _widget.Location = OnScreen400(p, _widget.Size);
            _widget.VolumeChanged += v => MiniSetLevel("master", v);
            _widget.MuteClicked += () => MiniToggleMute("master");
            _widget.Media += id => SendMedia(id, false);
            _widget.Moved += w => { _settings.WidgetX = w.Left; _settings.WidgetY = w.Top; ScheduleSave(); };
            _widget.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var m = NewMenu();
                m.Items.Add(new ToolStripMenuItem("Pult öffnen", null, (s2, e2) => RestoreFromTray()));
                m.Items.Add(new ToolStripMenuItem("Widget ausblenden", null, (s2, e2) => SetWidget400(false)));
                m.Show(Cursor.Position);
            };
            _widget.SetData(OverlayData400(0));
            _widget.Show();
        }

        private void SetWidget400(bool on)
        {
            _settings.WidgetOn = on;
            if (on) ShowWidget400(); else { try { _widget?.Close(); } catch { } _widget = null; }
            ScheduleSave();
        }

        // ================================================================ 209 Mini-Player

        private void ShowPlayer400()
        {
            if (Open400(_player)) { _player.Activate(); return; }
            _player = new PlayerForm { TopMost = !_settings.PlayerNotTop, More = _settings.MediaMoreOn };
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = _settings.PlayerX.HasValue && _settings.PlayerY.HasValue ? new Point(_settings.PlayerX.Value, _settings.PlayerY.Value) : new Point(wa.Right - _player.Width - Theme.Px(24), wa.Top + Theme.Px(24));
            _player.Location = OnScreen400(p, _player.Size);
            _player.Media += PlayerMedia400;
            _player.Seek += SeekFrac400;   // 4.0 finale: Song-Fortschritt
            _player.Moved += w => { _settings.PlayerX = w.Left; _settings.PlayerY = w.Top; ScheduleSave(); };
            _player.FormClosed += (s, e) => { if (!_closing && !_exiting && _settings.PlayerOn) { _settings.PlayerOn = false; ScheduleSave(); } };
            _player.SetData(OverlayData400(0));
            _player.Show();
        }

        private void PlayerMedia400(string id)
        {
            if (id == "play" || id == "next" || id == "prev") SendMedia(id, false);
            else MediaAction400(id);
        }

        private void SetPlayer400(bool on)
        {
            _settings.PlayerOn = on;
            ScheduleSave();
            if (on) ShowPlayer400(); else { try { _player?.Close(); } catch { } _player = null; }
        }

        // ================================================================ 191 Raum-Ansicht

        public void ShowRoom400()
        {
            if (!_settings.RoomOn) return;
            if (Open400(_room)) { _room.Activate(); return; }
            _room = new RoomWindow();
            _room.Location = OnScreen400(new Point(Left + Theme.Px(80), Top + Theme.Px(80)), _room.Size);
            _room.Moved2 += (id, pos, pan) =>
            {
                var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == id);
                if (c == null || c.FaderLock) return;
                c.Pan = (float)Math.Round(pan, 2);
                MiniSetLevel(id, pos);
            };
            _room.MuteClicked += id => MiniToggleMute(id);
            _room.SetDots(RoomDots400());
            _room.Show(this);
        }

        private List<RoomWindow.Dot> RoomDots400()
        {
            var l = new List<RoomWindow.Dot>();
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden) continue;
                _channelPeak.TryGetValue(c.Id, out var pk);
                l.Add(new RoomWindow.Dot { Id = "ch:" + c.Id, Name = c.Name, Color = Color.FromArgb(255, Color.FromArgb(c.Color)), Pos = c.Fader, Pan = c.Pan, Mute = c.Mute, Peak = pk == null ? 0f : Math.Max(pk[0], pk[1]) });
            }
            return l;
        }

        // ================================================================ 203 Ton-Zeitleiste

        private void TickTimeline400()
        {
            if (!_settings.TimelineOn) return;
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden) continue;
                _channelPeak.TryGetValue(c.Id, out var pk);
                bool on = !c.Mute && pk != null && Math.Max(pk[0], pk[1]) > 0.003f;   // ab etwa −50 dB
                _timeline.Mark(c.Id, c.Name, Color.FromArgb(255, Color.FromArgb(c.Color)), on);
            }
        }

        public void ShowTimeline400()
        {
            if (!_settings.TimelineOn) return;
            if (Open400(_timelineWin)) { _timelineWin.Activate(); return; }
            _timelineWin = new TimelineWindow(_timeline);
            _timelineWin.Location = OnScreen400(new Point(Left + Theme.Px(60), Top + Theme.Px(120)), _timelineWin.Size);
            _timelineWin.Show(this);
        }

        private void TimelineEvent400(string text) { if (_settings.TimelineOn) _timeline.Event(text); }

        // ================================================================ 204 Statistik

        private void EnsureStats400()
        {
            if (_settings.Stats == null) _settings.Stats = new StatsData();
            var s = _settings.Stats;
            if (s.Since == null) s.Since = DateTime.Today.ToString("dd.MM.yyyy");
        }

        private int _statsMin;

        private void TickStats400(double now)
        {
            if (!_settings.StatsOn) return;
            EnsureStats400();
            var st = _settings.Stats;
            bool any = false;
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden || c.Mute) continue;
                _channelPeak.TryGetValue(c.Id, out var pk);
                if (pk == null || Math.Max(pk[0], pk[1]) <= 0.003f) continue;
                if (st.ChannelSec == null) st.ChannelSec = new Dictionary<string, int>();
                string k = c.Name ?? c.Id;
                st.ChannelSec[k] = (st.ChannelSec.TryGetValue(k, out int v) ? v : 0) + 1;
                any = true;
            }
            if (any)
            {
                if (st.DaySec == null) st.DaySec = new Dictionary<string, int>();
                string day = DateTime.Today.ToString("yyyy-MM-dd");
                st.DaySec[day] = (st.DaySec.TryGetValue(day, out int dv) ? dv : 0) + 1;
            }
            var lv = _snap.Loudness;
            if (lv != null && lv.Length > 1 && !float.IsInfinity(lv[1]) && lv[1] > -70f && lv[1] < 10f && (st.MaxLufs == 0f || lv[1] > st.MaxLufs))
            {
                st.MaxLufs = (float)Math.Round(lv[1], 1);
                st.MaxLufsWhen = DateTime.Now.ToString("dd.MM. HH:mm");
            }
            if (++_statsMin >= 60)
            {
                _statsMin = 0;
                var d = DefaultDevice;
                if (d != null && !d.Mute) { st.VolSum += d.Volume; st.VolCount++; }
            }
            if (now >= _nextStatsSave) { _nextStatsSave = now + 60; ScheduleSave(); }
        }

        private void NoteScene400(Scene sc)
        {
            if (sc == null) return;
            TimelineEvent400("Szene " + sc.Name);
            ObsSceneFromPult400(sc);   // 198: gleichnamige OBS-Szene
            if (!_settings.StatsOn) return;
            EnsureStats400();
            if (_settings.Stats.SceneUse == null) _settings.Stats.SceneUse = new Dictionary<string, int>();
            string k = sc.Name ?? "?";
            _settings.Stats.SceneUse[k] = (_settings.Stats.SceneUse.TryGetValue(k, out int v) ? v : 0) + 1;
        }

        public void ShowStats400()
        {
            if (!_settings.StatsOn) return;
            EnsureStats400();
            if (Open400(_statsWin)) { _statsWin.Activate(); return; }
            _statsWin = new StatsWindow { Data = () => _settings.Stats, Extra = () => "Zählt nur, solange „Statistik“ an ist. Bleibt auf diesem PC (in den Einstellungen)." };
            _statsWin.Location = OnScreen400(new Point(Left + Theme.Px(100), Top + Theme.Px(70)), _statsWin.Size);
            _statsWin.Show(this);
        }

        private void ResetStats400()
        {
            if (MessageBox.Show(this, "Alle Zahlen der Statistik löschen?", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _settings.Stats = null;
            EnsureStats400();
            ScheduleSave();
            _statsWin?.Invalidate();
        }

        // ================================================================ 180 Ton-Doktor

        public void ShowDoctor400()
        {
            if (!_settings.DoctorOn) return;
            if (Open400(_doctorWin)) { _doctorWin.Recheck(); _doctorWin.Activate(); return; }
            _doctorWin = new DoctorWindow { Run = DoctorChecks400 };
            _doctorWin.Location = OnScreen400(new Point(Left + Theme.Px(120), Top + Theme.Px(60)), _doctorWin.Size);
            _doctorWin.Show(this);
        }

        /// <summary>Windows: andere Toene bei Anrufen leiser? (0 stumm, 1 −80 %, 2 −50 % (Standard), 3 nichts tun)</summary>
        private static int WindowsDucking()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Multimedia\Audio"))
                    return k?.GetValue("UserDuckingPreference") is int v ? v : 1;
            }
            catch { return -1; }
        }

        private static void SetWindowsDuckingOff()
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio")) k?.SetValue("UserDuckingPreference", 3, RegistryValueKind.DWord); } catch { }
        }

        /// <summary>Alles pruefen, was dazu fuehren kann, dass etwas nicht (oder zu leise) zu hoeren ist.</summary>
        internal List<DoctorCheck> DoctorChecks400()
        {
            var l = new List<DoctorCheck>();
            var dev = DefaultDevice;
            if (dev == null) l.Add(new DoctorCheck { Level = 2, Title = "Kein Ausgabegerät", Detail = "Windows hat gerade kein Standardgerät für den Ton. Kopfhörer oder Lautsprecher anschließen bzw. in Windows auswählen." });
            else
            {
                if (dev.Mute) l.Add(new DoctorCheck { Level = 2, Title = "Ausgang ist stumm: " + dev.Name, Detail = "Der Windows-Ausgang selbst ist stummgeschaltet.", FixText = "Ton an", Fix = () => MiniToggleMute("master") });
                else if (dev.Volume < 0.03f) l.Add(new DoctorCheck { Level = 2, Title = "Ausgang ganz leise (" + (int)Math.Round(dev.Volume * 100) + " %)", Detail = dev.Name + " steht fast auf null.", FixText = "Auf 40 %", Fix = () => MiniSetLevel("master", 0.4f) });
                else l.Add(new DoctorCheck { Level = 0, Title = "Ausgang: " + dev.Name + " · " + (int)Math.Round(dev.Volume * 100) + " %" + (string.IsNullOrEmpty(dev.FormatText) ? "" : " · " + dev.FormatText) });
            }
            if (PanicActive) l.Add(new DoctorCheck { Level = 2, Title = "Panik-Taste ist gedrückt", Detail = "Alle Ausgänge sind stumm, bis die Panik-Taste noch einmal gedrückt wird.", FixText = "Aufheben", Fix = TogglePanic });
            var solo = _settings.Channels.Where(c => c.Solo).ToList();
            if (solo.Count > 0) l.Add(new DoctorCheck { Level = 1, Title = "SOLO an: " + string.Join(", ", solo.Select(c => c.Name)), Detail = "Solange ein Kanal auf SOLO steht, sind alle anderen still.", FixText = "SOLO aus", Fix = () => { foreach (var c in _settings.Channels) c.Solo = false; foreach (var c in solo) OnChannelChanged(c, false); foreach (var s in _channelStrips) s.InvalidateChrome(); ScheduleSave(); } });
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden) continue;
                var apps = _snap.Apps.Where(a => c.IsCatchAll ? ChannelOf(a.Key) == c : c.Apps.Contains(a.Key, StringComparer.OrdinalIgnoreCase)).ToList();
                bool playing = apps.Any(a => a.Active);
                if (!playing) continue;
                var ch = c;
                if (c.Mute) l.Add(new DoctorCheck { Level = 2, Title = c.Name + " ist stumm – spielt aber gerade", Detail = string.Join(", ", apps.Where(a => a.Active).Select(a => a.Name).Distinct()) + " ist nicht zu hören, weil MUTE an ist.", FixText = "MUTE aus", Fix = () => SetMute400("ch:" + ch.Id, false) });
                else if (c.Fader <= FaderLaw.MinPos + 0.001f) l.Add(new DoctorCheck { Level = 2, Title = c.Name + " ist ganz unten", Detail = "Der Fader steht auf −∞ – die Apps darin sind nicht zu hören.", FixText = "Auf −10 dB", Fix = () => MiniSetLevel("ch:" + ch.Id, FaderLaw.DbToPos(-10f)) });
                else if (FaderLaw.PosToDb(c.Fader) < -35f) l.Add(new DoctorCheck { Level = 1, Title = c.Name + " ist sehr leise (" + FaderLaw.FormatDb(c.Fader) + ")", Detail = "Nur sehr leise zu hören.", FixText = "Auf −10 dB", Fix = () => MiniSetLevel("ch:" + ch.Id, FaderLaw.DbToPos(-10f)) });
            }
            int duck = Win32.IsWindows ? WindowsDucking() : -1;
            if (duck >= 0 && duck != 3) l.Add(new DoctorCheck { Level = 1, Title = "Windows macht bei Anrufen alles leiser", Detail = "Erkennt Windows eine „Kommunikation“, wird der übrige Ton um " + (duck == 0 ? "100" : duck == 1 ? "80" : "50") + " % leiser. Das wirkt oft so, als ob das Pult spinnt.", FixText = "Abschalten", Fix = SetWindowsDuckingOff });
            if (_settings.EveningOn && EveningCeiling(_settings, DateTime.Now) >= 0f) l.Add(new DoctorCheck { Level = 1, Title = "Abendkurve aktiv", Detail = "Der Ausgang wird gerade höchstens " + (int)Math.Round(EveningCeiling(_settings, DateTime.Now) * 100) + " % laut.", FixText = "Ausschalten", Fix = () => { _settings.EveningOn = false; ScheduleSave(); } });
            if (_settings.QuietHoursOn) l.Add(new DoctorCheck { Level = 1, Title = "Leise-Stunden sind eingeschaltet", Detail = "In der eingestellten Zeit wird der Ausgang nicht lauter als die Grenze." });
            if (_settings.TimeLimitOn && _settings.Channels.Any(c => c.TimeLimitMin > 0)) l.Add(new DoctorCheck { Level = 1, Title = "Zeit-Limit pro Kanal ist an", Detail = "Ein Kanal, der sein Tageslimit erreicht hat, wird ausgeblendet." });
            if (_focusRun) l.Add(new DoctorCheck { Level = 1, Title = "Fokus-Timer läuft", Detail = "Die Fokus-Kanäle sind gerade " + (_settings.FocusMode == 0 ? "stumm." : "leiser."), FixText = "Stoppen", Fix = ToggleFocus });
            if (_top.SleepLeft >= 0) l.Add(new DoctorCheck { Level = 1, Title = "Sleep-Timer läuft", Detail = "Am Ende wird der Ton ausgeblendet." });
            if (!_snap.EqSupported && !_demo) l.Add(new DoctorCheck { Level = 1, Title = "EQ & Effekte gehen auf diesem System nicht", Detail = "Lautstärke, MUTE und Szenen funktionieren trotzdem. EQ, Kompressor und Effekte brauchen Windows 10 (ab 2004) oder 11." });
            if (l.All(x => x.Level == 0)) l.Add(new DoctorCheck { Level = 0, Title = "Keine stummen oder ganz leisen Kanäle, in denen gerade etwas spielt." });
            return l;
        }

        // ================================================================ Menue, Tastenkuerzel, Einstellungen

        private void AddWindowsMenu400(List<ToolStripItem> items)
        {
            var list = new List<ToolStripItem>();
            if (_settings.RoomOn) list.Add(new ToolStripMenuItem("Raum-Ansicht", null, (s, e) => ShowRoom400()));
            if (_settings.TimelineOn) list.Add(new ToolStripMenuItem("Ton-Zeitleiste", null, (s, e) => ShowTimeline400()));
            if (_settings.StatsOn) list.Add(new ToolStripMenuItem("Statistik", null, (s, e) => ShowStats400()));
            if (_settings.DoctorOn) list.Add(new ToolStripMenuItem("Ton-Doktor", null, (s, e) => ShowDoctor400()));
            list.Add(new ToolStripMenuItem("Mini-Player", null, (s, e) => SetPlayer400(!Open400(_player))) { Checked = Open400(_player) });
            list.Add(new ToolStripMenuItem("Desktop-Widget", null, (s, e) => SetWidget400(!Open400(_widget))) { Checked = Open400(_widget) });
            if (_settings.OverlayOn) list.Add(new ToolStripMenuItem("Overlay immer zeigen", null, (s, e) => ToggleOverlay400()) { Checked = _overlayForce });
            var sub = Sub("Fenster", true);
            foreach (var it in list) sub.DropDownItems.Add(it);
            items.Add(sub);
        }

        private void AddHotkeyActionsWindows400(List<HotkeyAction> list)
        {
            if (_settings.OverlayOn) list.Add(new HotkeyAction("win.overlay", "Overlay: immer zeigen / nur bei Spielen", "Allgemein"));
            list.Add(new HotkeyAction("win.player", "Mini-Player zeigen/verstecken", "Allgemein"));
            if (_settings.RoomOn) list.Add(new HotkeyAction("win.room", "Raum-Ansicht öffnen", "Allgemein"));
            if (_settings.TimelineOn) list.Add(new HotkeyAction("win.timeline", "Ton-Zeitleiste öffnen", "Allgemein"));
            if (_settings.StatsOn) list.Add(new HotkeyAction("win.stats", "Statistik öffnen", "Allgemein"));
            if (_settings.DoctorOn) list.Add(new HotkeyAction("win.doctor", "Ton-Doktor öffnen", "Allgemein"));
        }

        private bool ExecuteHotkeyWindows400(string action)
        {
            switch (action)
            {
                case "win.overlay": ToggleOverlay400(); return true;
                case "win.player": SetPlayer400(!Open400(_player)); return true;
                case "win.room": ShowRoom400(); return true;
                case "win.timeline": ShowTimeline400(); return true;
                case "win.stats": ShowStats400(); return true;
                case "win.doctor": ShowDoctor400(); return true;
            }
            return false;
        }

        private void AddWindows400(SPage p)
        {
            p.Section("4.0 Fenster");
            p.Toggle("Spiel-Overlay", "Ist ein Spiel vorne (Fenster oder randloses Vollbild), zeigt eine kleine Tafel in der Ecke: Lautstärke, die lautesten Kanäle und was gerade läuft. Klicks gehen hindurch, das Spiel behält den Fokus. Bei echtem Vollbild (exklusiv) kann Windows sie nicht zeigen.",
                () => _settings.OverlayOn, v => { _settings.OverlayOn = v; if (!v) { _overlayForce = false; try { _overlay?.Close(); } catch { } _overlay = null; } ApplyHotkeys(); ScheduleSave(); });
            p.Choice("Ecke des Overlays", () => "Wo die Tafel steht (Hauptbildschirm).", MixerSettings.OverlayPosNames, () => _settings.OverlayPos, i => { _settings.OverlayPos = i; ScheduleSave(); })
                .Enabled = () => _settings.OverlayOn;
            p.Toggle("Desktop-Widget", "Ein Lautstärke-Ring auf dem Schreibtisch (hinter den Fenstern): ziehen oder Mausrad = Lautstärke, Klick in die Mitte = stumm, darunter Musik-Tasten. Rechtsklick: ausblenden.",
                () => _settings.WidgetOn, v => SetWidget400(v));
            p.Toggle("Mini-Player", "Kleines Fenster mit Cover, Titel und Musik-Tasten (mit „Mehr Musik-Tasten“ auch Spulen, Zufall, Wiederholen).",
                () => _settings.PlayerOn, v => SetPlayer400(v));
            p.Toggle("Mini-Player immer im Vordergrund", "Aus = verhält sich wie ein normales Fenster.", () => !_settings.PlayerNotTop, v => { _settings.PlayerNotTop = !v; if (Open400(_player)) _player.TopMost = v; ScheduleSave(); })
                .Enabled = () => _settings.PlayerOn;
            p.Toggle("Raum-Ansicht", "Dein Mix von oben: du in der Mitte, jeder Kanal ein Punkt – näher = lauter, links/rechts = Panorama. Punkte mit der Maus verschieben, Doppelklick = stumm. Öffnen: Rechtsklick auf OPTIONEN → Fenster.",
                () => _settings.RoomOn, v => { _settings.RoomOn = v; if (!v) { try { _room?.Close(); } catch { } } ApplyHotkeys(); ScheduleSave(); });
            p.Button("Raum-Ansicht öffnen", "", "Öffnen", r => ShowRoom400()).Enabled = () => _settings.RoomOn;
            p.Toggle("Ton-Zeitleiste", "Zeichnet auf, welcher Kanal wann zu hören war (die letzten 2 Stunden, nur im Speicher) – mit Szenenwechseln als Marken.",
                () => _settings.TimelineOn, v => { _settings.TimelineOn = v; if (!v) { try { _timelineWin?.Close(); } catch { } } ApplyHotkeys(); ScheduleSave(); });
            p.Button("Zeitleiste öffnen", "", "Öffnen", r => ShowTimeline400()).Enabled = () => _settings.TimelineOn;
            p.Toggle("Statistik", "Zählt mit: welche Kanäle wie lange zu hören waren, welche Szenen du am meisten lädst, Minuten mit Ton pro Tag, Starts. Bleibt auf diesem PC.",
                () => _settings.StatsOn, v => { _settings.StatsOn = v; if (v) EnsureStats400(); ApplyHotkeys(); ScheduleSave(); });
            p.Button("Statistik", "Anzeigen oder alle Zahlen löschen.", "Öffnen ▾", r =>
            {
                var m = NewMenu();
                m.Items.Add(new ToolStripMenuItem("Statistik anzeigen", null, (s, e) => ShowStats400()));
                m.Items.Add(new ToolStripMenuItem("Zahlen löschen …", null, (s, e) => BeginInvoke(new Action(ResetStats400))));
                ShowMenuAt(m, r);
            }, () => _settings.Stats?.Since == null ? "" : "seit " + _settings.Stats.Since).Enabled = () => _settings.StatsOn;
            p.Toggle("Ton-Doktor", "Prüft auf Knopfdruck, warum etwas nicht zu hören sein könnte – stumme oder ganz leise Kanäle, in denen gerade etwas spielt, SOLO, Panik, Windows-Leiserstellen bei Anrufen … – und behebt es mit einem Klick.",
                () => _settings.DoctorOn, v => { _settings.DoctorOn = v; ApplyHotkeys(); ScheduleSave(); });
            p.Button("Ton-Doktor öffnen", "", "Prüfen", r => ShowDoctor400()).Enabled = () => _settings.DoctorOn;
        }
    }
}
