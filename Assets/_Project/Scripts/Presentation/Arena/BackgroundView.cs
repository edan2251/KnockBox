using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>배경 질감은 캐시하고 그림자는 광원에서 뻗는 선명한 메시로 그린다. 타격 때 메시/광원만 갱신하며 텍스처를 다시 굽지 않는다.</summary>
    public sealed class BackgroundView : MonoBehaviour
    {
        public static Vector2 DefaultLight(ArenaDef arena) => new Vector2(0, arena.HalfHeight + arena.Rows * .75f);
        public const float ShadowStrength = .65f;
        public const float FalloffExponent = .7f;
        public const float RoundLightPulseSeconds = .20f;
        const float RoundLightPulsePeak = .18f;
        const int TexelsPerUnit = 6, MoteCount = 26;
        const float CoverMarginX = 4, CoverMarginY = 2.25f, NoiseDepth = .06f;
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        readonly SpriteRenderer[] motes = new SpriteRenderer[MoteCount];
        readonly Vector2[] motePosition = new Vector2[MoteCount], moteVelocity = new Vector2[MoteCount];
        readonly float[] moteSize = new float[MoteCount];
        Vector2 lightPoint;
        SpriteRenderer baseRenderer;
        Sprite baseSprite;
        Texture2D ownedThemeTexture;
        Material baseMaterial;
        ProjectedShadowMesh terrainShadows;
        GameObject shadowObject;
        Rect[] occluders;
        ConvexPolygon[] polygonOccluders;
        PaletteAsset palette;
        Color moteColor;
        FxRandom random;
        string currentKey;
        float coverWidth, coverHeight, halfWidth = 16, halfHeight = 9, reach;
        float transitionOpacity = 1;
        float shadowDarkness = ShadowStrength;
        int width, height;
        public float ShadowVisibility { get; private set; } = 1;
        public bool RoundLightOn { get; private set; } = true;
        public float RoundLightPulse { get; private set; }
        public Vector2 LightPosition => lightPoint;
        public Vector2 BaseLightPosition { get; private set; }
        public Material ShadowMaterial { get; private set; }

        public void SetTransitionOpacity(float opacity)
        {
            transitionOpacity = Mathf.Clamp01(opacity);
            if (baseMaterial != null) baseMaterial.SetFloat("_Opacity", transitionOpacity);
            if (ShadowMaterial != null) ShadowMaterial.SetFloat("_Opacity", transitionOpacity);
            // 반짝이는 입자는 조명과 독립적으로 보이며 기존 경기장 등장/퇴장 페이드만 따른다.
            foreach (var mote in motes) if (mote != null) mote.color = PaletteAsset.WithAlpha(moteColor, .4f * transitionOpacity);
        }

        /// <summary>GO 전에는 배경 전체를 그림자 명암으로 놓고, GO에서 밝은 영역을 연다. 퇴장 페이드와 독립적이다.</summary>
        public void SetRoundLight(bool on)
        {
            if (RoundLightOn == on) return;
            RoundLightOn = on;
            if (baseMaterial != null) baseMaterial.SetFloat("_ShadowStrength", on ? 0 : shadowDarkness);
            if (!on) SetRoundLightPulse(-1);
            SetTransitionOpacity(transitionOpacity);
        }

        /// <summary>GO의 빛 받는 배경만 잠깐 밝힌다. PhaseTick 기반 경과초이므로 렌더 프레임률과 무관하다.</summary>
        public void SetRoundLightPulse(float elapsedSeconds)
        {
            float remaining = RoundLightOn && elapsedSeconds >= 0
                ? 1 - Mathf.Clamp01(elapsedSeconds / RoundLightPulseSeconds) : 0;
            RoundLightPulse = RoundLightPulsePeak * remaining * remaining * remaining;
            if (baseMaterial != null) baseMaterial.SetFloat("_LightBoost", RoundLightPulse);
            // 공유 그림자 메시와 입자에는 값을 전달하지 않는다. 어두운 영역은 같은 밝기로 위에 그려진다.
        }

        /// <summary>지형 재구성과 라운드 조명 조건을 충족할 때 공통 그림자 레이어를 표시한다.</summary>
        public void SetShadowVisibility(float value)
        {
            ShadowVisibility = Mathf.Clamp01(value);
            if (ShadowMaterial != null) ShadowMaterial.SetFloat("_Visible", ShadowVisibility);
        }

        public void Build(ArenaDef arena, ArenaTheme theme, PaletteAsset paletteAsset, ShapeLibrary shapes, Vector2 lightPosition, float shadowStrength = ShadowStrength)
        {
            palette = paletteAsset;
            moteColor = theme.Mote(palette);
            if (motes[0] == null)
                for (int i = 0; i < MoteCount; i++) motes[i] = ShapeLibrary.Create(transform, "Mote", shapes.Circle, SortingLayers.Background, 3, palette.mote);
            string key = $"{arena.Name}#{arena.ComputeGeometryChecksum():X8}|{theme.name}|{lightPosition.x:F2},{lightPosition.y:F2}|{shadowStrength:F2}";
            if (key == currentKey) { SetLight(lightPosition); return; }
            currentKey = key;
            BaseLightPosition = lightPoint = lightPosition;
            shadowDarkness = Mathf.Clamp01(shadowStrength);
            halfWidth = arena.HalfWidth; halfHeight = arena.HalfHeight;
            coverWidth = arena.Columns + CoverMarginX; coverHeight = arena.Rows + CoverMarginY;
            width = Mathf.RoundToInt(coverWidth * TexelsPerUnit); height = Mathf.RoundToInt(coverHeight * TexelsPerUnit);
            reach = (coverWidth + coverHeight + lightPosition.magnitude) * 4;
            var boxes = new List<Rect>();var polygons=new List<ConvexPolygon>();
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                if (b.Kind == TerrainKind.Spike || b.MaxY >= halfHeight - .001f) continue;
                if(b.IsPolygon){polygons.Add(b.Polygon);continue;}
                float minY = b.Kind == TerrainKind.OneWay ? b.MaxY - ArenaView.OneWayThickness : b.MinY;
                boxes.Add(Rect.MinMaxRect(b.MinX, minY, b.MaxX, b.MaxY));
            }
            occluders = boxes.ToArray();polygonOccluders=polygons.ToArray();
            Texture2D texture;
            if (ownedThemeTexture != null)
            {
                if (Application.isPlaying) Destroy(ownedThemeTexture); else DestroyImmediate(ownedThemeTexture);
                ownedThemeTexture = null;
            }
            if (theme.backgroundGradient)
            {
                // Custom color edits have unbounded combinations. Keep one per view instead
                // of retaining every intermediate color in the static legacy texture cache.
                texture = ownedThemeTexture = CreateTexture(theme);
            }
            else if (!Cache.TryGetValue(key, out texture) || texture == null) Cache[key] = texture = CreateTexture(theme);
            if (baseMaterial == null)
            {
                var shader = Resources.Load<Shader>("Presentation/ProjectedBackground");
                if (shader == null) throw new System.InvalidOperationException("ProjectedBackground shader missing");
                baseMaterial = new Material(shader) { name = "Moving_background_light", hideFlags = HideFlags.HideAndDontSave };
                ShadowMaterial = new Material(shader) { name = "Shared_sharp_shadows", hideFlags = HideFlags.HideAndDontSave };
            }
            ConfigureMaterial(baseMaterial, texture, RoundLightOn ? 0 : shadowDarkness);
            ConfigureMaterial(ShadowMaterial, texture, shadowDarkness);
            SetRoundLightPulse(-1);
            if (baseSprite != null) Destroy(baseSprite);
            baseSprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, TexelsPerUnit);
            baseSprite.hideFlags = HideFlags.HideAndDontSave;
            if (baseRenderer == null) baseRenderer = ShapeLibrary.Create(transform, "Background", baseSprite, SortingLayers.Background, -1, PaletteAsset.Neutral);
            baseRenderer.sprite = baseSprite; baseRenderer.drawMode = SpriteDrawMode.Simple; baseRenderer.sharedMaterial = baseMaterial;
            if (shadowObject == null)
            {
                shadowObject = new GameObject("Terrain_projected_shadows");
                shadowObject.transform.SetParent(transform, false);
            }
            terrainShadows?.Dispose();
            terrainShadows = new ProjectedShadowMesh(shadowObject, Mathf.Max(1, occluders.Length+polygonOccluders.Length), ShadowMaterial, 0);
            Reproject();
            SetTransitionOpacity(transitionOpacity); SetShadowVisibility(ShadowVisibility); ResetMotes();
        }

        void ConfigureMaterial(Material material, Texture2D texture, float strength)
        {
            material.SetTexture("_BackgroundTex", texture);
            material.SetVector("_CoverSize", new Vector4(coverWidth, coverHeight, 0, 0));
            material.SetVector("_BaseLight", BaseLightPosition);
            material.SetVector("_LightPosition", lightPoint);
            material.SetFloat("_HalfHeight", halfHeight); material.SetFloat("_FalloffExponent", FalloffExponent);
            material.SetFloat("_ShadowStrength", strength);
            material.SetFloat("_LightBoost", 0);
            material.SetColor("_BackgroundColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? palette.bgBase.linear : palette.bgBase);
        }

        public void SetLight(Vector2 value)
        {
            if (lightPoint == value) return;
            lightPoint = value;
            if (baseMaterial == null) return;
            baseMaterial.SetVector("_LightPosition", value); ShadowMaterial.SetVector("_LightPosition", value);
            Reproject();
        }

        void Reproject()
        {
            terrainShadows.Begin();
            foreach (var rect in occluders) terrainShadows.Box(rect.center, rect.size * .5f, 0, lightPoint, reach);
            foreach(var polygon in polygonOccluders)terrainShadows.Polygon(polygon,lightPoint,reach);
            terrainShadows.Commit();
        }

        public float Falloff(Vector2 point)
        {
            float top = Mathf.Max(.1f, Mathf.Abs(lightPoint.y - halfHeight));
            return Mathf.Pow(top / Mathf.Max(top, Vector2.Distance(point, lightPoint)), FalloffExponent);
        }

        /// <summary>현재 광원에서의 정확한 가림 판정. 흐린 텍스처 표본을 읽지 않는다.</summary>
        public float ShadowAmount(Vector2 point)
        {
            if (occluders == null) return 0;
            var p=new Vec2(point.x,point.y);var light=new Vec2(lightPoint.x,lightPoint.y);
            foreach(var polygon in polygonOccluders)if(polygon.Contains(p))return 0;
            foreach (var rect in occluders) if (rect.Contains(point)) return 0;
            foreach (var rect in occluders) if (SegmentHitsRect(point, lightPoint, rect)) return 1;
            foreach(var polygon in polygonOccluders)if(polygon.SegmentHits(p,light))return 1;
            return 0;
        }
        static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0, t1 = 1; var d = b - a;
            return Clip(-d.x, a.x-r.xMin, ref t0, ref t1) && Clip(d.x,r.xMax-a.x,ref t0,ref t1)
                && Clip(-d.y,a.y-r.yMin,ref t0,ref t1) && Clip(d.y,r.yMax-a.y,ref t0,ref t1) && t1 > t0 + .00001f;
        }
        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0;
            float t = q/p;
            if (p < 0) { if(t > t1)return false; if(t > t0)t0=t; }
            else { if(t < t0)return false; if(t < t1)t1=t; }
            return true;
        }

        Texture2D CreateTexture(ArenaTheme theme)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp, hideFlags=HideFlags.HideAndDontSave, name="Unshadowed_background" };
            var noise = ShapeSprites.NoiseValues(width, height, .035f * 6 / TexelsPerUnit);
            var pixels = new Color[width * height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int i=y*width+x;var point=new Vector2(-coverWidth*.5f+(x+.5f)/TexelsPerUnit,-coverHeight*.5f+(y+.5f)/TexelsPerUnit);
                var c=theme.BackgroundAt((point.y+halfHeight)/(halfHeight*2))*(Falloff(point)*(1-noise[i]*NoiseDepth));c.a=1;pixels[i]=c;
            }
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }

        public void ResetMotes()
        {
            random=new FxRandom(0xD057u);
            for(int i=0;i<MoteCount;i++)Respawn(i,true);
            Tick(0);
        }
        void Respawn(int i,bool anywhere)
        {
            float y=anywhere?random.Range(-halfHeight+1,halfHeight-1):random.Range(-halfHeight+1,-2);
            motePosition[i]=new Vector2(random.Range(-halfWidth+1,halfWidth-1),y);
            moteVelocity[i]=new Vector2(random.Range(-.18f,.18f),random.Range(.06f,.28f));moteSize[i]=random.Range(.06f,.12f);
        }
        public void Tick(float dt)
        {
            if(palette==null)return;
            for(int i=0;i<MoteCount;i++)
            {
                motePosition[i]+=moteVelocity[i]*dt;
                if(motePosition[i].y>halfHeight-.5f || Mathf.Abs(motePosition[i].x)>halfWidth-.5f)Respawn(i,false);
                motes[i].transform.localPosition=motePosition[i];ShapeLibrary.SetSize(motes[i],moteSize[i],moteSize[i]);
            }
        }
        void OnDestroy()
        {
            terrainShadows?.Dispose();
            if(ownedThemeTexture!=null)Destroy(ownedThemeTexture);
            if(baseSprite!=null)Destroy(baseSprite);
            if(baseMaterial!=null)Destroy(baseMaterial);
            if(ShadowMaterial!=null)Destroy(ShadowMaterial);
        }
    }
}
