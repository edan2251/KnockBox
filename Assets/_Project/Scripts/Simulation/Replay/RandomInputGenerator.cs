namespace Knockbox.Simulation
{
    /// <summary>
    /// 고정 시드 무작위 입력. 결정론 테스트와 허수아비(06 3절 "고정 시드 무작위")가 같이 쓴다.
    /// 한 입력을 1~maxHoldTicks 틱 동안 유지해서 점프·버튼 떼기 같은 의미 있는 패턴이 나오게 한다.
    /// </summary>
    public struct RandomInputGenerator
    {
        SimRng rng;
        InputFrame current;
        int holdTicksLeft;
        readonly int maxHoldTicks;

        public RandomInputGenerator(uint seed, int maxHoldTicks = 20)
        {
            rng = new SimRng(seed);
            current = default;
            holdTicksLeft = 0;
            this.maxHoldTicks = maxHoldTicks < 1 ? 1 : maxHoldTicks;
        }

        public InputFrame Next()
        {
            if (holdTicksLeft <= 0)
            {
                var buttons = InputButtons.None;
                for (int bit = 0; bit < 5; bit++)
                {
                    if (rng.Range(0, 4) == 0)
                        buttons |= (InputButtons)(1 << bit);
                }
                current = new InputFrame(rng.Range(-1, 2), rng.Range(-1, 2), buttons);
                holdTicksLeft = rng.Range(1, maxHoldTicks + 1);
            }
            holdTicksLeft--;
            return current;
        }
    }
}
