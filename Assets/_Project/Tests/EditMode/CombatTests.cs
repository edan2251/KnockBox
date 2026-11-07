using System;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP4-1~4: 전투 공통 규칙(03-combat.md 3절)을 테스트용 공격(AttackDef.TestJab)으로 확인한다.
    /// 기본 배치: 평평한 맵에서 P1(-0.6)과 P2(0.6)가 서로 바라본다. P1이 첫 틱에 공격을 누르면
    /// 시작 5틱 뒤 6번째 틱(ActionTick 5)에 첫 활성 판정이 P2 몸에 닿는다.
    /// </summary>
    public class CombatTests
    {
        const InputButtons A = InputButtons.Attack;
        const InputButtons G = InputButtons.Guard;
        const InputButtons J = InputButtons.Jump;
        const float Tolerance = 1e-4f;
        const float FloorY = -7.55f;
        /// <summary>P1이 첫 틱에 공격하면 명중하는 틱.</summary>
        const int HitTick = 6;

        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("CombatFlat", (c, r) => '\0');

        static SimWorld Duel(SimConfig config = null, float x0 = -0.6f, float x1 = 0.6f) =>
            TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x1, FloorY), config, Flat);

        static InputFrame Btn(InputButtons b) => new InputFrame(0, 0, b);

        static bool Has(SimWorld w, SimEventType type, int fighter)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                    return true;
            }
            return false;
        }

        static AttackDef Jab => SimConfig.CreateDefault().GetAttack(0);

        // CP4-1 ---------------------------------------------------------------

        [Test]
        public void Hitstop_FreezesBothFighters_AndKeepsInputBuffer()
        {
            var w = Duel();
            int hitstop = FighterCombat.HitstopFor(Jab.Damage, w.Config.Combat);
            Assert.AreEqual(7, hitstop, "clamp(4 + 12 × 0.25, 4, 12) = 7");

            for (int t = 1; t < HitTick; t++)
            {
                // 명중 직전 틱에 점프를 눌러 둔다(버퍼 8 → 명중 틱에 7).
                w.Step(Btn(t == 1 ? A : t == HitTick - 1 ? J : InputButtons.None), default);
                Assert.IsFalse(Has(w, SimEventType.Hit, 1), $"틱 {t}에 너무 일찍 맞음");
            }
            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.Hit, 1), "6번째 틱에 명중해야 함");
            var hit = w.State;
            Assert.AreEqual(hitstop, hit.Fighter0.HitstopTicks);
            Assert.AreEqual(hitstop, hit.Fighter1.HitstopTicks);
            Assert.AreEqual(7, hit.Fighter0.JumpBuffer);

            for (int i = 1; i <= hitstop; i++)
            {
                // 히트스탑 셋째 틱에 점프와 대시를 새로 누른다.
                w.Step(Btn(i == 3 ? J | InputButtons.Dash : InputButtons.None), default);
                var s = w.State;
                foreach (int k in new[] { 0, 1 })
                {
                    var now = s.GetFighter(k);
                    var then = hit.GetFighter(k);
                    string who = $"히트스탑 {i}틱째 P{k + 1}";
                    Assert.AreEqual(then.Position, now.Position, who + " 위치");
                    Assert.AreEqual(then.Velocity, now.Velocity, who + " 속도");
                    Assert.AreEqual(then.Action, now.Action, who + " 행동");
                    Assert.AreEqual(then.ActionTick, now.ActionTick, who + " 행동 틱");
                    Assert.AreEqual(then.StunTicks, now.StunTicks, who + " 경직");
                    Assert.AreEqual(hitstop - i, now.HitstopTicks, who + " 히트스탑");
                }
                if (i < 3)
                    Assert.AreEqual(7, s.Fighter0.JumpBuffer, "히트스탑 중 점프 버퍼가 줄면 안 됨");
                else
                    Assert.AreEqual(w.Config.Movement.JumpBufferTicks, s.Fighter0.JumpBuffer, "히트스탑 중 누른 점프가 버퍼에 남아야 함");
                if (i >= 3)
                    Assert.AreEqual(J | InputButtons.Dash, s.Fighter0.PendingPresses, "히트스탑 중 누른 버튼을 기억해야 함");
            }

            w.Step(default, default);
            Assert.AreEqual(hit.Fighter0.ActionTick + 1, w.State.Fighter0.ActionTick, "히트스탑이 끝나면 행동 타이머가 다시 간다");
            Assert.AreNotEqual(hit.Fighter1.Position, w.State.Fighter1.Position, "히트스탑이 끝나면 넉백으로 움직인다");
            Assert.AreEqual(InputButtons.None, w.State.Fighter0.PendingPresses);
        }

        [Test]
        public void PressDuringParryHitstop_ActsOnFirstTickAfter()
        {
            // P2가 패링하고 히트스탑 동안 가드를 놓고 공격을 누른다 → 히트스탑이 끝난 첫 틱에 바로 공격.
            var w = Duel();
            int parryHitstop = w.Config.Combat.ParryHitstop;
            for (int t = 1; t <= HitTick; t++)
                w.Step(Btn(t == 1 ? A : InputButtons.None), Btn(t >= HitTick - 2 ? G : InputButtons.None));
            Assert.IsTrue(Has(w, SimEventType.Parry, 1), "패링");

            for (int i = 1; i <= parryHitstop; i++)
                w.Step(default, Btn(i == 4 ? A : InputButtons.None));
            Assert.AreEqual(ActionState.Guard, w.State.Fighter1.Action, "히트스탑 동안은 그대로");

            w.Step(default, default);
            Assert.AreEqual(ActionState.Attack, w.State.Fighter1.Action, "히트스탑 직후 첫 틱에 공격");
            Assert.AreEqual(0, w.State.Fighter1.ActionTick);

            bool punished = false;
            for (int t = 0; t < Jab.TotalTicks && !punished; t++)
            {
                w.Step(default, default);
                punished = Has(w, SimEventType.Hit, 0);
            }
            Assert.IsTrue(punished, "비틀거리는 P1을 맞혀야 함");
        }

        // CP4-2 ---------------------------------------------------------------

        [Test]
        public void Knockback_ReplacesVelocity()
        {
            var w = Duel(null, -0.6f, 2.5f);
            // P2가 P1 쪽으로 최고 속도로 달려오다 맞는다. 맞은 틱의 속도는 이전 속도와 무관하게 방향 × 넉백.
            bool hit = false;
            for (int t = 1; t <= 40 && !hit; t++)
            {
                w.Step(Btn(t == 8 ? A : InputButtons.None), new InputFrame(-1, 0, InputButtons.None));
                hit = Has(w, SimEventType.Hit, 1);
            }
            Assert.IsTrue(hit, "명중하지 않음");
            var dir = SimMath.Normalize(Jab.KnockbackDirection);
            Assert.AreEqual(dir.X * Jab.KnockbackStrength, w.State.Fighter1.Velocity.X, Tolerance, "vx = 방향 × 넉백(앞쪽 = 오른쪽)");
            Assert.AreEqual(dir.Y * Jab.KnockbackStrength, w.State.Fighter1.Velocity.Y, Tolerance, "vy = 방향 × 넉백");
            Assert.AreEqual(ActionState.Hitstun, w.State.Fighter1.Action);
            Assert.AreEqual(100 - Jab.Damage, w.State.Fighter1.Health);
        }

        [TestCase(14f, 0, 15)]   // round(15.4)
        [TestCase(5f, 0, 8)]     // 최소 8
        [TestCase(50f, 0, 45)]   // 최대 45
        [TestCase(11.9f, 1, 10)] // round(13.09) = 13, × 0.8 = 10.4
        [TestCase(10.115f, 2, 7)] // round(11.1265) = 11, × 0.64 = 7.04
        public void HitstunFormula(float knockback, int comboIndex, int expected)
        {
            Assert.AreEqual(expected, FighterCombat.HitstunFor(knockback, comboIndex, CombatParams.Default));
        }

        [TestCase(0, 14f, 15)]
        [TestCase(1, 11.9f, 10)]
        [TestCase(2, 10.115f, 7)]
        public void ComboDecay_InWorld(int previousHits, float expectedKnockback, int expectedStun)
        {
            var w = Duel();
            if (previousHits > 0)
            {
                w.State.Fighter1.RestartAction(ActionState.Hitstun);
                w.State.Fighter1.StunTicks = 40;
                w.State.Fighter1.ComboHits = previousHits;
            }
            for (int t = 1; t <= HitTick; t++)
                w.Step(Btn(t == 1 ? A : InputButtons.None), default);
            Assert.IsTrue(Has(w, SimEventType.Hit, 1));
            var dir = SimMath.Normalize(Jab.KnockbackDirection);
            Assert.AreEqual(dir.X * expectedKnockback, w.State.Fighter1.Velocity.X, 1e-3f, "넉백 × 0.85^n");
            Assert.AreEqual(expectedStun, w.State.Fighter1.StunTicks, "경직 × 0.8^n");
            Assert.AreEqual(previousHits + 1, w.State.Fighter1.ComboHits);
        }

        [Test]
        public void ComboCap_FourthHitDoesNotLand()
        {
            var w = Duel();
            w.State.Fighter1.RestartAction(ActionState.Hitstun);
            w.State.Fighter1.StunTicks = 40;
            w.State.Fighter1.ComboHits = w.Config.Combat.MaxComboHits;
            for (int t = 1; t <= Jab.TotalTicks; t++)
            {
                w.Step(Btn(t == 1 ? A : InputButtons.None), default);
                Assert.IsFalse(Has(w, SimEventType.Hit, 1), $"틱 {t}: 3회 맞은 상대에게 4번째가 들어감");
            }
            Assert.AreEqual(100, w.State.Fighter1.Health);
        }

        /// <summary>공격하는 쪽: 상대 쪽으로 걸으며 period틱마다 공격을 한 틱 누른다. jumpEvery가 0보다 크면 그 간격으로 점프.</summary>
        static InputFrame Masher(in FighterState self, in FighterState other, int tick, int period, int jumpEvery)
        {
            int toward = SimMath.Sign(other.Position.X - self.Position.X);
            var buttons = tick % period == 0 ? A : InputButtons.None;
            if (jumpEvery > 0 && tick % jumpEvery == 0)
                buttons |= J;
            return new InputFrame(toward, 0, buttons);
        }

        static AttackDef Heavy
        {
            get
            {
                var a = AttackDef.TestJab;
                a.KnockbackStrength = 24f;
                a.KnockbackDirection = new Vec2(1f, 0.15f);
                return a;
            }
        }

        /// <summary>같은 공격을 여러 간격과 위치에서 반복해 한 번에 이어진 최대 연속 명중 수를 센다.</summary>
        static int MaxConsecutiveHits(CombatParams combat, out int runs)
        {
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Test);
            int max = 0;
            runs = 0;
            float[][] spots =
            {
                new[] { 12.6f, 13.9f },   // 오른쪽 벽에 몰림
                new[] { -1.0f, 0.3f },    // 가운데
                new[] { -13.9f, -12.6f }, // 왼쪽 벽에 몰림(공격자가 오른쪽)
            };
            foreach (var spot in spots)
            {
                for (int period = 2; period <= 26; period++)
                {
                    foreach (int jumpEvery in new[] { 0, 29 })
                    {
                        runs++;
                        bool victimOnLeft = spot[0] < 0f && spot[1] < 0f;
                        var w = victimOnLeft
                            ? TestWorlds.CreateAt(new Vec2(spot[1], FloorY), new Vec2(spot[0], FloorY), config, Flat)
                            : TestWorlds.CreateAt(new Vec2(spot[0], FloorY), new Vec2(spot[1], FloorY), config, Flat);
                        for (int t = 0; t < 600 && w.State.Match.Phase == MatchPhase.Fighting; t++)
                        {
                            var p1 = Masher(w.State.Fighter0, w.State.Fighter1, t, period, jumpEvery);
                            w.Step(p1, default);
                            if (Has(w, SimEventType.Hit, 1))
                                max = Math.Max(max, w.State.Fighter1.ComboHits);
                        }
                    }
                }
            }
            return max;
        }

        [Test]
        public void SameAttackRepeated_NeverHitsFourTimesInARow()
        {
            var jab = CombatParams.Default;
            var heavy = CombatParams.Default;
            heavy.TestAttack = Heavy;
            int jabMax = MaxConsecutiveHits(jab, out int runs);
            int heavyMax = MaxConsecutiveHits(heavy, out _);

            // 상한 없이 감쇠만으로는 몇 번까지 이어지는지(기록용).
            var jabNoCap = jab;
            jabNoCap.MaxComboHits = 99;
            var heavyNoCap = heavy;
            heavyNoCap.MaxComboHits = 99;
            int jabDecayOnly = MaxConsecutiveHits(jabNoCap, out _);
            int heavyDecayOnly = MaxConsecutiveHits(heavyNoCap, out _);

            string report = $"[Knockbox][CP4-2] 연속 명중 최대: 테스트 공격 {jabMax}, 강한 넉백 변형 {heavyMax} (각 {runs}가지). " +
                            $"상한 없이 감쇠만: {jabDecayOnly}, {heavyDecayOnly}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.LessOrEqual(jabMax, 3);
            Assert.LessOrEqual(heavyMax, 3);
            Assert.GreaterOrEqual(Math.Max(jabMax, heavyMax), 2, "연속 명중이 한 번도 나오지 않으면 이 테스트가 감쇠를 시험하지 못한 것");
        }

        // CP4-3 ---------------------------------------------------------------

        /// <summary>피격 경직 중인 P2를 원하는 위치·속도로 둔 월드. P1은 멀리 있다.</summary>
        static SimWorld Flying(Vec2 position, Vec2 velocity)
        {
            var w = TestWorlds.CreateAt(new Vec2(-12f, FloorY), position, null, Flat);
            w.State.Fighter1.RestartAction(ActionState.Hitstun);
            w.State.Fighter1.StunTicks = 30;
            w.State.Fighter1.ComboHits = 1;
            w.State.Fighter1.Velocity = velocity;
            w.State.Fighter1.Grounded = position.Y <= FloorY + Tolerance;
            return w;
        }

        static void AssertRebound(SimWorld w, bool splat, Vec2 expectedVelocity, string label)
        {
            var c = w.Config.Combat;
            var f = w.State.Fighter1;
            Assert.AreEqual(splat, Has(w, SimEventType.WallSplat, 1), label + " 벽꽝 이벤트");
            Assert.AreEqual(!splat, Has(w, SimEventType.BounceRebound, 1), label + " 튕김 이벤트");
            Assert.AreEqual(splat ? 100 - c.WallSplatDamage : 100, f.Health, label + " 데미지");
            Assert.AreEqual(splat ? 29 + c.WallSplatHitstun : 29, f.StunTicks, label + " 경직(+18)");
            Assert.AreEqual(splat ? c.WallSplatHitstop : 0, f.HitstopTicks, label + " 히트스탑");
            Assert.AreEqual(expectedVelocity.X, f.Velocity.X, 1e-3f, label + " vx");
            Assert.AreEqual(expectedVelocity.Y, f.Velocity.Y, 1e-3f, label + " vy");
        }

        [TestCase(14f, true)]
        [TestCase(13.9f, false)]
        [TestCase(20f, true)]
        [TestCase(8f, false)]
        public void WallSplatOrBounce_Wall(float speed, bool splat)
        {
            var m = MovementParams.Default;
            float vy = -m.RiseGravity * m.FallGravityMultiplier * SimConfig.Dt;
            var w = Flying(new Vec2(14.45f, 0f), new Vec2(speed, 0f));
            w.Step(default, default);
            float r = splat ? CombatParams.Default.WallSplatRestitution : CombatParams.Default.BounceRestitution;
            AssertRebound(w, splat, new Vec2(-speed * r, vy * r), $"벽 {speed}");
            Assert.AreEqual(new Vec2(-1f, 0f), FindEvent(w, splat ? SimEventType.WallSplat : SimEventType.BounceRebound).Direction, "법선");

            // 같은 왼쪽 벽도 대칭으로.
            var left = Flying(new Vec2(-14.45f, 0f), new Vec2(-speed, 0f));
            left.Step(default, default);
            AssertRebound(left, splat, new Vec2(speed * r, vy * r), $"왼쪽 벽 {speed}");
        }

        [TestCase(-14.5f, true)]
        [TestCase(-11f, false)]
        public void WallSplatOrBounce_Floor(float startVy, bool splat)
        {
            var m = MovementParams.Default;
            float impact = -(startVy - m.RiseGravity * m.FallGravityMultiplier * SimConfig.Dt);
            var w = Flying(new Vec2(0f, -7.4f), new Vec2(3f, startVy));
            w.Step(default, default);
            float r = splat ? CombatParams.Default.WallSplatRestitution : CombatParams.Default.BounceRestitution;
            Assert.AreEqual(splat, impact >= 14f, "테스트 배치 확인");
            AssertRebound(w, splat, new Vec2(3f * r, impact * r), $"바닥 {impact}");
            Assert.IsFalse(w.State.Fighter1.Grounded, "튕겨 올라야 함");
        }

        [TestCase(16f, true)]
        [TestCase(13f, false)]
        public void WallSplatOrBounce_Ceiling(float startVy, bool splat)
        {
            var m = MovementParams.Default;
            float impact = startVy - m.RiseGravity * SimConfig.Dt;
            var w = Flying(new Vec2(0f, 7.4f), new Vec2(0f, startVy));
            w.Step(default, default);
            float r = splat ? CombatParams.Default.WallSplatRestitution : CombatParams.Default.BounceRestitution;
            Assert.AreEqual(splat, impact >= 14f, "테스트 배치 확인");
            AssertRebound(w, splat, new Vec2(0f, -impact * r), $"천장 {impact}");
        }

        [Test]
        public void WallSplat_OnlyWhileInHitstun()
        {
            var w = Flying(new Vec2(14.45f, 0f), new Vec2(20f, 0f));
            w.State.Fighter1.RestartAction(ActionState.Airborne);
            w.Step(default, default);
            Assert.IsFalse(Has(w, SimEventType.WallSplat, 1));
            Assert.IsFalse(Has(w, SimEventType.BounceRebound, 1));
            Assert.AreEqual(100, w.State.Fighter1.Health);
            Assert.AreEqual(0f, w.State.Fighter1.Velocity.X, "조작 가능한 상태는 벽에 그냥 멈춤");
        }

        static SimEvent FindEvent(SimWorld w, SimEventType type)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type)
                    return w.Events[i];
            }
            Assert.Fail($"{type} 이벤트 없음");
            return default;
        }

        // CP4-4 ---------------------------------------------------------------

        /// <summary>
        /// P1이 10번째 틱에 공격하고(15번째 틱 명중), P2가 명중 틱보다 lead틱 먼저 가드를 눌러 계속 누르고 있을 때.
        /// 누른 틱을 1번째로 세면 명중은 lead + 1번째 틱이다.
        /// </summary>
        static SimWorld GuardWithLead(int lead)
        {
            var w = Duel();
            const int attackTick = 10;
            int hitTick = attackTick + HitTick - 1;
            int pressTick = hitTick - lead;
            for (int t = 1; t <= hitTick; t++)
                w.Step(Btn(t == attackTick ? A : InputButtons.None), Btn(t >= pressTick ? G : InputButtons.None));
            return w;
        }

        [Test]
        public void ParryWindow_IsExactlySixTicks()
        {
            Assert.AreEqual(6, CombatParams.Default.ParryWindowTicks);
            for (int lead = 0; lead <= 8; lead++)
            {
                var w = GuardWithLead(lead);
                bool parry = lead < 6;
                Assert.AreEqual(parry, Has(w, SimEventType.Parry, 1), $"누른 틱부터 {lead + 1}번째 틱에 맞음 → {(parry ? "패링" : "가드")}");
                Assert.AreEqual(!parry, Has(w, SimEventType.GuardHit, 1), $"{lead + 1}번째 틱");
            }
        }

        [TestCase(23, false)]
        [TestCase(24, true)]
        [TestCase(40, true)]
        public void ParryCooldown_24TicksSincePreviousGuardPress(int gap, bool expectParry)
        {
            var w = Duel();
            int secondPress = 1 + gap;
            int attackPress = secondPress - (HitTick - 1);
            for (int t = 1; t <= secondPress; t++)
            {
                var p1 = Btn(t == attackPress ? A : InputButtons.None);
                var p2 = Btn(t == 1 || t >= secondPress ? G : InputButtons.None);
                w.Step(p1, p2);
            }
            Assert.AreEqual(expectParry, Has(w, SimEventType.Parry, 1), $"간격 {gap}");
            Assert.AreEqual(!expectParry, Has(w, SimEventType.GuardHit, 1), $"간격 {gap}");
        }

        [Test]
        public void GuardHit_ReducesDamageAndKnockback()
        {
            var w = GuardWithLead(10);
            var c = w.Config.Combat;
            Assert.IsTrue(Has(w, SimEventType.GuardHit, 1));
            var f = w.State.Fighter1;
            Assert.AreEqual(100 - 2, f.Health, "데미지 12 × 0.2 = 2.4 → 2");
            var dir = SimMath.Normalize(Jab.KnockbackDirection);
            Assert.AreEqual(dir.X * Jab.KnockbackStrength * c.GuardKnockbackMultiplier, f.Velocity.X, Tolerance, "넉백 × 0.35");
            Assert.AreEqual(0f, f.Velocity.Y, "수평만");
            Assert.AreEqual(ActionState.GuardStun, f.Action);
            Assert.AreEqual(c.GuardStunTicks, f.StunTicks);
            Assert.AreEqual(4, f.HitstopTicks, "히트스탑 7의 절반(3.5 → 4)");
            Assert.AreEqual(4, w.State.Fighter0.HitstopTicks);
        }

        [Test]
        public void Parry_StaggersAttacker()
        {
            var w = GuardWithLead(2);
            var c = w.Config.Combat;
            Assert.IsTrue(Has(w, SimEventType.Parry, 1));
            var attacker = w.State.Fighter0;
            Assert.AreEqual(ActionState.Stagger, attacker.Action);
            Assert.AreEqual(c.StaggerTicks, attacker.StunTicks);
            Assert.AreEqual(-c.StaggerPushSpeed, attacker.Velocity.X, "P2 반대쪽(왼쪽)으로 8");
            Assert.AreEqual(c.ParryHitstop, attacker.HitstopTicks);
            Assert.AreEqual(c.ParryHitstop, w.State.Fighter1.HitstopTicks);
            Assert.AreEqual(100, w.State.Fighter1.Health, "패링은 데미지 없음");
            Assert.AreEqual(0, w.State.Fighter1.ParryTicks);
        }

        [Test]
        public void Guard_LimitsSpeedAndBlocksJumpDashAttack()
        {
            var w = Duel(null, -6f, 6f);
            for (int t = 0; t < 60; t++)
            {
                var buttons = G | (t % 2 == 0 ? J | InputButtons.Dash | A : InputButtons.None);
                w.Step(new InputFrame(1, 0, buttons), default);
                Assert.AreEqual(ActionState.Guard, w.State.Fighter0.Action, $"틱 {t}");
                Assert.LessOrEqual(w.State.Fighter0.Velocity.X, w.Config.Combat.GuardMaxSpeed + Tolerance);
                Assert.IsTrue(w.State.Fighter0.Grounded, "가드 중 점프 불가");
            }
            Assert.AreEqual(w.Config.Combat.GuardMaxSpeed, w.State.Fighter0.Velocity.X, Tolerance);
        }

        // 클래시 --------------------------------------------------------------

        [Test]
        public void SimultaneousAttacks_Clash()
        {
            var w = Duel();
            var c = w.Config.Combat;
            for (int t = 1; t <= HitTick; t++)
                w.Step(Btn(t == 1 ? A : InputButtons.None), Btn(t == 1 ? A : InputButtons.None));
            Assert.IsTrue(Has(w, SimEventType.Clash, SimEvent.NoFighter), "동시에 내민 공격은 클래시");
            Assert.IsFalse(Has(w, SimEventType.Hit, 0) || Has(w, SimEventType.Hit, 1), "클래시는 피해 없음");
            foreach (int k in new[] { 0, 1 })
            {
                var f = w.State.GetFighter(k);
                Assert.AreEqual(ActionState.Recoil, f.Action);
                Assert.AreEqual(c.RecoilTicks, f.StunTicks);
                Assert.AreEqual(c.ClashHitstop, f.HitstopTicks);
                Assert.AreEqual(100, f.Health);
            }
            Assert.AreEqual(-c.ClashSpeed, w.State.Fighter0.Velocity.X, Tolerance, "접촉 반대 방향으로 8");
            Assert.AreEqual(c.ClashSpeed, w.State.Fighter1.Velocity.X, Tolerance);
        }
    }
}
