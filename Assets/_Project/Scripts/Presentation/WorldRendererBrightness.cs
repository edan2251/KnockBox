using System;
using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 지정한 월드 오브젝트만 감광한다. 원본 재질과 SpriteRenderer.color를 보존하므로
    /// UI 초상·피격 플래시·외곽선·알파에 누적 곱셈이 남지 않는다. 맵 생성 때만 대상을 수집한다.
    /// </summary>
    public sealed class WorldRendererBrightness : IDisposable
    {
        sealed class Target
        {
            public Renderer Renderer;
            public Material[] Original, Dimmed;
        }
        sealed class Tint
        {
            public Material Material;
            public int Property;
            public Color Original;
        }
        readonly List<Target> targets = new List<Target>();
        readonly Dictionary<Material, Tint> variants = new Dictionary<Material, Tint>();
        static readonly int SpriteTint = Shader.PropertyToID("_Color");
        static readonly int ModelTint = Shader.PropertyToID("_BaseColor");
        public float Brightness { get; private set; } = 1;

        public void Rebuild(params Transform[] roots)
        {
            Dispose();
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var original = renderer.sharedMaterials;
                    var dimmed = new Material[original.Length];
                    bool supported = false;
                    for (int i = 0; i < original.Length; i++)
                    {
                        var material = original[i];
                        dimmed[i] = material;
                        if (material == null) continue;
                        int property = material.HasProperty(SpriteTint) ? SpriteTint
                            : material.HasProperty(ModelTint) ? ModelTint : 0;
                        if (property == 0) continue;
                        if (!variants.TryGetValue(material, out var tint))
                        {
                            tint = new Tint { Material = new Material(material)
                                { name = material.name + " (round preparation)", hideFlags = HideFlags.HideAndDontSave },
                                Property = property, Original = material.GetColor(property) };
                            variants.Add(material, tint);
                        }
                        dimmed[i] = tint.Material;
                        supported = true;
                    }
                    if (supported) targets.Add(new Target { Renderer = renderer, Original = original, Dimmed = dimmed });
                }
            }
        }

        public void SetBrightness(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, Brightness)) return;
            bool wasNormal = Brightness == 1, normal = value == 1;
            Brightness = value;
            foreach (var tint in variants.Values)
            {
                var c = tint.Original;
                tint.Material.SetColor(tint.Property, PaletteAsset.Scaled(c, value));
            }
            if (wasNormal == normal) return;
            foreach (var target in targets)
                if (target.Renderer != null) target.Renderer.sharedMaterials = normal ? target.Original : target.Dimmed;
        }

        public void Dispose()
        {
            SetBrightness(1);
            targets.Clear();
            foreach (var tint in variants.Values)
                if (tint.Material != null) UnityEngine.Object.Destroy(tint.Material);
            variants.Clear();
        }
    }
}
