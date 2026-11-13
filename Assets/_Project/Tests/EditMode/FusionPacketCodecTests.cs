using System;
using System.Linq;
using Knockbox.Game.Online;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class FusionPacketCodecTests
    {
        static byte[] Data(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 11)).ToArray();

        [TestCase(1)]
        [TestCase(388)]
        [TestCase(389)]
        [TestCase(1053)]
        [TestCase(2048)]
        public void OriginalPacket_RoundTripsOutOfOrder_WithinRpcBudget(int length)
        {
            byte[] source = Data(length);
            var fragments = FusionPacketCodec.Encode(source, length, uint.MaxValue).Reverse().ToArray();
            var assembler = new FusionPacketAssembler();
            byte[] result = null;
            int completions = 0;
            foreach (var fragment in fragments)
            {
                Assert.LessOrEqual(fragment.Length, 400);
                if (assembler.TryAccept(fragment, 0, out var packet)) { result = packet; completions++; }
                Assert.IsFalse(assembler.TryAccept(fragment, .1, out _), "조각/완성 패킷 중복은 재출력하지 않음");
            }
            Assert.AreEqual(1, completions);
            CollectionAssert.AreEqual(source, result);
            Assert.AreEqual(0, assembler.PendingCount);
        }

        [Test]
        public void SenderAndReceiver_CopyBuffers_NotTheirLaterMutations()
        {
            byte[] source = Data(1000);
            byte[] expected = (byte[])source.Clone();
            var parts = FusionPacketCodec.Encode(source, source.Length, 7).ToArray();
            source[0] ^= 0xff;
            var assembler = new FusionPacketAssembler();
            Assert.IsFalse(assembler.TryAccept(parts[0], 0, out _));
            parts[0][FusionPacketCodec.HeaderBytes] ^= 0xff;
            Assert.IsFalse(assembler.TryAccept(parts[1], .1, out _));
            Assert.IsTrue(assembler.TryAccept(parts[2], .2, out var result));
            CollectionAssert.AreEqual(expected, result);
        }

        [Test]
        public void LostFragment_DoesNotYieldPartialPacket_AndExpires()
        {
            var parts = FusionPacketCodec.Encode(Data(1000), 1000, 8).ToArray();
            var assembler = new FusionPacketAssembler();
            Assert.IsFalse(assembler.TryAccept(parts[0], 0, out _));
            Assert.IsFalse(assembler.TryAccept(parts[2], .1, out _));
            Assert.IsFalse(assembler.TryAccept(null, 2.01, out _));
            Assert.AreEqual(0, assembler.PendingCount);
            Assert.IsFalse(assembler.TryAccept(parts[1], 2.02, out _));
            // 전송 계층은 새 시퀀스로 재전송한다. 오래된 조각과 섞이지 않는다.
            var retry = FusionPacketCodec.Encode(Data(1000), 1000, 9).ToArray();
            Assert.IsFalse(assembler.TryAccept(retry[0], 2.03, out _));
            Assert.IsFalse(assembler.TryAccept(retry[1], 2.04, out _));
            Assert.IsTrue(assembler.TryAccept(retry[2], 2.05, out var result));
            CollectionAssert.AreEqual(Data(1000), result);
        }

        [Test]
        public void ConflictingDuplicate_RejectsWholeSequence()
        {
            var parts = FusionPacketCodec.Encode(Data(1000), 1000, 3).ToArray();
            var assembler = new FusionPacketAssembler();
            Assert.IsFalse(assembler.TryAccept(parts[0], 0, out _));
            var conflict = (byte[])parts[0].Clone();
            conflict[FusionPacketCodec.HeaderBytes] ^= 0xff;
            Assert.IsFalse(assembler.TryAccept(conflict, .1, out _));
            Assert.IsFalse(assembler.TryAccept(parts[1], .2, out _));
            Assert.IsFalse(assembler.TryAccept(parts[2], .3, out _));
            Assert.AreEqual(0, assembler.PendingCount);
        }

        [Test]
        public void ConflictingLength_RejectsWholeSequence()
        {
            var assembler = new FusionPacketAssembler();
            var original = FusionPacketCodec.Encode(Data(1000), 1000, 4).ToArray();
            var different = FusionPacketCodec.Encode(Data(900), 900, 4).ToArray();
            Assert.IsFalse(assembler.TryAccept(original[0], 0, out _));
            Assert.IsFalse(assembler.TryAccept(different[1], .1, out _));
            Assert.IsFalse(assembler.TryAccept(original[1], .2, out _));
            Assert.IsFalse(assembler.TryAccept(original[2], .3, out _));
            Assert.AreEqual(0, assembler.PendingCount);
        }

        [Test]
        public void InvalidWireHeaders_AllocateNoPendingPacket()
        {
            var source = FusionPacketCodec.Encode(Data(1000), 1000, 1).First();
            var assembler = new FusionPacketAssembler();
            foreach (int index in new[] { 0, 1, 2, 3, 5, 6, 7 })
            {
                var bad = (byte[])source.Clone(); bad[index] = 0xff;
                Assert.IsFalse(assembler.TryAccept(bad, 0, out _), "header byte " + index);
                Assert.AreEqual(0, assembler.PendingCount);
            }
            var empty = (byte[])source.Clone(); empty[4] = empty[5] = 0;
            Assert.IsFalse(assembler.TryAccept(empty, 0, out _));
            Assert.IsFalse(assembler.TryAccept(source.Take(source.Length - 1).ToArray(), 0, out _));
            Assert.IsFalse(assembler.TryAccept(new byte[401], 0, out _));
            Assert.IsFalse(assembler.TryAccept(source, double.NaN, out _));
            Assert.AreEqual(0, assembler.PendingCount);
        }

        [Test]
        public void PendingAndRetainedPackets_AreBounded_AndClearAllowsNewSession()
        {
            var assembler = new FusionPacketAssembler();
            for (uint i = 0; i < 300; i++)
            {
                var fragment = FusionPacketCodec.Encode(Data(1000), 1000, i).First();
                Assert.IsFalse(assembler.TryAccept(fragment, i * .001, out _));
                Assert.LessOrEqual(assembler.PendingCount, FusionPacketAssembler.MaxPendingPackets);
            }
            for (uint i = 300; i < 600; i++)
            {
                var fragment = FusionPacketCodec.Encode(Data(1), 1, i).First();
                Assert.IsTrue(assembler.TryAccept(fragment, i * .001, out _));
                Assert.LessOrEqual(assembler.RetainedSequenceCount, FusionPacketAssembler.MaxRetainedSequences);
            }
            assembler.Clear();
            Assert.AreEqual(0, assembler.PendingCount);
            Assert.AreEqual(0, assembler.RetainedSequenceCount);
            Assert.IsTrue(assembler.TryAccept(FusionPacketCodec.Encode(Data(1), 1, 599).First(), 0, out _));
        }

        [Test]
        public void InvalidSourceLength_ThrowsInsteadOfTruncating()
        {
            Assert.Throws<ArgumentNullException>(() => FusionPacketCodec.Encode(null, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FusionPacketCodec.Encode(Data(1), 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FusionPacketCodec.Encode(Data(1), 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FusionPacketCodec.Encode(Data(2049), 2049, 0));
        }
    }
}
