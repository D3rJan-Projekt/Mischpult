using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5 – Optik: gluehende Roehren, Lauflicht beim Szenenwechsel, Pult wackelt mit dem Bass, Spektrum hinter den
    /// Kanaelen, Geraete-Bild am Ausgang. Alles einzeln an/aus, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 135 Roehren

        private void TickTubes()
        {
            if (!_settings.TubesOn || _master == null) return;
            TubeView.LevelL = _master.LastPeakL;
            TubeView.LevelR = _master.LastPeakR;
        }

        // ================================================================ 136 Lauflicht

        private double _nextSweepPaint;

        private void TickSweep(double now)
        {
            if (!_settings.SceneSweepOn || !StripBase.SweepRunning || now < _nextSweepPaint) return;
            _nextSweepPaint = now + 0.016;
            foreach (var s in _channelStrips) if (s.Visible) s.Invalidate();
            foreach (var g in _groupStrips) if (g.Visible) g.Invalidate();
        }

        // ================================================================ 154 Pult wackelt mit dem Bass

        private float _bassAvg;
        private double _shakeStart = -1;
        private float _shakeAmp;
        private Point _shakeBase, _shakeLast;
        private bool _shaking;

        private void TickShake(double now, float dt)
        {
            if (!_settings.ShakeOn || A11yCalm) { StopShake(); return; }   // 4.0: weniger Bewegung
            float bass = _snap.MasterBass;
            if (bass < 0f) return;
            bool canShake = Visible && WindowState == FormWindowState.Normal && !DrawerActive && Control.MouseButtons == MouseButtons.None && PultShown;
            float prevAvg = _bassAvg;
            _bassAvg += (bass - _bassAvg) * Math.Min(1f, dt * 1.5f);   // langsamer Mittelwert
            if (canShake && !_shaking && bass > 0.12f && bass > prevAvg * 1.8f + 0.03f)
            {
                _shakeAmp = Math.Min(3f, 1f + (bass / Math.Max(0.02f, prevAvg) - 1.8f) * 0.8f) * Theme.S;
                _shakeStart = now;
                _shakeBase = Location;
                _shakeLast = Location;
                _shaking = true;
            }
            if (!_shaking) return;
            if (!canShake || Location != _shakeLast) { _shaking = false; if (canShake && Location == _shakeLast) Location = _shakeBase; return; }   // Fenster wurde bewegt
            double t = now - _shakeStart;
            if (t > 0.16) { StopShake(); return; }
            double env = Math.Exp(-t / 0.05);
            int dx = (int)Math.Round(_shakeAmp * env * Math.Sin(t * 2 * Math.PI * 23));
            int dy = (int)Math.Round(_shakeAmp * 0.6 * env * Math.Cos(t * 2 * Math.PI * 19));
            var p = new Point(_shakeBase.X + dx, _shakeBase.Y + dy);
            if (p != Location) { Location = p; _shakeLast = p; }
        }

        private void StopShake()
        {
            if (!_shaking) return;
            _shaking = false;
            if (Location == _shakeLast && WindowState == FormWindowState.Normal) Location = _shakeBase;
        }

        // ================================================================ 155 Spektrum hinter den Kanaelen

        private double _nextSpecBg;
        private int _specBgVer = -1;
        private readonly float[] _sbL = new float[48], _sbP = new float[48];

        private void TickSpecBg(double now)
        {
            if (!_settings.SpecBgOn || !PultShown || _channelHost == null || now < _nextSpecBg) return;
            _nextSpecBg = now + 0.033;
            if (_spectrum.Version == _specBgVer) return;
            _specBgVer = _spectrum.Version;
            _channelHost.Invalidate(new Rectangle(0, 0, _channelHost.ClientSize.Width, _channelHost.StripBottom), false);
        }

        /// <summary>Aus PaintHost346: grosses, schwaches Spektrum hinter den Kanaelen (sichtbar in Luecken und freier Flaeche).</summary>
        private void PaintSpecBg350(StripHost h, Graphics g)
        {
            if (!_settings.SpecBgOn || h != _channelHost || !_spectrum.Active) return;
            int n = Math.Max(16, Math.Min(_sbL.Length, h.ClientSize.Width / Theme.Px(22)));
            _spectrum.Bars(n, _sbL, _sbP);
            float top = Theme.F(26), bottom = h.StripBottom, hh = bottom - top, w = h.ClientSize.Width / (float)n;
            var col = Theme.Accent;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new LinearGradientBrush(new RectangleF(0, top - 1, Math.Max(10, h.ClientSize.Width), hh + 2), Color.FromArgb(70, col), Color.FromArgb(10, col), LinearGradientMode.Vertical))
            using (var pk = new SolidBrush(Color.FromArgb(90, col)))
                for (int i = 0; i < n; i++)
                {
                    float v = Math.Max(0f, Math.Min(1f, _sbL[i])), p = Math.Max(0f, Math.Min(1f, _sbP[i]));
                    float x = i * w + w * 0.12f, bw = w * 0.76f;
                    if (v > 0.01f) g.FillRectangle(b, x, bottom - v * hh, bw, v * hh);
                    if (p > 0.02f) g.FillRectangle(pk, x, bottom - p * hh - Theme.F(2), bw, Theme.F(2));
                }
            g.SmoothingMode = sm;
        }

        // ================================================================ Einstellungen

        private void AddLook350(SPage p)
        {
            p.Section("3.5 Optik");
            p.Toggle("Glühende Röhren", "In der Meterbridge stehen drei Verstärkerröhren, die mit dem Pegel des MASTERs aufglühen (links, Mitte, rechts) – leicht flackernd wie echte Heizfäden. Braucht etwas Platz in der Meterbridge.",
                () => _settings.TubesOn, v => { _settings.TubesOn = v; MeterBridge.TubesOn = v; RestyleBridges(); ScheduleSave(); });
            p.Toggle("Lauflicht beim Szenenwechsel", "Wird eine Szene geladen, läuft ein Licht einmal über alle Kanäle – von links nach rechts.",
                () => _settings.SceneSweepOn, v => { _settings.SceneSweepOn = v; StripBase.SweepOn = v; if (v) StripBase.StartSweep(); ScheduleSave(); });
            p.Toggle("Pult wackelt mit dem Bass", "Bei kräftigen Bässen (Bassdrum, Explosion) zittert das ganze Fenster ganz kurz um 1–3 Pixel, wie ein Tisch neben der Box. Nicht, wenn das Fenster maximiert ist oder du es gerade ziehst.",
                () => _settings.ShakeOn, v => { _settings.ShakeOn = v; LoudnessSink.BassOn = v; if (!v) StopShake(); ScheduleSave(); });
            p.Toggle("Spektrum hinter den Kanälen", "Ein großes, schwaches Spektrum in der Akzentfarbe hinter den Kanälen – zu sehen in den Lücken und in der freien Fläche rechts.",
                () => _settings.SpecBgOn, v => { _settings.SpecBgOn = v; _channelHost.Invalidate(); ScheduleSave(); });
            p.Toggle("Geräte-Bild am Ausgang", "MASTER und Ausgänge zeigen links im Geräte-Feld, was dranhängt: Lautsprecher, Kopfhörer, Headset oder Bildschirm (HDMI) – mit kleinem Zeichen für Bluetooth bzw. USB.",
                () => _settings.DevIconOn, v => { _settings.DevIconOn = v; OutputStrip.DevIconOn = v; InvalidateOutputs350(); ScheduleSave(); });
        }
    }
}
