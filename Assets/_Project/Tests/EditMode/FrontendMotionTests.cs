using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class FrontendMotionTests
    {
        static SimConfig Config(bool online = true)
        {
            var m = BrawlParams.Match(MatchParams.Default); m.RematchBothHold = online;
            return CardCollectionRules.Apply(new SimConfig(BrawlParams.Movement(MovementParams.Default), m,
                BrawlParams.Combat(CombatParams.Default, weaponsEnabled: true, combinedAttackInput: true, throwEnabled: true, revisedBalance: true), RuleSet.Brawl));
        }
        static ArenaDef Arena => ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
        sealed class HeldAttack : IInputSource { public InputFrame Poll(int tick) => new InputFrame(0, 0, InputButtons.Attack); }

        [Test]
        public void IntroIsLonger_SecondRoundUsesRebuildCountdown_AndBothBodiesStayAtSpawn()
        {
            var c = Config(); var a = Arena;
            Assert.AreEqual(480, c.Match.CountdownForRound(1), "첫 VS 222틱 + 재구성 54틱 + 3·2·1·GO 204틱");
            Assert.AreEqual(258, c.Match.CountdownForRound(2), "후속 라운드에는 VS 시간이 다시 붙지 않는다");
            var w = new SimWorld(c, a, SimState.CreateRound(c, a, 7, MatchPhase.Countdown));
            var p0 = w.State.Fighter0.Position; var p1 = w.State.Fighter1.Position;
            for (int i = 0; i < c.Match.IntroCountdownTicks - 1; i++) w.Step(default, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(p0, w.State.Fighter0.Position); Assert.AreEqual(p1, w.State.Fighter1.Position);
            w.Step(default, default); Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
            w.State.Fighter1.Health = 0;
            for (int i = 0; i < 200 && w.State.Match.Round == 1; i++) w.Step(default, default);
            Assert.AreEqual(2, w.State.Match.Round); Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            for (int i = 0; i < c.Match.CountdownTicks - 1; i++) w.Step(default, default);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            w.Step(default, default); Assert.AreEqual(MatchPhase.Fighting, w.State.Match.Phase);
        }

        [Test]
        public void OneClickReadinessPersists_WaitsForOtherSide_ReplayRestoreAndSyncSevenAgree()
        {
            var c = Config();var a = Arena;
            var record = InputRecording.Begin(c, a, 7, a.SpawnFoot0, a.SpawnFoot1, MatchPhase.MatchEnd, c.Rules.Format());
            var w = new SimWorld(c, a, record.BuildState(c, new[] { a }));
            var sync = new SyncTest<SimWorld>(w, 7);
            var one = new FrontendInputSource(() => w, () => false, new HeldAttack(), 0);
            var two = new FrontendInputSource(() => w, () => false, new HeldAttack(), 1);
            Assert.IsFalse(one.ReadyForRematch(), "결과 감상 중 O 입력 금지");
            for (int i = 0; i <= c.Match.MatchEndMinTicks; i++)
            {
                var x = one.Poll(i);var y = two.Poll(i);
                Assert.AreEqual(InputButtons.None, x.Buttons | y.Buttons, "전투 때 홀드한 클릭은 결과를 넘기지 않는다");
                record.Append(x, y);sync.Advance(x, y);
            }
            Assert.IsTrue(one.ReadyForRematch());Assert.IsFalse(one.ReadyForRematch());
            var snapshot = w.State;var frames = new List<(InputFrame, InputFrame)>();
            for (int i = 0; i < 40; i++)
            {
                var x = one.Poll(i);var y = two.Poll(i);frames.Add((x, y));record.Append(x, y);sync.Advance(x, y);
                Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
                Assert.AreEqual(InputButtons.Attack, w.State.Fighter0.PrevButtons);
                Assert.AreEqual(InputButtons.None, w.State.Fighter1.PrevButtons);
            }
            Assert.IsTrue(two.ReadyForRematch());
            var final0 = one.Poll(999);var final1 = two.Poll(999);
            frames.Add((final0, final1));record.Append(final0, final1);sync.Advance(final0, final1);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase);
            Assert.AreEqual(InputButtons.None, one.Poll(1000).Buttons, "준비 입력이 새 경기로 이어지지 않는다");
            Assert.AreEqual(0, sync.MismatchCount);
            uint checksum = w.ComputeChecksum();record.Finish(checksum);
            Assert.AreEqual(checksum, InputRecording.FromBytes(record.ToBytes()).Replay(c, a));
            w.State = snapshot;foreach (var frame in frames) w.Step(frame.Item1, frame.Item2);
            Assert.AreEqual(checksum, w.ComputeChecksum());
        }

        [Test]
        public void OfflinePracticeBotWaitsForLocalReadyClick_ThenReplayAgrees()
        {
            var c=Config(false);var a=Arena;
            var record=InputRecording.Begin(c,a,7,a.SpawnFoot0,a.SpawnFoot1,MatchPhase.MatchEnd,c.Rules.Format());
            var w=new SimWorld(c,a,record.BuildState(c,new[]{a}));
            var local=new FrontendInputSource(()=>w,()=>false,new HeldAttack(),0);
            var bot=new FrontendInputSource(()=>w,()=>false,new HeldAttack(),1,true);
            for(int i=0;i<600;i++)
            {
                var p0=local.Poll(i);var p1=bot.Poll(i);record.Append(p0,p1);w.Step(p0,p1);
                Assert.AreEqual(MatchPhase.MatchEnd,w.State.Match.Phase,"연습 상대의 자동 선택 기능이 O 없이 재대결하면 안 됨");
                Assert.AreEqual(InputButtons.None,p0.Buttons|p1.Buttons);
            }
            Assert.IsTrue(local.ReadyForRematch());
            var ready=local.Poll(600);var other=bot.Poll(600);record.Append(ready,other);w.Step(ready,other);
            Assert.AreEqual(MatchPhase.Countdown,w.State.Match.Phase);
            record.Finish(w.ComputeChecksum());Assert.AreEqual(record.FinalChecksum,InputRecording.FromBytes(record.ToBytes()).Replay(c,a));
        }

        [Test]
        public void ReadyClickDoesNotSurviveBlockedMenuOrReplacedWorld()
        {
            var c = Config();var a = Arena;bool blocked = false;
            var w = new SimWorld(c, a, SimState.CreateRound(c, a, 7, MatchPhase.MatchEnd));
            w.State.Match.PhaseTick = c.Match.MatchEndMinTicks;
            var input = new FrontendInputSource(() => w, () => blocked, new IdleInputSource(), 0);
            Assert.IsTrue(input.ReadyForRematch());blocked = true;Assert.AreEqual(default(InputFrame), input.Poll(0));
            blocked = false;Assert.AreEqual(default(InputFrame), input.Poll(1));
            Assert.IsTrue(input.ReadyForRematch());
            w = new SimWorld(c, a, SimState.CreateRound(c, a, 7, MatchPhase.MatchEnd));
            w.State.Match.PhaseTick = c.Match.MatchEndMinTicks;
            Assert.AreEqual(default(InputFrame), input.Poll(2));
        }

        [Test]
        public void PairedOnlineReadyClicks_WithLossAndDelay_StartTogetherAndReplay()
        {
            var c=Config();var arena=Arena;
            var record=InputRecording.Begin(c,arena,7,arena.SpawnFoot0,arena.SpawnFoot1,MatchPhase.MatchEnd,c.Rules.Format());
            var initial=record.BuildState(c,new[]{arena});
            var worlds=new[]{new SimWorld(c,arena,initial),new SimWorld(c,arena,initial)};
            MemoryTransport.Pair(out var a,out var b);double now=0;
            var link0=new SimulatedLink(a,()=>now,.035,.01,.08,.04,31);
            var link1=new SimulatedLink(b,()=>now,.035,.01,.08,.04,32);
            var sessions=new[]{new LockstepSession(link0,true,c.ComputeChecksum(),arena.ComputeChecksum(),7,4),
                new LockstepSession(link1,false,c.ComputeChecksum(),arena.ComputeChecksum(),0,0)};
            var input0=new FrontendInputSource(()=>worlds[0],()=>false,new HeldAttack(),0);
            var input1=new FrontendInputSource(()=>worlds[1],()=>false,new HeldAttack(),1);
            var sums=new[]{new List<uint>(),new List<uint>()};bool first=false,second=false,oneReadySeen=false;
            try
            {
                for(int i=0;i<120;i++){now+=1d/60;sessions[0].Poll(now);sessions[1].Poll(now);}
                sessions[0].Begin(0,input0);sessions[1].Begin(0,input1);
                for(int frame=0;frame<1600;frame++)
                {
                    now+=1d/60;
                    if(!first && worlds[0].State.Match.PhaseTick>=c.Match.MatchEndMinTicks)first=input0.ReadyForRematch();
                    if(!second && worlds[1].State.Match.PhaseTick>=c.Match.MatchEndMinTicks+70)second=input1.ReadyForRematch();
                    for(int side=0;side<2;side++)
                    {
                        var session=sessions[side];var w=worlds[side];session.Poll(now);
                        for(int step=0;step<2&&session.TryGetInputs(w.State.Tick,out var p0,out var p1);step++)
                        {
                            if(side==0)record.Append(p0,p1);
                            w.Step(p0,p1);uint sum=w.ComputeChecksum();sums[side].Add(sum);session.ReportChecksum(w.State.Tick,sum);
                            oneReadySeen|=w.State.Match.Phase==MatchPhase.MatchEnd && w.State.Fighter0.PrevButtons==InputButtons.Attack && w.State.Fighter1.PrevButtons==InputButtons.None;
                        }
                    }
                    if(worlds[0].State.Match.Phase==MatchPhase.Fighting&&worlds[1].State.Match.Phase==MatchPhase.Fighting)break;
                }
                Assert.IsTrue(first&&second&&oneReadySeen);
                for(int side=0;side<2;side++)
                {
                    Assert.AreEqual(SessionState.Running,sessions[side].State,sessions[side].EndDetail);
                    Assert.AreEqual(MatchPhase.Fighting,worlds[side].State.Match.Phase);
                    Assert.AreEqual(InputButtons.None,worlds[side].State.Fighter0.PrevButtons|worlds[side].State.Fighter1.PrevButtons);
                }
                int n=System.Math.Min(sums[0].Count,sums[1].Count);
                for(int i=0;i<n;i++)Assert.AreEqual(sums[0][i],sums[1][i],"tick "+i);
                record.Finish(worlds[0].ComputeChecksum());Assert.AreEqual(record.FinalChecksum,InputRecording.FromBytes(record.ToBytes()).Replay(c,arena));
                TestContext.WriteLine($"ready/rematch {n} ticks agree; lost {link0.Dropped}/{link1.Dropped}; replay {record.FinalChecksum:X8}");
            }
            finally{sessions[0].Dispose();sessions[1].Dispose();}
        }
    }
}
