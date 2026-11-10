using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>제작 전용 배치 핸들. 게임 물리는 정의에서 생성된 PropState만 사용한다.</summary>
    public sealed class MapPhysicalBoxHandle : MonoBehaviour
    {
        [SerializeField] int order;
        [SerializeField] float mass=2.25f;
        [SerializeField] bool playerPassThrough;
        [SerializeField] bool connector;
        [SerializeField] float connectorSpeedLoss=.15f;
        public bool Connector {get=>connector;set=>connector=value;}
        public float ConnectorSpeedLoss {get=>connectorSpeedLoss;set=>connectorSpeedLoss=value;}
        [SerializeField,HideInInspector] MapPhysicalBox original;
        [SerializeField,HideInInspector] Vector3 loadedPosition,loadedScale;
        public int Order=>order;
        public string Id=>original.id;
        public float Mass { get=>mass; set=>mass=value; }
        public bool PlayerPassThrough { get=>playerPassThrough; set=>playerPassThrough=value; }
        public void Initialize(MapPhysicalBox box,int index)
        {
            original=box;connector=box.connector;connectorSpeedLoss=box.connectorSpeedLoss;mass=box.mass;playerPassThrough=box.playerPassThrough;SetOrder(index);
            loadedPosition=new Vector3(box.center.x,box.center.y,0);loadedScale=new Vector3(box.size.x,box.size.y,1);
            transform.localPosition=loadedPosition;transform.localScale=loadedScale;transform.localRotation=Quaternion.identity;
        }
        public static string ObjectName(int index)=>"Physics_Box_"+index;
        public void SetOrder(int index){order=index;gameObject.name=ObjectName(index);}
        public MapPhysicalBox Read()
        {
            var b=original;b.connector=connector;b.connectorSpeedLoss=connectorSpeedLoss;b.mass=mass;b.playerPassThrough=playerPassThrough;
            if(!transform.localPosition.Equals(loadedPosition))b.center=transform.localPosition;
            if(!transform.localScale.Equals(loadedScale))b.size=transform.localScale;
            return b;
        }
        void OnDrawGizmosSelected()
        {Gizmos.color=Color.cyan;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Vector3.zero,Vector3.one);}
    }
}
