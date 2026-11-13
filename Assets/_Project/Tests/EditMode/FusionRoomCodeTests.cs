using System.Linq;
using Knockbox.Game.Online;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class FusionRoomCodeTests
    {
        [Test]
        public void Alphabet_HasExactly32DistinctReadableCharacters()
        {
            Assert.AreEqual(32, FusionRoomCode.Alphabet.Length, "바이트의 하위 5비트로 균등 매핑하는 조건");
            Assert.AreEqual(32, FusionRoomCode.Alphabet.Distinct().Count());
            foreach (char confusing in "01IO")
                Assert.IsFalse(FusionRoomCode.Alphabet.Contains(confusing), "헷갈리는 문자 제외: " + confusing);
        }

        [Test]
        public void GeneratedCode_AlwaysFitsAcceptedInputAndDisplayLength()
        {
            for (int i = 0; i < 512; i++)
            {
                string code = FusionRoomCode.Create();
                Assert.AreEqual(FusionRoomCode.Length, code.Length);
                Assert.IsTrue(code.All(c => FusionRoomCode.Alphabet.IndexOf(c) >= 0));
                Assert.IsTrue(FusionRoomCode.TryNormalize(code, out var normalized));
                Assert.AreEqual(code, normalized);
            }
        }

        [TestCase(" k7m4q2rx ", "K7M4Q2RX")]
        [TestCase("\tabcdefgh\r\n", "ABCDEFGH")]
        [TestCase("23456789", "23456789")]
        [TestCase("ABCDEFGH", "ABCDEFGH")]
        public void Normalize_AcceptsLowercaseAndOuterWhitespace(string input, string expected)
        {
            Assert.IsTrue(FusionRoomCode.TryNormalize(input, out var actual));
            Assert.AreEqual(expected, actual);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("        ")]
        [TestCase("ABCDEFG")]
        [TestCase("ABCDEFGHJ")]
        [TestCase("ABCDEF0H")]
        [TestCase("ABCDEF1H")]
        [TestCase("ABCDEFIO")]
        [TestCase("ABCDefio")]
        [TestCase("ABCD EFG")]
        [TestCase("ABCD-EFG")]
        [TestCase("ABCD_EFG")]
        [TestCase("ABCD한글EF")]
        public void Normalize_RejectsMissingInvalidLengthOrUnacceptedCharacters(string input)
        {
            Assert.IsFalse(FusionRoomCode.TryNormalize(input, out _));
        }
    }
}
