using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 후처리(08-visual-direction.md 5절). Main에 배치한 전역 Volume: 색수차 펄스(평소 0, KO에 강하게, 강한 명중·벽꽝에 약하게),
    /// 약한 비네트. 흔들림 줄이기(F7)를 켜면 색수차를 끈다. 연출 전용이고 시뮬레이션과 무관하다.
    /// 블룸은 URP Bloom 대신 연출별 글로(ImpactFx.Glow, 파이터 몸 글로)로 낸다: URP Bloom은 문턱값의 절반부터 번지기 시작해서
    /// (soft knee) 가장 밝은 지형(사이언 #0AB2E6)까지 번지고, 아주 어두운 배경에서는 그 번짐이 바닥·벽 주변 배경을 눈에 띄게 밝혔다
    /// (빈 경기장 사이언 테마 아래쪽 밝기 58% → 72%). 지형은 번지지 않아야 한다(08 5절).
    /// </summary>
    public sealed class PostFx : MonoBehaviour
    {
        public const float VignetteIntensity = 0.2f;

        [SerializeField] Volume volume;
        [SerializeField] VolumeProfile baseProfile;
        VolumeProfile profile;
        ChromaticAberration aberration;
        float pulseStrength;
        float pulseDuration;
        float pulseAge;

        public bool ReduceMotion { get; set; }

        /// <summary>후처리 전체를 켜고 끈다(성능 비교·진단용).</summary>
        public bool VolumeEnabled
        {
            get => volume != null && volume.enabled;
            set
            {
                if (volume != null)
                    volume.enabled = value;
            }
        }

        /// <summary>지금 색수차 세기(테스트·캡처 확인용).</summary>
        public float Aberration => aberration != null ? aberration.intensity.value : 0f;

        public void Setup(Camera camera)
        {
            ReleaseProfile();
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            // VolumeProfile's components are separate ScriptableObjects. A shallow clone
            // would let a runtime pulse modify the shared, Inspector-editable asset.
            if (baseProfile != null)
                foreach (var component in baseProfile.components)
                {
                    var copy = Instantiate(component);
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    profile.components.Add(copy);
                }
            // 프로젝트 기본 볼륨의 블룸이 켜져 있어도 쓰지 않는다(위 설명).
            if(!profile.TryGet<Bloom>(out var bloom)) bloom=profile.Add<Bloom>(true);
            bloom.intensity.Override(0f);
            if(!profile.TryGet<ChromaticAberration>(out aberration)) aberration=profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0f);
            if(!profile.TryGet<Vignette>(out var vignette)) vignette=profile.Add<Vignette>(true);
            if(baseProfile==null){vignette.intensity.Override(VignetteIntensity);vignette.smoothness.Override(0.45f);}
            if(volume==null)volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            if (camera != null)
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        /// <summary>색수차를 strength에서 duration초 동안 0으로. 더 약한 펄스는 진행 중인 강한 펄스를 덮지 않는다.</summary>
        public void Pulse(float strength, float duration)
        {
            if (strength < Current())
                return;
            pulseStrength = strength;
            pulseDuration = duration;
            pulseAge = 0f;
        }

        float Current() => pulseDuration > 0f ? pulseStrength * Mathf.Clamp01(1f - pulseAge / pulseDuration) : 0f;

        public void Tick(float dt)
        {
            if (aberration == null)
                return;
            pulseAge += dt;
            aberration.intensity.Override(ReduceMotion ? 0f : Current());
        }

        public void ResetEffects()
        {
            pulseStrength = 0f;
            pulseDuration = 0f;
            pulseAge = 0f;
            Tick(0f);
        }

        void ReleaseProfile()
        {
            if (profile == null) return;
            if (volume != null && volume.sharedProfile == profile) volume.sharedProfile = baseProfile;
            foreach (var component in profile.components)
                if (Application.isPlaying) Destroy(component); else DestroyImmediate(component);
            profile.components.Clear();
            if (Application.isPlaying) Destroy(profile); else DestroyImmediate(profile);
            profile = null;
        }
        void OnDestroy() => ReleaseProfile();
    }
}
