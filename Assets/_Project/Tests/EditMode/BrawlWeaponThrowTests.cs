using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class BrawlWeaponThrowTests
    {
        static SimConfig Config(float speed = 0f, bool revisedBalance = false)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: true,
                combinedAttackInput: true, throwEnabled: true, revisedBalance: revisedBalance);
            combat.Weapons.SpawnDelayTicks = 3;
            combat.Weapons.WarningTicks = 2;
            combat.Weapons.FallTicks = 3;
            if (speed > 0f) combat.Weapons.ThrowSpeed = speed;
            var match = BrawlParams.Match(MatchParams.Default);
            match.CountdownTicks = 3;
            return new SimConfig(MovementParams.Default, match, combat, RuleSet.Brawl);
        }

        static ArenaDef Arena(int wallSide = 0, bool ceiling = false)
        {
            var boxes = new List<ArenaBox>
            {
                new ArenaBox(TerrainKind.Solid, -16f, -1f, 16f, 0f),
                new ArenaBox(TerrainKind.Solid, -16f, 0f, -15f, 9f),
                new ArenaBox(TerrainKind.Solid, 15f, 0f, 16f, 9f),
            };
            if (wallSide > 0) boxes.Add(new ArenaBox(TerrainKind.Solid, 1f, 0f, 1.03f, 6f));
            if (wallSide < 0) boxes.Add(new ArenaBox(TerrainKind.Solid, -1.03f, 0f, -1f, 6f));
            if (ceiling) boxes.Add(new ArenaBox(TerrainKind.Solid, -15f, 3f, 15f, 3.03f));
            return new ArenaDef("Throw test", boxes.ToArray(), new Vec2(-0.6f, 0f), new Vec2(1.5f, 0f));
        }

        static SimWorld Fresh(SimConfig config = null, ArenaDef arena = null, float x0 = -0.6f, float x1 = 1.5f)
        {
            config ??= Config();
            arena ??= Arena();
            return new SimWorld(config, arena, SimState.CreateRound(config, arena, 1u,
                new Vec2(x0, config.HalfBody), new Vec2(x1, config.HalfBody), MatchPhase.Fighting));
        }

        static SimWorld Held(int slot = 0, BrawlWeaponKind kind = BrawlWeaponKind.Pistol,
            SimConfig config = null, ArenaDef arena = null, bool closeTarget = false)
        {
            var w = Fresh(config, arena, slot == 0 ? 0f : closeTarget ? -1.5f : -8f,
                slot == 1 ? 0f : closeTarget ? 1.5f : 8f);
            w.State.Weapon.Kind = kind;
            w.State.Weapon.Phase = BrawlWeaponPhase.Held;
            w.State.Weapon.Owner = slot;
            w.State.Weapon.Position = w.State.GetFighter(slot).Position;
            w.State.Weapon.Ammo = kind == BrawlWeaponKind.Pistol ? 2 : 0;
            return w;
        }

        static InputFrame Input(InputButtons buttons, int aim = 0) => new InputFrame(0, 0, buttons, AimDirections.Encode(aim));
        static void Step(SimWorld w, int slot, InputFrame input) => w.Step(slot == 0 ? input : default, slot == 1 ? input : default);
        static int Events(SimWorld w, SimEventType type, int fighter = -2)
        {
            int count = 0;
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Type == type && (fighter == -2 || w.Events[i].Fighter == fighter)) count++;
            return count;
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Grab_ThrowsAlongCursor_AndKeepsAmmoWithoutAnAttack(int slot)
        {
            var w = Held(slot);
            int aim = slot == 0 ? 8 : 24;
            Step(w, slot, Input(InputButtons.Grab | InputButtons.Attack, aim));
            Assert.AreEqual(BrawlWeaponPhase.Thrown, w.State.Weapon.Phase);
            Assert.AreEqual(-1, w.State.Weapon.Owner);
            Assert.AreEqual(slot, w.State.Weapon.Thrower);
            Assert.AreEqual(2, w.State.Weapon.Ammo);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponThrow));
            Assert.AreEqual(0, Events(w, SimEventType.AttackStart));
            Assert.AreEqual(0, Events(w, SimEventType.BrawlWeaponShot));
            Assert.AreNotEqual(ActionState.Grab, w.State.GetFighter(slot).Action);
            AimDirections.TryGet(AimDirections.Encode(aim), out var direction);
            Assert.AreEqual(direction.X * 16f, w.State.Weapon.Velocity.X, 1e-5f);
            Assert.AreEqual(direction.Y * 16f - 30f * SimConfig.Dt, w.State.Weapon.Velocity.Y, 1e-5f);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void GrabDuringLmbCharge_ThrowsAndCancelsWithoutDelayedUse(int slot)
        {
            var w = Held(slot);
            int aim = slot == 0 ? 0 : 32;
            for (int i = 0; i < 10; i++) Step(w, slot, Input(InputButtons.Attack, aim));
            Assert.AreEqual(ActionState.Charge, w.State.GetFighter(slot).Action);
            Step(w, slot, Input(InputButtons.Grab | InputButtons.Attack, aim));
            Assert.AreEqual(BrawlWeaponPhase.Thrown, w.State.Weapon.Phase);
            Assert.AreNotEqual(ActionState.Charge, w.State.GetFighter(slot).Action);
            for (int i = 0; i < 12; i++)
            {
                Step(w, slot, Input(InputButtons.Attack, aim));
                Assert.AreEqual(0, Events(w, SimEventType.AttackStart));
                Assert.AreEqual(0, Events(w, SimEventType.LungeStart));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void FastThrow_CannotTunnelThroughThinWall_AndReboundCannotHitOwner(int slot)
        {
            int direction = slot == 0 ? 1 : -1;
            var w = Held(slot, config: Config(120f), arena: Arena(direction));
            Step(w, slot, Input(InputButtons.Grab, slot == 0 ? 0 : 32));
            Assert.AreEqual(BrawlWeaponPhase.Thrown, w.State.Weapon.Phase);
            Assert.Less(w.State.Weapon.Position.X * direction, 1f - w.Config.Combat.Weapons.ThrowRadius);
            Assert.Less(w.State.Weapon.Velocity.X * direction, 0f, "벽에서 약하게 반사");
            Assert.LessOrEqual(SimMath.Abs(w.State.Weapon.Velocity.X), 120f * 0.3f + 1e-4f);
            for (int i = 0; i < 180 && w.State.Weapon.Phase != BrawlWeaponPhase.Ground; i++) w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(100, w.State.Fighter0.Health);
            Assert.AreEqual(100, w.State.Fighter1.Health, "벽 뒤 상대와 반사 경로의 던진 본인은 피해 없음");
        }

        [Test]
        public void UpwardFastThrow_BouncesOffThinCeiling_ThenSettles()
        {
            var w = Held(config: Config(240f), arena: Arena(ceiling: true));
            w.State.Fighter0.Position.Y = 1f;
            Step(w, 0, Input(InputButtons.Grab, 16));
            Assert.Less(w.State.Weapon.Position.Y, 3f - w.Config.Combat.Weapons.ThrowRadius);
            Assert.Less(w.State.Weapon.Velocity.Y, 0f);
            for (int i = 0; i < 240 && w.State.Weapon.Phase != BrawlWeaponPhase.Ground; i++) w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.GreaterOrEqual(w.State.Weapon.Position.Y, w.Config.Combat.Weapons.ThrowRadius);
        }

        [TestCase(BrawlWeaponKind.Pan)]
        [TestCase(BrawlWeaponKind.Pistol)]
        public void FloorThrow_HasSmallBounce_SettlesAndCanBePickedUpWithoutReload(BrawlWeaponKind kind)
        {
            var w = Held(kind: kind);
            w.State.Fighter0.Position.Y = 2f;
            int ammo = w.State.Weapon.Ammo;
            Step(w, 0, Input(InputButtons.Grab, 48));
            bool bounced = false;
            for (int i = 0; i < 180 && w.State.Weapon.Phase != BrawlWeaponPhase.Ground; i++)
            {
                w.Step(default, default);
                bounced |= w.State.Weapon.Velocity.Y > 0f;
            }
            Assert.IsTrue(bounced, "바닥에서 적어도 한 번 작은 반동");
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(Vec2.Zero, w.State.Weapon.Velocity);
            var landed = w.State.Weapon.Position;
            for (int i = 0; i < 20; i++) w.Step(default, default);
            Assert.AreEqual(landed, w.State.Weapon.Position, "바닥에서 떨거나 다시 생성하지 않는다");
            w.State.Fighter0.Position = new Vec2(landed.X, w.Config.HalfBody);
            w.State.Fighter0.RestartAction(ActionState.Idle);
            w.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreEqual(kind, w.State.Weapon.Kind);
            Assert.AreEqual(ammo, w.State.Weapon.Ammo);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Throw_HitsOpponentOnlyOnce_WithTenDamageAndNoOwnerDamage(int slot)
        {
            var w = Held(slot, closeTarget: true);
            Step(w, slot, Input(InputButtons.Grab, slot == 0 ? 0 : 32));
            int hits = 0;
            for (int i = 0; i < 160; i++)
            {
                w.Step(default, default);
                hits += Events(w, SimEventType.Hit, 1 - slot);
            }
            Assert.AreEqual(1, hits);
            Assert.AreEqual(90, w.State.GetFighter(1 - slot).Health);
            Assert.AreEqual(100, w.State.GetFighter(slot).Health);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(2, w.State.Weapon.Ammo);
        }

        [Test]
        public void AirborneWeapon_CannotBePickedUpByGrab()
        {
            var w = Held();
            Step(w, 0, Input(InputButtons.Grab, 16));
            w.Step(default, default);
            var before = w.State.Weapon;
            Step(w, 0, Input(InputButtons.Grab));
            Assert.AreEqual(BrawlWeaponPhase.Thrown, w.State.Weapon.Phase);
            Assert.AreEqual(-1, w.State.Weapon.Owner);
            Assert.AreEqual(before.Ammo, w.State.Weapon.Ammo);
            Assert.AreEqual(0, Events(w, SimEventType.BrawlWeaponPickup));
        }

        [Test]
        public void KnockoutDuringFlight_ClearsThrowState_AndNextRoundKeepsNewRules()
        {
            var w = Held();
            Step(w, 0, Input(InputButtons.Grab, 16));
            uint rng = w.State.Rng.State;
            w.State.Fighter1.Health = 0;
            w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Finished, w.State.Weapon.Phase);
            Assert.IsTrue(w.State.Weapon.ThrowEnabled);
            Assert.AreEqual(Vec2.Zero, w.State.Weapon.Velocity);
            Assert.AreEqual(-1, w.State.Weapon.Thrower);
            Assert.AreEqual(0, w.State.Weapon.Ammo);
            int round = w.State.Match.Round;
            for (int i = 0; i < 1000 && w.State.Match.Round == round; i++) w.Step(default, default);
            Assert.AreEqual(round + 1, w.State.Match.Round);
            Assert.AreEqual(BrawlWeaponPhase.Waiting, w.State.Weapon.Phase);
            Assert.IsTrue(w.State.Weapon.ThrowEnabled);
            Assert.AreEqual(rng, w.State.Rng.State);
        }

        static InputFrame[] Sequence(bool revisedBalance = false)
        {
            var inputs = new InputFrame[150];
            inputs[8] = Input(InputButtons.Grab);
            inputs[10] = Input(InputButtons.Grab);
            if (revisedBalance)
            {
                for (int t = 60; t < 72; t++) inputs[t] = Input(InputButtons.Attack, 16);
                inputs[72] = Input(InputButtons.None, 16); // 최소 충전 미달 취소.
                for (int t = 100; t < 122; t++) inputs[t] = Input(InputButtons.Attack, 16);
                inputs[122] = Input(InputButtons.None, 16); // 충분한 충전은 발사.
            }
            return inputs;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowSequence_MatchesTwinRestoreSyncAndSerializedReplay(bool revisedBalance)
        {
            var config = Config(revisedBalance: revisedBalance);
            var a = Fresh(config);
            var b = Fresh(config);
            var rewind = Fresh(config);
            var sync = new SyncTest<SimWorld>(rewind, 7);
            var frames = Sequence(revisedBalance);
            var recording = InputRecording.Begin(a.Config, a.Arena, 1u, a.State.Fighter0.Position, a.State.Fighter1.Position,
                MatchPhase.Fighting, RuleSet.Brawl.Format());
            var checks = new uint[frames.Length + 1];
            checks[0] = a.ComputeChecksum();
            SimState snapshot = default;
            int throws = 0, hits = 0, lunges = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                a.Step(frames[i], default);
                b.Step(frames[i], default);
                sync.Advance(frames[i], default);
                recording.Append(frames[i], default);
                checks[i + 1] = a.ComputeChecksum();
                Assert.AreEqual(checks[i + 1], b.ComputeChecksum());
                throws += Events(a, SimEventType.BrawlWeaponThrow);
                hits += Events(a, SimEventType.Hit, 1);
                lunges += Events(a, SimEventType.LungeStart);
                if (revisedBalance && i == 72) Assert.AreNotEqual(ActionState.Lunge, a.State.Fighter0.Action);
                if (i == 12) snapshot = a.State;
            }
            Assert.AreEqual(1, throws);
            Assert.AreEqual(1, hits);
            Assert.AreEqual(revisedBalance ? 1 : 0, lunges);
            Assert.AreEqual(BrawlWeaponPhase.Thrown, snapshot.Weapon.Phase, "비행 중 상태를 실제 복원");
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(a.ComputeChecksum(), rewind.ComputeChecksum());
            b.State = snapshot;
            for (int i = 13; i < frames.Length; i++)
            {
                b.Step(frames[i], default);
                Assert.AreEqual(checks[i + 1], b.ComputeChecksum(), $"restore {i}");
            }
            recording.Finish(a.ComputeChecksum());
            var restored = InputRecording.FromBytes(recording.ToBytes());
            CollectionAssert.AreEqual(checks, ReplayTrace.Run(restored, a.Config, new[] { a.Arena }, null, null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowSequence_AllocatesNoBytesAfterWarmup(bool revisedBalance)
        {
            var world = Fresh(Config(revisedBalance: revisedBalance));
            var initial = world.State;
            var frames = Sequence(revisedBalance);
            for (int i = 0; i < frames.Length; i++) world.Step(frames[i], default);
            Assert.AreEqual(90, world.State.Fighter1.Health, "워밍업에서 실제 투척 명중을 지나야 함");
            uint expected = world.ComputeChecksum();
            world.State = initial;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames.Length; i++) world.Step(frames[i], default);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(expected, world.ComputeChecksum());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MemoryPeers_ExchangeThrowAndCollision_AndMatchEachCommittedTick(bool revisedBalance)
        {
            var config = Config(revisedBalance: revisedBalance);
            var a = Fresh(config);
            var b = Fresh(config);
            MemoryTransport.Pair(out var ta, out var tb);
            using var host = new LockstepSession(ta, true, a.Config.ComputeChecksum(), a.Arena.ComputeChecksum(), 1u, 3);
            using var guest = new LockstepSession(tb, false, b.Config.ComputeChecksum(), b.Arena.ComputeChecksum(), 0u, 0);
            double now = 0;
            for (int i = 0; i < 100 && (host.State != SessionState.Running || guest.State != SessionState.Running); i++)
            {
                now += 1.0 / 60.0;
                guest.Poll(now);
                host.Poll(now);
            }
            Assert.AreEqual(SessionState.Running, host.State);
            Assert.AreEqual(SessionState.Running, guest.State);
            host.Begin(0, new FrameArrayInputSource(Sequence(revisedBalance)));
            guest.Begin(0, new IdleInputSource());
            var checksA = new List<uint>();
            var checksB = new List<uint>();
            int throws = 0, hits = 0;
            void Advance(LockstepSession session, SimWorld world, List<uint> checks, bool count)
            {
                session.Poll(now);
                for (int i = 0; i < 2 && checks.Count < 150 && session.TryGetInputs(world.State.Tick, out var p0, out var p1); i++)
                {
                    world.Step(p0, p1);
                    checks.Add(world.ComputeChecksum());
                    session.ReportChecksum(world.State.Tick, world.ComputeChecksum());
                    if (count)
                    {
                        throws += Events(world, SimEventType.BrawlWeaponThrow);
                        hits += Events(world, SimEventType.Hit, 1);
                    }
                }
            }
            for (int i = 0; i < 600 && (checksA.Count < 150 || checksB.Count < 150); i++)
            {
                now += 1.0 / 60.0;
                Advance(host, a, checksA, true);
                Advance(guest, b, checksB, false);
            }
            Assert.AreEqual(SessionState.Running, host.State, host.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.State, guest.EndDetail);
            Assert.AreEqual(150, checksA.Count);
            Assert.AreEqual(150, checksB.Count);
            CollectionAssert.AreEqual(checksA, checksB);
            Assert.AreEqual(1, throws);
            Assert.AreEqual(1, hits);
            Assert.Greater(host.ChecksumsCompared + guest.ChecksumsCompared, 0);
        }
        static float FirstFloorContactDistance(bool revisedBalance, int aim)
        {
            var arena = new ArenaDef("Throw range", 100, 40,
                new[] { new ArenaBox(TerrainKind.Solid, -50f, -1f, 50f, 0f) }, new Vec2(-40f, 0f), new Vec2(40f, 0f));
            var w = Held(config: Config(revisedBalance: revisedBalance), arena: arena);
            w.State.Fighter1.Position.X = -40f;
            w.Step(Input(InputButtons.Grab, aim), default);
            for (int t = 0; t < 240; t++)
            {
                var before = w.State.Weapon.Velocity;
                w.Step(default, default);
                if (w.State.Weapon.Phase == BrawlWeaponPhase.Ground || before.Y < 0f && w.State.Weapon.Velocity.Y >= 0f)
                    return w.State.Weapon.Position.X;
            }
            Assert.Fail("투척한 무기가 바닥에 닿아야 한다");
            return 0f;
        }

        [TestCase(0)]
        [TestCase(8)]
        public void FasterThrow_ReachesFartherBeforeFloorContact_WithoutChangingGravity(int aim)
        {
            Assert.AreEqual(Config().Combat.Weapons.ThrowGravity, Config(revisedBalance: true).Combat.Weapons.ThrowGravity);
            float before = FirstFloorContactDistance(false, aim);
            float after = FirstFloorContactDistance(true, aim);
            Assert.Greater(after, before * 1.3f);
            TestContext.WriteLine($"aim={aim * 5.625f}deg first-floor range {before:F3} -> {after:F3}");
        }
    }
}
