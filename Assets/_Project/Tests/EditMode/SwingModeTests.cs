using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP6-1~6: 모드 B 무기 휘두르기(03-combat.md 5절).</summary>
    public class SwingModeTests
    {
        const InputButtons A = InputButtons.Attack;
        const float FloorY = -7.55f;
        const float Tolerance = 1e-4f;

        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("SwingFlat", (c, r) => '\0');

        static SimConfig SwingConfig(System.Func<CombatParams, CombatParams> change = null)
        {
            var combat = CombatParams.Default;
            if (change != null)
                combat = change(combat);
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Swing);
        }

        static SimWorld World(float x0, float y0, float x1, float y1, SimConfig config = null) =>
            TestWorlds.CreateAt(new Vec2(x0, y0), new Vec2(x1, y1), config ?? SwingConfig(), Flat);

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

        // CP6-1 ---------------------------------------------------------------

        /// <summary>공격을 시작하고 끝날 때까지 시작·활성·후딜 틱을 센다.</summary>
        static void MeasureFrames(SimWorld w, InputFrame press, int expectedId, out int startup, out int active, out int recovery)
        {
            var config = w.Config;
            w.Step(press, default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action, "공격 시작");
            Assert.AreEqual(expectedId, w.State.Fighter0.AttackId, "기술 선택");
            startup = active = recovery = 0;
            int guard = 0;
            while (w.State.Fighter0.Action == ActionState.Attack && guard++ < 100)
            {
                if (FighterCombat.IsHitboxActive(w.State.Fighter0, config))
                    active++;
                else if (active == 0)
                    startup++;
                else
                    recovery++;
                w.Step(default, default);
            }
        }

        [TestCase(0, 0, false, AttackIds.SwingForward, 5, 4, 14)]
        [TestCase(1, 0, false, AttackIds.SwingForward, 5, 4, 14)]
        [TestCase(0, -1, false, AttackIds.SwingForward, 5, 4, 14)]
        [TestCase(0, 1, false, AttackIds.SwingUp, 5, 4, 16)]
        [TestCase(0, 0, true, AttackIds.SwingForward, 5, 4, 14)]
        [TestCase(0, 1, true, AttackIds.SwingUp, 5, 4, 16)]
        [TestCase(0, -1, true, AttackIds.SwingDown, 6, 5, 16)]
        public void FrameData_MatchesSpec(int moveX, int moveY, bool air, int id, int startup, int active, int recovery)
        {
            var w = World(-6f, air ? 6f : FloorY, 12f, FloorY);
            MeasureFrames(w, In(moveX, moveY, A), id, out int s, out int a, out int r);
            Assert.AreEqual(startup, s, "시작");
            Assert.AreEqual(active, a, "활성");
            Assert.AreEqual(recovery, r, "후딜");
        }

        // CP6-2 ---------------------------------------------------------------

        [TestCase(AttackIds.SwingForward, 1)]
        [TestCase(AttackIds.SwingForward, -1)]
        [TestCase(AttackIds.SwingUp, 1)]
        [TestCase(AttackIds.SwingDown, -1)]
        public void HitCircles_FollowKeyframesEachActiveTick(int id, int facing)
        {
            var config = SwingConfig();
            ref readonly var attack = ref config.GetAttack(id);
            var f = new FighterState { Position = new Vec2(2f, 1f), Facing = (sbyte)facing, AttackId = id };
            f.RestartAction(ActionState.Attack);
            for (int k = 0; k < attack.ActiveTicks; k++)
            {
                f.ActionTick = attack.StartupTicks + k;
                Assert.IsTrue(FighterCombat.IsHitboxActive(f, config));
                float t = (float)k / (attack.ActiveTicks - 1);
                for (int i = 0; i < attack.UsedCircles; i++)
                {
                    var c = attack.GetCircle(i);
                    // 꺾은선: 앞 절반은 Start → Mid, 뒤 절반은 Mid → End.
                    var local = t <= 0.5f ? c.Start + (c.Mid - c.Start) * (t * 2f) : c.Mid + (c.End - c.Mid) * ((t - 0.5f) * 2f);
                    var world = FighterCombat.CircleCenter(f, attack, c);
                    Assert.AreEqual(2f + local.X * facing, world.X, Tolerance, $"원 {i} 활성 {k}틱 x");
                    Assert.AreEqual(1f + local.Y, world.Y, Tolerance, $"원 {i} 활성 {k}틱 y");
                }
            }
            // 첫 틱은 Start, 마지막 틱은 End에 정확히 있다.
            f.ActionTick = attack.StartupTicks;
            Assert.AreEqual(2f + attack.Circle2.Start.X * facing, FighterCombat.CircleCenter(f, attack, attack.Circle2).X, Tolerance);
            f.ActionTick = attack.StartupTicks + attack.ActiveTicks - 1;
            Assert.AreEqual(2f + attack.Circle2.End.X * facing, FighterCombat.CircleCenter(f, attack, attack.Circle2).X, Tolerance);
        }

        [Test]
        public void OneSwing_HitsSameOpponentOnce()
        {
            // 상대가 활성 4틱 내내 판정 안에 있어도 한 번만 맞는다(히트스탑 뒤 남은 활성 틱 포함).
            var w = World(-1f, FloorY, 0.2f, FloorY);
            int hits = 0, guards = 0;
            w.Step(In(0, 0, A), In(0, 0, InputButtons.None));
            for (int t = 0; t < 40; t++)
            {
                w.Step(default, default);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    hits += w.Events[i].Type == SimEventType.Hit ? 1 : 0;
                    guards += w.Events[i].Type == SimEventType.GuardHit ? 1 : 0;
                }
            }
            Assert.AreEqual(1, hits + guards, "한 번 휘두를 때 한 번");
        }

        // CP6-3 ---------------------------------------------------------------

        [Test]
        public void DownSlamHit_PogosAndRestoresAirMoves()
        {
            var w = World(-1.1f, -5.6f, 0f, FloorY);
            w.State.Fighter0.AirJumpsLeft = 0;
            w.State.Fighter0.DashCharges = 0;
            w.Step(In(0, -1, A), default);
            Assert.AreEqual(AttackIds.SwingDown, w.State.Fighter0.AttackId);
            bool hit = false;
            for (int t = 0; t < 12 && !hit; t++)
            {
                w.Step(In(0, -1, InputButtons.None), default);
                hit = Has(w, SimEventType.Hit, 1);
            }
            Assert.IsTrue(hit, "내려찍기가 명중해야 함");
            Assert.AreEqual(18f, w.State.Fighter0.Velocity.Y, Tolerance, "포고 vy = 18");
            Assert.AreEqual(MovementParams.Default.AirJumps, w.State.Fighter0.AirJumpsLeft, "공중 점프 회복");
            Assert.AreEqual(MovementParams.Default.DashCharges, w.State.Fighter0.DashCharges, "대시 회복");
            var dir = SimMath.Normalize(new Vec2(0.3420201f, -0.9396926f));
            Assert.AreEqual(dir.X * 12f, w.State.Fighter1.Velocity.X, 1e-3f, "넉백 12, 아래 앞 -70°");
            Assert.AreEqual(dir.Y * 12f, w.State.Fighter1.Velocity.Y, 1e-3f);
            Assert.AreEqual(90, w.State.Fighter1.Health);

            // 히트스탑이 끝나면 실제로 튀어 오른다.
            int hitstop = w.State.Fighter0.HitstopTicks;
            float y = w.State.Fighter0.Position.Y;
            for (int t = 0; t <= hitstop; t++)
                w.Step(default, default);
            Assert.Greater(w.State.Fighter0.Position.Y, y, "포고로 올라감");
        }

        [Test]
        public void GuardedDownSlam_DoesNotPogo()
        {
            // 명중 테스트와 같은 배치에서 P2가 이미 가드를 누르고 있다(패링 구간 없음).
            var w = World(-1.1f, -5.6f, 0f, FloorY);
            w.State.Fighter0.AirJumpsLeft = 0;
            w.State.Fighter1.RestartAction(ActionState.Guard);
            w.State.Fighter1.PrevButtons = InputButtons.Guard;
            w.Step(In(0, -1, A), In(0, 0, InputButtons.Guard));
            bool guarded = false;
            for (int t = 0; t < 12 && !guarded; t++)
            {
                w.Step(In(0, -1, InputButtons.None), In(0, 0, InputButtons.Guard));
                guarded = Has(w, SimEventType.GuardHit, 1);
            }
            Assert.IsTrue(guarded, "가드에 막힘");
            Assert.Less(w.State.Fighter0.Velocity.Y, 18f, "가드에 막히면 포고 없음");
            Assert.AreEqual(0, w.State.Fighter0.AirJumpsLeft);
        }

        // CP6-4 ---------------------------------------------------------------

        [Test]
        public void ActiveSwingsOverlap_Clash()
        {
            int clashes = 0;
            var again = World(-1.4f, FloorY, 1.4f, FloorY);
            for (int t = 0; t < 12; t++)
            {
                again.Step(In(0, 0, t == 0 ? A : InputButtons.None), In(0, 0, t == 0 ? A : InputButtons.None));
                clashes += Has(again, SimEventType.Clash, SimEvent.NoFighter) ? 1 : 0;
                Assert.IsFalse(Has(again, SimEventType.Hit, 0) || Has(again, SimEventType.Hit, 1), "동시 휘두르기는 명중이 아니라 클래시");
            }
            Assert.AreEqual(1, clashes);
            Assert.AreEqual(ActionState.Recoil, again.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Recoil, again.State.Fighter1.Action);
            Assert.AreEqual(100, again.State.Fighter0.Health);
            Assert.AreEqual(100, again.State.Fighter1.Health);
        }

        [Test]
        public void BodyContact_OnlyPushesInSwingMode()
        {
            var w = World(-2f, FloorY, 0f, FloorY);
            for (int t = 0; t < 12; t++)
                w.Step(In(1, 0, t == 0 ? InputButtons.Dash : InputButtons.None), default);
            Assert.AreEqual(100, w.State.Fighter1.Health, "모드 B의 대시는 공격이 아님");
        }

        // CP6-5 ---------------------------------------------------------------

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        public void DashAttackCancel_OnlyAfterFourTicks(int elapsed, bool cancels)
        {
            var w = World(-10f, FloorY, 12f, FloorY);
            w.Step(In(1, 0, InputButtons.Dash), default);
            for (int e = 1; e < elapsed; e++)
                w.Step(In(1, 0, InputButtons.None), default);
            w.Step(In(1, 0, A), default);
            if (cancels)
            {
                Assert.AreEqual(ActionState.Attack, w.State.Fighter0.Action, $"대시 {elapsed}틱째 → 공격으로 캔슬");
                Assert.Greater(w.State.Fighter0.Velocity.X, MovementParams.Default.MaxRunSpeed, "대시 관성을 싣고 공격");
            }
            else
            {
                Assert.AreEqual(ActionState.Dash, w.State.Fighter0.Action, $"대시 {elapsed}틱째 → 아직 캔슬 불가");
            }
        }

        [Test]
        public void DashAttackCancel_CanBeDisabled()
        {
            var w = World(-10f, FloorY, 12f, FloorY, SwingConfig(c => { c.Swing.DashAttackCancelEnabled = false; return c; }));
            w.Step(In(1, 0, InputButtons.Dash), default);
            // 대시가 이어지는 동안(8틱) 4·6틱째에 공격을 눌러도 캔슬되지 않는다.
            for (int e = 1; e < MovementParams.Default.DashTicks; e++)
            {
                bool press = e == 4 || e == 6;
                w.Step(In(1, 0, press ? A : InputButtons.None), default);
                if (press)
                    Assert.AreEqual(ActionState.Dash, w.State.Fighter0.Action, $"대시 {e}틱째");
            }
        }

        [Test]
        public void LandingDuringAirAttack_FourTickLag()
        {
            var w = World(-6f, -7.0f, 12f, FloorY);
            w.Step(In(0, 0, A), default);
            Assert.IsTrue(w.State.Fighter0.AttackFromAir);
            int guard = 0;
            while (w.State.Fighter0.Action == ActionState.Attack && guard++ < 30)
                w.Step(default, default);
            Assert.AreEqual(ActionState.LandingLag, w.State.Fighter0.Action, "착지하면 공격이 끝나고 착지 경직");
            Assert.IsTrue(w.State.Fighter0.Grounded);
            for (int t = 1; t <= 4; t++)
            {
                w.Step(In(1, 0, InputButtons.Jump), default);
                Assert.AreEqual(ActionState.LandingLag, w.State.Fighter0.Action, $"착지 경직 {t}틱째");
            }
            w.Step(In(1, 0, InputButtons.None), default);
            Assert.AreNotEqual(ActionState.LandingLag, w.State.Fighter0.Action, "4틱 뒤 조작 가능");
        }

        [Test]
        public void GroundAttack_NoLandingLag_AndTestModeUnaffected()
        {
            var ground = World(-6f, FloorY, 12f, FloorY);
            ground.Step(In(0, 0, A), default);
            Assert.IsFalse(ground.State.Fighter0.AttackFromAir);

            var test = World(-6f, -7.0f, 12f, FloorY, SimConfig.CreateDefault());
            test.Step(In(0, 0, A), default);
            for (int t = 0; t < 30; t++)
            {
                test.Step(default, default);
                Assert.AreNotEqual(ActionState.LandingLag, test.State.Fighter0.Action, "착지 경직은 모드 B 규칙");
            }
        }

        // CP6-6 ---------------------------------------------------------------

        [TestCase("Box", 31u)]
        [TestCase("Towers", 32u)]
        [TestCase("Pit", 33u)]
        public void MirroredSwingFight_AndSyncTest(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var combat = CombatParams.Default;
            combat.MaxHealth = 50;
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Swing);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new CombatBot(seed * 13u);
            var bot1 = new CombatBot(seed * 29u);
            var counts = new CombatEventCounts();
            int swings = 0;
            for (int t = 0; t < 3600; t++)
            {
                var s = normal.State;
                var p1 = bot0.Next(s.Fighter0, s.Fighter1, t);
                var p2 = bot1.Next(s.Fighter1, s.Fighter0, t);
                normal.Step(p1, p2);
                mirrored.Step(SymmetryTests.Mirror(p2), SymmetryTests.Mirror(p1));
                sync.Advance(p1, p2);
                counts.Add(normal.Events);
                for (int i = 0; i < normal.Events.Count; i++)
                    swings += normal.Events[i].Type == SimEventType.AttackStart ? 1 : 0;
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            string report = $"[Knockbox][CP6-6] {map}: 휘두르기 {swings}, {counts}, SyncTest 불일치 {sync.MismatchCount}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(swings, 20);
            Assert.Greater(counts.Hit, 0);
        }
    }
}
