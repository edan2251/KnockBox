using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>The transform is the fixed world anchor. Connection coordinates are unscaled body units.</summary>
    public sealed class MapRopeHandle : MonoBehaviour
    {
        [SerializeField] int order;
        [SerializeField] MapPhysicalBoxHandle targetBox;
        [SerializeField] bool graph;
        [SerializeField] bool sourceIsBody;
        [SerializeField] MapPhysicalBoxHandle sourceBox;
        [SerializeField] Vector2 sourceLocalAnchor;
        public bool Graph {get=>graph;set=>graph=value;}
        public bool SourceIsBody {get=>sourceIsBody;set=>sourceIsBody=value;}
        public MapPhysicalBoxHandle Source {get=>sourceBox;set{sourceBox=value;sourceIsBody=value!=null;}}
        public Vector2 SourceLocalAnchor {get=>sourceLocalAnchor;set=>sourceLocalAnchor=value;}
        public Vector2 Anchor=>graph&&sourceIsBody&&sourceBox!=null?(Vector2)sourceBox.transform.position+sourceLocalAnchor:(Vector2)transform.position;
        [SerializeField] Vector2 localAnchor;
        [SerializeField] float length=4;
        [SerializeField] bool breakable=true;
        [SerializeField] float breakForce=600;
        [SerializeField] Knockbox.Simulation.RopeBreakMode mode;
        [SerializeField] float connectorPosition=.5f;
        [SerializeField] float connectorSize=.55f;
        [SerializeField] float connectorSpeedLoss=.15f;
        [SerializeField,HideInInspector] MapRope original;
        [SerializeField,HideInInspector] Vector3 loadedPosition;
        public int Order=>order;
        public string Id=>original.id;
        public MapPhysicalBoxHandle Target {get=>targetBox;set=>targetBox=value;}
        public Vector2 LocalAnchor {get=>localAnchor;set=>localAnchor=value;}
        public float Length {get=>length;set=>length=value;}
        public bool Breakable {get=>breakable;set=>breakable=value;}
        public float BreakForce {get=>breakForce;set=>breakForce=value;}
        public Knockbox.Simulation.RopeBreakMode Mode {get=>mode;set=>mode=value;}
        public float ConnectorPosition {get=>connectorPosition;set=>connectorPosition=value;}
        public float ConnectorSize {get=>connectorSize;set=>connectorSize=value;}
        public float ConnectorSpeedLoss {get=>connectorSpeedLoss;set=>connectorSpeedLoss=value;}
        public Vector2 ConnectorPoint
        {
            get
            {
                var a=Anchor;var b=Connection;
                var p=Knockbox.Simulation.RopeGeometry.Point(new Knockbox.Simulation.Vec2(a.x,a.y),
                    new Knockbox.Simulation.Vec2(b.x,b.y),length,connectorPosition);
                return new Vector2(p.X,p.Y);
            }
        }
        public Vector2 Connection=>targetBox==null?(Vector2)transform.position:(Vector2)targetBox.transform.position+localAnchor;
        public static string ObjectName(int index)=>"Rope_"+index;
        public void SetOrder(int index){order=index;gameObject.name=ObjectName(index);}
        public void Initialize(MapRope rope,int index,MapPhysicalBoxHandle box,MapPhysicalBoxHandle source=null)
        {
            original=rope;graph=rope.graph;sourceBox=source;sourceIsBody=!string.IsNullOrEmpty(rope.sourceBoxId);sourceLocalAnchor=rope.sourceLocalAnchor;targetBox=box;localAnchor=rope.localAnchor;length=rope.length;breakable=rope.breakable;breakForce=rope.breakForce;
            mode=rope.mode;connectorPosition=rope.connectorPosition;connectorSize=rope.connectorSize;connectorSpeedLoss=rope.connectorSpeedLoss;
            SetOrder(index);loadedPosition=new Vector3(rope.anchor.x,rope.anchor.y,0);
            transform.position=loadedPosition;transform.rotation=Quaternion.identity;transform.localScale=Vector3.one;
        }
        public MapRope Read()
        {
            var r=original;if(targetBox!=null)r.boxId=targetBox.Id;
            if(!transform.position.Equals(loadedPosition))r.anchor=transform.position;
            r.graph=graph;r.sourceBoxId=sourceIsBody?(sourceBox!=null?sourceBox.Id:original.sourceBoxId??"missing_source"):null;r.sourceLocalAnchor=sourceLocalAnchor;
            r.localAnchor=localAnchor;r.length=length;r.breakable=breakable;r.breakForce=breakForce;
            r.mode=mode;r.connectorPosition=connectorPosition;r.connectorSize=connectorSize;r.connectorSpeedLoss=connectorSpeedLoss;return r;
        }
        readonly Vector2[] preview=new Vector2[Knockbox.Presentation.RopePath.Segments+1];
        void OnDrawGizmos()
        {
            Gizmos.color=targetBox==null?Color.red:Knockbox.Presentation.PaletteAsset.RopeColor;
            Gizmos.DrawSphere(Anchor,.10f);
            Knockbox.Presentation.RopePath.Fill(Anchor,Connection,length,preview);
            var matrix=Gizmos.matrix;
            for(int i=1;i<preview.Length;i++)
            {
                var delta=preview[i]-preview[i-1];
                Gizmos.matrix=Matrix4x4.TRS((preview[i]+preview[i-1])*.5f,
                    Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
                Gizmos.DrawCube(Vector3.zero,new Vector3(delta.magnitude,Knockbox.Presentation.PropView.RopeWidth,.01f));
            }
            Gizmos.matrix=matrix;
            Gizmos.DrawWireSphere(Connection,.08f);
            if(!graph && mode==Knockbox.Simulation.RopeBreakMode.Connector)
            {
                Gizmos.color=Knockbox.Presentation.PropView.ConnectorColor;
                Gizmos.DrawWireCube(ConnectorPoint,new Vector3(connectorSize,connectorSize,.01f));
            }
        }
    }
}
