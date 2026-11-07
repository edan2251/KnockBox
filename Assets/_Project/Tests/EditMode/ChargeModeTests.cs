using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
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
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Charge);
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

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(10)]
        [TestCase(18)]
        [TestCase(27)]
        [TestCase(36)]
        [TestCase(50)]
        public void LaunchSpeed_IsLinearInChargeRatio(int heldTicks)
        {
            var w = World(-12f, 12f);
            for (int t = 1; t <= heldTicks; t++)
            {
                w.Step(In(0, 0, A), default);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action, $"{t}틱째 충전 중");
            }
            w.Step(default, default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "떼면 발사");
            float ratio = heldTicks < 4 ? 0f : System.Math.Min(heldTicks, 36) / 36f;
            float expected = 18f + 16f * ratio;
            Assert.AreEqual(expected, w.State.Fighter0.Velocity.X, Tolerance, $"{heldTicks}틱 충전 → {expected}");
            Assert.AreEqual(0f, w.State.Fighter0.Velocity.Y, Tolerance);
            Assert.That(w.State.Fighter0.Velocity.X, Is.InRange(18f - Tolerance, 34f + Tolerance));
        }

        [Test]
        public void FullCharge_AutoLaunchesAfter24MoreTicks()
        {
            var w = World(-12f, 12f);
            int auto = P.FullChargeTicks + P.FullChargeHoldTicks;
            for (int t = 1; t <= auto; t++)
            {
                w.Step(In(0, 0, A), default);
                Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action, $"{t}틱째");
            }
            w.Step(In(0, 0, A), default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "최대 충전 뒤 24틱이 지나면 누르고 있어도 발사");
            Assert.AreEqual(P.LungeMaxSpeed, w.State.Fighter0.Velocity.X, Tolerance);
        }

        [Test]
        public void Launch_UsesEightWayInput_NeutralUsesFacing()
        {
            var w = World(-12f, 12f);
            for (int t = 0; t < 10; t++)
                w.Step(In(0, 0, A), default);
            w.Step(In(-1, 1, InputButtons.None), default);
            float s = P.LungeSpeed(10) / SimMath.Sqrt(2f);
            float gravity = MovementParams.Default.RiseGravity * P.LungeGravityMultiplier * SimConfig.Dt;
            Assert.AreEqual(-s, w.State.Fighter0.Velocity.X, Tolerance, "왼쪽 위 대각선");
            Assert.AreEqual(s - gravity, w.State.Fighter0.Velocity.Y, Tolerance, "공중이면 발사한 틱부터 중력 × 0.3");
            Assert.AreEqual(-1, w.State.Fighter0.Facing, "발사 쪽을 바라봄");

            var n = World(12f, -12f);
            n.Step(In(0, 0, A), default);
            n.Step(default, default);
            Assert.AreEqual(-P.LungeMinSpeed, n.State.Fighter0.Velocity.X, Tolerance, "중립이면 바라보는 쪽(왼쪽)");
        }

        [Test]
        public void Charging_LimitsSpeedAndFloatsInAir()
        {
            var w = World(-12f, 12f);
            for (int t = 0; t < 40; t++)
                w.Step(In(1, 0, A), default);
            Assert.AreEqual(P.ChargeMoveMaxSpeed, w.State.Fighter0.Velocity.X, Tolerance, "충전 중 지상 최대 속도 3");

            var air = World(-12f, 12f, null, null, 4f);
            for (int t = 0; t < 40; t++)
            {
                air.Step(In(0, 0, A), default);
                Assert.GreaterOrEqual(air.State.Fighter0.Velocity.Y, -P.ChargeFallMaxSpeed - Tolerance, "공중 낙하 최대 4");
            }
        }

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

        [Test]
        public void LungeIntoParry_ReflectsAndStaggers()
        {
            var w = World(-1.6f, 0.4f);
            w.Step(default, In(0, 0, G));
            SetLunge(ref w.State.Fighter0, new Vec2(20f, 0f));
            bool parried = false;
            for (int t = 0; t < 6 && !parried; t++)
            {
                w.Step(default, In(0, 0, G));
                parried = Has(w, SimEventType.Parry, 1);
            }
            Assert.IsTrue(parried, "패링 구간 안에 부딪힘");
            var c = CombatParams.Default;
            Assert.AreEqual(ActionState.Stagger, w.State.Fighter0.Action);
            Assert.AreEqual(-20f * P.ParryReflectScale, w.State.Fighter0.Velocity.X, Tolerance, "접촉 법선 기준 반사 × 0.8");
            Assert.AreEqual(c.ParryHitstop, w.State.Fighter0.HitstopTicks);
            Assert.AreEqual(100, w.State.Fighter1.Health);
        }

        [Test]
        public void LungeIntoGuard_FollowsGuardRules()
        {
            var w = World(-1.6f, 0.4f);
            for (int t = 0; t < 10; t++)
                w.Step(default, In(0, 0, G));
            SetLunge(ref w.State.Fighter0, new Vec2(20f, 0f));
            bool guarded = false;
            for (int t = 0; t < 6 && !guarded; t++)
            {
                w.Step(default, In(0, 0, G));
                guarded = Has(w, SimEventType.GuardHit, 1);
            }
            Assert.IsTrue(guarded);
            var c = CombatParams.Default;
            Assert.AreEqual(100 - 2, w.State.Fighter1.Health, "위력 20 → 데미지 12 × 0.2 → 2");
            Assert.AreEqual(18f * c.GuardKnockbackMultiplier, w.State.Fighter1.Velocity.X, Tolerance, "넉백 18 × 0.35 수평");
            Assert.AreEqual(ActionState.GuardStun, w.State.Fighter1.Action);
            Assert.AreEqual(4, w.State.Fighter1.HitstopTicks, "히트스탑 7의 절반");
            Assert.AreEqual(-5f, w.State.Fighter0.Velocity.X, Tolerance, "이긴 쪽 반동 × -0.25");
        }

        // CP5-6 ---------------------------------------------------------------

        [Test]
        public void DashIntoStandingOpponent_Damages()
        {
            // CR-003에서 대시 몸통 공격성 기본값이 0이 됐다(S-149). 가중치가 있을 때의 규칙은 CP16 값 0.6으로 확인한다.
            var w = World(-2f, 0f, ChargeConfig(c => { c.Charge.DashAggression = 0.6f; return c; }));
            bool hit = false;
            for (int t = 0; t < 12 && !hit; t++)
            {
                w.Step(In(1, 0, t == 0 ? InputButtons.Dash : InputButtons.None), default);
                hit = Has(w, SimEventType.Hit, 1);
            }
            Assert.IsTrue(hit, "대시(가중치 0.6)로 피해");
            int expected = SimMath.Clamp(SimMath.RoundToInt(26f * 0.6f * 0.6f), 4, 24);
            Assert.AreEqual(100 - expected, w.State.Fighter1.Health, "위력 15.6 → 데미지 9");
        }

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

        [Test]
        public void DashInTestMode_DoesNoDamage()
        {
            var w = World(-2f, 0f, SimConfig.CreateDefault());
            for (int t = 0; t < 12; t++)
                w.Step(In(1, 0, t == 0 ? InputButtons.Dash : InputButtons.None), default);
            Assert.AreEqual(100, w.State.Fighter1.Health, "모드 A가 아니면 대시 가중치 0");
        }

        // CP5-7 ---------------------------------------------------------------

        [TestCase("Box", 21u)]
        [TestCase("Towers", 22u)]
        [TestCase("Pit", 23u)]
        public void MirroredChargeFight_AndSyncTest(string map, uint seed)
        {
            var arena = ArenaFiles.LoadByName(map);
            var combat = CombatParams.Default;
            combat.MaxHealth = 50;
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Charge);
            var normal = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var mirrored = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var synced = TestWorlds.Create(MatchPhase.Fighting, 7, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            var bot0 = new CombatBot(seed * 13u);
            var bot1 = new CombatBot(seed * 29u);
            var counts = new CombatEventCounts();
            int lunges = 0;
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
                    lunges += normal.Events[i].Type == SimEventType.LungeStart ? 1 : 0;
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "P1↔P2'");
                SymmetryTests.AssertMirrored(normal.State.Fighter1, mirrored.State.Fighter0, t + 1, "P2↔P1'");
            }
            string report = $"[Knockbox][CP5-7] {map}: 돌진 {lunges}, {counts}, SyncTest 불일치 {sync.MismatchCount}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());
            Assert.AreEqual(normal.ComputeChecksum(), synced.ComputeChecksum());
            Assert.Greater(lunges, 10);
            Assert.Greater(counts.Hit, 0, "돌진·대시 명중이 나와야 함");
        }
    }
}
