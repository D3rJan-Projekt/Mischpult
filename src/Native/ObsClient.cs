using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mischpult.Native
{
    /// <summary>
    /// 4.0: Verbindung zu OBS Studio (obs-websocket 5, in OBS 28+ eingebaut): Szenen umschalten, Stream/Aufnahme
    /// starten/stoppen und mitbekommen, was OBS gerade tut. Laeuft im Hintergrund, verbindet sich bei Bedarf neu.
    /// </summary>
    internal sealed class ObsClient : IDisposable
    {
        public string Host = "127.0.0.1";
        public int Port = 4455;
        public string Password;
        public volatile bool Connected;
        public volatile bool Streaming, Recording;
        public volatile string CurrentScene, Status = "nicht verbunden";
        public List<string> Scenes = new List<string>();
        /// <summary>Etwas hat sich geaendert (Verbindung, Szene, Stream, Aufnahme) – aus einem Hintergrund-Thread.</summary>
        public event Action Changed;
        /// <summary>OBS hat die Szene gewechselt (Name) – aus einem Hintergrund-Thread.</summary>
        public event Action<string> SceneSwitched;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _loop;
        private readonly SemaphoreSlim _send = new SemaphoreSlim(1, 1);

        public bool Running => _loop != null;

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => Run(ct));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Abort(); } catch { }
            _loop = null;
            Connected = false; Streaming = false; Recording = false;
            Status = "nicht verbunden";
            Changed?.Invoke();
        }

        private async Task Run(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await ConnectOnce(ct).ConfigureAwait(false); }
                catch (Exception ex) { if (!ct.IsCancellationRequested) Status = "keine Verbindung (" + Short(ex) + ")"; }
                Connected = false; Streaming = false; Recording = false;
                try { Changed?.Invoke(); } catch { }
                try { await Task.Delay(5000, ct).ConfigureAwait(false); } catch { break; }
            }
        }

        private static string Short(Exception ex)
        {
            var e = ex.InnerException ?? ex;
            string m = e.Message ?? e.GetType().Name;
            return m.Length > 60 ? m.Substring(0, 60) + "…" : m;
        }

        private async Task ConnectOnce(CancellationToken ct)
        {
            try { _ws?.Dispose(); } catch { }
            _ws = new ClientWebSocket();
            Status = "verbinde …";
            await _ws.ConnectAsync(new Uri("ws://" + Host + ":" + Port), ct).ConfigureAwait(false);
            var buf = new byte[65536];
            var sb = new StringBuilder();
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) { Status = r.CloseStatusDescription != null && r.CloseStatusDescription.Length > 0 ? "OBS: " + r.CloseStatusDescription : "getrennt"; return; }
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                } while (!r.EndOfMessage);
                try { Handle(sb.ToString()); } catch { }
            }
        }

        /// <summary>Antwort auf die Anmelde-Frage von OBS: base64(sha256(base64(sha256(Passwort + Salz)) + Challenge)).</summary>
        internal static string AuthString(string password, string salt, string challenge)
        {
            using (var sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((password ?? "") + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        private void Handle(string json)
        {
            var o = MiniJson.Obj(MiniJson.Parse(json));
            if (o == null) return;
            int op = MiniJson.I(o, "op");
            var d = MiniJson.Obj(o.TryGetValue("d", out var dv) ? dv : null);
            switch (op)
            {
                case 0:   // Hello
                {
                    var id = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 1 | 4 | 64 };
                    var auth = MiniJson.Obj(d != null && d.TryGetValue("authentication", out var a) ? a : null);
                    if (auth != null) id["authentication"] = AuthString(Password, MiniJson.S(auth, "salt"), MiniJson.S(auth, "challenge"));
                    SendJson(new Dictionary<string, object> { ["op"] = 1, ["d"] = id });
                    break;
                }
                case 2:   // Identified
                    Connected = true;
                    Status = "verbunden";
                    Request("GetSceneList");
                    Request("GetStreamStatus");
                    Request("GetRecordStatus");
                    Changed?.Invoke();
                    break;
                case 5:   // Event
                {
                    string type = MiniJson.S(d, "eventType");
                    var ed = MiniJson.Obj(d != null && d.TryGetValue("eventData", out var e) ? e : null);
                    switch (type)
                    {
                        case "CurrentProgramSceneChanged":
                            CurrentScene = MiniJson.S(ed, "sceneName");
                            Changed?.Invoke();
                            SceneSwitched?.Invoke(CurrentScene);
                            break;
                        case "SceneListChanged": Request("GetSceneList"); break;
                        case "StreamStateChanged": Streaming = MiniJson.B(ed, "outputActive"); Changed?.Invoke(); break;
                        case "RecordStateChanged": Recording = MiniJson.B(ed, "outputActive"); Changed?.Invoke(); break;
                        case "ExitStarted": Status = "OBS wird beendet"; break;
                    }
                    break;
                }
                case 7:   // Antwort
                {
                    string type = MiniJson.S(d, "requestType");
                    var rd = MiniJson.Obj(d != null && d.TryGetValue("responseData", out var x) ? x : null);
                    if (type == "GetSceneList" && rd != null)
                    {
                        CurrentScene = MiniJson.S(rd, "currentProgramSceneName") ?? CurrentScene;
                        var list = rd.TryGetValue("scenes", out var sl) ? sl as List<object> : null;
                        if (list != null) Scenes = list.Select(MiniJson.Obj).Where(s => s != null).OrderByDescending(s => MiniJson.I(s, "sceneIndex")).Select(s => MiniJson.S(s, "sceneName")).Where(n => n != null).ToList();
                        Changed?.Invoke();
                    }
                    else if (type == "GetStreamStatus" && rd != null) { Streaming = MiniJson.B(rd, "outputActive"); Changed?.Invoke(); }
                    else if (type == "GetRecordStatus" && rd != null) { Recording = MiniJson.B(rd, "outputActive"); Changed?.Invoke(); }
                    break;
                }
            }
        }

        public void Request(string type, Dictionary<string, object> data = null)
        {
            var d = new Dictionary<string, object> { ["requestType"] = type, ["requestId"] = Guid.NewGuid().ToString("N") };
            if (data != null) d["requestData"] = data;
            SendJson(new Dictionary<string, object> { ["op"] = 6, ["d"] = d });
        }

        public void SetScene(string name) => Request("SetCurrentProgramScene", new Dictionary<string, object> { ["sceneName"] = name });
        public void ToggleStream() => Request("ToggleStream");
        public void ToggleRecord() => Request("ToggleRecord");

        private async void SendJson(object o)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(MiniJson.Write(o));
            try
            {
                await _send.WaitAsync().ConfigureAwait(false);
                try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
                finally { _send.Release(); }
            }
            catch { }
        }

        public void Dispose() { Stop(); try { _ws?.Dispose(); } catch { } }
    }
}
