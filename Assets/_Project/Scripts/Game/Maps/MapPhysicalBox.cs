using System;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>물리 상자의 원본 배치. 회전/속도/휴면은 경기 중 PropState에서만 관리한다.</summary>
    [Serializable]
    public struct MapPhysicalBox
    {
        public string id;
        public Vector2 center;
        public Vector2 size;
        public float mass;
        public bool playerPassThrough;
        public bool connector;
        public float connectorSpeedLoss;
        public MapBox Bounds => new MapBox { id=id, kind=TerrainKind.Solid,
            minX=center.x-size.x*.5f, maxX=center.x+size.x*.5f,
            minY=center.y-size.y*.5f, maxY=center.y+size.y*.5f };
        public PropDef ToProp(int order) => new PropDef { Id=id, Kind=PropKind.LooseBox, Group=order,
            Center=new Vec2(center.x,center.y), HalfSize=new Vec2(size.x*.5f,size.y*.5f),
            Mass=mass, StartAwake=true, PlayerPassThrough=playerPassThrough, AnchorProp=-1, IsConnector=connector, ConnectorSpeedLoss=connectorSpeedLoss };
    }
}
