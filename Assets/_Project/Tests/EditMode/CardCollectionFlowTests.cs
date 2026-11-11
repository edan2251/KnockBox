using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SessionState = Knockbox.Game.Online.SessionState;

namespace Knockbox.Tests.EditMode
{
    public class CardCollectionFlowTests
    {
        static ArenaDef Arena => ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
        static SimConfig Config(bool fast = false, bool online = false)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled:true,
                combinedAttackInput:true, throwEnabled:true, revisedBalance:true);
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl);
            var config = CardCollectionRules.Apply(original);
            var match = config.Match;
            match.RematchBothHold=online;
            if(fast) { match.IntroCountdownTicks=6;match.CountdownTicks=6;match.KoTicks=6;match.PickHoldTicks=4;match.MatchEndMinTicks=60;combat.MaxHealth=1; }
            return new SimConfig(config.Movement,match,combat,config.Rules,config.Augments);
        }

        static SimWorld World(SimConfig config=null)
        {
            config ??= Config();var arena=Arena;
            return new SimWorld(config,arena,SimState.CreateRound(config,arena,7u,MatchPhase.Countdown));
        }
        static InputFrame Press(InputButtons b) => new InputFrame(0,0,b);
        static void Fighting(SimWorld w)
        {
            for(int i=0;i<500 && w.State.Match.Phase!=MatchPhase.Fighting;i++)w.Step(default,default);
            Assert.AreEqual(MatchPhase.Fighting,w.State.Match.Phase);
        }
        static void LoseRound(SimWorld w,int loser)
        {
            Fighting(w);
            if(loser!=1)w.State.Fighter0.Health=0;
            if(loser!=0)w.State.Fighter1.Health=0;
            for(int i=0;i<200;i++)
            {
                w.Step(default,default);
                var phase=w.State.Match.Phase;
                if(phase==MatchPhase.RoundTransition||phase==MatchPhase.SetPick||phase==MatchPhase.MatchEnd)return;
            }
            Assert.Fail("KO 후 진행 실패");
        }
        static void LoseSet(SimWorld w,int loser) {LoseRound(w,loser);LoseRound(w,loser);}
        static void Pick(SimWorld w,int index)
        {
            int slot=w.State.Match.Picker;w.Step(default,default);
            var input=Press(MatchFlow.OptionButton(index));
            w.Step(slot==0?input:default,slot==1?input:default);
        }

        [Test]
        public void ApprovedRules_TwoRoundsFiveSets_NoFormSelection()
        {
            var w=World();var c=w.Config;
            Assert.AreEqual(2,c.Match.RoundsToWinSet);Assert.AreEqual(5,c.Match.SetsToWinMatch);
            Assert.IsFalse(c.Rules.LegacyMatch);Assert.IsTrue(c.Match.CollectCardsOnly);
            Assert.AreEqual(MatchPhase.Countdown,w.State.Match.Phase);
            LoseRound(w,1);
            Assert.AreEqual(MatchPhase.RoundTransition,w.State.Match.Phase);
            Assert.AreEqual(1,w.State.Match.Score0);Assert.AreEqual(0,w.State.Match.SetScore0);
            LoseRound(w,-1);
            Assert.AreEqual(1,w.State.Match.Score0);Assert.AreEqual(0,w.State.Match.Picks0+w.State.Match.Picks1);
            LoseRound(w,1);
            Assert.AreEqual(MatchPhase.SetPick,w.State.Match.Phase);
            Assert.AreEqual(1,w.State.Match.SetScore0);Assert.AreEqual(1,w.State.Match.Picker);
            Assert.AreEqual(0,w.State.Match.Score0+w.State.Match.Score1);
            Assert.AreEqual(3,w.State.Match.OptionCount);
        }

        [TestCase(0,0)] [TestCase(0,2)] [TestCase(1,1)] [TestCase(1,2)]
        public void OnlyLoserAcquiresExactlyOne_ThenNextRoundPreservesCard(int loser,int option)
        {
            var w=World();LoseSet(w,loser);int round=w.State.Match.Round;
            var input=Press(MatchFlow.OptionButton(option));
            w.Step(loser==1?input:default,loser==0?input:default);
            Assert.Less(w.State.Match.PickedAt,0,"승자 입력은 무시");
            Pick(w,option);
            Assert.AreEqual(CardCollectionRules.FirstCardId+option,w.State.GetFighter(loser).Augments.Get(0));
            Assert.AreEqual(1,w.State.Match.GetPicks(loser));
            for(int t=0;t<10;t++)w.Step(Press(InputButtons.Jump),Press(InputButtons.Attack));
            Assert.AreEqual(1,w.State.GetFighter(loser).Augments.Count,"연타로 추가 획득 금지");
            Fighting(w);
            Assert.AreEqual(round+1,w.State.Match.Round);
            Assert.AreEqual(1,w.State.GetFighter(loser).Augments.Count);
            Assert.AreEqual(1,loser==1?w.State.Match.SetScore0:w.State.Match.SetScore1);
        }

        [Test]
        public void FiveSets_EndWithoutFifthPick_RematchClearsCollection()
        {
            var w=World(Config(true));
            for(int set=0;set<5;set++)
            {
                LoseSet(w,1);
                if(set<4){Assert.AreEqual(MatchPhase.SetPick,w.State.Match.Phase);Pick(w,0);Fighting(w);}
            }
            Assert.AreEqual(MatchPhase.MatchEnd,w.State.Match.Phase);
            Assert.AreEqual(5,w.State.Match.SetScore0);
            Assert.AreEqual(4,w.State.Fighter1.Augments.Count,"동일 카드도 네 번까지 보유");
            for(int i=0;i<150&&w.State.Match.Phase==MatchPhase.MatchEnd;i++)w.Step(i%2==0?Press(InputButtons.Attack):default,default);
            Assert.AreEqual(MatchPhase.Countdown,w.State.Match.Phase);
            Assert.AreEqual(0,w.State.Fighter0.Augments.Count+w.State.Fighter1.Augments.Count);
            Assert.AreEqual(0,w.State.Match.SetScore0+w.State.Match.SetScore1);
        }

        [Test]
        public void CardsHaveNoEffects_AndSnapshotRestoresSelectionAndRng()
        {
            var w=World();LoseSet(w,0);var before=w.State;uint sum=w.ComputeChecksum();
            Pick(w,2);uint picked=w.ComputeChecksum();var selected=w.State;
            Assert.AreEqual(Augments.Compute(default,w.Config),Augments.Compute(w.State.Fighter0.Augments,w.Config));
            Assert.AreNotEqual(sum,picked,"획득 목록은 체크섬에 포함");
            w.State=before;Pick(w,2);Assert.AreEqual(picked,w.ComputeChecksum());
            Assert.AreEqual(selected.Fighter0.Augments,w.State.Fighter0.Augments);
            foreach(var def in w.Config.Augments)
            {
                Assert.AreEqual(AugmentStat.None,def.AddStat);Assert.AreEqual(AugmentStat.None,def.MulStat);
                Assert.AreEqual(AugmentFlag.None,def.Flag);Assert.AreEqual(AugmentSlot.None,def.Slot);
                Assert.AreEqual(AugmentTrigger.None,def.Trigger);
            }
        }

        [Test]
        public void OnlineRematch_RequiresBothHolds_ThenClearsCards()
        {
            var w=World(Config(true,true));
            for(int set=0;set<5;set++) {LoseSet(w,1);if(set<4){Pick(w,1);Fighting(w);}}
            for(int i=0;i<150;i++)w.Step(Press(InputButtons.Attack),default);
            Assert.AreEqual(MatchPhase.MatchEnd,w.State.Match.Phase,"한 쪽만 눌러서는 재시작하지 않음");
            w.Step(Press(InputButtons.Attack),Press(InputButtons.Attack));
            Assert.AreEqual(MatchPhase.Countdown,w.State.Match.Phase);
            Assert.AreEqual(0,w.State.Fighter0.Augments.Count+w.State.Fighter1.Augments.Count);
        }

        [Test]
        public void PickTimeout_SameSeedSameChoice_Continues()
        {
            var a=World();LoseSet(a,0);var b=World();b.State=a.State;
            for(int i=0;i<=a.Config.Match.PickTimeoutTicks+1;i++){a.Step(default,default);b.Step(default,default);}
            Assert.AreEqual(1,a.State.Fighter0.Augments.Count);Assert.AreEqual(a.ComputeChecksum(),b.ComputeChecksum());
            Fighting(a);Assert.AreEqual(1,a.State.Fighter0.Augments.Count);
        }

        sealed class HeldInput : IInputSource
        {
            public InputButtons Buttons=InputButtons.Attack;
            public InputFrame Poll(int tick)=>new InputFrame(1,0,Buttons,AimDirections.Encode(12));
        }

        [TestCase(0)] [TestCase(1)]
        public void InputRouting_ClickOnlyCorrectSlot_RejectsDuplicateAndStaleClick(int slot)
        {
            var w=World();LoseSet(w,slot);var held=new HeldInput();bool blocked=false;
            var local=new FrontendInputSource(()=>w,()=>blocked,held,slot);
            var other=new FrontendInputSource(()=>w,()=>false,held,1-slot);
            Assert.IsFalse(other.Choose(1));Assert.IsFalse(local.Choose(3));
            Assert.IsTrue(local.Choose(2));Assert.IsFalse(local.Choose(1));
            Assert.AreEqual(default(InputFrame),local.Poll(0),"단계 진입은 중립 입력");
            Assert.AreEqual(InputButtons.Dash,local.Poll(1).Buttons);
            Assert.AreEqual(InputButtons.None,local.Poll(2).Buttons,"홀드한 공격은 카드 선택으로 쓰지 않음");
            Assert.IsTrue(local.Choose(0));blocked=true;Assert.AreEqual(default(InputFrame),local.Poll(3));
            blocked=false;local.Poll(4);Assert.AreEqual(default(InputFrame),local.Poll(5),"메뉴 이전 예약 선택 폐기");
            w.State.Match.SetPhase(MatchPhase.Fighting);
            Assert.AreEqual(default(InputFrame),local.Poll(6));
            Assert.AreEqual(InputButtons.None,local.Poll(7).Buttons,"계속 누른 클릭은 차징으로 이어지지 않음");
            held.Buttons=0;local.Poll(8);held.Buttons=InputButtons.Attack;
            Assert.AreEqual(InputButtons.Attack,local.Poll(9).Buttons);
            Assert.AreEqual(AimDirections.Encode(12),local.Poll(10).Aim);
        }

        [TestCase(true)] [TestCase(false)]
        public void CollectedCards_ReplayAndRollbackSevenFrames_ExactChecksum(bool fast)
        {
            var config=Config(fast);
            if(!fast){var combat=config.Combat;combat.MaxHealth=1;config=new SimConfig(config.Movement,config.Match,combat,config.Rules,config.Augments);}
            var w=World(config);var arena=Arena;
            var rec=InputRecording.Begin(config,arena,7u,w.State.Fighter0.Position,w.State.Fighter1.Position,MatchPhase.Countdown,config.Rules.Format());
            w.State=rec.BuildState(config,new[]{arena});
            var sync=new SyncTest<SimWorld>(w,7);
            var p1=new FrontendInputSource(()=>w,()=>false,new BrawlChaserBot(()=>w,0,10),0,true);
            var p2=new FrontendInputSource(()=>w,()=>false,new BrawlBot(()=>w,1,0xB22u),1,true);
            bool picked=false;
            for(int t=0;t<30000 && w.State.Match.Phase!=MatchPhase.MatchEnd;t++)
            {
                var a=p1.Poll(t);var b=p2.Poll(t);rec.Append(a,b);sync.Advance(a,b);
                picked|=w.State.Fighter0.Augments.Count+w.State.Fighter1.Augments.Count>0;
            }
            Assert.IsTrue(picked);Assert.AreEqual(MatchPhase.MatchEnd,w.State.Match.Phase);
            Assert.AreEqual(0,sync.MismatchCount,sync.FirstMismatch?.ToString());
            rec.Finish(w.ComputeChecksum());var restored=InputRecording.FromBytes(rec.ToBytes());
            Assert.AreEqual(rec.FinalChecksum,restored.Replay(config,arena));
            TestContext.WriteLine($"{rec.Count} ticks, collected {w.State.Fighter0.Augments.Count}/{w.State.Fighter1.Augments.Count}, checksum {rec.FinalChecksum:X8}, SyncTest(7)=0");
        }

        [TestCase(false,0)] [TestCase(true,0)] [TestCase(false,1)] [TestCase(true,1)] [TestCase(false,2)] [TestCase(true,2)]
        [TestCase(false,3)] [TestCase(true,3)]
        public void RecordingConfigLookup_FindsCardFlowAndKeepsV8Format(bool online,int previousPresentation)
        {
            var root=new GameObject("Card lookup");root.SetActive(false);
            try
            {
                var r=root.AddComponent<MatchRunner>();r.BrawlExperiment=true;r.Rules=RuleSet.Brawl;
                r.Tuning=AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
                r.CombatTuning=AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset");
                r.OnlineRematch=online;r.FrontendEnabled=true;
                var c=r.CreateConfig();
                if(previousPresentation > 0)
                {
                    r.FrontendEnabled=false;
                    c=CardCollectionRules.Apply(r.CreateConfig(),motion:previousPresentation>=2,
                        cinematic:previousPresentation==3,extendedIntro:false);
                    r.FrontendEnabled=true;
                    if(previousPresentation==3)
                        Assert.AreEqual(348,c.Match.CountdownForRound(1),"90틱 VS를 쓰던 v8 설정 보존");
                }
                var w=World(c);
                var recording=InputRecording.Begin(c,Arena,7u,w.State.Fighter0.Position,w.State.Fighter1.Position,MatchPhase.Countdown,c.Rules.Format());
                var found=r.FindRecordedConfig(recording);Assert.IsNotNull(found);
                Assert.IsTrue(found.Match.CollectCardsOnly);Assert.AreEqual(c.ComputeChecksum(),found.ComputeChecksum());
                var replayWorld=new SimWorld(c,Arena,recording.BuildState(c,new[]{Arena}));
                for(int tick=0;tick<c.Match.CountdownForRound(1)+90;tick++){recording.Append(default,default);replayWorld.Step(default,default);}
                recording.Finish(replayWorld.ComputeChecksum());
                Assert.AreEqual(recording.FinalChecksum,InputRecording.FromBytes(recording.ToBytes()).Replay(found,Arena));
                Assert.AreEqual(8,InputRecording.FormatVersion);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [TestCase(true)] [TestCase(false)]
        public void OnlinePairedSessions_CollectCardsAndFinish_WithLossAndDelay(bool fast)
        {
            var config=Config(fast,true);
            if(!fast){var combat=config.Combat;combat.MaxHealth=1;config=new SimConfig(config.Movement,config.Match,combat,config.Rules,config.Augments);}
            var worlds=new[]{World(config),World(config)};
            MemoryTransport.Pair(out var ma,out var mb);double now=0;
            var a=new SimulatedLink(ma,()=>now,.035,.01,.08,.04,11);
            var b=new SimulatedLink(mb,()=>now,.035,.01,.08,.04,12);
            var sessions=new[]{new LockstepSession(a,true,config.ComputeChecksum(),Arena.ComputeChecksum(),7u,4),
                new LockstepSession(b,false,config.ComputeChecksum(),Arena.ComputeChecksum(),0u,0)};
            try
            {
                for(int i=0;i<120;i++){now+=1d/60;sessions[0].Poll(now);sessions[1].Poll(now);}
                for(int i=0;i<2;i++)Assert.AreEqual(SessionState.Running,sessions[i].State);
                sessions[0].Begin(0,new FrontendInputSource(()=>worlds[0],()=>false,new BrawlChaserBot(()=>worlds[0],0,10),0,true));
                sessions[1].Begin(0,new FrontendInputSource(()=>worlds[1],()=>false,new BrawlBot(()=>worlds[1],1,0xB22u),1,true));
                var sums=new[]{new List<uint>(),new List<uint>()};bool[] finished={false,false};bool picked=false;
                for(int frame=0;frame<35000 && !(finished[0]&&finished[1]);frame++)
                {
                    now+=1d/60;
                    for(int side=0;side<2;side++)
                    {
                        var s=sessions[side];var w=worlds[side];s.Poll(now);
                        for(int step=0;step<2&&s.TryGetInputs(w.State.Tick,out var p1,out var p2);step++)
                        {
                            w.Step(p1,p2);uint sum=w.ComputeChecksum();sums[side].Add(sum);s.ReportChecksum(w.State.Tick,sum);
                            picked|=w.State.Fighter0.Augments.Count+w.State.Fighter1.Augments.Count>0;
                            finished[side]|=w.State.Match.Phase==MatchPhase.MatchEnd;
                        }
                    }
                }
                Assert.IsTrue(picked);Assert.IsTrue(finished[0]&&finished[1],"5세트 승리까지 진행");
                for(int i=0;i<2;i++)Assert.AreEqual(SessionState.Running,sessions[i].State,sessions[i].EndDetail);
                int n=System.Math.Min(sums[0].Count,sums[1].Count);
                for(int tick=0;tick<n;tick++)Assert.AreEqual(sums[0][tick],sums[1][tick],$"tick {tick}");
                TestContext.WriteLine($"paired {n} ticks, lost {a.Dropped}/{b.Dropped}, checksums {sessions[0].ChecksumsCompared}/{sessions[1].ChecksumsCompared}");
            }
            finally{sessions[0].Dispose();sessions[1].Dispose();}
        }
    }
}
