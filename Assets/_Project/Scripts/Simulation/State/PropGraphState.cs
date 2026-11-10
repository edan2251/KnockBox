using System;
namespace Knockbox.Simulation
{
    /// <summary>Optional schema-8 state. Edges release exactly when either endpoint is destroyed.
    /// Body slots never move. Value-only masks preserve snapshot-by-assignment and zero tick allocations.</summary>
    [Serializable]
    public struct PropGraphState
    {
        public bool Enabled;
        public uint DestroyedLow, DestroyedHigh;
        public readonly bool IsDestroyed(int body) => Enabled && (body < 32
            ? (DestroyedLow & (1u << body)) != 0 : (DestroyedHigh & (1u << (body-32))) != 0);
        public void Destroy(int body)
        { if(body<32) DestroyedLow |= 1u<<body; else DestroyedHigh |= 1u<<(body-32); }
        public readonly void Visit<T>(ref T v) where T:struct,IStateVisitor
        {
            v.Value(nameof(Enabled),Enabled);
            v.Value(nameof(DestroyedLow),DestroyedLow);
            v.Value(nameof(DestroyedHigh),DestroyedHigh);
        }
    }
}
