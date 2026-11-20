using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 로비의 머리 위 표시(2026-11-19): 선수 색 "P1"/"P2" 글자와 아래를 가리키는 작은 삼각형. 모놀리스 카드 선택의 "P1 선택 중" 표시
    /// (CardPickBanner)와 같은 글꼴·외곽선·삼각형 모양을 쓴다. 준비 전에는 삼각형 속이 비어 있고, 준비하면 한 번 톡 차오른다.
    /// 판정과 무관한 화면 전용 표시이며, 결과 공간의 준비 고리(ReadyMarkers)는 그대로 둔다.
    /// </summary>
    public sealed class LobbyHeadMarkers
    {
        const float CanvasUnitsPerWorld = 100f;
        const float HeadGap = .42f;
        const float PopSeconds = .26f;
        const float BobHeight = .05f, BobSeconds = 1.6f;
        static readonly Vector2 PointerSize = new Vector2(40, 30);

        readonly Transform[] roots = new Transform[2];
        readonly Text[] labels = new Text[2];
        // 2026-11-19 2차: 이름 위의 준비 상태 줄("[R] 준비"·"준비 전"·"준비 완료"). 눈이 가는 캐릭터 위에서 바로 읽히게 한다.
        readonly Text[] statuses = new Text[2];
        readonly RectTransform[] pointers = new RectTransform[2];
        readonly Image[] holes = new Image[2];
        readonly RectTransform[] fills = new RectTransform[2];
        readonly CanvasGroup[] groups = new CanvasGroup[2];
        readonly float[] changedAt = { -10f, -10f };
        readonly bool[] ready = new bool[2];
        readonly bool[] shown = new bool[2];

        public LobbyHeadMarkers(Transform parent, ShapeLibrary shapes, PaletteAsset palette, FrontendTheme theme)
        {
            for (int p = 0; p < 2; p++)
            {
                var color = palette.FighterBody(p);
                var root = new GameObject("Lobby_head_marker_P" + (p + 1), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
                root.transform.SetParent(parent, false);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingLayerName = SortingLayers.Fx;
                canvas.sortingOrder = 12;
                root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(300, 160);
                rect.localScale = Vector3.one / CanvasUnitsPerWorld;
                roots[p] = root.transform;
                groups[p] = root.GetComponent<CanvasGroup>();
                groups[p].interactable = groups[p].blocksRaycasts = false;
                labels[p] = Label(root.transform, theme != null ? theme.titleFont : null, "P" + (p + 1), color);
                statuses[p] = Label(root.transform, theme != null ? theme.boldFont : null, "", PaletteAsset.Neutral);
                statuses[p].fontSize = 46;
                Place(statuses[p].rectTransform, new Vector2(0, 112), new Vector2(360, 60));
                var pointer = new GameObject("Pointer", typeof(RectTransform)).GetComponent<RectTransform>();
                pointer.SetParent(root.transform, false);
                Place(pointer, new Vector2(0, -6), PointerSize);
                pointer.localRotation = Quaternion.Euler(0, 0, 180);
                pointers[p] = pointer;
                Triangle(pointer, "Edge", shapes.Triangle, color, Vector2.zero, PointerSize, true);
                // 속이 빈 삼각형: 같은 모양의 어두운 속을 안쪽에 둔다. 준비하면 선수 색 채움이 그 위로 차오른다.
                var inner = PointerSize * .52f;
                var innerAt = new Vector2(0, -PointerSize.y * .08f);
                holes[p] = Triangle(pointer, "Hollow", shapes.Triangle, PaletteAsset.MonolithDefaults.LabelOutline, innerAt, inner, false);
                fills[p] = Triangle(pointer, "Fill", shapes.Triangle, color, innerAt, inner, false).rectTransform;
                fills[p].localScale = Vector3.zero;
                root.SetActive(false);
            }
        }

        /// <param name="headTop">선수 몸 위쪽 끝(월드 좌표).</param>
        /// <param name="fade">0이면 보이지 않는다(로비 퇴장 시작에 빠르게 사라진다).</param>
        /// <param name="status">이름 위의 준비 상태 줄. 비우면 숨긴다.</param>
        public void Render(bool visible, int fighter, Vector2 headTop, bool isReady, float fade, float now, string status = "", Color statusColor = default)
        {
            var root = roots[fighter];
            bool on = visible && fade > .001f;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on)
            {
                shown[fighter] = false;
                return;
            }
            if (!shown[fighter])
            {
                // 다시 보일 때(재합류·재등장)는 지금 상태로 바로 시작한다. 이전 준비의 톡 연출을 다시 틀지 않는다.
                shown[fighter] = true;
                ready[fighter] = isReady;
                changedAt[fighter] = -10f;
            }
            else if (isReady != ready[fighter])
            {
                ready[fighter] = isReady;
                changedAt[fighter] = now;
            }
            float t = Mathf.Clamp01((now - changedAt[fighter]) / PopSeconds);
            float fill = isReady ? FrontendMotion.OutBack(t, 2.4f) : 1f - FrontendMotion.Out(t);
            float pop = 1f + (isReady ? .32f : .1f) * Mathf.Sin(t * Mathf.PI);
            float bob = Mathf.Sin(now * Mathf.PI * 2f / BobSeconds + fighter * 1.7f) * BobHeight;
            root.position = new Vector3(headTop.x, headTop.y + HeadGap + bob, root.position.z);
            groups[fighter].alpha = fade;
            pointers[fighter].localScale = Vector3.one * pop;
            var line = statuses[fighter];
            line.text = status ?? "";
            line.color = statusColor;
            line.rectTransform.localScale = Vector3.one * (1f + (isReady ? .18f : .06f) * Mathf.Sin(t * Mathf.PI));
            line.rectTransform.anchoredPosition = new Vector2(0, 112);
            labels[fighter].rectTransform.anchoredPosition = new Vector2(0, 46);
            fills[fighter].localScale = Vector3.one * Mathf.Max(0f, fill);
            holes[fighter].enabled = fill < .98f;
        }

        public void Hide()
        {
            for (int p = 0; p < 2; p++) Render(false, p, Vector2.zero, false, 0, 0);
        }

        static Text Label(Transform parent, Font font, string value, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 54;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = value;
            text.color = color;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = PaletteAsset.MonolithDefaults.LabelOutline;
            outline.effectDistance = new Vector2(3, -3);
            Place(text.rectTransform, new Vector2(0, 46), new Vector2(240, 70));
            return text;
        }

        static Image Triangle(Transform parent, string name, Sprite sprite, Color color, Vector2 at, Vector2 size, bool outline)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (outline)
            {
                var edge = go.AddComponent<Outline>();
                edge.effectColor = PaletteAsset.MonolithDefaults.LabelOutline;
                edge.effectDistance = new Vector2(3, -3);
            }
            Place(image.rectTransform, at, size);
            return image;
        }

        static void Place(RectTransform rect, Vector2 center, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
        }
    }
}
