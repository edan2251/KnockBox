using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// MatchRunner의 상태와 이벤트를 연출 계층에 넘긴다. 연출은 상태를 읽기만 하고 시뮬레이션에 쓰지 않는다.
    /// 연출 시간은 시뮬레이션 시간((틱 + 보간 비율) / 60)으로 진행한다. 멈추면 연출도 멈추고,
    /// 캡처처럼 한 틱씩 진행해도 실제 플레이와 같은 모습이 나온다.
    /// 타격 연출(05-presentation.md 4절)은 여러 연출을 같은 순간에 겹친다: 번쩍임, 떨림, 파편, 고리, 흔들림, 확대, 효과음.
    /// </summary>
    [RequireComponent(typeof(MatchRunner))]
    public sealed class MatchPresenter : MonoBehaviour
    {
        const int EventQueueCapacity = 256;
        const float MaxFrameDt = 0.1f;
        const float Frame = 1f / 60f;
        const float PreparationBrightness = .65f;

        [SerializeField] PaletteAsset palette;
        [SerializeField] ShapeLibrary shapes;
        [SerializeField] SfxLibrary sfx;
        [SerializeField] bool showDebugLabels = true;

        [SerializeField] MatchRunner runner;
        [SerializeField] BackgroundView background;
        [SerializeField] PostFx postFx;
        [SerializeField] ArenaView arenaView;
        [SerializeField] PropView propView;
        [SerializeField] BrawlWeaponView brawlWeaponView;
        [SerializeField] PropShadowView propShadows;
        [SerializeField] FighterShadowView fighterShadows;
        /// <summary>3D 시험 외형(CR-004, 에디터 전용 로컬 설정이 있을 때만).</summary>
        FighterModelRig modelRig;
        int modelFighter = -1;
        [SerializeField] BoundaryView boundaryView;
        [SerializeField] FighterView fighter0;
        [SerializeField] FighterView fighter1;
        [SerializeField] DustPool dust;
        [SerializeField] ImpactFx fx;
        [SerializeField] HudView hud;
        [SerializeField] SfxPlayer sfxPlayer;
        [SerializeField] CameraRig cameraRig;
        [SerializeField, Range(.5f, 2f), InspectorName("연결 상자 타격 강조")]
        [Tooltip("연결 상자가 깨질 때 화면 반동과 확대의 강도입니다. 충돌·피해·관통 감속에는 영향이 없습니다.")]
        float connectorImpact = 1f;
        [SerializeField] DebugStateLabels labels;
        /// <summary>디버그 글자(상태, 틱, 설정 줄). 빌드에서는 기본으로 숨기고 F12로 켠다(08 6절). 에디터는 켠 채 시작.</summary>
        bool debugText;
        bool fighterLabelsAllowed = true;
        [SerializeField] HitboxOverlay overlay;
        [SerializeField] AimLineView aimLine;
        [SerializeField] HelpOverlay help;
        [SerializeField] KoShatter koShatter;
        readonly bool[] shattered = new bool[2];
        int countdownKey = -1;
        readonly WorldRendererBrightness worldBrightness = new WorldRendererBrightness();
        public float PreparedWorldBrightness => worldBrightness.Brightness;

        readonly SimEvent[] queue = new SimEvent[EventQueueCapacity];
        int queued;
        double lastSimTime;
        int renderedRound;
        /// <summary>지금 그려진 맵. 시뮬레이션의 현재 맵(라운드마다 바뀔 수 있다)과 다르면 다시 그린다.</summary>
        ArenaDef builtArena;

        // 디버그 글자 아래줄: 바뀔 때만 새로 만든다.
        string footer;
        string footerArena;
        SimConfig footerConfig;
        IInputSource footerP2;
        DummyBehavior footerDummy;
        string footerPreset;
        bool footerRecording;
        bool footerPlayback;

        public PaletteAsset Palette
        {
            get => palette;
            set => palette = value;
        }

        public FighterView Fighter(int index) => index == 0 ? fighter0 : fighter1;
        public ShapeLibrary Shapes => shapes;
        public DustPool Dust => dust;
        public ImpactFx Fx => fx;
        public CameraRig CameraRig => cameraRig;
        public SfxPlayer SfxPlayer => sfxPlayer;
        public bool OverlayVisible => overlay != null && overlay.gameObject.activeSelf;

        /// <summary>현재 폼 표시 방식(10 문서 5.1, HOME). 연출만 바뀐다.</summary>
        public SfxLibrary Library => sfx;
        public HudView Hud => hud;
        public HelpOverlay Help => help;
        public KoShatter KoShatter => koShatter;

        /// <summary>연출 물리(KO 파편)를 켜고 끈다. 시뮬레이션 결과와 무관해야 한다(CP8-5).</summary>
        public bool PresentationPhysics
        {
            get => koShatter == null || koShatter.PhysicsEnabled;
            set
            {
                if (koShatter != null)
                    koShatter.PhysicsEnabled = value;
            }
        }

        /// <summary>마지막 LateUpdate에서 쓴 연출 dt(초).</summary>
        public float LastDt { get; private set; }

        [SerializeField] bool authoredOwners;
        public bool AuthoredOwners => authoredOwners;

        void Awake()
        {
            runner ??= GetComponent<MatchRunner>();
            if (palette == null)
                palette = ScriptableObject.CreateInstance<PaletteAsset>();
            if (shapes == null)
                shapes = ScriptableObject.CreateInstance<ShapeLibrary>();
            shapes.Activate();

            background = Reuse(background, "Background");
            arenaView = Reuse(arenaView, "Arena");
            propView = Reuse(propView, "Props");
            propShadows = Reuse(propShadows, "PropShadows");
            propShadows.Setup(palette, ShapeLibrary.ActiveMaterial);
            fighterShadows = Reuse(fighterShadows, "FighterShadows");
            fighterShadows.Setup(palette, ShapeLibrary.ActiveMaterial);
            fighter0 = CreateFighter(0);
            fighter1 = CreateFighter(1);
            CreateTestModel();
            dust = Reuse(dust, "Dust");
            dust.Setup(palette, shapes);
            brawlWeaponView = Reuse(brawlWeaponView, "Brawl_Weapon");
            brawlWeaponView.Setup(palette, shapes);
            boundaryView = Reuse(boundaryView, "Boundary");
            boundaryView.Setup(palette, shapes);
            fx = Reuse(fx, "Impact_Fx");
            fx.Setup(palette, shapes);
            koShatter = Reuse(koShatter, "KO_Shatter");
            koShatter.Setup(palette, shapes);
            hud = Reuse(hud, "Hud");
            hud.Setup(palette, shapes);
            sfxPlayer = Reuse(sfxPlayer, "Sfx");
            sfxPlayer.Setup(sfx);
            // 최종 출력 리미터는 AudioListener와 같은 오브젝트에 있어야 전체 소리에 걸린다(F-2).
            var listener = Object.FindFirstObjectByType<AudioListener>();
            if (listener != null && listener.GetComponent<OutputLimiter>() == null)
                listener.gameObject.AddComponent<OutputLimiter>();
            if (showDebugLabels)
            {
                labels = Reuse(labels, "Debug_Labels");
                // 파이터 위 상태 글자와 위 틱 줄은 어두운 배경 위(uiSub), 아래 설정 줄은 바닥 테두리 지형 위(ink).
                labels.Setup(palette.uiSub, palette.ink);
                debugText = Application.isEditor;
            }
            overlay = Reuse(overlay, "Hitbox_Overlay");
            overlay.Setup(palette, shapes);
            aimLine = Reuse(aimLine, "Aim_Line");
            aimLine.Setup(palette, shapes);
            overlay.gameObject.SetActive(false);
            help = Reuse(help, "Help");
            help.Setup(palette, shapes, runner.BrawlExperiment);
            help.Visible = false;

            var cam = cameraRig != null ? cameraRig.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = palette.bgBase;
                cameraRig = cam.GetComponent<CameraRig>() ?? cam.gameObject.AddComponent<CameraRig>();
            }
            postFx = Reuse(postFx, "Post_Fx");
            postFx.Setup(cam);

            runner.MatchBegan += OnMatchBegan;
            runner.Ticked += OnTicked;
            if (runner.World != null)
                OnMatchBegan(runner);
        }

        void OnDestroy()
        {
            worldBrightness.Dispose();
            if (runner == null)
                return;
            runner.MatchBegan -= OnMatchBegan;
            runner.Ticked -= OnTicked;
        }

        void OnDisable() => worldBrightness.SetBrightness(1);

        T Reuse<T>(T existing, string name) where T : Component
        {
            if(existing != null)return existing;
            if(authoredOwners)throw new System.InvalidOperationException("Missing Main presentation reference: "+name);
            return Create<T>(name);
        }

        T Create<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>
        /// 3D 시험 외형(CR-004): 로컬 전용 설정(FighterModelSettings.LocalPath)이 있으면 외형 프리팹을 만들어 그 파이터의 2D 몸 그림을 숨긴다.
        /// 권리 미확인 모델이 빌드·저장소에 들어가지 않도록 에디터에서만 읽는다. 외형은 시뮬레이션 상태를 보여 주기만 한다.
        /// </summary>
        void CreateTestModel()
        {
#if UNITY_EDITOR
            // 실험 경기(Main)에서만. 측정용 장면(Sandbox 등)은 2D 외형 그대로.
            if (!runner.BrawlExperiment)
                return;
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<FighterModelSettings>(FighterModelSettings.LocalPath);
            if (settings == null || !settings.use || settings.prefab == null)
                return;
            var instance = Instantiate(settings.prefab, transform);
            modelRig = instance.GetComponent<FighterModelRig>();
            if (modelRig == null)
            {
                Destroy(instance);
                Debug.LogWarning("[Knockbox] 3D 외형 프리팹에 FighterModelRig가 없음");
                return;
            }
            modelFighter = Mathf.Clamp(settings.fighter, 0, 1);
            Fighter(modelFighter).SetBodyVisible(false);
            Debug.Log($"[Knockbox] 3D 시험 외형: {settings.prefab.name} → P{modelFighter + 1}");
#endif
        }

        FighterView CreateFighter(int index)
        {
            var view = Reuse(index == 0 ? fighter0 : fighter1, $"Fighter_{index + 1}");
            view.Setup(index, palette, shapes);
            return view;
        }

        /// <summary>시나리오가 정한 테마(0 A 민트, 1 B 사이언, 2 C 슬레이트). -1이면 맵 짝 파일이 정한다.</summary>
        public int ThemeOverride { get; set; } = -1;

        public int ThemeIndex(ArenaDef arena)
        {
            int theme = ThemeOverride >= 0 ? ThemeOverride : runner.GetMeta(arena.Name).Theme;
            return Mathf.Clamp(theme, 0, palette.ThemeCount - 1);
        }

        /// <summary>주 광원 위치: 맵 짝 파일, 없으면 기본값.</summary>
        public Vector2 LightPosition(ArenaDef arena)
        {
            var meta = runner.GetMeta(arena.Name);
            return meta.HasLight ? meta.Light : BackgroundView.DefaultLight(arena);
        }

        public ArenaTheme CurrentTheme { get; private set; }
        public BackgroundView Background => background;
        public PostFx PostFx => postFx;

        /// <summary>맵을 그린다: 테마, 배경(빛과 그림자), 지형, KO 파편 충돌, 카메라 맞춤, HUD 위치.</summary>
        void BuildArena(ArenaDef arena)
        {
            builtArena = arena;
            var meta = runner.GetMeta(arena.Name);
            CurrentTheme = ThemeOverride >= 0 ? palette.Theme(ThemeIndex(arena)) : meta.ResolveTheme(palette);
            background.Build(arena, CurrentTheme, palette, shapes, LightPosition(arena), meta.Shadow);
            propShadows.SetMaterial(background.ShadowMaterial);
            fighterShadows.SetMaterial(background.ShadowMaterial);
            // 캐릭터 그림자는 실험 경기에서만(CR-004 D/E 피드백). 이전 장면의 측정 테스트는 그대로 둔다.
            fighterShadows.SetLight(LightPosition(arena), runner.BrawlExperiment ? meta.Shadow : 0f);
            background.ResetMotes();
            arenaView.Build(arena, CurrentTheme, palette, shapes, runner.GetMeta(arena.Name).Ceiling);
            propView.Build(arena, CurrentTheme, shapes);
            propShadows.SetArena(arena, LightPosition(arena));
            boundaryView.SetArena(arena);
            koShatter.BuildArena(arena);
            if (cameraRig != null)
                cameraRig.FitArena(arena);
            hud.SetArena(arena);
            worldBrightness.Rebuild(arenaView.transform, propView.transform, fighter0.transform, fighter1.transform,
                modelRig != null ? modelRig.transform : null);
        }

        void OnMatchBegan(MatchRunner r)
        {
            BuildArena(r.World.Arena);
            postFx.ResetEffects();
            shattered[0] = shattered[1] = false;
            countdownKey = -1;
            fighter0.ResetPose();
            fighter1.ResetPose();
            dust.Clear();
            fx.Clear();
            propView.ResetFeedback();
            brawlWeaponView.Clear();
            overlay.ClearPower();
            if (cameraRig != null)
                cameraRig.ResetEffects();
            renderedRound = r.World.State.Match.Round;
            queued = 0;
            lastSimTime = SimTime();
        }

        /// <summary>틱마다 이벤트를 모아 두었다가 LateUpdate에서 연출로 소비한다.</summary>
        void OnTicked(MatchRunner r)
        {
            var events = r.World.Events;
            for (int i = 0; i < events.Count && queued < EventQueueCapacity; i++)
                queue[queued++] = events[i];
        }

        double SimTime() => (runner.World.State.Tick + (double)runner.RenderAlpha) / SimConfig.TickRate;

        /// <summary>디버그 글자를 끄면 측정(CP3-5)에서 글자 갱신 할당이 빠진다.</summary>
        public void SetDebugLabels(bool visible)
        {
            if (labels != null)
                labels.gameObject.SetActive(visible);
        }

        /// <summary>파이터 위 상태 글자만 켜고 끈다(위·아래 설정 줄은 그대로). 봇 영상은 끈다.</summary>
        public void SetFighterLabels(bool visible) => fighterLabelsAllowed = visible;

        public bool DebugTextVisible => debugText;

        /// <summary>F12: 디버그 글자 전체. 꺼져 있어도 입력 기록·재생 중에는 설정 줄(REC·REPLAY)이 보인다.</summary>
        public void SetDebugText(bool visible) => debugText = visible;

        /// <summary>F3 판정 오버레이.</summary>
        public void SetOverlay(bool visible)
        {
            if (overlay != null)
                overlay.gameObject.SetActive(visible);
        }

        void LateUpdate() => RenderFrame();

        /// <summary>한 프레임 연출. LateUpdate가 부르며, 측정 테스트가 직접 부를 수도 있다.</summary>
        public void RenderFrame()
        {
            if (runner.World == null)
                return;
            // 이전 프레임의 연출 오프셋은 IK/그림자 계산 전에 제거한다.
            fighter0.transform.localScale = fighter1.transform.localScale = Vector3.one;
            double now = SimTime();
            float dt = Mathf.Clamp((float)(now - lastSimTime), 0f, MaxFrameDt);
            lastSimTime = now;
            LastDt = dt;

            for (int i = 0; i < queued; i++)
                Consume(queue[i]);
            queued = 0;

            var world = runner.World;
            var previous = runner.PreviousState;
            var current = world.State;
            float alpha = runner.RenderAlpha;
            if (current.Match.Round != renderedRound)
            {
                // 새 라운드(또는 새 경기): 시작 위치로 순간 이동했으므로 보간하지 않고 발을 새로 디딘다.
                // 맵 순환(CP12)이면 맵도 바뀌었다.
                renderedRound = current.Match.Round;
                propView.ResetFeedback();
                if (world.Arena != builtArena)
                {
                    BuildArena(world.Arena);
                    fx.Clear();
                    dust.Clear();
                }
                fighter0.ResetPose();
                fighter1.ResetPose();
                koShatter.Clear();
                brawlWeaponView.Clear();
                shattered[0] = shattered[1] = false;
                alpha = 1f;
            }
            propView.TickFeedback(dt);
            propView.Render(previous.Props, current.Props, alpha);
            fighter0.Render(previous.Fighter0, current.Fighter0, alpha, dt, world.Arena, world.Config);
            fighter1.Render(previous.Fighter1, current.Fighter1, alpha, dt, world.Arena, world.Config);
            brawlWeaponView.Render(previous, current, alpha, dt, world.Config);
            aimLine.Render(runner, alpha);
            if (modelRig != null)
            {
                var prevModel = modelFighter == 0 ? previous.Fighter0 : previous.Fighter1;
                var curModel = modelFighter == 0 ? current.Fighter0 : current.Fighter1;
                modelRig.Render(prevModel, curModel, alpha, dt, world.Config);
            }
            UpdateKnockout(current, world.Config);
            UpdateCountdownSound(current, world.Config);
            dust.Tick(dt);
            koShatter.Tick(dt);
            background.Tick(dt);
            if (cameraRig != null)
            {
                cameraRig.Tick(dt);
                postFx.ReduceMotion = cameraRig.ReduceMotion;
            }
            // 카메라와 같은 확정 타격 사건에서 얻은 광원 위치를 세 종류의 그림자에 같은 프레임에 적용한다.
            var movingLight = background.BaseLightPosition + (cameraRig != null ? cameraRig.LightOffset : Vector2.zero);
            background.SetLight(movingLight);
            propShadows.SetLight(movingLight);
            fighterShadows.SetLight(movingLight, runner.BrawlExperiment ? runner.GetMeta(world.Arena.Name).Shadow : 0);
            propShadows.Render(propView, current.Props);
            // 몸 그림(BodySize 1 × 크기 배율)에 맞춘 그림자.
            fighterShadows.Render(fighter0.transform.localPosition, fighter1.transform.localPosition, world.Config.Movement.BodySize / .9f * .5f);
            postFx.Tick(dt);
            fx.Tick(dt, cameraRig != null ? (Vector2)cameraRig.transform.position : Vector2.zero);

            Vector2 body0 = fighter0.transform.localPosition;
            Vector2 body1 = fighter1.transform.localPosition;
            boundaryView.Render(V(current.Fighter0.Position), V(current.Fighter1.Position), world.Config.HalfBody);
            boundaryView.Tick(dt);
            hud.Render(current, body0 + new Vector2(0f, FighterView.VisualLift), body1 + new Vector2(0f, FighterView.VisualLift),
                world.Config.Match, world.Config.Combat.MaxHealth, dt);
            hud.SetFlow = !world.Config.Rules.LegacyMatch;
            hud.RenderAugments(body0 + new Vector2(0f, FighterView.VisualLift), body1 + new Vector2(0f, FighterView.VisualLift));
            if (labels != null && labels.gameObject.activeSelf)
            {
                bool recordingOrReplay = runner.Recording != null || runner.Playback != null;
                labels.SetVisibility(debugText, debugText || recordingOrReplay, debugText && fighterLabelsAllowed);
                labels.Render(current, body0, body1, Footer(world));
            }
            if (overlay.gameObject.activeSelf)
                overlay.Render(current, world.Config, dt);
            var frontend = GetComponent<UI.FrontendController>();
            if (frontend != null && frontend.View != null) frontend.View.PresentMatchFrame();
            else worldBrightness.SetBrightness(1);
        }

        /// <summary>시뮬레이션 렌더링 후 적용하는 화면 전용 이동. 지형/파이터의 판정 위치는 바꾸지 않는다.</summary>
        public void PresentFrontendStage(float progress, bool entering, float opacity, float scale0, float scale1, float rise, bool combat, bool roundLightOn = true, bool keepBackdrop = false)
        {
            arenaView.PresentTransition(progress, entering);
            float away = entering ? 1 - UI.FrontendMotion.Out(progress) : progress * progress;
            propView.transform.localPosition = propShadows.transform.localPosition = Vector3.up * (entering ? 1 : -1) * away * (runner.World.Arena.Rows + 3);
            background.SetTransitionOpacity(keepBackdrop ? 1 : opacity);
            background.SetRoundLight(roundLightOn);
            var frontend = GetComponent<UI.FrontendController>();
            bool worldVisible = frontend == null || frontend.View == null || frontend.View.Screen == UI.FrontendScreen.Hidden;
            worldBrightness.SetBrightness(entering && !roundLightOn && worldVisible ? PreparationBrightness : 1);
            bool shadowsReady = roundLightOn && (entering ? arenaView.TransitionSettled : progress <= 0);
            background.SetShadowVisibility(shadowsReady ? 1 : 0);
            propShadows.gameObject.SetActive(shadowsReady);
            var center0 = fighter0.BodyCenter; var center1 = fighter1.BodyCenter;
            fighter0.transform.localScale = Vector3.one * Mathf.Max(.0001f, scale0);
            fighter1.transform.localScale = Vector3.one * Mathf.Max(.0001f, scale1);
            if (runner.World.State.Match.Phase == MatchPhase.KO)
            {
                fighter0.transform.position += (Vector3)(center0 - fighter0.BodyCenter);
                fighter1.transform.position += (Vector3)(center1 - fighter1.BodyCenter);
            }
            fighter0.transform.localPosition += Vector3.up * rise;
            fighter1.transform.localPosition += Vector3.up * rise;
            if (modelRig != null) modelRig.gameObject.SetActive(combat || (modelFighter == 0 ? scale0 : scale1) > .01f);
            hud.FrontendFlow = runner.World.Config.Match.IntroCountdownTicks > 0;
            hud.gameObject.SetActive(combat);
            fighterShadows.gameObject.SetActive(combat || (entering && shadowsReady));
            aimLine.gameObject.SetActive(combat);
            boundaryView.gameObject.SetActive(combat);
            brawlWeaponView.gameObject.SetActive(combat);
            fx.gameObject.SetActive(opacity > .05f);
            dust.gameObject.SetActive(opacity > .05f);
            // 라운드/세트/최종 경기 종료에 공통 적용. 낙하한 조각이 그 자리에서 작은 먼지로 사라진다.
            if (!entering && progress > 0)
                koShatter.PresentDissolve(progress);
            koShatter.gameObject.SetActive(koShatter.Active);
        }

        /// <summary>
        /// KO 히트스탑 뒤 패자는 조각나고 승자는 점수로 바뀌기 전까지 통통 뛴다.
        /// 상태만 보고 정하므로 틱을 건너뛰어도 한 번만 일어난다.
        /// </summary>
        void UpdateKnockout(in SimState s, SimConfig config)
        {
            bool celebrate = s.Match.Phase == MatchPhase.KO && s.Match.PhaseTick >= config.Combat.KoHitstop
                && (!CardCollectionRules.HasCountIn(config.Match) || s.Match.PhaseTick < 97);
            fighter0.Celebrating = celebrate && s.Match.RoundWinner == 0;
            fighter1.Celebrating = celebrate && s.Match.RoundWinner == 1;
            if (s.Match.Phase != MatchPhase.KO || s.Match.PhaseTick < config.Combat.KoHitstop)
                return;
            for (int k = 0; k < 2; k++)
            {
                var f = s.GetFighter(k);
                if (f.Action != ActionState.KO || shattered[k])
                    continue;
                shattered[k] = true;
                var view = Fighter(k);
                var center = view.BodyCenter;
                koShatter.Shatter(center, palette.FighterBody(k), new Vector2(f.Velocity.X, f.Velocity.Y));
                if (koShatter.PhysicsEnabled)
                    view.SetShattered(true);
                fx.RingBurst(center, palette.FighterBody(k), 0.6f, 4f, 0.3f);
                fx.Shards(center, Vector2.up, 360f, 10, palette.FighterBody(k), 11f, 0.22f, 0.7f);
                dust.Burst(center, 4, 3f, 0f);
                AddTrauma(0.5f);
                sfxPlayer.Play(Sfx.WallSplat, 0.8f);
            }
        }

        /// <summary>카운트다운: READY가 뜰 때 짧은 신호음, GO가 뜰 때 높은 신호음.</summary>
        void UpdateCountdownSound(in SimState s, SimConfig config)
        {
            if (CardCollectionRules.HasCountIn(config.Match))
            {
                int start = config.Match.CountdownForRound(s.Match.Round) - CardCollectionRules.CountInTicks;
                int beat = s.Match.Phase == MatchPhase.Countdown && s.Match.PhaseTick >= start
                    ? Mathf.Min(3, (s.Match.PhaseTick - start) / CardCollectionRules.CountBeatTicks) : -1;
                if (beat != countdownKey && beat >= 0) sfxPlayer.Play(Sfx.Countdown, .8f, beat == 3 ? 1.5f : 1 + beat * .08f);
                countdownKey = beat;
                return;
            }
            int key = s.Match.Phase != MatchPhase.Countdown ? -1
                : s.Match.PhaseTick >= config.Match.CountdownForRound(s.Match.Round) - (config.Match.IntroCountdownTicks > 0 ? 10 : HudView.GoTicks) ? 1 : 0;
            if (key == countdownKey)
                return;
            countdownKey = key;
            if (key == 0)
                sfxPlayer.Play(Sfx.Countdown);
            else if (key == 1)
                sfxPlayer.Play(Sfx.Countdown, 1f, 1.5f);
        }

        string Footer(SimWorld world)
        {
            var p2 = runner.PracticeOpponent;
            var dummy = p2 is DummyInputSource d ? d.Behavior : DummyBehavior.Idle;
            bool recording = runner.Recording != null;
            bool playback = runner.Playback != null;
            if (footer == null || !ReferenceEquals(world.Arena.Name, footerArena) || !ReferenceEquals(world.Config, footerConfig)
                || !ReferenceEquals(p2, footerP2) || dummy != footerDummy
                || !ReferenceEquals(runner.PresetName, footerPreset) || recording != footerRecording || playback != footerPlayback)
            {
                footerPreset = runner.PresetName;
                footerRecording = recording;
                footerPlayback = playback;
                footerArena = world.Arena.Name;
                footerConfig = world.Config;
                footerP2 = p2;
                footerDummy = dummy;
                var sw = world.Config.Rules.Switches;
                footer = $"{footerArena} / Body{(sw.GrabEnabled ? " +grab" : "")}{(world.Config.Combat.Body.SurfaceMode == 1 ? " / surface D" : world.Config.Combat.Body.SurfaceMode == 2 ? " / surface E" : "")} / {footerPreset} / P2 {InputSourceName(p2)}"
                         + (recording ? " / REC" : "") + (playback ? " / REPLAY" : "");
            }
            return footer;
        }

        public static string InputSourceName(IInputSource source)
        {
            switch (source)
            {
                case KeyboardInputSource _: return "keyboard";
                case GamepadInputSource _: return "gamepad";
                case DummyInputSource d: return "dummy " + d.Behavior;
                case BrawlBot _: return "brawl bot";
                case LungePracticeBot _: return "lunge practice";
                case MouseAimInputSource _: return "mouse";
                case FrameArrayInputSource _: return "scenario";
                default: return source == null ? "none" : "idle";
            }
        }

        static Vector2 V(Vec2 v) => new Vector2(v.X, v.Y);

        void Consume(in SimEvent e)
        {
            brawlWeaponView.OnEvent(e);
            if (e.Fighter == 0)
                fighter0.OnEvent(e);
            else if (e.Fighter == 1)
                fighter1.OnEvent(e);

            float half = runner.World.Config.HalfBody;
            var feet = new Vector2(e.Position.X, e.Position.Y - half);
            // 몸 그림은 충돌 상자보다 VisualLift만큼 위에 있으므로 몸에 생기는 연출은 그만큼 올린다.
            var onBody = V(e.Position) + new Vector2(0f, FighterView.VisualLift);
            bool reduceMotion = cameraRig != null && cameraRig.ReduceMotion;
            switch (e.Type)
            {
                case SimEventType.Land:
                    // 먼지 원 2~4개(세게 떨어질수록 많이).
                    dust.Burst(feet, e.Strength > 20f ? 4 : e.Strength > 10f ? 3 : 2, 1.5f + e.Strength * 0.05f, 0f);
                    if (e.Strength > 24f && cameraRig != null)
                        cameraRig.AddTrauma(0.18f);
                    sfxPlayer.Play(Sfx.Land, Mathf.Clamp01(0.4f + e.Strength / 40f));
                    break;
                case SimEventType.Jump:
                    dust.Burst(feet, 2, 1.2f, 0f);
                    sfxPlayer.Play(Sfx.Jump);
                    break;
                case SimEventType.AirJump:
                    dust.Burst(feet, 2, 0.8f, 0f);
                    sfxPlayer.Play(Sfx.Jump, 0.8f);
                    break;
                case SimEventType.WallJump:
                    dust.Burst(new Vector2(e.Position.X - e.Direction.X * half, e.Position.Y), 3, 1.2f, e.Direction.X);
                    sfxPlayer.Play(Sfx.Jump);
                    break;
                case SimEventType.PadBounce:
                    dust.Burst(feet, 3, 1.5f, 0f);
                    fx.RingBurst(feet, palette.energySoft, 0.3f, 1.4f, 0.18f);
                    sfxPlayer.Play(Sfx.Pad);
                    break;
                case SimEventType.Spike:
                    // 가시: 위험색 파편이 위로, 고리, 흔들림.
                    fx.Shards(V(e.Position), Vector2.up, 70f, 7, palette.hit, 9f, 0.18f, 0.4f);
                    fx.RingBurst(V(e.Position), palette.hit, 0.3f, 1.6f, 0.18f);
                    AddTrauma(0.3f);
                    sfxPlayer.Play(Sfx.Spike);
                    break;
                case SimEventType.Dash:
                    dust.Burst(new Vector2(e.Position.X, e.Position.Y - half), 2, 1.6f, -e.Direction.X);
                    sfxPlayer.Play(Sfx.Dash);
                    break;
                case SimEventType.LungeStart:
                    // 돌진 발사: 뒤로 먼지, 바람 소리, 약한 흔들림(빠를수록 조금 더).
                    dust.Burst(new Vector2(e.Position.X, e.Position.Y - half), 3, 2f, -e.Direction.X);
                    AddTrauma(0.05f + e.Strength * 0.003f);
                    sfxPlayer.Play(Sfx.Dash);
                    break;
                case SimEventType.AugmentActive:
                    hud.ShowAugment(e.Fighter, Augments.TryGet(runner.World.Config, (int)e.Strength, out var augment) ? augment.Name : "AUG");
                    break;
                case SimEventType.LungeStun:
                case SimEventType.AttackInterrupted:
                    // 돌진이 무기에 맞아 끊김 / 근접공격이 돌진에 짐: 위험색 작은 고리.
                    fx.RingBurst(V(e.Position), palette.hit, 0.3f, 1.1f, 0.18f);
                    break;
                case SimEventType.PowerCheck:
                    overlay.ShowPower(e.Fighter, e.Strength);
                    break;
                case SimEventType.Grab:
                {
                    // 손이 붙는 순간: 양쪽 짧은 번쩍임, 작은 고리, 약한 흔들림.
                    int grabber = 1 - e.Fighter;
                    Fighter(grabber).Flash(2f * Frame);
                    fx.RingBurst(onBody, palette.flash, 0.2f, 1.0f, 0.12f);
                    AddTrauma(0.12f);
                    sfxPlayer.Play(Sfx.Grab);
                    break;
                }
                case SimEventType.Throw:
                    // 던지기: 크게 휘두르는 바람 소리와 흔들림. 던져진 쪽은 잔상(FighterView).
                    dust.Burst(new Vector2(e.Position.X, e.Position.Y - half), 3, 2f, -e.Direction.X);
                    fx.Shards(onBody, V(e.Direction), 50f, 4, palette.FighterBody(1 - e.Fighter), 9f, 0.18f, 0.35f);
                    AddTrauma(0.22f);
                    sfxPlayer.Play(Sfx.Throw);
                    break;
                case SimEventType.GrabBreak:
                    // 잡기 풀기: 불꽃과 고리, 양쪽 외곽선 번쩍임.
                    fx.Sparks(onBody, 8, 11f);
                    fx.RingBurst(onBody, palette.energy, 0.3f, 1.4f, 0.18f);
                    fighter0.FlashOutline(3f * Frame);
                    fighter1.FlashOutline(3f * Frame);
                    AddTrauma(0.2f);
                    sfxPlayer.Play(Sfx.Clash, 0.7f);
                    break;

                case SimEventType.Hit:
                {
                    // 맞은 쪽 4프레임·때린 쪽 3프레임 번쩍임, 파편 6~10개(hit 빨강 위주 + 공격자 몸 색 일부, 08 5절), 흰 고리,
                    // 데미지 비례 흔들림, 15 이상이면 확대와 약한 색수차.
                    int attacker = 1 - e.Fighter;
                    float damage = e.Strength;
                    Fighter(attacker).Flash(3f * Frame);
                    int shards = Mathf.Clamp(6 + Mathf.RoundToInt(damage / 4f), 6, 10);
                    fx.Shards(onBody, V(e.Direction), 75f, shards - shards / 3, palette.hit, 8f + damage * 0.35f, 0.24f);
                    fx.Shards(onBody, V(e.Direction), 60f, shards / 3, palette.FighterBody(attacker), 8f + damage * 0.35f, 0.24f);
                    fx.RingBurst(onBody, palette.flash, 0.3f, 1.5f + damage * 0.04f);
                    fx.Glow(onBody, palette.flash, 2.2f + damage * 0.06f, 0.45f, 0.2f);
                    AddTrauma(CameraRig.DamageTrauma(damage), e.Direction.X);
                    if (damage >= 15f)
                    {
                        Punch(0.015f, 6f * Frame);
                        postFx.Pulse(0.25f, 0.15f);
                    }
                    sfxPlayer.Play(damage >= 15f ? Sfx.HitStrong : Sfx.HitWeak);
                    break;
                }
                case SimEventType.PropHit:
                case SimEventType.BoxHit:
                {
                    // 구조물에 맞음(CP14): 명중과 같은 크기의 연출, 파편은 hit 빨강과 구조물 색.
                    float damage = e.Strength;
                    int shards = Mathf.Clamp(6 + Mathf.RoundToInt(damage / 4f), 6, 10);
                    fx.Shards(onBody, V(e.Direction), 75f, shards - shards / 3, palette.hit, 8f + damage * 0.35f, 0.24f);
                    fx.Shards(onBody, V(e.Direction), 60f, shards / 3, CurrentTheme.prop, 8f + damage * 0.35f, 0.24f);
                    fx.RingBurst(onBody, palette.flash, 0.3f, 1.5f + damage * 0.04f);
                    fx.Glow(onBody, palette.flash, 2.2f + damage * 0.06f, 0.45f, 0.2f);
                    AddTrauma(CameraRig.DamageTrauma(damage), e.Direction.X);
                    sfxPlayer.Play(damage >= 15f ? Sfx.HitStrong : Sfx.HitWeak);
                    break;
                }
                case SimEventType.ConnectorContact:
                    if (propView.OnConnectorEvent(e, runner.World.State.Props, palette.flash))
                    {
                        float touch = Mathf.InverseLerp(1.5f,12,e.Strength);
                        dust.Burst(V(e.Position),2,1.1f+touch,0);
                        fx.RingBurst(V(e.Position),palette.energySoft,.12f,.45f+touch*.3f,.12f);
                        AddTrauma(.025f+touch*.055f,e.Direction.X);
                        sfxPlayer.Play(Sfx.Land,.3f+touch*.3f);
                    }
                    break;
                case SimEventType.ConnectorBreak:
                    if (propView.OnConnectorEvent(e, runner.World.State.Props, palette.flash))
                    {
                        var body = PropState.At(runner.World.State.Props,e.PropIndex);
                        var size = V(runner.World.Arena.GetProp(runner.World.Arena.DynamicProp(e.PropIndex)).HalfSize)*2;
                        float strength = Mathf.InverseLerp(8,38,e.Strength);
                        fx.ConnectorShatter(V(e.Position),size,Mathf.Atan2(body.Rotation.Y,body.Rotation.X)*Mathf.Rad2Deg,
                            V(body.Velocity),V(e.Direction),PropView.ConnectorColor);
                        fx.RingBurst(V(e.Position),palette.flash,.18f,2.2f+strength*.5f,.18f);
                        fx.Glow(V(e.Position),palette.flash,2.4f,.5f,.12f);
                        fx.Star(V(e.Position),6,1.3f+strength*.45f);
                        fx.Sparks(V(e.Position),6,8+strength*3);
                        float emphasis = Mathf.Clamp(connectorImpact, .5f, 2f);
                        AddTrauma((.48f+strength*.12f)*emphasis,e.Direction.X);
                        if (cameraRig != null)
                            cameraRig.Kick(V(e.Direction),(.1f+strength*.05f)*emphasis,
                                (.018f+strength*.008f)*emphasis,.18f);
                        sfxPlayer.Play(Sfx.Snap,.9f);
                        sfxPlayer.Play(strength>.5f?Sfx.HitStrong:Sfx.HitWeak,.45f+strength*.2f);
                    }
                    break;
                case SimEventType.JointBreak:
                    // 줄·경첩이 끊어짐(CP15): 연결점에서 작은 섬광과 먼지, 짧은 끊어지는 소리.
                    fx.Glow(V(e.Position), palette.flash, 1.6f, 0.4f, 0.15f);
                    fx.Shards(V(e.Position), Vector2.down, 140f, 4, CurrentTheme.prop, 6f, 0.12f, 0.35f);
                    AddTrauma(0.12f);
                    sfxPlayer.Play(Sfx.Snap);
                    break;
                case SimEventType.PropStrike:
                    // 돌진이 구조물을 침: 먼지, 작은 고리, 둔탁한 소리, 위력 비례 흔들림.
                    dust.Burst(V(e.Position), 3, 1.6f, 0f);
                    fx.RingBurst(V(e.Position), palette.energySoft, 0.22f, 0.9f + e.Strength * 0.02f, 0.16f);
                    AddTrauma(0.06f + e.Strength * 0.006f);
                    sfxPlayer.Play(Sfx.WallSplat, 0.5f);
                    break;
                case SimEventType.BoxKnockout:
                    // 상자가 기둥에서 빠짐(CP16): 상자 중심에서 먼지와 구조물 색 파편, 둔탁한 소리, 위력 비례 흔들림.
                    dust.Burst(V(e.Position), 5, 2.2f, 0f);
                    fx.Shards(V(e.Position), V(e.Direction), 70f, 4, CurrentTheme.prop, 7f, 0.16f, 0.4f);
                    AddTrauma(0.1f + e.Strength * 0.006f);
                    sfxPlayer.Play(Sfx.WallSplat, 0.8f);
                    break;
                case SimEventType.PropImpact:
                    // 구조물이 세게 부딪힘(09 6절): 작은 먼지와 둔탁한 소리. 속도 비례(5 → 작게, 20 이상 → 크게).
                {
                    float strength = Mathf.InverseLerp(5f, 20f, e.Strength);
                    dust.Burst(V(e.Position), 2 + Mathf.RoundToInt(strength * 3f), 1.2f + strength, 0f);
                    AddTrauma(0.02f + strength * 0.06f);
                    sfxPlayer.Play(Sfx.Land, 0.35f + strength * 0.4f);
                    break;
                }
                case SimEventType.Clash:
                    // 접점의 별 모양 폭발과 불꽃, 양쪽 외곽선 번쩍임, 중간 흔들림, 금속성 소리.
                    fx.Star(onBody, 8, 1.1f);
                    fx.Sparks(onBody, 12, 15f);
                    fx.RingBurst(onBody, palette.energy, 0.2f, 1.2f, 0.15f);
                    fx.Glow(onBody, palette.flash, 2.6f, 0.5f, 0.22f);
                    fx.Glow(onBody, palette.energy, 3.6f, 0.3f, 0.3f);
                    fighter0.FlashOutline(4f * Frame);
                    fighter1.FlashOutline(4f * Frame);
                    AddTrauma(0.3f);
                    sfxPlayer.Play(Sfx.Clash);
                    break;
                case SimEventType.WallSplat:
                {
                    // 금 간 자국, 먼지 폭발, 강한 흔들림, 확대, 묵직한 충돌음, 작은 지형 파편.
                    var point = V(e.Position);
                    var normal = V(e.Direction);
                    fx.Crack(point, normal);
                    dust.Burst(point, 4, 2.6f, Mathf.Abs(normal.x) > 0.5f ? normal.x : 0f);
                    fx.Shards(point, normal, 110f, 6, CurrentTheme.terrain, 7f, 0.15f, 0.55f);
                    fx.RingBurst(point, palette.flash, 0.3f, 1.8f, 0.2f);
                    fx.Glow(point, palette.flash, 2.8f, 0.35f, 0.22f);
                    AddTrauma(0.5f, e.Direction.X);
                    Punch(0.02f, 8f * Frame);
                    postFx.Pulse(0.25f, 0.15f);
                    sfxPlayer.Play(Sfx.WallSplat);
                    break;
                }
                case SimEventType.BoundaryHit:
                {
                    // 경계 튕김(CR-002): 가장자리를 따라 퍼지는 빨강 물결, 흰 섬광, 흔들림, 짧은 전기 소리. 면역 중(피해 0)은 작게.
                    var point = V(e.Position);
                    bool hurt = e.Strength > 0f;
                    boundaryView.Hit(point, V(e.Direction));
                    fx.Glow(point, palette.flash, hurt ? 3f : 1.6f, hurt ? 0.45f : 0.2f, 0.2f);
                    AddTrauma(hurt ? 0.25f + e.Strength * 0.015f : 0.06f);
                    sfxPlayer.Play(Sfx.Boundary, hurt ? 1f : 0.5f);
                    break;
                }
                case SimEventType.BounceRebound:
                    // 경직·돌진 중 지형에 튕김: 먼지, 작은 고리, 세기 비례 흔들림.
                    dust.Burst(V(e.Position), 3, 1.4f, Mathf.Abs(e.Direction.X) > 0.5f ? e.Direction.X : 0f);
                    fx.RingBurst(V(e.Position), palette.uiSub, 0.2f, 0.8f + e.Strength * 0.02f, 0.16f);
                    AddTrauma(0.04f + e.Strength * 0.006f);
                    sfxPlayer.Play(Sfx.Land, 0.6f);
                    break;
                case SimEventType.KO:
                {
                    // 큰 폭발. 히트스탑 20틱 뒤 진 쪽 몸이 조각난다(UpdateKnockout).
                    var center = Fighter(e.Fighter).BodyCenter;
                    fx.RingBurst(center, palette.flash, 0.5f, 4.5f, 0.35f);
                    fx.Glow(center, palette.flash, 6f, 0.5f, 0.4f);
                    fx.Shards(center, Vector2.up, 360f, 10, palette.FighterBody(e.Fighter), 12f, 0.26f, 0.6f);
                    if (!reduceMotion)
                        fx.ScreenFlash(false);
                    AddTrauma(0.6f, e.Direction.X);
                    Punch(0.03f, 12f * Frame);
                    postFx.Pulse(0.6f, 0.25f);
                    sfxPlayer.Play(Sfx.KO);
                    break;
                }
            }
        }

        void AddTrauma(float amount, float horizontalDirection = 0)
        {
            if (cameraRig != null)
                cameraRig.AddTrauma(amount, horizontalDirection);
        }

        void Punch(float amount, float duration)
        {
            if (cameraRig != null)
                cameraRig.Punch(amount, duration);
        }
    }
}
