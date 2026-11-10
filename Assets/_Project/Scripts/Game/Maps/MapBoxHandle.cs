using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>Editor placement, never a gameplay collider. Order is explicit and independent of Hierarchy order.</summary>
    [ExecuteAlways]
    public sealed class MapBoxHandle : MonoBehaviour
    {
        [SerializeField] int order;
        [SerializeField,HideInInspector] MapBox original;
        [SerializeField,HideInInspector] Vector3 loadedPosition,loadedScale;
        public int Order=>order;
        public string Id=>original.id;
        public Knockbox.Simulation.TerrainKind Kind=>original.kind;
        public void Initialize(MapBox box,int index)
        {
            original=box.Copy();SetOrder(index);
            loadedPosition=new Vector3((box.minX+box.maxX)*.5f,(box.minY+box.maxY)*.5f,0);
            loadedScale=new Vector3(box.maxX-box.minX,box.maxY-box.minY,1);
            transform.localPosition=loadedPosition;transform.localScale=loadedScale;transform.localRotation=Quaternion.identity;
            RefreshShape();
        }
        public static string ObjectName(int index)=>"Terrain_Box_"+index;
        public void SetOrder(int index){order=index;gameObject.name=ObjectName(index);}
        public MapBox Read()
        {
            var b=original.Copy();
            // Preserves the exact source float bits for an unchanged box.
            if(transform.localPosition.Equals(loadedPosition) && transform.localScale.Equals(loadedScale))return b;
            var p=transform.localPosition;var s=transform.localScale;
            b.minX=p.x-s.x*.5f;b.maxX=p.x+s.x*.5f;b.minY=p.y-s.y*.5f;b.maxY=p.y+s.y*.5f;
            if(b.IsPolygon)for(int i=0;i<b.vertices.Length;i++)
                b.vertices[i]=new Vector2(p.x+(b.vertices[i].x-loadedPosition.x)*s.x/loadedScale.x,p.y+(b.vertices[i].y-loadedPosition.y)*s.y/loadedScale.y);
            if(b.IsPolygon)
            {
                b.minX=b.maxX=b.vertices[0].x;b.minY=b.maxY=b.vertices[0].y;
                foreach(var v in b.vertices){b.minX=Mathf.Min(b.minX,v.x);b.maxX=Mathf.Max(b.maxX,v.x);b.minY=Mathf.Min(b.minY,v.y);b.maxY=Mathf.Max(b.maxY,v.y);}
            }
            return b;
        }
        void OnEnable()=>RefreshShape();
        void Update(){if(!Application.isPlaying)RefreshShape();}
        void RefreshShape()
        {
            var sr=GetComponent<SpriteRenderer>();if(sr==null)return;
            var visual=GetComponent<Knockbox.Presentation.ConvexTerrainSprite>();
            if(original.IsPolygon&&visual!=null&&sr.sprite!=null&&visual.Matches(original.vertices,original.minX,original.minY,original.maxX,original.maxY))return;
            if(original.IsPolygon)
            {
                if(visual==null)visual=gameObject.AddComponent<Knockbox.Presentation.ConvexTerrainSprite>();
                // Transform changes scale the same normalized polygon; only cuts change its topology.
                var verts=original.vertices;var normalized=new Vector2[verts.Length];
                float width=original.maxX-original.minX,height=original.maxY-original.minY;
                if(width<=0||height<=0)return;
                for(int i=0;i<verts.Length;i++)normalized[i]=new Vector2((verts[i].x-original.minX)/width-.5f,(verts[i].y-original.minY)/height-.5f);
                visual.SetNormalized(normalized);
            }
            else if(visual!=null)visual.Clear();
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.white;
            if(original.IsPolygon)
            {
                var b=Read();Gizmos.matrix=Matrix4x4.identity;
                for(int i=0;i<b.vertices.Length;i++)Gizmos.DrawLine(b.vertices[i],b.vertices[(i+1)%b.vertices.Length]);
            }
            else{Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Vector3.zero,Vector3.one);}
        }
    }
}
