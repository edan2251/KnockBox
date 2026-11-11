using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 이동 튜닝값(02-movement.md). 시간은 틱, 속도는 유닛/초, 가속도는 유닛/초².
    /// MovementTuning(ScriptableObject)이 이 구조체를 그대로 직렬화한다.
    /// CP1의 임시 이동은 일부만 쓰고, 나머지는 CP2에서 쓰기 시작한다.
    /// </summary>
    [Serializable]
    public struct MovementParams
    {
        public float BodySize;

        public float MaxRunSpeed;
        public float GroundAccel;
        public float GroundDecel;
        public float TurnAccelMultiplier;
        public float AirAccel;
        public float AirDecel;

        public float JumpSpeed;
        public float RiseGravity;
        public float FallGravityMultiplier;
        public float EarlyReleaseGravityMultiplier;
        public float ApexSpeedThreshold;
        public float ApexGravityMultiplier;
        public float ApexAirAccelMultiplier;
        public float MaxFallSpeed;
        public float FastFallMaxSpeed;
        public float FastFallGravityMultiplier;
        public int AirJumps;
        public float AirJumpSpeed;
        public int CoyoteTicks;
        public int JumpBufferTicks;

        public float WallSlideMaxSpeed;
        public float WallJumpSpeedX;
        public float WallJumpSpeedY;
        public int WallJumpInputLockTicks;
        public int WallCoyoteTicks;

        public float DashSpeed;
        public int DashTicks;
        public float DashEndSpeedRetention;
        public int DashCharges;
        public int DashCooldownTicks;
        public bool DashJumpCancelEnabled;
        public int DashJumpCancelMinTicks;

        public float SubstepMaxDistance;
        public float GroundProbeDistance;
        public int DropThroughTicks;
        public float BouncePadSpeed;

        public static MovementParams Default => new MovementParams
        {
            BodySize = 0.9f,

            MaxRunSpeed = 11f,
            GroundAccel = 110f,
            GroundDecel = 80f,
            TurnAccelMultiplier = 1.5f,
            AirAccel = 70f,
            AirDecel = 25f,

            JumpSpeed = 22.9f,
            RiseGravity = 65f,
            FallGravityMultiplier = 1.6f,
            EarlyReleaseGravityMultiplier = 2.5f,
            ApexSpeedThreshold = 3f,
            ApexGravityMultiplier = 0.5f,
            ApexAirAccelMultiplier = 1.2f,
            MaxFallSpeed = 30f,
            FastFallMaxSpeed = 40f,
            FastFallGravityMultiplier = 1.4f,
            AirJumps = 1,
            AirJumpSpeed = 19f,
            CoyoteTicks = 6,
            JumpBufferTicks = 8,

            WallSlideMaxSpeed = 5f,
            WallJumpSpeedX = 13f,
            WallJumpSpeedY = 20f,
            WallJumpInputLockTicks = 7,
            WallCoyoteTicks = 5,

            DashSpeed = 26f,
            DashTicks = 8,
            DashEndSpeedRetention = 0.55f,
            DashCharges = 1,
            DashCooldownTicks = 18,
            DashJumpCancelEnabled = true,
            DashJumpCancelMinTicks = 3,

            SubstepMaxDistance = 0.2f,
            GroundProbeDistance = 0.02f,
            DropThroughTicks = 12,
            BouncePadSpeed = 30f,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(BodySize), BodySize);
            v.Value(nameof(MaxRunSpeed), MaxRunSpeed);
            v.Value(nameof(GroundAccel), GroundAccel);
            v.Value(nameof(GroundDecel), GroundDecel);
            v.Value(nameof(TurnAccelMultiplier), TurnAccelMultiplier);
            v.Value(nameof(AirAccel), AirAccel);
            v.Value(nameof(AirDecel), AirDecel);
            v.Value(nameof(JumpSpeed), JumpSpeed);
            v.Value(nameof(RiseGravity), RiseGravity);
            v.Value(nameof(FallGravityMultiplier), FallGravityMultiplier);
            v.Value(nameof(EarlyReleaseGravityMultiplier), EarlyReleaseGravityMultiplier);
            v.Value(nameof(ApexSpeedThreshold), ApexSpeedThreshold);
            v.Value(nameof(ApexGravityMultiplier), ApexGravityMultiplier);
            v.Value(nameof(ApexAirAccelMultiplier), ApexAirAccelMultiplier);
            v.Value(nameof(MaxFallSpeed), MaxFallSpeed);
            v.Value(nameof(FastFallMaxSpeed), FastFallMaxSpeed);
            v.Value(nameof(FastFallGravityMultiplier), FastFallGravityMultiplier);
            v.Value(nameof(AirJumps), AirJumps);
            v.Value(nameof(AirJumpSpeed), AirJumpSpeed);
            v.Value(nameof(CoyoteTicks), CoyoteTicks);
            v.Value(nameof(JumpBufferTicks), JumpBufferTicks);
            v.Value(nameof(WallSlideMaxSpeed), WallSlideMaxSpeed);
            v.Value(nameof(WallJumpSpeedX), WallJumpSpeedX);
            v.Value(nameof(WallJumpSpeedY), WallJumpSpeedY);
            v.Value(nameof(WallJumpInputLockTicks), WallJumpInputLockTicks);
            v.Value(nameof(WallCoyoteTicks), WallCoyoteTicks);
            v.Value(nameof(DashSpeed), DashSpeed);
            v.Value(nameof(DashTicks), DashTicks);
            v.Value(nameof(DashEndSpeedRetention), DashEndSpeedRetention);
            v.Value(nameof(DashCharges), DashCharges);
            v.Value(nameof(DashCooldownTicks), DashCooldownTicks);
            v.Value(nameof(DashJumpCancelEnabled), DashJumpCancelEnabled);
            v.Value(nameof(DashJumpCancelMinTicks), DashJumpCancelMinTicks);
            v.Value(nameof(SubstepMaxDistance), SubstepMaxDistance);
            v.Value(nameof(GroundProbeDistance), GroundProbeDistance);
            v.Value(nameof(DropThroughTicks), DropThroughTicks);
            v.Value(nameof(BouncePadSpeed), BouncePadSpeed);
        }
    }

    /// <summary>경기 흐름 설정(06-match-and-tools.md 1절).</summary>
    [Serializable]
    public struct MatchParams
    {
        public int CountdownTicks;
        /// <summary>첫 라운드의 소개와 재조립 시간. 0이면 과거 CountdownTicks 규칙을 유지한다.</summary>
        public int IntroCountdownTicks;
        public readonly int CountdownForRound(int round) => round == 1 && IntroCountdownTicks > 0 ? IntroCountdownTicks : CountdownTicks;
        public int KoTicks;
        public int RoundsToWin;
        /// <summary>MatchEnd에 들어온 뒤 이만큼 지나야 버튼으로 새 경기를 시작한다(연타로 결과 화면을 넘기지 않게).</summary>
        public int MatchEndMinTicks;
        /// <summary>세트 승리에 필요한 라운드 승수(3라운드 중 2선승, D-049).</summary>
        public int RoundsToWinSet;
        /// <summary>경기 승리에 필요한 세트 수(D-052).</summary>
        public int SetsToWinMatch;
        /// <summary>한 선수의 경기당 선택 상한(D-054).</summary>
        public int MaxPicks;
        /// <summary>선택 단계(초기 폼·세트 패자 선택)에서 이만큼 고르지 않으면 시드 난수로 정한다(진행이 멈추지 않게).</summary>
        public int PickTimeoutTicks;
        /// <summary>선택이 끝난 뒤 다음 단계로 가기 전 잠깐 멈추는 틱(연출용, 결과는 표시하지 않음).</summary>
        public int PickHoldTicks;
        /// <summary>
        /// 온라인 재대결(CR-004 온라인 2단계): 켜면 MatchEnd에서 두 선수가 같은 틱에 공격 버튼을 누르고 있어야 새 경기를 시작한다
        /// (한 명만 눌러도 시작하던 규칙 대신). 상태를 따로 두지 않는다. 로컬 경기는 끔.
        /// </summary>
        public bool RematchBothHold;
        /// <summary>CR-005 카드 획득 시연. 몸 폼으로 시작하고 능력 없는 카드 3개만 제시한다.</summary>
        public bool CollectCardsOnly;

        public static MatchParams Default => new MatchParams
        {
            CountdownTicks = 90,
            KoTicks = 60,
            RoundsToWin = 5,
            MatchEndMinTicks = 60,
            RoundsToWinSet = 2,
            SetsToWinMatch = 5,
            MaxPicks = 4,
            PickTimeoutTicks = 900,
            PickHoldTicks = 20,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(CountdownTicks), CountdownTicks);
            v.Value(nameof(KoTicks), KoTicks);
            v.Value(nameof(RoundsToWin), RoundsToWin);
            v.Value(nameof(MatchEndMinTicks), MatchEndMinTicks);
            v.Value(nameof(RoundsToWinSet), RoundsToWinSet);
            v.Value(nameof(SetsToWinMatch), SetsToWinMatch);
            v.Value(nameof(MaxPicks), MaxPicks);
            v.Value(nameof(PickTimeoutTicks), PickTimeoutTicks);
            v.Value(nameof(PickHoldTicks), PickHoldTicks);
            // 켤 때만 넣는다: 끈 설정의 체크섬(기존 입력 기록·재생 기준)은 그대로.
            if (RematchBothHold)
                v.Value(nameof(RematchBothHold), true);
            if (CollectCardsOnly)
                v.Value(nameof(CollectCardsOnly), true);
            if (IntroCountdownTicks != 0)
                v.Value(nameof(IntroCountdownTicks), IntroCountdownTicks);
        }
    }

    /// <summary>
    /// 시뮬레이션 설정. 라운드 시작 시점에 튜닝 데이터에서 만들고 그 뒤로 바뀌지 않는다.
    /// </summary>
    public sealed class SimConfig
    {
        public const int TickRate = 60;
        public const float Dt = 1f / TickRate;

        public readonly MovementParams Movement;
        public readonly MatchParams Match;
        public readonly CombatParams Combat;
        /// <summary>시작 폼과 기능 스위치(CR-003, 10 문서 5.1).</summary>
        public readonly RuleSet Rules;
        /// <summary>증강 목록(CP23). 기본은 대표 증강 4개(AugmentCatalog), 테스트는 전용 증강을 더한 목록을 쓴다.</summary>
        public readonly AugmentDef[] Augments;
        public readonly float HalfBody;

        /// <summary>
        /// 기본 이동·경기 수치에 현재 몸 폼 전투(BrawlParams.Combat, 좌클릭 즉시 주먹)와 규칙(RuleSet.Brawl).
        /// 2026-11-11 폼 전투 제거 전에는 Test 폼(시험 공격, 가드 켬)이었다.
        /// </summary>
        public SimConfig(in MovementParams movement, in MatchParams match)
            : this(movement, match, BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl)
        {
        }

        public SimConfig(in MovementParams movement, in MatchParams match, in CombatParams combat, in RuleSet rules)
            : this(movement, match, combat, rules, null)
        {
        }

        public SimConfig(in MovementParams movement, in MatchParams match, in CombatParams combat, in RuleSet rules, AugmentDef[] augments)
        {
            Augments = augments ?? AugmentCatalog.CreateDefault();
            Movement = movement;
            Match = match;
            Combat = combat;
            Combat.TestAttack = Prepare(combat.TestAttack);
            Combat.Swing.Forward = Prepare(combat.Swing.Forward);
            Combat.Swing.Up = Prepare(combat.Swing.Up);
            Combat.Swing.Down = Prepare(combat.Swing.Down);
            if (combat.Body.Enabled)
                Combat.Body.Punch = Prepare(combat.Body.Punch);
            if (combat.Weapons.Enabled)
            {
                Combat.Weapons.Pan = Prepare(combat.Weapons.Pan);
                Combat.Weapons.Pistol = Prepare(combat.Weapons.Pistol);
            }
            Combat.Grab.NeutralThrowDirection = SimMath.Normalize(combat.Grab.NeutralThrowDirection);
            Rules = rules;
            HalfBody = movement.BodySize * 0.5f;
        }

        /// <summary>잡기 켜기/끄기. 꺼져 있으면 잡기 입력을 무시한다.</summary>
        public bool GrabEnabled => Rules.Switches.GrabEnabled;

        public static SimConfig CreateDefault() => new SimConfig(MovementParams.Default, MatchParams.Default);

        /// <summary>공격 번호(AttackIds) → 데이터.</summary>
        public ref readonly AttackDef GetAttack(int id)
        {
            switch (id)
            {
                case AttackIds.Punch: return ref Combat.Body.Punch;
                case AttackIds.Pan: return ref Combat.Weapons.Pan;
                case AttackIds.Pistol: return ref Combat.Weapons.Pistol;
                // 폼 시대의 시험 공격(TestAttack)은 설정 해시에만 남는 데이터다. 알 수 없는 번호는 주먹으로 본다.
                default: return ref Combat.Body.Punch;
            }
        }

        /// <summary>넉백 방향을 정규화하고 활성 틱을 1 이상으로 맞춘다. 판정 원 개수는 쓸 때 자른다(AttackDef.UsedCircles).</summary>
        static AttackDef Prepare(AttackDef attack)
        {
            attack.KnockbackDirection = SimMath.Normalize(attack.KnockbackDirection);
            attack.ActiveTicks = SimMath.Max(attack.ActiveTicks, 1);
            return attack;
        }

        /// <summary>입력 기록에 넣어 재생 시 같은 설정인지 확인하는 데 쓴다.</summary>
        public uint ComputeChecksum()
        {
            var h = Hasher.Create();
            Visit(ref h);
            return h.Hash;
        }

        public void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Begin(nameof(Movement));
            Movement.Visit(ref v);
            v.End();
            v.Begin(nameof(Match));
            Match.Visit(ref v);
            v.End();
            v.Begin(nameof(Combat));
            Combat.Visit(ref v);
            v.End();
            // 기록 호환용 고정값(2026-11-11 폼 전투 제거): 시작 폼 Body(4), P2 시작 폼 Body(4), 시작 보유 폼 0. 2단계(기록 v10)에서 제거.
            v.Value("StartForm", (int)FighterForm.Body);
            v.Value("StartFormP2", (int)FighterForm.Body);
            v.Value("OwnedForms", 0);
            v.Value("LegacyMatch", Rules.LegacyMatch);
            v.Value("AugmentCount", Augments.Length);
            for (int i = 0; i < Augments.Length; i++)
            {
                v.Begin("Augment");
                Augments[i].Visit(ref v);
                v.End();
            }
            v.Value("GuardEnabled", false); // 기록 호환용 고정값
            v.Value(nameof(GrabEnabled), GrabEnabled);
            v.Value("EnergyEnabled", false); // 기록 호환용 고정값
        }
    }
}
