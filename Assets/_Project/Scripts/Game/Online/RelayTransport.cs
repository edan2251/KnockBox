using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// Unity Relay 연결(CR-004 온라인 3단계, D-074): 익명 로그인(사용자 가입 없음) → 방장은 Relay 할당과 참가 코드, 손님은 코드로 참가 →
    /// Unity Transport 드라이버(DTLS, 순서·도착 보장 없는 기본 파이프라인)로 패킷만 주고받는다. 잃음·중복·순서는 LockstepSession이 처리.
    /// 같은 PC 두 실행이 같은 익명 계정이 되지 않게 역할마다 인증 프로필을 나눈다.
    /// </summary>
    public sealed class RelayTransport : ITransport
    {
        public const string ConnectionType = "dtls";
        /// <summary>방장 외 손님 수(2인). 4인이면 3.</summary>
        public const int MaxGuests = 1;

        NetworkDriver driver;
        NetworkConnection connection;
        readonly bool isHost;
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        int pumpedFrame = -1;

        RelayTransport(RelayServerData data, bool isHost)
        {
            this.isHost = isHost;
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref data);
            driver = NetworkDriver.Create(settings);
            if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                throw new InvalidOperationException("Relay 드라이버 Bind 실패");
            if (isHost)
            {
                if (driver.Listen() != 0)
                    throw new InvalidOperationException("Relay 드라이버 Listen 실패");
            }
            else
            {
                connection = driver.Connect(data.Endpoint);
            }
        }

        /// <summary>연결 상태(화면 표시용).</summary>
        public bool Connected { get; private set; }

        static async Task SignInAsync(string profile)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>방 만들기: (연결, 참가 코드).</summary>
        public static async Task<(RelayTransport transport, string code)> HostAsync(string profile = "host")
        {
            await SignInAsync(profile);
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxGuests);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return (new RelayTransport(AllocationUtils.ToRelayServerData(allocation, ConnectionType), true), code);
        }

        /// <summary>코드로 참가.</summary>
        public static async Task<RelayTransport> JoinAsync(string code, string profile = "guest")
        {
            await SignInAsync(profile);
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
            return new RelayTransport(AllocationUtils.ToRelayServerData(allocation, ConnectionType), false);
        }

        void Pump()
        {
            if (!driver.IsCreated || pumpedFrame == UnityEngine.Time.frameCount)
                return;
            pumpedFrame = UnityEngine.Time.frameCount;
            driver.ScheduleUpdate().Complete();
            if (isHost)
            {
                NetworkConnection incoming;
                while ((incoming = driver.Accept()) != default)
                {
                    // 2인: 처음 손님 하나만. 그 뒤 연결은 끊는다.
                    if (connection.IsCreated)
                        driver.Disconnect(incoming);
                    else
                    {
                        connection = incoming;
                        Connected = true;
                    }
                }
            }
            if (!connection.IsCreated)
                return;
            NetworkEvent.Type type;
            while ((type = driver.PopEventForConnection(connection, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        Connected = true;
                        break;
                    case NetworkEvent.Type.Data:
                        var data = new byte[reader.Length];
                        for (int i = 0; i < data.Length; i++)
                            data[i] = reader.ReadByte();
                        inbox.Enqueue(data);
                        break;
                    case NetworkEvent.Type.Disconnect:
                        // 끊김은 세션이 받은 것 없음(시간 초과)으로 판단한다.
                        Connected = false;
                        connection = default;
                        return;
                }
            }
        }

        public void Send(byte[] data, int length)
        {
            if (!driver.IsCreated || !connection.IsCreated || !Connected)
                return;
            if (driver.BeginSend(connection, out var writer) != 0)
                return;
            for (int i = 0; i < length; i++)
                writer.WriteByte(data[i]);
            driver.EndSend(writer);
        }

        public bool TryReceive(out byte[] packet)
        {
            Pump();
            if (inbox.Count > 0)
            {
                packet = inbox.Dequeue();
                return true;
            }
            packet = null;
            return false;
        }

        public void Dispose()
        {
            if (!driver.IsCreated)
                return;
            if (connection.IsCreated)
            {
                driver.Disconnect(connection);
                driver.ScheduleUpdate().Complete();
            }
            driver.Dispose();
        }
    }
}
