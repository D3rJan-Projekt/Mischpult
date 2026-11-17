using System;

namespace Mischpult.UI
{
    /// <summary>4.3 – Optik im Takt: Blitze beim Gewitter, Plattenspieler-Deko, Fenstergroesse fuer den Sternenwirbel.</summary>
    internal sealed partial class MixerForm
    {
        private readonly Random _rnd430 = new Random();
        private double _stormNext430, _stormLast430;
        private float _stormAvg430, _turnSpeed430;

        /// <summary>Bewegter Hintergrund "Gewitter" ist gewaehlt.</summary>
        private bool Storm430 => _settings.LiveBg == 10;
        /// <summary>Ein Plattenspieler steht auf dem Pult (und animierte Deko ist an).</summary>
        private bool TurntableShown430 => _settings.AnimDecoOn && DekoShown && _settings.Deko.Exists(d => d.Kind == 7);

        /// <summary>Seite Aussehen: 4.3 – Optik (die Auswahl selbst steht bei Design, Wand, Hintergrund und Deko).</summary>
        private void AddLook430(SPage p)
        {
            p.Section("4.3 – Optik");
            p.Info("Neue Designs, Hintergründe, Wände und Deko", () => "Steampunk, Arktis und Japan stehen oben unter „Design“, Gewitter, Sternenwirbel und Glühwürmchen unter „Bewegter Hintergrund“, Graffiti, Bücherregal und Serverraum unter „Wand hinter dem Pult“. Den Plattenspieler stellst du mit Rechtsklick in eine freie Fläche → Deko hierhin auf (dafür „Deko auf dem Pult“ und „Animierte Deko“ einschalten).");
        }

        private void TickLook430(double now, float dt)
        {
            if (!PultShown) return;
            if (_settings.LiveBg == 11 && ClientSize.Width > 10) Theme.LiveForm = ClientSize;
            TickStorm430(now, dt);
            TickTurntable430(dt);
        }

        private void TickStorm430(double now, float dt)
        {
            if (!Storm430) { Theme.StormFlash = 0f; _stormNext430 = 0; return; }
            if (ClientSize.Width > 10) Theme.LiveForm = ClientSize;
            Theme.StormFlash = Math.Max(0f, Theme.StormFlash - dt * 2.2f);
            float lvl = Math.Min(1f, _master?.LevelNow ?? 0f);
            float bass = _snap.MasterBass >= 0f ? Math.Min(1f, _snap.MasterBass * 3.2f) : lvl;
            bool hit = bass > 0.3f && bass > _stormAvg430 * 1.7f && now - _stormLast430 > 0.9;
            _stormAvg430 += (bass - _stormAvg430) * Math.Min(1f, dt * 1.5f);
            if (_stormNext430 == 0) _stormNext430 = now + 5;
            if (now >= _stormNext430) hit = true;   // auch ohne Musik blitzt es ab und zu
            if (hit)
            {
                Theme.StormFlash = 1f;
                Theme.StormSeed = _rnd430.Next();
                _stormLast430 = now;
                _stormNext430 = now + 8 + _rnd430.NextDouble() * 10;
            }
            if (_demo && DemoOn("STORMFLASH")) { Theme.StormFlash = Math.Max(Theme.StormFlash, 0.95f); Theme.StormSeed = 7; }
        }

        private void TickTurntable430(float dt)
        {
            if (!TurntableShown430)
            {
                Theme.DekoCover = null;
                return;
            }
            var np = _nowPlaying.Current;
            bool playing = (np != null && np.Playing) || (_demo && DemoOn("TURNPLAY"));
            Theme.DekoPlaying = playing;
            Theme.DekoCover = np?.Cover;
            Theme.DekoArm = playing ? Math.Min(1f, Theme.DekoArm + dt * 1.2f) : Math.Max(0f, Theme.DekoArm - dt * 1.2f);
            bool spin = playing && Theme.DekoArm >= 0.95f;
            _turnSpeed430 = spin ? Math.Min(1f, _turnSpeed430 + dt * 1.5f) : Math.Max(0f, _turnSpeed430 - dt * 0.8f);
            Theme.DekoSpin = (Theme.DekoSpin + dt * 200.0 * _turnSpeed430) % 360.0;   // 33 1/3 Umdrehungen pro Minute
        }
    }
}
