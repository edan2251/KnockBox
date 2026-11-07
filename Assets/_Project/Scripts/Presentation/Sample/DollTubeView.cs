using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 외형 후보 R2(8절): IK 체인 튜브. 어깨 → 팔꿈치 → 손(엉덩이 → 무릎 → 발)을 굵기가 변하는 매끈한 튜브 메시로 매 프레임 만들고,
    /// 몸통은 테두리 점마다 스프링이 달린 말랑한 메시(움직이면 살짝 출렁임). 각 부위는 외곽선 메시(잉크, 조금 굵게) 위에 채움 메시를 겹쳐
    /// 마디 사이 이음새 없이 한 덩어리로 보인다. 앞 팔다리는 몸통 앞 층, 뒤 팔다리는 뒤 층이라 팔이 몸통 앞을 지날 때 외곽선으로 구분된다.
    /// </summary>
    public sealed class DollTubeView : MonoBehaviour
    {
        const float Outline = 0.05f;
        const int FanSegments = 18;
        const int TorsoPoints = 24;
        const float Spring = 260f;
        const float Damping = 14f;

        sealed class Layer
        {
            public Mesh Mesh;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();
            public Color Color;
        }

        sealed class Piece
        {
            public Layer Ink;
            public Layer Fill;
        }

        Piece torso, head, eyes;
        readonly Piece[] arms = new Piece[2];
        readonly Piece[] legs = new Piece[2];
        Transform root;
        readonly Vector2[] torsoPoints = new Vector2[TorsoPoints];
        readonly Vector2[] torsoVelocity = new Vector2[TorsoPoints];
        bool torsoReady;

        public void Setup(Color body, Color shade, Color ink)
        {
            root = new GameObject("R2_Root").transform;
            root.SetParent(transform, false);
            Piece Make(string name, string layer, int order, Color fill)
            {
                return new Piece { Ink = MakeLayer(name + " Ink", layer, order, ink), Fill = MakeLayer(name, layer, order + 1, fill) };
            }
            for (int i = 0; i < 2; i++)
            {
                string layer = i == 0 ? SortingLayers.LimbFront : SortingLayers.LimbBack;
                var fill = i == 0 ? body : shade;
                legs[i] = Make($"Leg{i}", layer, 0, fill);
                arms[i] = Make($"Arm{i}", layer, 10, fill);
            }
            torso = Make("Torso", SortingLayers.Fighter, 0, body);
            head = Make("Head", SortingLayers.Fighter, 2, body);
            eyes = Make("Eyes", SortingLayers.Fighter, 6, ink);
        }

        Layer MakeLayer(string name, string sortingLayer, int order, Color color)
        {
            var go = new GameObject(SceneObjectName.Normalize(name));
            go.transform.SetParent(root, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShapeLibrary.ActiveMaterial;
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = order;
            var mesh = new Mesh { name = SceneObjectName.Normalize(name) };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            // 정점 색은 색 공간 변환을 거치지 않으므로 선형 색 공간이면 스프라이트와 같은 색이 되게 미리 바꾼다.
            return new Layer { Mesh = mesh, Color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color };
        }

        public void Render(in DollSkeleton k, float worldAngle, float dt)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, worldAngle);
            RenderTorso(k, dt);
            var headPos = k.Head;
            BuildBoth(head, (layer, grow) => Fan(layer, headPos, DollSkeleton.HeadRadius + grow));
            var forward = (Vector2)(Quaternion.Euler(0f, 0f, k.Angle) * Vector3.right);
            var up = (Vector2)(Quaternion.Euler(0f, 0f, k.Angle) * Vector3.up);
            BuildBoth(eyes, (layer, grow) =>
            {
                if (grow > 0f)
                    return;
                Fan(layer, headPos + forward * 0.12f + up * 0.04f, 0.035f);
                Fan(layer, headPos + up * 0.04f, 0.035f);
            });
            for (int i = 0; i < 2; i++)
            {
                var s = i == 0 ? k.Shoulder0 : k.Shoulder1;
                var e = i == 0 ? k.Elbow0 : k.Elbow1;
                var h = i == 0 ? k.Hand0 : k.Hand1;
                var hip = i == 0 ? k.Hip0 : k.Hip1;
                var kn = i == 0 ? k.Knee0 : k.Knee1;
                var f = i == 0 ? k.Foot0 : k.Foot1;
                float armW = DollSkeleton.ArmThickness * 0.5f;
                float legW = DollSkeleton.LegThickness * 0.5f;
                BuildBoth(arms[i], (layer, grow) =>
                {
                    Tube(layer, s, e, armW * 1.1f + grow, armW + grow);
                    Tube(layer, e, h, armW + grow, armW * 0.85f + grow);
                    Fan(layer, s, armW * 1.1f + grow);
                    Fan(layer, e, armW + grow);
                    Fan(layer, h, DollSkeleton.HandRadius + grow);
                });
                BuildBoth(legs[i], (layer, grow) =>
                {
                    Tube(layer, hip, kn, legW * 1.1f + grow, legW + grow);
                    Tube(layer, kn, f, legW + grow, legW * 0.95f + grow);
                    Fan(layer, hip, legW * 1.1f + grow);
                    Fan(layer, kn, legW + grow);
                    Ellipse(layer, f + new Vector2(0.05f, 0.02f), DollSkeleton.FootWidth * 0.5f + grow, legW * 0.95f + grow);
                });
            }
        }

        delegate void Shape(Layer layer, float grow);

        static void BuildBoth(Piece piece, Shape shape)
        {
            Begin(piece.Ink);
            shape(piece.Ink, Outline);
            End(piece.Ink);
            Begin(piece.Fill);
            shape(piece.Fill, 0f);
            End(piece.Fill);
        }

        /// <summary>몸통: 목표 타원 점을 스프링으로 따라가는 말랑한 테두리.</summary>
        void RenderTorso(in DollSkeleton k, float dt)
        {
            float rot = k.Angle * Mathf.Deg2Rad;
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            float hw = DollSkeleton.TorsoWidth * 0.5f * k.Scale.x, hh = DollSkeleton.TorsoHeight * 0.5f * k.Scale.y;
            dt = Mathf.Clamp(dt, 0f, 1f / 30f);
            for (int i = 0; i < TorsoPoints; i++)
            {
                float a = i * Mathf.PI * 2f / TorsoPoints;
                // 아래가 조금 더 넓은 통통한 배 모양.
                float belly = 1f + 0.08f * Mathf.Max(0f, -Mathf.Sin(a));
                var local = new Vector2(Mathf.Cos(a) * hw * belly, Mathf.Sin(a) * hh);
                var target = k.Torso + new Vector2(local.x * c - local.y * s, local.x * s + local.y * c);
                if (!torsoReady)
                {
                    torsoPoints[i] = target;
                    continue;
                }
                torsoVelocity[i] += ((target - torsoPoints[i]) * Spring - torsoVelocity[i] * Damping) * dt;
                torsoPoints[i] += torsoVelocity[i] * dt;
            }
            torsoReady = true;
            var center = k.Torso;
            BuildBoth(torso, (layer, grow) =>
            {
                int start = layer.Vertices.Count;
                layer.Vertices.Add(center);
                for (int i = 0; i < TorsoPoints; i++)
                {
                    var p = torsoPoints[i];
                    var n = (p - center).normalized;
                    layer.Vertices.Add(p + n * grow);
                }
                for (int i = 0; i < TorsoPoints; i++)
                {
                    layer.Triangles.Add(start);
                    layer.Triangles.Add(start + 1 + (i + 1) % TorsoPoints);
                    layer.Triangles.Add(start + 1 + i);
                }
            });
        }

        static void Begin(Layer layer)
        {
            layer.Vertices.Clear();
            layer.Triangles.Clear();
        }

        static void End(Layer layer)
        {
            layer.Colors.Clear();
            for (int i = 0; i < layer.Vertices.Count; i++)
                layer.Colors.Add(layer.Color);
            layer.Mesh.Clear();
            layer.Mesh.SetVertices(layer.Vertices);
            layer.Mesh.SetColors(layer.Colors);
            layer.Mesh.SetTriangles(layer.Triangles, 0);
            layer.Mesh.RecalculateBounds();
        }

        static void Fan(Layer layer, Vector2 center, float radius) => Ellipse(layer, center, radius, radius);

        static void Ellipse(Layer layer, Vector2 center, float rx, float ry)
        {
            int start = layer.Vertices.Count;
            layer.Vertices.Add(center);
            for (int i = 0; i < FanSegments; i++)
            {
                float a = i * Mathf.PI * 2f / FanSegments;
                layer.Vertices.Add(center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
            }
            for (int i = 0; i < FanSegments; i++)
            {
                layer.Triangles.Add(start);
                layer.Triangles.Add(start + 1 + (i + 1) % FanSegments);
                layer.Triangles.Add(start + 1 + i);
            }
        }

        /// <summary>a(반폭 wa)에서 b(반폭 wb)로 가늘어지는 사다리꼴. 양 끝의 원(Fan)과 합쳐 매끈한 튜브가 된다.</summary>
        static void Tube(Layer layer, Vector2 a, Vector2 b, float wa, float wb)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-8f)
                return;
            var n = new Vector2(-d.y, d.x).normalized;
            int start = layer.Vertices.Count;
            layer.Vertices.Add(a + n * wa);
            layer.Vertices.Add(a - n * wa);
            layer.Vertices.Add(b - n * wb);
            layer.Vertices.Add(b + n * wb);
            layer.Triangles.Add(start);
            layer.Triangles.Add(start + 1);
            layer.Triangles.Add(start + 2);
            layer.Triangles.Add(start);
            layer.Triangles.Add(start + 2);
            layer.Triangles.Add(start + 3);
        }

        void OnDestroy()
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null)
                    Destroy(mf.sharedMesh);
            }
        }
    }
}
