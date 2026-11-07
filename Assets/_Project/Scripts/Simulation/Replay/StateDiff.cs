using System;
using System.Collections.Generic;
using System.Text;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 상태 방문자로 모든 필드를 이름과 함께 모은다. 불일치 보고와 테스트용이므로 할당을 신경 쓰지 않는다.
    /// </summary>
    public struct FieldRecorder : IStateVisitor
    {
        public List<string> Names;
        public List<long> Bits;
        public List<string> Texts;
        List<string> scopes;

        public static FieldRecorder Create() => new FieldRecorder
        {
            Names = new List<string>(),
            Bits = new List<long>(),
            Texts = new List<string>(),
            scopes = new List<string>(),
        };

        public void Begin(string scope) => scopes.Add(scope);
        public void End() => scopes.RemoveAt(scopes.Count - 1);

        public void Value(string name, int v) => Add(name, v, v.ToString());
        public void Value(string name, uint v) => Add(name, v, v.ToString());
        public void Value(string name, float v) => Add(name, BitConverter.SingleToInt32Bits(v), v.ToString("R"));
        public void Value(string name, bool v) => Add(name, v ? 1 : 0, v ? "true" : "false");

        void Add(string name, long bits, string text)
        {
            Names.Add(scopes.Count == 0 ? name : string.Join(".", scopes) + "." + name);
            Bits.Add(bits);
            Texts.Add(text);
        }
    }

    public static class StateDiff
    {
        public static FieldRecorder Record(in SimState state)
        {
            var r = FieldRecorder.Create();
            state.Visit(ref r);
            return r;
        }

        /// <summary>다른 필드를 "이름: 기대 → 실제" 줄로 나열한다. 같으면 빈 문자열.</summary>
        public static string Describe(in SimState expected, in SimState actual, int maxLines = 16)
        {
            var a = Record(expected);
            var b = Record(actual);
            var sb = new StringBuilder();
            int lines = 0;
            for (int i = 0; i < a.Names.Count; i++)
            {
                if (a.Bits[i] == b.Bits[i])
                    continue;
                if (lines == maxLines)
                {
                    sb.AppendLine("...");
                    break;
                }
                sb.Append(a.Names[i]).Append(": ").Append(a.Texts[i]).Append(" → ").Append(b.Texts[i]).AppendLine();
                lines++;
            }
            return sb.ToString();
        }
    }
}
