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
            presenter.SetDebugLabels(false);
            if(presenter.Help!=null)presenter.Help.Visible=false;
            if(runner.IsAuthoringPreview)lobby.StartPractice();
        }

        void Join() {lobby.JoinCode=View.CodeInput.text;lobby.Join();}

        void ReadyForRematch()
        {
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
                practiceReadySent = runner.RequestRematch(runner.LocalPlayerSlot);
        }

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

        void Update()
        {
            if(View==null || runner.World==null)return;
            var keyboard=Keyboard.current;
            if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame && !online.Active)
            {
                if(joinEntry||createEntry||lobby.Busy) {joinEntry=false;createEntry=false;lobby.CancelAndOpen();}
                else lobby.TogglePracticeMenu();
            }
            if(joinEntry && keyboard!=null && keyboard.enterKey.wasPressedThisFrame && !lobby.Busy && !online.Active)Join();
            int slot=runner.LocalPlayerSlot;
            if(slot!=shownSlot){shownSlot=slot;View.SetLocalSlot(slot);}
            var m=runner.FrontendWorld.State.Match;
            FrontendScreen desired;
            var session=online.Session;
            if(session!=null && session.State!=SessionState.Running) desired=FrontendScreen.Waiting;
            else if(!online.Active && lobby.IsOpen) desired=joinEntry?FrontendScreen.Join:createEntry||lobby.Busy?FrontendScreen.Waiting:FrontendScreen.Main;
            else if(m.Phase==MatchPhase.Countdown && m.Round==1 && m.PhaseTick<View.IntroRevealTick) desired=FrontendScreen.Versus;
            else if(m.Phase==MatchPhase.SetPick) desired=FrontendScreen.Cards;
            else if(m.Phase==MatchPhase.MatchEnd) desired=FrontendScreen.Result;
            else desired=FrontendScreen.Hidden;
            if(desired!=previous)
            {
                previous=desired;View.Show(desired);
                if(desired!=FrontendScreen.Cards)submittedRound=-1;
                if(desired==FrontendScreen.Versus||desired==FrontendScreen.Cards)presenter.SfxPlayer.Play(Presentation.Sfx.Dash,.25f,1.3f);
            }
            runner.MenuInputBlocked=!online.Active&&lobby.IsOpen;
            UpdatePracticeReady();
            // 과거 리플레이/개발 시나리오는 원래 HUD와 선택 화면을 사용한다.
            bool oldReplay=!runner.World.Config.Match.CollectCardsOnly && !lobby.IsOpen;
            View.Canvas.enabled=!oldReplay;
            if(oldReplay) {View.Show(FrontendScreen.Hidden);previous=FrontendScreen.Hidden;return;}
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
                    m.PickedAt<0?0:(m.PhaseTick-m.PickedAt)/(float)runner.World.Config.Match.PickHoldTicks,
                    Mathf.Max(0,Mathf.CeilToInt((runner.World.Config.Match.PickTimeoutTicks-m.PhaseTick)/60f)));
                if(m.PickedAt>=0&&(m.PickedAt!=lastPickedAt||m.Picker!=lastPicker))presenter.SfxPlayer.Play(Presentation.Sfx.Grab,.5f,1.25f);
                lastPickedAt=m.PickedAt;lastPicker=m.Picker;
            }
            else {lastPickedAt=-1;lastPicker=-1;}
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
