using System;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    [Serializable]
    public struct MapRope
    {
        public string id, boxId;
        public bool graph;
        public string sourceBoxId;
        public Vector2 sourceLocalAnchor;
        public Vector2 anchor, localAnchor;
        public float length;
        public bool breakable;
        public float breakForce;
        public RopeBreakMode mode;
        public float connectorPosition, connectorSize, connectorSpeedLoss;
        public AuthoredRope ToRope()=>new AuthoredRope{Enabled=true,Id=id,
            WorldAnchor=new Vec2(anchor.x,anchor.y),LocalAnchor=new Vec2(localAnchor.x,localAnchor.y),
            Length=length,Breakable=breakable,BreakForce=breakForce,RopeMode=mode,
            ConnectorPosition=connectorPosition,ConnectorSize=connectorSize,ConnectorSpeedLoss=connectorSpeedLoss};
    }
}
