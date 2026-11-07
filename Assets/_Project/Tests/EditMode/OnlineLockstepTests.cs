using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 온라인 2단계: 락스텝 세션(메모리 연결 + 나쁜 망 흉내)과 두 선수 함께 누르기 재대결.
    /// 연결 검증만 한다(전투 반응성은 사람 플레이로 따로 본다).
    /// </summary>
    public class OnlineLockstepTests
    {
        static ArenaDef arena;
        static ArenaDef Arena => arena ??= ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimConfig Config(bool online)
        {
            var match = BrawlParams.Match(MatchParams.Default);
            match.RematchBothHold = online;
            return new SimConfig(MovementParams.Default, match, BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl);
        }

        static SimWorld NewWorld(SimConfig config) =>
            new SimWorld(config, new[] { Arena }, SimState.CreateMatch(config, new[] { Arena }, 7u, MatchPhase.Countdown));

        sealed class Side
        {
            public LockstepSession Session;
            public SimWorld World;
            public readonly List<uint> Checksums = new List<uint>();

            public void Frame(double now)
            {
                Session.Poll(now);
                for (int i = 0; i < 2 && Session.TryGetInputs(World.State.Tick, out var p1, out var p2); i++)
                {
                    World.Step(p1, p2);
                    int tick = World.State.Tick;
                    uint sum = World.ComputeChecksum();
                    Checksums.Add(sum);
                    Session.ReportChecksum(tick, sum);
                }
            }
        }

        static (Side host, Side guest) Pair(ITransport a, ITransport b, int delay, uint configChecksum)
        {
            var config = Config(true);
            var host = new Side { World = NewWorld(config) };
            var guest = new Side { World = NewWorld(config) };
            host.Session = new LockstepSession(a, true, configChecksum, Arena.ComputeChecksum(), 99u, delay);
            guest.Session = new LockstepSession(b, false, configChecksum, Arena.ComputeChecksum(), 0u, 0);
            return (host, guest);
        }

        static void Connect(Side host, Side guest, ref double now)
        {
            for (int i = 0; i < 120 && !(host.Session.State == SessionState.Running && guest.Session.State == SessionState.Running); i++)
            {
                now += 1.0 / 60.0;
                guest.Session.Poll(now);
                host.Session.Poll(now);
            }
            Assert.AreEqual(SessionState.Running, host.Session.State);
            Assert.AreEqual(SessionState.Running, guest.Session.State);
            host.Session.Begin(0, new BrawlChaserBot(() => host.World, 0, 10));
            guest.Session.Begin(0, new BrawlBot(() => guest.World, 1, 0xB22u));
        }

        [Test]
        public void Lockstep_BadNetwork_SameStateEveryTick()
        {
            MemoryTransport.Pair(out var ma, out var mb);
            double now = 0;
            // 편도 40 ms + 흔들림 15 ms(순서 바뀜), 잃음 10%, 중복 5% → 왕복 약 80~110 ms.
            var la = new SimulatedLink(ma, () => now, 0.04, 0.015, 0.10, 0.05, 1);
            var lb = new SimulatedLink(mb, () => now, 0.04, 0.015, 0.10, 0.05, 2);
            var (host, guest) = Pair(la, lb, 6, Config(true).ComputeChecksum());
            Connect(host, guest, ref now);
            Assert.AreEqual(6, guest.Session.DelayTicks, "손님은 방장의 입력 지연을 받음");
            for (int frame = 0; frame < 3000; frame++)
            {
                now += 1.0 / 60.0;
                host.Frame(now);
                guest.Frame(now);
            }
            int n = System.Math.Min(host.Checksums.Count, guest.Checksums.Count);
            TestContext.WriteLine($"틱 {host.Checksums.Count}/{guest.Checksums.Count}, 왕복 {host.Session.RttSeconds * 1000:0} ms, 대기 {host.Session.StallCount}/{guest.Session.StallCount}, " +
                                  $"체크섬 비교 {host.Session.ChecksumsCompared}, 중복 무시 {host.Session.DuplicatesIgnored}/{guest.Session.DuplicatesIgnored}, 잃음 {la.Dropped}/{lb.Dropped}");
            Assert.AreEqual(SessionState.Running, host.Session.State, host.Session.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.Session.State, guest.Session.EndDetail);
            Assert.Greater(n, 2000, "멈추지 않고 진행");
            for (int i = 0; i < n; i++)
                Assert.AreEqual(host.Checksums[i], guest.Checksums[i], $"틱 {i + 1}");
            Assert.Greater(host.Session.ChecksumsCompared, 50);
            Assert.Greater(host.Session.DuplicatesIgnored + guest.Session.DuplicatesIgnored, 0, "중복 패킷을 받아도 버림");
            Assert.Greater(host.Session.RttSeconds, 0.06);
        }

        [Test]
        public void Lockstep_Timeout_PeerLeft_ConfigMismatch()
        {
            MemoryTransport.Pair(out var a, out var b);
            var (host, guest) = Pair(a, b, 3, Config(true).ComputeChecksum());
            double now = 0;
            Connect(host, guest, ref now);
            for (int i = 0; i < 60; i++)
            {
                now += 1.0 / 60.0;
                host.Frame(now);
                guest.Frame(now);
            }
            // 손님이 멈춤(아무것도 보내지 않음) → 방장은 5초 뒤 끊김.
            for (int i = 0; i < 60 * 6; i++)
            {
                now += 1.0 / 60.0;
                host.Frame(now);
            }
            Assert.AreEqual(EndReason.Timeout, host.Session.Reason);

            MemoryTransport.Pair(out a, out b);
            (host, guest) = Pair(a, b, 3, Config(true).ComputeChecksum());
            now = 0;
            Connect(host, guest, ref now);
            guest.Session.Leave();
            host.Frame(now + 0.1);
            Assert.AreEqual(EndReason.PeerLeft, host.Session.Reason);

            // 설정이 다른 빌드는 거절.
            MemoryTransport.Pair(out a, out b);
            var h = new LockstepSession(a, true, 1u, 2u, 1u, 3);
            var g = new LockstepSession(b, false, 5u, 2u, 0u, 0);
            for (int i = 0; i < 10; i++)
            {
                g.Poll(i * 0.3);
                h.Poll(i * 0.3);
            }
            Assert.AreEqual(SessionState.WaitingPeer, h.State);
            Assert.AreEqual(EndReason.Rejected, g.Reason);
        }

        [Test]
        public void Lockstep_SameTickDifferentInput_StopsWithProtocolError()
        {
            MemoryTransport.Pair(out var a, out var b);
            var (host, guest) = Pair(a, b, 3, Config(true).ComputeChecksum());
            double now = 0;
            Connect(host, guest, ref now);
            for (int i = 0; i < 30; i++)
            {
                now += 1.0 / 60.0;
                host.Frame(now);
                guest.Frame(now);
            }
            // 이미 받은 틱 3(지연 3틱 뒤 첫 실제 입력)을 다른 값으로 다시 보낸다.
            var bytes = new List<byte>();
            void U(uint v) => bytes.AddRange(System.BitConverter.GetBytes(v));
            U(LockstepSession.Magic);
            bytes.Add(LockstepSession.ProtocolVersion);
            bytes.Add(4);
            U(1u);
            U(0u);
            bytes.AddRange(System.BitConverter.GetBytes((ushort)0));
            U(0u);
            U(3u);
            bytes.Add(1);
            var buffer = new byte[InputFrame.Size];
            new InputFrame(1, 1, InputButtons.Attack | InputButtons.Grab | InputButtons.Jump, 63).Write(buffer, 0);
            bytes.AddRange(buffer);
            U(unchecked((uint)-1));
            U(0u);
            b.Send(bytes.ToArray(), bytes.Count);
            host.Frame(now + 0.01);
            Assert.AreEqual(EndReason.ProtocolError, host.Session.Reason, host.Session.EndDetail);
        }

        [Test]
        public void Rematch_BothHold_OnlyWhenOnline()
        {
            foreach (bool online in new[] { false, true })
            {
                var config = Config(online);
                var w = NewWorld(config);
                w.State.Match.SetPhase(MatchPhase.MatchEnd);
                w.State.Match.PhaseTick = config.Match.MatchEndMinTicks;
                var hold = new InputFrame(0, 0, InputButtons.Attack, 0);
                w.Step(hold, default);
                Assert.AreEqual(online ? MatchPhase.MatchEnd : MatchPhase.Countdown, w.State.Match.Phase, $"한 명만 누름(온라인 {online})");
                if (!online)
                    continue;
                for (int i = 0; i < 5; i++)
                    w.Step(hold, default);
                Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase, "한 명이 계속 눌러도 그대로");
                w.Step(hold, hold);
                Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase, "둘 다 누르고 있으면 새 경기");
            }
            // 켜면 설정 체크섬이 달라 로컬 기록과 섞이지 않는다(끈 설정이 예전과 같은지는 대표 기록 재생 검사로 확인).
            Assert.AreNotEqual(Config(false).ComputeChecksum(), Config(true).ComputeChecksum());
        }
    }
}
