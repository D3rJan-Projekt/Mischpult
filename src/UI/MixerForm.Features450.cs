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
        private bool RemoteStrips450 => !_settings.RemoteEqOff && !_settings.RemotePultOff;

        private void AppendGlobal450(StringBuilder sb)
        {
            sb.Append(",\"pult\":").Append(_settings.RemotePultOff ? "false" : "true")
              .Append(",\"strips\":").Append(RemoteStrips450 ? "true" : "false")
              .Append(",\"eqok\":").Append(EqOk450 ? "true" : "false")
              .Append(",\"eqrange\":").Append(F4(MixerSettings.EqRange));
        }

        /// <summary>Befehle der Pult-Ansicht (vom Server-Thread), null = kein 4.5-Befehl.</summary>
        private Action RemoteCommand450(string name, string id, float raw, Dictionary<string, string> a)
        {
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
                        float db = Math.Max(-MixerSettings.EqRange, Math.Min(MixerSettings.EqRange, (float)Math.Round(raw * 2) / 2f));
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
                        c.Pan = Math.Abs(p) < 0.03f ? 0f : (float)Math.Round(p, 2);
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
            p.Toggle("Pult-Ansicht fürs Tablet", "Auf dem Tablet sieht die Fernbedienung aus wie das Pult: senkrechte Fader mit Skala und Pegelanzeige, Namensschild in der Kanalfarbe. Oben rechts zwischen „Pult“ und „Liste“ umschalten – am Handy startet die Liste. Aus = überall nur die Liste.",
                () => !_settings.RemotePultOff, v => { _settings.RemotePultOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("EQ, Balance und SOLO am Handy", "In der Pult-Ansicht hat jeder Kanal wie am PC einen 3-Band-EQ (LO, MID, HI) mit EQ-Taste, einen Balance-Regler (PAN), SOLO und COMP. Zweimal auf einen Drehregler tippen = Mitte. Aus = nur Lautstärke und MUTE.",
                () => !_settings.RemoteEqOff, v => { _settings.RemoteEqOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn && !_settings.RemotePultOff;
        }
    }
}
