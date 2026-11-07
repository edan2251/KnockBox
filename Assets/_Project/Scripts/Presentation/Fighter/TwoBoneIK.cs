using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 해석적 2마디 IK. 뿌리, 목표, 두 마디 길이, 굽는 방향으로 관절 위치를 구한다.
    /// 2D Animation 패키지의 LimbSolver2D 대신 쓴다(DECISIONS.md S-030): 컴포넌트·뼈대 없이 계산만 하므로
    /// 할당이 없고 테스트하기 쉽다.
    /// </summary>
    public static class TwoBoneIK
    {
        /// <param name="bend">+1이면 뿌리→목표 방향의 왼쪽(반시계)으로, -1이면 오른쪽으로 관절이 나온다.</param>
        /// <param name="end">닿을 수 있으면 목표, 아니면 그 방향으로 최대한 뻗은 끝점.</param>
        public static Vector2 Solve(Vector2 root, Vector2 target, float upper, float lower, float bend, out Vector2 end)
        {
            Vector2 toTarget = target - root;
            float distance = toTarget.magnitude;
            float maxReach = upper + lower - 1e-4f;
            float minReach = Mathf.Abs(upper - lower) + 1e-4f;
            Vector2 direction = distance > 1e-6f ? toTarget / distance : Vector2.down;
            float d = Mathf.Clamp(distance, minReach, maxReach);
            end = root + direction * d;

            // 코사인 법칙: 뿌리에서 관절까지의 각.
            float cosAngle = Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f);
            float sinAngle = Mathf.Sqrt(1f - cosAngle * cosAngle) * Mathf.Sign(bend == 0f ? 1f : bend);
            var rotated = new Vector2(
                direction.x * cosAngle - direction.y * sinAngle,
                direction.x * sinAngle + direction.y * cosAngle);
            return root + rotated * upper;
        }
    }

    /// <summary>시뮬레이션의 지형 상자로 직접 계산하는 연출용 지면 찾기(Unity 물리 없음).</summary>
    public static class GroundQuery
    {
        /// <summary>x에서 fromY 아래(또는 같은 높이)에 있는 가장 높은 지형 윗면. 없으면 false.</summary>
        public static bool SurfaceBelow(Knockbox.Simulation.ArenaDef arena, float x, float fromY, out float surfaceY)
        {
            surfaceY = float.MinValue;
            bool found = false;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (x < b.MinX || x > b.MaxX || b.MaxY > fromY + 1e-3f)
                    continue;
                if (b.MaxY > surfaceY)
                {
                    surfaceY = b.MaxY;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>y 높이에서 side(-1 왼쪽, 1 오른쪽) 쪽으로 가장 가까운 단단한 벽면의 x.</summary>
        public static bool WallSurface(Knockbox.Simulation.ArenaDef arena, float x, float y, int side, out float wallX)
        {
            wallX = side > 0 ? float.MaxValue : float.MinValue;
            bool found = false;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.IsSolidBlock || y < b.MinY || y > b.MaxY)
                    continue;
                if (side > 0 && b.MinX >= x - 1e-3f && b.MinX < wallX)
                {
                    wallX = b.MinX;
                    found = true;
                }
                else if (side < 0 && b.MaxX <= x + 1e-3f && b.MaxX > wallX)
                {
                    wallX = b.MaxX;
                    found = true;
                }
            }
            return found;
        }
    }

    /// <summary>연출용 고정 시드 난수(xorshift). 캡처가 재현되게 UnityEngine.Random 대신 쓴다.</summary>
    public struct FxRandom
    {
        uint state;

        public FxRandom(uint seed)
        {
            state = seed == 0 ? 0x2545F491u : seed;
        }

        public float Next01()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return (x >> 8) * (1f / 16777216f);
        }

        public float Range(float min, float max) => min + (max - min) * Next01();
    }
}
