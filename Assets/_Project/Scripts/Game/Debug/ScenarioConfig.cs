using System;
using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>
    /// 시나리오의 시뮬레이션 설정: 튜닝 에셋(없으면 명세 초기값) + 시나리오의 mode + tuning 덮어쓰기.
    /// 덮어쓰기는 "Combat.TestAttack.KnockbackStrength"처럼 필드 경로로 쓰고, 반사로 적용한다(Game 계층이라 허용).
    /// </summary>
    public static class ScenarioConfig
    {
        public static SimConfig Create(ScenarioDef def, MovementTuning movement = null, CombatTuning combat = null) =>
            Apply(string.IsNullOrEmpty(def.Form)
                ? SimConfigFactory.Create(movement, combat, ParseMode(def.Mode), def.Grab)
                : SimConfigFactory.Create(movement, combat, ParseRules(def)), def.Tuning);

        /// <summary>"form"이 있으면 폼 규칙(가드·잡기·에너지는 시나리오 값), 없으면 mode·grab의 레거시 규칙.</summary>
        public static RuleSet ParseRules(ScenarioDef def)
        {
            if (string.IsNullOrEmpty(def.Form))
                return RuleSet.Legacy(ParseMode(def.Mode), def.Grab);
            var form = ParseForm(def.Form, "form");
            var form2 = string.IsNullOrEmpty(def.Form2) ? form : ParseForm(def.Form2, "form2");
            if (def.Owned < 0 || def.Owned > Forms.AllPlayable)
                throw new FormatException($"\"owned\": 0~{Forms.AllPlayable} 사이 비트여야 함");
            return new RuleSet
            {
                Switches = new FeatureSwitches { GuardEnabled = def.Guard, GrabEnabled = def.Grab, EnergyEnabled = def.Energy },
                OwnedForms = (byte)def.Owned,
            }.WithForms(form, form2);
        }

        static FighterForm ParseForm(string text, string key)
        {
            if (!Enum.TryParse(text, false, out FighterForm form) || !Enum.IsDefined(typeof(FighterForm), form))
                throw new FormatException($"\"{key}\": 알 수 없는 폼 \"{text}\" (가능: {string.Join(", ", Enum.GetNames(typeof(FighterForm)))})");
            return form;
        }

        /// <summary>빈 문자열이면 테스트 모드.</summary>
        public static CombatMode ParseMode(string mode)
        {
            if (string.IsNullOrEmpty(mode))
                return CombatMode.Test;
            if (Enum.TryParse(mode, false, out CombatMode parsed) && Enum.IsDefined(typeof(CombatMode), parsed))
                return parsed;
            throw new FormatException($"\"mode\": 알 수 없는 모드 \"{mode}\" (가능: {string.Join(", ", Enum.GetNames(typeof(CombatMode)))})");
        }

        public static SimConfig Apply(SimConfig source, ScenarioTuning[] overrides)
        {
            if (overrides == null || overrides.Length == 0)
                return source;
            object movement = source.Movement;
            object match = source.Match;
            object combat = source.Combat;
            foreach (var t in overrides)
            {
                string[] parts = (t.Path ?? "").Split('.');
                if (parts.Length < 2)
                    throw new FormatException($"tuning.{t.Path}: \"Movement.\", \"Match.\", \"Combat.\"으로 시작하는 경로가 필요함");
                switch (parts[0])
                {
                    case "Movement": movement = SetPath(movement, parts, 1, t); break;
                    case "Match": match = SetPath(match, parts, 1, t); break;
                    case "Combat": combat = SetPath(combat, parts, 1, t); break;
                    default: throw new FormatException($"tuning.{t.Path}: \"{parts[0]}\"는 Movement, Match, Combat 중 하나여야 함");
                }
            }
            return new SimConfig((MovementParams)movement, (MatchParams)match, (CombatParams)combat, source.Rules);
        }

        static object SetPath(object boxed, string[] parts, int index, in ScenarioTuning t)
        {
            var field = boxed.GetType().GetField(parts[index]);
            if (field == null)
                throw new FormatException($"tuning.{t.Path}: {boxed.GetType().Name}에 \"{parts[index]}\" 필드가 없음");
            if (index < parts.Length - 1)
            {
                field.SetValue(boxed, SetPath(field.GetValue(boxed), parts, index + 1, t));
                return boxed;
            }
            var type = field.FieldType;
            if (type == typeof(bool))
            {
                if (!t.IsBool)
                    throw new FormatException($"tuning.{t.Path}: 참/거짓이 필요함");
                field.SetValue(boxed, t.Flag);
            }
            else if (type == typeof(int))
            {
                if (t.IsBool || t.Number != Math.Floor(t.Number))
                    throw new FormatException($"tuning.{t.Path}: 정수가 필요함");
                field.SetValue(boxed, (int)t.Number);
            }
            else if (type == typeof(float))
            {
                if (t.IsBool)
                    throw new FormatException($"tuning.{t.Path}: 숫자가 필요함");
                field.SetValue(boxed, (float)t.Number);
            }
            else
            {
                throw new FormatException($"tuning.{t.Path}: {type.Name} 필드는 덮어쓸 수 없음(숫자·참/거짓만)");
            }
            return boxed;
        }
    }
}
