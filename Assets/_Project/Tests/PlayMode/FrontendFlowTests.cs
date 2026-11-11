using System.Collections;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Knockbox.Tests.PlayMode
{
    public class FrontendFlowTests
    {
        GameObject root;
        MatchRunner runner;
        FrontendController controller;
        FrontendView View=>controller.View;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return PlayModeSceneScope.OpenEmpty();
            root=new GameObject("Frontend test");root.SetActive(false);
            root.AddComponent<AudioListener>();
            runner=root.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            root.AddComponent<MatchPresenter>();root.AddComponent<OnlineMatch>();root.AddComponent<OnlineLobby>();
            controller=root.AddComponent<FrontendController>();root.SetActive(true);
            yield return null;yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown(){Object.Destroy(root);yield return null;}

        Button ButtonAt(string path)=>View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path(path)).GetComponent<Button>();

        [UnityTest]
        public IEnumerator MainMenuHighlightFollowsOnlyPointer_NotInitialOrClickSelection_AndClearsOnReturn()
        {
            controller.enabled=false;
            var buttons=new[]{ButtonAt("Main_and_lobby/Menu/Create_room"),ButtonAt("Main_and_lobby/Menu/Join_room"),ButtonAt("Main_and_lobby/Menu/Practice")};
            var events=EventSystem.current;var input=events.currentInputModule;
            bool inputEnabled=input!=null && input.enabled;
            if(input!=null)input.enabled=false;
            // A real pointer can enter during the fixture's first two frames. Reset that transient
            // before driving explicit pointer events; production OnDisable also clears pressed/hover scale.
            foreach(var button in buttons){button.gameObject.SetActive(false);button.gameObject.SetActive(true);}
            var pointer=new PointerEventData(events){button=PointerEventData.InputButton.Left};
            try
            {
                foreach(int slot in new[]{0,1})
                {
                    View.SetLocalSlot(slot);View.Show(FrontendScreen.Main);yield return null;yield return null;
                    var fill=root.GetComponent<MatchPresenter>().Palette.FighterBody(slot);
                    AssertMenuHighlight(buttons,-1,fill);
                    events.SetSelectedGameObject(buttons[0].gameObject);yield return null;
                    AssertMenuHighlight(buttons,-1,fill); // 선택 포커스가 생겨도 포인터 밖은 강조하지 않는다.
                    foreach(var button in buttons)Assert.AreEqual(Vector3.one,button.transform.localScale);
                    for(int i=0;i<buttons.Length;i++)
                    {
                        ExecuteEvents.Execute(buttons[i].gameObject,pointer,ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(buttons[i].gameObject,pointer,ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(buttons[i].gameObject,pointer,ExecuteEvents.pointerUpHandler);
                        AssertMenuHighlight(buttons,i,fill);
                        Assert.AreEqual(buttons[i].gameObject,events.currentSelectedGameObject);
                        if(slot==0 && i==1)
                        {
                            yield return new WaitForSecondsRealtime(.8f);yield return new WaitForEndOfFrame();
                            var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
                            try
                            {
                                System.IO.File.WriteAllBytes(PlayModeSceneScope.CapturePath("main-join-hover.png"),screenshot.EncodeToPNG());
                            }
                            finally{Object.Destroy(screenshot);}
                        }
                        ExecuteEvents.Execute(buttons[i].gameObject,pointer,ExecuteEvents.pointerExitHandler);
                        AssertMenuHighlight(buttons,-1,fill); // 클릭 선택은 남아 있어도 하이라이트는 즉시 해제된다.
                    }
                    ExecuteEvents.Execute(buttons[0].gameObject,pointer,ExecuteEvents.pointerEnterHandler);
                    View.Show(FrontendScreen.Join);yield return null;yield return null;
                    View.Show(FrontendScreen.Main);yield return null;yield return null;
                    AssertMenuHighlight(buttons,-1,fill);
                    ExecuteEvents.Execute(buttons[2].gameObject,pointer,ExecuteEvents.pointerEnterHandler);
                    buttons[2].interactable=false;yield return null;yield return new WaitForEndOfFrame();
                    AssertMenuHighlight(buttons,-1,fill);buttons[2].interactable=true;
                    // 다음 슬롯의 초깃값 검사를 위해 버튼 Tween도 완전히 초기화한다.
                    View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Main);yield return null;
                }
            }
            finally{events.SetSelectedGameObject(null);if(input!=null)input.enabled=inputEnabled;}
            LogAssert.NoUnexpectedReceived();
        }

        void AssertMenuHighlight(Button[] buttons,int hovered,Color fill)
        {
            for(int i=0;i<buttons.Length;i++)
            {
                Assert.AreEqual(i==hovered?fill:Color.clear,buttons[i].image.color,buttons[i].name);
                Assert.AreEqual(i==hovered?View.Theme.ink:View.Theme.white,buttons[i].GetComponentInChildren<Text>().color);
            }
        }

        [UnityTest]
        public IEnumerator MainCharacterStaysFixedAcrossMainJoinAndWaiting_WhileMenusAnimate()
        {
            controller.enabled=false; // 방 만들기 네트워크 요청 없이 동일한 화면 전환 경로를 검사한다.
            foreach(int slot in new[]{0,1})
            {
                View.SetLocalSlot(slot);View.Show(FrontendScreen.Main);yield return null;
                var actor=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Actual P"+(slot+1))).GetComponent<RectTransform>();
                var position=actor.anchoredPosition;var rotation=actor.localRotation;var scale=actor.localScale;
                foreach(var screen in new[]{FrontendScreen.Join,FrontendScreen.Waiting,FrontendScreen.Main})
                {
                    View.Show(screen);yield return null;
                    var menu=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu")).GetComponent<RectTransform>();
                    var menuBefore=menu.anchoredPosition;
                    for(int frame=0;frame<18;frame++)
                    {
                        yield return null;
                        Assert.AreEqual(position,actor.anchoredPosition,"배경 캐릭터는 전환 중 고정");
                        Assert.AreEqual(rotation,actor.localRotation);Assert.AreEqual(scale,actor.localScale);
                        Assert.IsTrue(actor.gameObject.activeInHierarchy);
                    }
                    if(screen==FrontendScreen.Join || screen==FrontendScreen.Main)
                        Assert.Greater(Vector2.Distance(menuBefore,menu.anchoredPosition),.01f,"메뉴 애니메이션은 유지");
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        sealed class WaitingInputs : ILockstepInputs
        {
            public int LocalSlot=>0;
            public bool TryGetInputs(int tick,out InputFrame p1,out InputFrame p2){p1=p2=default;return false;}
        }

        sealed class RoutedInputs : ILockstepInputs
        {
            public int LocalSlot { get; set; }
            public IInputSource First, Second;
            public bool TryGetInputs(int tick,out InputFrame p1,out InputFrame p2)
            { p1=First.Poll(tick);p2=Second.Poll(tick);return true; }
        }

        Camera EnsureCamera()
        {
            if(Camera.main!=null)return Camera.main;
            var go=new GameObject("Fragment exit test camera",typeof(Camera));go.transform.SetParent(root.transform);
            go.tag="MainCamera";go.transform.position=new Vector3(0,0,-10);
            var camera=go.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=12;
            return camera;
        }

        void Until(MatchPhase phase)
        {
            for(int i=0;i<500&&runner.World.State.Match.Phase!=phase;i++)runner.StepOnce();
            Assert.AreEqual(phase,runner.World.State.Match.Phase);
        }

        [UnityTest]
        public IEnumerator WinnerBecomesPoint_OrdinaryRoundSkipsVs_BothFightersRebuildTogether()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1);
            Until(MatchPhase.Fighting);yield return null;
            var presenter=root.GetComponent<MatchPresenter>();
            var arena=root.GetComponentInChildren<ArenaView>();
            var positions=new Vector3[arena.transform.childCount];
            for(int i=0;i<positions.Length;i++)positions[i]=arena.transform.GetChild(i).localPosition;
            runner.World.State.Fighter1.Health=0;runner.StepOnce();
            for(int i=0;i<31;i++)runner.StepOnce();
            yield return null;
            Assert.IsTrue(presenter.Fighter(1).Shattered,"패자 사망 효과가 먼저");
            Assert.IsTrue(presenter.Fighter(0).Celebrating,"승자는 몸을 유지하고 점프");
            Assert.IsFalse(View.Points.Traveling,"사망을 읽을 시간 전에는 점으로 바뀌지 않음");
            Assert.AreEqual(1,presenter.Fighter(0).transform.localScale.x);
            for(int i=31;i<118;i++)runner.StepOnce();yield return null;
            Assert.IsTrue(View.Points.Traveling);
            Assert.Less(presenter.Fighter(0).transform.localScale.x,.01f,"실제 승리 캐릭터가 줄어들어 점으로 이어짐");
            uint before=runner.World.ComputeChecksum();presenter.RenderFrame();Assert.AreEqual(before,runner.World.ComputeChecksum());
            Until(MatchPhase.Countdown);yield return null;
            Assert.AreEqual(2,runner.World.State.Match.Round);
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen,"매 라운드 VS를 반복하지 않음");
            for(int i=0;i<27;i++)runner.StepOnce();yield return null;
            float scale=presenter.Fighter(0).transform.localScale.x;
            Assert.Greater(scale,.01f);Assert.Less(scale,1);
            Assert.AreEqual(presenter.Fighter(0).transform.localScale,presenter.Fighter(1).transform.localScale,"두 선수 같은 재등장 시각/크기");
            Until(MatchPhase.Fighting);yield return null;
            Assert.AreEqual(Vector3.one,presenter.Fighter(0).transform.localScale);
            Assert.AreEqual(Vector3.one,presenter.Fighter(1).transform.localScale);
            for(int i=0;i<positions.Length;i++)Assert.Less(Vector3.Distance(positions[i],arena.transform.GetChild(i).localPosition),.001f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ResultExpandsWinner_ThenCenters_OnlyConfirmedReadySideBrightens_XReturns()
        {
            runner.OnlineRematch=true;root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1);
            runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
            runner.World.State.Match.SetScore0=5;runner.World.State.Match.Round=12;runner.World.State.Match.PhaseTick=90;
            yield return null;
            Assert.AreEqual(FrontendScreen.Result,View.Screen);Assert.Greater(View.DividerCenter,.85f);
            Assert.IsTrue(View.Portrait(0).gameObject.activeInHierarchy);
            Assert.IsFalse(View.Portrait(1).gameObject.activeInHierarchy,"승리 강조 중 상대 초상 숨김");
            Assert.IsFalse(View.RematchVisible);
            Assert.IsFalse(runner.RequestRematch(0));
            runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;yield return null;
            Assert.AreEqual(.5f,View.DividerCenter,.001f);Assert.IsTrue(View.RematchVisible);
            Assert.IsTrue(View.Portrait(0).gameObject.activeInHierarchy);
            Assert.IsTrue(View.Portrait(1).gameObject.activeInHierarchy,"재대결 단계에서 양쪽 복원");
            var panels=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            var unreadyRight=panels.right;var unreadyLeft=panels.left;
            ButtonAt("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();
            runner.StepOnce();yield return null;
            Assert.AreEqual(MatchPhase.MatchEnd,runner.World.State.Match.Phase);
            Assert.AreEqual(1,View.Portrait(0).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(1,View.Portrait(1).GetComponent<CanvasGroup>().alpha,"초상의 개별 알파는 유지");
            Assert.AreEqual(unreadyLeft,panels.left);Assert.AreEqual(unreadyRight,panels.right);
            Assert.AreEqual(0,View.RematchDim.left.a,"O를 누른 P1 전체 덮개 해제");
            Assert.AreEqual(.98f,View.RematchDim.right.a,"누르지 않은 P2는 같은 덮개 유지");
            Assert.IsFalse(ButtonAt("Shared_VS_and_cards/Ready_for_rematch").interactable);
            Assert.AreEqual(Color.white,View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>().line);
            ButtonAt("Shared_VS_and_cards/Return_to_menu").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Main,View.Screen);Assert.IsFalse(runner.Running);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator P2ReadyClickBrightensOnlyP2_AfterConfirmedInput_WaitsForP1()
        {
            runner.OnlineRematch=true;root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            var first=runner.RouteFrontendInput(new IdleInputSource(),0);
            var second=runner.RouteFrontendInput(new IdleInputSource(),1);
            runner.Lockstep=new RoutedInputs {LocalSlot=1,First=first,Second=second};
            runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
            runner.World.State.Match.SetScore1=5;runner.World.State.Match.Round=12;
            runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;
            yield return null;
            var panels=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            var unreadyLeft=panels.left;var unreadyRight=panels.right;
            ButtonAt("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();yield return null;
            Assert.AreEqual(.98f,View.RematchDim.right.a,"확정 입력 전에는 상대에게 준비로 표시하지 않음");
            runner.StepOnce();yield return null;
            Assert.AreEqual(unreadyLeft,panels.left);
            Assert.AreEqual(unreadyRight,panels.right);
            Assert.AreEqual(0,View.RematchDim.right.a);Assert.AreEqual(.98f,View.RematchDim.left.a);
            Assert.AreEqual(1,View.Portrait(1).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(1,View.Portrait(0).GetComponent<CanvasGroup>().alpha);
            Assert.IsFalse(ButtonAt("Shared_VS_and_cards/Ready_for_rematch").interactable);
            for(int i=0;i<90;i++)runner.StepOnce();yield return null;
            Assert.AreEqual(MatchPhase.MatchEnd,runner.World.State.Match.Phase);
            Assert.AreEqual(0,View.RematchDim.right.a,"한 번 누른 준비 표시 유지");
            Assert.IsTrue(runner.RequestRematch(0));runner.StepOnce();yield return null;
            Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PracticeReadyClickShowsBrightSideBeforeRestart_WithoutChangingSimulationRules()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1,true);
            runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);runner.World.State.Match.SetScore0=5;
            runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;yield return null;
            uint before=runner.World.ComputeChecksum();
            ButtonAt("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();yield return null;
            Assert.AreEqual(before,runner.World.ComputeChecksum(),"준비 연출은 경기 상태를 쓰지 않음");
            Assert.AreEqual(1,View.Portrait(0).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(1,View.Portrait(1).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(0,View.RematchDim.left.a);Assert.AreEqual(.98f,View.RematchDim.right.a);
            Assert.IsFalse(ButtonAt("Shared_VS_and_cards/Ready_for_rematch").interactable);
            runner.StepOnce();Assert.AreEqual(MatchPhase.MatchEnd,runner.World.State.Match.Phase);
            yield return new WaitForSecondsRealtime(.75f);
            runner.StepOnce();yield return null;
            Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
            Assert.AreEqual(InputButtons.None,runner.Player1Input.Poll(runner.World.State.Tick).Buttons,"준비 입력이 전투로 새지 않음");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PracticeReadyFeedbackIsCancelledByMenuOrReplacedMatch()
        {
            for(int replacement=0;replacement<2;replacement++)
            {
                root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
                runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
                runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;yield return null;
                ButtonAt("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();yield return null;
                if(replacement==0){ButtonAt("Shared_VS_and_cards/Return_to_menu").onClick.Invoke();yield return null;}
                root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
                runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
                runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;
                yield return new WaitForSecondsRealtime(.75f);
                runner.StepOnce();yield return null;
                Assert.AreEqual(MatchPhase.MatchEnd,runner.World.State.Match.Phase,"취소한 O가 나중에 새 경기를 넘기면 안 됨");
                Assert.IsTrue(ButtonAt("Shared_VS_and_cards/Ready_for_rematch").interactable);
                Assert.AreEqual(.98f,View.RematchDim.left.a);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator P2CardAreaExpandsWithoutChangingPortraitSize_OnlyLocalPickerCanClick()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            for(int i=0;i<80;i++)runner.StepOnce();yield return null;
            var edge=View.Portrait(1).anchoredPosition;
            var m=runner.World.State.Match;m.SetPhase(MatchPhase.SetPick);m.Picker=1;m.PickedAt=-1;
            m.Option0=PickOption.AugmentBase+CardCollectionRules.FirstCardId;
            runner.World.State.Match=m;
            yield return new WaitForSecondsRealtime(1.35f);
            Assert.AreEqual(FrontendScreen.Cards,View.Screen);Assert.Less(View.DividerCenter,.2f);
            Assert.AreEqual(View.Theme.portraitSize,View.Portrait(1).sizeDelta);
            Assert.Less(Vector2.Distance(edge,View.Portrait(1).anchoredPosition),.001f,"P2 선택자도 VS의 오른쪽 가장자리 구도 유지");
            Assert.IsTrue(View.Portrait(1).gameObject.activeInHierarchy);
            Assert.IsFalse(View.Portrait(0).gameObject.activeInHierarchy,"상대 초상과 패널은 숨김");
            Assert.AreEqual(1,View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>().SingleSide);
            Assert.AreEqual(1,View.Portrait(1).GetComponent<CanvasGroup>().alpha);
            Assert.IsFalse(View.CanChoose);Assert.IsFalse(View.CardButton(0).interactable);
            Assert.Greater(View.Portrait(1).GetComponent<CanvasGroup>().alpha,View.Portrait(0).GetComponent<CanvasGroup>().alpha);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ResultBackgroundLeadsPortraitAndPlate_WinnerStaysAtEdge()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            for(int i=0;i<80;i++)runner.StepOnce();yield return null; // 현재 VS의 초상 진입(42~66틱)이 정착한 구도
            var edge=View.Portrait(1).anchoredPosition;
            runner.World.State.Match.SetPhase(MatchPhase.MatchEnd);
            runner.World.State.Match.SetScore1=5;runner.World.State.Match.PhaseTick=6;yield return null;
            Assert.Less(View.DividerCenter,.5f,"배경부터 움직임");
            Assert.AreEqual(0,View.Portrait(1).GetComponent<CanvasGroup>().alpha);
            Assert.IsFalse(View.Portrait(0).gameObject.activeInHierarchy);
            var plate=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/P2_name")).GetComponent<CanvasGroup>();
            Assert.AreEqual(0,plate.alpha);
            runner.World.State.Match.PhaseTick=15;yield return null;
            Assert.Greater(View.Portrait(1).GetComponent<CanvasGroup>().alpha,0);
            Assert.AreEqual(0,plate.alpha,"이름표는 캐릭터보다 늦게 등장");
            runner.World.State.Match.PhaseTick=60;yield return null;
            Assert.Less(Vector2.Distance(edge,View.Portrait(1).anchoredPosition),.001f);
            Assert.AreEqual(1,plate.alpha);
            runner.World.State.Match.PhaseTick=165;yield return null;
            Assert.Greater(View.DividerCenter,.12f,"배경이 먼저 중앙으로 돌아옴");
            Assert.IsFalse(View.Portrait(0).gameObject.activeInHierarchy,"상대는 배경보다 늦게 돌아옴");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CheatKeysKillEachPlayer_ControlKeysKeepPauseAndStep()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1,true);
            root.AddComponent<DebugHotkeys>();
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                Until(MatchPhase.Fighting);yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.O));yield return null;
                Assert.AreEqual(MatchPhase.KO,runner.World.State.Match.Phase);Assert.AreEqual(1,runner.World.State.Match.RoundWinner);
                Assert.IsFalse(runner.TryDebugKnockout(1),"전투 밖에서는 즉사 무시");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Until(MatchPhase.Fighting);yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.P));yield return null;
                Assert.AreEqual(MatchPhase.KO,runner.World.State.Match.Phase);Assert.AreEqual(0,runner.World.State.Match.RoundWinner);
                Assert.IsFalse(runner.Running,"P는 더 이상 일시정지 토글이 아님");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                int tick=runner.World.State.Tick;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl,Key.O));yield return null;
                Assert.AreEqual(tick+1,runner.World.State.Tick);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl,Key.P));yield return null;
                Assert.IsTrue(runner.Running);runner.Running=false;
            }
            finally{InputSystem.RemoveDevice(keyboard);}
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CheatCannotMutateOnlineSyncRecordingOrPlayback()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            Until(MatchPhase.Fighting);yield return null;
            uint before=runner.World.ComputeChecksum();
            runner.Lockstep=new WaitingInputs();Assert.IsFalse(runner.TryDebugKnockout(0));runner.Lockstep=null;
            Assert.AreEqual(before,runner.World.ComputeChecksum());
            runner.SetSyncTest(true);Assert.IsFalse(runner.TryDebugKnockout(1));runner.SetSyncTest(false);
            Assert.AreEqual(before,runner.World.ComputeChecksum());
            runner.StartNewMatch();runner.Running=false;runner.StartRecording(runner.World.Config.Rules.Format());
            Until(MatchPhase.Fighting);Assert.IsNotNull(runner.Recording);
            before=runner.World.ComputeChecksum();Assert.IsFalse(runner.TryDebugKnockout(0));Assert.AreEqual(before,runner.World.ComputeChecksum());
            var recorded=runner.StopRecording();Assert.IsTrue(runner.PlayRecording(recorded));runner.Running=false;
            LogAssert.Expect(LogType.Log,"[Knockbox] 기록 재생 끝("+recorded.Count+"틱): 최종 체크섬이 기록과 같음");
            Until(MatchPhase.Fighting);Assert.IsNotNull(runner.Playback);
            before=runner.World.ComputeChecksum();Assert.IsFalse(runner.TryDebugKnockout(1));Assert.AreEqual(before,runner.World.ComputeChecksum());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MainJoinValidationPracticeVs_RealFontsAndGraphicsRender()
        {
            Assert.AreEqual(FrontendScreen.Main,View.Screen);Assert.IsFalse(runner.Running);
            Assert.IsNotNull(View.Theme.titleFont);Assert.IsNotNull(View.Theme.textFont);Assert.IsNotNull(View.Theme.boldFont);
            var actors=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby"));
            Assert.IsTrue(actors.Find(Knockbox.Presentation.SceneObjectName.Path("Actual_P1")).gameObject.activeSelf);Assert.IsFalse(actors.Find(Knockbox.Presentation.SceneObjectName.Path("Actual_P2")).gameObject.activeSelf);
            View.SetLocalSlot(1);
            Assert.IsFalse(actors.Find(Knockbox.Presentation.SceneObjectName.Path("Actual_P1")).gameObject.activeSelf);Assert.IsTrue(actors.Find(Knockbox.Presentation.SceneObjectName.Path("Actual_P2")).gameObject.activeSelf);
            ButtonAt("Main_and_lobby/Menu/Join_room").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Join,View.Screen);
            ButtonAt("Main_and_lobby/Room/Join").onClick.Invoke();yield return null;
            StringAssert.Contains("방 코드",View.Status.text);Assert.IsFalse(root.GetComponent<OnlineLobby>().Busy);
            ButtonAt("Main_and_lobby/Room/Back").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Main,View.Screen);
            ButtonAt("Main_and_lobby/Menu/Practice").onClick.Invoke();runner.Running=false;yield return null;
            Assert.AreEqual(FrontendScreen.Versus,View.Screen);
            var title=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")).GetComponent<Text>();
            Assert.AreEqual(VerticalWrapMode.Overflow,title.verticalOverflow,"제목 폰트 행 높이에 의한 누락 방지");
            Canvas.ForceUpdateCanvases();
            foreach(var graphic in View.GetComponentsInChildren<FrontendGraphic>())
            {
                Assert.IsNotNull(graphic.canvasRenderer);
                var mesh=graphic.canvasRenderer.GetMesh();
                Assert.IsNotNull(mesh,graphic.name);Assert.Greater(mesh.vertexCount,0,graphic.name);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CurtainsCarryTheirOwnWhiteEdges_AndMeetAtAsymmetricTarget()
        {
            View.Show(FrontendScreen.Versus);View.enabled=false;
            var panels=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            panels.SetCenter(.5f);panels.SetOpening(.6f);Canvas.ForceUpdateCanvases();
            var mesh=panels.canvasRenderer.GetMesh();int white=0;
            for(int i=0;i<mesh.vertexCount;i++) if(mesh.colors32[i].r==255 && mesh.colors32[i].g==255 && mesh.colors32[i].b==255)
            {
                white++;float x=mesh.vertices[i].x;
                Assert.IsTrue(x<480 || x>1440,"열린 커튼 사이 중앙에 고정된 흰 선이 없어야 함");
            }
            Assert.Greater(white,0);
            foreach(float center in new[]{.15f,.85f})
            {
                panels.SetCenter(center);panels.SetOpening(1);
                Assert.Less(panels.LeftEdge(.5f),0);Assert.Greater(panels.RightEdge(.5f),1);
                panels.SetOpening(0);
                Assert.AreEqual(panels.LeftEdge(.5f),panels.RightEdge(.5f),.00001f,"넓이가 달라도 목표 위치에서 같은 순간 만남");
                Assert.AreEqual(Color.white,panels.line);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundRebuildAndThreeTwoOneKeepLightOff_GoRevealsShadows()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            var presenter=root.GetComponent<MatchPresenter>();
            var arena=root.GetComponentInChildren<ArenaView>();
            var cue=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Round_start_cue")).GetComponent<Text>();
            foreach(int round in new[]{1,2})
            {
                var m=runner.World.State.Match;m.Round=round;m.SetPhase(MatchPhase.Countdown);runner.World.State.Match=m;
                int rebuildStart=round==1?View.IntroRevealTick:0;
                runner.World.State.Match.PhaseTick=rebuildStart+27;yield return null;yield return new WaitForEndOfFrame();
                Assert.IsFalse(arena.TransitionSettled);Assert.AreEqual(0,presenter.Background.ShadowVisibility);
                Assert.IsFalse(presenter.Background.RoundLightOn);
                Assert.IsFalse(cue.gameObject.activeInHierarchy);
                for(int beat=0;beat<4;beat++)
                {
                    runner.World.State.Match.PhaseTick=rebuildStart+CardCollectionRules.RebuildTicks+beat*60;yield return null;yield return new WaitForEndOfFrame();
                    Assert.IsTrue(arena.TransitionSettled);
                    Assert.AreEqual(beat==3?1:0,presenter.Background.ShadowVisibility);
                    Assert.AreEqual(beat==3,presenter.Background.RoundLightOn);
                    int visibleMotes=0;
                    foreach(var sprite in presenter.Background.GetComponentsInChildren<SpriteRenderer>())
                        if(sprite.name=="Mote" && sprite.enabled && sprite.color.a>.1f)visibleMotes++;
                    Assert.Greater(visibleMotes,0,"불이 꺼진 3·2·1과 GO 모두 배경 입자가 남음");
                    Assert.AreEqual(Vector3.one,arena.BorderRoot.localScale);
                    Assert.AreEqual(beat==3?"GO":(3-beat).ToString(),cue.text);
                    Assert.IsTrue(cue.gameObject.activeInHierarchy);
                    Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
                    Assert.AreEqual(beat==3,root.GetComponentInChildren<FighterShadowView>(true).gameObject.activeSelf);
                }
                Until(MatchPhase.Fighting);yield return null;yield return new WaitForEndOfFrame();Assert.IsFalse(cue.gameObject.activeInHierarchy);
                Assert.IsTrue(presenter.Background.RoundLightOn);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DefeatedPiecesDissolve_WholePickerEnters_HandRises_DarkCardsAndGroupLeave()
        {
            EnsureCamera();
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1);
            Until(MatchPhase.Fighting);runner.World.State.Match.Score1=1;
            runner.TryDebugKnockout(0);
            for(int i=0;i<25;i++)runner.StepOnce();yield return null;
            Assert.Greater(root.GetComponent<MatchPresenter>().KoShatter.PieceCountActive,0);
            Until(MatchPhase.SetPick);yield return null;
            Assert.IsNull(GameObject.Find("PickPanel"),"커튼 사이에 옛 PICK/WAIT 안내가 보이지 않아야 함(옛 선택 화면은 삭제됨)");
            Assert.IsFalse(root.GetComponent<MatchPresenter>().KoShatter.Active);
            Assert.IsNull(View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/Defeated pieces reassemble")),"카드 화면에서 파편을 재조립하지 않음");
            var portrait=View.Portrait(0);
            Assert.Less(portrait.anchoredPosition.x,-800,"완성 캐릭터가 화면 밖에서 시작");
            Assert.AreEqual(1,portrait.GetComponent<CanvasGroup>().alpha,.001f,"초상은 흐려지지 않고 불투명하게 진입");
            var hand=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/Selecting_player_s_hand")) as RectTransform;
            Assert.Less(hand.anchoredPosition.y,-500);Assert.AreEqual(1,hand.GetComponent<CanvasGroup>().alpha,"아래에서 들기, 페이드 아님");
            yield return new WaitForSecondsRealtime(1.4f);
            Assert.AreEqual(0,hand.anchoredPosition.y,.01f);
            Assert.Greater(portrait.anchoredPosition.x,0);
            View.CardButton(1).GetComponent<FrontendHover>().Hover?.Invoke(null);yield return new WaitForSecondsRealtime(.5f);
            Assert.Less(View.CardButton(1).transform.Find(Knockbox.Presentation.SceneObjectName.Path("Unhovered_shade")).GetComponent<Image>().color.a,.01f,"호버 밝기는 부드럽게 정상값에 도달");
            Assert.Greater(View.CardButton(0).transform.Find(Knockbox.Presentation.SceneObjectName.Path("Unhovered_shade")).GetComponent<Image>().color.a,.65f,"비호버 카드 전체를 더 어둡게");
            View.CardButton(1).GetComponent<FrontendHover>().Leave?.Invoke();yield return null;Assert.AreEqual(-1,View.HoveredCard);
            View.CardButton(1).GetComponent<FrontendHover>().Hover?.Invoke(null);
            View.CardButton(1).onClick.Invoke();runner.StepOnce();runner.StepOnce();
            for(int i=0;i<70;i++)runner.StepOnce();yield return null;
            Assert.Less(View.CardButton(1).GetComponent<RectTransform>().anchoredPosition.x,400,"선택 카드는 P1 초상 쪽으로 가져감");
            Assert.AreEqual(0,View.CardButton(1).GetComponent<CanvasGroup>().alpha,"선택 카드는 빠르게 획득되고 사라짐");
            Assert.AreEqual(0,View.CardButton(0).GetComponent<CanvasGroup>().alpha,"비선택 카드는 함께 쓸려 사라짐");
            Assert.Less(View.Portrait(0).anchoredPosition.x,-900,"선택 캐릭터도 손/카드와 함께 퇴장");
            Assert.AreEqual(1,runner.World.State.Fighter0.Augments.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BothLosers_FragmentsFallThenDissolveLocally_OnRoundsSetsAndMatchEnd_WithoutChangingState()
        {
            EnsureCamera();var presenter=root.GetComponent<MatchPresenter>();
            foreach(int defeated in new[]{0,1})foreach(int ending in new[]{0,1,2})
            {
                root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
                runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
                runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1);
                Until(MatchPhase.Fighting);yield return null;
                if(ending>0){if(defeated==0)runner.World.State.Match.Score1=1;else runner.World.State.Match.Score0=1;}
                if(ending==2){if(defeated==0)runner.World.State.Match.SetScore1=4;else runner.World.State.Match.SetScore0=4;}
                Assert.IsTrue(runner.TryDebugKnockout(defeated));
                while(runner.World.State.Match.PhaseTick<30)runner.StepOnce();yield return null;
                var shatter=presenter.KoShatter;
                Assert.AreEqual(6,shatter.PieceCountActive);Assert.IsFalse(shatter.Exiting);
                var pieces=new SpriteRenderer[6];var sizes=new Vector3[6];var positions=new Vector3[6];
                for(int i=0;i<6;i++){pieces[i]=shatter.PieceVisual(i);sizes[i]=pieces[i].transform.localScale;}
                float startY=pieces[0].transform.position.y;
                uint physicsBefore=runner.World.ComputeChecksum();
                for(int i=0;i<180;i++)shatter.Tick(1f/120f);
                Assert.AreEqual(physicsBefore,runner.World.ComputeChecksum(),"파편 물리는 경기 판정과 분리");
                Assert.Less(pieces[0].transform.position.y,startY,"소멸 전 실제 중력으로 낙하");
                while(runner.World.State.Match.PhaseTick<120)runner.StepOnce();yield return null;
                Assert.IsTrue(shatter.Exiting);
                for(int i=0;i<6;i++)positions[i]=pieces[i].transform.position;
                while(runner.World.State.Match.PhaseTick<140)runner.StepOnce();yield return null;
                for(int i=0;i<6;i++)
                {
                    Assert.AreEqual(positions[i],pieces[i].transform.position,"떨어진 자리에서 소멸, 화면 바깥으로 끌지 않음");
                    Assert.Less(pieces[i].transform.localScale.x,sizes[i].x);
                    Assert.Less(pieces[i].color.a,.5f,"몸 조각은 작아지며 흐려짐");
                }
                Assert.Greater(shatter.DissolveDustCount,0,"몸 조각 자리에 KO 전용 먼지가 남음");
                for(int i=0;i<24;i++)
                {
                    var dust=shatter.DustVisual(i);
                    Assert.Less(Vector3.Distance(dust.transform.position,positions[i/4]),.65f,"먼지는 주변에만 작게 퍼짐");
                }
                Assert.Greater(root.GetComponentInChildren<ArenaView>().BorderRoot.localScale.x,1,"지형도 함께 퇴장 중");
                uint before=runner.World.ComputeChecksum();presenter.RenderFrame();Assert.AreEqual(before,runner.World.ComputeChecksum());
                Until(MatchPhase.RoundEnd);yield return null;
                Assert.IsFalse(shatter.Active);
                Assert.AreEqual(0,shatter.PieceCountActive);Assert.AreEqual(0,shatter.DissolveDustCount);
                for(int i=0;i<6;i++)
                {
                    Assert.AreEqual(positions[i],pieces[i].transform.position);
                    Assert.IsFalse(pieces[i].gameObject.activeInHierarchy);
                }
                for(int i=0;i<24;i++)Assert.IsFalse(shatter.DustVisual(i).enabled);
                Until(ending==2?MatchPhase.MatchEnd:ending==1?MatchPhase.SetPick:MatchPhase.Countdown);yield return null;
                Assert.AreEqual(ending==2?FrontendScreen.Result:ending==1?FrontendScreen.Cards:FrontendScreen.Hidden,View.Screen);
                shatter.Shatter(Vector2.zero,presenter.Palette.FighterBody(defeated),Vector2.up);
                Assert.AreEqual(6,shatter.PieceCountActive);Assert.AreEqual(0,shatter.DissolveDustCount);
                Assert.AreEqual(1,shatter.PieceVisual(0).color.a);Assert.Greater(shatter.PieceVisual(0).transform.localScale.x,0);
                shatter.Clear();
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator P2CardDepartureMirrorsP1_WithoutChangingSimulation()
        {
            controller.enabled=false;View.Show(FrontendScreen.Cards);View.SetLocalSlot(1);
            yield return new WaitForSecondsRealtime(1.35f);
            uint before=runner.World.ComputeChecksum();
            View.Cards(1,false,2,.90f,0);yield return null;
            Assert.Greater(View.CardButton(2).GetComponent<RectTransform>().anchoredPosition.x,1500);
            Assert.AreEqual(0,View.CardButton(2).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(0,View.CardButton(0).GetComponent<CanvasGroup>().alpha);
            Assert.Greater(View.Portrait(1).anchoredPosition.x,2300);
            Assert.AreEqual(before,runner.World.ComputeChecksum());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AllElevenMaps_TransitionKeepsBoundaryConnected_AndReturnsEveryTerrainPiece()
        {
            var presenter=root.GetComponent<MatchPresenter>();var view=root.GetComponentInChildren<ArenaView>();
            foreach(string name in new[]{"Brawl","Box","Chimes","Gantry","Mobile","Pit","Ruins","PropLab","Scales","Totems","Towers"})
            {
                var map=ArenaFiles.LoadByName(name);uint checksum=map.ComputeChecksum();
                view.Build(map,presenter.CurrentTheme,presenter.Palette,presenter.Shapes,true);
                var sprites=view.GetComponentsInChildren<SpriteRenderer>();
                var home=new Vector3[sprites.Length];var size=new Vector3[sprites.Length];
                for(int i=0;i<sprites.Length;i++){home[i]=sprites[i].transform.position;size[i]=sprites[i].bounds.size;}
                var boundary=view.BorderRoot.GetComponentsInChildren<SpriteRenderer>();
                var bounds=new Bounds[boundary.Length];for(int i=0;i<bounds.Length;i++)bounds[i]=boundary[i].bounds;
                view.PresentTransition(.45f,false);
                for(int a=0;a<bounds.Length;a++)for(int b=a+1;b<bounds.Length;b++)
                    if(BoundsGap(bounds[a],bounds[b])<.001f)Assert.Less(BoundsGap(boundary[a].bounds,boundary[b].bounds),.005f,name+" 외곽 연결 유지");
                view.PresentTransition(0,true);Assert.IsFalse(view.TransitionSettled);
                view.PresentTransition(1,true);Assert.IsTrue(view.TransitionSettled);
                for(int i=0;i<sprites.Length;i++)
                {
                    Assert.Less(Vector3.Distance(home[i],sprites[i].transform.position),.001f,name+" 실제 지형 위치 복귀");
                    Assert.Less(Vector3.Distance(size[i],sprites[i].bounds.size),.001f,name+" 실제 지형 크기 복귀");
                }
                Assert.AreEqual(checksum,map.ComputeChecksum());
            }
            yield return null;LogAssert.NoUnexpectedReceived();
        }

        static float BoundsGap(Bounds a,Bounds b) => Mathf.Max(0,Mathf.Max(Mathf.Max(a.min.x-b.max.x,b.min.x-a.max.x),Mathf.Max(a.min.y-b.max.y,b.min.y-a.max.y)));

        [UnityTest]
        public IEnumerator SharedPortraits_HoverOnlyMovesHand_ClickCollectsAndStartsNextRound()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1,true);
            for(int i=0;i<80;i++)runner.StepOnce(); // 배경 충돌 후 초상 진입이 끝난 VS 구도와 비교한다.
            yield return new WaitForSecondsRealtime(.45f);
            var portrait0=View.Portrait(0);var portrait1=View.Portrait(1);
            var p0=portrait0.anchoredPosition;
            var size=portrait0.sizeDelta;var rotation=portrait0.localRotation;
            for(int round=0;round<2;round++)
            {
                for(int t=0;t<500&&runner.World.State.Match.Phase!=MatchPhase.Fighting;t++)runner.StepOnce();
                runner.World.State.Fighter0.Health=0;
                for(int t=0;t<200;t++)
                {
                    runner.StepOnce();var phase=runner.World.State.Match.Phase;
                    if(phase==MatchPhase.RoundTransition||phase==MatchPhase.SetPick)break;
                }
            }
            yield return new WaitForSecondsRealtime(1.35f);
            Assert.AreEqual(FrontendScreen.Cards,View.Screen);
            Assert.AreSame(portrait0,View.Portrait(0));Assert.AreSame(portrait1,View.Portrait(1));
            Assert.Less(Vector2.Distance(p0,portrait0.anchoredPosition),.001f,"P1 선택자는 VS의 왼쪽 가장자리 구도 유지");
            // 비활성 상대의 RectTransform 재계산 오차 대신 실제 비표시를 검증한다. P2가 선택자일 때 위치는 별도 검사한다.
            Assert.IsTrue(portrait0.gameObject.activeInHierarchy);
            Assert.IsFalse(portrait1.gameObject.activeInHierarchy,"상대 초상과 패널은 숨김");
            Assert.AreEqual(size,portrait0.sizeDelta);Assert.AreEqual(rotation,portrait0.localRotation);
            var divider=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            Assert.AreEqual(Color.white,divider.line);
            Assert.Greater(divider.center,.80f);
            Assert.Greater(portrait0.GetComponent<CanvasGroup>().alpha,portrait1.GetComponent<CanvasGroup>().alpha);
            ExecuteEvents.Execute(View.CardButton(2).gameObject,new PointerEventData(EventSystem.current){position=new Vector2(900,400)},ExecuteEvents.pointerEnterHandler);
            Assert.AreEqual(2,View.HoveredCard);Assert.AreEqual(0,runner.World.State.Fighter0.Augments.Count);
            View.CardButton(2).onClick.Invoke();View.CardButton(0).onClick.Invoke();
            runner.StepOnce();runner.StepOnce();yield return null;
            Assert.AreEqual(1,runner.World.State.Fighter0.Augments.Count);
            Assert.AreEqual(CardCollectionRules.FirstCardId+2,runner.World.State.Fighter0.Augments.Get(0));
            Assert.IsTrue(View.Points.Root.gameObject.activeInHierarchy,"획득 연출 중에도 승점 유지");
            Assert.IsTrue(View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Acquired_cards/P1_acquired_0")).gameObject.activeInHierarchy,"선택한 카드가 기존 보유 UI에 즉시 반영");
            for(int t=0;t<500&&runner.World.State.Match.Phase!=MatchPhase.Fighting;t++)runner.StepOnce();
            yield return null;
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen);
            Assert.AreEqual(1,runner.World.State.Fighter0.Augments.Count);
            Assert.IsTrue(View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Acquired_cards/P1_acquired_0")).gameObject.activeInHierarchy);
            Assert.AreEqual(1,runner.World.State.Match.SetScore1);
            LogAssert.NoUnexpectedReceived();
        }

        void PreviewCardAge(float age)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(FrontendView).GetField("enteredAt",flags).SetValue(View,Time.unscaledTime-age);
            typeof(FrontendView).GetMethod("AnimateScreen",flags).Invoke(View,null);
        }

        [UnityTest]
        public IEnumerator CardLayoutLeavesFaceClear_WiderPickerPanel_RejectsSweepTogetherWithoutTumbling()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;controller.enabled=false;View.enabled=false;
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var panel=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            foreach(int picker in new[]{0,1})
            {
                View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Cards);View.SetLocalSlot(picker);
                View.Cards(picker,true,-1,0,15);PreviewCardAge(1.19f);PreviewCardAge(2);
                Assert.AreEqual(picker==0?.98f:.14f,panel.LeftEdge(0),.0001f);
                Assert.AreEqual(picker==0?.86f:.02f,panel.LeftEdge(1),.0001f);
                var homes=new Vector2[3];
                for(int i=0;i<3;i++)
                {
                    var rect=View.CardButton(i).GetComponent<RectTransform>();homes[i]=rect.anchoredPosition;
                    // 최대 호버 크기에서도 테두리 포함 안전한 화면 여백과 카드 사이 공간.
                    Assert.GreaterOrEqual(homes[i].x-220,95);Assert.LessOrEqual(homes[i].x+220,1825);
                    if(i>0)Assert.Greater(homes[i].x-homes[i-1].x,413.6f);
                }
                Assert.IsTrue(picker==0?homes[0].x-220>=530:homes[2].x+220<=1390,"카드를 캐릭터 얼굴 반대쪽으로 모음");
                uint checksum=runner.World.ComputeChecksum();View.BeginCardFeedback(1);
                Vector2 previous=homes[0];float previousAlpha=1;
                foreach(float seconds in new[]{.10f,.20f,.30f,.40f})
                {
                    typeof(FrontendView).GetField("cardClickedAt",flags).SetValue(View,Time.unscaledTime-seconds);PreviewCardAge(2);
                    var a=View.CardButton(0).GetComponent<RectTransform>();var b=View.CardButton(2).GetComponent<RectTransform>();
                    Assert.Less(Vector2.Distance(a.anchoredPosition-homes[0],b.anchoredPosition-homes[2]),.02f,"같은 흐름으로 함께 이동");
                    Assert.Less(Mathf.Abs(a.anchoredPosition.y-homes[0].y),37);
                    Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0,a.localEulerAngles.z)),3.1f);
                    Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0,a.localEulerAngles.y)),.1f,"화면을 휙 뒤집지 않음");
                    Assert.GreaterOrEqual((a.anchoredPosition.x-previous.x)*(picker==0?1:-1),0);
                    float alpha=View.CardButton(0).GetComponent<CanvasGroup>().alpha;
                    Assert.Less(alpha,previousAlpha);previousAlpha=alpha;previous=a.anchoredPosition;
                    if(seconds==.10f)Assert.Less(alpha,.65f,"이동 초반부터 부드럽게 흐려짐");
                }
                Assert.AreEqual(0,View.CardButton(0).GetComponent<CanvasGroup>().alpha,.0001f);
                Assert.AreEqual(checksum,runner.World.ComputeChecksum());
                View.Show(FrontendScreen.Versus);Assert.AreEqual(.10f,panel.DividerSlant);
                View.Show(FrontendScreen.Result);Assert.AreEqual(.10f,panel.DividerSlant);
            }
            yield return null;LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ImpactVs_BackgroundsCloseFirst_ActorsHoldTwoSeconds_DiagonalEdgesMeet()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            var panel=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            runner.World.State.Match.PhaseTick=18;yield return null;
            Assert.AreEqual(FrontendScreen.Versus,View.Screen);
            Assert.Greater(panel.Opening,.8f,"출발은 느리고 충돌 직전에 가속");
            for(int p=0;p<2;p++)Assert.AreEqual(0,View.Portrait(p).GetComponent<CanvasGroup>().alpha);
            runner.World.State.Match.PhaseTick=34;yield return null;
            for(int p=0;p<2;p++)Assert.AreEqual(0,View.Portrait(p).GetComponent<CanvasGroup>().alpha,"배경 충돌 중에도 초상은 아직 숨김");
            runner.World.State.Match.PhaseTick=42;yield return null;
            Assert.AreEqual(0,panel.Opening,.001f,"충돌 반동 정착 후 캐릭터 진입");
            runner.World.State.Match.PhaseTick=70;yield return null;
            var a=View.Portrait(0).anchoredPosition;var b=View.Portrait(1).anchoredPosition;
            for(int p=0;p<2;p++)Assert.AreEqual(1,View.Portrait(p).GetComponent<CanvasGroup>().alpha);
            runner.World.State.Match.PhaseTick=190;yield return null;
            Assert.AreEqual(a,View.Portrait(0).anchoredPosition);Assert.AreEqual(b,View.Portrait(1).anchoredPosition);
            Assert.Greater(panel.LeftEdge(0),panel.LeftEdge(1)+.19f,"전체 높이에 걸친 대각선");
            foreach(float center in new[]{.12f,.5f,.88f})
            {
                panel.SetCenter(center);panel.SetOpening(0);
                foreach(float y in new[]{0f,.5f,1f})Assert.AreEqual(panel.LeftEdge(y),panel.RightEdge(y),.0001f);
                panel.SetOpening(1);
                foreach(float y in new[]{0f,1f}){Assert.Less(panel.LeftEdge(y),0);Assert.Greater(panel.RightEdge(y),1);}
            }
            runner.World.State.Match.PhaseTick=View.IntroRevealTick;yield return null;
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen);
            Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Cards_DarkSmallerRest_SmoothHover_PressPulse_ImmediateRejectSweep_BothSlots()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;controller.enabled=false;
            // This test drives hover explicitly; the user's physical cursor must not supply another hover.
            using var pointerIsolation = PlayModeSceneScope.SuspendPointerInput();
            foreach(int picker in new[]{0,1})
            {
                View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Cards);View.SetLocalSlot(picker);
                PreviewCardAge(2);View.Cards(picker,true,-1,0,15);yield return null;
                var chosen=View.CardButton(1);var other=View.CardButton(0);
                var shade=chosen.transform.Find(Knockbox.Presentation.SceneObjectName.Path("Unhovered_shade")).GetComponent<Image>();
                Assert.Greater(shade.color.a,.9f);Assert.AreEqual(.88f,chosen.transform.localScale.x,.001f);
                chosen.GetComponent<FrontendHover>().Hover?.Invoke(null);yield return null;
                Assert.That(chosen.transform.localScale.x,Is.InRange(.88f,1f));
                yield return new WaitForSecondsRealtime(.45f);
                Assert.Greater(chosen.transform.localScale.x,.995f);Assert.Less(shade.color.a,.01f);
                uint checksum=runner.World.ComputeChecksum();View.BeginCardFeedback(1);View.CanChoose=false;
                typeof(FrontendView).GetField("cardClickedAt",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(View,Time.unscaledTime-.05f);
                PreviewCardAge(2); // 프레임 대기 오차로 짧은 눌림 뒤의 복원 구간을 검사하지 않는다.
                Assert.Less(chosen.transform.localScale.x,.96f,"입력 전송을 기다리지 않고 눌림 표시");
                yield return new WaitForSecondsRealtime(.34f);
                Assert.Less(other.GetComponent<CanvasGroup>().alpha,.01f,"다른 카드는 즉시 쓸려 나감");
                Assert.Less(chosen.GetComponent<CanvasGroup>().alpha,.01f,"선택 카드는 짧게 초상 쪽으로 가져가 획득");
                yield return new WaitForSecondsRealtime(.75f); // 온라인 확정 지연을 흉내 낸다.
                var pendingPosition=chosen.GetComponent<RectTransform>().anchoredPosition;
                View.Cards(picker,false,1,0,0);yield return null;
                Assert.Less(Vector2.Distance(pendingPosition,chosen.GetComponent<RectTransform>().anchoredPosition),.01f,"늦은 확정 때문에 선택 카드가 순간이동하지 않음");
                View.Cards(picker,false,1,.8f,0);yield return null;
                Assert.AreEqual(picker==0?330:1590,chosen.GetComponent<RectTransform>().anchoredPosition.x,.01f);
                Assert.AreEqual(0,chosen.GetComponent<CanvasGroup>().alpha);
                Assert.AreEqual(checksum,runner.World.ComputeChecksum(),"선행 클릭 반응은 판정 상태를 바꾸지 않음");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Cards_EarlyHover_QuickClickKeepsPose_HandStaysOnGrip_AuthorityCorrectionDoesNotResurrect()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;controller.enabled=false;View.enabled=false;
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(int picker in new[]{0,1})
            {
                View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Cards);View.SetLocalSlot(picker);
                View.Cards(picker,true,-1,0,15);PreviewCardAge(.8f);
                var chosen=View.CardButton(1);var rect=chosen.GetComponent<RectTransform>();
                ExecuteEvents.Execute(chosen.gameObject,new PointerEventData(EventSystem.current){position=new Vector2(900,400)},ExecuteEvents.pointerEnterHandler);
                Assert.AreEqual(1,View.HoveredCard,"입장 중 올라온 포인터를 기억함");
                Assert.IsFalse(View.CanChoose,"입장 중 클릭은 여전히 차단");
                PreviewCardAge(2);View.Cards(picker,true,-1,0,13);PreviewCardAge(2);
                Assert.IsTrue(View.CanChoose);Assert.Greater(rect.localScale.x,.88f,"포인터를 다시 움직이지 않아도 호버 시작");
                var position=rect.anchoredPosition;float scale=rect.localScale.x;
                View.BeginCardFeedback(1);View.CanChoose=false;
                typeof(FrontendView).GetField("cardClickedAt",flags).SetValue(View,Time.unscaledTime);
                PreviewCardAge(2);
                Assert.Less(Vector2.Distance(position,rect.anchoredPosition),.02f,"빠른 클릭도 현재 위치에서 시작");
                Assert.AreEqual(scale,rect.localScale.x,.002f,"확대가 끝나기 전에 클릭해도 크기 스냅 없음");
                uint checksum=runner.World.ComputeChecksum();
                foreach(float seconds in new[]{.06f,.16f,.24f})
                {
                    typeof(FrontendView).GetField("cardClickedAt",flags).SetValue(View,Time.unscaledTime-seconds);
                    PreviewCardAge(seconds==.06f?1.21f:2); // 손 상승이 아직 끝나지 않은 최초 클릭도 접점 유지
                    var grip=rect.TransformPoint(new Vector3(0,-291,0));
                    var hand=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/Selecting_player_s_hand/Hand"));
                    Assert.Less(Vector3.Distance(grip,hand.position),.1f,"회전·축소 중 손은 실제 카드 접점에 붙음");
                }
                typeof(FrontendView).GetField("cardClickedAt",flags).SetValue(View,Time.unscaledTime-1);
                PreviewCardAge(2);Assert.AreEqual(0,View.CardButton(0).GetComponent<CanvasGroup>().alpha);
                View.Cards(picker,false,0,0,0);PreviewCardAge(2);
                Assert.AreEqual(0,View.CardButton(0).GetComponent<CanvasGroup>().alpha,"다른 자동 선택이 확정돼도 사라진 카드를 부활시키지 않음");
                Assert.AreEqual(checksum,runner.World.ComputeChecksum());

                View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Cards);
                View.Cards(picker,true,-1,0,15);PreviewCardAge(2);View.BeginCardFeedback(1);
                typeof(FrontendView).GetField("cardClickedAt",flags).SetValue(View,Time.unscaledTime-.16f);PreviewCardAge(2);
                var corrected=View.CardButton(0).GetComponent<RectTransform>();
                var beforePosition=corrected.anchoredPosition;var beforeRotation=corrected.localRotation;
                View.Cards(picker,false,0,0,0);PreviewCardAge(2);
                Assert.Less(Vector2.Distance(beforePosition,corrected.anchoredPosition),.02f,"진행 중 다른 선택이 확정돼도 위치가 뛰지 않음");
                Assert.Less(Quaternion.Angle(beforeRotation,corrected.localRotation),.02f,"뒤집힌 현재 각도도 이어감");
            }
            yield return null;LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CardPickerKeepsScoresAndInventory_OnlyLoserPanel_CharacterThenStaggeredCards()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;controller.enabled=false;
            var presenter=root.GetComponent<MatchPresenter>();
            Augments.Grant(ref runner.World.State.Fighter0,CardCollectionRules.FirstCardId,runner.World.Config);
            Augments.Grant(ref runner.World.State.Fighter1,CardCollectionRules.FirstCardId+2,runner.World.Config);
            var panel=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards/White_divider")).GetComponent<FrontendGraphic>();
            var owned=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Acquired_cards"));
            foreach(int picker in new[]{0,1})
            {
                var m=runner.World.State.Match;m.SetPhase(MatchPhase.SetPick);m.Picker=picker;m.PickedAt=-1;m.SetScore0=2;m.SetScore1=3;
                runner.World.State.Match=m;
                View.Show(FrontendScreen.Hidden);View.Show(FrontendScreen.Cards);View.SetLocalSlot(picker);
                View.Inventory(runner.World.State.Fighter0.Augments,runner.World.State.Fighter1.Augments);
                View.Cards(picker,true,-1,0,15);uint before=runner.World.ComputeChecksum();
                foreach(float age in new[]{0f,.30f,.74f,.84f,.94f,1.35f})
                {
                    PreviewCardAge(age);presenter.RenderFrame();Canvas.ForceUpdateCanvases();
                    Assert.IsTrue(View.Points.Root.gameObject.activeInHierarchy);Assert.IsTrue(owned.gameObject.activeInHierarchy);
                    Assert.IsTrue(owned.Find(Knockbox.Presentation.SceneObjectName.Path("P1_acquired_0")).gameObject.activeInHierarchy);Assert.IsTrue(owned.Find(Knockbox.Presentation.SceneObjectName.Path("P2_acquired_0")).gameObject.activeInHierarchy);
                    Assert.Greater(owned.GetSiblingIndex(),panel.transform.parent.GetSiblingIndex());
                    Assert.Greater(View.Points.Root.GetSiblingIndex(),panel.transform.parent.GetSiblingIndex());
                    Assert.AreEqual(presenter.Palette.FighterBody(0),View.Points.Root.Find(Knockbox.Presentation.SceneObjectName.Path("Set_0_1")).GetComponent<Image>().color);
                    Assert.AreEqual(presenter.Palette.FighterBody(1),View.Points.Root.Find(Knockbox.Presentation.SceneObjectName.Path("Set_1_2")).GetComponent<Image>().color);
                    Assert.AreEqual(1,presenter.Background.ShadowMaterial.GetFloat("_Opacity"),.001f,"카드 뒤 경기 배경 유지");
                    Assert.AreEqual(picker,panel.SingleSide);Assert.IsFalse(View.Portrait(1-picker).gameObject.activeInHierarchy);
                    Assert.AreEqual(1,View.Portrait(picker).GetComponent<CanvasGroup>().alpha);
                    if(age>.1f && age<.7f)
                    {
                        Assert.That(panel.Opening,Is.InRange(.001f,.99f));
                        float home=picker==0?View.Theme.portraitOffset.x+View.Theme.portraitSize.x*.5f:1920-View.Theme.portraitOffset.x-View.Theme.portraitSize.x*.5f;
                        Assert.Less(Mathf.Abs(View.Portrait(picker).anchoredPosition.x-home),1100,"배경과 캐릭터는 이미 함께 진입 중");
                        foreach(var button in new[]{View.CardButton(0),View.CardButton(1),View.CardButton(2)})Assert.AreEqual(0,button.GetComponent<CanvasGroup>().alpha);
                    }
                    if(age>=.74f && age<1)
                    {
                        int visible=age<.8f?1:age<.9f?2:3;
                        for(int i=0;i<3;i++)Assert.AreEqual(i<visible,View.CardButton(i).GetComponent<CanvasGroup>().alpha>0,"카드가 한 장씩 시차를 두고 등장");
                        Assert.Less(panel.Opening,.001f);
                    }
                    Assert.AreEqual(before,runner.World.ComputeChecksum());
                }
                var mesh=panel.canvasRenderer.GetMesh();Assert.IsNotNull(mesh);
                Color32 wanted=picker==0?panel.left:panel.right;
                foreach(Color32 color in mesh.colors32)Assert.IsTrue(color.Equals(wanted)||color.Equals((Color32)Color.white),"상대 패널/테두리 메시를 만들지 않음");
                Assert.IsFalse(View.CanChoose,"입장 첫 프레임은 클릭을 받지 않음");View.Cards(picker,true,-1,0,13);Assert.IsTrue(View.CanChoose);
                View.Cards(picker,false,1,.95f,0);PreviewCardAge(2);presenter.RenderFrame();
                Assert.Greater(panel.Opening,.8f);Assert.IsTrue(View.Points.Root.gameObject.activeInHierarchy);Assert.IsTrue(owned.gameObject.activeInHierarchy);
                View.Show(FrontendScreen.Versus);Assert.AreEqual(-1,panel.SingleSide,"VS는 양쪽 패널 복원");
                View.Show(FrontendScreen.Result);Assert.AreEqual(-1,panel.SingleSide,"결과는 양쪽 패널 유지");
            }
            yield return null;LogAssert.NoUnexpectedReceived();
        }
    }
}
