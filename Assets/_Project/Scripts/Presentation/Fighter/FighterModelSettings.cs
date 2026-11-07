using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 3D 시험 외형 연결 설정(CR-004): 어떤 외형 프리팹(FighterModelRig가 붙은 것)을 몇 번 파이터에 쓸지.
    /// 권리·배포 허용이 확인되지 않은 시험 모델은 Git 밖 로컬 폴더(Assets/_Project/LocalOnly)에 두고, 에디터에서만 이 설정을 읽어 빌드에 들어가지 않게 한다.
    /// 다른 모델로 바꿀 때는 프리팹만 바꾼다.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Fighter Model Settings")]
    public sealed class FighterModelSettings : ScriptableObject
    {
        /// <summary>에디터가 이 경로의 설정을 찾는다(없으면 2D 외형 그대로).</summary>
        public const string LocalPath = "Assets/_Project/LocalOnly/FighterModelSettings.asset";

        [Tooltip("끄면 2D 외형으로 돌아간다")]
        public bool use = true;
        [Tooltip("외형 프리팹(루트에 FighterModelRig)")]
        public GameObject prefab;
        [Tooltip("적용할 파이터(0 = P1, 1 = P2)")]
        public int fighter;
    }
}
