using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 D: 붙기만 하는 표면 이동(7절, 비교용). E(거미식 자유 표면 이동)는 BrawlSpiderTests.
    /// Brawl 맵: 외곽 안쪽 면 x ±17, 바닥 y −9, 천장 y 9. T 가로 막대 x[−5, 5] y[3, 4], 기둥 x[−1, 1] y[−1, 3].
    /// 이 테스트는 이전 몸 크기(0.9)로 돈다.
    /// </summary>
    public class BrawlSurfaceTests
    {
        const int D = 1;
        const float Half = 0.45f;
        const float WallX = 17f - Half;
        const float CeilY = 9f - Half;
        const float FloorY = -9f + Half;

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimConfig Config(int mode) =>
            new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default, mode), RuleSet.Brawl);

        /// <summary>P1을 pos에 속도 vel로 두고, P2는 길을 막지 않게 가운데 블록 위에 세운다.</summary>
        static SimWorld World(int mode, Vec2 pos, Vec2 vel = default, Vec2? p2 = null)
        {
            var config = Config(mode);
            var w = TestWorlds.CreateAt(pos, p2 ?? new Vec2(1.5f, 4f + Half), config, Arena);
            var s = w.State;
            s.Fighter0.Velocity = vel;
            s.Fighter0.Grounded = false;
            s.Fighter0.GroundBox = FighterState.NoBox;
            s.Fighter0.RestartAction(ActionState.Airborne);
            w.State = s;
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

        static void RunUntil(SimWorld w, InputFrame input, System.Func<FighterState, bool> done, int limit = 90)
        {
            for (int i = 0; i < limit && !done(w.State.Fighter0); i++)
                w.Step(input, default);
            Assert.IsTrue(done(w.State.Fighter0), $"조건에 닿지 못함: {w.State.Fighter0.Position} {w.State.Fighter0.Action} 표면 {w.State.Fighter0.Surface}");
        }

        static FighterState P1(SimWorld w) => w.State.Fighter0;

        /// <summary>외곽 오른벽(높이 y)에 붙은 월드. 키를 뗀 상태로 끝난다.</summary>
        static SimWorld OnRightWall(int mode, float y = 0f)
        {
            var w = World(mode, new Vec2(WallX - 0.6f, y), new Vec2(12f, 0f));
            RunUntil(w, Keys(2), f => f.Surface == FighterState.SurfaceWallRight);
            Run(w, Keys(0), 2);
            return w;
        }

        static SimWorld OnLeftWall(int mode, float y = 0f)
        {
            var w = World(mode, new Vec2(-WallX + 0.6f, y), new Vec2(-12f, 0f));
            RunUntil(w, Keys(1), f => f.Surface == FighterState.SurfaceWallLeft);
            Run(w, Keys(0), 2);
            return w;
        }

        static SimWorld OnCeiling(int mode, float x = -9f)
        {
            var w = World(mode, new Vec2(x, CeilY - 0.6f), new Vec2(0f, 12f));
            RunUntil(w, Keys(0), f => f.Surface == FighterState.SurfaceCeiling);
            Run(w, Keys(0), 2);
            return w;
        }

        // 붙기 조건 -------------------------------------------------------------------

        [Test]
        public void Attach_OuterWallsAndCeiling_InnerSidesAndBottom_NotInnerTop()
        {
            Assert.IsTrue(P1(OnRightWall(D)).SurfaceOuter, "외곽 오른벽");
            Assert.IsTrue(P1(OnLeftWall(D)).SurfaceOuter, "외곽 왼벽");
            Assert.IsTrue(P1(OnCeiling(D)).SurfaceOuter, "외곽 천장");

            var side = World(D, new Vec2(1f + Half + 0.5f, 1.5f), new Vec2(-12f, 0f));
            RunUntil(side, Keys(1), f => f.Surface == FighterState.SurfaceWallLeft);
            Assert.IsFalse(P1(side).SurfaceOuter, "내부 지형 옆면");

            var bottom = World(D, new Vec2(-3f, 3f - Half - 0.5f), new Vec2(0f, 12f));
            RunUntil(bottom, Keys(0), f => f.Surface == FighterState.SurfaceCeiling);
            Assert.IsFalse(P1(bottom).SurfaceOuter, "내부 지형 아랫면");

            var top = World(D, new Vec2(-3f, 4f + Half + 0.8f), new Vec2(0f, -2f));
            RunUntil(top, Keys(0), f => f.Grounded);
            Assert.AreEqual(FighterState.SurfaceNone, P1(top).Surface, "윗면은 기존 서기");
        }

        [Test]
        public void Attach_GravityOff_StaysPut()
        {
            var w = OnRightWall(D, 2f);
            float y = P1(w).Position.Y;
            Run(w, Keys(0), 60);
            Assert.AreEqual(FighterState.SurfaceWallRight, P1(w).Surface);
            Assert.AreEqual(y, P1(w).Position.Y, 1e-4f, "붙은 동안 중력 없음");
        }

        [Test]
        public void HitCollision_DoesNotAttach()
        {
            var w = World(D, new Vec2(WallX - 1f, 0f), new Vec2(30f, 0f));
            var s = w.State;
            s.Fighter0.StunTicks = 20;
            s.Fighter0.RestartAction(ActionState.Hitstun);
            w.State = s;
            Run(w, Keys(2), 6);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface, "피격 충돌은 붙지 않고 벽꽝");
        }

        [Test]
        public void Standing_PushingWall_InD_DoesNotAttach()
        {
            var w = World(D, new Vec2(WallX - 1f, FloorY));
            Run(w, Keys(0), 2);
            Run(w, Keys(2), 30);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface);
            Assert.IsTrue(P1(w).Grounded);
        }



        // 모서리 ---------------------------------------------------------------------

        // 기억 -----------------------------------------------------------------------

        [Test]
        public void SKey_DropsOff()
        {
            var w = OnCeiling(D);
            Run(w, Keys(0, InputButtons.None, -1), 2);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface);
            Assert.Less(P1(w).Position.Y, CeilY - 0.01f, "떨어짐");

            var wall = OnRightWall(D);
            Run(wall, Keys(2, InputButtons.None, -1), 1);
            Run(wall, Keys(2), 4);
            Assert.AreEqual(FighterState.SurfaceNone, P1(wall).Surface, "S로 놓으면 벽 쪽 입력이 잠깐 무시되어 바로 다시 붙지 않음");
        }

        [Test]
        public void HitWhileAttached_DetachesAndKeysCountAsNewAfter()
        {
            var w = OnRightWall(D);
            var s = w.State;
            s.Fighter0.StunTicks = 10;
            s.Fighter0.Velocity = new Vec2(-8f, 0f);
            s.Fighter0.RestartAction(ActionState.Hitstun);
            w.State = s;
            w.Step(Keys(2), default);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface);
            Assert.AreEqual(0, P1(w).SurfaceCirc);
        }

        // 박치기와 표면 -----------------------------------------------------------------

        [Test]
        public void LungeFromSurface_Detaches_AndAimedIntoSurfaceReattaches()
        {
            var config = Config(D);
            int hold = config.Combat.Body.TapHoldTicks + 4;
            // 천장에서 아래로 박치기: 떨어지고 기억 삭제.
            var w = OnCeiling(D);
            for (int i = 0; i <= hold; i++)
                w.Step(new InputFrame(0, 0, InputButtons.Charge, AimDirections.Encode(48)), default);
            w.Step(new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(48)), default);
            Assert.AreEqual(ActionState.Lunge, P1(w).Action);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface);

            // 오른벽에서 벽 안쪽(오른쪽)으로 겨눈 박치기: 조준 그대로 발사 → 벽에 닿아 멈춤 → 다시 붙음.
            var into = OnRightWall(D, 2f);
            for (int i = 0; i <= hold; i++)
                into.Step(new InputFrame(0, 0, InputButtons.Charge, AimDirections.Encode(0)), default);
            into.Step(new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(0)), default);
            RunUntil(into, default, f => f.Surface == FighterState.SurfaceWallRight && f.Action != ActionState.Lunge, 20);
        }

        // 잡은 쪽 / 잡힌 쪽 ---------------------------------------------------------------

        [Test]
        public void Holder_StaysAttached_HeldDetaches()
        {
            var config = Config(D);
            // P1은 바닥 바로 위 오른벽, P2는 그 왼쪽 바닥에 붙어 선다.
            var w = World(D, new Vec2(WallX - 0.6f, FloorY + 0.4f), new Vec2(12f, 0f), new Vec2(WallX - 1.4f, FloorY));
            RunUntil(w, Keys(2), f => f.Surface == FighterState.SurfaceWallRight, 10);
            w.Step(new InputFrame(0, 0, InputButtons.Grab, AimDirections.Encode(32)), default);
            RunUntil(w, new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(32)), f => f.Action == ActionState.Holding, 20);
            Run(w, new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(32)), 3);
            Assert.AreEqual(FighterState.SurfaceWallRight, P1(w).Surface, "잡은 쪽은 붙은 채 멈춤");
            Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action);
            Assert.AreEqual(FighterState.SurfaceNone, w.State.Fighter1.Surface, "잡힌 쪽은 붙지 않음");
        }

        // 내부 지형과 D 모드 ------------------------------------------------------------

        [Test]
        public void InnerSurface_IgnoresKeys_InBothModes()
        {
            foreach (int mode in new[] { D })
            {
                var w = World(mode, new Vec2(1f + Half + 0.5f, 1.5f), new Vec2(-12f, 0f));
                RunUntil(w, Keys(1), f => f.Surface == FighterState.SurfaceWallLeft);
                var p = P1(w).Position;
                Run(w, Keys(2), 10);
                Run(w, Keys(1), 30);
                Assert.AreEqual(FighterState.SurfaceWallLeft, P1(w).Surface, $"모드 {mode}");
                Assert.AreEqual(p.Y, P1(w).Position.Y, 1e-4f, $"모드 {mode}: 내부 지형에서는 달리지 않음");
            }
        }

        [Test]
        public void ModeD_OuterSurface_IgnoresKeys()
        {
            var w = OnRightWall(D);
            float y = P1(w).Position.Y;
            Run(w, Keys(2), 20);
            Run(w, Keys(1), 20);
            Assert.AreEqual(y, P1(w).Position.Y, 1e-4f);
        }

        [Test]
        public void ModeOff_NoAttach()
        {
            var w = World(0, new Vec2(WallX - 0.6f, 0f), new Vec2(12f, 0f));
            Run(w, Keys(2), 20);
            Assert.AreEqual(FighterState.SurfaceNone, P1(w).Surface);
        }

        // 3.3 안전 검사와 봇 스모크 ------------------------------------------------------
    }
}
