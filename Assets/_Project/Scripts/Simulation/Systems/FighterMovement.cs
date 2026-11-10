namespace Knockbox.Simulation
{
    /// <summary>한 틱 동안 이동 단계 사이에 넘기는 값. 상태가 아니다.</summary>
    public struct MovementScratch
    {
        public MoveContacts Contacts;
        /// <summary>벽 점프 입력 잠금을 적용한 뒤의 좌우 입력.</summary>
        public int MoveX;
        public int MoveY;
        public bool WasGrounded;
        /// <summary>이번 틱에 벽을 기어올랐다(CR-004).</summary>
        public bool Climbing;
    }

    /// <summary>
    /// 이동(02-movement.md). 한 틱은 두 단계다.
    /// Integrate: 입력 → 속도 → 지형 충돌 이동. 두 파이터 모두 같은 이전 상태에서 따로 계산한다.
    /// (그 사이에 FighterPush가 몸 겹침을 푼다.)
    /// ResolveContacts: 접지·벽 접촉 판정, 착지·튕김 처리, 착지 틱의 버퍼 점프, 행동 상태와 이벤트.
    /// 전투 상태(공격, 가드, 경직)에서는 이동 규칙 대신 IntegrateCombatState가 속도를 정하고,
    /// 피격 경직 중 지형에 부딪히면 벽꽝·튕김(03-combat.md 3절)을 처리한다.
    /// </summary>
    public static class FighterMovement
    {
        /// <param name="pressed">이번 틱에 새로 눌린 버튼(히트스탑 동안 눌러 둔 버튼 포함). FighterCombat.BeginTick이 계산한다.</param>
        public static void Integrate(ref FighterState f, in InputFrame input, InputButtons pressed, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events, out MovementScratch scratch)
        {
            // CR-004 E: 거미식 자유 표면 이동(중력 방향을 돌린 좌표계에서 아래 핵심을 돌린다).
            if (config.Combat.Body.SurfaceMode == 2)
                SpiderMovement.Integrate(ref f, input, pressed, config, arena, index, tick, events, out scratch);
            else
                IntegrateCore(ref f, input, pressed, config, arena, index, tick, events, out scratch);
        }

        public static void IntegrateCore(ref FighterState f, in InputFrame input, InputButtons pressed, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events, out MovementScratch scratch)
        {
            ref readonly var m = ref config.Movement;
            bool jumpHeld = input.Held(InputButtons.Jump);
            int moveX = input.MoveX;
            int moveY = input.MoveY;

            scratch = default;
            scratch.WasGrounded = f.Grounded;

            // 타이머
            if ((pressed & InputButtons.Jump) != 0)
                f.JumpBuffer = m.JumpBufferTicks;
            else if (f.JumpBuffer > 0)
                f.JumpBuffer--;
            if (f.DashCooldown > 0)
                f.DashCooldown--;
            if (f.WallJumpLockTicks > 0)
            {
                // 벽 점프 직후에는 떠난 벽 쪽으로 되돌아가는 입력만 무시한다.
                if (moveX == f.WallJumpLockDirection)
                    moveX = 0;
                f.WallJumpLockTicks--;
            }
            if (f.DropThroughTicks > 0 && --f.DropThroughTicks == 0)
                f.DropThroughBox = FighterState.NoBox;

            scratch.MoveX = moveX;
            scratch.MoveY = moveY;

            if (config.Combat.Body.SurfaceMode == 1)
            {
                SurfacePrepare(ref f, input, config, arena);
                if (f.Surface != FighterState.SurfaceNone && f.InMovementState
                    && IntegrateAttached(ref f, input, pressed, config, arena, ref scratch))
                    return;
            }

            // 모서리 오르기 중: 정해진 경로를 따라 움직인다(입력·중력·충돌 없음, 경로는 시작할 때 비어 있음을 확인했다).
            if (f.MantleTicks > 0)
            {
                if (f.Action != ActionState.Mantle)
                    f.MantleTicks = 0;
                else
                {
                    AdvanceMantle(ref f, config);
                    return;
                }
            }

            if (!f.InMovementState)
            {
                IntegrateCombatState(ref f, config, moveX, moveY, jumpHeld, (pressed & InputButtons.Jump) != 0);
                TerrainCollision.Move(ref f.Position, ref f.Velocity, config.HalfBody, arena, m.SubstepMaxDistance, f.DropThroughBox, ref scratch.Contacts);
                return;
            }

            bool dashing = f.Action == ActionState.Dash && f.DashTicksLeft > 0;

            // 대시 시작
            if (!config.Combat.Body.SeparateChargeInput && !config.Combat.Body.CombinedAttackInput
                && !dashing && (pressed & InputButtons.Dash) != 0 && f.DashCharges > 0 && f.DashCooldown == 0)
            {
                var direction = moveX == 0 && moveY == 0 ? new Vec2(f.Facing, 0f) : SimMath.Normalize(new Vec2(moveX, moveY));
                f.DashDirection = direction;
                // 대시를 시작하는 틱은 아직 조작 가능한 상태이므로 대시 쪽을 바라본다. 대시 중에는 바꾸지 않는다.
                if (moveX != 0)
                    f.Facing = (sbyte)moveX;
                f.DashTicksLeft = m.DashTicks;
                f.DashCharges--;
                f.DashCooldown = m.DashCooldownTicks;
                f.DashFromGround = f.Grounded;
                f.JumpCutEligible = false;
                f.SetAction(ActionState.Dash);
                dashing = true;
                events.Add(new SimEvent(tick, SimEventType.Dash, index, f.Position, direction, m.DashSpeed));
            }

            if (dashing)
            {
                int elapsed = m.DashTicks - f.DashTicksLeft;
                bool cancel = m.DashJumpCancelEnabled && f.DashFromGround && f.Grounded
                              && elapsed >= m.DashJumpCancelMinTicks && f.JumpBuffer > 0;
                if (cancel)
                {
                    // 대시 점프 캔슬: 수평 속도는 그대로 두고 점프로 바꾼다.
                    f.DashTicksLeft = 0;
                    dashing = false;
                    f.Velocity = new Vec2(f.DashDirection.X * m.DashSpeed, 0f);
                    GroundJump(ref f, m, index, tick, events);
                    ApplyGravity(ref f, m, jumpHeld, moveX, moveY);
                }
                else
                {
                    f.Velocity = f.DashDirection * m.DashSpeed;
                }
            }
            else
            {
                ref readonly var body = ref config.Combat.Body;
                // 벽 기어오르기(CR-004): 벽에 닿은 채 벽 쪽 + W, 이번 틱에 점프하지 않으면 위로 일정 속도. 바닥에서 시작해도 된다.
                bool climb = body.ClimbSpeed > 0f && f.WallSide != 0 && moveX == f.WallSide && moveY > 0 && f.JumpBuffer <= 0;
                bool apex = !f.Grounded && SimMath.Abs(f.Velocity.Y) < m.ApexSpeedThreshold && jumpHeld;
                ApplyHorizontal(ref f, m, moveX, m.MaxRunSpeed, apex ? m.ApexAirAccelMultiplier : 1f, 1f);
                ResolveJumpRequest(ref f, m, arena, moveY, index, tick, events);
                ApplyGravity(ref f, m, jumpHeld, moveX, moveY);
                if (climb)
                {
                    f.Velocity = new Vec2(f.WallSide * 1f, body.ClimbSpeed);
                    f.Grounded = false;
                    f.GroundBox = FighterState.NoBox;
                    f.JumpCutEligible = false;
                    scratch.Climbing = true;
                }
                // 모서리 잡기: 공중에서 벽 쪽을 누르고 있고 그 지형 윗면이 머리 높이쯤이면 끌어올라 선다.
                if (body.ClimbSpeed > 0f && !f.Grounded && moveX != 0 && TryStartMantle(ref f, moveX, config, arena, index, tick, events))
                    return;
            }

            TerrainCollision.Move(ref f.Position, ref f.Velocity, config.HalfBody, arena, m.SubstepMaxDistance, f.DropThroughBox, ref scratch.Contacts);

            if (dashing && --f.DashTicksLeft <= 0)
            {
                f.DashTicksLeft = 0;
                f.Velocity *= m.DashEndSpeedRetention;
            }
        }

        /// <summary>좌우 입력으로 maxSpeed까지 가속하거나 감속한다. 배율은 정점(1.2)과 공격 중 공중 조작(0.5)에 쓴다.</summary>
        static void ApplyHorizontal(ref FighterState f, in MovementParams m, int moveX, float maxSpeed, float accelMultiplier, float decelMultiplier)
        {
            const float dt = SimConfig.Dt;
            float vx = f.Velocity.X;
            float decel = (f.Grounded ? m.GroundDecel : m.AirDecel) * decelMultiplier;
            if (moveX != 0)
            {
                int velocitySign = SimMath.Sign(vx);
                if (velocitySign == moveX && SimMath.Abs(vx) > maxSpeed)
                {
                    // 최대 속도를 넘는 관성(대시 뒤, 대시 점프)은 같은 방향 입력이면 감속률로만 줄인다.
                    vx = SimMath.MoveTowards(vx, moveX * maxSpeed, decel * dt);
                }
                else
                {
                    float accel = f.Grounded ? m.GroundAccel : m.AirAccel;
                    accel *= accelMultiplier;
                    if (velocitySign != 0 && velocitySign != moveX)
                        accel *= m.TurnAccelMultiplier;
                    vx = SimMath.MoveTowards(vx, moveX * maxSpeed, accel * dt);
                }
            }
            else
            {
                vx = SimMath.MoveTowards(vx, 0f, decel * dt);
            }
            f.Velocity.X = vx;
        }

        /// <summary>
        /// 전투 상태의 속도. 가드: 최대 속도 GuardMaxSpeed, 점프·대시 불가. 공격: 지상은 감속만, 공중은 조작 × 0.5.
        /// 충전(모드 A): 최대 속도 3, 공중 낙하 최대 4. 돌진: 조작 없음, 공중에서 중력 × 0.3.
        /// 피격 경직: 조작 불가, 중력 정상, 공중 감속 0(날아가는 관성 유지). 그 밖의 경직: 조작 없이 감속.
        /// </summary>
        static void IntegrateCombatState(ref FighterState f, SimConfig config, int moveX, int moveY, bool jumpHeld, bool jumpPressed)
        {
            const float dt = SimConfig.Dt;
            ref readonly var m = ref config.Movement;
            ref readonly var c = ref config.Combat;
            // 표면에 붙은 채 공격·잡기 중이면 그 자리에 멈춘다(CR-004 7절: 행동이 끝나면 다시 이어간다).
            if (f.Surface != FighterState.SurfaceNone)
            {
                f.Velocity = Vec2.Zero;
                return;
            }
            switch (f.Action)
            {
                case ActionState.Guard:
                    ApplyHorizontal(ref f, m, moveX, c.GuardMaxSpeed, 1f, 1f);
                    ApplyGravity(ref f, m, false, 0, 0);
                    break;
                case ActionState.Attack:
                case ActionState.Grab:
                case ActionState.Shoot:
                    if (f.Grounded)
                        f.Velocity.X = SimMath.MoveTowards(f.Velocity.X, 0f, m.GroundDecel * dt);
                    else
                        ApplyHorizontal(ref f, m, moveX, m.MaxRunSpeed, c.AttackAirControlMultiplier, c.AttackAirControlMultiplier);
                    ApplyGravity(ref f, m, jumpHeld, 0, moveY);
                    break;
                case ActionState.Thrown when c.Body.Enabled:
                    // CR-004: 던져진 몸은 낮은 중력으로 일직선에 가깝게 멀리 날아간다. 바닥을 따라 던져져도 던져진 동안은 마찰 없이 미끄러진다.
                    if (!f.Grounded)
                        f.Velocity.Y = SimMath.Max(f.Velocity.Y - m.RiseGravity * m.FallGravityMultiplier * c.Body.ThrownGravityScale * dt, -m.MaxFallSpeed);
                    break;
                case ActionState.Hitstun:
                case ActionState.Thrown:
                    if (f.Grounded)
                        f.Velocity.X = SimMath.MoveTowards(f.Velocity.X, 0f, m.GroundDecel * dt);
                    ApplyGravity(ref f, m, false, 0, 0);
                    break;
                case ActionState.Holding when f.Form == FighterForm.Body:
                    // 몸 폼(5.2): 잡은 쪽은 느리게 걷고, 땅에서 점프할 수 있고(단계 C 피드백), 중력을 받는다. 잡힌 쪽은 상호작용 판정에서 따라온다.
                    ApplyHorizontal(ref f, m, moveX, c.Body.HoldMoveSpeed, 1f, 1f);
                    if (jumpPressed && f.Grounded && c.Body.HoldJumpSpeed > 0f)
                    {
                        f.Velocity.Y = c.Body.HoldJumpSpeed;
                        f.Grounded = false;
                        f.GroundBox = FighterState.NoBox;
                    }
                    ApplyGravity(ref f, m, jumpHeld, 0, 0);
                    break;
                case ActionState.Holding:
                case ActionState.Held:
                    // 붙잡기 동안 둘 다 움직이지 않는다(공중이어도 떠 있음).
                    f.Velocity = Vec2.Zero;
                    break;
                case ActionState.Charge:
                    ApplyHorizontal(ref f, m, moveX, c.Charge.ChargeMoveMaxSpeed, 1f, 1f);
                    ApplyGravity(ref f, m, jumpHeld, 0, 0);
                    if (!f.Grounded && f.Velocity.Y < -c.Charge.ChargeFallMaxSpeed)
                        f.Velocity.Y = -c.Charge.ChargeFallMaxSpeed;
                    break;
                case ActionState.Lunge:
                    if (!f.Grounded)
                        f.Velocity.Y -= m.RiseGravity * c.Charge.LungeGravityMultiplier * dt;
                    if (c.Charge.LungeDrag > 0f)
                    {
                        float speed = SimMath.Length(f.Velocity);
                        if (speed > SimMath.Epsilon)
                            f.Velocity = f.Velocity * (SimMath.Max(0f, speed - c.Charge.LungeDrag * dt) / speed);
                    }
                    break;
                default:
                    f.Velocity.X = SimMath.MoveTowards(f.Velocity.X, 0f, (f.Grounded ? m.GroundDecel : m.AirDecel) * dt);
                    ApplyGravity(ref f, m, false, 0, 0);
                    break;
            }
        }

        // 표면 이동(CR-004 7절) -------------------------------------------------------------

        /// <summary>좌우 키 집합: 1 A, 2 D, 3 둘 다, 0 없음.</summary>
        static byte HorizontalKeys(in InputFrame input) =>
            input.Held(InputButtons.BothHorizontal) ? (byte)3 : input.MoveX < 0 ? (byte)1 : input.MoveX > 0 ? (byte)2 : (byte)0;

        static bool OuterWall(Vec2 p, float half, ArenaDef arena, int side) =>
            side > 0 ? p.X + half >= arena.HalfWidth - 1f - 0.01f : p.X - half <= -(arena.HalfWidth - 1f) + 0.01f;

        static bool OuterCeiling(Vec2 p, float half, ArenaDef arena) => p.Y + half >= arena.HalfHeight - 1f - 0.01f;

        static bool OuterFloor(Vec2 p, float half, ArenaDef arena) => p.Y - half <= -(arena.HalfHeight - 1f) + 0.01f;

        static int WallSideOf(sbyte surface) => surface == FighterState.SurfaceWallRight ? 1 : surface == FighterState.SurfaceWallLeft ? -1 : 0;

        /// <summary>새로 누른 키(D = +1, A = -1)를 표면 기준 도는 방향으로(7절 매핑 표).</summary>
        public static sbyte DecideCirculation(sbyte surface, int key)
        {
            switch (surface)
            {
                case FighterState.SurfaceFloor:
                case FighterState.SurfaceWallRight:
                    return (sbyte)key;
                default:
                    return (sbyte)-key;
            }
        }

        /// <summary>표면 위 도는 방향 → 단위 이동 방향.</summary>
        public static Vec2 AlongSurface(sbyte surface, int circulation)
        {
            switch (surface)
            {
                case FighterState.SurfaceFloor: return new Vec2(circulation, 0f);
                case FighterState.SurfaceWallRight: return new Vec2(0f, circulation);
                case FighterState.SurfaceCeiling: return new Vec2(-circulation, 0f);
                case FighterState.SurfaceWallLeft: return new Vec2(0f, -circulation);
                default: return Vec2.Zero;
            }
        }

        /// <summary>떨어진다. 방향 기억도 지운다(점프, 박치기, S, 피격·던져짐, 잡힘).</summary>
        static void Detach(ref FighterState f)
        {
            f.Surface = FighterState.SurfaceNone;
            f.SurfaceOuter = false;
            f.SurfaceCirc = 0;
        }

        /// <summary>
        /// 매 틱 이동 전: 붙어 있을 수 없는 행동(경직, 던져짐, 잡힘, 박치기 등)이면 떨어뜨리고, 키 집합으로 방향 기억을 갱신한다.
        /// 공격·충전·잡기·잡은 상태는 붙은 채 잠시 멈추며 기억을 유지한다.
        /// </summary>
        static void SurfacePrepare(ref FighterState f, in InputFrame input, SimConfig config, ArenaDef arena)
        {
            bool keepable = f.InMovementState || f.Action == ActionState.Attack || f.Action == ActionState.Charge
                            || f.Action == ActionState.Grab || f.Action == ActionState.Holding;
            if (f.Surface != FighterState.SurfaceNone && !keepable)
            {
                Detach(ref f);
                // 회복한 뒤 키가 눌려 있어도 새로 누른 것으로 처리한다.
                f.SurfaceKeys = 0;
            }
            byte keys = HorizontalKeys(input);
            if (config.Combat.Body.SurfaceMode < 2)
            {
                // D: 키 집합만 기록한다(달리기 없음).
                f.SurfaceKeys = keys;
                return;
            }
            sbyte decide = FighterState.SurfaceNone;
            if (f.Surface != FighterState.SurfaceNone)
            {
                if (f.SurfaceOuter)
                    decide = f.Surface;
            }
            else if (f.Grounded && OuterFloor(f.Position, config.HalfBody, arena))
            {
                decide = FighterState.SurfaceFloor;
            }
            else if (!f.Grounded)
            {
                // 공중(점프 등으로 떨어짐): A/D는 화면 좌우이고 기억은 없다.
                f.SurfaceCirc = 0;
            }
            UpdateCirculation(ref f, keys, decide);
        }

        static void UpdateCirculation(ref FighterState f, byte keys, sbyte decide)
        {
            byte previous = f.SurfaceKeys;
            f.SurfaceKeys = keys;
            if (keys == 0)
            {
                // 키를 실제로 뗌: 정지하고 기억 삭제.
                f.SurfaceCirc = 0;
                return;
            }
            // 양쪽 동시: 중립 정지, 기억 유지. 방향을 정할 표면이 없으면(공중·내부 지형) 그대로.
            if (keys == 3 || decide == FighterState.SurfaceNone)
                return;
            if (keys != previous || f.SurfaceCirc == 0)
                f.SurfaceCirc = DecideCirculation(decide, keys == 2 ? 1 : -1);
        }

        /// <summary>
        /// 붙은 채 이동 상태: 떼기(점프, S, 대시)를 처리하고, 아니면 E 모드 외곽에서 기억한 방향으로 달리거나 멈춘다.
        /// 표면 쪽으로 살짝 밀어 접촉을 유지한다. 붙은 채 이동을 마쳤으면 true(떨어졌으면 false: 일반 이동이 이어진다).
        /// </summary>
        static bool IntegrateAttached(ref FighterState f, in InputFrame input, InputButtons pressed, SimConfig config, ArenaDef arena, ref MovementScratch scratch)
        {
            ref readonly var m = ref config.Movement;
            ref readonly var body = ref config.Combat.Body;
            sbyte surface = f.Surface;
            if (!body.SeparateChargeInput && !body.CombinedAttackInput
                && (pressed & InputButtons.Dash) != 0 && f.DashCharges > 0 && f.DashCooldown == 0)
            {
                Detach(ref f);
                return false;
            }
            if (f.JumpBuffer > 0)
            {
                Detach(ref f);
                if (surface == FighterState.SurfaceCeiling)
                {
                    // 천장에서 점프: 아래로 떨어진다(벽은 일반 이동의 벽 점프가 처리한다).
                    f.JumpBuffer = 0;
                    f.Velocity = new Vec2(0f, -m.JumpSpeed * 0.25f);
                }
                return false;
            }
            if (input.MoveY < 0)
            {
                // S: 놓고 떨어진다. 벽 쪽 입력을 잠깐 무시해 바로 다시 붙지 않게 한다.
                int side = WallSideOf(surface);
                Detach(ref f);
                f.Velocity = Vec2.Zero;
                if (side != 0)
                {
                    f.WallJumpLockTicks = m.WallJumpInputLockTicks;
                    f.WallJumpLockDirection = (sbyte)side;
                }
                return false;
            }

            var velocity = Vec2.Zero;
            bool running = body.SurfaceMode >= 2 && f.SurfaceOuter && f.SurfaceCirc != 0 && (f.SurfaceKeys == 1 || f.SurfaceKeys == 2);
            if (running)
                velocity = AlongSurface(surface, f.SurfaceCirc) * m.MaxRunSpeed;
            float push = body.SurfaceHoldPush;
            if (surface == FighterState.SurfaceCeiling)
                velocity.Y += push;
            else
                velocity.X += WallSideOf(surface) * push;
            f.Velocity = velocity;
            if (running)
            {
                var along = AlongSurface(surface, f.SurfaceCirc);
                if (along.X > 0f) f.Facing = 1;
                else if (along.X < 0f) f.Facing = -1;
            }
            TerrainCollision.Move(ref f.Position, ref f.Velocity, config.HalfBody, arena, m.SubstepMaxDistance, f.DropThroughBox, ref scratch.Contacts);
            return true;
        }

        /// <summary>
        /// 이동 뒤 접촉: 붙어 있으면 외곽 모서리 전환(E)·바닥 도착·떨어짐을 처리하고, 붙어 있지 않으면 이동·돌진 접촉으로 붙는다.
        /// 붙어 있는 상태로 끝나면 true(접지·벽 미끄러짐 처리를 건너뛴다).
        /// </summary>
        static bool SurfaceContacts(ref FighterState f, in MovementScratch scratch, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events)
        {
            ref readonly var m = ref config.Movement;
            ref readonly var contacts = ref scratch.Contacts;
            float half = config.HalfBody;
            bool run = config.Combat.Body.SurfaceMode >= 2;

            if (f.Surface != FighterState.SurfaceNone)
            {
                if (run && f.SurfaceOuter && f.InMovementState)
                {
                    // 외곽 오목 모서리: 방향은 그대로, 표면만 바꾼다.
                    if (f.Surface != FighterState.SurfaceCeiling && contacts.HitCeiling && OuterCeiling(f.Position, half, arena))
                        f.Surface = FighterState.SurfaceCeiling;
                    else if (f.Surface == FighterState.SurfaceCeiling && contacts.HitWall != 0 && OuterWall(f.Position, half, arena, contacts.HitWall))
                        f.Surface = contacts.HitWall > 0 ? FighterState.SurfaceWallRight : FighterState.SurfaceWallLeft;
                    else if (f.Surface != FighterState.SurfaceCeiling && contacts.Landed)
                    {
                        // 벽을 타고 내려와 바닥에 닿음: 서기로 돌아가고 기억은 그대로(바닥 매핑과 같은 방향).
                        f.Surface = FighterState.SurfaceNone;
                        f.SurfaceOuter = false;
                        return false;
                    }
                }
                bool touching = f.Surface == FighterState.SurfaceCeiling
                    ? TerrainCollision.ProbeCeiling(f.Position, half, arena, m.GroundProbeDistance)
                    : TerrainCollision.ProbeWall(f.Position, half, arena, m.GroundProbeDistance) == WallSideOf(f.Surface);
                if (!touching)
                {
                    Detach(ref f);
                    return false;
                }
                FinishAttached(ref f, config);
                return true;
            }

            // 붙기: 이동 접촉(이동 상태)과 돌진 접촉(돌진이 지형에 닿아 끝난 직후)만. 피격 충돌은 이동 상태가 아니라서 붙지 않는다.
            // S를 누르고 있으면 붙지 않는다(놓고 떨어지는 중).
            if (!f.InMovementState || scratch.MoveY < 0)
                return false;
            sbyte surface;
            bool outer;
            if (contacts.HitWall != 0)
            {
                outer = OuterWall(f.Position, half, arena, contacts.HitWall);
                // 바닥에 서서 벽을 미는 것은 붙기가 아니다. E 모드의 외곽 벽만 바닥 → 벽 모서리 전환으로 붙는다.
                if (scratch.WasGrounded && !(run && outer && OuterFloor(f.Position, half, arena)))
                    return false;
                surface = contacts.HitWall > 0 ? FighterState.SurfaceWallRight : FighterState.SurfaceWallLeft;
            }
            else if (contacts.HitCeiling)
            {
                outer = OuterCeiling(f.Position, half, arena);
                surface = FighterState.SurfaceCeiling;
            }
            else
            {
                return false;
            }
            f.Surface = surface;
            f.SurfaceOuter = outer;
            f.Velocity = Vec2.Zero;
            f.DashTicksLeft = 0;
            if (run && outer && (f.SurfaceKeys == 1 || f.SurfaceKeys == 2) && f.SurfaceCirc == 0)
                f.SurfaceCirc = DecideCirculation(surface, f.SurfaceKeys == 2 ? 1 : -1);
            int side = WallSideOf(surface);
            var normal = side != 0 ? new Vec2(-side, 0f) : new Vec2(0f, -1f);
            events.Add(new SimEvent(tick, SimEventType.SurfaceAttach, index, f.Position, normal, outer ? 1f : 0f));
            FinishAttached(ref f, config);
            return true;
        }

        static void FinishAttached(ref FighterState f, SimConfig config)
        {
            ref readonly var m = ref config.Movement;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            f.TicksSinceGrounded = SimMath.Min(f.TicksSinceGrounded + 1, FighterState.TimerCap);
            int side = WallSideOf(f.Surface);
            f.WallSide = (sbyte)side;
            if (side != 0)
            {
                f.TicksSinceWall = 0;
                f.LastWallSide = (sbyte)side;
            }
            f.AirJumpsLeft = m.AirJumps;
            f.DashCharges = m.DashCharges;
            f.JumpCutEligible = false;
            if (f.InMovementState)
                f.SetAction(ActionState.WallSlide);
        }

        // 벽 기어오르기·모서리 오르기(CR-004 D/E 피드백 2) ------------------------------------------

        /// <summary>
        /// side 쪽에 바로 맞닿은 고체 지형 중 윗면이 발보다 높고 머리 위 LedgeReach 안에 있는 것을 찾아, 그 윗면에 설 자리가 비어 있으면
        /// 모서리 오르기를 시작한다. 경로: 지금 x에서 윗면 높이까지 올라간 뒤 윗면 안쪽으로.
        /// </summary>
        static bool TryStartMantle(ref FighterState f, int side, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events)
        {
            float half = config.HalfBody;
            float probe = config.Movement.GroundProbeDistance;
            ref readonly var body = ref config.Combat.Body;
            var p = f.Position;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                if (!b.IsSolidBlock)
                    continue;
                float gap = side > 0 ? b.MinX - (p.X + half) : (p.X - half) - b.MaxX;
                if (!b.IsPolygon && (gap < -TerrainCollision.Epsilon || gap > probe))
                    continue;
                float top = b.MaxY;
                float edge=side>0?b.MinX:b.MaxX;
                if(b.IsPolygon)
                {
                    if(!b.Polygon.Slice(p,new Vec2(half,half),false,out float left,out float right,out _,out _))continue;
                    edge=side>0?left+half:right-half;
                    gap=side>0?left-p.X:p.X-right;
                    if(gap< -TerrainCollision.Epsilon||gap>probe)continue;
                    var target=new Vec2(side>0?edge+half+.05f:edge-half-.05f,0);
                    if(!b.Polygon.Slice(target,new Vec2(half,half),true,out _,out float high,out _,out var n)||n.Y<TerrainCollision.WalkableNormalY)continue;
                    top=high-half;
                }
                if (top <= p.Y - half + 0.1f || top > p.Y + half + body.LedgeReach)
                    continue;
                if (p.Y + half <= b.MinY)
                    continue;
                var up = new Vec2(p.X, top + half + 0.02f);
                var to = new Vec2(side > 0 ? edge + half + 0.05f : edge - half - 0.05f, up.Y);
                if (TerrainCollision.OverlapsSolid(up, half - 0.01f, arena) || TerrainCollision.OverlapsSolid(to, half - 0.01f, arena))
                    continue;
                f.MantleFrom = p;
                f.MantleTo = to;
                f.MantleTicks = SimMath.Max(2, body.MantleTicks);
                f.Velocity = Vec2.Zero;
                f.DashTicksLeft = 0;
                f.Facing = (sbyte)side;
                f.RestartAction(ActionState.Mantle);
                events.Add(new SimEvent(tick, SimEventType.WallSlideStart, index, p, new Vec2(side, 1f), 0f));
                return true;
            }
            return false;
        }

        static void AdvanceMantle(ref FighterState f, SimConfig config)
        {
            int total = SimMath.Max(2, config.Combat.Body.MantleTicks);
            f.MantleTicks--;
            float t = 1f - f.MantleTicks / (float)total;
            const float rise = 0.6f;
            if (t <= rise)
            {
                float k = t / rise;
                f.Position = new Vec2(f.MantleFrom.X, f.MantleFrom.Y + (f.MantleTo.Y - f.MantleFrom.Y) * k);
            }
            else
            {
                float k = (t - rise) / (1f - rise);
                f.Position = new Vec2(f.MantleFrom.X + (f.MantleTo.X - f.MantleFrom.X) * k, f.MantleTo.Y);
            }
            f.Velocity = Vec2.Zero;
            if (f.MantleTicks == 0)
            {
                f.Position = f.MantleTo;
                f.RestartAction(ActionState.Idle);
            }
        }

        /// <summary>점프 버퍼가 있으면 우선순위대로 처리한다: 발판 하강 → 땅·코요테 점프 → 벽 점프 → 공중 점프.</summary>
        static void ResolveJumpRequest(ref FighterState f, in MovementParams m, ArenaDef arena, int moveY, int index, int tick, SimEventBuffer events)
        {
            if (f.JumpBuffer <= 0)
                return;

            if (TryDropThrough(ref f, m, arena, moveY))
                return;
            if (f.Grounded || f.TicksSinceGrounded <= m.CoyoteTicks)
            {
                GroundJump(ref f, m, index, tick, events);
                return;
            }
            if (f.WallSide != 0 || f.TicksSinceWall <= m.WallCoyoteTicks)
            {
                WallJump(ref f, m, f.WallSide != 0 ? f.WallSide : f.LastWallSide, index, tick, events);
                return;
            }
            if (f.AirJumpsLeft > 0)
            {
                f.AirJumpsLeft--;
                f.Velocity.Y = m.AirJumpSpeed;
                f.JumpBuffer = 0;
                f.JumpCutEligible = true;
                events.Add(new SimEvent(tick, SimEventType.AirJump, index, f.Position, new Vec2(0f, 1f), m.AirJumpSpeed));
            }
        }

        /// <summary>단방향 발판 위에서 아래 + 점프면 그 발판을 잠시 무시하고 떨어진다.</summary>
        static bool TryDropThrough(ref FighterState f, in MovementParams m, ArenaDef arena, int moveY)
        {
            if (moveY >= 0 || !f.Grounded || f.GroundBox < 0 || arena.GetBox(f.GroundBox).Kind != TerrainKind.OneWay)
                return false;
            f.DropThroughBox = f.GroundBox;
            f.DropThroughTicks = m.DropThroughTicks;
            f.JumpBuffer = 0;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            f.TicksSinceGrounded = m.CoyoteTicks + 1;
            return true;
        }

        static void GroundJump(ref FighterState f, in MovementParams m, int index, int tick, SimEventBuffer events)
        {
            f.Velocity.Y = m.JumpSpeed;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            f.TicksSinceGrounded = m.CoyoteTicks + 1;
            f.JumpBuffer = 0;
            f.JumpCutEligible = true;
            events.Add(new SimEvent(tick, SimEventType.Jump, index, f.Position, new Vec2(0f, 1f), m.JumpSpeed));
        }

        static void WallJump(ref FighterState f, in MovementParams m, int wallSide, int index, int tick, SimEventBuffer events)
        {
            f.Velocity = new Vec2(-wallSide * m.WallJumpSpeedX, m.WallJumpSpeedY);
            f.Facing = (sbyte)-wallSide;
            f.WallJumpLockTicks = m.WallJumpInputLockTicks;
            f.WallJumpLockDirection = (sbyte)wallSide;
            f.TicksSinceWall = FighterState.TimerCap;
            f.AirJumpsLeft = m.AirJumps;
            f.DashCharges = m.DashCharges;
            f.JumpBuffer = 0;
            f.JumpCutEligible = true;
            events.Add(new SimEvent(tick, SimEventType.WallJump, index, f.Position, new Vec2(-wallSide, 1f), m.WallJumpSpeedY));
        }

        static void ApplyGravity(ref FighterState f, in MovementParams m, bool jumpHeld, int moveX, int moveY)
        {
            if (f.Grounded)
                return;
            const float dt = SimConfig.Dt;
            float vy = f.Velocity.Y;
            bool apex = SimMath.Abs(vy) < m.ApexSpeedThreshold && jumpHeld;
            float gravity = m.RiseGravity;
            if (vy > 0f)
            {
                if (f.JumpCutEligible && !jumpHeld)
                    gravity *= m.EarlyReleaseGravityMultiplier;
            }
            else
            {
                gravity *= m.FallGravityMultiplier;
            }
            if (apex)
                gravity *= m.ApexGravityMultiplier;
            bool fastFall = moveY < 0 && vy <= 0f;
            if (fastFall)
                gravity *= m.FastFallGravityMultiplier;
            float maxFall = fastFall ? m.FastFallMaxSpeed : m.MaxFallSpeed;
            vy = SimMath.Max(vy - gravity * dt, -maxFall);

            // 벽 미끄러짐: 공중, 벽에 닿음, 벽 쪽 입력, 내려가는 중
            if (f.WallSide != 0 && moveX == f.WallSide && vy < 0f)
                vy = SimMath.Max(vy, -m.WallSlideMaxSpeed);

            f.Velocity.Y = vy;
            if (vy <= 0f)
                f.JumpCutEligible = false;
        }

        public static void ResolveContacts(ref FighterState f, in FighterState other, in MovementScratch scratch, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events)
        {
            if (config.Combat.Body.SurfaceMode == 2)
                SpiderMovement.ResolveContacts(ref f, other, scratch, config, arena, index, tick, events);
            else
                ResolveContactsCore(ref f, other, scratch, config, arena, index, tick, events);
        }

        public static void ResolveContactsCore(ref FighterState f, in FighterState other, in MovementScratch scratch, SimConfig config, ArenaDef arena, int index, int tick, SimEventBuffer events)
        {
            ref readonly var m = ref config.Movement;
            float half = config.HalfBody;
            bool dashing = f.Action == ActionState.Dash && f.DashTicksLeft > 0;
            ref readonly var contacts = ref scratch.Contacts;

            if (contacts.OutOfBounds)
                events.Add(new SimEvent(tick, SimEventType.OutOfBounds, index, f.Position, Vec2.Zero, 0f));
            if (f.Action == ActionState.Mantle)
                return;

            bool bounced = false;
            if (contacts.BoundaryX != 0 || contacts.BoundaryY != 0)
            {
                BoundaryHit(ref f, contacts, config, index, tick, events);
                dashing = false;
                bounced = true;
            }
            else if (contacts.Landed && contacts.LandedKind == TerrainKind.Bounce)
            {
                // 튕김 패드: 윗면에 닿으면 vy = 30, 공중 점프·대시 회복.
                f.Velocity.Y = m.BouncePadSpeed;
                f.AirJumpsLeft = m.AirJumps;
                f.DashCharges = m.DashCharges;
                f.JumpCutEligible = false;
                f.DashTicksLeft = 0;
                dashing = false;
                bounced = true;
                events.Add(new SimEvent(tick, SimEventType.PadBounce, index, f.Position, new Vec2(0f, 1f), m.BouncePadSpeed));
            }
            else if (contacts.Landed && contacts.LandedKind == TerrainKind.Spike && CanTouchSpikes(f))
            {
                SpikeHit(ref f, config, index, tick, events);
                bounced = true;
            }
            else if (f.IsLaunched)
            {
                bounced = Rebound(ref f, contacts, config, index, tick, events);
            }
            else if (f.Action == ActionState.Lunge)
            {
                bounced = LungeBounce(ref f, contacts, config, index, tick, events);
            }

            // 표면 이동(CR-004 7절): 붙어 있거나 이번 틱에 붙으면 접지·벽 미끄러짐 처리 대신 여기서 끝낸다.
            if (config.Combat.Body.SurfaceMode == 1 && SurfaceContacts(ref f, scratch, config, arena, index, tick, events))
                return;

            int ground = FighterState.NoBox;
            if (!bounced && f.Velocity.Y <= 0f)
            {
                ground = TerrainCollision.ProbeGround(f.Position, half, arena, m.GroundProbeDistance, f.DropThroughBox);
                if (ground == FighterState.NoBox && f.OnProp > 0)
                    ground = FighterState.OnPropBox;
                if (ground == FighterState.NoBox && StandsOn(f.Position, other.Position, config))
                    ground = FighterState.OnFighter;
            }
            // 면역이 끝난 뒤 가시 위에 서 있으면 다시 닿은 것이다.
            if (ground >= 0 && arena.GetBox(ground).Kind == TerrainKind.Spike && CanTouchSpikes(f))
            {
                SpikeHit(ref f, config, index, tick, events);
                ground = FighterState.NoBox;
                bounced = true;
            }
            f.Grounded = ground != FighterState.NoBox;
            f.GroundBox = ground;
            f.WallSide = (sbyte)TerrainCollision.ProbeWall(f.Position, half, arena, m.GroundProbeDistance);

            if (f.Grounded)
            {
                if (!scratch.WasGrounded)
                {
                    float impact = contacts.Landed ? contacts.ImpactSpeed : -f.Velocity.Y;
                    events.Add(new SimEvent(tick, SimEventType.Land, index, f.Position, new Vec2(0f, -1f), impact));
                }
                if (f.Velocity.Y < 0f)
                    f.Velocity.Y = 0f;
                f.AirJumpsLeft = m.AirJumps;
                f.DashCharges = m.DashCharges;
                f.TicksSinceGrounded = 0;
                f.JumpCutEligible = false;

                // 착지 직전에 누른 점프(버퍼)는 착지한 틱에 실행한다.
                if (!scratch.WasGrounded && f.JumpBuffer > 0 && !dashing && f.InMovementState && !TryDropThrough(ref f, m, arena, scratch.MoveY))
                    GroundJump(ref f, m, index, tick, events);
            }
            else
            {
                f.TicksSinceGrounded = SimMath.Min(f.TicksSinceGrounded + 1, FighterState.TimerCap);
            }

            if (f.WallSide != 0 && !f.Grounded)
            {
                f.TicksSinceWall = 0;
                f.LastWallSide = f.WallSide;
                f.AirJumpsLeft = m.AirJumps;
                f.DashCharges = m.DashCharges;
            }
            else
            {
                f.TicksSinceWall = SimMath.Min(f.TicksSinceWall + 1, FighterState.TimerCap);
            }

            // 모드 B: 공중에서 시작한 공격 도중 착지하면 공격이 끝나고 착지 경직.
            if (f.Action == ActionState.Attack && f.AttackFromAir && f.Grounded && !scratch.WasGrounded && f.Form == FighterForm.Paper)
            {
                f.RestartAction(ActionState.LandingLag);
                f.StunTicks = config.Combat.Swing.LandingLagTicks;
                f.AttackId = FighterState.NoAttack;
                f.AttackHasHit = false;
            }

            // 돌진은 속도가 12 미만이 되거나 30틱이 지나면 끝나고 이동 상태로 돌아간다.
            if (f.Action == ActionState.Lunge)
            {
                ref readonly var charge = ref config.Combat.Charge;
                if (f.Velocity.LengthSquared < charge.LungeEndSpeed * charge.LungeEndSpeed || f.ActionTick >= charge.LungeMaxTicks)
                    f.RestartAction(ActionState.Airborne);
            }

            if (!f.InMovementState)
                return;

            if (dashing)
            {
                f.SetAction(ActionState.Dash);
            }
            else if (f.Grounded)
            {
                f.SetAction(SimMath.Abs(f.Velocity.X) > SimMath.Epsilon || scratch.MoveX != 0 ? ActionState.Run : ActionState.Idle);
            }
            else if (scratch.Climbing && f.WallSide != 0)
            {
                f.SetAction(ActionState.Climb);
            }
            else if (f.WallSide != 0 && scratch.MoveX == f.WallSide && f.Velocity.Y < 0f)
            {
                if (f.Action != ActionState.WallSlide)
                    events.Add(new SimEvent(tick, SimEventType.WallSlideStart, index, f.Position, new Vec2(f.WallSide, 0f), -f.Velocity.Y));
                f.SetAction(ActionState.WallSlide);
            }
            else
            {
                f.SetAction(ActionState.Airborne);
            }

            if (!dashing && scratch.MoveX != 0)
                f.Facing = (sbyte)scratch.MoveX;
        }

        /// <summary>
        /// 피격 경직 중 지형에 부딪힘. 법선 속도가 WallSplatSpeed(14) 이상이면 벽꽝(데미지, 반사 × 0.45, 경직 +18, 히트스탑 6),
        /// BounceMinSpeed 이상이면 튕김(반사 × 0.35). 튕겼으면 true.
        /// 지형 이동이 부딪힌 축의 속도를 0으로 만들었으므로 부딪히기 직전 값으로 되돌려 반사한다.
        /// </summary>
        static bool Rebound(ref FighterState f, in MoveContacts contacts, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            float half = config.HalfBody;
            float wall = contacts.HitWall != 0 ? contacts.WallImpactSpeed : 0f;
            float floor = contacts.Landed ? contacts.ImpactSpeed : 0f;
            float ceiling = contacts.HitCeiling ? contacts.CeilingImpactSpeed : 0f;
            float strongest = SimMath.Max(wall, SimMath.Max(floor, ceiling));
            if (strongest < c.BounceMinSpeed)
                return false;
            // 던져진 상태는 벽꽝 기준이 낮고(12) 데미지가 크다(10).
            bool thrown = f.Action == ActionState.Thrown;
            float splatSpeed = thrown ? c.Grab.ThrownSplatSpeed : c.WallSplatSpeed;
            int splatDamage = thrown ? c.Grab.ThrownSplatDamage : c.WallSplatDamage;

            var v = f.Velocity;
            if (contacts.HitWall != 0)
                v.X = -contacts.HitWall * wall;
            if (contacts.Landed)
                v.Y = floor;
            else if (contacts.HitCeiling)
                v.Y = -ceiling;

            Vec2 normal, point;
            if (wall >= floor && wall >= ceiling)
            {
                normal = new Vec2(-contacts.HitWall, 0f);
                point = new Vec2(f.Position.X + contacts.HitWall * half, f.Position.Y);
            }
            else if (floor >= ceiling)
            {
                normal = new Vec2(0f, 1f);
                point = new Vec2(f.Position.X, f.Position.Y - half);
            }
            else
            {
                normal = new Vec2(0f, -1f);
                point = new Vec2(f.Position.X, f.Position.Y + half);
            }

            bool splat = strongest >= splatSpeed;
            f.Velocity = v * (splat ? c.WallSplatRestitution : c.BounceRestitution);
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            if (splat && c.Body.Enabled)
            {
                // CR-004(5.2): 벽꽝 추가 피해는 충돌 속도에 비례하고, 한 번 날아가는 동안의 합이 상한을 넘지 않는다. 경직은 고정(누적하지 않음).
                int damage = SimMath.Min(c.Body.SplatDamageMax - f.SplatDamageTaken, (int)(strongest * c.Body.SplatDamagePerSpeed + 0.5f));
                damage = SimMath.Max(0, damage);
                f.SplatDamageTaken += damage;
                f.Health = SimMath.Max(0, f.Health - damage);
                f.StunTicks = SimMath.Max(f.StunTicks, c.Body.SplatHitstun);
                f.HitstopTicks = SimMath.Max(f.HitstopTicks, c.WallSplatHitstop);
                events.Add(new SimEvent(tick, SimEventType.WallSplat, index, point, normal, strongest));
            }
            else if (splat)
            {
                f.Health = SimMath.Max(0, f.Health - splatDamage);
                f.StunTicks += c.WallSplatHitstun;
                f.HitstopTicks = SimMath.Max(f.HitstopTicks, c.WallSplatHitstop);
                events.Add(new SimEvent(tick, SimEventType.WallSplat, index, point, normal, strongest));
            }
            else
            {
                events.Add(new SimEvent(tick, SimEventType.BounceRebound, index, point, normal, strongest));
            }
            return true;
        }

        /// <summary>
        /// 돌진 중 지형에 부딪힘: 법선 기준 반사 × 0.6, 최대 2회. 그 뒤로 부딪히면 돌진이 끝난다.
        /// 바닥을 미끄러지듯 스치는 접촉(법선 속도 BounceMinSpeed 미만)은 튕김으로 세지 않는다. 튕겼으면 true.
        /// </summary>
        static bool LungeBounce(ref FighterState f, in MoveContacts contacts, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            float half = config.HalfBody;
            float wall = contacts.HitWall != 0 ? contacts.WallImpactSpeed : 0f;
            float floor = contacts.Landed ? contacts.ImpactSpeed : 0f;
            float ceiling = contacts.HitCeiling ? contacts.CeilingImpactSpeed : 0f;
            float strongest = SimMath.Max(wall, SimMath.Max(floor, ceiling));
            if (strongest < c.BounceMinSpeed)
                return false;
            if (f.LungeBounces >= c.Charge.LungeMaxBounces)
            {
                f.RestartAction(ActionState.Airborne);
                if (c.Charge.LungeStopRecoil <= 0f)
                    return false;
                return LungeStop(ref f, contacts, wall, floor, ceiling, strongest, config, index, tick, events);
            }

            var v = f.Velocity;
            if (contacts.HitWall != 0)
                v.X = -contacts.HitWall * wall;
            if (contacts.Landed)
                v.Y = floor;
            else if (contacts.HitCeiling)
                v.Y = -ceiling;
            f.Velocity = v * c.Charge.LungeBounceRestitution;
            f.LungeBounces++;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            if (contacts.HitWall != 0)
                f.Facing = (sbyte)-contacts.HitWall;

            Vec2 normal, point;
            if (wall >= floor && wall >= ceiling)
            {
                normal = new Vec2(-contacts.HitWall, 0f);
                point = new Vec2(f.Position.X + contacts.HitWall * half, f.Position.Y);
            }
            else if (floor >= ceiling)
            {
                normal = new Vec2(0f, 1f);
                point = new Vec2(f.Position.X, f.Position.Y - half);
            }
            else
            {
                normal = new Vec2(0f, -1f);
                point = new Vec2(f.Position.X, f.Position.Y + half);
            }
            events.Add(new SimEvent(tick, SimEventType.BounceRebound, index, point, normal, strongest));
            return true;
        }

        /// <summary>
        /// CR-004: 튕김 대신 멈춤. 부딪힌 면의 법선 쪽으로 LungeStopRecoil만큼 작은 반동, 접선 속도는 LungeStopKeep만 남긴다.
        /// 바닥에 닿았으면 반동 없이 착지한다. 충격 연출용으로 BounceRebound 이벤트를 낸다.
        /// </summary>
        static bool LungeStop(ref FighterState f, in MoveContacts contacts, float wall, float floor, float ceiling, float strongest,
            SimConfig config, int index, int tick, SimEventBuffer events)
        {
            ref readonly var p = ref config.Combat.Charge;
            float half = config.HalfBody;
            var v = f.Velocity * p.LungeStopKeep;
            Vec2 normal, point;
            if (wall >= floor && wall >= ceiling)
            {
                normal = new Vec2(-contacts.HitWall, 0f);
                point = new Vec2(f.Position.X + contacts.HitWall * half, f.Position.Y);
                v.X = normal.X * p.LungeStopRecoil;
                f.Facing = (sbyte)-contacts.HitWall;
            }
            else if (floor >= ceiling)
            {
                normal = new Vec2(0f, 1f);
                point = new Vec2(f.Position.X, f.Position.Y - half);
                v.Y = 0f;
            }
            else
            {
                normal = new Vec2(0f, -1f);
                point = new Vec2(f.Position.X, f.Position.Y + half);
                v.Y = -p.LungeStopRecoil;
            }
            f.Velocity = v;
            events.Add(new SimEvent(tick, SimEventType.BounceRebound, index, point, normal, strongest));
            // 반사가 아니므로 땅 확인을 그대로 한다(바닥을 달리던 돌진이 벽에 멈추면 서 있다).
            return false;
        }

        /// <summary>면역 중이거나 붙잡기 중(둘이 함께 멈춰 있음)이면 가시에 닿지 않는다.</summary>
        static bool CanTouchSpikes(in FighterState f) =>
            f.SpikeImmunityTicks == 0 && f.Action != ActionState.Held && f.Action != ActionState.Holding;

        /// <summary>
        /// 가시: 데미지 15, vy = 20으로 튕겨 올림, 경직 20틱, 45틱 면역, 공중 점프·대시 회복(빠져나올 기회).
        /// 이미 날아가는 경직 중이면 연속 명중 수는 그대로 둔다.
        /// </summary>
        static void SpikeHit(ref FighterState f, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            ref readonly var m = ref config.Movement;
            int combo = f.IsLaunched ? f.ComboHits : 0;
            f.Velocity = new Vec2(f.Velocity.X, c.SpikeBounceSpeed);
            f.Health = SimMath.Max(0, f.Health - c.SpikeDamage);
            f.StunTicks = c.SpikeHitstun;
            FighterCombat.EnterStun(ref f, ActionState.Hitstun);
            f.ComboHits = combo;
            f.SpikeImmunityTicks = c.SpikeImmunityTicks;
            f.AirJumpsLeft = m.AirJumps;
            f.DashCharges = m.DashCharges;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            events.Add(new SimEvent(tick, SimEventType.Spike, index, new Vec2(f.Position.X, f.Position.Y - config.HalfBody), new Vec2(0f, 1f), c.SpikeDamage));
        }

        /// <summary>
        /// 경계 튕김(03-combat.md 3절, CR-002): 피해 clamp(round(4 + 법선 속도 × 0.35), 4, 15), 법선 속도를 반사해 × 1.1(안쪽으로 최소 14),
        /// 경직 14, 히트스탑 5, 면역 45틱. 면역 중에는 피해 없이 반사 × 0.5. 가드·패링은 적용하지 않는다.
        /// 잡고 있거나 잡힌 동안은 피해 없이 멈추기만 한다. 공중 점프·대시는 회복한다(아래 경계에서 빠져나올 수 있게, S-114).
        /// </summary>
        static void BoundaryHit(ref FighterState f, in MoveContacts contacts, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            ref readonly var m = ref config.Movement;
            float half = config.HalfBody;
            var normal = SimMath.Normalize(new Vec2(-contacts.BoundaryX, -contacts.BoundaryY));
            var point = new Vec2(f.Position.X + contacts.BoundaryX * half, f.Position.Y + contacts.BoundaryY * half);
            float speed = SimMath.Max(contacts.BoundarySpeedX, contacts.BoundarySpeedY);
            if (f.Action == ActionState.Held || f.Action == ActionState.Holding)
            {
                if (contacts.BoundaryX != 0)
                    f.Velocity.X = 0f;
                if (contacts.BoundaryY != 0)
                    f.Velocity.Y = 0f;
                return;
            }
            bool immune = f.BoundaryImmunityTicks > 0;
            if (contacts.BoundaryX != 0)
                f.Velocity.X = -contacts.BoundaryX * BoundaryReboundSpeed(contacts.BoundarySpeedX, immune, c);
            if (contacts.BoundaryY != 0)
                f.Velocity.Y = -contacts.BoundaryY * BoundaryReboundSpeed(contacts.BoundarySpeedY, immune, c);
            int damage = 0;
            if (!immune)
            {
                damage = BoundaryDamage(speed, c);
                int combo = f.IsLaunched ? f.ComboHits : 0;
                f.Health = SimMath.Max(0, f.Health - damage);
                f.StunTicks = c.BoundaryHitstun;
                FighterCombat.EnterStun(ref f, ActionState.Hitstun);
                f.ComboHits = combo;
                f.HitstopTicks = SimMath.Max(f.HitstopTicks, c.BoundaryHitstop);
                f.BoundaryImmunityTicks = c.BoundaryImmunityTicks;
            }
            f.DashTicksLeft = 0;
            f.AirJumpsLeft = m.AirJumps;
            f.DashCharges = m.DashCharges;
            f.Grounded = false;
            f.GroundBox = FighterState.NoBox;
            events.Add(new SimEvent(tick, SimEventType.BoundaryHit, index, point, normal, damage));
        }

        public static int BoundaryDamage(float normalSpeed, in CombatParams c) =>
            SimMath.Clamp(SimMath.RoundToInt(c.BoundaryBaseDamage + normalSpeed * c.BoundaryDamagePerSpeed), c.BoundaryMinDamage, c.BoundaryMaxDamage);

        public static float BoundaryReboundSpeed(float normalSpeed, bool immune, in CombatParams c) =>
            immune ? normalSpeed * c.BoundaryImmuneRestitution : SimMath.Max(normalSpeed * c.BoundaryRestitution, c.BoundaryMinReboundSpeed);

        /// <summary>상대 머리 위(단방향 발판처럼)에 서 있는지.</summary>
        public static bool StandsOn(Vec2 self, Vec2 other, SimConfig config)
        {
            float size = config.Movement.BodySize;
            if (SimMath.Abs(self.X - other.X) >= size - TerrainCollision.Epsilon)
                return false;
            float gap = self.Y - other.Y - size;
            return gap >= -TerrainCollision.Epsilon && gap <= config.Movement.GroundProbeDistance;
        }
    }

    /// <summary>
    /// 파이터끼리의 몸 충돌(02-movement.md 7절). 겹치면 수평으로 절반씩 밀어낸다. 속도는 바꾸지 않는다.
    /// 직전 틱에 완전히 위에 있었고 내려오는 중이면 상대 머리 위에 세운다.
    /// 히트스탑으로 멈춘 쪽은 움직이지 않고, 움직이는 쪽이 겹침을 모두 떠맡는다.
    /// 두 파이터를 같은 규칙으로 동시에 다루며, 번호로 우선순위를 정하지 않는다.
    /// </summary>
    public static class FighterPush
    {
        public static void Resolve(ref FighterState a, ref FighterState b, in FighterState prevA, in FighterState prevB, SimConfig config, ArenaDef arena)
            => Resolve(ref a, ref b, prevA, prevB, false, false, config, arena);

        public static void Resolve(ref FighterState a, ref FighterState b, in FighterState prevA, in FighterState prevB, bool frozenA, bool frozenB, SimConfig config, ArenaDef arena)
        {
            if (frozenA && frozenB)
                return;
            float size = config.Movement.BodySize;
            float half = config.HalfBody;
            float dx = b.Position.X - a.Position.X;
            float dy = b.Position.Y - a.Position.Y;
            const float eps = TerrainCollision.Epsilon;
            if (SimMath.Abs(dx) >= size - eps || SimMath.Abs(dy) >= size - eps)
                return;

            bool aWasAbove = !frozenA && prevA.Position.Y - prevB.Position.Y >= size - eps && a.Velocity.Y <= 0f;
            bool bWasAbove = !frozenB && prevB.Position.Y - prevA.Position.Y >= size - eps && b.Velocity.Y <= 0f;
            if (aWasAbove && TryStack(ref a, ref b, frozenB, size, half, arena))
                return;
            if (bWasAbove && TryStack(ref b, ref a, frozenA, size, half, arena))
                return;

            int direction = SimMath.Sign(dx);
            if (direction == 0)
                direction = SimMath.Sign(prevB.Position.X - prevA.Position.X);
            if (direction == 0)
                return;
            float overlap = size - SimMath.Abs(dx);
            if (!frozenA)
            {
                float fromX = a.Position.X;
                a.Position.X -= direction * (overlap * (frozenB ? 1f : 0.5f));
                TerrainCollision.ResolveX(ref a.Position, -direction, half, arena, fromX);
                TerrainCollision.ClampToBoundary(ref a.Position, half, arena);
            }
            if (!frozenB)
            {
                float fromX = b.Position.X;
                b.Position.X += direction * (overlap * (frozenA ? 1f : 0.5f));
                TerrainCollision.ResolveX(ref b.Position, direction, half, arena, fromX);
                TerrainCollision.ClampToBoundary(ref b.Position, half, arena);
            }
        }

        /// <summary>
        /// 위에서 내려온 파이터(top)를 아래 파이터 머리 위에 세운다(02-movement.md 7절). 아래 파이터가 솟아올라 위 파이터가
        /// 천장 속으로 들어가게 되면, 위 파이터는 천장 바로 아래에 두고 아래 파이터를 그만큼 내리며 위로 가던 속도를 멈춘다(CP9 QA F-1).
        /// 둘이 쌓일 높이가 없거나 아래 파이터가 히트스탑 중이면 false: 옆으로 밀어내는 규칙으로 넘긴다.
        /// </summary>
        static bool TryStack(ref FighterState top, ref FighterState bottom, bool bottomFrozen, float size, float half, ArenaDef arena)
        {
            var place = new Vec2(top.Position.X, bottom.Position.Y + size);
            if (arena == null || !TerrainCollision.ResolveCeiling(ref place, half, arena, top.Position.Y))
            {
                top.Position.Y = place.Y;
                top.Velocity.Y = 0f;
                return true;
            }
            var below = new Vec2(bottom.Position.X, place.Y - size);
            if (bottomFrozen || TerrainCollision.OverlapsSolid(below, half, arena))
                return false;
            top.Position.Y = place.Y;
            top.Velocity.Y = 0f;
            bottom.Position.Y = below.Y;
            if (bottom.Velocity.Y > 0f)
                bottom.Velocity.Y = 0f;
            return true;
        }
    }
}
