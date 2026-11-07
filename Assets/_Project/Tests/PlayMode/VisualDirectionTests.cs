using System.Collections;
using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// 대비·광원과 현재 S-182의 선명한 그림자: 테마 3종을 1080p 렌더 텍스처(후처리 포함)로 그려 픽셀로 잰다.
    /// 두 파이터는 Box 가운데 발판 위(x ±1.5, y 6)에 세우고 틱을 진행하지 않는다.
    /// "밝기"는 화면 sRGB 값의 감마 휘도(Rec.601: 0.299R + 0.587G + 0.114B)다. 사용자가 Rounds 캡처에서 잰 비율과 같은 척도.
    /// 그림자는 가운데 발판(x −4~4, 윗면 y 4) 바로 아래(0, 2.5)와 같은 높이의 빛 받는 곳(−6.5, 2.5)을 비교한다.
    /// </summary>
    public class VisualDirectionTests
    {
        const int Width = 1920;
        const int Height = 1080;
        MatchRunner runner;
        MatchPresenter presenter;
        Camera camera;

        IEnumerator LoadSandbox()
        {
            yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Single);
            yield return null;
            runner = Object.FindFirstObjectByType<MatchRunner>();
            presenter = Object.FindFirstObjectByType<MatchPresenter>();
            camera = Camera.main;
            Assert.IsNotNull(runner);
            Assert.IsNotNull(presenter);
        }

        static float Linear(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);
        static float Luminance(Color c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        static float Contrast(float a, float b) => (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);
        static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        Vector2Int ToPixel(Vector2 world)
        {
            float halfH = camera.orthographicSize;
            float halfW = halfH * Width / Height;
            Vector3 c = camera.transform.position;
            return new Vector2Int(Mathf.RoundToInt((world.x - c.x + halfW) / (2f * halfW) * Width),
                Mathf.RoundToInt((world.y - c.y + halfH) / (2f * halfH) * Height));
        }

        /// <summary>반경 안 픽셀 휘도의 중앙값(먼지 입자 같은 작은 점을 피한다).</summary>
        static float MedianLuminance(Texture2D tex, Vector2Int p, int radius) => Median(tex, p, radius, Luminance);
        static float MedianLuma(Texture2D tex, Vector2Int p, int radius) => Median(tex, p, radius, Luma);

        static float Median(Texture2D tex, Vector2Int p, int radius, System.Func<Color, float> measure)
        {
            var values = new List<float>();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                values.Add(measure(tex.GetPixel(p.x + dx, p.y + dy)));
            values.Sort();
            return values[values.Count / 2];
        }

        /// <summary>테두리 벽만 있는 빈 경기장(광원 측정용).</summary>
        static ArenaDef EmptyArena()
        {
            var rows = new System.Text.StringBuilder("name: Empty\n---\n");
            for (int r = 0; r < ArenaDef.ClassicRows; r++)
            {
                for (int c = 0; c < ArenaDef.ClassicColumns; c++)
                {
                    bool border = r == 0 || r == ArenaDef.ClassicRows - 1 || c == 0 || c == ArenaDef.ClassicColumns - 1;
                    rows.Append(border ? '#' : r == ArenaDef.ClassicRows - 2 && c == 3 ? '1' : r == ArenaDef.ClassicRows - 2 && c == ArenaDef.ClassicColumns - 4 ? '2' : '.');
                }
                rows.Append('\n');
            }
            return ArenaFiles.Parse(rows.ToString(), "Empty");
        }

        IEnumerator RenderTheme(int theme, System.Action<Texture2D> measure, ArenaDef arenaOverride = null)
        {
            presenter.ThemeOverride = theme;
            presenter.SetDebugText(false);
            var config = runner.CreateConfig();
            var arena = arenaOverride ?? runner.ResolveArena("Box");
            runner.Begin(config, arena, SimState.CreateRound(config, arena, 1, new Vec2(-1.5f, 6f), new Vec2(1.5f, 6f), MatchPhase.Fighting));
            runner.Running = false;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScenarioPlayer.RenderCamera(camera, Width, Height);
            try
            {
                measure(tex);
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        [UnityTest]
        public IEnumerator EveryTheme_FightersStandOutFromLitBackground()
        {
            yield return LoadSandbox();
            var failures = new List<string>();
            for (int theme = 0; theme < 3; theme++)
            {
                int t = theme;
                yield return RenderTheme(theme, tex =>
                {
                    Assert.Less(presenter.Background.ShadowAmount(new Vector2(-3.5f, 6f)), 0.02f, "배경 비교 지점이 빛 받는 곳이어야 함");
                    float background = MedianLuminance(tex, ToPixel(new Vector2(-3.5f, 6f)), 4);
                    var body = new float[2];
                    for (int i = 0; i < 2; i++)
                    {
                        // 몸 중심에서 눈 아래쪽(눈과 외곽선을 피한 곳).
                        Vector2 center = presenter.Fighter(i).Body.transform.position;
                        body[i] = MedianLuminance(tex, ToPixel(center + new Vector2(0f, -0.2f)), 2);
                    }
                    float c1 = Contrast(body[0], background), c2 = Contrast(body[1], background), diff = Mathf.Abs(body[0] - body[1]);
                    string report = $"[Knockbox][CP11-2] 테마 {presenter.CurrentTheme.name}: 빛 받는 배경 휘도 {background:F4}, P1 {body[0]:F3}(대비 {c1:F1}:1), P2 {body[1]:F3}(대비 {c2:F1}:1), 흑백 차 {diff:F3}";
                    Debug.Log(report);
                    TestContext.WriteLine(report);
                    if (c1 < 4.5f || c2 < 3.5f || diff < 0.25f)
                        failures.Add(report);
                });
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>2026-11-04 사용자 GIF 기준으로 대체: 짙고 명확한 경계, 같은 높이 빛의 25~50%, 1080p 경계 4픽셀 이하.</summary>
        [UnityTest]
        public IEnumerator EveryTheme_ShadowIsSharpAndDark()
        {
            yield return LoadSandbox();
            var failures = new List<string>();
            for (int theme = 0; theme < 3; theme++)
            {
                yield return RenderTheme(theme, tex =>
                {
                    var shadowPoint = new Vector2(0f, 2.5f);
                    var litPoint = new Vector2(-6.5f, 2.5f);
                    Assert.Greater(presenter.Background.ShadowAmount(shadowPoint), 0.98f, "그림자 측정점은 완전한 그림자여야 함");
                    Assert.Less(presenter.Background.ShadowAmount(litPoint), 0.02f, "빛 측정점은 그림자가 없어야 함");
                    float shadow = MedianLuma(tex, ToPixel(shadowPoint), 4);
                    float lit = MedianLuma(tex, ToPixel(litPoint), 4);
                    float ratio = shadow / Mathf.Max(lit, 1e-5f);

                    // 경계 전환 폭: y 2.5에서 x −7.5 → −1.5로 가로지르며(가운데 발판 그림자의 왼쪽 경계) 열마다 세로 9픽셀의 중앙값.
                    int from = ToPixel(new Vector2(-7.5f, 2.5f)).x, to = ToPixel(new Vector2(-1.5f, 2.5f)).x, row = ToPixel(shadowPoint).y;
                    var profile = new List<float>();
                    for (int x = from; x <= to; x++)
                        profile.Add(MedianLuma(tex, new Vector2Int(x, row), 1));
                    float high = 0f, low = 0f;
                    for (int k = 0; k < 20; k++)
                    {
                        high += profile[k] / 20f;
                        low += profile[profile.Count - 1 - k] / 20f;
                    }
                    int start = -1, end = -1;
                    for (int k = 0; k < profile.Count; k++)
                    {
                        float t = (high - profile[k]) / Mathf.Max(high - low, 1e-5f);
                        if (start < 0 && t >= 0.1f)
                            start = k;
                        if (end < 0 && t >= 0.9f)
                            end = k;
                    }
                    int edgeWidth = end - start;
                    string report = $"[Knockbox][SharpShadow] 테마 {presenter.CurrentTheme.name}: 그림자 밝기 {shadow:F3}, 같은 높이 빛 {lit:F3} → {ratio * 100f:F0}%(기준 25~50%), " +
                                    $"경계 전환 폭 {edgeWidth}px(10%→90%, 기준 4 이하)";
                    Debug.Log(report);
                    TestContext.WriteLine(report);
                    if (ratio < .25f || ratio > .50f || start < 0 || end < 0 || edgeWidth > 4)
                        failures.Add(report);
                });
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// CP11-3 (3): 빈 경기장에서 광원이 화면 전체를 비춘다. 위쪽 가운데가 가장 밝고, 아래쪽 가장자리(가운데·양 구석)는 그 40~60%,
        /// 가로 줄을 따라 이웃한 측정점(0.25 유닛 간격) 사이에 계단(위쪽 가운데 밝기의 8% 넘는 차이)이 없고, 화면 어디도 그 35% 아래로 꺼지지 않는다.
        /// 8%는 배경 질감(밝기 ±3%, 이웃끼리 최대 약 6%)과 감쇠 기울기를 넘는 값이다. 원뿔 테두리 같은 경계는 수십 % 계단이라 걸린다.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryTheme_LightCoversScreenWithSmoothFalloff()
        {
            yield return LoadSandbox();
            var failures = new List<string>();
            var arena = EmptyArena();
            for (int theme = 0; theme < 3; theme++)
            {
                yield return RenderTheme(theme, tex =>
                {
                    float top = MedianLuma(tex, ToPixel(new Vector2(0f, 8.3f)), 4);
                    float bottom = MedianLuma(tex, ToPixel(new Vector2(0f, -7.7f)), 4);
                    float bottomLeft = MedianLuma(tex, ToPixel(new Vector2(-14.5f, -7.7f)), 4);
                    float bottomRight = MedianLuma(tex, ToPixel(new Vector2(14.5f, -7.7f)), 4);
                    float worstStep = 0f, darkest = float.MaxValue, brightest = 0f;
                    foreach (float y in new[] { 8.3f, 4f, 0f, -4f, -7.7f })
                    {
                        float previous = -1f;
                        for (float x = -14.5f; x <= 14.5f + 1e-3f; x += 0.25f)
                        {
                            float v = MedianLuma(tex, ToPixel(new Vector2(x, y)), 3);
                            darkest = Mathf.Min(darkest, v);
                            brightest = Mathf.Max(brightest, v);
                            if (previous >= 0f)
                                worstStep = Mathf.Max(worstStep, Mathf.Abs(v - previous));
                            previous = v;
                        }
                    }
                    float rb = bottom / top, rl = bottomLeft / top, rr = bottomRight / top;
                    string report = $"[Knockbox][CP11-3] 테마 {presenter.CurrentTheme.name}: 위 가운데 {top:F3}, 아래 가운데 {rb * 100f:F0}%, 아래 왼쪽 {rl * 100f:F0}%, 아래 오른쪽 {rr * 100f:F0}%(기준 40~60%), " +
                                    $"가장 어두운 곳 {darkest / top * 100f:F0}%, 가장 밝은 곳 {brightest / top * 100f:F0}%, 이웃 측정점 최대 차이 {worstStep / top * 100f:F1}%";
                    Debug.Log(report);
                    TestContext.WriteLine(report);
                    bool ok = rb >= 0.4f && rb <= 0.6f && rl >= 0.4f && rl <= 0.6f && rr >= 0.4f && rr <= 0.6f
                              && darkest >= 0.35f * top && brightest <= 1.05f * top && worstStep <= 0.08f * top;
                    if (!ok)
                        failures.Add(report);
                }, arena);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [UnityTest]
        public IEnumerator MovingLightReprojectsPixels_OverlappingShadowsDoNotAccumulate()
        {
            yield return LoadSandbox();
            yield return RenderTheme(2, original=>
            {
                var background=presenter.Background;
                var oldLight=background.LightPosition;
                var material=background.ShadowMaterial;
                var baseTexture=material.GetTexture("_BackgroundTex");
                var sample=ToPixel(new Vector2(0,2.5f));
                float before=MedianLuma(original,sample,1);
                var duplicate=new GameObject("Overlap test shadow");
                var projection=new ProjectedShadowMesh(duplicate,1,material,2);
                try
                {
                    projection.Begin();projection.Box(new Vector2(0,3.5f),new Vector2(4,.5f),0,oldLight,150);projection.Commit();
                    var overlapped=ScenarioPlayer.RenderCamera(camera,Width,Height);
                    try{Assert.AreEqual(before,MedianLuma(overlapped,sample,1),.005f,"겹친 그림자의 명암이 더 쌓이지 않음");}
                    finally{Object.Destroy(overlapped);}
                    projection.Begin();projection.Commit();
                    background.SetLight(oldLight+Vector2.right*4);
                    var moved=ScenarioPlayer.RenderCamera(camera,Width,Height);
                    try
                    {
                        int changed=0;int row=sample.y;
                        int start=ToPixel(new Vector2(-7.5f,2.5f)).x,end=ToPixel(new Vector2(-1.5f,2.5f)).x;
                        for(int x=start;x<=end;x++)if(Mathf.Abs(Luma(original.GetPixel(x,row))-Luma(moved.GetPixel(x,row)))>.015f)changed++;
                        Assert.Greater(changed,6,"광원 이동이 실제 경계 픽셀을 이동시킴");
                        Assert.AreSame(baseTexture,material.GetTexture("_BackgroundTex"),"타격마다 텍스처를 다시 굽지 않음");
                        System.IO.File.WriteAllBytes(PlayModeSceneScope.CapturePath("shadow-rest.png"),original.EncodeToPNG());
                        System.IO.File.WriteAllBytes(PlayModeSceneScope.CapturePath("shadow-light-offset.png"),moved.EncodeToPNG());
                        TestContext.WriteLine("light moved 4 units; changed edge pixels="+changed);
                    }
                    finally{Object.Destroy(moved);background.SetLight(oldLight);}
                }
                finally{projection.Dispose();Object.Destroy(duplicate);}
            });
            LogAssert.NoUnexpectedReceived();
        }

        float PeakLightFromHit(float damage,bool reduced)
        {
            var rig=presenter.CameraRig;rig.ResetEffects();rig.ReduceMotion=reduced;
            var hit=new SimEvent(runner.World.State.Tick,SimEventType.Hit,1,runner.World.State.Fighter1.Position,new Vec2(1,0),damage);
            typeof(MatchPresenter).GetMethod("Consume",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(presenter,new object[]{hit});
            float peak=0;
            for(int i=0;i<480;i++)
            {
                rig.Tick(1f/60);presenter.RenderFrame();
                peak=Mathf.Max(peak,rig.LightOffset.magnitude);
                Assert.Less(Vector2.Distance(presenter.Background.BaseLightPosition+rig.LightOffset,presenter.Background.LightPosition),.00001f);
            }
            Assert.AreEqual(Vector2.zero,rig.LightOffset,"타격이 끝나면 광원 원위치");
            return peak;
        }

        [UnityTest]
        public IEnumerator HitDamageDrivesLightSway_WeakStrongReducedPausedAndReset_WithoutStateChanges()
        {
            yield return LoadSandbox();yield return RenderTheme(2,tex=>{});
            uint checksum=runner.World.ComputeChecksum();
            float weak=PeakLightFromHit(4,false),strong=PeakLightFromHit(24,false),reduced=PeakLightFromHit(24,true);
            Assert.Greater(weak,.001f);Assert.Greater(strong,weak*2,"강타가 약타보다 크게 광원을 흔듦");
            Assert.AreEqual(.3f,reduced/strong,.01f,"F7은 광원도 같은 비율로 줄임");
            var rig=presenter.CameraRig;rig.ReduceMotion=false;rig.ResetEffects();rig.AddTrauma(100);rig.Tick(1f/60);
            Assert.LessOrEqual(rig.Trauma,1);Assert.LessOrEqual(rig.LightOffset.magnitude,CameraRig.MaxLightSwing);
            var paused=rig.LightOffset;rig.Tick(0);Assert.AreEqual(paused,rig.LightOffset,"일시정지 중 광원 정지");
            rig.ResetEffects();presenter.RenderFrame();
            Assert.AreEqual(Vector2.zero,rig.LightOffset);Assert.AreEqual(presenter.Background.BaseLightPosition,presenter.Background.LightPosition);
            Assert.AreEqual(checksum,runner.World.ComputeChecksum(),"타격 연출·광원은 경기 상태를 바꾸지 않음");
            TestContext.WriteLine($"light peak: weak={weak:F4}, strong={strong:F4}, reduced={reduced:F4}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LightHitMovesOutAndBackOnce_StopsWithinPointFiveFiveSeconds()
        {
            yield return LoadSandbox(); yield return RenderTheme(2, tex => { });
            var rig = presenter.CameraRig;
            for (int hit = 0; hit < 2; hit++)
            {
                int damage = hit == 0 ? 4 : 24;
                rig.ResetEffects(); rig.ReduceMotion = false;
                rig.AddTrauma(CameraRig.DamageTrauma(damage), 1);
                Assert.AreEqual(Vector2.zero, rig.LightOffset, "타격 순간 위치를 튀게 하지 않음");
                float peak = 0, peakTime = 0, previous = 0;
                bool returning = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    rig.Tick(1f / 120);
                    float t = (frame + 1) / 120f, x = rig.LightOffset.x;
                    if (x > peak) { peak = x; peakTime = t; }
                    if (x < previous - .00001f) returning = true;
                    if (returning) Assert.LessOrEqual(x, previous + .00001f, "한 번 복귀한 뒤 두 번째 흔들림이 생기지 않음");
                    Assert.GreaterOrEqual(x, 0, "단일 충격은 반대편 진자 왕복을 만들지 않음");
                    if (t > CameraRig.LightDuration + .001f) Assert.AreEqual(Vector2.zero, rig.LightOffset, "0.55초 뒤 잔진동 없음");
                    previous = x;
                }
                Assert.IsTrue(returning);
                Assert.Greater(peak, hit == 0 ? .65f : 3.3f, "크기는 유지하고 꼬리만 빠르게 정리");
                Assert.That(peakTime, Is.InRange(.08f, .14f));
                TestContext.WriteLine($"one beat damage={damage}: peak={peak:F4} at {peakTime:F3}s; one outward/return lobe, zero after {CameraRig.LightDuration:F2}s");
            }
            rig.ResetEffects(); rig.AddTrauma(.6f, 1); rig.Tick(.38f);
            var fading = rig.LightOffset;
            rig.AddTrauma(.3f, -1); rig.Tick(0);
            Assert.Less(Vector2.Distance(fading, rig.LightOffset), .00001f, "종료 페이드 중 재타격도 위치를 튀게 하지 않음");
            rig.Tick(.6f); Assert.AreEqual(Vector2.zero, rig.LightOffset);
        }

        [UnityTest]
        public IEnumerator LightSwingIsFrameRateIndependent_BoundedForRepeatedHits_AndUsesHitDirection()
        {
            yield return LoadSandbox(); yield return RenderTheme(2, tex => { });
            var rig = presenter.CameraRig;
            Vector2 reference = Vector2.zero;
            foreach (int fps in new[] { 30, 60, 144 })
            {
                rig.ResetEffects(); rig.ReduceMotion = false;
                rig.AddTrauma(CameraRig.DamageTrauma(24), 1);
                for (int i = 0; i < fps / 6; i++) rig.Tick(1f / fps);
                if (fps == 30) reference = rig.LightOffset;
                else Assert.Less(Vector2.Distance(reference, rig.LightOffset), .0001f, fps + "fps에서도 같은 1/6초 위치");
            }
            foreach (int direction in new[] { -1, 1 })
            {
                rig.ResetEffects();
                var hit = new SimEvent(runner.World.State.Tick, SimEventType.Hit, 1, runner.World.State.Fighter1.Position, new Vec2(direction, 0), 24);
                typeof(MatchPresenter).GetMethod("Consume", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(presenter, new object[] { hit });
                rig.Tick(.3f); presenter.RenderFrame();
                Assert.Greater(rig.LightOffset.x * direction, 1, "실제 명중 사건 방향으로 첫 왕복 시작");
            }
            rig.ResetEffects();
            for (int i = 0; i < 720; i++)
            {
                var beforeImpact = rig.LightOffset;
                rig.AddTrauma(1, i % 23 < 11 ? 1 : -1);
                Assert.AreEqual(beforeImpact, rig.LightOffset, "연속 타격도 기존 위치를 순간 이동시키지 않음");
                rig.Tick(i % 31 == 0 ? .2f : 1f / 60);
                Assert.LessOrEqual(Mathf.Abs(rig.LightOffset.x), CameraRig.MaxLightSwing + .0001f, "연속 명중에도 변위가 폭주하지 않음");
                Assert.LessOrEqual(Mathf.Abs(rig.LightOffset.y), 1.5f, "세로 늘어짐도 상한 유지");
                Assert.IsFalse(float.IsNaN(rig.LightOffset.x));
            }
            rig.ResetEffects(); rig.Tick(1);
            Assert.AreEqual(Vector2.zero, rig.LightOffset, "리셋은 남은 속도도 지움");
            TestContext.WriteLine($"30/60/144fps light position after 1/6s={reference}; repeated hits remained bounded");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AllMapsUseSharedMovingLight_ProjectionReusesTexturesAndBuffers()
        {
            yield return LoadSandbox();
            foreach(string name in new[]{"Brawl","Box","Chimes","Gantry","Mobile","Pit","Ruins","PropLab","Scales","Totems","Towers"})
            {
                var arena=runner.ResolveArena(name);var config=runner.CreateConfig();
                runner.Begin(config,arena,SimState.CreateRound(config,arena,1,MatchPhase.Fighting));runner.Running=false;yield return null;
                var bg=presenter.Background;uint before=runner.World.ComputeChecksum();
                var material=bg.ShadowMaterial;var texture=material.GetTexture("_BackgroundTex");
                Assert.AreSame(material,presenter.GetComponentInChildren<PropShadowView>().GetComponent<MeshRenderer>().sharedMaterial);
                Assert.AreSame(material,presenter.GetComponentInChildren<FighterShadowView>().GetComponent<MeshRenderer>().sharedMaterial);
                for(int i=0;i<20;i++)bg.SetLight(bg.BaseLightPosition+Vector2.right*(i%2==0?1:-1));
                long memory=System.GC.GetAllocatedBytesForCurrentThread();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<240;i++)bg.SetLight(bg.BaseLightPosition+new Vector2(Mathf.Sin(i*.1f)*2,0));
                watch.Stop();long allocated=System.GC.GetAllocatedBytesForCurrentThread()-memory;
                Assert.Less(allocated,2048,name+" 광원 이동 중 프레임별 관리 힙 할당 방지");
                Assert.AreSame(texture,material.GetTexture("_BackgroundTex"));Assert.AreEqual(before,runner.World.ComputeChecksum());
                TestContext.WriteLine($"{name}: 240 projection updates {watch.Elapsed.TotalMilliseconds:F2}ms, allocated {allocated}B (includes test stopwatch)");
                bg.SetLight(bg.BaseLightPosition);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MainGoIlluminatesUnshadowedBackground_AndRestoresPreparedWorldBrightness()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single); yield return null;
            runner = Object.FindFirstObjectByType<MatchRunner>();
            presenter = Object.FindFirstObjectByType<MatchPresenter>(); camera = Camera.main;
            typeof(Knockbox.Game.Online.OnlineMatch).GetMethod("Attach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            yield return null;
            var view = runner.GetComponent<Knockbox.Game.UI.FrontendController>().View;
            view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Practice")).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            runner.Running = false;
            int go = view.IntroRevealTick + CardCollectionRules.RebuildTicks + 3 * CardCollectionRules.CountBeatTicks;
            var terrain = new float[2]; var fighter = new float[2];
            var lit = new float[2, 2]; var shadow = new float[2, 2];
            var texture = presenter.Background.ShadowMaterial.GetTexture("_BackgroundTex");
            for (int frame = 0; frame < 2; frame++)
            {
                runner.World.State.Match.PhaseTick = frame == 0 ? go - 5 : go;
                uint before = runner.World.ComputeChecksum();
                yield return null; yield return new WaitForEndOfFrame();
                var cue = view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Round_start_cue")).GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual(frame == 0 ? "1" : "GO", cue.text);
                Assert.AreEqual(Color.white, cue.color, "조명이 꺼져도 카운트다운은 밝게 유지");
                Assert.AreEqual(frame == 1, presenter.Background.RoundLightOn);
                Assert.AreEqual(frame == 0 ? .65f : 1f, presenter.PreparedWorldBrightness, .0001f);
                Assert.AreEqual(before, runner.World.ComputeChecksum(), "켜짐/그림자/암전은 표시만 변경");
                Assert.AreSame(texture, presenter.Background.ShadowMaterial.GetTexture("_BackgroundTex"));
                int visibleMotes=0;
                foreach(var sprite in presenter.Background.GetComponentsInChildren<SpriteRenderer>())
                    if(sprite.name=="Mote" && sprite.enabled && sprite.color.a>.1f)visibleMotes++;
                Assert.Greater(visibleMotes,0,"Main의 불 꺼짐/켜짐 모두 반짝이는 입자가 남음");
                // 실제 GameView를 읽어 화면 전체 덮개에 의한 오염도 검출한다. 영상 촬영은 하지 않는다.
                var pixels = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    terrain[frame] = ScreenLuma(pixels, new Vector2(0, 3.5f));
                    fighter[frame] = ScreenLuma(pixels, (Vector2)presenter.Fighter(0).BodyCenter + new Vector2(-.15f, -.1f));
                    for (int side = 0; side < 2; side++)
                    {
                        float direction = side == 0 ? -1 : 1;
                        lit[frame, side] = ScreenLuma(pixels, new Vector2(direction * 8, 2));
                        shadow[frame, side] = ScreenLuma(pixels, new Vector2(direction * 4, 2));
                    }
                    System.IO.File.WriteAllBytes(PlayModeSceneScope.CapturePath(System.DateTime.Now.ToString("HHmmssfff") + (frame == 0 ? "-round-shadow.png" : "-round-go.png")), pixels.EncodeToPNG());
                }
                finally { Object.Destroy(pixels); }
            }
            Assert.Greater(terrain[1], terrain[0] * 1.12f, "준비 중 지형만 살짝 어두웠다가 GO에 원래 색으로 복귀");
            Assert.Greater(fighter[1], fighter[0] * 1.12f, "준비 중 캐릭터도 함께 감광되고 GO에 원래 색으로 복귀");
            for (int side = 0; side < 2; side++)
            {
                Assert.AreEqual(shadow[0, side], shadow[1, side], .005f, "그림자 영역은 GO에도 같은 어둠 유지");
                Assert.AreEqual(shadow[0, side], lit[0, side], .005f, "점등 전에는 빛 영역도 그림자와 같은 명암");
                Assert.Greater(lit[1, side], lit[0, side] * 2, "가려지지 않은 배경만 GO에서 밝아짐");
                TestContext.WriteLine($"GO side {side}: lit {lit[0, side]:F4}->{lit[1, side]:F4}; shadow {shadow[0, side]:F4}->{shadow[1, side]:F4}");
            }
            TestContext.WriteLine($"GO terrain {terrain[0]:F4}->{terrain[1]:F4}; fighter {fighter[0]:F4}->{fighter[1]:F4}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PreparationTint_PreservesSpriteColorsAndFlash_RestoresMaterialsOnGoRebuildAndDisable()
        {
            yield return LoadSandbox();
            var config = runner.CreateConfig(); var arena = runner.ResolveArena("PropLab");
            runner.Begin(config, arena, SimState.CreateRound(config, arena, 7, MatchPhase.Countdown));
            runner.Running = false; yield return null;
            var sprites = new List<SpriteRenderer>();
            sprites.AddRange(presenter.GetComponentInChildren<ArenaView>().GetComponentsInChildren<SpriteRenderer>(true));
            var props = presenter.GetComponentInChildren<PropView>();
            Assert.Greater(props.GetComponentsInChildren<SpriteRenderer>(true).Length, 0, "구조물도 검사 대상");
            sprites.AddRange(props.GetComponentsInChildren<SpriteRenderer>(true));
            sprites.AddRange(presenter.Fighter(0).GetComponentsInChildren<SpriteRenderer>(true));
            sprites.AddRange(presenter.Fighter(1).GetComponentsInChildren<SpriteRenderer>(true));
            presenter.Fighter(0).Flash(.2f); presenter.Fighter(0).FlashOutline(.2f);
            presenter.RenderFrame();
            var colors = new Color[sprites.Count]; var materials = new Material[sprites.Count];
            for (int i = 0; i < sprites.Count; i++) { colors[i] = sprites[i].color; materials[i] = sprites[i].sharedMaterial; }
            uint checksum = runner.World.ComputeChecksum();
            for (int repeat = 0; repeat < 12; repeat++) presenter.PresentFrontendStage(1, true, 1, 1, 1, 0, false, false);
            Assert.AreEqual(.65f, presenter.PreparedWorldBrightness, .0001f);
            for (int i = 0; i < sprites.Count; i++)
            {
                Assert.AreEqual(colors[i], sprites[i].color, "피격/외곽선/글로우의 색과 알파를 덮어쓰지 않음");
                Assert.AreNotSame(materials[i], sprites[i].sharedMaterial, "원본 대신 캐시한 감광 재질 사용");
            }
            var dimMaterial = presenter.Fighter(0).Body.sharedMaterial;
            presenter.PresentFrontendStage(1, true, 1, 1, 1, 0, false, false);
            Assert.AreSame(dimMaterial, presenter.Fighter(0).Body.sharedMaterial, "프레임마다 재질을 만들거나 감광을 누적하지 않음");
            presenter.PresentFrontendStage(1, true, 1, 1, 1, 0, false, true);
            for (int i = 0; i < sprites.Count; i++)
            {
                Assert.AreSame(materials[i], sprites[i].sharedMaterial, "GO에서 원본 공유 재질로 복귀");
                Assert.AreEqual(colors[i], sprites[i].color);
            }
            Assert.AreEqual(checksum, runner.World.ComputeChecksum());
            presenter.PresentFrontendStage(1, true, 1, 1, 1, 0, false, false);
            presenter.enabled = false;
            Assert.AreEqual(1, presenter.PreparedWorldBrightness);
            Assert.AreSame(materials[sprites.IndexOf(presenter.Fighter(0).Body)], presenter.Fighter(0).Body.sharedMaterial);
            presenter.enabled = true;
            presenter.PresentFrontendStage(1, true, 1, 1, 1, 0, false, false);
            var next = runner.ResolveArena("Box");
            runner.Begin(config, next, SimState.CreateRound(config, next, 7, MatchPhase.Countdown)); runner.Running = false;
            Assert.AreEqual(1, presenter.PreparedWorldBrightness, "맵/경기 재시작 시 이전 감광 초기화");
            Assert.AreSame(materials[sprites.IndexOf(presenter.Fighter(0).Body)], presenter.Fighter(0).Body.sharedMaterial);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MainGoPulse_BrightensOnlyLitBackground_ThenSettlesWithinPointTwoSeconds()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single); yield return null;
            runner = Object.FindFirstObjectByType<MatchRunner>();
            presenter = Object.FindFirstObjectByType<MatchPresenter>(); camera = Camera.main;
            typeof(Knockbox.Game.Online.OnlineMatch).GetMethod("Attach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            yield return null;
            var view = runner.GetComponent<Knockbox.Game.UI.FrontendController>().View;
            view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Practice")).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            runner.Running = false;
            int go = view.IntroRevealTick + CardCollectionRules.RebuildTicks + 3 * CardCollectionRules.CountBeatTicks;
            var lit = new float[4, 2]; var shadow = new float[4, 2]; var terrain = new float[4];
            var pulse = new float[4]; var offsets = new[] { 0, 6, 12, 20 };
            var motes = new List<SpriteRenderer>();
            foreach (var sprite in presenter.Background.GetComponentsInChildren<SpriteRenderer>())
                if (sprite.name == "Mote") motes.Add(sprite);
            Color[] moteColors = null;
            var texts = view.Points.Root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Color[] textColors = null;
            for (int frame = 0; frame < offsets.Length; frame++)
            {
                runner.World.State.Match.PhaseTick = go + offsets[frame];
                uint checksum = runner.World.ComputeChecksum();
                yield return null; yield return new WaitForEndOfFrame();
                Assert.AreEqual(checksum, runner.World.ComputeChecksum(), "점등 플래시는 경기 상태에 쓰지 않는다");
                pulse[frame] = presenter.Background.RoundLightPulse;
                Assert.AreEqual(0, presenter.Background.ShadowMaterial.GetFloat("_LightBoost"), "공유 그림자 재질은 밝히지 않는다");
                if (frame == 0)
                {
                    moteColors = new Color[motes.Count];
                    for (int i = 0; i < motes.Count; i++) moteColors[i] = motes[i].color;
                    textColors = new Color[texts.Length];
                    for (int i = 0; i < texts.Length; i++) textColors[i] = texts[i].color;
                }
                else
                {
                    for (int i = 0; i < motes.Count; i++) Assert.AreEqual(moteColors[i], motes[i].color, "입자 밝기 유지");
                    for (int i = 0; i < texts.Length; i++) Assert.AreEqual(textColors[i], texts[i].color, "점수 HUD 밝기 유지");
                }
                var pixels = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    terrain[frame] = ScreenLuma(pixels, new Vector2(0, 3.5f));
                    for (int side = 0; side < 2; side++)
                    {
                        float direction = side == 0 ? -1 : 1;
                        lit[frame, side] = ScreenLuma(pixels, new Vector2(direction * 8, 2));
                        shadow[frame, side] = ScreenLuma(pixels, new Vector2(direction * 4, 2));
                    }
                }
                finally { Object.Destroy(pixels); }
            }
            Assert.Greater(motes.Count, 0); Assert.Greater(texts.Length, 0);
            Assert.Greater(pulse[0], .13f, "GO에서 약 18% 밝기 오버슈트");
            Assert.Less(pulse[1], pulse[0] * .3f, "등속 페이드 대신 초반에 빠르게 돌아오며 완만하게 멎는다");
            Assert.AreEqual(0, pulse[2], .00001f); Assert.AreEqual(0, pulse[3], .00001f);
            for (int frame = 0; frame < 4; frame++)
            {
                Assert.AreEqual(terrain[3], terrain[frame], .005f, "지형을 밝히지 않는다");
                for (int side = 0; side < 2; side++)
                    Assert.AreEqual(shadow[3, side], shadow[frame, side], .005f, "실제 그림자 픽셀의 명암 유지");
            }
            for (int side = 0; side < 2; side++)
            {
                Assert.Greater(lit[0, side], lit[3, side] * 1.08f, "빛 받는 배경만 순간적으로 밝아짐");
                Assert.Greater(lit[0, side], lit[1, side]);
                Assert.AreEqual(lit[2, side], lit[3, side], .005f, "0.2초 후 정상 밝기로 정착");
                TestContext.WriteLine($"GO pulse side {side}: lit {lit[0, side]:F4}->{lit[1, side]:F4}->{lit[2, side]:F4}; shadow {shadow[0, side]:F4}->{shadow[3, side]:F4}");
            }
            presenter.Background.SetRoundLight(false);
            presenter.Background.SetRoundLightPulse(0);
            Assert.AreEqual(0, presenter.Background.RoundLightPulse, "소등 중 잔여 플래시 없음");
            presenter.Background.SetRoundLight(true);
            Assert.AreEqual(0, presenter.Background.RoundLightPulse, "다음 라운드에 이전 플래시가 남지 않음");
            LogAssert.NoUnexpectedReceived();
        }

        float ScreenLuma(Texture2D texture, Vector2 world)
        {
            var p = camera.WorldToViewportPoint(world);
            return MedianLuma(texture, new Vector2Int(Mathf.RoundToInt(p.x * texture.width), Mathf.RoundToInt(p.y * texture.height)), 2);
        }

        [UnityTest]
        public IEnumerator ResetBeforeAwakePreservesCameraDepth_AndMainPracticeRendersWorld()
        {
            // 실제 Main에서는 MatchPresenter.Awake가 CameraRig.Awake보다 먼저 올 수 있다.
            var cold = new GameObject("Camera before Awake");
            cold.SetActive(false);
            var original = new Vector3(2, 3, -10);
            cold.transform.localPosition = original;
            cold.AddComponent<Camera>();
            var coldRig = cold.AddComponent<CameraRig>();
            coldRig.ResetEffects();
            Assert.AreEqual(original, cold.transform.localPosition, "Awake 전 Reset이 미초기화 좌표로 카메라를 옮기면 안 됨");
            cold.SetActive(true);
            yield return null;
            coldRig.Tick(0);
            Assert.AreEqual(original, cold.transform.localPosition);
            Object.Destroy(cold);

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            runner = Object.FindFirstObjectByType<MatchRunner>();
            presenter = Object.FindFirstObjectByType<MatchPresenter>();
            camera = Camera.main;
            // 테스트 도중 SceneManager로 로드하면 최초 Play의 RuntimeInitialize 콜백이 재실행되지 않는다.
            typeof(Knockbox.Game.Online.OnlineMatch).GetMethod("Attach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            yield return null;
            var frontend = runner.GetComponent<Knockbox.Game.UI.FrontendController>();
            frontend.View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Practice")).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            runner.Running = false;
            for (int i = 0; i < 800 && runner.World.State.Match.Phase == MatchPhase.Countdown; i++) runner.StepOnce();
            Assert.AreEqual(MatchPhase.Fighting, runner.World.State.Match.Phase);
            yield return null;
            yield return new WaitForEndOfFrame();
            var point = camera.WorldToViewportPoint(presenter.Fighter(0).BodyCenter);
            Assert.Greater(point.z, camera.nearClipPlane, "Main의 캐릭터/경기장이 카메라 앞에 있어야 함");
            var tex = ScenarioPlayer.RenderCamera(camera, Width, Height);
            try
            {
                int bright = 0;
                foreach (var pixel in tex.GetPixels32()) if (Mathf.Max(pixel.r, pixel.g, pixel.b) > 100) bright++;
                Assert.Greater(bright, 10000, "UI 점수만 남은 검은 화면이 아니라 실제 지형/캐릭터가 렌더링됨");
                TestContext.WriteLine($"Main camera depth={camera.transform.position.z}, world pixels above threshold={bright}");
            }
            finally { Object.Destroy(tex); }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PostFx_AberrationPulsesOnKoAndRestsAtZero()
        {
            yield return LoadSandbox();
            yield return RenderTheme(0, tex => { });
            var postFx = presenter.PostFx;
            Assert.AreEqual(0f, postFx.Aberration, 1e-4f, "평소 색수차 0");
            postFx.Pulse(0.6f, 0.25f);
            postFx.Tick(0f);
            Assert.AreEqual(0.6f, postFx.Aberration, 1e-4f);
            postFx.Tick(0.3f);
            Assert.AreEqual(0f, postFx.Aberration, 1e-4f, "0.25초 뒤 0");
            postFx.ReduceMotion = true;
            postFx.Pulse(0.6f, 0.25f);
            postFx.Tick(0f);
            Assert.AreEqual(0f, postFx.Aberration, 1e-4f, "흔들림 줄이기면 끔");
        }
    }
}
