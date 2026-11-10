using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class MapIntegratedWorkflowTests
    {
        public const string TrialPath="Assets/_Project/Data/Maps/Validation/CP5_Integrated_Trial.asset";
        Scene scene;MapAuthoring a;MapDefinition d;Object[] selection;
        readonly List<Object> owned=new List<Object>();
        T Own<T>(T value) where T:Object{owned.Add(value);return value;}
        Transform Child(string name)
        {var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(a.transform,false);return go.transform;}

        // A separate, unregistered review map. Existing trial definitions and the user's catalog stay untouched.
        public static MapDefinition CreateTrialDefinition()
        {
            var floor=new MapBox{id="floor",kind=TerrainKind.Solid,minX=-18,minY=-10,maxX=18,maxY=-8};
            var ramp=MapTerrainCut.Preview(new MapBox{id="ramp",kind=TerrainKind.Solid,minX=-12,minY=-8,maxX=-6,maxY=-2},
                new Vector2(-12,-8),new Vector2(-6,-2),false);
            var rectangle=new MapBox{id="cut_this_rectangle",kind=TerrainKind.Solid,minX=-2,minY=-6,maxX=2,maxY=-2};
            var boxes=new[]{floor,ramp,rectangle};
            var props=new[]{
                Box("stack_bottom",new Vector2(4,-7.25f),new Vector2(1.5f,1.5f),2.25f),
                Box("stack_middle",new Vector2(4,-5.75f),new Vector2(1.5f,1.5f),2.25f),
                Box("stack_top",new Vector2(4,-4.25f),new Vector2(1.5f,1.5f),2.25f),
                Box("pass_through_pillar",new Vector2(6,-6),new Vector2(1.5f,4),4),
                Box("connector",new Vector2(11,-3.4f),new Vector2(.65f,.65f),.65f),
                Box("suspended_beam",new Vector2(11,-6),new Vector2(4,.6f),2.4f),
                Box("lower_box",new Vector2(11,-7.3f),new Vector2(.8f,.8f),.64f)};
            props[3].playerPassThrough=true;props[4].connector=true;props[4].connectorSpeedLoss=.15f;
            var ropes=new[]{
                Edge("ceiling",props,4,-1,new Vector2(11,2),new Vector2(0,.325f)),
                Edge("beam_left",props,5,4,new Vector2(0,-.325f),new Vector2(-1.1f,.3f)),
                Edge("beam_right",props,5,4,new Vector2(0,-.325f),new Vector2(1.1f,.3f)),
                Edge("lower",props,6,5,new Vector2(0,-.3f),new Vector2(0,.4f))};
            var result=ScriptableObject.CreateInstance<MapDefinition>();
            result.Import(new ArenaDef("CP5_Integrated_Trial",36,20,false,boxes.Select(b=>b.ToBox()).ToArray(),new Vec2(-15,-8),new Vec2(16,-8)),
                new ArenaMeta(),"cp5_integrated_trial","CP5_Integrated_Trial");
            result.SaveLayout(boxes,props,ropes,new Vector2(-15,-8),new Vector2(16,-8));return result;
        }
        static MapPhysicalBox Box(string id,Vector2 center,Vector2 size,float mass)=>new MapPhysicalBox{id=id,center=center,size=size,mass=mass,connectorSpeedLoss=.15f};
        static MapRope Edge(string id,MapPhysicalBox[] p,int target,int source,Vector2 start,Vector2 end)
        {
            var world=source<0?start:p[source].center+start;
            return new MapRope{id=id,graph=true,boxId=p[target].id,sourceBoxId=source<0?null:p[source].id,anchor=world,
                sourceLocalAnchor=source<0?Vector2.zero:start,localAnchor=end,length=Vector2.Distance(world,p[target].center+end),mode=RopeBreakMode.Unbreakable};
        }
        [SetUp] public void Setup()
        {
            selection=Selection.objects;Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("CP5_Authoring_Test");SceneManager.MoveGameObjectToScene(go,scene);
            a=go.AddComponent<MapAuthoring>();a.terrainRoot=Child("Terrain");a.spawn0=Child("P1");a.spawn1=Child("P2");
            a.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            a.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            d=Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(TrialPath)));a.definition=d;MapAuthoringOperations.Open(a);
        }
        [TearDown] public void Teardown()
        {
            MapTerrainCut.Cancel();Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();EditorSceneManager.ClosePreviewScene(scene);
            foreach(var item in owned)if(item!=null&&!AssetDatabase.Contains(item))Object.DestroyImmediate(item);owned.Clear();
            Selection.objects=selection.Where(x=>x!=null).ToArray();
        }
        static void AssertVertices(MapBox box,SpriteRenderer sr)
        {
            var actual=sr.sprite.vertices.Select(p=>(Vector2)sr.transform.TransformPoint(p)).ToArray();Assert.AreEqual(box.vertices.Length,actual.Length);
            for(int i=0;i<actual.Length;i++)Assert.Less(Vector2.Distance(box.vertices[i],actual[i]),.00002f,"visible collision vertex "+i);
        }
        [Test] public void CutPlaceConnectSaveReopenPreviewReturnAndSeparateRegistration()
        {
            string realCatalog=JsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<MapCatalog>(MapAuthoringOperations.CatalogPath));
            var terrain=Enumerable.Range(0,d.BoxCount).Select(d.Box).ToArray();
            d.SaveLayout(terrain,Array.Empty<MapPhysicalBox>(),Array.Empty<MapRope>(),d.Spawn(0),d.Spawn(1));MapAuthoringOperations.Open(a);
            var cut=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>().Single(x=>x.Order==2);string id=cut.Id;
            MapTerrainCut.Apply(cut,new Vector2(0,-2),new Vector2(2,-4),false);
            Assert.AreEqual(id,cut.Id);Assert.AreEqual(2,cut.Order);AssertVertices(cut.Read(),cut.GetComponent<SpriteRenderer>());
            var template=Own(CreateTrialDefinition());var handles=new List<MapPhysicalBoxHandle>();
            for(int i=0;i<template.PhysicalBoxCount;i++)
            {
                var b=template.PhysicalBox(i);var h=MapPhysicalBoxTools.Add(a,b.connector?MapPhysicalBoxPreset.Connector:b.playerPassThrough?MapPhysicalBoxPreset.TallPillar:MapPhysicalBoxPreset.Box);
                h.transform.position=b.center;h.transform.localScale=new Vector3(b.size.x,b.size.y,1);h.Mass=b.mass;handles.Add(h);
            }
            var fixedRope=MapRopeTools.AddTo(a,handles[4]);fixedRope.transform.position=new Vector2(11,2);MapRopeTools.FitLength(fixedRope);
            var left=MapRopeTools.Connect(a,handles[4],handles[5]);left.LocalAnchor=new Vector2(-1.1f,.3f);MapRopeTools.FitLength(left);
            var right=MapRopeTools.Connect(a,handles[4],handles[5]);right.LocalAnchor=new Vector2(1.1f,.3f);MapRopeTools.FitLength(right);
            MapRopeTools.Connect(a,handles[5],handles[6]);
            Assert.IsEmpty(a.ValidatePlacement());
            string folder="Assets/_Project/LocalOnly/Map_CP5_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Mixed_Map.asset";AssetDatabase.CreateAsset(d,path);MapAuthoringOperations.Save(a);
            uint hash=d.ToArena().ComputeGeometryChecksum();var savedBoxes=a.ReadOrderedBoxes();var savedProps=a.ReadOrderedPhysicalBoxes();var savedRopes=a.ReadOrderedRopes();
            MapAuthoringOperations.Open(a);Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            CollectionAssert.AreEqual(savedBoxes,a.ReadOrderedBoxes());CollectionAssert.AreEqual(savedProps,a.ReadOrderedPhysicalBoxes());CollectionAssert.AreEqual(savedRopes,a.ReadOrderedRopes());

            // Save and reopen a separate authoring scene, never replacing the user's actual scene.
            var host=SceneManager.GetActiveScene();Scene opened=default;
            try
            {
                if(string.IsNullOrEmpty(host.path))
                {
                    Assert.IsTrue(host.GetRootGameObjects().All(x=>x.name=="Main Camera"&&x.GetComponents<Component>().All(c=>c is Transform||c is Camera||c is AudioListener)));
                    Assert.IsTrue(EditorSceneManager.SaveScene(host,folder+"/Test_Runner_Host.unity"));
                }
                opened=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var copy=Object.Instantiate(a.gameObject);SceneManager.MoveGameObjectToScene(copy,opened);
                string scenePath=folder+"/Mixed_Authoring.unity";Assert.IsTrue(EditorSceneManager.SaveScene(opened,scenePath));
                EditorSceneManager.CloseScene(opened,true);opened=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
                var reopened=opened.GetRootGameObjects().Select(x=>x.GetComponent<MapAuthoring>()).Single(x=>x!=null);
                Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(reopened));
                CollectionAssert.AreEqual(savedBoxes,reopened.ReadOrderedBoxes());CollectionAssert.AreEqual(savedProps,reopened.ReadOrderedPhysicalBoxes());CollectionAssert.AreEqual(savedRopes,reopened.ReadOrderedRopes());
                string guid=MapAuthoringOperations.SavedTrialGuid(reopened);var previousStart=EditorSceneManager.playModeStartScene;
                Assert.IsFalse(MapTestReturn.Active);
                try{MapTestReturn.Prepare(guid);Assert.AreEqual(MapAuthoringOperations.MainPath,AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));}
                finally{MapTestReturn.Restore();}
                Assert.AreSame(previousStart,EditorSceneManager.playModeStartScene);Assert.IsFalse(MapTestReturn.Active);
                var runnerGo=new GameObject("Mixed_Trial_Runner");runnerGo.SetActive(false);SceneManager.MoveGameObjectToScene(runnerGo,opened);
                var runner=runnerGo.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;runner.ConfigureAuthoringPreview(reopened.definition);runner.StartNewMatch();
                Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual(1,runner.World.ArenaCount);Assert.AreEqual(hash,runner.World.Arena.ComputeGeometryChecksum());
                Assert.AreEqual(4,runner.World.Arena.GraphRopeCount);Assert.IsTrue(runner.World.Arena.GetBox(2).IsPolygon);runner.StopRecording();
                var catalog=Own(ScriptableObject.CreateInstance<MapCatalog>());Assert.Zero(catalog.Maps.Count);catalog.Register(reopened.definition);
                Assert.AreEqual(hash,catalog.CreateRotation().Single().ComputeGeometryChecksum());
            }
            finally{if(opened.IsValid()&&opened.isLoaded)EditorSceneManager.CloseScene(opened,true);if(host.IsValid()&&host.isLoaded)SceneManager.SetActiveScene(host);}
            Assert.AreEqual(realCatalog,JsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<MapCatalog>(MapAuthoringOperations.CatalogPath)));
            TestContext.WriteLine("cut + 7 bodies + 4 graph ropes; scene/asset roundtrip="+hash.ToString("X8")+"; unregistered Main runner and return settings verified");
        }
        [Test] public void MixedMapRealPunchReplaysEveryTickAndRestoresAcrossConnectorBreak()
        {
            var c=ConnectorContactTests.Config(true);var arena=d.ToArena();
            var initial=SimState.CreateRound(c,arena,77,new Vec2(10,-3.4f),new Vec2(16,-8+c.HalfBody),MatchPhase.Fighting);
            var w=new SimWorld(c,arena,initial);var sync=new SyncTest<SimWorld>(w);var hashes=new List<uint>();
            var recording=InputRecording.Begin(c,arena,77,initial.Fighter0.Position,initial.Fighter1.Position,MatchPhase.Fighting,"brawl");
            int broken=-1;
            for(int t=0;t<600;t++)
            {
                var input=new InputFrame(t>60&&t<150?-1:0,0,t==0?InputButtons.Attack:InputButtons.None,1);
                recording.Append(input,default);Assert.IsTrue(sync.Advance(input,default),"rollback tick "+t);hashes.Add(w.ComputeChecksum());
                if(w.State.Props.IsDestroyed(4)&&broken<0)broken=t;
            }
            Assert.That(broken,Is.InRange(1,30));Assert.IsTrue(arena.GetGraphRope(3).Active(w.State.Props),"lower internal rope survives");
            recording.Finish(w.ComputeChecksum());var loaded=InputRecording.FromBytes(recording.ToBytes());
            var replay=new SimWorld(c,arena,loaded.CreateInitialState(c,arena));
            for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"replay tick "+t);}
            TestContext.WriteLine("mixed map connector break tick="+broken+"; rollback/replay compared="+hashes.Count);
        }
        [Test] public void MixedMapLossyOnlinePeersAgreeOnDestructionAndEveryState()
        {
            var c=ConnectorContactTests.Config(true);var arena=d.ToArena();
            // Six initial input-delay ticks pass before the punch. Start 1 unit above the node so the falling fighter still reaches it.
            // The zero-delay replay test intentionally uses the lower start; collision or network rules are not changed.
            var initial=SimState.CreateRound(c,arena,77,new Vec2(10,-2.4f),new Vec2(16,-8+c.HalfBody),MatchPhase.Fighting);
            var hw=new SimWorld(c,arena,initial);var gw=new SimWorld(c,arena,initial);
            MemoryTransport.Pair(out var ta,out var tb);double now=0;
            var la=new SimulatedLink(ta,()=>now,.04,.015,.10,.05,5);var lb=new SimulatedLink(tb,()=>now,.04,.015,.10,.05,6);
            uint manifest=OnlineMapManifest.Compute(new[]{arena});
            using(var host=new LockstepSession(la,true,c.ComputeChecksum(),manifest,77,6))
            using(var guest=new LockstepSession(lb,false,c.ComputeChecksum(),manifest,0,0))
            {
                for(int t=0;t<180&&(host.State!=Knockbox.Game.Online.SessionState.Running||guest.State!=Knockbox.Game.Online.SessionState.Running);t++)
                {now+=1.0/60;host.Poll(now);guest.Poll(now);}
                Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,host.State);Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,guest.State);
                var frames=new InputFrame[1600];for(int t=0;t<frames.Length;t++)frames[t]=new InputFrame(0,0,t==0?InputButtons.Attack:InputButtons.None,1);
                host.Begin(0,new FrameArrayInputSource(frames));guest.Begin(0,new IdleInputSource());
                var hh=new List<uint>();var gh=new List<uint>();int hb=-1,gb=-1;
                for(int t=0;t<1200;t++)
                {
                    now+=1.0/60;host.Poll(now);guest.Poll(now);
                    for(int i=0;i<2&&host.TryGetInputs(hw.State.Tick,out var p0,out var p1);i++)
                    {hw.Step(p0,p1);uint h=hw.ComputeChecksum();hh.Add(h);host.ReportChecksum(hw.State.Tick,h);if(hb<0&&hw.State.Props.IsDestroyed(4))hb=hw.State.Tick;}
                    for(int i=0;i<2&&guest.TryGetInputs(gw.State.Tick,out var p0,out var p1);i++)
                    {gw.Step(p0,p1);uint h=gw.ComputeChecksum();gh.Add(h);guest.ReportChecksum(gw.State.Tick,h);if(gb<0&&gw.State.Props.IsDestroyed(4))gb=gw.State.Tick;}
                }
                int count=Math.Min(hh.Count,gh.Count);Assert.Greater(count,800);Assert.Greater(hb,0);Assert.AreEqual(hb,gb);
                Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,host.State,host.EndDetail);Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,guest.State,guest.EndDetail);
                for(int i=0;i<count;i++)Assert.AreEqual(hh[i],gh[i],"mixed online tick "+i);Assert.Greater(host.ChecksumsCompared,10);
                TestContext.WriteLine("mixed online compared="+count+"; destruction tick="+hb+"; dropped="+(la.Dropped+lb.Dropped));
            }
        }
        [TestCase("slope")] [TestCase("size")] [TestCase("mass")] [TestCase("role")] [TestCase("endpoint")] [TestCase("length")]
        public void MixedMapChangesInvalidateRecordingAndOnlineManifest(string change)
        {
            var original=d.ToArena();var boxes=Enumerable.Range(0,d.BoxCount).Select(d.Box).ToArray();
            var props=Enumerable.Range(0,d.PhysicalBoxCount).Select(d.PhysicalBox).ToArray();var ropes=Enumerable.Range(0,d.RopeCount).Select(d.Rope).ToArray();
            switch(change)
            {
                case "slope":boxes[1]=MapTerrainCut.Preview(boxes[1],new Vector2(-6,-5),new Vector2(-9,-5),true);break;
                case "size":props[0].size.x-=.1f;break;case "mass":props[0].mass+=.1f;break;case "role":props[3].playerPassThrough=false;break;
                case "endpoint":ropes[0].localAnchor.x+=.1f;ropes[0].length+=.1f;break;case "length":ropes[0].length+=.25f;break;
            }
            d.SaveLayout(boxes,props,ropes,d.Spawn(0),d.Spawn(1));Assert.IsEmpty(d.Validate());var changed=d.ToArena();
            Assert.AreNotEqual(original.ComputeGeometryChecksum(),changed.ComputeGeometryChecksum());
            Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{original}),OnlineMapManifest.Compute(new[]{changed}));
            var c=ConnectorContactTests.Config(true);var rec=InputRecording.Begin(c,original,77,original.SpawnFoot0,original.SpawnFoot1,MatchPhase.Fighting,"brawl");
            Assert.Throws<InvalidOperationException>(()=>rec.CreateInitialState(c,changed));
        }
        [Test] public void MixedMapIdleStackAndGraphRemainSupported()
        {
            var w=RopeGraphTests.World(d.ToArena());float drift=0,spin=0,stretch=0;
            for(int t=0;t<1800;t++)
            {
                w.Step(default,default);Assert.IsFalse(w.State.Props.IsDestroyed(4));
                for(int e=0;e<w.Arena.GraphRopeCount;e++)
                {
                    var edge=w.Arena.GetGraphRope(e);var start=edge.Joint.OtherBody<0?edge.Joint.WorldAnchor:PropState.At(w.State.Props,edge.Joint.OtherBody).ToWorld(edge.Joint.OtherLocal);
                    stretch=Math.Max(stretch,SimMath.Sqrt((PropState.At(w.State.Props,edge.Body).ToWorld(edge.Joint.LocalAnchor)-start).LengthSquared)-edge.Joint.Length);
                }
                for(int i=0;i<4;i++){var b=PropState.At(w.State.Props,i);drift=Math.Max(drift,Math.Abs(b.Position.X-d.PhysicalBox(i).center.x));spin=Math.Max(spin,Math.Abs(b.AngularVelocity));}
            }
            Assert.Less(drift,.03f);Assert.Less(spin,.03f);Assert.Less(stretch,.08f);
            for(int i=0;i<3;i++)Assert.That(PropState.At(w.State.Props,i).Position.Y,Is.EqualTo(d.PhysicalBox(i).center.y).Within(.04f));
            TestContext.WriteLine("1800-tick mixed stack: lateral drift="+drift+", peak angular velocity="+spin+", rope stretch="+stretch);
        }
        [Test] public void PreviewAndMatchShareFillOutlineRopeWidthAndCollisionVertices()
        {
            MapAuthoringOperations.RefreshPreviewColors(a);var h=a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>();
            foreach(int i in new[]{3,4})Assert.AreEqual(new Color32(34,34,34,130),(Color32)h.Single(x=>x.Order==i).GetComponent<SpriteRenderer>().color);
            var arena=d.ToArena();var w=RopeGraphTests.World(arena);uint before=w.ComputeChecksum();
            var view=Child("Props").gameObject.AddComponent<PropView>();view.Build(arena,d.Metadata().ResolveTheme(a.palette),a.shapes);view.Render(w.State.Props,w.State.Props,1);
            foreach(int i in new[]{3,4})
            {var sr=view.BodyTransform(i).GetComponent<SpriteRenderer>();Assert.IsTrue(sr.enabled);Assert.AreEqual(new Color32(34,34,34,130),(Color32)sr.color);}
            Assert.AreEqual(4,view.BodyTransform(4).Find("Connector_Outline").childCount);
            foreach(var line in view.GetComponentsInChildren<SpriteRenderer>().Where(x=>x.name=="Rope"))
            {Assert.AreEqual(new Color32(85,85,85,255),(Color32)line.color);Assert.AreEqual(.12f,line.bounds.size.y>0?line.sprite.bounds.size.y*line.transform.lossyScale.y:0,.0001f);}
            var terrain=Child("Terrain_View").gameObject.AddComponent<ArenaView>();terrain.Build(arena,d.Metadata().ResolveTheme(a.palette),a.palette,a.shapes,true);
            AssertVertices(d.Box(1),terrain.GetComponentsInChildren<ConvexTerrainSprite>().Single().GetComponent<SpriteRenderer>());
            Assert.AreEqual(before,w.ComputeChecksum(),"presentation cannot change the simulation");
        }
        [Test] public void DeleteCanRepairAnInvalidPlacementWithoutDeletingUnrelatedRopes()
        {
            var handles=a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>();var ropes=MapRopeTools.Handles(a);
            ropes[0].Target=null;Assert.IsNotEmpty(a.ValidatePlacement());
            Selection.activeGameObject=handles.Single(x=>x.Order==4).gameObject;
            MapPhysicalBoxTools.DeleteSelected(a);Assert.AreEqual(1,MapRopeTools.Handles(a).Length);Assert.IsEmpty(a.ValidatePlacement());
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(7,a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>().Length);
        }
    }
}
