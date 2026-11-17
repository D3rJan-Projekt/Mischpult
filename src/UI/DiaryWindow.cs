using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>2.17: Hoer-Tagebuch im eigenen Fenster – die letzten 14 Tage als Balken (rot = laut), heute und die Apps.</summary>
    internal sealed class DiaryWindow : Form
    {
        private readonly Func<ListeningDiary> _diary;
        private readonly Func<float> _loudFrom;
        private readonly DiaryPanel _panel;

        public DiaryWindow(MixerForm mixer, Func<ListeningDiary> diary, Func<float> loudFrom)
        {
            _diary = diary;
            _loudFrom = loudFrom;
            Text = "Hör-Tagebuch – Mischpult";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(Theme.Px(520), Theme.Px(380));
            Size = new Size(Theme.Px(720), Theme.Px(460));
            BackColor = MixerForm.BodyColor;
            KeyPreview = true;
            try { Icon = mixer.Icon; } catch { }
            _panel = new DiaryPanel(this) { Dock = DockStyle.Fill };
            Controls.Add(_panel);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) Close(); }

        private int _ver = -1;
        /// <summary>Aus dem Takt: neu zeichnen, wenn sich etwas getan hat (hoechstens alle paar Sekunden noetig).</summary>
        public void Tick()
        {
            var d = _diary();
            if (d != null && d.Version / 5 != _ver) { _ver = d.Version / 5; _panel.Invalidate(); }
        }

        private sealed class DiaryPanel : Control
        {
            private readonly DiaryWindow _w;
            public DiaryPanel(DiaryWindow w)
            {
                _w = w;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Setup(g);
                g.Clear(Color.FromArgb(0x12, 0x13, 0x16));
                var d = _w._diary();
                if (d == null) return;
                var now = DateTime.Now;
                float pad = Theme.F(18);
                var white = Color.FromArgb(0xf2, 0xf4, 0xf6);
                var dim = Color.FromArgb(0x93, 0x9b, 0xa6);
                var faint = Color.FromArgb(0x5a, 0x62, 0x6e);

                // ---- Heute
                var today = d.Get(now, false) ?? new ListeningDiary.Day();
                using (var b = new SolidBrush(white))
                    Theme.Str(g, "Heute", Theme.Ui(15f, FontStyle.Bold), b, new RectangleF(pad, pad - Theme.F(4), Theme.F(200), Theme.F(24)), Theme.Left);
                float y = pad + Theme.F(24);
                DrawStat(g, new RectangleF(pad, y, Theme.F(150), Theme.F(44)), "GEHÖRT", ListeningDiary.Duration(today.ListenSec), white);
                DrawStat(g, new RectangleF(pad + Theme.F(160), y, Theme.F(150), Theme.F(44)), "DAVON LAUT", ListeningDiary.Duration(today.LoudSec),
                    today.LoudSec > 2 * 3600 ? Theme.MeterRed : today.LoudSec > 3600 ? Theme.MeterYellow : Theme.MeterGreen);
                double avg = today.AvgLevel;
                DrawStat(g, new RectangleF(pad + Theme.F(320), y, Theme.F(150), Theme.F(44)), "IM SCHNITT",
                    double.IsNaN(avg) ? "–" : avg.ToString("0", De).Replace("-", "−") + " dB", dim);

                // ---- 14 Tage
                float chartTop = y + Theme.F(66), chartH = Math.Max(Theme.F(80), Height - chartTop - Theme.F(150));
                float chartW = Width - 2 * pad;
                using (var b = new SolidBrush(white))
                    Theme.Str(g, "Letzte 14 Tage", Theme.Ui(11f, FontStyle.Bold), b, new RectangleF(pad, chartTop - Theme.F(20), Theme.F(200), Theme.F(16)), Theme.Left);
                double maxSec = 3600;
                for (int i = 0; i < 14; i++) { var dd = d.Get(now.AddDays(-i), false); if (dd != null) maxSec = Math.Max(maxSec, dd.ListenSec); }
                maxSec = Math.Ceiling(maxSec / 3600.0) * 3600.0;
                var plot = new RectangleF(pad + Theme.F(34), chartTop, chartW - Theme.F(34), chartH);
                using (var pen = new Pen(Color.FromArgb(0x24, 0x27, 0x2d), 1f))
                using (var lb = new SolidBrush(faint))
                    for (int h = 0; h <= (int)(maxSec / 3600); h += Math.Max(1, (int)(maxSec / 3600) / 4))
                    {
                        float yy = (float)Math.Round(plot.Bottom - plot.Height * (h * 3600 / maxSec)) + 0.5f;
                        g.DrawLine(pen, plot.X, yy, plot.Right, yy);
                        Theme.Str(g, h + " h", Theme.Ui(8f), lb, new RectangleF(pad, yy - Theme.F(6), Theme.F(30), Theme.F(12)), Theme.Right);
                    }
                float slot = plot.Width / 14f, bw = Math.Max(Theme.F(6), slot * 0.62f);
                for (int i = 0; i < 14; i++)
                {
                    var date = now.Date.AddDays(i - 13);
                    var dd = d.Get(date, false);
                    float x = plot.X + slot * i + (slot - bw) / 2f;
                    if (dd != null && dd.ListenSec > 0)
                    {
                        float hAll = (float)(plot.Height * dd.ListenSec / maxSec), hLoud = (float)(plot.Height * dd.LoudSec / maxSec);
                        Theme.FillRound(g, Color.FromArgb(0x3d, 0xa8, 0x6a), new RectangleF(x, plot.Bottom - hAll, bw, hAll), Theme.F(3));
                        if (hLoud > 1f) Theme.FillRound(g, Color.FromArgb(0xe8, 0x4a, 0x3d), new RectangleF(x, plot.Bottom - hLoud, bw, hLoud), Theme.F(3));
                    }
                    using (var b = new SolidBrush(i == 13 ? white : faint))
                        Theme.Str(g, i == 13 ? "heute" : date.ToString("ddd", De).TrimEnd('.'), Theme.Ui(8f, i == 13 ? FontStyle.Bold : FontStyle.Regular), b,
                            new RectangleF(plot.X + slot * i - Theme.F(4), plot.Bottom + Theme.F(4), slot + Theme.F(8), Theme.F(12)), Theme.Center);
                }
                // Legende
                float ly = plot.Bottom + Theme.F(22);
                Theme.FillRound(g, Color.FromArgb(0x3d, 0xa8, 0x6a), new RectangleF(pad, ly + Theme.F(2), Theme.F(10), Theme.F(10)), Theme.F(2));
                using (var b = new SolidBrush(dim)) Theme.Str(g, "gehört", Theme.Ui(8.5f), b, new RectangleF(pad + Theme.F(14), ly, Theme.F(60), Theme.F(14)), Theme.Left);
                Theme.FillRound(g, Color.FromArgb(0xe8, 0x4a, 0x3d), new RectangleF(pad + Theme.F(76), ly + Theme.F(2), Theme.F(10), Theme.F(10)), Theme.F(2));
                using (var b = new SolidBrush(dim))
                    Theme.Str(g, "laut (ab " + _w._loudFrom().ToString("0", De).Replace("-", "−") + " dB · Richtwert aus Lautheit und Windows-Lautstärke)", Theme.Ui(8.5f), b,
                        new RectangleF(pad + Theme.F(90), ly, Width - pad - Theme.F(100), Theme.F(14)), Theme.Left);

                // ---- Apps (heute und Woche)
                float ay = ly + Theme.F(28), colW = (Width - 2 * pad - Theme.F(20)) / 2f;
                DrawApps(g, new RectangleF(pad, ay, colW, Height - ay - Theme.F(8)), "Apps heute", d.TopApps(now, 1, 5), white, dim);
                DrawApps(g, new RectangleF(pad + colW + Theme.F(20), ay, colW, Height - ay - Theme.F(8)), "Apps diese Woche", d.TopApps(now, 7, 5), white, dim);
            }

            private static void DrawStat(Graphics g, RectangleF r, string label, string value, Color c)
            {
                using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                    Theme.Str(g, label, Theme.Ui(8f, FontStyle.Bold), b, new RectangleF(r.X, r.Y, r.Width, Theme.F(12)), Theme.Left);
                using (var b = new SolidBrush(c))
                    Theme.Str(g, value, Theme.Ui(16f, FontStyle.Bold), b, new RectangleF(r.X, r.Y + Theme.F(13), r.Width, Theme.F(28)), Theme.Left);
            }

            private static void DrawApps(Graphics g, RectangleF r, string title, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>> apps, Color white, Color dim)
            {
                using (var b = new SolidBrush(white)) Theme.Str(g, title, Theme.Ui(11f, FontStyle.Bold), b, new RectangleF(r.X, r.Y, r.Width, Theme.F(16)), Theme.Left);
                if (apps.Count == 0)
                {
                    using (var b = new SolidBrush(dim)) Theme.Str(g, "Noch nichts aufgezeichnet.", Theme.Ui(9f), b, new RectangleF(r.X, r.Y + Theme.F(20), r.Width, Theme.F(14)), Theme.Left);
                    return;
                }
                double max = Math.Max(1, apps[0].Value);
                float y = r.Y + Theme.F(20), rowH = Theme.F(17);
                foreach (var kv in apps)
                {
                    if (y + rowH > r.Bottom) break;
                    float bw = (float)((r.Width - Theme.F(170)) * kv.Value / max);
                    using (var b = new SolidBrush(dim)) Theme.Str(g, kv.Key, Theme.Ui(9f), b, new RectangleF(r.X, y, Theme.F(90), rowH), Theme.Left);
                    Theme.FillRound(g, Color.FromArgb(0x4c, 0x7d, 0xd6), new RectangleF(r.X + Theme.F(92), y + Theme.F(4), Math.Max(Theme.F(2), bw), rowH - Theme.F(8)), Theme.F(2));
                    using (var b = new SolidBrush(white))
                        Theme.Str(g, ListeningDiary.Duration(kv.Value), Theme.Ui(9f), b, new RectangleF(r.X + Theme.F(96) + bw, y, Theme.F(80), rowH), Theme.Left);
                    y += rowH;
                }
            }
        }
    }
}
