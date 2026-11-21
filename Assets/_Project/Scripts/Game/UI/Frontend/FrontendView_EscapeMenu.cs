using System;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;

namespace Knockbox.Game.UI
{
    // 기존 Frontend의 로컬 메뉴. 네트워크 상태·시간은 변경하지 않는다.
    public sealed partial class FrontendView
    {
        public event Action Surrender, LeaveMatch;
        public bool EscapeMenuOpen { get; private set; }
        public bool EscapeInputBlocked => EscapeMenuOpen || Time.frameCount - escapeClosedFrame <= 1;
        int escapeClosedFrame = -10, escapeConfirm, escapeHovered = -1;
        float escapeFade, escapeOpenedAt;
        RectTransform escapeRoot;
        CanvasGroup escapeGroup;
        Button[] escapeButtons;
        Text escapeWarning, matchNotice;
        Image escapeBlocker;
        SpriteRenderer escapeDim;
        readonly SortingGroup[] escapeFighters = new SortingGroup[2];
        Vector2 escapePosition;
        bool escapePlaced, escapeLeft;

        public void SetEscapeMenu(bool open)
        {
            EnsureEscapeMenu();
            if (EscapeMenuOpen == open) return;
            EscapeMenuOpen = open;
            escapeConfirm = 0; escapeHovered = -1;
            if (open) { escapeOpenedAt = Time.unscaledTime; escapePlaced = false; }
            else escapeClosedFrame = Time.frameCount;
        }

        public bool PointerOverEscapeMenu() => EscapeInputBlocked || escapeHovered >= 0;

        void EnsureEscapeMenu()
        {
            if (escapeRoot != null) return;
            escapeBlocker = Image(stage, "Escape_input_blocker", new Rect(0, 0, 1920, 1080), PaletteAsset.Transparent);
            escapeBlocker.raycastTarget = true;
            escapeRoot = Centered(Rect(stage, "Escape_menu", 0, 0, 350, 210));
            escapeRoot.pivot = new Vector2(0, 1);
            escapeGroup = escapeRoot.gameObject.AddComponent<CanvasGroup>();
            escapeButtons = new Button[3];
            string[] labels = { "계속 [Esc]", "항복", "메인으로 나가기" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var button = FrontendWidgets.Button(escapeRoot, "Action_" + i, new Rect(0, i * 52, 350, 48),
                    labels[i], Theme, PaletteAsset.Transparent, Theme.white, () => PressEscapeItem(index), 27);
                button.transition = Selectable.Transition.None;
                button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                var hover = button.gameObject.AddComponent<FrontendHover>();
                hover.Hover = _ => escapeHovered = index;
                hover.Leave = () => { if (escapeHovered == index) escapeHovered = -1; };
                escapeButtons[i] = button;
            }
            escapeWarning = Text(escapeRoot, "Confirmation", new Rect(0, -44, 350, 40), "", Theme.textFont, 19, Theme.white, TextAnchor.MiddleLeft);
            matchNotice = Text(stage, "Match_end_notice", new Rect(510, 145, 900, 48), "", Theme.boldFont, 27, Theme.white);
            matchNotice.supportRichText = false;
            ReadableOnDivider(matchNotice, 2);
            escapeDim = ShapeLibrary.Create(motionPresenter.transform, "Escape_arena_dim", motionPresenter.Shapes.Square,
                SortingLayers.UI, 100, PaletteAsset.Transparent);
            for (int p = 0; p < 2; p++)
            {
                escapeFighters[p] = motionPresenter.Fighter(p).gameObject.AddComponent<SortingGroup>();
                escapeFighters[p].sortingLayerName = SortingLayers.UI;
                escapeFighters[p].sortingOrder = 101;
                escapeFighters[p].enabled = false;
            }
            escapeDim.gameObject.SetActive(false);
            escapeBlocker.gameObject.SetActive(false);
            escapeRoot.gameObject.SetActive(false);
            matchNotice.gameObject.SetActive(false);
        }

        void PressEscapeItem(int index)
        {
            if (!EscapeMenuOpen) return;
            if (index == 0) { SetEscapeMenu(false); return; }
            if (escapeConfirm != index) { escapeConfirm = index; return; }
            SetEscapeMenu(false);
            if (index == 1) Surrender?.Invoke(); else LeaveMatch?.Invoke();
        }

        // 캔버스 기준 위치. 우측 공간이 없으면 좌측, 최종 위치는 화면 안으로 제한한다.
        public static Vector2 EscapePosition(Vector2 head, Vector2 halfScreen, bool left, float width = 350, float height = 210)
        {
            float x = left ? head.x - width - 36 : head.x + 36;
            float y = head.y + 90;
            return new Vector2(Mathf.Clamp(x, -halfScreen.x + 24, halfScreen.x - width - 24),
                Mathf.Clamp(y, -halfScreen.y + height + 24, halfScreen.y - 68));
        }

        void PresentEscapeMenu()
        {
            if (motionRunner == null || motionRunner.FrontendWorld == null) return;
            var m = motionRunner.FrontendWorld.State.Match;
            if (escapeRoot == null && !m.Forfeited) return;
            EnsureEscapeMenu();
            bool showNotice = m.Forfeited && m.Phase == MatchPhase.KO && Screen == FrontendScreen.Hidden;
            matchNotice.gameObject.SetActive(showNotice);
            if (showNotice)
            {
                matchNotice.text = m.Departed ? "상대가 나갔습니다 · 기권승" : motionPresenter.PlayerName(m.ForfeitLoser) + " 항복";
                matchNotice.color = playerColors[m.RoundWinner];
            }
            escapeFade = Mathf.MoveTowards(escapeFade, EscapeMenuOpen ? 1 : 0, Time.unscaledDeltaTime / .14f);
            bool visible = escapeFade > .001f;
            escapeRoot.gameObject.SetActive(visible);
            escapeBlocker.gameObject.SetActive(EscapeInputBlocked);
            escapeGroup.alpha = FrontendMotion.Smooth(escapeFade);
            escapeGroup.interactable = escapeGroup.blocksRaycasts = EscapeMenuOpen;
            escapeDim.gameObject.SetActive(visible);
            for (int p = 0; p < 2; p++) escapeFighters[p].enabled = visible && p == localSlot;
            motionPresenter.Hud.SetMenuFocus(localSlot, visible);
            if (!visible) return;
            var cam = Camera.main;
            if (cam == null) return;
            escapeDim.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0);
            ShapeLibrary.SetSize(escapeDim, cam.orthographicSize * cam.aspect * 2.2f, cam.orthographicSize * 2.2f);
            escapeDim.color = PaletteAsset.WithAlpha(PaletteAsset.Black, .65f * FrontendMotion.Smooth(escapeFade));
            Vector2 head = ToStage(cam, motionPresenter.Fighter(localSlot).BodyCenter + Vector2.up * motionRunner.World.Config.HalfBody);
            var half = ((RectTransform)Canvas.transform).rect.size * .5f / stage.localScale.x;
            if (!escapePlaced || escapeHovered < 0)
            {
                escapeLeft = head.x + 386 > half.x - 24;
                escapePosition = EscapePosition(head, half, escapeLeft);
                escapePlaced = true;
            }
            float slide = 1 - FrontendMotion.Out(Mathf.Clamp01((Time.unscaledTime - escapeOpenedAt) / .18f));
            escapeRoot.anchoredPosition = escapePosition + Vector2.right * (escapeLeft ? 22 : -22) * slide;
            escapeWarning.text = escapeConfirm == 2 ? "지금 나가면 기권패로 처리됩니다" : escapeConfirm == 1 ? "이 경기를 항복할까요?" : "";
            for (int i = 0; i < 3; i++)
            {
                var button = escapeButtons[i];
                button.gameObject.SetActive(escapeConfirm == 0 || i == 0 || i == escapeConfirm);
                button.GetComponentInChildren<Text>().text = i == 0 ? "계속 [Esc]" : i == 1 ? (escapeConfirm == 1 ? "항복 확인" : "항복")
                    : escapeConfirm == 2 ? "나가기 확인" : "메인으로 나가기";
                button.GetComponentInChildren<Text>().color = i == escapeHovered ? Color.Lerp(Theme.white, playerColors[localSlot], .6f) : Theme.white;
            }
        }
    }
}
