using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>PlayMode 테스트 실행 경로가 살아 있는지 확인한다. 실제 검증은 CP3 이후에 붙는다.</summary>
    public class PlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator PlayerLoop_AdvancesFrames()
        {
            var start = Time.frameCount;
            yield return null;
            yield return null;
            Assert.Greater(Time.frameCount, start);
        }
    }
}
