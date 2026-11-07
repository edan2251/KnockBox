using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP4-6: 공격과 피격이 들어간 경기를 P1↔P2, x를 뒤집어 진행해도 결과가 뒤집어서 일치한다.
    /// CP4-7: 전투(명중, 가드, 패링, 클래시, 벽꽝, KO, 라운드 전환)가 들어간 상태로 SyncTest 통과.
    /// 두 테스트 모두 상대를 보고 싸우는 CombatBot을 쓴다.
    /// </summary>
    public class CombatSymmetryTests
    {
        const int Ticks = 3600;

        /// <summary>KO와 라운드 전환이 여러 번 나오도록 체력을 줄인 설정.</summary>
        static SimConfig ShortRounds()
        {
            var combat = CombatParams.Default;
            combat.MaxHealth = 40;
            var match = MatchParams.Default;
            match.RoundsToWin = 3;
            return new SimConfig(MovementParams.Default, match, combat, CombatMode.Test);
        }

        [TestCase("Box", 1u)]
        [TestCase("Box", 2u)]
        [TestCase("Towers", 3u)]
        [TestCase("Pit", 4u)]
        public void MirroredFight_ProducesMirroredResult(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = ShortRounds();
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var bot0 = new CombatBot(seed * 31u);
            var bot1 = new CombatBot(seed * 57u);
            var counts = new CombatEventCounts();
            for (int t = 0; t < Ticks; t++)
            {
                var s = normal.State;
                var p1 = bot0.Next(s.Fighter0, s.Fighter1, t);
                var p2 = bot1.Next(s.Fighter1, s.Fighter0, t);
                normal.Step(p1, p2);
                mirrored.Step(SymmetryTests.Mirror(p2), SymmetryTests.Mirror(p1));
                counts.Add(normal.Events);

                var a = normal.State;
                var b = mirrored.State;
                SymmetryTests.AssertMirrored(a.Fighter0, b.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(a.Fighter1, b.Fighter0, t + 1, "P2↔P1'");
                Assert.AreEqual(a.Match.Phase, b.Match.Phase, $"틱 {t + 1} phase");
                Assert.AreEqual(a.Match.Score0, b.Match.Score1, $"틱 {t + 1} 점수");
                Assert.AreEqual(a.Match.Score1, b.Match.Score0, $"틱 {t + 1} 점수");
                int winner = a.Match.RoundWinner;
                Assert.AreEqual(winner < 0 ? winner : 1 - winner, b.Match.RoundWinner, $"틱 {t + 1} 라운드 승자");
            }
            string report = $"[Knockbox][CP4-6] {map} 시드 {seed}: {counts}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.GreaterOrEqual(counts.Hit + counts.Spike, 5, "명중(Pit은 가시 포함)이 충분히 나와야 대칭 검증 의미가 있음");
            Assert.Greater(counts.GuardHit + counts.Parry + counts.Clash, 0, "가드·패링·클래시 중 하나는 나와야 함");
            Assert.Greater(counts.KO, 0, "KO가 나와야 함");
        }

        [Test]
        public void MirroredWallSplat_ProducesMirroredResult()
        {
            // P1이 오른쪽 벽 앞의 P2를 강하게 쳐서 벽꽝. 뒤집은 쪽은 P2가 왼쪽 벽 앞의 P1을 친다.
            var combat = CombatParams.Default;
            combat.TestAttack.KnockbackStrength = 24f;
            combat.TestAttack.KnockbackDirection = new Vec2(1f, 0.2f);
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Test);
            var normal = TestWorlds.CreateAt(new Vec2(12.6f, -7.55f), new Vec2(13.8f, -7.55f), config);
            var mirrored = TestWorlds.CreateAt(new Vec2(-13.8f, -7.55f), new Vec2(-12.6f, -7.55f), config);
            bool splat = false;
            for (int t = 0; t < 180; t++)
            {
                var p1 = new InputFrame(0, 0, t == 0 || t == 40 ? InputButtons.Attack : InputButtons.None);
                normal.Step(p1, default);
                mirrored.Step(default, SymmetryTests.Mirror(p1));
                for (int i = 0; i < normal.Events.Count; i++)
                    splat |= normal.Events[i].Type == SimEventType.WallSplat;
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "공격자");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "피격자");
            }
            Assert.IsTrue(splat, "벽꽝이 나와야 함");
        }

        [TestCase("Box")]
        [TestCase("Towers")]
        [TestCase("Pit")]
        public void SyncTest_WithCombat_NoMismatch(string map)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = ShortRounds();
            var world = TestWorlds.Create(MatchPhase.Countdown, 9, config, arena);
            var plain = TestWorlds.Create(MatchPhase.Countdown, 9, config, arena);
            var sync = new SyncTest<SimWorld>(world, SyncTest<SimWorld>.DefaultRollbackTicks);
            var bot0 = new CombatBot(77u);
            var bot1 = new CombatBot(88u);
            var counts = new CombatEventCounts();
            for (int t = 0; t < Ticks; t++)
            {
                var s = world.State;
                var p1 = bot0.Next(s.Fighter0, s.Fighter1, t);
                var p2 = bot1.Next(s.Fighter1, s.Fighter0, t);
                sync.Advance(p1, p2);
                plain.Step(p1, p2);
                counts.Add(plain.Events);
            }
            string report = $"[Knockbox][CP4-7] {map} SyncTest {Ticks}틱(되감기 {sync.RollbackTicks}): 불일치 {sync.MismatchCount}, {counts}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(plain.ComputeChecksum(), world.ComputeChecksum(), "SyncTest를 거친 결과와 그냥 진행한 결과가 같아야 함");
            Assert.Greater(counts.Hit, 0);
            Assert.Greater(counts.KO, 0);
            Assert.Greater(counts.RoundTransition, 0, "라운드 전환을 지나며 되감아야 함");
        }
    }
}
