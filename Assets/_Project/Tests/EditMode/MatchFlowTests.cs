using System;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP4-5: 경기 흐름(06-match-and-tools.md 1절).
    /// Fighting → KO(60틱) → RoundEnd(1틱) → RoundTransition(1틱) → Countdown, 5점이면 RoundEnd → MatchEnd.
    /// </summary>
    public class MatchFlowTests
    {
        const float FloorY = -7.55f;

        static bool Has(SimWorld w, SimEventType type, int fighter = int.MinValue)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && (fighter == int.MinValue || w.Events[i].Fighter == fighter))
                    return true;
            }
            return false;
        }

        /// <summary>P1이 체력 hp2인 P2를 한 번 때려 KO시키는 Box 월드. 명중(KO) 틱까지 진행한 상태로 돌려준다.</summary>
        static SimWorld KnockOutP2(int hp2, SimConfig config = null)
        {
            var w = TestWorlds.CreateAt(new Vec2(-10.6f, FloorY), new Vec2(-9.4f, FloorY), config);
            w.State.Fighter1.Health = hp2;
            for (int t = 1; t <= 6; t++)
                w.Step(new InputFrame(0, 0, t == 1 ? InputButtons.Attack : InputButtons.None), default);
            Assert.IsTrue(Has(w, SimEventType.KO, 1), "6번째 틱에 KO");
            return w;
        }

        [Test]
        public void KnockOut_FreezesBothForKoPhase_ThenNextRound()
        {
            var w = KnockOutP2(5);
            var c = w.Config;
            Assert.AreEqual(MatchPhase.KO, w.State.Match.Phase);
            Assert.AreEqual(0, w.State.Match.RoundWinner);
            Assert.AreEqual(ActionState.KO, w.State.Fighter1.Action);
            Assert.AreEqual(c.Combat.KoHitstop, w.State.Fighter0.HitstopTicks, "KO 명중 히트스탑 20");
            Assert.AreEqual(c.Combat.KoHitstop, w.State.Fighter1.HitstopTicks);
            var ko = w.State;

            for (int i = 1; i < c.Match.KoTicks; i++)
            {
                w.Step(new InputFrame(1, 0, InputButtons.Jump), new InputFrame(-1, 0, InputButtons.Attack));
                Assert.AreEqual(MatchPhase.KO, w.State.Match.Phase, $"KO {i}틱째");
                Assert.AreEqual(ko.Fighter0.Position, w.State.Fighter0.Position, "KO 동안 두 파이터 모두 정지");
                Assert.AreEqual(ko.Fighter1.Position, w.State.Fighter1.Position);
            }

            w.Step(default, default);
            Assert.AreEqual(MatchPhase.RoundEnd, w.State.Match.Phase, "KO 60틱 뒤 RoundEnd");
            Assert.IsTrue(Has(w, SimEventType.RoundEnd, 0), "RoundEnd 이벤트(승자 P1)");
            Assert.AreEqual(1, w.State.Match.Score0);
            Assert.AreEqual(0, w.State.Match.Score1);

            w.Step(default, default);
            Assert.AreEqual(MatchPhase.RoundTransition, w.State.Match.Phase);
            Assert.IsTrue(Has(w, SimEventType.RoundTransition), "라운드 사이에 RoundTransition 이벤트");

            w.Step(default, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(2, w.State.Match.Round);
            Assert.AreEqual(c.Combat.MaxHealth, w.State.Fighter0.Health);
            Assert.AreEqual(c.Combat.MaxHealth, w.State.Fighter1.Health);
            Assert.AreEqual(SimState.SpawnCenter(c, w.Arena, 0), w.State.Fighter0.Position, "시작 위치로");
            Assert.AreEqual(SimState.SpawnCenter(c, w.Arena, 1), w.State.Fighter1.Position);
            Assert.AreEqual(ActionState.Idle, w.State.Fighter1.Action);
            Assert.AreEqual(1, w.State.Match.Score0, "점수는 유지");

            for (int i = 0; i < c.Match.CountdownTicks; i++)
                w.Step(default, default);
            Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
            Assert.IsTrue(Has(w, SimEventType.RoundStart));
        }

        [Test]
        public void SimultaneousKnockOut_IsDrawWithoutPoints()
        {
            // 체력 5인 두 파이터가 같은 틱에 양쪽 벽에 벽꽝(데미지 6)한다.
            var w = TestWorlds.CreateAt(new Vec2(-14.45f, 0f), new Vec2(14.45f, 0f));
            foreach (int k in new[] { 0, 1 })
            {
                ref var f = ref (k == 0 ? ref w.State.Fighter0 : ref w.State.Fighter1);
                f.RestartAction(ActionState.Hitstun);
                f.StunTicks = 30;
                f.Health = 5;
                f.Velocity = new Vec2(k == 0 ? -20f : 20f, 0f);
            }
            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.WallSplat, 0) && Has(w, SimEventType.WallSplat, 1), "같은 틱에 둘 다 벽꽝");
            Assert.IsTrue(Has(w, SimEventType.KO, 0) && Has(w, SimEventType.KO, 1), "둘 다 KO");
            Assert.AreEqual(MatchPhase.KO, w.State.Match.Phase);
            Assert.AreEqual(-1, w.State.Match.RoundWinner, "무승부");

            for (int i = 0; i < w.Config.Match.KoTicks; i++)
                w.Step(default, default);
            Assert.AreEqual(MatchPhase.RoundEnd, w.State.Match.Phase);
            Assert.IsTrue(Has(w, SimEventType.RoundEnd, SimEvent.NoFighter), "무승부 RoundEnd");
            Assert.AreEqual(0, w.State.Match.Score0, "점수 변화 없음");
            Assert.AreEqual(0, w.State.Match.Score1, "점수 변화 없음");

            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.RoundTransition), "무승부도 다음 라운드로");
            w.Step(default, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(2, w.State.Match.Round);
        }

        [Test]
        public void FifthPoint_EndsMatch_ThenAnyButtonStartsNewMatch()
        {
            var w = KnockOutP2(5);
            w.State.Match.Score0 = w.Config.Match.RoundsToWin - 1;
            int ticks = 0;
            while (w.State.Match.Phase != MatchPhase.MatchEnd && ticks++ < 200)
            {
                w.Step(default, default);
                Assert.IsFalse(Has(w, SimEventType.RoundTransition), "마지막 라운드 뒤에는 RoundTransition이 없음");
            }
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
            Assert.IsTrue(Has(w, SimEventType.MatchEnd, 0), "MatchEnd 이벤트(승자 P1)");
            Assert.AreEqual(w.Config.Match.RoundsToWin, w.State.Match.Score0);

            int minTicks = w.Config.Match.MatchEndMinTicks;
            for (int i = 1; i <= minTicks; i++)
            {
                // 결과 화면을 연타로 넘기지 않게 MatchEndMinTicks 전에는 버튼을 받지 않는다.
                bool press = i == minTicks / 2 || i == minTicks;
                w.Step(default, new InputFrame(0, 0, press ? InputButtons.Jump : InputButtons.None));
                if (i < minTicks)
                    Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase, $"MatchEnd {i}틱째");
            }
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase, "아무 버튼으로 새 경기");
            Assert.AreEqual(0, w.State.Match.Score0);
            Assert.AreEqual(0, w.State.Match.Score1);
            Assert.AreEqual(1, w.State.Match.Round);
        }

        [Test]
        public void ReplaceConfig_OnlyAtRoundTransition_AppliesToNextRound()
        {
            var w = KnockOutP2(5);
            var combat = CombatParams.Default;
            combat.MaxHealth = 150;
            var next = new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Test);
            Assert.Throws<InvalidOperationException>(() => w.ReplaceConfig(next), "KO 중에는 바꿀 수 없음");

            while (w.State.Match.Phase != MatchPhase.RoundTransition)
                w.Step(default, default);
            w.ReplaceConfig(next);
            w.Step(default, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(150, w.State.Fighter0.Health, "다음 라운드부터 새 설정");
            Assert.AreEqual(150, w.State.Fighter1.Health);
        }

        [Test]
        public void FullMatch_BetweenBots_ReachesMatchEnd()
        {
            var combat = CombatParams.Default;
            combat.MaxHealth = 30;
            var match = MatchParams.Default;
            match.RoundsToWin = 3;
            var config = new SimConfig(MovementParams.Default, match, combat, CombatMode.Test);
            var w = TestWorlds.Create(MatchPhase.Countdown, 5, config);
            var bot0 = new CombatBot(101);
            var bot1 = new CombatBot(202);
            var counts = new CombatEventCounts();
            int rounds = 0;
            for (int t = 0; t < 20000 && w.State.Match.Phase != MatchPhase.MatchEnd; t++)
            {
                var s = w.State;
                w.Step(bot0.Next(s.Fighter0, s.Fighter1, t), bot1.Next(s.Fighter1, s.Fighter0, t));
                counts.Add(w.Events);
                if (Has(w, SimEventType.RoundEnd))
                    rounds++;
            }
            string report = $"[Knockbox][CP4-5] 봇 경기 {rounds}라운드 {w.State.Match.Score0}:{w.State.Match.Score1} ({w.State.Tick}틱) — {counts}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase, "경기가 끝나야 함");
            Assert.AreEqual(3, Math.Max(w.State.Match.Score0, w.State.Match.Score1));
            Assert.AreEqual(rounds - 1, counts.RoundTransition, "마지막을 뺀 라운드 사이마다 RoundTransition");
        }
    }
}
