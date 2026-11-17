using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.3: Kabel-Ansicht wie ein Steckfeld – links die Kanaele, rechts die Ausgaenge, dazwischen haengen Kabel in der Kanalfarbe.
    /// Kabel ziehen: von einer Kanal-Buchse auf eine Ausgangs-Buchse. Auf den Standard-Ausgang gezogen = "Standard".
    /// </summary>
    internal sealed class PatchbayWindow : Form
    {
        private readonly Func<IList<ChannelConfig>> _channels;
        private readonly Func<IList<DeviceState>> _devices;
        private readonly Func<bool> _supported;
        private readonly Action<ChannelConfig, string, string> _route;
        private readonly Panel _canvas;
        private readonly ToolTip _tip = PultTip.Make(new ToolTip { InitialDelay = 400, AutoPopDelay = 6000 });

        private readonly List<KeyValuePair<ChannelConfig, PointF>> _chJacks = new List<KeyValuePair<ChannelConfig, PointF>>();
        private readonly List<KeyValuePair<DeviceState, PointF>> _outJacks = new List<KeyValuePair<DeviceState, PointF>>();
        private ChannelConfig _drag;
        private PointF _dragPt;
        private string _sig;
        private string _tipShown;

        public PatchbayWindow(Form owner, Func<IList<ChannelConfig>> channels, Func<IList<DeviceState>> devices, Func<bool> supported, Action<ChannelConfig, string, string> route)
        {
            _channels = channels; _devices = devices; _supported = supported; _route = route;
            Text = "Kabel-Ansicht";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(Theme.Px(460), Theme.Px(260));
            Size = new Size(Theme.Px(680), Theme.Px(460));
            BackColor = MixerForm.BodyColor;
            KeyPreview = true;
            try { Icon = owner?.Icon; } catch { }
            _canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = MixerForm.BodyColor };
            _canvas.Paint += (s, e) => PaintAll(e.Graphics);
            _canvas.MouseDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseUp += OnUp;
            _canvas.Resize += (s, e) => _canvas.Invalidate();
            Controls.Add(_canvas);
        }

        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { if (_drag != null) { _drag = null; _canvas.Invalidate(); } else Close(); }
        }

        /// <summary>Aus dem Takt: neu zeichnen, wenn sich Kanaele, Ausgaenge oder Wege geaendert haben.</summary>
        public void Refresh330()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in _channels()) sb.Append(c.Id).Append(c.Name).Append(c.Color).Append(c.OutputDevice).Append('|');
            foreach (var d in _devices()) sb.Append(d.Id).Append(d.FullName).Append(d.IsDefault).Append('|');
            sb.Append(_supported());
            string s = sb.ToString();
            if (s == _sig) return;
            _sig = s;
            _canvas.Invalidate();
        }

        private DeviceState Target(ChannelConfig c, IList<DeviceState> devs)
        {
            if (c.OutputDevice == null) return devs.FirstOrDefault(d => d.IsDefault);
            return devs.FirstOrDefault(d => string.Equals(d.Id, c.OutputDevice, StringComparison.OrdinalIgnoreCase));
        }

        private void PaintAll(Graphics g)
        {
            Theme.Setup(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = _canvas.ClientRectangle;
            using (var b = new SolidBrush(MixerForm.BodyColor)) g.FillRectangle(b, area);
            var chs = _channels();
            var devs = _devices();
            float pad = Theme.F(16), top = Theme.F(40), rowH = Theme.F(34);
            float colW = Math.Min(Theme.F(220), area.Width * 0.34f);
            var left = new RectangleF(pad, top, colW, area.Height - top - pad);
            var right = new RectangleF(area.Width - pad - colW, top, colW, area.Height - top - pad);
            var cap = Theme.UiSemi(9.5f);
            using (var b = new SolidBrush(Theme.BodyText))
            {
                Theme.Str(g, "KANÄLE", cap, b, new RectangleF(left.X, Theme.F(12), left.Width, Theme.F(18)), Theme.Left);
                Theme.Str(g, "AUSGÄNGE", cap, b, new RectangleF(right.X, Theme.F(12), right.Width, Theme.F(18)), Theme.Right);
            }
            bool ok = _supported();
            if (!ok)
                using (var b = new SolidBrush(Theme.WarnText))
                    Theme.Str(g, "App-Umleitung wird von diesem Windows nicht unterstützt – nur Ansicht.", Theme.Ui(9f), b, new RectangleF(left.Right, Theme.F(12), right.X - left.Right, Theme.F(18)), Theme.Center);
            // Felder (Steckfeld-Blech)
            float lh = Math.Max(rowH, chs.Count * rowH) + Theme.F(10), rh = Math.Max(rowH, devs.Count * rowH) + Theme.F(10);
            var lPanel = new RectangleF(left.X, left.Y, left.Width, Math.Min(left.Height, lh));
            var rPanel = new RectangleF(right.X, right.Y, right.Width, Math.Min(right.Height, rh));
            Theme.DrawPanel(g, lPanel, Theme.F(6));
            Theme.DrawPanel(g, rPanel, Theme.F(6));
            _chJacks.Clear(); _outJacks.Clear();
            var font = Theme.UiSemi(9.5f);
            for (int i = 0; i < chs.Count; i++)
            {
                var c = chs[i];
                float y = lPanel.Y + Theme.F(5) + i * rowH;
                if (y + rowH > lPanel.Bottom + 1) break;
                var row = new RectangleF(lPanel.X + Theme.F(8), y + Theme.F(4), lPanel.Width - Theme.F(36), rowH - Theme.F(8));
                Color col = Color.FromArgb(c.Color);
                Theme.FillRound(g, Theme.Darken(col, 0.15f), row, Theme.F(3));
                using (var b = new SolidBrush(Theme.TextOn(col)))
                using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, c.Name, font, b, RectangleF.Inflate(row, -Theme.F(6), 0), sf);
                var jack = new PointF(lPanel.Right - Theme.F(14), y + rowH / 2f);
                DrawJack(g, jack, col);
                _chJacks.Add(new KeyValuePair<ChannelConfig, PointF>(c, jack));
            }
            for (int i = 0; i < devs.Count; i++)
            {
                var d = devs[i];
                float y = rPanel.Y + Theme.F(5) + i * rowH;
                if (y + rowH > rPanel.Bottom + 1) break;
                var row = new RectangleF(rPanel.X + Theme.F(28), y + Theme.F(4), rPanel.Width - Theme.F(36), rowH - Theme.F(8));
                Theme.FillRound(g, Color.FromArgb(0x22, 0x25, 0x2b), row, Theme.F(3));
                using (var b = new SolidBrush(Theme.Text))
                using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, (d.IsDefault ? "★ " : "") + (d.Name ?? d.FullName), font, b, RectangleF.Inflate(row, -Theme.F(6), 0), sf);
                var jack = new PointF(rPanel.X + Theme.F(14), y + rowH / 2f);
                DrawJack(g, jack, Color.FromArgb(0x9a, 0xa0, 0xa8));
                _outJacks.Add(new KeyValuePair<DeviceState, PointF>(d, jack));
            }
            // Kabel
            foreach (var kv in _chJacks)
            {
                if (kv.Key == _drag) continue;
                var t = Target(kv.Key, devs);
                if (t == null) continue;
                var oj = _outJacks.FirstOrDefault(o => o.Key == t);
                if (oj.Key == null) continue;
                DrawCable(g, kv.Value, oj.Value, Color.FromArgb(kv.Key.Color), kv.Key.OutputDevice == null);
            }
            if (_drag != null)
            {
                var from = _chJacks.FirstOrDefault(k => k.Key == _drag);
                if (from.Key != null) DrawCable(g, from.Value, _dragPt, Color.FromArgb(_drag.Color), false);
            }
            using (var b = new SolidBrush(Theme.TextDim))
                Theme.Str(g, ok ? "Kabel von einer Kanal-Buchse auf einen Ausgang ziehen · auf ★ = Standard (folgt Windows) · gestrichelt = Standard"
                                : "gestrichelt = Standard (folgt Windows)", Theme.Ui(8.5f), b,
                    new RectangleF(pad, area.Height - Theme.F(20), area.Width - pad * 2, Theme.F(16)), Theme.Center);
        }

        private static void DrawJack(Graphics g, PointF c, Color ring)
        {
            float r = Theme.F(7.5f);
            using (var b = new LinearGradientBrush(new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), Color.FromArgb(0xe0, 0xe3, 0xe8), Color.FromArgb(0x4a, 0x4e, 0x55), LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
            using (var pen = new Pen(ring, Math.Max(1.5f, Theme.F(2)))) g.DrawEllipse(pen, c.X - r + Theme.F(1), c.Y - r + Theme.F(1), r * 2 - Theme.F(2), r * 2 - Theme.F(2));
            float h = r * 0.45f;
            using (var b = new SolidBrush(Color.FromArgb(0x05, 0x05, 0x06))) g.FillEllipse(b, c.X - h, c.Y - h, h * 2, h * 2);
        }

        private static void DrawCable(Graphics g, PointF a, PointF b, Color col, bool dashed)
        {
            float sag = Math.Min(Theme.F(90), Math.Abs(b.X - a.X) * 0.25f + Math.Abs(b.Y - a.Y) * 0.15f + Theme.F(20));
            var c1 = new PointF(a.X + (b.X - a.X) * 0.3f, Math.Max(a.Y, b.Y) + sag);
            var c2 = new PointF(a.X + (b.X - a.X) * 0.7f, Math.Max(a.Y, b.Y) + sag);
            using (var pen = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(3f, Theme.F(5))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, a.X, a.Y + Theme.F(2), c1.X, c1.Y + Theme.F(3), c2.X, c2.Y + Theme.F(3), b.X, b.Y + Theme.F(2));
            using (var pen = new Pen(col, Math.Max(2.5f, Theme.F(3.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (dashed) pen.DashStyle = DashStyle.Dash;
                g.DrawBezier(pen, a, c1, c2, b);
            }
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, Theme.F(1))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, a.X, a.Y - Theme.F(1), c1.X, c1.Y - Theme.F(1), c2.X, c2.Y - Theme.F(1), b.X, b.Y - Theme.F(1));
            float s = Theme.F(4);
            using (var br = new SolidBrush(Theme.Darken(col, 0.2f)))
            {
                g.FillEllipse(br, a.X - s, a.Y - s, s * 2, s * 2);
                g.FillEllipse(br, b.X - s, b.Y - s, s * 2, s * 2);
            }
        }

        private static bool Near(PointF p, PointF q) => Math.Abs(p.X - q.X) <= Theme.F(14) && Math.Abs(p.Y - q.Y) <= Theme.F(14);

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_supported()) return;
            foreach (var kv in _chJacks)
                if (Near(e.Location, kv.Value)) { _drag = kv.Key; _dragPt = e.Location; _canvas.Capture = true; _canvas.Invalidate(); return; }
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_drag != null) { _dragPt = e.Location; _canvas.Invalidate(); return; }
            string tip = null;
            foreach (var kv in _chJacks) if (Near(e.Location, kv.Value)) { tip = kv.Key.Name + " – Kabel ziehen, um den Ausgang zu wählen"; break; }
            if (tip == null) foreach (var kv in _outJacks) if (Near(e.Location, kv.Value)) { tip = kv.Key.FullName + (kv.Key.IsDefault ? " (Windows-Standard)" : ""); break; }
            if (tip != _tipShown)
            {
                _tipShown = tip;
                if (tip == null) _tip.Hide(_canvas); else _tip.Show(tip, _canvas, e.X + Theme.Px(14), e.Y + Theme.Px(14), 5000);
            }
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (_drag == null) return;
            var c = _drag;
            _drag = null;
            _canvas.Capture = false;
            foreach (var kv in _outJacks)
                if (Near(e.Location, kv.Value))
                {
                    if (kv.Key.IsDefault) _route(c, null, null);   // Standard: folgt Windows
                    else _route(c, kv.Key.Id, kv.Key.FullName);
                    break;
                }
            _sig = null;
            _canvas.Invalidate();
        }
    }
}
