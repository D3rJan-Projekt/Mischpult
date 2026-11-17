using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 – Pult-Aufbau: Kanalzug anpassen (Teile ausblenden), zwei Reihen, Barrierefreiheit
    /// (Bildschirmleser, deutlicher Fokus-Rahmen, kraeftige Schrift, weniger Bewegung). Alles einzeln an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private bool A11yCalm => _settings.A11yCalmOn;

        private void ApplyAufbau400(MixerSettings s)
        {
            ChannelStrip.Hide.Clear();
            if (s.StripCustomOn && s.StripHide != null)
                foreach (var p in s.StripHide) if (p == "pan" || p == "eq" || p == "keys" || p == "field" || p == "fx") ChannelStrip.Hide.Add(p);
            StripBase.HideScale = s.StripHidden("scale");
            StripBase.HideReadout = s.StripHidden("readout");
            StripBase.Calm = s.A11yCalmOn;
            StripBase.A11yFocus = s.A11yFocusOn;
            Theme.StrongText = s.A11yContrastOn;
            SeasonDeco.Calm = s.A11yCalmOn;
            ChannelStrip.GifCalm = s.A11yCalmOn;
            StripBase.KeyFocusChanged -= OnKeyFocus400;
            StripBase.KeyFocusChanged += OnKeyFocus400;
        }

        /// <summary>Zwei Reihen an den Kanal-Bereich weitergeben (nach dem Anlegen der Bereiche und bei jeder Aenderung).</summary>
        private void ApplyRows400()
        {
            if (_channelHost == null) return;
            _channelHost.TwoRows = _settings.TwoRowsOn;
            _channelHost.MinRowHeight = StripBase.HeightFor(MultiOutput, 80f) - Theme.Px(10);
            _channelHost.Relayout();
            _bridge?.Invalidate();
        }

        private void SetAufbau400(Action change, bool relayout = true, bool restyle = false)
        {
            change();
            ApplyAufbau400(_settings);
            if (restyle) SetScheme(_settings.ColorScheme, _settings.AccentColor, _settings.UiStyle);
            if (relayout) RelayoutAllStrips();
            ApplyRows400();
            _top.RefreshDeco();
            ScheduleSave();
        }

        // ================================================================ 174 Kanalzug anpassen

        private bool PartHidden400(string part) => part == "leds" ? (_settings.SignalLedOff && _settings.PeakLedOff) : _settings.StripHide != null && _settings.StripHide.Contains(part);

        private void TogglePart400(string part)
        {
            if (part == "leds") { bool on = _settings.SignalLedOff && _settings.PeakLedOff; SetLeds(on, on); return; }   // wie SIG/PEAK unter Leuchten & Anzeigen
            SetAufbau400(() =>
            {
                var l = _settings.StripHide ?? new System.Collections.Generic.List<string>();
                if (l.Contains(part)) l.Remove(part); else l.Add(part);
                _settings.StripHide = l.Count == 0 ? null : l;
                _settings.StripCustomOn = true;
            });
        }

        private void ShowPartsMenu400(Rectangle at)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            m.Items.Add(Header("Was im Kanalzug zu sehen ist"));
            for (int i = 0; i < MixerSettings.StripPartNames.Length; i++)
            {
                string part = MixerSettings.StripPartNames[i];
                var it = new ToolStripMenuItem(MixerSettings.StripPartLabels[i], null, (s, e) => TogglePart400(part)) { Checked = !PartHidden400(part) };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Hint("Was wegfällt, bekommt das App-Feld –"));
            m.Items.Add(Hint("die Fader bleiben auf einer Höhe."));
            ShowMenuAt(m, at);
        }

        private string PartsSummary400()
        {
            int hidden = MixerSettings.StripPartNames.Count(PartHidden400);
            return hidden == 0 ? "alles zu sehen" : hidden + " ausgeblendet";
        }

        // ================================================================ 182 Bildschirmleser

        private void OnKeyFocus400(StripBase s)
        {
            if (!_settings.A11yReaderOn || s == null) return;
            Announce400(DescribeStrip400(s));
        }

        /// <summary>Text fuer den Bildschirmleser: "Kanal 2, Spiel, minus 4 dB, stumm".</summary>
        internal string DescribeStrip400(StripBase s)
        {
            if (s is ChannelStrip cs)
            {
                var c = cs.Config;
                return "Kanal " + (cs.Number > 0 ? cs.Number + ", " : "") + c.Name + ", " + FaderLaw.FormatDb(c.Fader) + (c.Mute ? ", stumm" : "") + (c.Solo ? ", Solo" : "")
                       + (c.Apps.Count > 0 ? ", " + c.Apps.Count + (c.Apps.Count == 1 ? " App" : " Apps") : "");
            }
            return s.DisplayName;
        }

        private MethodInfo _notify400;
        private bool _notifyLooked;

        /// <summary>
        /// Text an den Bildschirmleser (Erzaehler, NVDA …) geben – ueber die UI-Automation-Benachrichtigung von Windows
        /// (gibt es ab Windows 10 1709; sonst passiert einfach nichts).
        /// </summary>
        private void Announce400(string text)
        {
            if (!_settings.A11yReaderOn || string.IsNullOrEmpty(text)) return;
            AccessibleDescription = text;
            try
            {
                var ao = AccessibilityObject;
                if (!_notifyLooked)
                {
                    _notifyLooked = true;
                    _notify400 = ao.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(mi => mi.Name == "RaiseAutomationNotification" && mi.GetParameters().Length == 3);
                }
                if (_notify400 == null) return;
                var ps = _notify400.GetParameters();
                _notify400.Invoke(ao, new object[] { Enum.ToObject(ps[0].ParameterType, 4), Enum.ToObject(ps[1].ParameterType, 3), text });   // Other, MostRecent
            }
            catch { }
        }

        private double _nextA11y;

        /// <summary>Namen und Zustand der Zuege fuer Bildschirmleser aktuell halten (einmal pro Sekunde).</summary>
        private void TickA11y400(double now)
        {
            if (!_settings.A11yReaderOn || now < _nextA11y) return;
            _nextA11y = now + 1.0;
            foreach (var s in AllStrips())
            {
                if (s == null || s.IsDisposed) continue;
                string name = s is ChannelStrip ? DescribeStrip400(s) : s.DisplayName;
                if (s.AccessibleName != name) s.AccessibleName = name;
                if (s.AccessibleRole != AccessibleRole.Slider) s.AccessibleRole = AccessibleRole.Slider;
            }
        }

        // ================================================================ Einstellungen

        private void AddAufbau400(SPage p)
        {
            p.Section("4.0 Pult-Aufbau");
            p.Toggle("Kanalzug anpassen", () => "Teile der Kanalzüge ausblenden, die du nie brauchst (z. B. EQ-Knöpfe oder Panorama). Was wegfällt, bekommt das App-Feld. Gerade: " + PartsSummary400() + ".",
                () => _settings.StripCustomOn, v => SetAufbau400(() => _settings.StripCustomOn = v));
            p.Button("Teile wählen", "Haken weg = ausgeblendet.", "Wählen ▾", r => ShowPartsMenu400(r), PartsSummary400).Enabled = () => _settings.StripCustomOn;
            p.Toggle("Zwei Reihen", "Passen nicht alle Kanäle nebeneinander und ist das Fenster hoch genug, stehen sie in zwei Reihen übereinander (statt zu scrollen). Die Fader werden dabei kürzer.",
                () => _settings.TwoRowsOn, v => SetAufbau400(() => _settings.TwoRowsOn = v, false));
        }

        private void AddA11y400(SPage p)
        {
            p.Section("4.0 Barrierefreiheit");
            p.Toggle("Bildschirmleser", "Kanäle mit den Pfeiltasten wählen (links/rechts), Lautstärke mit hoch/runter, M = stumm, S = Solo. Der Bildschirmleser (Erzähler, NVDA, JAWS) liest Name, Lautstärke und Zustand vor.",
                () => _settings.A11yReaderOn, v => { _settings.A11yReaderOn = v; ScheduleSave(); if (v) { _nextA11y = 0; Announce400("Bildschirmleser an. Pfeiltasten wählen Kanäle."); } });
            p.Toggle("Deutlicher Fokus-Rahmen", "Der per Tastatur gewählte Kanal bekommt einen dicken weißen Rahmen mit Akzentfarbe.",
                () => _settings.A11yFocusOn, v => SetAufbau400(() => _settings.A11yFocusOn = v, false));
            p.Toggle("Kräftige Schrift", "Graue Beschriftungen (dB-Skala, kleine Hinweise) werden heller und besser lesbar.",
                () => _settings.A11yContrastOn, v => SetAufbau400(() => _settings.A11yContrastOn = v, false, true));
            p.Toggle("Weniger Bewegung", "Keine Motorfahrten, kein Lauflicht, kein Wackeln, kein bewegter Hintergrund, Deko und GIFs stehen still, das Musik-Glühen ist ruhig.",
                () => _settings.A11yCalmOn, v => SetAufbau400(() => _settings.A11yCalmOn = v, false));
        }
    }
}
