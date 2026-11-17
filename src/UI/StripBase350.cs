using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.5: Uebersteuerungs-Liste, mehrere Fader zusammen (Strg+Klick), Mittelklick = stumm, Mausrad umkehren,
    /// Wert eintippen, Szenen-Vorschau und Lauflicht.
    /// </summary>
    internal abstract partial class StripBase
    {
        public static bool ClipLogOn, MultiFaderOn, MiddleMuteOn, TypeValueOn, WheelInvert, ScenePreviewOn;

        // ---------------------------------------------------------------- mehrere Fader zusammen
        private static readonly HashSet<StripBase> _selected = new HashSet<StripBase>();
        public static IReadOnlyCollection<StripBase> Selected => _selected;
        public bool MultiSelected => _selected.Contains(this);

        /// <summary>Kann dieser Zug mit anderen zusammen bewegt werden?</summary>
        protected virtual bool CanMultiSelect => false;

        public void ToggleSelected()
        {
            if (!_selected.Remove(this)) _selected.Add(this);
            Invalidate();
        }

        public static void ClearSelection()
        {
            if (_selected.Count == 0) return;
            var old = new List<StripBase>(_selected);
            _selected.Clear();
            foreach (var s in old) try { if (!s.IsDisposed) s.Invalidate(); } catch { }
        }

        /// <summary>Die anderen gewaehlten Zuege um denselben Weg mitbewegen (dB-Fader in dB, sonst im Faderweg).</summary>
        private void MoveSelectedBy(float fromPos, float toPos, bool final)
        {
            if (!MultiFaderOn || !MultiSelected || _selected.Count < 2) return;
            bool db = DbFader;
            float dDb = 0f, dPos = toPos - fromPos;
            if (db)
            {
                float a = FaderLaw.PosToDb(fromPos), b = FaderLaw.PosToDb(toPos);
                if (float.IsNegativeInfinity(a)) a = -60f;
                if (float.IsNegativeInfinity(b)) b = -60f;
                dDb = b - a;
            }
            foreach (var s in new List<StripBase>(_selected))
            {
                if (ReferenceEquals(s, this) || s.IsDisposed || s.FaderLocked) continue;
                float cur = s.FaderPos, np;
                if (db && s.DbFader)
                {
                    float d = FaderLaw.PosToDb(cur);
                    if (float.IsNegativeInfinity(d)) d = -60f;
                    d += dDb;
                    np = d < -60f ? 0f : FaderLaw.DbToPos(Math.Min(0f, d));
                }
                else np = Clamp(cur + dPos);
                if (Math.Abs(np - cur) > 1e-5f || final) { s.OnFaderMoved(np, final); s.InvalidateChrome(); }
            }
        }

        // ---------------------------------------------------------------- Mittelklick, Wert eintippen
        /// <summary>Mittlere Maustaste: stumm an/aus (Kanal, Ausgang …).</summary>
        protected virtual void OnMiddleClick() { }

        /// <summary>Getippter Wert (dB bzw. %) als Faderstellung, NaN = nicht verstanden.</summary>
        internal float ParseTyped(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return float.NaN;
            string t = text.Trim().ToLowerInvariant().Replace("db", "").Replace("%", "").Replace("−", "-").Replace(",", ".").Trim();
            if (t == "aus" || t == "-inf" || t == "-∞" || t == "∞" || t == "-unendlich") return 0f;
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v)) return float.NaN;
            if (DbFader)
            {
                if (v > 0f) v = -v;                  // "12" heisst −12 dB (ueber 0 dB geht nicht)
                if (v <= -60f) return v < -60.01f ? 0f : FaderLaw.DbToPos(-60f);
                return Clamp(FaderLaw.DbToPos(v));
            }
            return Clamp(v / 100f);
        }

        /// <summary>Getippten Wert einstellen (der Fader faehrt sichtbar hin). false = nicht verstanden.</summary>
        internal bool ApplyTyped(string text)
        {
            float p = ParseTyped(text);
            if (float.IsNaN(p) || FaderLocked) return false;
            float from = ShownFaderPos;
            OnFaderMoved(p, true);
            MoveSelectedBy(from, p, true);
            InvalidateChrome();
            StartMotor(from);
            return true;
        }

        internal string TypedHint => DbFader ? "Wert in dB (z. B. −12,5 oder 12; „aus“ = ganz zu):" : "Wert in Prozent (0 bis 100):";
        internal string TypedNow => DbFader ? FaderLaw.FormatDb(FaderPos).Replace(" dB", "") : ((int)Math.Round(FaderPos * 100)).ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- Szenen-Vorschau
        /// <summary>Wo der Fader mit der Szene unter der Maus landen wuerde (NaN = keine Vorschau).</summary>
        public float PreviewPos = float.NaN;

        public void SetPreview(float pos)
        {
            if (float.IsNaN(pos) && float.IsNaN(PreviewPos)) return;
            if (!float.IsNaN(pos) && !float.IsNaN(PreviewPos) && Math.Abs(pos - PreviewPos) < 1e-4f) return;
            PreviewPos = pos;
            Invalidate(FaderPreviewBounds);
        }

        private Rectangle FaderPreviewBounds => Rectangle.Inflate(Rectangle.Ceiling(FaderArea), Theme.Px(4), Theme.Px(6));

        private void DrawPreview(Graphics g)
        {
            float m = PreviewPos;
            if (float.IsNaN(m) || FaderArea.IsEmpty) return;
            if (KnobView && _knobR > 0f) { var sm0 = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; KnobTick(g, m, Theme.IsLight ? Color.FromArgb(0x20, 0x24, 0x2a) : Color.White, 1.8f); g.SmoothingMode = sm0; return; }   // 4.0
            float y = (float)Math.Round(PosToY(Math.Max(0f, Math.Min(1f, m)))) + 0.5f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = Theme.IsLight ? Color.FromArgb(0x20, 0x24, 0x2a) : Color.White;
            float s = Theme.F(5f), x1 = SlotX - Theme.F(17), x2 = SlotX + Theme.F(17);
            using (var pen = new Pen(c, Math.Max(1.2f, Theme.F(1.4f))) { LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(pen, new[] { new PointF(x1 - s, y - s), new PointF(x1 + Theme.F(0.5f), y), new PointF(x1 - s, y + s) });
                g.DrawPolygon(pen, new[] { new PointF(x2 + s, y - s), new PointF(x2 - Theme.F(0.5f), y), new PointF(x2 + s, y + s) });
                pen.DashStyle = DashStyle.Dash;
                pen.Color = Theme.Alpha(c, 170);
                g.DrawLine(pen, x1 + Theme.F(1), y, x2 - Theme.F(1), y);
            }
            g.SmoothingMode = sm;
        }

        // ---------------------------------------------------------------- Lauflicht beim Szenenwechsel
        public static bool SweepOn;
        private static readonly System.Diagnostics.Stopwatch SweepClock = System.Diagnostics.Stopwatch.StartNew();
        private static double _sweepStart = -10;
        public const double SweepSec = 1.1;

        public static void StartSweep() { if (SweepOn && !Calm) _sweepStart = SweepClock.Elapsed.TotalSeconds; }
        public static bool SweepRunning => SweepClock.Elapsed.TotalSeconds - _sweepStart < SweepSec + 0.1;

        private void DrawSweepOverlay(Graphics g)
        {
            if (!SweepRunning || Parent == null) return;
            double el = SweepClock.Elapsed.TotalSeconds - _sweepStart;
            float hostW = Math.Max(1, Parent.ClientSize.Width);
            float delay = (float)(Math.Max(0, Left) / hostW * 0.65);
            float t = (float)((el - delay) / 0.4);
            if (t < -0.3f || t > 1.3f) return;
            var st = g.Save();
            using (var path = Theme.Round(RectangleF.Inflate(PanelRect, -Theme.F(0.5f), -Theme.F(0.5f)), Theme.PanelRad))
            {
                g.SetClip(path, CombineMode.Intersect);
                Theme.DrawSweep(g, Rectangle.Ceiling(PanelRect), t, AccentColor);
            }
            g.Restore(st);
        }

        // ---------------------------------------------------------------- Zeichnen (live, ueber allem)
        private void PaintOverlays350(Graphics g)
        {
            if (MultiSelected)
            {
                var sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = RectangleF.Inflate(PanelRect, -Theme.F(2.5f), -Theme.F(2.5f));
                using (var pen = new Pen(Theme.SoloAmber, Theme.F(1.8f)) { DashStyle = DashStyle.Dash })
                using (var path = Theme.Round(r, Theme.PanelRad))
                    g.DrawPath(pen, path);
                g.SmoothingMode = sm;
            }
            if (!float.IsNaN(PreviewPos)) DrawPreview(g);
            if (SweepOn) DrawSweepOverlay(g);
        }
    }
}
