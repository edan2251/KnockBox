using Fusion;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    /// <summary>The one coupled combat object. Fusion restores these properties before every resimulation.</summary>
    public sealed class FusionPredictionWorld : NetworkBehaviour
    {
        public const string ResourcePath = "FusionPredictionWorld";
        [Networked] public FusionWorldSnapshot Snapshot { get; set; }
        [Networked] public FusionPlayerInput LastP1 { get; set; }
        [Networked] public FusionPlayerInput LastP2 { get; set; }
        [Networked] public PlayerRef Player1 { get; set; }
        [Networked] public PlayerRef Player2 { get; set; }
        [Networked] public int StartTick { get; set; }
        FusionConnection connection;

        public override void Spawned()
        {
            connection = Runner.GetComponent<FusionConnection>();
            connection?.RegisterPredictionWorld(this);
        }

        public override void FixedUpdateNetwork()
        {
            var session = connection != null ? connection.Prediction : null;
            if (session == null || !session.Active || Runner.Tick.Raw < StartTick) return;
            // 로비 시작: Client는 Host가 정한 첫 공유 상태(이어 간 P1)를 받은 뒤부터 예측한다.
            if (WaitsForHostStart(session)) return;
            var before = Snapshot.Restore();
            if (HasStateAuthority && session.PendingLobbyStart) before = session.BeginSharedLobby();
            bool got1 = Runner.TryGetInputForPlayer(Player1, out FusionPlayerInput p1);
            bool got2 = Runner.TryGetInputForPlayer(Player2, out FusionPlayerInput p2);
            if (got1) LastP1 = p1; else p1 = LastP1;
            if (got2) LastP2 = p2; else p2 = LastP2;
            var a = p1.ForState(before);
            var b = p2.ForState(before);
            session.Step(before, a, b, Runner.IsResimulation, HasStateAuthority,
                got1, got2, session.LocalSlot == 0 ? p1 : p2);
            Snapshot = FusionWorldSnapshot.Capture(session.Match.World.State);
        }

        bool WaitsForHostStart(FusionPredictionSession session) =>
            !HasStateAuthority && session.StartsInLobby && Runner.LatestServerTick.Raw < StartTick;

        public override void Render()
        {
            var session = connection != null ? connection.Prediction : null;
            if (session == null || !session.Active || WaitsForHostStart(session)) return;
            var latest = Snapshot.Restore();
            int offset = Runner.Tick.Raw - latest.Tick;
            session.ObserveRender(latest.Tick, Runner.LatestServerTick.Raw - offset);
            session.Match.SetPredictionRenderState(latest, Runner.LocalAlpha);
        }
    }
}
