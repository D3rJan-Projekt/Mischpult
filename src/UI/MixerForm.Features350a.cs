using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5 – Ton: Hoehen-Filter und Stimmen nach vorne pro Kanal, Werbung stumm (Spotify Free), Bass mono an den
    /// Ausgaengen, zwei Geraete gleichzeitig, Einschlaf-Geraeusche als eigener Kanal. Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private NoiseStrip _noiseStrip;

        // ================================================================ 137/138 Klang pro Kanal (Menue-Teil)

        private void AddChannel350Items(ToolStripMenuItem sub, ChannelConfig c)
        {
            if (_settings.TrebleCutOn)
            {
                var h = Sub("Höhen-Filter");
                foreach (var v in MixerSettings.LpValues)
                {
                    int vv = v;
                    h.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : "über " + (v / 1000) + " kHz weg", null, (s, e) => { c.LpHz = vv; PushPolicy(); ScheduleSave(); }) { Checked = c.LpHz == v });
                }
                h.DropDownItems.Add(Hint("Nimmt Zischeln und Rauschen oben weg – je kleiner, desto dumpfer."));
                sub.DropDownItems.Add(h);
            }
            if (_settings.DialogOn)
                sub.DropDownItems.Add(new ToolStripMenuItem("Stimmen nach vorne", null, (s, e) => { c.Dialog = !c.Dialog; PushPolicy(); ScheduleSave(); })
                    { Checked = c.Dialog, ToolTipText = "Sind Musik und Effekte laut, werden sie etwas leiser – Stimmen bleiben, wie sie sind." });
        }

        private bool AnyKlang350 => _settings.TrebleCutOn || _settings.DialogOn;

        private void KlangState350(List<string> parts, ChannelConfig c)
        {
            if (_settings.TrebleCutOn && c.LpHz > 0) parts.Add("bis " + (c.LpHz / 1000) + " kHz");
            if (_settings.DialogOn && c.Dialog) parts.Add("Stimmen");
        }

        // ================================================================ 157 Bass mono an den Ausgaengen

        /// <summary>Gilt Bass mono gerade (Schalter in den Einstellungen)?</summary>
        internal static bool BassMonoGlobal;

        private void AddOutput350Items(ToolStripItemCollection items, string deviceId, string name)
        {
            if (!_settings.BassMonoOn || deviceId == null) return;
            var o = OutFxConfig(deviceId, false);
            int cur = o?.BassMonoHz ?? 0;
            var sub = Sub("Bass mono: " + (cur == 0 ? "aus" : "unter " + cur + " Hz"));
            foreach (var v in MixerSettings.BassMonoValues)
            {
                int vv = v;
                sub.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : "unter " + v + " Hz", null, (s, e) => SetBassMono(deviceId, name, vv)) { Checked = cur == v });
            }
            sub.DropDownItems.Add(Hint("Bässe kommen links und rechts gleich – gut für Lautsprecher mit Subwoofer."));
            items.Add(sub);
        }

        private void SetBassMono(string deviceId, string name, int hz)
        {
            if (hz > 0 && !_snap.EqSupported && !_demo) { MessageBox.Show(this, "Bass mono braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            var o = OutFxConfig(deviceId, true, name);
            o.BassMonoHz = hz;
            if (hz > 0) _settings.OutFxOff = false;
            PushPolicy();
            ScheduleSave();
            ShowOsd("Ausgang · " + (name ?? "Gerät"), hz == 0 ? "Bass stereo" : "Bass mono unter " + hz + " Hz", -1, CueColor);
        }

        // ================================================================ 158 Zwei Geraete gleichzeitig

        private string _mirrorSent;
        private int _mirrorDelaySent = -1;

        private void PushMirror()
        {
            string want = _settings.MirrorOn ? _settings.MirrorDevice : null;
            if (want != null && string.Equals(want, DefaultDevice?.Id, StringComparison.OrdinalIgnoreCase)) want = null;   // nie auf sich selbst
            if (want == _mirrorSent && _settings.MirrorDelayMs == _mirrorDelaySent) return;
            _mirrorSent = want; _mirrorDelaySent = _settings.MirrorDelayMs;
            try { Backend.SetMirror(want, _settings.MirrorDelayMs); } catch { }
        }

        private string MirrorStateText()
        {
            if (!_settings.MirrorOn) return "Aus.";
            if (_settings.MirrorDevice == null) return "Noch kein zweites Gerät gewählt.";
            var d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, _settings.MirrorDevice, StringComparison.OrdinalIgnoreCase));
            string name = d?.Name ?? _settings.MirrorDeviceName ?? "Gerät";
            if (d == null) return "„" + name + "“ ist gerade nicht angeschlossen.";
            if (d.IsDefault) return "„" + name + "“ ist gerade selbst das Standard-Gerät – dann läuft nichts doppelt.";
            return "Alles vom Standard-Gerät läuft zusätzlich auf „" + name + "“" + (_settings.MirrorDelayMs > 0 ? ", " + _settings.MirrorDelayMs + " ms später" : "") + ".";
        }

        private void ShowMirrorDeviceMenu(Rectangle r)
        {
            var m = NewMenu();
            m.Items.Add(Header("Zweites Gerät"));
            var defId = DefaultDevice?.Id;
            foreach (var d in _snap.Devices)
            {
                if (string.Equals(d.Id, defId, StringComparison.OrdinalIgnoreCase)) continue;
                var dd = d;
                m.Items.Add(new ToolStripMenuItem(d.FullName ?? d.Name, null, (s, e) =>
                {
                    _settings.MirrorDevice = dd.Id; _settings.MirrorDeviceName = dd.Name; _settings.MirrorOn = true;
                    PushMirror(); ScheduleSave();
                }) { Checked = string.Equals(d.Id, _settings.MirrorDevice, StringComparison.OrdinalIgnoreCase) });
            }
            if (m.Items.Count == 1) m.Items.Add(Hint("Kein zweites Gerät angeschlossen."));
            m.Show(r.Left, r.Bottom);
        }

        // ================================================================ 159 Einschlaf-Geraeusche

        public bool NoisePlaying => _settings.NoisePlaying;
        public float NoiseFader => _settings.NoiseFaderNow;
        public int NoiseKind => _settings.NoiseKind;
        private int _noiseKindSent = -1;
        private float _noiseGainSent = -1f;

        public void SetNoiseFader(float pos, bool final)
        {
            _settings.NoiseFader = Math.Max(0f, Math.Min(1f, pos));
            PushNoise();
            _noiseStrip?.InvalidateChrome();
            if (final) ScheduleSave();
        }

        public void ToggleNoise()
        {
            _settings.NoisePlaying = !_settings.NoisePlaying;
            PushNoise();
            _noiseStrip?.InvalidateChrome();
            ScheduleSave();
        }

        public void ShowNoiseMenu(Point screen)
        {
            var m = NewMenu();
            m.Items.Add(Header("Einschlaf-Geräusch"));
            for (int i = 0; i < NoiseGen.KindNames.Length; i++)
            {
                int k = i;
                m.Items.Add(new ToolStripMenuItem(NoiseGen.KindNames[i], null, (s, e) =>
                {
                    _settings.NoiseKind = k;
                    if (!_settings.NoisePlaying) _settings.NoisePlaying = true;
                    PushNoise(); _noiseStrip?.InvalidateChrome(); ScheduleSave();
                }) { Checked = _settings.NoiseKind == i });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(_settings.NoisePlaying ? "Stoppen" : "Starten", null, (s, e) => ToggleNoise()));
            m.Items.Add(Hint("Läuft der Sleep-Timer, blendet das Geräusch mit aus."));
            m.Show(screen);
        }

        /// <summary>Lautstaerke an die Engine (mit Sleep-Timer). Nur wenn sich etwas aendert.</summary>
        private void PushNoise()
        {
            float gain = 0f;
            if (_settings.NoiseOn && _settings.NoisePlaying)
            {
                gain = FaderLaw.PosToGain(_settings.NoiseFaderNow);
                if (_sleepDb < 0) gain *= (float)Math.Pow(10, _sleepDb / 20.0);
            }
            if (Math.Abs(gain - _noiseGainSent) < 1e-4f && _settings.NoiseKind == _noiseKindSent) return;
            _noiseGainSent = gain; _noiseKindSent = _settings.NoiseKind;
            try { Backend.SetNoise(_settings.NoiseKind, gain); } catch { }
        }

        private void SetNoiseOn(bool on)
        {
            _settings.NoiseOn = on;
            if (!on) _settings.NoisePlaying = false;
            PushNoise();
            RefreshChannelViews();
            ScheduleSave();
        }

        // ================================================================ 139 Werbung stumm (Spotify Free)

        private string _adKey;          // App-Schluessel, der gerade wegen Werbung stumm ist (null = keiner)
        private double _adSince;

        /// <summary>Ist das, was gerade laeuft, Spotify-Werbung? (Titel "Advertisement"/"Werbung"/"Spotify", kein Interpret)</summary>
        internal static bool IsSpotifyAd(NowPlayingInfo np)
        {
            if (np == null || !np.Playing) return false;
            string app = np.App ?? "";
            if (app.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string t = (np.Title ?? "").Trim(), a = (np.Artist ?? "").Trim();
            if (a.Length > 0 && !a.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == 0) return true;
            string[] ads = { "Advertisement", "Advertisment", "Werbung", "Anzeige", "Spotify", "Spotify Free", "Spotify Premium", "Spotify Ad", "Ad" };
            foreach (var x in ads) if (t.Equals(x, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string SpotifyAppKey()
        {
            foreach (var a in _snap.Apps)
                if (a.Key != null && a.Key.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return a.Key.ToLowerInvariant();
            return null;
        }

        private void TickAdMute(double now)
        {
            string want = null;
            if (_settings.AdMuteOn && IsSpotifyAd(_nowPlaying.Current)) want = SpotifyAppKey();
            if (want == _adKey) return;
            if (want != null) _adSince = now;
            bool wasMuted = _adKey != null;
            _adKey = want;
            PushPolicy();
            if (want != null) ShowOsd("Werbung", "Spotify stumm, bis die Musik weitergeht", -1, Theme.MuteRed);
            else if (wasMuted && now - _adSince > 3) ShowOsd("Werbung vorbei", "Spotify wieder an", -1, Theme.LedGreen);
        }

        /// <summary>Aus PerApp: diese App ist gerade wegen Werbung stumm.</summary>
        private bool AdMuted(string key) => _adKey != null && key != null && string.Equals(key, _adKey, StringComparison.OrdinalIgnoreCase);

        // ================================================================ Takt und Einstellungen

        private double _nextTon350;

        private void TickTon350(double now)
        {
            TickAdMute(now);
            if (now < _nextTon350) return;
            _nextTon350 = now + 0.25;
            PushNoise();      // Sleep-Timer blendet mit aus
            PushMirror();     // Standard-Geraet gewechselt?
            if (_settings.NoisePlaying && _sleepEnd <= 0 && _sleepResetAt > 0 && _settings.NoiseOn)
            {
                // Sleep-Timer ist gerade abgelaufen: Geraeusch aus
                _settings.NoisePlaying = false; PushNoise(); _noiseStrip?.InvalidateChrome(); ScheduleSave();
            }
        }

        private void AddSound350(SPage p)
        {
            p.Section("Noch mehr Klang");
            p.Toggle("Höhen-Filter pro Kanal", "Rechtsklick auf einen Kanal → Klang → Höhen-Filter: nimmt alles über 12, 8 oder 5 kHz weg – gegen Zischeln, Rauschen und schrille Töne. Läuft dafür durchs Mischpult.",
                () => _settings.TrebleCutOn, v => { _settings.TrebleCutOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Stimmen nach vorne", "Rechtsklick auf einen Kanal → Klang → Stimmen nach vorne: Sind Musik und Explosionen viel lauter als die Stimmen (Filme, Spiele), werden nur sie leiser – höchstens 8 dB. Die Stimme selbst wird nie lauter gemacht. Ist es ruhig, ändert sich nichts.",
                () => _settings.DialogOn, v => { _settings.DialogOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Werbung stumm (Spotify Free)", "Spielt Spotify Werbung (Titel „Advertisement“ bzw. „Werbung“ ohne Interpret), wird nur Spotify stummgeschaltet, bis wieder Musik kommt. Erkannt wird über „Jetzt läuft“ – das dauert bis zu etwa einer Sekunde.",
                () => _settings.AdMuteOn, v => { _settings.AdMuteOn = v; if (!v && _adKey != null) { _adKey = null; PushPolicy(); } ScheduleSave(); });
            p.Toggle("Bass mono an den Ausgängen", "Rechtsklick auf MASTER oder einen Ausgang → Bass mono: Bässe unter 80, 120 oder 160 Hz kommen links und rechts gleich. Gut für Lautsprecher mit Subwoofer und wenn der Bass in einer Ecke „hängt“.",
                () => _settings.BassMonoOn, v => { _settings.BassMonoOn = v; BassMonoGlobal = v; PushPolicy(); ScheduleSave(); });
            p.Section("Zwei Geräte gleichzeitig");
            p.Toggle("Ton zusätzlich auf einem zweiten Gerät", () => MirrorStateText() + " Was am Standard-Gerät läuft, kommt dann auch aus dem zweiten (z. B. Kopfhörer und Lautsprecher, oder zwei Bluetooth-Boxen). Lautstärke des zweiten Geräts: an seinem Ausgang bzw. in Windows.",
                () => _settings.MirrorOn, v => { _settings.MirrorOn = v; PushMirror(); ScheduleSave(); });
            p.Button("Zweites Gerät", "Auf welchem Gerät der Ton zusätzlich läuft (nicht das Standard-Gerät selbst).", "Wählen …", r => ShowMirrorDeviceMenu(r),
                () => _settings.MirrorDevice == null ? "keins" : (_settings.MirrorDeviceName ?? "Gerät")).Enabled = () => _settings.MirrorOn;
            p.Choice("Zweites Gerät später", () => "Hört man ein Echo zwischen den Geräten, hier verzögern (das Standard-Gerät lässt sich nicht verzögern – Bluetooth ist meist von selbst langsamer).",
                MixerSettings.MirrorDelayValues.Select(v => v == 0 ? "0" : v + " ms").ToArray(), () => Math.Max(0, Array.IndexOf(MixerSettings.MirrorDelayValues, _settings.MirrorDelayMs)),
                i => { _settings.MirrorDelayMs = MixerSettings.MirrorDelayValues[i]; PushMirror(); ScheduleSave(); }).Enabled = () => _settings.MirrorOn;
            p.Section("Einschlafen");
            p.Toggle("Einschlaf-Geräusche (eigener Kanal)", "Ein Kanal GERÄUSCH mit Regen, Meer, Rauschen oder Kaminfeuer – vom Pult selbst erzeugt. Taste AN startet, der Fader stellt die Lautstärke. Läuft der Sleep-Timer, blendet das Geräusch mit aus und stoppt am Ende.",
                () => _settings.NoiseOn, v => SetNoiseOn(v));
        }
    }
}
