// Unity-side WebSocket client. Standalone/editor use ClientWebSocket;
// WebGL uses Assets/Plugins/WebGL/TerritoryWs.jslib through DllImport.
// Incoming text messages are queued and drained on the main thread.
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Territory.Client.Unity
{
    public sealed class UnityWsClient : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void TerritoryWsConnect(string url);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void TerritoryWsSend(string text);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void TerritoryWsClose();
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string TerritoryWsPoll();
#endif

        public string url = "ws://localhost:4173";
        public bool autoReconnect = true;

        private readonly Queue<string> _incoming = new Queue<string>();
        private readonly Queue<string> _outgoing = new Queue<string>();
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private bool _wantOpen;

        public bool IsOpen { get; private set; }

        public event Action<string> Message;
        public event Action Opened;
        public event Action Closed;

        void OnEnable()
        {
            _wantOpen = true;
            Open();
        }

        void OnDisable()
        {
            _wantOpen = false;
            Close();
        }

        public void Open()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TerritoryWsConnect(url);
#else
            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            Task.Run(ConnectLoop);
#endif
        }

        public void Send(string text)
        {
            lock (_outgoing) _outgoing.Enqueue(text);
#if UNITY_WEBGL && !UNITY_EDITOR
            FlushOutgoing();
#else
            FlushOutgoing();
#endif
        }

        void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DrainJslib();
#endif
            FlushOutgoing();
            DrainIncoming();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _jsOpen;

        // Called from the .jslib when the socket opens/closes.
        // ReSharper disable once UnusedMember.Global
        public void OnWsOpen()
        {
            _jsOpen = true;
            IsOpen = true;
            try { Opened?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        // ReSharper disable once UnusedMember.Global
        public void OnWsClose()
        {
            _jsOpen = false;
            IsOpen = false;
            try { Closed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            if (_wantOpen && autoReconnect)
                Invoke(nameof(Open), 1f);
        }

        // ReSharper disable once UnusedMember.Global
        public void OnWsMessage(string text)
        {
            lock (_incoming) _incoming.Enqueue(text);
        }

        private void DrainJslib()
        {
            // Messages arrive via OnWsMessage callbacks; nothing to poll here
            // unless the build predates the callback wiring.
            try
            {
                string batch = TerritoryWsPoll();
                if (!string.IsNullOrEmpty(batch))
                {
                    foreach (string line in batch.Split('\n'))
                    {
                        if (line.Length > 0) lock (_incoming) _incoming.Enqueue(line);
                    }
                }
            }
            catch { }
        }
#else
        private async Task ConnectLoop()
        {
            while (_wantOpen)
            {
                try
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        await _socket.ConnectAsync(new Uri(url), cts.Token);
                    }
                    IsOpen = true;
                    try { Opened?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    await ReceiveLoop(_cts.Token);
                }
                catch { }
                IsOpen = false;
                try { Closed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                if (!_wantOpen || !autoReconnect) break;
                try { await Task.Delay(1000, _cts.Token); } catch { break; }
                _socket = new ClientWebSocket();
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var buf = new byte[65536];
            var ms = new System.IO.MemoryStream();
            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await _socket.ReceiveAsync(new ArraySegment<byte>(buf), token);
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) break;
                if (r.MessageType != WebSocketMessageType.Text) continue;
                lock (_incoming) _incoming.Enqueue(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
#endif

        private void FlushOutgoing()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_jsOpen) return;
            while (true)
            {
                string text;
                lock (_outgoing)
                {
                    if (_outgoing.Count == 0) return;
                    text = _outgoing.Dequeue();
                }
                try { TerritoryWsSend(text); } catch { return; }
            }
#else
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            while (true)
            {
                string text;
                lock (_outgoing)
                {
                    if (_outgoing.Count == 0) return;
                    text = _outgoing.Dequeue();
                }
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                try
                {
                    _socket.SendAsync(new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
                }
                catch { return; }
            }
#endif
        }

        private void DrainIncoming()
        {
            while (true)
            {
                string text;
                lock (_incoming)
                {
                    if (_incoming.Count == 0) return;
                    text = _incoming.Dequeue();
                }
                try { Message?.Invoke(text); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void Close()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { TerritoryWsClose(); } catch { }
            _jsOpen = false;
#else
            try { _cts?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
            _socket = null;
#endif
            IsOpen = false;
        }
    }
}
