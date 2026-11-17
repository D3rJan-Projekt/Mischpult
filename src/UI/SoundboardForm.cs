using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>Soundboard: Sounds per Klick oder Hotkey abspielen – fuer dich, in den Stream und/oder in den Sprach-Ausgang.</summary>
    internal sealed class SoundboardForm : Form
    {
        private readonly MixerForm _mixer;
        private readonly MixerSettings _s;
        private readonly Timer _timer = new Timer { Interval = 50 };
        private string _hover;
        private int _scroll;

        public SoundboardForm(MixerForm mixer, MixerSettings settings)
        {
            _mixer = mixer;
            _s = settings;
            Text = "Soundboard – Mischpult";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.Px(640), Theme.Px(460));
            MinimumSize = new Size(Theme.Px(480), Theme.Px(340));
            AllowDrop = true;
            try { Icon = mixer.Icon; } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _timer.Tick += (o, e) => { if (_mixer.AnyPadPlaying || _wasPlaying) Invalidate(); _wasPlaying = _mixer.AnyPadPlaying; };
            _timer.Start();
        }

        private bool _wasPlaying;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- Bereiche
        private int Pad => Theme.Px(14);
        private RectangleF BtnAdd => new RectangleF(Pad, Pad, Theme.F(150), Theme.F(30));
        private RectangleF BtnStop => new RectangleF(Pad + Theme.F(158), Pad, Theme.F(130), Theme.F(30));
        private RectangleF BtnVoice => new RectangleF(ClientSize.Width - Pad - Theme.F(200), Pad, Theme.F(200), Theme.F(30));
        private RectangleF ChkMe => new RectangleF(Pad, Pad + Theme.F(40), Theme.F(120), Theme.F(22));
        private RectangleF ChkStream => new RectangleF(Pad + Theme.F(126), Pad + Theme.F(40), Theme.F(140), Theme.F(22));
        private RectangleF ChkVoice => new RectangleF(Pad + Theme.F(272), Pad + Theme.F(40), Theme.F(250), Theme.F(22));
        private RectangleF GridArea => new RectangleF(Pad, Pad + Theme.F(74), ClientSize.Width - 2 * Pad, ClientSize.Height - Pad * 2 - Theme.F(74));

        private int Cols => Math.Max(1, (int)((GridArea.Width + Theme.F(8)) / (Theme.F(146) + Theme.F(8))));

        private RectangleF PadRect(int i)
        {
            var g = GridArea;
            int cols = Cols;
            float w = (g.Width - (cols - 1) * Theme.F(8)) / cols, h = Theme.F(68);
            int r = i / cols, c = i % cols;
            return new RectangleF(g.X + c * (w + Theme.F(8)), g.Y + r * (h + Theme.F(8)) - _scroll, w, h);
        }

        // ---------------------------------------------------------------- Zeichnen
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);

            Theme.DrawKey(g, BtnAdd, "+  SOUND HINZUFÜGEN", false, Theme.TextDim, _hover == "add");
            Theme.DrawKey(g, BtnStop, "■  ALLES STOPPEN", _mixer.AnyPadPlaying, Theme.MuteRed, _hover == "stop");
            string vt = _s.VoiceOn && _s.VoiceDevice != null ? "SPRACH-AUSGANG: AN" : "SPRACH-AUSGANG …";
            Theme.DrawKey(g, BtnVoice, vt, _s.VoiceOn && _s.VoiceDevice != null, ChannelStrip.StreamColor, _hover == "voice");

            DrawCheck(g, ChkMe, "Ich höre es", !_s.SbNotMe, true, "me");
            DrawCheck(g, ChkStream, "Im Stream-Mix", !_s.SbNotStream, _s.StreamOn, "stream");
            DrawCheck(g, ChkVoice, "Im Sprach-Ausgang (Discord)", !_s.SbNotVoice, _s.VoiceOn && _s.VoiceDevice != null, "voicechk");

            var ga = GridArea;
            var old = g.Clip;
            g.SetClip(ga);
            if (_s.Sounds.Count == 0)
            {
                using (var b = new SolidBrush(Theme.TextFaint))
                    Theme.Str(g, "Noch keine Sounds.\n\n»Sound hinzufügen« klicken oder Dateien hier hineinziehen\n(WAV, MP3, M4A, WMA, FLAC – bis 3 Minuten).",
                        Theme.Ui(12f), b, ga, Theme.Center);
            }
            for (int i = 0; i < _s.Sounds.Count; i++) DrawPad(g, i, _s.Sounds[i]);
            g.Clip = old;
        }

        private void DrawCheck(Graphics g, RectangleF r, string text, bool on, bool enabled, string id)
        {
            var box = new RectangleF(r.X, r.Y + (r.Height - Theme.F(16)) / 2, Theme.F(16), Theme.F(16));
            Theme.FillRound(g, _hover == id && enabled ? Theme.HoverFill : Theme.Input, box, Theme.F(3));
            Theme.StrokeRound(g, Theme.DispEdgeHover, Theme.F(1), box, Theme.F(3));
            if (on)
                using (var pen = new Pen(enabled ? Theme.Accent : Theme.TextFaint, Theme.F(2)))
                    g.DrawLines(pen, new[] { new PointF(box.X + Theme.F(3.5f), box.Y + Theme.F(8)), new PointF(box.X + Theme.F(6.5f), box.Y + Theme.F(11.5f)), new PointF(box.X + Theme.F(12.5f), box.Y + Theme.F(4.5f)) });
            using (var b = new SolidBrush(enabled ? Theme.Text : Theme.TextFaint))
                Theme.Str(g, text, Theme.Ui(11.5f), b, new RectangleF(box.Right + Theme.F(6), r.Y, r.Width - Theme.F(22), r.Height), Theme.Left);
        }

        private void DrawPad(Graphics g, int i, SoundPad p)
        {
            var r = PadRect(i);
            if (r.Bottom < GridArea.Y || r.Y > GridArea.Bottom) return;
            bool hover = _hover == "pad:" + p.Id;
            double prog = _mixer.PadProgress(p.Id);
            string err = _mixer.PadError(p.Id);
            Color col = p.Color != 0 ? Color.FromArgb(p.Color) : Color.FromArgb(MixerSettings.Palette[(i + 1) % (MixerSettings.Palette.Length - 1)]);
            Theme.FillVertical(g, r, hover ? Theme.PadHoverTop : Theme.PadTop, hover ? Theme.PadHoverBottom : Theme.PadBottom, Theme.F(6));
            Theme.StrokeRound(g, prog >= 0 ? col : Theme.PanelEdge, Theme.F(prog >= 0 ? 2 : 1), r, Theme.F(6));
            using (var b = new SolidBrush(col)) g.FillRectangle(b, r.X + Theme.F(1), r.Y + Theme.F(6), Theme.F(4), r.Height - Theme.F(12));
            using (var b = new SolidBrush(err != null ? Theme.MuteRed : Theme.Text))
            using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                Theme.Str(g, p.Name, Theme.Ui(12f, FontStyle.Bold), b, new RectangleF(r.X + Theme.F(12), r.Y + Theme.F(8), r.Width - Theme.F(18), Theme.F(20)), sf);
            string hk = _mixer.PadHotkey(p.Id);
            string sub = err ?? ((hk.Length > 0 ? hk + "  ·  " : "") + (int)Math.Round(p.Volume * 100) + " %");
            using (var b = new SolidBrush(err != null ? Theme.MuteRed : Theme.TextDim))
            using (var sf = new StringFormat(Theme.Left) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                Theme.Str(g, sub, Theme.Ui(10f), b, new RectangleF(r.X + Theme.F(12), r.Y + Theme.F(30), r.Width - Theme.F(18), Theme.F(16)), sf);
            if (prog >= 0)
            {
                var bar = new RectangleF(r.X + Theme.F(12), r.Bottom - Theme.F(12), r.Width - Theme.F(24), Theme.F(4));
                Theme.FillRound(g, Theme.Deep, bar, Theme.F(2));
                Theme.FillRound(g, col, new RectangleF(bar.X, bar.Y, Math.Max(Theme.F(4), bar.Width * (float)Math.Min(1, prog)), bar.Height), Theme.F(2));
            }
        }

        // ---------------------------------------------------------------- Maus & Drag-and-drop
        private string HitTest(Point p)
        {
            if (BtnAdd.Contains(p)) return "add";
            if (BtnStop.Contains(p)) return "stop";
            if (BtnVoice.Contains(p)) return "voice";
            if (ChkMe.Contains(p)) return "me";
            if (ChkStream.Contains(p)) return "stream";
            if (ChkVoice.Contains(p)) return "voicechk";
            if (!GridArea.Contains(p)) return null;
            for (int i = 0; i < _s.Sounds.Count; i++) if (PadRect(i).Contains(p)) return "pad:" + _s.Sounds[i].Id;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string h = HitTest(e.Location);
            if (h == null) return;
            if (h.StartsWith("pad:"))
            {
                var pad = _s.Sounds.FirstOrDefault(x => "pad:" + x.Id == h);
                if (pad == null) return;
                if (e.Button == MouseButtons.Right) _mixer.ShowPadMenu(pad, PointToScreen(e.Location), this);
                else if (e.Button == MouseButtons.Left) _mixer.PlayPad(pad);
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            switch (h)
            {
                case "add": AddFiles(); break;
                case "stop": _mixer.StopAllSounds(); break;
                case "voice": _mixer.ShowVoiceDialog(); break;
                case "me": _s.SbNotMe = !_s.SbNotMe; _mixer.SettingsChanged(); break;
                case "stream":
                    if (!_s.StreamOn) MessageBox.Show(this, "Erst den Stream-Mix einschalten (OPTIONEN → Stream-Mix).", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else { _s.SbNotStream = !_s.SbNotStream; _mixer.SettingsChanged(); }
                    break;
                case "voicechk":
                    if (!(_s.VoiceOn && _s.VoiceDevice != null)) _mixer.ShowVoiceDialog();
                    else { _s.SbNotVoice = !_s.SbNotVoice; _mixer.SettingsChanged(); }
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int rows = (_s.Sounds.Count + Cols - 1) / Cols;
            float content = rows * Theme.F(76);
            int max = (int)Math.Max(0, content - GridArea.Height);
            _scroll = Math.Max(0, Math.Min(max, _scroll - e.Delta / 2));
            Invalidate();
        }

        private void AddFiles()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Sounds hinzufügen",
                Filter = "Audiodateien|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.mp4|Alle Dateien|*.*",
                Multiselect = true,
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) _mixer.AddSounds(dlg.FileNames);
            }
            Invalidate();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            var ok = files.Where(f => SoundDecoder.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            if (ok.Length > 0) _mixer.AddSounds(ok);
            Invalidate();
        }
    }
}
