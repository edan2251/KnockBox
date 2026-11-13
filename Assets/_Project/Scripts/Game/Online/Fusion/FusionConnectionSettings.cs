using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>접속 정책만 보관한다. App ID는 SDK의 로컬 PhotonAppSettings에서 읽는다.</summary>
    [CreateAssetMenu(menuName = "KnockBox/Online/Fusion Connection Settings")]
    public sealed class FusionConnectionSettings : ScriptableObject
    {
        public const string ResourcePath = "FusionConnectionSettings";
        public const string DefaultRegion = "kr";
        public const string DefaultAppVersion = "knockbox-fusion-1";

        [Tooltip("코드 초대는 같은 지역끼리만 찾을 수 있습니다. 양쪽 빌드가 같은 값을 사용해야 합니다.")]
        public string Region = DefaultRegion;
        [Tooltip("서로 연결할 빌드는 같은 값을 사용합니다. 호환되지 않는 배포에서는 변경하세요.")]
        public string AppVersion = DefaultAppVersion;
    }
}
