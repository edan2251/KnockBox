using System;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 디버그 전용 자동 진행(CP24): 명령줄 인수로 봇 대 봇 경기를 빠르게 돌리고 상태 전이를 로그에 남긴다.
    /// 개발 빌드나 에디터에서만 동작한다. 사람이 하는 플레이에는 영향이 없다(인수가 없으면 아무것도 하지 않음).
    ///   -autobots            두 선수를 봇으로(선택 단계도 봇이 고른다)
    ///   -ticksperframe N     프레임마다 N틱 더 진행(시뮬레이션 결과는 같고 화면만 빨리 감김)
    ///   -replaycheck 기록     입력 기록 재생 검사만 하고 끝냄(ReplayCheck, CR-004 온라인 1단계)
    ///   (온라인 인수 -host/-join/-relayhost/-relayjoin은 Online.OnlineMatch가 직접 본다)
    /// 로그 형식: "[Knockbox][flow] t=틱 단계 ..." (Player.log)
    /// </summary>
    [RequireComponent(typeof(MatchRunner))]
    public sealed class AutoPlayDebug : MonoBehaviour
    {
        MatchRunner runner;
        int extraTicks;
        bool bots;
        MatchPhase lastPhase = (MatchPhase)255;
        int matches;
        bool replayCheck;

        public static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static int IntArg(string name, int fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
        }

        void Awake()
        {
            runner = GetComponent<MatchRunner>();
            if (!Debug.isDebugBuild && !Application.isEditor)
            {
                enabled = false;
                return;
            }
            replayCheck = HasArg(ReplayCheck.Arg);
            bots = HasArg("-autobots");
            extraTicks = Mathf.Clamp(IntArg("-ticksperframe", 0), 0, 200);
            if (!bots && extraTicks == 0 && !replayCheck)
                enabled = false;
        }

        void Start()
        {
            if (replayCheck)
            {
                runner.Running = false;
                var outcome = ReplayCheck.RunFromCommandLine(runner);
                if (!Application.isEditor)
                    Application.Quit(outcome.ExitCode);
                enabled = false;
                return;
            }
            if (!bots)
                return;
            runner.Player1Input = new BotInputSource(() => runner.World, 0, 0xA11u);
            runner.Player2Input = new BotInputSource(() => runner.World, 1, 0xB22u);
            runner.Ticked += LogFlow;
            Debug.Log($"[Knockbox][flow] 자동 진행: 봇 대 봇, 프레임마다 추가 {extraTicks}틱, 규칙 {runner.World?.Config.Rules.Format()}");
        }

        void Update()
        {
            for (int i = 0; i < extraTicks && runner.Running && runner.World != null; i++)
                runner.StepOnce();
        }

        void LogFlow(MatchRunner r)
        {
            var w = r.World;
            if (w == null)
                return;
            ref readonly var s = ref w.State;
            ref readonly var m = ref s.Match;
            for (int i = 0; i < w.Events.Count; i++)
            {
                var e = w.Events[i];
                switch (e.Type)
                {
                    case SimEventType.RoundEnd:
                        Debug.Log($"[Knockbox][flow] t={s.Tick} 라운드 {m.Round} 끝: 승자 P{e.Fighter + 1}(무승부면 P0), 라운드 점수 {m.Score0}:{m.Score1}, 세트 {m.Set}");
                        break;
                    case SimEventType.SetEnd:
                        Debug.Log($"[Knockbox][flow] t={s.Tick} 세트 {(int)e.Strength} 끝: 승자 P{e.Fighter + 1}, 세트스코어 {m.SetScore0}:{m.SetScore1}, 라운드 점수 {m.Score0}:{m.Score1}");
                        break;
                    case SimEventType.PickStart:
                        Debug.Log($"[Knockbox][flow] t={s.Tick} 선택 시작: {(e.Fighter < 0 ? "초기 폼(양쪽)" : $"세트 패자 P{e.Fighter + 1}")}, 선택지 {(int)e.Strength}개");
                        break;
                    case SimEventType.Picked:
                        Debug.Log($"[Knockbox][flow] t={s.Tick} 선택 완료 P{e.Fighter + 1}: 선택 수 {m.Picks0}/{m.Picks1}");
                        break;
                    case SimEventType.MatchEnd:
                        matches++;
                        Debug.Log($"[Knockbox][flow] t={s.Tick} 경기 {matches} 끝: 승자 P{e.Fighter + 1}, 세트스코어 {m.SetScore0}:{m.SetScore1}, 선택 수 {m.Picks0}/{m.Picks1}, "
                                  + $"보유 폼 {s.Fighter0.OwnedForms}/{s.Fighter1.OwnedForms}, 증강 {s.Fighter0.Augments.Count}/{s.Fighter1.Augments.Count}");
                        break;
                }
            }
            if (m.Phase == lastPhase)
                return;
            lastPhase = m.Phase;
            if (m.Phase == MatchPhase.FormSelect)
                Debug.Log($"[Knockbox][flow] t={s.Tick} 초기 폼 선택(경기 시작): 선택 수 {m.Picks0}/{m.Picks1}, 세트스코어 {m.SetScore0}:{m.SetScore1}, 보유 폼 {s.Fighter0.OwnedForms}/{s.Fighter1.OwnedForms}, 증강 {s.Fighter0.Augments.Count}/{s.Fighter1.Augments.Count}");
            else if (m.Phase == MatchPhase.Countdown)
                Debug.Log($"[Knockbox][flow] t={s.Tick} 라운드 {m.Round} 시작(세트 {m.Set}): 폼 {s.Fighter0.Form}/{s.Fighter1.Form}, 보유 {s.Fighter0.OwnedForms}/{s.Fighter1.OwnedForms}, "
                          + $"증강 {s.Fighter0.Augments.Count}/{s.Fighter1.Augments.Count}, 체력 {s.Fighter0.Health}/{s.Fighter1.Health}, 에너지 {s.Fighter0.Energy}/{s.Fighter1.Energy}, 투사체 {s.Projectiles.CountActive(-1)}");
        }
    }
}
