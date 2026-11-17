using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.16: Die Meterbridge im eigenen, frei grossen Fenster – z. B. auf dem zweiten Bildschirm beim Spielen.
    /// Alle Kanaele und Ausgaenge nebeneinander, rechts daneben (wenn Platz) Lautheits-Verlauf, Stereo-Bild, Uhr und Spektrum.
    /// Laeuft auch, wenn das Pult selbst versteckt, minimiert oder von einem Spiel verdeckt ist.
    /// </summary>
    internal sealed class BridgeWindow : Form
    {
        public readonly MeterBridge Bridge;

        public BridgeWindow(MixerForm mixer, Func<IEnumerable<StripBase>> strips)
        {
            Text = "Meterbridge – Mischpult";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Theme.Px(360), Theme.Px(120));
            Size = new Size(Theme.Px(1240), Theme.Px(240));
            BackColor = MixerForm.BodyColor;
            KeyPreview = true;
            try { Icon = mixer.Icon; } catch { }
            Bridge = new MeterBridge { Dock = DockStyle.Fill, FreeLayout = true, Strips = strips };
            Controls.Add(Bridge);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F11) FullScreen.Toggle(this);                       // 2.18: Vollbild
            else if (e.KeyCode == Keys.Escape) { if (FullScreen.IsOn(this)) FullScreen.Set(this, false); else Close(); }
        }

        /// <summary>Zu sehen (nicht minimiert)?</summary>
        public bool IsOnScreen => !IsDisposed && Visible && WindowState != FormWindowState.Minimized;
    }
}
