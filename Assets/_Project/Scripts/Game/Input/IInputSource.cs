using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>
    /// 한 플레이어의 틱별 입력원. 로컬 장치, 시나리오 재생, 허수아비, (나중에) 네트워크 입력이 구현한다.
    /// tick은 라운드 상태의 State.Tick이며, 반환한 입력은 그 틱에서 다음 틱으로 가는 Step에 쓰인다.
    /// </summary>
    public interface IInputSource
    {
        InputFrame Poll(int tick);
    }

    public sealed class IdleInputSource : IInputSource
    {
        public InputFrame Poll(int tick) => default;
    }

    /// <summary>고정 시드 무작위 허수아비.</summary>
    public sealed class RandomInputSource : IInputSource
    {
        RandomInputGenerator generator;

        public RandomInputSource(uint seed, int maxHoldTicks = 20)
        {
            generator = new RandomInputGenerator(seed, maxHoldTicks);
        }

        public InputFrame Poll(int tick) => generator.Next();
    }

    /// <summary>미리 펼친 프레임 배열을 재생한다. 범위를 벗어나면 빈 입력.</summary>
    public sealed class FrameArrayInputSource : IInputSource
    {
        readonly InputFrame[] frames;

        public FrameArrayInputSource(InputFrame[] frames)
        {
            this.frames = frames ?? new InputFrame[0];
        }

        public int Length => frames.Length;

        public InputFrame Poll(int tick) => tick >= 0 && tick < frames.Length ? frames[tick] : default;
    }
}
