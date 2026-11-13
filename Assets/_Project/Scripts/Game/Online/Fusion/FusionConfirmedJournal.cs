using System;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    /// <summary>Host-approved input stream. Prediction/resimulation never appends to this replay or emits its events.</summary>
    public sealed class FusionConfirmedJournal
    {
        public const byte Version = 1;
        public const int BatchSize = 4;
        public SimWorld World { get; }
        public InputRecording Recording { get; }
        public event Action Confirmed;
        public int BatchesVerified { get; private set; }
        public int DuplicateBatches { get; private set; }
        readonly SimConfig config;
        readonly ArenaDef[] arenas;
        SimWorld validation;

        public FusionConfirmedJournal(SimConfig config, ArenaDef[] arenas, in SimState initial)
        {
            this.config = config; this.arenas = arenas;
            World = new SimWorld(config, arenas, initial);
            Recording = InputRecording.Begin(config, arenas, initial.Match.Rotation, initial.Rng.State,
                initial.Fighter0.Position, initial.Fighter1.Position, initial.Match.Phase, config.Rules.Format());
            if (Recording.InitialChecksum != initial.ComputeChecksum())
                throw new InvalidOperationException("확정 입력 기록의 시작 상태가 다릅니다.");
        }

        public void Append(InputFrame p1, InputFrame p2)
        {
            World.Step(p1, p2);
            Recording.Append(p1, p2);
            Confirmed?.Invoke();
        }

        public byte[] CreateBatch(int first, int count)
        {
            if (count < 1 || count > BatchSize || first < 0 || first + count != Recording.Count)
                throw new ArgumentOutOfRangeException(nameof(count));
            var data = new byte[10 + count * InputFrame.Size * 2];
            data[0] = Version;
            WriteInt(data, 1, first);
            data[5] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                Recording.GetP1(first + i).Write(data, 6 + i * 8);
                Recording.GetP2(first + i).Write(data, 10 + i * 8);
            }
            WriteInt(data, data.Length - 4, unchecked((int)World.ComputeChecksum()));
            return data;
        }

        public void AcceptBatch(byte[] data)
        {
            if (data == null || data.Length < 18 || data[0] != Version || data[5] < 1 || data[5] > BatchSize
                || data.Length != 10 + data[5] * 8) throw new InvalidOperationException("확정 입력 패킷 형식 오류");
            int first = ReadInt(data, 1), count = data[5];
            if (first < 0) throw new InvalidOperationException("확정 입력 순서 오류");
            if (first + count <= Recording.Count) { DuplicateBatches++; return; }
            if (first != Recording.Count) throw new InvalidOperationException("확정 입력에 빈 구간이 있습니다.");
            validation ??= new SimWorld(config, arenas, World.State);
            validation.State = World.State;
            for (int i = 0; i < count; i++) validation.Step(InputFrame.Read(data, 6 + i * 8), InputFrame.Read(data, 10 + i * 8));
            if (validation.ComputeChecksum() != unchecked((uint)ReadInt(data, data.Length - 4)))
                throw new InvalidOperationException("확정 입력 재생 체크섬 불일치");
            // Validate before committing any replay frames, card UI transitions or one-shot effects.
            for (int i = 0; i < count; i++) Append(InputFrame.Read(data, 6 + i * 8), InputFrame.Read(data, 10 + i * 8));
            BatchesVerified++;
        }

        public InputRecording Finish()
        { Recording.Finish(World.ComputeChecksum()); return Recording; }

        static void WriteInt(byte[] b, int o, int value)
        { for (int i = 0; i < 4; i++) b[o + i] = (byte)(value >> (8 * i)); }
        static int ReadInt(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    }
}
