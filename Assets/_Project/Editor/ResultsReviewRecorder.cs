using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// 결과 발표 검수 촬영(2026-11-20). Main Play의 실제 Game View를 30fps MP4와 주요 순간 PNG로 남긴다(설치 없음, 에디터 MediaEncoder).
    /// - 결과 화면: 연습을 시작해 디버그 경기 승리(K/L과 같은 기능)로 P1 승리 → 결과 발표 → R 재시작 → P2 승리 → 결과 발표까지.
    /// 점수 숫자 발판(실제 물리)은 결과 단계 signDropTick부터 떨어진다.
    /// 로컬 연습 화면이며 두 PC 경기가 아니다. 저장 위치: 프로젝트 Recordings/Results-Review-시각.
    /// </summary>
    public static class ResultsReviewRecorder
    {
        public static string Status { get; private set; } = "idle";
        public static string Output { get; private set; }
        public static string Folder { get; private set; }

        [MenuItem("Knockbox/Record/Results Ceremony (Main Play, P1 승 → P2 승)")]
        public static void StartCeremony()
        {
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            if (!EditorApplication.isPlaying || runner == null || runner.Lockstep != null) throw new InvalidOperationException("Main의 로컬 Play에서만 촬영할 수 있습니다.");
            if (Status == "recording") throw new InvalidOperationException("촬영 중입니다.");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Folder = Path.GetFullPath(Path.Combine("Recordings", "Results-Review-" + stamp));
            Directory.CreateDirectory(Folder);
            Output = Path.Combine(Folder, "KNOCKBOX_Results_Ceremony_" + stamp + ".mp4");
            runner.StartCoroutine(Record(runner));
        }

        static void SeedLoserSets(SimWorld w, int winner)
        {
            if (winner == 0) w.State.Match.SetScore1 = 2; else w.State.Match.SetScore0 = 3;
        }

        static IEnumerator Record(MatchRunner runner)
        {
            var controller = runner.GetComponent<FrontendController>();
            var lobby = runner.GetComponent<OnlineLobby>();
            var toggle = typeof(FrontendController).GetMethod("TogglePodiumReady", BindingFlags.NonPublic | BindingFlags.Instance);
            int oldRate = Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
            bool oldBackground = Application.runInBackground;
            MediaEncoder encoder = null;
            Status = "recording";
            Application.runInBackground = true; Application.targetFrameRate = 30; QualitySettings.vSyncCount = 0;
            double started = Time.realtimeSinceStartupAsDouble;
            int stage = 0, winnerNow = 0, frames = 0, results = 0;
            int lastShotKey = -1;
            double videoTime = 0;
            var log = new System.Text.StringBuilder();
            try
            {
                if (controller.View.Screen == FrontendScreen.Main) lobby.StartPractice();
                while (Time.realtimeSinceStartupAsDouble - started < 75)
                {
                    var w = runner.World;
                    var m = w.State.Match;
                    if (stage == 0 && m.Phase == MatchPhase.Fighting)
                    {
                        // 진 쪽 세트 점수를 미리 넣어 "5 : 2", "3 : 5"처럼 좌우 점수가 다른 결과를 본다(연습 전용 검수).
                        SeedLoserSets(runner.World, winnerNow);
                        if (runner.TryDebugMatchWin(winnerNow)) { stage = 1; log.AppendLine($"match win P{winnerNow + 1}"); }
                    }
                    if (stage == 1 && w.InResultsSpace) { stage = 2; results++; log.AppendLine($"results #{results} winner P{SimWorld.ResultsWinner(m) + 1} sets {m.SetScore0}:{m.SetScore1}"); }
                    if (stage == 2 && w.InResultsSpace)
                    {
                        int end = w.FreeSpaceUnlockTick + 110;
                        if (m.PhaseTick >= end)
                        {
                            if (winnerNow == 1) break;
                            // 연습 재대전: R과 같은 경로(짧은 준비 표시 뒤 새 경기).
                            toggle.Invoke(controller, null);
                            log.AppendLine("practice R (rematch)");
                            stage = 3;
                        }
                    }
                    if (stage == 3 && !w.InResultsSpace) { winnerNow = 1; stage = 0; }

                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    try
                    {
                        double elapsed = Time.realtimeSinceStartupAsDouble - started;
                        if (encoder == null)
                            encoder = new MediaEncoder(Output, new VideoTrackAttributes { frameRate = new MediaRational(30), width = (uint)texture.width, height = (uint)texture.height, includeAlpha = false, bitRateMode = VideoBitrateMode.High });
                        // 결과 공간 앞뒤만 남긴다(연습 대기 화면은 건너뛴다).
                        bool keep = w.InResultsSpace || stage == 3 || (stage == 1);
                        if (keep)
                        {
                            encoder.AddFrame(texture, new MediaTime((long)(videoTime * 1000000), 1000000, 1));
                            videoTime += Mathf.Max(1f / 60f, Time.unscaledDeltaTime);
                            frames++;
                        }
                        if (stage == 2 && w.InResultsSpace)
                        {
                            int t = m.PhaseTick;
                            var s = runner.ResultsMap.ResultsStage;
                            int[] marks = { 20, 58, 72, 90, s.winnerDropTick + 10, s.loserDropTick + s.fighterFallTicks + 4, s.signDropTick + 12, s.signDropTick + 27, s.signDropTick + 38, w.FreeSpaceUnlockTick + 60 };
                            for (int i = 0; i < marks.Length; i++)
                            {
                                int key = results * 100 + i;
                                if (t >= marks[i] && key > lastShotKey)
                                {
                                    lastShotKey = key;
                                    string name = $"results{results}_P{SimWorld.ResultsWinner(m) + 1}win_{i:00}_tick{t}.png";
                                    File.WriteAllBytes(Path.Combine(Folder, name), texture.EncodeToPNG());
                                }
                            }
                        }
                    }
                    finally { Object.Destroy(texture); }
                }
            }
            finally
            {
                encoder?.Dispose();
                Application.targetFrameRate = oldRate; QualitySettings.vSyncCount = oldVsync; Application.runInBackground = oldBackground;
                Status = "complete";
                File.WriteAllText(Path.Combine(Folder, "status.txt"), $"{Status}\n{Output}\nframes={frames}\n{log}\nGame View {Screen.width}x{Screen.height}. Local practice in Editor Play (not a 2PC match).");
            }
        }
    }
}
