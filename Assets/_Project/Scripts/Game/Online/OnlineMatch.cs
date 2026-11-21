using System;
using System.IO;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// Fusion 예측 경기. 방 화면(OnlineLobby)이 연결을 열어 StartSession을 부른다.
    /// 메모리 전송 분기는 계정 없이 실행하는 기존 결정론·입력 순서 검사용이다.
    /// Fusion 방에 접속한 실행에서도 사용할 수 있는 검사 인수:
    ///   -onlinebot                        내 자리를 봇이 친다(방장 추격 봇, 손님 BrawlBot). 경기 끝에는 잠깐 뒤 공격을 눌러 재대결 준비
    ///   -onlinematches N                  경기 N개가 끝나면 나간다(종료 코드 0 = 어긋남·오류 없음)
    ///   -netsim 편도ms,손실%              보내는 쪽 지연·잃음 흉내(왕복은 두 끝 합)
    /// 끝나면 입력 기록과 요약을 Replays/online_P슬롯_시각.*에 남긴다. 온라인 중에는 디버그 단축키를 끈다(설정을 바꾸면 두 PC가 어긋남).
    /// 화면 글자는 영문 짧은 단어(D-016).
    /// </summary>
    // Fusion 기본 수신 처리 뒤, MatchRunner(-100)의 틱 처리 전에 입력을 비운다.
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(MatchRunner))]
    public sealed class OnlineMatch : MonoBehaviour
    {
        public const int DefaultOnlineDelay = FusionPredictionPolicy.DelayTicks;

        [SerializeField] MatchRunner runner;
        [SerializeField] MatchPresenter presenter;
        LockstepSession session;
        SimulatedLink link;
        FusionTransport fusionTransport;
        FusionPredictionSession prediction;
        public FusionPredictionSession Prediction => prediction;
        CardHoverRelay cardHover;
        /// <summary>카드 선택 무대의 표시 전용 호버 전송(Fusion 경기에서만). 시뮬레이션·확정 기록과 무관하다.</summary>
        public CardHoverRelay CardHover => cardHover;
        FusionTransport.ConnectionTiming connectionTiming = FusionTransport.ConnectionTiming.Unavailable;
        /// <summary>HUD와 CSV가 공유하는 SDK RTT. 측정 전에는 -1이며 제어 메시지 RTT로 대체하지 않는다.</summary>
        public double NetworkRttSeconds => connectionTiming.RttSeconds;
        [SerializeField] DebugHotkeys hotkeys;
        bool started;
        bool finished;
        bool bot;
        int targetMatches;
        int matches;
        double quitAt = -1;
        bool showControls;
        SimConfig preparedConfig;
        ArenaDef[] preparedArenas;
        GUIStyle top, marker, big;

        public LockstepSession Session => session;
        public int MatchesFinished => matches;
        public bool Active => session != null;
        /// <summary>YOU 표시도 캐릭터·조준선과 같은 로컬 슬롯 팔레트를 쓴다.</summary>
        public Color LocalPlayerColor => presenter != null && presenter.Palette != null
            ? presenter.Palette.FighterBody(runner.LocalPlayerSlot) : Color.white;
        /// <summary>연결이 끝났고(나감·끊김·어긋남) 방 화면으로 돌아갈 수 있음.</summary>
        public bool Ended => session != null && session.State == SessionState.Ended;
        /// <summary>방 화면이 보여 줄 짧은 상태(오류 등).</summary>
        public string Notice { get; private set; } = "";
        /// <summary>
        /// 경기 확정 전(로비)에 손님이 나가 방장이 혼자 로비로 돌아왔다(2026-11-19). 방 화면(OnlineLobby)이 같은 코드로 방을 다시 연다.
        /// 받는 쪽이 없으면 예전처럼 연결 종료로 끝난다.
        /// </summary>
        public event Action ReturnedToSoloLobby;

        /// <summary>내 닉네임(정리된 값, 비어 있으면 상대 화면에 자리 이름). 화면(FrontendController)이 정한다.</summary>
        public string LocalName { get; set; } = "";
        /// <summary>상대가 보낸 닉네임(표시 전용, 2026-11-21). 아직 못 받았거나 비어 있으면 빈 문자열.</summary>
        public string PeerName { get; private set; } = "";
        bool nameSent, nameReceived;
        bool departureRequested;
        int departureAck = -1, sentDepartureAck = -1, departureOutcomeTick = -1;
        public bool DepartureDelivered { get; private set; }
        public bool DeparturePending => departureRequested && !DepartureDelivered;
        public bool PeerDeparture => Active && runner.FrontendWorld != null && runner.FrontendWorld.State.Match.Departed
            && runner.FrontendWorld.State.Match.ForfeitLoser != runner.LocalPlayerSlot;
        public string EndNotice
        {
            get
            {
                var m = runner.FrontendWorld.State.Match;
                if (PeerDeparture) return $"상대가 나갔습니다 · 기권승\n{m.SetScore0} : {m.SetScore1}";
                return UI.FrontendController.EndMessage(session);
            }
        }

        public bool RequestMatchCommand(MatchCommand command)
        {
            if (!Active || Ended || !runner.RequestMatchCommand(command)) return false;
            departureRequested = command == MatchCommand.Leave;
            return true;
        }

        void UpdateDeparture()
        {
            if (runner.FrontendWorld == null) return;
            var m = runner.FrontendWorld.State.Match;
            departureOutcomeTick = DepartureConfirmationTick(runner.FrontendWorld);
            int tick = departureOutcomeTick;
            if (tick < 0) return;
            // 손님은 확정 기록을 받은 뒤에만 응답한다. KO가 이미 끝났어도 같은 퇴장 틱을 확인한다.
            if (runner.LocalPlayerSlot == 1 && (!m.Departed || m.ForfeitLoser == 0) && sentDepartureAck != tick
                && fusionTransport?.Connection?.SendDepartureAcknowledgement(tick) == true) sentDepartureAck = tick;
            if (departureRequested && (!m.Departed || m.ForfeitLoser == runner.LocalPlayerSlot)
                && (runner.LocalPlayerSlot == 1 || departureAck == tick)) DepartureDelivered = true;
        }

        static int DepartureConfirmationTick(SimWorld world)
        {
            var m = world.State.Match;
            if (m.Departed)
                return world.State.Tick - m.PhaseTick - (m.Phase == MatchPhase.MatchEnd ? SimWorld.ForfeitKoTicks : 0);
            // 마지막 정상 KO나 먼저 확정된 항복과 경합했다면 최종 점수까지 전달한 뒤 일반 퇴장한다.
            return m.Phase == MatchPhase.MatchEnd ? world.State.Tick - m.PhaseTick : -1;
        }

        void ReceiveDepartureAcknowledgement(int tick)
        {
            if (!Active || Ended || !departureRequested || runner.LocalPlayerSlot != 0 || runner.FrontendWorld == null) return;
            var m = runner.FrontendWorld.State.Match;
            if (m.Departed && m.ForfeitLoser != 0) return;
            int expected = DepartureConfirmationTick(runner.FrontendWorld);
            // 단독 응답·이전 경기·다른 틱은 퇴장이나 승패의 근거가 될 수 없다.
            if (expected >= 0 && tick == expected) departureAck = tick;
        }

        /// <summary>
        /// 씬이 뜬 뒤: 일반 Brawl 실행/에디터 Play에는 공통 UI를 붙인다.
        /// 배치 모드·재생 검사·자동 봇 실행에는 로비를 붙이지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            var runner = FindFirstObjectByType<MatchRunner>();
            if (runner == null || !runner.BrawlExperiment || !UI.FrontendController.UseFrontendForThisRun)
                return;
            // 첫 방 만들기·참가의 Fusion 첫 실행 비용을 앱 시작(이미 불러오기 정지가 있는 때)으로 옮긴다(빌드 전용).
            FusionConnection.WarmUpOnce();
            if (runner.GetComponent<OnlineMatch>() != null)
                return;
            runner.gameObject.AddComponent<OnlineMatch>();
            runner.gameObject.AddComponent<OnlineLobby>();
            runner.gameObject.AddComponent<UI.FrontendController>();
        }

        void Awake()
        {
            runner ??= GetComponent<MatchRunner>();
            if(UI.FrontendController.UseFrontendForThisRun) runner.FrontendEnabled = runner.BrawlExperiment;
            presenter ??= GetComponent<MatchPresenter>();
            Application.runInBackground = true;
            bot = AutoPlayDebug.HasArg("-onlinebot");
            targetMatches = int.TryParse(ReplayCheck.StringArg("-onlinematches"), out int n) ? n : 0;
            hotkeys ??= GetComponent<DebugHotkeys>();
        }

        /// <summary>연결로 세션을 연다. 방장은 손님을 기다리고, 손님은 Hello를 보낸다. 연결이 되면 경기를 시작한다.</summary>
        public void StartSession(ITransport connection, bool host, int delay)
        {
            if (runner.IsAuthoringPreview)
            {
                connection?.Dispose();
                throw new InvalidOperationException("제작기 시험은 로컬 연습 전용입니다.");
            }
            EndSession();
            bool wasRunning=runner.Running,wasOnlineRematch=runner.OnlineRematch,wasRandomizeSeed=runner.RandomizeSeed;
            bool hotkeysWereEnabled=hotkeys!=null&&hotkeys.enabled;
            runner.Running = false;
            runner.OnlineRematch = true;
            runner.RandomizeSeed = false;
            if (hotkeys != null)
                hotkeys.enabled = false;
            uint configChecksum,arenaChecksum;
            try
            {
                preparedConfig = runner.CreateConfig();
                preparedArenas = runner.PrepareMatchArenas(preparedConfig);
                configChecksum = preparedConfig.ComputeChecksum();
                arenaChecksum = OnlineMapManifest.Compute(preparedArenas);
            }
            catch
            {
                connection.Dispose();
                preparedConfig=null;preparedArenas=null;
                runner.Running=wasRunning;runner.OnlineRematch=wasOnlineRematch;runner.RandomizeSeed=wasRandomizeSeed;
                if(hotkeys!=null)hotkeys.enabled=hotkeysWereEnabled;
                throw;
            }

            string netsim = ReplayCheck.StringArg("-netsim");
            fusionTransport = connection as FusionTransport;
            bool usePrediction = fusionTransport != null;
            if (usePrediction)
            {
                var wire = fusionTransport;
                cardHover = new CardHoverRelay((round, sequence, option) => wire.Connection?.SendCardHover(round, sequence, option));
                if (wire.Connection != null) wire.Connection.CardHover = cardHover;
                if (wire.Connection != null) wire.Connection.PlayerNameReceived = ReceivePeerName;
                if (wire.Connection != null) wire.Connection.DepartureAcknowledged = ReceiveDepartureAcknowledgement;
            }
            if (usePrediction) delay = FusionPredictionPolicy.DelayTicks;
            connectionTiming = FusionTransport.ConnectionTiming.Unavailable;
            link = null;
            if (!string.IsNullOrEmpty(netsim) && !usePrediction)
            {
                var parts = netsim.Split(',');
                double oneWay = parts.Length > 0 && double.TryParse(parts[0], out double ms) ? ms / 1000.0 : 0;
                double loss = parts.Length > 1 && double.TryParse(parts[1], out double pct) ? pct / 100.0 : 0;
                link = new SimulatedLink(connection, () => Time.realtimeSinceStartupAsDouble, oneWay, oneWay * 0.25, loss, 0.01, host ? 11 : 22);
                connection = link;
                Debug.Log($"[Knockbox][online] 망 흉내: 편도 {oneWay * 1000:0} ms(흔들림 25%), 잃음 {loss * 100:0.#}%, 중복 1%");
            }
            uint seed = (uint)Environment.TickCount | 1u;
            session = new LockstepSession(connection, host, configChecksum, arenaChecksum, seed, delay,
                () => Time.realtimeSinceStartupAsDouble);
            started = false;
            finished = false;
            matches = 0;
            Notice = "";
            // 로비 맵이 있으면 방장은 상대를 기다리는 동안 혼자 로비에서 움직인다(로컬 월드, 기록·전송 없음).
            if (host && runner.FrontendEnabled && preparedConfig.Match.LobbySpace)
            {
                // 손님이 나간 뒤 같은 코드로 다시 연 방은 지금 돌고 있는 혼자 로비를 그대로 이어 간다(P1 계속 조작).
                if (!runner.InSoloLobby)
                    runner.StartSoloLobby(preparedConfig, preparedArenas, seed);
                runner.MenuInputBlocked = false;
                runner.Running = true;
            }
            Debug.Log($"[Knockbox][online] {(host ? "방장" : "손님")} 시작({connection.GetType().Name}): 설정 {configChecksum:X8}, 맵 {arenaChecksum:X8}, 봇 {bot}");
        }

        /// <summary>닉네임 교환(표시 전용): 상대가 보이면 한 번 보낸다.</summary>
        void SendNameOnce()
        {
            if (nameSent || fusionTransport == null || fusionTransport.Connection == null) return;
            nameSent = fusionTransport.Connection.SendPlayerName(LocalName);
        }

        /// <summary>처음 받으면 내 이름을 한 번 더 보낸다(상대가 나를 알기 전에 보낸 첫 값이 버려졌어도 맞춰진다).</summary>
        void ReceivePeerName(string name)
        {
            PeerName = Knockbox.Game.GameSettings.CleanNickname(name);
            if (nameReceived) return;
            nameReceived = true;
            if (nameSent) fusionTransport?.Connection?.SendPlayerName(LocalName);
        }

        /// <summary>세션을 닫고 혼자 연습(로컬 경기)으로 돌아간다.</summary>
        public void LeaveToPractice()
        {
            EndSession();
            runner.OnlineRematch = false;
            runner.RandomizeSeed = true;
            if (hotkeys != null)
                hotkeys.enabled = true;
            runner.StartNewMatch();
            runner.Running = true;
        }

        void EndSession()
        {
            preparedConfig = null;
            preparedArenas = null;
            if (session == null)
                return;
            if (session.State != SessionState.Ended)
                session.Leave();
            if (!finished)
                Finish();
            runner.Ticked -= OnTicked;
            prediction?.Dispose();
            prediction = null;
            runner.Lockstep = null;
            session.Dispose();
            session = null;
            if (fusionTransport != null && fusionTransport.Connection != null) fusionTransport.Connection.CardHover = null;
            if (fusionTransport != null && fusionTransport.Connection != null) fusionTransport.Connection.PlayerNameReceived = null;
            PeerName = ""; nameSent = nameReceived = false;
            if (fusionTransport != null && fusionTransport.Connection != null) fusionTransport.Connection.DepartureAcknowledged = null;
            departureRequested = DepartureDelivered = false;
            departureAck = sentDepartureAck = departureOutcomeTick = -1;
            fusionTransport = null;
            cardHover = null;
            connectionTiming = FusionTransport.ConnectionTiming.Unavailable;
        }

        void Update()
        {
            if (session == null)
                return;
            double now = Time.realtimeSinceStartupAsDouble;
            session.Poll(now);
            SendNameOnce();
            if (!started && session.State == SessionState.Running)
                BeginMatch();
            prediction?.Pump();
            UpdateDeparture();
            if (session.State == SessionState.Ended && !finished)
            {
                if (CanReturnToSoloLobby()) { ReturnToSoloLobby(); return; }
                Finish();
            }
            if (quitAt > 0 && now >= quitAt)
                Quit(ExitCode());
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame)
                showControls = !showControls;
        }

        /// <summary>
        /// 방장 화면에서 손님이 나갔고(PeerLeft) 아직 경기가 확정되지 않았다: 혼자 기다리는 로비, 또는 둘 다 준비하기 전의 공유 로비.
        /// 둘 다 준비해 퇴장 전환(LobbyExit)·경기에 들어간 뒤에는 예전처럼 연결 종료다.
        /// </summary>
        bool CanReturnToSoloLobby() =>
            ReturnedToSoloLobby != null && session.LocalSlot == 0 && session.Reason == EndReason.PeerLeft
            && fusionTransport != null && runner.FrontendEnabled && preparedConfig != null && preparedConfig.Match.LobbySpace
            && targetMatches == 0 && runner.World != null && runner.World.InLobby;

        void ReturnToSoloLobby()
        {
            var config = preparedConfig; var arenas = preparedArenas; uint seed = session.Seed;
            bool solo = runner.InSoloLobby;
            finished = true;
            if (started) SaveRecords($"{session.Reason}: {session.EndDetail}");
            Debug.Log("[Knockbox][online] 손님이 로비에서 나감: 방장은 혼자 로비로 돌아가 같은 코드로 방을 다시 엽니다.");
            EndSession();
            // 이미 혼자 기다리던 중(손님이 연결 도중 나감)이면 그 월드를 그대로 둔다.
            if (!solo) runner.ResumeSoloLobby(config, arenas, seed);
            runner.MenuInputBlocked = false;
            runner.Running = true;
            ReturnedToSoloLobby?.Invoke();
        }

        SimState StartShared()
        {
            runner.StartPreparedOnlineMatch(preparedConfig, preparedArenas, session.Seed);
            return runner.World.State;
        }

        void BeginMatch()
        {
            started = true;
            int slot = session.LocalSlot;
            // 로비 입장 A안(2026-11-17): Fusion 방장은 혼자 돌던 로비 월드를 다시 만들지 않고(맵 재구성·암전 없음) 이어 간다.
            bool continueSolo = fusionTransport != null && slot == 0 && runner.InSoloLobby;
            SimState initial = continueSolo
                ? runner.ContinueSoloLobbyOnline(preparedConfig, preparedArenas, session.Seed)
                : StartShared();
            IInputSource local;
            if (bot)
            {
                IInputSource inner = slot == 0
                    ? new BrawlChaserBot(() => runner.World, slot, 10)
                    : new BrawlBot(() => runner.World, slot, 0xB22u * 10);
                local = new RematchHoldInput(() => runner.World, inner, slot == 0 ? 40 : 95);
            }
            else
            {
                local = new MouseAimInputSource(() => runner.World, slot);
            }
            local = runner.RouteFrontendInput(local, slot, bot);
            if (fusionTransport != null)
            {
                // The common session carries agreement, heartbeat/RTT, checksums and termination.
                // Combat input and state do not pass through TryGetInputs or the old delay queue.
                session.Begin(initial.Tick, null);
                runner.Ticked += OnTicked;
                try
                {
                    prediction = new FusionPredictionSession(fusionTransport.Connection, runner,
                        preparedConfig, preparedArenas, local, slot, session.DelayTicks, initial, continueSolo);
                    // 손님: 방장이 이어 간 P1 위치를 첫 공유 상태로 받기 전에는 P1을 그리지 않는다(기본 낙하 자리에 잠깐 보였다가 튀지 않게).
                    if (slot == 1 && prediction.StartsInLobby) runner.HideUntilPredicted(0);
                }
                catch (Exception e)
                {
                    runner.Running = false;
                    fusionTransport.MarkEnded("Fusion 예측 경기 준비에 실패했습니다.", EndReason.ProtocolError);
                    Debug.LogError($"[Knockbox][prediction] 경기 준비 실패: {e.GetType().Name}");
                    return;
                }
                runner.Running = true;
                Debug.Log($"[Knockbox][prediction] 기본 예측 시작: P{slot + 1}, 60Hz, SDK 입력 지연 {session.DelayTicks}틱, 진단 {prediction.DiagnosticPath}");
                return;
            }
            // Offline MemoryTransport fixtures retain the lockstep scheduling/replay regression path.
            session.Begin(runner.World.State.Tick, local);
            runner.Lockstep = session;
            runner.StartRecording(runner.World.Config.Rules.Format());
            runner.Ticked += OnTicked;
            runner.Running = true;
            Debug.Log($"[Knockbox][online] 경기 시작: 내 자리 P{slot + 1}, 시드 {session.Seed:X8}, 입력 지연 {session.DelayTicks}틱");
        }

        // 경기 틱 이후 SDK RTT를 한 번 읽어 HUD와 진단에 함께 전달한다. CSV 쓰기는 초당으로 제한한다.
        void LateUpdate()
        {
            if (!started || finished || prediction == null) return;
            connectionTiming = fusionTransport.ReadConnectionTiming();
            prediction.RecordFrame(session.RttSeconds, connectionTiming);
        }

        void OnTicked(MatchRunner r)
        {
            // 방장이 공유 첫 틱을 기다리며 혼자 로비를 돌리는 동안의 틱은 로컬 월드다(체크섬·경기 수에 넣지 않는다).
            if (prediction != null && !r.PredictionDriven) return;
            var w = r.EventWorld;
            UpdateDeparture();
            int tick = w.State.Tick;
            if (LockstepSession.NeedsChecksum(tick))
                session.ReportChecksum(tick, w.ComputeChecksum());
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type != SimEventType.MatchEnd)
                    continue;
                matches++;
                Debug.Log($"[Knockbox][online] t={tick} 경기 {matches} 끝: 승자 P{w.Events[i].Fighter + 1}, 점수 {w.State.Match.Score0}:{w.State.Match.Score1}, 세트 {w.State.Match.SetScore0}:{w.State.Match.SetScore1}, 체크섬 {w.ComputeChecksum():X8}, {Stats()}");
            }
            // 목표 경기 수를 채우면 재대결이 시작된 뒤(양쪽이 같은 틱까지 계산) 잠깐 있다가 나간다.
            if (targetMatches > 0 && matches >= targetMatches && quitAt < 0 && w.State.Match.Phase != MatchPhase.MatchEnd)
            {
                quitAt = Time.realtimeSinceStartupAsDouble + 1.0;
                Debug.Log($"[Knockbox][online] 목표 경기 {targetMatches}개 끝, 곧 나감");
            }
        }

        string Stats() =>
            $"SDK RTT {(NetworkRttSeconds < 0 ? "-" : (NetworkRttSeconds * 1000).ToString("0"))} ms, " +
            $"제어 RTT {(session.RttSeconds < 0 ? "-" : (session.RttSeconds * 1000).ToString("0"))} ms(최대 {session.MaxRttSeconds * 1000:0}), " +
            $"비교한 체크섬 {session.ChecksumsCompared}개, 중복 무시 {session.DuplicatesIgnored}, " +
            $"보냄 {session.PacketsSent}·받음 {session.PacketsReceived}" + (link != null ? $", 흉내 잃음 {link.Dropped}·중복 {link.Duplicated}" : "");

        void Finish()
        {
            finished = true;
            runner.Running = false;
            string detail = $"{session.Reason}: {session.EndDetail}";
            if (session.Reason == EndReason.Desync)
                Debug.LogError($"[Knockbox][online] 동기화 어긋남, 경기를 멈춤. {detail}");
            else
                Debug.Log($"[Knockbox][online] 연결 끝: {detail}");
            SaveRecords(detail);
            if (targetMatches > 0 && quitAt < 0)
                quitAt = Time.realtimeSinceStartupAsDouble + 0.5;
        }

        void SaveRecords(string detail)
        {
            var recording = prediction != null ? prediction.Finish() : runner.StopRecording();
            // 배포용 빌드(2026-11-21)는 진단 기록을 남기지 않는다. 설치 폴더(예: Program Files)에 쓰지 못해 문제가 되지 않게 한다.
            if (recording == null || (!Application.isEditor && !Debug.isDebugBuild))
                return;
            string name = $"online_P{session.LocalSlot + 1}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = ReplayFiles.Save(recording, name);
            File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                $"끝: {detail}\n틱 {recording.Count}, 경기 {matches}개, 최종 체크섬 {recording.FinalChecksum:X8}\n어긋난 틱 {session.DesyncTick}\n{Stats()}\n시간 진단: {prediction?.DiagnosticPath ?? "-"}\n");
            Debug.Log($"[Knockbox][online] 입력 기록 저장: {path} ({recording.Count}틱, 최종 {recording.FinalChecksum:X8})");
        }

        int ExitCode()
        {
            if (session == null)
                return 2;
            bool bad = session.Reason == EndReason.Desync || session.Reason == EndReason.ProtocolError || session.Reason == EndReason.Rejected
                       || session.Reason == EndReason.Timeout;
            return !bad && matches >= targetMatches ? 0 : 1;
        }

        void Quit(int code)
        {
            if (session != null && session.State != SessionState.Ended)
            {
                session.Leave();
                if (!finished)
                    Finish();
            }
            Debug.Log($"[Knockbox][online] 종료 코드 {code}");
            if (!Application.isEditor)
                Application.Quit(code);
            quitAt = -1;
            targetMatches = 0;
        }

        void OnDestroy() => EndSession();

        void OnApplicationQuit() => EndSession();

        // 화면 ------------------------------------------------------------------

        public static float GuiScale => Mathf.Max(0.5f, Screen.height / 540f);

        public static string EndText(LockstepSession s) =>
            s.Reason == EndReason.Desync ? $"DESYNC AT TICK {s.DesyncTick} - MATCH STOPPED"
            : s.Reason == EndReason.PeerLeft ? "OPPONENT LEFT"
            : s.Reason == EndReason.Timeout ? (s.PacketsReceived == 0 ? "NO RESPONSE FROM HOST" : "CONNECTION LOST")
            : s.Reason == EndReason.Rejected ? "REJECTED: DIFFERENT BUILD"
            : s.Reason == EndReason.ProtocolError ? "CONNECTION ERROR"
            : "DISCONNECTED";

        bool guiWarmed;

        /// <summary>
        /// 경기 중 상단 온라인 정보 줄의 글꼴(기본 GUI 글꼴·굵게)을 앱 첫 프레임에 미리 불러 둔다(2026-11-18). 처음 그릴 때 글꼴을 읽느라
        /// 온라인 1라운드 카운트다운에서 두 PC 모두 0.2~0.4초 멈췄다(빌드 측정: GUI.Repaint·FontEngine). 화면 표시는 바꾸지 않는다.
        /// </summary>
        void WarmGuiFont()
        {
            guiWarmed = true;
            top ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            marker ??= new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold };
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            var sample = new GUIContent("ONLINE YOU = P1 P2 RTT - 0123456789 ms DELAY TAB: CONTROLS WAITING FOR OPPONENT .s YOU WIN LOSE REMATCH: BOTH HOLD LEFT CLICK READY");
            top.CalcSize(sample); marker.CalcSize(sample); big.CalcSize(sample);
        }

        void OnGUI()
        {
            // Main은 실제 Frontend 점수판·머리 위 닉네임만 사용한다. 개발 상태 줄과 YOU는 그리지 않는다.
            if (runner != null && runner.FrontendEnabled) return;
            if (!guiWarmed) WarmGuiFont();
            var frontend = GetComponent<UI.FrontendController>();
            if (frontend != null && frontend.View != null && frontend.View.Screen != UI.FrontendScreen.Hidden) return;
            if (session == null || session.State == SessionState.WaitingPeer || session.State == SessionState.Connecting)
                return;
            float scale = GuiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            top ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            marker ??= new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold };
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            if (session.State == SessionState.Ended)
            {
                GUI.Label(new Rect(0, 230, width, 40), EndText(session), big);
                return;
            }
            string rtt = NetworkRttSeconds < 0 ? "-" : (NetworkRttSeconds * 1000).ToString("0");
            string line = $"ONLINE   YOU = P{session.LocalSlot + 1}   RTT {rtt} ms   DELAY {session.DelayTicks}   TAB: CONTROLS";
            if (session.Waiting && session.SecondsSinceReceive > 0.25)
                line += $"\nWAITING FOR OPPONENT  {session.SecondsSinceReceive:0.0}s";
            GUI.Label(new Rect(0, 4, width, 40), line, top);

            var w = runner.World;
            if (w == null)
                return;
            ref readonly var s = ref w.State;
            // 내 캐릭터 표시.
            var cam = Camera.main;
            if (cam != null && (!w.Config.Match.CollectCardsOnly || s.Match.Phase == MatchPhase.Fighting))
            {
                var me = s.GetFighter(session.LocalSlot);
                var p = cam.WorldToScreenPoint(new Vector3(me.Position.X, me.Position.Y + w.Config.HalfBody + 0.35f, 0f));
                if (p.z >= 0f)
                {
                    float x = p.x / scale, y = (Screen.height - p.y) / scale;
                    var old = GUI.color;
                    GUI.color = LocalPlayerColor;
                    GUI.Label(new Rect(x - 40, y - 22, 80, 22), "YOU", marker);
                    GUI.color = old;
                }
            }
            if (s.Match.Phase == MatchPhase.MatchEnd)
            {
                int winner = w.Config.Rules.LegacyMatch ? (s.Match.Score0 >= s.Match.Score1 ? 0 : 1)
                    : (s.Match.SetScore0 >= s.Match.SetScore1 ? 0 : 1);
                bool meReady = (s.GetFighter(session.LocalSlot).PrevButtons & InputButtons.Attack) != 0;
                bool otherReady = (s.GetFighter(1 - session.LocalSlot).PrevButtons & InputButtons.Attack) != 0;
                GUI.Label(new Rect(0, 150, width, 40), winner == session.LocalSlot ? "YOU WIN" : "YOU LOSE", big);
                GUI.Label(new Rect(0, 330, width, 40),
                    $"REMATCH: BOTH HOLD LEFT CLICK\nYOU {(meReady ? "READY" : "-")}   OPPONENT {(otherReady ? "READY" : "-")}", top);
            }
            if (showControls)
                OnlineLobby.DrawControls(new Rect(width / 2 - 170, 120, 340, 170), w.Config.Match.CollectCardsOnly);
        }
    }
}
