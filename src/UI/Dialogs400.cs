using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0: Teil-Szene – Name und welche Kanaele dazugehoeren (Haken), dazu "Ausgangs-Lautstaerke mitspeichern".</summary>
    internal sealed class PartialSceneDialog : DarkDialog
    {
        private readonly TextBox _name;
        private readonly CheckedListBox _list;
        private readonly CheckBox _master;
        private readonly List<KeyValuePair<string, string>> _items;

        public string SceneName => _name.Text.Trim();
        public List<string> Picked => _list.CheckedIndices.Cast<int>().Select(i => _items[i].Key).ToList();
        public bool WithMaster => _master.Checked;

        /// <param name="items">Kanal-ID und Name (Kanaele und Gruppen).</param>
        public PartialSceneDialog(string title, string name, List<KeyValuePair<string, string>> items, ICollection<string> picked, bool withMaster) : base(title, 380, 430)
        {
            _items = items;
            AddLabel("Name der Szene:", 16, 12, 350, 22);
            _name = new TextBox
            {
                Text = name ?? "", MaxLength = 30, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Ui(13f),
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(36), Theme.Px(348), Theme.Px(28)),
            };
            Controls.Add(_name);
            AddLabel("Nur diese Kanäle lädt die Szene – alle anderen bleiben, wie sie sind:", 16, 72, 350, 40);
            _list = new CheckedListBox
            {
                CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Ui(12f),
                Bounds = new Rectangle(Theme.Px(16), Theme.Px(114), Theme.Px(348), Theme.Px(220)), IntegralHeight = false,
            };
            foreach (var it in items) _list.Items.Add(it.Value, picked == null || picked.Contains(it.Key));
            Controls.Add(_list);
            _master = new CheckBox { Text = "Ausgangs-Lautstärke mitspeichern", Checked = withMaster, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.Ui(12f), Bounds = new Rectangle(Theme.Px(16), Theme.Px(340), Theme.Px(348), Theme.Px(24)), UseMnemonic = false };
            Controls.Add(_master);
            CancelButton = AddButton("Abbrechen", 150, 384, 100, DialogResult.Cancel, false);
            var ok = AddButton("Speichern", 262, 384, 102, DialogResult.OK, true);
            AcceptButton = ok;
            Action check = () => ok.Enabled = SceneName.Length > 0 && _list.CheckedIndices.Count > 0;
            _name.TextChanged += (s, e) => check();
            _list.ItemCheck += (s, e) => BeginInvoke(check);
            check();
            Shown += (s, e) => { _name.Focus(); _name.SelectAll(); };
        }
    }
}
