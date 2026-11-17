using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    internal static class DarkTitle
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dunkle (bzw. beim Stil "Hell" helle) Fenster-Titelleiste (Windows 10 20H1+ / 11).</summary>
        public static void Apply(IntPtr hwnd)
        {
            if (!Win32.IsWindows) return;
            try
            {
                int on = Theme.IsLight ? 0 : 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }
    }

    /// <summary>Kurze Einblendung unten auf dem Bildschirm (z. B. bei Hotkeys).</summary>
    internal sealed class OsdForm : Form
    {
        private string _title = "", _value = "";
        private float _bar = -1f;
        private Color _accent = Theme.Accent;
        private readonly Timer _timer = new Timer { Interval = 30 };
        private readonly System.Diagnostics.Stopwatch _shown = new System.Diagnostics.Stopwatch();

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            Size = new Size(Theme.Px(300), Theme.Px(74));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer.Tick += (s, e) =>
            {
                if (_shown.ElapsedMilliseconds < 1400) return;
                double o = Opacity - 0.08;
                if (o <= 0.05) { _timer.Stop(); Hide(); Opacity = 0.97; }
                else Opacity = o;
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT */ | 0x8 /* TOPMOST */;
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.F(12)))
                Region = new Region(p);
        }

        // ---- 3.1: Lage und Groesse einstellbar
        /// <summary>0 unten Mitte (Standard), 1 oben Mitte, 2 Mitte, 3 unten rechts, 4 oben rechts, 5 unten links, 6 oben links.</summary>
        public static int Position;
        /// <summary>0 normal, 1 klein, 2 gross.</summary>
        public static int SizeMode;
        public static readonly string[] PositionNames = { "Unten Mitte", "Oben Mitte", "Mitte", "Unten rechts", "Oben rechts", "Unten links", "Oben links" };
        public static readonly string[] SizeNames = { "Normal", "Klein", "Groß" };
        private static float K => SizeMode == 1 ? 0.8f : SizeMode == 2 ? 1.35f : 1f;

        public void ShowMessage(string title, string value, float bar, Color accent)
        {
            _title = title ?? ""; _value = value ?? ""; _bar = bar; _accent = accent;
            var want = new Size((int)Math.Round(Theme.Px(300) * K), (int)Math.Round(Theme.Px(74) * K));
            if (Size != want) Size = want;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int m = Theme.Px(24), mb = Theme.Px(70);
            int cx = wa.X + (wa.Width - Width) / 2, cy = wa.Y + (wa.Height - Height) / 2;
            int left = wa.X + m, right = wa.Right - Width - m, top = wa.Y + m + Theme.Px(16), bottom = wa.Bottom - Height - mb;
            switch (Position)
            {
                case 1: Location = new Point(cx, top); break;
                case 2: Location = new Point(cx, cy); break;
                case 3: Location = new Point(right, bottom); break;
                case 4: Location = new Point(right, top); break;
                case 5: Location = new Point(left, bottom); break;
                case 6: Location = new Point(left, top); break;
                default: Location = new Point(cx, bottom); break;
            }
            Opacity = 0.97;
            if (!Visible) Show();
            Invalidate();
            _shown.Restart();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Math.Abs(K - 1f) > 0.01f) { Theme.WithScale(Theme.S * K, () => PaintOsd(e)); return; }   // 3.1: klein/gross
            PaintOsd(e);
        }

        private void PaintOsd(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.StrokeRound(g, Theme.MenuBorder, Theme.F(1), r, Theme.F(12));
            using (var b = new SolidBrush(_accent)) g.FillRectangle(b, Theme.F(14), Theme.F(14), Theme.F(4), Height - Theme.F(28));
            Theme.Label(g, _title.ToUpperInvariant(), new RectangleF(Theme.F(28), Theme.F(10), Width - Theme.F(40), Theme.F(18)), Theme.Left, Theme.TextDim, 10f);
            using (var b = new SolidBrush(Theme.Text))
                Theme.Str(g, _value, Theme.Ui(19f, FontStyle.Bold), b, new RectangleF(Theme.F(26), Theme.F(26), Width - Theme.F(40), Theme.F(28)), Theme.Left);
            if (_bar >= 0)
            {
                var track = new RectangleF(Theme.F(28), Height - Theme.F(14), Width - Theme.F(46), Theme.F(4));
                Theme.FillRound(g, Theme.Track, track, Theme.F(2));
                Theme.FillRound(g, _accent, new RectangleF(track.X, track.Y, Math.Max(Theme.F(4), track.Width * Math.Min(1, _bar)), track.Height), Theme.F(2));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Basis fuer dunkle Dialoge.</summary>
    internal class DarkDialog : Form
    {
        protected DarkDialog(string title, int w, int h)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(12f);
            ClientSize = new Size(Theme.Px(w), Theme.Px(h));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected Button AddButton(string text, int x, int y, int w, DialogResult result, bool primary)
        {
            var b = new Button
            {
                Text = text,
                DialogResult = result,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Theme.Primary : Theme.Button,
                ForeColor = primary ? Color.White : Theme.ButtonText,
                Font = Theme.Ui(12f, FontStyle.Bold),
                Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(30)),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                UseMnemonic = false,
            };
            b.FlatAppearance.BorderColor = Theme.ButtonEdge;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.PrimaryHover : Theme.ButtonHover;
            Controls.Add(b);
            return b;
        }

        protected Label AddLabel(string text, int x, int y, int w, int h, bool bold = false, Color? color = null, Control parent = null)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(h)),
                ForeColor = color ?? Theme.Text,
                BackColor = Color.Transparent,
                Font = bold ? Theme.Ui(12f, FontStyle.Bold) : Theme.Ui(12f),
                UseMnemonic = false,
            };
            (parent ?? this).Controls.Add(l);
            return l;
        }
    }

    internal sealed class HotkeyAction
    {
        public string Id, Label, Group;
        public HotkeyAction(string id, string label, string group) { Id = id; Label = label; Group = group; }
    }

    /// <summary>Tastenkuerzel festlegen.</summary>
    internal sealed class HotkeyDialog : DarkDialog
    {
        private readonly Dictionary<string, HotkeyBox> _boxes = new Dictionary<string, HotkeyBox>();
        public List<HotkeyBinding> Result { get; private set; }

        public HotkeyDialog(IList<HotkeyAction> actions, IList<HotkeyBinding> current, ICollection<string> failed)
            : base("Hotkeys – Mischpult", 470, 560)
        {
            AddLabel("Tastenkürzel funktionieren überall – auch im Spiel.\nFeld anklicken, Kombination drücken. Entf = löschen.",
                16, 12, 440, 40, false, Theme.TextDim);

            var panel = new Panel
            {
                Bounds = new Rectangle(Theme.Px(12), Theme.Px(58), Theme.Px(446), Theme.Px(440)),
                AutoScroll = true,
                BackColor = Theme.Inset,
            };
            Controls.Add(panel);

            int y = 8;
            string group = null;
            foreach (var a in actions)
            {
                if (a.Group != group)
                {
                    group = a.Group;
                    AddLabel(group.ToUpperInvariant(), 8, y + 4, 400, 20, true, Theme.TextFaint, panel);
                    y += 28;
                }
                AddLabel(a.Label, 14, y + 4, 230, 22, false, null, panel);
                var box = new HotkeyBox
                {
                    Bounds = new Rectangle(Theme.Px(250), Theme.Px(y), Theme.Px(170), Theme.Px(26)),
                    Font = Theme.Mono(12f, FontStyle.Bold),
                };
                var cur = current.FirstOrDefault(h => h.Action == a.Id);
                box.Keys = cur?.Keys ?? 0;
                string id = a.Id;
                box.Changed += () => ClearDuplicates(id);
                panel.Controls.Add(box);
                _boxes[a.Id] = box;
                if (failed != null && failed.Contains(a.Id))
                    AddLabel("belegt", 424, y + 5, 40, 20, false, Theme.MuteRed, panel);
                y += 32;
            }

            var reset = AddButton("Standard", 12, 512, 110, DialogResult.None, false);
            reset.Click += (s, e) =>
            {
                var def = MixerSettings.DefaultHotkeys();
                foreach (var kv in _boxes) kv.Value.Keys = def.FirstOrDefault(h => h.Action == kv.Key)?.Keys ?? 0;
            };
            AddButton("Abbrechen", 238, 512, 105, DialogResult.Cancel, false);
            var ok = AddButton("OK", 352, 512, 105, DialogResult.OK, true);
            AcceptButton = null; // Enter soll als Hotkey aufnehmbar bleiben
            CancelButton = null;
            ok.Click += (s, e) =>
            {
                Result = _boxes.Where(kv => kv.Value.Keys != 0)
                               .Select(kv => new HotkeyBinding { Action = kv.Key, Keys = kv.Value.Keys }).ToList();
            };
        }

        private void ClearDuplicates(string changed)
        {
            int k = _boxes[changed].Keys;
            if (k == 0) return;
            foreach (var kv in _boxes)
                if (kv.Key != changed && kv.Value.Keys == k) kv.Value.Keys = 0;
        }
    }

    /// <summary>Ducking einstellen.</summary>
    internal sealed class DuckDialog : DarkDialog
    {
        private readonly CheckBox _on;
        private readonly CheckedListBox _trigger;   // 4.2.1: mehrere Ausloeser
        private readonly CheckedListBox _targets;
        private readonly TrackBar _amount, _threshold, _hold;
        private readonly Label _amountLabel, _thresholdLabel, _holdLabel;
        private readonly LevelBar _level;
        private readonly Timer _timer = new Timer { Interval = 40 };
        private readonly List<ChannelConfig> _channels;
        // 4.2.3: Feinschliff
        private readonly CheckBox _lamps, _minOn, _fadeOn;
        private readonly TrackBar _minTalk, _down, _up;
        private readonly Label _minLabel, _downLabel, _upLabel;
        private readonly LampColumn _lampCol;
        public bool LampsOn => _lamps.Checked;
        public bool MinTalkOn => _minOn.Checked;
        public int MinTalkMs => _minTalk.Value * 100;
        public bool FadeOn => _fadeOn.Checked;
        public int DownMs => _down.Value <= 0 ? 1 : _down.Value * 50;
        public int UpMs => _up.Value * 100;

        public bool DuckOn => _on.Checked;
        /// <summary>Erster Ausloeser (wie bisher).</summary>
        public string Trigger => Triggers[0];
        /// <summary>4.2.1: alle angehakten Ausloeser (mindestens einer), in der Reihenfolge der Kanaele – das Mikrofon (4.2.2) zuletzt.</summary>
        public List<string> Triggers
        {
            get
            {
                var list = new List<string>();
                bool mic = false, sb = false;
                for (int i = 0; i < _trigger.Items.Count; i++)
                {
                    if (!_trigger.GetItemChecked(i)) continue;
                    string id = ((ChannelItem)_trigger.Items[i]).Id;
                    if (id == MixerSettings.DuckMicId) mic = true; else if (id == MixerSettings.DuckSbId) sb = true; else list.Add(id);
                }
                if (mic) list.Add(MixerSettings.DuckMicId);
                if (sb) list.Add(MixerSettings.DuckSbId);   // 4.3
                if (list.Count == 0) list.Add(_channels.Count > 0 ? _channels[0].Id : "");
                return list;
            }
        }
        public List<string> Targets
        {
            get
            {
                var list = new List<string>();
                foreach (var item in _targets.CheckedItems) list.Add(((ChannelItem)item).Id);
                return list;
            }
        }
        public float Amount => _amount.Value;
        public float Threshold => _threshold.Value;
        public float Hold => _hold.Value / 10f;

        private sealed class ChannelItem
        {
            public string Id, Name;
            public ChannelItem(ChannelConfig c) { Id = c.Id; Name = c.Name; }
            public ChannelItem(string id, string name) { Id = id; Name = name; }
            public override string ToString() => Name;
        }

        private readonly CheckBox _leak;
        private readonly Label _info;
        public bool LeakFilter => _leak.Checked;

        public DuckDialog(MixerSettings s, Func<string, float> levelDbOf, Func<string> info, Func<string, float, int> lampOf = null) : base("Ducking – Mischpult", 776, 646)
        {
            _channels = s.Channels.ToList();

            AddLabel("Ducking macht Kanäle automatisch leiser, solange in einem anderen Kanal Ton ist – z. B. Musik leiser, wenn im Discord jemand spricht. Auslöser kannst du mehrere anhaken – auch dein MIKRO.",
                16, 12, 410, 60, false, Theme.TextDim);

            _on = new CheckBox
            {
                Text = "Ducking einschalten", Checked = s.DuckOn, ForeColor = Theme.Text, Font = Theme.Ui(12f, FontStyle.Bold),
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(78), Theme.Px(300), Theme.Px(26)), FlatStyle = FlatStyle.Flat,
            };
            Controls.Add(_on);

            // 4.2.1: Ausloeser zum Anhaken (auch mehrere) – links die Ausloeser, rechts die Kanaele, die leiser werden
            AddLabel("Auslöser – wenn hier Ton ist:", 16, 114, 204, 22, true);
            _trigger = new CheckedListBox
            {
                CheckOnClick = true, BorderStyle = BorderStyle.None,
                BackColor = Theme.Input, ForeColor = Theme.Text,
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(138), Theme.Px(180), Theme.Px(150)),
            };
            var trig = s.DuckTriggerList;
            // 4.2.2: dein Mikrofon ganz oben – dann wird es leiser, wenn du selbst sprichst
            _trigger.Items.Add(new ChannelItem(MixerSettings.DuckMicId, "MIKRO (du sprichst)"), trig.Contains(MixerSettings.DuckMicId));
            _trigger.Items.Add(new ChannelItem(MixerSettings.DuckSbId, "SOUNDBOARD (Sounds)"), trig.Contains(MixerSettings.DuckSbId));   // 4.3
            foreach (var c in _channels) _trigger.Items.Add(new ChannelItem(c), trig.Contains(c.Id));
            if (_trigger.CheckedItems.Count == 0 && _trigger.Items.Count > 2) _trigger.SetItemChecked(2, true);
            Controls.Add(_trigger);
            // 4.2.3: Laempchen rechts neben den Ausloesern
            _lampCol = new LampColumn { List = _trigger, BackColor = Theme.Input, Bounds = new Rectangle(Theme.Px(196), Theme.Px(138), Theme.Px(20), Theme.Px(150)) };
            _lampCol.State = i =>
            {
                if (lampOf == null || i < 0 || i >= _trigger.Items.Count) return 0;
                int st = lampOf(((ChannelItem)_trigger.Items[i]).Id, _threshold.Value);
                return _trigger.GetItemChecked(i) ? st : Math.Min(1, st);   // nicht angehakt: loest (noch) nicht aus
            };
            Controls.Add(_lampCol);

            AddLabel("… werden diese Kanäle leiser:", 228, 114, 200, 22, true);
            _targets = new CheckedListBox
            {
                CheckOnClick = true, BorderStyle = BorderStyle.None,
                BackColor = Theme.Input, ForeColor = Theme.Text,
                Bounds = new Rectangle(Theme.Px(228), Theme.Px(138), Theme.Px(196), Theme.Px(150)),
            };
            Controls.Add(_targets);
            FillTargets(s.DuckTargets);
            _trigger.ItemCheck += (o, e) =>
            {
                // mindestens ein Ausloeser bleibt
                if (e.NewValue == CheckState.Unchecked && _trigger.CheckedItems.Count <= 1) { e.NewValue = CheckState.Checked; return; }
                var keep = Targets;
                BeginInvoke(new Action(() => FillTargets(keep)));   // erst nach dem Haken: dann stimmt die Liste der Ausloeser
            };

            _level = new LevelBar { Bounds = new Rectangle(Theme.Px(16), Theme.Px(296), Theme.Px(408), Theme.Px(18)) };
            Controls.Add(_level);

            _amountLabel = AddLabel("", 16, 324, 400, 22, true);
            _amount = MakeTrack(-30, -3, (int)Math.Round(s.DuckAmount), 348);
            _thresholdLabel = AddLabel("", 16, 390, 400, 22, true);
            _threshold = MakeTrack(-70, -15, (int)Math.Round(s.DuckThreshold), 414);
            _holdLabel = AddLabel("", 16, 456, 400, 22, true);
            _hold = MakeTrack(2, 30, (int)Math.Round(s.DuckHold * 10), 480);
            EventHandler upd = (o, e) => UpdateLabels();
            _amount.ValueChanged += upd; _threshold.ValueChanged += upd; _hold.ValueChanged += upd;
            UpdateLabels();

            _leak = new CheckBox
            {
                Text = "PC-Ton im Auslöser ignorieren (empfohlen)", Checked = !s.DuckNoLeakFilter, ForeColor = Theme.Text,
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(522), Theme.Px(400), Theme.Px(24)), FlatStyle = FlatStyle.Flat,
            };
            Controls.Add(_leak);
            _info = AddLabel("Für Apps wie Discord, die den Ton des PCs mit abgreifen: dann löst nur eine echte Stimme aus.",
                34, 546, 390, 36, false, Theme.TextDim);

            // ---- 4.2.3: Feinschliff (rechte Spalte)
            Controls.Add(new Panel { BackColor = Theme.ButtonEdge, Bounds = new Rectangle(Theme.Px(440), Theme.Px(82), 1, Theme.Px(500)) });
            AddLabel("Feinschliff", 456, 78, 300, 26, true);
            _lamps = MakeCheck("Lämpchen bei den Auslösern", s.DuckLampsOn, 456, 110);
            AddLabel("Punkt neben dem Auslöser: hell = löst gerade aus, schwach = Ton da, zählt aber nicht.", 474, 134, 290, 40, false, Theme.TextDim);
            _minOn = MakeCheck("Kurze Geräusche ignorieren", s.DuckMinTalkOn, 456, 186);
            AddLabel("Tippen, Mausklicks und Pieptöne lösen nicht aus.", 474, 210, 290, 22, false, Theme.TextDim);
            _minLabel = AddLabel("", 456, 238, 300, 22, true);
            _minTalk = MakeTrack(1, 10, (int)Math.Round((s.DuckMinTalkMs > 0 ? s.DuckMinTalkMs : 200) / 100.0), 262, 450, 316);
            _fadeOn = MakeCheck("Übergang selbst einstellen", s.DuckFadeOn, 456, 310);
            _downLabel = AddLabel("", 456, 340, 300, 22, true);
            _down = MakeTrack(0, 40, s.DuckDownMs <= 1 && s.DuckDownMs > 0 ? 0 : (int)Math.Round((s.DuckDownMs > 0 ? s.DuckDownMs : 100) / 50.0), 364, 450, 316);
            _upLabel = AddLabel("", 456, 406, 300, 22, true);
            _up = MakeTrack(1, 50, (int)Math.Round((s.DuckUpMs > 0 ? s.DuckUpMs : 600) / 100.0), 430, 450, 316);
            AddLabel("Eigene Stärke pro Auslöser – z. B. MIKRO −6 dB, TS −15 dB – geht mit den Ducking-Regeln: OPTIONEN → Profi-Ton → Ducking-Regeln. Dort gibt es jetzt auch MIKRO als Auslöser.",
                456, 482, 306, 80, false, Theme.TextDim);
            EventHandler upd2 = (o, e) => UpdateFine();
            _minOn.CheckedChanged += upd2; _fadeOn.CheckedChanged += upd2; _minTalk.ValueChanged += upd2; _down.ValueChanged += upd2; _up.ValueChanged += upd2;
            _lamps.CheckedChanged += (o, e) => _lampCol.Invalidate();
            UpdateFine();

            CancelButton = AddButton("Abbrechen", 548, 600, 100, DialogResult.Cancel, false);
            AddButton("OK", 658, 600, 102, DialogResult.OK, true);

            string baseInfo = _info.Text;
            _timer.Tick += (o, e) =>
            {
                string i = info?.Invoke();
                _info.Text = string.IsNullOrEmpty(i) ? baseInfo : i;
                _info.ForeColor = string.IsNullOrEmpty(i) ? Theme.TextDim : Theme.WarnText;
                float lv = -100f;
                foreach (var id in Triggers) lv = Math.Max(lv, levelDbOf(id));   // 4.2.1: der lauteste Ausloeser
                _level.LevelDb = lv;
                _level.ThresholdDb = _threshold.Value;
                _level.Invalidate();
                _lampCol.On = _lamps.Checked && lampOf != null;
                if (_lampCol.On || _lampCol.WasOn) _lampCol.Invalidate();   // 4.2.3
            };
            _timer.Start();
        }

        private void FillTargets(List<string> selected)
        {
            _targets.Items.Clear();
            var trig = Triggers;
            foreach (var c in _channels)
            {
                if (trig.Contains(c.Id)) continue;
                _targets.Items.Add(new ChannelItem(c), selected != null && selected.Contains(c.Id));
            }
        }

        private TrackBar MakeTrack(int min, int max, int value, int y, int x = 10, int w = 420)
        {
            var t = new TrackBar
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                TickStyle = TickStyle.None, BackColor = Theme.MenuBg,
                Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(32)),
            };
            Controls.Add(t);
            return t;
        }

        private CheckBox MakeCheck(string text, bool on, int x, int y)
        {
            var c = new CheckBox
            {
                Text = text, Checked = on, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(304), Theme.Px(24)),
            };
            Controls.Add(c);
            return c;
        }

        /// <summary>4.2.3: Texte und Freigabe der Feinschliff-Regler.</summary>
        private void UpdateFine()
        {
            _minLabel.Text = "Erst ab " + MixerSettings.MsText(MinTalkMs) + " Ton am Stück";
            _downLabel.Text = "Leiser werden dauert: " + MixerSettings.MsText(DownMs);
            _upLabel.Text = "Lauter werden dauert: " + MixerSettings.MsText(UpMs);
            _minTalk.Enabled = _minOn.Checked;
            _down.Enabled = _up.Enabled = _fadeOn.Checked;
            var dim = Theme.TextDim;
            _minLabel.ForeColor = _minOn.Checked ? Theme.Text : dim;
            _downLabel.ForeColor = _upLabel.ForeColor = _fadeOn.Checked ? Theme.Text : dim;
        }

        /// <summary>4.2.3: Laempchen neben der Ausloeser-Liste (die Liste selbst kann man nicht bemalen).</summary>
        private sealed class LampColumn : Control
        {
            public CheckedListBox List;
            public Func<int, int> State;
            public bool On, WasOn;

            public LampColumn()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                WasOn = On;
                if (!On || List == null || State == null || List.Items.Count == 0) return;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float d = Math.Max(6f, Theme.F(8)), cx = Width / 2f;
                var amber = Theme.SoloAmber;
                for (int i = Math.Max(0, List.TopIndex); i < List.Items.Count; i++)
                {
                    Rectangle r;
                    try { r = List.GetItemRectangle(i); } catch { break; }
                    if (r.Top >= List.ClientSize.Height) break;
                    if (r.Bottom <= 0) continue;
                    float cy = r.Top + r.Height / 2f;
                    var dot = new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
                    int st = State(i);
                    if (st >= 2)
                    {
                        using (var glow = new SolidBrush(Theme.Alpha(amber, 70))) g.FillEllipse(glow, cx - d, cy - d, d * 2, d * 2);
                        using (var b = new SolidBrush(amber)) g.FillEllipse(b, dot);
                    }
                    else if (st == 1)
                        using (var p = new Pen(Theme.Alpha(amber, 170), Math.Max(1.2f, Theme.F(1.5f)))) g.DrawEllipse(p, dot);
                    else
                        using (var b = new SolidBrush(Theme.Alpha(Theme.TextFaint, 70))) g.FillEllipse(b, dot);
                }
            }
        }

        private void UpdateLabels()
        {
            _amountLabel.Text = "Wie viel leiser: " + _amount.Value + " dB";
            _thresholdLabel.Text = "Empfindlichkeit: reagiert ab " + _threshold.Value + " dB";
            _holdLabel.Text = "Wieder lauter nach: " + (_hold.Value / 10f).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " s Stille";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Waagerechte Pegelanzeige mit Schwellen-Markierung.</summary>
        private sealed class LevelBar : Control
        {
            public float LevelDb = -100, ThresholdDb = -45;
            public LevelBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                g.Clear(Theme.MenuBg);
                var r = new RectangleF(0, Theme.F(3), Width - 1, Height - Theme.F(7));
                Theme.FillRound(g, Theme.Deep, r, Theme.F(3));
                float frac(float db) => Math.Max(0, Math.Min(1, (db + 70f) / 70f));
                float lv = frac(LevelDb);
                bool over = LevelDb > ThresholdDb;
                Theme.FillRound(g, over ? Theme.SoloAmber : Theme.StatusGreen, new RectangleF(r.X, r.Y, Math.Max(0, r.Width * lv), r.Height), Theme.F(3));
                float tx = r.X + r.Width * frac(ThresholdDb);
                using (var pen = new Pen(Theme.IsLight ? Theme.Text : Color.White, Theme.F(2))) g.DrawLine(pen, tx, 0, tx, Height);
            }
        }
    }
}
