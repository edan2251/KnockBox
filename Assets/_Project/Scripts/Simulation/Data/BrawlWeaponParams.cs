using System;

namespace Knockbox.Simulation
{
    /// <summary>라운드당 한 번 떨어지는 Brawl 임시 무기. 기본값 0은 기존 기록용 비활성 설정이다.</summary>
    [Serializable]
    public struct BrawlWeaponParams
    {
        public bool Enabled;
        public int SpawnDelayTicks;
        public int WarningTicks;
        public int FallTicks;
        public int DropTicks;
        public float DropHeight;
        public float PickupRadius;
        public float GroundHeight;
        public int PistolAmmo;
        public float PistolRange;
        public int PistolDamage;
        public float PistolKnockback;
        public AttackDef Pan;
        public AttackDef Pistol;
        public bool ThrowEnabled;
        public float ThrowSpeed;
        public float ThrowGravity;
        public float ThrowRestitution;
        public int ThrowDamage;
        public float ThrowKnockback;
        public float ThrowRadius;

        public static BrawlWeaponParams Default => new BrawlWeaponParams
        {
            Enabled = true,
            SpawnDelayTicks = 300,
            WarningTicks = 60,
            FallTicks = 36,
            DropTicks = 12,
            DropHeight = 3f,
            PickupRadius = 1.35f,
            GroundHeight = 0.15f,
            PistolAmmo = 6,
            PistolRange = 20f,
            PistolDamage = 12,
            PistolKnockback = 10f,
            Pan = new AttackDef
            {
                StartupTicks = 5,
                ActiveTicks = 3,
                RecoveryTicks = 16,
                Damage = 16,
                KnockbackStrength = 20f,
                KnockbackDirection = new Vec2(0.9396926f, 0.34202015f),
                CircleCount = 1,
                Circle0 = new HitCircle(new Vec2(0.8f, 0f), new Vec2(1f, 0f), 0.35f),
            },
            Pistol = new AttackDef
            {
                StartupTicks = 0,
                ActiveTicks = 1,
                RecoveryTicks = 23,
                KnockbackDirection = new Vec2(1f, 0f),
                CircleCount = 0,
            },
        };

        /// <summary>v7 기본값은 보존하고 새 경기에서만 투척 규칙을 더한다.</summary>
        public static BrawlWeaponParams WithThrowing(BrawlWeaponParams value)
        {
            value.ThrowEnabled = true;
            value.ThrowSpeed = 16f;
            value.ThrowGravity = 30f;
            value.ThrowRestitution = 0.3f;
            value.ThrowDamage = 10;
            value.ThrowKnockback = 8f;
            value.ThrowRadius = 0.15f;
            return value;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Enabled), Enabled);
            v.Value(nameof(SpawnDelayTicks), SpawnDelayTicks);
            v.Value(nameof(WarningTicks), WarningTicks);
            v.Value(nameof(FallTicks), FallTicks);
            v.Value(nameof(DropTicks), DropTicks);
            v.Value(nameof(DropHeight), DropHeight);
            v.Value(nameof(PickupRadius), PickupRadius);
            v.Value(nameof(GroundHeight), GroundHeight);
            v.Value(nameof(PistolAmmo), PistolAmmo);
            v.Value(nameof(PistolRange), PistolRange);
            v.Value(nameof(PistolDamage), PistolDamage);
            v.Value(nameof(PistolKnockback), PistolKnockback);
            v.Begin(nameof(Pan));
            Pan.Visit(ref v);
            v.End();
            v.Begin(nameof(Pistol));
            Pistol.Visit(ref v);
            v.End();
            if (ThrowEnabled)
            {
                v.Value(nameof(ThrowEnabled), true);
                v.Value(nameof(ThrowSpeed), ThrowSpeed);
                v.Value(nameof(ThrowGravity), ThrowGravity);
                v.Value(nameof(ThrowRestitution), ThrowRestitution);
                v.Value(nameof(ThrowDamage), ThrowDamage);
                v.Value(nameof(ThrowKnockback), ThrowKnockback);
                v.Value(nameof(ThrowRadius), ThrowRadius);
            }
        }
    }
}
