using System.Collections.Generic;

namespace Knockbox.Simulation
{
    public sealed class ScenarioResult
    {
        public string Name;
        public int TicksRun;
        public int ExpectCount;
        public uint FinalChecksum;
        public readonly List<string> Failures = new List<string>();
        public bool Passed => Failures.Count == 0;
    }

    /// <summary>시나리오를 화면 없이 끝까지 진행하며 expect를 평가한다. 자동 테스트가 쓴다.</summary>
    public static class ScenarioRun
    {
        public static ScenarioResult Run(ScenarioDef def, SimConfig config, ArenaDef arena)
        {
            var result = new ScenarioResult { Name = def.Name, ExpectCount = def.Expects.Length };
            var p1 = ScenarioInputs.Build(def.P1, def.Ticks);
            var p2 = ScenarioInputs.Build(def.P2, def.Ticks);
            var world = new SimWorld(config, arena, ScenarioInputs.CreateInitialState(def, config, arena));

            foreach (var e in def.Expects)
            {
                if (e.Tick < 0 || e.Tick > def.Ticks)
                    result.Failures.Add($"tick {e.Tick} P{e.Fighter + 1} {e.Field}: 시나리오 길이({def.Ticks}) 밖");
            }

            EvaluateAt(def, world.State, result);
            for (int t = 0; t < def.Ticks; t++)
            {
                world.Step(p1[t], p2[t]);
                EvaluateAt(def, world.State, result);
            }

            result.TicksRun = world.State.Tick;
            result.FinalChecksum = world.ComputeChecksum();
            return result;
        }

        static void EvaluateAt(ScenarioDef def, in SimState state, ScenarioResult result)
        {
            foreach (var e in def.Expects)
            {
                if (e.Tick != state.Tick)
                    continue;
                string failure = ExpectEvaluator.Evaluate(state, e);
                if (failure != null)
                    result.Failures.Add(failure);
            }
        }
    }
}
