namespace Knockbox.Simulation
{
    /// <summary>
    /// 라운드·세트·경기 흐름의 선택 단계(10-forms-and-growth.md 4.6·4.7 개정, CP22).
    /// 선택 입력은 버튼 비트를 선택 번호로 읽는다: 점프 = 1번, 공격 = 2번, 대시 = 3번, 가드 = 4번(누른 틱).
    /// 고르지 않으면 PickTimeoutTicks 뒤 상태의 난수로 정한다. 무엇을 골랐는지는 이벤트에 담지 않는다(비공개, D-040).
    /// </summary>
    public static class MatchFlow
    {
        /// <summary>선택 번호 → 버튼(점프, 공격, 대시, 가드).</summary>
        public static InputButtons OptionButton(int index) =>
            index == 0 ? InputButtons.Jump : index == 1 ? InputButtons.Attack : index == 2 ? InputButtons.Dash : index == 3 ? InputButtons.Guard : InputButtons.None;

        /// <summary>이번 틱에 새로 누른 선택 번호(0~count-1), 없으면 -1. 여러 개면 앞 번호.</summary>
        public static int PressedOption(InputButtons pressed, int count)
        {
            for (int i = 0; i < count && i < MatchState.MaxOptions; i++)
            {
                if ((pressed & OptionButton(i)) != 0)
                    return i;
            }
            return -1;
        }

        /// <summary>세트 패자의 선택지: 카드 흐름이면 카드 세 장(CardCollectionRules), 아니면 없음.</summary>
        public static int BuildOptions(ref MatchState m, in FighterState f, SimConfig config)
        {
            for (int i = 0; i < MatchState.MaxOptions; i++)
                m.SetOption(i, PickOption.None);
            if (config.Match.CollectCardsOnly)
            {
                if (f.Augments.Count >= AugmentState.Capacity) return 0;
                for (int i = 0; i < CardCollectionRules.CardCount; i++)
                    m.SetOption(i, PickOption.AugmentBase + CardCollectionRules.FirstCardId + i);
                return CardCollectionRules.CardCount;
            }
            // 카드 흐름이 아닌 세트 흐름에는 고를 것이 없다(폼 획득·폼 증강 후보는 폼 전투와 함께 제거).
            return 0;
        }

        /// <summary>세트 패자 선택(D-050). 고르면(또는 시간 초과) 적용하고 잠깐 멈춘 뒤 다음 라운드로.</summary>
        public static void StepSetPick(ref SimState s, SimConfig config, InputButtons pressed0, InputButtons pressed1, int tick, SimEventBuffer events, out bool done)
        {
            done = false;
            ref var m = ref s.Match;
            int picker = m.Picker;
            if (m.PickedAt < 0)
            {
                int count = m.OptionCount;
                int pick = PressedOption(picker == 0 ? pressed0 : pressed1, count);
                bool timedOut = false;
                if (pick < 0 && m.PhaseTick >= config.Match.PickTimeoutTicks)
                {
                    pick = s.Rng.Range(0, count);
                    timedOut = true;
                }
                if (pick < 0)
                    return;
                ref var f = ref picker == 0 ? ref s.Fighter0 : ref s.Fighter1;
                Apply(ref f, m.GetOption(pick), config);
                if (picker == 0)
                    m.Picks0++;
                else
                    m.Picks1++;
                m.PickedAt = m.PhaseTick;
                events.Add(new SimEvent(tick, SimEventType.Picked, picker, Vec2.Zero, Vec2.Zero, timedOut ? 1 : 0));
            }
            if (m.PhaseTick - m.PickedAt < config.Match.PickHoldTicks)
                return;
            for (int i = 0; i < MatchState.MaxOptions; i++)
                m.SetOption(i, PickOption.None);
            m.Picker = -1;
            m.PickedAt = -1;
            done = true;
        }

        /// <summary>선택 적용: 증강(카드)을 획득 목록에 더한다(CP23).</summary>
        public static void Apply(ref FighterState f, int option, SimConfig config)
        {
            if (PickOption.IsAugment(option))
            {
                Augments.Grant(ref f, PickOption.AugmentOf(option), config);
            }
        }
    }

}
