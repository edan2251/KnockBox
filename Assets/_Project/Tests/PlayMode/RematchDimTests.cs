using System.Collections;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Knockbox.Tests.PlayMode
{
    public class RematchDimTests
    {
        GameObject root;
        MatchRunner runner;
        FrontendView view;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return PlayModeSceneScope.OpenEmpty();
            root = new GameObject("Rematch dim test"); root.SetActive(false);
            root.AddComponent<AudioListener>();
            runner = root.AddComponent<MatchRunner>(); runner.BrawlExperiment = true; runner.Rules = RuleSet.Brawl;
            root.AddComponent<MatchPresenter>(); root.AddComponent<OnlineMatch>(); root.AddComponent<OnlineLobby>();
            var controller = root.AddComponent<FrontendController>(); root.SetActive(true);
            yield return null; yield return null;
            controller.enabled = false; runner.Running = false; view = controller.View;
            runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
            runner.World.State.Match.PhaseTick = 240;
            view.Show(FrontendScreen.Result); view.Result(0, 5, 2, true, false, false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() { Object.Destroy(root); yield return null; }

        [UnityTest]
        public IEnumerator UniformOverlayCoversWholeActorsAndNameplates_BelowRematchControls_AndMatchesDiagonal()
        {
            var dim = view.RematchDim;
            Assert.IsNotNull(dim); Assert.IsTrue(dim.gameObject.activeInHierarchy);
            Assert.IsFalse(dim.raycastTarget, "O/X 클릭을 가로채지 않는다");
            Assert.AreEqual(0, dim.thickness); Assert.AreEqual(Color.clear, dim.line);
            Assert.AreEqual(new Vector2(1920, 1080), dim.rectTransform.sizeDelta);
            var duel = view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards"));
            var split = duel.Find(Knockbox.Presentation.SceneObjectName.Path("White_divider")).GetComponent<FrontendGraphic>();
            foreach (var behind in new[] { "White_divider", "Shared_portrait_framing", "P1_name", "P2_name", "Selecting_player_s_hand" })
                Assert.Less(duel.Find(Knockbox.Presentation.SceneObjectName.Path(behind)).GetSiblingIndex(), dim.transform.GetSiblingIndex(), behind + " 위에 덮개 배치");
            foreach (var ahead in new[] { "Winner", "Set_score", "Rematch", "Ready_for_rematch", "Return_to_menu" })
                Assert.Greater(duel.Find(Knockbox.Presentation.SceneObjectName.Path(ahead)).GetSiblingIndex(), dim.transform.GetSiblingIndex(), ahead + " 가독성 유지");
            for (int i = 0; i <= 10; i++)
            {
                Assert.AreEqual(split.LeftEdge(i / 10f), dim.LeftEdge(i / 10f), .00001f);
                Assert.AreEqual(split.RightEdge(i / 10f), dim.RightEdge(i / 10f), .00001f);
            }
            AssertOpaqueActors();
            Assert.AreEqual(.98f, dim.left.a, .0001f); Assert.AreEqual(.98f, dim.right.a, .0001f);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReadyClearsOnlyThatSide_WithoutChangingSourceColorsOrSimulation_AndShowResetsOverlay()
        {
            var duel = view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards"));
            var split = duel.Find(Knockbox.Presentation.SceneObjectName.Path("White_divider")).GetComponent<FrontendGraphic>();
            var left = split.left; var right = split.right;
            var bodyColors = new[] { view.Portrait(0).Find("Body").GetComponent<Image>().color,
                view.Portrait(1).Find("Body").GetComponent<Image>().color };
            uint checksum = runner.World.ComputeChecksum();
            foreach (int winner in new[] { 0, 1 })
            foreach (int mask in new[] { 0, 1, 2, 3 })
            {
                bool ready0 = (mask & 1) != 0, ready1 = (mask & 2) != 0;
                view.Result(winner, winner == 0 ? 5 : 2, winner == 1 ? 5 : 2, true, ready0, ready1);
                yield return null;
                Assert.AreEqual(ready0 ? 0 : .98f, view.RematchDim.left.a, .0001f);
                Assert.AreEqual(ready1 ? 0 : .98f, view.RematchDim.right.a, .0001f);
                Assert.AreEqual(left, split.left); Assert.AreEqual(right, split.right);
                AssertOpaqueActors();
                for (int p = 0; p < 2; p++) Assert.AreEqual(bodyColors[p], view.Portrait(p).Find("Body").GetComponent<Image>().color);
                Assert.AreEqual(checksum, runner.World.ComputeChecksum(), "준비 강조는 표시만 변경");
            }
            foreach (var screen in new[] { FrontendScreen.Main, FrontendScreen.Versus, FrontendScreen.Cards, FrontendScreen.Hidden })
            {
                view.Show(screen);
                Assert.IsFalse(view.RematchDim.gameObject.activeSelf, "다른 화면에 결과 덮개가 남지 않는다");
                Assert.AreEqual(0, view.RematchDim.left.a); Assert.AreEqual(0, view.RematchDim.right.a);
                view.Show(FrontendScreen.Result); view.Result(0, 5, 2, true, false, false);
                yield return null;
                Assert.AreEqual(.98f, view.RematchDim.left.a, .0001f); Assert.AreEqual(.98f, view.RematchDim.right.a, .0001f);
            }
            LogAssert.NoUnexpectedReceived();
        }

        void AssertOpaqueActors()
        {
            for (int p = 0; p < 2; p++)
            {
                var portrait = view.Portrait(p);
                Assert.IsTrue(portrait.gameObject.activeInHierarchy);
                Assert.AreEqual(1, portrait.GetComponent<CanvasGroup>().alpha, "몸/눈을 개별 반투명으로 만들지 않는다");
                foreach (var name in new[] { "Body", "Outline", "Eye0", "Eye1" })
                    Assert.AreEqual(1, portrait.Find(Knockbox.Presentation.SceneObjectName.Path(name)).GetComponent<Image>().color.a, name + " 원래 불투명도 유지");
                var plate = view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/P" + (p + 1) + " name"));
                Assert.AreEqual(1, plate.GetComponent<CanvasGroup>().alpha);
                Assert.AreEqual(1, plate.GetComponent<FrontendGraphic>().color.a);
                Assert.AreEqual(1, plate.Find(Knockbox.Presentation.SceneObjectName.Path("Player")).GetComponent<Text>().color.a);
            }
        }
    }
}
