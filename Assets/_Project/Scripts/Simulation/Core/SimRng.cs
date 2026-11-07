using System;

namespace Knockbox.Simulation
{
    /// <summary>상태에 포함되는 xorshift32 난수. 같은 시드면 어디서든 같은 수열을 낸다.</summary>
    [Serializable]
    public struct SimRng
    {
        // 0은 xorshift의 고정점이라 시드로 쓸 수 없다.
        const uint ZeroSeedReplacement = 0x9E3779B9u;

        public uint State;

        public SimRng(uint seed)
        {
            State = seed == 0u ? ZeroSeedReplacement : seed;
        }

        public uint NextUInt()
        {
            uint x = State;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            State = x;
            return x;
        }

        /// <summary>[minInclusive, maxExclusive). 범위가 비면 minInclusive.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>[0, 1) 구간의 float. 상위 24비트를 쓴다.</summary>
        public float NextFloat01() => (NextUInt() >> 8) * (1f / 16777216f);
    }
}
