using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 라운드·세트·경기와 세트 패자 선택(D-049~D-054)을 현재 Body 카드 흐름 설정으로 검사한다.
    /// 승패는 KO를 직접 만들어(체력 0) 빠르게 진행한다. 승수·세트 수는 설정값에서 읽는다.
    /// 2026-11-11: 폼 전투 제거로 폼 획득·폼 유지 검사는 카드(증강) 획득·유지 검사로 바꿨다.
    /// </summary>
    public class MatchStructureTests
    {
        static SimConfig Config()
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: true,
                combinedAttackInput: true, throwEnabled: true, revisedBalance: true);
            return CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl));
        }

        static readonly string[] Maps = { "Chimes", "Gantry", "Mobile", "Ruins", "Scales", "Totems" };

        static SimWorld Match(SimConfig config, uint seed = 3)
        {
            var arenas = new ArenaDef[Maps.Length];
            for (int i = 0; i < Maps.Length; i++)
                arenas[i] = ArenaFiles.LoadByName(Maps[i]);
            return new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, seed, MatchPhase.Countdown));
        }

        static InputFrame Press(InputButtons b) => new InputFrame(0, 0, b);

        /// <summary>선택 단계면 option번을 고르게 하며 전투 단계까지 진행.</summary>
        static void ToFighting(SimWorld w, int option = 0)
        {
            for (int t = 0; t < 6000 && w.State.Match.Phase != MatchPhase.Fighting; t++)
            {
                var b = (t & 1) == 0 ? MatchFlow.OptionButton(option) : InputButtons.None;
                bool select = w.State.Match.Phase == MatchPhase.SetPick;
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
            for (int t = 0; t < 400; t++)
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
            for (int t = 0; t < 1000 && w.State.Match.Phase == MatchPhase.SetPick; t++)
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

        static void Rematch(SimWorld w)
        {
            for (int t = 0; t <= w.Config.Match.MatchEndMinTicks + 2 && w.State.Match.Phase == MatchPhase.MatchEnd; t++)
                w.Step((t & 1) == 0 ? Press(InputButtons.Jump) : default, default);
        }

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
            Rematch(y);
            Assert.AreEqual(MatchPhase.Countdown, y.State.Match.Phase, "재시작은 카운트다운부터(초기 폼 선택 없음)");
            Assert.AreEqual(0, y.State.Match.SetScore0 + y.State.Match.SetScore1);
        }

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
            WinSet(w, 0);
            Pick(w, 1);
            int cards = w.State.Fighter1.Augments.Count;
            Assert.AreEqual(1, cards, "세트 패자가 카드 하나를 얻음");
            int set = w.State.Match.Set;
            FinishRound(w, -1);
            Assert.AreEqual(0, w.State.Match.Score0 + w.State.Match.Score1, "무승부는 점수 없음");
            Assert.AreEqual(set, w.State.Match.Set);
            ToFighting(w);
            Assert.AreEqual(cards, w.State.Fighter1.Augments.Count, "무승부 뒤에도 카드 유지");
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
        }

        [Test]
        public void RoundStart_ResetsCombatState_KeepsMatchPart_NoLeak()
        {
            var config = Config();
            var w = Match(config, 7);
            WinSet(w, 0);
            Pick(w, 0);
            var b1 = new BrawlBot(() => w, 0, 11u);
            var b2 = new BrawlBot(() => w, 1, 13u);
            // 봇이 싸우다가 둘 다 무언가를 하는 중에(충전, 돌진, 공격 등) KO를 만든다.
            ToFighting(w);
            bool busy = false;
            for (int t = 0; t < 1200; t++)
            {
                int tick = w.State.Tick;
                w.Step(b1.Poll(tick), b2.Poll(tick));
                if (w.State.Match.Phase != MatchPhase.Fighting)
                    ToFighting(w);
                if (t > 120 && w.State.Fighter0.Action != ActionState.Idle && w.State.Fighter1.Action != ActionState.Idle)
                {
                    busy = true;
                    break;
                }
            }
            Assert.IsTrue(busy, "둘 다 행동 중인 장면");
            int score0 = w.State.Match.Score0, score1 = w.State.Match.Score1;
            w.State.Fighter1.Health = 0;
            var keep0 = w.State.Fighter0;
            var keep1 = w.State.Fighter1;
            int picks = w.State.Match.Picks1;
            while (w.State.Match.Phase != MatchPhase.Countdown)
            {
                w.Step(default, default);
                if (w.State.Match.Phase == MatchPhase.SetPick)
                    Pick(w, 0);
            }
            var s = w.State;
            // 같은 맵으로 새로 시작한 라운드와 전투 부분이 같다.
            var fresh = SimState.CreateRound(config, w.Arena, 1, MatchPhase.Countdown);
            AssertCombatEqual(fresh.Fighter0, s.Fighter0, "P1");
            AssertCombatEqual(fresh.Fighter1, s.Fighter1, "P2");
            Assert.AreEqual(Hash(fresh.Props), Hash(s.Props), "구조물·상자가 새 맵의 초기 상태");
            // 경기 부분(카드)은 유지.
            Assert.AreEqual(keep0.Augments.Count, s.Fighter0.Augments.Count);
            Assert.GreaterOrEqual(s.Fighter1.Augments.Count, keep1.Augments.Count);
            Assert.GreaterOrEqual(s.Match.Picks1, picks);
            Assert.That(s.Match.Score0 + s.Match.Score1 + s.Match.SetScore0 + s.Match.SetScore1, Is.GreaterThanOrEqualTo(score0 + score1 + 1));
        }

        static void AssertCombatEqual(FighterState fresh, FighterState actual, string who)
        {
            // 경기 부분과 직전 버튼(입력 기록용)은 비교에서 뺀다.
            SimState.KeepMatchPart(ref fresh, actual);
            fresh.PrevButtons = actual.PrevButtons;
            Assert.AreEqual(Hash(fresh), Hash(actual), $"{who}: 라운드 시작 전투 상태가 새 라운드와 다름 ({actual.Action}, {actual.Position})");
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
            Assert.AreEqual(1, w.State.Fighter1.Augments.Count, "얻은 카드 유지");
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
            Rematch(w);
            var r = w.State;
            Assert.AreEqual(MatchPhase.Countdown, r.Match.Phase);
            Assert.AreEqual(0, r.Match.Picks0 + r.Match.Picks1 + r.Match.SetScore0 + r.Match.SetScore1 + r.Match.Score0 + r.Match.Score1);
            Assert.AreEqual(0, r.Fighter1.Augments.Count, "카드 초기화");
            Assert.AreNotEqual(order, r.Match.Rotation.Order, "맵 순환을 새로 섞음");
        }
    }
}
