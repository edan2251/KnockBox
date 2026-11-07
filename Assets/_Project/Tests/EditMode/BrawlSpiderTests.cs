using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 E(D/E 피드백, "벽에 붙어 다니는 거미"): 어느 면이든 닿으면 그쪽으로 중력이 바뀌어 서고, 그 면을 따라 걷고(오목 모서리는 닿아서,
    /// 볼록 모서리는 떠 있는 지형 중력장으로 감아 돌며), 점프로 떠난다. 벽 점프·벽 미끄러짐은 없다. 몸 0.72(반 0.36).
    /// Brawl 맵: 외곽 안쪽 면 x ±17, y ±9. T 가로 막대 x[−5, 5] y[3, 4], 기둥 x[−1, 1] y[−1, 3]. ㄴ 세로 x[−12, −11] y[−5, −1], 가로 x[−12, −5] y[−6, −5].
    /// </summary>
    public class BrawlSpiderTests
    {
        const float Half = 0.36f;
        const float WallX = 17f - Half;
        const float CeilY = 9f - Half;
        const float FloorY = -9f + Half;
        const float BarTopY = 4f + Half;

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimConfig Config() =>
            new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default, 2), RuleSet.Brawl);

        /// <summary>
        /// P1을 pos에 중력 g로 둔다. g가 아래가 아니면 그 면에 정확히 닿아 선 상태로 시작한다(공중이면 다음 틱에 아래 중력으로 돌아가므로).
        /// P2는 길을 막지 않게 T 윗면에 세운다.
        /// </summary>
        static SimWorld World(Vec2 pos, sbyte g = SpiderMovement.Down, int settle = 6)
        {
            var config = Config();
            var w = TestWorlds.CreateAt(pos, new Vec2(1.5f, BarTopY), config, Arena);
            var s = w.State;
            s.Fighter0.GravityDir = g;
            s.Fighter0.Grounded = g != SpiderMovement.Down;
            s.Fighter0.GroundBox = FighterState.NoBox;
            w.State = s;
            for (int i = 0; i < settle; i++)
                w.Step(default, default);
            return w;
        }

        static InputFrame Keys(int keys, InputButtons buttons = InputButtons.None, int moveY = 0)
        {
            int x = keys == 2 ? 1 : keys == 1 ? -1 : 0;
            if (keys == 3)
                buttons |= InputButtons.BothHorizontal;
            return new InputFrame(x, moveY, buttons);
        }

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
            Assert.IsTrue(done(f), $"조건에 닿지 못함: {f.Position} g{f.GravityDir} {f.Action} 접지 {f.Grounded}");
        }

        static FighterState P1(SimWorld w) => w.State.Fighter0;

        [Test]
        public void BodyIs20PercentSmaller()
        {
            Assert.AreEqual(0.36f, Config().HalfBody, 1e-5f);
        }

        // 붙기와 오목 모서리 --------------------------------------------------------------

        [Test]
        public void HoldingD_ClimbsOuterFrameCounterClockwise_NoWallSlide()
        {
            var w = World(new Vec2(12f, FloorY));
            Assert.AreEqual(SpiderMovement.Down, P1(w).GravityDir);
            bool slid = false;
            void Watch(int limit, sbyte g)
            {
                for (int i = 0; i < limit && !(P1(w).GravityDir == g && P1(w).Grounded); i++)
                {
                    w.Step(Keys(2), default);
                    slid |= P1(w).Action == ActionState.WallSlide;
                }
                Assert.AreEqual(g, P1(w).GravityDir, $"{P1(w).Position}");
            }
            Watch(120, SpiderMovement.Right);
            float y = P1(w).Position.Y;
            Run(w, Keys(2), 5);
            Assert.Greater(P1(w).Position.Y, y, "오른벽 위로");
            Watch(300, SpiderMovement.Up);
            float x = P1(w).Position.X;
            Run(w, Keys(2), 5);
            Assert.Less(P1(w).Position.X, x, "천장 왼쪽으로");
            Watch(400, SpiderMovement.Left);
            y = P1(w).Position.Y;
            Run(w, Keys(2), 5);
            Assert.Less(P1(w).Position.Y, y, "왼벽 아래로");
            Watch(300, SpiderMovement.Down);
            Assert.IsFalse(slid, "벽 미끄러짐 없음");
        }

        [Test]
        public void JumpFromWall_PushesAwayAlongNormal_NotWallJump()
        {
            var w = World(new Vec2(WallX, 0f), SpiderMovement.Right);
            Assert.IsTrue(P1(w).Grounded, "오른벽에 섬");
            w.Step(Keys(0, InputButtons.Jump), default);
            Assert.Less(P1(w).Velocity.X, -5f, "벽에서 수직으로 떨어져 나감");
            Assert.Less(SimMath.Abs(P1(w).Velocity.Y), 1f, "벽 점프의 위쪽 성분 없음");
        }

        // 볼록 모서리와 중력장 ------------------------------------------------------------

        [Test]
        public void HoldingD_WrapsAroundFloatingT_Clockwise()
        {
            var w = World(new Vec2(-3f, BarTopY));
            Assert.IsTrue(P1(w).Grounded);
            RunUntil(w, Keys(2), f => f.GravityDir == SpiderMovement.Left && f.Grounded, 90);
            Assert.Greater(P1(w).Position.X, 5f, "막대 오른쪽 끝을 돌아 옆면");
            RunUntil(w, Keys(2), f => f.GravityDir == SpiderMovement.Up && f.Grounded, 60);
            Assert.Less(P1(w).Position.Y, 3f, "막대 아랫면");
            RunUntil(w, Keys(2), f => f.GravityDir == SpiderMovement.Right && f.Grounded, 200);
            Assert.Less(P1(w).Position.X, 0f, "기둥을 돌아 왼쪽 면(위로 올라감)");
            RunUntil(w, Keys(2), f => f.GravityDir == SpiderMovement.Down && f.Grounded, 200);
            Assert.Greater(P1(w).Position.Y, 4f, "다시 윗면");
        }

        [Test]
        public void FieldPullsAirborneTowardFloatingTerrain()
        {
            // 기둥 밑면(y −1) 아래 공중: 중력장(2칸) 안이라 위로 끌려가 밑면에 선다.
            var w = World(new Vec2(0f, -1f - Half - 1.2f), SpiderMovement.Down, 0);
            RunUntil(w, Keys(0), f => f.GravityDir == SpiderMovement.Up && f.Grounded, 60);
        }

        [Test]
        public void JumpFromUnderside_LeavesTheSurface()
        {
            var w = World(new Vec2(-3f, 3f - Half), SpiderMovement.Up);
            Assert.IsTrue(P1(w).Grounded, "막대 아랫면에 매달림");
            w.Step(Keys(0, InputButtons.Jump), default);
            Assert.Less(P1(w).Velocity.Y, -5f, "아랫면에서 아래로 뛰어내림");
            // 근처 지형(기둥·ㄴ)의 중력장에 잡히거나 바닥에 떨어진다. 어느 쪽이든 아랫면을 떠난다.
            RunUntil(w, Keys(0), f => f.Grounded && f.Position.Y < 2f, 180);
        }

        [Test]
        public void SKey_LetsGoAndFallsDown()
        {
            var w = World(new Vec2(-9f, CeilY), SpiderMovement.Up);
            Assert.IsTrue(P1(w).Grounded, "천장에 매달림");
            Assert.AreEqual(SpiderMovement.Up, P1(w).GravityDir);
            Run(w, Keys(0, InputButtons.None, -1), 1);
            Assert.AreEqual(SpiderMovement.Down, P1(w).GravityDir);
            Run(w, Keys(0), 20);
            Assert.Less(P1(w).Position.Y, CeilY - 1f, "떨어짐");
        }

        // 7절 방향 규칙(새로 누름·기억) ---------------------------------------------------------

        [TestCase(SpiderMovement.Up, 2, 1f, 0f)]
        [TestCase(SpiderMovement.Up, 1, -1f, 0f)]
        [TestCase(SpiderMovement.Right, 2, 0f, 1f)]
        [TestCase(SpiderMovement.Right, 1, 0f, -1f)]
        [TestCase(SpiderMovement.Left, 2, 0f, 1f)]
        [TestCase(SpiderMovement.Left, 1, 0f, -1f)]
        public void NewPress_MapsToScreenDirection(sbyte g, int key, float dx, float dy)
        {
            var pos = g == SpiderMovement.Up ? new Vec2(-9f, CeilY) : g == SpiderMovement.Right ? new Vec2(WallX, 0f) : new Vec2(-WallX, 0f);
            var w = World(pos, g);
            var p0 = P1(w).Position;
            Run(w, Keys(key), 10);
            var d = P1(w).Position - p0;
            if (dx != 0f) Assert.AreEqual(dx, SimMath.Sign(d.X), $"{d}");
            if (dy != 0f) Assert.AreEqual(dy, SimMath.Sign(d.Y), $"{d}");
            Assert.AreEqual(g, P1(w).GravityDir);
        }

        [Test]
        public void ReleaseBoth_Forgets_BothPressed_Keeps_ReleaseOne_Redecides()
        {
            var w = World(new Vec2(WallX, -2f), SpiderMovement.Right);
            Run(w, Keys(2), 4);
            Assert.AreEqual(1, P1(w).SurfaceCirc);
            Run(w, Keys(3), 4);
            float y = P1(w).Position.Y;
            Run(w, Keys(3), 4);
            Assert.AreEqual(y, P1(w).Position.Y, 0.02f, "양쪽 동시: 정지");
            Assert.AreEqual(1, P1(w).SurfaceCirc, "기억 유지");
            Run(w, Keys(1), 1);
            Assert.AreEqual(-1, P1(w).SurfaceCirc, "남은 A로 재결정(오른벽 A = 아래)");
            Run(w, Keys(0), 1);
            Assert.AreEqual(0, P1(w).SurfaceCirc, "뗌: 기억 삭제");
        }

        // 행동과 표면 -------------------------------------------------------------------

        [Test]
        public void PunchOnWall_StaysOnWall()
        {
            var w = World(new Vec2(WallX, 0f), SpiderMovement.Right);
            var p = P1(w).Position;
            w.Step(new InputFrame(0, 0, InputButtons.Attack, AimDirections.Encode(32)), default);
            w.Step(new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(32)), default);
            for (int i = 0; i < 20; i++)
            {
                w.Step(default, default);
                Assert.AreEqual(SpiderMovement.Right, P1(w).GravityDir);
            }
            Assert.AreEqual(p.X, P1(w).Position.X, 0.02f);
            Assert.AreEqual(p.Y, P1(w).Position.Y, 0.05f);
        }

        [Test]
        public void HitOrLunge_UsesDownGravity_LungeIntoSurfaceSticks()
        {
            var config = Config();
            var hit = World(new Vec2(WallX, 0f), SpiderMovement.Right);
            var s = hit.State;
            s.Fighter0.StunTicks = 10;
            s.Fighter0.RestartAction(ActionState.Hitstun);
            hit.State = s;
            hit.Step(default, default);
            Assert.AreEqual(SpiderMovement.Down, P1(hit).GravityDir, "맞으면 붙지 않고 아래 중력");

            // T 윗면에서 위(천장)로 겨눈 최대 충전 박치기: 천장에 멈추고 붙는다.
            var w = World(new Vec2(-3f, BarTopY));
            int hold = config.Combat.Body.TapHoldTicks + 30;
            for (int i = 0; i <= hold; i++)
                w.Step(new InputFrame(0, 0, InputButtons.Charge, AimDirections.Encode(16)), default);
            w.Step(new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(16)), default);
            Assert.AreEqual(ActionState.Lunge, P1(w).Action);
            Assert.AreEqual(SpiderMovement.Down, P1(w).GravityDir);
            RunUntil(w, default, f => f.GravityDir == SpiderMovement.Up && f.Grounded, 40);
        }

        // 3.3 안전 검사와 봇 스모크 ------------------------------------------------------

        [Test]
        public void SpiderScenario_DeterministicSaveRestoreSyncTestReplay_BotSmoke()
        {
            var config = Config();
            SimWorld world = null;
            var bot0 = new BrawlBot(() => world, 0, 3u);
            var bot1 = new BrawlBot(() => world, 1, 4u);
            world = new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, 7, MatchPhase.Countdown));
            const int ticks = 1800;
            var p1 = new InputFrame[ticks];
            var p2 = new InputFrame[ticks];
            var rng = new SimRng(55);
            int sticks = 0, notDown = 0;
            for (int t = 0; t < ticks; t++)
            {
                int tick = world.State.Tick;
                p1[t] = bot0.Poll(tick);
                p2[t] = bot1.Poll(tick);
                // 봇은 지형을 찾지 않으므로 P1에 무작위 점프·좌우·S를 섞는다.
                if (rng.Range(0, 3) == 0)
                    p1[t] = new InputFrame(rng.Range(-1, 2), rng.Range(0, 10) == 0 ? -1 : 0, rng.Range(0, 4) == 0 ? InputButtons.Jump : p1[t].Buttons & ~InputButtons.Jump, p1[t].Aim);
                world.Step(p1[t], p2[t]);
                var s = world.State;
                for (int k = 0; k < 2; k++)
                {
                    var f = s.GetFighter(k);
                    Assert.IsFalse(float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y), $"NaN 틱 {t}");
                    Assert.LessOrEqual(SimMath.Abs(f.Position.X), WallX + 1e-3f, $"틱 {t}");
                    Assert.LessOrEqual(SimMath.Abs(f.Position.Y), CeilY + 1e-3f, $"틱 {t}");
                    Assert.IsFalse(TerrainCollision.OverlapsSolid(f.Position, config.HalfBody - 0.01f, Arena), $"P{k + 1} 지형 속 틱 {t} {f.Position}");
                    if (f.GravityDir != SpiderMovement.Down) notDown++;
                }
                for (int e = 0; e < world.Events.Count; e++)
                {
                    Assert.AreNotEqual(SimEventType.OutOfBounds, world.Events[e].Type, $"틱 {t}");
                    if (world.Events[e].Type == SimEventType.SurfaceAttach)
                        sticks++;
                }
            }
            TestContext.WriteLine($"[CR-004 E] 봇 30초: 붙기 {sticks}회, 아래가 아닌 중력 {notDown}틱");
            Assert.Greater(sticks + notDown, 0, "표면 이동이 일어나야 함");

            SimWorld Fresh() => new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, 7, MatchPhase.Countdown));
            var a = Fresh();
            var b = Fresh();
            for (int t = 0; t < ticks; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"틱 {t + 1}\n{StateDiff.Describe(a.State, b.State)}");
            }
            Assert.AreEqual(world.ComputeChecksum(), a.ComputeChecksum());

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
            var recording = InputRecording.Begin(config, Arena, 7, spawn0, spawn1, MatchPhase.Countdown, RuleSet.Brawl.Format());
            var rw = new SimWorld(config, Arena, SimState.CreateRound(config, Arena, 7, spawn0, spawn1, MatchPhase.Countdown));
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
