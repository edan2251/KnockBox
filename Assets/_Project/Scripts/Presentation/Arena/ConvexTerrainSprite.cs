using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;
using Unity.Collections;

namespace Knockbox.Presentation
{
    /// <summary>A solid sprite generated from saved normalized convex vertices; uses the ordinary terrain lighting/transition path.</summary>
    [ExecuteAlways,RequireComponent(typeof(SpriteRenderer))]
    public sealed class ConvexTerrainSprite : MonoBehaviour
    {
        [SerializeField,HideInInspector] Vector2[] vertices;
        [SerializeField,HideInInspector] Sprite rectangularSprite;
        Sprite generated;
        public void SetPolygon(ConvexPolygon polygon)
        {
            var min=polygon.Min;var size=polygon.Max-min;
            var normalized=new Vector2[polygon.Count];
            for(int i=0;i<normalized.Length;i++){var p=polygon.Point(i);normalized[i]=new Vector2((p.X-min.X)/size.X-.5f,(p.Y-min.Y)/size.Y-.5f);}
            SetNormalized(normalized);
        }
        public void SetNormalized(Vector2[] next)
        {
            bool same=vertices!=null&&vertices.Length==next.Length;
            if(same)for(int i=0;i<next.Length;i++)if(vertices[i]!=next[i]){same=false;break;}
            if(same&&generated!=null){GetComponent<SpriteRenderer>().sprite=generated;return;}
            vertices=(Vector2[])next.Clone();Rebuild();
        }
        public bool Matches(Vector2[] points,float minX,float minY,float maxX,float maxY)
        {
            if(generated==null||vertices==null||vertices.Length!=points.Length)return false;
            for(int i=0;i<points.Length;i++)
                if(vertices[i]!=new Vector2((points[i].x-minX)/(maxX-minX)-.5f,(points[i].y-minY)/(maxY-minY)-.5f))return false;
            return true;
        }
        public void Clear()
        {vertices=null;GetComponent<SpriteRenderer>().sprite=rectangularSprite;Release();}
        void OnEnable(){if(vertices!=null&&vertices.Length>=3)Rebuild();}
        void Rebuild()
        {
            var sr=GetComponent<SpriteRenderer>();if(rectangularSprite==null&&sr.sprite!=generated)rectangularSprite=sr.sprite;
            Release();var texture=Texture2D.whiteTexture;
            generated=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f,texture.width,0,SpriteMeshType.Tight);
            generated.name="Convex_Terrain_Sprite";generated.hideFlags=HideFlags.HideAndDontSave;
            var indices=new ushort[(vertices.Length-2)*3];
            for(int i=0;i<vertices.Length-2;i++){indices[i*3]=0;indices[i*3+1]=(ushort)(i+1);indices[i*3+2]=(ushort)(i+2);}
            // Unity 6000.3 rejects OverrideGeometry on the generated sprite in EditMode.
            // Populate the documented sprite vertex channels directly; no mesh/shader fork.
            generated.SetVertexCount(vertices.Length);
            var positions=new NativeArray<Vector3>(vertices.Length,Allocator.Temp);
            var uv=new NativeArray<Vector2>(vertices.Length,Allocator.Temp);
            var triangles=new NativeArray<ushort>(indices,Allocator.Temp);
            try
            {
                for(int i=0;i<vertices.Length;i++){positions[i]=vertices[i];uv[i]=Vector2.one*.5f;}
                generated.SetVertexAttribute(VertexAttribute.Position,positions);
                generated.SetVertexAttribute(VertexAttribute.TexCoord0,uv);
                generated.SetIndices(triangles);
            }
            finally{positions.Dispose();uv.Dispose();triangles.Dispose();}
            sr.drawMode=SpriteDrawMode.Simple;sr.sprite=generated;
        }
        void Release(){if(generated==null)return;if(Application.isPlaying)Destroy(generated);else DestroyImmediate(generated);generated=null;}
        void OnDestroy()=>Release();
    }
}
