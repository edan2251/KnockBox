using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapRopeOnlineTests
    {
        static ArenaDef Arena(bool convex)
        {
            var d=MapIndependentRopeTests.Definition();ArenaDef a;try{a=d.ToArena();}finally{UnityEngine.Object.DestroyImmediate(d);}
            if(!convex)return a;
            var ramp=new ConvexPolygon(new[]{new Vec2(-12,-8),new Vec2(-4,-8),new Vec2(-4,0)});
            var shape=new ArenaDef("CP4_Online_Mixed",36,20,false,new[]{a.GetBox(0),new ArenaBox(TerrainKind.Solid,ramp)},a.SpawnFoot0,a.SpawnFoot1);
            return shape.WithProps(new[]{a.GetProp(0),new MapPhysicalBox{id="slope_box",center=new Vector2(-8,2),size=new Vector2(1.5f,1.5f),mass=2.25f}.ToProp(1)});
        }
        sealed class Side
        {
            public LockstepSession session;public SimWorld world;public List<uint> hashes=new List<uint>();public int broken=-1;
            public void Advance(double now)
            {
                session.Poll(now);
                for(int i=0;i<2&&session.TryGetInputs(world.State.Tick,out var p0,out var p1);i++)
                {
                    world.Step(p0,p1);uint hash=world.ComputeChecksum();hashes.Add(hash);session.ReportChecksum(world.State.Tick,hash);
                    if(world.State.Props.B00.Broken!=0&&broken<0)broken=world.State.Tick;
                }
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void LossyNetworkMatchesRopeBreakAndConvexContactsEveryTick(bool convex)
        {
            var a=Arena(convex);var config=MapPhysicalBoxReactionTests.CurrentConfig();var s=SimState.CreateRound(config,a,77,
                new Vec2(a.SpawnFoot0.X,a.SpawnFoot0.Y+config.HalfBody),new Vec2(a.SpawnFoot1.X,a.SpawnFoot1.Y+config.HalfBody),MatchPhase.Fighting);
            MemoryTransport.Pair(out var ta,out var tb);double now=0;
            var la=new SimulatedLink(ta,()=>now,.04,.015,.10,.05,5);
            var lb=new SimulatedLink(tb,()=>now,.04,.015,.10,.05,6);
            uint map=OnlineMapManifest.Compute(new[]{a});
            var host=new Side{world=new SimWorld(config,a,s),session=new LockstepSession(la,true,config.ComputeChecksum(),map,77,6)};
            var guest=new Side{world=new SimWorld(config,a,s),session=new LockstepSession(lb,false,config.ComputeChecksum(),map,0,0)};
            try
            {
                for(int i=0;i<180&&(host.session.State!=SessionState.Running||guest.session.State!=SessionState.Running);i++)
                {now+=1.0/60;host.session.Poll(now);guest.session.Poll(now);}
                Assert.AreEqual(SessionState.Running,host.session.State);Assert.AreEqual(SessionState.Running,guest.session.State);
                var frames=new InputFrame[1800];
                for(int t=0;t<frames.Length;t++)frames[t]=new InputFrame(0,0,t>=60&&t<420&&(t-60)%90<36?InputButtons.Attack:InputButtons.None,1);
                host.session.Begin(0,new FrameArrayInputSource(frames));guest.session.Begin(0,new IdleInputSource());
                for(int f=0;f<1800;f++){now+=1.0/60;host.Advance(now);guest.Advance(now);}
                int count=Math.Min(host.hashes.Count,guest.hashes.Count);Assert.Greater(count,1000);
                Assert.AreEqual(SessionState.Running,host.session.State,host.session.EndDetail);Assert.AreEqual(SessionState.Running,guest.session.State,guest.session.EndDetail);
                for(int i=0;i<count;i++)Assert.AreEqual(host.hashes[i],guest.hashes[i],"tick "+i);
                Assert.Greater(host.broken,0);Assert.AreEqual(host.broken,guest.broken);Assert.Greater(host.session.ChecksumsCompared,20);
                TestContext.WriteLine($"convex={convex}; compared={count}; rope break={host.broken}; dropped={la.Dropped+lb.Dropped}");
            }
            finally{host.session.Dispose();guest.session.Dispose();}
        }
        [TestCase(false)] [TestCase(true)]
        public void MismatchedRopeOrPolygonIsRejectedBeforeMatch(bool polygon)
        {
            var a=Arena(true);ArenaDef b;
            if(polygon)
            {
                var changed=new ArenaBox(TerrainKind.Solid,new ConvexPolygon(new[]{new Vec2(-12,-8),new Vec2(-4,-8),new Vec2(-4,-1)}));
                b=new ArenaDef(a.Name,36,20,false,new[]{a.GetBox(0),changed},a.SpawnFoot0,a.SpawnFoot1).WithProps(new[]{a.GetProp(0),a.GetProp(1)});
            }
            else {var prop=a.GetProp(0);prop.Rope.BreakForce+=1;b=a.WithProps(new[]{prop,a.GetProp(1)});}
            MemoryTransport.Pair(out var ta,out var tb);var config=MapPhysicalBoxReactionTests.CurrentConfig();
            using(var host=new LockstepSession(ta,true,config.ComputeChecksum(),OnlineMapManifest.Compute(new[]{a}),77,3))
            using(var guest=new LockstepSession(tb,false,config.ComputeChecksum(),OnlineMapManifest.Compute(new[]{b}),0,0))
            {
                for(int i=1;i<=180;i++){host.Poll(i/60.0);guest.Poll(i/60.0);}
                Assert.IsTrue(host.State==SessionState.Ended||guest.State==SessionState.Ended);
                Assert.IsTrue(host.Reason==EndReason.Rejected||guest.Reason==EndReason.Rejected);
            }
        }
    }
}
