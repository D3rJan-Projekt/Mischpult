using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0 – Bedienung: eigene Tasten in der Kopfleiste.</summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 186 Eigene Tasten

        /// <summary>Aktion wie bei den Tastenkuerzeln ausfuehren – oder ein Programm starten ("run:Pfad").</summary>
        private void RunAction400(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (action.StartsWith("run:"))
            {
                string path = action.Substring(4);
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { ShowOsd("Starten ging nicht", Path.GetFileName(path) + ": " + ex.Message, -1, Theme.MuteRed); }
                return;
            }
            try { ExecuteHotkey(action); } catch { }
        }

        private string ActionLabel400(string action)
        {
            if (action == null) return "–";
            if (action.StartsWith("run:")) return "Programm starten: " + FileBase(action.Substring(4));
            var a = HotkeyActions().FirstOrDefault(x => x.Id == action);
            return a?.Label ?? action;
        }

        private void ShowTopKeyEditMenu400(int index, Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            var k = _settings.TopKeys != null && index >= 0 && index < _settings.TopKeys.Count ? _settings.TopKeys[index] : null;
            m.Items.Add(Header(k != null ? "Taste „" + k.Label + "“: " + ActionLabel400(k.Action) : "Neue Taste"));
            m.Items.Add(new ToolStripSeparator());
            FillActionMenu400(m.Items, k?.Action, act => BeginInvoke(new Action(() => EditTopKey400(index, act, k?.Label))));
            if (k != null)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Beschriftung ändern …", null, (s, e) => BeginInvoke(new Action(() => EditTopKey400(index, k.Action, null)))));
                if (index > 0) m.Items.Add(new ToolStripMenuItem("Nach links", null, (s, e) => { MoveTopKey400(index, -1); }));
                if (index < _settings.TopKeys.Count - 1) m.Items.Add(new ToolStripMenuItem("Nach rechts", null, (s, e) => { MoveTopKey400(index, 1); }));
                m.Items.Add(new ToolStripMenuItem("Taste entfernen", null, (s, e) => { _settings.TopKeys.RemoveAt(index); if (_settings.TopKeys.Count == 0) _settings.TopKeys = null; RefreshTopKeys400(); ScheduleSave(); }));
            }
            m.Show(screen);
        }

        /// <summary>Alle Aktionen (nach Gruppen) plus "Programm starten …".</summary>
        private void FillActionMenu400(ToolStripItemCollection items, string current, Action<string> pick)
        {
            foreach (var grp in HotkeyActions().GroupBy(a => a.Group))
            {
                var sub = Sub(grp.Key);
                foreach (var a in grp)
                {
                    string id = a.Id;
                    sub.DropDownItems.Add(new ToolStripMenuItem(a.Label, null, (s, e) => pick(id)) { Checked = id == current });
                }
                items.Add(sub);
            }
            items.Add(new ToolStripMenuItem("Programm oder Datei starten …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog { Title = "Was soll die Taste starten?", Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd|Alle Dateien|*.*" })
                    if (dlg.ShowDialog(this) == DialogResult.OK) pick("run:" + dlg.FileName);
            }))) { Checked = current != null && current.StartsWith("run:") });
        }

        private void EditTopKey400(int index, string action, string label)
        {
            if (action == null) return;
            if (label == null)
            {
                string suggest = SuggestKeyLabel(action);
                if (_settings.TopKeys != null && index >= 0 && index < _settings.TopKeys.Count) suggest = _settings.TopKeys[index].Label;
                using (var dlg = new InputDialog("Eigene Taste", "Beschriftung (höchstens 6 Zeichen):", suggest, 6))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    label = dlg.Value;
                }
                if (string.IsNullOrWhiteSpace(label)) return;
            }
            if (_settings.TopKeys == null) _settings.TopKeys = new List<TopKeyConfig>();
            var cfg = new TopKeyConfig { Label = label.Trim().Length > 6 ? label.Trim().Substring(0, 6) : label.Trim(), Action = action };
            if (index >= 0 && index < _settings.TopKeys.Count) _settings.TopKeys[index] = cfg;
            else if (_settings.TopKeys.Count < 6) _settings.TopKeys.Add(cfg);
            _settings.TopKeysOn = true;
            RefreshTopKeys400();
            ScheduleSave();
        }

        internal static string SuggestKeyLabel(string action)
        {
            if (action == null) return "";
            if (action.StartsWith("run:")) { var n = FileBase(action.Substring(4)).ToUpperInvariant(); return n.Length > 6 ? n.Substring(0, 6) : n; }
            switch (action)
            {
                case "mic.mute": return "MIC";
                case "master.mute": return "STUMM";
                case "dim.toggle": return "DIM";
                case "rec.toggle": return "REC";
                case "night.toggle": return "NACHT";
                case "media.play": return "PLAY";
                case "panic": return "PANIK";
            }
            return "TASTE";
        }

        /// <summary>Dateiname ohne Endung (auch mit Windows-Pfad auf anderen Systemen).</summary>
        internal static string FileBase(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string n = i >= 0 ? path.Substring(i + 1) : path;
            int d = n.LastIndexOf('.');
            return d > 0 ? n.Substring(0, d) : n;
        }

        private void MoveTopKey400(int index, int dir)
        {
            var l = _settings.TopKeys;
            int j = index + dir;
            if (l == null || j < 0 || j >= l.Count) return;
            var t = l[index]; l[index] = l[j]; l[j] = t;
            RefreshTopKeys400();
            ScheduleSave();
        }

        private void ShowTopKeysManager400(Rectangle at)
        {
            var m = NewMenu();
            m.ShowCheckMargin = false; m.ShowImageMargin = false;
            int n = _settings.TopKeys?.Count ?? 0;
            for (int i = 0; i < n; i++)
            {
                int k = i;
                var item = Sub(_settings.TopKeys[i].Label + "  –  " + ActionLabel400(_settings.TopKeys[i].Action), false);
                item.DropDownItems.Add(new ToolStripMenuItem("Aktion ändern …", null, (s, e) => BeginInvoke(new Action(() => ShowTopKeyEditMenu400(k, Cursor.Position)))));
                item.DropDownItems.Add(new ToolStripMenuItem("Beschriftung ändern …", null, (s, e) => BeginInvoke(new Action(() => EditTopKey400(k, _settings.TopKeys[k].Action, null)))));
                item.DropDownItems.Add(new ToolStripMenuItem("Entfernen", null, (s, e) => { _settings.TopKeys.RemoveAt(k); if (_settings.TopKeys.Count == 0) _settings.TopKeys = null; RefreshTopKeys400(); ScheduleSave(); }));
                m.Items.Add(item);
            }
            if (n < 6)
            {
                var add = Sub("Neue Taste", false);
                FillActionMenu400(add.DropDownItems, null, act => BeginInvoke(new Action(() => EditTopKey400(-1, act, null))));
                m.Items.Add(add);
            }
            else m.Items.Add(Hint("Höchstens 6 Tasten."));
            m.Items.Add(Hint("Passt es nicht in die Kopfleiste, fallen Tasten weg – Fenster breiter ziehen."));
            ShowMenuAt(m, at);
        }
    }
}
