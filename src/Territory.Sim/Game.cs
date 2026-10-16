// Pure C# port of territory/server/game.js. No UnityEngine dependency:
// EditMode tests and plain `dotnet test` both run it. Phase is one of
// "lobby", "countdown", "play", "over". Wire/JSON serialization lives in
// Territory.Net (Phase 2), not here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Territory.Sim
{
    public sealed class BotConfig
    {
        public int HomeAt;
        public double Noise;
        public bool Hunt;
        public int Rest;
        public BotConfig(int homeAt, double noise, bool hunt, int rest)
        {
            HomeAt = homeAt; Noise = noise; Hunt = hunt; Rest = rest;
        }
    }

    public static class Defs
    {
        public static readonly string[] Colors = new[]
        {
            "#ff2a6d", "#ff6a00", "#ffe600", "#39ff14", "#00f5d4", "#00d4ff",
            "#3d7bff", "#b44dff", "#ff4fd8", "#ff4d4d", "#7af0ff", "#c6ff4a",
        };
        public static readonly int[] Spread = new[] { 0, 11, 3, 8, 5, 6, 1, 10, 4, 7, 2, 9 };
        public static readonly HashSet<int> Minutes = new HashSet<int> { 1, 2, 3, 5 };
        public const int MaxBots = 3;
        public const double TurnRate = Math.PI * 1.35;

        // How each bot level plays. Same numbers as BOT_LEVELS in game.js:
        //   HomeAt: trail length before it turns back toward its own land.
        //   Noise:  random wobble added to each candidate heading.
        //   Hunt:   steers toward other players' exposed lines to cut them.
        //   Rest:   ticks it idles on its own land before each trip out.
        public static readonly Dictionary<string, BotConfig> BotLevels =
            new Dictionary<string, BotConfig>
            {
                { "easy", new BotConfig(homeAt: 12, noise: 14, hunt: false, rest: 60) },
                { "normal", new BotConfig(homeAt: 16, noise: 0, hunt: false, rest: 0) },
                { "hard", new BotConfig(homeAt: 28, noise: 0, hunt: true, rest: 0) },
            };
    }

    public sealed class OutInfo
    {
        public string Reason;
        public int? By;
        public OutInfo(string reason, int? by) { Reason = reason; By = by; }
    }

    public sealed class Player
    {
        public int Id;
        public string Name = "";
        public string Color = "";
        public double X, Y;
        public double Heading;
        public int Turn;
        public double? Aim;
        public string Dir = "right";
        public bool Alive;
        public bool Bot;
        public string Level; // null for humans
        public bool Connected = true;
        public bool InRound;
        public bool OnOwn;
        public int LandCount;
        public int Kills;
        public List<int> TrailCells = new List<int>();
        public OutInfo Out;
        // Bot steering scratch state (mirrors the ad-hoc JS fields).
        public double? BotTurn;
        public long? RestUntil;
    }

    public sealed class PublicPlayer
    {
        public int Id;
        public string Name;
        public string Color;
        public double X, Y, Heading;
        public string Dir;
        public bool Alive, Bot, Connected, InRound;
        public string Level;
        public int Kills, Land;
        public OutInfo Out;
    }

    public sealed class ScoreRow
    {
        public int Id;
        public string Name;
        public string Color;
        public int Land, Kills;
        public bool Alive;
        public OutInfo Out;
        public double Pct;
    }

    public sealed class ScoreResult
    {
        public List<ScoreRow> List = new List<ScoreRow>();
        public bool Tied;
        public int? WinnerId;
    }

    public sealed class TickResult
    {
        public string Phase;
        public ScoreResult Scores;
        public bool Resync;
    }

    public sealed class AddBotResult
    {
        public bool Ok;
        public string Error = "";
        public int Id;
        public string Level;
    }

    public sealed class SimpleResult
    {
        public bool Ok;
        public string Error = "";
    }

    public sealed class RenameResult
    {
        public bool Ok;
        public string Error = "";
        public string Name = "";
        public bool Changed;
    }

    public sealed class StartResult
    {
        public bool Ok;
        public string Error = "";
    }

    public sealed class GameOptions
    {
        public int W = 168;
        public int H = 126;
        public int TickMs = 110;
        public int StartSize = 10;
        public int CountdownMs = 3000;
        public Func<double> Random;
    }

    internal sealed class MoveState
    {
        public Player Player;
        public double Nx, Ny;
        public bool Out;
        public List<int> Cells = new List<int>();
    }

    public sealed class Game
    {
        public readonly int W, H;
        public readonly int TickMs;
        public readonly byte[] Land;
        public readonly byte[] Trail;

        private readonly int _startSize;
        private readonly int _countdownMs;
        private readonly Func<double> _random;
        private const int MaxPlayers = 12;

        private readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        private readonly List<Player> _order = new List<Player>();

        // Per-tick write sets, mirroring landWrites/trailWrites in game.js.
        // The server turns these into landRuns deltas and full snapshots.
        private readonly Dictionary<int, byte> _landWrites = new Dictionary<int, byte>();
        private readonly Dictionary<int, byte> _trailWrites = new Dictionary<int, byte>();

        private string _phase = "lobby";
        private int? _hostId;
        private double _durationMs = 180000;
        private double _endsAt;
        private double _countdownRemaining;
        private long _tickCount;
        private double _nowMs;

        public string Phase => _phase;
        public int? HostId => _hostId;
        public double DurationMs => _durationMs;
        public long TickCount => _tickCount;

        public double MsLeftAt(double nowMs) =>
            _phase == "countdown" ? _countdownRemaining
            : _phase == "play" ? Math.Max(0, _endsAt - nowMs) : 0;

        public Game(GameOptions options = null)
        {
            var o = options ?? new GameOptions();
            W = o.W; H = o.H; TickMs = o.TickMs;
            _startSize = o.StartSize;
            _countdownMs = o.CountdownMs;
            _random = o.Random ?? new Func<double>(() => new Random().NextDouble());
            Land = new byte[W * H];
            Trail = new byte[W * H];
        }

        private int Idx(int x, int y) => y * W + x;

        private void SetLand(int i, int id)
        {
            int prev = Land[i];
            if (prev == id) return;
            if (prev != 0 && _players.TryGetValue(prev, out var oldOwner)) oldOwner.LandCount -= 1;
            if (id != 0 && _players.TryGetValue(id, out var newOwner)) newOwner.LandCount += 1;
            Land[i] = (byte)id;
            _landWrites[i] = (byte)id;
        }

        private void SetTrail(int i, int id)
        {
            if (Trail[i] == id) return;
            Trail[i] = (byte)id;
            _trailWrites[i] = (byte)id;
        }

        public void ClearPatch()
        {
            _landWrites.Clear();
            _trailWrites.Clear();
        }

        public List<int[]> LandRuns()
        {
            var keys = _landWrites.Keys.ToList();
            keys.Sort();
            var runs = new List<int[]>();
            foreach (int i in keys)
            {
                int value = _landWrites[i];
                if (runs.Count > 0)
                {
                    var last = runs[runs.Count - 1];
                    if (last[2] == value && last[0] + last[1] == i) { last[1] += 1; continue; }
                }
                runs.Add(new[] { i, 1, value });
            }
            return runs;
        }

        public List<int[]> TrailWrites() =>
            _trailWrites.OrderBy(kv => kv.Key).Select(kv => new[] { kv.Key, (int)kv.Value }).ToList();

        private static string CleanName(string raw)
        {
            var s = Regex.Replace(raw ?? "", "[\\u0000-\\u001F\\u007F]", "");
            s = Regex.Replace(s, "\\s+", " ");
            s = s.Trim();
            if (s.Length > 16) s = s.Substring(0, 16);
            return s.Length == 0 ? "Player" : s;
        }

        private string UniqueName(string name, int exceptId)
        {
            var taken = new HashSet<string>();
            foreach (var p in _order) if (p.Id != exceptId) taken.Add(p.Name);
            if (!taken.Contains(name)) return name;
            int n = 2;
            while (taken.Contains($"{name} {n}")) n += 1;
            return $"{name} {n}";
        }

        private int NextId()
        {
            var used = new HashSet<int>(_players.Keys);
            int id = 1;
            while (used.Contains(id)) id += 1;
            return id;
        }

        public Player AddPlayer(string name)
        {
            if (_players.Count >= MaxPlayers) return null;
            int id = NextId();
            var player = new Player
            {
                Id = id,
                Name = UniqueName(CleanName(name), -1),
                Color = Defs.Colors[id - 1],
            };
            _players[id] = player;
            _order.Add(player);
            if (_hostId == null && !player.Bot) _hostId = id;
            return player;
        }

        public Player GetPlayer(int id) => _players.TryGetValue(id, out var p) ? p : null;

        private void ElectHost()
        {
            if (_hostId != null && _players.TryGetValue(_hostId.Value, out var cur)
                && cur.Connected && !cur.Bot) return;
            _hostId = null;
            foreach (var p in _order)
            {
                if (p.Connected && !p.Bot) { _hostId = p.Id; return; }
            }
        }

        public static PublicPlayer ToPublic(Player p) => new PublicPlayer
        {
            Id = p.Id, Name = p.Name, Color = p.Color, X = p.X, Y = p.Y,
            Heading = p.Heading, Dir = p.Dir, Alive = p.Alive, Bot = p.Bot,
            Level = p.Bot ? p.Level : null, Connected = p.Connected,
            InRound = p.InRound, Kills = p.Kills, Land = p.LandCount, Out = p.Out,
        };

        public List<PublicPlayer> Roster() => _order.Select(ToPublic).ToList();

        public ScoreResult Scores()
        {
            var list = _order
                .Where(p => p.InRound)
                .Select(p => new ScoreRow
                {
                    Id = p.Id, Name = p.Name, Color = p.Color, Land = p.LandCount,
                    Kills = p.Kills, Alive = p.Alive, Out = p.Out,
                    Pct = (p.LandCount / (double)(W * H)) * 100,
                }).ToList();
            list.Sort((a, b) =>
            {
                int c = b.Land.CompareTo(a.Land);
                if (c != 0) return c;
                c = b.Kills.CompareTo(a.Kills);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            var result = new ScoreResult { List = list };
            var living = list.Where(r => r.Alive).ToList();
            if (living.Count == 1 && list.Count > 1)
            {
                result.Tied = false;
                result.WinnerId = living[0].Id;
                return result;
            }
            var top = list.Count > 0 ? list[0] : null;
            result.Tied = top != null &&
                list.Count(r => r.Land == top.Land && r.Kills == top.Kills) > 1;
            result.WinnerId = top != null && !result.Tied && top.Land > 0 ? (int?)top.Id : null;
            return result;
        }

        private static string LossReason(ScoreRow row, List<ScoreRow> list)
        {
            string who = null;
            if (row.Out?.By != null)
            {
                var killer = list.Find(o => o.Id == row.Out.By.Value);
                who = killer?.Name;
            }
            switch (row.Out?.Reason)
            {
                case "cut": return who != null ? $"cut by {who}" : "line was cut";
                case "wall": return "hit the wall";
                case "crash": return "crashed into another player";
                case "enclosed": return who != null ? $"boxed in by {who}" : "got boxed in";
                case "left": return "left the round";
                default: return "out";
            }
        }

        public string ResultLog()
        {
            var result = Scores();
            var lines = new List<string>();
            if (result.Tied || result.WinnerId == null) lines.Add("Draw");
            else lines.Add($"Winner: {result.List.Find(r => r.Id == result.WinnerId.Value).Name}");
            foreach (var row in result.List)
            {
                string share = $"{row.Pct:F1}%";
                if (!result.Tied && row.Id == result.WinnerId)
                {
                    lines.Add($"{row.Name} won with {share}");
                    continue;
                }
                string why = row.Alive ? "less territory" : LossReason(row, result.List);
                lines.Add($"{row.Name} lost with {share}, {why}");
            }
            return string.Join("\n", lines);
        }

        private void Kill(Player player, string reason, Player killer)
        {
            if (player == null || !player.Alive) return;
            player.Alive = false;
            player.OnOwn = false;
            player.Out = new OutInfo(reason,
                killer != null && killer.Id != player.Id ? (int?)killer.Id : null);
            if (killer != null && killer.Id != player.Id) killer.Kills += 1;
            for (int k = 0; k < player.TrailCells.Count; k++)
            {
                int i = player.TrailCells[k];
                if (Trail[i] == player.Id) SetTrail(i, 0);
            }
            player.TrailCells.Clear();
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private void FloodOutside(int ownerId, byte[] seen, HashSet<int> walls)
        {
            var queue = new List<int>();
            int head = 0;
            Action<int> push = (i) =>
            {
                if (seen[i] != 0 || Land[i] == ownerId || (walls != null && walls.Contains(i))) return;
                seen[i] = 1;
                queue.Add(i);
            };
            for (int x = 0; x < W; x += 1) { push(x); push((H - 1) * W + x); }
            for (int y = 0; y < H; y += 1) { push(y * W); push(y * W + W - 1); }
            while (head < queue.Count)
            {
                int i = queue[head]; head += 1;
                int x = i % W;
                int y = (i - x) / W;
                if (x > 0) push(i - 1);
                if (x < W - 1) push(i + 1);
                if (y > 0) push(i - W);
                if (y < H - 1) push(i + W);
            }
        }

        private void Capture(Player player)
        {
            var trailCells = new List<int>(player.TrailCells);
            var walls = new HashSet<int>(trailCells);
            var withTrail = new byte[W * H];
            var beforeTrail = new byte[W * H];
            FloodOutside(player.Id, withTrail, walls);
            FloodOutside(player.Id, beforeTrail, null);

            var cut = new HashSet<int>();
            for (int i = 0; i < Land.Length; i += 1)
            {
                if (walls.Contains(i) || Land[i] == player.Id) continue;
                if (withTrail[i] != 0 || beforeTrail[i] == 0) continue;
                int trailOwner = Trail[i];
                if (trailOwner != 0 && trailOwner != player.Id && Land[i] != trailOwner)
                    cut.Add(trailOwner);
                if (trailOwner != 0) SetTrail(i, 0);
                SetLand(i, player.Id);
            }

            foreach (int i in trailCells)
            {
                if (Trail[i] == player.Id) SetTrail(i, 0);
                SetLand(i, player.Id);
            }
            player.TrailCells.Clear();

            foreach (int id in cut)
            {
                var victim = GetPlayer(id);
                if (victim != null && victim.Alive) Kill(victim, "cut", player);
            }
            foreach (var other in _order)
            {
                if (!other.Alive || other.Id == player.Id || !other.InRound) continue;
                int head = Idx((int)Math.Floor(other.X), (int)Math.Floor(other.Y));
                if (Land[head] == player.Id) Kill(other, "enclosed", player);
            }
        }

        private void Place(Player player, int slot)
        {
            int zoneW = W / 4;
            int zoneH = H / 3;
            int size = Math.Min(_startSize, Math.Min(zoneW - 2, zoneH - 2));
            int col = slot % 4;
            int row = slot / 4;
            int bx = col * zoneW + (zoneW - size) / 2;
            int by = row * zoneH + (zoneH - size) / 2;
            for (int y = by; y < by + size; y += 1)
                for (int x = bx; x < bx + size; x += 1)
                    SetLand(Idx(x, y), player.Id);
            player.X = bx + size / 2 + 0.5;
            player.Y = by + size / 2 + 0.5;
            player.OnOwn = true;
            player.Heading = Math.Atan2(H / 2.0 - player.Y, W / 2.0 - player.X);
            player.Turn = 0;
            player.Aim = null;
            player.Dir = "right";
        }

        private void ResetBoard()
        {
            Array.Clear(Land, 0, Land.Length);
            Array.Clear(Trail, 0, Trail.Length);
            foreach (var p in _order)
            {
                p.LandCount = 0; p.Kills = 0; p.TrailCells.Clear(); p.Out = null;
                p.Alive = false; p.InRound = false; p.OnOwn = false; p.X = 0; p.Y = 0;
            }
        }

        private static double WrapAngle(double angle)
        {
            double v = angle;
            while (v > Math.PI) v -= Math.PI * 2;
            while (v < -Math.PI) v += Math.PI * 2;
            return v;
        }

        private void ApplyHeading(Player player, double dt)
        {
            if (player.Aim != null && IsFinite(player.Aim.Value))
            {
                double max = Defs.TurnRate * dt;
                double d = WrapAngle(player.Aim.Value - player.Heading);
                player.Heading += Math.Max(-max, Math.Min(max, d));
                return;
            }
            if (player.Turn != 0) player.Heading += player.Turn * Defs.TurnRate * dt;
        }

        private void CellsCrossed(double x0, double y0, double x1, double y1,
            out bool oob, List<int> cells)
        {
            oob = false;
            cells.Clear();
            double dist = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int samples = Math.Max(1, (int)Math.Ceiling(dist / 0.2));
            int last = Idx((int)Math.Floor(x0), (int)Math.Floor(y0));
            for (int s = 1; s <= samples; s += 1)
            {
                double t = s / (double)samples;
                double x = x0 + (x1 - x0) * t;
                double y = y0 + (y1 - y0) * t;
                int cx = (int)Math.Floor(x);
                int cy = (int)Math.Floor(y);
                if (cx < 0 || cy < 0 || cx >= W || cy >= H) { oob = true; return; }
                int cell = Idx(cx, cy);
                if (cell == last) continue;
                int prevX = last % W;
                int prevY = (last - prevX) / W;
                if (Math.Abs(cx - prevX) + Math.Abs(cy - prevY) == 2)
                {
                    int bridge = Idx(cx, prevY);
                    if (bridge != last) cells.Add(bridge);
                }
                cells.Add(cell);
                last = cell;
            }
        }

        public AddBotResult AddBot(string level = "normal")
        {
            if (!Defs.BotLevels.ContainsKey(level))
                return new AddBotResult { Ok = false, Error = "Pick easy, normal or hard." };
            int bots = _order.Count(p => p.Bot);
            if (bots >= Defs.MaxBots)
                return new AddBotResult { Ok = false, Error = $"The room can have up to {Defs.MaxBots} bots." };
            if (_phase == "play" || _phase == "countdown")
                return new AddBotResult { Ok = false, Error = "Wait until the round is over." };
            var player = AddPlayer("Bot");
            if (player == null) return new AddBotResult { Ok = false, Error = "The room is full." };
            player.Bot = true;
            player.Level = level;
            return new AddBotResult { Ok = true, Id = player.Id, Level = level };
        }

        public SimpleResult RemoveBot(int id)
        {
            var player = GetPlayer(id);
            if (player == null || !player.Bot)
                return new SimpleResult { Ok = false, Error = "That is not a bot." };
            if (_phase == "play" || _phase == "countdown")
                return new SimpleResult { Ok = false, Error = "Wait until the round is over." };
            RemovePlayer(id);
            return new SimpleResult { Ok = true };
        }

        public RenameResult Rename(int id, string raw)
        {
            var player = GetPlayer(id);
            if (player == null) return new RenameResult { Ok = false, Error = "You are not in the room." };
            if (_phase != "lobby" && _phase != "over")
                return new RenameResult { Ok = false, Error = "You can change your name in the waiting room." };
            string cleaned = CleanName(raw);
            var taken = new HashSet<string>();
            foreach (var o in _order) if (o.Id != id) taken.Add(o.Name);
            string name = cleaned;
            if (taken.Contains(name))
            {
                int n = 2;
                while (taken.Contains($"{name} {n}")) n += 1;
                name = $"{name} {n}";
            }
            if (name == player.Name) return new RenameResult { Ok = true, Name = name, Changed = false };
            player.Name = name;
            return new RenameResult { Ok = true, Name = name, Changed = true };
        }

        public void RemovePlayer(int id)
        {
            var player = GetPlayer(id);
            if (player == null) return;
            player.Connected = false;
            if (_phase == "lobby" || _phase == "over")
            {
                for (int i = 0; i < Land.Length; i += 1)
                    if (Land[i] == id) SetLand(i, 0);
                foreach (int i in player.TrailCells)
                    if (Trail[i] == id) SetTrail(i, 0);
                _players.Remove(id);
                _order.Remove(player);
            }
            else if (player.Alive) Kill(player, "left", null);
            ElectHost();
        }

        public void MarkConnected(int id)
        {
            var player = GetPlayer(id);
            if (player == null) return;
            player.Connected = true;
            if (_hostId == null) { ElectHost(); return; }
            if (!_players.TryGetValue(_hostId.Value, out var host) || !host.Connected) ElectHost();
        }

        private static readonly Dictionary<string, double> FacingAngles =
            new Dictionary<string, double>
            {
                { "right", 0 }, { "down", Math.PI / 2 }, { "left", Math.PI }, { "up", -Math.PI / 2 },
            };

        public void SetDirection(int id, string dir)
        {
            var player = GetPlayer(id);
            if (player == null || !FacingAngles.ContainsKey(dir)) return;
            if (!player.Alive && _phase == "play") return;
            player.Heading = FacingAngles[dir];
            player.Turn = 0;
            player.Aim = null;
        }

        public void SetSteer(int id, double turn)
        {
            var player = GetPlayer(id);
            if (player == null) return;
            player.Turn = turn < 0 ? -1 : turn > 0 ? 1 : 0;
            player.Aim = null;
        }

        public void SetAim(int id, double angle)
        {
            var player = GetPlayer(id);
            if (player == null || !IsFinite(angle)) return;
            player.Aim = angle;
            player.Turn = 0;
        }

        public void Vacate()
        {
            if (_phase == "play" || _phase == "countdown") return;
            foreach (var p in _order.ToList())
                if (!p.Connected) { _players.Remove(p.Id); _order.Remove(p); }
            ElectHost();
        }

        public SimpleResult ReturnToLobby(int byId)
        {
            if (byId != _hostId) return new SimpleResult { Ok = false, Error = "Only the host can restart." };
            if (_phase == "lobby") return new SimpleResult { Ok = false, Error = "The round has not started." };
            ResetBoard();
            _phase = "lobby";
            _endsAt = 0;
            _countdownRemaining = 0;
            _tickCount = 0;
            return new SimpleResult { Ok = true };
        }

        public StartResult Start(int byId, int minutes, double nowMs)
        {
            if (byId != _hostId) return new StartResult { Ok = false, Error = "Only the host can start." };
            foreach (var p in _order.ToList())
                if (!p.Connected) { _players.Remove(p.Id); _order.Remove(p); }
            ElectHost();
            if (_hostId == null) return new StartResult { Ok = false, Error = "Nobody is here." };
            if (!_order.Any(p => p.Connected))
                return new StartResult { Ok = false, Error = "Nobody is here." };

            int chosen = Defs.Minutes.Contains(minutes) ? minutes : 3;
            _durationMs = chosen * 60 * 1000;
            _nowMs = nowMs;
            ResetBoard();
            _tickCount = 0;

            var seated = _order.Where(p => p.Connected).ToList();
            for (int index = 0; index < seated.Count; index += 1)
            {
                var player = seated[index];
                player.Alive = true;
                player.InRound = true;
                player.Connected = true;
                int slot = Defs.Spread.Length > 0 ? Defs.Spread[index % Defs.Spread.Length] : index;
                Place(player, slot);
            }

            if (_countdownMs <= 0)
            {
                _phase = "play";
                _countdownRemaining = 0;
                _endsAt = _nowMs + _durationMs;
            }
            else
            {
                _phase = "countdown";
                _countdownRemaining = _countdownMs;
                _endsAt = 0;
            }
            ClearPatch();
            return new StartResult { Ok = true };
        }

        private int[] NearestOwn(Player player, out bool found)
        {
            int cx = (int)Math.Floor(player.X);
            int cy = (int)Math.Floor(player.Y);
            int bestX = 0, bestY = 0;
            double bestDist = double.PositiveInfinity;
            found = false;
            const int reach = 24;
            for (int y = Math.Max(0, cy - reach); y <= Math.Min(H - 1, cy + reach); y += 1)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(W - 1, cx + reach); x += 1)
                {
                    if (Land[Idx(x, y)] != player.Id) continue;
                    double dx = x + 0.5 - player.X;
                    double dy = y + 0.5 - player.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; bestX = x; bestY = y; found = true; }
                }
            return new[] { bestX, bestY };
        }

        private int[] NearestForeignTrail(Player player, int reach, out bool found)
        {
            int cx = (int)Math.Floor(player.X);
            int cy = (int)Math.Floor(player.Y);
            int bestX = 0, bestY = 0;
            double bestDist = double.PositiveInfinity;
            found = false;
            for (int y = Math.Max(0, cy - reach); y <= Math.Min(H - 1, cy + reach); y += 1)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(W - 1, cx + reach); x += 1)
                {
                    int cell = Idx(x, y);
                    int owner = Trail[cell];
                    if (owner == 0 || owner == player.Id || Land[cell] == owner) continue;
                    double dx = x + 0.5 - player.X;
                    double dy = y + 0.5 - player.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; bestX = x; bestY = y; found = true; }
                }
            return new[] { bestX, bestY };
        }

        private double Clearance(Player player, double angle, bool ignoreForeign)
        {
            int head = Idx((int)Math.Floor(player.X), (int)Math.Floor(player.Y));
            var recent = new HashSet<int>(player.TrailCells.Skip(Math.Max(0, player.TrailCells.Count - 5)));
            for (double dist = 1.4; dist <= 18; dist += 0.9)
            {
                double x = player.X + Math.Cos(angle) * dist;
                double y = player.Y + Math.Sin(angle) * dist;
                int cx = (int)Math.Floor(x);
                int cy = (int)Math.Floor(y);
                if (cx < 1 || cy < 1 || cx >= W - 1 || cy >= H - 1) return dist;
                int cell = Idx(cx, cy);
                if (cell == head || recent.Contains(cell)) continue;
                if (Trail[cell] != 0 && !(ignoreForeign && Trail[cell] != player.Id)) return dist;
            }
            return 20;
        }

        private bool WouldCrossOwnTrail(Player player, double angle)
        {
            double dt = TickMs / 1000.0;
            double maxTurn = Defs.TurnRate * dt;
            var recent = new HashSet<int>(player.TrailCells.Skip(Math.Max(0, player.TrailCells.Count - 4)));
            double heading = player.Heading;
            double x = player.X, y = player.Y;
            var cells = new List<int>();
            for (int step = 0; step < 5; step += 1)
            {
                heading += Math.Max(-maxTurn, Math.Min(maxTurn, WrapAngle(angle - heading)));
                double nx = x + Math.Cos(heading);
                double ny = y + Math.Sin(heading);
                bool oob;
                CellsCrossed(x, y, nx, ny, out oob, cells);
                if (oob) return true;
                if (cells.Any(c => Trail[c] == player.Id && !recent.Contains(c))) return true;
                x = nx; y = ny;
            }
            return false;
        }

        private double[] OwnCentroid(Player player)
        {
            int cx = (int)Math.Floor(player.X);
            int cy = (int)Math.Floor(player.Y);
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = Math.Max(0, cy - 12); y <= Math.Min(H - 1, cy + 12); y += 1)
                for (int x = Math.Max(0, cx - 12); x <= Math.Min(W - 1, cx + 12); x += 1)
                {
                    if (Land[Idx(x, y)] != player.Id) continue;
                    sx += x + 0.5; sy += y + 0.5; count += 1;
                }
            if (count == 0) return new[] { player.X, player.Y };
            return new[] { sx / count, sy / count };
        }

        private void SteerBots()
        {
            foreach (var player in _order)
            {
                if (!player.Bot || !player.Alive || !player.InRound) continue;
                if (player.BotTurn == null) player.BotTurn = player.Id % 2 == 0 ? 1 : -1;
                var config = Defs.BotLevels.ContainsKey(player.Level)
                    ? Defs.BotLevels[player.Level] : Defs.BotLevels["normal"];
                if (player.TrailCells.Count > 0) player.RestUntil = null;
                else if (config.Rest != 0 && player.OnOwn)
                {
                    if (player.RestUntil == null) player.RestUntil = _tickCount + config.Rest;
                    if (_tickCount < player.RestUntil)
                    {
                        player.Turn = 1;
                        player.Aim = null;
                        continue;
                    }
                }
                double preferred;
                if (player.OnOwn && player.TrailCells.Count == 0)
                {
                    var center = OwnCentroid(player);
                    preferred = Math.Atan2(player.Y - center[1], player.X - center[0]);
                }
                else if (player.TrailCells.Count > config.HomeAt)
                {
                    bool found;
                    var home = NearestOwn(player, out found);
                    preferred = found
                        ? Math.Atan2(home[1] + 0.5 - player.Y, home[0] + 0.5 - player.X)
                        : player.Heading + player.BotTurn.Value * 0.15;
                }
                else
                {
                    bool hasPrey = false;
                    int[] prey = null;
                    if (config.Hunt && player.TrailCells.Count > 10)
                    {
                        prey = NearestForeignTrail(player, 25, out hasPrey);
                    }
                    preferred = hasPrey
                        ? Math.Atan2(prey[1] + 0.5 - player.Y, prey[0] + 0.5 - player.X)
                        : player.Heading + player.BotTurn.Value * 0.15;
                }
                double bestAngle = player.Heading;
                double bestScore = double.NegativeInfinity;
                for (int step = -8; step <= 8; step += 1)
                {
                    double angle = player.Heading + step * 0.22;
                    double room = Clearance(player, angle, config.Hunt);
                    double turn = Math.Abs(WrapAngle(angle - preferred));
                    bool crosses = WouldCrossOwnTrail(player, angle);
                    double wobble = config.Noise != 0 ? (_random() - 0.5) * config.Noise : 0;
                    double score = room * 5 - turn - (room < 9 ? 200 : 0) - (crosses ? 500 : 0) + wobble;
                    if (score > bestScore) { bestScore = score; bestAngle = angle; }
                }
                double diff = WrapAngle(bestAngle - player.Heading);
                if (Math.Abs(diff) > 0.08) player.BotTurn = diff > 0 ? 1 : -1;
                player.Aim = bestAngle;
                player.Turn = 0;
            }
        }

        private int VulnerableOwner(int cell)
        {
            int owner = Trail[cell];
            if (owner == 0 || Land[cell] == owner) return 0;
            return owner;
        }

        private void Step()
        {
            SteerBots();
            double dt = TickMs / 1000.0;
            double distance = dt * (1000.0 / TickMs);
            var moving = _order.Where(p => p.InRound && p.Alive && p.Connected).ToList();
            foreach (var p in moving) ApplyHeading(p, dt);

            var moves = new List<MoveState>();
            foreach (var p in moving)
            {
                var m = new MoveState { Player = p };
                m.Nx = p.X + Math.Cos(p.Heading) * distance;
                m.Ny = p.Y + Math.Sin(p.Heading) * distance;
                bool oob; var cells = new List<int>();
                CellsCrossed(p.X, p.Y, m.Nx, m.Ny, out oob, cells);
                m.Out = oob; m.Cells = cells;
                moves.Add(m);
            }

            var doomed = new Dictionary<int, Tuple<string, Player>>();
            Action<Player, string, Player> doom = (player, reason, killer) =>
            {
                if (player == null || !player.Alive || doomed.ContainsKey(player.Id)) return;
                doomed[player.Id] = Tuple.Create(reason, killer);
            };

            foreach (var m in moves)
                if (m.Out) doom(m.Player, "wall", null);

            var groups = new Dictionary<int, List<MoveState>>();
            foreach (var m in moves)
            {
                if (doomed.ContainsKey(m.Player.Id) || m.Out) continue;
                int key = Idx((int)Math.Floor(m.Nx), (int)Math.Floor(m.Ny));
                if (!groups.ContainsKey(key)) groups[key] = new List<MoveState>();
                groups[key].Add(m);
            }
            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                int head = Idx((int)Math.Floor(group[0].Nx), (int)Math.Floor(group[0].Ny));
                int trailOwner = VulnerableOwner(head);
                if (trailOwner != 0) doom(GetPlayer(trailOwner), "cut", group[0].Player);
                foreach (var m in group) doom(m.Player, "crash", null);
            }

            var selfClosed = new HashSet<int>();
            foreach (var m in moves)
            {
                if (doomed.ContainsKey(m.Player.Id)) continue;
                foreach (int cell in m.Cells)
                {
                    int trailOwner = VulnerableOwner(cell);
                    if (trailOwner == 0) continue;
                    if (trailOwner == m.Player.Id) selfClosed.Add(m.Player.Id);
                    else doom(GetPlayer(trailOwner), "cut", m.Player);
                }
            }

            foreach (var kv in doomed) Kill(GetPlayer(kv.Key), kv.Value.Item1, kv.Value.Item2);

            var returning = new List<Player>();
            foreach (var m in moves)
            {
                var player = m.Player;
                if (!player.Alive || m.Out) continue;
                bool outside = !player.OnOwn;
                foreach (int cell in m.Cells)
                {
                    if (Land[cell] == player.Id)
                    {
                        if (outside && player.TrailCells.Count > 0) returning.Add(player);
                        outside = false;
                        player.OnOwn = true;
                    }
                    else if (Trail[cell] != player.Id)
                    {
                        SetTrail(cell, player.Id);
                        player.TrailCells.Add(cell);
                        outside = true;
                        player.OnOwn = false;
                    }
                    else
                    {
                        outside = true;
                        player.OnOwn = false;
                    }
                }
                player.X = m.Nx;
                player.Y = m.Ny;
                if (selfClosed.Contains(player.Id) && player.TrailCells.Count > 0
                    && !returning.Contains(player))
                    returning.Add(player);
            }
            foreach (var player in returning)
            {
                if (!player.Alive) continue;
                Capture(player);
                player.OnOwn = true;
            }
        }

        public TickResult Tick(double nowMs)
        {
            _nowMs = nowMs;
            ClearPatch();
            if (_phase == "lobby" || _phase == "over")
                return new TickResult { Phase = _phase };

            if (_phase == "countdown")
            {
                _countdownRemaining -= TickMs;
                if (_countdownRemaining > 0)
                {
                    double dt = TickMs / 1000.0;
                    foreach (var p in _order)
                        if (p.InRound && p.Alive) ApplyHeading(p, dt);
                    return new TickResult { Phase = _phase };
                }
                _phase = "play";
                _endsAt = _nowMs + _durationMs;
            }

            if (_phase == "play")
            {
                if (_nowMs >= _endsAt)
                    return new TickResult { Phase = "over", Scores = Scores() };
                _phase = "play";
                _tickCount += 1;
                Step();
                var seated = _order.Where(p => p.InRound).ToList();
                int alive = seated.Count(p => p.Alive);
                if (seated.Count > 1 && alive <= 1) _phase = "over";
                if (_nowMs >= _endsAt) _phase = "over";
            }
            // Mirror pack(): the JS returns phase 'over' via pack() after assignment.
            if (_phase == "over")
                return new TickResult { Phase = "over", Scores = Scores() };
            return new TickResult
            {
                Phase = _phase,
                Scores = null,
                Resync = _phase == "play" && _tickCount % 20 == 0,
            };
        }
    }
}
