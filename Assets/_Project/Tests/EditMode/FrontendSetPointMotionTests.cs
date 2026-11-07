using Knockbox.Game;
using Knockbox.Game.UI;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class FrontendSetPointMotionTests
    {
        static SimConfig Config() => CardCollectionRules.Apply(new SimConfig(
            BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default),
            BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl));

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void SetPointBounceSurvivesActualKoBoundary_ThenSettlesOnce(int winner, bool finalSet)
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            var state = SimState.CreateRound(config, arena, 7, MatchPhase.KO);
            state.Tick = 2000;
            state.Match.PhaseTick = 152;
            state.Match.RoundWinner = winner;
            state.Match.Round = 2;
            int index = finalSet ? 4 : 0;
            if (winner == 0) { state.Match.Score0 = 1; state.Match.SetScore0 = index; }
            else { state.Match.Score1 = 1; state.Match.SetScore1 = index; }
            var world = new SimWorld(config, arena, state);
            float minimum = 1, maximum = 1;
            bool roundEndSeen = false, subsequentSeen = false;
            for (int i = 0; i < 40; i++)
            {
                var match = world.State.Match;
                float scale = FrontendScoreView.SetPointScale(match, config.Match, winner, index, 0);
                minimum = System.Math.Min(minimum, scale); maximum = System.Math.Max(maximum, scale);
                Assert.AreEqual(scale, FrontendScoreView.SetPointScale(match, config.Match, winner, index, 0),
                    "같은 틱을 다시 그리거나 정지했을 때 반응을 다시 시작하지 않는다");
                Assert.AreEqual(1, FrontendScoreView.SetPointScale(match, config.Match, 1 - winner, index, 0));
                Assert.AreEqual(1, FrontendScoreView.SetPointScale(match, config.Match, winner, (index + 1) % 5, 0));
                roundEndSeen |= match.Phase == MatchPhase.RoundEnd && scale > 1;
                subsequentSeen |= match.Phase == (finalSet ? MatchPhase.MatchEnd : MatchPhase.SetPick) && scale != 1;
                float nextBoundary = FrontendScoreView.SetPointScale(match, config.Match, winner, index, 1);
                world.Step(default, default);
                if (match.Phase == MatchPhase.KO && world.State.Match.Phase == MatchPhase.RoundEnd
                    || match.Phase == MatchPhase.RoundEnd)
                    Assert.AreEqual(nextBoundary, FrontendScoreView.SetPointScale(world.State.Match, config.Match, winner, index, 0),
                        .0001f, "단계가 바뀌어도 합치기 반응의 크기는 연속이다");
            }
            Assert.IsTrue(roundEndSeen && subsequentSeen);
            Assert.Greater(maximum, 1.2f);
            Assert.Less(minimum, .96f);
            Assert.AreEqual(1, FrontendScoreView.SetPointScale(world.State.Match, config.Match, winner, index, 0));
            Assert.AreEqual(index + 1, world.State.Match.GetSetScore(winner), "시뮬레이션 점수는 한 번만 획득한다");
        }

        [Test]
        public void OrdinaryRoundAndNewMatchNeverPulseASetPoint()
        {
            var config = Config();
            var match = new MatchState { Phase = MatchPhase.KO, PhaseTick = 155, RoundWinner = 0 };
            Assert.AreEqual(1, FrontendScoreView.SetPointScale(match, config.Match, 0, 0, 0));
            match.Phase = MatchPhase.RoundEnd; match.PhaseTick = 0; match.Score0 = 1;
            Assert.AreEqual(1, FrontendScoreView.SetPointScale(match, config.Match, 0, 0, 0));
            match.Phase = MatchPhase.Countdown;
            Assert.AreEqual(1, FrontendScoreView.SetPointScale(match, config.Match, 0, 0, 0));
        }
    }
}
