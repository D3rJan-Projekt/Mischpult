using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.18 (3.0 Schritt 2): Kanal-Baenke, gespeicherte Ansichten, automatische Sicherung, Bildschirmschoner
    /// (Vollbild-Visualizer) und Einrichtungs-Assistent. Alles einzeln in den Einstellungen an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Baenke

        public static readonly string[] BankLetters = { "A", "B", "C", "D" };
        private int BankCount => _settings.BankCount ?? 2;

        /// <summary>Steht der Kanal in der gezeigten Bank? (Baenke aus oder Reiter ALLE: jeder)</summary>
        internal static bool InBank(MixerSettings s, ChannelConfig c)
        {
            if (!s.BanksOn || s.CurrentBank <= 0) return true;
            if (s.PinOn && c.Pinned) return true;   // 3.5: angepinnt = in jeder Bank
            int b = c.Bank > (s.BankCount ?? 2) ? 0 : c.Bank;
            return b == 0 || b == s.CurrentBank;
        }

        /// <summary>Kanal ist am Pult zu sehen (nicht ausgeblendet und in der gezeigten Bank).</summary>
        private bool ChannelShown(ChannelConfig c) => !c.Hidden && InBank(_settings, c);

        /// <summary>Eigener Name der Bank (1..4) oder null.</summary>
        private string BankName(int b) =>
            b >= 1 && _settings.BankNames != null && b - 1 < _settings.BankNames.Count ? _settings.BankNames[b - 1] : null;

        /// <summary>Beschriftung: "ALLE", "A" oder "A · SPIEL".</summary>
        internal static string BankLabel(MixerSettings s, int b)
        {
            if (b <= 0) return "ALLE";
            string n = s.BankNames != null && b - 1 < s.BankNames.Count ? s.BankNames[b - 1] : null;
            return BankLetters[Math.Min(3, b - 1)] + (n != null ? " · " + n.ToUpperInvariant() : "");
        }

        private string BankLabel(int b) => BankLabel(_settings, b);

        private void ApplyBankTabs()
        {
            if (_channelHost == null) return;
            if (!_settings.BanksOn) _channelHost.Tabs = null;
            else
            {
                var tabs = new List<string> { "ALLE" };
                for (int b = 1; b <= BankCount; b++) tabs.Add(BankLabel(b));
                _channelHost.Tabs = tabs.ToArray();
                _channelHost.ActiveTab = Math.Min(_settings.CurrentBank, BankCount);
            }
            _channelHost.Invalidate();
        }

        /// <summary>Bank zeigen (0 = alle).</summary>
        public void SetBank(int b, bool osd)
        {
            if (!_settings.BanksOn) return;
            b = Math.Max(0, Math.Min(BankCount, b));
            if (osd) ShowOsd("Bank", BankLabel(b), -1, Theme.Accent);
            if (b == _settings.CurrentBank) return;
            ChannelStrip.Renaming?.EndRename(true);
            _settings.CurrentBank = b;
            ApplyBankTabs();
            BuildChannelStrips();
            _channelHost.ScrollBy(-1000000);
            BridgeLayoutChanged();
            ScheduleSave();
        }

        private void SetBanksOn(bool on)
        {
            if (on == _settings.BanksOn) return;
            ChannelStrip.Renaming?.EndRename(true);
            _settings.BanksOn = on;
            if (on)
            {
                AutoSplitBanks(_settings, false);
                if (_settings.CurrentBank > BankCount) _settings.CurrentBank = 0;
            }
            ApplyBankTabs();
            BuildChannelStrips();
            BridgeLayoutChanged();
            ScheduleSave();
        }

        private void SetBankCount(int n)
        {
            _settings.BankCount = Math.Max(2, Math.Min(4, n));
            if (_settings.CurrentBank > BankCount) _settings.CurrentBank = BankCount;
            ApplyBankTabs();
            BuildChannelStrips();
            ScheduleSave();
        }

        /// <summary>Kanaele gleichmaessig auf die Baenke verteilen (der Sammelkanal steht in allen). force = auch wenn schon verteilt.</summary>
        internal static void AutoSplitBanks(MixerSettings s, bool force)
        {
            if (!force && s.Channels.Any(c => c.Bank != 0)) return;
            int n = s.BankCount ?? 2;
            var list = s.Channels.Where(c => !c.IsCatchAll && !c.Hidden).ToList();
            foreach (var c in s.Channels) c.Bank = 0;
            if (list.Count < 2) return;
            int per = (int)Math.Ceiling(list.Count / (double)n);
            for (int i = 0; i < list.Count; i++) list[i].Bank = Math.Min(n, i / per + 1);
        }

        private void SetChannelBank(ChannelConfig c, int b)
        {
            c.Bank = Math.Max(0, Math.Min(4, b));
            BuildChannelStrips();
            BridgeLayoutChanged();
            ScheduleSave();
        }

        private void RenameBank(int b)
        {
            if (b < 1 || b > 4) return;
            using (var dlg = new InputDialog("Bank " + BankLetters[b - 1] + " benennen", "Name der Bank (z. B. Spiel, Musik, Arbeit) – leer = nur " + BankLetters[b - 1] + ":", BankName(b) ?? ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetBankName(b, dlg.Value);
            }
        }

        private void SetBankName(int b, string name)
        {
            var list = _settings.BankNames ?? new List<string>();
            while (list.Count < 4) list.Add(null);
            list[b - 1] = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            _settings.BankNames = list.All(n => n == null) ? null : list;
            ApplyBankTabs();
            ScheduleSave();
        }

        /// <summary>Kanal-Menue: Bank ▸</summary>
        private ToolStripMenuItem BankSub(ChannelConfig c)
        {
            int cur = c.Bank > BankCount ? 0 : c.Bank;
            var sub = Sub("Bank: " + (cur == 0 ? "alle" : BankLabel(cur)));
            sub.DropDownItems.Add(new ToolStripMenuItem("In allen Bänken", null, (s, e) => SetChannelBank(c, 0)) { Checked = cur == 0 });
            for (int b = 1; b <= BankCount; b++)
            {
                int k = b;
                sub.DropDownItems.Add(new ToolStripMenuItem("Nur in Bank " + BankLabel(b), null, (s, e) => SetChannelBank(c, k)) { Checked = cur == b });
            }
            return sub;
        }

        /// <summary>Rechtsklick auf einen Reiter.</summary>
        private void ShowBankMenu(int b, Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            AddBankMenuItems(m.Items, b);
            m.Show(screen);
        }

        private void AddBankMenuItems(ToolStripItemCollection items, int b)
        {
            items.Add(Header(b == 0 ? "Alle Kanäle" : "Bank " + BankLabel(b)));
            items.Add(new ToolStripSeparator());
            if (b > 0)
            {
                items.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() => RenameBank(b)))));
                var take = Sub("Kanal in diese Bank holen", false);
                foreach (var c in _settings.Channels.Where(x => !x.Hidden && x.Bank != b))
                {
                    var ch = c;
                    take.DropDownItems.Add(new ToolStripMenuItem(c.Name + (c.Bank == 0 || c.Bank > BankCount ? "  (in allen)" : "  (" + BankLetters[c.Bank - 1] + ")"),
                        Swatch(Color.FromArgb(c.Color)), (s, e) => SetChannelBank(ch, b)));
                }
                if (take.DropDownItems.Count == 0) take.DropDownItems.Add(Hint("Alle Kanäle sind schon hier"));
                items.Add(take);
                items.Add(new ToolStripSeparator());
            }
            var count = Sub("Anzahl Bänke");
            for (int n = 2; n <= 4; n++)
            {
                int k = n;
                count.DropDownItems.Add(new ToolStripMenuItem(n + " Bänke (" + string.Join(", ", BankLetters.Take(n)) + ")", null, (s, e) => SetBankCount(k)) { Checked = BankCount == n });
            }
            items.Add(count);
            items.Add(new ToolStripMenuItem("Kanäle neu verteilen", null, (s, e) => { AutoSplitBanks(_settings, true); BuildChannelStrips(); BridgeLayoutChanged(); ScheduleSave(); }));
            items.Add(new ToolStripMenuItem("Bänke ausschalten", null, (s, e) => SetBanksOn(false)));
            items.Add(Hint("Kanal zuordnen: Rechtsklick auf den Kanal → Bank"));
        }

        private string BanksText()
        {
            if (!_settings.BanksOn) return "aus";
            var parts = new List<string>();
            for (int b = 1; b <= BankCount; b++) parts.Add(BankLetters[b - 1] + ": " + _settings.Channels.Count(c => !c.Hidden && c.Bank == b));
            int all = _settings.Channels.Count(c => !c.Hidden && (c.Bank == 0 || c.Bank > BankCount));
            return string.Join(" · ", parts) + (all > 0 ? " · in allen: " + all : "");
        }

        // ================================================================ Ansichten

        /// <summary>Was eine Ansicht speichert: nur Aussehen und Anzeigen – kein Ton, keine Zuordnungen.</summary>
        internal static readonly string[] ViewFields =
        {
            "UiStyle", "ColorScheme", "AccentColor", "MeterColors", "LcdStyle", "ColorCapsOff", "Narrow", "Compact",
            "BridgeOff", "BridgeBarWidth", "BridgeStyle", "BridgeHidden", "BridgePackOff", "BridgeOrder", "BridgeClockOff", "BridgeArrange", "BridgeFreeOrder",
            "SpecOff", "SpecStyle", "GonioOff", "LoudHistOff", "LoudHistMinutes", "OscOff", "CoverOff", "ClockOff", "ClockStyle",
            "ChannelField", "VuOff", "LufsOff", "LufsEachOff", "NowPlayingOff", "ChannelImagesOff", "NumbersOff", "PeakDbOff",
            "PanelMaterial", "CheekStyle", "KnobStyle", "CapStyle", "DigitalPlates", "TapePlates", "LedShape", "LedSize", "VuLight", "VuFace",
            "LampMode", "LampPos", "LampStrength", "AmbilightOff", "BgImagePath", "BgDim", "PultDimMode",
            "HideMic", "MicWithOutputs", "HiddenOutputs", "BanksOn", "BankCount", "CurrentBank", "BankNames",
            "CustomOff", "CustomColors", "Tilt", "ArmRest",
            // 3.1
            "SpecPalette", "VuDual", "KeyShape", "KeyColorsOn", "KeyColors", "CapShape", "BridgeHeight", "LedBright", "AutoIconColor", "SeasonMode", "BigShowOff", "MuteDimOff", "SoloDimOff", "ClipFlashOff", "AutoFoldOn", "AnimStripsOff", "OsdPos", "OsdSize",
            // 3.2
            "FontOn", "FontName", "MeterLowColor", "MeterMidColor", "MeterTopColor", "MeterMidDb", "MeterTopDb", "ChannelWidthOn", "TimeDesignOn", "TimeDayStyle", "TimeNightStyle",
            "AccentSource", "CoverAmbiOn", "Opacity", "OpacityIdleOnly", "Zoom", "ShotButtonOn", "AutoSort",
            // 3.3
            "VuKind", "CoverArt", "ScopeLookOn", "OnAirMode", "KnobRingOn", "CapGlowOn", "DymoPlates", "SymbolsOn", "Wear", "TubeGlowOn", "WarmUpOn",
            "PowerOffShowOn", "Wall", "LiveBg", "SparksOn", "DuckLinesOn", "GroupBracketsOn", "NotesOn", "PultTipsOn",
            // 3.4
            "PcMeterOn", "PaperLookOn", "SpeakersOn", "SunLightOn", "DustCoverOn", "NamePlateOn", "NamePlateText", "TwoToneOn", "StickersOn", "MonoKeyOn",
            // 3.4.3
            "OutputDbOn",
            // 3.4.5
            "DragWidthOff",
        };

        private static string ToJson(object v, Type t)
        {
            if (v == null) return null;
            try
            {
                var ser = new DataContractJsonSerializer(t);
                using (var ms = new MemoryStream()) { ser.WriteObject(ms, v); return Encoding.UTF8.GetString(ms.ToArray()); }
            }
            catch { return null; }
        }

        private static bool FromJson(string json, Type t, out object value)
        {
            value = null;
            if (json == null) return !t.IsValueType || Nullable.GetUnderlyingType(t) != null;
            try
            {
                var ser = new DataContractJsonSerializer(t);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json))) value = ser.ReadObject(ms);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Jetzige Ansicht festhalten.</summary>
        internal static ViewPreset CaptureView(MixerSettings s, string name)
        {
            var v = new ViewPreset { Id = Guid.NewGuid().ToString("N"), Name = name, Values = new Dictionary<string, string>(), Channels = new Dictionary<string, string>() };
            var t = typeof(MixerSettings);
            foreach (var f in ViewFields)
            {
                var fi = t.GetField(f);
                if (fi == null) continue;
                v.Values[f] = ToJson(fi.GetValue(s), fi.FieldType);
            }
            foreach (var c in s.Channels)
                if (c?.Id != null) v.Channels[c.Id] = (c.Hidden ? "1" : "0") + "|" + (c.Field.HasValue ? c.Field.Value.ToString() : "") + "|" + c.Bank;
            return v;
        }

        /// <summary>Ansicht in die Einstellungen uebernehmen (Unbekanntes wird uebergangen).</summary>
        internal static void ApplyViewTo(MixerSettings s, ViewPreset v)
        {
            var t = typeof(MixerSettings);
            if (v.Values != null)
                foreach (var kv in v.Values)
                {
                    if (Array.IndexOf(ViewFields, kv.Key) < 0) continue;
                    var fi = t.GetField(kv.Key);
                    if (fi == null) continue;
                    if (FromJson(kv.Value, fi.FieldType, out object val)) fi.SetValue(s, val);
                }
            if (v.Channels != null)
                foreach (var c in s.Channels)
                {
                    if (c?.Id == null || !v.Channels.TryGetValue(c.Id, out string code) || code == null) continue;
                    var p = code.Split('|');
                    if (p.Length > 0) c.Hidden = p[0] == "1" && !c.IsCatchAll;
                    if (p.Length > 1) c.Field = int.TryParse(p[1], out int fl) ? fl : (int?)null;
                    if (p.Length > 2 && int.TryParse(p[2], out int bk)) c.Bank = bk;
                }
            if (s.Channels.All(c => c.Hidden)) s.Channels[0].Hidden = false;
            s.Normalize();
        }

        /// <summary>Entspricht das Pult gerade dieser Ansicht?</summary>
        internal static bool ViewMatches(MixerSettings s, ViewPreset v)
        {
            var now = CaptureView(s, "");
            foreach (var kv in v.Values)
                if (now.Values.TryGetValue(kv.Key, out string cur) && cur != kv.Value) return false;
            foreach (var kv in v.Channels)
                if (now.Channels.TryGetValue(kv.Key, out string cur) && cur != kv.Value) return false;
            return true;
        }

        private List<ViewPreset> Views => _settings.Views ?? (_settings.Views = new List<ViewPreset>());

        private void SaveCurrentView()
        {
            string suggestion = Views.Count == 0 ? "Zocken" : Views.Count == 1 ? "Musik" : "Ansicht " + (Views.Count + 1);
            using (var dlg = new InputDialog("Ansicht speichern", "Name der Ansicht (z. B. Zocken, Musik, Streamen):", suggestion))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                var same = Views.FirstOrDefault(x => string.Equals(x.Name, dlg.Value, StringComparison.OrdinalIgnoreCase));
                if (same != null)
                {
                    if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Es gibt schon eine Ansicht »" + same.Name + "«. Überschreiben?", "Mischpult",
                            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    OverwriteView(same);
                    return;
                }
                if (Views.Count >= 20) { MessageBox.Show(this, "Höchstens 20 Ansichten – bitte erst eine löschen.", "Mischpult"); return; }
                Views.Add(CaptureView(_settings, dlg.Value));
                ScheduleSave();
                ShowOsd("Ansicht gespeichert", dlg.Value, -1, Theme.LedGreen);
            }
        }

        private void OverwriteView(ViewPreset v)
        {
            var n = CaptureView(_settings, v.Name);
            v.Values = n.Values;
            v.Channels = n.Channels;
            ScheduleSave();
            ShowOsd("Ansicht gespeichert", v.Name, -1, Theme.LedGreen);
        }

        private void RenameView(ViewPreset v)
        {
            using (var dlg = new InputDialog("Ansicht umbenennen", "Neuer Name:", v.Name))
            {
                if (dlg.ShowDialog(_settingsWin ?? (IWin32Window)this) != DialogResult.OK) return;
                v.Name = dlg.Value;
                ScheduleSave();
            }
        }

        private void DeleteView(ViewPreset v)
        {
            Views.Remove(v);
            if (_settings.Views.Count == 0) _settings.Views = null;
            ScheduleSave();
        }

        /// <summary>Ansicht laden: Einstellungen uebernehmen, Pult startet kurz neu (damit wirklich alles passt).</summary>
        public void LoadView(ViewPreset v)
        {
            if (v == null) return;
            if (ViewMatches(_settings, v)) { ShowOsd("Ansicht", v.Name, -1, Theme.Accent); return; }
            if (_demo) { ShowOsd("Ansicht " + v.Name, "Vorschau: wird nicht geladen", -1, Theme.Accent); return; }
            ChannelStrip.Renaming?.EndRename(true);
            ApplyViewTo(_settings, v);
            RememberWindow();
            SaveNow();
            Program.RestartRequested = true;
            ExitApp();
        }

        private ToolStripMenuItem ViewsSub()
        {
            var sub = Sub("Ansichten");
            AddViewItems(sub.DropDownItems);
            return sub;
        }

        private void AddViewItems(ToolStripItemCollection items)
        {
            foreach (var v in Views)
            {
                var vv = v;
                items.Add(new ToolStripMenuItem(v.Name, null, (s, e) => BeginInvoke(new Action(() => LoadView(vv)))) { Checked = ViewMatches(_settings, v) });
            }
            if (Views.Count > 0) items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Jetzige Ansicht speichern …", null, (s, e) => BeginInvoke(new Action(SaveCurrentView))));
            if (Views.Count > 0)
            {
                var over = Sub("Überschreiben mit jetziger", false);
                var ren = Sub("Umbenennen", false);
                var del = Sub("Löschen", false);
                foreach (var v in Views)
                {
                    var vv = v;
                    over.DropDownItems.Add(new ToolStripMenuItem(v.Name, null, (s, e) => OverwriteView(vv)));
                    ren.DropDownItems.Add(new ToolStripMenuItem(v.Name + " …", null, (s, e) => BeginInvoke(new Action(() => RenameView(vv)))));
                    del.DropDownItems.Add(new ToolStripMenuItem(v.Name, null, (s, e) => DeleteView(vv)));
                }
                items.Add(over);
                items.Add(ren);
                items.Add(del);
            }
            items.Add(Hint("Design, Meterbridge, Anzeigen, Bänke – kein Ton."));
            items.Add(Hint("Laden startet das Pult kurz neu."));
        }

        // ================================================================ Automatische Sicherung

        internal static string BackupFolder => MixerSettings.BackupFolder;   // 4.0: Dokumente\Mischpult\Sicherungen
        internal static string BackupFile(string folder, DateTime d) => Path.Combine(folder, "einstellungen-" + d.ToString("yyyy-MM-dd") + ".json");
        private int BackupKeep => _settings.BackupKeep ?? MixerSettings.BackupKeepDefault;
        private double _nextBackupCheck = 8;   // erst kurz nach dem Start

        /// <summary>Sicherungen im Ordner, neueste zuerst.</summary>
        internal static List<string> ListBackups(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return new List<string>();
                return Directory.GetFiles(folder, "einstellungen-*.json").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ThenByDescending(f => f, StringComparer.Ordinal).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Nur die neuesten keep Sicherungen behalten.</summary>
        internal static int PruneBackups(string folder, int keep)
        {
            int n = 0;
            foreach (var f in ListBackups(folder).Skip(Math.Max(1, keep)))
                try { File.Delete(f); n++; } catch { }
            return n;
        }

        /// <summary>Tagessicherung anlegen (einmal am Tag). force = jetzt, auch wenn es schon eine gibt.</summary>
        internal static bool WriteBackup(MixerSettings s, string folder, DateTime now, bool force, int keep)
        {
            string f = BackupFile(folder, now);
            if (!force && File.Exists(f)) return false;
            bool ok = s.SaveTo(f);
            if (ok) PruneBackups(folder, keep);
            return ok;
        }

        private void TickBackup(double now)
        {
            if (now < _nextBackupCheck) return;
            _nextBackupCheck = now + 600;   // alle 10 Minuten nachsehen, ob ein neuer Tag ist
            if (_demo || _settings.AutoBackupOff || _importing || Program.SafeMode) return;   // 4.1: abgesichert nichts sichern
            try { WriteBackup(_settings, BackupFolder, DateTime.Now, false, BackupKeep); } catch { }
        }

        private void BackupNow()
        {
            if (_demo) { ShowOsd("Sicherung", "Vorschau: wird nicht gespeichert", -1, Theme.Accent); return; }
            RememberWindow();
            bool ok = WriteBackup(_settings, BackupFolder, DateTime.Now, true, BackupKeep);
            ShowOsd(ok ? "Gesichert" : "Sichern fehlgeschlagen", ok ? DateTime.Now.ToString("dd.MM.yyyy HH:mm") : "Ordner nicht beschreibbar", -1, ok ? Theme.LedGreen : Theme.MuteRed);
        }

        internal static string BackupLabel(string file)
        {
            try
            {
                var t = File.GetLastWriteTime(file);
                string day = t.Date == DateTime.Today ? "Heute" : t.Date == DateTime.Today.AddDays(-1) ? "Gestern" : t.ToString("ddd dd.MM.yyyy", new System.Globalization.CultureInfo("de-DE"));
                return day + ", " + t.ToString("HH:mm") + " Uhr";
            }
            catch { return Path.GetFileNameWithoutExtension(file); }
        }

        private void AddBackupItems(ToolStripItemCollection items)
        {
            var list = _demo ? DemoBackups() : ListBackups(BackupFolder);
            foreach (var f in list)
            {
                var ff = f;
                items.Add(new ToolStripMenuItem(_demo ? f : BackupLabel(f) + " …", null, (s, e) => BeginInvoke(new Action(() => RestoreBackup(ff)))));
            }
            if (list.Count == 0) items.Add(Hint("Noch keine Sicherung"));
        }

        private static List<string> DemoBackups()
        {
            var de = new System.Globalization.CultureInfo("de-DE");
            return Enumerable.Range(0, 6).Select(i => i == 0 ? "Heute, 09:41 Uhr …" : i == 1 ? "Gestern, 18:02 Uhr …" : DateTime.Today.AddDays(-i).ToString("ddd dd.MM.yyyy", de) + ", 1" + i + ":0" + i + " Uhr …").ToList();
        }

        private void RestoreBackup(string file)
        {
            if (_demo) { MessageBox.Show(this, "Im Demo-Modus wird nichts gespeichert.", "Mischpult"); return; }
            var s = MixerSettings.LoadFrom(file);
            if (s == null) { MessageBox.Show(this, "Diese Sicherung ist beschädigt.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(_settingsWin ?? (IWin32Window)this, "Sicherung von " + BackupLabel(file) + " zurückholen?\n\n" + s.Channels.Count + " Kanäle, " + s.Scenes.Count + " Szenen.\n" +
                    "Die jetzigen Einstellungen werden vorher noch gesichert. Das Mischpult startet dafür neu.", "Mischpult", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            RememberWindow();
            try { _settings.SaveTo(Path.Combine(BackupFolder, "einstellungen-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + "-vorher.json")); } catch { }
            if (!s.SaveTo(MixerSettings.FilePath)) { MessageBox.Show(this, "Die Einstellungen konnten nicht gespeichert werden.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _importing = true;   // beim Beenden nicht ueberschreiben
            Program.RestartRequested = true;
            ExitApp();
        }

        // ================================================================ Bildschirmschoner (Vollbild-Visualizer)

        public static readonly string[] SaverShowNames = { "Meterbridge", "VU-Meter und Spektrum", "Großes Spektrum" };
        public static readonly string[] SaverScreenNames = { "Bildschirm des Pults", "Hauptbildschirm", "Anderer Bildschirm" };
        private bool _saverOn, _saverHadWin, _saverWasFull, _saverCursor;
        private Form _saverWin;
        private long _saverLastIdle;
        private double _nextSaverCheck;

        public bool SaverActive => _saverOn;

        private Screen SaverScreen()
        {
            Screen pult;
            try { pult = Screen.FromControl(this); } catch { pult = Screen.PrimaryScreen; }
            if (_settings.SaverScreen == 1) return Screen.PrimaryScreen;
            if (_settings.SaverScreen == 2) return Screen.AllScreens.FirstOrDefault(s => s.DeviceName != pult.DeviceName) ?? pult;
            return pult;
        }

        /// <summary>Laeuft ein Spiel oder ein Vollbild-Programm im Vordergrund? Dann kein Bildschirmschoner.</summary>
        private bool SaverBlocked(double now)
        {
            if (!Win32.IsWindows) return false;
            try
            {
                IntPtr fg = Win32.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                Win32.GetWindowThreadProcessId(fg, out uint pid);
                if (pid == 0 || pid == OwnPid) return false;
                return PerfNative.IsFullscreen(fg) || IsGamePid(pid, now);
            }
            catch { return false; }
        }

        private void TickSaver(double now)
        {
            if (now < _nextSaverCheck) return;
            _nextSaverCheck = now + 0.4;
            if (_saverOn)
            {
                if (_saverWin == null || _saverWin.IsDisposed || !_saverWin.Visible) { StopSaver(); return; }
                long idle = FullScreen.IdleMs();
                if (idle + 300 < _saverLastIdle) StopSaver();   // Maus oder Taste: weg
                else _saverLastIdle = Math.Max(_saverLastIdle, idle);
                return;
            }
            if (_settings.SaverMinutes <= 0 || _demo || !Win32.IsWindows || _importing) return;
            if (FullScreen.IdleMs() < _settings.SaverMinutes * 60000L) return;
            if (SaverBlocked(now)) return;
            StartSaver();
        }

        /// <summary>Bildschirmschoner zeigen: Meterbridge oder grosses VU-Meter/Spektrum im Vollbild.</summary>
        public void StartSaver()
        {
            if (_saverOn) return;
            Form w;
            if (_settings.SaverShows == 0)
            {
                _saverHadWin = _bridgeWin != null && !_bridgeWin.IsDisposed;
                ShowBridgeWindow();
                w = _bridgeWin;
            }
            else
            {
                _saverHadWin = VuWindowOpen;
                bool vuOff = _settings.VuWinVuOff, specOff = _settings.VuWinSpecOff;   // Einstellungen des VU-Fensters nicht verstellen
                ShowVuWindow(false);
                _settings.VuWinVuOff = vuOff; _settings.VuWinSpecOff = specOff;
                w = _vuWin;
                if (w != null && !_saverHadWin)
                {
                    _vuWin.ShowSpectrum = true;
                    _vuWin.ShowVu = _settings.SaverShows == 1;
                    _vuWin.Restyle();
                }
            }
            if (w == null || w.IsDisposed) return;
            _saverWasFull = FullScreen.IsOn(w);
            FullScreen.Set(w, true, SaverScreen());
            _saverWin = w;
            _saverOn = true;
            _saverLastIdle = FullScreen.IdleMs();
            _nextSaverCheck = Now + 1.0;
            if (Win32.IsWindows && !_saverCursor) { try { Cursor.Hide(); _saverCursor = true; } catch { } }
        }

        public void StopSaver()
        {
            if (!_saverOn) return;
            _saverOn = false;
            var w = _saverWin;
            _saverWin = null;
            if (_saverCursor) { try { Cursor.Show(); } catch { } _saverCursor = false; }
            if (w == null || w.IsDisposed) return;
            if (!_saverHadWin)
            {
                if (w == _bridgeWin) CloseBridgeWindow();
                else w.Close();
            }
            else if (!_saverWasFull) FullScreen.Set(w, false);
        }

        // ================================================================ Einrichtungs-Assistent

        private static readonly string[][] ChannelPresets =
        {
            null,
            new[] { "SPIEL", "CHAT", "MUSIK", "BROWSER" },
            new[] { "SPIEL", "CHAT", "MUSIK", "BROWSER", "ALERTS" },
            new[] { "MUSIK", "ANRUFE", "BROWSER", "VIDEO" },
            new[] { "SPIEL", "MUSIK" },
        };
        private static readonly string[] ChannelPresetNames = { "So lassen", "Zocken", "Zocken & Streamen", "Musik & Arbeit", "Schlicht" };

        /// <summary>Kanaele wie im Vorschlag: vorhandene Kanaele mit gleichem Namen bleiben (mit ihren Apps und Reglern), neue bekommen passende Apps.</summary>
        internal static List<ChannelConfig> BuildPresetChannels(List<ChannelConfig> current, string[] names)
        {
            var defaults = MixerSettings.CreateDefault().Channels;
            var list = new List<ChannelConfig>();
            var catchAll = current.FirstOrDefault(c => c.IsCatchAll) ?? defaults.First(c => c.IsCatchAll);
            list.Add(catchAll);
            int ci = 0;
            foreach (var n in names)
            {
                var keep = current.FirstOrDefault(c => !c.IsCatchAll && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase) && !list.Contains(c));
                if (keep == null && n == "ANRUFE") keep = current.FirstOrDefault(c => !c.IsCatchAll && string.Equals(c.Name, "CHAT", StringComparison.OrdinalIgnoreCase) && !list.Contains(c));
                if (keep != null)
                {
                    keep.Name = n;
                    keep.Hidden = false;
                    list.Add(keep);
                    continue;
                }
                var def = defaults.FirstOrDefault(c => !c.IsCatchAll && string.Equals(c.Name, n == "ANRUFE" ? "CHAT" : n, StringComparison.OrdinalIgnoreCase));
                int color = def?.Color ?? MixerSettings.Palette[(4 + ci++) % (MixerSettings.Palette.Length - 1)];
                var apps = def?.Apps?.ToArray() ?? new string[0];
                if (n == "VIDEO") apps = new[] { "vlc.exe", "mpc-hc64.exe", "mpc-hc.exe", "mpc-be64.exe", "potplayermini64.exe", "wmplayer.exe", "video.ui.exe", "netflix.exe" };
                var nc = ChannelConfig.Create(n, color, apps);
                if (n == "VIDEO") foreach (var m in list.Where(c => !c.IsCatchAll)) m.Apps.RemoveAll(a => apps.Contains(a, StringComparer.OrdinalIgnoreCase));
                list.Add(nc);
            }
            if (names.Contains("VIDEO"))
            {
                var video = list.First(c => c.Name == "VIDEO");
                foreach (var m in list.Where(c => c != video && !c.IsCatchAll)) m.Apps.RemoveAll(a => video.Apps.Contains(a, StringComparer.OrdinalIgnoreCase));
            }
            return list;
        }

        private void ApplyChannelPreset(int preset)
        {
            if (preset <= 0 || preset >= ChannelPresets.Length) return;
            var old = _settings.Channels.ToList();
            var list = BuildPresetChannels(old, ChannelPresets[preset]);
            foreach (var c in old.Where(c => !list.Contains(c)))
            {
                _policyCache.Remove(c.Id);
                if (_settings.GameChannel == c.Id) _settings.GameChannel = null;
                foreach (var g in _settings.Groups) g.Members.Remove(c.Id);
                if (_settings.RecordTarget == "ch:" + c.Id) _settings.RecordTarget = null;
                _settings.Midi.RemoveAll(b => b.Target != null && b.Target.StartsWith("ch:" + c.Id + ":"));
                if (_settings.MicAfter == c.Id) _settings.MicAfter = null;
            }
            _settings.Channels = list;
            if (_settings.GameChannel == null) _settings.GameChannel = list.FirstOrDefault(c => c.Name == "SPIEL")?.Id;
            RebuildAppMap();
            BuildChannelStrips();
            PushPolicy();
            if (!_settings.NoAutoGames) DetectGames();
            RefreshChannelViews();
            ScheduleSave();
        }

        private static readonly string[] DisplayPresetNames = { "So lassen", "Schlicht", "Normal", "Studio" };

        private void ApplyDisplayPreset(int preset)
        {
            if (preset <= 0) return;
            if (preset == 1) { SetBridge(false); SetGlobalField(0, true); return; }
            SetBridge(true);
            SetSpectrumOn(true);
            SetGonio(true);
            SetLoudHist(true);
            SetOscOn(true);
            SetCoverOn(true);
            SetGlobalField(preset == 3 ? 1 : 0, true);
        }

        /// <summary>Einrichtungs-Assistent: Design, Kanaele, Ausgaenge, Anzeige. first = beim ersten Start.</summary>
        public void ShowSetupWizard(bool first)
        {
            int styleBefore = _settings.UiStyle;
            string chNow = string.Join(" · ", _settings.Channels.Where(c => !c.Hidden).Select(c => c.Name));
            string catchName = _settings.Channels.FirstOrDefault(c => c.IsCatchAll)?.Name ?? "SYSTEM";
            var steps = new List<SetupWizard.Step>
            {
                new SetupWizard.Step
                {
                    Title = "Wie soll dein Pult aussehen?",
                    Text = "Ein Klick zeigt das Design gleich am Pult. Farben, Knöpfe, LEDs und vieles mehr gibt es später unter OPTIONEN → Aussehen.",
                    Options = Theme.StyleNames, Hints = Theme.StyleHints, Choice = Math.Max(0, Math.Min(Theme.StyleNames.Length - 1, _settings.UiStyle)),
                    Live = i => SetScheme(_settings.ColorScheme, _settings.AccentColor, i),
                },
                new SetupWizard.Step
                {
                    Title = "Welche Kanäle brauchst du?",
                    Text = "Jeder Kanal ist ein Fader für eine Gruppe von Apps. Bekannte Apps (Discord, Spotify, Browser …) landen von selbst im richtigen Kanal, Spiele werden erkannt. Alles Übrige kommt in " + (_settings.Channels.FirstOrDefault(c => c.IsCatchAll)?.Name ?? "SYSTEM") + ".",
                    Options = ChannelPresetNames,
                    Hints = new[] { chNow }.Concat(ChannelPresets.Skip(1).Select(pr => catchName + " · " + string.Join(" · ", pr))).ToArray(),
                    Choice = 0,
                },
                new SetupWizard.Step
                {
                    Title = "Ein Ausgang oder mehrere?",
                    Text = "Mit mehreren Ausgängen wählst du pro Kanal, wo er spielt – z. B. das Spiel aufs Headset und die Musik auf die Lautsprecher.",
                    Options = new[] { "Ein Ausgang", "Mehrere Ausgänge" },
                    Hints = new[] { "Alles spielt auf dem Windows-Standardgerät (einfach).", "Ein Zug pro Gerät, pro Kanal wählbar." + (_snap.RoutingSupported || _demo ? "" : " (Braucht Windows 10 1803 oder neuer.)") },
                    Choice = MultiOutput ? 1 : 0,
                },
                new SetupWizard.Step
                {
                    Title = "Wie viel soll das Pult anzeigen?",
                    Text = "Alles lässt sich später einzeln an- und ausschalten – oben in der Meterbridge auch mit der Maus anordnen.",
                    Options = DisplayPresetNames,
                    Hints = new[] { "Wie es gerade ist.", "Nur Fader und Pegel, keine Meterbridge – schön kompakt.", "Meterbridge mit Spektrum, Lautheit, Stereo-Bild, Oszilloskop und Uhr.", "Wie Normal, dazu in jedem Kanal die Lautheit." },
                    Choice = 0,
                },
                new SetupWizard.Step
                {
                    Title = "Fertig!",
                    Text = "Tipps: Rechtsklick auf einen Kanal zeigt alle Möglichkeiten. Apps per Klick auf das App-Feld zuordnen. Unter OPTIONEN findest du alles – auch diesen Assistenten (Allgemein → Einrichtungs-Assistent).",
                    Options = new string[0], Hints = new string[0],
                },
            };
            using (var wiz = new SetupWizard(steps, first))
            {
                var res = wiz.ShowDialog(this);
                _settings.WizardDone = true;
                if (res != DialogResult.OK)
                {
                    if (_settings.UiStyle != styleBefore && !first) SetScheme(_settings.ColorScheme, _settings.AccentColor, styleBefore);
                    ScheduleSave();
                    return;
                }
                ApplyChannelPreset(steps[1].Choice);
                bool multi = steps[2].Choice == 1;
                if (multi != MultiOutput && (!multi || _snap.RoutingSupported || _demo)) SetMultiOutput(multi);
                ApplyDisplayPreset(steps[3].Choice);
                ScheduleSave();
            }
        }

        // ================================================================ Einbindung

        private void InitFeatures12()
        {
            _channelHost.TabClicked += i => SetBank(i, false);
            _channelHost.TabMenu += ShowBankMenu;
            ApplyBankTabs();
        }

        private void TickFeatures12(double now)
        {
            TickBackup(now);
            TickSaver(now);
        }

        private void ShownFeatures12()
        {
            if (!_demo && MixerSettings.WasFresh && !_settings.WizardDone && !_startHidden)
                BeginInvoke(new Action(() => { var t = new Timer { Interval = 1800 }; t.Tick += (s, e) => { t.Stop(); t.Dispose(); ShowSetupWizard(true); }; t.Start(); }));
            if (!_demo) return;
            // nur Vorschau
            string bk = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BANKMENU");
            if (int.TryParse(bk, out int bmi)) DemoLater(() => _channelHost.TabMenuDemo(bmi));
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_WIZARD") is string wz && int.TryParse(wz, out int wstep))
                DemoLater(() => { SetupWizard.DemoStep = wstep; ShowSetupWizard(wstep == 0); });
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_SAVER"), out int sv) && sv > 0)
                DemoLater(() => { _settings.SaverShows = sv - 1; StartSaver(); });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VIEWMENU") == "1")
                DemoLater(() =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    m.Items.Add(Header("Ansichten"));
                    m.Items.Add(new ToolStripSeparator());
                    AddViewItems(m.Items);
                    var b = _top.OptionsButtonBounds;
                    m.Show(_top.PointToScreen(new Point(b.Right - m.PreferredSize.Width, b.Bottom + Theme.Px(4))));
                });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BACKUPMENU") == "1")
                DemoLater(() =>
                {
                    var m = NewMenu();
                    m.ShowCheckMargin = false; m.ShowImageMargin = false;
                    m.Items.Add(Header("Sicherung zurückholen"));
                    m.Items.Add(new ToolStripSeparator());
                    AddBackupItems(m.Items);
                    m.Show(PointToScreen(new Point(ClientSize.Width / 2 - Theme.Px(120), Theme.Px(120))));
                });
        }

        /// <summary>Nur Vorschau: Einstellungen fuer die 2.18-Bilder.</summary>
        internal static void DemoFeatures12(MixerSettings s)
        {
            string bank = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BANK");
            if (int.TryParse(bank, out int b))
            {
                s.BanksOn = true;
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BANKCOUNT"), out int bc)) s.BankCount = bc;
                AutoSplitBanks(s, true);
                s.CurrentBank = Math.Max(0, Math.Min(s.BankCount ?? 2, b));
                string names = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_BANKNAMES");
                s.BankNames = string.IsNullOrEmpty(names) ? null : names.Split(',').Select(n => n.Length == 0 ? null : n).ToList();
            }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_VIEWS") == "1")
            {
                s.Views = new List<ViewPreset> { CaptureView(s, "Zocken") };
                var copy = CaptureView(s, "Musik");
                copy.Values["ChannelField"] = "4";
                s.Views.Add(copy);
                var st = CaptureView(s, "Streamen");
                st.Values["BridgeOff"] = "true";
                s.Views.Add(st);
            }
        }

        private ToolStripMenuItem BanksSub()
        {
            var sub = Sub("Kanal-Bänke");
            sub.DropDownItems.Add(new ToolStripMenuItem("Bänke (Reiter über den Kanälen)", null, (s, e) => SetBanksOn(!_settings.BanksOn)) { Checked = _settings.BanksOn });
            if (_settings.BanksOn)
            {
                sub.DropDownItems.Add(new ToolStripSeparator());
                for (int b = 0; b <= BankCount; b++)
                {
                    int k = b;
                    sub.DropDownItems.Add(new ToolStripMenuItem(b == 0 ? "ALLE zeigen" : "Bank " + BankLabel(b) + " zeigen", null, (s, e) => SetBank(k, false)) { Checked = _settings.CurrentBank == b });
                }
                sub.DropDownItems.Add(new ToolStripSeparator());
                AddBankMenuItems(sub.DropDownItems, Math.Max(0, _settings.CurrentBank));
            }
            return sub;
        }

        private void AddHotkeyActions12(List<HotkeyAction> list)
        {
            if (_settings.BanksOn)
            {
                list.Add(new HotkeyAction("bank.next", "Nächste Bank", "Bänke"));
                for (int b = 0; b <= BankCount; b++) list.Add(new HotkeyAction("bank:" + b, b == 0 ? "Alle Kanäle zeigen" : "Bank " + BankLabel(b) + " zeigen", "Bänke"));
            }
            if (!_settings.ViewsOff && _settings.Views != null)
                foreach (var v in _settings.Views) list.Add(new HotkeyAction("view:" + v.Id, "Ansicht laden: " + v.Name, "Ansichten"));
            list.Add(new HotkeyAction("saver.show", "Bildschirmschoner (Vollbild-Visualizer) zeigen", "Allgemein"));
        }

        private bool ExecuteHotkey12(string action)
        {
            if (action == "bank.next") { SetBank((_settings.CurrentBank + 1) % (BankCount + 1), true); return true; }
            if (action.StartsWith("bank:")) { if (int.TryParse(action.Substring(5), out int b)) SetBank(b, true); return true; }
            if (action.StartsWith("view:")) { LoadView(_settings.Views?.FirstOrDefault(v => "view:" + v.Id == action)); return true; }
            if (action == "saver.show") { if (_saverOn) StopSaver(); else StartSaver(); return true; }
            return false;
        }
    
        // ================================================================ Einstellungen

        private void AddWizardSettings(SPage p)
        {
            p.Button("Einrichtungs-Assistent", "Design, Kanäle, Ausgänge und Anzeige in ein paar Schritten einstellen – wie beim ersten Start.", "Starten …",
                r => BeginInvoke(new Action(() => ShowSetupWizard(false))));
        }

        private void AddViewSettings(SPage p)
        {
            p.Section("Ansichten");
            p.Toggle("Ansichten", "Das Aussehen des Pults unter einem Namen speichern (z. B. „Zocken“ und „Musik“) und mit einem Klick wechseln – unter OPTIONEN → Ansichten oder per Hotkey. Gespeichert werden Design, Meterbridge, Anzeigen, Bänke und ausgeblendete Kanäle, kein Ton.",
                () => !_settings.ViewsOff, v => { _settings.ViewsOff = !v; ScheduleSave(); });
            p.Button("Ansichten verwalten", "Speichern, laden, überschreiben, umbenennen, löschen. Laden startet das Pult kurz neu.", "Auswählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                AddViewItems(m.Items);
                ShowMenuAt(m, r);
            }, () => { int n = _settings.Views?.Count ?? 0; return n == 0 ? "keine" : n + (n == 1 ? " Ansicht" : " Ansichten"); }).Enabled = () => !_settings.ViewsOff;
        }

        private void AddSaverSettings(SPage p)
        {
            p.Section("Bildschirmschoner");
            p.Choice("Bildschirmschoner", () => _settings.SaverMinutes == 0 ? "Aus. (Vollbild geht trotzdem: im großen VU-Meter oder in der Meterbridge im eigenen Fenster F11 drücken.)"
                    : "Nach " + _settings.SaverMinutes + " Minuten ohne Maus und Tastatur geht die Meterbridge oder das Spektrum im Vollbild an – nicht, solange ein Spiel oder ein Vollbild-Programm läuft. Maus bewegen oder Taste drücken = weg.",
                new[] { "Aus", "5 Minuten", "10 Minuten", "20 Minuten", "30 Minuten" }, () => Math.Max(0, Array.IndexOf(MixerSettings.SaverMinuteValues, _settings.SaverMinutes)),
                i => { _settings.SaverMinutes = MixerSettings.SaverMinuteValues[i]; ScheduleSave(); });
            p.Choice("Zeigt", () => "Was im Vollbild läuft.", SaverShowNames, () => _settings.SaverShows, i => { _settings.SaverShows = i; ScheduleSave(); });
            p.Choice("Auf", () => Screen.AllScreens.Length > 1 ? "Auf welchem Bildschirm." : "Auf welchem Bildschirm (gerade ist nur einer da).", SaverScreenNames,
                () => _settings.SaverScreen, i => { _settings.SaverScreen = i; ScheduleSave(); });
            p.Button("Vorschau", "Jetzt zeigen – Maus bewegen oder Taste drücken beendet sie.", "Zeigen", r => BeginInvoke(new Action(StartSaver)));
        }

        private void AddBankSettings(SPage p)
        {
            p.Section("Bänke");
            p.Toggle("Kanal-Bänke", "Reiter ALLE · A · B … über den Kanälen: pro Bank nur bestimmte Kanäle, z. B. A zum Zocken, B für Musik und Arbeit. Mausrad auf den Reitern wechselt, Rechtsklick auf einen Reiter = Name und Kanäle. Kanäle „in allen Bänken“ stehen immer da. Kanäle anderer Bänke laufen ganz normal weiter.",
                () => _settings.BanksOn, v => SetBanksOn(v));
            p.Choice("Anzahl Bänke", () => "So viele Reiter neben ALLE.", new[] { "2 (A, B)", "3 (A, B, C)", "4 (A, B, C, D)" }, () => BankCount - 2, i => SetBankCount(i + 2))
                .Visible = () => _settings.BanksOn;
            p.Button("Bänke einteilen", "Welche Kanäle in welcher Bank stehen – oder Rechtsklick auf einen Kanal → Bank.", "Einteilen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = true; m.ShowImageMargin = false;
                foreach (var c in _settings.Channels.Where(x => !x.Hidden))
                {
                    var sub = BankSub(c);
                    sub.Text = c.Name + "  –  " + sub.Text;
                    m.Items.Add(sub);
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Gleichmäßig neu verteilen", null, (s, e) => { AutoSplitBanks(_settings, true); BuildChannelStrips(); BridgeLayoutChanged(); ScheduleSave(); }));
                ShowMenuAt(m, r);
            }, BanksText).Visible = () => _settings.BanksOn;
            p.Button("Namen der Bänke", "Statt A, B … eigene Namen, z. B. SPIEL oder ARBEIT.", "Umbenennen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = false; m.ShowImageMargin = false;
                for (int b = 1; b <= BankCount; b++)
                {
                    int k = b;
                    m.Items.Add(new ToolStripMenuItem("Bank " + BankLabel(b) + " …", null, (s, e) => BeginInvoke(new Action(() => RenameBank(k)))));
                }
                ShowMenuAt(m, r);
            }).Visible = () => _settings.BanksOn;
        }

        private void AddBackupSettings(SPage p)
        {
            p.Section("Automatisch sichern");
            p.Toggle("Täglich automatisch sichern", "Einmal am Tag eine Kopie aller Einstellungen in den Ordner „Sicherungen“ – falls mal etwas schiefgeht oder du etwas zurückhaben willst.",
                () => !_settings.AutoBackupOff, v => { _settings.AutoBackupOff = !v; if (v) _nextBackupCheck = 0; ScheduleSave(); });
            p.Choice("Sicherungen behalten", () => "Ältere werden gelöscht.", new[] { "5", "10 (Standard)", "30" },
                () => Math.Max(0, Array.IndexOf(MixerSettings.BackupKeepValues, BackupKeep)), i => { _settings.BackupKeep = MixerSettings.BackupKeepValues[i]; ScheduleSave(); })
                .Enabled = () => !_settings.AutoBackupOff;
            p.Button("Sicherung zurückholen", "Einen früheren Stand laden – der jetzige wird vorher noch gesichert. Das Mischpult startet dafür neu.", "Auswählen ▾", r =>
            {
                var m = NewMenu();
                m.ShowCheckMargin = false; m.ShowImageMargin = false;
                AddBackupItems(m.Items);
                ShowMenuAt(m, r);
            }, () => { int n = _demo ? 6 : ListBackups(BackupFolder).Count; return n == 0 ? "keine" : n + (n == 1 ? " Sicherung" : " Sicherungen"); });
            p.Button("Jetzt sichern", "Sofort eine Sicherung anlegen (z. B. vor großen Änderungen).", "Sichern", r => BackupNow());
            p.Button("Ordner mit den Sicherungen", BackupFolder, "Öffnen", r =>
            {
                try { Directory.CreateDirectory(BackupFolder); Process.Start("explorer.exe", BackupFolder); } catch { }
            });
        }
}
}
