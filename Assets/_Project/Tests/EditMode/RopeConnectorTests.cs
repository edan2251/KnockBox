using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class RopeConnectorTests
    {
        public static ArenaDef Arena(RopeBreakMode mode=RopeBreakMode.Connector,float loss=.15f)
        {
            var p=new PropDef{Kind=PropKind.LooseBox,Id="connector_box",Center=new Vec2(0,-2.75f),
                HalfSize=new Vec2(.75f,.75f),Mass=2.25f,StartAwake=true,
                Rope=new AuthoredRope{Enabled=true,Id="connector_rope",WorldAnchor=new Vec2(0,2),
                    LocalAnchor=new Vec2(0,.75f),Length=4,Breakable=true,BreakForce=1,RopeMode=mode,
                    ConnectorPosition=.5f,ConnectorSize=.55f,ConnectorSpeedLoss=loss}};
            return new ArenaDef("Connector_Trial",36,20,false,new[]{new ArenaBox(TerrainKind.Solid,-18,-10,18,-8)},
                new Vec2(-.9f,.25f),new Vec2(12,-7.6f)).WithProps(new[]{p});
        }
        public static SimWorld World(ArenaDef arena=null)
        {
            var a=arena??Arena();var c=MapPhysicalBoxReactionTests.CurrentConfig();
            return new SimWorld(c,a,SimState.CreateRound(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting));
        }
        static void PrepareScratch(SimWorld w)
        {for(int i=0;i<w.State.Props.Count;i++){var b=PropState.At(w.State.Props,i);w.PropScratch.StartPosition[i]=b.Position;w.PropScratch.StartRotation[i]=b.Rotation;}}
        static void Resolve(SimWorld w,FighterState before,bool frozen=false)
        {PrepareScratch(w);RopeConnectors.Resolve(ref w.State,w.Arena,w.Config,before,w.State.Fighter1,frozen,false,w.PropScratch,w.Events);}
        static FighterState Travel(SimWorld w,ActionState action,int player=0)
        {
            var f=w.State.GetFighter(player);f.Action=action;f.AttackId=FighterState.NoAttack;f.Position=new Vec2(-4,0);
            f.Velocity=new Vec2(30,6);f.StunTicks=20;f.Grounded=false;f.GroundBox=FighterState.NoBox;
            if(player==0)w.State.Fighter0=f;else w.State.Fighter1=f;return f;
        }
        static FighterState Punch(SimWorld w)
        {
            var f=w.State.Fighter0;f.Action=ActionState.Attack;f.AttackId=AttackIds.Punch;f.AttackDir=new Vec2(1,0);f.Facing=1;
            var a=w.Config.GetAttack(AttackIds.Punch);f.ActionTick=a.StartupTicks;
            f.Position=-FighterCombat.AttackOffset(f,a.GetCircle(0).At(0));f.Velocity=new Vec2(2,3);
            w.State.Fighter0=f;return f;
        }
        [TestCase(ActionState.Lunge)] [TestCase(ActionState.Thrown)]
        public void FastBodyPassesThroughWithOneAdjustableSlowdownAndReleasesMomentum(ActionState action)
        {
            var w=World();var before=Travel(w,action);w.State.Fighter0.Position=new Vec2(4,0);
            w.State.Props.B00.Velocity=new Vec2(3,-7);w.State.Props.B00.AngularVelocity=2;
            var prop=w.State.Props.B00;var now=w.State.Fighter0;Resolve(w,before);
            Assert.AreEqual(1,w.State.Props.B00.Broken);Assert.AreEqual(before.Velocity*.85f,w.State.Fighter0.Velocity);
            Assert.AreEqual(now.Position,w.State.Fighter0.Position);Assert.AreEqual(now.Health,w.State.Fighter0.Health);
            Assert.AreEqual(now.StunTicks,w.State.Fighter0.StunTicks);Assert.AreEqual(action,w.State.Fighter0.Action);
            Assert.AreEqual(prop.Velocity,w.State.Props.B00.Velocity);Assert.AreEqual(prop.AngularVelocity,w.State.Props.B00.AngularVelocity);
            Assert.AreEqual(1,w.Events.Count);Assert.AreEqual(SimEventType.JointBreak,w.Events[0].Type);
            Resolve(w,before);Assert.AreEqual(1,w.Events.Count);Assert.AreEqual(before.Velocity*.85f,w.State.Fighter0.Velocity);
        }
        [TestCase(0f)] [TestCase(.35f)] [TestCase(1f)]
        public void SavedLossControlsRemainingSpeed(float loss)
        {var w=World(Arena(loss:loss));var before=Travel(w,ActionState.Thrown);w.State.Fighter0.Position=new Vec2(4,0);Resolve(w,before);Assert.AreEqual(before.Velocity*(1-loss),w.State.Fighter0.Velocity);}
        [Test] public void PunchUsesActiveHitCirclesAndDoesNotConsumeOpponentHitOrSlowAttacker()
        {
            var w=World();var f=Punch(w);Resolve(w,f);Assert.AreEqual(1,w.State.Props.B00.Broken);
            Assert.IsFalse(w.State.Fighter0.AttackHasHit);Assert.AreEqual(f.Velocity,w.State.Fighter0.Velocity);
            Assert.AreEqual(f.Action,w.State.Fighter0.Action);Assert.AreEqual(f.ActionTick,w.State.Fighter0.ActionTick);
        }
        [TestCase(ActionState.Idle)] [TestCase(ActionState.Run)] [TestCase(ActionState.Airborne)]
        [TestCase(ActionState.Hitstun)] [TestCase(ActionState.Charge)]
        public void WalkingJumpingAndNonThrownHitstunDoNotBreakOrSlow(ActionState action)
        {var w=World();var before=Travel(w,action);w.State.Fighter0.Position=new Vec2(4,0);Resolve(w,before);Assert.Zero(w.State.Props.B00.Broken);Assert.AreEqual(before.Velocity,w.State.Fighter0.Velocity);Assert.Zero(w.Events.Count);}
        [TestCase("startup")] [TestCase("miss")] [TestCase("frozen")] [TestCase("already_hit")]
        public void NonHitsDoNotBreak(string reason)
        {
            var w=World();Punch(w);
            if(reason=="startup")w.State.Fighter0.ActionTick--;
            if(reason=="miss")w.State.Fighter0.Position.Y+=3;
            if(reason=="already_hit")w.State.Fighter0.AttackHasHit=true;
            Resolve(w,w.State.Fighter0,reason=="frozen");Assert.Zero(w.State.Props.B00.Broken);Assert.Zero(w.Events.Count);
        }
        [TestCase(RopeBreakMode.Unbreakable)] [TestCase(RopeBreakMode.Connector)]
        public void NewRopesIgnoreOrdinaryAndExtremeTension(RopeBreakMode mode)
        {
            var w=World(Arena(mode));w.State.Fighter0.Position=new Vec2(-12,-7.6f);
            w.State.Props.B00.Velocity=new Vec2(12,-25);float peak=0;
            for(int t=0;t<300;t++){w.Step(default,default);peak=Math.Max(peak,w.PropScratch.RopePull[0]/SimConfig.Dt);Assert.Zero(w.State.Props.B00.Broken);}
            Assert.Greater(peak,600);Assert.Zero(w.Arena.GetJoint(0).BreakForce);
            var f=Punch(w);Resolve(w,f);if(mode==RopeBreakMode.Unbreakable)Assert.Zero(w.State.Props.B00.Broken);
        }
        [Test] public void SimultaneousBodyHitsSlowBothSlotsWithoutOrderBias()
        {
            var w=World();var p0=Travel(w,ActionState.Thrown);var p1=Travel(w,ActionState.Lunge,1);
            p1.Position=new Vec2(4,0);p1.Velocity=-p1.Velocity;w.State.Fighter1=p1;
            w.State.Fighter0.Position=new Vec2(4,0);w.State.Fighter1.Position=new Vec2(-4,0);PrepareScratch(w);
            RopeConnectors.Resolve(ref w.State,w.Arena,w.Config,p0,p1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(1,w.Events.Count);Assert.AreEqual(SimEvent.NoFighter,w.Events[0].Fighter);
            Assert.AreEqual(p0.Velocity*.85f,w.State.Fighter0.Velocity);Assert.AreEqual(p1.Velocity*.85f,w.State.Fighter1.Velocity);
        }
        [Test] public void EarlierHitWinsAndSecondPlayerDoesNotPayForAlreadyBrokenConnector()
        {
            var w=World();var p0=Travel(w,ActionState.Thrown);var p1=Travel(w,ActionState.Lunge,1);
            p1.Position=new Vec2(2,0);p1.Velocity=-p1.Velocity;w.State.Fighter1=p1;
            w.State.Fighter0.Position=new Vec2(4,0);w.State.Fighter1.Position=new Vec2(-6,0);PrepareScratch(w);
            RopeConnectors.Resolve(ref w.State,w.Arena,w.Config,p0,p1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(p0.Velocity,w.State.Fighter0.Velocity);Assert.AreEqual(p1.Velocity*.85f,w.State.Fighter1.Velocity);
        }
        [TestCase(false)] [TestCase(true)]
        public void SolidAndPolygonWallsOccludePunch(bool polygon)
        {
            var a=Arena();var wall=polygon?new ArenaBox(TerrainKind.Solid,new ConvexPolygon(new[]{new Vec2(-.4f,-1),new Vec2(-.3f,-1),new Vec2(-.3f,1),new Vec2(-.4f,1)}))
                :new ArenaBox(TerrainKind.Solid,-.4f,-1,-.3f,1);
            a=new ArenaDef(a.Name,36,20,false,new[]{a.GetBox(0),wall},a.SpawnFoot0,a.SpawnFoot1).WithProps(new[]{a.GetProp(0)});
            var w=World(a);var f=Punch(w);Resolve(w,f);Assert.Zero(w.State.Props.B00.Broken);
        }
        [Test] public void MovingConnectorSweepsRelativeToFighterAndSnapshotRestoresBreak()
        {
            var w=World();var f=Punch(w);f.Position.X+=2;w.State.Fighter0=f;PrepareScratch(w);
            w.State.Props.B00.Position.X+=6;var before=w.State;
            RopeConnectors.Resolve(ref w.State,w.Arena,w.Config,f,w.State.Fighter1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(1,w.State.Props.B00.Broken);uint hash=w.ComputeChecksum();var evt=w.Events[0];
            w.State=before;w.Events.Clear();RopeConnectors.Resolve(ref w.State,w.Arena,w.Config,f,w.State.Fighter1,false,false,w.PropScratch,w.Events);
            Assert.AreEqual(hash,w.ComputeChecksum());Assert.AreEqual(evt,w.Events[0]);
            var resumed=new SimWorld(w.Config,w.Arena,w.State);
            for(int t=0;t<90;t++){w.Step(default,default);resumed.Step(default,default);Assert.AreEqual(w.ComputeChecksum(),resumed.ComputeChecksum());}
        }
        [TestCase(false)] [TestCase(true)]
        public void RealChargeReleaseAndThrowInputPassThroughWithoutWallStun(bool thrown)
        {
            var w=World();var control=World(Arena(RopeBreakMode.Unbreakable));
            w.State.Fighter0.Position=new Vec2(-1.8f,0);w.State.Fighter0.Grounded=false;w.State.Fighter0.GroundBox=FighterState.NoBox;
            if(thrown)
            {
                w.State.Fighter0.Action=ActionState.Holding;w.State.Fighter0.ActionTick=0;w.State.Fighter0.PrevButtons=InputButtons.Grab;
                w.State.Fighter1.Action=ActionState.Held;w.State.Fighter1.Position=new Vec2(-1,0);w.State.Fighter1.Grounded=false;w.State.Fighter1.GroundBox=FighterState.NoBox;
            }
            else
            {
                w.State.Fighter0.Action=ActionState.Charge;w.State.Fighter0.PrevButtons=InputButtons.Attack;
                w.State.Fighter0.ActionTick=w.Config.Combat.Body.TapHoldTicks+w.Config.Combat.Body.MinLungeChargeTicks+5;
            }
            control.State=w.State;int broken=-1;bool launched=false;
            for(int t=0;t<12;t++)
            {
                var input=new InputFrame(0,0,thrown&&t==0?InputButtons.Attack:InputButtons.None,1);
                w.Step(input,default);control.Step(input,default);
                for(int e=0;e<w.Events.Count;e++)if(w.Events[e].Type==(thrown?SimEventType.Throw:SimEventType.LungeStart))launched=true;
                if(w.State.Props.B00.Broken==0)continue;
                var f=w.State.GetFighter(thrown?1:0);var same=control.State.GetFighter(thrown?1:0);
                Assert.AreEqual(thrown?ActionState.Thrown:ActionState.Lunge,f.Action);Assert.AreEqual(same.Health,f.Health);Assert.AreEqual(same.StunTicks,f.StunTicks);
                Assert.AreEqual(same.Position,f.Position);Assert.AreEqual(same.Velocity*.85f,f.Velocity);Assert.Zero(control.State.Props.B00.Broken);
                broken=t;break;
            }
            Assert.IsTrue(launched);Assert.GreaterOrEqual(broken,0);TestContext.WriteLine((thrown?"throw":"charge release")+" broke at tick "+broken);
        }
        [Test] public void SeparateConnectorsEachSlowOnceAndUseSameOrderedStateOnRestore()
        {
            var a=Arena();var p=a.GetProp(0);p.Id="connector_box_2";p.Center.X=3;p.Rope.Id="connector_rope_2";p.Rope.WorldAnchor.X=3;
            var w=World(a.WithProps(new[]{a.GetProp(0),p}));var previous=Travel(w,ActionState.Thrown);w.State.Fighter0.Position=new Vec2(5,0);
            Resolve(w,previous);Assert.AreEqual(1,w.State.Props.B00.Broken);Assert.AreEqual(1,w.State.Props.B01.Broken);Assert.AreEqual(2,w.Events.Count);
            Assert.AreEqual(previous.Velocity*(.85f*.85f),w.State.Fighter0.Velocity);
            Resolve(w,previous);Assert.AreEqual(2,w.Events.Count);Assert.AreEqual(previous.Velocity*(.85f*.85f),w.State.Fighter0.Velocity);
        }

        public static InputFrame TrialInput(int tick)=>new InputFrame(0,0,tick==0?InputButtons.Attack:InputButtons.None,1);
        [Test] public void ActualClickBreaksConnectorAndEveryReplayRollbackTickMatches()
        {
            var w=World();var rec=InputRecording.Begin(w.Config,w.Arena,77,w.State.Fighter0.Position,w.State.Fighter1.Position,MatchPhase.Fighting,"brawl");
            var sync=new SyncTest<SimWorld>(w);var hashes=new List<uint>();int broken=-1;
            for(int t=0;t<360;t++){var input=TrialInput(t);rec.Append(input,default);Assert.IsTrue(sync.Advance(input,default));hashes.Add(w.ComputeChecksum());if(w.State.Props.B00.Broken!=0&&broken<0)broken=t;}
            Assert.That(broken,Is.InRange(1,30));rec.Finish(w.ComputeChecksum());var loaded=InputRecording.FromBytes(rec.ToBytes());Assert.AreEqual(9,loaded.WrittenVersion);
            var replay=new SimWorld(w.Config,w.Arena,loaded.CreateInitialState(w.Config,w.Arena));
            for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"tick "+t);}
            TestContext.WriteLine("click break tick="+broken+"; replay/restore ticks="+hashes.Count);
        }
        [TestCase("mode")] [TestCase("position")] [TestCase("size")] [TestCase("loss")]
        public void EveryNewRuleChangesManifestAndWrongReplayIsRejected(string field)
        {
            var a=Arena();var p=a.GetProp(0);
            if(field=="mode")p.Rope.RopeMode=RopeBreakMode.Unbreakable;
            if(field=="position")p.Rope.ConnectorPosition+=.1f;
            if(field=="size")p.Rope.ConnectorSize+=.1f;
            if(field=="loss")p.Rope.ConnectorSpeedLoss+=.1f;
            var b=a.WithProps(new[]{p});Assert.AreNotEqual(a.ComputeGeometryChecksum(),b.ComputeGeometryChecksum());
            Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{a}),OnlineMapManifest.Compute(new[]{b}));
            var c=MapPhysicalBoxReactionTests.CurrentConfig();var rec=InputRecording.Begin(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting,"brawl");
            Assert.Throws<InvalidOperationException>(()=>rec.CreateInitialState(c,b));
            MemoryTransport.Pair(out var ta,out var tb);
            using(var host=new LockstepSession(ta,true,c.ComputeChecksum(),OnlineMapManifest.Compute(new[]{a}),77,3))
            using(var guest=new LockstepSession(tb,false,c.ComputeChecksum(),OnlineMapManifest.Compute(new[]{b}),0,0))
            {
                for(int t=1;t<=180;t++){host.Poll(t/60.0);guest.Poll(t/60.0);}
                Assert.IsTrue(host.Reason==EndReason.Rejected||guest.Reason==EndReason.Rejected);
            }
        }
        [Test] public void LossyOnlineInputAgreesOnConnectorBreakAndEveryTick()
        {
            var a=Arena();var hostWorld=World(a);var guestWorld=World(a);
            // The network buffers six idle ticks before the first click. Start higher so that the same punch reaches the connector.
            hostWorld.State.Fighter0.Position.Y+=1;guestWorld.State.Fighter0.Position.Y+=1;
            MemoryTransport.Pair(out var ta,out var tb);double now=0;
            var la=new SimulatedLink(ta,()=>now,.04,.015,.10,.05,5);var lb=new SimulatedLink(tb,()=>now,.04,.015,.10,.05,6);
            uint manifest=OnlineMapManifest.Compute(new[]{a});var c=hostWorld.Config;
            using(var host=new LockstepSession(la,true,c.ComputeChecksum(),manifest,77,6))
            using(var guest=new LockstepSession(lb,false,c.ComputeChecksum(),manifest,0,0))
            {
                for(int t=0;t<180&&(host.State!=SessionState.Running||guest.State!=SessionState.Running);t++){now+=1.0/60;host.Poll(now);guest.Poll(now);}
                Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
                var frames=new InputFrame[1600];for(int t=0;t<frames.Length;t++)frames[t]=TrialInput(t);
                host.Begin(0,new FrameArrayInputSource(frames));guest.Begin(0,new IdleInputSource());
                var hh=new List<uint>();var gh=new List<uint>();int hb=-1,gb=-1;
                for(int t=0;t<1200;t++)
                {
                    now+=1.0/60;host.Poll(now);guest.Poll(now);
                    for(int i=0;i<2&&host.TryGetInputs(hostWorld.State.Tick,out var p0,out var p1);i++)
                    {hostWorld.Step(p0,p1);uint h=hostWorld.ComputeChecksum();hh.Add(h);host.ReportChecksum(hostWorld.State.Tick,h);if(hb<0&&hostWorld.State.Props.B00.Broken!=0)hb=hostWorld.State.Tick;}
                    for(int i=0;i<2&&guest.TryGetInputs(guestWorld.State.Tick,out var p0,out var p1);i++)
                    {guestWorld.Step(p0,p1);uint h=guestWorld.ComputeChecksum();gh.Add(h);guest.ReportChecksum(guestWorld.State.Tick,h);if(gb<0&&guestWorld.State.Props.B00.Broken!=0)gb=guestWorld.State.Tick;}
                }
                int count=Math.Min(hh.Count,gh.Count);Assert.Greater(count,800);Assert.Greater(hb,0);Assert.AreEqual(hb,gb);
                Assert.AreEqual(SessionState.Running,host.State,host.EndDetail);Assert.AreEqual(SessionState.Running,guest.State,guest.EndDetail);
                for(int i=0;i<count;i++)Assert.AreEqual(hh[i],gh[i],"tick "+i);Assert.Greater(host.ChecksumsCompared,10);
                TestContext.WriteLine("online compared="+count+"; break="+hb+"; dropped="+(la.Dropped+lb.Dropped));
            }
        }
    }
}
