using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    /// <summary>결과 공간(2026-11-16 게임 흐름 CP1): 맵 전환·낙하·KO 재등장·준비 비트 재대결·결정론·기존 설정 해시 보존.</summary>
    public class ResultsSpaceTests
    {
        const string ResultsMapPath = "Assets/_Project/Data/Maps/Results/Results_Stage.asset";
        static ArenaDef MatchArena => ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
        static ArenaDef ResultsArena => AssetDatabase.LoadAssetAtPath<MapDefinition>(ResultsMapPath).ToArena();

        static SimConfig Config(bool online = false, bool results = true)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: false,
                combinedAttackInput: true, throwEnabled: true, revisedBalance: true);
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl);
            var config = CardCollectionRules.Apply(original, resultsSpace: results);
            var match = config.Match;
            match.RematchBothHold = online;
            match.IntroCountdownTicks = 6; match.CountdownTicks = 6; match.KoTicks = 6;
            return new SimConfig(config.Movement, match, config.Combat, config.Rules, config.Augments);
        }

        static SimWorld World(SimConfig config)
        {
            var arenas = new[] { MatchArena, ResultsArena };
            return new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 7u, MatchPhase.Countdown));
        }

        static InputFrame Ready => new InputFrame(0, 0, SimWorld.ReadyButton);

        /// <summary>P1이 마지막 세트의 마지막 라운드를 이겨 경기가 끝나는 지점까지.</summary>
        static void FinishMatch(SimWorld w)
        {
            for (int i = 0; i < 100 && w.State.Match.Phase != MatchPhase.Fighting; i++) w.Step(default, default);
            Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
            w.State.Match.SetScore0 = w.Config.Match.SetsToWinMatch - 1;
            w.State.Match.Score0 = w.Config.Match.RoundsToWinSet - 1;
            w.State.Fighter1.Health = 0;
            for (int i = 0; i < 100 && w.State.Match.Phase != MatchPhase.MatchEnd; i++) w.Step(default, default);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
        }

        static void StepTo(SimWorld w, int phaseTick)
        {
            while (w.State.Match.PhaseTick < phaseTick) w.Step(default, default);
        }

        [Test]
        public void MatchEnd_MovesToResultsArena_BothFallAndLandOnTheirSpots()
        {
            var w = World(Config());
            FinishMatch(w);
            var results = w.ResultsArena;
            Assert.IsTrue(w.InResultsSpace);
            Assert.AreSame(results, w.Arena);
            Assert.IsTrue(w.State.Fighter0.Arriving && w.State.Fighter1.Arriving);
            // 승자(P1)는 맵의 P1 자리(시상대) 위, 패자는 P2 자리 위에서 시작한다.
            Assert.AreEqual(results.SpawnFoot0.X, w.State.Fighter0.Position.X, 1e-4f);
            Assert.AreEqual(results.SpawnFoot1.X, w.State.Fighter1.Position.X, 1e-4f);
            // 2026-11-16 CP5: 승자는 시상대 위에 바로 서고(화면에서 시상대와 함께 솟음), 패자는 자리 위에서 떨어진다.
            Assert.AreEqual(results.SpawnFoot0.Y + w.Config.HalfBody, w.State.Fighter0.Position.Y, 1e-4f);
            Assert.Greater(w.State.Fighter1.Position.Y, results.SpawnFoot1.Y + w.Config.HalfBody + 1f);
            float startY = w.State.Fighter1.Position.Y;
            StepTo(w, w.Config.Match.ResultsDropTick);
            Assert.AreEqual(startY, w.State.Fighter1.Position.Y, "재구성 동안은 자리 위에서 기다린다");
            // 입장 연출이 끝날 때까지(ResultsUnlockTick) 두 선수 모두 조작이 잠긴다.
            var right = new InputFrame(127, 0, InputButtons.None);
            float winnerX = w.State.Fighter0.Position.X;
            while (w.State.Match.PhaseTick < w.Config.Match.ResultsUnlockTick - 1) w.Step(right, right);
            Assert.AreEqual(winnerX, w.State.Fighter0.Position.X, 1e-4f, "잠금 중에는 움직이지 않는다");
            StepTo(w, w.Config.Match.ResultsUnlockTick);
            Assert.IsFalse(w.State.Fighter0.Arriving || w.State.Fighter1.Arriving, "1초 안에 둘 다 착지");
            Assert.IsTrue(w.State.Fighter0.Grounded && w.State.Fighter1.Grounded);
            Assert.AreEqual(results.SpawnFoot0.Y + w.Config.HalfBody, w.State.Fighter0.Position.Y, 0.05f);
            Assert.AreEqual(w.Config.Match.SetsToWinMatch, w.State.Match.SetScore0);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
        }

        [Test]
        public void ArrivingFighter_IgnoresInputUntilLanding()
        {
            var w = World(Config());
            FinishMatch(w);
            StepTo(w, w.Config.Match.ResultsDropTick);
            var right = new InputFrame(127, 0, InputButtons.None);
            float x = w.State.Fighter1.Position.X;
            w.Step(default, right);
            Assert.IsTrue(w.State.Fighter1.Arriving);
            Assert.AreEqual(x, w.State.Fighter1.Position.X, 1e-5f);
        }

        /// <summary>
        /// 보정 해제 보완(2026-11-18): 결과 공간에서 남은 화면 보정이 있을 때 새 보고 없이 KO·재등장(낙하)으로 바뀌면 그 선수의 보정은 바로 사라진다.
        /// </summary>
        [Test]
        public void ResultsKoAndRespawn_ReleaseStaleDisplayCorrectionWithoutNewReport()
        {
            var w = World(Config(online: true));
            FinishMatch(w);
            StepTo(w, w.Config.Match.ResultsUnlockTick);
            Assert.IsFalse(w.State.Fighter0.Arriving || w.State.Fighter1.Arriving, "둘 다 착지함");
            var go = new UnityEngine.GameObject("CorrectionReleaseTest");
            try
            {
                var runner = go.AddComponent<MatchRunner>();
                FusionPredictionTests.StaleCorrection(runner);
                runner.UpdateDisplayCorrection(w.State, 0f);
                Assert.AreNotEqual(UnityEngine.Vector2.zero, runner.DisplayCorrection(1), "서 있는 동안은 남음");
                w.State.Fighter1.Health = 0;
                w.Step(default, default);
                Assert.AreEqual(ActionState.KO, w.State.Fighter1.Action);
                runner.UpdateDisplayCorrection(w.State, 0f);
                Assert.AreEqual(UnityEngine.Vector2.zero, runner.DisplayCorrection(1), "KO 첫 프레임에 바로 지움");
                Assert.AreNotEqual(UnityEngine.Vector2.zero, runner.DisplayCorrection(0), "다른 선수는 그대로 줄임");
                // KO 마지막 틱에 들어온 보정도 재등장(위에서 낙하) 첫 프레임에 지운다.
                for (int i = 0; i < w.Config.Match.ResultsRespawnTicks + 1 && w.State.Fighter1.Action == ActionState.KO; i++)
                {
                    FusionPredictionTests.StaleCorrection(runner, 1);
                    w.Step(default, default);
                }
                Assert.IsTrue(w.State.Fighter1.Arriving, "재등장");
                runner.UpdateDisplayCorrection(w.State, 0f);
                Assert.AreEqual(UnityEngine.Vector2.zero, runner.DisplayCorrection(1), "재등장 첫 프레임에 바로 지움");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ResultsKo_RespawnsFromAboveWithoutScoring()
        {
            var w = World(Config());
            FinishMatch(w);
            StepTo(w, w.Config.Match.ResultsUnlockTick);
            int set0 = w.State.Match.SetScore0, set1 = w.State.Match.SetScore1;
            w.State.Fighter1.Health = 0;
            w.Step(default, default);
            Assert.AreEqual(ActionState.KO, w.State.Fighter1.Action);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
            Assert.AreEqual(set0, w.State.Match.SetScore0);
            Assert.AreEqual(set1, w.State.Match.SetScore1);
            for (int i = 0; i < w.Config.Match.ResultsRespawnTicks + 1 && w.State.Fighter1.Action == ActionState.KO; i++) w.Step(default, default);
            Assert.AreNotEqual(ActionState.KO, w.State.Fighter1.Action);
            Assert.IsTrue(w.State.Fighter1.Arriving);
            Assert.AreEqual(w.Config.Combat.MaxHealth, w.State.Fighter1.Health);
            Assert.AreEqual(w.ResultsArena.SpawnFoot1.X, w.State.Fighter1.Position.X, 1e-4f);
        }

        [Test]
        public void Online_RematchNeedsBothReady_ReadyIsNotAnAttack()
        {
            var w = World(Config(online: true));
            FinishMatch(w);
            StepTo(w, w.Config.Match.ResultsUnlockTick);
            for (int i = 0; i < 30; i++) w.Step(Ready, default);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase, "한 명만 준비하면 기다린다");
            Assert.AreNotEqual(ActionState.Attack, w.State.Fighter0.Action, "준비 비트는 공격이 아니다");
            Assert.AreNotEqual(ActionState.Charge, w.State.Fighter0.Action);
            w.Step(Ready, Ready);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(1, w.State.Match.Round);
            Assert.AreEqual(0, w.State.Match.SetScore0);
            Assert.IsFalse(w.InResultsSpace);
            Assert.AreSame(w.GetArena(w.State.Match.Rotation.Current), w.Arena);
        }

        [Test]
        public void Practice_RematchWithOneReady_NotBeforeUnlock()
        {
            var w = World(Config());
            FinishMatch(w);
            for (int i = 0; i < w.Config.Match.ResultsUnlockTick - 2; i++) w.Step(Ready, default);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
            StepTo(w, w.Config.Match.ResultsUnlockTick);
            w.Step(Ready, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
        }

        [Test]
        public void Rematch_PicksFromMatchMapsOnly_NeverTheResultsMap()
        {
            var config = Config(online: true);
            var arenas = new[] { ArenaFiles.LoadByName("Brawl"), ArenaFiles.LoadByName("Ruins"), ArenaFiles.LoadByName("Towers"), ResultsArena };
            var w = new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 11u, MatchPhase.Countdown));
            Assert.AreEqual(3, w.State.Match.Rotation.Count, "결과 공간 맵은 순환에 들어가지 않는다");
            for (int match = 0; match < 12; match++)
            {
                Assert.Less(w.State.Match.Rotation.Current, 3);
                Assert.AreNotSame(w.ResultsArena, w.Arena);
                FinishMatch(w);
                Assert.AreSame(w.ResultsArena, w.Arena);
                StepTo(w, w.Config.Match.ResultsUnlockTick);
                w.Step(Ready, Ready);
                Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
                Assert.AreSame(w.GetArena(w.State.Match.Rotation.Current), w.Arena);
            }
        }

        [Test]
        public void SameInputs_SameChecksum_ThroughResultsAndRematch()
        {
            uint Run()
            {
                var w = World(Config(online: true));
                FinishMatch(w);
                for (int i = 0; i < 400; i++)
                {
                    var a = new InputFrame((sbyte)(i % 40 < 20 ? 127 : -127), 0, i % 17 == 0 ? InputButtons.Attack : InputButtons.None);
                    var b = new InputFrame((sbyte)(i % 30 < 15 ? -127 : 127), 0, i % 23 == 0 ? InputButtons.Jump : InputButtons.None);
                    if (i > 300) { a.Buttons |= SimWorld.ReadyButton; b.Buttons |= SimWorld.ReadyButton; }
                    w.Step(a, b);
                }
                return w.ComputeChecksum();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void ExistingConfigsAndStates_KeepTheirHashes()
        {
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default));
            Assert.AreEqual(CardCollectionRules.Apply(original).ComputeChecksum(),
                CardCollectionRules.Apply(original, resultsSpace: false).ComputeChecksum());
            Assert.AreNotEqual(CardCollectionRules.Apply(original).ComputeChecksum(),
                CardCollectionRules.Apply(original, resultsSpace: true).ComputeChecksum());
            var state = SimState.CreateRound(original, MatchArena, 3u, MatchPhase.Countdown);
            uint before = state.ComputeChecksum();
            state.Fighter0.Arriving = true;
            Assert.AreNotEqual(before, state.ComputeChecksum());
            state.Fighter0.Arriving = false;
            Assert.AreEqual(before, state.ComputeChecksum());
        }
    }
}
