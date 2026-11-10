using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class RopeGraphReplayTests
    {
        public static ArenaDef TrialArena()
        {
            var a=RopeGraphTests.Arena();var p=new PropDef[a.PropCount];for(int i=0;i<p.Length;i++)p[i]=a.GetProp(i);
            var r=new GraphRope[a.GraphRopeCount];for(int i=0;i<r.Length;i++)r[i]=a.GetGraphRope(i);
            return new ArenaDef("Graph_Input_Trial",36,20,false,new[]{a.GetBox(0),new ArenaBox(TerrainKind.Solid,-3,2,-.65f,2.5f)},
                new Vec2(-1,2.5f),a.SpawnFoot1).WithProps(p).WithGraphRopes(r);
        }
        static InputFrame Input(int tick)=>new InputFrame(0,0,tick==0?InputButtons.Attack:InputButtons.None,1);
        [Test] public void ActualPunchInputDestructionReplaysAndRollsBackEveryTick()
        {
            var w=RopeGraphTests.World(TrialArena());var rec=InputRecording.Begin(w.Config,w.Arena,77,w.State.Fighter0.Position,w.State.Fighter1.Position,MatchPhase.Fighting,"brawl");
            var sync=new SyncTest<SimWorld>(w);var hashes=new List<uint>();int broken=-1;
            for(int t=0;t<360;t++){var input=Input(t);rec.Append(input,default);Assert.IsTrue(sync.Advance(input,default));hashes.Add(w.ComputeChecksum());if(w.State.Props.IsDestroyed(0)&&broken<0)broken=t;}
            Assert.That(broken,Is.InRange(1,30));rec.Finish(w.ComputeChecksum());var loaded=InputRecording.FromBytes(rec.ToBytes());Assert.AreEqual(9,loaded.WrittenVersion);
            var replay=new SimWorld(w.Config,w.Arena,loaded.CreateInitialState(w.Config,w.Arena));
            for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"tick "+t);}
            TestContext.WriteLine("physical connector punch break="+broken+"; replay/rollback="+hashes.Count);
        }
        [TestCase(false)] [TestCase(true)]
        public void ActualChargeAndThrowInputsDestroyWithoutWallStun(bool thrown)
        {
            var w=RopeGraphTests.World();w.State.Fighter0.Position=new Vec2(-1.8f,3);w.State.Fighter0.Grounded=false;w.State.Fighter0.GroundBox=FighterState.NoBox;
            if(thrown)
            {w.State.Fighter0.Action=ActionState.Holding;w.State.Fighter0.PrevButtons=InputButtons.Grab;w.State.Fighter1.Action=ActionState.Held;w.State.Fighter1.Position=new Vec2(-1,3);w.State.Fighter1.Grounded=false;}
            else
            {w.State.Fighter0.Action=ActionState.Charge;w.State.Fighter0.PrevButtons=InputButtons.Attack;w.State.Fighter0.ActionTick=w.Config.Combat.Body.TapHoldTicks+w.Config.Combat.Body.MinLungeChargeTicks+5;}
            int broken=-1;
            for(int t=0;t<12;t++)
            {
                w.Step(new InputFrame(0,0,thrown&&t==0?InputButtons.Attack:InputButtons.None,1),default);
                if(!w.State.Props.IsDestroyed(0))continue;
                var f=w.State.GetFighter(thrown?1:0);Assert.AreEqual(thrown?ActionState.Thrown:ActionState.Lunge,f.Action);
                Assert.Greater(f.Velocity.X,0);broken=t;break;
            }
            Assert.GreaterOrEqual(broken,0);TestContext.WriteLine((thrown?"throw":"charge")+" physical node break="+broken);
        }
        [Test] public void StandAndJumpOnThePhysicalNodeNeverDestroyIt()
        {
            var w=RopeGraphTests.World();w.State.Fighter0.Position=new Vec2(0,3.325f+w.Config.HalfBody);w.State.Fighter0.OnProp=1;
            bool stood=false,jumped=false;
            for(int t=0;t<120;t++)
            {
                w.Step(new InputFrame(0,0,t==60?InputButtons.Jump:InputButtons.None,1),default);
                stood|=w.State.Fighter0.OnProp==1;jumped|=t>60&&w.State.Fighter0.Velocity.Y>1;
                Assert.IsFalse(w.State.Props.IsDestroyed(0));
            }
            Assert.IsTrue(stood);Assert.IsTrue(jumped);
        }
        [TestCase(false)] [TestCase(true)] public void LossyOnlineInputAgreesOnNodeDestructionAndAllEdges(bool stableContacts)
        {
            var a=TrialArena();var original=RopeGraphTests.World(a);var profile=ConnectorContactTests.Config(stableContacts);
            var hw=new SimWorld(profile,a,original.State);var gw=new SimWorld(profile,a,original.State);
            MemoryTransport.Pair(out var ta,out var tb);double now=0;
            var la=new SimulatedLink(ta,()=>now,.04,.015,.10,.05,5);var lb=new SimulatedLink(tb,()=>now,.04,.015,.10,.05,6);
            uint manifest=OnlineMapManifest.Compute(new[]{a});var c=hw.Config;
            using(var host=new LockstepSession(la,true,c.ComputeChecksum(),manifest,77,6))
            using(var guest=new LockstepSession(lb,false,c.ComputeChecksum(),manifest,0,0))
            {
                for(int t=0;t<180&&(host.State!=SessionState.Running||guest.State!=SessionState.Running);t++){now+=1.0/60;host.Poll(now);guest.Poll(now);}
                Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
                var frames=new InputFrame[1600];for(int t=0;t<frames.Length;t++)frames[t]=Input(t);
                host.Begin(0,new FrameArrayInputSource(frames));guest.Begin(0,new IdleInputSource());
                var hh=new List<uint>();var gh=new List<uint>();int hb=-1,gb=-1;
                for(int t=0;t<1200;t++)
                {
                    now+=1.0/60;host.Poll(now);guest.Poll(now);
                    for(int i=0;i<2&&host.TryGetInputs(hw.State.Tick,out var p0,out var p1);i++)
                    {hw.Step(p0,p1);uint h=hw.ComputeChecksum();hh.Add(h);host.ReportChecksum(hw.State.Tick,h);if(hb<0&&hw.State.Props.IsDestroyed(0))hb=hw.State.Tick;}
                    for(int i=0;i<2&&guest.TryGetInputs(gw.State.Tick,out var p0,out var p1);i++)
                    {gw.Step(p0,p1);uint h=gw.ComputeChecksum();gh.Add(h);guest.ReportChecksum(gw.State.Tick,h);if(gb<0&&gw.State.Props.IsDestroyed(0))gb=gw.State.Tick;}
                }
                int count=Math.Min(hh.Count,gh.Count);Assert.Greater(count,800);Assert.Greater(hb,0);Assert.AreEqual(hb,gb);
                Assert.AreEqual(SessionState.Running,host.State,host.EndDetail);Assert.AreEqual(SessionState.Running,guest.State,guest.EndDetail);
                for(int i=0;i<count;i++)Assert.AreEqual(hh[i],gh[i],"tick "+i);Assert.Greater(host.ChecksumsCompared,10);
                TestContext.WriteLine("graph online compared="+count+"; destroyed="+hb+"; dropped="+(la.Dropped+lb.Dropped));
            }
        }
        [Test] public void DifferentGraphEndpointRejectsReplayAndOnlineHandshake()
        {
            var a=TrialArena();var r=new GraphRope[a.GraphRopeCount];for(int i=0;i<r.Length;i++)r[i]=a.GetGraphRope(i);r[3].Joint.OtherLocal.X=.1f;
            var b=a.WithGraphRopes(r);var c=MapPhysicalBoxReactionTests.CurrentConfig();var rec=InputRecording.Begin(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting,"brawl");
            Assert.Throws<InvalidOperationException>(()=>rec.CreateInitialState(c,b));
            MemoryTransport.Pair(out var ta,out var tb);
            using(var h=new LockstepSession(ta,true,c.ComputeChecksum(),OnlineMapManifest.Compute(new[]{a}),77,3))
            using(var g=new LockstepSession(tb,false,c.ComputeChecksum(),OnlineMapManifest.Compute(new[]{b}),0,0))
            {for(int t=1;t<=180;t++){h.Poll(t/60.0);g.Poll(t/60.0);}Assert.IsTrue(h.Reason==EndReason.Rejected||g.Reason==EndReason.Rejected);}
        }
    }
}
