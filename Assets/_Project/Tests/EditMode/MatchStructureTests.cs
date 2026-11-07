using System.IO;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP22: 라운드·세트·경기와 세트 패자 선택(10-forms-and-growth.md 4.6·4.7 개정, D-049~D-054).
    /// 승패는 KO를 직접 만들어(체력 0) 빠르게 진행한다. 승수·세트 수는 설정값에서 읽는다.
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class MatchStructureTests
    {
        static SimConfig Config(FighterForm p1 = FighterForm.Scissors, FighterForm p2 = FighterForm.Scissors) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = FeatureSwitches.Default }.WithForms(p1, p2));

        static readonly string[] Maps = { "Chimes", "Gantry", "Mobile", "Ruins", "Scales", "Totems" };

        static SimWorld Match(SimConfig config, uint seed = 3, MatchPhase start = MatchPhase.Countdown)
        {
            var arenas = new ArenaDef[Maps.Length];
            for (int i = 0; i < Maps.Length; i++)
                arenas[i] = ArenaFiles.LoadByName(Maps[i]);
            return new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, seed, start));
        }

        static InputFrame Press(InputButtons b) => new InputFrame(0, 0, b);

        /// <summary>선택 단계면 1번(점프)을 고르게 하며 전투 단계까지 진행.</summary>
        static void ToFighting(SimWorld w, int option = 0)
        {
            for (int t = 0; t < 3000 && w.State.Match.Phase != MatchPhase.Fighting; t++)
            {
                var b = (t & 1) == 0 ? MatchFlow.OptionButton(option) : InputButtons.None;
                bool select = w.State.Match.Phase == MatchPhase.FormSelect || w.State.Match.Phase == MatchPhase.SetPick;
                w.Step(select ? Press(b) : default, select ? Press(b) : default);
            }
            Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
        }

        /// <summary>전투 중 loser를 KO시키고(-1이면 둘 다) 라운드 다음 단계(RoundTransition, SetPick, MatchEnd)까지.</summary>
        static void FinishRound(SimWorld w, int loser)
        {
            ToFighting(w);
            if (loser != 1)
                w.State.Fighter0.Health = 0;
            if (loser != 0)
                w.State.Fighter1.Health = 0;
            for (int t = 0; t < 200; t++)
            {
                w.Step(default, default);
                var p = w.State.Match.Phase;
                if (p == MatchPhase.RoundTransition || p == MatchPhase.SetPick || p == MatchPhase.MatchEnd)
                    return;
            }
            Assert.Fail($"라운드가 끝나지 않음({w.State.Match.Phase})");
        }

        /// <summary>SetPick에서 고르는 쪽이 index번을 고르고 단계가 끝날 때까지.</summary>
        static void Pick(SimWorld w, int index)
        {
            Assert.AreEqual(MatchPhase.SetPick, w.State.Match.Phase);
            int picker = w.State.Match.Picker;
            for (int t = 0; t < 400 && w.State.Match.Phase == MatchPhase.SetPick; t++)
            {
                var b = (t & 1) == 0 ? Press(MatchFlow.OptionButton(index)) : default;
                w.Step(picker == 0 ? b : default, picker == 1 ? b : default);
            }
            Assert.AreNotEqual(MatchPhase.SetPick, w.State.Match.Phase);
        }

        /// <summary>P(winner)가 한 세트를 2:0으로 이긴다(마지막 라운드 뒤 단계에서 멈춤).</summary>
        static void WinSet(SimWorld w, int winner)
        {
            for (int r = 0; r < w.Config.Match.RoundsToWinSet; r++)
                FinishRound(w, 1 - winner);
        }

        // CP22-1 ----------------------------------------------------------------

        [Test]
        public void SetAndMatchTransitions()
        {
            var config = Config();
            ref readonly var m = ref config.Match;
            Assert.AreEqual(2, m.RoundsToWinSet);
            Assert.AreEqual(5, m.SetsToWinMatch);

            // 2:0
            var w = Match(config);
            FinishRound(w, 1);
            Assert.AreEqual(MatchPhase.RoundTransition, w.State.Match.Phase, "1승으로는 세트가 끝나지 않음");
            Assert.AreEqual(1, w.State.Match.Score0);
            FinishRound(w, 1);
            Assert.AreEqual(1, w.State.Match.SetScore0, "2승으로 세트 획득");
            Assert.AreEqual(0, w.State.Match.Score0 + w.State.Match.Score1, "세트가 끝나면 라운드 점수 초기화");
            Assert.AreEqual(2, w.State.Match.Set);

            // 2:1
            var x = Match(config);
            FinishRound(x, 1);
            FinishRound(x, 0);
            Assert.AreEqual(1, x.State.Match.Score0);
            Assert.AreEqual(1, x.State.Match.Score1);
            FinishRound(x, 1);
            Assert.AreEqual(1, x.State.Match.SetScore0);
            Assert.AreEqual(0, x.State.Match.SetScore1);

            // 5세트 선승 → 경기 종료 → 재시작
            var y = Match(config);
            for (int s = 0; s < m.SetsToWinMatch; s++)
            {
                WinSet(y, 0);
                if (y.State.Match.Phase == MatchPhase.SetPick)
                    Pick(y, 0);
            }
            Assert.AreEqual(MatchPhase.MatchEnd, y.State.Match.Phase);
            Assert.AreEqual(m.SetsToWinMatch, y.State.Match.SetScore0);
            for (int t = 0; t <= m.MatchEndMinTicks + 2 && y.State.Match.Phase == MatchPhase.MatchEnd; t++)
                y.Step((t & 1) == 0 ? Press(InputButtons.Jump) : default, default);
            Assert.AreEqual(MatchPhase.FormSelect, y.State.Match.Phase, "재시작은 초기 폼 선택부터");
            Assert.AreEqual(0, y.State.Match.SetScore0 + y.State.Match.SetScore1);
        }

        // CP22-2, CP22-8 ------------------------------------------------------

        [Test]
        public void OnlySetLoserPicks_NoPickAfterRoundOrDraw_NoPickWhenMatchEnds()
        {
            var w = Match(Config());
            FinishRound(w, 1);
            Assert.AreEqual(MatchPhase.RoundTransition, w.State.Match.Phase, "라운드 뒤 선택 없음");
            FinishRound(w, -1);
            Assert.AreEqual(MatchPhase.RoundTransition, w.State.Match.Phase, "무승부 뒤 선택 없음");
            Assert.AreEqual(1, w.State.Match.Score0, "무승부는 점수 없음");
            Assert.AreEqual(0, w.State.Match.Score1);
            FinishRound(w, 1);
            Assert.AreEqual(MatchPhase.SetPick, w.State.Match.Phase);
            Assert.AreEqual(1, w.State.Match.Picker, "세트 패자만");
            Pick(w, 0);
            Assert.AreEqual(0, w.State.Match.Picks0, "세트 승자는 선택이 없음");
            Assert.AreEqual(1, w.State.Match.Picks1);

            // 경기를 끝내는 세트 뒤에는 선택이 없다.
            var z = Match(Config());
            for (int s = 0; s < z.Config.Match.SetsToWinMatch - 1; s++)
            {
                WinSet(z, 0);
                if (z.State.Match.Phase == MatchPhase.SetPick)
                    Pick(z, 0);
            }
            WinSet(z, 0);
            Assert.AreEqual(MatchPhase.MatchEnd, z.State.Match.Phase);
        }

        [Test]
        public void DrawRound_ReplaysWithRoundResetOnly()
        {
            var w = Match(Config());
            FinishRound(w, 1);
            ToFighting(w);
            w.State.Fighter1.OwnedForms = Forms.AllPlayable;
            w.Step(default, Press(InputButtons.SwitchForm));
            var form = w.State.Fighter1.Form;
            int set = w.State.Match.Set;
            FinishRound(w, -1);
            Assert.AreEqual(1, w.State.Match.Score0);
            Assert.AreEqual(set, w.State.Match.Set);
            ToFighting(w);
            Assert.AreEqual(form, w.State.Fighter1.Form, "무승부 뒤에도 폼 유지");
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        // CP22-3, CP22-6 ------------------------------------------------------

        [Test]
        public void FormAcquisition_ExcludesOwned_AndStartsNextRoundWithNewForm()
        {
            var w = Match(Config(FighterForm.Scissors, FighterForm.Rock));
            WinSet(w, 0);
            Assert.AreEqual(MatchPhase.SetPick, w.State.Match.Phase);
            var m = w.State.Match;
            // 가진 바위는 빼고 가위·보 획득, 그다음 바위 증강(철갑탄, CP23).
            Assert.AreEqual(3, m.OptionCount);
            Assert.AreEqual(PickOption.Form(FighterForm.Scissors), m.Option0);
            Assert.AreEqual(PickOption.Form(FighterForm.Paper), m.Option1);
            Assert.AreEqual(PickOption.AugmentBase + AugmentCatalog.ArmorPiercing, m.Option2);
            Pick(w, 1);
            ToFighting(w);
            Assert.AreEqual(FighterForm.Paper, w.State.Fighter1.Form, "새로 얻은 폼으로 시작");
            Assert.IsTrue(Forms.Owns(w.State.Fighter1.OwnedForms, FighterForm.Rock) && Forms.Owns(w.State.Fighter1.OwnedForms, FighterForm.Paper));
            w.Step(default, Press(InputButtons.SwitchForm));
            Assert.AreEqual(FighterForm.Rock, w.State.Fighter1.Form, "보유 목록이 늘어 전환 가능");

            // 승자(P1)는 직전 현재 폼으로, 라운드 중 전환해 둔 폼은 다음 세트로 이어진다.
            w.Step(default, default);
            w.Step(default, Press(InputButtons.SwitchForm));
            Assert.AreEqual(FighterForm.Paper, w.State.Fighter1.Form);
            FinishRound(w, 0);
            ToFighting(w);
            Assert.AreEqual(FighterForm.Paper, w.State.Fighter1.Form, "라운드 중 전환한 폼으로 다음 라운드 시작");
            Assert.AreEqual(FighterForm.Scissors, w.State.Fighter0.Form);
        }

        // CP22-4, CP22-5 ------------------------------------------------------

        [Test]
        public void RoundStart_ResetsCombatState_KeepsMatchPart_NoLeak()
        {
            var config = Config(FighterForm.Rock, FighterForm.Scissors);
            var w = Match(config, 7);
            var b1 = new BotInputSource(() => w, 0, 11u);
            var b2 = new BotInputSource(() => w, 1, 13u);
            // 봇이 싸우다가 둘 다 무언가를 하는 중에(투사체, 충전 등) KO를 만든다.
            ToFighting(w);
            int t = 0;
            for (; t < 1200; t++)
            {
                int tick = w.State.Tick;
                w.Step(b1.Poll(tick), b2.Poll(tick));
                if (w.State.Match.Phase != MatchPhase.Fighting)
                    ToFighting(w);
                if (t > 300 && w.State.Projectiles.CountActive(-1) > 0 && w.State.Fighter1.Action != ActionState.Idle)
                    break;
            }
            Assert.Greater(w.State.Projectiles.CountActive(-1), 0, "투사체가 날고 있는 장면");
            w.State.Fighter1.OwnedForms = Forms.AllPlayable;
            int score0 = w.State.Match.Score0, score1 = w.State.Match.Score1;
            w.State.Fighter1.Health = 0;
            var keep0 = w.State.Fighter0;
            var keep1 = w.State.Fighter1;
            int picks = w.State.Match.Picks0;
            while (w.State.Match.Phase != MatchPhase.Countdown)
            {
                w.Step(default, default);
                if (w.State.Match.Phase == MatchPhase.SetPick)
                    Pick(w, 0);
            }
            var s = w.State;
            // 같은 맵·같은 폼으로 새로 시작한 라운드와 전투 부분이 같다.
            var fresh = SimState.CreateRound(config, w.Arena, 1, MatchPhase.Countdown);
            AssertCombatEqual(fresh.Fighter0, s.Fighter0, "P1");
            AssertCombatEqual(fresh.Fighter1, s.Fighter1, "P2");
            Assert.AreEqual(0, s.Projectiles.CountActive(-1), "투사체 0");
            Assert.AreEqual(Hash(fresh.Props), Hash(s.Props), "구조물·상자가 새 맵의 초기 상태");
            Assert.AreEqual(config.Combat.Energy.Max, s.Fighter0.Energy);
            // 경기 부분은 유지.
            Assert.AreEqual(keep0.Form, s.Fighter0.Form);
            Assert.AreEqual(keep1.Form, s.Fighter1.Form);
            Assert.AreEqual(keep1.OwnedForms, s.Fighter1.OwnedForms);
            Assert.AreEqual(picks, s.Match.Picks0);
            Assert.That(s.Match.Score0 + s.Match.Score1 + s.Match.SetScore0 + s.Match.SetScore1, Is.GreaterThanOrEqualTo(score0 + score1 + 1));
        }

        static void AssertCombatEqual(FighterState fresh, FighterState actual, string who)
        {
            // 경기 부분과 직전 버튼(입력 기록용)은 비교에서 뺀다.
            SimState.KeepMatchPart(ref fresh, actual);
            fresh.PrevButtons = actual.PrevButtons;
            Assert.AreEqual(Hash(fresh), Hash(actual), $"{who}: 라운드 시작 전투 상태가 새 라운드와 다름 ({actual.Action}, {actual.Position}, {actual.Energy})");
        }

        static uint Hash(in FighterState f)
        {
            var h = Hasher.Create();
            f.Visit(ref h);
            return h.Hash;
        }

        static uint Hash(in PropState p)
        {
            var h = Hasher.Create();
            p.Visit(ref h);
            return h.Hash;
        }

        // CP22-7 ----------------------------------------------------------------

        [Test]
        public void SetEnd_ResetsRoundScoreOnly_RestartResetsMatchPart()
        {
            var config = Config();
            var w = Match(config, 5);
            WinSet(w, 0);
            int slot = w.State.Match.Rotation.Slot;
            Pick(w, 0);
            var m = w.State.Match;
            Assert.AreEqual(0, m.Score0 + m.Score1);
            Assert.AreEqual(1, m.SetScore0);
            Assert.AreEqual(1, m.Picks1);
            ToFighting(w);
            Assert.AreNotEqual(Forms.Bit(FighterForm.Scissors), w.State.Fighter1.OwnedForms, "얻은 폼 유지");
            Assert.AreNotEqual(slot, w.State.Match.Rotation.Slot, "맵 순환은 이어간다");

            // 경기 끝까지 몰아서 재시작.
            for (int s = 1; s < config.Match.SetsToWinMatch; s++)
            {
                WinSet(w, 0);
                if (w.State.Match.Phase == MatchPhase.SetPick)
                    Pick(w, 0);
            }
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
            var order = w.State.Match.Rotation.Order;
            for (int t = 0; t <= config.Match.MatchEndMinTicks + 2 && w.State.Match.Phase == MatchPhase.MatchEnd; t++)
                w.Step((t & 1) == 0 ? Press(InputButtons.Jump) : default, default);
            var r = w.State;
            Assert.AreEqual(MatchPhase.FormSelect, r.Match.Phase);
            Assert.AreEqual(0, r.Match.Picks0 + r.Match.Picks1 + r.Match.SetScore0 + r.Match.SetScore1 + r.Match.Score0 + r.Match.Score1);
            Assert.AreEqual(config.Rules.OwnedFormsOf(1), r.Fighter1.OwnedForms, "보유 폼 초기화");
            Assert.AreEqual(0, r.Fighter1.Augments.Count, "증강 초기화");
            Assert.AreNotEqual(order, r.Match.Rotation.Order, "맵 순환을 새로 섞음");
            // 초기 폼을 새로 고른다.
            for (int t = 0; t < 200 && r.Match.Phase == MatchPhase.FormSelect; t++)
            {
                w.Step((t & 1) == 0 ? Press(InputButtons.Dash) : default, (t & 1) == 0 ? Press(InputButtons.Attack) : default);
                r = w.State;
            }
            Assert.AreEqual(FighterForm.Paper, r.Fighter0.Form);
            Assert.AreEqual(FighterForm.Rock, r.Fighter1.Form);
        }

        // CP22-9, CP22-10 -----------------------------------------------------

        [TestCase(221u)]
        [TestCase(222u)]
        public void FullFlowWithBots_SameSeedSameOptions_SyncTest_Replay_MapRotates(uint seed)
        {
            const int ticks = 9000;
            var config = Config();
            var w = Match(config, seed, MatchPhase.FormSelect);
            var b1 = new BotInputSource(() => w, 0, 300u + seed);
            var b2 = new BotInputSource(() => w, 1, 700u + seed);
            var in1 = new InputFrame[ticks];
            var in2 = new InputFrame[ticks];
            int picks = 0, lastRound = 0, lastMap = w.State.Match.Rotation.Current, mapChanges = 0, rounds = 0;
            var options = new System.Text.StringBuilder();
            for (int t = 0; t < ticks; t++)
            {
                int tick = w.State.Tick;
                in1[t] = b1.Poll(tick);
                in2[t] = b2.Poll(tick);
                w.Step(in1[t], in2[t]);
                for (int i = 0; i < w.Events.Count; i++)
                {
                    if (w.Events[i].Type == SimEventType.PickStart && w.Events[i].Fighter >= 0)
                        options.Append($"{w.State.Match.Option0},{w.State.Match.Option1},{w.State.Match.Option2},{w.State.Match.Option3};");
                    if (w.Events[i].Type == SimEventType.Picked && w.State.Match.Phase == MatchPhase.SetPick)
                        picks++;
                }
                if (w.State.Match.Round != lastRound)
                {
                    rounds++;
                    if (w.State.Match.Rotation.Current != lastMap)
                        mapChanges++;
                    lastRound = w.State.Match.Round;
                    lastMap = w.State.Match.Rotation.Current;
                }
            }
            Assert.Greater(picks, 0, "세트 패자 선택이 일어남");
            Assert.AreEqual(rounds - 1, mapChanges, "라운드마다 맵이 바뀜");
            uint first = w.ComputeChecksum();

            var again = Match(config, seed, MatchPhase.FormSelect);
            var options2 = new System.Text.StringBuilder();
            for (int t = 0; t < ticks; t++)
            {
                again.Step(in1[t], in2[t]);
                for (int i = 0; i < again.Events.Count; i++)
                {
                    if (again.Events[i].Type == SimEventType.PickStart && again.Events[i].Fighter >= 0)
                        options2.Append($"{again.State.Match.Option0},{again.State.Match.Option1},{again.State.Match.Option2},{again.State.Match.Option3};");
                }
            }
            Assert.AreEqual(first, again.ComputeChecksum());
            Assert.AreEqual(options.ToString(), options2.ToString(), "같은 시드면 같은 선택지");

            var sync = new SyncTest<SimWorld>(Match(config, seed, MatchPhase.FormSelect));
            for (int t = 0; t < ticks; t++)
                sync.Advance(in1[t], in2[t]);
            Assert.AreEqual(0, sync.MismatchCount, "선택 단계를 포함한 SyncTest");

            var start = Match(config, seed, MatchPhase.FormSelect);
            var arenas = new ArenaDef[Maps.Length];
            for (int i = 0; i < Maps.Length; i++)
                arenas[i] = ArenaFiles.LoadByName(Maps[i]);
            var recording = InputRecording.Begin(config, arenas, start.State.Match.Rotation, seed, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.FormSelect, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                recording.Append(in1[t], in2[t]);
            var loaded = InputRecording.FromBytes(recording.ToBytes());
            Assert.AreEqual(first, loaded.Replay(config, arenas), "입력 기록 재생");
        }

        // CP22-11 (측정, 이름으로 실행) ------------------------------------------

        [Test, Explicit("측정: 이름으로 실행"), Category("Measure")]
        public void BotsCompleteFullMatches_Record()
        {
            var report = new System.Text.StringBuilder();
            int violations = 0;
            bool allEnded = true;
            for (uint seed = 1; seed <= 6; seed++)
            {
                var config = Config();
                var w = Match(config, seed, MatchPhase.FormSelect);
                var b1 = new BotInputSource(() => w, 0, 300u + seed * 17u);
                var b2 = new BotInputSource(() => w, 1, 700u + seed * 31u);
                int t = 0;
                bool ended = false;
                for (; t < 60 * 60 * 40 && !ended; t++)
                {
                    int tick = w.State.Tick;
                    w.Step(b1.Poll(tick), b2.Poll(tick));
                    for (int i = 0; i < w.Events.Count; i++)
                    {
                        if (w.Events[i].Type == SimEventType.OutOfBounds)
                            violations++;
                        ended |= w.Events[i].Type == SimEventType.MatchEnd;
                    }
                    var f = w.State.Fighter0;
                    if (float.IsNaN(f.Position.X) || float.IsNaN(w.State.Fighter1.Position.X))
                        violations++;
                }
                var m = w.State.Match;
                report.AppendLine($"시드 {seed}: 끝남 {ended}, {t / 3600f:F1}분, 라운드 {m.Round}, 세트 {m.SetScore0}:{m.SetScore1}, 선택 P1 {m.Picks0} P2 {m.Picks1}, "
                                  + $"보유 폼 P1 {w.State.Fighter0.OwnedForms} P2 {w.State.Fighter1.OwnedForms}, 마지막 단계 {m.Phase}({m.PhaseTick}틱), 라운드 점수 {m.Score0}:{m.Score1}, "
                                  + $"맵 {w.Arena.Name}, P1 {w.State.Fighter0.Form} 체력 {w.State.Fighter0.Health} {w.State.Fighter0.Position} {w.State.Fighter0.Action} 에너지 {w.State.Fighter0.Energy}, "
                                  + $"P2 {w.State.Fighter1.Form} 체력 {w.State.Fighter1.Health} {w.State.Fighter1.Position} {w.State.Fighter1.Action} 에너지 {w.State.Fighter1.Energy}, 투사체 {w.State.Projectiles.CountActive(-1)}");
                allEnded &= ended;
                if (m.Picks0 > config.Match.MaxPicks || m.Picks1 > config.Match.MaxPicks)
                    violations++;
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/m2-full-match.txt", report.ToString());
            TestContext.WriteLine(report.ToString());
            Assert.IsTrue(allEnded, report.ToString());
            Assert.AreEqual(0, violations);
        }
    }
}
