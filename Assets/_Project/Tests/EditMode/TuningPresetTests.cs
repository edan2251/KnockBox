using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP9: 비교용 튜닝 프리셋(기본, 더 빠르게) 에셋과 코드 일치, 빠른 프리셋의 몸 폼 대칭·SyncTest. 폼 모드별 수치 비교와 봇 측정은 폼 전투와 함께 제거(2026-11-11).</summary>
    public class TuningPresetTests
    {
        const string FastAssetPath = "Assets/_Project/Data/Tuning/CombatTuningFast.asset";
        const string DefaultAssetPath = "Assets/_Project/Data/Tuning/CombatTuning.asset";
        /// <summary>현재 몸 폼 규칙. 프리셋 수치에 BrawlParams.Combat을 적용한다(2026-11-11 폼 모드 설정 대신).</summary>
        static SimConfig Config(in CombatParams combat) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, BrawlParams.Combat(combat), RuleSet.Brawl);

        [Test]
        public void PresetAssets_MatchCode()
        {
            var normal = AssetDatabase.LoadAssetAtPath<CombatTuning>(DefaultAssetPath);
            var fast = AssetDatabase.LoadAssetAtPath<CombatTuning>(FastAssetPath);
            Assert.IsNotNull(normal, DefaultAssetPath);
            Assert.IsNotNull(fast, FastAssetPath);
            Assert.AreEqual("default", normal.PresetName);
            Assert.AreEqual("fast", fast.PresetName);
            Assert.AreEqual(Config(CombatParams.Default).ComputeChecksum(), Config(normal.Combat).ComputeChecksum());
            Assert.AreEqual(Config(CombatPresets.Fast).ComputeChecksum(), Config(fast.Combat).ComputeChecksum());
            Assert.AreNotEqual(Config(normal.Combat).ComputeChecksum(), Config(fast.Combat).ComputeChecksum());
        }

        [TestCase("Brawl", 41u)]
        [TestCase("Brawl", 44u)]
        public void FastPreset_MirroredGrabFight_AndSyncTest(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var combat = CombatPresets.Fast;
            combat.MaxHealth = 50;
            var config = Config(combat);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new BrawlBot(() => normal, 0, seed * 13u);
            var bot1 = new BrawlBot(() => normal, 1, seed * 29u);
            var counts = new CombatEventCounts();
            for (int t = 0; t < 3600; t++)
            {
                int tick = normal.State.Tick;
                var p1 = bot0.Poll(tick);
                var p2 = bot1.Poll(tick);
                normal.Step(p1, p2);
                mirrored.Step(SymmetryTests.Mirror(p2), SymmetryTests.Mirror(p1));
                sync.Advance(p1, p2);
                counts.Add(normal.Events);
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            TestContext.WriteLine($"[Knockbox][CP9] fast Body {map}: {counts}, SyncTest 불일치 {sync.MismatchCount}");
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(counts.Hit + counts.Clash, 0);
        }
    }
}
