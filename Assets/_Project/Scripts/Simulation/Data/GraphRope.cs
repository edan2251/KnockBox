namespace Knockbox.Simulation
{
    /// <summary>Independent, unbreakable edge. Body is endpoint B; Joint.OtherBody is A (-1 = world).</summary>
    public struct GraphRope
    {
        public const int Capacity = 96;
        public string Id;
        public int Body;
        public PropJoint Joint;
        public bool Active(in PropState state) => !state.IsDestroyed(Body)
            && (Joint.OtherBody < 0 || !state.IsDestroyed(Joint.OtherBody));
    }
}
