using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.1 (Teil 2): stumme Kanaele abdunkeln, SOLO hebt hervor, Rand blinkt bei Uebersteuerung, inaktive Kanaele einklappen,
    /// Lautstaerke-Einblendung einstellbar, Design per Tastenkuerzel, Zufalls-Design beim Start, animierte Kanaele.
    /// Alles einzeln an/aus in den Einstellungen.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private void ApplyFeatures310bStatics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FOLD") == "1") s.AutoFoldOn = true;
                if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OSDPOS"), out int op)) s.OsdPos = op;
            }
            ChannelStrip.MuteDimOn = !s.MuteDimOff;
            ChannelStrip.SoloDimOn = !s.SoloDimOff;
            StripHost.AnimateOn = !s.AnimStripsOff;
            OsdForm.Position = s.OsdPos;
            OsdForm.SizeMode = s.OsdSize;
            // Zufalls-Design: vor dem ersten Zeichnen ein anderes Design aus den Favoriten
            if (s.RandomDesignOn && !demo)
            {
                var pool = (s.RandomPool ?? new List<int>()).Where(i => i >= 0 && i < Theme.StyleNames.Length).Distinct().ToList();
                if (pool.Count == 0) pool = Enumerable.Range(0, Theme.StyleNames.Length).ToList();
                if (pool.Count > 1) pool.Remove(s.UiStyle);
                s.UiStyle = pool[new Random().Next(pool.Count)];
            }
        }

        // ================================================================ Takt

        private int _clipCount;
        private float _clipFlash;
        private double _nextFoldCheck;
        private readonly Dictionary<string, double> _lastSound = new Dictionary<string, double>();

        /// <summary>Helligkeit des roten Blinkens am Rand (0 = aus).</summary>
        public float ClipFlashLevel => _clipFlash <= 0f ? 0f : Math.Min(1f, _clipFlash / 0.25f);

        private void TickFeatures310(double now, float dt)
        {
            // Rand blinkt bei Uebersteuerung
            if (!_settings.ClipFlashOff && _ambiL != null)
            {
                int clips = 0;
                foreach (var s in AllStrips()) if (s != null && s.ShowClip) clips++;
                bool was = _clipFlash > 0f;
                if (clips > _clipCount) _clipFlash = 0.45f;
                _clipCount = clips;
                if (_clipFlash > 0f) _clipFlash -= dt;
                if (_clipFlash > 0f || was) { _ambiL.Invalidate(); _ambiR.Invalidate(); _ambiB.Invalidate(); }
            }
            // inaktive Kanaele einklappen (eine Minute ohne Ton), sofort wieder auf, wenn Ton kommt
            if (now >= _nextFoldCheck || (_settings.AutoFoldOn && AnyFoldedPlaying()))
            {
                _nextFoldCheck = now + 1;
                bool relayout = false;
                foreach (var s in _channelStrips)
                {
                    bool playing = s.Config.Apps.Any(k => _appsByKey.TryGetValue(k, out var a) && a.Active) || (s.Config.IsCatchAll && s.ShowL > 0.02f);
                    if (playing || !_lastSound.ContainsKey(s.Config.Id)) _lastSound[s.Config.Id] = now;
                    bool fold = _settings.AutoFoldOn && !s.Config.IsCatchAll && now - _lastSound[s.Config.Id] > FoldAfterSec;
                    if (fold != s.Folded) { s.Folded = fold; s.InvalidateChrome(); relayout = true; }
                }
                if (relayout) { _channelHost.AnimateNextLayout(); _channelHost.Relayout(); }
            }
        }

        private double FoldAfterSec => _demo && Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FOLD") == "1" ? 2 : 60;

        private bool AnyFoldedPlaying()
        {
            foreach (var s in _channelStrips)
                if (s.Folded && s.Config.Apps.Any(k => _appsByKey.TryGetValue(k, out var a) && a.Active)) return true;
            return false;
        }

        // ================================================================ Design per Tastenkuerzel

        private int _designBeforeFav = -1;

        private void NextDesign(int dir)
        {
            int n = Theme.StyleNames.Length;
            int d = ((_settings.UiStyle + dir) % n + n) % n;
            SetScheme(_settings.ColorScheme, _settings.AccentColor, d);
            ShowOsd("Design", Theme.StyleNames[d], -1, Theme.Accent);
            ScheduleSave();
        }

        private void FavDesign()
        {
            int fav = _settings.FavDesign ?? -1;
            if (fav < 0 || fav >= Theme.StyleNames.Length) { ShowOsd("Lieblingsdesign", "Noch keins gewählt (OPTIONEN → Aussehen)", -1, Theme.Accent); return; }
            int d;
            if (_settings.UiStyle == fav && _designBeforeFav >= 0 && _designBeforeFav != fav) { d = _designBeforeFav; _designBeforeFav = -1; }
            else { _designBeforeFav = _settings.UiStyle; d = fav; }
            SetScheme(_settings.ColorScheme, _settings.AccentColor, d);
            ShowOsd("Design", Theme.StyleNames[d], -1, Theme.Accent);
            ScheduleSave();
        }

        private void AddHotkeyActions310b(List<HotkeyAction> list)
        {
            list.Add(new HotkeyAction("design.next", "Nächstes Design", "Aussehen"));
            list.Add(new HotkeyAction("design.prev", "Voriges Design", "Aussehen"));
            list.Add(new HotkeyAction("design.fav", "Lieblingsdesign an/aus", "Aussehen"));
        }

        private bool ExecuteHotkey310b(string action)
        {
            switch (action)
            {
                case "design.next": NextDesign(1); return true;
                case "design.prev": NextDesign(-1); return true;
                case "design.fav": FavDesign(); return true;
            }
            return false;
        }

        // ================================================================ Einstellungen

        private void SetMuteDim(bool on) { _settings.MuteDimOff = !on; ChannelStrip.MuteDimOn = on; RefreshStrips(); ScheduleSave(); }
        private void SetSoloDim(bool on) { _settings.SoloDimOff = !on; ChannelStrip.SoloDimOn = on; RefreshStrips(); ScheduleSave(); }

        private void SetClipFlash(bool on)
        {
            _settings.ClipFlashOff = !on;
            if (_ambiL != null) _ambiL.Visible = _ambiR.Visible = _ambiB.Visible = on || !_settings.AmbilightOff;
            _clipFlash = 0f;
            ScheduleSave();
        }

        private void SetAutoFold(bool on)
        {
            _settings.AutoFoldOn = on;
            _nextFoldCheck = 0;
            if (!on)
            {
                bool any = false;
                foreach (var s in _channelStrips) if (s.Folded) { s.Folded = false; s.InvalidateChrome(); any = true; }
                if (any) { _channelHost.AnimateNextLayout(); _channelHost.Relayout(); }
            }
            ScheduleSave();
        }

        private void SetAnimStrips(bool on) { _settings.AnimStripsOff = !on; StripHost.AnimateOn = on; ScheduleSave(); }

        private void SetOsdPos(int i) { _settings.OsdPos = Math.Max(0, Math.Min(OsdForm.PositionNames.Length - 1, i)); OsdForm.Position = _settings.OsdPos; ShowOsd("Lautstärke", "So sieht es aus", 0.6f, Theme.Accent); ScheduleSave(); }
        private void SetOsdSize(int i) { _settings.OsdSize = Math.Max(0, Math.Min(OsdForm.SizeNames.Length - 1, i)); OsdForm.SizeMode = _settings.OsdSize; ShowOsd("Lautstärke", "So sieht es aus", 0.6f, Theme.Accent); ScheduleSave(); }

        /// <summary>Leuchten &amp; Anzeigen: Abdunkeln, Rand blinkt.</summary>
        private void AddStrip310bSettings(SPage p)
        {
            p.Toggle("Stumme Kanäle abdunkeln", "Ein Kanal auf MUTE wird insgesamt dunkler – Fader, Knöpfe, Schild. So sieht man sofort, was stumm ist. Die MUTE-Taste leuchtet weiter hell.",
                () => !_settings.MuteDimOff, v => SetMuteDim(v));
            p.Toggle("SOLO hebt hervor", "Ist ein Kanal auf SOLO, werden alle anderen Kanäle abgedunkelt – der gesoloete steht hell da.",
                () => !_settings.SoloDimOff, v => SetSoloDim(v));
            p.Toggle("Rand blinkt bei Übersteuerung", "Der Pultrand blinkt kurz rot, sobald irgendwo CLIP kommt (zu laut). Geht auch, wenn das Ambilight aus ist – dann ist der Rand sonst dunkel.",
                () => !_settings.ClipFlashOff, v => SetClipFlash(v));
        }

        /// <summary>Kanaele &amp; Ausgaenge: einklappen, animiert.</summary>
        private void AddChannels310bSettings(SPage p)
        {
            p.Toggle("Inaktive Kanäle einklappen", "Spielt in einem Kanal eine Minute lang nichts, wird er schmal und etwas dunkler – sobald wieder Ton kommt, geht er sofort auf. „Alle übrigen“ bleibt immer offen.",
                () => _settings.AutoFoldOn, v => SetAutoFold(v));
            p.Toggle("Kanäle gleiten weich", "Neue Kanäle gleiten herein, beim Löschen, Umsortieren und Einklappen rutschen die anderen weich zur Seite, statt zu springen.",
                () => !_settings.AnimStripsOff, v => SetAnimStrips(v));
        }

        /// <summary>Allgemein/Steuerung: Lautstaerke-Einblendung.</summary>
        private void AddOsd310bSettings(SPage p)
        {
            p.Choice("Lautstärke-Einblendung: Ort", () => "Wo die kleine Anzeige erscheint, wenn du per Tastenkürzel, MIDI oder Mausrad die Lautstärke änderst (auf dem Bildschirm mit der Maus).",
                OsdForm.PositionNames, () => _settings.OsdPos, i => SetOsdPos(i));
            p.Choice("Lautstärke-Einblendung: Größe", () => "Klein, normal oder groß.",
                OsdForm.SizeNames, () => _settings.OsdSize, i => SetOsdSize(i));
        }

        /// <summary>Aussehen: Lieblingsdesign, Zufalls-Design.</summary>
        private void AddDesign310bSettings(SPage p)
        {
            var favNames = new[] { "Keins" }.Concat(Theme.StyleNames).ToArray();
            p.Choice("Lieblingsdesign (für die Taste)", () => "Mit dem Tastenkürzel „Lieblingsdesign an/aus“ springst du zu diesem Design und wieder zurück. Dazu gibt es „Nächstes Design“ und „Voriges Design“ (Steuerung → Tastenkürzel).",
                favNames, () => (_settings.FavDesign ?? -1) + 1, i => { _settings.FavDesign = i <= 0 ? (int?)null : i - 1; ScheduleSave(); });
            p.Toggle("Zufalls-Design beim Start", "Bei jedem Start ein anderes Design – aus den Designs, die du darunter ankreuzt (keins angekreuzt = aus allen).",
                () => _settings.RandomDesignOn, v => { _settings.RandomDesignOn = v; ScheduleSave(); });
            for (int i = 0; i < Theme.StyleNames.Length; i++)
            {
                int k = i;
                var t = p.Toggle("  " + Theme.StyleNames[i], (string)null,
                    () => _settings.RandomPool != null && _settings.RandomPool.Contains(k),
                    v =>
                    {
                        if (_settings.RandomPool == null) _settings.RandomPool = new List<int>();
                        _settings.RandomPool.Remove(k);
                        if (v) _settings.RandomPool.Add(k);
                        ScheduleSave();
                    });
                t.Visible = () => _settings.RandomDesignOn;
            }
        }
    }
}
