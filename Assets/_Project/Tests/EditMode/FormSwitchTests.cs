using System.IO;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP21: 폼 전환(10-forms-and-growth.md 4.5, 5.2). 타이밍은 공격 데이터에서 정한다.</summary>
    [Category(TestCategories.LegacyCombat)]
    public class FormSwitchTests
    {
        const float FloorY = -7.55f;
        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("SwitchFlat", (c, r) => '\0');

        static SimConfig Config(FighterForm start, bool energy = true, FighterForm? p2 = null) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = new FeatureSwitches { EnergyEnabled = energy }, OwnedForms = Forms.AllPlayable }.WithForms(start, p2 ?? start));

        static SimWorld World(SimConfig config, float x0 = -3f, float x1 = 6f) =>
            TestWorlds.CreateAt(new Vec2(x0, FloorY), new Vec2(x1, FloorY), config, Flat);

        static InputFrame In(InputButtons b, int x = 0) => new InputFrame(x, 0, b);
        static readonly InputFrame Switch = new InputFrame(0, 0, InputButtons.SwitchForm);

        static int Count(SimWorld w, SimEventType type, int fighter)
        {
            int n = 0;
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                    n++;
            }
            return n;
        }

        [Test]
        public void Switch_CyclesOwnedForms_OnlyOwned()
        {
            var w = World(Config(FighterForm.Scissors));
            w.Step(Switch, default);
            Assert.AreEqual(FighterForm.Rock, w.State.Fighter0.Form);
            w.Step(default, default);
            w.Step(Switch, default);
            Assert.AreEqual(FighterForm.Paper, w.State.Fighter0.Form);
            w.Step(default, default);
            w.Step(Switch, default);
            Assert.AreEqual(FighterForm.Scissors, w.State.Fighter0.Form);

            // 가위와 보만 가졌으면 바위를 건너뛴다. 하나만 가졌으면 그대로.
            var two = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { StartForm = FighterForm.Scissors, OwnedForms = (byte)(Forms.Bit(FighterForm.Scissors) | Forms.Bit(FighterForm.Paper)) });
            var w2 = World(two);
            w2.Step(Switch, default);
            Assert.AreEqual(FighterForm.Paper, w2.State.Fighter0.Form);
            var one = World(new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, new RuleSet { StartForm = FighterForm.Rock }));
            one.Step(Switch, default);
            Assert.AreEqual(FighterForm.Rock, one.State.Fighter0.Form);
            Assert.AreEqual(0, Count(one, SimEventType.FormSwitch, 0));
        }

        // CP21-1 ----------------------------------------------------------------

        [Test]
        public void SwitchIgnored_DuringAttackRecoveryAndStun()
        {
            var config = Config(FighterForm.Rock);
            ref readonly var rock = ref config.Combat.Rock;

            // 바위 선딜 중, 후딜 중.
            var w = World(config);
            w.Step(In(InputButtons.Attack), default);
            for (int t = 1; t <= rock.TotalTicks; t++)
            {
                w.Step(t % 2 == 1 ? Switch : default, default);
                if (w.State.Fighter0.Action == ActionState.Shoot)
                    Assert.AreEqual(FighterForm.Rock, w.State.Fighter0.Form, $"틱 {t}: 공격 중 전환이 받아들여짐");
            }
            Assert.AreEqual(FighterForm.Rock, w.State.Fighter0.Form);

            // 경직 중.
            var s = World(config);
            s.State.Fighter0.StunTicks = 20;
            s.State.Fighter0.RestartAction(ActionState.Hitstun);
            for (int t = 0; t < 15; t++)
                s.Step(t % 2 == 0 ? Switch : default, default);
            Assert.AreEqual(ActionState.Hitstun, s.State.Fighter0.Action);
            Assert.AreEqual(FighterForm.Rock, s.State.Fighter0.Form, "경직 중 전환 무시");
            Assert.AreEqual(0, Count(s, SimEventType.FormSwitch, 0));
        }

        [Test]
        public void AfterLunge_SwitchThenAttack_PaysEnergyAndNewStartup()
        {
            var config = Config(FighterForm.Scissors);
            ref readonly var e = ref config.Combat.Energy;
            var w = World(config, -12f, 12f);
            w.Step(In(InputButtons.Attack, 1), default);
            w.Step(default, default);
            Assert.AreEqual(ActionState.Lunge, w.State.Fighter0.Action);
            while (w.State.Fighter0.Action == ActionState.Lunge)
                w.Step(default, default);
            int before = w.State.Fighter0.Energy;
            // 돌진이 끝난 첫 틱에 전환과 공격을 함께 누른다: 바위로 바뀌고 바위의 선딜이 처음부터 시작한다.
            w.Step(new InputFrame(0, 0, InputButtons.SwitchForm | InputButtons.Attack), default);
            Assert.AreEqual(FighterForm.Rock, w.State.Fighter0.Form, "보유 순서상 가위 다음은 바위");
            Assert.AreEqual(ActionState.Shoot, w.State.Fighter0.Action);
            Assert.AreEqual(0, w.State.Fighter0.ActionTick, "새 폼의 선딜이 처음부터");
            int fired = 0;
            for (int t = 0; t < config.Combat.Rock.TotalTicks; t++)
            {
                w.Step(default, default);
                fired += Count(w, SimEventType.ProjectileFire, 0);
                if (fired == 0)
                    Assert.LessOrEqual(t + 1, config.Combat.Rock.StartupTicks, "선딜보다 먼저 발사하지 않음");
            }
            Assert.AreEqual(1, fired);
            Assert.AreEqual(before - e.ShotCost, w.State.Fighter0.Energy, "새 폼의 비용");
        }

        // CP21-2 ----------------------------------------------------------------

        [Test]
        public void Switch_KeepsEnergyAndFlyingProjectiles()
        {
            var config = Config(FighterForm.Rock);
            var w = World(config, -10f, 10f);
            w.Step(In(InputButtons.Attack, 1), default);
            for (int t = 0; t < config.Combat.Rock.TotalTicks + 1; t++)
                w.Step(default, default);
            Assert.AreEqual(1, w.State.Projectiles.CountActive(0));
            int energy = w.State.Fighter0.Energy;
            int delay = w.State.Fighter0.EnergyRegenDelay;
            w.Step(Switch, default);
            Assert.AreEqual(FighterForm.Paper, w.State.Fighter0.Form);
            Assert.AreEqual(energy, w.State.Fighter0.Energy - (delay == 0 ? config.Combat.Energy.RegenPerTick : 0), "전환은 에너지를 바꾸지 않음(회복만)");
            Assert.AreEqual(1, w.State.Projectiles.CountActive(0), "바위로 쏜 투사체는 전환해도 계속 날아감");
            w.Step(default, default);
            w.Step(Switch, default);
            w.Step(default, default);
            w.Step(Switch, default);
            Assert.AreEqual(FighterForm.Rock, w.State.Fighter0.Form);
            Assert.AreEqual(1, w.State.Projectiles.CountActive(0));
        }

        // CP21-3 ----------------------------------------------------------------

        [Test]
        public void SwitchPressedInHitstop_AppliesAfterHitstop()
        {
            var w = World(Config(FighterForm.Scissors));
            w.State.Fighter0.HitstopTicks = 5;
            w.Step(Switch, default);
            for (int t = 0; t < 3; t++)
            {
                w.Step(default, default);
                Assert.AreEqual(FighterForm.Scissors, w.State.Fighter0.Form, "히트스탑 중에는 바뀌지 않음");
            }
            bool switched = false;
            for (int t = 0; t < 3 && !switched; t++)
            {
                w.Step(default, default);
                switched = w.State.Fighter0.Form == FighterForm.Rock;
            }
            Assert.IsTrue(switched, "히트스탑이 끝난 뒤 처리됨");
        }

        // CP21-4, CP21-6 --------------------------------------------------------

        [TestCase(211u)]
        [TestCase(212u)]
        public void BotsSwitching_Deterministic_SyncTest_Replay_AndMetrics(uint seed)
        {
            const int ticks = 3600;
            var config = Config(FighterForm.Scissors, true, FighterForm.Paper);
            var arena = ArenaFiles.LoadByName("Ruins");
            // 봇 입력을 한 번 기록해 같은 입력으로 다시 돌린다.
            var w = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            var b1 = new BotInputSource(() => w, 0, 300u + seed);
            var b2 = new BotInputSource(() => w, 1, 700u + seed);
            var in1 = new InputFrame[ticks];
            var in2 = new InputFrame[ticks];
            var metrics = new SwitchMetrics();
            for (int t = 0; t < ticks; t++)
            {
                int tick = w.State.Tick;
                in1[t] = b1.Poll(tick);
                in2[t] = b2.Poll(tick);
                var before = w.State;
                w.Step(in1[t], in2[t]);
                metrics.Observe(before, w.State, w.Events);
            }
            uint first = w.ComputeChecksum();
            Assert.Greater(metrics.Switches, 0, "봇이 전환함");

            var again = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            for (int t = 0; t < ticks; t++)
                again.Step(in1[t], in2[t]);
            Assert.AreEqual(first, again.ComputeChecksum());

            var sync = new SyncTest<SimWorld>(TestWorlds.Create(MatchPhase.Countdown, seed, config, arena));
            for (int t = 0; t < ticks; t++)
                sync.Advance(in1[t], in2[t]);
            Assert.AreEqual(0, sync.MismatchCount, "SyncTest");

            var start = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            var recording = InputRecording.Begin(config, arena, seed, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.Countdown, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                recording.Append(in1[t], in2[t]);
            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.IsTrue(RuleSet.TryParse(loaded.Mode, out var rules) && rules.Equals(config.Rules), loaded.Mode);
            Assert.AreEqual(first, loaded.Replay(config, arena), "입력 기록 재생(전환 포함)");

            string report = $"[Knockbox][CP21-6] 시드 {seed} Ruins 가위/보(세 폼 보유) 1분: {metrics.Summary()}";
            TestContext.WriteLine(report);
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/switch-metrics.txt", report + "\n");
        }
    }
}
