namespace Knockbox.Game
{
    /// <summary>
    /// 경과 시간을 누적해 고정 60틱으로 바꾼다. 한 프레임에 최대 MaxTicksPerFrame틱까지만 따라잡고
    /// 그 이상 밀린 틱은 버린다(01-architecture.md 5절). 남은 시간으로 보간 비율을 준다.
    /// </summary>
    public sealed class FixedTickClock
    {
        public const double TickSeconds = 1.0 / 60.0;
        public const int DefaultMaxTicksPerFrame = 4;

        // 1/60을 여러 번 더할 때 생기는 반올림 오차로 틱이 한 프레임 늦어지지 않게 하는 여유.
        const double Slack = 1e-9;

        double accumulator;

        public FixedTickClock(int maxTicksPerFrame = DefaultMaxTicksPerFrame)
        {
            MaxTicksPerFrame = maxTicksPerFrame < 1 ? 1 : maxTicksPerFrame;
        }

        public int MaxTicksPerFrame { get; }

        /// <summary>마지막 Advance 이후 버린 틱 수의 누적.</summary>
        public long DroppedTicks { get; private set; }

        /// <summary>다음 틱까지의 진행 비율 [0, 1). 연출 보간에 쓴다.</summary>
        public float Alpha => (float)(accumulator / TickSeconds);

        public void Reset()
        {
            accumulator = 0;
            DroppedTicks = 0;
        }

        /// <summary>경과 시간을 더하고 이번 프레임에 실행할 틱 수를 돌려준다.</summary>
        public int Advance(double elapsedSeconds)
        {
            if (elapsedSeconds > 0)
                accumulator += elapsedSeconds;

            int ticks = (int)((accumulator + Slack) / TickSeconds);
            accumulator -= ticks * TickSeconds;
            if (accumulator < 0)
                accumulator = 0;

            if (ticks > MaxTicksPerFrame)
            {
                DroppedTicks += ticks - MaxTicksPerFrame;
                ticks = MaxTicksPerFrame;
            }
            return ticks;
        }
    }
}
