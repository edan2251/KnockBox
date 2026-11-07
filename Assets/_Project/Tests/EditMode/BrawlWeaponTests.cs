using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>무기 판정, 상태 복원·리플레이와 실제 무기 입력을 교환하는 메모리 온라인 회귀.</summary>
    public class BrawlWeaponTests
    {
        static SimConfig Config(bool enabled = true)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: enabled);
            if (enabled)
            {
                combat.Weapons.SpawnDelayTicks = 3;
                combat.Weapons.WarningTicks = 2;
                combat.Weapons.FallTicks = 3;
                combat.Weapons.DropTicks = 2;
            }
            var match = BrawlParams.Match(MatchParams.Default);
            match.CountdownTicks = 3;
            return new SimConfig(MovementParams.Default, match, combat, RuleSet.Brawl);
        }

        static ArenaDef Arena(bool obstruction = false)
        {
            var boxes = new List<ArenaBox>
            {
                new ArenaBox(TerrainKind.Solid, -16f, -1f, 16f, 0f),
                new ArenaBox(TerrainKind.Solid, -16f, 0f, -15f, 9f),
                new ArenaBox(TerrainKind.Solid, 15f, 0f, 16f, 9f),
            };
            if (obstruction)
                boxes.Add(new ArenaBox(TerrainKind.Solid, 1.5f, 0f, 1.6f, 3f));
            return new ArenaDef("Weapon test", boxes.ToArray(), new Vec2(-6f, 0f), new Vec2(6f, 0f));
        }

        static SimWorld Fresh(SimConfig config = null, ArenaDef arena = null, uint seed = 1,
            float x0 = -0.6f, float x1 = 5f, MatchPhase phase = MatchPhase.Fighting)
        {
            config ??= Config();
            arena ??= Arena();
            return new SimWorld(config, arena, SimState.CreateRound(config, arena, seed,
                new Vec2(x0, config.HalfBody), new Vec2(x1, config.HalfBody), phase));
        }

        static InputFrame Input(InputButtons buttons, int aim = 0) => new InputFrame(0, 0, buttons, AimDirections.Encode(aim));
        static void Run(SimWorld w, int ticks, InputFrame p0 = default, InputFrame p1 = default)
        {
            for (int i = 0; i < ticks; i++) w.Step(p0, p1);
        }
        static int Events(SimWorld w, SimEventType type)
        {
            int n = 0;
            for (int i = 0; i < w.Events.Count; i++) if (w.Events[i].Type == type) n++;
            return n;
        }
        static SimWorld Ground(BrawlWeaponKind kind, float x0 = -0.6f, float x1 = 5f, float weaponX = 0f, ArenaDef arena = null)
        {
            var w = Fresh(arena: arena, x0: x0, x1: x1);
            w.State.Weapon.Kind = kind;
            w.State.Weapon.Phase = BrawlWeaponPhase.Ground;
            w.State.Weapon.Position = new Vec2(weaponX, w.Config.Combat.Weapons.GroundHeight);
            w.State.Weapon.LandingPosition = w.State.Weapon.Position;
            w.State.Weapon.Ammo = kind == BrawlWeaponKind.Pistol ? w.Config.Combat.Weapons.PistolAmmo : 0;
            return w;
        }
        static void PickUp(SimWorld w, int slot = 0)
        {
            w.Step(slot == 0 ? Input(InputButtons.Grab) : default, slot == 1 ? Input(InputButtons.Grab, 32) : default);
            Assert.AreEqual(BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreEqual(slot, w.State.Weapon.Owner);
            w.Step(default, default);
        }

        [TestCase(1u, BrawlWeaponKind.Pistol)]
        [TestCase(2u, BrawlWeaponKind.Pan)]
        public void Spawn_HasExactWarningAndFallTiming_OneRandomChoiceAndNoLandingDamage(uint seed, BrawlWeaponKind kind)
        {
            var w = Fresh(seed: seed, x0: 0f);
            var p = w.Config.Combat.Weapons;
            var rng = w.State.Rng;
            rng.Range(0, 2);
            Run(w, p.SpawnDelayTicks - 1);
            Assert.AreEqual(BrawlWeaponPhase.Waiting, w.State.Weapon.Phase);
            Assert.AreEqual(seed, w.State.Rng.State);
            w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Warning, w.State.Weapon.Phase);
            Assert.AreEqual(kind, w.State.Weapon.Kind);
            Assert.AreEqual(rng.State, w.State.Rng.State);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponWarning));
            Vec2 target = w.State.Weapon.LandingPosition;
            Assert.AreEqual(0f, target.X);
            Assert.AreEqual(p.GroundHeight, target.Y, 1e-6f);
            Run(w, p.WarningTicks - 1);
            Assert.AreEqual(BrawlWeaponPhase.Warning, w.State.Weapon.Phase);
            w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Falling, w.State.Weapon.Phase);
            Run(w, p.FallTicks - 1);
            Assert.AreEqual(BrawlWeaponPhase.Falling, w.State.Weapon.Phase);
            Assert.Greater(w.State.Weapon.Position.Y, target.Y);
            w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(target, w.State.Weapon.Position);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponLanded));
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter0.Health, "바로 아래 선수에게 낙하 피해 없음");
            Run(w, 100);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(kind, w.State.Weapon.Kind);
            Assert.AreEqual(rng.State, w.State.Rng.State, "바닥에 남아 있는 동안 재추첨하지 않음");
        }

        [Test]
        public void CountdownDoesNotAdvanceSpawn_AndDisabledWeaponsConsumeNoRng()
        {
            var w = Fresh(phase: MatchPhase.Countdown);
            Run(w, w.Config.Match.CountdownTicks);
            Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
            Assert.AreEqual(0, w.State.Weapon.AgeTicks);
            Assert.AreEqual(BrawlWeaponPhase.Waiting, w.State.Weapon.Phase);
            var disabled = Fresh(Config(false));
            uint rng = disabled.State.Rng.State;
            Run(disabled, 400);
            Assert.AreEqual(default(BrawlWeaponState), disabled.State.Weapon);
            Assert.AreEqual(rng, disabled.State.Rng.State);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Pickup_BeatsOpponentGrabAndSimultaneousAttack_AndStaysHeld(int slot)
        {
            var w = Ground(BrawlWeaponKind.Pan, -0.6f, 0.6f);
            var input = Input(InputButtons.Grab | InputButtons.Attack | InputButtons.Charge, slot == 0 ? 0 : 32);
            w.Step(slot == 0 ? input : default, slot == 1 ? input : default);
            Assert.AreEqual(slot, w.State.Weapon.Owner);
            Assert.AreEqual(BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreEqual(0, Events(w, SimEventType.AttackStart));
            Assert.AreNotEqual(ActionState.Holding, w.State.GetFighter(slot).Action);
            Assert.AreNotEqual(ActionState.Held, w.State.GetFighter(1 - slot).Action);
            Run(w, 60);
            Assert.AreEqual(slot, w.State.Weapon.Owner, "Grab를 뗀 뒤에도 계속 든다");
        }

        [TestCase(-0.6f, 1f, 0)]
        [TestCase(-1f, 0.6f, 1)]
        [TestCase(-0.6f, 0.6f, -1)]
        public void SimultaneousPickup_NearestWins_ExactTieHasNoSlotPriority(float x0, float x1, int owner)
        {
            var w = Ground(BrawlWeaponKind.Pistol, x0, x1);
            w.Step(Input(InputButtons.Grab), Input(InputButtons.Grab, 32));
            Assert.AreEqual(owner, w.State.Weapon.Owner);
            Assert.AreEqual(owner < 0 ? BrawlWeaponPhase.Ground : BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter1.Action);
        }

        [Test]
        public void Pickup_RequiresRangeAndClearTerrainLine()
        {
            var far = Ground(BrawlWeaponKind.Pan, x0: -2f);
            far.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(-1, far.State.Weapon.Owner);
            var blocked = Ground(BrawlWeaponKind.Pan, x0: 1f, weaponX: 2f, arena: Arena(true));
            blocked.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(-1, blocked.State.Weapon.Owner, "얇은 벽 너머의 가까운 무기는 줍지 않는다");
        }

        [TestCase(0.6f, true)]
        [TestCase(3f, false)]
        public void Pan_IsOneMeleeHit_AfterStartupAndOnlyWithinReach(float targetX, bool hit)
        {
            var w = Ground(BrawlWeaponKind.Pan, x1: targetX);
            PickUp(w);
            w.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(AttackIds.Pan, w.State.Fighter0.AttackId);
            Run(w, w.Config.Combat.Weapons.Pan.StartupTicks - 1);
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health, "선딜 중 피해 없음");
            Run(w, 45);
            Assert.AreEqual(w.Config.Combat.MaxHealth - (hit ? w.Config.Combat.Weapons.Pan.Damage : 0), w.State.Fighter1.Health);
            Assert.AreEqual(BrawlWeaponKind.Pan, w.State.Weapon.Kind);
            Assert.AreEqual(0, w.State.Weapon.Owner);
        }

        [TestCase(false, 5f, true)]
        [TestCase(true, 5f, false)]
        public void Pistol_UsesRangedHit_StopsAtThinTerrain(bool obstruction, float targetX, bool hit)
        {
            var w = Ground(BrawlWeaponKind.Pistol, x1: targetX, arena: Arena(obstruction));
            PickUp(w);
            w.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponShot));
            Assert.AreEqual(w.Config.Combat.MaxHealth - (hit ? w.Config.Combat.Weapons.PistolDamage : 0), w.State.Fighter1.Health);
            Assert.AreEqual(w.Config.Combat.Weapons.PistolAmmo - 1, w.State.Weapon.Ammo);
            if (obstruction)
            {
                for (int i = 0; i < w.Events.Count; i++)
                    if (w.Events[i].Type == SimEventType.BrawlWeaponShot)
                        Assert.AreEqual(1.5f, w.Events[i].Position.X + w.Events[i].Direction.X, 1e-5f, "시각 탄흔도 벽에서 끝남");
            }
        }

        [Test]
        public void Pistol_EnforcesRecovery_UsesSixRoundsAndDryFiresWithoutPunchFallback()
        {
            var w = Ground(BrawlWeaponKind.Pistol);
            PickUp(w);
            int interval = w.Config.Combat.Weapons.Pistol.ActiveTicks + w.Config.Combat.Weapons.Pistol.RecoveryTicks;
            int shots = 0, previousShot = -1000, empties = 0;
            // 위쪽으로 격발해 피격 경직과 KO 없이 순수 발사 간격·탄약을 확인한다.
            for (int tick = 0; tick < interval * 9; tick++)
            {
                w.Step(Input(tick % 2 == 0 ? InputButtons.Attack : InputButtons.None, 16), default);
                if (Events(w, SimEventType.BrawlWeaponShot) > 0)
                {
                    Assert.GreaterOrEqual(tick - previousShot, interval);
                    previousShot = tick;
                    shots++;
                }
                empties += Events(w, SimEventType.BrawlWeaponEmpty);
                if (w.State.Fighter0.Action == ActionState.Attack)
                    Assert.AreEqual(AttackIds.Pistol, w.State.Fighter0.AttackId);
            }
            Assert.AreEqual(6, shots);
            Assert.AreEqual(0, w.State.Weapon.Ammo);
            Assert.Greater(empties, 0);
            Assert.AreEqual(0, w.State.Weapon.Owner);
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
            Run(w, interval);
            w.Step(Input(InputButtons.Grab), default);
            Run(w, w.Config.Combat.Weapons.DropTicks);
            PickUp(w);
            Assert.AreEqual(0, w.State.Weapon.Ammo, "빈 권총도 내려놓고 다시 주울 수 있지만 장전되지 않는다");
        }

        [Test]
        public void Pistol_DoesNotHitBeyondConfiguredRange()
        {
            var w = Ground(BrawlWeaponKind.Pistol, x0: -14f, x1: 14f, weaponX: -14f);
            PickUp(w);
            w.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponShot));
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        [TestCase(BrawlWeaponKind.Pan, false, false)]
        [TestCase(BrawlWeaponKind.Pistol, false, false)]
        [TestCase(BrawlWeaponKind.Pan, true, false)]
        [TestCase(BrawlWeaponKind.Pistol, true, false)]
        [TestCase(BrawlWeaponKind.Pan, false, true)]
        [TestCase(BrawlWeaponKind.Pistol, false, true)]
        [TestCase(BrawlWeaponKind.Pan, true, true)]
        [TestCase(BrawlWeaponKind.Pistol, true, true)]
        public void HolderGrab_UsesCurrentTickRecovery_AndNeverLeaksIntoOpponentGrab(BrawlWeaponKind kind, bool stun, bool endsNow)
        {
            var w = Ground(kind, x1: 0.6f);
            PickUp(w);
            if (stun)
            {
                w.State.Fighter0.RestartAction(ActionState.Hitstun);
                w.State.Fighter0.StunTicks = endsNow ? 0 : 1;
            }
            else
            {
                int id = kind == BrawlWeaponKind.Pan ? AttackIds.Pan : AttackIds.Pistol;
                var attack = w.Config.GetAttack(id);
                w.State.Fighter0.RestartAction(ActionState.Attack);
                w.State.Fighter0.AttackId = id;
                w.State.Fighter0.ActionTick = attack.TotalTicks - (endsNow ? 1 : 2);
            }
            w.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(endsNow ? -1 : 0, w.State.Weapon.Owner);
            Assert.AreEqual(endsNow ? BrawlWeaponPhase.Falling : BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Held, w.State.Fighter1.Action);
            if (endsNow) return; // 이번 틱부터 조작 가능하면 같은 Grab 입력으로 즉시 내려놓는다.
            Run(w, 3, Input(InputButtons.Grab));
            Assert.AreEqual(0, w.State.Weapon.Owner, "회복 경계의 입력을 누른 채 유지해도 뒤늦게 내려놓지 않는다");
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            w.Step(default, default);
            w.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(-1, w.State.Weapon.Owner, "자유 상태의 새 Grab 입력은 정상적으로 내려놓는다");
            Assert.AreEqual(BrawlWeaponPhase.Falling, w.State.Weapon.Phase);
        }

        [TestCase(BrawlWeaponKind.Pan, false)]
        [TestCase(BrawlWeaponKind.Pistol, false)]
        [TestCase(BrawlWeaponKind.Pan, true)]
        [TestCase(BrawlWeaponKind.Pistol, true)]
        public void GroundPickup_OnRecoveryBoundaryStillBeatsOpponentGrab(BrawlWeaponKind kind, bool stun)
        {
            var w = Ground(kind, x1: 0.6f);
            if (stun)
            {
                w.State.Fighter0.RestartAction(ActionState.Hitstun);
                w.State.Fighter0.StunTicks = 0;
            }
            else
            {
                w.State.Fighter0.RestartAction(ActionState.Attack);
                w.State.Fighter0.AttackId = AttackIds.Punch;
                w.State.Fighter0.ActionTick = w.Config.GetAttack(AttackIds.Punch).TotalTicks - 1;
            }
            w.Step(Input(InputButtons.Grab | InputButtons.Attack), default);
            Assert.AreEqual(0, w.State.Weapon.Owner);
            Assert.AreEqual(BrawlWeaponPhase.Held, w.State.Weapon.Phase);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Held, w.State.Fighter1.Action);
            Assert.AreEqual(0, Events(w, SimEventType.AttackStart));
        }

        [TestCase(BrawlWeaponKind.Pan)]
        [TestCase(BrawlWeaponKind.Pistol)]
        public void HeldWeapon_ChargeSuppressesAttack_GrabDropsAndCancelsCharge_AmmoPersists(BrawlWeaponKind kind)
        {
            var w = Ground(kind);
            PickUp(w);
            if (kind == BrawlWeaponKind.Pistol) w.State.Weapon.Ammo = 2;
            int ammo = w.State.Weapon.Ammo;
            Run(w, 10, Input(InputButtons.Charge | InputButtons.Attack));
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            Assert.AreEqual(ammo, w.State.Weapon.Ammo);
            w.Step(Input(InputButtons.Charge | InputButtons.Grab | InputButtons.Attack), default);
            Assert.AreEqual(-1, w.State.Weapon.Owner);
            Assert.AreEqual(BrawlWeaponPhase.Falling, w.State.Weapon.Phase);
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action);
            Assert.AreEqual(0, Events(w, SimEventType.AttackStart));
            Run(w, w.Config.Combat.Weapons.DropTicks);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            PickUp(w);
            Assert.AreEqual(ammo, w.State.Weapon.Ammo, "다시 주워도 장전하지 않는다");
            Assert.AreEqual(kind, w.State.Weapon.Kind);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void WallSplat_DropsTheOwnersWeapon_AndItRemainsOnTheFloor(int slot)
        {
            var w = Ground(BrawlWeaponKind.Pistol, x0: slot == 0 ? -0.6f : -5f, x1: slot == 1 ? 0.6f : 5f);
            PickUp(w, slot);
            var f = w.State.GetFighter(slot);
            f.Position = new Vec2(14.45f, 2f);
            f.Velocity = new Vec2(20f, 0f);
            f.Grounded = false;
            f.StunTicks = 30;
            f.RestartAction(ActionState.Hitstun);
            if (slot == 0) w.State.Fighter0 = f; else w.State.Fighter1 = f;
            w.Step(default, default);
            Assert.AreEqual(1, Events(w, SimEventType.WallSplat), "실제 지형 접촉에서 벽꽝을 발생시킨다");
            Assert.AreEqual(-1, w.State.Weapon.Owner);
            Assert.AreEqual(1, Events(w, SimEventType.BrawlWeaponDrop));
            Run(w, w.Config.Combat.Weapons.DropTicks + 60);
            Assert.AreEqual(BrawlWeaponPhase.Ground, w.State.Weapon.Phase);
            Assert.AreEqual(BrawlWeaponKind.Pistol, w.State.Weapon.Kind);
            Assert.AreEqual(6, w.State.Weapon.Ammo);
        }

        [Test]
        public void KnockoutClearsWeapon_NextRoundResetsSpawnButCarriesRng()
        {
            var w = Fresh();
            Run(w, 8);
            PickUp(w);
            uint rng = w.State.Rng.State;
            w.State.Fighter1.Health = 0;
            w.Step(default, default);
            Assert.AreEqual(MatchPhase.KO, w.State.Match.Phase);
            Assert.AreEqual(BrawlWeaponPhase.Finished, w.State.Weapon.Phase);
            Assert.AreEqual(BrawlWeaponKind.None, w.State.Weapon.Kind);
            Assert.AreEqual(-1, w.State.Weapon.Owner);
            Assert.AreEqual(0, w.State.Weapon.Ammo);
            int oldRound = w.State.Match.Round;
            for (int i = 0; i < 1000 && w.State.Match.Round == oldRound; i++) w.Step(default, default);
            Assert.AreEqual(oldRound + 1, w.State.Match.Round);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(BrawlWeaponPhase.Waiting, w.State.Weapon.Phase);
            Assert.AreEqual(0, w.State.Weapon.AgeTicks);
            Assert.AreEqual(rng, w.State.Rng.State);
            var expected = w.State.Rng;
            expected.Range(0, 2);
            for (int i = 0; i < 30 && w.State.Weapon.Phase == BrawlWeaponPhase.Waiting; i++) w.Step(default, default);
            Assert.AreEqual(BrawlWeaponPhase.Warning, w.State.Weapon.Phase);
            Assert.AreEqual(expected.State, w.State.Rng.State, "다음 라운드도 이어진 난수에서 한 번만 추첨한다");
        }

        static InputFrame[] WeaponSequence()
        {
            var frames = new InputFrame[130];
            frames[8] = Input(InputButtons.Grab);
            frames[10] = Input(InputButtons.Attack);
            frames[35] = Input(InputButtons.Attack);
            for (int i = 71; i < 79; i++) frames[i] = Input(InputButtons.Charge);
            frames[79] = Input(InputButtons.Charge | InputButtons.Grab);
            frames[82] = Input(InputButtons.Grab);
            frames[84] = Input(InputButtons.Attack);
            return frames;
        }

        [Test]
        public void SpawnPickupFireDropSequence_ClonesRestoresSyncsAndReplaysEveryTick()
        {
            var config = Config();
            var arena = Arena();
            var frames = WeaponSequence();
            var a = Fresh(config, arena);
            var b = Fresh(config, arena);
            var syncWorld = Fresh(config, arena);
            var sync = new SyncTest<SimWorld>(syncWorld, 7);
            var recording = InputRecording.Begin(config, arena, 1u, a.State.Fighter0.Position, a.State.Fighter1.Position,
                MatchPhase.Fighting, RuleSet.Brawl.Format());
            SimState snapshot = default;
            int pickups = 0, shots = 0, drops = 0;
            var expected = new uint[frames.Length + 1];
            expected[0] = a.ComputeChecksum();
            for (int t = 0; t < frames.Length; t++)
            {
                a.Step(frames[t], default);
                b.Step(frames[t], default);
                sync.Advance(frames[t], default);
                recording.Append(frames[t], default);
                expected[t + 1] = a.ComputeChecksum();
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"tick {t + 1}");
                pickups += Events(a, SimEventType.BrawlWeaponPickup);
                shots += Events(a, SimEventType.BrawlWeaponShot);
                drops += Events(a, SimEventType.BrawlWeaponDrop);
                if (t == 20) snapshot = a.State;
            }
            Assert.AreEqual(2, pickups, "첫 획득과 내려놓은 무기의 재획득을 실제로 지나야 한다");
            Assert.AreEqual(3, shots);
            Assert.AreEqual(1, drops);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(a.ComputeChecksum(), syncWorld.ComputeChecksum());
            var original = snapshot;
            snapshot.Weapon.Ammo--;
            Assert.AreNotEqual(original.ComputeChecksum(), snapshot.ComputeChecksum());
            b.State = original;
            for (int t = 21; t < frames.Length; t++)
            {
                b.Step(frames[t], default);
                Assert.AreEqual(expected[t + 1], b.ComputeChecksum(), $"restore tick {t + 1}");
            }
            recording.Finish(a.ComputeChecksum());
            var bytes = recording.ToBytes();
            Assert.AreEqual(8, BitConverter.ToUInt16(bytes, 4));
            var restored = InputRecording.FromBytes(bytes);
            var trace = ReplayTrace.Run(restored, config, new[] { arena }, null, null);
            Assert.AreEqual(-1, ReplayTrace.FirstMismatch(trace, expected));
            Assert.AreEqual(recording.FinalChecksum, restored.Replay(config, arena));
        }

        [Test]
        public void WeaponSequence_StepAllocatesZeroBytesAfterWarmup()
        {
            var frames = WeaponSequence();
            var world = Fresh();
            var initial = world.State;
            int pickups = 0, shots = 0, drops = 0;
            // 동일한 스폰·획득·격발·내려놓기 경로와 기존 전투 경로를 먼저 모두 예열한다.
            for (int t = 0; t < frames.Length; t++)
            {
                world.Step(frames[t], default);
                pickups += Events(world, SimEventType.BrawlWeaponPickup);
                shots += Events(world, SimEventType.BrawlWeaponShot);
                drops += Events(world, SimEventType.BrawlWeaponDrop);
            }
            Assert.AreEqual(2, pickups);
            Assert.AreEqual(3, shots);
            Assert.AreEqual(1, drops);
            uint expected = world.ComputeChecksum();
            world.State = initial;
            // 측정 구간에는 미리 만든 입력 배열과 Step만 사용한다(NUnit·이벤트 집계·문자열 없음).
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < frames.Length; t++) world.Step(frames[t], default);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "무기 스폰·획득·공격·내려놓기 Step에서 GC 할당 금지");
            Assert.AreEqual(expected, world.ComputeChecksum());
        }

        [Test]
        public void MemoryPeers_ExchangeActualPickupAndShots_AndMatchEveryCommittedTick()
        {
            var config = Config();
            var arena = Arena();
            var hostWorld = Fresh(config, arena);
            var guestWorld = Fresh(config, arena);
            MemoryTransport.Pair(out var hostTransport, out var guestTransport);
            using var host = new LockstepSession(hostTransport, true, config.ComputeChecksum(), arena.ComputeChecksum(), 1u, 3);
            using var guest = new LockstepSession(guestTransport, false, config.ComputeChecksum(), arena.ComputeChecksum(), 0u, 0);
            double now = 0;
            for (int i = 0; i < 100 && (host.State != SessionState.Running || guest.State != SessionState.Running); i++)
            {
                now += 1.0 / 60.0;
                guest.Poll(now);
                host.Poll(now);
            }
            Assert.AreEqual(SessionState.Running, host.State);
            Assert.AreEqual(SessionState.Running, guest.State);
            host.Begin(0, new FrameArrayInputSource(WeaponSequence()));
            guest.Begin(0, new IdleInputSource());
            var hostChecks = new List<uint>();
            var guestChecks = new List<uint>();
            int pickups = 0, shots = 0;
            void Advance(LockstepSession session, SimWorld world, List<uint> checks, bool countEvents)
            {
                session.Poll(now);
                for (int i = 0; i < 2 && checks.Count < 150 && session.TryGetInputs(world.State.Tick, out var p0, out var p1); i++)
                {
                    world.Step(p0, p1);
                    checks.Add(world.ComputeChecksum());
                    session.ReportChecksum(world.State.Tick, world.ComputeChecksum());
                    if (countEvents)
                    {
                        pickups += Events(world, SimEventType.BrawlWeaponPickup);
                        shots += Events(world, SimEventType.BrawlWeaponShot);
                    }
                }
            }
            for (int i = 0; i < 600 && (hostChecks.Count < 150 || guestChecks.Count < 150); i++)
            {
                now += 1.0 / 60.0;
                Advance(host, hostWorld, hostChecks, true);
                Advance(guest, guestWorld, guestChecks, false);
            }
            Assert.AreEqual(SessionState.Running, host.State, host.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.State, guest.EndDetail);
            Assert.AreEqual(150, hostChecks.Count);
            Assert.AreEqual(150, guestChecks.Count);
            CollectionAssert.AreEqual(hostChecks, guestChecks);
            Assert.AreEqual(2, pickups);
            Assert.AreEqual(3, shots);
            Assert.Greater(host.ChecksumsCompared + guest.ChecksumsCompared, 0);
        }
    }
}
