using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>허수아비 입력원(06-match-and-tools.md 3절): 행동별 입력과 순환.</summary>
    public class DummyInputTests
    {
        [Test]
        public void Behaviors_ProduceExpectedInputs()
        {
            var dummy = new DummyInputSource(DummyBehavior.Idle);
            Assert.AreEqual(default(InputFrame), dummy.Poll(10));

            dummy.Behavior = DummyBehavior.AttackEvery40;
            int presses = 0;
            for (int t = 0; t < 400; t++)
                presses += dummy.Poll(t).Held(InputButtons.Attack) ? 1 : 0;
            Assert.AreEqual(10, presses, "40틱마다 한 틱씩");

            dummy.Behavior = DummyBehavior.JumpRepeat;
            Assert.IsTrue(dummy.Poll(0).Held(InputButtons.Jump));
            Assert.IsFalse(dummy.Poll(DummyInputSource.JumpHoldTicks).Held(InputButtons.Jump), "떼었다가 다시 눌러야 반복 점프");
            Assert.IsTrue(dummy.Poll(DummyInputSource.JumpPeriod).Held(InputButtons.Jump));

            dummy.Behavior = DummyBehavior.WalkBackAndForth;
            Assert.AreEqual(1, dummy.Poll(0).MoveX);
            Assert.AreEqual(-1, dummy.Poll(DummyInputSource.WalkPeriod).MoveX);
        }

        [Test]
        public void RandomBehavior_IsFixedSeed()
        {
            var a = new DummyInputSource(DummyBehavior.Random);
            var b = new DummyInputSource(DummyBehavior.Random);
            for (int t = 0; t < 300; t++)
                Assert.AreEqual(a.Poll(t), b.Poll(t), $"틱 {t}");
        }

        [Test]
        public void Cycle_WrapsBothWays()
        {
            Assert.AreEqual(DummyBehavior.JumpRepeat, DummyInputSource.Cycle(DummyBehavior.Idle, 1));
            Assert.AreEqual(DummyBehavior.Random, DummyInputSource.Cycle(DummyBehavior.Idle, -1));
            Assert.AreEqual(DummyBehavior.Idle, DummyInputSource.Cycle(DummyBehavior.Random, 1));
        }

    }
}
