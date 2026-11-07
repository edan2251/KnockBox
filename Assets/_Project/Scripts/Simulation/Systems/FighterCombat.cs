namespace Knockbox.Simulation
{
    /// <summary>이동이 끝난 직후(몸 밀어내기 전) 두 파이터의 몸 접촉. 상호작용 판정의 몸 충돌에 쓴다.</summary>
    public struct BodyContact
    {
        public bool Touching;
        /// <summary>A → B 방향 단위 벡터. 위치가 완전히 같으면 (0, 1).</summary>
        public Vec2 Normal;
        public Vec2 Point;

        public static BodyContact Measure(in FighterState a, in FighterState b, SimConfig config)
        {
            float size = config.Movement.BodySize;
            float reach = size + config.Combat.Charge.ContactSlop;
            var d = b.Position - a.Position;
            var contact = new BodyContact
            {
                Touching = SimMath.Abs(d.X) < reach && SimMath.Abs(d.Y) < reach,
                Normal = SimMath.Normalize(d),
                Point = (a.Position + b.Position) * 0.5f,
            };
            if (contact.Normal.LengthSquared == 0f)
                contact.Normal = new Vec2(0f, 1f);
            return contact;
        }
    }

    /// <summary>몸 충돌 판정 결과.</summary>
    public enum BodyOutcome : byte
    {
        /// <summary>둘 다 위력 기준 미만: 밀어내기만.</summary>
        None,
        Clash,
        FirstWins,
        SecondWins,
    }

    /// <summary>
    /// 전투 공통 규칙(03-combat.md 3절)과 모드 A 돌진(4절).
    /// BeginTick: 각 파이터가 자기 상태와 입력만 보고 행동을 바꾼다(경직 진행, 공격 종료, 가드·패링, 공격·충전 시작, 돌진 발사).
    /// Freeze: 히트스탑 중인 파이터는 위치, 속도, 행동 타이머가 멈추고 새로 누른 버튼만 기록한다.
    /// ResolveInteractions: 이동이 끝난 두 파이터의 스냅숏 하나로 양쪽 판정(공격 판정 원, 몸 충돌 위력)을 모두 계산한 뒤
    /// 결과를 동시에 적용한다. 어느 단계에서도 파이터 번호로 우선순위를 정하지 않는다. 서로 동시에 맞히면 클래시다.
    /// </summary>
    public static class FighterCombat
    {
        /// <summary>히트스탑 중인 틱. 타이머를 하나 줄이고, 새로 누른 버튼을 히트스탑이 끝난 뒤로 미룬다.</summary>
        public static void Freeze(ref FighterState f, in InputFrame input, SimConfig config)
        {
            f.HitstopTicks--;
            var pressed = InputFrame.Pressed(input.Buttons, f.PrevButtons);
            f.PendingPresses |= pressed;
            if ((pressed & InputButtons.Jump) != 0)
                f.JumpBuffer = config.Movement.JumpBufferTicks;
        }

        /// <summary>
        /// 행동 전환. 이번 틱에 새로 눌린 버튼(히트스탑 동안 눌러 둔 것 포함)을 돌려준다.
        /// 경직이 끝나는 틱과 공격이 끝나는 틱에는 바로 다음 행동을 할 수 있다.
        /// </summary>
        public static InputButtons BeginTick(ref FighterState f, in InputFrame input, SimConfig config, int index, int tick, SimEventBuffer events,
            BrawlWeaponKind heldWeapon = BrawlWeaponKind.None)
        {
            ref readonly var c = ref config.Combat;
            var pressed = InputFrame.Pressed(input.Buttons, f.PrevButtons) | f.PendingPresses;
            f.PendingPresses = InputButtons.None;

            if (f.ParryTicks > 0)
                f.ParryTicks--;
            if (f.ContactCooldown > 0)
                f.ContactCooldown--;
            if (f.SpikeImmunityTicks > 0)
                f.SpikeImmunityTicks--;
            if (f.BoundaryImmunityTicks > 0)
                f.BoundaryImmunityTicks--;
            if (f.BoxDamageTicks > 0 && --f.BoxDamageTicks == 0)
                f.BoxDamageSum = 0;
            if (f.AttackBuffer > 0)
                f.AttackBuffer--;
            if (f.GrabProtectTicks > 0)
                f.GrabProtectTicks--;
            TickEnergy(ref f, config);
            // 가드 입력은 조작할 수 없을 때 눌러도 센다(경직 중 연타로 패링을 준비하지 못하게).
            bool guardPressed = (pressed & InputButtons.Guard) != 0;
            int sinceGuardPress = SimMath.Min(f.TicksSinceGuardPress + 1, FighterState.TimerCap);
            f.TicksSinceGuardPress = guardPressed ? 0 : sinceGuardPress;

            switch (f.Action)
            {
                case ActionState.Hitstun:
                case ActionState.GuardStun:
                case ActionState.Stagger:
                case ActionState.Recoil:
                case ActionState.LandingLag:
                case ActionState.Thrown:
                    if (f.StunTicks > 0)
                    {
                        f.StunTicks--;
                        return pressed;
                    }
                    if (f.IsLaunched)
                        f.ComboHits = 0;
                    // 재그랩 보호(5.2): 던져졌거나 벽꽝을 맞은 뒤 회복하면 잠시 잡히지 않는다.
                    if (c.Body.Enabled && (f.Action == ActionState.Thrown || f.SplatDamageTaken > 0))
                        f.GrabProtectTicks = c.Body.RegrabProtectTicks;
                    f.SplatDamageTaken = 0;
                    f.RestartAction(ActionState.Airborne);
                    break;
                case ActionState.Attack:
                    if (f.ActionTick < config.GetAttack(f.AttackId).TotalTicks)
                    {
                        // 몸 폼(5.1): 주먹 후딜 중 누른 좌클릭은 잠시 저장했다가 끝나면 시작한다.
                        if (f.Form == FighterForm.Body && (pressed & InputButtons.Attack) != 0
                            && (!c.Body.SeparateChargeInput
                                || ((input.Buttons | pressed) & InputButtons.Charge) == 0))
                            f.AttackBuffer = c.Body.PunchBufferTicks;
                        return pressed;
                    }
                    f.AttackId = FighterState.NoAttack;
                    f.AttackHasHit = false;
                    f.AttackDir = Vec2.Zero;
                    f.RestartAction(ActionState.Airborne);
                    if (f.AttackBuffer > 0)
                    {
                        f.AttackBuffer = 0;
                        pressed |= InputButtons.Attack;
                    }
                    break;
                case ActionState.Charge when f.Form == FighterForm.Body:
                    // 준비·충전 중 우클릭: 취소하고 잡기로 전환(비용 없음, 5.2 받아내기 최우선).
                    if ((pressed & InputButtons.Grab) != 0 && config.GrabEnabled)
                    {
                        StartGrab(ref f, AimFacing(input), index, tick, events);
                        f.AttackDir = AimOrMove(f, input);
                        return pressed;
                    }
                    BodyCharge(ref f, input, config, index, tick, events, heldWeapon);
                    return pressed;
                case ActionState.Charge:
                    // 누르고 있는 동안 충전. 떼거나 최대 충전 뒤 24틱이 지나면 발사.
                    if (input.Held(InputButtons.Attack) && f.ActionTick < c.Charge.FullChargeTicks + c.Charge.FullChargeHoldTicks)
                        return pressed;
                    Launch(ref f, input, config, index, tick, events);
                    return pressed;
                case ActionState.Shoot:
                    // 바위(CP19): 선딜이 끝나는 틱에 발사, 후딜이 끝나면 조작 가능.
                    int startup = ShotStartup(f, config);
                    if (f.ActionTick == startup)
                        Fire(ref f, input, config, index, tick, events);
                    if (f.Action != ActionState.Shoot || f.ActionTick < startup + c.Rock.RecoveryTicks)
                        return pressed;
                    f.RestartAction(ActionState.Airborne);
                    break;
                case ActionState.Grab:
                    // 시작 → 활성 → 헛잡기 후딜. 잡으면 상호작용 판정이 Holding으로 바꾼다.
                    if (f.ActionTick < c.Grab.TotalWhiffTicks)
                        return pressed;
                    f.AttackDir = Vec2.Zero;
                    f.RestartAction(ActionState.Airborne);
                    break;
                case ActionState.Holding when f.Form == FighterForm.Body:
                    // 몸 폼(5.2): 좌클릭을 누르는 순간(탭/홀드 구분 없음) 커서 방향으로 던진다.
                    // 잡는 순간 이미 눌려 있던 좌클릭은 눌림이 아니므로 무시된다(뗐다가 다시 눌러야 한다).
                    // 최대 유지 시간 동안 던지지 않으면 놓아준다(단계 C 피드백: 자동 던짐 대신 해제). 방향 0 = 놓기.
                    if (!f.ThrowPending && (pressed & InputButtons.Attack) != 0)
                    {
                        f.ThrowPending = true;
                        f.ThrowDirection = AimOrMove(f, input);
                    }
                    else if (!f.ThrowPending && f.ActionTick >= c.Grab.MaxHoldTicks)
                    {
                        f.ThrowPending = true;
                        f.ThrowDirection = Vec2.Zero;
                    }
                    return pressed;
                case ActionState.Holding:
                    // 잡기 버튼을 떼거나 최대 시간이 지나면 이번 틱에 던진다. 방향은 이번 틱의 8방향 입력.
                    if (!f.ThrowPending && (!input.Held(InputButtons.Grab) || f.ActionTick >= c.Grab.MaxHoldTicks))
                    {
                        f.ThrowPending = true;
                        f.ThrowDirection = input.MoveX == 0 && input.MoveY == 0
                            ? new Vec2(c.Grab.NeutralThrowDirection.X * f.Facing, c.Grab.NeutralThrowDirection.Y)
                            : SimMath.Normalize(new Vec2(input.MoveX, input.MoveY));
                    }
                    return pressed;
                case ActionState.Lunge:
                case ActionState.KO:
                case ActionState.Held:
                    return pressed;
            }

            if (f.Action == ActionState.Dash && f.DashTicksLeft > 0)
            {
                // 모드 B: 대시 시작 4틱 이후에는 공격으로 캔슬할 수 있다(대시 속도를 그대로 실은 공격).
                ref readonly var swing = ref c.Swing;
                int elapsed = config.Movement.DashTicks - f.DashTicksLeft;
                if (f.Form == FighterForm.Paper && swing.DashAttackCancelEnabled && (pressed & InputButtons.Attack) != 0
                    && elapsed >= swing.DashAttackCancelMinTicks && CanAfford(f, CostFor(f, FighterForm.Paper, config), config))
                {
                    StartAttack(ref f, input.MoveX, SwingAttack(f, input, c.Swing), index, tick, events);
                    Spend(ref f, CostFor(f, FighterForm.Paper, config), config);
                }
                return pressed;
            }

            if (config.GuardEnabled && input.Held(InputButtons.Guard))
            {
                f.SetAction(ActionState.Guard);
                if (guardPressed && sinceGuardPress >= c.ParryCooldownTicks)
                {
                    f.ParryTicks = c.ParryWindowTicks;
                    events.Add(new SimEvent(tick, SimEventType.ParryWindow, index, f.Position, new Vec2(f.Facing, 0f), c.ParryWindowTicks));
                }
                return pressed;
            }
            if (f.Action == ActionState.Guard)
            {
                f.ParryTicks = 0;
                f.RestartAction(ActionState.Airborne);
            }

            // 폼 전환(4.5): 지금 공격을 시작할 수 있는 자유 상태에서만 받는다(공격·후딜·경직·가드 중의 입력은 위에서 이미 돌아갔다).
            // 즉시 바뀌고, 같은 틱에 누른 공격은 새 폼으로 시작한다. 에너지와 폼별 상태(날아가는 투사체)는 그대로.
            if ((pressed & InputButtons.SwitchForm) != 0)
            {
                var next = Forms.NextOwned(f.Form, f.OwnedForms);
                if (next != f.Form)
                {
                    events.Add(new SimEvent(tick, SimEventType.FormSwitch, index, f.Position, new Vec2((int)f.Form, 0f), (int)next));
                    f.Form = next;
                }
            }

            int grabCost = SimMath.Max(c.Energy.GrabCost, 0);
            if ((pressed & InputButtons.Grab) != 0 && (pressed & InputButtons.Attack) == 0 && config.GrabEnabled && CanAfford(f, grabCost, config))
            {
                StartGrab(ref f, f.Form == FighterForm.Body ? AimFacing(input) : input.MoveX, index, tick, events);
                // 몸 폼: 잡기 판정은 커서 방향(등 뒤도 잡을 수 있다).
                if (f.Form == FighterForm.Body)
                    f.AttackDir = AimOrMove(f, input);
                Spend(ref f, grabCost, config);
                return pressed;
            }

            // 에너지가 모자라면 확인에서 막혀 공격이 시작되지 않는다. 입력은 버려지고 대기하지 않는다(4.4).
            int cost = CostFor(f, f.Form, config);
            // 현재 몸 폼은 Shift 차징과 주먹을 분리한다. 동시에 누르면 차징이 우선하며,
            // 차징 중 좌클릭은 위의 Charge 분기에서 소비하므로 나중에 주먹으로 이어지지 않는다.
            if (f.Form == FighterForm.Body && c.Body.SeparateChargeInput
                && (pressed & InputButtons.Charge) != 0 && CanAfford(f, cost, config))
            {
                f.AttackBuffer = 0;
                StartCharge(ref f, AimFacing(input));
                return pressed;
            }
            if ((pressed & InputButtons.Attack) != 0 && CanAfford(f, cost, config))
            {
                switch (f.Form)
                {
                    case FighterForm.Scissors:
                        // 충전은 확인만 하고, 소비는 발사할 때(Launch). 즉발 돌진 증강이면 누른 틱에 바로 발사(시작 = 발사).
                        StartCharge(ref f, input.MoveX);
                        if (Augments.Compute(f.Augments, config).InstantLunge)
                            Launch(ref f, input, config, index, tick, events);
                        break;
                    case FighterForm.Paper:
                        StartAttack(ref f, input.MoveX, SwingAttack(f, input, c.Swing), index, tick, events);
                        Spend(ref f, cost, config);
                        break;
                    case FighterForm.Rock:
                        // 선딜 시작은 확인만, 소비는 발사할 때(Fire).
                        StartShoot(ref f, input.MoveX);
                        break;
                    case FighterForm.Body:
                        if (c.Body.SeparateChargeInput)
                        {
                            int attackId = heldWeapon == BrawlWeaponKind.Pan ? AttackIds.Pan
                                : heldWeapon == BrawlWeaponKind.Pistol ? AttackIds.Pistol : AttackIds.Punch;
                            StartAttack(ref f, AimFacing(input), attackId, index, tick, events);
                            f.AttackDir = AimOrMove(f, input);
                        }
                        else
                        {
                            // 구 기록: 좌클릭 탭/홀드는 다음 틱부터 BodyCharge가 가른다.
                            StartCharge(ref f, AimFacing(input));
                        }
                        break;
                    default:
                        StartAttack(ref f, input.MoveX, AttackIds.Test, index, tick, events);
                        break;
                }
            }
            return pressed;
        }

        /// <summary>공격 행동(충전, 돌진, 선딜·활성·후딜, 잡기)인지. 이 동안 에너지 회복 지연이 처음으로 돌아간다.</summary>
        public static bool IsAttackAction(in FighterState f) =>
            f.Action == ActionState.Charge || f.Action == ActionState.Lunge || f.Action == ActionState.Attack
            || f.Action == ActionState.Grab || f.Action == ActionState.Holding || f.Action == ActionState.Shoot;

        /// <summary>에너지 회복(4.4): 공격 행동 동안은 지연을 처음으로 돌리고, 끝난 뒤 지연이 지나면 틱마다 회복한다.</summary>
        static void TickEnergy(ref FighterState f, SimConfig config)
        {
            if (!config.EnergyEnabled)
                return;
            ref readonly var e = ref config.Combat.Energy;
            if (IsAttackAction(f))
                f.EnergyRegenDelay = e.RegenDelayTicks;
            else if (f.EnergyRegenDelay > 0)
                f.EnergyRegenDelay--;
            else if (f.Energy < e.Max)
                f.Energy = SimMath.Min(e.Max, f.Energy + e.RegenPerTick);
        }

        /// <summary>증강을 적용한 폼의 공격 비용(하한 0, 5.3).</summary>
        public static int CostFor(in FighterState f, FighterForm form, SimConfig config)
        {
            int cost = config.Combat.Energy.CostOf(form);
            if (f.Augments.Count == 0)
                return cost;
            var e = Augments.Compute(f.Augments, config);
            int add = form == FighterForm.Scissors ? e.LungeCostAdd : form == FighterForm.Rock ? e.ShotCostAdd : form == FighterForm.Paper ? e.SwingCostAdd : 0;
            return SimMath.Max(0, cost + add);
        }

        /// <summary>증강을 적용한 바위 선딜(하한 1틱, 5.3).</summary>
        public static int ShotStartup(in FighterState f, SimConfig config)
        {
            int startup = config.Combat.Rock.StartupTicks;
            if (f.Augments.Count > 0)
                startup += Augments.Compute(f.Augments, config).ShotStartupAdd;
            return SimMath.Max(1, startup);
        }

        /// <summary>시작 확인(차감 없음). 스위치가 꺼져 있으면 항상 된다.</summary>
        public static bool CanAfford(in FighterState f, int cost, SimConfig config) => !config.EnergyEnabled || f.Energy >= cost;

        /// <summary>행동당 한 번만 소비한다(4.4 규칙 1).</summary>
        static void Spend(ref FighterState f, int cost, SimConfig config)
        {
            if (!config.EnergyEnabled || f.ActionPaid)
                return;
            f.Energy -= cost;
            f.ActionPaid = true;
            f.EnergyRegenDelay = config.Combat.Energy.RegenDelayTicks;
        }

        /// <summary>모드 B 기술 고르기: 위 → 위 휘두르기, 공중에서 아래 → 내려찍기, 그 외(중립·좌우·지상 아래) → 앞 휘두르기.</summary>
        static int SwingAttack(in FighterState f, in InputFrame input, in SwingModeParams swing)
        {
            if (swing.ForwardOnly)
                return AttackIds.SwingForward;
            if (input.MoveY > 0)
                return AttackIds.SwingUp;
            if (input.MoveY < 0 && !f.Grounded)
                return AttackIds.SwingDown;
            return AttackIds.SwingForward;
        }

        static void StartAttack(ref FighterState f, int moveX, int attackId, int index, int tick, SimEventBuffer events)
        {
            // 공격을 시작하는 틱은 아직 조작 가능한 상태이므로 입력 쪽을 바라본다. 공격 중에는 바꾸지 않는다.
            if (moveX != 0)
                f.Facing = (sbyte)moveX;
            f.AttackId = attackId;
            f.AttackHasHit = false;
            f.AttackDir = Vec2.Zero;
            f.ActionPaid = false;
            f.AttackFromAir = !f.Grounded;
            f.DashTicksLeft = 0;
            f.RestartAction(ActionState.Attack);
            events.Add(new SimEvent(tick, SimEventType.AttackStart, index, f.Position, new Vec2(f.Facing, 0f), attackId));
        }

        static void StartGrab(ref FighterState f, int moveX, int index, int tick, SimEventBuffer events)
        {
            if (moveX != 0)
                f.Facing = (sbyte)moveX;
            f.DashTicksLeft = 0;
            f.ThrowPending = false;
            f.ActionPaid = false;
            f.AttackBuffer = 0;
            f.AttackDir = Vec2.Zero;
            f.RestartAction(ActionState.Grab);
            events.Add(new SimEvent(tick, SimEventType.GrabStart, index, f.Position, new Vec2(f.Facing, 0f), 0f));
        }

        static void StartShoot(ref FighterState f, int moveX)
        {
            if (moveX != 0)
                f.Facing = (sbyte)moveX;
            f.DashTicksLeft = 0;
            f.ActionPaid = false;
            f.ShotPending = false;
            f.RestartAction(ActionState.Shoot);
        }

        /// <summary>
        /// 바위 발사 틱: 에너지가 모자라면 발사하지 않고 취소한다(4.4 규칙 4). 투사체는 이동이 끝난 뒤
        /// Projectiles.SpawnPending이 풀에 넣는다(발사 방향은 이번 틱의 8방향 입력, 중립이면 바라보는 쪽).
        /// </summary>
        static void Fire(ref FighterState f, in InputFrame input, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            int cost = CostFor(f, FighterForm.Rock, config);
            if (config.EnergyEnabled && !f.ActionPaid && f.Energy < cost)
            {
                f.RestartAction(ActionState.Airborne);
                events.Add(new SimEvent(tick, SimEventType.EnergyShort, index, f.Position, new Vec2(f.Facing, 0f), f.Energy));
                return;
            }
            Spend(ref f, cost, config);
            f.ShotPending = true;
            f.ShotDirection = input.MoveX == 0 && input.MoveY == 0
                ? new Vec2(f.Facing, 0f)
                : SimMath.Normalize(new Vec2(input.MoveX, input.MoveY));
        }

        static void StartCharge(ref FighterState f, int moveX)
        {
            if (moveX != 0)
                f.Facing = (sbyte)moveX;
            f.DashTicksLeft = 0;
            f.ActionPaid = false;
            f.RestartAction(ActionState.Charge);
        }

        /// <summary>조준(없으면 이동 입력)의 좌우. 0이면 바꾸지 않는다.</summary>
        static int AimFacing(in InputFrame input)
        {
            if (AimDirections.TryGet(input.Aim, out var aim))
                return aim.X > SimMath.Epsilon ? 1 : aim.X < -SimMath.Epsilon ? -1 : 0;
            return input.MoveX;
        }

        /// <summary>조준 방향. 조준이 없으면 8방향 입력, 그것도 없으면 바라보는 쪽.</summary>
        static Vec2 AimOrMove(in FighterState f, in InputFrame input)
        {
            if (AimDirections.TryGet(input.Aim, out var aim))
                return aim;
            return input.MoveX == 0 && input.MoveY == 0 ? new Vec2(f.Facing, 0f) : SimMath.Normalize(new Vec2(input.MoveX, input.MoveY));
        }

        /// <summary>
        /// 몸 폼 차징. 행동 틱 = 누른 뒤 지난 틱. 별도 Charge 입력의 구 기록은 T 전에 떼면 취소한다.
        /// 좌클릭은 T 전에 떼면 선딜 진행분을 이어받아 주먹/보유 무기 사용. T 이후는 뗀 틱의 조준으로 박치기(충전은 T부터 센다).
        /// 현재 규칙은 최소 충전에 못 미친 해제를 공격 없이 취소한다. 최대 충전 뒤 유지 시간이 지나면 자동 발사.
        /// </summary>
        static void BodyCharge(ref FighterState f, in InputFrame input, SimConfig config, int index, int tick, SimEventBuffer events,
            BrawlWeaponKind heldWeapon)
        {
            ref readonly var c = ref config.Combat;
            int t = c.Body.TapHoldTicks;
            int facing = AimFacing(input);
            if (facing != 0)
                f.Facing = (sbyte)facing;
            bool held = input.Held(c.Body.SeparateChargeInput ? InputButtons.Charge : InputButtons.Attack);
            if (held && f.ActionTick < t + c.Charge.FullChargeTicks + c.Charge.FullChargeHoldTicks)
                return;
            if (f.ActionTick < t)
            {
                if (c.Body.SeparateChargeInput)
                {
                    f.RestartAction(ActionState.Airborne);
                    return;
                }
                int elapsed = f.ActionTick;
                int attackId = c.Body.CombinedAttackInput && heldWeapon == BrawlWeaponKind.Pan ? AttackIds.Pan
                    : c.Body.CombinedAttackInput && heldWeapon == BrawlWeaponKind.Pistol ? AttackIds.Pistol : AttackIds.Punch;
                StartAttack(ref f, 0, attackId, index, tick, events);
                f.AttackDir = AimOrMove(f, input);
                // 권총 선딜은 0이므로 발사 틱(ActionTick 0)을 건너뛰지 않는다.
                f.ActionTick = SimMath.Min(elapsed, config.GetAttack(attackId).StartupTicks);
                return;
            }
            if (ChargeTicks(f, config) < c.Body.MinLungeChargeTicks)
            {
                f.RestartAction(ActionState.Airborne);
                f.AttackDir = Vec2.Zero;
                f.AttackBuffer = 0;
                return;
            }
            Launch(ref f, input, config, index, tick, events);
        }

        /// <summary>몸 폼이 최소 충전까지 마쳐 해제하면 박치기가 나가는지. 연출과 봇의 대응에 쓴다.</summary>
        public static bool IsLungeReady(in FighterState f, SimConfig config) =>
            f.Form == FighterForm.Body && f.Action == ActionState.Charge
            && f.ActionTick >= config.Combat.Body.TapHoldTicks + config.Combat.Body.MinLungeChargeTicks;

        /// <summary>충전 진행 틱(몸 폼은 T 이후부터). 연출의 충전 단계에 쓴다.</summary>
        public static int ChargeTicks(in FighterState f, SimConfig config) =>
            f.Form == FighterForm.Body ? SimMath.Max(0, f.ActionTick - config.Combat.Body.TapHoldTicks) : f.ActionTick;

        /// <summary>
        /// 돌진 발사: 충전 틱(행동 틱)에 따라 18 → 34, 방향은 이번 틱의 8방향 입력(중립이면 바라보는 쪽).
        /// 몸 폼(CR-004)은 조준 방향, 충전은 탭/홀드 임계 뒤부터 센다.
        /// </summary>
        static void Launch(ref FighterState f, in InputFrame input, SimConfig config, int index, int tick, SimEventBuffer events)
        {
            // 소비는 발사할 때 한 번. 발사 틱에 모자라면 발사하지 않고 취소하며 소비하지 않는다(4.4 규칙 4).
            int cost = CostFor(f, FighterForm.Scissors, config);
            if (config.EnergyEnabled && !f.ActionPaid && f.Energy < cost)
            {
                f.RestartAction(ActionState.Airborne);
                events.Add(new SimEvent(tick, SimEventType.EnergyShort, index, f.Position, new Vec2(f.Facing, 0f), f.Energy));
                return;
            }
            Spend(ref f, cost, config);
            // 증강(5.4): 즉발 돌진은 고정 속도, 속도 배율은 충전형·즉발 모두에 곱한다.
            var aug = Augments.Compute(f.Augments, config);
            float speed = (aug.InstantLunge ? config.Combat.Charge.InstantLungeSpeed : config.Combat.Charge.LungeSpeed(ChargeTicks(f, config))) * aug.LungeSpeedMul;
            if (aug.InstantLunge)
                events.Add(new SimEvent(tick, SimEventType.AugmentActive, index, f.Position, Vec2.Zero, aug.LaunchSlotOwner));
            if (aug.LungeSpeedMul != 1f)
                events.Add(new SimEvent(tick, SimEventType.AugmentActive, index, f.Position, Vec2.Zero, AugmentCatalog.LungeSpeed));
            var direction = AimOrMove(f, input);
            int facing = AimFacing(input);
            if (facing != 0)
                f.Facing = (sbyte)facing;
            f.Velocity = direction * speed;
            f.LungeBounces = 0;
            f.JumpCutEligible = false;
            if (f.Velocity.Y > 0f)
            {
                f.Grounded = false;
                f.GroundBox = FighterState.NoBox;
            }
            f.RestartAction(ActionState.Lunge);
            events.Add(new SimEvent(tick, SimEventType.LungeStart, index, f.Position, direction, speed));
        }

        /// <summary>공격 판정이 활성 틱인지.</summary>
        public static bool IsHitboxActive(in FighterState f, SimConfig config)
        {
            if (f.Action != ActionState.Attack || f.AttackId == FighterState.NoAttack)
                return false;
            ref readonly var attack = ref config.GetAttack(f.AttackId);
            int k = f.ActionTick - attack.StartupTicks;
            return k >= 0 && k < attack.ActiveTicks;
        }

        /// <summary>활성 판정 원의 월드 위치. 활성 첫 틱에 Start, 가운데에 Mid, 마지막 틱에 End.</summary>
        public static Vec2 CircleCenter(in FighterState f, in AttackDef attack, in HitCircle circle)
        {
            int k = f.ActionTick - attack.StartupTicks;
            float t = attack.ActiveTicks > 1 ? (float)k / (attack.ActiveTicks - 1) : 0f;
            return f.Position + AttackOffset(f, circle.At(t));
        }

        /// <summary>
        /// 공격 기준 좌표(앞 +x, 위 +y) → 월드 오프셋. 조준 공격(AttackDir)은 x축을 조준 방향으로 돌리고 y축은 화면 위쪽에 가까운 수직으로 잡아
        /// 좌우 대칭이 그대로 유지된다. 그 외는 바라보는 쪽으로 좌우만 뒤집는다.
        /// </summary>
        public static Vec2 AttackOffset(in FighterState f, Vec2 local)
        {
            var d = f.AttackDir;
            if (d.X == 0f && d.Y == 0f)
                return new Vec2(local.X * f.Facing, local.Y);
            var up = d.X >= 0f ? new Vec2(-d.Y, d.X) : new Vec2(d.Y, -d.X);
            return d * local.X + up * local.Y;
        }

        /// <summary>몸 충돌의 공격성 가중치: 가위 폼에서 돌진 1.0, 대시는 튜닝 값(CR-003 기본 0), 그 외 0.</summary>
        public static float Aggression(in FighterState f, SimConfig config)
        {
            if (f.Form != FighterForm.Scissors && f.Form != FighterForm.Body)
                return 0f;
            if (f.Action == ActionState.Lunge)
                return config.Combat.Charge.LungeAggression;
            if (f.Action == ActionState.Dash && f.DashTicksLeft > 0)
                return config.Combat.Charge.DashAggression;
            return 0f;
        }

        /// <summary>법선 쪽 속도 × 가중치. 상대에게서 멀어지는 속도는 0.</summary>
        public static float Power(in FighterState f, Vec2 towardOther, SimConfig config) =>
            SimMath.Max(0f, Vec2.Dot(f.Velocity, towardOther)) * Aggression(f, config);

        /// <summary>공격을 받을 수 있는지. KO, 그리고 연속 명중 상한에 닿은 피격 경직 중에는 맞지 않는다.</summary>
        public static bool CanBeHit(in FighterState f, SimConfig config) =>
            f.Action != ActionState.KO && !(f.IsLaunched && f.ComboHits >= config.Combat.MaxComboHits);

        /// <summary>잡을 수 있는지. 피격 경직, 던져지는 중, KO, 이미 붙잡힌 상대는 잡히지 않는다. 비틀거림은 잡힌다.</summary>
        public static bool CanBeGrabbed(in FighterState f) =>
            f.Action != ActionState.Hitstun && f.Action != ActionState.Thrown && f.Action != ActionState.KO
            && f.Action != ActionState.Held && f.Action != ActionState.Holding && f.GrabProtectTicks == 0;

        /// <summary>잡기 상자가 활성인 틱인지.</summary>
        public static bool IsGrabActive(in FighterState f, SimConfig config)
        {
            if (f.Action != ActionState.Grab)
                return false;
            ref readonly var g = ref config.Combat.Grab;
            return f.ActionTick >= g.StartupTicks && f.ActionTick < g.StartupTicks + g.ActiveTicks;
        }

        /// <summary>잡기 시작 또는 판정 중(헛잡기 후딜 전). 이때 잡히면 잡기 풀기가 된다.</summary>
        public static bool IsGrabbing(in FighterState f, SimConfig config) =>
            f.Action == ActionState.Grab && f.ActionTick < config.Combat.Grab.StartupTicks + config.Combat.Grab.ActiveTicks;

        /// <summary>잡기 상자(몸 앞 0.65, 폭 0.7, 높이 0.9)의 월드 중심.</summary>
        public static Vec2 GrabBoxCenter(in FighterState f, SimConfig config)
        {
            // 몸 폼(CR-004): 잡기를 시작한 틱의 커서 방향(AttackDir) 쪽. 그 외는 바라보는 쪽.
            var d = f.AttackDir;
            if (d.X != 0f || d.Y != 0f)
                return f.Position + d * config.Combat.Grab.BoxOffset;
            return new Vec2(f.Position.X + config.Combat.Grab.BoxOffset * f.Facing, f.Position.Y);
        }

        static bool GrabTouches(in FighterState grabber, in FighterState target, SimConfig config)
        {
            ref readonly var g = ref config.Combat.Grab;
            var center = GrabBoxCenter(grabber, config);
            float half = config.HalfBody;
            return SimMath.Abs(target.Position.X - center.X) < g.BoxWidth * 0.5f + half
                && SimMath.Abs(target.Position.Y - center.Y) < g.BoxHeight * 0.5f + half;
        }

        static bool TouchesBody(in FighterState attacker, SimConfig config, Vec2 target, out Vec2 point)
        {
            ref readonly var attack = ref config.GetAttack(attacker.AttackId);
            float half = config.HalfBody;
            for (int i = 0; i < attack.UsedCircles; i++)
            {
                var circle = attack.GetCircle(i);
                var center = CircleCenter(attacker, attack, circle);
                var closest = new Vec2(
                    SimMath.Clamp(center.X, target.X - half, target.X + half),
                    SimMath.Clamp(center.Y, target.Y - half, target.Y + half));
                if ((closest - center).LengthSquared <= circle.Radius * circle.Radius)
                {
                    point = closest;
                    return true;
                }
            }
            point = default;
            return false;
        }

        static bool CirclesTouch(in FighterState a, in FighterState b, SimConfig config, out Vec2 point)
        {
            ref readonly var attackA = ref config.GetAttack(a.AttackId);
            ref readonly var attackB = ref config.GetAttack(b.AttackId);
            for (int i = 0; i < attackA.UsedCircles; i++)
            {
                var circleA = attackA.GetCircle(i);
                var centerA = CircleCenter(a, attackA, circleA);
                for (int j = 0; j < attackB.UsedCircles; j++)
                {
                    var circleB = attackB.GetCircle(j);
                    var centerB = CircleCenter(b, attackB, circleB);
                    float reach = circleA.Radius + circleB.Radius;
                    if ((centerB - centerA).LengthSquared <= reach * reach)
                    {
                        point = (centerA + centerB) * 0.5f;
                        return true;
                    }
                }
            }
            point = default;
            return false;
        }

        /// <summary>
        /// 상호작용 판정. 이동 후 스냅숏에서 양쪽 공격을 모두 계산한다.
        /// 공격 판정 원: 판정 원끼리 닿거나 서로의 몸을 동시에 맞히면 클래시, 한쪽만 맞히면 받는 쪽 상태에 따라 패링·가드·명중.
        /// 몸 충돌(모드 A): 위력이 둘 다 기준 미만이면 밀어내기만, 둘 다 기준 이상이고 차이가 작으면 클래시, 그 외 높은 쪽 승리.
        /// 결과가 서로 배타적이므로 스냅숏에서 계산해 적용하면 동시 적용과 같다.
        /// </summary>
        /// <remarks>
        /// 순서: 던지기(붙잡은 쪽이 놓기로 한 경우) → 판정 원 → 몸 충돌 → 잡기. 앞 단계에서 결과가 나면 뒤 단계는 하지 않으므로
        /// 상성표(03-combat.md 2절)의 "공격이 닿으면 공격 승, 아니면 잡힘"이 그대로 성립한다.
        /// </remarks>
        public static void ResolveInteractions(ref FighterState a, ref FighterState b, bool frozenA, bool frozenB, in BodyContact contact, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            if (a.ThrowPending)
            {
                Throw(ref a, ref b, 1, config, arena, tick, events);
                return;
            }
            if (b.ThrowPending)
            {
                Throw(ref b, ref a, 0, config, arena, tick, events);
                return;
            }
            if (config.Combat.Body.Enabled)
            {
                // 잡은 채로 움직이면 잡힌 쪽이 따라온다(5.2).
                if (a.Action == ActionState.Holding && b.Action == ActionState.Held)
                    KeepHeld(a, ref b, config, arena);
                else if (b.Action == ActionState.Holding && a.Action == ActionState.Held)
                    KeepHeld(b, ref a, config, arena);
                // 받아내기(5.2): 잡기 판정이 박치기 중인 상대와 겹치면 잡기 성공. 같은 틱의 박치기 타격보다 먼저 처리한다.
                if (Catch(ref a, ref b, frozenA, frozenB, config, arena, tick, events))
                    return;
            }
            var snapA = a;
            var snapB = b;
            if (ResolveHitboxes(ref a, ref b, snapA, snapB, frozenA, frozenB, config, tick, events))
                return;
            if (!frozenA && !frozenB && contact.Touching && ResolveBodies(ref a, ref b, snapA, snapB, contact, config, tick, events))
                return;
            ResolveGrabs(ref a, ref b, snapA, snapB, frozenA, frozenB, config, arena, tick, events);
        }

        public static void ResolveInteractions(ref FighterState a, ref FighterState b, bool frozenA, bool frozenB, SimConfig config, int tick, SimEventBuffer events)
            => ResolveInteractions(ref a, ref b, frozenA, frozenB, default, config, null, tick, events);

        /// <summary>
        /// 잡기: 활성 잡기 상자가 잡을 수 있는 상대에게 닿으면 붙잡는다. 서로 닿거나, 상대가 잡기 시작·판정 중이면 잡기 풀기.
        /// </summary>
        static void ResolveGrabs(ref FighterState a, ref FighterState b, in FighterState snapA, in FighterState snapB, bool frozenA, bool frozenB, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            bool aGrabs = !frozenA && IsGrabActive(snapA, config) && CanBeGrabbed(snapB) && GrabTouches(snapA, snapB, config);
            bool bGrabs = !frozenB && IsGrabActive(snapB, config) && CanBeGrabbed(snapA) && GrabTouches(snapB, snapA, config);
            if (!aGrabs && !bGrabs)
                return;
            if ((aGrabs && bGrabs) || (aGrabs && IsGrabbing(snapB, config)) || (bGrabs && IsGrabbing(snapA, config)))
            {
                GrabBreak(ref a, ref b, snapA, snapB, config, tick, events);
                return;
            }
            if (aGrabs)
                Hold(ref a, ref b, 1, config, arena, tick, events);
            else
                Hold(ref b, ref a, 0, config, arena, tick, events);
        }

        /// <summary>
        /// 받아내기(CR-004 5.2): 활성 잡기 상자가 박치기(Lunge) 중인 상대에 닿으면 붙잡는다. 위력 조건은 없다.
        /// 잡은 쪽은 제자리, 잡힌 쪽은 속도 0(9절 임시안). 한 파이터는 잡기와 박치기를 동시에 할 수 없으므로 양쪽 성공은 없다.
        /// </summary>
        static bool Catch(ref FighterState a, ref FighterState b, bool frozenA, bool frozenB, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            bool aCatches = !frozenA && IsGrabActive(a, config) && b.Action == ActionState.Lunge && CanBeGrabbed(b) && GrabTouches(a, b, config);
            bool bCatches = !frozenB && IsGrabActive(b, config) && a.Action == ActionState.Lunge && CanBeGrabbed(a) && GrabTouches(b, a, config);
            if (aCatches)
                CatchAndThrow(ref a, ref b, 1, config, arena, tick, events);
            else if (bCatches)
                CatchAndThrow(ref b, ref a, 0, config, arena, tick, events);
            else
                return false;
            return true;
        }

        /// <summary>
        /// 받아내기 성공(단계 C 피드백): 붙잡고 바로 박치기의 진행 방향 그대로 등 뒤로 넘겨 내던진다(내 쪽으로 오는 반동을 이용하는 패링 느낌).
        /// 날아온 쪽을 바라보고 잡으므로 뒤로 던지기가 되어 잡힌 쪽은 등 뒤로 옮겨진 뒤 날아간다. 두 파이터 모두 짧은 히트스탑으로 멈췄다가 날아간다.
        /// </summary>
        static void CatchAndThrow(ref FighterState catcher, ref FighterState lunger, int lungerIndex, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            var onward = SimMath.Normalize(lunger.Velocity);
            if (onward.LengthSquared == 0f)
                onward = SimMath.Normalize(catcher.Position - lunger.Position);
            if (onward.LengthSquared == 0f)
                onward = new Vec2(-catcher.Facing, 0f);
            // 날아온 쪽(진행 방향의 반대)을 바라보고 잡는다.
            if (onward.X > SimMath.Epsilon)
                catcher.Facing = -1;
            else if (onward.X < -SimMath.Epsilon)
                catcher.Facing = 1;
            Hold(ref catcher, ref lunger, lungerIndex, config, arena, tick, events);
            events.Add(new SimEvent(tick, SimEventType.LungeCaught, lungerIndex, lunger.Position, onward, 0f));
            catcher.ThrowDirection = onward;
            Throw(ref catcher, ref lunger, lungerIndex, config, arena, tick, events);
            int stop = config.Combat.Body.CatchHitstop;
            catcher.HitstopTicks = SimMath.Max(catcher.HitstopTicks, stop);
            lunger.HitstopTicks = SimMath.Max(lunger.HitstopTicks, stop);
        }

        /// <summary>잡힌 쪽을 잡은 쪽 몸 앞에 다시 놓는다(잡은 쪽이 이동했을 때).</summary>
        static void KeepHeld(in FighterState grabber, ref FighterState victim, SimConfig config, ArenaDef arena)
        {
            victim.Position = new Vec2(grabber.Position.X + config.Combat.Grab.HoldOffset * grabber.Facing, grabber.Position.Y);
            victim.Velocity = Vec2.Zero;
            if (arena != null)
            {
                TerrainCollision.ResolveX(ref victim.Position, grabber.Facing, config.HalfBody, arena, grabber.Position.X);
                TerrainCollision.ClampToBoundary(ref victim.Position, config.HalfBody, arena);
            }
        }

        /// <summary>붙잡기: 상대를 몸 앞 0.8에 고정하고 둘 다 멈춘다.</summary>
        static void Hold(ref FighterState grabber, ref FighterState victim, int victimIndex, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            ref readonly var g = ref config.Combat.Grab;
            grabber.RestartAction(ActionState.Holding);
            grabber.Velocity = Vec2.Zero;
            grabber.ThrowPending = false;
            victim.StunTicks = 0;
            victim.ComboHits = 0;
            EnterStun(ref victim, ActionState.Held);
            victim.Velocity = Vec2.Zero;
            victim.Position = new Vec2(grabber.Position.X + g.HoldOffset * grabber.Facing, grabber.Position.Y);
            if (arena != null)
            {
                TerrainCollision.ResolveX(ref victim.Position, grabber.Facing, config.HalfBody, arena, grabber.Position.X);
                TerrainCollision.ClampToBoundary(ref victim.Position, config.HalfBody, arena);
            }
            events.Add(new SimEvent(tick, SimEventType.Grab, victimIndex, victim.Position, new Vec2(grabber.Facing, 0f), 0f));
        }

        /// <summary>던지기: 속도 28, 데미지 6, 24틱 던져진 상태. 뒤로 던지면 상대를 등 뒤로 옮겨서 던진다.</summary>
        static void Throw(ref FighterState thrower, ref FighterState victim, int victimIndex, SimConfig config, ArenaDef arena, int tick, SimEventBuffer events)
        {
            ref readonly var g = ref config.Combat.Grab;
            var direction = thrower.ThrowDirection;
            thrower.ThrowPending = false;
            thrower.RestartAction(ActionState.Airborne);
            if (victim.Action != ActionState.Held)
                return;
            if (direction.X == 0f && direction.Y == 0f)
            {
                // 놓기(몸 폼의 최대 유지 시간 초과): 피해·속도 없이 둘 다 풀려난다.
                victim.Velocity = Vec2.Zero;
                victim.RestartAction(ActionState.Airborne);
                events.Add(new SimEvent(tick, SimEventType.GrabRelease, victimIndex, victim.Position, Vec2.Zero, 0f));
                return;
            }
            if (direction.X * thrower.Facing < 0f)
            {
                victim.Position = new Vec2(thrower.Position.X - g.BackThrowOffset * thrower.Facing, thrower.Position.Y);
                if (arena != null)
                {
                    TerrainCollision.ResolveX(ref victim.Position, -thrower.Facing, config.HalfBody, arena, thrower.Position.X);
                    TerrainCollision.ClampToBoundary(ref victim.Position, config.HalfBody, arena);
                }
            }
            victim.Velocity = direction * g.ThrowSpeed;
            victim.Health = SimMath.Max(0, victim.Health - g.ThrowDamage);
            victim.StunTicks = g.ThrownTicks;
            // 던지기를 연속 명중의 첫 타로 센다(이어지는 공격은 감쇠를 받고 상한에 걸린다).
            victim.ComboHits = 1;
            EnterStun(ref victim, ActionState.Thrown);
            events.Add(new SimEvent(tick, SimEventType.Throw, victimIndex, victim.Position, direction, g.ThrowDamage));
        }

        /// <summary>잡기 풀기: 둘 다 반대로 10만큼 튕기고 12틱 반동, 히트스탑 6.</summary>
        static void GrabBreak(ref FighterState a, ref FighterState b, in FighterState snapA, in FighterState snapB, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var g = ref config.Combat.Grab;
            var n = SimMath.Normalize(snapB.Position - snapA.Position);
            if (n.LengthSquared == 0f)
                n = new Vec2(0f, 1f);
            a.Velocity = -n * g.TechSpeed;
            b.Velocity = n * g.TechSpeed;
            a.StunTicks = g.TechRecoilTicks;
            b.StunTicks = g.TechRecoilTicks;
            EnterStun(ref a, ActionState.Recoil);
            EnterStun(ref b, ActionState.Recoil);
            a.HitstopTicks = g.TechHitstop;
            b.HitstopTicks = g.TechHitstop;
            events.Add(new SimEvent(tick, SimEventType.GrabBreak, SimEvent.NoFighter, (snapA.Position + snapB.Position) * 0.5f, n, g.TechSpeed));
        }

        static bool ResolveHitboxes(ref FighterState a, ref FighterState b, in FighterState snapA, in FighterState snapB, bool frozenA, bool frozenB, SimConfig config, int tick, SimEventBuffer events)
        {
            bool activeA = !frozenA && !snapA.AttackHasHit && IsHitboxActive(snapA, config);
            bool activeB = !frozenB && !snapB.AttackHasHit && IsHitboxActive(snapB, config);
            if (!activeA && !activeB)
                return false;

            Vec2 pointAB = default, pointBA = default;
            bool aHitsB = activeA && CanBeHit(snapB, config) && TouchesBody(snapA, config, snapB.Position, out pointAB);
            bool bHitsA = activeB && CanBeHit(snapA, config) && TouchesBody(snapB, config, snapA.Position, out pointBA);
            // 돌진 → 근접 활성 판정(4.1): 돌진 위력이 임계 이상이면 돌진이 이긴다. 근접공격은 중단되고 피해를 받으며 돌진은 계속된다.
            // 임계 미만이면 돌진은 공격성이 없는 것으로 보고 아래 규칙대로 판정이 그대로 맞는다. 한 파이터는 돌진과 근접공격을 동시에 할 수 없다.
            if (bHitsA && !frozenA && OverpowersMelee(snapA, snapB, config, out float powerA))
            {
                LungeBreaksMelee(ref a, ref b, snapA, snapB, powerA, pointBA, 1, config, tick, events);
                return true;
            }
            if (aHitsB && !frozenB && OverpowersMelee(snapB, snapA, config, out float powerB))
            {
                LungeBreaksMelee(ref b, ref a, snapB, snapA, powerB, pointAB, 0, config, tick, events);
                return true;
            }
            if (activeA && activeB && CirclesTouch(snapA, snapB, config, out var clashPoint))
            {
                Clash(ref a, ref b, snapA, snapB, clashPoint, config.Combat.ClashSpeed, config, tick, events);
                return true;
            }
            if (aHitsB && bHitsA)
            {
                Clash(ref a, ref b, snapA, snapB, (pointAB + pointBA) * 0.5f, config.Combat.ClashSpeed, config, tick, events);
                return true;
            }
            if (aHitsB)
            {
                HitboxStrike(ref a, ref b, snapA, snapB, pointAB, 1, config, tick, events);
                return true;
            }
            if (bHitsA)
            {
                HitboxStrike(ref b, ref a, snapB, snapA, pointBA, 0, config, tick, events);
                return true;
            }
            return false;
        }

        /// <summary>돌진 중이고 근접공격 쪽으로의 위력이 임계 이상인지.</summary>
        static bool OverpowersMelee(in FighterState lunger, in FighterState melee, SimConfig config, out float power)
        {
            power = 0f;
            if (lunger.Action != ActionState.Lunge)
                return false;
            var toward = melee.Position - lunger.Position;
            toward = toward.LengthSquared > 0f ? SimMath.Normalize(toward) : new Vec2(0f, 1f);
            power = Power(lunger, toward, config);
            return power >= config.Combat.Charge.PowerThreshold;
        }

        /// <summary>돌진이 근접공격을 이김: 몸 충돌 승리와 같은 피해·넉백을 주되, 돌진은 튕겨 나오지 않고 계속된다.</summary>
        static void LungeBreaksMelee(ref FighterState lunger, ref FighterState melee, in FighterState snapLunger, in FighterState snapMelee,
            float power, Vec2 point, int meleeIndex, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var p = ref config.Combat.Charge;
            var toward = SimMath.Normalize(snapMelee.Position - snapLunger.Position);
            BodyStrikeValues(snapLunger.Velocity, toward, power, p, out int damage, out var direction, out float knockback);
            var strike = new Strike { Damage = damage, Direction = direction, Knockback = knockback, Point = point };
            events.Add(new SimEvent(tick, SimEventType.AttackInterrupted, meleeIndex, snapMelee.Position, direction, power));
            ApplyStrike(ref lunger, ref melee, snapLunger, snapMelee, strike, meleeIndex, config, tick, events);
            melee.AttackId = FighterState.NoAttack;
            melee.AttackHasHit = false;
            // 한 돌진은 이긴 상대를 다시 치지 않는다(계속 날아가도 같은 돌진으로 두 번 맞히지 않게, S-152).
            lunger.ContactCooldown = SimMath.Max(p.ContactCooldownTicks, p.LungeMaxTicks);
            melee.ContactCooldown = lunger.ContactCooldown;
        }

        static void HitboxStrike(ref FighterState attacker, ref FighterState defender, in FighterState snapAttacker, in FighterState snapDefender, Vec2 point, int defenderIndex, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var attack = ref config.GetAttack(snapAttacker.AttackId);
            var direction = AttackOffset(snapAttacker, attack.KnockbackDirection);
            attacker.AttackHasHit = true;
            var strike = new Strike
            {
                Damage = attack.Damage,
                Direction = direction,
                Knockback = attack.KnockbackStrength,
                Point = point,
            };
            var outcome = ApplyStrike(ref attacker, ref defender, snapAttacker, snapDefender, strike, defenderIndex, config, tick, events);
            // 적중 트리거 증강(5.4, 보의 에너지 환급).
            if (outcome == SimEventType.Hit && attacker.Augments.Count > 0)
                Augments.RunTriggers(ref attacker, 1 - defenderIndex, AugmentTrigger.OnMeleeHit, config, tick, events);
            if (outcome == SimEventType.Hit && attack.PogoSpeed > 0f)
            {
                // 내려찍기 포고 튕김: 히트스탑이 끝나면 위로 튀어 오르고 공중 점프·대시를 되찾는다.
                attacker.Velocity.Y = attack.PogoSpeed;
                attacker.Grounded = false;
                attacker.GroundBox = FighterState.NoBox;
                attacker.AirJumpsLeft = config.Movement.AirJumps;
                attacker.DashCharges = config.Movement.DashCharges;
                attacker.JumpCutEligible = false;
            }
        }

        /// <summary>
        /// 모드 A 몸 충돌(03-combat.md 4절). n = A → B, 위력A = max(0, A.vel·n) × 가중치A, 위력B = max(0, B.vel·(-n)) × 가중치B.
        /// 같은 두 파이터 사이의 판정은 결과가 난 뒤 10틱 동안 다시 하지 않는다.
        /// </summary>
        /// <summary>결과(클래시·명중·가드·패링)가 났으면 true.</summary>
        static bool ResolveBodies(ref FighterState a, ref FighterState b, in FighterState snapA, in FighterState snapB, in BodyContact contact, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var p = ref config.Combat.Charge;
            if (snapA.ContactCooldown > 0 || snapB.ContactCooldown > 0)
                return false;
            var n = contact.Normal;
            float powerA = Power(snapA, n, config);
            float powerB = Power(snapB, -n, config);
            if (powerA <= 0f && powerB <= 0f)
                return false;
            events.Add(new SimEvent(tick, SimEventType.PowerCheck, 0, snapA.Position, n, powerA));
            events.Add(new SimEvent(tick, SimEventType.PowerCheck, 1, snapB.Position, -n, powerB));

            switch (JudgeBodies(powerA, powerB, p))
            {
                case BodyOutcome.Clash:
                    float speed = SimMath.Max(config.Combat.ClashSpeed, SimMath.Max(powerA, powerB) * p.ClashSpeedPerPower);
                    Clash(ref a, ref b, snapA, snapB, contact.Point, speed, config, tick, events);
                    a.ContactCooldown = p.ContactCooldownTicks;
                    b.ContactCooldown = p.ContactCooldownTicks;
                    return true;
                case BodyOutcome.FirstWins:
                    return BodyStrike(ref a, ref b, snapA, snapB, n, powerA, contact.Point, 1, config, tick, events);
                case BodyOutcome.SecondWins:
                    return BodyStrike(ref b, ref a, snapB, snapA, -n, powerB, contact.Point, 0, config, tick, events);
            }
            return false;
        }

        /// <summary>
        /// 둘 다 위력 6 미만이면 밀어내기만, 둘 다 6 이상이고 차이가 4 미만이면 클래시, 그 외 위력이 높은 쪽 승리.
        /// 위력이 같으면 앞의 두 규칙 중 하나에 걸리므로 번호로 승자를 정하는 일이 없다.
        /// </summary>
        public static BodyOutcome JudgeBodies(float powerA, float powerB, in ChargeModeParams p)
        {
            bool strongA = powerA >= p.PowerThreshold;
            bool strongB = powerB >= p.PowerThreshold;
            if (!strongA && !strongB)
                return BodyOutcome.None;
            if (strongA && strongB && SimMath.Abs(powerA - powerB) < p.ClashPowerMargin)
                return BodyOutcome.Clash;
            return powerA > powerB ? BodyOutcome.FirstWins : BodyOutcome.SecondWins;
        }

        /// <summary>
        /// 이긴 쪽 W가 진 쪽 L에게 주는 것: 데미지 clamp(round(위력 × 0.6), 4, 24),
        /// 넉백 방향 normalize(0.5 × normalize(W.vel) + 0.5 × n(W → L)), 세기 max(10, 위력 × 0.9).
        /// </summary>
        public static void BodyStrikeValues(Vec2 winnerVelocity, Vec2 towardLoser, float power, in ChargeModeParams p, out int damage, out Vec2 direction, out float knockback)
        {
            damage = SimMath.Clamp(SimMath.RoundToInt(power * p.DamagePerPower), p.MinDamage, p.MaxDamage);
            direction = SimMath.Normalize(SimMath.Normalize(winnerVelocity) * 0.5f + towardLoser * 0.5f);
            if (direction.LengthSquared == 0f)
                direction = towardLoser;
            knockback = SimMath.Max(p.MinKnockback, power * p.KnockbackPerPower);
        }

        static bool BodyStrike(ref FighterState winner, ref FighterState loser, in FighterState snapWinner, in FighterState snapLoser, Vec2 towardLoser, float power, Vec2 point, int loserIndex, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var p = ref config.Combat.Charge;
            if (!CanBeHit(snapLoser, config))
                return false;
            BodyStrikeValues(snapWinner.Velocity, towardLoser, power, p, out int damage, out var direction, out float knockback);
            var strike = new Strike
            {
                Damage = damage,
                Direction = direction,
                Knockback = knockback,
                Point = point,
                Body = true,
                ContactNormal = towardLoser,
            };
            ApplyStrike(ref winner, ref loser, snapWinner, snapLoser, strike, loserIndex, config, tick, events);
            winner.ContactCooldown = p.ContactCooldownTicks;
            loser.ContactCooldown = p.ContactCooldownTicks;
            return true;
        }

        /// <summary>한쪽이 다른 쪽을 친 결과. 판정 원 공격과 몸 충돌이 같은 적용 규칙을 쓴다.</summary>
        struct Strike
        {
            public int Damage;
            public Vec2 Direction;
            public float Knockback;
            public Vec2 Point;
            /// <summary>몸 충돌(돌진·대시)이면 이긴 쪽이 튕겨 나오고 돌진이 끝나며, 패링당하면 반사된다.</summary>
            public bool Body;
            /// <summary>몸 충돌의 접촉 법선(이긴 쪽 → 진 쪽).</summary>
            public Vec2 ContactNormal;
        }

        /// <summary>결과(Parry, GuardHit, Hit)를 돌려준다.</summary>
        static SimEventType ApplyStrike(ref FighterState attacker, ref FighterState defender, in FighterState snapAttacker, in FighterState snapDefender, in Strike strike, int defenderIndex, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;

            if (snapDefender.Action == ActionState.Guard && snapDefender.ParryTicks > 0)
            {
                // 패링: 공격한 쪽은 비틀거린다. 판정 원 공격은 반대로 8 밀려나고, 돌진은 접촉 법선 기준 반사 × 0.8.
                int away = SimMath.Sign(snapAttacker.Position.X - snapDefender.Position.X);
                if (away == 0)
                    away = -snapAttacker.Facing;
                if (strike.Body)
                {
                    var v = snapAttacker.Velocity;
                    var nrm = strike.ContactNormal;
                    attacker.Velocity = (v - nrm * (2f * Vec2.Dot(v, nrm))) * c.Charge.ParryReflectScale;
                }
                else
                {
                    attacker.Velocity = new Vec2(away * c.StaggerPushSpeed, 0f);
                }
                attacker.StunTicks = c.StaggerTicks;
                EnterStun(ref attacker, ActionState.Stagger);
                defender.ParryTicks = 0;
                attacker.HitstopTicks = c.ParryHitstop;
                defender.HitstopTicks = c.ParryHitstop;
                events.Add(new SimEvent(tick, SimEventType.Parry, defenderIndex, strike.Point, new Vec2(away, 0f), strike.Damage));
                return SimEventType.Parry;
            }

            int hitstop = HitstopFor(strike.Damage, c);
            if (snapDefender.IsGuarding)
            {
                // 가드: 데미지 × 0.2, 넉백 × 0.35(수평만), 가드 경직, 히트스탑 절반.
                int damage = SimMath.Max(1, SimMath.RoundToInt(strike.Damage * c.GuardDamageMultiplier));
                defender.Health = SimMath.Max(0, defender.Health - damage);
                defender.Velocity.X = strike.Direction.X * (strike.Knockback * c.GuardKnockbackMultiplier);
                defender.StunTicks = c.GuardStunTicks;
                EnterStun(ref defender, ActionState.GuardStun);
                int guardHitstop = SimMath.RoundToInt(hitstop * c.GuardHitstopMultiplier);
                attacker.HitstopTicks = guardHitstop;
                defender.HitstopTicks = guardHitstop;
                if (strike.Body)
                    Rebound(ref attacker, snapAttacker, config);
                events.Add(new SimEvent(tick, SimEventType.GuardHit, defenderIndex, strike.Point, strike.Direction, damage));
                return SimEventType.GuardHit;
            }

            // 명중: 넉백은 속도를 대체한다. 경직이 풀리기 전에 다시 맞으면 넉백 × 0.85^n, 경직 × 0.8^n.
            int n = snapDefender.IsLaunched ? snapDefender.ComboHits : 0;
            float knockback = strike.Knockback * SimMath.Pow(c.ComboKnockbackDecay, n);
            defender.Velocity = strike.Direction * knockback;
            defender.Health = SimMath.Max(0, defender.Health - strike.Damage);
            defender.StunTicks = HitstunFor(knockback, n, c);
            defender.ComboHits = n + 1;
            EnterStun(ref defender, ActionState.Hitstun);
            attacker.HitstopTicks = hitstop;
            defender.HitstopTicks = hitstop;
            if (strike.Body)
                Rebound(ref attacker, snapAttacker, config);
            events.Add(new SimEvent(tick, SimEventType.Hit, defenderIndex, strike.Point, strike.Direction, strike.Damage));
            return SimEventType.Hit;
        }

        /// <summary>
        /// 투사체에 맞음(CP19): 공통 틀(가드면 가드 규칙, 아니면 명중·연속 명중 감쇠·경직). 쏜 쪽은 멀리 있으므로 히트스탑은 맞은 쪽만.
        /// 패링 구간도 가드로 받는다(되돌려 비틀거리게 할 공격자가 곁에 없다).
        /// </summary>
        public static void ProjectileHit(ref FighterState defender, int defenderIndex, int damage, Vec2 direction, float knockback, Vec2 point,
            SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            int hitstop = HitstopFor(damage, c);
            if (defender.IsGuarding)
            {
                int guarded = SimMath.Max(1, SimMath.RoundToInt(damage * c.GuardDamageMultiplier));
                defender.Health = SimMath.Max(0, defender.Health - guarded);
                defender.Velocity.X = direction.X * (knockback * c.GuardKnockbackMultiplier);
                defender.ParryTicks = 0;
                defender.StunTicks = c.GuardStunTicks;
                EnterStun(ref defender, ActionState.GuardStun);
                defender.HitstopTicks = SimMath.RoundToInt(hitstop * c.GuardHitstopMultiplier);
                events.Add(new SimEvent(tick, SimEventType.GuardHit, defenderIndex, point, direction, guarded));
                return;
            }
            bool lunging = defender.Action == ActionState.Lunge;
            int n = defender.IsLaunched ? defender.ComboHits : 0;
            float k = knockback * SimMath.Pow(c.ComboKnockbackDecay, n);
            defender.Velocity = direction * k;
            defender.Health = SimMath.Max(0, defender.Health - damage);
            defender.StunTicks = HitstunFor(k, n, c);
            defender.ComboHits = n + 1;
            EnterStun(ref defender, ActionState.Hitstun);
            defender.HitstopTicks = hitstop;
            events.Add(new SimEvent(tick, SimEventType.Hit, defenderIndex, point, direction, damage));
            if (lunging)
            {
                // 돌진 중에 맞으면(4.1): 돌진이 끊기고 관성을 잃은 채 짧게 기절한다.
                defender.Velocity = defender.Velocity * c.Rock.LungeStunVelocityScale;
                defender.ComboHits = 0;
                defender.StunTicks = c.Rock.LungeStunTicks;
                EnterStun(ref defender, ActionState.Stagger);
                events.Add(new SimEvent(tick, SimEventType.LungeStun, defenderIndex, defender.Position, direction, c.Rock.LungeStunTicks));
            }
        }

        /// <summary>몸 충돌에서 이긴 쪽: 속도 × -0.25로 살짝 튕겨 나오고 돌진(대시)이 끝난다.</summary>
        static void Rebound(ref FighterState winner, in FighterState snapWinner, SimConfig config)
        {
            winner.Velocity = snapWinner.Velocity * config.Combat.Charge.WinnerVelocityScale;
            winner.DashTicksLeft = 0;
            if (winner.Action == ActionState.Lunge || winner.Action == ActionState.Dash)
                winner.RestartAction(ActionState.Airborne);
        }

        static void Clash(ref FighterState a, ref FighterState b, in FighterState snapA, in FighterState snapB, Vec2 point, float speed, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            var n = SimMath.Normalize(snapB.Position - snapA.Position);
            if (n.LengthSquared == 0f)
                n = new Vec2(0f, 1f);
            a.Velocity = -n * speed;
            b.Velocity = n * speed;
            a.StunTicks = c.RecoilTicks;
            b.StunTicks = c.RecoilTicks;
            EnterStun(ref a, ActionState.Recoil);
            EnterStun(ref b, ActionState.Recoil);
            a.HitstopTicks = c.ClashHitstop;
            b.HitstopTicks = c.ClashHitstop;
            events.Add(new SimEvent(tick, SimEventType.Clash, SimEvent.NoFighter, point, n, speed));
        }

        /// <summary>경직 계열 상태로 들어간다. 진행 중이던 공격, 충전·돌진, 패링 구간, 대시를 끊는다. StunTicks는 부르는 쪽이 정한다.</summary>
        internal static void EnterStun(ref FighterState f, ActionState action)
        {
            f.RestartAction(action);
            f.ThrowPending = false;
            f.AttackId = FighterState.NoAttack;
            f.AttackHasHit = false;
            f.ParryTicks = 0;
            f.DashTicksLeft = 0;
            f.JumpCutEligible = false;
            if (f.Velocity.Y > 0f)
            {
                f.Grounded = false;
                f.GroundBox = FighterState.NoBox;
            }
        }

        /// <summary>일반 명중 히트스탑: clamp(4 + 데미지 × 0.25, 4, 12), 반올림.</summary>
        public static int HitstopFor(int damage, in CombatParams c) =>
            SimMath.Clamp(SimMath.RoundToInt(c.HitstopBase + damage * c.HitstopPerDamage), c.HitstopMin, c.HitstopMax);

        /// <summary>피격 경직: clamp(round(넉백 × 1.1), 8, 45) × 0.8^n, 반올림(최소 1). 넉백은 이미 감쇠한 값.</summary>
        public static int HitstunFor(float knockback, int comboIndex, in CombatParams c)
        {
            int stun = SimMath.Clamp(SimMath.RoundToInt(knockback * c.HitstunPerKnockback), c.HitstunMin, c.HitstunMax);
            return SimMath.Max(1, SimMath.RoundToInt(stun * SimMath.Pow(c.ComboHitstunDecay, comboIndex)));
        }
    }
}
