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
        bool joinEntry, createEntry, joinQueued;
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
        // 메인 화면의 로비 공간(2026-11-21): 게임 시작 메뉴·코드 입력·방 만들기 대기 동안 여는 혼자 로비 월드(기록·전송 없음, 조작 잠김).
        // 방을 만들면 이 월드가 그대로 방장의 혼자 로비가 된다(OnlineMatch가 이어 감). 로비 맵이 없는 설정(Sandbox 등)은 열지 않는다.
        SimWorld spaceWorld;
        bool spaceAvailable;
        // 뒤로로 공간이 닫히는 중에 연습을 누르면(빠른 반복) 다 닫힌 뒤 바로 연습을 시작한다.
        bool practiceQueued;
        // Esc로 멈춘 연습 월드(같은 월드일 때만 Esc로 이어 간다).
        SimWorld pausedPractice;
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
            View.CreateRoom+=()=>{joinEntry=false;createEntry=true;lobby.CreateRoom();LeaveSpaceForRoom(FrontendView.SpaceExit.Room);};
            View.OpenJoin+=()=>{joinEntry=true;createEntry=false;};
            View.JoinRoom+=Join;
            View.Practice+=()=>{joinEntry=false;createEntry=false;if(!LeaveSpaceForPractice())lobby.StartPractice();};
            View.Back+=()=>{joinQueued=false;joinEntry=false;createEntry=false;lobby.CancelAndOpen();};
            View.CopyCode+=()=>GUIUtility.systemCopyBuffer=lobby.RoomCode;
            View.CardClicked+=Choose;
            View.Rematch+=ReadyForRematch;
            online.ReturnedToSoloLobby+=()=>peerLeftAt=Time.unscaledTime;
            runner.PointerOverFrontendButton=()=>View.PointerOverPodiumButton() || View.PointerOverEscapeMenu();
            View.Surrender+=()=>{if(online.Active)online.RequestMatchCommand(MatchCommand.Surrender);else runner.RequestMatchCommand(MatchCommand.Surrender);};
            View.LeaveMatch+=()=>{if(online.Active)online.RequestMatchCommand(MatchCommand.Leave);else lobby.CancelAndOpen();};
            View.Quit+=QuitGame;
            View.Options+=()=>{optionsOpen=true;View.SetOptionLabels(settings.Labels(),GameSettings.ControlsGuide);};
            View.OptionPressed+=PressOption;
            settings=GameSettings.Load();settings.Apply(presenter);
            View.NicknameEdited+=value=>{settings.SaveNickname(value);View.Nickname=settings.Nickname;online.LocalName=settings.Nickname;};
            View.Nickname=settings.Nickname;
            spaceAvailable=!runner.IsAuthoringPreview && runner.CreateConfig().Match.LobbySpace;
            presenter.SetDebugLabels(false);
            if(presenter.Help!=null)presenter.Help.Visible=false;
            // 제작기 시험(CP4): 경기 맵은 바로 연습, 로비 맵은 혼자 기다리는 로비, 결과 공간 맵은 연습을 시작해 첫 싸움에서 P1 승리로 바로 결과 공간까지 간다.
            if(runner.PreviewUsage==Knockbox.Game.Maps.MapUsage.Lobby)lobby.StartLobbyPreview();
            else if(runner.IsAuthoringPreview){lobby.StartPractice();resultsPreviewPending=runner.PreviewUsage==Knockbox.Game.Maps.MapUsage.Results;}
        }

        void Join()
        {
            if(joinQueued || lobby.Busy || online.Active || View.SpaceExiting) return;
            lobby.JoinCode=View.CodeInput.text;
            if(!string.IsNullOrWhiteSpace(lobby.JoinCode)) LeaveSpaceForRoom(FrontendView.SpaceExit.Join);
            // 참가는 접속 직후 월드가 교체된다. 방 만들기처럼 도약·낙하를 끝까지 보여준 다음 요청한다.
            joinQueued=View.CurrentSpaceExit==FrontendView.SpaceExit.Join;
            if(!joinQueued) lobby.Join();
        }

        /// <summary>
        /// 방 만들기·참가(2026-11-21 2차): 처음 메인 화면에서 로비로 넘어가던 것처럼 선수가 한 번 튀어 올랐다가 아래로 떨어져 나가고
        /// 넓은 로비 구도로 물러난다. 방 만들기는 이어서 같은 장면에 선수가 다시 떨어지고(방이 열리면 이 월드가 혼자 로비),
        /// 참가는 공유 로비가 장면을 이어 받는다(연결이 안 되면 다시 떨어진다).
        /// </summary>
        void LeaveSpaceForRoom(FrontendView.SpaceExit kind)
        {
            if(spaceWorld==null || runner.World!=spaceWorld || View.SpaceExiting || View.SpaceFighterAway) return;
            View.BeginSpaceExit(kind);
            if(kind==FrontendView.SpaceExit.Join) runner.KeepLobbySceneOnNextBegin=true;
        }

        /// <summary>
        /// 메인 화면 공간 월드의 설정: Main 설정 그대로에 선수가 떨어지기 시작하는 틱만 앞당긴다(2026-11-21: 게임 시작 뒤 캐릭터가 늦게 나옴).
        /// 기록·전송하지 않는 로컬 월드이며, 방장 혼자 로비로 이어질 수 있는 월드(방 만들기 뒤 다시 떨어뜨림·대기 화면)에는 쓰지 않는다.
        /// </summary>
        SimConfig SpaceConfig()
        {
            var config=runner.CreateConfig();
            var match=config.Match;
            match.ResultsDropTick=FrontendView.MenuDropTick;
            return new SimConfig(config.Movement,match,config.Combat,config.Rules,config.Augments);
        }

        /// <summary>같은 로비 장면(이어 간 장면)에 선수만 다시 떨어뜨린다. 기록·전송 없는 로컬 혼자 로비 월드다.</summary>
        void RedropSpace()
        {
            if(runner.PredictionDriven) return;
            // 방 만들기 뒤 다시 떨어뜨린 월드는 방장 혼자 로비로 이어지므로 Main 설정 그대로다.
            var config=runner.CreateConfig();
            runner.KeepLobbySceneOnNextBegin=false;
            runner.StartSoloLobby(config,runner.PrepareMatchArenas(config),(uint)System.Environment.TickCount|1u);
            runner.MarkLobbyContinued(-1);
            spaceWorld=runner.World;
            View.SpaceFighterAway=false;
        }

        /// <summary>게임 시작 메뉴의 연습: 로비 공간이 열려 있으면 로비 → 경기와 같은 퇴장 뒤 연습을 시작한다(그동안 누름은 무시).</summary>
        bool LeaveSpaceForPractice()
        {
            if(View.CurrentSpaceExit==FrontendView.SpaceExit.Back){practiceQueued=true;return true;}
            if(View.SpaceExiting) return true;
            if(spaceWorld==null || runner.World!=spaceWorld) return false;
            View.BeginSpaceExit(FrontendView.SpaceExit.Practice);
            return true;
        }

        /// <summary>
        /// 메인 화면의 로비 공간(2026-11-21): 게임 시작 메뉴·코드 입력·방 만들기 대기에서는 혼자 로비 월드를 열고(바닥·조명·낙하는 로비 입장 연출),
        /// 첫 화면으로 돌아가면 선수가 왼쪽으로 뛰어 나가고 공간이 걷힌 뒤 원래의 숨은 연습 월드로 되돌린다.
        /// 닫는 동안 다시 게임 시작을 누르면 다 닫힌 다음 새로 연다(중복 생성·되감기 없음). 방장이 되면 월드를 넘기고, 손님은 선수만 나간 뒤 공유 로비가 장면을 이어 간다.
        /// </summary>
        void UpdateMenuSpace()
        {
            if(spaceWorld!=null && runner.World!=spaceWorld) ReleaseSpace();
            // 방장: 선수가 다시 떨어진 뒤에는 이 월드가 그대로 혼자 로비다(손님은 공유 로비로 월드가 바뀔 때 놓는다).
            if(spaceWorld!=null && online.Active && online.Session.LocalSlot==0 && !View.SpaceExiting && !View.SpaceFighterAway){ReleaseSpace();return;}
            if(View.SpaceExitFinished)
            {
                var kind=View.CurrentSpaceExit;
                if(kind==FrontendView.SpaceExit.Practice){ReleaseSpace();lobby.StartPractice();return;}
                if(kind==FrontendView.SpaceExit.Back && practiceQueued){ReleaseSpace();lobby.StartPractice();return;}
                if(kind==FrontendView.SpaceExit.Back){ReleaseSpace();runner.StartNewMatch();runner.Running=false;}
                else if(kind==FrontendView.SpaceExit.Room){View.ClearSpaceExit();RedropSpace();}
                else if(kind==FrontendView.SpaceExit.Join)
                {
                    View.ClearSpaceExit();View.SpaceFighterAway=true;
                    if(joinQueued){joinQueued=false;lobby.Join();}
                }
            }
            // 참가가 안 되었으면(코드 오류·연결 실패·돌아가기) 같은 장면에 선수를 다시 떨어뜨린다.
            if(spaceWorld!=null && View.SpaceFighterAway && !online.Active && !lobby.Busy) RedropSpace();
            bool want=spaceAvailable && !online.Active && !lobby.Reopening && lobby.IsOpen && !optionsOpen
                && ((View.Screen==FrontendScreen.Main && View.StartMenuOpen) || View.Screen==FrontendScreen.Join || View.Screen==FrontendScreen.Waiting);
            if(spaceWorld==null && want && !View.SpaceExiting)
            {
                // 게임 시작 메뉴에서 연 공간만 빨리 떨어지는 설정을 쓴다. 방 만들기 대기·코드 입력에서 연 공간은 방장 혼자 로비로
                // 이어질 수 있으므로 Main 설정 그대로다(온라인으로 이어지는 월드에 다른 설정이 섞이지 않게).
                var config=View.Screen==FrontendScreen.Main?SpaceConfig():runner.CreateConfig();
                runner.KeepLobbySceneOnNextBegin=false;
                runner.StartSoloLobby(config,runner.PrepareMatchArenas(config),(uint)System.Environment.TickCount|1u);
                spaceWorld=runner.World;
            }
            else if(spaceWorld!=null && !want && !online.Active && !View.SpaceExiting) View.BeginSpaceExit(FrontendView.SpaceExit.Back);
            View.MenuSpaceActive=spaceWorld!=null;
            // 열려 있는 동안 월드는 계속 돈다(코드 입력에서 돌아가기가 멈춰도 다시 돈다). 닫는 동안은 멈춘 그림에서 퇴장만 그린다.
            if(spaceWorld!=null && !online.Active) runner.Running=!View.SpaceExiting || View.CurrentSpaceExit==FrontendView.SpaceExit.Room || View.CurrentSpaceExit==FrontendView.SpaceExit.Join;
        }

        void ReleaseSpace()
        {
            spaceWorld=null;
            joinQueued=false;
            practiceQueued=false;
            View.SpaceFighterAway=false;
            View.ClearSpaceExit();
            View.MenuSpaceActive=false;
        }

        /// <summary>
        /// 이름(2026-11-21 닉네임): 내 자리는 저장한 닉네임, 상대 자리는 온라인 상대가 보낸 닉네임(없으면 P1/P2).
        /// 로비 머리 위·경기 점수판·카드 선택 표시·결과 머리 위가 같은 이름을 쓴다. 판정·기록과 무관하다.
        /// </summary>
        void UpdateNames(int slot)
        {
            online.LocalName=settings.Nickname;
            string mine=GameSettings.DisplayName(settings.Nickname,!online.Active && View.MenuSpaceActive ? View.MenuPreviewSlot : slot);
            string theirs=online.Active && !string.IsNullOrEmpty(online.PeerName)?online.PeerName:GameSettings.SlotName(1-slot);
            presenter.SetPlayerNames(slot==0?mine:theirs,slot==0?theirs:mine);
            View.Points?.SetNames(presenter.PlayerName(0),presenter.PlayerName(1));
        }

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
        /// 경기 중 종료는 중앙의 제목·안내·돌아가기만 표시하며, 돌아가기를 누르면 바로 돌아간다.
        /// </summary>
        void ReturnToMainAfterSessionEnded()
        {
            if(!online.Ended && !online.PeerDeparture){endedAt=-1;return;}
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
            if (online.DepartureDelivered)
            {
                View.SetEscapeMenu(false); joinEntry=false; createEntry=false; lobby.CancelAndOpen();
            }
            bool battleMenu = online.Active && !online.Ended && !online.DeparturePending && !online.PeerDeparture
                && SimWorld.CanForfeit(runner.FrontendWorld.State.Match, runner.FrontendWorld.Config.Match)
                && View.Screen != FrontendScreen.Lobby && View.Screen != FrontendScreen.Waiting;
            if (!battleMenu && View.EscapeMenuOpen) View.SetEscapeMenu(false);
            // 로비의 나가기는 Esc 하나(2026-11-19). 방장은 방을 닫고 메인으로(손님도 메인으로 돌아간다),
            // 손님은 메인으로 가고 방장은 같은 로비에서 같은 코드로 다시 기다린다. 둘 다 준비해 퇴장 전환이 시작된 뒤에는 받지 않는다.
            bool roomLobby=View.Screen==FrontendScreen.Lobby && (online.Active||lobby.Reopening) && !online.Ended;
            // 결과 공간의 나가기도 Esc(2026-11-20: 화면 한쪽의 "[Esc] 나가기"). 기존 결과 공간 나가기(X)와 같은 동작이다.
            // 닉네임을 고치는 중의 Esc는 고치기만 취소한다(입력칸이 처리, 메뉴는 움직이지 않음).
            bool escape=keyboard!=null && keyboard.escapeKey.wasPressedThisFrame && !View.NameInputBusy;
            if(escape && battleMenu) View.SetEscapeMenu(!View.EscapeMenuOpen);
            else if(escape && View.Screen==FrontendScreen.Podium && !online.Ended)
            {joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
            else if(escape && roomLobby)
            {
                if(!runner.World.InLobbyExit && !runner.FrontendWorld.InLobbyExit){joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
            }
            else if(escape && !online.Active)
            {
                if(optionsOpen) optionsOpen=false;
                else if(View.CurrentSpaceExit==FrontendView.SpaceExit.Practice) {}
                else if(joinEntry||createEntry||lobby.Busy) {joinQueued=false;joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
                else if(View.Screen==FrontendScreen.Main&&View.StartMenuOpen) View.SetMenuPage(false);
                // 연습 중 Esc는 메뉴(일시정지)를 열고, 그렇게 멈춘 연습만 메뉴에서 Esc로 다시 이어 간다. 처음 켠 메인 화면의 Esc는
                // 아무것도 하지 않는다(2026-11-21: 숨은 연습 월드가 바로 시작되던 문제). 메인 화면 공간이 있는 동안도 마찬가지.
                else if(spaceWorld==null && !View.SpaceExiting)
                {
                    if(!lobby.IsOpen){pausedPractice=runner.World;lobby.TogglePracticeMenu();}
                    else if(pausedPractice!=null && runner.World==pausedPractice){pausedPractice=null;lobby.TogglePracticeMenu();}
                }
            }
            if(joinEntry && View.CodeSubmitPressed && !View.NameInputBusy && !lobby.Busy && !online.Active)Join();
            if((View.Screen==FrontendScreen.Podium||View.Screen==FrontendScreen.Lobby) && keyboard!=null && keyboard.rKey.wasPressedThisFrame)TogglePodiumReady();
            ReturnToMainAfterSessionEnded();
            if(!lobby.IsOpen||online.Active)optionsOpen=false;
            UpdateMenuSpace();
            int slot=runner.LocalPlayerSlot;
            if(slot!=shownSlot){shownSlot=slot;View.SetLocalSlot(slot);}
            UpdateNames(slot);
            var m=runner.FrontendWorld.State.Match;
            FrontendScreen desired;
            var session=online.Session;
            // 방장이 혼자 기다리는 로컬 로비와 둘이 들어온 공유 로비는 같은 로비 화면이다(2026-11-16 CP3).
            // 메뉴가 열려 있는 동안의 혼자 로비 월드는 메인 화면의 로비 공간이다(2026-11-21). 로비 화면은 방을 만든 뒤(손님이 나가 같은 코드로
            // 다시 여는 동안 포함)·로비 맵 시험에서만.
            bool soloLobby=(session==null || session.State==SessionState.WaitingPeer) && runner.AbsentSlot>=0 && runner.World.InLobby
                && (online.Active || lobby.Reopening || !lobby.IsOpen) && spaceWorld==null;
            if(resultsPreviewPending && runner.World.State.Match.Phase==MatchPhase.Fighting) resultsPreviewPending=!runner.TryDebugMatchWin(0);
            if(online.PeerDeparture || (online.Ended && !EndedInLobby)) desired=FrontendScreen.Ended;
            else if(soloLobby) desired=FrontendScreen.Lobby;
            else if(EndedInLobby) desired=FrontendScreen.Lobby;
            else if(session!=null && session.State!=SessionState.Running) desired=FrontendScreen.Waiting;
            else if(!online.Active && lobby.IsOpen) desired=optionsOpen?FrontendScreen.Options:joinQueued||lobby.Busy?FrontendScreen.Waiting:joinEntry?FrontendScreen.Join:createEntry?FrontendScreen.Waiting:FrontendScreen.Main;
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
            runner.MenuInputBlocked=View.EscapeInputBlocked || online.DeparturePending || online.PeerDeparture || (!(online.Active||lobby.Reopening)&&lobby.IsOpen);
            // 메인 화면 공간에서 방을 만들거나 참가하는 동안은 예전 방 패널을 띄우지 않는다(오류가 나면 이유를 보여 준다).
            View.HideRoomPanel=spaceWorld!=null && View.Screen==FrontendScreen.Waiting && (joinQueued||lobby.Busy||online.Active);
            UpdatePracticeReady();
            // 과거 리플레이/개발 시나리오는 원래 HUD와 선택 화면을 사용한다.
            bool oldReplay=!runner.World.Config.Match.CollectCardsOnly && !lobby.IsOpen;
            View.Canvas.enabled=!oldReplay;
            if(oldReplay) {View.Show(FrontendScreen.Hidden);previous=FrontendScreen.Hidden;presenter.SetReadyMarkers(false,false,false);presenter.SetLobbyMarkers(false,false,false);return;}
            View.Inventory(runner.FrontendWorld.State.Fighter0.Augments,runner.FrontendWorld.State.Fighter1.Augments);
            View.Score(m.SetScore0,m.SetScore1);
            if(desired==FrontendScreen.Ended)
                View.MatchEnded(online.PeerDeparture || session.Reason==EndReason.PeerLeft
                    ? "상대가 게임을 종료했습니다" : online.EndNotice);
            else if(desired==FrontendScreen.Join||desired==FrontendScreen.Waiting)
            {
                string title=joinEntry?"코드로 참가":"방 만들기";
                string message=lobby.StatusMessage;
                bool canCopy=session!=null&&session.State==SessionState.WaitingPeer;
                if(canCopy)message="상대를 기다리는 중…";
                else if(session!=null&&session.State==SessionState.Connecting)message="상대와 연결하는 중…";
                else if(session!=null&&session.State==SessionState.Ended){title="경기 종료";message=online.EndNotice;}
                View.Room(title,lobby.RoomCode,message,joinQueued||lobby.Busy,canCopy);
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
            // 메인 화면 공간(2026-11-21)의 선수 머리 위에는 이름(닉네임)과 삼각형만 띄운다.
            else if(View.MenuSpaceShowsName && !lobbyScreen) presenter.SetLobbyMarkers(true, false, false, slot, false, true);
            else presenter.SetLobbyMarkers(lobbyScreen && !EndedInLobby, ready0, ready1, slot, View.PodiumReadyVisible, soloLobby);
            if(desired==FrontendScreen.Result)
                View.Result(m.SetScore0>=m.SetScore1?0:1,m.SetScore0,m.SetScore1,online.Active,
                    (runner.FrontendWorld.State.Fighter0.PrevButtons&InputButtons.Attack)!=0 || (practiceReadyWorld!=null && slot==0),
                    (runner.FrontendWorld.State.Fighter1.PrevButtons&InputButtons.Attack)!=0 || (practiceReadyWorld!=null && slot==1));
        }

        public static string EndMessage(LockstepSession session) => session==null?"연결이 끊겨 경기가 종료됐습니다":
            session.Reason==EndReason.PeerLeft || session.Reason==EndReason.Timeout?"연결이 끊겨 경기가 종료됐습니다":
            session.Reason==EndReason.Rejected?"서로 같은 버전의 게임으로 접속해주세요.":
            session.Reason==EndReason.Desync?"경기 상태가 달라 진행을 멈췄습니다.":"연결이 종료되었습니다.";
    }
}
