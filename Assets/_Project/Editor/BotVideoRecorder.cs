using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Knockbox.Game;
using Knockbox.Simulation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// Knockbox/Record/Bot Round Videos: 모드별 봇(CombatBot.Smart)끼리 싸운 1라운드를 MP4(1920×1080, 60fps, 효과음)로
    /// 프로젝트 루트의 Recordings/bot_rounds/(Git 제외)에 저장한다. 비교 순서 4조합 × 맵 3개(기본 속도) + 4조합 Box(fast) = 16개.
    /// 조합마다 시드 20개를 헤드리스로 돌려 라운드 길이가 중간인 판을 고르고(골라 찍지 않기), 그 판의 입력 기록(.kbrp)과
    /// 대표 장면 PNG, index.md(라운드 길이·승자·사건 수)를 같이 남긴다.
    /// 화면은 틱마다 한 장씩 렌더 텍스처로 그려 인코딩하므로 실시간 녹화와 달리 끊김이 없고 다시 만들어도 같다.
    /// 효과음은 첫 번째 재생에서 SfxPlayer가 낸 소리를 기록해 따로 섞는다(같은 장면을 두 번 재생, 최종 체크섬으로 확인).
    /// Unity 창을 앞으로 가져와 실행한다(D-015).
    /// </summary>
    [InitializeOnLoad]
    public static class BotVideoRecorder
    {
        const string PendingKey = "Knockbox.BotVideo.Pending";
        const string LimitKey = "Knockbox.BotVideo.Limit";
        public const string Folder = "Recordings/bot_rounds";
        const int Width = 1920;
        const int Height = 1080;
        const int Fps = 60;
        const int SampleRate = 48000;
        const int SamplesPerFrame = SampleRate / Fps;
        const int Candidates = 20;
        const int MaxFightTicks = 3600;
        /// <summary>미리보기 장: 이 프레임마다(7.5fps) 384×216 칸 하나, 5 × 5 칸(약 3.3초)을 1920×1080 한 장에.</summary>
        const int SheetStep = 8;
        const int SheetColumns = 5;
        const int ThumbWidth = Width / SheetColumns;
        const int ThumbHeight = Height / SheetColumns;
        /// <summary>다음 라운드 카운트다운(READY, ROUND 2, 점수 칸)을 이만큼 보여 주고 끝낸다.</summary>
        const int TailTicks = 40;

        struct Job
        {
            public string Name;
            public CombatMode Mode;
            public bool Grab;
            public string Map;
            public bool Fast;
        }

        struct RoundResult
        {
            public uint Seed;
            public bool Ended;
            public int FightingTicks;
            public int Frames;
            public int KoFrame;
            public int FightStartFrame;
            public int Winner;
            public uint Checksum;
            public int Hit, GuardHit, Parry, Clash, WallSplat, Throw, Spike, Lunge, Swing;
            public InputRecording Recording;
        }

        static readonly string[] Maps = { "Box", "Towers", "Pit" };

        static List<Job> Jobs()
        {
            var jobs = new List<Job>();
            var combos = new[] { (CombatMode.Charge, false), (CombatMode.Charge, true), (CombatMode.Swing, false), (CombatMode.Swing, true) };
            foreach (var (mode, grab) in combos)
                foreach (string map in Maps)
                    jobs.Add(new Job { Mode = mode, Grab = grab, Map = map });
            foreach (var (mode, grab) in combos)
                jobs.Add(new Job { Mode = mode, Grab = grab, Map = "Box", Fast = true });
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                j.Name = $"{i + 1:00}_{(j.Mode == CombatMode.Charge ? "A" : "B")}_{(j.Grab ? "grab-on" : "grab-off")}_{j.Map}_{(j.Fast ? "fast" : "default")}";
                jobs[i] = j;
            }
            return jobs;
        }

        static BotVideoRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Knockbox/Record/Bot Round Videos")]
        public static void Run() => Run(0);

        /// <summary>첫 영상 하나만 만든다(화질·용량·시간 확인용).</summary>
        [MenuItem("Knockbox/Record/Bot Round Video (First Only)")]
        public static void RunFirst() => Run(1);

        static void Run(int limit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Knockbox] 플레이 모드 중에는 녹화를 시작할 수 없음");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ProjectSetup.SandboxScenePath, OpenSceneMode.Single);
            SessionState.SetInt(LimitKey, limit);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
                return;
            SessionState.SetBool(PendingKey, false);
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            var presenter = Object.FindFirstObjectByType<MatchPresenter>();
            var camera = Camera.main;
            if (runner == null || presenter == null || camera == null)
            {
                Debug.LogError("[Knockbox] Sandbox 씬에 MatchRunner, MatchPresenter 또는 Main Camera가 없음");
                EditorApplication.isPlaying = false;
                return;
            }
            runner.StartCoroutine(RecordAll(runner, presenter, camera, SessionState.GetInt(LimitKey, 0)));
        }

        static SimConfig CreateConfig(in Job job)
        {
            var movement = AssetDatabase.LoadAssetAtPath<MovementTuning>(ProjectSetup.MovementTuningPath);
            var combat = AssetDatabase.LoadAssetAtPath<CombatTuning>(job.Fast ? ProjectSetup.CombatTuningFastPath : ProjectSetup.CombatTuningPath);
            return SimConfigFactory.Create(movement, combat, job.Mode, job.Grab);
        }

        static uint BotSeed(uint seed, int fighter) => fighter == 0 ? 1000u + seed * 7u : 2000u + seed * 13u;

        static void SetBots(MatchRunner runner, uint seed)
        {
            runner.Player1Input = new BotInputSource(() => runner.World, 0, BotSeed(seed, 0));
            runner.Player2Input = new BotInputSource(() => runner.World, 1, BotSeed(seed, 1));
        }

        /// <summary>한 판을 헤드리스로 돌린다. 영상 끝(다음 라운드 READY)까지의 프레임 수와 첫 라운드의 사건 수를 센다.</summary>
        static RoundResult Simulate(SimConfig config, ArenaDef arena, CombatMode mode, uint seed, bool record)
        {
            var world = new SimWorld(config, arena, SimState.CreateRound(config, arena, seed, MatchPhase.Countdown));
            var p1 = new BotInputSource(() => world, 0, BotSeed(seed, 0));
            var p2 = new BotInputSource(() => world, 1, BotSeed(seed, 1));
            var r = new RoundResult { Seed = seed, KoFrame = -1, FightStartFrame = -1, Winner = -1 };
            if (record)
            {
                ref readonly var s = ref world.State;
                r.Recording = InputRecording.Begin(config, arena, s.Rng.State, s.Fighter0.Position, s.Fighter1.Position, s.Match.Phase, mode.ToString());
            }
            bool roundOver = false;
            for (int frame = 1; ; frame++)
            {
                if (world.State.Match.Phase == MatchPhase.Fighting)
                {
                    if (r.FightStartFrame < 0)
                        r.FightStartFrame = frame - 1;
                    if (++r.FightingTicks > MaxFightTicks)
                        return r;
                }
                int tick = world.State.Tick;
                var a = p1.Poll(tick);
                var b = p2.Poll(tick);
                r.Recording?.Append(a, b);
                world.Step(a, b);
                for (int i = 0; i < world.Events.Count && !roundOver; i++)
                {
                    switch (world.Events[i].Type)
                    {
                        case SimEventType.Hit: r.Hit++; break;
                        case SimEventType.GuardHit: r.GuardHit++; break;
                        case SimEventType.Parry: r.Parry++; break;
                        case SimEventType.Clash: r.Clash++; break;
                        case SimEventType.WallSplat: r.WallSplat++; break;
                        case SimEventType.Throw: r.Throw++; break;
                        case SimEventType.Spike: r.Spike++; break;
                        case SimEventType.LungeStart: r.Lunge++; break;
                        case SimEventType.AttackStart: r.Swing++; break;
                        case SimEventType.KO:
                            if (r.KoFrame < 0)
                                r.KoFrame = frame;
                            break;
                        case SimEventType.RoundEnd:
                            roundOver = true;
                            r.Ended = true;
                            r.Winner = world.State.Match.RoundWinner;
                            break;
                    }
                }
                if (roundOver && world.State.Match.Phase == MatchPhase.Countdown && world.State.Match.PhaseTick >= TailTicks)
                {
                    r.Frames = frame;
                    r.Checksum = world.ComputeChecksum();
                    r.Recording?.Finish(r.Checksum);
                    return r;
                }
            }
        }

        static IEnumerator RecordAll(MatchRunner runner, MatchPresenter presenter, Camera camera, int limit)
        {
            Directory.CreateDirectory(Folder);
            float listenerVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            presenter.SetOverlay(false);
            if (presenter.Help != null)
                presenter.Help.Visible = false;
            presenter.SetDebugLabels(true);
            presenter.SetFighterLabels(false);
            runner.InputDelayTicks = 0;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var frameTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var thumbRt = new RenderTexture(ThumbWidth, ThumbHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var sheet = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            Directory.CreateDirectory(Path.Combine(Folder, "sheets"));
            var index = new StringBuilder();
            var jobs = Jobs();
            if (limit > 0 && jobs.Count > limit)
                jobs.RemoveRange(limit, jobs.Count - limit);
            int failures = 0;
            var started = System.DateTime.Now;
            index.AppendLine("# 봇 라운드 영상");
            index.AppendLine();
            index.AppendLine("`Knockbox/Record/Bot Round Videos`로 만든 영상. 모드별 봇(CombatBot.Smart)끼리 싸운 1라운드(READY부터 다음 라운드 READY까지), 1920×1080 60fps, 효과음 포함.");
            index.AppendLine($"조합마다 시드 {Candidates}개를 돌려 라운드 길이가 **중간인 판**을 골랐다. `.kbrp`를 프로젝트 루트 `Replays/`에 복사하면 게임에서 F10으로 같은 판을 볼 수 있다.");
            index.AppendLine();
            index.AppendLine("| 영상 | 모드 | 잡기 | 맵 | 속도 | 라운드 | 승자 | 명중 | 가드 | 패링 | 클래시 | 벽꽝 | 던지기 | 가시 | 돌진/휘두르기 | 20판 라운드 길이(최소/중간/최대, 끝난 판) |");
            index.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            try
            {
                foreach (var job in jobs)
                {
                    var config = CreateConfig(job);
                    var arena = ArenaFiles.LoadByName(job.Map);

                    // 1) 시드 20개 중 라운드 길이가 중간인 판을 고른다.
                    var ended = new List<RoundResult>();
                    for (uint seed = 1; seed <= Candidates; seed++)
                    {
                        var candidate = Simulate(config, arena, job.Mode, seed, false);
                        if (candidate.Ended)
                            ended.Add(candidate);
                    }
                    if (ended.Count == 0)
                    {
                        Debug.LogError($"[Knockbox] {job.Name}: {Candidates}판 모두 {MaxFightTicks / 60}초 안에 끝나지 않음");
                        failures++;
                        continue;
                    }
                    ended.Sort((x, y) => x.FightingTicks != y.FightingTicks ? x.FightingTicks.CompareTo(y.FightingTicks) : x.Seed.CompareTo(y.Seed));
                    var chosen = Simulate(config, arena, job.Mode, ended[(ended.Count - 1) / 2].Seed, true);
                    string basePath = Path.Combine(Folder, job.Name);
                    File.WriteAllBytes(basePath + ReplayFiles.Extension, chosen.Recording.ToBytes());

                    // 2) 첫 재생: 효과음을 기록한다(화면은 그리지만 저장하지 않음).
                    var sounds = new List<(int frame, AudioClip clip, float volume, float pitch)>();
                    int frame = 0;
                    System.Action<AudioClip, float, float> onPlayed = (clip, volume, pitch) => sounds.Add((frame, clip, volume, pitch));
                    presenter.SfxPlayer.Played += onPlayed;
                    runner.Begin(config, arena, SimState.CreateRound(config, arena, chosen.Seed, MatchPhase.Countdown));
                    runner.Running = false;
                    SetBots(runner, chosen.Seed);
                    yield return new WaitForEndOfFrame();
                    for (frame = 1; frame <= chosen.Frames; frame++)
                    {
                        runner.StepOnce();
                        yield return new WaitForEndOfFrame();
                    }
                    presenter.SfxPlayer.Played -= onPlayed;
                    uint audioPass = runner.World.ComputeChecksum();
                    var audio = Mix(sounds, chosen.Frames + 1, out float peak, out int clipped);

                    // 3) 두 번째 재생: 틱마다 한 장씩 그려 인코딩한다.
                    string videoPath = basePath + ".mp4";
                    var videoAttributes = new VideoTrackAttributes
                    {
                        frameRate = new MediaRational(Fps),
                        width = Width,
                        height = Height,
                        includeAlpha = false,
                        bitRateMode = VideoBitrateMode.High,
                    };
                    var audioAttributes = new AudioTrackAttributes
                    {
                        sampleRate = new MediaRational(SampleRate),
                        channelCount = 2,
                        language = "en",
                    };
                    int midFrame = chosen.FightStartFrame + chosen.FightingTicks / 2;
                    int koStill = Mathf.Min(chosen.KoFrame + 30, chosen.Frames);
                    var chunk = new NativeArray<float>(SamplesPerFrame * 2, Allocator.Persistent);
                    int thumbs = 0, sheets = 0;
                    ClearSheet(sheet);
                    using (var encoder = new MediaEncoder(videoPath, videoAttributes, audioAttributes))
                    {
                        runner.Begin(config, arena, SimState.CreateRound(config, arena, chosen.Seed, MatchPhase.Countdown));
                        runner.Running = false;
                        SetBots(runner, chosen.Seed);
                        for (frame = 0; frame <= chosen.Frames; frame++)
                        {
                            if (frame > 0)
                                runner.StepOnce();
                            yield return new WaitForEndOfFrame();
                            Render(camera, rt, frameTexture);
                            encoder.AddFrame(frameTexture);
                            NativeArray<float>.Copy(audio, frame * SamplesPerFrame * 2, chunk, 0, chunk.Length);
                            encoder.AddSamples(chunk);
                            if (frame % SheetStep == 0)
                            {
                                AddThumb(rt, thumbRt, sheet, thumbs++);
                                if (thumbs == SheetColumns * SheetColumns || frame + SheetStep > chosen.Frames)
                                {
                                    sheet.Apply();
                                    File.WriteAllBytes(Path.Combine(Folder, "sheets", $"{job.Name}_{++sheets:00}.png"), sheet.EncodeToPNG());
                                    thumbs = 0;
                                    ClearSheet(sheet);
                                }
                            }
                            if (frame == midFrame)
                                File.WriteAllBytes(basePath + "_mid.png", frameTexture.EncodeToPNG());
                            else if (frame == koStill)
                                File.WriteAllBytes(basePath + "_ko.png", frameTexture.EncodeToPNG());
                        }
                    }
                    chunk.Dispose();
                    audio.Dispose();
                    uint videoPass = runner.World.ComputeChecksum();
                    bool same = audioPass == chosen.Checksum && videoPass == chosen.Checksum;
                    if (!same)
                        failures++;

                    long bytes = new FileInfo(videoPath).Length;
                    string winner = chosen.Winner == 0 ? "P1" : chosen.Winner == 1 ? "P2" : "무승부";
                    string attacks = job.Mode == CombatMode.Charge ? $"돌진 {chosen.Lunge}" : $"휘두르기 {chosen.Swing}";
                    index.AppendLine($"| [{job.Name}.mp4]({job.Name}.mp4) | {(job.Mode == CombatMode.Charge ? "A 돌진" : "B 휘두르기")} | {(job.Grab ? "켬" : "끔")} | {job.Map} | {(job.Fast ? "fast" : "default")} | " +
                                     $"{chosen.FightingTicks / 60f:F1}초 | {winner} | {chosen.Hit} | {chosen.GuardHit} | {chosen.Parry} | {chosen.Clash} | {chosen.WallSplat} | {chosen.Throw} | {chosen.Spike} | {attacks} | " +
                                     $"{ended[0].FightingTicks / 60f:F1} / {chosen.FightingTicks / 60f:F1} / {ended[ended.Count - 1].FightingTicks / 60f:F1}초, {ended.Count}/{Candidates} |");
                    Debug.Log($"[Knockbox] 봇 영상 {job.Name}: 시드 {chosen.Seed}, 라운드 {chosen.FightingTicks / 60f:F1}초, {chosen.Frames + 1}프레임({(chosen.Frames + 1) / (float)Fps:F1}초), " +
                              $"{bytes / (1024f * 1024f):F1} MB, 효과음 {sounds.Count}개(섞은 최대 {peak:F2} → 리미터 뒤 {Knockbox.Presentation.SfxMaster.Limit(peak):F2}, 0.8 위 샘플 {clipped}), " +
                              $"체크섬 헤드리스 {chosen.Checksum:X8} / 소리 재생 {audioPass:X8} / 영상 재생 {videoPass:X8}{(same ? "" : " 불일치")}");
                }
            }
            finally
            {
                rt.Release();
                Object.Destroy(rt);
                thumbRt.Release();
                Object.Destroy(thumbRt);
                Object.Destroy(sheet);
                Object.Destroy(frameTexture);
                AudioListener.volume = listenerVolume;
                presenter.SetFighterLabels(true);
            }
            index.AppendLine();
            index.AppendLine("- 봇은 사람처럼 거리를 재거나 속이지 않는다. 규칙과 손맛이 실제 싸움에서 어떻게 보이는지 보는 용도.");
            index.AppendLine("- `_mid.png`는 라운드 중간, `_ko.png`는 KO 0.5초 뒤 장면. `sheets/`는 영상 전체를 0.13초 간격 25칸(약 3.3초)씩 모은 미리보기.");
            // 한 편만 찍는 시험(First Only)은 16편 목록을 덮어쓰지 않는다.
            if (limit == 0)
                File.WriteAllText(Path.Combine(Folder, "index.md"), index.ToString(), new UTF8Encoding(false));
            string summary = $"[Knockbox] 봇 영상 {jobs.Count - failures}/{jobs.Count}개 저장: {Path.GetFullPath(Folder)} ({(System.DateTime.Now - started).TotalMinutes:F1}분)";
            if (failures > 0)
                Debug.LogError(summary + $", 실패·불일치 {failures}");
            else
                Debug.Log(summary);
            EditorApplication.isPlaying = false;
        }

        /// <summary>카메라를 1080p sRGB 렌더 텍스처에 그려 읽어 온다(ScenarioPlayer.RenderCamera와 같은 방식, 텍스처는 재사용).</summary>
        static void Render(Camera camera, RenderTexture rt, Texture2D target)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                var rig = camera.GetComponent<Knockbox.Presentation.CameraRig>();
                if (rig != null)
                    rig.Tick(0f);
                camera.Render();
                RenderTexture.active = rt;
                target.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                target.Apply();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
            }
        }

        /// <summary>방금 그린 1080p 화면을 미리보기 장의 n번째 칸(왼쪽 위부터)에 줄여 넣는다.</summary>
        static void AddThumb(RenderTexture source, RenderTexture thumb, Texture2D sheet, int n)
        {
            var previous = RenderTexture.active;
            Graphics.Blit(source, thumb);
            RenderTexture.active = thumb;
            int x = n % SheetColumns * ThumbWidth;
            int y = Height - (n / SheetColumns + 1) * ThumbHeight;
            sheet.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), x, y);
            RenderTexture.active = previous;
        }

        static void ClearSheet(Texture2D sheet)
        {
            var pixels = sheet.GetPixels32();
            var background = new Color32(24, 26, 38, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = background;
            sheet.SetPixels32(pixels);
        }

        /// <summary>기록한 효과음을 48 kHz 스테레오로 섞는다(음높이는 선형 보간 재표본화, 볼륨은 PlayOneShot 볼륨, 게임과 같은 리미터).</summary>
        static NativeArray<float> Mix(List<(int frame, AudioClip clip, float volume, float pitch)> sounds, int frames, out float peak, out int clipped)
        {
            var mix = new float[frames * SamplesPerFrame * 2];
            var cache = new Dictionary<AudioClip, float[]>();
            foreach (var (frame, clip, volume, pitch) in sounds)
            {
                if (!cache.TryGetValue(clip, out var data))
                {
                    data = new float[clip.samples * clip.channels];
                    clip.GetData(data, 0);
                    cache[clip] = data;
                }
                int channels = clip.channels;
                double step = clip.frequency * (double)pitch / SampleRate;
                double position = 0.0;
                for (int i = frame * SamplesPerFrame; i < frames * SamplesPerFrame; i++, position += step)
                {
                    int i0 = (int)position;
                    if (i0 + 1 >= clip.samples)
                        break;
                    float t = (float)(position - i0);
                    for (int c = 0; c < 2; c++)
                    {
                        int source = c < channels ? c : channels - 1;
                        float a = data[i0 * channels + source];
                        float b = data[(i0 + 1) * channels + source];
                        mix[i * 2 + c] += (a + (b - a) * t) * volume;
                    }
                }
            }
            // 게임의 OutputLimiter와 같은 리미터(볼륨 여유는 SfxPlayer가 이미 곱했다).
            peak = 0f;
            clipped = 0;
            for (int i = 0; i < mix.Length; i++)
            {
                float v = Mathf.Abs(mix[i]);
                if (v > peak)
                    peak = v;
                if (v > Knockbox.Presentation.SfxMaster.Knee)
                    clipped++;
                mix[i] = Knockbox.Presentation.SfxMaster.Limit(mix[i]);
            }
            return new NativeArray<float>(mix, Allocator.Persistent);
        }
    }
}
