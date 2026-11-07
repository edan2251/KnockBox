using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 다른 PC 결정론 확인(CR-004 온라인 1단계, 개발 빌드·에디터 전용). 명령줄로 입력 기록을 재생해 틱별 체크섬을 파일로 남기고 끝낸다.
    ///   -replaycheck 기록.kbrp   재생할 기록
    ///   -out 폴더                결과 폴더(기본: 실행 파일 옆 results)
    ///   -expect trace.txt        기준 추적 파일(다른 PC나 이 PC의 이전 결과). 처음 다른 틱과 그 앞 틱의 상태를 덤프한다
    ///   -dumpticks 10,11         이 틱들의 전체 상태를 덤프(상대 PC의 불일치 틱을 이 PC에서 다시 뽑을 때)
    /// 결과: trace.txt(머리말 + "틱 체크섬"), result.txt(요약), dump_t틱.txt. 종료 코드 0 같음, 1 다름, 2 실행 오류.
    /// </summary>
    public static class ReplayCheck
    {
        public const string Arg = "-replaycheck";

        public struct Outcome
        {
            public int ExitCode;
            public string Summary;
            public int FirstMismatch;
        }

        public static string StringArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>명령줄 인수로 실행한다.</summary>
        public static Outcome RunFromCommandLine(MatchRunner runner)
        {
            string outDir = StringArg("-out") ?? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "results");
            var ticks = new List<int>();
            string list = StringArg("-dumpticks");
            if (!string.IsNullOrEmpty(list))
            {
                foreach (string part in list.Split(','))
                {
                    if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t))
                        ticks.Add(t);
                }
            }
            return Run(runner, StringArg(Arg), outDir, StringArg("-expect"), ticks);
        }

        public static Outcome Run(MatchRunner runner, string recordingPath, string outDir, string expectPath, List<int> dumpTicks)
        {
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            var outcome = new Outcome { ExitCode = 2, FirstMismatch = -1 };
            try
            {
                if (string.IsNullOrEmpty(recordingPath) || !File.Exists(recordingPath))
                    throw new FileNotFoundException($"기록 파일이 없음: {recordingPath}");
                byte[] bytes = File.ReadAllBytes(recordingPath);
                var recording = InputRecording.FromBytes(bytes);
                var config = runner.FindRecordedConfig(recording)
                             ?? throw new InvalidOperationException($"기록과 같은 설정을 찾지 못함(설정 체크섬 {recording.ConfigChecksum:X8}, 규칙 {recording.Mode}). 빌드가 기록과 다름");
                string[] names = recording.ArenaNames;
                var arenas = new ArenaDef[names.Length];
                for (int i = 0; i < names.Length; i++)
                    arenas[i] = runner.ResolveRecordedArena(names[i]);

                List<uint> expected = null;
                if (!string.IsNullOrEmpty(expectPath))
                    expected = ReplayTrace.ParseChecksums(File.ReadAllText(expectPath));

                var dumps = new Dictionary<int, string>();
                var wanted = new HashSet<int>(dumpTicks ?? new List<int>());
                uint[] checksums = ReplayTrace.Run(recording, config, arenas, wanted, (t, text) => dumps[t] = text);
                int mismatch = expected != null ? ReplayTrace.FirstMismatch(checksums, expected) : -1;
                if (mismatch >= 0)
                {
                    // 처음 다른 틱과 그 앞(마지막으로 같았던) 틱의 상태를 다시 계산해 남긴다.
                    var extra = new HashSet<int> { Math.Max(mismatch - 1, 0), Math.Min(mismatch, checksums.Length - 1) };
                    extra.ExceptWith(dumps.Keys);
                    if (extra.Count > 0)
                        ReplayTrace.Run(recording, config, arenas, extra, (t, text) => dumps[t] = text);
                }

                var header = new StringBuilder();
                header.Append("# Knockbox replay trace\n");
                header.Append("# recording: ").Append(Path.GetFileName(recordingPath)).Append(" (").Append(bytes.Length).Append(" bytes, FNV ").Append(Fnv(bytes).ToString("X8")).Append(")\n");
                header.Append("# frames: ").Append(recording.Count).Append('\n');
                header.Append("# config: ").Append(recording.ConfigChecksum.ToString("X8")).Append(", arena: ").Append(recording.ArenaChecksum.ToString("X8"))
                    .Append(" (").Append(recording.Arena).Append("), rules: ").Append(recording.Mode).Append('\n');
                header.Append("# initial: ").Append(checksums[0].ToString("X8")).Append(", final: ").Append(checksums[checksums.Length - 1].ToString("X8"))
                    .Append(", recorded final: ").Append(recording.HasFinalChecksum ? recording.FinalChecksum.ToString("X8") : "-").Append('\n');
                header.Append("# build: ").Append(Application.isEditor ? "editor" : "player").Append(", unity ").Append(Application.unityVersion)
                    .Append(", buildGUID ").Append(Application.buildGUID).Append(", version ").Append(Application.version).Append('\n');
                header.Append("# os: ").Append(SystemInfo.operatingSystem).Append('\n');
                header.Append("# cpu: ").Append(SystemInfo.processorType).Append(", ").Append(SystemInfo.processorCount).Append(" threads\n");
                File.WriteAllText(Path.Combine(outDir, "trace.txt"), header + ReplayTrace.FormatChecksums(checksums));
                foreach (var pair in dumps)
                    File.WriteAllText(Path.Combine(outDir, $"dump_t{pair.Key}.txt"), $"# tick {pair.Key}, checksum {checksums[pair.Key]:X8}\n" + pair.Value);

                bool finalSame = !recording.HasFinalChecksum || checksums[checksums.Length - 1] == recording.FinalChecksum;
                sb.Append(header);
                sb.Append("최종 체크섬이 기록(만든 PC의 에디터)과 ").Append(finalSame ? "같음" : "다름").Append('\n');
                if (expected == null)
                    sb.Append("기준 추적 파일 없음(-expect): 틱별 비교는 하지 않음\n");
                else if (mismatch < 0)
                    sb.Append("기준 추적과 모든 틱(").Append(checksums.Length).Append("개, 0 = 시작 상태) 일치\n");
                else
                    sb.Append("기준 추적과 처음 다른 틱: ").Append(mismatch).Append(" (이쪽 ").Append(mismatch < checksums.Length ? checksums[mismatch].ToString("X8") : "-")
                        .Append(", 기준 ").Append(mismatch < expected.Count ? expected[mismatch].ToString("X8") : "-").Append("), 상태 덤프: dump_t")
                        .Append(Math.Max(mismatch - 1, 0)).Append(".txt, dump_t").Append(mismatch).Append(".txt\n");
                if (dumps.Count > 0)
                    sb.Append("덤프한 틱: ").Append(string.Join(",", new List<int>(dumps.Keys))).Append('\n');
                bool pass = finalSame && mismatch < 0;
                sb.Append("결과: ").Append(pass ? "PASS" : "FAIL").Append('\n');
                outcome.ExitCode = pass ? 0 : 1;
                outcome.FirstMismatch = mismatch;
            }
            catch (Exception e)
            {
                sb.Append("결과: ERROR\n").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
            }
            outcome.Summary = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "result.txt"), outcome.Summary);
            Debug.Log("[Knockbox][replaycheck]\n" + outcome.Summary);
            return outcome;
        }

        static uint Fnv(byte[] bytes)
        {
            uint h = 2166136261u;
            foreach (byte b in bytes)
                h = (h ^ b) * 16777619u;
            return h;
        }
    }
}
