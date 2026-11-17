using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>Szenen nach Uhrzeit: Liste der Eintraege (an/aus per Haekchen), neuer Eintrag mit Szene, Uhrzeit und Wochentagen.</summary>
    internal sealed class ScheduleDialog : DarkDialog
    {
        private readonly List<Scene> _scenes;
        private readonly List<SceneSchedule> _items;
        private readonly CheckedListBox _list;
        private readonly ComboBox _scene;
        private readonly NumericUpDown _hour, _min;
        private readonly CheckBox[] _days = new CheckBox[7];
        private readonly Label _hint;
        private bool _filling;

        public List<SceneSchedule> Result { get; private set; }

        public ScheduleDialog(List<Scene> scenes, IEnumerable<SceneSchedule> current) : base("Szenen nach Uhrzeit – Mischpult", 470, 486)
        {
            _scenes = scenes;
            _items = current.Select(e => new SceneSchedule { SceneId = e.SceneId, Minute = e.Minute, Days = e.Days, Off = e.Off }).ToList();

            AddLabel("Lädt eine Szene automatisch zur eingestellten Uhrzeit – z. B. ab 22:00 „Nacht“ und morgens wieder normal. " +
                     "Häkchen = Eintrag aktiv.", 16, 12, 440, 44, false, Theme.TextDim);

            _list = new CheckedListBox
            {
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(60), Theme.Px(438), Theme.Px(150)),
                BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                IntegralHeight = false, CheckOnClick = true, Font = Theme.Ui(12.5f),
            };
            _list.ItemCheck += (o, e) =>
            {
                if (_filling || e.Index < 0 || e.Index >= _items.Count) return;
                _items[e.Index].Off = e.NewValue != CheckState.Checked;
            };
            Controls.Add(_list);

            var del = AddButton("Ausgewählten entfernen", 16, 218, 200, DialogResult.None, false);
            del.Click += (o, e) =>
            {
                int i = _list.SelectedIndex;
                if (i < 0 || i >= _items.Count) return;
                _items.RemoveAt(i);
                Fill();
            };

            AddLabel("NEUER EINTRAG", 16, 264, 300, 20, true, Theme.TextFaint);
            AddLabel("Szene", 16, 292, 70, 24);
            _scene = DarkControls.Combo(86, 289, 172);
            foreach (var sc in scenes) _scene.Items.Add(sc.Name);
            if (_scene.Items.Count > 0) _scene.SelectedIndex = 0;
            Controls.Add(_scene);

            AddLabel("Uhrzeit", 272, 292, 62, 24);
            _hour = Num(23, 334, 289);
            AddLabel(":", 391, 291, 10, 24, true);
            _min = Num(59, 402, 289);
            _hour.Value = 22;

            for (int i = 0; i < 7; i++)
            {
                _days[i] = DarkControls.Check(SceneSchedule.DayNames[i], true, 16 + i * 62, 326, 58);
                Controls.Add(_days[i]);
            }

            var add = AddButton("+  Eintrag hinzufügen", 16, 364, 200, DialogResult.None, false);
            add.Click += (o, e) => AddEntry();
            _hint = AddLabel("", 226, 368, 230, 24, false, Theme.WarnText);

            CancelButton = AddButton("Abbrechen", 240, 438, 100, DialogResult.Cancel, false);
            var ok = AddButton("OK", 350, 438, 104, DialogResult.OK, true);
            ok.Click += (o, e) => Result = _items.OrderBy(x => x.Minute).ToList();
            AcceptButton = ok;

            Fill();
        }

        private NumericUpDown Num(int max, int x, int y)
        {
            var n = new NumericUpDown
            {
                Minimum = 0, Maximum = max, Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(54), Theme.Px(26)),
                BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center,
            };
            Controls.Add(n);
            return n;
        }

        private void AddEntry()
        {
            if (_scene.SelectedIndex < 0 || _scene.SelectedIndex >= _scenes.Count) { _hint.Text = "Bitte eine Szene wählen."; return; }
            int days = 0;
            for (int i = 0; i < 7; i++) if (_days[i].Checked) days |= 1 << i;
            if (days == 0) { _hint.Text = "Mindestens einen Tag wählen."; return; }
            var e = new SceneSchedule { SceneId = _scenes[_scene.SelectedIndex].Id, Minute = (int)_hour.Value * 60 + (int)_min.Value, Days = days };
            if (_items.Any(x => x.Minute == e.Minute && (x.Days & e.Days) != 0 && !x.Off))
            {
                _hint.Text = "Diese Uhrzeit gibt es schon.";
                return;
            }
            _hint.Text = "";
            _items.Add(e);
            var sorted = _items.OrderBy(x => x.Minute).ToList();   // stabil: gleiche Uhrzeit bleibt in der Reihenfolge
            _items.Clear();
            _items.AddRange(sorted);
            Fill();
        }

        private void Fill()
        {
            _filling = true;
            _list.Items.Clear();
            foreach (var e in _items)
            {
                string name = _scenes.FirstOrDefault(s => s.Id == e.SceneId)?.Name ?? "?";
                _list.Items.Add(e.TimeText + "   " + e.DaysText + "   →  " + name, !e.Off);
            }
            for (int i = 0; i < _items.Count; i++) _list.SetItemChecked(i, !_items[i].Off);
            if (_items.Count == 0) _list.Items.Add("(noch keine Einträge)", false);
            _list.Enabled = _items.Count > 0;
            _list.Invalidate();
            _filling = false;
        }
    }

    /// <summary>Ab welchem Pegel SIG (gruen) und PEAK (rot) leuchten. Vorschau sofort im Pult.</summary>
    internal sealed class LedThresholdDialog : DarkDialog
    {
        private readonly TrackBar _sig, _peak;
        private readonly Label _sigText, _peakText;

        public float SigDb => _sig.Value;
        public float PeakDb => _peak.Value;

        public LedThresholdDialog(float sigDb, float peakDb, bool peakFirst, Action<float, float> preview) : base("SIG und PEAK – Mischpult", 470, 356)
        {
            AddLabel("Hier stellst du ein, ab welchem Pegel die Leuchten angehen. Das Pult zeigt die Änderung sofort – " +
                     "am besten mit laufender Musik ausprobieren.", 16, 12, 440, 42, false, Theme.TextDim);

            _sigText = AddLabel("", 16, 62, 440, 22, true, Theme.OkText);
            _sig = Bar((int)MixerSettings.SigLedMin, (int)MixerSettings.SigLedMax, sigDb, 86);
            AddLabel("schon bei sehr leisem Ton", 16, 132, 220, 18, false, Theme.TextFaint);
            var r1 = AddLabel("nur bei deutlichem Ton", 236, 132, 220, 18, false, Theme.TextFaint);
            r1.TextAlign = ContentAlignment.TopRight;

            _peakText = AddLabel("", 16, 166, 440, 22, true, Theme.IsLight ? Color.FromArgb(0xc0, 0x22, 0x1a) : Theme.MeterRed);
            _peak = Bar((int)MixerSettings.PeakLedMin, (int)MixerSettings.PeakLedMax, peakDb, 190);
            AddLabel("früh warnen", 16, 236, 220, 18, false, Theme.TextFaint);
            var r2 = AddLabel("erst kurz vor dem Übersteuern", 236, 236, 220, 18, false, Theme.TextFaint);
            r2.TextAlign = ContentAlignment.TopRight;

            AddLabel("Standard: SIG " + StripBase.DbText(MixerSettings.SigLedDefault) + " · PEAK " + StripBase.DbText(MixerSettings.PeakLedDefault) +
                     ". 0 dB ist die Grenze, ab der es übersteuert.", 16, 266, 440, 20, false, Theme.TextDim);

            EventHandler upd = (o, e) =>
            {
                _sigText.Text = "SIG (grün) leuchtet ab " + StripBase.DbText(_sig.Value) + (_sig.Value == (int)MixerSettings.SigLedDefault ? "   (Standard)" : "");
                _peakText.Text = "PEAK (rot) leuchtet ab " + StripBase.DbText(_peak.Value) + (_peak.Value == (int)MixerSettings.PeakLedDefault ? "   (Standard)" : "");
                preview?.Invoke(_sig.Value, _peak.Value);
            };
            _sig.ValueChanged += upd;
            _peak.ValueChanged += upd;
            upd(null, null);

            var std = AddButton("Standard", 16, 308, 105, DialogResult.None, false);
            std.Click += (o, e) => { _sig.Value = (int)MixerSettings.SigLedDefault; _peak.Value = (int)MixerSettings.PeakLedDefault; };
            CancelButton = AddButton("Abbrechen", 236, 308, 105, DialogResult.Cancel, false);
            AcceptButton = AddButton("OK", 350, 308, 105, DialogResult.OK, true);
            ActiveControl = peakFirst ? _peak : _sig;
        }

        private TrackBar Bar(int min, int max, float value, int y)
        {
            var b = new TrackBar
            {
                Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 5, TickFrequency = 5, TickStyle = TickStyle.BottomRight,
                Value = Math.Max(min, Math.Min(max, (int)Math.Round(value))), BackColor = Theme.MenuBg,
                Bounds = new Rectangle(Theme.Px(10), Theme.Px(y), Theme.Px(450), Theme.Px(40)),
            };
            Controls.Add(b);
            return b;
        }
    }
}
