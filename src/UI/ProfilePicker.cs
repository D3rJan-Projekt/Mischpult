using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0: beim Start fragen, mit welchem Profil das Mischpult laeuft (eigene, einfache Farben – das Design ist noch nicht geladen).</summary>
    internal sealed class ProfilePicker : Form
    {
        public const string Cancel = "\u0001";
        private string _result = Cancel;

        private ProfilePicker(List<string> names, string current)
        {
            Text = "Mischpult – wer bist du?";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x1d, 0x1f, 0x24);
            ForeColor = Color.FromArgb(0xe8, 0xea, 0xee);
            Font = new Font("Segoe UI", 11f);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            var all = new List<string> { null };
            all.AddRange(names);
            int w = 320, bh = 40, y = 58;
            var title = new Label { Text = "Profil wählen", Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = false, Bounds = new Rectangle(20, 14, w - 40, 32), ForeColor = ForeColor };
            Controls.Add(title);
            foreach (var n in all)
            {
                string name = n;
                var b = new Button
                {
                    Text = MixerSettings.ProfileDisplay(n) + (n == current ? "   ✓" : ""),
                    Bounds = new Rectangle(20, y, w - 40, bh),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = n == current ? Color.FromArgb(0x3a, 0x33, 0x22) : Color.FromArgb(0x2a, 0x2d, 0x34),
                    ForeColor = ForeColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 0, 0),
                    Cursor = Cursors.Hand,
                };
                b.FlatAppearance.BorderColor = n == current ? Color.FromArgb(0xff, 0xb0, 0x30) : Color.FromArgb(0x44, 0x48, 0x52);
                b.Click += (s, e) => { _result = name; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(b);
                if (n == current) AcceptButton = b;
                y += bh + 8;
            }
            var hint = new Label { Text = "Ändern: OPTIONEN → Sichern → Profile", AutoSize = false, Bounds = new Rectangle(20, y + 4, w - 40, 24), ForeColor = Color.FromArgb(0x9a, 0xa0, 0xaa), Font = new Font("Segoe UI", 9f) };
            Controls.Add(hint);
            ClientSize = new Size(w, y + 36);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { _result = Cancel; Close(); }
        }

        /// <summary>Profil-Name (null = Standard) oder <see cref="Cancel"/>.</summary>
        public static string Ask()
        {
            try
            {
                var names = MixerSettings.ListProfiles();
                if (names.Count == 0) return null;
                using (var f = new ProfilePicker(names, MixerSettings.Profile))
                {
                    f.ShowDialog();
                    return f._result;
                }
            }
            catch { return null; }
        }
    }
}
