using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 시나리오 JSON(06-match-and-tools.md 5절) → ScenarioDef. 형식 오류는 FormatException으로 알린다.
    /// </summary>
    public static class ScenarioJson
    {
        /// <summary>Assets/_Project/Data/Scenarios. 에디터와 개발 빌드 도구에서만 쓴다.</summary>
        public static string ScenarioFolder => Path.Combine(Application.dataPath, "_Project", "Data", "Scenarios");

        public static ScenarioDef LoadFile(string path)
        {
            try
            {
                return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{Path.GetFileName(path)}: {e.Message}", e);
            }
        }

        /// <summary>이름(확장자 제외)으로 시나리오 폴더에서 찾는다.</summary>
        public static ScenarioDef LoadByName(string name) => LoadFile(Path.Combine(ScenarioFolder, name + ".json"));

        public static string[] ListFiles() =>
            Directory.Exists(ScenarioFolder) ? Directory.GetFiles(ScenarioFolder, "*.json", SearchOption.TopDirectoryOnly) : new string[0];

        public static ScenarioDef Parse(string json, string fallbackName = "")
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("최상위 값은 객체여야 함");

            var def = new ScenarioDef
            {
                Name = GetString(root, "name", fallbackName),
                Arena = GetString(root, "arena", "Box"),
                Grab = GetBool(root, "grab", true),
                Overlay = GetBool(root, "overlay", false),
                Help = GetBool(root, "help", false),
                Theme = ParseTheme(GetString(root, "theme", "")),
                Ticks = GetInt(root, "ticks", -1),
                Seed = (uint)GetInt(root, "seed", 1),
            };
            if (def.Ticks <= 0)
                throw new FormatException("\"ticks\"는 1 이상이어야 함");
            foreach (string removed in new[] { "mode", "form", "form2", "owned", "guard", "energy" })
            {
                if (root.ContainsKey(removed))
                    throw new FormatException($"\"{removed}\": 폼 전투 시대의 키다(2026-11-11 제거). 시나리오는 현재 몸 폼 규칙으로만 돈다");
            }

            if (root.TryGetValue("spawn", out object spawnValue) && spawnValue != null)
            {
                var spawn = AsObject(spawnValue, "spawn");
                def.HasSpawn = true;
                def.Spawn0 = GetVec2(spawn, "p1");
                def.Spawn1 = GetVec2(spawn, "p2");
            }

            def.P1 = ReadInputs(root, "p1");
            def.P2 = ReadInputs(root, "p2");
            def.Captures = ReadCaptures(root);
            def.Expects = ReadExpects(root);
            def.Tuning = ReadTuning(root);
            // 모드 이름과 튜닝 경로가 맞는지 읽을 때 확인한다.
            ScenarioConfig.Create(def);
            return def;
        }

        static ScenarioTuning[] ReadTuning(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("tuning", out object value) || value == null)
                return new ScenarioTuning[0];
            var obj = AsObject(value, "tuning");
            var result = new ScenarioTuning[obj.Count];
            int i = 0;
            foreach (var pair in obj)
            {
                var t = new ScenarioTuning { Path = pair.Key };
                switch (pair.Value)
                {
                    case double d: t.Number = d; break;
                    case bool b: t.IsBool = true; t.Flag = b; break;
                    default: throw new FormatException($"tuning.{pair.Key}: 숫자나 참/거짓이 필요함");
                }
                result[i++] = t;
            }
            return result;
        }

        static ScenarioInput[] ReadInputs(Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out object value) || value == null)
                return new ScenarioInput[0];
            var list = AsArray(value, key);
            var result = new ScenarioInput[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                string where = $"{key}[{i}]";
                var entry = AsObject(list[i], where);
                bool isPress = entry.ContainsKey("at");
                bool isHold = entry.ContainsKey("from");
                if (isPress == isHold)
                    throw new FormatException($"{where}: \"from\"~\"to\"(hold) 또는 \"at\"(press) 중 하나만 써야 함");

                ParseNames(entry, isPress ? "press" : "hold", where, out var directions, out var buttons);
                if (isPress)
                {
                    result[i] = ScenarioInput.Press(GetInt(entry, "at", 0), GetInt(entry, "holdFor", 1), directions, buttons);
                }
                else
                {
                    int from = GetInt(entry, "from", 0);
                    int to = GetInt(entry, "to", -1);
                    if (to < from)
                        throw new FormatException($"{where}: \"to\"가 \"from\"보다 작음");
                    result[i] = ScenarioInput.Hold(from, to, directions, buttons);
                }
            }
            return result;
        }

        static void ParseNames(Dictionary<string, object> entry, string key, string where, out ScenarioDirections directions, out InputButtons buttons)
        {
            directions = ScenarioDirections.None;
            buttons = InputButtons.None;
            if (!entry.TryGetValue(key, out object value))
                throw new FormatException($"{where}: \"{key}\" 목록이 필요함");
            foreach (object item in AsArray(value, $"{where}.{key}"))
            {
                switch (item as string)
                {
                    case "Left": directions |= ScenarioDirections.Left; break;
                    case "Right": directions |= ScenarioDirections.Right; break;
                    case "Up": directions |= ScenarioDirections.Up; break;
                    case "Down": directions |= ScenarioDirections.Down; break;
                    case "Jump": buttons |= InputButtons.Jump; break;
                    case "Attack": buttons |= InputButtons.Attack; break;
                    case "Charge": buttons |= InputButtons.Charge; break;
                    case "Dash": buttons |= InputButtons.Dash; break;
                    case "Grab": buttons |= InputButtons.Grab; break;
                    default:
                        throw new FormatException($"{where}: 알 수 없는 입력 이름 \"{item}\" (Left, Right, Up, Down, Jump, Attack, Charge, Dash, Grab)");
                }
            }
        }

        static ScenarioCapture[] ReadCaptures(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("captures", out object value) || value == null)
                return new ScenarioCapture[0];
            var list = AsArray(value, "captures");
            var result = new ScenarioCapture[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var entry = AsObject(list[i], $"captures[{i}]");
                result[i] = new ScenarioCapture { Tick = GetInt(entry, "tick", 0), Name = GetString(entry, "name", $"capture{i}") };
            }
            return result;
        }

        static ScenarioExpect[] ReadExpects(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("expect", out object value) || value == null)
                return new ScenarioExpect[0];
            var list = AsArray(value, "expect");
            var result = new ScenarioExpect[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                string where = $"expect[{i}]";
                var entry = AsObject(list[i], where);
                var e = new ScenarioExpect
                {
                    Tick = GetInt(entry, "tick", -1),
                    Fighter = GetInt(entry, "fighter", 0),
                    Field = GetString(entry, "field", ""),
                };
                if (!ExpectEvaluator.IsSupported(e.Field))
                    throw new FormatException($"{where}: 지원하지 않는 필드 \"{e.Field}\" (지원: {ExpectEvaluator.SupportedFields})");
                if (entry.TryGetValue("min", out object min))
                {
                    e.HasMin = true;
                    e.Min = (float)AsNumber(min, where + ".min");
                }
                if (entry.TryGetValue("max", out object max))
                {
                    e.HasMax = true;
                    e.Max = (float)AsNumber(max, where + ".max");
                }
                if (entry.TryGetValue("equals", out object eq))
                {
                    switch (eq)
                    {
                        case double d: e.Equality = ExpectEquality.Number; e.EqualsNumber = (float)d; break;
                        case bool b: e.Equality = ExpectEquality.Bool; e.EqualsBool = b; break;
                        case string s: e.Equality = ExpectEquality.Text; e.EqualsText = s; break;
                        default: throw new FormatException($"{where}.equals: 숫자, 참/거짓, 문자열 중 하나여야 함");
                    }
                }
                if (!e.HasMin && !e.HasMax && e.Equality == ExpectEquality.None)
                    throw new FormatException($"{where}: min, max, equals 중 하나가 필요함");
                result[i] = e;
            }
            return result;
        }

        static Dictionary<string, object> AsObject(object value, string where) =>
            value as Dictionary<string, object> ?? throw new FormatException($"{where}: 객체가 필요함");

        static List<object> AsArray(object value, string where) =>
            value as List<object> ?? throw new FormatException($"{where}: 배열이 필요함");

        static double AsNumber(object value, string where) =>
            value is double d ? d : throw new FormatException($"{where}: 숫자가 필요함");

        static string GetString(Dictionary<string, object> obj, string key, string fallback) =>
            obj.TryGetValue(key, out object v) && v != null ? v as string ?? throw new FormatException($"\"{key}\": 문자열이 필요함") : fallback;

        static bool GetBool(Dictionary<string, object> obj, string key, bool fallback) =>
            obj.TryGetValue(key, out object v) && v != null ? v is bool b ? b : throw new FormatException($"\"{key}\": 참/거짓이 필요함") : fallback;

        static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            if (!obj.TryGetValue(key, out object v) || v == null)
                return fallback;
            double d = AsNumber(v, key);
            if (d != Math.Floor(d))
                throw new FormatException($"\"{key}\": 정수가 필요함");
            return (int)d;
        }

        static Vec2 GetVec2(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object v))
                throw new FormatException($"spawn.{key}가 필요함");
            var list = AsArray(v, "spawn." + key);
            if (list.Count != 2)
                throw new FormatException($"spawn.{key}: [x, y] 두 숫자가 필요함");
            return new Vec2((float)AsNumber(list[0], key), (float)AsNumber(list[1], key));
        }

        /// <summary>"A", "B", "C"(대소문자 무관) → 0, 1, 2. 비어 있으면 -1(맵이 정함).</summary>
        internal static int ParseTheme(string value)
        {
            if (string.IsNullOrEmpty(value))
                return -1;
            char c = char.ToUpperInvariant(value[0]);
            if (c < 'A' || c > 'C')
                throw new System.FormatException($"알 수 없는 테마: {value}(A, B, C 중 하나)");
            return c - 'A';
        }
    }
}
