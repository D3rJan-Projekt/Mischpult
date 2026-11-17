using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>4.0 – Schaltzentrale: statische Schalter, Takt und Haken fuer alle 4.0-Funktionen.</summary>
    internal sealed partial class MixerForm
    {
        /// <summary>Alle 4.0-Schalter (bool, Standard aus) – fuer die Vorschau per MISCHPULT_DEMO_&lt;NAME&gt;=1.</summary>
        internal static readonly string[] Bools400 =
        {
            "PeqOn", "HpEqOn", "GroupFxOn", "HearEqOn", "EveningOn", "DevGuardOn", "DevHoldOn", "CoupleOn", "PanicOn", "TimeLimitOn", "SurroundOn",
            "EqShareOn", "FollowWinTheme", "RadarOn", "KnobViewOn", "NixieBarOn", "GifOn", "MotorSoundOn", "CoverBgOn", "MoreDecoOn", "MusicBgOn", "StripCustomOn",
            "TwoRowsOn", "A11yReaderOn", "A11yFocusOn", "A11yContrastOn", "A11yCalmOn", "AutomationOn", "WinWheelOn", "HotCornerOn", "AppDragOn",
            "ChanWinOn", "TopKeysOn", "SearchOn", "PartialScenesOn", "OverlayOn", "WidgetOn", "RoomOn", "TimelineOn", "StatsOn", "PlayerOn",
            "DoctorOn", "FocusOn", "RecPlanOn", "MediaMoreOn", "ArtistVolOn", "ChanTitleOn", "MidiFeedbackOn", "ObsOn", "WledOn", "SimpleSettings",
        };

        /// <summary>4.0 (finale): weitere Schalter (alle Standard aus).</summary>
        internal static readonly string[] Bools400f =
        {
            "ProCompOn", "DuckFreqOn", "DuckRulesOn", "DynEqOn", "DeEssOn", "WarmOn", "NoiseGateOn", "MsEqOn", "FxOrderOn", "InvertOn", "AutoMixOn", "HallBusOn",
            "PunchOn", "CrossOn", "MuteGroupsOn", "StreamLayerOn", "StreamLimitOn", "HumOn", "GenOn",
            "LightShowOn", "NeonOn", "SkyOn", "DesignFadeOn", "SongProgressOn", "ImageWashOn", "WallpaperOn", "AnimDecoOn", "BgGifOn", "MotorPotsOn", "FaderTrailOn",
            "SceneGalleryOn", "EqOverviewOn", "MaskingOn", "BugReportOn", "CmdSearchOn",
        };

        private void ApplyFeatures400Statics(MixerSettings s, bool demo)
        {
            if (demo)
            {
                foreach (var f in Bools400.Concat(Bools400f))
                    if (DemoFlag(demo, f.ToUpperInvariant())) typeof(MixerSettings).GetField(f)?.SetValue(s, true);
                DemoFeatures400(s);
            }
            else if (MixerSettings.DocsMode && !s.DocsRecMoved)
            {
                // 183: Aufnahmen einmal vom alten Standard (Musik) in den Dokumente-Ordner umstellen
                s.DocsRecMoved = true;
                if (string.IsNullOrEmpty(s.RecordFolder) || string.Equals(s.RecordFolder.TrimEnd('\\', '/'), MixerSettings.OldRecordFolder, StringComparison.OrdinalIgnoreCase))
                    s.RecordFolder = MixerSettings.DefaultRecordFolder;
            }
            ApplyStatics400a(s);
            ApplyKlang400(s);
            ApplyKomfort400(s);
            ApplyMusik400(s);
            ApplyOptik400(s, demo);
            ApplyAufbau400(s);
            ApplyBedienung400(s);
            ApplyExtern400(s);
            ApplyPro400(s);       // 4.0 finale: Profi-Ton
            ApplyFinal400(s, demo);
            ApplyUpdate410(s, demo);   // 4.1
            ApplyLook420(s, demo);     // 4.2
        }

        /// <summary>Einmal nach dem Anzeigen (aus ShownFeatures340).</summary>
        private void HookFeatures400()
        {
            HookFundament400();
            _top.TopKeyClicked += OnTopKey400;
            _top.MediaMenu += ShowMediaMenu400;
            RefreshTopKeys400();
            ApplyRows400();
            HookBedienung400();
            HookWindows400();
            HookExtern400();
            HookFinal400();
            HookUpdate410();   // 4.1
            HookLook420();     // 4.2
            if (_demo) DemoShown400();
        }

        private double _last400 = -1, _nextSlow400, _nextSec400;

        private void TickFeatures400(double now)
        {
            float dt = _last400 < 0 ? 0f : (float)Math.Min(0.5, now - _last400);
            _last400 = now;
            TickKomfort400(now, dt);
            TickOptik400(now, dt);
            TickA11y400(now);
            TickBedienung400(now);
            TickWindows400(now);
            TickExtern400(now);
            TickPro400(dt);       // 4.0 finale
            TickFinal400(now, dt);
            TickLook420(now, dt);   // 4.2
            if (now >= _nextSec400)
            {
                _nextSec400 = now + 1.0;
                TickFocus400();
                TickRecPlan400();
                TickArtist400();
                TickUpdate410(now);   // 4.1
            }
            if (now >= _nextSlow400)
            {
                _nextSlow400 = now + 5.0;
                TickCloud400();
            }
        }

        /// <summary>Optionen-Schnellmenue (Rechtsklick auf OPTIONEN): 4.0-Fenster und Profile.</summary>
        private void AddOptions400(ContextMenuStrip m)
        {
            var items = new List<ToolStripItem>();
            AddProfileMenu400(items);
            AddWindowsMenu400(items);   // Fenster: Raum, Zeitleiste, Statistik, Ton-Doktor, Mini-Player, Widget
            if (items.Count == 0) return;
            m.Items.Add(new ToolStripSeparator());
            foreach (var it in items) m.Items.Add(it);
        }

        /// <summary>Kanal-Menue: alle 4.0-Eintraege.</summary>
        private void AddChannel400Menu(ToolStripItemCollection items, ChannelConfig c, ChannelStrip strip)
        {
            AddChannelKlang400(items, c);
            AddTimeLimitItems400(items, c);
            AddChannelPro400(items, c);   // 4.0 finale: Profi-Ton
            if (_settings.ChanWinOn) items.Add(new ToolStripMenuItem("Im eigenen Fenster", null, (s, e) => OpenChanWin400(c)) { Checked = _chanWins.ContainsKey("ch:" + c.Id) });   // 4.0
        }

        // ================================================================ Kopfleiste: Zusatz-Tasten

        private void RefreshTopKeys400()
        {
            if (_top == null) return;
            var l = new List<TopKey>();
            if (_settings.TopKeysOn && _settings.TopKeys != null)
                for (int i = 0; i < _settings.TopKeys.Count; i++)
                {
                    var k = _settings.TopKeys[i];
                    l.Add(new TopKey { Id = "user:" + i, Text = k.Label, Tip = "Eigene Taste: " + ActionLabel400(k.Action) + "\nRechtsklick: ändern", Width = Math.Max(28, 14 + 7 * k.Label.Length), Priority = 1 });
                }
            if (_settings.AutomationOn)   // 4.0: Fader-Automation
                l.Add(new TopKey { Id = "auto", Text = "AUTO", On = AutoRecording || AutoPlaying, Lit = AutoRecording ? Theme.MuteRed : Theme.LedGreen, Width = 50, Priority = 2,
                    Tip = AutoRecording ? "Automation nimmt auf – Klick: Menü" : AutoPlaying ? "Automation spielt ab – Klick: Menü" : "Fader-Automation – Klick: Menü" });
            if (_settings.FocusOn)
                l.Add(new TopKey { Id = "focus", Text = FocusKeyText(), On = _focusRun, Lit = _focusPhase == 0 ? FocusColor : BreakColor, Width = 56, Priority = 3,
                    Tip = _focusRun ? (_focusPhase == 0 ? "Fokus läuft – " : "Pause – ") + "Klick: Menü" : "Fokus-Timer starten – Rechtsklick: einstellen" });
            AddTopKeysExtern400(l);
            AddTopKeysPro400(l);   // 4.0 finale
            if (_settings.PanicOn)
                l.Add(new TopKey { Id = "panic", Icon = 5, On = PanicActive, Lit = Theme.MuteRed, Priority = 5,
                    Tip = PanicActive ? "PANIK aktiv – alles stumm. Klick: alles wie vorher" : "Panik: sofort alle Ausgänge stumm (noch ein Klick: wie vorher)" });
            AddTopKeysMedia400(l);
            _top.SetTopKeys(l);
        }

        private void OnTopKey400(string id, bool right, System.Drawing.Point screen)
        {
            if (id == "panic") { if (!right) TogglePanic(); return; }
            if (OnTopKeyPro400(id, right, screen)) return;   // 4.0 finale
            if (id == "auto") { var am = NewMenu(); am.ShowCheckMargin = true; am.ShowImageMargin = false; AddAutomationMenu400(am.Items); if (am.Items.Count == 1 && am.Items[0] is ToolStripMenuItem ti) { var arr = new ToolStripItem[ti.DropDownItems.Count]; ti.DropDownItems.CopyTo(arr, 0); am.Items.Clear(); am.Items.AddRange(arr); } am.Show(screen); return; }
            if (id == "focus") { if (right || _focusRun) ShowFocusMenu(screen); else ToggleFocus(); return; }
            if (id.StartsWith("user:") && int.TryParse(id.Substring(5), out int ui))
            {
                if (right) { ShowTopKeyEditMenu400(ui, screen); return; }
                var k = _settings.TopKeys != null && ui < _settings.TopKeys.Count ? _settings.TopKeys[ui] : null;
                if (k != null) RunAction400(k.Action);
                return;
            }
            OnTopKeyExtern400(id, right, screen);
        }

        // ================================================================ Tastenkuerzel

        private void AddHotkeyActions400(List<HotkeyAction> list)
        {
            if (_settings.PanicOn) list.Add(new HotkeyAction("panic", "Panik: alles stumm / wie vorher", "Allgemein"));
            if (_settings.FocusOn) list.Add(new HotkeyAction("focus", "Fokus-Timer starten/stoppen", "Allgemein"));
            AddHotkeyActionsWindows400(list);
            AddHotkeyActionsMore400(list);
            AddHotkeyActionsPro400(list);   // 4.0 finale
            AddHotkeyActionsFinal400(list);
            AddHotkeyActions430(list);   // 4.3: Ducking halten
        }

        private bool ExecuteHotkey400(string action)
        {
            switch (action)
            {
                case "panic": TogglePanic(); return true;
                case "focus": ToggleFocus(); return true;
            }
            if (ExecuteHotkeyWindows400(action)) return true;
            if (ExecuteHotkey430(action)) return true;   // 4.3
            if (ExecuteHotkeyPro400(action) || ExecuteHotkeyFinal400(action)) return true;   // 4.0 finale
            return ExecuteHotkeyMore400(action);
        }

        /// <summary>Hinweis vorne in der Statuszeile (oder null).</summary>
        private string StatusNote400(out bool warn)
        {
            warn = false;
            var parts = new List<string>();
            if (_cloudNote != null) { parts.Add(_cloudNote); warn = true; }
            if (PanicActive) { parts.Add("PANIK – alles stumm"); warn = true; }
            if (AutoRecording) { parts.Add("● Automation nimmt auf " + FormatSec((float)_autoClock.Elapsed.TotalSeconds)); warn = true; }
            else if (AutoPlaying) parts.Add("▶ Automation: " + _autoPlay.Name);
            if (_focusRun) parts.Add(_focusPhase == 0 ? "Fokus" : "Pause");
            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }

        /// <summary>Nur Vorschau: Beispiel-Inhalte fuer die 4.0-Bilder.</summary>
        private static void DemoFeatures400(MixerSettings s)
        {
            if (DemoOn("PEQWIN") || DemoOn("PEQDEMO"))
            {
                s.PeqOn = true;
                if (s.Channels.Count > 3)
                    s.Channels[3].Peq = new List<PeqBand>
                    {
                        new PeqBand { Type = PeqBand.HighPass, Hz = 40, Q = 0.71f },
                        new PeqBand { Type = PeqBand.LowShelf, Hz = 110, Db = 4.5f, Q = 0.71f },
                        new PeqBand { Type = PeqBand.Bell, Hz = 420, Db = -3.5f, Q = 1.4f },
                        new PeqBand { Type = PeqBand.Bell, Hz = 3200, Db = 2.5f, Q = 2f },
                        new PeqBand { Type = PeqBand.HighShelf, Hz = 9000, Db = -2f, Q = 0.71f },
                    };
            }
            if (DemoOn("HPEQ"))
            {
                s.HpEqOn = true;
                if (s.OutputFx == null) s.OutputFx = new List<OutputFxConfig>();
                s.OutputFx.Add(new OutputFxConfig
                {
                    DeviceId = "demo-spk", Name = "Lautsprecher", HpName = "Beispiel-Kopfhörer", HpPreamp = -5.8f,
                    HpBands = new List<PeqBand>
                    {
                        new PeqBand { Type = PeqBand.LowShelf, Hz = 105, Db = 5.5f, Q = 0.7f },
                        new PeqBand { Type = PeqBand.Bell, Hz = 2300, Db = -3.1f, Q = 1.9f },
                        new PeqBand { Type = PeqBand.Bell, Hz = 5800, Db = 4.2f, Q = 3f },
                        new PeqBand { Type = PeqBand.HighShelf, Hz = 10000, Db = -2.5f, Q = 0.7f },
                    },
                });
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_OVERLAYPOS"), out int op)) s.OverlayPos = op;
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_PLAYERXY") is string pxy) { var pp = DemoXY("PLAYERXY", System.Drawing.Point.Empty); s.PlayerX = pp.X; s.PlayerY = pp.Y; }
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_STRIPHIDE") is string sh && sh.Length > 0) { s.StripCustomOn = true; s.StripHide = sh.Split(',').ToList(); }
            if (int.TryParse(Environment.GetEnvironmentVariable("MISCHPULT_DEMO_CHANNELS"), out int nch) && nch > s.Channels.Count)
                for (int i = s.Channels.Count; i < nch; i++) { var cc = ChannelConfig.Create("KANAL " + (i + 1), MixerSettings.Palette[i % MixerSettings.Palette.Length]); cc.Id = "demo-x" + i; s.Channels.Add(cc); }
            if (DemoOn("TOPKEYSON"))
                s.TopKeys = new List<TopKeyConfig> { new TopKeyConfig { Label = "LOUD", Action = "loud.toggle" }, new TopKeyConfig { Label = "DIM", Action = "dim.toggle" } };
            if (DemoOn("HEARDEMO"))
            {
                s.HearEqOn = true;
                var f = HearProfile.Freqs;
                Func<int, double> ath = i => Audio.HearingEq.Ath(f[i]) - 70;
                s.Hear = new HearProfile
                {
                    Left = Enumerable.Range(0, f.Length).Select(i => (float)(ath(i) + (i >= 6 ? 12 + (i - 6) * 3 : 0))).ToArray(),
                    Right = Enumerable.Range(0, f.Length).Select(i => (float)(ath(i) + (i >= 7 ? 8 : 0))).ToArray(),
                    DeviceId = "demo-spk", DeviceName = "Lautsprecher", Date = "2026-10-22",
                };
            }
        }

        private static System.Drawing.Point DemoXY(string name, System.Drawing.Point def)
        {
            var v = Environment.GetEnvironmentVariable("MISCHPULT_DEMO_" + name)?.Split(',');
            return v != null && v.Length == 2 && int.TryParse(v[0], out int x) && int.TryParse(v[1], out int y) ? new System.Drawing.Point(x, y) : def;
        }

        /// <summary>Nur Vorschau: 4.0-Fenster gleich zeigen.</summary>
        private void DemoShown400()
        {
            if (DemoOn("PEQWIN") && _settings.Channels.Count > 3) DemoLater(() => ShowChannelPeq(_settings.Channels[3]));
            if (DemoOn("PEQOUT")) DemoLater(() => ShowOutputPeq("demo-spk", "Lautsprecher"));
            if (DemoOn("HEARWIN")) DemoLater(() => { ShowHearTest(); if (_settings.Hear != null) _hearWin?.DemoResult(_settings.Hear); });
            if (DemoOn("HEARWIN0")) DemoLater(ShowHearTest);
            if (DemoOn("ROOMWIN")) DemoLater(() => { _settings.RoomOn = true; ShowRoom400(); _room.Location = DemoXY("ROOMXY", new System.Drawing.Point(1240, 60)); });
            if (DemoOn("DOCTORWIN")) DemoLater(() => { _settings.DoctorOn = true; ShowDoctor400(); _doctorWin.Location = new System.Drawing.Point(1080, 60); });
            if (DemoOn("STATSWIN")) DemoLater(() =>
            {
                _settings.StatsOn = true;
                _settings.Stats = new StatsData { Since = "01.10.2026", Starts = 38, VolSum = 21.6, VolCount = 40, MaxLufs = -9.4f, MaxLufsWhen = "19.10. 21:14",
                    ChannelSec = new Dictionary<string, int> { ["SPIEL"] = 61200, ["MUSIK"] = 48100, ["CHAT"] = 30500, ["BROWSER"] = 12800, ["SYSTEM"] = 2600 },
                    SceneUse = new Dictionary<string, int> { ["Zocken"] = 41, ["Musik"] = 23, ["Nacht"] = 12, ["Film"] = 5 },
                    DaySec = Enumerable.Range(0, 14).ToDictionary(k => DateTime.Today.AddDays(-k).ToString("yyyy-MM-dd"), k => 1800 + (k * 2711 % 9000)) };
                ShowStats400(); _statsWin.Location = new System.Drawing.Point(1100, 60);
            });
            if (DemoOn("TIMELINEWIN")) DemoLater(() =>
            {
                _settings.TimelineOn = true;
                var rnd = new Random(3);
                _timeline.Start = DateTime.Now.AddSeconds(-1900);
                foreach (var c in _settings.Channels.Take(5))
                {
                    var lane = new SoundTimeline.Lane { Id = c.Id, Name = c.Name, Color = System.Drawing.Color.FromArgb(255, System.Drawing.Color.FromArgb(c.Color)) };
                    bool on = rnd.Next(2) == 0;
                    for (int s = 0; s < 1900; s++) { if (rnd.Next(120) == 0) on = !on; lane.On[s % SoundTimeline.Seconds] = on; }
                    _timeline.Lanes[c.Id] = lane;
                }
                _timeline.Events.Add(new KeyValuePair<DateTime, string>(DateTime.Now.AddMinutes(-22), "Szene Zocken"));
                _timeline.Events.Add(new KeyValuePair<DateTime, string>(DateTime.Now.AddMinutes(-6), "Szene Musik"));
                ShowTimeline400(); _timelineWin.Location = DemoXY("TIMELINEXY", new System.Drawing.Point(900, 520));
            });
            if (DemoOn("CHANTITLEON")) DemoLater(() => { var t = new[] { null, "Elden Ring", "Discord – #allgemein", "Neon Harbor – Midnight Drive", "Mix-Tutorial: Kompressor einstellen – YouTube" }; for (int i = 0; i < _channelStrips.Count && i < t.Length; i++) _channelStrips[i].SetWinTitle(t[i]); });
            if (DemoOn("CHANWIN") && _settings.Channels.Count > 1) DemoLater(() => { _settings.ChanWinOn = true; OpenChanWin400(_settings.Channels[1], new System.Drawing.Point(1450, 200)); });
            if (Environment.GetEnvironmentVariable("MISCHPULT_DEMO_FIND") is string fq && fq.Length > 0) DemoLater(() => { _settings.SearchOn = true; OpenSearch400(); _searchBox.Text = fq; });
        }
    }
}
