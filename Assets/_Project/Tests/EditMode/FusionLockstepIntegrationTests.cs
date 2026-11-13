using System;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>Photon 계정 없이 실제 락스텝 패킷을 Fusion 조각 규격과 나쁜 망을 통과시킨다.</summary>
    public class FusionLockstepIntegrationTests
    {
        sealed class FragmentLink : ITransport
        {
            readonly SimulatedLink wire;
            readonly Func<double> now;
            readonly FusionPacketAssembler assembler = new FusionPacketAssembler();
            uint sequence;
            public int FragmentedPackets { get; private set; }
            public int LargestPacket { get; private set; }

            public FragmentLink(SimulatedLink wire, Func<double> now) { this.wire = wire; this.now = now; }

            public void Send(byte[] data, int length)
            {
                LargestPacket = Math.Max(LargestPacket, length);
                if (length > FusionPacketCodec.ChunkBytes) FragmentedPackets++;
                foreach (var fragment in FusionPacketCodec.Encode(data, length, ++sequence))
                    wire.Send(fragment, fragment.Length);
            }

            public bool TryReceive(out byte[] packet)
            {
                while (wire.TryReceive(out var fragment))
                    if (assembler.TryAccept(fragment, now(), out packet)) return true;
                packet = null;
                return false;
            }

            public void Dispose() { assembler.Clear(); wire.Dispose(); }
        }

        sealed class SequenceInput : IInputSource
        {
            readonly int slot;
            int sample;
            public SequenceInput(int slot) => this.slot = slot;
            public InputFrame Poll(int tick) => Expected(sample++, slot);
        }

        static InputFrame Expected(int sample, int slot)
        {
            int n = sample + slot * 7;
            return new InputFrame(n % 3 - 1, n / 3 % 3 - 1, (InputButtons)((n * 13) & 255), (byte)(n % 65));
        }

        sealed class Trace
        {
            public readonly List<(InputFrame p1, InputFrame p2)> Inputs = new List<(InputFrame, InputFrame)>();
            uint checksum = 2166136261;

            public void Advance(LockstepSession session)
            {
                for (int budget = 0; budget < 2; budget++)
                {
                    int tick = Inputs.Count;
                    if (!session.TryGetInputs(tick, out var p1, out var p2)) return;
                    InputFrame expected1 = tick < session.DelayTicks ? default : Expected(tick - session.DelayTicks, 0);
                    InputFrame expected2 = tick < session.DelayTicks ? default : Expected(tick - session.DelayTicks, 1);
                    Assert.AreEqual(expected1, p1, "P1 input at " + tick);
                    Assert.AreEqual(expected2, p2, "P2 input at " + tick);
                    Inputs.Add((p1, p2));
                    checksum = unchecked((checksum ^ (uint)p1.GetHashCode()) * 16777619);
                    checksum = unchecked((checksum ^ (uint)p2.GetHashCode()) * 16777619);
                    session.ReportChecksum(tick + 1, checksum);
                }
            }
        }

        [Test]
        public void LargeLockstepPackets_RecoverFromFragmentLossReorderingAndDuplicates()
        {
            double now = 0;
            MemoryTransport.Pair(out var left, out var right);
            var leftWire = new SimulatedLink(left, () => now, .01, .06, .14, .10, 17);
            var rightWire = new SimulatedLink(right, () => now, .01, .06, .14, .10, 29);
            var leftLink = new FragmentLink(leftWire, () => now);
            var rightLink = new FragmentLink(rightWire, () => now);
            using var host = new LockstepSession(leftLink, true, 100, 200, 77, 4);
            using var guest = new LockstepSession(rightLink, false, 100, 200, 0, 0);

            for (int i = 0; i < 600 && (host.State != SessionState.Running || guest.State != SessionState.Running); i++)
            {
                now += 1.0 / 60.0;
                guest.Poll(now); host.Poll(now);
            }
            Assert.AreEqual(SessionState.Running, host.State, host.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.State, guest.EndDetail);
            Assert.AreEqual(host.Seed, guest.Seed);
            Assert.AreEqual(4, guest.DelayTicks);
            host.Begin(0, new SequenceInput(0));
            guest.Begin(0, new SequenceInput(1));

            // 포화된 미확인 입력 묶음을 강제로 만든다. 둘 다 상대 입력이 없으므로 확정 틱은 진행하지 않는다.
            // 255 frames * 4 bytes + 33-byte lockstep header/trailer = 1053 bytes (> Fusion RPC limit).
            Assert.IsFalse(host.TryGetInputs(254, out _, out _));
            Assert.IsFalse(guest.TryGetInputs(254, out _, out _));
            Assert.AreEqual(1053, leftLink.LargestPacket);
            Assert.AreEqual(1053, rightLink.LargestPacket);

            var hostTrace = new Trace();
            var guestTrace = new Trace();
            for (int frame = 0; frame < 3000; frame++)
            {
                now += 1.0 / 60.0;
                host.Poll(now); guest.Poll(now);
                hostTrace.Advance(host); guestTrace.Advance(guest);
            }
            Assert.AreEqual(SessionState.Running, host.State, host.EndDetail);
            Assert.AreEqual(SessionState.Running, guest.State, guest.EndDetail);
            int commonTicks = Math.Min(hostTrace.Inputs.Count, guestTrace.Inputs.Count);
            Assert.Greater(commonTicks, 1000, "조각 손실을 입력 재전송으로 회복하며 계속 진행");
            for (int tick = 0; tick < commonTicks; tick++)
                Assert.AreEqual(hostTrace.Inputs[tick], guestTrace.Inputs[tick], "accepted input pair at " + tick);
            Assert.Greater(host.ChecksumsCompared, 10);
            Assert.Greater(guest.ChecksumsCompared, 10);
            Assert.Greater(leftWire.Dropped + rightWire.Dropped, 0);
            Assert.Greater(leftWire.Duplicated + rightWire.Duplicated, 0);
            Assert.Greater(leftLink.FragmentedPackets + rightLink.FragmentedPackets, 2,
                "최초 큰 패킷뿐 아니라 조각 손실 뒤의 재전송도 검사");
        }
    }
}
