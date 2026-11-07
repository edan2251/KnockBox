using System;
using System.Text;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP2-2~7, CP2-9, CP2-10: 02-movement.md의 수치와 동작.</summary>
    public class MovementTests
    {
        const InputButtons J = InputButtons.Jump;
        const InputButtons D = InputButtons.Dash;
        const float Tolerance = 1e-4f;

        static InputFrame In(int x, int y = 0, InputButtons b = InputButtons.None) => new InputFrame(x, y, b);
        static InputFrame Btn(InputButtons b) => new InputFrame(0, 0, b);

        /// <summary>P1에게 입력을 주고 한 틱 진행한 뒤, 이번 틱에 P1의 해당 이벤트가 있었는지.</summary>
        static bool Step(SimWorld w, InputFrame p1, SimEventType type)
        {
            w.Step(p1, default);
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Fighter == 0 && w.Events[i].Type == type)
                    return true;
            }
            return false;
        }

        static FighterState P1(SimWorld w) => w.State.Fighter0;

        /// <summary>테두리 + 추가 칸. cells(c, r)가 '\0'이 아니면 그 문자를 놓는다.</summary>
        public static ArenaDef Build(string name, Func<int, int, char> cells)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < ArenaDef.ClassicRows; r++)
            {
                for (int c = 0; c < ArenaDef.ClassicColumns; c++)
                {
                    bool border = r == 0 || r == ArenaDef.ClassicRows - 1 || c == 0 || c == ArenaDef.ClassicColumns - 1;
                    // 시작 표시는 맨 윗줄에 둔다. 테스트는 CreateAt으로 위치를 직접 정한다.
                    char ch = border ? '#' : r == 1 && c == 3 ? '1' : r == 1 && c == 28 ? '2' : '.';
                    char extra = cells(c, r);
                    if (!border && extra != '\0')
                        ch = extra;
                    sb.Append(ch);
                }
                sb.Append('\n');
            }
            return TestWorlds.Grid(name, sb.ToString());
        }

        static readonly Vec2 FarAway = new Vec2(12.5f, -7.55f);

        // CP2-2 ---------------------------------------------------------------

        static float JumpHeight(int holdTicks)
        {
            var w = TestWorlds.Create();
            float start = P1(w).Position.Y;
            float max = start;
            for (int t = 0; t < 120; t++)
            {
                w.Step(t < holdTicks ? Btn(J) : default, default);
                max = Math.Max(max, P1(w).Position.Y);
            }
            Assert.IsTrue(P1(w).Grounded, "착지하지 않음");
            return max - start;
        }

        [Test]
        public void JumpApex_FullAndShort()
        {
            float full = JumpHeight(60);
            float tap = JumpHeight(1);
            string report = $"[Knockbox][CP2-2] 최대 점프 정점 {full:F3}, 짧은 점프 {tap:F3}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.That(full, Is.InRange(3.8f, 4.3f));
            Assert.That(tap, Is.InRange(1.3f, 1.9f));
        }

        // CP2-3 ---------------------------------------------------------------

        [Test]
        public void Run_AccelerationAndDecelerationTicks()
        {
            var w = TestWorlds.Create();
            float max = w.Config.Movement.MaxRunSpeed;
            int up = 0;
            while (P1(w).Velocity.X < max - Tolerance && up < 60)
            {
                w.Step(In(1), default);
                up++;
            }
            int down = 0;
            while (P1(w).Velocity.X > 0f && down < 60)
            {
                w.Step(default, default);
                down++;
            }
            string report = $"[Knockbox][CP2-3] 정지→최대 {up}틱, 최대→정지 {down}틱";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.That(up, Is.InRange(5, 7));
            Assert.That(down, Is.InRange(7, 10));
        }

        [Test]
        public void Turnaround_IsFasterThanPlainAcceleration()
        {
            var w = TestWorlds.Create();
            for (int t = 0; t < 10; t++)
                w.Step(In(1), default);
            float before = P1(w).Velocity.X;
            w.Step(In(-1), default);
            var m = w.Config.Movement;
            Assert.AreEqual(before - m.GroundAccel * m.TurnAccelMultiplier * SimConfig.Dt, P1(w).Velocity.X, Tolerance);
        }

        // CP2-4 ---------------------------------------------------------------

        /// <summary>왼쪽에 꼭대기 y = -3인 단단한 턱(x -15..-5).</summary>
        static ArenaDef LedgeArena() => Build("Ledge", (c, r) => r >= 12 && c <= 10 ? '#' : '\0');

        [TestCase(5, true)]
        [TestCase(6, true)]
        [TestCase(7, false)]
        public void Coyote_JumpAfterLeavingLedge(int ticksAfterLeaving, bool expectJump)
        {
            var config = TestWorlds.Config(m => { m.AirJumps = 0; return m; });
            var w = TestWorlds.CreateAt(new Vec2(-6f, -2.55f), FarAway, config, LedgeArena());
            Assert.IsTrue(P1(w).Grounded);
            int guard = 0;
            while (P1(w).Grounded && guard++ < 120)
                w.Step(In(1), default);
            Assert.IsFalse(P1(w).Grounded, "턱을 벗어나지 못함");

            for (int k = 1; k < ticksAfterLeaving; k++)
                w.Step(In(1), default);
            Assert.AreEqual(expectJump, Step(w, In(1, 0, J), SimEventType.Jump), $"벗어난 지 {ticksAfterLeaving}틱 뒤 점프");
        }

        // CP2-5 ---------------------------------------------------------------

        [Test]
        public void JumpBuffer_PressedSevenTicksBeforeLanding_JumpsOnLandingTick()
        {
            var config = TestWorlds.Config(m => { m.AirJumps = 0; return m; });
            SimWorld Make() => TestWorlds.CreateAt(new Vec2(-13.5f, -2f), FarAway, config);

            var reference = Make();
            int landing = -1;
            for (int t = 1; t <= 200 && landing < 0; t++)
            {
                if (Step(reference, default, SimEventType.Land))
                    landing = t;
            }
            Assert.Greater(landing, 10);

            Assert.IsTrue(JumpsOnLanding(Make(), landing, landing - 7), "착지 7틱 전에 누른 점프가 착지 틱에 실행되지 않음");
            Assert.IsFalse(JumpsOnLanding(Make(), landing, landing - 10), "버퍼(8틱)보다 일찍 누른 점프가 실행됨");
        }

        static bool JumpsOnLanding(SimWorld w, int landing, int pressTick)
        {
            bool landed = false, jumped = false;
            for (int t = 1; t <= landing; t++)
            {
                w.Step(t == pressTick ? Btn(J) : default, default);
                if (t != landing)
                    continue;
                for (int i = 0; i < w.Events.Count; i++)
                {
                    var e = w.Events[i];
                    if (e.Fighter != 0)
                        continue;
                    landed |= e.Type == SimEventType.Land;
                    jumped |= e.Type == SimEventType.Jump;
                }
            }
            Assert.IsTrue(landed, "예상한 틱에 착지하지 않음");
            return jumped && P1(w).Velocity.Y > 0f;
        }

        // CP2-6 ---------------------------------------------------------------

        [Test]
        public void WallSlide_SpeedIsCapped()
        {
            var w = TestWorlds.CreateAt(new Vec2(-14.55f, 4f), FarAway);
            bool sliding = false;
            float slowest = 0f;
            for (int t = 0; t < 60; t++)
            {
                w.Step(In(-1), default);
                sliding |= P1(w).Action == ActionState.WallSlide;
                slowest = Math.Min(slowest, P1(w).Velocity.Y);
                Assert.IsFalse(P1(w).Grounded);
            }
            Assert.IsTrue(sliding, "벽 미끄러짐 상태가 되지 않음");
            Assert.GreaterOrEqual(slowest, -w.Config.Movement.WallSlideMaxSpeed - Tolerance, $"가장 빠른 낙하 {slowest}");
        }

        [Test]
        public void WallJump_IgnoresWallwardInputForSevenTicks()
        {
            SimWorld Make() => TestWorlds.CreateAt(new Vec2(-14.55f, 4f), FarAway);
            var holding = Make();
            var neutral = Make();
            for (int t = 0; t < 10; t++)
            {
                holding.Step(In(-1), default);
                neutral.Step(In(-1), default);
            }
            Assert.IsTrue(Step(holding, In(-1, 0, J), SimEventType.WallJump));
            Assert.IsTrue(Step(neutral, In(-1, 0, J), SimEventType.WallJump));
            Assert.Greater(P1(holding).Velocity.X, 0f, "벽 반대쪽으로 튀어야 함");

            int lockTicks = holding.Config.Movement.WallJumpInputLockTicks;
            for (int k = 1; k <= lockTicks; k++)
            {
                holding.Step(In(-1, 0, J), default);
                neutral.Step(Btn(J), default);
                Assert.AreEqual(P1(neutral).Velocity.X, P1(holding).Velocity.X, $"벽 점프 후 {k}틱: 벽 쪽 입력이 무시되지 않음");
            }
            holding.Step(In(-1, 0, J), default);
            neutral.Step(Btn(J), default);
            Assert.Less(P1(holding).Velocity.X, P1(neutral).Velocity.X, $"{lockTicks + 1}틱째에는 입력이 다시 먹혀야 함");
        }

        /// <summary>가운데 폭 2(x -1..1)짜리 수직 통로. 벽은 y -8..6.</summary>
        static ArenaDef ShaftArena() => Build("Shaft", (c, r) => r >= 3 && (c == 14 || c == 17) ? '#' : '\0');

        [Test]
        public void WallJumps_ClimbTwoWideShaft()
        {
            var w = TestWorlds.CreateAt(new Vec2(0f, -7.55f), FarAway, null, ShaftArena());
            float start = P1(w).Position.Y;
            float best = start;
            int wallJumps = 0;
            for (int t = 0; t < 360; t++)
            {
                var f = P1(w);
                bool touching = f.WallSide != 0 && !f.Grounded;
                bool held = (f.PrevButtons & J) != 0;
                int dir = f.LastWallSide != 0 ? -f.LastWallSide : 1;
                var buttons = J;
                if (f.Grounded || touching)
                {
                    // 붙어 있는 동안 한 틱 떼었다가 새로 누른다. 떼는 틱에는 벽 쪽을 눌러 붙어 있는다.
                    buttons = held ? InputButtons.None : J;
                    dir = touching ? (held ? f.WallSide : -f.WallSide) : 1;
                }
                if (Step(w, In(dir, 0, buttons), SimEventType.WallJump))
                    wallJumps++;
                best = Math.Max(best, P1(w).Position.Y);
            }
            float climbed = best - start;
            string report = $"[Knockbox][CP2-6] 폭 2 통로에서 벽 점프 {wallJumps}회, 상승 {climbed:F2}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.GreaterOrEqual(climbed, 6f);
        }

        // CP2-7 ---------------------------------------------------------------

        [Test]
        public void GroundDash_DistanceAndNoVerticalMotion()
        {
            var w = TestWorlds.Create();
            var start = P1(w).Position;
            Assert.IsTrue(Step(w, Btn(D), SimEventType.Dash));
            int dashTicks = w.Config.Movement.DashTicks;
            for (int t = 1; t < dashTicks; t++)
            {
                Assert.AreEqual(ActionState.Dash, P1(w).Action);
                w.Step(default, default);
            }
            float distance = P1(w).Position.X - start.X;
            string report = $"[Knockbox][CP2-7] 지상 수평 대시 거리 {distance:F3} ({dashTicks}틱)";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.That(distance, Is.InRange(3.0f, 4.0f));
            Assert.AreEqual(start.Y, P1(w).Position.Y, Tolerance);
            Assert.AreNotEqual(ActionState.Dash, P1(w).Action, "8틱 뒤에는 대시가 끝나야 함");
            Assert.AreEqual(w.Config.Movement.DashSpeed * w.Config.Movement.DashEndSpeedRetention, P1(w).Velocity.X, 1e-3f, "종료 속도 유지율");
        }

        [Test]
        public void AirDash_HasNoGravity()
        {
            var w = TestWorlds.CreateAt(new Vec2(-13.5f, 2f), FarAway);
            w.Step(default, default);
            Assert.IsTrue(Step(w, In(1, 0, D), SimEventType.Dash));
            float y = P1(w).Position.Y;
            for (int t = 1; t < w.Config.Movement.DashTicks; t++)
            {
                w.Step(default, default);
                Assert.AreEqual(y, P1(w).Position.Y, Tolerance, "대시 중 높이가 바뀜");
                Assert.AreEqual(0f, P1(w).Velocity.Y);
            }
        }

        [Test]
        public void DashCharge_RecoversOnLandingAndWallContact()
        {
            var w = TestWorlds.CreateAt(new Vec2(-12f, 6f), FarAway);
            Assert.IsTrue(Step(w, In(1, 0, D), SimEventType.Dash));
            Assert.AreEqual(0, P1(w).DashCharges);
            for (int t = 0; t < w.Config.Movement.DashCooldownTicks + 1; t++)
                w.Step(default, default);
            Assert.IsFalse(P1(w).Grounded);
            Assert.IsFalse(Step(w, In(1, 0, D), SimEventType.Dash), "충전 없이 공중 대시가 됨");

            int guard = 0;
            while (!P1(w).Grounded && guard++ < 200)
                w.Step(default, default);
            Assert.IsTrue(P1(w).Grounded);
            Assert.AreEqual(1, P1(w).DashCharges, "착지로 회복되지 않음");

            // 벽 접촉: 공중에서 왼쪽 벽으로 대시해 닿으면 회복된다.
            var wall = TestWorlds.CreateAt(new Vec2(-12f, 6f), FarAway);
            Assert.IsTrue(Step(wall, In(-1, 0, D), SimEventType.Dash));
            Assert.AreEqual(-1, P1(wall).Facing, "대시를 시작한 쪽을 바라봐야 함");
            Assert.AreEqual(0, P1(wall).DashCharges);
            guard = 0;
            while (P1(wall).WallSide == 0 && guard++ < 30)
                wall.Step(default, default);
            Assert.AreEqual(-1, P1(wall).WallSide);
            Assert.IsFalse(P1(wall).Grounded);
            Assert.AreEqual(1, P1(wall).DashCharges, "벽 접촉으로 회복되지 않음");
        }

        [Test]
        public void DashJumpCancel_KeepsHorizontalSpeed()
        {
            var w = TestWorlds.Create();
            Assert.IsTrue(Step(w, Btn(D), SimEventType.Dash));
            int minTicks = w.Config.Movement.DashJumpCancelMinTicks;
            for (int t = 1; t < minTicks; t++)
                w.Step(default, default);
            Assert.IsTrue(Step(w, Btn(J), SimEventType.Jump), "대시 3틱 이후 점프 캔슬이 안 됨");
            Assert.AreNotEqual(ActionState.Dash, P1(w).Action);
            Assert.AreEqual(w.Config.Movement.DashSpeed, P1(w).Velocity.X, Tolerance);
            Assert.Greater(P1(w).Velocity.Y, 0f);

            var early = TestWorlds.Create();
            Step(early, Btn(D), SimEventType.Dash);
            Assert.IsFalse(Step(early, Btn(J), SimEventType.Jump), "대시 3틱 전에 캔슬됨");
        }

        // CP2-9 ---------------------------------------------------------------

        /// <summary>가운데(x -8..8) 단방향 발판, 윗면 y = -5(바닥에서 3 위).</summary>
        static ArenaDef PlatformArena() => Build("Platform", (c, r) => r == 14 && c >= 8 && c <= 23 ? '=' : '\0');

        [Test]
        public void OneWay_PassFromBelowLandFromAboveDropWithDownJump()
        {
            var arena = PlatformArena();
            float onTop = -5f + 0.45f;

            var below = TestWorlds.CreateAt(new Vec2(0f, -7.55f), FarAway, null, arena);
            for (int t = 0; t < 90; t++)
                below.Step(t < 40 ? Btn(J) : default, default);
            Assert.IsTrue(P1(below).Grounded, "아래에서 통과해 올라가 착지해야 함");
            Assert.AreEqual(onTop, P1(below).Position.Y, Tolerance);

            var above = TestWorlds.CreateAt(new Vec2(0f, 2f), FarAway, null, arena);
            for (int t = 0; t < 60; t++)
                above.Step(default, default);
            Assert.IsTrue(P1(above).Grounded);
            Assert.AreEqual(onTop, P1(above).Position.Y, Tolerance, "위에서 내려오면 발판에 착지");
            Assert.AreEqual(TerrainKind.OneWay, arena.GetBox(P1(above).GroundBox).Kind);

            for (int t = 0; t < 20; t++)
                above.Step(In(0, -1), default);
            Assert.AreEqual(onTop, P1(above).Position.Y, Tolerance, "아래만 누르면 내려가지 않음");

            Assert.IsFalse(Step(above, In(0, -1, J), SimEventType.Jump), "아래 + 점프는 점프가 아니라 하강");
            for (int t = 0; t < 60; t++)
                above.Step(default, default);
            Assert.IsTrue(P1(above).Grounded);
            Assert.AreEqual(-7.55f, P1(above).Position.Y, Tolerance, "바닥까지 떨어져야 함");
        }

        // CP2-10 --------------------------------------------------------------

        [Test]
        public void BouncePad_LaunchesAndRecovers()
        {
            var arena = Build("Pad", (c, r) => r == 16 && c >= 14 && c <= 17 ? 'B' : '\0');
            var w = TestWorlds.CreateAt(new Vec2(0f, 0f), FarAway, null, arena);
            Assert.IsTrue(Step(w, Btn(J), SimEventType.AirJump));
            Assert.IsTrue(Step(w, In(0, -1, D), SimEventType.Dash), "아래로 대시");
            Assert.AreEqual(0, P1(w).AirJumpsLeft);
            Assert.AreEqual(0, P1(w).DashCharges);

            bool bounced = false;
            for (int t = 0; t < 120 && !bounced; t++)
                bounced = Step(w, default, SimEventType.PadBounce);
            Assert.IsTrue(bounced, "튕김 패드에 닿지 않음");
            Assert.AreEqual(w.Config.Movement.BouncePadSpeed, P1(w).Velocity.Y, Tolerance);
            Assert.AreEqual(w.Config.Movement.AirJumps, P1(w).AirJumpsLeft);
            Assert.AreEqual(w.Config.Movement.DashCharges, P1(w).DashCharges);
            Assert.IsFalse(P1(w).Grounded);
        }

        /// <summary>왼쪽에서 달려가다 x ≥ jumpAt에서 점프. 기둥(-1..1, 높이 3)을 넘었는지.</summary>
        static bool ClearsPillar(float jumpAt, int holdTicks)
        {
            var config = TestWorlds.Config(m => { m.AirJumps = 0; return m; });
            var w = TestWorlds.CreateAt(new Vec2(-8f, -7.55f), FarAway, config);
            int pressedAt = -1;
            for (int t = 0; t < 150; t++)
            {
                if (pressedAt < 0 && P1(w).Position.X >= jumpAt && P1(w).Grounded)
                    pressedAt = t;
                bool jump = pressedAt >= 0 && t < pressedAt + holdTicks;
                w.Step(In(1, 0, jump ? J : InputButtons.None), default);
            }
            return pressedAt >= 0 && P1(w).Position.X > 1.45f;
        }

        [Test]
        public void BoxPillar_ClearedByFullJumpOnly()
        {
            int fullSuccesses = 0, shortSuccesses = 0;
            for (float jumpAt = -7f; jumpAt <= -1.5f; jumpAt += 0.1f)
            {
                if (ClearsPillar(jumpAt, 60))
                    fullSuccesses++;
                if (ClearsPillar(jumpAt, 1))
                    shortSuccesses++;
            }
            string report = $"[Knockbox][CP2-10] 기둥 넘기 성공: 최대 점프 {fullSuccesses}/56, 짧은 점프 {shortSuccesses}/56";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.Greater(fullSuccesses, 0, "최대 점프로 넘을 수 있어야 함");
            Assert.AreEqual(0, shortSuccesses, "짧은 점프로는 넘을 수 없어야 함");
        }
    }
}
