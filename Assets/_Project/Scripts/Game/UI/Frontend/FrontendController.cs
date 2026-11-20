using Knockbox.Game.Online;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knockbox.Game.UI
{
    /// <summary>로비 상태와 확정된 시뮬레이션을 화면으로 연결한다. 카드 획득·단계 진행은 SimWorld만 결정한다.</summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(MatchRunner),typeof(OnlineMatch),typeof(OnlineLobby))]
    public sealed class FrontendController : MonoBehaviour
    {
        [SerializeField] MatchRunner runner;
        [SerializeField] MatchPresenter presenter;
        [SerializeField] OnlineMatch online;
        [SerializeField] OnlineLobby lobby;
        bool joinEntry, createEntry;
        int submittedRound=-1, lastPicker=-1, lastPickedAt=-1;
        int shownSlot=-1;
        FrontendScreen previous;
        // 연습은 한 번의 O로 즉시 재시작하므로, 전송 전에 짧게 준비 표시를 보여준다. 온라인은 확정 입력을 그대로 표시한다.
        const float PracticeReadyFeedbackSeconds = .65f;
        SimWorld practiceReadyWorld;
        int practiceReadyRound = -1;
        float practiceReadyAt;
        bool practiceReadySent;
        // 온라인 연결이 끝나면(상대가 나감·끊김·어긋남) 이유를 잠깐 보여준 뒤 메인으로 돌아간다(2026-11-16 게임 흐름 CP2, 시간은 임시값).
        const float EndedNoticeSeconds = 3.5f;
        // 로비에서 끝난 경우(방장이 방을 닫음 등)는 로비 상단에 짧게 알린 뒤 메인으로(2026-11-19).
        const float LobbyEndedNoticeSeconds = 1.6f;
        // 방장 화면: 손님이 로비에서 나가 혼자 기다리게 되면 잠깐 알린다.
        const float PeerLeftNoticeSeconds = 2.4f;
        float peerLeftAt = -10;
        float endedAt = -1;
        bool resultsPreviewPending;
        // 옵션(CP6): 메인 메뉴에서 열고 닫는다. 값은 누를 때마다 바로 적용·저장한다.
        bool optionsOpen;
        GameSettings settings;
        [field: SerializeField] public FrontendView View { get; private set; }
        public static bool UseFrontendForThisRun => !Application.isBatchMode
            && !AutoPlayDebug.HasArg(ReplayCheck.Arg) && !AutoPlayDebug.HasArg("-autobots");

        void Start()
        {
            if (!UseFrontendForThisRun) { if(View!=null)View.gameObject.SetActive(false); enabled=false; return; }
            runner ??=GetComponent<MatchRunner>();presenter ??=GetComponent<MatchPresenter>();
            online ??=GetComponent<OnlineMatch>();lobby ??=GetComponent<OnlineLobby>();
            runner.FrontendEnabled=true;
            if(!online.Active) {runner.StartNewMatch();runner.Running=false;runner.MenuInputBlocked=true;}
            if (View != null && View.IsAuthored) View.BindAuthored(runner,presenter);
            else if(gameObject.scene.name=="Main") throw new System.InvalidOperationException("Main requires an authored FrontendView reference.");
            else { View=gameObject.AddComponent<FrontendView>(); View.Build(runner,presenter); }
            View.CreateRoom+=()=>{joinEntry=false;createEntry=true;lobby.CreateRoom();};
            View.OpenJoin+=()=>{joinEntry=true;createEntry=false;};
            View.JoinRoom+=Join;
            View.Practice+=()=>{joinEntry=false;createEntry=false;lobby.StartPractice();};
            View.Back+=()=>{joinEntry=false;createEntry=false;lobby.CancelAndOpen();};
            View.CopyCode+=()=>GUIUtility.systemCopyBuffer=lobby.RoomCode;
            View.CardClicked+=Choose;
            View.Rematch+=ReadyForRematch;
            online.ReturnedToSoloLobby+=()=>peerLeftAt=Time.unscaledTime;
            runner.PointerOverFrontendButton=View.PointerOverPodiumButton;
            View.Quit+=QuitGame;
            View.Options+=()=>{optionsOpen=true;View.SetOptionLabels(settings.Labels(),GameSettings.ControlsGuide);};
            View.OptionPressed+=PressOption;
            settings=GameSettings.Load();settings.Apply(presenter);
            presenter.SetDebugLabels(false);
            if(presenter.Help!=null)presenter.Help.Visible=false;
            // 제작기 시험(CP4): 경기 맵은 바로 연습, 로비 맵은 혼자 기다리는 로비, 결과 공간 맵은 연습을 시작해 첫 싸움에서 P1 승리로 바로 결과 공간까지 간다.
            if(runner.PreviewUsage==Knockbox.Game.Maps.MapUsage.Lobby)lobby.StartLobbyPreview();
            else if(runner.IsAuthoringPreview){lobby.StartPractice();resultsPreviewPending=runner.PreviewUsage==Knockbox.Game.Maps.MapUsage.Results;}
        }

        void Join() {lobby.JoinCode=View.CodeInput.text;lobby.Join();}

        void PressOption(int index)
        {
            switch(index)
            {
                case 0: settings.CycleVolume(); break;
                case 1: settings.Fullscreen=!settings.Fullscreen; break;
                case 2: settings.NextResolution(); break;
                case 3: settings.ReduceShake=!settings.ReduceShake; break;
                case 4: settings.ReduceFlash=!settings.ReduceFlash; break;
                case 5: settings.ResetToDefaults(); break;
                case FrontendView.OptionBack: optionsOpen=false; return;
            }
            settings.Apply(presenter);settings.Save();
            View.SetOptionLabels(settings.Labels(),GameSettings.ControlsGuide);
        }

        /// <summary>게임 종료: 온라인 방에 있으면 먼저 나간다. 에디터에서는 Play를 끝낸다.</summary>
        void QuitGame()
        {
            if(online.Active)lobby.CancelAndOpen();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }

        void ReadyForRematch()
        {
            if (View.Screen == FrontendScreen.Podium) { TogglePodiumReady(); return; }
            if (!View.RematchVisible || practiceReadyWorld != null) return;
            if (online.Active || runner.World.Config.Match.RematchBothHold)
            {
                runner.RequestRematch(runner.LocalPlayerSlot);
                return;
            }
            practiceReadyWorld = runner.World;
            practiceReadyRound = runner.World.State.Match.Round;
            practiceReadyAt = Time.unscaledTime;
            practiceReadySent = false;
        }

        /// <summary>
        /// 결과 공간의 다시하기 준비(R 키·O 버튼): 한 번 누르면 켜지고 다시 누르면 꺼진다.
        /// 온라인은 바로 준비 비트를 켜고 끈다. 연습은 켜는 즉시 재시작되므로 기존처럼 짧게 준비 표시를 보여준 뒤 보낸다(그 사이 다시 누르면 취소).
        /// </summary>
        void TogglePodiumReady()
        {
            if (!View.PodiumReadyVisible) return;
            int slot = runner.LocalPlayerSlot;
            if (online.Active || runner.World.Config.Match.RematchBothHold)
            {
                runner.ToggleReady(slot);
                return;
            }
            if (practiceReadyWorld != null && !practiceReadySent) { practiceReadyWorld = null; practiceReadyRound = -1; return; }
            if (runner.IsReadyToggled(slot)) { runner.ToggleReady(slot); return; }
            practiceReadyWorld = runner.World;
            practiceReadyRound = runner.World.State.Match.Round;
            practiceReadyAt = Time.unscaledTime;
            practiceReadySent = false;
        }

        void UpdatePracticeReady()
        {
            if (practiceReadyWorld == null) return;
            if (runner.World != practiceReadyWorld || runner.World.State.Match.Phase != MatchPhase.MatchEnd
                || runner.World.State.Match.Round != practiceReadyRound || lobby.IsOpen || online.Active)
            {
                practiceReadyWorld = null; practiceReadyRound = -1;
                return;
            }
            if (!practiceReadySent && Time.unscaledTime - practiceReadyAt >= PracticeReadyFeedbackSeconds)
                practiceReadySent = runner.World.InFreeSpace ? runner.ToggleReady(runner.LocalPlayerSlot) : runner.RequestRematch(runner.LocalPlayerSlot);
        }

        /// <summary>
        /// 연결이 끝난 쪽(상대가 X로 나감·연결 끊김 등)도 이유를 보여준 뒤 스스로 메인으로 돌아간다. 내가 나간 쪽은 이미 바로 메인이다.
        /// 끝난 동안의 화면은 기존 방 화면의 "연결 종료"와 이유이며, 돌아가기를 누르면 바로 돌아간다.
        /// </summary>
        void ReturnToMainAfterSessionEnded()
        {
            if(!online.Ended){endedAt=-1;return;}
            if(endedAt<0){endedAt=Time.unscaledTime;return;}
            if(Time.unscaledTime-endedAt<(EndedInLobby?LobbyEndedNoticeSeconds:EndedNoticeSeconds))return;
            endedAt=-1;joinEntry=false;createEntry=false;
            lobby.CancelAndOpen();
        }

        /// <summary>이번 라운드에 카드 선택을 이미 보냈는지. 온라인 확정 전에 두 번째 카드를 보내지 않게 한다.</summary>
        public bool CardSubmitted => runner != null && runner.FrontendWorld != null && submittedRound == runner.FrontendWorld.State.Match.Round;

        /// <summary>모놀리스 선택 무대도 기존 카드 UI와 같은 경로(FrontendInputSource.Choose)로 선택을 보낸다.</summary>
        public void ChooseCard(int index) => Choose(index);

        /// <summary>모놀리스 무대가 켜져 있으면 기존 카드 화면 대신 경기 화면(Hidden) 위에서 고른다.</summary>
        bool MonolithPick => presenter != null && presenter.CardPickStage != null && presenter.CardPickStage.UsesMonoliths;

        void Choose(int index)
        {
            if(submittedRound==runner.FrontendWorld.State.Match.Round)return;
            if(runner.RequestCardSelection(runner.LocalPlayerSlot,index))
            {
                submittedRound=runner.FrontendWorld.State.Match.Round;
                View.CanChoose=false;
                View.BeginCardFeedback(index);
            }
        }

        /// <summary>연결이 로비(혼자·공유 로비 또는 퇴장 전환)에서 끝났는지.</summary>
        bool EndedInLobby => online.Ended && runner.World != null && (runner.World.InLobby || runner.World.InLobbyExit);

        void Update()
        {
            if(View==null || runner.World==null)return;
            var keyboard=Keyboard.current;
            // 로비의 나가기는 Esc 하나(2026-11-19). 방장은 방을 닫고 메인으로(손님도 메인으로 돌아간다),
            // 손님은 메인으로 가고 방장은 같은 로비에서 같은 코드로 다시 기다린다. 둘 다 준비해 퇴장 전환이 시작된 뒤에는 받지 않는다.
            bool roomLobby=View.Screen==FrontendScreen.Lobby && (online.Active||lobby.Reopening) && !online.Ended;
            // 결과 공간의 나가기도 Esc(2026-11-20: 화면 한쪽의 "[Esc] 나가기"). 기존 결과 공간 나가기(X)와 같은 동작이다.
            if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame && View.Screen==FrontendScreen.Podium && !online.Ended)
            {joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
            else if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame && roomLobby)
            {
                if(!runner.World.InLobbyExit && !runner.FrontendWorld.InLobbyExit){joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
            }
            else if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame && !online.Active)
            {
                if(optionsOpen) optionsOpen=false;
                else if(joinEntry||createEntry||lobby.Busy) {joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
                else if(View.Screen==FrontendScreen.Main&&View.StartMenuOpen) View.SetMenuPage(false);
                else lobby.TogglePracticeMenu();
            }
            if(joinEntry && keyboard!=null && keyboard.enterKey.wasPressedThisFrame && !lobby.Busy && !online.Active)Join();
            if((View.Screen==FrontendScreen.Podium||View.Screen==FrontendScreen.Lobby) && keyboard!=null && keyboard.rKey.wasPressedThisFrame)TogglePodiumReady();
            ReturnToMainAfterSessionEnded();
            if(!lobby.IsOpen||online.Active)optionsOpen=false;
            int slot=runner.LocalPlayerSlot;
            if(slot!=shownSlot){shownSlot=slot;View.SetLocalSlot(slot);}
            var m=runner.FrontendWorld.State.Match;
            FrontendScreen desired;
            var session=online.Session;
            // 방장이 혼자 기다리는 로컬 로비와 둘이 들어온 공유 로비는 같은 로비 화면이다(2026-11-16 CP3).
            bool soloLobby=(session==null || session.State==SessionState.WaitingPeer) && runner.AbsentSlot>=0 && runner.World.InLobby;
            if(resultsPreviewPending && runner.World.State.Match.Phase==MatchPhase.Fighting) resultsPreviewPending=!runner.TryDebugMatchWin(0);
            if(soloLobby) desired=FrontendScreen.Lobby;
            else if(EndedInLobby) desired=FrontendScreen.Lobby;
            else if(session!=null && session.State!=SessionState.Running) desired=FrontendScreen.Waiting;
            else if(!online.Active && lobby.IsOpen) desired=optionsOpen?FrontendScreen.Options:joinEntry?FrontendScreen.Join:createEntry||lobby.Busy?FrontendScreen.Waiting:FrontendScreen.Main;
            else if(runner.FrontendWorld.InLobby || runner.FrontendWorld.InLobbyExit || runner.World.InLobbyExit) desired=FrontendScreen.Lobby;
            else if(m.Phase==MatchPhase.Countdown && m.Round==1 && m.PhaseTick<View.IntroRevealTick) desired=FrontendScreen.Versus;
            else if(m.Phase==MatchPhase.SetPick) desired=MonolithPick?FrontendScreen.Hidden:FrontendScreen.Cards;
            else if(m.Phase==MatchPhase.MatchEnd) desired=runner.FrontendWorld.InResultsSpace?FrontendScreen.Podium:FrontendScreen.Result;
            else desired=FrontendScreen.Hidden;
            if(desired!=previous)
            {
                previous=desired;View.Show(desired);
                if(desired!=FrontendScreen.Cards)submittedRound=-1;
                if(desired==FrontendScreen.Versus||desired==FrontendScreen.Cards)presenter.SfxPlayer.Play(Presentation.Sfx.Dash,.25f,1.3f);
            }
            runner.MenuInputBlocked=!(online.Active||lobby.Reopening)&&lobby.IsOpen;
            UpdatePracticeReady();
            // 과거 리플레이/개발 시나리오는 원래 HUD와 선택 화면을 사용한다.
            bool oldReplay=!runner.World.Config.Match.CollectCardsOnly && !lobby.IsOpen;
            View.Canvas.enabled=!oldReplay;
            if(oldReplay) {View.Show(FrontendScreen.Hidden);previous=FrontendScreen.Hidden;presenter.SetReadyMarkers(false,false,false);presenter.SetLobbyMarkers(false,false,false);return;}
            View.Inventory(runner.FrontendWorld.State.Fighter0.Augments,runner.FrontendWorld.State.Fighter1.Augments);
            View.Score(m.SetScore0,m.SetScore1);
            if(desired==FrontendScreen.Join||desired==FrontendScreen.Waiting)
            {
                string title=joinEntry?"코드로 참가":"방 만들기";
                string message=lobby.StatusMessage;
                bool canCopy=session!=null&&session.State==SessionState.WaitingPeer;
                if(canCopy)message="상대를 기다리는 중…";
                else if(session!=null&&session.State==SessionState.Connecting)message="상대와 연결하는 중…";
                else if(session!=null&&session.State==SessionState.Ended){title="연결 종료";message=EndMessage(session);}
                View.Room(title,lobby.RoomCode,message,lobby.Busy,canCopy);
            }
            if(desired==FrontendScreen.Cards)
            {
                int index=-1;
                if(m.PickedAt>=0)
                {
                    var owned=runner.FrontendWorld.State.GetFighter(m.Picker).Augments;
                    if(owned.Count>0)index=CardCollectionRules.CardIndex(owned.Get(owned.Count-1));
                }
                View.Cards(m.Picker,m.Picker==slot && m.PickedAt<0 && submittedRound!=m.Round,index,
                    m.PickedAt<0?0:Mathf.Min(1,(m.PhaseTick-m.PickedAt)/(float)FrontendView.CardUiHoldTicks(runner.World.Config.Match)),
                    Mathf.Max(0,Mathf.CeilToInt((runner.World.Config.Match.PickTimeoutTicks-m.PhaseTick)/60f)));
            }
            // 획득 효과음은 기존 카드 화면과 모놀리스 무대가 같은 확정 시점을 쓴다.
            if(desired==FrontendScreen.Cards||(m.Phase==MatchPhase.SetPick&&MonolithPick))
            {
                if(m.PickedAt>=0&&(m.PickedAt!=lastPickedAt||m.Picker!=lastPicker))presenter.SfxPlayer.Play(Presentation.Sfx.Grab,.5f,1.25f);
                lastPickedAt=m.PickedAt;lastPicker=m.Picker;
            }
            else {lastPickedAt=-1;lastPicker=-1;}
            bool podium=desired==FrontendScreen.Podium, lobbyScreen=desired==FrontendScreen.Lobby;
            bool localReady=(podium||lobbyScreen) && (runner.IsReadyToggled(slot) || (practiceReadyWorld!=null && !practiceReadySent));
            if(podium)
                View.Podium(SimWorld.ResultsWinner(m),m.SetScore0,m.SetScore1,online.Active,localReady);
            // 준비 표시: 내 쪽은 누른 즉시(의도), 상대는 확정된 입력으로 표시한다.
            var confirmed=runner.FrontendWorld.State;
            bool ready0=slot==0?localReady:(confirmed.Fighter0.PrevButtons&SimWorld.ReadyButton)!=0;
            bool ready1=slot==1?localReady:(confirmed.Fighter1.PrevButtons&SimWorld.ReadyButton)!=0;
            if(lobbyScreen)
            {
                View.Lobby(soloLobby,localReady);
                bool host=session!=null?session.LocalSlot==0:lobby.Reopening;
                string notice="";
                if(EndedInLobby) notice=session!=null&&session.LocalSlot==1&&session.Reason==EndReason.PeerLeft?"방장이 방을 닫았습니다":EndMessage(session);
                else if(soloLobby&&Time.unscaledTime-peerLeftAt<PeerLeftNoticeSeconds) notice="상대가 나갔습니다 · 같은 코드로 다시 들어올 수 있어요";
                View.LobbyHud(new FrontendView.LobbyHudState
                {
                    Solo=soloLobby, Reopening=lobby.Reopening,
                    ShowCode=host&&soloLobby&&session!=null&&session.State==SessionState.WaitingPeer&&!string.IsNullOrEmpty(lobby.RoomCode),
                    Code=lobby.RoomCode, Ready0=ready0, Ready1=ready1, Notice=notice,
                });
            }
            // 머리 위 준비 표식: 로비는 "P1/P2 + 삼각형"(2026-11-19, 혼자 기다릴 때도 내 머리 위). 결과 발표가 켜진 결과 공간(2026-11-20)도
            // 조작이 풀리면 같은 표시로 내 머리 위 "[R] 재대전"·상대 "준비 전"(연습은 상대 상태 없음)을 띄운다. 그 전 설정은 기존 고리.
            var fw=runner.FrontendWorld;
            bool ceremony=podium && fw.InResultsSpace && fw.Config.Match.ResultsCeremonyTicks>0;
            bool resultsOpen=podium && fw.InFreeSpace && fw.State.Match.PhaseTick>=fw.FreeSpaceUnlockTick;
            presenter.SetReadyMarkers(resultsOpen && !ceremony, ready0, ready1);
            if(ceremony) presenter.SetLobbyMarkers(resultsOpen && !online.Ended, ready0, ready1, slot, View.PodiumReadyVisible, false, "[R] 재대전", online.Active);
            else presenter.SetLobbyMarkers(lobbyScreen && !EndedInLobby, ready0, ready1, slot, View.PodiumReadyVisible, soloLobby);
            if(desired==FrontendScreen.Result)
                View.Result(m.SetScore0>=m.SetScore1?0:1,m.SetScore0,m.SetScore1,online.Active,
                    (runner.FrontendWorld.State.Fighter0.PrevButtons&InputButtons.Attack)!=0 || (practiceReadyWorld!=null && slot==0),
                    (runner.FrontendWorld.State.Fighter1.PrevButtons&InputButtons.Attack)!=0 || (practiceReadyWorld!=null && slot==1));
        }

        public static string EndMessage(LockstepSession session) =>
            session.Reason==EndReason.PeerLeft?"상대가 방을 나갔습니다.":session.Reason==EndReason.Timeout?"연결이 끊겼습니다.":
            session.Reason==EndReason.Rejected?"서로 같은 버전의 게임으로 접속해주세요.":
            session.Reason==EndReason.Desync?"경기 상태가 달라 진행을 멈췄습니다.":"연결이 종료되었습니다.";
    }
}
