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
        /// <summary>연출 모드의 확정 대기(2026-11-14~, 2.4초).</summary>
        public const int LongPickHoldTicks = 144;
        public static bool HasCountIn(in MatchParams match) => match.CollectCardsOnly && match.CountdownTicks >= RebuildTicks + CountInTicks;

        /// <param name="longPickHold">
        /// 2026-11-14부터 모놀리스 카드 선택의 확정 연출(충전·곡선 도약·내리찍기·흡수) 시간을 위해 확정 대기를 84틱에서 144틱으로 늘렸다.
        /// 그 전 기록은 false(84틱)로 재현한다.
        /// </param>
        /// <summary>결과 공간: 맵 재구성(RebuildTicks) 뒤 낙하, 1초 뒤 준비 허용, KO 1초 뒤 재등장.</summary>
        public const int ResultsDropTick = RebuildTicks + 6;
        public const int ResultsUnlockTick = ResultsDropTick + 60;
        public const int ResultsRespawnTicks = 60;
        public const float ResultsDropHeight = 7f;
        /// <summary>로비 → 첫 경기 전환(눌림·도약·낙하 퇴장) 1.2초. 이 시간 동안 두 선수의 조작을 멈춘다.</summary>
        public const int LobbyExitTicks = 72;
        /// <summary>결과 발표(빈 시상대 상승 → 두 선수 낙하 → 결과 글자 낙하)의 기본 조작 잠금 3.2초. 결과 맵의 발표 값이 덮어쓴다.</summary>
        public const int ResultsCeremonyTicks = 192;
        /// <summary>결과 글자가 떨어지기 시작하는 기본 단계 틱(점수 숫자 발판 물리가 이 틱부터 움직인다).</summary>
        public const int ResultsSignTick = 146;

        /// <param name="resultsSpace">
        /// 2026-11-16 게임 흐름 CP1: 경기가 끝나면 결과 공간에서 자유 조작한다. 맵 목록의 마지막에 결과 공간 맵이 있어야 한다.
        /// 그 전 기록과 결과 공간 맵이 없는 실행은 false.
        /// </param>
        /// <param name="vsIntro">
        /// 거짓이면 첫 라운드도 다른 라운드와 같은 재구성·3·2·1·GO만 하고 VS 소개(222틱)를 하지 않는다(2026-11-17 사용자 요청, Main 기본).
        /// 그 전 기록·검사는 참(VS 소개 있음)으로 재현한다.
        /// </param>
        /// <param name="resultsCeremonyTicks">결과 발표 잠금 틱(0이면 그 전 결과 공간). 결과 공간 설정이 없으면 쓰지 않는다(2026-11-20).</param>
        /// <param name="resultsPropReleaseTick">결과 공간 물리 시작 틱(0이면 물리 없음, 점수 숫자 발판용). 결과 공간 설정이 없으면 쓰지 않는다.</param>
        /// <param name="lobbyExit">
        /// 참이면 로비에서 둘 다 준비한 뒤 LobbyExitTicks 동안 퇴장 연출을 하고 1라운드로 간다(2026-11-19 로비 화면 수정, Main 기본).
        /// 그 전 기록은 거짓(바로 1라운드)으로 재현한다. 로비 설정이 없으면 쓰지 않는다.
        /// </param>
        public static SimConfig Apply(SimConfig source, bool motion = true, bool cinematic = true, bool extendedIntro = true, bool longPickHold = true,
            bool resultsSpace = false, bool lobbySpace = false, bool vsIntro = true, bool lobbyExit = false,
            int resultsCeremonyTicks = 0, int resultsPropReleaseTick = 0)
        {
            var match = source.Match;
            if (resultsSpace || lobbySpace)
            {
                match.ResultsSpace = resultsSpace;
                match.LobbySpace = lobbySpace;
                match.ResultsDropTick = ResultsDropTick;
                match.ResultsUnlockTick = ResultsUnlockTick;
                match.ResultsRespawnTicks = ResultsRespawnTicks;
                match.ResultsDropHeight = ResultsDropHeight;
            }
            if (lobbySpace && lobbyExit)
                match.LobbyExitTicks = LobbyExitTicks;
            if (resultsSpace)
            {
                match.ResultsCeremonyTicks = resultsCeremonyTicks > 0 ? resultsCeremonyTicks : 0;
                match.ResultsPropReleaseTick = resultsPropReleaseTick > 0 ? resultsPropReleaseTick : 0;
            }
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
                    match.IntroCountdownTicks = vsIntro ? (extendedIntro ? IntroVsTicks : 90) + match.CountdownTicks : match.CountdownTicks;
                    match.KoTicks = 156;
                    match.PickHoldTicks = longPickHold ? LongPickHoldTicks : 84;
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
