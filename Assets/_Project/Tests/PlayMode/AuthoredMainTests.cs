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
