using System.Collections;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// 실제 Main 씬(저장된 Frontend 프리팹 UI)에서 메뉴·라운드 흐름·검수 단축키·재구성 조명·KO 조각·맵 전환을 확인한다(2026-11-17 정리).
    /// 이전에는 빈 씬에 절차식으로 만든 UI로 검사했고, Main이 더 쓰지 않는 VS·기존 카드·기존 결과 화면 검사는 지웠다.
    /// </summary>
    public class FrontendFlowTests
    {
        const string MainPath = "Assets/_Project/Scenes/Main.unity";
        Scene loaded;
        GameObject root;
        MatchRunner runner;
        FrontendController controller;
        FrontendView View=>controller.View;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return PlayModeSceneScope.OpenEmpty();
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(MainPath,new LoadSceneParameters(LoadSceneMode.Additive));
#else
            yield return SceneManager.LoadSceneAsync("Main",LoadSceneMode.Additive);
#endif
            loaded=SceneManager.GetSceneByPath(MainPath);SceneManager.SetActiveScene(loaded);
            yield return null;yield return null;
            runner=Object.FindFirstObjectByType<MatchRunner>();root=runner.gameObject;controller=root.GetComponent<FrontendController>();
            Assert.IsTrue(View.IsAuthored,"실제 Main의 저장된 UI");
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if(loaded.IsValid()&&loaded.isLoaded)yield return SceneManager.UnloadSceneAsync(loaded);
            yield return PlayModeSceneScope.OpenEmpty();
        }

        static ArenaView Arena=>Object.FindFirstObjectByType<ArenaView>(FindObjectsInactive.Include);

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
                    // 2026-11-16 CP3: 방 만들기·코드로 참가·연습은 메인의 게임 시작 하위 메뉴에 있다.
                    View.SetLocalSlot(slot);View.Show(FrontendScreen.Main);View.SetMenuPage(true);yield return null;yield return null;
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

        /// <summary>
        /// 2026-11-21 메인 화면: 메뉴의 UI 초상 캐릭터·바닥 선은 쓰지 않는다(게임 시작 메뉴의 캐릭터는 실제 로비 공간의 선수).
        /// 메인·코드 입력·대기 화면을 오가는 동안 UI 캐릭터는 숨은 채로 있고 메뉴 애니메이션은 유지된다.
        /// </summary>
        [UnityTest]
        public IEnumerator MainMenuUiCharacterStaysHidden_WhileMenusAnimate()
        {
            controller.enabled=false; // 방 만들기 네트워크 요청 없이 동일한 화면 전환 경로를 검사한다.
            foreach(int slot in new[]{0,1})
            {
                View.SetLocalSlot(slot);View.Show(FrontendScreen.Main);yield return null;
                var menuRoot=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby"));
                foreach(var screen in new[]{FrontendScreen.Join,FrontendScreen.Waiting,FrontendScreen.Main})
                {
                    View.Show(screen);yield return null;
                    var menu=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu")).GetComponent<RectTransform>();
                    var menuBefore=menu.anchoredPosition;
                    for(int frame=0;frame<18;frame++)
                    {
                        yield return null;
                        Assert.IsNull(menuRoot.Find("Actual_P1"),"메뉴의 UI 캐릭터는 없다(2026-11-21 정리)");
                        Assert.IsNull(menuRoot.Find("Floor"),"메뉴의 바닥 선은 없다(2026-11-21 정리)");
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
            var arena=Arena;
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
            // Main은 라운드마다 등록 맵을 바꿔 그리므로, 새 맵의 지형 묶음이 모두 제자리(연출 오프셋 0)인지 본다.
            for(int i=0;i<arena.transform.childCount;i++)Assert.Less(arena.transform.GetChild(i).localPosition.magnitude,.001f,"재구성 뒤 지형 제자리");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CheatKeysKillEachPlayer_ControlKeysKeepPauseAndStep()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1,true);
            var hotkeys=root.GetComponent<DebugHotkeys>();if(hotkeys==null)root.AddComponent<DebugHotkeys>();else hotkeys.enabled=true;
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
            // 2026-11-21: 첫 화면에는 캐릭터·바닥이 없다(게임 시작 메뉴의 캐릭터는 실제 로비 공간의 선수, 메뉴 UI 캐릭터·바닥 선은 정리함).
            Assert.IsNull(actors.Find("Actual_P1"));Assert.IsNull(actors.Find("Floor"));
            ButtonAt("Main_and_lobby/Menu/Join_room").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Join,View.Screen);
            ButtonAt("Main_and_lobby/Room/Join").onClick.Invoke();yield return null;
            StringAssert.Contains("방 코드",View.Status.text);Assert.IsFalse(root.GetComponent<OnlineLobby>().Busy);
            ButtonAt("Main_and_lobby/Room/Back").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Main,View.Screen);
            // 2026-11-21: 코드 입력에서 연 메인 화면 공간(로비 맵이 있는 설정)이 닫히는 중이면 다 닫힌 뒤 연습이 시작된다.
            ButtonAt("Main_and_lobby/Menu/Practice").onClick.Invoke();
            for(float until=Time.realtimeSinceStartup+2f;View.Screen!=FrontendScreen.Hidden&&Time.realtimeSinceStartup<until;)yield return null;
            runner.Running=false;yield return null;
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen,"2026-11-17: 연습은 VS 소개 없이 바로 시작");
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
        public IEnumerator RoundRebuildAndThreeTwoOneKeepLightOff_GoRevealsShadows()
        {
            root.GetComponent<OnlineLobby>().StartPractice();runner.Running=false;
            var presenter=root.GetComponent<MatchPresenter>();
            var arena=Arena;
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
                    Assert.AreEqual(beat==3,Object.FindFirstObjectByType<FighterShadowView>(FindObjectsInactive.Include).gameObject.activeSelf);
                }
                Until(MatchPhase.Fighting);yield return null;yield return new WaitForEndOfFrame();Assert.IsFalse(cue.gameObject.activeInHierarchy);
                Assert.IsTrue(presenter.Background.RoundLightOn);
            }
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
                Assert.Greater(Arena.BorderRoot.localScale.x,1,"지형도 함께 퇴장 중");
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
                // Main: 세트 패자 선택은 모놀리스 무대(경기 화면 위), 경기 끝은 결과 공간.
                Assert.AreEqual(ending==2?FrontendScreen.Podium:FrontendScreen.Hidden,View.Screen);
                shatter.Shatter(Vector2.zero,presenter.Palette.FighterBody(defeated),Vector2.up);
                Assert.AreEqual(6,shatter.PieceCountActive);Assert.AreEqual(0,shatter.DissolveDustCount);
                Assert.AreEqual(1,shatter.PieceVisual(0).color.a);Assert.Greater(shatter.PieceVisual(0).transform.localScale.x,0);
                shatter.Clear();
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AllElevenMaps_TransitionKeepsBoundaryConnected_AndReturnsEveryTerrainPiece()
        {
            var presenter=root.GetComponent<MatchPresenter>();var view=Arena;
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

    }
}
