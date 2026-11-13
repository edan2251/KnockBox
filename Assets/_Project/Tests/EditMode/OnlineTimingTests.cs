using System;
using System.IO;
using System.Reflection;
using Knockbox.Game.Online;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public sealed class OnlineTimingTests
    {
        sealed class Pair : IDisposable
        {
            public double Now = 1;
            public readonly MemoryTransport Left, Right;
            public readonly LockstepSession Host, Guest;
            public Pair()
            {
                MemoryTransport.Pair(out var left, out var right);
                Left = left; Right = right;
                Host = new LockstepSession(left, true, 1, 2, 3, 4, () => Now);
                Guest = new LockstepSession(right, false, 1, 2, 0, 0, () => Now);
                Host.Poll(Now); Guest.Poll(Now); Host.Poll(Now); Guest.Poll(Now);
                Assert.AreEqual(SessionState.Running, Host.State);
                Assert.AreEqual(SessionState.Running, Guest.State);
                Host.Begin(0, null); Guest.Begin(0, null);
            }
            public void Dispose() { Host.Dispose(); Guest.Dispose(); }
        }

        [Test]
        public void ClockInitializationAndTimeout_UseTheInjectedClockConsistently()
        {
            MemoryTransport.Pair(out var a, out var b);
            double now = 30;
            using var host = new LockstepSession(a, true, 1, 2, 3, 4, () => now);
            using var guest = new LockstepSession(b, false, 1, 2, 0, 0);
            for (int i = 0; i < 15; i++) { guest.Poll(i * .05); host.Poll(i * .05); }
            Assert.AreEqual(SessionState.Running, host.State, host.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.State, guest.EndDetail);
            now += 6;
            host.Poll(.75);
            Assert.AreEqual(EndReason.Timeout, host.Reason, "Timeout must use the same clock as reception.");
        }

        [Test]
        public void FirstPollAtZero_IsNotReinitializedAtTheNextPoll()
        {
            MemoryTransport.Pair(out var a, out var b);
            using (b)
            using (var session = new LockstepSession(a, false, 1, 2, 3, 4))
            {
                session.Poll(0);
                session.Poll(LockstepSession.ConnectTimeoutSeconds + 1);
                Assert.AreEqual(EndReason.Timeout, session.Reason);
            }
        }

        [Test]
        public void Rtt_UsesSendTimeAndSubtractsActualPeerHold_AfterPoll()
        {
            using var pair = new Pair();
            pair.Now = 1.020;
            pair.Host.TryGetInputs(0, out _, out _); // Last Poll was at 1.000.
            pair.Now = 1.030;
            pair.Guest.Poll(pair.Now);              // 10 ms outbound.
            pair.Now = 1.080;
            pair.Guest.TryGetInputs(0, out _, out _); // 50 ms peer hold after its Poll.
            pair.Now = 1.100;
            pair.Host.Poll(pair.Now);              // 20 ms return.
            Assert.That(pair.Host.LastRttSeconds, Is.EqualTo(.030).Within(.002),
                "RTT must exclude the 50 ms peer hold and must not use the preceding Poll as send time.");
        }

        [Test]
        public void LocalInputAge_RemainsFourTicks_WithContinuousInputExchange()
        {
            using var pair = new Pair();
            int hostTick = 0, guestTick = 0, samples = 0;
            pair.Host.InputAgesMeasured += (local, remote) =>
            {
                Assert.That(local, Is.EqualTo(4.0 / 60).Within(.00001));
                Assert.That(remote, Is.GreaterThanOrEqualTo(0));
                samples++;
            };
            for (int frame = 0; frame < 120; frame++)
            {
                pair.Now += 1.0 / 60;
                pair.Host.Poll(pair.Now); pair.Guest.Poll(pair.Now);
                if (pair.Host.TryGetInputs(hostTick, out _, out _)) hostTick++;
                if (pair.Guest.TryGetInputs(guestTick, out _, out _)) guestTick++;
            }
            Assert.AreEqual(4, pair.Host.DelayTicks);
            Assert.AreEqual(4, pair.Guest.DelayTicks);
            Assert.AreEqual(120, hostTick);
            Assert.AreEqual(hostTick, guestTick);
            Assert.AreEqual(116, samples);
        }

        [Test]
        public void WaitingDuration_CountsElapsedTimeOnce_AndFreezesAfterRecovery()
        {
            using var pair = new Pair();
            pair.Now = 1.100;
            Assert.IsFalse(pair.Host.TryGetInputs(4, out _, out _));
            pair.Now = 1.130;
            Assert.IsFalse(pair.Host.TryGetInputs(4, out _, out _));
            pair.Now = 1.150;
            Assert.That(pair.Host.WaitingSeconds, Is.EqualTo(.050).Within(.00001));
            pair.Guest.TryGetInputs(0, out _, out _);
            pair.Host.Poll(pair.Now);
            Assert.IsTrue(pair.Host.TryGetInputs(4, out _, out _));
            pair.Now = 2;
            Assert.That(pair.Host.WaitingSeconds, Is.EqualTo(.050).Within(.00001));
            Assert.AreEqual(2, pair.Host.StallCount);
        }

        [Test]
        public void OutOfOrderDuplicate_PreservesFirstInputArrivalTime()
        {
            using var pair = new Pair();
            double remoteAge = -1;
            pair.Host.InputAgesMeasured += (_, remote) => remoteAge = remote;
            void Deliver(int index, double at)
            {
                pair.Now = at;
                var packet = InputPacket(index);
                pair.Right.Send(packet, packet.Length);
                pair.Host.Poll(at);
            }
            Deliver(5, 1.010);
            Deliver(5, 1.020); // Tick 4 is missing. Duplicate must not replace tick 5's arrival time.
            Deliver(4, 1.030);
            pair.Now = 1.050;
            Assert.IsTrue(pair.Host.TryGetInputs(5, out _, out _));
            Assert.That(remoteAge, Is.EqualTo(.040).Within(.00001));
        }

        static byte[] InputPacket(int tick)
        {
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            writer.Write(LockstepSession.Magic); writer.Write(LockstepSession.ProtocolVersion); writer.Write((byte)4);
            writer.Write(0u); writer.Write(0u); writer.Write((ushort)0);
            writer.Write(3); writer.Write(tick); writer.Write((byte)1);
            writer.Write(0u); // Neutral InputFrame, four bytes.
            writer.Write(-1); writer.Write(0u);
            return bytes.ToArray();
        }

        [Test]
        public void FusionQueueTiming_TracksCallbackToDequeue_AndCountsOverflow()
        {
            double now = 1;
            using var transport = (FusionTransport)Activator.CreateInstance(typeof(FusionTransport),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, (Func<double>)(() => now) }, null);
            var receive = typeof(FusionTransport).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic);
            double queueSeconds = -1;
            transport.ReceiveQueueMeasured += seconds => queueSeconds = seconds;
            for (uint sequence = 1; sequence <= 65; sequence++)
                foreach (var fragment in FusionPacketCodec.Encode(new byte[] { (byte)sequence }, 1, sequence))
                    receive.Invoke(transport, new object[] { fragment, now });
            now = 1.035;
            Assert.IsTrue(transport.TryReceive(out var packet));
            Assert.AreEqual(2, packet[0], "Oldest packet was dropped by the existing bounded inbox policy.");
            Assert.That(queueSeconds, Is.EqualTo(.035).Within(.00001));
            Assert.AreEqual(65, transport.ReceivedFragments);
            Assert.AreEqual(1, transport.DroppedQueuedPackets);
        }

    }
}
