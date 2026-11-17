using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>Funktionen ab Version 2.0: Szenen, Spiel-Profile, Automatiken, Mini-Ansicht, MIDI, Aufnahme.</summary>
    internal sealed partial class MixerForm
    {
        private static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        private readonly Dictionary<string, float[]> _channelPeak = new Dictionary<string, float[]>();

        // Spiel im Hintergrund leiser
        private float _bgQuietApplied;
        private double _nextBgCheck;
        private readonly Dictionary<uint, string> _pidKeyCache = new Dictionary<uint, string>();
        private double _pidCacheClear;

        // Push-to-Talk
        private bool _pttHeld, _micHardMute;
        private double _pttReleaseAt, _pttAppliedAt;
        private int _pttApplied = -1;   // -1 unbekannt, 0 an, 1 stumm
        private string _pttMicId;

        // Sleep-Timer
        private double _sleepEnd;       // 0 = aus
        private double _sleepResetAt;   // nach dem Stummschalten Kanaele wieder normal
        private float _sleepDb;
        private const double SleepFade = 60;
        private int _lastStatusSecond = -1;

        // Aufnahme
        private WavWriter _recWriter;
        private string _recLabel, _lastRecording;
        private double _nextRecCheck;

        // Spiel-Profile
        private HashSet<string> _running;
        private readonly Dictionary<string, double> _lastSeen = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private double _nextProfileCheck;
        private const double GameGoneAfter = 6; // Ladebildschirm/Geraetewechsel: Ton kurz weg zaehlt nicht als "beendet"
        private Scene _profileRestore;
        private string _activeProfile;

        // Headset automatisch
        private HashSet<string> _outSeen, _inSeen;
        private double _autoMicUntil;

        // Mini-Ansicht
        private MiniForm _mini;
        private readonly Dictionary<string, KeyValuePair<float, double>> _miniLocal = new Dictionary<string, KeyValuePair<float, double>>();

        // MIDI
        private MidiHub _midi;
        private MidiDialog _midiDialog;
        private double _nextMidiSync;
        private readonly Dictionary<string, bool> _midiPressed = new Dictionary<string, bool>();

        // Statuszeile
        private string _baseStatus;
        private bool _baseWarning;

        public bool NightMode => _settings.NightMode;

        private ChannelConfig GameChannel =>
            _settings.GameChannel == null ? null : _settings.Channels.FirstOrDefault(c => c.Id == _settings.GameChannel && !c.IsCatchAll);

        private double Now => _clock.Elapsed.TotalSeconds;

        // ================================================================ Laufend (aus Tick)

        private void TickFeatures(double now, float dt)
        {
            UpdatePtt(now);
            if (now >= _nextProfileCheck) { _nextProfileCheck = now + 0.5; CheckProfiles(now); }
            UpdateBgQuiet(now);
            UpdateSleep(now);
            UpdateFades(dt);
            UpdateLoudness();
            UpdateRecording(now);
            PumpMidi(now);
            if (_mini != null && _mini.Visible) _mini.UpdateRows(MiniRows(), dt);
        }

        /// <summary>Bei jeder Aenderung an Apps/Geraeten.</summary>
        private void OnStructureFeatures()
        {
            if (_snap.Structure == "") return; // noch kein echter Zustand
            CheckAutoDevices();
        }

        private void UpdateStatus()
        {
            string s = _baseStatus;
            bool warn = _baseWarning;
            if (_sleepEnd > 0)
            {
                int left = (int)Math.Ceiling(Math.Max(0, _sleepEnd - Now));
                s = "Sleep-Timer " + (left / 60) + ":" + (left % 60).ToString("00") + (string.IsNullOrEmpty(s) ? "" : "  ·  " + s);
                warn = true;
            }
            if (_dimOn)
            {
                s = "DIM −" + (int)Math.Round(DimDb) + " dB" + (string.IsNullOrEmpty(s) ? "" : "  ·  " + s);
                warn = true;
            }
            if (_dpiNote != null)
            {
                s = _dpiNote + (string.IsNullOrEmpty(s) ? "" : "  ·  " + s);
                warn = true;
            }
            if (_noSoundNote != null)   // 3.5: Kein-Ton-Hinweis
            {
                s = _noSoundNote + (string.IsNullOrEmpty(s) ? "" : "  ·  " + s);
                warn = true;
            }
            string n400 = StatusNote400(out bool w400);   // 4.0: Cloud, Panik, Fokus …
            if (n400 != null)
            {
                s = n400 + (string.IsNullOrEmpty(s) ? "" : "  ·  " + s);
                warn |= w400;
            }
            if (_top.Status != s || _top.StatusWarning != warn)
            {
                _top.Status = s;
                _top.StatusWarning = warn;
                _top.Invalidate();
            }
        }

        // ================================================================ Szenen

        private Scene CaptureScene(string name, string id = null)
        {
            var sc = new Scene { Id = id ?? Guid.NewGuid().ToString("N"), Name = name, Channels = new List<SceneChannel>(), DuckOn = _settings.DuckOn, MasterVolume = -1f };
            foreach (var c in _settings.Channels)
                sc.Channels.Add(new SceneChannel
                {
                    Id = c.Id, Fader = c.Fader, Mute = c.Mute, Pan = c.Pan, EqOn = c.EqOn, CompOn = c.CompOn, AutoOn = c.AutoOn,
                    EqLow = c.EqLow, EqMid = c.EqMid, EqHigh = c.EqHigh, Geq = c.Geq == null ? null : (float[])c.Geq.Clone(),
                    StreamDb = c.StreamDb, StreamOff = c.StreamOff,
                });
            foreach (var g in _settings.Groups)
                sc.Channels.Add(new SceneChannel { Id = g.Id, Fader = g.Fader, Mute = g.Mute });
            return sc;
        }

        private void ApplyScene(Scene sc, string osdTitle)
        {
            if (sc == null) return;
            NoteSceneApplied346(sc);   // 3.4.6: Szenen-Tasten leuchten
            NoteScene400(sc);          // 4.0: Zeitleiste, Statistik
            FinishAllFades();
            var xfFrom = CaptureXfadeFrom();   // 3.2: Szenen weich ueberblenden
            // Motorfader: vorher merken, wo die Fader stehen – danach fahren sie sichtbar an die neue Stelle
            var motorFrom = new List<KeyValuePair<StripBase, float>>();
            foreach (var st in AllStrips()) if (st != null) motorFrom.Add(new KeyValuePair<StripBase, float>(st, st.MotorFrom));
            var potFrom = _channelStrips.ToDictionary(s => s, s => s.PotValues);   // 4.0 finale: Motor-Potis
            foreach (var c in _settings.Channels)
            {
                var s = sc.Channels.Find(x => x.Id == c.Id);
                if (s == null) continue;
                c.Fader = Math.Max(0f, Math.Min(1f, s.Fader));
                c.Mute = s.Mute; c.Pan = s.Pan; c.Solo = false;
                c.EqOn = s.EqOn; c.CompOn = s.CompOn; c.AutoOn = s.AutoOn;
                c.EqLow = s.EqLow; c.EqMid = s.EqMid; c.EqHigh = s.EqHigh;
                c.Geq = s.Geq == null ? null : (float[])s.Geq.Clone();
                c.StreamDb = s.StreamDb; c.StreamOff = s.StreamOff;
            }
            foreach (var g in _settings.Groups)
            {
                var s = sc.Channels.Find(x => x.Id == g.Id);
                if (s == null) continue;
                g.Fader = Math.Max(0f, Math.Min(1f, s.Fader));
                g.Mute = s.Mute;
            }
            foreach (var gs in _groupStrips) gs.InvalidateChrome();
            _streamStrip?.InvalidateChrome();
            if (sc.MasterVolume >= 0f && sc.MasterVolume <= 1f)
            {
                var d = DefaultDevice;
                if (d != null) { d.Volume = sc.MasterVolume; Backend.SetDeviceVolume(d.Id, sc.MasterVolume); }
            }
            if (sc.DuckOn != _settings.DuckOn && sc.Only == null)   // 4.0: Teil-Szenen lassen das Ducking in Ruhe
            {
                _settings.DuckOn = sc.DuckOn;
                _top.DuckOn = sc.DuckOn;
            }
            foreach (var s in _channelStrips) s.InvalidateChrome();
            float xfSec = StartSceneXfade(xfFrom);
            StripBase.MotorOverrideSec = xfSec;   // beim Ueberblenden fahren die Fader genauso lange
            try { foreach (var kv in motorFrom) kv.Key.StartMotor(kv.Value); }
            finally { StripBase.MotorOverrideSec = 0f; }
            foreach (var kv in potFrom) kv.Key.StartPots(kv.Value);   // 4.0 finale
            _top.Invalidate();
            PushPolicy();
            ScheduleSave();
            if (osdTitle != null) ShowOsd(osdTitle, sc.Name, -1, Theme.Accent);
        }

        private ToolStripMenuItem Sub(string text, bool checkMargin = true, Image image = null)
        {
            var it = new ToolStripMenuItem(text, image);
            it.DropDown.Renderer = _renderer;
            ((ToolStripDropDownMenu)it.DropDown).ShowImageMargin = !checkMargin;
            ((ToolStripDropDownMenu)it.DropDown).ShowCheckMargin = checkMargin;
            it.DropDown.BackColor = Theme.MenuBg;
            it.DropDown.ForeColor = Theme.Text;
            it.DropDown.Font = Theme.Ui(12f);
            return it;
        }

        private void ShowScenesMenu()
        {
            var m = NewMenu();
            m.ShowImageMargin = true;
            m.Items.Add(Header("Szenen – Mix mit einem Klick laden"));
            m.Items.Add(new ToolStripSeparator());
            AddSceneLoadItems(m.Items);
            HookScenePreview(m.Items);   // 3.5: Maus ueber einer Szene = Vorschau
            m.Closed += (s2, e2) => ClearScenePreview();
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Aktuellen Mix als neue Szene speichern …", null, (s, e) => BeginInvoke(new Action(SaveNewScene))));
            AddPartialScenes400(m.Items);   // 4.0: Teil-Szenen
            AddGallery400(m.Items);         // 4.0 finale: Szenen-Galerie
            if (_settings.Scenes.Count > 0)
            {
                var over = Sub("Szene mit aktuellem Mix überschreiben", false);
                var ren = Sub("Szene umbenennen", false);
                var del = Sub("Szene löschen", false);
                foreach (var sc in _settings.Scenes)
                {
                    var scene = sc;
                    over.DropDownItems.Add(new ToolStripMenuItem(sc.Name, null, (s, e) =>
                    {
                        int i = _settings.Scenes.IndexOf(scene);
                        if (i < 0) return;
                        _settings.Scenes[i] = KeepPartial400(CaptureScene(scene.Name, scene.Id), scene);   // 4.0: Teil-Szene bleibt Teil-Szene
                        ScheduleSave();
                        ShowOsd("Szene gespeichert", scene.Name, -1, Theme.LedGreen);
                    }));
                    ren.DropDownItems.Add(new ToolStripMenuItem(sc.Name, null, (s, e) => BeginInvoke(new Action(() =>
                    {
                        using (var dlg = new InputDialog("Szene umbenennen", "Neuer Name:", scene.Name))
                            if (dlg.ShowDialog(this) == DialogResult.OK) { scene.Name = dlg.Value; ScheduleSave(); }
                    }))));
                    del.DropDownItems.Add(new ToolStripMenuItem(sc.Name, null, (s, e) => BeginInvoke(new Action(() =>
                    {
                        if (MessageBox.Show(this, "Szene »" + scene.Name + "« löschen?", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                        _settings.Scenes.Remove(scene);
                        _settings.GameProfiles.RemoveAll(g => g.SceneId == scene.Id);
                        _settings.Schedules.RemoveAll(x => x.SceneId == scene.Id);
                        _settings.Hotkeys.RemoveAll(h => h.Action == "scene:" + scene.Id);
                        _settings.Midi.RemoveAll(b => b.Target == "scene:" + scene.Id);
                        ApplyHotkeys();
                        ScheduleSave();
                    }))));
                }
                m.Items.Add(over);
                m.Items.Add(ren);
                m.Items.Add(del);
                m.Items.Add(new ToolStripMenuItem("Hotkeys für Szenen …", null, (s, e) => BeginInvoke(new Action(ShowHotkeyDialog))));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(BuildProfileMenu());
            int active = _settings.Schedules.Count(x => !x.Off);
            m.Items.Add(new ToolStripMenuItem("Szenen nach Uhrzeit …" + (active > 0 ? "  (" + active + " aktiv)" : ""), null, (s, e) => BeginInvoke(new Action(ShowScheduleDialog)))
            { Checked = active > 0, Enabled = _settings.Scenes.Count > 0, ToolTipText = _settings.Scenes.Count > 0 ? null : "Zuerst eine Szene speichern." });
            AddSceneMenu350(m);   // 3.5: Ueberblendzeit pro Szene
            AddAutomationMenu400(m.Items);   // 4.0: Fader-Automation
            var b = _top.ScenesButtonBounds;
            m.Show(_top.PointToScreen(new Point(b.X, b.Bottom + Theme.Px(4))));
        }

        private void ShowScheduleDialog()
        {
            using (var dlg = new ScheduleDialog(_settings.Scenes, _settings.Schedules))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                _settings.Schedules = dlg.Result;
                _lastScheduleStamp = ScheduleStamp(DateTime.Now);   // nicht sofort ausloesen, was gerade eingetragen wurde
                ScheduleSave();
                int n = dlg.Result.Count(x => !x.Off);
                ShowOsd("Szenen nach Uhrzeit", n == 0 ? "keine aktiv" : n + (n == 1 ? " Eintrag aktiv" : " Einträge aktiv"), -1, Theme.Accent);
            }
        }

        private int _lastScheduleStamp = -1;
        private static int ScheduleStamp(DateTime t) => t.DayOfYear * 1440 + t.Hour * 60 + t.Minute;

        /// <summary>Aus dem UI-Timer: Szene laden, wenn ein Eintrag fuer diese Minute faellig ist (einmal pro Minute).</summary>
        private void CheckSchedules()
        {
            if (_settings.Schedules.Count == 0) return;
            var t = DateTime.Now;
            int stamp = ScheduleStamp(t);
            if (stamp == _lastScheduleStamp) return;
            _lastScheduleStamp = stamp;
            int minute = t.Hour * 60 + t.Minute, bit = SceneSchedule.DayBit(t.DayOfWeek);
            foreach (var e in _settings.Schedules.ToList())
            {
                if (e.Off || e.Minute != minute || (e.Days & bit) == 0) continue;
                var sc = _settings.Scenes.FirstOrDefault(x => x.Id == e.SceneId);
                if (sc != null) ApplyScene(sc, "Szene (" + e.TimeText + " Uhr)");
            }
        }

        private void AddSceneLoadItems(ToolStripItemCollection items)
        {
            if (_settings.Scenes.Count == 0) items.Add(Hint("Noch keine Szene gespeichert."));
            foreach (var sc in _settings.Scenes)
            {
                var scene = sc;
                var it = new ToolStripMenuItem(sc.Name + (sc.Only != null ? "   · Teil" : ""), Glyph("▶"), (s, e) => ApplyScene(scene, "Szene"));   // 4.0: Teil-Szene
                string hk = HotkeyManager.Format(HotkeyKeys("scene:" + sc.Id));
                if (hk.Length > 0) it.ShortcutKeyDisplayString = hk;
                items.Add(it);
            }
        }

        private void SaveNewScene()
        {
            string suggestion = "Szene " + (_settings.Scenes.Count + 1);
            using (var dlg = new InputDialog("Neue Szene", "Name der Szene (z. B. Zocken, Musik, Nacht):", suggestion))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sc = CaptureScene(dlg.Value);
                _settings.Scenes.Add(sc);
                ScheduleSave();
                ShowOsd("Szene gespeichert", sc.Name, -1, Theme.LedGreen);
            }
        }

        // ================================================================ Profil pro Spiel

        private string AppDisplayName(string key)
        {
            if (_appsByKey.TryGetValue(key, out var a)) return a.Name;
            return FindKnown(key)?.Name ?? PrettyKey(key);
        }

        private ToolStripMenuItem BuildProfileMenu()
        {
            var prof = Sub(_settings.AppProfilesOn ? "Profil pro Spiel / App" : "Profil pro Spiel");
            prof.DropDownItems.Add(Hint(_settings.AppProfilesOn ? "Startet das Spiel oder die App, wird ihre Szene geladen." : "Startet das Spiel, wird seine Szene geladen."));
            prof.DropDownItems.Add(new ToolStripSeparator());

            var games = new List<string>();
            foreach (var g in _settings.GameProfiles) if (!games.Contains(g.AppKey)) games.Add(g.AppKey);
            var gc = GameChannel;
            if (gc != null) foreach (var k in gc.Apps) if (!games.Contains(k)) games.Add(k);
            foreach (var a in _snap.Apps)
                if (!games.Contains(a.Key) && GameDetector.IsGame(a.Key, a.Path)) games.Add(a.Key);
            int gameCount = games.Count;
            if (_settings.AppProfilesOn)   // 4.3: auch andere Apps (laufende und die in den Kanaelen)
            {
                var more = new List<string>();
                foreach (var a in _snap.Apps) if (a.Key != AppKeys.System) more.Add(a.Key);
                foreach (var c in _settings.Channels) more.AddRange(c.Apps);
                foreach (var k in more.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => AppDisplayName(k), StringComparer.CurrentCultureIgnoreCase))
                    if (!games.Contains(k, StringComparer.OrdinalIgnoreCase) && k != AppKeys.System) games.Add(k);
            }

            if (games.Count == 0)
                prof.DropDownItems.Add(Hint("Noch kein Spiel bekannt – Spiel einmal starten."));
            else if (_settings.Scenes.Count == 0)
                prof.DropDownItems.Add(Hint("Zuerst eine Szene speichern."));
            else
            {
                for (int gi = 0; gi < games.Count; gi++)
                {
                    string k = games[gi];
                    if (gi == gameCount && gameCount < games.Count) { if (gi > 0) prof.DropDownItems.Add(new ToolStripSeparator()); prof.DropDownItems.Add(Hint("Andere Apps")); }   // 4.3
                    var known = FindKnown(k);
                    var cur = _settings.GameProfiles.FirstOrDefault(g => g.AppKey == k);
                    var curScene = cur == null ? null : _settings.Scenes.FirstOrDefault(s => s.Id == cur.SceneId);
                    var item = Sub(AppDisplayName(k) + (curScene != null ? "   → " + curScene.Name : ""), true);
                    item.Image = IconFor(k, AppDisplayName(k), known?.Path ?? (_appsByKey.TryGetValue(k, out var ap) ? ap.Path : null));
                    item.DropDownItems.Add(new ToolStripMenuItem("Keine Szene", null, (s, e) =>
                    {
                        _settings.GameProfiles.RemoveAll(g => g.AppKey == k);
                        ScheduleSave();
                    }) { Checked = curScene == null });
                    item.DropDownItems.Add(new ToolStripSeparator());
                    foreach (var sc in _settings.Scenes)
                    {
                        var scene = sc;
                        item.DropDownItems.Add(new ToolStripMenuItem(sc.Name, null, (s, e) =>
                        {
                            _settings.GameProfiles.RemoveAll(g => g.AppKey == k);
                            _settings.GameProfiles.Add(new GameProfile { AppKey = k, SceneId = scene.Id });
                            ScheduleSave();
                            // laeuft das Spiel schon? Dann gleich laden
                            if (_appsByKey.ContainsKey(k))
                            {
                                if (_activeProfile == null) _profileRestore = CaptureScene("vorher");
                                _activeProfile = k;
                                ApplyScene(scene, "Profil · " + AppDisplayName(k));
                            }
                        }) { Checked = curScene == sc });
                    }
                    prof.DropDownItems.Add(item);
                }
            }
            prof.DropDownItems.Add(new ToolStripSeparator());
            prof.DropDownItems.Add(new ToolStripMenuItem("Nach dem Spiel vorherigen Mix wiederherstellen", null, (s, e) =>
            {
                _settings.ProfileNoRestore = !_settings.ProfileNoRestore;
                ScheduleSave();
            }) { Checked = !_settings.ProfileNoRestore });
            return prof;
        }

        private void CheckProfiles(double t)
        {
            if (_snap.Structure == "") return;
            foreach (var a in _snap.Apps) _lastSeen[a.Key] = t;
            foreach (var k in _lastSeen.Where(kv => t - kv.Value > 120).Select(kv => kv.Key).ToList()) _lastSeen.Remove(k);
            var now = new HashSet<string>(_lastSeen.Where(kv => t - kv.Value < GameGoneAfter).Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
            var before = _running ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _running = now;

            // Spiel beendet?
            if (_activeProfile != null && !now.Contains(_activeProfile))
            {
                string ended = _activeProfile;
                _activeProfile = null;
                // laeuft noch ein anderes Spiel mit Profil? Dann dessen Szene
                var other = _settings.GameProfiles.FirstOrDefault(g => now.Contains(g.AppKey));
                if (other != null)
                {
                    _activeProfile = other.AppKey;
                    ApplyScene(_settings.Scenes.FirstOrDefault(s => s.Id == other.SceneId), "Profil · " + AppDisplayName(other.AppKey));
                }
                else
                {
                    if (!_settings.ProfileNoRestore && _profileRestore != null)
                    {
                        ApplyScene(_profileRestore, null);
                        ShowOsd(AppDisplayName(ended) + " beendet", "Mix wiederhergestellt", -1, Theme.TextDim);
                    }
                    _profileRestore = null;
                }
            }

            // Spiel gestartet?
            foreach (var g in _settings.GameProfiles)
            {
                if (!now.Contains(g.AppKey) || before.Contains(g.AppKey) || g.AppKey == _activeProfile) continue;
                var sc = _settings.Scenes.FirstOrDefault(s => s.Id == g.SceneId);
                if (sc == null) continue;
                if (_activeProfile == null) _profileRestore = CaptureScene("vorher");
                _activeProfile = g.AppKey;
                ApplyScene(sc, "Profil · " + AppDisplayName(g.AppKey));
                break;
            }
        }

        // ================================================================ Spiele automatisch erkennen

        private void DetectGames()
        {
            if (_settings.NoAutoGames || _snap.Structure == "") return;
            var gc = GameChannel;
            if (gc == null) return;
            var added = new List<string>();
            foreach (var a in _snap.Apps)
            {
                if (_appToChannel.ContainsKey(a.Key)) continue;
                if (_settings.NotGames.Contains(a.Key, StringComparer.OrdinalIgnoreCase)) continue;
                if (!GameDetector.IsGame(a.Key, a.Path)) continue;
                gc.Apps.Add(a.Key.ToLowerInvariant());
                _appToChannel[a.Key] = gc;
                Remember(a.Key, a.Name, a.Path);
                added.Add(a.Name);
            }
            if (added.Count == 0) return;
            PushPolicy();
            ScheduleSave();
            ShowOsd("Spiel erkannt → " + gc.Name, string.Join(", ", added), -1, Color.FromArgb(gc.Color));
        }

        /// <summary>Wird eine App vom Nutzer aus dem Spiel-Kanal genommen, gilt sie nicht mehr als Spiel.</summary>
        private void NoteGameAssignment(string key, ChannelConfig from, ChannelConfig to)
        {
            var gc = GameChannel;
            if (gc == null) return;
            if (to == gc) _settings.NotGames.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            else if ((from == gc || to == null || to.IsCatchAll) && !_settings.NotGames.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                string path = _appsByKey.TryGetValue(key, out var a) ? a.Path : FindKnown(key)?.Path;
                if (from == gc || GameDetector.IsGame(key, path)) _settings.NotGames.Add(key);
            }
        }

        // ================================================================ Spiel im Hintergrund leiser

        private string KeyOfPid(uint pid)
        {
            if (Now > _pidCacheClear) { _pidKeyCache.Clear(); _pidCacheClear = Now + 30; }
            if (_pidKeyCache.TryGetValue(pid, out var k)) return k;
            string path = Win32.GetProcessPath(pid);
            k = path != null ? AppKeys.FromExe(path) : null;
            _pidKeyCache[pid] = k;
            return k;
        }

        private void UpdateBgQuiet(double now)
        {
            if (now < _nextBgCheck) return;
            _nextBgCheck = now + 0.25;
            bool want = false;
            var gc = GameChannel;
            if (!_settings.BgQuietOff && gc != null && Win32.IsWindows && !_demo)
            {
                bool gameRunning = gc.Apps.Any(k => _appsByKey.TryGetValue(k, out var a) && a.Active);
                if (gameRunning)
                {
                    uint pid = Win32.ForegroundPid();
                    if (pid != 0 && pid != OwnPid && (_mini == null || !_mini.Visible || !_mini.ContainsFocus))
                    {
                        string key = KeyOfPid(pid);
                        want = key != null && !gc.Apps.Contains(key, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            float target = want ? _settings.BgQuietDb : 0f;
            if (target != _bgQuietApplied)
            {
                _bgQuietApplied = target;
                PushPolicy();
            }
            foreach (var s in _channelStrips) s.SetBgQuiet((want && s.Config == gc) || BgAppsQuiet(s.Config));   // 3.2: auch Hintergrund-Apps
        }

        // ================================================================ Push-to-Talk

        private void UpdatePtt(double now)
        {
            int mode = _settings.PttMode;
            if (mode == 0 || _settings.PttKeys == 0 || _snap.Mic == null)
            {
                _pttHeld = false;
                if (mode == 0) _pttApplied = -1;
                return;
            }
            if (_snap.Mic.Id != _pttMicId) { _pttMicId = _snap.Mic.Id; _pttApplied = -1; } // anderes Mikro
            bool down = Win32.IsKeyDown(_settings.PttKeys);
            if (mode == 1)
            {
                if (down) _pttReleaseAt = now + 0.25;
                _pttHeld = down || now < _pttReleaseAt;
            }
            else _pttHeld = down;
            bool mute = _micHardMute || (mode == 1 ? !_pttHeld : _pttHeld);
            int want = mute ? 1 : 0;
            // auch korrigieren, wenn eine andere App das Mikro umgeschaltet hat
            bool drift = !PathMute && _pttApplied == want && now - _pttAppliedAt > 0.5 && _snap.Mic.Mute != mute;
            if (want != _pttApplied || drift)
            {
                _pttApplied = want;
                _pttAppliedAt = now;
                ApplyMicMute(mute);
                if (!PathMute) _snap.Mic.Mute = mute;
            }
        }

        public void ToggleMicMute(bool osd)
        {
            if (_settings.PttMode != 0)
            {
                _micHardMute = !_micHardMute;
                _pttApplied = -1;
                UpdatePtt(Now);
                _mic.InvalidateChrome();
                PlayMuteBeep(_micHardMute);
                if (osd) ShowOsd("Mikrofon", _micHardMute ? "STUMM" : (_settings.PttMode == 1 ? "PUSH-TO-TALK" : "PUSH-TO-MUTE"), -1,
                    _micHardMute ? Theme.MuteRed : Theme.Accent);
                return;
            }
            if (PathMute)
            {
                SetPathMuted(!_pathMuted);
                _mic.InvalidateChrome();
                PlayMuteBeep(_pathMuted);
                if (osd) ShowOsd("Mikrofon (Sprach-Ausgang)", _pathMuted ? "STUMM" : "AN", -1, _pathMuted ? Theme.MuteRed : Theme.LedGreen);
                return;
            }
            var m = _snap.Mic;
            if (m == null) { if (osd) ShowOsd("Mikrofon", "Kein Mikrofon", -1, Theme.MuteRed); return; }
            m.Mute = !m.Mute;
            Backend.SetMicMute(m.Mute);
            _mic.InvalidateChrome();
            PlayMuteBeep(m.Mute);
            if (osd) ShowOsd("Mikrofon", m.Mute ? "STUMM" : "AN", -1, m.Mute ? Theme.MuteRed : Theme.LedGreen);
        }

        private System.Media.SoundPlayer _beepPlayer;

        /// <summary>Hinweiston: absteigend = stumm, aufsteigend = an. Nur auf dem eigenen Ausgang.</summary>
        private void PlayMuteBeep(bool muted)
        {
            if (_settings.MuteBeepOff) return;
            var clip = muted ? Beeps.MicOff : Beeps.MicOn;
            float gain = Beeps.GainOf(_settings.MuteBeepLevel);
            try
            {
                if (_snap.EqSupported || _demo) Backend.PlaySound(clip, gain, true, false, false);
                else if (Win32.IsWindows)
                {
                    // ohne EQ-Mischer (aelteres Windows): ueber die Windows-Wiedergabe
                    _beepPlayer?.Dispose();
                    _beepPlayer = new System.Media.SoundPlayer(new System.IO.MemoryStream(Beeps.ToWav(clip, gain)));
                    _beepPlayer.Play();
                }
            }
            catch { }
        }

        public void ShowPttDialog()
        {
            using (var dlg = new PttDialog(_settings.PttMode, _settings.PttKeys))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int old = _settings.PttMode;
                _settings.PttMode = dlg.Mode;
                _settings.PttKeys = dlg.Key;
                _micHardMute = false;
                _pttApplied = -1;
                if (dlg.Mode == 0 && old != 0) // wieder normal
                {
                    if (PathMute) SetPathMuted(false);
                    else Backend.SetMicMute(false);
                }
                _mic.InvalidateChrome();
                ScheduleSave();
            }
        }

        // ================================================================ Nachtmodus / Gehoerschutz / Sanft

        private void ToggleNight(bool osd)
        {
            if (!_settings.NightMode && !_snap.EqSupported)
            {
                string msg = _snap.EqStatus ?? "Der Nachtmodus braucht Windows 10 Version 2004 oder neuer.";
                if (osd) ShowOsd("Nachtmodus", "nicht verfügbar", -1, Theme.MuteRed);
                else MessageBox.Show(this, msg, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.NightMode = !_settings.NightMode;
            _top.NightOn = _settings.NightMode;
            _top.Invalidate();
            foreach (var s in _channelStrips) s.InvalidateChrome();
            PushPolicy();
            ScheduleSave();
            if (osd) ShowOsd("Nachtmodus", _settings.NightMode ? "AN" : "AUS", -1, _settings.NightMode ? Color.FromArgb(0x7a, 0xa8, 0xff) : Theme.TextDim);
            if (_settings.PultDimMode == 3) UpdatePultDim(true);   // Pult dunkler "mit NACHT"
        }

        private void SetHearingCap(float db)
        {
            _settings.HearingCap = db;
            Backend.SetHearingCap(db);
            ScheduleSave();
        }

        private static readonly KeyValuePair<float, string>[] HearingLevels =
        {
            new KeyValuePair<float, string>(0f, "Aus"),
            new KeyValuePair<float, string>(-3f, "Leicht – höchstens −3 dB"),
            new KeyValuePair<float, string>(-6f, "Mittel – höchstens −6 dB"),
            new KeyValuePair<float, string>(-10f, "Stark – höchstens −10 dB"),
            new KeyValuePair<float, string>(-15f, "Sehr stark – höchstens −15 dB"),
        };

        // ================================================================ Headset automatisch umschalten

        private void CheckAutoDevices()
        {
            var outs = new HashSet<string>(_snap.Devices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            var ins = new HashSet<string>(_snap.CaptureDevices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            var prevOut = _outSeen; var prevIn = _inSeen;
            _outSeen = outs; _inSeen = ins;
            string dev = _settings.AutoDevice;
            if (prevOut == null || !_settings.AutoSwitchOn || dev == null) return;

            bool appeared = outs.Contains(dev) && !prevOut.Contains(dev);
            bool vanished = !outs.Contains(dev) && prevOut.Contains(dev);
            if (appeared)
            {
                Backend.SetDefaultDevice(dev);
                if (_settings.AutoMic != null)
                {
                    if (ins.Contains(_settings.AutoMic)) Backend.SetDefaultDevice(_settings.AutoMic);
                    else _autoMicUntil = Now + 8; // Mikro taucht oft etwas spaeter auf
                }
                ShowOsd("Headset angeschlossen", _settings.AutoDeviceName ?? "Ausgabe gewechselt", -1, Theme.LedGreen);
            }
            else if (vanished)
            {
                _autoMicUntil = 0;
                string fb = _settings.AutoFallback;
                if (fb != null && outs.Contains(fb)) Backend.SetDefaultDevice(fb);
                if (_settings.AutoMicFallback != null && ins.Contains(_settings.AutoMicFallback)) Backend.SetDefaultDevice(_settings.AutoMicFallback);
                ShowOsd("Headset getrennt", fb != null && outs.Contains(fb) ? (_settings.AutoFallbackName ?? "zurück") : "Windows-Standard", -1, Theme.TextDim);
            }
            else if (_autoMicUntil > Now && _settings.AutoMic != null && ins.Contains(_settings.AutoMic) && prevIn != null && !prevIn.Contains(_settings.AutoMic))
            {
                _autoMicUntil = 0;
                Backend.SetDefaultDevice(_settings.AutoMic);
            }
        }

        private void ShowHeadsetDialog()
        {
            using (var dlg = new HeadsetDialog(_settings, _snap.Devices, _snap.CaptureDevices))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.AutoSwitchOn = dlg.On;
                _settings.AutoDevice = dlg.Device; _settings.AutoDeviceName = dlg.DeviceName;
                _settings.AutoMic = dlg.Mic; _settings.AutoMicName = dlg.MicName;
                _settings.AutoFallback = dlg.Fallback; _settings.AutoFallbackName = dlg.FallbackName;
                _settings.AutoMicFallback = dlg.MicFallback; _settings.AutoMicFallbackName = dlg.MicFallbackName;
                ScheduleSave();
            }
        }

        // ================================================================ Sleep-Timer

        private void StartSleep(int minutes)
        {
            _sleepEnd = Now + minutes * 60;
            _sleepResetAt = 0;
            if (_sleepDb != 0) { _sleepDb = 0; PushPolicy(); }
            ShowOsd("Sleep-Timer", minutes + " Minuten", -1, Theme.Accent);
            UpdateStatus();
        }

        private void StopSleep()
        {
            _sleepEnd = 0;
            _sleepResetAt = 0;
            if (_sleepDb != 0) { _sleepDb = 0; PushPolicy(); }
            UpdateStatus();
        }

        private void UpdateSleep(double now)
        {
            if (_sleepResetAt > 0 && now >= _sleepResetAt)
            {
                _sleepResetAt = 0;
                if (_sleepDb != 0) { _sleepDb = 0; PushPolicy(); }
            }
            if (_sleepEnd <= 0) return;
            double left = _sleepEnd - now;
            if (left <= 0)
            {
                // Ton aus: Ausgang(e) stumm; die Kanaele erst danach wieder auf normal (kein kurzes Aufblitzen)
                if (MultiOutput) foreach (var d in _snap.Devices) Backend.SetDeviceMute(d.Id, true);
                else if (DefaultDevice != null) Backend.SetDeviceMute(DefaultDevice.Id, true);
                _sleepEnd = 0;
                _sleepResetAt = now + 1.5;
                ShowOsd("Sleep-Timer", "Gute Nacht – Ton aus", -1, Theme.TextDim);
                UpdateStatus();
                SleepEnded400();   // 4.0: Bildschirm aus / Energiesparen / Herunterfahren
                return;
            }
            float db = left < SleepFade ? (float)(-45.0 * (1 - left / SleepFade)) : 0f;
            db = (float)Math.Round(db * 2) / 2f;
            if (db != _sleepDb) { _sleepDb = db; PushPolicy(); }
            int sec = (int)left;
            if (sec != _lastStatusSecond) { _lastStatusSecond = sec; UpdateStatus(); }
        }

        private ToolStripMenuItem BuildSleepMenu()
        {
            var sleep = Sub(_sleepEnd > 0 ? "Sleep-Timer (läuft)" : "Sleep-Timer");
            sleep.DropDownItems.Add(Hint("Blendet am Ende eine Minute lang aus, dann Ton aus."));
            sleep.DropDownItems.Add(new ToolStripMenuItem("Aus", null, (s, e) => StopSleep()) { Checked = _sleepEnd <= 0 });
            foreach (int min in new[] { 15, 30, 45, 60, 90, 120 })
            {
                int mm = min;
                sleep.DropDownItems.Add(new ToolStripMenuItem(min + " Minuten", null, (s, e) => StartSleep(mm)));
            }
            sleep.DropDownItems.Add(new ToolStripSeparator());
            AddSleepEnd400(sleep.DropDownItems);   // 4.0
            return sleep;
        }

        // ================================================================ Aufnahme

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        private string RecordTargetLabel(out string channelId, out string deviceId)
        {
            channelId = null; deviceId = null;
            string t = _settings.RecordTarget;
            if (t == "multi" && _snap.EqSupported) return "Mehrspur";   // 2.19
            if (t == "stream" && _settings.StreamOn && _snap.EqSupported) { channelId = DspMixer.StreamRecordKey; return "Stream-Mix"; }
            if (t != null && t.StartsWith("ch:"))
            {
                var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == t);
                if (c != null && _snap.EqSupported) { channelId = c.Id; return c.Name; }
            }
            else if (t != null && t.StartsWith("dev:"))
            {
                var d = _snap.Devices.FirstOrDefault(x => "dev:" + x.Id == t);
                if (d != null) { deviceId = d.Id; return d.Name; }
            }
            return "Alles";
        }

        private void ToggleRecording()
        {
            if (_recWriter != null) StopRecording(true);
            else StartRecording();
        }

        private void StartRecording()
        {
            if (_recWriter != null) return;
            string label = RecordTargetLabel(out string channelId, out string deviceId);
            if (label == "Mehrspur") { StartMultitrackRecording(); return; }   // 2.19
            string baseName = "Mischpult " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + " " + SafeName(label);
            WavWriter w;
            try { w = new WavWriter(_settings.RecordFolder, baseName, DspMixer.Rate); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die Aufnahme konnte nicht gestartet werden:\n" + ex.Message + "\n\nOrdner: " + _settings.RecordFolder,
                    "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string err = Backend.StartRecording(channelId, deviceId, w);
            if (err != null)
            {
                w.Dispose();
                try { File.Delete(w.CurrentFile); } catch { }
                MessageBox.Show(this, err, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _recWriter = w;
            _recLabel = label;
            _top.RecOn = true;
            _top.RecText = "REC 0:00";
            _top.Invalidate();
            _nextRecCheck = 0;
            ShowOsd("Aufnahme läuft", label, -1, Theme.MuteRed);
        }

        private void StopRecording(bool osd)
        {
            var w = _recWriter;
            if (w == null) return;
            _recWriter = null;
            try { Backend.StopRecording(); } catch { }
            var tracks = StopMultitrackTracks();   // 2.19
            w.Dispose();
            _lastRecording = tracks != null ? Path.GetDirectoryName(w.CurrentFile) : w.CurrentFile;
            _top.RecOn = false;
            _top.Invalidate();
            FinishRecording(w, tracks, osd);   // 2.19: FLAC/MP3
        }

        private void UpdateRecording(double now)
        {
            var w = _recWriter;
            if (w == null || now < _nextRecCheck) return;
            _nextRecCheck = now + 0.25;
            int sec = (int)w.Seconds;
            string text = "REC " + (sec >= 3600 ? (sec / 3600) + ":" + (sec / 60 % 60).ToString("00") : (sec / 60).ToString()) + ":" + (sec % 60).ToString("00");
            bool blink = ((int)(now * 2)) % 2 == 0;
            if (text != _top.RecText || blink != _top.RecBlink)
            {
                _top.RecText = text;
                _top.RecBlink = blink;
                _top.Invalidate();
            }
            string err = w.Error ?? Backend.RecordingError;
            if (err != null)
            {
                StopRecording(false);
                ShowOsd("Aufnahme angehalten", err, -1, Theme.MuteRed);
            }
        }

        private void ShowRecordMenu()
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Aufnahme als " + RecordingExport.FormatNames[_settings.RecFormat] + "-Datei"));
            m.Items.Add(new ToolStripSeparator());
            string cur = _settings.RecordTarget;
            Action<string> set = t => { _settings.RecordTarget = t; ScheduleSave(); };
            m.Items.Add(new ToolStripMenuItem("Alles, was du hörst" + (MultiOutput ? " (Standardgerät)" : ""), null, (s, e) => set(null))
            { Checked = cur == null || RecordTargetLabel(out _, out _) == "Alles" });
            m.Items.Add(new ToolStripMenuItem("Mehrspur: jeder Kanal eine eigene Datei", null, (s, e) => set("multi"))   // 2.19
            { Checked = cur == "multi", Enabled = _snap.EqSupported || _demo, ToolTipText = "Plus eine Spur mit allem – zum Nachbearbeiten (z. B. in Audacity)" });
            if (MultiOutput)
                foreach (var d in _snap.Devices)
                {
                    string t = "dev:" + d.Id;
                    m.Items.Add(new ToolStripMenuItem("Ausgang: " + d.FullName, null, (s, e) => set(t)) { Checked = cur == t });
                }
            if (_settings.StreamOn)
                m.Items.Add(new ToolStripMenuItem("Stream-Mix", null, (s, e) => set("stream")) { Checked = cur == "stream", Enabled = _snap.EqSupported });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("Nur ein Kanal"));
            foreach (var c in _settings.Channels)
            {
                string t = "ch:" + c.Id;
                m.Items.Add(new ToolStripMenuItem("Kanal: " + c.Name, null, (s, e) => set(t)) { Checked = cur == t, Enabled = _snap.EqSupported });
            }
            if (!_snap.EqSupported) m.Items.Add(Hint("Kanal-Aufnahme braucht Windows 10 Version 2004+"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(RecFormatSub());   // 2.19
            m.Items.Add(new ToolStripMenuItem(_recWriter != null ? "Aufnahme stoppen" : "Aufnahme starten", null, (s, e) => ToggleRecording()));
            AddRecPlanItems400(m.Items);   // 4.0: geplante Aufnahme
            if (_lastRecording != null && File.Exists(_lastRecording))
                m.Items.Add(new ToolStripMenuItem("Letzte Aufnahme im Ordner zeigen", null, (s, e) =>
                {
                    try { Process.Start("explorer.exe", "/select,\"" + _lastRecording + "\""); } catch { }
                }));
            m.Items.Add(new ToolStripMenuItem("Aufnahme-Ordner öffnen", null, (s, e) =>
            {
                try { Directory.CreateDirectory(_settings.RecordFolder); Process.Start("explorer.exe", "\"" + _settings.RecordFolder + "\""); } catch { }
            }));
            m.Items.Add(new ToolStripMenuItem("Ordner ändern …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Ordner für Aufnahmen", SelectedPath = _settings.RecordFolder, ShowNewFolderButton = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { _settings.RecordFolder = dlg.SelectedPath; ScheduleSave(); }
            }))));
            m.Items.Add(Hint("Ordner: " + _settings.RecordFolder));
            var b = _top.RecButtonBounds;
            m.Show(_top.PointToScreen(new Point(b.X, b.Bottom + Theme.Px(4))));
        }

        // ================================================================ Mini-Ansicht

        public void SetMini(bool on)
        {
            _settings.MiniOn = on;
            ScheduleSave();
            if (on)
            {
                if (_mini == null || _mini.IsDisposed)
                {
                    _mini = new MiniForm(this);
                    PlaceMini();
                }
                _mini.UpdateRows(MiniRows(), 0f);
                _mini.Show();
                if (Visible) { RememberWindow(); Hide(); }
            }
            else
            {
                _mini?.Hide();
                if (!Visible) RestoreFromTray();
            }
        }

        private void PlaceMini()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = new Point(_settings.MiniX, _settings.MiniY);
            bool ok = _settings.MiniX != int.MinValue && Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(p.X + Theme.Px(40), p.Y + Theme.Px(10))));
            _mini.Location = ok ? p : new Point(wa.Right - _mini.Width - Theme.Px(16), wa.Bottom - Theme.Px(320));
        }

        public void ShowFromMini()
        {
            _mini?.Hide();
            _settings.MiniOn = false;
            ScheduleSave();
            RestoreFromTray();
        }

        public void MiniMoved(Point p)
        {
            _settings.MiniX = p.X;
            _settings.MiniY = p.Y;
            ScheduleSave();
        }

        private List<MiniRow> MiniRows()
        {
            var rows = new List<MiniRow>();
            double now = Now;
            float Local(string id, float v) => _miniLocal.TryGetValue(id, out var l) && now - l.Value < 0.7 ? l.Key : v;
            var gc = GameChannel;
            foreach (var c in _settings.Channels)
            {
                if (c.Hidden) continue;
                _channelPeak.TryGetValue(c.Id, out var pk);
                string id = "ch:" + c.Id;
                bool ducked = _duckActive && _settings.DuckTargets.Contains(c.Id) && !_settings.IsDuckTrigger(c.Id);
                rows.Add(new MiniRow
                {
                    Id = id, Name = c.Name, Color = Color.FromArgb(c.Color), Pos = c.Fader, Mute = c.Mute,
                    PeakL = pk?[0] ?? 0f, PeakR = pk?[1] ?? 0f, PreL = pk?[2] ?? 0f, PreR = pk?[3] ?? 0f, Value = FaderLaw.FormatDb(c.Fader),
                    Badge = ducked ? "DUCK" : (_bgQuietApplied < 0 && c == gc) ? "LEISER" : c.Solo ? "SOLO" : null,
                });
            }
            foreach (var g in _settings.Groups)
            {
                var g4 = GroupPeak(g);
                rows.Add(new MiniRow { Id = "grp:" + g.Id, Name = g.Name, Color = Color.FromArgb(g.Color), Pos = g.Fader, Mute = g.Mute, PeakL = g4[0], PeakR = g4[1], PreL = g4[2], PreR = g4[3], Value = FaderLaw.FormatDb(g.Fader), Badge = "GRUPPE" });
            }
            var mic = _snap.Mic;
            if (mic != null && !_settings.HideMic)
            {
                float v = Local("mic", mic.Volume);
                var m4 = MicPeak();
                string badge = _settings.PttMode == 1 ? (_pttHeld ? "SENDEN" : "PTT") : _settings.PttMode == 2 ? "PTM" : null;
                rows.Add(new MiniRow
                {
                    Id = "mic", Name = "MIKRO", Color = Color.FromArgb(MixerSettings.Palette[7]), Pos = v,
                    Mute = _settings.PttMode != 0 ? _micHardMute : PathMute ? _pathMuted : mic.Mute, PeakL = m4[0], PeakR = m4[1], PreL = m4[2], PreR = m4[3],
                    Value = (int)Math.Round(v * 100) + " %", Badge = badge,
                });
            }
            if (_settings.StreamOn)
            {
                float sp = _settings.StreamMasterDb <= -60f ? 0f : FaderLaw.DbToPos(_settings.StreamMasterDb);
                rows.Add(new MiniRow
                {
                    Id = "stream", Name = "STREAM", Color = ChannelStrip.StreamColor, Pos = sp, Mute = _settings.StreamMute,
                    PeakL = _snap.StreamPeak?[0] ?? 0f, PeakR = _snap.StreamPeak?[1] ?? 0f, Value = FaderLaw.FormatDb(sp),
                });
            }
            var d = DefaultDevice;
            if (d != null)
            {
                float v = Local("master", d.Volume);
                rows.Add(new MiniRow
                {
                    Id = "master", Name = "AUSGANG", Color = Theme.Accent, Pos = v, Mute = d.Mute,
                    PeakL = DevicePeak(d)[0], PeakR = DevicePeak(d)[1], PreL = d.PeakL, PreR = d.PeakR, Value = (int)Math.Round(v * 100) + " %",
                });
            }
            return rows;
        }

        public void MiniSetLevel(string id, float pos)
        {
            pos = Math.Max(0f, Math.Min(1f, pos));
            _miniLocal[id] = new KeyValuePair<float, double>(pos, Now);
            if (id == "mic") { if (_snap.Mic != null) { _snap.Mic.Volume = pos; Backend.SetMicVolume(pos); } return; }
            if (id == "master") { var d = DefaultDevice; if (d != null) { d.Volume = pos; Backend.SetDeviceVolume(d.Id, pos); } return; }
            if (id == "stream") { float db = FaderLaw.PosToDb(pos); SetStreamMaster(float.IsNegativeInfinity(db) ? -60f : db); return; }
            if (id.StartsWith("grp:"))
            {
                var g = _settings.Groups.FirstOrDefault(x => "grp:" + x.Id == id);
                if (g != null && !g.FaderLock) { g.Fader = pos <= FaderLaw.MinPos ? 0f : pos; OnGroupChanged(g); }   // gesperrt: Maus aendert nichts
                return;
            }
            var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == id);
            if (c == null || c.FaderLock) return;
            c.Fader = pos <= FaderLaw.MinPos ? 0f : pos;
            OnChannelChanged(c, false);
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
        }

        public void MiniToggleMute(string id)
        {
            if (id == "mic") { ToggleMicMute(false); return; }
            if (id == "master")
            {
                var d = DefaultDevice;
                if (d != null) { d.Mute = !d.Mute; Backend.SetDeviceMute(d.Id, d.Mute); }
                return;
            }
            if (id == "stream") { ToggleStreamMute(); return; }
            if (id.StartsWith("grp:"))
            {
                var g = _settings.Groups.FirstOrDefault(x => "grp:" + x.Id == id);
                if (g != null) { g.Mute = !g.Mute; OnGroupChanged(g); }
                return;
            }
            var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == id);
            if (c == null) return;
            _fades.Remove(c.Id);   // MUTE bricht ein laufendes Ein-/Ausblenden ab
            c.Mute = !c.Mute;
            OnChannelChanged(c, false);
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
        }

        public void MiniWheel(string id, int delta)
        {
            float notches = delta / 120f;
            if (id == "mic" || id == "master")
            {
                float cur = id == "mic" ? (_snap.Mic?.Volume ?? 0f) : (DefaultDevice?.Volume ?? 0f);
                if (_miniLocal.TryGetValue(id, out var l) && Now - l.Value < 0.7) cur = l.Key;
                MiniSetLevel(id, (float)Math.Round((cur + notches * 0.02f) * 50) / 50f);
                return;
            }
            float fp;
            if (id == "stream") fp = _settings.StreamMasterDb <= -60f ? 0f : FaderLaw.DbToPos(_settings.StreamMasterDb);
            else if (id.StartsWith("grp:")) { var g = _settings.Groups.FirstOrDefault(x => "grp:" + x.Id == id); if (g == null) return; fp = g.Fader; }
            else { var c = _settings.Channels.FirstOrDefault(x => "ch:" + x.Id == id); if (c == null) return; fp = c.Fader; }
            float db = FaderLaw.PosToDb(fp);
            if (float.IsNegativeInfinity(db)) db = -62f;
            db += notches * 2f;
            MiniSetLevel(id, db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db)));
        }

        public void ShowMiniMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            m.Items.Add(new ToolStripMenuItem("Großes Mischpult öffnen", null, (s, e) => ShowFromMini()) { Font = Theme.Ui(12f, FontStyle.Bold) });
            m.Items.Add(new ToolStripMenuItem("Mini-Ansicht schließen", null, (s, e) => SetMini(false)));
            m.Items.Add(new ToolStripSeparator());
            var scenes = Sub("Szene laden", false);
            AddSceneLoadItems(scenes.DropDownItems);
            m.Items.Add(scenes);
            m.Items.Add(new ToolStripMenuItem("Nachtmodus", null, (s, e) => ToggleNight(true)) { Checked = _settings.NightMode });
            m.Items.Add(new ToolStripMenuItem("Ducking", null, (s, e) => ToggleDuck(true)) { Checked = _settings.DuckOn });
            m.Items.Add(new ToolStripMenuItem("DIM – alles leiser", null, (s, e) => ToggleDim(true)) { Checked = _dimOn });
            m.Items.Add(new ToolStripMenuItem("Loudness", null, (s, e) => ToggleLoud(true)) { Checked = _settings.LoudOn });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Beenden", null, (s, e) => ExitApp()));
            m.Show(screen);
        }

        // ================================================================ MIDI

        private static bool IsContinuous(string target) =>
            target.EndsWith(":fader") || target.EndsWith(":pan") || target == "master.volume" || target == "mic.volume" || target == "stream.volume";

        private List<MidiDialog.Target> MidiTargets()
        {
            var list = new List<MidiDialog.Target>();
            void add(string id, string label, bool cont) => list.Add(new MidiDialog.Target { Id = id, Label = label, Continuous = cont });
            foreach (var c in _settings.Channels)
            {
                add("ch:" + c.Id + ":fader", c.Name + " – Lautstärke", true);
                add("ch:" + c.Id + ":mute", c.Name + " – Stumm", false);
                add("ch:" + c.Id + ":solo", c.Name + " – Solo", false);
                add("ch:" + c.Id + ":fade", c.Name + " – FADE (langsam aus/ein)", false);
                add("ch:" + c.Id + ":pan", c.Name + " – Balance", true);
            }
            foreach (var g in _settings.Groups)
            {
                add("grp:" + g.Id + ":fader", g.Name + " (Gruppe) – Lautstärke", true);
                add("grp:" + g.Id + ":mute", g.Name + " (Gruppe) – Stumm", false);
            }
            add("master.volume", "Ausgang – Lautstärke", true);
            add("master.mute", "Ausgang – Stumm", false);
            add("mic.volume", "Mikro – Lautstärke", true);
            add("mic.mute", "Mikro – Stumm", false);
            add("duck.toggle", "Ducking an/aus", false);
            add("night.toggle", "Nachtmodus an/aus", false);
            add("dim.toggle", "DIM (alles leiser) an/aus", false);
            add("loud.toggle", "Loudness an/aus", false);
            add("vu.window", "Großes VU-Meter zeigen/schließen", false);
            add("rec.toggle", "Aufnahme starten/stoppen", false);
            add("mini.toggle", "Mini-Ansicht an/aus", false);
            if (_settings.StreamOn)
            {
                add("stream.volume", "Stream-Mix – Lautstärke", true);
                add("stream.mute", "Stream-Mix – Stumm", false);
            }
            foreach (var sc in _settings.Scenes) add("scene:" + sc.Id, "Szene laden: " + sc.Name, false);
            foreach (var sp in _settings.Sounds) add("sb:" + sp.Id, "Sound: " + sp.Name, false);
            add("sb.stop", "Soundboard: alles stoppen", false);
            add("media.play", "Musik: Play / Pause", false);
            add("media.next", "Musik: nächster Titel", false);
            add("media.prev", "Musik: vorheriger Titel", false);
            return list;
        }

        /// <summary>Oeffnet die MIDI-Eingaenge mit Zuordnungen (bzw. alle, waehrend das MIDI-Fenster offen ist).</summary>
        private void SyncMidi()
        {
            if (!MidiHub.Available) return;
            bool all = _midiDialog != null;
            if (_midi == null)
            {
                if (!all && _settings.Midi.Count == 0) return;
                _midi = new MidiHub();
            }
            var want = all ? MidiHub.DeviceNames() : _settings.Midi.Select(b => b.Device).Where(d => d != null).Distinct().ToList();
            _midi.Sync(want);
        }

        private void PumpMidi(double now)
        {
            if (now >= _nextMidiSync)
            {
                _nextMidiSync = now + 5; // neu angesteckte Controller finden
                try { SyncMidi(); } catch { }
            }
            if (_midi == null) return;
            Dictionary<string, float> cont = null;
            int guard = 0;
            while (guard++ < 500 && _midi.TryDequeue(out var e))
            {
                if (_midiDialog != null) { _midiDialog.OnMidi(e); continue; }
                var b = _settings.Midi.FirstOrDefault(x => x.Device == e.Device && x.Kind == e.Kind && x.Channel == e.Channel && (x.Kind == 2 || x.Number == e.Number));
                if (b == null || b.Target == null) continue;
                NoteMidiIn400(b);   // 4.0: MIDI-Rueckmeldung ruht, solange der Regler bewegt wird
                if (IsContinuous(b.Target))
                {
                    if (e.Kind == 1) continue;
                    (cont ?? (cont = new Dictionary<string, float>()))[b.Target] = e.Normalized;
                    continue;
                }
                string key = e.Device + "|" + e.Kind + "|" + e.Channel + "|" + e.Number;
                bool pressed = e.Kind == 1 ? e.Value > 0 : e.Value >= 64;
                _midiPressed.TryGetValue(key, out bool was);
                _midiPressed[key] = pressed;
                if (!pressed || (e.Kind != 1 && was)) continue; // nur beim Druecken
                try { ExecuteHotkey(b.Target); } catch { }
            }
            if (cont != null) foreach (var kv in cont) ApplyMidiValue(kv.Key, kv.Value);
        }

        private void ApplyMidiValue(string target, float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            if (target == "master.volume" || target == "mic.volume")
            {
                MiniSetLevel(target == "mic.volume" ? "mic" : "master", v);
                if (target == "mic.volume") _mic.InvalidateChrome();
                return;
            }
            if (target == "stream.volume") { MiniSetLevel("stream", v); return; }
            var parts = target.Split(':');
            if (parts.Length != 3) return;
            if (parts[0] == "grp") { MiniSetLevel("grp:" + parts[1], v); return; }
            var c = _settings.Channels.FirstOrDefault(x => x.Id == parts[1]);
            if (c == null) return;
            if (parts[2] == "fader") c.Fader = v <= FaderLaw.MinPos ? 0f : v;
            else if (parts[2] == "pan")
            {
                float p = v * 2f - 1f;
                c.Pan = Math.Abs(p) < 0.03f ? 0f : (float)Math.Round(p, 2);
            }
            OnChannelChanged(c, false);
            _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
        }

        private void ShowMidiDialog()
        {
            using (var dlg = new MidiDialog(_settings.Midi, MidiTargets(), MidiHub.DeviceNames, d => _midi?.ErrorFor(d)))
            {
                _midiDialog = dlg;
                try { SyncMidi(); } catch { }
                try
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _settings.Midi = dlg.Result;
                        ScheduleSave();
                    }
                }
                finally
                {
                    _midiDialog = null;
                    try { SyncMidi(); } catch { }
                }
            }
        }

        // ================================================================ EQ-Presets

        public void ShowEqPresetMenu(ChannelStrip strip, Point screen)
        {
            var m = NewMenu();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            m.Items.Add(Header("EQ · " + strip.Config.Name));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Großer EQ (10 Bänder + Spektrum) …", null, (s, e) => ShowBigEq(strip.Config)) { Font = Theme.Ui(12f, FontStyle.Bold) });
            m.Items.Add(new ToolStripSeparator());
            AddPresetItems(m.Items, strip);
            m.Show(new Point(Math.Max(0, screen.X - m.PreferredSize.Width), screen.Y));
        }

        private static string Db(float v) =>
            Math.Abs(v) < 0.05f ? "0" : (v > 0 ? "+" : "−") + Math.Abs(v).ToString("0.#", CultureInfo.InvariantCulture);

        private ToolStripMenuItem PresetItem(EqPreset p, ChannelStrip strip)
        {
            var c = strip.Config;
            bool cur = c.EqLow == p.Low && c.EqMid == p.Mid && c.EqHigh == p.High && (!p.Comp || c.CompOn) && (c.EqOn || (p.Low == 0 && p.Mid == 0 && p.High == 0));
            return new ToolStripMenuItem(p.Name, null, (s, e) => strip.ApplyPreset(p))
            {
                Checked = cur,
                ShortcutKeyDisplayString = Db(p.Low) + " / " + Db(p.Mid) + " / " + Db(p.High) + (p.Comp ? " + COMP" : ""),
            };
        }

        private void AddPresetItems(ToolStripItemCollection items, ChannelStrip strip)
        {
            foreach (var p in EqPreset.BuiltIn) items.Add(PresetItem(p, strip));
            if (_settings.EqPresets.Count > 0)
            {
                items.Add(new ToolStripSeparator());
                items.Add(Header("Eigene"));
                foreach (var p in _settings.EqPresets) items.Add(PresetItem(p, strip));
            }
            items.Add(new ToolStripSeparator());
            var c = strip.Config;
            items.Add(new ToolStripMenuItem("Aktuellen EQ als Preset speichern …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("EQ-Preset speichern", "Name des Presets:", c.Name.Substring(0, 1) + c.Name.Substring(1).ToLowerInvariant() + "-EQ"))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    _settings.EqPresets.RemoveAll(x => string.Equals(x.Name, dlg.Value, StringComparison.CurrentCultureIgnoreCase));
                    _settings.EqPresets.Add(new EqPreset { Name = dlg.Value, Low = c.EqLow, Mid = c.EqMid, High = c.EqHigh, Comp = c.CompOn });
                    ScheduleSave();
                }
            }))));
            if (_settings.EqPresets.Count > 0)
            {
                var del = Sub("Eigenes Preset löschen", false);
                foreach (var p in _settings.EqPresets)
                {
                    var preset = p;
                    del.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (s, e) => { _settings.EqPresets.Remove(preset); ScheduleSave(); }));
                }
                items.Add(del);
            }
            AddPresetShare400(items);   // 4.0: Presets als Datei
        }

        // ================================================================ Infobereich (Schnellmenue)

        private void BuildTrayMenu(ContextMenuStrip menu)
        {
            var old = menu.Items.Cast<ToolStripItem>().ToList();
            menu.Items.Clear();
            foreach (var it in old) try { it.Dispose(); } catch { }
            menu.ShowCheckMargin = true;
            menu.ShowImageMargin = false;

            menu.Items.Add(new ToolStripMenuItem("Mischpult öffnen", null, (s, e) => RestoreFromTray()) { Font = Theme.Ui(12f, FontStyle.Bold) });
            menu.Items.Add(new ToolStripMenuItem("Einstellungen …", null, (s, e) => BeginInvoke(new Action(() => ShowSettings()))));
            menu.Items.Add(new ToolStripMenuItem("Mini-Ansicht", null, (s, e) => SetMini(!(_mini != null && _mini.Visible))) { Checked = _mini != null && _mini.Visible });
            menu.Items.Add(new ToolStripSeparator());

            var mic = _snap.Mic;
            bool micMuted = _settings.PttMode != 0 ? _micHardMute : PathMute ? _pathMuted : mic != null && mic.Mute;
            menu.Items.Add(new ToolStripMenuItem("Mikrofon stumm", null, (s, e) => ToggleMicMute(true)) { Checked = micMuted, Enabled = mic != null });
            var def = DefaultDevice;
            menu.Items.Add(new ToolStripMenuItem("Ausgang stumm", null, (s, e) => ExecuteHotkey("master.mute")) { Checked = def != null && def.Mute, Enabled = def != null });
            menu.Items.Add(new ToolStripMenuItem("DIM – alles " + (int)Math.Round(DimDb) + " dB leiser", null, (s, e) => ToggleDim(false)) { Checked = _dimOn });
            var devs = Sub("Ausgabegerät");
            foreach (var d in _snap.Devices)
            {
                string id = d.Id;
                devs.DropDownItems.Add(new ToolStripMenuItem(d.FullName, null, (s, e) => Backend.SetDefaultDevice(id)) { Checked = d.IsDefault });
            }
            if (_snap.Devices.Length == 0) devs.DropDownItems.Add(Hint("Kein Ausgabegerät"));
            menu.Items.Add(devs);
            var music = Sub("Musik", false);
            music.DropDownItems.Add(new ToolStripMenuItem("Play / Pause", null, (s, e) => SendMedia("play", false)));
            music.DropDownItems.Add(new ToolStripMenuItem("Nächster Titel", null, (s, e) => SendMedia("next", false)));
            music.DropDownItems.Add(new ToolStripMenuItem("Vorheriger Titel", null, (s, e) => SendMedia("prev", false)));
            menu.Items.Add(music);
            menu.Items.Add(new ToolStripSeparator());

            var scenes = Sub("Szene laden", false);
            AddSceneLoadItems(scenes.DropDownItems);
            menu.Items.Add(scenes);
            menu.Items.Add(new ToolStripMenuItem("Nachtmodus", null, (s, e) => ToggleNight(true)) { Checked = _settings.NightMode });
            menu.Items.Add(new ToolStripMenuItem("Loudness", null, (s, e) => ToggleLoud(true)) { Checked = _settings.LoudOn });
            menu.Items.Add(new ToolStripMenuItem("Ducking", null, (s, e) => ToggleDuck(true)) { Checked = _settings.DuckOn });
            menu.Items.Add(BuildSleepMenu());
            menu.Items.Add(new ToolStripMenuItem("Soundboard …", null, (s, e) => ShowSoundboard()));
            menu.Items.Add(new ToolStripMenuItem(_recWriter != null ? "Aufnahme stoppen (" + _top.RecText?.Replace("REC ", "") + ")" : "Aufnahme starten", null, (s, e) => ToggleRecording()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Beenden", null, (s, e) => ExitApp()));
        }

        // ================================================================ Optionen (Untermenues)

        private void AddFeatureOptions(ContextMenuStrip m)
        {
            // Automatik
            var auto = Sub("Automatik");
            auto.DropDownItems.Add(new ToolStripMenuItem("Spiele automatisch erkennen", null, (s, e) =>
            {
                _settings.NoAutoGames = !_settings.NoAutoGames;
                ScheduleSave();
                if (!_settings.NoAutoGames) { DetectGames(); RefreshChannelViews(); }
            }) { Checked = !_settings.NoAutoGames });
            var gcMenu = Sub("Kanal für Spiele");
            foreach (var c in _settings.Channels.Where(x => !x.IsCatchAll))
            {
                var ch = c;
                gcMenu.DropDownItems.Add(new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color)), (s, e) =>
                {
                    _settings.GameChannel = ch.Id;
                    ScheduleSave();
                    DetectGames();
                    RefreshChannelViews();
                }) { Checked = _settings.GameChannel == c.Id });
            }
            auto.DropDownItems.Add(gcMenu);
            var bg = Sub("Spiel im Hintergrund leiser");
            bg.DropDownItems.Add(Hint("Wenn du aus dem Spiel heraus­wechselst (Alt+Tab)."));
            bg.DropDownItems.Add(new ToolStripMenuItem("Aus", null, (s, e) => { _settings.BgQuietOff = true; ScheduleSave(); }) { Checked = _settings.BgQuietOff });
            foreach (float db in new[] { -6f, -12f, -20f, -40f })
            {
                float d = db;
                bg.DropDownItems.Add(new ToolStripMenuItem(db <= -40f ? "Stumm" : db.ToString("0", CultureInfo.InvariantCulture).Replace("-", "−") + " dB", null, (s, e) =>
                {
                    _settings.BgQuietOff = false;
                    _settings.BgQuietDb = d;
                    _bgQuietApplied = 1f; // neu anwenden
                    ScheduleSave();
                }) { Checked = !_settings.BgQuietOff && Math.Abs(_settings.BgQuietDb - db) < 0.1f });
            }
            auto.DropDownItems.Add(bg);
            auto.DropDownItems.Add(GameModeItem());
            auto.DropDownItems.Add(new ToolStripSeparator());
            auto.DropDownItems.Add(BuildProfileMenu());
            auto.DropDownItems.Add(new ToolStripMenuItem("Headset automatisch umschalten …", null, (s, e) => BeginInvoke(new Action(ShowHeadsetDialog)))
            { Checked = _settings.AutoSwitchOn && _settings.AutoDevice != null });
            m.Items.Add(auto);

            // Ton
            var ton = Sub("Ton");
            ton.DropDownItems.Add(new ToolStripMenuItem("Nachtmodus (Kompressor auf allen Kanälen)", null, (s, e) => ToggleNight(false)) { Checked = _settings.NightMode });
            var hear = Sub("Gehörschutz");
            hear.DropDownItems.Add(Hint("Fängt plötzlich laute Geräusche ab (Explosionen, Schreie im Voice-Chat)."));
            foreach (var kv in HearingLevels)
            {
                float db = kv.Key;
                hear.DropDownItems.Add(new ToolStripMenuItem(kv.Value, null, (s, e) => SetHearingCap(db)) { Checked = Math.Abs(_settings.HearingCap - db) < 0.1f });
            }
            ton.DropDownItems.Add(hear);
            ton.DropDownItems.Add(new ToolStripMenuItem("Sanft stummschalten (kurz ein-/ausblenden)", null, (s, e) =>
            {
                _settings.NoSoftMute = !_settings.NoSoftMute;
                Backend.SetSoftFade(!_settings.NoSoftMute);
                ScheduleSave();
            }) { Checked = !_settings.NoSoftMute });
            var beep = Sub("Piepton beim Stummschalten");
            beep.DropDownItems.Add(Hint("Kurzer Ton bei Mikro stumm/an – nur für dich, nicht in Discord."));
            beep.DropDownItems.Add(new ToolStripMenuItem("Aus", null, (s, e) => { _settings.MuteBeepOff = true; ScheduleSave(); }) { Checked = _settings.MuteBeepOff });
            foreach (var kv in new[] { new KeyValuePair<int, string>(1, "Leise"), new KeyValuePair<int, string>(2, "Mittel"), new KeyValuePair<int, string>(3, "Laut") })
            {
                int lvl = kv.Key;
                beep.DropDownItems.Add(new ToolStripMenuItem(kv.Value, null, (s, e) =>
                {
                    _settings.MuteBeepOff = false; _settings.MuteBeepLevel = lvl; ScheduleSave();
                    PlayMuteBeep(true);   // Probe
                }) { Checked = !_settings.MuteBeepOff && _settings.MuteBeepLevel == lvl });
            }
            ton.DropDownItems.Add(beep);
            m.Items.Add(ton);
            m.Items.Add(BuildViewMenu());
            m.Items.Add(BuildSleepMenu());
            m.Items.Add(new ToolStripMenuItem("Mini-Ansicht", null, (s, e) => SetMini(!(_mini != null && _mini.Visible))) { Checked = _mini != null && _mini.Visible });
        }

        private void CleanupFeatures()
        {
            StopRecording(false);
            if (_settings.PttMode != 0 && !_micHardMute)
                Backend.SetMicMute(false); // Push-to-Talk hinterlaesst kein stummes Mikro
            try { _midi?.Dispose(); } catch { }
            _midi = null;
            try { _mini?.Dispose(); } catch { }
            _mini = null;
        }
    }
}
