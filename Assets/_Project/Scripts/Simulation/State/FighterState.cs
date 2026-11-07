using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 파이터의 행동 상태. Idle~Dash는 이동 규칙이 정하는 상태(조작 가능), 나머지는 전투 상태다.
    /// 경직 계열(Hitstun, GuardStun, Stagger, Recoil)은 StunTicks가 끝날 때까지 조작할 수 없다.
    /// </summary>
    public enum ActionState : byte
    {
        Idle,
        Run,
        Airborne,
        WallSlide,
        /// <summary>벽 기어오르기(CR-004: 벽 쪽 + W). 이동 상태다.</summary>
        Climb,
        /// <summary>모서리를 잡고 끌어올라 윗면에 서는 중(CR-004 "영차"). 위치는 정해진 경로를 따른다.</summary>
        Mantle,
        Dash,
        Attack,
        /// <summary>가드 버튼을 누르고 있음. 누른 순간부터 ParryTicks 동안은 패링 구간.</summary>
        Guard,
        Hitstun,
        GuardStun,
        /// <summary>패링당해 비틀거림.</summary>
        Stagger,
        /// <summary>클래시 반동.</summary>
        Recoil,
        KO,
        /// <summary>모드 A: 공격 버튼을 누르고 있는 동안 충전. 행동 틱이 충전 틱이다.</summary>
        Charge,
        /// <summary>모드 A: 충전을 풀어 날아가는 돌진.</summary>
        Lunge,
        /// <summary>모드 B: 공중에서 시작한 공격 도중 착지한 경직.</summary>
        LandingLag,
        /// <summary>잡기: 시작 → 활성(잡기 상자) → 헛잡기 후딜. 행동 틱으로 단계를 나눈다.</summary>
        Grab,
        /// <summary>상대를 붙잡고 있음. 잡기 버튼을 떼거나 최대 시간이 지나면 던진다.</summary>
        Holding,
        /// <summary>붙잡혀 있음(움직일 수 없음).</summary>
        Held,
        /// <summary>던져진 상태. 피격 경직과 같은 규칙이고 벽꽝 기준이 낮다.</summary>
        Thrown,
        /// <summary>바위 폼(CP19): 선딜 → 발사 틱 → 후딜. 선딜 동안 끊기면 발사하지 않는다.</summary>
        Shoot,
    }

    /// <summary>
    /// 파이터 한 명의 상태. 배열이나 참조 필드 없이 값만 둔다.
    /// 필드를 추가하면 반드시 Visit에도 추가한다(체크섬 범위 테스트가 잡아낸다).
    /// </summary>
    [Serializable]
    public struct FighterState
    {
        public const int NoBox = -1;
        public const int OnFighter = -2;
        /// <summary>GroundBox: 구조물 강체 위(어느 구조물인지는 OnProp).</summary>
        public const int OnPropBox = -3;
        public const int TimerCap = 1000;

        public Vec2 Position;
        /// <summary>유닛/초.</summary>
        public Vec2 Velocity;
        /// <summary>-1 왼쪽, 1 오른쪽.</summary>
        public sbyte Facing;
        public ActionState Action;
        /// <summary>현재 행동에 들어온 뒤 지난 틱 수.</summary>
        public int ActionTick;
        /// <summary>직전 틱의 버튼. 눌림/뗌 계산용.</summary>
        public InputButtons PrevButtons;

        public bool Grounded;
        /// <summary>딛고 선 지형 상자 번호. NoBox, 또는 상대 머리 위면 OnFighter.</summary>
        public int GroundBox;
        /// <summary>지금 닿은 벽 쪽. -1 왼쪽 벽, 1 오른쪽 벽, 0 없음.</summary>
        public sbyte WallSide;
        /// <summary>마지막으로 닿았던 벽 쪽(벽 코요테용).</summary>
        public sbyte LastWallSide;
        public int TicksSinceGrounded;
        public int TicksSinceWall;

        /// <summary>남은 점프 버퍼 틱. 0이면 없음.</summary>
        public int JumpBuffer;
        /// <summary>점프로 올라가는 중이라 버튼을 떼면 조기 해제 중력을 받는다.</summary>
        public bool JumpCutEligible;
        public int AirJumpsLeft;

        public int DashCharges;
        public int DashCooldown;
        public int DashTicksLeft;
        public Vec2 DashDirection;
        public bool DashFromGround;

        public int WallJumpLockTicks;
        /// <summary>벽 점프 직후 무시하는 입력 방향(떠난 벽 쪽).</summary>
        public sbyte WallJumpLockDirection;

        public int DropThroughTicks;
        public int DropThroughBox;

        public int Health;
        /// <summary>남은 히트스탑 틱. 0보다 크면 이 파이터는 위치, 속도, 행동 타이머가 멈춘다.</summary>
        public int HitstopTicks;
        /// <summary>경직 계열 상태의 남은 틱.</summary>
        public int StunTicks;
        /// <summary>지금 이어지는 연속 명중 횟수. 피격 경직이 풀리면 0.</summary>
        public int ComboHits;
        /// <summary>진행 중인 공격 번호(SimConfig.GetAttack). 공격 중이 아니면 NoAttack.</summary>
        public int AttackId;
        /// <summary>이번 공격이 이미 상대에게 닿았다(한 번 휘두를 때 같은 상대는 한 번만).</summary>
        public bool AttackHasHit;
        /// <summary>이번 공격을 공중에서 시작했다(착지 경직 판정).</summary>
        public bool AttackFromAir;
        /// <summary>남은 패링 구간 틱.</summary>
        public int ParryTicks;
        /// <summary>마지막으로 가드 버튼을 누른 뒤 지난 틱(패링 재사용 제한).</summary>
        public int TicksSinceGuardPress;
        /// <summary>히트스탑 동안 새로 누른 버튼. 히트스탑이 끝난 첫 틱에 눌림으로 처리한다.</summary>
        public InputButtons PendingPresses;
        /// <summary>이번 돌진이 지형에 튕긴 횟수.</summary>
        public int LungeBounces;
        /// <summary>남은 몸 충돌 판정 대기 틱(같은 두 파이터 사이 판정은 10틱에 한 번). 두 파이터가 같은 값을 갖는다.</summary>
        public int ContactCooldown;
        /// <summary>붙잡은 쪽이 이번 틱에 던지기로 했다. 상호작용 판정에서 상대에게 적용한다.</summary>
        public bool ThrowPending;
        /// <summary>던질 방향(월드, 단위 벡터).</summary>
        public Vec2 ThrowDirection;
        /// <summary>남은 가시 면역 틱. 면역 동안 가시는 바닥처럼 선다.</summary>
        public int SpikeImmunityTicks;
        /// <summary>남은 경계 튕김 면역 틱(CR-002). 면역 동안 경계는 피해 없이 반사 × 0.5.</summary>
        public int BoundaryImmunityTicks;
        /// <summary>서 있는 구조물 강체: 0 없음, 1 + 몸 번호(CP14). 기본값 0이 "없음"이 되도록 1을 더한다.</summary>
        public int OnProp;
        /// <summary>상자 피해 상한(CP16): 첫 상자 피해 뒤 남은 창 틱과 그동안 받은 상자 피해 합.</summary>
        public int BoxDamageTicks;
        public int BoxDamageSum;

        /// <summary>현재 폼(CR-003). 공격 버튼이 무엇을 하는지 정한다.</summary>
        public FighterForm Form;
        /// <summary>보유 폼 비트(Forms.Bit). 전환은 보유한 폼 사이에서만(CP21).</summary>
        public byte OwnedForms;
        /// <summary>공격 에너지(10 문서 4.4). 스위치가 꺼져 있으면 쓰지 않는다.</summary>
        public int Energy;
        /// <summary>남은 회복 지연 틱. 공격 행동 동안 RegenDelayTicks로 유지되고, 행동이 끝난 뒤부터 줄어든다.</summary>
        public int EnergyRegenDelay;
        /// <summary>지금 행동이 이미 에너지를 소비했다(행동당 정확히 한 번, 4.4 규칙 1). 새 행동을 시작할 때 지운다.</summary>
        public bool ActionPaid;
        /// <summary>이번 틱에 바위 투사체를 발사하기로 했다(CP19). 이동이 끝난 뒤 투사체 풀에 넣고 지운다.</summary>
        public bool ShotPending;
        /// <summary>증강 획득 목록(CP23). 경기 끝까지 유지.</summary>
        public AugmentState Augments;
        public Vec2 ShotDirection;
        /// <summary>
        /// 조준 공격의 방향(CR-004 몸 폼의 주먹, 단위 벡터). 0이면 바라보는 쪽 기준(이전 공격). 판정 원과 넉백을 이 방향 기준으로 놓는다.
        /// </summary>
        public Vec2 AttackDir;
        /// <summary>주먹 후딜 중 누른 좌클릭을 저장해 둔 남은 틱(CR-004 5.1). 0이면 없음.</summary>
        public int AttackBuffer;
        /// <summary>남은 재그랩 보호 틱(CR-004 5.2). 0보다 크면 잡히지 않는다.</summary>
        public int GrabProtectTicks;
        /// <summary>이번에 날아가는 동안 받은 벽꽝 추가 피해 합(상한 검사용). 회복하면 0.</summary>
        public int SplatDamageTaken;
        /// <summary>붙은 표면(CR-004 7절): SurfaceNone, SurfaceWallRight(오른쪽에 벽), SurfaceWallLeft, SurfaceCeiling.</summary>
        public sbyte Surface;
        /// <summary>붙은 표면이 외곽 프레임인지(달리기는 외곽에서만).</summary>
        public bool SurfaceOuter;
        /// <summary>표면 달리기 방향 기억: +1 반시계(바닥 → 오른벽 위 → 천장 왼쪽 → 왼벽 아래), -1 시계, 0 없음.</summary>
        public sbyte SurfaceCirc;
        /// <summary>직전 틱의 좌우 키 집합(1 A, 2 D, 3 둘 다). 집합이 바뀔 때만 방향을 다시 정한다.</summary>
        public byte SurfaceKeys;
        /// <summary>중력 방향(CR-004 E, SpiderMovement): 0 아래, 1 오른쪽, 2 위, 3 왼쪽.</summary>
        public sbyte GravityDir;
        /// <summary>지금 중력을 준 떠 있는 지형 조각 + 1(0 없음). 같은 조각 안에서 중력이 바뀌면 모서리를 감아 돈다.</summary>
        public int GravityPiece;
        /// <summary>남은 중력장 무시 틱(S로 놓고 떨어진 직후).</summary>
        public int GravityLock;
        /// <summary>모서리 오르기 남은 틱(0이면 아님)과 출발·도착 위치(먼저 위로, 그다음 윗면 쪽으로).</summary>
        public int MantleTicks;
        public Vec2 MantleFrom;
        public Vec2 MantleTo;

        public const int NoAttack = -1;
        public const sbyte SurfaceNone = 0;
        public const sbyte SurfaceWallRight = 1;
        public const sbyte SurfaceWallLeft = 2;
        public const sbyte SurfaceCeiling = 3;
        /// <summary>방향을 정할 때만 쓰는 외곽 바닥(붙기 상태가 아니라 서 있음).</summary>
        public const sbyte SurfaceFloor = 4;

        /// <summary>이동 규칙이 행동을 정하는 상태(조작 가능).</summary>
        public readonly bool InMovementState => Action <= ActionState.Dash;

        public readonly bool IsStunned =>
            Action == ActionState.Hitstun || Action == ActionState.GuardStun || Action == ActionState.Stagger
            || Action == ActionState.Recoil || Action == ActionState.LandingLag || Action == ActionState.Thrown;

        /// <summary>날아가는 경직(지형에 부딪히면 벽꽝·튕김).</summary>
        public readonly bool IsLaunched => Action == ActionState.Hitstun || Action == ActionState.Thrown;

        public readonly bool IsGuarding => Action == ActionState.Guard || Action == ActionState.GuardStun;

        public void SetAction(ActionState action)
        {
            if (Action == action)
                return;
            Action = action;
            ActionTick = 0;
        }

        /// <summary>같은 상태라도 처음부터 다시 시작한다(연속 피격 등).</summary>
        public void RestartAction(ActionState action)
        {
            Action = action;
            ActionTick = 0;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value("Position.X", Position.X);
            v.Value("Position.Y", Position.Y);
            v.Value("Velocity.X", Velocity.X);
            v.Value("Velocity.Y", Velocity.Y);
            v.Value(nameof(Facing), (int)Facing);
            v.Value(nameof(Action), (int)Action);
            v.Value(nameof(ActionTick), ActionTick);
            v.Value(nameof(PrevButtons), (int)PrevButtons);
            v.Value(nameof(Grounded), Grounded);
            v.Value(nameof(GroundBox), GroundBox);
            v.Value(nameof(WallSide), (int)WallSide);
            v.Value(nameof(LastWallSide), (int)LastWallSide);
            v.Value(nameof(TicksSinceGrounded), TicksSinceGrounded);
            v.Value(nameof(TicksSinceWall), TicksSinceWall);
            v.Value(nameof(JumpBuffer), JumpBuffer);
            v.Value(nameof(JumpCutEligible), JumpCutEligible);
            v.Value(nameof(AirJumpsLeft), AirJumpsLeft);
            v.Value(nameof(DashCharges), DashCharges);
            v.Value(nameof(DashCooldown), DashCooldown);
            v.Value(nameof(DashTicksLeft), DashTicksLeft);
            v.Value("DashDirection.X", DashDirection.X);
            v.Value("DashDirection.Y", DashDirection.Y);
            v.Value(nameof(DashFromGround), DashFromGround);
            v.Value(nameof(WallJumpLockTicks), WallJumpLockTicks);
            v.Value(nameof(WallJumpLockDirection), (int)WallJumpLockDirection);
            v.Value(nameof(DropThroughTicks), DropThroughTicks);
            v.Value(nameof(DropThroughBox), DropThroughBox);
            v.Value(nameof(Health), Health);
            v.Value(nameof(HitstopTicks), HitstopTicks);
            v.Value(nameof(StunTicks), StunTicks);
            v.Value(nameof(ComboHits), ComboHits);
            v.Value(nameof(AttackId), AttackId);
            v.Value(nameof(AttackHasHit), AttackHasHit);
            v.Value(nameof(AttackFromAir), AttackFromAir);
            v.Value(nameof(ParryTicks), ParryTicks);
            v.Value(nameof(TicksSinceGuardPress), TicksSinceGuardPress);
            v.Value(nameof(PendingPresses), (int)PendingPresses);
            v.Value(nameof(LungeBounces), LungeBounces);
            v.Value(nameof(ContactCooldown), ContactCooldown);
            v.Value(nameof(ThrowPending), ThrowPending);
            v.Value("ThrowDirection.X", ThrowDirection.X);
            v.Value("ThrowDirection.Y", ThrowDirection.Y);
            v.Value(nameof(SpikeImmunityTicks), SpikeImmunityTicks);
            v.Value(nameof(BoundaryImmunityTicks), BoundaryImmunityTicks);
            v.Value(nameof(OnProp), OnProp);
            v.Value(nameof(BoxDamageTicks), BoxDamageTicks);
            v.Value(nameof(BoxDamageSum), BoxDamageSum);
            v.Value(nameof(Form), (int)Form);
            v.Value(nameof(OwnedForms), (int)OwnedForms);
            v.Value(nameof(Energy), Energy);
            v.Value(nameof(EnergyRegenDelay), EnergyRegenDelay);
            v.Value(nameof(ActionPaid), ActionPaid);
            v.Value(nameof(ShotPending), ShotPending);
            v.Begin(nameof(Augments));
            Augments.Visit(ref v);
            v.End();
            v.Value("ShotDirection.X", ShotDirection.X);
            v.Value("ShotDirection.Y", ShotDirection.Y);
            v.Value("AttackDir.X", AttackDir.X);
            v.Value("AttackDir.Y", AttackDir.Y);
            v.Value(nameof(AttackBuffer), AttackBuffer);
            v.Value(nameof(GrabProtectTicks), GrabProtectTicks);
            v.Value(nameof(SplatDamageTaken), SplatDamageTaken);
            v.Value(nameof(Surface), (int)Surface);
            v.Value(nameof(SurfaceOuter), SurfaceOuter);
            v.Value(nameof(SurfaceCirc), (int)SurfaceCirc);
            v.Value(nameof(SurfaceKeys), (int)SurfaceKeys);
            v.Value(nameof(GravityDir), (int)GravityDir);
            v.Value(nameof(GravityPiece), GravityPiece);
            v.Value(nameof(GravityLock), GravityLock);
            v.Value(nameof(MantleTicks), MantleTicks);
            v.Value("MantleFrom.X", MantleFrom.X);
            v.Value("MantleFrom.Y", MantleFrom.Y);
            v.Value("MantleTo.X", MantleTo.X);
            v.Value("MantleTo.Y", MantleTo.Y);
        }
    }
}
