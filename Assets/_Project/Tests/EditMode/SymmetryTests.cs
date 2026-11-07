using System;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP2-11: P1과 P2를 바꾸고 x를 뒤집은 경기의 결과가 뒤집어서 일치한다(오차 1e-4). CP4부터 전투 필드도 비교한다.
    /// CP2-12: 이동이 들어간 상태로 세 맵에서 결정론·SyncTest.
    /// </summary>
    public class SymmetryTests
    {
        const float Tolerance = 1e-4f;

        internal static InputFrame Mirror(InputFrame f) => new InputFrame(-f.MoveX, f.MoveY, f.Buttons);

        internal static void AssertMirrored(in FighterState a, in FighterState b, int tick, string who)
        {
            string at = $"틱 {tick} {who}";
            Assert.AreEqual(a.Position.X, -b.Position.X, Tolerance, at + " pos.x");
            Assert.AreEqual(a.Position.Y, b.Position.Y, Tolerance, at + " pos.y");
            Assert.AreEqual(a.Velocity.X, -b.Velocity.X, Tolerance, at + " vel.x");
            Assert.AreEqual(a.Velocity.Y, b.Velocity.Y, Tolerance, at + " vel.y");
            Assert.AreEqual(a.Facing, -b.Facing, at + " facing");
            Assert.AreEqual(a.Action, b.Action, at + " action");
            Assert.AreEqual(a.Grounded, b.Grounded, at + " grounded");
            Assert.AreEqual(a.WallSide, -b.WallSide, at + " wallSide");
            Assert.AreEqual(a.AirJumpsLeft, b.AirJumpsLeft, at + " airJumps");
            Assert.AreEqual(a.DashCharges, b.DashCharges, at + " dashCharges");
            Assert.AreEqual(a.ActionTick, b.ActionTick, at + " actionTick");
            Assert.AreEqual(a.Health, b.Health, at + " hp");
            Assert.AreEqual(a.HitstopTicks, b.HitstopTicks, at + " hitstop");
            Assert.AreEqual(a.StunTicks, b.StunTicks, at + " stun");
            Assert.AreEqual(a.ComboHits, b.ComboHits, at + " combo");
            Assert.AreEqual(a.AttackId, b.AttackId, at + " attack");
            Assert.AreEqual(a.AttackHasHit, b.AttackHasHit, at + " attackHasHit");
            Assert.AreEqual(a.ParryTicks, b.ParryTicks, at + " parry");
            Assert.AreEqual(a.LungeBounces, b.LungeBounces, at + " lungeBounces");
            Assert.AreEqual(a.ContactCooldown, b.ContactCooldown, at + " contactCooldown");
            Assert.AreEqual(a.ThrowPending, b.ThrowPending, at + " throwPending");
            Assert.AreEqual(a.ThrowDirection.X, -b.ThrowDirection.X, Tolerance, at + " throwDirection.x");
            Assert.AreEqual(a.ThrowDirection.Y, b.ThrowDirection.Y, Tolerance, at + " throwDirection.y");
            Assert.AreEqual(a.SpikeImmunityTicks, b.SpikeImmunityTicks, at + " spikeImmunity");
        }

        [TestCase(11u)]
        [TestCase(12u)]
        [TestCase(13u)]
        public void MirroredMatch_ProducesMirroredResult(uint seed)
        {
            const int ticks = 1800;
            TestWorlds.RandomInputs(seed, ticks, out var p1, out var p2);
            var normal = TestWorlds.Create();
            var mirrored = TestWorlds.Create();
            for (int t = 0; t < ticks; t++)
            {
                normal.Step(p1[t], p2[t]);
                mirrored.Step(Mirror(p2[t]), Mirror(p1[t]));
                AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
        }

        [Test]
        public void HeadOnCollision_IsSymmetric()
        {
            // 서로를 향해 달려 부딪히고, 한쪽이 다른 쪽 머리 위로 뛰어오르는 장면.
            var w = TestWorlds.CreateAt(new Vec2(-3f, -7.55f), new Vec2(3f, -7.55f), null, TestWorlds.Grid("Flat", FlatGrid));
            for (int t = 0; t < 60; t++)
            {
                w.Step(new InputFrame(1, 0, InputButtons.None), new InputFrame(-1, 0, InputButtons.None));
                AssertMirrored(w.State.Fighter0, w.State.Fighter1, t + 1, "정면");
            }
            Assert.AreEqual(w.Config.Movement.BodySize, w.State.Fighter1.Position.X - w.State.Fighter0.Position.X, 1e-3f, "밀어내기로 딱 붙어 있어야 함");
        }

        [Test]
        public void StandOnOpponentsHead()
        {
            var w = TestWorlds.CreateAt(new Vec2(0f, -3f), new Vec2(0f, -7.55f), null, TestWorlds.Grid("Flat", FlatGrid));
            for (int t = 0; t < 60; t++)
                w.Step(default, default);
            Assert.IsTrue(w.State.Fighter0.Grounded, "상대 머리 위에 서야 함");
            Assert.AreEqual(FighterState.OnFighter, w.State.Fighter0.GroundBox);
            Assert.AreEqual(-7.55f + w.Config.Movement.BodySize, w.State.Fighter0.Position.Y, Tolerance);
            w.Step(new InputFrame(0, 0, InputButtons.Jump), default);
            Assert.Greater(w.State.Fighter0.Velocity.Y, 0f, "머리 위에서 점프");
        }

        const string FlatGrid = @"
            ################################
            #..1........................2..#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            ################################";

        [TestCase("Box")]
        [TestCase("Towers")]
        [TestCase("Pit")]
        public void EachMap_DeterministicAndSyncTestClean(string map)
        {
            var arena = ArenaFiles.LoadByName(map);
            TestWorlds.RandomInputs(900, 1800, out var p1, out var p2);
            var a = TestWorlds.Create(MatchPhase.Fighting, 5, null, arena);
            var b = TestWorlds.Create(MatchPhase.Fighting, 5, null, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 5, null, arena);
            var sync = new SyncTest<SimWorld>(synced, 7);
            int outOfBounds = 0;
            for (int t = 0; t < p1.Length; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                sync.Advance(p1[t], p2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"{map} 틱 {t + 1}");
                for (int i = 0; i < a.Events.Count; i++)
                    outOfBounds += a.Events[i].Type == SimEventType.OutOfBounds ? 1 : 0;
            }
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(a.ComputeChecksum(), synced.ComputeChecksum());
            Assert.AreEqual(0, outOfBounds, "경기장 밖으로 나감");
        }
    }
}
