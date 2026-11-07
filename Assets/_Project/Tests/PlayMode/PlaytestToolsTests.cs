using System.Collections;
using System.Text.RegularExpressions;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CP9 비교 빌드 도구: F9·F10 입력 기록과 재생, F11 튜닝 프리셋, F2 맵, `~` 도움말.
    /// 단축키가 부르는 MatchRunner·MatchPresenter 기능을 Sandbox 씬에서 직접 확인한다. Unity 창을 앞으로 가져와 실행한다(D-015).
    /// </summary>
    public class PlaytestToolsTests
    {
        MatchRunner runner;
        MatchPresenter presenter;

        IEnumerator LoadSandbox()
        {
            yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Single);
            yield return null;
            runner = Object.FindFirstObjectByType<MatchRunner>();
            presenter = Object.FindFirstObjectByType<MatchPresenter>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(presenter);
        }

        /// <summary>틱을 직접 진행하면서 가끔 프레임을 넘겨 연출도 같이 돈다.</summary>
        IEnumerator Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                runner.StepOnce();
                if (i % 20 == 0)
                    yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RecordThenReplay_SameFinalChecksum_EvenAfterSettingsChange()
        {
            yield return LoadSandbox();
            runner.ArenaName = "Towers";
            runner.Mode = CombatMode.Swing;
            runner.GrabEnabled = true;
            runner.CyclePreset();
            Assert.AreEqual("fast", runner.PresetName);
            runner.StartNewMatch();
            runner.Running = false;
            runner.Player1Input = new DummyInputSource(DummyBehavior.Random, 0x51u);
            runner.Player2Input = new DummyInputSource(DummyBehavior.Random, 0x52u);
            runner.StartRecording(runner.Mode.ToString());
            Assert.IsNotNull(runner.Recording, "경기 시작 직후에는 기록을 시작할 수 있어야 함");
            yield return Step(1200);
            uint recorded = runner.World.ComputeChecksum();
            var recording = InputRecording.FromBytes(runner.StopRecording().ToBytes());
            Assert.AreEqual(1200, recording.Count);
            Assert.AreEqual(recorded, recording.FinalChecksum);

            // 프리셋·모드·잡기·맵을 바꿔 둬도 기록과 같은 설정을 찾아 처음부터 재생한다.
            runner.CyclePreset();
            Assert.AreEqual("default", runner.PresetName);
            runner.Mode = CombatMode.Charge;
            runner.GrabEnabled = false;
            runner.ArenaName = "Box";
            runner.StartNewMatch();
            Assert.IsTrue(runner.PlayRecording(recording));
            runner.Running = false;
            Assert.AreEqual(CombatMode.Swing, runner.World.Config.Mode);
            Assert.IsTrue(runner.World.Config.GrabEnabled);
            Assert.AreEqual("Towers", runner.World.Arena.Name);
            Assert.AreEqual(recording.ConfigChecksum, runner.World.Config.ComputeChecksum());
            LogAssert.Expect(LogType.Log, new Regex("최종 체크섬이 기록과 같음"));
            yield return Step(recording.Count);
            Assert.AreEqual(recorded, runner.World.ComputeChecksum());
            Assert.IsNotNull(runner.Playback);

            // 새 경기는 재생을 끝낸다.
            runner.StartNewMatch();
            Assert.IsNull(runner.Playback);
        }

        [UnityTest]
        public IEnumerator HelpOverlay_ListsEveryHotkey_AndToggles()
        {
            yield return LoadSandbox();
            runner.StartNewMatch();
            yield return null;
            var help = presenter.Help;
            Assert.IsNotNull(help);
            Assert.IsFalse(help.Visible, "에디터에서는 닫힌 채 시작(빌드에서만 처음에 열림)");
            foreach (string key in new[] { "F1 ", "F2 ", "F3 ", "F4 ", "F5 ", "F6 ", "F7 ", "F8 ", "F9 ", "F10 ", "F11 ", "F12 ", "[ ]", "P pause", "O step", "~ close" })
                StringAssert.Contains(key, HelpOverlay.Text);
            foreach (string control in new[] { "WASD", "SPACE", "LSHIFT", "ARROWS", "NUM0", "NUM4" })
                StringAssert.Contains(control, HelpOverlay.Text);

            help.Visible = true;
            yield return null;
            // 판과 글자가 화면(직교 크기 9, 16:9) 안에 들어온다.
            var camera = Camera.main;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * 16f / 9f;
            foreach (var r in help.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                Assert.LessOrEqual(b.max.x - camera.transform.position.x, halfWidth, r.name);
                Assert.GreaterOrEqual(b.min.x - camera.transform.position.x, -halfWidth, r.name);
                Assert.LessOrEqual(b.max.y - camera.transform.position.y, halfHeight, r.name);
                Assert.GreaterOrEqual(b.min.y - camera.transform.position.y, -halfHeight, r.name);
            }
            help.Visible = false;
            yield return null;
            Assert.IsFalse(help.Visible);
        }

        [UnityTest]
        public IEnumerator BotOpponent_FightsBackAsP2()
        {
            yield return LoadSandbox();
            runner.Mode = CombatMode.Swing;
            runner.ArenaName = "Box";
            runner.StartNewMatch();
            runner.Running = false;
            var bot = new BotInputSource(() => runner.World, 1, 0xB07u);
            runner.Player1Input = new DummyInputSource(DummyBehavior.WalkBackAndForth);
            runner.Player2Input = bot;
            Assert.AreEqual("bot", MatchPresenter.InputSourceName(bot));
            int botAttacks = 0, hits = 0;
            for (int i = 0; i < 1200; i++)
            {
                runner.StepOnce();
                for (int e = 0; e < runner.World.Events.Count; e++)
                {
                    var ev = runner.World.Events[e];
                    botAttacks += ev.Type == SimEventType.AttackStart && ev.Fighter == 1 ? 1 : 0;
                    hits += ev.Type == SimEventType.Hit ? 1 : 0;
                }
                if (i % 20 == 0)
                    yield return null;
            }
            TestContext.WriteLine($"봇 공격 {botAttacks}, 명중 {hits}");
            Assert.Greater(botAttacks, 5, "봇이 직접 공격해야 함");
            Assert.Greater(hits, 0);
        }

        [UnityTest]
        public IEnumerator AudioListener_HasOutputLimiter()
        {
            yield return LoadSandbox();
            var listener = Object.FindFirstObjectByType<AudioListener>();
            Assert.IsNotNull(listener);
            Assert.IsNotNull(listener.GetComponent<Knockbox.Presentation.OutputLimiter>(), "최종 출력 리미터가 AudioListener 오브젝트에 있어야 함(F-2)");
        }

        [UnityTest]
        public IEnumerator ArenaCycle_StartsMatchOnEachMap()
        {
            yield return LoadSandbox();
            foreach (string map in new[] { "Box", "Towers", "Pit" })
            {
                runner.ArenaName = map;
                runner.StartNewMatch();
                yield return null;
                Assert.AreEqual(map, runner.World.Arena.Name);
            }
        }
    }
}
