namespace Knockbox.Simulation
{
    /// <summary>한 틱 이동의 접촉 결과. 상태가 아니라 그 틱 안에서만 쓴다.</summary>
    public struct MoveContacts
    {
        public bool Landed;
        public int LandedBox;
        public TerrainKind LandedKind;
        /// <summary>착지 직전의 아래 방향 속력.</summary>
        public float ImpactSpeed;
        public bool HitCeiling;
        /// <summary>천장에 닿기 직전의 위 방향 속력.</summary>
        public float CeilingImpactSpeed;
        /// <summary>이번 틱에 가로로 막힌 쪽. -1 왼쪽, 1 오른쪽, 0 없음.</summary>
        public int HitWall;
        /// <summary>벽에 닿기 직전의 가로 속력.</summary>
        public float WallImpactSpeed;
        public bool OutOfBounds;
        /// <summary>경계 튕김(CR-002): 이번 틱에 닿은 경계. -1 왼쪽·아래, 1 오른쪽·위, 0 없음.</summary>
        public int BoundaryX;
        public int BoundaryY;
        /// <summary>경계에 닿기 직전의 경계 법선 방향(바깥쪽) 속력.</summary>
        public float BoundarySpeedX;
        public float BoundarySpeedY;
    }

    /// <summary>
    /// 지형 충돌(02-movement.md 6절). X축, Y축 순서로 분리해서 처리하고, 한 하위 단계의 이동량이
    /// SubstepMaxDistance(0.2)를 넘지 않게 나눈다. 지형 최소 두께 0.5와 함께 최대 속도에서도 벽을 뚫지 않는다.
    /// 여러 상자에 동시에 걸리면 상자 순서와 무관하게 가장 가까운 면을 고른다(좌우 대칭 유지).
    /// </summary>
    public static class TerrainCollision
    {
        public const float Epsilon = 1e-4f;

        public static void Move(ref Vec2 position, ref Vec2 velocity, float half, ArenaDef arena, float substepMax, int ignoreOneWayBox, ref MoveContacts contacts)
        {
            float startBottom = position.Y - half;
            float dx = velocity.X * SimConfig.Dt;
            float dy = velocity.Y * SimConfig.Dt;
            float largest = SimMath.Max(SimMath.Abs(dx), SimMath.Abs(dy));
            int steps = largest <= substepMax ? 1 : (int)SimMath.Ceiling(largest / substepMax);
            float sx = dx / steps;
            float sy = dy / steps;
            float fallSpeed = -velocity.Y;
            float riseSpeed = velocity.Y;
            float sideSpeed = SimMath.Abs(velocity.X);

            for (int i = 0; i < steps; i++)
            {
                if (sx != 0f)
                {
                    float fromX = position.X;
                    position.X += sx;
                    if (ResolveX(ref position, sx > 0f ? 1 : -1, half, arena, fromX))
                    {
                        contacts.HitWall = sx > 0f ? 1 : -1;
                        contacts.WallImpactSpeed = sideSpeed;
                        velocity.X = 0f;
                        sx = 0f;
                    }
                }
                if (sy < 0f)
                {
                    position.Y += sy;
                    if (ResolveDown(ref position, half, arena, startBottom, ignoreOneWayBox, out int box))
                    {
                        contacts.Landed = true;
                        contacts.LandedBox = box;
                        contacts.LandedKind = arena.GetBox(box).Kind;
                        contacts.ImpactSpeed = fallSpeed;
                        velocity.Y = 0f;
                        sy = 0f;
                    }
                }
                else if (sy > 0f)
                {
                    position.Y += sy;
                    if (ResolveUp(ref position, half, arena))
                    {
                        contacts.HitCeiling = true;
                        contacts.CeilingImpactSpeed = riseSpeed;
                        velocity.Y = 0f;
                        sy = 0f;
                    }
                }
            }

            if (arena.HasBoundary)
            {
                float bx = arena.BoundaryHalfWidth - half, by = arena.BoundaryHalfHeight - half;
                if (position.X > bx || position.X < -bx)
                {
                    contacts.BoundaryX = position.X > 0f ? 1 : -1;
                    contacts.BoundarySpeedX = SimMath.Max(0f, velocity.X * contacts.BoundaryX);
                    position.X = bx * contacts.BoundaryX;
                }
                if (position.Y > by || position.Y < -by)
                {
                    contacts.BoundaryY = position.Y > 0f ? 1 : -1;
                    contacts.BoundarySpeedY = SimMath.Max(0f, velocity.Y * contacts.BoundaryY);
                    position.Y = by * contacts.BoundaryY;
                }
            }

            // 경기장은 벽(또는 경계)으로 닫혀 있으므로 여기 걸리면 버그 신호다.
            float limitX = arena.HalfWidth - half;
            float limitY = arena.HalfHeight - half;
            if (position.X < -limitX || position.X > limitX || position.Y < -limitY || position.Y > limitY)
            {
                position.X = SimMath.Clamp(position.X, -limitX, limitX);
                position.Y = SimMath.Clamp(position.Y, -limitY, limitY);
                contacts.OutOfBounds = true;
            }
        }

        static bool Overlaps(in Vec2 p, float half, in ArenaBox b) =>
            p.X + half > b.MinX + Epsilon && p.X - half < b.MaxX - Epsilon &&
            p.Y + half > b.MinY + Epsilon && p.Y - half < b.MaxY - Epsilon;

        /// <summary>
        /// fromX에서 direction 쪽으로 움직이다 막는 지형에 들어갔으면 그 면까지 되돌린다.
        /// fromX에서 이미 가로로 겹쳐 있던 상자(위아래로 파고든 상자)는 가로로 막지 않는다. 그런 상자를 가로로 밀어내면
        /// 상자 반대쪽 끝으로 순간이동한다(CP9 QA F-1: 천장 상자 폭 32 → x −16.45).
        /// </summary>
        public static bool ResolveX(ref Vec2 position, int direction, float half, ArenaDef arena, float fromX)
        {
            bool hit = false;
            float limit = 0f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.BlocksAllSides || !Overlaps(position, half, b))
                    continue;
                bool enteredSideways = direction > 0 ? fromX + half <= b.MinX + Epsilon : fromX - half >= b.MaxX - Epsilon;
                if (!enteredSideways)
                    continue;
                float candidate = direction > 0 ? b.MinX - half : b.MaxX + half;
                if (!hit || (direction > 0 ? candidate < limit : candidate > limit))
                    limit = candidate;
                hit = true;
            }
            if (hit)
                position.X = limit;
            return hit;
        }

        /// <summary>
        /// 아래로 움직이다 착지했는지. 단방향 발판은 틱 시작 때 발이 윗면보다 위에 있었고
        /// 무시 중인 발판이 아닐 때만 막는다.
        /// </summary>
        static bool ResolveDown(ref Vec2 position, float half, ArenaDef arena, float startBottom, int ignoreOneWayBox, out int landedBox)
        {
            landedBox = FighterState.NoBox;
            float top = 0f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!Overlaps(position, half, b))
                    continue;
                if (b.Kind == TerrainKind.OneWay && (i == ignoreOneWayBox || startBottom < b.MaxY - Epsilon))
                    continue;
                if (landedBox == FighterState.NoBox || b.MaxY > top)
                {
                    top = b.MaxY;
                    landedBox = i;
                }
            }
            if (landedBox == FighterState.NoBox)
                return false;
            position.Y = top + half;
            return true;
        }

        /// <summary>몸 상자가 막는 지형(단방향 발판 제외)과 겹치는가.</summary>
        public static bool OverlapsSolid(Vec2 position, float half, ArenaDef arena)
        {
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (b.BlocksAllSides && Overlaps(position, half, b))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 겹친 막는 지형 중 아랫면이 aboveY보다 위인 것(천장)이 있으면 그 아랫면 밑으로 내린다.
        /// aboveY는 옮기기 전 몸 중심 높이로, 옆벽처럼 원래 몸 높이에 걸친 상자는 보지 않기 위한 기준이다.
        /// </summary>
        public static bool ResolveCeiling(ref Vec2 position, float half, ArenaDef arena, float aboveY)
        {
            bool hit = false;
            float bottom = 0f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.BlocksAllSides || b.MinY <= aboveY || !Overlaps(position, half, b))
                    continue;
                if (!hit || b.MinY < bottom)
                    bottom = b.MinY;
                hit = true;
            }
            // 경계 튕김 맵에서는 위쪽 경계도 천장이다.
            if (arena.HasBoundary && position.Y + half > arena.BoundaryHalfHeight && (!hit || arena.BoundaryHalfHeight < bottom))
            {
                bottom = arena.BoundaryHalfHeight;
                hit = true;
            }
            if (hit)
                position.Y = bottom - half;
            return hit;
        }

        /// <summary>
        /// 경계 튕김 맵에서 몸을 경계 안으로 들인다(접촉으로 세지 않음). 밀어내기, 잡기·던지기 배치처럼 이동 밖에서
        /// 위치를 정하는 곳에서 쓴다. 이동(Move) 중의 경계 접촉은 Move가 기록한다.
        /// </summary>
        public static void ClampToBoundary(ref Vec2 position, float half, ArenaDef arena)
        {
            if (arena == null || !arena.HasBoundary)
                return;
            float bx = arena.BoundaryHalfWidth - half, by = arena.BoundaryHalfHeight - half;
            position.X = SimMath.Clamp(position.X, -bx, bx);
            position.Y = SimMath.Clamp(position.Y, -by, by);
        }

        static bool ResolveUp(ref Vec2 position, float half, ArenaDef arena)
        {
            bool hit = false;
            float bottom = 0f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.BlocksAllSides || !Overlaps(position, half, b))
                    continue;
                if (!hit || b.MinY < bottom)
                    bottom = b.MinY;
                hit = true;
            }
            if (hit)
                position.Y = bottom - half;
            return hit;
        }

        /// <summary>발 바로 아래(probe 이내)의 지형 상자. 없으면 NoBox. 여럿이면 가장 높은 것.</summary>
        public static int ProbeGround(Vec2 position, float half, ArenaDef arena, float probe, int ignoreOneWayBox)
        {
            float bottom = position.Y - half;
            int found = FighterState.NoBox;
            float top = 0f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (position.X + half <= b.MinX + Epsilon || position.X - half >= b.MaxX - Epsilon)
                    continue;
                float gap = bottom - b.MaxY;
                if (gap < -Epsilon || gap > probe)
                    continue;
                if (b.Kind == TerrainKind.OneWay && i == ignoreOneWayBox)
                    continue;
                if (found == FighterState.NoBox || b.MaxY > top)
                {
                    top = b.MaxY;
                    found = i;
                }
            }
            return found;
        }

        /// <summary>머리 바로 위(probe 이내)에 단단한 지형의 아랫면이 있는지.</summary>
        public static bool ProbeCeiling(Vec2 position, float half, ArenaDef arena, float probe)
        {
            float top = position.Y + half;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.IsSolidBlock)
                    continue;
                if (position.X + half <= b.MinX + Epsilon || position.X - half >= b.MaxX - Epsilon)
                    continue;
                float gap = b.MinY - top;
                if (gap >= -Epsilon && gap <= probe)
                    return true;
            }
            return false;
        }

        /// <summary>옆(probe 이내)에 닿은 단단한 지형 쪽. -1 왼쪽, 1 오른쪽, 양쪽이거나 없으면 0.</summary>
        public static int ProbeWall(Vec2 position, float half, ArenaDef arena, float probe)
        {
            bool left = false, right = false;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.IsSolidBlock)
                    continue;
                if (position.Y + half <= b.MinY + Epsilon || position.Y - half >= b.MaxY - Epsilon)
                    continue;
                float rightGap = b.MinX - (position.X + half);
                if (rightGap >= -Epsilon && rightGap <= probe)
                    right = true;
                float leftGap = position.X - half - b.MaxX;
                if (leftGap >= -Epsilon && leftGap <= probe)
                    left = true;
            }
            return left == right ? 0 : left ? -1 : 1;
        }
    }
}
