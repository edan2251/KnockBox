using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knockbox.Game
{
    /// <summary>
    /// 디버그 단축키(06-match-and-tools.md 4절) 중 지금 있는 기능만: F1 전투 모드 전환(라운드 리셋), F3 판정 오버레이,
    /// F4 잡기 켜기/끄기(라운드 리셋), F5 라운드 리셋(튜닝 재적용),
    /// F6 P2 입력원 순환(키보드 오른쪽 → 게임패드 → 허수아비), `[` `]` 허수아비 행동 바꾸기,
    /// F7 흔들림 줄이기, F8 SyncTest, F2 맵 순환, F9 입력 기록 시작·중지, F10 마지막 기록 재생,
    /// F11 전투 튜닝 프리셋(기본 ↔ 더 빠르게), `~` 도움말, O/P P1/P2 즉사, Ctrl+P 일시정지, Ctrl+O 한 틱 진행.
    /// 에디터와 개발 빌드에서만 동작한다.
    /// </summary>
    [RequireComponent(typeof(MatchRunner))]
    public sealed class DebugHotkeys : MonoBehaviour
    {
        MatchRunner runner;
        MatchPresenter presenter;
        CameraRig cameraRig;
        readonly DummyInputSource dummy = new DummyInputSource();
        BotInputSource bot;
        BrawlBot brawlBot;
        LungePracticeBot practiceBot;
        /// <summary>빌드의 F1 순환: 가위·바위·보(시험 공격 폼은 에디터 전용).</summary>
        static readonly FighterForm[] BuildForms = { FighterForm.Scissors, FighterForm.Rock, FighterForm.Paper };

        void Awake()
        {
            runner = GetComponent<MatchRunner>();
            presenter = GetComponent<MatchPresenter>();
            // 명령줄 자동 진행(-autobots, -ticksperframe N). 인수가 없으면 스스로 꺼진다(CP24).
            if (GetComponent<AutoPlayDebug>() == null)
                gameObject.AddComponent<AutoPlayDebug>();
            bot = new BotInputSource(() => runner.World, 1, 0xB07u);
            // CR-004 D/E 지표: 플레이하는 동안 라운드마다 한 줄만 남긴다.
            var metrics = new SurfaceMetrics();
            runner.Ticked += metrics.OnTicked;
            brawlBot = new BrawlBot(() => runner.World, 1, 0xB07u);
            practiceBot = new LungePracticeBot(() => runner.World, 1);
        }

        void Start()
        {
            // 빌드에서는 처음 실행할 때 도움말을 열어 둔다(06-match-and-tools.md 6절).
            if (presenter != null && presenter.Help != null && !Application.isEditor)
                presenter.Help.Visible = true;
        }

        void Update()
        {
            if (!Debug.isDebugBuild && !Application.isEditor)
                return;
            if (runner.MenuInputBlocked) return;
            var k = Keyboard.current;
            if (k == null)
                return;
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<CameraRig>();

            if (k.f1Key.wasPressedThisFrame)
            {
                // 시작 폼 순환(CR-003). 전투 중 전환은 CP21.
                var forms = Application.isEditor ? (FighterForm[])System.Enum.GetValues(typeof(FighterForm)) : BuildForms;
                var rules = runner.Rules;
                rules.StartForm = forms[(System.Array.IndexOf(forms, rules.StartForm) + 1) % forms.Length];
                runner.Rules = rules;
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 시작 폼: {rules.StartForm}");
            }
            if (k.f12Key.wasPressedThisFrame && presenter != null)
                presenter.SetDebugText(!presenter.DebugTextVisible);
            if (k.backquoteKey.wasPressedThisFrame && presenter != null && presenter.Help != null)
                presenter.Help.Visible = !presenter.Help.Visible;
            if (k.f2Key.wasPressedThisFrame)
            {
                // 맵 순환(Rotation) → 맵 하나에 고정 → ... (04-arena.md 6절: F2는 디버그용 맵 고정)
                string[] arenas = runner.ArenaChoices();
                int index = System.Array.IndexOf(arenas, runner.ArenaName);
                runner.ArenaName = arenas[(index + 1) % arenas.Length];
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 맵: {runner.ArenaName}");
            }
            if (k.f11Key.wasPressedThisFrame)
            {
                runner.CyclePreset();
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 튜닝 프리셋: {runner.PresetName}");
            }
            if (k.f9Key.wasPressedThisFrame)
                ToggleRecording();
            if (k.f10Key.wasPressedThisFrame)
                PlayLatestRecording();
            if (k.f4Key.wasPressedThisFrame && runner.BrawlExperiment)
            {
                // CR-004 실험 경기: 표면 이동 모드 순환(끔 → D 붙기 → E 붙기 + 외곽 달리기).
                runner.SurfaceMode = (runner.SurfaceMode + 1) % 3;
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 표면 이동: {SurfaceModeName(runner.SurfaceMode)}");
            }
            else if (k.f4Key.wasPressedThisFrame)
            {
                // 가드·잡기 한 스위치(10 문서 5.1, D-043).
                var rules = runner.Rules;
                bool on = !(rules.Switches.GuardEnabled && rules.Switches.GrabEnabled);
                rules.Switches.GuardEnabled = on;
                rules.Switches.GrabEnabled = on;
                runner.Rules = rules;
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 가드·잡기 {(on ? "켬" : "끔")}");
            }
            if (k.insertKey.wasPressedThisFrame)
            {
                var rules = runner.Rules;
                rules.Switches.EnergyEnabled = !rules.Switches.EnergyEnabled;
                runner.Rules = rules;
                RestartKeepingDummy();
                Debug.Log($"[Knockbox] 공격 에너지 {(rules.Switches.EnergyEnabled ? "켬" : "끔")}");
            }
            if (k.f3Key.wasPressedThisFrame && presenter != null)
                presenter.SetOverlay(!presenter.OverlayVisible);
            if (k.f5Key.wasPressedThisFrame)
                RestartKeepingDummy();
            if (k.f6Key.wasPressedThisFrame)
                CycleP2Input();
            if (runner.PracticeOpponent is DummyInputSource current)
            {
                int step = (k.rightBracketKey.wasPressedThisFrame ? 1 : 0) - (k.leftBracketKey.wasPressedThisFrame ? 1 : 0);
                if (step != 0)
                {
                    current.Behavior = DummyInputSource.Cycle(current.Behavior, step);
                    Debug.Log($"[Knockbox] 허수아비 행동: {current.Behavior}");
                }
            }
            if (k.homeKey.wasPressedThisFrame && presenter != null)
            {
                // 현재 폼 표시 방식 순환(10 문서 5.1). 연출 옵션이라 시뮬레이션에 영향이 없다.
                presenter.FormDisplay = (FormDisplayMode)(((int)presenter.FormDisplay + 1) % 3);
                Debug.Log($"[Knockbox] 폼 표시: {presenter.FormDisplay}");
            }
            if (k.f7Key.wasPressedThisFrame && cameraRig != null)
            {
                cameraRig.ReduceMotion = !cameraRig.ReduceMotion;
                Debug.Log($"[Knockbox] 흔들림 줄이기 {(cameraRig.ReduceMotion ? "켬" : "끔")}");
            }
            if (k.f8Key.wasPressedThisFrame)
            {
                bool enable = runner.SyncTest == null;
                runner.SetSyncTest(enable);
                Debug.Log($"[Knockbox] SyncTest {(enable ? "켬" : "끔")}");
            }
            bool control = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
            if (control && k.pKey.wasPressedThisFrame)
                runner.Running = !runner.Running;
            if (control && k.oKey.wasPressedThisFrame && !runner.Running)
                runner.StepOnce();
            if (!control && k.oKey.wasPressedThisFrame) runner.TryDebugKnockout(0);
            if (!control && k.pKey.wasPressedThisFrame) runner.TryDebugKnockout(1);
        }

        /// <summary>
        /// F9: 기록 중이 아니면 새 경기를 시작하며 기록을 시작하고(기록은 경기 시작 상태에서만 가능), 기록 중이면 멈추고 Replays/에 저장한다.
        /// </summary>
        void ToggleRecording()
        {
            if (runner.Recording == null)
            {
                RestartKeepingDummy();
                runner.StartRecording(runner.Rules.Format());
                Debug.Log(runner.Recording != null ? "[Knockbox] 입력 기록 시작" : "[Knockbox] 입력 기록을 시작하지 못함");
                return;
            }
            var recording = runner.StopRecording();
            string path = ReplayFiles.Save(recording);
            Debug.Log($"[Knockbox] 입력 기록 저장({recording.Count}틱): {path}");
        }

        /// <summary>F10: Replays/의 가장 최근 기록을 처음부터 재생한다. 끝나면 F5로 평소 경기로 돌아간다.</summary>
        void PlayLatestRecording()
        {
            string path = ReplayFiles.FindLatest();
            if (path == null)
            {
                Debug.Log("[Knockbox] 재생할 입력 기록이 없음(F9로 기록)");
                return;
            }
            if (runner.PlayRecording(ReplayFiles.Load(path)))
                Debug.Log($"[Knockbox] 입력 기록 재생: {path}");
        }

        public static string SurfaceModeName(int mode) => mode == 1 ? "D (stick)" : mode == 2 ? "E (spider: free surface + terrain gravity)" : "off";

        /// <summary>새 경기는 P2를 기본 장치로 되돌리므로, 허수아비를 쓰고 있었다면 다시 붙인다.</summary>
        void RestartKeepingDummy()
        {
            var p2 = runner.PracticeOpponent;
            runner.StartNewMatch();
            if (p2 is DummyInputSource)
                runner.PracticeOpponent = p2;
            else if (p2 is BotInputSource b)
            {
                b.Reset();
                runner.PracticeOpponent = b;
            }
            else if (p2 is BrawlBot bb)
            {
                bb.Reset();
                runner.PracticeOpponent = bb;
            }
            else if (p2 is LungePracticeBot pb)
            {
                pb.Reset();
                runner.PracticeOpponent = pb;
            }
        }

        void CycleP2Input()
        {
            var current = runner.PracticeOpponent;
            IInputSource next;
            if (current is KeyboardInputSource)
                next = GamepadInputSource.Available(0) ? new GamepadInputSource(0) : (IInputSource)dummy;
            else if (current is GamepadInputSource)
                next = dummy;
            else if (current is DummyInputSource)
            {
                // CR-004 실험 경기는 몸 폼 봇.
                if (runner.BrawlExperiment)
                {
                    brawlBot.Reset();
                    next = brawlBot;
                }
                else
                {
                    bot.Reset();
                    next = bot;
                }
            }
            else if (current is BrawlBot)
            {
                // 박치기 받아내기 연습 상대(CR-004 단계 C).
                practiceBot.Reset();
                next = practiceBot;
            }
            else
                next = new KeyboardInputSource(KeyboardLayout.Right);
            runner.PracticeOpponent = next;
            Debug.Log($"[Knockbox] P2 입력원: {MatchPresenter.InputSourceName(next)}");
        }
    }
}
