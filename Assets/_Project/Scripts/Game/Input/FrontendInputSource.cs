using System;
using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>UI 클릭을 기존 선택 버튼 비트로 전달한다. 온라인도 락스텝에 입력이 확정된 뒤에만 획득한다.</summary>
    public sealed class FrontendInputSource : IInputSource
    {
        readonly Func<SimWorld> world;
        readonly Func<bool> blocked;
        readonly IInputSource combat;
        readonly int player;
        readonly bool automaticPick;
        int queued = -1, queuedRound = -1, observedRound = -1;
        MatchPhase observedPhase = MatchPhase.MatchEnd;
        InputButtons awaitingRelease;
        SimWorld readyWorld;
        int readyRound = -1;
        public IInputSource Combat => combat;

        public FrontendInputSource(Func<SimWorld> world, Func<bool> blocked, IInputSource combat, int player, bool automaticPick = false)
        {
            this.world = world; this.blocked = blocked; this.combat = combat;
            this.player = player; this.automaticPick = automaticPick;
        }

        public bool Choose(int option)
        {
            var w = world();
            if (w == null || !w.Config.Match.CollectCardsOnly || w.State.Match.Phase != MatchPhase.SetPick
                || w.State.Match.Picker != player || w.State.Match.PickedAt >= 0
                || option < 0 || option >= w.State.Match.OptionCount || queued >= 0) return false;
            queued = option;
            queuedRound = w.State.Match.Round;
            return true;
        }

        public InputFrame Poll(int tick)
        {
            var w = world();
            if (w == null || blocked()) { observedRound = -1; queued = -1; readyRound = -1; readyWorld = null; return default; }
            if (!w.Config.Match.CollectCardsOnly) return combat?.Poll(tick) ?? default;
            var m = w.State.Match;
            bool entered = observedPhase != m.Phase || observedRound != m.Round;
            observedPhase = m.Phase; observedRound = m.Round;
            if (m.Phase == MatchPhase.MatchEnd)
            {
                queued = -1;
                // O 클릭은 기존 Attack 홀드로 유지한다. 확정된 양쪽 PrevButtons가 준비 표시와 재시작의 근거다.
                // 전투 중 누른 마우스/키는 결과 화면을 넘길 수 없다.
                // 오프라인은 어느 한쪽 입력만으로 재시작하므로 연습 봇이 자동 준비하면 사용자의 O를 건너뛴다.
                if (automaticPick && w.Config.Match.RematchBothHold && m.PhaseTick >= w.Config.Match.MatchEndMinTicks + 15) ReadyForRematch();
                return readyWorld == w && readyRound == m.Round && m.PhaseTick >= w.Config.Match.MatchEndMinTicks
                    ? new InputFrame(0, 0, InputButtons.Attack) : default;
            }
            readyRound = -1; readyWorld = null;
            if (m.Phase != MatchPhase.SetPick)
            {
                queued = -1;
                if (m.Phase != MatchPhase.Fighting && m.Phase != MatchPhase.MatchEnd) return default;
                var frame = combat?.Poll(tick) ?? default;
                // 화면을 넘길 때 누르고 있던 버튼은 실제로 뗀 뒤 다시 눌러야 한다.
                if (entered) { awaitingRelease = frame.Buttons; return default; }
                awaitingRelease &= frame.Buttons;
                frame.Buttons &= ~awaitingRelease;
                return frame;
            }
            if (entered) return default;
            if (m.Picker != player || m.PickedAt >= 0) { queued = -1; return default; }
            int automaticAt = CardCollectionRules.HasCountIn(w.Config.Match) ? 120 : 45;
            if (automaticPick && m.PhaseTick >= automaticAt && queued < 0) Choose(m.GetPicks(player) % m.OptionCount);
            if (queued < 0 || queuedRound != m.Round) return default;
            int choice = queued;
            queued = -1;
            return new InputFrame(0, 0, MatchFlow.OptionButton(choice));
        }

        public bool ReadyForRematch()
        {
            var w = world();
            if (w == null || blocked() || !w.Config.Match.CollectCardsOnly || w.State.Match.Phase != MatchPhase.MatchEnd
                || w.State.Match.PhaseTick < w.Config.Match.MatchEndMinTicks || (readyWorld == w && readyRound == w.State.Match.Round)) return false;
            readyWorld = w; readyRound = w.State.Match.Round;
            return true;
        }
    }
}
