using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>4.0 – Fokus-Timer (Arbeiten/Pause mit ruhigen Kanaelen) und geplante Aufnahme.</summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 199 Fokus-Timer

        private bool _focusRun;
        private int _focusPhase;          // 0 Fokus, 1 Pause
        private DateTime _focusEnd;
        private int _focusRounds;

        public bool FocusRunning => _focusRun;

        /// <summary>Kanaele, die beim Fokus ruhig sind (ohne Auswahl: Spiel-Kanal und CHAT).</summary>
        private List<string> FocusChannelIds()
        {
            if (_settings.FocusChannels != null) return _settings.FocusChannels;
            var l = new List<string>();
            if (_settings.GameChannel != null) l.Add(_settings.GameChannel);
            var chat = _settings.Channels.FirstOrDefault(c => string.Equals(c.Name, "CHAT", StringComparison.OrdinalIgnoreCase) && !c.IsCatchAll);
            if (chat != null && !l.Contains(chat.Id)) l.Add(chat.Id);
            return l;
        }

        private float FocusDb400(ChannelConfig c)
        {
            if (!_focusRun || _focusPhase != 0 || !_settings.FocusOn) return 0f;
            return FocusChannelIds().Contains(c.Id) ? (_settings.FocusMode == 0 ? -96f : -20f) : 0f;
        }

        public void ToggleFocus()
        {
            if (!_settings.FocusOn) return;
            if (_focusRun) StopFocus(); else StartFocus();
        }

        private void StartFocus()
        {
            _focusRun = true; _focusPhase = 0; _focusRounds = 0;
            _focusEnd = DateTime.Now.AddMinutes(_settings.FocusMinNow);
            PushPolicy();
            ShowOsd("Fokus", _settings.FocusMinNow + " Minuten – " + FocusNames() + (_settings.FocusMode == 0 ? " stumm" : " leiser"), -1, FocusColor);
            RefreshTopKeys400();
        }

        private void StopFocus()
        {
            _focusRun = false;
            PushPolicy();
            ShowOsd("Fokus-Timer", "aus", -1, Theme.TextDim);
            RefreshTopKeys400();
        }

        private string FocusNames()
        {
            var names = FocusChannelIds().Select(id => _settings.Channels.FirstOrDefault(c => c.Id == id)?.Name).Where(n => n != null).ToList();
            return names.Count == 0 ? "keine Kanäle" : string.Join(", ", names);
        }

        internal static readonly Color FocusColor = Color.FromArgb(0x7d, 0xd8, 0x6a), BreakColor = Color.FromArgb(0x4c, 0xc9, 0xf0);

        private void TickFocus400()
        {
            if (!_focusRun) return;
            if (!_settings.FocusOn) { _focusRun = false; PushPolicy(); RefreshTopKeys400(); return; }
            if (DateTime.Now < _focusEnd) { RefreshTopKeys400(); return; }
            if (_focusPhase == 0)
            {
                _focusPhase = 1; _focusRounds++;
                _focusEnd = DateTime.Now.AddMinutes(_settings.BreakMinNow);
                ShowOsd("Pause", _settings.BreakMinNow + " Minuten – Runde " + _focusRounds + " geschafft", -1, BreakColor);
            }
            else
            {
                _focusPhase = 0;
                _focusEnd = DateTime.Now.AddMinutes(_settings.FocusMinNow);
                ShowOsd("Weiter", "Fokus " + _settings.FocusMinNow + " Minuten", -1, FocusColor);
            }
            if (!_settings.FocusGongOff) try { Backend.PlaySound(Gong, 0.35f, true, false, false); } catch { }
            PushPolicy();
            RefreshTopKeys400();
        }

        private static SoundClip _gong;
        /// <summary>Sanfter Gong (zwei Toene, die ausklingen).</summary>
        internal static SoundClip Gong
        {
            get
            {
                if (_gong != null) return _gong;
                const int rate = DspMixer.Rate;
                int n = rate * 2;
                var d = new float[n * 2];
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)rate;
                    double env = Math.Min(1.0, t / 0.008) * Math.Exp(-t * 2.2);
                    double v = (Math.Sin(2 * Math.PI * 523.25 * t) * 0.6 + Math.Sin(2 * Math.PI * 784.0 * t) * 0.3 + Math.Sin(2 * Math.PI * 1046.5 * t) * 0.12 * Math.Exp(-t * 4)) * env * 0.8;
                    d[i * 2] = d[i * 2 + 1] = (float)v;
                }
                return _gong = new SoundClip { Name = "Gong", Data = d, Frames = n };
            }
        }

        private string FocusKeyText()
        {
            if (!_focusRun) return "FOKUS";
            var left = _focusEnd - DateTime.Now;
            int s = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
            return (_focusPhase == 0 ? "F " : "P ") + (s / 60) + ":" + (s % 60).ToString("00");
        }

        private void ShowFocusMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            m.Items.Add(Header(_focusRun ? (_focusPhase == 0 ? "Fokus läuft" : "Pause läuft") + " – noch " + FocusKeyText().Substring(2) : "Fokus-Timer"));
            m.Items.Add(new ToolStripMenuItem(_focusRun ? "Stoppen" : "Starten (" + _settings.FocusMinNow + " min Fokus, " + _settings.BreakMinNow + " min Pause)", null, (s, e) => ToggleFocus()));
            if (_focusRun) m.Items.Add(new ToolStripMenuItem(_focusPhase == 0 ? "Jetzt Pause machen" : "Pause beenden", null, (s, e) => { _focusEnd = DateTime.Now; TickFocus400(); }));
            m.Items.Add(new ToolStripSeparator());
            var fm = Sub("Fokus: " + _settings.FocusMinNow + " min");
            foreach (var v in MixerSettings.FocusValues) { int vv = v; fm.DropDownItems.Add(new ToolStripMenuItem(v + " Minuten", null, (s, e) => { _settings.FocusMin = vv; ScheduleSave(); }) { Checked = _settings.FocusMinNow == v }); }
            m.Items.Add(fm);
            var bm = Sub("Pause: " + _settings.BreakMinNow + " min");
            foreach (var v in MixerSettings.BreakValues) { int vv = v; bm.DropDownItems.Add(new ToolStripMenuItem(v + " Minuten", null, (s, e) => { _settings.BreakMin = vv; ScheduleSave(); }) { Checked = _settings.BreakMinNow == v }); }
            m.Items.Add(bm);
            var ch = Sub("Ruhig beim Fokus: " + FocusNames());
            foreach (var c in _settings.Channels.Where(x => !x.IsCatchAll))
            {
                var cc = c;
                ch.DropDownItems.Add(new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color)), (s, e) =>
                {
                    var l = new List<string>(FocusChannelIds());
                    if (l.Contains(cc.Id)) l.Remove(cc.Id); else l.Add(cc.Id);
                    _settings.FocusChannels = l;
                    if (_focusRun) PushPolicy();
                    ScheduleSave();
                    _keepMenuOpen = true;
                }) { Checked = FocusChannelIds().Contains(c.Id) });
            }
            m.Items.Add(ch);
            m.Items.Add(new ToolStripMenuItem("Stumm statt nur leiser (−20 dB)", null, (s, e) => { _settings.FocusMode = _settings.FocusMode == 0 ? 1 : 0; if (_focusRun) PushPolicy(); ScheduleSave(); }) { Checked = _settings.FocusMode == 0 });
            m.Items.Add(new ToolStripMenuItem("Gong zum Wechsel", null, (s, e) => { _settings.FocusGongOff = !_settings.FocusGongOff; ScheduleSave(); }) { Checked = !_settings.FocusGongOff });
            m.Show(screen);
        }

        // ================================================================ 197 Geplante Aufnahme

        private bool _recPlanStarted;
        private DateTime _recPlanStop;
        private string _recPlanDoneKey;

        private void TickRecPlan400()
        {
            if (_recPlanStarted)
            {
                if (_recWriter == null) { _recPlanStarted = false; return; }   // von Hand gestoppt
                if (DateTime.Now >= _recPlanStop) { _recPlanStarted = false; StopRecording(true); ShowOsd("Geplante Aufnahme", "fertig", -1, Theme.TextDim); }
                return;
            }
            if (!_settings.RecPlanOn || _settings.RecPlanAt <= 0 || _demo) return;
            var now = DateTime.Now;
            int minute = _settings.RecPlanAt - 1;
            string key = now.ToString("yyyy-MM-dd") + "@" + minute;
            if (now.Hour * 60 + now.Minute != minute || _recPlanDoneKey == key) return;
            _recPlanDoneKey = key;
            if (_recWriter != null) return;   // laeuft schon
            StartRecording();
            if (_recWriter == null) return;
            _recPlanStarted = true;
            _recPlanStop = now.AddMinutes(_settings.RecPlanDurNow);
            ShowOsd("Geplante Aufnahme", "läuft " + _settings.RecPlanDurNow + " Minuten", -1, Theme.MuteRed);
            if (!_settings.RecPlanDaily) { _settings.RecPlanAt = 0; ScheduleSave(); }
        }

        private string RecPlanText() => _settings.RecPlanAt <= 0 ? "keine"
            : ((_settings.RecPlanAt - 1) / 60).ToString("00") + ":" + ((_settings.RecPlanAt - 1) % 60).ToString("00") + " Uhr, " + _settings.RecPlanDurNow + " min" + (_settings.RecPlanDaily ? ", täglich" : "");

        private void AddRecPlanItems400(ToolStripItemCollection items)
        {
            if (!_settings.RecPlanOn) return;
            var sub = Sub("Geplante Aufnahme: " + RecPlanText());
            sub.DropDownItems.Add(new ToolStripMenuItem("Uhrzeit festlegen …", null, (s, e) => BeginInvoke(new Action(AskRecPlanTime))));
            var dur = Sub("Dauer: " + _settings.RecPlanDurNow + " min");
            foreach (var v in MixerSettings.RecPlanDurValues) { int vv = v; dur.DropDownItems.Add(new ToolStripMenuItem(MinText(v), null, (s, e) => { _settings.RecPlanDur = vv; ScheduleSave(); }) { Checked = _settings.RecPlanDurNow == v }); }
            sub.DropDownItems.Add(dur);
            sub.DropDownItems.Add(new ToolStripMenuItem("Jeden Tag", null, (s, e) => { _settings.RecPlanDaily = !_settings.RecPlanDaily; ScheduleSave(); }) { Checked = _settings.RecPlanDaily });
            if (_settings.RecPlanAt > 0) sub.DropDownItems.Add(new ToolStripMenuItem("Nicht mehr planen", null, (s, e) => { _settings.RecPlanAt = 0; ScheduleSave(); }));
            sub.DropDownItems.Add(Hint("Nimmt auf, was oben unter „Aufnahme als …“ gewählt ist. Das Pult muss laufen."));
            items.Add(sub);
        }

        private void AskRecPlanTime()
        {
            string cur = _settings.RecPlanAt > 0 ? ((_settings.RecPlanAt - 1) / 60).ToString("00") + ":" + ((_settings.RecPlanAt - 1) % 60).ToString("00") : DateTime.Now.AddMinutes(10).ToString("HH:mm");
            using (var dlg = new InputDialog("Geplante Aufnahme", "Uhrzeit (z. B. 20:15):", cur, 5))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int m = ParseClock(dlg.Value);
                if (m < 0) { MessageBox.Show(this, "Bitte die Uhrzeit als SS:MM eingeben, z. B. 20:15.", "Mischpult"); return; }
                _settings.RecPlanAt = m + 1;
                ScheduleSave();
                ShowOsd("Geplante Aufnahme", RecPlanText(), -1, Theme.MuteRed);
            }
        }

        /// <summary>"20:15", "20.15", "2015", "8" → Minute des Tages (oder -1).</summary>
        internal static int ParseClock(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            s = s.Trim().Replace('.', ':').Replace(" Uhr", "").Replace("Uhr", "").Trim();
            int h, m = 0;
            if (s.Contains(":"))
            {
                var p = s.Split(':');
                if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m)) return -1;
            }
            else if (s.Length >= 3 && int.TryParse(s, out int hm)) { h = hm / 100; m = hm % 100; }
            else if (!int.TryParse(s, out h)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }

        // ================================================================ Einstellungen

        private void AddTimers400(SPage p)
        {
            p.Toggle("Fokus-Timer", () => "Arbeiten ohne Ablenkung: " + _settings.FocusMinNow + " Minuten sind " + FocusNames() + (_settings.FocusMode == 0 ? " stumm" : " leiser") + ", dann " + _settings.BreakMinNow + " Minuten Pause – mit sanftem Gong. Taste „FOKUS“ in der Kopfleiste (Rechtsklick: einstellen), auch per Tastenkürzel.",
                () => _settings.FocusOn, v => { _settings.FocusOn = v; if (!v && _focusRun) StopFocus(); RefreshTopKeys400(); ScheduleSave(); });
            p.Toggle("Geplante Aufnahme", () => "Eine Aufnahme startet zu einer Uhrzeit und stoppt nach der eingestellten Dauer (einmal oder täglich). Einstellen: Rechtsklick auf AUFNAHME. Jetzt: " + RecPlanText() + ".",
                () => _settings.RecPlanOn, v => { _settings.RecPlanOn = v; ScheduleSave(); });
        }
    }
}
