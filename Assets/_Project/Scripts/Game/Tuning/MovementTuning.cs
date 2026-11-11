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
        /// <summary>튜닝 에셋이 없으면 명세 초기값으로 만든다. 규칙은 현재 몸 폼(RuleSet.Brawl).</summary>
        public static SimConfig Create(MovementTuning tuning) => tuning != null
            ? new SimConfig(tuning.Movement, tuning.Match)
            : SimConfig.CreateDefault();

        public static SimConfig Create(MovementTuning movement, CombatTuning combat, in RuleSet rules) =>
            new SimConfig(
                movement != null ? movement.Movement : MovementParams.Default,
                movement != null ? movement.Match : MatchParams.Default,
                combat != null ? combat.Combat : CombatParams.Default,
                rules);
    }
}
