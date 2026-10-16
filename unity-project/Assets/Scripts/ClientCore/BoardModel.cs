// Client-side board state ported from public/client.js: snapshot decode,
// run-length deltas, interpolated head poses, and scoreboard sorting.
// Rendering (canvas -> Unity) lives in Territory.Client.Unity.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Territory.Client
{
    // Interpolated head, mirroring the bodies map in client.js.
    public sealed class BodyState
    {
        public int Id;
        public string Name = "";
        public string Color = "";
        public double Heading;
        public bool Alive;
        public string OutReason;
        public int? OutBy;
        public int Land, Kills;
        public double FromX, FromY, ToX, ToY;
        public double At;
    }

    public sealed class BoardModel
    {
        public int W, H;
        public byte[] Land;
        public byte[] Trail;
        public readonly HashSet<int> TrailCells = new HashSet<int>();
        public readonly Dictionary<int, BodyState> Bodies = new Dictionary<int, BodyState>();

        public void LoadSnapshot(int w, int h, string landB64, string trailB64)
        {
            W = w; H = h;
            Land = Convert.FromBase64String(landB64);
            Trail = Convert.FromBase64String(trailB64);
            TrailCells.Clear();
            for (int i = 0; i < Trail.Length; i += 1)
                if (Trail[i] != 0) TrailCells.Add(i);
        }

        // Applies landRuns; returns newly-taken cells per owner so the caller
        // can celebrate captures. Mirrors applyRuns() + celebrateCapture().
        public Dictionary<int, List<int>> ApplyRuns(List<int[]> runs)
        {
            var taken = new Dictionary<int, List<int>>();
            foreach (var run in runs)
            {
                int start = run[0], len = run[1], value = run[2];
                if (value != 0)
                {
                    for (int i = start; i < start + len; i += 1)
                    {
                        if (Land[i] == value) continue;
                        if (!taken.TryGetValue(value, out var list))
                        {
                            list = new List<int>();
                            taken[value] = list;
                        }
                        list.Add(i);
                    }
                }
                for (int i = start; i < start + len; i += 1) Land[i] = (byte)value;
            }
            return taken;
        }

        public static double CapturePct(int cells, int w, int h) =>
            (cells / (double)(w * h)) * 100;

        public void ApplyTrail(List<int[]> pairs)
        {
            foreach (var p in pairs)
            {
                Trail[p[0]] = (byte)p[1];
                if (p[1] != 0) TrailCells.Add(p[0]);
                else TrailCells.Remove(p[0]);
            }
        }

        // Mirrors syncPlayers(): returns players that died on this update.
        public List<BodyState> SyncPlayers(IEnumerable<BodyState> list, double nowMs, double tickMs)
        {
            var seen = new HashSet<int>();
            var died = new List<BodyState>();
            foreach (var player in list)
            {
                seen.Add(player.Id);
                if (Bodies.TryGetValue(player.Id, out var prev))
                {
                    if (prev.Alive && !player.Alive) died.Add(player);
                    double cx = prev.ToX, cy = prev.ToY;
                    bool jump = Math.Sqrt((cx - player.ToX) * (cx - player.ToX)
                        + (cy - player.ToY) * (cy - player.ToY)) > 2;
                    player.FromX = jump ? player.ToX : cx;
                    player.FromY = jump ? player.ToY : cy;
                    player.At = nowMs;
                    Bodies[player.Id] = player;
                }
                else
                {
                    player.FromX = player.ToX;
                    player.FromY = player.ToY;
                    player.At = nowMs;
                    Bodies[player.Id] = player;
                }
            }
            foreach (int id in Bodies.Keys.ToList())
                if (!seen.Contains(id)) Bodies.Remove(id);
            return died;
        }

        public static void Pose(BodyState body, double nowMs, double tickMs, out double x, out double y)
        {
            double t = Math.Min(1, (nowMs - body.At) / tickMs);
            x = body.FromX + (body.ToX - body.FromX) * t;
            y = body.FromY + (body.ToY - body.FromY) * t;
        }

        public static int OwnerAt(byte[] land, byte[] trail, int w, int h, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return 0;
            int i = y * w + x;
            return trail[i] != 0 ? trail[i] : land[i];
        }

        // Scoreboard order: land desc, kills desc, name. Mirrors paintHud().
        public static List<BodyState> SortRows(IEnumerable<BodyState> rows) =>
            rows.OrderByDescending(r => r.Land)
                .ThenByDescending(r => r.Kills)
                .ThenBy(r => r.Name, StringComparer.Ordinal).ToList();

        public static string CountdownText(double msLeft) =>
            Math.Max(1, (long)Math.Ceiling(msLeft / 1000)).ToString();

        public string StatusText(int? myId, string phase)
        {
            if (myId != null && Bodies.TryGetValue(myId.Value, out var me)
                && !me.Alive && (phase == "play" || phase == "countdown"))
            {
                if (me.OutReason == null) return "Watcher";
                return $"Watcher. {ViewHelpers.ReasonText(me.OutReason, KillerName(me.OutBy))}";
            }
            if (myId == null && (phase == "play" || phase == "countdown")) return "Watching";
            return "";
        }

        private string KillerName(int? id) =>
            id != null && Bodies.TryGetValue(id.Value, out var k) ? k.Name : null;

        public static double[] HexToRgb(string hex)
        {
            int n = Convert.ToInt32(hex.Substring(1), 16);
            return new double[] { (n >> 16) & 255, (n >> 8) & 255, n & 255 };
        }
    }
}
