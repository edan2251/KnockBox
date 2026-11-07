using Knockbox.Simulation;

namespace Knockbox.Tests.EditMode
{
    // 싸움 봇(CombatBot)은 F6 상대·봇 영상과 같이 쓰도록 Knockbox.Game으로 옮겼다(Scripts/Game/Input/CombatBot.cs).

    /// <summary>한 경기 동안의 전투 이벤트 수.</summary>
    public struct CombatEventCounts
    {
        public int Hit, GuardHit, Parry, Clash, WallSplat, Bounce, KO, RoundTransition, MatchEnd, Spike;

        public void Add(SimEventBuffer events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                switch (events[i].Type)
                {
                    case SimEventType.Hit: Hit++; break;
                    case SimEventType.GuardHit: GuardHit++; break;
                    case SimEventType.Parry: Parry++; break;
                    case SimEventType.Clash: Clash++; break;
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
            $"명중 {Hit}, 가드 {GuardHit}, 패링 {Parry}, 클래시 {Clash}, 벽꽝 {WallSplat}, 튕김 {Bounce}, KO {KO}, 라운드 전환 {RoundTransition}, 경기 종료 {MatchEnd}, 가시 {Spike}";
    }
}
