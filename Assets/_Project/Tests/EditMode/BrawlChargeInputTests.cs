using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Knockbox.Tests.EditMode
{
    /// <summary>몸 폼의 독립 차징 입력과 기존 탭/홀드 호환 경계를 검사한다.</summary>
    public class BrawlChargeInputTests
    {
        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
        static SimConfig Config(bool separate = true) => new SimConfig(MovementParams.Default,
            BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default, separateChargeInput: separate), RuleSet.Brawl);
        static SimWorld World(bool separate = true, float x0 = -8f, float x1 = 8f)
        {
            var config = Config(separate);
            float y = Arena.SpawnFoot0.Y + config.HalfBody;
            return TestWorlds.CreateAt(new Vec2(x0, y), new Vec2(x1, y), config, Arena);
        }
        static InputFrame Input(InputButtons buttons, int aim = 0) => new InputFrame(0, 0, buttons, AimDirections.Encode(aim));
        static void Step(SimWorld w, int fighter, InputFrame input)
        {
            if (fighter == 0) w.Step(input, default);
            else w.Step(default, input);
        }
        static int Count(SimWorld w, SimEventType type, int fighter = 0)
        {
            int count = 0;
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter) count++;
            return count;
        }

        [TestCase(0)]
        [TestCase(1)]
        public void AttackPress_PunchesImmediately_HoldingDoesNotChargeOrRepeat(int fighter)
        {
            var w = World();
            int punches = 0;
            for (int t = 0; t < 80; t++)
            {
                Step(w, fighter, Input(InputButtons.Attack, fighter == 0 ? 0 : 32));
                var f = w.State.GetFighter(fighter);
                if (t == 0)
                {
                    Assert.AreEqual(ActionState.Attack, f.Action);
                    Assert.AreEqual(AttackIds.Punch, f.AttackId);
                }
                punches += Count(w, SimEventType.AttackStart, fighter);
                Assert.AreNotEqual(ActionState.Charge, f.Action);
                Assert.AreNotEqual(ActionState.Lunge, f.Action);
            }
            Assert.AreEqual(1, punches, "누른 채로는 차징이나 자동 연타로 바뀌지 않는다");
        }

        [TestCase(1)]
        [TestCase(7)]
        public void ChargeReleasedBeforeEightTicks_CancelsWithoutPunchOrLunge(int releaseTick)
        {
            var w = World();
            w.Step(Input(InputButtons.Charge), default);
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            for (int t = 1; t < releaseTick; t++) w.Step(Input(InputButtons.Charge), default);
            w.Step(Input(InputButtons.None), default);
            Assert.IsTrue(w.State.Fighter0.InMovementState);
            Assert.AreEqual(0, Count(w, SimEventType.AttackStart));
            Assert.AreEqual(0, Count(w, SimEventType.LungeStart));
            Assert.AreEqual(0, w.State.Fighter0.AttackBuffer);
        }

        [Test]
        public void ChargeAtThreshold_LaunchesTowardReleaseAim()
        {
            var w = World();
            w.Step(Input(InputButtons.Charge, 0), default);
            for (int t = 1; t < w.Config.Combat.Body.TapHoldTicks; t++)
                w.Step(Input(InputButtons.Charge, 0), default);
            w.Step(Input(InputButtons.None, 24), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            Assert.AreEqual(1, Count(w, SimEventType.LungeStart));
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Type == SimEventType.LungeStart)
                {
                    var direction = AimDirections.Direction(24);
                    Assert.AreEqual(direction.X, w.Events[i].Direction.X);
                    Assert.AreEqual(direction.Y, w.Events[i].Direction.Y);
                }
        }

        [Test]
        public void HoldingCharge_AutoFiresAfterExistingMaximumDuration()
        {
            var w = World();
            int limit = w.Config.Combat.Body.TapHoldTicks + w.Config.Combat.Charge.FullChargeTicks + w.Config.Combat.Charge.FullChargeHoldTicks;
            w.Step(Input(InputButtons.Charge, 16), default);
            for (int t = 1; t < limit; t++)
            {
                w.Step(Input(InputButtons.Charge, 16), default);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action, $"충전 {t}틱");
            }
            w.Step(Input(InputButtons.Charge, 16), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            Assert.AreEqual(1, Count(w, SimEventType.LungeStart));
        }

        [TestCase(4)]
        [TestCase(12)]
        public void ChargeWinsSimultaneousAttack_AndIgnoredClicksNeverBecomeDelayedPunch(int releaseTick)
        {
            var w = World();
            int punches = 0;
            w.Step(Input(InputButtons.Charge | InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            punches += Count(w, SimEventType.AttackStart);
            for (int t = 1; t < releaseTick; t++)
            {
                var buttons = InputButtons.Charge | (t % 2 == 0 ? InputButtons.Attack : InputButtons.None);
                w.Step(Input(buttons), default);
                punches += Count(w, SimEventType.AttackStart);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            }
            // 차징을 뗄 때 새 좌클릭이 함께 들어와도 기존 차징 동작만 끝낸다.
            for (int t = 0; t < 35; t++)
            {
                w.Step(Input(InputButtons.Attack), default);
                punches += Count(w, SimEventType.AttackStart);
            }
            Assert.AreEqual(0, punches, "취소/박치기 뒤 무시했던 좌클릭이 예약 펀치가 되지 않는다");
            Assert.AreEqual(0, w.State.Fighter0.AttackBuffer);
        }

        static SimWorld ResumeFrozenPunch(InputButtons pressedDuringFreeze)
        {
            var w = World();
            w.Step(Input(InputButtons.Attack), default);
            w.Step(Input(InputButtons.None), default);
            // 후딜이 3틱 남은 상태: 정상 좌클릭이면 6틱 버퍼가 다음 펀치까지 살아 있어야 한다.
            w.State.Fighter0.ActionTick = w.Config.GetAttack(AttackIds.Punch).TotalTicks - 3;
            w.State.Fighter0.HitstopTicks = 2;
            w.Step(Input(pressedDuringFreeze), default);
            Assert.AreEqual(pressedDuringFreeze, w.State.Fighter0.PendingPresses);
            // Shift는 히트스탑이 끝나기 전에 떼고 좌클릭만 유지한다.
            w.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(0, w.State.Fighter0.HitstopTicks);
            w.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action);
            return w;
        }

        [Test]
        public void ShiftAndClickDuringHitstop_DoNotQueuePunchAfterShiftWasReleased()
        {
            var w = ResumeFrozenPunch(InputButtons.Charge | InputButtons.Attack);
            Assert.AreEqual(0, w.State.Fighter0.AttackBuffer, "히트스탑에 보관된 Shift도 클릭 우선순위에 반영");
            int punches = Count(w, SimEventType.AttackStart);
            for (int t = 0; t < 8; t++)
            {
                w.Step(Input(InputButtons.Attack), default);
                punches += Count(w, SimEventType.AttackStart);
            }
            Assert.AreEqual(0, punches, "무시된 클릭이 후딜 뒤 예약 펀치가 되지 않는다");
        }

        [Test]
        public void ClickAloneDuringHitstop_StillQueuesOnePunchAfterRecovery()
        {
            var w = ResumeFrozenPunch(InputButtons.Attack);
            Assert.Greater(w.State.Fighter0.AttackBuffer, 0, "Shift 없는 정상 펀치 버퍼는 유지");
            int punches = Count(w, SimEventType.AttackStart);
            for (int t = 0; t < 8; t++)
            {
                w.Step(Input(InputButtons.Attack), default);
                punches += Count(w, SimEventType.AttackStart);
            }
            Assert.AreEqual(1, punches);
        }

        [Test]
        public void GrabDuringCharge_CancelsChargeAndStartsGrab()
        {
            var w = World();
            w.Step(Input(InputButtons.Charge), default);
            w.Step(Input(InputButtons.Charge | InputButtons.Grab, 8), default);
            Assert.AreEqual(ActionState.Grab, w.State.Fighter0.Action);
            Assert.AreEqual(0, Count(w, SimEventType.LungeStart));
            Assert.AreEqual(0, Count(w, SimEventType.AttackStart));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HoldingOpponent_IgnoresCharge_ButAttackWithChargeStillThrows(bool combined)
        {
            var w = combined ? CombinedWorld() : World(x0: -2f, x1: -0.9f);
            w.State.Fighter0.Position.X = -2f;
            w.State.Fighter1.Position.X = -0.9f;
            for (int t = 0; t < 24 && w.State.Fighter0.Action != ActionState.Holding; t++)
                w.Step(Input(InputButtons.Grab), default);
            Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action);
            w.Step(Input(InputButtons.Charge), default);
            Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action);
            // 잡기 히트스탑이 남아 있다면 끝난 뒤 보관된 새 클릭으로 던진다.
            for (int t = 0; t < 12 && w.State.Fighter1.Action != ActionState.Thrown; t++)
                w.Step(Input(InputButtons.Attack | InputButtons.Charge, 8), default);
            Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OldDashBit_IsBlockedOnlyForSeparateChargeRules(bool separate)
        {
            var w = World(separate);
            w.Step(new InputFrame(1, 0, InputButtons.Dash), default);
            if (separate)
                Assert.AreNotEqual(ActionState.Dash, w.State.Fighter0.Action);
            else
                Assert.AreEqual(ActionState.Dash, w.State.Fighter0.Action, "구 입력 규칙의 대시는 보존");
        }

        [Test]
        public void LegacyBodyRules_KeepAttackTapAndHoldBehavior()
        {
            var tap = World(false);
            tap.Step(Input(InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Charge, tap.State.Fighter0.Action);
            tap.Step(Input(InputButtons.None), default);
            Assert.AreEqual(ActionState.Attack, tap.State.Fighter0.Action);
            var hold = World(false);
            for (int t = 0; t <= hold.Config.Combat.Body.TapHoldTicks; t++) hold.Step(Input(InputButtons.Attack, 16), default);
            hold.Step(Input(InputButtons.None, 16), default);
            Assert.AreEqual(ActionState.Lunge, hold.State.Fighter0.Action);
            Assert.AreNotEqual(Config(false).ComputeChecksum(), Config(true).ComputeChecksum(), "새 조작과 구 기록 설정을 구분");
        }

        [Test]
        public void CurrentMouseSource_IgnoresShift_AndKeepsLeftClickAttack()
        {
            var previousKeyboard = Keyboard.current;
            var previousMouse = Mouse.current;
            Keyboard keyboard = null;
            Mouse mouse = null;
            try
            {
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                InputSystem.Update();
                keyboard.MakeCurrent();
                mouse.MakeCurrent();
                var w = World();
                var input = new MouseAimInputSource(() => w, 0).Poll(0);
                Assert.IsFalse(input.Held(InputButtons.Charge));
                Assert.IsTrue(input.Held(InputButtons.Attack));
                Assert.IsFalse(input.Held(InputButtons.Dash));
            }
            finally
            {
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            }
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void AllBrawlBots_UseTheConfiguredChargeButton(bool separate, bool combined)
        {
            var w = combined ? CombinedWorld() : World(separate, -2f, 4f);
            w.State.Fighter0.Position.X = -2f;
            w.State.Fighter1.Position.X = 4f;
            var expected = separate ? InputButtons.Charge : InputButtons.Attack;
            var other = separate ? InputButtons.Attack : InputButtons.Charge;
            var bots = new IInputSource[]
            {
                new BrawlBot(() => w, 0, 1u),
                new LungePracticeBot(() => w, 0),
                new BrawlChaserBot(() => w, 0, 0),
            };
            foreach (var bot in bots)
            {
                bool charged = false;
                for (int t = 0; t < LungePracticeBot.Interval + 30; t++)
                {
                    var frame = bot.Poll(t);
                    Assert.IsFalse(frame.Held(other), bot.GetType().Name);
                    if (!frame.Held(expected)) continue;
                    Assert.IsTrue(bot.Poll(t + 1).Held(expected), $"{bot.GetType().Name} 충전 유지");
                    charged = true;
                    break;
                }
                Assert.IsTrue(charged, $"{bot.GetType().Name} 박치기 입력 발생");
            }
        }

        [Test]
        public void ChargeBit_RoundTripsAndParticipatesInStateChecksums()
        {
            var input = Input(InputButtons.Charge | InputButtons.Attack, 24);
            var bytes = new byte[InputFrame.Size];
            input.Write(bytes, 0);
            Assert.AreEqual(input, InputFrame.Read(bytes, 0));
            var state = World().State;
            uint baseline = state.ComputeChecksum();
            state.Fighter0.PrevButtons = InputButtons.Charge;
            Assert.AreNotEqual(baseline, state.ComputeChecksum());
            state.Fighter0.PrevButtons = InputButtons.None;
            state.Fighter0.PendingPresses = InputButtons.Charge;
            Assert.AreNotEqual(baseline, state.ComputeChecksum());
        }

        [Test]
        public void ChargeSequence_IsDeterministic_RestoresDuringCharge_SyncsAndReplays()
        {
            const int ticks = 180, split = 45;
            var config = Config();
            float y = Arena.SpawnFoot0.Y + config.HalfBody;
            var initial = SimState.CreateRound(config, Arena, 17u, new Vec2(-8f, y), new Vec2(8f, y), MatchPhase.Fighting);
            var w = new SimWorld(config, Arena, initial);
            var twin = new SimWorld(config, Arena, initial);
            var synced = new SimWorld(config, Arena, initial);
            var sync = new SyncTest<SimWorld>(synced, 7);
            var recording = InputRecording.Begin(config, new[] { Arena }, initial.Match.Rotation, 17u,
                initial.Fighter0.Position, initial.Fighter1.Position, MatchPhase.Fighting, config.Rules.Format());
            SimState snapshot = default;
            for (int t = 0; t < ticks; t++)
            {
                var p1 = Sequence(t, 16);
                var p2 = Sequence(t, 16);
                recording.Append(p1, p2);
                w.Step(p1, p2);
                twin.Step(p1, p2);
                Assert.AreEqual(w.ComputeChecksum(), twin.ComputeChecksum(), $"틱 {t + 1}");
                Assert.IsTrue(sync.Advance(p1, p2), sync.LastMismatch?.ToString());
                if (t + 1 == split) snapshot = w.State;
            }
            Assert.AreEqual(ActionState.Charge, snapshot.Fighter0.Action, "충전 중 상태를 저장");
            uint expected = w.ComputeChecksum();
            w.State = snapshot;
            for (int t = split; t < ticks; t++) w.Step(Sequence(t, 16), Sequence(t, 16));
            Assert.AreEqual(expected, w.ComputeChecksum(), "충전 상태 복원 후 재진행");
            Assert.AreEqual(0, sync.MismatchCount);
            recording.Finish(expected);
            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.AreEqual(InputButtons.Charge, loaded.GetP1(0).Buttons);
            Assert.AreEqual(expected, loaded.Replay(config, Arena));
        }

        static SimWorld CombinedWorld(BrawlWeaponKind kind = BrawlWeaponKind.None, int owner = 0, int surfaceMode = 0,
            bool revisedBalance = false)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, surfaceMode, false, true,
                combinedAttackInput: true, revisedBalance: revisedBalance);
            var config = new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl);
            float y = Arena.SpawnFoot0.Y + config.HalfBody;
            var w = TestWorlds.CreateAt(new Vec2(-8f, y), new Vec2(8f, y), config, Arena);
            if (kind != BrawlWeaponKind.None)
            {
                w.State.Weapon.Kind = kind;
                w.State.Weapon.Phase = BrawlWeaponPhase.Held;
                w.State.Weapon.Owner = owner;
                w.State.Weapon.Position = w.State.GetFighter(owner).Position;
                w.State.Weapon.Ammo = 6;
            }
            return w;
        }

        [TestCase(BrawlWeaponKind.None, 0, 1)]
        [TestCase(BrawlWeaponKind.None, 1, 7)]
        [TestCase(BrawlWeaponKind.Pan, 0, 1)]
        [TestCase(BrawlWeaponKind.Pan, 1, 7)]
        [TestCase(BrawlWeaponKind.Pistol, 0, 1)]
        [TestCase(BrawlWeaponKind.Pistol, 1, 7)]
        public void CombinedTap_UsesPunchOrHeldWeaponOnlyOnRelease(BrawlWeaponKind kind, int slot, int heldTicks)
        {
            var w = CombinedWorld(kind, slot);
            for (int i = 0; i < heldTicks; i++)
            {
                Step(w, slot, Input(InputButtons.Attack));
                Assert.AreEqual(ActionState.Charge, w.State.GetFighter(slot).Action);
                Assert.AreEqual(0, Count(w, SimEventType.AttackStart, slot));
                if (kind == BrawlWeaponKind.Pistol) Assert.AreEqual(6, w.State.Weapon.Ammo);
            }
            Step(w, slot, Input(InputButtons.None, 16));
            var f = w.State.GetFighter(slot);
            Assert.AreEqual(ActionState.Attack, f.Action);
            int expected = kind == BrawlWeaponKind.Pan ? AttackIds.Pan : kind == BrawlWeaponKind.Pistol ? AttackIds.Pistol : AttackIds.Punch;
            Assert.AreEqual(expected, f.AttackId);
            Assert.AreEqual(AimDirections.Direction(16), f.AttackDir);
            Assert.AreEqual(1, Count(w, SimEventType.AttackStart, slot));
            if (kind == BrawlWeaponKind.Pistol)
            {
                Assert.AreEqual(5, w.State.Weapon.Ammo);
                Assert.AreEqual(1, Count(w, SimEventType.BrawlWeaponShot, slot));
            }
        }

        [TestCase(BrawlWeaponKind.None, 0, false)]
        [TestCase(BrawlWeaponKind.Pan, 1, false)]
        [TestCase(BrawlWeaponKind.Pistol, 0, false)]
        [TestCase(BrawlWeaponKind.None, 1, true)]
        [TestCase(BrawlWeaponKind.Pan, 0, true)]
        [TestCase(BrawlWeaponKind.Pistol, 1, true)]
        public void CombinedHold_LaunchesOnReleaseOrMaximumWithoutUsingWeapon(BrawlWeaponKind kind, int slot, bool automatic)
        {
            var w = CombinedWorld(kind, slot);
            int duration = automatic ? 56 : 8;
            for (int t = 0; t < duration; t++)
            {
                Step(w, slot, Input(InputButtons.Attack, 0));
                Assert.AreEqual(ActionState.Charge, w.State.GetFighter(slot).Action);
                Assert.AreEqual(0, Count(w, SimEventType.AttackStart, slot));
            }
            Step(w, slot, Input(automatic ? InputButtons.Attack : InputButtons.None, 16));
            Assert.AreEqual(ActionState.Lunge, w.State.GetFighter(slot).Action);
            Assert.AreEqual(1, Count(w, SimEventType.LungeStart, slot));
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Type == SimEventType.LungeStart && w.Events[i].Fighter == slot)
                    Assert.AreEqual(AimDirections.Direction(16), w.Events[i].Direction);
            if (kind != BrawlWeaponKind.None)
            {
                Assert.AreEqual(slot, w.State.Weapon.Owner);
                Assert.AreEqual(6, w.State.Weapon.Ammo);
            }
        }

        [TestCase(BrawlWeaponKind.None, 0)]
        [TestCase(BrawlWeaponKind.None, 1)]
        [TestCase(BrawlWeaponKind.Pan, 0)]
        [TestCase(BrawlWeaponKind.Pan, 1)]
        [TestCase(BrawlWeaponKind.Pistol, 0)]
        [TestCase(BrawlWeaponKind.Pistol, 1)]
        public void CombinedTapBufferedNearRecoveryEnd_FiresExactlyOnce(BrawlWeaponKind kind, int slot)
        {
            var w = CombinedWorld(kind, slot);
            Step(w, slot, Input(InputButtons.Attack));
            Step(w, slot, Input(InputButtons.None));
            var f = w.State.GetFighter(slot);
            int total = w.Config.GetAttack(f.AttackId).TotalTicks;
            while (w.State.GetFighter(slot).ActionTick < total - 3)
                Step(w, slot, Input(InputButtons.None));
            Step(w, slot, Input(InputButtons.Attack));
            int starts = Count(w, SimEventType.AttackStart, slot);
            for (int i = 0; i < 60; i++)
            {
                Step(w, slot, Input(InputButtons.None));
                starts += Count(w, SimEventType.AttackStart, slot);
            }
            Assert.AreEqual(1, starts, "후딜 중 탭을 놓아도 저장된 공격은 한 번만 발동");
            if (kind == BrawlWeaponKind.Pistol) Assert.AreEqual(4, w.State.Weapon.Ammo);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void CombinedControls_IgnoreOldDashAndChargeBits(int surfaceMode)
        {
            var w = CombinedWorld(surfaceMode: surfaceMode);
            for (int t = 0; t < 65; t++)
            {
                w.Step(Input(InputButtons.Dash | InputButtons.Charge), default);
                Assert.AreNotEqual(ActionState.Dash, w.State.Fighter0.Action);
                Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action);
                Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action);
            }
        }

        [Test]
        public void CombinedTapDuringHitstop_IsConsumedOnceAfterResume()
        {
            var w = CombinedWorld(BrawlWeaponKind.Pistol);
            w.State.Fighter0.HitstopTicks = 2;
            w.Step(Input(InputButtons.Attack), default);
            w.Step(default, default);
            int shots = 0;
            for (int i = 0; i < 40; i++)
            {
                w.Step(Input(InputButtons.None), default);
                shots += Count(w, SimEventType.BrawlWeaponShot);
            }
            Assert.AreEqual(1, shots);
            Assert.AreEqual(5, w.State.Weapon.Ammo);
        }

        [TestCase(BrawlWeaponKind.None, 0)]
        [TestCase(BrawlWeaponKind.None, 1)]
        [TestCase(BrawlWeaponKind.Pan, 0)]
        [TestCase(BrawlWeaponKind.Pan, 1)]
        [TestCase(BrawlWeaponKind.Pistol, 0)]
        [TestCase(BrawlWeaponKind.Pistol, 1)]
        public void MinimumCharge_EveryReleaseFromEightThroughNineteenCancels(BrawlWeaponKind kind, int slot)
        {
            for (int release = 8; release < 20; release++)
            {
                var w = CombinedWorld(kind, slot, revisedBalance: true);
                for (int t = 0; t < release; t++) Step(w, slot, Input(InputButtons.Attack));
                Assert.IsFalse(FighterCombat.IsLungeReady(w.State.GetFighter(slot), w.Config));
                Step(w, slot, Input(InputButtons.None));
                Assert.IsTrue(w.State.GetFighter(slot).InMovementState, $"release {release}");
                Assert.AreEqual(0, Count(w, SimEventType.LungeStart, slot));
                Assert.AreEqual(0, Count(w, SimEventType.AttackStart, slot));
                Assert.AreEqual(0, Count(w, SimEventType.BrawlWeaponShot, slot));
                if (kind != BrawlWeaponKind.None) Assert.AreEqual(6, w.State.Weapon.Ammo);
            }
        }

        [TestCase(0, 20, false)]
        [TestCase(1, 20, false)]
        [TestCase(0, 56, true)]
        [TestCase(1, 56, true)]
        public void MinimumCharge_ExactBoundaryAndAutomaticLaunchKeepReleaseAim(int slot, int duration, bool automatic)
        {
            var w = CombinedWorld(BrawlWeaponKind.Pistol, slot, revisedBalance: true);
            for (int t = 0; t < duration; t++)
            {
                Step(w, slot, Input(InputButtons.Attack));
                Assert.AreEqual(t >= 20, FighterCombat.IsLungeReady(w.State.GetFighter(slot), w.Config));
            }
            Step(w, slot, Input(automatic ? InputButtons.Attack : InputButtons.None, 16));
            Assert.AreEqual(1, Count(w, SimEventType.LungeStart, slot));
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Type == SimEventType.LungeStart)
                    Assert.AreEqual(AimDirections.Direction(16), w.Events[i].Direction);
            Assert.AreEqual(6, w.State.Weapon.Ammo);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void MinimumCharge_ShortChargeSpamCannotLungeOrBecomeWeaponAttacks(int slot)
        {
            var w = CombinedWorld(BrawlWeaponKind.Pistol, slot, revisedBalance: true);
            var start = w.State.GetFighter(slot).Position;
            int lunges = 0, attacks = 0, shots = 0;
            for (int repeat = 0; repeat < 36; repeat++)
            {
                int hold = 8 + repeat % 12;
                for (int t = 0; t <= hold; t++)
                {
                    Step(w, slot, Input(t < hold ? InputButtons.Attack : InputButtons.None));
                    lunges += Count(w, SimEventType.LungeStart, slot);
                    attacks += Count(w, SimEventType.AttackStart, slot);
                    shots += Count(w, SimEventType.BrawlWeaponShot, slot);
                }
            }
            Assert.AreEqual(0, lunges);
            Assert.AreEqual(0, attacks);
            Assert.AreEqual(0, shots);
            Assert.AreEqual(start.X, w.State.GetFighter(slot).Position.X);
            Assert.AreEqual(6, w.State.Weapon.Ammo);
        }

        [TestCase(BrawlWeaponKind.None)]
        [TestCase(BrawlWeaponKind.Pan)]
        [TestCase(BrawlWeaponKind.Pistol)]
        public void MinimumCharge_SevenTickTapStillUsesTheExpectedAttack(BrawlWeaponKind kind)
        {
            var w = CombinedWorld(kind, revisedBalance: true);
            for (int t = 0; t < 7; t++) w.Step(Input(InputButtons.Attack), default);
            w.Step(Input(InputButtons.None), default);
            int attack = kind == BrawlWeaponKind.None ? AttackIds.Punch : kind == BrawlWeaponKind.Pan ? AttackIds.Pan : AttackIds.Pistol;
            Assert.AreEqual(attack, w.State.Fighter0.AttackId);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action);
        }

        [Test]
        public void PracticeBot_ActuallyReachesTheNewMinimumAndLaunches()
        {
            var w = CombinedWorld(revisedBalance: true);
            var bot = new LungePracticeBot(() => w, 0);
            int launches = 0;
            for (int t = 0; t < 150; t++)
            {
                w.Step(bot.Poll(t), default);
                launches += Count(w, SimEventType.LungeStart);
            }
            Assert.Greater(launches, 0, "연습 봇의 짧은 차징도 새 최소값을 충족해야 한다");
        }

        static InputFrame Sequence(int tick, int aim)
        {
            var buttons = tick < 5 || tick >= 30 && tick < 50 || tick >= 90 && tick < 150
                ? InputButtons.Charge : InputButtons.None;
            if (tick == 10 || tick == 94) buttons |= InputButtons.Attack;
            if (tick == 70) buttons |= InputButtons.Grab;
            return Input(buttons, aim);
        }
    }
}
