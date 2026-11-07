using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 시뮬레이션이 쓰는 수학 연산의 유일한 관문. 고정소수점으로 바꿀 때 이 파일만 고친다(D-004).
    /// 삼각함수는 두지 않는다. 각도는 설정 단계에서 단위 벡터로 바꿔 둔다.
    /// </summary>
    public static class SimMath
    {
        public const float Epsilon = 1e-6f;

        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static float Abs(float x) => x < 0f ? -x : x;
        public static int Abs(int x) => x < 0 ? -x : x;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Floor(float x) => MathF.Floor(x);
        public static float Ceiling(float x) => MathF.Ceiling(x);

        /// <summary>가장 가까운 정수. 0.5는 위로 올린다(음수에서도 같은 규칙: -2.5 → -2).</summary>
        public static int RoundToInt(float x) => (int)MathF.Floor(x + 0.5f);

        /// <summary>정수 지수 거듭제곱. 곱셈만 쓰므로 순서가 정해져 있다.</summary>
        public static float Pow(float x, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent; i++)
                result *= x;
            return result;
        }

        public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;
        public static int Clamp(int x, int min, int max) => x < min ? min : x > max ? max : x;

        /// <summary>-1, 0, 1. 0은 0으로 돌려준다.</summary>
        public static int Sign(float x) => x > 0f ? 1 : x < 0f ? -1 : 0;

        /// <summary>current를 target 쪽으로 최대 maxDelta만큼 옮긴다. 넘치지 않는다.</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (Abs(delta) <= maxDelta)
                return target;
            return current + (delta > 0f ? maxDelta : -maxDelta);
        }

        public static float Length(Vec2 v) => Sqrt(v.LengthSquared);

        /// <summary>길이가 0에 가까우면 0 벡터를 돌려준다.</summary>
        public static Vec2 Normalize(Vec2 v)
        {
            float len = Length(v);
            return len > Epsilon ? v / len : Vec2.Zero;
        }
    }
}
