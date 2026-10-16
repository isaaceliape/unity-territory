// Wire protocol: the same JSON shapes as territory/server/index.js so the old
// Node client and this server stay interop-compatible during migration.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Territory.Sim;

namespace Territory.Server
{
    public sealed class UrlSet
    {
        public string Local = "";
        public List<string> Shared = new List<string>();
    }

    public static class Protocol
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions();

        public static string Encode(object message) => JsonSerializer.Serialize(message, JsonOptions);

        public static Dictionary<string, object> OutOf(OutInfo o) =>
            o == null ? null : new Dictionary<string, object> { { "reason", o.Reason }, { "by", o.By } };

        public static Dictionary<string, object> PlayerRow(PublicPlayer p) =>
            new Dictionary<string, object>
            {
                { "id", p.Id }, { "name", p.Name }, { "color", p.Color },
                { "x", p.X }, { "y", p.Y }, { "heading", p.Heading }, { "dir", p.Dir },
                { "alive", p.Alive }, { "bot", p.Bot }, { "level", p.Level },
                { "connected", p.Connected }, { "inRound", p.InRound },
                { "kills", p.Kills }, { "land", p.Land }, { "out", OutOf(p.Out) },
            };

        public static Dictionary<string, object> ScoreRowOf(ScoreRow r) =>
            new Dictionary<string, object>
            {
                { "id", r.Id }, { "name", r.Name }, { "color", r.Color },
                { "land", r.Land }, { "kills", r.Kills }, { "alive", r.Alive },
                { "out", OutOf(r.Out) }, { "pct", r.Pct },
            };

        public static Dictionary<string, object> ScoresOf(ScoreResult s) =>
            s == null ? null : new Dictionary<string, object>
            {
                { "list", s.List.Select(ScoreRowOf).ToList() },
                { "tied", s.Tied }, { "winnerId", s.WinnerId },
            };

        public static Dictionary<string, object> UrlsOf(UrlSet urls) =>
            new Dictionary<string, object> { { "local", urls.Local }, { "shared", urls.Shared } };

        public static Dictionary<string, object> Hello(string token, int? playerId, int? hostId, UrlSet urls, int tickMs) =>
            new Dictionary<string, object>
            {
                { "type", "hello" }, { "token", token }, { "playerId", playerId },
                { "hostId", hostId }, { "urls", UrlsOf(urls) }, { "tickMs", tickMs },
            };

        public static Dictionary<string, object> Lobby(
            Dictionary<string, int> wins, string phase, int? hostId,
            List<Dictionary<string, object>> roster, Dictionary<string, object> scores, UrlSet urls) =>
            new Dictionary<string, object>
            {
                { "type", "lobby" }, { "wins", wins }, { "phase", phase },
                { "hostId", hostId }, { "roster", roster },
                { "scores", scores }, { "urls", UrlsOf(urls) },
            };

        public static Dictionary<string, object> Snapshot(
            string type, Dictionary<string, int> wins, Game game, TickResult msg,
            List<Dictionary<string, object>> players, List<Dictionary<string, object>> roster,
            double nowMs) =>
            new Dictionary<string, object>
            {
                { "type", type }, { "wins", wins }, { "phase", msg.Phase },
                { "msLeft", game.MsLeftAt(nowMs) }, { "hostId", game.HostId },
                { "tick", game.TickCount }, { "tickMs", game.TickMs },
                { "w", game.W }, { "h", game.H },
                { "players", players }, { "roster", roster },
                { "scores", ScoresOf(msg.Scores) },
                { "land", Convert.ToBase64String(game.Land) },
                { "trail", Convert.ToBase64String(game.Trail) },
            };

        public static Dictionary<string, object> Delta(
            Game game, TickResult msg, List<Dictionary<string, object>> players, double nowMs) =>
            new Dictionary<string, object>
            {
                { "type", "tick" }, { "phase", msg.Phase },
                { "msLeft", game.MsLeftAt(nowMs) }, { "hostId", game.HostId },
                { "tick", game.TickCount }, { "tickMs", game.TickMs },
                { "players", players },
                { "landRuns", game.LandRuns() },
                { "trail", game.TrailWrites() },
                { "scores", ScoresOf(msg.Scores) },
            };

        public static Dictionary<string, object> Error(string message) =>
            new Dictionary<string, object> { { "type", "error" }, { "message", message } };
    }
}
