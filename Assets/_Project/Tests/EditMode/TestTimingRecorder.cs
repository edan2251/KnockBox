using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 테스트 실행이 끝나면 테스트마다 걸린 시간과 결과를 Logs/test-timings-{모드}.csv(Git 제외)에 쓴다.
    /// 느린 테스트를 카테고리로 나누는 근거(CLAUDE.md 12절 4항: 결과는 파일로 남기고 요약만 읽는다).
    /// </summary>
    [InitializeOnLoad]
    static class TestTimingRecorder
    {
        static TestTimingRecorder()
        {
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new Callbacks());
        }

        sealed class Callbacks : ICallbacks
        {
            readonly List<ITestResultAdaptor> leaves = new List<ITestResultAdaptor>();

            public void RunStarted(ITestAdaptor testsToRun) => leaves.Clear();

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                    leaves.Add(result);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                leaves.Sort((a, b) => b.Duration.CompareTo(a.Duration));
                var csv = new StringBuilder("seconds,state,categories,name\n");
                foreach (var r in leaves)
                    csv.Append($"{r.Duration:F3},{r.TestStatus},{string.Join("|", r.Test.Categories)},\"{r.FullName}\"\n");
                string mode = result.Test.TestMode == TestMode.PlayMode ? "PlayMode" : "EditMode";
                Directory.CreateDirectory("Logs");
                File.WriteAllText($"Logs/test-timings-{mode}.csv", csv.ToString());
            }
        }
    }
}
