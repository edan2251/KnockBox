using System;
using System.Security.Cryptography;

namespace Knockbox.Game.Online
{
    /// <summary>시뮬레이션 난수를 소비하지 않는 8자리 초대 코드. 이름/계정 ID와 무관하다.</summary>
    public static class FusionRoomCode
    {
        public const int Length = 8;
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Create()
        {
            var bytes = new byte[Length];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var code = new char[Length];
            // 32문자이므로 256에서 균등하게 매핑된다.
            for (int i = 0; i < code.Length; i++) code[i] = Alphabet[bytes[i] & 31];
            return new string(code);
        }

        public static bool TryNormalize(string value, out string code)
        {
            code = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length != Length) return false;
            for (int i = 0; i < code.Length; i++)
                if (Alphabet.IndexOf(code[i]) < 0) return false;
            return true;
        }
    }
}
