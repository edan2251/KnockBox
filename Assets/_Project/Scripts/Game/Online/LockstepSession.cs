using System;
using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>MatchRunner가 틱마다 확정된 두 입력을 받는 곳(온라인 락스텝).</summary>
    public interface ILockstepInputs
    {
        /// <summary>이 클라이언트의 선수 슬롯(0=P1, 1=P2). 로컬 화면 표시에도 같은 배정을 쓴다.</summary>
        int LocalSlot { get; }

        /// <summary>이 틱의 두 입력이 모두 확정됐으면 true. 아니면 그 틱을 계산하지 않고 기다린다.</summary>
        bool TryGetInputs(int tick, out InputFrame p1, out InputFrame p2);
    }
}

namespace Knockbox.Game.Online
{
    public enum SessionState
    {
        /// <summary>손님: 방장에게 Hello를 보내는 중.</summary>
        Connecting,
        /// <summary>방장: 손님을 기다리는 중.</summary>
        WaitingPeer,
        Running,
        Ended,
    }

    public enum EndReason
    {
        None,
        /// <summary>상대가 나간다고 알림.</summary>
        PeerLeft,
        /// <summary>TimeoutSeconds 동안 아무 패킷도 오지 않음(연결 끊김), 손님이면 방장을 찾지 못함.</summary>
        Timeout,
        /// <summary>확정 틱 체크섬이 다름. 감지일 뿐이고 복구하지 않는다.</summary>
        Desync,
        /// <summary>이미 받은 틱의 입력이 다른 값으로 다시 옴, 또는 형식 오류.</summary>
        ProtocolError,
        /// <summary>방장과 빌드·설정이 다름.</summary>
        Rejected,
        /// <summary>이쪽이 끝냄.</summary>
        Local,
    }

    /// <summary>
    /// 2인 락스텝 입력 교환(CR-004 온라인 2단계, 4인은 슬롯 배열만 열어 둠). 각 PC가 같은 입력으로 같은 경기를 계산한다. 예측·되감기 없음.
    /// - 입력 지연 D틱: 이번 틱 t를 계산할 때 내 입력은 t + D 틱 몫으로 미리 뽑아 보낸다. 0 ~ D−1 틱은 모두 중립 입력.
    /// - 손실 대비: 패킷마다 상대가 아직 확인(ack)하지 않은 내 입력을 모두 다시 싣는다(최대 MaxFramesPerPacket).
    ///   ack = 빈틈없이 받은 상대 입력의 마지막 틱. 순서가 바뀌어 와도 틱 번호로 제자리에 넣는다.
    /// - 중복: 이미 받은 틱이 같은 값이면 버리고, 다른 값이면 ProtocolError로 멈춘다(덮어쓰지 않음).
    /// - 상대 입력이 없으면 그 틱을 계산하지 않고 기다린다(Waiting). 입력이 없어도 HeartbeatSeconds마다 보낸다.
    ///   TimeoutSeconds 동안 아무것도 오지 않으면 Timeout으로 끝낸다(재접속·이어 하기 없음).
    /// - 확정 틱 체크섬을 ChecksumInterval틱마다 주고받아 비교한다. 다르면 Desync로 멈춘다(복구 아님).
    /// - 지연 수치는 왕복(RTT): 패킷에 실은 보낸 시각을 상대가 되돌려 준 값으로 잰다.
    /// 런타임은 단조 시계를 주입해 실제 송수신 시각을 읽는다. 시계가 없는 검사는 Poll의 가짜 시간을 쓴다.
    /// </summary>
    public sealed class LockstepSession : ILockstepInputs, IDisposable
    {
        public const uint Magic = 0x4C4E424B; // "KBNL"
        public const byte ProtocolVersion = 9; // 등록 맵의 정렬된 ID/전체 지형 manifest와 시작 스냅샷을 공유하는 규격.
        public const int MaxPlayers = 4;
        public const int ChecksumInterval = 30;
        public const double HeartbeatSeconds = 0.1;
        public const double TimeoutSeconds = 5.0;
        public const double ConnectTimeoutSeconds = 15.0;
        const double HelloSeconds = 0.25;
        const int Ring = 1024;
        const int CheckRing = 64;
        const int MaxFramesPerPacket = 255;

        enum PacketType : byte { Hello = 1, Welcome = 2, Reject = 3, Input = 4, Bye = 5 }

        readonly ITransport transport;
        readonly Func<double> realtimeClock;
        readonly bool isHost;
        readonly uint configChecksum;
        readonly uint arenaChecksum;
        readonly byte[] sendBuffer = new byte[64 + MaxFramesPerPacket * InputFrame.Size];

        // 입력: [슬롯, 링]. tag = 틱 + 1(0이면 비어 있음).
        readonly InputFrame[,] frames = new InputFrame[MaxPlayers, Ring];
        readonly int[,] tags = new int[MaxPlayers, Ring];
        readonly double[,] inputTimes = new double[MaxPlayers, Ring];
        readonly int[] contiguous = new int[MaxPlayers];
        int localNext;
        int peerAck;
        IInputSource localSource;
        int startTick;

        // 체크섬: 확정 틱만. [링] = (틱, 값), 틱 −1이면 비어 있음.
        readonly int[] localCheckTick = new int[CheckRing];
        readonly uint[] localCheckValue = new uint[CheckRing];
        readonly int[] remoteCheckTick = new int[CheckRing];
        readonly uint[] remoteCheckValue = new uint[CheckRing];
        int latestLocalCheck = -1;

        double now;
        double startedAt;
        double lastReceive;
        double lastSend = double.NegativeInfinity;
        uint lastPeerStamp;
        double lastPeerStampAt;
        bool waiting;
        bool polled;
        double waitStarted, waitedSeconds;
        int lastMeasuredInput = -1;

        public LockstepSession(ITransport transport, bool isHost, uint configChecksum, uint arenaChecksum, uint seed, int delayTicks,
            Func<double> realtimeClock = null)
        {
            this.transport = transport;
            this.realtimeClock = realtimeClock;
            this.isHost = isHost;
            this.configChecksum = configChecksum;
            this.arenaChecksum = arenaChecksum;
            Seed = seed;
            DelayTicks = Math.Max(0, Math.Min(delayTicks, 30));
            LocalSlot = isHost ? 0 : 1;
            State = isHost ? SessionState.WaitingPeer : SessionState.Connecting;
            for (int i = 0; i < CheckRing; i++)
            {
                localCheckTick[i] = -1;
                remoteCheckTick[i] = -1;
            }
        }

        public SessionState State { get; private set; }
        public EndReason Reason { get; private set; }
        public string EndDetail { get; private set; } = "";
        public int LocalSlot { get; private set; }
        public int PlayerCount { get; private set; } = 2;
        /// <summary>방장이 정해 손님에게 보낸 시드·입력 지연. 손님은 Welcome을 받은 뒤 확정된다.</summary>
        public uint Seed { get; private set; }
        public int DelayTicks { get; private set; }
        public bool Began { get; private set; }

        /// <summary>왕복 지연(초), 부드럽게 한 값. 재기 전이면 −1.</summary>
        public double RttSeconds { get; private set; } = -1;
        public double LastRttSeconds { get; private set; } = -1;
        public double MaxRttSeconds { get; private set; }
        /// <summary>상대 입력이 없어 틱을 계산하지 못한 횟수.</summary>
        public int StallCount { get; private set; }
        /// <summary>방금 상대 입력을 기다렸는가(화면 표시용).</summary>
        public bool Waiting => waiting;
        public int ChecksumsCompared { get; private set; }
        public int DuplicatesIgnored { get; private set; }
        public int PacketsSent { get; private set; }
        public int PacketsReceived { get; private set; }
        public int DesyncTick { get; private set; } = -1;
        public double SecondsSinceReceive => now - lastReceive;
        public double WaitingSeconds => waitedSeconds + (waiting ? Math.Max(0, CurrentTime - waitStarted) : 0);

        // 진단 전용. 입력 내용·판정·패킷 형식에는 영향을 주지 않는다. 단위는 초.
        public event Action<double> RttMeasured;
        public event Action<double, double> InputAgesMeasured;
        double CurrentTime => realtimeClock != null ? realtimeClock() : now;

        /// <summary>경기를 시작한다. 손님은 Running이 된 뒤(Welcome 받은 뒤) 부른다. 이 틱부터 입력을 주고받는다.</summary>
        public void Begin(int tick, IInputSource local)
        {
            startTick = tick;
            localSource = local;
            waiting = false;
            waitedSeconds = 0;
            lastMeasuredInput = -1;
            for (int s = 0; s < MaxPlayers; s++)
            {
                for (int i = 0; i < DelayTicks; i++)
                {
                    frames[s, i] = default;
                    tags[s, i] = i + 1;
                }
                contiguous[s] = DelayTicks - 1;
            }
            localNext = DelayTicks;
            peerAck = DelayTicks - 1;
            Began = true;
        }

        public void Poll(double time)
        {
            now = time;
            now = CurrentTime;
            if (State != SessionState.Ended && transport is ITransportLifetime lifetime && lifetime.Ended)
            {
                End(lifetime.Reason, lifetime.EndMessage);
                return;
            }
            if (!polled)
            {
                polled = true;
                startedAt = now;
                lastReceive = now;
            }
            while (State != SessionState.Ended && transport.TryReceive(out var packet))
            {
                now = CurrentTime;
                Handle(packet);
            }
            if (State == SessionState.Ended)
                return;
            if (State == SessionState.Connecting)
            {
                if (now - lastSend >= HelloSeconds)
                    SendHello();
                if (now - startedAt > ConnectTimeoutSeconds)
                    End(EndReason.Timeout, "방장을 찾지 못함");
                return;
            }
            if (State == SessionState.Running)
            {
                if (now - lastReceive > TimeoutSeconds)
                {
                    End(EndReason.Timeout, $"{TimeoutSeconds:0}초 동안 받은 것이 없음");
                    return;
                }
                if (Began && now - lastSend >= HeartbeatSeconds)
                    SendInputs();
            }
        }

        public bool TryGetInputs(int tick, out InputFrame p1, out InputFrame p2)
        {
            p1 = default;
            p2 = default;
            if (State != SessionState.Running || !Began)
                return false;
            now = CurrentTime;
            int index = tick - startTick;
            bool sampled = false;
            while (localNext <= index + DelayTicks)
            {
                var f = localSource != null ? localSource.Poll(tick) : default;
                int r = localNext % Ring;
                frames[LocalSlot, r] = f;
                tags[LocalSlot, r] = localNext + 1;
                inputTimes[LocalSlot, r] = CurrentTime;
                contiguous[LocalSlot] = localNext;
                localNext++;
                sampled = true;
            }
            if (sampled)
                SendInputs();
            for (int s = 0; s < PlayerCount; s++)
            {
                if (contiguous[s] < index)
                {
                    if (!waiting) waitStarted = CurrentTime;
                    waiting = true;
                    StallCount++;
                    return false;
                }
            }
            StopWaiting();
            int at = index % Ring;
            p1 = frames[0, at];
            p2 = frames[1, at];
            if (index >= DelayTicks && index > lastMeasuredInput)
            {
                double usedAt = CurrentTime;
                InputAgesMeasured?.Invoke(Math.Max(0, usedAt - inputTimes[LocalSlot, at]),
                    Math.Max(0, usedAt - inputTimes[1 - LocalSlot, at]));
                lastMeasuredInput = index;
            }
            return true;
        }

        void StopWaiting()
        {
            if (waiting) waitedSeconds += Math.Max(0, CurrentTime - waitStarted);
            waiting = false;
        }

        /// <summary>틱 tick을 계산한 직후의 상태 체크섬(확정 틱). ChecksumInterval의 배수 틱만 쓴다.</summary>
        public void ReportChecksum(int tick, uint checksum)
        {
            if (State != SessionState.Running || tick % ChecksumInterval != 0)
                return;
            int slot = tick / ChecksumInterval % CheckRing;
            localCheckTick[slot] = tick;
            localCheckValue[slot] = checksum;
            latestLocalCheck = tick;
            Compare(slot);
        }

        public static bool NeedsChecksum(int tick) => tick % ChecksumInterval == 0;

        /// <summary>이쪽이 끝낸다(상대에게 Bye).</summary>
        public void Leave()
        {
            if (State == SessionState.Ended)
                return;
            for (int i = 0; i < 3; i++)
                SendSimple(PacketType.Bye);
            End(EndReason.Local, "이쪽이 끝냄");
        }

        public void Dispose()
        {
            Leave();
            transport.Dispose();
        }

        void Compare(int slot)
        {
            if (localCheckTick[slot] < 0 || localCheckTick[slot] != remoteCheckTick[slot])
                return;
            if (localCheckValue[slot] == remoteCheckValue[slot])
            {
                ChecksumsCompared++;
                return;
            }
            DesyncTick = localCheckTick[slot];
            End(EndReason.Desync, $"틱 {DesyncTick}: 이쪽 {localCheckValue[slot]:X8}, 상대 {remoteCheckValue[slot]:X8}");
        }

        void End(EndReason reason, string detail)
        {
            if (State == SessionState.Ended)
                return;
            StopWaiting();
            State = SessionState.Ended;
            Reason = reason;
            EndDetail = detail;
        }

        // 보내기 ---------------------------------------------------------------

        int WriteHeader(PacketType type)
        {
            int o = 0;
            WriteUInt(sendBuffer, ref o, Magic);
            sendBuffer[o++] = ProtocolVersion;
            sendBuffer[o++] = (byte)type;
            return o;
        }

        void Send(int length)
        {
            transport.Send(sendBuffer, length);
            lastSend = now;
            PacketsSent++;
        }

        void SendSimple(PacketType type) => Send(WriteHeader(type));

        void SendHello()
        {
            int o = WriteHeader(PacketType.Hello);
            WriteUInt(sendBuffer, ref o, configChecksum);
            WriteUInt(sendBuffer, ref o, arenaChecksum);
            Send(o);
        }

        void SendWelcome()
        {
            int o = WriteHeader(PacketType.Welcome);
            WriteUInt(sendBuffer, ref o, Seed);
            sendBuffer[o++] = (byte)DelayTicks;
            sendBuffer[o++] = 1;
            sendBuffer[o++] = (byte)PlayerCount;
            WriteUInt(sendBuffer, ref o, configChecksum);
            Send(o);
        }

        void SendReject(byte reason)
        {
            int o = WriteHeader(PacketType.Reject);
            sendBuffer[o++] = reason;
            Send(o);
        }

        void SendInputs()
        {
            // 입력 채취는 Poll 뒤에도 일어난다. 이전 Poll 시각을 송신 시각으로 재사용하지 않는다.
            now = CurrentTime;
            int o = WriteHeader(PacketType.Input);
            WriteUInt(sendBuffer, ref o, Stamp(now));
            WriteUInt(sendBuffer, ref o, lastPeerStamp);
            int hold = lastPeerStamp == 0 ? 0 : (int)Math.Min(ushort.MaxValue, (now - lastPeerStampAt) * 1000.0);
            WriteUShort(sendBuffer, ref o, (ushort)hold);
            int remoteSlot = 1 - LocalSlot;
            WriteInt(sendBuffer, ref o, contiguous[remoteSlot]);
            int first = Math.Max(peerAck + 1, DelayTicks);
            int count = Math.Max(0, Math.Min(localNext - first, MaxFramesPerPacket));
            WriteInt(sendBuffer, ref o, first);
            sendBuffer[o++] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                frames[LocalSlot, (first + i) % Ring].Write(sendBuffer, o);
                o += InputFrame.Size;
            }
            int checkSlot = latestLocalCheck < 0 ? -1 : latestLocalCheck / ChecksumInterval % CheckRing;
            WriteInt(sendBuffer, ref o, latestLocalCheck);
            WriteUInt(sendBuffer, ref o, checkSlot < 0 ? 0u : localCheckValue[checkSlot]);
            Send(o);
        }

        // 받기 -----------------------------------------------------------------

        void Handle(byte[] p)
        {
            if (p.Length < 6)
                return;
            int o = 0;
            if (ReadUInt(p, ref o) != Magic)
                return;
            if (p[o++] != ProtocolVersion)
            {
                End(EndReason.Rejected, "상대의 통신 형식 버전이 다름");
                return;
            }
            var type = (PacketType)p[o++];
            PacketsReceived++;
            lastReceive = now;
            try
            {
                switch (type)
                {
                    case PacketType.Hello:
                        if (!isHost)
                            return;
                        uint config = ReadUInt(p, ref o);
                        uint arena = ReadUInt(p, ref o);
                        if (config != configChecksum || arena != arenaChecksum)
                        {
                            SendReject(1);
                            return;
                        }
                        if (State == SessionState.WaitingPeer)
                            State = SessionState.Running;
                        SendWelcome();
                        break;
                    case PacketType.Welcome:
                        if (isHost || State != SessionState.Connecting)
                            return;
                        Seed = ReadUInt(p, ref o);
                        DelayTicks = p[o++];
                        LocalSlot = p[o++];
                        PlayerCount = p[o++];
                        if (ReadUInt(p, ref o) != configChecksum)
                        {
                            End(EndReason.Rejected, "방장과 설정이 다름(다른 빌드)");
                            return;
                        }
                        State = SessionState.Running;
                        break;
                    case PacketType.Reject:
                        End(EndReason.Rejected, "방장이 거절함: 빌드·설정·맵이 다름");
                        break;
                    case PacketType.Bye:
                        End(EndReason.PeerLeft, "상대가 나감");
                        break;
                    case PacketType.Input:
                        if (State == SessionState.Running && Began)
                            HandleInputs(p, o);
                        break;
                }
            }
            catch (IndexOutOfRangeException)
            {
                End(EndReason.ProtocolError, "패킷이 잘림");
            }
        }

        void HandleInputs(byte[] p, int o)
        {
            uint stamp = ReadUInt(p, ref o);
            uint echo = ReadUInt(p, ref o);
            int hold = ReadUShort(p, ref o);
            int ack = ReadInt(p, ref o);
            int first = ReadInt(p, ref o);
            int count = p[o++];
            if (stamp != 0)
            {
                lastPeerStamp = stamp;
                lastPeerStampAt = now;
            }
            if (echo != 0)
            {
                double rtt = Math.Max(0, ((long)Stamp(now) - echo - hold) / 1000.0);
                LastRttSeconds = rtt;
                MaxRttSeconds = Math.Max(MaxRttSeconds, rtt);
                RttSeconds = RttSeconds < 0 ? rtt : RttSeconds * 0.9 + rtt * 0.1;
                RttMeasured?.Invoke(rtt);
            }
            if (ack > peerAck)
                peerAck = Math.Min(ack, localNext - 1);

            int remote = 1 - LocalSlot;
            for (int i = 0; i < count; i++)
            {
                var f = InputFrame.Read(p, o);
                o += InputFrame.Size;
                int index = first + i;
                int r = index % Ring;
                if (index <= contiguous[remote])
                {
                    if (tags[remote, r] == index + 1)
                    {
                        if (!f.Equals(frames[remote, r]))
                        {
                            End(EndReason.ProtocolError, $"틱 {index + startTick}의 상대 입력이 다른 값으로 다시 옴");
                            return;
                        }
                        DuplicatesIgnored++;
                    }
                    continue;
                }
                if (index - contiguous[remote] >= Ring)
                    continue;
                if (tags[remote, r] == index + 1 && !f.Equals(frames[remote, r]))
                {
                    End(EndReason.ProtocolError, $"틱 {index + startTick}의 상대 입력이 다른 값으로 다시 옴");
                    return;
                }
                // 빠진 입력을 기다리는 동안 중복 패킷이 와도 최초 수신 시각은 보존한다.
                if (tags[remote, r] != index + 1) inputTimes[remote, r] = CurrentTime;
                frames[remote, r] = f;
                tags[remote, r] = index + 1;
            }
            while (tags[remote, (contiguous[remote] + 1) % Ring] == contiguous[remote] + 2)
                contiguous[remote]++;

            int checkTick = ReadInt(p, ref o);
            uint check = ReadUInt(p, ref o);
            if (checkTick >= 0)
            {
                int slot = checkTick / ChecksumInterval % CheckRing;
                if (remoteCheckTick[slot] != checkTick)
                {
                    remoteCheckTick[slot] = checkTick;
                    remoteCheckValue[slot] = check;
                    Compare(slot);
                }
            }
        }

        static uint Stamp(double seconds) => (uint)(seconds * 1000.0) | 1u;

        static void WriteUInt(byte[] b, ref int o, uint v)
        {
            b[o++] = (byte)v;
            b[o++] = (byte)(v >> 8);
            b[o++] = (byte)(v >> 16);
            b[o++] = (byte)(v >> 24);
        }

        static void WriteInt(byte[] b, ref int o, int v) => WriteUInt(b, ref o, (uint)v);

        static void WriteUShort(byte[] b, ref int o, ushort v)
        {
            b[o++] = (byte)v;
            b[o++] = (byte)(v >> 8);
        }

        static uint ReadUInt(byte[] b, ref int o)
        {
            uint v = (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
            o += 4;
            return v;
        }

        static int ReadInt(byte[] b, ref int o) => (int)ReadUInt(b, ref o);

        static int ReadUShort(byte[] b, ref int o)
        {
            int v = b[o] | b[o + 1] << 8;
            o += 2;
            return v;
        }
    }
}
