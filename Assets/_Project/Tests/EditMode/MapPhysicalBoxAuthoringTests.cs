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
    public class MapPhysicalBoxAuthoringTests
    {
        Scene scene;
        Object[] selected;
        MapAuthoring a;
        MapDefinition definition;
        readonly List<Object> owned=new List<Object>();
        T Own<T>(T o) where T:Object {owned.Add(o);return o;}
        public static ArenaDef Floor()=>new ArenaDef("CP2_Floor",36,20,false,
            new[]{new ArenaBox(TerrainKind.Solid,-18,-10,18,-8)},new Vec2(-12,-8),new Vec2(12,-8));
        public static MapPhysicalBox Box(int id,float x,float y,float size=1.2f,float mass=1.44f)=>new MapPhysicalBox
            {id="box_cp2_"+id,center=new Vector2(x,y),size=new Vector2(size,size),mass=mass};
        public static MapDefinition Definition(MapPhysicalBox[] boxes)
        {
            var d=ScriptableObject.CreateInstance<MapDefinition>();d.Import(Floor(),new ArenaMeta(),"cp2_boxes","상자_검수");
            d.SaveLayout(new[]{d.Box(0)},boxes,d.Spawn(0),d.Spawn(1));return d;
        }
        Transform Child(string name,Transform parent)
        {var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(parent,false);return go.transform;}
        MapPhysicalBoxHandle[] Handles()=>a.physicsRoot==null?Array.Empty<MapPhysicalBoxHandle>():a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true).OrderBy(h=>h.Order).ToArray();
        [SetUp] public void Setup()
        {
            selected=Selection.objects;Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("CP2_Authoring_Probe");SceneManager.MoveGameObjectToScene(go,scene);
            a=go.AddComponent<MapAuthoring>();a.terrainRoot=Child("Terrain",a.transform);a.spawn0=Child("P1",a.transform);a.spawn1=Child("P2",a.transform);
            a.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            a.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            definition=Own(Definition(Array.Empty<MapPhysicalBox>()));a.definition=definition;MapAuthoringOperations.Open(a);
        }
        [TearDown] public void Teardown()
        {
            Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();EditorSceneManager.ClosePreviewScene(scene);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null&&!AssetDatabase.Contains(owned[i]))Object.DestroyImmediate(owned[i]);owned.Clear();
            Selection.objects=selected;
        }
        [Test] public void AddResizeMassStackMirrorAndOneUndo_PreserveIdsAndOrder()
        {
            var h=MapPhysicalBoxTools.Add(a);Assert.AreEqual("Physics_Box_0",h.name);
            h.transform.localPosition=new Vector3(-3,-7.4f,0);h.transform.localScale=new Vector3(1.2f,1.2f,1);h.Mass=3f;
            string id=h.Id;var upper=MapPhysicalBoxTools.Copy(a,new[]{h},false).Single();
            Assert.AreEqual(-6.2f,upper.Read().center.y,.00001f);Assert.AreEqual(3f,upper.Mass);Assert.AreNotEqual(id,upper.Id);
            var expected=MapPhysicalBoxTools.Copy(a,new[]{upper,h},true).Select(x=>x.Read()).ToArray();Assert.AreEqual(4,Handles().Length);
            Undo.PerformUndo();Assert.AreEqual(2,Handles().Length);Undo.PerformRedo();Assert.AreEqual(4,Handles().Length);
            Assert.AreEqual(expected[0],Handles()[2].Read());Assert.AreEqual(expected[1],Handles()[3].Read());
            Assert.AreEqual(id,Handles()[0].Id);Assert.AreEqual(4,Handles().Select(x=>x.Id).Distinct().Count());
            Assert.IsEmpty(a.ValidatePlacement());Assert.Zero(definition.PhysicalBoxCount,"Only draft edited");
            Selection.activeGameObject=Handles()[1].gameObject;MapPhysicalBoxTools.DeleteSelected(a);
            CollectionAssert.AreEqual(new[]{0,1,2},Handles().Select(x=>x.Order));Undo.PerformUndo();Assert.AreEqual(4,Handles().Length);Assert.IsEmpty(a.ValidatePlacement());
        }
        [Test] public void InspectorCallbacksAfterBoxDeletion_DoNotAccessDestroyedSelection()
        {
            var h=MapPhysicalBoxTools.Add(a);
            var inspector=Own(UnityEditor.Editor.CreateEditor(h)) as MapPhysicalBoxInspector;
            Assert.NotNull(inspector);
            Object.DestroyImmediate(h.gameObject);
            Assert.IsTrue(inspector.target==null);
            Assert.DoesNotThrow(()=>inspector.OnInspectorGUI());
            var sceneGui=typeof(MapPhysicalBoxInspector).GetMethod("OnSceneGUI",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(sceneGui);Assert.DoesNotThrow(()=>sceneGui.Invoke(inspector,null));
        }
        [Test] public void TallPreset_IsAnEditableBox_WithAddUndoStackAndMirror()
        {
            var h=MapPhysicalBoxTools.Add(a,MapPhysicalBoxPreset.TallPillar);
            Assert.AreEqual(new Vector3(1.5f,4,1),h.transform.localScale);Assert.AreEqual(4f,h.Mass);
            Assert.IsTrue(h.PlayerPassThrough);Assert.IsTrue(h.Read().ToProp(h.Order).PlayerPassThrough);
            Assert.AreEqual(PropKind.LooseBox,h.Read().ToProp(h.Order).Kind);
            Undo.PerformUndo();Assert.Zero(Handles().Length);Undo.PerformRedo();h=Handles().Single();
            h.transform.localPosition=new Vector3(-3,-6.25f,0);h.transform.localScale=new Vector3(.75f,3.5f,1);h.Mass=2.75f;
            var upper=MapPhysicalBoxTools.Copy(a,new[]{h},false).Single();
            Assert.AreEqual(-2.75f,upper.transform.localPosition.y);Assert.AreEqual(h.Mass,upper.Mass);Assert.IsTrue(upper.PlayerPassThrough);
            Assert.AreNotEqual(h.Id,upper.Id);
            MapPhysicalBoxTools.Copy(a,new[]{h,upper},true);Assert.AreEqual(4,Handles().Length);
            Undo.PerformUndo();Assert.AreEqual(2,Handles().Length);Undo.PerformRedo();Assert.AreEqual(4,Handles().Length);
            Assert.IsEmpty(a.ValidatePlacement());Assert.AreEqual(4,Handles().Select(x=>x.Id).Distinct().Count());
            Assert.IsTrue(Handles().All(x=>x.PlayerPassThrough));
        }

        [TestCase(false)] [TestCase(true)]
        public void TallMixedStack_PresetPlacementSaveReopenAndUnregisteredRunnerUseSameBodies(bool adjacent)
        {
            var layout=MapTallPillarTests.Layout(adjacent,passThrough:true);
            for(int i=0;i<layout.Length;i++)
            {
                var h=MapPhysicalBoxTools.Add(a,(i==1||i==3||i==4)?MapPhysicalBoxPreset.TallPillar:MapPhysicalBoxPreset.Box);
                h.transform.localPosition=layout[i].center;
                Assert.AreEqual(layout[i].size,h.Read().size);Assert.AreEqual(layout[i].mass,h.Mass);
            }
            a.spawn0.position=new Vector3(-1.5f,-8,0);
            var catalog=Own(ScriptableObject.CreateInstance<MapCatalog>());a.catalog=catalog;
            string folder="Assets/_Project/LocalOnly/Map_CP2_1_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Mixed_Stack.asset";AssetDatabase.CreateAsset(definition,path);MapAuthoringOperations.Save(a);
            var expected=definition.ToArena();var values=a.ReadOrderedPhysicalBoxes();
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            Handles()[1].Mass=99;Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            CollectionAssert.AreEqual(values,a.ReadOrderedPhysicalBoxes());
            Assert.AreEqual(expected.ComputeGeometryChecksum(),a.definition.ToArena().ComputeGeometryChecksum());
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            var go=Child("Tall_Trial_Runner",a.transform).gameObject;go.SetActive(false);
            var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();
            Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual(1,runner.World.ArenaCount);Assert.IsEmpty(catalog.Maps);
            Assert.AreEqual(expected.ComputeGeometryChecksum(),runner.World.Arena.ComputeGeometryChecksum());
            Assert.AreEqual(layout.Length,runner.World.State.Props.Count);
            var view=Child("Tall_Trial_View",a.transform).gameObject.AddComponent<PropView>();
            a.shapes.Activate();view.Build(runner.World.Arena,a.definition.Metadata().ResolveTheme(a.palette),a.shapes);
            for(int i=0;i<layout.Length;i++)
            {
                var def=runner.World.Arena.GetProp(i);
                Assert.AreEqual(values[i].id,def.Id);Assert.AreEqual(values[i].mass,def.Mass);
                Assert.AreEqual(values[i].size.x,def.HalfSize.X*2);Assert.AreEqual(values[i].size.y,def.HalfSize.Y*2);
                Assert.AreEqual(PropJointKind.None,runner.World.Arena.GetJoint(i).Kind);
                Assert.AreEqual(values[i].playerPassThrough,def.PlayerPassThrough);
                var color=view.BodyTransform(i).GetComponent<SpriteRenderer>().color;
                Assert.AreEqual(PropView.BoxColor(a.definition.Metadata().ResolveTheme(a.palette).prop,i,values[i].playerPassThrough),color);
                if(values[i].playerPassThrough)Assert.AreEqual(PropView.PassThroughColor,Handles()[i].GetComponent<SpriteRenderer>().color);
            }
            var state=runner.World.State.Props;ref var body=ref PropState.Get(ref state,1);body.Position=new Vec2(-3,0);body.Rotation=new Vec2(.6f,.8f);
            view.Render(state,state,1);var bounds=view.BodyTransform(1).GetComponent<SpriteRenderer>().bounds;
            Assert.AreEqual(-3,bounds.center.x,.001f);Assert.AreEqual(0,bounds.center.y,.001f);
            Assert.AreEqual(.6f*1.5f+.8f*4,bounds.size.x,.001f);Assert.AreEqual(.8f*1.5f+.6f*4,bounds.size.y,.001f);
            Assert.AreEqual(layout.Length,view.GetComponentsInChildren<SpriteRenderer>().Length);
        }

        [Test] public void PassThroughPillar_ExcludesFootSnapButAllowsBoxTopAndSpawnOverlap()
        {
            var h=MapPhysicalBoxTools.Add(a,MapPhysicalBoxPreset.TallPillar);h.transform.localPosition=new Vector3(0,-6,0);
            Assert.IsFalse(MapPlacementSnap.TrySnapFoot(a,new Vector3(0,-3.9f,0),out _));
            var upper=MapPhysicalBoxTools.Create(a,Box(1,0,-3.25f),1);
            Assert.IsTrue(MapPlacementSnap.TrySnapBoxTop(a,upper,new Vector3(0,-3.3f,0),new Vector3(1.5f,1.5f,1),out var pos));
            Assert.AreEqual(-3.25f,pos.y,.0001f);
            var d=Own(Definition(new[]{h.Read()}));d.SaveLayout(new[]{d.Box(0)},new[]{h.Read()},new Vector2(0,-8),d.Spawn(1));
            Assert.IsEmpty(d.Validate());h.PlayerPassThrough=false;
            d.SaveLayout(new[]{d.Box(0)},new[]{h.Read()},d.Spawn(0),d.Spawn(1));
            Assert.That(d.Validate().Any(x=>x.Contains("시작 위치")));
        }
        [Test] public void PassThroughPreviewRemainsGrayAcrossThemeChangesAndToggleUndo()
        {
            var h=MapPhysicalBoxTools.Add(a,MapPhysicalBoxPreset.TallPillar);var sr=h.GetComponent<SpriteRenderer>();
            Assert.AreEqual(PropView.PassThroughColor,sr.color);Assert.Less(sr.color.a,1);
            for(int theme=0;theme<3;theme++)
            {
                var so=new SerializedObject(definition);so.FindProperty("theme").intValue=theme;so.ApplyModifiedPropertiesWithoutUndo();
                MapAuthoringOperations.RefreshPreviewColors(a);Assert.AreEqual(PropView.PassThroughColor,sr.color);
            }
            Undo.IncrementCurrentGroup();Undo.RecordObject(h,"통과 설정 시험");h.PlayerPassThrough=false;Undo.FlushUndoRecordObjects();
            MapAuthoringOperations.RefreshPreviewColors(a);Assert.AreNotEqual(PropView.PassThroughColor,sr.color);
            Undo.PerformUndo();MapAuthoringOperations.RefreshPreviewColors(a);
            Assert.IsTrue(h.Read().playerPassThrough);Assert.AreEqual(PropView.PassThroughColor,sr.color);
        }

        [Test] public void RunnerRejectsLegacyRecordingOnPassThroughMapWithoutReplacingWorld()
        {
            var h=MapPhysicalBoxTools.Create(a,Box(0,0,-7.4f),0);h.PlayerPassThrough=true;
            MapAuthoringOperations.Save(a);
            var arena=definition.ToArena();var p=arena.GetProp(0);p.Id=null;p.Mass=0;p.StartAwake=false;p.PlayerPassThrough=false;
            var old=arena.WithProps(new[]{p});var config=MapPhysicalBoxReactionTests.CurrentConfig();
            var state=SimState.CreateRound(config,old,77,old.SpawnFoot0,old.SpawnFoot1,MatchPhase.Fighting);
            var rec=InputRecording.Begin(config,old,77,state.Fighter0.Position,state.Fighter1.Position,MatchPhase.Fighting,config.Rules.Format());
            Assert.AreEqual(8,rec.WrittenVersion);
            var go=Child("Legacy_Trial_Runner",a.transform).gameObject;go.SetActive(false);
            var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(definition);runner.StartNewMatch();var before=runner.World;
            Assert.IsFalse(runner.PlayRecording(rec));Assert.AreSame(before,runner.World);
        }

        [Test] public void GridTopAndFootSnap_AreOptionalAndRejectBlockedSurface()
        {
            var baseBox=MapPhysicalBoxTools.Create(a,Box(0,0,-7.4f),0);
            var upper=MapPhysicalBoxTools.Create(a,Box(1,.12f,-6f),1);
            a.gridSnap=false;MapPlacementSnap.Apply(a,upper.transform);Assert.AreEqual(-6.2f,upper.transform.localPosition.y,.00001f);Assert.AreEqual(.12f,upper.transform.localPosition.x);
            a.boxTopSnap=false;upper.transform.localPosition=new Vector3(.38f,-5.38f,0);a.gridSnap=true;
            MapPlacementSnap.Apply(a,upper.transform);Assert.AreEqual(new Vector3(.5f,-5.5f,0),upper.transform.localPosition);Assert.AreEqual(new Vector3(1.25f,1.25f,1),upper.transform.localScale);
            a.gridSnap=false;upper.transform.localPosition=new Vector3(.1234f,-5.4321f,0);MapPlacementSnap.Apply(a,upper.transform);Assert.AreEqual(.1234f,upper.transform.localPosition.x);
            Assert.IsTrue(MapPlacementSnap.TrySnapFoot(a,new Vector3(0,-6.7f,0),out var foot));Assert.AreEqual(-6.8f,foot.y,.00001f);
            var wall=Child("Blocking",a.terrainRoot).gameObject.AddComponent<MapBoxHandle>();wall.Initialize(new MapBox{id="wall",kind=TerrainKind.Solid,minX=-.3f,maxX=.3f,minY=-6.7f,maxY=-6f},1);
            Assert.IsFalse(MapPlacementSnap.TrySnapBoxTop(a,upper,new Vector3(0,-6.2f,0),new Vector3(1.2f,1.2f,1),out _));
            Assert.AreEqual(0,baseBox.Order);
        }
        [Test] public void SavedReopenedDefinition_RetainsExactIdsMassOrderAndUnregisteredRunnerLoad()
        {
            MapPhysicalBoxTools.Create(a,Box(0,-2.1234f,-6.5f,1.23456f,2.75f),0);
            MapPhysicalBoxTools.Create(a,Box(1,3,-7.4f),1);
            string folder="Assets/_Project/LocalOnly/Map_CP2_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Persistence_Probe.asset";AssetDatabase.CreateAsset(definition,path);
            var catalog=Own(ScriptableObject.CreateInstance<MapCatalog>());a.catalog=catalog;
            Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));MapAuthoringOperations.Save(a);
            var expected=definition.ToArena();var values=Enumerable.Range(0,definition.PhysicalBoxCount).Select(definition.PhysicalBox).ToArray();
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));Handles()[0].Mass+=.5f;Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            Handles()[0].transform.SetAsLastSibling();CollectionAssert.AreEqual(values,a.ReadOrderedPhysicalBoxes());
            Assert.AreEqual(expected.ComputeGeometryChecksum(),Own(MapAuthoringOperations.Draft(a)).ToArena().ComputeGeometryChecksum());
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            var go=Child("Runner",a.transform).gameObject;go.SetActive(false);var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();
            Assert.AreEqual(expected.ComputeGeometryChecksum(),runner.World.Arena.ComputeGeometryChecksum());Assert.AreEqual(2,runner.World.State.Props.Count);Assert.IsEmpty(catalog.Maps);
            MapAuthoringOperations.Register(a);Assert.AreSame(a.definition,catalog.Find(a.definition.MatchName));
            runner.StartRecording(runner.World.Config.Rules.Format());var rec=runner.StopRecording();Assert.NotNull(rec);Assert.AreEqual(9,rec.WrittenVersion);Assert.IsTrue(runner.PlayRecording(rec));
            var changed=Own(Object.Instantiate(a.definition));var changedBoxes=Enumerable.Range(0,changed.PhysicalBoxCount).Select(changed.PhysicalBox).ToArray();changedBoxes[0].mass+=1;
            changed.SaveLayout(Enumerable.Range(0,changed.BoxCount).Select(changed.Box).ToArray(),changedBoxes,changed.Spawn(0),changed.Spawn(1));
            var previousWorld=runner.World;runner.ConfigureAuthoringPreview(changed);Assert.IsFalse(runner.PlayRecording(rec));Assert.AreSame(previousWorld,runner.World);
        }
        [Test] public void SavedSceneReopensWithTheSamePhysicalHandlesAndDefinition()
        {
            MapPhysicalBoxTools.Create(a,Box(0,-2.25f,-6.5f,1.125f,3.25f),0).PlayerPassThrough=true;
            MapAuthoringOperations.RefreshPreviewColors(a);
            MapPhysicalBoxTools.Create(a,Box(1,4,-7.4f),1);
            string folder="Assets/_Project/LocalOnly/Map_CP2_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            AssetDatabase.CreateAsset(definition,folder+"/Map.asset");MapAuthoringOperations.Save(a);
            var expected=a.ReadOrderedPhysicalBoxes();uint hash=definition.ToArena().ComputeGeometryChecksum();
            Scene previous=SceneManager.GetActiveScene();Scene savedScene=default;
            try{
                // Test Runner supplies an untitled 2D host with a default camera. Save that host
                // to a unique local file before additive scene work; reject any gameplay objects.
                if(string.IsNullOrEmpty(previous.path)){
                    Assert.That(previous.GetRootGameObjects().All(go=>go.name=="Main Camera" && go.GetComponents<Component>().All(c=>c is Transform||c is Camera||c is AudioListener)),
                        "Non-test unsaved scene must not be changed");
                    Assert.IsTrue(EditorSceneManager.SaveScene(previous,folder+"/Test_Runner_Host.unity"));
                }
                savedScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var copy=Object.Instantiate(a.gameObject);SceneManager.MoveGameObjectToScene(copy,savedScene);
                string path=folder+"/Authoring_Probe.unity";Assert.IsTrue(EditorSceneManager.SaveScene(savedScene,path));
                EditorSceneManager.CloseScene(savedScene,true);savedScene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                var reopened=savedScene.GetRootGameObjects().Select(go=>go.GetComponent<MapAuthoring>()).Single(x=>x!=null);
                CollectionAssert.AreEqual(expected,reopened.ReadOrderedPhysicalBoxes());Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(reopened));
                Assert.AreEqual(hash,Own(MapAuthoringOperations.Draft(reopened)).ToArena().ComputeGeometryChecksum());
            }finally{
                if(savedScene.IsValid()&&savedScene.isLoaded)EditorSceneManager.CloseScene(savedScene,true);
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            }
        }
        [TestCase("mass")] [TestCase("size")] [TestCase("overlap")] [TestCase("id")] [TestCase("spawn")] [TestCase("outside")]
        public void InvalidPhysicsCannotBeSaved(string issue)
        {
            var b=Box(0,0,-7.4f);var other=Box(1,3,-7.4f);
            switch(issue){case "mass":b.mass=float.NaN;break;case "size":b.size.x=-1;break;case "overlap":other.center=b.center;break;
                case "id":other.id=b.id;break;case "spawn":b.center.x=-12;break;case "outside":b.center.x=100;break;}
            var invalid=Own(Definition(new[]{b,other}));Assert.IsNotEmpty(invalid.Validate());Assert.Throws<InvalidOperationException>(()=>invalid.ToArena());
        }
        [Test] public void RotationDuplicateOrderAndMissingRootAreReported_OriginalIsProtected()
        {
            var h=MapPhysicalBoxTools.Add(a);h.transform.localRotation=Quaternion.Euler(0,0,10);Assert.IsNotEmpty(a.ValidatePlacement());
            h.transform.localRotation=Quaternion.identity;MapPhysicalBoxTools.Create(a,Box(3,5,0),0);Assert.IsNotEmpty(a.ValidatePlacement());
            var saved=Own(Definition(new[]{Box(0,0,-7.4f)}));a.definition=saved;a.physicsRoot=null;a.MarkLoaded();Assert.That(a.ValidatePlacement().Any(x=>x.Contains("묶음")));
            var original=AssetDatabase.LoadAssetAtPath<MapDefinition>(MapAuthoringOperations.Folder+"/BrawlOriginal.asset");
            Assert.Throws<InvalidOperationException>(()=>original.SaveLayout(new[]{original.Box(0)},new[]{Box(0,0,0)},original.Spawn(0),original.Spawn(1)));
        }
        [Test] public void DefinitionLimitAndVisualBoundsMatchThePhysics()
        {
            var tooMany=Enumerable.Range(0,PropState.Capacity+1).Select(i=>Box(i,i,0)).ToArray();Assert.That(Own(Definition(tooMany)).Validate().Any(e=>e.Contains("최대")));
            var d=Own(Definition(new[]{Box(0,0,-7.4f),Box(1,4,-3)}));var arena=d.ToArena();
            var view=Child("Prop_View",a.transform).gameObject.AddComponent<PropView>();a.shapes.Activate();view.Build(arena,d.Metadata().ResolveTheme(a.palette),a.shapes);
            var config=MapPhysicalBoxReactionTests.CurrentConfig();var w=new SimWorld(config,arena,SimState.CreateRound(config,arena,77,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.Fighting));
            for(int t=0;t<45;t++)w.Step(default,default);view.Render(w.State.Props,w.State.Props,1);
            Assert.AreEqual(arena.DynamicPropCount,view.GetComponentsInChildren<SpriteRenderer>().Length,
                "각 상자는 판정 크기의 몸체 하나만 표시하며 틈처럼 보이는 이음매를 더하지 않는다.");
            for(int i=0;i<arena.DynamicPropCount;i++){
                ref readonly var body=ref PropState.At(w.State.Props,i);var tr=view.BodyTransform(i);var bounds=tr.GetComponent<SpriteRenderer>().bounds;
                Assert.AreEqual(body.Position.X,tr.localPosition.x);Assert.AreEqual(body.Position.Y,tr.localPosition.y);
                Assert.AreEqual(arena.GetProp(i).HalfSize.X*2,bounds.size.x,.001f);Assert.AreEqual(arena.GetProp(i).HalfSize.Y*2,bounds.size.y,.001f);
                Assert.AreEqual(PropJointKind.None,arena.GetJoint(i).Kind);
            }
        }
    }
}
