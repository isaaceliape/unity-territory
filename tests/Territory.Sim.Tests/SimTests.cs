// Port of territory/test/rules.test.js. Same scenario names, same numbers:
// any divergence here means the C# sim drifted from the JS rules.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Territory.Sim;

namespace Territory.Sim.Tests
{
    public class SimTests
    {
        private static Game Create(GameOptions o = null) => new Game(o);

        private sealed class Setup
        {
            public Game Game;
            public List<Player> Players = new List<Player>();
            public double Now;
            public void Steps(IEnumerable<string> dirs, int? id = null)
            {
                int target = id ?? Players[0].Id;
                foreach (var dir in dirs)
                {
                    Game.SetDirection(target, dir);
                    Now += Game.TickMs;
                    Game.Tick(Now);
                }
            }
        }

        private static Setup MakeSetup(int count = 1)
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 100, CountdownMs = 0 });
            var s = new Setup { Game = game, Now = 10000 };
            for (int i = 0; i < count; i += 1) s.Players.Add(game.AddPlayer($"P{i + 1}"));
            Assert.That(game.Start(s.Players[0].Id, 3, s.Now).Ok, Is.True);
            return s;
        }

        private static readonly string[] OutAndBack = { "right", "right", "right", "down", "left", "left" };
        private static readonly string[] Loop =
            { "right", "right", "right", "down", "down", "down", "left", "left", "left", "up", "up" };

        [Test] public void ALineOutAndBackOnlyClaimsTheLine()
        {
            var s = MakeSetup();
            s.Steps(OutAndBack);
            Assert.That(s.Players[0].Alive, Is.True);
            Assert.That(s.Players[0].LandCount, Is.EqualTo(20));
            Assert.That(s.Game.Land[5 * s.Game.W + 9], Is.EqualTo(0));
        }

        [Test] public void ClosingALoopClaimsTheEmptyCellsInsideIt()
        {
            var s = MakeSetup(2);
            s.Players[1].Connected = false;
            s.Steps(Loop);
            Assert.That(s.Players[0].Alive, Is.True);
            Assert.That(s.Players[0].LandCount, Is.EqualTo(28));
            Assert.That(s.Game.Land[6 * s.Game.W + 7], Is.EqualTo(s.Players[0].Id));
            Assert.That(s.Game.Land[5 * s.Game.W + 9], Is.EqualTo(0));
            Assert.That(s.Players[1].Alive, Is.True);
            Assert.That(s.Players[1].LandCount, Is.EqualTo(16));
        }

        [Test] public void ALineOverTheirLandBecomesMineWhenIGetHome()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            int crossed = 23 * s.Game.W + 33;
            int approach = 5 * s.Game.W + 7;
            a.TrailCells = new List<int> { crossed, approach };
            s.Game.Trail[crossed] = (byte)a.Id;
            s.Game.Trail[approach] = (byte)a.Id;
            a.X = 7.5; a.Y = 5.5; a.OnOwn = false;
            a.Heading = Math.PI; a.Turn = 0; a.Aim = null;
            double now = 12000 + s.Game.TickMs;
            s.Game.Tick(now);
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
            Assert.That(b.LandCount, Is.EqualTo(15));
            Assert.That(s.Game.Land[crossed], Is.EqualTo(a.Id));
            Assert.That(s.Game.Trail[crossed], Is.EqualTo(0));
            Assert.That(s.Game.Phase, Is.EqualTo("play"));
        }

        [Test] public void RidingAcrossAnotherPlayerLandDoesNotKillThem()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            a.X = 32.5; a.Y = 25.5; a.OnOwn = false;
            a.Heading = 0; a.Turn = 0; a.Aim = null;
            a.TrailCells = new List<int>();
            double now = 12000;
            for (int i = 0; i < 5; i += 1) { now += s.Game.TickMs; s.Game.Tick(now); }
            Assert.That(b.Alive, Is.True);
            Assert.That(b.LandCount, Is.EqualTo(16));
            Assert.That(s.Game.Land[25 * s.Game.W + 35], Is.EqualTo(b.Id));
            Assert.That(s.Game.Phase, Is.EqualTo("play"));
            Assert.That(a.Alive, Is.True);
        }

        [Test] public void CrossingATrailKillsThatPlayerAndLeavesTheirLand()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            a.Connected = false;
            s.Steps(new[] { "up", "up", "up", "up" }, b.Id);
            b.Connected = false;
            Assert.That(b.TrailCells.Count, Is.GreaterThan(0));
            a.Connected = true;
            a.X = 34; a.Y = 22; a.Dir = "right"; a.OnOwn = false;
            a.TrailCells = new List<int>();
            s.Steps(new[] { "right" }, a.Id);
            Assert.That(b.Alive, Is.False);
            Assert.That(b.LandCount, Is.EqualTo(16));
            Assert.That(b.TrailCells.Count, Is.EqualTo(0));
            Assert.That(s.Game.Trail.Contains((byte)b.Id), Is.False);
            Assert.That(b.Out.Reason, Is.EqualTo("cut"));
            Assert.That(b.Out.By, Is.EqualTo(a.Id));
            Assert.That(a.Kills, Is.EqualTo(1));
            Assert.That(a.Alive, Is.True);
        }

        [Test] public void CrossingYourOwnLineClosesTheLoopInsteadOfKillingYou()
        {
            var s = MakeSetup();
            s.Steps(new[] { "right", "right", "right", "down", "left", "up" });
            Assert.That(s.Players[0].Alive, Is.True);
            Assert.That(s.Players[0].Out, Is.Null);
            Assert.That(s.Players[0].OnOwn, Is.True);
            Assert.That(s.Players[0].LandCount, Is.EqualTo(20));
            Assert.That(s.Players[0].TrailCells.Count, Is.EqualTo(0));
        }

        [Test] public void ALoopClosedOnOpenGroundClaimsEverythingInsideIt()
        {
            var s = MakeSetup(2);
            s.Players[1].Connected = false;
            var dirs = Enumerable.Repeat("right", 5)
                .Concat(Enumerable.Repeat("down", 4))
                .Concat(Enumerable.Repeat("left", 4))
                .Concat(Enumerable.Repeat("up", 3));
            s.Steps(dirs);
            Assert.That(s.Players[0].Alive, Is.True);
            Assert.That(s.Players[0].LandCount, Is.EqualTo(39));
            Assert.That(s.Game.Land[8 * s.Game.W + 8], Is.EqualTo(s.Players[0].Id));
            Assert.That(s.Game.Trail.Contains((byte)s.Players[0].Id), Is.False);
        }

        [Test] public void ASelfClosedLoopOverTheirLandTakesTheInsideAndMyLine()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            for (int y = 4; y <= 9; y += 1)
                for (int x = 8; x <= 12; x += 1)
                {
                    s.Game.Land[y * s.Game.W + x] = (byte)b.Id;
                    b.LandCount += 1;
                }
            b.X = 10.5; b.Y = 4.5;
            var dirs = Enumerable.Repeat("right", 7)
                .Concat(Enumerable.Repeat("down", 5))
                .Concat(Enumerable.Repeat("left", 5))
                .Concat(Enumerable.Repeat("up", 5));
            s.Steps(dirs);
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
            Assert.That(s.Game.Land[8 * s.Game.W + 9], Is.EqualTo(a.Id));
            Assert.That(s.Game.Land[5 * s.Game.W + 10], Is.EqualTo(a.Id));
            Assert.That(s.Game.Land[4 * s.Game.W + 10], Is.EqualTo(b.Id));
            Assert.That(b.LandCount, Is.EqualTo(16 + 30 - 25));
        }

        [Test] public void TheirHeadOutsideMySelfClosedLoopIsNotAKill()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            b.X = 30.5; b.Y = 20.5;
            s.Steps(new[] { "right", "right", "right", "down", "left", "up" });
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
            Assert.That(b.Out, Is.Null);
            Assert.That(s.Game.Land[5 * s.Game.W + 6], Is.EqualTo(a.Id));
        }

        [Test] public void ADiagonalStepConnectsThroughTheSide()
        {
            var s = MakeSetup();
            var player = s.Players[0];
            player.X = 10.5; player.Y = 10.5; player.OnOwn = false;
            player.Heading = Math.PI / 4; player.Turn = 0; player.Aim = null;
            player.TrailCells = new List<int>();
            s.Game.Tick(12000 + s.Game.TickMs);
            var cells = player.TrailCells
                .Select(i => new { X = i % s.Game.W, Y = i / s.Game.W }).ToList();
            Assert.That(cells.Any(c => c.X == 11 && c.Y == 11), Is.True);
            Assert.That(cells.Any(c => (c.X == 11 && c.Y == 10) || (c.X == 10 && c.Y == 11)), Is.True);
        }

        [Test] public void HoldingATurnMakesTheSnakeCurve()
        {
            var s = MakeSetup();
            var player = s.Players[0];
            s.Game.SetDirection(player.Id, "right");
            s.Game.SetSteer(player.Id, 1);
            double x = player.X, y = player.Y;
            double now = 11000;
            for (int i = 0; i < 4; i += 1) { now += s.Game.TickMs; s.Game.Tick(now); }
            Assert.That(player.Alive, Is.True);
            Assert.That(player.X, Is.GreaterThan(x));
            Assert.That(player.Y, Is.GreaterThan(y));
            Assert.That(player.Heading, Is.GreaterThan(0.4));
        }

        [Test] public void RunningIntoTheWallIsDeath()
        {
            var s = MakeSetup();
            s.Steps(Enumerable.Repeat("up", 40));
            Assert.That(s.Players[0].Alive, Is.False);
            Assert.That(s.Players[0].Out.Reason, Is.EqualTo("wall"));
            Assert.That(s.Players[0].LandCount, Is.EqualTo(16));
        }

        [Test] public void LandInsideALoopIsStolenAndTheOwnerStaysAlive()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            int cell = 7 * s.Game.W + 7;
            s.Game.Land[cell] = (byte)b.Id;
            b.LandCount += 1;
            s.Steps(Loop.Take(Loop.Length - 1));
            Assert.That(s.Game.Land[cell], Is.EqualTo(b.Id));
            s.Steps(new[] { "up" });
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
            Assert.That(b.LandCount, Is.EqualTo(16));
            Assert.That(s.Game.Land[cell], Is.EqualTo(a.Id));
        }

        [Test] public void BoxingAPlayerInKillsThem()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            b.Connected = false;
            s.Steps(Loop.Take(Loop.Length - 1));
            b.X = 7; b.Y = 7; b.OnOwn = false;
            s.Steps(new[] { "up" });
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.False);
            Assert.That(b.Out.Reason, Is.EqualTo("enclosed"));
            Assert.That(b.Out.By, Is.EqualTo(a.Id));
            Assert.That(s.Game.Land[7 * s.Game.W + 7], Is.EqualTo(a.Id));
            Assert.That(b.LandCount, Is.EqualTo(16));
            Assert.That(s.Game.Land[25 * s.Game.W + 35], Is.EqualTo(b.Id));
        }

        [Test] public void TheRoundEndsWhenOnlyOnePlayerIsLeftAlive()
        {
            var s = MakeSetup(2);
            var (a, b) = (s.Players[0], s.Players[1]);
            a.Connected = false;
            s.Steps(new[] { "up", "up", "up", "up" }, b.Id);
            b.Connected = false;
            a.Connected = true;
            a.X = 34; a.Y = 22; a.Dir = "right"; a.OnOwn = false;
            a.TrailCells = new List<int>();
            s.Steps(new[] { "right" }, a.Id);
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.False);
            Assert.That(s.Game.Phase, Is.EqualTo("over"));
            var scores = s.Game.Scores();
            Assert.That(scores.WinnerId, Is.EqualTo(a.Id));
            Assert.That(scores.List.Find(r => r.Id == b.Id).Land, Is.EqualTo(16));
            string log = s.Game.ResultLog();
            Assert.That(log, Does.Contain($"Winner: {a.Name}"));
            Assert.That(log, Does.Contain($"{b.Name} lost with "));
            Assert.That(log, Does.Contain($"cut by {a.Name}"));
        }

        [Test] public void ASoloPlayerCanBeOutWithoutEndingTheRound()
        {
            var s = MakeSetup();
            s.Steps(Enumerable.Repeat("up", 40));
            Assert.That(s.Players[0].Alive, Is.False);
            Assert.That(s.Game.Phase, Is.EqualTo("play"));
        }

        [Test] public void ABotJoinsTheRoomAndSteersDuringTheRound()
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 100, CountdownMs = 0 });
            var ada = game.AddPlayer("Ada");
            var added = game.AddBot();
            Assert.That(added.Ok, Is.True);
            Assert.That(game.Start(ada.Id, 1, 0).Ok, Is.True);
            var bot = game.GetPlayer(added.Id);
            Assert.That(bot.Bot, Is.True);
            Assert.That(bot.Alive, Is.True);
            Assert.That(game.HostId, Is.Not.EqualTo(bot.Id));
            double startX = bot.X, startY = bot.Y;
            double now = 1000;
            for (int i = 0; i < 6; i += 1) { now += game.TickMs; game.Tick(now); }
            Assert.That(bot.X != startX || bot.Y != startY, Is.True);
            Assert.That(bot.Aim.HasValue && double.IsFinite(bot.Aim.Value), Is.True);
            Assert.That(bot.Alive, Is.True);
        }

        [Test] public void ABotLoopsOutWithoutCuttingItsOwnLineStraightAway()
        {
            var game = new Game(new GameOptions { W = 80, H = 60, StartSize = 10, TickMs = 110, CountdownMs = 0 });
            var ada = game.AddPlayer("Ada");
            var added = game.AddBot();
            Assert.That(game.Start(ada.Id, 3, 0).Ok, Is.True);
            var bot = game.GetPlayer(added.Id);
            double now = 0;
            for (int i = 0; i < 40; i += 1) { now += 110; game.Tick(now); }
            Assert.That(bot.Alive, Is.True);
            Assert.That(game.Phase, Is.EqualTo("play"));
        }

        [Test] public void APlayerCanRenameInTheWaitingRoom()
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 100, CountdownMs = 0 });
            var ada = game.AddPlayer("Ada");
            var bea = game.AddPlayer("Bea");
            Assert.That(game.Rename(ada.Id, "Ada Nova").Ok, Is.True);
            Assert.That(ada.Name, Is.EqualTo("Ada Nova"));
            Assert.That(game.Rename(ada.Id, "Bea").Name, Is.EqualTo("Bea 2"));
            Assert.That(game.Start(ada.Id, 1, 0).Ok, Is.True);
            Assert.That(game.Rename(ada.Id, "Nope").Ok, Is.False);
            Assert.That(ada.Name, Is.EqualTo("Bea 2"));
            game.ReturnToLobby(ada.Id);
            Assert.That(game.Rename(bea.Id, "Bea").Ok, Is.True);
            Assert.That(bea.Name, Is.EqualTo("Bea"));
        }

        [Test] public void RestartWaitsInTheLobbyUntilTheHostClicksStart()
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 100, CountdownMs = 3000 });
            var a = game.AddPlayer("Ada");
            var b = game.AddPlayer("Bea");
            Assert.That(game.Start(a.Id, 1, 0).Ok, Is.True);
            Assert.That(game.Phase, Is.EqualTo("countdown"));
            Assert.That(game.ReturnToLobby(b.Id).Ok, Is.False);
            Assert.That(game.Phase, Is.EqualTo("countdown"));
            Assert.That(game.ReturnToLobby(a.Id).Ok, Is.True);
            Assert.That(game.Phase, Is.EqualTo("lobby"));
            Assert.That(game.Tick(1000).Phase, Is.EqualTo("lobby"));
            Assert.That(a.LandCount, Is.EqualTo(0));
            Assert.That(game.Start(a.Id, 1, 2000).Ok, Is.True);
            Assert.That(game.Phase, Is.EqualTo("countdown"));
        }

        [Test] public void TheHostCanRestartARoundThatIsStillGoing()
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 100, CountdownMs = 0 });
            var a = game.AddPlayer("Ada");
            var b = game.AddPlayer("Bea");
            double now = 0;
            Assert.That(game.Start(a.Id, 3, now).Ok, Is.True);
            now += 100;
            game.Tick(now);
            a.Kills = 4;
            Assert.That(game.Start(b.Id, 1, now).Ok, Is.False);
            Assert.That(game.Phase, Is.EqualTo("play"));
            Assert.That(game.Start(a.Id, 1, now).Ok, Is.True);
            Assert.That(game.Phase, Is.EqualTo("play"));
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
            Assert.That(a.Kills, Is.EqualTo(0));
            Assert.That(a.LandCount, Is.EqualTo(16));
            Assert.That(b.LandCount, Is.EqualTo(16));
        }

        [Test] public void TheTimerEndsTheRoundAndOnlyTheHostCanStart()
        {
            var game = new Game(new GameOptions { W = 40, H = 30, StartSize = 4, TickMs = 1000, CountdownMs = 0 });
            var a = game.AddPlayer("Ada");
            var b = game.AddPlayer("Bea");
            double now = 0;
            Assert.That(game.Start(a.Id, 1, now).Ok, Is.True);
            now = 59000;
            Assert.That(game.Tick(now).Phase, Is.EqualTo("play"));
            now = 60000;
            var done = game.Tick(now);
            Assert.That(done.Phase, Is.EqualTo("over"));
            Assert.That(done.Scores.List[0].Id, Is.EqualTo(a.Id));
            Assert.That(game.Start(b.Id, 1, now).Ok, Is.False);
            Assert.That(game.Start(a.Id, 1, now).Ok, Is.True);
            Assert.That(a.Alive, Is.True);
            Assert.That(b.Alive, Is.True);
        }

        // mulberry32, identical to the seeded() helper in rules.test.js so bot
        // behavior matches the JS game tick for tick.
        private static Func<double> Seeded(uint seed)
        {
            uint a = seed;
            return () =>
            {
                unchecked
                {
                    a += 0x6D2B79F5u;
                    uint t = a ^ (a >> 15);
                    t *= 1u | a;
                    int prod = (int)(t ^ (t >> 7)) * (int)(61u | t);
                    t = (t + (uint)prod) ^ t;
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        [Test] public void BotsHaveALevelNormalByDefault()
        {
            var game = new Game(new GameOptions { CountdownMs = 0 });
            game.AddPlayer("Ada");
            var normal = game.AddBot();
            var easy = game.AddBot("easy");
            var hard = game.AddBot("hard");
            Assert.That(game.GetPlayer(normal.Id).Level, Is.EqualTo("normal"));
            Assert.That(game.GetPlayer(easy.Id).Level, Is.EqualTo("easy"));
            Assert.That(game.GetPlayer(hard.Id).Level, Is.EqualTo("hard"));
            var roster = game.Roster();
            Assert.That(roster.Select(r => r.Level).ToList(),
                Is.EqualTo(new List<string> { null, "normal", "easy", "hard" }));
            Assert.That(game.AddBot("impossible").Ok, Is.False);
            Assert.That(game.AddBot("toString").Ok, Is.False);
            Assert.That(Defs.BotLevels.Keys.ToList(),
                Is.EqualTo(new List<string> { "easy", "normal", "hard" }));
        }

        [Test] public void ARoomHoldsUpToThreeBotsAndRefusesAFourth()
        {
            var game = new Game(new GameOptions { CountdownMs = 0 });
            game.AddPlayer("Ada");
            Assert.That(Defs.MaxBots, Is.EqualTo(3));
            for (int i = 0; i < Defs.MaxBots; i += 1) Assert.That(game.AddBot().Ok, Is.True);
            var fourth = game.AddBot();
            Assert.That(fourth.Ok, Is.False);
            Assert.That(fourth.Error, Does.Contain("up to 3 bots"));
            var names = game.Roster().Where(r => r.Bot).Select(r => r.Name).ToList();
            Assert.That(new HashSet<string>(names).Count, Is.EqualTo(3));
        }

        [Test] public void BotsCanBeRemovedInTheWaitingRoom()
        {
            var game = new Game(new GameOptions { CountdownMs = 0 });
            var ada = game.AddPlayer("Ada");
            var bot = game.AddBot("hard");
            Assert.That(game.RemoveBot(ada.Id).Ok, Is.False);
            Assert.That(game.RemoveBot(999).Ok, Is.False);
            Assert.That(game.RemoveBot(bot.Id).Ok, Is.True);
            Assert.That(game.GetPlayer(bot.Id), Is.Null);
            Assert.That(game.AddBot().Ok, Is.True);
            var again = game.AddBot();
            Assert.That(game.Start(ada.Id, 1, 0).Ok, Is.True);
            Assert.That(game.RemoveBot(again.Id).Ok, Is.False);
            Assert.That(game.AddBot().Ok, Is.False);
        }

        private static int BotGrowth(string level, uint seed, int ticks)
        {
            var game = new Game(new GameOptions { CountdownMs = 0, Random = Seeded(seed) });
            var human = game.AddPlayer("Human");
            var bot = game.GetPlayer(game.AddBot(level).Id);
            double now = 1000;
            game.Start(human.Id, 5, now);
            game.SetSteer(human.Id, 1);
            for (int i = 0; i < ticks; i += 1) { now += game.TickMs; game.Tick(now); }
            return bot.LandCount;
        }

        [Test] public void AnEasyBotGrowsClearlySlowerThanANormalOne()
        {
            int[] seeds = { 1, 2, 3, 4, 5, 6 };
            double easy = seeds.Average(s => BotGrowth("easy", (uint)s, 545));
            double normal = seeds.Average(s => BotGrowth("normal", (uint)s, 545));
            Assert.That(easy, Is.LessThan(normal * 0.75), $"easy {easy} should be well under normal {normal}");
        }

        [Test] public void AHardBotBeatsANormalBotAndCutsLinesToDoIt()
        {
            int hardWins = 0, hardKills = 0, normalKills = 0;
            for (uint seed = 1; seed <= 6; seed += 1)
            {
                var game = new Game(new GameOptions { CountdownMs = 0, Random = Seeded(seed) });
                var human = game.AddPlayer("Human");
                var hard = game.GetPlayer(game.AddBot("hard").Id);
                var normal = game.GetPlayer(game.AddBot("normal").Id);
                double now = 1000;
                game.Start(human.Id, 5, now);
                game.SetSteer(human.Id, 1);
                for (int i = 0; i < 1500 && game.Phase == "play"; i += 1)
                {
                    now += game.TickMs;
                    game.Tick(now);
                }
                if (hard.LandCount > normal.LandCount) hardWins += 1;
                hardKills += hard.Kills;
                normalKills += normal.Kills;
            }
            Assert.That(hardWins, Is.GreaterThanOrEqualTo(5), $"hard won {hardWins} of 6");
            Assert.That(hardKills, Is.GreaterThan(normalKills),
                $"hard kills {hardKills} vs normal {normalKills}");
        }

        [Test] public void TheNormalBotPlaysTheSameAsBeforeLevelsExisted()
        {
            var normal = Defs.BotLevels["normal"];
            Assert.That(normal.HomeAt, Is.EqualTo(16));
            Assert.That(normal.Noise, Is.EqualTo(0));
            Assert.That(normal.Hunt, Is.False);
            Assert.That(normal.Rest, Is.EqualTo(0));
            Assert.That(BotGrowth("normal", 1, 300), Is.EqualTo(BotGrowth("normal", 99, 300)));
        }
    }
}
