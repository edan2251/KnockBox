using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 온라인 로비 화면(2026-11-19 사용자 요청): 방을 만든 순간부터 둘이 준비해 첫 경기로 들어가기까지.
    /// - 좌측 상단 한 줄: 방 코드와 복사(누르면 같은 자리에서 잠깐 "복사됨"). 손님이 들어오면 접히고, 다시 혼자 기다리면 펼쳐진다.
    /// - 상단 가운데: 경기 점수판과 같은 순서(P1 왼쪽, P2 오른쪽)의 두 자리. 이름과 준비 상태를 보여 준다.
    /// - 둘 다 준비해 퇴장 전환(MatchPhase.LobbyExit)이 시작되면: 점프 정점에서 준비 글자가 사라지고 두 이름이 좌우로 벌어져
    ///   실제 경기 점수판의 "P1"/"P2" 자리에서 멈추며, 그 사이에 점수판의 빈 세트·라운드 점이 드러난다(FrontendScoreView.RenderLobbyReveal).
    ///   두 선수는 지금 보이는 자리에서 살짝 눌렸다가 튀어 오르고, 아래로 가속하며 발판을 지나 화면 밖으로 빠진다(메인 → 로비 점프와 같은 곡선).
    /// 기존 큰 방 패널·하단 준비 버튼·돌아가기 버튼은 로비에서 쓰지 않는다(나가기는 Esc). 이 표시는 런타임에 만든다(프리팹 변경 없음).
    /// 시뮬레이션 상태는 읽기만 한다.
    /// </summary>
    public sealed partial class FrontendView
    {
        /// <summary>로비 화면이 매 프레임 받는 표시 값(FrontendController가 정한다).</summary>
        public struct LobbyHudState
        {
            /// <summary>방장이 혼자 기다리는 중(P2 자리 비움).</summary>
            public bool Solo;
            /// <summary>방 코드와 복사를 펼쳐 보여 줄지(방장이 혼자 기다리며 방이 열려 있을 때).</summary>
            public bool ShowCode;
            /// <summary>손님이 나가 같은 코드로 방을 다시 여는 중.</summary>
            public bool Reopening;
            public string Code;
            public bool Ready0, Ready1;
            /// <summary>상단 가운데 아래의 짧은 알림(상대가 나감·방이 닫힘). 비우면 숨긴다.</summary>
            public string Notice;
        }

        // 퇴장 전환 박자(틱, 60Hz). 합계는 CardCollectionRules.LobbyExitTicks(72) 안에 끝난다.
        const float ExitSquashEnd = 7, ExitApex = 24, ExitFallEnd = 58;
        const float ExitJumpHeight = 2.6f, ExitFallDistance = 22f;
        /// <summary>로비 장면이 경기 KO 퇴장처럼 빠지기 시작하는 틱(두 선수가 발판 아래로 지나간 뒤). 전환 끝(72틱)까지 약 0.5초.</summary>
        const float LobbySceneLeaveStart = 42;
        const float LobbySlotSpread = 170f, LobbyNameScale = 1.3f, CopiedSeconds = 1.2f;
        // 방 코드 줄(2026-11-19 2차): 글자 칸은 고정 폭(코드처럼 읽히게). 들어올 때는 왼쪽부터 한 글자씩 떨어지며 몇 번 바뀌다가 제 글자로 맞춰지고,
        // 나갈 때는 오른쪽부터 다시 뒤섞이며 위로 흩어진다. 방을 다시 여는 동안은 글자가 계속 뒤섞인다.
        const int CodeSlots = 8;
        const float CodeAdvance = 31f, CodeStartX = 128f, CodeLineHeight = 64f;
        const float CodeInStart = .10f, CodeInStagger = .05f, CodeInScramble = .16f;
        const float CodeOutStagger = .035f, CodeOutScramble = .09f, CodeOutFade = .14f;
        const string CodeScrambleGlyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        // 상대를 기다리는 중: 글자는 천천히 숨 쉬고, 뒤의 점 세 개가 차례로 튀는 물결(입력 중 표시와 같은 방식).
        const float WaitBreathSeconds = 1.8f, WaitWaveSeconds = 1.1f, WaitDotSize = 7f, WaitDotBounce = 7f;

        RectTransform lobbyHud, lobbyCodeLine;
        Text lobbyCodeLabel, lobbyCopyLabel, lobbyNotice, lobbyWaiting;
        readonly Text[] lobbyCodeChars = new Text[CodeSlots];
        readonly Image[] lobbyWaitDots = new Image[3];
        Button lobbyCopy;
        readonly Text[] lobbyNames = new Text[2], lobbyStatus = new Text[2];
        readonly int[] lobbyNameBase = new int[2];
        readonly bool[] lobbyShownReady = new bool[2];
        readonly float[] lobbyReadyAt = { -10f, -10f };
        LobbyHudState lobbyHudState;
        float lobbyCopiedAt = -10f, lobbyCodeChangedAt = -10f, lobbyPeerChangedAt = -10f;
        bool lobbyCodeShown, lobbyCodeReady, lobbyPeerPresent, lobbyCodeReopening;
        // 글자가 제 글자로 맞춰지기 시작하는 시각(보이기 시작하거나 방을 다시 연 순간부터 왼쪽부터 차례로).
        float lobbyCodeLockBase;
        string lobbyLastCode = "";
        int lobbyExitSoundTick = -1;

        public void LobbyHud(in LobbyHudState state) => lobbyHudState = state;

        void BuildLobbyHud()
        {
            if (lobbyHud != null) return;
            lobbyHud = Rect(stage, "Lobby_hud", 0, 0, 1920, 1080);
            lobbyCodeLine = Rect(lobbyHud, "Room_code_line", 44, 34, 640, CodeLineHeight);
            lobbyCodeLabel = Text(lobbyCodeLine, "Label", new Rect(0, 0, 124, CodeLineHeight), "방 코드", Theme.textFont, 26, Theme.hint, TextAnchor.MiddleLeft);
            ReadableOnDivider(lobbyCodeLabel, 2);
            for (int i = 0; i < CodeSlots; i++)
            {
                var c = Text(lobbyCodeLine, "Code_char_" + i, new Rect(0, 0, 40, CodeLineHeight), "", Theme.boldFont, 44, playerColors[0]);
                c.rectTransform.pivot = new Vector2(.5f, .5f);
                c.horizontalOverflow = HorizontalWrapMode.Overflow;
                ReadableOnDivider(c, 3);
                lobbyCodeChars[i] = c;
            }
            float copyX = CodeStartX + CodeSlots * CodeAdvance + 18;
            lobbyCopy = Button(lobbyCodeLine, "Copy", new Rect(copyX, 9, 140, 46), "복사", Theme, Theme.button, Theme.white, OnLobbyCopy, 26);
            var copyRect = (RectTransform)lobbyCopy.transform;
            copyRect.pivot = new Vector2(.5f, .5f);
            copyRect.anchoredPosition = new Vector2(copyX + 70, -CodeLineHeight * .5f);
            lobbyCopyLabel = lobbyCopy.GetComponentInChildren<Text>(true);
            for (int p = 0; p < 2; p++)
            {
                var label = points != null ? points.Label(p) : null;
                lobbyNames[p] = Text(lobbyHud, "P" + (p + 1) + "_slot_name", new Rect(0, 0, 65, 55), "P" + (p + 1),
                    label != null ? label.font : Theme.boldFont, label != null ? label.fontSize : 30, playerColors[p]);
                lobbyNames[p].horizontalOverflow = HorizontalWrapMode.Overflow;
                lobbyNames[p].rectTransform.pivot = new Vector2(.5f, .5f);
                if (label != null) lobbyNames[p].rectTransform.sizeDelta = label.rectTransform.rect.size;
                ReadableOnDivider(lobbyNames[p], 3);
                lobbyStatus[p] = Text(lobbyHud, "P" + (p + 1) + "_slot_status", new Rect(0, 0, 360, 40), "", Theme.textFont, 24, Theme.white);
                lobbyStatus[p].horizontalOverflow = HorizontalWrapMode.Overflow;
                lobbyStatus[p].rectTransform.pivot = new Vector2(.5f, .5f);
                ReadableOnDivider(lobbyStatus[p], 2);
            }
            lobbyWaiting = Text(lobbyHud, "Waiting_for_opponent", new Rect(0, 0, 360, 44), "상대를 기다리는 중", Theme.textFont, 28, Theme.white);
            lobbyWaiting.horizontalOverflow = HorizontalWrapMode.Overflow;
            lobbyWaiting.rectTransform.pivot = new Vector2(.5f, .5f);
            ReadableOnDivider(lobbyWaiting, 2);
            for (int i = 0; i < lobbyWaitDots.Length; i++)
            {
                lobbyWaitDots[i] = Image(lobbyHud, "Waiting_dot_" + i, new Rect(0, 0, WaitDotSize, WaitDotSize), Theme.white,
                    motionPresenter != null ? motionPresenter.Shapes.Circle : null);
                lobbyWaitDots[i].rectTransform.pivot = new Vector2(.5f, .5f);
            }
            lobbyNotice = Text(lobbyHud, "Notice", new Rect(460, 168, 1000, 46), "", Theme.textFont, 28, Theme.white);
            ReadableOnDivider(lobbyNotice, 2);
            lobbyHud.SetAsLastSibling();
            lobbyHud.gameObject.SetActive(false);
        }

        void OnLobbyCopy()
        {
            if (!lobbyCodeReady) return;
            CopyCode?.Invoke();
            lobbyCopiedAt = Time.unscaledTime;
        }

        /// <summary>점수판 "P1"/"P2" 글자의 가운데(로비 표시 좌표). 프리팹에서 옮긴 자리도 그대로 따른다.</summary>
        Vector2 ScoreLabelCenter(int slot)
        {
            var label = points != null ? points.Label(slot) : null;
            if (label == null) return FrontendScoreView.SetPoint(slot, slot == 0 ? 0 : 4) + new Vector2(slot == 0 ? -52 : 52, 0);
            var rect = label.rectTransform;
            // 닉네임이 칸보다 길면 점수판 글자는 한쪽 정렬이라, 실제 글자 가운데로 날아와 멈춘다(2026-11-21).
            Vector2 local = lobbyHud.InverseTransformPoint(rect.TransformPoint(points.NameCenterLocal(slot)));
            return local;
        }

        /// <summary>
        /// 퇴장 전환의 진행 틱. 아직 로비면 0, 전환 중이면 단계 틱(보간 포함), 이미 1라운드가 시작됐는데(예측이 확정보다 앞섬) 로비 화면이면 끝.
        /// </summary>
        float LobbyExitTick(SimWorld w)
        {
            if (w.InLobbyExit) return w.State.Match.PhaseTick + motionRunner.RenderAlpha;
            return w.InLobby ? 0 : w.Config.Match.LobbyExitTicks;
        }

        /// <summary>표시용 뒤섞인 글자(시간 칸마다 바뀌고 칸마다 다르다). 판정·난수 상태와 무관하다.</summary>
        static char ScrambleGlyph(int slot, float now)
        {
            uint h = (uint)(Mathf.FloorToInt(now / .045f) * 73856093) ^ (uint)((slot + 1) * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return CodeScrambleGlyphs[(int)(h % (uint)CodeScrambleGlyphs.Length)];
        }

        /// <summary>
        /// 방 코드 줄: 보일 때 "방 코드" 글자가 먼저 들어오고, 코드 글자가 왼쪽부터 하나씩 위에서 떨어지며 몇 번 뒤섞이다 제 글자로 맞춰진 뒤
        /// 복사 버튼이 톡 튀어나온다. 숨길 때는 복사 버튼이 먼저 줄고, 글자가 오른쪽부터 다시 뒤섞이며 위로 흩어진다.
        /// </summary>
        void AnimateLobbyCode(bool show, bool reopening, string code, float centerY, float now)
        {
            if (!string.IsNullOrEmpty(code)) lobbyLastCode = code;
            if (show != lobbyCodeShown) { lobbyCodeShown = show; lobbyCodeChangedAt = now; lobbyCodeLockBase = now + CodeInStart; }
            if (lobbyCodeReopening && !reopening) lobbyCodeLockBase = Mathf.Max(lobbyCodeLockBase, now);
            lobbyCodeReopening = reopening;
            float t = now - lobbyCodeChangedAt;
            lobbyCodeLine.anchoredPosition = new Vector2(44, centerY + CodeLineHeight * .5f);
            var color = playerColors[localSlot];
            float lastLock = lobbyCodeLockBase + (CodeSlots - 1) * CodeInStagger + CodeInScramble; // 절대 시각
            if (show)
            {
                float label = Out(Range(t, 0, .22f));
                lobbyCodeLabel.color = PaletteAsset.WithAlpha(Theme.hint, label);
                lobbyCodeLabel.rectTransform.anchoredPosition = new Vector2(-14 * (1 - label), 0);
            }
            else
            {
                float label = 1 - Out(Range(t, CodeOutStagger * CodeSlots + .05f, CodeOutStagger * CodeSlots + .25f));
                lobbyCodeLabel.color = PaletteAsset.WithAlpha(Theme.hint, label);
                lobbyCodeLabel.rectTransform.anchoredPosition = new Vector2(-14 * (1 - label), 0);
            }
            for (int i = 0; i < CodeSlots; i++)
            {
                var c = lobbyCodeChars[i];
                char final = i < lobbyLastCode.Length ? lobbyLastCode[i] : ' ';
                float x = CodeStartX + (i + .5f) * CodeAdvance;
                float alpha, y, scale = 1; char shown;
                if (show)
                {
                    float start = CodeInStart + i * CodeInStagger;
                    float enter = Out(Range(t, start, start + .14f));
                    float lockAt = lobbyCodeLockBase + i * CodeInStagger + CodeInScramble;
                    bool locked = !reopening && now >= lockAt;
                    shown = locked ? final : ScrambleGlyph(i, now);
                    alpha = t < start ? 0 : enter * (locked ? 1 : .55f);
                    y = 16 * (1 - enter);
                    float since = now - lockAt;
                    if (locked && since < .16f) scale = 1 + .22f * Mathf.Sin(Mathf.Clamp01(since / .16f) * Mathf.PI);
                }
                else
                {
                    float start = (CodeSlots - 1 - i) * CodeOutStagger;
                    float leave = Range(t, start + CodeOutScramble, start + CodeOutScramble + CodeOutFade);
                    shown = t < start ? final : ScrambleGlyph(i, now);
                    alpha = (t < start ? 1 : .6f) * (1 - Out(leave));
                    y = 18 * In(leave);
                }
                c.text = alpha > .001f ? shown.ToString() : "";
                c.color = PaletteAsset.WithAlpha(color, alpha);
                c.rectTransform.anchoredPosition = new Vector2(x, -CodeLineHeight * .5f + y);
                c.rectTransform.localScale = Vector3.one * scale;
            }
            // 복사 버튼: 마지막 글자가 맞춰진 뒤 튀어나오고, 숨길 때는 먼저 줄어든다. 다시 여는 동안은 숨긴다.
            float copy = show && !reopening ? OutBack(Range(now, lastLock, lastLock + .2f), 2.2f) : show ? 0 : 1 - Out(Range(t, 0, .12f));
            if (!show && !lobbyCopy.gameObject.activeSelf) copy = 0;
            lobbyCodeReady = show && !reopening && now >= lastLock + .2f;
            lobbyCopy.gameObject.SetActive(copy > .01f);
            lobbyCopy.interactable = lobbyCodeReady;
            float copied = now - lobbyCopiedAt;
            bool showCopied = copied >= 0 && copied < CopiedSeconds;
            lobbyCopyLabel.text = showCopied ? "복사됨" : "복사";
            lobbyCopyLabel.color = showCopied ? color : Theme.white;
            float press = showCopied ? 1 + .12f * Mathf.Sin(Mathf.Clamp01(copied / .22f) * Mathf.PI) : 1;
            lobbyCopy.transform.localScale = Vector3.one * Mathf.Max(0, copy) * press;
        }

        /// <summary>P2 자리의 "상대를 기다리는 중" + 점 세 개 물결. visible이 0이면 숨긴다.</summary>
        void AnimateWaiting(Vector2 at, float visible, float now)
        {
            bool on = visible > .001f;
            lobbyWaiting.gameObject.SetActive(on);
            foreach (var dot in lobbyWaitDots) dot.gameObject.SetActive(on);
            if (!on) return;
            float breath = .72f + .28f * (.5f + .5f * Mathf.Sin(now * Mathf.PI * 2 / WaitBreathSeconds));
            lobbyWaiting.color = PaletteAsset.WithAlpha(Theme.white, breath * visible);
            float width = lobbyWaiting.preferredWidth;
            float total = width + 10 + 2 * 14 + WaitDotSize;
            float left = at.x - total * .5f;
            lobbyWaiting.rectTransform.anchoredPosition = new Vector2(left + width * .5f, at.y);
            lobbyWaiting.rectTransform.localScale = Vector3.one * (.94f + .06f * visible);
            for (int i = 0; i < lobbyWaitDots.Length; i++)
            {
                // 점마다 0.18주기씩 늦게 튄다(사인 양의 반쪽만 써서 바닥에서 잠깐 쉰다).
                float phase = now / WaitWaveSeconds * Mathf.PI * 2 - i * 1.1f;
                float hop = Mathf.Max(0, Mathf.Sin(phase));
                var dot = lobbyWaitDots[i].rectTransform;
                dot.anchoredPosition = new Vector2(left + width + 10 + i * 14 + WaitDotSize * .5f, at.y - 8 + hop * WaitDotBounce);
                lobbyWaitDots[i].color = PaletteAsset.WithAlpha(Theme.white, (.35f + .65f * hop) * visible);
            }
        }

        void AnimateLobbyHud()
        {
            if (lobbyHud == null) return;
            var w = motionRunner.World;
            var s = lobbyHudState;
            float now = Time.unscaledTime;
            float tick = LobbyExitTick(w);
            bool exiting = tick > 0;
            float statusFade = 1 - Out(Range(tick, ExitApex - 4, ExitApex + 6));
            float spread = Smooth(Range(tick, ExitApex - 2, ExitFallEnd - 2));

            // 방 코드 줄: 혼자 기다릴 때만 보인다. 높이는 상단 두 자리 이름의 가운데에 맞춘다.
            bool showLine = !exiting && s.Solo && (s.ShowCode || s.Reopening) && (!string.IsNullOrEmpty(s.Code) || lobbyLastCode.Length > 0);
            AnimateLobbyCode(showLine, s.Reopening, s.Code, ScoreLabelCenter(0).y, now);

            // P2가 들어오면 "기다리는 중"이 사라지며 이름이 톡 나타나고, 나가면 반대로 돌아온다.
            bool peer = !s.Solo;
            if (peer != lobbyPeerPresent) { lobbyPeerPresent = peer; lobbyPeerChangedAt = now; }
            float peerT = Range(now - lobbyPeerChangedAt, 0, .32f);
            float peerIn = peer ? Out(peerT) : 1 - Out(peerT);
            for (int p = 0; p < 2; p++)
            {
                bool ready = exiting || (p == 0 ? s.Ready0 : s.Ready1);
                bool local = p == localSlot;
                if (ready != lobbyShownReady[p]) { lobbyShownReady[p] = ready; lobbyReadyAt[p] = now; }
                var name = lobbyNames[p];
                var status = lobbyStatus[p];
                Vector2 target = ScoreLabelCenter(p);
                Vector2 start = target + Vector2.right * (p == 0 ? LobbySlotSpread : -LobbySlotSpread);
                Vector2 at = Vector2.Lerp(start, target, spread);
                float scale = Mathf.Lerp(LobbyNameScale, 1, spread);
                float sinceReady = now - lobbyReadyAt[p];
                float readyPop = ready && sinceReady < .3f ? 1 + .18f * Mathf.Sin(sinceReady / .3f * Mathf.PI) : 1;
                // 다른 자리(P2 또는 손님 화면의 P1)는 들어오는 동안 이름이 톡 커지며 나타난다.
                float appear = p == 0 ? 1 : peerIn;
                float appearPop = appear < 1 ? Pop(appear) : 1;
                name.rectTransform.anchoredPosition = at;
                name.rectTransform.localScale = Vector3.one * scale * (exiting ? 1 : readyPop) * Mathf.Max(0, appearPop);
                // 닉네임(2026-11-21, 없으면 P1/P2). 점수판 글자와 같은 폭 기준으로 줄여 도착 자리에서 크기가 맞는다.
                string playerName = motionPresenter.PlayerName(p);
                string nameText = ready || exiting || !local ? playerName : playerName + " · 나";
                if (name.text != nameText)
                {
                    name.text = nameText;
                    if (lobbyNameBase[p] == 0) lobbyNameBase[p] = name.fontSize;
                    name.fontSize = FrontendScoreView.FitFontSize(name, lobbyNameBase[p], FrontendScoreView.MaxNameWidth);
                }
                name.color = PaletteAsset.WithAlpha(playerColors[p], appear);
                string line; Color color;
                if (p == 1 && s.Solo) { line = ""; color = Theme.white; }
                else if (ready) { line = "준비 완료"; color = playerColors[p]; }
                else if (s.Solo) { line = ""; color = Theme.white; }
                else if (local) { line = "[R] 준비"; color = PodiumReadyVisible ? Theme.white : PaletteAsset.WithAlpha(Theme.white, .45f); }
                else { line = "준비 전"; color = Theme.hint; }
                status.text = line;
                status.color = PaletteAsset.WithAlpha(color, color.a * statusFade * appear);
                status.rectTransform.anchoredPosition = at + Vector2.down * (46 * scale);
                status.rectTransform.localScale = Vector3.one * (exiting ? 1 : readyPop);
                if (p == 1) AnimateWaiting(at, exiting ? 0 : 1 - peerIn, now);
            }
            lobbyNotice.gameObject.SetActive(!string.IsNullOrEmpty(s.Notice));
            lobbyNotice.text = s.Notice ?? "";
        }

        /// <summary>
        /// 퇴장 전환 동안의 월드 표시(MatchPresenter의 LateUpdate에서): 두 선수가 눌렸다 튀어 오르고 아래로 가속해 화면 밖으로 빠진다.
        /// 판정 위치는 준비가 확정된 자리에 멈춰 있고(LobbyExit), 이 연출은 그림에만 더한다. 점수판 점도 같은 박자로 드러난다.
        /// </summary>
        void PresentLobbyExit(SimWorld w, float tick)
        {
            int absent = motionRunner.AbsentSlot;
            PoseExitJump(w, tick, absent);
            // 도약하는 순간 한 번: 기존 점프 연출(먼지·소리)을 두 선수 발밑에서 보여 준다.
            int exitStart = w.State.Tick - w.State.Match.PhaseTick;
            if (tick >= ExitSquashEnd && lobbyExitSoundTick != exitStart)
            {
                lobbyExitSoundTick = exitStart;
                for (int k = 0; k < 2; k++)
                {
                    if (k == absent) continue;
                    var f = w.State.GetFighter(k);
                    motionPresenter.PresentDisplayEvent(new SimEvent(w.State.Tick, SimEventType.Jump, k, f.Position, new Vec2(0, 12f), 0f));
                }
            }
            points.RenderLobbyReveal(Range(tick, ExitApex + 4, ExitFallEnd + 8));
        }

        /// <summary>
        /// 로비 퇴장의 도약 그림(눌림 → 튀어 오름 → 아래로 가속해 화면 밖으로). 메인 화면에서 연습으로 나갈 때도 같은 박자를 쓴다(2026-11-21).
        /// 판정 위치는 그대로이고 그림에만 더한다. absent 자리(비어 있는 선수)는 건너뛴다.
        /// </summary>
        /// <param name="scale">높이·낙하 거리 배율(메인 화면의 근접 구도에서 작게, 기본 1).</param>
        void PoseExitJump(SimWorld w, float tick, int absent, float scale = 1f)
        {
            float half = w.Config.HalfBody;
            float squash = Range(tick, 0, ExitSquashEnd), rise = Range(tick, ExitSquashEnd, ExitApex), fall = Range(tick, ExitApex, ExitFallEnd);
            float y = (tick < ExitApex ? ExitJumpHeight * Out(rise) * (tick < ExitSquashEnd ? 0 : 1) : ExitJumpHeight - ExitFallDistance * In(fall)) * scale;
            float stretch = tick < ExitSquashEnd ? -.16f * Out(squash) : tick < ExitApex ? .14f * (1 - rise) : .06f + .12f * fall;
            for (int k = 0; k < 2; k++)
            {
                if (k == absent) continue;
                motionPresenter.ScaleFighterVisual(k, 1 - stretch * .7f, 1 + stretch, half);
                motionPresenter.OffsetFighterVisual(k, y);
            }
            // 발이 땅을 떠나면 바닥 그림자도 함께 떠난다(그림자는 판정 위치에 그려지므로 남지 않게 숨긴다).
            if (tick >= ExitSquashEnd) motionPresenter.HideFighterShadows();
        }
    }
}
