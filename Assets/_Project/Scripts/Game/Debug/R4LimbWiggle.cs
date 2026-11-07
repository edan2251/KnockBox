using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>CR-004 단계 S2: R4 2D 스키닝 확인용 팔다리의 두 위쪽 뼈를 천천히 흔들어 변형이 보이게 한다(외형 샘플 씬 전용).</summary>
    public sealed class R4LimbWiggle : MonoBehaviour
    {
        Transform b1, b2;
        Quaternion rest1, rest2;
        float time;

        void Start()
        {
            b1 = transform.Find(Knockbox.Presentation.SceneObjectName.Path("b0/b1"));
            b2 = b1 != null ? b1.Find(Knockbox.Presentation.SceneObjectName.Path("b2")) : null;
            if (b1 != null) rest1 = b1.localRotation;
            if (b2 != null) rest2 = b2.localRotation;
        }

        void Update()
        {
            if (b1 == null || b2 == null)
                return;
            time += Time.deltaTime;
            b1.localRotation = rest1 * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2f) * 35f);
            b2.localRotation = rest2 * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2f + 0.8f) * 45f);
        }
    }
}
