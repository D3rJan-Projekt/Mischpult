using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.14: Pegel einstellbar (Ruecklauf, Spitze halten, Spitze/Mittelwert), Lautheit (LUFS) im MASTER,
    /// "Jetzt laeuft"-Laufschrift, Pultlampe, EQ-Kurve im Kanal, Tasten-Klicken. Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private readonly NowPlayingWatcher _nowPlaying = new NowPlayingWatcher();

        /// <summary>Einstellungen von 2.14 auf die Anzeige uebertragen (beim Start).</summary>
        private void ApplyFeatures7Statics()
        {
            MeterBallistics.Fall = _settings.MeterFall ?? 1;
            StripBase.MeterDelayMs = MixerSettings.MeterDelayValues[Math.Max(0, Math.Min(MixerSettings.MeterDelayValues.Length - 1, _settings.MeterDelay))];
            MeterBallistics.Hold = _settings.MeterHold ?? 2;
            MeterBallistics.Mode = _settings.MeterMode;
            MeterBallistics.OutFall = _settings.MeterOutFall ?? -1;
            MeterBallistics.OutHold = _settings.MeterOutHold ?? -1;
            MeterBallistics.OutMode = _settings.MeterOutMode ?? -1;
            OutputStrip.LufsOn = !_settings.LufsOff;
            OutputStrip.LufsEach = !_settings.LufsEachOff;
            OutputStrip.LufsMode = _settings.LufsMode ?? 1;
            ChannelStrip.EqCurveOn = !_settings.EqCurveOff;
            Theme.LampStrength = _settings.LampStrength ?? 1;
            MeterBridge.BarWidth = _settings.BridgeBarWidth;
        }

        /// <summary>Breite der Pegel in der Meterbridge.</summary>
        private void SetBridgeBarWidth(int w)
        {
            _settings.BridgeBarWidth = Math.Max(0, Math.Min(2, w));
            MeterBridge.BarWidth = _settings.BridgeBarWidth;
            _bridge.Restyle();
            ScheduleSave();
        }

        private ToolStripMenuItem BridgeBarSub()
        {
            var sub = Sub("Pegel-Breite");
            for (int i = 0; i < MeterBridge.BarWidthNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(MeterBridge.BarWidthNames[i], null, (s, e) => SetBridgeBarWidth(k)) { Checked = _settings.BridgeBarWidth == i });
            }
            return sub;
        }

        // ================================================================ Takt

        private float _lampCheck;

        private void TickFeatures7(float dt)
        {
            bool shown = PultShown;

            // Lautheit: nur messen, wenn sie zu sehen ist (hinter einem Vollbild-Spiel misst sie weiter, nur die Anzeige ruht).
            // Der Verlauf misst immer – das Standardgeraet bzw. (2.16.1) das gewaehlte Geraet.
            bool lufsShown = OutputStrip.LufsOn && Visible && WindowState != FormWindowState.Minimized;
            Backend.SetLoudness(lufsShown || (LoudHistOn && _settings.LoudHistSource == null) || !_settings.DiaryOff || MeasureNeedsLoudness || (_settings.BpmOn && shown)
                || _settings.PhaseWarnOn || (_settings.ShakeOn && shown) || (_settings.HearTimeOn && _settings.DiaryOff) || NeedsLoudness400);   // 3.5, 4.0   // 2.17: Hoer-Tagebuch misst immer; 3.4.6: True-Peak/Dynamik
            Backend.SetLoudnessDevices(LoudnessDeviceIds(lufsShown));
            if (lufsShown && shown)
            {
                _master.SetLoudness(_snap.Loudness);
                foreach (var os in _outputStrips.Values) os.SetLoudness(LoudnessOf(os));
            }
            if (shown && MeasureNeedsLoudness)   // 3.4.6
            {
                _master.SetMasterPeaks(_snap.MasterPeaks);
                foreach (var os in _outputStrips.Values) if (os.UsesDefaultLoudness) os.SetMasterPeaks(_snap.MasterPeaks);
            }

            // Jetzt laeuft
            bool marquee = !_settings.NowPlayingOff && shown;
            bool np = marquee || NowPlayingNeeded;   // 2.17: auch fuers Cover und den Song-Verlauf
            if (np && !_nowPlaying.Running) _nowPlaying.Start();
            else if (!np && _nowPlaying.Running) _nowPlaying.Stop();
            _top.SetNowPlaying(marquee ? _nowPlaying.Current : null);
            if (marquee) _top.TickMarquee(dt);

            // Pultlampe: Lage pruefen (Fenstergroesse, Pult dunkler an/aus)
            _lampCheck -= dt;
            if (_lampCheck <= 0f) { _lampCheck = 0.25f; UpdateLamp(false); }
        }

        // ================================================================ Pultlampe

        private int LampMode => _settings.LampMode ?? 1;

        /// <summary>Lage und Zustand der Lampe berechnen; bei Aenderung alles Betroffene neu zeichnen.</summary>
        private void UpdateLamp(bool force)
        {
            int mode = LampMode;
            bool show = mode != 0 && _channelHost != null && _channelHost.Visible;
            bool on = show && (mode == 1 || (mode == 2 && Theme.NightOn));
            float frac = (_settings.LampPos ?? 1) == 0 ? 0.25f : (_settings.LampPos ?? 1) == 2 ? 0.75f : 0.5f;
            PointF at = PointF.Empty;
            float rx = 0, ry = 0;
            if (show)
            {
                _channelHost.LampFrac = frac;
                var o = Theme.FormOrigin(_channelHost);
                var lp = _channelHost.LampPoint;
                at = new PointF(o.X + lp.X, o.Y + lp.Y);
                rx = (float)Math.Round(Math.Max(Theme.F(240), Math.Min(Theme.F(520), _channelHost.Width * 0.4f)));
                ry = (float)Math.Round(Math.Max(Theme.F(300), _channelHost.Height * 0.62f));
            }
            bool changed = force || on != Theme.LampOn || show != _channelHost.ShowLamp || at != Theme.LampAt ||
                           Math.Abs(rx - Theme.LampReachX) > 0.5f || Math.Abs(ry - Theme.LampReachY) > 0.5f;
            if (!changed) return;
            Theme.LampOn = on;
            Theme.LampAt = at;
            Theme.LampPool = new PointF(at.X, at.Y + ry * 0.45f);   // der Kegel trifft weiter unten aufs Pult
            if (show) { Theme.LampReachX = rx; Theme.LampReachY = ry; }
            Theme.LampVersion++;
            _channelHost.ShowLamp = show;
            foreach (var c in new Control[] { _channelHost, _outputHost, _divider, _addButton }) c?.Invalidate();
        }

        private void SetLampMode(int mode)
        {
            _settings.LampMode = Math.Max(0, Math.Min(2, mode));
            UpdateLamp(true);
            ScheduleSave();
        }

        private void SetLampPos(int pos)
        {
            _settings.LampPos = Math.Max(0, Math.Min(2, pos));
            UpdateLamp(true);
            ScheduleSave();
        }

        private void SetLampStrength(int s)
        {
            _settings.LampStrength = Math.Max(0, Math.Min(2, s));
            Theme.LampStrength = _settings.LampStrength.Value;
            UpdateLamp(true);
            ScheduleSave();
        }

        private ToolStripMenuItem LampSub()
        {
            var sub = Sub("Pultlampe");
            for (int i = 0; i < Theme.LampModeNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(Theme.LampModeNames[i], null, (s, e) => SetLampMode(k)) { Checked = LampMode == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < Theme.LampPosNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem("Hängt " + Theme.LampPosNames[i].ToLowerInvariant(), null, (s, e) => SetLampPos(k))
                { Checked = (_settings.LampPos ?? 1) == i, Enabled = LampMode != 0 });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < Theme.LampStrengthNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem("Licht " + Theme.LampStrengthNames[i].ToLowerInvariant(), null, (s, e) => SetLampStrength(k))
                { Checked = (_settings.LampStrength ?? 1) == i, Enabled = LampMode != 0 });
            }
            return sub;
        }

        // ================================================================ Tasten-Klicken

        private long _lastClickMs;
        private System.Media.SoundPlayer _clickPlayer;

        /// <summary>Leises mechanisches Klicken beim Druecken einer Pult-Taste (nur auf dem eigenen Ausgang, nicht im Stream-Mix; waehrend einer Aufnahme still).</summary>
        public void KeyClick()
        {
            if (_settings.KeyClickOff || _top.RecOn) return;   // nicht in eine laufende Aufnahme klicken
            long now = Environment.TickCount;
            if (now - _lastClickMs < 35) return;
            _lastClickMs = now;
            float gain = Beeps.ClickGainOf(_settings.KeyClickLevel ?? 0);
            try
            {
                var clip = Beeps.ClickOf(_settings.ClickKind);   // 4.0: Klick-Art
                if (_snap.EqSupported || _demo) Backend.PlaySound(clip, gain, true, false, false);
                else if (Win32.IsWindows)
                {
                    _clickPlayer?.Dispose();
                    _clickPlayer = new System.Media.SoundPlayer(new System.IO.MemoryStream(Beeps.ToWav(clip, gain)));
                    _clickPlayer.Play();
                }
            }
            catch { }
        }

        private void SetKeyClick(bool on)
        {
            _settings.KeyClickOff = !on;
            ScheduleSave();
            if (on) KeyClick();
        }

        private void SetKeyClickLevel(int level)
        {
            _settings.KeyClickLevel = Math.Max(0, Math.Min(2, level));
            _settings.KeyClickOff = false;
            ScheduleSave();
            _lastClickMs = 0;
            KeyClick();
        }

        // ================================================================ Pegel

        private void SetMeterFall(int v)
        {
            _settings.MeterFall = Math.Max(0, Math.Min(3, v));
            MeterBallistics.Fall = _settings.MeterFall.Value;
            ScheduleSave();
        }

        private void SetMeterHold(int v)
        {
            _settings.MeterHold = Math.Max(0, Math.Min(4, v));
            MeterBallistics.Hold = _settings.MeterHold.Value;
            foreach (var s in AllStrips()) s.ResetMeterHold();
            RelayoutAllStrips();   // Klickflaeche "Spitze loeschen" an/aus
            ScheduleSave();
        }

        private void SetMeterMode(int v)
        {
            _settings.MeterMode = Math.Max(0, Math.Min(2, v));
            MeterBallistics.Mode = _settings.MeterMode;
            foreach (var s in AllStrips()) s.ResetMeterHold();
            ScheduleSave();
        }

        // ---- MASTER und Ausgaenge: eigene Einstellung (null = wie die Kanaele)

        private IEnumerable<StripBase> OutputStripsAll()
        {
            if (_master != null) yield return _master;
            foreach (var os in _outputStrips.Values) yield return os;
        }

        private void SetMeterOutFall(int? v)
        {
            _settings.MeterOutFall = v.HasValue ? Math.Max(0, Math.Min(3, v.Value)) : (int?)null;
            MeterBallistics.OutFall = _settings.MeterOutFall ?? -1;
            ScheduleSave();
        }

        private void SetMeterOutHold(int? v)
        {
            _settings.MeterOutHold = v.HasValue ? Math.Max(0, Math.Min(4, v.Value)) : (int?)null;
            MeterBallistics.OutHold = _settings.MeterOutHold ?? -1;
            foreach (var s in OutputStripsAll()) s.ResetMeterHold();
            RelayoutAllStrips();   // Klickflaeche "Spitze loeschen" an/aus
            ScheduleSave();
        }

        private void SetMeterOutMode(int? v)
        {
            _settings.MeterOutMode = v.HasValue ? Math.Max(0, Math.Min(2, v.Value)) : (int?)null;
            MeterBallistics.OutMode = _settings.MeterOutMode ?? -1;
            foreach (var s in OutputStripsAll()) s.ResetMeterHold();
            ScheduleSave();
        }

        /// <summary>Menue-Eintraege fuer die Pegel im MASTER/an den Ausgaengen.</summary>
        private void AddOutMeterItems(ToolStripItemCollection items)
        {
            items.Add(Header("Zeigt"));
            items.Add(new ToolStripMenuItem("Wie bei den Kanälen", null, (s, e) => SetMeterOutMode(null)) { Checked = _settings.MeterOutMode == null });
            for (int i = 0; i < MeterBallistics.ModeNames.Length; i++)
            {
                int k = i;
                items.Add(new ToolStripMenuItem(MeterModeText(i), null, (s, e) => SetMeterOutMode(k)) { Checked = _settings.MeterOutMode == i });
            }
            items.Add(new ToolStripSeparator());
            items.Add(Header("Rücklauf"));
            items.Add(new ToolStripMenuItem("Wie bei den Kanälen", null, (s, e) => SetMeterOutFall(null)) { Checked = _settings.MeterOutFall == null });
            for (int i = 0; i < MeterBallistics.FallNames.Length; i++)
            {
                int k = i;
                items.Add(new ToolStripMenuItem(Cap(MeterBallistics.FallNames[i]), null, (s, e) => SetMeterOutFall(k)) { Checked = _settings.MeterOutFall == i });
            }
            items.Add(new ToolStripSeparator());
            items.Add(Header("Spitze bleibt stehen"));
            items.Add(new ToolStripMenuItem("Wie bei den Kanälen", null, (s, e) => SetMeterOutHold(null)) { Checked = _settings.MeterOutHold == null });
            for (int i = 0; i < MeterBallistics.HoldNames.Length; i++)
            {
                int k = i;
                items.Add(new ToolStripMenuItem(Cap(MeterBallistics.HoldNames[i]), null, (s, e) => SetMeterOutHold(k)) { Checked = _settings.MeterOutHold == i });
            }
        }

        /// <summary>Rechtsklick auf den Pegel im MASTER/Ausgang.</summary>
        public void ShowOutMeterMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.Items.Add(Header("Pegel im MASTER und an den Ausgängen"));
            m.Items.Add(new ToolStripSeparator());
            AddOutMeterItems(m.Items);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Pegel der Kanäle einstellen …", null, (s, e) => ShowSettings("Leuchten & Anzeigen")));
            m.Show(screen);
        }

        // ---- 2.16 Echtzeit und Ausgleich
        private void SetMeterRealtime(bool on)
        {
            _settings.MeterRealtimeOff = !on;
            Backend.SetRealtime(on);
            ScheduleSave();
        }

        private void SetMeterDelay(int i)
        {
            _settings.MeterDelay = Math.Max(0, Math.Min(MixerSettings.MeterDelayValues.Length - 1, i));
            StripBase.MeterDelayMs = MixerSettings.MeterDelayValues[_settings.MeterDelay];
            ScheduleSave();
        }

        public static readonly string[] MeterDelayNames = { "Aus", "40 ms", "80 ms", "150 ms", "250 ms" };

        private ToolStripMenuItem MeterSub()
        {
            var sub = Sub("Pegel");
            sub.DropDownItems.Add(Header("Zeigt"));
            for (int i = 0; i < MeterBallistics.ModeNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(MeterModeText(i), null, (s, e) => SetMeterMode(k)) { Checked = _settings.MeterMode == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(Header("Rücklauf"));
            for (int i = 0; i < MeterBallistics.FallNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(Cap(MeterBallistics.FallNames[i]), null, (s, e) => SetMeterFall(k)) { Checked = (_settings.MeterFall ?? 1) == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(Header("Spitze bleibt stehen"));
            for (int i = 0; i < MeterBallistics.HoldNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(Cap(MeterBallistics.HoldNames[i]), null, (s, e) => SetMeterHold(k)) { Checked = (_settings.MeterHold ?? 2) == i });
            }
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(new ToolStripMenuItem("Pegel in Echtzeit", null, (s, e) => SetMeterRealtime(_settings.MeterRealtimeOff)) { Checked = !_settings.MeterRealtimeOff });
            var dly = Sub("Pegel an den Ton anpassen");
            for (int i = 0; i < MeterDelayNames.Length; i++) { int k = i; dly.DropDownItems.Add(new ToolStripMenuItem(MeterDelayNames[i], null, (s, e) => SetMeterDelay(k)) { Checked = _settings.MeterDelay == i }); }
            sub.DropDownItems.Add(dly);
            var outSub = Sub("MASTER und Ausgänge");
            AddOutMeterItems(outSub.DropDownItems);
            sub.DropDownItems.Add(outSub);
            return sub;
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string MeterModeText(int i) =>
            i == 0 ? "Spitze (schnell, wie bisher)" : i == 1 ? "Mittelwert (ruhiger, wie laut es klingt)" : "Beides (Mittelwert hell, Spitze darüber)";

        // ================================================================ Lautheit (LUFS)

        // 2.16.1: weitere Geraete messen – Ausgaenge mit eigener Anzeige und das Geraet des Verlaufs
        private string[] _loudDevIds = new string[0];
        private readonly List<string> _loudDevTmp = new List<string>();

        private string[] LoudnessDeviceIds(bool lufsShown)
        {
            _loudDevTmp.Clear();
            if (lufsShown && OutputStrip.LufsEach && !StripBase.Compact)
                foreach (var os in _outputStrips.Values)
                    if (os.ShowsLufs && !os.UsesDefaultLoudness && !string.IsNullOrEmpty(os.DeviceId)) _loudDevTmp.Add(os.DeviceId);
            if (LoudHistOn && _settings.LoudHistSource != null) _loudDevTmp.Add(_settings.LoudHistSource);
            // gleiche Liste wie vorher? Dann dasselbe Feld (die Engine vergleicht nur die Referenz)
            bool same = _loudDevTmp.Count == _loudDevIds.Length;
            for (int i = 0; same && i < _loudDevIds.Length; i++) same = string.Equals(_loudDevIds[i], _loudDevTmp[i], StringComparison.OrdinalIgnoreCase);
            if (!same) _loudDevIds = _loudDevTmp.ToArray();
            return _loudDevIds;
        }

        /// <summary>Lautheit eines Geraets (null = Standardgeraet) – null, wenn (noch) nicht gemessen.</summary>
        private float[] LoudnessOfDevice(string id)
        {
            if (id == null) return _snap.Loudness;
            var d = _snap.LoudnessByDevice;
            return d != null && d.TryGetValue(id, out var v) ? v : null;
        }

        private float[] LoudnessOf(OutputStrip os) => os.UsesDefaultLoudness ? _snap.Loudness : LoudnessOfDevice(os.DeviceId);

        private void SetLufsEach(bool on)
        {
            _settings.LufsEachOff = !on;
            OutputStrip.LufsEach = on;
            RefreshStrips();
            ScheduleSave();
        }

        private void SetLufsOn(bool on)
        {
            _settings.LufsOff = !on;
            OutputStrip.LufsOn = on;
            if (!on) Backend.SetLoudness(false);
            RelayoutAllStrips();
            RefreshStrips();
            ScheduleSave();
        }

        private void SetLufsMode(int mode)
        {
            _settings.LufsMode = Math.Max(0, Math.Min(2, mode));
            OutputStrip.LufsMode = _settings.LufsMode.Value;
            ScheduleSave();
        }

        public void ShowLufsMenu(Point screen, OutputStrip strip = null)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            bool own = strip != null && !strip.UsesDefaultLoudness;
            m.Items.Add(Header(own ? "Lautheit (LUFS) · " + strip.LufsSourceName : "Lautheit (LUFS)"));
            m.Items.Add(Hint("Wie laut es sich anfühlt – Spotify/YouTube ≈ −14, Fernsehen ≈ −23."));
            m.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < OutputStrip.LufsModeNames.Length; i++)
            {
                int k = i;
                m.Items.Add(new ToolStripMenuItem(OutputStrip.LufsModeNames[i], null, (s, e) => SetLufsMode(k)) { Checked = OutputStrip.LufsMode == i });
            }
            m.Items.Add(new ToolStripMenuItem("Integriert neu beginnen", null, (s, e) =>
            {
                Backend.ResetLoudness();
                ShowOsd("Lautheit", "Integriert: neu", -1, Theme.Accent);
            }));
            m.Items.Add(new ToolStripSeparator());
            // 2.16.1: diesen Ausgang im Verlauf zeigen
            if (strip != null)
            {
                string id = own ? strip.DeviceId : null, name = own ? strip.LufsSourceName : null;
                bool now = LoudHistOn && (id == null ? _settings.LoudHistSource == null : string.Equals(_settings.LoudHistSource, id, StringComparison.OrdinalIgnoreCase));
                m.Items.Add(new ToolStripMenuItem(own ? "Im Lautheits-Verlauf zeigen" : "Im Lautheits-Verlauf zeigen (Standard-Ausgabe)", null, (s, e) =>
                {
                    if (!LoudHistOn) SetLoudHist(true);
                    SetLoudHistSource(id, name);
                }) { Checked = now });
            }
            if (_outputStrips.Count > 0)
                m.Items.Add(new ToolStripMenuItem("Lautheit in jedem Ausgang", null, (s, e) => SetLufsEach(_settings.LufsEachOff))
                { Checked = OutputStrip.LufsEach, ToolTipText = "Jeder Ausgang misst sein eigenes Gerät" });
            m.Items.Add(new ToolStripMenuItem("Lautheit ausblenden", null, (s, e) => SetLufsOn(false)));
            m.Show(screen);
        }

        // ================================================================ Jetzt laeuft, EQ-Kurve

        private void SetNowPlayingOn(bool on)
        {
            _settings.NowPlayingOff = !on;
            if (!on) { _nowPlaying.Stop(); _top.SetNowPlaying(null); }
            ScheduleSave();
        }

        private void SetEqCurveOn(bool on)
        {
            _settings.EqCurveOff = !on;
            ChannelStrip.EqCurveOn = on;
            RefreshStrips();
            ScheduleSave();
        }

        /// <summary>Alle Schalter von 2.14 fuers Schnellmenue (Ansicht → Leuchten).</summary>
        private void AddFeatures7Items(ToolStripItemCollection items)
        {
            items.Add(new ToolStripSeparator());
            items.Add(MeterSub());
            items.Add(new ToolStripMenuItem("Lautheit (LUFS) im MASTER", null, (s, e) => SetLufsOn(_settings.LufsOff)) { Checked = !_settings.LufsOff });
            if (_outputStrips.Count > 0)
                items.Add(new ToolStripMenuItem("Lautheit in jedem Ausgang", null, (s, e) => SetLufsEach(_settings.LufsEachOff)) { Checked = !_settings.LufsEachOff, Enabled = !_settings.LufsOff });
            items.Add(new ToolStripMenuItem("„Jetzt läuft“-Laufschrift", null, (s, e) => SetNowPlayingOn(_settings.NowPlayingOff)) { Checked = !_settings.NowPlayingOff });
            items.Add(new ToolStripMenuItem("EQ-Kurve im Kanal", null, (s, e) => SetEqCurveOn(_settings.EqCurveOff)) { Checked = !_settings.EqCurveOff });
            items.Add(LampSub());
            items.Add(new ToolStripMenuItem("Tasten klicken", null, (s, e) => SetKeyClick(_settings.KeyClickOff)) { Checked = !_settings.KeyClickOff });
        }
    }
}
