using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>3.4.6 – Optik: Ausgaenge breiter ziehen, Deko auf dem Pult, Kabel unter dem Pult, Roehrenbild.</summary>
    internal sealed partial class MixerForm
    {
        private void ApplyLookStatics346(MixerSettings s, bool demo)
        {
            if (demo)
            {
                if (DemoFlag(demo, "OUTDRAG")) s.OutDragOn = true;
                if (DemoFlag(demo, "DEKO") && s.Deko == null)
                {
                    s.DekoOn = true;
                    s.Deko = new List<DekoConfig> { new DekoConfig { Kind = 3, X = 790, Y = 610 }, new DekoConfig { Kind = 1, X = 858, Y = 470 }, new DekoConfig { Kind = 0, X = 915, Y = 600 } };
                }
                if (DemoFlag(demo, "CABLES")) s.CablesOn = true;
                if (DemoFlag(demo, "CRT")) s.CrtOn = true;
            }
            Theme.CrtOn = s.CrtOn;
        }

        /// <summary>Nach dem Anlegen der Bereiche (aus ShownFeatures346): Zeichen- und Maus-Haken setzen.</summary>
        private void HookHosts346()
        {
            foreach (var h in new[] { _channelHost, _outputHost })
            {
                if (h == null) continue;
                h.PaintExtra = PaintHost346;
                h.ExtraMouseDown = HostMouseDown346;
                h.ExtraMouseMove = HostMouseMove346;
                h.ExtraMouseUp = HostMouseUp346;
            }
            ApplyCables346();
        }

        // ================================================================ 110 Ausgaenge breiter ziehen

        public bool OutDragEnabled => _settings.OutDragOn;
        public int OutWidthOf(string key) => _settings.OutDragOn && key != null && _settings.OutWidths != null && _settings.OutWidths.TryGetValue(key, out int w) ? w : 0;

        public void SetOutWidth(string key, int w, bool all, bool final)
        {
            if (key == null) return;
            if (w != 0) w = Math.Max(MixerSettings.DragWidthMin, Math.Min(MixerSettings.DragWidthMax, w));
            if (_settings.OutWidths == null) _settings.OutWidths = new Dictionary<string, int>();
            var keys = new List<string> { key };
            if (all) { keys.Add("master"); keys.AddRange(_outputStrips.Keys); }
            foreach (var k in keys.Distinct()) { if (w == 0) _settings.OutWidths.Remove(k); else _settings.OutWidths[k] = w; }
            _master?.Relayout();
            foreach (var os in _outputStrips.Values) os.Relayout();
            _outputHost.Relayout();
            RestyleBridges();
            if (final) ScheduleSave();
        }

        // ================================================================ 111 Deko auf dem Pult

        private int _dekoDrag = -1;
        private PointF _dekoGrab;
        private double _nextDekoAnim;
        private static readonly System.Diagnostics.Stopwatch DekoClock = System.Diagnostics.Stopwatch.StartNew();

        private bool DekoShown => _settings.DekoOn && _settings.Deko != null && _settings.Deko.Count > 0;

        /// <summary>Mitte eines Deko-Stuecks im Kanal-Bereich (Client-Koordinaten, mit Scrollen).</summary>
        private PointF DekoCenter(DekoConfig d) => new PointF(d.X * Theme.F(1) - _channelHost.ScrollX, d.Y * Theme.F(1));

        private RectangleF DekoRect(DekoConfig d)
        {
            var c = DekoCenter(d);
            float s = Theme.F(Theme.DekoSize);
            return new RectangleF(c.X - s * 0.45f, c.Y - s * 0.75f, s * 0.9f, s * 1.15f);   // mit Dampf oben
        }

        private int DekoAt(Point p)
        {
            if (!DekoShown) return -1;
            for (int i = _settings.Deko.Count - 1; i >= 0; i--)
                if (RectangleF.Inflate(DekoRect(_settings.Deko[i]), -Theme.F(6), -Theme.F(6)).Contains(p)) return i;
            return -1;
        }

        private bool OverStrip(StripHost h, Point p)
        {
            foreach (Control c in h.Controls) if (c.Visible && c.Bounds.Contains(p)) return true;
            return false;
        }

        private bool HostMouseDown346(StripHost h, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && StripBase.Selected.Count > 0 && !OverStrip(h, e.Location)) StripBase.ClearSelection();   // 3.5
            if (h != _channelHost || !_settings.DekoOn) return false;
            int i = DekoAt(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0)
            {
                var c = DekoCenter(_settings.Deko[i]);
                _dekoDrag = i;
                _dekoGrab = new PointF(e.X - c.X, e.Y - c.Y);
                h.Capture = true;
                h.Cursor = Cursors.SizeAll;
                return true;
            }
            if (e.Button == MouseButtons.Right && !OverStrip(h, e.Location))
            {
                var m = NewMenu();
                if (i >= 0)
                {
                    int k = i;
                    m.Items.Add(new ToolStripMenuItem(Theme.DekoNamesAll[_settings.Deko[k].Kind] + " wegnehmen", null, (s, ev) => { _settings.Deko.RemoveAt(k); if (_settings.Deko.Count == 0) _settings.Deko = null; _channelHost.Invalidate(); ScheduleSave(); }));
                }
                else
                {
                    var hp = e.Location;
                    var sub = Sub("Deko hierhin");
                    for (int kind = 0; kind < Theme.DekoCount; kind++)   // 4.0 finale: mit animierter Deko
                    {
                        int kk = kind;
                        sub.DropDownItems.Add(new ToolStripMenuItem(Theme.DekoNamesAll[kind], null, (s, ev) => AddDeko(kk, hp)));
                    }
                    m.Items.Add(sub);
                }
                m.Show(h, e.Location);
                return true;
            }
            return false;
        }

        private void AddDeko(int kind, Point hostPt)
        {
            if (_settings.Deko == null) _settings.Deko = new List<DekoConfig>();
            if (_settings.Deko.Count >= 20) _settings.Deko.RemoveAt(0);
            _settings.Deko.Add(new DekoConfig { Kind = kind, X = (hostPt.X + _channelHost.ScrollX) / Theme.F(1), Y = hostPt.Y / Theme.F(1) });
            _channelHost.Invalidate();
            ScheduleSave();
        }

        private bool HostMouseMove346(StripHost h, MouseEventArgs e)
        {
            if (h != _channelHost) return false;
            if (_dekoDrag >= 0 && _settings.Deko != null && _dekoDrag < _settings.Deko.Count)
            {
                var d = _settings.Deko[_dekoDrag];
                float s = Theme.F(Theme.DekoSize);
                float cx = Math.Max(s * 0.4f, Math.Min(h.ClientSize.Width - s * 0.4f, e.X - _dekoGrab.X));
                float cy = Math.Max(s * 0.8f, Math.Min(h.ClientSize.Height - s * 0.45f, e.Y - _dekoGrab.Y));
                d.X = (cx + h.ScrollX) / Theme.F(1);
                d.Y = cy / Theme.F(1);
                h.Invalidate();
                return true;
            }
            if (_settings.DekoOn && !OverStrip(h, e.Location))
            {
                bool over = DekoAt(e.Location) >= 0;
                if (over) { h.Cursor = Cursors.Hand; return true; }
            }
            return false;
        }

        private bool HostMouseUp346(StripHost h, MouseEventArgs e)
        {
            if (_dekoDrag < 0) return false;
            _dekoDrag = -1;
            h.Capture = false;
            h.Cursor = Cursors.Default;
            ScheduleSave();
            return true;
        }

        // ================================================================ 112 Kabel unter dem Pult

        private float _cableSwing;
        private double _nextCableAnim;

        private void ApplyCables346()
        {
            int r = Math.Max(_settings.CablesOn ? Theme.Px(34) : 0, _settings.DeskMirrorOn ? Theme.Px(58) : 0);   // 4.2: Platz fuer den Tisch
            if (_channelHost != null) _channelHost.BottomReserve = r;
            if (_outputHost != null) _outputHost.BottomReserve = r;
        }

        private void PaintHost346(StripHost h, Graphics g)
        {
            PaintDesk420(h, g);     // 4.2: spiegelnder Tisch
            PaintHost400(h, g);     // 4.0: Cover, Musik-Gluehen
            PaintSpecBg350(h, g);   // 3.5: Spektrum hinter den Kanaelen
            double t = DekoClock.Elapsed.TotalSeconds;
            if (_settings.CablesOn)
            {
                int top = h.StripBottom - Theme.Px(6), bottom = h.ClientSize.Height;
                int i = 0;
                foreach (Control c in h.Controls)
                {
                    if (!(c is StripBase sb) || !c.Visible) continue;
                    Color col = c is ChannelStrip cs ? Color.FromArgb(255, Color.FromArgb(cs.Config.Color)) : Color.FromArgb(0x6a, 0x70, 0x7a);
                    float lv = Math.Min(1f, Math.Max(sb.LastPeakL, sb.LastPeakR));
                    float swing = (float)Math.Sin(t * 5.2 + i * 1.7) * Theme.F(3.5f) * lv * (0.4f + 0.6f * _cableSwing);
                    float x = c.Left + c.Width * 0.5f;
                    Theme.DrawCable(g, x, top, bottom, col, i % 2 == 0 ? -1 : 1, swing);
                    i++;
                }
            }
            if (h == _channelHost && DekoShown)
                foreach (var d in _settings.Deko) if (d.Kind < 4 || Theme.MoreDeko) Theme.DrawDeko(g, d.Kind, DekoCenter(d), t);   // 4.0 finale: animierte Deko nur, wenn an
        }

        private void TickLook346(double now, float dt)
        {
            if (!PultShown) return;
            if (DekoShown && now >= _nextDekoAnim)
            {
                _nextDekoAnim = now + 0.066;   // Dampf und Blaetter etwa 15x pro Sekunde
                foreach (var d in _settings.Deko)
                    if (d.Kind == 0 || d.Kind == 1 || d.Kind == 3 || (d.Kind >= 4 && Theme.MoreDeko))
                    {
                        int grow = d.Kind == 4 ? Theme.Px(Theme.DekoSize) : 4;   // Diskokugel: Lichtpunkte drumherum
                        _channelHost.Invalidate(Rectangle.Inflate(Rectangle.Ceiling(DekoRect(d)), grow, grow), false);
                    }
            }
            if (_settings.CablesOn && now >= _nextCableAnim)
            {
                _nextCableAnim = now + 0.04;
                float bass = Math.Min(1f, _master?.LevelNow ?? 0f);
                _cableSwing += (bass - _cableSwing) * 0.3f;
                foreach (var h in new[] { _channelHost, _outputHost })
                    h.Invalidate(new Rectangle(0, h.StripBottom - Theme.Px(8), h.ClientSize.Width, h.ClientSize.Height - h.StripBottom + Theme.Px(8)), false);
            }
        }

        private void AddLook346(SPage p)
        {
            p.Section("Noch mehr Optik");
            p.Toggle("Ausgänge breiter ziehen", "Wie bei den Kanälen: die rechte Kante von MASTER oder einem Ausgang mit der Maus ziehen (Strg = alle gleich, Doppelklick = normal).",
                () => _settings.OutDragOn, v => { _settings.OutDragOn = v; SetOutWidth("master", OutWidthOf("master"), false, true); });
            p.Toggle("Deko auf dem Pult", "Rechtsklick auf eine freie Stelle neben den Kanälen → Deko hierhin: dampfende Kaffeetasse, Pflanze, Kopfhörer oder Gummiente. Mit der Maus verschieben, Rechtsklick → wegnehmen.",
                () => _settings.DekoOn, v => { _settings.DekoOn = v; _channelHost.Invalidate(); ScheduleSave(); });
            p.Toggle("Kabel unter dem Pult", "Unter jedem Kanal hängt ein Kabel in der Kanalfarbe heraus – es wackelt leicht mit dem Bass.",
                () => _settings.CablesOn, v => { _settings.CablesOn = v; ApplyCables346(); RelayoutAllStrips(); ScheduleSave(); });
            p.Toggle("Röhrenbild in den Anzeigen", "Spektrum, Oszilloskop, Stereo-Bild und Lautheits-Verlauf bekommen Zeilen, leichtes Flimmern und dunklere Ecken wie ein alter Röhrenmonitor.",
                () => _settings.CrtOn, v => { _settings.CrtOn = v; Theme.CrtOn = v; Theme.LampVersion++; _bridge.Invalidate(true); ScheduleSave(); });
        }
    }
}
