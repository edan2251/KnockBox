using Knockbox.EditorTools;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapPlacementSnapTests
    {
        GameObject root;
        MapAuthoring authoring;
        [SetUp] public void SetUp()
        {
            root=new GameObject("Snap_Test");authoring=root.AddComponent<MapAuthoring>();
            authoring.terrainRoot=new GameObject("Terrain").transform;authoring.terrainRoot.SetParent(root.transform,false);
            authoring.spawn0=new GameObject("P1").transform;authoring.spawn0.SetParent(root.transform,false);
            authoring.spawn1=new GameObject("P2").transform;authoring.spawn1.SetParent(root.transform,false);
        }
        [TearDown] public void TearDown(){Object.DestroyImmediate(root);MapPlacementSnap.ResetTracking();}
        MapBoxHandle Box(int index,float minX,float minY,float maxX,float maxY)
        {
            var go=new GameObject();go.transform.SetParent(authoring.terrainRoot,false);
            var box=go.AddComponent<MapBoxHandle>();box.Initialize(new MapBox{id="private_abc_"+index,kind=TerrainKind.Solid,minX=minX,minY=minY,maxX=maxX,maxY=maxY},index);return box;
        }
        [Test] public void GridToggleSnapsMovementAndSize_OffPreservesExactValues()
        {
            var b=Box(11,-1,-1,1,1);Assert.AreEqual("Terrain_Box_11",b.name);Assert.AreEqual("private_abc_11",b.Id);
            b.transform.localPosition=new Vector3(.38f,-.38f,0);b.transform.localScale=new Vector3(2.12f,1.89f,1);
            MapPlacementSnap.Apply(authoring,b.transform);
            Assert.AreEqual(new Vector3(.5f,-.5f,0),b.transform.localPosition);Assert.AreEqual(new Vector3(2,2,1),b.transform.localScale);
            authoring.gridSnap=false;b.transform.localPosition=new Vector3(.123456f,-.654321f,0);
            MapPlacementSnap.Apply(authoring,b.transform);Assert.AreEqual(.123456f,b.transform.localPosition.x);Assert.AreEqual(-.654321f,b.transform.localPosition.y);
        }
        [Test] public void FootSnapsToSafeTop_AndHonorsRangeAndToggle()
        {
            Box(0,-3,-2,3,-1);authoring.gridSnap=false;
            authoring.spawn0.localPosition=new Vector3(1,-.83f,0);MapPlacementSnap.Apply(authoring,authoring.spawn0);
            Assert.AreEqual(new Vector3(1,-1,0),authoring.spawn0.position);
            authoring.spawnEdgeSnap=false;authoring.spawn0.position=new Vector3(1,-.8f,0);MapPlacementSnap.Apply(authoring,authoring.spawn0);Assert.AreEqual(-.8f,authoring.spawn0.position.y);
            Assert.IsFalse(MapPlacementSnap.TrySnapFoot(authoring,new Vector3(0,3,0),out _));
        }
        [Test] public void FootCannotSnapIntoOverlappingWall()
        {
            Box(0,-3,-2,3,-1);Box(1,-.5f,-1,.5f,2);
            Assert.IsFalse(MapPlacementSnap.TrySnapFoot(authoring,new Vector3(0,-.9f,0),out _));
            Assert.IsTrue(MapPlacementSnap.TrySnapFoot(authoring,new Vector3(2,-.9f,0),out var p));Assert.AreEqual(-1,p.y);
        }
    }
}
