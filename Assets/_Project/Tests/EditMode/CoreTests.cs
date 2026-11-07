using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>기본형, 틱 시계, 입력 지연, JSON, 튜닝 변환, 임시 이동의 단위 테스트.</summary>
    public class CoreTests
    {
        [Test]
        public void SimRng_IsDeterministicAndNeverStuckAtZero()
        {
            var a = new SimRng(42);
            var b = new SimRng(42);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt());

            var zero = new SimRng(0);
            Assert.AreNotEqual(0u, zero.NextUInt());

            var r = new SimRng(9);
            for (int i = 0; i < 1000; i++)
            {
                int v = r.Range(-1, 2);
                Assert.That(v, Is.InRange(-1, 1));
                float f = r.NextFloat01();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void InputFrame_PressedAndReleased()
        {
            var prev = InputButtons.Jump | InputButtons.Guard;
            var cur = InputButtons.Jump | InputButtons.Dash;
            Assert.AreEqual(InputButtons.Dash, InputFrame.Pressed(cur, prev));
            Assert.AreEqual(InputButtons.Guard, InputFrame.Released(cur, prev));
            Assert.AreEqual(new InputFrame(1, -1, InputButtons.None), new InputFrame(5, -9, InputButtons.None), "방향은 -1~1로 자른다");
        }

        [Test]
        public void FixedTickClock_RunsSixtyTicksPerSecondAndCapsAtFour()
        {
            var clock = new FixedTickClock();
            int total = 0;
            for (int i = 0; i < 144; i++)
                total += clock.Advance(1.0 / 144.0);
            Assert.That(total, Is.InRange(59, 60), "144fps로 1초");

            clock.Reset();
            Assert.AreEqual(4, clock.Advance(0.5), "한 프레임에 최대 4틱");
            Assert.AreEqual(26, clock.DroppedTicks, "0.5초 = 30틱, 4틱 실행, 26틱 버림");
            Assert.That(clock.Alpha, Is.InRange(0f, 1f));

            clock.Reset();
            Assert.AreEqual(0, clock.Advance(1.0 / 120.0));
            Assert.That(clock.Alpha, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.AreEqual(1, clock.Advance(1.0 / 120.0));
        }

        [Test]
        public void InputDelayQueue_DelaysByConfiguredTicks()
        {
            var q = new InputDelayQueue(2);
            var a = new InputFrame(1, 0, InputButtons.None);
            var b = new InputFrame(-1, 0, InputButtons.Jump);
            var c = new InputFrame(0, 1, InputButtons.Dash);
            Assert.AreEqual(default(InputFrame), q.Push(a));
            Assert.AreEqual(default(InputFrame), q.Push(b));
            Assert.AreEqual(a, q.Push(c));
            Assert.AreEqual(b, q.Push(default));

            var none = new InputDelayQueue(0);
            Assert.AreEqual(c, none.Push(c));
        }

        [Test]
        public void MiniJson_ParsesNestedValues()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse("{\"a\": [1, -2.5e1, true, null, \"x\\\"y\"], \"b\": {}}");
            var list = (List<object>)root["a"];
            Assert.AreEqual(1.0, list[0]);
            Assert.AreEqual(-25.0, list[1]);
            Assert.AreEqual(true, list[2]);
            Assert.IsNull(list[3]);
            Assert.AreEqual("x\"y", list[4]);
            Assert.IsEmpty((Dictionary<string, object>)root["b"]);
        }

        [Test]
        public void MovementTuning_DefaultMatchesSpecDefaults()
        {
            var tuning = ScriptableObject.CreateInstance<MovementTuning>();
            try
            {
                Assert.AreEqual(SimConfig.CreateDefault().ComputeChecksum(), SimConfigFactory.Create(tuning).ComputeChecksum());
                tuning.Movement.MaxRunSpeed = 12f;
                var changed = SimConfigFactory.Create(tuning);
                Assert.AreEqual(12f, changed.Movement.MaxRunSpeed);
                Assert.AreNotEqual(SimConfig.CreateDefault().ComputeChecksum(), changed.ComputeChecksum());
                Assert.AreEqual(SimConfig.CreateDefault().ComputeChecksum(), SimConfigFactory.Create(null).ComputeChecksum());
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }
    }
}
