using System.Collections.Generic;
using System.Linq;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>카드 선택 호버 전송 규칙(표시 전용): 보내는 빈도, 손실 복구, 순번·라운드 거르기, 오래된 값 만료.</summary>
    public class CardHoverRelayTests
    {
        struct Packet { public double At; public ushort Round, Sequence; public sbyte Option; }

        static CardHoverRelay Sender(List<Packet> sent, System.Func<double> clock) =>
            new CardHoverRelay((r, s, o) => sent.Add(new Packet { At = clock(), Round = r, Sequence = s, Option = o }));

        [Test]
        public void Publish_SendsChangeImmediately_RepeatsEveryQuarterSecond_AndCapsRate()
        {
            var sent = new List<Packet>(); double now = 0;
            var relay = Sender(sent, () => now);
            relay.Publish(now, 5, -1);
            Assert.AreEqual(1, sent.Count, "첫 값은 바로 보낸다");
            for (now = 1 / 60.0; now < .249; now += 1 / 60.0) relay.Publish(now, 5, -1);
            Assert.AreEqual(1, sent.Count, "바뀌지 않으면 0.25초 동안 다시 보내지 않는다");
            now = .25; relay.Publish(now, 5, -1);
            Assert.AreEqual(2, sent.Count, "0.25초마다 같은 값을 다시 보내 손실을 메운다");
            now = .26; relay.Publish(now, 5, 1);
            Assert.AreEqual(2, sent.Count, "직전 전송 0.05초 안의 변경은 다음 프레임으로 미룬다");
            now = .31; relay.Publish(now, 5, 1);
            Assert.AreEqual(3, sent.Count); Assert.AreEqual(1, sent[2].Option);
            // 매 프레임 호버를 바꿔도 초당 20회를 넘지 않는다.
            int before = sent.Count;
            for (int f = 0; f < 60; f++) { now += 1 / 60.0; relay.Publish(now, 5, f % 3); }
            Assert.LessOrEqual(sent.Count - before, 21);
            CollectionAssert.AreEqual(Enumerable.Range(1, sent.Count).Select(i => (ushort)i), sent.Select(p => p.Sequence), "같은 라운드 안에서 순번은 1씩 커진다");
            Assert.IsTrue(sent.All(p => p.Round == 5));
        }

        [Test]
        public void Publish_RestartsSequencePerRound_AndStopResetsSchedule()
        {
            var sent = new List<Packet>(); double now = 0;
            var relay = Sender(sent, () => now);
            relay.Publish(now, 3, 0); now = .3; relay.Publish(now, 3, 0);
            Assert.AreEqual(2, sent.Last().Sequence);
            now = .31; relay.Publish(now, 4, 2);
            Assert.AreEqual(1, sent.Last().Sequence, "다음 선택 단계(라운드)는 순번 1부터");
            relay.StopPublishing();
            now = .32; relay.Publish(now, 4, 2);
            Assert.AreEqual(1, sent.Last().Sequence); Assert.AreEqual(4, sent.Count, "멈춘 뒤 다시 고르면 바로 보낸다");
            now = .40; relay.Publish(now, 4, 99);
            Assert.AreEqual(CardHoverRelay.None, sent.Last().Option, "범위 밖 선택지는 호버 없음으로 보낸다");
        }

        [Test]
        public void Receive_DropsStaleSequence_AcceptsNewRound_AndExpiresAfterSilence()
        {
            var relay = new CardHoverRelay(null);
            Assert.AreEqual(-1, relay.RemoteOption(0, 7), "받은 것이 없으면 호버 없음");
            relay.Receive(7, 3, 1, 0);
            Assert.AreEqual(1, relay.RemoteOption(.1, 7));
            relay.Receive(7, 2, 2, .2);
            Assert.AreEqual(1, relay.RemoteOption(.2, 7), "늦게 도착한 옛 순번은 버린다");
            relay.Receive(7, 3, 2, .2);
            Assert.AreEqual(1, relay.RemoteOption(.2, 7), "중복도 버린다");
            Assert.AreEqual(2, relay.Dropped);
            Assert.AreEqual(-1, relay.RemoteOption(.2, 8), "다른 라운드에서는 쓰지 않는다");
            // 버린 값은 수신 시각을 늘리지 않는다. 마지막으로 받아들인 값(0초) 기준으로 만료된다.
            Assert.AreEqual(1, relay.RemoteOption(CardHoverRelay.StaleSeconds - .01, 7));
            Assert.AreEqual(-1, relay.RemoteOption(CardHoverRelay.StaleSeconds + .01, 7), "0.75초 넘게 소식이 없으면 호버 없음");
            relay.Receive(1, 1, 0, 2);
            Assert.AreEqual(0, relay.RemoteOption(2, 1), "재대결로 라운드가 처음부터 시작해도 새 라운드 값을 받는다");
            relay.Receive(1, 2, 12, 2.1);
            Assert.AreEqual(-1, relay.RemoteOption(2.1, 1), "범위 밖 값은 호버 없음");
        }

        [Test]
        public void LossyDelayedLink_RecoversWithinRepeatPlusDelay()
        {
            // 편도 90ms, 손실 50%(결정적 패턴)에서 승자 화면은 늦어도 반복 주기 + 지연 + 한 번 더 잃은 만큼 안에 따라온다.
            var sent = new List<Packet>(); double now = 0;
            var sender = Sender(sent, () => now);
            var receiver = new CardHoverRelay(null);
            const double delay = .09;
            int delivered = 0, option = -1;
            double changedAt = 0, caughtUpAt = -1;
            for (int frame = 0; frame < 60 * 4; frame++)
            {
                now = frame / 60.0;
                if (frame == 60) { option = 2; changedAt = now; }
                sender.Publish(now, 9, option);
                foreach (var p in sent.Where(p => p.At + delay <= now && p.At + delay > now - 1 / 60.0))
                    if ((p.Sequence & 1) == 0) { receiver.Receive(p.Round, p.Sequence, p.Option, now); delivered++; }
                if (caughtUpAt < 0 && frame >= 60 && receiver.RemoteOption(now, 9) == 2) caughtUpAt = now;
            }
            Assert.Greater(delivered, 0);
            Assert.GreaterOrEqual(caughtUpAt, changedAt);
            Assert.LessOrEqual(caughtUpAt - changedAt, CardHoverRelay.RepeatSeconds * 2 + delay + .02);
            Assert.AreEqual(2, receiver.RemoteOption(now, 9), "반복 전송 덕분에 손실이 있어도 값이 유지된다(만료되지 않음)");
        }

        [Test]
        public void HoverStaysOutOfSimulationInput()
        {
            // 호버는 별도 RPC로만 다닌다. 시뮬레이션 입력 구조체·InputFrame에는 호버 칸이 없다.
            var inputFields = typeof(FusionPlayerInput).GetFields().Select(f => f.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "CardRound", "Packed", "SampledAt", "Sequence" }, inputFields);
            Assert.IsFalse(typeof(InputFrame).GetFields().Any(f => f.Name.ToLowerInvariant().Contains("hover")));
            Assert.IsFalse(typeof(MatchState).GetFields().Any(f => f.Name.ToLowerInvariant().Contains("hover")));
        }
    }
}
