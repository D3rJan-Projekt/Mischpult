using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.6 – Fernbedienung als Studio-Pult (Tablet): Ansicht im Stil grosser Digitalpulte mit MIX (Kanalzuege mit Pegel,
    /// Balance, MUTE, SOLO), EDIT (EQ-Kurve zum Ziehen, Dynamik COMP/AUTO, Apps, Stream-Anteil), PEGEL-Uebersicht, Szenen,
    /// DUCKING und Musik-Tasten. Schalter „Studio-Ansicht fürs Tablet“ und „Musik-Tasten am Handy“ unter der Fernbedienung.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private bool RemoteStudio460 => !ROff470(_settings.RemoteStudioOff);

        /// <summary>Zusaetzliche Kanal-Werte: AUTO und Stream-Anteil ([aus 0/1, dB]).</summary>
        private void AppendStrip460(StringBuilder sb, ChannelConfig c)
        {
            if (!RemoteStrips450 || c == null) return;
            sb.Append(",\"auto\":").Append(c.AutoOn ? "true" : "false");
            if (_settings.StreamOn)
                sb.Append(",\"str\":[").Append(c.StreamOff || c.StreamDb <= ChannelStrip.StreamOffDb ? "1" : "0").Append(',').Append(F4(c.StreamDb)).Append(']');
        }

        private void AppendGlobal460(StringBuilder sb)
        {
            sb.Append(",\"studio\":").Append(RemoteStudio460 ? "true" : "false")
              .Append(",\"streamon\":").Append(_settings.StreamOn ? "true" : "false")
              .Append(",\"scene\":").Append(J(ROff470(_settings.RemoteScenesOff) ? null : _sceneLit346))
              .Append(",\"media\":").Append(ROff470(_settings.RemoteMediaOff) ? "false" : "true");
            AppendGlobal480(sb);   // 4.8
            var np = !ROff470(_settings.RemoteMediaOff) && !_settings.NowPlayingOff ? _nowPlaying?.Current : null;
            if (np != null && (!string.IsNullOrEmpty(np.Title) || !string.IsNullOrEmpty(np.Artist)))
                sb.Append(",\"np\":{\"t\":").Append(J(np.Title)).Append(",\"a\":").Append(J(np.Artist)).Append(",\"p\":").Append(np.Playing ? "true" : "false").Append('}');
        }

        /// <summary>Befehle der Studio-Ansicht (vom Server-Thread), null = kein 4.6-Befehl.</summary>
        private Action RemoteCommand460(string name, string id, float raw, Dictionary<string, string> a)
        {
            if (name == "media")
            {
                if (ROff470(_settings.RemoteMediaOff) || !a.TryGetValue("k", out string k) || (k != "play" && k != "next" && k != "prev")) return () => { };
                return () => { if (!ROff470(_settings.RemoteMediaOff)) SendMedia(k, false); };
            }
            if (name != "auto" && name != "str") return null;
            if (!RemoteStrips450 || id == null || !id.StartsWith("ch:") || float.IsNaN(raw)) return () => { };
            string cid = id.Substring(3);
            return () =>
            {
                var c = _settings.Channels.FirstOrDefault(x => x.Id == cid);
                if (c == null || !RemoteStrips450) return;
                if (name == "auto")
                {
                    bool on = raw >= 0.5f;
                    if (on && !EqOk450) return;
                    if (c.AutoOn == on) return;
                    c.AutoOn = on;
                }
                else
                {
                    if (!_settings.StreamOn) return;
                    float db = Math.Max(ChannelStrip.StreamOffDb, Math.Min(ChannelStrip.StreamMaxDb, (float)Math.Round(raw * 10) / 10f));
                    c.StreamOff = db <= ChannelStrip.StreamOffDb;
                    c.StreamDb = db;
                }
                OnChannelChanged(c, false);
                _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
            };
        }

        private void AddRemote460(SPage p)
        {
            AddRemote480(p);   // 4.8
            p.Toggle("Studio-Ansicht fürs Tablet", "Die Fernbedienung im Stil großer Digitalpulte: MIX mit allen Kanalzügen, EDIT mit EQ-Kurve zum Ziehen, Dynamik (COMP, AUTO), Apps und Stream-Anteil, eine PEGEL-Übersicht, rechts Ebenen, Szenen, DUCKING und ALLE STUMM. Oben links zwischen „Studio“, „Pult“ und „Liste“ umschalten. Aus = Pult bzw. Liste wie bisher.",
                () => !_settings.RemoteStudioOff, v => { _settings.RemoteStudioOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Musik-Tasten am Handy", "Zurück, Play/Pause und Weiter für die Musik am PC und oben im Display, was gerade läuft.",
                () => !_settings.RemoteMediaOff, v => { _settings.RemoteMediaOff = !v; ScheduleSave(); }).Visible = () => _settings.RemoteOn && !_settings.RemoteStudioOff;
        }
    }
}
