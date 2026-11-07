using System;

namespace Knockbox.Simulation
{
    /// <summary>증강의 적용 대상(D-055). Common은 모든 폼.</summary>
    public enum AugmentTarget : byte
    {
        Scissors,
        Rock,
        Paper,
        Common,
    }

    /// <summary>희귀도(D-055). 지금은 모두 일반이다. 희귀 증강의 내용·확률·중복 규칙은 뒤로 미룬다.</summary>
    public enum AugmentRarity : byte
    {
        Common,
        Rare,
    }

    /// <summary>① 가산·② 배율 단계가 바꾸는 수치.</summary>
    public enum AugmentStat : byte
    {
        None,
        LungeSpeed,
        LungeCost,
        ShotCost,
        SwingCost,
        ShotStartup,
    }

    /// <summary>③ 단계가 설정·해제하는 속성 플래그.</summary>
    public enum AugmentFlag : byte
    {
        None,
        ProjectileReflectable,
    }

    /// <summary>④ 단계의 동작 슬롯. 슬롯마다 하나만 활성이다.</summary>
    public enum AugmentSlot : byte
    {
        None,
        /// <summary>가위의 발사 방식(충전형 → 즉발).</summary>
        ScissorsLaunch,
    }

    /// <summary>④ 단계의 트리거가 반응하는 사건.</summary>
    public enum AugmentTrigger : byte
    {
        None,
        /// <summary>근접 판정이 상대를 일반 명중.</summary>
        OnMeleeHit,
        /// <summary>에너지 환급(다른 트리거가 일으킨다. 연쇄 깊이 상한 1이라 트리거 안에서는 반응하지 않는다).</summary>
        OnEnergyRefund,
    }

    /// <summary>
    /// 증강이 수정하거나 없애는 대상(무의미 조합 판정, 10 문서 5.3). 비트 묶음.
    /// </summary>
    [Flags]
    public enum AugmentAspect
    {
        None = 0,
        ChargePhase = 1 << 0,
        LungeSpeed = 1 << 1,
        LaunchMode = 1 << 2,
        Projectile = 1 << 3,
        MeleeHit = 1 << 4,
    }

    /// <summary>
    /// 증강 하나(수정자 묶음, 10-forms-and-growth.md 5.3). 수정자는 단계마다 하나씩:
    /// ① Stat에 Add, ② Stat에 Mul(중복이면 거듭제곱), ③ Flag를 FlagValue로, ④ Slot을 차지하거나 Trigger에 TriggerValue만큼 반응.
    /// 적용 순서는 ① → ② → ③ → ④, 같은 단계 안에서는 Id 순(획득 순서와 무관).
    /// </summary>
    [Serializable]
    public struct AugmentDef
    {
        public int Id;
        public string Name;
        public AugmentTarget Target;
        public AugmentRarity Rarity;
        public int Priority;
        /// <summary>같은 증강을 몇 번까지 가질 수 있는지(제한적 중복, D-056). 기본 1.</summary>
        public int MaxStacks;
        public AugmentStat AddStat;
        public float Add;
        public AugmentStat MulStat;
        public float Mul;
        public AugmentFlag Flag;
        public bool FlagValue;
        public AugmentSlot Slot;
        public AugmentTrigger Trigger;
        /// <summary>트리거 효과의 크기(에너지 환급량 등).</summary>
        public float TriggerValue;
        public AugmentAspect Targets;
        public AugmentAspect Removes;

        public readonly bool AppliesTo(FighterForm form) =>
            Target == AugmentTarget.Common || (int)Target == (int)form;

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Id), Id);
            v.Value(nameof(Target), (int)Target);
            v.Value(nameof(Rarity), (int)Rarity);
            v.Value(nameof(Priority), Priority);
            v.Value(nameof(MaxStacks), MaxStacks);
            v.Value(nameof(AddStat), (int)AddStat);
            v.Value(nameof(Add), Add);
            v.Value(nameof(MulStat), (int)MulStat);
            v.Value(nameof(Mul), Mul);
            v.Value(nameof(Flag), (int)Flag);
            v.Value(nameof(FlagValue), FlagValue);
            v.Value(nameof(Slot), (int)Slot);
            v.Value(nameof(Trigger), (int)Trigger);
            v.Value(nameof(TriggerValue), TriggerValue);
            v.Value(nameof(Targets), (int)Targets);
            v.Value(nameof(Removes), (int)Removes);
        }
    }

    /// <summary>M2의 대표 증강 4개(10 문서 5.4). 이름과 수치는 임시. 개수는 늘리지 않는다.</summary>
    public static class AugmentCatalog
    {
        public const int LungeSpeed = 0;
        public const int InstantLunge = 1;
        public const int ArmorPiercing = 2;
        public const int EnergyRefund = 3;

        public static AugmentDef[] CreateDefault() => new[]
        {
            new AugmentDef
            {
                Id = LungeSpeed, Name = "SPEED", Target = AugmentTarget.Scissors, MaxStacks = 3,
                MulStat = AugmentStat.LungeSpeed, Mul = 1.2f, Targets = AugmentAspect.LungeSpeed,
            },
            new AugmentDef
            {
                Id = InstantLunge, Name = "INSTANT", Target = AugmentTarget.Scissors, MaxStacks = 1,
                Slot = AugmentSlot.ScissorsLaunch, Targets = AugmentAspect.LaunchMode, Removes = AugmentAspect.ChargePhase,
            },
            new AugmentDef
            {
                Id = ArmorPiercing, Name = "PIERCE", Target = AugmentTarget.Rock, MaxStacks = 1,
                Flag = AugmentFlag.ProjectileReflectable, FlagValue = false, Targets = AugmentAspect.Projectile,
            },
            new AugmentDef
            {
                Id = EnergyRefund, Name = "REFUND", Target = AugmentTarget.Paper, MaxStacks = 1,
                Trigger = AugmentTrigger.OnMeleeHit, TriggerValue = 20f, Targets = AugmentAspect.MeleeHit,
            },
        };
    }
}
