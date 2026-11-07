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

        /// <summary>
        /// 경기 시작의 초기 폼 선택(D-053): 둘 다 가위·바위·보 중 하나를 동시에 고른다. 둘 다 고르면 잠깐 멈춘 뒤 폼을 정하고 카운트다운.
        /// </summary>
        public static void StepFormSelect(ref SimState s, SimConfig config, InputButtons pressed0, InputButtons pressed1, SimEventBuffer events)
        {
            ref var m = ref s.Match;
            bool timeout = m.PhaseTick >= config.Match.PickTimeoutTicks;
            ChooseForm(ref m.FormChoice0, 0, pressed0, timeout, ref s.Rng, s.Tick, events);
            ChooseForm(ref m.FormChoice1, 1, pressed1, timeout, ref s.Rng, s.Tick, events);
            if (m.FormChoice0 < 0 || m.FormChoice1 < 0)
                return;
            if (m.PickedAt < 0)
                m.PickedAt = m.PhaseTick;
            if (m.PhaseTick - m.PickedAt < config.Match.PickHoldTicks)
                return;
            ApplyStartForm(ref s.Fighter0, (FighterForm)m.FormChoice0, config);
            ApplyStartForm(ref s.Fighter1, (FighterForm)m.FormChoice1, config);
            m.PickedAt = -1;
            m.SetPhase(MatchPhase.Countdown);
        }

        static void ChooseForm(ref int choice, int fighter, InputButtons pressed, bool timeout, ref SimRng rng, int tick, SimEventBuffer events)
        {
            if (choice >= 0)
                return;
            int pick = PressedOption(pressed, Forms.PlayableCount);
            bool timedOut = false;
            if (pick < 0 && timeout)
            {
                pick = rng.Range(0, Forms.PlayableCount);
                timedOut = true;
            }
            if (pick < 0)
                return;
            choice = pick;
            events.Add(new SimEvent(tick, SimEventType.Picked, fighter, Vec2.Zero, Vec2.Zero, timedOut ? 1 : 0));
        }

        static void ApplyStartForm(ref FighterState f, FighterForm form, SimConfig config)
        {
            f.Form = form;
            f.OwnedForms = (byte)(config.Rules.OwnedForms | Forms.Bit(form));
        }

        /// <summary>
        /// 세트 패자의 선택지(최대 4개): 아직 없는 폼 획득, 그다음 증강 후보(CP23). 넘치면 상태의 난수로 고른다(같은 시드면 같다).
        /// </summary>
        public static int BuildOptions(ref MatchState m, in FighterState f, SimConfig config, ref SimRng rng)
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
            // 후보를 고정 순서로 모은다: 폼(가위·바위·보), 그다음 증강 번호 순.
            int count = 0;
            var candidates = new CandidateList();
            for (int k = 0; k < Forms.PlayableCount; k++)
            {
                var form = (FighterForm)k;
                if (!Forms.Owns(f.OwnedForms, form))
                    candidates.Add(PickOption.Form(form), ref count);
            }
            Augments.AddCandidates(f, config, ref candidates, ref count);
            // 넘치면 부분 셔플로 앞의 MaxOptions개를 뽑는다. 순서는 뽑힌 순서 그대로(같은 시드면 같다).
            int take = SimMath.Min(count, MatchState.MaxOptions);
            for (int i = 0; i < take; i++)
            {
                int j = count > MatchState.MaxOptions ? rng.Range(i, count) : i;
                int chosen = candidates.Get(j);
                candidates.Set(j, candidates.Get(i));
                candidates.Set(i, chosen);
                m.SetOption(i, chosen);
            }
            return take;
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

        /// <summary>선택 적용: 폼 획득이면 보유하고 다음 라운드를 그 폼으로 시작(4.7), 증강이면 획득 목록에 더한다(CP23).</summary>
        public static void Apply(ref FighterState f, int option, SimConfig config)
        {
            if (PickOption.IsForm(option))
            {
                var form = PickOption.FormOf(option);
                f.OwnedForms |= Forms.Bit(form);
                f.Form = form;
            }
            else if (PickOption.IsAugment(option))
            {
                Augments.Grant(ref f, PickOption.AugmentOf(option), config);
            }
        }
    }

    /// <summary>선택지 후보를 모으는 고정 크기 목록(할당 없음). 폼 3 + 증강 최대 12.</summary>
    public struct CandidateList
    {
        public const int Capacity = 16;
        int c0, c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12, c13, c14, c15;

        public void Add(int value, ref int count)
        {
            if (count < Capacity)
                Set(count++, value);
        }

        public readonly int Get(int i)
        {
            switch (i)
            {
                case 0: return c0; case 1: return c1; case 2: return c2; case 3: return c3;
                case 4: return c4; case 5: return c5; case 6: return c6; case 7: return c7;
                case 8: return c8; case 9: return c9; case 10: return c10; case 11: return c11;
                case 12: return c12; case 13: return c13; case 14: return c14; default: return c15;
            }
        }

        public void Set(int i, int value)
        {
            switch (i)
            {
                case 0: c0 = value; break; case 1: c1 = value; break; case 2: c2 = value; break; case 3: c3 = value; break;
                case 4: c4 = value; break; case 5: c5 = value; break; case 6: c6 = value; break; case 7: c7 = value; break;
                case 8: c8 = value; break; case 9: c9 = value; break; case 10: c10 = value; break; case 11: c11 = value; break;
                case 12: c12 = value; break; case 13: c13 = value; break; case 14: c14 = value; break; default: c15 = value; break;
            }
        }
    }
}
