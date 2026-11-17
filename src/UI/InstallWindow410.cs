using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.1: kleines Frage-Fenster im Pult-Look – Ueberschrift, Text, auf Wunsch eine Liste (Neuheiten), Schalter und Tasten.
    /// Dient fuers Ein-Klick-Installieren, den Start-Schutz, den abgesicherten Start und als Hinweis unten rechts (Update gefunden).
    /// </summary>
    internal sealed class AskWin410 : ProWin
    {
        public sealed class Check { public string Id, Text; public bool On; public Func<bool> Visible; }
        private sealed class Key { public string Id, Text; public bool Primary; public bool Left; }

        public string Heading, Body, Busy, ListTitle;
        public string[] List;
        public readonly List<Check> Checks = new List<Check>();
        private readonly List<Key> _keys = new List<Key>();
        private readonly Dictionary<string, RectangleF> _checkR = new Dictionary<string, RectangleF>();
        /// <summary>Taste gedrueckt: true = Fenster schliessen.</summary>
        public Func<string, bool> Clicked;
        public string Result { get; private set; }
        /// <summary>Als Hinweis unten rechts (ohne Fokus, ueber allem).</summary>
        public bool Toast;
        private readonly int _w;

        public AskWin410(string title, int w = 560) : base(title, w, 300) { _w = w; }

        public AskWin410 AddKey(string id, string text, bool primary = false, bool left = false) { _keys.Add(new Key { Id = id, Text = text, Primary = primary, Left = left }); return this; }
        public AskWin410 AddCheck(string id, string text, bool on, Func<bool> visible = null) { Checks.Add(new Check { Id = id, Text = text, On = on, Visible = visible }); return this; }
        public bool IsOn(string id) { var c = Checks.FirstOrDefault(x => x.Id == id); return c != null && c.On && (c.Visible == null || c.Visible()); }
        public void SetKeys(params string[] idTextPairs)
        {
            _keys.Clear();
            for (int i = 0; i + 1 < idTextPairs.Length; i += 2) _keys.Add(new Key { Id = idTextPairs[i], Text = idTextPairs[i + 1], Primary = i + 2 >= idTextPairs.Length });
            Relayout();
        }

        protected override bool ShowWithoutActivation => Toast;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (Toast) cp.ExStyle |= 0x00000008 | 0x00000080;   // TOPMOST, TOOLWINDOW
                return cp;
            }
        }

        /// <summary>Hoehe passend zum Inhalt (nach Aenderungen am Text aufrufen).</summary>
        public void Relayout()
        {
            using (var bmp = new Bitmap(4, 4))
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Setup(g);
                float h = Measure(g, Theme.Px(_w) - Theme.F(20));
                int ch = (int)Math.Ceiling(TitleH + Theme.F(4) + h + Theme.F(12));
                if (ClientSize.Height != ch) ClientSize = new Size(Theme.Px(_w), ch);
            }
            if (IsHandleCreated) Place();
            Invalidate();
        }

        private float Measure(Graphics g, float w)
        {
            float y = Theme.F(6);
            if (!string.IsNullOrEmpty(Heading)) y += g.MeasureString(Heading, Theme.Ui(15f, FontStyle.Bold), (int)w).Height + Theme.F(6);
            if (!string.IsNullOrEmpty(Body)) y += g.MeasureString(Body, Theme.Ui(10.5f), (int)w).Height + Theme.F(10);
            if (List != null && List.Length > 0)
            {
                if (!string.IsNullOrEmpty(ListTitle)) y += Theme.F(22);
                y += (int)Math.Ceiling(List.Length / 2.0) * Theme.F(19) + Theme.F(8);
            }
            int checks = Checks.Count(c => c.Visible == null || c.Visible());
            y += checks * Theme.F(30);
            y += Theme.F(12) + Theme.F(32);
            return y;
        }

        private void Place()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Toast) Location = new Point(wa.Right - Width - Theme.Px(16), wa.Bottom - Height - Theme.Px(16));
            else if (Owner == null || !Owner.Visible) Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            else Location = new Point(Owner.Left + (Owner.Width - Width) / 2, Owner.Top + Math.Max(0, (Owner.Height - Height) / 3));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Relayout();
            Place();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!Toast) { try { Activate(); if (Win32.IsWindows) Win32.SetForegroundWindow(Handle); } catch { } }
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            Btn.Clear(); _checkR.Clear();
            float y = r.Y + Theme.F(6);
            if (!string.IsNullOrEmpty(Heading))
            {
                var f = Theme.Ui(15f, FontStyle.Bold);
                float h = g.MeasureString(Heading, f, (int)r.Width).Height;
                using (var b = new SolidBrush(Theme.Text)) Theme.Str(g, Heading, f, b, new RectangleF(r.X, y, r.Width, h + 2), Theme.Wrap);
                y += h + Theme.F(6);
            }
            if (!string.IsNullOrEmpty(Body))
            {
                var f = Theme.Ui(10.5f);
                float h = g.MeasureString(Body, f, (int)r.Width).Height;
                using (var b = new SolidBrush(Theme.TextDim)) Theme.Str(g, Body, f, b, new RectangleF(r.X, y, r.Width, h + 2), Theme.Wrap);
                y += h + Theme.F(10);
            }
            if (List != null && List.Length > 0)
            {
                if (!string.IsNullOrEmpty(ListTitle)) { Label(g, ListTitle.ToUpperInvariant(), new RectangleF(r.X, y, r.Width, Theme.F(20)), Theme.AccentText, 9.5f, true); y += Theme.F(22); }
                float cw = (r.Width - Theme.F(10)) / 2f;
                for (int i = 0; i < List.Length; i++)
                {
                    float cx = r.X + (i % 2) * (cw + Theme.F(10)), cy = y + (i / 2) * Theme.F(19);
                    using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, cx + Theme.F(2), cy + Theme.F(7), Theme.F(5), Theme.F(5));
                    Label(g, List[i], new RectangleF(cx + Theme.F(13), cy, cw - Theme.F(13), Theme.F(19)), Theme.Text, 10f, false, VCenter);
                }
                y += (int)Math.Ceiling(List.Length / 2.0) * Theme.F(19) + Theme.F(8);
            }
            foreach (var c in Checks)
            {
                if (c.Visible != null && !c.Visible()) continue;
                var row = new RectangleF(r.X, y, r.Width, Theme.F(28));
                _checkR[c.Id] = row;
                bool hov = Hover == "chk:" + c.Id;
                SettingsView.DrawToggle(g, new RectangleF(row.X, row.Y + Theme.F(4), Theme.F(38), Theme.F(20)), c.On, Busy == null, hov);
                Label(g, c.Text, new RectangleF(row.X + Theme.F(48), row.Y, row.Width - Theme.F(48), row.Height), Busy == null ? Theme.Text : Theme.TextFaint, 10.5f, false, VCenter);
                y += Theme.F(30);
            }
            float by = r.Bottom - Theme.F(32);
            if (Busy != null)
            {
                Label(g, Busy, new RectangleF(r.X, by, r.Width, Theme.F(30)), Theme.AccentText, 10.5f, true, VCenter);
                return;
            }
            float right = r.Right, left = r.X;
            for (int i = _keys.Count - 1; i >= 0; i--)
            {
                var k = _keys[i];
                if (k.Left) continue;
                float w = Math.Max(Theme.F(96), g.MeasureString(k.Text, Theme.UiSemi(9.5f)).Width + Theme.F(26));
                DrawBtn(g, k.Id, new RectangleF(right - w, by, w, Theme.F(30)), k.Text, k.Primary, Theme.Accent);
                right -= w + Theme.F(8);
            }
            foreach (var k in _keys.Where(x => x.Left))
            {
                float w = Math.Max(Theme.F(96), g.MeasureString(k.Text, Theme.UiSemi(9.5f)).Width + Theme.F(26));
                DrawBtn(g, k.Id, new RectangleF(left, by, w, Theme.F(30)), k.Text, false, Theme.Accent);
                left += w + Theme.F(8);
            }
        }

        protected override string ExtraZone(Point p)
        {
            if (Busy != null) return null;
            foreach (var kv in _checkR) if (kv.Value.Contains(p)) return "chk:" + kv.Key;
            return null;
        }

        protected override bool ExtraDown(MouseEventArgs e)
        {
            if (Busy != null || e.Button != MouseButtons.Left) return false;
            foreach (var kv in _checkR)
                if (kv.Value.Contains(e.Location))
                {
                    var c = Checks.First(x => x.Id == kv.Key);
                    c.On = !c.On;
                    Invalidate();
                    return true;
                }
            return false;
        }

        protected override void OnButton(string id, MouseEventArgs e)
        {
            if (Busy != null || e.Button != MouseButtons.Left) return;
            if (Clicked == null || Clicked(id)) { Result = id; Close(); }
        }

        /// <summary>Waehrend einer laengeren Aktion: Text statt Tasten zeigen (sofort gezeichnet).</summary>
        public void ShowBusy(string text)
        {
            Busy = text;
            Invalidate();
            try { Update(); } catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Busy == null)
            {
                var k = _keys.LastOrDefault(x => x.Primary);
                if (k != null) { OnButton(k.Id, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)); return; }
            }
            if (e.KeyCode == Keys.Escape && Busy != null) return;
            base.OnKeyDown(e);
        }
    }

    /// <summary>4.1: Ein-Klick-Installieren beim Start einer heruntergeladenen EXE, Start-Schutz und Hinweise.</summary>
    internal static class InstallFlow410
    {
        /// <summary>Nur fuer die Vorschau: so tun, als waere diese Version installiert ("alt", "gleich", "neu" oder leer).</summary>
        internal static string DemoInstalled;

        /// <summary>0 = nicht installiert, 1 = aeltere installiert (Update), 2 = genau diese, 3 = neuere installiert.</summary>
        internal static int ModeFor(Update410.ExeInfo installed, bool isInstalled)
        {
            if (!isInstalled) return 0;
            if (installed == null || !installed.IsMischpult) return 1;
            int c = installed.Stamp > 0 && Program.BuildStamp > 0 ? installed.Stamp.CompareTo(Program.BuildStamp) : Update410.CompareVersion(installed.Version, Program.Version);
            return c < 0 ? 1 : c == 0 ? 2 : 3;
        }

        /// <summary>Kurze Liste der Neuheiten seit der installierten Version (fuer das Update-Fenster).</summary>
        internal static string[] NewsTitles(string installed = null)
        {
            var l = new List<string>();
            var parts = new List<string[][]> { WhatsNewWindow.Sections42 };
            if (installed == null || Update410.CompareVersion(installed, "4.1") < 0) parts.Add(WhatsNewWindow.Sections41);   // von 4.0: auch alles aus 4.1
            foreach (var sec in parts)
                foreach (var s in sec)
                    for (int i = 1; i + 1 < s.Length; i += 2) l.Add(s[i]);
            return l.ToArray();
        }

        /// <summary>
        /// Das Fenster beim Start (bzw. nach "--update"). true = das Mischpult startet danach ganz normal aus dieser Datei,
        /// false = fertig (installierte Version gestartet oder abgebrochen).
        /// </summary>
        public static bool Run(MixerSettings pre, bool forced)
        {
            var w = Build(pre, forced, out int mode);
            w.ShowInTaskbar = true;
            Application.Run(w);
            return w.Result == "try";
        }

        internal static AskWin410 Build(MixerSettings pre, bool forced, out int mode)
        {
            string selfPath = Application.ExecutablePath;
            var self = Update410.Read(selfPath) ?? new Update410.ExeInfo { Version = Program.Version, Stamp = Program.BuildStamp, IsMischpult = true };
            bool isInst = Installer.IsInstalled;
            var inst = isInst ? Update410.Read(Installer.InstallExe) : null;
            if (DemoInstalled != null)
            {
                isInst = DemoInstalled.Length > 0;
                int d = DemoInstalled == "alt" ? -60 * 24 * 30 : DemoInstalled == "neu" ? 60 * 24 * 30 : 0;
                inst = isInst ? new Update410.ExeInfo { IsMischpult = true, Version = DemoInstalled == "alt" ? "4.0" : DemoInstalled == "neu" ? "4.2" : Program.Version, Stamp = Math.Max(1, Program.BuildStamp + d) } : null;
                self = new Update410.ExeInfo { IsMischpult = true, Version = Program.Version, Stamp = Program.BuildStamp };
            }
            int m = mode = ModeFor(inst, isInst);
            bool downloaded = DemoInstalled != null || Update410.IsDownloadedFile(selfPath);
            bool backup = pre != null && pre.UpdateBackupOn;
            var w = new AskWin410(m == 0 ? "Mischpult installieren" : m == 1 ? "Mischpult aktualisieren" : "Mischpult");
            switch (m)
            {
                case 0:
                    w.Heading = "Mischpult installieren?";
                    w.Body = "Das Mischpult kommt in deinen Programme-Ordner, ins Startmenü und unter Windows-Einstellungen → Apps – dort lässt es sich auch wieder entfernen. Keine Admin-Rechte nötig.";
                    w.AddCheck("desk", "Verknüpfung auf dem Desktop", true);
                    break;
                case 1:
                    w.Heading = "Update auf Mischpult " + Program.Version;
                    w.Body = "Installiert ist " + (inst?.Text ?? "eine ältere Version") + ", diese Datei ist " + self.Text + ". Deine Einstellungen bleiben, wie sie sind" +
                             (backup ? " – vorher wird alles gesichert, und die alte Version wird aufgehoben." : ".");
                    if (pre != null && pre.UpdateNewsOn) { w.ListTitle = "Neu seit " + (inst?.Version ?? "deiner Version"); w.List = NewsTitles(inst?.Version); }
                    break;
                case 2:
                    w.Heading = "Schon installiert";
                    w.Body = "Genau diese Version (" + self.Text + ") ist schon installiert. Am besten startest du die installierte – die heruntergeladene Datei brauchst du dann nicht mehr.";
                    break;
                default:
                    w.Heading = "Installiert ist eine neuere Version";
                    w.Body = "Installiert ist " + inst.Text + ", diese Datei ist älter (" + self.Text + "). Am besten startest du die installierte Version.";
                    break;
            }
            w.AddCheck("del", "Heruntergeladene Datei danach löschen", true, () => downloaded);
            if (!forced) w.AddCheck("never", "Nicht mehr fragen (wieder einschalten: OPTIONEN → Allgemein)", false);
            if (m == 0) w.AddKey("pkg", "UMZUGS-PAKET …", false, true).AddKey("try", "NUR AUSPROBIEREN").AddKey("go", "INSTALLIEREN", true);
            else if (m == 1) w.AddKey("try", "NUR DIESE DATEI STARTEN").AddKey("go", "AKTUALISIEREN", true);
            else if (m == 2) w.AddKey("try", "DIESE DATEI STARTEN").AddKey("inst", "INSTALLIERTE STARTEN", true);
            else w.AddKey("try", "TROTZDEM DIESE STARTEN").AddKey("inst", "NEUERE STARTEN", true);
            w.Clicked = id => Act(w, id, m, pre, selfPath);
            return w;
        }

        private static void Never(AskWin410 w, MixerSettings pre)
        {
            if (pre == null || !w.IsOn("never")) return;
            try
            {
                var fresh = MixerSettings.Load();   // frisch lesen: eine eben beendete Version hat evtl. gerade gespeichert
                fresh.InstallPromptOff = true;
                fresh.Save();
            }
            catch { }
        }

        private static bool Act(AskWin410 w, string id, int mode, MixerSettings pre, string selfPath)
        {
            if (DemoInstalled != null) return true;   // Vorschau: nichts tun
            if (id == "try") { Never(w, pre); return true; }
            if (id == "inst")
            {
                Never(w, pre);
                Update410.StartInstalled(w.IsOn("del") ? selfPath : null);   // 4.2.4: die installierte Version raeumt auf
                return true;
            }
            string pkg = null;
            if (id == "pkg")
            {
                using (var dlg = new OpenFileDialog { Title = "Umzugs-Paket laden", Filter = "Mischpult-Umzugs-Paket (*" + Package410.Ext + ")|*" + Package410.Ext + "|Alle Dateien|*.*" })
                {
                    if (dlg.ShowDialog(w) != DialogResult.OK) return false;
                    var info = Package410.Read(dlg.FileName);
                    if (info.Error != null) { w.Body = "Dieses Paket passt nicht: " + info.Error; w.Relayout(); return false; }
                    pkg = dlg.FileName;
                }
            }
            if (id != "go" && pkg == null) return true;
            // installieren / aktualisieren
            if (Update410.OtherRunning())
            {
                w.ShowBusy("Die laufende Version wird beendet …");
                Update410.QuitRunning(8000);
            }
            w.ShowBusy(mode == 1 ? "Wird aktualisiert …" : "Wird installiert …");
            if (mode == 1 && pre != null && pre.UpdateBackupOn) Update410.BackupSettings("vor-update-" + Program.Version);
            Never(w, pre);
            bool desk = mode == 0 ? w.IsOn("desk") : File.Exists(Installer.DesktopLink);
            string err = Update410.InstallSelf(desk, mode == 1 && pre != null && pre.UpdateBackupOn);
            if (err == null && pkg != null) err = Package410.Import(pkg);
            if (err != null)
            {
                w.Busy = null;
                w.Heading = "Das hat nicht geklappt";
                w.Body = err + "\n\nTipp: Mischpult über das Symbol unten rechts beenden und nochmal versuchen.";
                w.Relayout();
                return false;
            }
            Update410.StartInstalled(w.IsOn("del") ? selfPath : null);   // 4.2.4: die installierte Version raeumt auf
            return true;
        }

        /// <summary>Start-Schutz: zweimal hintereinander nicht richtig gestartet. Liefert "prev", "safe", "normal" oder null (beenden).</summary>
        public static string AskGuard(int fails, bool canRestore)
        {
            var w = BuildGuard(fails, canRestore);
            w.ShowInTaskbar = true;
            Application.Run(w);
            return w.Result;
        }

        internal static AskWin410 BuildGuard(int fails, bool canRestore)
        {
            var w = new AskWin410("Start-Schutz", 580)
            {
                Heading = "Das Mischpult ist " + (fails == 2 ? "zweimal" : fails + "-mal") + " direkt beim Start ausgestiegen",
                Body = "Wie soll es weitergehen?\n\n• Abgesichert starten: ohne Designs, Effekte und Zusatzfenster – nur für diesen Start, es wird nichts verändert." +
                       (canRestore ? "\n• Vorige Version: die Version vor dem letzten Update zurückholen." : "") +
                       "\n• Normal starten: einfach nochmal versuchen.",
            };
            w.AddKey("normal", "NORMAL STARTEN");
            if (canRestore) w.AddKey("prev", "VORIGE VERSION");
            w.AddKey("safe", "ABGESICHERT STARTEN", true);
            return w;
        }
    }
}
