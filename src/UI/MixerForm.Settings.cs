using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>Einstellungsfenster (OPTIONEN): alle Seiten und was die Schalter tun.</summary>
    internal sealed partial class MixerForm
    {
        private SettingsWindow _settingsWin;
        private int _sleepChoice;

        /// <summary>Einstellungen oeffnen (bzw. nach vorne holen). page = Titel einer Seite.</summary>
        public void ShowSettings(string page = null)
        {
            if (_settingsWin != null && !_settingsWin.IsDisposed)
            {
                if (page != null) _settingsWin.SelectPage(page);
                if (_settingsWin.WindowState == FormWindowState.Minimized) _settingsWin.WindowState = FormWindowState.Normal;
                _settingsWin.Activate();
                return;
            }
            if (!Visible) RestoreFromTray();
            _settingsWin = new SettingsWindow(BuildSettingsPages(), page) { SimpleGet = () => _settings.SimpleSettings, SimpleSet = SetSimple400 };   // 4.0: Nur das Wichtigste
            _settingsWin.FormClosed += (s, e) => _settingsWin = null;
            _settingsWin.Show(this);
        }

        /// <summary>Kontextmenue unter einem Knopf im Einstellungsfenster zeigen.</summary>
        private void ShowMenuAt(ContextMenuStrip m, Rectangle screenButton) => m.Show(new Point(screenButton.X, screenButton.Bottom + Theme.Px(2)));

        private void ShowDropDownAt(ToolStripMenuItem item, Rectangle screenButton)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = true;
            foreach (var it in item.DropDownItems.Cast<ToolStripItem>().ToList()) m.Items.Add(it);
            ShowMenuAt(m, screenButton);
        }

        private List<SPage> BuildSettingsPages()
        {
            var pages = new List<SPage>();
            bool autostart = IsAutostart();

            // ------------------------------------------------------------ Allgemein
            var p = new SPage("Allgemein", "", "Fenster, Start mit Windows und Infobereich.");
            p.Section("Fenster");
            AddOwnTitle310(p);   // 3.1: eigene Titelleiste
            AddOsd310bSettings(p);   // 3.1: Lautstaerke-Einblendung
            AddShot320(p);           // 3.2: Bild vom Pult
            p.Toggle("Immer im Vordergrund", "Das Mischpult bleibt über allen anderen Fenstern.",
                () => _settings.AlwaysOnTop, v => { _settings.AlwaysOnTop = v; TopMost = v; ScheduleSave(); });
            p.Toggle("Mit Windows starten", "Startet beim Anmelden unsichtbar im Infobereich (unten rechts).",
                () => autostart, v => { SetAutostart(v); autostart = IsAutostart(); }).Enabled = () => Win32.IsWindows && !_demo;
            p.Toggle("Beim Schließen im Infobereich weiterlaufen", "Das X versteckt das Fenster nur – der Ton wird weiter gemischt.",
                () => _settings.CloseToTray, v => { _settings.CloseToTray = v; ScheduleSave(); });
            p.Button("Mini-Ansicht", "Kleines Fenster immer im Vordergrund: pro Kanal ein Regler mit Pegel und Stummschalter.", "Öffnen",
                r => SetMini(true));
            AddWindow346(p);   // 3.4.6: Schublade, Infobereich-Symbol, Song-Einblendung
            p.Section("Windows");
            p.Button("Windows-Lautstärkemixer", "Die Lautstärke-Einstellungen von Windows öffnen.", "Öffnen", r => OpenUri("ms-settings:apps-volume"));
            p.Button("App-Geräteeinstellungen zurücksetzen", "Alle in Windows gespeicherten Ausgabegeräte pro App löschen – alle Apps spielen wieder auf dem Standardgerät.",
                "Zurücksetzen …", r => ResetAppRouting()).Enabled = () => _snap.RoutingSupported;
            p.Info("Anzeige und Schrift", () => DisplayInfo());
            p.Choice("Schrift", () => Theme.GdiText ? "Windows-Schrift: am schärfsten, wie in normalen Windows-Fenstern."
                    : "Geglättet: etwas weicher. Nimm das, falls Schrift bei dir klobig oder bunt aussieht.",
                new[] { "Windows (scharf)", "Geglättet" }, () => Theme.GdiText ? 0 : 1, i => SetGdiText(i == 0))
                .Enabled = () => Win32.IsWindows;
            AddAccelSettings(p);   // 3.0: Hardware-Beschleunigung
            p.Section("Leistung");
            p.Info("Flüssig, auch beim Spielen", () => PerfStatusText());
            p.Choice("Bilder pro Sekunde", () => _settings.PerfFps == 0
                    ? "Gleichmäßig passend zur Bildwiederholrate des Bildschirms (höchstens " + AutoFpsMax + ") – Standard."
                    : "So oft pro Sekunde bewegen sich Pegel und Anzeigen. Weniger = weniger Rechenzeit.",
                PerfFpsNames, () => Math.Max(0, Array.IndexOf(MixerSettings.PerfFpsValues, _settings.PerfFps)), i => SetPerfFps(i));
            AddPerf341(p);   // 3.4.1: Bildrate, im Hintergrund sparen
            p.Toggle("Schnelles Zeichnen", "Leuchtende LEDs und das Spektrum kommen fertig aus einem Bildspeicher, statt jedes Bild LED für LED gezeichnet zu werden. Die Meterbridge erneuert nur, was sich ändert. Sieht gleich aus, braucht aber viel weniger Rechenzeit. Aus = wie bis 2.15.2.",
                () => !_settings.FastDrawOff, v => SetFastDraw(v));
            p.Toggle("Genauer Takt", "Ein eigener, genauer Zeitgeber gibt den Takt vor, und die Pegel der Ausgänge werden alle 4 ms gemessen – alles läuft gleichmäßig statt ruckelig. Aus = wie bis 2.15.",
                () => !_settings.PacerOff, v => SetPacer(v));
            p.Toggle("Pult bekommt Vorrang", "Läuft ein Spiel, kommt das Pult beim Zeichnen und Messen nicht ganz hinten dran. Es braucht dafür nur Bruchteile einer Millisekunde – das Spiel merkt davon nichts.",
                () => !_settings.PerfPriorityOff, v => SetPerfPriority(v));
            p.Toggle("Spiele-Modus", "Läuft ein Spiel oder ein Vollbild-Programm im Vordergrund, spart das Pult Rechenzeit (siehe unten). Der Ton läuft immer normal weiter.",
                () => !_settings.GameModeOff, v => SetGameMode(v));
            p.Toggle("Hinter Vollbild-Spiel pausieren", "Verdeckt ein Vollbild-Spiel das Pult (gleicher Bildschirm), stehen Pegel und Anzeigen still – man sieht sie ja nicht. Steht das Pult auf einem zweiten Bildschirm, läuft es normal weiter.",
                () => !_settings.GamePauseOff, v => SetGamePause(v)).Enabled = () => !_settings.GameModeOff;
            p.Toggle("Leucht-Effekte beim Spielen sparen", "Lichtschein um die LEDs, Nachglühen und leuchtende Fader-Spur sind aus, solange ein Spiel im Vordergrund ist.",
                () => !_settings.GameFxOff, v => SetGameFx(v)).Enabled = () => !_settings.GameModeOff;
            p.Toggle("… nur wenn der PC nicht hinterherkommt", "Die Leucht-Effekte bleiben auch beim Spielen an (das Pult sieht gleich aus, Pegel glühen weich nach) und gehen erst aus, wenn das Pult wirklich Bilder verliert. Aus = wie bis 3.0.1: beim Spielen immer aus.",
                () => !_settings.SmartFxOff, v => SetSmartFx(v)).Enabled = () => !_settings.GameModeOff && !_settings.GameFxOff;
            p.Choice("Bilder pro Sekunde beim Spielen", () => _settings.GameFps == 0 ? "Wie sonst (Standard)." : "Höchstens " + _settings.GameFps + ", solange ein Spiel im Vordergrund ist.",
                GameFpsNames, () => Math.Max(0, Array.IndexOf(MixerSettings.GameFpsValues, _settings.GameFps)), i => SetGameFps(i)).Enabled = () => !_settings.GameModeOff;
            p.Section("Mischpult");
            p.Info("Mischpult " + Program.Version + " · made by " + Program.Author, () => "App-Audio-Mixer für Windows 10/11");
            AddWizardSettings(p);   // 2.18
            p.Button("Über Mischpult", "Alle Funktionen und Tipps auf einen Blick.", "Anzeigen", r => ShowAbout());
            var quit = p.Button("Mischpult beenden", "Beendet das Mischpult ganz (auch im Infobereich).", "Beenden", r => ExitApp());
            quit.Danger = true;
            AddGeneral400(p);   // 4.0: einfache Einstellungen, Installieren, Was ist neu
            AddUpdate410(p);    // 4.1: Installieren & Aktualisieren, Uebersicht, Reparieren
            AddComfort420(p);   // 4.2: Schalter im Was-ist-neu, Pakete, Doppelklick
            AddA11y400(p);      // 4.0: Barrierefreiheit
            pages.Add(p);

            // ------------------------------------------------------------ 4.0: Fenster & Verbindungen
            p = new SPage("Fenster & Verbindungen", "\uE8A7", "Overlay, Widget, Mini-Player, Raum, Zeitleiste, Statistik, Ton-Doktor, MIDI, OBS, WLED.");
            AddWindows400(p);
            AddWindowsFinal400(p);   // 4.0 finale: Szenen-Galerie, EQ-Uebersicht, Befehlssuche, Fehlerbericht
            AddExtras420(p);         // 4.2: Klang-Kunst
            AddExtern400(p);
            AddRemote440(p);         // 4.4: Fernbedienung im Browser
            pages.Add(p);

            // ------------------------------------------------------------ Aussehen
            p = new SPage("Aussehen", "", "Design, Farben und Aufbau des Pults.");
            p.Section("Design");
            p.Choice("Design", () => Theme.StyleHints[Math.Max(0, Math.Min(Theme.StyleHints.Length - 1, _settings.UiStyle))],
                Theme.StyleNames, () => _settings.UiStyle, i => SetScheme(_settings.ColorScheme, _settings.AccentColor, i));
            p.Choice("Farbschema", () => "Grundfarbe des Pults.", Theme.SchemeNames, () => _settings.ColorScheme, i => SetScheme(i, _settings.AccentColor));
            p.Swatches("Akzentfarbe", Enumerable.Range(0, Theme.AccentNames.Length).Select(Theme.AccentPreview).ToArray(), Theme.AccentNames,
                () => _settings.AccentColor, i => SetScheme(_settings.ColorScheme, i));
            AddAccent320(p);   // 3.2: Akzent von Windows oder vom Album-Cover
            p.Choice("Pegel-Farben", () => _settings.MeterColors == 3 ? "Jeder Pegel leuchtet in seiner Kanalfarbe – oben bleibt es rot." : "Farben der LED-Pegel – oben bleibt es immer rot (Übersteuerung).",
                Theme.MeterStyleNames, () => _settings.MeterColors, i => SetMeterStyle(i));
            AddMeterColors320(p);   // 3.2: eigene Pegel-Farben
            p.Choice("Zahlen-Anzeigen", () => _settings.LcdStyle == 0 ? (Theme.Vintage ? "Passend zum Design – bei Vintage Nixie-Röhren." : "Passend zum Design.")
                    : _settings.LcdStyle == 1 ? "Grünes LCD mit dunklen Ziffern, wie an alten Taschenrechnern und Geräten."
                    : _settings.LcdStyle == 2 ? "Rote LED-Ziffern auf Schwarz." : _settings.LcdStyle == 3 ? "Orange glühende Nixie-Röhren hinter Rauchglas."
                    : _settings.LcdStyle == 5 ? "Punktmatrix: bernsteinfarbene Leuchtpunkte wie an alten Anzeigetafeln." : "Türkis leuchtendes VFD-Display wie an Hi-Fi-Geräten.",
                Theme.LcdStyleNames, () => _settings.LcdStyle, i => SetLcdStyle(i));
            p.Toggle("Kanalfarbe auf Fadern und Knöpfen", "Faderkappen bekommen eine farbige Einlage, die Drehknöpfe der Kanäle eine farbige Kappe – wie bei Pulten mit farbigen Kappen.",
                () => !_settings.ColorCapsOff, v => SetColorCaps(v));
            p.Section("Pult");
            p.Toggle("Meterbridge (große Pegel oben)",
                () => _bridgeNoRoom ? "Ist an, aber das Fenster ist zu niedrig – Fenster höher ziehen, dann erscheint sie." : "Leiste über dem Pult: großer Pegel über jedem Kanal, mit dB und SIG/PEAK.",
                () => !_settings.BridgeOff, v => SetBridge(v));
            p.Choice("Breite der Pegel in der Meterbridge", () => _settings.BridgeBarWidth == 0 ? "Schmale LED-Säulen wie an einer echten Meterbridge (Standard)."
                    : _settings.BridgeBarWidth == 1 ? "Mittelbreite Säulen." : "Breite Säulen (wie bis Version 2.14).",
                MeterBridge.BarWidthNames, () => _settings.BridgeBarWidth, i => SetBridgeBarWidth(i)).Enabled = () => !_settings.BridgeOff;
            p.Choice("Meterbridge zeigt", () => _settings.BridgeStyle == 0 ? "LED-Säulen (Standard)."
                    : "Kleine analoge Zeigerinstrumente über jedem Kanal – wie bei alten Studiopulten.",
                BridgeStyleNames, () => _settings.BridgeStyle, i => SetBridgeStyle(i));
            AddBridgeHeight310(p);   // 3.1: Hoehe der Meterbridge
            p.Button("Welche Pegel in der Meterbridge stehen", "Jeden Pegel der Meterbridge einzeln ein- oder ausblenden – Ausgeblendete machen Platz für Spektrum, Lautheit, Stereo-Bild und Uhr. Auch per Rechtsklick auf einen Pegel oben.",
                "Wählen ▾", r => ShowBridgeColumnsMenu(r), () => BridgeShownText()).Enabled = () => !_settings.BridgeOff;
            p.Toggle("Meterbridge frei anordnen", "Pegel und Anzeigen (Spektrum, Lautheit, Stereo-Bild, Uhr) in eigener Reihenfolge – einfach oben mit der Maus an die gewünschte Stelle ziehen. Aus = jeder Pegel steht über seinem Kanal.",
                () => _settings.BridgeArrange, v => SetBridgeArrange(v)).Enabled = () => !_settings.BridgeOff;
            p.Toggle("In der Meterbridge mit der Maus verschieben", "Pegel oder Anzeige mit gedrückter linker Maustaste nach links oder rechts ziehen – dann ist die Meterbridge frei angeordnet. Aus = nichts verrutscht aus Versehen.",
                () => !_settings.BridgeDragOff, v => SetBridgeDrag(v)).Enabled = () => !_settings.BridgeOff;
            p.Button("Anordnung der Meterbridge zurücksetzen", "Wie am Anfang: jeder Pegel über seinem Kanal, die Anzeigen in der Lücke.",
                "Zurücksetzen", r => ResetBridgeArrange(), () => _settings.BridgeArrange ? "frei angeordnet" : null).Enabled = () => _settings.BridgeArrange || _settings.BridgeFreeOrder != null;
            p.Toggle("Pegel zusammenrücken", "Ausgeblendete Pegel lassen keine Lücke in der Meterbridge: die Kanäle rücken nach links, die Ausgänge nach rechts – in der Mitte wird Platz frei. Aus = jeder Pegel bleibt genau über seinem Kanal. (Nicht bei „frei anordnen“.)",
                () => !_settings.BridgePackOff, v => SetBridgePack(v)).Enabled = () => !_settings.BridgeOff && !_settings.BridgeArrange;
            p.Button("Reihenfolge der Anzeigen", "Wie Spektrum, Lautheits-Verlauf, Stereo-Bild und Uhr in der freien Mitte der Meterbridge nebeneinander stehen (von links). Auch per Rechtsklick auf eine Anzeige: nach links/rechts. (Bei „frei anordnen“: mit der Maus ziehen.)",
                "Ändern ▾", r => ShowBridgeOrderMenu(r), () => BridgeOrderText()).Enabled = () => !_settings.BridgeOff && !_settings.BridgeArrange;
            p.Button("Meterbridge im eigenen Fenster", "Alle Pegel nebeneinander in einem frei großen Fenster – z. B. auf dem zweiten Bildschirm beim Spielen. Läuft auch, wenn das Pult selbst versteckt oder verdeckt ist.",
                "Öffnen", r => ShowBridgeWindow(), () => _bridgeWin != null && !_bridgeWin.IsDisposed ? "ist offen" : null);
            p.Toggle("Spektrum in der Meterbridge", "Leuchtende Frequenzbalken (Bass links, Höhen rechts) von dem, was gerade läuft – in der freien Lücke der Meterbridge, sonst über dem MASTER.",
                () => !_settings.SpecOff, v => SetSpectrumOn(v)).Enabled = () => !_settings.BridgeOff;
            p.Button("Spektrum zeigt", "Von welchem Ausgang: Standard-Ausgabe, ein bestimmtes Gerät oder alle zusammen (z. B. Musik auf den Lautsprechern und Spiel auf dem Headset). Gilt auch fürs große VU-Meter.",
                "Wählen ▾", r =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    AddSpecSourceItems(m.Items);
                    ShowMenuAt(m, r);
                }, () => SpecSourceText(true));
            p.Choice("Spektrum als", () => _settings.SpecStyle == 0 ? "LED-Balken (Standard)."
                    : _settings.SpecStyle == 1 ? "Wasserfall: die Frequenzen laufen als Farbbild nach unten durch – oben ist jetzt, je heller, desto lauter."
                    : _settings.SpecStyle == 2 ? "Kurve: eine leuchtende Linie durch alle Frequenzen, darunter gefüllt; gepunktet die Spitzen."
                    : _settings.SpecStyle == 3 ? "Gespiegelt: die LED-Balken wachsen von der Mitte nach oben und unten."
                    : _settings.SpecStyle == 4 ? "Rund: LED-Strahlen um einen Kreis – Bass oben, Höhen unten, links und rechts gespiegelt; die Mitte glimmt mit dem Bass."
                    : _settings.SpecStyle == 5 ? "3D-Landschaft: vorn das Spektrum von jetzt, dahinter laufen die letzten Augenblicke als Gebirge nach hinten weg."
                    : "Lichtorgel: drei Lampen wie in den 70ern – rot für den Bass, gelb für die Mitte, blau für die Höhen.",
                SpecStyleNames, () => _settings.SpecStyle, i => SetSpecStyle(i));
            p.Choice("Spektrum-Farben", () => _settings.SpecPalette == 0 ? "Klassisch: grün, gelb, rot wie die Pegel (Standard)."
                    : _settings.SpecPalette == 1 ? "Regenbogen: jede Frequenz in ihrer Farbe, Bass rot bis Höhen violett."
                    : _settings.SpecPalette == 2 ? "Akzentfarbe: alles in der Farbe des Designs, oben heller."
                    : _settings.SpecPalette == 3 ? "Feuer: dunkelrot über orange bis gelb." : "Eis: tiefblau über cyan bis weiß.",
                SpectrumView.PaletteNames, () => _settings.SpecPalette, i => SetSpecPalette(i));
            p.Toggle("Goniometer (Stereo-Bild) in der Meterbridge", "Leuchtende Punktwolke wie im Tonstudio: senkrecht = Mitte (mono), breit = Stereo; darunter die Korrelation (rot = gegenphasig). Steht in der freien Lücke der Meterbridge, wenn Platz ist.",
                () => !_settings.GonioOff, v => SetGonio(v)).Enabled = () => !_settings.BridgeOff;
            p.Button("Stereo-Bild zeigt", "Von welchem Ausgang: wie das Spektrum, Standard-Ausgabe, ein bestimmtes Gerät oder alle zusammen.",
                "Wählen ▾", r =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    AddGonioSourceItems(m.Items);
                    ShowMenuAt(m, r);
                }, () => GonioSourceText()).Enabled = () => !_settings.GonioOff;
            p.Toggle("Lautheits-Verlauf in der Meterbridge", "Kurve, wie laut es sich die letzten Minuten angefühlt hat (LUFS) – gestrichelt die −14 von Spotify und YouTube. Steht in der freien Lücke, wenn Platz ist. Klick darauf: Ausgang, Zeitraum, löschen.",
                () => LoudHistOn, v => SetLoudHist(v));
            p.Button("Lautheits-Verlauf misst", "Standard-Ausgabe (wie im MASTER) oder ein bestimmtes Gerät, z. B. das Headset – auch per Klick auf den Verlauf.",
                "Wählen ▾", r =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    AddLoudSourceItems(m.Items);
                    ShowMenuAt(m, r);
                }, () => LoudSourceText()).Enabled = () => LoudHistOn;
            p.Choice("Zeitraum", () => "So weit zurück reicht die Kurve.", new[] { "1 Minute", "5 Minuten", "15 Minuten" },
                () => Math.Max(0, Array.IndexOf(MixerSettings.LoudHistMinuteValues, LoudMinutes)), i => SetLoudMinutes(MixerSettings.LoudHistMinuteValues[i])).Enabled = () => LoudHistOn;
            p.Toggle("Oszilloskop in der Meterbridge", "Die Wellenform live, wie am Messgerät – gleicher Ausgang wie das Stereo-Bild. Steht in der freien Lücke, wenn Platz ist.",
                () => !_settings.OscOff, v => SetOscOn(v)).Enabled = () => !_settings.BridgeOff;
            p.Toggle("„Jetzt läuft“ mit Cover in der Meterbridge", "Album-Cover, Titel und Interpret von dem, was gerade läuft (Spotify, YouTube …). Klick darauf: zuletzt gespielte Songs.",
                () => !_settings.CoverOff, v => SetCoverOn(v)).Enabled = () => !_settings.BridgeOff;
            AddBridge330(p);   // 3.3: Schallplatte/Bandmaschine, Oszilloskop-Look, Ducking sichtbar
            AddBridge340(p);   // 3.4: PC-Auslastung, Papierschreiber
            p.Choice("Spitzen im Spektrum", () => _settings.SpecPeak == 0 ? "Keine Spitzen – nur die Balken." : _settings.SpecPeak == 2 ? "Die hellen Spitzen bleiben 2,5 Sekunden stehen." : "Die hellen Spitzen bleiben kurz stehen (Standard).",
                new[] { "Aus", "Kurz", "Lang" }, () => _settings.SpecPeak ?? 1, i => SetSpecPeak(i));
            p.Toggle("Frequenz unter der Maus", "Maus übers Spektrum in der Meterbridge: zeigt Frequenz und Pegel an dieser Stelle (z. B. „1,2 kHz · −24 dB“).",
                () => !_settings.SpecHoverOff, v => SetSpecHover(v));
            p.Button("Großes Spektrum", "Das Spektrum allein in einem eigenen Fenster – auch ohne Meterbridge, frei vergrößerbar, auf Wunsch immer im Vordergrund.",
                "Öffnen", r => ShowSpectrumWindow(), () => VuWindowOpen && _vuWin.ShowSpectrum ? "ist offen" : null);
            p.Toggle("Uhr", "Uhrzeit mit Laufzeit seit dem Start – läuft der Sleep-Timer, zeigt die Uhr oben die Restzeit. Klick auf die Uhr: Sleep-Timer.",
                () => !_settings.ClockOff, v => SetClockOn(v));
            p.Choice("Art der Uhr", () => _settings.ClockStyle == 0
                    ? "Rote LED-Uhr wie im Tonstudio, mit Sekundenkranz."
                    : _settings.ClockStyle == 2 ? "Analoge Studio-Uhr mit Zeigern und rotem Sekundenzeiger – Farben wie das VU-Meter."
                    : _settings.ClockStyle == 3 ? "Fallblatt-Uhr wie früher am Bahnhof: weiße Ziffern auf schwarzen Klappkarten."
                    : _settings.ClockStyle == 4 ? "Nixie-Röhren: orange glühende Ziffern hinter Glas, wie in alten Messgeräten."
                    : _settings.ClockStyle == 5 ? "Rollzählwerk: Ziffernrollen wie am Bandzähler – jede Sekunde rollt die letzte Rolle weiter."
                    : _settings.ClockStyle == 6 ? "Wortuhr: „ES IST HALB ZEHN“ in leuchtenden Buchstaben, auf 5 Minuten genau – die Minuten dazwischen sind Punkte in den Ecken."
                    : _settings.ClockStyle == 8 ? "Oszilloskop-Uhr: Vektor-Uhr auf einem runden Röhrenschirm, grüner Leuchtstrahl mit Nachleuchten."
                    : _settings.ClockStyle == 7 ? "Binäruhr: Stunden, Minuten und Sekunden als leuchtende Punkte (Werte 8, 4, 2, 1 von oben nach unten)."
                    : "Uhr im LCD-Look (passend zu den Anzeigen), in der Meterbridge mit Sekunden und Datum.",
                new[] { "Studio-Uhr", "LCD", "Analog", "Fallblatt", "Nixie-Röhren", "Rollzählwerk", "Wortuhr", "Binäruhr", "Oszilloskop" }, () => _settings.ClockStyle, i => SetClockStyle(i)).Enabled = () => !_settings.ClockOff;
            p.Toggle("Uhr in der Meterbridge", "Groß in der freien Mitte der Meterbridge, wenn Platz ist – sonst oben in der Kopfleiste. Aus = immer oben.",
                () => !_settings.BridgeClockOff, v => SetBridgeClock(v)).Enabled = () => !_settings.ClockOff && !_settings.BridgeOff;
            p.Toggle("Schmale Kanäle", "Mehr Kanäle passen nebeneinander.", () => _settings.Narrow, v => SetNarrow(v));
            AddChannelWidth320(p);   // 3.2: Breite pro Kanal
            AddDragWidth345(p);      // 3.4.5: mit der Maus breiter ziehen
            p.Choice("Anzeigefeld der Kanäle", () => ChannelField.Hints[ChannelStrip.GlobalField] + " Pro Kanal anders: Rechtsklick auf den Kanal → Anzeigefeld.",
                ChannelField.Short, () => ChannelStrip.GlobalField, i => SetGlobalField(i));
            p.Toggle("Kompakt-Ansicht", "Nur Name, Tasten, SIG/PEAK, Fader und Pegel – ohne Apps, EQ-Knöpfe, Balance und VU-Meter. Passt auch auf kleine Bildschirme, die Fader werden länger. Einstellen geht weiter über Rechtsklick und OPTIONEN.",
                () => _settings.Compact, v => SetCompact(v));
            p.Slider("Grauer Pegel (was eigentlich läuft)", "Hinter dem farbigen Pegel: so laut wäre es ohne Fader und Stumm. 0 = aus.",
                0, 100, 1, () => _settings.GhostPct ?? Theme.GhostDefault, (v, fin) => SetGhost((int)v, fin),
                v => v <= 0 ? "aus" : (int)v + " %" + ((int)v == Theme.GhostDefault ? " (Standard)" : ""));
            p.Toggle("Mikrofon-Kanal anzeigen", "Der MIKRO-Zug mit Lautstärke, Stumm und Pegel.", () => !_settings.HideMic, v => SetMicVisible(v));
            p.Toggle("Mikrofon bei den Kanälen", "Aus = der MIKRO-Zug steht rechts bei den Ausgängen.", () => !_settings.MicWithOutputs, v => SetMicWithOutputs(!v))
                .Enabled = () => !_settings.HideMic;
            p.Button("Ausgeblendete Kanäle", "Ausgeblendete Kanäle laufen weiter – hier wieder einblenden.", "Anzeigen ▾", r =>
            {
                var m = NewMenu();
                var hidden = _settings.Channels.Where(c => c.Hidden).ToList();
                if (hidden.Count == 0) m.Items.Add(Hint("Keine – Rechtsklick auf einen Kanal → Ausblenden"));
                foreach (var c in hidden)
                {
                    var ch = c;
                    m.Items.Add(new ToolStripMenuItem(c.Name + " wieder anzeigen", Swatch(Color.FromArgb(c.Color)), (s, e) => SetChannelHidden(ch, false)));
                }
                ShowMenuAt(m, r);
            }, () => { int n = _settings.Channels.Count(c => c.Hidden); return n == 0 ? "keine" : n + " ausgeblendet"; });

            AddLook320(p);         // 3.2: Schrift, Zoom, durchsichtig
            AddTimeDesign320(p);   // 3.2: Design nach Uhrzeit
            p.Section("Material");
            p.Choice("Oberfläche der Kanalzüge", () => _settings.PanelMaterial == 0 ? "Wie das Design es vorsieht." : _settings.PanelMaterial == 1 ? "Carbon-Gewebe mit Klarlack." : _settings.PanelMaterial == 2 ? "Gebürstetes Aluminium." : _settings.PanelMaterial == 3 ? "Holz-Furnier mit Maserung." : "Genarbtes Leder mit Ziernaht.",
                Theme.MaterialNames, () => _settings.PanelMaterial, i => SetPanelMaterial(i));
            p.Choice("Seitenteile", () => _settings.CheekStyle == 0 ? "Passend: bei Vintage und Retro-Hi-Fi Holz, sonst keine." : _settings.CheekStyle == 1 ? "Keine Seitenteile." : "Links und rechts am Pult – " + Theme.CheekNames[_settings.CheekStyle] + ".",
                Theme.CheekNames, () => _settings.CheekStyle, i => SetCheekStyle(i));
            p.Choice("Knöpfe", () => _settings.KnobStyle == 0 ? "Wie das Design es vorsieht." : "Alle Drehknöpfe: " + Theme.KnobStyleNames[_settings.KnobStyle] + ".",
                Theme.KnobStyleNames, () => _settings.KnobStyle, i => SetKnobStyle(i));
            p.Choice("Faderkappen", () => _settings.CapStyle == 0 ? "Wie das Design es vorsieht." : "Kappen der Kanäle: " + Theme.CapStyleNames[_settings.CapStyle] + " – die MASTER-Kappe bleibt rot.",
                Theme.CapStyleNames, () => _settings.CapStyle, i => SetCapStyle(i));
            AddCapShape310(p);   // 3.1: Kappen-Form
            AddIconColor310(p);  // 3.1: Kanalfarbe aus dem App-Symbol
            AddSeason310(p);     // 3.1: Saison-Deko
            AddDesign310bSettings(p);   // 3.1: Lieblingsdesign, Zufalls-Design
            p.Toggle("Bilder in den Kanälen", "Eigenes Bild pro Kanal hinter den App-Symbolen, z. B. ein Spiel-Cover. Bild wählen: Rechtsklick auf den Kanal → Bild im Kanal.",
                () => !_settings.ChannelImagesOff, v => SetChannelImagesOn(v));
            p.Choice("Namensschilder", () => _settings.DymoPlates ? "Prägeband wie vom Beschriftungsgerät: glänzendes Kunststoffband in der Kanalfarbe, weiße erhabene Buchstaben."
                    : _settings.TapePlates ? "Kreppband mit Handschrift – wie im Studio, wo die Kanäle mit Klebeband beschriftet werden; rechts ein Streifen Kanalfarbe."
                    : _settings.DigitalPlates ? "Beleuchtete LCD-Schilder wie am Digitalpult – der Name leuchtet in der Kanalfarbe." : "Farbige Schilder in der Kanalfarbe.",
                Theme.PlateStyleNames, () => PlateStyle, i => SetPlateStyle(i));
            p.Section("Licht");
            p.Toggle("Leuchtende Fader-Spur", "Der Faderschlitz leuchtet in der Kanalfarbe und wird mit dem Pegel heller.",
                () => !_settings.FaderGlowOff, v => SetFaderGlow(v));
            p.Toggle("Ambilight am Pultrand", "Der Rand ums Pult leuchtet in der Farbe des lautesten Kanals und pulsiert mit der Musik.",
                () => !_settings.AmbilightOff, v => SetAmbilight(v));
            p.Choice("Ruhemodus", () => (_settings.IdleMinutes ?? 10) == 0 ? "Aus." : "Wird das Pult " + (_settings.IdleMinutes ?? 10) + " Minuten nicht bedient, wird es dunkel – Pegel, LEDs und Uhr leuchten weiter. Maus bewegen = sofort wieder hell.",
                new[] { "Aus", "2 Min", "5 Min", "10 Min", "30 Min" }, () => Math.Max(0, Array.IndexOf(IdleMinutes, _settings.IdleMinutes ?? 10)), i => SetIdleMinutes(IdleMinutes[i]));
            p.Section("Hintergrund");
            p.Button("Eigenes Hintergrundbild", "Ein Bild deiner Wahl hinter dem Pult (z. B. eine Studio-Wand) – abgedunkelt, damit alles lesbar bleibt.",
                "Wählen …", r => ChooseBgImage(), () => string.IsNullOrEmpty(_settings.BgImagePath) ? "keins" : System.IO.Path.GetFileName(_settings.BgImagePath));
            p.Button("Hintergrundbild entfernen", null, "Entfernen", r => ClearBgImage()).Visible = () => !string.IsNullOrEmpty(_settings.BgImagePath);
            p.Slider("Bild abdunkeln", "Weiter rechts = dunkler, die Schrift bleibt besser lesbar.", 0, 90, 5, () => _settings.BgDim ?? 55, (v, fin) => SetBgDim((int)v, fin),
                v => (int)v + " %" + ((int)v == 55 ? " (Standard)" : "")).Visible = () => !string.IsNullOrEmpty(_settings.BgImagePath);
            p.Section("Pultlampe");
            p.Choice("Pultlampe", () => LampMode == 0 ? "Keine Lampe." : LampMode == 1 ? "Eine kleine Schwanenhals-Lampe über den Kanälen wirft einen warmen Lichtkegel aufs Pult."
                    : "Die Lampe hängt immer da und geht an, sobald das Pult dunkler ist (abends).",
                Theme.LampModeNames, () => LampMode, i => SetLampMode(i));
            p.Choice("Lampe hängt", () => "Wo über den Kanälen.", Theme.LampPosNames, () => _settings.LampPos ?? 1, i => SetLampPos(i)).Enabled = () => LampMode != 0;
            p.Choice("Licht", () => "Wie hell der Lichtkegel ist.", Theme.LampStrengthNames, () => _settings.LampStrength ?? 1, i => SetLampStrength(i)).Enabled = () => LampMode != 0;
            p.Section("Abends dunkler");
            p.Choice("Pult dunkler", () => _settings.PultDimMode == 0 ? "Flächen, Schilder und Schrift werden dunkler – Pegel, LEDs, gedrückte Tasten und Anzeigen bleiben hell."
                    : _settings.PultDimMode == 1 ? "Immer dunkler." : _settings.PultDimMode == 2 ? "Abends dunkler, tagsüber normal." : "Dunkler, solange NACHT (Nachtmodus) an ist.",
                PultDimModes, () => _settings.PultDimMode, i => SetPultDimMode(i));
            p.Slider("So hell", "Weiter links = dunkler.", MixerSettings.PultBrightMin, MixerSettings.PultBrightMax, 5,
                () => _settings.PultBright ?? MixerSettings.PultBrightDefault, (v, fin) => SetPultBright((int)v, fin),
                v => (int)v + " %" + ((int)v == MixerSettings.PultBrightDefault ? " (Standard)" : "")).Enabled = () => _settings.PultDimMode != 0;
            p.Choice("Abends ab", () => "Ab dieser Uhrzeit wird das Pult dunkler …", Array.ConvertAll(DimFromHours, h => h + " Uhr"),
                () => Array.IndexOf(DimFromHours, _settings.PultDimFrom ?? 20), i => { _settings.PultDimFrom = DimFromHours[i]; UpdatePultDim(true); ScheduleSave(); })
                .Enabled = () => _settings.PultDimMode == 2;
            p.Choice("Morgens bis", () => "… und ab dieser Uhrzeit wieder normal hell.", Array.ConvertAll(DimToHours, h => h + " Uhr"),
                () => Array.IndexOf(DimToHours, _settings.PultDimTo ?? 7), i => { _settings.PultDimTo = DimToHours[i]; UpdatePultDim(true); ScheduleSave(); })
                .Enabled = () => _settings.PultDimMode == 2;
            AddLook330(p);          // 3.3: Licht, Spuren, Wand, Hintergrund, Hinweise
            AddLook340(p);          // 3.4: Boxen, Licht nach Uhrzeit, Haube, Typenschild, Aufkleber
            AddLook346(p);          // 3.4.6: Ausgaenge ziehen, Deko, Kabel, Roehrenbild
            AddLook350(p);          // 3.5: Roehren, Lauflicht, Wackeln, Spektrum, Geraete-Bild
            AddLook400(p);          // 4.0: Windows hell/dunkel, Radar, Hintergruende, Drehknoepfe, Nixie, GIF, Motor, Deko, Klicks
            AddLookFinal400(p);     // 4.0 finale: Lichtshow, Neon, Himmel, Umblenden, Song-Fortschritt, Wallpaper, Deko, Motor-Potis, Leuchtspur
            AddLook420(p);          // 4.2: Tisch, RGB-Welle, Fensterrand, Startbild, App-Symbol
            AddLook430(p);          // 4.3: Hinweis auf die neuen Designs, Hintergruende, Waende und den Plattenspieler
            AddAufbau400(p);        // 4.0: Kanalzug anpassen, zwei Reihen
            AddLookSettings20(p);   // 2.20: Design-Editor, 3D
            AddViewSettings(p);   // 2.18
            pages.Add(p);

            // ------------------------------------------------------------ Leuchten & Anzeigen
            p = new SPage("Leuchten & Anzeigen", "", "Was am Pult leuchtet, blinkt und anzeigt.");
            p.Section("SIG und PEAK");
            p.Toggle("SIG-Leuchte (grün)", "Hier kommt Ton an – auch wenn der Kanal stumm oder leise ist.",
                () => !_settings.SignalLedOff, v => SetLeds(v, !_settings.PeakLedOff));
            p.Slider("SIG leuchtet ab", "Weiter links = schon bei sehr leisem Ton.", MixerSettings.SigLedMin, MixerSettings.SigLedMax, 1,
                () => _settings.SigLedDb ?? MixerSettings.SigLedDefault,
                (v, fin) => { _settings.SigLedDb = v; StripBase.SetLedThresholds(v, _settings.PeakLedDb ?? MixerSettings.PeakLedDefault); if (fin) ScheduleSave(); },
                v => StripBase.DbText(v) + ((int)v == (int)MixerSettings.SigLedDefault ? " (Standard)" : "")).Enabled = () => !_settings.SignalLedOff;
            p.Toggle("PEAK-Leuchte (rot)", "So laut, dass es gleich übersteuert. Leuchtet es oft: Fader etwas zurücknehmen.",
                () => !_settings.PeakLedOff, v => SetLeds(!_settings.SignalLedOff, v));
            p.Slider("PEAK leuchtet ab", "Weiter links = warnt früher, ganz rechts = erst kurz vor dem Übersteuern.", MixerSettings.PeakLedMin, MixerSettings.PeakLedMax, 1,
                () => _settings.PeakLedDb ?? MixerSettings.PeakLedDefault,
                (v, fin) => { _settings.PeakLedDb = v; StripBase.SetLedThresholds(_settings.SigLedDb ?? MixerSettings.SigLedDefault, v); if (fin) ScheduleSave(); },
                v => StripBase.DbText(v) + ((int)v == (int)MixerSettings.PeakLedDefault ? " (Standard)" : "")).Enabled = () => !_settings.PeakLedOff;
            p.Toggle("PEAK zählt mit", "Aus (Standard): PEAK leuchtet nur. An: auf der Leuchte steht, wie oft es zu laut war, z. B. „PEAK 3“ – Klick auf PEAK im Kanal = 0.",
                () => _settings.PeakCountOn, v => { _settings.PeakCountOn = v; StripBase.PeakCountOn = v; foreach (var x in AllStrips()) x.ResetPeakCount(); RelayoutAllStrips(); ScheduleSave(); })
                .Enabled = () => !_settings.PeakLedOff;
            var pcAll = p.Button("PEAK-Zähler aller Kanäle", null, "Auf 0 setzen", r => { foreach (var x in AllStrips()) x.ResetPeakCount(); },
                () => { int n = AllStrips().Sum(x => x.PeakCount); return n == 0 ? "alle auf 0" : "zusammen " + n + "×"; });
            pcAll.Enabled = () => !_settings.PeakLedOff && AllStrips().Any(x => x.PeakCount > 0);
            pcAll.Visible = () => _settings.PeakCountOn;
            p.Section("Pegel");
            AddLedBright310(p);   // 3.1: LED-Helligkeit
            AddLowLatency311(p);  // 3.1.1: Pegel ohne Verzoegerung
            p.Choice("Pegel zeigt", () => _settings.MeterMode == 0 ? "Spitze: jeder Ausschlag sofort – zappelig, zeigt, ob es übersteuert."
                    : _settings.MeterMode == 1 ? "Mittelwert: ruhiger, zeigt eher, wie laut es klingt (die Spitze bleibt im dB-Wert)."
                    : "Beides: der Mittelwert leuchtet hell, die Spitze darüber halb hell – wie an Studio-Pegeln.",
                MeterBallistics.ModeNames, () => _settings.MeterMode, i => SetMeterMode(i));
            p.Choice("Rücklauf", () => (_settings.MeterFall ?? 1) == 0 ? "Pegel fallen schnell zurück." : (_settings.MeterFall ?? 1) == 1 ? "Normal (Standard)."
                    : (_settings.MeterFall ?? 1) == 3 ? "Sofort: die Pegel folgen dem Ton fast ohne Nachlauf – tanzen im Takt mit." : "Pegel sinken langsam – ruhiger zum Ablesen.",
                new[] { "Schnell", "Normal", "Langsam", "Sofort" }, () => _settings.MeterFall ?? 1, i => SetMeterFall(i));
            p.Choice("Spitze bleibt stehen", () => (_settings.MeterHold ?? 2) == 0 ? "Kein stehender Strich über dem Pegel."
                    : (_settings.MeterHold ?? 2) == 4 ? "Der oberste Strich bleibt, bis du auf den Pegel klickst – so siehst du hinterher, wie laut es war."
                    : "So lange bleibt der oberste Strich stehen: " + ((_settings.MeterHold ?? 2) == 1 ? "kurz (0,6 s)." : (_settings.MeterHold ?? 2) == 2 ? "normal (1,3 s, Standard)." : "lang (4 s)."),
                new[] { "Aus", "Kurz", "Normal", "Lang", "Bis Klick" }, () => _settings.MeterHold ?? 2, i => SetMeterHold(i));
            p.Toggle("Pegel in Echtzeit", "Die Pegel werden alle 10 ms gemessen und weitergegeben (vorher etwa alle 30 ms) – sie zeigen den Ton ohne spürbare Verzögerung. Kostet kaum Rechenzeit. Aus = wie bis 2.15.",
                () => !_settings.MeterRealtimeOff, v => SetMeterRealtime(v));
            p.Choice("Pegel an den Ton anpassen", () => _settings.MeterDelay == 0
                    ? "Aus (Standard): die Pegel zeigen sofort, was gemessen wird. Kopfhörer per Funk oder Bluetooth spielen den Ton später ab – dann laufen die Pegel voraus; hier lässt sich das ausgleichen."
                    : "Pegel, VU-Meter und Spektrum zeigen den Ton " + MixerSettings.MeterDelayValues[_settings.MeterDelay] + " ms später – passend zu Funk-Kopfhörern (ca. 40–80 ms) oder Bluetooth (ca. 150–250 ms). Das Mikro bleibt in Echtzeit.",
                MeterDelayNames, () => _settings.MeterDelay, i => SetMeterDelay(i));
            // MASTER und Ausgaenge: eigene Einstellung (Index 0 = wie die Kanaele)
            p.Choice("MASTER zeigt", () => _settings.MeterOutMode == null ? "Pegel im MASTER und an den Ausgängen: wie bei den Kanälen (Standard). Auch per Rechtsklick auf den MASTER-Pegel."
                    : "Pegel im MASTER und an den Ausgängen: " + MeterModeText(_settings.MeterOutMode.Value) + ".",
                new[] { "Wie Kanäle", "Spitze", "Mittelwert", "Beides" }, () => (_settings.MeterOutMode ?? -1) + 1, i => SetMeterOutMode(i == 0 ? (int?)null : i - 1));
            p.Choice("MASTER Rücklauf", () => _settings.MeterOutFall == null ? "Wie bei den Kanälen (Standard)."
                    : _settings.MeterOutFall == 0 ? "MASTER und Ausgänge fallen schnell zurück." : _settings.MeterOutFall == 1 ? "MASTER und Ausgänge: normal."
                    : _settings.MeterOutFall == 3 ? "MASTER und Ausgänge folgen dem Ton sofort." : "MASTER und Ausgänge sinken langsam – ruhiger zum Ablesen.",
                new[] { "Wie Kanäle", "Schnell", "Normal", "Langsam", "Sofort" }, () => (_settings.MeterOutFall ?? -1) + 1, i => SetMeterOutFall(i == 0 ? (int?)null : i - 1));
            p.Choice("MASTER Spitze bleibt stehen", () => _settings.MeterOutHold == null ? "Wie bei den Kanälen (Standard)."
                    : _settings.MeterOutHold == 0 ? "Kein stehender Strich über dem MASTER-Pegel."
                    : _settings.MeterOutHold == 4 ? "Der oberste Strich bleibt, bis du auf den MASTER-Pegel klickst."
                    : "So lange bleibt der oberste Strich stehen: " + (_settings.MeterOutHold == 1 ? "kurz (0,6 s)." : _settings.MeterOutHold == 2 ? "normal (1,3 s)." : "lang (4 s)."),
                new[] { "Wie Kanäle", "Aus", "Kurz", "Normal", "Lang", "Bis Klick" }, () => (_settings.MeterOutHold ?? -1) + 1, i => SetMeterOutHold(i == 0 ? (int?)null : i - 1));
            p.Toggle("Lautheit (LUFS) im MASTER", "Wie laut es sich anfühlt – wie bei Radio, YouTube und Spotify (um −14 LUFS). Steht im MASTER statt „SPIELT GERADE“. Klick auf die Anzeige: Momentan, Kurzzeit oder Integriert.",
                () => !_settings.LufsOff, v => SetLufsOn(v));
            p.Toggle("Lautheit in jedem Ausgang", "Im Modus „Mehrere Ausgänge“ zeigt jeder Ausgang seine eigene Lautheit – jeder misst sein Gerät. Aus: nur MASTER und der Standard-Ausgang (wie bisher).",
                () => !_settings.LufsEachOff, v => SetLufsEach(v)).Enabled = () => !_settings.LufsOff;
            p.Choice("Lautheit zeigt", () => OutputStrip.LufsMode == 0 ? "Momentan: die letzten 0,4 Sekunden." : OutputStrip.LufsMode == 1 ? "Kurzzeit: die letzten 3 Sekunden (Standard)." : "Integriert: Durchschnitt seit dem Zurücksetzen (Klick auf die Anzeige → neu beginnen).",
                new[] { "Momentan", "Kurzzeit", "Integriert" }, () => OutputStrip.LufsMode, i => SetLufsMode(i)).Enabled = () => !_settings.LufsOff;
            p.Choice("LED-Form", () => _settings.LedShape == 0 ? "Eckige Segmente (Standard)." : _settings.LedShape == 1 ? "Runde LEDs." : "Feine Striche.",
                Theme.LedShapeNames, () => _settings.LedShape, i => SetLedShape(i));
            p.Choice("LED-Größe", () => (_settings.LedSize ?? 1) == 0 ? "Viele kleine LEDs." : (_settings.LedSize ?? 1) == 1 ? "Normal (Standard)." : "Weniger, größere LEDs – wie an alten Geräten.",
                Theme.LedSizeNames, () => _settings.LedSize ?? 1, i => SetLedSize(i));
            p.Toggle("LEDs glühen nach", "Eben ausgegangene LEDs glimmen kurz nach, wie echte Glühlampen – wirkt ruhiger.",
                () => !_settings.LedAfterglowOff, v => SetAfterglow(v));
            p.Toggle("Lichtschein um LEDs", "Leuchtende Pegel werfen einen weichen Schein auf die Umgebung – besonders schön abends.",
                () => !_settings.LedHaloOff, v => SetHalo(v));
            p.Toggle("EQ-Kurve im Kanal", "Im PRESET-Feld jedes Kanals steht eine kleine Kurve: so ist der EQ gerade eingestellt (Bass links, Höhen rechts). Maus darüber zeigt wieder „PRESET“.",
                () => !_settings.EqCurveOff, v => SetEqCurveOn(v));
            p.Toggle("Absenkung (GR) neben dem Pegel", "Orange LEDs: so viel dB machen COMP, AUTO, Ducking, „Spiel im Hintergrund leiser“ und Sleep-Timer den Kanal gerade leiser.",
                () => !_settings.GrOff, v => { _settings.GrOff = !v; StripBase.GrOn = v; RelayoutAllStrips(); ScheduleSave(); });
            p.Toggle("Kanäle leuchten im Takt", "Das Namensschild glüht in der Kanalfarbe mit – je lauter, desto heller.",
                () => !_settings.GlowOff, v => { _settings.GlowOff = !v; StripBase.GlowOn = v; RefreshStrips(); ScheduleSave(); });
            p.Toggle("dB-Anzeige an den Pegeln", "Spitzenwert in dB über jedem Pegel (rot bei Übersteuerung) und dB-Skala daneben.",
                () => !_settings.PeakDbOff, v => { _settings.PeakDbOff = !v; StripBase.PeakDb = v; RefreshStrips(); ScheduleSave(); });
            p.Toggle("MUTE blinkt", "Ist etwas stumm geschaltet, blinkt seine MUTE-Taste – egal, ob gerade Ton ankommt. Aus: MUTE leuchtet dauerhaft.",
                () => !_settings.MuteBlinkOff, v => { _settings.MuteBlinkOff = !v; StripBase.MuteBlink = v; RefreshStrips(); ScheduleSave(); });
            p.Section("VU-Meter");
            p.Toggle("VU-Meter mit Zeiger am Ausgang", "Analoges Zeigerinstrument im MASTER-Zug.",
                () => !_settings.VuOff, v => { _settings.VuOff = !v; StripBase.VuOn = v; RelayoutAllStrips(); ScheduleSave(); });
            var vuStart = p.Slider("Zeiger reagiert ab", null, MixerSettings.VuStartMin, MixerSettings.VuStartMax, 1,
                () => StripBase.VuStartDb, (v, fin) => SetVuStart(v, fin),
                v => StripBase.DbText(v) + ((int)Math.Round(v) == (int)MixerSettings.VuStartDefault ? " (Standard)" : ""));
            vuStart.HintFunc = () => (Theme.VuDbScale
                ? "Linkes Ende der Skala (rechts ist immer 0 dB, rot ab " + StripBase.DbText(Theme.VuRedDb) + "). Weiter links = auch leiser Ton bewegt den Zeiger."
                : "Empfindlichkeit: weiter links = auch leiser Ton bewegt den Zeiger. Gerade: „20“ auf der Skala = "
                    + StripBase.DbText(StripBase.VuStartDb) + ", „0“ = " + StripBase.DbText(StripBase.VuStartDb + 20f) + ".").Replace(" dB", "\u00A0dB");
            vuStart.Enabled = () => !_settings.VuOff;
            p.Button("An meinen Ton anpassen", "Spiel etwas ab, das du normal hörst, und klick: der Zeiger steht danach etwa in der Mitte.", "Jetzt anpassen",
                r => CalibrateVu(out _), () => _vuCalNote)
                .Enabled = () => !_settings.VuOff;
            p.Choice("Zahlen auf der Skala", () => Theme.VuDbScale
                    ? "Echte Pegel von „Zeiger reagiert ab“ bis 0 dB, gerade " + StripBase.DbText(StripBase.VuStartDb).Replace(" dB", "") + "\u00A0…\u00A00 dB."
                    : "Klassisch wie am Studio-VU-Meter: 20 … 0 … +3 (nur 23 dB breit – sehr empfindlich eingestellt schlägt er schnell an).",
                new[] { "dB", "VU (klassisch)" }, () => Theme.VuDbScale ? 0 : 1, i => SetVuScale(i == 0))
                .Enabled = () => !_settings.VuOff;
            p.Choice("VU-Licht", () => _settings.VuLight == 0 ? "Warmes Licht hinter der Skala (Standard)." : _settings.VuLight == 1 ? "Blau – wie bei alten Hi-Fi-Verstärkern." : _settings.VuLight == 2 ? "Grün." : "Kühles Weiß.",
                Theme.VuLightNames, () => _settings.VuLight, i => SetVuLight(i)).Enabled = () => !_settings.VuOff;
            p.Choice("Farbe der Skala", () => Theme.VuFace == 0 ? "Passt sich dem Design an (Studio/Klassisch: Creme, Modern/Neon: dunkel, Hell: weiß)." :
                    Theme.VuFace == 2 ? "Schwarze Skala mit heller Schrift und hellem Zeiger." : Theme.VuFace == 1 ? "Warm beleuchtete Creme-Skala wie bei alten Studio-Geräten." : "Weiße Skala mit dunkler Schrift.",
                new[] { "Passend", "Creme", "Schwarz", "Weiß" }, () => Theme.VuFace,
                i => { _settings.VuFace = i; Theme.VuFace = i; RefreshStrips(); ScheduleSave(); })
                .Enabled = () => !_settings.VuOff;
            p.Choice("Zeiger geht zurück", () => (_settings.VuFall ?? 1) == 0 ? "Wie ein echtes VU-Meter (0,3 s)." : (_settings.VuFall ?? 1) == 1 ? "Steigt flott, fällt gemächlich (Standard)." : "Bleibt lange oben – gut zum Ablesen.",
                new[] { "schnell", "langsam", "sehr langsam" }, () => _settings.VuFall ?? 1, i => { _settings.VuFall = i; StripBase.VuFall = i; ScheduleSave(); })
                .Enabled = () => !_settings.VuOff;
            p.Button("Großes VU-Meter", "Zwei große Zeiger-Instrumente (links/rechts) in einem eigenen Fenster – frei vergrößerbar, auf Wunsch immer im Vordergrund und mit Spektrum. Rechtsklick ins Fenster: welcher Ausgang.",
                "Öffnen", r => ShowVuWindow(), () => VuWindowOpen && _vuWin.ShowVu ? "ist offen" : null);
            p.Toggle("Schleppzeiger", "Ein dünner roter Zeiger bleibt kurz am lautesten Punkt stehen und sinkt dann langsam zurück.",
                () => !_settings.VuPeakNeedleOff, v => { _settings.VuPeakNeedleOff = !v; StripBase.VuPeakNeedle = v; RefreshVuStrips(); ScheduleSave(); })
                .Enabled = () => !_settings.VuOff;
            p.Toggle("PEAK-Leuchte im VU-Meter", () => "Kleine rote Leuchte unten rechts im Instrument – geht an ab " + StripBase.DbText(_settings.PeakLedDb ?? MixerSettings.PeakLedDefault) + " (wie „PEAK leuchtet ab“).",
                () => !_settings.VuPeakLedOff, v => { _settings.VuPeakLedOff = !v; StripBase.VuPeakLed = v; RefreshVuStrips(); ScheduleSave(); })
                .Enabled = () => !_settings.VuOff;
            AddVu310Settings(p);   // 3.1: Doppelzeiger
            AddVu330(p);           // 3.3: BBC, DIN, Magisches Auge
            AddKeys310Settings(p); // 3.1: Tasten-Form und -Farben
            p.Section("Kanäle hervorheben");
            AddStrip310bSettings(p);   // 3.1: stumm abdunkeln, SOLO, Rand blinkt
            p.Section("Sonstiges");
            p.Toggle("Kanalnummern", "1, 2, 3 … im Namensschild – wie am echten Pult.",
                () => !_settings.NumbersOff, v => { _settings.NumbersOff = !v; StripBase.ShowNumbers = v; RefreshStrips(); ScheduleSave(); });
            p.Toggle("Licht über dem Kanal unter der Maus", "Der Kanal, über dem die Maus steht, wird leicht angestrahlt.",
                () => !_settings.HoverLightOff, v => { _settings.HoverLightOff = !v; StripBase.HoverLight = v; RefreshStrips(); ScheduleSave(); });
            p.Toggle("Selbsttest beim Start", "Beim Start laufen alle Pegel und Leuchten einmal durch, die Fader fahren an ihre Stelle.",
                () => !_settings.SelfTestOff, v => { _settings.SelfTestOff = !v; StripBase.SelfTestOn = v; ScheduleSave(); });
            AddBigShow310(p);   // 3.1: grosse Einschalt-Show
            p.Button("Selbsttest ansehen", null, "Jetzt zeigen", r => StripBase.StartSelfTest(true));
            p.Toggle("Einschalt-Gruß", "Beim Start läuft „MISCHPULT " + Program.Version + " · MADE BY " + Program.Author.ToUpperInvariant() + "“ als Laufschrift durch die Anzeigen aller Kanäle.",
                () => !_settings.GreetingOff, v => { _settings.GreetingOff = !v; StripBase.GreetingOn = v; ScheduleSave(); });
            p.Button("Einschalt-Gruß ansehen", null, "Jetzt zeigen", r => ShowGreeting());
            AddTicker310(p);   // 3.1: eigene Laufschrift
            AddCountdown320(p);   // 3.2: Countdown
            AddOnAir330(p);       // 3.3: ON-AIR-Lampe
            p.Toggle("Motorfader", "Beim Laden einer Szene fahren die Fader sichtbar an ihre neue Stelle, statt zu springen – wie am Digitalpult.",
                () => !_settings.MotorOff, v => { _settings.MotorOff = !v; StripBase.MotorFaders = v; ScheduleSave(); });
            AddMotorSpeed320(p);   // 3.2: Tempo der Motorfader
            p.Toggle("„Jetzt läuft“-Laufschrift", "Titel und Interpret (Spotify, YouTube im Browser, Media Player …) als LCD-Laufschrift oben neben den Musik-Tasten. Klick darauf: Play / Pause.",
                () => !_settings.NowPlayingOff, v => SetNowPlayingOn(v));
            p.Toggle("Song-Verlauf", "Merkt sich die zuletzt gespielten Songs (die mindestens 10 Sekunden liefen) mit Uhrzeit – bleibt nur auf diesem PC.",
                () => !_settings.SongsOff, v => SetSongsOn(v));
            p.Button("Zuletzt gespielt", "Liste der letzten Songs – Klick auf einen Titel kopiert ihn. Auch per Klick aufs Cover in der Meterbridge.",
                "Anzeigen ▾", r =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    AddSongItems(m.Items);
                    ShowMenuAt(m, r);
                }, () => { int n = _settings.Songs?.Count ?? 0; return n == 0 ? "leer" : n + (n == 1 ? " Song" : " Songs"); });
            p.Toggle("Tasten klicken", "Leises mechanisches Klicken beim Drücken von MUTE, SOLO, EQ & Co. – nur auf deinem Ausgang, nicht im Stream-Mix und nicht in Discord; während einer Aufnahme still.",
                () => !_settings.KeyClickOff, v => SetKeyClick(v));
            p.Choice("Klicken", () => "Lautstärke des Klickens.", new[] { "Leise", "Mittel", "Laut" }, () => _settings.KeyClickLevel ?? 0, i => SetKeyClickLevel(i))
                .Enabled = () => !_settings.KeyClickOff;
            AddRgbSettings20(p);   // 2.20: OpenRGB
            AddSaverSettings(p);   // 2.18
            AddMeters346(p);       // 3.4.6: True-Peak, K-Skala, Dynamik, Ziel, Balance, Reserve, Lupe
            AddBpm346(p);          // 3.4.6: Takt-Lampe
            AddMeters350(p);       // 3.5: Phase, Uebersteuerungen, Verzoegerung, Format, Hoerzeit, Statistik, Kein Ton
            pages.Add(p);

            // ------------------------------------------------------------ Ton
            p = new SPage("Ton", "", "Nachtmodus, Loudness, DIM, FADE, Gehörschutz, Stummschalten und Sleep-Timer.");
            p.Toggle("Nachtmodus", "Kompressor auf allen Kanälen: Leises wird lauter, Lautes leiser (auch Taste NACHT oben).",
                () => _settings.NightMode, v => { if (v != _settings.NightMode) ToggleNight(false); });
            p.Choice("Gehörschutz", () => "Fängt plötzlich laute Geräusche ab (Explosionen, Schreie im Voice-Chat)." +
                    (_settings.HearingCap < 0 ? " Höchstens " + StripBase.DbText(_settings.HearingCap) + "." : ""),
                new[] { "Aus", "Leicht", "Mittel", "Stark", "Sehr stark" },
                () => Array.FindIndex(HearingLevels, kv => Math.Abs(kv.Key - _settings.HearingCap) < 0.1f),
                i => SetHearingCap(HearingLevels[i].Key));
            p.Toggle("Sanft stummschalten", "MUTE, Ducking, Szenen usw. blenden kurz über statt hart zu schalten.",
                () => !_settings.NoSoftMute, v => { _settings.NoSoftMute = !v; Backend.SetSoftFade(v); ScheduleSave(); });
            AddCleanStartSettings(p);   // 3.0.3: kein Knacken beim Abspielen
            AddFaderCurve320(p);        // 3.2: Fader-Kurve
            AddChannelSound320(p);      // 3.2: mono, getauscht, Verzoegerung
            AddSound340(p);             // 3.4: sanft, leise, angleichen, EQ A/B, MONO
            AddSound346(p);             // 3.4.6: Klang pro Kanal, Mix pro Geraet, Wecker
            AddSound350(p);             // 3.5: Hoehen-Filter, Stimmen, Werbung, Bass mono, zweites Geraet, Geraeusche
            AddKlang400(p);             // 4.0: parametrischer EQ, AutoEq, Gruppen-Klang, Hoer-EQ
            AddKomfort400(p);           // 4.0: Abendkurve, Geraeteschutz, Koppeln, Panik, Zeit-Limit, Surround, Presets, Sleep-Ende
            AddTimers400(p);            // 4.0: Fokus-Timer, geplante Aufnahme
            p.Choice("Piepton beim Stummschalten", () => "Kurzer Ton bei Mikro stumm/an – nur für dich, nicht in Discord.",
                new[] { "Aus", "Leise", "Mittel", "Laut" }, () => _settings.MuteBeepOff ? 0 : _settings.MuteBeepLevel,
                i =>
                {
                    if (i == 0) _settings.MuteBeepOff = true;
                    else { _settings.MuteBeepOff = false; _settings.MuteBeepLevel = i; PlayMuteBeep(true); }
                    ScheduleSave();
                });
            int[] sleepMins = { 0, 15, 30, 45, 60, 90, 120 };
            p.Choice("Sleep-Timer", () =>
                {
                    if (_sleepEnd <= 0) return "Blendet am Ende eine Minute lang aus, dann ist der Ton aus.";
                    int left = (int)Math.Ceiling(Math.Max(0, _sleepEnd - Now));
                    return "Läuft – noch " + (left / 60) + ":" + (left % 60).ToString("00") + " Minuten.";
                },
                new[] { "Aus", "15 min", "30 min", "45 min", "1 Std.", "1½ Std.", "2 Std." },
                () => _sleepEnd <= 0 ? 0 : Math.Max(1, _sleepChoice),
                i => { if (i == 0) StopSleep(); else { _sleepChoice = i; StartSleep(sleepMins[i]); } });

            p.Section("Loudness");
            p.Toggle("Loudness", "Voller Klang bei leisem Hören: Bass und Höhen werden angehoben – wie bei alten Hi-Fi-Anlagen (auch Taste LOUD im MASTER).",
                () => _settings.LoudOn, v => { if (v != _settings.LoudOn) ToggleLoud(false); })
                .Enabled = () => _snap.EqSupported || _settings.LoudOn;
            p.Choice("Stärke", () =>
                {
                    return "Bei ganz leise bis +" + (int)LoudMax[Math.Max(0, Math.Min(2, _settings.LoudStrength))] + " dB Bass." +
                           (LoudOn ? " Gerade: " + LoudNowText() + "." : "");
                },
                LoudNames, () => _settings.LoudStrength, i => SetLoudStrength(i)).Enabled = () => _settings.LoudOn;
            p.Toggle("Nach der Windows-Lautstärke richten", "Je leiser Windows eingestellt ist, desto mehr Anhebung (bei voller Lautstärke keine). Aus = immer volle Anhebung, z. B. wenn du am Verstärker oder Headset-Regler leiser drehst.",
                () => !_settings.LoudFixed, v => SetLoudFixed(!v)).Enabled = () => _settings.LoudOn;
            p.Info("Wie die Loudness arbeitet", () => "Solange sie an ist, laufen alle Kanäle wie beim EQ durch das Mischpult (ca. 30–50 ms Verzögerung) und werden etwas leiser gemacht, damit der angehobene Bass nicht verzerrt. Wirkt auch im Stream-Mix und in Aufnahmen. Braucht Windows 10 Version 2004 oder neuer.");

            AddSoundSettings19(p);   // 2.19: Effekte, Vorhoeren, Aufnahme
            p.Section("Hör-Tagebuch");
            p.Toggle("Hör-Tagebuch", "Schreibt mit, wie lange und wie laut du jeden Tag hörst und welche App wie lange Ton gemacht hat (Standard-Ausgabe). Bleibt nur auf diesem PC, die letzten 60 Tage.",
                () => !_settings.DiaryOff, v => SetDiaryOn(v));
            p.Choice("„Laut“ ab", () => "Richtwert aus Lautheit und Windows-Lautstärke: ab " + DiaryLoudFrom.ToString("0").Replace("-", "\u2212") + " dB zählt es als laut. Empfindlich = schon früher.",
                ListeningDiary.LoudNames, () => DiaryLoudIdx, i => { _settings.DiaryLoud = i; ScheduleSave(); }).Enabled = () => !_settings.DiaryOff;
            p.Choice("Warnen, wenn es laut war", () => DiaryWarnIdx == 0 ? "Keine Warnung." : "Einmal am Tag ein Hinweis, wenn du so lange laut gehört hast.",
                ListeningDiary.WarnNames, () => DiaryWarnIdx, i => { _settings.DiaryWarn = i; ScheduleSave(); }).Enabled = () => !_settings.DiaryOff;
            p.Button("Hör-Tagebuch ansehen", "Die letzten 14 Tage als Balken, heute im Detail und die Apps mit der meisten Zeit.",
                "Öffnen", r => ShowDiaryWindow(), () => _diary == null ? null : "heute " + ListeningDiary.Duration(_diary.Get(DateTime.Now, false)?.ListenSec ?? 0));
            p.Button("Hör-Tagebuch löschen", "Alles Aufgezeichnete löschen.", "Löschen", r => ClearDiary());

            p.Section("DIM und FADE");
            p.Choice("DIM: so viel leiser", () => "Taste DIM im MASTER (oder Hotkey): alles auf einmal leiser, nochmal = wie vorher. Die Fader bleiben, wo sie sind. Wirkt auch im Stream-Mix.",
                Array.ConvertAll(DimSteps, d => (int)d + " dB"), () => Array.FindIndex(DimSteps, d => Math.Abs(d - DimDb) < 0.5f), i => SetDimDb(DimSteps[i]));
            p.Toggle("FADE-Taste in den Kanälen", "Zwischen SOLO und MUTE: Kanal langsam ausblenden (danach stumm) bzw. wieder einblenden. Geht immer auch mit Shift+Klick auf MUTE.",
                () => ChannelStrip.FadeKeys, v => SetFadeKeys(v));
            p.Choice("FADE: Dauer", () => "So lange dauert das Aus- bzw. Einblenden. Rechtsklick auf FADE: auch dort einstellbar.",
                Array.ConvertAll(FadeSteps, d => FadeSecText(d)), () => Array.FindIndex(FadeSteps, d => Math.Abs(d - FadeSec) < 0.5f), i => SetFadeSec(FadeSteps[i]));
            pages.Add(p);

            // ------------------------------------------------------------ 4.0 finale: Profi-Ton
            p = new SPage("Profi-Ton", "\uE9E9", "Kompressor-Fenster, De-Esser, Wärme, Ducking-Regeln, Auto-Mix, Hall-Bus, Crossfader, Mute-Gruppen, Stream-Limiter, Signalgenerator.");
            AddPro400(p);
            pages.Add(p);

            // ------------------------------------------------------------ Kanäle & Ausgänge
            p = new SPage("Kanäle & Ausgänge", "", "Kanäle, Gruppen, Ausgabegeräte und Stream-Mix.");
            p.Section("Kanäle");
            AddChannels310bSettings(p);   // 3.1: einklappen, weich gleiten
            AddSort320(p);                // 3.2: Kanaele sortieren
            AddCaps320(p);                // 3.2: Lautstaerke-Deckel
            AddChannels330(p);            // 3.3: Symbole, Notizen, Gruppen-Klammer, Kabel-Ansicht
            AddChannels340(p);            // 3.4: duplizieren, zuruecksetzen, zweifarbig
            AddOutputs343(p);             // 3.4.3: Ausgaenge in dB
            p.Button("Neuer Kanal", "Ein weiterer Kanalzug – Apps per Klick zuordnen.", "Hinzufügen", r => AddChannel()).Primary = true;
            p.Button("Neue Gruppe", "Mehrere Kanäle mit einem Fader und einem MUTE steuern (z. B. „Alles außer Chat“).", "Hinzufügen …", r => AddGroup(null));
            var reset = p.Button("Alle Kanäle auf Werkseinstellung", "Alle Kanäle, Zuordnungen und Reglerstellungen zurücksetzen.", "Zurücksetzen …", r => ResetChannels());
            reset.Danger = true;
            AddBankSettings(p);   // 2.18
            p.Section("Ausgänge");
            p.Toggle("Mehrere Ausgänge", "Ein Zug pro Gerät, und pro Kanal wählbar, wo er spielt (z. B. Spiel auf Headset, Musik auf Lautsprecher).",
                () => MultiOutput, v => SetMultiOutput(v)).Enabled = () => _snap.RoutingSupported || MultiOutput || _demo;
            p.Button("Ausgänge anzeigen", "Welche Geräte als Zug zu sehen sind.", "Auswählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                foreach (var d in _snap.Devices)
                {
                    var id = d.Id;
                    bool shown = !_settings.HiddenOutputs.Contains(id, StringComparer.OrdinalIgnoreCase);
                    m.Items.Add(new ToolStripMenuItem(d.FullName, null, (s, e) =>
                    {
                        if (shown) _settings.HiddenOutputs.Add(id);
                        else _settings.HiddenOutputs.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                        SyncOutputs();
                        ScheduleSave();
                    }) { Checked = shown });
                }
                if (_snap.Devices.Length == 0) m.Items.Add(Hint("Keine Geräte"));
                ShowMenuAt(m, r);
            }).Visible = () => MultiOutput;
            p.Toggle("Stream-Mix", "Ein zweiter, eigener Mix auf einem anderen Gerät (z. B. VB-Cable für OBS). Jeder Kanal bekommt einen Regler STREAM.",
                () => _settings.StreamOn, v => SetStreamOn(v));
            AddOutputNames320(p);   // 3.2: Ausgaenge umbenennen
            AddTestTone320(p);      // 3.2: Testton links/rechts
            pages.Add(p);

            // ------------------------------------------------------------ Automatik
            p = new SPage("Automatik", "", "Was das Mischpult von selbst erledigt.");
            p.Section("Spiele");
            p.Toggle("Spiele automatisch erkennen", "Spiele aus Steam, Epic, GOG, Riot, Ubisoft, EA, Xbox usw. landen von selbst im Spiele-Kanal.",
                () => !_settings.NoAutoGames, v =>
                {
                    _settings.NoAutoGames = !v; ScheduleSave();
                    if (v) { DetectGames(); RefreshChannelViews(); }
                });
            p.Button("Kanal für Spiele", "In diesen Kanal kommen erkannte Spiele.", "Wählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true;
                foreach (var c in _settings.Channels.Where(x => !x.IsCatchAll))
                {
                    var ch = c;
                    m.Items.Add(new ToolStripMenuItem(c.Name, Swatch(Color.FromArgb(c.Color)), (s, e) =>
                    {
                        _settings.GameChannel = ch.Id; ScheduleSave(); DetectGames(); RefreshChannelViews();
                    }) { Checked = _settings.GameChannel == c.Id });
                }
                ShowMenuAt(m, r);
            }, () => _settings.Channels.FirstOrDefault(c => c.Id == _settings.GameChannel)?.Name ?? "keiner");
            float[] bgDb = { 0f, -6f, -12f, -20f, -40f };
            p.Choice("Spiel im Hintergrund leiser", () => "Wenn du aus dem Spiel herauswechselst (Alt+Tab), wird es so viel leiser.",
                new[] { "Aus", "−6 dB", "−12 dB", "−20 dB", "Stumm" },
                () => _settings.BgQuietOff ? 0 : Math.Max(1, Array.FindIndex(bgDb, d => Math.Abs(d - _settings.BgQuietDb) < 0.1f)),
                i =>
                {
                    if (i == 0) _settings.BgQuietOff = true;
                    else { _settings.BgQuietOff = false; _settings.BgQuietDb = bgDb[i]; _bgQuietApplied = 1f; }
                    ScheduleSave();
                });
            p.Button("Profil pro Spiel", "Startet ein Spiel, wird automatisch seine Szene geladen; danach kommt der vorherige Mix zurück.", "Einstellen ▾",
                r => ShowDropDownAt(BuildProfileMenu(), r), () => _settings.GameProfiles.Count == 0 ? "keine" : _settings.GameProfiles.Count + " Spiel(e)");
            p.Section("Ducking");
            p.Toggle("Ducking", "Musik & Co. werden leiser, sobald im Auslöser-Kanal (z. B. CHAT mit Discord) jemand spricht.",
                () => _settings.DuckOn, v => { if (v != _settings.DuckOn) ToggleDuck(false); });
            p.Button("Ducking einstellen", "Auslöser, welche Kanäle leiser werden, wie viel und wie lange.", "Einstellen …", r => ShowDuckDialog());
            AddDuck423(p);          // 4.2.3: Feinschliff
            AddDuck430(p);          // 4.3: wer loest aus, Halte-Taste, Vorlagen, nur wenn …
            p.Section("Geräte und Zeit");
            p.Button("Headset automatisch umschalten", "Headset einstecken = wird Standardgerät (auch das Mikro), abziehen = zurück auf die Lautsprecher.", "Einstellen …",
                r => ShowHeadsetDialog(), () => _settings.AutoSwitchOn && _settings.AutoDevice != null ? "an" : "aus");
            p.Button("Szenen nach Uhrzeit", "Szene automatisch laden, z. B. werktags ab 22:00 „Nacht“.", "Einstellen …",
                r => ShowScheduleDialog(), () => { int n = _settings.Schedules?.Count(x => !x.Off) ?? 0; return n == 0 ? "keine" : n + " aktiv"; });
            AddSceneXfade320(p);    // 3.2: Szenen weich ueberblenden
            AddBgApps320(p);        // 3.2: Hintergrund-Apps leiser
            AddAutoPause320(p);     // 3.2: Auto-Pause
            AddAuto430(p);          // 4.3: Profil fuer Apps, Kanal-Timer, Windows-Toene, Akku
            pages.Add(p);

            // ------------------------------------------------------------ Mikrofon & Discord
            p = new SPage("Mikrofon & Discord", "", "Push-to-Talk, Sprach-Ausgang, Mikro-Effekte und Soundboard.");
            p.Button("Push-to-Talk / Push-to-Mute", "Mikro nur beim Drücken einer Taste an (oder aus) – funktioniert überall, auch im Spiel.", "Einstellen …",
                r => ShowPttDialog(), () => _settings.PttMode == 1 ? "Push-to-Talk" : _settings.PttMode == 2 ? "Push-to-Mute" : "aus");
            p.Button("Sprach-Ausgang (Mikro für Discord)", "Dein Mikro mit Rauschsperre plus Soundboard auf ein eigenes Gerät, z. B. VB-Cable.", "Einstellen …",
                r => ShowVoiceDialog(), () => _settings.VoiceOn && _settings.VoiceDevice != null ? "an" : "aus");
            p.Button("Mikro-Effekte", "Rumpelfilter, „Stimme klarer“, Bässe/Höhen, Kompressor – wirken im Sprach-Ausgang.", "Einstellen …",
                r => ShowMicFxDialog(), () => CurrentVoiceFx().IsOff ? "aus" : "an");
            p.Button("Soundboard", "Sounds per Klick, Hotkey oder MIDI-Taste abspielen.", "Öffnen", r => ShowSoundboard(),
                () => _settings.Sounds.Count == 0 ? "leer" : _settings.Sounds.Count + " Sound(s)");
            pages.Add(p);

            // ------------------------------------------------------------ Steuerung
            p = new SPage("Steuerung", "", "Tastenkürzel und MIDI-Controller.");
            p.Button("Hotkeys", "Tastenkürzel, die überall funktionieren – auch im Spiel (Mikro stumm, Szenen, Kanäle, Musik …).", "Einstellen …",
                r => ShowHotkeyDialog(), () => { int n = _settings.Hotkeys?.Count(h => h.Keys != 0) ?? 0; return n == 0 ? "keine" : n + " belegt"; });
            p.Button("MIDI-Controller", "Fader, Drehregler und Tasten eines MIDI-Controllers per „Lernen“ zuordnen.", "Einstellen …",
                r => ShowMidiDialog(), () => _settings.Midi.Count == 0 ? "keine" : _settings.Midi.Count + " Zuordnung(en)");
            AddMacros320(p);   // 3.2: Makros
            AddLock320(p);     // 3.2: Pult sperren
            AddControl340(p);  // 3.4: Lautstaerketasten, Mausrad, Schnell-Regler, Tastatur
            AddControl346(p);  // 3.4.6: Einrasten, Mausrad-Schritte, Szenen-Tasten, Taskleiste
            AddControl350(p);  // 3.5: eintippen, mehrere Fader, kopieren, Spickzettel, Controller, Szenen, Obergrenze …
            AddControl400(p);  // 4.0: Automation, Mausrad, heisse Ecke, Apps ziehen, Kanal-Fenster, eigene Tasten, Suche, Teil-Szenen
            pages.Add(p);

            // ------------------------------------------------------------ Sichern
            p = new SPage("Sichern", "", "Einstellungen sichern oder auf einen anderen PC mitnehmen.");
            p.Button("Einstellungen exportieren", "Alles in einer Datei sichern.", "Exportieren …", r => ExportSettings());
            p.Button("Einstellungen importieren", "Ersetzt alle Einstellungen und startet das Mischpult neu.", "Importieren …", r => ImportSettings());
            p.Button("Einstellungsordner", MixerSettings.Folder, "Öffnen", r =>
            {
                try { Directory.CreateDirectory(MixerSettings.Folder); Process.Start("explorer.exe", MixerSettings.Folder); } catch { }
            });
            AddBackupSettings(p);   // 2.18
            AddPortable346(p);      // 3.4.6: portabler Modus
            AddStorage400(p);       // 4.0: Dokumente, Cloud, Profile
            AddPackage410(p);       // 4.1: Umzugs-Paket
            pages.Add(p);

            ApplySimple400(pages);  // 4.0: einfache Einstellungen
            return pages;
        }

        /// <summary>Kurzer Bericht: Skalierung, ob Windows hochrechnet (unscharf), Schriftglaettung.</summary>
        public string DisplayInfo()
        {
            int pct = (int)Math.Round(Theme.S * 100f);
            int aw = Win32.DpiAwareness();
            string mode = aw == 0 ? "Achtung: Windows rechnet das Fenster hoch – dadurch wird alles unscharf. Abhilfe: Rechtsklick auf Mischpult.exe → Eigenschaften → Kompatibilität → „Hohe DPI-Einstellungen ändern“ → beide Haken entfernen, Mischpult neu starten."
                : aw == 1 ? "Windows rechnet nur auf Bildschirmen mit anderer Skalierung hoch."
                : aw == 2 ? "Scharf: Windows rechnet nichts hoch."
                : "–";
            string text = Theme.GdiText ? "Windows-Schrift (ClearType)" : "Schrift-Glättung: " + (Theme.TextHint == System.Drawing.Text.TextRenderingHint.ClearTypeGridFit ? "ClearType" : "Graustufen");
            return "Skalierung " + pct + " % · " + text + ". " + mode;
        }

        private void ResetAppRouting()
        {
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Alle in Windows gespeicherten Ausgabegeräte pro App zurücksetzen?\n\nAlle Apps spielen danach wieder auf dem Standardgerät.",
                    "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Backend.ResetAllAppRouting();
        }

        private void ResetChannels()
        {
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Alle Kanäle, Zuordnungen und Reglerstellungen auf die Grundeinstellung zurücksetzen?",
                    "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var d = MixerSettings.CreateDefault();
            d.Normalize();
            _settings.Channels = d.Channels;
            _policyCache.Clear();
            RebuildAppMap();
            BuildChannelStrips();
            PushPolicy();
            ScheduleSave();
        }
    }
}
