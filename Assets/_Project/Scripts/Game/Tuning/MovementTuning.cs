using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 이동 튜닝 데이터. 값은 시뮬레이션의 MovementParams를 그대로 직렬화한다.
    /// 플레이 중에 바꾼 값은 라운드 리셋(F5) 때 SimConfig로 다시 변환되어 반영된다.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Movement Tuning", fileName = "MovementTuning")]
    public sealed class MovementTuning : ScriptableObject
    {
        public MovementParams Movement = MovementParams.Default;
        public MatchParams Match = MatchParams.Default;

        void Reset()
        {
            Movement = MovementParams.Default;
            Match = MatchParams.Default;
        }
    }

    public static class SimConfigFactory
    {
        /// <summary>튜닝 에셋이 없으면 명세 초기값으로 만든다.</summary>
        public static SimConfig Create(MovementTuning tuning) => Create(tuning, null, CombatMode.Test);

        /// <summary>CP16까지의 규칙(전역 모드, 가드 켬, 에너지 끔, 모드 B 방향 휘두르기). 기존 테스트·레거시 시나리오용.</summary>
        public static SimConfig Create(MovementTuning movement, CombatTuning combat, CombatMode mode, bool grabEnabled = true) =>
            new SimConfig(
                movement != null ? movement.Movement : MovementParams.Default,
                movement != null ? movement.Match : MatchParams.Default,
                combat != null ? combat.Combat : CombatParams.Default,
                mode,
                grabEnabled);

        public static SimConfig Create(MovementTuning movement, CombatTuning combat, in RuleSet rules) =>
            new SimConfig(
                movement != null ? movement.Movement : MovementParams.Default,
                movement != null ? movement.Match : MatchParams.Default,
                combat != null ? combat.Combat : CombatParams.Default,
                rules);
    }
}
