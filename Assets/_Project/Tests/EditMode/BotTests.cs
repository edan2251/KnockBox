using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>F6 상대와 봇 영상이 쓰는 모드별 봇(CombatBot.Smart, BotInputSource).</summary>
    public class BotTests
    {
        static SimConfig Config(CombatMode mode, bool grab) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, mode, grab);

        /// <summary>두 봇이 맞붙어 한 라운드가 끝날 때까지(또는 상한까지) 진행한다.</summary>
        static (SimWorld world, int fightingTicks, bool ended) RunRound(CombatMode mode, bool grab, string map, uint seed,
            System.Action<SimWorld> perTick = null, int maxTicks = 90 + 3600)
        {
            var config = Config(mode, grab);
            var arena = ArenaFiles.LoadByName(map);
            var world = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            var p1 = new BotInputSource(() => world, 0, 1000u + seed * 7u);
            var p2 = new BotInputSource(() => world, 1, 2000u + seed * 13u);
            int fighting = 0;
            for (int t = 0; t < maxTicks; t++)
            {
                if (world.State.Match.Phase == MatchPhase.Fighting)
                    fighting++;
                int tick = world.State.Tick;
                world.Step(p1.Poll(tick), p2.Poll(tick));
                perTick?.Invoke(world);
                for (int i = 0; i < world.Events.Count; i++)
                {
                    if (world.Events[i].Type == SimEventType.RoundEnd)
                        return (world, fighting, true);
                }
            }
            return (world, fighting, false);
        }

        [Test]
        public void ChargeBot_ChargesToDifferentPowers_AndLungesTowardOpponent()
        {
            int lunges = 0, slow = 0, fast = 0, maxCharge = 0;
            float min = CombatParams.Default.Charge.LungeMinSpeed, max = CombatParams.Default.Charge.LungeMaxSpeed;
            for (uint seed = 1; seed <= 6; seed++)
            {
                RunRound(CombatMode.Charge, true, "Box", seed, w =>
                {
                    if (w.State.Fighter0.Action == ActionState.Charge && w.State.Fighter0.ActionTick > maxCharge)
                        maxCharge = w.State.Fighter0.ActionTick;
                    for (int i = 0; i < w.Events.Count; i++)
                    {
                        var e = w.Events[i];
                        if (e.Type != SimEventType.LungeStart)
                            continue;
                        lunges++;
                        if (e.Strength < min + 4f) // 충전 9틱 이하
                            slow++;
                        if (e.Strength > (min + max) * 0.5f)
                            fast++;
                    }
                });
            }
            TestContext.WriteLine($"돌진 {lunges}, 약한 돌진 {slow}, 절반 이상 충전 {fast}, 최대 충전 {maxCharge}틱");
            Assert.Greater(lunges, 20);
            Assert.Greater(slow, 0, "가까우면 짧게 떼는 돌진도 있어야 함");
            Assert.Greater(fast, lunges / 5, "충전을 길게 한 돌진이 충분히 있어야 함");
            Assert.GreaterOrEqual(maxCharge, 20);
        }

        [Test]
        public void SwingBot_UsesUpSwingAndDownSlam()
        {
            int forward = 0, up = 0, down = 0;
            for (uint seed = 1; seed <= 6; seed++)
            {
                RunRound(CombatMode.Swing, true, "Box", seed, w =>
                {
                    for (int i = 0; i < w.Events.Count; i++)
                    {
                        var e = w.Events[i];
                        if (e.Type != SimEventType.AttackStart)
                            continue;
                        int id = (int)e.Strength;
                        forward += id == AttackIds.SwingForward ? 1 : 0;
                        up += id == AttackIds.SwingUp ? 1 : 0;
                        down += id == AttackIds.SwingDown ? 1 : 0;
                    }
                });
            }
            TestContext.WriteLine($"앞 {forward}, 위 {up}, 내려찍기 {down}");
            Assert.Greater(forward, 20);
            Assert.Greater(up, 0);
            Assert.Greater(down, 0);
        }

        [TestCase(CombatMode.Charge, false)]
        [TestCase(CombatMode.Charge, true)]
        [TestCase(CombatMode.Swing, false)]
        [TestCase(CombatMode.Swing, true)]
        public void SmartBotRounds_EndInReasonableTime_OnEveryMap(CombatMode mode, bool grab)
        {
            var report = new System.Text.StringBuilder();
            foreach (string map in new[] { "Box", "Towers", "Pit" })
            {
                int ended = 0, total = 0, hits = 0, spikes = 0;
                for (uint seed = 1; seed <= 10; seed++)
                {
                    var (_, fighting, done) = RunRound(mode, grab, map, seed, w =>
                    {
                        for (int i = 0; i < w.Events.Count; i++)
                        {
                            hits += w.Events[i].Type == SimEventType.Hit ? 1 : 0;
                            spikes += w.Events[i].Type == SimEventType.Spike ? 1 : 0;
                        }
                    });
                    if (done)
                    {
                        ended++;
                        total += fighting;
                    }
                }
                report.Append($"{map}: {ended}/10 끝남, 평균 {(ended > 0 ? total / 60f / ended : 0f):F1}초, 명중 {hits}, 가시 {spikes}  ");
                Assert.GreaterOrEqual(ended, 8, $"{mode} grab {grab} {map}: 60초 안에 끝난 라운드가 너무 적음");
            }
            TestContext.WriteLine($"{mode} grab {grab}: {report}");
        }

        [TestCase(CombatMode.Charge, "Towers", 5u)]
        [TestCase(CombatMode.Swing, "Pit", 6u)]
        public void SmartBotFight_IsMirrored_AndPassesSyncTest(CombatMode mode, string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = Config(mode, true);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 9, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var p1 = new BotInputSource(() => normal, 0, seed * 13u);
            var p2 = new BotInputSource(() => normal, 1, seed * 29u);
            for (int t = 0; t < 3600; t++)
            {
                int tick = normal.State.Tick;
                var a = p1.Poll(tick);
                var b = p2.Poll(tick);
                normal.Step(a, b);
                mirrored.Step(SymmetryTests.Mirror(b), SymmetryTests.Mirror(a));
                sync.Advance(a, b);
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
        }

        [Test]
        public void BotInputSource_RebuildsBotWhenModeChanges_AndResetRestarts()
        {
            SimWorld world = TestWorlds.Create(MatchPhase.Fighting, 3, Config(CombatMode.Charge, true), ArenaFiles.LoadByName("Box"));
            var source = new BotInputSource(() => world, 1, 77u);
            var first = new InputFrame[120];
            for (int t = 0; t < first.Length; t++)
            {
                first[t] = source.Poll(world.State.Tick);
                world.Step(default, first[t]);
            }
            // 같은 시작에서 Reset하면 같은 입력이 다시 나온다.
            world = TestWorlds.Create(MatchPhase.Fighting, 3, Config(CombatMode.Charge, true), ArenaFiles.LoadByName("Box"));
            source.Reset();
            for (int t = 0; t < first.Length; t++)
            {
                var again = source.Poll(world.State.Tick);
                Assert.AreEqual(first[t], again, $"틱 {t}");
                world.Step(default, again);
            }
            Assert.AreEqual(default(InputFrame), new BotInputSource(() => null, 0, 1u).Poll(0), "경기가 없으면 빈 입력");
        }
    }
}
