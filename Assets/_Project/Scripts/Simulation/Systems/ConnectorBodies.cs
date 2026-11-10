namespace Knockbox.Simulation
{
    /// <summary>Attackable physical nodes. Resolve before solid body contacts so a thrown body never wall-splats on a destroyed node.</summary>
    public static class ConnectorBodies
    {
        public static void Resolve(ref SimState s,ArenaDef arena,SimConfig config,
            in FighterState previous0,in FighterState previous1,bool frozen0,bool frozen1,PropScratch scratch,SimEventBuffer events)
        {
            var f0=s.Fighter0;var f1=s.Fighter1;
            float keep0=1,keep1=1;
            for(int i=0;i<s.Props.Count;i++)
            {
                ref readonly var def=ref arena.GetProp(arena.DynamicProp(i));
                if(!def.IsConnector||s.Props.IsDestroyed(i))continue;
                var b=PropState.At(s.Props,i);
                float t0=Hit(previous0,f0,frozen0,b,scratch.StartPosition[i],def.HalfSize,config,out bool travel0);
                float t1=Hit(previous1,f1,frozen1,b,scratch.StartPosition[i],def.HalfSize,config,out bool travel1);
                if(travel0 && BlockedTravel(previous0.Position,f0.Position,t0,i,s.Props,arena,scratch,config.HalfBody))t0=2;
                if(travel1 && BlockedTravel(previous1.Position,f1.Position,t1,i,s.Props,arena,scratch,config.HalfBody))t1=2;
                // Test obstruction at the attack's contact point, excluding the node itself.
                if(t0<=1 && !RopeConnectors.ClearLine(previous0.Position+(f0.Position-previous0.Position)*t0,
                    scratch.StartPosition[i]+(b.Position-scratch.StartPosition[i])*t0,arena,s.Props,i))t0=2;
                if(t1<=1 && !RopeConnectors.ClearLine(previous1.Position+(f1.Position-previous1.Position)*t1,
                    scratch.StartPosition[i]+(b.Position-scratch.StartPosition[i])*t1,arena,s.Props,i))t1=2;
                float first=SimMath.Min(t0,t1);if(first>1)continue;
                bool hit0=t0<=first+1e-5f,hit1=t1<=first+1e-5f;
                if(hit0&&travel0)keep0*=1-def.ConnectorSpeedLoss;
                if(hit1&&travel1)keep1*=1-def.ConnectorSpeedLoss;
                Destroy(ref s.Props,arena,i);
                int actor=hit0&&hit1?SimEvent.NoFighter:hit0?0:1;
                var striker = hit0 ? f0 : f1;
                bool traveling = hit0 ? travel0 : travel1;
                var direction = traveling ? striker.Velocity : b.Position - striker.Position;
                float speed = SimMath.Sqrt(direction.LengthSquared);
                direction = speed > 1e-5f ? direction / speed : new Vec2(0, 1);
                events.Add(new SimEvent(s.Tick,SimEventType.ConnectorBreak,actor,b.Position,direction,
                    traveling ? speed : config.Combat.Body.Punch.Damage) { PropIndex = i });
            }
            s.Fighter0.Velocity*=keep0;s.Fighter1.Velocity*=keep1;
        }

        public static void Destroy(ref PropState props,ArenaDef arena,int body)
        {
            if(!props.Graph.Enabled||props.IsDestroyed(body))return;
            props.Graph.Destroy(body);
            // Keep every body's momentum. Only remove incident edges and wake their surviving endpoints.
            for(int k=0;k<arena.GraphRopeCount;k++)
            {
                ref readonly var r=ref arena.GetGraphRope(k);
                if(r.Body!=body&&r.Joint.OtherBody!=body)continue;
                int other=r.Body==body?r.Joint.OtherBody:r.Body;
                if(other>=0&&!props.IsDestroyed(other))PropState.Get(ref props,other).SleepTicks=0;
            }
            // A sleeping body resting on this node has lost its support even if it has no rope.
            var removed=PropState.At(props,body);var half=arena.GetProp(arena.DynamicProp(body)).HalfSize;
            var extent=Extent(removed.Rotation,half);
            for(int i=0;i<props.Count;i++)
            {
                if(i==body||props.IsDestroyed(i))continue;
                ref var b=ref PropState.Get(ref props,i);var reach=Extent(b.Rotation,arena.GetProp(arena.DynamicProp(i)).HalfSize);
                if(SimMath.Abs(b.Position.X-removed.Position.X)<=extent.X+reach.X+.05f
                    && SimMath.Abs(b.Position.Y-removed.Position.Y)<=extent.Y+reach.Y+.05f)b.SleepTicks=0;
            }
            // A mixed map may retain a historical fixed-anchor rope on this node.
            PropState.Get(ref props,body).Broken=1;
            for(int i=0;i<props.Count;i++)
                if(arena.GetJoint(i).OtherBody==body){ref var b=ref PropState.Get(ref props,i);b.Broken=1;b.SleepTicks=0;}
        }

        static float Hit(in FighterState prev,in FighterState now,bool frozen,in PropBody body,Vec2 oldPosition,
            Vec2 half,SimConfig config,out bool travel)
        {
            travel=now.Action==ActionState.Lunge||now.Action==ActionState.Thrown;
            if(frozen)return 2;
            if(travel)
            {
                if((now.Position-prev.Position).LengthSquared<1e-10f)return 2;
                return SweepBox(prev.Position-oldPosition,(now.Position-prev.Position)-(body.Position-oldPosition),half,body.Rotation,config.HalfBody);
            }
            if(now.AttackId!=AttackIds.Punch||now.AttackHasHit||!FighterCombat.IsHitboxActive(now,config))return 2;
            ref readonly var attack=ref config.GetAttack(now.AttackId);float hit=2;
            for(int i=0;i<attack.UsedCircles;i++)
            {
                var circle=attack.GetCircle(i);var end=FighterCombat.CircleCenter(now,attack,circle);
                var start=prev.Action==now.Action&&prev.AttackId==now.AttackId&&FighterCombat.IsHitboxActive(prev,config)
                    ?FighterCombat.CircleCenter(prev,attack,circle):end-(now.Position-prev.Position);
                hit=SimMath.Min(hit,RopeConnectors.SweepCircle(Local(body.Rotation,start-oldPosition),
                    Local(body.Rotation,(end-start)-(body.Position-oldPosition)),half,circle.Radius,skipAtLimit:false));
            }
            return hit;
        }
        static Vec2 Extent(Vec2 q,Vec2 half)=>new Vec2(SimMath.Abs(q.X)*half.X+SimMath.Abs(q.Y)*half.Y,
            SimMath.Abs(q.Y)*half.X+SimMath.Abs(q.X)*half.Y);
        static bool BlockedTravel(Vec2 start,Vec2 end,float targetTime,int target,in PropState props,ArenaDef arena,PropScratch scratch,float half)
        {
            if(targetTime>1)return false;
            for(int i=0;i<props.Count;i++)
            {
                ref readonly var def=ref arena.GetProp(arena.DynamicProp(i));
                if(i==target||props.IsDestroyed(i)||def.PlayerPassThrough||def.IsConnector)continue;
                ref readonly var body=ref PropState.At(props,i);
                float t=SweepBox(start-scratch.StartPosition[i],end-start-(body.Position-scratch.StartPosition[i]),def.HalfSize,body.Rotation,half);
                // Ignore an initial resting surface when moving away from it.
                if(t<=1e-5f)
                {
                    var relative=end-start-(body.Position-scratch.StartPosition[i]);
                    if(Vec2.Dot(relative,start-scratch.StartPosition[i])>=0)continue;
                }
                if(t<targetTime-1e-5f)return true;
            }
            return false;
        }
        static Vec2 Local(Vec2 q,Vec2 v)=>new Vec2(q.X*v.X+q.Y*v.Y,-q.Y*v.X+q.X*v.Y);

        // Continuous translation SAT: axis-aligned fighter against the physical body's rotated rectangle.
        static float SweepBox(Vec2 start,Vec2 delta,Vec2 half,Vec2 q,float fighterHalf)
        {
            float enter=0,exit=1;
            for(int i=0;i<4;i++)
            {
                var n=i==0?new Vec2(1,0):i==1?new Vec2(0,1):i==2?q:new Vec2(-q.Y,q.X);
                float radius=fighterHalf*(SimMath.Abs(n.X)+SimMath.Abs(n.Y))
                    +half.X*SimMath.Abs(Vec2.Dot(n,q))+half.Y*SimMath.Abs(Vec2.Dot(n,new Vec2(-q.Y,q.X)));
                float p=Vec2.Dot(start,n),v=Vec2.Dot(delta,n);
                if(SimMath.Abs(v)<1e-9f){if(SimMath.Abs(p)>radius)return 2;continue;}
                float a=(-radius-p)/v,b=(radius-p)/v;
                enter=SimMath.Max(enter,SimMath.Min(a,b));exit=SimMath.Min(exit,SimMath.Max(a,b));
                if(enter>exit)return 2;
            }
            return enter;
        }
    }
}
