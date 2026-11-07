using System;
using System.Collections;
using System.IO;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CP3-2(두 몸의 밝기 차이), CP3-3(발과 지면 오차), CP3-5(프레임 시간·연출 할당), CP3-6(연출 유무와 체크섬).
    /// 연출이 모두 연결된 Sandbox 씬을 불러 쓴다. Unity 창을 앞으로 가져와 실행한다(D-015).
    /// </summary>
    public class PresentationTests
    {
        MatchRunner runner;
        ScenarioPlayer player;
        MatchPresenter presenter;
        Camera camera;

        IEnumerator LoadSandbox()
        {
            yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Single);
            yield return null;
            runner = UnityEngine.Object.FindFirstObjectByType<MatchRunner>();
            player = UnityEngine.Object.FindFirstObjectByType<ScenarioPlayer>();
            presenter = UnityEngine.Object.FindFirstObjectByType<MatchPresenter>();
            camera = Camera.main;
            Assert.IsNotNull(runner);
            Assert.IsNotNull(player);
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(camera);
        }

        /// <summary>시나리오를 시작하고 한 틱 진행할 때마다 한 프레임을 그린다(캡처와 같은 방식).</summary>
        IEnumerator StepTo(ScenarioDef def, int tick)
        {
            player.Play(def);
            runner.Running = false;
            yield return null;
            while (runner.World.State.Tick < tick)
            {
                runner.StepOnce();
                yield return null;
            }
        }

        static float Linear(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);

        static float RelativeLuminance(Color c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);

        /// <summary>직교 카메라 기준 월드 좌표 → 렌더 텍스처 픽셀.</summary>
        static Vector2Int ToPixel(Camera cam, Vector2 world, int width, int height)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * width / height;
            Vector3 c = cam.transform.position;
            return new Vector2Int(
                Mathf.RoundToInt((world.x - c.x + halfW) / (2f * halfW) * width),
                Mathf.RoundToInt((world.y - c.y + halfH) / (2f * halfH) * height));
        }

        static Color Average(Texture2D tex, Vector2Int p, int radius)
        {
            Color sum = Color.black;
            int n = 0;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                sum += tex.GetPixel(p.x + dx, p.y + dy);
                n++;
            }
            return sum / n;
        }

        [UnityTest]
        public IEnumerator TwoBodies_LuminanceDiffersByAtLeastQuarter()
        {
            yield return LoadSandbox();
            yield return StepTo(ScenarioJson.LoadByName("showcase_cp3_presentation"), 50);
            yield return new WaitForEndOfFrame();

            const int width = 1920, height = 1080;
            var tex = ScenarioPlayer.RenderCamera(camera, width, height);
            try
            {
                var lum = new float[2];
                for (int i = 0; i < 2; i++)
                {
                    // 몸 중심에서 눈 아래쪽(눈과 외곽선을 피한 곳)을 고른다.
                    Vector2 bodyCenter = presenter.Fighter(i).Body.transform.position;
                    var pixel = ToPixel(camera, bodyCenter + new Vector2(0f, -0.2f), width, height);
                    var color = Average(tex, pixel, 2);
                    lum[i] = RelativeLuminance(color);
                    TestContext.WriteLine($"P{i + 1} 픽셀 {pixel} 색 {ColorUtility.ToHtmlStringRGB(color)} 상대 휘도 {lum[i]:F3}");
                }
                float diff = Mathf.Abs(lum[0] - lum[1]);
                string report = $"[Knockbox][CP3-2] 몸 중심 상대 휘도 P1 {lum[0]:F3}, P2 {lum[1]:F3}, 차이 {diff:F3}";
                Debug.Log(report);
                Assert.GreaterOrEqual(diff, 0.25f, report);
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        [UnityTest]
        public IEnumerator Running120Frames_FeetStayOnGround()
        {
            yield return LoadSandbox();
            var def = ScenarioJson.Parse(@"{
                ""name"": ""feet_run"", ""arena"": ""Box"", ""ticks"": 130,
                ""p1"": [ { ""from"": 0, ""to"": 59, ""hold"": [""Right""] }, { ""from"": 60, ""to"": 129, ""hold"": [""Left""] } ],
                ""p2"": [ { ""from"": 0, ""to"": 59, ""hold"": [""Left""] }, { ""from"": 60, ""to"": 129, ""hold"": [""Right""] } ]
            }");
            player.Play(def);
            runner.Running = false;
            yield return null;

            float worstPlanted = 0f, worstBelow = 0f;
            int plantedSamples = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                runner.StepOnce();
                yield return null;
                for (int i = 0; i < 2; i++)
                {
                    var f = runner.World.State.GetFighter(i);
                    if (!f.Grounded)
                        continue;
                    var rig = presenter.Fighter(i).Rig;
                    for (int side = 0; side < 2; side++)
                    {
                        float bottom = rig.DrawnFoot(side).y - rig.FootRadius;
                        float ground = -8f; // Box 바닥
                        worstBelow = Mathf.Max(worstBelow, ground - bottom);
                        if (!rig.FootPlanted(side))
                            continue;
                        plantedSamples++;
                        worstPlanted = Mathf.Max(worstPlanted, Mathf.Abs(bottom - rig.FootGroundY(side)));
                    }
                }
            }
            string report = $"[Knockbox][CP3-3] 120프레임 달리기: 디딘 발과 지면 최대 오차 {worstPlanted:F4}, 지면 아래로 들어간 최대 깊이 {worstBelow:F4}, 표본 {plantedSamples}";
            Debug.Log(report);
            Assert.Greater(plantedSamples, 100);
            Assert.LessOrEqual(worstPlanted, 0.05f, report);
            Assert.LessOrEqual(worstBelow, 0.05f, report);
        }

        [UnityTest]
        public IEnumerator DashesAndTurns_KneesAndFeetStayAboveGround()
        {
            yield return LoadSandbox();
            var def = ScenarioJson.Parse(@"{
                ""name"": ""legs_dash"", ""arena"": ""Box"", ""ticks"": 200,
                ""p1"": [ { ""from"": 0, ""to"": 40, ""hold"": [""Right""] }, { ""at"": 20, ""press"": [""Dash"", ""Right""] },
                          { ""from"": 41, ""to"": 120, ""hold"": [""Left""] }, { ""at"": 70, ""press"": [""Dash"", ""Left""] },
                          { ""at"": 140, ""press"": [""Dash"", ""Right""] }, { ""at"": 170, ""press"": [""Dash"", ""Left""] } ],
                ""p2"": [ { ""at"": 10, ""press"": [""Dash"", ""Left""] }, { ""from"": 30, ""to"": 90, ""hold"": [""Right""] }, { ""at"": 100, ""press"": [""Dash"", ""Left""] } ]
            }");
            player.Play(def);
            runner.Running = false;
            yield return null;

            float worst = 0f;
            for (int frame = 0; frame < def.Ticks; frame++)
            {
                runner.StepOnce();
                yield return null;
                for (int i = 0; i < 2; i++)
                {
                    if (!runner.World.State.GetFighter(i).Grounded)
                        continue;
                    var rig = presenter.Fighter(i).Rig;
                    for (int side = 0; side < 2; side++)
                    {
                        worst = Mathf.Max(worst, -8f - (rig.DrawnFoot(side).y - rig.FootRadius));
                        worst = Mathf.Max(worst, -8f - (rig.DrawnKnee(side).y - rig.LegRadius));
                    }
                }
            }
            string report = $"[Knockbox][CP3] 대시·방향 전환 중 무릎·발이 지면 아래로 들어간 최대 깊이 {worst:F4}";
            Debug.Log(report);
            Assert.LessOrEqual(worst, 0.05f, report);
        }

        [UnityTest]
        public IEnumerator Showcases_WithPresentation_MatchHeadlessChecksum()
        {
            yield return LoadSandbox();
            int played = 0;
            foreach (string path in ScenarioJson.ListFiles())
            {
                if (!Path.GetFileName(path).StartsWith("showcase_", StringComparison.Ordinal))
                    continue;
                var def = ScenarioJson.LoadFile(path);
                player.Play(def);
                double deadline = Time.realtimeSinceStartupAsDouble + def.Ticks / 60.0 + 5.0;
                while (!player.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.IsTrue(player.Finished, def.Name);
                Assert.AreEqual(0, player.FailureCount, def.Name);
                var headless = ScenarioRun.Run(def, ScenarioConfig.Create(def), ArenaFiles.LoadByName(def.Arena));
                Assert.AreEqual(headless.FinalChecksum, runner.World.ComputeChecksum(), $"{def.Name}: 연출을 켠 실행과 시뮬레이션만의 실행이 다름");
                played++;
            }
            Assert.Greater(played, 0);
            LogAssert.NoUnexpectedReceived();
            Debug.Log($"[Knockbox][CP3-6] 연출을 켠 Sandbox에서 showcase {played}개 재생, 최종 체크섬이 헤드리스 실행과 모두 같음");
        }

        /// <summary>
        /// CP21-5: 현재 폼 표시 세 방식 모두 시뮬레이션 체크섬에 영향이 없다(연출을 켠 재생 = 헤드리스 재생).
        /// CP21-7: 렌더링 오류 확인용 캡처를 처음 방식에서만 960×540으로 남긴다(Captures/CP21, Git 제외).
        /// </summary>
        [UnityTest]
        public IEnumerator FormDisplayModes_DoNotChangeChecksum()
        {
            yield return LoadSandbox();
            var def = ScenarioJson.LoadByName("showcase_cp21_forms");
            var headless = ScenarioRun.Run(def, ScenarioConfig.Create(def), ArenaFiles.LoadByName(def.Arena));
            Directory.CreateDirectory(ScenarioPlayer.CaptureFolder("CP21"));
            foreach (FormDisplayMode mode in Enum.GetValues(typeof(FormDisplayMode)))
            {
                presenter.FormDisplay = mode;
                player.Play(def);
                double deadline = Time.realtimeSinceStartupAsDouble + def.Ticks / 60.0 + 5.0;
                bool captured0 = false, captured1 = false;
                while (!player.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    if (mode == FormDisplayMode.Always)
                    {
                        int tick = runner.World.State.Tick;
                        if (!captured0 && tick >= def.Captures[0].Tick)
                        {
                            presenter.RenderFrame();
                            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(ScenarioPlayer.CaptureFolder("CP21"), $"{def.Captures[0].Name}.png"), 960, 540);
                            captured0 = true;
                        }
                        if (!captured1 && tick >= def.Captures[1].Tick)
                        {
                            presenter.RenderFrame();
                            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(ScenarioPlayer.CaptureFolder("CP21"), $"{def.Captures[1].Name}.png"), 960, 540);
                            captured1 = true;
                        }
                    }
                    yield return null;
                }
                Assert.IsTrue(player.Finished, $"{mode}");
                Assert.AreEqual(headless.FinalChecksum, runner.World.ComputeChecksum(), $"{mode}: 표시 방식이 시뮬레이션을 바꿈");
            }
            presenter.FormDisplay = FormDisplayMode.Always;
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// CP22-12·CP23-12: 선택 화면(초기 폼, 세트 패자)과 경기 종료 화면이 오류 없이 그려진다. 렌더링 확인용 캡처를 960×540으로 남긴다.
        /// </summary>
        [UnityTest]
        public IEnumerator PickAndMatchEndScreens_Render()
        {
            yield return LoadSandbox();
            var config = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = FeatureSwitches.Default }.WithForms(FighterForm.Scissors, FighterForm.Rock));
            var arena = ArenaFiles.LoadByName("Ruins");
            runner.Running = false;
            runner.Begin(config, arena, SimState.CreateRound(config, arena, 3, MatchPhase.FormSelect));
            string folder = ScenarioPlayer.CaptureFolder("CP23");
            Directory.CreateDirectory(folder);

            // 초기 폼 선택: P1은 아직, P2는 고름.
            var s = runner.World.State;
            s.Match.FormChoice1 = 1;
            runner.World.State = s;
            presenter.RenderFrame();
            yield return null;
            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(folder, "form_select.png"), 960, 540);

            // 세트 패자(P2) 선택: 폼 획득 둘과 증강 자리.
            s = runner.World.State;
            s.Match.SetPhase(MatchPhase.SetPick);
            s.Match.SetScore0 = 1;
            s.Match.Picker = 1;
            s.Match.PickedAt = -1;
            s.Match.Option0 = PickOption.Form(FighterForm.Scissors);
            s.Match.Option1 = PickOption.Form(FighterForm.Paper);
            s.Match.Option2 = PickOption.AugmentBase;
            runner.World.State = s;
            presenter.RenderFrame();
            yield return null;
            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(folder, "set_pick.png"), 960, 540);
            Assert.AreEqual("", presenter.Hud.Banner, "선택 목록을 가리지 않게 큰 문구 없음");
            StringAssert.StartsWith("P2 LOST THE SET", presenter.Hud.SubText);

            // 증강 발동 표시(CP23): 전투 중 머리 위에 이름이 잠깐.
            s = runner.World.State;
            s.Match.SetPhase(MatchPhase.Fighting);
            s.Match.Picker = -1;
            runner.World.State = s;
            presenter.Hud.ShowAugment(0, "SPEED");
            presenter.RenderFrame();
            yield return null;
            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(folder, "augment_icon.png"), 960, 540);
            Assert.AreEqual("SPEED", presenter.Hud.AugmentShown(0));

            s = runner.World.State;
            s.Match.SetPhase(MatchPhase.MatchEnd);
            s.Match.SetScore0 = config.Match.SetsToWinMatch;
            s.Match.SetScore1 = 2;
            runner.World.State = s;
            presenter.RenderFrame();
            yield return null;
            ScenarioPlayer.SaveCameraImage(camera, Path.Combine(folder, "match_end.png"), 960, 540);
            Assert.AreEqual("P1 WINS", presenter.Hud.Banner);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FrameTimeAndPresentationAllocations()
        {
            yield return LoadSandbox();
            presenter.SetDebugLabels(false);
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            var def = ScenarioJson.LoadByName("showcase_cp3_presentation");
            try
            {
                // 1) 연출 계층 할당: 한 틱 + 한 프레임 연출을 직접 300번 부르고 이 스레드의 할당을 잰다.
                player.Play(def);
                runner.Running = false;
                for (int i = 0; i < 30; i++)
                {
                    runner.StepOnce();
                    presenter.RenderFrame();
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 240; i++)
                {
                    runner.StepOnce();
                    presenter.RenderFrame();
                }
                long presentationBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                // 2) 실제 플레이: 1920×1080 렌더 텍스처에 그리며 300프레임의 시간과 프레임당 GC 할당(에디터 포함).
                player.Play(def);
                using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 400);
                yield return null;
                yield return null;
                float total = 0f, worst = 0f;
                const int frames = 300;
                for (int i = 0; i < frames; i++)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    total += dt;
                    worst = Mathf.Max(worst, dt);
                    if (player.Finished)
                        player.Play(def);
                }
                long gcTotal = 0, gcMax = 0;
                int gcSamples = gcRecorder.Count;
                for (int i = 0; i < gcSamples; i++)
                {
                    long v = gcRecorder.GetSample(i).Value;
                    gcTotal += v;
                    gcMax = Math.Max(gcMax, v);
                }
                float avgMs = total / frames * 1000f;
                string report = $"[Knockbox][CP3-5] 1920×1080 렌더 텍스처, {frames}프레임: 평균 {avgMs:F2} ms ({1000f / avgMs:F0} fps), 최대 {worst * 1000f:F2} ms. " +
                                $"연출 계층 할당 240프레임 {presentationBytes} B (프레임당 {presentationBytes / 240f:F1} B). " +
                                $"에디터 포함 GC 프레임당 평균 {(gcSamples > 0 ? gcTotal / gcSamples : 0)} B, 최대 {gcMax} B ({gcSamples} 표본)";
                Debug.Log(report);
                TestContext.WriteLine(report);
                Assert.Less(avgMs, 1000f / 60f, report);
                Assert.AreEqual(0, presentationBytes, report);
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}
