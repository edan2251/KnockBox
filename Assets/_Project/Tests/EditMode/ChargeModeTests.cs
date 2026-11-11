using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    // 2026-11-11 폼 전투 제거: 가위 폼 입력(충전·발사)과 대시 몸통 공격 검사는 지웠다. 몸 폼의 충전·발사는 BrawlLungeTests·BrawlBodyTests·BrawlChargeInputTests가 맡고,
    // 이 파일은 상태로 만든 돌진의 공통 판정(위력, 클래시, 피해, 넉백, 종료)을 몸 폼 규칙(RuleSet.Brawl)과 기본 돌진 수치로 확인한다.
    /// <summary>CP5-1~7: 모드 A 돌진 충돌(03-combat.md 4절).</summary>
    public class ChargeModeTests
    {
        const InputButtons A = InputButtons.Attack;
        const InputButtons G = InputButtons.Guard;
        const float Tolerance = 1e-3f;
        const float FloorY = -7.55f;

        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("ChargeFlat", (c, r) => '\0');

        static readonly ChargeModeParams P = ChargeModeParams.Default;

        static SimConfig ChargeConfig(System.Func<CombatParams, CombatParams> change = null)
        {
            var combat = CombatParams.Default;
            if (change != null)
                combat = change(combat);
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, RuleSet.Brawl);
        }

        static SimWorld World(float x0, float x1, SimConfig config = null, ArenaDef arena = null, float y0 = FloorY, float y1 = FloorY) =>
            TestWorlds.CreateAt(new Vec2(x0, y0), new Vec2(x1, y1), config ?? ChargeConfig(), arena ?? Flat);

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

        /// <summary>P1을 바로 돌진 상태로 만든다(지상이면 중력 없이 미끄러진다).</summary>
        static void SetLunge(ref FighterState f, Vec2 velocity)
        {
            f.RestartAction(ActionState.Lunge);
            f.Velocity = velocity;
            f.LungeBounces = 0;
        }

        // CP5-1 ---------------------------------------------------------------

        // CP5-2 ---------------------------------------------------------------

        [TestCase(5f, 0f, BodyOutcome.None)]
        [TestCase(5.9f, 5.9f, BodyOutcome.None)]
        [TestCase(6f, 0f, BodyOutcome.FirstWins)]
        [TestCase(0f, 6f, BodyOutcome.SecondWins)]
        [TestCase(20f, 16.1f, BodyOutcome.Clash)]
        [TestCase(20f, 16f, BodyOutcome.FirstWins)]
        [TestCase(6f, 9.9f, BodyOutcome.Clash)]
        [TestCase(6f, 10f, BodyOutcome.SecondWins)]
        [TestCase(18f, 18f, BodyOutcome.Clash)]
        [TestCase(5f, 8f, BodyOutcome.SecondWins)]
        public void PowerJudgement(float powerA, float powerB, BodyOutcome expected)
        {
            Assert.AreEqual(expected, FighterCombat.JudgeBodies(powerA, powerB, P));
        }

        [Test]
        public void HeadOnLunges_SimilarPower_Clash()
        {
            var w = World(-0.5f, 0.5f);
            SetLunge(ref w.State.Fighter0, new Vec2(20f, 0f));
            SetLunge(ref w.State.Fighter1, new Vec2(-18f, 0f));
            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.Clash, SimEvent.NoFighter), "위력 20 대 18 → 클래시");
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter0.Action);
            Assert.AreEqual(ActionState.Recoil, w.State.Fighter1.Action);
            Assert.AreEqual(100, w.State.Fighter0.Health);
            Assert.AreEqual(100, w.State.Fighter1.Health);
            float speed = System.Math.Max(8f, 20f * P.ClashSpeedPerPower);
            Assert.AreEqual(-speed, w.State.Fighter0.Velocity.X, Tolerance, "반대로 튕김(8 이상)");
            Assert.AreEqual(speed, w.State.Fighter1.Velocity.X, Tolerance);
        }

        [Test]
        public void HeadOnLunges_StrongerWins()
        {
            var w = World(-0.5f, 0.5f);
            SetLunge(ref w.State.Fighter0, new Vec2(26f, 0f));
            SetLunge(ref w.State.Fighter1, new Vec2(-18f, 0f));
            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.Hit, 1), "26 대 18 → P1 승리");
            Assert.AreEqual(ActionState.Hitstun, w.State.Fighter1.Action);
            Assert.AreEqual(100, w.State.Fighter0.Health);
        }

        [Test]
        public void GlancingLunge_BelowThreshold_OnlyPushes()
        {
            // 둘이 함께 위로 날아가는데 P1만 옆으로 5씩 다가간다. 법선이 수평이라 위력 5 → 기준 미만, 밀어내기만.
            var w = World(-0.95f, 0f, null, null, 0f, 0f);
            SetLunge(ref w.State.Fighter0, new Vec2(5f, 12f));
            SetLunge(ref w.State.Fighter1, new Vec2(0f, 12f));
            w.State.Fighter0.Grounded = false;
            w.State.Fighter1.Grounded = false;
            bool checkedPower = false;
            for (int t = 0; t < 20; t++)
            {
                w.Step(default, default);
                Assert.IsFalse(Has(w, SimEventType.Hit, 1), $"틱 {t}");
                Assert.IsFalse(Has(w, SimEventType.Clash, SimEvent.NoFighter), $"틱 {t}");
                for (int i = 0; i < w.Events.Count; i++)
                {
                    if (w.Events[i].Type == SimEventType.PowerCheck && w.Events[i].Fighter == 0)
                    {
                        checkedPower = true;
                        Assert.Less(w.Events[i].Strength, P.PowerThreshold);
                    }
                }
            }
            Assert.IsTrue(checkedPower, "몸이 닿아 위력을 계산해야 함");
            Assert.AreEqual(100, w.State.Fighter1.Health);
        }

        // CP5-3 ---------------------------------------------------------------

        [TestCase(6.5f, 4)]
        [TestCase(10f, 6)]
        [TestCase(30f, 18)]
        [TestCase(40f, 24)]
        [TestCase(60f, 24)]
        public void Damage_IsClampedFourToTwentyFour(float power, int expected)
        {
            FighterCombat.BodyStrikeValues(new Vec2(1f, 0f), new Vec2(1f, 0f), power, P, out int damage, out _, out _);
            Assert.AreEqual(expected, damage);
        }

        [Test]
        public void KnockbackDirection_MixesVelocityAndNormal()
        {
            var velocity = new Vec2(17f, 17f);
            var normal = new Vec2(1f, 0f);
            FighterCombat.BodyStrikeValues(velocity, normal, 17f, P, out _, out var direction, out float knockback);
            float vx = 1f / SimMath.Sqrt(2f);
            var mix = new Vec2(0.5f * vx + 0.5f, 0.5f * vx);
            float len = SimMath.Sqrt(mix.LengthSquared);
            Assert.AreEqual(mix.X / len, direction.X, 1e-4f);
            Assert.AreEqual(mix.Y / len, direction.Y, 1e-4f);
            Assert.AreEqual(17f * 0.9f, knockback, 1e-4f, "max(10, 위력 × 0.9)");

            FighterCombat.BodyStrikeValues(velocity, normal, 7f, P, out _, out _, out float weak);
            Assert.AreEqual(10f, weak, "최소 넉백 10");
        }

        [Test]
        public void Winner_ReboundsAndStopsLunging()
        {
            var w = World(-0.6f, 0.4f);
            SetLunge(ref w.State.Fighter0, new Vec2(30f, 0f));
            w.Step(default, default);
            Assert.IsTrue(Has(w, SimEventType.Hit, 1));
            Assert.AreEqual(100 - 18, w.State.Fighter1.Health, "위력 30 → 데미지 18");
            Assert.AreEqual(27f, w.State.Fighter1.Velocity.X, Tolerance, "정면이면 방향 (1, 0), 세기 27");
            Assert.AreEqual(-7.5f, w.State.Fighter0.Velocity.X, Tolerance, "이긴 쪽 속도 × -0.25");
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action, "돌진 종료");
            Assert.AreEqual(P.ContactCooldownTicks, w.State.Fighter0.ContactCooldown);
            Assert.AreEqual(P.ContactCooldownTicks, w.State.Fighter1.ContactCooldown);
        }

        [Test]
        public void SamePair_JudgedOncePerTenTicks()
        {
            var w = World(-0.6f, 0.4f);
            SetLunge(ref w.State.Fighter0, new Vec2(30f, 0f));
            w.Step(default, default);
            int hitstop = w.State.Fighter0.HitstopTicks;
            for (int i = 0; i < hitstop; i++)
                w.Step(default, default);
            // 쿨다운 동안 다시 돌진해 부딪혀도 판정하지 않는다.
            w.State.Fighter1.RestartAction(ActionState.Idle);
            w.State.Fighter1.Position = new Vec2(w.State.Fighter0.Position.X + 0.95f, FloorY);
            w.State.Fighter1.Velocity = Vec2.Zero;
            SetLunge(ref w.State.Fighter0, new Vec2(30f, 0f));
            int before = w.State.Fighter1.Health;
            w.Step(default, default);
            Assert.Greater(w.State.Fighter0.ContactCooldown, 0);
            Assert.IsFalse(Has(w, SimEventType.Hit, 1), "10틱 안에는 같은 두 파이터를 다시 판정하지 않음");
            Assert.AreEqual(before, w.State.Fighter1.Health);
        }

        // CP5-4 ---------------------------------------------------------------

        static ArenaDef corridor;
        /// <summary>x = -4..-3과 0..1에 벽이 있는 폭 3의 통로.</summary>
        static ArenaDef Corridor => corridor ??= MovementTests.Build("Corridor", (c, r) => c == 12 || c == 16 ? '#' : '\0');

        [Test]
        public void TerrainBounce_AtMostTwice_ThenEnds()
        {
            var w = World(-2.5f, 12f, null, Corridor, 0f, FloorY);
            SetLunge(ref w.State.Fighter0, new Vec2(34f, 0f));
            w.State.Fighter0.Grounded = false;
            int bounces = 0;
            bool ended = false;
            for (int t = 0; t < 40 && !ended; t++)
            {
                float before = SimMath.Abs(w.State.Fighter0.Velocity.X);
                w.Step(default, default);
                if (Has(w, SimEventType.BounceRebound, 0))
                {
                    bounces++;
                    Assert.AreEqual(before * P.LungeBounceRestitution, SimMath.Abs(w.State.Fighter0.Velocity.X), 0.05f, $"{bounces}번째 튕김 × 0.6");
                }
                ended = w.State.Fighter0.Action != ActionState.Lunge;
            }
            Assert.IsTrue(ended, "돌진이 끝나야 함");
            Assert.AreEqual(2, bounces, "34 → 20.4 → 12.24: 두 번 튕긴 뒤 세 번째에 끝남");
            Assert.AreEqual(2, w.State.Fighter0.LungeBounces);
        }

        [Test]
        public void Lunge_EndsBelowSpeed12()
        {
            var w = World(-12f, 12f, null, null, 0f, FloorY);
            SetLunge(ref w.State.Fighter0, new Vec2(0f, 18f));
            w.State.Fighter0.Grounded = false;
            int ticks = 0;
            while (w.State.Fighter0.Action == ActionState.Lunge && ticks < 40)
            {
                w.Step(default, default);
                ticks++;
            }
            Assert.Less(ticks, P.LungeMaxTicks, "30틱 전에 속도 때문에 끝나야 함");
            Assert.Less(SimMath.Length(w.State.Fighter0.Velocity), 12f + 1.5f);
        }

        [Test]
        public void Lunge_EndsAfter30Ticks()
        {
            var w = World(-14f, 12f);
            w.Step(In(0, 0, A), default);
            w.Step(default, default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            for (int t = 1; t < P.LungeMaxTicks; t++)
            {
                w.Step(default, default);
                Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, $"돌진 {t}틱째");
            }
            w.Step(default, default);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action, "30틱째에 끝남");
        }

        // CP5-5 ---------------------------------------------------------------

        // CP5-6 ---------------------------------------------------------------

        [Test]
        public void OrdinaryMovement_NeverDamages()
        {
            var w = World(-3f, 0f);
            for (int t = 0; t < 180; t++)
            {
                var buttons = t % 40 == 20 ? InputButtons.Jump : InputButtons.None;
                w.Step(In(1, 0, buttons), In(-1, 0, InputButtons.None));
                Assert.IsFalse(Has(w, SimEventType.Hit, 0) || Has(w, SimEventType.Hit, 1), $"틱 {t}: 달리기·점프로 피해가 생김");
            }
            Assert.AreEqual(100, w.State.Fighter0.Health);
            Assert.AreEqual(100, w.State.Fighter1.Health);
        }

        // CP5-7 ---------------------------------------------------------------


        // 돌진 대 주먹(2026-11-11 폼 전투 제거 전에는 가위 돌진 대 보 휘두르기, FormInteractionTests) ----------

        /// <summary>P2가 pressTick에 주먹을 내밀고, P1은 처음부터 speed로 돌진 중. 활성 주먹과 돌진이 만난 결과를 센다.</summary>
        static SimWorld LungeVsPunch(SimConfig config, int pressTick, float speed, out int interrupts1, out int hits0, out int hits1)
        {
            var w = World(-2.5f, 0.5f, config);
            var s = w.State;
            SetLunge(ref s.Fighter0, new Vec2(speed, 0f));
            w.State = s;
            interrupts1 = hits0 = hits1 = 0;
            for (int t = 0; t < 40; t++)
            {
                w.Step(default, t == pressTick ? In(-1, 0, InputButtons.Attack) : default);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    var e = w.Events[i];
                    if (e.Type == SimEventType.AttackInterrupted && e.Fighter == 1) interrupts1++;
                    if (e.Type == SimEventType.Hit && e.Fighter == 0) hits0++;
                    if (e.Type == SimEventType.Hit && e.Fighter == 1) hits1++;
                }
            }
            return w;
        }

        [Test]
        public void Lunge_IntoActivePunch_AbovePowerThreshold_LungeWins_BelowThreshold_PunchHits()
        {
            foreach (bool strong in new[] { true, false })
            {
                var combat = BrawlParams.Combat(CombatParams.Default);
                float speed = combat.Charge.LungeMaxSpeed;
                // 약한 경우는 돌진 공격성을 낮춰 위력이 임계 아래가 되게 한다(데이터).
                if (!strong)
                    combat.Charge.LungeAggression = (combat.Charge.PowerThreshold - 1f) / speed;
                var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, RuleSet.Brawl);
                Assert.AreEqual(strong, speed * config.Combat.Charge.LungeAggression >= config.Combat.Charge.PowerThreshold);
                bool found = false;
                // 주먹을 누르는 틱을 바꿔 가며, 돌진이 활성 판정에 닿는 경기를 찾는다.
                for (int press = 0; press < 14 && !found; press++)
                {
                    var w = LungeVsPunch(config, press, speed, out int interrupts1, out int hits0, out int hits1);
                    if (strong && interrupts1 > 0)
                    {
                        found = true;
                        Assert.AreEqual(1, hits1, $"누름 {press}: 주먹을 낸 쪽이 피해");
                        Assert.AreEqual(0, hits0, $"누름 {press}: 돌진은 맞지 않음");
                        Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter0.Health);
                    }
                    else if (!strong && hits0 > 0)
                    {
                        found = true;
                        Assert.AreEqual(0, interrupts1, $"누름 {press}: 임계 미만이면 중단 없음");
                        Assert.AreEqual(0, hits1, $"누름 {press}: 주먹을 낸 쪽은 맞지 않음");
                    }
                }
                Assert.IsTrue(found, strong ? "돌진이 이기는 경기" : "주먹이 맞는 경기");
            }
        }
    }
}
