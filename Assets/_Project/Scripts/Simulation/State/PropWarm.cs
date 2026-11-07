using System;

namespace Knockbox.Simulation
{
    /// <summary>웜 스타트용 접촉 하나(09 3절): 접촉 번호(상대 + 접점 특징, 0이면 빈 칸)와 지난 틱의 법선·마찰 충격량(서브스텝 하나 분량).</summary>
    [Serializable]
    public struct PropWarmSlot
    {
        public int Id;
        public float Normal;
        public float Tangent;
    }

    /// <summary>
    /// 몸 하나가 지난 틱에 가진 접촉의 충격량(고정 8칸). 접촉은 번호가 작은 몸(지형·경계는 그 몸) 쪽에 저장한다.
    /// 칸이 모자라면 남는 접촉은 웜 스타트 없이 0에서 시작한다(결정론에는 영향 없음). 이 파일은 스크립트로 만들었다(필드와 switch만 반복).
    /// </summary>
    [Serializable]
    public struct PropWarm
    {
        public const int Capacity = 8;

        public PropWarmSlot W0;
        public PropWarmSlot W1;
        public PropWarmSlot W2;
        public PropWarmSlot W3;
        public PropWarmSlot W4;
        public PropWarmSlot W5;
        public PropWarmSlot W6;
        public PropWarmSlot W7;

        public static ref PropWarmSlot Get(ref PropWarm w, int index)
        {
            switch (index)
            {
                case 0: return ref w.W0;
                case 1: return ref w.W1;
                case 2: return ref w.W2;
                case 3: return ref w.W3;
                case 4: return ref w.W4;
                case 5: return ref w.W5;
                case 6: return ref w.W6;
                case 7: return ref w.W7;
                default: throw new IndexOutOfRangeException($"웜 스타트 칸 {index}(용량 {Capacity})");
            }
        }

        public static ref readonly PropWarmSlot At(in PropWarm w, int index)
        {
            switch (index)
            {
                case 0: return ref w.W0;
                case 1: return ref w.W1;
                case 2: return ref w.W2;
                case 3: return ref w.W3;
                case 4: return ref w.W4;
                case 5: return ref w.W5;
                case 6: return ref w.W6;
                case 7: return ref w.W7;
                default: throw new IndexOutOfRangeException($"웜 스타트 칸 {index}(용량 {Capacity})");
            }
        }

        /// <summary>id의 지난 충격량. 없으면 0(자주 불려 칸을 switch 없이 바로 본다).</summary>
        public readonly bool Find(int id, out float normal, out float tangent)
        {
            if (W0.Id == id)
            {
                normal = W0.Normal;
                tangent = W0.Tangent;
                return true;
            }
            if (W1.Id == id)
            {
                normal = W1.Normal;
                tangent = W1.Tangent;
                return true;
            }
            if (W2.Id == id)
            {
                normal = W2.Normal;
                tangent = W2.Tangent;
                return true;
            }
            if (W3.Id == id)
            {
                normal = W3.Normal;
                tangent = W3.Tangent;
                return true;
            }
            if (W4.Id == id)
            {
                normal = W4.Normal;
                tangent = W4.Tangent;
                return true;
            }
            if (W5.Id == id)
            {
                normal = W5.Normal;
                tangent = W5.Tangent;
                return true;
            }
            if (W6.Id == id)
            {
                normal = W6.Normal;
                tangent = W6.Tangent;
                return true;
            }
            if (W7.Id == id)
            {
                normal = W7.Normal;
                tangent = W7.Tangent;
                return true;
            }
            normal = 0f;
            tangent = 0f;
            return false;
        }

        /// <summary>첫 빈 칸에 넣는다. 칸이 없으면 버린다.</summary>
        public static void Add(ref PropWarm w, int id, float normal, float tangent)
        {
            for (int i = 0; i < Capacity; i++)
            {
                ref var slot = ref Get(ref w, i);
                if (slot.Id != 0)
                    continue;
                slot.Id = id;
                slot.Normal = normal;
                slot.Tangent = tangent;
                return;
            }
        }

        static string Name(int index) => index switch
        {
            0 => "W0",
            1 => "W1",
            2 => "W2",
            3 => "W3",
            4 => "W4",
            5 => "W5",
            6 => "W6",
            7 => "W7",
            _ => "W?",
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            for (int i = 0; i < Capacity; i++)
            {
                ref readonly var slot = ref At(in this, i);
                v.Begin(Name(i));
                v.Value("Id", slot.Id);
                v.Value("Normal", slot.Normal);
                v.Value("Tangent", slot.Tangent);
                v.End();
            }
        }
    }
}
