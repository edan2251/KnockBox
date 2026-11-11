using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP7: 잡기·던지기의 공통 규칙(03-combat.md 6절)을 현재 몸 폼 설정으로 확인한다.
    /// 2026-11-11 폼 전투 제거로 Swing·Charge 폼 설정과 폼 봇 대신 몸 폼(BrawlParams.Combat)과 BrawlBot을 쓴다.
    /// 잡기 수치(선딜·활성·유지 틱)와 커서 방향 던지기는 BrawlGrabTests가 검사한다.
    /// 기본 배치: Brawl 맵 바닥, P1(-2)이 오른쪽(-0.9)의 P2를 겨눈다. 우클릭 = Grab.
    /// </summary>
    public class GrabTests
    {
        const InputButtons Grab = InputButtons.Grab;
        const float Tolerance = 1e-3f;
        /// <summary>오른쪽·왼쪽 조준 번호(AimDirections).</summary>
        const int AimRight = 0, AimLeft = AimDirections.Count / 2;

        static SimConfig Config(bool grabEnabled = true, int maxHealth = 0)
        {
            var combat = BrawlParams.Combat(CombatParams.Default);
            if (maxHealth > 0)
                combat.MaxHealth = maxHealth;
            var rules = RuleSet.Brawl;
            rules.Switches.GrabEnabled = grabEnabled;
            return new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default), combat, rules);
        }

        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimWorld World(SimConfig config = null, float x0 = -2f, float x1 = -0.9f)
        {
            config ??= Config();
            float y = Arena.SpawnFoot0.Y + config.HalfBody;
            return TestWorlds.CreateAt(new Vec2(x0, y), new Vec2(x1, y), config, Arena);
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

        static InputFrame In(InputButtons b, int aim) => new InputFrame(0, 0, b, AimDirections.Encode(aim));

        [TestCase(ActionState.Hitstun, false)]
        [TestCase(ActionState.Thrown, false)]
        [TestCase(ActionState.Stagger, true)]
        [TestCase(ActionState.Recoil, true)]
        public void Grabbability_ByState(ActionState state, bool grabbed)
        {
            var w = World();
            w.State.Fighter1.RestartAction(state);
            w.State.Fighter1.StunTicks = 40;
            bool started = false;
            for (int t = 1; t <= 20 && w.State.Fighter1.Action != ActionState.Held; t++)
            {
                w.Step(In(Grab, AimRight), default);
                started |= Has(w, SimEventType.GrabStart, 0);
            }
            Assert.IsTrue(started, "잡기를 시작함");
            Assert.AreEqual(grabbed, w.State.Fighter1.Action == ActionState.Held, $"{state}: 잡힘 {grabbed}");
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

        /// <summary>던져진 상태는 충돌 속도 12 이상, 피격 경직은 벽꽝 속도 이상에서 벽꽝. 피해는 몸 폼 규칙(충돌 속도 × 계수, 상한).</summary>
        [TestCase(ActionState.Thrown, 13f, true)]
        [TestCase(ActionState.Thrown, 12f, true)]
        [TestCase(ActionState.Thrown, 11.5f, false)]
        [TestCase(ActionState.Hitstun, 13f, false)]
        [TestCase(ActionState.Hitstun, 14f, true)]
        public void ThrownWallSplat_ThresholdTwelve(ActionState state, float speed, bool splat)
        {
            var config = SimConfig.CreateDefault();
            var w = TestWorlds.CreateAt(new Vec2(-12f, -7.55f), new Vec2(14.45f, 0f), config, null);
            w.State.Fighter1.RestartAction(state);
            w.State.Fighter1.StunTicks = 30;
            w.State.Fighter1.Velocity = new Vec2(speed, 0f);
            w.Step(default, default);
            Assert.AreEqual(splat, Has(w, SimEventType.WallSplat, 1), $"{state} {speed}");
            var b = config.Combat.Body;
            int damage = splat ? System.Math.Min(b.SplatDamageMax, (int)(speed * b.SplatDamagePerSpeed + 0.5f)) : 0;
            Assert.AreEqual(100 - damage, w.State.Fighter1.Health);
        }

        [Test]
        public void MutualGrabSameTick_Breaks()
        {
            var w = World();
            var g = w.Config.Combat.Grab;
            bool broke = false;
            for (int t = 1; t <= 30 && !broke; t++)
            {
                w.Step(In(t == 1 ? Grab : InputButtons.None, AimRight), In(t == 1 ? Grab : InputButtons.None, AimLeft));
                broke = Has(w, SimEventType.GrabBreak, SimEvent.NoFighter);
            }
            Assert.IsTrue(broke, "같은 틱 상호 잡기 → 잡기 풀기");
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter1.Action);
            Assert.AreEqual(-g.TechSpeed, w.State.Fighter0.Velocity.X, Tolerance, "서로 반대로");
            Assert.AreEqual(g.TechSpeed, w.State.Fighter1.Velocity.X, Tolerance);
            Assert.AreEqual(g.TechRecoilTicks, w.State.Fighter0.StunTicks);
            Assert.AreEqual(g.TechHitstop, w.State.Fighter0.HitstopTicks);
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter0.Health);
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        [Test]
        public void GrabDisabled_IgnoresGrabInput()
        {
            var w = World(Config(grabEnabled: false));
            for (int t = 0; t < 20; t++)
            {
                w.Step(In(t % 2 == 0 ? Grab : InputButtons.None, AimRight), default);
                Assert.IsFalse(Has(w, SimEventType.GrabStart, 0), "잡기를 끄면 잡기 입력 무시");
                Assert.AreNotEqual(ActionState.Grab, w.State.Fighter0.Action);
            }
            Assert.AreNotEqual(ActionState.Held, w.State.Fighter1.Action);
            Assert.AreNotEqual(Config().ComputeChecksum(), Config(grabEnabled: false).ComputeChecksum(), "잡기 설정은 설정 체크섬에 들어간다");
        }

        /// <summary>몸 폼 봇 대전을 좌우로 뒤집어 진행해도 매 틱 뒤집힌 상태이고, 같은 입력으로 SyncTest도 통과한다.</summary>
        [TestCase("Brawl", 41u)]
        [TestCase("Brawl", 42u)]
        public void MirroredGrabFight_AndSyncTest(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            // 봇 대전은 현재 경기와 같은 몸 크기·이동(BrawlParams.Movement)으로 한다.
            var combat = BrawlParams.Combat(CombatParams.Default);
            combat.MaxHealth = 50;
            var config = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new BrawlBot(() => normal, 0, seed * 13u);
            var bot1 = new BrawlBot(() => normal, 1, seed * 29u);
            int grabs = 0, throws = 0, breaks = 0;
            for (int t = 0; t < 7200; t++)
            {
                int tick = normal.State.Tick;
                var p1 = bot0.Poll(tick);
                var p2 = bot1.Poll(tick);
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
            string report = $"[Knockbox][CP7-7] Body {map}: 잡기 {grabs}, 던지기 {throws}, 잡기 풀기 {breaks}, SyncTest 불일치 {sync.MismatchCount}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(grabs, 0, "잡기가 나와야 대칭 검증 의미가 있음");
            Assert.Greater(throws, 0);
        }
    }
}
