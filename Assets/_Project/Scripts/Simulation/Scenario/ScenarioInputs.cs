namespace Knockbox.Simulation
{
    /// <summary>시나리오 입력 항목을 틱별 InputFrame 배열로 펼친다. 인덱스 t는 틱 t → t+1 Step에 쓰는 입력이다.</summary>
    public static class ScenarioInputs
    {
        public static InputFrame[] Build(ScenarioInput[] entries, int ticks)
        {
            var directions = new ScenarioDirections[ticks];
            var buttons = new InputButtons[ticks];
            foreach (var e in entries)
            {
                int first = SimMath.Max(e.FirstTick, 0);
                int last = SimMath.Min(e.LastTick, ticks - 1);
                for (int t = first; t <= last; t++)
                {
                    directions[t] |= e.Directions;
                    buttons[t] |= e.Buttons;
                }
            }

            var frames = new InputFrame[ticks];
            for (int t = 0; t < ticks; t++)
                frames[t] = ToFrame(directions[t], buttons[t]);
            return frames;
        }

        public static InputFrame ToFrame(ScenarioDirections d, InputButtons buttons)
        {
            int x = ((d & ScenarioDirections.Right) != 0 ? 1 : 0) - ((d & ScenarioDirections.Left) != 0 ? 1 : 0);
            int y = ((d & ScenarioDirections.Up) != 0 ? 1 : 0) - ((d & ScenarioDirections.Down) != 0 ? 1 : 0);
            return new InputFrame(x, y, buttons);
        }

        /// <summary>
        /// 시나리오의 시작 상태. 카운트다운을 건너뛰고 Fighting에서 시작한다.
        /// spawn은 파이터 중심 좌표이고, 없으면 맵의 시작 칸 위에 선다.
        /// </summary>
        public static SimState CreateInitialState(ScenarioDef def, SimConfig config, ArenaDef arena)
        {
            var spawn0 = def.HasSpawn ? def.Spawn0 : SimState.SpawnCenter(config, arena, 0);
            var spawn1 = def.HasSpawn ? def.Spawn1 : SimState.SpawnCenter(config, arena, 1);
            return SimState.CreateRound(config, arena, def.Seed, spawn0, spawn1, MatchPhase.Fighting);
        }
    }
}
