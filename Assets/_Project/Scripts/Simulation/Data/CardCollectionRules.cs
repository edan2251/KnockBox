namespace Knockbox.Simulation
{
    /// <summary>CR-005: 2라운드 선승/5세트 선승. 카드는 획득 목록에만 쌓이며 능력 수정자는 전혀 없다.</summary>
    public static class CardCollectionRules
    {
        public const int FirstCardId = 1000;
        public const int CardCount = 3;
        public const int VsTicks = 108;
        // 첫 경기 소개: 배경 충돌 → 캐릭터 진입 → 2초 감상 → 퇴장. 이후 라운드는 이 시간을 쓰지 않는다.
        public const int IntroVsTicks = 222;
        public const int RebuildTicks = 54;
        public const int CountBeatTicks = 60;
        public const int GoTicks = 24;
        public const int CountInTicks = CountBeatTicks * 3 + GoTicks;
        public static bool HasCountIn(in MatchParams match) => match.CollectCardsOnly && match.CountdownTicks >= RebuildTicks + CountInTicks;

        public static SimConfig Apply(SimConfig source, bool motion = true, bool cinematic = true, bool extendedIntro = true)
        {
            var match = source.Match;
            match.CollectCardsOnly = true;
            match.RoundsToWinSet = 2;
            match.SetsToWinMatch = 5;
            match.MaxPicks = 4;
            match.CountdownTicks = VsTicks;
            match.PickHoldTicks = 54;
            match.PickTimeoutTicks = 900;
            if (motion)
            {
                match.IntroCountdownTicks = 144;
                match.CountdownTicks = 54;
                match.KoTicks = 66;
                match.PickHoldTicks = 42;
                match.MatchEndMinTicks = 210;
                if (cinematic)
                {
                    match.CountdownTicks = RebuildTicks + CountInTicks;
                    // 기존 v8 리플레이의 90틱 소개도 같은 입력/상태 형식으로 재현한다.
                    match.IntroCountdownTicks = (extendedIntro ? IntroVsTicks : 90) + match.CountdownTicks;
                    match.KoTicks = 156;
                    match.PickHoldTicks = 84;
                }
            }
            var rules = source.Rules;
            rules.LegacyMatch = false;
            return new SimConfig(source.Movement, match, source.Combat, rules, new[]
            {
                Card(0, "한 방"), Card(1, "맷집"), Card(2, "멀리 던지기")
            });
        }

        static AugmentDef Card(int index, string name) => new AugmentDef
        {
            Id = FirstCardId + index, Name = name, Target = AugmentTarget.Common, MaxStacks = 4,
        };

        public static int CardIndex(int id) => id >= FirstCardId && id < FirstCardId + CardCount ? id - FirstCardId : -1;
    }
}
