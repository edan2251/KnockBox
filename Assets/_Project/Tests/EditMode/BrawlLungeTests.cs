using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 단계 A 피드백(S-161): 실험 경기의 돌진은 빠르게 나가 감속하며 짧게 끝나고, 지형에 부딪히면 튕겨 나오지 않고 작은 반동으로 멈춘다.
    /// 이전 규칙(LungeDrag·LungeStopRecoil 0)은 바뀌지 않는다(기존 ChargeModeTests가 회귀 검사).
    /// </summary>
    public class BrawlLungeTests
    {
        static SimConfig Config() =>
            new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl);

        /// <summary>바닥 위 x에서 왼쪽으로 T + chargeTicks만큼 누른 뒤 떼서 돌진하고, 돌진이 끝날 때까지 진행한다.</summary>
        static SimWorld LungeLeft(SimConfig config, ArenaDef arena, float x, int chargeTicks, out float launchX, out int lungeTicks)
        {
            float floorY = arena.SpawnFoot0.Y + config.HalfBody;
            var w = TestWorlds.CreateAt(new Vec2(x, floorY), new Vec2(14f, floorY), config, arena);
            // 몸 폼(단계 B): 충전은 탭/홀드 임계 T 뒤부터 센다.
            int hold = config.Combat.Body.TapHoldTicks + chargeTicks;
            for (int t = 0; t <= hold; t++)
                w.Step(new InputFrame(-1, 0, InputButtons.Charge), default);
            launchX = w.State.Fighter0.Position.X;
            lungeTicks = 0;
            w.Step(new InputFrame(-1, 0, InputButtons.None), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "뗀 틱에 발사");
            while (w.State.Fighter0.Action == ActionState.Lunge && lungeTicks < 120)
            {
                w.Step(default, default);
                lungeTicks++;
            }
            return w;
        }

        [TestCase(2, 1.5f, 4f)]
        [TestCase(30, 4f, 7.5f)]
        public void Lunge_IsShortBurst(int chargeTicks, float minDistance, float maxDistance)
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            var w = LungeLeft(config, arena, 2f, chargeTicks, out float launchX, out int lungeTicks);
            float distance = launchX - w.State.Fighter0.Position.X;
            TestContext.WriteLine($"[CR-004 A] 충전 {chargeTicks}틱: 돌진 {lungeTicks}틱, 거리 {distance:0.00}");
            Assert.LessOrEqual(lungeTicks, config.Combat.Charge.LungeMaxTicks + 1);
            Assert.That(distance, Is.InRange(minDistance, maxDistance));
        }

        [Test]
        public void LungeIntoWall_StopsWithSmallRecoil()
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            float half = config.HalfBody;
            float wallFace = -(arena.HalfWidth - 1f) + half;
            var w = LungeLeft(config, arena, wallFace + 4f, 30, out _, out _);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action);
            float farthest = w.State.Fighter0.Position.X;
            for (int t = 0; t < 30; t++)
            {
                w.Step(default, default);
                farthest = System.Math.Max(farthest, w.State.Fighter0.Position.X);
            }
            float recoil = farthest - wallFace;
            TestContext.WriteLine($"[CR-004 A] 벽에서 밀려난 거리 {recoil:0.00}");
            Assert.That(recoil, Is.InRange(0f, 0.6f), "튕겨 나오지 않고 작은 반동만");
        }
    }
}
