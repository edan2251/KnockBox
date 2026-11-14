using UnityEngine;
using Ease = Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 카드 선택 확정 연출(4단계)의 시간표와 상태 계산. 상태가 없는 순수 계산이며, 입력은 확정 뒤 경과초(확정 월드의 PhaseTick − PickedAt)다.
    /// 순서: 클릭 직후 고른 비석이 화면 흔들림·떨림과 함께 플레이어 색으로 두 번에 걸쳐 쫀득하게 꽉 차오르고 번쩍인다 → 패자가 경기의 좌클릭 충전 그대로 힘을 모은다(새총을 당기듯) →
    /// 새총처럼 실제 포물선으로 날아간다(발사 직후 가장 빠르고 솟을수록 느려짐) → 꼭대기에 닿아 착지 반동으로 바로 다시 뛰어오른다(쉬지 않음) →
    /// 순식간에 내리찍는다(쾅, 히트스톱) → 비석이 바닥 끝까지 순식간에 박혀 사라지고(슉, 크기 변화 없음) 패자는 바닥에 착지 →
    /// 구멍에서 솟은 카드 조각 점이 패자에게 흡수된다.
    /// 비석마다 거리와 높이가 달라 시간표를 고른 비석의 위치로 계산한다(멀수록 오래 날고, 화면 위끝까지 남은 높이만큼 다시 뛰어오른다).
    /// 충전·돌진 모습은 FighterActionPose가 경기 상태로 만들어 FighterView가 그린다(여기서는 시간과 위치만 정한다).
    /// 확정 대기(PickHoldTicks)에서 퇴장 시간을 뺀 만큼에 맞춘다(모자라면 전체를 줄인다). 시뮬레이션과 무관한 화면 계산이다.
    /// </summary>
    public sealed class CardPickConfirm
    {
        /// <summary>좌클릭 충전 시작과 끝(= 새총 발사).</summary>
        public const float ChargeStart = .1f, ChargeEnd = .55f;
        /// <summary>비행 시간: 기본 + 옆 거리에 비례, 최소·최대. 날아가는 처음 이 비율 동안만 돌진 모습(잔상)이다.</summary>
        public const float FlightBase = .42f, FlightPerUnit = .011f, FlightMin = .5f, FlightMax = .75f, LungeShare = .35f;
        /// <summary>꼭대기에 닿은 뒤 다시 뛰기까지(착지 반동, 거의 쉬지 않음). 클릭 뒤 고른 비석의 호버 빛이 빠지는 시간과 색이 차오르기 시작하는 때.</summary>
        public const float ReboundDelay = .05f, LitFadeSeconds = .1f;
        /// <summary>
        /// 클릭 반응(쫀득한 두 번 차오름): 0.02초부터 첫 박동으로 SurgeFill까지 빠르게 → 살짝 걸려 내려앉음 → 두 번째 박동으로 꽉 참 → 꽉 찬 순간 밝게 번쩍.
        /// </summary>
        public const float CoverStart = .02f, SurgeEnd = .12f, CatchEnd = .16f, FillEnd = .3f, SurgeFill = .55f, CatchDrop = .06f, FlashSeconds = .18f;
        /// <summary>클릭 반응 동안 비석이 좌우로 떠는 폭(유닛, 바닥은 그대로 두고 통째로 아주 살짝).</summary>
        public const float TrembleWidth = .07f;
        /// <summary>다시 뛰어오르는 높이(최대)와 화면 위끝 한계, 기준 높이에서 오르는 시간, 꼭대기 멈칫, 내리찍기 시간.</summary>
        public const float HopHeight = 2.4f, HopLimit = 8.9f, HopMinHeight = 1.4f, HopRiseSeconds = .18f, HangSeconds = .07f, SlamSeconds = .04f;
        public const float Hitstop = .08f, SinkSeconds = .1f;
        public const float AbsorbBurst = .07f, AbsorbFly = .16f, AbsorbStagger = .006f;
        /// <summary>흡수되는 점 개수(아이콘 색·플레이어 색 반씩).</summary>
        public const int DotCount = 12;
        /// <summary>비석이 다 박혀 화면에서 사라지도록 높이보다 이만큼 더 내려간다.</summary>
        public const float SinkBelow = 1.2f;
        const float ArcAbove = 2.5f, ArcLimit = 8f, HangLift = .12f;

        readonly Vector2 stand, top;
        readonly float floorY, height, scale, peak, hopApex;
        // 기준 시간표(초, 히트스톱 제외). 고른 비석의 거리·높이로 정한다.
        public readonly float TopLandAt, HopAt, HopApexAt, SlamAt, ImpactAt, SinkEnd, AbsorbEnd, Total;

        /// <param name="availableSeconds">확정 대기에서 퇴장을 뺀 시간.</param>
        /// <param name="stand">서 있던 발</param><param name="top">고른 비석 꼭대기(박히기 전)</param>
        /// <param name="floorY">바닥 높이</param><param name="height">비석 높이</param>
        public CardPickConfirm(float availableSeconds, Vector2 stand, Vector2 top, float floorY, float height)
        {
            this.stand = stand; this.top = top; this.floorY = floorY; this.height = height;
            peak = ArcPeak(stand.y, top.y);
            float hop = Mathf.Clamp(HopLimit - top.y, HopMinHeight, HopHeight);
            hopApex = top.y + hop;
            float flight = Mathf.Clamp(FlightBase + FlightPerUnit * Mathf.Abs(top.x - stand.x), FlightMin, FlightMax);
            TopLandAt = ChargeEnd + flight;
            HopAt = TopLandAt + ReboundDelay;
            // 중력이 같다면 오르는 시간은 높이의 제곱근에 비례한다.
            HopApexAt = HopAt + HopRiseSeconds * Mathf.Sqrt(hop / HopHeight);
            SlamAt = HopApexAt + HangSeconds;
            ImpactAt = SlamAt + SlamSeconds;
            SinkEnd = ImpactAt + SinkSeconds;
            AbsorbEnd = SinkEnd + (DotCount - 1) * AbsorbStagger + AbsorbBurst + AbsorbFly;
            Total = AbsorbEnd + Hitstop;
            scale = Mathf.Clamp(availableSeconds / Total, .05f, 1f);
        }
        public float Scale => scale;
        public float HopApex => hopApex;

        /// <summary>새총 발사·꼭대기 닿음·재도약·충돌(쾅)·바닥 착지(슉 끝)·흡수 끝 순간과 히트스톱 길이(실제 초).</summary>
        public float LaunchTime => ChargeEnd * scale;
        public float TopLandTime => TopLandAt * scale;
        public float HopTime => HopAt * scale;
        public float SlamTime => SlamAt * scale;
        public float ImpactTime => ImpactAt * scale;
        public float HitstopSeconds => Hitstop * scale;
        public float FloorTime => RealTime(SinkEnd * scale);
        public float AbsorbedTime => RealTime(AbsorbEnd * scale);

        /// <summary>히트스톱 동안 멈춘 시간축(기준 초). 충돌 뒤 HitstopSeconds 동안은 충돌 순간에 머문다.</summary>
        public float Base(float since)
        {
            float effective = since <= ImpactTime ? since : ImpactTime + Mathf.Max(0, since - ImpactTime - HitstopSeconds);
            return effective / scale;
        }
        /// <summary>기준 시간축의 시각(줄인 값)을 실제 경과초로 바꾼다(충돌 뒤는 히트스톱만큼 늦다).</summary>
        public float RealTime(float scaledEffective) => scaledEffective <= ImpactTime ? scaledEffective : scaledEffective + HitstopSeconds;

        /// <summary>포물선의 최고점 높이(화면 안으로 제한, 출발·도착보다는 조금이라도 높게).</summary>
        public static float ArcPeak(float startY, float topY) => Mathf.Max(Mathf.Min(Mathf.Max(startY, topY) + ArcAbove, ArcLimit), Mathf.Max(startY, topY) + .3f);

        /// <summary>고른 비석의 호버 빛: 클릭하는 순간 빠르게(가속 이징) 빠진다.</summary>
        public float ChosenLit(float since) => 1 - Ease.InQuad(Ease.Range(Base(since), 0, LitFadeSeconds));

        /// <summary>
        /// 고른 비석을 덮는 플레이어 색(0 → 1): 클릭 직후 첫 박동으로 절반 넘게 빠르게 차오르고(감속), 살짝 걸려 내려앉았다가,
        /// 두 번째 박동으로 0.3초 안에 꽉 찬다. 덮인 자리의 카드 내용은 가려진다.
        /// </summary>
        public float Cover(float since)
        {
            float b = Base(since);
            if (b < SurgeEnd) return SurgeFill * Ease.Out(Ease.Range(b, CoverStart, SurgeEnd));
            if (b < CatchEnd) return SurgeFill - CatchDrop * Ease.OutQuad(Ease.Range(b, SurgeEnd, CatchEnd));
            return Mathf.Lerp(SurgeFill - CatchDrop, 1, Ease.Out(Ease.Range(b, CatchEnd, FillEnd)));
        }

        /// <summary>꽉 찬 순간의 번쩍임(0~1, 빠르게 켜졌다 서서히 빠짐).</summary>
        public float FillFlash(float since)
        {
            float u = (Base(since) - FillEnd) / FlashSeconds;
            return u < 0 || u >= 1 ? 0 : Mathf.Min(1, u * 6) * (1 - Ease.OutQuad(u));
        }

        /// <summary>클릭 반응 동안 비석의 떨림(좌우 이동, 결정적). 클릭과 두 번째 박동에서 세게, 꽉 차면 멎는다.</summary>
        public float Tremble(float since)
        {
            float b = Base(since);
            if (b < 0 || b >= FillEnd + .06f) return 0;
            float first = Mathf.Exp(-b / .06f), second = b >= CatchEnd ? Mathf.Exp(-(b - CatchEnd) / .05f) * .8f : 0;
            return TrembleWidth * Mathf.Max(first, second) * (Mathf.Sin(b * 120f) * .7f + Mathf.Sin(b * 191f + 1.1f) * .3f);
        }

        /// <summary>두 번째 박동·꽉 참 순간(실제 초). 효과음·흔들림에 쓴다.</summary>
        public float SecondSurgeTime => CatchEnd * scale;
        public float FilledTime => FillEnd * scale;

        /// <summary>고른 비석의 세로 이동: 충돌 뒤 순식간에(가속 이징) 바닥 끝까지 박혀 사라진다. 크기는 바꾸지 않는다.</summary>
        public float Sink(float since)
        {
            float b = Base(since);
            if (b < ImpactAt) return 0;
            return -(height + SinkBelow) * Ease.InQuad(Ease.Range(b, ImpactAt, SinkEnd));
        }

        /// <summary>
        /// 포물선 비행. 시작 a에서 최고점 높이 peak를 지나 끝 b에 내려앉는다(비행 시간 duration). 옆으로는 일정한 속도, 위아래로는 일정한 중력.
        /// 최고점은 오르는 높이와 내려오는 높이의 비율로 정해진 시각(높이 차가 클수록 끝 쪽)에 지난다.
        /// </summary>
        public static void Ballistic(Vector2 a, Vector2 b, float peak, float duration, float t, out Vector2 position, out Vector2 velocity)
        {
            float rise = Mathf.Max(.01f, peak - a.y), fall = Mathf.Max(.01f, peak - b.y);
            float apexTime = duration * Mathf.Sqrt(rise) / (Mathf.Sqrt(rise) + Mathf.Sqrt(fall));
            float gravity = 2 * rise / (apexTime * apexTime);
            float vy = gravity * apexTime;
            float vx = (b.x - a.x) / duration;
            position = new Vector2(a.x + vx * t, a.y + vy * t - .5f * gravity * t * t);
            velocity = new Vector2(vx, vy - gravity * t);
        }

        /// <summary>패자의 발 위치·속도·충전 진행도(충전 중이 아니면 −1)·공중 여부·돌진 모습 여부.</summary>
        public void Fighter(float since, out Vector2 foot, out Vector2 velocity, out float charge, out bool airborne, out bool lunging)
        {
            float b = Base(since);
            velocity = Vector2.zero;
            charge = -1;
            airborne = false;
            lunging = false;
            foot = stand;
            if (b < ChargeStart) return;
            if (b < ChargeEnd)
            {
                // 좌클릭 충전(새총 당기기): 경기와 같이 진행도가 일정하게 쌓인다(충전 단계 외곽선·떨림은 FighterView가 그린다).
                charge = Ease.Range(b, ChargeStart, ChargeEnd);
                return;
            }
            if (b < TopLandAt)
            {
                // 새총 발사: 실제 포물선. 발사 직후가 가장 빠르고 솟을수록 느려지며, 처음 얼마 동안만 돌진 모습(잔상)이다.
                airborne = true;
                float duration = (TopLandAt - ChargeEnd) * scale, t = (b - ChargeEnd) * scale;
                Ballistic(stand, top, peak, duration, t, out foot, out velocity);
                lunging = t < duration * LungeShare;
                return;
            }
            if (b < HopAt)
            {
                // 꼭대기에 닿음: 착지 찌그러짐의 반동으로 바로 다시 뛴다.
                foot = top;
                return;
            }
            if (b < HopApexAt)
            {
                // 반동 재도약(감속 이징).
                airborne = true;
                float span = (HopApexAt - HopAt) * scale;
                float u = (b - HopAt) / (HopApexAt - HopAt);
                foot = new Vector2(top.x, Mathf.Lerp(top.y, hopApex, Ease.OutQuad(u)));
                velocity = new Vector2(0, (hopApex - top.y) * 2 * (1 - u) / span);
                return;
            }
            if (b < SlamAt)
            {
                // 꼭대기에서 멈칫하는 짧은 체공(이때 비석이 다 차오른다).
                airborne = true;
                float u = (b - HopApexAt) / (SlamAt - HopApexAt);
                foot = new Vector2(top.x, hopApex + HangLift * Mathf.Sin(u * Mathf.PI));
                return;
            }
            if (b < ImpactAt)
            {
                // 내리찍기: 순식간에(강한 가속 이징) 꽂힌다.
                airborne = true;
                float span = (ImpactAt - SlamAt) * scale;
                float u = (b - SlamAt) / (ImpactAt - SlamAt);
                foot = new Vector2(top.x, Mathf.Lerp(hopApex, top.y, u * u * u));
                velocity = new Vector2(0, -3 * (hopApex - top.y) * u * u / span);
                return;
            }
            // 쾅 → 슉: 박혀 들어가는 비석 위에 선 채 함께 내려가 바닥에 착지한다.
            foot = new Vector2(top.x, Mathf.Max(floorY, top.y + Sink(since)));
        }

        /// <summary>
        /// 흡수 점 하나. from은 비석이 박힌 구멍(바닥), to는 패자 몸 가운데. 착지 순간 구멍에서 위로 솟아 흩어졌다가(감속) 곡선을 그리며 빨려 든다(가속).
        /// 아직 출발 전이거나 이미 닿았으면 false.
        /// </summary>
        public bool Dot(int index, float since, Vector2 from, Vector2 to, out Vector2 position, out float size)
        {
            position = from;
            size = 0;
            float b = Base(since) - SinkEnd - index * AbsorbStagger;
            if (b < 0) return false;
            // 위쪽 부채꼴로 고르게 솟는 방향(황금각).
            float angle = Mathf.Lerp(25f, 155f, Mathf.Repeat(index * .618f, 1)) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float reach = 1.2f + 1.3f * Mathf.Repeat(index * .382f, 1);
            var burst = from + direction * reach;
            if (b < AbsorbBurst)
            {
                position = Vector2.LerpUnclamped(from, burst, Ease.OutQuad(b / AbsorbBurst));
                size = Mathf.Lerp(.14f, .32f, Ease.OutQuad(b / AbsorbBurst));
                return true;
            }
            float v = (b - AbsorbBurst) / AbsorbFly;
            if (v >= 1) return false;
            position = Ease.Arc(burst, to, Ease.InQuad(v), 1.2f);
            size = Mathf.Lerp(.32f, .14f, v);
            return true;
        }
    }
}
