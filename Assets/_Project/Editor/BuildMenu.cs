using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// 비교용 Windows 개발 빌드(CHECKPOINTS.md CP9, 06-match-and-tools.md 6절).
    /// Main 씬 하나, 디버그 단축키 포함(개발 빌드), 출력은 프로젝트 루트의 Builds/(Git 제외).
    /// </summary>
    public static class BuildMenu
    {
        public const string OutputPath = "Builds/Windows/Knockbox.exe";

        [MenuItem("Knockbox/Build/Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[Knockbox] 빌드 성공: {Path.GetFullPath(OutputPath)} ({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0}초, 경고 {summary.totalWarnings})");
            else
                Debug.LogError($"[Knockbox] 빌드 실패: {summary.result}, 오류 {summary.totalErrors}");
        }

        public const string ReplayCheckPath = "Builds/ReplayCheck/Knockbox.exe";
        const string ReplayCheckPack = "Tools/ReplayCheck";

        /// <summary>
        /// 다른 PC 결정론 확인(CR-004 온라인 1단계): 같은 개발 빌드를 Builds/ReplayCheck/에 만들고
        /// Tools/ReplayCheck/의 기록·실행 스크립트·안내·기준 추적(reference/trace.txt, 만든 PC의 빌드 실행 결과)을 옆에 복사한다.
        /// </summary>
        [MenuItem("Knockbox/Build/Windows Replay Check")]
        public static void BuildReplayCheck()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath },
                locationPathName = ReplayCheckPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            string folder = Path.GetDirectoryName(ReplayCheckPath);
            Directory.CreateDirectory(folder);
            var summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Knockbox] 재생 검사 빌드 실패: {summary.result}, 오류 {summary.totalErrors}");
                return;
            }
            foreach (string file in Directory.GetFiles(ReplayCheckPack, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(folder, Path.GetRelativePath(ReplayCheckPack, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
            Debug.Log($"[Knockbox] 재생 검사 빌드 성공: {Path.GetFullPath(ReplayCheckPath)} ({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0}초)");
        }
    }
}
