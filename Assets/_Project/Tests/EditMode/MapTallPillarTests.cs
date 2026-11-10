using System;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapTallPillarTests
    {
        public static MapPhysicalBox[] Layout(bool adjacent, float neighborX=-4.5f, bool passThrough=false)
        {
            var boxes=new MapPhysicalBox[adjacent?5:4];float bottom=-8f;
            for(int i=0;i<4;i++)
            {
                // Keep the original 1x4 solid fixture unchanged for legacy replay/physics regression.
                bool pillar=(i&1)!=0;
                var size=pillar?new Vector2(passThrough?1.5f:1f,4):new Vector2(1.5f,1.5f);
                boxes[i]=new MapPhysicalBox{id="cp21_body_"+i,center=new Vector2(0,bottom+size.y*.5f),size=size,mass=pillar?4f:2.25f,playerPassThrough=pillar&&passThrough};
                bottom+=size.y;
            }
            if(adjacent)
            {
                var size=new Vector2(passThrough?1.5f:1f,4);
                boxes[4]=new MapPhysicalBox{id="cp21_neighbor",center=new Vector2(neighborX,-8+size.y*.5f),size=size,mass=4,playerPassThrough=passThrough};
            }
            return boxes;
        }
        public static MapDefinition Definition(bool adjacent,float neighborX=-4.5f,bool passThrough=false)
        {
            var d=ScriptableObject.CreateInstance<MapDefinition>();
            d.Import(MapPhysicalBoxAuthoringTests.Floor(),new ArenaMeta(),adjacent?"cp21_neighbor_review":"cp21_single_review",adjacent?"긴기둥_인접붕괴":"긴기둥_단독적층");
            d.SaveLayout(new[]{d.Box(0)},Layout(adjacent,neighborX,passThrough),new Vector2(-1.5f,-8),d.Spawn(1));
            return d;
        }
        public static SimWorld World(bool adjacent,float neighborX=-4.5f,bool passThrough=false)
        {
            var definition=Definition(adjacent,neighborX,passThrough);ArenaDef arena;
            try{arena=definition.ToArena();}finally{UnityEngine.Object.DestroyImmediate(definition);}
            var c=MapBoxReleaseTests.Config(true);
            return new SimWorld(c,arena,SimState.CreateRound(c,arena,77,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.Fighting));
        }
        public static void HitBase(SimWorld w,float speed)
        {
            if(speed<=0)return;
            w.State.Fighter0.Position=new Vec2(-.75f-w.Config.HalfBody-.055f,-7.25f);
            w.State.Fighter0.Velocity=new Vec2(speed,0);w.State.Fighter0.SetAction(ActionState.Lunge);w.State.Fighter0.Grounded=false;
        }
        public sealed class Observation
        {
            public float[] dx,tilt,early,lastX,lastY;
            public float settleDrift,settleTilt,settleSag,topDrop,maxPenetration,maxNeighborImpulse,neighborMotionBeforeContact;
            public int firstNeighborTick=-1,firstNeighborBody=-1,firstLongNeighborTick=-1,loose,awakeAtEnd;
            public float maxLongNeighborImpulse;
            public uint hash;
            public string Description=>"settle drift="+settleDrift.ToString("F5")+", tilt="+settleTilt.ToString("F5")+", sag="+settleSag.ToString("F5")
                +"; dx="+string.Join(",",dx.Select(x=>x.ToString("F4")))+"; sin="+string.Join(",",tilt.Select(x=>x.ToString("F4")))
                +"; early="+string.Join(",",early.Select(x=>x.ToString("F4")))+"; finalX="+string.Join(",",lastX.Select(x=>x.ToString("F4")))
                +"; finalY="+string.Join(",",lastY.Select(x=>x.ToString("F4")))+"; topDrop="+topDrop.ToString("F4")+"; penetration="+maxPenetration.ToString("F4")
                +"; neighbor first="+firstNeighborTick+"/body="+firstNeighborBody+"/impulse="+maxNeighborImpulse.ToString("F4")+"/before="+neighborMotionBeforeContact.ToString("F5")
                +"; long contact="+firstLongNeighborTick+"/impulse="+maxLongNeighborImpulse.ToString("F4")+"; loose="+loose+"; awakeEnd="+awakeAtEnd+"; hash="+hash.ToString("X8");
        }
        public static Observation Observe(bool adjacent,float speed,float neighborX=-4.5f,bool passThrough=false)
        {
            var w=World(adjacent,neighborX,passThrough);int count=w.State.Props.Count;
            var r=new Observation{dx=new float[count],tilt=new float[count],early=new float[count],lastX=new float[count],lastY=new float[count]};
            for(int t=0;t<240;t++)
            {
                w.Step(default,default);
                for(int i=0;i<count;i++)
                {
                    ref readonly var b=ref PropState.At(w.State.Props,i);var def=w.Arena.GetProp(i);
                    r.settleDrift=Math.Max(r.settleDrift,Math.Abs(b.Position.X-def.Center.X));
                    r.settleTilt=Math.Max(r.settleTilt,Math.Abs(b.Rotation.Y));
                    r.settleSag=Math.Max(r.settleSag,Math.Abs(b.Position.Y-def.Center.Y));
                }
            }
            var initial=w.State;HitBase(w,speed);var hash=Hasher.Create();
            for(int t=0;t<600;t++)
            {
                w.Step(default,default);hash.AddUInt(w.ComputeChecksum());
                for(int j=0;j<w.PropScratch.ContactCount;j++)
                {
                    var contact=w.PropScratch.Contacts[j];r.maxPenetration=Math.Max(r.maxPenetration,-contact.Separation);
                    int other=contact.Body==4?contact.Other:contact.Other==4?contact.Body:-1;
                    if(adjacent&&other>=0&&contact.NormalImpulse>1e-5f)
                    {
                        if(r.firstNeighborTick<0){r.firstNeighborTick=t;r.firstNeighborBody=other;}
                        r.maxNeighborImpulse=Math.Max(r.maxNeighborImpulse,contact.NormalImpulse);
                        if(other==1||other==3)
                        {
                            if(r.firstLongNeighborTick<0)r.firstLongNeighborTick=t;
                            r.maxLongNeighborImpulse=Math.Max(r.maxLongNeighborImpulse,contact.NormalImpulse);
                        }
                    }
                }
                for(int i=0;i<count;i++)
                {
                    ref readonly var b=ref PropState.At(w.State.Props,i);
                    float dx=Math.Abs(b.Position.X-PropState.At(initial.Props,i).Position.X);
                    r.dx[i]=Math.Max(r.dx[i],dx);r.tilt[i]=Math.Max(r.tilt[i],Math.Abs(b.Rotation.Y));r.loose=Math.Max(r.loose,b.LooseTicks);
                    if(adjacent&&i==4&&r.firstNeighborTick<0)r.neighborMotionBeforeContact=Math.Max(r.neighborMotionBeforeContact,Math.Max(dx,Math.Abs(b.Rotation.Y)));
                    if(float.IsNaN(b.Position.X+b.Position.Y+b.AngularVelocity)||float.IsInfinity(b.Position.X+b.Position.Y+b.AngularVelocity))throw new Exception("non-finite body "+i);
                    r.lastX[i]=b.Position.X;r.lastY[i]=b.Position.Y;
                }
                if(t==29)Array.Copy(r.dx,r.early,count);
            }
            r.topDrop=PropState.At(initial.Props,3).Position.Y-PropState.At(w.State.Props,3).Position.Y;
            for(int i=0;i<count;i++)if(!PropPhysics.Asleep(PropState.At(w.State.Props,i),w.Config.Combat.Props))r.awakeAtEnd++;
            r.hash=hash.Hash;return r;
        }

        [TestCase(false,0f)] [TestCase(true,0f)]
        [TestCase(false,18f)] [TestCase(true,18f)]
        [TestCase(false,38f)] [TestCase(true,38f)]
        public void MixedStack_RestWeakAndFullCharge_UsesExistingBoxPhysics(bool adjacent,float speed)
        {
            var r=Observe(adjacent,speed);TestContext.WriteLine($"adjacent={adjacent}; speed={speed}; {r.Description}");
            Assert.Less(r.settleDrift,.002f);Assert.Less(r.settleTilt,.002f);
            Assert.Less(r.settleSag,.06f,"접촉 완화에 따른 작은 초기 내려앉음만 허용한다.");
            Assert.Less(r.maxPenetration,.06f,"긴 몸체의 관통이나 접촉 폭발이 없어야 한다.");
            Assert.Zero(r.awakeAtEnd);
            if(speed==0){Assert.Less(r.dx.Max(),.002f);Assert.Less(r.tilt.Max(),.002f);Assert.Less(Math.Abs(r.topDrop),.002f);}
            else if(speed==18)
            {
                Assert.Greater(r.early[0],.02f);Assert.Greater(r.early[1],.01f);Assert.Greater(r.early[2],.01f);
                Assert.Less(r.dx.Take(4).Max(),.12f);Assert.Less(Math.Abs(r.topDrop),.02f);Assert.Zero(r.loose);
            }
            else
            {
                Assert.Greater(r.dx[0],.7f);Assert.Greater(r.topDrop,4f);Assert.Greater(r.tilt[1],.9f);Assert.Greater(r.tilt[3],.9f);
                if(adjacent)
                {
                    Assert.GreaterOrEqual(r.firstNeighborTick,0);Assert.GreaterOrEqual(r.firstLongNeighborTick,r.firstNeighborTick);
                    Assert.Greater(r.maxLongNeighborImpulse,.1f);Assert.Less(r.neighborMotionBeforeContact,.002f);
                    Assert.Greater(r.dx[4],1f);Assert.Greater(r.tilt[4],.9f);
                }
            }
            if(adjacent&&speed<38){Assert.AreEqual(-1,r.firstNeighborTick);Assert.Less(r.dx[4],.002f);}
            using(var definition=new DefinitionOwner(Definition(adjacent)))
            {
                var a=definition.Value.ToArena();
                for(int i=0;i<a.DynamicPropCount;i++)Assert.AreEqual(PropJointKind.None,a.GetJoint(i).Kind);
            }
        }

        // The definition is an in-memory fixture; never deletes a user's saved map.
        sealed class DefinitionOwner : IDisposable
        {
            public readonly MapDefinition Value;
            public DefinitionOwner(MapDefinition value){Value=value;}
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Value);
        }

        [Test]
        public void HeavyStackWithSupportRemaining_DoesNotPretendToCollapse_AndDistantColumnDoesNotReact()
        {
            var supported=Observe(false,34);
            Assert.Greater(supported.dx[0],.4f);Assert.Less(supported.dx[0],.7f);Assert.Less(supported.topDrop,.04f);
            var distant=Observe(true,38,10);
            TestContext.WriteLine("remaining support: "+supported.Description+"\ndistant: "+distant.Description);
            Assert.Greater(distant.topDrop,4f);Assert.AreEqual(-1,distant.firstNeighborTick);
            Assert.Less(distant.dx[4],.002f);Assert.Less(distant.tilt[4],.002f);
        }

        [Test]
        public void SeparatedLongBodies_MoveAndRotateIndependently()
        {
            var w=World(false);
            for(int i=0;i<4;i++)
            {
                ref var b=ref PropState.Get(ref w.State.Props,i);
                b.Position=new Vec2(-12+8*i,4);b.Velocity=new Vec2(i-1,0);b.AngularVelocity=i==1?1:-1;
                b.SleepTicks=0;b.Warm=default;
            }
            var other=new SimWorld(w.Config,w.Arena,w.State);PropState.Get(ref other.State.Props,1).Velocity.X=-5;
            for(int t=0;t<15;t++)
            {
                w.Step(default,default);other.Step(default,default);
                Assert.AreEqual(PropState.At(w.State.Props,3),PropState.At(other.State.Props,3));
            }
            Assert.Greater(Math.Abs(PropState.At(w.State.Props,1).Rotation.Y),.1f);
            Assert.Greater(Math.Abs(PropState.At(w.State.Props,3).Rotation.Y),.1f);
        }

        public static string InputTrial(bool adjacent,bool verifyReplay,bool passThrough=false,float neighborX=-4.5f)
        {
            var w=World(adjacent,neighborX,passThrough);
            var rec=InputRecording.Begin(w.Config,new[]{w.Arena},w.State.Match.Rotation,77,
                w.State.Fighter0.Position,w.State.Fighter1.Position,w.State.Match.Phase,w.Config.Rules.Format());
            var sync=new SyncTest<SimWorld>(w);var hashes=new System.Collections.Generic.List<uint>();
            float tilt=0,neighborTilt=0,drop=0,power=0;int longContact=-1,firstCollapse=-1;
            float topStart=PropState.At(w.State.Props,3).Position.Y;
            for(int t=0;t<900;t++)
            {
                // Same real input bits as local practice: rest, charge in place, release toward the base.
                int age=t-240;
                var input=new InputFrame(0,0,age>=0&&age<270&&age%90<36?InputButtons.Attack:InputButtons.None,1);
                rec.Append(input,default);
                if(verifyReplay)Assert.IsTrue(sync.Advance(input,default),"restore tick "+t);else w.Step(input,default);
                hashes.Add(w.ComputeChecksum());
                tilt=Math.Max(tilt,Math.Abs(PropState.At(w.State.Props,1).Rotation.Y));
                // A wider lower pillar can rest against its neighbor; the top pillar must still fall independently.
                if(passThrough)tilt=Math.Max(tilt,Math.Abs(PropState.At(w.State.Props,3).Rotation.Y));
                drop=Math.Max(drop,topStart-PropState.At(w.State.Props,3).Position.Y);
                if(tilt>.9f&&firstCollapse<0)firstCollapse=t;
                if(adjacent)neighborTilt=Math.Max(neighborTilt,Math.Abs(PropState.At(w.State.Props,4).Rotation.Y));
                for(int j=0;j<w.Events.Count;j++)if(w.Events[j].Type==SimEventType.PropStrike)power=Math.Max(power,w.Events[j].Strength);
                for(int j=0;j<w.PropScratch.ContactCount;j++)
                {
                    var c=w.PropScratch.Contacts[j];int source=c.Body==4?c.Other:c.Other==4?c.Body:-1;
                    if(adjacent&&(source==1||source==3)&&c.NormalImpulse>1e-5f&&longContact<0)longContact=t;
                }
            }
            rec.Finish(w.ComputeChecksum());
            if(verifyReplay)
            {
                var loaded=InputRecording.FromBytes(rec.ToBytes());Assert.AreEqual(9,loaded.WrittenVersion);
                var replay=new SimWorld(w.Config,w.Arena,loaded.CreateInitialState(w.Config,w.Arena));
                for(int t=0;t<loaded.Count;t++){replay.Step(loaded.GetP1(t),loaded.GetP2(t));Assert.AreEqual(hashes[t],replay.ComputeChecksum(),"replay tick "+t);}
            }
            string result=$"input: adjacent={adjacent}, strike={power:F4}, tilt={tilt:F4}, drop={drop:F4}, neighborTilt={neighborTilt:F4}, firstCollapse={firstCollapse}, longContact={longContact}, final={w.ComputeChecksum():X8}";
            if(verifyReplay)
            {
                TestContext.WriteLine(result);Assert.Greater(power,30);Assert.Greater(tilt,.9f);Assert.Greater(drop,4f);
                if(adjacent){Assert.Greater(neighborTilt,.9f);Assert.GreaterOrEqual(longContact,0);}
            }
            return result;
        }
        [TestCase(false)] [TestCase(true)]
        public void RealChargeInput_CollapsesSavedLayout_AndReplayRollbackMatchEveryTick(bool adjacent)=>InputTrial(adjacent,true);
    }
}
