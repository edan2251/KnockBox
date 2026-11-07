using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>Editor placement, never a gameplay collider. Order is explicit and independent of Hierarchy order.</summary>
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
            original=box;order=index;
            gameObject.name=index.ToString();
            loadedPosition=new Vector3((box.minX+box.maxX)*.5f,(box.minY+box.maxY)*.5f,0);
            loadedScale=new Vector3(box.maxX-box.minX,box.maxY-box.minY,1);
            transform.localPosition=loadedPosition;transform.localScale=loadedScale;transform.localRotation=Quaternion.identity;
        }
        public MapBox Read()
        {
            var b=original;
            // Preserves the exact source float bits for an unchanged box.
            if(transform.localPosition.Equals(loadedPosition) && transform.localScale.Equals(loadedScale))return b;
            var p=transform.localPosition;var s=transform.localScale;
            b.minX=p.x-s.x*.5f;b.maxX=p.x+s.x*.5f;b.minY=p.y-s.y*.5f;b.maxY=p.y+s.y*.5f;
            return b;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.white;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Vector3.zero,Vector3.one);
        }
    }
}
