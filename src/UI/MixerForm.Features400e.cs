using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Bedienung: Fader-Automation, Mausrad ueber einem Fenster, heisse Ecke, Apps zwischen Kanaelen ziehen,
    /// Kanal-Fenster, Kanal-Suche (Strg+F), Teil-Szenen. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyBedienung400(MixerSettings s)
        {
            ChannelStrip.AppDragOn = s.AppDragOn;
        }

        private void HookBedienung400()
        {
            RestoreChanWins400();
            ApplyTrayWheel();
        }

        private void TickBedienung400(double now)
        {
            TickAutomation400(now);
            TickChanWins400(now);
        }

        // ================================================================ 172 Fader-Automation

        private AutoTake _autoRec, _autoPlay;
        private readonly Stopwatch _autoClock = new Stopwatch();
        private readonly Dictionary<string, KeyValuePair<float, bool>> _autoLast = new Dictionary<string, KeyValuePair<float, bool>>();
        private int _autoIdx;
        private double _nextAutoSample;
        public bool AutoRecording => _autoRec != null;
        public bool AutoPlaying => _autoPlay != null;

        /// <summary>Alle Werte, die die Automation aufnimmt: Kanal-ID (bzw. "master", "grp:…") → Faderstand und stumm.</summary>
        private IEnumerable<KeyValuePair<string, KeyValuePair<float, bool>>> AutoState()
        {
            foreach (var c in _settings.Channels) if (!c.Hidden) yield return new KeyValuePair<string, KeyValuePair<float, bool>>(c.Id, new KeyValuePair<float, bool>(c.Fader, c.Mute));
            foreach (var g in _settings.Groups) yield return new KeyValuePair<string, KeyValuePair<float, bool>>("grp:" + g.Id, new KeyValuePair<float, bool>(g.Fader, g.Mute));
            var d = DefaultDevice;
            if (d != null) yield return new KeyValuePair<string, KeyValuePair<float, bool>>("master", new KeyValuePair<float, bool>(d.Volume, d.Mute));
        }

        public void ToggleAutoRec()
        {
            if (!_settings.AutomationOn) return;
            if (_autoRec != null) { StopAutoRec(); return; }
            StopAutoPlay();
            _autoRec = new AutoTake { Id = Guid.NewGuid().ToString("N"), Name = "Aufnahme " + ((_settings.Takes?.Count ?? 0) + 1), Events = new List<AutoEvent>() };
            _autoLast.Clear();
            _autoClock.Restart();
            _nextAutoSample = 0;
            SampleAuto(true);
            ShowOsd("Fader-Automation", "● nimmt auf – bewege die Fader", -1, Theme.MuteRed);
            RefreshTopKeys400();
        }

        private void StopAutoRec()
        {
            var t = _autoRec;
            if (t == null) return;
            SampleAuto(false);
            _autoRec = null;
            t.Seconds = (float)_autoClock.Elapsed.TotalSeconds;
            int moves = t.Events.Count(e => e.T > 0.001f);
            if (moves == 0) { ShowOsd("Fader-Automation", "nichts bewegt – nicht gespeichert", -1, Theme.TextDim); RefreshTopKeys400(); return; }
            if (_settings.Takes == null) _settings.Takes = new List<AutoTake>();
            _settings.Takes.Add(t);
            if (_settings.Takes.Count > 20) _settings.Takes.RemoveAt(0);
            ScheduleSave();
            ShowOsd("Fader-Automation", t.Name + " gespeichert (" + FormatSec(t.Seconds) + ")", -1, Theme.LedGreen);
            RefreshTopKeys400();
        }

        internal static string FormatSec(float s) { int x = (int)Math.Round(s); return x / 60 + ":" + (x % 60).ToString("00"); }

        /// <summary>Waehrend der Aufnahme 20 mal pro Sekunde: was hat sich geaendert?</summary>
        private void SampleAuto(bool initial)
        {
            if (_autoRec == null) return;
            float t = initial ? 0f : (float)_autoClock.Elapsed.TotalSeconds;
            foreach (var kv in AutoState())
            {
                var v = kv.Value;
                bool had = _autoLast.TryGetValue(kv.Key, out var last);
                bool posCh = !had || Math.Abs(last.Key - v.Key) > 0.0015f;
                bool muteCh = !had || last.Value != v.Value;
                if (!posCh && !muteCh) continue;
                _autoLast[kv.Key] = v;
                _autoRec.Events.Add(new AutoEvent { T = t, Ch = kv.Key, Pos = posCh ? v.Key : float.NaN, Mute = muteCh ? (v.Value ? 1 : 2) : 0 });
                if (_autoRec.Events.Count >= 20000) { StopAutoRec(); return; }
            }
        }

        public void PlayTake(AutoTake t, bool loop)
        {
            if (t == null || t.Events == null || t.Events.Count == 0) return;
            if (_autoRec != null) StopAutoRec();
            t.Loop = loop;
            _autoPlay = t;
            _autoIdx = 0;
            _autoClock.Restart();
            ShowOsd("Fader-Automation", "▶ " + t.Name + (loop ? " (Schleife)" : ""), -1, Theme.LedGreen);
            RefreshTopKeys400();
        }

        public void StopAutoPlay()
        {
            if (_autoPlay == null) return;
            _autoPlay = null;
            RefreshTopKeys400();
        }

        private void TickAutomation400(double now)
        {
            if (_autoRec != null)
            {
                if (!_settings.AutomationOn) { _autoRec = null; RefreshTopKeys400(); return; }
                if (now >= _nextAutoSample) { _nextAutoSample = now + 0.05; SampleAuto(false); }
                return;
            }
            var t = _autoPlay;
            if (t == null) return;
            if (!_settings.AutomationOn) { StopAutoPlay(); return; }
            float el = (float)_autoClock.Elapsed.TotalSeconds;
            while (_autoIdx < t.Events.Count && t.Events[_autoIdx].T <= el) ApplyAutoEvent(t.Events[_autoIdx++]);
            if (_autoIdx >= t.Events.Count && el >= t.Seconds)
            {
                if (t.Loop) { _autoIdx = 0; _autoClock.Restart(); }
                else { StopAutoPlay(); ShowOsd("Fader-Automation", t.Name + " fertig", -1, Theme.TextDim); }
            }
        }

        private void ApplyAutoEvent(AutoEvent e)
        {
            string id = e.Ch == "master" || e.Ch.StartsWith("grp:") ? e.Ch : "ch:" + e.Ch;
            if (!float.IsNaN(e.Pos)) MiniSetLevel(id, e.Pos);
            if (e.Mute != 0) SetMute400(id, e.Mute == 1);
        }

        /// <summary>Stumm setzen (nicht umschalten) – fuer Automation, Raum-Ansicht, Fenster.</summary>
        internal void SetMute400(string id, bool mute)
        {
            if (id == "master") { var d = DefaultDevice; if (d != null && d.Mute != mute) MiniToggleMute(id); return; }
            if (id.StartsWith("grp:")) { var g = _settings.Groups.FirstOrDefault(x => "grp:" + x.Id == id); if (g != null && g.Mute != mute) MiniToggleMute(id); return; }
            var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == id);
            if (c != null && c.Mute != mute) MiniToggleMute(id);
        }

        private void AddAutomationMenu400(ToolStripItemCollection items)
        {
            if (!_settings.AutomationOn) return;
            var sub = Sub("Fader-Automation", true);
            sub.DropDownItems.Add(new ToolStripMenuItem(_autoRec != null ? "■ Aufnahme stoppen" : "● Fader-Bewegungen aufnehmen", null, (s, e) => ToggleAutoRec()));
            if (_autoPlay != null) sub.DropDownItems.Add(new ToolStripMenuItem("■ Wiedergabe stoppen (" + _autoPlay.Name + ")", null, (s, e) => StopAutoPlay()));
            if (_settings.Takes != null && _settings.Takes.Count > 0)
            {
                sub.DropDownItems.Add(new ToolStripSeparator());
                foreach (var tk in _settings.Takes.ToList())
                {
                    var take = tk;
                    var it = Sub(take.Name + "  (" + FormatSec(take.Seconds) + ")", true);
                    it.Checked = ReferenceEquals(_autoPlay, take);
                    it.DropDownItems.Add(new ToolStripMenuItem("▶ Abspielen", null, (s, e) => PlayTake(take, false)));
                    it.DropDownItems.Add(new ToolStripMenuItem("⟲ In Schleife abspielen", null, (s, e) => PlayTake(take, true)));
                    it.DropDownItems.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() =>
                    {
                        using (var d = new InputDialog("Aufnahme umbenennen", "Neuer Name:", take.Name)) if (d.ShowDialog(this) == DialogResult.OK) { take.Name = d.Value; ScheduleSave(); }
                    }))));
                    it.DropDownItems.Add(new ToolStripMenuItem("Löschen", null, (s, e) => { if (ReferenceEquals(_autoPlay, take)) StopAutoPlay(); _settings.Takes.Remove(take); if (_settings.Takes.Count == 0) _settings.Takes = null; ScheduleSave(); }));
                    sub.DropDownItems.Add(it);
                }
            }
            else sub.DropDownItems.Add(Hint("Noch nichts aufgenommen."));
            items.Add(sub);
        }

        // ================================================================ 173 Mausrad ueber einem Fenster, 219 heisse Ecke

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private static bool KeyDown400(int vk) { try { return (GetAsyncKeyState(vk) & 0x8000) != 0; } catch { return false; } }

        /// <summary>Gedrueckte Zusatztasten passen zur Einstellung (0 Alt, 1 Strg+Alt, 2 Umschalt+Alt, 3 Strg+Umschalt)?</summary>
        internal static bool ModMatches(int mod, bool ctrl, bool alt, bool shift)
        {
            switch (mod)
            {
                case 1: return ctrl && alt && !shift;
                case 2: return shift && alt && !ctrl;
                case 3: return ctrl && shift && !alt;
                default: return alt && !ctrl && !shift;
            }
        }

        /// <summary>In welcher Ecke steht der Punkt (0 unten rechts, 1 unten links, 2 oben rechts, 3 oben links; -1 = keine)?</summary>
        internal static int CornerOf(Rectangle screen, int x, int y, int margin)
        {
            bool l = x <= screen.Left + margin, r = x >= screen.Right - 1 - margin, t = y <= screen.Top + margin, b = y >= screen.Bottom - 1 - margin;
            if (b && r) return 0;
            if (b && l) return 1;
            if (t && r) return 2;
            if (t && l) return 3;
            return -1;
        }

        private long _altUsedMs;

        /// <summary>Aus dem Maus-Haken (muss schnell sein): true = Mausrad gehoert uns (nicht weiterreichen).</summary>
        private bool WheelHook400(int x, int y, short delta)
        {
            if (_settings.HotCornerOn)
            {
                Rectangle scr;
                try { scr = Screen.FromPoint(new Point(x, y)).Bounds; } catch { scr = Rectangle.Empty; }
                if (!scr.IsEmpty && CornerOf(scr, x, y, 4) == _settings.HotCorner)
                {
                    string act = TargetAction(_settings.HotCornerTarget, delta > 0 ? "up" : "down");
                    BeginInvoke(new Action(() => { try { ExecuteHotkey(act); } catch { } }));
                    return true;
                }
            }
            if (_settings.WinWheelOn)
            {
                bool ctrl = KeyDown400(0x11), alt = KeyDown400(0x12), shift = KeyDown400(0x10);
                if (!ModMatches(_settings.WinWheelMod, ctrl, alt, shift)) return false;
                IntPtr h = Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y });
                if (h == IntPtr.Zero) return false;
                h = GetAncestor(h, 2);   // GA_ROOT
                Win32.GetWindowThreadProcessId(h, out uint pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return false;   // ueber dem Pult: normal weiter
                if (alt)
                {
                    // Alt allein loslassen oeffnet sonst das Menue des Fensters: einmal eine unbelegte Taste "druecken"
                    long now = Environment.TickCount;
                    if (now - _altUsedMs > 400) { try { keybd_event(0xE8, 0, 0, UIntPtr.Zero); keybd_event(0xE8, 0, 2, UIntPtr.Zero); } catch { } }
                    _altUsedMs = now;
                }
                BeginInvoke(new Action(() => WinWheelApply(pid, delta)));
                return true;
            }
            return false;
        }

        private void WinWheelApply(uint pid, short delta)
        {
            string key = KeyOfPid(pid);
            var app = key == null ? null : _snap.Apps.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
            if (app == null) { ShowOsd("Mausrad", "Dieses Fenster spielt gerade keinen Ton", -1, Theme.TextDim); return; }
            float np = NudgeApp(app.Key, delta > 0 ? 240 : -240, false);   // 2 dB pro Raste
            float db = FaderLaw.PosToDb(np);
            ShowOsd(app.Name, float.IsNegativeInfinity(db) ? "aus" : db > -0.05f ? "0 dB (wie der Kanal)" : FaderLaw.FormatDb(np), np, Theme.Accent);
        }

        // ================================================================ 184 Apps zwischen Kanaelen ziehen

        /// <summary>Waehrend eine App gezogen wird: Kanal unter der Maus hervorheben.</summary>
        public void AppDragHover(Point screen)
        {
            var target = StripAtScreen400(screen);
            if (!ReferenceEquals(ChannelStrip.DropTarget, target))
            {
                var old = ChannelStrip.DropTarget;
                ChannelStrip.DropTarget = target;
                old?.Invalidate();
                target?.Invalidate();
            }
        }

        public void DropApp(string key, ChannelStrip from, Point screen)
        {
            var target = StripAtScreen400(screen);
            var old = ChannelStrip.DropTarget;
            ChannelStrip.DropTarget = null;
            old?.Invalidate();
            if (target == null || ReferenceEquals(target, from) || key == null) return;
            if (target.Config.Apps.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase))) return;
            string name = AppDisplayName(key);
            AssignApp(key, target.Config);
            ShowOsd("App verschoben", name + " → " + target.Config.Name, -1, Color.FromArgb(target.Config.Color));
        }

        private ChannelStrip StripAtScreen400(Point screen)
        {
            foreach (var s in _channelStrips)
            {
                if (!s.Visible || s.Parent == null) continue;
                try { if (s.RectangleToScreen(s.ClientRectangle).Contains(screen)) return s; } catch { }
            }
            return null;
        }

        // ================================================================ 185 Kanal-Fenster

        private readonly Dictionary<string, ChanWindow> _chanWins = new Dictionary<string, ChanWindow>();
        private double _nextChanWin;

        public void OpenChanWin400(ChannelConfig c, Point? at = null)
        {
            if (!_settings.ChanWinOn || c == null) return;
            string id = "ch:" + c.Id;
            if (_chanWins.TryGetValue(id, out var w) && !w.IsDisposed) { w.Close(); return; }   // nochmal = zu
            var saved = _settings.ChanWins?.FirstOrDefault(x => x.Id == c.Id);
            w = new ChanWindow(id);
            var p = at ?? (saved != null ? new Point(saved.X, saved.Y) : new Point(Cursor.Position.X - Theme.Px(48), Cursor.Position.Y - Theme.Px(20)));
            w.Location = OnScreen400(p, w.Size);
            w.LevelChanged += (rid, pos) => MiniSetLevel(rid, pos);
            w.MuteClicked += rid => MiniToggleMute(rid);
            w.Moved += win => { RememberChanWin400(c.Id, win.Location); };
            w.Opened += win => RestoreFromTray();
            w.FormClosed += (s, e) =>
            {
                _chanWins.Remove(id);
                if (!_closing && !_exiting && _settings.ChanWins != null) { _settings.ChanWins.RemoveAll(x => x.Id == c.Id); if (_settings.ChanWins.Count == 0) _settings.ChanWins = null; ScheduleSave(); }
            };
            _chanWins[id] = w;
            RememberChanWin400(c.Id, w.Location);
            w.SetRow(MiniRows().FirstOrDefault(r => r.Id == id));
            w.Show();
        }

        private void RememberChanWin400(string chId, Point p)
        {
            if (_settings.ChanWins == null) _settings.ChanWins = new List<ChanWinConfig>();
            var e = _settings.ChanWins.FirstOrDefault(x => x.Id == chId);
            if (e == null) _settings.ChanWins.Add(e = new ChanWinConfig { Id = chId });
            e.X = p.X; e.Y = p.Y;
            ScheduleSave();
        }

        internal static Point OnScreen400(Point p, Size s)
        {
            try
            {
                var wa = Screen.FromPoint(p).WorkingArea;
                return new Point(Math.Max(wa.Left, Math.Min(wa.Right - s.Width, p.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - s.Height, p.Y)));
            }
            catch { return p; }
        }

        private void RestoreChanWins400()
        {
            if (!_settings.ChanWinOn || _settings.ChanWins == null) return;
            foreach (var cw in _settings.ChanWins.ToList())
            {
                var c = _settings.Channels.FirstOrDefault(x => x.Id == cw.Id);
                if (c != null) OpenChanWin400(c, new Point(cw.X, cw.Y));
            }
        }

        private void CloseChanWins400()
        {
            foreach (var w in _chanWins.Values.ToList()) { try { w.Close(); } catch { } }
            _chanWins.Clear();
        }

        private void TickChanWins400(double now)
        {
            if (_chanWins.Count == 0 || now < _nextChanWin) return;
            _nextChanWin = now + 0.033;
            var rows = MiniRows();
            foreach (var kv in _chanWins.ToList())
            {
                var r = rows.FirstOrDefault(x => x.Id == kv.Key);
                if (r == null) { try { kv.Value.Close(); } catch { } continue; }   // Kanal geloescht
                kv.Value.SetRow(r);
            }
        }

        // ================================================================ 194 Kanal-Suche (Strg+F)

        private TextBox _searchBox;

        /// <summary>Aus ProcessCmdKey (vor allem anderen). true = erledigt.</summary>
        private bool HandleKeys400(Keys keyData)
        {
            if (HandleKeysFinal400(keyData)) return true;   // 4.0 finale: Strg+K, Ueberraschung
            if (_settings.SearchOn && keyData == (Keys.Control | Keys.F)) { OpenSearch400(); return true; }
            return false;
        }

        public void OpenSearch400()
        {
            if (_channelHost == null) return;
            if (_searchBox == null || _searchBox.IsDisposed)
            {
                _searchBox = new TextBox
                {
                    BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Ui(11f),
                    Width = Theme.Px(200), MaxLength = 40,
                };
                _searchBox.TextChanged += (s, e) => UpdateSearch400(_searchBox.Text);
                _searchBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseSearch400(); }
                    else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; JumpToMatch400(); }
                };
                _channelHost.Controls.Add(_searchBox);
            }
            _searchBox.Location = new Point(_channelHost.ClientSize.Width - _searchBox.Width - Theme.Px(12), Theme.Px(1));
            _searchBox.BringToFront();
            _searchBox.Visible = true;
            _searchBox.Focus();
            _searchBox.SelectAll();
            UpdateSearch400(_searchBox.Text);
        }

        private void CloseSearch400()
        {
            if (_searchBox != null) { _searchBox.Visible = false; _searchBox.Text = ""; }
            UpdateSearch400("");
            Focus();
        }

        /// <summary>Passt der Kanal zum Suchtext (Name oder eine seiner Apps)?</summary>
        internal static bool SearchMatches(ChannelConfig c, IEnumerable<string> appNames, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return true;
            q = q.Trim();
            if (c.Name != null && c.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            foreach (var a in appNames ?? Enumerable.Empty<string>()) if (a != null && a.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            foreach (var a in c.Apps ?? new List<string>()) if (a != null && a.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void UpdateSearch400(string q)
        {
            bool active = !string.IsNullOrWhiteSpace(q);
            foreach (var s in _channelStrips)
            {
                bool hit = !active || SearchMatches(s.Config, s.AppNames, q);
                s.SetSearch(active, hit);
            }
        }

        private void JumpToMatch400()
        {
            string q = _searchBox?.Text;
            var first = _channelStrips.FirstOrDefault(s => s.Visible && SearchMatches(s.Config, s.AppNames, q));
            if (first == null) return;
            StripBase.KeyFocus = first;
            _channelHost.ScrollIntoView(first);
        }

        // ================================================================ 196 Teil-Szenen

        private List<KeyValuePair<string, string>> SceneTargets400() =>
            _settings.Channels.Select(c => new KeyValuePair<string, string>(c.Id, c.Name))
                .Concat(_settings.Groups.Select(g => new KeyValuePair<string, string>(g.Id, g.Name + " (Gruppe)"))).ToList();

        /// <summary>Szene auf die gewaehlten Kanaele beschraenken (Ausgangs-Lautstaerke nur auf Wunsch).</summary>
        internal Scene MakePartial400(Scene sc, List<string> only, bool withMaster)
        {
            sc.Channels.RemoveAll(x => !only.Contains(x.Id));
            sc.Only = only.ToList();
            sc.OnlyNoMaster = !withMaster;
            var d = DefaultDevice;
            sc.MasterVolume = withMaster && d != null ? d.Volume : -1f;
            return sc;
        }

        /// <summary>Beim Ueberschreiben: Teil-Szene bleibt Teil-Szene.</summary>
        private Scene KeepPartial400(Scene fresh, Scene old)
        {
            if (old?.Only == null) return fresh;
            return MakePartial400(fresh, old.Only, !old.OnlyNoMaster);
        }

        private void SavePartialScene400(Scene existing = null)
        {
            using (var dlg = new PartialSceneDialog(existing == null ? "Teil-Szene speichern" : "Teil-Szene ändern", existing?.Name ?? "Teil-Szene " + (_settings.Scenes.Count + 1),
                SceneTargets400(), existing?.Only ?? (ICollection<string>)_settings.Channels.Select(c => c.Id).ToList(), existing != null && !existing.OnlyNoMaster))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sc = MakePartial400(CaptureScene(dlg.SceneName, existing?.Id), dlg.Picked, dlg.WithMaster);
                int i = existing == null ? -1 : _settings.Scenes.IndexOf(existing);
                if (i >= 0) _settings.Scenes[i] = sc; else _settings.Scenes.Add(sc);
                ScheduleSave();
                ShowOsd("Teil-Szene gespeichert", sc.Name + " · " + dlg.Picked.Count + (dlg.Picked.Count == 1 ? " Kanal" : " Kanäle"), -1, Theme.LedGreen);
            }
        }

        private void AddPartialScenes400(ToolStripItemCollection items)
        {
            if (!_settings.PartialScenesOn) return;
            items.Add(new ToolStripMenuItem("Teil-Szene speichern (nur bestimmte Kanäle) …", null, (s, e) => BeginInvoke(new Action(() => SavePartialScene400()))));
            if (_settings.Scenes.Count == 0) return;
            var ed = Sub("Teil-Szene ändern (Kanäle wählen, aktuellen Mix speichern)", false);
            foreach (var sc in _settings.Scenes)
            {
                var scene = sc;
                ed.DropDownItems.Add(new ToolStripMenuItem(sc.Name + (sc.Only != null ? "  · " + sc.Only.Count + " Kanäle" : "  · alle"), null, (s, e) => BeginInvoke(new Action(() => SavePartialScene400(scene)))));
            }
            items.Add(ed);
        }

        // ================================================================ Tastenkuerzel

        private void AddHotkeyActionsBedienung400(List<HotkeyAction> list)
        {
            if (_settings.AutomationOn)
            {
                list.Add(new HotkeyAction("auto.rec", "Fader-Automation: Aufnahme an/aus", "Allgemein"));
                list.Add(new HotkeyAction("auto.play", "Fader-Automation: letzte Aufnahme abspielen/stoppen", "Allgemein"));
            }
            if (_settings.SearchOn) list.Add(new HotkeyAction("search", "Kanal suchen", "Allgemein"));
        }

        private bool ExecuteHotkeyBedienung400(string action)
        {
            switch (action)
            {
                case "auto.rec": ToggleAutoRec(); return true;
                case "auto.play":
                    if (_autoPlay != null) StopAutoPlay();
                    else if (_settings.Takes != null && _settings.Takes.Count > 0) PlayTake(_settings.Takes[_settings.Takes.Count - 1], false);
                    return true;
                case "search": RestoreFromTray(); OpenSearch400(); return true;
            }
            return false;
        }

        // ================================================================ Einstellungen

        private void AddControl400(SPage p)
        {
            p.Section("4.0 Bedienung");
            p.Toggle("Fader-Automation", "Fader-Bewegungen aufnehmen und später abspielen – einmal oder in Schleife (z. B. Musik langsam ausblenden, Spiel hoch). SZENEN → Fader-Automation, oder per Tastenkürzel.",
                () => _settings.AutomationOn, v => { _settings.AutomationOn = v; if (!v) { _autoRec = null; StopAutoPlay(); } ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });
            p.Toggle("Mausrad über einem Fenster", () => "Zusatztaste halten und über einem Fenster am Mausrad drehen: nur diese App wird lauter/leiser (2 dB pro Raste) – egal welches Fenster vorne ist. Taste: " + MixerSettings.WinWheelModNames[_settings.WinWheelMod] + ".",
                () => _settings.WinWheelOn, v => { _settings.WinWheelOn = v; ApplyTrayWheel(); ScheduleSave(); });
            p.Choice("Zusatztaste", () => "Welche Taste du beim Drehen hältst.", MixerSettings.WinWheelModNames, () => _settings.WinWheelMod, i => { _settings.WinWheelMod = i; ScheduleSave(); })
                .Enabled = () => _settings.WinWheelOn;
            p.Toggle("Heiße Ecke", () => "Maus ganz in die Bildschirmecke und am Mausrad drehen: " + (HotCornerTargetName() ) + " lauter/leiser – egal welches Fenster vorne ist.",
                () => _settings.HotCornerOn, v => { _settings.HotCornerOn = v; ApplyTrayWheel(); ScheduleSave(); });
            p.Choice("Ecke", () => "In welcher Ecke das Mausrad wirkt.", MixerSettings.CornerNames, () => _settings.HotCorner, i => { _settings.HotCorner = i; ScheduleSave(); })
                .Enabled = () => _settings.HotCornerOn;
            p.Button("Was die Ecke regelt", "MASTER (Windows-Lautstärke) oder ein Kanal.", "Wählen ▾", r => ShowTargetMenu(r, () => _settings.HotCornerTarget, v => { _settings.HotCornerTarget = v; ScheduleSave(); }), HotCornerTargetName)
                .Enabled = () => _settings.HotCornerOn;
            p.Toggle("Apps zwischen Kanälen ziehen", "Ein App-Symbol mit der Maus aus einem Kanal packen und auf einen anderen Kanal ziehen – die App wechselt dorthin.",
                () => _settings.AppDragOn, v => { _settings.AppDragOn = v; ChannelStrip.AppDragOn = v; ScheduleSave(); });
            p.Toggle("Kanal-Fenster", "Rechtsklick auf einen Kanal → „Im eigenen Fenster“: ein kleiner Kanal mit Fader, Pegel und MUTE, immer im Vordergrund (auch über Spielen im Fenster). Am Rand ziehen verschiebt es, Rechtsklick schließt es.",
                () => _settings.ChanWinOn, v => { _settings.ChanWinOn = v; if (!v) CloseChanWins400(); ScheduleSave(); });
            p.Toggle("Eigene Tasten in der Kopfleiste", "Bis zu 6 eigene Tasten oben in der Kopfleiste, z. B. „MIC“ für Mikro stumm oder ein Programm starten. Rechtsklick auf eine Taste: ändern.",
                () => _settings.TopKeysOn, v => { _settings.TopKeysOn = v; RefreshTopKeys400(); ScheduleSave(); });
            p.Button("Tasten", "Tasten anlegen, ändern und entfernen.", "Bearbeiten ▾", r => ShowTopKeysManager400(r), () => { int n = _settings.TopKeys?.Count ?? 0; return n == 0 ? "keine" : n + (n == 1 ? " Taste" : " Tasten"); })
                .Enabled = () => _settings.TopKeysOn;
            p.Toggle("Kanal-Suche (Strg+F)", "Strg+F öffnet ein Suchfeld über den Kanälen: Name oder App eintippen, passende Kanäle leuchten, die anderen werden dunkel. Enter springt hin, Esc schließt.",
                () => _settings.SearchOn, v => { _settings.SearchOn = v; if (!v) CloseSearch400(); ApplyHotkeys(); ScheduleSave(); });
            p.Toggle("Teil-Szenen", "Eine Szene, die nur bestimmte Kanäle lädt – alle anderen bleiben, wie sie sind (z. B. nur Musik und Browser). SZENEN → Teil-Szene speichern.",
                () => _settings.PartialScenesOn, v => { _settings.PartialScenesOn = v; ScheduleSave(); });
        }

        private string HotCornerTargetName()
        {
            var c = _settings.HotCornerTarget == null ? null : _settings.Channels.FirstOrDefault(x => x.Id == _settings.HotCornerTarget);
            return c == null ? "MASTER" : c.Name;
        }
    }
}
