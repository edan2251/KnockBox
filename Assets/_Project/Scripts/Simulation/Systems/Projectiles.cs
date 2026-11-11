namespace Knockbox.Simulation
{
    /// <summary>
    /// 선분-상자 교차(슬랩 방식). 연결부·연결 상자 판정(RopeConnectors·ConnectorBodies)과 무기(BrawlWeapons)가 쓴다.
    /// 2026-11-11 폼 전투 제거로 바위 폼 투사체 시스템은 지웠고, 공용 교차 함수만 남았다(이름은 호출처를 바꾸지 않으려고 유지).
    /// 상태의 투사체 풀(SimState.Projectiles)은 기록 체크섬 호환을 위해 비어 있는 채로 남는다.
    /// </summary>
    public static class Projectiles
    {
        /// <summary>from에서 delta만큼 가는 선분이 상자에 처음 들어가는 비율 0~1. 안 닿으면 2.</summary>
        public static float SegmentBox(Vec2 from, Vec2 delta, float minX, float minY, float maxX, float maxY)
        {
            float enter = 0f, exit = 1f;
            if (!Slab(from.X, delta.X, minX, maxX, ref enter, ref exit))
                return 2f;
            if (!Slab(from.Y, delta.Y, minY, maxY, ref enter, ref exit))
                return 2f;
            return enter;
        }

        static bool Slab(float x, float dx, float min, float max, ref float enter, ref float exit)
        {
            if (dx == 0f)
                return x >= min && x <= max;
            float inv = 1f / dx;
            float a = (min - x) * inv;
            float b = (max - x) * inv;
            if (a > b)
            {
                float tmp = a;
                a = b;
                b = tmp;
            }
            enter = SimMath.Max(enter, a);
            exit = SimMath.Min(exit, b);
            return enter <= exit;
        }
    }
}
