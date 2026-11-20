using System;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>광원에서 본 사각형 실루엣을 연장하는 공통 그림자 메시. 배열/메시는 한 번 만들고 재사용한다.</summary>
    public sealed class ProjectedShadowMesh : IDisposable
    {
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Vector3[] vertices;
        readonly Color[] colors;
        bool colorsDirty;
        public int Count { get; private set; }

        public ProjectedShadowMesh(GameObject owner, int capacity, Material material, int order)
        {
            var filter = owner.GetComponent<MeshFilter>();
            if (filter == null) filter = owner.AddComponent<MeshFilter>();
            renderer = owner.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = owner.AddComponent<MeshRenderer>();
            mesh = new Mesh { name = owner.name + " projection" };
            mesh.MarkDynamic();
            vertices = new Vector3[capacity * 4];
            colors = new Color[vertices.Length];
            var triangles = new int[capacity * 6];
            for (int i = 0; i < colors.Length; i++) colors[i] = PaletteAsset.Neutral;
            for (int q = 0; q < capacity; q++)
            {
                int v = q * 4, t = q * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            filter.sharedMesh = mesh;
            renderer.sortingLayerName = SortingLayers.Background;
            renderer.sortingOrder = order;
            SetMaterial(material);
        }

        public void SetMaterial(Material material) => renderer.sharedMaterial = material;
        public void Begin() => Count = 0;

        /// <summary>마지막에 넣은 그림자 하나의 진하기(0~1, 기본 1). 셰이더가 정점 알파로 그림자 어둡기를 줄인다.</summary>
        public void SetLastStrength(float strength)
        {
            if (Count == 0) return;
            int q = (Count - 1) * 4;
            float a = Mathf.Clamp01(strength);
            for (int k = 0; k < 4; k++)
            {
                if (colors[q + k].a == a) continue;
                var c = colors[q + k]; c.a = a; colors[q + k] = c; colorsDirty = true;
            }
        }
        public void Box(Vector2 center, Vector2 half, float angle, Vector2 light, float reach)
        {
            var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var v = new Vector2(-u.y, u.x);
            Vector2 delta = light - center;
            if (Mathf.Abs(Vector2.Dot(delta, u)) <= half.x && Mathf.Abs(Vector2.Dot(delta, v)) <= half.y) return;
            var hx = u * half.x; var hy = v * half.y;
            var left = center - hx - hy; var right = left;
            Consider(light, center + hx - hy, ref left, ref right);
            Consider(light, center + hx + hy, ref left, ref right);
            Consider(light, center - hx + hy, ref left, ref right);
            int q = Count++ * 4;
            vertices[q] = left; vertices[q + 1] = right;
            vertices[q + 2] = right + (right - light).normalized * reach;
            vertices[q + 3] = left + (left - light).normalized * reach;
            SetLastStrength(1f);
        }

        public void Polygon(Knockbox.Simulation.ConvexPolygon polygon,Vector2 light,float reach)
        {
            if(polygon.Contains(new Knockbox.Simulation.Vec2(light.x,light.y)))return;
            var p=polygon.Point(0);Vector2 left=new Vector2(p.X,p.Y),right=left;
            for(int i=1;i<polygon.Count;i++){p=polygon.Point(i);Consider(light,new Vector2(p.X,p.Y),ref left,ref right);}
            int q=Count++*4;vertices[q]=left;vertices[q+1]=right;
            vertices[q+2]=right+(right-light).normalized*reach;vertices[q+3]=left+(left-light).normalized*reach;
            SetLastStrength(1f);
        }

        /// <summary>
        /// 볼록 다각형(월드 좌표 꼭짓점 count개)의 그림자. 움직이는 조형물(결과 글자 조각, 2026-11-20)처럼 매 프레임 바뀌는 모양에 쓴다. 할당 없음.
        /// </summary>
        /// <returns>그림자를 넣었는지(광원이 안쪽이거나 칸이 모자라면 거짓).</returns>
        public bool Points(Vector2[] points, int count, Vector2 light, float reach)
        {
            if (count < 3 || Count * 4 >= vertices.Length) return false;
            Vector2 left = points[0], right = left;
            bool inside = true;
            for (int i = 0; i < count; i++)
            {
                var a = points[i]; var b = points[(i + 1) % count];
                if ((b.x - a.x) * (light.y - a.y) - (b.y - a.y) * (light.x - a.x) < 0) inside = false;
                if (i > 0) Consider(light, a, ref left, ref right);
            }
            if (inside) return false;
            int q = Count++ * 4;
            vertices[q] = left; vertices[q + 1] = right;
            vertices[q + 2] = right + (right - light).normalized * reach;
            vertices[q + 3] = left + (left - light).normalized * reach;
            SetLastStrength(1f);
            return true;
        }

        static void Consider(Vector2 light, Vector2 p, ref Vector2 left, ref Vector2 right)
        {
            var dl = left - light; var dr = right - light; var dp = p - light;
            if (dl.x * dp.y - dl.y * dp.x > 0) left = p;
            if (dr.x * dp.y - dr.y * dp.x < 0) right = p;
        }

        public void Commit()
        {
            Array.Clear(vertices, Count * 4, vertices.Length - Count * 4);
            mesh.vertices = vertices;
            if (colorsDirty) { mesh.colors = colors; colorsDirty = false; }
        }
        public void Dispose()
        {
            if(mesh==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
