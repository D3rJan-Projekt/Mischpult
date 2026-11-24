using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.8 – mehr fuers Studio-Pult am Handy: Aufnahme, Spektrum (RTA) hinter der EQ-Kurve, Mute-Gruppen, Szenen speichern,
    /// Name und Farbe eines Kanals, Lautheit (LUFS). Jeder Teil mit eigenem Schalter unter der Fernbedienung, zuerst aus –
    /// im Studio-Fenster am PC (4.7) immer dabei.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private string _rtaId480;
        private double _rtaUntil480;

        /// <summary>Schalter „… am Handy“ (zuerst aus) – im Studio-Fenster am PC immer an.</summary>
        private static bool ROn480(bool on) => on || _rl470;

        private bool RemoteWatched480(RemoteServer440 s) => s != null && (DateTime.UtcNow - s.LastRequestUtc).TotalSeconds < 6;

        /// <summary>Lautheit messen, solange jemand in der Fernbedienung bzw. im Studio-Fenster zuschaut.</summary>
        private bool NeedsLoudness480 => (_settings.RemoteOn && _settings.RemoteLufsOn && RemoteWatched480(_remote)) || (_settings.StudioWinOn && RemoteWatched480(_studio470));

        private void Tick480(double now)
        {
            if (_rtaId480 != null && now > _rtaUntil480)
            {
                _rtaId480 = null;
                if (_eqWin == null || _eqWin.IsDisposed) Backend.SetSpectrumChannel(null);
            }
        }

        private static string RecTime480(double secs)
        {
            int sec = (int)secs;
            return (sec >= 3600 ? (sec / 3600) + ":" + (sec / 60 % 60).ToString("00") : (sec / 60).ToString(CultureInfo.InvariantCulture)) + ":" + (sec % 60).ToString("00");
        }

        private static string Hex480(int argb) => "#" + (argb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

        private void AppendStrip480(StringBuilder sb, ChannelConfig c)
        {
            if (c != null && ROn480(_settings.RemoteMuteGrpOn) && MuteGroupHit400(c)) sb.Append(",\"mgm\":true");
        }

        private void AppendGlobal480(StringBuilder sb)
        {
            if (ROn480(_settings.RemoteRecOn))
            {
                var w = _recWriter;
                sb.Append(",\"rec\":{\"on\":").Append(w != null ? "true" : "false").Append(",\"t\":").Append(J(w != null ? RecTime480(w.Seconds) : null)).Append('}');
            }
            if (ROn480(_settings.RemoteRtaOn) && RemoteStrips450)
            {
                sb.Append(",\"rtaok\":true");
                var sp = _snap.Spectrum;
                bool mine = _rtaId480 != null && (_eqWin == null || _eqWin.IsDisposed || _eqWin.Channel?.Id == _rtaId480);
                if (mine && sp != null && sp.Length > 0)
                {
                    sb.Append(",\"rta\":{\"id\":").Append(J("ch:" + _rtaId480)).Append(",\"b\":[");
                    for (int i = 0; i < sp.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        float v = sp[i];
                        sb.Append(float.IsNaN(v) || float.IsInfinity(v) ? -120 : (int)Math.Round(Math.Max(-120f, Math.Min(6f, v))));
                    }
                    sb.Append("]}");
                }
            }
            if (ROn480(_settings.RemoteMuteGrpOn) && _settings.MuteGroupsOn && _settings.MuteGroups != null && _settings.MuteGroups.Count > 0)
            {
                sb.Append(",\"mg\":[");
                for (int i = 0; i < _settings.MuteGroups.Count; i++)
                {
                    var g = _settings.MuteGroups[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"n\":").Append(J(string.IsNullOrEmpty(g.Name) ? "MG " + (i + 1) : g.Name)).Append(",\"on\":").Append(g.On ? "true" : "false").Append('}');
                }
                sb.Append(']');
            }
            if (ROn480(_settings.RemoteSceneSaveOn) && !ROff470(_settings.RemoteScenesOff)) sb.Append(",\"scsave\":true");
            if (ROn480(_settings.RemoteEditOn))
            {
                sb.Append(",\"edit\":true,\"pal\":[");
                for (int i = 0; i < MixerSettings.Palette.Length; i++) { if (i > 0) sb.Append(','); sb.Append(J(Hex480(MixerSettings.Palette[i]))); }
                sb.Append("],\"paln\":[");
                for (int i = 0; i < MixerSettings.PaletteNames.Length; i++) { if (i > 0) sb.Append(','); sb.Append(J(MixerSettings.PaletteNames[i])); }
                sb.Append(']');
            }
            if (ROn480(_settings.RemoteLufsOn))
            {
                sb.Append(",\"lufson\":true,\"lufst\":").Append(F4(_settings.LufsTargetDb));
                var l = _snap.Loudness;
                if (l != null && l.Length >= 3)
                {
                    sb.Append(",\"lufs\":[");
                    for (int i = 0; i < 3; i++)
                    {
                        if (i > 0) sb.Append(',');
                        float v = l[i];
                        sb.Append(float.IsNaN(v) || float.IsInfinity(v) || v < -70f ? "null" : F4((float)Math.Round(v, 1)));
                    }
                    sb.Append(']');
                }
            }
        }

        /// <summary>Befehle (vom Server-Thread), null = kein 4.8-Befehl.</summary>
        private Action RemoteCommand480(string name, string id, float raw, Dictionary<string, string> a)
        {
            switch (name)
            {
                case "rec":
                    if (!ROn480(_settings.RemoteRecOn) || float.IsNaN(raw)) return () => { };
                    return () =>
                    {
                        if (!ROn480(_settings.RemoteRecOn)) return;
                        if (raw >= 0.5f && _recWriter == null) StartRecording();
                        else if (raw < 0.5f && _recWriter != null) StopRecording(true);
                    };
                case "rta":
                {
                    if (!ROn480(_settings.RemoteRtaOn)) return () => { };
                    string cid = id != null && id.StartsWith("ch:") ? id.Substring(3) : null;
                    return () =>
                    {
                        if (!ROn480(_settings.RemoteRtaOn)) return;
                        bool pcEq = _eqWin != null && !_eqWin.IsDisposed;
                        if (cid == null || !_settings.Channels.Any(c => c.Id == cid))
                        {
                            if (_rtaId480 != null && !pcEq) Backend.SetSpectrumChannel(null);
                            _rtaId480 = null;
                            return;
                        }
                        _rtaUntil480 = Now + 5.0;
                        if (_rtaId480 != cid) { _rtaId480 = cid; if (!pcEq) Backend.SetSpectrumChannel(cid); }
                    };
                }
                case "mg":
                {
                    if (!ROn480(_settings.RemoteMuteGrpOn) || float.IsNaN(raw) || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return () => { };
                    return () =>
                    {
                        var l = _settings.MuteGroups;
                        if (!ROn480(_settings.RemoteMuteGrpOn) || l == null || i < 0 || i >= l.Count) return;
                        if (l[i].On != (raw >= 0.5f)) ToggleMuteGroup400(i);
                    };
                }
                case "scsave":
                case "scover":
                {
                    if (!ROn480(_settings.RemoteSceneSaveOn) || ROff470(_settings.RemoteScenesOff)) return () => { };
                    a.TryGetValue("n", out string n);
                    n = CleanName480(n, 30);
                    if (name == "scsave" && n == null) return () => { };
                    return () =>
                    {
                        if (!ROn480(_settings.RemoteSceneSaveOn)) return;
                        if (name == "scsave")
                        {
                            if (_settings.Scenes.Count >= 60) { ShowOsd("Szene nicht gespeichert", "Es gibt schon 60 Szenen.", -1, Theme.MuteRed); return; }
                            var sc = CaptureScene(n);
                            _settings.Scenes.Add(sc);
                            ScheduleSave();
                            ShowOsd("Szene gespeichert", sc.Name + " (vom Handy)", -1, Theme.LedGreen);
                            return;
                        }
                        int i = _settings.Scenes.FindIndex(x => x != null && x.Id == id);
                        if (i < 0) return;
                        var old = _settings.Scenes[i];
                        _settings.Scenes[i] = KeepPartial400(CaptureScene(old.Name, old.Id), old);
                        ScheduleSave();
                        ShowOsd("Szene gespeichert", old.Name + " (vom Handy)", -1, Theme.LedGreen);
                    };
                }
                case "chname":
                case "chcolor":
                {
                    if (!ROn480(_settings.RemoteEditOn) || id == null || !id.StartsWith("ch:")) return () => { };
                    string cid = id.Substring(3);
                    a.TryGetValue("n", out string n);
                    n = CleanName480(n, 24);
                    int ci = -1;
                    if (name == "chcolor" && (float.IsNaN(raw) || (ci = (int)raw) < 0 || ci >= MixerSettings.Palette.Length)) return () => { };
                    if (name == "chname" && n == null) return () => { };
                    return () =>
                    {
                        var c = _settings.Channels.FirstOrDefault(x => x.Id == cid);
                        if (c == null || !ROn480(_settings.RemoteEditOn)) return;
                        if (name == "chname") { if (c.Name == n) return; c.Name = n; OnChannelChanged(c, false); }
                        else { if (c.Color == MixerSettings.Palette[ci]) return; c.Color = MixerSettings.Palette[ci]; }
                        _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome();
                        ScheduleSave();
                    };
                }
            }
            return null;
        }

        /// <summary>Name vom Handy: ohne Steuerzeichen, gekuerzt; null = leer.</summary>
        internal static string CleanName480(string s, int max)
        {
            if (s == null) return null;
            var sb = new StringBuilder();
            foreach (char ch in s) if (!char.IsControl(ch)) sb.Append(ch);
            string t = sb.ToString().Trim();
            if (t.Length > max) t = t.Substring(0, max).Trim();
            return t.Length == 0 ? null : t;
        }

        private void DemoShown480()
        {
            if (!DemoOn("MUTEGRP")) return;
            var ch = _settings.Channels;
            _settings.MuteGroupsOn = true;
            _settings.MuteGroups = new List<MuteGroupConfig>
            {
                new MuteGroupConfig { Name = "SPRACH", Members = ch.Where(c => c.Name == "CHAT").Select(c => c.Id).ToList() },
                new MuteGroupConfig { Name = "MEDIEN", Members = ch.Where(c => c.Name == "MUSIK" || c.Name == "BROWSER").Select(c => c.Id).ToList() },
            };
        }

        private void AddRemote480(SPage p)
        {
            p.Toggle("Aufnahme am Handy", "Im Studio-Pult am Ausgang (in der Liste oben) eine Aufnahme-Taste: startet und stoppt die Aufnahme am PC, mit laufender Zeit.",
                () => _settings.RemoteRecOn, v => { _settings.RemoteRecOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Spektrum im EQ am Handy", "In EDIT → EQ die Taste RTA: Hinter der EQ-Kurve läuft live das Spektrum des Kanals mit – so siehst du, wo es dröhnt oder zischt. Der EQ des Kanals muss dafür an sein.",
                () => _settings.RemoteRtaOn, v => { _settings.RemoteRtaOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Mute-Gruppen am Handy", "Die Mute-Gruppen vom PC als Tasten in der Seitenleiste des Studio-Pults. Ein Tipp schaltet mehrere Kanäle zusammen stumm (Mute-Gruppen unter OPTIONEN einrichten).",
                () => _settings.RemoteMuteGrpOn, v => { _settings.RemoteMuteGrpOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Szenen speichern am Handy", "Den aktuellen Stand am Handy als neue Szene speichern oder eine vorhandene Szene überschreiben.",
                () => _settings.RemoteSceneSaveOn, v => { _settings.RemoteSceneSaveOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn && !_settings.RemoteScenesOff;
            p.Toggle("Name und Farbe am Handy", "Im Studio-Pult unter EDIT → KANAL den Namen und die Farbe eines Kanals ändern.",
                () => _settings.RemoteEditOn, v => { _settings.RemoteEditOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
            p.Toggle("Lautheit am Handy", "In PEGEL die Lautheit (LUFS) – Momentan, Kurzzeit und Integriert – mit dem Zielwert aus OPTIONEN → Leuchten & Anzeigen.",
                () => _settings.RemoteLufsOn, v => { _settings.RemoteLufsOn = v; ScheduleSave(); }).Visible = () => _settings.RemoteOn;
        }
    }
}
