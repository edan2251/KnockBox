using System.Collections.Generic;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 결과 공간 세트 점수 숫자 발판의 그림(2026-11-20, 사용자 확정: 점수만 매단다, 왼쪽 P1·오른쪽 P2).
    /// 결과 맵의 모양 있는 물리 몸(점수 숫자 0~5 양쪽, 콜론 두 점)마다 선수와 닿는 조각(PropDef.Shape)을 그대로 그리고,
    /// 남은(점수와 맞는) 몸의 실제 위치·회전을 따라간다. 낙하·'툭'·흔들림은 물리 로프가, 줄은 구조물 그림(PropView)이,
    /// 그림자는 투영 그림자(PropShadowView)가 맡는다. 판정과 무관하다.
    /// </summary>
    public sealed class ResultSign
    {
        sealed class Body
        {
            public int Index;
            public Transform Root;
            public bool Alive = true;
        }

        readonly Transform root;
        readonly ShapeLibrary shapes;
        readonly PaletteAsset palette;
        readonly List<Body> bodies = new List<Body>();
        ArenaDef bodiesArena;
        public bool Visible { get; private set; }

        public ResultSign(Transform parent, ShapeLibrary shapes, PaletteAsset palette)
        {
            this.shapes = shapes; this.palette = palette;
            root = new GameObject("Result_sign").transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (!Visible) return;
            Visible = false;
            root.gameObject.SetActive(false);
        }

        void Show()
        {
            if (Visible) return;
            Visible = true;
            root.gameObject.SetActive(true);
        }

        public bool HasBodies => bodies.Count > 0;

        /// <summary>
        /// 모양 있는 몸마다 조각을 만든다. P1 점수는 P1 색, P2 점수는 P2 색, 콜론은 밝은 회색. 맵(경기 설정)이 바뀔 때만 다시 만든다.
        /// </summary>
        public void BuildBodies(ArenaDef arena)
        {
            if (arena == bodiesArena) return;
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
            bodies.Clear();
            bodiesArena = arena;
            var neutral = PaletteAsset.Scaled(PaletteAsset.Neutral, .86f);
            for (int b = 0; b < arena.DynamicPropCount; b++)
            {
                ref readonly var def = ref arena.GetProp(arena.DynamicProp(b));
                if (!def.HasShape) continue;
                var color = def.ScoreSlot == 1 ? palette.FighterBody(0) : def.ScoreSlot == 2 ? palette.FighterBody(1) : neutral;
                var body = new Body { Index = b, Root = new GameObject("Score_body_" + b).transform };
                body.Root.SetParent(root, false);
                foreach (var piece in def.Shape)
                {
                    var sr = ShapeLibrary.Create(body.Root, "Glyph_piece", shapes.Square, SortingLayers.Terrain, 4, color);
                    var min = new Vector2(piece.Min.X, piece.Min.Y); var max = new Vector2(piece.Max.X, piece.Max.Y);
                    sr.transform.localPosition = (min + max) * .5f;
                    sr.transform.localScale = new Vector3(max.x - min.x, max.y - min.y, 1);
                    sr.gameObject.AddComponent<ConvexTerrainSprite>().SetPolygon(piece);
                }
                bodies.Add(body);
            }
        }

        /// <summary>남은 몸의 지금 그려진 위치·회전에 조각을 맞춘다. 몸 상자 숨김·켜고 끄기는 바뀔 때만 한다.</summary>
        public void PresentBodies(PropView view, in PropState state)
        {
            Show();
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                // 구조물 그림이 맵·테마를 다시 만들면 숨김이 풀리므로 풀렸을 때만 다시 숨긴다.
                if (!view.BodyHidden(body.Index)) view.HideBody(body.Index, true);
                bool alive = body.Index < state.Count && !state.IsDestroyed(body.Index);
                if (alive != body.Alive) { body.Alive = alive; body.Root.gameObject.SetActive(alive); }
                if (!alive) continue;
                var t = view.BodyTransform(body.Index);
                body.Root.localPosition = (Vector2)root.InverseTransformPoint(t.position);
                body.Root.localRotation = Quaternion.Euler(0, 0, t.eulerAngles.z - root.eulerAngles.z);
            }
        }
    }
}
