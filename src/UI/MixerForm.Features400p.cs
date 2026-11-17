using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 (finale): Profi-Ton – Kompressor-Fenster, De-Esser, Waerme, Rauschsperre, Brumm-Entferner, Polaritaet, Punch,
    /// Effekt-Reihenfolge, dynamischer und Mitte/Seite-EQ, Frequenz-Ducking, Ducking-Regeln, Auto-Mix, Hall-Bus,
    /// Crossfader, Mute-Gruppen, Fader-Ebene Stream, Stream-Limiter, Signalgenerator. Alles einzeln schaltbar, Standard aus.
    /// </summary>
    internal sealed partial class MixerForm
    {
        private static readonly CultureInfo DeP = CultureInfo.GetCultureInfo("de-DE");

        // ================================================================ Klang des Kanals fuer den Mischer

        private ChannelPro ProFor(ChannelConfig c)
        {
            if (c == null) return null;
            var s = _settings;
            CompSpec comp = s.ProCompOn && c.CompOn && c.Comp400 != null ? new CompSpec(c.Comp400.Thr, c.Comp400.Ratio, c.Comp400.Att, c.Comp400.Rel, c.Comp400.Makeup, c.Comp400.Knee) : null;
            IEnumerable<DynBand> dyn = s.DynEqOn && s.PeqOn && !c.PeqBypass && c.Peq != null
                ? c.Peq.Where(b => b != null && !b.Off && b.IsDyn && Math.Abs(b.Db) > 0.05f).Select(b => new DynBand(b.Hz, b.Db, b.QNow, b.DynThrNow)) : null;
            bool armed = DuckEqArmed400(c);
            var p = new ChannelPro(comp,
                s.DeEssOn ? c.DeEss : 0,
                s.WarmOn ? c.SatType : 0, MixerSettings.SatAmtOf(c.SatAmt),
                s.NoiseGateOn ? c.GateDb : 0f,
                s.HumOn ? c.HumHz : 0,
                s.InvertOn && c.Invert,
                s.PunchOn ? c.Punch : 0,
                s.HallBusOn ? c.HallSend / 100f : 0f,
                s.FxOrderOn ? c.FxOrder : null,
                armed, armed ? DuckEqDb400(c) : 0f, dyn);
            return p.IsNeutral ? null : p;
        }

        /// <summary>Frequenz-Ducking moeglich (Kanal ist Ziel) – dann bleibt er auf dem EQ-Weg, auch wenn gerade nicht abgesenkt wird.</summary>
        private bool DuckEqArmed400(ChannelConfig c)
        {
            if (_settings.DuckFreqOn && _settings.DuckOn && _settings.DuckTargets.Contains(c.Id) && !_settings.IsDuckTrigger(c.Id)) return true;
            if (_settings.DuckRulesOn && _settings.DuckRules != null)
                foreach (var r in _settings.DuckRules) if (!r.Off && r.Freq && r.Targets != null && r.Targets.Contains(c.Id) && r.Trigger != c.Id) return true;
            return false;
        }

        private float DuckEqDb400(ChannelConfig c)
        {
            float db = 0f;
            if (_settings.DuckFreqOn && _duckApplied < 0 && _settings.DuckTargets.Contains(c.Id) && !_settings.IsDuckTrigger(c.Id)) db = Math.Min(db, _duckApplied * 1.25f);
            if (_ruleFq.TryGetValue(c.Id, out float r)) db = Math.Min(db, r * 1.25f);
            return db;
        }

        /// <summary>Weitere Absenkungen (dB): Ducking-Regeln, Auto-Mix, Crossfader.</summary>
        private float ExtraDbPro400(ChannelConfig c)
        {
            float db = 0f;
            if (_ruleBb.TryGetValue(c.Id, out float r)) db += r;
            if (_autoMixApplied.TryGetValue(c.Id, out float a)) db += a;
            db += CrossDb400(c);
            return db;
        }

        /// <summary>Fuer die GR-Anzeige: Ducking-Regeln und Auto-Mix (positiv).</summary>
        private float ProDuckGr400(ChannelConfig c)
        {
            float db = 0f;
            if (_ruleBb.TryGetValue(c.Id, out float r)) db -= r;
            if (_autoMixApplied.TryGetValue(c.Id, out float a)) db -= a;
            return Math.Max(0f, db);
        }

        public bool StreamLimitWanted => _settings.StreamLimitOn;

        private bool MuteGroupHit400(ChannelConfig c) =>
            _settings.MuteGroupsOn && _settings.MuteGroups != null && _settings.MuteGroups.Any(g => g.On && g.Members != null && g.Members.Contains(c.Id));

        // ================================================================ Einstellungen anwenden

        private void ApplyPro400(MixerSettings s)
        {
            Pro400.Hall = s.HallBusOn ? new HallSpec(s.HallPresetNow, s.HallReturnDb) : null;
            Pro400.StreamLimitOn = s.StreamLimitOn;
            PeqWindow.MsOn = s.MsEqOn;
            PeqWindow.DynOn = s.DynEqOn;
            bool layer = s.StreamLayerOn && _streamLayer;
            if (ChannelStrip.StreamLayer != layer) SetStreamLayer400(layer, false);
            if (!s.GenOn && _genRun) { _genRun = false; }
            ApplyGen400();
        }

        // ================================================================ Takt

        private float _proT;
        private readonly Dictionary<string, float> _ruleBb = new Dictionary<string, float>(), _ruleFq = new Dictionary<string, float>();
        private float[] _ruleCur = new float[0];
        private double[] _ruleHold = new double[0];
        private readonly Dictionary<string, float> _autoMixApplied = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _amLvl = new Dictionary<string, float>(), _amCur = new Dictionary<string, float>();
        private float _streamGrShown;
        private float _compFeedT;

        private void TickPro400(float dt)
        {
            if (dt > 0.2f) dt = 0.2f;
            _proT += dt;
            bool push = TickDuckRules400(dt);
            push |= TickAutoMix400(dt);
            if (push) PushPolicy();

            if (_settings.StreamLimitOn)
            {
                float gr = Pro400.TakeStreamGr();
                _streamGrShown = gr >= _streamGrShown ? gr : Math.Max(gr, _streamGrShown - 20f * dt);
                StreamStrip.LimitGr = _streamGrShown;
            }
            else StreamStrip.LimitGr = -1f;

            _compFeedT += dt;
            if (_compWins.Count > 0 && _compFeedT >= 0.033f)
            {
                _compFeedT = 0f;
                foreach (var kv in _compWins.ToList())
                {
                    if (kv.Value.IsDisposed) { _compWins.Remove(kv.Key); continue; }
                    var c = ChannelById(kv.Key);
                    if (c == null) { try { kv.Value.Close(); } catch { } continue; }
                    float gr = _snap.CompReduction != null && _snap.CompReduction.TryGetValue(c.Id, out float g) && !float.IsNaN(g) ? Math.Max(0f, g) : 0f;
                    float pre = _channelPeak.TryGetValue(c.Id, out var pk) ? Math.Max(pk[2], pk[3]) : 0f;
                    float preDb = MeterBallistics.ToDb(pre);
                    var cc = c.Comp400 ?? CompConfig.Defaults();
                    kv.Value.Feed(gr, preDb <= -99f ? -100f : preDb - cc.Makeup + gr);
                }
            }
        }

        private bool TickDuckRules400(float dt)
        {
            var rules = _settings.DuckRulesOn ? _settings.DuckRules : null;
            if (rules == null || rules.Count == 0)
            {
                bool had = _ruleBb.Count > 0 || _ruleFq.Count > 0;
                _ruleBb.Clear(); _ruleFq.Clear();
                if (had) RefreshDuckLeds400();
                return had;
            }
            if (_ruleCur.Length != rules.Count) { Array.Resize(ref _ruleCur, rules.Count); Array.Resize(ref _ruleHold, rules.Count); }
            if (_ruleTalk423.Length != rules.Count) Array.Resize(ref _ruleTalk423, rules.Count);
            var bb = new Dictionary<string, float>(); var fq = new Dictionary<string, float>();
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                float target = 0f;
                float lvl = DuckLevelDb(r.Trigger);   // 4.2.3: auch das Mikrofon
                if (!r.Off && r.Targets != null && r.Targets.Count > 0 && lvl > -999f)
                {
                    if (_dimOn && r.Trigger != MixerSettings.DuckMicId) lvl += DimDb;
                    if (TalkGate423(ref _ruleTalk423[i], lvl > r.ThrNow, dt)) _ruleHold[i] = _proT + 0.6;   // 4.2.3: kurze Geraeusche ignorieren
                    if (_proT < _ruleHold[i]) target = r.DbNow;
                }
                else _ruleTalk423[i] = 0f;
                float cur = _ruleCur[i];
                DuckRates423(r.DbNow, out float down, out float up);   // 4.2.3: Uebergang einstellbar
                cur = cur > target ? Math.Max(target, cur - down * dt) : Math.Min(target, cur + up * dt);
                _ruleCur[i] = cur;
                float q = (float)Math.Round(cur * 2) / 2f;
                if (q >= 0f || r.Targets == null) continue;
                foreach (var t in r.Targets)
                {
                    if (t == r.Trigger) continue;
                    var d = r.Freq ? fq : bb;
                    d[t] = d.TryGetValue(t, out float o) ? Math.Min(o, q) : q;
                }
            }
            bool changed = !SameDict(bb, _ruleBb) || !SameDict(fq, _ruleFq);
            if (!changed) return false;
            _ruleBb.Clear(); foreach (var kv in bb) _ruleBb[kv.Key] = kv.Value;
            _ruleFq.Clear(); foreach (var kv in fq) _ruleFq[kv.Key] = kv.Value;
            RefreshDuckLeds400();
            return true;
        }

        private static bool SameDict(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out float v) || v != kv.Value) return false;
            return true;
        }

        /// <summary>DUCK-Leuchten der Zuege: Haupt-Ducking und Regeln.</summary>
        private void RefreshDuckLeds400()
        {
            foreach (var s in _channelStrips)
            {
                var id = s.Config.Id;
                bool main = _duckActive && _settings.DuckTargets.Contains(id) && !_settings.IsDuckTrigger(id);
                bool rule = (_ruleBb.TryGetValue(id, out float a) && a < -0.25f) || (_ruleFq.TryGetValue(id, out float b) && b < -0.25f);
                s.SetDucked(main || rule);
            }
        }

        /// <summary>Auto-Mix (Gain-Sharing): wer spricht, bekommt den Anteil – die anderen werden leiser.</summary>
        private bool TickAutoMix400(float dt)
        {
            var list = _settings.AutoMixOn ? _settings.Channels.Where(c => c.AutoMix && !c.Mute).ToList() : new List<ChannelConfig>();
            if (list.Count < 2)
            {
                bool had = _autoMixApplied.Count > 0;
                _autoMixApplied.Clear(); _amCur.Clear(); _amLvl.Clear();
                return had;
            }
            double sum = 0; float loudest = -120f;
            foreach (var c in list)
            {
                float pre = _channelPeak.TryGetValue(c.Id, out var pk) ? Math.Max(pk[2], pk[3]) : 0f;
                float db = Math.Max(-90f, MeterBallistics.ToDb(pre));
                _amLvl.TryGetValue(c.Id, out float l);
                if (!_amLvl.ContainsKey(c.Id)) l = -90f;
                l = db > l ? l + (db - l) * Math.Min(1f, dt / 0.04f) : l + (db - l) * Math.Min(1f, dt / 0.35f);
                _amLvl[c.Id] = l;
                sum += Math.Pow(10, l / 10.0);
                loudest = Math.Max(loudest, l);
            }
            bool changed = false;
            foreach (var c in list)
            {
                _amCur.TryGetValue(c.Id, out float cur);
                float target = cur;
                if (loudest > -55f) target = (float)Math.Max(-15.0, Math.Min(0.0, 10 * Math.Log10(Math.Pow(10, _amLvl[c.Id] / 10.0) / Math.Max(1e-12, sum))));
                cur = cur > target ? Math.Max(target, cur - 40f * dt) : Math.Min(target, cur + 25f * dt);
                _amCur[c.Id] = cur;
                float q = (float)Math.Round(cur * 2) / 2f;
                _autoMixApplied.TryGetValue(c.Id, out float old);
                if (q != old) { if (q >= 0f) _autoMixApplied.Remove(c.Id); else _autoMixApplied[c.Id] = q; changed = true; }
            }
            foreach (var k in _autoMixApplied.Keys.ToList()) if (!list.Any(c => c.Id == k)) { _autoMixApplied.Remove(k); changed = true; }
            return changed;
        }

        // ================================================================ Kanal-Menue

        private bool AnyProChannel400 => _settings.ProCompOn || _settings.DeEssOn || _settings.WarmOn || _settings.NoiseGateOn || _settings.HumOn || _settings.InvertOn
                                         || _settings.PunchOn || _settings.HallBusOn || _settings.FxOrderOn || _settings.AutoMixOn;

        private string ProState400(ChannelConfig c)
        {
            var l = new List<string>();
            if (_settings.ProCompOn && c.Comp400 != null) l.Add("Komp");
            if (_settings.DeEssOn && c.DeEss > 0) l.Add("De-Ess");
            if (_settings.WarmOn && c.SatType > 0) l.Add(ChannelPro.SatNames[c.SatType]);
            if (_settings.NoiseGateOn && c.GateDb < 0) l.Add("Sperre");
            if (_settings.HumOn && c.HumHz > 0) l.Add(c.HumHz + " Hz");
            if (_settings.InvertOn && c.Invert) l.Add("Ø");
            if (_settings.PunchOn && c.Punch != 0) l.Add("Punch " + (c.Punch > 0 ? "+" : "") + c.Punch);
            if (_settings.HallBusOn && c.HallSend > 0) l.Add("Hall " + c.HallSend + " %");
            if (_settings.FxOrderOn && c.FxOrder != null) l.Add("eigene Reihenfolge");
            if (_settings.AutoMixOn && c.AutoMix) l.Add("Auto-Mix");
            return l.Count == 0 ? "aus" : string.Join(", ", l);
        }

        private void ProEdited400(ChannelConfig c) { OnChannelEdited(c); ScheduleSave(); }

        private void AddChannelPro400(ToolStripItemCollection items, ChannelConfig c)
        {
            if (!AnyProChannel400) return;
            var sub = Sub("Profi-Ton: " + ProState400(c));
            var it = sub.DropDownItems;
            if (_settings.ProCompOn)
                it.Add(new ToolStripMenuItem("Kompressor-Fenster …", null, (s, e) => ShowComp400(c)));
            if (_settings.DeEssOn)
            {
                var d = Sub("De-Esser (Zischlaute): " + ChannelPro.DeEssNames[c.DeEss]);
                for (int i = 0; i < 4; i++) { int v = i; d.DropDownItems.Add(new ToolStripMenuItem(ChannelPro.DeEssNames[i], null, (s, e) => { c.DeEss = v; ProEdited400(c); }) { Checked = c.DeEss == i }); }
                it.Add(d);
            }
            if (_settings.WarmOn)
            {
                var w = Sub("Analoge Wärme: " + (c.SatType == 0 ? "aus" : ChannelPro.SatNames[c.SatType] + ", " + MixerSettings.SatAmtNames[c.SatAmt].ToLowerInvariant()));
                for (int i = 0; i < 4; i++) { int v = i; w.DropDownItems.Add(new ToolStripMenuItem(ChannelPro.SatNames[i], null, (s, e) => { c.SatType = v; ProEdited400(c); }) { Checked = c.SatType == i }); }
                w.DropDownItems.Add(new ToolStripSeparator());
                w.DropDownItems.Add(Header("Stärke"));
                foreach (int i in new[] { 1, 0, 2 }) { int v = i; w.DropDownItems.Add(new ToolStripMenuItem(MixerSettings.SatAmtNames[i], null, (s, e) => { c.SatAmt = v; ProEdited400(c); }) { Checked = c.SatAmt == i, Enabled = c.SatType > 0 }); }
                it.Add(w);
            }
            if (_settings.NoiseGateOn)
            {
                var g = Sub("Rauschsperre: " + (c.GateDb < 0 ? "ab " + c.GateDb + " dB" : "aus"));
                g.DropDownItems.Add(new ToolStripMenuItem("Aus", null, (s, e) => { c.GateDb = 0; ProEdited400(c); }) { Checked = c.GateDb == 0 });
                foreach (var v in MixerSettings.GateValues) { int vv = v; g.DropDownItems.Add(new ToolStripMenuItem("Leiser als " + v + " dB = still", null, (s, e) => { c.GateDb = vv; ProEdited400(c); }) { Checked = c.GateDb == v }); }
                g.DropDownItems.Add(Hint("Tipp: so tief wie möglich – nur das Rauschen soll weg."));
                it.Add(g);
            }
            if (_settings.HumOn)
            {
                var h = Sub("Brumm-Entferner: " + (c.HumHz > 0 ? c.HumHz + " Hz" : "aus"));
                foreach (var v in new[] { 0, 50, 60 }) { int vv = v; h.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : v + " Hz" + (v == 50 ? " (Europa)" : " (USA)"), null, (s, e) => { c.HumHz = vv; ProEdited400(c); }) { Checked = c.HumHz == v }); }
                it.Add(h);
            }
            if (_settings.InvertOn)
                it.Add(new ToolStripMenuItem("Polarität drehen (Ø)", null, (s, e) => { c.Invert = !c.Invert; ProEdited400(c); }) { Checked = c.Invert });
            if (_settings.PunchOn)
            {
                var p = Sub("Punch (Anschläge): " + (c.Punch == 0 ? "neutral" : (c.Punch > 0 ? "+" : "") + c.Punch));
                string[] names = { "Deutlich weicher", "Etwas weicher", "Neutral", "Etwas knackiger", "Deutlich knackiger" };
                for (int i = -2; i <= 2; i++) { int v = i; p.DropDownItems.Add(new ToolStripMenuItem(names[i + 2], null, (s, e) => { c.Punch = v; ProEdited400(c); }) { Checked = c.Punch == i }); }
                it.Add(p);
            }
            if (_settings.HallBusOn)
            {
                var hs = Sub("Hall-SEND: " + (c.HallSend == 0 ? "aus" : c.HallSend + " %"));
                foreach (var v in MixerSettings.HallSendValues) { int vv = v; hs.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "Aus" : v + " %", null, (s, e) => { c.HallSend = vv; ProEdited400(c); }) { Checked = c.HallSend == v }); }
                it.Add(hs);
            }
            if (_settings.FxOrderOn)
            {
                var o = Sub("Effekt-Reihenfolge");
                foreach (var ord in MixerSettings.OrderPresets)
                {
                    string oo = ord;
                    o.DropDownItems.Add(new ToolStripMenuItem(ChannelPro.OrderLabel(ord) + (ord == ChannelPro.DefaultOrder ? "  (Standard)" : ""), null, (s, e) => { c.FxOrder = ChannelPro.CleanOrder(oo); ProEdited400(c); })
                        { Checked = (c.FxOrder ?? ChannelPro.DefaultOrder) == ord });
                }
                if (c.FxOrder != null && Array.IndexOf(MixerSettings.OrderPresets, c.FxOrder) < 0)
                    o.DropDownItems.Add(new ToolStripMenuItem(ChannelPro.OrderLabel(c.FxOrder), null, (s, e) => { }) { Checked = true });
                it.Add(o);
            }
            if (_settings.AutoMixOn)
                it.Add(new ToolStripMenuItem("Im Auto-Mix (Sprache)", null, (s, e) => { c.AutoMix = !c.AutoMix; ProEdited400(c); }) { Checked = c.AutoMix });
            items.Add(sub);
        }

        // ================================================================ 231 Kompressor-Fenster

        private readonly Dictionary<string, CompWindow> _compWins = new Dictionary<string, CompWindow>();

        private void ShowComp400(ChannelConfig c)
        {
            if (c == null || !_settings.ProCompOn) return;
            if (_compWins.TryGetValue(c.Id, out var w) && !w.IsDisposed) { w.Activate(); return; }
            w = new CompWindow(c.Name, Color.FromArgb(255, Color.FromArgb(c.Color)))
            {
                Key = c.Id,
                Get = () => c.Comp400,
                Set = (v, final) => { c.Comp400 = v; PushPolicy(); if (final) ScheduleSave(); },
                On = () => c.CompOn,
                SetOn = v => { c.CompOn = v; OnChannelEdited(c); _channelStrips.FirstOrDefault(s => s.Config == c)?.InvalidateChrome(); },
            };
            w.FormClosed += (s, e) => _compWins.Remove(c.Id);
            _compWins[c.Id] = w;
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            w.Show(this);
        }

        // ================================================================ 233 Ducking-Regeln

        private string RuleLabel400(DuckRule r)
        {
            string trig = DuckTriggerName(r.Trigger);
            var tg = (r.Targets ?? new List<string>()).Select(t => ChannelById(t)?.Name).Where(n => n != null).ToList();
            return (r.Off ? "(aus) " : "") + trig + " → " + (tg.Count == 0 ? "keine Ziele" : string.Join(", ", tg)) + ": " + r.DbNow + " dB" + (r.Freq ? ", Sprachbereich" : "");
        }

        private static void KeepOpen(ToolStripMenuItem sub) =>
            sub.DropDown.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };

        private void ShowDuckRules400(Rectangle at)
        {
            var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
            var rules = _settings.DuckRules ?? new List<DuckRule>();
            if (rules.Count == 0) m.Items.Add(Hint("Noch keine Regel."));
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                var sub = Sub(RuleLabel400(r));
                var trig = Sub("Auslöser: " + DuckTriggerName(r.Trigger));
                // 4.2.3: dein Mikrofon als Ausloeser einer Regel
                trig.DropDownItems.Add(new ToolStripMenuItem("MIKRO (du sprichst)", null, (s, e) => { r.Trigger = MixerSettings.DuckMicId; RulesChanged400(); }) { Checked = r.Trigger == MixerSettings.DuckMicId });
                trig.DropDownItems.Add(new ToolStripMenuItem("SOUNDBOARD (ein Sound spielt)", null, (s, e) => { r.Trigger = MixerSettings.DuckSbId; RulesChanged400(); }) { Checked = r.Trigger == MixerSettings.DuckSbId });   // 4.3
                trig.DropDownItems.Add(new ToolStripSeparator());
                foreach (var ch in _settings.Channels)
                {
                    var cc = ch;
                    trig.DropDownItems.Add(new ToolStripMenuItem(cc.Name, null, (s, e) => { r.Trigger = cc.Id; r.Targets?.Remove(cc.Id); RulesChanged400(); }) { Checked = r.Trigger == cc.Id });
                }
                sub.DropDownItems.Add(trig);
                var tg = Sub("Ziele (" + (r.Targets?.Count ?? 0) + ")");
                KeepOpen(tg);
                foreach (var ch in _settings.Channels.Where(x => x.Id != r.Trigger))
                {
                    var cc = ch;
                    var item = new ToolStripMenuItem(cc.Name) { Checked = r.Targets != null && r.Targets.Contains(cc.Id) };
                    item.Click += (s, e) =>
                    {
                        if (r.Targets == null) r.Targets = new List<string>();
                        if (r.Targets.Contains(cc.Id)) r.Targets.Remove(cc.Id); else r.Targets.Add(cc.Id);
                        item.Checked = r.Targets.Contains(cc.Id);
                        RulesChanged400();
                    };
                    tg.DropDownItems.Add(item);
                }
                sub.DropDownItems.Add(tg);
                var db = Sub("Absenkung: " + r.DbNow + " dB");
                foreach (var v in MixerSettings.DuckRuleDbValues) { int vv = v; db.DropDownItems.Add(new ToolStripMenuItem(v + " dB", null, (s, e) => { r.Db = vv; RulesChanged400(); }) { Checked = r.DbNow == v }); }
                sub.DropDownItems.Add(db);
                var th = Sub("Auslösen ab: " + r.ThrNow + " dB");
                foreach (var v in MixerSettings.DuckRuleThrValues) { int vv = v; th.DropDownItems.Add(new ToolStripMenuItem(v + " dB", null, (s, e) => { r.Thr = vv; RulesChanged400(); }) { Checked = r.ThrNow == v }); }
                sub.DropDownItems.Add(th);
                sub.DropDownItems.Add(new ToolStripMenuItem("Nur den Sprachbereich absenken", null, (s, e) => { r.Freq = !r.Freq; RulesChanged400(); }) { Checked = r.Freq });
                sub.DropDownItems.Add(new ToolStripMenuItem("Regel ausschalten", null, (s, e) => { r.Off = !r.Off; RulesChanged400(); }) { Checked = r.Off });
                sub.DropDownItems.Add(new ToolStripSeparator());
                sub.DropDownItems.Add(new ToolStripMenuItem("Regel löschen", null, (s, e) => { _settings.DuckRules?.Remove(r); if (_settings.DuckRules?.Count == 0) _settings.DuckRules = null; RulesChanged400(); }));
                m.Items.Add(sub);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Neue Regel", null, (s, e) =>
            {
                if (_settings.DuckRules == null) _settings.DuckRules = new List<DuckRule>();
                if (_settings.DuckRules.Count >= 12) return;
                var trig = ChannelById(_settings.DuckTrigger) ?? _settings.Channels.FirstOrDefault();
                if (trig == null) return;
                _settings.DuckRules.Add(new DuckRule { Trigger = trig.Id, Targets = _settings.Channels.Where(c => c.Id != trig.Id && !c.IsCatchAll).Take(1).Select(c => c.Id).ToList() });
                RulesChanged400();
                BeginInvoke(new Action(() => ShowDuckRules400(at)));
            }) { Enabled = rules.Count < 12 });
            ShowMenuAt(m, at);
        }

        private void RulesChanged400() { _ruleCur = new float[0]; _ruleHold = new double[0]; _ruleTalk423 = new float[0]; PushPolicy(); ScheduleSave(); }

        // ================================================================ 257 Crossfader

        private CrossfaderWindow _xfWin;

        private float CrossDb400(ChannelConfig c)
        {
            if (!_settings.CrossOn || _settings.CrossA == null || _settings.CrossB == null || _settings.CrossA == _settings.CrossB) return 0f;
            bool a = c.Id == _settings.CrossA, b = c.Id == _settings.CrossB;
            if (!a && !b) return 0f;
            float g = CrossGain400(_settings.CrossPosNow, _settings.CrossCurve, b);
            return g <= 1e-5f ? -96f : 20f * (float)Math.Log10(g);
        }

        /// <summary>Verstaerkung eines Crossfader-Kanals (pos 0 = ganz A, 1 = ganz B).</summary>
        internal static float CrossGain400(float pos, int curve, bool isB)
        {
            float x = isB ? pos : 1f - pos;   // 1 = ganz auf meiner Seite
            x = Math.Max(0f, Math.Min(1f, x));
            switch (curve)
            {
                case 1: return Math.Min(1f, x * 2f);
                case 2: return x;
                default: return (float)Math.Sin(x * Math.PI / 2);
            }
        }

        private void SetCross400(float pos, bool final)
        {
            _settings.CrossPos = Math.Abs(pos - 0.5f) < 0.001f ? (float?)null : Math.Max(0f, Math.Min(1f, pos));
            PushPolicy();
            if (final) ScheduleSave();
            _xfWin?.Invalidate();
        }

        private void ShowCross400()
        {
            if (!_settings.CrossOn) return;
            if (_xfWin != null && !_xfWin.IsDisposed) { _xfWin.Activate(); return; }
            var w = new CrossfaderWindow
            {
                NameA = () => ChannelById(_settings.CrossA)?.Name,
                NameB = () => ChannelById(_settings.CrossB)?.Name,
                ColorA = () => ChannelById(_settings.CrossA) is ChannelConfig a ? Color.FromArgb(255, Color.FromArgb(a.Color)) : Theme.TextFaint,
                ColorB = () => ChannelById(_settings.CrossB) is ChannelConfig b ? Color.FromArgb(255, Color.FromArgb(b.Color)) : Theme.TextFaint,
                Pos = () => _settings.CrossPosNow,
                SetPos = SetCross400,
                TopMost = true,
            };
            w.Pick += (isB, pt) =>
            {
                var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
                m.Items.Add(Header(isB ? "Kanal B" : "Kanal A"));
                foreach (var ch in _settings.Channels)
                {
                    var cc = ch;
                    m.Items.Add(new ToolStripMenuItem(cc.Name, null, (s, e) =>
                    {
                        if (isB) { _settings.CrossB = cc.Id; if (_settings.CrossA == cc.Id) _settings.CrossA = null; }
                        else { _settings.CrossA = cc.Id; if (_settings.CrossB == cc.Id) _settings.CrossB = null; }
                        PushPolicy(); ScheduleSave(); _xfWin?.Invalidate();
                    }) { Checked = (isB ? _settings.CrossB : _settings.CrossA) == cc.Id });
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Keiner", null, (s, e) => { if (isB) _settings.CrossB = null; else _settings.CrossA = null; PushPolicy(); ScheduleSave(); _xfWin?.Invalidate(); }));
                m.Show(pt);
            };
            w.Moved += win => { _settings.CrossX = win.Left; _settings.CrossY = win.Top; ScheduleSave(); };
            w.FormClosed += (s, e) => { if (_xfWin == w) _xfWin = null; RefreshTopKeys400(); };
            var wa = Screen.FromControl(this).WorkingArea;
            var loc = _settings.CrossX.HasValue && _settings.CrossY.HasValue ? new Point(_settings.CrossX.Value, _settings.CrossY.Value) : new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Bottom - w.Height - Theme.Px(40));
            w.Location = OnScreen400(loc, w.Size);
            _xfWin = w;
            w.Show(this);
            RefreshTopKeys400();
        }

        // ================================================================ 258 Mute-Gruppen

        private void ToggleMuteGroup400(int i)
        {
            var l = _settings.MuteGroups;
            if (!_settings.MuteGroupsOn || l == null || i < 0 || i >= l.Count) return;
            l[i].On = !l[i].On;
            PushPolicy(); ScheduleSave(); RefreshTopKeys400();
            ShowOsd("Mute-Gruppe " + l[i].Name, l[i].On ? "stumm" : "wieder an", -1, l[i].On ? Theme.MuteRed : Theme.LedGreen);
            Announce400("Mute-Gruppe " + l[i].Name + (l[i].On ? " stumm" : " an"));
        }

        private void AddMuteGroupItems400(ToolStripItemCollection items, int i)
        {
            var g = _settings.MuteGroups[i];
            items.Add(new ToolStripMenuItem(g.On ? "Wieder an" : "Stumm schalten", null, (s, e) => ToggleMuteGroup400(i)));
            items.Add(new ToolStripMenuItem("Umbenennen …", null, (s, e) => BeginInvoke(new Action(() =>
            {
                using (var dlg = new InputDialog("Mute-Gruppe umbenennen", "Name (auf der Taste höchstens 6 Zeichen):", g.Name, 12))
                    if (dlg.ShowDialog(this) == DialogResult.OK) { g.Name = dlg.Value.Trim(); ScheduleSave(); RefreshTopKeys400(); }
            }))));
            var mem = Sub("Kanäle (" + (g.Members?.Count ?? 0) + ")");
            KeepOpen(mem);
            foreach (var ch in _settings.Channels)
            {
                var cc = ch;
                var item = new ToolStripMenuItem(cc.Name) { Checked = g.Members != null && g.Members.Contains(cc.Id) };
                item.Click += (s, e) =>
                {
                    if (g.Members == null) g.Members = new List<string>();
                    if (g.Members.Contains(cc.Id)) g.Members.Remove(cc.Id); else g.Members.Add(cc.Id);
                    item.Checked = g.Members.Contains(cc.Id);
                    PushPolicy(); ScheduleSave();
                };
                mem.DropDownItems.Add(item);
            }
            items.Add(mem);
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Gruppe löschen", null, (s, e) =>
            {
                _settings.MuteGroups.Remove(g);
                if (_settings.MuteGroups.Count == 0) _settings.MuteGroups = null;
                PushPolicy(); ScheduleSave(); RefreshTopKeys400();
            }));
        }

        private void NewMuteGroup400()
        {
            if (_settings.MuteGroups == null) _settings.MuteGroups = new List<MuteGroupConfig>();
            if (_settings.MuteGroups.Count >= 4) return;
            _settings.MuteGroups.Add(new MuteGroupConfig { Name = "MUTE " + (_settings.MuteGroups.Count + 1), Members = new List<string>() });
            ScheduleSave(); RefreshTopKeys400();
        }

        private void ShowMuteGroups400(Rectangle at)
        {
            var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
            var l = _settings.MuteGroups ?? new List<MuteGroupConfig>();
            if (l.Count == 0) m.Items.Add(Hint("Noch keine Gruppe."));
            for (int i = 0; i < l.Count; i++)
            {
                var sub = Sub(l[i].Name + " – " + (l[i].Members?.Count ?? 0) + " Kanäle" + (l[i].On ? " (stumm)" : ""));
                AddMuteGroupItems400(sub.DropDownItems, i);
                m.Items.Add(sub);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Neue Gruppe", null, (s, e) => { NewMuteGroup400(); BeginInvoke(new Action(() => ShowMuteGroups400(at))); }) { Enabled = l.Count < 4 });
            ShowMenuAt(m, at);
        }

        // ================================================================ 259 Fader-Ebene Stream

        private bool _streamLayer;

        private void SetStreamLayer400(bool on, bool osd)
        {
            if (on && !_settings.StreamLayerOn) on = false;
            if (on && !_settings.StreamOn) { if (osd) ShowOsd("Fader-Ebene", "Stream-Mix ist aus (OPTIONEN → Kanäle & Ausgänge)", -1, Theme.TextDim); on = false; }
            var from = _channelStrips.ToDictionary(s => s, s => s.MotorFrom);
            _streamLayer = on;
            ChannelStrip.StreamLayer = on;
            foreach (var s in _channelStrips) { s.StartMotor(from[s]); s.InvalidateChrome(); }
            RefreshTopKeys400();
            if (osd) ShowOsd("Fader-Ebene", on ? "STREAM – die Fader stellen den Stream-Mix" : "Normal – die Fader stellen deinen Mix", -1, on ? ChannelStrip.StreamColor : Theme.LedGreen);
            Announce400(on ? "Fader-Ebene Stream" : "Fader-Ebene normal");
        }

        // ================================================================ 262 Signalgenerator

        private GeneratorWindow _genWin;
        private bool _genRun;

        private void ApplyGen400()
        {
            var s = _settings;
            Pro400.Gen = s.GenOn && _genRun ? new GenSpec(s.GenKind, s.GenHzNow, s.GenLevelNow, s.GenDevice, s.GenSide) : null;
            _genWin?.Invalidate();
        }

        private string GenDeviceName400()
        {
            if (_settings.GenDevice == null) return "Windows-Standardgerät";
            var d = _snap.Devices.FirstOrDefault(x => string.Equals(x.Id, _settings.GenDevice, StringComparison.OrdinalIgnoreCase));
            return d?.FullName ?? "Gerät nicht angeschlossen";
        }

        private void SetGenRunning400(bool on)
        {
            if (on && !_snap.EqSupported && !_demo) { MessageBox.Show(this, (_snap.EqStatus ?? "Nicht verfügbar") + "\n\nDer Signalgenerator braucht Windows 10 Version 2004 oder neuer.", "Mischpult"); return; }
            _genRun = on && _settings.GenOn;
            ApplyGen400();
            RefreshTopKeys400();
        }

        private void ShowGen400()
        {
            if (!_settings.GenOn) return;
            if (_genWin != null && !_genWin.IsDisposed) { _genWin.Activate(); return; }
            var w = new GeneratorWindow
            {
                Kind = () => _settings.GenKind, SetKind = v => { _settings.GenKind = v; ApplyGen400(); ScheduleSave(); },
                Hz = () => _settings.GenHzNow, SetHz = v => { _settings.GenHz = v; ApplyGen400(); ScheduleSave(); },
                Level = () => _settings.GenLevelNow, SetLevel = v => { _settings.GenLevel = Math.Min(-6, v); ApplyGen400(); ScheduleSave(); },
                Side = () => _settings.GenSide, SetSide = v => { _settings.GenSide = v; ApplyGen400(); ScheduleSave(); },
                DeviceName = GenDeviceName400,
                Running = () => _genRun, SetRunning = SetGenRunning400,
            };
            w.PickDevice += pt =>
            {
                var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
                m.Items.Add(new ToolStripMenuItem("Windows-Standardgerät", null, (s, e) => { _settings.GenDevice = null; ApplyGen400(); ScheduleSave(); }) { Checked = _settings.GenDevice == null });
                foreach (var d in _snap.Devices)
                {
                    var dd = d;
                    m.Items.Add(new ToolStripMenuItem(dd.FullName, null, (s, e) => { _settings.GenDevice = dd.Id; ApplyGen400(); ScheduleSave(); })
                        { Checked = string.Equals(_settings.GenDevice, dd.Id, StringComparison.OrdinalIgnoreCase) });
                }
                m.Show(pt);
            };
            w.FormClosed += (s, e) => { if (_genWin == w) _genWin = null; };
            var wa = Screen.FromControl(this).WorkingArea;
            w.Location = new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + (wa.Height - w.Height) / 2);
            _genWin = w;
            w.Show(this);
        }

        // ================================================================ Kopfleiste und Tastenkuerzel

        private void AddTopKeysPro400(List<TopKey> l)
        {
            if (_settings.MuteGroupsOn && _settings.MuteGroups != null)
                for (int i = 0; i < _settings.MuteGroups.Count; i++)
                {
                    var g = _settings.MuteGroups[i];
                    string t = g.Name.ToUpperInvariant(); if (t.Length > 6) t = t.Substring(0, 6);
                    l.Add(new TopKey { Id = "mg:" + i, Text = t, On = g.On, Lit = Theme.MuteRed, Width = Math.Max(34, 14 + 7 * t.Length), Priority = 2,
                        Tip = "Mute-Gruppe " + g.Name + " (" + (g.Members?.Count ?? 0) + " Kanäle) – " + (g.On ? "stumm. Klick: wieder an" : "Klick: stumm") + "\nRechtsklick: bearbeiten" });
                }
            if (_settings.StreamLayerOn && _settings.StreamOn)
                l.Add(new TopKey { Id = "layer", Text = "STREAM", On = _streamLayer, Lit = ChannelStrip.StreamColor, Width = 60, Priority = 2,
                    Tip = _streamLayer ? "Fader-Ebene STREAM: die Fader stellen den Stream-Mix – Klick: zurück" : "Fader-Ebene umschalten: die Fader stellen dann den Stream-Mix" });
            if (_settings.CrossOn)
                l.Add(new TopKey { Id = "xf", Text = "XF", On = _xfWin != null && !_xfWin.IsDisposed, Lit = Theme.Accent, Width = 32, Priority = 2, Tip = "Crossfader öffnen/schließen" });
            if (_settings.GenOn)
                l.Add(new TopKey { Id = "gen", Text = "GEN", On = _genRun, Lit = Theme.MuteRed, Width = 40, Priority = 1, Tip = _genRun ? "Signalgenerator läuft – Klick: stopp, Rechtsklick: Fenster" : "Signalgenerator – Klick: Fenster" });
        }

        private bool OnTopKeyPro400(string id, bool right, Point screen)
        {
            if (id.StartsWith("mg:") && int.TryParse(id.Substring(3), out int i))
            {
                if (!right) ToggleMuteGroup400(i);
                else if (_settings.MuteGroups != null && i < _settings.MuteGroups.Count)
                {
                    var m = NewMenu(); m.ShowCheckMargin = true; m.ShowImageMargin = false;
                    AddMuteGroupItems400(m.Items, i);
                    m.Items.Add(new ToolStripMenuItem("Neue Gruppe", null, (s, e) => NewMuteGroup400()) { Enabled = _settings.MuteGroups.Count < 4 });
                    m.Show(screen);
                }
                return true;
            }
            switch (id)
            {
                case "layer": SetStreamLayer400(!_streamLayer, true); return true;
                case "xf": if (_xfWin != null && !_xfWin.IsDisposed) _xfWin.Close(); else ShowCross400(); return true;
                case "gen": if (_genRun && !right) SetGenRunning400(false); else ShowGen400(); return true;
            }
            return false;
        }

        private void AddHotkeyActionsPro400(List<HotkeyAction> list)
        {
            if (_settings.StreamLayerOn) list.Add(new HotkeyAction("layer.stream", "Fader-Ebene Stream an/aus", "Mischen"));
            if (_settings.CrossOn)
            {
                list.Add(new HotkeyAction("xf.a", "Crossfader: ganz auf A", "Mischen"));
                list.Add(new HotkeyAction("xf.mid", "Crossfader: Mitte", "Mischen"));
                list.Add(new HotkeyAction("xf.b", "Crossfader: ganz auf B", "Mischen"));
                list.Add(new HotkeyAction("xf.left", "Crossfader: ein Stück Richtung A", "Mischen"));
                list.Add(new HotkeyAction("xf.right", "Crossfader: ein Stück Richtung B", "Mischen"));
            }
            if (_settings.MuteGroupsOn)
                for (int i = 0; i < 4; i++) list.Add(new HotkeyAction("mg." + (i + 1), "Mute-Gruppe " + (i + 1) + " an/aus", "Mischen"));
            if (_settings.GenOn) list.Add(new HotkeyAction("gen", "Signalgenerator starten/stoppen", "Mischen"));
        }

        private bool ExecuteHotkeyPro400(string action)
        {
            switch (action)
            {
                case "layer.stream": if (_settings.StreamLayerOn) SetStreamLayer400(!_streamLayer, true); return true;
                case "xf.a": if (_settings.CrossOn) SetCross400(0f, true); return true;
                case "xf.mid": if (_settings.CrossOn) SetCross400(0.5f, true); return true;
                case "xf.b": if (_settings.CrossOn) SetCross400(1f, true); return true;
                case "xf.left": if (_settings.CrossOn) SetCross400((float)Math.Round(Math.Max(0f, _settings.CrossPosNow - 0.1f) * 20) / 20f, true); return true;
                case "xf.right": if (_settings.CrossOn) SetCross400((float)Math.Round(Math.Min(1f, _settings.CrossPosNow + 0.1f) * 20) / 20f, true); return true;
                case "gen": if (_settings.GenOn) SetGenRunning400(!_genRun); return true;
            }
            if (action.StartsWith("mg.") && int.TryParse(action.Substring(3), out int n)) { ToggleMuteGroup400(n - 1); return true; }
            return false;
        }

        // ================================================================ Einstellungen

        private void AddPro400(SPage p)
        {
            p.Section("Klang pro Kanal (Rechtsklick auf den Kanal → Profi-Ton)");
            p.Toggle("Kompressor-Fenster", "Eigene Werte für den Kompressor (COMP) eines Kanals: Schwelle, Verhältnis, Attack, Release, Make-up, Knie – mit Kennlinie und Verlauf der Absenkung. Ohne eigene Werte bleibt alles wie bisher.",
                () => _settings.ProCompOn, v => { _settings.ProCompOn = v; if (!v) foreach (var w in _compWins.Values.ToList()) try { w.Close(); } catch { } PushPolicy(); ScheduleSave(); });
            p.Toggle("De-Esser", "Nimmt scharfe S- und Zischlaute weg (nur dann, sonst unverändert). Stärke pro Kanal: leicht, mittel, stark.",
                () => _settings.DeEssOn, v => { _settings.DeEssOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Analoge Wärme", "Etwas Sättigung wie bei alter Technik: Röhre (warm, rund), Band (weich, Höhen runder) oder Konsole (ganz leicht). Stärke pro Kanal.",
                () => _settings.WarmOn, v => { _settings.WarmOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Rauschsperre", "Unter einer Schwelle wird der Kanal still (z. B. Rauschen in Pausen). Mit Haltezeit, damit Wortenden nicht abgeschnitten werden.",
                () => _settings.NoiseGateOn, v => { _settings.NoiseGateOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Brumm-Entferner", "Schmale Kerbfilter bei 50 Hz (Europa) oder 60 Hz (USA) und den Obertönen – gegen Netzbrummen.",
                () => _settings.HumOn, v => { _settings.HumOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Polarität drehen", "Dreht das Signal um (Ø) – hilft, wenn zwei Quellen sich gegenseitig auslöschen und dünn klingen.",
                () => _settings.InvertOn, v => { _settings.InvertOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Transienten-Former (Punch)", "Anschläge knackiger oder weicher machen – Trommeln, Schritte, Schüsse – ohne die Lautstärke zu ändern.",
                () => _settings.PunchOn, v => { _settings.PunchOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Effekt-Reihenfolge", "Pro Kanal festlegen, in welcher Reihenfolge EQ, Kompressor, Wärme und Hall/Echo wirken (z. B. erst komprimieren, dann EQ).",
                () => _settings.FxOrderOn, v => { _settings.FxOrderOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Dynamischer EQ", "Im parametrischen EQ: ein Glocken-Band kann „dynamisch“ sein – es greift nur, wenn das Band lauter als die Schwelle ist (z. B. schrille Stellen nur dann zähmen).",
                () => _settings.DynEqOn, v => { _settings.DynEqOn = v; PeqWindow.DynOn = v; PushPolicy(); ScheduleSave(); }).Enabled = () => _settings.PeqOn;
            p.Toggle("Mitte/Seite-EQ", "Im parametrischen EQ: ein Band kann nur auf die Mitte (Stimme, Bass) oder nur auf die Seiten (Raum, Breite) wirken.",
                () => _settings.MsEqOn, v => { _settings.MsEqOn = v; PeqWindow.MsOn = v; PushPolicy(); ScheduleSave(); }).Enabled = () => _settings.PeqOn;

            p.Section("Ducking");
            p.Toggle("Frequenz-Ducking", "Beim Ducking wird die Musik nicht ganz leiser, sondern nur im Sprachbereich (ca. 400 Hz – 4 kHz) – Bass und Höhen bleiben. Stimmen sind trotzdem gut zu verstehen.",
                () => _settings.DuckFreqOn, v => { _settings.DuckFreqOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Mehrere Ducking-Regeln", "Zusätzlich zum normalen Ducking eigene Regeln: Auslöser (ein Kanal oder dein MIKRO) → Ziel-Kanäle, je mit eigener Absenkung und Schwelle, auf Wunsch nur im Sprachbereich.",
                () => _settings.DuckRulesOn, v => { _settings.DuckRulesOn = v; RulesChanged400(); });
            p.Button("Ducking-Regeln", "Regeln anlegen, ändern oder löschen.", "Bearbeiten ▾", r => ShowDuckRules400(r), () => (_settings.DuckRules?.Count ?? 0) + " Regeln").Enabled = () => _settings.DuckRulesOn;

            p.Section("Mischen");
            p.Toggle("Auto-Mix für Sprach-Kanäle", "Wie am Profi-Pult für Gesprächsrunden: Wer spricht, ist voll da – die anderen Sprach-Kanäle werden automatisch leiser (bis −15 dB). Kanäle auswählen: Rechtsklick → Profi-Ton → Im Auto-Mix.",
                () => _settings.AutoMixOn, v => { _settings.AutoMixOn = v; PushPolicy(); ScheduleSave(); });
            p.Toggle("Hall-Bus", "Ein gemeinsamer Hall für alle Kanäle: pro Kanal ein SEND (wie viel in den Hall geht). Klingt wie ein Raum, in dem alles steht – und spart Rechenzeit.",
                () => _settings.HallBusOn, v => { _settings.HallBusOn = v; Pro400.Hall = v ? new HallSpec(_settings.HallPresetNow, _settings.HallReturnDb) : null; PushPolicy(); ScheduleSave(); });
            p.Choice("Größe des Halls", () => "", MixerSettings.HallPresetNames, () => _settings.HallPreset, i => { _settings.HallPreset = i; ApplyPro400(_settings); ScheduleSave(); }).Enabled = () => _settings.HallBusOn;
            p.Choice("Lautstärke des Halls", () => "Rückweg des Hall-Busses.", MixerSettings.HallReturnValues.Select(v => (v > 0 ? "+" : "") + v + " dB").ToArray(),
                () => Math.Max(0, Array.IndexOf(MixerSettings.HallReturnValues, _settings.HallReturnDb)), i => { _settings.HallReturnDb = MixerSettings.HallReturnValues[i]; ApplyPro400(_settings); ScheduleSave(); }).Enabled = () => _settings.HallBusOn;
            p.Toggle("Crossfader", "Ein DJ-Überblender zwischen zwei Kanälen (z. B. zwei Musik-Apps). Öffnen über die Taste XF in der Kopfleiste.",
                () => _settings.CrossOn, v => { _settings.CrossOn = v; if (!v) try { _xfWin?.Close(); } catch { } PushPolicy(); ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });
            p.Choice("Kurve des Crossfaders", () => "", MixerSettings.CrossCurveNames, () => _settings.CrossCurve, i => { _settings.CrossCurve = i; PushPolicy(); ScheduleSave(); }).Enabled = () => _settings.CrossOn;
            p.Button("Crossfader öffnen", "", "Öffnen", r => ShowCross400()).Enabled = () => _settings.CrossOn;
            p.Toggle("Mute-Gruppen", "Bis zu 4 Tasten in der Kopfleiste, die mehrere Kanäle auf einmal stumm schalten. Ein Kanal darf in mehreren Gruppen sein.",
                () => _settings.MuteGroupsOn, v => { _settings.MuteGroupsOn = v; PushPolicy(); ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });
            p.Button("Mute-Gruppen", "Gruppen anlegen, Kanäle wählen, umbenennen.", "Bearbeiten ▾", r => ShowMuteGroups400(r), () => (_settings.MuteGroups?.Count ?? 0) + " von 4").Enabled = () => _settings.MuteGroupsOn;
            p.Toggle("Fader-Ebene „Stream“", "Eine Taste STREAM in der Kopfleiste: dann stellen die Kanal-Fader den Anteil im Stream-Mix statt deiner Lautstärke (Fader fahren hin). Braucht den Stream-Mix.",
                () => _settings.StreamLayerOn, v => { _settings.StreamLayerOn = v; if (!v && _streamLayer) SetStreamLayer400(false, false); ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });

            p.Section("Stream & Werkzeuge");
            p.Toggle("Stream-Limiter", "Der Stream-Mix geht nie über −1 dB True-Peak (auch zwischen den Samples), mit Vorausschau – keine Übersteuerung im Stream. Absenkung als GR-Anzeige am STREAM-Zug.",
                () => _settings.StreamLimitOn, v => { _settings.StreamLimitOn = v; Pro400.StreamLimitOn = v; RelayoutAllStrips(); ScheduleSave(); });
            p.Toggle("Signalgenerator", "Testtöne zum Einmessen: Sinus, Gleitton 20 Hz – 20 kHz, rosa und weißes Rauschen, auf einem wählbaren Gerät. Vorher leise stellen!",
                () => _settings.GenOn, v => { _settings.GenOn = v; if (!v) { _genRun = false; try { _genWin?.Close(); } catch { } } ApplyGen400(); ApplyHotkeys(); RefreshTopKeys400(); ScheduleSave(); });
            p.Button("Signalgenerator öffnen", "", "Öffnen", r => ShowGen400()).Enabled = () => _settings.GenOn;
        }

        private void CleanupPro400()
        {
            Pro400.Gen = null;
            foreach (var w in _compWins.Values.ToList()) try { w.Close(); } catch { }
            try { _xfWin?.Close(); } catch { }
            try { _genWin?.Close(); } catch { }
        }
    }
}
