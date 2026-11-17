using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;
using Mischpult.Native;

namespace Mischpult.UI
{
    internal static class DarkControls
    {
        public static ComboBox Combo(int x, int y, int w) => new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Input, ForeColor = Theme.Text,
            Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(26)),
        };

        public static CheckBox Check(string text, bool on, int x, int y, int w, bool bold = false) => new CheckBox
        {
            Text = text, Checked = on, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
            Font = bold ? Theme.Ui(12f, FontStyle.Bold) : Theme.Ui(12f),
            Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(26)),
        };

        public static RadioButton Radio(string text, bool on, int x, int y, int w) => new RadioButton
        {
            Text = text, Checked = on, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(Theme.Px(x), Theme.Px(y), Theme.Px(w), Theme.Px(26)),
        };
    }

    /// <summary>Einfache Texteingabe (z. B. Name einer Szene).</summary>
    internal sealed class InputDialog : DarkDialog
    {
        private readonly TextBox _box;
        public string Value => _box.Text.Trim();

        public InputDialog(string title, string prompt, string value, int maxLength = 30, bool password = false) : base(title, 380, 150)
        {
            AddLabel(prompt, 16, 14, 350, 24);
            _box = new TextBox
            {
                Text = value ?? "", MaxLength = maxLength, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Ui(13f),
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(44), Theme.Px(348), Theme.Px(28)),
                UseSystemPasswordChar = password,   // 3.5: Passwort
            };
            Controls.Add(_box);
            CancelButton = AddButton("Abbrechen", 150, 104, 100, DialogResult.Cancel, false);
            var ok = AddButton("OK", 262, 104, 102, DialogResult.OK, true);
            AcceptButton = ok;
            _box.TextChanged += (s, e) => ok.Enabled = Value.Length > 0;
            ok.Enabled = Value.Length > 0;
            Shown += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }
    }

    /// <summary>Push-to-Talk / Push-to-Mute einstellen.</summary>
    internal sealed class PttDialog : DarkDialog
    {
        private readonly RadioButton _off, _ptt, _ptm;
        private readonly KeyCaptureBox _key;
        public int Mode => _ptt.Checked ? 1 : _ptm.Checked ? 2 : 0;
        public int Key => _key.Vk;

        public PttDialog(int mode, int vk) : base("Push-to-Talk – Mischpult", 430, 330)
        {
            AddLabel("Steuert das Mikrofon für alle Apps (Discord, Spiele, …) mit einer Taste.", 16, 12, 400, 40, false, Theme.TextDim);
            _off = DarkControls.Radio("Aus – Mikro normal", mode == 0, 16, 56, 390);
            _ptt = DarkControls.Radio("Push-to-Talk – Mikro nur an, solange die Taste gedrückt ist", mode == 1, 16, 84, 400);
            _ptm = DarkControls.Radio("Push-to-Mute – Mikro stumm, solange die Taste gedrückt ist", mode == 2, 16, 112, 400);
            Controls.Add(_off); Controls.Add(_ptt); Controls.Add(_ptm);

            AddLabel("Taste:", 16, 156, 60, 24, true);
            _key = new KeyCaptureBox { Bounds = new Rectangle(Theme.Px(80), Theme.Px(152), Theme.Px(200), Theme.Px(28)), Font = Theme.Mono(12f, FontStyle.Bold) };
            _key.Vk = vk;
            Controls.Add(_key);
            AddLabel("Feld anklicken, dann Taste drücken –\noder Maustaste 4/5 bzw. Mausrad-Klick.", 16, 186, 400, 40, false, Theme.TextDim);
            AddLabel("Die Taste wird nicht blockiert (das Spiel bekommt sie trotzdem). Nach dem Loslassen bleibt das Mikro noch 0,25 s an, damit das letzte Wort nicht abgeschnitten wird.",
                16, 228, 400, 52, false, Theme.TextFaint);

            CancelButton = AddButton("Abbrechen", 196, 288, 105, DialogResult.Cancel, false);
            var ok = AddButton("OK", 310, 288, 105, DialogResult.OK, true);
            ok.Click += (s, e) =>
            {
                if (Mode != 0 && Key == 0)
                {
                    MessageBox.Show(this, "Bitte zuerst eine Taste festlegen.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };
        }

        public static string KeyName(int vk)
        {
            if (vk == 0) return "";
            switch ((Keys)vk)
            {
                case Keys.XButton1: return "Maustaste 4";
                case Keys.XButton2: return "Maustaste 5";
                case Keys.MButton: return "Mausrad-Klick";
                case Keys.ShiftKey: case Keys.LShiftKey: return "Shift";
                case Keys.RShiftKey: return "Shift rechts";
                case Keys.ControlKey: case Keys.LControlKey: return "Strg";
                case Keys.RControlKey: return "Strg rechts";
                case Keys.Menu: case Keys.LMenu: return "Alt";
                case Keys.RMenu: return "Alt Gr";
                case Keys.Capital: return "Feststelltaste";
                case Keys.Tab: return "Tab";
                case Keys.Oemtilde: return "Ö";
                case Keys.Oem5: return "^";
                case Keys.Oem7: return "Ä";
                case Keys.Oem1: return "Ü";
                case Keys.OemBackslash: return "<";
            }
            if (vk >= (int)Keys.F1 && vk <= (int)Keys.F24) return "F" + (vk - (int)Keys.F1 + 1);
            return HotkeyManager.Format(vk & 0xFF);
        }

        /// <summary>Nimmt eine einzelne Taste oder eine Zusatz-Maustaste auf.</summary>
        private sealed class KeyCaptureBox : TextBox
        {
            private int _vk;
            public int Vk { get => _vk; set { _vk = value; Text = value == 0 ? "—" : KeyName(value); } }

            public KeyCaptureBox()
            {
                ReadOnly = true; ShortcutsEnabled = false; TextAlign = HorizontalAlignment.Center;
                BackColor = Theme.Input; ForeColor = Theme.ValueText; BorderStyle = BorderStyle.FixedSingle;
                Cursor = Cursors.Hand;
            }

            protected override bool IsInputKey(Keys keyData) => true;

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); BackColor = Theme.InputFocus; Text = "Taste drücken …"; }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); BackColor = Theme.Input; Vk = _vk; }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                e.SuppressKeyPress = true; e.Handled = true;
                if (e.KeyCode == Keys.Escape) { Vk = _vk; return; }
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) { Vk = 0; return; }
                Vk = (int)e.KeyCode;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (!Focused) return;
                if (e.Button == MouseButtons.XButton1) Vk = (int)Keys.XButton1;
                else if (e.Button == MouseButtons.XButton2) Vk = (int)Keys.XButton2;
                else if (e.Button == MouseButtons.Middle) Vk = (int)Keys.MButton;
            }
        }
    }

    /// <summary>Headset automatisch umschalten.</summary>
    internal sealed class HeadsetDialog : DarkDialog
    {
        private sealed class DevItem
        {
            public string Id, Name; public bool Missing;
            public override string ToString() => Id == null ? Name : Name + (Missing ? "  (nicht angeschlossen)" : "");
        }

        private readonly CheckBox _on;
        private readonly ComboBox _dev, _mic, _fbDev, _fbMic;

        public bool On => _on.Checked;
        public string Device => (_dev.SelectedItem as DevItem)?.Id;
        public string DeviceName => (_dev.SelectedItem as DevItem)?.Name;
        public string Mic => (_mic.SelectedItem as DevItem)?.Id;
        public string MicName => (_mic.SelectedItem as DevItem)?.Name;
        public string Fallback => (_fbDev.SelectedItem as DevItem)?.Id;
        public string FallbackName => (_fbDev.SelectedItem as DevItem)?.Name;
        public string MicFallback => (_fbMic.SelectedItem as DevItem)?.Id;
        public string MicFallbackName => (_fbMic.SelectedItem as DevItem)?.Name;

        public HeadsetDialog(MixerSettings s, DeviceState[] outputs, DeviceState[] inputs) : base("Headset automatisch umschalten – Mischpult", 450, 410)
        {
            AddLabel("Steckst du dein Headset ein, wird es automatisch zum Windows-Standardgerät. Ziehst du es ab, geht der Ton zurück auf das andere Gerät.",
                16, 12, 420, 58, false, Theme.TextDim);
            _on = DarkControls.Check("Automatisch umschalten", s.AutoSwitchOn, 16, 72, 400, true);
            Controls.Add(_on);

            // Vorschlag, solange noch nichts eingestellt ist: Headset-artiges Geraet, zurueck auf Lautsprecher
            string devId = s.AutoDevice, fbId = s.AutoFallback, micId = s.AutoMic;
            if (devId == null)
            {
                var hs = outputs.FirstOrDefault(d => LooksLike(d, "headset", "kopfhörer", "headphone", "arctis", "hyperx", "kraken", "corsair", "logitech", "usb"));
                devId = (hs ?? outputs.FirstOrDefault(d => !d.IsDefault) ?? outputs.FirstOrDefault())?.Id;
                var spk = outputs.FirstOrDefault(d => d.Id != devId && LooksLike(d, "lautsprecher", "speaker", "realtek"));
                if (fbId == null && spk != null) fbId = spk.Id;
                var dev = outputs.FirstOrDefault(d => d.Id == devId);
                string hw = Hardware(dev?.FullName);
                if (micId == null && hw != null) micId = inputs.FirstOrDefault(i => Hardware(i.FullName) == hw)?.Id;
            }

            AddLabel("Wenn dieses Gerät angeschlossen wird:", 16, 108, 420, 22, true);
            _dev = DarkControls.Combo(16, 132, 418);
            Fill(_dev, outputs, devId, s.AutoDeviceName, null);
            AddLabel("… dann dieses Mikrofon verwenden:", 16, 166, 420, 22);
            _mic = DarkControls.Combo(16, 190, 418);
            Fill(_mic, inputs, micId, s.AutoMicName, "(Mikrofon nicht ändern)");

            AddLabel("Wird es getrennt, zurück auf:", 16, 232, 420, 22, true);
            _fbDev = DarkControls.Combo(16, 256, 418);
            Fill(_fbDev, outputs, fbId, s.AutoFallbackName, "(Windows entscheidet)");
            AddLabel("… und dieses Mikrofon:", 16, 290, 420, 22);
            _fbMic = DarkControls.Combo(16, 314, 418);
            Fill(_fbMic, inputs, s.AutoMicFallback, s.AutoMicFallbackName, "(Mikrofon nicht ändern)");
            Controls.AddRange(new Control[] { _dev, _mic, _fbDev, _fbMic });

            CancelButton = AddButton("Abbrechen", 216, 364, 105, DialogResult.Cancel, false);
            var ok = AddButton("OK", 330, 364, 105, DialogResult.OK, true);
            ok.Click += (o, e) =>
            {
                if (On && Device == null)
                {
                    MessageBox.Show(this, "Bitte das Headset auswählen (es muss dafür gerade angeschlossen sein).", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };
        }

        private static bool LooksLike(DeviceState d, params string[] words)
        {
            string n = (d.FullName ?? "").ToLowerInvariant();
            return words.Any(w => n.Contains(w));
        }

        /// <summary>Hardware-Teil eines Geraetenamens: "Kopfhoerer (Arctis 7 Game)" -> "arctis 7".</summary>
        private static string Hardware(string full)
        {
            if (string.IsNullOrEmpty(full)) return null;
            int a = full.IndexOf('('), b = full.LastIndexOf(')');
            if (a < 0 || b <= a) return null;
            string hw = full.Substring(a + 1, b - a - 1).ToLowerInvariant();
            foreach (var w in new[] { " game", " chat", " hands-free", " stereo", " ag audio" }) hw = hw.Replace(w, "");
            return hw.Trim();
        }

        private static void Fill(ComboBox box, DeviceState[] devs, string id, string name, string none)
        {
            if (none != null) box.Items.Add(new DevItem { Id = null, Name = none });
            int sel = 0;
            foreach (var d in devs)
            {
                if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) sel = box.Items.Count;
                box.Items.Add(new DevItem { Id = d.Id, Name = d.FullName });
            }
            if (id != null && !devs.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                sel = box.Items.Count;
                box.Items.Add(new DevItem { Id = id, Name = name ?? "Gerät", Missing = true });
            }
            if (box.Items.Count > 0) box.SelectedIndex = sel;
        }
    }

    /// <summary>MIDI-Controller zuordnen ("MIDI-Learn").</summary>
    internal sealed class MidiDialog : DarkDialog
    {
        public sealed class Target
        {
            public string Id, Label; public bool Continuous;
            public override string ToString() => Label;
        }

        private readonly List<MidiBinding> _bindings;
        private readonly List<Target> _targets;
        private readonly ListBox _list;
        private readonly ComboBox _target;
        private readonly Button _learn;
        private readonly Label _status, _devices, _last;
        private bool _learning;

        public List<MidiBinding> Result => _bindings;

        public MidiDialog(List<MidiBinding> current, List<Target> targets, Func<List<string>> devices, Func<string, string> deviceError)
            : base("MIDI-Controller – Mischpult", 500, 520)
        {
            _bindings = current.Select(b => new MidiBinding { Device = b.Device, Kind = b.Kind, Channel = b.Channel, Number = b.Number, Target = b.Target }).ToList();
            _targets = targets;

            AddLabel("Fader, Drehregler und Tasten eines MIDI-Controllers (z. B. Korg nanoKONTROL, Behringer X-Touch) steuern das Mischpult.",
                16, 12, 470, 40, false, Theme.TextDim);
            _devices = AddLabel("", 16, 54, 380, 40, false, Theme.Text);
            var rescan = AddButton("Neu suchen", 390, 56, 96, DialogResult.None, false);
            rescan.Click += (s, e) => ShowDevices(devices, deviceError);
            ShowDevices(devices, deviceError);

            AddLabel("Neue Zuordnung:", 16, 102, 470, 22, true);
            _target = DarkControls.Combo(16, 128, 340);
            foreach (var t in targets) _target.Items.Add(t);
            if (_target.Items.Count > 0) _target.SelectedIndex = 0;
            Controls.Add(_target);
            _learn = AddButton("Lernen", 366, 126, 120, DialogResult.None, true);
            _learn.Click += (s, e) => SetLearning(!_learning);
            _status = AddLabel(HintText, 16, 160, 470, 22, false, Theme.TextDim);
            _last = AddLabel("", 16, 182, 470, 22, false, Theme.TextFaint);

            AddLabel("Zuordnungen:", 16, 212, 300, 22, true);
            _list = new ListBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text,
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(236), Theme.Px(470), Theme.Px(200)), IntegralHeight = false,
            };
            Controls.Add(_list);
            var remove = AddButton("Entfernen", 16, 446, 110, DialogResult.None, false);
            remove.Click += (s, e) =>
            {
                int i = _list.SelectedIndex;
                if (i >= 0 && i < _bindings.Count) { _bindings.RemoveAt(i); RefreshList(); }
            };
            var clear = AddButton("Alle entfernen", 134, 446, 130, DialogResult.None, false);
            clear.Click += (s, e) => { _bindings.Clear(); RefreshList(); };
            CancelButton = AddButton("Abbrechen", 270, 480, 105, DialogResult.Cancel, false);
            AddButton("OK", 382, 480, 105, DialogResult.OK, true);
            RefreshList();
        }

        private void ShowDevices(Func<List<string>> devices, Func<string, string> deviceError)
        {
            var list = devices();
            if (list.Count == 0) { _devices.Text = "Kein MIDI-Gerät gefunden – Controller anschließen und »Neu suchen« klicken."; _devices.ForeColor = Theme.WarnText; return; }
            _devices.ForeColor = Theme.Text;
            _devices.Text = "Gefunden: " + string.Join(", ", list.Select(d => { string e = deviceError(d); return e != null ? d + " (" + e + ")" : d; }));
        }

        private const string HintText = "Funktion wählen, »Lernen« klicken, dann am Controller bewegen.";

        private void SetLearning(bool on)
        {
            _learning = on;
            _learn.Text = on ? "Abbrechen" : "Lernen";
            _status.Text = on ? "Jetzt einen Regler oder eine Taste am Controller bewegen …" : HintText;
            _status.ForeColor = on ? Theme.WarnText : Theme.TextDim;
        }

        private static string Source(MidiBinding b) =>
            (b.Kind == 0 ? "CC " + b.Number : b.Kind == 1 ? "Taste " + b.Number : "Pitchbend") + " (K" + (b.Channel + 1) + ")";

        private string TargetLabel(string id) => _targets.FirstOrDefault(t => t.Id == id)?.Label ?? id;

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (var b in _bindings) _list.Items.Add(TargetLabel(b.Target) + "   ←   " + Source(b) + " · " + b.Device);
        }

        /// <summary>Vom Mischpult: eine MIDI-Nachricht ist angekommen.</summary>
        public void OnMidi(MidiEvent e)
        {
            var tmp = new MidiBinding { Device = e.Device, Kind = e.Kind, Channel = e.Channel, Number = e.Number };
            _last.Text = "Zuletzt empfangen: " + Source(tmp) + " = " + e.Value + " · " + e.Device;
            if (!_learning) return;
            if (e.Kind == 1 && e.Value == 0) return; // Loslassen ignorieren
            var target = _target.SelectedItem as Target;
            if (target == null) return;
            if (target.Continuous && e.Kind == 1)
            {
                _status.Text = "Für »" + target.Label + "« bitte einen Fader oder Drehregler bewegen (keine Taste).";
                return;
            }
            _bindings.RemoveAll(b => b.Device == e.Device && b.Kind == e.Kind && b.Channel == e.Channel && b.Number == e.Number);
            _bindings.RemoveAll(b => b.Target == target.Id);
            _bindings.Add(new MidiBinding { Device = e.Device, Kind = e.Kind, Channel = e.Channel, Number = e.Number, Target = target.Id });
            RefreshList();
            SetLearning(false);
            _status.Text = "Zugeordnet: " + target.Label + " ← " + Source(tmp);
            _status.ForeColor = Theme.OkText;
            if (_target.SelectedIndex < _target.Items.Count - 1) _target.SelectedIndex++;
        }
    }
}
