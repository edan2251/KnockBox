namespace Knockbox.Presentation
{
    /// <summary>
    /// 효과음 전체 볼륨 여유와 최종 출력 리미터(CP9 QA F-2). 효과음 15개가 모두 최대 0.85로 맞춰져 있어
    /// 명중·착지·벽꽝이 같은 순간에 겹치면 합이 2를 넘었다. 게임(OutputLimiter)과 봇 영상 녹화가 같은 계산을 쓴다.
    /// </summary>
    public static class SfxMaster
    {
        /// <summary>모든 효과음 볼륨에 곱한다. 한 소리 최대 0.85 × 0.6 ≈ 0.51, 두 소리가 겹쳐도 1 안팎.</summary>
        public const float Gain = 0.6f;

        /// <summary>이 진폭까지는 그대로 두고, 넘는 부분만 tanh로 눌러 1을 넘지 않게 한다.</summary>
        public const float Knee = 0.8f;

        public static float Limit(float sample)
        {
            float magnitude = sample < 0f ? -sample : sample;
            if (magnitude <= Knee)
                return sample;
            float over = (magnitude - Knee) / (1f - Knee);
            float limited = Knee + (1f - Knee) * (float)System.Math.Tanh(over);
            return sample < 0f ? -limited : limited;
        }
    }
}
