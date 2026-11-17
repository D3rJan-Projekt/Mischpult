using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.0: Zeichnen ueber die Grafikkarte (Direct2D) – an/aus in den Einstellungen, mit automatischem Rueckfall:
    /// geht etwas schief, zeichnet das Pult sofort wieder wie bisher. Stuerzt es dabei sogar ab, merkt es das beim
    /// naechsten Start (Merkdatei) und schaltet die Grafikkarte von selbst aus.
    /// </summary>
    internal static class GpuDraw
    {
        /// <summary>In den Einstellungen eingeschaltet.</summary>
        public static bool Wanted;
        /// <summary>Grund, warum in dieser Sitzung wieder wie bisher gezeichnet wird (null = alles gut).</summary>
        public static string Disabled;
        /// <summary>Wie viele Bilder schon ohne Fehler ueber die Grafikkarte kamen.</summary>
        public static int FramesOk;
        public static bool Enabled => Wanted && Disabled == null && D2DCanvas.Available;
        /// <summary>Fuer Tests: Ordner der Merkdatei.</summary>
        public static string Folder = MixerSettings.Folder;
        public static string GuardFile => Path.Combine(Folder, "grafikkarte-test.tmp");
        private static bool _armed;
        public const int FramesToTrust = 120;

        /// <summary>Beim Start: lag beim letzten Mal noch die Merkdatei da (= Absturz beim Zeichnen ueber die Grafikkarte)?</summary>
        public static bool CrashedLastTime()
        {
            try
            {
                if (!File.Exists(GuardFile)) return false;
                File.Delete(GuardFile);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Vor dem ersten Bild dieser Sitzung: Merkdatei anlegen.</summary>
        public static void BeginFrame()
        {
            if (_armed || FramesOk >= FramesToTrust) return;
            _armed = true;
            try { Directory.CreateDirectory(Folder); File.WriteAllText(GuardFile, DateTime.Now.ToString("s")); } catch { }
        }

        /// <summary>Bild ist gut angekommen; nach einigen Bildern ist die Grafikkarte vertrauenswuerdig (Merkdatei weg).</summary>
        public static void FrameOk()
        {
            FramesOk++;
            if (FramesOk == FramesToTrust) Disarm();
        }

        public static void Disarm()
        {
            _armed = false;
            try { if (File.Exists(GuardFile)) File.Delete(GuardFile); } catch { }
        }

        /// <summary>Fehler: diese Sitzung wieder wie bisher zeichnen.</summary>
        public static void Fail(string why)
        {
            Disabled = string.IsNullOrEmpty(why) ? "Fehler" : why;
            Disarm();
        }

        public static string Status =>
            !Wanted ? "aus" : !D2DCanvas.Available ? "nicht verfügbar (braucht Windows mit Direct2D)" : Disabled != null ? "zurück auf normales Zeichnen: " + Disabled
            : FramesOk > 0 ? "läuft (Direct2D)" : "an – wird beim nächsten großen VU-Meter/Spektrum benutzt";
    }

    internal static partial class Theme
    {
        /// <summary>3.0: ein LED-Segment auf der Grafikkarte (eckig, rund oder Strich wie FillSeg).</summary>
        public static void FillSegGpu(D2DCanvas c, RectangleF r, Color col)
        {
            switch (LedShape)
            {
                case 1:
                    float d = Math.Min(r.Width, r.Height);
                    if (r.Width <= r.Height * 1.8f) c.FillEllipse(new RectangleF(r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d), col);
                    else c.FillRoundRect(r, d / 2f, col);
                    break;
                case 2:
                    float lh = Math.Max(1f, (float)Math.Round(r.Height * 0.45f));
                    c.FillRect(new RectangleF(r.X, r.Y + (float)Math.Round((r.Height - lh) / 2f), r.Width, lh), col);
                    break;
                default:
                    c.FillRect(r, col);
                    break;
            }
        }

        /// <summary>3.0: Zeiger, Schleppzeiger und PEAK-Leuchte auf der Grafikkarte (Abdeckung und Glas kommen als fertiges Bild darueber).</summary>
        public static void DrawVuPointerGpu(D2DCanvas c, RectangleF r, float p, bool dim, float peakP, int led)
        {
            VuColors(out _, out _, out _, out var red, out var needle, out _);
            if (dim) needle = Darken(needle, 0.3f);
            p = Math.Max(-0.015f, Math.Min(1.06f, p));
            VuGeometry(r, out var pv, out float R);
            float a = VuAngle(p);
            c.PushClip(r);
            if (led > 0)
            {
                var lr = VuLedRect(r);
                if (led == 2 && !dim)
                {
                    for (int i = 3; i >= 1; i--) c.FillEllipse(RectangleF.Inflate(lr, F(i * 1.2f), F(i * 1.2f)), Color.FromArgb(34, red));
                    c.FillEllipse(lr, red);
                    c.FillEllipse(RectangleF.Inflate(lr, -lr.Width * 0.22f, -lr.Height * 0.22f), Lighten(red, 0.45f));
                }
                else c.FillEllipse(lr, Mix(Color.FromArgb(0x24, 0x10, 0x10), red, 0.22f));
                c.FillEllipse(new RectangleF(lr.X + lr.Width * 0.25f, lr.Y + lr.Height * 0.15f, lr.Width * 0.35f, lr.Height * 0.3f), Color.FromArgb(led == 2 ? 170 : 70, 255, 255, 255));
            }
            float tipR = R + F(7.5f), baseR = R * 0.25f;
            if (peakP >= 0f && peakP > p + 0.006f)
            {
                float ap = VuAngle(Math.Min(1.06f, peakP));
                c.Line(Polar(pv, R * 0.5f, ap), Polar(pv, tipR - F(0.5f), ap), Alpha(dim ? Darken(red, 0.4f) : red, 215), Math.Max(1f, F(0.8f)));
            }
            var tip = Polar(pv, tipR, a); var bas = Polar(pv, baseR, a); var mid = Polar(pv, (baseR + tipR) / 2f, a);
            bool darkFace = VuFace == 2 || (VuFace == 0 && (Style == StyleModern || Style == StyleNeon));
            if (Style != StyleNeon || VuFace != 0)
            {
                var sh = darkFace ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(45, 0, 0, 0);
                float dx = F(1.6f), dy = F(1.2f);
                c.Line(new PointF(bas.X + dx, bas.Y + dy), new PointF(tip.X + dx, tip.Y + dy), sh, F(1.4f));
            }
            else c.Line(bas, tip, Alpha(Accent, 100), F(3.4f));
            c.Line(bas, mid, needle, F(1.75f));   // Zeiger laeuft zur Spitze schmal zu
            c.Line(mid, tip, needle, F(1.0f));
            c.PopClip();
        }
    }
}
