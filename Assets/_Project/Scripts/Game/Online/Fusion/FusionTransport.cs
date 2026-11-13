using System;
using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>Fusion과 기존 락스텝 사이의 패킷 어댑터. 경기 규칙과 UI는 참조하지 않는다.</summary>
    public sealed class FusionTransport : ITransport, ITransportLifetime
    {
        const int MaxQueuedPackets = 64;
        readonly FusionConnection owner;
        internal FusionConnection Connection => owner;
        readonly FusionPacketAssembler assembler = new FusionPacketAssembler();
        readonly Queue<(byte[] packet, double receivedAt)> inbox = new Queue<(byte[], double)>();
        readonly Func<double> clock;
        uint sequence;
        bool disposed;

        internal FusionTransport(FusionConnection owner) : this(owner, () => Time.realtimeSinceStartupAsDouble) { }

        internal FusionTransport(FusionConnection owner, Func<double> clock)
        {
            this.owner = owner;
            this.clock = clock;
        }

        public event Action<double> ReceiveQueueMeasured;
        public long SentFragments { get; private set; }
        public long ReceivedFragments { get; private set; }
        public long DroppedQueuedPackets { get; private set; }
        public ConnectionTiming ReadConnectionTiming() => owner != null ? owner.ReadConnectionTiming() : ConnectionTiming.Unavailable;

        /// <summary>SDK 관측값. RttSeconds에도 상대 PC의 처리 시간이 포함되며 순수 회선 Ping이 아니다.</summary>
        public struct ConnectionTiming
        {
            public string Region, Path;
            public double RttSeconds;
            public int TickRate, SendRate;
            public static ConnectionTiming Unavailable => new ConnectionTiming { Region = "unknown", Path = "unknown", RttSeconds = -1 };
        }

        public bool Ended { get; private set; }
        public EndReason Reason { get; private set; }
        public string EndMessage { get; private set; } = string.Empty;

        public void Send(byte[] data, int length)
        {
            if (disposed || Ended || owner == null || !owner.CanSend) return;
            foreach (var fragment in FusionPacketCodec.Encode(data, length, ++sequence))
            {
                owner.SendFragment(fragment);
                SentFragments++;
            }
        }

        internal void Receive(byte[] fragment, double time)
        {
            if (disposed || Ended) return;
            ReceivedFragments++;
            if (!assembler.TryAccept(fragment, time, out var packet)) return;
            // 수신 측이 멈춰도 메모리가 계속 늘지 않는다. 누락은 기존 락스텝 재전송으로 회복한다.
            if (inbox.Count >= MaxQueuedPackets) { inbox.Dequeue(); DroppedQueuedPackets++; }
            inbox.Enqueue((packet, time));
        }

        public bool TryReceive(out byte[] packet)
        {
            if (!disposed && inbox.Count > 0)
            {
                var received = inbox.Dequeue();
                packet = received.packet;
                ReceiveQueueMeasured?.Invoke(Math.Max(0, clock() - received.receivedAt));
                return true;
            }
            packet = null;
            return false;
        }

        internal void MarkEnded(string message, EndReason reason = EndReason.PeerLeft)
        {
            if (Ended) return;
            Reason = reason;
            Ended = true;
            EndMessage = message;
            assembler.Clear();
            inbox.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            MarkEnded("연결을 종료했습니다.", EndReason.Local);
            if (owner != null) owner.RequestShutdown();
        }
    }
}
