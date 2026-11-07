using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>
    /// 로컬 입력 지연(기본 0틱). 틱 t에 넣은 입력이 틱 t + Delay에 나온다. 처음 Delay틱 동안은 빈 입력.
    /// 온라인 체감을 미리 시험하는 용도다.
    /// </summary>
    public sealed class InputDelayQueue
    {
        public const int MaxDelay = 15;

        readonly InputFrame[] ring = new InputFrame[MaxDelay + 1];
        int head;

        public InputDelayQueue(int delayTicks = 0)
        {
            Reset(delayTicks);
        }

        public int Delay { get; private set; }

        /// <summary>지연 틱을 바꾸면 대기 중인 입력은 비운다.</summary>
        public void Reset(int delayTicks)
        {
            Delay = delayTicks < 0 ? 0 : delayTicks > MaxDelay ? MaxDelay : delayTicks;
            System.Array.Clear(ring, 0, ring.Length);
            head = 0;
        }

        public InputFrame Push(InputFrame current)
        {
            if (Delay == 0)
                return current;
            ring[head] = current;
            int outIndex = (head - Delay + ring.Length) % ring.Length;
            head = (head + 1) % ring.Length;
            return ring[outIndex];
        }
    }
}
