using System;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 메인 메뉴의 두 쪽(2026-11-16 게임 흐름 CP3): 첫 화면은 게임 시작·옵션·게임 종료, 게임 시작을 누르면 방 만들기·코드로 참가·연습.
    /// 버튼은 Frontend 프리팹의 Menu 아래 실제 오브젝트이며(Start_game, Options, Quit, Create_room, Join_room, Practice), 배치·문구는 프리팹에서 고친다.
    /// 뒤로는 화면 왼쪽 아래의 작은 "[Esc] 뒤로"(런타임 생성)와 Esc 키다.
    /// 2026-11-21 메인 화면: 첫 화면은 단색 배경 가운데에 제목·메뉴만 둔다. 게임 시작을 누르면 제목·메뉴가 오른쪽으로 비키고,
    /// 왼쪽에 실제 로비 맵 공간(FrontendController가 여는 혼자 로비 월드: 바닥 재구성·조명·그림자·낙하·착지는 기존 연출 그대로)이 열린다.
    /// 머리 위 이름(닉네임)은 눌러서 그 자리에서 고친다. 뒤로 가면 선수가 작게 뛰어 왼쪽 밖으로 나가고 공간이 걷힌다.
    /// </summary>
    public sealed partial class FrontendView
    {
        public event Action Options, Quit;
        /// <summary>닉네임을 확정했다(정리 전 입력값). 저장·전송은 FrontendController가 맡는다.</summary>
        public event Action<string> NicknameEdited;
        [SerializeField] Button startGame, optionsButton, quitButton;
        /// <summary>참이면 게임 시작 하위 메뉴(방 만들기·코드로 참가·연습)를 보여준다.</summary>
        public bool StartMenuOpen { get; private set; }

        // 제목·메뉴 묶음의 자리(1920×1080 기준): 첫 화면은 가운데, 게임 시작 메뉴는 오른쪽. 세로는 묶음 전체가 화면 가운데.
        const float MenuCenterX = 480f, MenuSideX = 845f, MenuTopY = 259f, MenuSideSeconds = .26f;
        float menuSide, menuSideVelocity, pageChangedAt;

        /// <summary>메인 메뉴 쪽을 바꾸고 버튼 등장 연출을 처음부터 다시 한다(제목은 자리만 옮긴다).</summary>
        public void SetMenuPage(bool start)
        {
            if (StartMenuOpen != start) CancelNameEdit();
            StartMenuOpen = start;
            ApplyMenuPage();
            pageChangedAt = Time.unscaledTime;
        }

        void ApplyMenuPage()
        {
            foreach (var b in new[] { create, joinMenu, practice })
                if (b != null) b.gameObject.SetActive(StartMenuOpen);
            foreach (var b in new[] { startGame, optionsButton, quitButton })
                if (b != null) b.gameObject.SetActive(!StartMenuOpen);
        }

        void BindMenuPages()
        {
            Bind(startGame, () => SetMenuPage(true));
            Bind(optionsButton, () => Options?.Invoke());
            Bind(quitButton, () => Quit?.Invoke());
        }

        /// <summary>절차식 화면(Sandbox·검사용)에도 같은 버튼을 만든다. Main은 프리팹의 저장된 버튼을 쓴다.</summary>
        void BuildMenuPages(RectTransform mainContent)
        {
            startGame = FrontendWidgets.Button(mainContent, "Start_game", new Rect(120, 240, 720, 94), "게임 시작", Theme, PaletteAsset.Transparent, Theme.white, () => SetMenuPage(true), 46);
            optionsButton = FrontendWidgets.Button(mainContent, "Options", new Rect(120, 354, 720, 94), "옵션", Theme, PaletteAsset.Transparent, Theme.white, () => Options?.Invoke(), 36);
            quitButton = FrontendWidgets.Button(mainContent, "Quit", new Rect(120, 468, 720, 94), "게임 종료", Theme, PaletteAsset.Transparent, Theme.white, () => Quit?.Invoke(), 36);
        }

        /// <summary>
        /// 메인 메뉴 묶음의 매 프레임 배치: 제목·메뉴가 첫 화면(가운데)과 게임 시작 메뉴(오른쪽) 사이를 부드럽게 오가고,
        /// 버튼은 쪽이 바뀔 때마다 차례로 들어온다. 제목은 화면에 들어올 때만 들어오는 연출을 한다.
        /// </summary>
        void AnimateMenuColumn(bool main, float screenAge)
        {
            float target = StartMenuOpen ? 1 : 0;
            menuSide = Mathf.SmoothDamp(menuSide, target, ref menuSideVelocity, MenuSideSeconds * .45f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(menuSide - target) < .0005f) { menuSide = target; menuSideVelocity = 0; }
            float x = Mathf.Lerp(MenuCenterX, MenuSideX, Smooth(Mathf.Clamp01(menuSide)));
            ((RectTransform)menuAlpha.transform).anchoredPosition = new Vector2(x - 100 * (1 - menuAlpha.alpha), -MenuTopY);
            if (!main) return;
            float pageAge = Time.unscaledTime - pageChangedAt;
            int order = 0;
            for (int i = 0; i < menuEntries.Length; i++)
            {
                var item = menuEntries[i];
                if (item == null) continue;
                bool title = logo != null && item == logo.rectTransform;
                float t;
                if (title) t = Range(screenAge, 0, .32f);
                else
                {
                    if (!item.gameObject.activeSelf) continue;
                    t = Range(pageAge, .06f + order * .045f, .38f + order * .045f);
                    order++;
                }
                item.anchoredPosition = menuEntryPositions[i] + Vector2.right * 85 * (1 - Out(t));
                menuEntryAlpha[i].alpha = Out(t);
            }
        }

        // ── 메인 화면의 로비 공간(2026-11-21) ──────────────────────────────────────────────

        /// <summary>FrontendController가 메인 화면용 혼자 로비 월드를 열어 두었는지(월드가 보이도록 배경·덮개를 걷는다).</summary>
        public bool MenuSpaceActive { get; set; }
        /// <summary>
        /// 공간의 퇴장: 뒤로(작게 뛰어 왼쪽 밖으로, 장면 걷힘), 연습(로비 → 경기 전환과 같은 도약·낙하, 장면 걷힘),
        /// 방 만들기·참가(같은 도약·낙하 뒤 넓은 로비 구도로 물러남, 장면은 남김).
        /// </summary>
        public enum SpaceExit { None, Back, Practice, Room, Join }
        SpaceExit spaceExit;
        float spaceExitAt, spaceExitBuild, spaceBuild, spaceSpawn, spaceExitScale = 1f;
        // 공간 장면이 보이는 정도(0~1). 단색 배경을 이만큼 걷어, 꺼진 월드의 어두운 색과 단색 배경 사이가 한 프레임에 바뀌지 않게 한다.
        float spaceSceneOpacity;
        bool spaceExitFighter, spaceJumpPlayed;
        const float HopSquash = .07f, HopSeconds = .42f, HopHeight = 1.3f, BackSceneStart = .12f, BackSceneEnd = .62f;
        /// <summary>메인 화면 공간: 누르자마자 바닥이 솟기 시작해 이 틱 동안 다 쌓이고, 선수는 MenuDropTick에 떨어지기 시작한다(로비 입장보다 빠름).</summary>
        public const int MenuDropTick = 18;
        const float MenuBuildTicks = 24;

        /// <summary>메인 화면 공간에서 방을 만들거나 참가하는 동안 예전 방 패널을 숨긴다(FrontendController가 정한다).</summary>
        public bool HideRoomPanel { get; set; }

        /// <summary>참가 뒤 선수가 화면 밖으로 나가 있는 동안(연결을 기다림). 연결이 안 되면 FrontendController가 다시 떨어뜨린다.</summary>
        public bool SpaceFighterAway { get; set; }

        public SpaceExit CurrentSpaceExit => spaceExit;
        public bool SpaceExiting => spaceExit != SpaceExit.None;
        public bool SpaceExitFinished => spaceExit != SpaceExit.None && Time.unscaledTime - spaceExitAt >= SpaceExitSeconds(spaceExit);

        float SpaceExitSeconds(SpaceExit kind) => kind == SpaceExit.Back ? BackSceneEnd
            : kind == SpaceExit.Practice ? motionRunner.World.Config.Match.LobbyExitTicks / 60f : ExitFallEnd / 60f;

        /// <summary>공간 닫기 시작(이미 닫는 중이면 무시). 지금 보이는 장면에서 이어서 닫는다.</summary>
        public void BeginSpaceExit(SpaceExit kind)
        {
            if (spaceExit != SpaceExit.None || kind == SpaceExit.None) return;
            CancelNameEdit();
            spaceExit = kind; spaceExitAt = Time.unscaledTime;
            spaceExitBuild = spaceBuild; spaceExitFighter = spaceSpawn > .5f && !SpaceFighterAway; spaceJumpPlayed = false;
            spaceExitScale = MenuMotionScale;
        }

        public void ClearSpaceExit()
        {
            spaceExit = SpaceExit.None;
            if (motionPresenter != null) motionPresenter.LobbyMarkerFade = 1f;
        }

        bool MenuScreen => Screen == FrontendScreen.Main || Screen == FrontendScreen.Join || Screen == FrontendScreen.Waiting || Screen == FrontendScreen.Options;

        /// <summary>머리 위 이름을 띄울지(메인 화면 공간이 열려 있고 선수가 나가는 중이 아닐 때).</summary>
        public bool MenuSpaceShowsName => MenuSpaceActive && Screen != FrontendScreen.Options && MenuScreen && spaceExit == SpaceExit.None && !SpaceFighterAway;

        // 접속 전의 색 미리보기일 뿐 실제 네트워크 슬롯은 바꾸지 않는다.
        public int MenuPreviewSlot => Screen == FrontendScreen.Join || spaceExit == SpaceExit.Join ? 1 : 0;
        float menuColorBlend;
        Color MenuPlayerColor => Color.Lerp(playerColors[0], playerColors[1], menuColorBlend);
        public bool CodeSubmitPressed => Screen == FrontendScreen.Join && !NameInputBusy
            && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == CodeInput.gameObject
            && Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);

        // ── 근접 구도(2026-11-21 2차: 처음 메인 화면의 큰 캐릭터 크기) ───────────────────────────
        // 화면 높이 1080 기준 선수 몸 약 350px(처음 메인 화면의 UI 캐릭터와 같은 크기), 발은 화면 위에서 960px, 몸 가운데는 왼쪽에서 423px.
        const float CloseSize = 1.25f, CloseFeetFromCenter = 420f / 540f, CloseSideFromCenter = 537f / 540f, CloseSeconds = .32f;
        float closeWeight, closeVelocity;
        bool closeShown;

        /// <summary>근접 구도에서 그림 연출(점프 높이·머리 위 글자)을 맞추는 배율. 경기장 전체 구도면 1.</summary>
        float MenuMotionScale => motionPresenter.CameraRig != null
            ? motionPresenter.CameraRig.FramingScale * Mathf.Lerp(1f, 2f, motionPresenter.CameraRig.CloseWeight) : 1f;

        /// <summary>
        /// 게임 시작 메뉴·코드 입력은 선수를 크게 보는 근접 구도, 방 만들기·참가로 선수가 떨어져 나가는 중(정점 뒤)·대기 화면은
        /// 로비 전체 구도로 부드럽게 물러난다. 공간이 처음 열릴 때는 장면이 비어 있으므로 바로 근접 구도에서 시작한다.
        /// </summary>
        void UpdateCloseFraming(SimWorld w)
        {
            var rig = motionPresenter.CameraRig;
            if (rig == null) return;
            // 방 만들기·참가는 선수가 화면 아래로 다 빠진 뒤에 물러난다(물러나는 동안 선수가 작아지며 구석으로 가 보이지 않게).
            bool far = SpaceFighterAway || (Screen == FrontendScreen.Waiting && spaceExit == SpaceExit.None);
            float target = far ? 0f : 1f;
            if (!closeShown) { closeShown = true; closeWeight = target; closeVelocity = 0; }
            closeWeight = Mathf.SmoothDamp(closeWeight, target, ref closeVelocity, CloseSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(closeWeight - target) < .001f) { closeWeight = target; closeVelocity = 0; }
            var feet = w.Arena.SpawnFoot(0);
            closeCenter = new Vector2(w.State.Fighter0.Position.X + CloseSideFromCenter * CloseSize, feet.Y + CloseFeetFromCenter * CloseSize);
            rig.SetCloseFraming(closeCenter, CloseSize, Smooth(closeWeight));
        }

        Vector2 closeCenter;
        float moteShown = 1f;

        /// <summary>
        /// 메인 화면 공간이 아니면 원래 구도로 되돌린다. 같은 로비 장면으로 이어 간 경우(방 만들기)는 남은 근접 구도를 부드럽게 풀고,
        /// 그 밖(연습·뒤로: 장면이 비어 있음)은 바로 되돌린다. 숨겨 둔 반짝이는 입자도 서서히 돌아온다.
        /// </summary>
        void ResetCloseFraming(bool ease)
        {
            if (moteShown < 1f)
            {
                moteShown = Mathf.MoveTowards(moteShown, 1f, Time.unscaledDeltaTime / .8f);
                motionPresenter.Background.SetMoteVisibility(moteShown);
            }
            if (!closeShown) return;
            var rig = motionPresenter.CameraRig;
            if (ease && closeWeight > .001f)
            {
                closeWeight = Mathf.SmoothDamp(closeWeight, 0f, ref closeVelocity, CloseSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
                if (rig != null) rig.SetCloseFraming(closeCenter, CloseSize, Smooth(closeWeight));
                return;
            }
            closeShown = false; closeWeight = 0; closeVelocity = 0;
            if (rig != null) rig.SetCloseFraming(Vector2.zero, 1f, 0f);
        }

        /// <summary>
        /// 로비에 떨어지는 선수(낙하 등장 중)를 화면 위끝 밖에서 떨어지는 것처럼 그린다(2026-11-21 2차: 방 만들기 뒤 천장 위에서 떨어짐).
        /// 실제 낙하 높이(ResultsDropHeight)에서 남은 높이 비율만큼 위로 올려 그려, 착지 순간(먼지·소리)은 판정과 같다. 그림만 바꾼다.
        /// </summary>
        void LiftArrivals(SimWorld w, int absent)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float top = cam.transform.position.y + cam.orthographicSize;
            float half = w.Config.HalfBody;
            float drop = Mathf.Max(.5f, w.Config.Match.ResultsDropHeight);
            bool lifted = false;
            for (int k = 0; k < 2; k++)
            {
                if (k == absent) continue;
                var f = w.State.GetFighter(k);
                if (!f.Arriving || f.Grounded) continue;
                float land = w.Arena.SpawnFoot(k).Y + half;
                float extra = top + half * 3f - (land + drop);
                if (extra <= 0f) continue;
                float left = Mathf.Clamp01((f.Position.Y - land) / drop);
                motionPresenter.OffsetFighterVisual(k, extra * left);
                lifted = true;
            }
            if (lifted) motionPresenter.RefreshFighterShadows();
        }

        /// <summary>
        /// 메인 화면 공간의 월드 표시(MatchPresenter의 LateUpdate에서). 들어올 때는 로비 입장과 같은 바닥 재구성·조명 켜짐·낙하를 쓰고
        /// (다시 떨어뜨린 월드는 장면을 이어 받아 선수만 떨어진다), 나갈 때는 지금 보이는 장면에서 이어서 연출한다.
        /// </summary>
        void PresentMenuSpace(SimWorld w, float tick)
        {
            menuColorBlend = Mathf.MoveTowards(menuColorBlend, MenuPreviewSlot, Time.unscaledDeltaTime / .28f);
            motionPresenter.MenuPreviewColor = MenuPlayerColor;
            motionPresenter.Fighter(0).SetMenuTint(MenuPlayerColor);
            var match = w.Config.Match;
            points.Root.gameObject.SetActive(false);
            startCue.gameObject.SetActive(false);
            UpdateCloseFraming(w);
            bool continued = motionRunner.LobbySceneContinued;
            float sceneTick = continued ? tick + ContinuedSceneTicks : tick;
            float build = Range(sceneTick, 0, MenuBuildTicks);
            float spawn = continued && motionRunner.ContinuedLobbySlot == 0 ? 1 : Pop(Range(tick, match.ResultsDropTick, match.ResultsDropTick + 12));
            if (SpaceFighterAway) spawn = 0;
            // 반짝이는 입자는 이 화면에서 쓰지 않는다(로비로 넘어가면 서서히 돌아온다).
            moteShown = 0;
            motionPresenter.Background.SetMoteVisibility(0);
            if (spaceExit == SpaceExit.None || spaceExit == SpaceExit.Room || spaceExit == SpaceExit.Join)
            {
                if (spaceExit == SpaceExit.None) { spaceBuild = build; spaceSpawn = spawn; }
                // 배경은 단색에서 천천히 바뀌고 조명도 번쩍이지 않고 서서히 켜진다(2026-11-21 2차).
                spaceSceneOpacity = Smooth(Range(sceneTick, 0, MenuBuildTicks * 1.3f));
                float light = Smooth(Range(sceneTick, MenuBuildTicks * .3f, MenuBuildTicks * 1.4f));
                float fighter = spaceExit == SpaceExit.None ? spawn : spaceExitFighter ? 1 : 0;
                // 조준선·경계 표시는 띄우지 않는다(메뉴 동안 조작은 잠겨 있다).
                motionPresenter.PresentFrontendStage(build, true, Out(build), fighter, 0, 0, false, light, false, false);
                motionPresenter.Background.SetRoundLightPulse(-1);
                if (spaceExit == SpaceExit.None) LiftArrivals(w, motionRunner.AbsentSlot);
                float exitAge = Time.unscaledTime - spaceExitAt;
                motionPresenter.LobbyMarkerFade = spaceExit == SpaceExit.None ? 1f : 1 - Out(Range(exitAge, 0, .12f));
                // 방 만들기·참가: 처음 메인 화면에서 로비로 넘어가던 것처럼 한 번 튀어 올랐다가 아래로 떨어져 나간다(장면은 그대로).
                if (spaceExit != SpaceExit.None) JumpOut(w, exitAge * 60f);
                return;
            }
            float age = Time.unscaledTime - spaceExitAt;
            motionPresenter.LobbyMarkerFade = 1 - Out(Range(age, 0, .12f));
            float shown = spaceExitFighter ? 1 : 0;
            float leave;
            if (spaceExit == SpaceExit.Back)
            {
                leave = Range(age, BackSceneStart, BackSceneEnd);
                LeaveSpaceScene(leave, shown);
                HopOut(w, age);
                return;
            }
            // 연습: 로비 → 첫 경기 전환과 같은 박자(눌림 → 도약 → 아래로 빠짐, 42틱부터 장면이 빠짐) 뒤 연습 경기의 재구성이 이어진다.
            float exitTick = age * 60f;
            leave = Range(exitTick, LobbySceneLeaveStart, match.LobbyExitTicks);
            LeaveSpaceScene(leave, shown);
            JumpOut(w, exitTick);
        }

        /// <summary>눌림 → 튀어 오름 → 아래로 가속해 화면 밖으로(로비 퇴장과 같은 박자, 근접 구도에 맞춘 크기). 뛰는 순간 기존 점프 먼지·소리.</summary>
        void JumpOut(SimWorld w, float exitTick)
        {
            if (!spaceExitFighter) return;
            PoseExitJump(w, exitTick, 1, spaceExitScale);
            if (exitTick < ExitSquashEnd || spaceJumpPlayed) return;
            spaceJumpPlayed = true;
            var f = w.State.Fighter0;
            motionPresenter.PresentDisplayEvent(new SimEvent(w.State.Tick, SimEventType.Jump, 0, f.Position, new Vec2(0, 12f), 0f));
        }

        void LeaveSpaceScene(float leave, float fighter)
        {
            if (spaceExitBuild >= 1)
            {
                spaceSceneOpacity = 1 - Smooth(leave);
                motionPresenter.PresentFrontendStage(leave, false, spaceSceneOpacity, fighter, 0, 0, false,
                    RoundLight(MatchPhase.KO, true, true, leave, false), false, false);
            }
            else
            {
                float b = spaceExitBuild * (1 - Smooth(leave));
                spaceSceneOpacity = Out(b);
                motionPresenter.PresentFrontendStage(b, true, Out(b), fighter, 0, 0, false, 0f, false, false);
            }
            motionPresenter.Background.SetRoundLightPulse(-1);
        }

        /// <summary>뒤로: 선수가 짧게 눌렸다가 작게 뛰어 왼쪽 화면 밖으로 나간다(그림만, 판정 위치는 그대로).</summary>
        void HopOut(SimWorld w, float age)
        {
            if (!spaceExitFighter) return;
            var f = w.State.Fighter0;
            float half = w.Config.HalfBody;
            var cam = Camera.main;
            float left = cam != null ? cam.transform.position.x - cam.orthographicSize * cam.aspect : -20f;
            float distance = Mathf.Max(1f, f.Position.X - (left - 2f * spaceExitScale));
            float t = Range(age, HopSquash, HopSquash + HopSeconds);
            float squash = Range(age, 0, HopSquash);
            float stretch = age < HopSquash ? -.14f * Out(squash) : .1f * (1 - t);
            // 오른쪽을 보고 있으면 뛰는 순간 왼쪽을 보게 뒤집는다.
            float face = age >= HopSquash && f.Facing > 0 ? -1f : 1f;
            motionPresenter.ScaleFighterVisual(0, face * (1 - stretch * .7f), 1 + stretch, half);
            float x = -distance * Mathf.Lerp(t, t * t, .35f);
            float y = 4f * HopHeight * spaceExitScale * t * (1 - t);
            motionPresenter.OffsetFighterVisual(0, new Vector2(x, y));
            if (age < HopSquash) return;
            motionPresenter.HideFighterShadows();
            if (spaceJumpPlayed) return;
            spaceJumpPlayed = true;
            motionPresenter.PresentDisplayEvent(new SimEvent(w.State.Tick, SimEventType.Jump, 0, f.Position, new Vec2(-8f, 10f), 0f));
        }

        // ── 작은 "[Esc] 뒤로" ───────────────────────────────────────────────────────────

        Button escBack;
        Text escBackLabel;
        float escBackShown;
        bool escBackHovered;

        // ── 머리 위 닉네임 고치기 ─────────────────────────────────────────────────────

        /// <summary>지금 닉네임(정리된 값, 비어 있으면 자리 이름). FrontendController가 넣는다.</summary>
        public string Nickname { get; set; } = "";
        RectTransform nameHitRect, namePencil;
        Button nameHit;
        InputField nameField;
        Text nameFieldText, namePlaceholder;
        Outline nameFieldOutline;
        Image[] pencilParts;
        bool editingName, nameHovered;
        int nameEditEndedFrame = -10, nameCommitFrame;
        float pencilShown;

        /// <summary>이름을 고치는 중이거나 방금 끝냈다(같은 프레임의 Esc·Enter를 메뉴가 다시 쓰지 않게).</summary>
        public bool NameInputBusy => editingName || Time.frameCount - nameEditEndedFrame <= 1;

        /// <summary>메인 화면 전용 표시(뒤로 버튼·이름 고치기)를 만든다. 절차식·프리팹 화면 모두 시작할 때 한 번 부른다.</summary>
        void BuildMenuSpaceUi()
        {
            if (escBack != null) return;
            var back = Image(stage, "Menu_esc_back", new Rect(44, 946, 220, 56), PaletteAsset.Transparent);
            back.raycastTarget = true;
            escBack = back.gameObject.AddComponent<Button>();
            escBack.transition = Selectable.Transition.None;
            // 이름을 고치던 중에 누르면 바깥 누름으로 이름이 확정된 뒤 뒤로 간다(Esc 키만 고치기 취소로 쓴다).
            escBack.onClick.AddListener(() => { if (Screen == FrontendScreen.Main && StartMenuOpen) SetMenuPage(false); });
            var backHover = back.gameObject.AddComponent<FrontendHover>();
            backHover.Hover = _ => escBackHovered = true; backHover.Leave = () => escBackHovered = false;
            escBackLabel = Text(back.transform, "Label", new Rect(0, 0, 220, 56), "[Esc] 뒤로", Theme.textFont, 26, Theme.white, TextAnchor.MiddleLeft);
            ReadableOnDivider(escBackLabel, 2);
            back.gameObject.SetActive(false);

            // 이름 누를 자리: 머리 위 이름(월드 글자)에 겹친 투명 버튼과 오른쪽의 작은 연필 표시.
            var hit = Image(stage, "Main_nickname_hit", new Rect(0, 0, 10, 10), PaletteAsset.Transparent);
            hit.raycastTarget = true;
            nameHitRect = Centered(hit.rectTransform);
            nameHit = hit.gameObject.AddComponent<Button>();
            nameHit.transition = Selectable.Transition.None;
            nameHit.onClick.AddListener(BeginNameEdit);
            var hover = hit.gameObject.AddComponent<FrontendHover>();
            hover.Hover = _ => nameHovered = true; hover.Leave = () => nameHovered = false;
            namePencil = Centered(Rect(stage, "Main_nickname_pencil", 0, 0, 22, 22));
            var shapes = motionPresenter != null ? motionPresenter.Shapes : null;
            var body = Image(namePencil, "Body", new Rect(0, 0, 6, 15), Theme.white);
            Centered(body.rectTransform).anchoredPosition = new Vector2(0, 2.5f);
            var tip = Image(namePencil, "Tip", new Rect(0, 0, 6, 5), Theme.white, shapes != null ? shapes.Triangle : null);
            Centered(tip.rectTransform).anchoredPosition = new Vector2(0, -7.5f);
            tip.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            namePencil.localRotation = Quaternion.Euler(0, 0, -40);
            pencilParts = new[] { body, tip };
            namePencil.gameObject.SetActive(false);
            hit.gameObject.SetActive(false);

            // 그 자리 입력칸: 머리 위 이름과 같은 글꼴·색·외곽선. Enter·바깥 누름은 확정, Esc는 고치기만 취소.
            var field = Image(stage, "Main_nickname_input", new Rect(0, 0, 420, 80), PaletteAsset.Transparent);
            field.raycastTarget = true;
            Centered(field.rectTransform);
            nameField = field.gameObject.AddComponent<NicknameInputField>(); // 한글 조합 + 선택 예외를 막은 입력칸
            nameFieldText = Text(field.transform, "Value", new Rect(0, 0, 420, 80), "", Theme.titleFont, 30, Theme.white);
            Stretch(nameFieldText.rectTransform);
            nameFieldText.supportRichText = false;
            nameFieldText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameFieldOutline = nameFieldText.gameObject.AddComponent<Outline>();
            nameFieldOutline.effectColor = PaletteAsset.MonolithDefaults.LabelOutline;
            namePlaceholder = Text(field.transform, "Placeholder", new Rect(0, 0, 420, 80), "닉네임", Theme.titleFont, 30, PaletteAsset.WithAlpha(Theme.white, .35f));
            Stretch(namePlaceholder.rectTransform);
            nameField.textComponent = nameFieldText;
            nameField.placeholder = namePlaceholder;
            nameField.characterLimit = GameSettings.NicknameMaxLength;
            nameField.lineType = InputField.LineType.SingleLine;
            nameField.contentType = InputField.ContentType.Standard;
            nameField.customCaretColor = true;
            nameField.caretColor = Theme.white;
            nameField.caretWidth = 3;
            nameField.onEndEdit.AddListener(EndNameEdit);
            field.gameObject.SetActive(false);
        }

        static RectTransform Centered(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        bool CanEditName => (Screen == FrontendScreen.Join || Screen == FrontendScreen.Main && StartMenuOpen) && MenuSpaceActive && spaceExit == SpaceExit.None && !SpaceFighterAway
            && motionRunner != null && motionRunner.World != null && motionRunner.World.InLobby && motionRunner.World.State.Fighter0.Grounded;

        void BeginNameEdit()
        {
            if (editingName || !CanEditName || nameField == null) return;
            editingName = true;
            nameCommitFrame = 0;
            if (CodeInput.isFocused) CodeInput.DeactivateInputField();
            nameField.gameObject.SetActive(true);
            nameField.text = Nickname ?? "";
            namePlaceholder.text = "P" + (MenuPreviewSlot + 1);
            motionPresenter.HiddenLobbyName = localSlot;
            PlaceNameEditor();
            nameField.Select();
            nameField.ActivateInputField();
        }

        /// <summary>입력칸의 확정·취소(Enter, 바깥 누름, Esc). 취소면 그대로 두고, 확정이면 정리는 받는 쪽에서 한다.</summary>
        void EndNameEdit(string value)
        {
            if (!editingName) return;
            editingName = false;
            nameEditEndedFrame = Time.frameCount;
            motionPresenter.HiddenLobbyName = -1;
            if (nameField.wasCanceled) return;
            // 조합 중인 한글이 남은 채 Enter가 먼저 왔으면 그 글자까지 확정한다.
            string composing = Input.compositionString;
            NicknameEdited?.Invoke(string.IsNullOrEmpty(composing) ? value : value + composing);
        }

        /// <summary>쪽이 바뀌거나 공간이 닫히면 고치던 이름은 버린다(저장하지 않음).</summary>
        void CancelNameEdit()
        {
            if (!editingName) return;
            editingName = false;
            nameEditEndedFrame = Time.frameCount;
            if (motionPresenter != null) motionPresenter.HiddenLobbyName = -1;
            if (EventSystem.current != null && nameField != null && EventSystem.current.currentSelectedGameObject == nameField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// 매 프레임(MatchPresenter가 머리 위 표시를 그린 뒤): 머리 위 이름 자리를 화면 좌표로 옮겨 누를 자리·연필·입력칸을 겹친다.
        /// 뒤로 버튼의 보임도 여기서 정한다.
        /// </summary>
        void PlaceNameEditor()
        {
            if (nameField == null) return;
            float dt = Time.unscaledDeltaTime;
            // [Esc] 뒤로: 게임 시작 메뉴에서만(코드 입력·방 화면은 패널의 돌아가기가 맡는다).
            bool backOn = Screen == FrontendScreen.Main && StartMenuOpen && spaceExit != SpaceExit.Practice;
            escBackShown = Mathf.MoveTowards(escBackShown, backOn ? 1 : 0, dt / .18f);
            escBack.gameObject.SetActive(escBackShown > .001f);
            escBack.interactable = backOn;
            float backAlpha = Out(escBackShown) * (escBackHovered && backOn ? 1f : .72f);
            escBackLabel.color = PaletteAsset.WithAlpha(Theme.white, backAlpha);
            escBackLabel.rectTransform.anchoredPosition = new Vector2(-14 * (1 - Out(escBackShown)), 0);

            bool editable = CanEditName;
            Vector3 center = default; Vector2 size = default; float fontWorld = 0;
            bool placed = (editable || editingName) && motionPresenter.TryGetLobbyName(localSlot, out center, out size, out fontWorld);
            if (editingName && !placed) CancelNameEdit();
            bool pencilOn = editable && placed && !editingName;
            pencilShown = Mathf.MoveTowards(pencilShown, pencilOn ? 1 : 0, dt / (pencilOn ? .2f : .08f));
            bool hitOn = editable && placed && !editingName;
            nameHit.gameObject.SetActive(hitOn);
            namePencil.gameObject.SetActive(pencilShown > .001f);
            if (nameField.gameObject.activeSelf != editingName) nameField.gameObject.SetActive(editingName);
            if (!placed) { nameHovered = false; return; }

            var cam = Camera.main;
            if (cam == null) return;
            Vector2 middle = ToStage(cam, center);
            float unit = Mathf.Abs(ToStage(cam, center + Vector3.right).x - middle.x);
            float textWidth = Mathf.Max(size.x * unit, 24f);
            float fontPx = Mathf.Max(12f, fontWorld * unit);
            const float pencilGap = 14f, pencilSize = 22f;
            nameHitRect.anchoredPosition = middle + new Vector2((pencilGap + pencilSize) * .5f, 0);
            nameHitRect.sizeDelta = new Vector2(textWidth + pencilGap + pencilSize + 24f, fontPx * 1.5f);
            float hoverPop = nameHovered && hitOn ? 1.12f : 1f;
            namePencil.anchoredPosition = middle + new Vector2(textWidth * .5f + pencilGap + pencilSize * .5f, fontPx * .05f);
            namePencil.localScale = Vector3.one * (fontPx / 30f) * Mathf.Lerp(.6f, 1f, Out(pencilShown)) * hoverPop;
            var pencilColor = PaletteAsset.WithAlpha(Theme.white, Out(pencilShown) * (nameHovered && hitOn ? 1f : .55f));
            foreach (var part in pencilParts) part.color = pencilColor;
            if (!editingName) return;
            CommitOnEnter();
            if (!editingName) return;
            var rect = (RectTransform)nameField.transform;
            int fontSize = Mathf.RoundToInt(fontPx);
            // 최대 글자 수(한글 한 글자 ≈ 글꼴 크기)가 다 들어가는 고정 폭: 입력 중 칸이 밀려 앞 글자가 잘리거나 칸 크기가 흔들리지 않는다.
            rect.anchoredPosition = middle;
            rect.sizeDelta = new Vector2(fontPx * (GameSettings.NicknameMaxLength + 2), fontPx * 1.6f);
            nameFieldText.fontSize = namePlaceholder.fontSize = fontSize;
            nameFieldText.color = MenuPlayerColor;
            nameField.selectionColor = PaletteAsset.WithAlpha(MenuPlayerColor, .35f);
            float outline = 3f * fontPx / 54f; // 머리 위 글자(54)의 외곽선 3과 같은 비율
            nameFieldOutline.effectDistance = new Vector2(outline, -outline);
        }

        Color menuBackgroundColor;
        bool menuBackgroundColorKnown;

        /// <summary>
        /// 메인 메뉴 화면의 단색 배경: 공간이 열려 있으면 장면이 보이는 만큼 걷고(덮개는 끈다), 아니면 그대로 덮는다.
        /// 같은 색 덮개(matte)는 공간이 열린 동안 끈다.
        /// </summary>
        void ApplyMenuBackdrop(bool menuScreen)
        {
            bool space = menuScreen && MenuSpaceActive;
            // 공간이 없을 때만 보임 값을 지운다(공간 연출이 바뀌는 프레임에 단색 배경이 한 번 덮이지 않게).
            if (!MenuSpaceActive) spaceSceneOpacity = 0;
            matte.enabled = !space;
            if (menuBackground == null) menuBackground = menu.Find("Background")?.GetComponent<Image>();
            if (menuBackground == null) return;
            if (!menuBackgroundColorKnown) { menuBackgroundColor = menuBackground.color; menuBackgroundColorKnown = true; }
            menuBackground.enabled = true;
            menuBackground.color = PaletteAsset.WithAlpha(menuBackgroundColor, menuBackgroundColor.a * (space ? 1 - spaceSceneOpacity : 1));
        }

        /// <summary>
        /// 한글을 조합하는 중에 누른 Enter는 IME가 글자 확정에 써서 입력칸에 전달되지 않을 수 있다. 실제 키 누름(Input System)을 보고
        /// 다음 프레임(조합 글자가 들어온 뒤)에 확정해 Enter 한 번으로 끝나게 한다. 조합이 없으면 입력칸이 먼저 확정해 아무 일도 없다.
        /// </summary>
        void CommitOnEnter()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelNameEdit();
                nameField.DeactivateInputField();
                return;
            }
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                nameCommitFrame = Time.frameCount + 1;
                return;
            }
            if (nameCommitFrame == 0 || Time.frameCount < nameCommitFrame) return;
            nameCommitFrame = 0;
            if (nameField.isFocused) nameField.DeactivateInputField();
        }

        Vector2 ToStage(Camera cam, Vector3 world)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, cam.WorldToScreenPoint(world), null, out var local);
            return local;
        }
    }
}
