using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.13: Spektrum (und grosses VU-Meter) von einem waehlbaren Ausgang oder von allen Ausgaengen zusammen,
    /// Spektrum auch allein im eigenen Fenster.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Welcher Ausgang

        private bool SpecAll => _settings.SpecSource == MixerSettings.SpecAll;

        /// <summary>Gewaehltes Geraet (null = Standard-Ausgabe bzw. alle bzw. gerade nicht da).</summary>
        private DeviceState SpecDevice =>
            _settings.SpecSource == null || SpecAll ? null
            : _snap.Devices.FirstOrDefault(d => string.Equals(d.Id, _settings.SpecSource, StringComparison.OrdinalIgnoreCase));

        /// <summary>Gewaehltes Geraet ist gerade nicht angeschlossen.</summary>
        private bool SpecDeviceMissing => _settings.SpecSource != null && !SpecAll && SpecDevice == null;

        private string[] _specIds;
        private string _specIdsSrc, _specIdsStruct;
        private int _specIdsN = -1;

        /// <summary>Geraete, deren Ton ins Spektrum kommt (Eintrag null = Windows-Standardgeraet), leer = keins.</summary>
        private string[] SpectrumDeviceIds()
        {
            if (_specIds != null && _specIdsSrc == _settings.SpecSource && _specIdsN == _snap.Devices.Length && string.Equals(_specIdsStruct, _snap.Structure))
                return _specIds;
            _specIdsSrc = _settings.SpecSource; _specIdsStruct = _snap.Structure; _specIdsN = _snap.Devices.Length;
            if (_settings.SpecSource == null) _specIds = new string[] { null };
            else if (SpecAll)
            {
                var ids = _snap.Devices.Select(d => d.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
                _specIds = ids.Length > 0 ? ids : new string[] { null };
            }
            else _specIds = SpecDevice != null ? new[] { SpecDevice.Id } : new string[0];
            return _specIds;
        }

        /// <summary>Name fuer Titel und Menue, z. B. "Lautsprecher", "Alle Ausgaben", "Kopfhörer (nicht angeschlossen)".</summary>
        private string SpecSourceText(bool full = false)
        {
            if (SpecAll) return "Alle Ausgaben";
            if (_settings.SpecSource == null)
            {
                var d = DefaultDevice;
                return d == null ? "Standard-Ausgabe" : full ? "Standard-Ausgabe (" + d.Name + ")" : d.Name;
            }
            var sd = SpecDevice;
            if (sd != null) return sd.Name;
            string n = string.IsNullOrEmpty(_settings.SpecSourceName) ? "Gerät" : _settings.SpecSourceName;
            return n + " (nicht angeschlossen)";
        }

        /// <summary>Ueberschrift ueber dem Spektrum.</summary>
        private string SpecCaption()
        {
            if (SpecAll) return "SPEKTRUM · ALLE AUSGÄNGE";
            if (_settings.SpecSource == null)
                return MultiOutput ? "SPEKTRUM · " + (DefaultDevice?.Name ?? "STANDARD").ToUpperInvariant() : "SPEKTRUM";
            return "SPEKTRUM · " + SpecSourceText().ToUpperInvariant();
        }

        private void SetSpecSource(string id, string name)
        {
            _settings.SpecSource = string.IsNullOrEmpty(id) ? null : id;
            _settings.SpecSourceName = id == null || id == MixerSettings.SpecAll ? null : name;
            _specIds = null;
            _spectrum.Reset();
            ApplySpecCaption();
            ScheduleSave();
            ShowOsd("Spektrum", SpecSourceText(true), -1, Theme.Accent);
        }

        private string _shownSpecCaption;

        /// <summary>Ueberschriften in Meterbridge und Fenster nachziehen (nur bei Aenderung).</summary>
        private void ApplySpecCaption()
        {
            string cap = SpecCaption();
            if (cap == _shownSpecCaption) return;
            _shownSpecCaption = cap;
            _bridge.SpectrumCaption = cap;
            if (VuWindowOpen) _vuWin.Caption = cap;
        }

        /// <summary>Auswahl des Ausgangs (fuer Menues).</summary>
        private void AddSpecSourceItems(ToolStripItemCollection items)
        {
            var def = DefaultDevice;
            items.Add(new ToolStripMenuItem("Standard-Ausgabe" + (def != null ? "  (" + def.Name + ")" : ""), null, (s, e) => SetSpecSource(null, null))
            { Checked = _settings.SpecSource == null, ToolTipText = "Folgt dem Windows-Standardgerät" });
            items.Add(new ToolStripMenuItem("Alle Ausgaben zusammen", null, (s, e) => SetSpecSource(MixerSettings.SpecAll, null))
            { Checked = SpecAll, ToolTipText = "Was auf irgendeinem Gerät läuft – z. B. Musik auf den Lautsprechern und Spiel auf dem Headset" });
            if (_snap.Devices.Length > 0) items.Add(new ToolStripSeparator());
            foreach (var d in _snap.Devices)
            {
                string id = d.Id, name = d.Name;
                items.Add(new ToolStripMenuItem(d.FullName ?? d.Name, null, (s, e) => SetSpecSource(id, name))
                { Checked = string.Equals(_settings.SpecSource, id, StringComparison.OrdinalIgnoreCase) });
            }
            if (SpecDeviceMissing)
                items.Add(new ToolStripMenuItem(SpecSourceText()) { Checked = true, Enabled = false });
        }

        private void ShowSpecSourceMenu(Point screen)
        {
            var m = NewMenu();
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            m.Items.Add(Header("Spektrum zeigt"));
            m.Items.Add(Hint("Gilt auch für das große VU-Meter."));
            m.Items.Add(new ToolStripSeparator());
            AddSpecSourceItems(m.Items);
            m.Items.Add(new ToolStripSeparator());
            if (!VuWindowOpen || !_vuWin.ShowSpectrum)
                m.Items.Add(new ToolStripMenuItem("Großes Spektrum im Fenster …", null, (s, e) => ShowSpectrumWindow()));
            m.Show(screen);
        }

        private ToolStripMenuItem SpecSourceSub()
        {
            var sub = Sub("Spektrum zeigt: " + SpecSourceText());
            AddSpecSourceItems(sub.DropDownItems);
            return sub;
        }

        // ================================================================ Pegel fuers grosse VU-Meter

        /// <summary>Pegel [L, R] des gewaehlten Ausgangs – bei "alle" der lauteste.</summary>
        private float[] SpecSourcePeak(out string name)
        {
            if (SpecAll)
            {
                name = "Alle Ausgaben";
                float l = 0f, r = 0f;
                foreach (var d in _snap.Devices)
                {
                    var p = DevicePeak(d);
                    if (p[0] > l) l = p[0];
                    if (p[1] > r) r = p[1];
                }
                return new[] { l, r };
            }
            var dev = _settings.SpecSource == null ? DefaultDevice : SpecDevice;
            name = dev?.Name ?? SpecSourceText();
            var pk = DevicePeak(dev);
            return new[] { pk[0], pk[1] };
        }

        // ================================================================ Spektrum allein im Fenster

        /// <summary>Grosses Fenster mit dem Spektrum oeffnen (ist es schon offen, kommt das Spektrum dazu).</summary>
        public void ShowSpectrumWindow()
        {
            if (VuWindowOpen)
            {
                if (!_vuWin.ShowSpectrum) { _vuWin.ShowSpectrum = true; _vuWin.Restyle(); VuWindowChanged(_vuWin); }
                if (_vuWin.WindowState == FormWindowState.Minimized) _vuWin.WindowState = FormWindowState.Normal;
                _vuWin.Activate();
                return;
            }
            _settings.VuWinVuOff = true;
            _settings.VuWinSpecOff = false;
            ShowVuWindow(false);
        }

        /// <summary>Was das grosse Fenster zeigt: 0 = VU-Meter + Spektrum, 1 = nur VU-Meter, 2 = nur Spektrum.</summary>
        private static int VuWinMode(VuWindow w) => !w.ShowVu ? 2 : w.ShowSpectrum ? 0 : 1;

        private void SetVuWinMode(VuWindow w, int mode)
        {
            w.ShowVu = mode != 2;
            w.ShowSpectrum = mode != 1;
            w.Restyle();
            VuWindowChanged(w);
        }

        public static readonly string[] VuWinModeNames = { "VU-Meter und Spektrum", "Nur VU-Meter", "Nur Spektrum" };
    }
}
