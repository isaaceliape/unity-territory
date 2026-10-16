// Port of territory/test/server.test.js: the C# host must speak the same
// wire protocol, so these run real WebSocket clients against it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Territory.Sim;

namespace Territory.Server.Tests
{
    internal sealed class TestClient : IAsyncDisposable
    {
        public readonly ClientWebSocket Ws = new ClientWebSocket();
        private readonly List<JsonElement> _messages = new List<JsonElement>();
        private readonly List<(Func<JsonElement, bool>, TaskCompletionSource<JsonElement>)> _waiters =
            new List<(Func<JsonElement, bool>, TaskCompletionSource<JsonElement>)>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public IReadOnlyList<JsonElement> Messages
        {
            get { lock (_messages) return _messages.ToList(); }
        }

        public void ClearMessages() { lock (_messages) _messages.Clear(); }

        public static async Task<TestClient> Connect(string url)
        {
            var c = new TestClient();
            await c.Ws.ConnectAsync(new Uri(url), CancellationToken.None);
            _ = Task.Run(c.ReceiveLoop);
            return c;
        }

        private async Task ReceiveLoop()
        {
            var buf = new byte[65536];
            var ms = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await Ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                        ms.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    JsonElement el;
                    using (var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray())))
                        el = doc.RootElement.Clone();
                    List<(Func<JsonElement, bool>, TaskCompletionSource<JsonElement>)> hit =
                        new List<(Func<JsonElement, bool>, TaskCompletionSource<JsonElement>)>();
                    lock (_messages)
                    {
                        _messages.Add(el);
                        foreach (var w in _waiters) if (w.Item1(el)) hit.Add(w);
                        foreach (var h in hit) _waiters.Remove(h);
                    }
                    foreach (var h in hit) h.Item2.TrySetResult(el);
                }
            }
            catch { }
        }

        public Task Send(object o)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o));
            return Ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public Task<JsonElement> Until(Func<JsonElement, bool> pred, int timeoutMs = 15000)
        {
            lock (_messages)
            {
                foreach (var m in _messages) if (pred(m)) return Task.FromResult(m);
                var tcs = new TaskCompletionSource<JsonElement>();
                _waiters.Add((pred, tcs));
                var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetException(new TimeoutException("timed out waiting for a message")));
                return tcs.Task;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }
            Ws.Dispose();
        }
    }

    internal static class Msg
    {
        public static string Type(JsonElement e) => e.GetProperty("type").GetString();
        public static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        public static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
        public static double Num(JsonElement e, string name) => e.GetProperty(name).GetDouble();
        public static string Phase(JsonElement e) => e.GetProperty("phase").GetString();
        public static List<JsonElement> Roster(JsonElement e) =>
            e.GetProperty("roster").EnumerateArray().ToList();
        public static List<JsonElement> Players(JsonElement e) =>
            e.GetProperty("players").EnumerateArray().ToList();
        public static JsonElement Wins(JsonElement e) => e.GetProperty("wins");
        public static bool Has(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;
    }

    public class ServerTests
    {
        private string _wwwroot;
        private GameHost _host;
        private string _url;

        private async Task StartHost(GameOptions gameOptions = null, string resultsLog = null)
        {
            _wwwroot = Path.Combine(Path.GetTempPath(), "terr-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_wwwroot);
            File.WriteAllText(Path.Combine(_wwwroot, "index.html"), "<title>Territory</title>");
            var options = new ServerOptions
            {
                Port = 0,
                WwwRoot = _wwwroot,
                ResultsLog = resultsLog ?? Path.Combine(_wwwroot, "results.log"),
                GameOptions = gameOptions,
                Log = false,
            };
            _host = new GameHost(options);
            _host.Start();
            _url = $"ws://127.0.0.1:{_host.Port}";
            await Task.Delay(50);
        }

        [TearDown]
        public void Teardown()
        {
            try { _host?.Dispose(); } catch { }
            try { if (_wwwroot != null) Directory.Delete(_wwwroot, true); } catch { }
        }

        [Test]
        public async Task TwoBrowsersCanJoinAndOnlyTheHostCanStart()
        {
            await StartHost();
            var ada = await TestClient.Connect(_url);
            var bea = await TestClient.Connect(_url);
            try
            {
                await ada.Send(new { type = "join", name = "Ada" });
                var hello = await ada.Until(e => Msg.Type(e) == "hello");
                Assert.That(Msg.Str(hello, "token"), Is.Not.Null);
                Assert.That(Msg.Int(hello, "playerId"), Is.EqualTo(Msg.Int(hello, "hostId")));
                await bea.Send(new { type = "join", name = "Bea" });
                await bea.Until(e => Msg.Type(e) == "hello");
                var lobby = await ada.Until(e => Msg.Type(e) == "lobby" && Msg.Roster(e).Count == 2);
                Assert.That(Msg.Str(Msg.Roster(lobby)[0], "name"), Is.EqualTo("Ada"));
                Assert.That(Msg.Str(Msg.Roster(lobby)[1], "name"), Is.EqualTo("Bea"));

                await bea.Send(new { type = "start", minutes = 1 });
                var denied = await bea.Until(e => Msg.Type(e) == "error");
                Assert.That(Msg.Str(denied, "message"), Does.Contain("host"));

                await ada.Send(new { type = "start", minutes = 1 });
                var full = await ada.Until(e => Msg.Type(e) == "full");
                Assert.That(Msg.Phase(full), Is.EqualTo("countdown"));
                Assert.That(Msg.Players(full).Count, Is.EqualTo(2));
                int w = Msg.Int(full, "w"), h = Msg.Int(full, "h");
                Assert.That(Convert.FromBase64String(Msg.Str(full, "land")).Length, Is.EqualTo(w * h));
                var seen = await bea.Until(e => Msg.Type(e) == "full");
                Assert.That(Msg.Players(seen).Count, Is.EqualTo(2));
            }
            finally { await ada.DisposeAsync(); await bea.DisposeAsync(); }
        }

        [Test]
        public async Task AVisitorKeepsTheJoinFormWhileARoundIsRunning()
        {
            await StartHost();
            var ada = await TestClient.Connect(_url);
            var visitor = await TestClient.Connect(_url);
            try
            {
                await ada.Send(new { type = "join", name = "Ada" });
                await ada.Until(e => Msg.Type(e) == "hello");
                await ada.Send(new { type = "start", minutes = 1 });
                await ada.Until(e => Msg.Type(e) == "full");
                await Task.Delay(400);
                Assert.That(visitor.Messages.Count, Is.EqualTo(0));
            }
            finally { await ada.DisposeAsync(); await visitor.DisposeAsync(); }
        }

        [Test]
        public async Task ThePageIsServedFromTheSamePort()
        {
            await StartHost();
            using (var http = new HttpClient())
            {
                var response = await http.GetAsync($"http://127.0.0.1:{_host.Port}/");
                Assert.That((int)response.StatusCode, Is.EqualTo(200));
                string html = await response.Content.ReadAsStringAsync();
                Assert.That(html, Does.Contain("Territory"));
            }
        }

        [Test]
        public async Task SessionWinsCountUpAcrossRoundsAndShowInTheLobby()
        {
            string dir = Path.Combine(Path.GetTempPath(), "terr-res-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            await StartHost(new GameOptions { CountdownMs = 0, TickMs = 12 },
                Path.Combine(dir, "results.log"));
            var ada = await TestClient.Connect(_url);
            var bea = await TestClient.Connect(_url);
            try
            {
                await ada.Send(new { type = "join", name = "Ada" });
                await ada.Until(e => Msg.Type(e) == "hello");
                await bea.Send(new { type = "join", name = "Bea" });
                await bea.Until(e => Msg.Type(e) == "hello");
                await ada.Until(e => Msg.Type(e) == "lobby" && Msg.Roster(e).Count == 2);
                var first = await ada.Until(e => Msg.Type(e) == "lobby");
                Assert.That(Msg.Wins(first).EnumerateObject().Count(), Is.EqualTo(0));

                for (int round = 1; round <= 2; round += 1)
                {
                    await ada.Send(new { type = "start", minutes = 1 });
                    var full = await ada.Until(e => Msg.Type(e) == "full" && Msg.Has(e, "wins"));
                    await ada.Send(new { type = "steer", turn = 1 });
                    var beaRow = Msg.Players(full).Find(r => Msg.Str(r, "name") == "Bea");
                    double h = Msg.Num(full, "h");
                    await bea.Send(new { type = "dir", dir = Msg.Num(beaRow, "y") < h / 2 ? "up" : "down" });
                    int want = round;
                    var over = await ada.Until(e => Msg.Type(e) == "over"
                        && Msg.Wins(e).TryGetProperty("ada", out var a) && a.GetInt32() == want);
                    var scores = over.GetProperty("scores");
                    int winnerId = scores.GetProperty("winnerId").GetInt32();
                    var adaRow = Msg.Roster(over).Find(r => Msg.Str(r, "name") == "Ada");
                    Assert.That(winnerId, Is.EqualTo(Msg.Int(adaRow, "id")));
                    Assert.That(Msg.Wins(over).TryGetProperty("bea", out _), Is.False);
                    await ada.Send(new { type = "restart" });
                    var lobby = await ada.Until(e => Msg.Type(e) == "lobby"
                        && Msg.Phase(e) == "lobby"
                        && Msg.Wins(e).TryGetProperty("ada", out var w2) && w2.GetInt32() == want);
                    Assert.That(Msg.Wins(lobby).EnumerateObject().Count(), Is.EqualTo(1));
                    ada.ClearMessages();
                    bea.ClearMessages();
                }
            }
            finally
            {
                await ada.DisposeAsync(); await bea.DisposeAsync();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Test]
        public async Task OnlyTheHostAddsOrRemovesBots()
        {
            await StartHost();
            var ada = await TestClient.Connect(_url);
            var bea = await TestClient.Connect(_url);
            try
            {
                await ada.Send(new { type = "join", name = "Ada" });
                await ada.Until(e => Msg.Type(e) == "hello");
                await bea.Send(new { type = "join", name = "Bea" });
                await bea.Until(e => Msg.Type(e) == "hello");

                await bea.Send(new { type = "bot", level = "hard" });
                var denied = await bea.Until(e => Msg.Type(e) == "error");
                Assert.That(Msg.Str(denied, "message"), Does.Contain("host"));

                await ada.Send(new { type = "bot", level = "easy" });
                await ada.Send(new { type = "bot", level = "hard" });
                var two = await ada.Until(e => Msg.Type(e) == "lobby"
                    && Msg.Roster(e).FindAll(r => r.GetProperty("bot").GetBoolean()).Count == 2);
                var levels = Msg.Roster(two).Where(r => r.GetProperty("bot").GetBoolean())
                    .Select(r => Msg.Str(r, "level")).ToList();
                Assert.That(levels, Is.EqualTo(new List<string> { "easy", "hard" }));

                await ada.Send(new { type = "bot" });
                await ada.Until(e => Msg.Type(e) == "lobby"
                    && Msg.Roster(e).FindAll(r => r.GetProperty("bot").GetBoolean()).Count == 3);
                ada.ClearMessages();
                await ada.Send(new { type = "bot", level = "normal" });
                var full = await ada.Until(e => Msg.Type(e) == "error");
                Assert.That(Msg.Str(full, "message"), Does.Contain("up to 3 bots"));

                await ada.Send(new { type = "bot", level = "nope" });
                var bad = await ada.Until(e => Msg.Type(e) == "error"
                    && Msg.Str(e, "message").Contains("easy, normal or hard"));
                Assert.That(Msg.Str(bad, "message"), Does.Contain("easy, normal or hard"));

                int botId = Msg.Int(Msg.Roster(two).Find(r => Msg.Str(r, "level") == "easy"), "id");
                bea.ClearMessages();
                await bea.Send(new { type = "removeBot", id = botId });
                var deniedRm = await bea.Until(e => Msg.Type(e) == "error");
                Assert.That(Msg.Str(deniedRm, "message"), Does.Contain("host"));

                await ada.Send(new { type = "removeBot", id = botId });
                var after = await ada.Until(e => Msg.Type(e) == "lobby"
                    && Msg.Roster(e).FindAll(r => r.GetProperty("bot").GetBoolean()).Count == 2);
                Assert.That(Msg.Roster(after).Any(r => Msg.Int(r, "id") == botId), Is.False);
            }
            finally { await ada.DisposeAsync(); await bea.DisposeAsync(); }
        }
    }
}
