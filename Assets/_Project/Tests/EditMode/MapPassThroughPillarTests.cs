using System;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    public class MapPassThroughPillarTests
    {
        static SimConfig Config()=>MapPhysicalBoxReactionTests.CurrentConfig();
        static ArenaDef Single(bool pass)=>MapPhysicalBoxAuthoringTests.Floor().WithProps(new[]{
            new PropDef{Id="pass_pillar",Kind=PropKind.LooseBox,Center=new Vec2(0,-6),HalfSize=new Vec2(.75f,2),
                Mass=4,StartAwake=true,AnchorProp=-1,PlayerPassThrough=pass}});
        static SimWorld World(ArenaDef arena)
        {var c=Config();return new SimWorld(c,arena,SimState.CreateRound(c,arena,77,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.Fighting));}
        static InputRecording Record(SimWorld w)=>InputRecording.Begin(w.Config,new[]{w.Arena},w.State.Match.Rotation,77,
            w.State.Fighter0.Position,w.State.Fighter1.Position,w.State.Match.Phase,w.Config.Rules.Format());

        [TestCase(0,false)] [TestCase(1,false)] [TestCase(0,true)] [TestCase(1,true)]
        public void PlayersWalkAndFallThroughPillarExactlyAsEmptySpace(int slot,bool falling)
        {
            var w=World(Single(true));var empty=World(MapPhysicalBoxAuthoringTests.Floor());
            ref var f=ref (slot==0?ref w.State.Fighter0:ref w.State.Fighter1);
            f.Position=falling?new Vec2(0,-2):new Vec2(-3,-8+w.Config.HalfBody);
            f.Grounded=!falling;
            if(slot==0)empty.State.Fighter0=f;else empty.State.Fighter1=f;
            for(int t=0;t<100;t++)
            {
                var input=new InputFrame(falling?0:1,0,InputButtons.None,1);
                w.Step(slot==0?input:default,slot==1?input:default);
                empty.Step(slot==0?input:default,slot==1?input:default);
                Assert.AreEqual(slot==0?empty.State.Fighter0:empty.State.Fighter1,slot==0?w.State.Fighter0:w.State.Fighter1,"tick "+t);
            }
            var end=slot==0?w.State.Fighter0:w.State.Fighter1;
            if(falling)Assert.Less(end.Position.Y,-7);else Assert.Greater(end.Position.X,1);
            Assert.Zero(end.OnProp);
        }

        [TestCase(0,false)] [TestCase(1,false)] [TestCase(0,true)] [TestCase(1,true)]
        public void LungeOrFallingPillarCannotPushDamageOrCarryPlayer(int slot,bool movingPillar)
        {
            var w=World(Single(true));ref var f=ref (slot==0?ref w.State.Fighter0:ref w.State.Fighter1);
            f.Position=new Vec2(-.8f,-5.5f);f.Velocity=new Vec2(movingPillar?0:38,0);
            f.SetAction(movingPillar?ActionState.Idle:ActionState.Lunge);
            f.OnProp=1; // A stale support must be cleared before carry/probe.
            var expected=f;expected.OnProp=0;
            ref var b=ref PropState.Get(ref w.State.Props,0);b.Velocity=new Vec2(movingPillar?-30:0,0);b.AngularVelocity=2;
            var before=w.State.Props;var p0=w.State.Fighter0;var p1=w.State.Fighter1;
            PropInteraction.Resolve(ref w.State,w.Arena,w.Config,p0,p1,false,false,w.Events,w.PropScratch);
            Assert.AreEqual(expected,slot==0?w.State.Fighter0:w.State.Fighter1);
            Assert.AreEqual(before,w.State.Props);Assert.Zero(w.Events.Count);
        }

        [Test]
        public void OrdinaryBoxStillCollidesWithPlayer()
        {
            var w=World(Single(false));w.State.Fighter0.Position=new Vec2(-.8f,-5.5f);w.State.Fighter0.Velocity=new Vec2(18,0);
            w.State.Fighter0.SetAction(ActionState.Lunge);var before=w.State.Fighter0;var p1=w.State.Fighter1;
            PropInteraction.Resolve(ref w.State,w.Arena,w.Config,before,p1,false,false,w.Events,w.PropScratch);
            Assert.AreNotEqual(before,w.State.Fighter0);Assert.Greater(w.Events.Count,0);
            Assert.Greater(PropState.At(w.State.Props,0).Velocity.X,0);
        }

        [TestCase(0f)] [TestCase(18f)] [TestCase(38f)]
        public void WidePassThroughStack_RestWeakStrongAndActualNeighborContact(float speed)
        {
            var result=MapTallPillarTests.Observe(true,speed,-2.25f,true);
            TestContext.WriteLine("pass-through speed="+speed+"; "+result.Description);
            Assert.Less(result.settleDrift,.002f);Assert.Less(result.settleTilt,.002f);Assert.Less(result.settleSag,.06f);
            Assert.Less(result.maxPenetration,.06f);Assert.Less(result.neighborMotionBeforeContact,.002f);
            if(speed==0){Assert.Less(result.dx.Max(),.002f);Assert.Less(result.tilt.Max(),.002f);}
            if(speed==18){Assert.Greater(result.early[0],.02f);Assert.Greater(result.early[1],.01f);Assert.Zero(result.loose);}
            if(speed==38)
            {
                Assert.Greater(result.topDrop,4);Assert.Greater(result.tilt[3],.9f);
                Assert.GreaterOrEqual(result.firstLongNeighborTick,0);Assert.Greater(result.maxLongNeighborImpulse,.1f);
                Assert.Greater(result.tilt[4],.9f);
            }
        }

        [Test]
        public void PassThroughFlagDoesNotChangeBoxToBoxOrTerrainPhysics()
        {
            var left=Single(true).GetProp(0);left.Center.X=-3;left.Id="left";
            var right=Single(true).GetProp(0);right.Id="right";
            var pass=MapPhysicalBoxAuthoringTests.Floor().WithProps(new[]{left,right});
            var solids=Enumerable.Range(0,pass.PropCount).Select(i=>{var p=pass.GetProp(i);p.PlayerPassThrough=false;return p;}).ToArray();
            var a=World(pass);var b=World(pass.WithProps(solids));
            bool neighborContact=false;float maxNeighborMotion=0;
            for(int t=0;t<420;t++)
            {
                if(t==120)
                {
                    PropState.Get(ref a.State.Props,0).Velocity=new Vec2(12,0);
                    PropState.Get(ref a.State.Props,0).SleepTicks=0;
                    b.State.Props=a.State.Props;
                }
                a.Step(default,default);b.Step(default,default);
                Assert.AreEqual(a.State.Props,b.State.Props,"body physics tick "+t);
                for(int i=0;i<a.PropScratch.ContactCount;i++)
                {
                    var c=a.PropScratch.Contacts[i];
                    if((c.Body==1&&c.Other==0||c.Body==0&&c.Other==1)&&c.NormalImpulse>1e-5f)neighborContact=true;
                }
                maxNeighborMotion=Math.Max(maxNeighborMotion,Math.Abs(PropState.At(a.State.Props,1).Position.X));
                for(int i=0;i<a.State.Props.Count;i++)Assert.Greater(PropState.At(a.State.Props,i).Position.Y,-8.1f,"floor must support bodies");
            }
            Assert.IsTrue(neighborContact);Assert.Greater(maxNeighborMotion,.1f);
        }

        [TestCase(false)] [TestCase(true)]
        public void NewPillarMap_RealInputReplayAndRollbackMatchEveryTick(bool adjacent)
            =>TestContext.WriteLine(MapTallPillarTests.InputTrial(adjacent,true,true,-2.25f));

        [Test]
        public void NewFlagChangesOnlyExtendedMapHash_RejectsWrongReplayAndOnlineMap()
        {
            var pass=Single(true);var solid=Single(false);var rec=Record(World(pass));
            Assert.AreEqual(pass.ComputeChecksum(),solid.ComputeChecksum(),"Historical schema 1 is unchanged");
            Assert.AreNotEqual(pass.ComputeGeometryChecksum(),solid.ComputeGeometryChecksum());
            Assert.AreEqual(9,rec.WrittenVersion);var loaded=InputRecording.FromBytes(rec.ToBytes());
            Assert.DoesNotThrow(()=>loaded.CreateInitialState(Config(),pass));
            Assert.Throws<InvalidOperationException>(()=>loaded.CreateInitialState(Config(),solid));
            MemoryTransport.Pair(out var ma,out var mb);
            try
            {
                var host=new LockstepSession(ma,true,Config().ComputeChecksum(),OnlineMapManifest.Compute(new[]{pass}),77,3);
                var guest=new LockstepSession(mb,false,Config().ComputeChecksum(),OnlineMapManifest.Compute(new[]{solid}),0,0);
                for(int i=0;i<120;i++){host.Poll(i/60d);guest.Poll(i/60d);}
                Assert.AreNotEqual(Knockbox.Game.Online.SessionState.Running,guest.State);
            }finally{ma.Dispose();mb.Dispose();}
            var flagOnly=new PropDef{Kind=PropKind.LooseBox,HalfSize=new Vec2(.75f,2),PlayerPassThrough=true};
            Assert.IsTrue(flagOnly.HasAuthoringData,"Even without authored IDs the flag must select version 9");
            var legacy=solid.GetProp(0);legacy.Id=null;legacy.Mass=0;legacy.StartAwake=false;
            var oldMap=solid.WithProps(new[]{legacy});var oldRec=Record(World(oldMap));Assert.AreEqual(8,oldRec.WrittenVersion);
            legacy.PlayerPassThrough=true;var changedOldMap=oldMap.WithProps(new[]{legacy});
            Assert.AreEqual(oldMap.ComputeChecksum(),changedOldMap.ComputeChecksum());
            Assert.IsFalse(oldRec.CanVerifyPlayerCollision(new[]{changedOldMap}));
            Assert.Throws<InvalidOperationException>(()=>oldRec.CreateInitialState(Config(),changedOldMap));
            Assert.DoesNotThrow(()=>oldRec.CreateInitialState(Config(),oldMap));
        }

        [TestCase("CP2_1_Single_Stack","9A173423")] [TestCase("CP2_1_Adjacent_Stack","4616BEA6")]
        public void PreviouslySavedSolidPillarsRetainTheirExactGeometryHash(string name,string checksum)
        {
            var d=AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/Validation/"+name+".asset");
            Assert.NotNull(d);Assert.AreEqual(checksum,d.ToArena().ComputeGeometryChecksum().ToString("X8"));
            for(int i=0;i<d.PhysicalBoxCount;i++)Assert.IsFalse(d.PhysicalBox(i).playerPassThrough);
        }
    }
}
