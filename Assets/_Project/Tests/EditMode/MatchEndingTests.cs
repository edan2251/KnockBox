using System;
using System.Collections.Generic;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SessionState = Knockbox.Game.Online.SessionState;

namespace Knockbox.Tests.EditMode
{
    public class MatchEndingTests
    {
        static SimConfig Config(bool results = false)
        {
            var config = CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default),
                BrawlParams.Match(MatchParams.Default), BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl), resultsSpace: results);
            var match = config.Match; match.RematchBothHold = true;
            return new SimConfig(config.Movement, match, config.Combat, config.Rules, config.Augments);
        }

        static InputFrame Command(MatchCommand command) => new InputFrame(0, 0, InputButtons.None, 0, command);

        [TestCase(0, MatchCommand.Surrender)] [TestCase(1, MatchCommand.Surrender)]
        [TestCase(0, MatchCommand.Leave)] [TestCase(1, MatchCommand.Leave)]
        public void Forfeit_FinalizesOnce_PreservesLoserSets_AndSkipsRoundsAndCards(int loser, MatchCommand command)
        {
            var w = TestWorlds.Create(config: Config());
            w.State.Match.SetScore0 = 3; w.State.Match.SetScore1 = 1;
            var position = w.State.GetFighter(loser).Position;
            var a = loser == 0 ? Command(command) : default;
            var b = loser == 1 ? Command(command) : default;
            w.Step(a, b);
            Assert.AreEqual(position, w.State.GetFighter(loser).Position);
            Assert.AreEqual(loser == 0 ? 3 : 5, w.State.Match.SetScore0);
            Assert.AreEqual(loser == 0 ? 5 : 1, w.State.Match.SetScore1);
            Assert.AreEqual(1 - loser, w.State.Match.RoundWinner);
            int kos = 0, ends = 0;
            for (int e = 0; e < w.Events.Count; e++) if (w.Events[e].Type == SimEventType.KO) kos++;
            for (int i = 0; i < SimWorld.ForfeitKoTicks + 30; i++)
            {
                w.Step(a, b);
                for (int e = 0; e < w.Events.Count; e++)
                {
                    if (w.Events[e].Type == SimEventType.KO) kos++;
                    if (w.Events[e].Type == SimEventType.MatchEnd) ends++;
                    Assert.AreNotEqual(SimEventType.PickStart, w.Events[e].Type);
                    Assert.AreNotEqual(SimEventType.RoundTransition, w.Events[e].Type);
                }
            }
            Assert.AreEqual(1, kos); Assert.AreEqual(1, ends);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
            w.Step(Command(MatchCommand.Leave), Command(MatchCommand.Leave));
            Assert.AreEqual(1 - loser, w.State.Match.RoundWinner, "퇴장으로 확정 승자가 바뀌지 않는다");
        }

        [Test]
        public void Forfeit_UsesConfiguredTarget_AndLobbyDoesNotForfeit()
        {
            var config = Config(); var match = config.Match; match.SetsToWinMatch = 3;
            config = new SimConfig(config.Movement, match, config.Combat, config.Rules, config.Augments);
            var w = TestWorlds.Create(config: config);
            w.Step(default, Command(MatchCommand.Surrender));
            Assert.AreEqual(3, w.State.Match.SetScore0);
            var lobby = w.State.Match; lobby.EndKind = MatchEndKind.Normal; lobby.Phase = MatchPhase.Lobby;
            Assert.IsFalse(SimWorld.CanForfeit(lobby));
            lobby.Phase = MatchPhase.LobbyExit; Assert.IsFalse(SimWorld.CanForfeit(lobby));
        }

        [Test]
        public void ConfirmedForfeit_ReplaysAndDeduplicates_BothPeersHaveIdenticalOutcome()
        {
            var w = TestWorlds.Create(config: Config()); var arenas = new[] { w.Arena };
            var host = new FusionConfirmedJournal(w.Config, arenas, w.State);
            var guest = new FusionConfirmedJournal(w.Config, arenas, w.State);
            int confirmations = 0; guest.Confirmed += () => confirmations++;
            for (int i = 0; i < 120; i++)
            {
                host.Append(i == 4 ? Command(MatchCommand.Surrender) : default, default);
                var packet = host.CreateBatch(i, 1); guest.AcceptBatch(packet); guest.AcceptBatch(packet);
            }
            Assert.AreEqual(120, confirmations);
            Assert.AreEqual(host.World.ComputeChecksum(), guest.World.ComputeChecksum());
            var recording = InputRecording.FromBytes(guest.Finish().ToBytes());
            Assert.AreEqual(InputRecording.MatchCommandVersion, recording.WrittenVersion);
            Assert.AreEqual(recording.FinalChecksum, recording.Replay(w.Config, arenas));
            CollectionAssert.AreEqual(host.Finish().ToBytes(), guest.Finish().ToBytes());
        }

        [Test]
        public void Surrender_ResultsUseSameWinnerAndScore_AndBothReadyRematch()
        {
            var config = Config(true);
            var results = AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/Results/Results_Stage.asset").ToArena();
            var arenas = new[] { TestWorlds.Box, results };
            var w = new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 7, MatchPhase.Fighting));
            w.State.Match.SetScore0 = 3; w.State.Match.SetScore1 = 1;
            w.Step(Command(MatchCommand.Surrender), default);
            for (int i = 0; i < SimWorld.ForfeitKoTicks; i++) w.Step(default, default);
            Assert.IsTrue(w.InResultsSpace); Assert.AreEqual(1, SimWorld.ResultsWinner(w.State.Match));
            Assert.AreEqual(3, w.State.Match.SetScore0); Assert.AreEqual(5, w.State.Match.SetScore1);
            while (w.State.Match.PhaseTick <= w.FreeSpaceUnlockTick) w.Step(default, default);
            var ready = new InputFrame(0, 0, SimWorld.ReadyButton);
            w.Step(ready, default); Assert.IsTrue(w.InResultsSpace);
            w.Step(ready, ready);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.IsFalse(w.State.Match.Forfeited); Assert.AreEqual(0, w.State.Match.SetScore0 + w.State.Match.SetScore1);
        }

        // 씬·NetworkRunner를 실행하지 않는다. 실제 입력 수집/확정 저널/OnlineMatch와 메모리 연결로 종료 순서를 검사한다.
        sealed class EndingLink : ITransport, ITransportLifetime
        {
            readonly ITransport inner;
            public bool Ended { get; set; }
            public EndReason Reason { get; set; }
            public bool Silent;
            public string EndMessage => "검사용 연결 종료";
            public EndingLink(ITransport inner) { this.inner = inner; }
            public void Send(byte[] data, int length) { if (!Silent && !Ended) inner.Send(data, length); }
            public bool TryReceive(out byte[] packet)
            { if (!Silent && !Ended) return inner.TryReceive(out packet); packet = null; return false; }
            public void Dispose() => inner.Dispose();
        }

        sealed class EndingPair : IDisposable
        {
            public readonly MatchRunner[] Runners = new MatchRunner[2];
            public readonly OnlineMatch[] Matches = new OnlineMatch[2];
            public readonly FusionConfirmedJournal[] Journals = new FusionConfirmedJournal[2];
            public readonly FusionInputCollector[] Inputs = new FusionInputCollector[2];
            public readonly LockstepSession[] Sessions = new LockstepSession[2];
            public readonly EndingLink[] Links = new EndingLink[2];
            readonly GameObject[] roots = new GameObject[2];
            readonly List<byte[]> pending = new List<byte[]>();
            public bool ObserveGuest = true;
            public double Now = 1;
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

            public EndingPair()
            {
                MemoryTransport.Pair(out var a, out var b);
                Links[0] = new EndingLink(a); Links[1] = new EndingLink(b);
                var config = Config(); var arenas = new[] { TestWorlds.Box };
                var initial = SimState.CreateMatch(config, arenas, 7, MatchPhase.Fighting);
                for (int p = 0; p < 2; p++)
                {
                    int slot = p;
                    roots[p] = new GameObject("Ending_Code_Test_" + p) { hideFlags = HideFlags.HideAndDontSave };
                    roots[p].SetActive(false);
                    Runners[p] = roots[p].AddComponent<MatchRunner>();
                    Runners[p].Begin(config, arenas, initial);
                    Runners[p].FrontendEnabled = true;
                    Journals[p] = new FusionConfirmedJournal(config, arenas, initial);
                    Runners[p].BeginPrediction(p, Journals[p].World);
                    var input = Runners[p].RouteFrontendInput(new IdleInputSource(), p);
                    Inputs[p] = new FusionInputCollector(input, () => Journals[slot].World, p);
                    Sessions[p] = new LockstepSession(Links[p], p == 0, 1, 2, 7, 0, () => Now);
                    Matches[p] = roots[p].AddComponent<OnlineMatch>();
                    Set(p, "runner", Runners[p]); Set(p, "session", Sessions[p]); Set(p, "started", true);
                    Journals[p].Confirmed += () => { if (slot == 0 || ObserveGuest) Observe(slot); };
                }
                for (int i = 0; i < 3; i++) { Sessions[0].Poll(Now); Sessions[1].Poll(Now); }
                foreach (var session in Sessions)
                { Assert.AreEqual(SessionState.Running, session.State); session.Begin(0, null); }
            }

            void Set(int slot, string field, object value) => typeof(OnlineMatch).GetField(field, Private).SetValue(Matches[slot], value);
            public int OutcomeTick(int slot) => (int)typeof(OnlineMatch).GetField("departureOutcomeTick", Private).GetValue(Matches[slot]);
            public void Observe(int slot) => typeof(OnlineMatch).GetMethod("UpdateDeparture", Private).Invoke(Matches[slot], null);
            public void Acknowledge(int tick, bool observe = true)
            {
                typeof(OnlineMatch).GetMethod("ReceiveDepartureAcknowledgement", Private).Invoke(Matches[0], new object[] { tick });
                if (observe) Observe(0);
            }
            public void Poll(int slot) => typeof(OnlineMatch).GetMethod("Update", Private).Invoke(Matches[slot], null);

            public void Step(bool collect = false, bool deliver = true)
            {
                var state = Journals[0].World.State;
                var p1 = collect ? Inputs[0].Collect(Now).ForState(state) : default;
                var p2 = collect ? Inputs[1].Collect(Now).ForState(state) : default;
                int first = Journals[0].Recording.Count;
                Journals[0].Append(p1, p2);
                pending.Add(Journals[0].CreateBatch(first, 1));
                if (deliver) Deliver();
            }

            public void Deliver()
            {
                foreach (var packet in pending) Journals[1].AcceptBatch(packet);
                pending.Clear();
            }

            public void Disconnect(int survivor, EndReason reason)
            { Links[survivor].Reason = reason; Links[survivor].Ended = true; Poll(survivor); }

            public void Dispose()
            {
                for (int p = 0; p < 2; p++)
                {
                    Set(p, "session", null); Runners[p].EndPrediction();
                    Sessions[p].Dispose(); UnityEngine.Object.DestroyImmediate(roots[p]);
                }
            }
        }

        [TestCase(0)] [TestCase(SimWorld.ForfeitKoTicks + 3)]
        public void HostDeparture_WaitsForPeerReceipt_EvenAfterKoAnimation(int acknowledgementDelay)
        {
            using var pair = new EndingPair();
            Assert.IsTrue(pair.Matches[0].RequestMatchCommand(MatchCommand.Leave));
            pair.Acknowledge(pair.Journals[0].World.State.Tick + 1);
            pair.Step(collect: true, deliver: false);
            int outcome = pair.Journals[0].World.State.Tick;
            Assert.IsFalse(pair.Matches[0].DepartureDelivered, "확정 전에 온 응답은 실제 퇴장의 수신 확인이 아니다");
            Assert.IsTrue(pair.Matches[0].DeparturePending); Assert.IsFalse(pair.Matches[1].PeerDeparture);
            pair.Acknowledge(outcome - 1);
            Assert.IsFalse(pair.Matches[0].DepartureDelivered, "다른 틱의 응답으로는 방을 닫지 않는다");
            for (int i = 0; i < acknowledgementDelay; i++) pair.Step(deliver: false);
            Assert.IsFalse(pair.Matches[0].DepartureDelivered, "연출 종료는 상대 수신 확인이 아니다");
            pair.Deliver();
            Assert.IsTrue(pair.Matches[1].PeerDeparture); StringAssert.Contains("기권승", pair.Matches[1].EndNotice);
            Assert.AreEqual(5, pair.Journals[1].World.State.Match.SetScore1);
            pair.Acknowledge(outcome, observe: false);
            pair.Acknowledge(outcome + 1);
            Assert.IsTrue(pair.Matches[0].DepartureDelivered);
            pair.Disconnect(1, EndReason.Timeout);
            Assert.IsTrue(pair.Matches[1].PeerDeparture, "방장이 연결을 닫아도 이미 받은 기권승은 유지된다");
            StringAssert.Contains("0 : 5", pair.Matches[1].EndNotice);
        }

        [Test]
        public void DepartureReceipt_FirstObservedInResults_StillUsesTheForfeitTick()
        {
            using var pair = new EndingPair(); pair.ObserveGuest = false;
            Assert.IsTrue(pair.Matches[0].RequestMatchCommand(MatchCommand.Leave));
            pair.Step(collect: true);
            int outcome = pair.Journals[0].World.State.Tick;
            for (int i = 0; i < SimWorld.ForfeitKoTicks + 3; i++) pair.Step();
            Assert.AreEqual(MatchPhase.MatchEnd, pair.Journals[1].World.State.Match.Phase);
            pair.Observe(1);
            Assert.AreEqual(outcome, pair.OutcomeTick(1), "결과 공간에 늦게 관측해도 같은 퇴장 입력을 확인한다");
        }

        [TestCase(0)] [TestCase(1)]
        public void PendingExit_WhenNaturalFinalKoWinsTheRace_WaitsForFinalScore(int leaving)
        {
            using var pair = new EndingPair();
            for (int p = 0; p < 2; p++)
            {
                var w = pair.Journals[p].World;
                w.State.Match.SetScore0 = w.Config.Match.SetsToWinMatch - 1;
                w.State.Match.Score0 = w.Config.Match.RoundsToWinSet - 1;
                w.State.Fighter1.Health = 0;
            }
            Assert.IsTrue(pair.Matches[leaving].RequestMatchCommand(MatchCommand.Leave));
            pair.Step(); // Leave가 도착하기 전에 마지막 정상 KO가 확정됨.
            Assert.AreEqual(MatchPhase.KO, pair.Journals[0].World.State.Match.Phase);
            Assert.IsFalse(pair.Matches[leaving].DepartureDelivered, "마지막 세트가 아직 반영되지 않은 KO에서 먼저 끊지 않는다");
            for (int i = 0; i < pair.Journals[0].World.Config.Match.KoTicks + 2; i++) pair.Step(collect: true, deliver: false);
            Assert.AreEqual(MatchPhase.MatchEnd, pair.Journals[0].World.State.Match.Phase);
            Assert.AreEqual(5, pair.Journals[0].World.State.Match.SetScore0);
            Assert.IsFalse(pair.Journals[0].World.State.Match.Forfeited);
            Assert.IsFalse(pair.Matches[leaving].DepartureDelivered, "최종 기록이 상대에 도착하기 전에는 나가지 않는다");
            pair.Deliver();
            if (leaving == 0) pair.Acknowledge(pair.OutcomeTick(1));
            Assert.IsTrue(pair.Matches[leaving].DepartureDelivered);
            int survivor = 1-leaving;
            var final = pair.Journals[survivor].World.State.Match;
            pair.Disconnect(survivor, EndReason.PeerLeft);
            Assert.AreEqual(final, pair.Journals[survivor].World.State.Match);
            Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            StringAssert.DoesNotContain("기권승", pair.Matches[survivor].EndNotice);
        }

        [TestCase(0, EndReason.PeerLeft, false)] [TestCase(1, EndReason.PeerLeft, false)]
        [TestCase(0, EndReason.Timeout, false)] [TestCase(1, EndReason.Timeout, false)]
        [TestCase(0, EndReason.PeerLeft, true)] [TestCase(1, EndReason.Timeout, true)]
        public void UnconfirmedDisconnect_DoesNotAwardAForfeit(int survivor, EndReason reason, bool queuedIntent)
        {
            using var pair = new EndingPair();
            if (queuedIntent) Assert.IsTrue(pair.Matches[1-survivor].RequestMatchCommand(MatchCommand.Leave));
            uint before = pair.Journals[survivor].World.ComputeChecksum();
            pair.Disconnect(survivor, reason);
            Assert.IsTrue(pair.Matches[survivor].Ended); Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            Assert.IsFalse(pair.Journals[survivor].World.State.Match.Forfeited);
            Assert.AreEqual(before, pair.Journals[survivor].World.ComputeChecksum());
            Assert.AreEqual("연결이 끊겨 경기가 종료됐습니다", pair.Matches[survivor].EndNotice);
        }

        [TestCase(0)] [TestCase(1)]
        public void TemporarySilence_DoesNotEndOrAwardTheMatch(int slot)
        {
            using var pair = new EndingPair();
            pair.Links[slot].Silent = true; pair.Now += LockstepSession.TimeoutSeconds - .1;
            pair.Poll(slot);
            Assert.AreEqual(SessionState.Running, pair.Sessions[slot].State);
            Assert.IsFalse(pair.Matches[slot].PeerDeparture); Assert.IsFalse(pair.Journals[slot].World.State.Match.Forfeited);
            pair.Links[slot].Silent = false;
            pair.Sessions[1-slot].TryGetInputs(0, out _, out _); pair.Poll(slot);
            Assert.AreEqual(SessionState.Running, pair.Sessions[slot].State);
        }

        [TestCase(0)] [TestCase(1)]
        public void SilencePastExistingTimeout_EndsWithoutAForfeit(int survivor)
        {
            using var pair = new EndingPair();
            uint before = pair.Journals[survivor].World.ComputeChecksum();
            pair.Links[survivor].Silent = true; pair.Now += LockstepSession.TimeoutSeconds + .1;
            pair.Poll(survivor);
            Assert.IsTrue(pair.Matches[survivor].Ended);
            Assert.AreEqual(EndReason.Timeout, pair.Sessions[survivor].Reason);
            Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            Assert.AreEqual(before, pair.Journals[survivor].World.ComputeChecksum());
            Assert.AreEqual("연결이 끊겨 경기가 종료됐습니다", pair.Matches[survivor].EndNotice);
        }

        [TestCase(0)] [TestCase(1)]
        public void SessionByeWithoutConfirmedLeave_IsNotAForfeit(int survivor)
        {
            using var pair = new EndingPair();
            uint before = pair.Journals[survivor].World.ComputeChecksum();
            // OnApplicationQuit/EndSession도 이 제어 메시지만 보낸다. 경기 중 메뉴의 확정 Leave와 다르다.
            pair.Sessions[1-survivor].Leave(); pair.Poll(survivor);
            Assert.AreEqual(EndReason.PeerLeft, pair.Sessions[survivor].Reason);
            Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            Assert.AreEqual(before, pair.Journals[survivor].World.ComputeChecksum());
            StringAssert.DoesNotContain("기권승", pair.Matches[survivor].EndNotice);
        }

        [TestCase(0, MatchPhase.Lobby)] [TestCase(1, MatchPhase.Lobby)]
        [TestCase(0, MatchPhase.LobbyExit)] [TestCase(1, MatchPhase.LobbyExit)]
        public void LobbyExit_RejectsMatchCommands_AndDoesNotAwardAForfeit(int leaving, MatchPhase phase)
        {
            using var pair = new EndingPair();
            foreach (var journal in pair.Journals) journal.World.State.Match.SetPhase(phase);
            Assert.IsFalse(pair.Matches[leaving].RequestMatchCommand(MatchCommand.Surrender));
            Assert.IsFalse(pair.Matches[leaving].RequestMatchCommand(MatchCommand.Leave));
            int survivor = 1-leaving;
            uint before = pair.Journals[survivor].World.ComputeChecksum();
            pair.Sessions[leaving].Leave(); pair.Poll(survivor);
            Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            Assert.AreEqual(before, pair.Journals[survivor].World.ComputeChecksum());
        }

        [TestCase(0)] [TestCase(1)]
        public void ConfirmedDeparture_SurvivesLaterDisconnect_ForEitherRole(int leaving)
        {
            using var pair = new EndingPair();
            Assert.IsTrue(pair.Matches[leaving].RequestMatchCommand(MatchCommand.Leave));
            pair.Step(collect: true);
            if (leaving == 0) pair.Acknowledge(pair.Journals[0].World.State.Tick);
            Assert.IsTrue(pair.Matches[leaving].DepartureDelivered);
            int survivor = 1-leaving;
            uint before = pair.Journals[survivor].World.ComputeChecksum();
            pair.Disconnect(survivor, leaving == 0 ? EndReason.Timeout : EndReason.PeerLeft);
            Assert.AreEqual(before, pair.Journals[survivor].World.ComputeChecksum());
            Assert.IsTrue(pair.Matches[survivor].PeerDeparture); StringAssert.Contains("기권승", pair.Matches[survivor].EndNotice);
        }

        [TestCase(0)] [TestCase(1)]
        public void SurrenderResult_LaterExitDoesNotChangeWinnerOrBecomeDeparture(int survivor)
        {
            using var pair = new EndingPair();
            Assert.IsTrue(pair.Matches[0].RequestMatchCommand(MatchCommand.Surrender)); pair.Step(collect: true);
            for (int i = 0; i < SimWorld.ForfeitKoTicks; i++) pair.Step();
            var match = pair.Journals[survivor].World.State.Match;
            Assert.IsFalse(pair.Matches[survivor].RequestMatchCommand(MatchCommand.Leave));
            pair.Disconnect(survivor, EndReason.PeerLeft);
            Assert.AreEqual(match, pair.Journals[survivor].World.State.Match);
            Assert.IsFalse(pair.Matches[survivor].PeerDeparture);
            StringAssert.DoesNotContain("기권승", pair.Matches[survivor].EndNotice);
        }

        sealed class MutableInput : IInputSource { public InputFrame Frame; public InputFrame Poll(int tick) => Frame; }

        [Test]
        public void MenuBlocksOnlyItsInput_AndClosingClickMustBeReleased()
        {
            var w = TestWorlds.Create(config: Config());
            bool blocked = false;
            var raw = new MutableInput { Frame = new InputFrame(1, 0, InputButtons.Attack | InputButtons.Charge) };
            var source = new FrontendInputSource(() => w, () => blocked, raw, 0);
            source.Poll(0); blocked = true;
            Assert.AreEqual(default(InputFrame), source.Poll(1));
            blocked = false; source.Poll(2);
            Assert.AreEqual(InputButtons.None, source.Poll(3).Buttons);
            raw.Frame = default; source.Poll(4);
            raw.Frame = new InputFrame(0, 0, InputButtons.Attack);
            Assert.AreEqual(InputButtons.Attack, source.Poll(5).Buttons);
        }

        [TestCase(-1500f, -1000f)] [TestCase(1500f, 1000f)]
        public void EscapeMenu_StaysInsideViewport(float x, float y)
        {
            var at = FrontendView.EscapePosition(new Vector2(x, y), new Vector2(960, 540), x > 0);
            Assert.That(at.x, Is.InRange(-936, 586)); Assert.That(at.y, Is.InRange(-306, 472));
        }
    }
}
