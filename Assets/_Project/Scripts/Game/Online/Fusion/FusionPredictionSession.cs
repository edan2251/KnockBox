using System;
using Fusion;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>Bridges SDK simulation to the existing match, confirmed UI/events, replay and diagnostics.</summary>
    public sealed class FusionPredictionSession : IDisposable
    {
        readonly FusionConnection connection;
        readonly FusionInputCollector collector;
        readonly int[] historyTick = new int[256];
        readonly uint[] historyHash = new uint[256];
        readonly Vec2[] historyPosition0 = new Vec2[256], historyPosition1 = new Vec2[256];
        readonly FusionPredictionDiagnostics diagnostics;
        int sentThrough, lastInputSequence;
        bool spawned;
        public MatchRunner Match { get; }
        public FusionConfirmedJournal Journal { get; }
        public int LocalSlot { get; }
        public bool Active { get; private set; } = true;
        public int ForwardTicks { get; private set; }
        public int ResimulationTicks { get; private set; }
        public int CorrectedTicks { get; private set; }
        public int MissingLocalInputs { get; private set; }
        public int MissingRemoteInputs { get; private set; }
        public float LargestCorrection { get; private set; }
        public int LocalInputSamples { get; private set; }
        public double LocalInputAgeTotal { get; private set; }
        public double LocalInputAgeMax { get; private set; }
        public string DiagnosticPath => diagnostics.FilePath;
        public FusionPredictionDiagnostics Diagnostics => diagnostics;
        /// <summary>가장 최근 Render에서 본 예측 틱(SimState 기준).</summary>
        public int PredictedTick { get; private set; } = -1;
        /// <summary>SDK가 마지막으로 받은 Host 상태 스냅샷의 틱(SimState 기준). 확정 저널(입력·체크섬 RPC)의 틱과 다르다.</summary>
        public int LatestServerTick { get; private set; } = -1;
        public int RenderObservedFrame { get; private set; } = -1;
        float frameCorrection;

        /// <summary>
        /// Host가 혼자 로비를 이어 시작하는 중: 공유 월드의 첫 틱 전까지 MatchRunner는 혼자 로비를 계속 돌리고,
        /// 첫 틱에 <see cref="BeginSharedLobby"/>가 P1을 이어 간 시작 상태로 바꾼다.
        /// </summary>
        public bool PendingLobbyStart { get; private set; }
        /// <summary>공유 월드가 로비에서 시작한다. Client는 Host의 첫 공유 상태를 받기 전에는 예측·표시하지 않는다(이어 간 P1을 모르므로).</summary>
        public bool StartsInLobby { get; }

        public FusionPredictionSession(FusionConnection connection, MatchRunner match, SimConfig config,
            ArenaDef[] arenas, IInputSource local, int slot, int delay)
            : this(connection, match, config, arenas, local, slot, delay, match.World.State, false) { }

        /// <param name="initial">확정 기록의 시작 상태(로비 이어 가기 전 값)</param>
        /// <param name="continueSoloLobby">Host: 혼자 로비를 공유 첫 틱까지 계속 돌리고 그때 P1을 이어 간다.</param>
        public FusionPredictionSession(FusionConnection connection, MatchRunner match, SimConfig config,
            ArenaDef[] arenas, IInputSource local, int slot, int delay, in SimState initial, bool continueSoloLobby)
        {
            FusionWorldSnapshot.ValidateLayout();
            this.connection = connection; Match = match; LocalSlot = slot;
            Journal = new FusionConfirmedJournal(config, arenas, initial);
            StartsInLobby = initial.Match.Phase == MatchPhase.Lobby;
            collector = new FusionInputCollector(local, () => Journal.World, slot);
            PendingLobbyStart = continueSoloLobby;
            if (!continueSoloLobby) match.BeginPrediction(slot, Journal.World);
            Journal.Confirmed += () => Match.PublishConfirmedTick();
            diagnostics = new FusionPredictionDiagnostics(slot, delay);
            connection.AttachPrediction(this);
        }

        public FusionPlayerInput Collect() => Active ? collector.Collect(Time.realtimeSinceStartupAsDouble) : default;

        public void Pump()
        {
            if (!Active || spawned || connection == null || connection.Network == null
                || !connection.Network.IsRunning || !connection.Network.IsServer || !connection.PredictionPeerReady) return;
            var prefab = Resources.Load<NetworkObject>(FusionPredictionWorld.ResourcePath);
            if (prefab == null) { Fail("Fusion 예측 프리팹이 빌드에 없습니다."); return; }
            spawned = true;
            connection.Network.Spawn(prefab, inputAuthority: connection.Peer, onBeforeSpawned: (network, obj) =>
            {
                var world = obj.GetComponent<FusionPredictionWorld>();
                world.Player1 = network.LocalPlayer;
                world.Player2 = connection.Peer;
                world.StartTick = network.Tick.Raw + SimConfig.TickRate;
                world.Snapshot = FusionWorldSnapshot.Capture(Journal.World.State);
            });
        }

        /// <summary>
        /// Host의 첫 공유 틱: 지금 혼자 로비의 P1(위치·속도·방향)을 이어 공유 시작 상태를 만들고 예측 경기로 넘긴다.
        /// 이어 갈 수 없으면(낙하·KO 중 등) 빈 값으로 두 선수 모두 기본 낙하로 시작한다. 이 값은 첫 확정 묶음으로 Client에 간다.
        /// </summary>
        public SimState BeginSharedLobby()
        {
            if (!PendingLobbyStart) throw new InvalidOperationException("혼자 로비 이어 가기 대기 중이 아닙니다.");
            PendingLobbyStart = false;
            var solo = Match.World;
            var carry = solo != null && solo.InLobby
                ? LobbyCarry.Capture(Journal.World.Config, Journal.World.LobbyArena, solo.State.Fighter0) : default;
            var start = Journal.ApplyLobbyCarry(carry);
            Match.BeginPrediction(LocalSlot, Journal.World);
            Match.MarkLobbyContinued(carry.Has ? 0 : -1);
            Debug.Log($"[Knockbox][prediction] 공유 로비 시작: P1 {(carry.Has ? "이어 감" : "다시 낙하")}");
            return start;
        }

        public void Step(in SimState before, InputFrame p1, InputFrame p2, bool resimulation,
            bool authority, bool got1, bool got2, FusionPlayerInput localInput)
        {
            Match.StepPrediction(before, p1, p2);
            ref var state = ref Match.World.State;
            int index = state.Tick & 255;
            uint hash = state.ComputeChecksum();
            if (resimulation)
            {
                ResimulationTicks++;
                if (historyTick[index] == state.Tick && historyHash[index] != hash)
                {
                    // 화면에 그리던 틱이 고쳐졌으면 표시만 짧게 이어 준다(판정은 고친 값 그대로).
                    if (state.Tick == PredictedTick)
                    {
                        Match.ReportPredictionCorrection(0, historyPosition0[index], state.Fighter0.Position, MatchRunner.SnapsDisplayCorrection(state.Fighter0));
                        Match.ReportPredictionCorrection(1, historyPosition1[index], state.Fighter1.Position, MatchRunner.SnapsDisplayCorrection(state.Fighter1));
                    }
                    CorrectedTicks++;
                    var delta0 = state.Fighter0.Position - historyPosition0[index];
                    var delta1 = state.Fighter1.Position - historyPosition1[index];
                    float size = Mathf.Max(Mathf.Sqrt(delta0.X * delta0.X + delta0.Y * delta0.Y), Mathf.Sqrt(delta1.X * delta1.X + delta1.Y * delta1.Y));
                    LargestCorrection = Mathf.Max(LargestCorrection, size);
                    frameCorrection = Mathf.Max(frameCorrection, size);
                }
            }
            else
            {
                ForwardTicks++;
                if (!(LocalSlot == 0 ? got1 : got2)) MissingLocalInputs++;
                // The client cannot read the opponent's input buffer; this is expected prediction, not packet loss.
                if (authority && !got2) MissingRemoteInputs++;
                if (localInput.Sequence > lastInputSequence)
                {
                    lastInputSequence = localInput.Sequence;
                    double age = Math.Max(0, Time.realtimeSinceStartupAsDouble - localInput.SampledAt);
                    LocalInputSamples++; LocalInputAgeTotal += age; LocalInputAgeMax = Math.Max(LocalInputAgeMax, age);
                    diagnostics.InputApplied(localInput.Sequence, localInput.SampledAt, Time.realtimeSinceStartupAsDouble, state.Tick);
                }
            }
            historyTick[index] = state.Tick; historyHash[index] = hash;
            historyPosition0[index] = state.Fighter0.Position; historyPosition1[index] = state.Fighter1.Position;
            if (!authority || resimulation) return;
            Journal.Append(p1, p2);
            if (Journal.World.ComputeChecksum() != hash) { Fail("호스트 확정 기록과 전투 상태가 다릅니다."); return; }
            // Send this tick now: batching would add up to three extra ticks to confirmed hit/card feedback.
            Flush();
        }

        public void Accept(byte[] batch)
        {
            if (!Active || connection.Network.IsServer) return;
            try { Journal.AcceptBatch(batch); }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            { Fail(e.Message); }
        }

        void Fail(string message)
        {
            Active = false;
            connection.FailPrediction(message);
        }

        void Flush()
        {
            if (connection == null || connection.Network == null || !connection.Network.IsRunning || !connection.Network.IsServer) return;
            int count = Journal.Recording.Count - sentThrough;
            if (count == 0) return;
            connection.SendPredictionBatch(Journal.CreateBatch(sentThrough, count));
            sentThrough += count;
        }


        /// <summary>Render에서 한 번: 표시 직전의 예측 틱과 SDK가 받은 최신 Host 스냅샷 틱(둘 다 SimState 기준).</summary>
        public void ObserveRender(int predictedTick, int latestServerTick)
        {
            PredictedTick = predictedTick; LatestServerTick = latestServerTick; RenderObservedFrame = Time.frameCount;
        }

        /// <summary>지난 프레임 기록 뒤 같은 틱 재실행에서 본 가장 큰 위치 보정(진단 전용).</summary>
        public float TakeFrameCorrection() { float v = frameCorrection; frameCorrection = 0; return v; }

        public void RecordFrame(double controlRtt, FusionTransport.ConnectionTiming timing) =>
            diagnostics.RecordFrame(this, controlRtt, timing);

        public InputRecording Finish()
        {
            Active = false;
            Flush();
            diagnostics.Dispose();
            return Journal.Finish();
        }

        public void Dispose()
        {
            Active = false;
            diagnostics.Dispose();
            if (connection != null) connection.DetachPrediction(this);
            if (Match != null) Match.EndPrediction();
        }
    }
}
