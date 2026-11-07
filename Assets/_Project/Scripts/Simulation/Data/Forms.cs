using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 파이터의 폼(10-forms-and-growth.md, D-035). 폼이 곧 기본공격이다.
    /// 가위 = 충전 돌진(구 모드 A), 바위 = 투사체(CP19), 보 = 근접 휘두르기(구 모드 B, CP20).
    /// Test는 CP4의 시험 공격을 쓰는 기존 테스트 전용이며 게임에서 고를 수 없다.
    /// </summary>
    public enum FighterForm : byte
    {
        Scissors,
        Rock,
        Paper,
        Test,
        /// <summary>몸(CR-004): 주먹(탭), 박치기(홀드). 실험 경기 전용이며 폼 전환·선택에 들어가지 않는다.</summary>
        Body,
    }

    public static class Forms
    {
        /// <summary>게임에서 고를 수 있는 폼 수(가위, 바위, 보).</summary>
        public const int PlayableCount = 3;

        public static byte Bit(FighterForm form) => (byte)(1 << (int)form);

        public static bool Owns(byte mask, FighterForm form) => (mask & Bit(form)) != 0;

        /// <summary>세 폼을 모두 가진 비트.</summary>
        public const byte AllPlayable = (1 << 0) | (1 << 1) | (1 << 2);

        /// <summary>보유한 다음 폼(가위 → 바위 → 보 → 가위 순서). 다른 폼이 없으면 지금 폼.</summary>
        public static FighterForm NextOwned(FighterForm current, byte owned)
        {
            int start = current == FighterForm.Test ? -1 : (int)current;
            for (int k = 1; k <= PlayableCount; k++)
            {
                int next = ((start + k) % PlayableCount + PlayableCount) % PlayableCount;
                if (Owns(owned, (FighterForm)next) && next != (int)current)
                    return (FighterForm)next;
            }
            return current;
        }

        /// <summary>CR-003 전의 전역 모드 → 시작 폼.</summary>
        public static FighterForm FromMode(CombatMode mode)
        {
            switch (mode)
            {
                case CombatMode.Charge: return FighterForm.Scissors;
                case CombatMode.Swing: return FighterForm.Paper;
                default: return FighterForm.Test;
            }
        }
    }

    /// <summary>
    /// 기능 스위치(10 문서 5.1). 가드·잡기는 한 스위치(D-043)지만, 기존 F4(잡기만 끔) 테스트를 위해 잡기를 따로 둔다.
    /// </summary>
    [Serializable]
    public struct FeatureSwitches
    {
        public bool GuardEnabled;
        public bool GrabEnabled;
        /// <summary>공격 에너지(10 문서 4.4). 끄면 비용 없이 공격한다.</summary>
        public bool EnergyEnabled;

        /// <summary>CR-003 기본: 가드·잡기 끔, 에너지 켬.</summary>
        public static FeatureSwitches Default => new FeatureSwitches { GuardEnabled = false, GrabEnabled = false, EnergyEnabled = true };

        /// <summary>CP16까지의 규칙(기존 행동 회귀 검사): 가드 켬, 잡기는 인자, 에너지 끔.</summary>
        public static FeatureSwitches Legacy(bool grabEnabled) => new FeatureSwitches { GuardEnabled = true, GrabEnabled = grabEnabled, EnergyEnabled = false };
    }

    /// <summary>시작 폼과 기능 스위치. 입력 기록에는 Format() 문자열로 남긴다.</summary>
    [Serializable]
    public struct RuleSet
    {
        /// <summary>P1의 시작 폼. SeparateP2Form이 거짓이면 P2도 같다.</summary>
        public FighterForm StartForm;
        /// <summary>참이면 P2는 StartFormP2로 시작한다(폼 쌍 시험·봇 매트릭스, CP20).</summary>
        public bool SeparateP2Form;
        public FighterForm StartFormP2;
        /// <summary>시작할 때 보유하는 폼 비트(시작 폼은 항상 포함). 0이면 시작 폼 하나. CP22의 선택 전까지 전환을 시험하는 데 쓴다.</summary>
        public byte OwnedForms;
        public FeatureSwitches Switches;
        /// <summary>참이면 CP16까지의 경기 흐름(라운드 RoundsToWin 선승, 선택 없음). 거짓이면 라운드·세트·경기와 세트 패자 선택(D-049~D-052).</summary>
        public bool LegacyMatch;

        public static RuleSet Default => new RuleSet { StartForm = FighterForm.Scissors, Switches = FeatureSwitches.Default };

        public static RuleSet Legacy(CombatMode mode, bool grabEnabled) =>
            new RuleSet { StartForm = Forms.FromMode(mode), Switches = FeatureSwitches.Legacy(grabEnabled), LegacyMatch = true };

        /// <summary>CR-004 실험(D-061·D-062): 몸 폼, CP16 경기 흐름, 잡기 켬(단계 C), 가드·에너지 끔.</summary>
        public static RuleSet Brawl => new RuleSet
        {
            StartForm = FighterForm.Body,
            Switches = new FeatureSwitches { GrabEnabled = true },
            LegacyMatch = true,
        };

        public readonly FighterForm StartFormOf(int fighter) => fighter == 1 && SeparateP2Form ? StartFormP2 : StartForm;

        public readonly byte OwnedFormsOf(int fighter) => (byte)(OwnedForms | Forms.Bit(StartFormOf(fighter)));

        public readonly RuleSet WithOwned(byte owned)
        {
            var r = this;
            r.OwnedForms = owned;
            return r;
        }

        /// <summary>P1·P2의 시작 폼을 따로 정한 규칙.</summary>
        public readonly RuleSet WithForms(FighterForm p1, FighterForm p2)
        {
            var r = this;
            r.StartForm = p1;
            r.StartFormP2 = p2;
            r.SeparateP2Form = p1 != p2;
            if (!r.SeparateP2Form)
                r.StartFormP2 = default;
            return r;
        }

        /// <summary>예: "Scissors g0 r0 e1"(가드, 잡기, 에너지), 폼이 다르면 "Rock/Paper g0 r0 e1", 보유 폼이 있으면 끝에 " o7".</summary>
        public readonly string Format() =>
            $"{StartForm}{(SeparateP2Form ? "/" + StartFormP2 : "")} g{(Switches.GuardEnabled ? 1 : 0)} r{(Switches.GrabEnabled ? 1 : 0)} e{(Switches.EnergyEnabled ? 1 : 0)}"
            + (OwnedForms != 0 ? $" o{OwnedForms}" : "") + (LegacyMatch ? " legacy" : "");

        public static bool TryParse(string text, out RuleSet rules)
        {
            rules = Default;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text.Split(' ');
            bool legacy = parts.Length > 4 && parts[parts.Length - 1] == "legacy";
            int count = legacy ? parts.Length - 1 : parts.Length;
            if (count != 4 && count != 5)
                return false;
            byte owned = 0;
            if (count == 5 && (parts[4].Length < 2 || parts[4][0] != 'o' || !byte.TryParse(parts[4].Substring(1), out owned) || owned == 0))
                return false;
            string[] forms = parts[0].Split('/');
            if (forms.Length > 2 || !ParseForm(forms[0], out var p1))
                return false;
            var p2 = p1;
            if (forms.Length == 2 && !ParseForm(forms[1], out p2))
                return false;
            if (!Flag(parts[1], 'g', out bool guard) || !Flag(parts[2], 'r', out bool grab) || !Flag(parts[3], 'e', out bool energy))
                return false;
            rules = new RuleSet
            {
                Switches = new FeatureSwitches { GuardEnabled = guard, GrabEnabled = grab, EnergyEnabled = energy },
                OwnedForms = owned,
                LegacyMatch = legacy,
            }.WithForms(p1, p2);
            return true;
        }

        static bool ParseForm(string text, out FighterForm form) =>
            Enum.TryParse(text, false, out form) && Enum.IsDefined(typeof(FighterForm), form);

        static bool Flag(string part, char key, out bool value)
        {
            value = part.Length == 2 && part[1] == '1';
            return part.Length == 2 && part[0] == key && (part[1] == '0' || part[1] == '1');
        }
    }
}
