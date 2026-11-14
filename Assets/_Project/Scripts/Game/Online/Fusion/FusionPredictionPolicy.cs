using System;
using System.Globalization;
using Fusion;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    /// <summary>모든 Fusion 접속에 쓰는 고정 정책. 60Hz와 SDK 입력 지연 3틱을 한 곳에서 관리한다.</summary>
    public static class FusionPredictionPolicy
    {
        public const int DelayTicks = 3;

        public static string ConnectionVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new FusionSetupException("Photon 게임 버전을 설정해주세요.");
            // hover1: 카드 선택 호버 RPC가 있는 빌드끼리만 접속한다(RPC 목록이 다르면 서로 맞지 않는다).
            // hold144: 카드 선택 확정 대기가 144틱인 설정끼리만 접속한다(설정 체크섬이 다르면 같은 경기를 진행할 수 없다).
            return $"{version.Trim()}-prediction1-snapshot{FusionWorldSnapshot.ByteCount}-tick60-delay{DelayTicks}-hover1-hold{CardCollectionRules.LongPickHoldTicks}";
        }

        public static NetworkProjectConfig CreateConfig(NetworkProjectConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // 공개 API로 접속 전용 사본을 만든다. SDK 공유 에셋과 런타임 프리팹 등록은 보존한다.
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(source));
            config.PrefabTable = source.PrefabTable;
            config.DoNotSetForecastDisabledProxyRigidbodiesToKinematic = source.DoNotSetForecastDisabledProxyRigidbodiesToKinematic;
            config.Simulation.TickRateSelection = new TickRate.Selection
            {
                Client = SimConfig.TickRate, ClientSendInterval = 1,
                ServerTickInterval = 1, ServerSendInterval = 1,
            };
            config.TimeSync.InputDelaySettings = TimeSyncConfiguration.Selection.Custom;
            config.TimeSync.MinimumInputDelay = DelayTicks;
            config.TimeSync.MinimumInputDelayUnit = TimeSyncConfiguration.Unit.Ticks;
            config.TimeSync.ClientsCanAddInputDelayToLimitResims = false;
            // SimWorld has coupled two-player/prop state. Always resimulate the whole object.
            config.ClientsOnlyRollbackUpdatedObjects = false;
#if UNITY_EDITOR
            // Allows the automated two-peer test in one editor; Player still uses the source peer mode.
            config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
#endif
            string condition = ReplayCheck.StringArg("-netsim");
            if (!string.IsNullOrEmpty(condition))
            {
                var parts = condition.Split(',');
                if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float loss)
                    || ms < 0 || ms > 1000 || loss < 0 || loss > 50)
                    throw new FusionSetupException("망 시험 인수는 편도ms,손실%(0~50) 형식입니다.");
                config.NetworkConditions.Enabled = true;
                config.NetworkConditions.DelayMin = config.NetworkConditions.DelayMax = ms / 1000f;
                config.NetworkConditions.AdditionalJitter = ms / 4000f;
                config.NetworkConditions.LossChanceMin = config.NetworkConditions.LossChanceMax = loss / 100f;
            }
            return config;
        }
    }
}
