using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 라운드마다 맵 교체(04-arena.md 6절). 경기 시드로 섞은 순서를 한 바퀴 다 쓰기 전에는 같은 맵이 다시 나오지 않고,
    /// 다음 바퀴를 섞을 때도 바로 앞 맵이 첫 맵이 되지 않는다. 순서는 맵 번호를 4비트씩 uint 하나에 담는다(최대 8개).
    /// Count가 1 이하면 순환하지 않는다(맵 하나로 고정한 월드, F2).
    /// </summary>
    [Serializable]
    public struct ArenaRotation
    {
        public const int MaxArenas = 8;

        public uint Seed;
        /// <summary>이번 바퀴의 순서. Slot번째 4비트가 현재 맵 번호.</summary>
        public uint Order;
        public int Count;
        /// <summary>이번 바퀴에서 몇 번째 맵인지(0부터).</summary>
        public int Slot;
        /// <summary>몇 번째 바퀴인지(0부터).</summary>
        public int Cycle;

        /// <summary>지금 라운드의 맵 번호(월드에 넘긴 맵 배열의 인덱스).</summary>
        public readonly int Current => Count > 1 ? Get(Order, Slot) : 0;

        public static ArenaRotation Create(uint seed, int count)
        {
            if (count < 1 || count > MaxArenas)
                throw new ArgumentOutOfRangeException(nameof(count), $"맵 순환은 1~{MaxArenas}개: {count}");
            var rotation = new ArenaRotation { Seed = seed, Count = count };
            if (count > 1)
                rotation.Order = Shuffle(seed, 0, count, -1);
            return rotation;
        }

        /// <summary>다음 라운드의 맵으로. 바퀴가 끝나면 새로 섞는다.</summary>
        public void Advance()
        {
            if (Count <= 1)
                return;
            int previous = Current;
            Slot++;
            if (Slot < Count)
                return;
            Slot = 0;
            Cycle++;
            Order = Shuffle(Seed, Cycle, Count, previous);
        }

        static int Get(uint order, int slot) => (int)((order >> (slot * 4)) & 0xFu);

        static uint Swap(uint order, int i, int j)
        {
            uint a = (uint)Get(order, i), b = (uint)Get(order, j);
            order &= ~((0xFu << (i * 4)) | (0xFu << (j * 4)));
            return order | (b << (i * 4)) | (a << (j * 4));
        }

        /// <summary>시드와 바퀴 번호로 정해지는 피셔-예이츠 섞기. 첫 맵이 avoidFirst면 두 번째와 바꾼다.</summary>
        static uint Shuffle(uint seed, int cycle, int count, int avoidFirst)
        {
            var h = Hasher.Create();
            h.AddUInt(seed);
            h.AddInt(cycle);
            var rng = new SimRng(h.Hash);
            uint order = 0;
            for (int i = 0; i < count; i++)
                order |= (uint)i << (i * 4);
            for (int i = count - 1; i > 0; i--)
                order = Swap(order, i, rng.Range(0, i + 1));
            if (Get(order, 0) == avoidFirst)
                order = Swap(order, 0, 1);
            return order;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Seed), Seed);
            v.Value(nameof(Order), Order);
            v.Value(nameof(Count), Count);
            v.Value(nameof(Slot), Slot);
            v.Value(nameof(Cycle), Cycle);
        }
    }
}
