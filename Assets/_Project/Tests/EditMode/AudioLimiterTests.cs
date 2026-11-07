using Knockbox.Presentation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP10-3: 효과음 볼륨 여유와 최종 출력 리미터(CP9 QA F-2).</summary>
    public class AudioLimiterTests
    {
        [Test]
        public void Limit_LeavesQuietSamples_AndNeverExceedsOne()
        {
            for (float x = -SfxMaster.Knee; x <= SfxMaster.Knee; x += 0.01f)
                Assert.AreEqual(x, SfxMaster.Limit(x), 1e-6f, "무릎 아래는 그대로");
            float previous = SfxMaster.Limit(SfxMaster.Knee);
            for (float x = SfxMaster.Knee; x <= 20f; x += 0.01f)
            {
                float y = SfxMaster.Limit(x);
                Assert.LessOrEqual(y, 1f, $"{x} → {y}");
                Assert.GreaterOrEqual(y, previous - 1e-6f, "단조 증가");
                Assert.AreEqual(-y, SfxMaster.Limit(-x), 1e-6f, "부호 대칭");
                previous = y;
            }
        }

        /// <summary>
        /// CP9 봇 영상 16편에서 효과음 합의 최대는 볼륨 1 기준 2.01이었다. 볼륨 여유를 곱하면 1.21,
        /// 리미터를 거치면 1 미만이고, 효과음 하나(최대 0.85)는 무릎 아래라 그대로 들린다.
        /// </summary>
        [Test]
        public void GainAndLimiter_KeepWorstCaseBelowOne()
        {
            const float WorstMixAtFullVolume = 2.01f;
            const float SingleClipPeak = 0.85f;
            Assert.Less(SfxMaster.Limit(WorstMixAtFullVolume * SfxMaster.Gain), 1f);
            Assert.LessOrEqual(SingleClipPeak * SfxMaster.Gain, SfxMaster.Knee);
        }
    }
}
