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
        readonly Vector2[] piecePoints = new Vector2[ConvexPolygon.MaxVertices];
        // 숫자 발판은 몸 하나가 조각 여러 개라 투영 칸을 넉넉히 둔다(숫자 2 × 최대 5조각 + 콜론 2 + 다른 구조물).
        public void Setup(PaletteAsset palette, Material material) => projection = new ProjectedShadowMesh(gameObject, PropState.Capacity * 2, material, 1);
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
                if(p.HasShape)
                {
                    // 결과 공간 숫자 발판(2026-11-20): 몸 상자 대신 깎은 조각마다 같은 광원으로 그림자를 투영한다(몸 상자는 숨겨져 있다).
                    // 화면 위 밖에서 기다리는 동안은 그림자가 없고, 화면에 들어오며 서서히 진해진다(멀리 있는 몸의 투영 그림자가 아무것도 없는 곳에서 크게 움찔거렸다).
                    float strength=EntryStrength(tr.localPosition.y,p.HalfSize.Y);
                    if(strength<=0f)continue;
                    float angle=tr.localEulerAngles.z*Mathf.Deg2Rad, c=Mathf.Cos(angle), s=Mathf.Sin(angle);
                    Vector2 at=tr.localPosition;
                    foreach(var piece in p.Shape)
                    {
                        int n=Mathf.Min(piece.Count,piecePoints.Length);
                        for(int k=0;k<n;k++){var q=piece.Point(k);piecePoints[k]=at+new Vector2(c*q.X-s*q.Y,s*q.X+c*q.Y);}
                        if(projection.Points(piecePoints,n,lightPoint,reach))projection.SetLastStrength(strength);
                    }
                    continue;
                }
                if(view.BodyHidden(b))continue;
                projection.Box(tr.localPosition,new Vector2(p.HalfSize.X,p.HalfSize.Y),tr.localEulerAngles.z*Mathf.Deg2Rad,lightPoint,reach);
            }
            projection.Commit();
        }
        /// <summary>
        /// 모양 있는 몸(결과 공간 숫자 발판) 그림자의 진하기: 몸 전체가 화면 위끝보다 위면 0, 화면 안으로 EntryFadeDistance만큼 내려오면 1(부드러운 곡선).
        /// 쉬는 자리(숫자 가운데 y 1.2)에 오기 전에 다 진해진다. 그림만 바꾸며 판정과 무관하다.
        /// </summary>
        float EntryStrength(float centerY,float halfY)
        {
            float enter=arena.HalfHeight+halfY;
            float t=Mathf.Clamp01((enter-centerY)/EntryFadeDistance);
            return t*t*(3f-2f*t);
        }
        const float EntryFadeDistance=7f;

        void OnDestroy() => projection?.Dispose();
    }
}
