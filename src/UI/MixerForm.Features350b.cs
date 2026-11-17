using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5 – Pegel und Anzeigen: Phasen-Warnung, Uebersteuerungs-Liste, Verzoegerung am Kanal, Geraete-Format,
    /// Hoerzeit heute, Kanal-Statistik, Kein-Ton-Hinweis. Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 131 Phasen-Warnung

        private double _phaseBadSince = -1, _phaseOkSince = -1;

        private void TickPhase(double now)
        {
            if (!_settings.PhaseWarnOn) { if (OutputStrip.PhaseBad) { OutputStrip.PhaseBad = false; InvalidateMasterPhase(); } return; }
            float c = _snap.Correlation;
            OutputStrip.PhaseCorr = c;
            bool bad = OutputStrip.PhaseBad;
            if (!float.IsNaN(c) && c < -0.2f)
            {
                _phaseOkSince = -1;
                if (_phaseBadSince < 0) _phaseBadSince = now;
                if (!bad && now - _phaseBadSince > 1.5) bad = true;
            }
            else
            {
                _phaseBadSince = -1;
                if (_phaseOkSince < 0) _phaseOkSince = now;
                if (bad && now - _phaseOkSince > (float.IsNaN(c) ? 3.0 : 2.0)) bad = false;
            }
            if (bad != OutputStrip.PhaseBad) { OutputStrip.PhaseBad = bad; InvalidateMasterPhase(); }
        }

        private void InvalidateMasterPhase()
        {
            _master?.InvalidateChrome();
            foreach (var os in _outputStrips.Values) os.InvalidateChrome();
        }

        // ================================================================ 132 Uebersteuerungs-Liste

        internal sealed class ClipEntry { public DateTime Time; public string Name; public float Db; }
        private readonly List<ClipEntry> _clipLog = new List<ClipEntry>();
        public IReadOnlyList<ClipEntry> Clips => _clipLog;

        /// <summary>Aus StripBase: PEAK ist gerade angegangen.</summary>
        public void NoteClip(StripBase s, float post)
        {
            if (!_settings.ClipLogOn || s == null) return;
            float db = post > 1e-6f ? 20f * (float)Math.Log10(post) : -120f;
            var last = _clipLog.Count > 0 ? _clipLog[0] : null;
            string name = s.DisplayName ?? "?";
            // dieselbe Stelle nicht mehrfach (z. B. ein lauter Song: hoechstens alle 5 s ein Eintrag je Zug)
            if (last != null && last.Name == name && (DateTime.Now - last.Time).TotalSeconds < 5) { if (db > last.Db) last.Db = db; return; }
            _clipLog.Insert(0, new ClipEntry { Time = DateTime.Now, Name = name, Db = db });
            if (_clipLog.Count > 10) _clipLog.RemoveRange(10, _clipLog.Count - 10);
        }

        public void ShowClipMenu(StripBase s, Point screen)
        {
            var m = NewMenu();
            m.Items.Add(Header("Zuletzt zu laut (alle Züge)"));
            if (_clipLog.Count == 0) m.Items.Add(Hint("Seit dem Start war nichts zu laut."));
            foreach (var c in _clipLog)
                m.Items.Add(Hint(c.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "   " + c.Name + "   " +
                                 (c.Db > -0.05f ? "0,0" : c.Db.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")).Replace("-", "−")) + " dB"));
            m.Items.Add(new ToolStripSeparator());
            if (_clipLog.Count > 0) m.Items.Add(new ToolStripMenuItem("Liste leeren", null, (o, e) => _clipLog.Clear()));
            m.Items.Add(new ToolStripMenuItem("Ab wann PEAK leuchtet …", null, (o, e) => BeginInvoke(new Action(() => ShowLedDialog(true)))));
            m.Show(screen);
        }

        // ================================================================ 149 Verzoegerung am Kanal

        private void UpdateLatency350()
        {
            int ms = _settings.LatencyOn ? Math.Max(0, _snap.EqLatencyMs) : 0;
            foreach (var s in _channelStrips)
            {
                int v = 0;
                if (ms > 0 && _policyCache.TryGetValue(s.Config.Id, out var p) && p.Eq != null) v = ms + Math.Max(0, DelayOf(s.Config));
                s.SetLatency(v);
            }
        }

        // ================================================================ 151 Hoerzeit heute

        private float _hearAcc;

        private void TickHearTime(float dt)
        {
            if (!_settings.HearTimeOn) { if (_top.HearText != null) { _top.HearText = null; _top.Invalidate(); } return; }
            double listen, loud;
            if (!_settings.DiaryOff && _diary != null)
            {
                var d = _diary.Today;
                listen = d.ListenSec; loud = d.LoudSec;
            }
            else
            {
                // eigenes Zaehlen (gleiche Rechnung wie das Hoer-Tagebuch: Lautheit + Windows-Lautstaerke)
                string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (_settings.HearDate != today) { _settings.HearDate = today; _settings.HearSec = 0; _settings.HearLoudSec = 0; }
                var v = _snap.Loudness;
                var def = DefaultDevice;
                if (dt > 0f && dt < 1f && v != null && v.Length > 1 && !float.IsNaN(v[1]) && v[1] > -70f && def != null && !def.Mute)
                {
                    _hearAcc += dt;
                    if (_hearAcc >= 1f)
                    {
                        int sec = (int)_hearAcc; _hearAcc -= sec;
                        _settings.HearSec += sec;
                        if (v[1] + def.VolumeDb >= DiaryLoudFrom) _settings.HearLoudSec += sec;
                        if (_settings.HearSec % 60 == 0) ScheduleSave();
                    }
                }
                listen = _settings.HearSec; loud = _settings.HearLoudSec;
            }
            int min = (int)(listen / 60);
            string text = (min / 60) + ":" + (min % 60).ToString("00");
            bool loudish = loud >= 3600;
            if (text != _top.HearText || loudish != _top.HearLoud)
            {
                _top.HearText = text; _top.HearLoud = loudish;
                _top.HearTip = "Hörzeit heute: " + ListeningDiary.Duration(listen) + " mit Ton, davon " + ListeningDiary.Duration(loud) + " laut" +
                               (loudish ? "\nSchon über eine Stunde laut – vielleicht etwas leiser?" : "") +
                               (!_settings.DiaryOff ? "\nAus dem Hör-Tagebuch (OPTIONEN → Ton)." : "\nGezählt, solange das Pult läuft.");
                _top.Invalidate();
            }
        }

        // ================================================================ 153 Kein-Ton-Hinweis

        private string _noSoundNote;
        private double _noSoundSince = -1;
        private string _noSoundCand;

        private void TickNoSound(double now)
        {
            string note = null;
            if (_settings.NoSoundWarnOn) note = FindNoSound();
            if (note != _noSoundCand) { _noSoundCand = note; _noSoundSince = now; }
            string shown = note != null && now - _noSoundSince >= 3.0 ? note : null;   // erst nach 3 s Ruhe melden
            if (shown == _noSoundNote) return;
            _noSoundNote = shown;
            UpdateStatus();
        }

        /// <summary>Spielt eine App hoerbar, aber auf ihrem Geraet kommt nichts an (stumm, 0 %)? Dann der Hinweis, sonst null.</summary>
        internal string FindNoSound()
        {
            var devs = _snap.Devices;
            if (devs == null || devs.Length == 0) return null;
            foreach (var a in _snap.Apps)
            {
                if (a.Key == AppKeys.System || !a.Active) continue;
                if (Math.Max(a.PreL, a.PreR) < 0.02f) continue;              // spielt nicht (oder sehr leise)
                var ch = ChannelOf(a.Key);
                if (ch != null && (ch.Mute || ch.Fader <= FaderLaw.MinPos)) continue;   // absichtlich stumm im Pult
                if (AppPos(a.Key) <= FaderLaw.MinPos || AdMuted(a.Key)) continue;
                var ids = a.DeviceIds != null && a.DeviceIds.Length > 0 ? a.DeviceIds : new[] { DefaultDevice?.Id };
                foreach (var id in ids)
                {
                    var d = devs.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (d == null) continue;
                    if (d.Mute) return "● Kein Ton: " + a.Name + " spielt, aber „" + d.Name + "“ ist stumm";
                    if (d.Volume < 0.005f) return "● Kein Ton: " + a.Name + " spielt, aber „" + d.Name + "“ steht auf 0 %";
                }
            }
            return null;
        }

        // ================================================================ Einstellungen

        private void AddMeters350(SPage p)
        {
            p.Section("Noch mehr Anzeigen");
            p.Toggle("Phasen-Warnung am MASTER", "Ist der Ton länger gegenphasig (links und rechts heben sich auf), leuchtet im MASTER ein rotes Φ. Er klingt dann dünn, und in Mono verschwinden Teile. Maus über das Geräte-Feld: Erklärung.",
                () => _settings.PhaseWarnOn, v => { _settings.PhaseWarnOn = v; LoudnessSink.CorrOn = v; InvalidateMasterPhase(); ScheduleSave(); });
            p.Toggle("Übersteuerungs-Liste", "Rechtsklick auf PEAK: die letzten 10 Mal „zu laut“ mit Uhrzeit, Zug und dB – auch wenn du gerade nicht hingeschaut hast.",
                () => _settings.ClipLogOn, v => { _settings.ClipLogOn = v; StripBase.ClipLogOn = v; ScheduleSave(); });
            p.Toggle("Verzögerung am Kanal", "Läuft ein Kanal durchs Mischpult (EQ, COMP, AUTO, Effekte …), steht oben im Kanal klein, wie viel später sein Ton kommt, z. B. „+38 ms“. Gelb ab 60 ms – beim Zocken merkt man das.",
                () => _settings.LatencyOn, v => { _settings.LatencyOn = v; ChannelStrip.LatencyOn = v; UpdateLatency350(); ScheduleSave(); });
            p.Toggle("Geräte-Format am Ausgang", "Unter dem Gerätenamen steht das Format, mit dem Windows das Gerät betreibt, z. B. „48 kHz · 24 Bit“ (statt des Treibernamens).",
                () => _settings.DevFormatOn, v => { _settings.DevFormatOn = v; OutputStrip.DevFormatOn = v; InvalidateOutputs350(); ScheduleSave(); });
            p.Toggle("Hörzeit heute", "Oben in der Kopfleiste: wie lange heute schon Ton lief (Stunden:Minuten). Maus darüber: wie viel davon laut war. Kommt aus dem Hör-Tagebuch, sonst zählt das Pult selbst.",
                () => _settings.HearTimeOn, v => { _settings.HearTimeOn = v; ScheduleSave(); });
            p.Toggle("Kanal-Statistik", "Maus über den Kanalnamen: wie lange der Kanal seit dem Start gespielt hat, wie laut er im Schnitt war und sein lautester Moment.",
                () => _settings.ChanStatsOn, v => { _settings.ChanStatsOn = v; ChannelStrip.StatsOn = v; ScheduleSave(); });
            p.Toggle("Kein-Ton-Hinweis", "Spielt eine App, aber ihr Gerät ist stumm oder steht auf 0 %, erscheint oben ein roter Hinweis, z. B. „Kein Ton: Spotify spielt, aber Kopfhörer ist stumm“.",
                () => _settings.NoSoundWarnOn, v => { _settings.NoSoundWarnOn = v; ScheduleSave(); });
        }

        private void InvalidateOutputs350()
        {
            _master?.InvalidateChrome();
            foreach (var os in _outputStrips.Values) os.InvalidateChrome();
        }
    }
}
