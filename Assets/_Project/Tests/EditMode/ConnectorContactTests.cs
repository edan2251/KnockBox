using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using SessionState = Knockbox.Game.Online.SessionState;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class ConnectorContactTests
    {
        static ArenaDef Trial => AssetDatabase.LoadAssetAtPath<MapDefinition>(
            "Assets/_Project/Data/Maps/Validation/CP3_Rope_Graph_Trial.asset").ToArena();

        public static SimConfig Config(bool stable)
        {
            var old=MapPhysicalBoxReactionTests.CurrentConfig();var combat=old.Combat;
            combat.Props.StableConnectorContacts=stable;
            return new SimConfig(old.Movement,old.Match,combat,old.Rules);
        }
        static SimWorld Rider(int node,float offset,float height,bool stable=true)
        {
            var a=Trial;var c=Config(stable);var old=RopeGraphTests.World(a).State;
            old.Fighter0.Position=a.GetProp(node).Center+new Vec2(offset,a.GetProp(node).HalfSize.Y+c.HalfBody+height);
            old.Fighter0.Grounded=false;old.Fighter0.GroundBox=FighterState.NoBox;
            return new SimWorld(c,a,old);
        }
        [Test,Combinatorial]
        public void SuppliedTrialCornerLandingAndStandingDoNotHurtOrBreak(
            [Values(3,4,5)]int node,[Values(-.5f,-.3f,0f,.3f,.5f)]float offset,[Values(.05f,.5f)]float height)
        {
            var w=Rider(node,offset,height);float peak=0,late=0;int standing=0;
            for(int t=0;t<900;t++)
            {
                w.Step(default,default);
                Assert.AreEqual(100,w.State.Fighter0.Health,"tick "+t);
                Assert.IsFalse(w.State.Props.IsDestroyed(node));
                float spin=Math.Abs(PropState.At(w.State.Props,node).AngularVelocity);peak=Math.Max(peak,spin);
                if(t>600)late=Math.Max(late,spin);
                if(w.State.Fighter0.OnProp==node+1)standing++;
                for(int e=0;e<w.Events.Count;e++)Assert.AreNotEqual(SimEventType.BoxHit,w.Events[e].Type);
            }
            Assert.Greater(standing,0,"must actually touch the node");
            Assert.Less(late,1f,"no persistent high-speed contact oscillation");
            TestContext.WriteLine($"node={node} offset={offset} height={height} peak={peak:F4} late={late:F4} stood={standing}");
        }
        [Test] public void HistoricalContactPathStillReproducesCapturedCornerFault()
        {
            var w=Rider(3,-.5f,.05f,false);int first=-1;float peak=0;
            for(int t=0;t<120;t++)
            {
                w.Step(default,default);peak=Math.Max(peak,Math.Abs(w.State.Props.B03.AngularVelocity));
                if(w.State.Fighter0.Health<100){first=t;break;}
            }
            Assert.AreEqual(23,first);Assert.AreEqual(97,w.State.Fighter0.Health);Assert.Greater(peak,16);
            TestContext.WriteLine($"legacy first damage tick={first}, health={w.State.Fighter0.Health}, spin={peak}");
        }
        [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void StandingJumpAndLandingKeepFullHealth(int node)
        {
            var w=Rider(node,0,.05f);bool jumped=false;int contacts=0;
            for(int t=0;t<600;t++)
            {
                w.Step(new InputFrame(0,0,t==60||t==180?InputButtons.Jump:InputButtons.None,1),default);
                jumped|=t>=60&&w.State.Fighter0.Velocity.Y>1;
                Assert.AreEqual(100,w.State.Fighter0.Health);Assert.IsFalse(w.State.Props.IsDestroyed(node));
                for(int e=0;e<w.Events.Count;e++)if(w.Events[e].Type==SimEventType.ConnectorContact)contacts++;
            }
            Assert.IsTrue(jumped);Assert.Less(contacts,15,"resting contact must not emit a hit every tick");
        }
        static SimWorld ContactWorld(float bodySpeed,float playerSpeed)
        {
            var a=new ArenaDef("Connector_Contact",36,20,false,Array.Empty<ArenaBox>(),new Vec2(-10,-8),new Vec2(10,-8))
                .WithProps(new[]{new PropDef{Kind=PropKind.LooseBox,Id="Connector",Center=Vec2.Zero,HalfSize=new Vec2(.325f,.325f),
                    Mass=.65f,StartAwake=true,IsConnector=true,ConnectorSpeedLoss=.15f}});
            var c=Config(true);var old=RopeGraphTests.World(a).State;old.Fighter0.Position=new Vec2(.65f,0);
            old.Fighter0.Velocity=new Vec2(playerSpeed,0);old.Fighter0.Grounded=false;old.Fighter0.GroundBox=FighterState.NoBox;
            old.Props.B00.Velocity=new Vec2(bodySpeed,0);return new SimWorld(c,a,old);
        }
        [Test] public void ActualFastSideImpactStillDamagesAndSlowContactOnlyGivesFeedback()
        {
            var w=ContactWorld(12,0);var p0=w.State.Fighter0;var p1=w.State.Fighter1;
            PropInteraction.Resolve(ref w.State,w.Arena,w.Config,p0,p1,false,false,w.Events,w.PropScratch);
            Assert.Less(w.State.Fighter0.Health,100);Assert.IsTrue(Enumerable.Range(0,w.Events.Count).Any(i=>w.Events[i].Type==SimEventType.BoxHit));
            w=ContactWorld(0,-4);p0=w.State.Fighter0;p1=w.State.Fighter1;
            PropInteraction.Resolve(ref w.State,w.Arena,w.Config,p0,p1,false,false,w.Events,w.PropScratch);
            Assert.AreEqual(100,w.State.Fighter0.Health);var contact=Enumerable.Range(0,w.Events.Count).Select(i=>w.Events[i]).Single(e=>e.Type==SimEventType.ConnectorContact);
            Assert.AreEqual(0,contact.PropIndex);Assert.Greater(contact.Strength,1.5f);Assert.IsFalse(w.State.Props.IsDestroyed(0));
        }
        [Test] public void RestoreRebuildsLoadsAndBothSimulationsAgreeEveryTick()
        {
            var w=Rider(3,.3f,.05f);var sync=new SyncTest<SimWorld>(w);int hp=w.State.Fighter0.Health;
            for(int t=0;t<600;t++)Assert.IsTrue(sync.Advance(new InputFrame(0,0,t==120?InputButtons.Jump:InputButtons.None,1),default),"tick "+t);
            Assert.AreEqual(hp,w.State.Fighter0.Health);
        }
        [TestCase(false)] [TestCase(true)]
        public void OrdinaryMapsKeepExactlyTheSameSimulation(bool boxes)
        {
            var a=boxes?MapPhysicalBoxReactionTests.Lab(4):ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            var before=RopeGraphTests.World(a).State;var old=new SimWorld(Config(false),a,before);var now=new SimWorld(Config(true),a,before);
            for(int t=0;t<600;t++)
            {
                var input=new InputFrame(t<250?1:0,0,t>=80&&t<110?InputButtons.Attack:InputButtons.None,1);
                old.Step(input,default);now.Step(input,default);Assert.AreEqual(old.ComputeChecksum(),now.ComputeChecksum(),"tick "+t);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void OnlineSupportLoadsAgreeAndDifferentRulesReject(bool mismatch)
        {
            var h=Rider(3,-.3f,.05f);var g=new SimWorld(Config(!mismatch),h.Arena,h.State);
            MemoryTransport.Pair(out var a,out var b);double time=0;
            var la=new SimulatedLink(a,()=>time,.04,.015,.1,.05,33);var lb=new SimulatedLink(b,()=>time,.04,.015,.1,.05,44);
            uint map=OnlineMapManifest.Compute(new[]{h.Arena});
            using(var host=new LockstepSession(la,true,h.Config.ComputeChecksum(),map,77,6))
            using(var guest=new LockstepSession(lb,false,g.Config.ComputeChecksum(),map,0,0))
            {
                for(int t=0;t<240&&(host.State!=SessionState.Running||guest.State!=SessionState.Running);t++)
                {time+=1.0/60;host.Poll(time);guest.Poll(time);}
                if(mismatch){Assert.IsTrue(host.Reason==EndReason.Rejected||guest.Reason==EndReason.Rejected);return;}
                Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
                host.Begin(0,new IdleInputSource());guest.Begin(0,new IdleInputSource());var hh=new List<uint>();var gh=new List<uint>();
                for(int t=0;t<900;t++)
                {
                    time+=1.0/60;host.Poll(time);guest.Poll(time);
                    for(int k=0;k<2&&host.TryGetInputs(h.State.Tick,out var p0,out var p1);k++)
                    {h.Step(p0,p1);hh.Add(h.ComputeChecksum());host.ReportChecksum(h.State.Tick,h.ComputeChecksum());}
                    for(int k=0;k<2&&guest.TryGetInputs(g.State.Tick,out var p0,out var p1);k++)
                    {g.Step(p0,p1);gh.Add(g.ComputeChecksum());guest.ReportChecksum(g.State.Tick,g.ComputeChecksum());}
                }
                int compared=Math.Min(hh.Count,gh.Count);Assert.Greater(compared,600);
                for(int t=0;t<compared;t++)Assert.AreEqual(hh[t],gh[t],"tick "+t);
                Assert.AreEqual(100,h.State.Fighter0.Health);Assert.AreEqual(100,g.State.Fighter0.Health);
                Assert.AreEqual(SessionState.Running,host.State);Assert.AreEqual(SessionState.Running,guest.State);
                Assert.Greater(la.Dropped+lb.Dropped,0);TestContext.WriteLine($"support online ticks={compared}, dropped={la.Dropped+lb.Dropped}");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void SavedInputSelectsItsExactConnectorRulesAndReplays(bool stable)
        {
            var go=new GameObject("Connector_Replay_Config");go.SetActive(false);
            var runner=go.AddComponent<MatchRunner>();var source=AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset");
            var tuning=UnityEngine.Object.Instantiate(source);
            try
            {
                runner.Tuning=AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
                runner.CombatTuning=tuning;runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;tuning.StableConnectorContacts=stable;
                var c=runner.PreviewConfig(false);Assert.AreEqual(stable,c.Combat.Props.StableConnectorContacts);
                var a=Trial;var spawn=a.GetProp(3).Center+new Vec2(-.5f,a.GetProp(3).HalfSize.Y+c.HalfBody+.05f);
                var rec=InputRecording.Begin(c,a,77,spawn,a.SpawnFoot1,MatchPhase.Fighting,c.Rules.Format());
                var w=new SimWorld(c,a,rec.CreateInitialState(c,a));var hashes=new List<uint>();
                for(int t=0;t<300;t++){rec.Append(default,default);w.Step(default,default);hashes.Add(w.ComputeChecksum());}
                rec.Finish(w.ComputeChecksum());var loaded=InputRecording.FromBytes(rec.ToBytes());tuning.StableConnectorContacts=!stable;
                var selected=runner.FindRecordedConfig(loaded);Assert.NotNull(selected);Assert.AreEqual(stable,selected.Combat.Props.StableConnectorContacts);
                var replay=new SimWorld(selected,a,loaded.CreateInitialState(selected,a));
                for(int t=0;t<300;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"tick "+t);}
                Assert.AreNotEqual(c.ComputeChecksum(),runner.PreviewConfig(false).ComputeChecksum());
            }
            finally {UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(tuning);}
        }
    }
}
