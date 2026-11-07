using System;

namespace Knockbox.Simulation
{
    public sealed class SyncTestMismatch
    {
        public int Frame;
        public int StateTick;
        public uint ExpectedChecksum;
        public uint ActualChecksum;
        public string Fields;

        public override string ToString() =>
            $"SyncTest 불일치: 프레임 {Frame} (상태 틱 {StateTick}), 체크섬 {ExpectedChecksum:X8} → {ActualChecksum:X8}\n{Fields}";
    }

    /// <summary>
    /// 롤백 넷코드의 사전 검증 도구(01-architecture.md 6절).
    /// 매 틱 상태를 저장하고 진행한 뒤, N틱 전으로 되감아 저장된 입력으로 다시 진행해서
    /// 각 틱의 체크섬이 처음과 같은지 비교한다. 불일치가 없으면 Advance는 할당을 하지 않는다.
    /// </summary>
    public sealed class SyncTest<TSim> where TSim : class, ISimStepper
    {
        public const int DefaultRollbackTicks = 7;

        readonly TSim sim;
        readonly int rollback;
        readonly int capacity;
        readonly SimState[] saved;
        readonly InputFrame[] inputs1;
        readonly InputFrame[] inputs2;
        readonly uint[] checksums;
        int frame;

        public SyncTest(TSim sim, int rollbackTicks = DefaultRollbackTicks)
        {
            if (rollbackTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(rollbackTicks));
            this.sim = sim;
            rollback = rollbackTicks;
            capacity = rollbackTicks + 2;
            saved = new SimState[capacity];
            inputs1 = new InputFrame[capacity];
            inputs2 = new InputFrame[capacity];
            checksums = new uint[capacity];
            checksums[0] = sim.ComputeChecksum();
        }

        public int Frame => frame;
        public int RollbackTicks => rollback;
        public int MismatchCount { get; private set; }
        public SyncTestMismatch FirstMismatch { get; private set; }
        public SyncTestMismatch LastMismatch { get; private set; }

        /// <summary>한 틱 진행하고 되감기 검사를 한다. 이번 틱에 불일치가 없으면 true.</summary>
        public bool Advance(in InputFrame p1, in InputFrame p2)
        {
            int slot = frame % capacity;
            saved[slot] = sim.State;
            inputs1[slot] = p1;
            inputs2[slot] = p2;
            sim.Step(p1, p2);
            frame++;
            checksums[frame % capacity] = sim.ComputeChecksum();

            if (frame < rollback)
                return true;

            var current = sim.State;
            int start = frame - rollback;
            sim.State = saved[start % capacity];
            bool ok = true;
            for (int f = start; f < frame; f++)
            {
                int j = f % capacity;
                sim.Step(inputs1[j], inputs2[j]);
                uint actual = sim.ComputeChecksum();
                uint expected = checksums[(f + 1) % capacity];
                if (actual != expected)
                {
                    var original = f + 1 == frame ? current : saved[(f + 1) % capacity];
                    Report(f + 1, original, sim.State, expected, actual);
                    ok = false;
                    break;
                }
            }

            // 불일치가 있었더라도 이후 진행은 처음 계산한 상태에서 이어간다.
            sim.State = current;
            return ok;
        }

        void Report(int atFrame, in SimState expected, in SimState actual, uint expectedChecksum, uint actualChecksum)
        {
            var m = new SyncTestMismatch
            {
                Frame = atFrame,
                StateTick = expected.Tick,
                ExpectedChecksum = expectedChecksum,
                ActualChecksum = actualChecksum,
                Fields = StateDiff.Describe(expected, actual),
            };
            MismatchCount++;
            if (FirstMismatch == null)
                FirstMismatch = m;
            LastMismatch = m;
        }
    }
}
