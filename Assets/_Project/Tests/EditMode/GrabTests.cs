using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP7-2~7: 잡기·던지기(03-combat.md 6절). 상성표 전체(CP7-1)는 matrix_*.json 시나리오가 ScenarioTests로 돈다.
    /// 기본 배치: P1(-8)이 1틱째에 잡기를 누르면 6틱째(행동 틱 5)에 잡기 상자가 켜지고, 1.2 앞의 P2를 붙잡는다.
    /// </summary>
    public class GrabTests
    {
        const InputButtons Grab = InputButtons.Grab;
        const float FloorY = -7.55f;
        const float Tolerance = 1e-3f;
        const int ConnectTick = 6;

        static readonly GrabParams G = GrabParams.Default;

        static SimWorld World(CombatMode mode = CombatMode.Swing, float d = 1.2f, bool grabEnabled = true, float x0 = -8f, ArenaDef arena = null)
        {
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, mode, grabEnabled);
            return TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x0 + d, FloorY), config, arena);
        }

        static bool Has(SimWorld w, SimEventType type, int fighter)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                    return true;
            }
            return false;
        }

        static InputFrame In(int x, int y, InputButtons b) => new InputFrame(x, y, b);

        /// <summary>P1이 잡기를 누르고 계속 누른 채 붙잡을 때까지 진행한다.</summary>
        static void GrabAndHold(SimWorld w)
        {
            for (int t = 1; t <= ConnectTick; t++)
                w.Step(In(0, 0, Grab), default);
            Assert.IsTrue(Has(w, SimEventType.Grab, 1), "6틱째에 붙잡음");
            Assert.AreEqual(ActionState.Holding, w.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action);
        }

        // CP7-2 ---------------------------------------------------------------

        [TestCase(ActionState.Hitstun, false)]
        [TestCase(ActionState.Thrown, false)]
        [TestCase(ActionState.Stagger, true)]
        [TestCase(ActionState.GuardStun, true)]
        [TestCase(ActionState.Recoil, true)]
        public void Grabbability_ByState(ActionState state, bool grabbed)
        {
            var w = World();
            w.State.Fighter1.RestartAction(state);
            w.State.Fighter1.StunTicks = 40;
            for (int t = 1; t <= 12; t++)
                w.Step(In(0, 0, Grab), default);
            Assert.AreEqual(grabbed, w.State.Fighter1.Action == ActionState.Held, $"{state}: 잡힘 {grabbed}");
            if (!grabbed)
                Assert.AreEqual(ActionState.Grab, w.State.Fighter0.Action, "헛잡기 후딜 중");
        }

        [Test]
        public void KnockedOut_IsNotGrabbable()
        {
            var ko = new FighterState();
            ko.RestartAction(ActionState.KO);
            Assert.IsFalse(FighterCombat.CanBeGrabbed(ko));
            var held = new FighterState();
            held.RestartAction(ActionState.Held);
            Assert.IsFalse(FighterCombat.CanBeGrabbed(held));
        }

        [Test]
        public void Whiff_HasFiveThreeTwentyTwo()
        {
            var w = World(CombatMode.Swing, 4f);
            w.Step(In(0, 0, Grab), default);
            int startup = 0, active = 0, recovery = 0, guard = 0;
            while (w.State.Fighter0.Action == ActionState.Grab && guard++ < 60)
            {
                if (FighterCombat.IsGrabActive(w.State.Fighter0, w.Config))
                    active++;
                else if (active == 0)
                    startup++;
                else
                    recovery++;
                w.Step(default, default);
            }
            Assert.AreEqual(5, startup);
            Assert.AreEqual(3, active);
            Assert.AreEqual(22, recovery);
        }

        // CP7-3 ---------------------------------------------------------------

        [Test]
        public void Hold_FreezesBoth_AndThrowsAfterTwentyTicks()
        {
            var w = World();
            GrabAndHold(w);
            var p0 = w.State.Fighter0.Position;
            var p1 = w.State.Fighter1.Position;
            Assert.AreEqual(p0.X + G.HoldOffset, p1.X, Tolerance, "상대를 몸 앞 0.8에 고정");
            for (int i = 1; i < G.MaxHoldTicks; i++)
            {
                w.Step(In(0, 0, Grab), default);
                Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action, $"붙잡기 {i}틱째");
                Assert.AreEqual(p0, w.State.Fighter0.Position, "붙잡은 쪽도 움직이지 않음");
                Assert.AreEqual(p1, w.State.Fighter1.Position, "붙잡힌 쪽 고정");
            }
            w.Step(In(0, 0, Grab), default);
            Assert.IsTrue(Has(w, SimEventType.Throw, 1), "누르고 있어도 20틱에 던짐");
            Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);
        }

        [Test]
        public void ReleasingGrab_ThrowsThatTick()
        {
            var w = World();
            GrabAndHold(w);
            w.Step(In(0, 0, Grab), default);
            w.Step(In(0, 0, InputButtons.None), default);
            Assert.IsTrue(Has(w, SimEventType.Throw, 1), "떼는 틱에 던짐");
            Assert.AreEqual(100 - G.ThrowDamage, w.State.Fighter1.Health);
            Assert.AreEqual(G.ThrownTicks, w.State.Fighter1.StunTicks);
            Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action, "던진 쪽은 바로 조작 가능");
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(-1, 0)]
        [TestCase(-1, -1)]
        [TestCase(0, -1)]
        [TestCase(1, -1)]
        public void ThrowDirection_EightWayAndNeutral(int x, int y)
        {
            var w = World(CombatMode.Swing, 1.2f, true, -6f);
            GrabAndHold(w);
            float grabberX = w.State.Fighter0.Position.X;
            w.Step(In(x, y, InputButtons.None), default);
            Assert.IsTrue(Has(w, SimEventType.Throw, 1));
            var expected = x == 0 && y == 0 ? SimMath.Normalize(new Vec2(0.8660254f, 0.5f)) : SimMath.Normalize(new Vec2(x, y));
            Assert.AreEqual(expected.X * G.ThrowSpeed, w.State.Fighter1.Velocity.X, Tolerance, "vx");
            Assert.AreEqual(expected.Y * G.ThrowSpeed, w.State.Fighter1.Velocity.Y, Tolerance, "vy");
            if (x < 0)
                Assert.Less(w.State.Fighter1.Position.X, grabberX, "뒤로 던지면 등 뒤에서 날아간다");
        }

        // CP7-4 ---------------------------------------------------------------

        [TestCase(ActionState.Thrown, 13f, true, 10)]
        [TestCase(ActionState.Thrown, 12f, true, 10)]
        [TestCase(ActionState.Thrown, 11.5f, false, 0)]
        [TestCase(ActionState.Hitstun, 13f, false, 0)]
        [TestCase(ActionState.Hitstun, 14f, true, 6)]
        public void ThrownWallSplat_ThresholdTwelve_DamageTen(ActionState state, float speed, bool splat, int damage)
        {
            var w = TestWorlds.CreateAt(new Vec2(-12f, FloorY), new Vec2(14.45f, 0f), SimConfig.CreateDefault(), null);
            w.State.Fighter1.RestartAction(state);
            w.State.Fighter1.StunTicks = 30;
            w.State.Fighter1.Velocity = new Vec2(speed, 0f);
            w.Step(default, default);
            Assert.AreEqual(splat, Has(w, SimEventType.WallSplat, 1), $"{state} {speed}");
            Assert.AreEqual(100 - damage, w.State.Fighter1.Health);
        }

        [Test]
        public void ThrowIntoWall_Splats()
        {
            // 벽 바로 앞에서 잡아 벽 쪽으로 던지면 벽꽝(던져진 상태 데미지 10).
            var w = World(CombatMode.Swing, 1.2f, true, 12.4f);
            GrabAndHold(w);
            w.Step(In(1, 0, InputButtons.None), default);
            bool splat = false;
            for (int t = 0; t < 10 && !splat; t++)
            {
                w.Step(default, default);
                splat = Has(w, SimEventType.WallSplat, 1);
            }
            Assert.IsTrue(splat, "벽에 던져 넣으면 벽꽝");
            Assert.AreEqual(100 - G.ThrowDamage - G.ThrownSplatDamage, w.State.Fighter1.Health);
        }

        // CP7-5 ---------------------------------------------------------------

        [Test]
        public void MutualGrabSameTick_Breaks()
        {
            var w = World();
            for (int t = 1; t <= ConnectTick; t++)
                w.Step(In(0, 0, t == 1 ? Grab : InputButtons.None), In(0, 0, t == 1 ? Grab : InputButtons.None));
            Assert.IsTrue(Has(w, SimEventType.GrabBreak, SimEvent.NoFighter), "같은 틱 상호 잡기 → 잡기 풀기");
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter1.Action);
            Assert.AreEqual(-G.TechSpeed, w.State.Fighter0.Velocity.X, Tolerance, "반대로 10");
            Assert.AreEqual(G.TechSpeed, w.State.Fighter1.Velocity.X, Tolerance);
            Assert.AreEqual(G.TechRecoilTicks, w.State.Fighter0.StunTicks);
            Assert.AreEqual(G.TechHitstop, w.State.Fighter0.HitstopTicks);
            Assert.AreEqual(100, w.State.Fighter0.Health);
            Assert.AreEqual(100, w.State.Fighter1.Health);
        }

        // CP7-6 ---------------------------------------------------------------

        [Test]
        public void GrabDisabled_IgnoresGrabInput()
        {
            var w = World(CombatMode.Swing, 1.2f, false);
            for (int t = 0; t < 20; t++)
            {
                w.Step(In(0, 0, t % 2 == 0 ? Grab : InputButtons.None), default);
                Assert.IsFalse(Has(w, SimEventType.GrabStart, 0), "잡기를 끄면 잡기 입력 무시");
                Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            }
            Assert.AreNotEqual(ActionState.Held, w.State.Fighter1.Action);
            Assert.AreNotEqual(SimConfig.CreateDefault().ComputeChecksum(),
                new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, CombatMode.Test, false).ComputeChecksum(),
                "잡기 설정은 설정 체크섬에 들어간다");
        }

        // CP7-7 ---------------------------------------------------------------

        [TestCase(CombatMode.Charge, "Box", 41u)]
        [TestCase(CombatMode.Charge, "Towers", 42u)]
        [TestCase(CombatMode.Swing, "Box", 43u)]
        [TestCase(CombatMode.Swing, "Towers", 44u)]
        public void MirroredGrabFight_AndSyncTest(CombatMode mode, string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var combat = CombatParams.Default;
            combat.MaxHealth = 50;
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, mode);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new CombatBot(seed * 13u, true);
            var bot1 = new CombatBot(seed * 29u, true);
            int grabs = 0, throws = 0, breaks = 0;
            for (int t = 0; t < 3600; t++)
            {
                var s = normal.State;
                var p1 = bot0.Next(s.Fighter0, s.Fighter1, t);
                var p2 = bot1.Next(s.Fighter1, s.Fighter0, t);
                normal.Step(p1, p2);
                mirrored.Step(SymmetryTests.Mirror(p2), SymmetryTests.Mirror(p1));
                sync.Advance(p1, p2);
                for (int i = 0; i < normal.Events.Count; i++)
                {
                    grabs += normal.Events[i].Type == SimEventType.Grab ? 1 : 0;
                    throws += normal.Events[i].Type == SimEventType.Throw ? 1 : 0;
                    breaks += normal.Events[i].Type == SimEventType.GrabBreak ? 1 : 0;
                }
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            string report = $"[Knockbox][CP7-7] {mode} {map}: 잡기 {grabs}, 던지기 {throws}, 잡기 풀기 {breaks}, SyncTest 불일치 {sync.MismatchCount}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(grabs, 0, "잡기가 나와야 대칭 검증 의미가 있음");
            Assert.Greater(throws, 0);
        }

        /// <summary>
        /// 정해진 입력으로 잡고 9방향(중립 포함)으로 던지는 장면과 벽에 던져 넣는 장면을 뒤집어 진행해도 매 틱 일치하고,
        /// 같은 입력으로 SyncTest도 통과한다(봇 대전보다 잡기·던지기를 빠짐없이 지나간다).
        /// </summary>
        [TestCase(CombatMode.Charge)]
        [TestCase(CombatMode.Swing)]
        public void MirroredScriptedGrabs_AllThrowDirections(CombatMode mode)
        {
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, mode);
            int[,] directions = { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 } };
            int throws = 0, splats = 0;
            for (int d = 0; d <= directions.GetLength(0); d++)
            {
                // 마지막은 벽 앞에서 벽 쪽으로 던지기.
                bool wall = d == directions.GetLength(0);
                float x0 = wall ? 12.4f : -6f;
                int dx = wall ? 1 : directions[d, 0], dy = wall ? 0 : directions[d, 1];
                var normal = TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x0 + 1.2f, FloorY), config);
                var mirrored = TestWorlds.CreateAt(new Vec2(-x0 - 1.2f, FloorY), new Vec2(-x0, FloorY), config);
                var synced = TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x0 + 1.2f, FloorY), config);
                var sync = new SyncTest<SimWorld>(synced);
                for (int t = 0; t < 70; t++)
                {
                    var p1 = t < 9 ? In(0, 0, Grab) : t == 9 ? In(dx, dy, InputButtons.None) : default;
                    normal.Step(p1, default);
                    mirrored.Step(default, SymmetryTests.Mirror(p1));
                    sync.Advance(p1, default);
                    for (int i = 0; i < normal.Events.Count; i++)
                    {
                        throws += normal.Events[i].Type == SimEventType.Throw ? 1 : 0;
                        splats += normal.Events[i].Type == SimEventType.WallSplat ? 1 : 0;
                    }
                    SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, $"방향 ({dx},{dy}) 던진 쪽");
                    SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, $"방향 ({dx},{dy}) 던져진 쪽");
                }
                Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            }
            Assert.AreEqual(10, throws, "10번 모두 던짐");
            Assert.Greater(splats, 0, "벽에 던져 넣기 포함");
        }
    }
}
