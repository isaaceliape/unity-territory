// Port of territory/test/view-helpers.test.js plus coverage for the new
// BoardModel, SfxSynth, and FxModel client core.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Territory.Client.Tests
{
    public class ViewHelperTests
    {
        private static CardPlayer P(int id, string name, bool bot = false, string level = null) =>
            new CardPlayer { Id = id, Name = name, Bot = bot, Level = level };

        [Test] public void LateJoinerIsFoundForNickButNotOnResultsCard()
        {
            var host = P(1, "Host"); var bot = P(2, "Bot", true); var late = P(3, "Late");
            var roster = new List<CardPlayer> { host, bot, late };
            var scores = new CardScores { List = new List<CardPlayer> { host, bot }, WinnerId = 2 };
            Assert.That(ViewHelpers.PeopleForNick("over", scores, roster).Any(p => p.Id == 3), Is.True);
            Assert.That(ViewHelpers.ListedPlayers("over", scores, roster).Select(p => p.Id).ToList(),
                Is.EqualTo(new List<int> { 1, 2 }));
            var lobby = new List<CardPlayer> { host, bot, late };
            Assert.That(ViewHelpers.ListedPlayers("lobby", null, lobby).Select(p => p.Id).ToList(),
                Is.EqualTo(new List<int> { 1, 2, 3 }));
        }

        [Test] public void WinnerTextCoversWinnerDrawAndMissingRow()
        {
            var bot = P(2, "Bot");
            Assert.That(ViewHelpers.WinnerText(new CardScores
                { List = new List<CardPlayer> { P(1, "Host"), bot }, WinnerId = 2 }), Is.EqualTo("Bot wins."));
            Assert.That(ViewHelpers.WinnerText(new CardScores
                { List = new List<CardPlayer> { P(1, "Host") }, WinnerId = null }), Is.EqualTo("Draw."));
            Assert.That(ViewHelpers.WinnerText(new CardScores
                { List = new List<CardPlayer> { P(1, "Host") }, WinnerId = 99 }), Is.EqualTo("Round over."));
        }

        [Test] public void WallWarningOnlyWallsYouDriveToward()
        {
            const int W = 168, H = 126;
            Func<double, double, double, List<string>> sides = (x, y, h) =>
                ViewHelpers.WallWarnings(x, y, h, W, H).Select(r => r.Side).OrderBy(s => s).ToList();
            var near = ViewHelpers.WallWarnings(4, 60, Math.PI, W, H);
            Assert.That(near.Select(r => r.Side).ToList(), Is.EqualTo(new List<string> { "left" }));
            var nearer = ViewHelpers.WallWarnings(1, 60, Math.PI, W, H);
            Assert.That(nearer[0].Strength, Is.GreaterThan(near[0].Strength));
            Assert.That(nearer[0].Strength, Is.LessThanOrEqualTo(1));
            Assert.That(sides(4, 60, 0), Is.Empty);
            Assert.That(sides(4, 60, -Math.PI / 2), Is.Empty);
            Assert.That(sides(80, 60, Math.PI), Is.Empty);
            Assert.That(sides(3, 3, -3 * Math.PI / 4), Is.EqualTo(new List<string> { "left", "top" }));
            Assert.That(sides(100, H - 3, Math.PI / 2), Is.EqualTo(new List<string> { "bottom" }));
            Assert.That(sides(W - 3, 60, 0), Is.EqualTo(new List<string> { "right" }));
        }

        [Test] public void KillFeedTextNamesTheRightPlayerForEachReason()
        {
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "cut", "Bot"), Is.EqualTo("Isaac was cut by Bot"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "cut", null), Is.EqualTo("Isaac's line was cut"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "enclosed", "Bot"), Is.EqualTo("Bot boxed in Isaac"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "enclosed", null), Is.EqualTo("Isaac got boxed in"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "wall", null), Is.EqualTo("Isaac hit the wall"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "crash", null), Is.EqualTo("Isaac crashed"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", "left", null), Is.EqualTo("Isaac left the round"));
            Assert.That(ViewHelpers.DeathFeedText("Isaac", null, null), Is.EqualTo("Isaac is out"));
            Assert.That(ViewHelpers.CaptureFeedText("Bot", 1.449), Is.EqualTo("Bot took +1.4%"));
        }

        [Test] public void DangerCellsOnlyOtherPlayersLinesWithinRange()
        {
            const int W = 100;
            Func<int, int, int> at = (x, y) => y * W + x;
            Assert.That(ViewHelpers.IsDangerCell(at(55, 50), W, 2, 1, 50.5, 50.5), Is.True);
            Assert.That(ViewHelpers.IsDangerCell(at(55, 50), W, 1, 1, 50.5, 50.5), Is.False);
            Assert.That(ViewHelpers.IsDangerCell(at(55, 50), W, 0, 1, 50.5, 50.5), Is.False);
            Assert.That(ViewHelpers.IsDangerCell(at(70, 50), W, 2, 1, 50.5, 50.5), Is.False);
            Assert.That(ViewHelpers.IsDangerCell(at(59, 50), W, 2, 1, 50.5, 50.5), Is.True);
            Assert.That(ViewHelpers.IsDangerCell(at(61, 50), W, 2, 1, 50.5, 50.5), Is.False);
        }

        [Test] public void SessionWinsLabel()
        {
            var wins = new Dictionary<string, int> { { "ada", 1 }, { "bot", 3 } };
            Assert.That(ViewHelpers.WinsLabel(wins, "Bea"), Is.EqualTo(""));
            Assert.That(ViewHelpers.WinsLabel(wins, "Ada"), Is.EqualTo("1 win"));
            Assert.That(ViewHelpers.WinsLabel(wins, "BOT"), Is.EqualTo("3 wins"));
            Assert.That(ViewHelpers.WinsLabel(null, "Ada"), Is.EqualTo(""));
            Assert.That(ViewHelpers.WinsLabel(new Dictionary<string, int>(), "Ada"), Is.EqualTo(""));
        }

        [Test] public void BotHelpers()
        {
            Assert.That(ViewHelpers.MaxBots, Is.EqualTo(Territory.Sim.Defs.MaxBots));
            Assert.That(ViewHelpers.BotLabel("hard"), Is.EqualTo("hard bot"));
            Assert.That(ViewHelpers.BotLabel(null), Is.EqualTo("normal bot"));
            var players = new List<CardPlayer> { P(1, "Host"), P(2, "Bot", true, "easy"), P(9, "Bot", true, "hard") };
            Assert.That(ViewHelpers.BotCount(players), Is.EqualTo(2));
        }

        [Test] public void TouchHelpers()
        {
            Assert.That(ViewHelpers.TurnFromHeld(false, false), Is.EqualTo(0));
            Assert.That(ViewHelpers.TurnFromHeld(true, false), Is.EqualTo(-1));
            Assert.That(ViewHelpers.TurnFromHeld(false, true), Is.EqualTo(1));
            Assert.That(ViewHelpers.TurnFromHeld(true, true), Is.EqualTo(0));
            Assert.That(ViewHelpers.FollowCellSize(1440), Is.EqualTo(20));
            Assert.That(ViewHelpers.FollowCellSize(360), Is.EqualTo(360.0 / 26).Within(1e-9));
            Assert.That(ViewHelpers.FollowCellSize(200), Is.EqualTo(12));
        }

        [Test] public void ClockAndReasonText()
        {
            Assert.That(ViewHelpers.FormatClock(175000), Is.EqualTo("2:55"));
            Assert.That(ViewHelpers.FormatClock(8000), Is.EqualTo("0:08"));
            Assert.That(ViewHelpers.ReasonText("cut", "Bot"), Is.EqualTo("Cut by Bot"));
            Assert.That(ViewHelpers.ReasonText("enclosed", null), Is.EqualTo("You got boxed in"));
            Assert.That(ViewHelpers.ReasonText("wall", null), Is.EqualTo("You hit the wall"));
        }
    }

    public class BoardModelTests
    {
        [Test] public void ApplyRunsReportsTakenCellsAndFills()
        {
            var board = new BoardModel { W = 10, H = 10, Land = new byte[100], Trail = new byte[100] };
            var taken = board.ApplyRuns(new List<int[]> { new[] { 5, 3, 1 }, new[] { 20, 2, 2 } });
            Assert.That(taken[1], Is.EqualTo(new List<int> { 5, 6, 7 }));
            Assert.That(taken[2], Is.EqualTo(new List<int> { 20, 21 }));
            Assert.That(board.Land[6], Is.EqualTo(1));
            // Re-applying the same runs reports nothing new.
            Assert.That(board.ApplyRuns(new List<int[]> { new[] { 5, 3, 1 } }).Count, Is.EqualTo(0));
            Assert.That(BoardModel.CapturePct(3, 10, 10), Is.EqualTo(3.0).Within(1e-9));
        }

        [Test] public void ApplyTrailTracksCells()
        {
            var board = new BoardModel { W = 10, H = 10, Land = new byte[100], Trail = new byte[100] };
            board.ApplyTrail(new List<int[]> { new[] { 7, 1 }, new[] { 8, 2 } });
            Assert.That(board.TrailCells.SetEquals(new[] { 7, 8 }), Is.True);
            board.ApplyTrail(new List<int[]> { new[] { 7, 0 } });
            Assert.That(board.TrailCells.SetEquals(new[] { 8 }), Is.True);
        }

        [Test] public void SyncPlayersDetectsDeathsAndInterpolates()
        {
            var board = new BoardModel();
            var a = new BodyState { Id = 1, Name = "A", ToX = 5, ToY = 5, Alive = true, Land = 10 };
            Assert.That(board.SyncPlayers(new[] { a }, 1000, 110).Count, Is.EqualTo(0));
            var moved = new BodyState { Id = 1, Name = "A", ToX = 6, ToY = 5, Alive = true, Land = 10 };
            Assert.That(board.SyncPlayers(new[] { moved }, 1110, 110).Count, Is.EqualTo(0));
            BoardModel.Pose(board.Bodies[1], 1165, 110, out double x, out double y);
            Assert.That(x, Is.EqualTo(5.5).Within(1e-9));
            var dead = new BodyState { Id = 1, Name = "A", ToX = 6, ToY = 5, Alive = false, OutReason = "wall", Land = 10 };
            var died = board.SyncPlayers(new[] { dead }, 1220, 110);
            Assert.That(died.Count, Is.EqualTo(1));
            Assert.That(board.StatusText(1, "play"), Is.EqualTo("Watcher. You hit the wall"));
        }

        [Test] public void SortRowsMatchesScoreboardOrder()
        {
            var rows = new List<BodyState>
            {
                new BodyState { Id = 1, Name = "B", Land = 5, Kills = 0 },
                new BodyState { Id = 2, Name = "A", Land = 9, Kills = 0 },
                new BodyState { Id = 3, Name = "C", Land = 9, Kills = 2 },
            };
            var sorted = BoardModel.SortRows(rows).Select(r => r.Id).ToList();
            Assert.That(sorted, Is.EqualTo(new List<int> { 3, 2, 1 }));
        }

        [Test] public void OwnerAtRespectsBoundsAndTrail()
        {
            var land = new byte[100]; var trail = new byte[100];
            land[42] = 3; trail[43] = 2;
            Assert.That(BoardModel.OwnerAt(land, trail, 10, 10, 2, 4), Is.EqualTo(3));
            Assert.That(BoardModel.OwnerAt(land, trail, 10, 10, 3, 4), Is.EqualTo(2));
            Assert.That(BoardModel.OwnerAt(land, trail, 10, 10, -1, 4), Is.EqualTo(0));
            Assert.That(BoardModel.OwnerAt(land, trail, 10, 10, 10, 4), Is.EqualTo(0));
        }
    }

    public class SfxTests
    {
        private static double Peak(float[] s)
        {
            double peak = 0;
            foreach (var v in s) peak = Math.Max(peak, Math.Abs(v));
            return peak;
        }

        [Test] public void EachEventRendersSound()
        {
            var sfx = new SfxSynth(() => 0.5);
            sfx.Capture(0.2); sfx.Death(1); sfx.TickSfx(); sfx.Go(); sfx.Win(); sfx.Lose();
            Assert.That(Peak(sfx.Render(2.0)), Is.GreaterThan(0.01));
        }

        [Test] public void EnvelopeDecays()
        {
            var sfx = new SfxSynth(() => 0.5);
            sfx.TickSfx();
            var s = sfx.Render(1.0);
            int n = s.Length;
            double early = 0, late = 0;
            for (int i = 0; i < n / 10; i += 1) early = Math.Max(early, Math.Abs(s[i]));
            for (int i = n * 9 / 10; i < n; i += 1) late = Math.Max(late, Math.Abs(s[i]));
            Assert.That(early, Is.GreaterThan(0.01));
            Assert.That(late, Is.EqualTo(0).Within(1e-6));
        }

        [Test] public void MuteSilencesEverything()
        {
            var sfx = new SfxSynth(() => 0.5);
            sfx.Capture(0.9);
            sfx.SetMuted(true);
            Assert.That(Peak(sfx.Render(1.0)), Is.EqualTo(0));
            Assert.That(sfx.IsMuted, Is.True);
        }

        [Test] public void ThemeLoops32StepsAndRespectsMusicMute()
        {
            Assert.That(SfxSynth.MusicLead.Length, Is.EqualTo(32));
            Assert.That(SfxSynth.MusicBass.Length, Is.EqualTo(32));
            var sfx = new SfxSynth(() => 0.5);
            sfx.StartMusic();
            Assert.That(sfx.IsMusicPlaying, Is.True);
            sfx.PumpMusic(0);
            int first = sfx.NoteCount;
            Assert.That(first, Is.GreaterThan(0));
            sfx.PumpMusic(5);
            Assert.That(sfx.NoteCount, Is.GreaterThan(first));
            sfx.SetMusicMuted(true);
            int before = sfx.NoteCount;
            sfx.PumpMusic(50);
            Assert.That(sfx.NoteCount, Is.EqualTo(before));
            sfx.StopMusic();
            Assert.That(sfx.IsMusicPlaying, Is.False);
        }
    }

    public class FxModelTests
    {
        private static readonly double[] Red = { 255, 42, 109 };

        [Test] public void CaptureAddsFlashRingsFloaterAndSparks()
        {
            var fx = new FxModel(() => 0.5);
            fx.Capture(new System.Collections.Generic.List<int> { 0, 1, 2, 10, 11, 12 }, 10, Red, null, null, "+1");
            Assert.That(fx.Flashes.Count, Is.EqualTo(1));
            Assert.That(fx.Rings.Count, Is.EqualTo(2));
            Assert.That(fx.Floaters.Count, Is.EqualTo(1));
            Assert.That(fx.Particles.Count, Is.GreaterThan(0));
            fx.Update(5.0);
            Assert.That(fx.Flashes.Count, Is.EqualTo(0));
            Assert.That(fx.Floaters.Count, Is.EqualTo(0));
        }

        [Test] public void DeathBurstsAndShakesThenDecays()
        {
            var fx = new FxModel(() => 0.75);
            fx.Death(5, 5, Red, true, 1);
            Assert.That(fx.Particles.Count, Is.EqualTo(110 + 24));
            Assert.That(fx.Rings.Count, Is.EqualTo(3));
            Assert.That(fx.ShakeAmp, Is.EqualTo(1));
            fx.Shake(0.016, out double x, out double y);
            Assert.That(Math.Abs(x) + Math.Abs(y), Is.GreaterThan(0));
            for (int i = 0; i < 60; i += 1) fx.Shake(0.016, out x, out y);
            Assert.That(fx.ShakeAmp, Is.EqualTo(0));
            fx.Shake(0.016, out x, out y);
            Assert.That(x, Is.EqualTo(0));
            Assert.That(y, Is.EqualTo(0));
        }

        [Test] public void ReducedMotionDisablesEffects()
        {
            var fx = new FxModel(() => 0.5, reducedMotion: true);
            fx.Capture(new System.Collections.Generic.List<int> { 1, 2, 3 }, 10, Red, 0, 0, "+1");
            fx.Death(5, 5, Red, true, 1);
            fx.TrailSparks(5, 5, 0, Red, 0.016);
            Assert.That(fx.Particles.Count, Is.EqualTo(0));
            Assert.That(fx.Rings.Count, Is.EqualTo(0));
            fx.Shake(0.016, out double x, out double y);
            Assert.That(x, Is.EqualTo(0));
            Assert.That(y, Is.EqualTo(0));
        }

        [Test] public void CelebrateScalesWithWinner()
        {
            var fx = new FxModel(() => 0.5);
            fx.Celebrate(new List<double[]>(), true, 800, 600);
            Assert.That(fx.Confetti.Count, Is.EqualTo(220));
            fx.Confetti.Clear();
            fx.Celebrate(new List<double[]>(), false, 800, 600);
            Assert.That(fx.Confetti.Count, Is.EqualTo(80));
        }
    }
}
