using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapIndependentRopeTests
    {
        public static MapDefinition Definition(bool breakable=true,float force=600,float slack=0)
        {
            var d=ScriptableObject.CreateInstance<MapDefinition>();
            d.Import(MapPhysicalBoxAuthoringTests.Floor(),new ArenaMeta(),"cp3_rope_trial","독립_로프_시험");
            var box=new MapPhysicalBox{id="cp3_box",center=new Vector2(0,-6.75f),size=new Vector2(1.5f,1.5f),mass=2.25f};
            var rope=new MapRope{id="cp3_rope",boxId=box.id,anchor=new Vector2(0,-2),localAnchor=new Vector2(0,.75f),
                length=4+slack,breakable=breakable,breakForce=force};
            d.SaveLayout(new[]{d.Box(0)},new[]{box},new[]{rope},new Vector2(-1.5f,-8),d.Spawn(1));return d;
        }
        public static SimWorld World(bool breakable=true,float force=600,float slack=0)
        {
            var d=Definition(breakable,force,slack);ArenaDef a;try{a=d.ToArena();}finally{UnityEngine.Object.DestroyImmediate(d);}
            var c=MapPhysicalBoxReactionTests.CurrentConfig();return new SimWorld(c,a,SimState.CreateRound(c,a,77,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.Fighting));
        }
        static void Away(SimWorld w)
        {w.State.Fighter0.Position=new Vec2(-14,-7.64f);w.State.Fighter1.Position=new Vec2(14,-7.64f);}
        static float Stretch(SimWorld w)
        {var j=w.Arena.GetJoint(0);return SimMath.Sqrt((w.State.Props.B00.ToWorld(j.LocalAnchor)-j.WorldAnchor).LengthSquared)-j.Length;}

        [Test] public void StaticWeightIsSupported_AndSlackExternalImpactDoesNotBreak()
        {
            var w=World();Away(w);float maxPull=0,maxStretch=0;
            for(int t=0;t<600;t++){w.Step(default,default);maxPull=Math.Max(maxPull,w.PropScratch.RopePull[0]/SimConfig.Dt);maxStretch=Math.Max(maxStretch,Stretch(w));Assert.Zero(w.State.Props.B00.Broken);}
            TestContext.WriteLine($"static force={maxPull}; stretch={maxStretch}");
            Assert.Less(maxStretch,.02f);Assert.Less(maxPull,600);Assert.Greater(w.State.Props.B00.Position.Y,-6.8f);
            var slack=World(true,10,4);Away(slack);slack.State.Props.B00.Velocity=new Vec2(20,0);slack.PropScratch.TickImpulse[0]=100000;
            PropPhysics.CheckBreaks(ref slack.State.Props,slack.Arena,slack.PropScratch,0,slack.Events);
            Assert.Zero(slack.State.Props.B00.Broken,"raw body impact is not rope tension");
            slack.Step(default,default);Assert.Zero(slack.State.Props.B00.Broken);Assert.Zero(slack.PropScratch.RopePull[0]);
            Assert.Less(Stretch(slack),-1);
        }
        [TestCase(1f,false)] [TestCase(12f,true)]
        public void WeakAndStrongRadialPullUseTension_ReleasePreservesMomentum(float down,bool shouldBreak)
        {
            var w=World();Away(w);w.State.Props.B00.Velocity=new Vec2(0,-down);w.State.Props.B00.AngularVelocity=.3f;
            var p=w.Arena.GetProp(0);p.Rope=default;var free=new SimWorld(w.Config,w.Arena.WithProps(new[]{p}),w.State);
            w.Step(default,default);free.Step(default,default);
            float force=w.PropScratch.RopePull[0]/SimConfig.Dt;
            TestContext.WriteLine($"down={down}; force={force}; broken={w.State.Props.B00.Broken}; vy={w.State.Props.B00.Velocity.Y}");
            Assert.AreEqual(shouldBreak?1:0,w.State.Props.B00.Broken);
            if(shouldBreak)
            {
                Assert.AreEqual(free.State.Props.B00.Velocity,w.State.Props.B00.Velocity);
                Assert.AreEqual(free.State.Props.B00.AngularVelocity,w.State.Props.B00.AngularVelocity);
                Assert.AreEqual(free.State.Props.B00.Rotation,w.State.Props.B00.Rotation);
                Assert.AreEqual(free.State.Props.B00.Position,w.State.Props.B00.Position);
                Assert.AreEqual(1,Enumerable.Range(0,w.Events.Count).Count(i=>w.Events[i].Type==SimEventType.JointBreak));
            }
            else {Assert.Less(force,600);Assert.Less(Stretch(w),.02f);}
        }
        [Test] public void SlackCatchBreaksOnlyWhenLineBecomesTaut_AndUnbreakableLineSurvives()
        {
            // Raise the body with the same four-unit line: the rope catches above the floor.
            var w=World(true,600);Away(w);w.State.Props.B00.Position.Y+=3;w.State.Props.B00.Velocity=new Vec2(0,-20);
            int broken=-1;for(int t=0;t<90;t++){w.Step(default,default);if(w.State.Props.B00.Broken!=0){broken=t;break;}Assert.LessOrEqual(Stretch(w),.02f);}
            Assert.Greater(broken,0);Assert.GreaterOrEqual(Stretch(w),-.001f);
            var unbreakable=World(false,1);Away(unbreakable);unbreakable.State.Props.B00.Velocity=new Vec2(12,-25);
            for(int t=0;t<240;t++){unbreakable.Step(default,default);Assert.Zero(unbreakable.State.Props.B00.Broken);Assert.Less(Stretch(unbreakable),.03f);}
        }
        [Test] public void SnapshotImmediatelyBeforeAndAfterBreakReproducesSameStateAndEvent()
        {
            var w=World();Away(w);w.State.Props.B00.Velocity=new Vec2(0,-12);
            var before=w.State;w.Step(default,default);var after=w.State;uint hash=w.ComputeChecksum();
            Assert.AreEqual(1,w.State.Props.B00.Broken);var evt=w.Events[0];
            w.State=before;w.Step(default,default);Assert.AreEqual(hash,w.ComputeChecksum());Assert.AreEqual(evt,w.Events[0]);
            w.State=after;var replay=new SimWorld(w.Config,w.Arena,after);
            for(int t=0;t<60;t++){w.Step(default,default);replay.Step(default,default);Assert.AreEqual(w.ComputeChecksum(),replay.ComputeChecksum());}
        }
        public static string InputTrial(float force,bool verify)
        {
            var w=World(true,force);var rec=InputRecording.Begin(w.Config,w.Arena,77,w.State.Fighter0.Position,w.State.Fighter1.Position,MatchPhase.Fighting,w.Config.Rules.Format());
            var sync=new SyncTest<SimWorld>(w);var sums=new List<uint>();int broken=-1;float maxForce=0,spin=0;
            for(int t=0;t<720;t++)
            {
                var input=new InputFrame(0,0,t>=60&&t<420&&(t-60)%90<36?InputButtons.Attack:InputButtons.None,1);
                rec.Append(input,default);if(verify)Assert.IsTrue(sync.Advance(input,default),"restore "+t);else w.Step(input,default);
                sums.Add(w.ComputeChecksum());maxForce=Math.Max(maxForce,w.PropScratch.RopePull[0]/SimConfig.Dt);
                spin=Math.Max(spin,Math.Abs(w.State.Props.B00.AngularVelocity));
                if(w.State.Props.B00.Broken!=0&&broken<0)broken=t;
            }
            rec.Finish(w.ComputeChecksum());
            if(verify)
            {
                Assert.GreaterOrEqual(broken,0);var loaded=InputRecording.FromBytes(rec.ToBytes());Assert.AreEqual(9,loaded.WrittenVersion);
                var replay=new SimWorld(w.Config,w.Arena,loaded.CreateInitialState(w.Config,w.Arena));
                for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(sums[t],replay.ComputeChecksum(),"replay "+t);}
            }
            return $"force setting={force}; peak={maxForce}; broken tick={broken}; angular={spin}; final={w.ComputeChecksum():X8}";
        }
        [Test] public void RealChargeInputBreaksRope_AndReplayRollbackAgree()=>TestContext.WriteLine(InputTrial(MapRopeTools.DefaultBreakForce,true));

        // 2026-11-11 사용자 승인: 폼 전투 제거로 PropStage2Tests 설정이 가위 폼에서 몸 폼이 되어 파이터 상태의 폼 자리 값이 바뀌었다.
        // 상자·로프 물리는 같다(지오메트리 체크섬 동일, Body 설정의 변경 전후 체크섬 동일). 이전 값: D0B329C7/F9A4F4AA, D8DE260E/5AD39E8B.
        [TestCase(0,"680E5F51","4F92C36C","F2346A75")]
        [TestCase(8,"97845734","D8144E61","5A827DED")]
        [TestCase(100000,"680E5F51","4F92C36C","E9F93BF8")]
        public void LegacyRopeStreamMatchesPreCP3Baseline(float force,string stream,string final,string geometry)
        {
            var t=typeof(PropStage2Tests);var flags=BindingFlags.NonPublic|BindingFlags.Static;
            var a=(ArenaDef)t.GetMethod("BreakLab",flags).Invoke(null,new object[]{force});
            var w=(SimWorld)t.GetMethod("World",flags).Invoke(null,new object[]{a});
            w.State.Props.B00.SleepTicks=0;w.State.Props.B00.Velocity=new Vec2(35,0);w.State.Props.B00.AngularVelocity=.5f;w.State.Props.B01.SleepTicks=0;
            var h=Hasher.Create();for(int i=0;i<600;i++){w.Step(default,default);h.AddUInt(w.ComputeChecksum());}
            Assert.AreEqual(stream,h.Hash.ToString("X8"));Assert.AreEqual(final,w.ComputeChecksum().ToString("X8"));Assert.AreEqual(geometry,a.ComputeGeometryChecksum().ToString("X8"));
        }

        [Test] public void EverySavedRopeRuleChangesManifestAndRefusesWrongReplay()
        {
            var d=Definition();try
            {
                var a=d.ToArena();var rec=InputRecording.Begin(MapPhysicalBoxReactionTests.CurrentConfig(),a,77,new Vec2(-1.5f,-7.64f),new Vec2(12,-7.64f),MatchPhase.Fighting,"brawl");
                for(int i=0;i<7;i++)
                {
                    var p=a.GetProp(0);var rope=p.Rope;
                    switch(i){case 0:rope.WorldAnchor.X+=.1f;break;case 1:rope.LocalAnchor.X+=.1f;break;case 2:rope.Length+=1;break;
                        case 3:rope.Breakable=false;break;case 4:rope.BreakForce+=1;break;case 5:rope.Id+="x";break;case 6:rope.Enabled=false;break;}
                    p.Rope=rope;var b=a.WithProps(new[]{p});Assert.AreNotEqual(a.ComputeGeometryChecksum(),b.ComputeGeometryChecksum());
                    Assert.AreNotEqual(OnlineMapManifest.Compute(new[]{a}),OnlineMapManifest.Compute(new[]{b}));
                    Assert.Throws<InvalidOperationException>(()=>rec.CreateInitialState(MapPhysicalBoxReactionTests.CurrentConfig(),b));
                }
            }finally{UnityEngine.Object.DestroyImmediate(d);}
        }
    }
}
