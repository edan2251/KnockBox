using System;
using Fusion;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// Same-build Windows snapshot, not a file/replay format. Includes every SimState byte,
    /// including RNG, card inventory, prop contacts and inactive compatibility state.
    /// A size change requires an explicit schema/version update and round-trip tests.
    /// </summary>
    public unsafe struct FusionWorldSnapshot : INetworkStruct
    {
        public const int ByteCount = 8692;
        public const int WordCount = ByteCount / sizeof(int);
        public fixed int Words[WordCount];

        public static int ActualByteCount => sizeof(SimState);

        public static FusionWorldSnapshot Capture(SimState state)
        {
            ValidateLayout();
            FusionWorldSnapshot snapshot = default;
            Buffer.MemoryCopy(&state, snapshot.Words, ByteCount, ByteCount);
            return snapshot;
        }

        public SimState Restore()
        {
            ValidateLayout();
            SimState state = default;
            fixed (int* source = Words) Buffer.MemoryCopy(source, &state, ByteCount, ByteCount);
            return state;
        }

        public static void ValidateLayout()
        {
            if (sizeof(SimState) != ByteCount || !BitConverter.IsLittleEndian)
                throw new InvalidOperationException("Fusion 전투 스냅샷 형식이 현재 빌드와 다릅니다.");
        }
    }
}
