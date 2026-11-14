using System;
using Knockbox.Game.Cards;
using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>모놀리스(비석) 색. 프리팹에서 바꾸면 세 비석에 함께 적용된다.</summary>
    [Serializable]
    public struct MonolithStyle
    {
        public Color body, bodyLit, rim, rimLit, face, faceLit, iconFill, stripe, icon, iconOnFill;
        public Color nameBand, nameText, nameLit, panel, text, dimText, silhouette, silhouetteRim, silhouetteText;

        public static MonolithStyle Default => new MonolithStyle
        {
            body = PaletteAsset.MonolithDefaults.Body, bodyLit = PaletteAsset.MonolithDefaults.BodyLit,
            rim = PaletteAsset.MonolithDefaults.Rim, rimLit = PaletteAsset.MonolithDefaults.RimLit,
            face = PaletteAsset.MonolithDefaults.Face, faceLit = PaletteAsset.MonolithDefaults.FaceLit,
            iconFill = PaletteAsset.MonolithDefaults.IconFill, stripe = PaletteAsset.MonolithDefaults.Stripe,
            icon = PaletteAsset.MonolithDefaults.Icon, iconOnFill = PaletteAsset.MonolithDefaults.IconOnFill,
            nameBand = PaletteAsset.MonolithDefaults.NameBand, nameText = PaletteAsset.MonolithDefaults.NameText,
            nameLit = PaletteAsset.MonolithDefaults.NameLit, panel = PaletteAsset.MonolithDefaults.Panel,
            text = PaletteAsset.MonolithDefaults.NeutralText, dimText = PaletteAsset.MonolithDefaults.DimText,
            silhouette = PaletteAsset.MonolithDefaults.Silhouette, silhouetteRim = PaletteAsset.MonolithDefaults.SilhouetteRim,
            silhouetteText = PaletteAsset.MonolithDefaults.SilhouetteText,
        };
    }

    /// <summary>
    /// 모놀리스 한 개. 화면 안에 윗부분이 보이는 비석이다. 윤곽은 맵 편집기의 상자 하나(크기 조절 + 두점자르기)로 만든 볼록 다각형을
    /// SetOutline으로 받는다(받지 않으면 아래 윤곽 값으로 만든 기본 비석). 외곽선·카드 면·이름 띠·패널은 그 윤곽을 안쪽으로 줄이고
    /// 높이로 잘라 만들어 몸체에 새겨진 한 덩어리로 보인다(그림자 투영도 같은 윤곽).
    /// 카드 면이 몸체 대부분을 차지하며 위에서 아래로 그림 → 이름 띠 → 어두운 텍스트 패널이다.
    /// 점등은 면과 몸체의 발광이 마스크로 아래에서 위로 차오르고, 꺼질 때 위에서 아래로 빠진다. 판정과 무관한 표시 전용 오브젝트이며 원점은 바닥 가운데다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonolithView : MonoBehaviour
    {
        public const float CanvasUnitsPerWorld = 100f;
        const int RimOrder = 3, BodyOrder = 4, GlowOrder = 5, FaceOrder = 6, FaceGlowOrder = 7, FillOrder = 8;
        const int PartOrder = 9, StripeOrder = 10, IconOrder = 11, TextOrder = 13, StripeCount = 16;

        [Header("기본 윤곽(유닛) - 맵 편집기 윤곽이 없을 때만 쓴다")]
        [SerializeField, Min(3f), Tooltip("비석 아랫변 폭. 캐릭터 몸(0.72)의 8~9배 안팎")] float width = 6.2f;
        [SerializeField, Min(8f), Tooltip("비석 높이. 화면 높이(약 19.4)의 75~85%")] float height = 15.4f;
        [SerializeField, Range(0f, .2f), Tooltip("위로 갈수록 한쪽에서 줄이는 비율(폭 기준)")] float taper = .035f;
        [SerializeField, Range(0f, 2f), Tooltip("위 두 모서리 깎기")] float topChamfer = .95f;
        [SerializeField, Range(0f, .3f), Tooltip("배경과 구분되는 외곽선 두께")] float rimWidth = .12f;
        [Header("카드 면(유닛, 바닥 기준)")]
        [SerializeField, Range(.1f, 1f), Tooltip("몸체 가장자리에서 카드 면까지")] float faceInset = .38f;
        [SerializeField, Range(0f, 1.2f), Tooltip("카드 면 네 모서리 깎기")] float faceChamfer = .5f;
        [SerializeField] float faceBottom = .7f;
        [SerializeField, Tooltip("카드 면 위로 남기는 몸체(유닛). 작을수록 면이 몸체를 더 차지한다")] float faceTopMargin = 1.35f;
        [SerializeField] float panelTop = 5.6f;
        [SerializeField] float bandGap = .25f;
        [SerializeField] float bandHeight = 1.4f;
        [SerializeField] float iconGap = .25f;
        [Header("텍스트 패널(유닛)")]
        [SerializeField, Tooltip("패널 위·아래 여백")] float panelPadding = .35f;
        [SerializeField, Tooltip("설명 한 줄 간격")] float descriptionPitch = .54f;
        [SerializeField, Tooltip("효과 한 줄 간격")] float effectPitch = .6f;
        [SerializeField, Tooltip("설명과 효과 사이")] float descriptionGap = .25f;
        [Header("점등")]
        [SerializeField, Range(1f, 1.3f), Tooltip("켜질 때 이름 띠 확대")] float bandHoverScale = 1.08f;
        [SerializeField, Min(.2f), Tooltip("그림 영역 사선 무늬 간격")] float stripeSpacing = .55f;
        [SerializeField, Range(0f, .4f), Tooltip("그림 영역 사선 무늬 굵기(0이면 무늬 없음)")] float stripeWidth = .12f;
        [SerializeField, Range(0f, 1f), Tooltip("다른 비석을 호버했을 때 어두워지는 정도")] float silhouetteDepth = .92f;
        [SerializeField] MonolithStyle style = MonolithStyle.Default;
        [Header("부품(프리팹에 저장)")]
        [SerializeField] SpriteRenderer rim, body, bodyGlow, face, faceGlow, iconFill, nameBand, panel;
        [SerializeField] SpriteRenderer[] stripes = new SpriteRenderer[StripeCount];
        [SerializeField] SpriteRenderer[] iconParts = new SpriteRenderer[4];
        [SerializeField] SpriteRenderer iconSprite;
        [SerializeField] SpriteMask lightMask;
        [SerializeField] Canvas canvas;
        [SerializeField] Text title, description;
        [SerializeField] Text[] effects = new Text[CardFaceCatalog.EffectMaxLines];
        [SerializeField, Tooltip("보유 개수 표시용 빈 칸(후속)")] Text countSlot;
        [SerializeField] Button hit;

        readonly Color[] iconBase = new Color[4];
        readonly bool[] iconTinted = new bool[4];
        readonly Color[] effectBase = new Color[CardFaceCatalog.EffectMaxLines];
        Vector2[] outline, authored;
        bool listening;
        float bandBaseScale = 1, titleFit = 1, depth;
        // 진입 연출: 플레이어 색 기운(0~1)과 카드 내용이 드러나는 경계(로컬 높이, 이 위만 보임). 기본은 기운 없음·내용 모두 보임.
        float energy;
        Color energyColor = PaletteAsset.MonolithDefaults.EnergyHighlight;
        float contentFront = float.NegativeInfinity;

        /// <summary>
        /// 진입 연출 상태. energy는 플레이어 색 기운이 아래에서 차오른 정도(0~1, 이미 이징한 값), color는 그 색,
        /// contentFront는 카드 내용(아이콘·이름 띠·패널·글자)이 보이는 경계 높이다. +무한이면 빈 비석, -무한이면 모두 보인다.
        /// </summary>
        public void SetEntrance(float energyFill, Color color, float front)
        {
            energy = Mathf.Clamp01(energyFill);
            energyColor = color;
            contentFront = front;
        }
        public float Energy => energy;
        /// <summary>
        /// 카드 내용이 보이는 정도(0 빈 비석 ~ 1). y는 그 내용의 아랫변이다. 걷히는 기운이 아랫변을 지나야 보이기 시작하므로
        /// 아직 기운이 남은 자리 위에 내용이 겹쳐 보이지 않는다.
        /// </summary>
        public float ContentVisible(float y) => float.IsPositiveInfinity(contentFront) ? 0 : float.IsNegativeInfinity(contentFront) ? 1
            : Smooth(Mathf.Clamp01((y - contentFront) / .6f));
        public float IconContentY => IconBottom;
        public Color BodyGlowColor => bodyGlow.color;

        /// <summary>
        /// 원근 보정: 0이면 그대로, 클수록 꺼진 상태 색을 어둡게 한다(뒤에 있는 작은 비석). 켜지는 만큼 보정이 빠져 호버한 비석은 늘 밝다.
        /// </summary>
        public void SetDepth(float amount) => depth = Mathf.Clamp01(amount);
        public float Depth => depth;

        public event Action Clicked;
        public float Width => Bounds(CurrentOutline).width;
        public float Height => Bounds(CurrentOutline).yMax;
        /// <summary>맵 편집기에서 만든 윤곽을 쓰는지(아니면 기본 윤곽 값).</summary>
        public bool UsesAuthoredOutline => authored != null;
        public float FaceBottom => faceBottom;
        public float FaceTop => Height - faceTopMargin;
        public float PanelTop => panelTop;
        public float BandBottom => panelTop + bandGap;
        public float BandTop => BandBottom + bandHeight;
        public float IconBottom => BandTop + iconGap;
        public float FaceCenterY => (faceBottom + FaceTop) * .5f;
        public Button Hit => hit;
        public Text Title => title;
        public Text Description => description;
        public Text Effect(int line) => effects[line];
        public Canvas Canvas => canvas;
        public bool IconPartVisible(int part) => iconParts[part].enabled;
        public bool IconSpriteVisible => iconSprite.enabled;
        public Color BodyColor => body.color;
        public Color FillColor => iconFill.color;
        public Color TitleColor => title.color;
        public float BandScale => nameBand.transform.localScale.y / Mathf.Max(.0001f, bandBaseScale);
        /// <summary>발광이 차오른 높이(바닥 기준 유닛). 0이면 꺼짐.</summary>
        public float GlowHeight => lightMask != null && lightMask.enabled ? lightMask.transform.localScale.y : 0;
        public RectTransform HitArea => hit.GetComponent<RectTransform>();
        public bool Built => body != null && rim != null && bodyGlow != null && iconFill != null && lightMask != null
            && canvas != null && title != null && hit != null && countSlot != null && stripes != null && stripes.Length > 0 && stripes[0] != null;
        /// <summary>몸체 윤곽(로컬, 반시계, 원점은 바닥 가운데). 그림자 투영에 쓴다.</summary>
        public Vector2[] Outline => CurrentOutline;
        Vector2[] CurrentOutline => authored ?? (outline ??= Band(width, height, taper, 0, 0, height, 0, topChamfer));

        /// <summary>
        /// 맵 편집기 상자에서 온 윤곽(로컬, 반시계, 원점은 바닥 가운데)을 쓴다. null이면 기본 윤곽으로 돌아간다.
        /// 몸체·카드 면·발광·그림자·클릭 영역이 모두 이 윤곽을 따른다.
        /// </summary>
        public void SetOutline(Vector2[] local)
        {
            authored = local == null ? null : Clean(local);
            outline = null;
            Layout();
        }

        void Awake() => Listen();

        void Listen()
        {
            if (listening || hit == null) return;
            listening = true;
            hit.onClick.AddListener(() => Clicked?.Invoke());
        }

        /// <summary>
        /// 몸체 가장자리에서 inset만큼 안쪽, bottom~top 높이의 볼록 다각형(반시계). 아래 모서리는 bottomCut, 위 모서리는 topCut만큼 깎는다.
        /// 몸체(inset 0)·외곽선(음수 inset)·카드 면·이름 띠·패널이 모두 이 함수를 써서 가장자리 기울기가 같다.
        /// </summary>
        public static Vector2[] Band(float width, float height, float taper, float inset, float bottom, float top, float bottomCut, float topCut)
        {
            float Half(float y) => width * .5f - taper * width * (y / height) - inset;
            float limit = Mathf.Min((top - bottom) * .45f, Half(top) * .9f);
            bottomCut = Mathf.Clamp(bottomCut, 0, limit);
            topCut = Mathf.Clamp(topCut, 0, limit);
            var points = new System.Collections.Generic.List<Vector2>(8);
            if (bottomCut > .0001f)
            {
                points.Add(new Vector2(-Half(bottom) + bottomCut, bottom));
                points.Add(new Vector2(Half(bottom) - bottomCut, bottom));
                points.Add(new Vector2(Half(bottom + bottomCut), bottom + bottomCut));
            }
            else
            {
                points.Add(new Vector2(-Half(bottom), bottom));
                points.Add(new Vector2(Half(bottom), bottom));
            }
            if (topCut > .0001f)
            {
                points.Add(new Vector2(Half(top - topCut), top - topCut));
                points.Add(new Vector2(Half(top) - topCut, top));
                points.Add(new Vector2(-Half(top) + topCut, top));
                points.Add(new Vector2(-Half(top - topCut), top - topCut));
            }
            else
            {
                points.Add(new Vector2(Half(top), top));
                points.Add(new Vector2(-Half(top), top));
            }
            if (bottomCut > .0001f) points.Add(new Vector2(-Half(bottom + bottomCut), bottom + bottomCut));
            return points.ToArray();
        }

        /// <summary>패널 안에 설명 최대 줄 수와 효과 최대 줄 수가 여백과 함께 들어가는지(남는 높이). 카드 문구 규칙과 배치를 검사에 쓴다.</summary>
        public float PanelSlack => (panelTop - (faceBottom + .18f)) - (panelPadding * 2 + descriptionPitch * CardFaceCatalog.DescriptionMaxLines
            + descriptionGap + effectPitch * CardFaceCatalog.EffectMaxLines);

        /// <summary>프리팹을 만들거나 Sandbox처럼 저장된 부품이 없을 때 부품을 만든다. 이미 있으면 다시 배치만 한다.</summary>
        public void Build(ShapeLibrary shapes, FrontendTheme theme)
        {
            if (Built) { Layout(); return; }
            var t = transform;
            rim = Convex(t, "Rim", shapes, RimOrder);
            body = Convex(t, "Body", shapes, BodyOrder);
            bodyGlow = Convex(t, "Body_glow", shapes, GlowOrder);
            face = Convex(t, "Face", shapes, FaceOrder);
            faceGlow = Convex(t, "Face_glow", shapes, FaceGlowOrder);
            iconFill = Convex(t, "Icon_fill", shapes, FillOrder);
            nameBand = Convex(t, "Name_band", shapes, PartOrder);
            panel = Convex(t, "Text_panel", shapes, PartOrder);
            var pattern = Group(t, "Icon_stripes");
            for (int i = 0; i < stripes.Length; i++)
                stripes[i] = ShapeLibrary.Create(pattern, "Stripe_" + i, shapes.Square, SortingLayers.Terrain, StripeOrder, style.stripe);
            foreach (var glow in new[] { bodyGlow, faceGlow, iconFill }) glow.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            foreach (var stripe in stripes) stripe.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var maskObject = new GameObject("Light_mask", typeof(SpriteMask));
            maskObject.transform.SetParent(t, false);
            lightMask = maskObject.GetComponent<SpriteMask>();
            lightMask.sprite = shapes.Square;
            var icon = Group(t, "Icon");
            for (int i = 0; i < iconParts.Length; i++)
                iconParts[i] = ShapeLibrary.Create(icon, "Icon_part_" + i, shapes.Square, SortingLayers.Terrain, IconOrder, style.icon);
            iconSprite = ShapeLibrary.Create(icon, "Icon_sprite", null, SortingLayers.Terrain, IconOrder, PaletteAsset.Neutral);

            var canvasObject = new GameObject("Face_text", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(t, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingLayerName = SortingLayers.Terrain;
            canvas.sortingOrder = TextOrder;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            var hitImage = UiImage(canvas.transform, "Click_area");
            hitImage.color = PaletteAsset.MonolithDefaults.HitArea;
            hitImage.raycastTarget = true;
            hit = hitImage.gameObject.AddComponent<Button>();
            hit.transition = Selectable.Transition.None;
            hit.targetGraphic = hitImage;
            hitImage.gameObject.AddComponent<FrontendHover>();
            title = UiText(canvas.transform, "Name", theme.titleFont, 78, TextAnchor.MiddleCenter, style.nameText);
            description = UiText(canvas.transform, "Description", theme.textFont, 40, TextAnchor.UpperCenter, style.text);
            for (int i = 0; i < effects.Length; i++)
                effects[i] = UiText(canvas.transform, "Effect_line_" + i, theme.boldFont, 40, TextAnchor.MiddleCenter, style.text);
            countSlot = UiText(canvas.transform, "Count_slot", theme.boldFont, 34, TextAnchor.MiddleRight, style.text);
            countSlot.text = "";
            Layout();
            Listen();
        }

        public FrontendHover Hover => hit != null ? hit.GetComponent<FrontendHover>() : null;

        /// <summary>윤곽·카드 면·글자 영역을 값에 맞춰 다시 만든다. 실행 때도 다시 부르므로 편집기에서는 미리보기용이다.</summary>
        [ContextMenu("윤곽 값으로 다시 배치")]
        public void Layout()
        {
            if (!Built) return;
            outline = null;
            var shape = CurrentOutline;
            float h = Height, w = Width, faceTop = FaceTop;
            ApplyPolygon(rim, ClipY(Offset(shape, rimWidth), 0, h + rimWidth * 2));
            ApplyPolygon(body, shape);
            ApplyPolygon(bodyGlow, shape);
            var faceShape = Corners(ClipY(Offset(shape, -faceInset), faceBottom, faceTop), faceChamfer, true, true);
            ApplyPolygon(face, faceShape);
            ApplyPolygon(faceGlow, faceShape);
            var inner = Offset(shape, -(faceInset + .16f));
            var fillShape = Corners(ClipY(inner, IconBottom, faceTop - .16f), faceChamfer * .7f, false, true);
            ApplyPolygon(iconFill, fillShape);
            ApplyPolygon(nameBand, ClipY(Offset(shape, -rimWidth * .5f), BandBottom, BandTop));
            ApplyPolygon(panel, Corners(ClipY(inner, faceBottom + .18f, panelTop), faceChamfer * .6f, true, false));
            bandBaseScale = nameBand.transform.localScale.y;
            PlaceStripes(fillShape);

            var root = (RectTransform)canvas.transform;
            root.localScale = Vector3.one / CanvasUnitsPerWorld;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.pivot = new Vector2(.5f, 0);
            root.sizeDelta = new Vector2(w + rimWidth * 2, h + rimWidth) * CanvasUnitsPerWorld;
            // 호버·클릭은 카드 면이 아니라 비석 전체(윗부분 포함)에서 받는다.
            Area(hit.GetComponent<RectTransform>(), 0, h + rimWidth, w + rimWidth * 2);
            Area(title.rectTransform, BandBottom, BandTop, Inner(BandTop) * 2 - .2f);
            float textWidth = Inner(panelTop) * 2 - .55f;
            float descriptionTop = panelTop - panelPadding;
            Area(description.rectTransform, descriptionTop - descriptionPitch * CardFaceCatalog.DescriptionMaxLines, descriptionTop, textWidth);
            float effectTop = descriptionTop - descriptionPitch * CardFaceCatalog.DescriptionMaxLines - descriptionGap;
            for (int i = 0; i < effects.Length; i++)
                Area(effects[i].rectTransform, effectTop - effectPitch * (i + 1), effectTop - effectPitch * i, textWidth);
            Area(countSlot.rectTransform, faceTop - .8f, faceTop - .2f, Inner(faceTop) * 2 - .7f);
            if (PanelSlack < 0)
                Debug.LogWarning($"[Knockbox] {name}: 텍스트 패널이 설명 {CardFaceCatalog.DescriptionMaxLines}줄·효과 {CardFaceCatalog.EffectMaxLines}줄보다 낮습니다.");
        }

        /// <summary>카드 면 안쪽(가장자리에서 faceInset) 높이 y에서 가운데(0)를 기준으로 쓸 수 있는 반폭.</summary>
        float Inner(float y) => HalfWidthAt(Offset(CurrentOutline, -faceInset), y);

        static Rect Bounds(Vector2[] poly)
        {
            Vector2 min = poly[0], max = poly[0];
            foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>반시계 볼록 다각형의 변을 d만큼 바깥(음수면 안쪽)으로 옮긴다. 옆변 기울기는 그대로다.</summary>
        public static Vector2[] Offset(Vector2[] poly, float d)
        {
            int n = poly.Length;
            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var a0 = poly[(i + n - 1) % n]; var a1 = poly[i]; var b1 = poly[(i + 1) % n];
                var e0 = (a1 - a0).normalized; var e1 = (b1 - a1).normalized;
                var n0 = new Vector2(e0.y, -e0.x); var n1 = new Vector2(e1.y, -e1.x);
                // 두 변을 d만큼 옮긴 직선의 교점.
                var p0 = a0 + n0 * d; var p1 = a1 + n1 * d;
                float cross = e0.x * e1.y - e0.y * e1.x;
                if (Mathf.Abs(cross) < 1e-6f) { result[i] = a1 + n1 * d; continue; }
                float t = ((p1.x - p0.x) * e1.y - (p1.y - p0.y) * e1.x) / cross;
                result[i] = p0 + e0 * t;
            }
            return Clean(result);
        }

        /// <summary>볼록 다각형에서 bottom~top 높이만 남긴다.</summary>
        public static Vector2[] ClipY(Vector2[] poly, float bottom, float top) =>
            Clip(Clip(poly, Vector2.up, bottom), Vector2.down, -top);

        /// <summary>법선 normal 쪽(dot(normal, p) ≥ offset)만 남긴다.</summary>
        static Vector2[] Clip(Vector2[] poly, Vector2 normal, float offset)
        {
            var result = new System.Collections.Generic.List<Vector2>(poly.Length + 2);
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                float dp = Vector2.Dot(normal, p) - offset, dq = Vector2.Dot(normal, q) - offset;
                if (dp >= 0) result.Add(p);
                if ((dp >= 0) != (dq >= 0)) result.Add(p + (q - p) * (dp / (dp - dq)));
            }
            return Clean(result.ToArray());
        }

        /// <summary>아래(또는 위) 끝의 거의 직각인 모서리를 cut만큼 깎는다.</summary>
        static Vector2[] Corners(Vector2[] poly, float cut, bool bottom, bool top)
        {
            if (cut <= .0001f || poly.Length < 3) return poly;
            var box = Bounds(poly);
            var planes = new System.Collections.Generic.List<(Vector2 n, float o)>();
            for (int i = 0; i < poly.Length; i++)
            {
                var v = poly[i];
                bool atBottom = bottom && Mathf.Abs(v.y - box.yMin) < 1e-3f, atTop = top && Mathf.Abs(v.y - box.yMax) < 1e-3f;
                if (!atBottom && !atTop) continue;
                var a = (poly[(i + poly.Length - 1) % poly.Length] - v); var b = (poly[(i + 1) % poly.Length] - v);
                if (Vector2.Angle(a, b) > 110f) continue;
                float c = Mathf.Min(cut, a.magnitude * .45f, b.magnitude * .45f);
                var p = v + a.normalized * c; var q = v + b.normalized * c;
                var n = new Vector2(-(q - p).y, (q - p).x).normalized;
                if (Vector2.Dot(n, v - p) > 0) n = -n;
                planes.Add((n, Vector2.Dot(n, p)));
            }
            foreach (var plane in planes) poly = Clip(poly, plane.n, plane.o);
            return poly;
        }

        /// <summary>높이 y에서 가운데(0) 기준으로 양쪽 모두 들어가는 반폭.</summary>
        static float HalfWidthAt(Vector2[] poly, float y)
        {
            float left = float.MinValue, right = float.MaxValue;
            bool hit = false;
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                if ((p.y - y) * (q.y - y) > 0 || Mathf.Abs(q.y - p.y) < 1e-6f) continue;
                float x = p.x + (q.x - p.x) * ((y - p.y) / (q.y - p.y));
                hit = true;
                if (x < 0) left = Mathf.Max(left, x); else right = Mathf.Min(right, x);
            }
            if (!hit) return 0;
            if (left == float.MinValue) left = -right;
            if (right == float.MaxValue) right = -left;
            return Mathf.Max(0, Mathf.Min(-left, right));
        }

        /// <summary>겹친 점과 한 직선 위의 점을 지운다.</summary>
        static Vector2[] Clean(Vector2[] poly)
        {
            var list = new System.Collections.Generic.List<Vector2>(poly);
            bool changed = true;
            while (changed && list.Count > 3)
            {
                changed = false;
                for (int i = 0; i < list.Count && list.Count > 3; i++)
                {
                    var a = list[(i + list.Count - 1) % list.Count]; var v = list[i]; var b = list[(i + 1) % list.Count];
                    if ((v - a).sqrMagnitude < 1e-8f || Mathf.Abs((v.x - a.x) * (b.y - a.y) - (v.y - a.y) * (b.x - a.x)) < 1e-6f)
                    { list.RemoveAt(i); changed = true; break; }
                }
            }
            return list.ToArray();
        }


        void PlaceStripes(Vector2[] area)
        {
            // 그림 영역 안에만 보이는 옅은 사선 무늬(45°). 각 줄을 볼록 다각형에 잘라 붙인다.
            Vector2 min = area[0], max = area[0];
            foreach (var p in area) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var dir = new Vector2(1, 1).normalized;
            var normal = new Vector2(-dir.y, dir.x);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in area) { float d = Vector2.Dot(p, normal); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
            int used = 0;
            for (float d = lo + stripeSpacing * .5f; d < hi && used < stripes.Length && stripeWidth > 0; d += stripeSpacing)
            {
                if (!Clip(area, normal * d, dir, out var a, out var b) || (b - a).magnitude < .2f) continue;
                var s = stripes[used++];
                s.enabled = true;
                s.transform.localPosition = (a + b) * .5f;
                s.transform.localRotation = Quaternion.Euler(0, 0, 45);
                ShapeLibrary.SetSize(s, (b - a).magnitude, stripeWidth);
            }
            for (int i = used; i < stripes.Length; i++) stripes[i].enabled = false;
        }

        /// <summary>직선(점 origin, 방향 dir)을 반시계 볼록 다각형으로 자른다(Cyrus-Beck).</summary>
        static bool Clip(Vector2[] poly, Vector2 origin, Vector2 dir, out Vector2 a, out Vector2 b)
        {
            float t0 = -1000, t1 = 1000;
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var e = poly[(i + 1) % poly.Length] - p;
                var inward = new Vector2(-e.y, e.x);
                float num = Vector2.Dot(inward, origin - p), den = Vector2.Dot(inward, dir);
                if (Mathf.Abs(den) < 1e-6f) { if (num < 0) { a = b = origin; return false; } continue; }
                float t = -num / den;
                if (den > 0) t0 = Mathf.Max(t0, t); else t1 = Mathf.Min(t1, t);
            }
            a = origin + dir * t0; b = origin + dir * t1;
            return t1 > t0;
        }

        /// <summary>카드 데이터로 표면을 채운다. 줄 수·글자 수는 CardFaceCatalog 규칙이 보장하고, 폭을 넘는 줄은 줄여서 넘치지 않게 한다.</summary>
        public void Bind(in CardFace card, ShapeLibrary shapes)
        {
            title.text = card.title;
            description.text = card.description ?? "";
            int count = card.effects?.Length ?? 0;
            for (int i = 0; i < effects.Length; i++)
            {
                bool used = i < count;
                effects[i].gameObject.SetActive(used);
                if (!used) continue;
                effects[i].text = card.effects[i].text;
                effectBase[i] = LineColor(card.effects[i].polarity);
                effects[i].color = effectBase[i];
                FitWidth(effects[i]);
            }
            FitWidth(title);
            FitWidth(description);
            bool art = card.iconSprite != null;
            iconSprite.enabled = art;
            float iconSize = Mathf.Min(FaceTop - .4f - IconBottom, Inner(FaceTop) * 2) - .6f;
            if (art)
            {
                iconSprite.sprite = card.iconSprite;
                iconSprite.transform.localPosition = new Vector3(0, IconCenter, 0);
                ShapeLibrary.SetSize(iconSprite, iconSize, iconSize);
            }
            DrawIcon(art ? (CardIconShape?)null : card.icon, shapes, iconSize / 3f);
            Present(0, 0, false);
        }

        float IconCenter => (IconBottom + FaceTop - .16f) * .5f;

        /// <summary>
        /// 상태 표시. lit(0~1)은 점등 진행도로, 면과 몸체의 발광이 아래에서 위로 차오른다(줄어들 때는 위에서 아래로 빠진다).
        /// 발광이 지나간 자리부터 텍스트 → 이름 → 그림 순서로 켜지며, 그림 영역은 황금색으로 차고 이름은 노란 강조색이 된다. 텍스트 패널은 어두운 바탕을 유지한다.
        /// silhouette(0~1)은 다른 비석을 호버한 뒤 이 비석을 거의 검은 실루엣으로 만든다(글자는 흐리게 읽힌다). chosen이면 켜 둔다.
        /// </summary>
        public void Present(float lit, float silhouette, bool chosen)
        {
            if (chosen) { lit = 1; silhouette = 0; }
            var box = Bounds(CurrentOutline);
            float litGlow = Out(Mathf.Clamp01(lit)) * (box.yMax + rimWidth), energyGlow = energy * (box.yMax + rimWidth);
            float glow = Mathf.Max(litGlow, energyGlow);
            // 진입 기운이 점등보다 높이 차 있으면 발광을 플레이어 색으로 칠한다(그림 영역·글자 점등은 점등 값만 따른다).
            bool energized = energyGlow > litGlow + .001f;
            lightMask.enabled = glow > .001f;
            bodyGlow.enabled = faceGlow.enabled = iconFill.enabled = lightMask.enabled;
            if (lightMask.enabled)
            {
                lightMask.transform.localPosition = new Vector3(box.center.x, glow * .5f, 0);
                lightMask.transform.localScale = new Vector3(box.width + rimWidth * 2 + .2f, glow, 1);
            }
            float Passed(float y, float span) => Smooth(Mathf.Clamp01((litGlow - y) / span));
            float textOn = Passed(faceBottom + 1f, 2f), nameOn = Passed(BandBottom, bandHeight), iconOn = Passed(IconCenter - 1f, 2f);
            // 실루엣 어두워짐과 원근 어두워짐을 합친다. 원근은 켜질수록 빠진다.
            float far = depth * (1 - Out(Mathf.Clamp01(lit)));
            float dim = 1 - (1 - silhouette * silhouetteDepth) * (1 - far);
            Color Dark(Color c) => Color.Lerp(c, style.silhouette, dim);
            body.color = Dark(style.body);
            bodyGlow.color = style.bodyLit;
            face.color = Dark(style.face);
            faceGlow.color = style.faceLit;
            iconFill.color = style.iconFill;
            foreach (var s in stripes) if (s.enabled) s.color = style.stripe;
            if (energized)
            {
                var energyBody = energyColor;
                energyBody.a = .92f;
                bodyGlow.color = energyBody;
                faceGlow.color = iconFill.color = Color.Lerp(energyColor, PaletteAsset.MonolithDefaults.EnergyHighlight, .28f);
                foreach (var s in stripes) if (s.enabled) s.color = PaletteAsset.MonolithDefaults.EnergyStripe;
            }
            panel.color = Dark(style.panel);
            rim.color = Color.Lerp(Color.Lerp(style.rim, style.silhouetteRim, Mathf.Max(silhouette, far * .8f)), style.rimLit, Out(lit) * .75f);
            nameBand.color = Dark(style.nameBand);
            var s0 = nameBand.transform.localScale;
            nameBand.transform.localScale = new Vector3(s0.x, bandBaseScale * (1 + (bandHoverScale - 1) * nameOn), 1);
            for (int i = 0; i < iconParts.Length; i++)
            {
                if (!iconParts[i].enabled) continue;
                // 면 색으로 깎아 낸 조각은 면/황금 바탕을 따라가고, 그림 조각은 꺼진 회색 → 실루엣(검정) → 켜지면 황금 위의 진한 색이 된다.
                iconParts[i].color = iconTinted[i]
                    ? Color.Lerp(Dark(iconBase[i]), style.iconFill, iconOn)
                    : Color.Lerp(Color.Lerp(style.icon, style.silhouette, Mathf.Max(silhouette, far * .7f)), style.iconOnFill, iconOn);
            }
            if (iconSprite.enabled)
                iconSprite.color = Color.Lerp(Color.Lerp(PaletteAsset.Neutral, style.silhouette, silhouette), PaletteAsset.Neutral, iconOn);
            // 글자는 읽혀야 하므로 원근 보정을 절반만 준다.
            float textDim = Mathf.Max(silhouette, far * .5f);
            var dimText = Color.Lerp(style.dimText, style.silhouetteText, textDim);
            title.color = Color.Lerp(Color.Lerp(style.nameText, style.silhouetteText, textDim), style.nameLit, nameOn);
            title.transform.localScale = Vector3.one * titleFit * (1 + (bandHoverScale - 1) * nameOn);
            description.color = Color.Lerp(dimText, style.text, textOn);
            for (int i = 0; i < effects.Length; i++)
                if (effects[i].gameObject.activeSelf)
                    effects[i].color = Color.Lerp(Color.Lerp(effectBase[i], style.silhouetteText, Mathf.Max(silhouette * .85f, far * .4f)), effectBase[i], textOn);
            // 진입 연출: 빈 비석에서 기운이 걷힌 자리부터 카드 내용이 드러난다(위에서 아래로).
            float iconShow = ContentVisible(IconBottom), bandShow = ContentVisible(BandBottom - .1f), panelShow = ContentVisible(faceBottom);
            for (int i = 0; i < iconParts.Length; i++) if (iconParts[i].enabled) iconParts[i].color = Fade(iconParts[i].color, iconShow);
            if (iconSprite.enabled) iconSprite.color = Fade(iconSprite.color, iconShow);
            nameBand.color = Fade(nameBand.color, bandShow);
            title.color = Fade(title.color, bandShow);
            panel.color = Fade(panel.color, panelShow);
            description.color = Fade(description.color, panelShow);
            countSlot.color = Fade(countSlot.color, panelShow);
            for (int i = 0; i < effects.Length; i++) if (effects[i].gameObject.activeSelf) effects[i].color = Fade(effects[i].color, panelShow);
        }

        static Color Fade(Color c, float visible) { c.a *= visible; return c; }

        public void SetInteractable(bool value) => hit.interactable = value;

        public static Color LineColor(CardLinePolarity polarity) =>
            polarity == CardLinePolarity.Positive ? PaletteAsset.MonolithDefaults.Positive
            : polarity == CardLinePolarity.Negative ? PaletteAsset.MonolithDefaults.Negative
            : PaletteAsset.MonolithDefaults.NeutralText;

        void FitWidth(Text text)
        {
            float available = text.rectTransform.rect.width;
            float preferred = text.preferredWidth;
            float fit = preferred > available && preferred > 0 ? available / preferred : 1;
            if (text == title) titleFit = fit;
            text.rectTransform.localScale = Vector3.one * fit;
        }

        void DrawIcon(CardIconShape? shape, ShapeLibrary shapes, float unit)
        {
            for (int i = 0; i < iconParts.Length; i++) { iconParts[i].enabled = false; iconTinted[i] = false; }
            if (shape == null) return;
            float cx = 0, cy = IconCenter;
            switch (shape.Value)
            {
                case CardIconShape.Burst:
                    // 한 방: 원 하나와 바깥으로 뻗은 삼각형 셋.
                    Part(0, shapes.Circle, cx, cy, 1.5f, 1.5f, 0, unit);
                    Part(1, shapes.Triangle, cx, cy + 1.25f, .7f, .8f, 0, unit);
                    Part(2, shapes.Triangle, cx - 1.1f, cy - .65f, .7f, .8f, 120, unit);
                    Part(3, shapes.Triangle, cx + 1.1f, cy - .65f, .7f, .8f, -120, unit);
                    break;
                case CardIconShape.Shield:
                    // 맷집: 사각형과 아래로 향한 삼각형, 가운데 홈.
                    Part(0, shapes.Square, cx, cy + .35f, 2.0f, 1.5f, 0, unit);
                    Part(1, shapes.Triangle, cx, cy - .85f, 2.0f, 1.0f, 180, unit);
                    Part(2, shapes.Square, cx, cy + .2f, .26f, 2.0f, 0, unit, style.face);
                    break;
                case CardIconShape.Arc:
                    // 멀리 던지기: 고리 일부와 화살촉.
                    Part(0, shapes.Ring, cx - .25f, cy - .35f, 2.6f, 2.6f, 0, unit);
                    Part(1, shapes.Square, cx - .25f, cy - 1.05f, 2.8f, 1.4f, 0, unit, style.face);
                    Part(2, shapes.Triangle, cx + 1.05f, cy + .65f, .75f, .75f, -45, unit);
                    Part(3, shapes.Circle, cx - 1.5f, cy - .35f, .35f, .35f, 0, unit);
                    break;
            }
        }

        void Part(int index, Sprite sprite, float x, float y, float w, float h, float angle, float unit, Color? tint = null)
        {
            var part = iconParts[index];
            part.sprite = sprite;
            part.enabled = true;
            iconTinted[index] = tint.HasValue;
            iconBase[index] = tint ?? style.icon;
            // 면 색으로 도형을 깎아 내는 조각은 항상 위에 그린다(같은 순서면 그리는 순서가 바뀔 수 있다).
            part.sortingOrder = tint.HasValue ? IconOrder + 1 : IconOrder;
            part.color = iconBase[index];
            float cy = IconCenter;
            part.transform.localPosition = new Vector3(x * unit, cy + (y - cy) * unit, 0);
            part.transform.localRotation = Quaternion.Euler(0, 0, angle);
            ShapeLibrary.SetSize(part, w * unit, h * unit);
        }

        static SpriteRenderer Convex(Transform parent, string name, ShapeLibrary shapes, int order)
        {
            var sr = ShapeLibrary.Create(parent, name, shapes.Square, SortingLayers.Terrain, order, PaletteAsset.Neutral);
            sr.gameObject.AddComponent<ConvexTerrainSprite>();
            return sr;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        /// <summary>로컬 다각형을 ConvexTerrainSprite(경계 상자 기준 정규화 꼭짓점)로 그린다.</summary>
        static void ApplyPolygon(SpriteRenderer sr, Vector2[] points)
        {
            Vector2 min = points[0], max = points[0];
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var size = Vector2.Max(max - min, Vector2.one * .001f);
            var center = (min + max) * .5f;
            var normalized = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) normalized[i] = (points[i] - center) / size;
            sr.transform.localPosition = center;
            sr.transform.localRotation = Quaternion.identity;
            sr.transform.localScale = new Vector3(size.x, size.y, 1);
            sr.GetComponent<ConvexTerrainSprite>().SetNormalized(normalized);
        }

        static void Area(RectTransform rect, float bottom, float top, float w)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, (bottom + top) * .5f) * CanvasUnitsPerWorld;
            rect.sizeDelta = new Vector2(w, top - bottom) * CanvasUnitsPerWorld;
            rect.localScale = Vector3.one;
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);
        static float Out(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

        static Image UiImage(Transform parent, string name)
        {
            var go = new GameObject(SceneObjectName.Normalize(name), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        static Text UiText(Transform parent, string name, Font font, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(SceneObjectName.Normalize(name), typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.05f;
            text.raycastTarget = false;
            return text;
        }
    }
}
