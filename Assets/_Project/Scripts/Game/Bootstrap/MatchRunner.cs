using System;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 틱 실행기(01-architecture.md 5절). Update에서 실제 경과 시간을 누적해 1/60초마다 SimWorld.Step을 부른다.
    /// 한 프레임에 최대 4틱까지만 따라잡는다. 연출은 PreviousState, World.State, InterpolationAlpha로 보간한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class MatchRunner : MonoBehaviour
    {
        [SerializeField] MovementTuning movementTuning;
        [SerializeField] CombatTuning combatTuning;
        [Tooltip("F11로 순환하는 전투 튜닝 프리셋(기본, 더 빠르게)")]
        [SerializeField] CombatTuning[] combatPresets;
        [Tooltip("시작 폼과 기능 스위치(CR-003): F1 폼, F4 가드·잡기, Insert 에너지. 다음 StartNewMatch부터 적용")]
        [SerializeField] RuleSet rules = RuleSet.Brawl;
        [SerializeField] TextAsset[] arenaFiles;
        [Tooltip("새 맵 정의 목록. 과거 txt/json 자료는 리플레이용으로 함께 보존합니다.")]
        [SerializeField] Maps.MapCatalog mapCatalog;
        [Tooltip("등록 목록이 비어 있을 때 사용할 기본 정의. 맵 제작 시험은 지정한 맵 하나만 사용합니다.")]
        [SerializeField] Maps.MapDefinition selectedMap;
        public Maps.MapCatalog MapCatalog => mapCatalog;
        [Tooltip("맵 짝 파일(<이름>.json): 테마, 광원, 순환 포함 여부")]
        [SerializeField] TextAsset[] arenaMetaFiles;
        [Tooltip("RegisteredMaps는 등록 맵 순환, Rotation은 옛 검증 맵 순환, 맵 이름은 한 맵 고정(F2)")]
        [SerializeField] string arenaName = RotationName;
        [SerializeField] uint seed = 1;
        [Tooltip("켜면 StartNewMatch마다 시드를 새로 뽑는다(맵 순서가 경기마다 달라짐)")]
        [SerializeField] bool randomizeSeed;
        [SerializeField, Range(0, InputDelayQueue.MaxDelay)] int inputDelayTicks;
        [SerializeField] bool startOnAwake = true;
        [SerializeField] bool skipCountdown;
        [Tooltip("켜면 경기 시작 때 P1 키보드 왼쪽, P2 첫 게임패드(없으면 키보드 오른쪽)를 붙인다")]
        [SerializeField] bool useLocalDevices;
        [Tooltip("Brawl 전투와 로컬 P1 마우스 조준·P2 연습 상대를 사용합니다. 시작 맵은 등록 목록에서 순환하며, 맵 제작 시험은 지정한 맵 하나만 사용합니다. 세트·카드 흐름은 화면 연결 설정을 따릅니다.")]
        [SerializeField] bool brawlExperiment;
        [Tooltip("CR-004 7절 표면 이동(보관, 기본 끔): 0 끔, 1 D(붙기만), 2 E(거미식 자유 표면 이동 + 지형 중력장). 실험 경기에서 F4로 순환")]
        [SerializeField, Range(0, 2)] int surfaceMode = 0;

        readonly FixedTickClock clock = new FixedTickClock();
        readonly InputDelayQueue delay1 = new InputDelayQueue();
        readonly InputDelayQueue delay2 = new InputDelayQueue();
        SyncTest<SimWorld> syncTest;
        double lastRealtime;
        /// <summary>StartNewMatch로 튜닝 에셋에서 만든 경기만 라운드 사이에 튜닝을 다시 읽는다(시나리오 덮어쓰기 보존).</summary>
        bool configFromTuning;

        public SimWorld World { get; private set; }
        /// <summary>Native prediction keeps UI decisions and one-shot effects on the confirmed timeline.</summary>
        public SimWorld FrontendWorld => confirmedWorld ?? World;
        public SimWorld EventWorld => confirmedWorld ?? World;
        SimWorld confirmedWorld;
        int predictionSlot;
        float predictionAlpha;
        public bool PredictionDriven { get; private set; }
        public SimState PreviousState { get; private set; }
        public float InterpolationAlpha => clock.Alpha;
        public FixedTickClock Clock => clock;

        /// <summary>Begin 이후 실행한 틱 수.</summary>
        public long TicksRun { get; private set; }

        /// <summary>false면 시간이 흘러도 틱을 실행하지 않는다(일시정지).</summary>
        public bool Running { get; set; }

        /// <summary>0보다 크면 그 틱 수에 도달했을 때 자동으로 멈춘다. 시나리오 재생용.</summary>
        public int StopAtTick { get; set; }

        /// <summary>온라인 경기 규칙(두 선수가 함께 눌러야 재대결). 다음 StartNewMatch부터 적용된다.</summary>
        public bool OnlineRematch { get; set; }

        /// <summary>
        /// 온라인 락스텝(CR-004 온라인 2단계): 설정되면 입력원·지연 큐 대신 이것이 틱마다 확정된 두 입력을 준다.
        /// 아직 없으면 그 틱을 계산하지 않고 기다린다(예측하지 않음).
        /// </summary>
        public ILockstepInputs Lockstep { get; set; }

        /// <summary>온라인은 세션의 내 슬롯, 로컬 연습은 마우스를 쓰는 P1. 시뮬레이션 상태에는 저장하지 않는다.</summary>
        public int LocalPlayerSlot => PredictionDriven ? predictionSlot : Lockstep?.LocalSlot ?? 0;
        public bool FrontendEnabled { get; set; }
        public bool MenuInputBlocked { get; set; }
        public bool LiveTuningLocked => Lockstep != null || OnlineRematch || Recording != null || Playback != null;
        readonly FrontendInputSource[] frontendInputs = new FrontendInputSource[2];

        public IInputSource RouteFrontendInput(IInputSource input, int slot, bool automaticPick = false)
        {
            if (!FrontendEnabled) return input;
            var routed = new FrontendInputSource(() => FrontendWorld, () => MenuInputBlocked, input, slot, automaticPick);
            frontendInputs[slot] = routed;
            return routed;
        }

        public bool RequestCardSelection(int slot, int option) =>
            slot >= 0 && slot < 2 && frontendInputs[slot] != null && frontendInputs[slot].Choose(option);

        public bool RequestRematch(int slot) =>
            slot >= 0 && slot < 2 && frontendInputs[slot] != null && frontendInputs[slot].ReadyForRematch();

        /// <summary>로컬 검수용 즉사. 입력 기록/재생·온라인·SyncTest에는 상태 주입을 허용하지 않는다.</summary>
        public bool TryDebugKnockout(int slot)
        {
            if ((!Application.isEditor && !Debug.isDebugBuild) || slot < 0 || slot > 1
                || MenuInputBlocked || World == null || World.State.Match.Phase != MatchPhase.Fighting
                || Lockstep != null || Recording != null || Playback != null || syncTest != null
                || GetComponent<Online.OnlineMatch>()?.Active == true) return false;
            if (slot == 0) World.State.Fighter0.Health = 0;
            else World.State.Fighter1.Health = 0;
            // 일반 틱을 거쳐 KO/무기 정리/득점 사건과 연출이 평소처럼 이어진다.
            StepOnce();
            return true;
        }

        public IInputSource Player1Input { get; set; } = new IdleInputSource();
        public IInputSource Player2Input { get; set; } = new IdleInputSource();
        /// <summary>연습 상대 전환은 카드 입력 라우터를 유지한다.</summary>
        public IInputSource PracticeOpponent
        {
            get => Player2Input is FrontendInputSource routed ? routed.Combat : Player2Input;
            set => Player2Input = RouteFrontendInput(value, 1, true);
        }

        /// <summary>null이 아니면 매 틱 실제로 쓰인(지연이 적용된) 입력을 덧붙인다.</summary>
        public InputRecording Recording { get; private set; }

        public SyncTest<SimWorld> SyncTest => syncTest;

        /// <summary>매 틱 Step 직후. 연출이 World.Events를 소비하는 지점이다.</summary>
        public event Action<MatchRunner> Ticked;

        /// <summary>Begin 직후. 연출이 맵을 다시 그리는 지점이다.</summary>
        public event Action<MatchRunner> MatchBegan;

        /// <summary>
        /// 멈춰 있으면 1(현재 상태를 그대로 그림), 진행 중이면 다음 틱까지의 비율.
        /// 히트스탑 중 보간 금지는 연출 쪽에서 처리한다(CP4).
        /// </summary>
        public float RenderAlpha => Running ? (PredictionDriven ? predictionAlpha : clock.Alpha) : 1f;

        /// <summary>arenaName이 이 값이면 순환 맵 전부를 라운드마다 바꿔 가며 쓴다(04-arena.md 6절).</summary>
        public const string RotationName = "Rotation";
        public const string RegisteredMapsName = "RegisteredMaps";

        public string ArenaName
        {
            get => arenaName;
            set => arenaName = value;
        }

        public uint Seed
        {
            get => seed;
            set => seed = value;
        }

        /// <summary>켜면 StartNewMatch마다 시드를 새로 뽑는다. 온라인은 끄고 방장의 시드를 쓴다.</summary>
        public bool RandomizeSeed
        {
            get => randomizeSeed;
            set => randomizeSeed = value;
        }

        public MovementTuning Tuning
        {
            get => movementTuning;
            set => movementTuning = value;
        }

        public CombatTuning CombatTuning
        {
            get => combatTuning;
            set => combatTuning = value;
        }

        public string PresetName => combatTuning != null && !string.IsNullOrEmpty(combatTuning.PresetName) ? combatTuning.PresetName : "default";

        /// <summary>F11: 다음 전투 튜닝 프리셋으로 바꾼다(다음 StartNewMatch부터).</summary>
        public void CyclePreset()
        {
            if (combatPresets == null || combatPresets.Length == 0)
                return;
            int index = System.Array.IndexOf(combatPresets, combatTuning);
            combatTuning = combatPresets[(index + 1) % combatPresets.Length];
        }

        /// <summary>재생 중인 입력 기록(F10). 끝나면 최종 체크섬을 기록 시점과 비교해 로그로 남긴다.</summary>
        public InputRecording Playback { get; private set; }

        /// <summary>
        /// 입력 기록을 처음부터 재생한다. 같은 튜닝·조작 규칙의 설정을 찾지 못하면 재생하지 않고, 맵 체크섬이 다르면 경고한다.
        /// </summary>
        public bool PlayRecording(InputRecording recording)
        {
            if (recording == null)
                return false;
            var config = FindRecordedConfig(recording);
            if (config == null)
            {
                Debug.LogWarning($"[Knockbox] 기록 재생 거절: 같은 튜닝·조작 규칙의 설정을 찾지 못함(설정 체크섬 {recording.ConfigChecksum:X8}, 규칙 {recording.Mode}). 기록을 만든 빌드나 설정이 필요함");
                return false;
            }
            string[] names = recording.ArenaNames;
            var arenas = new ArenaDef[names.Length];
            for (int i = 0; i < names.Length; i++)
                arenas[i] = ResolveRecordedArena(names[i]);
            if (!recording.CanVerifyPlayerCollision(arenas))
            {
                Debug.LogWarning("[Knockbox] 기록 재생 거절: 구 기록에는 새 기둥·로프 설정이 없습니다. 기록 당시 맵이 필요합니다.");
                return false;
            }
            if (recording.ExpectedArenaChecksum(arenas) != recording.ArenaChecksum)
            {
                if (recording.UsesGeometryChecksum)
                {
                    Debug.LogWarning("[Knockbox] 기록 재생 거절: 물리 맵의 지형·상자·질량이 기록과 다릅니다. 기록 당시의 맵 정의가 필요합니다.");
                    return false;
                }
                Debug.LogWarning("[Knockbox] 기록과 맵이 달라 재생 결과가 다를 수 있음");
            }
            var p1 = new InputFrame[recording.Count];
            var p2 = new InputFrame[recording.Count];
            for (int i = 0; i < recording.Count; i++)
            {
                p1[i] = recording.GetP1(i);
                p2[i] = recording.GetP2(i);
            }
            Begin(config, arenas, recording.BuildState(config, arenas));
            // 기록된 입력은 이미 지연을 거친 값이다.
            delay1.Reset(0);
            delay2.Reset(0);
            Player1Input = new FrameArrayInputSource(p1);
            Player2Input = new FrameArrayInputSource(p2);
            Playback = recording;
            return true;
        }

        /// <summary>지금 프리셋들과 기록의 규칙 후보 중에서 기록의 설정 체크섬과 같은 설정을 찾는다. 없으면 null.</summary>
        public SimConfig FindRecordedConfig(InputRecording recording)
        {
            var candidates = RecordedRuleCandidates(recording.Mode);
            for (int p = -1; p < (combatPresets != null ? combatPresets.Length : 0); p++)
            {
                var tuning = p < 0 ? combatTuning : combatPresets[p];
                if (p >= 0 && (tuning == null || tuning == combatTuning))
                    continue;
                foreach (var candidate in candidates)
                {
                    // 온라인 경기 기록(재대결 규칙 켬)도 찾는다.
                    for (int online = 0; online < 2; online++)
                    {
                        // 현재 좌클릭 통합 입력, CP4 Shift 차징, 구 탭/홀드 기록을 해시로 정확히 고른다.
                        for (int controls = 0; controls < 3; controls++)
                        {
                            for (int weapons = 0; weapons < 2; weapons++)
                            {
                                // CP5 첫 버전의 내려놓기와 현재 투척도 같은 Grab 비트를 쓰므로 설정을 구분한다.
                                for (int throwing = 0; throwing < (weapons == 0 ? 2 : 1); throwing++)
                                {
                                    // 입력 형식이 같은 v8의 조정 전·후 수치도 정확한 설정 해시로 구분한다.
                                    for (int balance = 0; balance < (controls == 0 ? 2 : 1); balance++)
                                    {
                                        // v8/v9 안에서도 설정 해시로 상자 반응 조정 전·후를 구분한다.
                                        for (int connectorResponse = 0; connectorResponse < 2; connectorResponse++)
                                        for (int boxResponse = 0; boxResponse < 2; boxResponse++)
                                        {
                                            var config = CreateConfig(tuning, candidate, online == 1,
                                                controls != 2, weapons == 0, controls == 0, weapons == 0 && throwing == 0,
                                                controls == 0 && balance == 0, smoothBoxes: boxResponse == 0, stableConnectors: connectorResponse == 0);
                                            if (config.ComputeChecksum() == recording.ConfigChecksum)
                                                return config;
                                            if (!candidate.LegacyMatch)
                                            {
                                                var cards = CardCollectionRules.Apply(config);
                                                if (cards.ComputeChecksum() == recording.ConfigChecksum) return cards;
                                                // 2026-11-14 전 기록: 확정 대기 84틱.
                                                var previousHold = CardCollectionRules.Apply(config, longPickHold: false);
                                                if (previousHold.ComputeChecksum() == recording.ConfigChecksum) return previousHold;
                                                var previousIntro = CardCollectionRules.Apply(config, extendedIntro: false, longPickHold: false);
                                                if (previousIntro.ComputeChecksum() == recording.ConfigChecksum) return previousIntro;
                                                var previousMotion = CardCollectionRules.Apply(config, cinematic: false);
                                                if (previousMotion.ComputeChecksum() == recording.ConfigChecksum) return previousMotion;
                                                var originalCards = CardCollectionRules.Apply(config, motion: false);
                                                if (originalCards.ComputeChecksum() == recording.ConfigChecksum) return originalCards;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>기록의 규칙 문자열(RuleSet.Format) → 후보 하나. 폼 시대 기록(다른 폼·전역 모드 이름)은 2026-11-11부터 읽지 않는다.</summary>
        static RuleSet[] RecordedRuleCandidates(string recorded) =>
            RuleSet.TryParse(recorded, out var parsed) ? new[] { parsed } : new RuleSet[0];

        /// <summary>경기 규칙(잡기 스위치, 경기 흐름). 다음 StartNewMatch부터 적용된다.</summary>
        public RuleSet Rules
        {
            get => rules;
            set => rules = value;
        }

        /// <summary>다음 StartNewMatch부터 적용된다.</summary>
        public bool GrabEnabled
        {
            get => rules.Switches.GrabEnabled;
            set => rules.Switches.GrabEnabled = value;
        }

        public int InputDelayTicks
        {
            get => inputDelayTicks;
            set
            {
                inputDelayTicks = value;
                delay1.Reset(value);
                delay2.Reset(value);
            }
        }

        /// <summary>CR-004 실험 경기(브랜치 exp/brawl-v1).</summary>
        public bool BrawlExperiment
        {
            get => brawlExperiment;
            set => brawlExperiment = value;
        }

        /// <summary>표면 이동 모드(0 끔, 1 D, 2 E). 다음 StartNewMatch부터 적용된다.</summary>
        public int SurfaceMode
        {
            get => surfaceMode;
            set => surfaceMode = value;
        }

        /// <summary>CR-004 실험 맵(6절).</summary>
        public const string BrawlArenaName = "Brawl";

        void Awake()
        {
            if (brawlExperiment)
            {
                arenaName = mapCatalog != null && mapCatalog.Maps.Count > 0 ? RegisteredMapsName
                    : selectedMap != null ? selectedMap.MatchName : BrawlArenaName;
                rules = RuleSet.Brawl;
            }
#if UNITY_EDITOR
            string testMapGuid=UnityEditor.SessionState.GetString("Knockbox.MapAuthoring.TestMap", "");
            if(!string.IsNullOrEmpty(testMapGuid) && gameObject.scene.name=="Main")
            {
                var trial=UnityEditor.AssetDatabase.LoadAssetAtPath<Maps.MapDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(testMapGuid));
                ConfigureAuthoringPreview(trial);
            }
#endif
            if (startOnAwake && World == null)
                StartNewMatch();
        }

        /// <summary>튜닝을 다시 읽어 새 경기를 시작한다.</summary>
        public void StartNewMatch()
        {
            var config = CreateConfig();
            var arenas = PrepareMatchArenas(config);
            if (randomizeSeed)
                seed = (uint)Environment.TickCount | 1u;
            StartPreparedMatch(config, arenas, seed);
            configFromTuning = true;
        }

        /// <summary>연결 대기 중 맵/튜닝 편집이 실제 경기 목록을 바꾸지 않도록 확정 스냅샷을 사용한다.</summary>
        public void StartPreparedOnlineMatch(SimConfig config, ArenaDef[] arenas, uint sharedSeed)
        {
            if (IsAuthoringPreview) throw new InvalidOperationException("제작기 시험은 로컬 전용입니다. 시험 종료 후 Main에서 연결해주세요.");
            seed = sharedSeed;
            StartPreparedMatch(config, arenas, sharedSeed, false);
        }

        void StartPreparedMatch(SimConfig config, ArenaDef[] arenas, uint matchSeed, bool allowLocalFastStart=true)
        {
            var phase = allowLocalFastStart && skipCountdown ? MatchPhase.Fighting : MatchPhase.Countdown;
            if (useLocalDevices)
            {
                Player1Input = brawlExperiment ? new MouseAimInputSource(() => World, 0) : (IInputSource)new KeyboardInputSource(KeyboardLayout.Left);
                Player2Input = brawlExperiment ? new DummyInputSource()
                    : GamepadInputSource.Available(0) ? new GamepadInputSource(0) : new KeyboardInputSource(KeyboardLayout.Right);
            }
            if (FrontendEnabled && Lockstep == null)
            {
                Player1Input = RouteFrontendInput(new MouseAimInputSource(() => World, 0), 0);
                Player2Input = RouteFrontendInput(new DummyInputSource(), 1, true);
            }
            Begin(config, arenas, SimState.CreateMatch(config, arenas, matchSeed, phase));
        }

        public SimConfig CreateConfig()
        {
            return PreviewConfig(FrontendEnabled);
        }

        /// <summary>Reads the same resolution path without starting a match or consuming randomness.</summary>
        public SimConfig PreviewConfig(bool includeFrontend)
        {
            // 무기/총알은 현재 경기에서 보류. 호환용 CreateConfig 기본값과 구 기록은 보존한다.
            var config = CreateConfig(combatTuning, rules, OnlineRematch, weaponsEnabled: false,
                smoothBoxes: combatTuning == null || combatTuning.SmoothBoxRelease,
                stableConnectors: combatTuning == null || combatTuning.StableConnectorContacts);
            return includeFrontend && brawlExperiment ? CardCollectionRules.Apply(config) : config;
        }

        SimConfig CreateConfig(CombatTuning combat, in RuleSet ruleSet, bool onlineRematch = false,
            bool separateChargeInput = true, bool weaponsEnabled = true, bool combinedAttackInput = true,
            bool throwEnabled = true, bool revisedBalance = true, bool smoothBoxes = false, bool stableConnectors = false)
        {
            var match = movementTuning != null ? movementTuning.Match : MatchParams.Default;
            var movement = movementTuning != null ? movementTuning.Movement : MovementParams.Default;
            var combatParams = combat != null ? combat.Combat : CombatParams.Default;
            var matchParams = brawlExperiment ? BrawlParams.Match(match) : match;
            matchParams.RematchBothHold = onlineRematch;
            const bool bothBody = true; // 2026-11-11 폼 전투 제거: 두 선수 모두 늘 몸 폼이다.
            combatParams.Props.SmoothBoxRelease = smoothBoxes && brawlExperiment && bothBody;
            combatParams.Props.StableConnectorContacts = stableConnectors && brawlExperiment && bothBody;
            combatParams.Props.ReleasedBoxFrictionScale = Mathf.Clamp(combat != null ? combat.ReleasedBoxFrictionScale : 0.15f, 0.02f, 1f);
            return new SimConfig(
                brawlExperiment ? BrawlParams.Movement(movement) : movement,
                matchParams,
                // 몸 폼 전투 값은 늘 적용한다(2026-11-11 폼 전투 제거 뒤 유일한 전투). 이동·경기 수치만 brawlExperiment를 따른다(Sandbox).
                BrawlParams.Combat(combatParams, surfaceMode, separateChargeInput,
                    weaponsEnabled && bothBody, combinedAttackInput && bothBody, throwEnabled && bothBody,
                    revisedBalance && bothBody),
                ruleSet);
        }

        public void Begin(SimConfig config, ArenaDef arena, in SimState initial) => Begin(config, new[] { arena }, initial);

        public void Begin(SimConfig config, ArenaDef[] arenas, in SimState initial)
        {
            EndPrediction();
            World = new SimWorld(config, arenas, initial);
            PreviousState = initial;
            configFromTuning = false;
            Playback = null;
            delay1.Reset(inputDelayTicks);
            delay2.Reset(inputDelayTicks);
            clock.Reset();
            lastRealtime = Time.realtimeSinceStartupAsDouble;
            TicksRun = 0;
            StopAtTick = 0;
            Recording = null;
            if (syncTest != null)
                syncTest = new SyncTest<SimWorld>(World);
            Running = true;
            MatchBegan?.Invoke(this);
        }

        /// <summary>F8. 켜면 매 틱 7틱 되감기 검사를 하고 불일치를 오류 로그로 남긴다.</summary>
        public void SetSyncTest(bool enabled, int rollbackTicks = SyncTest<SimWorld>.DefaultRollbackTicks)
        {
            syncTest = enabled && World != null ? new SyncTest<SimWorld>(World, rollbackTicks) : null;
        }

        public void StartRecording(string mode)
        {
            if (World == null)
                return;
            var s = World.State;
            // 기록은 현재 상태를 시작점으로 삼을 수 없으므로(라운드 생성 값만 저장) 라운드 시작 직후에만 의미가 있다.
            var arenas = new ArenaDef[World.ArenaCount];
            for (int i = 0; i < arenas.Length; i++)
                arenas[i] = World.GetArena(i);
            Recording = InputRecording.Begin(World.Config, arenas, s.Match.Rotation, s.Rng.State, s.Fighter0.Position, s.Fighter1.Position, s.Match.Phase, mode);
            if (Recording.InitialChecksum != World.ComputeChecksum())
            {
                Debug.LogWarning("[Knockbox] 입력 기록은 라운드 시작 상태에서만 시작할 수 있음");
                Recording = null;
            }
        }

        public InputRecording StopRecording()
        {
            var rec = Recording;
            Recording = null;
            rec?.Finish(World.ComputeChecksum());
            return rec;
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - lastRealtime;
            lastRealtime = now;
            if (!Running || World == null || PredictionDriven)
                return;

            int ticks = clock.Advance(elapsed);
            for (int i = 0; i < ticks && Running; i++)
            {
                if (!StepOnce())
                    break;
            }
        }

        /// <summary>한 틱 진행한다. 일시정지 중 한 틱 진행(Ctrl+O)에도 쓴다. 락스텝에서 상대 입력이 아직 없으면 진행하지 않고 false.</summary>
        public bool StepOnce()
        {
            if (World == null || PredictionDriven)
                return false;
            int tick = World.State.Tick;
            InputFrame p1, p2;
            if (Lockstep != null)
            {
                if (!Lockstep.TryGetInputs(tick, out p1, out p2))
                    return false;
            }
            else
            {
                p1 = delay1.Push(Player1Input != null ? Player1Input.Poll(tick) : default);
                p2 = delay2.Push(Player2Input != null ? Player2Input.Poll(tick) : default);
            }
            Recording?.Append(p1, p2);

            PreviousState = World.State;
            if (syncTest != null)
            {
                if (!syncTest.Advance(p1, p2))
                    Debug.LogError("[Knockbox] " + syncTest.LastMismatch);
            }
            else
            {
                World.Step(p1, p2);
            }
            TicksRun++;
            bool roundTransition = false;
            for (int i = 0; i < World.Events.Count; i++)
            {
                ref readonly var e = ref World.Events[i];
                if (e.Type == SimEventType.OutOfBounds)
                    Debug.LogWarning($"[Knockbox] 경기장 밖으로 나가 되돌림(버그 신호): {e}");
                else if (e.Type == SimEventType.RoundTransition)
                    roundTransition = true;
            }
            if (World.Events.Dropped > 0)
                Debug.LogWarning($"[Knockbox] 이벤트 버퍼가 넘쳐 {World.Events.Dropped}개를 버림(틱 {World.State.Tick})");
            Ticked?.Invoke(this);
            if (roundTransition)
                ApplyTuningAtRoundTransition();

            if (Playback != null && TicksRun == Playback.Count)
            {
                bool same = !Playback.HasFinalChecksum || World.ComputeChecksum() == Playback.FinalChecksum;
                if (same)
                    Debug.Log($"[Knockbox] 기록 재생 끝({Playback.Count}틱): 최종 체크섬이 기록과 같음");
                else
                    Debug.LogWarning($"[Knockbox] 기록 재생 끝({Playback.Count}틱): 최종 체크섬이 기록과 다름");
            }

            if (StopAtTick > 0 && World.State.Tick >= StopAtTick)
                Running = false;
            return true;
        }

        public void BeginPrediction(int localSlot, SimWorld confirmed)
        {
            if (confirmed == null) throw new ArgumentNullException(nameof(confirmed));
            predictionSlot = localSlot; confirmedWorld = confirmed; predictionAlpha = 1;
            PredictionDriven = true;
        }

        public void StepPrediction(in SimState restored, InputFrame p1, InputFrame p2)
        {
            if (!PredictionDriven) throw new InvalidOperationException("Fusion 예측 경기가 시작되지 않았습니다.");
            World.State = restored;
            PreviousState = restored;
            World.Step(p1, p2);
        }

        public void SetPredictionRenderState(in SimState state, float alpha)
        {
            if (!PredictionDriven) return;
            if (World.State.Tick != state.Tick) PreviousState = World.State;
            World.State = state;
            predictionAlpha = Mathf.Clamp01(alpha);
        }

        public void PublishConfirmedTick()
        {
            if (!PredictionDriven) return;
            TicksRun = confirmedWorld.State.Tick;
            Ticked?.Invoke(this);
        }

        public void EndPrediction()
        {
            PredictionDriven = false;
            confirmedWorld = null;
            predictionSlot = 0;
        }

        /// <summary>
        /// RoundTransition 훅: 튜닝을 다시 읽어 다음 라운드부터 반영한다(06-match-and-tools.md 1절).
        /// 바뀐 것이 없으면 그대로 둔다. 바뀌면 입력 기록과 SyncTest는 이 지점 이전과 설정이 달라진다.
        /// </summary>
        void ApplyTuningAtRoundTransition()
        {
            if (!configFromTuning || LiveTuningLocked)
                return;
            var config = CreateConfig();
            if (config.ComputeChecksum() == World.Config.ComputeChecksum())
                return;
            World.ReplaceConfig(config);
            Debug.Log("[Knockbox] 다음 라운드부터 바뀐 튜닝을 적용함");
            if (Recording != null)
                Debug.LogWarning("[Knockbox] 기록 도중 설정이 바뀌어 이 입력 기록은 재생 결과가 달라질 수 있음");
            if (syncTest != null)
                syncTest = new SyncTest<SimWorld>(World, syncTest.RollbackTicks);
        }
    }
}
