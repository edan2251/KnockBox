using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// CR-004 D/E 비교용 가벼운 카운터(4절): 표면에 붙어 있는 시간 비율, 멀리 떨어진(쫓는) 시간 비율, 붙은 상태에서 시작한 박치기·잡기 수.
    /// 표면 이동을 켠 실험 경기에서만, 라운드가 끝날 때 로그 한 줄을 남긴다. 따로 측정 실행은 하지 않는다.
    /// </summary>
    public sealed class SurfaceMetrics
    {
        /// <summary>이보다 멀면 "쫓는 시간"으로 센다.</summary>
        const float ChaseDistance = 8f;

        int fighting;
        readonly int[] attached = new int[2];
        int far;
        int lungesFromSurface;
        int grabsFromSurface;

        public void OnTicked(MatchRunner runner)
        {
            var world = runner.World;
            if (world == null || !runner.BrawlExperiment || world.Config.Combat.Body.SurfaceMode == 0)
                return;
            var s = world.State;
            var previous = runner.PreviousState;
            if (s.Match.Phase == MatchPhase.Fighting)
            {
                fighting++;
                for (int k = 0; k < 2; k++)
                {
                    if (s.GetFighter(k).Surface != FighterState.SurfaceNone)
                        attached[k]++;
                }
                if (SimMath.Length(s.Fighter0.Position - s.Fighter1.Position) > ChaseDistance)
                    far++;
            }
            bool roundOver = false;
            for (int i = 0; i < world.Events.Count; i++)
            {
                ref readonly var e = ref world.Events[i];
                if (e.Type == SimEventType.RoundTransition)
                    roundOver = true;
                if (e.Fighter < 0 || e.Fighter > 1 || previous.GetFighter(e.Fighter).Surface == FighterState.SurfaceNone)
                    continue;
                if (e.Type == SimEventType.LungeStart)
                    lungesFromSurface++;
                else if (e.Type == SimEventType.GrabStart)
                    grabsFromSurface++;
            }
            if (!roundOver || fighting == 0)
                return;
            string mode = world.Config.Combat.Body.SurfaceMode == 1 ? "D" : "E";
            Debug.Log($"[Knockbox][surface] mode {mode}, {fighting} ticks: attached P1 {Percent(attached[0])} P2 {Percent(attached[1])}, " +
                      $"far(>{ChaseDistance}) {Percent(far)}, lunges from surface {lungesFromSurface}, grabs from surface {grabsFromSurface}");
            fighting = far = lungesFromSurface = grabsFromSurface = 0;
            attached[0] = attached[1] = 0;
        }

        string Percent(int ticks) => $"{100f * ticks / fighting:0}%";
    }
}
