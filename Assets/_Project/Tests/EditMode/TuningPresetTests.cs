using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP9: 비교용 튜닝 프리셋(기본, 더 빠르게)과 사용자 보고용 봇 대전 측정.</summary>
    public class TuningPresetTests
    {
        const string FastAssetPath = "Assets/_Project/Data/Tuning/CombatTuningFast.asset";
        const string DefaultAssetPath = "Assets/_Project/Data/Tuning/CombatTuning.asset";
        static readonly string[] Maps = { "Box", "Towers", "Pit" };

        static SimConfig Config(in CombatParams combat, CombatMode mode, bool grab) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, combat, mode, grab);

        [Test]
        public void FastPreset_IsFasterInBothModes()
        {
            var d = CombatParams.Default;
            var f = CombatPresets.Fast;
            Assert.Less(f.Charge.FullChargeTicks, d.Charge.FullChargeTicks);
            Assert.Greater(f.Charge.FullChargeTicks, f.Charge.MinChargeTicks);
            Assert.Less(f.Charge.FullChargeHoldTicks, d.Charge.FullChargeHoldTicks);
            Assert.Greater(f.Charge.LungeMinSpeed, d.Charge.LungeMinSpeed);
            Assert.Greater(f.Charge.LungeMaxSpeed, d.Charge.LungeMaxSpeed);
            Assert.Less(f.Charge.LungeMaxTicks, d.Charge.LungeMaxTicks);
            Assert.Less(f.Swing.Forward.TotalTicks, d.Swing.Forward.TotalTicks);
            Assert.Less(f.Swing.Up.TotalTicks, d.Swing.Up.TotalTicks);
            Assert.Less(f.Swing.Down.TotalTicks, d.Swing.Down.TotalTicks);
            Assert.Less(f.Swing.DashAttackCancelMinTicks, d.Swing.DashAttackCancelMinTicks);
            Assert.Less(f.RecoilTicks, d.RecoilTicks);
            Assert.Less(f.GuardStunTicks, d.GuardStunTicks);
            Assert.Less(f.StaggerTicks, d.StaggerTicks);
            Assert.Less(f.Grab.TotalWhiffTicks, d.Grab.TotalWhiffTicks);
            // 데미지·넉백·체력은 같다(빠르기만 비교).
            Assert.AreEqual(d.MaxHealth, f.MaxHealth);
            Assert.AreEqual(d.Swing.Forward.Damage, f.Swing.Forward.Damage);
            Assert.AreEqual(d.ParryWindowTicks, f.ParryWindowTicks);
        }

        [TestCase(CombatMode.Charge)]
        [TestCase(CombatMode.Swing)]
        public void PresetAssets_MatchCode(CombatMode mode)
        {
            var normal = AssetDatabase.LoadAssetAtPath<CombatTuning>(DefaultAssetPath);
            var fast = AssetDatabase.LoadAssetAtPath<CombatTuning>(FastAssetPath);
            Assert.IsNotNull(normal, DefaultAssetPath);
            Assert.IsNotNull(fast, FastAssetPath);
            Assert.AreEqual("default", normal.PresetName);
            Assert.AreEqual("fast", fast.PresetName);
            Assert.AreEqual(Config(CombatParams.Default, mode, true).ComputeChecksum(), Config(normal.Combat, mode, true).ComputeChecksum());
            Assert.AreEqual(Config(CombatPresets.Fast, mode, true).ComputeChecksum(), Config(fast.Combat, mode, true).ComputeChecksum());
            Assert.AreNotEqual(Config(normal.Combat, mode, true).ComputeChecksum(), Config(fast.Combat, mode, true).ComputeChecksum());
        }

        [TestCase(CombatMode.Charge, "Box", 41u)]
        [TestCase(CombatMode.Charge, "Towers", 42u)]
        [TestCase(CombatMode.Swing, "Box", 43u)]
        [TestCase(CombatMode.Swing, "Pit", 44u)]
        public void FastPreset_MirroredGrabFight_AndSyncTest(CombatMode mode, string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var combat = CombatPresets.Fast;
            combat.MaxHealth = 50;
            var config = Config(combat, mode, true);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new CombatBot(seed * 13u, true);
            var bot1 = new CombatBot(seed * 29u, true);
            var counts = new CombatEventCounts();
            for (int t = 0; t < 3600; t++)
            {
                var s = normal.State;
                var p1 = bot0.Next(s.Fighter0, s.Fighter1, t);
                var p2 = bot1.Next(s.Fighter1, s.Fighter0, t);
                normal.Step(p1, p2);
                mirrored.Step(SymmetryTests.Mirror(p2), SymmetryTests.Mirror(p1));
                sync.Advance(p1, p2);
                counts.Add(normal.Events);
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            TestContext.WriteLine($"[Knockbox][CP9] fast {mode} {map}: {counts}, SyncTest 불일치 {sync.MismatchCount}");
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(counts.Hit + counts.GuardHit + counts.Clash, 0);
        }

        /// <summary>한 설정으로 세 맵에서 봇(또는 무작위 허수아비)끼리 싸운 합계.</summary>
        struct Tally
        {
            public int FightingTicks, Rounds, Hit, Clash, WallSplat, Throw, Parry, GuardHit;

            public void Add(SimEventBuffer events)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    switch (events[i].Type)
                    {
                        case SimEventType.RoundEnd: Rounds++; break;
                        case SimEventType.Hit: Hit++; break;
                        case SimEventType.Clash: Clash++; break;
                        case SimEventType.WallSplat: WallSplat++; break;
                        case SimEventType.Throw: Throw++; break;
                        case SimEventType.Parry: Parry++; break;
                        case SimEventType.GuardHit: GuardHit++; break;
                    }
                }
            }

            public float RoundSeconds => Rounds > 0 ? FightingTicks / 60f / Rounds : float.PositiveInfinity;
            float PerMinute(int n) => n * 3600f / FightingTicks;

            public override string ToString() =>
                $"라운드 {Rounds}, 평균 {RoundSeconds:F1}초 | 분당 명중 {PerMinute(Hit):F1}, 클래시 {PerMinute(Clash):F1}, 가드 {PerMinute(GuardHit):F1}, 패링 {PerMinute(Parry):F1}, 벽꽝 {PerMinute(WallSplat):F1}, 던지기 {PerMinute(Throw):F1}";
        }

        static Tally Measure(in CombatParams combat, CombatMode mode, bool grab, bool randomDummies, int ticksPerMap)
        {
            var tally = new Tally();
            for (int m = 0; m < Maps.Length; m++)
            {
                var arena = ArenaFiles.LoadByName(Maps[m]);
                var config = Config(combat, mode, grab);
                var world = TestWorlds.Create(MatchPhase.Fighting, 21u + (uint)m, config, arena);
                var bot0 = new CombatBot(101u + (uint)m * 7u, grab);
                var bot1 = new CombatBot(211u + (uint)m * 11u, grab);
                var dummy0 = new DummyInputSource(DummyBehavior.Random, 0xA11u + (uint)m);
                var dummy1 = new DummyInputSource(DummyBehavior.Random, 0xB22u + (uint)m);
                for (int t = 0; t < ticksPerMap; t++)
                {
                    var s = world.State;
                    if (s.Match.Phase == MatchPhase.Fighting)
                        tally.FightingTicks++;
                    var p1 = randomDummies ? dummy0.Poll(t) : bot0.Next(s.Fighter0, s.Fighter1, t);
                    var p2 = randomDummies ? dummy1.Poll(t) : bot1.Next(s.Fighter1, s.Fighter0, t);
                    world.Step(p1, p2);
                    tally.Add(world.Events);
                }
            }
            return tally;
        }

        /// <summary>
        /// 사용자 보고(CP9)용 측정: 추천 비교 순서(A 잡기 끔 → A 잡기 켬 → B 잡기 끔 → B 잡기 켬) × 프리셋 2개를
        /// 같은 봇으로 세 맵 10분씩 헤드리스로 돌려 라운드 길이와 분당 사건 수를 남긴다. 무작위 허수아비도 참고로 잰다.
        /// 판정 기준이 아니라 기록이므로, 라운드가 끝나는지만 확인한다.
        /// </summary>
        [Test]
        public void BotComparison_RoundLengthAndEventRates()
        {
            const int TicksPerMap = 36000;
            var report = new System.Text.StringBuilder("[Knockbox][CP9] 봇 대전 비교(세 맵 × 10분, 체력 100)\n");
            var modes = new[] { CombatMode.Charge, CombatMode.Swing };
            foreach (var mode in modes)
            {
                foreach (bool grab in new[] { false, true })
                {
                    for (int p = 0; p < 2; p++)
                    {
                        var combat = p == 0 ? CombatParams.Default : CombatPresets.Fast;
                        var tally = Measure(combat, mode, grab, false, TicksPerMap);
                        report.AppendLine($"  {(mode == CombatMode.Charge ? "A" : "B")} 잡기 {(grab ? "켬" : "끔")} {(p == 0 ? "default" : "fast")}: {tally}");
                        Assert.Greater(tally.Rounds, 0, $"{mode} grab {grab} preset {p}");
                    }
                }
            }
            foreach (var mode in modes)
            {
                var tally = Measure(CombatParams.Default, mode, true, true, TicksPerMap);
                report.AppendLine($"  무작위 허수아비 {(mode == CombatMode.Charge ? "A" : "B")} 잡기 켬 default: {tally}");
            }
            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log(report.ToString());
        }
    }
}
