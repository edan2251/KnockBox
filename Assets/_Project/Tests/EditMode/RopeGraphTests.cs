using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class RopeGraphTests
    {
        public static GraphRope Edge(string id,int target,int source,Vec2 a,Vec2 b,float length)=>new GraphRope
        {Id=id,Body=target,Joint=new PropJoint{Kind=PropJointKind.Rope,OtherBody=source,WorldAnchor=source<0?a:Vec2.Zero,
            OtherLocal=source<0?Vec2.Zero:a,LocalAnchor=b,Length=length,UsesTension=true,RopeMode=RopeBreakMode.Unbreakable}};
        public static ArenaDef Arena(bool secondarySupport=false)
        {
            var p=new[]{
                new PropDef{Kind=PropKind.LooseBox,Id="node",AnchorProp=-1,Center=new Vec2(0,3),HalfSize=new Vec2(.325f,.325f),Mass=.65f,StartAwake=true,IsConnector=true,ConnectorSpeedLoss=.15f},
                new PropDef{Kind=PropKind.LooseBox,Id="beam",AnchorProp=-1,Center=new Vec2(0,0),HalfSize=new Vec2(3,.3f),Mass=3.6f,StartAwake=true},
                new PropDef{Kind=PropKind.LooseBox,Id="box",AnchorProp=-1,Center=new Vec2(0,-3),HalfSize=new Vec2(.6f,.6f),Mass=1.44f,StartAwake=true}};
            var edges=new List<GraphRope>{Edge("ceiling",0,-1,new Vec2(0,7),new Vec2(0,.325f),3.675f),
                Edge("left",1,0,new Vec2(0,-.325f),new Vec2(-2,.3f),SimMath.Sqrt(4+2.375f*2.375f)),
                Edge("right",1,0,new Vec2(0,-.325f),new Vec2(2,.3f),SimMath.Sqrt(4+2.375f*2.375f)),
                Edge("lower",2,1,new Vec2(0,-.3f),new Vec2(0,.6f),2.1f)};
            if(secondarySupport)edges.Add(Edge("surviving_support",1,-1,new Vec2(2,7),new Vec2(2,.3f),6.7f));
            return new ArenaDef("Graph_Trial",36,20,false,new[]{new ArenaBox(TerrainKind.Solid,-18,-10,18,-8)},new Vec2(-12,-8),new Vec2(12,-8))
                .WithProps(p).WithGraphRopes(edges.ToArray());
        }
        public static SimWorld World(ArenaDef a=null)
        {a=a??Arena();var c=MapPhysicalBoxReactionTests.CurrentConfig();return new SimWorld(c,a,SimState.CreateRound(c,a,77,a.SpawnFoot0+new Vec2(0,c.HalfBody),a.SpawnFoot1+new Vec2(0,c.HalfBody),MatchPhase.Fighting));}
        static float Stretch(SimWorld w,int edge)
        {
            var r=w.Arena.GetGraphRope(edge);var a=r.Joint.OtherBody<0?r.Joint.WorldAnchor:PropState.At(w.State.Props,r.Joint.OtherBody).ToWorld(r.Joint.OtherLocal);
            return SimMath.Sqrt((PropState.At(w.State.Props,r.Body).ToWorld(r.Joint.LocalAnchor)-a).LengthSquared)-r.Joint.Length;
        }
        static void Prepare(SimWorld w)
        {for(int i=0;i<w.State.Props.Count;i++){var b=PropState.At(w.State.Props,i);w.PropScratch.StartPosition[i]=b.Position;w.PropScratch.StartRotation[i]=b.Rotation;}}
        static void Resolve(SimWorld w,FighterState previous,bool frozen=false)
        {Prepare(w);ConnectorBodies.Resolve(ref w.State,w.Arena,w.Config,previous,w.State.Fighter1,frozen,false,w.PropScratch,w.Events);}

        [TestCase(false)] [TestCase(true)]
        public void BranchingAndMultiplySupportedStructureStaysStableWithoutSpontaneousBreak(bool secondary)
        {
            var w=World(Arena(secondary));float stretch=0;
            for(int t=0;t<3600;t++)
            {
                w.Step(default,default);
                Assert.IsFalse(w.State.Props.IsDestroyed(0));
                for(int e=0;e<w.Arena.GraphRopeCount;e++)stretch=Math.Max(stretch,Stretch(w,e));
                for(int b=0;b<3;b++)Assert.Less(PropState.At(w.State.Props,b).Position.LengthSquared,200);
            }
            Assert.Less(stretch,.08f);Assert.That(w.State.Props.B00.Position.Y,Is.GreaterThan(2.9f));
            Assert.That(w.State.Props.B01.Position.Y,Is.GreaterThan(-.1f));
            TestContext.WriteLine("secondary="+secondary+" peak stretch="+stretch+" sleep="+w.State.Props.B01.SleepTicks);
        }
        [Test] public void DestroyReleasesOnlyIncidentEdgesAndPreservesAllMomentum()
        {
            var w=World(Arena(true));w.State.Props.B01.Velocity=new Vec2(2,-3);w.State.Props.B01.AngularVelocity=.5f;
            var before=w.State.Props;ConnectorBodies.Destroy(ref w.State.Props,w.Arena,0);
            Assert.IsTrue(w.State.Props.IsDestroyed(0));
            for(int e=0;e<3;e++)Assert.IsFalse(w.Arena.GetGraphRope(e).Active(w.State.Props));
            Assert.IsTrue(w.Arena.GetGraphRope(3).Active(w.State.Props));Assert.IsTrue(w.Arena.GetGraphRope(4).Active(w.State.Props));
            Assert.AreEqual(before.B01.Velocity,w.State.Props.B01.Velocity);Assert.AreEqual(before.B01.AngularVelocity,w.State.Props.B01.AngularVelocity);
            for(int t=0;t<240;t++)w.Step(default,default);
            Assert.Less(Stretch(w,4),.08f);Assert.IsFalse(w.State.Props.IsDestroyed(1));
        }
        [Test] public void ReleasedAssemblyFallsWhileInternalRopeRemainsAndCanRotate()
        {
            var w=World();w.State.Props.B01.Velocity=new Vec2(3,0);w.State.Props.B01.AngularVelocity=.6f;
            ConnectorBodies.Destroy(ref w.State.Props,w.Arena,0);float y=w.State.Props.B01.Position.Y;
            for(int t=0;t<25;t++)w.Step(default,default);
            Assert.Less(w.State.Props.B01.Position.Y,y-.3f);Assert.Greater(Math.Abs(w.State.Props.B01.Rotation.Y),.01f);
            Assert.IsTrue(w.Arena.GetGraphRope(3).Active(w.State.Props));Assert.Less(Stretch(w,3),.08f);
        }
        [TestCase(ActionState.Lunge)] [TestCase(ActionState.Thrown)]
        public void TravelingAttacksDestroyBeforeSolidCollisionAndSlowOnce(ActionState action)
        {
            var w=World();var f=w.State.Fighter0;f.Position=new Vec2(-2,3);f.Velocity=new Vec2(30,0);f.Action=action;
            f.Grounded=false;f.OnProp=0;f.StunTicks=20;w.State.Fighter0=f;w.State.Fighter0.Position=new Vec2(2,3);
            Resolve(w,f);Assert.IsTrue(w.State.Props.IsDestroyed(0));Assert.AreEqual(f.Velocity*.85f,w.State.Fighter0.Velocity);
            var after=w.State.Fighter0;PropInteraction.Resolve(ref w.State,w.Arena,w.Config,f,w.State.Fighter1,false,false,w.Events,w.PropScratch);
            Assert.AreEqual(after,w.State.Fighter0);Resolve(w,f);Assert.AreEqual(f.Velocity*.85f,w.State.Fighter0.Velocity);
        }
        [Test] public void PunchDestroysPhysicalNodeAndStandingDoesNot()
        {
            var w=World();var f=w.State.Fighter0;f.Position=new Vec2(0,3.325f+w.Config.HalfBody-.01f);f.Action=ActionState.Airborne;f.Velocity=new Vec2(0,-1);
            w.State.Fighter0=f;Resolve(w,f);Assert.IsFalse(w.State.Props.IsDestroyed(0));
            PropInteraction.Resolve(ref w.State,w.Arena,w.Config,f,w.State.Fighter1,false,false,w.Events,w.PropScratch);
            Assert.AreEqual(1,w.State.Fighter0.OnProp);
            f=w.State.Fighter0;f.OnProp=0;f.Action=ActionState.Attack;f.AttackId=AttackIds.Punch;f.AttackDir=new Vec2(1,0);f.Facing=1;
            var attack=w.Config.GetAttack(AttackIds.Punch);f.ActionTick=attack.StartupTicks;
            f.Position=new Vec2(0,3)-FighterCombat.AttackOffset(f,attack.GetCircle(0).At(0));w.State.Fighter0=f;
            Resolve(w,f);Assert.IsTrue(w.State.Props.IsDestroyed(0));Assert.IsFalse(w.State.Fighter0.AttackHasHit);
        }
        [TestCase(ActionState.Idle)] [TestCase(ActionState.Airborne)] [TestCase(ActionState.Hitstun)] [TestCase(ActionState.Charge)]
        public void NormalContactAndNonThrownDamageDoNotDestroy(ActionState action)
        {var w=World();var f=w.State.Fighter0;f.Action=action;f.Position=new Vec2(-2,3);w.State.Fighter0=f;w.State.Fighter0.Position=new Vec2(2,3);Resolve(w,f);Assert.IsFalse(w.State.Props.IsDestroyed(0));}
        [Test] public void LargeForceAndNormalBodyCollisionNeverBreakRopesOrNodes()
        {
            var w=World();w.State.Props.B00.Velocity=new Vec2(20,-40);w.State.Props.B01.Velocity=new Vec2(-15,-20);
            for(int t=0;t<360;t++)w.Step(default,default);
            for(int b=0;b<3;b++){Assert.IsFalse(w.State.Props.IsDestroyed(b));Assert.Zero(PropState.At(w.State.Props,b).Broken);}
            for(int r=0;r<w.Arena.GraphRopeCount;r++)Assert.IsTrue(w.Arena.GetGraphRope(r).Active(w.State.Props));
        }
        [Test] public void MovingBothAnchorsWakeAnEntireReverseOrderedChain()
        {
            var w=World();for(int t=0;t<600;t++)w.Step(default,default);
            w.State.Props.B02.Velocity=new Vec2(5,0);w.State.Props.B02.SleepTicks=0;
            w.Step(default,default);Assert.Less(w.State.Props.B00.SleepTicks,w.Config.Combat.Props.SleepTicks);
            Assert.Less(w.State.Props.B01.SleepTicks,w.Config.Combat.Props.SleepTicks);
            for(int t=0;t<180;t++)w.Step(default,default);Assert.Less(Stretch(w,3),.08f);
        }
        [Test] public void DestroyedSupportWakesRestingUnropedBodyAndRemovesItsContacts()
        {
            var original=Arena();var c=MapPhysicalBoxReactionTests.CurrentConfig();
            var a=original.GetProp(0);a.Center=new Vec2(0,-7.675f);
            var b=original.GetProp(2);b.Center=new Vec2(0,-6.75f);
            var arena=original.WithProps(new[]{a,b});var w=World(arena);
            w.State.Props.B00.SleepTicks=w.State.Props.B01.SleepTicks=c.Combat.Props.SleepTicks;
            float y=w.State.Props.B01.Position.Y;ConnectorBodies.Destroy(ref w.State.Props,arena,0);
            Assert.Zero(w.State.Props.B01.SleepTicks);
            for(int t=0;t<30;t++)w.Step(default,default);
            Assert.Less(w.State.Props.B01.Position.Y,y-.2f);
            for(int i=0;i<w.PropScratch.ContactCount;i++){Assert.AreNotEqual(0,w.PropScratch.Contacts[i].Body);Assert.AreNotEqual(0,w.PropScratch.Contacts[i].Other);}
        }
        [Test] public void EarlierOrdinaryBodyShieldsNodeFromFastTravel()
        {
            var original=Arena();var p=new PropDef[4];for(int i=0;i<3;i++)p[i]=original.GetProp(i);
            p[3]=new PropDef{Kind=PropKind.LooseBox,Id="shield",AnchorProp=-1,Center=new Vec2(-1.5f,3),HalfSize=new Vec2(.3f,1),Mass=100,StartAwake=true};
            var w=World(original.WithProps(p));var f=w.State.Fighter0;f.Action=ActionState.Thrown;f.Velocity=new Vec2(30,0);f.Position=new Vec2(-4,3);
            w.State.Fighter0=f;w.State.Fighter0.Position=new Vec2(4,3);Resolve(w,f);Assert.IsFalse(w.State.Props.IsDestroyed(0));
        }
        [Test] public void SnapshotRestoresAllDestructionBitsAndSubsequentFrames()
        {
            var w=World();var before=w.State;ConnectorBodies.Destroy(ref w.State.Props,w.Arena,0);uint changed=w.ComputeChecksum();
            Assert.AreNotEqual(before.ComputeChecksum(),changed);var resumed=new SimWorld(w.Config,w.Arena,w.State);
            for(int t=0;t<300;t++){w.Step(default,default);resumed.Step(default,default);Assert.AreEqual(w.ComputeChecksum(),resumed.ComputeChecksum());}
            w.State=before;Assert.IsFalse(w.State.Props.IsDestroyed(0));Assert.IsTrue(w.Arena.GetGraphRope(0).Active(w.State.Props));
        }
        [TestCase("source")] [TestCase("source_point")] [TestCase("target_point")] [TestCase("length")] [TestCase("loss")] [TestCase("connector")]
        public void AllNewGameplaySettingsAffectMapChecksum(string field)
        {
            var a=Arena();var props=new PropDef[3];for(int i=0;i<3;i++)props[i]=a.GetProp(i);
            var ropes=new GraphRope[a.GraphRopeCount];for(int i=0;i<ropes.Length;i++)ropes[i]=a.GetGraphRope(i);
            switch(field){case "source":ropes[3].Joint.OtherBody=0;break;case "source_point":ropes[3].Joint.OtherLocal.X+=.1f;break;
                case "target_point":ropes[3].Joint.LocalAnchor.X+=.1f;break;case "length":ropes[3].Joint.Length+=1;break;
                case "loss":props[0].ConnectorSpeedLoss=.3f;break;case "connector":props[0].IsConnector=false;break;}
            var b=a.WithProps(props).WithGraphRopes(ropes);Assert.AreNotEqual(a.ComputeGeometryChecksum(),b.ComputeGeometryChecksum());
            Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{a}),OnlineMapManifest.Compute(new[]{b}));
        }
    }
}
