using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class MapPhysicalBoxSimulationTests
    {
        static SimConfig Config()=>MapPhysicalBoxReactionTests.CurrentConfig();
        static ArenaDef Authored(int count)
        {
            var legacy=MapPhysicalBoxReactionTests.Lab(count);var props=new PropDef[count];var c=Config();
            for(int i=0;i<count;i++){props[i]=legacy.GetProp(i);props[i].Id="cp2_"+i;props[i].Mass=PropPhysics.Mass(props[i],c.Combat.Props);props[i].StartAwake=true;}
            return legacy.WithProps(props);
        }
        static SimWorld World(ArenaDef arena)
        {var c=Config();return new SimWorld(c,arena,SimState.CreateRound(c,arena,77,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.Fighting));}
        static InputRecording Record(SimWorld w)=>InputRecording.Begin(w.Config,new[]{w.Arena},w.State.Match.Rotation,w.State.Rng.State,
            w.State.Fighter0.Position,w.State.Fighter1.Position,w.State.Match.Phase,w.Config.Rules.Format());

        [TestCase(0f)] [TestCase(18f)] [TestCase(34f)]
        public void AuthoredPillarUsesExistingContactReaction_WithoutJoints(float speed)
        {
            var legacy=MapPhysicalBoxReactionTests.Observe(MapPhysicalBoxReactionTests.Settled(MapPhysicalBoxReactionTests.Lab(4)),speed);
            var a=Authored(4);var current=MapPhysicalBoxReactionTests.Observe(MapPhysicalBoxReactionTests.Settled(a),speed);
            Assert.AreEqual(legacy.hash,current.hash);for(int i=0;i<4;i++)Assert.AreEqual(PropJointKind.None,a.GetJoint(i).Kind);
        }
        [Test] public void AuthoredAirborneBoxFalls_LegacySleepingStartIsUnchanged()
        {
            var old=MapPhysicalBoxReactionTests.Lab(1).GetProp(0);old.Center=new Vec2(0,0);
            var oldArena=MapPhysicalBoxReactionTests.Lab(1).WithProps(new[]{old});var legacy=World(oldArena);
            var authored=old;authored.Id="airborne";authored.StartAwake=true;authored.Mass=PropPhysics.Mass(old,Config().Combat.Props);
            var w=World(oldArena.WithProps(new[]{authored}));Assert.AreEqual(Config().Combat.Props.SleepTicks,PropState.At(legacy.State.Props,0).SleepTicks);Assert.Zero(PropState.At(w.State.Props,0).SleepTicks);
            for(int t=0;t<60;t++){legacy.Step(default,default);w.Step(default,default);}
            Assert.AreEqual(0,PropState.At(legacy.State.Props,0).Position.Y);Assert.Less(PropState.At(w.State.Props,0).Position.Y,-2);
        }
        static PropBody SingleImpact(float mass)
        {
            var p=Authored(1).GetProp(0);p.Center=Vec2.Zero;p.Mass=mass;
            var w=World(Authored(1).WithProps(new[]{p}));w.State.Fighter0.Position=new Vec2(-.97f,.35f);w.State.Fighter0.Velocity=new Vec2(18,0);w.State.Fighter0.SetAction(ActionState.Lunge);w.State.Fighter0.Grounded=false;
            for(int t=0;t<4;t++)w.Step(default,default);return PropState.At(w.State.Props,0);
        }
        [Test] public void OrdinaryWalkingPushMovesTouchingLayersWithoutKnockout()
        {
            var w=MapPhysicalBoxReactionTests.Settled(Authored(4));w.State.Fighter0.Position=new Vec2(-3f,-8f+w.Config.HalfBody);w.State.Fighter0.Grounded=true;
            float baseMove=0,upperMove=0;int knocks=0;
            for(int t=0;t<180;t++){
                w.Step(new InputFrame(t<40?1:0,0,InputButtons.None,1),default);
                baseMove=Math.Max(baseMove,Math.Abs(PropState.At(w.State.Props,0).Position.X));
                upperMove=Math.Max(upperMove,Math.Abs(PropState.At(w.State.Props,1).Position.X));
                for(int i=0;i<w.Events.Count;i++)if(w.Events[i].Type==SimEventType.BoxKnockout)knocks++;
            }
            TestContext.WriteLine($"walking push: base={baseMove:F5}, second={upperMove:F5}, knockout={knocks}");
            Assert.Greater(baseMove,.001f);Assert.Greater(upperMove,.001f);Assert.Zero(knocks);
        }
        [Test] public void ExplicitMassAffectsSingleBoxTranslationAndRotation()
        {
            var light=SingleImpact(1.44f);var heavy=SingleImpact(5.76f);
            TestContext.WriteLine($"single: light vx={light.Velocity.X:F4}, spin={light.AngularVelocity:F4}; heavy vx={heavy.Velocity.X:F4}, spin={heavy.AngularVelocity:F4}");
            Assert.Greater(light.Velocity.X,.1f);Assert.Greater(Math.Abs(light.AngularVelocity),.01f);
            Assert.Greater(light.Velocity.X,heavy.Velocity.X);Assert.Greater(Math.Abs(light.AngularVelocity),Math.Abs(heavy.AngularVelocity));
        }
        [Test] public void SeparatedBoxesHaveIndependentVelocityAndRotation()
        {
            var arena=Authored(2);var w=World(arena);
            for(int i=0;i<2;i++){ref var b=ref PropState.Get(ref w.State.Props,i);b.Position=new Vec2(i==0?-5:5,2);b.Velocity=new Vec2(i==0?-1:2,0);b.AngularVelocity=i==0?1:-2;b.SleepTicks=0;b.Warm=default;}
            var other=new SimWorld(w.Config,arena,w.State);PropState.Get(ref other.State.Props,0).Velocity=new Vec2(-8,0);
            for(int t=0;t<20;t++){w.Step(default,default);other.Step(default,default);Assert.AreEqual(PropState.At(w.State.Props,1),PropState.At(other.State.Props,1));}
            Assert.Greater(Math.Abs(PropState.At(w.State.Props,0).Rotation.Y),.05f);Assert.Greater(Math.Abs(PropState.At(w.State.Props,1).Rotation.Y),.05f);
            Assert.AreNotEqual(PropState.At(w.State.Props,0).Velocity,PropState.At(w.State.Props,1).Velocity);
        }
        [Test] public void GeometryAndReplayV9DetectChangedPositionSizeMassIdentityOrAwake_ButOldV8Remains()
        {
            var arena=Authored(1);var rec=Record(World(arena));Assert.AreEqual(9,BitConverter.ToUInt16(rec.ToBytes(),4));
            var loaded=InputRecording.FromBytes(rec.ToBytes());Assert.IsTrue(loaded.UsesGeometryChecksum);Assert.AreEqual(rec.ArenaChecksum,loaded.ExpectedArenaChecksum(new[]{arena}));
            for(int field=0;field<5;field++){
                var p=arena.GetProp(0);switch(field){case 0:p.Center.X+=.5f;break;case 1:p.HalfSize.X+=.25f;break;case 2:p.Mass+=1;break;case 3:p.Id="other";break;case 4:p.StartAwake=false;break;}
                var changed=arena.WithProps(new[]{p});Assert.AreEqual(arena.ComputeChecksum(),changed.ComputeChecksum(),"Legacy hash is frozen");
                Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{arena}),OnlineMapManifest.Compute(new[]{changed}));
                Assert.Throws<InvalidOperationException>(()=>loaded.CreateInitialState(Config(),changed));
            }
            var oldArena=MapPhysicalBoxReactionTests.Lab(1);var old=Record(World(oldArena));Assert.AreEqual(8,BitConverter.ToUInt16(old.ToBytes(),4));
            Assert.IsFalse(InputRecording.FromBytes(old.ToBytes()).UsesGeometryChecksum);Assert.AreEqual(oldArena.ComputeChecksum(),old.ArenaChecksum);
            Assert.IsFalse(oldArena.RequiresExtendedMapChecksum);
        }
        [Test] public void NewPhysicalMap_ReplayAndRollbackAgreeAtEveryTick()
        {
            var arena=Authored(4);var w=World(arena);var rec=Record(w);var sync=new SyncTest<SimWorld>(w);var sums=new List<uint>();
            for(int t=0;t<720;t++){
                var input=new InputFrame(t<260?1:0,0,t>=100&&t<145?InputButtons.Attack:InputButtons.None,1);
                rec.Append(input,default);Assert.IsTrue(sync.Advance(input,default),"rollback tick "+t);sums.Add(w.ComputeChecksum());
            }
            rec.Finish(w.ComputeChecksum());var loaded=InputRecording.FromBytes(rec.ToBytes());var replay=new SimWorld(Config(),arena,loaded.CreateInitialState(Config(),arena));
            for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(sums[t],replay.ComputeChecksum(),"replay tick "+t);}
            Assert.AreEqual(rec.FinalChecksum,replay.ComputeChecksum());
        }
        [TestCase(false)] [TestCase(true)]
        public void NewPhysicalMap_MemoryLockstepAgreesAndMassMismatchRefusesSession(bool passThrough)
        {
            var arena=Authored(4);
            if(passThrough)
            {
                var definition=MapTallPillarTests.Definition(true,-2.25f,true);
                try{arena=definition.ToArena();}finally{Object.DestroyImmediate(definition);}
            }
            var hostWorld=World(arena);var guestWorld=World(arena);MemoryTransport.Pair(out var ma,out var mb);
            uint map=OnlineMapManifest.Compute(new[]{arena});var host=new LockstepSession(ma,true,Config().ComputeChecksum(),map,77,3);var guest=new LockstepSession(mb,false,Config().ComputeChecksum(),map,0,0);
            double now=0;var h=new List<uint>();var g=new List<uint>();
            try{
                for(int i=0;i<120&&(host.State!=SessionState.Running||guest.State!=SessionState.Running);i++){now+=1d/60;guest.Poll(now);host.Poll(now);}
                Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
                host.Begin(0,new BrawlChaserBot(()=>hostWorld,0,10));guest.Begin(0,new BrawlBot(()=>guestWorld,1,0xB22u));
                for(int frame=0;frame<900;frame++){
                    now+=1d/60;host.Poll(now);guest.Poll(now);
                    for(int i=0;i<2&&host.TryGetInputs(hostWorld.State.Tick,out var p0,out var p1);i++){hostWorld.Step(p0,p1);uint sum=hostWorld.ComputeChecksum();h.Add(sum);host.ReportChecksum(hostWorld.State.Tick,sum);}
                    for(int i=0;i<2&&guest.TryGetInputs(guestWorld.State.Tick,out var p0,out var p1);i++){guestWorld.Step(p0,p1);uint sum=guestWorld.ComputeChecksum();g.Add(sum);guest.ReportChecksum(guestWorld.State.Tick,sum);}
                }
                int n=Math.Min(h.Count,g.Count);Assert.Greater(n,700);CollectionAssert.AreEqual(h.Take(n),g.Take(n));Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
            }finally{ma.Dispose();mb.Dispose();}
            var changed=Enumerable.Range(0,arena.PropCount).Select(i=>arena.GetProp(i)).ToArray();changed[0].Mass+=1;MemoryTransport.Pair(out ma,out mb);
            host=new LockstepSession(ma,true,Config().ComputeChecksum(),map,77,3);guest=new LockstepSession(mb,false,Config().ComputeChecksum(),OnlineMapManifest.Compute(new[]{arena.WithProps(changed)}),0,0);
            try{for(int i=0;i<120;i++){now+=1d/60;host.Poll(now);guest.Poll(now);}Assert.AreNotEqual(SessionState.Running,guest.State);}finally{ma.Dispose();mb.Dispose();}
        }
    }
}
