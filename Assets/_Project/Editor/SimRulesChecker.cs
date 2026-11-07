using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// 시뮬레이션 규칙 점검(01-architecture.md 8절). Simulation 폴더의 소스에서 금지 문자열과
    /// static 가변 필드를 찾는다. 문자열 규칙은 주석까지 검사한다(단순하고 엄격하게).
    /// SimMath.cs는 Math/MathF 호출이 허용된 유일한 파일이다(삼각함수는 여기서도 금지).
    /// </summary>
    public static class SimRulesChecker
    {
        public const string SimulationFolder = "Assets/_Project/Scripts/Simulation";
        public const string MathGatewayFile = "SimMath.cs";
        public const string StaticFieldRule = "static 가변 필드";

        public struct Violation
        {
            public string File;
            public int Line;
            public string Rule;
            public string Text;

            public override string ToString() => $"{File}:{Line} [{Rule}] {Text.Trim()}";
        }

        struct Rule
        {
            public string Name;
            public Regex Pattern;
            public bool AllowedInMathGateway;
        }

        static Rule[] CreateRules() => new[]
        {
            new Rule { Name = "UnityEngine", Pattern = new Regex(@"UnityEngine") },
            new Rule { Name = "System.Random", Pattern = new Regex(@"System\.Random|\bnew\s+Random\s*\(") },
            new Rule { Name = "DateTime", Pattern = new Regex(@"\bDateTime\b") },
            new Rule { Name = "Stopwatch", Pattern = new Regex(@"\bStopwatch\b") },
            new Rule { Name = "Environment", Pattern = new Regex(@"\bEnvironment\.") },
            new Rule { Name = "스레드·비동기", Pattern = new Regex(@"System\.Threading|\bThread\b|\bTask\b|\basync\b|\bawait\b") },
            new Rule { Name = "Dictionary", Pattern = new Regex(@"Dictionary<") },
            new Rule { Name = "HashSet", Pattern = new Regex(@"HashSet<") },
            new Rule { Name = "LINQ", Pattern = new Regex(@"System\.Linq") },
            new Rule { Name = "삼각함수", Pattern = new Regex(@"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan)") },
            new Rule { Name = "SimMath 밖의 Math 호출", Pattern = new Regex(@"\bMathF?\."), AllowedInMathGateway = true },
        };

        static readonly Regex StaticWord = new Regex(@"\bstatic\b");
        static readonly Regex TypeDeclaration = new Regex(@"\bconst\b|\bclass\b|\bstruct\b|\benum\b|\binterface\b|\bdelegate\b");
        static readonly Regex MutableReadonly = new Regex(@"\[|\bList<|\bset;");

        public static List<Violation> CheckSource(string source, string fileName)
        {
            var violations = new List<Violation>();
            var rules = CreateRules();
            bool isGateway = Path.GetFileName(fileName) == MathGatewayFile;
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                foreach (var rule in rules)
                {
                    if (isGateway && rule.AllowedInMathGateway)
                        continue;
                    if (rule.Pattern.IsMatch(line))
                        violations.Add(new Violation { File = fileName, Line = i + 1, Rule = rule.Name, Text = line });
                }
                if (IsMutableStaticField(line))
                    violations.Add(new Violation { File = fileName, Line = i + 1, Rule = StaticFieldRule, Text = line });
            }
            return violations;
        }

        /// <summary>
        /// 한 줄 선언 기준의 근사 검사. 허용: const, static readonly(배열·List 제외), 메서드, 속성 식 본문, 형식 선언.
        /// </summary>
        public static bool IsMutableStaticField(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("using "))
                return false;
            if (!StaticWord.IsMatch(trimmed) || TypeDeclaration.IsMatch(trimmed))
                return false;

            // 메서드·식 본문 속성은 '('나 '=>'가 대입 '='보다 먼저 나온다. 필드 초기화는 '='가 먼저다.
            int assign = IndexOfAssignment(trimmed);
            int paren = trimmed.IndexOf('(');
            int arrow = trimmed.IndexOf("=>", System.StringComparison.Ordinal);
            bool isMember = (paren >= 0 && (assign < 0 || paren < assign)) || (arrow >= 0 && (assign < 0 || arrow < assign));
            if (isMember)
                return false;

            if (Regex.IsMatch(trimmed, @"\breadonly\b"))
                return MutableReadonly.IsMatch(trimmed);
            if (Regex.IsMatch(trimmed, @"\{\s*get\s*;\s*\}"))
                return false;
            return true;
        }

        /// <summary>'=>', '==', '!=', '&lt;=', '&gt;='가 아닌 대입 '='의 위치. 없으면 -1.</summary>
        static int IndexOfAssignment(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '=')
                    continue;
                char prev = i > 0 ? s[i - 1] : ' ';
                char next = i + 1 < s.Length ? s[i + 1] : ' ';
                if (next == '>' || next == '=' || prev == '=' || prev == '!' || prev == '<' || prev == '>')
                    continue;
                return i;
            }
            return -1;
        }

        public static List<Violation> CheckFolder(string folder)
        {
            var violations = new List<Violation>();
            foreach (string path in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string relative = path.Replace('\\', '/');
                violations.AddRange(CheckSource(File.ReadAllText(path), relative));
            }
            return violations;
        }

        [MenuItem("Knockbox/Verify/Check Simulation Rules")]
        public static void RunFromMenu()
        {
            var violations = CheckFolder(SimulationFolder);
            if (violations.Count == 0)
            {
                Debug.Log("[Knockbox] 시뮬레이션 규칙 점검 통과");
                return;
            }
            var sb = new StringBuilder($"[Knockbox] 시뮬레이션 규칙 위반 {violations.Count}건\n");
            foreach (var v in violations)
                sb.AppendLine(v.ToString());
            Debug.LogError(sb.ToString());
        }
    }
}
