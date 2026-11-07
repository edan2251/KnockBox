using System;

namespace Knockbox.Simulation
{
    /// <summary>구조물 물리와 캐릭터 상호작용 수치(09-props-physics.md 4·8절). 초기값이며 튜닝은 자체 결정.</summary>
    [Serializable]
    public struct PropParams
    {
        // 물리
        public float Gravity;
        public float LinearDamping;
        public float AngularDamping;
        /// <summary>면적당 질량(1 × 1 블록 = 1.0).</summary>
        public float Density;
        public float Restitution;
        public float Friction;
        /// <summary>화면 경계에서 피해 없이 튕기는 반발 계수(CR-002 3절).</summary>
        public float BoundaryRestitution;
        public int Substeps;
        public int Iterations;
        /// <summary>관통·줄 늘어남 보정(서브스텝마다 오차의 이 비율을 속도로 되돌린다).</summary>
        public float Correction;
        public float Slop;
        /// <summary>부드러운 접촉(CP16, Box2D v3의 soft step): 강성(Hz, 서브스텝 빈도의 1/4을 넘지 않게)과 감쇠비, 겹침을 밀어내는 최대 속도.</summary>
        public float ContactHertz;
        /// <summary>서브스텝마다 접촉을 푸는 횟수(연결 반복 Iterations의 마지막 몇 번에만, 그 뒤 이완 한 번). Box2D v3와 같은 1(두 점 접촉을 두 순서로 풀어 평균한 뒤로는 1회에서도 6단 기둥 흔들림 0, S-143).</summary>
        public int ContactIterations;
        public float ContactDampingRatio;
        public float ContactPushMaxSpeed;
        /// <summary>예측 접촉 거리(틱마다 한 번 찾을 때 속도 × 틱에 더하는 여유).</summary>
        public float SpeculativeDistance;
        /// <summary>이보다 빠르게 다가오던 접촉만 반발한다.</summary>
        public float RestitutionThreshold;
        /// <summary>상자(기둥·흩어진 상자)가 낀 접촉의 반발과 마찰(09 4절 "쌓인 기둥의 느낌": 0.05~0.1, 0.6~0.8).</summary>
        public float BoxRestitution;
        public float BoxFriction;
        /// <summary>
        /// 부분 붕괴(CP16, 09 4절): 이 위력 이상의 돌진에 맞은 상자는 LooseTicks 동안 다른 상자와의 마찰이 0이 되고 친 방향으로 적어도 KnockoutSpeed로 밀려나
        /// 기둥에서 빠진다(위 상자는 받침이 빠져 떨어지고 아래 상자와 받침은 남는다). 약한 돌진은 마찰에 막혀 거의 밀리지 않는다.
        /// </summary>
        public float KnockoutPower;
        public float KnockoutSpeed;
        public int LooseTicks;
        /// <summary>상자 피해 상한: 같은 캐릭터가 상자에게 받는 피해는 BoxDamageWindowTicks 동안 합쳐 BoxDamageCap까지(그 뒤 상자는 밀기만 한다).</summary>
        public int BoxDamageCap;
        public int BoxDamageWindowTicks;
        /// <summary>구조물이 이 속도 이상으로 지형·다른 구조물에 부딪히면 연출 이벤트(먼지와 둔탁한 소리, 09 6절).</summary>
        public float ImpactSpeed;
        /// <summary>잠들기: 속도·각속도가 이보다 작은 틱이 SleepTicks번 이어지면 잠든다(처음엔 잠든 채로 놓인다).</summary>
        public float SleepSpeed;
        public float SleepAngularSpeed;
        public int SleepTicks;
        /// <summary>경첩 발판·시소: 각도 한계(사인값, 처음 자세 기준)와 복원 스프링(토크 = 강성 × sin θ), 각 감쇠.</summary>
        public float HingeLimitSin;
        public float HingeStiffness;
        public float SeesawLimitSin;
        public float SeesawStiffness;
        public float PivotDamping;

        // 캐릭터 상호작용
        public float FighterMass;
        public float FighterRestitution;
        /// <summary>구조물 위에 섰다고 보는 접촉 법선의 위쪽 각도 한계의 코사인(50°).</summary>
        public float StandCos;
        /// <summary>돌진이 구조물을 칠 때 위력에 곱해 충격량으로 주는 배율.</summary>
        public float LungeImpulseScale;
        /// <summary>돌진이 이 질량 이상의 구조물을 치면 지형처럼 반사(× LungeHeavyRestitution), 아래면 계속 나아간다.</summary>
        public float LungeHeavyMass;
        public float LungeHeavyRestitution;
        /// <summary>같은 캐릭터의 돌진이 같은 구조물을 다시 칠 수 있을 때까지의 틱(그동안은 일반 운동량 교환).</summary>
        public int StrikeCooldownTicks;
        /// <summary>구조물이 캐릭터를 침: 접촉점 상대 법선 속도 하한, 피해 clamp(round(속도 × 질량 × 배율), 최소, 최대).</summary>
        public float HitMinSpeed;
        public float HitDamageScale;
        public int HitMinDamage;
        public int HitMaxDamage;
        public float HitKnockbackMin;
        public float HitKnockbackScale;
        public int HitCooldownTicks;
        /// <summary>날아가는 캐릭터가 이 질량 이상의 구조물에 부딪히면 벽꽝 대상.</summary>
        public float SplatMinMass;
        public float ParryRestitution;

        public static PropParams Default => new PropParams
        {
            Gravity = 50f,
            LinearDamping = 0.15f,
            AngularDamping = 0.4f,
            Density = 1f,
            Restitution = 0.2f,
            Friction = 0.5f,
            BoundaryRestitution = 0.5f,
            Substeps = 4,
            Iterations = 4,
            Correction = 0.2f,
            Slop = 0.005f,
            ContactHertz = 30f,
            ContactIterations = 1,
            ContactDampingRatio = 10f,
            ContactPushMaxSpeed = 3f,
            SpeculativeDistance = 0.02f,
            RestitutionThreshold = 1f,
            BoxRestitution = 0.08f,
            BoxFriction = 0.7f,
            KnockoutPower = 24f,
            KnockoutSpeed = 12f,
            LooseTicks = 20,
            BoxDamageCap = 18,
            BoxDamageWindowTicks = 60,
            ImpactSpeed = 5f,
            SleepSpeed = 0.05f,
            SleepAngularSpeed = 0.05f,
            SleepTicks = 30,
            HingeLimitSin = 0.5f,
            HingeStiffness = 400f,
            SeesawLimitSin = 0.342f,
            SeesawStiffness = 150f,
            PivotDamping = 3f,

            FighterMass = 1f,
            FighterRestitution = 0.15f,
            StandCos = 0.6427876f,
            LungeImpulseScale = 0.5f,
            LungeHeavyMass = 4f,
            LungeHeavyRestitution = 0.6f,
            StrikeCooldownTicks = 12,
            HitMinSpeed = 6f,
            HitDamageScale = 0.15f,
            HitMinDamage = 3,
            HitMaxDamage = 18,
            HitKnockbackMin = 10f,
            HitKnockbackScale = 0.8f,
            HitCooldownTicks = 20,
            SplatMinMass = 4f,
            ParryRestitution = 0.8f,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Gravity), Gravity);
            v.Value(nameof(LinearDamping), LinearDamping);
            v.Value(nameof(AngularDamping), AngularDamping);
            v.Value(nameof(Density), Density);
            v.Value(nameof(Restitution), Restitution);
            v.Value(nameof(Friction), Friction);
            v.Value(nameof(BoundaryRestitution), BoundaryRestitution);
            v.Value(nameof(Substeps), Substeps);
            v.Value(nameof(Iterations), Iterations);
            v.Value(nameof(Correction), Correction);
            v.Value(nameof(Slop), Slop);
            v.Value(nameof(ContactHertz), ContactHertz);
            v.Value(nameof(ContactIterations), ContactIterations);
            v.Value(nameof(ContactDampingRatio), ContactDampingRatio);
            v.Value(nameof(ContactPushMaxSpeed), ContactPushMaxSpeed);
            v.Value(nameof(SpeculativeDistance), SpeculativeDistance);
            v.Value(nameof(RestitutionThreshold), RestitutionThreshold);
            v.Value(nameof(BoxRestitution), BoxRestitution);
            v.Value(nameof(BoxFriction), BoxFriction);
            v.Value(nameof(KnockoutPower), KnockoutPower);
            v.Value(nameof(KnockoutSpeed), KnockoutSpeed);
            v.Value(nameof(LooseTicks), LooseTicks);
            v.Value(nameof(BoxDamageCap), BoxDamageCap);
            v.Value(nameof(BoxDamageWindowTicks), BoxDamageWindowTicks);
            v.Value(nameof(ImpactSpeed), ImpactSpeed);
            v.Value(nameof(SleepSpeed), SleepSpeed);
            v.Value(nameof(SleepAngularSpeed), SleepAngularSpeed);
            v.Value(nameof(SleepTicks), SleepTicks);
            v.Value(nameof(HingeLimitSin), HingeLimitSin);
            v.Value(nameof(HingeStiffness), HingeStiffness);
            v.Value(nameof(SeesawLimitSin), SeesawLimitSin);
            v.Value(nameof(SeesawStiffness), SeesawStiffness);
            v.Value(nameof(PivotDamping), PivotDamping);
            v.Value(nameof(FighterMass), FighterMass);
            v.Value(nameof(FighterRestitution), FighterRestitution);
            v.Value(nameof(StandCos), StandCos);
            v.Value(nameof(LungeImpulseScale), LungeImpulseScale);
            v.Value(nameof(LungeHeavyMass), LungeHeavyMass);
            v.Value(nameof(LungeHeavyRestitution), LungeHeavyRestitution);
            v.Value(nameof(StrikeCooldownTicks), StrikeCooldownTicks);
            v.Value(nameof(HitMinSpeed), HitMinSpeed);
            v.Value(nameof(HitDamageScale), HitDamageScale);
            v.Value(nameof(HitMinDamage), HitMinDamage);
            v.Value(nameof(HitMaxDamage), HitMaxDamage);
            v.Value(nameof(HitKnockbackMin), HitKnockbackMin);
            v.Value(nameof(HitKnockbackScale), HitKnockbackScale);
            v.Value(nameof(HitCooldownTicks), HitCooldownTicks);
            v.Value(nameof(SplatMinMass), SplatMinMass);
            v.Value(nameof(ParryRestitution), ParryRestitution);
        }
    }
}
