using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 결과 공간(이후 로비)의 머리 위 준비 표식(2026-11-16 게임 흐름 CP1). 선수 색 고리가 머리 위에 떠 있고,
    /// 준비를 켜면 같은 색으로 안이 톡 차오르며, 끄면 비워진다. 판정과 무관한 화면 전용 표시다.
    /// </summary>
    public sealed class ReadyMarkers
    {
        const float Size = 0.62f;
        const float FillSize = 0.42f;
        const float HeadGap = 0.55f;
        const float PopSeconds = 0.22f;
        readonly Transform[] roots = new Transform[2];
        readonly Transform[] fillPivots = new Transform[2];
        readonly float[] changedAt = { -10f, -10f };
        readonly bool[] ready = new bool[2];

        public ReadyMarkers(Transform parent, ShapeLibrary shapes, PaletteAsset palette)
        {
            for (int p = 0; p < 2; p++)
            {
                roots[p] = new GameObject("Ready_marker_P" + (p + 1)).transform;
                roots[p].SetParent(parent, false);
                var ring = ShapeLibrary.Create(roots[p], "Ring", shapes.Ring, SortingLayers.UI, 40, palette.FighterBody(p));
                ShapeLibrary.SetSize(ring, Size, Size);
                fillPivots[p] = new GameObject("Fill_pivot").transform;
                fillPivots[p].SetParent(roots[p], false);
                var fill = ShapeLibrary.Create(fillPivots[p], "Fill", shapes.Circle, SortingLayers.UI, 41, palette.FighterBody(p));
                ShapeLibrary.SetSize(fill, FillSize, FillSize);
                roots[p].gameObject.SetActive(false);
            }
        }

        /// <param name="headTop">선수 몸 위쪽 끝(월드 좌표).</param>
        public void Render(bool visible, int fighter, Vector2 headTop, bool isReady, float now)
        {
            var root = roots[fighter];
            root.gameObject.SetActive(visible);
            if (!visible)
            {
                ready[fighter] = false;
                changedAt[fighter] = -10f;
                return;
            }
            if (isReady != ready[fighter])
            {
                ready[fighter] = isReady;
                changedAt[fighter] = now;
            }
            float t = Mathf.Clamp01((now - changedAt[fighter]) / PopSeconds);
            // 켜질 때는 살짝 넘쳤다가 자리 잡고, 꺼질 때는 빠르게 줄어든다.
            float fill = isReady ? t + 0.35f * Mathf.Sin(t * Mathf.PI) : 1f - t;
            float pop = isReady ? 1f + 0.18f * Mathf.Sin(t * Mathf.PI) : 1f;
            root.position = new Vector3(headTop.x, headTop.y + HeadGap, root.position.z);
            root.localScale = Vector3.one * pop;
            fillPivots[fighter].localScale = Vector3.one * Mathf.Max(0.0001f, fill);
            fillPivots[fighter].gameObject.SetActive(fill > 0.001f);
        }
    }
}
