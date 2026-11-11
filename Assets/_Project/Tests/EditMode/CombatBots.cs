using Knockbox.Simulation;

namespace Knockbox.Tests.EditMode
{
    // 폼 봇(CombatBot)은 2026-11-11 폼 전투 제거로 삭제했다. 테스트의 봇 대전은 몸 폼 봇(Knockbox.Game.BrawlBot)을 쓴다.

    /// <summary>한 경기 동안의 전투 이벤트 수.</summary>
    public struct CombatEventCounts
    {
        public int Hit, Clash, Grab, WallSplat, Bounce, KO, RoundTransition, MatchEnd, Spike;

        public void Add(SimEventBuffer events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                switch (events[i].Type)
                {
                    case SimEventType.Hit: Hit++; break;
                    case SimEventType.Clash: Clash++; break;
                    case SimEventType.Grab: Grab++; break;
                    case SimEventType.WallSplat: WallSplat++; break;
                    case SimEventType.BounceRebound: Bounce++; break;
                    case SimEventType.KO: KO++; break;
                    case SimEventType.RoundTransition: RoundTransition++; break;
                    case SimEventType.MatchEnd: MatchEnd++; break;
                    case SimEventType.Spike: Spike++; break;
                }
            }
        }

        public override string ToString() =>
            $"명중 {Hit}, 클래시 {Clash}, 잡기 {Grab}, 벽꽝 {WallSplat}, 튕김 {Bounce}, KO {KO}, 라운드 전환 {RoundTransition}, 경기 종료 {MatchEnd}, 가시 {Spike}";
    }
}
