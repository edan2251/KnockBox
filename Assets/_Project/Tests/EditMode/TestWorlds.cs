using Knockbox.Game;
using Knockbox.Simulation;

namespace Knockbox.Tests.EditMode
{
    /// <summary>테스트 공용: 기본 설정·Box 맵의 월드, 고정 시드 무작위 입력, 문자 격자 맵.</summary>
    public static class TestWorlds
    {
        static ArenaDef box;

        public static ArenaDef Box => box ??= ArenaFiles.LoadByName("Box");

        public static SimWorld Create(MatchPhase startPhase = MatchPhase.Fighting, uint seed = 1, SimConfig config = null, ArenaDef arena = null)
        {
            config ??= SimConfig.CreateDefault();
            arena ??= Box;
            return new SimWorld(config, arena, SimState.CreateRound(config, arena, seed, startPhase));
        }

        /// <summary>시작 위치(파이터 중심)를 직접 정한 Fighting 상태의 월드.</summary>
        public static SimWorld CreateAt(Vec2 center0, Vec2 center1, SimConfig config = null, ArenaDef arena = null)
        {
            config ??= SimConfig.CreateDefault();
            arena ??= Box;
            return new SimWorld(config, arena, SimState.CreateRound(config, arena, 1, center0, center1, MatchPhase.Fighting));
        }

        /// <summary>테스트 안에 둔 문자 격자(32 × 18) 맵. 첫 줄 앞의 빈 줄과 들여쓰기는 무시한다. 좌우 대칭이 아니어도 된다.</summary>
        public static ArenaDef Grid(string name, string grid)
        {
            var lines = grid.Replace("\r\n", "\n").Split('\n');
            var sb = new System.Text.StringBuilder();
            sb.Append("name: ").Append(name).Append('\n').Append(ArenaParser.Separator).Append('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                    sb.Append(trimmed).Append('\n');
            }
            return ArenaParser.Parse(sb.ToString(), requireSymmetry: false);
        }

        public static SimConfig Config(System.Func<MovementParams, MovementParams> change)
        {
            return new SimConfig(change(MovementParams.Default), MatchParams.Default);
        }

        public static void RandomInputs(uint seed, int ticks, out InputFrame[] p1, out InputFrame[] p2)
        {
            var g1 = new RandomInputGenerator(seed);
            var g2 = new RandomInputGenerator(seed ^ 0xA5A5A5A5u);
            p1 = new InputFrame[ticks];
            p2 = new InputFrame[ticks];
            for (int i = 0; i < ticks; i++)
            {
                p1[i] = g1.Next();
                p2[i] = g2.Next();
            }
        }
    }
}
