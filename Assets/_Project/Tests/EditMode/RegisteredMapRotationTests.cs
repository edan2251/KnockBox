using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class RegisteredMapRotationTests
    {
        readonly List<Object> owned=new List<Object>();
        T Own<T>(T value) where T:Object { owned.Add(value);return value; }
        [TearDown] public void TearDown()
        {
            foreach(var go in owned.OfType<GameObject>())if(go!=null)go.GetComponent<MatchRunner>()?.StopRecording();
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
        static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        static ArenaDef Geometry(string name="test")=>new ArenaDef(name,12,8,false,new[]{
            new ArenaBox(TerrainKind.Solid,-6,-4,6,-3),
            new ArenaBox(TerrainKind.Solid,-3,1,-2,2)},new Vec2(-.65f,-3),new Vec2(.65f,-3));
        MapDefinition Definition(string id)
        {
            var d=Own(ScriptableObject.CreateInstance<MapDefinition>());
            d.Import(Geometry(),ArenaMeta.Default,id,id);return d;
        }
        MapCatalog Catalog(params MapDefinition[] maps)
        {
            var c=Own(ScriptableObject.CreateInstance<MapCatalog>());
            foreach(var m in maps)c.Register(m);return c;
        }
        static SimConfig QuickConfig(bool online=false)
        {
            var movement=BrawlParams.Movement(MovementParams.Default);
            var combat=BrawlParams.Combat(CombatParams.Default);combat.MaxHealth=1;
            var match=BrawlParams.Match(MatchParams.Default);match.CountdownTicks=1;match.IntroCountdownTicks=0;
            match.KoTicks=2;match.RoundsToWin=10000;match.RematchBothHold=online;
            return new SimConfig(movement,match,combat,RuleSet.Brawl);
        }
        static InputFrame Punch(int tick)=>new InputFrame(0,0,tick%24==0?InputButtons.Attack:InputButtons.None,1);
        sealed class PunchInput:IInputSource { public InputFrame Poll(int tick)=>Punch(tick); }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(8)]
        public void RegisteredPool_IsOrdinalAndNeverRepeatsAcrossShuffleCycles(int count)
        {
            var definitions=Enumerable.Range(0,count).Select(i=>Definition("map_"+i)).ToArray();
            var forward=Catalog(definitions).CreateRotation();var backward=Catalog(definitions.Reverse().ToArray()).CreateRotation();
            CollectionAssert.AreEqual(forward.Select(a=>a.Name),backward.Select(a=>a.Name));
            Assert.AreEqual(OnlineMapManifest.Compute(forward),OnlineMapManifest.Compute(backward));
            for(uint seed=1;seed<=40;seed++)
            {
                var a=ArenaRotation.Create(seed,count);var b=ArenaRotation.Create(seed,count);int previous=-1;
                for(int round=0;round<count*8+10;round++)
                {
                    Assert.AreEqual(a.Current,b.Current);
                    if(count>1)Assert.AreNotEqual(previous,a.Current,"직전 라운드 맵은 제외");
                    else Assert.Zero(a.Current,"한 맵이면 같은 맵 유지");
                    previous=a.Current;a.Advance();b.Advance();
                }
            }
        }

        [Test]
        public void CatalogValidationRejectsEmptyDuplicateAndOverflow_WithoutTruncation()
        {
            var c=Catalog();Assert.Throws<InvalidOperationException>(()=>c.CreateRotation());
            var one=Definition("one");c.Register(one);c.Register(one);Assert.AreEqual(1,c.Maps.Count);
            Assert.Throws<InvalidOperationException>(()=>c.Register(Definition("one")));
            Assert.Throws<InvalidOperationException>(()=>c.Register(Definition("invalid,id")));
            for(int i=1;i<ArenaRotation.MaxArenas;i++)c.Register(Definition("map_"+i));
            Assert.Throws<InvalidOperationException>(()=>c.Register(Definition("ninth")));
            Assert.AreEqual(ArenaRotation.MaxArenas,c.CreateRotation().Length);
            ((List<MapDefinition>)c.Maps).Add(Definition("ninth"));
            Assert.Throws<InvalidOperationException>(()=>c.CreateRotation(),"Inspector 우회 입력도 거절");
        }

        [Test]
        public void OnlineManifestIncludesOrderedIdentityAndFullGeometry_LeavesReplayHashUntouched()
        {
            var a=Catalog(Definition("a"),Definition("b")).CreateRotation();
            uint legacy=InputRecording.ComputeArenaChecksum(a),manifest=OnlineMapManifest.Compute(a);
            var reversed=a.Reverse().ToArray();
            Assert.AreEqual(legacy,InputRecording.ComputeArenaChecksum(reversed),"같은 지형/다른 ID의 과거 해시 규칙 보존");
            Assert.AreNotEqual(manifest,OnlineMapManifest.Compute(reversed));
            var replacement=Catalog(Definition("a"),Definition("c")).CreateRotation();
            Assert.AreNotEqual(manifest,OnlineMapManifest.Compute(replacement));
            var p=new PropDef{Kind=PropKind.LooseBox,Center=new Vec2(0,2),HalfSize=new Vec2(.5f,.5f),AnchorProp=-1};
            var q=p;q.Center.X+=.25f;q.HalfSize.Y+=.125f;
            var first=new[]{a[0].WithProps(new[]{p},3)};var second=new[]{a[0].WithProps(new[]{q},3)};
            Assert.AreEqual(InputRecording.ComputeArenaChecksum(first),InputRecording.ComputeArenaChecksum(second));
            Assert.AreNotEqual(OnlineMapManifest.Compute(first),OnlineMapManifest.Compute(second));
            Assert.AreEqual(legacy,InputRecording.ComputeArenaChecksum(a));
            Assert.AreEqual(8,InputRecording.FormatVersion);Assert.AreEqual(9,LockstepSession.ProtocolVersion);
        }

        [TestCase(false)] [TestCase(true)]
        public void DifferentCatalogIdentityOrGeometryIsRejectedBeforeOnlinePlay(bool geometryChanged)
        {
            var maps=Catalog(Definition("a"),Definition("b")).CreateRotation();
            var changed=Definition(geometryChanged?"b":"c");
            if(geometryChanged)
            {
                var boxes=Enumerable.Range(0,changed.BoxCount).Select(changed.Box).ToArray();
                boxes[1].minX+=.125f;boxes[1].maxX+=.125f;changed.SaveGeometry(boxes,changed.Spawn(0),changed.Spawn(1));
            }
            var other=Catalog(Definition("a"),changed).CreateRotation();
            MemoryTransport.Pair(out var ta,out var tb);
            using var host=new LockstepSession(ta,true,1,OnlineMapManifest.Compute(maps),77,3);
            using var guest=new LockstepSession(tb,false,1,OnlineMapManifest.Compute(other),0,0);
            for(int i=0;i<8;i++){guest.Poll(i*.1);host.Poll(i*.1);}
            Assert.AreEqual(Knockbox.Game.Online.SessionState.WaitingPeer,host.State);
            Assert.AreEqual(EndReason.Rejected,guest.Reason);Assert.IsFalse(guest.Began);
        }

        [TestCase(0)] [TestCase(1)]
        public void SetPickKeepsCurrentMap_ThenAdvancesOnceAndPreservesAward(int loser)
        {
            var arenas=Catalog(Definition("a"),Definition("b"),Definition("c")).CreateRotation();
            var config=CardCollectionRules.Apply(QuickConfig(),false);
            var world=new SimWorld(config,arenas,SimState.CreateMatch(config,arenas,97,MatchPhase.Countdown));
            // Enter the same post-KO state as the second win of a set.
            world.State.Match.SetPhase(MatchPhase.RoundEnd);world.State.Match.RoundWinner=1-loser;
            if(loser==0)world.State.Match.Score1=config.Match.RoundsToWinSet;
            else world.State.Match.Score0=config.Match.RoundsToWinSet;
            var before=world.State.Match.Rotation;string map=world.Arena.Name;
            world.Step(default,default);Assert.AreEqual(MatchPhase.SetPick,world.State.Match.Phase);
            Assert.AreEqual(loser,world.State.Match.Picker);
            for(int i=0;i<5;i++){world.Step(default,default);Assert.AreEqual(map,world.Arena.Name);}
            var pick=new InputFrame(0,0,InputButtons.Jump);
            world.Step(loser==0?pick:default,loser==1?pick:default);
            Assert.AreEqual(1,world.State.GetFighter(loser).Augments.Count);
            for(int i=0;i<config.Match.PickHoldTicks+2 && world.State.Match.Phase==MatchPhase.SetPick;i++)
            {world.Step(default,default);Assert.AreEqual(map,world.Arena.Name);Assert.AreEqual(before,world.State.Match.Rotation);}
            Assert.AreEqual(MatchPhase.RoundTransition,world.State.Match.Phase);
            var next=before;next.Advance();world.Step(default,default);
            Assert.AreEqual(MatchPhase.Countdown,world.State.Match.Phase);Assert.AreEqual(next,world.State.Match.Rotation);
            Assert.AreNotEqual(map,world.Arena.Name);Assert.AreEqual(arenas[next.Current].Name,world.Arena.Name);
            Assert.AreEqual(1,world.State.Match.GetSetScore(1-loser));
            Assert.AreEqual(CardCollectionRules.FirstCardId,world.State.GetFighter(loser).Augments.Get(0));
            for(int i=0;i<5;i++){world.Step(default,default);Assert.AreEqual(next,world.State.Match.Rotation);}
        }

        [Test]
        public void RegisteredRoundChanges_SaveRestoreAndV8RecordingStayIdentical()
        {
            var arenas=Catalog(Definition("c"),Definition("a"),Definition("b")).CreateRotation();
            var config=QuickConfig();var initial=SimState.CreateMatch(config,arenas,77,MatchPhase.Countdown);
            var world=new SimWorld(config,arenas,initial);var sync=new SyncTest<SimWorld>(world);
            var recording=InputRecording.Begin(config,arenas,initial.Match.Rotation,initial.Rng.State,
                initial.Fighter0.Position,initial.Fighter1.Position,initial.Match.Phase,config.Rules.Format());
            var checksums=new List<uint>();int round=world.State.Match.Round;int transitions=0;string last=world.Arena.Name;
            for(int t=0;t<1200;t++)
            {
                var a=Punch(t);recording.Append(a,default);
                if(!sync.Advance(a,default))Assert.Fail(sync.LastMismatch.ToString());
                checksums.Add(world.ComputeChecksum());
                if(world.State.Match.Round!=round)
                {
                    Assert.AreNotEqual(last,world.Arena.Name);last=world.Arena.Name;round=world.State.Match.Round;transitions++;
                    Assert.AreEqual(SimState.SpawnCenter(config,world.Arena,0),world.State.Fighter0.Position);
                }
            }
            Assert.Greater(transitions,3,"실제 공격 입력으로 여러 라운드 교체를 지나야 함");
            recording.Finish(world.ComputeChecksum());var loaded=InputRecording.FromBytes(recording.ToBytes());
            CollectionAssert.AreEqual(arenas.Select(a=>a.Name),loaded.ArenaNames);
            var replay=new SimWorld(config,arenas,loaded.CreateInitialState(config,arenas));
            for(int i=0;i<loaded.Count;i++){replay.Step(loaded.GetP1(i),loaded.GetP2(i));Assert.AreEqual(checksums[i],replay.ComputeChecksum(),"재생 틱 "+i);}
            Assert.AreEqual(loaded.FinalChecksum,replay.ComputeChecksum());
        }

        [Test]
        public void OppositeRegistrationOrder_OnlineInputsAgreeThroughRoundChanges()
        {
            var definitions=new[]{Definition("c"),Definition("a"),Definition("b")};
            var arenas0=Catalog(definitions).CreateRotation();var arenas1=Catalog(definitions.Reverse().ToArray()).CreateRotation();
            var config=QuickConfig(true);MemoryTransport.Pair(out var ta,out var tb);
            using var host=new LockstepSession(ta,true,config.ComputeChecksum(),OnlineMapManifest.Compute(arenas0),73,3);
            using var guest=new LockstepSession(tb,false,config.ComputeChecksum(),OnlineMapManifest.Compute(arenas1),0,0);
            double now=0;for(int i=0;i<20;i++){now+=.05;guest.Poll(now);host.Poll(now);}
            Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,host.State);Assert.AreEqual(host.Seed,guest.Seed);
            var a=new SimWorld(config,arenas0,SimState.CreateMatch(config,arenas0,host.Seed,MatchPhase.Countdown));
            var b=new SimWorld(config,arenas1,SimState.CreateMatch(config,arenas1,guest.Seed,MatchPhase.Countdown));
            host.Begin(0,new PunchInput());guest.Begin(0,new IdleInputSource());
            var first=new List<uint>();var second=new List<uint>();var maps0=new List<string>();var maps1=new List<string>();
            for(int frame=0;frame<1500;frame++)
            {
                now+=1.0/60;host.Poll(now);guest.Poll(now);
                if(host.TryGetInputs(a.State.Tick,out var p0,out var p1)){a.Step(p0,p1);first.Add(a.ComputeChecksum());maps0.Add(a.Arena.Name);host.ReportChecksum(a.State.Tick,a.ComputeChecksum());}
                if(guest.TryGetInputs(b.State.Tick,out p0,out p1)){b.Step(p0,p1);second.Add(b.ComputeChecksum());maps1.Add(b.Arena.Name);guest.ReportChecksum(b.State.Tick,b.ComputeChecksum());}
            }
            Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,host.State,host.EndDetail);Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,guest.State,guest.EndDetail);
            int common=Math.Min(first.Count,second.Count);Assert.Greater(common,1000);Assert.Greater(a.State.Match.Round,4);
            for(int i=0;i<common;i++){Assert.AreEqual(first[i],second[i],"온라인 틱 "+i);Assert.AreEqual(maps0[i],maps1[i]);}
        }

        [Test]
        public void InvalidCatalogSessionAttemptRestoresPracticeFlags()
        {
            var go=Own(new GameObject("Invalid_Online_Test"));go.SetActive(false);
            var runner=go.AddComponent<MatchRunner>();Set(runner,"mapCatalog",Catalog());
            runner.ArenaName=MatchRunner.RegisteredMapsName;runner.Running=true;runner.RandomizeSeed=true;runner.OnlineRematch=false;
            var hotkeys=go.AddComponent<DebugHotkeys>();hotkeys.enabled=true;
            var online=go.AddComponent<OnlineMatch>();Set(online,"runner",runner);Set(online,"hotkeys",hotkeys);
            MemoryTransport.Pair(out var local,out var peer);
            try{Assert.Throws<InvalidOperationException>(()=>online.StartSession(local,true,3));}
            finally{peer.Dispose();}
            Assert.IsTrue(runner.Running);Assert.IsTrue(runner.RandomizeSeed);Assert.IsFalse(runner.OnlineRematch);Assert.IsTrue(hotkeys.enabled);
            Assert.IsFalse(online.Active);
        }

        [Test]
        public void OnlineStartUsesHandshakeSnapshot_AfterCatalogAndTuningEdits()
        {
            var d=Definition("first");var catalog=Catalog(d,Definition("second"));
            var go=Own(new GameObject("Frozen_Online_Test"));go.SetActive(false);
            var runner=go.AddComponent<MatchRunner>();runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;
            runner.ArenaName=MatchRunner.RegisteredMapsName;Set(runner,"mapCatalog",catalog);
            var tuning=Own(ScriptableObject.CreateInstance<MovementTuning>());runner.Tuning=tuning;
            var online=go.AddComponent<OnlineMatch>();Set(online,"runner",runner);
            MemoryTransport.Pair(out var local,out var peer);
            online.StartSession(local,true,3);
            var frozenConfig=(SimConfig)typeof(OnlineMatch).GetField("preparedConfig",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(online);
            var frozenMaps=(ArenaDef[])typeof(OnlineMatch).GetField("preparedArenas",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(online);
            uint hash=OnlineMapManifest.Compute(frozenMaps),configHash=frozenConfig.ComputeChecksum();
            var boxes=Enumerable.Range(0,d.BoxCount).Select(d.Box).ToArray();boxes[1].minX+=.125f;boxes[1].maxX+=.125f;d.SaveGeometry(boxes,d.Spawn(0),d.Spawn(1));
            tuning.Movement.BodySize+=.125f;Set(runner,"skipCountdown",true);
            using var guest=new LockstepSession(peer,false,configHash,hash,0,0);
            for(int i=0;i<15;i++){guest.Poll(i*.05);online.Session.Poll(i*.05);}
            Assert.AreEqual(Knockbox.Game.Online.SessionState.Running,online.Session.State);
            typeof(OnlineMatch).GetMethod("BeginMatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(online,null);
            Assert.AreEqual(configHash,runner.World.Config.ComputeChecksum());Assert.AreEqual(2,runner.World.ArenaCount);
            Assert.AreSame(frozenMaps[0],runner.World.GetArena(0));Assert.AreEqual(MatchPhase.Countdown,runner.World.State.Match.Phase);
            Assert.AreNotEqual(hash,OnlineMapManifest.Compute(catalog.CreateRotation()));
            runner.StopRecording();
        }
    }
}
