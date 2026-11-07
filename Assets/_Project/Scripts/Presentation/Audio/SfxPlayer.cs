using UnityEngine;

namespace Knockbox.Presentation
{
    public enum Sfx
    {
        Jump,
        Land,
        Dash,
        HitWeak,
        HitStrong,
        Clash,
        Parry,
        Guard,
        WallSplat,
        Grab,
        Throw,
        Spike,
        Pad,
        Countdown,
        KO,
        Boundary,
        Snap,
    }

    /// <summary>
    /// 효과음 재생: 음높이 ±5% 무작위, 같은 효과음이 한 프레임에 겹치면 하나만 재생.
    /// AudioSource를 미리 만들어 돌려 쓴다.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int Voices = 8;
        const float PitchRange = 0.05f;

        readonly AudioSource[] voices = new AudioSource[Voices];
        readonly int[] lastFrame = new int[24];
        SfxLibrary library;
        int nextVoice;
        FxRandom random = new FxRandom(0x5F3759DFu);

        public int PlayedThisFrame { get; private set; }

        /// <summary>실제로 재생한 효과음(클립, 전체 볼륨 여유를 곱한 볼륨, 최종 음높이). 봇 영상 녹화가 소리를 따로 섞을 때 쓴다.</summary>
        public event System.Action<AudioClip, float, float> Played;
        int countedFrame = -1;

        public void Setup(SfxLibrary sfxLibrary)
        {
            library = sfxLibrary;
            for (int i = 0; i < Voices; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
            for (int i = 0; i < lastFrame.Length; i++)
                lastFrame[i] = -1;
        }

        /// <summary>재생했으면 true, 같은 프레임 중복이라 건너뛰었으면 false. pitch는 무작위 ±5%에 곱한다(GO 같은 변형).</summary>
        public bool Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            int frame = Time.frameCount;
            if (countedFrame != frame)
            {
                countedFrame = frame;
                PlayedThisFrame = 0;
            }
            int id = (int)sfx;
            if (lastFrame[id] == frame)
                return false;
            lastFrame[id] = frame;

            var clip = library != null ? library.Get(sfx) : null;
            if (clip == null)
                return false;
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % Voices;
            voice.pitch = pitch * (1f + random.Range(-PitchRange, PitchRange));
            float scaled = volume * SfxMaster.Gain;
            voice.PlayOneShot(clip, scaled);
            Played?.Invoke(clip, scaled, voice.pitch);
            PlayedThisFrame++;
            return true;
        }
    }
}
