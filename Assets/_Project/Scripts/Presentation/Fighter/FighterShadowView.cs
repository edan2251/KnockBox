using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>두 파이터의 현재 몸에서 공통 광원의 선명한 그림자를 투영한다. 그림자가 겹쳐도 명암은 누적하지 않는다.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FighterShadowView : MonoBehaviour
    {
        ProjectedShadowMesh projection;
        Vector2 lightPoint;
        bool active;
        public void Setup(PaletteAsset palette, Material material) => projection = new ProjectedShadowMesh(gameObject, 2, material, 1);
        public void SetMaterial(Material material) => projection.SetMaterial(material);
        public void SetLight(Vector2 lightPosition, float strength) { lightPoint=lightPosition;active=strength>0; }
        public void Render(Vector2 center0,Vector2 center1,float half)
        {
            if(projection==null)return;
            projection.Begin();
            if(active)
            {
                float reach=(lightPoint.magnitude+100)*4;
                projection.Box(center0,Vector2.one*half,0,lightPoint,reach);
                projection.Box(center1,Vector2.one*half,0,lightPoint,reach);
            }
            projection.Commit();
        }
        void OnDestroy() => projection?.Dispose();
    }
}
