using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP24-4 (다) D-057: 실제 경기 흐름에서 선택 4번을 거쳐 경기 끝까지 가고, 재시작 뒤 초기화된다.
    /// 지는 쪽(P1)은 싸우지 않고 가까운 경계로 계속 대시해 경계 튕김 피해를 받는다(선택 단계에서는 번호를 누름). 이기는 쪽(P2)은 봇이다.
    /// 체력·KO는 실제 시뮬레이션으로 정해진다.
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class FullFlowTests
    {
        [TestCase(241u)]
        [TestCase(242u)]
        public void ForcedSetLosses_FourPicks_ToMatchEnd_ThenRestartResets(uint seed)
        {
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = FeatureSwitches.Default });
            string[] maps = { "Chimes", "Gantry", "Mobile", "Ruins", "Scales", "Totems" };
            var arenas = new ArenaDef[maps.Length];
            for (int i = 0; i < maps.Length; i++)
                arenas[i] = ArenaFiles.LoadByName(maps[i]);
            var w = new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, seed, MatchPhase.FormSelect));
            var winner = new BotInputSource(() => w, 1, 900u + seed);
            int p1Option = 0;
            bool ended = false;
            int t = 0;
            for (; t < 60 * 60 * 60 && !ended; t++)
            {
                int tick = w.State.Tick;
                ref readonly var m = ref w.State.Match;
                bool p1Chooses = m.Phase == MatchPhase.FormSelect && m.FormChoice0 < 0 || m.Phase == MatchPhase.SetPick && m.Picker == 0 && m.PickedAt < 0;
                // P1은 고를 때마다 다른 번호를 눌러 폼 획득과 증강이 섞이게 한다.
                InputFrame p1;
                if (p1Chooses)
                    p1 = (tick & 1) == 0 ? new InputFrame(0, 0, MatchFlow.OptionButton(p1Option % System.Math.Max(1, m.Phase == MatchPhase.SetPick ? m.OptionCount : 3))) : default;
                else
                {
                    // 가까운 옆 경계 쪽으로 달리며 대시·점프로 부딪힌다.
                    int side = w.State.Fighter0.Position.X >= 0f ? 1 : -1;
                    var b = tick % 24 == 0 ? InputButtons.Dash : tick % 24 == 12 ? InputButtons.Jump : InputButtons.None;
                    p1 = new InputFrame(side, 0, b);
                }
                w.Step(p1, winner.Poll(tick));
                for (int i = 0; i < w.Events.Count; i++)
                {
                    if (w.Events[i].Type == SimEventType.Picked && w.Events[i].Fighter == 0)
                        p1Option++;
                    ended |= w.Events[i].Type == SimEventType.MatchEnd;
                }
            }
            var s = w.State;
            TestContext.WriteLine($"시드 {seed}: {t / 3600f:F1}분, 라운드 {s.Match.Round}, 세트 {s.Match.SetScore0}:{s.Match.SetScore1}, 선택 {s.Match.Picks0}/{s.Match.Picks1}, "
                                  + $"P1 보유 폼 {s.Fighter0.OwnedForms}, 증강 {s.Fighter0.Augments.Count}");
            Assert.IsTrue(ended, "경기 끝까지 감");
            Assert.AreEqual(config.Match.SetsToWinMatch, s.Match.SetScore1, "상대가 5세트");
            Assert.AreEqual(config.Match.MaxPicks, s.Match.Picks0, "지는 쪽 선택 4번");
            Assert.AreEqual(0, s.Match.Picks1, "이기는 쪽은 선택 없음");
            int gained = System.Math.Max(0, CountBits(s.Fighter0.OwnedForms) - 1) + s.Fighter0.Augments.Count;
            Assert.AreEqual(config.Match.MaxPicks, gained, "선택 4번이 폼·증강으로 모두 반영(경기 끝까지 누적)");

            // 재시작 → 초기화.
            for (int k = 0; k <= config.Match.MatchEndMinTicks + 2 && w.State.Match.Phase == MatchPhase.MatchEnd; k++)
                w.Step((k & 1) == 0 ? new InputFrame(0, 0, InputButtons.Jump) : default, default);
            var r = w.State;
            Assert.AreEqual(MatchPhase.FormSelect, r.Match.Phase);
            Assert.AreEqual(0, r.Match.Picks0 + r.Match.SetScore1 + r.Fighter0.Augments.Count);
            Assert.AreEqual(1, CountBits(r.Fighter0.OwnedForms), "보유 폼은 시작 폼 하나");
        }

        static int CountBits(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1)
                n++;
            return n;
        }
    }
}
