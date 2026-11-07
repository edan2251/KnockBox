using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP1-3과 SyncTest 자체의 검출 능력.</summary>
    public class SyncTestTests
    {
        [Test]
        public void Rollback7_3600Ticks_NoMismatch()
        {
            TestWorlds.RandomInputs(2024, 3600, out var p1, out var p2);
            var world = TestWorlds.Create(MatchPhase.Countdown, seed: 3);
            var sync = new SyncTest<SimWorld>(world, 7);
            for (int t = 0; t < p1.Length; t++)
                sync.Advance(p1[t], p2[t]);

            Assert.AreEqual(3600, sync.Frame);
            Assert.AreEqual(0, sync.MismatchCount, sync.FirstMismatch?.ToString());

            // SyncTest를 거쳐도 결과가 그냥 진행한 것과 같아야 한다.
            var plain = TestWorlds.Create(MatchPhase.Countdown, seed: 3);
            for (int t = 0; t < p1.Length; t++)
                plain.Step(p1[t], p2[t]);
            Assert.AreEqual(plain.ComputeChecksum(), world.ComputeChecksum());
        }

        /// <summary>Step 밖의 값에 의존하는 가짜 시뮬레이션. 다시 진행하면 결과가 달라진다.</summary>
        sealed class LeakySim : ISimStepper
        {
            readonly SimWorld inner = TestWorlds.Create();
            int stepCalls;

            public SimState State
            {
                get => inner.State;
                set => inner.State = value;
            }

            public void Step(in InputFrame p1, in InputFrame p2)
            {
                inner.Step(p1, p2);
                stepCalls++;
                inner.State.Fighter0.Position.X += stepCalls * 0.001f;
            }

            public uint ComputeChecksum() => inner.ComputeChecksum();
        }

        [Test]
        public void NonDeterministicSim_IsDetectedWithFieldName()
        {
            var sim = new LeakySim();
            var sync = new SyncTest<LeakySim>(sim, 7);
            for (int t = 0; t < 20; t++)
                sync.Advance(default, default);

            Assert.Greater(sync.MismatchCount, 0, "불일치를 잡지 못함");
            // 첫 되감기 검사(7번째 Advance)에서 0프레임부터 다시 진행하므로 1프레임에서 바로 어긋난다.
            Assert.AreEqual(1, sync.FirstMismatch.Frame);
            StringAssert.Contains("Fighter0.Position.X", sync.FirstMismatch.Fields);
            StringAssert.DoesNotContain("Fighter1", sync.FirstMismatch.Fields);
        }

        [Test]
        public void StateDiff_ListsOnlyChangedFields()
        {
            var a = TestWorlds.Create().State;
            var b = a;
            b.Fighter1.Velocity.Y = 3f;
            b.Match.Score0 = 2;
            string diff = StateDiff.Describe(a, b);
            StringAssert.Contains("Fighter1.Velocity.Y: 0 → 3", diff);
            StringAssert.Contains("Match.Score0: 0 → 2", diff);
            Assert.AreEqual(2, diff.Trim().Split('\n').Length, diff);
            Assert.AreEqual(string.Empty, StateDiff.Describe(a, a));
        }
    }
}
