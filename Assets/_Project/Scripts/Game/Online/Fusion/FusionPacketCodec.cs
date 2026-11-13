using System;
using System.Collections.Generic;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// Fusion RPC 한도 안에서 기존 락스텝 패킷을 분할한다. 전투/입력 규격은 변경하지 않는다.
    /// 조각 하나라도 잃으면 전체 패킷은 버린다. 입력 재전송은 기존 LockstepSession의 책임이다.
    /// </summary>
    public static class FusionPacketCodec
    {
        public const int MaxFragmentBytes = 400;
        public const int HeaderBytes = 12;
        public const int ChunkBytes = MaxFragmentBytes - HeaderBytes;
        public const int MaxPacketBytes = 2048;

        public static IEnumerable<byte[]> Encode(byte[] data, int length, uint sequence)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 1 || length > data.Length || length > MaxPacketBytes)
                throw new ArgumentOutOfRangeException(nameof(length));
            int count = (length + ChunkBytes - 1) / ChunkBytes;
            var fragments = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                int offset = i * ChunkBytes;
                int bytes = Math.Min(ChunkBytes, length - offset);
                var part = new byte[HeaderBytes + bytes];
                part[0] = 0x4b; part[1] = 0x42; part[2] = 1;
                part[3] = (byte)i;
                part[4] = (byte)length; part[5] = (byte)(length >> 8);
                part[6] = (byte)count; part[7] = 0;
                for (int b = 0; b < 4; b++) part[8 + b] = (byte)(sequence >> (b * 8));
                Buffer.BlockCopy(data, offset, part, HeaderBytes, bytes);
                fragments[i] = part;
            }
            return fragments;
        }
    }

    /// <summary>상대 한 명의 조각 조립기. 입력은 호출 중 복사하며 보관 개수와 수명을 제한한다.</summary>
    public sealed class FusionPacketAssembler
    {
        public const int MaxPendingPackets = 16;
        public const int MaxRetainedSequences = 128;
        public const double RetentionSeconds = 2.0;

        sealed class Pending
        {
            public byte[] Bytes;
            public int Count;
            public int Mask;
            public double CreatedAt;
        }

        readonly Dictionary<uint, Pending> pending = new Dictionary<uint, Pending>();
        // 완료되거나 충돌한 패킷은 잠깐 기억하여 중복/혼합 조립을 막는다.
        readonly Dictionary<uint, double> retained = new Dictionary<uint, double>();
        readonly List<uint> expired = new List<uint>(MaxRetainedSequences);

        public int PendingCount => pending.Count;
        public int RetainedSequenceCount => retained.Count;

        public void Clear()
        {
            pending.Clear(); retained.Clear(); expired.Clear();
        }

        public bool TryAccept(byte[] fragment, double now, out byte[] packet)
        {
            packet = null;
            if (double.IsNaN(now) || double.IsInfinity(now)) return false;
            Expire(now);
            if (fragment == null || fragment.Length <= FusionPacketCodec.HeaderBytes
                || fragment.Length > FusionPacketCodec.MaxFragmentBytes) return false;
            if (fragment[0] != 0x4b || fragment[1] != 0x42 || fragment[2] != 1 || fragment[7] != 0) return false;
            int index = fragment[3];
            int total = fragment[4] | (fragment[5] << 8);
            int count = fragment[6];
            if (total < 1 || total > FusionPacketCodec.MaxPacketBytes
                || count != (total + FusionPacketCodec.ChunkBytes - 1) / FusionPacketCodec.ChunkBytes
                || index >= count) return false;
            int offset = index * FusionPacketCodec.ChunkBytes;
            int bytes = Math.Min(FusionPacketCodec.ChunkBytes, total - offset);
            if (fragment.Length != FusionPacketCodec.HeaderBytes + bytes) return false;
            uint sequence = 0;
            for (int b = 0; b < 4; b++) sequence |= (uint)fragment[8 + b] << (b * 8);
            if (retained.ContainsKey(sequence)) return false;

            if (!pending.TryGetValue(sequence, out var entry))
            {
                if (pending.Count == MaxPendingPackets) EvictOldestPending();
                entry = new Pending { Bytes = new byte[total], Count = count, CreatedAt = now };
                pending.Add(sequence, entry);
            }
            else if (entry.Bytes.Length != total || entry.Count != count)
            {
                pending.Remove(sequence); Retain(sequence, now); return false;
            }
            int bit = 1 << index;
            if ((entry.Mask & bit) != 0)
            {
                for (int i = 0; i < bytes; i++)
                {
                    if (entry.Bytes[offset + i] == fragment[FusionPacketCodec.HeaderBytes + i]) continue;
                    pending.Remove(sequence); Retain(sequence, now); return false;
                }
                return false;
            }
            Buffer.BlockCopy(fragment, FusionPacketCodec.HeaderBytes, entry.Bytes, offset, bytes);
            entry.Mask |= bit;
            if (entry.Mask != (1 << count) - 1) return false;
            pending.Remove(sequence);
            Retain(sequence, now);
            packet = entry.Bytes;
            return true;
        }

        void Expire(double now)
        {
            expired.Clear();
            foreach (var pair in pending)
                if (now - pair.Value.CreatedAt >= RetentionSeconds) expired.Add(pair.Key);
            foreach (uint key in expired) pending.Remove(key);
            expired.Clear();
            foreach (var pair in retained)
                if (now - pair.Value >= RetentionSeconds) expired.Add(pair.Key);
            foreach (uint key in expired) retained.Remove(key);
            expired.Clear();
        }

        void EvictOldestPending()
        {
            uint key = 0;
            double oldest = double.PositiveInfinity;
            foreach (var pair in pending)
                if (pair.Value.CreatedAt < oldest) { oldest = pair.Value.CreatedAt; key = pair.Key; }
            pending.Remove(key);
        }

        void Retain(uint sequence, double now)
        {
            if (retained.Count == MaxRetainedSequences)
            {
                uint key = 0;
                double oldest = double.PositiveInfinity;
                foreach (var pair in retained)
                    if (pair.Value < oldest) { oldest = pair.Value; key = pair.Key; }
                retained.Remove(key);
            }
            retained[sequence] = now;
        }
    }
}
