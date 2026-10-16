// Session logic ported from territory/server/index.js: seating, tokens,
// 10s reconnect grace, host-only actions, session wins, results log,
// and the tick loop that broadcasts snapshots or deltas.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Territory.Sim;

namespace Territory.Server
{
    public sealed class ServerOptions
    {
        public int Port;
        public string WwwRoot = "";
        public string ResultsLog = "";
        public GameOptions GameOptions;
        public bool Log;
    }

    internal sealed class Session
    {
        public WsConnection Conn;
        public int? PlayerId;
        public string Token;
        public string QueuedName = "";
        public bool Replaced;
    }

    public sealed class GameHost : IDisposable
    {
        private readonly Game _game;
        private readonly ServerOptions _options;
        private readonly WsServer _ws;
        private readonly object _gate = new object();
        private bool _closing;

        private readonly Dictionary<string, int> _tokens = new Dictionary<string, int>();
        private readonly Dictionary<int, Session> _sockets = new Dictionary<int, Session>();
        private readonly HashSet<Session> _watchers = new HashSet<Session>();
        private readonly Dictionary<int, CancellationTokenSource> _dropTimers =
            new Dictionary<int, CancellationTokenSource>();
        private readonly Dictionary<string, int> _wins = new Dictionary<string, int>();
        private readonly Timer _timer;
        private UrlSet _urls = new UrlSet();

        public int Port { get; private set; }
        public UrlSet Urls => _urls;

        public GameHost(ServerOptions options)
        {
            _options = options;
            _game = new Game(options.GameOptions);
            _ws = new WsServer(options.WwwRoot, options.Port);
            _ws.OnConnect = (conn) =>
            {
                lock (_gate) _byConn[conn] = new Session { Conn = conn };
            };
            _ws.OnMessage = (conn, text) =>
            {
                Session session;
                lock (_gate) session = FindSession(conn);
                if (session == null) return;
                HandleMessage(session, text);
            };
            _ws.OnClose = (conn) =>
            {
                Session session;
                lock (_gate) session = FindSession(conn);
                if (session != null) Drop(session);
            };
            _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private readonly Dictionary<WsConnection, Session> _byConn = new Dictionary<WsConnection, Session>();
        private Session FindSession(WsConnection conn)
        {
            Session s;
            return _byConn.TryGetValue(conn, out s) ? s : null;
        }

        public void Start()
        {
            _ws.Start();
            Port = _ws.Port;
            _urls = NetworkUrls(Port);
            _timer.Change(_game.TickMs, _game.TickMs);
            if (_options.Log)
            {
                Console.WriteLine("Territory");
                Console.WriteLine($"Open on this machine: {_urls.Local}");
                if (_urls.Shared.Count > 0)
                {
                    Console.WriteLine("Others on the VPN open:");
                    foreach (var u in _urls.Shared) Console.WriteLine(u);
                }
                else Console.WriteLine("No other network address found. Check that the VPN is connected.");
            }
        }

        public static UrlSet NetworkUrls(int port)
        {
            var urls = new UrlSet { Local = $"http://localhost:{port}" };
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    if (System.Net.IPAddress.IsLoopback(addr.Address)) continue;
                    urls.Shared.Add($"http://{addr.Address}:{port}");
                }
            }
            return urls;
        }

        private static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string NewToken()
        {
            byte[] bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        // --- outgoing messages (call with _gate held) ---

        private void Send(Session s, object msg)
        {
            try { s.Conn.SendText(Protocol.Encode(msg)); } catch { }
        }

        private bool HasJoined(Session s) => s.PlayerId != null || _watchers.Contains(s);

        private void Broadcast(object msg)
        {
            string text = Protocol.Encode(msg);
            foreach (var s in _byConn.Values.ToList())
            {
                if (!HasJoined(s)) continue;
                try { s.Conn.SendText(text); } catch { }
            }
        }

        private Dictionary<string, int> WinsObject() =>
            _wins.ToDictionary(kv => kv.Key, kv => kv.Value);

        private object LobbyMessage()
        {
            var scores = _game.Phase == "over" ? Protocol.ScoresOf(_game.Scores()) : null;
            return Protocol.Lobby(WinsObject(), _game.Phase, _game.HostId,
                _game.Roster().Select(Protocol.PlayerRow).ToList(), scores, _urls);
        }

        private void PushLobby() => Broadcast(LobbyMessage());

        private void Hello(Session s) =>
            Send(s, Protocol.Hello(s.Token, s.PlayerId, _game.HostId, _urls, _game.TickMs));

        private object SnapshotMessage(TickResult msg, string type)
        {
            var players = _game.Roster().Where(r => GamePlayerInRound(r.Id))
                .Select(Protocol.PlayerRow).ToList();
            return Protocol.Snapshot(type, WinsObject(), _game, msg, players,
                _game.Roster().Select(Protocol.PlayerRow).ToList(), NowMs());
        }

        private bool GamePlayerInRound(int id)
        {
            var p = _game.GetPlayer(id);
            return p != null && p.InRound;
        }

        private void PushState(Session s)
        {
            if (_game.Phase == "lobby")
            {
                Send(s, LobbyMessage());
                return;
            }
            var msg = new TickResult
            {
                Phase = _game.Phase,
                Scores = _game.Phase == "over" ? _game.Scores() : null,
            };
            Send(s, SnapshotMessage(msg, _game.Phase == "over" ? "over" : "full"));
        }

        private void Bind(Session s, int id, string token)
        {
            Session previous;
            if (_sockets.TryGetValue(id, out previous) && previous != s)
            {
                previous.Replaced = true;
                try { previous.Conn.Dispose(); } catch { }
            }
            s.PlayerId = id;
            s.Token = token;
            s.Replaced = false;
            _sockets[id] = s;
            _watchers.Remove(s);
        }

        private void SeatWatchers()
        {
            foreach (var s in _watchers.ToList())
            {
                var player = _game.AddPlayer(s.QueuedName);
                if (player == null) return;
                _watchers.Remove(s);
                string token = !string.IsNullOrEmpty(s.Token) ? s.Token : NewToken();
                _tokens[token] = player.Id;
                Bind(s, player.Id, token);
                Hello(s);
            }
        }

        private void Join(Session s, JsonElement msg)
        {
            string token = "";
            JsonElement tok;
            if (msg.TryGetProperty("token", out tok) && tok.ValueKind == JsonValueKind.String)
                token = tok.GetString() ?? "";
            int existingId;
            Player existing = null;
            if (token != "" && _tokens.TryGetValue(token, out existingId))
                existing = _game.GetPlayer(existingId);
            if (existing != null)
            {
                _game.MarkConnected(existing.Id);
                CancellationTokenSource cts;
                if (_dropTimers.TryGetValue(existing.Id, out cts)) { cts.Cancel(); _dropTimers.Remove(existing.Id); }
                Bind(s, existing.Id, token);
                Hello(s);
                PushState(s);
                if (_game.Phase == "lobby" || _game.Phase == "over") PushLobby();
                return;
            }

            string name = "";
            JsonElement nm;
            if (msg.TryGetProperty("name", out nm) && nm.ValueKind == JsonValueKind.String)
                name = nm.GetString() ?? "";
            bool watching = _game.Phase == "play" || _game.Phase == "countdown";
            var player = watching ? null : _game.AddPlayer(name);
            if (player == null)
            {
                s.PlayerId = null;
                s.Token = token != "" ? token : NewToken();
                s.QueuedName = name;
                _watchers.Add(s);
                Hello(s);
                PushState(s);
                return;
            }

            string issued = NewToken();
            _tokens[issued] = player.Id;
            Bind(s, player.Id, issued);
            Hello(s);
            PushLobby();
            if (_options.Log) Console.WriteLine($"{player.Name} joined ({_sockets.Count} seated)");
        }

        private void Drop(Session s)
        {
            if (_closing || s.Replaced) return;
            _watchers.Remove(s);
            if (s.PlayerId == null) return;
            int id = s.PlayerId.Value;
            Session current;
            if (!_sockets.TryGetValue(id, out current) || current != s) return;
            var player = _game.GetPlayer(id);
            if (player == null) return;
            player.Connected = false;
            var cts = new CancellationTokenSource();
            _dropTimers[id] = cts;
            Task.Delay(10000, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                lock (_gate)
                {
                    _dropTimers.Remove(id);
                    Session cur;
                    if (!_sockets.TryGetValue(id, out cur) || cur != s) return;
                    string phase = _game.Phase;
                    _game.RemovePlayer(id);
                    _sockets.Remove(id);
                    if (phase == "lobby" || phase == "over") _tokens.Remove(s.Token);
                    if (_game.Phase == "lobby" || _game.Phase == "over")
                    {
                        _game.Vacate();
                        SeatWatchers();
                        PushLobby();
                    }
                }
            });
        }

        private void HandleMessage(Session s, string text)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch { return; }
            using (doc)
            {
                var msg = doc.RootElement;
                if (msg.ValueKind != JsonValueKind.Object) return;
                JsonElement t;
                if (!msg.TryGetProperty("type", out t) || t.ValueKind != JsonValueKind.String) return;
                lock (_gate)
                {
                    if (_closing) return;
                    switch (t.GetString())
                    {
                        case "join": Join(s, msg); break;
                        case "rename":
                            {
                                string name = "";
                                JsonElement nm;
                                if (msg.TryGetProperty("name", out nm) && nm.ValueKind == JsonValueKind.String)
                                    name = nm.GetString() ?? "";
                                var result = _game.Rename(s.PlayerId ?? -1, name);
                                if (!result.Ok) Send(s, Protocol.Error(result.Error));
                                else if (result.Changed) Broadcast(LobbyMessage());
                                break;
                            }
                        case "dir":
                            if (s.PlayerId != null)
                            {
                                JsonElement d;
                                if (msg.TryGetProperty("dir", out d) && d.ValueKind == JsonValueKind.String)
                                    _game.SetDirection(s.PlayerId.Value, d.GetString() ?? "");
                            }
                            break;
                        case "steer":
                            if (s.PlayerId != null)
                            {
                                double turn = 0;
                                JsonElement tn;
                                if (msg.TryGetProperty("turn", out tn) && tn.ValueKind == JsonValueKind.Number)
                                    turn = tn.GetDouble();
                                _game.SetSteer(s.PlayerId.Value, turn);
                            }
                            break;
                        case "aim":
                            if (s.PlayerId != null)
                            {
                                double angle = double.NaN;
                                JsonElement an;
                                if (msg.TryGetProperty("angle", out an) && an.ValueKind == JsonValueKind.Number)
                                    angle = an.GetDouble();
                                _game.SetAim(s.PlayerId.Value, angle);
                            }
                            break;
                        case "start":
                            {
                                _game.Vacate();
                                SeatWatchers();
                                double minutes = double.NaN;
                                JsonElement mn;
                                if (msg.TryGetProperty("minutes", out mn) && mn.ValueKind == JsonValueKind.Number)
                                    minutes = mn.GetDouble();
                                int mins = !double.IsNaN(minutes) && minutes == (int)minutes
                                    && Defs.Minutes.Contains((int)minutes) ? (int)minutes : 3;
                                var result = _game.Start(s.PlayerId ?? -1, mins, NowMs());
                                if (!result.Ok) { Send(s, Protocol.Error(result.Error)); break; }
                                var tick = new TickResult { Phase = _game.Phase };
                                Broadcast(SnapshotMessage(tick, "full"));
                                break;
                            }
                        case "bot":
                            {
                                if (s.PlayerId != _game.HostId)
                                {
                                    Send(s, Protocol.Error("Only the host can add a bot."));
                                    break;
                                }
                                string level = "normal";
                                JsonElement lv;
                                if (msg.TryGetProperty("level", out lv) && lv.ValueKind == JsonValueKind.String)
                                    level = lv.GetString() ?? "normal";
                                var result = _game.AddBot(level);
                                if (!result.Ok) { Send(s, Protocol.Error(result.Error)); break; }
                                PushLobby();
                                break;
                            }
                        case "removeBot":
                            {
                                if (s.PlayerId != _game.HostId)
                                {
                                    Send(s, Protocol.Error("Only the host can remove a bot."));
                                    break;
                                }
                                int id = -1;
                                JsonElement bid;
                                if (msg.TryGetProperty("id", out bid) && bid.ValueKind == JsonValueKind.Number)
                                    id = (int)bid.GetDouble();
                                var result = _game.RemoveBot(id);
                                if (!result.Ok) { Send(s, Protocol.Error(result.Error)); break; }
                                PushLobby();
                                break;
                            }
                        case "restart":
                            {
                                var result = _game.ReturnToLobby(s.PlayerId ?? -1);
                                if (!result.Ok) { Send(s, Protocol.Error(result.Error)); break; }
                                PushLobby();
                                break;
                            }
                    }
                }
            }
        }

        private void RecordWin(ScoreResult scores)
        {
            if (scores == null || scores.Tied || scores.WinnerId == null) return;
            var winner = scores.List.Find(r => r.Id == scores.WinnerId.Value);
            if (winner == null) return;
            string key = winner.Name.ToLowerInvariant();
            int count;
            _wins.TryGetValue(key, out count);
            _wins[key] = count + 1;
        }

        private string Stamp()
        {
            var d = DateTime.Now;
            return $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2} {d.Hour:D2}:{d.Minute:D2}:{d.Second:D2}";
        }

        private void WriteResult()
        {
            string entry = $"{Stamp()}\n{_game.ResultLog()}\n\n";
            Console.WriteLine(entry.TrimEnd());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_options.ResultsLog));
                File.AppendAllText(_options.ResultsLog, entry);
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); }
        }

        private void OnTick()
        {
            try
            {
                lock (_gate)
                {
                    if (_closing) return;
                    if (_game.Phase != "countdown" && _game.Phase != "play") return;
                    double now = NowMs();
                    var msg = _game.Tick(now);
                    if (msg.Phase == "over")
                    {
                        RecordWin(msg.Scores);
                        try { WriteResult(); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
                    }
                    if (msg.Phase == "over" || msg.Resync)
                        Broadcast(SnapshotMessage(msg, msg.Phase == "over" ? "over" : "full"));
                    else
                        Broadcast(Protocol.Delta(_game, msg,
                            _game.Roster().Where(r => GamePlayerInRound(r.Id))
                                .Select(Protocol.PlayerRow).ToList(), now));
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); }
        }

        public void Dispose()
        {
            lock (_gate) _closing = true;
            try { _timer.Dispose(); } catch { }
            lock (_gate)
            {
                foreach (var cts in _dropTimers.Values) try { cts.Cancel(); } catch { }
                _dropTimers.Clear();
                foreach (var s in _byConn.Values) try { s.Conn.Dispose(); } catch { }
                _byConn.Clear();
            }
            _ws.Dispose();
        }
    }
}
