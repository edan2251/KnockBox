using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class MapMirrorTests
    {
        Scene scene;
        Object[] selected;
        MapAuthoring authoring;
        MapDefinition definition;

        [SetUp] public void SetUp()
        {
            selected=Selection.objects;
            // The Test Runner can own an untitled, unsaved scene. A preview scene
            // isolates our draft without opening/closing or saving that scene.
            scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Mirror_Test");SceneManager.MoveGameObjectToScene(root,scene);
            authoring=root.AddComponent<MapAuthoring>();
            authoring.terrainRoot=Child("Terrain",authoring.transform);
            authoring.spawn0=Child("P1",authoring.transform);
            authoring.spawn1=Child("P2",authoring.transform);
            definition=ScriptableObject.CreateInstance<MapDefinition>();
            var arena=new ArenaDef("Mirror_Source",20,10,false,
                new[]{new ArenaBox(TerrainKind.Bounce,1.125f,-.25f,3.875f,1.75f)},new Vec2(-8,-4),new Vec2(8,-4));
            definition.Import(arena,new ArenaMeta(),"mirror_test","Mirror_Test");
            authoring.definition=definition;authoring.spawn0.position=definition.Spawn(0);authoring.spawn1.position=definition.Spawn(1);
            authoring.MarkLoaded();Box(0,definition.Box(0));
        }

        [TearDown] public void TearDown()
        {
            Selection.objects=new Object[0];MapPlacementSnap.ResetTracking();
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.ClosePreviewScene(scene);
            if(definition!=null)Object.DestroyImmediate(definition);
            Selection.objects=selected;
        }

        MapBoxHandle Box(int index,MapBox box)
        {
            var go=Child("Terrain_Box",authoring.terrainRoot).gameObject;
            var renderer=go.AddComponent<SpriteRenderer>();renderer.color=new Color(.2f,.4f,.6f,1);renderer.sortingOrder=17;
            var handle=go.AddComponent<MapBoxHandle>();handle.Initialize(box,index);return handle;
        }
        Transform Child(string name,Transform parent)
        {
            var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);
            go.transform.SetParent(parent,false);return go.transform;
        }
        MapBoxHandle[] Boxes()=>authoring.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true).OrderBy(h=>h.Order).ToArray();
        static MapBox Shape(int i,float min,float max,float y=0)=>new MapBox{id="source_"+i,kind=TerrainKind.Solid,minX=min,maxX=max,minY=y,maxY=y+1};

        [Test] public void MirrorUsesExactCurrentBoundsAndPreservesOriginalKindAndRenderer()
        {
            var source=Boxes()[0];source.transform.localPosition+=new Vector3(.1375f,.1234f,0);
            source.transform.localScale=new Vector3(2.0123f,1.2345f,1);
            var before=source.Read();var position=source.transform.localPosition;var scale=source.transform.localScale;
            uint definitionHash=definition.ToArena().ComputeGeometryChecksum();
            var result=MapMirrorTools.Copy(authoring,new[]{source});
            Assert.AreEqual(1,result.Created.Count);Assert.AreEqual(0,result.Skipped);
            var copy=result.Created[0];var mirrored=copy.Read();
            Assert.AreEqual(-before.maxX,mirrored.minX);Assert.AreEqual(-before.minX,mirrored.maxX);
            Assert.AreEqual(before.minY,mirrored.minY);Assert.AreEqual(before.maxY,mirrored.maxY);Assert.AreEqual(before.kind,mirrored.kind);
            Assert.AreNotEqual(before.id,copy.Id);Assert.That(copy.Id,Does.StartWith("box_"));Assert.AreEqual(1,copy.Order);Assert.AreEqual("Terrain_Box_1",copy.name);
            Assert.AreEqual(position,source.transform.localPosition);Assert.AreEqual(scale,source.transform.localScale);Assert.AreEqual(before.id,source.Id);
            Assert.That(copy.transform.localScale.x,Is.GreaterThan(0));Assert.AreEqual(Quaternion.identity,copy.transform.localRotation);
            Assert.AreEqual(source.GetComponent<SpriteRenderer>().color,copy.GetComponent<SpriteRenderer>().color);
            Assert.AreEqual(17,copy.GetComponent<SpriteRenderer>().sortingOrder);
            Assert.AreEqual(definitionHash,definition.ToArena().ComputeGeometryChecksum(),"Copy edits the draft only.");
            Assert.AreEqual(definition.Spawn(0),(Vector2)authoring.spawn0.position);Assert.AreEqual(definition.Spawn(1),(Vector2)authoring.spawn1.position);
        }

        [Test] public void MultiSelectionUsesExplicitOrderAndSkipsCenterOrExistingCounterpart()
        {
            var center=Boxes()[0];center.Initialize(Shape(0,-1,1),0);
            var right=Box(1,Shape(1,2,3));var left=Box(2,Shape(2,-3,-2));
            var later=Box(3,Shape(3,4,5));var last=Box(4,Shape(4,6,7,2));
            var result=MapMirrorTools.Copy(authoring,new[]{last,right,left,center,later,later});
            Assert.AreEqual(3,result.Skipped);Assert.AreEqual(2,result.Created.Count);
            Assert.AreEqual(5,result.Created[0].Order);Assert.AreEqual(-5,result.Created[0].Read().minX);
            Assert.AreEqual(6,result.Created[1].Order);Assert.AreEqual(-7,result.Created[1].Read().minX);
            Assert.AreEqual(7,Boxes().Select(h=>h.Id).Distinct().Count());
            Assert.IsEmpty(authoring.ValidatePlacement());
            var again=MapMirrorTools.Copy(authoring,new[]{later,last});Assert.AreEqual(0,again.Created.Count);Assert.AreEqual(2,again.Skipped);
        }

        [Test] public void OneUndoAndRedoRestoreEveryCopyWithTheirMirroredBoundsAndIds()
        {
            var second=Box(1,Shape(1,5,6,2));
            var result=MapMirrorTools.Copy(authoring,new[]{second,Boxes()[0]});
            var expected=result.Created.Select(h=>h.Read()).ToArray();
            Assert.AreEqual(4,Boxes().Length);Undo.PerformUndo();Assert.AreEqual(2,Boxes().Length);
            Undo.PerformRedo();var restored=Boxes();Assert.AreEqual(4,restored.Length);
            for(int i=0;i<expected.Length;i++)Assert.AreEqual(expected[i],restored[i+2].Read());
            Assert.IsEmpty(authoring.ValidatePlacement());
        }

        [Test] public void InvalidSelectionOrRotationCannotLeavePartialCopies()
        {
            var original=Boxes()[0];
            var foreignObject=Child("Not_Terrain",authoring.transform).gameObject;
            var foreign=foreignObject.AddComponent<MapBoxHandle>();
            Assert.Throws<System.InvalidOperationException>(()=>MapMirrorTools.Copy(authoring,new[]{original,foreign}));
            Assert.AreEqual(1,Boxes().Length);
            original.transform.localRotation=Quaternion.Euler(0,0,5);
            Assert.Throws<System.InvalidOperationException>(()=>MapMirrorTools.Copy(authoring,new[]{original}));
            Assert.AreEqual(1,Boxes().Length);Object.DestroyImmediate(foreign.gameObject);
        }
    }
}
