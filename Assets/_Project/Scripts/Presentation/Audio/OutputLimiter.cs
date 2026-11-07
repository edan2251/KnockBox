using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// AudioListener와 같은 오브젝트에 붙어 최종 출력 전체에 <see cref="SfxMaster.Limit"/>를 건다(오디오 스레드, 할당 없음).
    /// 여러 효과음이 겹쳐도 출력이 잘리지 않는다.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class OutputLimiter : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = SfxMaster.Limit(data[i]);
        }
    }
}
