using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Ton &amp; Komfort: Abendkurve, Geraete-Wechsel-Schutz, Ausgaenge koppeln, Panik-Taste, Zeit-Limit pro Kanal,
    /// Surround-Pegel, EQ-Presets als Datei und eine Aktion am Ende des Sleep-Timers.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyKomfort400(MixerSettings s)
        {
            OutputStrip.SurroundOn = s.SurroundOn;
            AudioEngine.SurroundWanted = s.SurroundOn;
        }

        private double _nextKomfortSec;

        private void TickKomfort400(double now, float dt)
        {
            if (_settings.SurroundOn)
            {
                _master?.UpdateSurround(dt);
                foreach (var o in _outputStrips.Values) o.UpdateSurround(dt);
            }
            if (_settings.CoupleOn && _settings.Couples != null) TickCouple400();
            if (now >= _nextKomfortSec)
            {
                _nextKomfortSec = now + 1.0;
                TickEvening400();
                TickDevices400(now);
                TickTimeLimit400(1f);
            }
            TickLimitFade400(dt);
        }

        // ================================================================ 179 Abendkurve

        private readonly Dictionary<string, float> _eveSaved = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _eveSet = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private bool _eveInside, _eveNotified;

        /// <summary>Hoechste Lautstaerke (0..1) der Abendkurve um diese Zeit, oder -1 (keine Grenze).</summary>
        internal static float EveningCeiling(MixerSettings s, DateTime t)
        {
            if (!s.EveningOn) return -1f;
            int from = s.EveningFromNow, to = s.EveningToNow;
            double minOfDay = t.Hour * 60 + t.Minute + t.Second / 60.0;
            double start = from * 60, end = to * 60;
            double since = minOfDay - start;
            if (since < 0) since += 1440;
            double length = end - start; if (length <= 0) length += 1440;
            if (since >= length) return -1f;
            double p = Math.Min(1.0, since / s.EveningRampNow);
            p = p * p * (3 - 2 * p);
            return (float)(1.0 + (s.EveningPctNow / 100.0 - 1.0) * p);
        }

        private void TickEvening400()
        {
            float cap = EveningCeiling(_settings, DateTime.Now);
            if (cap < 0f)
            {
                if (_eveInside)
                {
                    // morgens: Lautstaerke von vorher zurueck (wenn sie seitdem keiner veraendert hat)
                    foreach (var d in _snap.Devices)
                        if (_eveSaved.TryGetValue(d.Id, out float before) && _eveSet.TryGetValue(d.Id, out float set) && Math.Abs(d.Volume - set) < 0.02f)
                            try { Backend.SetDeviceVolume(d.Id, before); d.Volume = before; } catch { }
                    _eveSaved.Clear(); _eveSet.Clear();
                    _eveInside = false; _eveNotified = false;
                }
                return;
            }
            _eveInside = true;
            bool hit = false;
            foreach (var d in _snap.Devices)
            {
                if (d.Volume <= cap + 0.005f) continue;
                if (!_eveSaved.ContainsKey(d.Id)) _eveSaved[d.Id] = d.Volume;
                d.Volume = cap;
                _eveSet[d.Id] = cap;
                try { Backend.SetDeviceVolume(d.Id, cap); } catch { }
                hit = true;
            }
            if (hit)
            {
                _master.InvalidateChrome();
                foreach (var s in _outputStrips.Values) s.InvalidateChrome();
                if (!_eveNotified)
                    ShowOsd("Abendkurve", "wird langsam leiser – bis " + _settings.EveningPctNow + " %", cap, Color.FromArgb(0x9a, 0x8c, 0xff));
                _eveNotified = true;
            }
        }

        // ================================================================ 205 Geraete-Wechsel-Schutz

        private string _devLastDefault;
        private HashSet<string> _devKnown;
        private readonly Dictionary<string, double> _devFirstSeen = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private void TickDevices400(double now)
        {
            if (_snap.Devices.Length == 0) return;
            var ids = new HashSet<string>(_snap.Devices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids) if (!_devFirstSeen.ContainsKey(id)) _devFirstSeen[id] = _devKnown == null ? -100 : now;
            var def = _snap.Devices.FirstOrDefault(d => d.IsDefault);
            string defId = def?.Id;
            if (_devKnown != null && defId != null && _devLastDefault != null && !string.Equals(defId, _devLastDefault, StringComparison.OrdinalIgnoreCase))
            {
                bool brandNew = _devFirstSeen.TryGetValue(defId, out double seen) && now - seen < 8;
                bool autoSwitchTarget = _settings.AutoSwitchOn && string.Equals(_settings.AutoDevice, defId, StringComparison.OrdinalIgnoreCase);
                if (_settings.DevHoldOn && brandNew && !autoSwitchTarget && ids.Contains(_devLastDefault))
                {
                    // Windows hat von selbst auf das neue Geraet umgeschaltet: zurueck
                    string back = _devLastDefault;
                    try { Backend.SetDefaultDevice(back); } catch { }
                    ShowOsd("Standardgerät festgehalten", (def.Name ?? "Neues Gerät") + " ist da – Ton bleibt auf " + (_snap.Devices.FirstOrDefault(d => d.Id == back)?.Name ?? "dem alten Gerät"), -1, Theme.Accent);
                    _devKnown = ids;
                    return;   // _devLastDefault bleibt
                }
                if (_settings.DevGuardOn && def.Volume > _settings.DevGuardNow / 100f + 0.005f)
                {
                    float v = _settings.DevGuardNow / 100f;
                    try { Backend.SetDeviceVolume(defId, v); def.Volume = v; } catch { }
                    ShowOsd("Neues Gerät: " + (def.Name ?? "Ausgang"), "Lautstärke auf " + _settings.DevGuardNow + " % begrenzt", v, Theme.Accent);
                }
            }
            if (defId != null) _devLastDefault = defId;
            _devKnown = ids;
        }

        // ================================================================ 195 Ausgaenge koppeln

        private readonly Dictionary<string, float> _cpVol = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _cpMute = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private void TickCouple400()
        {
            foreach (var pair in _settings.Couples)
            {
                var ab = pair.Split('|');
                var a = _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, ab[0], StringComparison.OrdinalIgnoreCase));
                var b = _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, ab[1], StringComparison.OrdinalIgnoreCase));
                if (a == null || b == null) continue;
                bool known = _cpVol.ContainsKey(a.Id) && _cpVol.ContainsKey(b.Id);
                if (known)
                {
                    float da = a.Volume - _cpVol[a.Id], db = b.Volume - _cpVol[b.Id];
                    if (Math.Abs(da) > 0.004f && Math.Abs(db) < 0.004f) SetCoupled(b, b.Volume + da);
                    else if (Math.Abs(db) > 0.004f && Math.Abs(da) < 0.004f) SetCoupled(a, a.Volume + db);
                    bool ma = _cpMute[a.Id], mb = _cpMute[b.Id];
                    if (a.Mute != ma && b.Mute == mb && b.Mute != a.Mute) { try { Backend.SetDeviceMute(b.Id, a.Mute); b.Mute = a.Mute; } catch { } }
                    else if (b.Mute != mb && a.Mute == ma && a.Mute != b.Mute) { try { Backend.SetDeviceMute(a.Id, b.Mute); a.Mute = b.Mute; } catch { } }
                }
                _cpVol[a.Id] = a.Volume; _cpVol[b.Id] = b.Volume;
                _cpMute[a.Id] = a.Mute; _cpMute[b.Id] = b.Mute;
            }
        }

        private void SetCoupled(DeviceState d, float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            d.Volume = v;
            try { Backend.SetDeviceVolume(d.Id, v); } catch { }
            if (_outputStrips.TryGetValue(d.Id, out var s)) s.InvalidateChrome();
            if (d.IsDefault) _master?.InvalidateChrome();
        }

        internal static string CoupleKey(string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a + "|" + b : b + "|" + a;

        private void AddCoupleItems400(ToolStripItemCollection items, string deviceId)
        {
            if (!_settings.CoupleOn || deviceId == null) return;
            var partner = (_settings.Couples ?? new List<string>()).Select(c => c.Split('|')).FirstOrDefault(p => p.Any(x => string.Equals(x, deviceId, StringComparison.OrdinalIgnoreCase)));
            string other = partner?.FirstOrDefault(x => !string.Equals(x, deviceId, StringComparison.OrdinalIgnoreCase));
            var sub = Sub("Koppeln mit: " + (other == null ? "keinem" : _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, other, StringComparison.OrdinalIgnoreCase))?.Name ?? "Gerät (fehlt)"));
            sub.DropDownItems.Add(new ToolStripMenuItem("Keinem", null, (s, e) => SetCouple400(deviceId, null)) { Checked = other == null });
            foreach (var d in _snap.Devices)
            {
                if (string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase)) continue;
                var id = d.Id;
                sub.DropDownItems.Add(new ToolStripMenuItem(d.FullName, null, (s, e) => SetCouple400(deviceId, id)) { Checked = string.Equals(other, id, StringComparison.OrdinalIgnoreCase) });
            }
            sub.DropDownItems.Add(Hint("Gekoppelte Ausgänge werden gemeinsam lauter, leiser und stumm."));
            items.Add(sub);
        }

        private void SetCouple400(string a, string b)
        {
            if (_settings.Couples == null) _settings.Couples = new List<string>();
            _settings.Couples.RemoveAll(c => c.Split('|').Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase) || (b != null && string.Equals(x, b, StringComparison.OrdinalIgnoreCase))));
            if (b != null) _settings.Couples.Add(CoupleKey(a, b));
            if (_settings.Couples.Count == 0) _settings.Couples = null;
            _cpVol.Clear(); _cpMute.Clear();
            ScheduleSave();
            if (b != null) ShowOsd("Ausgänge gekoppelt", (_snap.Devices.FirstOrDefault(d => d.Id == a)?.Name ?? "") + " + " + (_snap.Devices.FirstOrDefault(d => d.Id == b)?.Name ?? ""), -1, Theme.Accent);
        }

        // ================================================================ 214 Panik-Taste

        private Dictionary<string, bool> _panicSaved;
        public bool PanicActive => _panicSaved != null;

        public void TogglePanic()
        {
            if (!_settings.PanicOn) return;
            if (_panicSaved == null)
            {
                _panicSaved = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in _snap.Devices)
                {
                    _panicSaved[d.Id] = d.Mute;
                    try { Backend.SetDeviceMute(d.Id, true); d.Mute = true; } catch { }
                }
                try { Backend.StopSounds(); } catch { }
                ShowOsd("PANIK", "Alles stumm – noch einmal drücken: wie vorher", -1, Theme.MuteRed);
            }
            else
            {
                foreach (var kv in _panicSaved)
                {
                    var d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                    try { Backend.SetDeviceMute(kv.Key, kv.Value); if (d != null) d.Mute = kv.Value; } catch { }
                }
                _panicSaved = null;
                ShowOsd("Panik vorbei", "Ton wie vorher", -1, Theme.LedGreen);
            }
            _master?.InvalidateChrome();
            foreach (var s in _outputStrips.Values) s.InvalidateChrome();
            RefreshTopKeys400();
            UpdateStatus();
        }

        // ================================================================ 221 Zeit-Limit pro Kanal

        private readonly Dictionary<string, float> _limitDb = new Dictionary<string, float>();   // Kanal-ID -> Absenkung (dB, gleitet)
        private readonly HashSet<string> _limitNotified = new HashSet<string>();

        private int TimeUsedSec(ChannelConfig c) => _settings.TimeUsed != null && _settings.TimeUsed.TryGetValue(c.Id, out int v) ? v : 0;
        private bool LimitReached(ChannelConfig c) => _settings.TimeLimitOn && c.TimeLimitMin > 0 && TimeUsedSec(c) >= c.TimeLimitMin * 60;

        private void TickTimeLimit400(float sec)
        {
            if (!_settings.TimeLimitOn) { if (_limitDb.Count > 0) { _limitDb.Clear(); PushPolicy(); } return; }
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_settings.TimeDate != today)
            {
                _settings.TimeDate = today; _settings.TimeUsed = null; _limitNotified.Clear();
                if (_limitDb.Count > 0) { _limitDb.Clear(); PushPolicy(); }
            }
            foreach (var c in _settings.Channels)
            {
                if (c.TimeLimitMin <= 0) continue;
                bool audible = _channelLevelDb.TryGetValue(c.Id, out float db) && db > -55f && !c.Mute;
                if (audible && !LimitReached(c))
                {
                    if (_settings.TimeUsed == null) _settings.TimeUsed = new Dictionary<string, int>();
                    _settings.TimeUsed[c.Id] = TimeUsedSec(c) + (int)sec;
                    int left = c.TimeLimitMin * 60 - TimeUsedSec(c);
                    if (left == 300) ShowOsd("Zeit-Limit · " + c.Name, "noch 5 Minuten für heute", -1, Theme.SoloAmber);
                    if ((TimeUsedSec(c) % 60) == 0) ScheduleSave();
                }
                if (LimitReached(c) && !_limitDb.ContainsKey(c.Id)) _limitDb[c.Id] = 0f;
            }
        }

        private void TickLimitFade400(float dt)
        {
            if (_limitDb.Count == 0) return;
            bool push = false;
            foreach (var id in _limitDb.Keys.ToList())
            {
                var c = _settings.Channels.FirstOrDefault(x => x.Id == id);
                if (c == null || !LimitReached(c)) { _limitDb.Remove(id); push = true; continue; }
                float v = _limitDb[id];
                if (v > -60f)
                {
                    float nv = Math.Max(-60f, v - dt * 6f);   // in 10 s ganz leise
                    if ((int)nv != (int)v || nv <= -60f) push = true;
                    _limitDb[id] = nv;
                    if (!_limitNotified.Contains(id)) { _limitNotified.Add(id); ShowOsd("Zeit um · " + c.Name, "Für heute ist Schluss – blendet aus", -1, Theme.MuteRed); }
                }
            }
            if (push) PushPolicy();
        }

        /// <summary>Zusaetzliche Absenkung eines Kanals (Zeit-Limit, Fokus-Timer) in dB.</summary>
        private float ExtraDb400(ChannelConfig c)
        {
            float db = 0f;
            if (_limitDb.TryGetValue(c.Id, out float l)) db += l <= -60f ? -96f : l;
            db += FocusDb400(c);
            return db;
        }

        private void AddTimeLimitItems400(ToolStripItemCollection items, ChannelConfig c)
        {
            if (!_settings.TimeLimitOn) return;
            int used = TimeUsedSec(c) / 60;
            string cur = c.TimeLimitMin == 0 ? "keins" : MinText(c.TimeLimitMin) + " (heute " + MinText(used) + (LimitReached(c) ? ", um" : "") + ")";
            var sub = Sub("Zeit-Limit: " + cur);
            foreach (var v in MixerSettings.TimeLimitValues)
            {
                int vv = v;
                sub.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Kein Limit" : MinText(v) + " pro Tag", null, (s, e) => SetTimeLimit400(c, vv)) { Checked = c.TimeLimitMin == v });
            }
            if (used > 0) sub.DropDownItems.Add(new ToolStripMenuItem("Heute wieder von vorn zählen", null, (s, e) => { if (!AskCapPassword()) return; _settings.TimeUsed?.Remove(c.Id); _limitNotified.Remove(c.Id); _limitDb.Remove(c.Id); PushPolicy(); ScheduleSave(); }));
            sub.DropDownItems.Add(Hint(_settings.VolCapHash != null ? "Mit Passwort geschützt (Lautstärke-Obergrenze)." : "Tipp: Mit dem Passwort der Lautstärke-Obergrenze kindersicher."));
            items.Add(sub);
        }

        private static string MinText(int min) => min < 60 ? min + " min" : (min % 60 == 0 ? (min / 60) + " Std." : (min / 60) + ":" + (min % 60).ToString("00") + " Std.");

        private void SetTimeLimit400(ChannelConfig c, int min)
        {
            if (!AskCapPassword()) return;
            c.TimeLimitMin = min;
            if (!LimitReached(c)) { _limitDb.Remove(c.Id); _limitNotified.Remove(c.Id); PushPolicy(); }
            ScheduleSave();
            ShowOsd("Zeit-Limit · " + c.Name, min == 0 ? "aus" : MinText(min) + " pro Tag", -1, Color.FromArgb(c.Color));
        }

        // ================================================================ 215 Sleep-Timer: danach

        private void SleepEnded400()
        {
            if (_settings.SleepEnd <= 0 || _demo) return;
            var dlg = new SleepEndCountdown(MixerSettings.SleepEndNames[_settings.SleepEnd], 30);
            dlg.Done += () => DoSleepEnd400(_settings.SleepEnd);
            dlg.Show();
        }

        private void DoSleepEnd400(int what)
        {
            try
            {
                switch (what)
                {
                    case 1: Win32.SendMessage(Win32.HWND_BROADCAST, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF170 /* SC_MONITORPOWER */, (IntPtr)2); break;
                    case 2: Application.SetSuspendState(PowerState.Suspend, false, false); break;
                    case 3:
                        SaveNow();
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                        break;
                }
            }
            catch { }
        }

        private void AddSleepEnd400(ToolStripItemCollection items)
        {
            var sub = Sub("Danach: " + MixerSettings.SleepEndNames[_settings.SleepEnd]);
            for (int i = 0; i < MixerSettings.SleepEndNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.SleepEndNames[i], null, (s, e) => { _settings.SleepEnd = k; ScheduleSave(); }) { Checked = _settings.SleepEnd == i });
            }
            sub.DropDownItems.Add(Hint("Vorher 30 Sekunden zum Abbrechen."));
            items.Add(sub);
        }

        // ================================================================ 220 EQ-Presets als Datei

        private void AddPresetShare400(ToolStripItemCollection items)
        {
            if (!_settings.EqShareOn) return;
            items.Add(new ToolStripMenuItem("Eigene Presets als Datei speichern …", null, (s, e) => BeginInvoke(new Action(ExportPresets400))) { Enabled = _settings.EqPresets.Count > 0 });
            items.Add(new ToolStripMenuItem("Presets aus Datei laden …", null, (s, e) => BeginInvoke(new Action(ImportPresets400))));
        }

        internal static string PresetsToJson(List<EqPreset> presets)
        {
            var ser = new DataContractJsonSerializer(typeof(List<EqPreset>));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, presets);
                return "{\"mischpult-eq-presets\":1,\"presets\":" + Encoding.UTF8.GetString(ms.ToArray()) + "}";
            }
        }

        internal static List<EqPreset> PresetsFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            int i = json.IndexOf("\"presets\"", StringComparison.Ordinal);
            string arr = json.Trim();
            if (i >= 0)
            {
                int a = json.IndexOf('[', i), b = json.LastIndexOf(']');
                if (a < 0 || b <= a) return null;
                arr = json.Substring(a, b - a + 1);
            }
            if (!arr.StartsWith("[")) return null;
            try
            {
                var ser = new DataContractJsonSerializer(typeof(List<EqPreset>));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(arr)))
                {
                    var l = ser.ReadObject(ms) as List<EqPreset>;
                    if (l == null) return null;
                    l.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
                    foreach (var p in l)
                    {
                        p.Name = p.Name.Trim(); if (p.Name.Length > 40) p.Name = p.Name.Substring(0, 40);
                        p.Low = Clamp12(p.Low); p.Mid = Clamp12(p.Mid); p.High = Clamp12(p.High);
                    }
                    return l;
                }
            }
            catch { return null; }
        }

        private static float Clamp12(float v) => float.IsNaN(v) ? 0f : Math.Max(-12f, Math.Min(12f, v));

        private void ExportPresets400()
        {
            using (var dlg = new SaveFileDialog { Title = "EQ-Presets speichern", Filter = "Mischpult-EQ-Presets (*.mpeq)|*.mpeq", FileName = "Meine EQ-Presets.mpeq" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, PresetsToJson(_settings.EqPresets), Encoding.UTF8); ShowOsd("EQ-Presets", _settings.EqPresets.Count + " gespeichert", -1, Theme.Accent); }
                catch (Exception ex) { MessageBox.Show(this, "Speichern ging nicht: " + ex.Message, "Mischpult"); }
            }
        }

        private void ImportPresets400()
        {
            using (var dlg = new OpenFileDialog { Title = "EQ-Presets laden", Filter = "Mischpult-EQ-Presets (*.mpeq)|*.mpeq|Alle Dateien|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                List<EqPreset> l = null;
                try { l = PresetsFromJson(File.ReadAllText(dlg.FileName)); } catch { }
                if (l == null || l.Count == 0) { MessageBox.Show(this, "In der Datei stehen keine EQ-Presets.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int n = MergePresets400(_settings.EqPresets, l);
                ScheduleSave();
                ShowOsd("EQ-Presets", n + " geladen", -1, Theme.Accent);
            }
        }

        /// <summary>Neue Presets dazu (gleicher Name = ersetzen). Liefert die Anzahl.</summary>
        internal static int MergePresets400(List<EqPreset> into, List<EqPreset> add)
        {
            foreach (var p in add)
            {
                into.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.CurrentCultureIgnoreCase));
                into.Add(p);
            }
            return add.Count;
        }

        // ================================================================ Einstellungen

        private void AddKomfort400(SPage p)
        {
            p.Section("4.0 Ton & Komfort");
            p.Toggle("Abendkurve", () => "Ab " + _settings.EveningFromNow + " Uhr wird der Ton über " + _settings.EveningRampNow + " Minuten langsam leiser, bis höchstens " + _settings.EveningPctNow + " % (Windows-Lautstärke). Um " + _settings.EveningToNow + " Uhr kommt die Lautstärke von vorher zurück. Anders als die Leise-Stunden ganz sanft.",
                () => _settings.EveningOn, v => { _settings.EveningOn = v; _eveNotified = false; ScheduleSave(); });
            p.Choice("Abendkurve ab", () => "Uhrzeit, ab der es leiser wird.", Enumerable.Range(16, 8).Select(h => h + " Uhr").ToArray(),
                () => Math.Max(0, Math.Min(7, _settings.EveningFromNow - 16)), i => { _settings.EveningFrom = 16 + i; if (_settings.EveningTo == 0) _settings.EveningTo = 7; ScheduleSave(); }).Enabled = () => _settings.EveningOn;
            p.Choice("Leiser werden über", () => "So lange dauert es, bis die Abendkurve unten ist.", MixerSettings.EveningRampValues.Select(v => MinText(v)).ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.EveningRampValues, _settings.EveningRampNow)), i => { _settings.EveningRampMin = MixerSettings.EveningRampValues[i]; ScheduleSave(); }).Enabled = () => _settings.EveningOn;
            p.Choice("Am Ende höchstens", () => "So laut darf es dann noch sein (Windows-Lautstärke der Ausgänge).", MixerSettings.EveningPctValues.Select(v => v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.EveningPctValues, _settings.EveningPctNow)), i => { _settings.EveningPct = MixerSettings.EveningPctValues[i]; ScheduleSave(); }).Enabled = () => _settings.EveningOn;
            p.Choice("Morgens wieder normal ab", () => "Ab dieser Uhrzeit gilt keine Grenze mehr.", Enumerable.Range(4, 8).Select(h => h + " Uhr").ToArray(),
                () => Math.Max(0, Math.Min(7, _settings.EveningToNow - 4)), i => { _settings.EveningTo = 4 + i; if (_settings.EveningFrom == 0) _settings.EveningFrom = 21; ScheduleSave(); }).Enabled = () => _settings.EveningOn;
            p.Toggle("Neues Gerät startet leise", () => "Wird ein anderes Gerät zum Standard (z. B. Bluetooth-Kopfhörer eingeschaltet), stellt das Pult seine Lautstärke auf höchstens " + _settings.DevGuardNow + " % – kein Schreck mehr.",
                () => _settings.DevGuardOn, v => { _settings.DevGuardOn = v; ScheduleSave(); });
            p.Choice("Höchstens", () => "So laut darf ein neues Gerät höchstens starten.", MixerSettings.DevGuardValues.Select(v => v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.DevGuardValues, _settings.DevGuardNow)), i => { _settings.DevGuardPct = MixerSettings.DevGuardValues[i]; ScheduleSave(); }).Enabled = () => _settings.DevGuardOn;
            p.Toggle("Standardgerät festhalten", "Schließt du ein Gerät an und Windows schaltet von selbst darauf um, schaltet das Pult zurück. Selbst gewählte Wechsel (in Windows oder im Pult) bleiben. „Headset automatisch umschalten“ hat Vorrang.",
                () => _settings.DevHoldOn, v => { _settings.DevHoldOn = v; ScheduleSave(); });
            p.Toggle("Ausgänge koppeln", "Zwei Ausgänge gemeinsam lauter, leiser und stumm – z. B. Lautsprecher und Subwoofer an zwei Anschlüssen. Koppeln: Rechtsklick auf einen Ausgang → „Koppeln mit“.",
                () => _settings.CoupleOn, v => { _settings.CoupleOn = v; _cpVol.Clear(); ScheduleSave(); });
            p.Toggle("Panik-Taste", "Ein Druck: alle Ausgänge sofort stumm, Soundboard aus. Noch ein Druck: alles wie vorher. Rote Taste in der Kopfleiste und als Tastenkürzel („Panik“).",
                () => _settings.PanicOn, v => { _settings.PanicOn = v; if (!v && _panicSaved != null) { _settings.PanicOn = true; TogglePanic(); _settings.PanicOn = false; } RefreshTopKeys400(); ScheduleSave(); });
            p.Toggle("Zeit-Limit pro Kanal", () => "Ein Kanal darf pro Tag nur so lange spielen (z. B. das Spiel 2 Stunden), dann blendet er langsam aus. Einstellen: Rechtsklick auf den Kanal → „Zeit-Limit“." +
                    (_settings.VolCapHash != null ? " Geschützt mit dem Passwort der Lautstärke-Obergrenze." : " Mit einem Passwort (Steuerung → Lautstärke-Obergrenze) kindersicher."),
                () => _settings.TimeLimitOn, v => { if (!v && !AskCapPassword()) return; _settings.TimeLimitOn = v; if (!v) { _limitDb.Clear(); PushPolicy(); } ScheduleSave(); });
            p.Toggle("Surround-Pegel", "Bei 5.1- und 7.1-Geräten zeigt das Zeiger-Feld am Ausgang jeden Kanal einzeln: vorne links/rechts, Mitte, Subwoofer, seitlich/hinten.",
                () => _settings.SurroundOn, v => { _settings.SurroundOn = v; OutputStrip.SurroundOn = v; AudioEngine.SurroundWanted = v; ScheduleSave(); });
            p.Toggle("EQ-Presets als Datei", "Im EQ-Preset-Menü eines Kanals: eigene Presets als Datei speichern und auf einem anderen PC (oder von Freunden) laden.",
                () => _settings.EqShareOn, v => { _settings.EqShareOn = v; ScheduleSave(); });
            p.Choice("Nach dem Sleep-Timer", () => _settings.SleepEnd == 0 ? "Wie bisher: nur Ton aus." : "Wenn der Sleep-Timer abgelaufen ist: " + MixerSettings.SleepEndNames[_settings.SleepEnd] + " – vorher 30 Sekunden zum Abbrechen.",
                MixerSettings.SleepEndNames, () => _settings.SleepEnd, i => { _settings.SleepEnd = i; ScheduleSave(); });
        }
    }

    /// <summary>4.0: 30 Sekunden Countdown vor der Aktion am Ende des Sleep-Timers (Abbrechen moeglich).</summary>
    internal sealed class SleepEndCountdown : Form
    {
        private readonly Timer _t = new Timer { Interval = 250 };
        private readonly DateTime _end;
        private readonly string _what;
        private readonly Button _cancel, _now;
        public event Action Done;

        public SleepEndCountdown(string what, int seconds)
        {
            _what = what;
            _end = DateTime.Now.AddSeconds(seconds);
            Text = "Sleep-Timer – Mischpult";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg; ForeColor = Theme.Text;
            ClientSize = new Size(Theme.Px(380), Theme.Px(170));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _cancel = new Button { Text = "Abbrechen", Bounds = new Rectangle(Theme.Px(200), Theme.Px(118), Theme.Px(160), Theme.Px(34)), FlatStyle = FlatStyle.Flat, BackColor = Theme.Button, ForeColor = Theme.ButtonText };
            _now = new Button { Text = "Jetzt", Bounds = new Rectangle(Theme.Px(20), Theme.Px(118), Theme.Px(120), Theme.Px(34)), FlatStyle = FlatStyle.Flat, BackColor = Theme.Button, ForeColor = Theme.ButtonText };
            _cancel.FlatAppearance.BorderColor = Theme.Accent;
            _cancel.Click += (s, e) => Close();
            _now.Click += (s, e) => { Fire(); };
            Controls.Add(_cancel); Controls.Add(_now);
            AcceptButton = _cancel; CancelButton = _cancel;
            _t.Tick += (s, e) => { if (DateTime.Now >= _end) Fire(); else Invalidate(); };
            _t.Start();
        }

        private bool _fired;
        private void Fire()
        {
            if (_fired) return;
            _fired = true;
            _t.Stop();
            Close();
            Done?.Invoke();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }
        protected override void OnFormClosed(FormClosedEventArgs e) { _t.Stop(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            int left = Math.Max(0, (int)Math.Ceiling((_end - DateTime.Now).TotalSeconds));
            using (var b = new SolidBrush(Theme.Text))
                Theme.Str(g, _what + " in " + left + " s", Theme.Ui(15f, FontStyle.Bold), b, new RectangleF(Theme.F(20), Theme.F(18), ClientSize.Width - Theme.F(40), Theme.F(34)), Theme.Left);
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, "Der Sleep-Timer ist abgelaufen. Noch wach? Dann auf „Abbrechen“.", Theme.Ui(11f), b, new RectangleF(Theme.F(20), Theme.F(60), ClientSize.Width - Theme.F(40), Theme.F(40)), Theme.Wrap);
            var bar = new RectangleF(Theme.F(20), Theme.F(102), ClientSize.Width - Theme.F(40), Theme.F(5));
            Theme.FillRound(g, Theme.Deep, bar, Theme.F(2));
            Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, bar.Width * left / 30f, bar.Height), Theme.F(2));
        }
    }
}
