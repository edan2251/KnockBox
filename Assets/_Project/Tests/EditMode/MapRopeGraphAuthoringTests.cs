using System;
using System.Collections.Generic;
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
    public class MapRopeGraphAuthoringTests
    {
        Scene scene;MapAuthoring a;MapDefinition d;Object[] selected;readonly List<Object> owned=new List<Object>();
        T Own<T>(T x) where T:Object{owned.Add(x);return x;}
        Transform Child(string name,Transform parent)
        {var g=new GameObject(name);SceneManager.MoveGameObjectToScene(g,scene);g.transform.SetParent(parent,false);return g.transform;}
        [SetUp] public void Setup()
        {
            selected=Selection.objects;Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Graph_Authoring");SceneManager.MoveGameObjectToScene(go,scene);
            a=go.AddComponent<MapAuthoring>();a.terrainRoot=Child("Terrain",a.transform);a.spawn0=Child("P1",a.transform);a.spawn1=Child("P2",a.transform);
            a.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");a.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            d=Own(MapPhysicalBoxAuthoringTests.Definition(Array.Empty<MapPhysicalBox>()));a.definition=d;MapAuthoringOperations.Open(a);
        }
        [TearDown] public void Teardown()
        {
            Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();EditorSceneManager.ClosePreviewScene(scene);
            foreach(var x in owned)if(x!=null&&!AssetDatabase.Contains(x))Object.DestroyImmediate(x);owned.Clear();Selection.objects=selected;
        }
        MapPhysicalBoxHandle Box(Vector2 p,bool connector=false)
        {var h=MapPhysicalBoxTools.Add(a,connector?MapPhysicalBoxPreset.Connector:MapPhysicalBoxPreset.Box);h.transform.localPosition=p;return h;}
        MapRopeHandle[] Layout()
        {
            var top=Box(new Vector2(-4,2),true);var lower=Box(new Vector2(-4,-2));
            return new[]{MapRopeTools.AddTo(a,top),MapRopeTools.Connect(a,top,lower),MapRopeTools.AddTo(a,lower)};
        }
        [Test] public void PresetIsIndependentStandableSolidAndHasOutlineInEditor()
        {
            var b=Box(Vector2.zero,true);Assert.IsTrue(b.Connector);Assert.IsFalse(b.PlayerPassThrough);Assert.AreEqual(.15f,b.ConnectorSpeedLoss);
            Assert.AreEqual(new Vector2(.65f,.65f),b.Read().size);Assert.IsTrue(b.GetComponent<SpriteRenderer>().enabled);
            Assert.AreEqual(new Color32(34,34,34,130),(Color32)b.GetComponent<SpriteRenderer>().color,"requested translucent fill");
            Assert.AreEqual(4,b.transform.Find("Connector_Outline").childCount);
            b.transform.localScale=new Vector3(1.2f,.8f,1);b.Mass=2;b.ConnectorSpeedLoss=.25f;MapAuthoringOperations.RefreshPreviewColors(a);
            var p=Own(MapAuthoringOperations.Draft(a)).ToArena().GetProp(0);Assert.IsTrue(p.IsConnector);Assert.AreEqual(2,p.Mass);Assert.AreEqual(.25f,p.ConnectorSpeedLoss);
        }
        [Test] public void MultipleRopesBodyEndpointsFollowMovementAndKeepExplicitIds()
        {
            var r=Layout();Assert.IsTrue(r.All(h=>h.Graph));Assert.AreSame(r[0].Target,r[1].Source);Assert.AreSame(r[2].Target,r[1].Target);
            var before=r[1].Anchor;r[1].Source.transform.position+=Vector3.up;
            Assert.AreEqual(before+Vector2.up,r[1].Anchor);Assert.IsTrue(MapRopeTools.NeedsLengthFit(r[1]));MapRopeTools.FitTooShort(a);
            var draft=Own(MapAuthoringOperations.Draft(a));Assert.IsEmpty(draft.Validate());var arena=draft.ToArena();
            Assert.AreEqual(3,arena.GraphRopeCount);Assert.AreEqual(0,arena.GetGraphRope(1).Joint.OtherBody);Assert.AreEqual(1,arena.GetGraphRope(1).Body);
        }
        [TestCase(false)] [TestCase(true)]
        public void CopyAndMirrorRemapBothEndpointsAndUndoTogether(bool mirror)
        {
            var r=Layout();var copied=MapPhysicalBoxTools.Copy(a,new[]{r[0].Target,r[1].Target},mirror);var all=MapRopeTools.Handles(a);
            Assert.AreEqual(6,all.Length);Assert.AreSame(copied[0],all[4].Source);Assert.AreSame(copied[1],all[4].Target);
            Assert.AreEqual(copied[0].Id,all[4].Read().sourceBoxId);Assert.AreEqual(copied[1].Id,all[4].Read().boxId);
            Assert.AreNotEqual(r[1].Id,all[4].Id);Assert.AreEqual(r[1].Length,all[4].Length);
            Assert.AreEqual(mirror?-r[1].SourceLocalAnchor.x:r[1].SourceLocalAnchor.x,all[4].SourceLocalAnchor.x);
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(3,MapRopeTools.Handles(a).Length);
            Undo.PerformRedo();Assert.AreEqual(6,MapRopeTools.Handles(a).Length);
        }
        [Test] public void PartialCopyDoesNotConnectNewBodyBackToOriginalAssembly()
        {
            var r=Layout();var copied=MapPhysicalBoxTools.Copy(a,new[]{r[1].Target},true);
            var all=MapRopeTools.Handles(a);Assert.AreEqual(4,all.Length);Assert.AreSame(copied[0],all[3].Target);Assert.IsNull(all[3].Source);
        }
        [Test] public void DeletingEitherEndpointRemovesOnlyIncidentRopesAndUndoRestoresReferences()
        {
            var r=Layout();Selection.activeGameObject=r[0].Target.gameObject;MapPhysicalBoxTools.DeleteSelected(a);
            Assert.AreEqual(1,MapRopeTools.Handles(a).Length);Assert.IsEmpty(a.ValidatePlacement());
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(3,MapRopeTools.Handles(a).Length);Assert.IsEmpty(a.ValidatePlacement());
        }
        [Test] public void DefinitionSaveReopenAndMainTrialRetainGraphAndExactLineEndpoints()
        {
            var r=Layout();r[1].SourceLocalAnchor=new Vector2(.1f,-.3f);r[1].LocalAnchor=new Vector2(-.2f,.7f);MapRopeTools.FitTooShort(a);
            string folder="Assets/_Project/LocalOnly/Map_Graph_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Graph_Map.asset";AssetDatabase.CreateAsset(d,path);MapAuthoringOperations.Save(a);
            var expected=a.ReadOrderedRopes();uint hash=d.ToArena().ComputeGeometryChecksum();
            r[1].Length+=1;Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            CollectionAssert.AreEqual(expected,a.ReadOrderedRopes());Assert.AreEqual(hash,a.definition.ToArena().ComputeGeometryChecksum());Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            var go=Child("Trial_Runner",a.transform).gameObject;go.SetActive(false);var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual(hash,runner.World.Arena.ComputeGeometryChecksum());
            var view=Child("Graph_View",a.transform).gameObject.AddComponent<PropView>();a.shapes.Activate();view.Build(runner.World.Arena,d.Metadata().ResolveTheme(a.palette),a.shapes);
            var state=runner.World.State.Props;state.B00.Rotation=new Vec2(.6f,.8f);state.B00.Position.X+=1;state.B01.Position.X-=.5f;view.Render(state,state,1);
            var lines=view.GetComponentsInChildren<SpriteRenderer>().Where(x=>x.name=="Rope").ToArray();Assert.AreEqual(3*RopePath.Segments,lines.Length);
            var edge=runner.World.Arena.GetGraphRope(1);var start=(Vector2)lines[RopePath.Segments].transform.TransformPoint(new Vector3(-.5f,0));
            var end=(Vector2)lines[2*RopePath.Segments-1].transform.TransformPoint(new Vector3(.5f,0));
            var ea=state.B00.ToWorld(edge.Joint.OtherLocal);var eb=state.B01.ToWorld(edge.Joint.LocalAnchor);
            Assert.Less(Vector2.Distance(start,new Vector2(ea.X,ea.Y)),.001f);Assert.Less(Vector2.Distance(end,new Vector2(eb.X,eb.Y)),.001f);
            ConnectorBodies.Destroy(ref state,runner.World.Arena,0);view.Render(state,state,1);
            Assert.IsFalse(view.BodyTransform(0).gameObject.activeSelf);Assert.IsTrue(view.BodyTransform(1).gameObject.activeSelf);
            Assert.AreEqual(RopePath.Segments,lines.Count(x=>x.enabled));
        }
        [TestCase("self")] [TestCase("missing")] [TestCase("point")] [TestCase("pass_through")] [TestCase("mode")]
        public void InvalidGraphDefinitionsAreBlockedWithoutSaving(string reason)
        {
            var r=Layout();switch(reason){case "self":r[1].Source=r[1].Target;break;case "missing":r[1].Source=null;r[1].SourceIsBody=true;break;
                case "point":r[1].SourceLocalAnchor=new Vector2(9,0);break;case "pass_through":r[0].Target.PlayerPassThrough=true;break;case "mode":r[1].Mode=RopeBreakMode.Connector;break;}
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.Save(a));
        }
    }
}
