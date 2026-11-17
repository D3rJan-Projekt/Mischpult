using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>4.0 – Musik: Spulen, Zufall, Wiederholen (ueber die Windows-Mediensteuerung).</summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 208 Mehr Musik-Tasten

        private void AddTopKeysMedia400(List<TopKey> l)
        {
            if (!_settings.MediaMoreOn) return;
            l.Add(new TopKey { Id = "seek-", Icon = 1, Priority = 2, Tip = "Musik: 10 Sekunden zurück\nRechtsklick: Zufall, Wiederholen" });
            l.Add(new TopKey { Id = "seek+", Icon = 2, Priority = 2, Tip = "Musik: 10 Sekunden vor\nRechtsklick: Zufall, Wiederholen" });
        }

        private void MediaAction400(string what)
        {
            if (!_settings.MediaMoreOn) return;
            Task.Run(() =>
            {
                string msg; bool ok;
                switch (what)
                {
                    case "seek-": ok = NowPlayingWatcher.Seek(-10); msg = "10 Sekunden zurück"; break;
                    case "seek+": ok = NowPlayingWatcher.Seek(10); msg = "10 Sekunden vor"; break;
                    case "shuffle": { var r = NowPlayingWatcher.ToggleShuffle(); ok = r.HasValue; msg = r == true ? "Zufall an" : "Zufall aus"; break; }
                    case "repeat": { int r = NowPlayingWatcher.CycleRepeat(); ok = r >= 0; msg = r == 1 ? "Titel wiederholen" : r == 2 ? "Liste wiederholen" : "Wiederholen aus"; break; }
                    default: return;
                }
                try { BeginInvoke(new Action(() => ShowOsd("Musik", ok ? msg : "Geht mit dieser App nicht", -1, ok ? Theme.Accent : Theme.TextDim))); } catch { }
            });
        }

        public void ShowMediaMenu400(Point screen)
        {
            if (!_settings.MediaMoreOn && !_settings.ArtistVolOn) return;
            var np = _nowPlaying.Current;
            var m = NewMenu();
            m.ShowCheckMargin = true; m.ShowImageMargin = false;
            m.Items.Add(Header(np != null && !string.IsNullOrEmpty(np.Title) ? np.Text : "Musik"));
            if (_settings.MediaMoreOn)
            {
                m.Items.Add(new ToolStripMenuItem("10 Sekunden zurück", null, (s, e) => MediaAction400("seek-")));
                m.Items.Add(new ToolStripMenuItem("10 Sekunden vor", null, (s, e) => MediaAction400("seek+")));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Zufall", null, (s, e) => MediaAction400("shuffle")) { Checked = np?.Shuffle == true });
                m.Items.Add(new ToolStripMenuItem("Wiederholen: " + (np == null || np.Repeat < 0 ? "?" : np.Repeat == 1 ? "Titel" : np.Repeat == 2 ? "Liste" : "aus") + " (weiterschalten)", null, (s, e) => MediaAction400("repeat")) { Checked = np != null && np.Repeat > 0 });
                m.Items.Add(Hint("Geht nur, wenn die App es anbietet (z. B. Spotify, Media Player)."));
            }
            AddArtistMenu400(m.Items);
            m.Show(screen);
        }

        private void AddHotkeyActionsMore400(List<HotkeyAction> list)
        {
            if (_settings.MediaMoreOn)
            {
                list.Add(new HotkeyAction("media.seekback", "Musik: 10 Sekunden zurück", "Musik"));
                list.Add(new HotkeyAction("media.seekfwd", "Musik: 10 Sekunden vor", "Musik"));
                list.Add(new HotkeyAction("media.shuffle", "Musik: Zufall an/aus", "Musik"));
                list.Add(new HotkeyAction("media.repeat", "Musik: Wiederholen weiterschalten", "Musik"));
            }
            AddHotkeyActionsBedienung400(list);
            AddHotkeyActionsExtern400(list);
        }

        private bool ExecuteHotkeyMore400(string action)
        {
            switch (action)
            {
                case "media.seekback": MediaAction400("seek-"); return true;
                case "media.seekfwd": MediaAction400("seek+"); return true;
                case "media.shuffle": MediaAction400("shuffle"); return true;
                case "media.repeat": MediaAction400("repeat"); return true;
            }
            if (ExecuteHotkeyBedienung400(action)) return true;
            return ExecuteHotkeyExtern400(action);
        }

        private void OnTopKeyExtern400(string id, bool right, Point screen)
        {
            if (id == "seek-" || id == "seek+") { if (right) ShowMediaMenu400(screen); else MediaAction400(id); return; }
            OnTopKeyObs400(id, right, screen);
        }

        private void ApplyMusik400(MixerSettings s)
        {
            NowPlayingWatcher.WantModes = s.MediaMoreOn;
        }
    }
}

namespace Mischpult.UI
{
    /// <summary>4.0 – Musik: Lautstaerke pro Interpret/Podcast.</summary>
    internal sealed partial class MixerForm
    {
        // ================================================================ 211 Lautstaerke pro Interpret

        private string _artistLast;
        private float _artistBase = -1f;     // Faderstand vor der ersten automatischen Aenderung
        private string _artistChannel;

        internal static string ArtistKey(NowPlayingInfo np)
        {
            if (np == null) return null;
            string a = string.IsNullOrWhiteSpace(np.Artist) ? null : np.Artist.Trim();
            return a?.ToLowerInvariant();
        }

        /// <summary>Kanal, in dem die Musik-App laeuft.</summary>
        private ChannelConfig MusicChannel400(NowPlayingInfo np)
        {
            if (np?.AppId != null)
            {
                var c = _settings.Channels.FirstOrDefault(x => !x.IsCatchAll && x.Apps.Count > 0 && NowPlayingWatcher.AppMatches(np.AppId, x.Apps.ToArray()));
                if (c != null) return c;
            }
            if (_settings.AutoPauseMusic != null) { var c = _settings.Channels.FirstOrDefault(x => x.Id == _settings.AutoPauseMusic); if (c != null) return c; }
            return _settings.Channels.FirstOrDefault(x => string.Equals(x.Name, "MUSIK", StringComparison.OrdinalIgnoreCase) && !x.IsCatchAll) ?? CatchAll;
        }

        private void TickArtist400()
        {
            if (!_settings.ArtistVolOn) { _artistLast = null; _artistBase = -1f; return; }
            var np = _nowPlaying.Current;
            string key = ArtistKey(np);
            if (key == _artistLast) return;
            _artistLast = key;
            var ch = MusicChannel400(np);
            if (ch == null) return;
            if (key != null && _settings.ArtistVols != null && _settings.ArtistVols.TryGetValue(key, out float pos))
            {
                if (_artistBase < 0f || _artistChannel != ch.Id) { _artistBase = ch.Fader; _artistChannel = ch.Id; }
                if (Math.Abs(ch.Fader - pos) > 0.002f) { ch.Fader = pos; OnChannelChanged(ch, false); RefreshStripOf(ch); ShowOsd(ch.Name + " · " + np.Artist, "gemerkte Lautstärke " + FaderLaw.FormatDb(pos), pos, Color.FromArgb(ch.Color)); }
            }
            else if (_artistBase >= 0f && _artistChannel == ch.Id)
            {
                float back = _artistBase; _artistBase = -1f;
                if (Math.Abs(ch.Fader - back) > 0.002f) { ch.Fader = back; OnChannelChanged(ch, false); RefreshStripOf(ch); }
            }
        }

        private void RefreshStripOf(ChannelConfig c)
        {
            var s = _channelStrips.FirstOrDefault(x => x.Config == c);
            s?.InvalidateChrome();
            s?.Invalidate();
        }

        private void AddArtistMenu400(ToolStripItemCollection items)
        {
            if (!_settings.ArtistVolOn) return;
            var np = _nowPlaying.Current;
            string key = ArtistKey(np);
            items.Add(new ToolStripSeparator());
            if (key == null) { items.Add(Hint("Lautstärke pro Interpret: kein Interpret erkannt")); return; }
            var ch = MusicChannel400(np);
            bool has = _settings.ArtistVols != null && _settings.ArtistVols.ContainsKey(key);
            items.Add(new ToolStripMenuItem("Lautstärke für „" + np.Artist + "“ merken (" + (ch == null ? "–" : FaderLaw.FormatDb(ch.Fader)) + ")", null, (s, e) =>
            {
                if (ch == null) return;
                if (_settings.ArtistVols == null) _settings.ArtistVols = new Dictionary<string, float>();
                _settings.ArtistVols[key] = ch.Fader;
                _artistLast = key;
                if (_artistBase < 0f) { _artistBase = ch.Fader; _artistChannel = ch.Id; }
                ScheduleSave();
                ShowOsd("Gemerkt", np.Artist + ": " + FaderLaw.FormatDb(ch.Fader), ch.Fader, Color.FromArgb(ch.Color));
            }) { Checked = has });
            if (has) items.Add(new ToolStripMenuItem("„" + np.Artist + "“ vergessen", null, (s, e) => { _settings.ArtistVols.Remove(key); if (_settings.ArtistVols.Count == 0) _settings.ArtistVols = null; ScheduleSave(); }));
        }
    }
}
