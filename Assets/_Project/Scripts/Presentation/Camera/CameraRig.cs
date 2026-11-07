using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 카메라 프레이밍과 화면 흔들림(05-presentation.md 1·4절).
    /// 프레이밍: 직교 크기 9(세로 18)로 32 × 18 경기장을 정확히 담고, 16:9보다 좁은 화면은 가로가 다 보이게 크기를 키운다.
    /// 남는 부분은 카메라 배경색(bgBase)으로 레터박스가 된다.
    /// 흔들림: 트라우마 방식. 오프셋 = 최대 진폭 0.35 × 트라우마² × 노이즈, 트라우마는 시간에 따라 줄어든다.
    /// 흔들림 줄이기(F7)를 켜면 30%로 줄인다.
    /// 확대(Punch): 강한 명중·벽꽝 순간 직교 크기를 잠깐 줄여(예: 1.5%, 6프레임) 화면을 당긴다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>좌우 테두리 벽(1유닛)은 이만큼만 보여도 된다(08 4절: 테두리는 일부가 화면 밖으로 나가도 된다).</summary>
        public const float SideWallShown = 0.5f;
        public const float MaxShake = 0.35f;
        public const float ReducedShakeScale = 0.3f;
        const float TraumaDecay = 1.6f;
        const float NoiseFrequency = 22f;

        Camera cam;
        Vector3 basePosition;
        float trauma;
        float noiseTime;
        float zoomAmount;
        float zoomLeft;
        float lightPosition, lightVelocity;
        float lightAge = LightDuration;
        const float LightImpulse = 350f;
        const float LightReturnRate = 10f;
        const float LightMaxVelocity = 140f;
        const float LightFadeStart = .2f;
        public const float LightDuration = .55f;
        const float LightArcLength = 14f;

        /// <summary>흔들림 줄이기(F7). 켜면 흔들림 30%, 화면 전체 번쩍임과 색 반전 끔(CP4).</summary>
        public bool ReduceMotion { get; set; }

        /// <summary>화면에 맞추는 경기장 반폭·반높이. 맵이 바뀌면 FitArena로 바꾼다.</summary>
        public float ArenaHalfWidth { get; private set; } = ArenaDef.ClassicColumns * 0.5f;
        public float ArenaHalfHeight { get; private set; } = ArenaDef.ClassicRows * 0.5f;

        public void FitArena(ArenaDef arena)
        {
            ArenaHalfWidth = arena.HalfWidth;
            ArenaHalfHeight = arena.HalfHeight;
            if (cam != null)
                Frame();
        }
        public float Trauma => trauma;
        public Vector2 ShakeOffset { get; private set; }
        /// <summary>타격에 한 번 밀렸다 바로 복귀하는 광원. 판정 위치나 카메라 구도에는 쓰지 않는다.</summary>
        public Vector2 LightOffset { get; private set; }
        public const float MaxLightSwing = 7f;
        public static float DamageTrauma(float damage) => Mathf.Clamp01(.2f + Mathf.Max(0, damage) * .016f);

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (cam != null) return;
            cam = GetComponent<Camera>();
            basePosition = transform.localPosition;
        }

        public void AddTrauma(float amount, float horizontalDirection = 0)
        {
            trauma = Mathf.Clamp01(trauma + amount);
            float impact = Mathf.Clamp01(amount);
            if (impact <= 0) return;
            // 새 타격도 현재 위치와 속도부터 이어 간다. 종료 페이드 중 재타격 시 원래 진폭으로 튀지 않게 한다.
            LightEnvelope(lightAge, out float envelope, out float slope);
            lightVelocity = lightVelocity * envelope + lightPosition * slope;
            lightPosition *= envelope;
            float direction = Mathf.Abs(horizontalDirection) > .01f ? Mathf.Sign(horizontalDirection)
                : Mathf.Abs(lightVelocity) > .01f ? Mathf.Sign(lightVelocity) : 1f;
            lightVelocity = Mathf.Clamp(lightVelocity + direction * LightImpulse * impact * impact, -LightMaxVelocity, LightMaxVelocity);
            lightAge = 0;
        }

        /// <summary>duration초 동안 화면을 amount 비율만큼 확대한다. 겹치면 큰 쪽.</summary>
        public void Punch(float amount, float duration)
        {
            zoomAmount = zoomLeft > 0f ? Mathf.Max(zoomAmount, amount) : amount;
            zoomLeft = Mathf.Max(zoomLeft, duration);
        }

        public float Zoom => zoomLeft > 0f ? zoomAmount : 0f;

        /// <summary>흔들림과 확대를 멈춘다(라운드·시나리오 시작).</summary>
        public void ResetEffects()
        {
            // MatchPresenter.Awake가 이 컴포넌트의 Awake보다 먼저 시작될 수 있다.
            EnsureInitialized();
            trauma = 0f;
            zoomLeft = 0f;
            // 흔들림 노이즈도 처음부터: 캡처가 앞서 재생한 시나리오에 따라 달라지지 않게.
            noiseTime = 0f;
            lightPosition = lightVelocity = 0;
            lightAge = LightDuration;
            ShakeOffset = LightOffset = Vector2.zero;
            transform.localPosition = basePosition;
        }

        /// <summary>연출 시간으로 진행한다(멈추면 흔들림도 멈춘다).</summary>
        public void Tick(float dt)
        {
            EnsureInitialized();
            Frame();
            zoomLeft = Mathf.Max(0f, zoomLeft - dt);
            noiseTime += dt;
            trauma = Mathf.Max(0f, trauma - TraumaDecay * dt);
            float amount = MaxShake * trauma * trauma * (ReduceMotion ? ReducedShakeScale : 1f);
            float nx = Mathf.PerlinNoise(noiseTime * NoiseFrequency, 0.37f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0.71f, noiseTime * NoiseFrequency) * 2f - 1f;
            ShakeOffset = new Vector2(nx, ny) * amount;
            transform.localPosition = basePosition + (Vector3)ShakeOffset;
            TickLight(dt);
        }

        void TickLight(float dt)
        {
            if (dt > 0 && lightAge < LightDuration)
            {
                float h = Mathf.Min(dt, LightDuration - lightAge);
                // 임계 감쇠: 한쪽으로 밀렸다 돌아오며 반대편으로 반복 왕복하지 않는다.
                float c = lightVelocity + LightReturnRate * lightPosition;
                float decay = Mathf.Exp(-LightReturnRate * h);
                lightPosition = (lightPosition + c * h) * decay;
                lightVelocity = (lightVelocity - LightReturnRate * c * h) * decay;
                lightAge += h;
                if (lightAge >= LightDuration) lightPosition = lightVelocity = 0;
            }
            LightEnvelope(lightAge, out float envelope, out _);
            float x = SoftLimit(lightPosition * envelope, MaxLightSwing);
            float rise = x * x / (2 * LightArcLength);
            LightOffset = new Vector2(x, SoftLimit(rise, 1.5f)) * (ReduceMotion ? ReducedShakeScale : 1f);
        }

        static float SoftLimit(float value, float limit) => limit * value / Mathf.Sqrt(limit * limit + value * value);

        static void LightEnvelope(float age, out float value, out float slope)
        {
            float u = Mathf.Clamp01((age - LightFadeStart) / (LightDuration - LightFadeStart));
            value = 1 - u * u * (3 - 2 * u);
            slope = -6 * u * (1 - u) / (LightDuration - LightFadeStart);
        }

        void Frame()
        {
            float aspect = cam.aspect;
            // 세로는 경기장 전체, 가로는 좌우 벽이 SideWallShown만큼 보이도록. 32 × 18은 16:9에 꼭 맞는다.
            float sizeForWidth = (ArenaHalfWidth - 1f + SideWallShown) / Mathf.Max(aspect, 0.01f);
            cam.orthographicSize = Mathf.Max(ArenaHalfHeight, sizeForWidth) * (1f - Zoom);
        }
    }
}
