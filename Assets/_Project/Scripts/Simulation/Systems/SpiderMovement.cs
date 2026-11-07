namespace Knockbox.Simulation
{
    /// <summary>
    /// CR-004 E(단계 D/E 피드백, "벽에 붙어 다니는 거미"): 파이터마다 중력 방향(GravityDir)을 두고, 그 중력이 아래가 되도록 돌린 좌표계에서
    /// 기존 이동(FighterMovement 핵심)을 그대로 돌린다. 맵은 90° 단위로 돌린 사본(ArenaDef.Rotated)을 쓴다.
    /// 중력: 이동·돌진 접촉으로 면에 닿으면 그 면 쪽(붙기), 땅에 서 있으면 유지, 공중이면 떠 있는 지형 중력장(FieldRadius 안의 가장 가까운 면 쪽)
    /// 또는 기본 아래. 같은 지형의 중력장 안에서 방향이 바뀌면(볼록 모서리) 속도도 함께 돌려 지형을 감아 돈다.
    /// 경직·던져짐·잡힘·KO·박치기 중에는 기본 아래. 벽 점프와 벽 미끄러짐은 쓰지 않는다.
    /// </summary>
    public static class SpiderMovement
    {
        public const sbyte Down = 0;
        public const sbyte Right = 1;
        public const sbyte Up = 2;
        public const sbyte Left = 3;

        /// <summary>월드 → 중력 g의 좌표계(g가 아래).</summary>
        public static Vec2 ToLocal(Vec2 v, int g)
        {
            switch (g)
            {
                case Right: return new Vec2(v.Y, -v.X);
                case Up: return new Vec2(-v.X, -v.Y);
                case Left: return new Vec2(-v.Y, v.X);
                default: return v;
            }
        }

        public static Vec2 ToWorld(Vec2 v, int g)
        {
            switch (g)
            {
                case Right: return new Vec2(-v.Y, v.X);
                case Up: return new Vec2(-v.X, -v.Y);
                case Left: return new Vec2(v.Y, -v.X);
                default: return v;
            }
        }

        /// <summary>월드 단위 방향(축) → 중력 번호.</summary>
        public static sbyte FromWorldDirection(Vec2 d)
        {
            if (SimMath.Abs(d.X) >= SimMath.Abs(d.Y))
                return d.X > 0f ? Right : Left;
            return d.Y > 0f ? Up : Down;
        }

        public static Vec2 Direction(int g) => ToWorld(new Vec2(0f, -1f), g);

        static void ToLocal(ref FighterState f, int g)
        {
            f.Position = ToLocal(f.Position, g);
            f.Velocity = ToLocal(f.Velocity, g);
            f.DashDirection = ToLocal(f.DashDirection, g);
        }

        static void ToWorld(ref FighterState f, int g)
        {
            f.Position = ToWorld(f.Position, g);
            f.Velocity = ToWorld(f.Velocity, g);
            f.DashDirection = ToWorld(f.DashDirection, g);
        }

        /// <summary>바라보는 쪽(월드 좌우)을 국소 이동 입력으로 갱신한다. 세로 방향이면 그대로.</summary>
        static void UpdateFacing(ref FighterState f, int localMoveX, int g)
        {
            if (localMoveX == 0)
                return;
            var world = ToWorld(new Vec2(localMoveX, 0f), g);
            if (world.X > 0.5f) f.Facing = 1;
            else if (world.X < -0.5f) f.Facing = -1;
        }

        static bool KeepsGravityOff(in FighterState f) =>
            f.IsStunned || f.Action == ActionState.Held || f.Action == ActionState.KO || f.Action == ActionState.Lunge;

        /// <summary>
        /// 떠 있는 지형 중력장: 몸 상자에서 가장 가까운 내부 지형까지의 틈이 반경 안이면 그 지형 쪽 축 방향(큰 틈 쪽 축, 같으면 지금 축 유지).
        /// 없으면 −1.
        /// </summary>
        public static sbyte FieldGravity(Vec2 p, float half, ArenaDef arena, float radius, sbyte current, out int piece)
        {
            piece = -1;
            float best = radius * radius;
            sbyte g = -1;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                int pc = arena.PieceOf(i);
                if (pc < 0)
                    continue;
                ref readonly var b = ref arena.GetBox(i);
                float right = (p.X - half) - b.MaxX;   // 몸이 상자 오른쪽에 있으면 양수
                float left = b.MinX - (p.X + half);
                float above = (p.Y - half) - b.MaxY;
                float below = b.MinY - (p.Y + half);
                // 부호 있는 틈: 양수면 그 축으로 떨어져 있다. 더 크게 떨어진 축이 몸과 지형을 가르는 축이다(면에 닿아 있으면 그 면 쪽).
                float sx = SimMath.Max(right, left);
                float sy = SimMath.Max(above, below);
                float dx = SimMath.Max(0f, sx);
                float dy = SimMath.Max(0f, sy);
                float d2 = dx * dx + dy * dy;
                if (d2 > best)
                    continue;
                sbyte horizontal = right >= left ? Left : Right;
                sbyte vertical = above >= below ? Down : Up;
                sbyte candidate;
                if (sx > sy + 1e-4f)
                    candidate = horizontal;
                else if (sy > sx + 1e-4f)
                    candidate = vertical;
                else
                    candidate = current == horizontal ? horizontal : vertical;
                if (d2 < best || g < 0)
                {
                    best = d2;
                    g = candidate;
                    piece = pc;
                }
            }
            return g;
        }

        /// <summary>이동 전: 중력 방향을 정하고, 키 집합으로 국소 이동 방향을 정해 돌린 좌표계에서 이동한다.</summary>
        public static void Integrate(ref FighterState f, in InputFrame input, InputButtons pressed, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events, out MovementScratch scratch)
        {
            ref readonly var body = ref config.Combat.Body;
            float half = config.HalfBody;
            if (f.GravityLock > 0)
                f.GravityLock--;

            // 1) 중력 방향
            sbyte g = f.GravityDir;
            if (KeepsGravityOff(f))
            {
                SetGravity(ref f, Down, -1, false);
            }
            else if (!f.Grounded)
            {
                int piece = -1;
                sbyte field = f.GravityLock > 0 ? (sbyte)-1 : FieldGravity(f.Position, half, arena, body.FieldRadius, g, out piece);
                if (field < 0)
                    SetGravity(ref f, Down, -1, false);
                else
                    SetGravity(ref f, field, piece, piece >= 0 && piece + 1 == f.GravityPiece && field != g);
            }
            g = f.GravityDir;

            // 2) 좌우 키 → 국소 이동(7절: 키 집합이 바뀔 때만 지금 중력 기준으로 다시 정함, 모서리를 지나도 유지)
            byte keys = input.Held(InputButtons.BothHorizontal) ? (byte)3 : input.MoveX < 0 ? (byte)1 : input.MoveX > 0 ? (byte)2 : (byte)0;
            byte previous = f.SurfaceKeys;
            f.SurfaceKeys = keys;
            if (keys == 0)
                f.SurfaceCirc = 0;
            else if (keys != 3 && (keys != previous || f.SurfaceCirc == 0))
                f.SurfaceCirc = (sbyte)((keys == 2 ? 1 : -1) * (g == Down || g == Right ? 1 : -1));
            int localX = keys == 1 || keys == 2 ? f.SurfaceCirc : 0;

            // S: 아래 중력이 아니면 놓고 떨어진다(잠깐 중력장을 무시).
            int localY = 0;
            if (input.MoveY < 0)
            {
                if (g != Down)
                {
                    SetGravity(ref f, Down, -1, false);
                    f.GravityLock = body.SurfaceReleaseTicks;
                    f.Grounded = false;
                    f.GroundBox = FighterState.NoBox;
                    f.SurfaceCirc = 0;
                    g = Down;
                    localX = keys == 1 ? -1 : keys == 2 ? 1 : 0;
                }
                else
                {
                    localY = -1;
                }
            }
            var local = new InputFrame(localX, localY, input.Buttons, input.Aim);

            // 3) 돌린 좌표계에서 기존 이동
            bool wasGrounded = f.Grounded;
            int start = events.Count;
            ToLocal(ref f, g);
            sbyte facing = f.Facing;
            FighterMovement.IntegrateCore(ref f, local, pressed, config, arena.Rotated(g), index, tick, events, out scratch);
            f.Facing = facing;
            ToWorld(ref f, g);
            UpdateFacing(ref f, local.MoveX, g);
            events.RotateSince(start, g);
            // 점프로 떠나면 방향 기억 삭제(다시 붙을 때 키가 눌려 있으면 새로 누른 것).
            if (wasGrounded && (pressed & InputButtons.Jump) != 0 && !f.Grounded)
                f.SurfaceCirc = 0;
        }

        /// <summary>이동 뒤 접촉: 돌린 좌표계에서 기존 접촉 처리 → 이동·돌진 접촉으로 벽·천장(국소)에 닿았으면 그쪽으로 중력을 바꿔 붙는다.</summary>
        public static void ResolveContacts(ref FighterState f, in FighterState other, in MovementScratch scratch, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events)
        {
            sbyte g = f.GravityDir;
            var rotated = arena.Rotated(g);
            var otherLocal = other;
            otherLocal.Position = ToLocal(other.Position, g);
            int start = events.Count;
            ToLocal(ref f, g);
            sbyte facing = f.Facing;
            FighterMovement.ResolveContactsCore(ref f, otherLocal, scratch, config, rotated, index, tick, events);
            f.Facing = facing;
            // 벽 점프·벽 미끄러짐은 쓰지 않는다.
            f.WallSide = 0;
            f.TicksSinceWall = FighterState.TimerCap;
            if (f.Action == ActionState.WallSlide)
                f.SetAction(ActionState.Airborne);

            // 붙기: 이동 상태(돌진이 지형에 멈춘 직후 포함)에서 국소 벽·천장에 닿음. 피격 충돌은 이동 상태가 아니라 붙지 않는다.
            ref readonly var c = ref scratch.Contacts;
            Vec2 stick = Vec2.Zero;
            if (f.InMovementState && f.GravityLock == 0 && scratch.MoveY >= 0)
            {
                if (c.HitWall != 0)
                    stick = new Vec2(c.HitWall, 0f);
                else if (c.HitCeiling)
                    stick = new Vec2(0f, 1f);
            }
            ToWorld(ref f, g);
            events.RotateSince(start, g);
            if (stick.X == 0f && stick.Y == 0f)
            {
                // 지형 조각 위에 서 있으면 기억해 둔다(그 조각의 중력장 안에서 모서리를 감아 돌 때 속도를 함께 돌림).
                if (f.Grounded && f.GroundBox >= 0)
                    f.GravityPiece = arena.PieceOf(f.GroundBox) + 1;
                return;
            }

            var worldDir = ToWorld(stick, g);
            sbyte ng = FromWorldDirection(worldDir);
            SetGravity(ref f, ng, -1, false);
            f.Velocity = Vec2.Zero;
            f.DashTicksLeft = 0;
            var localPos = ToLocal(f.Position, ng);
            int ground = TerrainCollision.ProbeGround(localPos, config.HalfBody, arena.Rotated(ng), config.Movement.GroundProbeDistance, FighterState.NoBox);
            f.Grounded = ground != FighterState.NoBox;
            f.GroundBox = ground;
            f.GravityPiece = ground >= 0 ? arena.PieceOf(ground) + 1 : 0;
            f.AirJumpsLeft = config.Movement.AirJumps;
            f.DashCharges = config.Movement.DashCharges;
            f.JumpCutEligible = false;
            if (f.Grounded)
            {
                f.TicksSinceGrounded = 0;
                f.SetAction(ActionState.Idle);
            }
            events.Add(new SimEvent(tick, SimEventType.SurfaceAttach, index, f.Position, -worldDir, f.GravityPiece == 0 ? 1f : 0f));
        }

        /// <summary>중력 방향을 바꾼다. wrap이면 국소 속도를 그대로 두도록 속도를 함께 돌린다(볼록 모서리 감아 돌기).</summary>
        static void SetGravity(ref FighterState f, sbyte g, int piece, bool wrap)
        {
            if (wrap && g != f.GravityDir)
                f.Velocity = ToWorld(ToLocal(f.Velocity, f.GravityDir), g);
            f.GravityDir = g;
            f.GravityPiece = piece + 1;
        }
    }
}
