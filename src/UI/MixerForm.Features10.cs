using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.16.2: Meterbridge nach Wunsch – jeden Pegel einzeln ein-/ausblenden, die uebrigen ruecken zusammen (die Mitte wird frei
    /// fuer Spektrum, Lautheits-Verlauf, Stereo-Bild und Uhr), Reihenfolge dieser Anzeigen frei, Uhr in der Meterbridge auch im LCD-Look.
    /// 2.16.3: frei anordnen – Pegel und Anzeigen mit der Maus an jede Stelle ziehen.
    /// Alles in den Einstellungen an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Einstellungen auf die Meterbridge uebertragen (vor dem Anlegen der Zuege).</summary>
        private void ApplyFeatures10Statics()
        {
            MeterBridge.Hidden.Clear();
            if (_settings.BridgeHidden != null) foreach (var k in _settings.BridgeHidden) MeterBridge.Hidden.Add(k);
            MeterBridge.Pack = !_settings.BridgePackOff;
            MeterBridge.Order = BridgeOrderNow;
            MeterBridge.LcdClock = _settings.ClockStyle == 1;
            Theme.AnalogClock = _settings.ClockStyle == 2;
            Theme.ClockKind = _settings.ClockStyle;
            MeterBridge.Arrange = _settings.BridgeArrange;
            MeterBridge.FreeOrder = _settings.BridgeFreeOrder;
            MeterBridge.DragOn = !_settings.BridgeDragOff;
            MeterBridge.ConfigVersion++;
        }

        // ================================================================ 2.16.3: frei anordnen (mit der Maus verschieben)

        /// <summary>Neue Reihenfolge aus der Meterbridge (verschoben bzw. nach links/rechts).</summary>
        private void OnBridgeOrderChanged(List<string> order)
        {
            if (order == null) return;
            bool was = _settings.BridgeArrange;
            order = MeterBridge.KeepMissing(order, _settings.BridgeFreeOrder);
            _settings.BridgeFreeOrder = order;
            _settings.BridgeArrange = true;
            MeterBridge.FreeOrder = order;
            MeterBridge.Arrange = true;
            BridgeLayoutChanged();
            if (!was) ShowOsd("Meterbridge frei angeordnet", "Zurück: Rechtsklick oben → Anordnung zurücksetzen", -1, Theme.Accent);
        }

        private void SetBridgeArrange(bool on)
        {
            if (on && _settings.BridgeFreeOrder == null) _settings.BridgeFreeOrder = _bridge.CurrentOrder();   // so wie es gerade aussieht
            _settings.BridgeArrange = on;
            MeterBridge.Arrange = on;
            MeterBridge.FreeOrder = _settings.BridgeFreeOrder;
            BridgeLayoutChanged();
        }

        /// <summary>Wie am Anfang: jeder Pegel ueber seinem Kanal, eigene Reihenfolge vergessen.</summary>
        private void ResetBridgeArrange()
        {
            _settings.BridgeArrange = false;
            _settings.BridgeFreeOrder = null;
            MeterBridge.Arrange = false;
            MeterBridge.FreeOrder = null;
            BridgeLayoutChanged();
        }

        private void SetBridgeDrag(bool on)
        {
            _settings.BridgeDragOff = !on;
            MeterBridge.DragOn = on;
            ScheduleSave();
        }

        /// <summary>Eintraege fuers Meterbridge-Menue (frei anordnen).</summary>
        private void AddBridgeArrangeItems(ToolStripItemCollection items)
        {
            items.Add(new ToolStripMenuItem("Frei anordnen", null, (o, e) => SetBridgeArrange(!_settings.BridgeArrange))
            { Checked = _settings.BridgeArrange, ToolTipText = "Pegel und Anzeigen in eigener Reihenfolge – aus = jeder Pegel über seinem Kanal" });
            items.Add(new ToolStripMenuItem("Mit der Maus verschieben", null, (o, e) => SetBridgeDrag(_settings.BridgeDragOff)) { Checked = !_settings.BridgeDragOff });
            if (_settings.BridgeArrange || _settings.BridgeFreeOrder != null)
                items.Add(new ToolStripMenuItem("Anordnung zurücksetzen", null, (o, e) => ResetBridgeArrange()));
            if (!_settings.BridgeDragOff) items.Add(Hint("Verschieben: Pegel oder Anzeige oben mit der Maus ziehen."));
        }

        private string BridgeOrderNow => MixerSettings.FullBridgeOrder(_settings.BridgeOrder);

        /// <summary>Nach jeder Aenderung: beide Meterbridges neu aufbauen, Uhr oben ggf. ein/aus, speichern.</summary>
        private void BridgeLayoutChanged()
        {
            MeterBridge.ConfigVersion++;
            RestyleBridges();
            UpdateTopClock();
            ScheduleSave();
        }

        // ================================================================ Pegel ein-/ausblenden

        /// <summary>Zuege, die in der Meterbridge stehen koennen (wie in der Leiste: links Kanaele, rechts Ausgaenge).</summary>
        private List<StripBase> BridgeCandidates()
        {
            bool win = BridgeWinShown;
            return BridgeStrips().Where(s => s != null && !s.IsDisposed && s.BridgeKey != null && (s.Visible || win)).ToList();
        }

        private void SetBridgeShown(StripBase s, bool show)
        {
            var k = s?.BridgeKey;
            if (k == null) return;
            if (show) MeterBridge.Hidden.Remove(k); else MeterBridge.Hidden.Add(k);
            _settings.BridgeHidden = MeterBridge.Hidden.Count == 0 ? null : MeterBridge.Hidden.ToList();
            BridgeLayoutChanged();
        }

        private void ShowAllBridge()
        {
            MeterBridge.Hidden.Clear();
            _settings.BridgeHidden = null;
            BridgeLayoutChanged();
        }

        private static string StripLabel(StripBase s) => (s.Number > 0 ? s.Number + "   " : "") + s.DisplayName;

        /// <summary>"alle" bzw. "7 von 11".</summary>
        private string BridgeShownText()
        {
            var all = BridgeCandidates();
            int shown = all.Count(s => !MeterBridge.IsHidden(s));
            return shown == all.Count ? "alle (" + all.Count + ")" : shown == 0 ? "keine" : shown + " von " + all.Count;
        }

        /// <summary>Haken je Pegel – das Menue bleibt beim Anklicken offen, so lassen sich mehrere nacheinander umschalten.</summary>
        private void AddBridgeColumnItems(ToolStripItemCollection items)
        {
            var list = BridgeCandidates();
            var checks = new List<KeyValuePair<StripBase, ToolStripMenuItem>>();
            foreach (var strip in list)
            {
                var s = strip;
                var it = new ToolStripMenuItem(StripLabel(s)) { Checked = !MeterBridge.IsHidden(s) };
                it.Click += (o, e) =>
                {
                    KeepMenuOpen();
                    SetBridgeShown(s, MeterBridge.IsHidden(s));
                    it.Checked = !MeterBridge.IsHidden(s);
                };
                checks.Add(new KeyValuePair<StripBase, ToolStripMenuItem>(s, it));
                items.Add(it);
            }
            if (list.Count == 0) items.Add(Hint("Gerade keine Pegel."));
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Alle zeigen", null, (o, e) =>
            {
                KeepMenuOpen();
                ShowAllBridge();
                foreach (var kv in checks) kv.Value.Checked = true;
            }) { Enabled = list.Any(MeterBridge.IsHidden) });
            items.Add(Hint("Ausgeblendete machen Platz für die Anzeigen."));
        }

        /// <summary>Ein Klick im Menue schliesst es nicht (gilt fuer das Hauptmenue und Untermenues).</summary>
        private void KeepMenuOpen()
        {
            _keepMenuOpen = true;
            BeginInvoke(new Action(() => _keepMenuOpen = false));
        }

        private ToolStripMenuItem BridgeColumnsSub()
        {
            var sub = Sub("Pegel zeigen: " + BridgeShownText());
            AddBridgeColumnItems(sub.DropDownItems);
            sub.DropDown.Closing += (o, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepMenuOpen) e.Cancel = true; };
            return sub;
        }

        private void ShowBridgeColumnsMenu(Rectangle r)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            m.Items.Add(Header("Pegel in der Meterbridge"));
            m.Items.Add(new ToolStripSeparator());
            AddBridgeColumnItems(m.Items);
            ShowMenuAt(m, r);
        }

        /// <summary>Eintrag fuers Menue eines Zugs.</summary>
        private void AddBridgeShownItem(ToolStripItemCollection items, StripBase s)
        {
            if (s?.BridgeKey == null || _settings.BridgeOff) return;
            items.Add(new ToolStripMenuItem("Pegel in der Meterbridge", null, (o, e) => SetBridgeShown(s, MeterBridge.IsHidden(s)))
            { Checked = !MeterBridge.IsHidden(s), ToolTipText = "Aus = macht oben Platz für Spektrum, Lautheit, Stereo-Bild und Uhr" });
        }

        private void SetBridgePack(bool on)
        {
            _settings.BridgePackOff = !on;
            MeterBridge.Pack = on;
            BridgeLayoutChanged();
        }

        // ================================================================ Anzeigen: Reihenfolge, an/aus

        public static string ExtraName(char k) => k == 's' ? "Spektrum" : k == 'l' ? "Lautheits-Verlauf" : k == 'g' ? "Stereo-Bild" : k == 'c' ? "Uhr"
                                                  : k == 'o' ? "Oszilloskop" : k == 'a' ? "Album-Cover" : "?";
        private static string ExtraShort(char k) => k == 's' ? "Spektrum" : k == 'l' ? "Lautheit" : k == 'g' ? "Stereo" : k == 'c' ? "Uhr" : k == 'o' ? "Oszi" : k == 'a' ? "Cover" : "?";

        /// <summary>"Spektrum · Lautheit · Stereo · Uhr".</summary>
        private string BridgeOrderText() => string.Join(" · ", BridgeOrderNow.Select(ExtraShort));

        /// <summary>Anzeige verschieben: -1/+1 einen Platz, -9/+9 ganz nach links/rechts.</summary>
        private void MoveExtra(char k, int by)
        {
            var o = BridgeOrderNow.ToList();
            int i = o.IndexOf(k);
            if (i < 0) return;
            o.RemoveAt(i);
            int j = by <= -9 ? 0 : by >= 9 ? o.Count : Math.Max(0, Math.Min(o.Count, i + by));
            o.Insert(j, k);
            string ns = new string(o.ToArray());
            _settings.BridgeOrder = ns == MixerSettings.BridgeOrderDefault ? null : ns;
            MeterBridge.Order = ns;
            BridgeLayoutChanged();
        }

        private void ResetBridgeOrder()
        {
            _settings.BridgeOrder = null;
            MeterBridge.Order = MixerSettings.BridgeOrderDefault;
            BridgeLayoutChanged();
        }

        private bool ExtraOn(char k) =>
            k == 's' ? !_settings.SpecOff : k == 'l' ? LoudHistOn : k == 'g' ? !_settings.GonioOff : k == 'o' ? !_settings.OscOff : k == 'a' ? !_settings.CoverOff
            : k == 'c' && !_settings.ClockOff && !_settings.BridgeClockOff;

        private void SetExtraOn(char k, bool on)
        {
            switch (k)
            {
                case 's': SetSpectrumOn(on); break;
                case 'l': SetLoudHist(on); break;
                case 'g': SetGonio(on); break;
                case 'c': SetBridgeClock(on); break;
                case 'o': SetOscOn(on); break;
                case 'a': SetCoverOn(on); break;
            }
            BridgeLayoutChanged();
        }

        /// <summary>Uhr in der Meterbridge (sonst bzw. ohne Platz oben in der Kopfleiste).</summary>
        private void SetBridgeClock(bool on)
        {
            _settings.BridgeClockOff = !on;
            if (on && _settings.ClockOff) _settings.ClockOff = false;
            ApplyClock();
            ScheduleSave();
        }

        /// <summary>Eintraege fuer die Reihenfolge (links → rechts), je Anzeige verschieben.</summary>
        private void AddBridgeOrderItems(ToolStripItemCollection items)
        {
            string order = BridgeOrderNow;
            items.Add(Hint("Von links nach rechts:"));
            for (int i = 0; i < order.Length; i++)
            {
                char k = order[i];
                var sub = Sub((i + 1) + ".  " + ExtraName(k) + (ExtraOn(k) ? "" : "  (aus)"), false);
                sub.DropDownItems.Add(new ToolStripMenuItem("◀  Nach links", null, (o, e) => MoveExtra(k, -1)) { Enabled = i > 0 });
                sub.DropDownItems.Add(new ToolStripMenuItem("Nach rechts  ▶", null, (o, e) => MoveExtra(k, +1)) { Enabled = i < order.Length - 1 });
                sub.DropDownItems.Add(new ToolStripMenuItem("Ganz nach links", null, (o, e) => MoveExtra(k, -9)) { Enabled = i > 0 });
                sub.DropDownItems.Add(new ToolStripMenuItem("Ganz nach rechts", null, (o, e) => MoveExtra(k, +9)) { Enabled = i < order.Length - 1 });
                sub.DropDownItems.Add(new ToolStripSeparator());
                sub.DropDownItems.Add(new ToolStripMenuItem(ExtraOn(k) ? ExtraName(k) + " ausblenden" : ExtraName(k) + " anzeigen", null, (o, e) => SetExtraOn(k, !ExtraOn(k))));
                items.Add(sub);
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Wie am Anfang", null, (o, e) => ResetBridgeOrder()) { Enabled = order != MixerSettings.BridgeOrderDefault });
            items.Add(Hint("Zu wenig Platz? Vorrang: Uhr, Stereo-Bild, Lautheit, Oszilloskop, Cover."));
        }

        private ToolStripMenuItem BridgeOrderSub()
        {
            var sub = Sub("Reihenfolge der Anzeigen", false);
            AddBridgeOrderItems(sub.DropDownItems);
            return sub;
        }

        private void ShowBridgeOrderMenu(Rectangle r)
        {
            var m = NewMenu();
            m.ShowCheckMargin = false; m.ShowImageMargin = false;
            m.Items.Add(Header("Reihenfolge der Anzeigen"));
            m.Items.Add(new ToolStripSeparator());
            AddBridgeOrderItems(m.Items);
            ShowMenuAt(m, r);
        }

        /// <summary>Oben ins Meterbridge-Menue: was angeklickt wurde (Pegel ausblenden bzw. Anzeige verschieben).</summary>
        private void AddBridgeContextItems(ToolStripItemCollection items, MeterBridge b)
        {
            var s = b.MenuStrip;
            char k = b.MenuExtra;
            if (s != null && s.BridgeKey != null)
            {
                // 2.16.3: auch Pegel verschieben (dann frei angeordnet)
                string key = s.BridgeKey, n = s.DisplayName;
                items.Add(new ToolStripMenuItem("◀  „" + n + "“ nach links", null, (o, e) => b.MoveItem(key, -1)) { Enabled = b.CanMove(key, -1) });
                items.Add(new ToolStripMenuItem("„" + n + "“ nach rechts  ▶", null, (o, e) => b.MoveItem(key, +1)) { Enabled = b.CanMove(key, +1) });
                items.Add(new ToolStripMenuItem("„" + n + "“ hier ausblenden", null, (o, e) => SetBridgeShown(s, false))
                { ToolTipText = "Wieder einblenden: Pegel zeigen" });
                items.Add(new ToolStripSeparator());
            }
            else if (k != '\0')
            {
                string key = "#" + k;
                bool arr = _settings.BridgeArrange;
                string order = BridgeOrderNow;
                int i = order.IndexOf(k);
                items.Add(new ToolStripMenuItem("◀  " + ExtraName(k) + " nach links", null, (o, e) => { if (arr) b.MoveItem(key, -1); else MoveExtra(k, -1); })
                { Enabled = arr ? b.CanMove(key, -1) : i > 0 });
                items.Add(new ToolStripMenuItem(ExtraName(k) + " nach rechts  ▶", null, (o, e) => { if (arr) b.MoveItem(key, +1); else MoveExtra(k, +1); })
                { Enabled = arr ? b.CanMove(key, +1) : i < order.Length - 1 });
                items.Add(new ToolStripMenuItem(ExtraName(k) + " ausblenden", null, (o, e) => SetExtraOn(k, false)));
                items.Add(new ToolStripSeparator());
            }
        }

        // ================================================================ Vorschau

        /// <summary>Nur Vorschau: "was,x[,drop]" – Spalte (Kanal-Nummer, "out") bzw. Anzeige ("s", "l", "g", "c") nach x ziehen.</summary>
        private void DemoBridgeDrag(string spec)
        {
            var parts = spec.Split(',');
            if (parts.Length < 2 || !_bridge.Visible || !int.TryParse(parts[1], out int tx)) return;
            Point p = DemoBridgePoint(parts[0]);
            if (p == Point.Empty) return;
            _bridge.DragDemo(p, new Point(tx, p.Y), parts.Length > 2 && parts[2] == "drop");
        }

        private Point DemoBridgePoint(string what)
        {
            if (what.Length == 1 && "slgc".IndexOf(what[0]) >= 0)
            {
                var r = _bridge.ExtraRect(what[0]);
                return r.IsEmpty ? Point.Empty : new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            }
            var s = BridgeCandidates().FirstOrDefault(x => what == "out" ? x is OutputStrip : x.Number.ToString() == what);
            return s == null ? Point.Empty : _bridge.PointOf(s);
        }

        /// <summary>Nur Vorschau: Rechtsklick auf eine Spalte (Nummer des Kanals, "out" = erster Ausgang) bzw. eine Anzeige ("s", "l", "g", "c").</summary>
        private void DemoBridgeRightClick(string what)
        {
            if (!_bridge.Visible) return;
            Point p = DemoBridgePoint(what);
            if (p != Point.Empty) _bridge.RightClickDemo(p);
        }
    }
}
