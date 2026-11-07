using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Knockbox.Game
{
    /// <summary>
    /// 시나리오 파일용 작은 JSON 읽기. 객체는 Dictionary&lt;string, object&gt;, 배열은 List&lt;object&gt;,
    /// 숫자는 double, 그 외 string·bool·null. 외부 패키지 없이 쓰기 위해 둔다.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var reader = new Reader(json ?? throw new ArgumentNullException(nameof(json)));
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("값 뒤에 남은 글자가 있음");
            return value;
        }

        sealed class Reader
        {
            readonly string text;
            int pos;

            public Reader(string text)
            {
                this.text = text;
            }

            public bool AtEnd => pos >= text.Length;

            public FormatException Error(string message)
            {
                int line = 1, column = 1;
                for (int i = 0; i < pos && i < text.Length; i++)
                {
                    if (text[i] == '\n') { line++; column = 1; }
                    else column++;
                }
                return new FormatException($"JSON {line}줄 {column}열: {message}");
            }

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
            }

            char Peek()
            {
                SkipWhitespace();
                if (AtEnd)
                    throw Error("예상치 못한 끝");
                return text[pos];
            }

            void Expect(char c)
            {
                if (Peek() != c)
                    throw Error($"'{c}'가 필요함");
                pos++;
            }

            public object ReadValue()
            {
                char c = Peek();
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error($"알 수 없는 값 시작 '{c}'");
                }
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                Expect('{');
                if (Peek() == '}')
                {
                    pos++;
                    return result;
                }
                while (true)
                {
                    if (Peek() != '"')
                        throw Error("객체 키는 문자열이어야 함");
                    string key = ReadString();
                    Expect(':');
                    if (result.ContainsKey(key))
                        throw Error($"중복된 키 \"{key}\"");
                    result[key] = ReadValue();
                    char c = Peek();
                    pos++;
                    if (c == '}')
                        return result;
                    if (c != ',')
                        throw Error("',' 또는 '}'가 필요함");
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                Expect('[');
                if (Peek() == ']')
                {
                    pos++;
                    return result;
                }
                while (true)
                {
                    result.Add(ReadValue());
                    char c = Peek();
                    pos++;
                    if (c == ']')
                        return result;
                    if (c != ',')
                        throw Error("',' 또는 ']'가 필요함");
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("문자열이 닫히지 않음");
                    char c = text[pos++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("잘못된 이스케이프");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length)
                                throw Error("잘못된 \\u 이스케이프");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default:
                            throw Error($"알 수 없는 이스케이프 '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0)
                    pos++;
                string token = text.Substring(start, pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error($"잘못된 숫자 \"{token}\"");
                return value;
            }

            void ReadLiteral(string literal)
            {
                SkipWhitespace();
                if (string.CompareOrdinal(text, pos, literal, 0, literal.Length) != 0)
                    throw Error($"'{literal}'가 필요함");
                pos += literal.Length;
            }
        }
    }
}
