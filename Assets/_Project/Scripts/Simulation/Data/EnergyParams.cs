using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 폼 시대의 공격 에너지 수치(10-forms-and-growth.md 4.4). 2026-11-11 폼 전투 제거로 규칙은 없다.
    /// 설정 해시 호환을 위해 데이터로만 남고, Max는 파이터 상태의 고정 시작값이다(2단계에서 제거).
    /// </summary>
    [Serializable]
    public struct EnergyParams
    {
        public int Max;
        public int RegenPerTick;
        /// <summary>공격 행동이 끝난 뒤 회복을 시작하기까지의 틱.</summary>
        public int RegenDelayTicks;
        public int LungeCost;
        public int ShotCost;
        public int SwingCost;
        public int GrabCost;

        public static EnergyParams Default => new EnergyParams
        {
            Max = 180,
            RegenPerTick = 1,
            RegenDelayTicks = 60,
            LungeCost = 50,
            ShotCost = 45,
            SwingCost = 40,
            GrabCost = 40,
        };

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Max), Max);
            v.Value(nameof(RegenPerTick), RegenPerTick);
            v.Value(nameof(RegenDelayTicks), RegenDelayTicks);
            v.Value(nameof(LungeCost), LungeCost);
            v.Value(nameof(ShotCost), ShotCost);
            v.Value(nameof(SwingCost), SwingCost);
            v.Value(nameof(GrabCost), GrabCost);
        }
    }
}
