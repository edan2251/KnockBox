using System.Collections.Generic;
using System.Text;
using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>
    /// 폼 전환 지표(10-forms-and-growth.md 5.2, CP21). 시뮬레이션 밖에서 매 틱의 상태와 이벤트를 읽기만 한다.
    /// 기준값으로 판정하지 않고 기록·보고만 한다.
    /// - 한 판(라운드)당 전환 횟수
    /// - 자기 공격이 끝난 뒤 AfterAttackTicks 안에 한 전환의 비율
    /// - 상대의 선딜(충전·바위 선딜·휘두르기 선딜)이 시작된 뒤 전환까지의 틱(ReactionWindowTicks 안만) 분포
    /// - 두 폼을 오가는 진동: A → B → A가 OscillationTicks 안에 일어난 횟수
    /// </summary>
    public sealed class SwitchMetrics
    {
        public const int AfterAttackTicks = 30;
        public const int ReactionWindowTicks = 60;
        public const int OscillationTicks = 60;

        public int Switches { get; private set; }
        public int Rounds { get; private set; }
        public int SwitchesAfterAttack { get; private set; }
        public int Oscillations { get; private set; }
        public readonly List<int> ReactionTicks = new List<int>();

        readonly int[] attackEndTick = { int.MinValue, int.MinValue };
        readonly int[] startupTick = { int.MinValue, int.MinValue };
        readonly int[] lastSwitchTick = { int.MinValue, int.MinValue };
        readonly FighterForm[] lastSwitchFrom = new FighterForm[2];
        readonly FighterForm[] lastSwitchTo = new FighterForm[2];

        static bool Startup(ActionState a) => a == ActionState.Charge || a == ActionState.Shoot || a == ActionState.Attack;
        static bool Attacking(ActionState a) => FighterCombat.IsAttackAction(new FighterState { Action = a });

        /// <summary>Step 직후에 부른다. previous는 Step 전 상태.</summary>
        public void Observe(in SimState previous, in SimState current, SimEventBuffer events)
        {
            int tick = current.Tick;
            for (int f = 0; f < 2; f++)
            {
                var before = previous.GetFighter(f);
                var now = current.GetFighter(f);
                if (Attacking(before.Action) && !Attacking(now.Action))
                    attackEndTick[f] = tick;
                if (!Startup(before.Action) && Startup(now.Action))
                    startupTick[f] = tick;
            }
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Type == SimEventType.RoundEnd)
                    Rounds++;
                if (e.Type != SimEventType.FormSwitch || e.Fighter < 0)
                    continue;
                int f = e.Fighter;
                var from = (FighterForm)(int)e.Direction.X;
                var to = (FighterForm)(int)e.Strength;
                Switches++;
                if (tick - attackEndTick[f] <= AfterAttackTicks)
                    SwitchesAfterAttack++;
                int sinceOpponent = tick - startupTick[1 - f];
                if (sinceOpponent >= 0 && sinceOpponent <= ReactionWindowTicks)
                    ReactionTicks.Add(sinceOpponent);
                if (tick - lastSwitchTick[f] <= OscillationTicks && lastSwitchFrom[f] == to && lastSwitchTo[f] == from)
                    Oscillations++;
                lastSwitchTick[f] = tick;
                lastSwitchFrom[f] = from;
                lastSwitchTo[f] = to;
            }
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            float perRound = Rounds > 0 ? (float)Switches / Rounds : Switches;
            sb.Append($"전환 {Switches}회, 라운드 {Rounds}, 라운드당 {perRound:F1}회, ");
            sb.Append($"공격 뒤 {AfterAttackTicks}틱 안 전환 {(Switches > 0 ? (float)SwitchesAfterAttack / Switches : 0f):P0}, ");
            sb.Append($"진동(A→B→A {OscillationTicks}틱 안) {Oscillations}회, ");
            if (ReactionTicks.Count == 0)
            {
                sb.Append("상대 선딜 뒤 반응 없음");
                return sb.ToString();
            }
            var sorted = new List<int>(ReactionTicks);
            sorted.Sort();
            sb.Append($"상대 선딜 뒤 전환 {sorted.Count}회(틱: 최소 {sorted[0]}, 중앙 {sorted[sorted.Count / 2]}, 최대 {sorted[sorted.Count - 1]})");
            return sb.ToString();
        }
    }
}
