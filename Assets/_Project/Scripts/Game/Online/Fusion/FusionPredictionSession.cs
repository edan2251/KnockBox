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

        public FusionPredictionSession(FusionConnection connection, MatchRunner match, SimConfig config,
            ArenaDef[] arenas, IInputSource local, int slot, int delay)
        {
            FusionWorldSnapshot.ValidateLayout();
            this.connection = connection; Match = match; LocalSlot = slot;
            Journal = new FusionConfirmedJournal(config, arenas, match.World.State);
            collector = new FusionInputCollector(local, () => Journal.World, slot);
            match.BeginPrediction(slot, Journal.World);
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
                    CorrectedTicks++;
                    var delta0 = state.Fighter0.Position - historyPosition0[index];
                    var delta1 = state.Fighter1.Position - historyPosition1[index];
                    LargestCorrection = Mathf.Max(LargestCorrection,
                        Mathf.Sqrt(delta0.X * delta0.X + delta0.Y * delta0.Y), Mathf.Sqrt(delta1.X * delta1.X + delta1.Y * delta1.Y));
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
