using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 시나리오 expect를 상태에 대해 평가한다. 지원 필드는 아래 switch가 전부다(SupportedFields).
    /// 필드를 추가하면 SupportedFields 문자열에도 추가한다.
    /// </summary>
    public static class ExpectEvaluator
    {
        public const string SupportedFields = "tick, phase, round, score, roundWinner, pos.x, pos.y, vel.x, vel.y, speed, facing, action, actionTick, grounded, wallSide, airJumps, dashCharges, hp, hitstop, stun, combo, parry, bounces, spikeImmunity";
        public const float EqualsTolerance = 1e-4f;

        enum Kind { Number, Bool, Text }

        public static bool IsSupported(string field) => TryRead(default, 0, field, out _, out _, out _, out _);

        /// <summary>통과하면 null, 실패하면 이유를 담은 문장.</summary>
        public static string Evaluate(in SimState state, in ScenarioExpect expect)
        {
            if (expect.Fighter < 0 || expect.Fighter > 1)
                return $"{Label(expect)}: 파이터 번호는 0 또는 1이어야 함";
            if (!TryRead(state, expect.Fighter, expect.Field, out var kind, out float number, out bool flag, out string text))
                return $"{Label(expect)}: 지원하지 않는 필드 (지원: {SupportedFields})";

            switch (expect.Equality)
            {
                case ExpectEquality.Number:
                    if (kind != Kind.Number)
                        return $"{Label(expect)}: 숫자 필드가 아님";
                    if (SimMath.Abs(number - expect.EqualsNumber) > EqualsTolerance)
                        return $"{Label(expect)} = {number:R} (기대 {expect.EqualsNumber:R})";
                    break;
                case ExpectEquality.Bool:
                    if (kind != Kind.Bool)
                        return $"{Label(expect)}: 참/거짓 필드가 아님";
                    if (flag != expect.EqualsBool)
                        return $"{Label(expect)} = {flag} (기대 {expect.EqualsBool})";
                    break;
                case ExpectEquality.Text:
                    if (kind != Kind.Text)
                        return $"{Label(expect)}: 이름 필드가 아님";
                    if (!string.Equals(text, expect.EqualsText, StringComparison.Ordinal))
                        return $"{Label(expect)} = {text} (기대 {expect.EqualsText})";
                    break;
            }

            if (expect.HasMin || expect.HasMax)
            {
                if (kind != Kind.Number)
                    return $"{Label(expect)}: min/max는 숫자 필드에만 쓸 수 있음";
                if ((expect.HasMin && number < expect.Min) || (expect.HasMax && number > expect.Max))
                {
                    string min = expect.HasMin ? expect.Min.ToString("R") : "-∞";
                    string max = expect.HasMax ? expect.Max.ToString("R") : "∞";
                    return $"{Label(expect)} = {number:R} (기대 {min} ~ {max})";
                }
            }

            if (expect.Equality == ExpectEquality.None && !expect.HasMin && !expect.HasMax)
                return $"{Label(expect)}: 비교 조건(min, max, equals)이 없음";
            return null;
        }

        static string Label(in ScenarioExpect e) => $"tick {e.Tick} P{e.Fighter + 1} {e.Field}";

        static bool TryRead(in SimState s, int fighter, string field, out Kind kind, out float number, out bool flag, out string text)
        {
            var f = s.GetFighter(fighter);
            kind = Kind.Number;
            number = 0f;
            flag = false;
            text = null;
            switch (field)
            {
                case "tick": number = s.Tick; return true;
                case "phase": kind = Kind.Text; text = s.Match.Phase.ToString(); return true;
                case "round": number = s.Match.Round; return true;
                case "score": number = s.Match.GetScore(fighter); return true;
                case "roundWinner": number = s.Match.RoundWinner; return true;
                case "pos.x": number = f.Position.X; return true;
                case "pos.y": number = f.Position.Y; return true;
                case "vel.x": number = f.Velocity.X; return true;
                case "vel.y": number = f.Velocity.Y; return true;
                case "speed": number = SimMath.Length(f.Velocity); return true;
                case "facing": number = f.Facing; return true;
                case "action": kind = Kind.Text; text = f.Action.ToString(); return true;
                case "actionTick": number = f.ActionTick; return true;
                case "grounded": kind = Kind.Bool; flag = f.Grounded; return true;
                case "wallSide": number = f.WallSide; return true;
                case "airJumps": number = f.AirJumpsLeft; return true;
                case "dashCharges": number = f.DashCharges; return true;
                case "hp": number = f.Health; return true;
                case "hitstop": number = f.HitstopTicks; return true;
                case "stun": number = f.StunTicks; return true;
                case "combo": number = f.ComboHits; return true;
                case "parry": number = f.ParryTicks; return true;
                case "bounces": number = f.LungeBounces; return true;
                case "spikeImmunity": number = f.SpikeImmunityTicks; return true;
                case "boundaryImmunity": number = f.BoundaryImmunityTicks; return true;
                default: return false;
            }
        }
    }
}
