using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4.6 – Ton: Stereo-Breite, Bass-Filter und Knall-Bremse pro Kanal, Mix pro Ausgabegeraet, Wecker.
    /// Alles einzeln an/aus (Standard aus).
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 116–118 Klang pro Kanal

        /// <summary>Weitere Klang-Einstellungen des Kanals fuer den Mischer (null = nichts davon an).</summary>
        private ChannelExtra ExtraFor(ChannelConfig c)
        {
            if (c == null) return null;
            float w = _settings.StereoWidthOn ? MixerSettings.WidthOf(c) : 1f;
            int hp = _settings.BassCutOn ? c.HpHz : 0;
            bool pg = _settings.PopGuardOn && c.PopGuard;
            int lp = _settings.TrebleCutOn ? c.LpHz : 0;     // 3.5: Hoehen-Filter
            bool dl = _settings.DialogOn && c.Dialog;          // 3.5: Stimmen nach vorne
            var x = new ChannelExtra(w, hp, pg, lp, dl, PeqFor(c), GroupFxFor(c), ProFor(c));   // 4.0: parametrischer EQ, Gruppen-Klang, Profi-Ton
            return x.IsNeutral ? null : x;
        }

        private void AddChannel346Items(ToolStripItemCollection items, ChannelConfig c)
        {
            if (!_settings.StereoWidthOn && !_settings.BassCutOn && !_settings.PopGuardOn && !AnyKlang350) return;
            var sub = Sub("Klang: " + KlangState(c));
            if (_settings.StereoWidthOn)
            {
                var w = Sub("Stereo-Breite");
                for (int i = 0; i < MixerSettings.StereoWidthValues.Length; i++)
                {
                    int v = MixerSettings.StereoWidthValues[i];
                    bool cur = (c.WidthPct == 0 ? 100 : c.WidthPct) == v;
                    w.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.StereoWidthNames[i], null, (s, e) => { c.WidthPct = v == 100 ? 0 : v; PushPolicy(); ScheduleSave(); }) { Checked = cur });
                }
                sub.DropDownItems.Add(w);
            }
            if (_settings.BassCutOn)
            {
                var h = Sub("Bass-Filter");
                foreach (var v in MixerSettings.HpValues)
                {
                    int vv = v;
                    h.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : "unter " + v + " Hz weg", null, (s, e) => { c.HpHz = vv; PushPolicy(); ScheduleSave(); }) { Checked = c.HpHz == v });
                }
                h.DropDownItems.Add(Hint("Nimmt Rumpeln und Brummen weg – z. B. für Stimmen im Chat."));
                sub.DropDownItems.Add(h);
            }
            if (_settings.PopGuardOn)
                sub.DropDownItems.Add(new ToolStripMenuItem("Knall-Bremse", null, (s, e) => { c.PopGuard = !c.PopGuard; PushPolicy(); ScheduleSave(); }) { Checked = c.PopGuard });
            AddChannel350Items(sub, c);   // 3.5: Hoehen-Filter, Stimmen nach vorne
            sub.DropDownItems.Add(Hint("Läuft dafür durchs Mischpult (wie beim EQ)."));
            items.Add(sub);
        }

        private string KlangState(ChannelConfig c)
        {
            var parts = new List<string>();
            if (_settings.StereoWidthOn && c.WidthPct != 0) parts.Add(c.WidthPct == 1 ? "mono" : c.WidthPct + " %");
            if (_settings.BassCutOn && c.HpHz > 0) parts.Add("ab " + c.HpHz + " Hz");
            if (_settings.PopGuardOn && c.PopGuard) parts.Add("Bremse");
            KlangState350(parts, c);
            return parts.Count == 0 ? "normal" : string.Join(", ", parts);
        }

        // ================================================================ 120 Mix pro Ausgabegeraet

        private string _mixDevId;
        private double _nextMixCheck;

        private void TickDeviceMix(double now)
        {
            if (now < _nextMixCheck) return;
            _nextMixCheck = now + 0.5;
            var d = DefaultDevice;
            string id = d?.Id;
            if (id == null || id == _mixDevId) return;
            string old = _mixDevId;
            _mixDevId = id;
            if (!_settings.MixPerDeviceOn || old == null) return;
            // Mix des alten Geraets merken, gemerkten Mix des neuen laden
            if (_settings.DeviceMixes == null) _settings.DeviceMixes = new Dictionary<string, Dictionary<string, float>>();
            _settings.DeviceMixes[old] = _settings.Channels.Where(c => c?.Id != null).ToDictionary(c => c.Id, c => c.Fader);
            if (_settings.DeviceMixes.TryGetValue(id, out var mix) && mix != null)
            {
                foreach (var c in _settings.Channels)
                    if (c?.Id != null && mix.TryGetValue(c.Id, out float f) && !float.IsNaN(f)) { c.Fader = Math.Max(0f, Math.Min(1f, f)); ClampToCap(c); }
                PushPolicy();
                foreach (var s in _channelStrips) s.InvalidateChrome();
                ShowOsd("Mix für " + d.Name, "deine Fader für dieses Gerät", -1, Theme.Accent);
            }
            ScheduleSave();
        }

        // ================================================================ 121 Wecker

        private DateTime _alarmFired = DateTime.MinValue;
        private double _alarmStart = -1, _alarmDur;
        private string _alarmDev;
        private float _alarmTarget, _alarmLastSet = -1f;
        private double _nextAlarmCheck;

        private static bool AlarmDayOk(int mode, DayOfWeek d) =>
            mode == 0 || (mode == 1 ? d != DayOfWeek.Saturday && d != DayOfWeek.Sunday : d == DayOfWeek.Saturday || d == DayOfWeek.Sunday);

        private void TickAlarm(double now)
        {
            if (_alarmStart >= 0) { TickAlarmRamp(now); return; }
            if (!_settings.AlarmOn || now < _nextAlarmCheck) return;
            _nextAlarmCheck = now + 1.0;
            var t = DateTime.Now;
            if (t.Date == _alarmFired.Date) return;
            if (t.Hour != _settings.AlarmHourNow || t.Minute != _settings.AlarmMinute || !AlarmDayOk(_settings.AlarmDays, t.DayOfWeek)) return;
            _alarmFired = t;
            StartAlarm(now, _settings.AlarmFadeNow * 60.0);
        }

        private void StartAlarm(double now, double seconds)
        {
            var d = DefaultDevice;
            if (d == null) return;
            _alarmDev = d.Id;
            _alarmTarget = _settings.AlarmVolumeNow / 100f;
            _alarmDur = Math.Max(5.0, seconds);
            _alarmStart = now;
            _alarmLastSet = 0f;
            try { if (d.Mute) Backend.SetDeviceMute(d.Id, false); } catch { }
            try { Backend.SetDeviceVolume(d.Id, 0f); } catch { }
            if (!_settings.AlarmNoPlay) System.Threading.Tasks.Task.Run(() => { try { NowPlayingWatcher.ControlAll(false, null); } catch { } });
            ShowOsd("Wecker", "Guten Morgen – wird langsam lauter", -1, Color.FromArgb(0xff, 0xc8, 0x57));
        }

        private void TickAlarmRamp(double now)
        {
            var d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, _alarmDev, StringComparison.OrdinalIgnoreCase));
            if (d == null) { _alarmStart = -1; return; }
            double el = now - _alarmStart;
            // jemand hat selbst an der Lautstaerke gedreht: Wecker hoert auf zu regeln
            if (el > 1.5 && _alarmLastSet >= 0 && Math.Abs(d.Volume - _alarmLastSet) > 0.04f) { _alarmStart = -1; return; }
            float u = (float)Math.Min(1.0, el / _alarmDur);
            float v = _alarmTarget * u * u * (3f - 2f * u);
            if (Math.Abs(v - _alarmLastSet) >= 0.004f || u >= 1f) { Backend.SetDeviceVolume(_alarmDev, v); _alarmLastSet = v; }
            if (u >= 1f) _alarmStart = -1;
        }

        private void TickFeatures346(double now)
        {
            TickDeviceMix(now);
            TickAlarm(now);
            TickFeatures346b(now);
        }

        // ================================================================ Einstellungen (Ton)

        private void AddSound346(SPage p)
        {
            p.Section("Klang pro Kanal");
            p.Toggle("Stereo-Breite pro Kanal", "Rechtsklick auf einen Kanal → Klang → Stereo-Breite: von Mono über schmal bis extra breit – z. B. Musik breiter, Chat schmaler. Läuft dafür durchs Mischpult.",
                () => _settings.StereoWidthOn, v => { _settings.StereoWidthOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Bass-Filter pro Kanal", "Rechtsklick auf einen Kanal → Klang → Bass-Filter: nimmt alles unter 80, 120 oder 200 Hz weg – gegen Rumpeln und Brummen, z. B. bei Stimmen im Chat.",
                () => _settings.BassCutOn, v => { _settings.BassCutOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Knall-Bremse pro Kanal", "Rechtsklick auf einen Kanal → Klang → Knall-Bremse: wird der Kanal plötzlich viel lauter als eben noch (Werbung, Jumpscare), fängt das Pult ihn sofort ab und lässt ihn in gut einer Sekunde wieder hoch. Normale Spitzen bleiben unberührt.",
                () => _settings.PopGuardOn, v => { _settings.PopGuardOn = v; PushPolicy(); ScheduleSave(); });
            p.Section("Ausgabegerät");
            p.Toggle("Mix pro Ausgabegerät", "Wechselst du das Standard-Gerät (z. B. Kopfhörer ↔ Lautsprecher), merkt sich das Pult die Fader des alten Geräts und holt die des neuen zurück – jedes Gerät hat seinen eigenen Mix.",
                () => _settings.MixPerDeviceOn, v => { _settings.MixPerDeviceOn = v; ScheduleSave(); });
            p.Section("Wecker");
            p.Toggle("Wecker", () => "Um " + _settings.AlarmHourNow + ":" + _settings.AlarmMinute.ToString("00") + " Uhr (" + MixerSettings.AlarmDayNames[_settings.AlarmDays] + ") startet die Musik leise und wird in " + _settings.AlarmFadeNow + " Minuten bis " + _settings.AlarmVolumeNow + " % lauter. Der PC muss an sein und Spotify (o. ä.) offen. Drehst du selbst an der Lautstärke, hört der Wecker auf zu regeln.",
                () => _settings.AlarmOn, v => { _settings.AlarmOn = v; ScheduleSave(); });
            var hours = Enumerable.Range(0, 24).Select(h => h.ToString()).ToArray();
            p.Choice("Stunde", () => "Weck-Uhrzeit: Stunde.", hours, () => _settings.AlarmHourNow, i => { int m = _settings.AlarmMinute; _settings.AlarmHour = i; _settings.AlarmMinute = m; if (i == 0 && m == 0) _settings.AlarmMinute = 0; ScheduleSave(); })
                .Enabled = () => _settings.AlarmOn;
            var mins = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToArray();
            p.Choice("Minute", () => "Weck-Uhrzeit: Minute.", mins, () => Math.Min(11, _settings.AlarmMinute / 5), i => { int h = _settings.AlarmHourNow; _settings.AlarmMinute = i * 5; _settings.AlarmHour = h; ScheduleSave(); })
                .Enabled = () => _settings.AlarmOn;
            p.Choice("Tage", () => "An welchen Tagen.", MixerSettings.AlarmDayNames, () => _settings.AlarmDays, i => { _settings.AlarmDays = i; ScheduleSave(); })
                .Enabled = () => _settings.AlarmOn;
            p.Choice("Lauter werden in", () => "So lange dauert es von leise bis zur Ziel-Lautstärke.", MixerSettings.AlarmFadeValues.Select(v => v + " min").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.AlarmFadeValues, _settings.AlarmFadeNow)), i => { _settings.AlarmFadeMin = MixerSettings.AlarmFadeValues[i]; ScheduleSave(); })
                .Enabled = () => _settings.AlarmOn;
            p.Choice("Bis", () => "Ziel-Lautstärke (Windows-Lautstärke des Standard-Geräts).", MixerSettings.AlarmVolumeValues.Select(v => v + " %").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.AlarmVolumeValues, _settings.AlarmVolumeNow)), i => { _settings.AlarmVolume = MixerSettings.AlarmVolumeValues[i]; ScheduleSave(); })
                .Enabled = () => _settings.AlarmOn;
            p.Toggle("Auf Play drücken", "Beim Wecken die Musik starten (wie die Play-Taste). Aus = nur lauter machen, was schon läuft.",
                () => !_settings.AlarmNoPlay, v => { _settings.AlarmNoPlay = !v; ScheduleSave(); }).Enabled = () => _settings.AlarmOn;
            p.Button("Wecker ausprobieren", "Jetzt so tun, als wäre es so weit – in 15 Sekunden laut.", "Testen", r => StartAlarm(_clock.Elapsed.TotalSeconds, 15));
        }
    }
}
