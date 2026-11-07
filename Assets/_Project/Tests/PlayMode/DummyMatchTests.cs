using System.Collections;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CP5-9·CP6-8: 무작위 허수아비 두 명이 모드 A·B로 각각 1분(3600틱) 자동 대전한다. 연출을 켠 상태로 진행하며
    /// 콘솔 오류 없이 라운드가 진행되는지 보고, 평균 라운드 길이와 클래시 빈도를 기록한다.
    /// </summary>
    public class DummyMatchTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator RandomDummies_OneMinuteMatch([Values(CombatMode.Charge, CombatMode.Swing)] CombatMode mode)
        {
            root = new GameObject("Match");
            var runner = root.AddComponent<MatchRunner>();
            root.AddComponent<MatchPresenter>();
            yield return null;

            runner.Mode = mode;
            runner.StartNewMatch();
            runner.Player1Input = new DummyInputSource(DummyBehavior.Random, 0xA11Cu);
            runner.Player2Input = new DummyInputSource(DummyBehavior.Random, 0xB0B5u);
            runner.Running = false;

            const int Ticks = 3600;
            int rounds = 0, clashes = 0, hits = 0, attacks = 0, roundStartTick = 0, roundTicks = 0;
            for (int t = 0; t < Ticks; t++)
            {
                runner.StepOnce();
                var events = runner.World.Events;
                for (int i = 0; i < events.Count; i++)
                {
                    switch (events[i].Type)
                    {
                        case SimEventType.Clash: clashes++; break;
                        case SimEventType.Hit: hits++; break;
                        case SimEventType.LungeStart:
                        case SimEventType.AttackStart:
                            attacks++;
                            break;
                        case SimEventType.RoundStart: roundStartTick = runner.World.State.Tick; break;
                        case SimEventType.RoundEnd:
                            // 라운드 길이는 GO부터 KO까지(KO 단계 60틱은 뺀다).
                            rounds++;
                            roundTicks += runner.World.State.Tick - runner.World.Config.Match.KoTicks - roundStartTick;
                            break;
                    }
                }
                yield return null;
            }

            var s = runner.World.State;
            string average = rounds > 0 ? $"{roundTicks / (float)rounds / 60f:F1}초" : "끝난 라운드 없음";
            string label = mode == CombatMode.Charge ? "CP5-9" : "CP6-8";
            string report = $"[Knockbox][{label}] 무작위 허수아비 {mode} {Ticks}틱: 끝난 라운드 {rounds}(평균 {average}), 현재 {s.Match.Round}라운드 {s.Match.Phase}, " +
                            $"점수 {s.Match.Score0}:{s.Match.Score1}, 체력 {s.Fighter0.Health}/{s.Fighter1.Health}, 공격 {attacks}, 명중 {hits}, 클래시 {clashes}(분당 {clashes * 3600f / Ticks:F1})";
            Debug.Log(report);
            TestContext.WriteLine(report);
            Assert.AreEqual(Ticks, s.Tick, "모든 틱이 진행돼야 함");
            Assert.Greater(attacks, 0);
        }
    }
}
