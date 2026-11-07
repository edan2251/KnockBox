using System.Collections;
using System.IO;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CP1-9(실시간 틱 속도)와 ALL-4(showcase 시나리오 실행 중 콘솔 오류 0).
    /// 에디터가 백그라운드면 플레이 모드가 멈추므로 Unity 창을 앞으로 가져와 실행한다(D-015).
    /// </summary>
    public class MatchRunnerTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.Destroy(root);
        }

        /// <summary>튜닝 에셋 없이(명세 초기값) 바로 경기를 시작하는 MatchRunner.</summary>
        MatchRunner CreateRunner()
        {
            root = new GameObject("Match");
            return root.AddComponent<MatchRunner>();
        }

        [UnityTest]
        public IEnumerator TenSecondsOfRealTime_Runs600Ticks()
        {
            var runner = CreateRunner();
            yield return null;

            long startTicks = runner.TicksRun;
            long startDropped = runner.Clock.DroppedTicks;
            double start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < 10.0)
                yield return null;
            double elapsed = Time.realtimeSinceStartupAsDouble - start;
            long ticks = runner.TicksRun - startTicks;
            long dropped = runner.Clock.DroppedTicks - startDropped;

            string report = $"[Knockbox][CP1-9] {elapsed:F4}초 동안 {ticks}틱 (기대 {elapsed * 60:F1}), 버린 틱 {dropped}, 프레임 {Time.frameCount}";
            Debug.Log(report);
            TestContext.WriteLine(report);
            Assert.That(ticks, Is.InRange(598, 602), report);
        }

        [UnityTest]
        public IEnumerator ShowcaseScenarios_RunWithoutErrors()
        {
            var runner = CreateRunner();
            var player = root.AddComponent<ScenarioPlayer>();
            player.Runner = runner;
            int played = 0;

            foreach (string path in ScenarioJson.ListFiles())
            {
                if (!Path.GetFileName(path).StartsWith("showcase_"))
                    continue;
                var def = ScenarioJson.LoadFile(path);
                player.Play(def);
                double deadline = Time.realtimeSinceStartupAsDouble + def.Ticks / 60.0 + 5.0;
                while (!player.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;

                Assert.IsTrue(player.Finished, $"{def.Name}: 제한 시간 안에 끝나지 않음 (틱 {runner.World.State.Tick}/{def.Ticks})");
                Assert.AreEqual(0, player.FailureCount, $"{def.Name}: expect 실패");
                Assert.AreEqual(def.Ticks, runner.World.State.Tick, "StopAtTick에서 정확히 멈춰야 함");

                // 플레이 모드 실행 결과가 화면 없는 실행과 같은 상태에 도달해야 한다.
                var headless = ScenarioRun.Run(def, ScenarioConfig.Create(def), ArenaFiles.LoadByName(def.Arena));
                Assert.AreEqual(headless.FinalChecksum, runner.World.ComputeChecksum(), $"{def.Name}: 플레이 모드와 헤드리스 결과가 다름");
                played++;
            }

            Assert.Greater(played, 0, "showcase 시나리오가 없음");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
