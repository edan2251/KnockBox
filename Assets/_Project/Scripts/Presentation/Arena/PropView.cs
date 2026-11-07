using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 구조물 그리기(09-props-physics.md 6절, 08-visual-direction.md 2절). 테마의 `prop` 색 단색, 줄은 같은 색의 가는 선,
    /// 축과 경첩은 작은 점. 상자(기둥, 흩어진 상자)는 상자마다 밝기 ±6% 이내로 다르고 상자 사이에 얇고 어두운 이음매를 둔다
    /// (CP16부터 상자도 강체: 이음매는 상자마다 아랫변에 붙어 함께 움직인다).
    /// 아직 물리가 없는 구조물(임시 물체)은 맵을 그릴 때 한 번 놓고, 강체 구조물(CP14부터)은 매 프레임 직전·현재 상태를
    /// 보간해(회전은 (cos, sin)을 보간해 정규화) 움직이며 줄이 따라간다.
    /// </summary>
    public sealed class PropView : MonoBehaviour
    {
        public const float RopeWidth = 0.06f;
        const float PivotSize = 0.24f;
        const float SeamHeight = 0.07f;
        /// <summary>상자 밝기 차의 최대 비율(08 2절: ±6% 이내).</summary>
        public const float BoxShadeRange = 0.06f;
        const float SeamDarkness = 0.55f;
        const float PivotDarkness = 0.6f;

        ArenaDef current;
        string currentTheme;
        readonly SpriteRenderer[] bodies = new SpriteRenderer[PropState.Capacity];
        readonly SpriteRenderer[] ropes = new SpriteRenderer[PropState.Capacity];
        /// <summary>움직이는 상자의 아랫변 이음매(상자를 따라 돌고, 쌓이면 위아래 상자 사이의 어두운 선이 된다). 상자가 아니면 null.</summary>
        readonly SpriteRenderer[] seams = new SpriteRenderer[PropState.Capacity];
        readonly Vector2[] halves = new Vector2[PropState.Capacity];
        int dynamicCount;

        /// <summary>강체 구조물 i가 지금 그려진 위치·회전(그림자, 테스트·캡처 확인용).</summary>
        public Vector2 BodyPosition(int body) => bodies[body].transform.localPosition;
        public Transform BodyTransform(int body) => bodies[body].transform;

        public void Build(ArenaDef arena, ArenaTheme theme, ShapeLibrary shapes)
        {
            if (arena == current && theme.name == currentTheme)
                return;
            current = arena;
            currentTheme = theme.name;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            // 임시 물체(아직 움직이지 않는 단계의 구조물).
            for (int box = arena.PropBoxStart; box < arena.BoxCount; box++)
            {
                int i = arena.PropOfBox(box);
                ref readonly var p = ref arena.GetProp(i);
                bool isBox = p.Kind == PropKind.BoxPillar || p.Kind == PropKind.LooseBox;
                var color = isBox ? BoxShade(theme.prop, i) : theme.prop;
                Rect(shapes, color, 3, p.MinX, p.MinY, p.MaxX, p.MaxY);
                if (p.HangsByRope)
                    Line(Rope(shapes, theme.prop), ToVector(p.Anchor), new Vector2(p.Center.X, p.MaxY));
                else if (p.HasAnchor)
                    Dot(shapes, PaletteAsset.Scaled(theme.prop, PivotDarkness), 5, ToVector(p.Anchor), PivotSize);

                // 기둥의 위아래 상자 사이 이음매.
                if (p.Kind == PropKind.BoxPillar && i + 1 < arena.PropCount)
                {
                    ref readonly var above = ref arena.GetProp(i + 1);
                    if (above.Group == p.Group && Mathf.Abs(above.MinY - p.MaxY) < 1e-3f)
                    {
                        float half = Mathf.Min(p.HalfSize.X, above.HalfSize.X);
                        Rect(shapes, PaletteAsset.Scaled(theme.prop, SeamDarkness), 4, p.Center.X - half, p.MaxY - SeamHeight * 0.5f, p.Center.X + half, p.MaxY + SeamHeight * 0.5f);
                    }
                }
            }

            // 강체 구조물: 몸 스프라이트(가운데 기준, 회전), 줄 또는 축 점.
            dynamicCount = arena.DynamicPropCount;
            for (int b = 0; b < dynamicCount; b++)
            {
                ref readonly var p = ref arena.GetProp(arena.DynamicProp(b));
                ref readonly var joint = ref arena.GetJoint(b);
                bool isBox = p.Kind == PropKind.BoxPillar || p.Kind == PropKind.LooseBox;
                bodies[b] = ShapeLibrary.Create(transform, "PropBody", shapes.Square, SortingLayers.Terrain, 3,
                    isBox ? BoxShade(theme.prop, arena.DynamicProp(b)) : theme.prop);
                ShapeLibrary.SetSize(bodies[b], p.HalfSize.X * 2f, p.HalfSize.Y * 2f);
                bodies[b].transform.localPosition = ToVector(p.Center);
                halves[b] = ToVector(p.HalfSize);
                seams[b] = null;
                if (isBox)
                {
                    seams[b] = ShapeLibrary.Create(transform, "BoxSeam", shapes.Square, SortingLayers.Terrain, 4, PaletteAsset.Scaled(theme.prop, SeamDarkness));
                    ShapeLibrary.SetSize(seams[b], p.HalfSize.X * 2f, SeamHeight);
                    seams[b].transform.localPosition = ToVector(p.Center) + new Vector2(0f, -p.HalfSize.Y);
                }
                ropes[b] = joint.Kind == PropJointKind.Rope ? Rope(shapes, theme.prop) : null;
                if (joint.Pinned)
                    Dot(shapes, PaletteAsset.Scaled(theme.prop, PivotDarkness), 5, ToVector(joint.WorldAnchor), PivotSize);
            }
        }

        /// <summary>강체 구조물을 직전·현재 상태 사이로 보간해 놓는다(연출 시간, 할당 없음).</summary>
        public void Render(in PropState previous, in PropState state, float alpha)
        {
            if (current == null)
                return;
            int count = Mathf.Min(dynamicCount, state.Count);
            for (int b = 0; b < count; b++)
            {
                ref readonly var now = ref PropState.At(in state, b);
                var before = b < previous.Count ? PropState.At(in previous, b) : now;
                Interpolate(before, now, alpha, out var position, out var rotation);
                var tr = bodies[b].transform;
                tr.localPosition = position;
                tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(rotation.y, rotation.x) * Mathf.Rad2Deg);
                if (seams[b] != null)
                {
                    var seam = seams[b].transform;
                    seam.localPosition = position + Rotate(rotation, new Vec2(0f, -halves[b].y));
                    seam.localRotation = tr.localRotation;
                }
                if (ropes[b] != null)
                {
                    ref readonly var joint = ref current.GetJoint(b);
                    // 끊어진 줄은 그리지 않는다(CP15).
                    ropes[b].enabled = now.Broken == 0;
                    if (now.Broken != 0)
                        continue;
                    var end = position + Rotate(rotation, joint.LocalAnchor);
                    var start = ToVector(joint.WorldAnchor);
                    if (joint.OtherBody >= 0)
                    {
                        // 다른 몸에 이어진 줄(연결 구조): 그 몸이 지금 그려진 자리에서 시작.
                        var other = bodies[joint.OtherBody].transform;
                        float angle = other.localEulerAngles.z * Mathf.Deg2Rad;
                        start = (Vector2)other.localPosition + Rotate(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), joint.OtherLocal);
                    }
                    Line(ropes[b], start, end);
                }
            }
        }

        /// <summary>위치는 선형 보간, 회전은 (cos, sin)을 보간해 정규화(09 6절).</summary>
        public static void Interpolate(in PropBody a, in PropBody b, float t, out Vector2 position, out Vector2 rotation)
        {
            position = Vector2.LerpUnclamped(ToVector(a.Position), ToVector(b.Position), t);
            rotation = Vector2.LerpUnclamped(ToVector(a.Rotation), ToVector(b.Rotation), t);
            float length = rotation.magnitude;
            rotation = length > 1e-5f ? rotation / length : Vector2.right;
        }

        /// <summary>상자마다 정해진 밝기 배율(1 ± 0.06 이내). 번호로 정하므로 매번 같다.</summary>
        public static Color BoxShade(Color prop, int index)
        {
            float k = ((index * 37 + 11) % 13) / 12f * 2f - 1f;
            return PaletteAsset.Scaled(prop, 1f + BoxShadeRange * k);
        }

        static Vector2 ToVector(Vec2 v) => new Vector2(v.X, v.Y);

        static Vector2 Rotate(Vector2 q, Vec2 local) => new Vector2(q.x * local.X - q.y * local.Y, q.y * local.X + q.x * local.Y);

        void Rect(ShapeLibrary shapes, Color color, int order, float minX, float minY, float maxX, float maxY)
        {
            var sr = ShapeLibrary.Create(transform, "Prop", shapes.Square, SortingLayers.Terrain, order, color);
            sr.transform.localPosition = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            ShapeLibrary.SetSize(sr, maxX - minX, maxY - minY);
        }

        SpriteRenderer Rope(ShapeLibrary shapes, Color color) =>
            ShapeLibrary.Create(transform, "Rope", shapes.Square, SortingLayers.Terrain, 1, color);

        static void Line(SpriteRenderer sr, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            sr.transform.localPosition = (Vector3)((from + to) * 0.5f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            ShapeLibrary.SetSize(sr, delta.magnitude, RopeWidth);
        }

        void Dot(ShapeLibrary shapes, Color color, int order, Vector2 at, float size)
        {
            var sr = ShapeLibrary.Create(transform, "Pivot", shapes.Circle, SortingLayers.Terrain, order, color);
            sr.transform.localPosition = at;
            ShapeLibrary.SetSize(sr, size, size);
        }
    }
}
