using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 전투 튜닝 데이터(03-combat.md). 값은 시뮬레이션의 CombatParams를 그대로 직렬화한다.
    /// 플레이 중에 바꾼 값은 라운드 리셋(F5)이나 다음 라운드(RoundTransition) 때 반영된다.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Combat Tuning", fileName = "CombatTuning")]
    public sealed class CombatTuning : ScriptableObject
    {
        [Tooltip("F11 프리셋 순환과 화면 아래 표시에 쓰는 짧은 이름(영문)")]
        public string PresetName = "default";
        public CombatParams Combat = CombatParams.Default;

        [Header("현재 경기 상자 반응")]
        [InspectorName("점진적인 상자 밀림")]
        [Tooltip("강한 돌진의 밀어내기 보정과 상자 마찰 회복을 점진적으로 적용합니다. 과거 리플레이는 기록의 설정 해시로 구 동작을 선택합니다.")]
        public bool SmoothBoxRelease = true;
        [InspectorName("연결 상자 접촉 안정화")]
        [Tooltip("좁은 연결 상자 위 접촉과 하중 전달을 안정화합니다. 과거 기록은 기록 당시 설정을 사용합니다.")]
        public bool StableConnectorContacts = true;
        [InspectorName("강한 충격 후 최소 마찰 비율"), Range(0.02f, 1f)]
        [Tooltip("상자끼리의 원래 마찰에 곱하는 최소 비율. LooseTicks 동안 원래 마찰로 돌아갑니다. 바닥 마찰에는 적용하지 않습니다.")]
        public float ReleasedBoxFrictionScale = 0.15f;

        void Reset()
        {
            Combat = CombatParams.Default;
        }
    }

    /// <summary>
    /// 비교용 튜닝 프리셋(CHECKPOINTS.md CP9: 모드별 기본, 더 빠르게).
    /// "더 빠르게"는 모드 A(충전이 짧고 돌진이 빠름)와 모드 B(시작·후딜이 짧음)를 각각 빠르게 하고, 공통 경직을 조금 줄인다.
    /// </summary>
    public static class CombatPresets
    {
        public static CombatParams Fast
        {
            get
            {
                var c = CombatParams.Default;
                // 모드 A
                c.Charge.FullChargeTicks = 24;
                c.Charge.FullChargeHoldTicks = 18;
                c.Charge.LungeMinSpeed = 22f;
                c.Charge.LungeMaxSpeed = 38f;
                c.Charge.LungeMaxTicks = 24;
                c.Charge.ContactCooldownTicks = 8;
                // 모드 B
                c.Swing.Forward.StartupTicks = 4;
                c.Swing.Forward.ActiveTicks = 3;
                c.Swing.Forward.RecoveryTicks = 11;
                c.Swing.Up.StartupTicks = 4;
                c.Swing.Up.ActiveTicks = 3;
                c.Swing.Up.RecoveryTicks = 12;
                c.Swing.Down.StartupTicks = 5;
                c.Swing.Down.ActiveTicks = 4;
                c.Swing.Down.RecoveryTicks = 12;
                c.Swing.DashAttackCancelMinTicks = 3;
                // 공통
                c.RecoilTicks = 10;
                c.GuardStunTicks = 8;
                c.StaggerTicks = 24;
                c.Grab.WhiffRecoveryTicks = 18;
                return c;
            }
        }
    }
}
