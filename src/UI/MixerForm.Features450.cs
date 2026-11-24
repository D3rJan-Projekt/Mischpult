using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.5 – Fernbedienung als Pult: am Tablet sieht die Fernbedienung aus wie das Pult (senkrechte Fader), pro Kanal lassen sich
    /// EQ (LO/MID/HI, an/aus), Balance, SOLO und COMP regeln. Schalter „Pult-Ansicht fürs Tablet“ und „EQ, Balance und SOLO am Handy“
    /// unter der Fernbedienung (an, solange die Fernbedienung an ist).
    /// 4.5.1: alle Ausgaenge (Schalter „Alle Ausgänge am Handy“), Pegel links/rechts, EQ in 0,1-dB-Schritten.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Kanal-Werte fuer die Pult-Ansicht (an den Zeilen-Eintrag angehaengt).</summary>
        private void AppendStrip450(StringBuilder sb, ChannelConfig c)
        {
            if (!RemoteStrips450 || c == null) return;
            sb.Append(",\"eq\":[").Append(c.EqOn ? "1" : "0").Append(',').Append(F4(c.EqLow)).Append(',').Append(F4(c.EqMid)).Append(',').Append(F4(c.EqHigh)).Append(']')
              .Append(",\"pan\":").Append(F4(c.Pan)).Append(",\"solo\":").Append(c.Solo ? "true" : "false").Append(",\"comp\":").Append(c.CompOn ? "true" : "false");
        }

        private bool EqOk450 => _snap.EqSupported || _demo;
        private bool RemoteStrips450 => !_settings.RemoteEqOff && (!_settings.RemotePultOff || !_settings.RemoteStudioOff);   // 4.6: auch fuer die Studio-Ansicht

        private void AppendGlobal450(StringBuilder sb)
        {
            sb.Append(",\"pult\":").Append(_settings.RemotePultOff ? "false" : "true")
              .Append(",\"strips\":").Append(RemoteStrips450 ? "true" : "false")
              .Append(",\"eqok\":").Append(EqOk450 ? "true" : "false")
              .Append(",\"eqrange\":").Append(F4(MixerSettings.EqRange));
        }

        // ---- 4.5.1 alle Ausgaenge

        /// <summary>Weitere Ausgaenge (ohne den Standard-Ausgang, der ist "master"), ohne die am PC ausgeblendeten.</summary>
        private List<Audio.DeviceState> RemoteOutDevices451()
        {
            var def = DefaultDevice;
            var hidden = _settings.HiddenOutputs ?? new List<string>();
            return _snap.Devices.Where(d => d != null && d != def && !string.IsNullOrEmpty(d.Id) && !hidden.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>"Lautsprecher (Realtek(R) Audio)" -> "Realtek(R) Audio".</summary>
        internal static string DeviceDetail451(string full, string name)
        {
            if (string.IsNullOrEmpty(full)) return null;
            int i = full.IndexOf(" (", StringComparison.Ordinal);
            if (i < 0) return full == name ? null : full;
            string s = full.Substring(i + 2);
            if (s.EndsWith(")")) s = s.Substring(0, s.Length - 1);
            return s.Length == 0 ? null : s;
        }

        private void RemoteOutputs451(List<MiniRow> rows)
        {
            var def = DefaultDevice;
            var master = rows.FirstOrDefault(r => r.Id == "master");
            if (master == null || def == null) return;
            master.Sub = DeviceDetail451(def.FullName, def.Name) ?? def.Name;
            if (_settings.RemoteOutputsOff) return;
            var outs = RemoteOutDevices451();
            if (outs.Count == 0) return;
            master.Name = string.IsNullOrEmpty(def.Name) ? "AUSGANG" : def.Name.ToUpperInvariant();
            master.Badge = "STANDARD";
            master.Sub = DeviceDetail451(def.FullName, def.Name);
            double now = Now;
            foreach (var d in outs)
            {
                string id = "out:" + d.Id;
                float v = _miniLocal.TryGetValue(id, out var l) && now - l.Value < 0.7 ? l.Key : d.Volume;
                var pk = DevicePeak(d);
                rows.Add(new MiniRow
                {
                    Id = id, Name = string.IsNullOrEmpty(d.Name) ? "AUSGANG" : d.Name.ToUpperInvariant(), Color = Theme.Accent, Pos = v, Mute = d.Mute,
                    PeakL = pk[0], PeakR = pk[1], PreL = pk[2], PreR = pk[3], Value = (int)Math.Round(v * 100) + " %",
                    Sub = DeviceDetail451(d.FullName, d.Name),
                });
            }
        }

        private Action RemoteOut451(string name, string id, float raw)
        {
            if (_settings.RemoteOutputsOff || float.IsNaN(raw)) return () => { };
            float v = Math.Max(0f, Math.Min(1f, raw));
            string did = id.Substring(4);
            return () =>
            {
                if (_settings.RemoteOutputsOff) return;
                var d = RemoteOutDevices451().FirstOrDefault(x => string.Equals(x.Id, did, StringComparison.OrdinalIgnoreCase));
                if (d == null) return;
                if (name == "pos")
                {
                    d.Volume = v;
                    _miniLocal[id] = new KeyValuePair<float, double>(v, Now);
                    Backend.SetDeviceVolume(d.Id, v);
                }
                else
                {
                    bool on = v >= 0.5f;
                    if (d.Mute == on) return;
                    d.Mute = on;
                    Backend.SetDeviceMute(d.Id, on);
                }
                if (_outputStrips.TryGetValue(d.Id, out var os)) os.InvalidateChrome();
            };
        }

        /// <summary>Befehle der Pult-Ansicht (vom Server-Thread), null = kein 4.5-Befehl.</summary>
        private Action RemoteCommand450(string name, string id, float raw, Dictionary<string, string> a)
        {
            if ((name == "pos" || name == "mute") && id != null && id.StartsWith("out:")) return RemoteOut451(name, id, raw);   // 4.5.1
            Action a460 = RemoteCommand460(name, id, raw, a);   // 4.6: AUTO, Stream-Anteil, Musik-Tasten
            if (a460 != null) return a460;
            if (name != "eq" && name != "eqon" && name != "pan" && name != "solo" && name != "comp") return null;
            if (!RemoteStrips450 || id == null || !id.StartsWith("ch:") || float.IsNaN(raw)) return () => { };
            string cid = id.Substring(3);
            int band = -1;
            if (name == "eq" && (!a.TryGetValue("b", out string bs) || !int.TryParse(bs, NumberStyles.Integer, CultureInfo.InvariantCulture, out band) || band < 0 || band > 2)) return () => { };
            return () =>
            {
                var c = _settings.Channels.FirstOrDefault(x => x.Id == cid);
                if (c == null || !RemoteStrips450) return;
                switch (name)
                {
                    case "eq":
                    {
                        float db = Math.Max(-MixerSettings.EqRange, Math.Min(MixerSettings.EqRange, (float)Math.Round(raw * 10) / 10f));   // 4.5.1: 0,1-dB-Schritte
                        if (band == 0) c.EqLow = db; else if (band == 1) c.EqMid = db; else c.EqHigh = db;
                        if (Math.Abs(db) > 0.01f && !c.EqOn && EqOk450) c.EqOn = true;   // wie am Pult: Drehen schaltet den EQ ein
                        OnChannelEdited(c);
                        break;
                    }
                    case "eqon":
                    {
                        bool on = raw >= 0.5f;
                        if (on && !EqOk450) return;
                        if (c.EqOn != on) { c.EqOn = on; OnChannelEdited(c); }
                        break;
                    }
                    case "comp":
                    {
                        bool on = raw >= 0.5f;
                        if (on && !EqOk450) return;
                        if (c.CompOn != on) { c.CompOn = on; OnChannelEdited(c); }
                        break;
                    }
                    case "pan":
                    {
                        float p = Math.Max(-1f, Math.Min(1f, raw));
                        c.Pan = Math.Abs(p) < 0.005f ? 0f : (float)Math.Round(p, 2);   // 4.5.1: Mitte rastet im Browser ein, hier nur Rundung
                        OnChannelEdited(c);
                        break;
                    }
                    case "solo":
                    {
                        bool on = raw >= 0.5f;
                        if (c.Solo == on) return;
                        c.Solo = on;
                        OnChannelChanged(c, false);
                        foreach (var s in _channelStrips) s.InvalidateChrome();
                        break;
                    }
                }
            };
        }

        private void AddRemote450(SPage p)
        {
            AddRemote460(p);   // 4.6
            p.Toggle("Pult-Ansicht fürs Tablet", "Auf dem Tablet sieht die Fernbedienung aus wie das Pult: senkrechte Fader mit Skala und Pegelanzeige, Namensschild in der Kanalfarbe. Oben rechts zwischen „Studio“, „Pult“ und „Liste“ umschalten – am Handy startet die Liste. Aus = keine Pult-Ansicht.",
                () => !_settings.RemotePultOff, v => { _settings.RemotePultOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Alle Ausgänge am Handy", "Neben dem Standard-Ausgang auch die anderen Ausgabegeräte (z. B. Kopfhörer, HDMI) mit Lautstärke, MUTE und Pegel – ausgeblendete Ausgänge bleiben weg. Aus = nur der Standard-Ausgang.",
                () => !_settings.RemoteOutputsOff, v => { _settings.RemoteOutputsOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("EQ, Balance und SOLO am Handy", "In der Pult-Ansicht hat jeder Kanal wie am PC einen 3-Band-EQ (LO, MID, HI) mit EQ-Taste, einen Balance-Regler (PAN), SOLO und COMP. Zweimal auf einen Drehregler tippen = Mitte. Aus = nur Lautstärke und MUTE.",
                () => !_settings.RemoteEqOff, v => { _settings.RemoteEqOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn && (!_settings.RemotePultOff || !_settings.RemoteStudioOff);
        }
    }
}
