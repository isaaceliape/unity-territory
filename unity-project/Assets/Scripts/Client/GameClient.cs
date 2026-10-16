// Orchestrates one client: socket <-> BoardModel/FxModel/SfxSynth <-> views.
// Needs the Newtonsoft.Json Unity package (com.unity.nuget.newtonsoft-json).
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Territory.Client.Unity
{
    public sealed class GameClient : MonoBehaviour
    {
        public UnityWsClient socket;
        public BoardRenderer board;
        public HudController hud;
        public InputController input;
        public SfxPlayer sfx;
        public FxRenderer fx;

        public readonly BoardModel model = new BoardModel();
        public readonly FxModel effects;
        public readonly SfxSynth synth = new SfxSynth();

        public int? myId;
        public int? hostId;
        public string phase = "lobby";
        public int w, h;
        public double tickMs = 110;
        public Dictionary<string, int> sessionWins = new Dictionary<string, int>();
        public string lastError = "";

        public GameClient()
        {
            effects = new FxModel(() => UnityEngine.Random.value,
                Application.platform == RuntimePlatform.WebGLPlayer && ReducedMotionWebGL());
        }

        private static bool ReducedMotionWebGL() => false; // wired to a JS matchMedia call in the WebGL template

        void Awake()
        {
            if (socket == null) socket = GetComponent<UnityWsClient>();
            socket.Message += OnSocketMessage;
            socket.Opened += OnSocketOpen;
        }

        void OnDestroy()
        {
            if (socket != null)
            {
                socket.Message -= OnSocketMessage;
                socket.Opened -= OnSocketOpen;
            }
        }

        void Update()
        {
            effects.Update(Math.Min(0.05, Time.unscaledDeltaTime), Screen.height);
            synth.PumpMusic(Time.unscaledTime);
        }

        // --- outgoing ---

        private void Send(string json) => socket.Send(json);

        public void Join(string name)
        {
            PlayerPrefs.SetString("territory-name", name);
            string token = PlayerPrefs.GetString("territory-token", "");
            Send(Json(new Dictionary<string, object>
                { { "type", "join" }, { "name", name }, { "token", token } }));
            synth.StartMusic();
            if (sfx != null) sfx.EnsureTheme();
        }

        public void Rename(string name)
        {
            PlayerPrefs.SetString("territory-name", name);
            Send(Json(new Dictionary<string, object> { { "type", "rename" }, { "name", name } }));
        }

        public void StartRound(int minutes) =>
            Send(Json(new Dictionary<string, object> { { "type", "start" }, { "minutes", minutes } }));

        public void Restart() =>
            Send(Json(new Dictionary<string, object> { { "type", "restart" } }));

        public void AddBot(string level) =>
            Send(Json(new Dictionary<string, object> { { "type", "bot" }, { "level", level } }));

        public void RemoveBot(int id) =>
            Send(Json(new Dictionary<string, object> { { "type", "removeBot" }, { "id", id } }));

        public void Steer(int turn) =>
            Send(Json(new Dictionary<string, object> { { "type", "steer" }, { "turn", turn } }));

        public void Aim(double angle) =>
            Send(Json(new Dictionary<string, object> { { "type", "aim" }, { "angle", angle } }));

        private static string Json(Dictionary<string, object> o) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(o);

        // --- incoming ---

        private void OnSocketOpen()
        {
            lastError = "";
            // Rejoin with the saved token, mirroring the browser client.
            string name = PlayerPrefs.GetString("territory-name", "");
            string token = PlayerPrefs.GetString("territory-token", "");
            if (token != "")
            {
                Send(Json(new Dictionary<string, object>
                    { { "type", "join" }, { "name", name }, { "token", token } }));
                synth.StartMusic();
            }
            if (hud != null) hud.Refresh();
        }

        private void OnSocketMessage(string text)
        {
            JObject msg;
            try { msg = JObject.Parse(text); }
            catch { return; }
            string type = Str(msg, "type");
            try
            {
                switch (type)
                {
                    case "hello": OnHello(msg); break;
                    case "error": lastError = Str(msg, "message") ?? ""; break;
                    case "lobby": OnLobby(msg); break;
                    case "full":
                    case "over": OnSnapshot(msg); break;
                    case "tick": OnTick(msg); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            if (hud != null) hud.Refresh();
        }

        private void OnHello(JObject msg)
        {
            myId = OptInt(msg, "playerId");
            hostId = OptInt(msg, "hostId");
            tickMs = Num(msg, "tickMs", tickMs);
            string token = Str(msg, "token");
            if (!string.IsNullOrEmpty(token)) PlayerPrefs.SetString("territory-token", token);
        }

        private void NotePhase(string next, JObject msg)
        {
            string last = phase;
            if (last == "countdown" && next == "play" && sfx != null) sfx.PlayGo();
            if (last == "play" && next == "over" && sfx != null) sfx.PlayWinLose(ParseScores(msg["scores"] as JObject));
        }

        private void OnLobby(JObject msg)
        {
            if ((phase == "countdown" || phase == "play") && Str(msg, "phase") != "lobby") return;
            phase = Str(msg, "phase") ?? "lobby";
            hostId = OptInt(msg, "hostId");
            sessionWins = ParseWins(msg["wins"] as JObject);
            if (phase == "lobby")
            {
                model.Land = null;
                model.Trail = null;
                model.TrailCells.Clear();
            }
            if (hud != null) hud.ShowCard(ParseRoster(msg), phase == "over" ? ParseScores(msg["scores"] as JObject) : null);
        }

        private void OnSnapshot(JObject msg)
        {
            w = (int)Num(msg, "w", w);
            h = (int)Num(msg, "h", h);
            tickMs = Num(msg, "tickMs", tickMs);
            hostId = OptInt(msg, "hostId") ?? hostId;
            sessionWins = ParseWins(msg["wins"] as JObject);
            model.LoadSnapshot(w, h, Str(msg, "land") ?? "", Str(msg, "trail") ?? "");
            NotePhase(Str(msg, "phase") ?? "lobby", msg);
            phase = Str(msg, "phase") ?? phase;
            if (phase == "countdown" && board != null) board.ResetCamera();
            SyncBodies(msg);
            if (board != null) board.Rebuild();
            if (hud != null) hud.Paint(msg, model, myId, hostId, phase);
            if (phase == "over" && hud != null)
                hud.ShowCard(ParseRoster(msg), ParseScores(msg["scores"] as JObject));
            else if (hud != null) hud.HideCard();
        }

        private void OnTick(JObject msg)
        {
            if (model.Land == null) return;
            NotePhase(Str(msg, "phase") ?? phase, msg);
            phase = Str(msg, "phase") ?? phase;
            var runs = ParseIntArrays(msg["landRuns"] as JArray);
            var taken = model.ApplyRuns(runs);
            foreach (var kv in taken)
            {
                if (kv.Value.Count < 3) continue;
                double pct = BoardModel.CapturePct(kv.Value.Count, w, h);
                BodyState body = model.Bodies.TryGetValue(kv.Key, out var b) ? b : null;
                if (pct >= 1 && body != null && hud != null)
                    hud.Feed(ViewHelpers.CaptureFeedText(body.Name, pct), body.Color);
                if (sfx != null) sfx.PlayCapture(Math.Min(1, pct / 3), kv.Key == myId ? 1 : 0.35);
                if (fx != null) fx.ShowCapture(kv.Value, w, body);
            }
            model.ApplyTrail(ParseIntArrays(msg["trail"] as JArray));
            SyncBodies(msg);
            if (board != null) board.ApplyRuns(runs);
            if (hud != null) hud.Paint(msg, model, myId, hostId, phase);
            if (phase == "over" && hud != null)
                hud.ShowCard(ParseRoster(msg), ParseScores(msg["scores"] as JObject));
        }

        private void SyncBodies(JObject msg)
        {
            var arr = msg["players"] as JArray;
            if (arr == null) return;
            var list = new List<BodyState>();
            foreach (JObject p in arr) list.Add(ParseBody(p));
            var died = model.SyncPlayers(list, Time.unscaledTime * 1000, tickMs);
            foreach (var d in died)
            {
                if (hud != null)
                    hud.Feed(ViewHelpers.DeathFeedText(d.Name, d.OutReason, KillerName(d.OutBy)), "#ffffff");
                if (sfx != null) sfx.PlayDeath(d.Id == myId ? 1 : 0.4);
                if (fx != null) fx.ShowDeath(d);
            }
        }

        private string KillerName(int? id) =>
            id != null && model.Bodies.TryGetValue(id.Value, out var k) ? k.Name : null;

        // --- parsing helpers ---

        private static string Str(JObject o, string name) =>
            o.TryGetValue(name, out var v) && v.Type == JTokenType.String ? v.Value<string>() : null;

        private static double Num(JObject o, string name, double fallback)
        {
            if (o.TryGetValue(name, out var v) && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                return v.Value<double>();
            return fallback;
        }

        private static int? OptInt(JObject o, string name)
        {
            if (o.TryGetValue(name, out var v) && v.Type == JTokenType.Integer)
                return v.Value<int>();
            return null;
        }

        public static BodyState ParseBody(JObject p) => new BodyState
        {
            Id = p.Value<int>("id"),
            Name = p.Value<string>("name") ?? "",
            Color = p.Value<string>("color") ?? "#ffffff",
            ToX = p.Value<double>("x"),
            ToY = p.Value<double>("y"),
            Heading = p["heading"]?.Value<double>() ?? 0,
            Alive = p.Value<bool>("alive"),
            OutReason = (p["out"] as JObject)?.Value<string>("reason"),
            OutBy = (p["out"] as JObject)?["by"]?.Type == JTokenType.Integer
                ? (p["out"] as JObject).Value<int?>("by") : null,
            Land = p.Value<int>("land"),
            Kills = p.Value<int>("kills"),
        };

        private static List<int[]> ParseIntArrays(JArray arr)
        {
            var result = new List<int[]>();
            if (arr == null) return result;
            foreach (JArray row in arr)
            {
                var ints = new int[row.Count];
                for (int i = 0; i < row.Count; i += 1) ints[i] = row[i].Value<int>();
                result.Add(ints);
            }
            return result;
        }

        private static Dictionary<string, int> ParseWins(JObject o)
        {
            var wins = new Dictionary<string, int>();
            if (o == null) return wins;
            foreach (var prop in o.Properties()) wins[prop.Name] = prop.Value.Value<int>();
            return wins;
        }

        private static List<CardPlayer> ParseRoster(JObject msg)
        {
            var result = new List<CardPlayer>();
            var arr = msg["roster"] as JArray;
            if (arr == null) return result;
            foreach (JObject p in arr) result.Add(ToCard(p));
            return result;
        }

        private static CardScores ParseScores(JObject s)
        {
            if (s == null) return null;
            var scores = new CardScores
            {
                Tied = s.Value<bool>("tied"),
                WinnerId = s["winnerId"]?.Type == JTokenType.Integer ? s.Value<int?>("winnerId") : null,
            };
            foreach (JObject r in (s["list"] as JArray) ?? new JArray())
            {
                var card = ToCard(r);
                card.Pct = r["pct"]?.Value<double>() ?? 0;
                scores.List.Add(card);
            }
            return scores;
        }

        private static CardPlayer ToCard(JObject p) => new CardPlayer
        {
            Id = p.Value<int>("id"),
            Name = p.Value<string>("name") ?? "",
            Color = p.Value<string>("color") ?? "#ffffff",
            Alive = p["alive"]?.Value<bool>() ?? true,
            Bot = p["bot"]?.Value<bool>() ?? false,
            Level = p["level"]?.Type == JTokenType.String ? p.Value<string>("level") : null,
            Kills = p["kills"]?.Value<int>() ?? 0,
            Land = p["land"]?.Value<int>() ?? 0,
            OutReason = (p["out"] as JObject)?.Value<string>("reason"),
            OutBy = (p["out"] as JObject)?["by"]?.Type == JTokenType.Integer
                ? (p["out"] as JObject).Value<int?>("by") : null,
        };
    }
}
