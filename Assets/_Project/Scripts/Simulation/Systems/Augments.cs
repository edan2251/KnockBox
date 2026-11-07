namespace Knockbox.Simulation
{
    /// <summary>
    /// 증강 적용 결과(10-forms-and-growth.md 5.3). 획득 목록과 설정에서 매번 계산한다(상태 아님).
    /// 단계 ①(가산)·②(배율)는 더하기·곱하기라 순서와 무관하고, ③(플래그)·④(슬롯)는 Priority → Id로 승자를 정해 획득 순서에 의존하지 않는다.
    /// </summary>
    public struct AugmentEffects
    {
        public float LungeSpeedMul;
        public int LungeCostAdd;
        public int ShotCostAdd;
        public int SwingCostAdd;
        public int ShotStartupAdd;
        /// <summary>-1 바꾸지 않음, 0 반사 불가, 1 반사 가능.</summary>
        public int Reflectable;
        /// <summary>가위 발사 방식 슬롯을 차지한 증강 번호(-1 없음).</summary>
        public int LaunchSlotOwner;
        /// <summary>휴면(무의미 조합)인 칸 비트(AugmentState 칸 번호).</summary>
        public int DormantSlots;
        /// <summary>슬롯 경쟁에서 져서 슬롯 효과만 꺼진 칸 비트.</summary>
        public int SlotLostSlots;

        public readonly bool InstantLunge => LaunchSlotOwner >= 0;
    }

    /// <summary>증강의 후보 제시, 부여, 효과 계산, 트리거(CP23).</summary>
    public static class Augments
    {
        public static bool TryGet(SimConfig config, int id, out AugmentDef def)
        {
            var catalog = config.Augments;
            for (int i = 0; i < catalog.Length; i++)
            {
                if (catalog[i].Id == id)
                {
                    def = catalog[i];
                    return true;
                }
            }
            def = default;
            return false;
        }

        /// <summary>다른 칸(같은 증강의 중복 칸은 제외)의 Targets·Removes 합.</summary>
        static void OtherAspects(in AugmentState held, int slot, int id, SimConfig config, out AugmentAspect targets, out AugmentAspect removes)
        {
            targets = AugmentAspect.None;
            removes = AugmentAspect.None;
            for (int i = 0; i < held.Count; i++)
            {
                int other = held.Get(i);
                if (i == slot || other == id || !TryGet(config, other, out var d))
                    continue;
                targets |= d.Targets;
                removes |= d.Removes;
            }
        }

        public static AugmentEffects Compute(in AugmentState held, SimConfig config)
        {
            var e = new AugmentEffects { LungeSpeedMul = 1f, Reflectable = -1, LaunchSlotOwner = -1 };
            if (held.Count == 0)
                return e;
            // 휴면: 다른 증강이 이 증강의 수정 대상을 없애면 효과가 없다.
            for (int i = 0; i < held.Count; i++)
            {
                int id = held.Get(i);
                if (!TryGet(config, id, out var d))
                {
                    e.DormantSlots |= 1 << i;
                    continue;
                }
                OtherAspects(held, i, id, config, out _, out var removes);
                if ((d.Targets & removes) != 0)
                    e.DormantSlots |= 1 << i;
            }
            int flagPriority = int.MinValue, flagId = int.MaxValue;
            int slotPriority = int.MinValue, slotId = int.MaxValue;
            for (int i = 0; i < held.Count; i++)
            {
                if ((e.DormantSlots & (1 << i)) != 0)
                    continue;
                TryGet(config, held.Get(i), out var d);
                // ① 가산
                AddStat(ref e, d.AddStat, d.Add);
                // ② 배율(중복 칸마다 한 번씩 곱해 거듭제곱)
                if (d.MulStat == AugmentStat.LungeSpeed)
                    e.LungeSpeedMul *= d.Mul;
                // ③ 플래그: Priority가 높은 쪽, 같으면 Id가 작은 쪽
                if (d.Flag == AugmentFlag.ProjectileReflectable && Wins(d.Priority, d.Id, flagPriority, flagId))
                {
                    flagPriority = d.Priority;
                    flagId = d.Id;
                    e.Reflectable = d.FlagValue ? 1 : 0;
                }
                // ④ 슬롯: 하나만 활성
                if (d.Slot == AugmentSlot.ScissorsLaunch && Wins(d.Priority, d.Id, slotPriority, slotId))
                {
                    slotPriority = d.Priority;
                    slotId = d.Id;
                    e.LaunchSlotOwner = d.Id;
                }
            }
            // 슬롯에서 진 칸 표시(진 쪽의 다른 효과는 위에서 이미 적용됐다).
            for (int i = 0; i < held.Count; i++)
            {
                if ((e.DormantSlots & (1 << i)) != 0 || !TryGet(config, held.Get(i), out var d))
                    continue;
                if (d.Slot != AugmentSlot.None && d.Id != e.LaunchSlotOwner)
                    e.SlotLostSlots |= 1 << i;
            }
            return e;
        }

        static void AddStat(ref AugmentEffects e, AugmentStat stat, float add)
        {
            int n = SimMath.RoundToInt(add);
            switch (stat)
            {
                case AugmentStat.LungeCost: e.LungeCostAdd += n; break;
                case AugmentStat.ShotCost: e.ShotCostAdd += n; break;
                case AugmentStat.SwingCost: e.SwingCostAdd += n; break;
                case AugmentStat.ShotStartup: e.ShotStartupAdd += n; break;
            }
        }

        static bool Wins(int priority, int id, int bestPriority, int bestId) =>
            priority > bestPriority || (priority == bestPriority && id < bestId);

        /// <summary>
        /// 후보(10 문서 5.3 필터): 대상 폼을 가졌고, 중복 상한 아래, 같은 슬롯을 다른 증강이 차지하지 않았고, 가진 증강과 무의미 조합이 아닌 것. Id 순.
        /// </summary>
        public static void AddCandidates(in FighterState f, SimConfig config, ref CandidateList list, ref int count)
        {
            if (f.Augments.Count >= AugmentState.Capacity)
                return;
            AugmentAspect heldTargets = AugmentAspect.None, heldRemoves = AugmentAspect.None;
            for (int i = 0; i < f.Augments.Count; i++)
            {
                if (TryGet(config, f.Augments.Get(i), out var h))
                {
                    heldTargets |= h.Targets;
                    heldRemoves |= h.Removes;
                }
            }
            var catalog = config.Augments;
            for (int k = 0; k < catalog.Length; k++)
            {
                var d = catalog[k];
                if (d.Target != AugmentTarget.Common && !Forms.Owns(f.OwnedForms, (FighterForm)(int)d.Target))
                    continue;
                int stacks = f.Augments.Stacks(d.Id);
                if (stacks >= SimMath.Max(1, d.MaxStacks))
                    continue;
                if (stacks == 0)
                {
                    if (d.Slot != AugmentSlot.None && HoldsSlot(f.Augments, d.Slot, config))
                        continue;
                    if ((d.Removes & heldTargets) != 0 || (d.Targets & heldRemoves) != 0)
                        continue;
                }
                list.Add(PickOption.AugmentBase + d.Id, ref count);
            }
        }

        static bool HoldsSlot(in AugmentState held, AugmentSlot slot, SimConfig config)
        {
            for (int i = 0; i < held.Count; i++)
            {
                if (TryGet(config, held.Get(i), out var d) && d.Slot == slot)
                    return true;
            }
            return false;
        }

        public static void Grant(ref FighterState f, int id, SimConfig config) => f.Augments.Add(id);

        /// <summary>
        /// 사건에 반응하는 트리거(④): 고정 순서 Priority 높은 순 → Id 작은 순. 한 틱 안에서 트리거가 새 트리거를 일으키지 않는다(연쇄 깊이 상한 1).
        /// 지금 효과는 에너지 환급뿐이다. 환급은 OnEnergyRefund 사건이지만 깊이 상한 때문에 여기서 다시 처리하지 않는다.
        /// </summary>
        public static void RunTriggers(ref FighterState f, int index, AugmentTrigger trigger, SimConfig config, int tick, SimEventBuffer events)
        {
            var e = Compute(f.Augments, config);
            int used = 0;
            for (int n = 0; n < f.Augments.Count; n++)
            {
                // 아직 쓰지 않은 칸 중 다음 순서: Priority 높은 순, Id 작은 순, 칸 번호 작은 순.
                int pick = -1, bestPriority = int.MinValue, bestId = int.MaxValue;
                for (int i = 0; i < f.Augments.Count; i++)
                {
                    if ((used & (1 << i)) != 0 || (e.DormantSlots & (1 << i)) != 0 || !TryGet(config, f.Augments.Get(i), out var d) || d.Trigger != trigger)
                        continue;
                    if (pick < 0 || Wins(d.Priority, d.Id, bestPriority, bestId))
                    {
                        pick = i;
                        bestPriority = d.Priority;
                        bestId = d.Id;
                    }
                }
                if (pick < 0)
                    break;
                used |= 1 << pick;
                TryGet(config, f.Augments.Get(pick), out var chosen);
                Fire(ref f, index, chosen, config, tick, events);
            }
        }

        static void Fire(ref FighterState f, int index, in AugmentDef d, SimConfig config, int tick, SimEventBuffer events)
        {
            if (d.Trigger == AugmentTrigger.OnMeleeHit && config.EnergyEnabled)
                f.Energy = SimMath.Min(config.Combat.Energy.Max, f.Energy + SimMath.RoundToInt(d.TriggerValue));
            events.Add(new SimEvent(tick, SimEventType.AugmentActive, index, f.Position, Vec2.Zero, d.Id));
        }
    }
}
