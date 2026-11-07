using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP20: 보 폼과 속성 해석기(10-forms-and-growth.md 4.1·4.2). 상호작용 표의 모든 칸을 결정적 시나리오로 확인한다.
    /// 타이밍과 수치는 설정값(공격 데이터, 바위 수치)에서 정한다.
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class FormInteractionTests
    {
        const float FloorY = -7.55f;
        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("FormFlat2", (c, r) => '\0');

        static SimConfig Config(FighterForm p1, FighterForm p2, bool energy = false, System.Func<CombatParams, CombatParams> change = null)
        {
            var combat = CombatParams.Default;
            if (change != null)
                combat = change(combat);
            var rules = new RuleSet { Switches = new FeatureSwitches { EnergyEnabled = energy } }.WithForms(p1, p2);
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, rules);
        }

        static SimWorld World(SimConfig config, float x0, float x1, ArenaDef arena = null) =>
            TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x1, FloorY), config, arena ?? Flat);

        static InputFrame In(int x, int y, InputButtons b) => new InputFrame(x, y, b);

        static int Count(SimWorld w, SimEventType type, int fighter = -2)
        {
            int n = 0;
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && (fighter == -2 || w.Events[i].Fighter == fighter))
                    n++;
            }
            return n;
        }

        static void Inject(SimWorld w, Vec2 position, Vec2 velocity, int owner = 0, bool reflectable = true, int reflections = 0,
            bool pierceFighter = false, bool pierceProp = false)
        {
            ProjectileState.Get(ref w.State.Projectiles, owner * ProjectileState.PerFighter) = new Projectile
            {
                Active = true,
                Owner = owner,
                Position = position,
                Velocity = velocity,
                Reflectable = reflectable,
                PierceFighter = pierceFighter,
                PierceProp = pierceProp,
                Reflections = reflections,
                SpawnTick = w.State.Tick,
            };
        }

        /// <summary>이벤트를 세면서 ticks만큼 진행(입력 없음).</summary>
        sealed class Tally
        {
            public int Reflect, Hit0, Hit1, Stun0, Stun1, Interrupt0, Interrupt1, Clash, Cancelled;

            public void Add(SimWorld w)
            {
                Reflect += Count(w, SimEventType.ProjectileReflect);
                Hit0 += Count(w, SimEventType.Hit, 0);
                Hit1 += Count(w, SimEventType.Hit, 1);
                Stun0 += Count(w, SimEventType.LungeStun, 0);
                Stun1 += Count(w, SimEventType.LungeStun, 1);
                Interrupt0 += Count(w, SimEventType.AttackInterrupted, 0);
                Interrupt1 += Count(w, SimEventType.AttackInterrupted, 1);
                Clash += Count(w, SimEventType.Clash);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    if (w.Events[i].Type == SimEventType.ProjectileEnd && (int)w.Events[i].Strength == (int)ProjectileEndReason.Cancelled)
                        Cancelled++;
                }
            }
        }

        static Tally Run(SimWorld w, int ticks, System.Func<int, InputFrame> p1 = null, System.Func<int, InputFrame> p2 = null)
        {
            var tally = new Tally();
            for (int t = 0; t < ticks; t++)
            {
                w.Step(p1 != null ? p1(t) : default, p2 != null ? p2(t) : default);
                tally.Add(w);
            }
            return tally;
        }

        /// <summary>P2(보)가 지금 휘두르기를 누르고, 판정이 활성인 첫 틱까지 진행한다.</summary>
        static void SwingUntilActive(SimWorld w, int fighter)
        {
            var press = In(0, 0, InputButtons.Attack);
            w.Step(fighter == 0 ? press : default, fighter == 1 ? press : default);
            for (int t = 0; t < 30 && !FighterCombat.IsHitboxActive(w.State.GetFighter(fighter), w.Config); t++)
                w.Step(default, default);
            Assert.IsTrue(FighterCombat.IsHitboxActive(w.State.GetFighter(fighter), w.Config), "활성 판정");
        }

        /// <summary>P2 보의 판정 원 가운데(Circle1) 왼쪽 앞에서 오른쪽으로 날아오는 P1의 투사체.</summary>
        static void InjectIntoSwing(SimWorld w, bool reflectable = true, int reflections = 0, bool pierce = false)
        {
            ref readonly var f = ref w.State.Fighter1;
            ref readonly var attack = ref w.Config.GetAttack(f.AttackId);
            var center = FighterCombat.CircleCenter(f, attack, attack.Circle1);
            float speed = w.Config.Combat.Rock.Speed;
            // 판정 원 앞, 몸 높이(몸 중심 + 0.4)에서: 판정 원과 몸 둘 다 지나는 높이.
            Inject(w, new Vec2(center.X - 0.6f, f.Position.Y + 0.4f), new Vec2(speed, 0f), 0, reflectable, reflections, pierce, pierce);
        }

        // CP20-1: 상호작용 표의 모든 칸 -----------------------------------------------

        [Test]
        public void Projectile_IntoLungingFighter_BreaksLungeWithShortStun()
        {
            var config = Config(FighterForm.Rock, FighterForm.Scissors);
            var w = World(config, -8f, 4f);
            // P2가 최소 돌진으로 왼쪽으로 날아오고, 그 앞에 P1의 투사체를 놓는다.
            w.Step(default, In(-1, 0, InputButtons.Attack));
            w.Step(default, default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter1.Action);
            Inject(w, w.State.Fighter1.Position + new Vec2(-1.2f, 0f), new Vec2(config.Combat.Rock.Speed, 0f));
            var tally = Run(w, 6);
            Assert.AreEqual(1, tally.Hit1, "피해");
            Assert.AreEqual(1, tally.Stun1, "짧은 기절");
            Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Rock.Damage, w.State.Fighter1.Health);
            Assert.AreNotEqual(ActionState.Lunge, w.State.Fighter1.Action, "돌진이 끊김");
        }

        [Test]
        public void Projectile_IntoActiveSwing_IsReflected()
        {
            var w = World(Config(FighterForm.Rock, FighterForm.Paper), -6f, 2f);
            SwingUntilActive(w, 1);
            InjectIntoSwing(w);
            var tally = Run(w, 1);
            Assert.AreEqual(1, tally.Reflect, "반사");
            var p = ProjectileState.At(w.State.Projectiles, 0);
            Assert.IsTrue(p.Active);
            Assert.AreEqual(1, p.Owner, "소유자가 반사한 쪽으로");
            Assert.Less(p.Velocity.X, 0f, "진행 방향이 반대");
        }

        [Test]
        public void Lunge_IntoActiveSwing_AbovePowerThreshold_LungeWins_BelowThreshold_SwingHits()
        {
            foreach (bool strong in new[] { true, false })
            {
                // 약한 경우는 돌진 공격성을 낮춰 위력이 임계(6) 아래가 되게 한다(데이터).
                var config = Config(FighterForm.Scissors, FighterForm.Paper,
                    change: c => { if (!strong) c.Charge.LungeAggression = 5f / c.Charge.LungeMinSpeed; return c; });
                Assert.AreEqual(strong, config.Combat.Charge.LungeMinSpeed * config.Combat.Charge.LungeAggression >= config.Combat.Charge.PowerThreshold);
                bool found = false;
                // 보가 누르는 틱을 바꿔 가며, 돌진이 활성 판정에 닿는 경기를 찾는다.
                for (int press = 0; press < 14 && !found; press++)
                {
                    var w = World(config, -4f, 1f);
                    int pressTick = press;
                    var tally = Run(w, 40,
                        t => t == 0 ? In(1, 0, InputButtons.Attack) : default,
                        t => t == pressTick ? In(-1, 0, InputButtons.Attack) : default);
                    if (strong && tally.Interrupt1 > 0)
                    {
                        found = true;
                        Assert.AreEqual(1, tally.Hit1, $"누름 {press}: 보가 피해");
                        Assert.AreEqual(0, tally.Hit0, $"누름 {press}: 돌진은 맞지 않음");
                        Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter0.Health);
                    }
                    else if (!strong && tally.Hit0 > 0)
                    {
                        found = true;
                        Assert.AreEqual(0, tally.Interrupt1, $"누름 {press}: 임계 미만이면 중단 없음");
                        Assert.AreEqual(0, tally.Hit1, $"누름 {press}: 보는 맞지 않음");
                    }
                }
                Assert.IsTrue(found, strong ? "돌진이 이기는 경기" : "보의 판정이 맞는 경기");
            }
        }

        [Test]
        public void LungeContinues_AfterBreakingSwing()
        {
            var config = Config(FighterForm.Scissors, FighterForm.Paper);
            for (int press = 0; press < 14; press++)
            {
                var w = World(config, -4f, 1f);
                int pressTick = press;
                for (int t = 0; t < 40; t++)
                {
                    w.Step(t == 0 ? In(1, 0, InputButtons.Attack) : default, t == pressTick ? In(-1, 0, InputButtons.Attack) : default);
                    if (Count(w, SimEventType.AttackInterrupted, 1) > 0)
                    {
                        Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action, "돌진은 계속");
                        return;
                    }
                }
            }
            Assert.Fail("돌진이 보를 이기는 경기를 찾지 못함");
        }

        [Test]
        public void ScissorsVsScissors_EqualLunges_Clash()
        {
            var w = World(Config(FighterForm.Scissors, FighterForm.Scissors), -3f, 3f);
            var tally = Run(w, 30, t => t == 0 ? In(1, 0, InputButtons.Attack) : default, t => t == 0 ? In(-1, 0, InputButtons.Attack) : default);
            Assert.AreEqual(1, tally.Clash);
            Assert.AreEqual(0, tally.Hit0 + tally.Hit1);
        }

        [Test]
        public void PaperVsPaper_SimultaneousSwings_Clash()
        {
            var w = World(Config(FighterForm.Paper, FighterForm.Paper), -1.2f, 1.2f);
            var tally = Run(w, 30, t => t == 0 ? In(1, 0, InputButtons.Attack) : default, t => t == 0 ? In(-1, 0, InputButtons.Attack) : default);
            Assert.AreEqual(1, tally.Clash);
            Assert.AreEqual(0, tally.Hit0 + tally.Hit1);
        }

        [Test]
        public void ProjectileVsProjectile_BothVanish()
        {
            var config = Config(FighterForm.Rock, FighterForm.Rock);
            var w = World(config, -5f, 5f);
            var tally = Run(w, 60, t => t == 0 ? In(1, 0, InputButtons.Attack) : default, t => t == 0 ? In(-1, 0, InputButtons.Attack) : default);
            Assert.AreEqual(2, tally.Cancelled);
            Assert.AreEqual(0, tally.Hit0 + tally.Hit1);
            Assert.AreEqual(0, w.State.Projectiles.CountActive(-1));
        }

        [TestCase(FighterForm.Scissors)]
        [TestCase(FighterForm.Rock)]
        [TestCase(FighterForm.Paper)]
        public void Projectile_IntoNonAttackingFighter_NormalHit(FighterForm target)
        {
            var config = Config(FighterForm.Rock, target);
            var w = World(config, -5f, 2f);
            Inject(w, new Vec2(0f, FloorY), new Vec2(config.Combat.Rock.Speed, 0f));
            var tally = Run(w, 10);
            Assert.AreEqual(1, tally.Hit1);
            Assert.AreEqual(0, tally.Stun1 + tally.Reflect);
            Assert.AreEqual(ActionState.Hitstun, w.State.Fighter1.Action);
            Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Rock.Damage, w.State.Fighter1.Health);
        }

        [TestCase(FighterForm.Scissors, FighterForm.Rock)]
        [TestCase(FighterForm.Scissors, FighterForm.Paper)]
        [TestCase(FighterForm.Paper, FighterForm.Rock)]
        [TestCase(FighterForm.Paper, FighterForm.Scissors)]
        public void Attack_IntoNonAttackingTarget_NormalHit(FighterForm attacker, FighterForm target)
        {
            var config = Config(attacker, target);
            float gap = attacker == FighterForm.Paper ? 1.4f : 3f;
            var w = World(config, -gap * 0.5f, gap * 0.5f);
            var tally = Run(w, 30, t => t == 0 ? In(1, 0, InputButtons.Attack) : default);
            Assert.AreEqual(1, tally.Hit1, "일반 명중");
            Assert.AreEqual(0, tally.Hit0 + tally.Clash + tally.Interrupt1);
        }

        // CP20-2: 반사 조건 ----------------------------------------------------------

        [Test]
        public void SwingStartupAndRecovery_DoNotReflect_ProjectileHitsBody()
        {
            var config = Config(FighterForm.Rock, FighterForm.Paper);
            ref readonly var attack = ref config.Combat.Swing.Forward;
            float speed = config.Combat.Rock.Speed;
            float bodyEdge = 2f - config.HalfBody - config.Combat.Rock.Radius;

            // 선딜: 누르자마자 몸 바로 앞에서.
            var startup = World(config, -6f, 2f);
            startup.Step(default, In(0, 0, InputButtons.Attack));
            Inject(startup, new Vec2(bodyEdge - 0.2f, FloorY), new Vec2(speed, 0f));
            var a = Run(startup, 2);
            Assert.Less(startup.State.Fighter1.ActionTick, attack.StartupTicks + 1, "아직 선딜");
            Assert.AreEqual(0, a.Reflect, "선딜에는 반사 없음");
            Assert.AreEqual(1, a.Hit1, "몸에 일반 명중");

            // 후딜: 활성이 끝난 뒤.
            var recovery = World(config, -6f, 2f);
            recovery.Step(default, In(0, 0, InputButtons.Attack));
            for (int t = 0; t < attack.StartupTicks + attack.ActiveTicks; t++)
                recovery.Step(default, default);
            Assert.IsFalse(FighterCombat.IsHitboxActive(recovery.State.Fighter1, config));
            Assert.AreEqual(ActionState.Attack, recovery.State.Fighter1.Action, "후딜");
            Inject(recovery, new Vec2(bodyEdge - 0.2f, FloorY), new Vec2(speed, 0f));
            var b = Run(recovery, 2);
            Assert.AreEqual(0, b.Reflect, "후딜에는 반사 없음");
            Assert.AreEqual(1, b.Hit1);
        }

        [Test]
        public void NonReflectable_PassesThroughSwing_HitsBody()
        {
            var config = Config(FighterForm.Rock, FighterForm.Paper);
            var w = World(config, -6f, 2f);
            SwingUntilActive(w, 1);
            InjectIntoSwing(w, reflectable: false);
            var tally = Run(w, 6);
            Assert.AreEqual(0, tally.Reflect);
            Assert.AreEqual(1, tally.Hit1, "보를 일반 명중");
            Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Rock.Damage, w.State.Fighter1.Health);
            Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter0.Health);
        }

        [Test]
        public void Reflected_HitsOriginalOwner_ReflectorUnhurt()
        {
            var config = Config(FighterForm.Rock, FighterForm.Paper);
            var w = World(config, -4f, 2f);
            SwingUntilActive(w, 1);
            InjectIntoSwing(w);
            var tally = Run(w, 40);
            Assert.AreEqual(1, tally.Reflect);
            Assert.AreEqual(1, tally.Hit0, "원래 소유자가 맞음");
            Assert.AreEqual(0, tally.Hit1);
            Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Rock.Damage, w.State.Fighter0.Health);
            Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        [Test]
        public void ReflectLimit_AndOncePerActiveWindow()
        {
            var config = Config(FighterForm.Rock, FighterForm.Paper);
            // 상한에 닿은 투사체는 반사되지 않고 몸을 맞힌다.
            var capped = World(config, -6f, 2f);
            SwingUntilActive(capped, 1);
            InjectIntoSwing(capped, reflections: config.Combat.Rock.MaxReflections);
            var a = Run(capped, 6);
            Assert.AreEqual(0, a.Reflect);
            Assert.AreEqual(1, a.Hit1);

            // 한 활성 판정은 같은 투사체를 한 번만.
            var once = World(config, -6f, 2f);
            SwingUntilActive(once, 1);
            InjectIntoSwing(once);
            var b = Run(once, config.Combat.Swing.Forward.ActiveTicks + 2);
            Assert.AreEqual(1, b.Reflect);
            Assert.AreEqual(1, ProjectileState.At(once.State.Projectiles, 0).Reflections);
        }

        // CP20-3: 속성 구분 -----------------------------------------------------------

        [Test]
        public void PiercingProjectile_IsStillReflectedBySwing()
        {
            var w = World(Config(FighterForm.Rock, FighterForm.Paper), -6f, 2f);
            SwingUntilActive(w, 1);
            InjectIntoSwing(w, pierce: true);
            var tally = Run(w, 1);
            Assert.AreEqual(1, tally.Reflect, "관통도 반사된다");
            Assert.AreEqual(0, tally.Hit1);
        }

        // CP20-4: 동시 처리와 대칭 --------------------------------------------------------

        static InputFrame Script(int t, int toward, bool first)
        {
            int[] presses = first ? new[] { 0, 40, 80 } : new[] { 3, 45, 85 };
            var b = System.Array.IndexOf(presses, t) >= 0 ? InputButtons.Attack : InputButtons.None;
            return In(t % 30 < 20 ? toward : 0, 0, b);
        }

        [TestCase(FighterForm.Rock, FighterForm.Paper)]
        [TestCase(FighterForm.Scissors, FighterForm.Paper)]
        [TestCase(FighterForm.Rock, FighterForm.Scissors)]
        public void SwappedRoles_GiveSwappedResults_NoIndexPriority(FighterForm x, FighterForm y)
        {
            // 경기 A: P1 = x(왼쪽), P2 = y(오른쪽). 경기 B: P1 = y(왼쪽), P2 = x(오른쪽). B는 A의 거울이어야 한다.
            var a = World(Config(x, y), -3f, 3f, TestWorlds.Box);
            var b = World(Config(y, x), -3f, 3f, TestWorlds.Box);
            for (int t = 0; t < 140; t++)
            {
                a.Step(Script(t, 1, true), Script(t, -1, false));
                b.Step(Script(t, 1, false), Script(t, -1, true));
                AssertMirror(a.State.Fighter0, b.State.Fighter1, $"틱 {t} x");
                AssertMirror(a.State.Fighter1, b.State.Fighter0, $"틱 {t} y");
            }
        }

        [TestCase(FighterForm.Scissors)]
        [TestCase(FighterForm.Rock)]
        [TestCase(FighterForm.Paper)]
        public void SameTickMutualAttacks_AreMirrored(FighterForm form)
        {
            var w = World(Config(form, form), -2.5f, 2.5f, TestWorlds.Box);
            for (int t = 0; t < 120; t++)
            {
                var press = t % 30 == 0 ? InputButtons.Attack : InputButtons.None;
                w.Step(In(1, 0, press), In(-1, 0, press));
                AssertMirror(w.State.Fighter0, w.State.Fighter1, $"틱 {t}");
            }
        }

        static void AssertMirror(in FighterState p, in FighterState q, string where)
        {
            Assert.AreEqual(p.Position.X, -q.Position.X, 1e-4f, $"{where}: x");
            Assert.AreEqual(p.Position.Y, q.Position.Y, 1e-4f, $"{where}: y");
            Assert.AreEqual(p.Health, q.Health, $"{where}: 체력");
            Assert.AreEqual(p.Action, q.Action, $"{where}: 행동");
        }

        // CP20-5: 결정론 -----------------------------------------------------------------

        [TestCase(FighterForm.Rock, FighterForm.Paper, 201u)]
        [TestCase(FighterForm.Scissors, FighterForm.Rock, 202u)]
        [TestCase(FighterForm.Paper, FighterForm.Scissors, 203u)]
        public void MixedForms_Deterministic_SyncTest_Replay(FighterForm p1, FighterForm p2, uint seed)
        {
            const int ticks = 3600;
            var config = Config(p1, p2, energy: true);
            var arena = TestWorlds.Box;
            TestWorlds.RandomInputs(seed, ticks, out var in1, out var in2);
            var w = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            for (int t = 0; t < ticks; t++)
                w.Step(in1[t], in2[t]);
            uint first = w.ComputeChecksum();
            var again = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            for (int t = 0; t < ticks; t++)
                again.Step(in1[t], in2[t]);
            Assert.AreEqual(first, again.ComputeChecksum());

            var sync = new SyncTest<SimWorld>(TestWorlds.Create(MatchPhase.Fighting, seed, config, arena));
            for (int t = 0; t < ticks; t++)
                sync.Advance(in1[t], in2[t]);
            Assert.AreEqual(0, sync.MismatchCount, "SyncTest");

            var start = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            var recording = InputRecording.Begin(config, arena, seed, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.Fighting, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                recording.Append(in1[t], in2[t]);
            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.IsTrue(RuleSet.TryParse(loaded.Mode, out var rules) && rules.Equals(config.Rules), loaded.Mode);
            Assert.AreEqual(first, loaded.Replay(config, arena), "입력 기록 재생");
        }
    }
}
