using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Musik &amp; Extern: was laeuft pro Kanal (Fenstertitel), MIDI-Rueckmeldung (Motorfader, LEDs), OBS Studio,
    /// WLED-Lichter. Dazu die Schalter fuer Musik-Tasten und Interpret-Lautstaerke. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyExtern400(MixerSettings s)
        {
            ChannelStrip.WinTitleOn = s.ChanTitleOn;
        }

        private void HookExtern400()
        {
            ApplyObs400();
        }

        private void TickExtern400(double now)
        {
            TickChanTitles400(now);
            TickMidiFeedback400(now);
            TickWled400(now);
        }

        // ================================================================ 212 Was laeuft pro Kanal

        private double _nextTitles;
        private volatile bool _titlesBusy;

        private void TickChanTitles400(double now)
        {
            if (!_settings.ChanTitleOn || !PultShown || now < _nextTitles || _titlesBusy || _demo) return;
            _nextTitles = now + 3.0;
            // pro Kanal die lauteste aktive App
            var want = new Dictionary<ChannelStrip, string>();
            foreach (var s in _channelStrips)
            {
                if (!s.Visible) continue;
                var c = s.Config;
                var apps = _snap.Apps.Where(a => a.Active && (c.IsCatchAll ? ChannelOf(a.Key) == c : c.Apps.Contains(a.Key, StringComparer.OrdinalIgnoreCase))).ToList();
                var best = apps.OrderByDescending(a => Math.Max(a.PreL, a.PreR)).FirstOrDefault();
                want[s] = best?.Path ?? best?.Key;
            }
            _titlesBusy = true;
            Task.Run(() =>
            {
                var res = new Dictionary<ChannelStrip, string>();
                foreach (var kv in want) res[kv.Key] = kv.Value == null ? null : WindowTitleOf(kv.Value);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _titlesBusy = false;
                        foreach (var kv in res) if (!kv.Key.IsDisposed) kv.Key.SetWinTitle(kv.Value);
                    }));
                }
                catch { _titlesBusy = false; }
            });
        }

        /// <summary>Fenstertitel der App (Pfad oder "name.exe") – erstes Fenster mit Titel.</summary>
        private static string WindowTitleOf(string pathOrKey)
        {
            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(pathOrKey);
                if (string.IsNullOrEmpty(name) || name.StartsWith("#")) return null;
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { string t = p.MainWindowTitle; if (!string.IsNullOrWhiteSpace(t)) return CleanTitle(t); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return null;
        }

        private static readonly string[] BrowserTails = { " - Google Chrome", " – Google Chrome", " - Mozilla Firefox", " — Mozilla Firefox", " – Mozilla Firefox", " - Microsoft​ Edge", " - Microsoft Edge", " - Brave", " - Opera", " - Vivaldi", " - Chromium" };

        /// <summary>Browser-Namen hinten abschneiden, "(3) " vorne weg, Leerzeichen glaetten.</summary>
        internal static string CleanTitle(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            t = t.Trim();
            foreach (var tail in BrowserTails)
                if (t.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) { t = t.Substring(0, t.Length - tail.Length).TrimEnd(); break; }
            // "Profil 1 - Microsoft Edge" o. ae. mit Profil-Zusatz
            int edge = t.IndexOf(" - Microsoft", StringComparison.OrdinalIgnoreCase);
            if (edge > 0 && t.IndexOf("Edge", edge, StringComparison.OrdinalIgnoreCase) > 0) t = t.Substring(0, edge).TrimEnd();
            if (t.StartsWith("(") && t.IndexOf(") ", StringComparison.Ordinal) is int k && k > 0 && k < 6) t = t.Substring(k + 2);   // "(3) YouTube" – ungelesene Nachrichten
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
            if (t.Length > 120) t = t.Substring(0, 120);
            return t.Length == 0 ? null : t;
        }

        // ================================================================ 170 MIDI-Rueckmeldung

        private MidiOut _midiOut;
        private readonly Dictionary<MidiBinding, int> _midiSent = new Dictionary<MidiBinding, int>();
        private readonly Dictionary<string, double> _midiInAt = new Dictionary<string, double>();
        private double _nextMidiFb, _nextMidiRetry;
        private List<string> _midiOutNames;

        /// <summary>Aus PumpMidi: an diesem Regler dreht gerade jemand (dann nicht dagegen senden).</summary>
        private void NoteMidiIn400(MidiBinding b)
        {
            if (b?.Target != null) _midiInAt[b.Target] = Now;
        }

        /// <summary>Aktueller Wert eines Ziels als MIDI-Wert (0..127, Pitchbend 0..16383) oder -1 = nicht rueckmeldbar.</summary>
        internal int MidiValueOf(MidiBinding b)
        {
            string t = b.Target;
            float v;
            bool? on = null;
            var dev = DefaultDevice;
            if (t == "master.volume") { if (dev == null) return -1; v = dev.Volume; }
            else if (t == "master.mute") { if (dev == null) return -1; on = dev.Mute; v = 0; }
            else if (t == "stream.volume") { v = FaderLaw.DbToPos(Math.Max(-60f, _settings.StreamMasterDb)); }
            else if (t == "stream.mute") { on = _settings.StreamMute; v = 0; }
            else if (t == "duck.toggle") { on = _settings.DuckOn; v = 0; }
            else if (t == "night.toggle") { on = _settings.NightMode; v = 0; }
            else if (t == "dim.toggle") { on = _dimOn; v = 0; }
            else if (t == "rec.toggle") { on = _top.RecOn; v = 0; }
            else if (t.StartsWith("scene:")) { on = _sceneLit346 == t.Substring(6); v = 0; }
            else
            {
                var p = t.Split(':');
                if (p.Length != 3) return -1;
                if (p[0] == "grp")
                {
                    var g = _settings.Groups.FirstOrDefault(x => x.Id == p[1]);
                    if (g == null) return -1;
                    if (p[2] == "fader") v = g.Fader; else if (p[2] == "mute") { on = g.Mute; v = 0; } else return -1;
                }
                else if (p[0] == "ch")
                {
                    var c = _settings.Channels.FirstOrDefault(x => x.Id == p[1]);
                    if (c == null) return -1;
                    switch (p[2])
                    {
                        case "fader": v = c.Fader; break;
                        case "pan": v = (c.Pan + 1f) / 2f; break;
                        case "mute": on = c.Mute; v = 0; break;
                        case "solo": on = c.Solo; v = 0; break;
                        default: return -1;
                    }
                }
                else return -1;
            }
            if (on.HasValue) return on.Value ? 127 : 0;
            v = Math.Max(0f, Math.Min(1f, v));
            return b.Kind == 2 ? (int)Math.Round(v * 16383) : (int)Math.Round(v * 127);
        }

        private void TickMidiFeedback400(double now)
        {
            if (!_settings.MidiFeedbackOn || !MidiHub.Available || _settings.Midi.Count == 0 || now < _nextMidiFb) return;
            _nextMidiFb = now + 0.05;
            if (_midiOut == null) _midiOut = new MidiOut();
            if (_midiOutNames == null || now >= _nextMidiRetry) { _nextMidiRetry = now + 10; _midiOutNames = MidiOut.DeviceNames(); _midiOut.Retry(); }
            foreach (var b in _settings.Midi)
            {
                if (b?.Target == null || b.Device == null) continue;
                if (_midiInAt.TryGetValue(b.Target, out double at) && now - at < 0.4) continue;   // wird gerade bewegt
                int val = MidiValueOf(b);
                if (val < 0) continue;
                if (_midiSent.TryGetValue(b, out int last) && last == val) continue;
                string outDev = _settings.MidiOutDevice ?? MidiOut.MatchOutput(b.Device, _midiOutNames);
                if (outDev == null) continue;
                int ch = Math.Max(0, Math.Min(15, b.Channel));
                bool ok = b.Kind == 2 ? _midiOut.Send(outDev, 0xE0 | ch, val & 0x7f, (val >> 7) & 0x7f)
                        : b.Kind == 1 ? _midiOut.Send(outDev, 0x90 | ch, b.Number, val)
                        : _midiOut.Send(outDev, 0xB0 | ch, b.Number, val);
                if (ok) _midiSent[b] = val;
            }
        }

        // ================================================================ 198 OBS Studio

        private ObsClient _obs;
        private string _obsLastSent;
        private double _obsSentAt;
        private bool ObsLive400 => _obs != null && _obs.Connected && (_obs.Streaming || _obs.Recording);

        internal static string ProtectPass(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try { return "dp:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
            catch { return "b6:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain)); }   // ohne Windows-Schluessel (z. B. Vorschau): nur verschleiert
        }

        internal static string UnprotectPass(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("dp:")) return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(3)), null, DataProtectionScope.CurrentUser));
                if (stored.StartsWith("b6:")) return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(3)));
            }
            catch { }
            return "";
        }

        private void ApplyObs400()
        {
            if (!_settings.ObsOn || _demo)
            {
                if (_obs != null) { _obs.Dispose(); _obs = null; }
                RefreshTopKeys400();
                return;
            }
            if (_obs == null)
            {
                _obs = new ObsClient();
                _obs.Changed += () => { try { BeginInvoke(new Action(OnObsChanged400)); } catch { } };
                _obs.SceneSwitched += name => { try { BeginInvoke(new Action(() => OnObsScene400(name))); } catch { } };
            }
            _obs.Stop();
            _obs.Host = _settings.ObsHost ?? "127.0.0.1";
            _obs.Port = _settings.ObsPortNow;
            _obs.Password = UnprotectPass(_settings.ObsPass);
            _obs.Start();
        }

        private void OnObsChanged400()
        {
            RefreshTopKeys400();
            if (_settings.ObsOnAir) { _top.OnAir = OnAirNow; _top.Invalidate(); }
        }

        /// <summary>OBS hat die Szene gewechselt: gleichnamige Pult-Szene laden (wenn verbunden).</summary>
        private void OnObsScene400(string name)
        {
            if (!_settings.ObsSceneLink || string.IsNullOrEmpty(name)) return;
            if (name == _obsLastSent && Now - _obsSentAt < 3) return;   // kam von uns
            var sc = _settings.Scenes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (sc != null) { _obsLastSent = null; ApplyScene(sc, "Szene (OBS)"); }
        }

        /// <summary>Pult-Szene geladen: gleichnamige OBS-Szene einschalten.</summary>
        private void ObsSceneFromPult400(Scene sc)
        {
            if (_obs == null || !_obs.Connected || !_settings.ObsSceneLink || sc?.Name == null) return;
            var target = _obs.Scenes.FirstOrDefault(n => string.Equals(n, sc.Name, StringComparison.CurrentCultureIgnoreCase));
            if (target == null || target == _obs.CurrentScene) return;
            _obsLastSent = target; _obsSentAt = Now;
            _obs.SetScene(target);
        }

        private void AddTopKeysExtern400(List<TopKey> l)
        {
            if (!_settings.ObsOn) return;
            bool conn = _obs != null && _obs.Connected;
            bool live = ObsLive400;
            l.Add(new TopKey
            {
                Id = "obs", Text = "OBS", On = conn, Lit = live ? Theme.MuteRed : Theme.LedGreen, Width = 46, Priority = 2,
                Tip = !conn ? "OBS: " + (_obs?.Status ?? "aus") + "\nKlick: Menü" : "OBS verbunden" + (_obs.Streaming ? " · STREAM läuft" : "") + (_obs.Recording ? " · Aufnahme läuft" : "") + (_obs.CurrentScene != null ? "\nSzene: " + _obs.CurrentScene : "") + "\nKlick: Menü",
            });
        }

        private void OnTopKeyObs400(string id, bool right, Point screen)
        {
            if (id != "obs") return;
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            bool conn = _obs != null && _obs.Connected;
            m.Items.Add(Header("OBS Studio – " + (_obs?.Status ?? "aus")));
            if (conn)
            {
                m.Items.Add(new ToolStripMenuItem(_obs.Streaming ? "■ Stream beenden" : "● Stream starten", null, (s, e) => _obs.ToggleStream()) { Checked = _obs.Streaming });
                m.Items.Add(new ToolStripMenuItem(_obs.Recording ? "■ Aufnahme beenden" : "● Aufnahme starten", null, (s, e) => _obs.ToggleRecord()) { Checked = _obs.Recording });
                if (_obs.Scenes.Count > 0)
                {
                    var sub = Sub("OBS-Szene", true);
                    foreach (var n in _obs.Scenes) { string nn = n; sub.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, e) => _obs.SetScene(nn)) { Checked = n == _obs.CurrentScene }); }
                    m.Items.Add(sub);
                }
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Neu verbinden", null, (s, e) => ApplyObs400()));
            m.Items.Add(new ToolStripMenuItem("Einstellungen …", null, (s, e) => BeginInvoke(new Action(AskObs400))));
            m.Show(screen);
        }

        private void AskObs400()
        {
            using (var d = new InputDialog("OBS", "Adresse und Port (z. B. 127.0.0.1:4455):", (_settings.ObsHost ?? "127.0.0.1") + ":" + _settings.ObsPortNow, 60))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ParseHostPort(d.Value, out string host, out int port);
                _settings.ObsHost = host; _settings.ObsPort = port;
            }
            using (var d = new InputDialog("OBS", "Passwort (OBS → Werkzeuge → WebSocket-Servereinstellungen; leer = keins):", UnprotectPass(_settings.ObsPass), 64, true))
            {
                if (d.ShowDialog(this) == DialogResult.OK) _settings.ObsPass = ProtectPass(d.Value);
            }
            ScheduleSave();
            ApplyObs400();
        }

        internal static void ParseHostPort(string s, out string host, out int port)
        {
            host = null; port = 0;
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.TrimEnd('/');
            int k = s.LastIndexOf(':');
            if (k > 0 && int.TryParse(s.Substring(k + 1), out int p) && p > 0 && p < 65536) { port = p; s = s.Substring(0, k); }
            host = s.Length == 0 ? null : s;
        }

        // ================================================================ 207 WLED

        private WledSender _wled;
        private double _nextWled;
        private readonly float[] _wledL = new float[48], _wledP = new float[48];
        private float _wledBeat, _wledLevel;
        private byte[] _wledBuf = new byte[0];
        private bool WledWantsSpectrum400 => _settings.WledOn && _settings.WledMode == 1;

        private void TickWled400(double now)
        {
            if (!_settings.WledOn || string.IsNullOrEmpty(_settings.WledHost) || _demo)
            {
                if (_wled != null) { _wled.Dispose(); _wled = null; }
                return;
            }
            if (now < _nextWled) return;
            _nextWled = now + 0.033;
            if (_wled == null) _wled = new WledSender();
            _wled.SetHost(_settings.WledHost);
            if (!_wled.Ready) return;
            int n = _settings.WledLedsNow;
            if (_wledBuf.Length != n * 3) _wledBuf = new byte[n * 3];
            FillWled400(_wledBuf, n, 0.033f);
            _wled.Send(_wledBuf, n);
        }

        /// <summary>Farben fuer die LEDs je nach Art (Pegel, Spektrum, Takt, Akzentfarbe, lautester Kanal).</summary>
        private void FillWled400(byte[] buf, int n, float dt)
        {
            float bright = (_settings.WledBright ?? 70) / 100f;
            float peak = _master == null ? 0f : Math.Max(_master.LastPeakL, _master.LastPeakR);
            float lv = ChanWindow.MeterFrac(peak);
            _wledLevel = lv > _wledLevel ? lv : _wledLevel + (lv - _wledLevel) * Math.Min(1f, dt * 6f);
            switch (_settings.WledMode)
            {
                case 1:   // Spektrum
                {
                    int bands = Math.Max(8, Math.Min(_wledL.Length, n));
                    if (_spectrum.Active) _spectrum.Bars(bands, _wledL, _wledP); else Array.Clear(_wledL, 0, _wledL.Length);
                    for (int i = 0; i < n; i++)
                    {
                        int b = Math.Min(bands - 1, i * bands / Math.Max(1, n));
                        float v = Math.Max(0f, Math.Min(1f, _wledL[b]));
                        var c = Hue(240f - 240f * b / Math.Max(1, bands - 1));
                        Put(buf, i, c, v * bright);
                    }
                    break;
                }
                case 2:   // Takt: blitzt bei Bass-Schlaegen
                {
                    float bass = _snap.MasterBass;
                    float hit = bass >= 0f ? Math.Min(1f, bass * 3.2f) : lv;
                    _wledBeat = hit > _wledBeat + 0.12f ? hit : Math.Max(0f, _wledBeat - dt * 2.5f);
                    for (int i = 0; i < n; i++) Put(buf, i, Theme.Accent, _wledBeat * bright);
                    break;
                }
                case 3:   // Akzentfarbe, atmet leicht mit
                    for (int i = 0; i < n; i++) Put(buf, i, Theme.Accent, (0.55f + 0.45f * _wledLevel) * bright);
                    break;
                case 4:   // Farbe des lautesten Kanals
                {
                    Color col = Theme.Accent; float best = 0.0005f;
                    foreach (var c in _settings.Channels)
                    {
                        if (c.Mute || c.Hidden) continue;
                        _channelPeak.TryGetValue(c.Id, out var pk);
                        float pv = pk == null ? 0f : Math.Max(pk[0], pk[1]);
                        if (pv > best) { best = pv; col = Color.FromArgb(255, Color.FromArgb(c.Color)); }
                    }
                    for (int i = 0; i < n; i++) Put(buf, i, col, (0.35f + 0.65f * _wledLevel) * bright);
                    break;
                }
                default:  // Pegel: von unten gruen – gelb – rot
                {
                    int lit = (int)Math.Round(_wledLevel * n);
                    for (int i = 0; i < n; i++)
                    {
                        float f = (i + 1f) / n;
                        Color c = f > 0.85f ? Color.FromArgb(255, 40, 30) : f > 0.6f ? Color.FromArgb(255, 190, 20) : Color.FromArgb(40, 230, 70);
                        Put(buf, i, c, i < lit ? bright : 0f);
                    }
                    break;
                }
            }
        }

        private static void Put(byte[] buf, int i, Color c, float k)
        {
            k = Math.Max(0f, Math.Min(1f, k));
            buf[i * 3] = (byte)(c.R * k); buf[i * 3 + 1] = (byte)(c.G * k); buf[i * 3 + 2] = (byte)(c.B * k);
        }

        internal static Color Hue(float h)
        {
            h = ((h % 360f) + 360f) % 360f;
            float x = 1f - Math.Abs(h / 60f % 2f - 1f);
            float r, g, b;
            if (h < 60) { r = 1; g = x; b = 0; } else if (h < 120) { r = x; g = 1; b = 0; } else if (h < 180) { r = 0; g = 1; b = x; }
            else if (h < 240) { r = 0; g = x; b = 1; } else if (h < 300) { r = x; g = 0; b = 1; } else { r = 1; g = 0; b = x; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        // ================================================================ Tastenkuerzel

        private void AddHotkeyActionsExtern400(List<HotkeyAction> list)
        {
            if (!_settings.ObsOn) return;
            list.Add(new HotkeyAction("obs.stream", "OBS: Stream starten/beenden", "Allgemein"));
            list.Add(new HotkeyAction("obs.record", "OBS: Aufnahme starten/beenden", "Allgemein"));
        }

        private bool ExecuteHotkeyExtern400(string action)
        {
            switch (action)
            {
                case "obs.stream": if (_obs != null && _obs.Connected) _obs.ToggleStream(); else ShowOsd("OBS", "nicht verbunden", -1, Theme.TextDim); return true;
                case "obs.record": if (_obs != null && _obs.Connected) _obs.ToggleRecord(); else ShowOsd("OBS", "nicht verbunden", -1, Theme.TextDim); return true;
            }
            return false;
        }

        // ================================================================ Einstellungen

        private void AddExtern400(SPage p)
        {
            p.Section("4.0 Musik");
            p.Toggle("Mehr Musik-Tasten", "Oben neben Play/Pause: 10 Sekunden zurück und vor; Rechtsklick auf die Musik-Tasten: Zufall und Wiederholen. Über die Windows-Mediensteuerung – geht nur, wenn die App es anbietet (z. B. Spotify).",
                () => _settings.MediaMoreOn, v => { _settings.MediaMoreOn = v; NowPlayingWatcher.WantModes = v; RefreshTopKeys400(); ApplyHotkeys(); ScheduleSave(); });
            p.Toggle("Lautstärke pro Interpret merken", "Rechtsklick auf die Musik-Tasten → „Lautstärke für … merken“: Kommt der Interpret (oder Podcast) wieder, stellt das Pult seinen Musik-Kanal auf diese Lautstärke – danach wieder zurück.",
                () => _settings.ArtistVolOn, v => { _settings.ArtistVolOn = v; ScheduleSave(); });
            p.Toggle("Was läuft, pro Kanal", "Im App-Feld jedes Kanals steht unten klein, was die lauteste App gerade zeigt – z. B. der Videotitel im Browser oder der Name des Spiels (aus dem Fenstertitel).",
                () => _settings.ChanTitleOn, v => { _settings.ChanTitleOn = v; ChannelStrip.WinTitleOn = v; _nextTitles = 0; foreach (var s in _channelStrips) { if (!v) s.SetWinTitle(null); s.InvalidateChrome(); } ScheduleSave(); });
            p.Section("4.0 Verbindungen");
            p.Toggle("MIDI-Rückmeldung", () => "Das Pult schickt den Stand zurück an den MIDI-Controller: Motorfader fahren mit, LED-Kränze und Tasten-Lampen (MUTE, SOLO, Szenen) zeigen den echten Zustand. Für alle Regler mit MIDI-Zuordnung (Steuerung → MIDI-Controller)." + (_settings.Midi.Count == 0 ? " Noch keine Zuordnung." : ""),
                () => _settings.MidiFeedbackOn, v => { _settings.MidiFeedbackOn = v; _midiSent.Clear(); if (!v) { _midiOut?.Dispose(); _midiOut = null; } ScheduleSave(); });
            p.Button("MIDI-Ausgang", "Wohin die Rückmeldung geht. „Automatisch“ nimmt den Ausgang mit dem gleichen Namen wie der Eingang.", "Wählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                m.Items.Add(new ToolStripMenuItem("Automatisch (wie der Eingang)", null, (s, e) => { _settings.MidiOutDevice = null; _midiSent.Clear(); ScheduleSave(); }) { Checked = _settings.MidiOutDevice == null });
                foreach (var n in MidiOut.DeviceNames()) { string nn = n; m.Items.Add(new ToolStripMenuItem(n, null, (s, e) => { _settings.MidiOutDevice = nn; _midiSent.Clear(); _midiOut?.Retry(); ScheduleSave(); }) { Checked = n == _settings.MidiOutDevice }); }
                ShowMenuAt(m, r);
            }, () => _settings.MidiOutDevice ?? "automatisch").Enabled = () => _settings.MidiFeedbackOn;
            p.Toggle("OBS Studio", () => "Verbindung zu OBS (ab Version 28, Werkzeuge → WebSocket-Servereinstellungen → „WebSocket-Server aktivieren“). Oben erscheint eine OBS-Taste: Stream und Aufnahme starten, Szenen umschalten. Gerade: " + (_obs?.Status ?? "aus") + ".",
                () => _settings.ObsOn, v => { _settings.ObsOn = v; ApplyObs400(); ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });
            p.Button("OBS-Verbindung", "Adresse, Port und Passwort des WebSocket-Servers von OBS. Das Passwort wird mit deinem Windows-Konto verschlüsselt gespeichert.", "Einstellen …", r => AskObs400(),
                () => (_settings.ObsHost ?? "127.0.0.1") + ":" + _settings.ObsPortNow).Enabled = () => _settings.ObsOn;
            p.Toggle("Szenen mit OBS verbinden", "Lädst du im Pult eine Szene, schaltet OBS auf die Szene mit dem gleichen Namen – und umgekehrt.",
                () => _settings.ObsSceneLink, v => { _settings.ObsSceneLink = v; ScheduleSave(); }).Enabled = () => _settings.ObsOn;
            p.Toggle("ON-AIR-Lampe folgt OBS", "Die ON-AIR-Lampe oben leuchtet, solange OBS streamt oder aufnimmt.",
                () => _settings.ObsOnAir, v => { _settings.ObsOnAir = v; if (v && _settings.OnAirMode == 0) { _settings.OnAirMode = 1; _top.OnAirMode = 1; } _top.OnAir = OnAirNow; _top.Invalidate(); ScheduleSave(); }).Enabled = () => _settings.ObsOn;
            p.Toggle("WLED-Lichter", () => "LED-Streifen mit WLED (ESP32/ESP8266) im WLAN leuchten zur Musik. Das Pult schickt die Farben direkt (UDP, Port 21324); hört es auf, macht WLED nach 2 Sekunden mit seinem eigenen Effekt weiter." + (_wled?.Error != null ? " Fehler: " + _wled.Error : ""),
                () => _settings.WledOn, v => { _settings.WledOn = v; if (v && string.IsNullOrEmpty(_settings.WledHost)) BeginInvoke(new Action(AskWledHost400)); ScheduleSave(); });
            p.Button("WLED-Adresse", "IP-Adresse oder Name des WLED-Geräts (steht in der WLED-App).", "Ändern …", r => AskWledHost400(), () => _settings.WledHost ?? "keine").Enabled = () => _settings.WledOn;
            p.Choice("Anzahl LEDs", () => "So viele LEDs hat dein Streifen (höchstens 490).", MixerSettings.WledLedValues.Select(x => x + " LEDs").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.WledLedValues, _settings.WledLedsNow)), i => { _settings.WledLeds = MixerSettings.WledLedValues[i]; ScheduleSave(); }).Enabled = () => _settings.WledOn;
            p.Choice("Lichter zeigen", () => "Was die LEDs zeigen.", MixerSettings.WledModeNames, () => _settings.WledMode, i => { _settings.WledMode = i; ScheduleSave(); }).Enabled = () => _settings.WledOn;
            p.Slider("Helligkeit der LEDs", "Wie hell die LEDs höchstens werden.", 10, 100, 5, () => _settings.WledBright ?? 70, (v, done) => { _settings.WledBright = (int)v; if (done) ScheduleSave(); }, v => (int)v + " %").Enabled = () => _settings.WledOn;
        }

        private void AskWledHost400()
        {
            using (var d = new InputDialog("WLED", "IP-Adresse oder Name des WLED-Geräts (z. B. 192.168.0.42 oder wled.local):", _settings.WledHost ?? "", 60))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _settings.WledHost = d.Value.Trim();
                ScheduleSave();
            }
        }
    }
}
