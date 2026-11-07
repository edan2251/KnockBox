using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>R5의 흰 굴곡 경계와 명판. 텍스처 없이 편집 가능한 UI 메시로 만든다.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrontendGraphic : MaskableGraphic
    {
        public enum Shape { Split, NameLeft, NameRight }
        public Shape shape;
        public Color left = PaletteAsset.FrontendDefaults.Left;
        public Color right = PaletteAsset.FrontendDefaults.Right;
        public Color line = PaletteAsset.Neutral;
        public float thickness = 22f;
        public float center = .5f;
        float dividerSlant = .10f;
        public float DividerSlant => dividerSlant;
        public void SetDividerSlant(float value)
        {
            value = Mathf.Clamp(value, .02f, .14f);
            if (Mathf.Abs(dividerSlant - value) < .00001f) return;
            dividerSlant = value; SetVerticesDirty();
        }
        float opening;
        public float Opening => opening;
        /// <summary>-1은 양쪽(VS/결과), 0/1은 카드 선택자의 패널만 그린다.</summary>
        public int SingleSide { get; private set; } = -1;
        public void SetSingleSide(int slot)
        {
            slot = Mathf.Clamp(slot, -1, 1);
            if (SingleSide == slot) return;
            SingleSide = slot; SetVerticesDirty();
        }
        // 각 패널은 자기 폭만큼 이동한다. 비대칭 구도도 같은 순간에 목표 경계에서 만난다.
        public float LeftEdge(float t) => Divider(t) - opening * (center + .14f);
        public float RightEdge(float t) => Divider(t) + opening * (1 - center + .14f);
        public void SetOpening(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(opening - value) < .00001f) return;
            opening = value; SetVerticesDirty();
        }

        public void SetCenter(float value)
        {
            value = Mathf.Clamp(value, .08f, .92f);
            if (Mathf.Abs(center - value) < .00001f) return;
            center = value; SetVerticesDirty();
        }

        public void SetSides(Color a, Color b)
        {
            if (left == a && right == b) return;
            left = a; right = b; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (shape != Shape.Split)
            {
                float slant = r.height * .17f;
                bool mirrored = shape == Shape.NameLeft;
                Quad(vh, r, 0, 0, r.width, r.height, line, slant, mirrored);
                var inner = new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8);
                Quad(vh, inner, 0, 0, inner.width, inner.height, color, slant, mirrored);
                return;
            }
            const int segments = 96;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments, t1 = (i + 1) / (float)segments;
                float l0 = LeftEdge(t0) * r.width + r.x, l1 = LeftEdge(t1) * r.width + r.x;
                float r0 = RightEdge(t0) * r.width + r.x, r1 = RightEdge(t1) * r.width + r.x;
                float y0 = r.yMax - t0 * r.height, y1 = r.yMax - t1 * r.height;
                float half = thickness * .5f;
                // 카드에서는 상대 패널과 그 테두리를 모두 생략해 원래 경기 배경이 보이게 한다.
                if (SingleSide != 1)
                {
                    Add(vh, new Vector2(r.xMin-r.width,y0),new Vector2(l0+half,y0),new Vector2(l1+half,y1),new Vector2(r.xMin-r.width,y1),left);
                    Add(vh,new Vector2(l0-half,y0),new Vector2(l0+half,y0),new Vector2(l1+half,y1),new Vector2(l1-half,y1),line);
                }
                if (SingleSide != 0)
                {
                    Add(vh, new Vector2(r0-half,y0),new Vector2(r.xMax+r.width,y0),new Vector2(r.xMax+r.width,y1),new Vector2(r1-half,y1),right);
                    Add(vh,new Vector2(r0-half,y0),new Vector2(r0+half,y0),new Vector2(r1+half,y1),new Vector2(r1-half,y1),line);
                }
            }
        }

        // 위는 오른쪽, 아래는 왼쪽인 한 직선. 양 패널에 같은 경계를 사용해 닫혔을 때 흰 선이 정확히 겹친다.
        float Divider(float t) => center + dividerSlant * (1 - 2 * t);

        static void Quad(VertexHelper vh, Rect r, float x, float y, float w, float h, Color c, float s, bool mirror)
        {
            Add(vh, new Vector2(r.x+x+(mirror?0:s),r.y+y),new Vector2(r.x+x+w-(mirror?s:0),r.y+y),
                new Vector2(r.x+x+w,r.y+y+h),new Vector2(r.x+x,r.y+y+h),c);
        }

        static void Add(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int n = vh.currentVertCount;
            vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddVert(d,color,Vector2.zero);
            vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
        }
    }
}
