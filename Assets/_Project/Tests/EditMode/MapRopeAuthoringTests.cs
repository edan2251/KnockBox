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
    public class MapRopeAuthoringTests
    {
        Scene scene;MapAuthoring a;MapDefinition d;Object[] selected;
        readonly List<Object> owned=new List<Object>();
        T Own<T>(T x) where T:Object{owned.Add(x);return x;}
        Transform Child(string name,Transform parent)
        {var g=new GameObject(name);SceneManager.MoveGameObjectToScene(g,scene);g.transform.SetParent(parent,false);return g.transform;}
        MapPhysicalBoxHandle[] Boxes()=>a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true).OrderBy(h=>h.Order).ToArray();
        [SetUp] public void Setup()
        {
            selected=Selection.objects;Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("CP3_Authoring");SceneManager.MoveGameObjectToScene(go,scene);
            a=go.AddComponent<MapAuthoring>();a.terrainRoot=Child("Terrain",a.transform);a.spawn0=Child("P1",a.transform);a.spawn1=Child("P2",a.transform);
            a.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");a.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            d=Own(MapPhysicalBoxAuthoringTests.Definition(Array.Empty<MapPhysicalBox>()));a.definition=d;MapAuthoringOperations.Open(a);
        }
        [TearDown] public void Teardown()
        {
            Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();EditorSceneManager.ClosePreviewScene(scene);
            foreach(var x in owned)if(x!=null&&!AssetDatabase.Contains(x))Object.DestroyImmediate(x);owned.Clear();Selection.objects=selected;
        }
        MapRopeHandle Add()
        {
            var box=MapPhysicalBoxTools.Add(a);box.transform.localPosition=new Vector3(-3,-3,0);
            return MapRopeTools.AddTo(a,box);
        }
        [Test] public void CreationCopyMirrorAndUndoKeepExplicitTargetReferences()
        {
            var r=Add();Assert.AreEqual("Rope_0",r.name);Assert.AreEqual("Physics_Box_0",r.Target.name);
            r.LocalAnchor=new Vector2(.3f,.75f);r.transform.position+=Vector3.right*.3f;var original=r.Read();
            var upper=MapPhysicalBoxTools.Copy(a,new[]{r.Target},false).Single();
            var copied=MapRopeTools.Handles(a)[1];Assert.AreSame(upper,copied.Target);Assert.AreNotEqual(r.Id,copied.Id);
            Assert.AreEqual(original.anchor+Vector2.up*1.5f,copied.Read().anchor);Assert.AreEqual(original.localAnchor,copied.LocalAnchor);
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(1,Boxes().Length);Assert.AreEqual(1,MapRopeTools.Handles(a).Length);
            Undo.PerformRedo();Assert.AreEqual(2,Boxes().Length);Assert.AreEqual(2,MapRopeTools.Handles(a).Length);
            var mirrors=MapPhysicalBoxTools.Copy(a,Boxes(),true);var ropes=MapRopeTools.Handles(a);
            Assert.AreEqual(4,ropes.Length);Assert.AreEqual(mirrors[0].Id,ropes[2].Read().boxId);
            Assert.AreEqual(-original.anchor.x,ropes[2].Read().anchor.x);Assert.AreEqual(-original.localAnchor.x,ropes[2].LocalAnchor.x);
            Assert.IsEmpty(a.ValidatePlacement());Assert.IsEmpty(Own(MapAuthoringOperations.Draft(a)).Validate());
        }
        [Test] public void MissingTargetCanBeFixedByDeletingRope_AndDedicatedBoxDeletionUndoesTogether()
        {
            var rope=Add();var box=rope.Target;
            Selection.activeGameObject=box.gameObject;MapPhysicalBoxTools.DeleteSelected(a);
            Assert.Zero(Boxes().Length);Assert.Zero(MapRopeTools.Handles(a).Length);
            Undo.PerformUndo();Assert.AreEqual(1,Boxes().Length);Assert.AreEqual(1,MapRopeTools.Handles(a).Length);
            rope=MapRopeTools.Handles(a).Single();Selection.objects=Array.Empty<Object>();Undo.DestroyObjectImmediate(rope.Target.gameObject);
            Assert.IsNotEmpty(a.ValidatePlacement());MapRopeTools.Delete(a,new[]{rope});Assert.IsEmpty(a.ValidatePlacement());
        }
        [Test] public void RopeOrderRepairAndGridPreserveLengthAndId()
        {
            var first=Add();MapPhysicalBoxTools.Copy(a,new[]{first.Target},true);
            var other=MapRopeTools.Handles(a)[1];string id=other.Id;float length=other.Length;other.SetOrder(7);
            Assert.IsNotEmpty(a.ValidatePlacement());MapAuthoringOperations.RepairOrder(a);
            Assert.AreEqual(1,other.Order);Assert.AreEqual(id,other.Id);Assert.AreEqual(length,other.Length);
            other.transform.position=new Vector3(2.12f,1.37f,0);a.gridSnap=true;MapPlacementSnap.Apply(a,other.transform);
            Assert.AreEqual(new Vector3(2,1.25f,0),other.transform.position);Assert.AreEqual(Vector3.one,other.transform.localScale);
        }
        [Test] public void SavedReopenedMapAndMainTrialUseSameRopeRulesAndLineEndpoints()
        {
            var h=Add();h.Graph=false;h.Mode=RopeBreakMode.LegacyTension;h.Breakable=true;h.BreakForce=420;h.Length=5;h.LocalAnchor=new Vector2(.3f,.65f);
            string folder="Assets/_Project/LocalOnly/Map_CP3_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Rope_Map.asset";AssetDatabase.CreateAsset(d,path);MapAuthoringOperations.Save(a);
            var expected=a.ReadOrderedRopes();uint hash=d.ToArena().ComputeGeometryChecksum();
            h.Length=9;Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            CollectionAssert.AreEqual(expected,a.ReadOrderedRopes());Assert.AreEqual(hash,a.definition.ToArena().ComputeGeometryChecksum());
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            var go=Child("Rope_Trial_Runner",a.transform).gameObject;go.SetActive(false);var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual(1,runner.World.ArenaCount);
            Assert.AreEqual(hash,runner.World.Arena.ComputeGeometryChecksum());var joint=runner.World.Arena.GetJoint(0);
            Assert.AreEqual(420,joint.BreakForce);Assert.AreEqual(5,joint.Length);Assert.AreEqual(-1,joint.OtherBody);
            var view=Child("Rope_View",a.transform).gameObject.AddComponent<PropView>();a.shapes.Activate();view.Build(runner.World.Arena,d.Metadata().ResolveTheme(a.palette),a.shapes);
            var state=runner.World.State.Props;state.B00.Rotation=new Vec2(.6f,.8f);view.Render(state,state,1);
            var lines=view.GetComponentsInChildren<SpriteRenderer>().Where(x=>x.name=="Rope").ToArray();Assert.AreEqual(RopePath.Segments,lines.Length);
            var start=(Vector2)lines[0].transform.TransformPoint(new Vector3(-.5f,0));
            var end=(Vector2)lines.Last().transform.TransformPoint(new Vector3(.5f,0));
            Assert.That(Vector2.Distance(start,new Vector2(joint.WorldAnchor.X,joint.WorldAnchor.Y)),Is.LessThan(.001f));
            var expectedEnd=state.B00.ToWorld(joint.LocalAnchor);
            Assert.That(Vector2.Distance(end,new Vector2(expectedEnd.X,expectedEnd.Y)),Is.LessThan(.001f));
            state.B00.Broken=1;view.Render(state,state,1);Assert.IsTrue(lines.All(x=>!x.enabled));
        }
        [Test] public void ShortRopeSaveErrorShowsTargetAndDistance_RepairHasOneUndoAndPreservesSlack()
        {
            var r=Add();r.transform.position+=Vector3.up*.25f;
            var copy=MapPhysicalBoxTools.Copy(a,new[]{r.Target},true).Single();
            var slack=MapRopeTools.Handles(a)[1];slack.Length=9;
            var boxes=a.ReadOrderedPhysicalBoxes();var before=a.ReadOrderedRopes();string source=JsonUtility.ToJson(d);
            string reason=MapAuthoringOperations.PendingSaveReason(a);
            StringAssert.Contains("Rope_0",reason);StringAssert.Contains("4.25",reason);
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.Save(a));
            Assert.AreEqual(source,JsonUtility.ToJson(d),"failed save must not change the definition");
            Assert.AreEqual(1,MapRopeTools.FitTooShort(a));Assert.AreEqual(4.25f,r.Length);Assert.AreEqual(9,slack.Length);
            CollectionAssert.AreEqual(boxes,a.ReadOrderedPhysicalBoxes());
            var after=a.ReadOrderedRopes();for(int i=0;i<after.Length;i++)
            {
                Assert.AreEqual(before[i].anchor,after[i].anchor);Assert.AreEqual(before[i].localAnchor,after[i].localAnchor);
                Assert.AreEqual(before[i].boxId,after[i].boxId);Assert.AreEqual(before[i].breakForce,after[i].breakForce);
            }
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(4,r.Length);Assert.AreEqual(9,slack.Length);
            Undo.PerformRedo();Assert.AreEqual(4.25f,r.Length);Assert.AreEqual(9,slack.Length);
            Assert.Zero(MapRopeTools.FitTooShort(a));Assert.IsEmpty(Own(MapAuthoringOperations.Draft(a)).Validate());
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator SavedRopeMapStaysReadyAfterEditorUpdatesAndReimport()
        {
            var r=Add();r.transform.position+=Vector3.up*.25f;MapRopeTools.FitTooShort(a);
            string folder="Assets/_Project/LocalOnly/Map_Save_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Map.asset";AssetDatabase.CreateAsset(d,path);MapAuthoringOperations.Save(a);
            var expected=a.ReadOrderedRopes();uint hash=d.ToArena().ComputeGeometryChecksum();
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            yield return null;yield return null;Undo.FlushUndoRecordObjects();
            Assert.IsFalse(EditorUtility.IsDirty(d));Assert.IsNull(MapAuthoringOperations.PendingSaveReason(a));
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            CollectionAssert.AreEqual(expected,a.ReadOrderedRopes());Assert.AreEqual(hash,a.definition.ToArena().ComputeGeometryChecksum());
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
        }
        [Test] public void RopeLengthRepairDoesNotHideOtherInvalidPlacement()
        {
            var r=Add();r.transform.position+=Vector3.up;r.Target.Mass=-1;
            Assert.AreEqual(1,MapRopeTools.FitTooShort(a));
            StringAssert.Contains("질량",MapAuthoringOperations.PendingSaveReason(a));
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.Save(a));Assert.Zero(d.PhysicalBoxCount);
        }
        [Test] public void DirtyThemeAndExternallySavedGeometryStillRequireExplicitSaveOrReload()
        {
            Add();string folder="Assets/_Project/LocalOnly/Map_Save_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            AssetDatabase.CreateAsset(d,folder+"/Map.asset");MapAuthoringOperations.Save(a);
            d.Metadata().MapTheme.mode=MapThemeMode.Custom;EditorUtility.SetDirty(d);
            StringAssert.Contains("맵 설정",MapAuthoringOperations.PendingSaveReason(a));MapAuthoringOperations.Save(a);
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            d.SaveGeometry(Enumerable.Range(0,d.BoxCount).Select(d.Box).ToArray(),d.Spawn(0)+Vector2.right,d.Spawn(1));
            EditorUtility.SetDirty(d);AssetDatabase.SaveAssetIfDirty(d);
            StringAssert.Contains("원본 맵",MapAuthoringOperations.PendingSaveReason(a));
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.SavedTrialGuid(a));
            MapAuthoringOperations.Open(a);Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
        }

        [TestCase("id")] [TestCase("target")] [TestCase("double")] [TestCase("length")] [TestCase("point")] [TestCase("force")] [TestCase("nan")]
        public void InvalidRopeCannotLoadIntoSimulation(string issue)
        {
            var map=Own(MapIndependentRopeTests.Definition());var r=map.Rope(0);var list=new List<MapRope>();
            switch(issue)
            {
                case "id":r.id=map.PhysicalBox(0).id;break;
                case "target":r.boxId="missing";break;
                case "double":list.Add(r);r.id+="2";break;
                case "length":r.length=1;break;
                case "point":r.localAnchor.x=100;break;
                case "force":r.breakForce=0;break;
                case "nan":r.anchor.x=float.NaN;break;
            }
            list.Add(r);map.SaveLayout(new[]{map.Box(0)},new[]{map.PhysicalBox(0)},list.ToArray(),map.Spawn(0),map.Spawn(1));
            Assert.IsNotEmpty(map.Validate());Assert.Throws<InvalidOperationException>(()=>map.ToArena());
        }
        [Test] public void DefaultNormalAndConnectorModesCopyMirrorUndoAndSaveWithoutLosingReferences()
        {
            var h=Add();Assert.AreEqual(RopeBreakMode.Unbreakable,h.Mode);Assert.IsFalse(h.Breakable);
            Assert.AreEqual(.15f,h.ConnectorSpeedLoss);Assert.Zero(Own(MapAuthoringOperations.Draft(a)).ToArena().GetJoint(0).BreakForce);
            // A legacy serialized handle has no connector settings. A multi-selection must initialize each one.
            var mirror=MapPhysicalBoxTools.Copy(a,new[]{h.Target},true).Single();var old=MapRopeTools.Handles(a)[1];
            old.Mode=RopeBreakMode.LegacyTension;old.ConnectorPosition=old.ConnectorSize=old.ConnectorSpeedLoss=0;
            MapRopeTools.SetMode(new[]{h,old},RopeBreakMode.Connector);
            Assert.AreEqual(.55f,old.ConnectorSize);Assert.AreEqual(.15f,old.ConnectorSpeedLoss);
            Selection.objects=Array.Empty<Object>();Undo.PerformUndo();Assert.AreEqual(RopeBreakMode.Unbreakable,h.Mode);Assert.AreEqual(RopeBreakMode.LegacyTension,old.Mode);
            Undo.PerformRedo();Assert.AreEqual(RopeBreakMode.Connector,h.Mode);Assert.AreEqual(RopeBreakMode.Connector,old.Mode);
            h.ConnectorPosition=.7f;h.ConnectorSize=.6f;h.ConnectorSpeedLoss=.3f;
            h.Target.transform.position+=Vector3.left*3;h.transform.position+=Vector3.left*3;
            MapPhysicalBoxTools.Copy(a,new[]{h.Target},true);var copied=MapRopeTools.Handles(a)[2];
            Assert.AreNotEqual(h.Target.Id,copied.Target.Id);Assert.AreEqual(copied.Target.Id,copied.Read().boxId);
            Assert.AreEqual(h.Mode,copied.Mode);Assert.AreEqual(.7f,copied.ConnectorPosition);Assert.AreEqual(.6f,copied.ConnectorSize);Assert.AreEqual(.3f,copied.ConnectorSpeedLoss);
            string folder="Assets/_Project/LocalOnly/Map_Connector_Validation/Run_"+Guid.NewGuid().ToString("N");MaintenanceSceneMigration.EnsureFolder(folder);
            string path=folder+"/Connector_Map.asset";AssetDatabase.CreateAsset(d,path);MapAuthoringOperations.Save(a);
            var expected=a.ReadOrderedRopes();uint hash=d.ToArena().ComputeGeometryChecksum();
            h.ConnectorSpeedLoss=.4f;Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);a.definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);MapAuthoringOperations.Open(a);
            CollectionAssert.AreEqual(expected,a.ReadOrderedRopes());Assert.AreEqual(hash,a.definition.ToArena().ComputeGeometryChecksum());
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(a));
            var go=Child("Connector_Trial_Runner",a.transform).gameObject;go.SetActive(false);var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ConfigureAuthoringPreview(a.definition);runner.StartNewMatch();Assert.IsTrue(runner.IsAuthoringPreview);Assert.AreEqual(1,runner.World.ArenaCount);
            Assert.AreEqual(hash,runner.World.Arena.ComputeGeometryChecksum());Assert.AreEqual(.3f,runner.World.Arena.GetJoint(0).ConnectorSpeedLoss);
            var view=Child("Connector_View",a.transform).gameObject.AddComponent<PropView>();a.shapes.Activate();view.Build(runner.World.Arena,d.Metadata().ResolveTheme(a.palette),a.shapes);
            var state=runner.World.State.Props;state.B00.Rotation=new Vec2(.6f,.8f);state.B00.Position.Y+=1;view.Render(state,state,1);
            var point=RopeGeometry.Connector(runner.World.Arena.GetJoint(0),state.B00.Position,state.B00.Rotation);
            Assert.That(Vector2.Distance(view.ConnectorPosition(0),new Vector2(point.X,point.Y)),Is.LessThan(.00001f));Assert.IsTrue(view.ConnectorVisible(0));
            var preview=MapRopeTools.Handles(a)[0].ConnectorPoint;var original=runner.World.State.Props.B00;
            point=RopeGeometry.Connector(runner.World.Arena.GetJoint(0),original.Position,original.Rotation);
            Assert.That(Vector2.Distance(preview,new Vector2(point.X,point.Y)),Is.LessThan(.00001f));
            state.B00.Broken=1;view.Render(state,state,1);Assert.IsFalse(view.ConnectorVisible(0));
            Assert.IsTrue(view.GetComponentsInChildren<SpriteRenderer>().All(x=>System.Text.RegularExpressions.Regex.IsMatch(x.name,"^[A-Za-z0-9_]+$")));
        }
        [TestCase("position")] [TestCase("size")] [TestCase("loss")] [TestCase("nan")] [TestCase("mode")]
        public void InvalidConnectorSettingsBlockSave(string issue)
        {
            var h=Add();MapRopeTools.SetMode(new[]{h},RopeBreakMode.Connector);
            switch(issue){case "position":h.ConnectorPosition=1.1f;break;case "size":h.ConnectorSize=0;break;case "loss":h.ConnectorSpeedLoss=1.01f;break;
                case "nan":h.ConnectorSpeedLoss=float.NaN;break;case "mode":h.Mode=(RopeBreakMode)255;break;}
            Assert.IsNotEmpty(Own(MapAuthoringOperations.Draft(a)).Validate());Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.Save(a));
        }

        [Test] public void SlackCurveMatchesBothAnchorsAndSavedLength()
        {
            var points=new Vector2[RopePath.Segments+1];RopePath.Fill(new Vector2(1,3),new Vector2(5,1),7,points);
            Assert.AreEqual(new Vector2(1,3),points[0]);Assert.AreEqual(new Vector2(5,1),points.Last());
            float sum=0;for(int i=1;i<points.Length;i++)sum+=Vector2.Distance(points[i-1],points[i]);
            Assert.AreEqual(7,sum,.03f);Assert.Less(points[4].y,2);
        }
    }
}
