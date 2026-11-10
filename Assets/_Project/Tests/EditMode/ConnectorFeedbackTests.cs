using System.Linq;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class ConnectorFeedbackTests
    {
        const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
        GameObject root;PropView view;ImpactFx fx;ShapeLibrary shapes;PaletteAsset palette;
        [SetUp] public void Setup()
        {
            root=new GameObject("Connector_Feedback_Test"){hideFlags=HideFlags.HideAndDontSave};
            shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            var body=new GameObject("Bodies");body.transform.SetParent(root.transform,false);view=body.AddComponent<PropView>();
            var particles=new GameObject("Effects");particles.transform.SetParent(root.transform,false);fx=particles.AddComponent<ImpactFx>();fx.Setup(palette,shapes);
        }
        [TearDown] public void Teardown()=>Object.DestroyImmediate(root);
        SpriteRenderer[][] Lines=>(SpriteRenderer[][])typeof(PropView).GetField("graphLines",Fields).GetValue(view);
        [Test] public void BreakLeavesShortRopeRecoilThenRetiresWithoutChangingState()
        {
            var w=RopeGraphTests.World();view.Build(w.Arena,palette.Theme(0),shapes);view.Render(w.State.Props,w.State.Props,1);
            var before=w.State.Props;ConnectorBodies.Destroy(ref w.State.Props,w.Arena,0);uint checksum=w.ComputeChecksum();
            Assert.IsTrue(view.OnConnectorEvent(new SimEvent(1,SimEventType.ConnectorBreak,0,w.State.Props.B00.Position,new Vec2(1,0),20){PropIndex=0},w.State.Props,palette.flash));
            view.Render(before,w.State.Props,1);
            Assert.IsFalse(view.BodyTransform(0).gameObject.activeSelf,"removed collider must not leave a solid visual body");
            Assert.IsTrue(Lines[0].All(x=>x.enabled));Assert.IsTrue(Lines[3].All(x=>x.enabled),"unrelated lower rope remains");
            var start=Lines[0].Select(x=>x.transform.localPosition).ToArray();
            view.TickFeedback(.08f);view.Render(before,w.State.Props,1);
            Assert.IsTrue(Lines[0].Where((x,i)=>Vector3.Distance(x.transform.localPosition,start[i])>.01f).Any());
            view.TickFeedback(.1f);view.Render(before,w.State.Props,1);
            Assert.IsTrue(Lines[0].All(x=>!x.enabled));Assert.IsTrue(Lines[3].All(x=>x.enabled));
            Assert.AreEqual(checksum,w.ComputeChecksum());
            view.ResetFeedback();view.Render(before,before,1);
            Assert.IsTrue(Lines.All(row=>row.All(x=>x.enabled&&x.color.a==1)),"next round restores rope opacity");
        }
        [Test] public void ContactPulsesOutlineOnceThenRestoresEditedSizeAndColor()
        {
            var w=RopeGraphTests.World();view.Build(w.Arena,palette.Theme(0),shapes);view.Render(w.State.Props,w.State.Props,1);
            var tr=view.BodyTransform(0);var original=tr.localScale;var q=w.ComputeChecksum();
            var e=new SimEvent(0,SimEventType.ConnectorContact,0,w.State.Props.B00.Position,new Vec2(1,0),4){PropIndex=0};
            Assert.IsTrue(view.OnConnectorEvent(e,w.State.Props,palette.flash));Assert.IsFalse(view.OnConnectorEvent(e,w.State.Props,palette.flash),"contact cooldown");
            view.TickFeedback(.04f);view.Render(w.State.Props,w.State.Props,1);Assert.Greater(tr.localScale.x,original.x);
            var outline=tr.Find("Connector_Outline").GetChild(0).GetComponent<SpriteRenderer>();Assert.AreNotEqual(PropView.ConnectorColor,outline.color);
            view.TickFeedback(.2f);view.Render(w.State.Props,w.State.Props,1);Assert.AreEqual(original,tr.localScale);Assert.AreEqual(PropView.ConnectorColor,outline.color);
            Assert.AreEqual(q,w.ComputeChecksum());
        }
        [Test] public void FrameOutlineFlashesSplitsAndAllFragmentsExpire()
        {
            var center=new Vector2(2,3);var size=new Vector2(.65f,.65f);
            fx.ConnectorShatter(center,size,30,Vector2.zero,Vector2.right,PropView.ConnectorColor);fx.Tick(.01f,Vector2.zero);
            var pieces=fx.GetComponentsInChildren<SpriteRenderer>().Where(x=>x.enabled).ToArray();Assert.AreEqual(8,pieces.Length);
            foreach(var piece in pieces){Assert.AreEqual(palette.flash,piece.color);Assert.Less(Vector2.Distance(piece.transform.localPosition,center),.5f);}
            var positions=pieces.Select(x=>x.transform.localPosition).ToArray();fx.Tick(.09f,Vector2.zero);
            Assert.IsTrue(pieces.Where((p,i)=>Vector3.Distance(p.transform.localPosition,positions[i])>.05f).Any());
            foreach(var piece in pieces)Assert.AreNotEqual(palette.flash,piece.color);
            for(int t=0;t<60;t++)fx.Tick(1f/60f,Vector2.zero);
            Assert.AreEqual(0,fx.ActiveCount);Assert.IsTrue(pieces.All(x=>!x.enabled));
            fx.Shards(Vector2.zero,Vector2.up,90,8,palette.hit,4,.1f);fx.Tick(.01f,Vector2.zero);Assert.Greater(fx.ActiveCount,0);
            fx.Clear();Assert.AreEqual(0,fx.ActiveCount);
        }
        [Test] public void FirstSlowRenderStillShowsAnIntactImpactOutline()
        {
            fx.ConnectorShatter(Vector2.zero,new Vector2(.65f,.65f),0,Vector2.zero,Vector2.right,PropView.ConnectorColor);
            fx.Tick(.1f,Vector2.zero);
            var pieces=fx.GetComponentsInChildren<SpriteRenderer>().Where(x=>x.enabled).ToArray();
            Assert.AreEqual(8,pieces.Length);
            foreach(var piece in pieces)
            {
                Assert.AreEqual(palette.flash,piece.color,"first frame must not skip the flash");
                Assert.Less(((Vector2)piece.transform.localPosition).magnitude,.5f);
            }
            fx.Tick(.1f,Vector2.zero);
            Assert.IsTrue(pieces.Any(x=>((Vector2)x.transform.localPosition).magnitude>.5f),"frame pieces burst outward");
            for(int i=0;i<50;i++)fx.Tick(1f/60f,Vector2.zero);
            Assert.AreEqual(0,fx.ActiveCount);
        }

        [Test] public void DirectionalKickHasCurveLimitReducedMotionAndCleanReset()
        {
            var go=new GameObject("Impact_Camera");go.transform.SetParent(root.transform,false);
            go.AddComponent<Camera>().orthographic=true;var camera=go.AddComponent<CameraRig>();
            camera.ResetEffects();camera.Kick(Vector2.left,.12f,.02f,.18f);
            Assert.Less(camera.KickOffset.x,-.1f);Assert.AreEqual(.02f,camera.Zoom,.0001f);
            var normal=camera.KickOffset;float zoom=camera.Zoom;
            camera.ReduceMotion=true;Assert.AreEqual(normal*CameraRig.ReducedShakeScale,camera.KickOffset);
            Assert.AreEqual(zoom*CameraRig.ReducedShakeScale,camera.Zoom,.0001f);camera.ReduceMotion=false;
            float last=camera.KickOffset.magnitude;
            for(int t=0;t<20;t++)
            {
                camera.Tick(1f/60f);Assert.LessOrEqual(camera.KickOffset.magnitude,last);last=camera.KickOffset.magnitude;
            }
            Assert.AreEqual(Vector2.zero,camera.ShakeOffset);Assert.AreEqual(0,camera.Zoom);
            for(int t=0;t<10;t++)camera.Kick(Vector2.right,.15f,.03f,.18f);
            Assert.LessOrEqual(camera.KickOffset.magnitude,.22001f);Assert.LessOrEqual(camera.Zoom,.04001f);
            camera.ResetEffects();Assert.AreEqual(Vector2.zero,camera.KickOffset);Assert.AreEqual(0,camera.Zoom);
            camera.AddTrauma(.3f);camera.Tick(.02f);Assert.AreEqual(Vector2.zero,camera.KickOffset,"ordinary hits must not use connector kick");
        }

        static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Fields).SetValue(owner,value);

        [Test] public void ActualPunchBreakReachesPresenterAndReactsWithoutMutatingSimulation()
        {
            var old=RopeGraphTests.World(RopeGraphReplayTests.TrialArena());
            var w=new SimWorld(ConnectorContactTests.Config(true),old.Arena,old.State);
            SimEvent impact=default;bool found=false;
            for(int t=0;t<30&&!found;t++)
            {
                w.Step(new InputFrame(0,0,t==0?InputButtons.Attack:InputButtons.None,1),default);
                for(int k=0;k<w.Events.Count;k++)if(w.Events[k].Type==SimEventType.ConnectorBreak){impact=w.Events[k];found=true;break;}
            }
            Assert.IsTrue(found,"real short-click input must generate the break event");
            var host=new GameObject("Impact_Dispatch_Test");host.transform.SetParent(root.transform,false);host.SetActive(false);
            var runner=host.AddComponent<MatchRunner>();runner.Begin(w.Config,w.Arena,w.State);runner.Running=false;
            var presenter=host.AddComponent<MatchPresenter>();
            var cameraObject=new GameObject("Impact_Dispatch_Camera");cameraObject.transform.SetParent(host.transform,false);
            cameraObject.AddComponent<Camera>().orthographic=true;var camera=cameraObject.AddComponent<CameraRig>();camera.FitArena(w.Arena);camera.ResetEffects();
            var players=new FighterView[2];
            for(int i=0;i<2;i++)
            {
                var go=new GameObject("Impact_Player_"+i);go.transform.SetParent(host.transform,false);
                players[i]=go.AddComponent<FighterView>();players[i].Setup(i,palette,shapes);players[i].ResetPose();
            }
            view.Build(w.Arena,palette.Theme(0),shapes);view.Render(w.State.Props,w.State.Props,1);
            Set(presenter,"runner",runner);Set(presenter,"palette",palette);Set(presenter,"propView",view);
            Set(presenter,"fx",fx);Set(presenter,"cameraRig",camera);Set(presenter,"fighter0",players[0]);Set(presenter,"fighter1",players[1]);
            Set(presenter,"brawlWeaponView",host.AddComponent<BrawlWeaponView>());
            var sound=host.AddComponent<SfxPlayer>();sound.Setup(null);Set(presenter,"sfxPlayer",sound);
            uint before=runner.World.ComputeChecksum();
            typeof(MatchPresenter).GetMethod("Consume",Fields).Invoke(presenter,new object[]{impact});
            Assert.GreaterOrEqual(camera.Trauma,.48f);Assert.Greater(camera.KickOffset.magnitude,.09f);Assert.Greater(camera.Zoom,.017f);
            Assert.Less(players[impact.Fighter].BodyScale.x,.9f,"attacker braces at the actual impact");
            fx.Tick(1f/60f,Vector2.zero);Assert.Greater(fx.ActiveCount,14,"outline, sparks and impact rays must reach the effect pool");
            for(int t=0;t<75;t++)
            {
                camera.Tick(1f/60f);fx.Tick(1f/60f,Vector2.zero);view.TickFeedback(1f/60f);view.Render(w.State.Props,w.State.Props,1);
                players[impact.Fighter].Render(w.State.GetFighter(impact.Fighter),w.State.GetFighter(impact.Fighter),1,1f/60f,w.Arena,w.Config);
            }
            Assert.AreEqual(0,fx.ActiveCount);Assert.AreEqual(0,camera.Zoom);Assert.AreEqual(Vector2.zero,camera.KickOffset);
            Assert.AreEqual(before,runner.World.ComputeChecksum(),"feedback must not alter tick, HP, movement or rope destruction");
            TestContext.WriteLine("Actual punch break dispatched; camera kick/zoom, attacker squash and pooled burst completed without state changes.");
        }

        [TestCase(ActionState.Lunge)] [TestCase(ActionState.Thrown)]
        public void DestructionEmitsBodyAndDirectionOnce(ActionState action)
        {
            var old=RopeGraphTests.World();var w=new SimWorld(ConnectorContactTests.Config(true),old.Arena,old.State);
            var prev=w.State.Fighter0;prev.Position=new Vec2(-2,3);prev.Action=action;prev.Velocity=new Vec2(30,0);w.State.Fighter0=prev;
            w.State.Fighter0.Position=new Vec2(2,3);
            for(int i=0;i<w.State.Props.Count;i++)w.PropScratch.StartPosition[i]=PropState.At(w.State.Props,i).Position;
            ConnectorBodies.Resolve(ref w.State,w.Arena,w.Config,prev,w.State.Fighter1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(1,w.Events.Count);Assert.AreEqual(SimEventType.ConnectorBreak,w.Events[0].Type);
            Assert.AreEqual(0,w.Events[0].PropIndex);Assert.Greater(w.Events[0].Direction.X,.9f);Assert.AreEqual(25.5f,w.State.Fighter0.Velocity.X,.0001f);
            w.Events.Clear();ConnectorBodies.Resolve(ref w.State,w.Arena,w.Config,prev,w.State.Fighter1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(0,w.Events.Count);
        }
    }
}
