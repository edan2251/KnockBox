using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>이전 재조립 실험 보존용. 현재 화면에서는 생성/호출하지 않는다. 파편 퇴장은 KoShatter, 완성 초상 진입은 FrontendView.Motion이 담당한다.</summary>
    public sealed class FrontendReassembly
    {
        readonly RectTransform stage;
        readonly Image[] pieces = new Image[6];
        readonly Vector2[] starts = new Vector2[6], sizes = new Vector2[6];
        readonly float[] angles = new float[6];
        readonly Color[] colors = new Color[6];
        public int CapturedCount { get; private set; }
        public int VisibleCount { get; private set; }

        public FrontendReassembly(RectTransform stage, RectTransform parent)
        {
            this.stage = stage;
            var root = FrontendWidgets.Rect(parent, "Defeated pieces reassemble", 0, 0, 1920, 1080);
            root.SetSiblingIndex(2); // 패널 앞, 카드 뒤.
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = FrontendWidgets.Image(root, "Defeated piece " + i, new Rect(0, 0, 32, 32), PaletteAsset.Neutral);
                pieces[i].rectTransform.pivot = Vector2.one * .5f;
            }
            Clear();
        }

        public void Capture(KoShatter shatter, Camera camera)
        {
            Clear();
            if (camera == null) return;
            for (int i = 0; i < pieces.Length; i++)
            {
                var source = shatter.PieceVisual(i);
                if (source == null) continue;
                Vector2 screen = camera.WorldToScreenPoint(source.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, screen, null, out var local);
                starts[i] = local + new Vector2(960, -540);
                // 화면 밖으로 튄 조각도 가장자리부터 들어와 이동 경로를 읽을 수 있게 한다.
                starts[i] = new Vector2(Mathf.Clamp(starts[i].x, -50, 1970), Mathf.Clamp(starts[i].y, -1130, 50));
                float pixelsPerUnit = camera.pixelHeight / (2 * camera.orthographicSize) / stage.lossyScale.y;
                sizes[i] = new Vector2(source.bounds.size.x, source.bounds.size.y) * pixelsPerUnit;
                angles[i] = source.transform.eulerAngles.z;
                colors[i] = source.color;
                pieces[i].sprite = source.sprite;
                CapturedCount++;
            }
            if (CapturedCount > 0) shatter.Clear();
        }

        public void Clear()
        {
            CapturedCount = VisibleCount = 0;
            foreach (var piece in pieces) piece.gameObject.SetActive(false);
        }

        public void Render(float age, RectTransform portrait)
        {
            var body = portrait.Find(Knockbox.Presentation.SceneObjectName.Path("Body")) as RectTransform;
            if (body == null) return;
            VisibleCount = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                bool visible = i < CapturedCount && age < 1.15f;
                pieces[i].gameObject.SetActive(visible);
                if (!visible) continue;
                VisibleCount++;
                float t = Smooth(Range(age, .12f + i * .018f, 1.03f + i * .018f));
                Vector2 grid = new Vector2((i % 3 - 1) * body.rect.width * .24f, (i / 3 == 0 ? -1 : 1) * body.rect.height * .22f);
                Vector2 target = (Vector2)stage.InverseTransformPoint(body.TransformPoint(body.rect.center + grid)) + new Vector2(960, -540);
                var rect = pieces[i].rectTransform;
                rect.anchoredPosition = Arc(starts[i], target, t, 160 + i * 22);
                rect.sizeDelta = Vector2.Lerp(sizes[i], new Vector2(body.rect.width / 3, body.rect.height / 2), t);
                rect.localRotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(angles[i], portrait.localEulerAngles.z, t));
                var color = colors[i]; color.a *= 1 - Range(age, .88f, 1.15f); pieces[i].color = color;
            }
        }
    }
}
