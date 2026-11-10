namespace Knockbox.Simulation
{
    public static partial class PropPhysics
    {
        static void SyncGraphSleep(ref PropState props,ArenaDef arena,bool wakeOnly)
        {
            // Bounded propagation also covers reverse-ordered chains and cycles, independently of authoring order.
            for(int pass=0;pass<props.Count && arena.GraphRopeCount>0;pass++)
            {
                bool changed=false;
                for(int k=0;k<arena.GraphRopeCount;k++)
                {
                    ref readonly var r=ref arena.GetGraphRope(k);
                    if(r.Joint.OtherBody<0 || !r.Active(props))continue;
                    ref var a=ref PropState.Get(ref props,r.Joint.OtherBody);
                    ref var b=ref PropState.Get(ref props,r.Body);
                    int min=SimMath.Min(a.SleepTicks,b.SleepTicks);
                    if((wakeOnly&&min!=0)||a.SleepTicks==b.SleepTicks)continue;
                    a.SleepTicks=b.SleepTicks=min;changed=true;
                }
                if(!changed)break;
            }
        }
        static void SolveGraph(ref PropState props,ArenaDef arena,in PropParams c,float h,PropScratch scratch,int tick,SimEventBuffer events,bool project,int iteration)
        {
            int passes=project?1:2;
            for(int pass=0;pass<passes;pass++)for(int k=0;k<arena.GraphRopeCount;k++)
            {
                int index=((project?iteration:pass)&1)==0?k:arena.GraphRopeCount-1-k;
                ref readonly var edge=ref arena.GetGraphRope(index);
                if(!edge.Active(props)||Asleep(PropState.At(props,edge.Body),c))continue;
                if(project)ProjectRope(ref props,arena,c,edge.Body,edge.Joint,scratch,tick,events,true);
                else SolveJoint(ref props,arena,c,edge.Body,edge.Joint,h,scratch,tick,events,true);
            }
        }
    }
}
