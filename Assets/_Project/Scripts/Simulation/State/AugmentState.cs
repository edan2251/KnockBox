using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 파이터의 증강 획득 목록(10-forms-and-growth.md 5.3, CP23). 경기 끝까지 유지된다(D-051).
    /// 선택이 경기당 최대 4번이라 칸 4개면 충분하다(D-054). 같은 증강을 다시 얻으면 칸을 하나 더 쓴다(제한적 중복, D-056).
    /// 칸 값은 증강 번호 + 1(0은 빈 칸). 적용 결과는 목록과 설정에서 매번 결정적으로 계산하므로 상태에 두지 않는다.
    /// </summary>
    [Serializable]
    public struct AugmentState
    {
        public const int Capacity = 4;

        public int Count;
        public int Slot0;
        public int Slot1;
        public int Slot2;
        public int Slot3;

        public readonly int Get(int index) =>
            (index == 0 ? Slot0 : index == 1 ? Slot1 : index == 2 ? Slot2 : index == 3 ? Slot3 : 0) - 1;

        public void Add(int id)
        {
            if (Count >= Capacity)
                return;
            switch (Count)
            {
                case 0: Slot0 = id + 1; break;
                case 1: Slot1 = id + 1; break;
                case 2: Slot2 = id + 1; break;
                default: Slot3 = id + 1; break;
            }
            Count++;
        }

        /// <summary>그 증강을 몇 번 가졌는지.</summary>
        public readonly int Stacks(int id)
        {
            int n = 0;
            for (int i = 0; i < Count; i++)
            {
                if (Get(i) == id)
                    n++;
            }
            return n;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Count), Count);
            v.Value(nameof(Slot0), Slot0);
            v.Value(nameof(Slot1), Slot1);
            v.Value(nameof(Slot2), Slot2);
            v.Value(nameof(Slot3), Slot3);
        }
    }
}
