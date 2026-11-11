namespace Knockbox.Simulation
{
    /// <summary>무기 스폰, 획득, 내려놓기와 권총 판정. Unity 시간·난수·물리 없이 고정 틱으로 진행한다.</summary>
    public static class BrawlWeapons
    {
        const InputButtons ConsumedButtons = InputButtons.Grab | InputButtons.Attack | InputButtons.Charge;

        public static BrawlWeaponState Init(SimConfig config) => !config.Combat.Weapons.Enabled
            ? default
            : new BrawlWeaponState
            {
                Enabled = true,
                Owner = -1,
                Phase = BrawlWeaponPhase.Waiting,
                ThrowEnabled = config.Combat.Weapons.ThrowEnabled,
                Thrower = config.Combat.Weapons.ThrowEnabled ? -1 : 0,
            };

        public static void Clear(ref BrawlWeaponState weapon)
        {
            if (!weapon.Enabled)
                return;
            bool throwing = weapon.ThrowEnabled;
            weapon = new BrawlWeaponState
            {
                Enabled = true, Owner = -1, Phase = BrawlWeaponPhase.Finished,
                ThrowEnabled = throwing, Thrower = throwing ? -1 : 0,
            };
        }

        public static BrawlWeaponKind HeldKind(in BrawlWeaponState weapon, int slot) =>
            weapon.Enabled && weapon.Phase == BrawlWeaponPhase.Held && weapon.Owner == slot
                ? weapon.Kind : BrawlWeaponKind.None;

        /// <summary>StepFighting의 시작에서 한 번 호출한다. 히트스탑 중에도 월드의 낙하는 계속된다.</summary>
        public static void Tick(ref SimState s, SimConfig config, ArenaDef arena, SimEventBuffer events)
        {
            ref var w = ref s.Weapon;
            if (!w.Enabled || w.Phase == BrawlWeaponPhase.Finished || s.Match.Phase != MatchPhase.Fighting)
                return;
            ref readonly var p = ref config.Combat.Weapons;
            w.AgeTicks++;
            switch (w.Phase)
            {
                case BrawlWeaponPhase.Waiting:
                    if (w.AgeTicks < p.SpawnDelayTicks)
                        return;
                    // 이 분기는 라운드에 한 번만 지나며 난수도 딱 한 번 소비한다.
                    w.Kind = s.Rng.Range(0, 2) == 0 ? BrawlWeaponKind.Pan : BrawlWeaponKind.Pistol;
                    w.Ammo = w.Kind == BrawlWeaponKind.Pistol ? p.PistolAmmo : 0;
                    float x = (arena.SpawnFoot0.X + arena.SpawnFoot1.X) * 0.5f;
                    float y = SimMath.Max(arena.SpawnFoot0.Y, arena.SpawnFoot1.Y) + p.DropHeight;
                    w.LandingPosition = SupportBelow(new Vec2(x, y), p.GroundHeight, arena);
                    w.FallStart = w.LandingPosition + new Vec2(0f, p.DropHeight);
                    w.Position = w.FallStart;
                    w.FallDurationTicks = SimMath.Max(1, p.FallTicks);
                    w.Phase = BrawlWeaponPhase.Warning;
                    w.PhaseTick = 0;
                    events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponWarning, SimEvent.NoFighter,
                        w.LandingPosition, Vec2.Zero, (float)w.Kind));
                    return;
                case BrawlWeaponPhase.Warning:
                    if (++w.PhaseTick < p.WarningTicks)
                        return;
                    w.Phase = BrawlWeaponPhase.Falling;
                    w.PhaseTick = 0;
                    w.Position = w.FallStart;
                    return;
                case BrawlWeaponPhase.Falling:
                    w.PhaseTick++;
                    float ratio = SimMath.Min(1f, w.PhaseTick / (float)SimMath.Max(1, w.FallDurationTicks));
                    w.Position = w.FallStart + (w.LandingPosition - w.FallStart) * ratio;
                    if (ratio < 1f)
                        return;
                    w.Position = w.LandingPosition;
                    w.Phase = BrawlWeaponPhase.Ground;
                    w.PhaseTick = 0;
                    events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponLanded, SimEvent.NoFighter,
                        w.Position, Vec2.Zero, (float)w.Kind));
                    return;
                case BrawlWeaponPhase.Held:
                    SyncHeldPosition(ref s);
                    return;
            }
        }

        /// <summary>
        /// BeginTick 전에 두 선수의 같은 스냅숏으로 획득 후보를 정한다. 동거리면 둘 다 못 줍는다.
        /// 수정된 입력은 이 틱의 전투에만 사용하고 PrevButtons에는 원래 입력을 저장해야 한다.
        /// </summary>
        public static void ProcessInputs(ref SimState s, ref InputFrame p0, ref InputFrame p1,
            SimConfig config, ArenaDef arena, SimEventBuffer events)
        {
            ref var w = ref s.Weapon;
            if (!w.Enabled)
                return;
            if (w.Phase == BrawlWeaponPhase.Held)
            {
                if (w.Owner == 0)
                {
                    if (RequestsGrab(s.Fighter0, p0, config))
                    {
                        var aim = p0;
                        Consume(ref s.Fighter0, ref p0);
                        Release(ref s, aim, config, arena, events);
                    }
                    else
                        SuppressGrab(ref s.Fighter0, ref p0);
                }
                else if (w.Owner == 1)
                {
                    if (RequestsGrab(s.Fighter1, p1, config))
                    {
                        var aim = p1;
                        Consume(ref s.Fighter1, ref p1);
                        Release(ref s, aim, config, arena, events);
                    }
                    else
                        SuppressGrab(ref s.Fighter1, ref p1);
                }
                return;
            }
            if (w.Phase != BrawlWeaponPhase.Ground)
                return;
            float radiusSq = config.Combat.Weapons.PickupRadius * config.Combat.Weapons.PickupRadius;
            float d0 = (s.Fighter0.Position - w.Position).LengthSquared;
            float d1 = (s.Fighter1.Position - w.Position).LengthSquared;
            bool pick0 = RequestsGrab(s.Fighter0, p0, config) && d0 <= radiusSq
                && ClearLine(s.Fighter0.Position, w.Position, s, arena);
            bool pick1 = RequestsGrab(s.Fighter1, p1, config) && d1 <= radiusSq
                && ClearLine(s.Fighter1.Position, w.Position, s, arena);
            // 낙하 무기를 잡으려 한 입력이 상대 잡기/펀치/박치기로 새지 않게 둘 다 소비한다.
            if (pick0)
                Consume(ref s.Fighter0, ref p0);
            if (pick1)
                Consume(ref s.Fighter1, ref p1);
            int owner = pick0 && (!pick1 || d0 < d1) ? 0 : pick1 && (!pick0 || d1 < d0) ? 1 : -1;
            if (owner < 0)
                return;
            w.Owner = owner;
            w.Phase = BrawlWeaponPhase.Held;
            w.PhaseTick = 0;
            if (w.ThrowEnabled)
            {
                w.Velocity = Vec2.Zero;
                w.Thrower = -1;
                w.ThrowHit = false;
            }
            SyncHeldPosition(ref s);
            events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponPickup, owner, w.Position, Vec2.Zero, (float)w.Kind));
        }

        /// <summary>접촉·전투 뒤, KO 처리 전에 호출한다. 벽꽝으로 소유를 먼저 풀고 발사할 수 있는지 검사한다.</summary>
        public static void AfterCombat(ref SimState s, SimConfig config, ArenaDef arena,
            bool frozen0, bool frozen1, SimEventBuffer events)
        {
            var prev0 = s.Fighter0;
            var prev1 = s.Fighter1;
            AfterCombat(ref s, config, arena, frozen0, frozen1, prev0, prev1, events);
        }

        public static void AfterCombat(ref SimState s, SimConfig config, ArenaDef arena,
            bool frozen0, bool frozen1, in FighterState prev0, in FighterState prev1, SimEventBuffer events)
        {
            ref var w = ref s.Weapon;
            if (w.Enabled && w.ThrowEnabled && w.Phase == BrawlWeaponPhase.Thrown)
            {
                StepThrown(ref s, config, arena, prev0, prev1, events);
                return;
            }
            if (!w.Enabled || w.Phase != BrawlWeaponPhase.Held)
                return;
            int owner = w.Owner;
            if (owner < 0 || owner > 1)
                return;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type != SimEventType.WallSplat || events[i].Fighter != owner)
                    continue;
                Drop(ref s, config, arena, events);
                return;
            }
            SyncHeldPosition(ref s);
            var fighter = s.GetFighter(owner);
            bool frozen = owner == 0 ? frozen0 : frozen1;
            if (w.Kind != BrawlWeaponKind.Pistol || frozen || fighter.Health <= 0 || fighter.Action != ActionState.Attack
                || fighter.AttackId != AttackIds.Pistol || fighter.ActionTick != 0)
                return;
            if (w.Ammo <= 0)
            {
                events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponEmpty, owner, fighter.Position,
                    fighter.AttackDir, 0f));
                return;
            }
            Fire(ref s, owner, fighter, config, arena, events);
        }

        static bool RequestsGrab(in FighterState f, in InputFrame input, SimConfig config) => f.HitstopTicks == 0
            && CanInteractThisTick(f, config)
            && ((InputFrame.Pressed(input.Buttons, f.PrevButtons) | f.PendingPresses) & InputButtons.Grab) != 0;

        static bool CanInteractThisTick(in FighterState f, SimConfig config)
        {
            if (f.InMovementState || f.Action == ActionState.Charge)
                return true;
            // StepFighter가 ActionTick을 먼저 올린 뒤 BeginTick에서 후딜·경직을 끝낸다.
            // 같은 경계에서 상대 잡기와 무기 줍기/내려놓기의 우선순위가 바뀌지 않아야 한다.
            switch (f.Action)
            {
                case ActionState.Attack:
                    return f.ActionTick + 1 >= config.GetAttack(f.AttackId).TotalTicks;
                case ActionState.Grab:
                    return f.ActionTick + 1 >= config.Combat.Grab.TotalWhiffTicks;
                case ActionState.Hitstun:
                case ActionState.Stagger:
                case ActionState.Recoil:
                case ActionState.Thrown:
                    return f.StunTicks <= 0;
                default:
                    return false;
            }
        }

        static void SuppressGrab(ref FighterState f, ref InputFrame input)
        {
            // 무기 보유 중 Grab은 내려놓기 전용이다. BeginTick이 이 틱에 후딜/경직을 끝내더라도
            // 미리 거절한 내려놓기 입력이 상대 잡기로 바뀌지 않게 소비한다(별도 예약 없음).
            // 히트스탑 동안에는 Freeze가 눌림을 저장하고, 재개하는 틱에 가능 여부를 다시 판단한다.
            if (f.HitstopTicks > 0)
                return;
            input.Buttons &= ~InputButtons.Grab;
            f.PendingPresses &= ~InputButtons.Grab;
        }

        static void Consume(ref FighterState f, ref InputFrame input)
        {
            input.Buttons &= ~ConsumedButtons;
            f.PendingPresses &= ~ConsumedButtons;
            f.AttackBuffer = 0;
            if (f.Action == ActionState.Charge)
            {
                f.RestartAction(ActionState.Airborne);
                f.AttackDir = Vec2.Zero;
            }
        }

        static void SyncHeldPosition(ref SimState s)
        {
            int owner = s.Weapon.Owner;
            if (owner == 0 || owner == 1)
                s.Weapon.Position = s.GetFighter(owner).Position;
        }

        static void Drop(ref SimState s, SimConfig config, ArenaDef arena, SimEventBuffer events)
        {
            ref var w = ref s.Weapon;
            int owner = w.Owner;
            Vec2 from = s.GetFighter(owner).Position;
            w.Owner = -1;
            w.Phase = BrawlWeaponPhase.Falling;
            w.PhaseTick = 0;
            w.Position = from;
            w.FallStart = from;
            w.LandingPosition = SupportBelow(from, config.Combat.Weapons.GroundHeight, arena);
            w.FallDurationTicks = SimMath.Max(1, config.Combat.Weapons.DropTicks);
            if (w.ThrowEnabled)
            {
                w.Velocity = Vec2.Zero;
                w.Thrower = -1;
                w.ThrowHit = false;
            }
            events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponDrop, owner, from,
                w.LandingPosition - from, (float)w.Kind));
        }

        static void Release(ref SimState s, in InputFrame input, SimConfig config, ArenaDef arena, SimEventBuffer events)
        {
            if (!s.Weapon.ThrowEnabled)
            {
                Drop(ref s, config, arena, events);
                return;
            }
            ref var w = ref s.Weapon;
            int owner = w.Owner;
            var fighter = s.GetFighter(owner);
            Vec2 direction = AimDirections.TryGet(input.Aim, out var aim) ? aim
                : input.MoveX == 0 && input.MoveY == 0 ? new Vec2(fighter.Facing, 0f)
                : SimMath.Normalize(new Vec2(input.MoveX, input.MoveY));
            w.Owner = -1;
            w.Phase = BrawlWeaponPhase.Thrown;
            w.PhaseTick = 0;
            w.Position = fighter.Position;
            w.FallStart = w.LandingPosition = w.Position;
            w.FallDurationTicks = 0;
            w.Velocity = direction * config.Combat.Weapons.ThrowSpeed;
            w.Thrower = owner;
            w.ThrowHit = false;
            events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponThrow, owner, w.Position, w.Velocity, (float)w.Kind));
        }

        /// <summary>투척도 고정 틱으로 판정한다. 각 이동 구간을 검사하고 벽 뒤의 상대는 맞히지 않는다.</summary>
        static void StepThrown(ref SimState s, SimConfig config, ArenaDef arena,
            in FighterState prev0, in FighterState prev1, SimEventBuffer events)
        {
            ref var w = ref s.Weapon;
            ref readonly var p = ref config.Combat.Weapons;
            w.PhaseTick++;
            w.Velocity.Y -= p.ThrowGravity * SimConfig.Dt;
            float remaining = 1f;
            float elapsed = 0f;
            // 코너를 한 틱에 여러 번 만나는 경우도 고정 횟수만 처리한다. 남은 시간은 버리고 다음 틱에 계속한다.
            for (int contact = 0; contact < 4 && remaining > 0f; contact++)
            {
                Vec2 from = w.Position;
                Vec2 delta = w.Velocity * (SimConfig.Dt * remaining);
                float block = ThrowBlockingTime(from, delta, p.ThrowRadius, s, arena, out var normal);
                int target = 1 - w.Thrower;
                float hit = 2f;
                if (!w.ThrowHit && target >= 0 && target < 2 && FighterCombat.CanBeHit(s.GetFighter(target), config))
                {
                    Vec2 before = target == 0 ? prev0.Position : prev1.Position;
                    Vec2 movement = s.GetFighter(target).Position - before;
                    Vec2 relative = from - (before + movement * elapsed);
                    float half = config.HalfBody + p.ThrowRadius;
                    hit = Projectiles.SegmentBox(relative, delta - movement * remaining, -half, -half, half, half);
                }
                if (hit <= 1f && hit < block)
                {
                    w.Position = from + delta * hit;
                    var direction = SimMath.Normalize(w.Velocity);
                    if (target == 0)
                        FighterCombat.ProjectileHit(ref s.Fighter0, target, p.ThrowDamage, direction, p.ThrowKnockback,
                            w.Position, config, s.Tick, events);
                    else
                        FighterCombat.ProjectileHit(ref s.Fighter1, target, p.ThrowDamage, direction, p.ThrowKnockback,
                            w.Position, config, s.Tick, events);
                    w.ThrowHit = true;
                    w.Velocity = -w.Velocity * p.ThrowRestitution;
                    elapsed += remaining * hit;
                    remaining *= 1f - hit;
                    continue;
                }
                if (block > 1f)
                {
                    w.Position = from + delta;
                    return;
                }
                w.Position = from + delta * block;
                w.Position += normal * TerrainCollision.Epsilon;
                float toward = Vec2.Dot(w.Velocity, normal);
                if (toward < 0f)
                    w.Velocity = (w.Velocity - normal * (2f * toward)) * p.ThrowRestitution;
                // 윗면에 닿고 반동이 작아졌으면 바닥에 남긴다. 벽·천장에서는 멈춰 붙지 않는다.
                if (normal.Y > 0.5f && SimMath.Abs(w.Velocity.Y) <= 1f)
                {
                    Settle(ref w, s.Tick, events);
                    return;
                }
                elapsed += remaining * block;
                remaining *= 1f - block;
            }
        }

        static void Settle(ref BrawlWeaponState w, int tick, SimEventBuffer events)
        {
            w.Phase = BrawlWeaponPhase.Ground;
            w.PhaseTick = 0;
            w.Velocity = Vec2.Zero;
            w.Thrower = -1;
            w.LandingPosition = w.FallStart = w.Position;
            w.FallDurationTicks = 0;
            events.Add(new SimEvent(tick, SimEventType.BrawlWeaponLanded, SimEvent.NoFighter,
                w.Position, Vec2.Zero, (float)w.Kind));
        }

        static float ThrowBlockingTime(Vec2 from, Vec2 delta, float radius, in SimState s, ArenaDef arena, out Vec2 normal)
        {
            float best = 2f;
            normal = Vec2.Zero;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (box.Kind == TerrainKind.OneWay && (delta.Y >= 0f || from.Y - radius < box.MaxY - TerrainCollision.Epsilon))
                    continue;
                float t = SweepBox(from, delta, box.MinX - radius, box.MinY - radius,
                    box.MaxX + radius, box.MaxY + radius, out var n);
                Nearest(t, n, ref best, ref normal);
            }
            for (int i = 0; i < s.Props.Count; i++)
            {
                ref readonly var body = ref PropState.At(s.Props, i);
                ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
                var local = body.ToLocal(from);
                var d = PropBody.InverseRotate(body.Rotation, delta);
                var h = def.HalfSize;
                float t = SweepBox(local, d, -h.X - radius, -h.Y - radius, h.X + radius, h.Y + radius, out var n);
                Nearest(t, PropBody.Rotate(body.Rotation, n), ref best, ref normal);
            }
            float hx = (arena.HasBoundary ? arena.BoundaryHalfWidth : arena.HalfWidth) - radius;
            float hy = (arena.HasBoundary ? arena.BoundaryHalfHeight : arena.HalfHeight) - radius;
            Nearest(BoundaryExit(from.X, delta.X, hx), new Vec2(delta.X > 0f ? -1f : 1f, 0f), ref best, ref normal);
            Nearest(BoundaryExit(from.Y, delta.Y, hy), new Vec2(0f, delta.Y > 0f ? -1f : 1f), ref best, ref normal);
            normal = SimMath.Normalize(normal);
            return best;
        }

        static void Nearest(float time, Vec2 normal, ref float best, ref Vec2 bestNormal)
        {
            if (time > 1f)
                return;
            if (time < best)
            {
                best = time;
                bestNormal = normal;
            }
            else if (time == best)
                bestNormal += normal;
        }

        static float SweepBox(Vec2 from, Vec2 delta, float minX, float minY, float maxX, float maxY, out Vec2 normal)
        {
            float t = Projectiles.SegmentBox(from, delta, minX, minY, maxX, maxY);
            normal = Vec2.Zero;
            if (t > 1f)
                return t;
            float tx = delta.X > 0f ? (minX - from.X) / delta.X
                : delta.X < 0f ? (maxX - from.X) / delta.X : float.NegativeInfinity;
            float ty = delta.Y > 0f ? (minY - from.Y) / delta.Y
                : delta.Y < 0f ? (maxY - from.Y) / delta.Y : float.NegativeInfinity;
            if (tx >= ty && delta.X != 0f)
                normal.X = delta.X > 0f ? -1f : 1f;
            if (ty >= tx && delta.Y != 0f)
                normal.Y = delta.Y > 0f ? -1f : 1f;
            normal = SimMath.Normalize(normal);
            return t;
        }

        static Vec2 SupportBelow(Vec2 from, float height, ArenaDef arena)
        {
            float halfWidth = arena.HasBoundary ? arena.BoundaryHalfWidth : arena.HalfWidth;
            float halfHeight = arena.HasBoundary ? arena.BoundaryHalfHeight : arena.HalfHeight;
            float x = SimMath.Clamp(from.X, -halfWidth + height, halfWidth - height);
            float floor = -halfHeight;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (x < box.MinX || x > box.MaxX || box.MaxY > from.Y - height)
                    continue;
                floor = SimMath.Max(floor, box.MaxY);
            }
            return new Vec2(x, floor + height);
        }

        static bool ClearLine(Vec2 from, Vec2 to, in SimState s, ArenaDef arena) =>
            BlockingTime(from, to - from, s, arena) >= 1f;

        static void Fire(ref SimState s, int owner, in FighterState fighter, SimConfig config,
            ArenaDef arena, SimEventBuffer events)
        {
            ref readonly var p = ref config.Combat.Weapons;
            s.Weapon.Ammo--;
            Vec2 dir = fighter.AttackDir.LengthSquared > 0f
                ? SimMath.Normalize(fighter.AttackDir) : new Vec2(fighter.Facing, 0f);
            Vec2 from = fighter.Position;
            Vec2 delta = dir * p.PistolRange;
            float block = BlockingTime(from, delta, s, arena);
            int target = 1 - owner;
            var victim = s.GetFighter(target);
            float hit = 2f;
            if (FighterCombat.CanBeHit(victim, config))
            {
                float h = config.HalfBody;
                hit = Projectiles.SegmentBox(from - victim.Position, delta, -h, -h, h, h);
            }
            float end = SimMath.Min(1f, block);
            // 막는 지형과 동시 접촉이면 지형을 우선한다(벽 뒤로 피해가 새지 않는다).
            if (hit <= 1f && hit < block)
            {
                end = hit;
                Vec2 point = from + delta * hit;
                if (target == 0)
                    FighterCombat.ProjectileHit(ref s.Fighter0, target, p.PistolDamage, dir, p.PistolKnockback,
                        point, config, s.Tick, events);
                else
                    FighterCombat.ProjectileHit(ref s.Fighter1, target, p.PistolDamage, dir, p.PistolKnockback,
                        point, config, s.Tick, events);
            }
            events.Add(new SimEvent(s.Tick, SimEventType.BrawlWeaponShot, owner, from, delta * end, s.Weapon.Ammo));
        }

        /// <summary>기존 투사체와 같은 상자 교차식을 사용한다. 막힌 곳이 없으면 2, 선분 안이면 0~1.</summary>
        static float BlockingTime(Vec2 from, Vec2 delta, in SimState s, ArenaDef arena)
        {
            float best = 2f;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                best = SimMath.Min(best, Projectiles.SegmentBox(from, delta, box.MinX, box.MinY, box.MaxX, box.MaxY));
            }
            for (int i = 0; i < s.Props.Count; i++)
            {
                ref readonly var body = ref PropState.At(s.Props, i);
                ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
                Vec2 a = body.ToLocal(from);
                Vec2 d = PropBody.InverseRotate(body.Rotation, delta);
                Vec2 h = def.HalfSize;
                best = SimMath.Min(best, Projectiles.SegmentBox(a, d, -h.X, -h.Y, h.X, h.Y));
            }
            float hx = arena.HasBoundary ? arena.BoundaryHalfWidth : arena.HalfWidth;
            float hy = arena.HasBoundary ? arena.BoundaryHalfHeight : arena.HalfHeight;
            best = SimMath.Min(best, BoundaryExit(from.X, delta.X, hx));
            return SimMath.Min(best, BoundaryExit(from.Y, delta.Y, hy));
        }

        static float BoundaryExit(float x, float dx, float halfSize)
        {
            if (x < -halfSize || x > halfSize)
                return 0f;
            if (dx > 0f && x + dx > halfSize)
                return (halfSize - x) / dx;
            if (dx < 0f && x + dx < -halfSize)
                return (-halfSize - x) / dx;
            return 2f;
        }
    }
}
