using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 파이터의 폼. 2026-11-11 가위·바위·보 폼 전투를 제거해 몸(Body) 하나만 남았다.
    /// 번호 4는 기록·설정 체크섬 호환을 위해 그대로 둔다(0~3은 폼 시대의 Scissors·Rock·Paper·Test 자리).
    /// </summary>
    public enum FighterForm : byte
    {
        /// <summary>몸(CR-004): 주먹, 차징 박치기, 잡기·던지기.</summary>
        Body = 4,
    }

    /// <summary>기능 스위치. 가드·에너지 스위치는 폼 전투와 함께 제거했다(설정 해시에는 고정값 false로 남는다).</summary>
    [Serializable]
    public struct FeatureSwitches
    {
        public bool GrabEnabled;
    }

    /// <summary>
    /// 경기 규칙: 잡기 스위치와 경기 흐름. 입력 기록에는 Format() 문자열("Body g0 r1 e0 legacy")로 남는다.
    /// 2026-11-11 폼 전투 제거로 시작 폼·보유 폼·가드·에너지 항목은 없앴다. 기록 문자열과 설정 해시의 그 자리는 고정값이다(Body, g0, e0).
    /// </summary>
    [Serializable]
    public struct RuleSet
    {
        public FeatureSwitches Switches;
        /// <summary>참이면 라운드 RoundsToWin 선승, 선택 없음. 거짓이면 라운드·세트·경기와 세트 패자 선택(카드 흐름, D-049~D-052).</summary>
        public bool LegacyMatch;

        /// <summary>CR-004 경기(D-061·D-062): 몸 폼, 라운드 선승 흐름, 잡기 켬.</summary>
        public static RuleSet Brawl => new RuleSet
        {
            Switches = new FeatureSwitches { GrabEnabled = true },
            LegacyMatch = true,
        };

        /// <summary>예: "Body g0 r1 e0 legacy"(가드·잡기·에너지 표기는 기록 호환을 위해 유지, 가드·에너지는 항상 0).</summary>
        public readonly string Format() =>
            $"Body g0 r{(Switches.GrabEnabled ? 1 : 0)} e0" + (LegacyMatch ? " legacy" : "");

        /// <summary>현재 형식만 읽는다. 폼 시대 문자열(다른 폼, 가드·에너지 켬, 보유 폼)은 거부한다.</summary>
        public static bool TryParse(string text, out RuleSet rules)
        {
            rules = default;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text.Split(' ');
            bool legacy = parts.Length == 5 && parts[4] == "legacy";
            if (parts.Length != 4 && !legacy)
                return false;
            if (parts[0] != nameof(FighterForm.Body))
                return false;
            if (!Flag(parts[1], 'g', out bool guard) || !Flag(parts[2], 'r', out bool grab) || !Flag(parts[3], 'e', out bool energy) || guard || energy)
                return false;
            rules = new RuleSet { Switches = new FeatureSwitches { GrabEnabled = grab }, LegacyMatch = legacy };
            return true;
        }

        static bool Flag(string part, char key, out bool value)
        {
            value = part.Length == 2 && part[1] == '1';
            return part.Length == 2 && part[0] == key && (part[1] == '0' || part[1] == '1');
        }
    }
}
