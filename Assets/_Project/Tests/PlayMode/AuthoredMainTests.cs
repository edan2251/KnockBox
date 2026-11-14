using System.Collections;
using System.IO;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Knockbox.Tests.PlayMode
{
    [PrebuildSetup(typeof(AuthoredMainProbeSetup))]
    public class AuthoredMainTests
    {
        Scene loaded;
        MatchRunner runner;
        FrontendController controller;
        FrontendView View=>controller.View;
        IEnumerator Load(string path="Assets/_Project/Scenes/Main.unity")
        {
            yield return PlayModeSceneScope.OpenEmpty();
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Additive));
#else
            yield return SceneManager.LoadSceneAsync("Main",LoadSceneMode.Additive);
#endif
            loaded=SceneManager.GetSceneByPath(path);SceneManager.SetActiveScene(loaded);
            yield return null;yield return null;
            runner=Object.FindFirstObjectByType<MatchRunner>();controller=runner.GetComponent<FrontendController>();
            Assert.IsTrue(View.IsAuthored);Assert.IsTrue(runner.GetComponent<MatchPresenter>().AuthoredOwners);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {if(loaded.IsValid()&&loaded.isLoaded)yield return SceneManager.UnloadSceneAsync(loaded);yield return PlayModeSceneScope.OpenEmpty();}
        Button ButtonAt(string path)=>View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path(path)).GetComponent<Button>();
        static void CaptureEvidence(string name)
        {
            string root=System.Environment.GetEnvironmentVariable("KNOCKBOX_TASK_OUTPUT");
            if(string.IsNullOrEmpty(root))return;
            string path=Path.Combine(root,"Evidence",name+"-"+System.Guid.NewGuid().ToString("N")+".png");
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path,texture.EncodeToPNG());Object.Destroy(texture);
        }
        void Idle()
        {
            runner.Running=false;runner.Player1Input=runner.RouteFrontendInput(new IdleInputSource(),0);
            runner.Player2Input=runner.RouteFrontendInput(new IdleInputSource(),1,true);
        }
        void Until(MatchPhase phase)
        {for(int i=0;i<650&&runner.World.State.Match.Phase!=phase;i++)runner.StepOnce();Assert.AreEqual(phase,runner.World.State.Match.Phase);}

        [UnityTest]
        public IEnumerator ActualMain_BindsSavedUI_LobbyPracticeVsAndCardAcquisitionReuseObjects()
        {
            yield return Load();using var pointer=PlayModeSceneScope.SuspendPointerInput();
            // 모놀리스 토글을 끈 기존 카드 UI 경로. 켠 경로는 ActualMain_MonolithPick_*에서 확인한다.
            runner.GetComponent<MatchPresenter>().CardPickStage.UseMonoliths=false;
#if UNITY_EDITOR
            var profile=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/_Project/Prefabs/Frontend/PostFxProfile.asset");
            Assert.True(profile.TryGet<UnityEngine.Rendering.Universal.ChromaticAberration>(out var originalAberration));
            float assetIntensity=originalAberration.intensity.value;
            var post=runner.GetComponent<MatchPresenter>().PostFx;post.Pulse(.6f,.2f);post.Tick(.05f);
            Assert.That(post.Aberration,Is.GreaterThan(.4f));Assert.AreEqual(assetIntensity,originalAberration.intensity.value);post.ResetEffects();
#endif
            var originalObjects=View.GetComponentsInChildren<Transform>(true).ToArray();
            var logo=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo"));var p0=View.Portrait(0);var p1=View.Portrait(1);
            Assert.AreEqual(FrontendScreen.Main,View.Screen);
            Assert.AreEqual(MatchRunner.RegisteredMapsName,runner.ArenaName);
            Assert.AreEqual(runner.MapCatalog.Maps.Count,runner.World.ArenaCount);
            Assert.NotNull(runner.MapCatalog.Find(runner.World.Arena.Name),"현재 경기 맵은 등록된 정의 중 하나여야 함");
            ButtonAt("Main_and_lobby/Menu/Join_room").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Join,View.Screen);Assert.IsTrue(View.CodeInput.gameObject.activeInHierarchy);
            ButtonAt("Main_and_lobby/Room/Back").onClick.Invoke();yield return null;Assert.AreEqual(FrontendScreen.Main,View.Screen);
            ButtonAt("Main_and_lobby/Menu/Practice").onClick.Invoke();Idle();yield return null;
            Assert.AreEqual(FrontendScreen.Versus,View.Screen);
            for(int i=0;i<80;i++)runner.StepOnce();yield return null;
            Vector2 vsPosition=p0.anchoredPosition;
            for(int r=0;r<2;r++)
            {
                Until(MatchPhase.Fighting);runner.World.State.Fighter0.Health=0;runner.StepOnce();
                Until(r==0?MatchPhase.RoundTransition:MatchPhase.SetPick);
            }
            yield return new WaitForSecondsRealtime(1.4f);yield return new WaitForEndOfFrame();
            Assert.AreEqual(FrontendScreen.Cards,View.Screen);Assert.AreSame(p0,View.Portrait(0));Assert.AreSame(p1,View.Portrait(1));
            CaptureEvidence("authored-main-cards");
            Assert.That(Vector2.Distance(vsPosition,p0.anchoredPosition),Is.LessThan(.01f));Assert.IsFalse(p1.gameObject.activeInHierarchy);
            Assert.True(View.CanChoose);View.CardButton(2).onClick.Invoke();View.CardButton(0).onClick.Invoke();
            runner.StepOnce();runner.StepOnce();yield return null;
            Assert.AreEqual(1,runner.World.State.Fighter0.Augments.Count);Assert.AreEqual(CardCollectionRules.FirstCardId+2,runner.World.State.Fighter0.Augments.Get(0));
            Until(MatchPhase.Fighting);yield return null;
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen);Assert.AreEqual(1,runner.World.State.Match.SetScore1);
            Assert.AreSame(logo,View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")));
            var after=View.GetComponentsInChildren<Transform>(true);
            foreach(var item in originalObjects)Assert.Contains(item,after,"Every authored UI object is reused.");
            foreach(var added in after.Except(originalObjects))Assert.IsTrue((added.name.EndsWith("Input Caret") || added.name.EndsWith("Input_Caret")),"Only Unity's InputField caret may be created: "+added.name);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator ActualMain_MonolithPick_LoserFacesMonoliths_ClickAcquiresCard_ThenArenaReturns([Values(0,1)] int picker)
        {
            yield return Load();using var pointer=PlayModeSceneScope.SuspendPointerInput();
            var presenter=runner.GetComponent<MatchPresenter>();var stage=presenter.CardPickStage;
            Assert.NotNull(stage,"Main에 모놀리스 선택 무대가 배치되어 있어야 함");Assert.IsTrue(stage.UseMonoliths);
            var catalog=stage.Catalog;Assert.NotNull(catalog);
            ButtonAt("Main_and_lobby/Menu/Practice").onClick.Invoke();Idle();yield return null;
            for(int i=0;i<80;i++)runner.StepOnce();yield return null;
            for(int r=0;r<2;r++)
            {
                Until(MatchPhase.Fighting);
                if(picker==0)runner.World.State.Fighter0.Health=0;else runner.World.State.Fighter1.Health=0;
                runner.StepOnce();Until(r==0?MatchPhase.RoundTransition:MatchPhase.SetPick);
            }
            yield return null;yield return new WaitForEndOfFrame();
            var m=runner.World.State.Match;
            Assert.AreEqual(picker,m.Picker);
            // 진입 연출(솟아오름): 시작 직후에는 비석이 땅속에 있고 고를 수 없으며, 선택 중 표시도 아직 없다.
            Assert.IsTrue(stage.Active);Assert.IsFalse(stage.EntranceSettled,"선택 단계 시작은 진입 연출 중");
            for(int i=0;i<stage.MonolithCount;i++)
            {
                Assert.Less(stage.Pose(i).offsetY+stage.Monolith(i).Height,0,"비석은 아직 땅속");
                Assert.IsFalse(stage.Monolith(i).Hit.interactable,"솟는 동안은 고를 수 없다");
            }
            Assert.IsFalse(stage.Banner.Visible,"선택 중 표시는 비석이 다 선 뒤에 튀어나온다");
            uint beforeEntrance=runner.World.ComputeChecksum();presenter.RenderFrame();Assert.AreEqual(beforeEntrance,runner.World.ComputeChecksum(),"진입 연출은 시뮬레이션을 바꾸지 않는다");
            // P1이 고르면 기운이 다 걷힐 때까지, P2(연습 상대 자동 선택 120틱)면 조작 가능해진 뒤 자동 선택 전(110틱)까지 진행한다.
            int entranceTicks=picker==0?Mathf.CeilToInt(stage.Entrance.FinishedAt(stage.MonolithCount)*SimConfig.TickRate)+1:110;
            for(int i=0;i<200&&runner.World.State.Match.PhaseTick<entranceTicks;i++)runner.StepOnce();
            yield return null;yield return new WaitForEndOfFrame();
            m=runner.World.State.Match;Assert.AreEqual(-1,m.PickedAt);
            Assert.IsTrue(stage.EntranceSettled,"진입 연출이 끝나면 고를 수 있다");
            for(int i=0;i<stage.MonolithCount;i++)Assert.AreEqual(0,stage.Pose(i).offsetY,1e-4f,"비석이 바닥에 섰다");
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen,"기존 카드 화면 대신 경기 화면 위의 선택 무대");
            Assert.IsTrue(stage.Active);Assert.AreEqual(picker,stage.Picker);
            Assert.AreSame(stage.Arena,presenter.BuiltArena,"선택 맵을 화면에만 그린다");
            Assert.AreNotSame(stage.Arena,runner.World.Arena,"시뮬레이션의 맵은 그대로");
            float side=picker==0?-1:1;
            Assert.That(presenter.Fighter(picker).transform.localPosition.x*side,Is.GreaterThan(5),"패자는 자기 프로필 쪽에 선다");
            Assert.That(presenter.Fighter(1-picker).transform.localScale.x,Is.LessThan(.01f),"승자는 나오지 않는다");
            var screenOrder=new System.Collections.Generic.List<(float x,int option)>();
            for(int i=0;i<stage.MonolithCount;i++)
            {
                var view=stage.Monolith(i);int option=stage.OptionOf(i);
                Assert.That((view.transform.position.x-presenter.Fighter(picker).transform.position.x)*-side,Is.GreaterThan(8),"모놀리스는 패자의 반대편(패자에게서 떨어진 쪽)");
                Assert.IsTrue(catalog.TryGet(PickOption.AugmentOf(m.GetOption(option)),out var face));
                Assert.AreEqual(face.title,view.Title.text);
                screenOrder.Add((view.transform.position.x,option));
                // 실제 화면 좌표의 UI 레이캐스트가 모놀리스 클릭 영역에 닿는다(앞 화면 UI가 가리지 않음).
                var point=Camera.main.WorldToScreenPoint(view.transform.TransformPoint(new Vector3(0,(view.FaceBottom+view.PanelTop)*.5f,0)));
                var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=point},hits);
                Assert.That(hits.Count,Is.GreaterThan(0));Assert.AreSame(view.Hit.gameObject,hits[0].gameObject);
                Assert.AreEqual(picker==runner.LocalPlayerSlot,view.Hit.interactable,"고르는 사람의 화면에서만 클릭");
            }
            CollectionAssert.AreEqual(new[]{0,1,2},screenOrder.OrderBy(o=>o.x).Select(o=>o.option).ToArray(),"화면 왼쪽부터 선택지 순서");
            uint before=runner.World.ComputeChecksum();presenter.RenderFrame();Assert.AreEqual(before,runner.World.ComputeChecksum());
            var banner=stage.Banner;Assert.NotNull(banner);Assert.IsTrue(banner.Visible,"누가 고르는 중인지 표시");
            Assert.AreEqual("P"+(picker+1)+" 선택 중",banner.Label.text);Assert.AreEqual(presenter.Palette.FighterBody(picker),banner.LabelColor);
            Assert.IsFalse(banner.HasBackgroundPlate,"라벨은 배경 판 없이 글자와 화살표만");Assert.IsTrue(banner.TimerVisible,"남은 초는 라벨 옆 작은 글자");
            var inventory=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Acquired_cards")).gameObject;
            Assert.IsTrue(inventory.activeSelf,"선택 장면에서도 양쪽이 고른 카드 목록(하단)이 보인다");
            Assert.IsTrue(View.Points.Root.gameObject.activeSelf,"선택 장면에서도 상단 승점 표시가 보인다");
            // 고정 광원: 고르는 패자 바로 위(같은 x, 화면 위쪽). 선택 맵 배경과 그림자를 이 위치로 굽는다.
            Assert.That(Mathf.Abs(stage.LightPosition.x-presenter.Fighter(picker).transform.localPosition.x),Is.LessThan(.01f),"광원은 패자와 같은 x");
            Assert.That(stage.LightPosition.y,Is.GreaterThan(9),"광원은 화면 위쪽");
            Assert.AreEqual(stage.MonolithCount,stage.ShadowCount,"비석 셋 모두 그림자를 드리운다");
            Assert.AreSame(presenter.Background.ShadowMaterial,stage.GetComponentInChildren<MeshRenderer>(true).sharedMaterial,"비석 그림자는 경기 그림자와 같은 재질(진하기 0.65)");
            var lightBefore=presenter.Background.BaseLightPosition;
            var offBody=Knockbox.Presentation.PaletteAsset.MonolithDefaults.Body;// 어두워지기 전 돌 색
            // 원근: 가장 높은 비석(앞)보다 낮은 비석(뒤)이 꺼진 상태에서 더 어둡다.
            var byHeight=Enumerable.Range(0,stage.MonolithCount).OrderBy(i=>stage.Monolith(i).Height).ToArray();
            System.Func<Color,float> sum=c=>c.r+c.g+c.b;
            Assert.Less(sum(stage.Monolith(byHeight[0]).BodyColor),sum(stage.Monolith(byHeight[2]).BodyColor),"뒤(낮은) 비석은 앞(높은) 비석보다 어둡다");
            Assert.AreEqual(0,stage.Monolith(byHeight[2]).Depth,1e-5f,"가장 높은 비석은 보정 없음");            for(int i=0;i<stage.MonolithCount;i++){Assert.AreEqual(0,stage.Lit(i));Assert.That(stage.Silhouette(i),Is.GreaterThan(.95f),"처음부터 카드 내용은 어둡게");}
            Assert.That(Vector2.Distance(stage.LightPosition,presenter.Background.BaseLightPosition),Is.LessThan(.01f),"평소에는 위쪽 기준 광원");
            int middle=Enumerable.Range(0,stage.MonolithCount).First(i=>stage.OptionOf(i)==1);
            int rightOf=Enumerable.Range(0,stage.MonolithCount).First(i=>stage.OptionOf(i)==2);
            Assert.AreSame(stage.Monolith(rightOf).Hit,stage.Monolith(middle).Hit.navigation.selectOnRight,"키보드 좌우 이동은 화면 순서");
            // 승자 화면 전송 경로(실제 온라인에서는 Fusion 비신뢰 RPC). 네트워크 없이 같은 규칙 객체를 끼워 넣는다.
            var sentHover=new System.Collections.Generic.List<(ushort round,ushort sequence,sbyte option)>();
            var relay=new Knockbox.Game.Online.CardHoverRelay((r,q,o)=>sentHover.Add((r,q,o)));stage.HoverRelay=relay;
            uint beforeHover=runner.World.ComputeChecksum();
            // 키보드 선택(=호버)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(stage.Monolith(middle).Hit.gameObject);
            yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            if(picker==0)
            {
                Assert.AreEqual(middle,stage.Hovered);Assert.That(stage.Lit(middle),Is.GreaterThan(.95f));
                Assert.That(stage.Monolith(middle).BandScale,Is.GreaterThan(1.05f),"호버한 이름 띠는 커진다");
                for(int i=0;i<stage.MonolithCount;i++)if(i!=middle)
                {
                    Assert.AreEqual(0,stage.Lit(i));Assert.That(stage.Silhouette(i),Is.GreaterThan(.95f),"호버하지 않은 것은 거의 검은 실루엣");
                    var c=stage.Monolith(i).BodyColor;Assert.That(c.r+c.g+c.b,Is.LessThan((offBody.r+offBody.g+offBody.b)*.35f),"어두워지는 폭이 크다");
                }
                Assert.That(stage.Monolith(middle).GlowHeight,Is.GreaterThan(stage.Monolith(middle).Height*.95f),"면과 몸체 발광이 아래에서 위로 다 찼다");
                Assert.AreEqual(Knockbox.Presentation.PaletteAsset.MonolithDefaults.NameLit,stage.Monolith(middle).TitleColor,"이름은 노란 강조색");
                Assert.That(Vector2.Distance(presenter.Background.BaseLightPosition,lightBefore),Is.LessThan(.001f),"호버해도 광원은 움직이지 않는다");
                Assert.AreEqual(stage.MonolithCount,stage.ShadowCount,"호버해도 비석 셋 모두 그림자를 드리운다");
                Assert.NotNull(presenter.Fighter(picker).LookTarget,"패자의 눈이 호버를 따라간다");Assert.AreNotEqual(0,presenter.Fighter(picker).StageLean);
                Assert.That(sentHover.Count,Is.GreaterThan(0),"고르는 쪽은 호버를 보낸다");
                Assert.AreEqual(1,sentHover[sentHover.Count-1].option,"모놀리스 칸이 아니라 선택지 번호로 보낸다");Assert.AreEqual(m.Round,sentHover[sentHover.Count-1].round);
                Assert.LessOrEqual(sentHover.Count,1+(int)(.8f/Knockbox.Game.Online.CardHoverRelay.RepeatSeconds)+2,"바뀌지 않으면 0.25초마다만 다시 보낸다");
                Assert.AreEqual(beforeHover,runner.World.ComputeChecksum(),"호버와 전송은 시뮬레이션 상태를 바꾸지 않는다");
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                yield return new WaitForSecondsRealtime(.5f);yield return null;
                Assert.AreEqual(-1,stage.Hovered);Assert.That(stage.Lit(middle),Is.LessThan(.05f));Assert.AreEqual(0,stage.Monolith(middle).GlowHeight,"꺼질 때 발광이 빠진다");
                Assert.AreEqual(-1,sentHover[sentHover.Count-1].option,"호버를 떼면 '없음'을 보낸다");
                Assert.That(stage.Silhouette(0)+stage.Silhouette(1)+stage.Silhouette(2),Is.GreaterThan(2.9f),"한 번 호버한 뒤에는 모두 그림만 어두운 상태");
                Assert.IsNull(presenter.Fighter(picker).LookTarget);
            }
            else
            {
                Assert.AreEqual(-1,stage.Hovered,"고르지 않는 쪽 화면에서는 로컬 호버를 받지 않는다");
                Assert.AreEqual(0,stage.Lit(middle));
                Assert.AreEqual(0,sentHover.Count,"내가 고르지 않으면 보내지 않는다");
                // 상대(패자)의 호버를 받으면 승자 화면에도 같은 점등·광원·시선이 나온다.
                ushort sequence=0;
                for(int i=0;i<5;i++){relay.Receive((ushort)m.Round,++sequence,1,Time.realtimeSinceStartupAsDouble);yield return new WaitForSecondsRealtime(.18f);}
                yield return new WaitForEndOfFrame();
                Assert.AreEqual(middle,stage.RemoteHovered);Assert.That(stage.Lit(middle),Is.GreaterThan(.95f));
                for(int i=0;i<stage.MonolithCount;i++)if(i!=middle)Assert.That(stage.Silhouette(i),Is.GreaterThan(.95f));
                Assert.That(Vector2.Distance(presenter.Background.BaseLightPosition,lightBefore),Is.LessThan(.001f),"받은 호버는 점등·어두워짐·눈에만 쓰고 광원은 그대로");
                Assert.AreEqual(stage.MonolithCount,stage.ShadowCount);Assert.NotNull(presenter.Fighter(picker).LookTarget);
                Assert.AreEqual(beforeHover,runner.World.ComputeChecksum(),"받은 호버도 시뮬레이션 상태를 바꾸지 않는다");
                // 소식이 끊기면 0.75초 뒤 호버 없음으로 돌아간다.
                yield return new WaitForSecondsRealtime(1.1f);yield return null;
                Assert.AreEqual(-1,stage.RemoteHovered);Assert.That(stage.Lit(middle),Is.LessThan(.05f));
            }
            if(picker==0)
            {
                // 토글을 끄면 같은 선택 단계에서도 기존 카드 UI로 돌아가고 경기 맵을 되돌린다.
                stage.UseMonoliths=false;yield return null;yield return null;
                Assert.AreEqual(FrontendScreen.Cards,View.Screen);Assert.IsFalse(stage.Active);
                Assert.AreSame(runner.World.Arena,presenter.BuiltArena);
                stage.UseMonoliths=true;yield return null;yield return null;
                Assert.AreEqual(FrontendScreen.Hidden,View.Screen);Assert.IsTrue(stage.Active);
                int target=Enumerable.Range(0,stage.MonolithCount).First(i=>stage.OptionOf(i)==2);
                int other=Enumerable.Range(0,stage.MonolithCount).First(i=>stage.OptionOf(i)==0);
                stage.Monolith(target).Hit.onClick.Invoke();stage.Monolith(other).Hit.onClick.Invoke();
                runner.StepOnce();runner.StepOnce();yield return null;
                Assert.AreEqual(1,runner.World.State.Fighter0.Augments.Count,"두 번째 클릭은 무시");
                Assert.AreEqual(CardCollectionRules.FirstCardId+2,runner.World.State.Fighter0.Augments.Get(0));
                Assert.IsFalse(stage.Monolith(target).Hit.interactable,"확정 뒤에는 다시 고를 수 없음");
                // 확정 연출: 패자가 고른 비석을 내리찍어 땅에 박는다(표시 전용, 시뮬레이션 상태는 그대로).
                int pickedAt=runner.World.State.Match.PickedAt;
                int hold=runner.World.Config.Match.PickHoldTicks;
                var timing=stage.Confirm;Assert.NotNull(timing,"확정되면 고른 비석으로 시간표를 만든다");
                int landed=Mathf.CeilToInt(timing.FloorTime*60)+1;// 비석이 바닥 끝까지 박히고 패자가 착지한 직후
                for(int i=0;i<hold&&runner.World.State.Match.PhaseTick-pickedAt<landed;i++)runner.StepOnce();
                yield return null;yield return new WaitForEndOfFrame();
                Assert.AreEqual(target,stage.ConfirmedMonolith,"고른 비석에 확정 연출");
                Assert.Less(stage.Pose(target).offsetY,-1f,"내리찍힌 비석이 땅에 박힌다");
                Assert.AreEqual(0,stage.Pose(other).offsetY,1e-4f,"고르지 않은 비석은 그대로");
                uint confirmBefore=runner.World.ComputeChecksum();presenter.RenderFrame();Assert.AreEqual(confirmBefore,runner.World.ComputeChecksum());
                yield return null;// 다음 단계 판정이 LateUpdate(화면 처리) 순서와 맞도록 한 프레임 넘긴다.
            }
            else
            {
                // 연습 상대(P2)는 기존 자동 선택으로 고른다. 내 화면에서는 클릭할 수 없다.
                for(int i=0;i<240&&runner.World.State.Match.PickedAt<0;i++)runner.StepOnce();
                yield return null;
                Assert.AreEqual(1,runner.World.State.Match.Picks1);Assert.AreEqual(1,runner.World.State.Fighter1.Augments.Count);
            }
            Until(MatchPhase.Fighting);yield return null;
            Assert.IsFalse(stage.Active);Assert.IsFalse(stage.Monolith(0).gameObject.activeInHierarchy);
            Assert.AreSame(runner.World.Arena,presenter.BuiltArena,"다음 라운드는 경기 맵으로 재구성");
            Assert.AreEqual(FrontendScreen.Hidden,View.Screen);
            Assert.That(presenter.Fighter(1-picker).transform.localScale.x,Is.GreaterThan(.99f));
            Assert.IsTrue(inventory.activeSelf,"경기로 돌아오면 보유 카드 표시를 되살린다");Assert.IsFalse(banner.Visible);
            Assert.IsNull(presenter.Fighter(picker).LookTarget);Assert.AreEqual(0,presenter.Fighter(picker).StageBreath);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator ActualMain_ResultWaitsForO_ThenLocalRematchOrX()
        {
            yield return Load();runner.GetComponent<OnlineLobby>().StartPractice();Idle();
            runner.World.State.Match.Phase=MatchPhase.MatchEnd;runner.World.State.Match.SetScore0=5;runner.World.State.Match.RoundWinner=0;
            runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;
            yield return null;Assert.AreEqual(FrontendScreen.Result,View.Screen);
            for(int i=0;i<90;i++)runner.StepOnce();yield return null;Assert.AreEqual(MatchPhase.MatchEnd,runner.World.State.Match.Phase);
            ButtonAt("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();yield return null;
            Assert.That(View.RematchDim.left.a,Is.LessThan(.01f));
            yield return new WaitForSecondsRealtime(.7f);for(int i=0;i<5;i++)runner.StepOnce();yield return null;
            Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
            runner.World.State.Match.Phase=MatchPhase.MatchEnd;runner.World.State.Match.SetScore1=5;
            runner.World.State.Match.PhaseTick=runner.World.Config.Match.MatchEndMinTicks;yield return null;
            ButtonAt("Shared_VS_and_cards/Return_to_menu").onClick.Invoke();yield return null;
            Assert.AreEqual(FrontendScreen.Main,View.Screen);LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator SavedSceneUIEdits_SurviveRuntimeMotionAndLobbyRoundTrip()
        {
#if UNITY_EDITOR
            string path=UnityEditor.SessionState.GetString(AuthoredMainProbeSetup.UIKey,"");
            Assert.IsTrue(File.Exists(path),"Persistence fixture must be prepared before Play.");
            yield return Load(path);
#else
            Assert.Ignore("Editor persistence probe.");yield break;
#endif
            using var pointer=PlayModeSceneScope.SuspendPointerInput();
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            CaptureEvidence("authored-main-edited-ui");
            var text=View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")).GetComponent<Text>();
            Assert.AreEqual(91,text.fontSize);Assert.That(Vector2.Distance(new Vector2(37,-19),text.rectTransform.anchoredPosition),Is.LessThan(.01f));
            Assert.AreEqual(new Vector2(982,161),text.rectTransform.sizeDelta);Assert.AreEqual(View.Theme.selectedLeft,text.color);
            Assert.AreEqual(Vector3.one*1.1f,text.rectTransform.localScale);Assert.Less(Quaternion.Angle(Quaternion.Euler(0,0,2),text.rectTransform.localRotation),.001f);
            ButtonAt("Main_and_lobby/Menu/Join_room").onClick.Invoke();yield return null;
            ButtonAt("Main_and_lobby/Room/Back").onClick.Invoke();yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            Assert.AreEqual(91,text.fontSize);Assert.That(Vector2.Distance(new Vector2(37,-19),text.rectTransform.anchoredPosition),Is.LessThan(.01f));
            Assert.AreEqual(View.Theme.selectedLeft,text.color);LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator AuthoringTestHandoff_UsesRegisteredTrialInMainWithoutChangingDefault()
        {
#if UNITY_EDITOR
            const string key="Knockbox.MapAuthoring.TestMap";
            UnityEditor.SessionState.SetString(key,UnityEditor.AssetDatabase.AssetPathToGUID("Assets/_Project/Data/Maps/BrawlTrial.asset"));
            try
            {
                yield return Load();
                Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual("map:brawl-authoring-trial",runner.World.Arena.Name);
                Assert.AreEqual(0x483E0206u,runner.World.Arena.ComputeChecksum());
            }
            finally { UnityEditor.SessionState.EraseString(key); }
#else
            Assert.Ignore("Editor map handoff.");yield break;
#endif
        }
        [UnityTest]
        public IEnumerator SavedEditedMap_RunsInActualMain_VisibleTerrainEqualsSimulation()
        {
#if UNITY_EDITOR
            yield return Load(UnityEditor.SessionState.GetString(AuthoredMainProbeSetup.MapKey,""));
            uint expected=uint.Parse(UnityEditor.SessionState.GetString(AuthoredMainProbeSetup.HashKey,""),System.Globalization.NumberStyles.HexNumber);
            Assert.AreEqual(expected,runner.World.Arena.ComputeChecksum());Assert.AreNotEqual(0x483E0206u,expected);
            runner.GetComponent<OnlineLobby>().StartPractice();Idle();Until(MatchPhase.Fighting);
            yield return null;yield return new WaitForEndOfFrame();
            var arena=runner.GetComponentInChildren<ArenaView>();var definition=runner.World.Arena;
            var expectedTheme=runner.GetMeta(definition.Name).ResolveTheme(runner.GetComponent<MatchPresenter>().Palette);
            for(int i=0;i<definition.BoxCount;i++)
            {
                var box=definition.GetBox(i);var parent=arena.GetComponentsInChildren<Transform>().First(t=>t.name=="Terrain_box_"+i+"_"+box.Kind);
                var bounds=parent.GetComponentInChildren<SpriteRenderer>().bounds;
                Assert.AreEqual(expectedTheme.terrain,parent.GetComponentInChildren<SpriteRenderer>().color,"저장한 테마가 실제 경기 지형에 적용");
                Assert.AreEqual(box.MinX,bounds.min.x,.00001f);Assert.AreEqual(box.MaxX,bounds.max.x,.00001f);
                Assert.AreEqual(box.MinY,bounds.min.y,.00001f);Assert.AreEqual(box.MaxY,bounds.max.y,.00001f);
            }
            Assert.IsTrue(runner.GetComponent<MatchPresenter>().Hud.GetComponentsInChildren<SpriteRenderer>().Any(s=>s.name=="Bar0Fill"&&s.enabled));
            LogAssert.NoUnexpectedReceived();
#else
            Assert.Ignore("Editor persistence probe.");yield break;
#endif
        }
    }
}
