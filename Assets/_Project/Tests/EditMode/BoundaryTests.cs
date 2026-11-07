using System.Text;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP13-2 경계 튕김(03-combat.md 3절, CR-002): 네 변의 피해·반사, 면역 45틱, 최대 속도에서도 이탈 없음, 대칭성, SyncTest.</summary>
    public class BoundaryTests
    {
        static SimConfig Config() =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, CombatMode.Charge, true);

        /// <summary>테두리만 있는 36 × 20 경계 튕김 맵(가운데 아래에 발판 하나).</summary>
        public static ArenaDef Open()
        {
            var sb = new StringBuilder("name: Open\nboundary: true\n---\n");
            for (int r = 0; r < 20; r++)
            {
                for (int c = 0; c < 36; c++)
                {
                    bool border = r == 0 || r == 19 || c == 0 || c == 35;
                    char ch = border ? '#' : r == 16 && c >= 12 && c <= 23 ? '#' : r == 15 && c == 14 ? '1' : r == 15 && c == 21 ? '2' : '.';
                    sb.Append(ch);
                }
                sb.Append('\n');
            }
            return ArenaParser.Parse(sb.ToString());
        }

        static int CountEvents(SimWorld w, SimEventType type, int fighter, out SimEvent last)
        {
            int n = 0;
            last = default;
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                {
                    n++;
                    last = w.Events[i];
                }
            }
            return n;
        }

        [Test]
        public void BorderCells_AreNotTerrain()
        {
            var arena = Open();
            Assert.IsTrue(arena.HasBoundary);
            Assert.AreEqual(17f, arena.BoundaryHalfWidth);
            Assert.AreEqual(9f, arena.BoundaryHalfHeight);
            Assert.AreEqual(1, arena.BoxCount, "테두리 칸은 지형이 아니고 가운데 발판만 남음");
        }

        // CP13 조정(S-118): 기본 3 + 법선 속도 × 0.25, 3~15. (CR-002 초기값 4 + × 0.35, 4~15)
        [TestCase(0f, 3)]
        [TestCase(10f, 6)]
        [TestCase(20f, 8)]
        [TestCase(30f, 11)]
        [TestCase(60f, 15)]
        public void Damage_FollowsFormula(float speed, int expected)
        {
            Assert.AreEqual(expected, FighterMovement.BoundaryDamage(speed, CombatParams.Default));
        }

        [TestCase(5f, false, 14f)]
        [TestCase(20f, false, 22f)]
        [TestCase(40f, false, 44f)]
        [TestCase(20f, true, 10f)]
        public void Rebound_FollowsFormula(float speed, bool immune, float expected)
        {
            Assert.AreEqual(expected, FighterMovement.BoundaryReboundSpeed(speed, immune, CombatParams.Default), 1e-4f);
        }

        /// <summary>네 변 각각: 속도 v로 경계에 닿으면 피해·반사·경직·히트스탑·면역이 공식대로.</summary>
        [Test]
        public void EveryEdge_DamagesReflectsStunsAndStaysInside([Values(1, -1)] int sign, [Values(true, false)] bool horizontal, [Values(12f, 24f, 40f)] float speed)
        {
            var config = Config();
            var arena = Open();
            float half = config.HalfBody;
            var normal = horizontal ? new Vec2(sign, 0f) : new Vec2(0f, sign);
            var start = horizontal ? new Vec2(sign * (arena.BoundaryHalfWidth - half - 0.05f), 2f) : new Vec2(-6f, sign * (arena.BoundaryHalfHeight - half - 0.05f));
            var world = TestWorlds.CreateAt(start, new Vec2(6f, 0f), config, arena);
            var s = world.State;
            s.Fighter0.Velocity = normal * speed;
            s.Fighter0.Grounded = false;
            s.Fighter0.SetAction(ActionState.Airborne);
            world.State = s;

            world.Step(default, default);
            // 아래로 떨어질 때는 최대 낙하 속도(30)로 제한된다.
            if (!horizontal && sign < 0)
                speed = System.Math.Min(speed, config.Movement.MaxFallSpeed);
            Assert.AreEqual(1, CountEvents(world, SimEventType.BoundaryHit, 0, out var e), "경계에 한 번 닿음");
            var f = world.State.Fighter0;
            // 한 틱 동안 중력·공중 감속으로 법선 속도가 최대 약 1.8 바뀐다.
            int low = FighterMovement.BoundaryDamage(speed - 2f, config.Combat), high = FighterMovement.BoundaryDamage(speed + 2f, config.Combat);
            Assert.That((int)e.Strength, Is.InRange(low, high), "피해");
            Assert.AreEqual(config.Combat.MaxHealth - (int)e.Strength, f.Health);
            float inward = -(horizontal ? f.Velocity.X : f.Velocity.Y) * sign;
            Assert.GreaterOrEqual(inward, 14f - 1e-4f, "안쪽으로 최소 14");
            Assert.AreEqual(FighterMovement.BoundaryReboundSpeed(speed, false, config.Combat), inward, 2.5f, "반사 × 1.1");
            Assert.AreEqual(ActionState.Hitstun, f.Action);
            Assert.AreEqual(config.Combat.BoundaryHitstun, f.StunTicks);
            Assert.AreEqual(config.Combat.BoundaryHitstop, f.HitstopTicks);
            Assert.AreEqual(config.Combat.BoundaryImmunityTicks, f.BoundaryImmunityTicks);
            Assert.AreEqual(-normal.X, e.Direction.X, 1e-5f);
            Assert.AreEqual(-normal.Y, e.Direction.Y, 1e-5f);
            Assert.LessOrEqual(System.Math.Abs(f.Position.X), arena.BoundaryHalfWidth - half + 1e-4f);
            Assert.LessOrEqual(System.Math.Abs(f.Position.Y), arena.BoundaryHalfHeight - half + 1e-4f);
        }

        /// <summary>면역 45틱: 그 안에 다시 닿으면 피해 없이 반사 × 0.5, 면역이 끝나면 다시 피해.</summary>
        [Test]
        public void Immunity_45Ticks_ThenDamageAgain()
        {
            var config = Config();
            var arena = Open();
            float half = config.HalfBody;
            float edge = arena.BoundaryHalfWidth - half;
            var world = TestWorlds.CreateAt(new Vec2(edge - 0.05f, 2f), new Vec2(-6f, 0f), config, arena);
            void PushIntoEdge(float speed)
            {
                var s = world.State;
                s.Fighter0.Position = new Vec2(edge - 0.05f, 2f);
                s.Fighter0.Velocity = new Vec2(speed, 0f);
                s.Fighter0.HitstopTicks = 0;
                world.State = s;
            }

            PushIntoEdge(20f);
            world.Step(default, default);
            CountEvents(world, SimEventType.BoundaryHit, 0, out var first);
            Assert.Greater(first.Strength, 0f);
            int health = world.State.Fighter0.Health;

            for (int t = 0; t < 20; t++)
                world.Step(default, default);
            Assert.Greater(world.State.Fighter0.BoundaryImmunityTicks, 0);
            PushIntoEdge(20f);
            world.Step(default, default);
            Assert.AreEqual(1, CountEvents(world, SimEventType.BoundaryHit, 0, out var immune));
            Assert.AreEqual(0f, immune.Strength, "면역 중 피해 없음");
            Assert.AreEqual(health, world.State.Fighter0.Health);
            Assert.AreEqual(-10f, world.State.Fighter0.Velocity.X, 1f, "면역 중 반사 × 0.5");

            // 면역이 끝날 때까지 가운데 발판 위에 세워 둔다(아래 경계로 떨어져 다시 맞지 않게).
            int guard = 0;
            while (world.State.Fighter0.BoundaryImmunityTicks > 0 && guard++ < 60)
            {
                var rest = world.State;
                rest.Fighter0.Position = new Vec2(0f, -6f + half);
                rest.Fighter0.Velocity = Vec2.Zero;
                world.State = rest;
                world.Step(default, default);
            }
            Assert.AreEqual(0, world.State.Fighter0.BoundaryImmunityTicks);
            PushIntoEdge(20f);
            world.Step(default, default);
            CountEvents(world, SimEventType.BoundaryHit, 0, out var again);
            Assert.Greater(again.Strength, 0f, "면역이 끝나면 다시 피해");
        }

        /// <summary>대시·돌진·던지기·넉백 속도(최대 80)로 아무 방향으로 날려도 경계 밖으로 나가지 않는다.</summary>
        [Test]
        public void MaxSpeeds_NeverLeaveBoundary()
        {
            var config = Config();
            var arena = Open();
            float half = config.HalfBody;
            var rng = new SimRng(77);
            int hits = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                var p = new Vec2(rng.Range(-160, 161) / 10f, rng.Range(-80, 81) / 10f);
                var world = TestWorlds.CreateAt(p, new Vec2(-p.X, -p.Y), config, arena);
                var s = world.State;
                float speed = 20f + rng.Range(0, 61);
                var dir = SimMath.Normalize(new Vec2(rng.Range(-100, 101), rng.Range(-100, 101)) + new Vec2(0.01f, 0f));
                s.Fighter0.Velocity = dir * speed;
                s.Fighter1.Velocity = dir * -speed;
                s.Fighter0.StunTicks = s.Fighter1.StunTicks = 30;
                s.Fighter0.SetAction(ActionState.Hitstun);
                s.Fighter1.SetAction(ActionState.Hitstun);
                world.State = s;
                TestWorlds.RandomInputs((uint)trial + 1, 90, out var in1, out var in2);
                for (int t = 0; t < 90; t++)
                {
                    world.Step(in1[t], in2[t]);
                    for (int i = 0; i < world.Events.Count; i++)
                    {
                        Assert.AreNotEqual(SimEventType.OutOfBounds, world.Events[i].Type);
                        if (world.Events[i].Type == SimEventType.BoundaryHit)
                            hits++;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        var f = world.State.GetFighter(k);
                        Assert.LessOrEqual(System.Math.Abs(f.Position.X), arena.BoundaryHalfWidth - half + 1e-4f, $"시도 {trial} 틱 {t} P{k + 1} {f.Position}");
                        Assert.LessOrEqual(System.Math.Abs(f.Position.Y), arena.BoundaryHalfHeight - half + 1e-4f, $"시도 {trial} 틱 {t} P{k + 1} {f.Position}");
                    }
                }
            }
            TestContext.WriteLine($"[Knockbox][CP13-2] 300회 × 90틱, 경계 튕김 {hits}회, 이탈 0");
            Assert.Greater(hits, 300);
        }

        /// <summary>왼쪽·오른쪽 경계에서 거울처럼 같은 결과.</summary>
        [Test]
        public void Mirrored_LeftAndRightEdges_GiveMirroredResults()
        {
            var config = Config();
            var arena = Open();
            var a = TestWorlds.CreateAt(new Vec2(14f, 3f), new Vec2(-14f, 3f), config, arena);
            var s = a.State;
            s.Fighter0.Velocity = new Vec2(33f, 7f);
            s.Fighter1.Velocity = new Vec2(-33f, 7f);
            s.Fighter0.SetAction(ActionState.Airborne);
            s.Fighter1.SetAction(ActionState.Airborne);
            s.Fighter0.Grounded = s.Fighter1.Grounded = false;
            a.State = s;
            for (int t = 0; t < 120; t++)
            {
                a.Step(default, default);
                var f0 = a.State.Fighter0;
                var f1 = a.State.Fighter1;
                Assert.AreEqual(f0.Position.X, -f1.Position.X, 1e-4f, $"틱 {t}");
                Assert.AreEqual(f0.Position.Y, f1.Position.Y, 1e-4f, $"틱 {t}");
                Assert.AreEqual(f0.Health, f1.Health, $"틱 {t}");
            }
        }

        /// <summary>바닥이 거의 없는 경계 맵에서 무작위 입력 3600틱, 7틱 되감기 SyncTest 불일치 0.</summary>
        [Test]
        public void RandomFight_OnBoundaryArena_SyncTestPasses()
        {
            var config = Config();
            var arena = Open();
            var world = new SimWorld(config, arena, SimState.CreateRound(config, arena, 5, MatchPhase.Fighting));
            var sync = new SyncTest<SimWorld>(world);
            TestWorlds.RandomInputs(9, 3600, out var p1, out var p2);
            int hits = 0;
            for (int t = 0; t < 3600; t++)
            {
                if (!sync.Advance(p1[t], p2[t]))
                    Assert.Fail(sync.LastMismatch.ToString());
                for (int i = 0; i < world.Events.Count; i++)
                {
                    if (world.Events[i].Type == SimEventType.BoundaryHit)
                        hits++;
                }
            }
            TestContext.WriteLine($"[Knockbox][CP13-2] SyncTest 3600틱, 경계 튕김 {hits}회");
            Assert.Greater(hits, 0);
        }
    }
}
