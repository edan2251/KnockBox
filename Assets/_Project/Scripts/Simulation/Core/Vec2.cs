using System;

namespace Knockbox.Simulation
{
    /// <summary>시뮬레이션 전용 2D 벡터. 길이·정규화 같은 연산은 SimMath를 거친다.</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => default;

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public float LengthSquared => X * X + Y * Y;

        // 비트 단위 비교. 결정론 검사에서 -0과 +0, NaN을 구분하기 위해서다.
        public bool Equals(Vec2 other) =>
            BitConverter.SingleToInt32Bits(X) == BitConverter.SingleToInt32Bits(other.X)
            && BitConverter.SingleToInt32Bits(Y) == BitConverter.SingleToInt32Bits(other.Y);

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => BitConverter.SingleToInt32Bits(X) * 397 ^ BitConverter.SingleToInt32Bits(Y);
        public override string ToString() => $"({X:0.####}, {Y:0.####})";
    }
}
