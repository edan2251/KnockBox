using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 단계 C: 그랩·잡은 상태·받아내기·던지기(5.2), 벽꽝 추가 피해 상한과 재그랩 보호, 그랩이 들어간 시나리오의 안전 검사(3.3), 봇 스모크.
    /// 기본 배치: Brawl 맵 바닥, P1(-2)이 오른쪽(-0.9)의 P2를 겨눈다. 우클릭 = Grab, 좌클릭 = Attack.
    /// </summary>
    public class BrawlGrabTests
    {
        static SimConfig Config(bool separateChargeInput = true) =>
            new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default, separateChargeInput: separateChargeInput), RuleSet.Brawl);

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static float FloorY(SimConfig config) => Arena.SpawnFoot0.Y + config.HalfBody;

        static SimWorld World(SimConfig config, float x0 = -2f, float x1 = -0.9f) =>
            TestWorlds.CreateAt(new Vec2(x0, FloorY(config)), new Vec2(x1, FloorY(config)), config, Arena);

        static InputFrame In(InputButtons buttons, int aimIndex) => new InputFrame(0, 0, buttons, AimDirections.Encode(aimIndex));

        /// <summary>buttons를 누른 채 조건이 될 때까지(최대 limit틱) 진행한다.</summary>
        static void StepUntil(SimWorld w, InputFrame p1, System.Func<SimState, bool> done, int limit = 60)
        {
            for (int i = 0; i < limit && !done(w.State); i++)
                w.Step(p1, default);
            Assert.IsTrue(done(w.State), "조건에 닿지 못함");
        }

        static void GrabAndHold(SimWorld w, InputButtons extra = InputButtons.None)
        {
            w.Step(In(InputButtons.Grab | extra, 0), default);
            Assert.AreEqual(ActionState.Grab, w.State.Fighter0.Action);
            StepUntil(w, In(InputButtons.Grab | extra, 0), s => s.Fighter0.Action == ActionState.Holding);
            Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action);
        }

        // 상태 전이 ------------------------------------------------------------------

        [Test]
        public void GrabHoldThrowThrown_TowardCursor()
        {
            var config = Config();
            var w = World(config);
            GrabAndHold(w);
            // 우클릭을 떼도 던지지 않는다(몸 폼은 좌클릭으로 던짐).
            for (int i = 0; i < 5; i++)
                w.Step(In(InputButtons.None, 0), default);
            Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action);

            w.Step(In(InputButtons.Attack, 8), default);
            Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action);
            var victim = w.State.Fighter1;
            Assert.AreEqual(ActionState.Thrown, victim.Action);
            Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Grab.ThrowDamage, victim.Health, "던지기 기본 피해(고정)");
            var dir = SimMath.Normalize(victim.Velocity);
            var aim = AimDirections.Direction(8);
            Assert.AreEqual(aim.X, dir.X, 0.05f);
            Assert.AreEqual(aim.Y, dir.Y, 0.05f);
        }

        [Test]
        public void Holding_ReleasesAfterMaxHoldWithoutThrow()
        {
            var config = Config();
            var w = World(config);
            GrabAndHold(w);
            int held = 0;
            while (w.State.Fighter0.Action == ActionState.Holding && held < 200)
            {
                w.Step(In(InputButtons.None, 16), default);
                held++;
            }
            var s = w.State;
            Assert.That(held, Is.InRange(config.Combat.Grab.MaxHoldTicks - 2, config.Combat.Grab.MaxHoldTicks + 2), "약 60틱 뒤 해제");
            Assert.AreNotEqual(ActionState.Held, s.Fighter1.Action);
            Assert.AreNotEqual(ActionState.Thrown, s.Fighter1.Action, "던지지 않고 놓아줌");
            Assert.AreEqual(config.Combat.MaxHealth, s.Fighter1.Health, "피해 없음");
            Assert.AreEqual(0f, s.Fighter1.Velocity.X, 1e-3f);
        }

        [Test]
        public void Holding_WalksSlowlyCarryingVictim()
        {
            var config = Config();
            var w = World(config);
            GrabAndHold(w);
            float x0 = w.State.Fighter0.Position.X;
            for (int i = 0; i < 30; i++)
                w.Step(new InputFrame(-1, 0, InputButtons.None, AimDirections.Encode(0)), default);
            var s = w.State;
            Assert.AreEqual(ActionState.Holding, s.Fighter0.Action);
            Assert.Less(s.Fighter0.Position.X, x0 - 0.3f, "느리게 걸음");
            Assert.LessOrEqual(SimMath.Abs(s.Fighter0.Velocity.X), config.Combat.Body.HoldMoveSpeed + 1e-3f);
            Assert.AreEqual(s.Fighter0.Position.X + config.Combat.Grab.HoldOffset * s.Fighter0.Facing, s.Fighter1.Position.X, 1e-3f, "잡힌 쪽이 따라옴");
        }

        [Test]
        public void Holding_CanJumpCarryingVictim()
        {
            var config = Config();
            var w = World(config);
            GrabAndHold(w);
            float y0 = w.State.Fighter0.Position.Y;
            w.Step(new InputFrame(0, 0, InputButtons.Jump, AimDirections.Encode(0)), default);
            float top = y0;
            for (int i = 0; i < 12; i++)
            {
                w.Step(new InputFrame(0, 0, InputButtons.Jump, AimDirections.Encode(0)), default);
                top = System.Math.Max(top, w.State.Fighter0.Position.Y);
                Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action);
                Assert.AreEqual(w.State.Fighter0.Position.Y, w.State.Fighter1.Position.Y, 1e-3f, "잡힌 쪽이 따라 올라감");
            }
            Assert.Greater(top, y0 + 0.8f, "잡은 채 점프");
        }

        [Test]
        public void Throw_FliesFarInNearlyStraightLine()
        {
            var config = Config();
            var w = World(config, -12f, -10.9f);
            GrabAndHold(w);
            w.Step(In(InputButtons.None, 0), default);
            w.Step(In(InputButtons.Attack, 0), default);
            Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);
            float y0 = w.State.Fighter1.Position.Y, x0 = w.State.Fighter1.Position.X, minY = y0;
            for (int i = 0; i < config.Combat.Grab.ThrownTicks; i++)
            {
                w.Step(default, default);
                minY = System.Math.Min(minY, w.State.Fighter1.Position.Y);
            }
            float distance = w.State.Fighter1.Position.X - x0;
            TestContext.WriteLine($"[CR-004 C] 수평 던지기 {config.Combat.Grab.ThrownTicks}틱: 거리 {distance:0.0}, 최대 하강 {y0 - minY:0.00}");
            Assert.Greater(distance, 18f, "거의 반대편까지");
            Assert.Less(y0 - minY, 1.5f, "거의 일직선");
        }

        // 5.2 우선순위 ----------------------------------------------------------------

        [TestCase(false)]
        [TestCase(true)]
        public void RightClickDuringWindupOrCharge_CancelsIntoGrab_AndHeldChargeButtonDoesNotThrow(bool separateChargeInput)
        {
            var config = Config(separateChargeInput);
            // 구기록의 좌클릭 홀드와 새 Shift 홀드 모두 잡기로 취소할 수 있다. 던지기는 좌클릭 새 눌림으로만 한다.
            var charge = separateChargeInput ? InputButtons.Charge : InputButtons.Attack;
            foreach (int chargeTicks in new[] { 2, config.Combat.Body.TapHoldTicks + 5 })
            {
                var w = World(config);
                for (int i = 0; i <= chargeTicks; i++)
                    w.Step(In(charge, 0), default);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
                // 충전 버튼을 누른 채 우클릭: 준비·충전을 취소하고 잡기.
                GrabAndHold(w, charge);
                for (int i = 0; i < 10; i++)
                    w.Step(In(charge, 0), default);
                Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action, "계속 누른 충전 버튼은 던지기를 새로 입력하지 않음");
                w.Step(In(InputButtons.None, 0), default);
                w.Step(In(InputButtons.Attack, 0), default);
                Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action, "뗐다가 다시 누르면 던짐");
            }
        }

        [Test]
        public void RightClickDuringPunchLungeOrGrab_IsIgnoredNotBuffered()
        {
            var config = Config();
            // 주먹 진행 중.
            var w = World(config, -2f, 10f);
            w.Step(In(InputButtons.Attack, 0), default);
            w.Step(In(InputButtons.None, 0), default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action);
            w.Step(In(InputButtons.Grab, 0), default);
            StepUntil(w, In(InputButtons.None, 0), s => s.Fighter0.Action != ActionState.Attack);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action, "주먹 중 우클릭은 저장하지 않음");

            // 박치기 진행 중.
            w = World(config, -2f, 10f);
            for (int i = 0; i <= config.Combat.Body.TapHoldTicks + 2; i++)
                w.Step(In(InputButtons.Charge, 0), default);
            w.Step(In(InputButtons.None, 0), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            w.Step(In(InputButtons.Grab, 0), default);
            StepUntil(w, In(InputButtons.None, 0), s => s.Fighter0.Action != ActionState.Lunge);
            w.Step(In(InputButtons.None, 0), default);
            Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action, "박치기 중 우클릭은 저장하지 않음");

            // 잡기 진행 중(헛잡기).
            w = World(config, -2f, 10f);
            w.Step(In(InputButtons.Grab, 0), default);
            w.Step(In(InputButtons.None, 0), default);
            w.Step(In(InputButtons.Grab, 0), default);
            Assert.AreEqual(ActionState.Grab, w.State.Fighter0.Action);
            Assert.AreEqual(2, w.State.Fighter0.ActionTick, "잡기 중 우클릭은 다시 시작하지 않음");
        }

        // 받아내기 -------------------------------------------------------------------

        /// <summary>
        /// P1은 다음 틱에 잡기 판정이 켜지는 잡기 상태(잡기 방향 grabDir, 0이면 바라보는 쪽), P2는 왼쪽으로 박치기 중(거리 gap). 한 틱 진행한 월드.
        /// </summary>
        static SimWorld CatchSetup(float gap, int p1Facing = 1, float lungeSpeed = 30f, float grabDirX = 0f)
        {
            var config = Config();
            var w = World(config, -2f, -2f + gap);
            var s = w.State;
            s.Fighter0.Facing = (sbyte)p1Facing;
            s.Fighter0.RestartAction(ActionState.Grab);
            s.Fighter0.ActionTick = config.Combat.Grab.StartupTicks - 1;
            s.Fighter0.AttackDir = new Vec2(grabDirX, 0f);
            s.Fighter1.Facing = -1;
            s.Fighter1.RestartAction(ActionState.Lunge);
            s.Fighter1.Velocity = new Vec2(-lungeSpeed, 0f);
            w.State = s;
            w.Step(In(InputButtons.Grab, p1Facing > 0 ? 0 : 32), default);
            return w;
        }

        [Test]
        public void Catch_GrabOverlappingLunge_SucceedsAndAutoThrowsBack_BeatsSameTickStrike()
        {
            var config = Config();
            var w = CatchSetup(1.3f);
            var s = w.State;
            bool caught = false;
            for (int e = 0; e < w.Events.Count; e++)
                caught |= w.Events[e].Type == SimEventType.LungeCaught;
            Assert.IsTrue(caught, "박치기를 받아냄");
            Assert.AreEqual(config.Combat.MaxHealth, s.Fighter0.Health, "같은 틱 박치기 타격 무효");
            // 받아내면 박치기 진행 방향(왼쪽) 그대로 등 뒤로 넘겨 내던진다. 두 파이터는 잠깐 멈춘다.
            Assert.AreEqual(ActionState.Thrown, s.Fighter1.Action);
            Assert.AreEqual(-1f, SimMath.Normalize(s.Fighter1.Velocity).X, 1e-3f, "박치기 진행 방향 그대로");
            Assert.Less(s.Fighter1.Position.X, s.Fighter0.Position.X, "잡은 쪽 등 뒤로 넘어감");
            Assert.AreEqual(1, s.Fighter0.Facing, "날아온 쪽을 바라봄");
            Assert.AreEqual(config.Combat.Grab.ThrowSpeed, SimMath.Length(s.Fighter1.Velocity), 1e-3f);
            Assert.AreEqual(config.Combat.Body.CatchHitstop, s.Fighter0.HitstopTicks);
            Assert.AreEqual(config.Combat.Body.CatchHitstop, s.Fighter1.HitstopTicks);
            Assert.AreEqual(Vec2.Zero, s.Fighter0.Velocity, "잡은 쪽 제자리");
        }

        [Test]
        public void Catch_BehindWithCursor_Succeeds()
        {
            // 왼쪽을 보고 있어도 커서(오른쪽) 방향 잡기면 등 뒤에서 오는 박치기를 받아낸다.
            var w = CatchSetup(1.3f, -1, 30f, 1f);
            Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);
            Assert.Less(w.State.Fighter1.Velocity.X, 0f, "진행 방향 그대로");
        }

        [Test]
        public void Catch_NoOverlap_Fails()
        {
            var w = CatchSetup(4f);
            Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action, "잡기 상자에 닿지 않음");
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter1.Action);

            // 잡기 방향(커서)이 반대쪽이면 받아내지 못하고 맞는다.
            var config = Config();
            var back = CatchSetup(1.3f, 1, 30f, -1f);
            Assert.AreNotEqual(ActionState.Thrown, back.State.Fighter1.Action);
            Assert.Less(back.State.Fighter0.Health, config.Combat.MaxHealth);
        }

        // 벽꽝 상한·재그랩 보호, 연속 잡기 최대 피해 --------------------------------------

        [Test]
        public void GrabSequenceIntoWall_DamageCapped_ThenRegrabProtected()
        {
            var config = Config();
            float wallFace = -(Arena.HalfWidth - 1f) + config.HalfBody;
            // P1이 왼쪽 벽 가까이의 P2를 잡아 벽 쪽(왼쪽)으로 최고 속도로 던진다.
            var w = World(config, wallFace + 3.2f, wallFace + 2.1f);
            var s0 = w.State;
            s0.Fighter0.Facing = -1;
            w.State = s0;
            w.Step(In(InputButtons.Grab, 32), default);
            StepUntil(w, In(InputButtons.Grab, 32), s => s.Fighter0.Action == ActionState.Holding);
            w.Step(In(InputButtons.None, 32), default);
            w.Step(In(InputButtons.Attack, 32), default);
            Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);

            StepUntil(w, In(InputButtons.None, 32), s => s.Fighter1.InMovementState, 240);
            int damage = config.Combat.MaxHealth - w.State.Fighter1.Health;
            int max = config.Combat.Grab.ThrowDamage + config.Combat.Body.SplatDamageMax;
            TestContext.WriteLine($"[CR-004 C] 연속 잡기 한 번의 피해 {damage} (상한 {max})");
            Assert.Greater(damage, config.Combat.Grab.ThrowDamage, "벽꽝 추가 피해가 들어감");
            Assert.LessOrEqual(damage, max, "던지기 + 벽꽝 상한");

            // 회복 직후: 재그랩 보호.
            var s1 = w.State;
            Assert.Greater(s1.Fighter1.GrabProtectTicks, 0);
            Assert.AreEqual(0, s1.Fighter1.SplatDamageTaken);
            s1.Fighter0.Position = new Vec2(s1.Fighter1.Position.X + 1.1f, s1.Fighter1.Position.Y);
            s1.Fighter0.Velocity = Vec2.Zero;
            s1.Fighter0.Facing = -1;
            s1.Fighter0.RestartAction(ActionState.Idle);
            s1.Fighter0.Grounded = s1.Fighter1.Grounded;
            w.State = s1;
            w.Step(In(InputButtons.Grab, 32), default);
            for (int i = 0; i < 12; i++)
                w.Step(In(InputButtons.None, 32), default);
            Assert.AreNotEqual(ActionState.Held, w.State.Fighter1.Action, "보호 중에는 잡히지 않음");
            Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action);
        }

        // 3.3 안전 검사(그랩이 들어간 시나리오)와 봇 스모크 ----------------------------------

        static void BotFrames(SimConfig config, uint seed, int ticks, out InputFrame[] p1, out InputFrame[] p2, out int holds, out int catches, out int throws)
        {
            SimWorld world = null;
            var bot = new BrawlBot(() => world, 0, seed);
            var practice = new LungePracticeBot(() => world, 1);
            world = new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, seed, MatchPhase.Countdown));
            p1 = new InputFrame[ticks];
            p2 = new InputFrame[ticks];
            holds = catches = throws = 0;
            float frameX = Arena.HalfWidth - 1f, frameY = Arena.HalfHeight - 1f;
            for (int t = 0; t < ticks; t++)
            {
                int tick = world.State.Tick;
                p1[t] = bot.Poll(tick);
                p2[t] = practice.Poll(tick);
                world.Step(p1[t], p2[t]);
                var s = world.State;
                for (int k = 0; k < 2; k++)
                {
                    var f = s.GetFighter(k);
                    Assert.IsFalse(float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y), $"NaN 틱 {t}");
                    Assert.LessOrEqual(SimMath.Abs(f.Position.X), frameX, $"P{k + 1} 프레임 밖 틱 {t}");
                    Assert.LessOrEqual(SimMath.Abs(f.Position.Y), frameY, $"P{k + 1} 프레임 밖 틱 {t}");
                    Assert.LessOrEqual(f.SplatDamageTaken, config.Combat.Body.SplatDamageMax);
                }
                for (int e = 0; e < world.Events.Count; e++)
                {
                    var type = world.Events[e].Type;
                    Assert.AreNotEqual(SimEventType.OutOfBounds, type, $"틱 {t}");
                    if (type == SimEventType.Grab) holds++;
                    if (type == SimEventType.LungeCaught) catches++;
                    if (type == SimEventType.Throw) throws++;
                }
                Assert.AreEqual(0, world.Events.Dropped);
            }
        }

        [Test]
        public void GrabScenario_BotSmoke_DeterministicSaveRestoreSyncTestAndReplay()
        {
            var config = Config();
            BotFrames(config, 21, 1800, out var p1, out var p2, out int holds, out int catches, out int throws);
            TestContext.WriteLine($"[CR-004 C] 봇 30초: 잡기 {holds}, 받아내기 {catches}, 던지기 {throws}");
            Assert.Greater(holds, 0);
            // 받아내기 횟수는 기록만 한다: 연습 봇은 거리와 상관없이 일정 간격으로 쏘므로 봇끼리는 거의 생기지 않는다.
            // 받아내기 규칙은 Catch_* 결정적 시나리오가 확인한다.
            Assert.Greater(throws, 0);

            SimWorld Fresh() => new SimWorld(config, Arena, SimState.CreateMatch(config, new[] { Arena }, 21, MatchPhase.Countdown));
            var a = Fresh();
            var b = Fresh();
            for (int t = 0; t < p1.Length; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"틱 {t + 1}\n{StateDiff.Describe(a.State, b.State)}");
            }

            var w = Fresh();
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

            var synced = Fresh();
            var sync = new SyncTest<SimWorld>(synced, 7);
            for (int t = 0; t < p1.Length; t++)
                sync.Advance(p1[t], p2[t]);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(a.ComputeChecksum(), synced.ComputeChecksum());

            // 입력 기록 재생(라운드 기록 형식).
            var spawn0 = SimState.SpawnCenter(config, Arena, 0);
            var spawn1 = SimState.SpawnCenter(config, Arena, 1);
            var recording = InputRecording.Begin(config, Arena, 21, spawn0, spawn1, MatchPhase.Countdown, RuleSet.Brawl.Format());
            var rw = new SimWorld(config, Arena, SimState.CreateRound(config, Arena, 21, spawn0, spawn1, MatchPhase.Countdown));
            for (int t = 0; t < p1.Length; t++)
            {
                recording.Append(p1[t], p2[t]);
                rw.Step(p1[t], p2[t]);
            }
            recording.Finish(rw.ComputeChecksum());
            Assert.AreEqual(recording.FinalChecksum, InputRecording.FromBytes(recording.ToBytes()).Replay(config, Arena));
        }
    }
}
