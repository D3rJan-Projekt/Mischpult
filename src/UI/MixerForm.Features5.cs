using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>2.12: Fader sperren, Fader-Marke, Pegel-Farben, LCD-Stil, Kanalfarbe auf Fadern, Einschalt-Gruss, Studio-Uhr.</summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Menuepunkte "Fader sperren" und "Fader-Marke" (Kanal- und Gruppenmenue).</summary>
        private void AddFaderItems(ToolStripItemCollection items, Func<bool> getLock, Action<bool> setLock, Func<float?> getMark, Action<float?> setMark,
                                   Func<float> getPos, Action<float> setPos, StripBase strip)
        {
            items.Add(new ToolStripMenuItem("Fader sperren", null, (s, e) =>
            {
                setLock(!getLock());
                strip.InvalidateChrome();
                ScheduleSave();
            }) { Checked = getLock(), ToolTipText = "Ziehen und Mausrad ändern den Fader dann nicht mehr\n(Szenen, MIDI und Hotkeys schon)" });
            var mark = getMark();
            var sub = Sub("Fader-Marke", false);
            sub.DropDownItems.Add(new ToolStripMenuItem("Hier setzen (" + FaderLaw.FormatDb(getPos()) + ")", null, (s, e) =>
            {
                setMark(getPos());
                strip.InvalidateChrome();
                ScheduleSave();
            }) { ToolTipText = "Merkt sich die jetzige Stellung – Doppelklick auf den Fader springt dann dahin zurück" });
            sub.DropDownItems.Add(new ToolStripMenuItem("Zur Marke fahren" + (mark.HasValue ? " (" + FaderLaw.FormatDb(mark.Value) + ")" : ""), null, (s, e) =>
            {
                var m = getMark();
                if (!m.HasValue) return;
                float from = strip.MotorFrom;
                setPos(Math.Max(0f, Math.Min(1f, m.Value)));
                strip.InvalidateChrome();
                strip.StartMotor(from);
                ScheduleSave();
            }) { Enabled = mark.HasValue });
            sub.DropDownItems.Add(new ToolStripMenuItem("Marke löschen", null, (s, e) =>
            {
                setMark(null);
                strip.InvalidateChrome();
                ScheduleSave();
            }) { Enabled = mark.HasValue });
            items.Add(sub);
        }
    
        private void SetMeterStyle(int i)
        {
            _settings.MeterColors = Math.Max(0, Math.Min(Theme.MeterStyleNames.Length - 1, i));
            Theme.MeterStyle = _settings.MeterColors;
            RefreshStrips();
            _bridge.Restyle();
            _mini?.Invalidate();
            if (VuWindowOpen) _vuWin.Restyle();
            ScheduleSave();
        }

        private void SetLcdStyle(int i)
        {
            _settings.LcdStyle = Math.Max(0, Math.Min(Theme.LcdStyleNames.Length - 1, i));
            Theme.LcdStyle = _settings.LcdStyle;
            RefreshStrips();
            _bridge.Restyle();
            _top.Invalidate();
            ScheduleSave();
        }

        private void SetColorCaps(bool on)
        {
            _settings.ColorCapsOff = !on;
            StripBase.ColorCaps = on;
            RefreshStrips();
            ScheduleSave();
        }

        /// <summary>Einschalt-Gruss noch einmal zeigen (aus den Einstellungen).</summary>
        private void ShowGreeting()
        {
            bool was = StripBase.GreetingOn;
            StripBase.GreetingOn = true;
            try
            {
                var area = RectangleToScreen(ClientRectangle);
                StripBase.StartGreeting("MISCHPULT " + Program.Version + "   ·   MADE BY " + Program.Author.ToUpperInvariant(), area.Left, area.Right);
            }
            catch { }
            StripBase.GreetingOn = was;
        }
}
}
