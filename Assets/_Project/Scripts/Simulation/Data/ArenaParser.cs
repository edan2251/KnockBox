using System;
using System.Collections.Generic;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 맵 텍스트(04-arena.md 2절) → ArenaDef. 같은 종류의 인접 칸을 가로로 먼저 합치고,
    /// 같은 가로 구간을 가진 행끼리 세로로 합친다. 반 칸짜리 지형(=, B, ^)은 층이 생기지 않도록 가로로만 합친다.
    /// 크기는 격자에서 읽는다(가로 32~40, 세로 18~22, 04-arena.md 6절). 맵 파일은 좌우 대칭이어야 한다('1'과 '2'는 서로 짝).
    /// 테스트 안에서 만드는 비대칭 고정 맵만 requireSymmetry를 끈다.
    /// </summary>
    public static class ArenaParser
    {
        public const string Separator = "---";
        public const int MinColumns = 32;
        public const int MaxColumns = 40;
        public const int MinRows = 18;
        public const int MaxRows = 22;

        public static ArenaDef Parse(string text, bool requireSymmetry = true)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string name = "";
            bool boundary = false;
            int gridStart = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == Separator)
                {
                    gridStart = i + 1;
                    break;
                }
                if (line.StartsWith("name:", StringComparison.Ordinal))
                    name = line.Substring(5).Trim();
                else if (line.StartsWith("boundary:", StringComparison.Ordinal))
                    boundary = line.Substring(9).Trim() == "true";
            }
            if (gridStart < 0)
                throw new FormatException($"'{Separator}' 구분선이 없음");
            if (name.Length == 0)
                throw new FormatException("'name:' 머리글이 없음");

            var grid = new List<string>();
            for (int i = gridStart; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0)
                    continue;
                grid.Add(line);
            }
            return ParseGrid(name, grid, requireSymmetry, boundary);
        }

        static ArenaDef ParseGrid(string name, List<string> grid, bool requireSymmetry, bool boundary)
        {
            int rows = grid.Count;
            if (rows < MinRows || rows > MaxRows)
                throw new FormatException($"{name}: 행 수가 {rows} (기대 {MinRows}~{MaxRows})");
            int columns = grid[0].Length;
            if (columns < MinColumns || columns > MaxColumns)
                throw new FormatException($"{name}: 열 수가 {columns} (기대 {MinColumns}~{MaxColumns})");
            var size = new Size { Columns = columns, Rows = rows };

            bool found1 = false, found2 = false;
            Vec2 spawn0 = default, spawn1 = default;
            for (int r = 0; r < rows; r++)
            {
                string row = grid[r];
                if (row.Length != columns)
                    throw new FormatException($"{name}: {r}행 길이가 {row.Length} (기대 {columns})");
                for (int c = 0; c < columns; c++)
                {
                    char ch = row[c];
                    bool border = r == 0 || r == rows - 1 || c == 0 || c == columns - 1;
                    if (border && ch != '#')
                        throw new FormatException($"{name}: 테두리 ({c}열, {r}행)가 '#'이 아님: '{ch}'");
                    switch (ch)
                    {
                        case '#':
                        case '=':
                        case 'B':
                        case '^':
                        case '.':
                            break;
                        case '1':
                            if (found1)
                                throw new FormatException($"{name}: 시작 위치 '1'이 둘 이상");
                            found1 = true;
                            spawn0 = CellFoot(size, c, r);
                            break;
                        case '2':
                            if (found2)
                                throw new FormatException($"{name}: 시작 위치 '2'가 둘 이상");
                            found2 = true;
                            spawn1 = CellFoot(size, c, r);
                            break;
                        default:
                            throw new FormatException($"{name}: 알 수 없는 문자 '{ch}' ({c}열, {r}행)");
                    }
                }
            }
            if (!found1 || !found2)
                throw new FormatException($"{name}: 시작 위치 '1'과 '2'가 각각 하나씩 있어야 함");
            if (requireSymmetry)
            {
                for (int r = 0; r < rows; r++)
                {
                    string row = grid[r];
                    for (int c = 0; c < columns / 2; c++)
                    {
                        if (row[c] != Mirror(row[columns - 1 - c]))
                            throw new FormatException($"{name}: 좌우 대칭이 아님 ({c}열, {r}행) '{row[c]}' / '{row[columns - 1 - c]}'");
                    }
                }
            }

            // 경계 튕김 맵(머리글 "boundary: true"): 테두리 칸은 검증만 하고 지형으로 만들지 않는다.
            if (boundary)
            {
                var inner = new List<string>(grid.Count);
                for (int r = 0; r < rows; r++)
                {
                    var chars = grid[r].ToCharArray();
                    for (int c = 0; c < columns; c++)
                    {
                        if (r == 0 || r == rows - 1 || c == 0 || c == columns - 1)
                            chars[c] = '.';
                    }
                    inner.Add(new string(chars));
                }
                grid = inner;
            }
            var boxes = new List<ArenaBox>();
            MergeFullCells(grid, size, '#', TerrainKind.Solid, boxes);
            MergeHalfCells(grid, size, '=', TerrainKind.OneWay, top: true, boxes);
            MergeHalfCells(grid, size, 'B', TerrainKind.Bounce, top: false, boxes);
            MergeHalfCells(grid, size, '^', TerrainKind.Spike, top: false, boxes);
            return new ArenaDef(name, columns, rows, boundary, boxes.ToArray(), spawn0, spawn1);
        }

        /// <summary>좌우로 뒤집었을 때 같은 자리에 와야 할 문자. 시작 위치 '1'과 '2'는 서로 짝이다.</summary>
        static char Mirror(char ch) => ch == '1' ? '2' : ch == '2' ? '1' : ch;

        struct Size
        {
            public int Columns;
            public int Rows;
            public float HalfWidth => Columns * 0.5f;
            public float HalfHeight => Rows * 0.5f;
        }

        static Vec2 CellFoot(in Size size, int c, int r) => new Vec2(c - size.HalfWidth + 0.5f, size.HalfHeight - r - 1f);

        struct Run
        {
            public int Row;
            public int C0;
            public int C1;
        }

        static List<Run> HorizontalRuns(List<string> grid, char ch, int row)
        {
            var runs = new List<Run>();
            string line = grid[row];
            int c = 0;
            while (c < line.Length)
            {
                if (line[c] != ch)
                {
                    c++;
                    continue;
                }
                int start = c;
                while (c < line.Length && line[c] == ch)
                    c++;
                runs.Add(new Run { Row = row, C0 = start, C1 = c - 1 });
            }
            return runs;
        }

        /// <summary>가로로 합친 뒤, 바로 아래 행에 같은 구간이 있으면 세로로 이어 붙인다.</summary>
        static void MergeFullCells(List<string> grid, Size size, char ch, TerrainKind kind, List<ArenaBox> output)
        {
            var open = new List<(Run top, int bottomRow)>();
            for (int r = 0; r <= size.Rows; r++)
            {
                var runs = r < size.Rows ? HorizontalRuns(grid, ch, r) : new List<Run>();
                var next = new List<(Run top, int bottomRow)>();
                foreach (var run in runs)
                {
                    int match = open.FindIndex(o => o.top.C0 == run.C0 && o.top.C1 == run.C1 && o.bottomRow == r - 1);
                    if (match >= 0)
                    {
                        next.Add((open[match].top, r));
                        open.RemoveAt(match);
                    }
                    else
                    {
                        next.Add((run, r));
                    }
                }
                foreach (var closed in open)
                    output.Add(FullBox(size, kind, closed.top, closed.bottomRow));
                open = next;
            }
        }

        static ArenaBox FullBox(in Size size, TerrainKind kind, Run top, int bottomRow) => new ArenaBox(
            kind,
            top.C0 - size.HalfWidth,
            size.HalfHeight - (bottomRow + 1),
            top.C1 + 1 - size.HalfWidth,
            size.HalfHeight - top.Row);

        static void MergeHalfCells(List<string> grid, Size size, char ch, TerrainKind kind, bool top, List<ArenaBox> output)
        {
            for (int r = 0; r < size.Rows; r++)
            {
                foreach (var run in HorizontalRuns(grid, ch, r))
                {
                    float cellTop = size.HalfHeight - r;
                    float cellBottom = cellTop - 1f;
                    output.Add(new ArenaBox(
                        kind,
                        run.C0 - size.HalfWidth,
                        top ? cellTop - 0.5f : cellBottom,
                        run.C1 + 1 - size.HalfWidth,
                        top ? cellTop : cellBottom + 0.5f));
                }
            }
        }
    }
}
