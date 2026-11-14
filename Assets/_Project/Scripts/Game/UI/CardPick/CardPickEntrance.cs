using UnityEngine;
using Ease = Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 비석 하나의 진입 상태. 무거운 돌이라 크기는 바꾸지 않고(바닥 고정) 세로 이동·떨림만 쓴다.
    /// energy는 플레이어 색이 덮은 높이 비율(0~1), contentFront는 카드 내용이 드러나는 경계(로컬 높이, 이 위만 보임).
    /// </summary>
    public struct MonolithPose
    {
        public float offsetY, jitterX, energy, contentFront;
    }

    /// <summary>
    /// 카드 선택 진입 연출(솟아오름)의 시간표와 상태 계산. 상태가 없는 순수 계산이며, 입력은 선택 단계 시작부터의 경과초(확정 월드 PhaseTick 기반)다.
    /// 순서: (직전 KO 퇴장에서 조명이 서서히 꺼진 채) 바닥 재구성 → 패자가 위에서 떨어져 착지 → 큰 비석부터 차례로 땅속 울림(끝만 내밀고 떨림) →
    /// 플레이어 색으로 덮인 채 땅을 가르며 솟음(처음에 세게, 점점 느려짐, 덜덜 떨림) → 쾅 멈춤(살짝 주저앉음) → 바로 위에서 아래로 색이 걷히며 어두운 카드 내용이 드러남 →
    /// 마지막 비석이 멈춘 뒤 조명이 팍 켜지고(펄스) 선택 중 표시가 튀어나오며 조작 가능.
    /// 다른 진입(예: 하늘에서 내리꽂기)으로 바꿀 때는 이 클래스만 바꾼다. 시뮬레이션과 무관한 화면 계산이다.
    /// </summary>
    public sealed class CardPickEntrance
    {
        /// <summary>바닥이 재구성 효과로 자리 잡는 시간(초).</summary>
        public const float FloorSeconds = .5f;
        /// <summary>패자가 떨어지기 시작하는 높이(유닛, 발 기준). 화면 위끝 밖에서 들어온다.</summary>
        public const float DropHeight = 21f;
        /// <summary>솟는 동안 갈리는 박자(초). 박자마다 먼지·돌 부스러기·낮은 소리·작은 흔들림을 낸다.</summary>
        public const float GrindBeat = .075f;
        const float PeekHeight = .35f, SinkDepth = .1f, SinkSeconds = .16f, BannerSeconds = .34f;

        readonly Maps.RewardStageSettings s;
        public CardPickEntrance(Maps.RewardStageSettings settings) => s = settings;

        public float RumbleAt(int order) => s.riseAt + order * s.riseStagger;
        public float EmergeAt(int order) => RumbleAt(order) + s.rumbleSeconds;
        /// <summary>다 솟아 멈추는(쾅) 순간. 이때부터 플레이어 색이 걷힌다.</summary>
        public float LandAt(int order) => EmergeAt(order) + s.riseSeconds;
        public float RevealedAt(int order) => LandAt(order) + s.energyRecedeSeconds;
        public float LoserLandAt => s.loserDropAt + s.loserDropSeconds;
        /// <summary>조명이 팍 켜지는 순간: 마지막 비석이 멈춘 뒤.</summary>
        public float LightAt(int count) => LandAt(Last(count)) + s.lightOnDelay;
        /// <summary>호버·클릭을 받기 시작하는 순간(조명이 켜질 때).</summary>
        public float SettledAt(int count) => LightAt(count);
        /// <summary>선택 중 표시가 튀어나오는 순간(조명이 켜질 때).</summary>
        public float BannerAt(int count) => LightAt(count);
        /// <summary>모든 움직임이 끝나는 순간.</summary>
        public float FinishedAt(int count) => Mathf.Max(RevealedAt(Last(count)), BannerAt(count) + BannerSeconds);
        /// <summary>솟는 동안의 갈리는 박자 수와 박자 시각.</summary>
        public int GrindBeats => Mathf.Max(1, Mathf.FloorToInt(s.riseSeconds / GrindBeat));
        public float GrindAt(int order, int beat) => EmergeAt(order) + beat * GrindBeat;
        static int Last(int count) => Mathf.Max(0, count - 1);

        public float FloorProgress(float t) => Ease.Range(t, 0, FloorSeconds);

        /// <summary>
        /// 조명 밝기(1 켜짐 ~ 0 꺼짐). 직전 KO 퇴장에서 서서히 꺼진 채로 시작하고, 마지막 비석이 멈춘 뒤 한 번에 켜진다.
        /// 켜진 뒤 밝기 펄스용 경과초도 준다(-1이면 펄스 없음).
        /// </summary>
        public float Light(float t, int count, out float pulseElapsed)
        {
            float on = LightAt(count);
            pulseElapsed = t >= on ? t - on : -1;
            // 직전 KO 퇴장에서 이미 서서히 꺼졌으므로 선택 맵은 어두운 채 나타난다.
            return t >= on ? 1 : 0;
        }

        /// <summary>퇴장 중 조명: 배경이 아직 보일 때 먼저 서서히 꺼진다(다음 맵이 켜진 채 나타나지 않게).</summary>
        public static float ExitLight(float exit) => 1 - Ease.Smooth(Ease.Range(exit, 0, .55f));

        /// <summary>패자: 떨어지기 전 숨김. 떨어지는 동안 발 높이와 낙하 속도(늘어남 계산용)를 준다.</summary>
        public bool Loser(float t, out float height, out float velocityY)
        {
            float u = Ease.Range(t, s.loserDropAt, LoserLandAt);
            // 중력처럼 점점 빨라진다(가속 이징). 속도는 그 미분.
            height = DropHeight * (1 - Ease.InQuad(u));
            velocityY = u > 0 && u < 1 ? -2 * DropHeight * u / Mathf.Max(.01f, s.loserDropSeconds) : 0;
            return t >= s.loserDropAt;
        }

        /// <summary>
        /// 비석 상태. order는 솟는 차례(0이 먼저), height는 비석 높이. 크기는 바꾸지 않는다.
        /// 플레이어 색으로 덮인 채 울림(끝만 내밀고 떨림) → 솟음(감속 이징 + 덜덜 떨림 + 갈리는 턱) → 쾅 멈춤(살짝 주저앉았다 자리 잡음) →
        /// 위에서 아래로 색이 걷히며(가감속) 걷힌 자리부터 카드 내용이 드러남.
        /// </summary>
        public MonolithPose Monolith(float t, int order, float height)
        {
            var pose = new MonolithPose { energy = 1, contentFront = float.PositiveInfinity };
            float rumble = RumbleAt(order), emerge = EmergeAt(order), land = LandAt(order);
            float hidden = -(height + 4f), peek = -height + PeekHeight;
            if (t < rumble) { pose.offsetY = hidden; return pose; }
            if (t < emerge)
            {
                float u = Ease.Range(t, rumble, emerge);
                pose.offsetY = Mathf.LerpUnclamped(hidden, peek, Ease.OutQuad(u));
                pose.jitterX = Shake(t, 1.2f) * s.grindJitter;
                return pose;
            }
            if (t < land)
            {
                float u = Ease.Range(t, emerge, land);
                // 처음에 세게 밀고 올라와(콰) 점점 느려지며 갈리다(가가가) 멈춘다. 박자마다 잠깐 걸리는 턱을 준다.
                float eased = 1 - Mathf.Pow(1 - u, 2.6f);
                float beat = Mathf.Repeat((t - emerge) / GrindBeat, 1);
                float catchStep = beat < .35f ? -(1 - u) * .07f : 0;
                pose.offsetY = Mathf.LerpUnclamped(peek, 0, eased) + catchStep;
                pose.jitterX = Shake(t, 1 - u * .5f) * s.grindJitter;
                return pose;
            }
            float since = t - land;
            // 쾅: 무게로 살짝 주저앉았다가 자리 잡는다(크기 변화 없음).
            if (since < SinkSeconds)
            {
                float k = since / SinkSeconds;
                pose.offsetY = -SinkDepth * Mathf.Sin(k * Mathf.PI) * (1 - k);
            }
            pose.jitterX = since < .12f ? Shake(t, 1 - since / .12f) * s.grindJitter * .6f : 0;
            float revealed = RevealedAt(order);
            if (t < revealed)
            {
                pose.energy = 1 - Ease.Smooth(Ease.Range(t, land, revealed));
                pose.contentFront = pose.energy * height;
            }
            else
            {
                pose.energy = 0;
                pose.contentFront = float.NegativeInfinity;
            }
            return pose;
        }

        /// <summary>돌이 갈리는 떨림: 두 주파수를 섞은 결정적 흔들림(-1~1).</summary>
        static float Shake(float t, float amount) => amount * (Mathf.Sin(t * 97f) * .6f + Mathf.Sin(t * 151f + 1.3f) * .4f);

        /// <summary>선택 중 표시의 크기: 0에서 오버슈트하며 튀어나오고, 세로로 살짝 늘었다 돌아온다.</summary>
        public Vector2 Banner(float t, int count)
        {
            float u = Ease.Range(t, BannerAt(count), BannerAt(count) + BannerSeconds);
            if (u <= 0) return Vector2.zero;
            float size = Ease.OutBack(u, 2.4f);
            float stretch = .18f * (1 - u) * (1 - u);
            return new Vector2(size * (1 - stretch * .5f), size * (1 + stretch));
        }
    }
}
