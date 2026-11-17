using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.15 Optik: Material, Seitenteile, Knoepfe, Faderkappen, digitale Namensschilder, LED-Form/-Groesse,
    /// Nachgluehen, Lichtschein, Fader-Spur, Ambilight, VU-Lichtfarbe, Ruhemodus, Goniometer, eigenes Hintergrundbild.
    /// Alles einzeln in den Einstellungen an/aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ Start

        /// <summary>Einstellungen von 2.15 auf die Anzeige uebertragen (beim Start, vor dem ersten Zeichnen).</summary>
        private void ApplyFeatures8Statics()
        {
            Theme.PanelMaterial = _settings.PanelMaterial;
            Theme.CheekStyle = _settings.CheekStyle;
            Theme.KnobStyle = _settings.KnobStyle;
            Theme.CapStyle = _settings.CapStyle;
            Theme.DigitalPlates = _settings.DigitalPlates;
            Theme.TapePlates = _settings.TapePlates;
            Theme.DymoPlates = _settings.DymoPlates;   // 3.3
            Theme.LedShape = _settings.LedShape;
            Theme.LedSize = _settings.LedSize ?? 1;
            Theme.VuLight = _settings.VuLight;
            StripBase.FastDraw = !_settings.FastDrawOff;   // 2.15.3: LEDs aus dem Bildspeicher, Meterbridge nur Geaendertes
            ApplyFxStatics(false);   // Nachgluehen, Lichtschein, Fader-Spur (beim Spielen evtl. gespart)
        }

        // ================================================================ Takt

        private void TickFeatures8(float dt)
        {
            bool shown = PultShown;
            // Goniometer (Daten kommen mit dem Spektrum-Loopback)
            if (shown && _bridge.GonioShown) _bridge.FeedGonio(_snap.Scope, _snap.ScopeCorr, dt);
            if (BridgeWinShown && _bridgeWin.Bridge.GonioShown) _bridgeWin.Bridge.FeedGonio(_snap.Scope, _snap.ScopeCorr, dt);
            if (shown) TickAmbilight(dt);
            if (_bgDirty && Environment.TickCount - _bgDirtyAt > 180) RebuildBgImage();
        }

        // ================================================================ Seitenteile

        private void ApplyCheeks()
        {
            if (_cheekL == null) return;
            bool on = Theme.EffCheek != 0;
            _cheekL.Visible = _cheekR.Visible = on;
            _cheekL.Invalidate(); _cheekR.Invalidate();
        }

        // ================================================================ Ambilight

        private AmbiBar _ambiL, _ambiR, _ambiB;
        private Color _ambiCol = Color.FromArgb(0x5a, 0xa9, 0xff);
        private float _ambiLevel;
        private int _ambiShownA = -1, _ambiShownC;

        /// <summary>Leuchtleisten am Pultrand anlegen (vor dem Andocken der anderen Teile).</summary>
        private void CreateAmbilight()
        {
            int w = Math.Max(3, Theme.Px(5));
            _ambiL = new AmbiBar(this, AmbiBar.Side.Left) { Dock = DockStyle.Left, Width = w };
            _ambiR = new AmbiBar(this, AmbiBar.Side.Right) { Dock = DockStyle.Right, Width = w };
            _ambiB = new AmbiBar(this, AmbiBar.Side.Bottom) { Dock = DockStyle.Bottom, Height = w };
            bool on = !_settings.AmbilightOff || !_settings.ClipFlashOff;   // 3.1: der Rand blinkt auch bei Uebersteuerung
            _ambiL.Visible = _ambiR.Visible = _ambiB.Visible = on;
        }

        public Color AmbiColor => _ambiCol;
        public bool AmbiOn => !_settings.AmbilightOff;
        public float AmbiLevel => _ambiLevel;

        private void TickAmbilight(float dt)
        {
            if (_settings.AmbilightOff || _ambiL == null) return;
            // Farbe des lautesten Kanals, Helligkeit mit der Musik
            float best = 0f; Color target = _ambiCol;
            foreach (var s in _channelStrips)
            {
                float v = Math.Max(s.ShowL, s.ShowR);
                if (v > best) { best = v; target = s.ChannelColor; }
            }
            if (best < 0.05f) target = _ambiCol;   // Stille: Farbe bleibt, nur dunkel
            else if (CoverAmbiColor.HasValue) target = CoverAmbiColor.Value;   // 3.2: Farbe des Album-Covers
            float k = Math.Min(1f, dt * 3f);
            _ambiCol = Color.FromArgb(
                (int)Math.Round(_ambiCol.R + (target.R - _ambiCol.R) * k),
                (int)Math.Round(_ambiCol.G + (target.G - _ambiCol.G) * k),
                (int)Math.Round(_ambiCol.B + (target.B - _ambiCol.B) * k));
            float lv = (float)Math.Pow(Math.Max(0f, best), 1.6);
            _ambiLevel += (lv - _ambiLevel) * Math.Min(1f, dt * (lv > _ambiLevel ? 18f : 3.5f));
            int a = (int)(_ambiLevel * 40), c = _ambiCol.ToArgb();
            if (a == _ambiShownA && c == _ambiShownC) return;
            _ambiShownA = a; _ambiShownC = c;
            _ambiL.Invalidate(); _ambiR.Invalidate(); _ambiB.Invalidate();
        }

        private void SetAmbilight(bool on)
        {
            _settings.AmbilightOff = !on;
            if (_ambiL != null) _ambiL.Visible = _ambiR.Visible = _ambiB.Visible = on || !_settings.ClipFlashOff;
            ScheduleSave();
        }

        /// <summary>Leuchtleiste am Rand: aussen hell in der Farbe der Musik, nach innen auslaufend.</summary>
        private sealed class AmbiBar : Control
        {
            public enum Side { Left, Right, Bottom }
            private readonly MixerForm _m;
            private readonly Side _side;

            public AmbiBar(MixerForm m, Side side)
            {
                _m = m; _side = side;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = BodyColor;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var r = ClientRectangle;
                using (var b = new SolidBrush(Color.FromArgb(0x08, 0x08, 0x0a))) g.FillRectangle(b, r);
                float lv = _m.AmbiOn ? _m.AmbiLevel : 0f;
                Color c = _m.AmbiColor;
                int aOut = _m.AmbiOn ? (int)(35 + 220 * lv) : 0, aIn = _m.AmbiOn ? (int)(10 + 60 * lv) : 0;
                float fl = _m.ClipFlashLevel;   // 3.1: rot blinken bei Uebersteuerung
                if (fl > 0f)
                {
                    c = Theme.Mix(c, Color.FromArgb(0xff, 0x2a, 0x1e), fl);
                    aOut = Math.Max(aOut, (int)(255 * fl)); aIn = Math.Max(aIn, (int)(120 * fl));
                }
                var mode = _side == Side.Bottom ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal;
                var gr = _side == Side.Bottom ? new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2) : new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height);
                Color inner = Color.FromArgb(aIn, c), outer = Color.FromArgb(aOut, Theme.Lighten(c, 0.15f));
                bool outerFirst = _side == Side.Left;   // links: aussen = links; rechts/unten: aussen = rechts/unten
                using (var b = new LinearGradientBrush(gr, outerFirst ? outer : inner, outerFirst ? inner : outer, mode))
                    g.FillRectangle(b, r);
            }
        }

        // ================================================================ Ruhemodus

        public static readonly int[] IdleMinutes = { 0, 2, 5, 10, 30 };
        private int _lastActivity = Environment.TickCount;
        private bool _idle;
        private ActivityFilter _activity;

        /// <summary>Merkt jede Maus-/Tastatur-Aktion im Mischpult (fuer den Ruhemodus).</summary>
        private sealed class ActivityFilter : IMessageFilter
        {
            private readonly MixerForm _m;
            private Point _lastPos = new Point(int.MinValue, int.MinValue);
            public ActivityFilter(MixerForm m) { _m = m; }

            public bool PreFilterMessage(ref Message msg)
            {
                int id = msg.Msg;
                if (id == 0x200 || id == 0xA0)
                {
                    // 3.4: nur echte Bewegung zaehlt (beim Erscheinen eines Fensters unter ruhender Maus kommt auch "bewegt")
                    var p = Cursor.Position;
                    if (p == _lastPos) return false;
                    _lastPos = p;
                }
                if (id == 0x200 || id == 0x201 || id == 0x204 || id == 0x207 || id == 0x20A || id == 0x100 || id == 0xA0)
                    _m.OnUserActivity();
                return false;
            }
        }

        private void StartActivityWatch()
        {
            if (_activity != null) return;
            _activity = new ActivityFilter(this);
            Application.AddMessageFilter(_activity);
        }

        private void OnUserActivity()
        {
            _lastActivity = Environment.TickCount;
            if (_idle)
            {
                _idle = false;
                BeginInvoke(new Action(() => UpdatePultDim(true)));   // sofort wieder hell
            }
        }

        /// <summary>Ruhemodus: nach so vielen Minuten ohne Bedienung wird das Pult dunkel (nur Leuchtendes bleibt).</summary>
        private float IdleDimTarget()
        {
            int min = _settings.IdleMinutes ?? 10;
            if (min <= 0 || _demo) { _idle = false; return 0f; }
            bool idle = unchecked(Environment.TickCount - _lastActivity) > min * 60000;
            _idle = idle;
            return idle ? 0.7f : 0f;
        }

        private void SetIdleMinutes(int min)
        {
            _settings.IdleMinutes = min;
            _lastActivity = Environment.TickCount;
            UpdatePultDim(true);
            ScheduleSave();
        }

        // ================================================================ Hintergrundbild

        private Bitmap _bgSource;
        private bool _bgDirty;
        private int _bgDirtyAt;

        /// <summary>Bild laden (verkleinert, Datei bleibt frei) – false, wenn es nicht geht.</summary>
        private bool LoadBgSource(string path)
        {
            _bgSource?.Dispose();
            _bgSource = null;
            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                using (var fs = System.IO.File.OpenRead(path))
                using (var img = Image.FromStream(fs))
                {
                    float k = Math.Min(1f, 2560f / Math.Max(img.Width, img.Height));
                    int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                    var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, new Rectangle(0, 0, w, h));
                    }
                    _bgSource = bmp;
                }
                return true;
            }
            catch { return false; }
        }

        private void MarkBgDirty()
        {
            if (_bgSource == null && Theme.BgImage == null) return;
            _bgDirty = true;
            _bgDirtyAt = Environment.TickCount;
        }

        /// <summary>Bild auf Fenstergroesse bringen (fuellend) und abdunkeln; dann alles neu zeichnen.</summary>
        private void RebuildBgImage()
        {
            _bgDirty = false;
            var old = Theme.BgImage;
            Bitmap next = null;
            if (_bgSource != null && ClientSize.Width > 10 && ClientSize.Height > 10)
            {
                int w = ClientSize.Width, h = ClientSize.Height;
                next = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(next))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float k = Math.Max(w / (float)_bgSource.Width, h / (float)_bgSource.Height);
                    float iw = _bgSource.Width * k, ih = _bgSource.Height * k;
                    g.DrawImage(_bgSource, new RectangleF((w - iw) / 2f, (h - ih) / 2f, iw, ih));
                    int dim = Math.Max(0, Math.Min(95, _settings.BgDim ?? 55));
                    using (var b = new SolidBrush(Color.FromArgb(dim * 255 / 100, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);
                }
            }
            else if (_settings.SkyOn && ClientSize.Width > 10 && ClientSize.Height > 10)
                next = Theme.MakeSky(ClientSize.Width, ClientSize.Height, SkyTime400, Theme.MoonPhase(DateTime.UtcNow), 40);   // 4.0 finale: Himmel nach Tageszeit
            else if (_settings.Wall > 0 && ClientSize.Width > 10 && ClientSize.Height > 10)
                next = Theme.MakeWall(ClientSize.Width, ClientSize.Height, _settings.Wall);   // 3.3: Wand hinter dem Pult
            else if (Theme.DesignBg && ClientSize.Width > 10 && ClientSize.Height > 10)
                next = Theme.MakeDesignBg(ClientSize.Width, ClientSize.Height);   // 4.0 finale: Weltraum, 4.2: Synthwave
            Theme.BgImage = next;
            Theme.BgVersion++;
            Theme.LampVersion++;   // Zuege zeichnen ihren Rand neu
            old?.Dispose();
            foreach (var c in new Control[] { _channelHost, _outputHost, _divider, _addButton }) c?.Invalidate();
            _bridge.Restyle();
        }

        private void ChooseBgImage()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Hintergrundbild wählen",
                Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Alle Dateien (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!LoadBgSource(dlg.FileName))
                {
                    ShowOsd("Hintergrundbild", "Bild konnte nicht geladen werden", -1, Theme.MuteRed);
                    return;
                }
                _settings.BgImagePath = dlg.FileName;
                RebuildBgImage();
                ScheduleSave();
            }
        }

        private void ClearBgImage()
        {
            _settings.BgImagePath = null;
            LoadBgSource(null);
            RebuildBgImage();
            ScheduleSave();
        }

        private void SetBgDim(int pct, bool final)
        {
            _settings.BgDim = Math.Max(0, Math.Min(95, pct));
            if (final) { RebuildBgImage(); ScheduleSave(); }
        }

        // ================================================================ Einstellen

        private void SetPanelMaterial(int i) { _settings.PanelMaterial = Clamp(i, 4); Theme.PanelMaterial = _settings.PanelMaterial; RefreshStrips(); }
        private void SetCheekStyle(int i) { _settings.CheekStyle = Clamp(i, 4); Theme.CheekStyle = _settings.CheekStyle; ApplyCheeks(); ScheduleSave(); }
        private void SetKnobStyle(int i) { _settings.KnobStyle = Clamp(i, 4); Theme.KnobStyle = _settings.KnobStyle; RefreshStrips(); }
        private void SetCapStyle(int i) { _settings.CapStyle = Clamp(i, 5); Theme.CapStyle = _settings.CapStyle; RefreshStrips(); }
        private void SetDigitalPlates(bool on) { _settings.DigitalPlates = on; Theme.DigitalPlates = on; _settings.TapePlates = false; Theme.TapePlates = false; _settings.DymoPlates = false; Theme.DymoPlates = false; RefreshStrips(); }

        /// <summary>2.18: Namensschilder 0 farbig, 1 digital, 2 Klebeband.</summary>
        private void SetPlateStyle(int i)
        {
            _settings.DigitalPlates = i == 1; Theme.DigitalPlates = i == 1;
            _settings.TapePlates = i == 2; Theme.TapePlates = i == 2;
            _settings.DymoPlates = i == 3; Theme.DymoPlates = i == 3;   // 3.3 Praegeband
            RefreshStrips();
            ScheduleSave();
        }

        private int PlateStyle => _settings.DymoPlates ? 3 : _settings.TapePlates ? 2 : _settings.DigitalPlates ? 1 : 0;
        private void SetLedShape(int i) { _settings.LedShape = Clamp(i, 2); Theme.LedShape = _settings.LedShape; RefreshStrips(); _bridge.Restyle(); }
        private void SetLedSize(int i) { _settings.LedSize = Clamp(i, 2); Theme.LedSize = _settings.LedSize.Value; RelayoutAllStrips(); RefreshStrips(); _bridge.Restyle(); }
        private void SetAfterglow(bool on) { _settings.LedAfterglowOff = !on; ApplyFxStatics(false); RefreshStrips(); }
        private void SetHalo(bool on) { _settings.LedHaloOff = !on; ApplyFxStatics(false); RefreshStrips(); _bridge.Restyle(); }
        private void SetVuLight(int i) { _settings.VuLight = Clamp(i, 3); Theme.VuLight = _settings.VuLight; RefreshStrips(); }
        private void SetFaderGlow(bool on) { _settings.FaderGlowOff = !on; ApplyFxStatics(false); RefreshStrips(); }
        private void SetGonio(bool on) { _settings.GonioOff = !on; _bridge.GonioOn = on; _bridge.Restyle(); ScheduleSave(); }

        private static int Clamp(int v, int max) => Math.Max(0, Math.Min(max, v));

        private ToolStripMenuItem ChoiceSub(string title, string[] names, int current, Action<int> set)
        {
            var sub = Sub(title);
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                sub.DropDownItems.Add(new ToolStripMenuItem(names[i], null, (s, e) => set(k)) { Checked = current == i });
            }
            return sub;
        }

        /// <summary>Schalter von 2.15 fuers Schnellmenue (Ansicht → Leuchten).</summary>
        private void AddFeatures8Items(ToolStripItemCollection items)
        {
            items.Add(new ToolStripSeparator());
            items.Add(ChoiceSub("LED-Form", Theme.LedShapeNames, _settings.LedShape, SetLedShape));
            items.Add(ChoiceSub("LED-Größe", Theme.LedSizeNames, _settings.LedSize ?? 1, SetLedSize));
            items.Add(new ToolStripMenuItem("LEDs glühen nach", null, (s, e) => SetAfterglow(_settings.LedAfterglowOff)) { Checked = !_settings.LedAfterglowOff });
            items.Add(new ToolStripMenuItem("Lichtschein um LEDs", null, (s, e) => SetHalo(_settings.LedHaloOff)) { Checked = !_settings.LedHaloOff });
            items.Add(new ToolStripMenuItem("Leuchtende Fader-Spur", null, (s, e) => SetFaderGlow(_settings.FaderGlowOff)) { Checked = !_settings.FaderGlowOff });
            items.Add(new ToolStripMenuItem("Ambilight am Pultrand", null, (s, e) => SetAmbilight(_settings.AmbilightOff)) { Checked = !_settings.AmbilightOff });
            items.Add(new ToolStripMenuItem("Goniometer in der Meterbridge", null, (s, e) => SetGonio(_settings.GonioOff)) { Checked = !_settings.GonioOff });
            items.Add(ChoiceSub("VU-Licht", Theme.VuLightNames, _settings.VuLight, SetVuLight));
        }

        /// <summary>Material-Auswahl fuers Schnellmenue (Ansicht).</summary>
        private ToolStripMenuItem MaterialSub()
        {
            var sub = Sub("Material & Teile");
            sub.DropDownItems.Add(ChoiceSub("Oberfläche", Theme.MaterialNames, _settings.PanelMaterial, SetPanelMaterial));
            sub.DropDownItems.Add(ChoiceSub("Seitenteile", Theme.CheekNames, _settings.CheekStyle, SetCheekStyle));
            sub.DropDownItems.Add(ChoiceSub("Knöpfe", Theme.KnobStyleNames, _settings.KnobStyle, SetKnobStyle));
            sub.DropDownItems.Add(ChoiceSub("Faderkappen", Theme.CapStyleNames, _settings.CapStyle, SetCapStyle));
            sub.DropDownItems.Add(new ToolStripMenuItem("Digitale Namensschilder", null, (s, e) => SetDigitalPlates(!_settings.DigitalPlates)) { Checked = _settings.DigitalPlates });
            sub.DropDownItems.Add(new ToolStripMenuItem("Namensschilder als Klebeband", null, (s, e) => SetPlateStyle(_settings.TapePlates ? 0 : 2)) { Checked = _settings.TapePlates });
            sub.DropDownItems.Add(new ToolStripMenuItem("Namensschilder als Prägeband", null, (s, e) => SetPlateStyle(_settings.DymoPlates ? 0 : 3)) { Checked = _settings.DymoPlates });
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(new ToolStripMenuItem("Hintergrundbild wählen …", null, (s, e) => ChooseBgImage()));
            if (!string.IsNullOrEmpty(_settings.BgImagePath))
                sub.DropDownItems.Add(new ToolStripMenuItem("Hintergrundbild entfernen", null, (s, e) => ClearBgImage()));
            return sub;
        }
    }
}
