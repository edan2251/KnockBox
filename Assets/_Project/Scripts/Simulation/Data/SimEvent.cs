namespace Knockbox.Simulation
{
    public enum SimEventType : byte
    {
        Jump,
        Land,
        WallSlideStart,
        WallJump,
        Dash,
        Hit,
        Clash,
        Parry,
        GuardHit,
        Grab,
        Throw,
        WallSplat,
        BounceRebound,
        KO,
        RoundStart,
        RoundEnd,
        RoundTransition,
        AirJump,
        PadBounce,
        /// <summary>경기장 밖으로 나가 되돌려짐. 벽으로 닫힌 경기장에서는 일어나면 안 되는 버그 신호.</summary>
        OutOfBounds,
        /// <summary>공격 시작(연출이 팔을 내지르는 시점). Strength는 공격 번호.</summary>
        AttackStart,
        /// <summary>패링 구간이 열림.</summary>
        ParryWindow,
        MatchEnd,
        /// <summary>모드 A 돌진 발사. Direction은 발사 방향, Strength는 속도.</summary>
        LungeStart,
        /// <summary>몸 충돌 판정에서 계산한 위력(F3 오버레이용). Fighter의 위력이 Strength.</summary>
        PowerCheck,
        /// <summary>잡기 시작(손을 뻗는 시점).</summary>
        GrabStart,
        /// <summary>잡기 풀기: 서로 잡아 반대로 튕김.</summary>
        GrabBreak,
        /// <summary>가시에 닿음. Strength는 데미지.</summary>
        Spike,
        /// <summary>경계 튕김(CR-002). Position은 닿은 점, Direction은 경계 안쪽 법선, Strength는 데미지(면역 중 0).</summary>
        BoundaryHit,
        /// <summary>구조물에 맞음(CP14). Fighter는 맞은 쪽, Direction은 넉백 방향, Strength는 데미지.</summary>
        PropHit,
        /// <summary>돌진이 구조물을 침(CP14). Fighter는 친 쪽, Direction은 구조물 쪽, Strength는 위력.</summary>
        PropStrike,
        /// <summary>구조물의 줄·축·경첩이 끊어짐(CP15). Fighter는 마지막 접촉자(없으면 NoFighter), Position은 연결점, Strength는 받은 충격.</summary>
        JointBreak,
        /// <summary>상자에 맞음(CP16). PropHit과 같은 뜻이지만 상자 피해 비중을 따로 재기 위해 나눈다. Strength는 데미지(상한 적용 뒤).</summary>
        BoxHit,
        /// <summary>돌진이 상자를 기둥에서 쳐냄(CP16 부분 붕괴). Fighter는 친 쪽, Position은 상자 중심, Direction은 친 방향, Strength는 위력.</summary>
        BoxKnockout,
        /// <summary>구조물이 지형·다른 구조물에 세게 부딪힘(CP16 연출용). Fighter는 NoFighter, Position은 접촉점, Direction은 법선, Strength는 다가온 속도. 몸마다 틱에 한 번.</summary>
        PropImpact,
        /// <summary>발사 틱에 에너지가 비용보다 적어 행동을 취소함(CR-003 4.4 규칙 4). Fighter는 취소한 쪽, Strength는 그때 에너지.</summary>
        EnergyShort,
        /// <summary>투사체 발사(CP19). Fighter는 소유자, Position은 생긴 곳, Direction은 진행 방향, Strength는 속력.</summary>
        ProjectileFire,
        /// <summary>투사체가 사라짐(CP19). Fighter는 소유자, Position은 사라진 곳. Strength는 이유(ProjectileEndReason).</summary>
        ProjectileEnd,
        /// <summary>근접 활성 판정이 투사체를 반사함(CP20). Fighter는 반사한 쪽(새 소유자), Position은 반사한 곳, Direction은 새 진행 방향, Strength는 반사 횟수.</summary>
        ProjectileReflect,
        /// <summary>돌진 중 투사체에 맞아 돌진이 끊기고 짧게 기절함(CP20). Fighter는 기절한 쪽, Strength는 기절 틱.</summary>
        LungeStun,
        /// <summary>근접공격이 위력 있는 돌진에 져서 중단됨(CP20). Fighter는 중단된 쪽, Strength는 돌진 위력.</summary>
        AttackInterrupted,
        /// <summary>폼 전환(CP21). Fighter는 전환한 쪽, Direction.X는 이전 폼 번호, Strength는 새 폼 번호.</summary>
        FormSwitch,
        /// <summary>세트 종료(세트 흐름). Fighter는 세트 승자, Strength는 끝난 세트 번호.</summary>
        SetEnd,
        /// <summary>선택 단계 시작. Fighter는 고르는 쪽(초기 폼 선택은 NoFighter), Strength는 선택지 수.</summary>
        PickStart,
        /// <summary>한 선수가 선택을 마침(비공개: 무엇을 골랐는지는 담지 않는다). Strength는 시간 초과로 정해졌으면 1.</summary>
        Picked,
        /// <summary>증강이 발동함(CP23). Fighter는 가진 쪽, Strength는 증강 번호. 연출이 이름 아이콘을 잠깐 띄운다.</summary>
        AugmentActive,
        /// <summary>받아내기(CR-004 5.2): 잡기가 박치기 중인 상대를 잡음. Fighter는 잡힌 쪽. Grab 이벤트도 함께 나온다.</summary>
        LungeCaught,
        /// <summary>잡기 해제(CR-004 몸 폼: 최대 유지 시간 동안 던지지 않음). Fighter는 풀려난 쪽.</summary>
        GrabRelease,
        /// <summary>표면에 붙음(CR-004 7절). Direction은 표면 바깥쪽 법선, Strength는 외곽이면 1.</summary>
        SurfaceAttach,
        BrawlWeaponWarning,
        BrawlWeaponLanded,
        BrawlWeaponPickup,
        BrawlWeaponDrop,
        /// <summary>Position은 실제 발사점, Direction은 발사점부터 충돌 끝점까지의 벡터, Strength는 남은 탄약.</summary>
        BrawlWeaponShot,
        BrawlWeaponEmpty,
        /// <summary>보유 무기 투척. Position은 출발점, Direction은 초기 속도, Strength는 종류.</summary>
        BrawlWeaponThrow,
        /// <summary>독립 연결 상자에 일반 접촉. PropIndex는 몸체, Strength는 접근 속도, Fighter는 접촉한 선수.</summary>
        ConnectorContact,
        /// <summary>독립 연결 상자 파괴. PropIndex는 몸체, Direction은 공격 방향, Strength는 충격 속도/펀치 위력.</summary>
        ConnectorBreak,
    }

    /// <summary>
    /// 연출이 소비하는 사건. 상태에는 포함하지 않는다.
    /// 롤백 대비로 (Tick, Type, Fighter)로 식별할 수 있다.
    /// 전투 이벤트의 Fighter는 맞은 쪽(Hit, GuardHit, WallSplat, BounceRebound, KO) 또는 패링한 쪽(Parry)이고,
    /// 클래시는 NoFighter다. Position은 접촉점, Direction은 넉백 방향 또는 지형 법선, Strength는 데미지나 속도.
    /// RoundEnd·MatchEnd의 Fighter는 이긴 쪽(무승부면 NoFighter).
    /// </summary>
    public struct SimEvent
    {
        public const sbyte NoFighter = -1;

        public int Tick;
        public SimEventType Type;
        public sbyte Fighter;
        public Vec2 Position;
        public Vec2 Direction;
        public float Strength;
        /// <summary>연출 대상 구조물 번호. -1은 없음. 경기 상태·기록 입력 형식에는 포함하지 않는다.</summary>
        public int PropIndex;

        public SimEvent(int tick, SimEventType type, int fighter, Vec2 position, Vec2 direction, float strength)
        {
            Tick = tick;
            Type = type;
            Fighter = (sbyte)fighter;
            Position = position;
            Direction = direction;
            Strength = strength;
            PropIndex = -1;
        }

        public override string ToString() => $"[{Tick}] {Type} P{Fighter + 1} {Position}";
    }

    /// <summary>고정 용량 이벤트 버퍼. 매 Step 시작 시 비워진다. 넘치면 버리고 개수를 센다.</summary>
    public sealed class SimEventBuffer
    {
        public const int DefaultCapacity = 64;

        readonly SimEvent[] items;

        public SimEventBuffer(int capacity = DefaultCapacity)
        {
            items = new SimEvent[capacity < 32 ? 32 : capacity];
        }

        public int Count { get; private set; }
        public int Capacity => items.Length;

        /// <summary>마지막 Clear 이후 용량 초과로 버린 개수.</summary>
        public int Dropped { get; private set; }

        public ref readonly SimEvent this[int index] => ref items[index];

        public void Clear()
        {
            Count = 0;
            Dropped = 0;
        }

        public void Add(in SimEvent e)
        {
            if (Count < items.Length)
                items[Count++] = e;
            else
                Dropped++;
        }

        /// <summary>start 이후에 쌓인 사건의 위치·방향을 중력 g 좌표계에서 월드로 돌린다(CR-004 E, SpiderMovement).</summary>
        public void RotateSince(int start, int g)
        {
            if (g == 0)
                return;
            for (int i = start; i < Count; i++)
            {
                items[i].Position = SpiderMovement.ToWorld(items[i].Position, g);
                items[i].Direction = SpiderMovement.ToWorld(items[i].Direction, g);
            }
        }
    }
}
