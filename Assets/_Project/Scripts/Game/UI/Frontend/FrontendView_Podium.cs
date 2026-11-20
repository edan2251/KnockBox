using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 점수 없는 자유 공간 위의 작은 표시(2026-11-16 게임 흐름). 월드를 가리지 않는다.
    /// - 결과 공간(Podium, CP1): 상단에 승자·세트 점수, 하단에 다시하기 안내와 O/X.
    /// - 로비(Lobby, CP3): 오른쪽에 기존 방 패널(방 코드·복사·상태·돌아가기)만 배경 없이 띄우고, 하단에 준비 안내와 O.
    /// 새 UI를 만들지 않고 기존 결과·방 화면의 같은 오브젝트를 쓰며, 들어갈 때 원래 크기·글자를 기억했다가 다른 화면으로 나갈 때 되돌린다.
    /// 준비 상태는 머리 위 표식(MatchPresenter)이 보여준다. 배치·크기·문구는 임시이며 연출 단계 뒤 다시 다듬는다.
    /// </summary>
    public sealed partial class FrontendView
    {
        const string PodiumReadyLabel = "O  다시하기", LobbyReadyLabel = "O  준비", PodiumBackLabel = "X  나가기";
        static readonly Vector2 PodiumButtonSize = new Vector2(300, 84);
        const int PodiumButtonFont = 40;
        bool podiumShown, podiumCaptured, podiumReady, lobbySolo;
        int podiumWinner;
        Vector2 savedReadySize, savedBackSize, savedReadyLabelSize, savedBackLabelSize;
        int savedReadyFont, savedBackFont;
        string savedReadyText, savedBackText;
        Color savedScoreColor;
        Image menuBackground;

        bool FreeSpaceScreen => Screen == FrontendScreen.Podium || Screen == FrontendScreen.Lobby;

        /// <summary>결과 공간·로비에서 준비를 켜고 끌 수 있는지(두 선수가 착지한 뒤, 상대가 있을 때).</summary>
        public bool PodiumReadyVisible => FreeSpaceScreen && !lobbySolo && motionRunner.FrontendWorld.InFreeSpace
            && motionRunner.FrontendWorld.State.Match.PhaseTick >= motionRunner.FrontendWorld.FreeSpaceUnlockTick;

        /// <summary>마우스가 결과 공간·로비의 화면 버튼 위에 있는지. 버튼을 누른 클릭은 펀치로 보내지 않는다.</summary>
        public bool PointerOverPodiumButton()
        {
            if (!FreeSpaceScreen || Mouse.current == null) return false;
            var point = Mouse.current.position.ReadValue();
            return Over(resultReady, point) || Over(resultBack, point) || Over(copy, point) || Over(lobbyBack, point) || Over(lobbyCopy, point);
        }

        static bool Over(Button button, Vector2 point) => button != null && button.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, point, null);

        public void Podium(int winner, int score0, int score1, bool online, bool localReady)
        {
            podiumWinner = winner; podiumReady = localReady; lobbySolo = false;
            setScore.text = "P" + (winner + 1) + " 승리     세트 " + score0 + " : " + score1;
            pickHint.text = localReady
                ? (online ? "준비 완료 · 상대를 기다리는 중   (R / O 다시 누르면 취소)" : "다시 시작합니다")
                : (online ? "R / O  다시하기 준비   ·   둘 다 준비하면 새 경기" : "R / O  다시하기");
        }

        /// <param name="solo">방장이 혼자 기다리는 중(준비 버튼 없음).</param>
        public void Lobby(bool solo, bool localReady)
        {
            lobbySolo = solo; podiumReady = localReady;
            pickHint.text = solo ? "친구에게 방 코드를 보내 주세요 · 기다리는 동안 자유롭게 움직일 수 있어요"
                : localReady ? "준비 완료 · 상대를 기다리는 중   (R / O 다시 누르면 취소)"
                : "R / O  준비   ·   둘 다 준비하면 경기 시작";
        }

        void ShowFreeSpace(FrontendScreen screen)
        {
            bool podium = screen == FrontendScreen.Podium, lobbyScreen = screen == FrontendScreen.Lobby;
            bool on = podium || lobbyScreen;
            split.gameObject.SetActive(!on);
            if (on)
            {
                setScore.gameObject.SetActive(podium);
                pickHint.gameObject.SetActive(podium);
            }
            if (lobbyHud != null) lobbyHud.gameObject.SetActive(lobbyScreen);
            if (!podium) HideResultsHud();
            // 로비 화면은 메뉴 배경을 숨겨 월드가 보이게 한다(2026-11-21: 메인 화면의 바닥·선수는
            // 실제 로비 월드가 맡는다. 게임 시작 메뉴에서 방을 만들면 그 공간과 선수가 그대로 로비가 되어 따로 들어오는 연출이 없다).
            menuBackground ??= menu.Find("Background")?.GetComponent<Image>();
            if (menuBackground != null) menuBackground.gameObject.SetActive(!lobbyScreen);
            var readyText = resultReady.GetComponentInChildren<Text>(true);
            var backText = resultBack.GetComponentInChildren<Text>(true);
            var readyRect = (RectTransform)resultReady.transform;
            var backRect = (RectTransform)resultBack.transform;
            if (on)
            {
                if (!podiumCaptured)
                {
                    podiumCaptured = true;
                    savedReadySize = readyRect.sizeDelta; savedBackSize = backRect.sizeDelta;
                    savedReadyLabelSize = readyText.rectTransform.sizeDelta; savedBackLabelSize = backText.rectTransform.sizeDelta;
                    savedReadyFont = readyText.fontSize; savedBackFont = backText.fontSize;
                    savedReadyText = readyText.text; savedBackText = backText.text;
                    savedScoreColor = setScore.color;
                }
                podiumShown = true;
                readyRect.sizeDelta = backRect.sizeDelta = PodiumButtonSize;
                readyText.rectTransform.sizeDelta = backText.rectTransform.sizeDelta = PodiumButtonSize;
                readyText.fontSize = backText.fontSize = PodiumButtonFont;
                readyText.text = podium ? PodiumReadyLabel : LobbyReadyLabel; backText.text = PodiumBackLabel;
                return;
            }
            if (!podiumShown) return;
            podiumShown = false;
            readyRect.sizeDelta = savedReadySize; backRect.sizeDelta = savedBackSize;
            readyText.rectTransform.sizeDelta = savedReadyLabelSize; backText.rectTransform.sizeDelta = savedBackLabelSize;
            readyText.fontSize = savedReadyFont; backText.fontSize = savedBackFont;
            readyText.text = savedReadyText; backText.text = savedBackText;
            setScore.color = savedScoreColor;
        }

        void AnimatePodium()
        {
            var w = motionRunner.FrontendWorld;
            var match = w.Config.Match;
            bool lobbyScreen = Screen == FrontendScreen.Lobby;
            float tick = w.InFreeSpace ? w.State.Match.PhaseTick + motionRunner.RenderAlpha : 0;
            float title = Out(Range(tick, match.ResultsDropTick, match.ResultsDropTick + 20));
            float question = Out(Range(tick, w.FreeSpaceUnlockTick, w.FreeSpaceUnlockTick + 20));
            if (lobbyScreen)
            {
                // 로비(2026-11-19): 방 패널·하단 준비/나가기 버튼 없이 상단 표시만 쓴다. 준비는 R, 나가기는 Esc.
                AnimateLobbyHud();
                pickHintAlpha.alpha = 0;
                resultReady.gameObject.SetActive(false);
                resultBack.gameObject.SetActive(false);
                return;
            }
            if (ResultsCeremony(w)) { AnimateResultsHud(w); return; }
            HideResultsHud();
            {
                setScore.color = PaletteAsset.WithAlpha(playerColors[podiumWinner], title);
                setScore.rectTransform.anchoredPosition = new Vector2(740, -62 + (1 - title) * 40);
            }
            pickHintAlpha.alpha = question;
            pickHint.rectTransform.anchoredPosition = new Vector2(450, -880 + (1 - question) * 30);
            bool canReady = PodiumReadyVisible;
            bool showReady = !lobbySolo || !lobbyScreen;
            resultReady.gameObject.SetActive(question > 0 && showReady);
            // 로비는 방 패널의 돌아가기가 나가기를 맡는다.
            resultBack.gameObject.SetActive(question > 0 && !lobbyScreen);
            resultReady.interactable = canReady;
            resultBack.interactable = question > 0;
            resultReady.image.color = resultBack.image.color = PaletteAsset.Transparent;
            var readyColor = podiumReady ? playerColors[localSlot] : Theme.white;
            resultReady.GetComponentInChildren<Text>().color = PaletteAsset.WithAlpha(readyColor, question);
            resultBack.GetComponentInChildren<Text>().color = PaletteAsset.WithAlpha(Theme.white, question);
            ((RectTransform)resultReady.transform).anchoredPosition = new Vector2(lobbyScreen ? 810 : 640, -950 - (1 - question) * 60);
            ((RectTransform)resultBack.transform).anchoredPosition = new Vector2(980, -950 - (1 - question) * 60);
        }

        /// <summary>
        /// 결과 공간·로비 맵: 경기 재구성처럼 지형이 솟고 배경이 켜지며, 재구성이 끝나면 조명이 켜진다(GO와 같은 펄스).
        /// 두 선수는 낙하가 시작될 때 나타나며, 방장이 혼자 기다리는 로컬 로비의 빈 자리(P2)는 그리지 않는다.
        /// 체력바는 숨기고 조작 관련 표시(조준선·그림자)는 보인다.
        /// </summary>
        /// <summary>이어 간 로비 장면을 "입장 연출이 끝난 뒤"로 보기 위해 더하는 틱(재구성·조명 펄스보다 충분히 길다).</summary>
        const float ContinuedSceneTicks = 100000f;

        void PresentResultsSpace(SimWorld w)
        {
            var match = w.Config.Match;
            float tick = w.State.Match.PhaseTick + motionRunner.RenderAlpha;
            // 메인 화면의 로비 공간(2026-11-21, FrontendController가 연 혼자 로비 월드).
            if (MenuSpaceActive && w.InLobby && MenuScreen) { PresentMenuSpace(w, tick); return; }
            motionPresenter.LobbyMarkerFade = 1f;
            // 결과 발표(2026-11-20): 빈 시상대 상승 → 두 선수 낙하 → 결과 글자 낙하. 그 전 설정(기록 재생 등)은 아래 기존 연출.
            if (ResultsCeremony(w)) { PresentResultsCeremony(w, tick); return; }
            HideResultSign();
            // 방장 혼자 로비를 이어 간 공유 로비: 맵·조명은 이미 다 켜진 상태로 두고, 이어 간 선수는 등장 연출 없이 그대로 보인다.
            // 로비 → 첫 경기 전환(LobbyExit)도 다 켜진 로비 장면 위에서 두 선수만 퇴장한다.
            bool exit = w.InLobbyExit;
            bool continued = (motionRunner.LobbySceneContinued && w.InLobby) || exit;
            float sceneTick = continued ? tick + ContinuedSceneTicks : tick;
            float build = Range(sceneTick, 0, CardCollectionRules.RebuildTicks);
            float spawn = Pop(Range(tick, match.ResultsDropTick, match.ResultsDropTick + 12));
            bool lit = build >= 1;
            int absent = motionRunner.AbsentSlot;
            float spawn0 = absent == 0 ? 0 : continued && (exit || motionRunner.ContinuedLobbySlot == 0) ? 1 : spawn;
            float spawn1 = absent == 1 ? 0 : continued && (exit || motionRunner.ContinuedLobbySlot == 1) ? 1 : spawn;
            if (!exit) points.Root.gameObject.SetActive(false);
            startCue.gameObject.SetActive(false);
            // 로비 → 첫 경기 전환: 선수가 발판을 지나 떨어진 뒤에는 경기 라운드가 바뀔 때(KO 퇴장)와 같은 방식으로 로비 장면을 내보낸다.
            // 지형 조각이 아래로 빠지고 배경이 서서히 걷히며 조명이 꺼진다. 다 걷힌 뒤 1라운드가 기존 재구성(지형 솟음·배경 켜짐)으로 이어져
            // 로비 배경이 한 프레임에 사라지는 끊김이 없다.
            float leave = exit ? Range(tick, LobbySceneLeaveStart, match.LobbyExitTicks) : 0;
            if (leave > 0)
            {
                motionPresenter.PresentFrontendStage(leave, false, 1 - Smooth(leave), spawn0, spawn1, 0, false,
                    FrontendView.RoundLight(MatchPhase.KO, true, true, leave, false), false, false);
                motionPresenter.Background.SetRoundLightPulse(-1);
            }
            else
            {
                // 퇴장 중에는 조준선·경계 표시를 끈다(조작이 잠겼다).
                motionPresenter.PresentFrontendStage(build, true, Out(build), spawn0, spawn1, 0, !exit, lit ? 1f : 0f, false, false);
                motionPresenter.Background.SetRoundLightPulse(lit ? (sceneTick - CardCollectionRules.RebuildTicks) / 60f : -1);
                // 로비에 떨어지는 선수는 화면 위끝 밖에서 떨어지는 것처럼 그린다(2026-11-21).
                if (w.InLobby && !exit) LiftArrivals(w, absent);
            }
            if (w.InResultsSpace) PresentPodiumRise(w, tick);
            if (exit) PresentLobbyExit(w, tick);
        }

        // 결과 공간 입장(CP5 기본 구조, 수치·곡선은 임시): 시상대가 낮게 묻혀 있다가 승자를 태우고 솟아오르고(0.5초),
        // 승자가 한 번 짧게 뛰었다 착지한 뒤 조작이 풀린다(ResultsUnlockTick). 시뮬레이션의 승자는 처음부터 시상대 위에 서 있다.
        const float PodiumRiseTicks = 30, PodiumHopStart = 34, PodiumHopTicks = 22, PodiumHopHeight = 1.6f;
        Maps.MapDefinition podiumMap; int podiumBox = -1; float podiumHeight;
        int podiumLandedRound = -1; SimWorld podiumLandedWorld;

        void PresentPodiumRise(SimWorld w, float tick)
        {
            var map = motionRunner.ResultsMap;
            if (map != podiumMap)
            {
                podiumMap = map; podiumBox = -1; podiumHeight = 0;
                if (map != null && !string.IsNullOrEmpty(map.PodiumBoxId))
                    for (int i = 0; i < map.BoxCount; i++)
                        if (map.Box(i).id == map.PodiumBoxId) { podiumBox = i; var b = map.Box(i); podiumHeight = b.maxY - b.minY; break; }
            }
            int winner = SimWorld.ResultsWinner(w.State.Match);
            float start = w.Config.Match.ResultsDropTick;
            float sunk = podiumBox >= 0 ? -(1 - Out(Range(tick, start, start + PodiumRiseTicks))) * podiumHeight : 0;
            float hopT = Mathf.Clamp01((tick - start - PodiumHopStart) / PodiumHopTicks);
            float hop = 4f * PodiumHopHeight * hopT * (1 - hopT);
            if (podiumBox >= 0) motionPresenter.OffsetTerrainBox(podiumBox, sunk);
            motionPresenter.OffsetFighterVisual(winner, sunk + hop);
            // 자동 점프의 착지는 한 번만 기존 착지 효과(먼지·소리)로 보여준다.
            if (hopT >= 1 && (podiumLandedWorld != w || podiumLandedRound != w.State.Match.Round))
            {
                podiumLandedWorld = w; podiumLandedRound = w.State.Match.Round;
                var f = w.State.GetFighter(winner);
                motionPresenter.PresentDisplayEvent(new SimEvent(w.State.Tick, SimEventType.Land, winner, f.Position, Vec2.Zero, 18f));
            }
        }
    }
}
