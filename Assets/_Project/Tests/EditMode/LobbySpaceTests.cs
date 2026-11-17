using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    /// <summary>온라인 로비(2026-11-16 게임 흐름 CP3): 로비에서 시작·낙하·자유 조작·둘 다 준비해야 1라운드·확정 입력/기록 재생·기존 해시 보존.</summary>
    public class LobbySpaceTests
    {
        static ArenaDef Load(string path) => AssetDatabase.LoadAssetAtPath<MapDefinition>(path).ToArena();
        static ArenaDef ResultsArena => Load("Assets/_Project/Data/Maps/Results/Results_Stage.asset");
        static ArenaDef LobbyArena => Load("Assets/_Project/Data/Maps/Lobby/Lobby_Stage.asset");

        static SimConfig Config(bool results = true)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: false, combinedAttackInput: true);
            combat.MaxHealth = 1;
            var original = CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default),
                BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl), resultsSpace: results, lobbySpace: true);
            var match = original.Match;
            match.IntroCountdownTicks = 6; match.CountdownTicks = 6; match.KoTicks = 6; match.PickHoldTicks = 4;
            match.RematchBothHold = true;
            return new SimConfig(original.Movement, match, combat, original.Rules, original.Augments);
        }

        static ArenaDef[] Maps(bool results = true) => results
            ? new[] { ArenaFiles.LoadByName("Brawl"), ArenaFiles.LoadByName("Ruins"), ResultsArena, LobbyArena }
            : new[] { ArenaFiles.LoadByName("Brawl"), ArenaFiles.LoadByName("Ruins"), LobbyArena };

        static InputFrame Ready => new InputFrame(0, 0, SimWorld.ReadyButton);

        static void StepTo(SimWorld w, int phaseTick)
        {
            while (w.State.Match.PhaseTick < phaseTick) w.Step(default, default);
        }

        [Test]
        public void OnlineStart_InLobby_BothFallOnTheirSlotSpots()
        {
            var config = Config(); var maps = Maps();
            var w = new SimWorld(config, maps, SimState.CreateMatch(config, maps, 5u, MatchPhase.Lobby));
            Assert.AreEqual(MatchPhase.Lobby, w.State.Match.Phase);
            Assert.AreEqual(2, w.State.Match.Rotation.Count, "로비·결과 맵은 순환에 들어가지 않는다");
            Assert.IsTrue(w.InLobby && w.InFreeSpace && !w.InResultsSpace);
            Assert.AreSame(w.LobbyArena, w.Arena);
            Assert.IsTrue(w.State.Fighter0.Arriving && w.State.Fighter1.Arriving);
            Assert.AreEqual(w.LobbyArena.SpawnFoot0.X, w.State.Fighter0.Position.X, 1e-4f);
            Assert.AreEqual(w.LobbyArena.SpawnFoot1.X, w.State.Fighter1.Position.X, 1e-4f);
            StepTo(w, config.Match.ResultsUnlockTick);
            Assert.IsFalse(w.State.Fighter0.Arriving || w.State.Fighter1.Arriving);
            // KO되어도 점수 없이 재등장한다.
            w.State.Fighter0.Health = 0; w.Step(default, default);
            Assert.AreEqual(ActionState.KO, w.State.Fighter0.Action);
            Assert.AreEqual(MatchPhase.Lobby, w.State.Match.Phase);
            Assert.AreEqual(0, w.State.Match.Score1);
        }

        [Test]
        public void Lobby_NeedsBothReady_ThenRoundOneOnTheSeedRotation()
        {
            var config = Config(); var maps = Maps();
            var w = new SimWorld(config, maps, SimState.CreateMatch(config, maps, 5u, MatchPhase.Lobby));
            var plain = SimState.CreateMatch(config, maps, 5u, MatchPhase.Countdown);
            uint rngBefore = w.State.Rng.State;
            StepTo(w, config.Match.ResultsUnlockTick);
            for (int i = 0; i < 30; i++) w.Step(Ready, default);
            Assert.AreEqual(MatchPhase.Lobby, w.State.Match.Phase, "한 명만 준비하면 기다린다");
            w.Step(Ready, Ready);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(1, w.State.Match.Round);
            Assert.AreEqual(plain.Match.Rotation.Current, w.State.Match.Rotation.Current, "첫 경기 맵은 시드 순환 그대로");
            Assert.AreEqual(rngBefore, w.State.Rng.State, "로비는 난수를 쓰지 않는다");
            Assert.AreSame(w.GetArena(w.State.Match.Rotation.Current), w.Arena);
            Assert.IsFalse(w.State.Fighter0.Arriving || w.State.Fighter1.Arriving);
            Assert.AreEqual(config.Combat.MaxHealth, w.State.Fighter0.Health);
        }

        [Test]
        public void LobbyWithoutResultsMap_UsesTheLastArena()
        {
            var config = Config(results: false); var maps = Maps(results: false);
            var w = new SimWorld(config, maps, SimState.CreateMatch(config, maps, 9u, MatchPhase.Lobby));
            Assert.IsNull(w.ResultsArena);
            Assert.AreSame(maps[2], w.LobbyArena);
            Assert.AreEqual(2, w.State.Match.Rotation.Count);
        }

        [Test]
        public void ConfirmedLobbyToMatch_SameOnBothPeers_AndRecordingReplaysFromLobby()
        {
            var config = Config(); var maps = Maps();
            var initial = SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            var a = new FusionInputCollector(new FrontendInputSource(() => host.World, () => false,
                new BrawlChaserBot(() => host.World, 0, 10), 0, true), () => host.World, 0);
            var b = new FusionInputCollector(new FrontendInputSource(() => client.World, () => false,
                new IdleInputSource(), 1, true), () => client.World, 1);
            int sent = 0; bool fighting = false;
            for (int t = 0; t < 3000 && !fighting; t++)
            {
                var ia = a.Collect(t / 60.0); var ib = b.Collect(t / 60.0);
                host.Append(ia.ForState(host.World.State), ib.ForState(host.World.State));
                fighting = host.World.State.Match.Phase == MatchPhase.Fighting;
                if (host.Recording.Count - sent == 4 || fighting)
                {
                    client.AcceptBatch(host.CreateBatch(sent, host.Recording.Count - sent));
                    sent = host.Recording.Count;
                    Assert.AreEqual(host.World.ComputeChecksum(), client.World.ComputeChecksum());
                }
            }
            Assert.IsTrue(fighting, "둘 다 준비해 경기로 들어감");
            var replay = InputRecording.FromBytes(client.Finish().ToBytes());
            Assert.AreEqual(MatchPhase.Lobby, replay.StartPhase);
            CollectionAssert.AreEqual(host.Finish().ToBytes(), client.Finish().ToBytes());
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(config, maps));
        }

        /// <summary>혼자 로비에서 움직이던 P1 하나를 만든다(낙하 끝난 뒤 오른쪽으로 걷는 중).</summary>
        static LobbyCarry SoloCarry(SimConfig config, ArenaDef[] maps)
        {
            var solo = new SimWorld(config, maps, SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby));
            StepTo(solo, config.Match.ResultsUnlockTick);
            for (int i = 0; i < 20; i++) solo.Step(new InputFrame(127, 0, InputButtons.None), default);
            var carry = LobbyCarry.Capture(config, solo.LobbyArena, solo.State.Fighter0);
            Assert.IsTrue(carry.Has, "착지해 걷는 P1은 이어 갈 수 있다");
            Assert.AreEqual(solo.State.Fighter0.Position.X, carry.Position.X);
            return carry;
        }

        [Test]
        public void LobbyJoin_ContinuesHostP1_OnlyP2Drops_AndBothPeersAgree()
        {
            var config = Config(); var maps = Maps();
            var carry = SoloCarry(config, maps);
            var initial = SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            var start = host.ApplyLobbyCarry(carry);
            Assert.IsFalse(start.Fighter0.Arriving, "P1은 혼자 로비 자리에서 이어 간다");
            Assert.IsTrue(start.Fighter1.Arriving, "P2만 떨어진다");
            Assert.AreEqual(carry.Position.X, start.Fighter0.Position.X);
            Assert.AreEqual(carry.Velocity.X, start.Fighter0.Velocity.X);
            Assert.AreEqual(initial.Fighter1.Position.X, start.Fighter1.Position.X);
            int sent = 0;
            for (int t = 0; t < 240; t++)
            {
                host.Append(new InputFrame(t < 60 ? -127 : 0, 0, InputButtons.None), default);
                if (host.Recording.Count - sent < 4) continue;
                client.AcceptBatch(host.CreateBatch(sent, 4));
                sent = host.Recording.Count;
                Assert.AreEqual(host.World.ComputeChecksum(), client.World.ComputeChecksum(), "tick " + t);
            }
            Assert.AreEqual(carry, client.Carry, "첫 확정 묶음으로 이어 간 값이 손님에게 간다");
            var bytes = client.Finish().ToBytes();
            CollectionAssert.AreEqual(host.Finish().ToBytes(), bytes);
            var replay = InputRecording.FromBytes(bytes);
            Assert.AreEqual(InputRecording.LobbyCarryVersion, replay.WrittenVersion);
            Assert.AreEqual(carry, replay.Carry);
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(config, maps), "v10 기록은 이어 간 시작 상태로 재생된다");
        }

        /// <summary>
        /// 공유 로비 첫 틱부터 이어 간 P1은 이동 입력대로 움직이고(지형 재구성 대기 ResultsDropTick에 묶이지 않음),
        /// P2는 그 대기 동안 자리 위에 그대로 있다가 떨어진다. 이어 가지 않은 기존 로비는 예전처럼 둘 다 기다린다.
        /// </summary>
        [Test]
        public void LobbyJoin_ContinuedP1_MovesFromFirstSharedTick_P2StillWaitsToDrop()
        {
            var config = Config(); var maps = Maps();
            var carry = SoloCarry(config, maps);
            var start = SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby);
            var plain = new SimWorld(config, maps, start);
            carry.Apply(config, maps, ref start);
            var w = new SimWorld(config, maps, start);
            var hold = new InputFrame(-127, 0, InputButtons.None);
            float x0 = w.State.Fighter0.Position.X, p2Spot = w.State.Fighter1.Position.Y, plainP1 = plain.State.Fighter0.Position.Y;
            w.Step(hold, default); plain.Step(hold, default);
            Assert.AreEqual(plainP1, plain.State.Fighter0.Position.Y, "이어 가지 않은 로비는 기존처럼 둘 다 대기");
            Assert.AreNotEqual(x0, w.State.Fighter0.Position.X, "첫 공유 틱부터 이동");
            int moved = 1;
            while (true)
            {
                w.Step(hold, default); plain.Step(hold, default); moved++;
                if (w.State.Match.PhaseTick > config.Match.ResultsDropTick) break; // 대기가 끝난 틱부터 P2 낙하
                Assert.AreEqual(p2Spot, w.State.Fighter1.Position.Y, "P2는 낙하 대기 자리 그대로");
                Assert.IsTrue(w.State.Fighter1.Arriving);
            }
            Assert.Less(w.State.Fighter0.Position.X, x0 - 1f, $"{moved}틱 동안 왼쪽으로 걸어감");

            for (int i = 0; i < 120; i++) w.Step(default, default);
            Assert.AreNotEqual(p2Spot, w.State.Fighter1.Position.Y, "대기 뒤 P2가 떨어짐");
        }

        [Test]
        public void LobbyJoin_WithoutCarry_KeepsOldStartAndRecordingVersion()
        {
            var config = Config(); var maps = Maps();
            var initial = SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            Assert.AreEqual(initial.ComputeChecksum(), host.ApplyLobbyCarry(default).ComputeChecksum());
            for (int i = 0; i < 4; i++) host.Append(default, default);
            client.AcceptBatch(host.CreateBatch(0, 4));
            Assert.AreEqual(host.World.ComputeChecksum(), client.World.ComputeChecksum());
            var replay = InputRecording.FromBytes(client.Finish().ToBytes());
            Assert.AreNotEqual(InputRecording.LobbyCarryVersion, replay.WrittenVersion, "이어 가지 않으면 기존 형식(v8/v9) 그대로");
            Assert.IsFalse(replay.Carry.Has);
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(config, maps));
        }

        [Test]
        public void LobbyCarry_RejectsFallingKoOrOutsideValues()
        {
            var config = Config(); var maps = Maps();
            var lobby = maps[maps.Length - 1];
            var w = new SimWorld(config, maps, SimState.CreateMatch(config, maps, 3u, MatchPhase.Lobby));
            Assert.IsFalse(LobbyCarry.Capture(config, lobby, w.State.Fighter0).Has, "낙하 중이면 다시 떨어진다");
            var ko = w.State.Fighter0; ko.Arriving = false; ko.RestartAction(ActionState.KO);
            Assert.IsFalse(LobbyCarry.Capture(config, lobby, ko).Has, "KO 중이면 다시 떨어진다");
            var outside = new LobbyCarry { Has = true, Position = new Vec2(0, -10000f), Facing = 1 };
            Assert.IsFalse(outside.IsValid(config, lobby));
            var nan = new LobbyCarry { Has = true, Position = new Vec2(float.NaN, 0), Facing = 1 };
            Assert.IsFalse(nan.IsValid(config, lobby));

            // 네트워크에서 받은 잘못된 이어 가기 값은 확정 기록·화면에 들어가기 전에 거절한다.
            var initial = SimState.CreateMatch(config, maps, 21u, MatchPhase.Lobby);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            host.ApplyLobbyCarry(SoloCarry(config, maps));
            for (int i = 0; i < 4; i++) host.Append(default, default);
            var batch = host.CreateBatch(0, 4);
            outside.Write(batch, 6 + 4 * 8);
            Assert.Throws<System.InvalidOperationException>(() => client.AcceptBatch(batch));
            Assert.AreEqual(0, client.Recording.Count);
        }

        [Test]
        public void LobbyConfig_IsHashedOnlyWhenOn()
        {
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default));
            uint results = CardCollectionRules.Apply(original, resultsSpace: true).ComputeChecksum();
            Assert.AreEqual(results, CardCollectionRules.Apply(original, resultsSpace: true, lobbySpace: false).ComputeChecksum());
            Assert.AreNotEqual(results, CardCollectionRules.Apply(original, resultsSpace: true, lobbySpace: true).ComputeChecksum());
            Assert.AreNotEqual(CardCollectionRules.Apply(original).ComputeChecksum(),
                CardCollectionRules.Apply(original, lobbySpace: true).ComputeChecksum());
        }
    }
}
