using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 경계 튕김 연출(08-visual-direction.md 5절, CR-002). 평소에는 경계를 그리지 않는다.
    /// 캐릭터가 경계에서 2유닛 안으로 들어오면 그 근처 가장자리에 `hit` 빨강의 옅은 경고 선(가까울수록 진하게),
    /// 닿는 순간 가장자리를 따라 퍼지는 빨강 물결. 흰 섬광·흔들림·소리는 MatchPresenter가 함께 낸다.
    /// </summary>
    public sealed class BoundaryView : MonoBehaviour
    {
        public const float WarningDistance = 2f;
        const float WarningLength = 6f;
        const float WarningWidth = 0.1f;
        public const float WarningMaxAlpha = 0.5f;
        const int RippleCount = 4;
        const float RippleLife = 0.35f;
        const float RippleLength = 16f;
        const float RippleWidth = 0.2f;

        readonly SpriteRenderer[] warnings = new SpriteRenderer[8];
        readonly SpriteRenderer[] ripples = new SpriteRenderer[RippleCount];
        readonly float[] rippleAge = new float[RippleCount];
        readonly bool[] rippleAlongX = new bool[RippleCount];
        PaletteAsset palette;
        ArenaDef arena;
        int nextRipple;

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            for (int i = 0; i < warnings.Length; i++)
            {
                warnings[i] = ShapeLibrary.Create(transform, "BoundaryWarning", shapes.Square, SortingLayers.Fx, 0, palette.hit);
                warnings[i].enabled = false;
            }
            for (int i = 0; i < RippleCount; i++)
            {
                ripples[i] = ShapeLibrary.Create(transform, "BoundaryRipple", shapes.Square, SortingLayers.Fx, 1, palette.hit);
                ripples[i].enabled = false;
                rippleAge[i] = RippleLife;
            }
        }

        public void SetArena(ArenaDef value)
        {
            arena = value;
            Clear();
        }

        public void Clear()
        {
            foreach (var w in warnings)
                w.enabled = false;
            for (int i = 0; i < RippleCount; i++)
            {
                ripples[i].enabled = false;
                rippleAge[i] = RippleLife;
            }
        }

        /// <summary>현재 보이는 경고 선의 가장 진한 불투명도(테스트·캡처 확인용).</summary>
        public float StrongestWarning { get; private set; }

        /// <summary>두 캐릭터 몸 가운데 위치로 경고 선을 갱신한다.</summary>
        public void Render(Vector2 body0, Vector2 body1, float half)
        {
            StrongestWarning = 0f;
            if (arena == null || !arena.HasBoundary)
            {
                foreach (var w in warnings)
                    w.enabled = false;
                return;
            }
            float bx = arena.BoundaryHalfWidth, by = arena.BoundaryHalfHeight;
            for (int k = 0; k < 2; k++)
            {
                var p = k == 0 ? body0 : body1;
                Warn(k * 4 + 0, bx - half - p.x, new Vector2(bx, p.y), false);
                Warn(k * 4 + 1, bx - half + p.x, new Vector2(-bx, p.y), false);
                Warn(k * 4 + 2, by - half - p.y, new Vector2(p.x, by), true);
                Warn(k * 4 + 3, by - half + p.y, new Vector2(p.x, -by), true);
            }
        }

        void Warn(int index, float distance, Vector2 at, bool alongX)
        {
            var sr = warnings[index];
            if (distance >= WarningDistance)
            {
                sr.enabled = false;
                return;
            }
            float alpha = WarningMaxAlpha * Mathf.Clamp01(1f - distance / WarningDistance);
            StrongestWarning = Mathf.Max(StrongestWarning, alpha);
            sr.enabled = true;
            sr.color = PaletteAsset.WithAlpha(palette.hit, alpha);
            sr.transform.localPosition = at;
            ShapeLibrary.SetSize(sr, alongX ? WarningLength : WarningWidth, alongX ? WarningWidth : WarningLength);
        }

        /// <summary>경계에 닿은 순간: 닿은 점에서 가장자리를 따라 퍼지는 빨강 물결.</summary>
        public void Hit(Vector2 point, Vector2 normal)
        {
            int i = nextRipple;
            nextRipple = (nextRipple + 1) % RippleCount;
            bool alongX = Mathf.Abs(normal.y) > Mathf.Abs(normal.x);
            rippleAge[i] = 0f;
            rippleAlongX[i] = alongX;
            ripples[i].enabled = true;
            ripples[i].transform.localPosition = point;
            Tick(0f);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < RippleCount; i++)
            {
                if (rippleAge[i] >= RippleLife)
                    continue;
                rippleAge[i] += dt;
                float t = Mathf.Clamp01(rippleAge[i] / RippleLife);
                if (t >= 1f)
                {
                    ripples[i].enabled = false;
                    continue;
                }
                float length = RippleLength * (1f - (1f - t) * (1f - t));
                float width = RippleWidth * (1f - 0.5f * t);
                ripples[i].color = PaletteAsset.WithAlpha(palette.hit, 0.9f * (1f - t));
                ShapeLibrary.SetSize(ripples[i], rippleAlongX[i] ? length : width, rippleAlongX[i] ? width : length);
            }
        }
    }
}
