using System;
using System.Collections.Generic;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapConvexTerrainTests
    {
        public static MapBox Chamfer()=>MapTerrainCut.Preview(new MapBox{id="terrain_cut",kind=TerrainKind.Solid,minX=-4,minY=-4,maxX=4,maxY=4},new Vector2(0,4),new Vector2(4,0),false);
        public static ArenaDef Arena(MapBox b)=>new ArenaDef("CP4_Convex",36,20,false,new[]{MapPhysicalBoxAuthoringTests.Floor().GetBox(0),b.ToBox()},new Vec2(-12,-8),new Vec2(12,-8));
        public static MapBox Ramp()=>MapTerrainCut.Preview(new MapBox{id="ramp",kind=TerrainKind.Solid,minX=-6,minY=-8,maxX=2,maxY=0},new Vector2(-6,-8),new Vector2(2,0),false);

        [Test] public void MidpointChamferSwapCancelAndRepeatedCutPreserveSource()
        {
            var box=new MapBox{id="fixed_id",kind=TerrainKind.Solid,minX=-4,minY=-4,maxX=4,maxY=4};
            var cut=MapTerrainCut.Preview(box,new Vector2(0,4),new Vector2(4,0),false);
            Assert.IsFalse(box.IsPolygon);Assert.AreEqual(box.id,cut.id);Assert.AreEqual(5,cut.vertices.Length);
            Assert.IsFalse(cut.ToBox().Polygon.Contains(new Vec2(3,3)));Assert.IsTrue(cut.ToBox().Polygon.Contains(new Vec2(-3,3)));
            var other=MapTerrainCut.Preview(box,new Vector2(0,4),new Vector2(4,0),true);Assert.AreEqual(3,other.vertices.Length);
            Assert.IsTrue(other.ToBox().Polygon.Contains(new Vec2(3,3)));
            var twice=MapTerrainCut.Preview(cut,new Vector2(-4,0),new Vector2(0,4),false);
            Assert.AreEqual(box.id,twice.id);Assert.AreEqual(5,twice.vertices.Length);
            Assert.IsFalse(twice.ToBox().Polygon.Contains(new Vec2(-3,3)));Assert.IsTrue(cut.ToBox().Polygon.Contains(new Vec2(-3,3)));
            Assert.Throws<InvalidOperationException>(()=>MapTerrainCut.Preview(box,Vector2.zero,new Vector2(4,0),false));
            Assert.Throws<InvalidOperationException>(()=>MapTerrainCut.Preview(box,new Vector2(4,0),new Vector2(4,0),false));
        }
        [Test] public void CharacterFallsOntoActualSlope_AndPassesMissingCorner()
        {
            var arena=Arena(Chamfer());float half=.36f;var p=new Vec2(3,7);var v=new Vec2(0,-60);bool landed=false;
            for(int i=0;i<12;i++){var c=default(MoveContacts);TerrainCollision.Move(ref p,ref v,half,arena,.2f,-1,ref c);landed|=c.Landed;}
            Assert.IsTrue(landed);Assert.AreEqual(1.72f,p.Y,.002f);Assert.AreEqual(1,TerrainCollision.ProbeGround(p,half,arena,.06f,-1));
            Assert.IsFalse(TerrainCollision.OverlapsSolid(new Vec2(3,3),half,arena),"removed corner");
            Assert.IsTrue(TerrainCollision.OverlapsSolid(new Vec2(2,1),half,arena),"retained slope interior");
            p=new Vec2(6,-2);v=new Vec2(-60,0);var wall=default(MoveContacts);
            TerrainCollision.Move(ref p,ref v,half,arena,.2f,-1,ref wall);
            TerrainCollision.Move(ref p,ref v,half,arena,.2f,-1,ref wall);
            Assert.AreEqual(4.36f,p.X,.001f);Assert.AreEqual(-1,wall.HitWall);
            p=new Vec2(3,-7);v=new Vec2(0,120);var ceiling=default(MoveContacts);TerrainCollision.Move(ref p,ref v,half,arena,.2f,-1,ref ceiling);
            TerrainCollision.Move(ref p,ref v,half,arena,.2f,-1,ref ceiling);
            Assert.IsTrue(ceiling.HitCeiling);Assert.AreEqual(-4.36f,p.Y,.001f);
        }
        [Test] public void WalkingUpAndDownSlopeMaintainsActualSupport()
        {
            var a=Arena(Ramp());var p=new Vec2(-7,-7.64f);float highest=p.Y;
            for(int t=0;t<125;t++)
            {
                var v=new Vec2(4,-3);var c=default(MoveContacts);TerrainCollision.Move(ref p,ref v,.36f,a,.2f,-1,ref c);
                Assert.IsFalse(TerrainCollision.OverlapsSolid(p,.355f,a),"overlap tick "+t);highest=Math.Max(highest,p.Y);
            }
            Assert.Greater(p.X,.9f);Assert.Greater(highest,-.5f);
            for(int t=0;t<125;t++){var v=new Vec2(-4,-3);var c=default(MoveContacts);TerrainCollision.Move(ref p,ref v,.36f,a,.2f,-1,ref c);}
            Assert.Less(p.X,-6.5f);Assert.AreEqual(-7.64f,p.Y,.01f);
        }
        [Test] public void BoxMeetsSlopeNormalAndRollsOff_WithoutArtificialAttachment()
        {
            var a=Arena(Chamfer());var props=new[]{new MapPhysicalBox{id="falling_box",center=new Vector2(2,5.5f),size=new Vector2(1.5f,1.5f),mass=2.25f}.ToProp(0)};
            a=a.WithProps(props);var c=MapPhysicalBoxReactionTests.CurrentConfig();var w=new SimWorld(c,a,SimState.CreateRound(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting));
            bool slope=false;float spin=0;
            for(int t=0;t<240;t++)
            {
                w.Step(default,default);spin=Math.Max(spin,Math.Abs(w.State.Props.B00.AngularVelocity));
                for(int i=0;i<w.PropScratch.ContactCount;i++){var n=w.PropScratch.Contacts[i].Normal;if(n.X>.6f&&n.Y>.6f)slope=true;}
                Assert.Greater(w.State.Props.B00.Position.Y,-9);
            }
            TestContext.WriteLine($"slope={slope}; final={w.State.Props.B00.Position}; max spin={spin}");
            Assert.IsTrue(slope);Assert.Greater(spin,.1f);Assert.Greater(w.State.Props.B00.Position.X,4.5f);
        }
        [Test] public void VerticesChangeManifest_EvenWhenBoundsAreIdentical_AndRejectLegacyRecording()
        {
            var a=Arena(Chamfer());var box=Chamfer();box.vertices[2]=new Vector2(4,-1);var b=Arena(box);
            Assert.AreEqual(a.ComputeChecksum(),b.ComputeChecksum(),"historical schema unchanged");
            Assert.AreNotEqual(a.ComputeGeometryChecksum(),b.ComputeGeometryChecksum());Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{a}),OnlineMapManifest.Compute(new[]{b}));
            var c=MapPhysicalBoxReactionTests.CurrentConfig();var rec=InputRecording.Begin(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting,"brawl");
            Assert.AreEqual(9,rec.WrittenVersion);Assert.Throws<InvalidOperationException>(()=>rec.CreateInitialState(c,b));
            var old=new InputRecording();Assert.IsFalse(old.CanVerifyPlayerCollision(new[]{a}));
        }
        [Test] public void ConvexReplayAndRollbackMatchEveryTick()
        {
            var a=Arena(Ramp());var c=MapPhysicalBoxReactionTests.CurrentConfig();var s=SimState.CreateRound(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting);
            var w=new SimWorld(c,a,s);var sync=new SyncTest<SimWorld>(w);var rec=InputRecording.Begin(c,a,77,s.Fighter0.Position,s.Fighter1.Position,MatchPhase.Fighting,"brawl");var hashes=new List<uint>();
            for(int t=0;t<600;t++){var input=new InputFrame((sbyte)(t<300?1:-1),0,t%100<5?InputButtons.Jump:InputButtons.None,1);rec.Append(input,default);Assert.IsTrue(sync.Advance(input,default));hashes.Add(w.ComputeChecksum());}
            rec.Finish(w.ComputeChecksum());var restored=InputRecording.FromBytes(rec.ToBytes());var replay=new SimWorld(c,a,restored.CreateInitialState(c,a));
            for(int t=0;t<restored.Count;t++){replay.Step(restored.GetP1(t),restored.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"replay "+t);}
        }
        [Test] public void RemovedCornerDoesNotCreateVisualLimbSupportOrSpiderConnection()
        {
            var a=Arena(Chamfer());a.PrepareSurfaceData();
            Assert.IsTrue(Knockbox.Presentation.GroundQuery.SurfaceBelow(a,3,3,out float top));Assert.AreEqual(1,top,.001f);
            Assert.IsTrue(Knockbox.Presentation.GroundQuery.WallSurface(a,3,3,-1,out float side));Assert.AreEqual(1,side,.001f);
            Assert.AreEqual(-1,SpiderMovement.FieldGravity(new Vec2(3,3),.36f,a,.3f,0,out _));
            var b=new ArenaDef("Corner_Contact",36,20,false,new[]{a.GetBox(0),a.GetBox(1),new ArenaBox(TerrainKind.Solid,3,3,3.5f,3.5f)},a.SpawnFoot0,a.SpawnFoot1);
            b.PrepareSurfaceData();Assert.AreNotEqual(b.PieceOf(1),b.PieceOf(2));
        }
        [Test] public void RotatedSurfaceMapsKeepEveryConvexVertex()
        {
            var a=Arena(Chamfer());a.PrepareSurfaceData();
            for(int rotation=1;rotation<4;rotation++)for(int i=0;i<5;i++)
                Assert.AreEqual(SpiderMovement.ToLocal(a.GetBox(1).Polygon.Point(i),rotation),a.Rotated(rotation).GetBox(1).Polygon.Point(i));
        }
    }
}
