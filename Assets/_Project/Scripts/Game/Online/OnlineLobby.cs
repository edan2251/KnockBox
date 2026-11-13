using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// 방 만들기/코드 참가/연습의 연결 상태. CR-005에서는 FrontendController가 화면을 그린다.
    /// 기존 IMGUI는 FrontendController 없는 개발 실행의 대체 화면으로 보존한다.
    /// </summary>
    [RequireComponent(typeof(OnlineMatch))]
    public sealed class OnlineLobby : MonoBehaviour
    {
        [SerializeField] OnlineMatch online;
        [SerializeField] MatchRunner runner;
        [SerializeField] MatchPresenter presenter;
        bool open = true;
        bool busy;
        string code = "";
        string joinCode = "";
        string status = "";
        GUIStyle title, label, box, field;
        int requestVersion;
        CancellationTokenSource connectionRequest;
        MatchRunner Runner => runner != null ? runner : (runner=GetComponent<MatchRunner>());
        public bool IsOpen => open;
        public bool Busy => busy;
        public string RoomCode => code;
        public string JoinCode { get => joinCode; set => joinCode = value ?? ""; }
        public string StatusMessage => status;

        public void StartPractice()
        {
            CancelPendingConnection();
            if (online.Active) online.LeaveToPractice();
            else Runner.StartNewMatch();
            open=false;
            Runner.MenuInputBlocked=false;
            Runner.Running=true;
        }

        public void CancelAndOpen()
        {
            CancelPendingConnection();
            busy=false; status=""; code="";
            if (online.Active) online.LeaveToPractice();
            open=true;
            Runner.Running=false;
            Runner.MenuInputBlocked=true;
        }

        public void TogglePracticeMenu()
        {
            if(online.Active || busy) return;
            open=!open;
            Runner.MenuInputBlocked=open;
            Runner.Running=!open;
        }

        void Awake()
        {
            online ??= GetComponent<OnlineMatch>();
            // 개발 빌드의 디버그 글자(틱, 상태 이름, 아래쪽 규칙 줄)는 친구 시험 화면에서 숨긴다.
            presenter ??= GetComponent<MatchPresenter>();
            if (presenter != null)
                presenter.SetDebugLabels(false);
        }

        void Update()
        {
            if (GetComponent<UI.FrontendController>() != null) return;
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame && !online.Active)
                open = !open;
        }

        public async void CreateRoom() => await ConnectAsync(true);
        public async void Join() => await ConnectAsync(false);

        // UI commands converge here; only the connection layer knows Photon APIs.
        async Task ConnectAsync(bool host)
        {
            if (Runner.IsAuthoringPreview) { status="제작기 시험은 로컬 전용입니다. 시험 종료 후 Main에서 연결해주세요."; return; }
            if (busy || online.Active) return;
            if (!host && string.IsNullOrWhiteSpace(joinCode))
            {
                status="방 코드를 입력해주세요.";
                return;
            }
            int request=++requestVersion;
            string submittedCode = joinCode;
            var cancellation = new CancellationTokenSource();
            connectionRequest = cancellation;
            ITransport connection = null;
            busy = true;
            code = "";
            status = host ? "방을 만드는 중…" : "방에 참가하는 중…";
            try
            {
                string roomCode;
                if (host)
                {
                    var created = await FusionConnection.HostAsync(cancellation.Token);
                    connection = created.transport;
                    roomCode = created.code;
                }
                else
                {
                    connection = await FusionConnection.JoinAsync(submittedCode, cancellation.Token);
                    roomCode = submittedCode.Trim().ToUpperInvariant();
                }
                if(this == null || request != requestVersion || cancellation.IsCancellationRequested) return;
                online.StartSession(connection, host, host ? OnlineMatch.DefaultOnlineDelay : 0);
                connection = null; // OnlineMatch owns this transport until the match is closed.
                code = roomCode;
                status = "";
                Debug.Log(host ? "[Knockbox][online] Fusion 방 생성" : "[Knockbox][online] Fusion 방 참가");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A newer request or returning to the menu owns the visible state.
            }
            catch (Exception e)
            {
                if(this == null || request != requestVersion) return;
                status = e is FusionSetupException ? e.Message : host
                    ? "방을 만들지 못했습니다. 연결을 확인하고 다시 시도해주세요."
                    : "참가하지 못했습니다. 방 코드와 연결을 확인해주세요.";
                Debug.LogWarning($"[Knockbox][online] 연결 실패: {e.GetType().Name}");
            }
            finally
            {
                connection?.Dispose();
                if (this != null && request == requestVersion)
                {
                    busy = false;
                    connectionRequest = null;
                }
                cancellation.Dispose();
            }
        }

        void CancelPendingConnection()
        {
            requestVersion++;
            connectionRequest?.Cancel();
            connectionRequest = null;
            busy = false;
        }

        void OnDestroy() => CancelPendingConnection();

        void Styles()
        {
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            box ??= new GUIStyle(GUI.skin.box) { fontSize = 14 };
            field ??= new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        }

        void OnGUI()
        {
            if (GetComponent<UI.FrontendController>() != null) return;
            float scale = OnlineMatch.GuiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            Styles();
            var session = online.Session;

            // 방장 대기·손님 연결 중.
            if (session != null && (session.State == SessionState.WaitingPeer || session.State == SessionState.Connecting))
            {
                var panel = new Rect(width / 2 - 200, 120, 400, 290);
                GUI.Box(panel, "", box);
                GUILayout.BeginArea(new Rect(panel.x + 16, panel.y + 12, panel.width - 32, panel.height - 24));
                if (session.State == SessionState.WaitingPeer)
                {
                    GUILayout.Label("ROOM CODE", label);
                    GUILayout.Label(code, title);
                    if (GUILayout.Button("COPY CODE", GUILayout.Height(28)))
                        GUIUtility.systemCopyBuffer = code;
                    GUILayout.Label("WAITING FOR OPPONENT...", label);
                }
                else
                {
                    GUILayout.Label("CONNECTING TO ROOM " + code + "...", label);
                }
                GUILayout.Space(8);
                if (GUILayout.Button("CANCEL", GUILayout.Height(28)))
                    online.LeaveToPractice();
                GUILayout.EndArea();
                return;
            }

            // 연결이 끝남(나감·끊김·어긋남): 메뉴로.
            if (session != null && session.State == SessionState.Ended)
            {
                if (GUI.Button(new Rect(width / 2 - 80, 290, 160, 32), "BACK TO MENU"))
                {
                    online.LeaveToPractice();
                    open = true;
                }
                return;
            }
            if (session != null || !open)
            {
                if (session == null)
                    GUI.Label(new Rect(0, 4, width, 24), "PRACTICE   ESC: MENU", label);
                return;
            }

            var menu = new Rect(width / 2 - 210, 60, 420, 420);
            GUI.Box(menu, "", box);
            GUILayout.BeginArea(new Rect(menu.x + 16, menu.y + 10, menu.width - 32, menu.height - 20));
            GUILayout.Label("KNOCKBOX  ONLINE TEST", title);
            GUILayout.Space(6);
            GUI.enabled = !busy;
            if (GUILayout.Button("CREATE ROOM", GUILayout.Height(34)))
                CreateRoom();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("CODE", label, GUILayout.Width(50), GUILayout.Height(34));
            joinCode = GUILayout.TextField(joinCode, 12, field, GUILayout.Height(34));
            if (GUILayout.Button("JOIN", GUILayout.Width(80), GUILayout.Height(34)))
                Join();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            if (GUILayout.Button("PRACTICE (ESC: MENU)", GUILayout.Height(28)))
                open = false;
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(status))
                GUILayout.Label(status, label);
            GUILayout.EndArea();
            DrawControls(new Rect(menu.x + 20, menu.y + 230, menu.width - 40, 170));
        }

        /// <summary>짧은 조작 안내(실험 경기, 마우스 + 키보드).</summary>
        public static void DrawControls(Rect rect, bool frontend = false)
        {
            GUI.Box(rect, "CONTROLS");
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            string text =
                "A / D MOVE   SPACE JUMP   MOUSE AIM\n" +
                "W at wall CLIMB   SHIFT unused\n" +
                "LEFT TAP/RELEASE   PUNCH / WEAPON\n" +
                "LEFT HOLD 0.33s+ / RELEASE: HEADBUTT\n" +
                "RIGHT CLICK  PICK UP / THROW / GRAB\n" +
                "HOLDING PLAYER: LEFT CLICK to THROW\n" +
                "PISTOL 6 SHOTS; EMPTY: THROW\n" +
                (frontend ? "REMATCH  CLICK O: READY / X: MENU" : "REMATCH      both HOLD LEFT CLICK");
            GUI.Label(new Rect(rect.x + 12, rect.y + 22, rect.width - 24, rect.height - 26), text, style);
        }
    }
}
