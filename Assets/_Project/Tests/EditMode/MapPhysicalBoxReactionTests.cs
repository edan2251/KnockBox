using System;
using System.Linq;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    public class MapPhysicalBoxReactionTests
    {
        public static SimConfig CurrentConfig()
        {
            var movement=AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
            var combat=AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset");
            return new SimConfig(BrawlParams.Movement(movement.Movement),BrawlParams.Match(movement.Match),
                BrawlParams.Combat(combat.Combat,0,true,false,true,false,true),RuleSet.Brawl);
        }
        public static ArenaDef Lab(int count)
        {
            var defs=new PropDef[count];
            for(int i=0;i<count;i++)defs[i]=new PropDef{Kind=PropKind.LooseBox,Center=new Vec2(0,-8f+.6f+1.2f*i),HalfSize=new Vec2(.6f,.6f),AnchorProp=-1};
            return new ArenaDef("CP2_Lab",36,20,false,new[]{new ArenaBox(TerrainKind.Solid,-18,-10,18,-8)},new Vec2(-12,-8),new Vec2(12,-8)).WithProps(defs);
        }
        public static SimWorld Settled(ArenaDef arena)
        {
            var c=CurrentConfig();
            var w=new SimWorld(c,arena,SimState.CreateRound(c,arena,77,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.Fighting));
            for(int i=0;i<w.State.Props.Count;i++)PropState.Get(ref w.State.Props,i).SleepTicks=0;
            for(int t=0;t<120;t++)w.Step(default,default);
            return w;
        }
        public static void Strike(SimWorld w,float speed)
        {
            if(speed==0)return;
            w.State.Fighter0.Position=new Vec2(-.97f,-7.4f);
            w.State.Fighter0.Velocity=new Vec2(speed,0);
            w.State.Fighter0.SetAction(ActionState.Lunge);w.State.Fighter0.Grounded=false;
        }
        public sealed class Observation
        {
            public float[] dx,tilt,early;
            public int loose,knockouts;
            public float drop;
            public uint hash;
            public string Description=>"dx="+string.Join(",",dx.Select(x=>x.ToString("F5")))+"; early="+string.Join(",",early.Select(x=>x.ToString("F5")))
                +"; sin="+string.Join(",",tilt.Select(x=>x.ToString("F5")))+"; drop="+drop.ToString("F5")+"; loose="+loose+"; knockout="+knockouts+"; hash="+hash.ToString("X8");
        }
        public static Observation Observe(SimWorld w,float speed)
        {
            int n=w.State.Props.Count;var initial=w.State;Strike(w,speed);
            var r=new Observation{dx=new float[n],tilt=new float[n],early=new float[n]};var hash=Hasher.Create();
            for(int t=0;t<360;t++)
            {
                w.Step(default,default);hash.AddUInt(w.ComputeChecksum());
                for(int i=0;i<n;i++)
                {
                    ref readonly var b=ref PropState.At(w.State.Props,i);
                    r.dx[i]=Math.Max(r.dx[i],Math.Abs(b.Position.X));r.tilt[i]=Math.Max(r.tilt[i],Math.Abs(b.Rotation.Y));r.loose=Math.Max(r.loose,b.LooseTicks);
                    Assert.IsFalse(float.IsNaN(b.Position.X+b.Position.Y+b.AngularVelocity));
                }
                for(int e=0;e<w.Events.Count;e++)if(w.Events[e].Type==SimEventType.BoxKnockout)r.knockouts++;
                if(t==29)Array.Copy(r.dx,r.early,n);
            }
            r.drop=PropState.At(initial.Props,n-1).Position.Y-PropState.At(w.State.Props,n-1).Position.Y;r.hash=hash.Hash;return r;
        }
        [TestCase(1,0f,"10F79F20")] [TestCase(1,18f,"95FF0962")] [TestCase(1,34f,"306CF66E")]
        [TestCase(4,0f,"1A5B06AD")] [TestCase(4,18f,"5DE1C562")] [TestCase(4,34f,"EB5A3F3C")]
        public void BaselineBoxReaction(int count,float speed,string expected)
        {
            var w=Settled(Lab(count));var result=Observe(w,speed);
            TestContext.WriteLine($"[CP2] count={count}; speed={speed}; {result.Description}");
            // Captured before CP2 simulation changes; never regenerate to hide a regression.
            Assert.AreEqual(expected,result.hash.ToString("X8"));
            Assert.AreEqual(.7f,w.Config.Combat.Props.BoxFriction);Assert.AreEqual(20,w.Config.Combat.Props.LooseTicks);
            if(speed==0){Assert.Less(result.dx.Max(),.02f);Assert.Less(result.tilt.Max(),.01f);}
            if(speed==18){Assert.Zero(result.knockouts);Assert.Zero(result.loose);Assert.Greater(result.dx[0],.1f);
                if(count==4){Assert.Greater(result.early[1],.03f);Assert.Greater(result.early[2],.03f);Assert.Less(result.drop,.12f);}}
            if(speed==34){Assert.Greater(result.knockouts,0);Assert.AreEqual(20,result.loose);Assert.Greater(result.dx[0],2f);
                if(count==4){Assert.Greater(result.drop,.9f);Assert.Greater(result.tilt.Skip(1).Max(),.05f);}}
        }
    }
}