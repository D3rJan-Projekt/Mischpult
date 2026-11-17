using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.3 – Optik: weitere Zeigerinstrumente, Schallplatte/Bandmaschine, 3D-Spektrum und Lichtorgel, Oszilloskop-Look,
    /// Fallblatt- und Nixie-Uhr, Punktmatrix, ON-AIR-Lampe, LED-Kranz, leuchtende Kappen, Kippschalter und Bakelit,
    /// Praegeband, Symbole, Gebrauchsspuren, Roehrengluehen, Aufwaermen, Ausschalt-Show, Wand, bewegter Hintergrund,
    /// Funken, Ducking-Linie, Kabel-Ansicht, Gruppen-Klammer, Notizzettel und Hinweise im Pult-Design.
    /// Alles einzeln an/aus – Standard: aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private readonly Stopwatch _clock330 = Stopwatch.StartNew();
        private double _warmStart = -1, _powerOffStart = -1, _nextLive330, _nextPatch330;
        private bool _powerOffDone;
        private double _powerOffOpacity = 1;
        private int _tubeQ = -1;
        private string _duckSig330;
        private PatchbayWindow _patch;

        // ================================================================ Start

        private static int DemoInt(string name, int def)
        {
            return int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_" + name), out int v) ? v : def;
        }

        private static bool DemoOn(string name) => Environment.GetEnvironmentVariable("MISCHPULT_DEMO_" + name) == "1";

        /// <summary>Beim Start: statische Schalter (vor dem Anlegen der Zuege).</summary>
        private void ApplyFeatures330Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                s.VuKind = DemoInt("VUKIND", s.VuKind);
                s.CoverArt = DemoInt("COVERART", s.CoverArt);
                if (DemoOn("SCOPE")) s.ScopeLookOn = true;
                s.OnAirMode = DemoInt("ONAIR", s.OnAirMode);
                if (DemoOn("KNOBRING")) s.KnobRingOn = true;
                if (DemoOn("CAPGLOW")) s.CapGlowOn = true;
                if (DemoOn("DYMO")) { s.DymoPlates = true; s.TapePlates = false; s.DigitalPlates = false; }
                if (DemoOn("SYMBOLS")) s.SymbolsOn = true;
                s.Wear = DemoInt("WEAR", s.Wear);
                if (DemoOn("TUBES")) { s.TubeGlowOn = true; if (s.CheekStyle == 0 || s.CheekStyle == 1) s.CheekStyle = 2; }
                if (DemoOn("WARMUP")) s.WarmUpOn = true;
                if (DemoOn("POWEROFF")) s.PowerOffShowOn = true;
                s.Wall = DemoInt("WALL", s.Wall);
                s.LiveBg = DemoInt("LIVEBG", s.LiveBg);
                if (DemoOn("SPARKS")) s.SparksOn = true;
                if (DemoOn("DUCKLINES")) s.DuckLinesOn = true;
                if (DemoOn("PATCHBAY")) s.PatchbayOn = true;
                if (DemoOn("BRACKETS"))
                {
                    s.GroupBracketsOn = true;
                    if (s.Groups != null && s.Groups.Count == 0 && s.Channels.Count > 3)
                        s.Groups.Add(new GroupConfig { Id = "demo-grp", Name = "SPIEL+MUSIK", Color = unchecked((int)0xff3fa7ff), Members = new List<string> { s.Channels[1].Id, s.Channels[2].Id } });
                }
                if (DemoOn("NOTES")) { s.NotesOn = true; if (s.Channels.Count > 2) s.Channels[2].Note = "Discord nachts leiser"; }
                if (DemoOn("TIPS")) s.PultTipsOn = true;
                int ks = DemoInt("KEYSHAPE", -1); if (ks >= 0) s.KeyShape = ks;
                int lcd = DemoInt("LCDSTYLE", -1); if (lcd >= 0) s.LcdStyle = lcd;
                int sp = DemoInt("SPECSTYLE", -1); if (sp >= 0) s.SpecStyle = sp;
            }
            Theme.VuKind = s.VuKind;
            CoverView.ArtStyle = s.CoverArt;
            OscView.ScopeLook = s.ScopeLookOn;
            Theme.KnobRing = s.KnobRingOn;
            StripBase.CapGlowOn = s.CapGlowOn;
            Theme.DymoPlates = s.DymoPlates;
            ChannelStrip.SymbolsOn = s.SymbolsOn;
            ChannelStrip.NotesOn = s.NotesOn;
            ChannelStrip.GroupBracketsOn = s.GroupBracketsOn;
            Theme.Wear = s.Wear;
            Theme.TubeGlow = s.TubeGlowOn;
            Theme.LiveBg = s.LiveBg;
            StripBase.SparksOn = s.SparksOn;
            MeterBridge.DuckLinesOn = s.DuckLinesOn;
            PultTip.On = s.PultTipsOn;
            Theme.WarmLevel = s.WarmUpOn ? 0f : 1f;   // Aufwaermen: startet dunkel
        }

        /// <summary>Nach dem ersten Anzeigen.</summary>
        private void ShownFeatures330()
        {
            _top.OnAirMode = _settings.OnAirMode;
            if (_settings.Wall > 0 && _bgSource == null) RebuildBgImage();
            if (_demo)
            {
                if (DemoOn("ONAIRLIT")) _demoOnAir = true;
                if (DemoOn("DUCKLINES") && _settings.Channels.Count > 3)
                    DemoLater(() =>
                    {
                        MeterBridge.DuckFrom = _settings.Channels[3].Id;
                        MeterBridge.DuckTo.Clear();
                        MeterBridge.DuckTo.Add(_settings.Channels[1].Id);
                        MeterBridge.DuckTo.Add(_settings.Channels[2].Id);
                        MeterBridge.DuckDb = -12f;
                        _demoDuck = true;
                        MeterBridge.DuckVer++;
                    });
                if (DemoOn("PATCHBAYWIN")) DemoLater(ShowPatchbay);
                if (DemoOn("SPARKS")) _demoSparks = true;
                if (DemoOn("POWEROFFNOW"))
                {
                    var t = new Timer { Interval = 3500 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); StartPowerOff(); };   // Vorschau: Show direkt starten
                    t.Start();
                }
            }
        }

        private bool _demoOnAir, _demoDuck, _demoSparks;
        private double _nextDemoSpark;

        private void TickFeatures330()
        {
            double now = _clock330.Elapsed.TotalSeconds;
            TickWarmUp(now);
            TickPowerOff(now);
            if (!PultShown) return;
            if (Theme.LiveBg > 0 && now >= _nextLive330)
            {
                _nextLive330 = now + 0.05;   // etwa 20 Bilder pro Sekunde – nur die freien Flaechen
                _channelHost.Invalidate(false);
                _outputHost.Invalidate(false);
            }
            if (Theme.TubeGlow && _cheekL != null && _cheekL.Visible)
            {
                float lv = _master.LevelNow;
                int q = (int)Math.Round(Math.Min(1f, lv) * 8);
                if (q != _tubeQ)
                {
                    _tubeQ = q;
                    Theme.TubeLevel = q / 8f;
                    _cheekL.Invalidate(); _cheekR.Invalidate();
                }
            }
            bool onAir = OnAirNow;
            if (onAir != _top.OnAir || _top.OnAirMode != _settings.OnAirMode)
            {
                _top.OnAir = onAir;
                _top.OnAirMode = _settings.OnAirMode;
                _top.Invalidate();
            }
            if (_settings.DuckLinesOn && !_demoDuck) TickDuckLines();
            if (_patch != null && !_patch.IsDisposed && now >= _nextPatch330) { _nextPatch330 = now + 0.5; _patch.Refresh330(); }
            if (_demoSparks && now >= _nextDemoSpark && _channelStrips.Count > 1)
            {
                _nextDemoSpark = now + 1.2;
                _channelStrips[1].DemoSparks();
            }
        }

        // ================================================================ Zeigerinstrument

        private void SetVuKind(int i)
        {
            _settings.VuKind = Math.Max(0, Math.Min(Theme.VuKindNames.Length - 1, i));
            Theme.VuKind = _settings.VuKind;
            RefreshVuStrips();
            RestyleBridges();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private void AddVu330(SPage p)
        {
            p.Choice("Zeigerinstrument", () => _settings.VuKind == 0 ? "Klassisches VU-Meter: träger Zeiger, wie er sich anhört (Standard)."
                    : _settings.VuKind == 1 ? "BBC-Spitzenpegel: schwarzes Instrument mit weißen Zahlen 1–7, flinker Zeiger, fällt langsam zurück."
                    : _settings.VuKind == 2 ? "DIN-Spitzenpegel: helle Skala in dB, flinker Zeiger, fällt langsam zurück – wie im deutschen Rundfunk."
                    : "Magisches Auge: grün leuchtende Röhre wie an alten Radios – je lauter, desto kleiner der dunkle Spalt.",
                Theme.VuKindNames, () => _settings.VuKind, i => SetVuKind(i)).Enabled = () => !_settings.VuOff || !_settings.BridgeOff;
        }

        // ================================================================ Meterbridge: Jetzt laeuft, Oszilloskop, Ducking

        private void AddBridge330(SPage p)
        {
            p.Choice("„Jetzt läuft“ als", () => _settings.CoverArt == 0 ? "Album-Cover (Standard)."
                    : _settings.CoverArt == 1 ? "Drehende Schallplatte mit dem Cover als Etikett – bei Pause hebt der Tonarm ab."
                    : _settings.CoverArt == 2 ? "Bandmaschine: zwei Spulen drehen sich, die REC-Lampe leuchtet, solange etwas läuft."
                    : "Kassettendeck: Kassette mit dem Cover als Etikett, im Sichtfenster drehen sich die Spulen – der Bandwickel wandert im Laufe des Titels.",
                CoverView.ArtStyleNames, () => _settings.CoverArt, i =>
                {
                    _settings.CoverArt = Math.Max(0, Math.Min(3, i));
                    CoverView.ArtStyle = _settings.CoverArt;
                    RestyleBridges();
                    ScheduleSave();
                }).Enabled = () => !_settings.CoverOff && !_settings.BridgeOff;
            p.Toggle("Oszilloskop wie ein Messgerät", "Grüner Bildschirm mit Raster und Teilstrichen, die letzten Bilder leuchten kurz nach – wie ein echtes Oszilloskop.",
                () => _settings.ScopeLookOn, v => { _settings.ScopeLookOn = v; OscView.ScopeLook = v; RestyleBridges(); ScheduleSave(); })
                .Enabled = () => !_settings.OscOff && !_settings.BridgeOff;
            p.Toggle("Ducking sichtbar", "Ist Ducking gerade aktiv, zeigt eine orange Linie in der Meterbridge, wer auslöst und welche Kanäle leiser werden (mit dB).",
                () => _settings.DuckLinesOn, v =>
                {
                    _settings.DuckLinesOn = v; MeterBridge.DuckLinesOn = v;
                    if (!v) { MeterBridge.DuckFrom = null; MeterBridge.DuckTo.Clear(); _demoDuck = false; }
                    _duckSig330 = null;
                    MeterBridge.DuckVer++;
                    ScheduleSave();
                }).Enabled = () => !_settings.BridgeOff;
        }

        private void TickDuckLines()
        {
            string from = _duckActive && _settings.DuckOn ? (_settings.IsDuckTrigger(_duckFrom) ? _duckFrom : _settings.DuckTrigger) : null;   // 4.2.1: vom Ausloeser, der gerade spricht
            float db = (float)Math.Round(_duckApplied);
            string sig = from == null ? "" : from + "|" + db + "|" + string.Join(",", _settings.DuckTargets ?? new List<string>());
            if (sig == _duckSig330) return;
            _duckSig330 = sig;
            MeterBridge.DuckFrom = from;
            MeterBridge.DuckTo.Clear();
            if (from != null && _settings.DuckTargets != null)
                foreach (var t in _settings.DuckTargets) if (t != from) MeterBridge.DuckTo.Add(t);
            MeterBridge.DuckDb = db;
            MeterBridge.DuckVer++;
        }

        // ================================================================ ON-AIR

        private bool OnAirNow
        {
            get
            {
                if (_settings.ObsOn && _settings.ObsOnAir && ObsLive400) return true;   // 4.0: OBS streamt/nimmt auf
                if (_settings.OnAirMode <= 0) return false;
                if (_demoOnAir || _top.RecOn) return true;
                return _settings.OnAirMode == 2 && _appliedStream != null && !_settings.StreamMute;
            }
        }

        private void AddOnAir330(SPage p)
        {
            p.Choice("ON-AIR-Lampe", () => _settings.OnAirMode == 0 ? "Aus (Standard)."
                    : _settings.OnAirMode == 1 ? "Leuchtkasten „ON AIR“ oben in der Kopfleiste – leuchtet rot, solange aufgenommen wird."
                    : "Leuchtet bei einer Aufnahme und wenn der Stream-Mix läuft (z. B. für OBS).",
                new[] { "Aus", "Bei Aufnahme", "Aufnahme oder Stream" }, () => _settings.OnAirMode, i =>
                {
                    _settings.OnAirMode = Math.Max(0, Math.Min(2, i));
                    _top.OnAirMode = _settings.OnAirMode;
                    _top.OnAir = OnAirNow;
                    _top.Invalidate();
                    ScheduleSave();
                });
        }

        // ================================================================ Optik der Zuege

        private void RefreshStripsQuiet()
        {
            foreach (var s in _channelStrips) s.InvalidateChrome();
            foreach (var s in _groupStrips) s.InvalidateChrome();
            foreach (var s in _outputStrips.Values) s.InvalidateChrome();
            _master.InvalidateChrome();
            _mic.InvalidateChrome();
            _streamStrip?.InvalidateChrome();
        }

        private void AddLook330(SPage p)
        {
            p.Section("Mehr Licht und Wirkung");
            p.Toggle("LED-Kranz um die Drehknöpfe", "Statt Punkten bzw. Bogen ein Kranz aus kleinen LED-Segmenten um jeden Knopf, wie an Digitalpulten.",
                () => _settings.KnobRingOn, v => { _settings.KnobRingOn = v; Theme.KnobRing = v; RefreshStrips(); RestyleBridges(); });
            p.Toggle("Leuchtende Faderkappen", "In jeder Faderkappe leuchtet ein Streifen in der Kanalfarbe – je lauter der Kanal, desto heller, mit Schein drumherum.",
                () => _settings.CapGlowOn, v => { _settings.CapGlowOn = v; StripBase.CapGlowOn = v; RefreshStrips(); });
            p.Toggle("Funken bei Übersteuerung", "Springt PEAK an, sprühen kurz ein paar Funken aus der Leuchte.",
                () => _settings.SparksOn, v => { _settings.SparksOn = v; StripBase.SparksOn = v; ScheduleSave(); });
            p.Choice("Gebrauchsspuren", () => _settings.Wear == 0 ? "Aus (Standard): alles wie neu."
                    : _settings.Wear == 1 ? "Leicht: feine Kratzer und abgegriffene Faderkappen – wie ein Pult, das schon ein paar Jahre im Studio steht."
                    : "Stark: deutlich benutzt, viele Kratzer und blanke Stellen.",
                Theme.WearNames, () => _settings.Wear, i => { _settings.Wear = Math.Max(0, Math.Min(2, i)); Theme.Wear = _settings.Wear; RefreshStrips(); });
            p.Toggle("Röhren glühen in den Seitenteilen", "Hinter Lüftungsschlitzen in den Seitenteilen glühen Röhren orange – heller, je lauter es ist. Nur mit Seitenteilen (Material → Seitenteile).",
                () => _settings.TubeGlowOn, v => { _settings.TubeGlowOn = v; Theme.TubeGlow = v; _tubeQ = -1; ApplyCheeks(); ScheduleSave(); });
            p.Toggle("Aufwärmen beim Start", "Beim Start glühen LEDs und Anzeigen in gut zwei Sekunden langsam auf – wie ein Gerät, das warm wird. Einschalten zeigt es einmal.",
                () => _settings.WarmUpOn, v =>
                {
                    _settings.WarmUpOn = v;
                    if (v) { Theme.WarmLevel = 0f; _warmStart = _clock330.Elapsed.TotalSeconds; RefreshStripsQuiet(); }
                    else if (Theme.WarmLevel < 1f) { Theme.WarmLevel = 1f; _warmStart = -1; RefreshStripsQuiet(); RestyleBridges(); }
                    ScheduleSave();
                });
            p.Toggle("Ausschalt-Show", "Beim Beenden fahren alle Fader herunter, die Pegel fallen, dann wird das Fenster dunkel (gut eine Sekunde). Nicht beim Herunterfahren von Windows.",
                () => _settings.PowerOffShowOn, v => { _settings.PowerOffShowOn = v; ScheduleSave(); });
            p.Choice("Wand hinter dem Pult", () => _settings.Wall == 0 ? "Keine (Standard)."
                    : "Hinter dem Pult: " + Theme.WallNames[_settings.Wall] + " (abgedunkelt)." + (string.IsNullOrEmpty(_settings.BgImagePath) ? "" : " Ein eigenes Hintergrundbild hat Vorrang."),
                Theme.WallNames, () => _settings.Wall, i => { _settings.Wall = Math.Max(0, Math.Min(Theme.WallNames.Length - 1, i)); RebuildBgImage(); ScheduleSave(); });
            p.Choice("Bewegter Hintergrund", () => _settings.LiveBg == 0 ? "Aus (Standard)."
                    : _settings.LiveBg == 1 ? "Funkelnder Sternenhimmel in den freien Flächen."
                    : _settings.LiveBg == 2 ? "Ganz langsam wandernder Farbverlauf."
                    : _settings.LiveBg == 4 ? "Regen am Fenster: Tropfen fallen in den freien Flächen, ein paar rinnen langsam die Scheibe hinunter."
                    : _settings.LiveBg == 5 ? "Schneefall: Flocken rieseln langsam durch die freien Flächen."
                    : _settings.LiveBg == 6 ? "Nordlicht: grüne und violette Schleier wehen oben durch die freien Flächen."
                    : _settings.LiveBg == 7 ? "Unterwasser: Lichtstrahlen fallen schräg von oben, Blasen steigen auf."
                    : _settings.LiveBg == 8 ? "Kaminfeuer: warmes Flackern von unten, ein paar Funken steigen auf."
                    : _settings.LiveBg == 9 ? "Matrix-Regen: grüne Zeichen fallen in Spalten – bei Musik schneller."
                    : _settings.LiveBg == 10 ? "Gewitter: dunkle Wolken und Regen – bei Bass-Schlägen in der Musik blitzt es (sonst ab und zu von selbst)."
                    : _settings.LiveBg == 11 ? "Sternenwirbel: eine Galaxie dreht sich ganz langsam um die Fenstermitte."
                    : _settings.LiveBg == 12 ? "Glühwürmchen: kleine Lichter schweben umher und leuchten nacheinander auf."
                    : "Bühnennebel: weiche Schwaden ziehen unten vorbei.",
                Theme.LiveBgNames, () => _settings.LiveBg, i =>
                {
                    _settings.LiveBg = Math.Max(0, Math.Min(Theme.LiveBgNames.Length - 1, i));
                    Theme.LiveBg = _settings.LiveBg;
                    _channelHost.Invalidate(false); _outputHost.Invalidate(false);
                    ScheduleSave();
                });
            p.Toggle("Hinweise im Pult-Design", "Die kleinen Hinweise unter der Maus erscheinen dunkel mit Akzent-Kante, passend zum Pult – statt im Windows-Stil.",
                () => _settings.PultTipsOn, v => { _settings.PultTipsOn = v; PultTip.On = v; ScheduleSave(); });
        }

        // ================================================================ Aufwaermen und Ausschalt-Show

        private void TickWarmUp(double now)
        {
            if (_warmStart < 0)
            {
                // erst aufwaermen, wenn etwas zu sehen ist (Start im Infobereich: beim ersten Anzeigen)
                if (Theme.WarmLevel >= 1f || !(PultShown || BridgeWinShown || VuWindowOpen || (_mini != null && _mini.Visible))) return;
                _warmStart = now;
            }
            float t = (float)((now - _warmStart) / 2.4);
            float q = t >= 1f ? 1f : (float)Math.Floor(Math.Max(0f, t) * 10f) / 10f;
            if (Math.Abs(q - Theme.WarmLevel) < 0.001f) return;
            Theme.WarmLevel = q;
            if (q >= 1f) _warmStart = -1;
            RefreshStripsQuiet();
            RestyleBridges();
            _top.Invalidate();
            _mini?.Invalidate();
        }

        /// <summary>Beim Schliessen: statt sofort zu beenden erst die Show (true = Schliessen aufgeschoben).</summary>
        private bool TryPowerOffShow(FormClosingEventArgs e)
        {
            if (_powerOffStart >= 0 && !_powerOffDone) { e.Cancel = true; return true; }   // laeuft schon
            if (!_settings.PowerOffShowOn || _powerOffDone || Program.RestartRequested) return false;
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return false;   // Windows faehrt herunter: sofort
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray && _tray != null) return false;   // geht nur in den Infobereich
            if (!PultShown) return false;
            e.Cancel = true;
            StartPowerOff();
            return true;
        }

        private void StartPowerOff()
        {
            if (_powerOffStart >= 0) return;
            _powerOffStart = _clock330.Elapsed.TotalSeconds;
            _powerOffOpacity = Opacity;
            StripBase.PowerOffT = 0f;
        }

        private void TickPowerOff(double now)
        {
            if (_powerOffStart < 0 || _powerOffDone) return;
            float t = (float)(now - _powerOffStart);
            StripBase.PowerOffT = t;
            if (t > 0.85f)
            {
                double f = Math.Max(0.0, 1.0 - (t - 0.85f) / 0.4f);
                try { Opacity = Math.Max(0.0, _powerOffOpacity * f); } catch { }
            }
            if (t >= 1.3f)
            {
                _powerOffDone = true;
                BeginInvoke(new Action(Close));
            }
        }

        // ================================================================ Kanaele: Symbole, Notizen, Gruppen-Klammer, Kabel-Ansicht

        private void AddChannels330(SPage p)
        {
            p.Toggle("Symbol im Namensschild", "Ein kleines Symbol rechts im Namensschild (Controller, Note, Sprechblase …) – automatisch nach dem Namen oder per Rechtsklick auf den Kanal → Symbol.",
                () => _settings.SymbolsOn, v => { _settings.SymbolsOn = v; ChannelStrip.SymbolsOn = v; RefreshStrips(); });
            p.Toggle("Notizzettel", "Gelbe Haftnotiz an einem Kanal (z. B. „nachts leiser“). Rechtsklick auf den Kanal → Notiz …; Maus drauf zeigt sie, Klick ändert sie.",
                () => _settings.NotesOn, v => { _settings.NotesOn = v; ChannelStrip.NotesOn = v; RelayoutAllStrips(); RefreshStrips(); });
            p.Toggle("Gruppen-Klammer", "Eine Linie in der Gruppenfarbe oben über den Kanälen, die zu einer Gruppe gehören.",
                () => _settings.GroupBracketsOn, v => { _settings.GroupBracketsOn = v; ChannelStrip.GroupBracketsOn = v; RefreshStrips(); });
            p.Toggle("Kabel-Ansicht", "Ein Fenster wie ein Steckfeld: links die Kanäle, rechts die Ausgänge, dazwischen Kabel. Kabel umstecken = Ausgang des Kanals ändern.",
                () => _settings.PatchbayOn, v => { _settings.PatchbayOn = v; if (!v && _patch != null && !_patch.IsDisposed) _patch.Close(); ScheduleSave(); });
            p.Button("Kabel-Ansicht öffnen", null, "Öffnen", r => ShowPatchbay(), () => _patch != null && !_patch.IsDisposed ? "ist offen" : null).Visible = () => _settings.PatchbayOn;
        }

        /// <summary>Gruppe, zu der der Kanal gehoert (fuer die Klammer): Farbe und ob er am Anfang/Ende der Gruppe steht.</summary>
        public bool GroupBracketOf(ChannelConfig c, out Color col, out bool first, out bool last)
        {
            col = Color.Empty; first = last = false;
            var g = _settings.Groups?.FirstOrDefault(x => x.Members != null && x.Members.Contains(c.Id));
            if (g == null) return false;
            col = Color.FromArgb(255, Color.FromArgb(g.Color));
            var shown = _channelStrips.Where(s => s.Visible).ToList();
            int i = shown.FindIndex(s => s.Config == c);
            if (i < 0) return false;
            first = i == 0 || !g.Members.Contains(shown[i - 1].Config.Id);
            last = i == shown.Count - 1 || !g.Members.Contains(shown[i + 1].Config.Id);
            return true;
        }

        public void EditNote(ChannelConfig c)
        {
            if (c == null) return;
            using (var dlg = new InputDialog("Notiz", "Notiz für „" + c.Name + "“:", c.Note ?? "", 120))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                SetNote(c, dlg.Value);
            }
        }

        private void SetNote(ChannelConfig c, string text)
        {
            c.Note = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (c.Note != null && !_settings.NotesOn) { _settings.NotesOn = true; ChannelStrip.NotesOn = true; }
            RelayoutAllStrips();
            RefreshStrips();
        }

        private void SetSymbol(ChannelConfig c, int sym)
        {
            c.Symbol = sym;
            foreach (var s in _channelStrips) if (s.Config == c) s.InvalidateChrome();
            ScheduleSave();
        }

        private ToolStripMenuItem SymbolSub(ChannelConfig c)
        {
            var sub = Sub("Symbol");
            for (int i = 0; i < Theme.SymbolNames.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(Theme.SymbolNames[i], null, (s, e) => SetSymbol(c, k)) { Checked = c.Symbol == i });
            }
            sub.DropDownItems.Add(new ToolStripMenuItem("Keins", null, (s, e) => SetSymbol(c, 99)) { Checked = c.Symbol == 99 });
            return sub;
        }

        private void AddChannel330Items(ToolStripItemCollection items, ChannelConfig c)
        {
            if (_settings.SymbolsOn) items.Add(SymbolSub(c));
            if (_settings.NotesOn)
            {
                items.Add(new ToolStripMenuItem(c.Note == null ? "Notiz …" : "Notiz ändern …", null, (s, e) => BeginInvoke(new Action(() => EditNote(c)))));
                if (c.Note != null) items.Add(new ToolStripMenuItem("Notiz löschen", null, (s, e) => SetNote(c, null)));
            }
        }

        private void ShowPatchbay()
        {
            if (_patch != null && !_patch.IsDisposed) { _patch.Activate(); return; }
            _patch = new PatchbayWindow(this,
                () => _settings.Channels.Where(c => !c.Hidden).ToList(),
                () => _snap.Devices,
                () => _snap.RoutingSupported || _demo,
                (c, id, name) => SetRoute(c, id, name));
            _patch.FormClosed += (s, e) => _patch = null;
            _patch.Show(this);
        }

        // ================================================================ Tastenkuerzel

        private void AddHotkeyActions330(List<HotkeyAction> list)
        {
            if (_settings.PatchbayOn) list.Add(new HotkeyAction("patchbay", "Kabel-Ansicht öffnen", "Allgemein"));
        }

        private bool ExecuteHotkey330(string action)
        {
            if (action == "patchbay") { if (_settings.PatchbayOn) ShowPatchbay(); return true; }
            return false;
        }
    }
}
