using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 실제 경기의 좌클릭 충전·돌진 연출을 화면 전용 장면(카드 선택 등)에서 그대로 쓰기 위한 표시용 파이터 상태를 만든다.
    /// FighterView는 충전·돌진을 상태(Action·ActionTick·Velocity)만 보고 그린다(찌그러짐·떨림·충전 단계 외곽선·팔 자세·눈·돌진 잔상·위험색).
    /// 그래서 여기서는 경기 규칙과 같은 상태만 만들고 그림은 손대지 않는다. 충전 연출이 바뀌어도 이 장면은 자동으로 따라가고,
    /// 충전 상태 형식이 바뀌면 이 파일만 고친다. 시뮬레이션에는 쓰지 않는다.
    /// </summary>
    public static class FighterActionPose
    {
        /// <summary>
        /// 충전 진행도(0~1)의 좌클릭 충전 상태. 0은 돌진 가능한 최소 충전(경기에서 충전 연출이 보이기 시작하는 지점), 1은 최대 충전이다.
        /// </summary>
        public static FighterState Charge(in FighterState source, float progress, SimConfig config)
        {
            var f = source;
            ref readonly var body = ref config.Combat.Body;
            int ready = body.TapHoldTicks + body.MinLungeChargeTicks;
            int full = Mathf.Max(ready, body.TapHoldTicks + config.Combat.Charge.FullChargeTicks);
            f.Action = ActionState.Charge;
            f.ActionTick = Mathf.RoundToInt(Mathf.Lerp(ready, full, Mathf.Clamp01(progress)));
            f.Velocity = new Vec2(0, 0);
            f.Grounded = true;
            f.HitstopTicks = 0;
            return f;
        }

        /// <summary>돌진 중 상태: 속도 방향으로 팔을 뻗고, 빠를수록 잔상이 길고 위험색 외곽선이 된다.</summary>
        public static FighterState Lunge(in FighterState source, Vector2 velocity)
        {
            var f = source;
            f.Action = ActionState.Lunge;
            f.ActionTick = 1;
            f.Velocity = new Vec2(velocity.x, velocity.y);
            f.Grounded = false;
            f.HitstopTicks = 0;
            if (Mathf.Abs(velocity.x) > .01f) f.Facing = (sbyte)(velocity.x > 0 ? 1 : -1);
            return f;
        }

        /// <summary>돌진 발사 사건(경기와 같은 먼지·바람 소리·흔들림·몸 늘어남). MatchPresenter.PresentDisplayEvent로 낸다.</summary>
        public static SimEvent LungeStart(int fighter, in FighterState f, Vector2 direction, float strength) =>
            new SimEvent(0, SimEventType.LungeStart, fighter, f.Position, new Vec2(direction.x, direction.y), strength);
    }
}
