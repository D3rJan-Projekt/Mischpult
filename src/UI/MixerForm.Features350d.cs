using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5 – Bedienung: Wert eintippen, mehrere Fader zusammen, Klang kopieren/einfuegen, Mittelklick, Spickzettel (F1),
    /// Xbox-Controller, Start-Szene, Ueberblendzeit pro Szene, Lautstaerke-Obergrenze mit Passwort, Hotkeys fuer die App im
    /// Vordergrund, Zahlentasten, Kanal anpinnen, Mausrad umkehren, Kanal-Vorlagen, Szenen-Vorschau.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 140 Wert eintippen

        public void AskFaderValue(StripBase s)
        {
            if (s == null || !_settings.TypeValueOn) return;
            BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Wert eintippen – " + (s.DisplayName ?? "Fader"), s.TypedHint, s.TypedNow, 12))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    if (!s.ApplyTyped(dlg.Value))
                        MessageBox.Show(this, "„" + dlg.Value + "“ habe ich nicht verstanden.\n\n" + s.TypedHint, "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }));
        }

        // ================================================================ 142 Klang kopieren/einfuegen, 164 Kanal-Vorlagen

        /// <summary>Was zum "Klang" eines Kanals gehoert (wie bei "Klang zuruecksetzen", plus 3.4.6/3.5).</summary>
        internal static readonly string[] SoundFields =
        {
            "EqOn", "EqLow", "EqMid", "EqHigh", "Geq", "CompOn", "AutoOn", "FxType", "FxPreset", "FxMix", "Pan",
            "Stereo", "DelayMs", "EqAlt", "EqIsB", "WidthPct", "HpHz", "PopGuard", "LpHz", "Dialog", "Peq", "PeqBypass",   // 4.0: parametrischer EQ
        };
        /// <summary>Eine Vorlage bringt zusaetzlich Aussehen mit.</summary>
        internal static readonly string[] TemplateExtraFields = { "Color", "Color2", "Symbol", "Field", "FaderMark" };

        private ChannelConfig _copied;

        internal static ChannelConfig CloneChannel(ChannelConfig c)
        {
            var ser = new DataContractSerializer(typeof(ChannelConfig));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, c);
                ms.Position = 0;
                return (ChannelConfig)ser.ReadObject(ms);
            }
        }

        /// <summary>Felder von src (tief kopiert) auf dst uebertragen.</summary>
        internal static void CopyFields(ChannelConfig src, ChannelConfig dst, IEnumerable<string> names)
        {
            var copy = CloneChannel(src);
            foreach (var n in names)
            {
                var f = typeof(ChannelConfig).GetField(n);
                if (f != null) f.SetValue(dst, f.GetValue(copy));
            }
        }

        private void AddChannel350Menu(ToolStripItemCollection items, ChannelConfig c)
        {
            if (_settings.CopyPasteOn)
            {
                items.Add(new ToolStripMenuItem("Klang kopieren", null, (s, e) => { _copied = CloneChannel(c); ShowOsd("Klang kopiert", c.Name, -1, Color.FromArgb(c.Color)); }));
                items.Add(new ToolStripMenuItem("Klang einfügen" + (_copied != null ? " (von " + _copied.Name + ")" : ""), null, (s, e) => PasteSound(c)) { Enabled = _copied != null });
            }
            if (_settings.PinOn)
                items.Add(new ToolStripMenuItem("Anpinnen (bleibt links stehen)", null, (s, e) => SetPinned(c, !c.Pinned)) { Checked = c.Pinned });
            if (_settings.TemplatesOn)
            {
                var sub = Sub("Vorlagen");
                sub.DropDownItems.Add(new ToolStripMenuItem("Diesen Kanal als Vorlage speichern …", null, (s, e) => BeginInvoke(new Action(() => SaveTemplate(c)))));
                if (_settings.Templates != null && _settings.Templates.Count > 0)
                {
                    sub.DropDownItems.Add(new ToolStripSeparator());
                    foreach (var t in _settings.Templates)
                    {
                        var tt = t;
                        sub.DropDownItems.Add(new ToolStripMenuItem("Anwenden: " + t.Name, null, (s, e) => ApplyTemplate(tt, c)));
                    }
                    var del = Sub("Vorlage löschen");
                    foreach (var t in _settings.Templates)
                    {
                        var tt = t;
                        del.DropDownItems.Add(new ToolStripMenuItem(t.Name, null, (s, e) => { _settings.Templates.Remove(tt); if (_settings.Templates.Count == 0) _settings.Templates = null; ScheduleSave(); }));
                    }
                    sub.DropDownItems.Add(del);
                }
                items.Add(sub);
            }
        }

        private void PasteSound(ChannelConfig c)
        {
            if (_copied == null) return;
            CopyFields(_copied, c, SoundFields);
            OnChannelEdited(c);
            ScheduleSave();
            ShowOsd("Klang eingefügt", _copied.Name + " → " + c.Name, -1, Color.FromArgb(c.Color));
        }

        private void SaveTemplate(ChannelConfig c)
        {
            using (var dlg = new InputDialog("Kanal-Vorlage speichern", "Name der Vorlage (z. B. Chat, Musik, Spiel):", c.Name.Length > 1 ? c.Name.Substring(0, 1) + c.Name.Substring(1).ToLowerInvariant() : c.Name))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (_settings.Templates == null) _settings.Templates = new List<ChannelTemplate>();
                var copy = CloneChannel(c);
                copy.Apps = new List<string>(); copy.IsCatchAll = false; copy.Solo = false; copy.Mute = false;
                _settings.Templates.RemoveAll(t => string.Equals(t.Name, dlg.Value, StringComparison.CurrentCultureIgnoreCase));
                _settings.Templates.Add(new ChannelTemplate { Name = dlg.Value, Config = copy });
                if (_settings.Templates.Count > 30) _settings.Templates.RemoveAt(0);
                ScheduleSave();
                ShowOsd("Vorlage gespeichert", dlg.Value, -1, Color.FromArgb(c.Color));
            }
        }

        private void ApplyTemplate(ChannelTemplate t, ChannelConfig c)
        {
            if (t?.Config == null || c == null) return;
            CopyFields(t.Config, c, SoundFields.Concat(TemplateExtraFields));
            OnChannelEdited(c);
            BuildChannelStrips();
            ScheduleSave();
            ShowOsd("Vorlage angewendet", t.Name + " → " + c.Name, -1, Color.FromArgb(c.Color));
        }

        /// <summary>Plus-Knopf: mit Vorlagen ein kleines Menue (leerer Kanal oder aus Vorlage).</summary>
        private bool AddChannelMenu350()
        {
            if (!_settings.TemplatesOn || _settings.Templates == null || _settings.Templates.Count == 0) return false;
            var m = NewMenu();
            m.Items.Add(new ToolStripMenuItem("Leerer Kanal", null, (s, e) => AddChannel()));
            m.Items.Add(new ToolStripSeparator());
            foreach (var t in _settings.Templates)
            {
                var tt = t;
                m.Items.Add(new ToolStripMenuItem("Aus Vorlage: " + t.Name, null, (s, e) => AddChannelFromTemplate(tt)));
            }
            m.Show(Cursor.Position);
            return true;
        }

        private void AddChannelFromTemplate(ChannelTemplate t)
        {
            int before = _settings.Channels.Count;
            AddChannel();
            if (_settings.Channels.Count <= before) return;
            var c = _settings.Channels.LastOrDefault(x => !x.IsCatchAll);
            if (c == null) return;
            CopyFields(t.Config, c, SoundFields.Concat(TemplateExtraFields));
            if (!string.IsNullOrWhiteSpace(t.Config.Name)) c.Name = t.Config.Name;
            OnChannelEdited(c);
            BuildChannelStrips();
            ScheduleSave();
        }

        // ================================================================ 162 Kanal anpinnen

        private void SetPinned(ChannelConfig c, bool on)
        {
            c.Pinned = on;
            if (on)
            {
                // angepinnte Kanaele stehen vorne (in ihrer Reihenfolge)
                _settings.Channels.Remove(c);
                int idx = _settings.Channels.TakeWhile(x => x.Pinned).Count();
                _settings.Channels.Insert(idx, c);
            }
            BuildChannelStrips();
            ScheduleSave();
        }

        private bool IsPinnedStrip(Control c) => _settings.PinOn && c is ChannelStrip cs && cs.Config.Pinned;

        // ================================================================ 146 Start-Szene, 147 Ueberblendzeit pro Szene

        private float _xfOverride = -1f;

        private void NoteScene350(Scene sc)
        {
            _xfOverride = _settings.SceneXfadeOwnOn && sc != null && sc.XfadeIdx > 0 && sc.XfadeIdx < MixerSettings.SceneXfadeOwnValues.Length
                ? MixerSettings.SceneXfadeOwnValues[sc.XfadeIdx] : -1f;
            if (_settings.SceneSweepOn) StripBase.StartSweep();   // 136
            ClearScenePreview();
        }

        private void AddSceneMenu350(ContextMenuStrip m)
        {
            if (!_settings.SceneXfadeOwnOn || _settings.Scenes.Count == 0) return;
            var sub = Sub("Überblendzeit pro Szene", false);
            foreach (var sc in _settings.Scenes)
            {
                var scene = sc;
                var it = Sub(sc.Name + ": " + MixerSettings.SceneXfadeOwnNames[Math.Max(0, Math.Min(MixerSettings.SceneXfadeOwnNames.Length - 1, sc.XfadeIdx))]);
                for (int i = 0; i < MixerSettings.SceneXfadeOwnNames.Length; i++)
                {
                    int k = i;
                    it.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.SceneXfadeOwnNames[i], null, (s, e) => { scene.XfadeIdx = k; ScheduleSave(); }) { Checked = sc.XfadeIdx == i });
                }
                sub.DropDownItems.Add(it);
            }
            int at = m.Items.Count;
            for (int i = 0; i < m.Items.Count; i++) if (m.Items[i] is ToolStripMenuItem mi && mi.Text.StartsWith("Hotkeys für Szenen")) { at = i; break; }
            m.Items.Insert(at, sub);
        }

        private void LoadStartScene()
        {
            if (!_settings.StartSceneOn || _settings.StartScene == null) return;
            var sc = _settings.Scenes.FirstOrDefault(x => x.Id == _settings.StartScene);
            if (sc == null) return;
            var t = new Timer { Interval = 1500 };   // erst wenn Apps und Geraete da sind
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); ApplyScene(sc, "Start-Szene"); };
            t.Start();
        }

        // ================================================================ 165 Szenen-Vorschau

        private void PreviewScene(Scene sc)
        {
            if (!_settings.ScenePreviewOn || sc == null) { ClearScenePreview(); return; }
            foreach (var s in _channelStrips)
            {
                var ch = sc.Channels?.Find(x => x.Id == s.Config.Id);
                s.SetPreview(ch == null ? float.NaN : ch.Mute ? 0f : Math.Max(0f, Math.Min(1f, ch.Fader)));
            }
            foreach (var g in _groupStrips)
            {
                var ch = sc.Channels?.Find(x => x.Id == g.Config.Id);
                g.SetPreview(ch == null ? float.NaN : ch.Mute ? 0f : Math.Max(0f, Math.Min(1f, ch.Fader)));
            }
        }

        private void ClearScenePreview()
        {
            foreach (var s in _channelStrips) s.SetPreview(float.NaN);
            foreach (var g in _groupStrips) g.SetPreview(float.NaN);
        }

        /// <summary>Im Szenen-Menue: Maus ueber einer Szene zeigt die Vorschau.</summary>
        private void HookScenePreview(ToolStripItemCollection items)
        {
            if (!_settings.ScenePreviewOn) return;
            foreach (ToolStripItem it in items)
            {
                if (!(it is ToolStripMenuItem mi)) continue;
                var sc = _settings.Scenes.FirstOrDefault(x => x.Name == mi.Text);
                if (sc == null) continue;
                mi.MouseEnter += (s, e) => PreviewScene(sc);
                mi.MouseLeave += (s, e) => ClearScenePreview();
            }
        }

        // ================================================================ 148 Lautstaerke-Obergrenze mit Passwort

        private double _nextCapCheck, _capOsdAt;

        private void TickVolCap(double now)
        {
            if (!_settings.VolCapOn || now < _nextCapCheck) return;
            _nextCapCheck = now + 0.25;
            float cap = _settings.VolCapNow / 100f;
            foreach (var d in _snap.Devices)
            {
                if (d.Volume <= cap + 0.004f) continue;
                d.Volume = cap;
                try { Backend.SetDeviceVolume(d.Id, cap); } catch { }
                if (now - _capOsdAt > 3) { _capOsdAt = now; ShowOsd("Obergrenze", d.Name + ": höchstens " + _settings.VolCapNow + " %", cap, Theme.SoloAmber); }
            }
        }

        internal static string HashPassword(string salt, string pw)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((salt ?? "") + "\u0001" + (pw ?? ""))));
        }

        internal static bool CheckPassword(MixerSettings s, string pw) => s.VolCapHash == null || HashPassword(s.VolCapSalt, pw) == s.VolCapHash;

        /// <summary>Passwort abfragen, falls eins gesetzt ist. true = darf aendern.</summary>
        private bool AskCapPassword()
        {
            if (_settings.VolCapHash == null) return true;
            using (var dlg = new InputDialog("Obergrenze – Passwort", "Passwort, um die Obergrenze zu ändern:", "", 40, true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                if (CheckPassword(_settings, dlg.Value)) return true;
            }
            MessageBox.Show(this, "Das Passwort stimmt nicht.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void SetCapPassword()
        {
            if (!AskCapPassword()) return;
            using (var dlg = new InputDialog("Obergrenze – neues Passwort", "Neues Passwort (leer lassen und Abbrechen = keins):", "", 40, true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
                _settings.VolCapSalt = Convert.ToBase64String(salt);
                _settings.VolCapHash = HashPassword(_settings.VolCapSalt, dlg.Value);
                ScheduleSave();
                ShowOsd("Obergrenze", "Passwort gesetzt", -1, Theme.SoloAmber);
            }
        }

        private void RemoveCapPassword()
        {
            if (!AskCapPassword()) return;
            _settings.VolCapHash = null; _settings.VolCapSalt = null;
            ScheduleSave();
        }

        // ================================================================ 160 Hotkeys fuer die App im Vordergrund

        private readonly Dictionary<string, float> _fgMutedPos = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private void AddHotkeyActions350(List<HotkeyAction> list)
        {
            if (!_settings.FgHotkeysOn) return;
            list.Add(new HotkeyAction("fg.up", "App im Vordergrund lauter", "Allgemein"));
            list.Add(new HotkeyAction("fg.down", "App im Vordergrund leiser", "Allgemein"));
            list.Add(new HotkeyAction("fg.mute", "App im Vordergrund stumm an/aus", "Allgemein"));
        }

        private bool ExecuteHotkey350(string action)
        {
            if (!action.StartsWith("fg.")) return false;
            if (!_settings.FgHotkeysOn) return true;
            string key = ForegroundAppKey(out string name);
            if (key == null) { ShowOsd("App im Vordergrund", "spielt gerade keinen Ton", -1, Theme.TextDim); return true; }
            if (action == "fg.mute")
            {
                float pos = AppPos(key);
                if (pos > FaderLaw.MinPos) { _fgMutedPos[key] = pos; SetAppPos(key, 0f); ShowOsd(name, "STUMM", -1, Theme.MuteRed); }
                else { float back = _fgMutedPos.TryGetValue(key, out float b) ? b : 1f; _fgMutedPos.Remove(key); SetAppPos(key, back); ShowOsd(name, "AN", -1, Theme.LedGreen); }
                return true;
            }
            float np = NudgeApp(key, action == "fg.up" ? 240 : -240, false);   // 2 dB
            float db = FaderLaw.PosToDb(np);
            ShowOsd(name, float.IsNegativeInfinity(db) ? "aus" : db > -0.05f ? "0 dB (wie der Kanal)" : db.ToString("0", System.Globalization.CultureInfo.InvariantCulture).Replace("-", "−") + " dB", np, Theme.Accent);
            return true;
        }

        /// <summary>Schluessel der App, deren Fenster vorne ist (nur wenn sie im Pult bekannt ist).</summary>
        private string ForegroundAppKey(out string name)
        {
            name = null;
            if (!Win32.IsWindows) return null;
            try
            {
                var hwnd = Win32.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                string exe;
                using (var p = Process.GetProcessById((int)pid)) exe = p.ProcessName + ".exe";
                string key = AppKeys.FromExe(exe);
                var a = _snap.Apps.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (a == null) return null;
                name = a.Name;
                return a.Key;
            }
            catch { return null; }
        }

        // ================================================================ 144 Spickzettel, 161 Zahlentasten

        private CheatSheet _cheat;

        /// <summary>Aus ProcessCmdKey (vor der Tastatur-Bedienung). true = erledigt.</summary>
        private bool HandleKeys350(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.F1 && _settings.CheatSheetOn) { ShowCheatSheet(); return true; }
            if (key == Keys.Escape && StripBase.Selected.Count > 0) { StripBase.ClearSelection(); return true; }
            if (_settings.DigitKeysOn && _master != null && ReferenceEquals(StripBase.KeyFocus, _master) && (keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                // MASTER per 0 gewaehlt: ↑/↓ Lautstaerke, M stumm
                if (key == Keys.Up || key == Keys.Down) { ExecuteHotkey(key == Keys.Up ? "master.up" : "master.down"); return true; }
                if (key == Keys.M) { ExecuteHotkey("master.mute"); return true; }
            }
            if (_settings.DigitKeysOn && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0)
            {
                int d = key >= Keys.D0 && key <= Keys.D9 ? key - Keys.D0 : key >= Keys.NumPad0 && key <= Keys.NumPad9 ? key - Keys.NumPad0 : -1;
                if (d >= 0)
                {
                    var shown = _channelStrips.Where(s => s.Visible).ToList();
                    StripBase target = d == 0 ? (StripBase)_master : d - 1 < shown.Count ? shown[d - 1] : null;
                    if (target == null) return true;
                    StripBase.KeyFocus = target;
                    if (target is ChannelStrip cs) _channelHost.ScrollIntoView(cs);
                    return true;
                }
            }
            return false;
        }

        private void ShowCheatSheet()
        {
            if (_cheat == null || _cheat.IsDisposed) _cheat = new CheatSheet();
            _cheat.ShowFor(this, CheatSheetText());
        }

        /// <summary>Inhalt des Spickzettels: [Ueberschrift, Zeilen "Taste|Wirkung"].</summary>
        internal List<KeyValuePair<string, List<string>>> CheatSheetText()
        {
            var r = new List<KeyValuePair<string, List<string>>>();
            var mouse = new List<string>
            {
                "Fader ziehen|lauter / leiser   ·   Shift = fein",
                "Mausrad über dem Kanal|lauter / leiser   ·   Strg = fein   ·   Shift = scrollen",
                "Doppelklick auf den Fader|0 dB bzw. zur Fader-Marke",
                "Doppelklick auf den Namen|umbenennen",
                "Name ziehen|Kanal verschieben",
                "Rechtsklick auf den Kanal|Menü: Farbe, Klang, Ausgang, Gruppe …",
                "Rechte Kante ziehen|breiter / schmaler   ·   Strg = alle",
                "Klick auf den Pegel|Spitze löschen",
                "Rechtsklick auf SIG / PEAK|ab wann sie leuchten",
            };
            if (_settings.TypeValueOn) mouse.Add("Doppelklick auf die dB-Anzeige|Wert eintippen");
            if (_settings.MultiFaderOn) mouse.Add("Strg+Klick auf den Namen|mehrere Fader zusammen bewegen");
            if (_settings.MiddleMuteOn) mouse.Add("Mittlere Maustaste|stumm an / aus");
            if (_settings.MeterLupeOn) mouse.Add("Doppelklick auf den Pegel|Pegel-Lupe");
            r.Add(new KeyValuePair<string, List<string>>("MAUS", mouse));
            var keys = new List<string>();
            if (_settings.KeyNavOn || _settings.DigitKeysOn)
            {
                keys.Add("← / →|Kanal wählen");
                keys.Add("↑ / ↓|Fader   ·   Shift fein, Strg grob");
                keys.Add("M / S|stumm / Solo");
                keys.Add("Pos1 / Ende|erster / letzter Kanal");
            }
            if (_settings.DigitKeysOn) keys.Add("1 … 9 / 0|Kanal 1–9 / MASTER wählen");
            keys.Add("Esc|Auswahl aufheben");
            keys.Add("F1|dieser Spickzettel");
            r.Add(new KeyValuePair<string, List<string>>("TASTATUR IM PULT", keys));
            var hk = new List<string>();
            var actions = HotkeyActions();
            foreach (var h in _settings.Hotkeys.Where(x => x.Keys != 0))
            {
                string label = actions.FirstOrDefault(a => a.Id == h.Action)?.Label ?? h.Action;
                hk.Add(HotkeyManager.Format(h.Keys) + "|" + label);
                if (hk.Count >= 14) { hk.Add("…|weitere unter OPTIONEN → Steuerung → Hotkeys"); break; }
            }
            if (hk.Count == 0) hk.Add("–|noch keine (OPTIONEN → Steuerung → Hotkeys)");
            r.Add(new KeyValuePair<string, List<string>>("DEINE HOTKEYS (ÜBERALL)", hk));
            return r;
        }

        // ================================================================ 145 Xbox-Controller

        private Timer _padTimer;
        private ushort _padPrev;
        private double _padRepeat, _padScan;
        private int _padSlot = -1;
        private static readonly Stopwatch PadClock = Stopwatch.StartNew();

        private void ApplyGamepad350()
        {
            bool want = _settings.GamepadOn && Win32.IsWindows && !_demo;
            if (want && _padTimer == null)
            {
                _padTimer = new Timer { Interval = 33 };
                _padTimer.Tick += (s, e) => PadTick();
                _padTimer.Start();
            }
            else if (!want && _padTimer != null) { _padTimer.Stop(); _padTimer.Dispose(); _padTimer = null; _padSlot = -1; }
        }

        private void PadTick()
        {
            double now = PadClock.Elapsed.TotalSeconds;
            if (_padSlot < 0)
            {
                if (now < _padScan) return;
                _padScan = now + 2;   // nicht angeschlossene Plaetze sind langsam: nur alle 2 s suchen
                for (int i = 0; i < 4; i++) if (XInput.TryGet(i, out _)) { _padSlot = i; break; }
                if (_padSlot < 0) return;
            }
            if (!XInput.TryGet(_padSlot, out var st)) { _padSlot = -1; return; }
            ushort b = st.Buttons, pressed = (ushort)(b & ~_padPrev);
            _padPrev = b;
            // Im Spiel nur mit gedrueckter Ansicht-Taste (Back), damit das Spiel nichts ausloest; ist das Pult vorne, immer
            bool armed = (b & XInput.Back) != 0 || PultInFront346;
            if (!armed) return;
            bool front = PultInFront346;
            var sel = StripBase.KeyFocus as ChannelStrip;
            var shown = _channelStrips.Where(s => s.Visible).ToList();
            void Select(int dir)
            {
                if (shown.Count == 0) return;
                int idx = sel == null ? -1 : shown.IndexOf(sel);
                idx = idx < 0 ? 0 : Math.Max(0, Math.Min(shown.Count - 1, idx + dir));
                StripBase.KeyFocus = shown[idx];
                _channelHost.ScrollIntoView(shown[idx]);
                if (!front) ShowOsd("Controller", shown[idx].Config.Name, -1, Color.FromArgb(shown[idx].Config.Color));
            }
            void Nudge(ChannelStrip s, float dDb)
            {
                if (s == null || s.Config.FaderLock) return;
                var c = s.Config;
                float db = FaderLaw.PosToDb(c.Fader);
                if (float.IsNegativeInfinity(db)) db = -61f;
                db += dDb;
                c.Fader = db < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, db));
                ClampToCap(c);
                OnChannelEdited(c);
                ScheduleSave();
                if (!front) ShowOsd(c.Name, FaderLaw.FormatDb(c.Fader), c.Fader, Color.FromArgb(c.Color));
            }
            if ((pressed & XInput.Left) != 0) { Select(-1); _padRepeat = now + 0.35; }
            if ((pressed & XInput.Right) != 0) { Select(1); _padRepeat = now + 0.35; }
            if ((pressed & XInput.Up) != 0) { Nudge(sel, 1f); _padRepeat = now + 0.35; }
            if ((pressed & XInput.Down) != 0) { Nudge(sel, -1f); _padRepeat = now + 0.35; }
            if (now >= _padRepeat && (b & (XInput.Left | XInput.Right | XInput.Up | XInput.Down)) != 0)
            {
                _padRepeat = now + 0.1;
                if ((b & XInput.Left) != 0) Select(-1); else if ((b & XInput.Right) != 0) Select(1);
                else if ((b & XInput.Up) != 0) Nudge(sel, 1f); else if ((b & XInput.Down) != 0) Nudge(sel, -1f);
            }
            float ly = st.LeftY / 32767f;
            if (sel != null && Math.Abs(ly) > 0.25f) Nudge(sel, (ly - Math.Sign(ly) * 0.25f) / 0.75f * 12f * 0.033f);   // bis 12 dB/s
            if ((pressed & XInput.A) != 0 && sel != null) { Mischpult.UI.MixerForm.ToggleChannelMuteStatic(this, sel.Config); }
            if ((pressed & XInput.B) != 0 && sel != null) { sel.Config.Solo = !sel.Config.Solo; OnChannelChanged(sel.Config, false); foreach (var s in _channelStrips) s.InvalidateChrome(); }
            if ((pressed & XInput.X) != 0) SendMedia("play", !front);
            if ((pressed & XInput.Y) != 0) ExecuteHotkey("master.mute");
            if ((pressed & XInput.LB) != 0) SendMedia("prev", !front);
            if ((pressed & XInput.RB) != 0) SendMedia("next", !front);
            if (st.LeftTrigger > 128 || st.RightTrigger > 128)
            {
                if (now >= _padRepeat) { _padRepeat = now + 0.12; ExecuteHotkey(st.RightTrigger > 128 ? "master.up" : "master.down"); }
            }
        }

        private static void ToggleChannelMuteStatic(MixerForm f, ChannelConfig c)
        {
            f.CancelFade(c);
            c.Mute = !c.Mute;
            f.OnChannelChanged(c, false);
            if (!f.PultInFront346) f.ShowOsd(c.Name, c.Mute ? "STUMM" : "AN", -1, c.Mute ? Theme.MuteRed : Theme.LedGreen);
        }

        // ================================================================ Einstellungen

        private void AddControl350(SPage p)
        {
            p.Section("3.5 Bedienung");
            p.Toggle("Wert eintippen", "Doppelklick auf die dB-Anzeige unter einem Fader: genauen Wert eingeben, z. B. −12,5 (bei %-Fadern 0 bis 100). Der Fader fährt hin.",
                () => _settings.TypeValueOn, v => SetControl350(() => _settings.TypeValueOn = v));
            p.Toggle("Mehrere Fader zusammen", "Strg+Klick auf Kanalnamen (oder Gruppen) wählt sie aus – gestrichelter Rahmen. Bewegst du dann einen Fader (Maus oder Mausrad), gehen die anderen um gleich viel dB mit. Esc hebt die Auswahl auf.",
                () => _settings.MultiFaderOn, v => SetControl350(() => { _settings.MultiFaderOn = v; if (!v) StripBase.ClearSelection(); }));
            p.Toggle("Klang kopieren und einfügen", "Rechtsklick auf einen Kanal → Klang kopieren, dann bei einem anderen → Klang einfügen: EQ, COMP, AUTO, Effekte, Balance, Mono, Verzögerung, Breite und Filter.",
                () => _settings.CopyPasteOn, v => SetControl350(() => _settings.CopyPasteOn = v));
            p.Toggle("Mittelklick = stumm", "Die mittlere Maustaste (Mausrad drücken) auf einem Kanal, Ausgang oder einer Gruppe schaltet stumm an/aus.",
                () => _settings.MiddleMuteOn, v => SetControl350(() => _settings.MiddleMuteOn = v));
            p.Toggle("Spickzettel (F1)", "F1 im Pult zeigt alle Maus-Griffe, Tasten und deine Hotkeys auf einen Blick. Eine Taste oder ein Klick schließt ihn.",
                () => _settings.CheatSheetOn, v => SetControl350(() => _settings.CheatSheetOn = v));
            p.Toggle("Zahlentasten 1–9", "Im Pult wählt 1–9 direkt den Kanal (0 = MASTER), danach gehen ↑/↓, M und S wie bei der Tastatur-Bedienung.",
                () => _settings.DigitKeysOn, v => SetControl350(() => _settings.DigitKeysOn = v));
            p.Toggle("Mausrad-Richtung umkehren", "Im Pult dreht das Mausrad andersherum (nach unten = lauter) – für alle, die es so gewohnt sind.",
                () => _settings.WheelInvert, v => SetControl350(() => _settings.WheelInvert = v));
            p.Toggle("Kanal anpinnen", "Rechtsklick auf einen Kanal → Anpinnen: Er rückt nach vorne und bleibt links stehen, auch wenn du scrollst oder die Bank wechselst.",
                () => _settings.PinOn, v => SetControl350(() => { _settings.PinOn = v; BuildChannelStrips(); }));
            p.Toggle("Kanal-Vorlagen", "Rechtsklick auf einen Kanal → Vorlagen: Farbe, Klang und EQ als Vorlage speichern und auf andere Kanäle anwenden. Der Plus-Knopf legt neue Kanäle auch aus einer Vorlage an.",
                () => _settings.TemplatesOn, v => SetControl350(() => _settings.TemplatesOn = v));
            p.Toggle("Hotkeys für die App im Vordergrund", "Unter Hotkeys gibt es dann „App im Vordergrund lauter/leiser/stumm“: Das Fenster, das gerade vorne ist (z. B. das Spiel), wird um 2 dB lauter oder leiser – ohne das Pult zu öffnen.",
                () => _settings.FgHotkeysOn, v => SetControl350(() => _settings.FgHotkeysOn = v));
            p.Toggle("Xbox-Controller", "Halte die Ansicht-Taste (zwei Fenster) und: Steuerkreuz ←/→ Kanal wählen, ↑/↓ lauter/leiser (auch linker Stick), A stumm, B Solo, X Play/Pause, Y Ausgang stumm, LB/RB Titel zurück/vor, LT/RT Ausgang leiser/lauter. Ist das Pult vorne, geht es auch ohne Ansicht-Taste.",
                () => _settings.GamepadOn, v => SetControl350(() => _settings.GamepadOn = v)).Enabled = () => Win32.IsWindows;
            p.Section("3.5 Szenen");
            p.Toggle("Start-Szene", () => "Beim Start lädt das Pult immer diese Szene" + (StartSceneName() != null ? " („" + StartSceneName() + "“)" : "") + " – statt mit dem letzten Stand weiterzumachen.",
                () => _settings.StartSceneOn, v => { _settings.StartSceneOn = v; if (v && _settings.StartScene == null && _settings.Scenes.Count > 0) _settings.StartScene = _settings.Scenes[0].Id; ScheduleSave(); });
            var names = _settings.Scenes.Select(x => x.Name).ToArray();
            if (names.Length > 0)
                p.Choice("Welche Szene beim Start", () => "Die Szene, die beim Start geladen wird.", names,
                    () => Math.Max(0, _settings.Scenes.FindIndex(x => x.Id == _settings.StartScene)), i => { if (i >= 0 && i < _settings.Scenes.Count) _settings.StartScene = _settings.Scenes[i].Id; ScheduleSave(); })
                    .Enabled = () => _settings.StartSceneOn;
            p.Toggle("Überblendzeit pro Szene", "SZENEN → Überblendzeit pro Szene: jede Szene mit eigener Zeit (sofort bis 30 s), z. B. „Zocken“ sofort und „Nacht“ langsam. „Wie eingestellt“ nimmt die allgemeine Zeit.",
                () => _settings.SceneXfadeOwnOn, v => { _settings.SceneXfadeOwnOn = v; ScheduleSave(); });
            p.Toggle("Szenen-Vorschau", "Maus über einer Szene im SZENEN-Menü (oder über den Szenen-Tasten 1–4): weiße Marken an den Fadern zeigen, wo sie landen würden.",
                () => _settings.ScenePreviewOn, v => SetControl350(() => { _settings.ScenePreviewOn = v; if (!v) ClearScenePreview(); }));
            p.Section("3.5 Obergrenze");
            p.Toggle("Lautstärke-Obergrenze", () => "Kein Ausgang wird lauter als " + _settings.VolCapNow + " % – auch nicht über Windows, Tastatur oder Fader." + (_settings.VolCapHash != null ? " Ändern nur mit Passwort." : " Mit Passwort z. B. für Kinder."),
                () => _settings.VolCapOn, v => { if (!AskCapPassword()) return; _settings.VolCapOn = v; ScheduleSave(); });
            p.Choice("Höchstens", () => "So laut darf ein Ausgang höchstens werden (Windows-Lautstärke).", MixerSettings.VolCapValues.Select(x => x + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.VolCapValues, _settings.VolCapNow)), i => { if (!AskCapPassword()) return; _settings.VolCapPct = MixerSettings.VolCapValues[i]; ScheduleSave(); })
                .Enabled = () => _settings.VolCapOn;
            p.Button("Passwort für die Obergrenze", "Ohne das Passwort lässt sich die Obergrenze nicht ausschalten oder höher stellen.", "Festlegen …", r => BeginInvoke(new Action(SetCapPassword)),
                () => _settings.VolCapHash != null ? "gesetzt" : "keins");
            p.Button("Passwort entfernen", "Fragt einmal nach dem alten Passwort.", "Entfernen", r => BeginInvoke(new Action(RemoveCapPassword))).Enabled = () => _settings.VolCapHash != null;
        }

        private string StartSceneName() => _settings.Scenes.FirstOrDefault(x => x.Id == _settings.StartScene)?.Name;

        private void SetControl350(Action change)
        {
            change();
            ApplyFeatures350Statics(_settings, false);
            foreach (var s in AllStrips()) s?.Invalidate();
            ScheduleSave();
        }
    }
}
