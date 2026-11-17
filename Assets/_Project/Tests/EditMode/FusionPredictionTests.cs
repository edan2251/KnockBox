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
        public void NativePolicy_UsesOne60HzClockAndZeroSdkDelayTicks()
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
            // 3틱·1틱은 빠른 연결에서 Client 예측을 받은 Host 상태 뒤에 묶어 화면이 정지·도약했다(2026-11-17 측정).
            Assert.AreEqual(0, config.TimeSync.MinimumInputDelay);
            Assert.AreEqual(0, FusionPredictionPolicy.DelayTicks);
            Assert.AreEqual(TimeSyncConfiguration.Unit.Ticks, config.TimeSync.MinimumInputDelayUnit);
            Assert.IsFalse(config.TimeSync.ClientsCanAddInputDelayToLimitResims);
            Assert.IsFalse(config.ClientsOnlyRollbackUpdatedObjects, "The coupled combat world must roll back together.");
            Assert.AreEqual(before, JsonUtility.ToJson(source), "Never modify the SDK project asset.");
        }

        /// <summary>
        /// 예측 보정 완화(2026-11-18, 화면 전용): 고친 만큼 그림을 원래 자리에 두었다가 짧게 줄이고, 충격 반응·큰 차이는 바로 보인다.
        /// </summary>
        [Test]
        public void PredictionCorrection_EasesSmallFixes_SnapsLaunchesAndLargeJumps()
        {
            var go = new GameObject("CorrectionTest");
            try
            {
                var runner = go.AddComponent<MatchRunner>();
                runner.ReportPredictionCorrection(0, new Vec2(0, 0.8f), new Vec2(0, 0), false);
                Assert.AreEqual(0.8f, runner.DisplayCorrection(0).y, 1e-5f, "고치기 전 자리에서 시작");
                runner.DecayDisplayCorrection(MatchRunner.CorrectionSeconds);
                Assert.Less(runner.DisplayCorrection(0).y, 0.8f * 0.4f);
                runner.DecayDisplayCorrection(0.3f);
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(0), "약 0.3초 안에 사라짐");
                runner.ReportPredictionCorrection(1, new Vec2(1, 0), new Vec2(0, 0), false);
                runner.ReportPredictionCorrection(1, new Vec2(1, 0), new Vec2(0, 0), true);
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(1), "맞아 날아감 등은 바로 보임");
                runner.ReportPredictionCorrection(0, new Vec2(MatchRunner.MaxSmoothedCorrection + 1, 0), new Vec2(0, 0), false);
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(0), "큰 차이(순간 이동)는 줄이지 않음");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// 보정 해제 보완(2026-11-18): 남은 보정이 있을 때 새 보정 보고 없이 실제 시뮬레이션이 피격·잡힘·던져짐으로 바뀌면
        /// 그 선수의 보정은 그 프레임에 바로 사라진다. 바뀌지 않은 선수의 보정은 그대로 줄인다(KO·재등장은 ResultsSpaceTests).
        /// </summary>
        [Test]
        public void PredictionCorrection_StaleOffset_ReleasedWhenSimEntersHitOrThrow()
        {
            var config = new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default),
                BrawlParams.Combat(CombatParams.Default, separateChargeInput: true), RuleSet.Brawl);
            var arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            float y = arena.SpawnFoot0.Y + config.HalfBody;
            var go = new GameObject("CorrectionReleaseTest");
            try
            {
                var runner = go.AddComponent<MatchRunner>();
                // 피격: 맞기 전 프레임까지는 남고, P2가 Hitstun이 된 첫 프레임에 P2만 지운다.
                var w = TestWorlds.CreateAt(new Vec2(-2f, y), new Vec2(-0.9f, y), config, arena);
                bool hit = false;
                for (int t = 1; t <= 40 && !hit; t++)
                {
                    StaleCorrection(runner);
                    w.Step(Aimed(t == 1 ? InputButtons.Attack : InputButtons.None, 0), default);
                    hit = w.State.Fighter1.Action == ActionState.Hitstun;
                    runner.UpdateDisplayCorrection(w.State, 0f);
                    if (!hit) Assert.AreNotEqual(Vector2.zero, runner.DisplayCorrection(1), $"맞기 전({t}틱)에는 남음");
                }
                Assert.IsTrue(hit, "주먹이 맞지 않음");
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(1), "맞은 첫 프레임에 바로 지움");
                Assert.AreNotEqual(Vector2.zero, runner.DisplayCorrection(0), "때린 쪽은 그대로 줄임");

                // 잡기 → 던져짐: 붙잡는 동안은 둘 다 지우고, 던지는 틱 직전에 남은 보정도 Thrown이 되면 지운다.
                w = TestWorlds.CreateAt(new Vec2(-2f, y), new Vec2(-0.9f, y), config, arena);
                for (int i = 0; i < 60 && w.State.Fighter0.Action != ActionState.Holding; i++)
                    w.Step(Aimed(InputButtons.Grab, 0), default);
                Assert.AreEqual(ActionState.Held, w.State.Fighter1.Action);
                StaleCorrection(runner);
                runner.UpdateDisplayCorrection(w.State, 0f);
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(0), "붙잡은 쪽");
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(1), "잡힌 쪽");
                StaleCorrection(runner);
                w.Step(Aimed(InputButtons.Attack, 8), default);
                Assert.AreEqual(ActionState.Thrown, w.State.Fighter1.Action);
                Assert.AreNotEqual(ActionState.Holding, w.State.Fighter0.Action);
                runner.UpdateDisplayCorrection(w.State, 0f);
                Assert.AreEqual(Vector2.zero, runner.DisplayCorrection(1), "던져진 첫 프레임에 바로 지움");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>두 선수 모두 화면에 남은 작은 보정(새 보고 없이 다음 프레임으로 넘어감).</summary>
        internal static void StaleCorrection(MatchRunner runner, int only = -1)
        {
            for (int i = 0; i < 2; i++)
                if (only < 0 || only == i) runner.ReportPredictionCorrection(i, new Vec2(0.5f, 0.3f), new Vec2(0f, 0f), false);
        }

        static InputFrame Aimed(InputButtons buttons, int aim) => new InputFrame(0, 0, buttons, AimDirections.Encode(aim));

        [Test]
        public void FrameTraceSummary_SeparatesSnapshotWaitStallsFromContinuousDisplayTime()
        {
            // 4ms 프레임(약 0.24틱씩 기대). 끊김: 상태가 올 때만 3틱 도약하고 사이에는 정지(2026-11-17 P2 측정 모양).
            var frameMs = new double[40];
            var stalled = new double[40];
            var smooth = new double[40];
            for (int i = 0; i < 40; i++)
            {
                frameMs[i] = 4;
                stalled[i] = 3 * (i / 12);
                smooth[i] = i * 0.24;
            }
            var bad = FrameTraceSummary.FromRenderTimes(stalled, frameMs);
            var good = FrameTraceSummary.FromRenderTimes(smooth, frameMs);
            Assert.Greater(bad.StallRatio, 0.5);
            Assert.AreEqual(3, bad.Jumps);
            Assert.AreEqual(0, good.Stalls);
            Assert.AreEqual(0, good.Jumps);
            Assert.Less(good.StepErrorRms, 1e-6);
            Assert.AreEqual(1, FrameTraceSummary.FromRenderTimes(new double[] { 5, 4 }, new double[] { 4, 4 }).Backwards);
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

        /// <summary>
        /// 결과 공간(2026-11-16 게임 흐름 CP2): 확정 입력으로 경기 끝 → 결과 공간(자유 조작·준비 토글) → 둘 다 준비 → 새 경기가
        /// 방장·손님에서 같고, 결과 공간 맵이 포함된 기록이 그대로 재생되며, 결과 공간 중 스냅샷 복원·재실행도 같은 결과를 낸다.
        /// </summary>
        [Test]
        public void ConfirmedResultsSpace_BothReadyRematch_MatchesOnBothPeersAndReplays()
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: false, combinedAttackInput: true);
            combat.MaxHealth = 1;
            var original = CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default),
                BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl), resultsSpace: true);
            var match = original.Match;
            match.IntroCountdownTicks = 6; match.CountdownTicks = 6; match.KoTicks = 6;
            match.PickHoldTicks = 4; match.RematchBothHold = true;
            var config = new SimConfig(original.Movement, match, combat, original.Rules, original.Augments);
            var results = UnityEditor.AssetDatabase.LoadAssetAtPath<Knockbox.Game.Maps.MapDefinition>("Assets/_Project/Data/Maps/Results/Results_Stage.asset").ToArena();
            var maps = new[] { ArenaFiles.LoadByName(MatchRunner.BrawlArenaName), results };
            var initial = SimState.CreateMatch(config, maps, 7, MatchPhase.Countdown);
            var host = new FusionConfirmedJournal(config, maps, initial);
            var client = new FusionConfirmedJournal(config, maps, initial);
            var a = new FusionInputCollector(new FrontendInputSource(() => host.World, () => false,
                new BrawlChaserBot(() => host.World, 0, 10), 0, true), () => host.World, 0);
            var b = new FusionInputCollector(new FrontendInputSource(() => client.World, () => false,
                new IdleInputSource(), 1, true), () => client.World, 1);
            int sent = 0, resultsTicks = 0;
            bool ended = false, rematched = false, snapshotChecked = false;
            for (int t = 0; t < 14000; t++)
            {
                var ia = a.Collect(t / 60.0); var ib = b.Collect(t / 60.0);
                var fa = ia.ForState(host.World.State); var fb = ib.ForState(host.World.State);
                if (host.World.InResultsSpace && host.World.State.Match.PhaseTick == config.Match.ResultsUnlockTick && !snapshotChecked)
                {
                    // 결과 공간 한가운데서도 전체 상태 스냅샷으로 되감아 같은 입력을 다시 넣으면 같은 결과다.
                    var before = FusionWorldSnapshot.Capture(host.World.State);
                    var probe = new SimWorld(config, maps, host.World.State);
                    probe.Step(fa, fb); uint expected = probe.ComputeChecksum();
                    probe.State = before.Restore(); probe.Step(fa, fb);
                    Assert.AreEqual(expected, probe.ComputeChecksum());
                    snapshotChecked = true;
                }
                host.Append(fa, fb);
                if (host.World.InResultsSpace) { ended = true; resultsTicks++; }
                rematched = ended && host.World.State.Match.Phase == MatchPhase.Countdown && host.World.State.Match.SetScore0 == 0;
                if (host.Recording.Count - sent == 4 || rematched)
                {
                    client.AcceptBatch(host.CreateBatch(sent, host.Recording.Count - sent));
                    sent = host.Recording.Count;
                    Assert.AreEqual(host.World.ComputeChecksum(), client.World.ComputeChecksum());
                }
                if (rematched) break;
            }
            Assert.IsTrue(ended, "경기가 끝나 결과 공간에 들어감");
            Assert.IsTrue(snapshotChecked);
            Assert.IsTrue(rematched, "둘 다 준비해 새 경기");
            Assert.GreaterOrEqual(resultsTicks, config.Match.ResultsUnlockTick);
            Assert.AreNotSame(host.World.ResultsArena, host.World.Arena);
            var replay = InputRecording.FromBytes(client.Finish().ToBytes());
            CollectionAssert.AreEqual(host.Finish().ToBytes(), client.Finish().ToBytes());
            Assert.AreEqual(replay.FinalChecksum, replay.Replay(config, maps));
        }
    }
}
