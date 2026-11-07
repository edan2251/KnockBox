using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 공격 버튼이 무엇을 하는가(03-combat.md 1절).
    /// Test: 테스트용 공격(CP4). Charge: 모드 A 돌진 충돌(CP5). Swing: 모드 B 무기 휘두르기(CP6).
    /// </summary>
    public enum CombatMode : byte
    {
        Test,
        Charge,
        Swing,
    }

    /// <summary>SimConfig.GetAttack의 공격 번호.</summary>
    public static class AttackIds
    {
        public const int Test = 0;
        public const int SwingForward = 1;
        public const int SwingUp = 2;
        public const int SwingDown = 3;
        /// <summary>몸 폼의 주먹(CR-004). 판정 원은 조준 방향(AttackDir) 기준으로 놓인다.</summary>
        public const int Punch = 4;
        public const int Pan = 5;
        public const int Pistol = 6;
    }

    /// <summary>
    /// 몸 폼: 현재는 Attack 탭 = 주먹/무기, 홀드 = 박치기(CombinedAttackInput).
    /// 구 기록의 별도 Charge 버튼과 대시가 있던 탭/홀드도 각각 유지한다. TapHoldTicks 뒤부터 박치기 충전을 센다.
    /// 기본값(0)은 몸 폼이 없는 설정이다(BrawlParams.Combat가 채운다).
    /// </summary>
    [Serializable]
    public struct BodyParams
    {
        /// <summary>새 Brawl 조작: Attack은 펀치, Charge는 박치기. false는 기존 입력 기록의 탭/홀드 규칙.</summary>
        public bool SeparateChargeInput;
        /// <summary>현재 좌클릭 탭/홀드: 무기도 짧게 떼면 사용하며 대시는 비활성화. false면 이전 기록 규칙.</summary>
        public bool CombinedAttackInput;
        /// <summary>박치기 준비 시간 T(틱), 구 입력의 탭/홀드 임계. 기본 8.</summary>
        public int TapHoldTicks;
        /// <summary>준비 T 뒤 실제로 충전해야 하는 최소 틱. 미달 해제는 취소하며, 0은 이전 기록의 즉시 돌진 규칙.</summary>
        public int MinLungeChargeTicks;
        /// <summary>주먹 후딜 중 누른 좌클릭을 저장해 두는 틱.</summary>
        public int PunchBufferTicks;
        public AttackDef Punch;
        /// <summary>잡은 상태(Holding)의 최대 이동 속도(5.2 "이동은 느리게").</summary>
        public float HoldMoveSpeed;
        /// <summary>벽꽝 추가 피해 = 충돌 속도 × 이 값(반올림). 던지기 기본 피해(Grab.ThrowDamage)와 따로.</summary>
        public float SplatDamagePerSpeed;
        /// <summary>한 번 날아가는 동안(회복 전까지) 받는 벽꽝 추가 피해 합의 상한.</summary>
        public int SplatDamageMax;
        /// <summary>벽꽝 경직(고정, 누적하지 않고 남은 경직과 큰 쪽).</summary>
        public int SplatHitstun;
        /// <summary>던져지거나 벽꽝을 맞고 회복한 뒤 잡히지 않는 틱(재그랩 보호).</summary>
        public int RegrabProtectTicks;
        /// <summary>잡은 상태의 점프 속도(0이면 점프 불가).</summary>
        public float HoldJumpSpeed;
        /// <summary>던져진 상태의 중력 배율(일직선으로 멀리 날아가게).</summary>
        public float ThrownGravityScale;
        /// <summary>받아내기 성공 뒤 자동 던짐 전 두 파이터 히트스탑(패링 느낌의 정지).</summary>
        public int CatchHitstop;
        /// <summary>표면 이동 실험(7절): 0 끔, 1 = D(붙기만), 2 = E(붙기 + 외곽 표면 달리기).</summary>
        public int SurfaceMode;
        /// <summary>붙은 채 표면 쪽으로 미는 속도(접촉 유지용, D 모드).</summary>
        public float SurfaceHoldPush;
        /// <summary>E: 떠 있는 지형 중력장 반경(몸 상자와 지형 사이 틈).</summary>
        public float FieldRadius;
        /// <summary>E: S로 놓고 떨어진 뒤 중력장·붙기를 무시하는 틱.</summary>
        public int SurfaceReleaseTicks;
        /// <summary>벽 기어오르기 속도(0이면 끔). 벽 쪽 + W.</summary>
        public float ClimbSpeed;
        /// <summary>모서리 잡기: 지형 윗면이 머리 위 이만큼 안에 있으면 잡고 올라간다.</summary>
        public float LedgeReach;
        /// <summary>모서리 오르기에 걸리는 틱(앞 60%는 위로, 나머지는 윗면 쪽으로).</summary>
        public int MantleTicks;

        public readonly bool Enabled => TapHoldTicks > 0;

        /// <summary>주먹: 선딜 3 / 활성 3 / 후딜 10, 데미지 8, 넉백 12(조준 방향에서 위로 20°).</summary>
        public static BodyParams Default => new BodyParams
        {
            TapHoldTicks = 8,
            PunchBufferTicks = 6,
            Punch = new AttackDef
            {
                StartupTicks = 3,
                ActiveTicks = 3,
                RecoveryTicks = 10,
                Damage = 8,
                KnockbackStrength = 12f,
                KnockbackDirection = new Vec2(0.9396926f, 0.34202015f),
                CircleCount = 2,
                Circle0 = new HitCircle(new Vec2(0.5f, 0f), new Vec2(1.0f, 0f), 0.3f),
                Circle1 = new HitCircle(new Vec2(0.35f, 0f), new Vec2(0.65f, 0f), 0.22f),
            },
            HoldMoveSpeed = 2.5f,
            SplatDamagePerSpeed = 0.5f,
            SplatDamageMax = 12,
            SplatHitstun = 18,
            RegrabProtectTicks = 45,
            HoldJumpSpeed = 12f,
            ThrownGravityScale = 0.2f,
            CatchHitstop = 8,
            SurfaceMode = 0,
            SurfaceHoldPush = 1f,
            FieldRadius = 2f,
            SurfaceReleaseTicks = 20,
            ClimbSpeed = 0f,
            LedgeReach = 0.45f,
            MantleTicks = 15,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            // false일 때는 기존 기록의 설정 체크섬을 그대로 유지한다.
            if (SeparateChargeInput)
                v.Value(nameof(SeparateChargeInput), true);
            if (CombinedAttackInput)
                // Hasher는 필드 이름을 해시하지 않는다. 별도 차징의 bool 1과 다른 바이트열이어야 한다.
                v.Value(nameof(CombinedAttackInput), 2);
            if (MinLungeChargeTicks > 0)
                v.Value(nameof(MinLungeChargeTicks), MinLungeChargeTicks);
            v.Value(nameof(TapHoldTicks), TapHoldTicks);
            v.Value(nameof(PunchBufferTicks), PunchBufferTicks);
            v.Begin(nameof(Punch));
            Punch.Visit(ref v);
            v.End();
            v.Value(nameof(HoldMoveSpeed), HoldMoveSpeed);
            v.Value(nameof(SplatDamagePerSpeed), SplatDamagePerSpeed);
            v.Value(nameof(SplatDamageMax), SplatDamageMax);
            v.Value(nameof(SplatHitstun), SplatHitstun);
            v.Value(nameof(RegrabProtectTicks), RegrabProtectTicks);
            v.Value(nameof(HoldJumpSpeed), HoldJumpSpeed);
            v.Value(nameof(ThrownGravityScale), ThrownGravityScale);
            v.Value(nameof(CatchHitstop), CatchHitstop);
            v.Value(nameof(SurfaceMode), SurfaceMode);
            v.Value(nameof(SurfaceHoldPush), SurfaceHoldPush);
            v.Value(nameof(FieldRadius), FieldRadius);
            v.Value(nameof(SurfaceReleaseTicks), SurfaceReleaseTicks);
            v.Value(nameof(ClimbSpeed), ClimbSpeed);
            v.Value(nameof(LedgeReach), LedgeReach);
            v.Value(nameof(MantleTicks), MantleTicks);
        }
    }

    /// <summary>보조 스킬: 잡기와 던지기(03-combat.md 6절). 두 모드 공통.</summary>
    [Serializable]
    public struct GrabParams
    {
        public int StartupTicks;
        public int ActiveTicks;
        /// <summary>헛잡기 후딜.</summary>
        public int WhiffRecoveryTicks;
        /// <summary>잡기 상자 중심의 몸 앞 거리.</summary>
        public float BoxOffset;
        public float BoxWidth;
        public float BoxHeight;
        /// <summary>붙잡은 상대를 고정하는 몸 앞 거리.</summary>
        public float HoldOffset;
        public int MaxHoldTicks;
        public float ThrowSpeed;
        public int ThrowDamage;
        /// <summary>던져진 상태(피격 경직과 같은 규칙) 틱.</summary>
        public int ThrownTicks;
        /// <summary>중립 던지기 방향(바라보는 쪽 기준). 설정을 만들 때 정규화한다.</summary>
        public Vec2 NeutralThrowDirection;
        /// <summary>뒤로 던질 때 상대를 등 뒤로 옮기는 거리.</summary>
        public float BackThrowOffset;
        public float ThrownSplatSpeed;
        public int ThrownSplatDamage;
        /// <summary>잡기 풀기: 서로 반대로 튕기는 속도, 반동, 히트스탑.</summary>
        public float TechSpeed;
        public int TechRecoilTicks;
        public int TechHitstop;

        public readonly int TotalWhiffTicks => StartupTicks + ActiveTicks + WhiffRecoveryTicks;

        public static GrabParams Default => new GrabParams
        {
            StartupTicks = 5,
            ActiveTicks = 3,
            WhiffRecoveryTicks = 22,
            BoxOffset = 0.65f,
            BoxWidth = 0.7f,
            BoxHeight = 0.9f,
            HoldOffset = 0.8f,
            MaxHoldTicks = 20,
            ThrowSpeed = 28f,
            ThrowDamage = 6,
            ThrownTicks = 24,
            NeutralThrowDirection = new Vec2(0.8660254f, 0.5f),
            BackThrowOffset = 0.95f,
            ThrownSplatSpeed = 12f,
            ThrownSplatDamage = 10,
            TechSpeed = 10f,
            TechRecoilTicks = 12,
            TechHitstop = 6,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(StartupTicks), StartupTicks);
            v.Value(nameof(ActiveTicks), ActiveTicks);
            v.Value(nameof(WhiffRecoveryTicks), WhiffRecoveryTicks);
            v.Value(nameof(BoxOffset), BoxOffset);
            v.Value(nameof(BoxWidth), BoxWidth);
            v.Value(nameof(BoxHeight), BoxHeight);
            v.Value(nameof(HoldOffset), HoldOffset);
            v.Value(nameof(MaxHoldTicks), MaxHoldTicks);
            v.Value(nameof(ThrowSpeed), ThrowSpeed);
            v.Value(nameof(ThrowDamage), ThrowDamage);
            v.Value(nameof(ThrownTicks), ThrownTicks);
            v.Value("NeutralThrowDirection.X", NeutralThrowDirection.X);
            v.Value("NeutralThrowDirection.Y", NeutralThrowDirection.Y);
            v.Value(nameof(BackThrowOffset), BackThrowOffset);
            v.Value(nameof(ThrownSplatSpeed), ThrownSplatSpeed);
            v.Value(nameof(ThrownSplatDamage), ThrownSplatDamage);
            v.Value(nameof(TechSpeed), TechSpeed);
            v.Value(nameof(TechRecoilTicks), TechRecoilTicks);
            v.Value(nameof(TechHitstop), TechHitstop);
        }
    }

    /// <summary>모드 B: 무기 휘두르기(03-combat.md 5절).</summary>
    [Serializable]
    public struct SwingModeParams
    {
        public AttackDef Forward;
        public AttackDef Up;
        public AttackDef Down;
        /// <summary>대시 시작 뒤 이 틱이 지나면 공격으로 대시를 캔슬할 수 있다(대시의 관성을 실은 공격).</summary>
        public bool DashAttackCancelEnabled;
        public int DashAttackCancelMinTicks;
        /// <summary>공중에서 시작한 공격 도중 착지하면 이만큼 조작할 수 없다.</summary>
        public int LandingLagTicks;
        /// <summary>참이면 보 폼은 앞 휘두르기 하나만 쓴다(CR-003 CP20). CP16까지의 모드 B 시험은 거짓으로 둔다.</summary>
        public bool ForwardOnly;

        public static SwingModeParams Default => new SwingModeParams
        {
            ForwardOnly = true,
            Forward = AttackDef.SwingForward,
            Up = AttackDef.SwingUp,
            Down = AttackDef.SwingDown,
            DashAttackCancelEnabled = true,
            DashAttackCancelMinTicks = 4,
            LandingLagTicks = 4,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Begin(nameof(Forward));
            Forward.Visit(ref v);
            v.End();
            v.Begin(nameof(Up));
            Up.Visit(ref v);
            v.End();
            v.Begin(nameof(Down));
            Down.Visit(ref v);
            v.End();
            v.Value(nameof(DashAttackCancelEnabled), DashAttackCancelEnabled);
            v.Value(nameof(DashAttackCancelMinTicks), DashAttackCancelMinTicks);
            v.Value(nameof(LandingLagTicks), LandingLagTicks);
            v.Value(nameof(ForwardOnly), ForwardOnly);
        }
    }

    /// <summary>모드 A: 돌진 충돌(03-combat.md 4절). 위력 = max(0, 속도·법선) × 공격성 가중치.</summary>
    [Serializable]
    public struct ChargeModeParams
    {
        /// <summary>충전 중 지상(과 공중) 최대 이동 속도.</summary>
        public float ChargeMoveMaxSpeed;
        /// <summary>충전 중 공중 최대 낙하 속도(살짝 떠 있음).</summary>
        public float ChargeFallMaxSpeed;
        /// <summary>이보다 짧게 누르고 떼면 최소 돌진.</summary>
        public int MinChargeTicks;
        public int FullChargeTicks;
        /// <summary>최대 충전 뒤 이만큼 더 누르고 있으면 자동 발사.</summary>
        public int FullChargeHoldTicks;
        public float LungeMinSpeed;
        public float LungeMaxSpeed;
        /// <summary>즉발 돌진 증강(5.4)의 발사 속도(속도 배율이 곱해진다).</summary>
        public float InstantLungeSpeed;
        public float LungeGravityMultiplier;
        /// <summary>속도가 이보다 작아지면 돌진이 끝난다.</summary>
        public float LungeEndSpeed;
        public int LungeMaxTicks;
        public float LungeBounceRestitution;
        public int LungeMaxBounces;
        /// <summary>돌진 감속(유닛/초²). 0이면 감속 없음(CP16 규칙). CR-004: 짧고 빠른 박치기.</summary>
        public float LungeDrag;
        /// <summary>
        /// 0보다 크면 튕김을 다 쓴 돌진이 지형에 부딪힐 때 반사 대신 멈추고 법선 쪽으로 이 속도만큼 작은 반동을 받는다(CR-004 5.1 "벽에 닿음: 종료").
        /// 0이면 CP16 규칙(속도를 그대로 두고 돌진만 끝냄).
        /// </summary>
        public float LungeStopRecoil;
        /// <summary>LungeStopRecoil로 멈출 때 남기는 접선 속도 비율.</summary>
        public float LungeStopKeep;
        public float LungeAggression;
        public float DashAggression;
        /// <summary>위력이 이 이상이어야 피해를 준다.</summary>
        public float PowerThreshold;
        /// <summary>둘 다 기준 이상이고 위력 차이가 이보다 작으면 클래시.</summary>
        public float ClashPowerMargin;
        /// <summary>같은 두 파이터 사이의 몸 충돌 판정 간격.</summary>
        public int ContactCooldownTicks;
        /// <summary>몸이 이 거리 안이면 닿은 것으로 본다(밀어내기로 딱 붙은 상태 포함).</summary>
        public float ContactSlop;
        public float DamagePerPower;
        public int MinDamage;
        public int MaxDamage;
        public float KnockbackPerPower;
        public float MinKnockback;
        /// <summary>이긴 쪽 속도 배율(살짝 튕겨 나옴).</summary>
        public float WinnerVelocityScale;
        /// <summary>패링당한 돌진의 반사 배율.</summary>
        public float ParryReflectScale;
        /// <summary>클래시 반동 속도 = max(ClashSpeed, 큰 위력 × 이 값).</summary>
        public float ClashSpeedPerPower;

        public static ChargeModeParams Default => new ChargeModeParams
        {
            ChargeMoveMaxSpeed = 3f,
            ChargeFallMaxSpeed = 4f,
            MinChargeTicks = 4,
            FullChargeTicks = 36,
            FullChargeHoldTicks = 24,
            LungeMinSpeed = 18f,
            LungeMaxSpeed = 34f,
            InstantLungeSpeed = 26f,
            LungeGravityMultiplier = 0.3f,
            LungeEndSpeed = 12f,
            LungeMaxTicks = 30,
            LungeBounceRestitution = 0.6f,
            LungeMaxBounces = 2,
            LungeAggression = 1f,
            // CR-003 4.4: 이동 대시의 몸통 공격성은 끈다(에너지 없이 피해를 주는 구멍). CP16까지 0.6.
            DashAggression = 0f,
            PowerThreshold = 6f,
            ClashPowerMargin = 4f,
            ContactCooldownTicks = 10,
            ContactSlop = 0.05f,
            DamagePerPower = 0.6f,
            MinDamage = 4,
            MaxDamage = 24,
            KnockbackPerPower = 0.9f,
            MinKnockback = 10f,
            WinnerVelocityScale = -0.25f,
            ParryReflectScale = 0.8f,
            ClashSpeedPerPower = 0.35f,
        };

        /// <summary>충전 틱 → 돌진 속도(18 → 34 선형, MinChargeTicks 미만은 최소).</summary>
        public readonly float LungeSpeed(int chargeTicks)
        {
            float ratio = chargeTicks < MinChargeTicks ? 0f : SimMath.Min(chargeTicks, FullChargeTicks) / (float)FullChargeTicks;
            return LungeMinSpeed + (LungeMaxSpeed - LungeMinSpeed) * ratio;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(ChargeMoveMaxSpeed), ChargeMoveMaxSpeed);
            v.Value(nameof(ChargeFallMaxSpeed), ChargeFallMaxSpeed);
            v.Value(nameof(MinChargeTicks), MinChargeTicks);
            v.Value(nameof(FullChargeTicks), FullChargeTicks);
            v.Value(nameof(FullChargeHoldTicks), FullChargeHoldTicks);
            v.Value(nameof(LungeMinSpeed), LungeMinSpeed);
            v.Value(nameof(LungeMaxSpeed), LungeMaxSpeed);
            v.Value(nameof(InstantLungeSpeed), InstantLungeSpeed);
            v.Value(nameof(LungeGravityMultiplier), LungeGravityMultiplier);
            v.Value(nameof(LungeEndSpeed), LungeEndSpeed);
            v.Value(nameof(LungeMaxTicks), LungeMaxTicks);
            v.Value(nameof(LungeBounceRestitution), LungeBounceRestitution);
            v.Value(nameof(LungeMaxBounces), LungeMaxBounces);
            v.Value(nameof(LungeDrag), LungeDrag);
            v.Value(nameof(LungeStopRecoil), LungeStopRecoil);
            v.Value(nameof(LungeStopKeep), LungeStopKeep);
            v.Value(nameof(LungeAggression), LungeAggression);
            v.Value(nameof(DashAggression), DashAggression);
            v.Value(nameof(PowerThreshold), PowerThreshold);
            v.Value(nameof(ClashPowerMargin), ClashPowerMargin);
            v.Value(nameof(ContactCooldownTicks), ContactCooldownTicks);
            v.Value(nameof(ContactSlop), ContactSlop);
            v.Value(nameof(DamagePerPower), DamagePerPower);
            v.Value(nameof(MinDamage), MinDamage);
            v.Value(nameof(MaxDamage), MaxDamage);
            v.Value(nameof(KnockbackPerPower), KnockbackPerPower);
            v.Value(nameof(MinKnockback), MinKnockback);
            v.Value(nameof(WinnerVelocityScale), WinnerVelocityScale);
            v.Value(nameof(ParryReflectScale), ParryReflectScale);
            v.Value(nameof(ClashSpeedPerPower), ClashSpeedPerPower);
        }
    }

    /// <summary>
    /// 공격 판정 원 하나. 위치는 파이터 중심 기준이고 x는 바라보는 방향이다.
    /// 활성 구간 동안 Start → Mid → End를 꺾은선으로 따라간다(휘두르는 호를 삼각함수 없이 근사).
    /// </summary>
    [Serializable]
    public struct HitCircle
    {
        /// <summary>활성 첫 틱의 위치.</summary>
        public Vec2 Start;
        /// <summary>활성 구간 가운데의 위치.</summary>
        public Vec2 Mid;
        /// <summary>활성 마지막 틱의 위치.</summary>
        public Vec2 End;
        public float Radius;

        public HitCircle(Vec2 start, Vec2 mid, Vec2 end, float radius)
        {
            Start = start;
            Mid = mid;
            End = end;
            Radius = radius;
        }

        /// <summary>직선으로 움직이는 원(가운데 = 두 끝의 중간).</summary>
        public HitCircle(Vec2 start, Vec2 end, float radius)
            : this(start, (start + end) * 0.5f, end, radius)
        {
        }

        /// <summary>활성 진행도 t(0 = 첫 틱, 1 = 마지막 틱)의 위치.</summary>
        public readonly Vec2 At(float t)
        {
            if (t <= 0.5f)
                return Start + (Mid - Start) * (t * 2f);
            return Mid + (End - Mid) * ((t - 0.5f) * 2f);
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value("Start.X", Start.X);
            v.Value("Start.Y", Start.Y);
            v.Value("Mid.X", Mid.X);
            v.Value("Mid.Y", Mid.Y);
            v.Value("End.X", End.X);
            v.Value("End.Y", End.Y);
            v.Value(nameof(Radius), Radius);
        }
    }

    /// <summary>
    /// 공격 하나의 데이터. 시작 → 활성 → 후딜 틱, 데미지, 넉백, 판정 원(최대 3개).
    /// 넉백 방향은 바라보는 방향 기준 단위 벡터이며 SimConfig를 만들 때 정규화한다(삼각함수를 쓰지 않는다).
    /// </summary>
    [Serializable]
    public struct AttackDef
    {
        public const int MaxCircles = 3;

        public int StartupTicks;
        public int ActiveTicks;
        public int RecoveryTicks;
        public int Damage;
        public float KnockbackStrength;
        public Vec2 KnockbackDirection;
        public int CircleCount;
        public HitCircle Circle0;
        public HitCircle Circle1;
        public HitCircle Circle2;
        /// <summary>0보다 크면 명중했을 때 공격자 vy를 이 값으로 바꾸고 공중 점프·대시를 회복한다(내려찍기 포고 튕김).</summary>
        public float PogoSpeed;

        public readonly int TotalTicks => StartupTicks + ActiveTicks + RecoveryTicks;

        /// <summary>실제로 쓰는 판정 원 개수(0~3). 설정 값은 그대로 두고 쓸 때 자른다(설정 체크섬이 모든 값을 덮게).</summary>
        public readonly int UsedCircles => SimMath.Clamp(CircleCount, 0, MaxCircles);

        public readonly HitCircle GetCircle(int index) => index == 0 ? Circle0 : index == 1 ? Circle1 : Circle2;

        /// <summary>
        /// 테스트용 공격(CP4): 앞으로 뻗는 주먹. 시작 5 / 활성 4 / 후딜 14, 데미지 12, 넉백 14 앞쪽 위 30°.
        /// 프레임 수와 넉백은 모드 B의 앞 휘두르기와 같게 두어 공통 규칙을 현실적인 값으로 시험한다.
        /// </summary>
        public static AttackDef TestJab => new AttackDef
        {
            StartupTicks = 5,
            ActiveTicks = 4,
            RecoveryTicks = 14,
            Damage = 12,
            KnockbackStrength = 14f,
            KnockbackDirection = new Vec2(0.8660254f, 0.5f),
            CircleCount = 2,
            Circle0 = new HitCircle(new Vec2(0.55f, 0.05f), new Vec2(0.95f, 0.05f), 0.3f),
            Circle1 = new HitCircle(new Vec2(0.35f, 0.05f), new Vec2(0.6f, 0.05f), 0.22f),
        };

        // 모드 B 기술. 판정 원 3개는 무기를 따라 손잡이 쪽(0.45)부터 끝(1.15)까지 놓이고,
        // 어깨 근처 회전축을 도는 호의 세 각도(시작·가운데·끝)를 키프레임으로 쓴다(값은 미리 계산한 상수).

        /// <summary>앞 휘두르기: 5/4/14, 데미지 12, 넉백 14 앞쪽 위 30°. 머리 위 앞(70°)에서 앞 아래(-25°)로.</summary>
        public static AttackDef SwingForward => new AttackDef
        {
            StartupTicks = 5,
            ActiveTicks = 4,
            RecoveryTicks = 14,
            Damage = 12,
            KnockbackStrength = 14f,
            KnockbackDirection = new Vec2(0.8660254f, 0.5f),
            CircleCount = 3,
            Circle0 = new HitCircle(new Vec2(0.504f, 0.673f), new Vec2(0.773f, 0.404f), new Vec2(0.758f, 0.06f), 0.22f),
            Circle1 = new HitCircle(new Vec2(0.624f, 1.002f), new Vec2(1.102f, 0.524f), new Vec2(1.075f, -0.088f), 0.24f),
            Circle2 = new HitCircle(new Vec2(0.743f, 1.331f), new Vec2(1.431f, 0.643f), new Vec2(1.392f, -0.236f), 0.26f),
        };

        /// <summary>위 휘두르기: 5/4/16, 데미지 10, 넉백 16 위 80°. 앞(20°)에서 머리 위(90°)를 지나 뒤(160°)로.</summary>
        public static AttackDef SwingUp => new AttackDef
        {
            StartupTicks = 5,
            ActiveTicks = 4,
            RecoveryTicks = 16,
            Damage = 10,
            KnockbackStrength = 16f,
            KnockbackDirection = new Vec2(0.1736482f, 0.9848078f),
            CircleCount = 3,
            Circle0 = new HitCircle(new Vec2(0.573f, 0.454f), new Vec2(0.15f, 0.75f), new Vec2(-0.273f, 0.454f), 0.22f),
            Circle1 = new HitCircle(new Vec2(0.902f, 0.574f), new Vec2(0.15f, 1.1f), new Vec2(-0.602f, 0.574f), 0.24f),
            Circle2 = new HitCircle(new Vec2(1.231f, 0.693f), new Vec2(0.15f, 1.45f), new Vec2(-0.931f, 0.693f), 0.26f),
        };

        /// <summary>
        /// 아래 내려찍기(공중 전용): 6/5/16, 데미지 10, 넉백 12 아래 앞 -70°. 앞 위(40°)에서 바로 아래(-90°)로.
        /// 명중하면 공격자 vy = 18(포고 튕김), 공중 점프·대시 회복.
        /// </summary>
        public static AttackDef SwingDown => new AttackDef
        {
            StartupTicks = 6,
            ActiveTicks = 5,
            RecoveryTicks = 16,
            Damage = 10,
            KnockbackStrength = 12f,
            KnockbackDirection = new Vec2(0.3420201f, -0.9396926f),
            CircleCount = 3,
            Circle0 = new HitCircle(new Vec2(0.645f, 0.389f), new Vec2(0.69f, -0.125f), new Vec2(0.3f, -0.35f), 0.22f),
            Circle1 = new HitCircle(new Vec2(0.913f, 0.614f), new Vec2(0.993f, -0.3f), new Vec2(0.3f, -0.7f), 0.24f),
            Circle2 = new HitCircle(new Vec2(1.181f, 0.839f), new Vec2(1.296f, -0.475f), new Vec2(0.3f, -1.05f), 0.26f),
            PogoSpeed = 18f,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(StartupTicks), StartupTicks);
            v.Value(nameof(ActiveTicks), ActiveTicks);
            v.Value(nameof(RecoveryTicks), RecoveryTicks);
            v.Value(nameof(Damage), Damage);
            v.Value(nameof(KnockbackStrength), KnockbackStrength);
            v.Value("KnockbackDirection.X", KnockbackDirection.X);
            v.Value("KnockbackDirection.Y", KnockbackDirection.Y);
            v.Value(nameof(CircleCount), CircleCount);
            v.Begin(nameof(Circle0));
            Circle0.Visit(ref v);
            v.End();
            v.Begin(nameof(Circle1));
            Circle1.Visit(ref v);
            v.End();
            v.Begin(nameof(Circle2));
            Circle2.Visit(ref v);
            v.End();
            v.Value(nameof(PogoSpeed), PogoSpeed);
        }
    }

    /// <summary>
    /// 전투 공통 규칙의 튜닝값(03-combat.md 3절). 시간은 틱, 속도는 유닛/초.
    /// CombatTuning(ScriptableObject)이 이 구조체를 그대로 직렬화한다.
    /// </summary>
    [Serializable]
    public struct CombatParams
    {
        public int MaxHealth;

        // 히트스탑: 일반 명중은 clamp(Base + 데미지 × PerDamage, Min, Max).
        public float HitstopBase;
        public float HitstopPerDamage;
        public int HitstopMin;
        public int HitstopMax;
        public int ClashHitstop;
        public int ParryHitstop;
        public int WallSplatHitstop;
        public int KoHitstop;

        // 넉백과 피격 경직: 경직 = clamp(round(넉백 × PerKnockback), Min, Max).
        public float HitstunPerKnockback;
        public int HitstunMin;
        public int HitstunMax;
        public float ComboKnockbackDecay;
        public float ComboHitstunDecay;
        /// <summary>연속으로 맞을 수 있는 최대 횟수. 이만큼 맞은 파이터는 경직이 풀릴 때까지 공격에 맞지 않는다.</summary>
        public int MaxComboHits;

        // 벽꽝과 튕김
        public float WallSplatSpeed;
        public int WallSplatDamage;
        public float WallSplatRestitution;
        public int WallSplatHitstun;
        public float BounceRestitution;
        /// <summary>법선 속도가 이보다 작으면 튕기지 않고 그냥 닿는다(바닥에서 잔떨림 방지).</summary>
        public float BounceMinSpeed;

        // 가드와 패링
        public int ParryWindowTicks;
        public int ParryCooldownTicks;
        public float GuardMaxSpeed;
        public float GuardDamageMultiplier;
        public float GuardKnockbackMultiplier;
        public int GuardStunTicks;
        public float GuardHitstopMultiplier;
        public int StaggerTicks;
        public float StaggerPushSpeed;

        // 클래시
        public float ClashSpeed;
        public int RecoilTicks;

        /// <summary>공격 중 공중 조작 배율. 지상에서는 입력 가속 없이 감속만 한다.</summary>
        public float AttackAirControlMultiplier;

        // 가시(04-arena.md 3절): 닿으면 데미지, 위로 튕김, 경직, 면역 동안은 바닥처럼 선다.
        public int SpikeDamage;
        public float SpikeBounceSpeed;
        public int SpikeHitstun;
        public int SpikeImmunityTicks;

        // 경계 튕김(03-combat.md 3절, CR-002): 화면 경계에 닿으면 피해, 반사, 경직, 히트스탑, 면역.
        public int BoundaryBaseDamage;
        public float BoundaryDamagePerSpeed;
        public int BoundaryMinDamage;
        public int BoundaryMaxDamage;
        public float BoundaryRestitution;
        public float BoundaryMinReboundSpeed;
        public int BoundaryHitstun;
        public int BoundaryHitstop;
        public int BoundaryImmunityTicks;
        public float BoundaryImmuneRestitution;

        public AttackDef TestAttack;
        public ChargeModeParams Charge;
        public SwingModeParams Swing;
        public GrabParams Grab;
        /// <summary>구조물 물리와 캐릭터 상호작용(09-props-physics.md, CP14).</summary>
        public PropParams Props;
        /// <summary>공격 에너지(10-forms-and-growth.md 4.4, CP18).</summary>
        public EnergyParams Energy;
        /// <summary>바위 폼의 투사체(CP19).</summary>
        public RockParams Rock;
        /// <summary>몸 폼(CR-004). 기본은 꺼짐(0).</summary>
        public BodyParams Body;
        /// <summary>라운드당 하나의 프라이팬/권총 프로토타입. 구 기록에서는 기본값(꺼짐).</summary>
        public BrawlWeaponParams Weapons;

        public static CombatParams Default => new CombatParams
        {
            MaxHealth = 100,

            HitstopBase = 4f,
            HitstopPerDamage = 0.25f,
            HitstopMin = 4,
            HitstopMax = 12,
            ClashHitstop = 8,
            ParryHitstop = 10,
            WallSplatHitstop = 6,
            KoHitstop = 20,

            HitstunPerKnockback = 1.1f,
            HitstunMin = 8,
            HitstunMax = 45,
            ComboKnockbackDecay = 0.85f,
            ComboHitstunDecay = 0.8f,
            MaxComboHits = 3,

            WallSplatSpeed = 14f,
            WallSplatDamage = 6,
            WallSplatRestitution = 0.45f,
            WallSplatHitstun = 18,
            BounceRestitution = 0.35f,
            BounceMinSpeed = 2f,

            ParryWindowTicks = 6,
            ParryCooldownTicks = 24,
            GuardMaxSpeed = 3f,
            GuardDamageMultiplier = 0.2f,
            GuardKnockbackMultiplier = 0.35f,
            GuardStunTicks = 10,
            GuardHitstopMultiplier = 0.5f,
            StaggerTicks = 28,
            StaggerPushSpeed = 8f,

            ClashSpeed = 8f,
            RecoilTicks = 12,

            AttackAirControlMultiplier = 0.5f,

            SpikeDamage = 15,
            SpikeBounceSpeed = 20f,
            SpikeHitstun = 20,
            SpikeImmunityTicks = 45,

            BoundaryBaseDamage = 3,
            BoundaryDamagePerSpeed = 0.25f,
            BoundaryMinDamage = 3,
            BoundaryMaxDamage = 15,
            BoundaryRestitution = 1.1f,
            BoundaryMinReboundSpeed = 14f,
            BoundaryHitstun = 14,
            BoundaryHitstop = 5,
            BoundaryImmunityTicks = 60,
            BoundaryImmuneRestitution = 0.5f,

            TestAttack = AttackDef.TestJab,
            Charge = ChargeModeParams.Default,
            Swing = SwingModeParams.Default,
            Grab = GrabParams.Default,
            Props = PropParams.Default,
            Energy = EnergyParams.Default,
            Rock = RockParams.Default,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(MaxHealth), MaxHealth);
            v.Value(nameof(HitstopBase), HitstopBase);
            v.Value(nameof(HitstopPerDamage), HitstopPerDamage);
            v.Value(nameof(HitstopMin), HitstopMin);
            v.Value(nameof(HitstopMax), HitstopMax);
            v.Value(nameof(ClashHitstop), ClashHitstop);
            v.Value(nameof(ParryHitstop), ParryHitstop);
            v.Value(nameof(WallSplatHitstop), WallSplatHitstop);
            v.Value(nameof(KoHitstop), KoHitstop);
            v.Value(nameof(HitstunPerKnockback), HitstunPerKnockback);
            v.Value(nameof(HitstunMin), HitstunMin);
            v.Value(nameof(HitstunMax), HitstunMax);
            v.Value(nameof(ComboKnockbackDecay), ComboKnockbackDecay);
            v.Value(nameof(ComboHitstunDecay), ComboHitstunDecay);
            v.Value(nameof(MaxComboHits), MaxComboHits);
            v.Value(nameof(WallSplatSpeed), WallSplatSpeed);
            v.Value(nameof(WallSplatDamage), WallSplatDamage);
            v.Value(nameof(WallSplatRestitution), WallSplatRestitution);
            v.Value(nameof(WallSplatHitstun), WallSplatHitstun);
            v.Value(nameof(BounceRestitution), BounceRestitution);
            v.Value(nameof(BounceMinSpeed), BounceMinSpeed);
            v.Value(nameof(ParryWindowTicks), ParryWindowTicks);
            v.Value(nameof(ParryCooldownTicks), ParryCooldownTicks);
            v.Value(nameof(GuardMaxSpeed), GuardMaxSpeed);
            v.Value(nameof(GuardDamageMultiplier), GuardDamageMultiplier);
            v.Value(nameof(GuardKnockbackMultiplier), GuardKnockbackMultiplier);
            v.Value(nameof(GuardStunTicks), GuardStunTicks);
            v.Value(nameof(GuardHitstopMultiplier), GuardHitstopMultiplier);
            v.Value(nameof(StaggerTicks), StaggerTicks);
            v.Value(nameof(StaggerPushSpeed), StaggerPushSpeed);
            v.Value(nameof(ClashSpeed), ClashSpeed);
            v.Value(nameof(RecoilTicks), RecoilTicks);
            v.Value(nameof(AttackAirControlMultiplier), AttackAirControlMultiplier);
            v.Value(nameof(SpikeDamage), SpikeDamage);
            v.Value(nameof(SpikeBounceSpeed), SpikeBounceSpeed);
            v.Value(nameof(SpikeHitstun), SpikeHitstun);
            v.Value(nameof(SpikeImmunityTicks), SpikeImmunityTicks);
            v.Value(nameof(BoundaryBaseDamage), BoundaryBaseDamage);
            v.Value(nameof(BoundaryDamagePerSpeed), BoundaryDamagePerSpeed);
            v.Value(nameof(BoundaryMinDamage), BoundaryMinDamage);
            v.Value(nameof(BoundaryMaxDamage), BoundaryMaxDamage);
            v.Value(nameof(BoundaryRestitution), BoundaryRestitution);
            v.Value(nameof(BoundaryMinReboundSpeed), BoundaryMinReboundSpeed);
            v.Value(nameof(BoundaryHitstun), BoundaryHitstun);
            v.Value(nameof(BoundaryHitstop), BoundaryHitstop);
            v.Value(nameof(BoundaryImmunityTicks), BoundaryImmunityTicks);
            v.Value(nameof(BoundaryImmuneRestitution), BoundaryImmuneRestitution);
            v.Begin(nameof(TestAttack));
            TestAttack.Visit(ref v);
            v.End();
            v.Begin(nameof(Charge));
            Charge.Visit(ref v);
            v.End();
            v.Begin(nameof(Swing));
            Swing.Visit(ref v);
            v.End();
            v.Begin(nameof(Grab));
            Grab.Visit(ref v);
            v.End();
            v.Begin(nameof(Props));
            Props.Visit(ref v);
            v.End();
            v.Begin(nameof(Energy));
            Energy.Visit(ref v);
            v.End();
            v.Begin(nameof(Rock));
            Rock.Visit(ref v);
            v.End();
            v.Begin(nameof(Body));
            Body.Visit(ref v);
            v.End();
            // 꺼진 구 기록은 기존 설정 체크섬을 그대로 유지한다.
            if (Weapons.Enabled)
            {
                v.Begin(nameof(Weapons));
                Weapons.Visit(ref v);
                v.End();
            }
        }
    }
}
