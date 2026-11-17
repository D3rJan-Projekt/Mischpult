using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>Mikro-Effekte fuer den Sprach-Ausgang: Voreinstellungen, Rumpelfilter, Klarheit, EQ, Kompressor, Pegel. Wirkt sofort.</summary>
    internal sealed class MicFxDialog : DarkDialog
    {
        private readonly CheckBox _hp, _clear;
        private readonly TrackBar _low, _mid, _high, _gain;
        private readonly Label _lowV, _midV, _highV, _gainV;
        private readonly ComboBox _comp;
        private readonly VoiceDialog.LevelMeter _meter;
        private readonly Timer _timer = new Timer { Interval = 40 };
        private readonly Action<VoiceFx> _preview;
        private bool _loading;

        public VoiceFx Result => new VoiceFx(_hp.Checked, _clear.Checked, _low.Value, _mid.Value, _high.Value, _comp.SelectedIndex, _gain.Value);

        private static readonly string[] PresetNames = { "Natürlich", "Deutlich", "Radio", "Aufhellen" };
        private static readonly string[] PresetTips =
        {
            "Alle Effekte aus – dein Mikro klingt wie es ist",
            "Rumpelfilter, Stimme klarer und leichter Kompressor – gut für die meisten Mikros",
            "Volle, gleichmäßig laute Stimme wie im Radio (starker Kompressor, etwas mehr Bass)",
            "Für dumpfe Headset-Mikros: mehr Höhen, weniger Mitten",
        };
        private readonly ToolTip _tips = new ToolTip();
        private static readonly VoiceFx[] Presets =
        {
            VoiceFx.Off,
            new VoiceFx(true, true, 0, 0, 0, 1, 0),
            new VoiceFx(true, true, 3, -1, 1, 2, 1),
            new VoiceFx(true, false, -1, -2, 5, 1, 0),
        };

        public MicFxDialog(VoiceFx fx, bool voiceOn, Func<float> level, Action<VoiceFx> preview) : base("Mikro-Effekte – Mischpult", 500, voiceOn ? 548 : 600)
        {
            _preview = preview;
            AddLabel("Die Effekte wirken im Sprach-Ausgang – also auf das, was Discord über das virtuelle Kabel von dir hört. " +
                     "Änderungen sind sofort aktiv: Zum Anhören in Discord unter Sprache & Video den Mikrofontest starten.",
                16, 12, 470, 58, false, Theme.TextDim);
            int y = 74;
            if (!voiceOn)
            {
                AddLabel("Der Sprach-Ausgang ist aus – ohne ihn kommen die Effekte nicht bei Discord an.", 16, y, 300, 40, false, Theme.WarnText);
                AddButton("Sprach-Ausgang einrichten …", 322, y + 2, 162, DialogResult.Retry, false);
                y += 52;
            }

            AddLabel("Voreinstellung:", 16, y, 300, 20, true);
            y += 24;
            for (int i = 0; i < Presets.Length; i++)
            {
                int k = i;
                var b = AddButton(PresetNames[i], 16 + i * 118, y, 112, DialogResult.None, false);
                b.Click += (o, e) => Apply(Presets[k]);
                _tips.SetToolTip(b, PresetTips[i]);
            }
            y += 40;

            _hp = DarkControls.Check("Rumpelfilter – Brummen, Wind und Tischklopfen (unter 90 Hz) entfernen", false, 16, y, 470);
            Controls.Add(_hp);
            y += 28;
            _clear = DarkControls.Check("Stimme klarer – Dröhnen absenken, Sprache deutlicher machen", false, 16, y, 470);
            Controls.Add(_clear);
            y += 36;

            AddLabel("Klang", 16, y, 200, 20, true);
            y += 22;
            _low = Slider("Bässe", ref y, out _lowV);
            _mid = Slider("Mitten", ref y, out _midV);
            _high = Slider("Höhen", ref y, out _highV);
            y += 6;

            AddLabel("Kompressor:", 16, y + 3, 110, 22, true);
            _comp = DarkControls.Combo(130, y, 354);
            _comp.Items.AddRange(new object[] { "Aus", "Leicht – gleicht leise und laute Stellen etwas aus", "Stark – gleichmäßig laut wie im Radio" });
            Controls.Add(_comp);
            y += 38;

            _gain = Slider("Lautstärke", ref y, out _gainV, -12, 12);
            y += 4;
            AddLabel("Pegel im Sprach-Ausgang:", 16, y, 300, 20, false, Theme.TextDim);
            y += 22;
            _meter = new VoiceDialog.LevelMeter { Bounds = new Rectangle(Theme.Px(16), Theme.Px(y), Theme.Px(468), Theme.Px(16)) };
            Controls.Add(_meter);
            y += 34;

            var off = AddButton("Alles aus", 16, y, 105, DialogResult.None, false);
            off.Click += (o, e) => Apply(VoiceFx.Off);
            CancelButton = AddButton("Abbrechen", 266, y, 105, DialogResult.Cancel, false);
            AddButton("OK", 380, y, 105, DialogResult.OK, true);

            EventHandler changed = (o, e) => Changed();
            _hp.CheckedChanged += changed; _clear.CheckedChanged += changed;
            _low.ValueChanged += changed; _mid.ValueChanged += changed; _high.ValueChanged += changed; _gain.ValueChanged += changed;
            _comp.SelectedIndexChanged += changed;
            Apply(fx ?? VoiceFx.Off);

            _timer.Tick += (o, e) => { _meter.LevelDb = MeterBallistics.ToDb(level()); _meter.Invalidate(); };
            _timer.Start();
        }

        private TrackBar Slider(string name, ref int y, out Label value, int min = -12, int max = 12)
        {
            AddLabel(name, 16, y + 6, 100, 22);
            var t = new TrackBar
            {
                Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 3, TickFrequency = 3, TickStyle = TickStyle.None,
                BackColor = Theme.MenuBg, Bounds = new Rectangle(Theme.Px(116), Theme.Px(y), Theme.Px(300), Theme.Px(32)),
            };
            Controls.Add(t);
            value = AddLabel("", 422, y + 6, 62, 22, true);
            value.TextAlign = ContentAlignment.TopRight;
            y += 34;
            return t;
        }

        private void Apply(VoiceFx fx)
        {
            _loading = true;
            _hp.Checked = fx.HighPass; _clear.Checked = fx.Clarity;
            _low.Value = (int)Math.Round(fx.Low); _mid.Value = (int)Math.Round(fx.Mid); _high.Value = (int)Math.Round(fx.High);
            _gain.Value = (int)Math.Round(fx.GainDb);
            _comp.SelectedIndex = fx.Comp;
            _loading = false;
            Changed();
        }

        private static string Db(int v) => v == 0 ? "0 dB" : (v > 0 ? "+" : "−") + Math.Abs(v) + " dB";

        private void Changed()
        {
            _lowV.Text = Db(_low.Value); _midV.Text = Db(_mid.Value); _highV.Text = Db(_high.Value); _gainV.Text = Db(_gain.Value);
            if (_loading) return;
            _preview?.Invoke(Result);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _tips.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Helligkeit des grauen Pegels ("was eigentlich laeuft") per Regler, mit Beispiel und Live-Vorschau.</summary>
    internal sealed class GhostDialog : DarkDialog
    {
        private readonly TrackBar _bar;
        private readonly Label _value;
        private readonly Sample _sample;

        public int Value => _bar.Value;

        public GhostDialog(int pct, Action<int> preview) : base("Grauer Pegel – Mischpult", 440, 300)
        {
            AddLabel("Der graue Pegel hinter dem farbigen zeigt, was eigentlich läuft – auch wenn ein Kanal leise oder stumm ist. " +
                     "Hier stellst du ein, wie hell er sein soll. Das Mischpult zeigt die Änderung sofort.",
                16, 12, 410, 58, false, Theme.TextDim);
            _value = AddLabel("", 16, 78, 410, 22, true);
            _bar = new TrackBar
            {
                Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, TickFrequency = 10, TickStyle = TickStyle.BottomRight,
                Value = Math.Max(0, Math.Min(100, pct)), BackColor = Theme.MenuBg,
                Bounds = new Rectangle(Theme.Px(10), Theme.Px(104), Theme.Px(420), Theme.Px(40)),
            };
            Controls.Add(_bar);
            AddLabel("aus", 16, 146, 60, 18, false, Theme.TextFaint);
            var hell = AddLabel("hell", 366, 146, 60, 18, false, Theme.TextFaint);
            hell.TextAlign = ContentAlignment.TopRight;
            _sample = new Sample { Bounds = new Rectangle(Theme.Px(16), Theme.Px(176), Theme.Px(408), Theme.Px(44)) };
            Controls.Add(_sample);

            EventHandler upd = (o, e) =>
            {
                _value.Text = _bar.Value == 0 ? "Helligkeit: aus" : "Helligkeit: " + _bar.Value + " %" + (_bar.Value == Theme.GhostDefault ? "  (Standard)" : "");
                _sample.Pct = _bar.Value;
                _sample.Invalidate();
                preview?.Invoke(_bar.Value);
            };
            _bar.ValueChanged += upd;
            upd(null, null);

            var std = AddButton("Standard", 16, 252, 105, DialogResult.None, false);
            std.Click += (o, e) => _bar.Value = Theme.GhostDefault;
            CancelButton = AddButton("Abbrechen", 206, 252, 105, DialogResult.Cancel, false);
            AddButton("OK", 320, 252, 105, DialogResult.OK, true);
        }

        /// <summary>Beispiel: farbiger Pegel (eingestellt) vor grauem (eigentlich), wie im Kanalzug.</summary>
        private sealed class Sample : Control
        {
            public int Pct;
            public Sample() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.IsLight ? Theme.Hole : Theme.MenuBg);
                const int n = 40;
                float seg = Width / (float)n, h = Theme.F(12);
                for (int row = 0; row < 2; row++)
                {
                    float y = row * (h + Theme.F(6)) + Theme.F(2);
                    int lit = row == 0 ? 17 : 0, pre = row == 0 ? 31 : 27;   // oben: leise gestellt, unten: stumm
                    for (int i = 0; i < n; i++)
                    {
                        float frac = (i + 1f) / n;
                        Color c = frac <= 0.55f ? Theme.MeterGreen : frac <= 0.85f ? Theme.MeterYellow : Theme.MeterRed;
                        Color fill = i < lit ? c
                                   : i < pre && Pct > 0 ? Theme.Mix(Theme.GhostColor(Pct), c, 0.14f)
                                   : Theme.Mix(Theme.SegOff, c, 0.13f);
                        using (var b = new SolidBrush(fill)) g.FillRectangle(b, i * seg + 1, y, seg - 2, h);
                    }
                }
            }
        }
    }

    /// <summary>Sprach-Ausgang einrichten: Mikro (+ Soundboard) auf ein virtuelles Kabel fuer Discord & Co.</summary>
    internal sealed class VoiceDialog : DarkDialog
    {
        private sealed class DevItem
        {
            public string Id, Name;
            public override string ToString() => Name;
        }

        private readonly CheckBox _on, _gate, _pathMute, _warn;
        private readonly ComboBox _dev;
        private readonly TrackBar _gateDb;
        private readonly Label _gateLabel;
        private readonly LevelMeter _level;
        private readonly Timer _timer = new Timer { Interval = 40 };

        public bool VoiceOn => _on.Checked;
        public string Device => (_dev.SelectedItem as DevItem)?.Id;
        public string DeviceName => (_dev.SelectedItem as DevItem)?.Name;
        public bool GateOn => _gate.Checked;
        public float GateDb => _gateDb.Value;
        public bool PathMute => _pathMute.Checked;
        public bool MuteWarn => _warn.Checked;

        public VoiceDialog(MixerSettings s, DeviceState[] outputs, Func<float> voiceLevel, bool supported) : base("Sprach-Ausgang – Mischpult", 480, 560)
        {
            AddLabel("Der Sprach-Ausgang schickt dein Mikro – mit Rauschsperre und Soundboard-Sounds – auf ein eigenes Gerät. " +
                     "Mit einem virtuellen Kabel (z. B. das kostenlose VB-Cable) nimmt Discord dann genau diesen Ton als Mikrofon.",
                16, 12, 450, 76, false, Theme.TextDim);
            _on = DarkControls.Check("Sprach-Ausgang einschalten", s.VoiceOn, 16, 92, 440, true);
            Controls.Add(_on);

            AddLabel("Ausgabe auf:", 16, 128, 440, 22, true);
            _dev = DarkControls.Combo(16, 152, 448);
            int sel = -1;
            foreach (var d in outputs)
            {
                if (string.Equals(d.Id, s.VoiceDevice, StringComparison.OrdinalIgnoreCase)) sel = _dev.Items.Count;
                _dev.Items.Add(new DevItem { Id = d.Id, Name = d.FullName });
            }
            if (sel < 0 && s.VoiceDevice != null) { sel = _dev.Items.Count; _dev.Items.Add(new DevItem { Id = s.VoiceDevice, Name = (s.VoiceDeviceName ?? "Gerät") + "  (nicht angeschlossen)" }); }
            if (sel < 0) sel = outputs.ToList().FindIndex(d => (d.FullName ?? "").IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                               (d.FullName ?? "").IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                               (d.FullName ?? "").IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0);
            if (sel >= 0 && sel < _dev.Items.Count) _dev.SelectedIndex = sel;
            Controls.Add(_dev);

            bool hasCable = outputs.Any(d => (d.FullName ?? "").IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0 || (d.FullName ?? "").IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0);
            var info = AddLabel(hasCable
                ? "In Discord: Einstellungen → Sprache & Video → Eingabegerät »CABLE Output (VB-Audio Virtual Cable)«."
                : "Kein virtuelles Kabel gefunden. VB-Cable gibt es kostenlos unter vb-audio.com/Cable – danach hier »CABLE Input« wählen und in Discord »CABLE Output« als Eingabegerät.",
                16, 184, 450, 52, false, hasCable ? Theme.TextDim : Theme.WarnText);
            if (!hasCable)
            {
                var link = new LinkLabel
                {
                    Text = "vb-audio.com/Cable öffnen", LinkColor = Theme.AccentText, ActiveLinkColor = Theme.Text, BackColor = Color.Transparent,
                    Bounds = new Rectangle(Theme.Px(16), Theme.Px(236), Theme.Px(300), Theme.Px(22)),
                };
                link.LinkClicked += (o, e) => { try { Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }); } catch { } };
                Controls.Add(link);
            }

            _gate = DarkControls.Check("Rauschsperre (Noise Gate): leise Geräusche ausblenden", s.VoiceGateOn, 16, 266, 450);
            Controls.Add(_gate);
            _gateLabel = AddLabel("", 34, 292, 300, 22);
            _gateDb = new TrackBar
            {
                Minimum = -70, Maximum = -20, Value = (int)Math.Round(Math.Max(-70, Math.Min(-20, s.VoiceGateDb))), TickStyle = TickStyle.None, BackColor = Theme.MenuBg,
                Bounds = new Rectangle(Theme.Px(28), Theme.Px(312), Theme.Px(436), Theme.Px(32)),
            };
            Controls.Add(_gateDb);
            _level = new LevelMeter { Bounds = new Rectangle(Theme.Px(34), Theme.Px(346), Theme.Px(424), Theme.Px(16)) };
            Controls.Add(_level);
            EventHandler upd = (o, e) => { _gateLabel.Text = "Schwelle: " + _gateDb.Value + " dB (der Balken zeigt dein Mikro)"; _level.ThresholdDb = _gateDb.Value; _level.Gate = _gate.Checked; };
            _gateDb.ValueChanged += upd; _gate.CheckedChanged += upd;
            upd(null, null);

            _pathMute = DarkControls.Check("Stumm / Push-to-Talk nur im Sprach-Ausgang", s.VoiceMuteInPath, 16, 376, 450);
            Controls.Add(_pathMute);
            AddLabel("Das Windows-Mikro bleibt dann für andere Apps an (z. B. Ingame-Voice-Chat). Nötig, damit der Stumm-Hinweis sicher funktioniert.",
                34, 400, 430, 40, false, Theme.TextDim);
            _warn = DarkControls.Check("Hinweis, wenn ich spreche, obwohl ich stumm bin", !s.MuteWarnOff, 16, 446, 450);
            Controls.Add(_warn);

            CancelButton = AddButton("Abbrechen", 246, 512, 105, DialogResult.Cancel, false);
            var ok = AddButton("OK", 360, 512, 105, DialogResult.OK, true);
            ok.Click += (o, e) =>
            {
                if (VoiceOn && !supported)
                {
                    MessageBox.Show(this, "Der Sprach-Ausgang braucht Windows 10 Version 2004 oder neuer.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
                else if (VoiceOn && Device == null)
                {
                    MessageBox.Show(this, "Bitte ein Ausgabegerät wählen (z. B. »CABLE Input«).", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };

            _timer.Tick += (o, e) => { _level.LevelDb = MeterBallistics.ToDb(voiceLevel()); _level.Invalidate(); };
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Waagerechter Pegel mit Schwelle.</summary>
        internal sealed class LevelMeter : Control
        {
            public float LevelDb = -100, ThresholdDb = -45;
            public bool Gate;
            private float _shown = -100;

            public LevelMeter() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                g.Clear(Theme.MenuBg);
                _shown = Math.Max(LevelDb, _shown - 1.5f);
                var r = new RectangleF(0, Theme.F(2), Width - 1, Height - Theme.F(5));
                Theme.FillRound(g, Theme.Deep, r, Theme.F(3));
                float frac(float db) => Math.Max(0, Math.Min(1, (db + 80f) / 80f));
                bool open = !Gate || _shown > ThresholdDb;
                Theme.FillRound(g, open ? Theme.StatusGreen : Theme.TextFaint, new RectangleF(r.X, r.Y, Math.Max(0, r.Width * frac(_shown)), r.Height), Theme.F(3));
                if (Gate)
                {
                    float tx = r.X + r.Width * frac(ThresholdDb);
                    using (var pen = new Pen(Theme.IsLight ? Theme.Text : Color.White, Theme.F(2))) g.DrawLine(pen, tx, 0, tx, Height);
                }
            }
        }
    }
}
