using System;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game.Maps;
using Knockbox.Game.UI;
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
    public class MapOrderRepairTests
    {
        Scene scene;
        MapAuthoring authoring;
        MapDefinition definition;
        Object[] selection;
        MapBoxHandle[] Terrain()=>authoring.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true).OrderBy(h=>h.Order).ThenBy(h=>h.transform.GetSiblingIndex()).ToArray();
        MapPhysicalBoxHandle[] Physics()=>authoring.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true).OrderBy(h=>h.Order).ThenBy(h=>h.transform.GetSiblingIndex()).ToArray();
        Transform Child(string name,Transform parent)
        {
            var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);
            go.transform.SetParent(parent,false);return go.transform;
        }
        [SetUp] public void SetUp()
        {
            selection=Selection.objects;Selection.objects=Array.Empty<Object>();
            scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Order_Repair_Test");SceneManager.MoveGameObjectToScene(root,scene);
            authoring=root.AddComponent<MapAuthoring>();
            authoring.terrainRoot=Child("Terrain",root.transform);authoring.spawn0=Child("P1",root.transform);authoring.spawn1=Child("P2",root.transform);
            authoring.shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            authoring.palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            var boxes=Enumerable.Range(0,3).Select(i=>MapPhysicalBoxAuthoringTests.Box(i,-6+6*i,-7.4f)).ToArray();
            definition=MapPhysicalBoxAuthoringTests.Definition(boxes);
            definition.SaveLayout(new[]{definition.Box(0),
                new MapBox{id="terrain_left",kind=TerrainKind.Solid,minX=-7,maxX=-5,minY=3,maxY=4},
                new MapBox{id="terrain_right",kind=TerrainKind.Solid,minX=5,maxX=7,minY=3,maxY=4}},
                boxes,definition.Spawn(0),definition.Spawn(1));
            authoring.definition=definition;MapAuthoringOperations.Open(authoring);
        }
        [TearDown] public void TearDown()
        {
            Selection.objects=Array.Empty<Object>();MapPlacementSnap.ResetTracking();
            EditorSceneManager.ClosePreviewScene(scene);
            if(definition!=null&&!AssetDatabase.Contains(definition))Object.DestroyImmediate(definition);
            Selection.objects=selection;
        }

        [Test]
        public void MiddleDeletion_RepairsBothGroupsWithoutRecreatingOrMoving_AndOneUndoRestoresNumbers()
        {
            uint sourceHash=definition.ToArena().ComputeGeometryChecksum();
            Object.DestroyImmediate(Terrain()[1].gameObject);Object.DestroyImmediate(Physics()[1].gameObject);
            var terrain=Terrain();var physics=Physics();
            terrain[1].transform.localPosition+=new Vector3(.12345f,.23456f,0);
            physics[1].Mass=3.75f;physics[1].transform.localScale=new Vector3(1.1234f,1.2345f,1);
            physics[1].transform.localPosition+=new Vector3(.1321f,.1f,0); // Keep the resized draft above the floor.
            terrain[0].name="0";physics[0].name="물리상자_0";
            // Explicit order wins over incidental hierarchy order.
            terrain[0].transform.SetAsLastSibling();physics[0].transform.SetAsLastSibling();
            var expectedTerrain=terrain.Select(h=>h.Read()).ToArray();var expectedPhysics=physics.Select(h=>h.Read()).ToArray();
            int[] instances=terrain.Select(h=>h.GetInstanceID()).Concat(physics.Select(h=>h.GetInstanceID())).ToArray();
            Vector3 p0=authoring.spawn0.position,p1=authoring.spawn1.position;
            Assert.AreEqual(2,authoring.ValidatePlacement().Count,"One actionable error per affected group.");
            Assert.Throws<InvalidOperationException>(()=>authoring.ReadOrderedBoxes());
            Assert.Greater(MapAuthoringOperations.RepairOrder(authoring),0);
            Assert.IsEmpty(authoring.ValidatePlacement());
            CollectionAssert.AreEqual(expectedTerrain,authoring.ReadOrderedBoxes());
            CollectionAssert.AreEqual(expectedPhysics,authoring.ReadOrderedPhysicalBoxes());
            CollectionAssert.AreEqual(instances,Terrain().Select(h=>h.GetInstanceID()).Concat(Physics().Select(h=>h.GetInstanceID())));
            Assert.AreEqual("Terrain_Box_0",terrain[0].name);Assert.AreEqual("Physics_Box_0",physics[0].name);
            Assert.AreEqual(p0,authoring.spawn0.position);Assert.AreEqual(p1,authoring.spawn1.position);
            Assert.AreEqual(sourceHash,definition.ToArena().ComputeGeometryChecksum(),"Definition is unchanged until explicit map save.");
            Undo.PerformUndo();
            CollectionAssert.AreEqual(new[]{0,2},Terrain().Select(h=>h.Order));CollectionAssert.AreEqual(new[]{0,2},Physics().Select(h=>h.Order));
            Assert.AreEqual("물리상자_0",physics[0].name);Assert.AreEqual(2,Terrain().Length);Assert.AreEqual(2,Physics().Length);
            Undo.PerformRedo();
            CollectionAssert.AreEqual(expectedTerrain,authoring.ReadOrderedBoxes());CollectionAssert.AreEqual(expectedPhysics,authoring.ReadOrderedPhysicalBoxes());

            string folder="Assets/_Project/LocalOnly/Map_Order_Validation/Run_"+Guid.NewGuid().ToString("N");
            MaintenanceSceneMigration.EnsureFolder(folder);string path=folder+"/Repaired_Map.asset";
            AssetDatabase.CreateAsset(definition,path);MapAuthoringOperations.Save(authoring);
            uint savedHash=definition.ToArena().ComputeGeometryChecksum();
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);MapAuthoringOperations.Open(authoring);
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(authoring));
            CollectionAssert.AreEqual(expectedTerrain,authoring.ReadOrderedBoxes());CollectionAssert.AreEqual(expectedPhysics,authoring.ReadOrderedPhysicalBoxes());
            Assert.AreEqual(savedHash,definition.ToArena().ComputeGeometryChecksum());
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path),MapAuthoringOperations.SavedTrialGuid(authoring));
            Assert.DoesNotThrow(()=>MapPhysicalBoxTools.Add(authoring,MapPhysicalBoxPreset.TallPillar),"Editing resumes after repair.");
        }

        [Test]
        public void ValidOrder_IsNoOp_AndNameOnlyRepairRetainsBothChecksumsAndExactCoordinates()
        {
            string folder="Assets/_Project/LocalOnly/Map_Order_Validation/Run_"+Guid.NewGuid().ToString("N");
            MaintenanceSceneMigration.EnsureFolder(folder);AssetDatabase.CreateAsset(definition,folder+"/Clean_Map.asset");
            AssetDatabase.SaveAssetIfDirty(definition);
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(authoring),"Start from a saved, clean definition.");
            uint legacy=definition.ToArena().ComputeChecksum(),geometry=definition.ToArena().ComputeGeometryChecksum();
            Terrain()[0].transform.SetAsLastSibling();Physics()[0].transform.SetAsLastSibling();
            Assert.Zero(MapAuthoringOperations.RepairOrder(authoring));
            foreach(var h in Terrain())h.name="기존 지형";
            foreach(var h in Physics())h.name="기존 상자";
            Assert.AreEqual(6,MapAuthoringOperations.RepairOrder(authoring));
            var draft=MapAuthoringOperations.Draft(authoring);
            try{Assert.AreEqual(legacy,draft.ToArena().ComputeChecksum());Assert.AreEqual(geometry,draft.ToArena().ComputeGeometryChecksum());}
            finally{Object.DestroyImmediate(draft);}
            Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(authoring));
        }

        [Test]
        public void DuplicateOrder_UsesSiblingOrderOnlyForTies_AndRetainsIds()
        {
            var terrain=Terrain();var physics=Physics();
            terrain[1].SetOrder(7);terrain[2].SetOrder(7);terrain[2].transform.SetSiblingIndex(1);
            physics[1].SetOrder(9);physics[2].SetOrder(9);physics[2].transform.SetSiblingIndex(1);
            MapAuthoringOperations.RepairOrder(authoring);
            CollectionAssert.AreEqual(new[]{terrain[0].Id,terrain[2].Id,terrain[1].Id},Terrain().Select(h=>h.Id));
            CollectionAssert.AreEqual(new[]{physics[0].Id,physics[2].Id,physics[1].Id},Physics().Select(h=>h.Id));
            Assert.IsEmpty(authoring.ValidatePlacement());
        }

        [Test]
        public void NestedBody_RejectsBeforeAnyPartialRenumbering()
        {
            var terrain=Terrain();var physics=Physics();
            terrain[1].SetOrder(8);physics[1].transform.SetParent(physics[0].transform,true);
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.RepairOrder(authoring));
            Assert.AreEqual(8,terrain[1].Order);Assert.AreEqual("Terrain_Box_8",terrain[1].name);
        }

        [Test]
        public void NumberRepair_DoesNotConcealRotationOrDuplicateIdentityErrors()
        {
            var body=Physics()[1];body.SetOrder(8);body.transform.localRotation=Quaternion.Euler(0,0,15);
            MapAuthoringOperations.RepairOrder(authoring);
            Assert.That(authoring.ValidatePlacement().Any(e=>e.Contains("회전")));
            body.transform.localRotation=Quaternion.identity;
            var duplicate=Object.Instantiate(Physics()[0].gameObject,authoring.physicsRoot);
            MapAuthoringOperations.RepairOrder(authoring);
            var draft=MapAuthoringOperations.Draft(authoring);
            try{Assert.That(draft.Validate().Any(e=>e.Contains("ID")));}finally{Object.DestroyImmediate(draft);}
            Assert.AreEqual(Physics()[0].Id,duplicate.GetComponent<MapPhysicalBoxHandle>().Id);
        }

        [Test]
        public void FixedTerrainInspector_AfterDeletion_DoesNotAccessDestroyedSelection()
        {
            var h=Terrain()[1];var editor=UnityEditor.Editor.CreateEditor(h) as MapBoxInspector;
            try
            {
                Object.DestroyImmediate(h.gameObject);
                Assert.DoesNotThrow(()=>editor.OnInspectorGUI());
                var callback=typeof(MapBoxInspector).GetMethod("OnSceneGUI",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Assert.DoesNotThrow(()=>callback.Invoke(editor,null));
            }
            finally{Object.DestroyImmediate(editor);}
        }

        [Test]
        public void GeneratedNamesStayEnglish_WhileUiLabelsRemainKorean()
        {
            var text=FrontendWidgets.Text(authoring.transform,"Test heading",new Rect(0,0,100,50),"도형 순서 정리",null,18,Color.white);
            Assert.AreEqual("Test_heading",text.name);Assert.AreEqual("도형 순서 정리",text.text);
            foreach(var h in Terrain())Assert.That(h.name,Does.Match(@"^[A-Za-z0-9_]+$"));
            foreach(var h in Physics())Assert.That(h.name,Does.Match(@"^[A-Za-z0-9_]+$"));
            var runtime=ShapeLibrary.Create(authoring.transform,"Terrain block 1",authoring.shapes.Square,SortingLayers.Terrain,0,Color.white);
            Assert.AreEqual("Terrain_block_1",runtime.name);
        }
    }
}
