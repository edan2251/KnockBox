using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 경기 흐름(06-match-and-tools.md 1절).
    /// Countdown → Fighting → KO → RoundEnd(1틱) → RoundTransition(1틱, 훅) → Countdown, 또는 RoundEnd → MatchEnd.
    /// 세트 흐름(CR-003 개정, 레거시가 아닌 규칙): FormSelect(경기 시작, 초기 폼) → … → RoundEnd → (세트 종료면) SetPick(세트 패자) → RoundTransition.
    /// 새 단계는 끝에 붙여 기존 번호를 바꾸지 않는다.
    /// </summary>
    public enum MatchPhase : byte
    {
        Countdown,
        Fighting,
        KO,
        RoundEnd,
        RoundTransition,
        MatchEnd,
        /// <summary>경기 시작의 초기 폼 비공개 동시 선택(D-053).</summary>
        FormSelect,
        /// <summary>세트가 끝난 뒤 세트 패자만 고르는 단계(D-050).</summary>
        SetPick,
    }

    /// <summary>선택지 번호. 0 없음, 1~3 폼 획득(폼 번호 + 1), 증강은 AugmentBase + 증강 번호(CP23).</summary>
    public static class PickOption
    {
        public const int None = 0;
        public const int AugmentBase = 16;
        public static int Form(FighterForm form) => 1 + (int)form;
        public static bool IsForm(int option) => option >= 1 && option <= Forms.PlayableCount;
        public static FighterForm FormOf(int option) => (FighterForm)(option - 1);
        public static bool IsAugment(int option) => option >= AugmentBase;
        public static int AugmentOf(int option) => option - AugmentBase;
    }

    [Serializable]
    public struct MatchState
    {
        public MatchPhase Phase;
        /// <summary>현재 단계에 들어온 뒤 지난 틱 수.</summary>
        public int PhaseTick;
        /// <summary>1부터 센다(경기 전체에서 이어 센다).</summary>
        public int Round;
        /// <summary>라운드 점수(세트 흐름에서는 지금 세트 안, 세트가 끝나면 0).</summary>
        public int Score0;
        public int Score1;
        /// <summary>세트스코어(세트 흐름).</summary>
        public int SetScore0;
        public int SetScore1;
        /// <summary>지금 세트 번호(1부터).</summary>
        public int Set;
        /// <summary>경기에서 한 선택 수(최대 MaxPicks).</summary>
        public int Picks0;
        public int Picks1;
        /// <summary>SetPick의 고르는 쪽(0, 1), 아니면 -1.</summary>
        public int Picker;
        /// <summary>선택지(PickOption). 쓰지 않는 칸은 None.</summary>
        public int Option0;
        public int Option1;
        public int Option2;
        public int Option3;
        /// <summary>FormSelect에서 고른 폼 번호(아직이면 -1).</summary>
        public int FormChoice0;
        public int FormChoice1;
        /// <summary>선택을 마친 틱(PhaseTick 기준, 아직이면 -1). PickHoldTicks 뒤 다음 단계로.</summary>
        public int PickedAt;
        /// <summary>마지막 KO의 승자. 0, 1, 또는 무승부·미정이면 -1.</summary>
        public int RoundWinner;
        /// <summary>라운드마다 맵 교체(CP12). 롤백 대상이라 상태에 둔다.</summary>
        public ArenaRotation Rotation;

        public readonly int GetScore(int fighter) => fighter == 0 ? Score0 : Score1;
        public readonly int GetSetScore(int fighter) => fighter == 0 ? SetScore0 : SetScore1;
        public readonly int GetPicks(int fighter) => fighter == 0 ? Picks0 : Picks1;

        public readonly int GetOption(int index) => index == 0 ? Option0 : index == 1 ? Option1 : index == 2 ? Option2 : index == 3 ? Option3 : PickOption.None;

        public void SetOption(int index, int option)
        {
            switch (index)
            {
                case 0: Option0 = option; break;
                case 1: Option1 = option; break;
                case 2: Option2 = option; break;
                case 3: Option3 = option; break;
            }
        }

        public readonly int OptionCount => (Option0 != 0 ? 1 : 0) + (Option1 != 0 ? 1 : 0) + (Option2 != 0 ? 1 : 0) + (Option3 != 0 ? 1 : 0);

        public const int MaxOptions = 4;

        public void SetPhase(MatchPhase phase)
        {
            Phase = phase;
            PhaseTick = 0;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Phase), (int)Phase);
            v.Value(nameof(PhaseTick), PhaseTick);
            v.Value(nameof(Round), Round);
            v.Value(nameof(Score0), Score0);
            v.Value(nameof(Score1), Score1);
            v.Value(nameof(RoundWinner), RoundWinner);
            v.Value(nameof(SetScore0), SetScore0);
            v.Value(nameof(SetScore1), SetScore1);
            v.Value(nameof(Set), Set);
            v.Value(nameof(Picks0), Picks0);
            v.Value(nameof(Picks1), Picks1);
            v.Value(nameof(Picker), Picker);
            v.Value(nameof(Option0), Option0);
            v.Value(nameof(Option1), Option1);
            v.Value(nameof(Option2), Option2);
            v.Value(nameof(Option3), Option3);
            v.Value(nameof(FormChoice0), FormChoice0);
            v.Value(nameof(FormChoice1), FormChoice1);
            v.Value(nameof(PickedAt), PickedAt);
            v.Begin(nameof(Rotation));
            Rotation.Visit(ref v);
            v.End();
        }
    }
}
