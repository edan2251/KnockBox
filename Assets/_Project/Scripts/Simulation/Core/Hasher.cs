using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 32비트 FNV-1a. 상태 방문자로 쓰면 SimState의 모든 필드를 정해진 순서로 섞는다.
    /// float는 비트 패턴으로 넣는다(01-architecture.md 4절 8항).
    /// </summary>
    public struct Hasher : IStateVisitor
    {
        public const uint OffsetBasis = 2166136261u;
        public const uint Prime = 16777619u;

        uint hash;
        bool started;

        public uint Hash => started ? hash : OffsetBasis;

        public static Hasher Create()
        {
            var h = new Hasher();
            h.hash = OffsetBasis;
            h.started = true;
            return h;
        }

        public void AddByte(byte b)
        {
            if (!started)
            {
                hash = OffsetBasis;
                started = true;
            }
            hash ^= b;
            hash *= Prime;
        }

        public void AddUInt(uint v)
        {
            AddByte((byte)v);
            AddByte((byte)(v >> 8));
            AddByte((byte)(v >> 16));
            AddByte((byte)(v >> 24));
        }

        public void AddInt(int v) => AddUInt(unchecked((uint)v));
        public void AddFloat(float v) => AddInt(BitConverter.SingleToInt32Bits(v));
        public void AddBool(bool v) => AddByte(v ? (byte)1 : (byte)0);

        // IStateVisitor: 이름은 무시하고 값만 섞는다. 범위 표시도 순서가 같으므로 따로 섞지 않는다.
        public void Begin(string scope) { }
        public void End() { }
        public void Value(string name, int v) => AddInt(v);
        public void Value(string name, uint v) => AddUInt(v);
        public void Value(string name, float v) => AddFloat(v);
        public void Value(string name, bool v) => AddBool(v);
    }
}
