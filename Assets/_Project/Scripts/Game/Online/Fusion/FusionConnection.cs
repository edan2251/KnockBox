using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>사용자에게 그대로 표시해도 되는 설정/입력 안내. SDK 예외 원문은 여기에 넣지 않는다.</summary>
    public sealed class FusionSetupException : Exception
    {
        public FusionSetupException(string message) : base(message) { }
    }

    /// <summary>
    /// 방 하나의 Runner 수명을 소유한다. 새 접속마다 새 객체를 만들고, UI/전투/카드 상태는 갖지 않는다.
    /// Fusion 입력·상태 오브젝트와 시작 합의·연결 관리용 제어 메시지를 전달한다.
    /// </summary>
    public sealed class FusionConnection : SimulationBehaviour, INetworkRunnerCallbacks
    {
        const int HostAttempts = 3;
        const int StartupTimeoutSeconds = 30;
        NetworkRunner network;
        FusionTransport transport;
        PlayerRef peer;
        bool hasPeer, stopping, shutdownObserved, starting;
        CancellationTokenSource startupCancellation;
        Task<StartGameResult> startupTask;
        Task shutdownTask;
        readonly Queue<byte[]> predictionBatches = new Queue<byte[]>();
        internal NetworkRunner Network => network;
        internal PlayerRef Peer => peer;
        public FusionPredictionSession Prediction { get; private set; }
        internal bool PredictionPeerReady { get; private set; }
        /// <summary>받은 카드 호버를 넘길 곳(OnlineMatch가 경기마다 연결). 없으면 버린다.</summary>
        internal CardHoverRelay CardHover { get; set; }

        internal bool CanSend => !stopping && transport != null && !transport.Ended && hasPeer && network != null && network.IsRunning;

        internal FusionTransport.ConnectionTiming ReadConnectionTiming()
        {
            if (!CanSend) return FusionTransport.ConnectionTiming.Unavailable;
            var rates = TickRate.Resolve(network.Config.Simulation.TickRateSelection);
            return new FusionTransport.ConnectionTiming
            {
                Region = network.SessionInfo.Region ?? "unknown",
                Path = (network.IsServer ? network.GetPlayerConnectionType(peer) : network.CurrentConnectionType).ToString(),
                RttSeconds = network.GetPlayerRtt(network.IsServer ? peer : network.LocalPlayer),
                TickRate = network.TickRate,
                SendRate = network.IsServer ? rates.ServerSend : rates.ClientSend,
            };
        }

        public static async Task<(FusionTransport transport, string code)> HostAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appSettings = ReadSettings();
            for (int attempt = 0; attempt < HostAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string code = FusionRoomCode.Create();
                var connection = Create();
                try
                {
                    var result = await connection.StartAsync(GameMode.Host, code, appSettings, cancellationToken);
                    if (result.Ok) return (connection.transport, code);
                    Debug.LogWarning($"[Knockbox][online] Photon 생성 실패 단계: {result.ShutdownReason}");
#if UNITY_EDITOR
                    // Stack locations contain no room/app values; do not print the SDK message or settings.
                    Debug.LogWarning(result.StackTrace);
#endif
                    await connection.ShutdownAsync();
                    if (result.ShutdownReason != ShutdownReason.GameIdAlreadyExists)
                        throw new InvalidOperationException("Photon 방 생성에 실패했습니다.");
                }
                catch
                {
                    await connection.ShutdownAsync();
                    throw;
                }
            }
            throw new InvalidOperationException("사용 가능한 방 코드를 만들지 못했습니다.");
        }

        public static async Task<FusionTransport> JoinAsync(string code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FusionRoomCode.TryNormalize(code, out var normalized))
                throw new FusionSetupException("8자리 방 코드를 확인해주세요.");
            var appSettings = ReadSettings();
            cancellationToken.ThrowIfCancellationRequested();
            var connection = Create();
            try
            {
                var result = await connection.StartAsync(GameMode.Client, normalized, appSettings, cancellationToken);
                if (!result.Ok)
                {
                    Debug.LogWarning($"[Knockbox][online] Photon 참가 실패 단계: {result.ShutdownReason}");
                    throw new InvalidOperationException("Photon 방 참가에 실패했습니다.");
                }
                return connection.transport;
            }
            catch
            {
                await connection.ShutdownAsync();
                throw;
            }
        }

        static FusionAppSettings ReadSettings()
        {
            if (!PhotonAppSettings.TryGetGlobal(out var global) || global.AppSettings == null
                || !Guid.TryParse(global.AppSettings.AppIdFusion, out var appId) || appId == Guid.Empty)
                throw new FusionSetupException("Photon Fusion App ID가 설정되지 않았습니다. 설정 안내를 확인해주세요.");
            var policy = Resources.Load<FusionConnectionSettings>(FusionConnectionSettings.ResourcePath);
            var region = policy != null ? policy.Region : FusionConnectionSettings.DefaultRegion;
            var version = policy != null ? policy.AppVersion : FusionConnectionSettings.DefaultAppVersion;
            if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(version))
                throw new FusionSetupException("Photon 접속 지역과 게임 버전을 설정해주세요.");
            // 공유 에셋을 실행 중 수정하지 않는다. 두 피어 모두 같은 region/version을 사용한다.
            var settings = global.AppSettings.GetCopy();
            settings.FixedRegion = region.Trim().ToLowerInvariant();
            settings.AppVersion = FusionPredictionPolicy.ConnectionVersion(version);
            settings.ClientLogging = global::Photon.Client.LogLevel.Off;
            settings.NetworkLogging = global::Photon.Client.LogLevel.Off;
            return settings;
        }

        static FusionConnection Create()
        {
            var go = new GameObject("FusionConnection_Runtime");
            var connection = go.AddComponent<FusionConnection>();
            connection.network = go.AddComponent<NetworkRunner>();
            connection.network.ProvideInput = true;
            // Combat is a spawned state-only prefab; Main remains owned by the existing UI flow.
            connection.transport = new FusionTransport(connection);
            connection.network.AddCallbacks(connection);
            return connection;
        }

        async Task<StartGameResult> StartAsync(GameMode mode, string code, FusionAppSettings settings, CancellationToken cancellationToken)
        {
            starting = true;
            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupCancellation.CancelAfter(TimeSpan.FromSeconds(StartupTimeoutSeconds));
            try
            {
                startupTask = network.StartGame(new StartGameArgs
                {
                    Config = FusionPredictionPolicy.CreateConfig(NetworkProjectConfig.Global),
                    SceneManager = new FusionStateSceneManager(gameObject.scene),
                    GameMode = mode,
                    SessionName = code,
                    PlayerCount = 2,
                    IsOpen = true,
                    IsVisible = false,
                    EnableClientSessionCreation = false,
                    CustomPhotonAppSettings = settings,
                    StartGameCancellationToken = startupCancellation.Token,
                });
                var result = await startupTask;
                cancellationToken.ThrowIfCancellationRequested();
                if (startupCancellation.IsCancellationRequested)
                    throw new TimeoutException("Photon 연결 시간이 초과되었습니다.");
                // 이미 입장한 피어를 콜백과 동일한 경로로 등록한다.
                if (result.Ok)
                {
                    if (stopping || this == null || network == null)
                        throw new InvalidOperationException("Photon 연결이 종료되었습니다.");
                    foreach (var player in network.ActivePlayers) OnPlayerJoined(network, player);
                    if (transport.Ended) throw new InvalidOperationException("Photon 연결이 종료되었습니다.");
                }
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // SDK가 연결 제한 시간에 결과 대신 취소 예외를 던져도 UI의 사용자 취소 경로로 숨기지 않는다.
                throw new TimeoutException("Photon 연결 시간이 초과되었습니다.");
            }
            finally
            {
                startupCancellation.Dispose();
                startupCancellation = null;
                starting = false;
            }
        }

        internal void SendFragment(byte[] fragment)
        {
            if (CanSend) Rpc_LockstepFragment(network, peer, fragment);
        }

        internal void AttachPrediction(FusionPredictionSession session)
        {
            Prediction = session;
            while (predictionBatches.Count > 0) session.Accept(predictionBatches.Dequeue());
            if (!network.IsServer) Rpc_PredictionReady(network, peer);
        }

        internal void RegisterPredictionWorld(FusionPredictionWorld world)
        {
            // The coupled world has P2 input authority, so both host and client simulate it.
            network.SetIsSimulated(world.Object, true);
        }

        internal void DetachPrediction(FusionPredictionSession session)
        { if (Prediction == session) Prediction = null; }

        internal void FailPrediction(string message) => transport.MarkEnded(message, EndReason.Desync);
        internal void SendPredictionBatch(byte[] batch)
        { if (CanSend) Rpc_PredictionBatch(network, peer, batch); }
        internal void SendCardHover(ushort round, ushort sequence, sbyte option)
        { if (CanSend) Rpc_CardHover(network, peer, round, sequence, option); }

        [Rpc(Channel = RpcChannel.Reliable, TickAligned = false, InvokeLocal = false, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public static void Rpc_PredictionReady(NetworkRunner runner, [RpcTarget] PlayerRef target, RpcInfo info = default)
        {
            var c = runner.GetComponent<FusionConnection>();
            if (c == null || c.stopping || !runner.IsServer || !c.hasPeer || info.Source != c.peer) return;
            c.PredictionPeerReady = true;
        }

        [Rpc(Channel = RpcChannel.Reliable, TickAligned = false, InvokeLocal = false, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public static void Rpc_PredictionBatch(NetworkRunner runner, [RpcTarget] PlayerRef target, byte[] batch, RpcInfo info = default)
        {
            var c = runner.GetComponent<FusionConnection>();
            if (c == null || c.stopping || runner.IsServer || !c.hasPeer || info.Source != c.peer) return;
            if (c.Prediction != null) c.Prediction.Accept(batch);
            else if (c.predictionBatches.Count < 32) c.predictionBatches.Enqueue(batch);
            else c.transport.MarkEnded("확정 입력 대기 한도를 넘었습니다.", EndReason.ProtocolError);
        }

        // 시작 합의·heartbeat·확정 체크섬·종료 제어용. 실제 전투 입력은 Fusion OnInput을 사용한다.
        [Rpc(Channel = RpcChannel.Unreliable, TickAligned = false, InvokeLocal = false, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public static void Rpc_LockstepFragment(NetworkRunner runner, [RpcTarget] PlayerRef target, byte[] fragment, RpcInfo info = default)
        {
            var connection = runner.GetComponent<FusionConnection>();
            if (connection == null || connection.stopping || !connection.hasPeer || info.Source != connection.peer) return;
            connection.transport.Receive(fragment, Time.realtimeSinceStartupAsDouble);
        }

        // 카드 선택 무대의 호버(표시 전용). 잃어도 0.25초마다 다시 오므로 비신뢰 채널을 쓴다.
        // 시뮬레이션 입력·확정 기록·체크섬·리플레이에는 들어가지 않는다.
        [Rpc(Channel = RpcChannel.Unreliable, TickAligned = false, InvokeLocal = false, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public static void Rpc_CardHover(NetworkRunner runner, [RpcTarget] PlayerRef target, ushort round, ushort sequence, sbyte option, RpcInfo info = default)
        {
            var connection = runner.GetComponent<FusionConnection>();
            if (connection == null || connection.stopping || !connection.hasPeer || info.Source != connection.peer) return;
            connection.CardHover?.Receive(round, sequence, option, Time.realtimeSinceStartupAsDouble);
        }

        internal void RequestShutdown() => _ = ShutdownAsync();

        Task ShutdownAsync()
        {
            if (shutdownTask != null) return shutdownTask;
            stopping = true;
            transport?.MarkEnded("연결을 종료했습니다.", EndReason.Local);
            startupCancellation?.Cancel();
            shutdownTask = ShutdownCoreAsync();
            return shutdownTask;
        }

        async Task ShutdownCoreAsync()
        {
            try
            {
                // 시작 취소가 끝난 뒤 종료한다. StartGame과 Shutdown을 동시에 진행하지 않는다.
                if (startupTask != null && !startupTask.IsCompleted)
                    try { await startupTask; } catch { /* 아래에서 자원을 정리한다. */ }
                if (network != null) await network.Shutdown(destroyGameObject: false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Knockbox][online] Photon 종료 정리: {e.GetType().Name}");
            }
            finally
            {
                if (network != null) network.RemoveCallbacks(this);
                if (this != null) Destroy(gameObject);
            }
        }

        void Update()
        {
            if (shutdownObserved && !starting && !stopping) RequestShutdown();
        }

        void OnDestroy()
        {
            stopping = true;
            startupCancellation?.Cancel();
            transport?.MarkEnded("연결이 종료되었습니다.", EndReason.Local);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner != network || stopping || player == runner.LocalPlayer) return;
            if (hasPeer && peer != player) return; // PlayerCount 및 OnConnectRequest에서도 세 번째 피어를 거절한다.
            peer = player;
            hasPeer = true;
            if (runner.IsServer) runner.SessionInfo.IsOpen = false;
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner != network || !hasPeer || player != peer) return;
            transport.MarkEnded("상대가 방을 나갔습니다.");
            shutdownObserved = true;
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (runner != network || stopping || hasPeer) request.Refuse();
            else request.Accept();
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason reason)
        {
            if (runner != network) return;
            transport?.MarkEnded("Photon 연결이 종료되었습니다.", stopping ? EndReason.Local : EndReason.Timeout);
            shutdownObserved = true;
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (runner != network) return;
            transport?.MarkEnded("상대와 연결이 끊겼습니다.", stopping ? EndReason.Local : EndReason.Timeout);
            shutdownObserved = true;
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress address, NetConnectFailedReason reason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnInput(NetworkRunner runner, NetworkInput input)
        { input.Set(Prediction != null ? Prediction.Collect() : default(FusionPlayerInput)); }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> list) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken token)
        {
            if (runner != network) return;
            transport?.MarkEnded("방장 연결이 종료되었습니다.");
            shutdownObserved = true;
        }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
#pragma warning disable CS0618
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618
    }
}
