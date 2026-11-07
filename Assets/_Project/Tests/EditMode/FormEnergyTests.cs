using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP18: 파이터별 폼, 기능 스위치, 공격 에너지(10-forms-and-growth.md 4.4·5.1).
    /// 수치 기대값은 EnergyParams·ChargeModeParams에서 계산한다.
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class FormEnergyTests
    {
        const float FloorY = -7.55f;
        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("FormFlat", (c, r) => '\0');

        static SimConfig FormConfig(FighterForm form = FighterForm.Scissors, bool guard = false, bool grab = false, bool energy = true,
            System.Func<CombatParams, CombatParams> change = null)
        {
            var combat = CombatParams.Default;
            if (change != null)
                combat = change(combat);
            var rules = new RuleSet
            {
                StartForm = form,
                Switches = new FeatureSwitches { GuardEnabled = guard, GrabEnabled = grab, EnergyEnabled = energy },
            };
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, rules);
        }

        static SimWorld World(SimConfig config, float x0 = -2f, float x1 = 4f, ArenaDef arena = null, float y0 = FloorY, float y1 = FloorY) =>
            TestWorlds.CreateAt(new Vec2(x0, y0), new Vec2(x1, y1), config, arena ?? Flat);

        static InputFrame In(int x, int y, InputButtons b) => new InputFrame(x, y, b);

        static bool Has(SimWorld w, SimEventType type, int fighter)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                    return true;
            }
            return false;
        }

        static void Idle(SimWorld w, int ticks)
        {
            for (int t = 0; t < ticks; t++)
                w.Step(default, default);
        }

        // CP18-3 ----------------------------------------------------------------

        [Test]
        public void Energy_RegeneratesAfterDelay_AndFillsUp()
        {
            var config = FormConfig();
            ref readonly var e = ref config.Combat.Energy;
            var w = World(config);
            w.State.Fighter0.Energy = 0;
            w.State.Fighter0.EnergyRegenDelay = e.RegenDelayTicks;

            Idle(w, e.RegenDelayTicks);
            Assert.AreEqual(0, w.State.Fighter0.Energy, "지연 동안 회복하지 않음");
            Idle(w, 10);
            Assert.AreEqual(10 * e.RegenPerTick, w.State.Fighter0.Energy, "지연 뒤 틱마다 회복");
            int toFull = (e.Max + e.RegenPerTick - 1) / e.RegenPerTick;
            Idle(w, toFull);
            Assert.AreEqual(e.Max, w.State.Fighter0.Energy, "최대에서 멈춤");
            float seconds = (e.RegenDelayTicks + toFull) / 60f;
            Assert.That(seconds, Is.InRange(3f, 5f), "약 1초 지연 + 약 3초 완전 회복");
        }

        [Test]
        public void EmptyEnergy_BlocksAttackStart_DiscardsPress_MovementStillWorks()
        {
            var config = FormConfig();
            ref readonly var e = ref config.Combat.Energy;
            var w = World(config);
            w.State.Fighter0.Energy = 0;
            w.State.Fighter0.EnergyRegenDelay = e.RegenDelayTicks;

            w.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action, "에너지 0이면 충전이 시작되지 않음");
            // 버튼을 계속 누르고 있어도, 에너지가 다 차도 눌림이 대기했다가 시작되지 않는다.
            for (int t = 0; t < e.RegenDelayTicks + e.Max / e.RegenPerTick + 5; t++)
            {
                w.Step(In(0, 0, InputButtons.Attack), default);
                Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action, $"틱 {t}: 버린 입력으로 충전이 시작됨");
            }
            Assert.AreEqual(e.Max, w.State.Fighter0.Energy);

            // 이동·점프·이동 대시는 에너지와 상관없다.
            var m = World(config);
            m.State.Fighter0.Energy = 0;
            m.Step(In(1, 0, InputButtons.Jump), default);
            Assert.Greater(m.State.Fighter0.Velocity.Y, 0f, "점프");
            Idle(m, 60);
            m.Step(In(1, 0, InputButtons.Dash), default);
            Assert.AreEqual(ActionState.Dash, m.State.Fighter0.Action, "이동 대시");
            Assert.AreEqual(0, CountHits(m, 20), "이동 대시로 피해 없음");
        }

        static int CountHits(SimWorld w, int ticks)
        {
            int hits = 0;
            for (int t = 0; t < ticks; t++)
            {
                w.Step(In(1, 0, InputButtons.None), default);
                hits += Has(w, SimEventType.Hit, 1) ? 1 : 0;
            }
            return hits;
        }

        // CP18-4 ----------------------------------------------------------------

        [Test]
        public void ScissorsCharge_ChecksAtStart_SpendsExactlyOnceAtLaunch()
        {
            var config = FormConfig();
            ref readonly var e = ref config.Combat.Energy;
            ref readonly var charge = ref config.Combat.Charge;

            // 비용 미만이면 충전이 시작되지 않는다.
            var poor = World(config);
            poor.State.Fighter0.Energy = e.LungeCost - 1;
            poor.State.Fighter0.EnergyRegenDelay = e.RegenDelayTicks;
            poor.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreNotEqual(ActionState.Charge, poor.State.Fighter0.Action);
            Assert.AreEqual(e.LungeCost - 1, poor.State.Fighter0.Energy);

            int[] holds = { 1, 20, charge.FullChargeTicks + charge.FullChargeHoldTicks + 10 };
            foreach (int hold in holds)
            {
                var w = World(config);
                int before = w.State.Fighter0.Energy;
                bool launched = false;
                for (int t = 0; t < hold && !launched; t++)
                {
                    w.Step(In(0, 0, InputButtons.Attack), default);
                    if (w.State.Fighter0.Action == ActionState.Charge)
                        Assert.AreEqual(before, w.State.Fighter0.Energy, $"충전 {hold}: 충전 중 에너지 변화");
                    launched = w.State.Fighter0.Action == ActionState.Lunge;
                }
                if (!launched)
                    w.Step(default, default);
                Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, $"충전 {hold}: 발사");
                Assert.AreEqual(before - e.LungeCost, w.State.Fighter0.Energy, $"충전 {hold}: 정확히 LungeCost 한 번(이중 차감 없음)");
            }
        }

        [Test]
        public void ScissorsLunges_NTimes_SpendNTimesCost()
        {
            // 회복이 없는 조건: 회복 0, 최대를 넉넉히.
            const int n = 5;
            var config = FormConfig(change: c => { c.Energy.RegenPerTick = 0; c.Energy.Max = c.Energy.LungeCost * (n + 1); return c; });
            var w = World(config, -12f, 12f);
            int before = w.State.Fighter0.Energy;
            int launches = 0;
            for (int i = 0; i < n; i++)
            {
                w.Step(In(0, 0, InputButtons.Attack), default);
                w.Step(default, default);
                launches += w.State.Fighter0.Action == ActionState.Lunge ? 1 : 0;
                Idle(w, 90);
            }
            Assert.AreEqual(n, launches);
            Assert.AreEqual(before - n * config.Combat.Energy.LungeCost, w.State.Fighter0.Energy);
        }

        // CP18-5 ----------------------------------------------------------------

        [Test]
        public void ChargeInterruptedByHit_SpendsNothing()
        {
            var config = FormConfig();
            var w = World(config, -2f, 2f);
            int before = w.State.Fighter0.Energy;
            bool interrupted = false;
            for (int t = 0; t < 40; t++)
            {
                // P1은 충전을 계속 누르고, P2는 한 번 눌러 최소 돌진으로 P1을 친다.
                var p2 = t == 5 ? In(-1, 0, InputButtons.Attack) : In(-1, 0, InputButtons.None);
                w.Step(In(0, 0, InputButtons.Attack), p2);
                interrupted |= Has(w, SimEventType.Hit, 0);
                Assert.AreEqual(before, w.State.Fighter0.Energy, $"틱 {t}: 끊긴 충전이 에너지를 씀");
            }
            Assert.IsTrue(interrupted, "P1이 충전 중 맞음");
            Assert.AreEqual(before - config.Combat.Energy.LungeCost, w.State.Fighter1.Energy, "P2는 발사할 때 한 번");
        }

        [Test]
        public void ChargeInterruptedByKo_SpendsNothing()
        {
            var config = FormConfig();
            var w = World(config, -2f, 2f);
            w.State.Fighter0.Health = 1;
            int before = w.State.Fighter0.Energy;
            for (int t = 0; t < 40 && w.State.Match.Phase == MatchPhase.Fighting; t++)
                w.Step(In(0, 0, InputButtons.Attack), t == 5 ? In(-1, 0, InputButtons.Attack) : default);
            Assert.AreEqual(ActionState.KO, w.State.Fighter0.Action);
            Assert.AreEqual(before, w.State.Fighter0.Energy);
        }

        [Test]
        public void ChargeInterruptedByBoundaryBounce_SpendsNothing()
        {
            var config = FormConfig();
            var arena = BoundaryTests.Open();
            float x = arena.BoundaryHalfWidth - config.HalfBody - 0.3f;
            var w = World(config, x, -x, arena, 0f, 0f);
            int before = w.State.Fighter0.Energy;
            w.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            // 충전 중 밀려 경계에 부딪힌 상황을 만든다.
            w.State.Fighter0.Velocity = new Vec2(40f, 0f);
            bool bounced = false;
            for (int t = 0; t < 5 && !bounced; t++)
            {
                w.Step(In(0, 0, InputButtons.Attack), default);
                bounced = Has(w, SimEventType.BoundaryHit, 0);
            }
            Assert.IsTrue(bounced, "경계 튕김");
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action, "경계 튕김이 충전을 끊음");
            Idle(w, 30);
            Assert.AreEqual(before, w.State.Fighter0.Energy);
        }

        [Test]
        public void ShortAtLaunch_CancelsWithoutFiring_AndLeavesEvent()
        {
            var config = FormConfig();
            int cost = config.Combat.Energy.LungeCost;
            var w = World(config);
            for (int t = 0; t < 10; t++)
                w.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Charge, w.State.Fighter0.Action);
            // 이후 규칙이나 증강이 에너지를 줄인 경우를 흉내 낸다.
            w.State.Fighter0.Energy = cost - 1;
            w.Step(default, default);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter0.Action, "발사하지 않음");
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action, "충전 취소");
            Assert.AreEqual(cost - 1, w.State.Fighter0.Energy, "소비 없음");
            Assert.IsTrue(Has(w, SimEventType.EnergyShort, 0), "부족 이벤트");
        }

        // CP18-6 ----------------------------------------------------------------

        [Test]
        public void GuardAndGrabSwitches_IgnoreInputWhenOff()
        {
            foreach (bool on in new[] { false, true })
            {
                var config = FormConfig(guard: on, grab: on);
                var guard = World(config);
                guard.Step(In(0, 0, InputButtons.Guard), default);
                Assert.AreEqual(on, guard.State.Fighter0.Action == ActionState.Guard, $"가드 스위치 {on}");
                var grab = World(config);
                grab.Step(In(0, 0, InputButtons.Grab), default);
                Assert.AreEqual(on, grab.State.Fighter0.Action == ActionState.Grab, $"잡기 스위치 {on}");
            }
        }

        // CP18-7 ----------------------------------------------------------------

        [Test]
        public void DashBodyAggressionZero_NoDamageWithoutEnergy()
        {
            var config = FormConfig();
            Assert.AreEqual(0f, config.Combat.Charge.DashAggression, "CR-003 기본값");
            var w = World(config, -2f, 0f);
            w.State.Fighter0.Energy = 0;
            w.Step(In(1, 0, InputButtons.Dash), default);
            for (int t = 0; t < 30; t++)
            {
                w.Step(In(1, 0, InputButtons.None), default);
                Assert.IsFalse(Has(w, SimEventType.Hit, 1), $"틱 {t}: 대시 접촉으로 피해");
            }
            Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        // CP18-2 ----------------------------------------------------------------

        [TestCase(FighterForm.Scissors, 181u)]
        [TestCase(FighterForm.Paper, 182u)]
        public void FormRules_Deterministic_SaveRestore_SyncTest_Replay(FighterForm form, uint seed)
        {
            const int ticks = 3600;
            var config = FormConfig(form);
            var arena = TestWorlds.Box;
            TestWorlds.RandomInputs(seed, ticks, out var p1, out var p2);

            uint Run()
            {
                var w = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
                for (int t = 0; t < ticks; t++)
                    w.Step(p1[t], p2[t]);
                return w.ComputeChecksum();
            }
            uint first = Run();
            Assert.AreEqual(first, Run(), "같은 입력, 같은 체크섬");

            var a = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            for (int t = 0; t < ticks / 2; t++)
                a.Step(p1[t], p2[t]);
            var saved = a.State;
            for (int t = ticks / 2; t < ticks; t++)
                a.Step(p1[t], p2[t]);
            a.State = saved;
            for (int t = ticks / 2; t < ticks; t++)
                a.Step(p1[t], p2[t]);
            Assert.AreEqual(first, a.ComputeChecksum(), "저장·복원");

            var synced = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            var sync = new SyncTest<SimWorld>(synced);
            for (int t = 0; t < ticks; t++)
                sync.Advance(p1[t], p2[t]);
            Assert.AreEqual(0, sync.MismatchCount, "SyncTest");

            var start = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            var recording = InputRecording.Begin(config, arena, seed, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.Fighting, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                recording.Append(p1[t], p2[t]);
            recording.Finish(first);
            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.AreEqual(config.Rules.Format(), loaded.Mode);
            Assert.IsTrue(RuleSet.TryParse(loaded.Mode, out var rules) && rules.Equals(config.Rules), "규칙 문자열 왕복");
            Assert.AreEqual(first, loaded.Replay(config, arena), "입력 기록 재생");
        }
    }
}
