using System;
using System.Reflection;
using Fusion;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public sealed class FusionPredictionTests
    {
        [Test]
        public void NativeMetadata_RegistersWovenInputAndSnapshotForRunnerStartup()
        {
            Assert.IsNotNull(typeof(FusionPlayerInput).GetCustomAttribute<NetworkInputWeavedAttribute>());
            Assert.IsNotNull(typeof(FusionWorldSnapshot).GetCustomAttribute<NetworkStructWeavedAttribute>());
            Assert.IsNotNull(typeof(FusionPredictionWorld).GetCustomAttribute<NetworkBehaviourWeavedAttribute>());
        }

        [Test]
        public void NativePolicy_UsesOne60HzClockAndOnlyThreeSdkDelayTicks()
        {
            var source = NetworkProjectConfig.Global;
            string before = JsonUtility.ToJson(source);
            var config = FusionPredictionPolicy.CreateConfig(source);
            Assert.AreNotSame(source, config);
            Assert.AreNotSame(source.Simulation, config.Simulation);
            Assert.AreNotSame(source.TimeSync, config.TimeSync);
            Assert.AreSame(source.PrefabTable, config.PrefabTable, "Registered prefab sources must remain available.");
            Assert.AreEqual(source.DoNotSetForecastDisabledProxyRigidbodiesToKinematic, config.DoNotSetForecastDisabledProxyRigidbodiesToKinematic);
            var rates = TickRate.Resolve(config.Simulation.TickRateSelection);
            Assert.AreEqual(60, rates.Client); Assert.AreEqual(60, rates.Server);
            Assert.AreEqual(60, rates.ClientSend); Assert.AreEqual(60, rates.ServerSend);
            Assert.AreEqual(TimeSyncConfiguration.Selection.Custom, config.TimeSync.InputDelaySettings);
            Assert.AreEqual(3, config.TimeSync.MinimumInputDelay);
            Assert.AreEqual(3, FusionPredictionPolicy.DelayTicks);
            Assert.AreEqual(TimeSyncConfiguration.Unit.Ticks, config.TimeSync.MinimumInputDelayUnit);
            Assert.IsFalse(config.TimeSync.ClientsCanAddInputDelayToLimitResims);
            Assert.IsFalse(config.ClientsOnlyRollbackUpdatedObjects, "The coupled combat world must roll back together.");
            Assert.AreEqual(before, JsonUtility.ToJson(source), "Never modify the SDK project asset.");
        }

        [Test]
        public void FullSnapshot_RoundTripsEveryLeafIncludingInactiveState()
        {
            int counter = 0;
            var state = (SimState)Fill(typeof(SimState), ref counter);
            Assert.AreEqual(FusionWorldSnapshot.ByteCount, FusionWorldSnapshot.ActualByteCount);
            Assert.Greater(counter, 1000, "Exercise all fields, not just currently active entities.");
            var restored = FusionWorldSnapshot.Capture(state).Restore();
            EqualLeaves(typeof(SimState), state, restored, "State");
        }

        static object Fill(Type type, ref int counter)
        {
            if (type.IsEnum) return Enum.ToObject(type, ++counter % 3);
            if (type.IsPrimitive)
            {
                counter++;
                if (type == typeof(bool)) return counter % 2 == 0;
                return Convert.ChangeType(counter % 97 + 1, type);
            }
            Assert.IsTrue(type.IsValueType, "Snapshots cannot contain a managed reference: " + type);
            object value = Activator.CreateInstance(type);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                field.SetValue(value, Fill(field.FieldType, ref counter));
            return value;
        }

        static void EqualLeaves(Type type, object a, object b, string path)
        {
            if (type.IsEnum || type.IsPrimitive) { Assert.AreEqual(a, b, path); return; }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                EqualLeaves(field.FieldType, field.GetValue(a), field.GetValue(b), path + "." + field.Name);
        }

        [Test]
        public void Snapshot_RestoresPropContactsAndDestructionBeforeResimulation()
        {
            var world = RopeGraphTests.World(RopeGraphReplayTests.TrialArena());
            for (int t = 0; t < 360; t++)
            {
                var input = new InputFrame(0, 0, t == 0 ? InputButtons.Attack : InputButtons.None, 1);
                var snapshot = FusionWorldSnapshot.Capture(world.State);
                world.Step(input, default);
                uint expected = world.ComputeChecksum();
                world.Step(new InputFrame(1, 0, InputButtons.Jump), new InputFrame(-1, 0, InputButtons.Attack));
                world.State = snapshot.Restore();
                world.Step(input, default);
                Assert.AreEqual(expected, world.ComputeChecksum(), "resim tick " + t);
            }
            Assert.IsTrue(world.State.Props.IsDestroyed(0));
        }

        [Test]
        public void AuthoritativeJournal_ExportsExistingReplayWithoutPredictedFramesOrDuplicateEvents()
        {
            var world = RopeGraphTests.World(RopeGraphReplayTests.TrialArena());
            var maps = new[] { world.Arena };
            var host = new FusionConfirmedJournal(world.Config, maps, world.State);
            var client = new FusionConfirmedJournal(world.Config, maps, world.State);
            int events = 0;
            client.Confirmed += () => events++;
            for (int t = 0; t < 360; t++)
            {
                host.Append(new InputFrame(0, 0, t == 0 ? InputButtons.Attack : InputButtons.None, 1), default);
                if ((t + 1) % 4 != 0) continue;
                var batch = host.CreateBatch(t - 3, 4);
                client.AcceptBatch(batch); client.AcceptBatch(batch);
                Assert.AreEqual(host.World.ComputeChecksum(), client.World.ComputeChecksum());
            }
            Assert.AreEqual(360, events); Assert.AreEqual(90, client.DuplicateBatches);
            var replay = InputRecording.FromBytes(client.Finish().ToBytes());
            Assert.AreEqual(9, replay.WrittenVersion);
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(world.Config, maps));
            CollectionAssert.AreEqual(host.Finish().ToBytes(), client.Finish().ToBytes());
        }

        [Test]
        public void Journal_RejectsMissingOrCorruptFramesBeforeUiOrReplayCommit()
        {
            var w = TestWorlds.Create(); var maps = new[] { w.Arena };
            var host = new FusionConfirmedJournal(w.Config, maps, w.State);
            var client = new FusionConfirmedJournal(w.Config, maps, w.State);
            int events = 0; client.Confirmed += () => events++;
            for (int i = 0; i < 8; i++) host.Append(default, default);
            Assert.Throws<InvalidOperationException>(() => client.AcceptBatch(host.CreateBatch(4, 4)));
            var firstHost = new FusionConfirmedJournal(w.Config, maps, w.State);
            for (int i = 0; i < 4; i++) firstHost.Append(default, default);
            var corrupt = firstHost.CreateBatch(0, 4); corrupt[corrupt.Length - 1] ^= 1;
            Assert.Throws<InvalidOperationException>(() => client.AcceptBatch(corrupt));
            Assert.AreEqual(0, events); Assert.AreEqual(0, client.Recording.Count);
            Assert.AreEqual(w.ComputeChecksum(), client.World.ComputeChecksum());
        }

        [Test]
        public void NativeInput_RoundTripsAllButtonsAndDiscardsStaleCardIntent()
        {
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int buttons = 0; buttons < 256; buttons++)
            {
                var input = new InputFrame(x, y, (InputButtons)buttons, 64);
                Assert.AreEqual(input, FusionPlayerInput.Unpack(FusionPlayerInput.Pack(input)));
            }
            var state = TestWorlds.Create().State;
            var card = new FusionPlayerInput { Packed = FusionPlayerInput.Pack(new InputFrame(0, 0, InputButtons.Attack)), CardRound = 2 };
            state.Match.Round = 2; state.Match.Phase = MatchPhase.SetPick;
            Assert.IsTrue(card.ForState(state).Held(InputButtons.Attack));
            state.Match.Phase = MatchPhase.Fighting;
            Assert.AreEqual(default(InputFrame), card.ForState(state));
            state.Match.Phase = MatchPhase.SetPick; state.Match.Round = 3;
            Assert.AreEqual(default(InputFrame), card.ForState(state));
        }

        [Test]
        public void CardClick_PersistsUntilConfirmedAndCannotBeAcquiredTwiceAfterRollback()
        {
            var baseConfig = SimConfig.CreateDefault();
            var config = CardCollectionRules.Apply(baseConfig);
            var world = TestWorlds.Create(config: config);
            world.State.Match.Phase = MatchPhase.SetPick;
            world.State.Match.Picker = 1; world.State.Match.PickedAt = -1;
            world.State.Match.PhaseTick = 150;
            world.State.Match.Option0 = PickOption.AugmentBase + CardCollectionRules.FirstCardId;
            world.State.Match.Option1 = PickOption.AugmentBase + CardCollectionRules.FirstCardId + 1;
            world.State.Match.Option2 = PickOption.AugmentBase + CardCollectionRules.FirstCardId + 2;
            var source = new FrontendInputSource(() => world, () => false, new IdleInputSource(), 1);
            source.Poll(0); Assert.IsTrue(source.Choose(2));
            var collector = new FusionInputCollector(source, () => world, 1);
            var click = collector.Collect(1);
            Assert.AreNotEqual(0, click.Packed);
            for (int i = 0; i < 12; i++) Assert.AreEqual(click.Packed, collector.Collect(1.01 + i).Packed);
            var before = FusionWorldSnapshot.Capture(world.State);
            world.Step(default, click.ForState(world.State));
            uint expected = world.ComputeChecksum();
            Assert.AreEqual(1, world.State.Fighter1.Augments.Count);
            world.State = before.Restore();
            world.Step(default, click.ForState(world.State));
            Assert.AreEqual(expected, world.ComputeChecksum());
            for (int i = 0; i < 8; i++) world.Step(default, click.ForState(world.State));
            Assert.AreEqual(1, world.State.Fighter1.Augments.Count);
            Assert.AreEqual(0, collector.Collect(30).Packed);
        }

        [Test]
        public void ConfirmedCards_FiveSetsAndBothReadyRematchReplayWithoutUiSideEffects()
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: false, combinedAttackInput: true);
            combat.MaxHealth = 1; // Fast test fixture only. Keep the real two-round/five-set rules.
            var original = CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default),
                BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl));
            var match = original.Match;
            match.IntroCountdownTicks = 6; match.CountdownTicks = 6; match.KoTicks = 6;
            match.PickHoldTicks = 4; match.MatchEndMinTicks = 60; match.RematchBothHold = true;
            var config = new SimConfig(original.Movement, match, combat, original.Rules, original.Augments);
            var maps = new[] { ArenaFiles.LoadByName(MatchRunner.BrawlArenaName) };
            var initial = SimState.CreateMatch(config, maps, 7, MatchPhase.Countdown);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            var a = new FusionInputCollector(new FrontendInputSource(() => host.World, () => false,
                new BrawlChaserBot(() => host.World, 0, 10), 0, true), () => host.World, 0);
            var b = new FusionInputCollector(new FrontendInputSource(() => client.World, () => false,
                new IdleInputSource(), 1, true), () => client.World, 1);
            int peakCards = 0, sent = 0, events = 0;
            bool ended = false, rematched = false;
            client.Confirmed += () => events++;
            for (int t = 0; t < 12000; t++)
            {
                var ia = a.Collect(t / 60.0); var ib = b.Collect(t / 60.0);
                host.Append(ia.ForState(host.World.State), ib.ForState(host.World.State));
                peakCards = Math.Max(peakCards, host.World.State.Fighter1.Augments.Count);
                ended |= host.World.State.Match.Phase == MatchPhase.MatchEnd;
                rematched = ended && host.World.State.Match.Phase == MatchPhase.Countdown && host.World.State.Match.SetScore0 == 0;
                if (host.Recording.Count - sent == 4 || rematched)
                {
                    client.AcceptBatch(host.CreateBatch(sent, host.Recording.Count - sent));
                    sent = host.Recording.Count;
                }
                if (rematched) break;
            }
            Assert.IsTrue(ended); Assert.IsTrue(rematched);
            Assert.AreEqual(4, peakCards);
            Assert.AreEqual(0, host.World.State.Fighter1.Augments.Count, "Rematch clears the collected cards.");
            Assert.AreEqual(host.Recording.Count, events);
            CollectionAssert.AreEqual(host.Finish().ToBytes(), client.Finish().ToBytes());
            var replay = InputRecording.FromBytes(client.Finish().ToBytes());
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(config, maps));
        }
    }
}
