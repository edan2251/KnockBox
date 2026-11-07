using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 공격 에너지(10-forms-and-growth.md 4.4). 정수 단위라 회복과 소비가 결정적이다.
    /// 최대 180, 회복 틱당 1이면 바닥에서 가득 차는 데 180틱(3초). 공격이 끝나고 60틱(1초) 뒤부터 회복한다.
    /// 비용은 연속 3~4번에 바닥나게 고른다: 가위 50(3번 150), 바위 45(4번 180), 보 40(4번 160).
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

        /// <summary>폼의 기본공격 비용. 시험 공격은 0.</summary>
        public readonly int CostOf(FighterForm form)
        {
            switch (form)
            {
                case FighterForm.Scissors: return SimMath.Max(LungeCost, 0);
                case FighterForm.Rock: return SimMath.Max(ShotCost, 0);
                case FighterForm.Paper: return SimMath.Max(SwingCost, 0);
                default: return 0;
            }
        }

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
