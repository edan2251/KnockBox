using System;
using System.Collections;
using System.IO;
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
    /// CP8-5(연출 물리를 켜고 끈 체크섬), CP8-6(효과음 15개와 같은 프레임 중복 제한), CP8-8(가장 바쁜 장면의 프레임 시간).
    /// 연출이 모두 연결된 Sandbox 씬을 쓴다. Unity 창을 앞으로 가져와 실행한다(D-015).
    /// </summary>
    public class MatchCompletionTests
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
        }

        /// <summary>시나리오를 한 틱에 한 프레임씩 연출과 함께 끝까지 진행하고 최종 체크섬을 돌려준다.</summary>
        IEnumerator PlayThrough(ScenarioDef def, Action<uint> done, Action<MatchPresenter> perFrame = null)
        {
            player.Play(def);
            runner.Running = false;
            yield return null;
            while (runner.World.State.Tick < def.Ticks)
            {
                runner.StepOnce();
                yield return null;
                perFrame?.Invoke(presenter);
            }
            done(runner.World.ComputeChecksum());
        }

        [UnityTest]
        public IEnumerator PresentationPhysics_OnAndOff_SameChecksum()
        {
            yield return LoadSandbox();
            var ko = ScenarioJson.LoadByName("showcase_cp8_ko");
            uint headless = ScenarioRun.Run(ko, ScenarioConfig.Create(ko), ArenaFiles.LoadByName(ko.Arena)).FinalChecksum;

            uint on = 0, off = 0;
            bool shattered = false;
            presenter.PresentationPhysics = true;
            yield return PlayThrough(ko, c => on = c, p => shattered |= p.KoShatter.Active && p.Fighter(1).Shattered);
            Assert.IsTrue(shattered, "연출 물리를 켜면 KO 파편이 생겨야 함");

            presenter.PresentationPhysics = false;
            bool anyShatter = false;
            yield return PlayThrough(ko, c => off = c, p => anyShatter |= p.KoShatter.Active);
            Assert.IsFalse(anyShatter, "끄면 파편 없음");
            Assert.AreEqual(headless, on, "연출 물리 켬 = 헤드리스");
            Assert.AreEqual(headless, off, "연출 물리 끔 = 헤드리스");

            // CP3-6 재실행: 모든 showcase를 연출 물리를 끈 채로도 돌려 헤드리스와 비교(켠 경우는 PresentationTests가 본다).
            int played = 0;
            foreach (string path in ScenarioJson.ListFiles())
            {
                if (!Path.GetFileName(path).StartsWith("showcase_", StringComparison.Ordinal))
                    continue;
                var def = ScenarioJson.LoadFile(path);
                uint expected = ScenarioRun.Run(def, ScenarioConfig.Create(def), ArenaFiles.LoadByName(def.Arena)).FinalChecksum;
                uint got = 0;
                yield return PlayThrough(def, c => got = c);
                Assert.AreEqual(expected, got, $"{def.Name}: 연출 물리를 끈 실행이 헤드리스와 다름");
                played++;
            }
            presenter.PresentationPhysics = true;
            string report = $"[Knockbox][CP8-5] KO 장면 체크섬 켬 {on:X8} / 끔 {off:X8} / 헤드리스 {headless:X8}, 연출 물리를 끈 showcase {played}개도 헤드리스와 같음";
            Debug.Log(report);
            TestContext.WriteLine(report);
        }

        [UnityTest]
        public IEnumerator EverySfx_HasClip_AndSameFrameDuplicatesAreDropped()
        {
            yield return LoadSandbox();
            var library = presenter.Library;
            Assert.IsNotNull(library, "Sandbox의 MatchPresenter에 효과음 묶음이 연결돼 있어야 함");
            var names = Enum.GetNames(typeof(Sfx));
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
                Assert.IsNotNull(library.Get(sfx), $"{sfx} 효과음 파일이 없음");
            Assert.AreEqual(17, names.Length, "05 7절 목록 15개 + 경계 튕김(CR-002) + 끊어짐(CP15)");

            var go = new GameObject("SfxProbe");
            try
            {
                var player2 = go.AddComponent<SfxPlayer>();
                player2.Setup(library);
                Assert.IsTrue(player2.Play(Sfx.HitWeak), "첫 재생");
                Assert.IsFalse(player2.Play(Sfx.HitWeak), "같은 프레임 같은 효과음은 하나만");
                Assert.IsTrue(player2.Play(Sfx.Clash), "다른 효과음은 같은 프레임에도 재생");
                yield return null;
                Assert.IsTrue(player2.Play(Sfx.HitWeak), "다음 프레임에는 다시 재생");
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
            Debug.Log($"[Knockbox][CP8-6] 효과음 {names.Length}개 모두 연결됨({string.Join(", ", names)}), 같은 프레임 중복 재생 제한 동작");
        }

        [UnityTest]
        public IEnumerator BusiestScene_KoShatterAndParticles_Holds60Fps()
        {
            yield return LoadSandbox();
            presenter.SetDebugLabels(false);
            presenter.PresentationPhysics = true;
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            var def = ScenarioJson.LoadByName("showcase_cp8_ko");
            try
            {
                // 실시간으로 KO 장면을 재생하며 KO 단계(파편, 고리, 파편 조각, 흔들림이 겹치는 구간)의 프레임 시간을 잰다.
                player.Play(def);
                yield return null;
                float total = 0f, worst = 0f;
                int frames = 0;
                double deadline = Time.realtimeSinceStartupAsDouble + 10.0;
                while (!player.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                    var phase = runner.World.State.Match.Phase;
                    if (phase != MatchPhase.KO)
                        continue;
                    float dt = Time.unscaledDeltaTime;
                    total += dt;
                    worst = Mathf.Max(worst, dt);
                    frames++;
                }
                Assert.Greater(frames, 20, "KO 구간을 재야 함");
                float avgMs = total / frames * 1000f;
                string report = $"[Knockbox][CP8-8] KO 파편 구간 1920×1080 렌더 텍스처 {frames}프레임: 평균 {avgMs:F2} ms ({1000f / avgMs:F0} fps), 최대 {worst * 1000f:F2} ms";
                Debug.Log(report);
                TestContext.WriteLine(report);
                Assert.Less(avgMs, 1000f / 60f, report);
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
                presenter.SetDebugLabels(true);
            }
        }
    }
}
