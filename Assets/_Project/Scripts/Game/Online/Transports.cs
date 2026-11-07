using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// 패킷 전달만 하는 연결(CR-004 온라인 2단계). 순서·도착을 보장하지 않는다(잃음·중복·순서 바뀜은 LockstepSession이 처리).
    /// 다음 단계에서 Unity Relay 같은 중계 서비스로 바꿔 끼울 자리다.
    /// </summary>
    public interface ITransport : IDisposable
    {
        void Send(byte[] data, int length);

        /// <summary>받은 패킷 하나. 없으면 false.</summary>
        bool TryReceive(out byte[] packet);
    }

    /// <summary>
    /// 패키지 없이 쓰는 UDP(System.Net.Sockets). 방장은 포트를 열고 처음 패킷을 보낸 상대 하나와만 주고받는다.
    /// 같은 PC 두 실행(127.0.0.1)과 같은 망 시험용. 인터넷 너머 연결(NAT 통과)은 하지 않는다.
    /// </summary>
    public sealed class UdpTransport : ITransport
    {
        readonly UdpClient client;
        IPEndPoint remote;

        UdpTransport(IPEndPoint local, IPEndPoint remote)
        {
            client = new UdpClient(local);
            client.Client.Blocking = false;
            // Windows: 상대 포트가 아직 닫혀 있을 때 ICMP 응답으로 다음 Receive가 예외를 내지 않게 한다(SIO_UDP_CONNRESET 끔).
            try
            {
                client.Client.IOControl(-1744830452, new byte[] { 0 }, null);
            }
            catch (Exception)
            {
                // Windows가 아니면 없는 옵션.
            }
            this.remote = remote;
        }

        public static UdpTransport Host(int port) => new UdpTransport(new IPEndPoint(IPAddress.Any, port), null);

        public static UdpTransport Join(string address, int port) =>
            new UdpTransport(new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.Parse(address), port));

        public int LocalPort => ((IPEndPoint)client.Client.LocalEndPoint).Port;

        public void Send(byte[] data, int length)
        {
            if (remote == null)
                return;
            try
            {
                client.Send(data, length, remote);
            }
            catch (SocketException)
            {
                // 보내기 실패는 패킷 잃음과 같게 본다.
            }
        }

        public bool TryReceive(out byte[] packet)
        {
            packet = null;
            while (true)
            {
                try
                {
                    if (client.Available <= 0)
                        return false;
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = client.Receive(ref from);
                    if (remote == null)
                        remote = from;
                    else if (!remote.Equals(from))
                        continue;
                    packet = data;
                    return true;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public void Dispose() => client.Dispose();
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
    public sealed class SimulatedLink : ITransport
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
