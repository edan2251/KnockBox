using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 플레이 모드에서 시나리오를 MatchRunner로 재생하고, 진행하면서 expect를 평가한다.
    /// 실패는 오류 로그로 남긴다(플레이 모드 테스트가 콘솔 오류로 잡는다).
    /// 캡처 모드에서는 틱을 직접 진행하고, captures의 틱이 화면에 그려진 직후 PNG로 저장한다.
    /// </summary>
    public sealed class ScenarioPlayer : MonoBehaviour
    {
        public const int CaptureWidth = 1920;
        public const int CaptureHeight = 1080;

        [SerializeField] MatchRunner runner;
        [SerializeField] string scenarioName;
        [SerializeField] bool playOnStart;

        public ScenarioDef Current { get; private set; }
        public int FailureCount { get; private set; }

        public MatchRunner Runner
        {
            get => runner;
            set => runner = value;
        }

        public bool Finished => Current != null && runner != null && runner.World != null && runner.World.State.Tick >= Current.Ticks;

        /// <summary>프로젝트 루트의 Captures/&lt;label&gt;(Git 제외, CP를 마치면 비운다).</summary>
        public static string CaptureFolder(string label) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", label);

        void Start()
        {
            if (playOnStart && !string.IsNullOrEmpty(scenarioName))
                Play(ScenarioJson.LoadByName(scenarioName));
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.Ticked -= OnTicked;
        }

        public void Play(ScenarioDef def)
        {
            Current = def;
            FailureCount = 0;
            var config = ScenarioConfig.Create(def, runner.Tuning, runner.CombatTuning);
            var arena = runner.ResolveArena(def.Arena);
            runner.InputDelayTicks = 0;
            var presenter = runner.GetComponent<MatchPresenter>();
            if (presenter != null)
                presenter.ThemeOverride = def.Theme;
            runner.Begin(config, arena, ScenarioInputs.CreateInitialState(def, config, arena));
            runner.Player1Input = new FrameArrayInputSource(ScenarioInputs.Build(def.P1, def.Ticks));
            runner.Player2Input = new FrameArrayInputSource(ScenarioInputs.Build(def.P2, def.Ticks));
            runner.StopAtTick = def.Ticks;
            if (presenter != null)
            {
                presenter.SetOverlay(def.Overlay);
                if (presenter.Help != null)
                    presenter.Help.Visible = def.Help;
            }
            runner.Ticked -= OnTicked;
            runner.Ticked += OnTicked;
            Evaluate(runner.World.State);
        }

        void OnTicked(MatchRunner r) => Evaluate(r.World.State);

        void Evaluate(in SimState state)
        {
            if (Current == null)
                return;
            foreach (var e in Current.Expects)
            {
                if (e.Tick != state.Tick)
                    continue;
                string failure = ExpectEvaluator.Evaluate(state, e);
                if (failure == null)
                    continue;
                FailureCount++;
                Debug.LogError($"[Knockbox] 시나리오 {Current.Name}: {failure}");
            }
        }

        /// <summary>
        /// 시나리오들을 차례로 재생하며 캡처를 저장한다. 틱은 직접 진행하므로 실제 시간과 무관하다.
        /// 한 틱 진행할 때마다 한 프레임을 그려서, 연출(스프링, 먼지, 발걸음)이 실제 플레이와 같은 시간으로 흐르게 한다.
        /// 끝나면 저장한 파일 목록을 넘긴다. suffix가 있으면 파일 이름 끝에 붙는다(해상도별 캡처).
        /// </summary>
        public IEnumerator RunCaptures(IReadOnlyList<ScenarioDef> scenarios, string label, Camera camera, Action<List<string>> onDone,
            int width = CaptureWidth, int height = CaptureHeight, string suffix = "")
        {
            var saved = new List<string>();
            string folder = CaptureFolder(label);
            Directory.CreateDirectory(folder);

            foreach (var def in scenarios)
            {
                Play(def);
                runner.Running = false;
                yield return null;
                var captures = new List<ScenarioCapture>(def.Captures);
                captures.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                foreach (var capture in captures)
                {
                    while (runner.World.State.Tick < capture.Tick && runner.World.State.Tick < def.Ticks)
                    {
                        runner.StepOnce();
                        yield return null;
                    }
                    // 이번 프레임의 LateUpdate가 현재 상태를 그린 뒤에 찍는다.
                    yield return new WaitForEndOfFrame();
                    string path = Path.Combine(folder, $"{def.Name}_{capture.Name}{suffix}.png");
                    SaveCameraImage(camera, path, width, height);
                    saved.Add(path);
                }
                while (runner.World.State.Tick < def.Ticks)
                {
                    runner.StepOnce();
                    yield return null;
                }
            }
            onDone?.Invoke(saved);
        }

        public static void SaveCameraImage(Camera camera, string path, int width = CaptureWidth, int height = CaptureHeight)
        {
            var texture = RenderCamera(camera, width, height);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Destroy(texture);
        }

        /// <summary>카메라를 지정 해상도의 sRGB 렌더 텍스처로 그려 읽어 온다. 호출한 쪽이 텍스처를 지운다.</summary>
        public static Texture2D RenderCamera(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                // 화면비에 맞춘 프레이밍을 렌더 텍스처 크기로 다시 계산한다.
                var rig = camera.GetComponent<Knockbox.Presentation.CameraRig>();
                if (rig != null)
                    rig.Tick(0f);
                camera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                Destroy(rt);
            }
        }
    }
}
