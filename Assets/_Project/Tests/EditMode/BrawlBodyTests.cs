using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 단계 B: 조준(64방향), 몸 폼의 탭/홀드(5.1), 주먹·박치기 판정, 몸 폼 시나리오의 안전 검사(3.3), 단순 봇 스모크.
    /// 기본 배치: Brawl 맵 바닥, P1(-2)은 오른쪽 P2를 바라본다. 행동 틱 = 누른 뒤 지난 틱(누른 틱 0).
    /// </summary>
    public class BrawlBodyTests
    {
        // 좌클릭 탭/홀드의 기존 전이와 구기록 재생 계약을 유지한다. 새 Shift 차징은 BrawlChargeInputTests에서 검사한다.
        static SimConfig Config() =>
            new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default, separateChargeInput: false), RuleSet.Brawl);

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimWorld World(SimConfig config, float x0 = -2f, float x1 = 4f)
        {
            float y = Arena.SpawnFoot0.Y + config.HalfBody;
            return TestWorlds.CreateAt(new Vec2(x0, y), new Vec2(x1, y), config, Arena);
        }

        static InputFrame Press(int aimIndex) => new InputFrame(0, 0, InputButtons.Attack, AimDirections.Encode(aimIndex));
        static InputFrame Release(int aimIndex) => new InputFrame(0, 0, InputButtons.None, AimDirections.Encode(aimIndex));

        // 조준 ---------------------------------------------------------------

        [Test]
        public void AimTable_UnitLengthAndExactlySymmetric()
        {
            void Is(int k, float x, float y)
            {
                var d = AimDirections.Direction(k);
                Assert.AreEqual(x, d.X, $"{k}.X");
                Assert.AreEqual(y, d.Y, $"{k}.Y");
            }
            Is(0, 1f, 0f);
            Is(16, 0f, 1f);
            Is(32, -1f, 0f);
            Is(48, 0f, -1f);
            for (int k = 0; k < AimDirections.Count; k++)
            {
                var d = AimDirections.Direction(k);
                Assert.AreEqual(1f, SimMath.Length(d), 1e-5f, $"{k}");
                var mirrored = AimDirections.Direction(AimDirections.Index(AimDirections.Mirror(AimDirections.Encode(k))));
                Assert.AreEqual(-d.X, mirrored.X, $"좌우 {k}");
                Assert.AreEqual(d.Y, mirrored.Y, $"좌우 {k}");
                var flipped = AimDirections.Direction(AimDirections.Count - k);
                Assert.AreEqual(d.X, flipped.X, $"상하 {k}");
                Assert.AreEqual(-d.Y, flipped.Y, $"상하 {k}");
            }
        }

        [TestCase(1f, 0f, 0)]
        [TestCase(1f, 1f, 8)]
        [TestCase(0f, 1f, 16)]
        [TestCase(-1f, 1f, 24)]
        [TestCase(-1f, 0f, 32)]
        [TestCase(0f, -1f, 48)]
        [TestCase(0.866f, 0.5f, 5)]
        [TestCase(-0.866f, 0.5f, 27)]
        public void AimQuantize_RepresentativeAngles(float x, float y, int expected)
        {
            Assert.AreEqual(expected, AimDirections.Index(AimInput.Quantize(new Vector2(x, y))));
        }

        [Test]
        public void AimQuantize_MirrorsLeftRight()
        {
            // 경계(반 칸)에 걸리지 않는 각도들: 좌우로 뒤집은 방향은 뒤집은 번호가 된다.
            for (int i = 0; i < 360; i++)
            {
                float angle = (i + 0.37f) * Mathf.Deg2Rad;
                var v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                byte aim = AimInput.Quantize(v);
                Assert.AreEqual(AimDirections.Mirror(aim), AimInput.Quantize(new Vector2(-v.x, v.y)), $"{i}°");
            }
            Assert.AreEqual(0, AimInput.Quantize(Vector2.zero));
        }

        // 5.1 탭/홀드 -----------------------------------------------------------

        [Test]
        public void Press_StartsWindupImmediately()
        {
            var w = World(Config());
            w.Step(Press(0), default);
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            Assert.AreEqual(0, w.State.Fighter0.ActionTick);
            Assert.IsFalse(FighterCombat.IsLungeReady(w.State.Fighter0, w.Config));
        }

        [Test]
        public void QuickTap_PunchLandsAtWindupEnd()
        {
            var w = World(Config());
            int startup = w.Config.Combat.Body.Punch.StartupTicks;
            w.Step(Press(0), default);
            w.Step(Release(0), default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action);
            Assert.AreEqual(AttackIds.Punch, w.State.Fighter0.AttackId);
            for (int t = 2; t < startup; t++)
            {
                Assert.IsFalse(FighterCombat.IsHitboxActive(w.State.Fighter0, w.Config), $"누른 뒤 {t - 1}틱");
                w.Step(Release(0), default);
            }
            Assert.IsFalse(FighterCombat.IsHitboxActive(w.State.Fighter0, w.Config));
            w.Step(Release(0), default);
            Assert.IsTrue(FighterCombat.IsHitboxActive(w.State.Fighter0, w.Config), "선딜 끝(누른 뒤 3틱)에 타격");
        }

        [Test]
        public void LateTap_PunchLandsOnReleaseTick()
        {
            var w = World(Config());
            int t = w.Config.Combat.Body.TapHoldTicks;
            w.Step(Press(0), default);
            for (int i = 1; i < t - 2; i++)
                w.Step(Press(0), default);
            w.Step(Release(0), default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action, "T 전에 떼면 주먹");
            Assert.IsTrue(FighterCombat.IsHitboxActive(w.State.Fighter0, w.Config), "선딜이 이미 끝났으면 뗀 틱에 타격");
        }

        [Test]
        public void HoldToThreshold_ReadyThenReleaseLungesTowardReleaseAim()
        {
            var config = Config();
            var w = World(config);
            int t = config.Combat.Body.TapHoldTicks;
            w.Step(Press(0), default);
            for (int i = 1; i <= t; i++)
            {
                Assert.IsFalse(FighterCombat.IsLungeReady(w.State.Fighter0, config), $"{i - 1}");
                w.Step(Press(0), default);
            }
            Assert.IsTrue(FighterCombat.IsLungeReady(w.State.Fighter0, config), "T에 준비 완료");
            Assert.AreEqual(0, FighterCombat.ChargeTicks(w.State.Fighter0, config), "충전은 T부터");

            // 누르는 동안 오른쪽을 겨눴다가 왼쪽 위(24)로 바꿔 떼면 뗀 틱의 조준으로 발사한다.
            w.Step(Release(24), default);
            var f = w.State.Fighter0;
            Assert.AreEqual(ActionState.Lunge, f.Action);
            Assert.AreEqual(-1, f.Facing);
            var expected = AimDirections.Direction(24);
            var dir = SimMath.Normalize(f.Velocity);
            Assert.AreEqual(expected.X, dir.X, 0.05f);
            Assert.AreEqual(expected.Y, dir.Y, 0.05f);
            Assert.AreEqual(config.Combat.Charge.LungeMinSpeed, SimMath.Length(f.Velocity), 2f, "충전 거의 없음 → 최소 속도 근처");
        }

        [Test]
        public void MaxCharge_AutoFires()
        {
            var config = Config();
            var w = World(config);
            int limit = config.Combat.Body.TapHoldTicks + config.Combat.Charge.FullChargeTicks + config.Combat.Charge.FullChargeHoldTicks;
            w.Step(Press(8), default);
            for (int i = 1; i < limit; i++)
            {
                w.Step(Press(8), default);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action, $"{i}");
            }
            w.Step(Press(8), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "최대 충전 유지 뒤 자동 발사");
        }

        /// <summary>탭 주먹 후딜 중 pressAt(주먹 행동 틱)에 다시 눌렀다 떼고, 주먹이 끝나는 틱의 행동을 돌려준다.</summary>
        static ActionState PressDuringRecovery(int pressAt)
        {
            var config = Config();
            var w = World(config, -2f, 10f);
            int total = config.Combat.Body.Punch.TotalTicks;
            w.Step(Press(0), default);
            w.Step(Release(0), default); // 주먹 행동 틱 1
            for (int tick = 2; tick < total; tick++)
                w.Step(tick == pressAt ? Press(0) : Release(0), default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action);
            w.Step(Release(0), default);
            return w.State.Fighter0.Action;
        }

        [Test]
        public void PressDuringPunchRecovery_BufferedWithinWindow()
        {
            int total = Config().Combat.Body.Punch.TotalTicks;
            int window = Config().Combat.Body.PunchBufferTicks;
            Assert.AreEqual(ActionState.Charge, PressDuringRecovery(total - window + 2), "끝나기 직전 누름은 저장했다가 시작");
            Assert.AreNotEqual(ActionState.Charge, PressDuringRecovery(total - window - 3), "저장 시간보다 일찍 누르면 사라짐");
        }

        // 판정 ------------------------------------------------------------------

        static (int damage, Vec2 velocity) PunchAt(float x1, int aimIndex)
        {
            var config = Config();
            var w = World(config, -2f, x1);
            w.Step(Press(aimIndex), default);
            for (int i = 0; i < 10; i++)
                w.Step(Release(aimIndex), default);
            return (config.Combat.MaxHealth - w.State.Fighter1.Health, w.State.Fighter1.Velocity);
        }

        [Test]
        public void Punch_HitsInAimDirectionOnly_MirrorSymmetric()
        {
            var (damage, velocity) = PunchAt(-0.9f, 0);
            Assert.AreEqual(Config().Combat.Body.Punch.Damage, damage, "오른쪽 조준이 오른쪽 상대를 맞힘");
            Assert.Greater(velocity.X, 0f);
            Assert.Greater(velocity.Y, 0f, "넉백은 조준 방향에서 위로 섞인다");

            var (missed, _) = PunchAt(-0.9f, 32);
            Assert.AreEqual(0, missed, "반대쪽 조준은 빗나감");

            var (leftDamage, leftVelocity) = PunchAt(-3.1f, 32);
            Assert.AreEqual(damage, leftDamage);
            Assert.AreEqual(-velocity.X, leftVelocity.X, 1e-4f, "좌우 대칭 넉백");
            Assert.AreEqual(velocity.Y, leftVelocity.Y, 1e-4f);
        }

        [Test]
        public void Lunge_HitsStandingOpponent()
        {
            var config = Config();
            var w = World(config, -4f, 0f);
            int hold = config.Combat.Body.TapHoldTicks + 20;
            w.Step(Press(0), default);
            for (int i = 1; i < hold; i++)
                w.Step(Press(0), default);
            w.Step(Release(0), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            for (int i = 0; i < 30; i++)
                w.Step(default, default);
            Assert.Less(w.State.Fighter1.Health, config.Combat.MaxHealth, "박치기가 서 있는 상대를 맞힘");
            Assert.Greater(w.State.Fighter1.Velocity.X, -1f);
        }

        // 3.3 안전 검사(몸 폼 + 조준이 들어간 무작위 입력) ----------------------------

        static void AimedInputs(uint seed, int ticks, out InputFrame[] p1, out InputFrame[] p2)
        {
            TestWorlds.RandomInputs(seed, ticks, out p1, out p2);
            var rng = new SimRng(seed ^ 0xA1Bu);
            int aim0 = 0, aim1 = 32;
            for (int t = 0; t < ticks; t++)
            {
                if (rng.Range(0, 8) == 0)
                    aim0 = rng.Range(0, AimDirections.Count);
                if (rng.Range(0, 8) == 0)
                    aim1 = rng.Range(0, AimDirections.Count);
                p1[t] = new InputFrame(p1[t].MoveX, p1[t].MoveY, p1[t].Buttons, AimDirections.Encode(aim0));
                p2[t] = new InputFrame(p2[t].MoveX, p2[t].MoveY, p2[t].Buttons, AimDirections.Encode(aim1));
            }
        }

        static SimWorld MatchWorld(SimConfig config, uint seed) =>
            new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, seed, MatchPhase.Countdown));

        [Test]
        public void BodyScenario_DeterministicSaveRestoreAndSyncTest()
        {
            var config = Config();
            AimedInputs(31, 1800, out var p1, out var p2);
            var a = MatchWorld(config, 5);
            var b = MatchWorld(config, 5);
            bool lunged = false, punched = false;
            for (int t = 0; t < p1.Length; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"틱 {t + 1}\n{StateDiff.Describe(a.State, b.State)}");
                lunged |= a.State.Fighter0.Action == ActionState.Lunge || a.State.Fighter1.Action == ActionState.Lunge;
                punched |= a.State.Fighter0.AttackId == AttackIds.Punch || a.State.Fighter1.AttackId == AttackIds.Punch;
            }
            Assert.IsTrue(lunged && punched, "시나리오에 주먹과 박치기가 모두 들어가야 함");

            // 저장 → 진행 → 복원 → 재진행.
            var w = MatchWorld(config, 5);
            for (int t = 0; t < 900; t++)
                w.Step(p1[t], p2[t]);
            var snapshot = w.State;
            for (int t = 900; t < 1800; t++)
                w.Step(p1[t], p2[t]);
            uint first = w.ComputeChecksum();
            w.State = snapshot;
            for (int t = 900; t < 1800; t++)
                w.Step(p1[t], p2[t]);
            Assert.AreEqual(first, w.ComputeChecksum(), "저장/복원 뒤 재진행");

            // SyncTest(되감기 7틱).
            var synced = MatchWorld(config, 5);
            var sync = new SyncTest<SimWorld>(synced, 7);
            for (int t = 0; t < p1.Length; t++)
                sync.Advance(p1[t], p2[t]);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(a.ComputeChecksum(), synced.ComputeChecksum());
        }

        [Test]
        public void LegacyBodyRecording_RoundTripKeepsAimAndChecksum()
        {
            var config = Config();
            var spawn0 = SimState.SpawnCenter(config, Arena, 0);
            var spawn1 = SimState.SpawnCenter(config, Arena, 1);
            var recording = InputRecording.Begin(config, Arena, 11, spawn0, spawn1, MatchPhase.Countdown, RuleSet.Brawl.Format());
            var world = new SimWorld(config, Arena, SimState.CreateRound(config, Arena, 11, spawn0, spawn1, MatchPhase.Countdown));
            AimedInputs(77, 1200, out var p1, out var p2);
            for (int t = 0; t < p1.Length; t++)
            {
                recording.Append(p1[t], p2[t]);
                world.Step(p1[t], p2[t]);
            }
            recording.Finish(world.ComputeChecksum());

            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.IsTrue(RuleSet.TryParse(loaded.Mode, out var rules));
            Assert.AreEqual(FighterForm.Body, rules.StartForm);
            for (int t = 0; t < p1.Length; t++)
            {
                Assert.AreEqual(p1[t], loaded.GetP1(t));
                Assert.AreEqual(p2[t], loaded.GetP2(t));
            }
            Assert.AreEqual(recording.FinalChecksum, loaded.Replay(config, Arena));
        }

        // 봇 -------------------------------------------------------------------

        [Test]
        public void BrawlBots_ThirtySeconds_NoInvariantBreaks()
        {
            var config = Config();
            SimWorld world = null;
            var bot0 = new BrawlBot(() => world, 0, 101u);
            var bot1 = new BrawlBot(() => world, 1, 202u);
            world = MatchWorld(config, 9);
            float frameX = Arena.HalfWidth - 1f, frameY = Arena.HalfHeight - 1f;
            int damage = 0, lunges = 0, punches = 0;
            for (int t = 0; t < 1800; t++)
            {
                int tick = world.State.Tick;
                var before = world.State.Fighter0.Health + world.State.Fighter1.Health;
                world.Step(bot0.Poll(tick), bot1.Poll(tick));
                var s = world.State;
                for (int k = 0; k < 2; k++)
                {
                    var f = s.GetFighter(k);
                    Assert.IsFalse(float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y), $"NaN 틱 {t}");
                    Assert.LessOrEqual(System.Math.Abs(f.Position.X), frameX, $"P{k + 1} 프레임 밖 틱 {t}");
                    Assert.LessOrEqual(System.Math.Abs(f.Position.Y), frameY, $"P{k + 1} 프레임 밖 틱 {t}");
                }
                for (int e = 0; e < world.Events.Count; e++)
                {
                    Assert.AreNotEqual(SimEventType.OutOfBounds, world.Events[e].Type, $"틱 {t}");
                    if (world.Events[e].Type == SimEventType.LungeStart)
                        lunges++;
                    if (world.Events[e].Type == SimEventType.AttackStart)
                        punches++;
                }
                Assert.AreEqual(0, world.Events.Dropped);
                int after = s.Fighter0.Health + s.Fighter1.Health;
                if (after < before)
                    damage += before - after;
            }
            TestContext.WriteLine($"[CR-004 B] 봇 30초: 주먹 {punches}, 박치기 {lunges}, 피해 합 {damage}, 라운드 {world.State.Match.Round}");
            Assert.Greater(punches, 0);
            Assert.Greater(lunges, 0);
            Assert.Greater(damage, 0);
        }
    }
}
