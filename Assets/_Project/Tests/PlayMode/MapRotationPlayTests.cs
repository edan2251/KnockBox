using System.Collections;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>CP12: 라운드마다 맵이 바뀌면 연출(테마, 배경, 카메라)이 새 맵으로 다시 그려진다. F2 선택지.</summary>
    public class MapRotationPlayTests
    {
        [UnityTest]
        public IEnumerator RoundChange_RebuildsArenaThemeAndCamera()
        {
            yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Single);
            yield return null;
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            var presenter = Object.FindFirstObjectByType<MatchPresenter>();

            string[] choices = runner.ArenaChoices();
            Assert.AreEqual(MatchRunner.RotationName, choices[0], "F2 첫 선택지는 맵 순환");
            CollectionAssert.AreEqual(new[] { "Chimes", "Gantry", "Mobile", "Ruins", "Scales", "Totems" }, runner.RotationNames());
            CollectionAssert.IsSubsetOf(new[] { "Box", "Pit", "Towers" }, choices, "v1 맵은 F2로 고를 수 있음");

            runner.ArenaName = MatchRunner.RotationName;
            runner.Seed = 7;
            runner.StartNewMatch();
            runner.Running = false;
            yield return null;
            yield return null;
            var first = runner.World.Arena;
            Assert.AreEqual(6, runner.World.ArenaCount);
            Assert.AreEqual(18f, presenter.CameraRig.ArenaHalfWidth, "카메라가 36 × 20 맵에 맞춤");
            string firstTheme = presenter.CurrentTheme.name;
            Assert.AreEqual(presenter.ThemeIndex(first), runner.GetMeta(first.Name).Theme);

            // 라운드를 끝낸 것처럼 RoundTransition으로 두고 한 틱: 다음 맵의 Countdown.
            var s = runner.World.State;
            s.Match.SetPhase(MatchPhase.RoundTransition);
            runner.World.State = s;
            runner.StepOnce();
            yield return null;
            yield return null;
            var second = runner.World.Arena;
            Assert.AreNotEqual(first.Name, second.Name, "라운드가 바뀌면 맵도 바뀜");
            Assert.AreEqual(MatchPhase.Countdown, runner.World.State.Match.Phase);
            Assert.AreEqual(runner.GetMeta(second.Name).Theme, presenter.ThemeIndex(second));
            if (runner.GetMeta(first.Name).Theme != runner.GetMeta(second.Name).Theme)
                Assert.AreNotEqual(firstTheme, presenter.CurrentTheme.name, "새 맵의 테마로 다시 그림");
            Assert.AreEqual(runner.GetMeta(second.Name).Light, presenter.Background.LightPosition, "새 맵의 광원");
            TestContext.WriteLine($"[Knockbox][CP12-2] 연출 다시 그림: {first.Name}({firstTheme}) → {second.Name}({presenter.CurrentTheme.name})");
        }
    }
}
