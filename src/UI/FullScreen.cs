using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>2.18: Fenster im Vollbild (F11) – z. B. die Meterbridge oder das grosse Spektrum auf dem zweiten Bildschirm.</summary>
    internal static class FullScreen
    {
        private sealed class Saved { public Rectangle Bounds; public FormBorderStyle Border; public FormWindowState State; public bool TopMost; }
        private static readonly Dictionary<Form, Saved> _saved = new Dictionary<Form, Saved>();

        public static bool IsOn(Form f) => f != null && _saved.ContainsKey(f);

        /// <summary>Vollbild an/aus. screen = auf diesem Bildschirm (null = wo das Fenster gerade ist).</summary>
        public static void Set(Form f, bool on, Screen screen = null)
        {
            if (f == null || f.IsDisposed) return;
            if (on == IsOn(f)) { if (on && screen != null) f.Bounds = screen.Bounds; return; }
            if (on)
            {
                _saved[f] = new Saved { Bounds = f.WindowState == FormWindowState.Normal ? f.Bounds : f.RestoreBounds, Border = f.FormBorderStyle, State = f.WindowState, TopMost = f.TopMost };
                var sc = screen ?? Screen.FromControl(f);
                f.WindowState = FormWindowState.Normal;
                f.FormBorderStyle = FormBorderStyle.None;
                f.Bounds = sc.Bounds;
                f.TopMost = true;
                f.FormClosed -= Forget;
                f.FormClosed += Forget;
            }
            else
            {
                var s = _saved[f];
                _saved.Remove(f);
                f.TopMost = s.TopMost;
                f.FormBorderStyle = s.Border;
                f.Bounds = s.Bounds;
                f.WindowState = s.State;
            }
        }

        public static void Toggle(Form f) => Set(f, !IsOn(f));

        private static void Forget(object sender, FormClosedEventArgs e) { if (sender is Form f) _saved.Remove(f); }

        // ---- Leerlauf (fuer den Bildschirmschoner)
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>Wie lange (ms) keine Maus/Tastatur benutzt wurde – 0, wenn es nicht feststellbar ist (nicht Windows).</summary>
        public static long IdleMs()
        {
            if (!Native.Win32.IsWindows) return 0;
            try
            {
                var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref li)) return 0;
                return Math.Max(0L, (long)(uint)(Environment.TickCount - (int)li.dwTime));
            }
            catch { return 0; }
        }
    }
}
