using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 카드 선택 무대의 "P1 선택 중" 표시. 배경 판 없이 플레이어 색 글자와 패자를 가리키는 아래 화살표만 둔다.
    /// 배경에서 읽히도록 글자 외곽을 어둡게 살짝 두르고, 남은 초는 라벨 옆에 작은 글자로 둔다.
    /// 고르는 패자의 머리 위(프로필 쪽)에 서며 승자 화면에도 같은 자리에 보인다. 판정과 무관한 표시 전용 오브젝트다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardPickBanner : MonoBehaviour
    {
        public const float CanvasUnitsPerWorld = 100f;
        const int Order = 12;

        [SerializeField, Tooltip("P1이 고를 때 패자 발 위치에서 라벨 가운데까지(유닛). P2는 좌우를 뒤집는다.")]
        Vector2 offset = new Vector2(0f, 3.1f);
        [SerializeField, Range(.5f, 2f)] float scale = 1f;
        [SerializeField, Range(0f, 6f), Tooltip("글자 외곽선 두께(캔버스 단위)")] float outlineWidth = 3f;
        [Header("움직임")]
        [SerializeField, Range(0f, .4f), Tooltip("표시 전체가 위아래로 떠다니는 폭(유닛)")] float bobHeight = .12f;
        [SerializeField, Min(.2f), Tooltip("떠다니는 한 번의 시간(초)")] float bobSeconds = 1.5f;
        [SerializeField, Range(0f, 40f), Tooltip("화살표가 통통 튀는 높이(캔버스 단위)")] float pointerBounce = 14f;
        [Header("남은 시간")]
        [SerializeField, Range(40, 140), Tooltip("남은 초 글자 크기")] int timerFontSize = 80;
        [SerializeField, Range(0f, 1f), Tooltip("초가 바뀔 때 튀어 커지는 정도")] float tickPop = .4f;
        [SerializeField, Range(1, 10), Tooltip("이 초 이하부터 빨갛게 바뀌고 심장 박동처럼 두 번씩 뛰며 급함을 알린다")] int urgentSeconds = 5;
        [SerializeField, Range(0f, .6f), Tooltip("급할 때 초마다 뛰는 두 번째 박동의 크기. 1초에 가까울수록 커진다")] float heartbeat = .22f;
        [Header("부품(프리팹에 저장)")]
        [SerializeField] Canvas canvas;
        [SerializeField] Text label;
        [SerializeField] Text timer;
        [SerializeField] Image pointer;

        public bool Built => canvas != null && label != null && timer != null && pointer != null;
        public Text Label => label;
        public Text Timer => timer;
        public Color LabelColor => label.color;
        public bool TimerVisible => timer != null && timer.gameObject.activeSelf;
        public bool Visible => canvas != null && canvas.gameObject.activeSelf;
        public bool HasBackgroundPlate => GetComponentsInChildren<FrontendGraphic>(true).Length > 0;

        public void Build(ShapeLibrary shapes, FrontendTheme theme)
        {
            if (Built) { Layout(); return; }
            var canvasObject = new GameObject("Banner_canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingLayerName = SortingLayers.Fx;
            canvas.sortingOrder = Order;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            label = Text(canvas.transform, "Label", theme.titleFont, 84, TextAnchor.MiddleCenter);
            timer = Text(canvas.transform, "Seconds", theme.boldFont, 48, TextAnchor.MiddleLeft);
            pointer = Image(canvas.transform, "Pointer", shapes.Triangle);
            Layout();
            Hide();
        }

        [ContextMenu("크기 값으로 다시 배치")]
        public void Layout()
        {
            if (!Built) return;
            var root = (RectTransform)canvas.transform;
            root.localScale = Vector3.one * scale / CanvasUnitsPerWorld;
            root.sizeDelta = new Vector2(600, 260);
            Place(label.rectTransform, new Vector2(0, 40), new Vector2(600, 120));
            Place(pointer.rectTransform, new Vector2(0, -62), new Vector2(64, 46));
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            foreach (var outline in GetComponentsInChildren<Outline>(true))
                outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
            // 남은 초는 라벨과 같은 글꼴로, 라벨 옆에 크게(굵은 외곽선) 둔다.
            timer.font = label.font;
            timer.fontSize = timerFontSize;
            timer.alignment = TextAnchor.MiddleCenter;
            var timerOutline = timer.GetComponent<Outline>();
            if (timerOutline != null) timerOutline.effectDistance = new Vector2(outlineWidth + 1.5f, -outlineWidth - 1.5f);
        }

        /// <summary>남은 초가 급한 구간인지(0~1, 1초에 가까울수록 1).</summary>
        public float Urgency(int seconds) => seconds < 0 || seconds > urgentSeconds ? 0 : Mathf.Clamp01((urgentSeconds - seconds + 1f) / urgentSeconds);
        public Color TimerColor => timer.color;
        public Vector3 TimerScale => timer.rectTransform.localScale;

        /// <summary>패자 발 위치(로컬 유닛) 위에 표시한다. seconds가 음수면 남은 초를 숨긴다.</summary>
        public void Show(int picker, Vector2 foot, Color playerColor, string text, int seconds) => Show(picker, foot, playerColor, text, seconds, seconds, 0);

        /// <summary>
        /// 패자 발 위치(로컬 유닛) 위에 표시한다. remaining은 남은 시간(초, 소수), clock은 움직임용 시각(초)이다.
        /// 표시 전체가 천천히 떠다니고 화살표가 통통 튄다. 남은 초는 초가 바뀔 때마다 튀어 커졌다 돌아오고(감속 이징),
        /// 급한 구간(urgentSeconds 이하)에서는 초마다 흰색으로 번쩍였다 빨강이 되고, 심장 박동처럼 두 번 뛴다(자리는 흔들리지 않는다).
        /// 급할수록 떠다니는 박자와 화살표도 조금 빨라진다.
        /// </summary>
        public void Show(int picker, Vector2 foot, Color playerColor, string text, int seconds, float remaining, float clock)
        {
            if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
            float side = picker == 1 ? -1 : 1;
            float urgency = Urgency(seconds);
            // 급할수록 움직임 박자가 빨라진다(최대 1.6배). 시각을 늘려 위상이 끊기지 않게 한다.
            float pace = 1 + .6f * urgency * urgency;
            float wave = Mathf.Sin(clock * pace * Mathf.PI * 2 / bobSeconds);
            transform.localPosition = new Vector3(foot.x + side * offset.x, foot.y + offset.y + wave * bobHeight, 0);
            label.text = text;
            label.color = playerColor;
            pointer.color = playerColor;
            // 화살표: 아래로 튀었다 올라오는 통통 튐(사인 절댓값이라 바닥에서 반동이 생긴다).
            float bounce = Mathf.Abs(Mathf.Sin(clock * pace * Mathf.PI / (bobSeconds * .5f)));
            pointer.rectTransform.anchoredPosition = new Vector2(0, -62 - (1 - bounce) * pointerBounce);
            float fit = Mathf.Min(1, 560 / Mathf.Max(1, label.preferredWidth));
            // 라벨은 숨쉬듯 아주 살짝 커졌다 작아진다.
            float breathe = 1 + .03f * Mathf.Sin(clock * pace * Mathf.PI * 2 / bobSeconds + Mathf.PI * .5f);
            label.rectTransform.localScale = Vector3.one * fit * breathe;
            label.rectTransform.anchoredPosition = new Vector2(0, 40);
            timer.gameObject.SetActive(seconds >= 0);
            if (seconds < 0) return;
            timer.text = seconds.ToString();
            // 이번 초가 시작된 뒤 지난 시간(0~1초). 초가 바뀌는 순간 튀어 커졌다가 감속하며 돌아온다.
            float sinceTick = Mathf.Clamp01(Mathf.Ceil(remaining) - remaining);
            if (remaining <= 0) sinceTick = 1;
            float pop = 1 + tickPop * (1 + urgency * .5f) * (1 - FrontendMotion.Out(sinceTick / .32f));
            // 급할 때: 심장 박동처럼 첫 박동 뒤 0.2초에 한 번 더 작게 뛴다(감속 이징으로 줄어든다).
            if (urgency > 0) pop += heartbeat * urgency * Pulse(sinceTick - .2f, .28f);
            timer.rectTransform.localScale = Vector3.one * pop;
            // 급할 때: 초마다 흰색으로 번쩍였다가 빨강으로.
            var urgent = Color.Lerp(PaletteAsset.MonolithDefaults.NegativeFlash, PaletteAsset.MonolithDefaults.Negative, FrontendMotion.Out(sinceTick / .25f));
            timer.color = urgency > 0 ? urgent : playerColor;
            float half = label.preferredWidth * fit * .5f;
            Place(timer.rectTransform, new Vector2(half + 34 + 60, 40), new Vector2(140, 110));
        }

        /// <summary>t초 시점의 박동 하나: 빠르게 커졌다가(감속) 천천히 돌아온다. t가 0~length 밖이면 0.</summary>
        static float Pulse(float t, float length)
        {
            if (t <= 0 || t >= length) return 0;
            float u = t / length;
            return u < .25f ? FrontendMotion.OutQuad(u / .25f) : 1 - FrontendMotion.Smooth((u - .25f) / .75f);
        }

        public void Hide()
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
        }

        static void Place(RectTransform rect, Vector2 center, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
        }

        static Image Image(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(SceneObjectName.Normalize(name), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = PaletteAsset.Neutral;
            image.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = PaletteAsset.MonolithDefaults.LabelOutline;
            return image;
        }

        static Text Text(Transform parent, string name, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(SceneObjectName.Normalize(name), typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = PaletteAsset.Neutral;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = PaletteAsset.MonolithDefaults.LabelOutline;
            return text;
        }
    }
}
