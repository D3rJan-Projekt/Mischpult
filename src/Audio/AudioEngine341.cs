using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Mischpult.Native;

namespace Mischpult.Audio
{
    /// <summary>3.4.1: Meldungen einer Session an die Engine weitergeben (Lautstaerke von aussen, Zustand).</summary>
    [ComVisible(true)]
    internal sealed class SessionGuard : IAudioSessionEvents
    {
        private readonly Action<float, bool, Guid> _volume;
        private readonly Action<AudioSessionState> _state;
        private readonly Action _gone;
        public IntPtr Ptr;
        private int _count;
        private long _windowMs = -1000000, _pauseUntil;   // nicht long.MinValue: die Differenz liefe ueber

        public SessionGuard(Action<float, bool, Guid> volume, Action<AudioSessionState> state, Action gone)
        {
            _volume = volume; _state = state; _gone = gone;
        }

        /// <summary>Hoechstens 15 Korrekturen pro Sekunde – kaempft eine App dagegen an, 3 s Ruhe (kein Hin und Her).</summary>
        public bool Allow(long nowMs)
        {
            lock (this)
            {
                if (nowMs < _pauseUntil) return false;
                if (nowMs - _windowMs > 1000) { _windowMs = nowMs; _count = 0; }
                if (++_count > 15) { _pauseUntil = nowMs + 3000; return false; }
                return true;
            }
        }

        public int OnDisplayNameChanged(IntPtr newDisplayName, IntPtr eventContext) => 0;
        public int OnIconPathChanged(IntPtr newIconPath, IntPtr eventContext) => 0;
        public int OnSimpleVolumeChanged(float newVolume, bool newMute, IntPtr eventContext)
        {
            try
            {
                var ctx = eventContext == IntPtr.Zero ? Guid.Empty : (Guid)Marshal.PtrToStructure(eventContext, typeof(Guid));
                _volume?.Invoke(newVolume, newMute, ctx);
            }
            catch { }
            return 0;
        }
        public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, IntPtr eventContext) => 0;
        public int OnGroupingParamChanged(IntPtr newGroupingParam, IntPtr eventContext) => 0;
        public int OnStateChanged(AudioSessionState newState)
        {
            try { _state?.Invoke(newState); } catch { }
            return 0;
        }
        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            try { _gone?.Invoke(); } catch { }
            return 0;
        }
    }

    /// <summary>
    /// 3.4.1 – App startet ohne lauten Anfang:
    /// – Sofort einstellen: Meldet Windows eine neue Session, stellt ein eigener Faden sie innerhalb von ~1 ms auf die
    ///   Lautstaerke des Kanals (EQ-Weg: -80 dB). Bisher kam das erst nach dem naechsten Durchgang der Engine und der
    ///   Prozess-Suche – so lange spielte die App mit der Lautstaerke, die Windows sich gemerkt hatte (oft 100 %).
    /// – Festhalten: Manche Apps (z. B. Spotify) stellen ihre Windows-Lautstaerke beim Abspielen selbst ein. Das Pult
    ///   bekommt jede Aenderung von aussen gemeldet und stellt sofort wieder seinen Wert ein. Dazu prueft es alle 100 ms nach.
    /// </summary>
    internal sealed partial class AudioEngine
    {
        /// <summary>App-Lautstaerke festhalten (Standard an).</summary>
        public static volatile bool GuardOn = true;

        private readonly ConcurrentQueue<IntPtr> _fastQueue = new ConcurrentQueue<IntPtr>();
        private readonly AutoResetEvent _fastApplyWake = new AutoResetEvent(false);
        private Thread _fastApplyThread;
        private readonly object _fastApplyLock = new object();
        private long _nextGuardPoll;

        // ------------------------------------------------------------------ Sofort einstellen

        private void QueueFastApply(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero || !CleanStart.On || _final || !_running) return;
            try { Marshal.AddRef(ptr); } catch { return; }
            _fastQueue.Enqueue(ptr);
            EnsureFastApplyThread();
            _fastApplyWake.Set();
        }

        private void EnsureFastApplyThread()
        {
            if (_fastApplyThread != null) return;
            lock (_fastApplyLock)
            {
                if (_fastApplyThread != null) return;
                var t = new Thread(FastApplyLoop) { IsBackground = true, Name = "Mischpult Sofort", Priority = ThreadPriority.Highest };
                t.SetApartmentState(ApartmentState.MTA);
                t.Start();
                _fastApplyThread = t;
            }
        }

        private void FastApplyLoop()
        {
            ThreadCpu.Register("Ton");
            while (_running)
            {
                _fastApplyWake.WaitOne(1000);
                while (_fastQueue.TryDequeue(out var ptr))
                {
                    object o = null;
                    try
                    {
                        o = Marshal.GetObjectForIUnknown(ptr);
                        FastApply(o);
                    }
                    catch { }
                    finally
                    {
                        try { Marshal.Release(ptr); } catch { }
                        ComUtil.Release(o);
                    }
                }
            }
            while (_fastQueue.TryDequeue(out var rest)) { try { Marshal.Release(rest); } catch { } }
        }

        /// <summary>Lautstaerke, mit der eine neue Session sofort laufen soll (EQ-Weg: -80 dB). null = nicht zustaendig.</summary>
        internal static float? FastTarget(AppPolicy p, bool eqPath, int newAppFadeMs)
        {
            if (p == null) return null;
            if (eqPath) return DspMixer.Attenuation;
            if (newAppFadeMs > 0) return 0f;                 // 3.4: neue App beginnt still und blendet ein
            return p.Mute ? 0f : Clamp01(p.Volume * p.AppGain);
        }

        private void FastApply(object o)
        {
            if (_final || !CleanStart.On) return;
            var c2 = o as IAudioSessionControl2;
            var vol = o as ISimpleAudioVolume;
            if (c2 == null || vol == null) return;
            if (c2.GetProcessId(out uint pid) != 0 || pid == 0 || pid == _ownPid) return;
            string key, path = null;
            if (c2.IsSystemSoundsSession() == 0) key = AppKeys.System;
            else
            {
                path = Win32.GetProcessPath(pid);
                if (path == null) return;                    // unbekannt: macht die Engine gleich
                key = AppKeys.FromExe(path);
            }
            var p = _policy.Get(key);
            if (p == null) return;
            var dsp = _dsp;
            bool eq = p.Eq != null && dsp != null && !dsp.IsChannelBroken(p.Eq.ChannelId) && key != AppKeys.System &&
                      !dsp.IsFailed(pid) && !_treeConflicts.Contains(pid);
            var target = FastTarget(p, eq, CleanStart.NewAppFadeMs);
            if (target == null) return;
            float before = -1f; bool beforeMute = false;
            if (SoundLog.On) { try { vol.GetMasterVolume(out before); vol.GetMute(out beforeMute); } catch { } }
            vol.SetMasterVolume(target.Value, ref EventContext);
            if (eq) dsp.NoteAttenuated(pid);
            if (SoundLog.On)
                SoundLog.Write("Neue App " + System.IO.Path.GetFileName(path ?? key) + " (PID " + pid + "): Windows startete sie mit " +
                               SoundLog.F(before) + (beforeMute ? " stumm" : "") + " -> sofort " + SoundLog.F(target.Value) + (eq ? " (EQ-Weg)" : ""));
        }

        // ------------------------------------------------------------------ Festhalten

        private void RegisterGuard(SessionEntry s)
        {
            if (s?.Control == null || !Win32.IsWindows) return;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                SessionGuard g = null;
                g = new SessionGuard((v, m, ctx) => OnExternalVolume(s, v, m, ctx, g),
                                     st => OnGuardState(s, st, g),
                                     () => { _sessionsDirty = true; _wake.Set(); });
                ptr = Marshal.GetComInterfaceForObject(g, typeof(IAudioSessionEvents));
                if (s.Control.RegisterAudioSessionNotification(ptr) == 0) { g.Ptr = ptr; s.Guard = g; ptr = IntPtr.Zero; }
            }
            catch { }
            finally { if (ptr != IntPtr.Zero) { try { Marshal.Release(ptr); } catch { } } }
        }

        private static void UnregisterGuard(SessionEntry s)
        {
            var g = s.Guard;
            if (g == null) return;
            s.Guard = null;
            try { s.Control?.UnregisterAudioSessionNotification(g.Ptr); } catch { }
            try { Marshal.Release(g.Ptr); } catch { }
            g.Ptr = IntPtr.Zero;
        }

        /// <summary>Weicht die Lautstaerke/Stummschaltung der Session vom Wert des Pults ab?</summary>
        internal static bool Deviates(float v, bool m, float want, bool wantMute) =>
            m != wantMute || Math.Abs(v - want) > Math.Max(1e-5f, want * 0.02f);

        private void OnExternalVolume(SessionEntry s, float v, bool m, Guid ctx, SessionGuard g)
        {
            if (ctx == EventContext) return;                 // eigene Aenderung
            Reassert(s, v, m, g, "gemeldet");
        }

        private void OnGuardState(SessionEntry s, AudioSessionState st, SessionGuard g)
        {
            _sessionsDirty = true;
            _wake.Set();
            if (st != AudioSessionState.Active || s.Volume == null) return;
            // faengt gerade an zu spielen: stimmt die Lautstaerke noch?
            try
            {
                if (s.Volume.GetMasterVolume(out float v) == 0 && s.Volume.GetMute(out bool m) == 0) Reassert(s, v, m, g, "beim Abspielen");
            }
            catch { }
        }

        private void Reassert(SessionEntry s, float v, bool m, SessionGuard g, string how)
        {
            if (_final || !GuardOn || !CleanStart.On || !s.HasApplied || s.Volume == null) return;
            float want = s.AppliedVolume;
            bool wantMute = s.AppliedMute;
            if (!Deviates(v, m, want, wantMute)) return;
            long now = _engineClock.ElapsedMilliseconds;
            if (g != null && !g.Allow(now))
            {
                SoundLog.Write(s.Name + ": stellt die Lautstaerke staendig selbst ein – Pult wartet 3 s");
                return;
            }
            try
            {
                if (wantMute && !m) s.Volume.SetMute(true, ref EventContext);
                s.Volume.SetMasterVolume(want, ref EventContext);
                if (!wantMute && m) s.Volume.SetMute(false, ref EventContext);
            }
            catch { return; }
            if (s.Dsp) _dsp?.NoteAttenuated(s.Pid);   // was in der Zeit laut ankam, ist ein Uebergang
            s.GainChangedMs = now;                      // Messung erst wieder werten, wenn sie sich beruhigt hat
            SoundLog.Write(s.Name + ": Lautstaerke von aussen auf " + SoundLog.F(v) + (m ? " stumm" : "") + " gesetzt (" + how + ") -> zurueck auf " + SoundLog.F(want) + (wantMute ? " stumm" : ""));
        }

        /// <summary>Alle 100 ms: stimmt die Lautstaerke der Sessions noch? (falls eine Meldung fehlt) Und: ist der EQ-Weg wirklich abgesenkt?</summary>
        private void GuardPoll()
        {
            long now = _engineClock.ElapsedMilliseconds;
            if (now < _nextGuardPoll || _final) return;
            _nextGuardPoll = now + 100;
            Dictionary<uint, bool> att = null;
            foreach (var s in _sessions.Values)
            {
                if (!s.HasApplied || s.Volume == null) continue;
                if (s.State != AudioSessionState.Active && !s.Dsp) continue;
                float v; bool m;
                try { if (s.Volume.GetMasterVolume(out v) != 0 || s.Volume.GetMute(out m) != 0) continue; }
                catch { continue; }
                if (CleanStart.On && GuardOn && Deviates(v, m, s.AppliedVolume, s.AppliedMute))
                {
                    Reassert(s, v, m, s.Guard, "nachgeprueft");
                    if (s.Dsp) (att ?? (att = new Dictionary<uint, bool>()))[s.Pid] = false;
                    continue;
                }
                if (s.Dsp && s.Pid != 0)
                {
                    if (att == null) att = new Dictionary<uint, bool>();
                    bool ok = !m && Math.Abs(v - DspMixer.Attenuation) <= DspMixer.Attenuation * 0.05f;
                    att[s.Pid] = (!att.TryGetValue(s.Pid, out bool prev) || prev) && ok;
                }
            }
            _dsp?.NoteVerified(att);
        }
    }
}
