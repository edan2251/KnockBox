using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP1-4: Step 안에서 GC 할당이 없어야 한다.</summary>
    public class AllocationTests
    {
        const int Ticks = 600;

        [Test]
        public void Step_DoesNotAllocate()
        {
            TestWorlds.RandomInputs(31, Ticks, out var p1, out var p2);

            // 같은 입력으로 모든 경로를 한 번 지나가게 해서 첫 호출의 JIT·정적 초기화를 측정에서 뺀다.
            var warmup = TestWorlds.Create(MatchPhase.Countdown);
            for (int t = 0; t < Ticks; t++)
                warmup.Step(p1[t], p2[t]);

            var world = TestWorlds.Create(MatchPhase.Countdown);
            Assert.That(() =>
            {
                for (int t = 0; t < Ticks; t++)
                    world.Step(p1[t], p2[t]);
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(Ticks, world.State.Tick);
            Assert.AreEqual(warmup.ComputeChecksum(), world.ComputeChecksum());
        }

        [Test]
        public void ComputeChecksum_DoesNotAllocate()
        {
            var world = TestWorlds.Create();
            world.ComputeChecksum();
            uint sink = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++)
                    sink ^= world.ComputeChecksum();
            }, Is.Not.AllocatingGCMemory());
            Assert.AreNotEqual(0u, sink | 1u);
        }

        [Test]
        public void SyncTestAdvance_DoesNotAllocateWithoutMismatch()
        {
            TestWorlds.RandomInputs(32, Ticks, out var p1, out var p2);
            var world = TestWorlds.Create();
            var sync = new SyncTest<SimWorld>(world);
            for (int t = 0; t < 10; t++)
                sync.Advance(p1[t], p2[t]);

            Assert.That(() =>
            {
                for (int t = 10; t < Ticks; t++)
                    sync.Advance(p1[t], p2[t]);
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, sync.MismatchCount);
        }
    }
}
