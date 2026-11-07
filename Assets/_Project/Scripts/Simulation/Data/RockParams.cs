using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 바위 폼의 기본공격: 투사체(10-forms-and-growth.md 4.3, CP19). 피해·넉백·히트스탑은 공통 틀(03 3절)을 쓴다.
    /// 발사 방향은 발사 틱의 8방향 입력(중립이면 바라보는 쪽).
    /// </summary>
    [Serializable]
    public struct RockParams
    {
        /// <summary>선딜: 이 틱에 발사한다. 이 동안 끊기면 에너지를 쓰지 않는다.</summary>
        public int StartupTicks;
        public int RecoveryTicks;
        /// <summary>유닛/초.</summary>
        public float Speed;
        public float Radius;
        /// <summary>몸 중심에서 발사 방향으로 이만큼 떨어진 곳에 생긴다.</summary>
        public float SpawnOffset;
        public int LifetimeTicks;
        public int Damage;
        public float Knockback;
        /// <summary>넉백 방향 = normalize(진행 방향 + 위로 이만큼). 맞은 쪽이 살짝 뜨게.</summary>
        public float KnockbackLift;
        public bool Reflectable;
        public bool PierceFighter;
        public bool PierceProp;
        /// <summary>한 투사체의 연속 반사 상한(4.2, 핑퐁 방지).</summary>
        public int MaxReflections;
        /// <summary>돌진 중인 파이터를 맞히면 돌진이 끊기고 이만큼 기절(비틀거림)한다(4.1).</summary>
        public int LungeStunTicks;
        /// <summary>기절할 때 남기는 속도 비율(돌진의 관성을 끊는다).</summary>
        public float LungeStunVelocityScale;

        public static RockParams Default => new RockParams
        {
            StartupTicks = 10,
            RecoveryTicks = 16,
            Speed = 26f,
            Radius = 0.22f,
            SpawnOffset = 0.55f,
            LifetimeTicks = 120,
            Damage = 8,
            Knockback = 13f,
            KnockbackLift = 0.35f,
            Reflectable = true,
            PierceFighter = false,
            PierceProp = false,
            MaxReflections = 3,
            LungeStunTicks = 24,
            LungeStunVelocityScale = 0.25f,
        };

        public readonly int TotalTicks => StartupTicks + RecoveryTicks;

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(StartupTicks), StartupTicks);
            v.Value(nameof(RecoveryTicks), RecoveryTicks);
            v.Value(nameof(Speed), Speed);
            v.Value(nameof(Radius), Radius);
            v.Value(nameof(SpawnOffset), SpawnOffset);
            v.Value(nameof(LifetimeTicks), LifetimeTicks);
            v.Value(nameof(Damage), Damage);
            v.Value(nameof(Knockback), Knockback);
            v.Value(nameof(KnockbackLift), KnockbackLift);
            v.Value(nameof(Reflectable), Reflectable);
            v.Value(nameof(PierceFighter), PierceFighter);
            v.Value(nameof(PierceProp), PierceProp);
            v.Value(nameof(MaxReflections), MaxReflections);
            v.Value(nameof(LungeStunTicks), LungeStunTicks);
            v.Value(nameof(LungeStunVelocityScale), LungeStunVelocityScale);
        }
    }
}
