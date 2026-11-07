namespace Knockbox.Simulation
{
    /// <summary>
    /// 캐릭터와 구조물 강체의 상호작용(09-props-physics.md 4절, CP14). 한 틱 순서(SimWorld.StepFighting):
    /// 구조물 적분(PropPhysics) → 캐릭터 이동과 지형 충돌 → 이 단계(겹침 해소, 운동량 교환, 돌진·맞기·벽꽝) →
    /// 캐릭터끼리 밀어내기 → 접지와 지형 반응 → 전투 판정.
    /// 두 캐릭터를 같은 규칙으로 따로 계산하고, 구조물에 주는 충격량은 틱 시작 상태로 계산해 더한 뒤 한 번에 준다(번호 우선 없음).
    /// 겹침은 캐릭터만 밀어낸다(구조물은 줄·축에 묶여 있어 속도로만 반응한다).
    /// </summary>
    public static class PropInteraction
    {
        public static void Resolve(ref SimState s, ArenaDef arena, SimConfig config, in FighterState prev0, in FighterState prev1,
            bool frozen0, bool frozen1, SimEventBuffer events, PropScratch scratch)
        {
            if (s.Props.Count == 0)
            {
                s.Fighter0.OnProp = 0;
                s.Fighter1.OnProp = 0;
                return;
            }
            var before = s.Props;
            var impulses = new PropImpulses();
            ResolveFighter(ref s.Fighter0, prev0, frozen0, 0, before, ref impulses, arena, config, s.Tick, events);
            ResolveFighter(ref s.Fighter1, prev1, frozen1, 1, before, ref impulses, arena, config, s.Tick, events);
            impulses.ApplyTo(ref s.Props, arena, config, scratch);
            // 캐릭터가 준 충격까지 더해 끊어짐을 판정(09 5절: 기준 충격 이상에서만).
            PropPhysics.CheckBreaks(ref s.Props, arena, scratch, s.Tick, events);
        }

        /// <summary>틱 동안 두 캐릭터가 구조물에 준 충격량의 합(몸마다 선·각 충격량). 할당 없이 PropState 모양을 빌린다.</summary>
        struct PropImpulses
        {
            PropState sums;

            public void Add(int body, Vec2 impulse, Vec2 point, in PropBody b, int toucher)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.Velocity += impulse;
                sum.AngularVelocity += PropBody.Cross(point - b.Position, impulse);
                sum.LastToucher = toucher + 1;
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            /// <summary>서 있는 캐릭터의 무게(상자 전용): 깨우지도, 마지막 접촉자를 바꾸지도 않는다.</summary>
            public void Press(int body, Vec2 impulse, Vec2 point, in PropBody b)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.Velocity += impulse;
                sum.AngularVelocity += PropBody.Cross(point - b.Position, impulse);
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            public void Strike(int body, int fighter, int cooldown)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.SetStrikeCooldown(fighter, cooldown);
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            public void Hit(int body, int fighter, int cooldown)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.SetHitCooldown(fighter, cooldown);
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            /// <summary>부분 붕괴: 쳐낸 방향(Position 대신 쓸 칸이 없어 StrikeCooldown과 함께 Broken 칸에 표시).</summary>
            public void Knockout(int body, Vec2 direction)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.Broken = 1;
                sum.Warm.W0.Normal = direction.X;
                sum.Warm.W0.Tangent = direction.Y;
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            public void Reflect(int body, Vec2 normal, float scale)
            {
                ref var sum = ref PropState.Get(ref sums, body);
                sum.Position = normal;
                sum.Rotation = new Vec2(scale, 1f);
                if (body + 1 > sums.Count)
                    sums.Count = body + 1;
            }

            public void ApplyTo(ref PropState props, ArenaDef arena, SimConfig config, PropScratch scratch)
            {
                for (int i = 0; i < sums.Count; i++)
                {
                    ref readonly var sum = ref PropState.At(in sums, i);
                    ref var b = ref PropState.Get(ref props, i);
                    ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
                    float m = PropPhysics.Mass(def, config.Combat.Props);
                    // 패링: 구조물 속도를 접촉 법선 기준으로 반사 × 0.8(충격량보다 먼저).
                    if (sum.Rotation.Y > 0f)
                    {
                        var n = sum.Position;
                        b.Velocity = (b.Velocity - n * (2f * Vec2.Dot(b.Velocity, n))) * sum.Rotation.X;
                        b.AngularVelocity *= -sum.Rotation.X;
                    }
                    scratch.TickImpulse[i] += SimMath.Sqrt(sum.Velocity.LengthSquared);
                    b.Velocity += sum.Velocity / m;
                    b.AngularVelocity += sum.AngularVelocity / PropPhysics.Inertia(def, m);
                    if (sum.Broken != 0)
                    {
                        // 부분 붕괴: 친 방향으로 적어도 KnockoutSpeed, 잠시 다른 상자와 마찰 0.
                        var dir = new Vec2(sum.Warm.W0.Normal, sum.Warm.W0.Tangent);
                        float along = Vec2.Dot(b.Velocity, dir);
                        ref readonly var pc = ref config.Combat.Props;
                        if (along < pc.KnockoutSpeed)
                            b.Velocity += dir * (pc.KnockoutSpeed - along);
                        b.LooseTicks = pc.LooseTicks;
                        b.SleepTicks = 0;
                    }
                    // 충격을 받으면 깬다.
                    if (sum.LastToucher > 0 || sum.Rotation.Y > 0f)
                        b.SleepTicks = 0;
                    if (sum.LastToucher > 0)
                        b.LastToucher = sum.LastToucher - 1;
                    if (sum.HitCooldown0 > 0)
                        b.HitCooldown0 = sum.HitCooldown0;
                    if (sum.HitCooldown1 > 0)
                        b.HitCooldown1 = sum.HitCooldown1;
                    if (sum.StrikeCooldown0 > 0)
                        b.StrikeCooldown0 = sum.StrikeCooldown0;
                    if (sum.StrikeCooldown1 > 0)
                        b.StrikeCooldown1 = sum.StrikeCooldown1;
                }
            }
        }

        static void ResolveFighter(ref FighterState f, in FighterState prev, bool frozen, int index, in PropState props, ref PropImpulses impulses,
            ArenaDef arena, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat.Props;
            float half = config.HalfBody;
            int wasOn = f.OnProp;
            f.OnProp = 0;
            if (f.Action == ActionState.Held || f.Action == ActionState.KO)
                return;

            // 서 있던 구조물을 따라 움직인다(같이 흔들림).
            if (wasOn > 0 && wasOn <= props.Count && !frozen)
            {
                ref readonly var under = ref PropState.At(in props, wasOn - 1);
                var feet = new Vec2(f.Position.X, f.Position.Y - half);
                Displace(ref f.Position, under.PointVelocity(feet) * SimConfig.Dt, half, arena, config);
            }

            int standing = 0;
            // 부분 붕괴는 한 번의 돌진에 상자 하나(캐릭터 중심 높이에 가장 가까운 상자)만 쳐낸다.
            int knockout = -1;
            float knockoutGap = float.MaxValue, knockoutPower = 0f;
            var knockoutDirection = Vec2.Zero;
            for (int i = 0; i < props.Count; i++)
            {
                ref readonly var b = ref PropState.At(in props, i);
                ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
                if (!Overlap(f.Position, prev.Position, half, b, def.HalfSize, out var n, out float depth))
                    continue;
                var point = f.Position - n * half;
                float m = PropPhysics.Mass(def, c);
                float invI = 1f / PropPhysics.Inertia(def, m);
                var r = point - b.Position;
                float rn = PropBody.Cross(r, n);
                float kProp = 1f / m + invI * rn * rn;
                var vp = b.PointVelocity(point);
                float vn = Vec2.Dot(f.Velocity - vp, n);
                bool onThis = wasOn == i + 1;

                // 1) 돌진이 구조물을 침: 공격성 위력 × 1.0을 충격량으로. 무거우면 지형처럼 반사 × 0.6, 가벼우면 계속 나아감.
                float power = frozen ? 0f : FighterCombat.Power(f, -n, config);
                if (f.Action == ActionState.Lunge && power > 0f && b.GetStrikeCooldown(index) == 0)
                {
                    impulses.Add(i, -n * (power * c.LungeImpulseScale), point, b, index);
                    impulses.Strike(i, index, c.StrikeCooldownTicks);
                    float gap = SimMath.Abs(b.Position.Y - f.Position.Y);
                    if (power >= c.KnockoutPower && PropPhysics.IsBox(def.Kind) && gap < knockoutGap)
                    {
                        knockout = i;
                        knockoutGap = gap;
                        knockoutPower = power;
                        knockoutDirection = -n;
                    }
                    if (m >= c.LungeHeavyMass)
                        f.Velocity = (f.Velocity - n * (2f * Vec2.Dot(f.Velocity, n))) * c.LungeHeavyRestitution;
                    events.Add(new SimEvent(tick, SimEventType.PropStrike, index, point, -n, power));
                }
                // 2) 구조물이 캐릭터를 침: 상대 법선 속도 10 이상, 구조물이 다가오는 쪽, 그 위에 서 있지 않음, 쌍마다 20틱.
                else if (-vn >= c.HitMinSpeed && Vec2.Dot(vp, n) > Vec2.Dot(f.Velocity, -n) && !onThis
                         && b.GetHitCooldown(index) == 0 && FighterCombat.CanBeHit(f, config)
                         && !(PropPhysics.IsBox(def.Kind) && f.BoxDamageSum >= c.BoxDamageCap))
                {
                    PropHit(ref f, -vn, m, n, point, index, i, b, PropPhysics.IsBox(def.Kind), ref impulses, config, tick, events);
                }
                // 3) 그 밖: 질량 비에 따른 운동량 교환(반발 0.15). 날아가던 캐릭터가 무거운 구조물에 세게 부딪히면 벽꽝.
                else if (vn < 0f)
                {
                    float j = -(1f + c.FighterRestitution) * vn / (1f / c.FighterMass + kProp);
                    bool splat = f.IsLaunched && m >= c.SplatMinMass && -vn >= config.Combat.WallSplatSpeed;
                    if (!frozen)
                        f.Velocity += n * (j / c.FighterMass);
                    // 상자에 느리게(1 미만) 닿은 것은 깨우지 않는다(위에 가만히 선 캐릭터가 매 틱 상자를 깨워 기둥이 잠들지 못했다).
                    if (!PropPhysics.IsBox(def.Kind) || -vn >= 1f)
                        impulses.Add(i, -n * j, point, b, index);
                    else if (!PropPhysics.Asleep(b, c))
                        impulses.Press(i, -n * j, point, b);
                    if (splat)
                        Splat(ref f, n, -vn, point, index, config, tick, events);
                }

                // 겹침 해소: 캐릭터만 밀어낸다(히트스탑 중에도 위치는 밀어낸다: 구조물은 멈추지 않으므로 그대로 두면 몸을 지나간다).
                // 위쪽 50° 이내 법선이면 그 구조물 위에 선 것.
                Displace(ref f.Position, n * depth, half, arena, config);
                if (n.Y >= c.StandCos && f.Velocity.Y <= vp.Y + 0.5f)
                {
                    standing = i + 1;
                    // 구조물의 위 방향 속도는 0.5 이상일 때만 이어받는다(부드러운 접촉이 상자를 0.01~0.03 밀어 올리는 것까지 받으면 접지가 풀렸다, CP16).
                    float carry = vp.Y >= 0.5f ? vp.Y : SimMath.Min(vp.Y, 0f);
                    if (f.Velocity.Y < carry)
                        f.Velocity.Y = carry;
                    // 캐릭터 무게만큼 누른다(밟으면 기운다). 상자는 무게로 깨우지 않는다(CP16: 잠든 상자는 움직이지 않는 받침,
                    // 깨어 있으면 무게만 전하고 잠들기는 그대로 세어 위에 서 있어도 다시 잠든다).
                    var weight = new Vec2(0f, -c.FighterMass * c.Gravity * SimConfig.Dt);
                    if (!PropPhysics.IsBox(def.Kind))
                        impulses.Add(i, weight, point, b, index);
                    else if (!PropPhysics.Asleep(b, c))
                        impulses.Press(i, weight, point, b);
                }
            }
            if (knockout >= 0)
            {
                impulses.Knockout(knockout, knockoutDirection);
                events.Add(new SimEvent(tick, SimEventType.BoxKnockout, index, PropState.At(in props, knockout).Position, knockoutDirection, knockoutPower));
            }
            // 서 있던 구조물에 겹치지 않고 딱 맞닿아 있으면(잠든 상자는 내려앉지 않는다) 지형처럼 접지 탐지 거리만큼 아래를 더듬어 계속 선 것으로 본다.
            if (standing == 0 && wasOn > 0 && wasOn <= props.Count && f.Velocity.Y <= 0f)
            {
                ref readonly var under = ref PropState.At(in props, wasOn - 1);
                var probe = new Vec2(0f, config.Movement.GroundProbeDistance);
                var halfSize = arena.GetProp(arena.DynamicProp(wasOn - 1)).HalfSize;
                if (Overlap(f.Position - probe, prev.Position - probe, half, under, halfSize, out var n, out _) && n.Y >= c.StandCos)
                    standing = wasOn;
            }
            f.OnProp = standing;
        }

        /// <summary>위치를 옮기되 지형(임시 물체 상자 포함)과 경계를 지나치지 않게 지형 충돌로 쓸어 간다(속도는 바꾸지 않음).</summary>
        static void Displace(ref Vec2 position, Vec2 delta, float half, ArenaDef arena, SimConfig config)
        {
            var velocity = delta / SimConfig.Dt;
            var contacts = default(MoveContacts);
            TerrainCollision.Move(ref position, ref velocity, half, arena, config.Movement.SubstepMaxDistance, FighterState.NoBox, ref contacts);
        }

        /// <summary>구조물에 맞음(09 4절): 피해 clamp(round(속도 × 질량 × 0.15), 3, 18), 넉백 max(10, 속도 × 0.8), 경직은 공통 규칙. 가드·패링 적용.</summary>
        static void PropHit(ref FighterState f, float speed, float mass, Vec2 n, Vec2 point, int index, int body, in PropBody b, bool box,
            ref PropImpulses impulses, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            ref readonly var p = ref c.Props;
            impulses.Hit(body, index, p.HitCooldownTicks);
            int damage = SimMath.Clamp(SimMath.RoundToInt(speed * mass * p.HitDamageScale), p.HitMinDamage, p.HitMaxDamage);
            if (box)
            {
                // 상자 피해 상한(CP16): 작은 상자 여러 개에 연속으로 맞아 순식간에 쓰러지지 않게, 창 안의 합을 BoxDamageCap까지.
                damage = SimMath.Min(damage, p.BoxDamageCap - f.BoxDamageSum);
                if (f.BoxDamageTicks == 0)
                    f.BoxDamageTicks = p.BoxDamageWindowTicks;
                f.BoxDamageSum += damage;
            }
            float knockback = SimMath.Max(p.HitKnockbackMin, speed * p.HitKnockbackScale);
            if (f.Action == ActionState.Guard && f.ParryTicks > 0)
            {
                impulses.Reflect(body, n, p.ParryRestitution);
                f.ParryTicks = 0;
                f.HitstopTicks = c.ParryHitstop;
                events.Add(new SimEvent(tick, SimEventType.Parry, index, point, n, damage));
                return;
            }
            int hitstop = FighterCombat.HitstopFor(damage, c);
            if (f.IsGuarding)
            {
                int chip = SimMath.Max(1, SimMath.RoundToInt(damage * c.GuardDamageMultiplier));
                f.Health = SimMath.Max(0, f.Health - chip);
                f.Velocity.X = n.X * knockback * c.GuardKnockbackMultiplier;
                f.StunTicks = c.GuardStunTicks;
                FighterCombat.EnterStun(ref f, ActionState.GuardStun);
                f.HitstopTicks = SimMath.RoundToInt(hitstop * c.GuardHitstopMultiplier);
                events.Add(new SimEvent(tick, SimEventType.GuardHit, index, point, n, chip));
                return;
            }
            int combo = f.IsLaunched ? f.ComboHits : 0;
            float scaled = knockback * SimMath.Pow(c.ComboKnockbackDecay, combo);
            f.Velocity = n * scaled;
            f.Health = SimMath.Max(0, f.Health - damage);
            f.StunTicks = FighterCombat.HitstunFor(scaled, combo, c);
            f.ComboHits = combo + 1;
            FighterCombat.EnterStun(ref f, ActionState.Hitstun);
            f.HitstopTicks = hitstop;
            events.Add(new SimEvent(tick, box ? SimEventType.BoxHit : SimEventType.PropHit, index, point, n, damage));
        }

        /// <summary>무거운 구조물에 벽꽝(03 3절과 같은 수치).</summary>
        static void Splat(ref FighterState f, Vec2 n, float speed, Vec2 point, int index, SimConfig config, int tick, SimEventBuffer events)
        {
            ref readonly var c = ref config.Combat;
            f.Health = SimMath.Max(0, f.Health - c.WallSplatDamage);
            float along = Vec2.Dot(f.Velocity, n);
            f.Velocity = (f.Velocity - n * (2f * SimMath.Min(along, 0f))) * c.WallSplatRestitution;
            f.StunTicks += c.WallSplatHitstun;
            f.HitstopTicks = SimMath.Max(f.HitstopTicks, c.WallSplatHitstop);
            events.Add(new SimEvent(tick, SimEventType.WallSplat, index, point, n, speed));
        }

        /// <summary>
        /// 캐릭터 몸(축 정렬 정사각형)과 회전한 구조물: 분리축 검사. 법선은 구조물 → 캐릭터.
        /// 직전 틱에 떨어져 있던 축 중 가장 늦게 겹친 축(들어온 방향)을 접촉 축으로 고르고, 그런 축이 없으면(이미 겹쳐 있었으면)
        /// 가장 얕은 축. 방향은 직전 틱 위치 쪽(한 틱에 가운데를 지나쳐도 들어온 쪽으로 밀어낸다, 터널링 방지).
        /// </summary>
        public static bool Overlap(Vec2 position, Vec2 previous, float half, in PropBody b, Vec2 propHalf, out Vec2 normal, out float depth)
        {
            var u = b.Rotation;
            var v = new Vec2(-u.Y, u.X);
            var delta = position - b.Position;
            var before = previous - b.Position;
            normal = Vec2.Zero;
            depth = 0f;
            float shallow = float.MaxValue, latest = float.MinValue, enteredDepth = 0f;
            int shallowAxis = -1, enteredAxis = -1;
            for (int axis = 0; axis < 4; axis++)
            {
                var a = Axis(axis, u, v);
                float reach = propHalf.X * SimMath.Abs(Vec2.Dot(a, u)) + propHalf.Y * SimMath.Abs(Vec2.Dot(a, v)) + half * (SimMath.Abs(a.X) + SimMath.Abs(a.Y));
                float now = reach - SimMath.Abs(Vec2.Dot(delta, a));
                if (now <= 0f)
                    return false;
                if (now < shallow - 1e-5f)
                {
                    shallow = now;
                    shallowAxis = axis;
                }
                float was = reach - SimMath.Abs(Vec2.Dot(before, a));
                if (was <= 0f)
                {
                    // 이 축에서 겹치기 시작한 시점(0 직전 틱 … 1 지금). 가장 늦은 축이 들어온 면.
                    float t = -was / (now - was);
                    if (t > latest)
                    {
                        latest = t;
                        enteredAxis = axis;
                        enteredDepth = now;
                    }
                }
            }
            // 구조물이 빠르게 돌면 직전 위치를 지금 모양에 대 본 "들어온 축"이 엉뚱할 수 있다. 한 틱 이동량보다 깊으면 가장 얕은 축으로.
            int chosen = enteredAxis >= 0 && enteredDepth <= shallow + EntrySlack ? enteredAxis : shallowAxis;
            var axisVector = Axis(chosen, u, v);
            float r = propHalf.X * SimMath.Abs(Vec2.Dot(axisVector, u)) + propHalf.Y * SimMath.Abs(Vec2.Dot(axisVector, v)) + half * (SimMath.Abs(axisVector.X) + SimMath.Abs(axisVector.Y));
            float d = Vec2.Dot(delta, axisVector);
            float prior = Vec2.Dot(before, axisVector);
            float side = prior > 0f ? 1f : prior < 0f ? -1f : (d >= 0f ? 1f : -1f);
            normal = axisVector * side;
            depth = r - side * d;
            return true;
        }

        /// <summary>들어온 축을 믿는 깊이 차이 한계(최대 돌진 34 × 1틱 ≈ 0.57보다 작게, 회전하는 막대의 가짜 축을 거른다).</summary>
        const float EntrySlack = 0.35f;

        static Vec2 Axis(int axis, Vec2 u, Vec2 v) =>
            axis == 0 ? u : axis == 1 ? v : axis == 2 ? new Vec2(1f, 0f) : new Vec2(0f, 1f);
    }
}
