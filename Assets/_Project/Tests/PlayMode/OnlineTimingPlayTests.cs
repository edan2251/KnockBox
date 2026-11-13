using System.Collections;
using Knockbox.Game;
using Knockbox.Game.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    public sealed class OnlineTimingPlayTests
    {
        sealed class ObservedTransport : ITransport
        {
            readonly ITransport inner;
            public int LastPollFrame = -1;
            public ObservedTransport(ITransport inner) => this.inner = inner;
            public void Send(byte[] data, int length) => inner.Send(data, length);
            public bool TryReceive(out byte[] packet)
            {
                LastPollFrame = Time.frameCount;
                return inner.TryReceive(out packet);
            }
            public void Dispose() => inner.Dispose();
        }

        [UnityTest]
        public IEnumerator ActualPlayerLoop_PollsBothSessionsBeforeTheirMatchTicks()
        {
            var left = new GameObject("TimingHost");
            var right = new GameObject("TimingGuest");
            MatchRunner hostRunner = null, guestRunner = null;
            try
            {
                hostRunner = left.AddComponent<MatchRunner>();
                guestRunner = right.AddComponent<MatchRunner>();
                var host = left.AddComponent<OnlineMatch>();
                var guest = right.AddComponent<OnlineMatch>();
                yield return null; // Allow normal component startup before connecting.
                MemoryTransport.Pair(out var a, out var b);
                var hostWire = new ObservedTransport(a);
                var guestWire = new ObservedTransport(b);
                int hostTicks = 0, guestTicks = 0;
                host.StartSession(hostWire, true, OnlineMatch.DefaultOnlineDelay);
                guest.StartSession(guestWire, false, OnlineMatch.DefaultOnlineDelay);
                hostRunner.Ticked += _ =>
                {
                    Assert.AreEqual(Time.frameCount, hostWire.LastPollFrame, "Host tick ran before its current-frame receive pump.");
                    hostTicks++;
                };
                guestRunner.Ticked += _ =>
                {
                    Assert.AreEqual(Time.frameCount, guestWire.LastPollFrame, "Guest tick ran before its current-frame receive pump.");
                    guestTicks++;
                };
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                while ((hostTicks < 8 || guestTicks < 8) && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.GreaterOrEqual(hostTicks, 8);
                Assert.GreaterOrEqual(guestTicks, 8);
                Assert.AreEqual(3, host.Session.DelayTicks);
                Assert.AreEqual(3, guest.Session.DelayTicks);
                Assert.AreEqual(SessionState.Running, host.Session.State);
                Assert.AreEqual(SessionState.Running, guest.Session.State);
            }
            finally
            {
                // This offline memory-transport fixture checks scheduling, not replay export.
                hostRunner?.StopRecording();
                guestRunner?.StopRecording();
                Object.Destroy(left);
                Object.Destroy(right);
            }
        }
    }
}
