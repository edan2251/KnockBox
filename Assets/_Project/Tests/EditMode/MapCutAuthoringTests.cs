using System;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
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
    public class MapCutAuthoringTests
    {
        Scene scene;MapAuthoring authoring;MapDefinition definition;Object[] selected;
        Transform Child(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(authoring.transform,false);return go.transform;}
        [SetUp] public void Setup()
        {
            selected=Selection.objects;Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("CP4_Editor_Test");SceneManager.MoveGameObjectToScene(go,scene);
            authoring=go.AddComponent<MapAuthoring>();authoring.terrainRoot=Child("Terrain");authoring.spawn0=Child("P1");authoring.spawn1=Child("P2");
            authoring.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            authoring.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            definition=ScriptableObject.CreateInstance<MapDefinition>();
            definition.Import(MapConvexTerrainTests.Arena(new MapBox{id="source",kind=TerrainKind.Solid,minX=-4,minY=-4,maxX=4,maxY=4}),new ArenaMeta(),"cp4_editor_trial","CP4_Editor_Trial");
            authoring.definition=definition;MapAuthoringOperations.Open(authoring);
        }
        [TearDown] public void Teardown()
        {
            MapTerrainCut.Cancel();Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            EditorSceneManager.ClosePreviewScene(scene);if(definition!=null&&!AssetDatabase.Contains(definition))Object.DestroyImmediate(definition);
            Selection.objects=selected.Where(x=>x!=null).ToArray();
        }
        MapBoxHandle Handle()=>authoring.terrainRoot.GetComponentsInChildren<MapBoxHandle>().Single(x=>x.Order==1);
        void Cut()=>MapTerrainCut.Apply(Handle(),new Vector2(0,4),new Vector2(4,0),false);
        static void AssertVertices(MapBox box,SpriteRenderer renderer)
        {
            var actual=renderer.sprite.vertices.Select(p=>(Vector2)renderer.transform.TransformPoint(p)).ToArray();
            Assert.AreEqual(box.vertices.Length,actual.Length);
            for(int i=0;i<actual.Length;i++)Assert.Less(Vector2.Distance(box.vertices[i],actual[i]),.00002f,"vertex "+i);
            Assert.AreEqual((box.vertices.Length-2)*3,renderer.sprite.triangles.Length);
        }
        [Test] public void ApplyIsOneUndo_RedoAndRepeatPreserveIdOrderAndSprite()
        {
            var h=Handle();string id=h.Id;var old=JsonUtility.ToJson(h.Read());Selection.activeGameObject=h.gameObject;
            MapTerrainCut.Begin(h);MapTerrainCut.Cancel();Assert.AreEqual(old,JsonUtility.ToJson(h.Read()));
            Cut();Assert.AreEqual(id,h.Id);Assert.AreEqual(1,h.Order);Assert.IsTrue(h.Read().IsPolygon);AssertVertices(h.Read(),h.GetComponent<SpriteRenderer>());
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(old,JsonUtility.ToJson(h.Read()));
            Undo.PerformRedo();h=Handle();Assert.IsTrue(h.Read().IsPolygon);AssertVertices(h.Read(),h.GetComponent<SpriteRenderer>());
            MapTerrainCut.Apply(h,new Vector2(-4,0),new Vector2(0,4),false);
            Assert.AreEqual(1,h.Order);Assert.AreEqual(id,h.Id);Assert.IsFalse(h.Read().ToBox().Polygon.Contains(new Vec2(-3,3)));
            Undo.PerformUndo();Assert.IsTrue(h.Read().ToBox().Polygon.Contains(new Vec2(-3,3)));
        }
        [Test] public void ResizeMoveMirrorAndSpawnSnapUsePolygonRatherThanOldBox()
        {
            Cut();var h=Handle();var mirror=MapMirrorTools.Copy(authoring,new[]{h}).Created.Single();Assert.AreNotEqual(h.Id,mirror.Id);
            Assert.IsTrue(mirror.Read().ToBox().Polygon.Contains(new Vec2(3,3)));Assert.IsFalse(mirror.Read().ToBox().Polygon.Contains(new Vec2(-3,3)));
            Object.DestroyImmediate(mirror.gameObject);
            Assert.IsTrue(MapPlacementSnap.TrySnapFoot(authoring,new Vector3(3,1.5f),out var foot));Assert.AreEqual(1.36f,foot.y,.001f);
            var d=Object.Instantiate(definition);try
            {
                d.SaveGeometry(new[]{definition.Box(0),h.Read()},new Vector2(3,3),definition.Spawn(1));
                Assert.IsEmpty(d.Validate(),"spawn in removed corner is valid");
                d.SaveGeometry(new[]{definition.Box(0),h.Read()},new Vector2(2,1),definition.Spawn(1));Assert.IsNotEmpty(d.Validate());
            }finally{Object.DestroyImmediate(d);}
            h.transform.localPosition=new Vector3(-1.13f,-.37f,0);h.transform.localScale=new Vector3(6.27f,4.63f,1);
            var changed=h.Read();Assert.DoesNotThrow(()=>changed.ToBox());AssertVertices(changed,h.GetComponent<SpriteRenderer>());
            var draft=MapAuthoringOperations.Draft(authoring);try{Assert.IsEmpty(draft.Validate());}finally{Object.DestroyImmediate(draft);}
        }
        [Test] public void SaveReopenSceneAndMainTrialPreserveCutGeometryAndRopeReference()
        {
            Cut();var physical=MapPhysicalBoxTools.Add(authoring);physical.transform.localPosition=new Vector3(-8,-6.75f);
            var rope=MapRopeTools.AddTo(authoring,physical);rope.BreakForce=600;
            string folder="Assets/_Project/LocalOnly/Map_CP4_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            AssetDatabase.CreateAsset(definition,folder+"/Map.asset");MapAuthoringOperations.Save(authoring);
            string expected=JsonUtility.ToJson(definition);uint hash=definition.ToArena().ComputeGeometryChecksum();
            MapAuthoringOperations.Open(authoring);Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(authoring));
            Assert.AreEqual(expected,JsonUtility.ToJson(definition));AssertVertices(Handle().Read(),Handle().GetComponent<SpriteRenderer>());
            var host=SceneManager.GetActiveScene();Scene opened=default;
            try
            {
                if(string.IsNullOrEmpty(host.path))
                {
                    Assert.IsTrue(host.GetRootGameObjects().All(x=>x.name=="Main Camera"&&x.GetComponents<Component>().All(c=>c is Transform||c is Camera||c is AudioListener)));
                    Assert.IsTrue(EditorSceneManager.SaveScene(host,folder+"/Test_Runner_Host.unity"));
                }
                opened=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var copy=Object.Instantiate(authoring.gameObject);SceneManager.MoveGameObjectToScene(copy,opened);
                string path=folder+"/Cut_Authoring.unity";Assert.IsTrue(EditorSceneManager.SaveScene(opened,path));
                EditorSceneManager.CloseScene(opened,true);opened=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                var a=opened.GetRootGameObjects().Select(x=>x.GetComponent<MapAuthoring>()).Single(x=>x!=null);
                Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));Assert.AreEqual(hash,a.definition.ToArena().ComputeGeometryChecksum());
                var h=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>().Single(x=>x.Order==1);AssertVertices(h.Read(),h.GetComponent<SpriteRenderer>());
                var r=MapRopeTools.Handles(a).Single();Assert.NotNull(r.Target);Assert.AreEqual(a.definition.Rope(0).boxId,r.Target.Id);
                var runnerGo=new GameObject("CP4_Trial_Runner");SceneManager.MoveGameObjectToScene(runnerGo,opened);runnerGo.SetActive(false);
                var runner=runnerGo.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();
                Assert.AreEqual(hash,runner.World.Arena.ComputeGeometryChecksum());Assert.AreEqual(1,runner.World.ArenaCount);Assert.IsTrue(runner.World.Arena.GetBox(1).IsPolygon);
                Assert.AreEqual(1,runner.World.Arena.GraphRopeCount);Assert.AreEqual(0,runner.World.Arena.GetGraphRope(0).Body);Assert.IsTrue(runner.World.Arena.GetGraphRope(0).Joint.UsesTension);Assert.AreEqual(a.definition.Rope(0).length,runner.World.Arena.GetGraphRope(0).Joint.Length);Assert.AreEqual(AssetDatabase.AssetPathToGUID(folder+"/Map.asset"),MapAuthoringOperations.SavedTrialGuid(a));
            }
            finally{if(opened.IsValid()&&opened.isLoaded)EditorSceneManager.CloseScene(opened,true);if(host.IsValid()&&host.isLoaded)SceneManager.SetActiveScene(host);}
        }
        [Test] public void TerrainRenderingAndShadowUseSameClippedBoundary()
        {
            Cut();MapAuthoringOperations.Save(authoring);var arena=definition.ToArena();var theme=definition.Metadata().ResolveTheme(authoring.palette);
            var view=Child("Terrain_View").gameObject.AddComponent<ArenaView>();view.Build(arena,theme,authoring.palette,authoring.shapes,true);
            var sr=view.GetComponentsInChildren<ConvexTerrainSprite>().Single().GetComponent<SpriteRenderer>();AssertVertices(definition.Box(1),sr);
            view.PresentTransition(0,true);Assert.Greater(sr.transform.position.y,10);view.PresentTransition(1,true);AssertVertices(definition.Box(1),sr);
            var background=Child("Background").gameObject.AddComponent<BackgroundView>();background.Build(arena,theme,authoring.palette,authoring.shapes,new Vector2(10,10));
            Assert.Zero(background.ShadowAmount(new Vector2(3,3)),"removed corner remains lit");
            Assert.AreEqual(1,background.ShadowAmount(new Vector2(0,-6)),"retained polygon casts shadow");
            var projector=Child("Polygon_Shadow");var mesh=new ProjectedShadowMesh(projector.gameObject,1,background.ShadowMaterial,0);
            try{mesh.Begin();mesh.Polygon(arena.GetBox(1).Polygon,new Vector2(10,10),100);mesh.Commit();Assert.AreEqual(1,mesh.Count);
                var vertices=projector.GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.IsTrue(definition.Box(1).vertices.Any(x=>Vector2.Distance(x,vertices[0])<.0001f));Assert.IsTrue(definition.Box(1).vertices.Any(x=>Vector2.Distance(x,vertices[1])<.0001f));
            }finally{mesh.Dispose();}
        }
    }
}
