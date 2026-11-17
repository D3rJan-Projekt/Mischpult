using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>Mikrofon-Zug: Windows-Standard-Aufnahmegeraet (Lautstaerke, MUTE, Pegel, Geraetewahl).</summary>
    internal sealed class MicStrip : StripBase
    {
        /// <summary>Das Mikro zeigt immer sofort (Pegel an den Ton anpassen gilt nur fuer das, was man hoert).</summary>
        protected override bool NoMeterDelay => true;
        private DeviceState _dev;
        private bool _live;
        private float _localVol = -1f;
        private readonly Stopwatch _localUntil = new Stopwatch();
        private string _hotkey = "";
        private int _pttMode;          // 0 aus, 1 Push-to-Talk, 2 Push-to-Mute
        private string _pttKey = "";
        private bool _pttHeld, _hardMute;

        private RectangleF _nameRect, _infoRect, _devBtn, _liveBtn, _fxBtn, _sbBtn, _hotRect, _muteBtn;
        private int _fx;   // Mikro-Effekte: 0 aus, 1 eingestellt (Sprach-Ausgang aus), 2 aktiv
        private bool _soundsPlaying;
        private string _voiceTarget;

        public MicStrip(MixerForm mixer) : base(mixer) { }

        // ---- 4.3: das Mikro loest gerade das Ducking aus
        private bool _duckSource;
        public void SetDuckSource(bool on)
        {
            if (on == _duckSource) return;
            _duckSource = on;
            InvalidateChrome();
        }

        protected override void DrawOverlay(Graphics g)
        {
            if (!_duckSource) return;
            var r = new RectangleF(PanelRect.X + Theme.F(Narrow ? 4 : 6), FaderArea.Y + Theme.F(1), Theme.F(Narrow ? 30 : 48), Theme.F(11));
            Theme.FillRound(g, ChannelStrip.DuckSourceColor, r, Theme.F(3));
            using (var b = new SolidBrush(Theme.Darken(ChannelStrip.DuckSourceColor, 0.88f)))
                Theme.Str(g, Narrow ? "AUSL." : "LÖST AUS", Theme.Ui(8f, FontStyle.Bold), b, r, Theme.Center);
        }

        public override int StripWidth => Theme.Px(118);
        protected override bool MasterCap => false;
        protected override Color AccentColor => Color.FromArgb(MixerSettings.Palette[7]);
        public override string DisplayName => "MIKRO";
        public override string BridgeKey => "mic";
        protected override RectangleF MuteKey => _muteBtn;
        protected override bool MuteLit => _pttMode != 0 ? _hardMute : _pathMute ?? (_dev != null && _dev.Mute);
        protected override float MeterBarWidth => 7f;
        protected override bool Dimmed => _pathMute ?? (_dev != null && _dev.Mute);

        private bool LocalActive => _localVol >= 0 && _localUntil.IsRunning && _localUntil.ElapsedMilliseconds < 700;
        protected override float FaderPos => LocalActive ? _localVol : _dev?.Volume ?? 0f;

        protected override string ReadoutText
        {
            get
            {
                if (_dev == null) return "--";
                if (_pttMode != 0 && !_hardMute)
                {
                    if (_pttMode == 1) return _pttHeld ? "SENDEN" : "PTT";
                    if (_pttHeld) return "STUMM";
                }
                return (_pathMute ?? _dev.Mute) ? "STUMM" : ((int)Math.Round(FaderPos * 100)).ToString(CultureInfo.InvariantCulture) + " %";
            }
        }

        /// <summary>Soundboard spielt gerade / Ziel des Sprach-Ausgangs (null = aus) / Mikro-Effekte (0 aus, 1 eingestellt, 2 aktiv).</summary>
        public void SetExtras(bool soundsPlaying, string voiceTarget, int fx)
        {
            if (soundsPlaying == _soundsPlaying && voiceTarget == _voiceTarget && fx == _fx) return;
            _soundsPlaying = soundsPlaying; _voiceTarget = voiceTarget; _fx = fx;
            InvalidateChrome();
        }

        private bool? _pathMute;

        /// <summary>Stummschaltung nur im Sprach-Ausgang (null = Windows-Stummschaltung).</summary>
        public void SetPathMute(bool? muted)
        {
            if (muted == _pathMute) return;
            _pathMute = muted;
            InvalidateChrome();
        }

        /// <summary>Push-to-Talk-Zustand fuer die Anzeige.</summary>
        public void SetPtt(int mode, string key, bool held, bool hardMute)
        {
            key = key ?? "";
            if (mode == _pttMode && key == _pttKey && held == _pttHeld && hardMute == _hardMute) return;
            _pttMode = mode; _pttKey = key; _pttHeld = held; _hardMute = hardMute;
            InvalidateChrome();
        }

        protected override string FaderTipSuffix =>
            _dev != null ? "  ·  " + _dev.VolumeDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : "";

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                yield return new KeyValuePair<float, string>(1f, "100");
                yield return new KeyValuePair<float, string>(0.75f, "75");
                yield return new KeyValuePair<float, string>(0.5f, "50");
                yield return new KeyValuePair<float, string>(0.25f, "25");
                yield return new KeyValuePair<float, string>(0f, "0");
            }
        }

        public void SetMic(DeviceState dev, bool live, string hotkey)
        {
            hotkey = hotkey ?? "";
            bool changed = (dev == null) != (_dev == null) || live != _live || hotkey != _hotkey ||
                (dev != null && (dev.Id != _dev.Id || dev.Mute != _dev.Mute || dev.Name != _dev.Name ||
                                 Math.Abs(dev.Volume - _dev.Volume) > 0.0005f));
            _dev = dev;
            _live = live;
            _hotkey = hotkey;
            if (!LocalActive && _localVol >= 0) { _localVol = -1f; changed = true; }
            if (changed) InvalidateChrome();
        }

        protected override void OnFaderMoved(float pos, bool final)
        {
            if (_dev == null) return;
            _localVol = pos;
            _localUntil.Restart();
            Mixer.Backend.SetMicVolume(pos);
        }

        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _infoRect = new RectangleF(x, Theme.F(42), iw, Theme.F(44));
            _devBtn = new RectangleF(x, Theme.F(90), iw, Theme.F(20));
            _liveBtn = new RectangleF(x, Theme.F(121 + RowOffset), iw, Theme.F(20));
            _fxBtn = new RectangleF(x, Theme.F(153 + RowOffset), iw, Theme.F(20));
            _sbBtn = new RectangleF(x, Theme.F(193 + RowOffset), iw, Theme.F(17));
            _hotRect = new RectangleF(x, Theme.F(196 + DynRow + RowOffset), iw, Theme.F(30));
            _muteBtn = new RectangleF(x, Theme.F(237 + DynRow + RowOffset), iw, Theme.F(22));
            AddZone(_nameRect, "name");
            AddZone(_infoRect, "device");
            AddZone(_devBtn, "device");
            AddZone(_liveBtn, "live");
            AddZone(_fxBtn, "fx");
            AddZone(_sbBtn, "soundboard");
            AddZone(_hotRect, "hotkey");
            AddZone(_muteBtn, "mute");
        }

        protected override void DrawTop(Graphics g)
        {
            var dev = _dev;
            Color col = AccentColor;
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);
            Theme.DrawColorEdge(g, PanelRect, col);

            // Name
            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Theme.Darken(col, 0.45f) : col;
            Theme.DrawNamePlate(g, nr, bg);
            DrawPlateNumber(g, nr, bg);
            using (var b = new SolidBrush(Theme.PlateText(bg)))
                Theme.Str(g, "MIKRO", Theme.FitPlate(g, "MIKRO", NameFitWidth(nr), 12.5f, 8f), b, NameTextRect(nr), Theme.Center);

            // Geraet
            var ir = _infoRect;
            bool hoverDev = HoverZone == "device";
            Theme.DrawWell(g, ir, hoverDev);
            if (dev != null)
            {
                string sub = dev.FullName ?? "";
                int p = sub.IndexOf('(');
                if (p > 0 && sub.EndsWith(")")) sub = sub.Substring(p + 1, sub.Length - p - 2);
                if (_voiceTarget != null) sub = "→ " + _voiceTarget;
                using (var b = new SolidBrush(Theme.Text))
                    Theme.Str(g, dev.Name, Theme.Fit(g, dev.Name, ir.Width - Theme.F(8), 11.5f, 8.5f), b,
                        new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(4), ir.Width - Theme.F(6), Theme.F(18)), Theme.Center);
                using (var b = new SolidBrush(_voiceTarget != null ? ChannelStrip.StreamColor : Theme.TextDim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, sub, Theme.Ui(9f), b, new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(23), ir.Width - Theme.F(6), Theme.F(16)), sf);
            }
            else
            {
                using (var b = new SolidBrush(Theme.MuteRed))
                    Theme.Str(g, "Kein Mikrofon", Theme.Ui(10f), b, ir, Theme.Center);
            }
            Theme.DrawDisplayButton(g, _devBtn, "GERÄT WÄHLEN", hoverDev, Theme.Text);

            Theme.Engrave(g, left, right, Theme.F(116 + RowOffset));

            // Live-Pegel
            Theme.DrawKey(g, _liveBtn, "LIVE-PEGEL", _live, Theme.LedGreen, HoverZone == "live");
            // Mikro-Effekte (EQ, Kompressor ... im Sprach-Ausgang)
            Theme.DrawKey(g, _fxBtn, "EFFEKTE", _fx == 2, Theme.LedGreen, HoverZone == "fx", _fx == 1);

            Theme.Engrave(g, left, right, Theme.F(188 + RowOffset));
            Theme.DrawKey(g, _sbBtn, "SOUNDBOARD", _soundsPlaying, Theme.LedGreen, HoverZone == "soundboard");
            Theme.Engrave(g, left, right, Theme.F(188 + DynRow + RowOffset));

            // Hotkey bzw. Push-to-Talk-Taste
            string title = _pttMode == 1 ? "PUSH-TO-TALK" : _pttMode == 2 ? "PUSH-TO-MUTE" : "HOTKEY STUMM";
            string keyText = _pttMode != 0 ? (_pttKey.Length > 0 ? _pttKey : "keine Taste") : (string.IsNullOrEmpty(_hotkey) ? "keiner" : _hotkey);
            Theme.Label(g, title, new RectangleF(_hotRect.X, _hotRect.Y - Theme.F(2), _hotRect.Width, Theme.F(12)), Theme.Center,
                _pttMode != 0 ? Theme.AccentText : Theme.TextFaint, 8f);
            var hk = new RectangleF(_hotRect.X, _hotRect.Y + Theme.F(11), _hotRect.Width, Theme.F(17));
            Theme.FillRound(g, HoverZone == "hotkey" ? Theme.Lighten(Theme.LcdBg, 0.06f) : Theme.LcdBg, hk, Theme.F(3));
            bool empty = _pttMode == 0 ? string.IsNullOrEmpty(_hotkey) : _pttKey.Length == 0;
            using (var b = new SolidBrush(empty ? Theme.TextFaint : (_pttMode != 0 && _pttHeld ? Theme.LedGreen : Theme.LcdText)))
                Theme.Str(g, keyText, Theme.Fit(g, keyText, hk.Width - Theme.F(6), 10f, 7.5f), b, hk, Theme.Center);

            Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset));
            DrawMuteKey(g, _muteBtn, MuteLit);
            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Mixer.ShowMicMenu(PointToScreen(e.Location));
                return false;
            }
            if (e.Button != MouseButtons.Left) return false;
            switch (zone)
            {
                case "name":
                case "device":
                    Mixer.ShowMicDeviceMenu(PointToScreen(new Point((int)_devBtn.X, (int)_devBtn.Bottom + 2)));
                    break;
                case "fx":
                    Mixer.ShowMicFxDialog();
                    break;
                case "live":
                    Mixer.SetMicLive(!_live);
                    break;
                case "soundboard":
                    Mixer.ShowSoundboard();
                    break;
                case "hotkey":
                    if (_pttMode != 0) Mixer.ShowPttDialog();
                    else Mixer.ShowHotkeyDialog();
                    break;
                case "mute":
                    Mixer.ToggleMicMute(false);
                    InvalidateChrome();
                    break;
            }
            return false;
        }

        protected override bool IsMoveHandle(string zone) => zone == "name";

        protected override void OnHandleClick(string zone, MouseEventArgs e)
        {
            Mixer.ShowMicDeviceMenu(PointToScreen(new Point((int)_devBtn.X, (int)_devBtn.Bottom + 2)));
        }

        protected override string GetTooltip(string zone)
        {
            switch (zone)
            {
                case "name": return Parent is StripHost h && h.CanMove(this)
                    ? "MIKRO – ziehen zum Verschieben · Klick: Gerät wählen · Rechtsklick: Optionen"
                    : "MIKRO – Klick: Gerät wählen · Rechtsklick: Optionen";
                case "device": return (_dev?.FullName ?? "Kein Mikrofon") + "\nKlicken, um das Windows-Aufnahmegerät zu wechseln" +
                                      (_voiceTarget != null ? "\n\nSprach-Ausgang: Mikro + Soundboard gehen zu " + _voiceTarget : "");
                case "soundboard": return "Soundboard öffnen: Sounds per Klick oder Hotkey abspielen";
                case "fx":
                    return "Mikro-Effekte für Discord: Rumpelfilter, Stimme klarer, EQ, Kompressor" +
                           (_fx == 2 ? "\nAn – wirken im Sprach-Ausgang" : _fx == 1 ? "\nEingestellt, aber der Sprach-Ausgang ist aus – ohne ihn erreichen die Effekte Discord nicht" : "\nZurzeit aus") +
                           "\n\nKlicken zum Einstellen";
                case "live":
                    return "Windows zeigt den Mikro-Pegel nur, wenn eine App aufnimmt.\n" +
                           "LIVE-PEGEL hält das Mikro offen, damit der Pegel immer sichtbar ist\n(Windows zeigt dann das Mikrofon-Symbol in der Taskleiste).";
                case "hotkey":
                    return _pttMode == 1 ? "Push-to-Talk: Mikro ist nur an, solange " + _pttKey + " gedrückt ist – klicken zum Ändern"
                         : _pttMode == 2 ? "Push-to-Mute: Mikro ist stumm, solange " + _pttKey + " gedrückt ist – klicken zum Ändern"
                         : "Tastenkürzel zum Stummschalten – klicken zum Ändern\n(Push-to-Talk: Rechtsklick → Push-to-Talk …)";
                case "mute":
                    return (_pttMode != 0 ? "Mikro ganz stumm (auch beim Drücken der Push-to-Talk-Taste)" :
                            _pathMute != null ? "Mikrofon stummschalten" : "Mikrofon stummschalten (für alle Apps)") +
                           (_pathMute != null ? "\nNur im Sprach-Ausgang – das Windows-Mikro bleibt für andere Apps an." : "");
            }
            return null;
        }
    }
}
