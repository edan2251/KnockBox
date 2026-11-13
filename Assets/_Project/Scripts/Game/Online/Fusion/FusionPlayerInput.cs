using System;
using Fusion;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    // INetworkInput is also supported as a networked property. Implementing both markers
    // makes this SDK version weave only the struct attribute and fail Runner startup.
    public struct FusionPlayerInput : INetworkInput
    {
        public int Packed;
        public int CardRound;
        public int Sequence;
        public double SampledAt;

        public static int Pack(InputFrame f) => (byte)f.MoveX | ((byte)f.MoveY << 8) | ((byte)f.Buttons << 16) | (f.Aim << 24);
        public static InputFrame Unpack(int packed) => new InputFrame(unchecked((sbyte)packed),
            unchecked((sbyte)(packed >> 8)), (InputButtons)(packed >> 16), (byte)(packed >> 24));

        public InputFrame ForState(in SimState state)
        {
            // A retransmitted card click must never become an attack in a later round.
            if (CardRound > 0 && (state.Match.Round != CardRound || state.Match.Phase != MatchPhase.SetPick)) return default;
            return Unpack(Packed);
        }
    }

    /// <summary>OnInput only: never called during resimulation. Card intent persists until authority acknowledges it.</summary>
    public sealed class FusionInputCollector
    {
        readonly IInputSource source;
        readonly Func<SimWorld> confirmedWorld;
        readonly int localSlot;
        int sequence, choiceRound = -1, choice;

        public FusionInputCollector(IInputSource source, Func<SimWorld> confirmedWorld, int localSlot)
        { this.source = source; this.confirmedWorld = confirmedWorld; this.localSlot = localSlot; }

        public FusionPlayerInput Collect(double now)
        {
            var world = confirmedWorld();
            var frame = source?.Poll(world.State.Tick) ?? default;
            var match = world.State.Match;
            bool choosing = world.Config.Match.CollectCardsOnly && match.Phase == MatchPhase.SetPick
                && match.Picker == localSlot && match.PickedAt < 0;
            if (!choosing || choiceRound != match.Round) { choice = 0; choiceRound = -1; }
            if (choosing && frame.Buttons != InputButtons.None)
            { choice = FusionPlayerInput.Pack(frame); choiceRound = match.Round; }
            return new FusionPlayerInput
            {
                Packed = choosing && choiceRound == match.Round ? choice : FusionPlayerInput.Pack(frame),
                CardRound = choosing ? match.Round : 0, Sequence = ++sequence, SampledAt = now,
            };
        }
    }
}
