using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.3 – Komfort: Profil auch fuer andere Apps, Timer pro Kanal, Windows-Toene stumm beim Zocken, Akku des
    /// Bluetooth-Kopfhoerers. Alles einzeln schaltbar, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 2 Timer pro Kanal

        private readonly Dictionary<string, DateTime> _chanTimer430 = new Dictionary<string, DateTime>();
        private double _nextKomfort430;

        private void SetChanTimer430(ChannelConfig c, int minutes)
        {
            if (minutes <= 0) { _chanTimer430.Remove(c.Id); ShowOsd(c.Name, "Timer aus", -1, Theme.TextDim); }
            else
            {
                _chanTimer430[c.Id] = DateTime.UtcNow.AddMinutes(minutes);
                ShowOsd(c.Name, "wird in " + minutes + " Minuten ausgeblendet", -1, ChannelStrip.TimerColor);
            }
            _nextKomfort430 = 0;
        }

        private static string TimerText430(TimeSpan left)
        {
            if (left.TotalMinutes >= 1) return ((int)Math.Ceiling(left.TotalMinutes)) + "′";
            return Math.Max(0, (int)Math.Ceiling(left.TotalSeconds)) + "″";
        }

        private void AddChannel430Items(ToolStripItemCollection items, ChannelConfig c)
        {
            if (!_settings.ChanTimerOn) return;
            bool has = _chanTimer430.TryGetValue(c.Id, out var end);
            var sub = Sub("Timer: " + (has ? "noch " + TimerText430(end - DateTime.UtcNow) : "aus"));
            sub.DropDownItems.Add(Hint("Danach wird der Kanal sanft ausgeblendet (FADE) und ist stumm."));
            foreach (int m in MixerSettings.ChanTimerMinutes)
            {
                int mm = m;
                sub.DropDownItems.Add(new ToolStripMenuItem(m + " Minuten", null, (s, e) => SetChanTimer430(c, mm)));
            }
            if (has)
            {
                sub.DropDownItems.Add(new ToolStripSeparator());
                sub.DropDownItems.Add(new ToolStripMenuItem("Timer aus", null, (s, e) => SetChanTimer430(c, 0)));
            }
            items.Add(sub);
        }

        private void TickTimers430()
        {
            if (!_settings.ChanTimerOn && _chanTimer430.Count > 0) _chanTimer430.Clear();
            var now = DateTime.UtcNow;
            foreach (var kv in _chanTimer430.ToList())
            {
                var c = ChannelById(kv.Key);
                if (c == null) { _chanTimer430.Remove(kv.Key); continue; }
                if (now < kv.Value) continue;
                _chanTimer430.Remove(kv.Key);
                if (!c.Mute && !_fades.ContainsKey(c.Id)) ToggleFade(c, false);   // sanft ausblenden, danach stumm
                ShowOsd(c.Name, "Timer abgelaufen – wird ausgeblendet", -1, ChannelStrip.TimerColor);
            }
            foreach (var s in _channelStrips)
                s.SetTimerText(_chanTimer430.TryGetValue(s.Config.Id, out var end) ? TimerText430(end - now) : null);
        }

        // ================================================================ 3 Windows-Toene stumm beim Zocken

        private bool _sysQuiet430;

        private bool SysQuiet430(string key) => _sysQuiet430 && string.Equals(key, AppKeys.System, StringComparison.OrdinalIgnoreCase);

        private void TickSysQuiet430()
        {
            bool want = _settings.SysQuietGameOn && GameRunning430;
            if (want == _sysQuiet430) return;
            _sysQuiet430 = want;
            PushPolicy();
        }

        // ================================================================ 5 Akku des Kopfhoerers

        private double _nextBattery430;
        private bool _batteryBusy430;

        private void TickBattery430(double now)
        {
            if (!_settings.BatteryOn)
            {
                if (_top.BatteryText != null) { _top.BatteryText = null; _top.Invalidate(); }
                return;
            }
            if (_batteryBusy430 || now < _nextBattery430) return;
            _nextBattery430 = now + 60;
            if (_demo)
            {
                int pct = DemoInt("BATTERYPCT", 72);
                ShowBattery430(new Battery430.Device { Name = "WH-1000XM4", Percent = pct });
                return;
            }
            var names = _snap.Devices.OrderByDescending(d => d.IsDefault).SelectMany(d => new[] { d.FullName, d.Name }).ToList();
            _batteryBusy430 = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Battery430.Device dev = null;
                try { dev = Battery430.Match(Battery430.Read(), names); } catch { }
                try { BeginInvoke(new Action(() => { _batteryBusy430 = false; ShowBattery430(dev); })); } catch { _batteryBusy430 = false; }
            });
        }

        private void ShowBattery430(Battery430.Device dev)
        {
            string text = dev == null ? null : dev.Percent + " %";
            _top.BatteryPct = dev?.Percent ?? -1;
            _top.BatteryTip = dev == null ? null : "Akku von " + dev.Name + ": " + dev.Percent + " % (so wie Windows ihn meldet)";
            if (text == _top.BatteryText) return;
            _top.BatteryText = text;
            _top.Invalidate();
        }

        // ================================================================ Takt (einmal pro Sekunde)

        private void TickKomfort430(double now)
        {
            if (now < _nextKomfort430) return;
            _nextKomfort430 = now + 1.0;
            TickTimers430();
            TickSysQuiet430();
            TickBattery430(now);
        }

        // ================================================================ Einstellungen (Automatik → 4.3)

        private void AddAuto430(SPage p)
        {
            p.Section("4.3 – Komfort");
            p.Toggle("Profil auch für andere Apps", "Unter „Profil pro Spiel“ stehen dann auch andere Apps: startet z. B. OBS, wird die Szene „Stream“ geladen, bei VLC oder Netflix „Film“ – danach kommt der vorherige Mix zurück.",
                () => _settings.AppProfilesOn, v => { _settings.AppProfilesOn = v; ScheduleSave(); });
            p.Toggle("Timer für einzelne Kanäle", "Rechtsklick auf einen Kanal → Timer: nach 15 bis 120 Minuten wird nur dieser Kanal sanft ausgeblendet (z. B. MUSIK), der Rest läuft weiter. Im Kanal steht die Restzeit.",
                () => _settings.ChanTimerOn, v => { _settings.ChanTimerOn = v; ScheduleSave(); });
            p.Toggle("Windows-Töne stumm beim Zocken", "Solange ein Spiel läuft (eine App aus dem Spiele-Kanal), sind die Windows-Systemklänge stumm – kein Ping, kein Fehlerton mitten im Spiel. Danach sind sie wieder da.",
                () => _settings.SysQuietGameOn, v => { _settings.SysQuietGameOn = v; ScheduleSave(); });
            p.Toggle("Kopfhörer-Akku in der Kopfleiste", "Zeigt den Akkustand deines Bluetooth-Kopfhörers oben im Pult (wird jede Minute gelesen). Geht nur, wenn Windows den Akkustand kennt – Kopfhörer mit eigenem USB-Funk-Stick meist nicht.",
                () => _settings.BatteryOn, v => { _settings.BatteryOn = v; _nextBattery430 = 0; ScheduleSave(); });
        }

        // ================================================================ Vorschau

        private void DemoShown430()
        {
            if (DemoOn("CHANTIMER") && _settings.Channels.Count > 3) _chanTimer430[_settings.Channels[3].Id] = DateTime.UtcNow.AddMinutes(12);
            if (DemoOn("DUCKMENU")) DemoLater(ShowDuckMenu430);
            if (DemoOn("DUCKPRESETS"))
                _settings.DuckPresets = new List<DuckPreset> { _settings.CaptureDuck("Zocken"), _settings.CaptureDuck("Stream"), _settings.CaptureDuck("Film") };
        }
    }
}
