using System;
using System.Drawing;

namespace Mischpult.UI
{
    internal sealed partial class ChannelStrip
    {
        // ---- 4.0: bewegtes GIF im App-Feld
        /// <summary>Weniger Bewegung (Barrierefreiheit): GIFs bleiben beim ersten Bild stehen.</summary>
        public static bool GifCalm;
        private int _gifShown = -1;
        private bool _gifWasLive;

        private bool GifLive => ChannelImages.GifOn && ImagesOn && FieldMode == ChannelField.Apps && !Compact && ChannelImages.IsAnimated(Config.ImagePath);
        private static double GifT => GifCalm ? 0.0 : ChannelImages.GifTime;

        // ---- 4.0: Kanalzug anpassen – ausgeblendete Teile; was frei wird, bekommt das App-Feld (die Fader bleiben auf einer Hoehe)
        /// <summary>Ausgeblendete Teile ("pan", "eq", "keys", "field", "fx").</summary>
        public static readonly System.Collections.Generic.HashSet<string> Hide = new System.Collections.Generic.HashSet<string>();
        private bool HidEq => Hide.Contains("eq");
        private bool HidPan => Hide.Contains("pan");
        private bool HidKeys => Hide.Contains("keys");
        private bool HidField => Hide.Contains("field");
        private bool HidFx => Hide.Contains("fx");
        /// <summary>So weit (Pixel) ruecken die Reihen nach unten.</summary>
        private float _shApps, _shEq, _shPan;

        /// <summary>Hoehen (unskaliert) der Bereiche, die wegfallen koennen.</summary>
        internal const float EqBand = 72f, PanBand = 44f, KeysBand = 30f;

        private void LayoutCustom400()
        {
            _shApps = _shEq = _shPan = 0f;
            if (Hide.Count == 0) return;
            float eq = HidEq ? Theme.F(EqBand) : 0f, pan = HidPan ? Theme.F(PanBand) : 0f, keys = HidKeys ? Theme.F(KeysBand) : 0f;
            _shPan = keys;
            _shEq = pan + keys;
            _shApps = eq + pan + keys;
            RectangleF Down(RectangleF r, float d) => r.IsEmpty ? r : new RectangleF(r.X, r.Y + d, r.Width, r.Height);
            _appsBtn = Down(_appsBtn, _shApps);
            _routeBtn = Down(_routeBtn, _shApps);
            _appsRect = HidField ? RectangleF.Empty : new RectangleF(_appsRect.X, _appsRect.Y, _appsRect.Width, _appsRect.Height + _shApps);
            if (HidEq) { _eqBtn = _presetBtn = RectangleF.Empty; for (int i = 0; i < 3; i++) _eqZones[i] = RectangleF.Empty; }
            else
            {
                _eqBtn = Down(_eqBtn, _shEq); _presetBtn = Down(_presetBtn, _shEq);
                for (int i = 0; i < 3; i++) { _eqZones[i] = Down(_eqZones[i], _shEq); _eqCenters[i] = new PointF(_eqCenters[i].X, _eqCenters[i].Y + _shEq); }
            }
            _compBtn = Down(_compBtn, _shEq);
            _autoBtn = Down(_autoBtn, _shEq);
            if (HidPan) { _panZone = _strZone = RectangleF.Empty; }
            else
            {
                _panZone = Down(_panZone, _shPan); _strZone = Down(_strZone, _shPan);
                _knobCenter = new PointF(_knobCenter.X, _knobCenter.Y + _shPan); _strCenter = new PointF(_strCenter.X, _strCenter.Y + _shPan);
            }
            if (HidKeys) _soloBtn = _fadeBtn = _muteBtn = RectangleF.Empty;
        }

        // ---- 4.0: Apps zwischen Kanaelen ziehen
        public static bool AppDragOn;
        /// <summary>Kanal, ueber dem gerade eine App losgelassen wuerde.</summary>
        public static ChannelStrip DropTarget;
        private string _dragApp;
        private Point _dragAppStart;
        private bool _dragAppMoving;

        private bool TryBeginAppDrag(System.Windows.Forms.MouseEventArgs e)
        {
            if (!AppDragOn) return false;
            string k = IconAt(e.Location);
            if (k == null) return false;
            _dragApp = k;
            _dragAppStart = System.Windows.Forms.Cursor.Position;
            _dragAppMoving = false;
            return true;
        }

        private void AppDragMove()
        {
            var p = System.Windows.Forms.Cursor.Position;
            if (!_dragAppMoving)
            {
                int ds = Math.Max(System.Windows.Forms.SystemInformation.DragSize.Width, Theme.Px(6));
                if (Math.Abs(p.X - _dragAppStart.X) <= ds && Math.Abs(p.Y - _dragAppStart.Y) <= ds) return;
                _dragAppMoving = true;
                Cursor = System.Windows.Forms.Cursors.SizeAll;
                ShowLcd("APP ZIEHEN");
            }
            Mixer.AppDragHover(p);
        }

        private void AppDragEnd()
        {
            string k = _dragApp;
            bool moved = _dragAppMoving;
            _dragApp = null; _dragAppMoving = false;
            if (k == null) return;
            Cursor = System.Windows.Forms.Cursors.Default;
            if (moved) Mixer.DropApp(k, this, System.Windows.Forms.Cursor.Position);
            else if (Apps.Count > 0) Mixer.ShowAppMix(this);   // nur geklickt: wie bisher
        }

        /// <summary>Namen der Apps (fuer die Suche).</summary>
        public System.Collections.Generic.IEnumerable<string> AppNames { get { foreach (var a in Apps) yield return a.Name; } }

        // ---- 4.0: was laeuft (Fenstertitel der lautesten App)
        public static bool WinTitleOn;
        private string _winTitle;

        public void SetWinTitle(string t)
        {
            if (t == _winTitle) return;
            _winTitle = t;
            if (FieldLive) Invalidate(FieldBounds); else InvalidateChrome();
        }

        /// <summary>Unten im App-Feld: Titel auf dunklem Streifen.</summary>
        private void DrawWinTitle(Graphics g, RectangleF r)
        {
            if (!WinTitleOn || string.IsNullOrEmpty(_winTitle) || r.Height < Theme.F(30)) return;
            var band = new RectangleF(r.X + Theme.F(1), r.Bottom - Theme.F(14), r.Width - Theme.F(2), Theme.F(13));
            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(b, band);
            using (var b = new SolidBrush(Color.FromArgb(0xe6, 0xe8, 0xec)))
                Theme.Str(g, _winTitle, Theme.Ui(7.8f), b, new RectangleF(band.X + Theme.F(3), band.Y, band.Width - Theme.F(6), band.Height), Theme.Left);
        }

        // ---- 4.0: Kanal-Suche
        private bool _searchActive, _searchHit = true;

        public void SetSearch(bool active, bool hit)
        {
            if (active == _searchActive && hit == _searchHit) return;
            _searchActive = active; _searchHit = hit;
            Invalidate();
        }

        protected override void PaintOverlays400(Graphics g)
        {
            bool drop = ReferenceEquals(DropTarget, this);
            if (!drop && !_searchActive) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = RectangleF.Inflate(PanelRect, -Theme.F(1), -Theme.F(1));
            if (_searchActive && !_searchHit && !drop)
                using (var path = Theme.Round(r, Theme.PanelRad))
                using (var b = new SolidBrush(Color.FromArgb(160, Theme.Body))) g.FillPath(b, path);
            if (drop || (_searchActive && _searchHit))
            {
                Color c = drop ? Theme.LedGreen : Theme.Accent;
                for (int i = 3; i >= 1; i--) Theme.StrokeRound(g, Theme.Alpha(c, 40), Theme.F(1.6f * i), r, Theme.PanelRad);
                Theme.StrokeRound(g, c, Theme.F(2f), r, Theme.PanelRad);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Aus dem Takt (~30 mal pro Sekunde): naechstes Bild faellig?</summary>
        public void TickGif()
        {
            bool live = GifLive;
            if (live != _gifWasLive) { _gifWasLive = live; _gifShown = -1; InvalidateChrome(); return; }
            if (!live || !Visible) return;
            int i = ChannelImages.FrameIndex(Config.ImagePath, GifT);
            if (i != _gifShown) Invalidate(FieldBounds);
        }
    }
}
