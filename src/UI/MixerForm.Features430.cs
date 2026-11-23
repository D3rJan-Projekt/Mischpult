using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.3 – Ducking: zeigen, wer ausloest (DUCKING-Taste und Kanal), Taste zum Halten, Soundboard als Ausloeser,
    /// Vorlagen und "nur, wenn …". Alles einzeln schaltbar, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private const string HoldId430 = "@hold";
        private bool _duckWaits430;
        private string _duckWhoSig430;

        // ================================================================ 13 nur, wenn …

        /// <summary>Laeuft gerade ein Spiel? (eine App aus dem Spiele-Kanal hat Ton geoeffnet)</summary>
        private bool GameRunning430
        {
            get
            {
                if (_demo && DemoOn("GAMERUNNING")) return true;
                var gc = GameChannel;
                if (gc == null) return false;
                foreach (var k in gc.Apps) if (_appsByKey.ContainsKey(k)) return true;
                return false;
            }
        }

        /// <summary>Darf das Ducking gerade arbeiten?</summary>
        private bool DuckAllowed430()
        {
            if (!_settings.DuckWhenOn) return true;
            bool game = GameRunning430, hours = _settings.InDuckHours(DateTime.Now);
            switch (_settings.DuckWhenMode)
            {
                case 1: return hours;
                case 2: return game && hours;
                default: return game;
            }
        }

        private string DuckWaitReason430 =>
            _settings.DuckWhenMode == 1 ? "nicht in der Zeit" : _settings.DuckWhenMode == 2 ? "kein Spiel oder nicht in der Zeit" : "kein Spiel";

        // ================================================================ 10 Taste zum Halten

        private int _holdVk430;
        private bool HoldDuck430 => _holdVk430 != 0;

        private void StartHold430()
        {
            int keys = HotkeyKeys("duck.hold");
            int vk = keys & (int)Keys.KeyCode;
            if (vk == 0) return;
            _holdVk430 = vk;
        }

        private void TickHold430()
        {
            if (_holdVk430 == 0) return;
            bool down = false;
            try { down = (GetAsyncKeyState(_holdVk430) & 0x8000) != 0; } catch { }
            if (!down || !_settings.DuckHoldKeyOn) _holdVk430 = 0;   // losgelassen: Musik kommt zurueck (Ducking-Rueckweg)
        }

        private void AddHotkeyActions430(List<HotkeyAction> list)
        {
            if (_settings.DuckHoldKeyOn) list.Add(new HotkeyAction("duck.hold", "Ducking halten (solange gedrückt)", "Allgemein"));
        }

        private bool ExecuteHotkey430(string action)
        {
            if (action == "duck.hold") { if (_settings.DuckHoldKeyOn) StartHold430(); return true; }
            return false;
        }

        // ================================================================ 14 wer loest aus

        /// <summary>Ausloeser, die gerade wirken (Hauptducking, Taste, Regeln).</summary>
        private List<string> DuckSources430(double now)
        {
            var src = new List<string>();
            if (_duckActive)
                foreach (var t in _settings.DuckTriggerList)
                    if (_duckFired423.TryGetValue(t ?? "", out double at) && now - at <= _settings.DuckHold + 0.35 && !src.Contains(t)) src.Add(t);
            if (HoldDuck430) src.Add(HoldId430);
            var rules = _settings.DuckRulesOn ? _settings.DuckRules : null;
            if (rules != null)
                for (int i = 0; i < rules.Count && i < _ruleCur.Length; i++)
                    if (_ruleCur[i] < -0.25f && rules[i].Trigger != null && !src.Contains(rules[i].Trigger)) src.Add(rules[i].Trigger);
            return src;
        }

        private void TickDuckWho430(double now)
        {
            bool on = _settings.DuckWhoOn;
            var src = on ? DuckSources430(now) : new List<string>();
            float db = _duckApplied < -0.25f ? _duckApplied : 0f;
            if (db == 0f && _ruleCur.Length > 0) db = Math.Min(0f, _ruleCur.Min());
            string text = null, tip = null;
            if (on && src.Count > 0)
            {
                var names = src.Select(DuckTriggerName).ToList();
                string who = names.Count > 2 ? names[0] + " +" + (names.Count - 1) : string.Join("+", names);
                text = who + (db < -0.25f ? " " + ((int)Math.Round(db)).ToString(System.Globalization.CultureInfo.InvariantCulture).Replace("-", "−") : "");
                var targets = _settings.DuckTargets.Select(id => ChannelById(id)?.Name).Where(n => n != null).ToList();
                tip = "Ducking: " + string.Join(", ", names) + (names.Count == 1 ? " löst" : " lösen") + " gerade aus" +
                      (targets.Count > 0 ? " – " + string.Join(", ", targets) + " werden leiser" : "") + (db < -0.25f ? " (" + ((int)Math.Round(db)) + " dB)." : ".") +
                      "\nKlick: Ducking an/aus – Rechtsklick: einstellen";
            }
            else if (on && _duckWaits430)
            {
                text = "WARTET";
                tip = "Ducking wartet: " + DuckWaitReason430 + " (OPTIONEN → Automatik → „Ducking nur, wenn …“).\nKlick: Ducking an/aus – Rechtsklick: einstellen";
            }
            string sig = (text ?? "") + "|" + string.Join(",", src);
            if (sig == _duckWhoSig430) return;
            _duckWhoSig430 = sig;
            _top.DuckText = text;
            _top.DuckTip = tip;
            _top.Invalidate();
            foreach (var s in _channelStrips) s.SetDuckSource(on && src.Contains(s.Config.Id));
            _mic?.SetDuckSource(on && src.Contains(MixerSettings.DuckMicId));
        }

        // ================================================================ 12 Vorlagen

        private void ShowDuckMenu430() => ShowDuckMenu430(_top.DuckButtonScreen);

        private void ShowDuckMenu430(Rectangle at)
        {
            var m = NewMenu();
            m.Items.Add(new ToolStripMenuItem("Ducking einstellen …", null, (s, e) => BeginInvoke(new Action(ShowDuckDialog))));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("Vorlagen"));
            var list = _settings.DuckPresets ?? new List<DuckPreset>();
            if (list.Count == 0) m.Items.Add(Hint("Noch keine – unten die aktuelle Einstellung speichern."));
            foreach (var p in list)
            {
                var pp = p;
                m.Items.Add(new ToolStripMenuItem(p.Name, null, (s, e) => ApplyDuckPreset430(pp)) { ToolTipText = PresetTip430(p) });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Aktuelle Einstellung als Vorlage speichern …", null, (s, e) => BeginInvoke(new Action(SaveDuckPreset430))) { Enabled = list.Count < 12 });
            if (list.Count > 0)
            {
                var del = Sub("Vorlage löschen");
                foreach (var p in list)
                {
                    var pp = p;
                    del.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (s, e) =>
                    {
                        _settings.DuckPresets?.Remove(pp);
                        if (_settings.DuckPresets?.Count == 0) _settings.DuckPresets = null;
                        ScheduleSave();
                    }));
                }
                m.Items.Add(del);
            }
            ShowMenuAt(m, at);
        }

        private string PresetTip430(DuckPreset p)
        {
            var trig = (p.Triggers != null && p.Triggers.Count > 0 ? p.Triggers : new List<string> { p.Trigger }).Where(t => t != null).Select(DuckTriggerName);
            var targets = (p.Targets ?? new List<string>()).Select(id => ChannelById(id)?.Name).Where(n => n != null);
            return (p.On ? "" : "(aus) ") + string.Join(", ", trig) + " → " + string.Join(", ", targets) + ": " + (int)Math.Round(p.Amount) + " dB";
        }

        private void SaveDuckPreset430()
        {
            using (var dlg = new InputDialog("Ducking-Vorlage", "Name der Vorlage (z. B. Zocken, Stream, Film):", "", 30))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
                if (_settings.DuckPresets == null) _settings.DuckPresets = new List<DuckPreset>();
                _settings.DuckPresets.RemoveAll(p => string.Equals(p.Name, dlg.Value, StringComparison.CurrentCultureIgnoreCase));
                _settings.DuckPresets.Add(_settings.CaptureDuck(dlg.Value));
                ScheduleSave();
                ShowOsd("Ducking-Vorlage gespeichert", dlg.Value, -1, Theme.SoloAmber);
            }
        }

        private void ApplyDuckPreset430(DuckPreset p)
        {
            _settings.ApplyDuck(p);
            _settings.Normalize();
            _leak.Reset(); _leaks.Clear();
            _top.DuckOn = _settings.DuckOn;
            _top.Invalidate();
            _duckActive = !_duckActive;   // Anzeige im naechsten Tick neu setzen
            PushPolicy();
            ScheduleSave();
            ShowOsd("Ducking-Vorlage", p.Name, -1, Theme.SoloAmber);
        }

        // ================================================================ Takt

        private void Tick430(double now, float dt)
        {
            TickHold430();
            TickDuckWho430(now);
            TickKomfort430(now);
            TickLook430(now, dt);
            TickRemote440(now);   // 4.4: Fernbedienung im Browser
        }

        // ================================================================ Einstellungen (Automatik → Ducking)

        private void AddDuck430(SPage p)
        {
            p.Toggle("DUCKING-Taste zeigt, wer auslöst", "Während das Ducking wirkt, steht auf der DUCKING-Taste, wer auslöst und wie viel leiser es ist (z. B. „MIKRO −15“). Der auslösende Kanal zeigt „LÖST AUS“, die leiseren Kanäle „DUCK“.",
                () => _settings.DuckWhoOn, v => { _settings.DuckWhoOn = v; _duckWhoSig430 = null; ScheduleSave(); });
            p.Toggle("Taste zum Halten", "Solange du eine Taste gedrückt hältst, wird die Musik leiser – z. B. wenn jemand ins Zimmer kommt. Die Taste legst du unter OPTIONEN → Steuerung → Tastenkürzel bei „Ducking halten“ fest.",
                () => _settings.DuckHoldKeyOn, v => { _settings.DuckHoldKeyOn = v; ApplyHotkeys(); ScheduleSave(); });
            p.Button("Taste festlegen", "", "Tastenkürzel …", r => ShowHotkeyDialog()).Visible = () => _settings.DuckHoldKeyOn;
            p.Toggle("Ducking-Vorlagen", "Ducking-Einstellungen unter einem Namen speichern (z. B. „Zocken“, „Stream“, „Film“) und per Rechtsklick auf DUCKING mit einem Klick wechseln.",
                () => _settings.DuckPresetsOn, v => { _settings.DuckPresetsOn = v; ScheduleSave(); });
            p.Button("Vorlagen", "", "Bearbeiten ▾", r => ShowDuckMenu430(r), () => (_settings.DuckPresets?.Count ?? 0) + " Vorlagen").Visible = () => _settings.DuckPresetsOn;
            p.Toggle("Ducking nur, wenn …", "Das Ducking arbeitet nur, wenn ein Spiel läuft und/oder zu bestimmten Uhrzeiten. Sonst bleibt die Musik in Ruhe.",
                () => _settings.DuckWhenOn, v => { _settings.DuckWhenOn = v; ScheduleSave(); });
            p.Choice("Wann", () => "", MixerSettings.DuckWhenNames, () => _settings.DuckWhenMode, i => { _settings.DuckWhenMode = i; ScheduleSave(); }).Visible = () => _settings.DuckWhenOn;
            p.Slider("Von", "Ab dieser Uhrzeit.", 0, 23, 1, () => _settings.DuckWhenFrom, (v, done) => { _settings.DuckWhenFrom = (int)Math.Round(v); if (done) ScheduleSave(); }, v => (int)Math.Round(v) + " Uhr")
                .Visible = () => _settings.DuckWhenOn && _settings.DuckWhenMode != 0;
            p.Slider("Bis", "Bis zu dieser Uhrzeit (über Mitternacht geht auch, z. B. 18 bis 2 Uhr).", 0, 23, 1, () => _settings.DuckWhenTo, (v, done) => { _settings.DuckWhenTo = (int)Math.Round(v); if (done) ScheduleSave(); }, v => (int)Math.Round(v) + " Uhr")
                .Visible = () => _settings.DuckWhenOn && _settings.DuckWhenMode != 0;
        }
    }
}
