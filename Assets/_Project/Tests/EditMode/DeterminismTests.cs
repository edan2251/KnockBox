using System.Collections.Generic;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP1-1, CP1-2, CP1-7.</summary>
    public class DeterminismTests
    {
        const int LongRunTicks = 3600;

        [Test]
        public void SameSeedRandomInputs_ChecksumsMatchEveryTick()
        {
            TestWorlds.RandomInputs(12345, LongRunTicks, out var p1, out var p2);
            var a = TestWorlds.Create(MatchPhase.Countdown, seed: 7);
            var b = TestWorlds.Create(MatchPhase.Countdown, seed: 7);
            var distinct = new HashSet<uint>();
            float minX = float.MaxValue, maxX = float.MinValue;

            for (int t = 0; t < LongRunTicks; t++)
            {
                a.Step(p1[t], p2[t]);
                b.Step(p1[t], p2[t]);
                uint ca = a.ComputeChecksum();
                Assert.AreEqual(ca, b.ComputeChecksum(), $"틱 {t + 1}에서 체크섬 불일치\n{StateDiff.Describe(a.State, b.State)}");
                distinct.Add(ca);
                minX = System.Math.Min(minX, a.State.Fighter0.Position.X);
                maxX = System.Math.Max(maxX, a.State.Fighter0.Position.X);
            }

            // 의미 있는 상태 변화가 있었는지(카운트다운을 지나 실제로 움직였는지) 확인한다.
            Assert.AreEqual(MatchPhase.Fighting, a.State.Match.Phase);
            Assert.AreEqual(LongRunTicks, distinct.Count, "틱마다 상태가 달라야 함(Tick 필드 포함)");
            Assert.Greater(maxX - minX, 5f, "P1이 거의 움직이지 않음");
        }

        [Test]
        public void SaveAdvanceRestoreReplay_ChecksumMatches()
        {
            TestWorlds.RandomInputs(777, 300, out var p1, out var p2);
            var world = TestWorlds.Create();
            for (int t = 0; t < 200; t++)
                world.Step(p1[t], p2[t]);

            var snapshot = world.State;
            uint snapshotChecksum = world.ComputeChecksum();
            for (int t = 200; t < 300; t++)
                world.Step(p1[t], p2[t]);
            uint first = world.ComputeChecksum();
            Assert.AreNotEqual(snapshotChecksum, first, "100틱 진행 후 상태가 바뀌어야 함");

            world.State = snapshot;
            Assert.AreEqual(snapshotChecksum, world.ComputeChecksum(), "복원 직후 체크섬이 저장 시점과 다름");
            for (int t = 200; t < 300; t++)
                world.Step(p1[t], p2[t]);
            Assert.AreEqual(first, world.ComputeChecksum());
        }

        [Test]
        public void RecordSerializeReplay_FinalChecksumMatches()
        {
            var config = SimConfig.CreateDefault();
            var arena = TestWorlds.Box;
            var spawn0 = SimState.SpawnCenter(config, arena, 0);
            var spawn1 = SimState.SpawnCenter(config, arena, 1);
            var recording = InputRecording.Begin(config, arena, 99, spawn0, spawn1, MatchPhase.Countdown, "Ram");
            var world = new SimWorld(config, arena, SimState.CreateRound(config, arena, 99, spawn0, spawn1, MatchPhase.Countdown));

            TestWorlds.RandomInputs(4242, 1800, out var p1, out var p2);
            for (int t = 0; t < p1.Length; t++)
            {
                recording.Append(p1[t], p2[t]);
                world.Step(p1[t], p2[t]);
            }
            recording.Finish(world.ComputeChecksum());

            byte[] bytes = recording.ToBytes();
            var loaded = InputRecording.FromBytes(bytes);
            Assert.AreEqual(recording.Count, loaded.Count);
            Assert.AreEqual("Box", loaded.Arena);
            Assert.AreEqual("Ram", loaded.Mode);
            Assert.IsTrue(loaded.HasFinalChecksum);
            Assert.AreEqual(recording.FinalChecksum, loaded.FinalChecksum);
            for (int t = 0; t < p1.Length; t++)
            {
                Assert.AreEqual(p1[t], loaded.GetP1(t));
                Assert.AreEqual(p2[t], loaded.GetP2(t));
            }

            Assert.AreEqual(recording.FinalChecksum, loaded.Replay(config, arena));
        }

        [Test]
        public void ReplayWithDifferentConfig_IsRejected()
        {
            var config = SimConfig.CreateDefault();
            var arena = TestWorlds.Box;
            var recording = InputRecording.Begin(config, arena, 1, SimState.SpawnCenter(config, arena, 0), SimState.SpawnCenter(config, arena, 1), MatchPhase.Fighting, "");
            var movement = MovementParams.Default;
            movement.MaxRunSpeed += 1f;
            var other = new SimConfig(movement, MatchParams.Default);
            Assert.Throws<System.InvalidOperationException>(() => recording.Replay(other, arena));
            Assert.Throws<System.InvalidOperationException>(() => recording.Replay(config, Knockbox.Game.ArenaFiles.LoadByName("Pit")));
            Assert.DoesNotThrow(() => recording.Replay(config, arena));
        }

        [Test]
        public void Countdown_FreezesFightersThenStartsFighting()
        {
            var world = TestWorlds.Create(MatchPhase.Countdown);
            var start = world.State.Fighter0.Position;
            var run = new InputFrame(1, 0, InputButtons.None);
            int countdown = world.Config.Match.CountdownTicks;
            bool roundStartSeen = false;

            for (int t = 0; t < countdown; t++)
            {
                world.Step(run, run);
                for (int i = 0; i < world.Events.Count; i++)
                    roundStartSeen |= world.Events[i].Type == SimEventType.RoundStart;
            }
            Assert.AreEqual(MatchPhase.Fighting, world.State.Match.Phase);
            Assert.IsTrue(roundStartSeen, "RoundStart 이벤트가 없음");
            Assert.AreEqual(start, world.State.Fighter0.Position, "카운트다운 중에 움직임");

            world.Step(run, run);
            Assert.Greater(world.State.Fighter0.Position.X, start.X);
        }

        [Test]
        public void JumpHeldThroughCountdown_IsNotTreatedAsNewPress()
        {
            var world = TestWorlds.Create(MatchPhase.Countdown);
            var jump = new InputFrame(0, 0, InputButtons.Jump);
            for (int t = 0; t <= world.Config.Match.CountdownTicks; t++)
                world.Step(jump, jump);
            Assert.IsTrue(world.State.Fighter0.Grounded, "카운트다운 동안 누르고 있던 점프가 GO 직후 실행됨");
        }
    }
}
