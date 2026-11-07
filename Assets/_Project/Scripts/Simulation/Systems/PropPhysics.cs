namespace Knockbox.Simulation
{
    /// <summary>접촉점 하나(한 틱의 계산 결과, 상태 아님). 웜 스타트 충격량은 PropBody.Warm에서 읽고 틱 끝에 되돌려 쓴다.</summary>
    public struct PropContact
    {
        public int Body;
        /// <summary>상대 강체(-1이면 지형·경계). 법선은 상대 → Body.</summary>
        public int Other;
        /// <summary>웜 스타트 번호(상대 × 64 + 접점 특징 + 1). 같은 두 면의 같은 끝이면 틱이 바뀌어도 같다.</summary>
        public int Id;
        /// <summary>Body를 밀어내는 방향.</summary>
        public Vec2 Normal;
        public Vec2 Point;
        /// <summary>찾을 때의 틈(음수면 겹침). 서브스텝 동안은 두 접점의 이동량으로 갱신한다.</summary>
        public float Separation;
        /// <summary>접점의 몸 좌표(틈 갱신용)와, 찾을 때의 무게중심 → 접점(풀이에는 고정해서 쓴다, Box2D v3와 같음).</summary>
        public Vec2 LocalBody;
        public Vec2 LocalOther;
        public Vec2 RBody;
        public Vec2 ROther;
        public float NormalMass;
        public float TangentMass;
        public float Friction;
        public float Restitution;
        /// <summary>서브스텝 하나 분량의 누적 충격량(웜 스타트로 이어 쓴다).</summary>
        public float NormalImpulse;
        public float TangentImpulse;
        public float MaxNormalImpulse;
        /// <summary>찾을 때의 법선 방향 상대 속도(반발 계산용).</summary>
        public float Approach;
        /// <summary>한 면에서 나온 두 점 중 첫째(바로 다음 접촉이 짝). 두 순서로 풀어 평균한다.</summary>
        public bool Twin;
    }

    /// <summary>구조물 물리의 작업 공간. SimWorld가 한 번 만들고 매 틱 다시 쓴다(할당 없음).</summary>
    public sealed class PropScratch
    {
        public const int MaxContacts = 512;
        public readonly PropContact[] Contacts = new PropContact[MaxContacts];
        public int ContactCount;
        /// <summary>이번 틱에 몸마다 받은 바깥 충격(접촉·캐릭터)의 합. 끊어짐 판정에 쓴다.</summary>
        public readonly float[] TickImpulse = new float[PropState.Capacity];
        /// <summary>이번 틱이 시작할 때의 위치·회전(잠들기 판정은 실제로 움직인 양으로 한다).</summary>
        public readonly Vec2[] StartPosition = new Vec2[PropState.Capacity];
        public readonly Vec2[] StartRotation = new Vec2[PropState.Capacity];
        /// <summary>이번 틱 풀이의 역질량·역관성(잠든 몸은 0: 움직이지 않는 몸으로 다룬다).</summary>
        public readonly float[] InvMass = new float[PropState.Capacity];
        public readonly float[] InvInertia = new float[PropState.Capacity];
        /// <summary>웜 스타트를 저장할 때 이 몸의 칸을 이번 틱에 비웠는지.</summary>
        public readonly bool[] Rebuilt = new bool[PropState.Capacity];
        /// <summary>틱 시작에 한 번 담는 몸 정보(쌍 훑기를 배열로만 하기 위해): 깨어 있음, 거의 멈춤, 위치, 반 크기, 축 정렬 외곽, 최대 점 속력.</summary>
        public readonly bool[] Awake = new bool[PropState.Capacity];
        public readonly bool[] Still = new bool[PropState.Capacity];
        public readonly Vec2[] Center = new Vec2[PropState.Capacity];
        public readonly Vec2[] Half = new Vec2[PropState.Capacity];
        public readonly Vec2[] Reach = new Vec2[PropState.Capacity];
        public readonly float[] Speed = new float[PropState.Capacity];
        /// <summary>이번 틱에 깨어 있는 몸 중 연결(줄·축·경첩)이 있는 몸, 그중 줄인 몸(번호 순서).</summary>
        public readonly int[] Jointed = new int[PropState.Capacity];
        public int JointedCount;
        public readonly int[] Roped = new int[PropState.Capacity];
        public int RopedCount;
        /// <summary>이번 틱에 깨어 있는 몸(번호 순서).</summary>
        public readonly int[] AwakeList = new int[PropState.Capacity];
        public int AwakeCount;
        /// <summary>접촉 풀이 동안의 몸 상태(위치, 회전, 속도, 각속도). 풀이 앞뒤로 깨어 있는 몸만 PropState와 주고받는다(잠든 몸은 속도 0).</summary>
        public readonly Vec2[] P = new Vec2[PropState.Capacity];
        public readonly Vec2[] Q = new Vec2[PropState.Capacity];
        public readonly Vec2[] V = new Vec2[PropState.Capacity];
        public readonly float[] W = new float[PropState.Capacity];
    }

    /// <summary>
    /// 구조물 물리(09-props-physics.md 2절). 1단계(CP14): 회전하는 직사각형 강체, 줄(최대 거리), 축(점 고정),
    /// 고정 지형·임시 물체·화면 경계와의 충돌. 2단계(CP15): 경첩·시소(점 고정 + 각도 한계 + 복원 스프링), 몸-몸 줄(연결 구조),
    /// 강체끼리의 충돌(마찰 포함), 기준 충격 이상에서 끊어짐.
    /// 3단계(CP16): 쌓인 상자를 위한 접촉 해법(틱마다 한 번 찾는 예측 접촉, 서브스텝 부드러운 제약, 웜 스타트, 이완, 반발 단계),
    /// 잠든 몸은 풀이에서 움직이지 않는 몸으로 다루고 움직이는 몸이 닿으면 깬다.
    /// 회전은 (cos, sin)으로 적분해 정규화한다. 몸 번호·쌍 번호 순서대로 처리하고 반복 횟수가 고정이라 결정론적이다.
    /// </summary>
    public static class PropPhysics
    {
        /// <summary>지금 강체로 움직이는 구조물 단계(09 2절 표). 이 단계 이하의 종류만 SimState.Props에 들어간다.</summary>
        public const int ImplementedStage = 3;

        /// <summary>맵의 강체 구조물을 처음 놓인 모양(정지, 각도 0, 잠든 채)으로.</summary>
        public static void Init(ref PropState props, ArenaDef arena, SimConfig config)
        {
            props = default;
            props.Count = arena.DynamicPropCount;
            for (int i = 0; i < props.Count; i++)
            {
                ref var b = ref PropState.Get(ref props, i);
                b.Position = arena.GetProp(arena.DynamicProp(i)).Center;
                b.Rotation = new Vec2(1f, 0f);
                b.LastToucher = -1;
                b.SleepTicks = config.Combat.Props.SleepTicks;
            }
        }

        public static bool Asleep(in PropBody b, in PropParams c) => b.SleepTicks >= c.SleepTicks;

        /// <summary>잠들 만큼 느린지(선속도·각속도 모두 기준 아래).</summary>
        static bool Slow(in PropBody b, in PropParams c) =>
            b.Velocity.LengthSquared < c.SleepSpeed * c.SleepSpeed && b.AngularVelocity * b.AngularVelocity < c.SleepAngularSpeed * c.SleepAngularSpeed;

        public static float Mass(in PropDef p, in PropParams c) => c.Density * 4f * p.HalfSize.X * p.HalfSize.Y;

        public static float Inertia(in PropDef p, float mass) =>
            mass * (4f * p.HalfSize.X * p.HalfSize.X + 4f * p.HalfSize.Y * p.HalfSize.Y) / 12f;

        static void InverseMass(ArenaDef arena, in PropParams c, int body, out float invM, out float invI)
        {
            ref readonly var def = ref arena.GetProp(arena.DynamicProp(body));
            float m = Mass(def, c);
            invM = 1f / m;
            invI = 1f / Inertia(def, m);
        }

        /// <summary>몸이 깨어 있으면 줄로 이어진 상대도 깨운다(사슬이 함께 움직이게). 번호 순서로 두 번 훑어 양방향으로 전한다.</summary>
        static void WakeLinked(ref PropState props, ArenaDef arena)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    ref readonly var joint = ref arena.GetJoint(i);
                    if (joint.OtherBody < 0)
                        continue;
                    ref var b = ref PropState.Get(ref props, i);
                    ref var o = ref PropState.Get(ref props, joint.OtherBody);
                    if (b.Broken != 0)
                        continue;
                    if (b.SleepTicks == 0 || o.SleepTicks == 0)
                    {
                        b.SleepTicks = 0;
                        o.SleepTicks = 0;
                    }
                }
            }
        }

        /// <summary>
        /// 줄로 이어진 두 몸의 잠들기 틱을 둘 중 작은 값으로 맞춘다(한쪽만 먼저 잠들면 그 몸은 연결 보정을 받지 않아 줄이 늘어난다).
        /// 번호 순서로 정방향·역방향 한 번씩 훑어 사슬 전체로 전한다.
        /// </summary>
        static void SyncLinkedSleep(ref PropState props, ArenaDef arena)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < props.Count; k++)
                {
                    int i = pass == 0 ? k : props.Count - 1 - k;
                    ref readonly var joint = ref arena.GetJoint(i);
                    if (joint.OtherBody < 0)
                        continue;
                    ref var b = ref PropState.Get(ref props, i);
                    if (b.Broken != 0)
                        continue;
                    ref var o = ref PropState.Get(ref props, joint.OtherBody);
                    int m = SimMath.Min(b.SleepTicks, o.SleepTicks);
                    b.SleepTicks = m;
                    o.SleepTicks = m;
                }
            }
        }

        /// <summary>
        /// 실제로 밀고 있는(충격량 &gt; 0) 접촉으로 맞닿은 깨어 있는 몸끼리 잠들기 틱을 작은 쪽으로 맞춰 무리가 함께 잠들게 한다(CP16, Box2D의 섬 단위 잠들기와 같은 생각).
        /// 쌓인 상자에서 아래 상자만 먼저 잠들면(움직이지 않는 몸이 되면) 위 상자들의 접촉 평형이 바뀌어 튀어 오르고 잠들기가 다시 0이 됐다.
        /// 접촉 목록 순서로 정방향·역방향 두 번씩 훑는다.
        /// </summary>
        static void SyncTouchingSleep(ref PropState props, in PropParams c, PropScratch scratch)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                for (int n = 0; n < scratch.ContactCount; n++)
                {
                    ref readonly var contact = ref scratch.Contacts[(pass & 1) == 0 ? n : scratch.ContactCount - 1 - n];
                    if (contact.Other < 0 || contact.NormalImpulse <= 0f)
                        continue;
                    ref var a = ref PropState.Get(ref props, contact.Body);
                    ref var b = ref PropState.Get(ref props, contact.Other);
                    if (scratch.InvMass[contact.Body] == 0f || scratch.InvMass[contact.Other] == 0f)
                        continue;
                    int m = SimMath.Min(a.SleepTicks, b.SleepTicks);
                    a.SleepTicks = m;
                    b.SleepTicks = m;
                }
            }
        }

        /// <summary>
        /// 한 틱. 히트스탑과 상관없이 계속 움직인다(09 4절 초기 결정).
        /// CP16: 접촉은 틱마다 한 번 찾고(예측 거리 = 속도 × 틱 + 여유), 서브스텝마다 속도 적분 → 웜 스타트 → 부드러운 접촉·연결 풀이 →
        /// 위치 적분 → 줄 위치 보정 → 이완(밀어내기 속도 없이 한 번 더) 순서로 푼 뒤, 반발 단계와 충격량 저장으로 끝낸다
        /// (Box2D v3 solver.c의 soft step 순서를 참고, MIT. 코드는 이 구조에 맞춰 직접 작성).
        /// </summary>
        public static void Step(ref PropState props, ArenaDef arena, SimConfig config, PropScratch scratch, int tick, SimEventBuffer events)
        {
            if (props.Count == 0)
                return;
            ref readonly var c = ref config.Combat.Props;
            int substeps = SimMath.Max(1, c.Substeps);
            float h = SimConfig.Dt / substeps;
            bool anyAwake = false;
            for (int i = 0; i < props.Count; i++)
            {
                ref var b = ref PropState.Get(ref props, i);
                scratch.TickImpulse[i] = 0f;
                scratch.StartPosition[i] = b.Position;
                scratch.StartRotation[i] = b.Rotation;
                if (b.HitCooldown0 > 0)
                    b.HitCooldown0--;
                if (b.HitCooldown1 > 0)
                    b.HitCooldown1--;
                if (b.StrikeCooldown0 > 0)
                    b.StrikeCooldown0--;
                if (b.StrikeCooldown1 > 0)
                    b.StrikeCooldown1--;
                if (b.LooseTicks > 0)
                    b.LooseTicks--;
                anyAwake |= !Asleep(b, c);
            }
            if (!anyAwake)
                return;
            CacheBodies(ref props, arena, c, scratch);
            WakeTouched(ref props, arena, c, scratch);
            WakeLinked(ref props, arena);
            scratch.JointedCount = 0;
            scratch.RopedCount = 0;
            scratch.AwakeCount = 0;
            for (int i = 0; i < props.Count; i++)
            {
                ref readonly var body = ref PropState.At(in props, i);
                scratch.Awake[i] = !Asleep(body, c);
                scratch.P[i] = body.Position;
                scratch.Q[i] = body.Rotation;
                scratch.V[i] = Vec2.Zero;
                scratch.W[i] = 0f;
                if (!scratch.Awake[i])
                {
                    scratch.InvMass[i] = 0f;
                    scratch.InvInertia[i] = 0f;
                    continue;
                }
                scratch.AwakeList[scratch.AwakeCount++] = i;
                InverseMass(arena, c, i, out scratch.InvMass[i], out scratch.InvInertia[i]);
                var kind = arena.GetJoint(i).Kind;
                if (kind != PropJointKind.None)
                    scratch.Jointed[scratch.JointedCount++] = i;
                if (kind == PropJointKind.Rope)
                    scratch.Roped[scratch.RopedCount++] = i;
            }
            FindContacts(ref props, arena, c, scratch);
            LoadBodies(in props, scratch);
            PrepareContacts(ref props, arena, c, scratch);
            var soft = Softness.Make(SimMath.Min(c.ContactHertz, 0.25f / h), c.ContactDampingRatio, h);
            for (int s = 0; s < substeps; s++)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    ref var b = ref PropState.Get(ref props, i);
                    if (Asleep(b, c))
                        continue;
                    b.Velocity.Y -= c.Gravity * h;
                    b.Velocity *= 1f / (1f + h * c.LinearDamping);
                    b.AngularVelocity *= 1f / (1f + h * c.AngularDamping);
                    ApplyPivotSpring(ref b, arena, c, i, h);
                }
                WarmStart(ref props, scratch);
                for (int it = 0; it < c.Iterations; it++)
                {
                    // 연결은 정방향과 역방향으로 한 번씩(무거운 틀에 가벼운 사슬이 이어져도 덜 늘어난다).
                    for (int k = 0; k < scratch.JointedCount; k++)
                        SolveJoint(ref props, arena, c, scratch.Jointed[k], h);
                    for (int k = scratch.JointedCount - 1; k >= 0; k--)
                        SolveJoint(ref props, arena, c, scratch.Jointed[k], h);
                    if (it >= c.Iterations - c.ContactIterations)
                        SolveContacts(ref props, c, scratch, soft, h, true);
                }
                for (int i = 0; i < props.Count; i++)
                {
                    ref var b = ref PropState.Get(ref props, i);
                    if (Asleep(b, c))
                        continue;
                    b.Position += b.Velocity * h;
                    b.Rotation = IntegrateRotation(b.Rotation, b.AngularVelocity * h);
                }
                // 늘어난 줄은 위치로 바로 되돌린다(반복 횟수만큼 정방향·역방향을 번갈아, 속도는 그대로라 에너지를 더하지 않는다).
                for (int it = 0; it < c.Iterations; it++)
                {
                    for (int k = 0; k < scratch.RopedCount; k++)
                        ProjectRope(ref props, arena, c, scratch.Roped[(it & 1) == 0 ? k : scratch.RopedCount - 1 - k]);
                }
                // 이완: 밀어내기 속도 없이 한 번 더 풀어, 겹침을 밀어낸 속도가 튕김으로 남지 않게 한다.
                SolveContacts(ref props, c, scratch, soft, h, false);
                for (int k = 0; k < scratch.ContactCount; k++)
                {
                    ref readonly var contact = ref scratch.Contacts[k];
                    scratch.TickImpulse[contact.Body] += contact.NormalImpulse;
                    if (contact.Other >= 0)
                        scratch.TickImpulse[contact.Other] += contact.NormalImpulse;
                }
            }
            ApplyRestitution(ref props, c, scratch);
            StoreImpulses(ref props, c, scratch);
            ReportImpacts(c, scratch, tick, events);
            for (int i = 0; i < props.Count; i++)
            {
                ref var b = ref PropState.Get(ref props, i);
                if (Asleep(b, c))
                    continue;
                // 속도가 아니라 한 틱 동안 실제로 움직인 양으로 판정한다. 가벼운 고리에 무거운 부품이 매달린 사슬은
                // 순차 풀이가 다 수렴하지 않아 속도가 조금 남지만, 줄 위치 보정이 제자리에 붙들어 실제로는 멈춰 있다.
                var moved = (b.Position - scratch.StartPosition[i]) * (1f / SimConfig.Dt);
                float turned = PropBody.Cross(scratch.StartRotation[i], b.Rotation) * (1f / SimConfig.Dt);
                bool still = moved.LengthSquared < c.SleepSpeed * c.SleepSpeed && turned * turned < c.SleepAngularSpeed * c.SleepAngularSpeed;
                b.SleepTicks = still ? b.SleepTicks + 1 : 0;
            }
            SyncTouchingSleep(ref props, c, scratch);
            SyncLinkedSleep(ref props, arena);
            for (int i = 0; i < props.Count; i++)
            {
                ref var b = ref PropState.Get(ref props, i);
                if (Asleep(b, c))
                {
                    b.Velocity = Vec2.Zero;
                    b.AngularVelocity = 0f;
                }
            }
            CheckBreaks(ref props, arena, scratch, tick, events);
        }

        /// <summary>이번 틱에 받은 바깥 충격이 기준 이상이면 연결을 끊는다(끊어진 몸은 깨어 자유롭게 떨어진다).</summary>
        public static void CheckBreaks(ref PropState props, ArenaDef arena, PropScratch scratch, int tick, SimEventBuffer events)
        {
            for (int i = 0; i < props.Count; i++)
            {
                ref readonly var joint = ref arena.GetJoint(i);
                ref var b = ref PropState.Get(ref props, i);
                if (b.Broken != 0 || joint.BreakImpulse <= 0f || scratch.TickImpulse[i] < joint.BreakImpulse)
                    continue;
                b.Broken = 1;
                b.SleepTicks = 0;
                var at = joint.OtherBody >= 0
                    ? PropState.At(in props, joint.OtherBody).ToWorld(joint.OtherLocal)
                    : joint.Pinned ? joint.WorldAnchor : b.ToWorld(joint.LocalAnchor);
                events.Add(new SimEvent(tick, SimEventType.JointBreak, b.LastToucher >= 0 ? b.LastToucher : SimEvent.NoFighter, at, Vec2.Zero, scratch.TickImpulse[i]));
            }
        }

        /// <summary>q + h·ω·(−s, c)를 정규화(삼각함수 없음).</summary>
        public static Vec2 IntegrateRotation(Vec2 q, float angle)
        {
            var r = new Vec2(q.X - angle * q.Y, q.Y + angle * q.X);
            float length = SimMath.Sqrt(r.LengthSquared);
            return length > 1e-6f ? r / length : new Vec2(1f, 0f);
        }

        // 연결 ----------------------------------------------------------------------

        /// <summary>
        /// 경첩·시소의 복원: 토크 = −강성 × sin θ − 감쇠 × ω + 처음 자세의 중력 토크를 상쇄하는 토크
        /// (비어 있으면 수평을 유지하고, 밟으면 기울었다가 돌아온다). 처음 각도는 0이므로 sin θ = 회전의 Y.
        /// </summary>
        static void ApplyPivotSpring(ref PropBody b, ArenaDef arena, in PropParams c, int i, float h)
        {
            ref readonly var joint = ref arena.GetJoint(i);
            if (b.Broken != 0 || (joint.Kind != PropJointKind.Hinge && joint.Kind != PropJointKind.Seesaw))
                return;
            ref readonly var def = ref arena.GetProp(arena.DynamicProp(i));
            float m = Mass(def, c);
            float stiffness = joint.Kind == PropJointKind.Hinge ? c.HingeStiffness : c.SeesawStiffness;
            // 축 → 무게중심(처음 자세). 중력 토크 = r × (0, −mg) = −r.x·m·g, 이를 상쇄.
            float compensation = -joint.LocalAnchor.X * m * c.Gravity;
            float torque = compensation - stiffness * b.Rotation.Y - c.PivotDamping * b.AngularVelocity * m;
            b.AngularVelocity += torque / Inertia(def, m) * h;
        }

        static void SolveJoint(ref PropState props, ArenaDef arena, in PropParams c, int i, float h)
        {
            ref readonly var joint = ref arena.GetJoint(i);
            if (joint.Kind == PropJointKind.None)
                return;
            ref var b = ref PropState.Get(ref props, i);
            if (b.Broken != 0)
                return;
            InverseMass(arena, c, i, out float invM, out float invI);
            var r = PropBody.Rotate(b.Rotation, joint.LocalAnchor);
            var p = b.Position + r;
            if (joint.Pinned)
            {
                // 점 고정: 몸의 축 위치 속도를 0으로, 위치 오차는 일부만 속도로 되돌린다.
                var error = p - joint.WorldAnchor;
                var cdot = b.Velocity + PropBody.Cross(b.AngularVelocity, r) + error * (c.Correction / h);
                float k11 = invM + invI * r.Y * r.Y, k12 = -invI * r.X * r.Y, k22 = invM + invI * r.X * r.X;
                float det = k11 * k22 - k12 * k12;
                if (det > 1e-9f)
                {
                    float invDet = 1f / det;
                    var impulse = new Vec2(-(k22 * cdot.X - k12 * cdot.Y) * invDet, -(-k12 * cdot.X + k11 * cdot.Y) * invDet);
                    b.Velocity += impulse * invM;
                    b.AngularVelocity += invI * PropBody.Cross(r, impulse);
                }
                // 각도 한계(처음 자세 기준, sin θ로 비교). 넘어가는 쪽 각속도를 막고 넘은 만큼 되돌린다.
                float limit = joint.Kind == PropJointKind.Hinge ? c.HingeLimitSin : joint.Kind == PropJointKind.Seesaw ? c.SeesawLimitSin : 0f;
                if (limit > 0f && b.Rotation.X > 0f)
                {
                    // 이번 서브스텝에 한계를 넘을 만큼의 각속도도 막는다(예측). 이미 넘었으면 넘은 만큼 일부를 되돌린다.
                    float sin = b.Rotation.Y;
                    float upper = sin > limit ? -(sin - limit) * (c.Correction / h) : (limit - sin) / h;
                    float lower = sin < -limit ? (-limit - sin) * (c.Correction / h) : (-limit - sin) / h;
                    b.AngularVelocity = SimMath.Clamp(b.AngularVelocity, lower / b.Rotation.X, upper / b.Rotation.X);
                }
                return;
            }
            // 줄: 최대 거리. 안쪽으로만 당긴다. 느슨하면 남은 여유만큼만 멀어지게 해(예측) 펴지는 순간 길이를 넘지 않는다.
            // 다른 강체에 이어졌으면 두 몸에 반대로.
            bool linked = joint.OtherBody >= 0;
            var anchor = joint.WorldAnchor;
            var rOther = Vec2.Zero;
            float invMO = 0f, invIO = 0f;
            var vOther = Vec2.Zero;
            if (linked)
            {
                ref readonly var o = ref PropState.At(in props, joint.OtherBody);
                rOther = PropBody.Rotate(o.Rotation, joint.OtherLocal);
                anchor = o.Position + rOther;
                vOther = o.PointVelocity(anchor);
                InverseMass(arena, c, joint.OtherBody, out invMO, out invIO);
            }
            var d = p - anchor;
            float length = SimMath.Sqrt(d.LengthSquared);
            if (length < 1e-6f)
                return;
            var n = d / length;
            float rn = PropBody.Cross(r, n);
            float ro = PropBody.Cross(rOther, n);
            float kn = invM + invI * rn * rn + invMO + invIO * ro * ro;
            float vn = Vec2.Dot(n, b.Velocity + PropBody.Cross(b.AngularVelocity, r) - vOther);
            float stretch = length - joint.Length;
            float bias = stretch > 0f ? stretch * (c.Correction / h) : stretch / h;
            float lambda = -(vn + bias) / kn;
            if (lambda >= 0f)
                return;
            var pull = n * lambda;
            b.Velocity += pull * invM;
            b.AngularVelocity += invI * PropBody.Cross(r, pull);
            if (linked)
            {
                ref var o = ref PropState.Get(ref props, joint.OtherBody);
                o.Velocity -= pull * invMO;
                o.AngularVelocity -= invIO * PropBody.Cross(rOther, pull);
            }
        }

        /// <summary>
        /// 줄이 최대 길이를 넘었으면 두 몸(또는 몸 하나)의 위치·회전을 질량 비율대로 옮겨 길이로 되돌리고,
        /// 팽팽한 줄이면 줄을 더 늘리는 방향의 속도도 없앤다(남겨 두면 매 서브스텝 다시 늘어나 잠들지 못한다).
        /// </summary>
        static void ProjectRope(ref PropState props, ArenaDef arena, in PropParams c, int i)
        {
            ref readonly var joint = ref arena.GetJoint(i);
            if (joint.Kind != PropJointKind.Rope)
                return;
            ref var b = ref PropState.Get(ref props, i);
            if (b.Broken != 0 || Asleep(b, c))
                return;
            InverseMass(arena, c, i, out float invM, out float invI);
            var r = PropBody.Rotate(b.Rotation, joint.LocalAnchor);
            bool linked = joint.OtherBody >= 0;
            var anchor = joint.WorldAnchor;
            var rOther = Vec2.Zero;
            float invMO = 0f, invIO = 0f;
            if (linked)
            {
                ref readonly var o = ref PropState.At(in props, joint.OtherBody);
                rOther = PropBody.Rotate(o.Rotation, joint.OtherLocal);
                anchor = o.Position + rOther;
                InverseMass(arena, c, joint.OtherBody, out invMO, out invIO);
            }
            var d = b.Position + r - anchor;
            float length = SimMath.Sqrt(d.LengthSquared);
            float stretch = length - joint.Length;
            // 느슨한 줄은 그대로(팽팽한 줄은 길이가 맞아도 늘리는 속도를 없앤다).
            if (stretch < -1e-4f || length < 1e-6f)
                return;
            var n = d / length;
            float rn = PropBody.Cross(r, n);
            float ro = PropBody.Cross(rOther, n);
            float kn = invM + invI * rn * rn + invMO + invIO * ro * ro;
            var vOther = linked ? PropState.At(in props, joint.OtherBody).PointVelocity(anchor) : Vec2.Zero;
            float vn = Vec2.Dot(n, b.Velocity + PropBody.Cross(b.AngularVelocity, r) - vOther);
            var move = n * (stretch > 0f ? -stretch / kn : 0f);
            var pull = n * (vn > 0f ? -vn / kn : 0f);
            b.Position += move * invM;
            b.Rotation = IntegrateRotation(b.Rotation, invI * PropBody.Cross(r, move));
            b.Velocity += pull * invM;
            b.AngularVelocity += invI * PropBody.Cross(r, pull);
            if (linked)
            {
                ref var o = ref PropState.Get(ref props, joint.OtherBody);
                o.Position -= move * invMO;
                o.Rotation = IntegrateRotation(o.Rotation, -invIO * PropBody.Cross(rOther, move));
                o.Velocity -= pull * invMO;
                o.AngularVelocity -= invIO * PropBody.Cross(rOther, pull);
            }
        }

        /// <summary>두 몸이 줄로 바로 이어졌는지(이어진 부품끼리는 충돌하지 않는다).</summary>
        static bool Connected(ArenaDef arena, in PropState props, int a, int b) =>
            (arena.GetJoint(a).OtherBody == b && PropState.At(in props, a).Broken == 0)
            || (arena.GetJoint(b).OtherBody == a && PropState.At(in props, b).Broken == 0);

        // 접촉 ----------------------------------------------------------------------
        // CP16: 지형·경계·강체 접촉을 모두 같은 방식으로 푼다. 틱마다 한 번 찾은 접촉점을 서브스텝 동안 두 몸의 이동량으로 갱신하고,
        // 겹쳐 있으면 부드러운 제약(강성 Hz·감쇠비), 틈이 있으면 그 틈만큼만 다가오게(예측 접촉) 막는다.
        // 접촉점 계산(분리축 + 기준 면에 맞댄 면을 잘라 최대 2점)과 풀이 수식은 Box2D v3(manifold.c b2ClipPolygons,
        // contact_solver.c, MIT License, Copyright (c) 2023 Erin Catto)를 참고해 이 구조에 맞춰 다시 썼다.

        /// <summary>몸 번호(0~47) 다음부터 지형 상자 번호, 그 다음이 화면 경계(웜 스타트 번호의 상대 부분).</summary>
        const int TerrainKey = 64;
        const int BoundaryKey = 4096;
        /// <summary>기준 면을 고를 때 상대 쪽을 우선하는 여유(같은 폭의 상자를 쌓아도 기준 면이 틱마다 바뀌지 않게).</summary>
        const float ReferenceTolerance = 0.0005f;

        /// <summary>부드러운 제약 계수(Box2D v3 b2MakeSoft와 같은 식).</summary>
        struct Softness
        {
            public float BiasRate;
            public float MassScale;
            public float ImpulseScale;

            public static Softness Make(float hertz, float zeta, float h)
            {
                if (hertz <= 0f)
                    return new Softness { BiasRate = 0f, MassScale = 1f, ImpulseScale = 0f };
                float omega = 6.2831855f * hertz;
                float a1 = 2f * zeta + h * omega;
                float a2 = h * omega * a1;
                float a3 = 1f / (1f + a2);
                return new Softness { BiasRate = omega / a1, MassScale = a2 * a3, ImpulseScale = a3 };
            }
        }

        public static bool IsBox(PropKind kind) => kind == PropKind.BoxPillar || kind == PropKind.LooseBox;

        static bool IsBox(ArenaDef arena, int body) => IsBox(arena.GetProp(arena.DynamicProp(body)).Kind);

        /// <summary>틱 시작에 몸 정보를 배열에 담는다(쌍 훑기는 이 배열로만 거른 뒤 가까운 쌍만 접촉을 계산한다).</summary>
        static void CacheBodies(ref PropState props, ArenaDef arena, in PropParams c, PropScratch scratch)
        {
            for (int i = 0; i < props.Count; i++)
            {
                ref readonly var b = ref PropState.At(in props, i);
                var half = arena.GetProp(arena.DynamicProp(i)).HalfSize;
                Extent(b, half, out float ex, out float ey);
                scratch.Awake[i] = !Asleep(b, c);
                scratch.Still[i] = Slow(b, c);
                scratch.Center[i] = b.Position;
                scratch.Half[i] = half;
                scratch.Reach[i] = new Vec2(ex, ey);
                scratch.Speed[i] = MaxPointSpeed(b, half);
            }
        }

        /// <summary>두 몸이 이번 틱에 닿을 수 있을 만큼 가까운지(축 정렬 외곽 + 예측 거리, 틱 시작 정보로).</summary>
        static bool NearCached(PropScratch scratch, int i, int j, in PropParams c)
        {
            float speculative = PairSpeculative(scratch, i, j, c);
            var d = scratch.Center[i] - scratch.Center[j];
            return SimMath.Abs(d.X) <= scratch.Reach[i].X + scratch.Reach[j].X + speculative
                   && SimMath.Abs(d.Y) <= scratch.Reach[i].Y + scratch.Reach[j].Y + speculative;
        }

        static float PairSpeculative(PropScratch scratch, int i, int j, in PropParams c) =>
            (scratch.Speed[i] + scratch.Speed[j]) * SimConfig.Dt + c.SpeculativeDistance;

        /// <summary>
        /// 잠든 몸이 움직이는 몸과 닿아 있으면 깨운다(틱 시작 상태로 판단해 한 틱에 한 칸씩 전해진다).
        /// 거의 멈춘 몸끼리 맞닿아 있는 것은 깨우지 않는다(S-133: 서로 잠들기를 되돌리면 맞닿아 쉬는 상자가 영원히 잠들지 못한다).
        /// 받침이 빠지면 빠지는 받침이 움직이므로 그 위 상자가 깬다.
        /// </summary>
        static void WakeTouched(ref PropState props, ArenaDef arena, in PropParams c, PropScratch scratch)
        {
            scratch.ContactCount = 0;
            for (int i = 0; i < props.Count; i++)
            {
                for (int j = i + 1; j < props.Count; j++)
                {
                    bool awakeA = scratch.Awake[i], awakeB = scratch.Awake[j];
                    if (awakeA == awakeB || (awakeA ? scratch.Still[i] : scratch.Still[j]) || !NearCached(scratch, i, j, c)
                        || Connected(arena, props, i, j))
                        continue;
                    ref var a = ref PropState.Get(ref props, i);
                    ref var bb = ref PropState.Get(ref props, j);
                    Collide(i, a, scratch.Half[i], j, j, bb, scratch.Half[j], PairSpeculative(scratch, i, j, c), -1f, scratch);
                    bool touched = scratch.ContactCount > 0;
                    scratch.ContactCount = 0;
                    if (!touched)
                        continue;
                    // 깨어난 몸은 아직 멈춰 있으므로(속도 0) 이 틱에 다른 몸을 다시 깨우지 않는다(한 틱에 한 칸).
                    int woken = awakeA ? j : i;
                    PropState.Get(ref props, woken).SleepTicks = 0;
                    scratch.Awake[woken] = true;
                    scratch.Still[woken] = true;
                }
            }
        }

        /// <summary>깨어 있는 몸의 지형·경계 접촉, 적어도 하나가 깨어 있는 강체 쌍의 접촉을 몸 번호·쌍 번호 순서로 찾고, 웜 스타트 충격량을 읽는다.</summary>
        static void FindContacts(ref PropState props, ArenaDef arena, in PropParams c, PropScratch scratch)
        {
            scratch.ContactCount = 0;
            for (int i = 0; i < props.Count; i++)
            {
                if (!scratch.Awake[i])
                    continue;
                ref readonly var b = ref PropState.At(in props, i);
                var half = scratch.Half[i];
                float ex = scratch.Reach[i].X, ey = scratch.Reach[i].Y;
                float speculative = scratch.Speed[i] * SimConfig.Dt + c.SpeculativeDistance;
                for (int k = 0; k < arena.BoxCount; k++)
                {
                    ref readonly var box = ref arena.GetBox(k);
                    if (box.Kind == TerrainKind.Spike)
                        continue;
                    if (b.Position.X + ex + speculative < box.MinX || b.Position.X - ex - speculative > box.MaxX
                        || b.Position.Y + ey + speculative < box.MinY || b.Position.Y - ey - speculative > box.MaxY)
                        continue;
                    // 단방향 발판은 위에서 내려올 때만 막는다.
                    if (box.Kind == TerrainKind.OneWay && b.Position.Y - ey < box.MaxY - 0.25f)
                        continue;
                    var boxBody = new PropBody
                    {
                        Position = new Vec2((box.MinX + box.MaxX) * 0.5f, (box.MinY + box.MaxY) * 0.5f),
                        Rotation = new Vec2(1f, 0f),
                    };
                    var boxHalf = new Vec2((box.MaxX - box.MinX) * 0.5f, (box.MaxY - box.MinY) * 0.5f);
                    Collide(i, b, half, -1, TerrainKey + k, boxBody, boxHalf, speculative, -1f, scratch);
                }
                if (arena.HasBoundary)
                    CollideBoundary(i, b, half, arena, speculative, c.BoundaryRestitution, scratch);
            }
            for (int i = 0; i < props.Count; i++)
            {
                for (int j = i + 1; j < props.Count; j++)
                {
                    if ((!scratch.Awake[i] && !scratch.Awake[j]) || !NearCached(scratch, i, j, c) || Connected(arena, props, i, j))
                        continue;
                    Collide(i, PropState.At(in props, i), scratch.Half[i], j, j, PropState.At(in props, j), scratch.Half[j], PairSpeculative(scratch, i, j, c), -1f, scratch);
                }
            }
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref var contact = ref scratch.Contacts[k];
                PropState.At(in props, contact.Body).Warm.Find(contact.Id, out contact.NormalImpulse, out contact.TangentImpulse);
                contact.MaxNormalImpulse = 0f;
            }
        }

        static void Extent(in PropBody b, Vec2 half, out float ex, out float ey)
        {
            var u = b.Rotation;
            ex = SimMath.Abs(u.X) * half.X + SimMath.Abs(u.Y) * half.Y;
            ey = SimMath.Abs(u.Y) * half.X + SimMath.Abs(u.X) * half.Y;
        }

        /// <summary>몸의 어느 점이든 낼 수 있는 최대 속력(선속도 + 각속도 × 대각선 반).</summary>
        static float MaxPointSpeed(in PropBody b, Vec2 half) =>
            SimMath.Sqrt(b.Velocity.LengthSquared) + SimMath.Abs(b.AngularVelocity) * SimMath.Sqrt(half.LengthSquared);

        /// <summary>restitution &lt; 0이면 준비 단계에서 몸 종류로 정한다.</summary>
        static void Add(PropScratch scratch, int body, int other, int id, Vec2 normal, Vec2 point, float separation, float restitution)
        {
            if (scratch.ContactCount >= PropScratch.MaxContacts)
                return;
            scratch.Contacts[scratch.ContactCount++] = new PropContact
            {
                Body = body, Other = other, Id = id, Normal = normal, Point = point, Separation = separation, Restitution = restitution,
            };
        }

        static void Corner(in PropBody b, Vec2 half, int k, out Vec2 corner)
        {
            var local = new Vec2((k & 1) == 0 ? -half.X : half.X, (k & 2) == 0 ? -half.Y : half.Y);
            corner = b.ToWorld(local);
        }

        /// <summary>면 f의 바깥 법선: 0 +x, 1 +y, 2 −x, 3 −y(몸 좌표).</summary>
        static Vec2 FaceNormal(in PropBody b, int f)
        {
            var u = b.Rotation;
            return f == 0 ? u : f == 1 ? new Vec2(-u.Y, u.X) : f == 2 ? -u : new Vec2(u.Y, -u.X);
        }

        /// <summary>
        /// 기준 상자 r의 면 중 상자 i와의 틈이 가장 큰 면(분리축)과 그 틈(음수면 겹침). 면 0 +x, 1 +y, 2 −x, 3 −y(몸 좌표).
        /// 반대쪽 면은 같은 축의 부호만 바뀌므로 축 두 개만 계산한다(스칼라로 풀어 씀).
        /// </summary>
        static void MaxSeparation(in PropBody r, Vec2 halfR, in PropBody i, Vec2 halfI, out int face, out float separation)
        {
            float ux = r.Rotation.X, uy = r.Rotation.Y;
            float ix = i.Rotation.X, iy = i.Rotation.Y;
            float dx = i.Position.X - r.Position.X, dy = i.Position.Y - r.Position.Y;
            // 축 u = (ux, uy), v = (−uy, ux). 상자 i의 축 iu = (ix, iy), iv = (−iy, ix).
            float du = dx * ux + dy * uy;
            float dv = dx * -uy + dy * ux;
            float reachU = halfI.X * SimMath.Abs(ux * ix + uy * iy) + halfI.Y * SimMath.Abs(ux * -iy + uy * ix);
            float reachV = halfI.X * SimMath.Abs(-uy * ix + ux * iy) + halfI.Y * SimMath.Abs(-uy * -iy + ux * ix);
            face = 0;
            separation = du - halfR.X - reachU;
            float s1 = dv - halfR.Y - reachV;
            if (s1 > separation + 1e-6f)
            {
                separation = s1;
                face = 1;
            }
            float s2 = -du - halfR.X - reachU;
            if (s2 > separation + 1e-6f)
            {
                separation = s2;
                face = 2;
            }
            float s3 = -dv - halfR.Y - reachV;
            if (s3 > separation + 1e-6f)
            {
                separation = s3;
                face = 3;
            }
        }

        /// <summary>
        /// 회전한 직사각형 두 개(Body, 상대). 분리축이 예측 거리보다 멀면 접촉 없음. 아니면 틈이 큰 쪽 면을 기준 면으로(비슷하면 상대 쪽),
        /// 다른 상자에서 기준 면과 가장 마주 보는 면을 기준 면의 양 끝으로 잘라 남은 끝점(최대 2개) 중 틈이 예측 거리 안인 것을 접촉점으로.
        /// 법선은 상대 → Body. 넓은 상자 위의 좁은 상자도 두 점으로 받친다(CP15의 꼭짓점 방식은 한 점만 남아 쌓기에 맞지 않았다).
        /// </summary>
        static void Collide(int body, in PropBody b, Vec2 half, int other, int key, in PropBody o, Vec2 otherHalf, float speculative, float restitution, PropScratch scratch)
        {
            MaxSeparation(o, otherHalf, b, half, out int faceO, out float sepO);
            if (sepO > speculative)
                return;
            MaxSeparation(b, half, o, otherHalf, out int faceB, out float sepB);
            if (sepB > speculative)
                return;
            if (sepB > sepO + ReferenceTolerance)
                Clip(body, other, key, b, half, faceB, o, otherHalf, true, speculative, restitution, scratch);
            else
                Clip(body, other, key, o, otherHalf, faceO, b, half, false, speculative, restitution, scratch);
        }

        static void Clip(int body, int other, int key, in PropBody r, Vec2 halfR, int face, in PropBody inc, Vec2 halfI, bool bodyIsReference,
            float speculative, float restitution, PropScratch scratch)
        {
            var n = FaceNormal(r, face);
            var t = new Vec2(-n.Y, n.X);
            float reach = (face & 1) == 0 ? halfR.X : halfR.Y;
            float span = (face & 1) == 0 ? halfR.Y : halfR.X;
            var center = r.Position + n * reach;
            // 다른 상자에서 기준 면과 가장 마주 보는 면.
            int incident = 0;
            float facing = float.MaxValue;
            for (int g = 0; g < 4; g++)
            {
                float d = Vec2.Dot(FaceNormal(inc, g), n);
                if (d < facing - 1e-6f)
                {
                    facing = d;
                    incident = g;
                }
            }
            var ni = FaceNormal(inc, incident);
            var ti = new Vec2(-ni.Y, ni.X);
            float incReach = (incident & 1) == 0 ? halfI.X : halfI.Y;
            float incSpan = (incident & 1) == 0 ? halfI.Y : halfI.X;
            var mid = inc.Position + ni * incReach;
            var v1 = mid - ti * incSpan;
            var v2 = mid + ti * incSpan;
            float s1 = Vec2.Dot(v1 - center, t), s2 = Vec2.Dot(v2 - center, t);
            int e1 = 0, e2 = 1;
            if (s1 > s2)
            {
                (v1, v2) = (v2, v1);
                (s1, s2) = (s2, s1);
                e1 = 1;
                e2 = 0;
            }
            // 법선은 상대 → Body. 기준 면이 Body 쪽이면 반대.
            var normal = bodyIsReference ? -n : n;
            int feature = (bodyIsReference ? 32 : 0) | (face << 3) | (incident << 1);
            if (s2 < -span || s1 > span)
            {
                // 면끼리 겹치는 구간이 없음(꼭짓점끼리 스침): 기준 면에 가까운 끝 하나만.
                var v = s2 < -span ? v2 : v1;
                int e = s2 < -span ? e2 : e1;
                AddClipped(scratch, body, other, key, feature | e, n, normal, center, v, speculative, restitution);
                return;
            }
            var p1 = s1 < -span ? v1 + (v2 - v1) * ((-span - s1) / (s2 - s1)) : v1;
            // 각 끝은 자기 쪽에서 잘라 낸다(거울상에서 같은 점이 비트 단위로 같게 나오도록).
            var p2 = s2 > span ? v2 + (v1 - v2) * ((s2 - span) / (s2 - s1)) : v2;
            int before = scratch.ContactCount;
            AddClipped(scratch, body, other, key, feature | e1, n, normal, center, p1, speculative, restitution);
            AddClipped(scratch, body, other, key, feature | e2, n, normal, center, p2, speculative, restitution);
            if (scratch.ContactCount == before + 2)
                scratch.Contacts[before].Twin = true;
        }

        /// <summary>틈이 예측 거리 안이면 두 면의 가운데에 접촉점을 둔다.</summary>
        static void AddClipped(PropScratch scratch, int body, int other, int key, int feature, Vec2 n, Vec2 normal, Vec2 center, Vec2 p,
            float speculative, float restitution)
        {
            float separation = Vec2.Dot(p - center, n);
            if (separation > speculative)
                return;
            Add(scratch, body, other, key * 64 + feature + 1, normal, p - n * (0.5f * separation), separation, restitution);
        }

        /// <summary>
        /// 화면 경계: 벽마다 예측 거리 안의 꼭짓점을 접촉으로(벽 순서 +x, −x, +y, −y). 같은 벽에 닿은 두 꼭짓점은 짝으로 묶어
        /// 두 순서로 풀어 평균한다(꼭짓점 번호 순서는 거울상에서 바뀐다).
        /// </summary>
        static void CollideBoundary(int body, in PropBody b, Vec2 half, ArenaDef arena, float speculative, float restitution, PropScratch scratch)
        {
            float bx = arena.BoundaryHalfWidth, by = arena.BoundaryHalfHeight;
            Extent(b, half, out float ex, out float ey);
            for (int side = 0; side < 4; side++)
            {
                // 이 벽에서 예측 거리보다 멀면 꼭짓점을 계산하지 않는다.
                float far = side == 0 ? bx - (b.Position.X + ex) : side == 1 ? b.Position.X - ex + bx : side == 2 ? by - (b.Position.Y + ey) : b.Position.Y - ey + by;
                if (far >= speculative)
                    continue;
                var normal = side == 0 ? new Vec2(-1f, 0f) : side == 1 ? new Vec2(1f, 0f) : side == 2 ? new Vec2(0f, -1f) : new Vec2(0f, 1f);
                float wall = side < 2 ? bx : by;
                int before = scratch.ContactCount;
                for (int k = 0; k < 4; k++)
                {
                    Corner(b, half, k, out var corner);
                    // 벽까지의 틈: 벽 안쪽 법선 방향으로 잰 꼭짓점 위치 + 벽 거리.
                    float separation = Vec2.Dot(corner, normal) + wall;
                    if (separation < speculative)
                        Add(scratch, body, -1, (BoundaryKey + side) * 64 + k + 1, normal, corner, separation, restitution);
                }
                if (scratch.ContactCount == before + 2)
                    scratch.Contacts[before].Twin = true;
            }
        }

        /// <summary>접점의 몸 좌표, 고정 팔, 유효 질량, 마찰·반발, 찾을 때의 상대 속도.</summary>
        static void PrepareContacts(ref PropState props, ArenaDef arena, in PropParams c, PropScratch scratch)
        {
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref var contact = ref scratch.Contacts[k];
                ref readonly var b = ref PropState.At(in props, contact.Body);
                float invM = scratch.InvMass[contact.Body], invI = scratch.InvInertia[contact.Body];
                float invMO = 0f, invIO = 0f;
                contact.RBody = contact.Point - b.Position;
                contact.LocalBody = PropBody.InverseRotate(b.Rotation, contact.RBody);
                bool box = IsBox(arena, contact.Body);
                if (contact.Other >= 0)
                {
                    ref readonly var o = ref PropState.At(in props, contact.Other);
                    invMO = scratch.InvMass[contact.Other];
                    invIO = scratch.InvInertia[contact.Other];
                    contact.ROther = contact.Point - o.Position;
                    contact.LocalOther = PropBody.InverseRotate(o.Rotation, contact.ROther);
                    box |= IsBox(arena, contact.Other);
                }
                else
                {
                    contact.ROther = Vec2.Zero;
                    contact.LocalOther = Vec2.Zero;
                }
                var n = contact.Normal;
                var t = new Vec2(-n.Y, n.X);
                float rn = PropBody.Cross(contact.RBody, n), ron = PropBody.Cross(contact.ROther, n);
                float rt = PropBody.Cross(contact.RBody, t), rot = PropBody.Cross(contact.ROther, t);
                float kn = invM + invI * rn * rn + invMO + invIO * ron * ron;
                float kt = invM + invI * rt * rt + invMO + invIO * rot * rot;
                contact.NormalMass = kn > 0f ? 1f / kn : 0f;
                contact.TangentMass = kt > 0f ? 1f / kt : 0f;
                contact.Friction = box ? c.BoxFriction : c.Friction;
                // 기둥에서 빠지는 중인 상자는 다른 상자와 미끄러진다(위 상자를 끌고 가지 않고, 아래 상자를 밀지 않는다).
                if (contact.Other >= 0 && (b.LooseTicks > 0 || PropState.At(in props, contact.Other).LooseTicks > 0)
                    && IsBox(arena, contact.Body) && IsBox(arena, contact.Other))
                    contact.Friction = 0f;
                if (contact.Restitution < 0f)
                    contact.Restitution = box ? c.BoxRestitution : c.Restitution;
                contact.Approach = Vec2.Dot(n, RelativeVelocity(scratch, contact));
            }
        }

        /// <summary>깨어 있는 몸의 위치·회전·속도를 풀이 배열로.</summary>
        static void LoadBodies(in PropState props, PropScratch scratch)
        {
            for (int k = 0; k < scratch.AwakeCount; k++)
            {
                int i = scratch.AwakeList[k];
                ref readonly var b = ref PropState.At(in props, i);
                scratch.P[i] = b.Position;
                scratch.Q[i] = b.Rotation;
                scratch.V[i] = b.Velocity;
                scratch.W[i] = b.AngularVelocity;
            }
        }

        /// <summary>풀이 배열의 속도를 깨어 있는 몸에 되돌린다.</summary>
        static void StoreBodies(ref PropState props, PropScratch scratch)
        {
            for (int k = 0; k < scratch.AwakeCount; k++)
            {
                int i = scratch.AwakeList[k];
                ref var b = ref PropState.Get(ref props, i);
                b.Velocity = scratch.V[i];
                b.AngularVelocity = scratch.W[i];
            }
        }

        /// <summary>세게 부딪힌 접촉(다가온 속도 ImpactSpeed 이상, 실제로 막힘)을 몸마다 틱에 한 번 연출 이벤트로 알린다.</summary>
        static void ReportImpacts(in PropParams c, PropScratch scratch, int tick, SimEventBuffer events)
        {
            for (int i = 0; i < PropState.Capacity; i++)
                scratch.Rebuilt[i] = false;
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref readonly var contact = ref scratch.Contacts[k];
                if (-contact.Approach < c.ImpactSpeed || contact.MaxNormalImpulse <= 0f || scratch.Rebuilt[contact.Body])
                    continue;
                scratch.Rebuilt[contact.Body] = true;
                events.Add(new SimEvent(tick, SimEventType.PropImpact, SimEvent.NoFighter, contact.Point, contact.Normal, -contact.Approach));
            }
        }

        static void WarmStart(ref PropState props, PropScratch scratch)
        {
            LoadBodies(in props, scratch);
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref readonly var contact = ref scratch.Contacts[k];
                var n = contact.Normal;
                var t = new Vec2(-n.Y, n.X);
                var impulse = n * contact.NormalImpulse + t * contact.TangentImpulse;
                if (!contact.Twin || k + 1 >= scratch.ContactCount)
                {
                    Apply(scratch, contact, impulse);
                    continue;
                }
                // 두 점은 합을 먼저 구해 한 번에 더한다(더하는 순서가 거울상에서 뒤바뀌면 반올림이 달라진다).
                ref readonly var second = ref scratch.Contacts[++k];
                var impulse2 = n * second.NormalImpulse + t * second.TangentImpulse;
                int i = contact.Body, j = contact.Other;
                scratch.V[i] += (impulse + impulse2) * scratch.InvMass[i];
                scratch.W[i] += scratch.InvInertia[i] * (PropBody.Cross(contact.RBody, impulse) + PropBody.Cross(second.RBody, impulse2));
                if (j < 0)
                    continue;
                scratch.V[j] -= (impulse + impulse2) * scratch.InvMass[j];
                scratch.W[j] -= scratch.InvInertia[j] * (PropBody.Cross(contact.ROther, impulse) + PropBody.Cross(second.ROther, impulse2));
            }
            StoreBodies(ref props, scratch);
        }

        /// <summary>접촉 하나를 풀 때 두 몸의 속도(작업 복사본).</summary>
        struct PairVelocity
        {
            public float Vbx, Vby, Wb, Vox, Voy, Wo;
        }

        const int SolveBiased = 0, SolveRelax = 1, SolveRestitution = 2;

        /// <summary>
        /// 법선(부드러운 제약 또는 예측 접촉)과 마찰을 한 번 푼다. useBias가 false면 겹침을 밀어내지 않고 단단한 제약으로(이완 단계).
        /// 틈은 찾을 때의 틈 + 두 접점이 법선 방향으로 움직인 양.
        /// 한 면에서 나온 두 점(Twin)은 두 순서로 각각 풀어 평균한다: 차례로 푸는 순서가 좌우 거울상에서 뒤바뀌어,
        /// 가운데 대칭인 상자도 한쪽으로 기울며 무너졌다(S-143). 속도는 충격량에 선형이라 평균한 충격량과 평균한 속도가 맞는다.
        /// </summary>
        static void SolveContacts(ref PropState props, in PropParams c, PropScratch scratch, in Softness soft, float h, bool useBias) =>
            SolveAll(ref props, c, scratch, soft, 1f / h, useBias ? SolveBiased : SolveRelax);

        /// <summary>
        /// 반발: 실제로 막힌(충격량 &gt; 0) 접촉 중 기준보다 빠르게 다가오던 것만, 다가오던 속도 × 반발 계수로 되튕긴다
        /// (Box2D v3 b2ApplyRestitution과 같은 생각. CP15에서는 강체끼리만, CP16부터 지형·경계 접촉도).
        /// </summary>
        static void ApplyRestitution(ref PropState props, in PropParams c, PropScratch scratch) =>
            SolveAll(ref props, c, scratch, default, 0f, SolveRestitution);

        static void SolveAll(ref PropState props, in PropParams c, PropScratch scratch, in Softness soft, float invH, int mode)
        {
            LoadBodies(in props, scratch);
            var V = scratch.V;
            var W = scratch.W;
            // 가장 많이 도는 반복이라 Vec2 연산을 스칼라로 풀어 썼다.
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref var first = ref scratch.Contacts[k];
                int i = first.Body, j = first.Other;
                var v = new PairVelocity { Vbx = V[i].X, Vby = V[i].Y, Wb = W[i] };
                if (j >= 0)
                {
                    v.Vox = V[j].X;
                    v.Voy = V[j].Y;
                    v.Wo = W[j];
                }
                float sep1 = mode == SolveRestitution ? 0f : CurrentSeparation(scratch, first);
                if (first.Twin && k + 1 < scratch.ContactCount)
                {
                    ref var second = ref scratch.Contacts[k + 1];
                    float sep2 = mode == SolveRestitution ? 0f : CurrentSeparation(scratch, second);
                    var start = v;
                    float n1 = first.NormalImpulse, t1 = first.TangentImpulse, m1 = first.MaxNormalImpulse;
                    float n2 = second.NormalImpulse, t2 = second.TangentImpulse, m2 = second.MaxNormalImpulse;
                    SolvePoint(ref first, ref v, scratch, c, soft, invH, sep1, mode);
                    SolvePoint(ref second, ref v, scratch, c, soft, invH, sep2, mode);
                    var a = v;
                    float an1 = first.NormalImpulse, at1 = first.TangentImpulse, am1 = first.MaxNormalImpulse;
                    float an2 = second.NormalImpulse, at2 = second.TangentImpulse, am2 = second.MaxNormalImpulse;
                    v = start;
                    first.NormalImpulse = n1;
                    first.TangentImpulse = t1;
                    first.MaxNormalImpulse = m1;
                    second.NormalImpulse = n2;
                    second.TangentImpulse = t2;
                    second.MaxNormalImpulse = m2;
                    SolvePoint(ref second, ref v, scratch, c, soft, invH, sep2, mode);
                    SolvePoint(ref first, ref v, scratch, c, soft, invH, sep1, mode);
                    first.NormalImpulse = 0.5f * (an1 + first.NormalImpulse);
                    first.TangentImpulse = 0.5f * (at1 + first.TangentImpulse);
                    first.MaxNormalImpulse = SimMath.Max(am1, first.MaxNormalImpulse);
                    second.NormalImpulse = 0.5f * (an2 + second.NormalImpulse);
                    second.TangentImpulse = 0.5f * (at2 + second.TangentImpulse);
                    second.MaxNormalImpulse = SimMath.Max(am2, second.MaxNormalImpulse);
                    v.Vbx = 0.5f * (a.Vbx + v.Vbx);
                    v.Vby = 0.5f * (a.Vby + v.Vby);
                    v.Wb = 0.5f * (a.Wb + v.Wb);
                    v.Vox = 0.5f * (a.Vox + v.Vox);
                    v.Voy = 0.5f * (a.Voy + v.Voy);
                    v.Wo = 0.5f * (a.Wo + v.Wo);
                    k++;
                }
                else
                {
                    SolvePoint(ref first, ref v, scratch, c, soft, invH, sep1, mode);
                }
                V[i] = new Vec2(v.Vbx, v.Vby);
                W[i] = v.Wb;
                if (j >= 0)
                {
                    V[j] = new Vec2(v.Vox, v.Voy);
                    W[j] = v.Wo;
                }
            }
            StoreBodies(ref props, scratch);
        }

        /// <summary>찾을 때의 틈 + 두 접점이 법선 방향으로 움직인 양.</summary>
        static float CurrentSeparation(PropScratch scratch, in PropContact contact)
        {
            int i = contact.Body, j = contact.Other;
            var P = scratch.P;
            var Q = scratch.Q;
            float qx = Q[i].X, qy = Q[i].Y, lx = contact.LocalBody.X, ly = contact.LocalBody.Y;
            float pbx = P[i].X + qx * lx - qy * ly, pby = P[i].Y + qy * lx + qx * ly;
            float pox = contact.Point.X, poy = contact.Point.Y;
            if (j >= 0)
            {
                float ox = Q[j].X, oy = Q[j].Y, mx = contact.LocalOther.X, my = contact.LocalOther.Y;
                pox = P[j].X + ox * mx - oy * my;
                poy = P[j].Y + oy * mx + ox * my;
            }
            return (pbx - pox) * contact.Normal.X + (pby - poy) * contact.Normal.Y + contact.Separation;
        }

        static void SolvePoint(ref PropContact contact, ref PairVelocity v, PropScratch scratch, in PropParams c, in Softness soft, float invH, float separation, int mode)
        {
            int i = contact.Body, j = contact.Other;
            float im = scratch.InvMass[i], ii = scratch.InvInertia[i];
            float imo = j >= 0 ? scratch.InvMass[j] : 0f, iio = j >= 0 ? scratch.InvInertia[j] : 0f;
            float nx = contact.Normal.X, ny = contact.Normal.Y;
            float rbx = contact.RBody.X, rby = contact.RBody.Y;
            float rox = contact.ROther.X, roy = contact.ROther.Y;
            float dvx = v.Vbx - v.Wb * rby - (v.Vox - v.Wo * roy);
            float dvy = v.Vby + v.Wb * rbx - (v.Voy + v.Wo * rox);
            float vn = dvx * nx + dvy * ny;
            float impulse;
            if (mode == SolveRestitution)
            {
                if (contact.Restitution <= 0f || contact.Approach > -c.RestitutionThreshold || contact.MaxNormalImpulse <= 0f)
                    return;
                impulse = -contact.NormalMass * (vn + contact.Restitution * contact.Approach);
            }
            else
            {
                float bias = 0f, massScale = 1f, impulseScale = 0f;
                if (separation > 0f)
                {
                    bias = separation * invH;
                }
                else if (mode == SolveBiased)
                {
                    bias = SimMath.Max(soft.BiasRate * SimMath.Min(0f, separation + c.Slop), -c.ContactPushMaxSpeed);
                    massScale = soft.MassScale;
                    impulseScale = soft.ImpulseScale;
                }
                impulse = -contact.NormalMass * massScale * (vn + bias) - impulseScale * contact.NormalImpulse;
            }
            float total = SimMath.Max(contact.NormalImpulse + impulse, 0f);
            impulse = total - contact.NormalImpulse;
            contact.NormalImpulse = total;
            contact.MaxNormalImpulse = SimMath.Max(contact.MaxNormalImpulse, impulse);
            float px = nx * impulse, py = ny * impulse;
            v.Vbx += px * im;
            v.Vby += py * im;
            v.Wb += ii * (rbx * py - rby * px);
            v.Vox -= px * imo;
            v.Voy -= py * imo;
            v.Wo -= iio * (rox * py - roy * px);
            if (mode == SolveRestitution)
                return;

            // 마찰: 접선 t = (−ny, nx).
            float tx = -ny, ty = nx;
            dvx = v.Vbx - v.Wb * rby - (v.Vox - v.Wo * roy);
            dvy = v.Vby + v.Wb * rbx - (v.Voy + v.Wo * rox);
            float vt = dvx * tx + dvy * ty;
            float limit = contact.Friction * contact.NormalImpulse;
            float totalT = SimMath.Clamp(contact.TangentImpulse - contact.TangentMass * vt, -limit, limit);
            float impulseT = totalT - contact.TangentImpulse;
            contact.TangentImpulse = totalT;
            px = tx * impulseT;
            py = ty * impulseT;
            v.Vbx += px * im;
            v.Vby += py * im;
            v.Wb += ii * (rbx * py - rby * px);
            v.Vox -= px * imo;
            v.Voy -= py * imo;
            v.Wo -= iio * (rox * py - roy * px);
        }

        /// <summary>
        /// 이번 틱의 충격량을 다음 틱 웜 스타트용으로 몸 상태에 저장한다(접촉의 Body 쪽). 이번 틱에 접촉을 가진 몸과 깨어 있는 몸은 칸을 새로 채우고,
        /// 접촉 없이 잠든 몸은 지난 값을 그대로 둔다(깨어나면 이어 쓴다).
        /// </summary>
        static void StoreImpulses(ref PropState props, in PropParams c, PropScratch scratch)
        {
            for (int i = 0; i < props.Count; i++)
                scratch.Rebuilt[i] = false;
            for (int k = 0; k < scratch.ContactCount; k++)
            {
                ref readonly var contact = ref scratch.Contacts[k];
                ref var b = ref PropState.Get(ref props, contact.Body);
                if (!scratch.Rebuilt[contact.Body])
                {
                    b.Warm = default;
                    scratch.Rebuilt[contact.Body] = true;
                }
                if (contact.NormalImpulse != 0f || contact.TangentImpulse != 0f)
                    PropWarm.Add(ref b.Warm, contact.Id, contact.NormalImpulse, contact.TangentImpulse);
            }
            for (int i = 0; i < props.Count; i++)
            {
                ref var b = ref PropState.Get(ref props, i);
                if (!scratch.Rebuilt[i] && !Asleep(b, c))
                    b.Warm = default;
            }
        }

        /// <summary>찾을 때의 팔로 잰 접점 상대 속도(Body − 상대, 풀이 배열에서).</summary>
        static Vec2 RelativeVelocity(PropScratch scratch, in PropContact contact)
        {
            var v = scratch.V[contact.Body] + PropBody.Cross(scratch.W[contact.Body], contact.RBody);
            if (contact.Other >= 0)
                v -= scratch.V[contact.Other] + PropBody.Cross(scratch.W[contact.Other], contact.ROther);
            return v;
        }

        /// <summary>충격량을 Body에 +, 상대에 −로(잠든 몸과 지형은 역질량 0이라 움직이지 않는다).</summary>
        static void Apply(PropScratch scratch, in PropContact contact, Vec2 impulse)
        {
            int i = contact.Body;
            scratch.V[i] += impulse * scratch.InvMass[i];
            scratch.W[i] += scratch.InvInertia[i] * PropBody.Cross(contact.RBody, impulse);
            int j = contact.Other;
            if (j < 0)
                return;
            scratch.V[j] -= impulse * scratch.InvMass[j];
            scratch.W[j] -= scratch.InvInertia[j] * PropBody.Cross(contact.ROther, impulse);
        }
    }
}
