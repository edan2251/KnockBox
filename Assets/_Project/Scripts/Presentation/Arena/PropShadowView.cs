using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>움직이는 구조물의 현재 형상과 공통 광원으로 고정 지형과 같은 선명한 그림자를 투영한다.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PropShadowView : MonoBehaviour
    {
        ProjectedShadowMesh projection;
        ArenaDef arena;
        Vector2 lightPoint;
        public int VisibleQuads => projection?.Count ?? 0;
        public void Setup(PaletteAsset palette, Material material) => projection = new ProjectedShadowMesh(gameObject, PropState.Capacity, material, 1);
        public void SetMaterial(Material material) => projection.SetMaterial(material);
        public void SetLight(Vector2 value) => lightPoint = value;
        public void SetArena(ArenaDef value, Vector2 lightPosition) { arena=value;lightPoint=lightPosition;Clear(); }
        public void Clear() { if(projection==null)return;projection.Begin();projection.Commit(); }
        public void Render(PropView view, in PropState state)
        {
            if(projection==null || arena==null)return;
            projection.Begin();
            int count=Mathf.Min(arena.DynamicPropCount,state.Count);
            float reach=(arena.Columns+arena.Rows+lightPoint.magnitude)*4;
            for(int b=0;b<count;b++)
            {
                if(state.IsDestroyed(b))continue;
                ref readonly var p=ref arena.GetProp(arena.DynamicProp(b));
                var tr=view.BodyTransform(b);
                projection.Box(tr.localPosition,new Vector2(p.HalfSize.X,p.HalfSize.Y),tr.localEulerAngles.z*Mathf.Deg2Rad,lightPoint,reach);
            }
            projection.Commit();
        }
        void OnDestroy() => projection?.Dispose();
    }
}
