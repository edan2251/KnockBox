using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// Windows 빌드: 개발 빌드(시연·검수)와 배포용 빌드(STOVE 데모 제출, 2026-11-21).
    /// Main 씬 하나, 디버그 단축키 포함(개발 빌드), 출력은 프로젝트 루트의 Builds/(Git 제외).
    /// </summary>
    public static class BuildMenu
    {
        public const string OutputPath = "Builds/Windows/Knockbox.exe";

        [MenuItem("Knockbox/Build/Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            FusionPredictionPrefab.Ensure();
            BuildWindows(OutputPath, BuildOptions.Development);
        }

        static void BuildWindows(string outputPath, BuildOptions buildOptions)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = buildOptions,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[Knockbox] 빌드 성공: {Path.GetFullPath(outputPath)} ({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0}초, 경고 {summary.totalWarnings})");
            else
                throw new UnityEditor.Build.BuildFailedException($"[Knockbox] 빌드 실패: {summary.result}, 오류 {summary.totalErrors}");
        }

        public const string ReleaseFolder = "Builds/Release";
        static readonly string[] ReleaseNotices = { "docs/licenses/Paperlogy_OFL.txt", "docs/licenses/HSJiptokki_NOTICE.txt", "docs/licenses/Box2D_MIT.txt" };

        /// <summary>
        /// 배포용 Windows 빌드(2026-11-21, STOVE 데모 제출용): 개발 빌드가 아니라 디버그 단축키·개발 표시·진단 기록이 빠진다.
        /// 이름·버전은 PlayerSettings(회사 WOMBAT, 제품 KnockBox, 버전)에서 읽어 Builds/Release/KnockBox_버전_Windows/에 만들고,
        /// 배포하지 않는 Burst 디버그 정보 폴더를 지운 뒤 외부 자료 고지(Licenses/)를 넣고 같은 이름의 ZIP을 만든다.
        /// 같은 버전 폴더·ZIP은 이 메뉴가 만든 결과이므로 새로 만들 때 지운다.
        /// </summary>
        [MenuItem("Knockbox/Build/Windows Release (배포용)")]
        public static void BuildWindowsRelease()
        {
            FusionPredictionPrefab.Ensure();
            string name = $"{PlayerSettings.productName}_{PlayerSettings.bundleVersion}_Windows";
            string folder = Path.Combine(ReleaseFolder, name);
            string zip = folder + ".zip";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (File.Exists(zip)) File.Delete(zip);
            BuildWindows(Path.Combine(folder, PlayerSettings.productName + ".exe"), BuildOptions.None);
            foreach (var debugInfo in Directory.GetDirectories(folder, "*_DoNotShip"))
                Directory.Delete(debugInfo, true);
            string licenses = Path.Combine(folder, "Licenses");
            Directory.CreateDirectory(licenses);
            foreach (var notice in ReleaseNotices)
            {
                if (!File.Exists(notice)) throw new FileNotFoundException("[Knockbox] 배포 고지 파일이 없습니다.", notice);
                File.Copy(notice, Path.Combine(licenses, Path.GetFileName(notice)), true);
            }
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
            Debug.Log($"[Knockbox] 배포용 묶음: {Path.GetFullPath(zip)} ({new FileInfo(zip).Length / (1024f * 1024f):F1} MB), 버전 {PlayerSettings.bundleVersion}");
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
            FusionPredictionPrefab.Ensure();
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
