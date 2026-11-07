using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 D/E 피드백 2: 벽 기어오르기(벽 쪽 + W, 제한 없음)와 모서리 잡고 오르기("영차"). 벽 점프·미끄러짐은 그대로.
    /// 실험 경기 설정(몸 0.72, 표면 이동 끔). Brawl: 외곽 안쪽 면 x ±17, y ±9. T 가로 x[−5, 5] y[3, 4]. ㄴ 세로 x[−12, −11] y[−5, −1].
    /// </summary>
    public class BrawlClimbTests
    {
        const float Half = 0.36f;
        const float WallX = 17f - Half;
        const float FloorY = -9f + Half;

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimConfig Config() =>
            new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl);

        static SimWorld World(Vec2 p1, bool airborne = true)
        {
            var w = TestWorlds.CreateAt(p1, new Vec2(1.5f, 4f + Half), Config(), Arena);
            if (airborne)
            {
                var s = w.State;
                s.Fighter0.Grounded = false;
                s.Fighter0.GroundBox = FighterState.NoBox;
                s.Fighter0.RestartAction(ActionState.Airborne);
                w.State = s;
            }
            return w;
        }

        static InputFrame In(int x, int y, InputButtons b = InputButtons.None) => new InputFrame(x, y, b);

        static void Run(SimWorld w, InputFrame input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                w.Step(input, default);
        }

        static void RunUntil(SimWorld w, InputFrame input, System.Func<FighterState, bool> done, int limit = 120)
        {
            for (int i = 0; i < limit && !done(w.State.Fighter0); i++)
                w.Step(input, default);
            var f = w.State.Fighter0;
            Assert.IsTrue(done(f), $"조건에 닿지 못함: {f.Position} {f.Action} 접지 {f.Grounded} 벽 {f.WallSide}");
        }

        static FighterState P1(SimWorld w) => w.State.Fighter0;

        [Test]
        public void TowardWallPlusW_Climbs_TowardWallOnly_Slides()
        {
            var config = Config();
            var w = World(new Vec2(WallX - 0.3f, 0f));
            RunUntil(w, In(1, 1), f => f.Action == ActionState.Climb, 20);
            float y = P1(w).Position.Y;
            Run(w, In(1, 1), 30);
            Assert.AreEqual(ActionState.Climb, P1(w).Action);
            Assert.AreEqual(y + config.Combat.Body.ClimbSpeed * 0.5f, P1(w).Position.Y, 0.05f, "30틱 동안 일정 속도로 오름");

            Run(w, In(1, 0), 20);
            Assert.AreEqual(ActionState.WallSlide, P1(w).Action, "W 없이 벽 쪽만: 미끄러짐");
            Assert.Less(P1(w).Velocity.Y, 0f);
        }

        [Test]
        public void ClimbFromFloor_AndWallJumpStillWorks()
        {
            var w = World(new Vec2(WallX, FloorY), false);
            Run(w, In(0, 0), 2);
            Assert.IsTrue(P1(w).Grounded);
            Run(w, In(1, 1), 10);
            Assert.AreEqual(ActionState.Climb, P1(w).Action, "바닥에서 바로 기어오름");
            Assert.Greater(P1(w).Position.Y, FloorY + 0.3f);

            w.Step(In(1, 1, InputButtons.Jump), default);
            bool wallJump = false;
            for (int e = 0; e < w.Events.Count; e++)
                wallJump |= w.Events[e].Type == SimEventType.WallJump;
            Assert.IsTrue(wallJump, "기어오르는 중 점프 = 벽 점프");
            Assert.Less(P1(w).Velocity.X, 0f);
            Assert.Greater(P1(w).Velocity.Y, 0f);
        }

        [Test]
        public void OuterWall_ClimbsUpToCeiling_NoLedge()
        {
            var w = World(new Vec2(WallX - 0.3f, 4f));
            Run(w, In(1, 1), 200);
            Assert.AreEqual(ActionState.Climb, P1(w).Action, "천장 아래에서 계속 붙어 있음(모서리 없음)");
            Assert.AreEqual(9f - Half, P1(w).Position.Y, 0.05f);
        }

        [Test]
        public void ClimbingInnerWall_MantlesOntoTop()
        {
            // ㄴ 세로 막대 왼쪽 면(x −12)을 기어올라 윗면(y −1)에 선다.
            var w = World(new Vec2(-12f - Half - 0.2f, -3.5f));
            RunUntil(w, In(1, 1), f => f.Action == ActionState.Mantle, 60);
            RunUntil(w, In(1, 1), f => f.Action != ActionState.Mantle, 30);
            Run(w, In(0, 0), 2);
            var f1 = P1(w);
            Assert.IsTrue(f1.Grounded, $"{f1.Position}");
            Assert.AreEqual(-1f + Half, f1.Position.Y, 0.05f, "막대 윗면에 섬");
            Assert.Greater(f1.Position.X, -12f);
        }

        [Test]
        public void AirborneTowardLedgeAtHeadHeight_GrabsAndPullsUp_WithoutW()
        {
            var config = Config();
            // T 가로 막대 왼쪽 끝(x −5, 윗면 y 4), 머리가 윗면보다 조금 아래.
            var w = World(new Vec2(-5f - Half - 0.1f, 4f - Half - 0.2f));
            RunUntil(w, In(1, 0), f => f.Action == ActionState.Mantle, 10);
            int ticks = 0;
            while (P1(w).Action == ActionState.Mantle && ticks < 60)
            {
                w.Step(In(1, 0), default);
                ticks++;
            }
            Assert.That(ticks, Is.InRange(config.Combat.Body.MantleTicks - 2, config.Combat.Body.MantleTicks + 1), "약 0.25초");
            Run(w, In(0, 0), 2);
            Assert.IsTrue(P1(w).Grounded);
            Assert.AreEqual(4f + Half, P1(w).Position.Y, 0.05f);
        }

        [Test]
        public void HitDuringMantle_Cancels()
        {
            var w = World(new Vec2(-5f - Half - 0.1f, 4f - Half - 0.2f));
            RunUntil(w, In(1, 0), f => f.Action == ActionState.Mantle, 10);
            var s = w.State;
            s.Fighter0.StunTicks = 10;
            s.Fighter0.RestartAction(ActionState.Hitstun);
            w.State = s;
            Run(w, In(1, 0), 2);
            Assert.AreEqual(0, P1(w).MantleTicks);
            Assert.AreEqual(ActionState.Hitstun, P1(w).Action);
        }

        [Test]
        public void ClimbScenario_DeterministicSaveRestoreSyncTestReplay_BotSmoke()
        {
            var config = Config();
            SimWorld world = null;
            var bot0 = new BrawlBot(() => world, 0, 13u);
            var bot1 = new BrawlBot(() => world, 1, 14u);
            world = new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, 9, MatchPhase.Countdown));
            const int ticks = 1800;
            const int openingTicks = 180;
            var p1 = new InputFrame[ticks];
            var p2 = new InputFrame[ticks];
            var rng = new SimRng(91);
            int climbs = 0, mantles = 0;
            for (int t = 0; t < ticks; t++)
            {
                int tick = world.State.Tick;
                p1[t] = bot0.Poll(tick);
                p2[t] = bot1.Poll(tick);
                // 전투 결과가 바뀌어도 기어오르기 상태를 반드시 기록한다. 카운트다운을 포함한 첫 180틱은
                // P1이 가까운 외곽 왼벽으로 A+W를 유지하고 P2는 쉰다. 그 뒤에는 기존 봇 혼합 입력을 쓴다.
                if (t < openingTicks)
                {
                    p1[t] = In(-1, 1);
                    p2[t] = default;
                }
                else if ((t / 20) % 2 == 0)
                    p1[t] = new InputFrame((t / 40) % 2 == 0 ? 1 : -1, 1, rng.Range(0, 30) == 0 ? InputButtons.Jump : InputButtons.None, p1[t].Aim);
                world.Step(p1[t], p2[t]);
                var s = world.State;
                for (int k = 0; k < 2; k++)
                {
                    var f = s.GetFighter(k);
                    Assert.IsFalse(float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y), $"NaN 틱 {t}");
                    Assert.IsFalse(TerrainCollision.OverlapsSolid(f.Position, config.HalfBody - 0.01f, Arena), $"P{k + 1} 지형 속 틱 {t} {f.Position}");
                    if (f.Action == ActionState.Climb) climbs++;
                    if (f.Action == ActionState.Mantle) mantles++;
                }
                for (int e = 0; e < world.Events.Count; e++)
                    Assert.AreNotEqual(SimEventType.OutOfBounds, world.Events[e].Type, $"틱 {t}");
                if (t == openingTicks - 1)
                    Assert.Greater(climbs, 0, "고정 도입부에서 실제 Climb 상태에 들어가야 한다");
            }
            TestContext.WriteLine($"[CR-004 기어오르기] 봇 30초: 기어오르기 {climbs}틱, 모서리 오르기 {mantles}틱");
            Assert.Greater(climbs, 0);

            SimWorld Fresh() => new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, 9, MatchPhase.Countdown));
            var a = Fresh();
            var b = Fresh();
            for (int t = 0; t < ticks; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"틱 {t + 1}\n{StateDiff.Describe(a.State, b.State)}");
            }
            var w = Fresh();
            for (int t = 0; t < 900; t++)
                w.Step(p1[t], p2[t]);
            var snapshot = w.State;
            for (int t = 900; t < ticks; t++)
                w.Step(p1[t], p2[t]);
            uint first = w.ComputeChecksum();
            w.State = snapshot;
            for (int t = 900; t < ticks; t++)
                w.Step(p1[t], p2[t]);
            Assert.AreEqual(first, w.ComputeChecksum(), "저장/복원 뒤 재진행");
            var synced = Fresh();
            var sync = new SyncTest<SimWorld>(synced, 7);
            for (int t = 0; t < ticks; t++)
                sync.Advance(p1[t], p2[t]);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            var spawn0 = SimState.SpawnCenter(config, Arena, 0);
            var spawn1 = SimState.SpawnCenter(config, Arena, 1);
            var recording = InputRecording.Begin(config, Arena, 9, spawn0, spawn1, MatchPhase.Countdown, RuleSet.Brawl.Format());
            var rw = new SimWorld(config, Arena, SimState.CreateRound(config, Arena, 9, spawn0, spawn1, MatchPhase.Countdown));
            for (int t = 0; t < ticks; t++)
            {
                recording.Append(p1[t], p2[t]);
                rw.Step(p1[t], p2[t]);
            }
            recording.Finish(rw.ComputeChecksum());
            Assert.AreEqual(recording.FinalChecksum, InputRecording.FromBytes(recording.ToBytes()).Replay(config, Arena));
        }
    }
}
