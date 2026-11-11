using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP4-6: 공격과 피격이 들어간 경기를 P1↔P2, x를 뒤집어 진행해도 결과가 뒤집어서 일치한다.
    /// CP4-7: 전투(명중, 가드, 패링, 클래시, 벽꽝, KO, 라운드 전환)가 들어간 상태로 SyncTest 통과.
    /// 두 테스트 모두 상대를 보고 싸우는 몸 폼 봇(BrawlBot)을 쓴다(2026-11-11 폼 봇 대신).
    /// </summary>
    public class CombatSymmetryTests
    {
        const int Ticks = 3600;

        /// <summary>KO와 라운드 전환이 여러 번 나오도록 체력을 줄인 설정.</summary>
        static SimConfig ShortRounds()
        {
            var combat = BrawlParams.Combat(CombatParams.Default);
            combat.MaxHealth = 25; // 몸 폼 봇은 폼 봇보다 덜 맞히므로 KO가 나오도록 40 → 25(2026-11-11).
            var match = MatchParams.Default;
            match.RoundsToWin = 3;
            return new SimConfig(MovementParams.Default, match, combat, RuleSet.Brawl);
        }

        // 2026-11-11: 몸 폼 봇은 현재 경기 맵(Brawl)에서 싸운다(옛 Box·Towers·Pit에서는 거의 움직이지 않음).
        [TestCase("Brawl", 1u)]
        [TestCase("Brawl", 2u)]
        [TestCase("Brawl", 3u)]
        [TestCase("Brawl", 4u)]
        public void MirroredFight_ProducesMirroredResult(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = ShortRounds();
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var bot0 = new BrawlBot(() => normal, 0, seed * 31u);
            var bot1 = new BrawlBot(() => normal, 1, seed * 57u);
            var counts = new CombatEventCounts();
            for (int t = 0; t < Ticks; t++)
            {
                int tick = normal.State.Tick;
                var p1 = bot0.Poll(tick);
                var p2 = bot1.Poll(tick);
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
            Assert.Greater(counts.Clash + counts.Grab, 0, "클래시·잡기 중 하나는 나와야 함(가드·패링은 폼 전투와 함께 제거)");
            Assert.Greater(counts.KO, 0, "KO가 나와야 함");
        }

        [Test]
        public void MirroredWallSplat_ProducesMirroredResult()
        {
            // P1이 오른쪽 벽 앞의 P2를 강하게 쳐서 벽꽝. 뒤집은 쪽은 P2가 왼쪽 벽 앞의 P1을 친다.
            // 2026-11-11: 시험 공격 대신 몸 폼 주먹(넉백을 키운 변형). 주먹 사거리에 맞춰 간격 0.6.
            var combat = BrawlParams.Combat(CombatParams.Default);
            combat.Body.Punch.KnockbackStrength = 24f;
            combat.Body.Punch.KnockbackDirection = new Vec2(1f, 0.2f);
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, RuleSet.Brawl);
            var normal = TestWorlds.CreateAt(new Vec2(13.2f, -7.55f), new Vec2(13.8f, -7.55f), config);
            var mirrored = TestWorlds.CreateAt(new Vec2(-13.8f, -7.55f), new Vec2(-13.2f, -7.55f), config);
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

        [TestCase("Brawl")]
        public void SyncTest_WithCombat_NoMismatch(string map)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = ShortRounds();
            var world = TestWorlds.Create(MatchPhase.Countdown, 9, config, arena);
            var plain = TestWorlds.Create(MatchPhase.Countdown, 9, config, arena);
            var sync = new SyncTest<SimWorld>(world, SyncTest<SimWorld>.DefaultRollbackTicks);
            var bot0 = new BrawlBot(() => plain, 0, 77u);
            var bot1 = new BrawlBot(() => plain, 1, 88u);
            var counts = new CombatEventCounts();
            for (int t = 0; t < Ticks; t++)
            {
                int tick = plain.State.Tick;
                var p1 = bot0.Poll(tick);
                var p2 = bot1.Poll(tick);
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
