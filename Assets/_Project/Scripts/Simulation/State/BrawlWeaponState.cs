using System;

namespace Knockbox.Simulation
{
    public enum BrawlWeaponKind : byte
    {
        None,
        Pan,
        Pistol,
    }

    public enum BrawlWeaponPhase : byte
    {
        Waiting,
        Warning,
        Falling,
        Ground,
        Held,
        Finished,
        Thrown,
    }

    /// <summary>무기 하나의 전체 판정 상태. 화면의 낙하 위치도 이 상태에서 읽는다.</summary>
    [Serializable]
    public struct BrawlWeaponState
    {
        public bool Enabled;
        public BrawlWeaponKind Kind;
        public BrawlWeaponPhase Phase;
        /// <summary>들고 있는 선수. 활성 무기의 비소유 상태는 -1.</summary>
        public int Owner;
        /// <summary>이번 라운드의 실제 전투 틱. 카운트다운과 라운드 종료 시간은 세지 않는다.</summary>
        public int AgeTicks;
        public int PhaseTick;
        public Vec2 Position;
        public Vec2 LandingPosition;
        public Vec2 FallStart;
        /// <summary>첫 낙하와 내려놓기의 서로 다른 길이도 스냅숏에 보존한다.</summary>
        public int FallDurationTicks;
        public int Ammo;
        /// <summary>v8 투척 상태 확장. 꺼진 v7 상태는 기존 체크섬을 유지한다.</summary>
        public bool ThrowEnabled;
        public Vec2 Velocity;
        public int Thrower;
        /// <summary>이번 투척에서 이미 상대를 한 번 맞혔다. 다시 주워 던질 때 초기화한다.</summary>
        public bool ThrowHit;

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Enabled), Enabled);
            v.Value(nameof(Kind), (int)Kind);
            v.Value(nameof(Phase), (int)Phase);
            v.Value(nameof(Owner), Owner);
            v.Value(nameof(AgeTicks), AgeTicks);
            v.Value(nameof(PhaseTick), PhaseTick);
            v.Value("Position.X", Position.X);
            v.Value("Position.Y", Position.Y);
            v.Value("LandingPosition.X", LandingPosition.X);
            v.Value("LandingPosition.Y", LandingPosition.Y);
            v.Value("FallStart.X", FallStart.X);
            v.Value("FallStart.Y", FallStart.Y);
            v.Value(nameof(FallDurationTicks), FallDurationTicks);
            v.Value(nameof(Ammo), Ammo);
            if (ThrowEnabled)
            {
                v.Value(nameof(ThrowEnabled), true);
                v.Value("Velocity.X", Velocity.X);
                v.Value("Velocity.Y", Velocity.Y);
                v.Value(nameof(Thrower), Thrower);
                v.Value(nameof(ThrowHit), ThrowHit);
            }
        }
    }
}
