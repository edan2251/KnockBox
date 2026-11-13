using System;
using System.Collections.Generic;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// Fusion 전송과 검사에서 공통으로 사용하는 패킷 연결.
    /// 순서·도착을 보장하지 않는다(잃음·중복·순서 바뀜은 LockstepSession이 처리).
    /// </summary>
    public interface ITransport : IDisposable
    {
        void Send(byte[] data, int length);

        /// <summary>받은 패킷 하나. 없으면 false.</summary>
        bool TryReceive(out byte[] packet);
    }

    /// <summary>메모리 안의 두 끝(테스트용).</summary>
    public sealed class MemoryTransport : ITransport
    {
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        MemoryTransport peer;

        public static void Pair(out MemoryTransport a, out MemoryTransport b)
        {
            a = new MemoryTransport();
            b = new MemoryTransport();
            a.peer = b;
            b.peer = a;
        }

        public void Send(byte[] data, int length)
        {
            var copy = new byte[length];
            Array.Copy(data, copy, length);
            peer?.inbox.Enqueue(copy);
        }

        public bool TryReceive(out byte[] packet)
        {
            if (inbox.Count > 0)
            {
                packet = inbox.Dequeue();
                return true;
            }
            packet = null;
            return false;
        }

        public void Dispose() => peer = null;
    }

    /// <summary>
    /// 나쁜 망 흉내(보내는 쪽에 건다): 편도 지연 + 흔들림(순서 바뀜), 잃음, 중복. 수치는 편도 기준이며
    /// 두 끝에 같은 값을 걸면 왕복 지연(RTT)은 대략 편도 × 2다. 시간은 밖에서 준다(테스트는 가짜 시계).
    /// </summary>
    public sealed class SimulatedLink : ITransport, ITransportLifetime
    {
        readonly ITransport inner;
        readonly Func<double> now;
        readonly double oneWaySeconds;
        readonly double jitterSeconds;
        readonly double loss;
        readonly double duplicate;
        readonly Random random;
        readonly List<(double at, byte[] data)> pending = new List<(double, byte[])>();

        public SimulatedLink(ITransport inner, Func<double> now, double oneWaySeconds, double jitterSeconds, double loss, double duplicate, int seed)
        {
            this.inner = inner;
            this.now = now;
            this.oneWaySeconds = oneWaySeconds;
            this.jitterSeconds = jitterSeconds;
            this.loss = loss;
            this.duplicate = duplicate;
            random = new Random(seed);
        }

        public int Dropped { get; private set; }
        public int Duplicated { get; private set; }
        public bool Ended => inner is ITransportLifetime lifetime && lifetime.Ended;
        public EndReason Reason => inner is ITransportLifetime lifetime ? lifetime.Reason : EndReason.None;
        public string EndMessage => inner is ITransportLifetime lifetime ? lifetime.EndMessage : "";

        public void Send(byte[] data, int length)
        {
            Pump();
            int copies = random.NextDouble() < duplicate ? 2 : 1;
            if (copies == 2)
                Duplicated++;
            for (int c = 0; c < copies; c++)
            {
                if (random.NextDouble() < loss)
                {
                    Dropped++;
                    continue;
                }
                var copy = new byte[length];
                Array.Copy(data, copy, length);
                pending.Add((now() + oneWaySeconds + random.NextDouble() * jitterSeconds, copy));
            }
            Pump();
        }

        public bool TryReceive(out byte[] packet)
        {
            Pump();
            return inner.TryReceive(out packet);
        }

        /// <summary>시간이 된 패킷을 실제로 보낸다(도착 시각 순서라 흔들림만큼 순서가 바뀐다).</summary>
        public void Pump()
        {
            double t = now();
            while (true)
            {
                int best = -1;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i].at <= t && (best < 0 || pending[i].at < pending[best].at))
                        best = i;
                }
                if (best < 0)
                    return;
                var data = pending[best].data;
                pending.RemoveAt(best);
                inner.Send(data, data.Length);
            }
        }

        public void Dispose() => inner.Dispose();
    }
}
