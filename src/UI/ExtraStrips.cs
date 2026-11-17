using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>Gruppe (VCA): ein Fader und MUTE fuer mehrere Kanaele zugleich.</summary>
    internal sealed class GroupStrip : StripBase
    {
        public readonly GroupConfig Config;
        private RectangleF _nameRect, _membersRect, _membersBtn, _muteBtn;
        private List<KeyValuePair<string, Color>> _members = new List<KeyValuePair<string, Color>>();

        public GroupStrip(MixerForm mixer, GroupConfig config) : base(mixer) { Config = config; }

        public override int StripWidth => Theme.Px(Mixer.Narrow ? 86 : 108);
        protected override bool Narrow => Mixer.Narrow;
        protected override float FaderPos => Config.Fader;
        protected override bool DbFader => true;
        protected override bool CanMultiSelect => true;   // 3.5
        protected override void OnMiddleClick() { Config.Mute = !Config.Mute; Mixer.OnGroupChanged(Config); }
        protected override Color AccentColor => Color.FromArgb(Config.Color);
        public override string DisplayName => Config.Name;
        public override string BridgeKey => "grp:" + Config.Id;
        protected override RectangleF MuteKey => _muteBtn;
        protected override bool MuteLit => Config.Mute;
        protected override float DefaultFaderPos => 1f;
        protected override bool FaderLocked => Config.FaderLock;
        protected override float FaderMarkPos => Config.FaderMark ?? float.NaN;
        protected override bool Dimmed => Config.Mute;
        protected override string ReadoutText => FaderLaw.FormatDb(Config.Fader);
        protected override string FaderTipSuffix => "  ·  wirkt auf alle Kanäle der Gruppe" + (Config.FaderMark.HasValue ? "" : "  ·  Doppelklick = 0 dB");

        protected override void DrawOverlay(Graphics g)
        {
            if (!Config.FaderLock) return;
            // Schloss: Fader gesperrt
            var r = new RectangleF(PanelRect.X + Theme.F(Narrow ? 4 : 6), FaderArea.Y + Theme.F(1), Theme.F(15), Theme.F(11));
            Theme.FillRound(g, Theme.IsLight ? Theme.Darken(Theme.PanelBottom, 0.35f) : Color.FromArgb(0x5a, 0x60, 0x6a), r, Theme.F(3));
            DrawPadlock(g, new RectangleF(r.X + Theme.F(4), r.Y + Theme.F(1.5f), Theme.F(7), Theme.F(8.5f)), Theme.IsLight ? Color.White : Color.FromArgb(0xe8, 0xeb, 0xef));
        }

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                yield return new KeyValuePair<float, string>(1f, "0");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-5), "-5");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-10), "-10");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-20), "-20");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-30), "-30");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-40), "-40");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-60), "-60");
                yield return new KeyValuePair<float, string>(0f, "∞");
            }
        }

        public void SetMembers(List<KeyValuePair<string, Color>> members)
        {
            bool same = members.Count == _members.Count;
            for (int i = 0; same && i < members.Count; i++) same = members[i].Key == _members[i].Key && members[i].Value == _members[i].Value;
            if (same) return;
            _members = members;
            InvalidateChrome();
        }

        protected override void OnFaderMoved(float pos, bool final)
        {
            Config.Fader = pos;
            Mixer.OnGroupChanged(Config);
        }

        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _membersRect = new RectangleF(x, Theme.F(42), iw, Theme.F(146 + RowOffset));
            _membersBtn = new RectangleF(x, Theme.F(193 + RowOffset), iw, Theme.F(20));
            _muteBtn = new RectangleF(x, Theme.F(237 + DynRow + RowOffset), iw, Theme.F(22));
            AddZone(_nameRect, "name");
            AddZone(_membersRect, "members");
            AddZone(_membersBtn, "members");
            AddZone(_muteBtn, "mute");
        }

        protected override void DrawTop(Graphics g)
        {
            Color col = Color.FromArgb(Config.Color);
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);
            // schraeg gestreifte Kante: Gruppe statt Kanal
            Theme.DrawColorEdge(g, PanelRect, col, true);

            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Theme.Darken(col, 0.45f) : col;
            Theme.DrawNamePlate(g, nr, bg);
            DrawPlateNumber(g, nr, bg);
            using (var b = new SolidBrush(Theme.PlateText(bg)))
            {
                var font = Theme.FitPlate(g, Config.Name, NameFitWidth(nr), 12.5f, 7.5f);
                bool fits = Theme.TextWidth(g, Config.Name, font) <= NameFitWidth(nr) + Theme.F(2);
                // zu lang fuer eine Zeile: zweizeilig, kleinere Schrift
                using (var sf = new StringFormat(Theme.Center) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = fits ? StringFormatFlags.NoWrap : 0 })
                    Theme.Str(g, Config.Name, fits ? font : Theme.Ui(8f, FontStyle.Bold), b, NameTextRect(nr), sf);
            }

            // Mitglieder
            var r = _membersRect;
            bool hover = HoverZone == "members";
            Theme.DrawWell(g, r, hover);
            Theme.Label(g, "GRUPPE", new RectangleF(r.X, r.Y + Theme.F(4), r.Width, Theme.F(12)), Theme.Center, Theme.TextFaint, 8f);
            if (_members.Count == 0)
            {
                using (var b = new SolidBrush(hover ? Theme.TextDim : Theme.TextFaint))
                    Theme.Str(g, "+ Kanäle\nwählen", Theme.Ui(9.5f), b, r, Theme.Center);
            }
            else
            {
                float lh = Theme.F(15), y = r.Y + Theme.F(20);
                int maxLines = Math.Max(1, (int)((r.Bottom - y - Theme.F(4)) / lh));
                int shown = _members.Count > maxLines ? maxLines - 1 : _members.Count;
                using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    for (int i = 0; i < shown; i++, y += lh)
                    {
                        Theme.FillEllipse(g, _members[i].Value, r.X + Theme.F(9), y + lh / 2, Theme.F(3.5f));
                        using (var b = new SolidBrush(Theme.Text))
                            Theme.Str(g, _members[i].Key, Theme.Ui(9.5f), b, new RectangleF(r.X + Theme.F(16), y, r.Width - Theme.F(19), lh), sf);
                    }
                    if (_members.Count > shown)
                        using (var b = new SolidBrush(Theme.TextDim))
                            Theme.Str(g, "+" + (_members.Count - shown) + " weitere", Theme.Ui(9f), b, new RectangleF(r.X + Theme.F(16), y, r.Width - Theme.F(19), lh), sf);
                }
            }
            Theme.DrawDisplayButton(g, _membersBtn, "KANÄLE", hover, Theme.Text);

            Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset));
            DrawMuteKey(g, _muteBtn, Config.Mute);
            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Mixer.ShowGroupMenu(this, PointToScreen(e.Location)); return false; }
            if (e.Button != MouseButtons.Left) return false;
            if (zone == "members") Mixer.ShowGroupMembersMenu(Config, PointToScreen(new Point((int)_membersBtn.X, (int)_membersBtn.Bottom + 2)));
            else if (zone == "mute") { Config.Mute = !Config.Mute; Mixer.OnGroupChanged(Config); InvalidateChrome(); }
            return false;
        }

        protected override bool IsMoveHandle(string zone) => zone == "name";

        protected override bool OnZoneDoubleClick(string zone, MouseEventArgs e)
        {
            if (zone == "name") { Mixer.RenameGroup(Config); return true; }
            return false;
        }

        protected override string GetTooltip(string zone)
        {
            switch (zone)
            {
                case "name": return "Gruppe – Ziehen: verschieben · Doppelklick: umbenennen · Rechtsklick: Farbe, löschen";
                case "members":
                    return _members.Count == 0 ? "Klicken, um Kanäle in die Gruppe zu nehmen"
                        : "Der Gruppen-Fader macht diese Kanäle gemeinsam lauter/leiser:\n" + string.Join(", ", _members.ConvertAll(m => m.Key));
                case "mute": return "Alle Kanäle der Gruppe stummschalten";
            }
            return null;
        }
    }

    /// <summary>Stream-Mix: zweiter Mix auf einem eigenen Geraet (z. B. VB-Cable fuer OBS).</summary>
    internal sealed class StreamStrip : StripBase
    {
        private RectangleF _nameRect, _infoRect, _devBtn, _listRect, _muteBtn;
        private List<KeyValuePair<string, Color>> _channels = new List<KeyValuePair<string, Color>>();
        private string _device, _deviceSub;
        private bool _missing;

        public StreamStrip(MixerForm mixer) : base(mixer) { }

        public override int StripWidth => Theme.Px(118);
        protected override Color AccentColor => ChannelStrip.StreamColor;
        /// <summary>4.0 (finale): Absenkung des Stream-Limiters (dB, −1 = Limiter aus).</summary>
        public static float LimitGr = -1f;
        protected override bool HasGr => Mixer.StreamLimitWanted;
        protected override float GrTarget => Math.Max(0f, LimitGr);
        protected override string GrTooltip => "Stream-Limiter (−1 dB True-Peak): " + (LimitGr > 0.05f ? "−" + LimitGr.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " dB" : "greift gerade nicht");
        public override string DisplayName => "STREAM";
        public override string BridgeKey => "stream";
        protected override RectangleF MuteKey => _muteBtn;
        protected override bool MuteLit => Mixer.StreamMute;
        protected override float MeterBarWidth => 7f;
        protected override float DefaultFaderPos => 1f;
        protected override bool Dimmed => Mixer.StreamMute || _device == null;
        protected override float FaderPos => Mixer.StreamMasterDb <= -60f ? 0f : FaderLaw.DbToPos(Mixer.StreamMasterDb);
        protected override bool DbFader => true;
        protected override void OnMiddleClick() => Mixer.ToggleStreamMute();   // 3.5
        protected override string ReadoutText => Mixer.StreamMute ? "STUMM" : FaderLaw.FormatDb(FaderPos);

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                yield return new KeyValuePair<float, string>(1f, "0");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-10), "-10");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-20), "-20");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-40), "-40");
                yield return new KeyValuePair<float, string>(0f, "∞");
            }
        }

        public void SetState(string device, string deviceSub, bool missing, List<KeyValuePair<string, Color>> channels)
        {
            bool same = device == _device && deviceSub == _deviceSub && missing == _missing && channels.Count == _channels.Count;
            for (int i = 0; same && i < channels.Count; i++) same = channels[i].Key == _channels[i].Key && channels[i].Value == _channels[i].Value;
            if (same) return;
            _device = device; _deviceSub = deviceSub; _missing = missing; _channels = channels;
            InvalidateChrome();
        }

        protected override void OnFaderMoved(float pos, bool final)
        {
            float db = FaderLaw.PosToDb(pos);
            Mixer.SetStreamMaster(float.IsNegativeInfinity(db) ? -60f : db);
        }

        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _infoRect = new RectangleF(x, Theme.F(42), iw, Theme.F(44));
            _devBtn = new RectangleF(x, Theme.F(90), iw, Theme.F(20));
            _listRect = new RectangleF(x, Theme.F(138), iw, Theme.F(90 + RowOffset));
            _muteBtn = new RectangleF(x, Theme.F(237 + DynRow + RowOffset), iw, Theme.F(22));
            AddZone(_nameRect, "device");
            AddZone(_infoRect, "device");
            AddZone(_devBtn, "device");
            AddZone(_listRect, "list", false);
            AddZone(_muteBtn, "mute");
        }

        protected override void DrawTop(Graphics g)
        {
            Color col = ChannelStrip.StreamColor;
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);
            Theme.DrawColorEdge(g, PanelRect, col);
            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Theme.Darken(col, 0.45f) : col;
            Theme.DrawNamePlate(g, nr, bg);
            DrawPlateNumber(g, nr, bg);
            using (var b = new SolidBrush(Theme.PlateText(bg)))
                Theme.Str(g, "STREAM", Theme.FitPlate(g, "STREAM", NameFitWidth(nr), 12.5f, 8f), b, NameTextRect(nr), Theme.Center);

            var ir = _infoRect;
            bool hoverDev = HoverZone == "device";
            Theme.DrawWell(g, ir, hoverDev);
            if (_device != null)
            {
                using (var b = new SolidBrush(_missing ? Theme.MuteRed : Theme.Text))
                    Theme.Str(g, _device, Theme.Fit(g, _device, ir.Width - Theme.F(8), 11.5f, 8.5f), b,
                        new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(4), ir.Width - Theme.F(6), Theme.F(18)), Theme.Center);
                using (var b = new SolidBrush(Theme.TextDim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, _missing ? "nicht angeschlossen" : _deviceSub ?? "", Theme.Ui(9f), b, new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(23), ir.Width - Theme.F(6), Theme.F(16)), sf);
            }
            else
            {
                using (var b = new SolidBrush(Theme.WarnText))
                    Theme.Str(g, "Gerät wählen\n(z. B. CABLE Input)", Theme.Ui(9.5f), b, ir, Theme.Center);
            }
            Theme.DrawDisplayButton(g, _devBtn, "GERÄT WÄHLEN", hoverDev, Theme.Text);

            Theme.Engrave(g, left, right, Theme.F(116));
            Theme.Label(g, "IM STREAM", new RectangleF(_listRect.X, Theme.F(121), _listRect.Width, Theme.F(14)), Theme.Center, Theme.TextFaint, 8.5f);
            var r = _listRect;
            Theme.DrawWell(g, r, false);
            if (_channels.Count == 0)
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, "kein Kanal", Theme.Ui(9.5f), b, r, Theme.Center);
            float lh = Theme.F(14.5f), y = r.Y + Theme.F(4);
            int maxLines = Math.Max(1, (int)((r.Height - Theme.F(6)) / lh));
            using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                for (int i = 0; i < _channels.Count && i < maxLines; i++, y += lh)
                {
                    bool last = i == maxLines - 1 && _channels.Count > maxLines;
                    if (!last) Theme.FillEllipse(g, _channels[i].Value, r.X + Theme.F(9), y + lh / 2, Theme.F(3.5f));
                    using (var b = new SolidBrush(last ? Theme.TextDim : Theme.Text))
                        Theme.Str(g, last ? "+" + (_channels.Count - i) + " weitere" : _channels[i].Key, Theme.Ui(9f), b, new RectangleF(r.X + Theme.F(16), y, r.Width - Theme.F(19), lh), sf);
                }

            Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset));
            DrawMuteKey(g, _muteBtn, Mixer.StreamMute);
            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || (zone == "device" && e.Button == MouseButtons.Left))
            {
                Mixer.ShowStreamDeviceMenu(PointToScreen(zone == "device" ? new Point((int)_devBtn.X, (int)_devBtn.Bottom + 2) : e.Location));
                return false;
            }
            if (zone == "mute" && e.Button == MouseButtons.Left) { Mixer.ToggleStreamMute(); InvalidateChrome(); }
            return false;
        }

        protected override string GetTooltip(string zone)
        {
            switch (zone)
            {
                case "device":
                    return "Stream-Mix geht auf: " + (_device ?? "noch kein Gerät") +
                           "\nIn OBS als Audio-Eingabe (bei VB-Cable: »CABLE Output«) hinzufügen." +
                           "\nAnteil pro Kanal: Regler STREAM im Kanal.";
                case "list": return "Diese Kanäle gehen in den Stream-Mix (Regler STREAM im Kanal).";
                case "mute": return "Stream-Mix stummschalten";
            }
            return null;
        }
    }
}
