using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP23: 증강 틀(10-forms-and-growth.md 5.3·5.4 개정, D-055~D-057). 수치 기대값은 증강 데이터·설정값에서 계산한다.
    /// 슬롯 충돌·무의미 조합·플래그 충돌·트리거 순서는 테스트 전용 증강 데이터(Id 100~)로만 확인한다(제품 증강 수는 늘리지 않는다).
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class AugmentTests
    {
        const float FloorY = -7.55f;
        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("AugFlat", (c, r) => '\0');

        // 테스트 전용 증강.
        const int TestFlagOnLow = 100, TestFlagOnHigh = 101, TestSlotA = 102, TestSlotB = 103, TestShortenCharge = 104,
            TestRefundA = 105, TestRefundB = 106, TestChainOnRefund = 107, TestCheapLunge = 108, TestFastShot = 109;

        static AugmentDef[] Catalog(bool withTest)
        {
            var list = new List<AugmentDef>(AugmentCatalog.CreateDefault());
            if (!withTest)
                return list.ToArray();
            list.Add(new AugmentDef { Id = TestFlagOnLow, Name = "T-ON-LOW", Target = AugmentTarget.Rock, Priority = 0, MaxStacks = 1, Flag = AugmentFlag.ProjectileReflectable, FlagValue = true });
            list.Add(new AugmentDef { Id = TestFlagOnHigh, Name = "T-ON-HIGH", Target = AugmentTarget.Rock, Priority = 5, MaxStacks = 1, Flag = AugmentFlag.ProjectileReflectable, FlagValue = true });
            list.Add(new AugmentDef { Id = TestSlotA, Name = "T-SLOT-A", Target = AugmentTarget.Scissors, Priority = 1, MaxStacks = 1, Slot = AugmentSlot.ScissorsLaunch, AddStat = AugmentStat.LungeCost, Add = -10 });
            list.Add(new AugmentDef { Id = TestSlotB, Name = "T-SLOT-B", Target = AugmentTarget.Scissors, Priority = 1, MaxStacks = 1, Slot = AugmentSlot.ScissorsLaunch, AddStat = AugmentStat.LungeCost, Add = -5 });
            list.Add(new AugmentDef { Id = TestShortenCharge, Name = "T-SHORT", Target = AugmentTarget.Scissors, MaxStacks = 1, MulStat = AugmentStat.LungeSpeed, Mul = 1.5f, Targets = AugmentAspect.ChargePhase });
            list.Add(new AugmentDef { Id = TestRefundA, Name = "T-REF-A", Target = AugmentTarget.Paper, Priority = 2, MaxStacks = 1, Trigger = AugmentTrigger.OnMeleeHit, TriggerValue = 7 });
            list.Add(new AugmentDef { Id = TestRefundB, Name = "T-REF-B", Target = AugmentTarget.Paper, Priority = 9, MaxStacks = 1, Trigger = AugmentTrigger.OnMeleeHit, TriggerValue = 3 });
            list.Add(new AugmentDef { Id = TestChainOnRefund, Name = "T-CHAIN", Target = AugmentTarget.Paper, MaxStacks = 1, Trigger = AugmentTrigger.OnEnergyRefund, TriggerValue = 50 });
            list.Add(new AugmentDef { Id = TestCheapLunge, Name = "T-CHEAP", Target = AugmentTarget.Scissors, MaxStacks = 1, AddStat = AugmentStat.LungeCost, Add = -500 });
            list.Add(new AugmentDef { Id = TestFastShot, Name = "T-FAST", Target = AugmentTarget.Rock, MaxStacks = 1, AddStat = AugmentStat.ShotStartup, Add = -500 });
            return list.ToArray();
        }

        static SimConfig Config(FighterForm p1, FighterForm p2, bool withTest = false, bool energy = true) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = new FeatureSwitches { EnergyEnabled = energy } }.WithForms(p1, p2), Catalog(withTest));

        static SimWorld World(SimConfig config, float x0 = -6f, float x1 = 6f) =>
            TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x1, FloorY), config, Flat);

        static InputFrame In(InputButtons b, int x = 0) => new InputFrame(x, 0, b);

        static AugmentState Held(params int[] ids)
        {
            var s = new AugmentState();
            foreach (int id in ids)
                s.Add(id);
            return s;
        }

        static IEnumerable<int[]> Permutations(int[] items)
        {
            if (items.Length <= 1)
            {
                yield return items;
                yield break;
            }
            for (int i = 0; i < items.Length; i++)
            {
                var rest = new List<int>(items);
                rest.RemoveAt(i);
                foreach (var p in Permutations(rest.ToArray()))
                {
                    var r = new int[items.Length];
                    r[0] = items[i];
                    p.CopyTo(r, 1);
                    yield return r;
                }
            }
        }

        static string Describe(in AugmentEffects e) =>
            $"mul {e.LungeSpeedMul:R} cost {e.LungeCostAdd}/{e.ShotCostAdd}/{e.SwingCostAdd} start {e.ShotStartupAdd} refl {e.Reflectable} slot {e.LaunchSlotOwner} dormant {Count(e.DormantSlots)} lost {Count(e.SlotLostSlots)}";

        static int Count(int bits)
        {
            int n = 0;
            for (; bits != 0; bits &= bits - 1)
                n++;
            return n;
        }

        // CP23-1 ----------------------------------------------------------------

        [TestCase(new[] { AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.InstantLunge })]
        [TestCase(new[] { AugmentCatalog.LungeSpeed, AugmentCatalog.InstantLunge, AugmentCatalog.ArmorPiercing, AugmentCatalog.EnergyRefund })]
        [TestCase(new[] { TestSlotA, TestSlotB, TestFlagOnHigh, AugmentCatalog.ArmorPiercing })]
        [TestCase(new[] { TestShortenCharge, AugmentCatalog.InstantLunge, AugmentCatalog.LungeSpeed, TestCheapLunge })]
        public void AllAcquisitionOrders_GiveSameResult(int[] ids)
        {
            var config = Config(FighterForm.Scissors, FighterForm.Scissors, withTest: true);
            string first = null;
            int orders = 0;
            foreach (var order in Permutations(ids))
            {
                string got = Describe(Augments.Compute(Held(order), config));
                first ??= got;
                Assert.AreEqual(first, got, $"순서 {string.Join(",", order)}");
                orders++;
            }
            Assert.AreEqual(24, orders, "4! = 24 순서");
        }

        // CP23-2 ----------------------------------------------------------------

        [Test]
        public void Conflicts_ResolvedByPriorityThenId()
        {
            var config = Config(FighterForm.Rock, FighterForm.Rock, withTest: true);
            // 플래그 반대 설정: 철갑탄(끔, 우선순위 0) 대 켬(우선순위 0, Id 100) → 같으면 Id 작은 철갑탄(2). 켬(우선순위 5)은 이긴다.
            Assert.AreEqual(0, Augments.Compute(Held(TestFlagOnLow, AugmentCatalog.ArmorPiercing), config).Reflectable);
            Assert.AreEqual(1, Augments.Compute(Held(AugmentCatalog.ArmorPiercing, TestFlagOnHigh), config).Reflectable);

            // 같은 슬롯: 하나만 활성(같은 우선순위면 Id 작은 A), 진 쪽 B의 수치 효과(비용 -5)는 유지.
            var slots = Augments.Compute(Held(TestSlotB, TestSlotA), config);
            Assert.AreEqual(TestSlotA, slots.LaunchSlotOwner);
            Assert.AreEqual(1, Count(slots.SlotLostSlots));
            Assert.AreEqual(-15, slots.LungeCostAdd, "진 쪽의 다른 효과는 유지");
            // 즉발 돌진(우선순위 0) 대 A(우선순위 1): A가 차지.
            Assert.AreEqual(TestSlotA, Augments.Compute(Held(AugmentCatalog.InstantLunge, TestSlotA), config).LaunchSlotOwner);
        }

        [Test]
        public void SimultaneousTriggers_FixedOrder_ChainDepthOne()
        {
            var config = Config(FighterForm.Paper, FighterForm.Scissors, withTest: true);
            var w = World(config, -0.7f, 0.7f);
            w.State.Fighter0.Augments = Held(TestRefundA, TestRefundB, TestChainOnRefund);
            int before = w.State.Fighter0.Energy;
            var order = new List<int>();
            for (int t = 0; t < 20; t++)
            {
                w.Step(t == 0 ? In(InputButtons.Attack, 1) : default, default);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    if (w.Events[i].Type == SimEventType.AugmentActive && w.Events[i].Fighter == 0)
                        order.Add((int)w.Events[i].Strength);
                }
            }
            CollectionAssert.AreEqual(new[] { TestRefundB, TestRefundA }, order, "Priority 높은 B → A, 환급에 반응하는 트리거는 연쇄하지 않음");
            int expected = before - config.Combat.Energy.SwingCost + 3 + 7;
            Assert.AreEqual(System.Math.Min(config.Combat.Energy.Max, expected), w.State.Fighter0.Energy);
        }

        // CP23-3 ----------------------------------------------------------------

        [Test]
        public void LowerBounds_CostAtLeastZero_StartupAtLeastOne()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Rock, withTest: true);
            var f = new FighterState { Augments = Held(TestCheapLunge) };
            Assert.AreEqual(0, FighterCombat.CostFor(f, FighterForm.Scissors, config));
            var g = new FighterState { Augments = Held(TestFastShot) };
            Assert.AreEqual(1, FighterCombat.ShotStartup(g, config));
            var w = World(config);
            w.State.Fighter1.Augments = Held(TestFastShot);
            w.Step(default, In(InputButtons.Attack));
            int fired = 0;
            for (int t = 0; t < 3; t++)
            {
                w.Step(default, default);
                for (int i = 0; i < w.Events.Count; i++)
                    fired += w.Events[i].Type == SimEventType.ProjectileFire ? 1 : 0;
                if (t == 0)
                    Assert.AreEqual(1, fired, "선딜 1틱");
            }
        }

        // CP23-4 ----------------------------------------------------------------

        /// <summary>평지에서 마주 선 바위(P1)와 보(P2). P1이 쏘고 P2는 투사체가 닿는 틱에 근접 활성 판정이 겹치도록 휘두른다.</summary>
        static (int reflects, int hit0, int hit1, int owner) ArmorScenario(params int[] p1Augments)
        {
            var config = Config(FighterForm.Rock, FighterForm.Paper, energy: false);
            var w = World(config, -4f, 2f);
            w.State.Fighter0.Augments = Held(p1Augments);
            ref readonly var rock = ref config.Combat.Rock;
            ref readonly var swing = ref config.Combat.Swing.Forward;
            // 투사체가 보의 판정 원(앞 약 1.1)에 닿는 틱을 설정값에서 계산: 발사 위치 → 판정 원 앞까지의 거리 / 틱당 이동.
            float spawnX = -4f + rock.SpawnOffset;
            float circleX = 2f - 1.1f;
            int travel = (int)System.Math.Ceiling((circleX - spawnX - swing.Circle1.Radius - rock.Radius) / (rock.Speed / 60f));
            int fireTick = rock.StartupTicks;
            int pressTick = fireTick + travel - swing.StartupTicks - 1;
            int reflects = 0, hit0 = 0, hit1 = 0, owner = -1;
            for (int t = 0; t < 120; t++)
            {
                w.Step(t == 0 ? In(InputButtons.Attack, 1) : default, t == pressTick ? In(InputButtons.Attack, -1) : default);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    var e = w.Events[i];
                    if (e.Type == SimEventType.ProjectileReflect) reflects++;
                    if (e.Type == SimEventType.Hit && e.Fighter == 0) hit0++;
                    if (e.Type == SimEventType.Hit && e.Fighter == 1) hit1++;
                }
                if (owner < 0 && w.State.Projectiles.CountActive(-1) > 0)
                {
                    for (int i = 0; i < ProjectileState.Capacity; i++)
                    {
                        var p = ProjectileState.At(w.State.Projectiles, i);
                        if (p.Active && p.Reflections > 0)
                            owner = p.Owner;
                    }
                }
            }
            return (reflects, hit0, hit1, owner);
        }

        [Test]
        public void ArmorPiercing_FlipsMatchupAgainstPaper_CountDoesNot()
        {
            var before = ArmorScenario();
            Assert.AreEqual(1, before.reflects, "증강 전: 반사");
            Assert.AreEqual(1, before.owner, "소유자가 P2로");
            Assert.AreEqual(1, before.hit0, "반사된 투사체가 P1을 맞힘");
            Assert.AreEqual(0, before.hit1, "P2는 피해 없음");

            var after = ArmorScenario(AugmentCatalog.ArmorPiercing);
            Assert.AreEqual(0, after.reflects, "증강 후: 반사 없음");
            Assert.AreEqual(1, after.hit1, "보를 일반 명중");
            Assert.AreEqual(0, after.hit0, "P1 체력 그대로");

            // 대조군: Reflectable을 건드리지 않는 증강 둘(다른 폼용이라도 개수만 늘림) → 반사 그대로.
            var control = ArmorScenario(AugmentCatalog.LungeSpeed, AugmentCatalog.EnergyRefund);
            Assert.AreEqual(1, control.reflects, "개수가 아니라 Reflectable을 바꾼 효과가 상성을 바꾼다");
            Assert.AreEqual(1, control.hit0);
        }

        // CP23-5 ----------------------------------------------------------------

        [Test]
        public void SpeedAndInstant_StackTogether_OrderFree_SingleCost()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Scissors);
            ref readonly var charge = ref config.Combat.Charge;
            float mul = AugmentCatalog.CreateDefault()[AugmentCatalog.LungeSpeed].Mul;
            foreach (var held in new[] { new[] { AugmentCatalog.LungeSpeed, AugmentCatalog.InstantLunge }, new[] { AugmentCatalog.InstantLunge, AugmentCatalog.LungeSpeed } })
            {
                var w = World(config, -10f, 10f);
                w.State.Fighter0.Augments = Held(held);
                int before = w.State.Fighter0.Energy;
                w.Step(In(InputButtons.Attack, 1), default);
                Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "누른 틱에 발사(충전 없음)");
                Assert.AreEqual(charge.InstantLungeSpeed * mul, w.State.Fighter0.Velocity.X, 1e-3f, "즉발 속도 × 배율");
                Assert.Greater(w.State.Fighter0.Velocity.X, charge.InstantLungeSpeed, "하나만 붙였을 때보다 큼");
                Assert.AreEqual(before - config.Combat.Energy.LungeCost, w.State.Fighter0.Energy, "비용 한 번");
            }
            // 속도 배율만: 충전형 속도 × 배율.
            var c = World(config, -10f, 10f);
            c.State.Fighter0.Augments = Held(AugmentCatalog.LungeSpeed);
            for (int t = 0; t < 20; t++)
                c.Step(In(InputButtons.Attack, 1), default);
            c.Step(In(InputButtons.None, 1), default);
            Assert.AreEqual(ActionState.Lunge, c.State.Fighter0.Action);
            Assert.AreEqual(charge.LungeSpeed(20) * mul, c.State.Fighter0.Velocity.X, 0.05f, "충전 속도 × 배율");
        }

        // CP23-6 ----------------------------------------------------------------

        [Test]
        public void NonsenseCombination_FilteredAndDormant_OrderFree()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Scissors, withTest: true);
            // 충전 시간 단축(Targets=충전 단계) + 즉발(Removes=충전 단계): 단축은 휴면(효과 없음).
            var a = Augments.Compute(Held(TestShortenCharge, AugmentCatalog.InstantLunge), config);
            var b = Augments.Compute(Held(AugmentCatalog.InstantLunge, TestShortenCharge), config);
            Assert.AreEqual(Describe(a), Describe(b));
            Assert.AreEqual(1, Count(a.DormantSlots));
            Assert.AreEqual(1f, a.LungeSpeedMul, "휴면한 증강의 배율이 적용되지 않음");
            // 후보 필터: 즉발을 가지면 단축이 후보에 없고, 단축을 가지면 즉발이 후보에 없다.
            Assert.IsFalse(Candidates(config, AugmentCatalog.InstantLunge).Contains(PickOption.AugmentBase + TestShortenCharge));
            Assert.IsFalse(Candidates(config, TestShortenCharge).Contains(PickOption.AugmentBase + AugmentCatalog.InstantLunge));
        }

        static List<int> Candidates(SimConfig config, params int[] held)
        {
            var f = new FighterState { Form = FighterForm.Scissors, OwnedForms = Forms.Bit(FighterForm.Scissors), Augments = Held(held) };
            var list = new CandidateList();
            int count = 0;
            Augments.AddCandidates(f, config, ref list, ref count);
            var r = new List<int>();
            for (int i = 0; i < count; i++)
                r.Add(list.Get(i));
            return r;
        }

        // CP23-7, CP23-9 ------------------------------------------------------

        [Test]
        public void ScissorsOnly_FirstPickHasExactlyFour_SameSeedSameOptions()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Scissors);
            var f = new FighterState { Form = FighterForm.Scissors, OwnedForms = Forms.Bit(FighterForm.Scissors) };
            var m = new MatchState();
            var rng = new SimRng(5);
            int n = MatchFlow.BuildOptions(ref m, f, config, ref rng);
            Assert.AreEqual(4, n);
            CollectionAssert.AreEquivalent(new[]
            {
                PickOption.Form(FighterForm.Rock), PickOption.Form(FighterForm.Paper),
                PickOption.AugmentBase + AugmentCatalog.LungeSpeed, PickOption.AugmentBase + AugmentCatalog.InstantLunge,
            }, new[] { m.Option0, m.Option1, m.Option2, m.Option3 });
            // 같은 시드면 같은 후보(넘쳐서 섞는 경우 포함).
            var all = new FighterState { Form = FighterForm.Rock, OwnedForms = Forms.AllPlayable };
            var m1 = new MatchState();
            var m2 = new MatchState();
            var r1 = new SimRng(9);
            var r2 = new SimRng(9);
            MatchFlow.BuildOptions(ref m1, all, config, ref r1);
            MatchFlow.BuildOptions(ref m2, all, config, ref r2);
            Assert.AreEqual((m1.Option0, m1.Option1, m1.Option2, m1.Option3), (m2.Option0, m2.Option1, m2.Option2, m2.Option3));
            // 하나를 고르면 다음 선택지에 가위 증강이 남는다.
            MatchFlow.Apply(ref f, PickOption.AugmentBase + AugmentCatalog.InstantLunge, config);
            var after = Candidates(config, AugmentCatalog.InstantLunge);
            Assert.Contains(PickOption.AugmentBase + AugmentCatalog.LungeSpeed, after);
            // 슬롯 필터(테스트 전용 데이터): 즉발이 슬롯을 차지하면 같은 슬롯의 A·B는 후보에 없다.
            var test = Config(FighterForm.Scissors, FighterForm.Scissors, withTest: true);
            var c = Candidates(test, AugmentCatalog.InstantLunge);
            Assert.IsFalse(c.Contains(PickOption.AugmentBase + TestSlotA) || c.Contains(PickOption.AugmentBase + TestSlotB));
            // 중복 상한: 속도 배율 3번이면 더 이상 후보가 아니다.
            Assert.IsFalse(Candidates(config, AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed)
                .Contains(PickOption.AugmentBase + AugmentCatalog.LungeSpeed));
        }

        /// <summary>
        /// (나) D-057: 실제 증강 데이터만으로, 가위만 가진 파이터가 가위 증강만 고르는 모든 경로에서 네 번의 선택이 모두 가능하고 후보가 0이 되지 않는다.
        /// 선택지는 시드로 섞이므로 시드 여러 개로 확인한다.
        /// </summary>
        [Test]
        public void ScissorsSpecialization_FourPicksAlwaysPossible_RealDataOnly()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Scissors);
            int paths = 0;
            for (uint seed = 1; seed <= 50; seed++)
                paths += Explore(new FighterState { Form = FighterForm.Scissors, OwnedForms = Forms.Bit(FighterForm.Scissors) }, 0, config, new SimRng(seed));
            Assert.Greater(paths, 0);
            float mul = AugmentCatalog.CreateDefault()[AugmentCatalog.LungeSpeed].Mul;
            var full = Augments.Compute(Held(AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.InstantLunge), config);
            Assert.AreEqual(mul * mul * mul, full.LungeSpeedMul, 1e-5f, "최종 속도 배율 1.2³");
            Assert.IsTrue(full.InstantLunge);
        }

        static int Explore(FighterState f, int depth, SimConfig config, SimRng rng)
        {
            if (depth == 4)
                return 1;
            var m = new MatchState();
            int n = MatchFlow.BuildOptions(ref m, f, config, ref rng);
            int paths = 0;
            for (int i = 0; i < n; i++)
            {
                int option = m.GetOption(i);
                if (!PickOption.IsAugment(option) || !Augments.TryGet(config, PickOption.AugmentOf(option), out var d) || d.Target != AugmentTarget.Scissors)
                    continue;
                var next = f;
                MatchFlow.Apply(ref next, option, config);
                paths += Explore(next, depth + 1, config, rng);
            }
            Assert.Greater(paths, 0, $"선택 {depth + 1}번째에서 가위 증강 후보가 없음(보유 {f.Augments.Count})");
            return paths;
        }

        // CP23-8 ----------------------------------------------------------------

        /// <summary>(가) D-057: 테스트 전용 데이터로 가위에 증강 4개(배율 중복 포함)를 강제로 부여해도 결정론·SyncTest·터널링·하한이 지켜진다.</summary>
        [Test]
        public void FourStackedScissors_Stable_NoTunneling_Deterministic()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Rock, withTest: true);
            var held = Held(AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, AugmentCatalog.LungeSpeed, TestCheapLunge);
            Assert.AreEqual(0, FighterCombat.CostFor(new FighterState { Augments = held }, FighterForm.Scissors, config), "하한 0");

            // 가장 빠른 돌진(최대 충전 × 1.2³)으로 두께 0.5 판을 여러 각도에서 쳐도 몸이 통과하지 않는다.
            foreach (var kind in new[] { TerrainKind.Solid, TerrainKind.OneWay, TerrainKind.Prop })
            {
                for (int dir = -1; dir <= 1; dir++)
                {
                    var arena = new ArenaDef("Thin", 36, 20, true, new[]
                    {
                        new ArenaBox(TerrainKind.Solid, -17f, -9f, 17f, -8f),
                        kind == TerrainKind.OneWay ? new ArenaBox(kind, -6f, -4.25f, 6f, -3.75f) : new ArenaBox(kind, 3.75f, -8f, 4.25f, 2f),
                    }, new Vec2(-8f, -8f), new Vec2(14f, -8f));
                    // 발판은 아래에서 위로는 원래 통과하므로, 발판 위에서 아래(대각) 방향으로 돌진해 뚫지 않는지 본다.
                    float startY = kind == TerrainKind.OneWay ? -3.75f + config.HalfBody : -7.55f;
                    var w = TestWorlds.CreateAt(new Vec2(-2f, startY), new Vec2(14f, -7.55f), config, arena);
                    w.State.Fighter0.Augments = held;
                    int moveY = kind == TerrainKind.OneWay ? -1 : dir;
                    int moveX = kind == TerrainKind.OneWay ? dir : 1;
                    for (int t = 0; t < 60; t++)
                        w.Step(new InputFrame(moveX, moveY, InputButtons.Attack), default);
                    bool lunged = false;
                    for (int t = 0; t < 40; t++)
                    {
                        w.Step(new InputFrame(moveX, moveY, InputButtons.None), default);
                        lunged |= w.State.Fighter0.Action == ActionState.Lunge;
                        var p = w.State.Fighter0.Position;
                        if (kind == TerrainKind.OneWay && SimMath.Abs(p.X) < 6f - config.HalfBody)
                            Assert.Greater(p.Y, -3.75f + config.HalfBody - 0.01f, $"{kind} {dir}: 발판을 위에서 뚫음({p})");
                        else if (kind == TerrainKind.OneWay)
                            continue;
                        else
                            Assert.Less(p.X, 3.75f - config.HalfBody + 0.01f, $"{kind} {dir}: 판을 통과({p})");
                    }
                    Assert.IsTrue(lunged);
                }
            }

            // 결정론·SyncTest.
            TestWorlds.RandomInputs(231, 1800, out var in1, out var in2);
            SimWorld Make()
            {
                var w = TestWorlds.Create(MatchPhase.Fighting, 4, config, TestWorlds.Box);
                w.State.Fighter0.Augments = held;
                return w;
            }
            var a = Make();
            var b = Make();
            for (int t = 0; t < in1.Length; t++)
            {
                a.Step(in1[t], in2[t]);
                b.Step(in1[t], in2[t]);
            }
            Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum());
            var sync = new SyncTest<SimWorld>(Make());
            for (int t = 0; t < in1.Length; t++)
                sync.Advance(in1[t], in2[t]);
            Assert.AreEqual(0, sync.MismatchCount);
        }

        // CP23-10 ---------------------------------------------------------------

        [Test]
        public void FullMatchWithAugments_SyncTest_Replay_Symmetric()
        {
            const int ticks = 9000;
            var config = Config(FighterForm.Scissors, FighterForm.Scissors);
            var arenas = new[] { ArenaFiles.LoadByName("Ruins"), ArenaFiles.LoadByName("Totems") };
            SimWorld Make() => new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 31, MatchPhase.FormSelect));
            var w = Make();
            var b1 = new BotInputSource(() => w, 0, 41u);
            var b2 = new BotInputSource(() => w, 1, 43u);
            var in1 = new InputFrame[ticks];
            var in2 = new InputFrame[ticks];
            for (int t = 0; t < ticks; t++)
            {
                int tick = w.State.Tick;
                in1[t] = b1.Poll(tick);
                in2[t] = b2.Poll(tick);
                w.Step(in1[t], in2[t]);
            }
            Assert.Greater(w.State.Fighter0.Augments.Count + w.State.Fighter1.Augments.Count + w.State.Match.Picks0 + w.State.Match.Picks1, 0, "선택이 일어남");
            uint first = w.ComputeChecksum();
            var sync = new SyncTest<SimWorld>(Make());
            for (int t = 0; t < ticks; t++)
                sync.Advance(in1[t], in2[t]);
            Assert.AreEqual(0, sync.MismatchCount);
            var start = Make();
            var rec = InputRecording.Begin(config, arenas, start.State.Match.Rotation, 31, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.FormSelect, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                rec.Append(in1[t], in2[t]);
            Assert.AreEqual(first, InputRecording.FromBytes(rec.ToBytes()).Replay(config, arenas));

            // 대칭: 같은 증강을 양쪽에 주고 거울 입력이면 거울 결과.
            var m = World(config, -4f, 4f);
            m.State.Fighter0.Augments = Held(AugmentCatalog.LungeSpeed, AugmentCatalog.InstantLunge);
            m.State.Fighter1.Augments = Held(AugmentCatalog.InstantLunge, AugmentCatalog.LungeSpeed);
            for (int t = 0; t < 90; t++)
            {
                var press = t % 30 == 0 ? InputButtons.Attack : InputButtons.None;
                m.Step(new InputFrame(1, 0, press), new InputFrame(-1, 0, press));
                Assert.AreEqual(m.State.Fighter0.Position.X, -m.State.Fighter1.Position.X, 1e-4f, $"틱 {t}");
                Assert.AreEqual(m.State.Fighter0.Health, m.State.Fighter1.Health, $"틱 {t}");
            }
        }
    }
}
