using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// Kleines Mischfenster fuer die Apps eines Kanals: pro App ein eigener Regler (relativ zum Kanal)
    /// mit Pegel (grau = was die App abspielt, farbig = was du hoerst). Schliesst sich beim Klick daneben.
    /// </summary>
    internal sealed class AppMixPopup : Form
    {
        private readonly MixerForm _mixer;
        private readonly ChannelStrip _strip;
        private List<AppView> _apps;
        private readonly Dictionary<string, MeterBallistics> _post = new Dictionary<string, MeterBallistics>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MeterBallistics> _pre = new Dictionary<string, MeterBallistics>(StringComparer.OrdinalIgnoreCase);
        private string _hoverKey, _hoverPart, _dragKey;
        private int _more;          // Apps, die nicht mehr auf den Bildschirm passen
        private readonly ToolTip _tip = PultTip.Make(new ToolTip());
        private string _tipFor;

        private int HeadH => Theme.Px(34);
        private int RowH => Theme.Px(34);
        private int FootH => Theme.Px(38);

        public ChannelConfig Channel => _strip.Config;

        public AppMixPopup(MixerForm mixer, ChannelStrip strip)
        {
            _mixer = mixer;
            _strip = strip;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            Text = "Apps – " + strip.Config.Name;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _apps = Limit(Sorted(strip.Apps));
            Size = new Size(Theme.Px(320), HeadH + Math.Max(1, _apps.Count) * RowH + FootH);
        }

        /// <summary>Hoechstens so viele Zeilen, wie auf den Bildschirm passen (spielende Apps stehen oben).</summary>
        private List<AppView> Limit(List<AppView> apps)
        {
            int max = 8;
            try { max = Math.Max(3, (int)((Screen.FromControl(_strip).WorkingArea.Height * 0.8 - HeadH - FootH) / RowH)); } catch { }
            _more = Math.Max(0, apps.Count - max);
            return _more > 0 ? apps.GetRange(0, max) : apps;
        }

        private static List<AppView> Sorted(List<AppView> apps)
        {
            var list = new List<AppView>(apps);
            list.Sort((a, b) =>
            {
                int ra = a.Active ? 0 : a.Running ? 1 : 2, rb = b.Active ? 0 : b.Running ? 1 : 2;
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;          // nicht in Alt+Tab
                cp.ClassStyle |= 0x20000;    // Schatten
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.F(9)))
                Region = new Region(p);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_dragKey == null) BeginInvoke(new Action(Close));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        /// <summary>Pegel und App-Liste auffrischen (aus dem Timer des Mischpults).</summary>
        public void UpdateLevels(float dt)
        {
            var apps = Limit(Sorted(_strip.Apps));
            bool changed = apps.Count != _apps.Count;
            for (int i = 0; !changed && i < apps.Count; i++) changed = apps[i].Key != _apps[i].Key;
            _apps = apps;
            if (changed)
            {
                int h = HeadH + Math.Max(1, _apps.Count) * RowH + FootH;
                if (Height != h) Height = h;
            }
            foreach (var a in _apps)
            {
                var lv = _mixer.AppLevels(a.Key);
                if (!_post.TryGetValue(a.Key, out var m)) _post[a.Key] = m = new MeterBallistics();
                if (!_pre.TryGetValue(a.Key, out var pm)) _pre[a.Key] = pm = new MeterBallistics();
                m.Update(lv[0], lv[1], dt);
                pm.Update(Math.Max(lv[0], lv[2]), Math.Max(lv[1], lv[3]), dt);
            }
            Invalidate();
        }

        // --------------------------------------------------------------- Layout
        private Rectangle RowRect(int i) => new Rectangle(0, HeadH + i * RowH, Width, RowH);
        private RectangleF TrackRect(Rectangle row) => new RectangleF(Theme.F(128), row.Y + Theme.F(10), Width - Theme.F(128) - Theme.F(70), Theme.F(9));
        private RectangleF ValueRect(Rectangle row) => new RectangleF(Width - Theme.F(66), row.Y, Theme.F(58), row.Height);
        private RectangleF ResetRect => new RectangleF(Theme.F(10), Height - FootH + Theme.F(8), Theme.F(118), Theme.F(22));
        private RectangleF AssignRect => new RectangleF(Width - Theme.F(150), Height - FootH + Theme.F(8), Theme.F(140), Theme.F(22));

        private RectangleF TrackHit(Rectangle rr)
        {
            var t = TrackRect(rr);
            return new RectangleF(t.X - Theme.F(8), rr.Y, t.Width + Theme.F(16), rr.Height);
        }

        // --------------------------------------------------------------- Zeichnen
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.PopupBg);
            Theme.StrokeRound(g, Theme.MenuBorder, Theme.F(1), new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.F(9));

            var col = _strip.ChannelColor;
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, HeadH), Theme.PopupHeadTop, Theme.PopupHeadBottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, 1, 1, Width - 2, HeadH - 1);
            Theme.FillRound(g, col, new RectangleF(Theme.F(10), Theme.F(9), Theme.F(4), HeadH - Theme.F(18)), Theme.F(2));
            Theme.DrawSpaced(g, _strip.Config.Name.ToUpperInvariant(), Theme.Ui(10f, FontStyle.Bold), Theme.Text, Theme.F(20), Theme.F(9), Theme.F(1.2f));
            using (var b = new SolidBrush(Theme.TextFaint))
                Theme.Str(g, _more > 0 ? "+ " + _more + " weitere (still)" : "Lautstärke pro App", Theme.Ui(9f), b, new RectangleF(Theme.F(130), 0, Width - Theme.F(140), HeadH), Theme.Right);

            if (_apps.Count == 0)
            {
                using (var b = new SolidBrush(Theme.TextFaint))
                    Theme.Str(g, "Keine Apps in diesem Kanal", Theme.Ui(9.5f), b, RowRect(0), Theme.Center);
            }

            for (int i = 0; i < _apps.Count; i++)
            {
                var a = _apps[i];
                var rr = RowRect(i);
                bool hover = string.Equals(_hoverKey, a.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(_dragKey, a.Key, StringComparison.OrdinalIgnoreCase);
                if (hover) using (var b = new SolidBrush(Theme.PopupHover)) g.FillRectangle(b, 1, rr.Y, Width - 2, rr.Height);
                if (i > 0) using (var p = new Pen(Theme.PopupLine)) g.DrawLine(p, Theme.F(10), rr.Y, Width - Theme.F(10), rr.Y);

                // Symbol + Name
                int size = Theme.Px(20);
                var ic = _mixer.Icons.Get(a.Key, a.Name, a.Path);
                g.DrawImage(a.Active ? ic.Normal : a.Running ? ic.Dim : ic.Off, new RectangleF(Theme.F(10), rr.Y + (rr.Height - size) / 2f, size, size));
                var nameRect = new RectangleF(Theme.F(36), rr.Y, Theme.F(88), rr.Height);
                using (var b = new SolidBrush(a.Running ? Theme.Text : Theme.TextFaint))
                using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, a.Name, Theme.Fit(g, a.Name, nameRect.Width, 10f, 7.5f), b, nameRect, sf);

                // Regler
                float pos = _mixer.AppPos(a.Key);
                var tr = TrackRect(rr);
                Theme.FillRound(g, Theme.Deep, tr, Theme.F(4.5f));
                if (pos > 0) Theme.FillRound(g, Theme.Alpha(col, 170), new RectangleF(tr.X, tr.Y, Math.Max(Theme.F(5), tr.Width * pos), tr.Height), Theme.F(4.5f));

                // Pegel darunter: grau = was die App spielt, farbig = was du hoerst
                var mr = new RectangleF(tr.X, tr.Bottom + Theme.F(3), tr.Width, Theme.F(3));
                using (var b = new SolidBrush(Theme.Deep)) g.FillRectangle(b, mr);
                if (Theme.GhostOn && _pre.TryGetValue(a.Key, out var pm))
                {
                    float pf = Math.Max(pm.FracL, pm.FracR);
                    if (pf > 0) using (var b = new SolidBrush(Theme.MeterGhost)) g.FillRectangle(b, mr.X, mr.Y, mr.Width * pf, mr.Height);
                }
                if (_post.TryGetValue(a.Key, out var m))
                {
                    float f = Math.Max(m.FracL, m.FracR);
                    if (f > 0)
                        using (var b = new SolidBrush(f > 0.92f ? Theme.MeterRed : f > 0.75f ? Theme.MeterYellow : Theme.MeterGreen))
                            g.FillRectangle(b, mr.X, mr.Y, mr.Width * f, mr.Height);
                }
                float hx = tr.X + tr.Width * pos;
                Theme.FillEllipse(g, Color.FromArgb(0x05, 0x06, 0x07), hx, tr.Y + tr.Height / 2, Theme.F(7.5f));
                Theme.FillEllipse(g, hover ? Color.White : Theme.IsLight ? Color.FromArgb(0xf4, 0xf6, 0xf8) : Color.FromArgb(0xd7, 0xdb, 0xe0), hx, tr.Y + tr.Height / 2, Theme.F(6f));

                string val = FaderLaw.FormatDb(pos);
                using (var b = new SolidBrush(pos >= 0.9995f ? Theme.TextFaint : Theme.ValueText))
                    Theme.Str(g, val, Theme.Mono(9.5f), b, ValueRect(rr), Theme.Right);
            }

            DrawFootButton(g, ResetRect, "Alle auf 0 dB", "reset");
            DrawFootButton(g, AssignRect, "Apps zuordnen …", "assign");
        }

        private void DrawFootButton(Graphics g, RectangleF r, string text, string id)
        {
            bool hover = _hoverPart == id;
            Theme.FillRound(g, hover ? Theme.FootBtnHover : Theme.FootBtn, r, Theme.F(4));
            Theme.StrokeRound(g, Theme.FootBtnEdge, Theme.F(1), r, Theme.F(4));
            using (var b = new SolidBrush(hover ? Theme.TextHover : Theme.TextDim))
                Theme.Str(g, text, Theme.Ui(9f, FontStyle.Bold), b, r, Theme.Center);
        }

        // --------------------------------------------------------------- Maus
        private int RowAt(Point p)
        {
            if (p.Y < HeadH || p.Y >= Height - FootH) return -1;
            int i = (p.Y - HeadH) / RowH;
            return i >= 0 && i < _apps.Count ? i : -1;
        }

        private void SetFromX(AppView a, int x, Rectangle rr)
        {
            var t = TrackRect(rr);
            float pos = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
            if (pos > 0.985f) pos = 1f;              // oben einrasten: 0 dB
            _mixer.SetAppPos(a.Key, pos, false);
            Invalidate();
        }

        /// <summary>Ziehen beenden und speichern (auch wenn die Maus verloren ging, z. B. Alt+Tab).</summary>
        private void EndDrag()
        {
            if (_dragKey == null) return;
            _dragKey = null;
            if (Capture) Capture = false;
            _mixer.SettingsChanged();
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) EndDrag();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EndDrag();
            base.OnFormClosing(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragKey != null && (e.Button & MouseButtons.Left) == 0) EndDrag();
            if (_dragKey != null)
            {
                int i = _apps.FindIndex(a => string.Equals(a.Key, _dragKey, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) SetFromX(_apps[i], e.X, RowRect(i));
                return;
            }
            string key = null, part = null;
            if (ResetRect.Contains(e.Location)) part = "reset";
            else if (AssignRect.Contains(e.Location)) part = "assign";
            else
            {
                int i = RowAt(e.Location);
                if (i >= 0)
                {
                    key = _apps[i].Key;
                    part = TrackHit(RowRect(i)).Contains(e.Location) ? "track" : "row";
                }
            }
            if (key != _hoverKey || part != _hoverPart)
            {
                _hoverKey = key; _hoverPart = part;
                Cursor = part == "track" || part == "reset" || part == "assign" ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            string tip = part == "track" || part == "row" ? "Ziehen oder Mausrad = Lautstärke dieser App (relativ zum Kanal) · Doppelklick = 0 dB"
                       : part == "reset" ? "Alle Apps dieses Kanals wieder so laut wie der Kanal"
                       : part == "assign" ? "Apps zu diesem Kanal hinzufügen oder entfernen" : null;
            if (tip != _tipFor) { _tip.SetToolTip(this, tip); _tipFor = tip; }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (ResetRect.Contains(e.Location))
            {
                foreach (var a in _apps) _mixer.SetAppPos(a.Key, 1f, false);
                _mixer.SettingsChanged();
                Invalidate();
                return;
            }
            if (AssignRect.Contains(e.Location))
            {
                var p = _strip.PointToScreen(new Point(Theme.Px(8), Theme.Px(112)));
                Close();
                _mixer.ShowAppMenu(_strip, p);
                return;
            }
            int i = RowAt(e.Location);
            if (i < 0) return;
            var rr = RowRect(i);
            if (TrackHit(rr).Contains(e.Location))
            {
                _dragKey = _apps[i].Key;
                Capture = true;
                SetFromX(_apps[i], e.X, rr);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = RowAt(e.Location);
            if (i < 0) return;
            _mixer.SetAppPos(_apps[i].Key, 1f);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int i = RowAt(e.Location);
            if (i < 0) return;
            _mixer.NudgeApp(_apps[i].Key, e.Delta, (ModifierKeys & Keys.Control) != 0);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverKey != null || _hoverPart != null) { _hoverKey = null; _hoverPart = null; Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
