using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 다른 PC 결정론 확인(CR-004 온라인 1단계): 입력 기록을 처음부터 재생하며 틱마다 체크섬을 남기고,
    /// 지정한 틱의 전체 상태를 필드 단위로 남긴다(시뮬레이션 밖 도구, Step 안에서는 쓰지 않음). 재생은 모든 입력이 확정된 계산이므로 모든 틱을 비교해도 된다.
    /// 체크섬 불일치는 감지일 뿐이고, 원인은 같은 틱의 상태 덤프를 두 PC에서 비교해 찾는다.
    /// </summary>
    public static class ReplayTrace
    {
        /// <summary>
        /// 재생한다. 반환 배열의 i번째는 i틱 진행 뒤의 체크섬(0번은 시작 상태). dumpTicks에 든 틱(0 = 시작 상태)의 덤프를 onDump(틱, 덤프)로 넘긴다.
        /// 설정·맵·시작 상태가 기록과 다르면 예외(InputRecording.CreateInitialState).
        /// </summary>
        public static uint[] Run(InputRecording recording, SimConfig config, ArenaDef[] arenas, ICollection<int> dumpTicks, Action<int, string> onDump)
        {
            var world = new SimWorld(config, arenas, recording.CreateInitialState(config, arenas));
            var checksums = new uint[recording.Count + 1];
            checksums[0] = world.ComputeChecksum();
            if (dumpTicks != null && dumpTicks.Contains(0))
                onDump(0, Dump(world.State));
            for (int i = 0; i < recording.Count; i++)
            {
                world.Step(recording.GetP1(i), recording.GetP2(i));
                checksums[i + 1] = world.ComputeChecksum();
                if (dumpTicks != null && dumpTicks.Contains(i + 1))
                    onDump(i + 1, Dump(world.State));
            }
            return checksums;
        }

        /// <summary>상태의 모든 필드를 "이름 = 값 [비트]" 줄로. 두 PC의 같은 틱 덤프를 줄 단위로 비교하면 처음 달라진 필드가 보인다.</summary>
        public static string Dump(in SimState state)
        {
            var r = StateDiff.Record(state);
            var sb = new StringBuilder(r.Names.Count * 40);
            for (int i = 0; i < r.Names.Count; i++)
                sb.Append(r.Names[i]).Append(" = ").Append(r.Texts[i]).Append(" [").Append((r.Bits[i] & 0xFFFFFFFFL).ToString("X", CultureInfo.InvariantCulture)).Append("]\n");
            return sb.ToString();
        }

        /// <summary>두 체크섬 열에서 처음 다른 틱. 같으면 −1, 길이만 다르면 짧은 쪽 길이.</summary>
        public static int FirstMismatch(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
        {
            int n = SimMath.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                    return i;
            }
            return a.Count == b.Count ? -1 : n;
        }

        /// <summary>추적 파일 본문: "틱 체크섬(16진)" 줄. '#'으로 시작하는 줄은 머리말.</summary>
        public static string FormatChecksums(uint[] checksums)
        {
            var sb = new StringBuilder(checksums.Length * 14);
            for (int i = 0; i < checksums.Length; i++)
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(checksums[i].ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        public static List<uint> ParseChecksums(string text)
        {
            var list = new List<uint>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                string[] parts = line.Split(' ');
                if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick) || tick != list.Count)
                    throw new FormatException($"추적 파일 줄 형식이 다름: \"{line}\"");
                list.Add(uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            return list;
        }
    }
}
