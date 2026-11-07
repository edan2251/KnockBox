using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 효과음 묶음(05-presentation.md 7절). 파일은 Tools/sfx_synth.py가 만든 WAV(Assets/_Project/Audio/Sfx).
    /// 필드 이름이 WAV 파일 이름이다(ProjectSetup.EnsureSfxLibrary가 이름으로 연결). 05 7절 목록 15개 전부.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Sfx Library", fileName = "SfxLibrary")]
    public sealed class SfxLibrary : ScriptableObject
    {
        public AudioClip jump;
        public AudioClip land;
        public AudioClip dash;
        public AudioClip hitWeak;
        public AudioClip hitStrong;
        public AudioClip clash;
        public AudioClip parry;
        public AudioClip guard;
        public AudioClip wallSplat;
        public AudioClip grab;
        public AudioClip @throw;
        public AudioClip spike;
        public AudioClip pad;
        public AudioClip countdown;
        public AudioClip ko;
        public AudioClip boundary;
        public AudioClip snap;

        public AudioClip Get(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Jump: return jump;
                case Sfx.Land: return land;
                case Sfx.Dash: return dash;
                case Sfx.HitWeak: return hitWeak;
                case Sfx.HitStrong: return hitStrong;
                case Sfx.Clash: return clash;
                case Sfx.Parry: return parry;
                case Sfx.Guard: return guard;
                case Sfx.WallSplat: return wallSplat;
                case Sfx.Grab: return grab;
                case Sfx.Throw: return @throw;
                case Sfx.Spike: return spike;
                case Sfx.Pad: return pad;
                case Sfx.Countdown: return countdown;
                case Sfx.KO: return ko;
                case Sfx.Boundary: return boundary;
                case Sfx.Snap: return snap;
                default: return null;
            }
        }
    }
}
