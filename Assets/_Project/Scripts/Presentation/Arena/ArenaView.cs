using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 지형 그리기(08-visual-direction.md 1·4절). 시뮬레이션과 같은 상자 목록으로 그리므로 둘이 어긋날 수 없다.
    /// 지형은 테마의 고정 지형 색 단색(외곽선·윗면 띠·평면 그림자 없음). 그림자는 배경의 광원이 만든다(BackgroundView).
    /// 단방향 발판: 같은 색의 얇은 판. 튕김 패드: 지형 색 받침 + energySoft 반원 돌기 줄. 가시: hit 삼각형 반복.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        /// <summary>단방향 발판을 그리는 두께(시뮬레이션의 칸 위쪽 절반 0.5 중 윗부분). 그림자도 이 판으로 만든다.</summary>
        public const float OneWayThickness = 0.24f;
        const float BumpSpacing = 0.5f;
        const float BumpSize = 0.34f;
        const float SpikeWidth = 0.5f;

        ArenaDef current;
        [SerializeField] string currentTheme;
        [SerializeField] uint authoredGeometry;
        [SerializeField] bool currentCeiling;
        [SerializeField] Transform[] motionParts;
        [SerializeField] Vector3[] motionPositions;
        [SerializeField] Transform borderRoot;
        [System.Serializable] struct TerrainPose
        {
            public SpriteRenderer renderer;
            public Vector3 position,scale;
            public Quaternion rotation;
            public Vector2 size;
        }
        [SerializeField,HideInInspector] TerrainPose[] authoredTerrain;
        [System.Serializable] struct GroupPose
        {
            public Transform transform;
            public Vector3 position,scale;
            public Quaternion rotation;
        }
        [SerializeField,HideInInspector] GroupPose[] authoredGroups;
        Transform buildParent;
        public Transform BorderRoot => borderRoot;
        public int MotionGroupCount => motionParts == null ? 0 : motionParts.Length;
        public bool TransitionSettled { get; private set; } = true;

        public void Build(ArenaDef arena, ArenaTheme theme, PaletteAsset palette, ShapeLibrary shapes, bool drawCeiling = false)
        {
            if ((arena == current || (borderRoot != null && authoredGeometry == arena.ComputeGeometryChecksum())) && theme.name == currentTheme && drawCeiling == currentCeiling)
            {
                bool newRoundDefinition=arena!=current;
                current=arena;
                if(newRoundDefinition)RestoreDefinitionGroups();
                RestoreDefinitionVisuals();
                if(newRoundDefinition)PresentTransition(1,true);
                return;
            }
            current = arena;
            authoredGeometry=arena.ComputeGeometryChecksum();
            currentTheme = theme.name;
            currentCeiling = drawCeiling;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                transform.GetChild(i).gameObject.SetActive(false);
                if(Application.isPlaying)Destroy(transform.GetChild(i).gameObject);
                else DestroyImmediate(transform.GetChild(i).gameObject);
            }

            borderRoot = new GameObject("Connected_arena_boundary").transform;
            borderRoot.SetParent(transform, false);
            var groups = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                // 경기장 맨 위 테두리 줄은 그리지 않는다: 화면 위에 띠나 액자가 없어야 하고(08 4·7절), 광원이 그 바깥에 있다.
                // 천장은 시뮬레이션에 그대로 있다(화면 위 끝에 부딪힌다). 짝 파일의 "ceiling": true면 그린다(CR-004).
                if (!drawCeiling && b.MinY >= arena.HalfHeight - 1f - 1e-3f)
                    continue;
                bool boundary = b.Kind == TerrainKind.Solid && (b.MinX <= -arena.HalfWidth + .001f
                    || b.MaxX >= arena.HalfWidth - .001f || b.MinY <= -arena.HalfHeight + .001f || b.MaxY >= arena.HalfHeight - .001f);
                buildParent = new GameObject("Terrain_box_" + i + "_" + b.Kind).transform;
                buildParent.SetParent(boundary ? borderRoot : transform, false);
                if (!boundary) groups.Add(buildParent);
                switch (b.Kind)
                {
                    case TerrainKind.Solid:
                        Rect(shapes, theme.terrain, SortingLayers.Terrain, 0, b.MinX, b.MinY, b.MaxX, b.MaxY);
                        break;
                    case TerrainKind.OneWay:
                        Rect(shapes, theme.terrain, SortingLayers.Terrain, 2, b.MinX, b.MaxY - OneWayThickness, b.MaxX, b.MaxY);
                        break;
                    case TerrainKind.Bounce:
                        Rect(shapes, theme.terrain, SortingLayers.Terrain, 2, b.MinX, b.MinY, b.MaxX, b.MaxY - BumpSize * 0.5f);
                        for (float x = b.MinX + BumpSpacing * 0.5f; x < b.MaxX; x += BumpSpacing)
                        {
                            var bump = ShapeLibrary.Create(buildParent, "Bump", shapes.Circle, SortingLayers.Terrain, 1, theme.Highlight(palette));
                            bump.transform.localPosition = new Vector3(x, b.MaxY - BumpSize * 0.5f, 0f);
                            ShapeLibrary.SetSize(bump, BumpSize, BumpSize);
                        }
                        break;
                    case TerrainKind.Spike:
                        for (float x = b.MinX; x < b.MaxX - 1e-3f; x += SpikeWidth)
                        {
                            var spike = ShapeLibrary.Create(buildParent, "Spike", shapes.Triangle, SortingLayers.Terrain, 2, palette.hit);
                            spike.transform.localPosition = new Vector3(x + SpikeWidth * 0.5f, (b.MinY + b.MaxY) * 0.5f, 0f);
                            ShapeLibrary.SetSize(spike, SpikeWidth, b.MaxY - b.MinY);
                        }
                        break;
                }
            }
            // 맵의 상자 정의 하나를 한 덩어리로 취급한다. 패드/가시 장식도 땅과 같이 이동한다.
            motionParts = groups.ToArray();
            motionPositions = new Vector3[motionParts.Length];
            for (int i = 0; i < motionParts.Length; i++)
                motionPositions[i] = motionParts[i].localPosition;
            TransitionSettled = true;
            CaptureDefinitionVisuals();
        }

        // Main terrain is generated from MapDefinition. Accidental edits of its render-only
        // children must never become a second collision source. Use MapAuthoring to change geometry.
        public void CaptureDefinitionVisuals()
        {
            var groups=GetComponentsInChildren<Transform>(true);authoredGroups=new GroupPose[groups.Length];
            for(int i=0;i<groups.Length;i++)
            {
                var t=groups[i];authoredGroups[i]=new GroupPose{transform=t,position=t.localPosition,scale=t.localScale,rotation=t.localRotation};
            }
            var renderers=GetComponentsInChildren<SpriteRenderer>(true);authoredTerrain=new TerrainPose[renderers.Length];
            for(int i=0;i<renderers.Length;i++)
            {
                var r=renderers[i];authoredTerrain[i]=new TerrainPose{renderer=r,position=r.transform.localPosition,
                    scale=r.transform.localScale,rotation=r.transform.localRotation,size=r.size};
            }
        }
        void RestoreDefinitionGroups()
        {
            if(authoredGroups==null)return;
            foreach(var p in authoredGroups)
            {
                if(p.transform==null)continue;
                p.transform.localPosition=p.position;p.transform.localScale=p.scale;p.transform.localRotation=p.rotation;
            }
        }
        void RestoreDefinitionVisuals()
        {
            if(authoredTerrain==null)return;
            foreach(var p in authoredTerrain)
            {
                if(p.renderer==null)continue;
                p.renderer.transform.localPosition=p.position;p.renderer.transform.localScale=p.scale;
                p.renderer.transform.localRotation=p.rotation;p.renderer.size=p.size;
            }
        }

        /// <summary>연출용 위치만 이동한다. 1에서 모든 파트가 원래 지형 위치로 정확히 돌아온다.</summary>
        public void PresentTransition(float progress, bool entering)
        {
            if (motionParts == null) return;
            float frameT = Mathf.Clamp01((progress - (entering ? .18f : 0)) / .76f);
            float frameAway = entering ? Mathf.Pow(1 - frameT, 3) : frameT * frameT;
            // 네 벽을 한 좌표계에서 확대한다. 모서리는 항상 붙어 있고 판정은 원래 맵 그대로다.
            borderRoot.localScale = Vector3.one * (1 + frameAway * 1.35f);
            TransitionSettled = entering && frameAway == 0;
            for (int i = 0; i < motionParts.Length; i++)
            {
                float delay = (i % 5) * .035f + (entering ? .04f : .10f);
                float t = Mathf.Clamp01((progress - delay) / .70f);
                float eased = entering ? 1 - Mathf.Pow(1 - t, 3) : t * t;
                float away = entering ? 1 - eased : eased;
                Vector2 direction = entering ? Vector2.up : Vector2.down;
                motionParts[i].localPosition = motionPositions[i] + (Vector3)(direction * (current.Rows + 3) * away);
                TransitionSettled &= away == 0;
            }
        }

        void Rect(ShapeLibrary shapes, Color color, string layer, int order, float minX, float minY, float maxX, float maxY)
        {
            var sr = ShapeLibrary.Create(buildParent, "Rect", shapes.Square, layer, order, color);
            sr.transform.localPosition = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            ShapeLibrary.SetSize(sr, maxX - minX, maxY - minY);
        }
    }
}
