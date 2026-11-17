using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0: "Was ist neu" – nach einem Update einmal zeigen (abschaltbar), sonst ueber die Einstellungen.</summary>
    internal sealed class WhatsNewWindow : Form
    {
        /// <summary>4.2: Neuheiten (Titel, dann Paare Name/Erklaerung).</summary>
        internal static readonly string[][] Sections42 =
        {
            new[] { "Ducking-Feinschliff (4.2.3)",
                "Lämpchen bei den Auslösern", "Im Ducking-Fenster leuchtet neben jedem Auslöser ein Punkt, wenn er gerade auslöst.",
                "Kurze Geräusche ignorieren", "Tippen, Mausklicks und Pieptöne machen die Musik nicht mehr leiser.",
                "Übergang selbst einstellen", "Wie schnell die Musik leiser und wie sanft sie wieder lauter wird.",
                "MIKRO in den Ducking-Regeln", "Eigene Stärke, wenn du sprichst – z. B. MIKRO −6 dB, TS −15 dB." },
            new[] { "Optik",
                "Drei neue Designs", "Synthwave (Sonnenuntergang mit Neon-Gitter), Retro-Game (Pixel-Look) und Cockpit (wie im Flugzeug).",
                "Neue bewegte Hintergründe", "Nordlicht, Unterwasser, Kaminfeuer und Matrix-Regen.",
                "Neue Wände", "Samtvorhang, Sichtbeton und Kacheln hinter dem Pult.",
                "Spiegelnder Tisch", "Das Pult steht auf einer glänzenden Fläche – die Züge und ihr Licht spiegeln sich.",
                "RGB-Welle", "Ein Regenbogen läuft über die Ränder aller Kanäle, auf Wunsch im Takt.",
                "Leuchtender Fensterrand", "Der Fensterrahmen leuchtet in der Akzentfarbe oder mit der Musik (Windows 11).",
                "Startbild", "Beim Start fahren die Fader hoch, das Logo leuchtet auf.",
                "App-Symbol auf der Faderkappe", "Das Symbol der App, die gerade spielt, sitzt auf der Kappe." },
            new[] { "Extras",
                "Klang-Kunst", "Jeden Abend ein Bild aus dem, was du gehört hast – Ringe wie Jahresringe, mit Galerie.",
                "Schalter im Was-ist-neu-Fenster", "Neuheiten direkt hier ein- und ausschalten.",
                "Funktions-Pakete", "Gamer, Streamer, Musik hören, Show oder Schlicht – ein Klick schaltet Passendes ein.",
                "Doppelklick auf Mischpult-Dateien", "Umzugs-Pakete und EQ-Presets öffnen sich direkt im Mischpult.",
                "Ducking mit mehreren Auslösern", "4.2.1: Im Ducking-Fenster (Rechtsklick auf DUCKING) mehrere Auslöser anhaken – einer, der spricht, reicht. Ab 4.2.2 auch dein MIKRO." },
        };

        /// <summary>Anzahl der 4.2-Neuheiten.</summary>
        internal static int Count42
        {
            get { int n = 0; foreach (var s in Sections42) n += (s.Length - 1) / 2; return n; }
        }

        /// <summary>4.1: Neuheiten dieser Version (Titel, dann Paare Name/Erklaerung).</summary>
        internal static readonly string[][] Sections41 =
        {
            new[] { "Installieren & Aktualisieren",
                "Ein-Klick-Fenster", "Startest du eine heruntergeladene Mischpult.exe, fragt sie gleich: installieren oder aktualisieren. Die alte Version beendet sich von selbst.",
                "Update per Ziehen", "Neue Mischpult.exe einfach aufs Pult ziehen.",
                "Download-Ordner im Blick", "Liegt dort eine neuere Mischpult.exe, kommt ein Hinweis mit „Jetzt aktualisieren“ – ohne Internet.",
                "Online nach Updates suchen", "Einmal am Tag beim GitHub-Projekt nachsehen und auf Knopfdruck aktualisieren.",
                "Neuheiten im Update-Fenster", "Vor dem Aktualisieren sehen, was neu ist.",
                "Sicherung vor dem Update", "Einstellungen werden gesichert, die alte Version aufgehoben – und lässt sich zurückholen.",
                "Sauber deinstallieren", "Auf Wunsch kommen beim Deinstallieren auch die Einstellungen weg." },
            new[] { "Sicher starten",
                "Start-Schutz", "Zweimal direkt beim Start ausgestiegen? Dann fragt das Mischpult: abgesichert starten, vorige Version oder nochmal versuchen.",
                "Abgesicherter Start", "Umschalt beim Start gedrückt halten: ohne Designs, Effekte und Zusatzfenster – nur für diesen Start.",
                "Autostart mit Verzögerung", "Mit Windows erst nach 10, 30 oder 60 Sekunden starten – der PC fährt schneller hoch." },
            new[] { "Umziehen & Aufräumen",
                "Umzugs-Paket", "Einstellungen, Sounds, Kanal-Bilder und Hintergrund in einer Datei – auf dem neuen PC ist alles wieder da.",
                "Mischpult reparieren", "Leistung, Aussehen und Fensterpositionen zurück auf Werkseinstellung, Kanäle und Szenen bleiben.",
                "Was ist eingeschaltet", "Eine Seite zeigt nur, was du verändert hast – mit „Alle Extras aus“." },
        };

        /// <summary>Anzahl der 4.1-Neuheiten.</summary>
        internal static int Count41
        {
            get { int n = 0; foreach (var s in Sections41) n += (s.Length - 1) / 2; return n; }
        }

        /// <summary>Abschnitte: Titel, dann Paare (Name, Erklaerung).</summary>
        internal static readonly string[][] Sections =
        {
            new[] { "Speichern & Start",
                "Alles im Ordner Dokumente", "Einstellungen, Sicherungen, Aufnahmen, Bilder und Protokolle liegen in Dokumente\\Mischpult – nicht mehr versteckt unter AppData.",
                "Einstellungen in der Cloud", "Ordner in OneDrive, Dropbox & Co. wählen – deine Einstellungen wandern mit auf den zweiten PC.",
                "Benutzer-Profile", "Mehrere Profile (z. B. „Jan“, „Gast“), jedes mit eigenem Pult. Auf Wunsch fragt das Mischpult beim Start.",
                "Installieren", "Mit einem Klick installieren: Startmenü, Desktop-Verknüpfung und Eintrag zum Deinstallieren in Windows.",
                "Einfache Einstellungen", "Nur das Wichtigste in den Einstellungen zeigen – umschalten links unten.",
                "Was ist neu", "Dieses Fenster. Erscheint nach einem Update einmal." },
            new[] { "Klang",
                "Parametrischer EQ", "Pro Kanal und Ausgang bis zu 8 Bänder mit Frequenz, Stärke und Güte – Punkte einfach in der Kurve ziehen.",
                "Kopfhörer-Profile (AutoEq)", "Profil für deinen Kopfhörer von autoeq.app laden (Datei „ParametricEQ.txt“) – klingt danach neutraler.",
                "EQ und Kompressor für Gruppen", "Eine Gruppe kann selbst EQ und Kompressor haben – wirkt auf alle ihre Kanäle.",
                "Hörtest mit EQ", "Kurzer Test mit Tönen pro Ohr – daraus macht das Pult einen EQ, der leise gehörte Töne etwas anhebt." },
            new[] { "Ton & Komfort",
                "Abendkurve", "Ab einer Uhrzeit wird es über eine Stunde (einstellbar) langsam leiser – morgens wieder normal.",
                "Geräte-Wechsel-Schutz", "Neues Gerät (z. B. Bluetooth-Kopfhörer) startet nie zu laut, und Windows schaltet nicht mehr von selbst um.",
                "Ausgänge koppeln", "Zwei Ausgänge werden gemeinsam lauter und leiser.",
                "Panik-Taste", "Ein Druck: alles stumm. Noch einer: alles wie vorher.",
                "Zeit-Limit pro Kanal", "Hat ein Kanal heute lange genug gespielt, blendet er aus – mit Passwort aus 3.5 auch kindersicher.",
                "Surround-Pegel", "Bei 5.1/7.1 zeigt der MASTER alle Kanäle einzeln.",
                "EQ-Presets teilen", "Eigene EQ-Presets als Datei speichern und auf einem anderen PC laden.",
                "Sleep-Timer mit Ende", "Nach dem Sleep-Timer: Bildschirm aus, Energiesparen oder Herunterfahren (30 Sekunden zum Abbrechen)." },
            new[] { "Optik",
                "Zwei neue Designs", "„Carbon“ und „Holz & Messing“.",
                "Design folgt Windows", "Hell oder dunkel – wie in Windows eingestellt, auch beim Umschalten.",
                "Lautheits-Radar", "Rundes Radar in der Meterbridge: ein Zeiger kreist einmal pro Minute und malt die Lautheit – grün ums Ziel.",
                "Hintergrund tanzt zur Musik", "Hinter den Kanälen glüht es in der Akzentfarbe – stärker, wenn der Bass kommt.",
                "Drehknöpfe statt Fader", "Ein großer Drehknopf in jedem Zug, die Pegel bleiben daneben.",
                "Nixie-Balken", "Pegel als orange glimmende Nixie-Balkenröhren hinter Glas.",
                "Bewegte GIF-Bilder", "Kanal-Bilder dürfen animierte GIFs sein.",
                "Motor-Surren", "Leises Surren, wenn die Motorfader fahren.",
                "Cover als Hintergrund", "Das Album-Cover weich verschwommen hinter den Kanälen.",
                "Neue Uhren", "Wortuhr („ES IST HALB ZEHN“) und Binäruhr.",
                "Mehr Saison-Deko", "Ostern, Frühling, Sommer, Herbstlaub und Konfetti an deinem Geburtstag.",
                "Klick-Arten", "Studio-Taste, Schreibmaschine oder Retro-Schalter." },
            new[] { "Aufbau",
                "Kanalzug anpassen", "Teile des Kanalzugs ausblenden, die du nicht brauchst (Panorama, EQ-Knöpfe, Tasten …).",
                "Zwei Reihen", "Viele Kanäle? Bei hohem Fenster stehen sie in zwei Reihen.",
                "Barrierefreiheit", "Bildschirmleser, deutlicher Fokus-Rahmen, kräftigere Schrift und weniger Bewegung." },
            new[] { "Bedienung",
                "Fader-Automation", "Fader-Bewegungen aufnehmen und abspielen (auch als Schleife).",
                "Mausrad über einem Fenster", "Alt (oder eine andere Zusatztaste) + Mausrad über einem Fenster macht genau diese App lauter oder leiser.",
                "Heiße Ecke", "Mausrad in einer Bildschirmecke ändert die Lautstärke – egal welches Fenster vorne ist.",
                "Apps ziehen", "App-Symbol von einem Kanal auf einen anderen ziehen.",
                "Kanal-Fenster", "Einen Kanal als kleines eigenes Fenster auf den Bildschirm legen.",
                "Eigene Tasten", "Bis zu 6 eigene Tasten in der Kopfleiste – für jede Aktion, Szene oder ein Programm.",
                "Kanal-Suche", "Strg+F: Kanal oder App suchen.",
                "Teil-Szenen", "Szenen, die nur einige Kanäle ändern." },
            new[] { "Fenster & Extras",
                "Spiel-Overlay", "Lautstärke und Song klein über dem Spiel (randloses Fenster).",
                "Desktop-Widget", "Lautstärke-Knopf und Song direkt auf dem Desktop.",
                "Raum-Ansicht", "Kanäle im Raum verschieben: Abstand = Lautstärke, Seite = links/rechts.",
                "Ton-Zeitleiste", "Wann lief heute welcher Kanal?",
                "Statistik", "Hörzeit pro Kanal und Tag, Lieblings-Szenen, lauteste Momente.",
                "Mini-Player", "Kleines Fenster mit Cover, Titel und Musik-Tasten.",
                "Ton-Doktor", "Sucht typische Ton-Probleme und behebt viele mit einem Klick.",
                "Fokus-Timer", "25 Minuten Ruhe vor Spiel und Chat, dann Pause – mit sanftem Gong.",
                "Geplante Aufnahme", "Aufnahme startet und stoppt zu einer Uhrzeit." },
            new[] { "Musik & Geräte",
                "Mehr Musik-Tasten", "10 Sekunden spulen, Zufall und Wiederholen.",
                "Lautstärke pro Interpret", "Für einen Interpreten oder Podcast eine eigene Lautstärke merken.",
                "Was läuft – pro Kanal", "Im App-Feld steht klein der Fenstertitel der lautesten App (Video, Spiel …).",
                "MIDI-Rückmeldung", "Motorfader und Leuchten am MIDI-Controller folgen dem Pult.",
                "OBS-Steuerung", "Stream, Aufnahme und Szenen in OBS – Szenen auf Wunsch verbunden, ON-AIR-Lampe folgt OBS.",
                "WLED-Raumlicht", "LED-Streifen mit WLED leuchten zur Musik." },
        };

        private readonly NewsView _view;

        public event Action OpenSettings;
        /// <summary>4.2: Schalter zu einer Neuheit (null = keine Schalter zeigen).</summary>
        public Func<string, SToggle> ToggleFor { get => _view.ToggleFor; set => _view.ToggleFor = value; }

        public WhatsNewWindow()
        {
            Text = "Was ist neu – Mischpult " + Program.Version;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(12f);
            ClientSize = new Size(Theme.Px(640), Theme.Px(640));
            MinimumSize = new Size(Theme.Px(480), Theme.Px(400));
            _view = new NewsView { Dock = DockStyle.Fill };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.Px(56), BackColor = Theme.MenuBg };
            var ok = new Button { Text = "Alles klar", Width = Theme.Px(130), Height = Theme.Px(34), FlatStyle = FlatStyle.Flat, BackColor = Theme.Button, ForeColor = Theme.ButtonText };
            var set = new Button { Text = "Zu den Einstellungen", Width = Theme.Px(190), Height = Theme.Px(34), FlatStyle = FlatStyle.Flat, BackColor = Theme.Button, ForeColor = Theme.ButtonText };
            ok.FlatAppearance.BorderColor = Theme.Accent;
            set.FlatAppearance.BorderColor = Theme.MenuBorder;
            ok.Click += (s, e) => Close();
            set.Click += (s, e) => { Close(); OpenSettings?.Invoke(); };
            bar.Controls.Add(ok); bar.Controls.Add(set);
            bar.Layout += (s, e) =>
            {
                ok.Location = new Point(bar.Width - ok.Width - Theme.Px(16), (bar.Height - ok.Height) / 2);
                set.Location = new Point(ok.Left - set.Width - Theme.Px(10), ok.Top);
            };
            Controls.Add(_view);
            Controls.Add(bar);
            AcceptButton = ok;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) _view.ScrollBy(e.KeyCode == Keys.Down ? Theme.F(40) : _view.Height * 0.8f);
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) _view.ScrollBy(-(e.KeyCode == Keys.Up ? Theme.F(40) : _view.Height * 0.8f));
        }

        /// <summary>Anzahl der Funktionen in der Liste.</summary>
        internal static int Count
        {
            get { int n = 0; foreach (var s in Sections) n += (s.Length - 1) / 2; return n; }
        }

        private sealed class NewsView : Control
        {
            private float _scroll, _contentH;

            public Func<string, SToggle> ToggleFor;
            private readonly Dictionary<string, SToggle> _toggles = new Dictionary<string, SToggle>();
            private readonly List<KeyValuePair<RectangleF, SToggle>> _hits = new List<KeyValuePair<RectangleF, SToggle>>();

            private SToggle Toggle(string name)
            {
                if (ToggleFor == null) return null;
                if (!_toggles.TryGetValue(name, out var t)) { try { t = ToggleFor(name); } catch { t = null; } _toggles[name] = t; }
                return t;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                foreach (var h in _hits)
                    if (h.Key.Contains(e.Location) && h.Value.IsEnabled) { try { h.Value.Set(!h.Value.Get()); } catch { } Invalidate(); return; }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool hit = false;
                foreach (var h in _hits) if (h.Key.Contains(e.Location)) { hit = true; break; }
                Cursor = hit ? Cursors.Hand : Cursors.Default;
            }

            public NewsView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            }

            public void ScrollBy(float d)
            {
                float max = Math.Max(0f, _contentH - Height);
                _scroll = Math.Max(0f, Math.Min(max, _scroll + d));
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollBy(-e.Delta / 120f * Theme.F(60));
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                g.Clear(Theme.MenuBg);
                float x = Theme.F(28), w = Width - Theme.F(56), y = Theme.F(22) - _scroll;
                using (var tb = new SolidBrush(Theme.Text))
                using (var db = new SolidBrush(Theme.TextDim))
                using (var ab = new SolidBrush(Theme.AccentText))
                {
                    var big = Theme.Ui(20f, FontStyle.Bold);
                    var subF = Theme.Ui(11.5f);
                    var head = Theme.UiSemi(13.5f);
                    var name = Theme.UiSemi(11.5f);
                    var desc = Theme.Ui(11f);
                    _hits.Clear();
                    for (int part = 0; part < 3; part++)
                    {
                    // 4.2 oben, darunter 4.1 und alles aus 4.0
                    if (part > 0) y += Theme.F(22);
                    Theme.Str(g, part == 0 ? "Neu in Mischpult " + Program.Version : part == 1 ? "Aus Version 4.1" : "Aus Version 4.0", part == 0 ? big : Theme.Ui(17f, FontStyle.Bold), tb, new RectangleF(x, y, w, Theme.F(34)), Theme.Left);
                    y += Theme.F(part == 0 ? 38 : 32);
                    string sub = part == 0
                        ? Count42 + " neue Sachen: Ducking-Feinschliff, Optik (Designs, Hintergründe, Wände, Tisch, RGB-Welle, Fensterrand, Startbild) und Extras – jede einzeln an- und ausschaltbar unter OPTIONEN → Automatik („Ducking“), Aussehen („4.2 – Optik“), Allgemein („4.2 – Komfort“) und Fenster & Verbindungen. Alles ist zuerst aus." +
                          (ToggleFor != null ? " Mit dem Schalter rechts schaltest du eine Neuheit gleich hier ein." : "")
                        : part == 1
                        ? Count41 + " Funktionen rund ums Installieren, Aktualisieren und Aufräumen – unter OPTIONEN → Allgemein (Abschnitte „4.1“) und Sichern."
                        : Count + " Funktionen aus 4.0 – Schau unter OPTIONEN in die Abschnitte „4.0“.";
                    float sh = g.MeasureString(sub, subF, (int)w).Height;
                    Theme.Str(g, sub, subF, db, new RectangleF(x, y, w, sh + 4), Theme.Wrap);
                    y += sh + Theme.F(14);
                    foreach (var s in part == 0 ? Sections42 : part == 1 ? Sections41 : Sections)
                    {
                        y += Theme.F(8);
                        Theme.Str(g, s[0].ToUpperInvariant(), head, ab, new RectangleF(x, y, w, Theme.F(24)), Theme.Left);
                        y += Theme.F(26);
                        for (int i = 1; i + 1 < s.Length; i += 2)
                        {
                            using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, x + Theme.F(2), y + Theme.F(7), Theme.F(5), Theme.F(5));
                            var tg = Toggle(s[i]);   // 4.2: Schalter rechts
                            float tw = tg != null ? Theme.F(56) : 0f;
                            if (tg != null)
                            {
                                var tr = new RectangleF(x + w - Theme.F(42), y + Theme.F(2), Theme.F(38), Theme.F(20));
                                bool on = false;
                                try { on = tg.Get(); } catch { }
                                SettingsView.DrawToggle(g, tr, on, tg.IsEnabled, false);
                                _hits.Add(new KeyValuePair<RectangleF, SToggle>(RectangleF.Inflate(tr, Theme.F(4), Theme.F(4)), tg));
                            }
                            float nh = g.MeasureString(s[i], name, (int)(w - Theme.F(16) - tw)).Height;
                            Theme.Str(g, s[i], name, tb, new RectangleF(x + Theme.F(14), y, w - Theme.F(16) - tw, nh + 2), Theme.Wrap);
                            y += nh;
                            float dh = g.MeasureString(s[i + 1], desc, (int)(w - Theme.F(16) - tw)).Height;
                            Theme.Str(g, s[i + 1], desc, db, new RectangleF(x + Theme.F(14), y, w - Theme.F(16) - tw, dh + 2), Theme.Wrap);
                            y += dh + Theme.F(8);
                        }
                    }
                    }
                }
                _contentH = y + _scroll + Theme.F(20);
                if (_contentH > Height)
                {
                    float th = Math.Max(Theme.F(30), Height * Height / _contentH);
                    float ty = (Height - th) * (_scroll / Math.Max(1f, _contentH - Height));
                    Theme.FillRound(g, Theme.Alpha(Theme.TextDim, 90), new RectangleF(Width - Theme.F(9), ty + Theme.F(2), Theme.F(5), th - Theme.F(4)), Theme.F(2.5f));
                }
            }
        }
    }
}
