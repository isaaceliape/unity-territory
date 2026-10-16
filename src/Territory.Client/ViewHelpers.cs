// Port of territory/public/view-helpers.js. Pure logic, no rendering:
// shared by the .NET client core and (later) the Unity scripts.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Territory.Client
{
    // Minimal client-side view of a player row (scores list or roster).
    public sealed class CardPlayer
    {
        public int Id;
        public string Name = "";
        public string Color = "";
        public bool Alive = true;
        public bool Bot;
        public string Level; // null for humans or old messages
        public int Kills;
        public int Land;
        public double Pct;
        public string OutReason;
        public int? OutBy;
    }

    public sealed class CardScores
    {
        public List<CardPlayer> List = new List<CardPlayer>();
        public bool Tied;
        public int? WinnerId;
    }

    public sealed class WallWarning
    {
        public string Side = "";
        public double Strength;
    }

    public static class ViewHelpers
    {
        public const int MaxBots = 3;

        public static bool IsEnded(string phase, CardScores scores) =>
            phase == "over" && scores != null;

        public static List<CardPlayer> ListedPlayers(string phase, CardScores scores, List<CardPlayer> roster) =>
            IsEnded(phase, scores) ? scores.List : (roster ?? new List<CardPlayer>());

        public static List<CardPlayer> PeopleForNick(string phase, CardScores scores, List<CardPlayer> roster) =>
            roster ?? ListedPlayers(phase, scores, roster);

        public static string WinnerText(CardScores scores)
        {
            if (scores?.WinnerId == null) return "Draw.";
            var winner = scores.List.Find(r => r.Id == scores.WinnerId.Value);
            return winner != null ? $"{winner.Name} wins." : "Round over.";
        }

        // Which walls the head is driving toward. Strength is 0 at `range`
        // cells away and 1 at the wall.
        public static List<WallWarning> WallWarnings(double x, double y, double heading, double w, double h, double range = 8)
        {
            double vx = Math.Cos(heading), vy = Math.Sin(heading);
            var sides = new[]
            {
                new { Side = "left", D = x, Toward = -vx },
                new { Side = "right", D = w - x, Toward = vx },
                new { Side = "top", D = y, Toward = -vy },
                new { Side = "bottom", D = h - y, Toward = vy },
            };
            var result = new List<WallWarning>();
            foreach (var s in sides)
            {
                if (s.D >= range || s.Toward < 0.2) continue;
                double closeness = 1 - Math.Max(0, s.D) / range;
                result.Add(new WallWarning
                {
                    Side = s.Side,
                    Strength = closeness * Math.Min(1, s.Toward * 1.4),
                });
            }
            return result;
        }

        public static string DeathFeedText(string who, string reason, string byName)
        {
            switch (reason)
            {
                case "cut": return byName != null ? $"{who} was cut by {byName}" : $"{who}'s line was cut";
                case "enclosed": return byName != null ? $"{byName} boxed in {who}" : $"{who} got boxed in";
                case "wall": return $"{who} hit the wall";
                case "crash": return $"{who} crashed";
                case "left": return $"{who} left the round";
                default: return $"{who} is out";
            }
        }

        public static string CaptureFeedText(string name, double pct) =>
            $"{name} took +{pct:F1}%";

        // True when an exposed line cell of another player is within range.
        public static bool IsDangerCell(int index, int w, int owner, int myId, double hx, double hy, double range = 10)
        {
            if (owner == 0 || owner == myId) return false;
            int x = index % w;
            int y = (index - x) / w;
            double dx = x + 0.5 - hx;
            double dy = y + 0.5 - hy;
            return dx * dx + dy * dy < range * range;
        }

        public static string WinsLabel(Dictionary<string, int> wins, string name)
        {
            int count = 0;
            if (wins != null) wins.TryGetValue((name ?? "").ToLowerInvariant(), out count);
            if (count == 0) return "";
            return count == 1 ? "1 win" : $"{count} wins";
        }

        // -1 left, 1 right, 0 for none or both held.
        public static int TurnFromHeld(bool left, bool right) =>
            (left ? -1 : 0) + (right ? 1 : 0);

        // Size of one map cell while following your own snake. Phones get
        // smaller cells so more of the map fits on screen.
        public static double FollowCellSize(double viewWidth) =>
            Math.Max(12, Math.Min(20, viewWidth / 26));

        public static int BotCount(List<CardPlayer> players) =>
            players == null ? 0 : players.Count(p => p.Bot);

        public static string BotLabel(string level) => $"{level ?? "normal"} bot";

        // Status line for your own death. Mirrors reasonText() in client.js.
        public static string ReasonText(string reason, string killerName)
        {
            switch (reason)
            {
                case "cut": return killerName != null ? $"Cut by {killerName}" : "Your line was cut";
                case "enclosed": return killerName != null ? $"{killerName} boxed you in" : "You got boxed in";
                case "wall": return "You hit the wall";
                case "crash": return "You crashed";
                case "left": return "You left the round";
                default: return "You are out";
            }
        }

        public static string FormatClock(double ms)
        {
            long total = Math.Max(0, (long)Math.Ceiling(ms / 1000));
            return $"{total / 60}:{(total % 60):D2}";
        }
    }
}
