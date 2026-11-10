namespace Knockbox.Simulation
{
    /// <summary>연결부는 비고체 피격 대상이다. 상자의 접촉·마찰·벽꽝 경로에 넣지 않는다.</summary>
    public static class RopeConnectors
    {
        const float TieEpsilon = 1e-5f;

        public static void Resolve(ref SimState state, ArenaDef arena, SimConfig config,
            in FighterState previous0, in FighterState previous1, bool frozen0, bool frozen1,
            PropScratch scratch, SimEventBuffer events)
        {
            // 두 플레이어를 같은 스냅샷에서 판정한다. 먼저 처리한 슬롯이 파괴/감속을 독점하지 않는다.
            var f0 = state.Fighter0;
            var f1 = state.Fighter1;
            if (!CanHit(f0, frozen0, config) && !CanHit(f1, frozen1, config)) return;
            float keep0 = 1f, keep1 = 1f;
            for (int i = 0; i < state.Props.Count; i++)
            {
                ref readonly var joint = ref arena.GetJoint(i);
                ref var body = ref PropState.Get(ref state.Props, i);
                if (joint.RopeMode != RopeBreakMode.Connector || body.Broken != 0) continue;
                var from = RopeGeometry.Connector(joint, scratch.StartPosition[i], scratch.StartRotation[i]);
                var to = RopeGeometry.Connector(joint, body.Position, body.Rotation);
                float t0 = Hit(previous0, f0, frozen0, from, to, joint.ConnectorSize, arena, state.Props, config, out bool travel0);
                float t1 = Hit(previous1, f1, frozen1, from, to, joint.ConnectorSize, arena, state.Props, config, out bool travel1);
                float first = SimMath.Min(t0, t1);
                if (first > 1f) continue;
                bool hit0 = t0 <= first + TieEpsilon, hit1 = t1 <= first + TieEpsilon;
                if (hit0 && travel0) keep0 *= 1f - joint.ConnectorSpeedLoss;
                if (hit1 && travel1) keep1 *= 1f - joint.ConnectorSpeedLoss;
                body.Broken = 1;
                body.SleepTicks = 0;
                int actor = hit0 && hit1 ? SimEvent.NoFighter : hit0 ? 0 : 1;
                body.LastToucher = actor;
                var velocity = hit0 && hit1 ? (f0.Velocity + f1.Velocity) * .5f : hit0 ? f0.Velocity : f1.Velocity;
                events.Add(new SimEvent(state.Tick, SimEventType.JointBreak, actor,
                    from + (to - from) * first, SimMath.Normalize(velocity), SimMath.Sqrt(velocity.LengthSquared)));
            }
            // 파괴당 한 번, 현재 진행 방향 유지. 체력·경직·공격 소비·매달린 상자의 운동량은 바꾸지 않는다.
            if (keep0 != 1f) state.Fighter0.Velocity *= keep0;
            if (keep1 != 1f) state.Fighter1.Velocity *= keep1;
        }

        static bool CanHit(in FighterState f, bool frozen, SimConfig config) => !frozen &&
            (f.Action == ActionState.Lunge || f.Action == ActionState.Thrown ||
             f.AttackId == AttackIds.Punch && !f.AttackHasHit && FighterCombat.IsHitboxActive(f, config));

        static float Hit(in FighterState previous, in FighterState now, bool frozen,
            Vec2 connectorFrom, Vec2 connectorTo, float size, ArenaDef arena, in PropState props, SimConfig config, out bool travel)
        {
            travel = now.Action == ActionState.Lunge || now.Action == ActionState.Thrown;
            if (frozen) return 2f;
            float t = 2f, half = size * .5f;
            if (travel)
            {
                // 실제 이동이 없는 정지 상태를 공격으로 보지 않는다. 일반 점프/피격 경직도 제외한다.
                if ((now.Position - previous.Position).LengthSquared < 1e-10f) return 2f;
                float reach = config.HalfBody + half;
                t = Projectiles.SegmentBox(previous.Position - connectorFrom,
                    (now.Position - previous.Position) - (connectorTo - connectorFrom), -reach, -reach, reach, reach);
            }
            else if (now.AttackId == AttackIds.Punch && !now.AttackHasHit && FighterCombat.IsHitboxActive(now, config))
            {
                ref readonly var attack = ref config.GetAttack(now.AttackId);
                for (int c = 0; c < attack.UsedCircles; c++)
                {
                    var circle = attack.GetCircle(c);
                    var end = FighterCombat.CircleCenter(now, attack, circle);
                    var start = previous.Action == now.Action && previous.AttackId == now.AttackId && FighterCombat.IsHitboxActive(previous, config)
                        ? FighterCombat.CircleCenter(previous, attack, circle) : end - (now.Position - previous.Position);
                    t = SimMath.Min(t, SweepCircle(start - connectorFrom,
                        (end - start) - (connectorTo - connectorFrom), new Vec2(half, half), circle.Radius, skipAtLimit: true));
                }
            }
            if (t > 1f) return 2f;
            var at = previous.Position + (now.Position - previous.Position) * t;
            var target = connectorFrom + (connectorTo - connectorFrom) * t;
            return ClearLine(at, target, arena, props) ? t : 2f;
        }

        /// <summary>
        /// 원(반지름 radius)이 원점 중심 상자(반 크기 half)에 처음 닿는 시점 0~1. 안 닿으면 1보다 크다.
        /// 원을 확장 사각형만으로 검사하면 모서리 바깥에서도 맞는다. 두 띠와 네 모서리 원을 합친다.
        /// 연결 상자(ConnectorBodies)도 같은 계산을 쓴다. 움직임이 거의 없을 때 모서리 원을 건너뛰는 기준만
        /// 과거 로프 연결부는 a ≤ 1e-12, 연결 상자는 a &lt; 1e-12로 달라서, 기록 호환을 위해 skipAtLimit로 둘 다 유지한다.
        /// </summary>
        internal static float SweepCircle(Vec2 start, Vec2 delta, Vec2 half, float radius, bool skipAtLimit)
        {
            float t = SimMath.Min(Projectiles.SegmentBox(start, delta, -half.X - radius, -half.Y, half.X + radius, half.Y),
                Projectiles.SegmentBox(start, delta, -half.X, -half.Y - radius, half.X, half.Y + radius));
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var p = start - new Vec2(x * half.X, y * half.Y);
                    float c = p.LengthSquared - radius * radius;
                    if (c <= 0f) return 0f;
                    float a = delta.LengthSquared, b = Vec2.Dot(p, delta), disc = b * b - a * c;
                    if ((skipAtLimit ? a <= 1e-12f : a < 1e-12f) || disc < 0f) continue;
                    float entry = (-b - SimMath.Sqrt(disc)) / a;
                    if (entry >= 0f && entry <= 1f) t = SimMath.Min(t, entry);
                }
            return t;
        }

        internal static bool ClearLine(Vec2 start, Vec2 end, ArenaDef arena, in PropState props, int excludeBody=-1)
        {
            var delta = end - start;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (!box.BlocksAllSides) continue;
                float t;
                if (!box.IsPolygon) t = Projectiles.SegmentBox(start, delta, box.MinX, box.MinY, box.MaxX, box.MaxY);
                else
                {
                    float enter = 0f, exit = 1f;
                    var polygon = box.Polygon;
                    for (int v = 0; v < polygon.Count; v++)
                    {
                        var n = polygon.Normal(v);
                        float distance = Vec2.Dot(n, start - polygon.Point(v)), speed = Vec2.Dot(n, delta);
                        if (SimMath.Abs(speed) < 1e-8f) { if (distance > 0f) { enter = 2f; break; } }
                        else if (speed < 0f) enter = SimMath.Max(enter, -distance / speed);
                        else exit = SimMath.Min(exit, -distance / speed);
                    }
                    t = enter <= exit ? enter : 2f;
                }
                if (t < 1f - TieEpsilon) return false;
            }
            for (int i = 0; i < props.Count; i++)
            {
                ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
                if (i==excludeBody || props.IsDestroyed(i) || def.PlayerPassThrough) continue;
                ref readonly var body = ref PropState.At(in props, i);
                var p = start - body.Position;
                var q = body.Rotation;
                var local = new Vec2(q.X * p.X + q.Y * p.Y, -q.Y * p.X + q.X * p.Y);
                var d = new Vec2(q.X * delta.X + q.Y * delta.Y, -q.Y * delta.X + q.X * delta.Y);
                float t = Projectiles.SegmentBox(local, d, -def.HalfSize.X, -def.HalfSize.Y, def.HalfSize.X, def.HalfSize.Y);
                if (t < 1f - TieEpsilon) return false;
            }
            return true;
        }
    }
}
