using Knockbox.Game.Online;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public sealed class FusionTransportLifetimeTests
    {
        sealed class ClosedLink : ITransport, ITransportLifetime
        {
            public bool Ended { get; set; }
            public EndReason Reason { get; set; } = EndReason.PeerLeft;
            public string EndMessage => "상대 연결 종료";
            public int SendCount;
            public void Send(byte[] data, int length) => SendCount++;
            public bool TryReceive(out byte[] packet) { packet = null; return false; }
            public void Dispose() { }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CarrierDisconnectEndsWaitingSessionWithoutWaitingForTimeout(bool host)
        {
            var link = new ClosedLink();
            using var session = new LockstepSession(link, host, 1, 2, 3, 4);
            session.Poll(1);
            int sent = link.SendCount;
            link.Ended = true;
            session.Poll(1.01);
            Assert.That(session.State, Is.EqualTo(SessionState.Ended));
            Assert.That(session.Reason, Is.EqualTo(EndReason.PeerLeft));
            Assert.That(session.EndDetail, Is.EqualTo(link.EndMessage));
            Assert.That(link.SendCount, Is.EqualTo(sent));
        }

        [Test]
        public void NetworkSimulationWrapperPreservesCarrierDisconnect()
        {
            var inner = new ClosedLink();
            var delayed = new SimulatedLink(inner, () => 1, .1, .02, .1, .01, 7);
            using var session = new LockstepSession(delayed, true, 1, 2, 3, 4);
            session.Poll(1);
            inner.Ended = true;
            session.Poll(1.01);
            Assert.That(session.State, Is.EqualTo(SessionState.Ended));
            Assert.That(session.EndDetail, Is.EqualTo(inner.EndMessage));
        }

        [Test]
        public void InvoluntaryConnectionLossKeepsTimeoutClassification()
        {
            var link = new ClosedLink { Ended = true, Reason = EndReason.Timeout };
            using var session = new LockstepSession(link, true, 1, 2, 3, 4);
            session.Poll(1);
            Assert.That(session.Reason, Is.EqualTo(EndReason.Timeout));
        }
    }
}
