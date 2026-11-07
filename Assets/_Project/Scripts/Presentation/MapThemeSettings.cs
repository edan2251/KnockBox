using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    public enum MapThemeMode
    {
        [InspectorName("기존 테마")] Legacy,
        [InspectorName("색상 프리셋")] Preset,
        [InspectorName("직접 설정")] Custom
    }

    public enum MapThemePreset
    {
        [InspectorName("깊은 청록")] Lagoon,
        [InspectorName("보랏빛 밤")] Violet,
        [InspectorName("은은한 노을")] Sunset,
        [InspectorName("고요한 숲")] Forest
    }

    /// <summary>맵에 저장하는 연출 설정. 경기 판정·입력·맵 해시에는 포함하지 않는다.</summary>
    [Serializable]
    public sealed class MapThemeSettings
    {
        public MapThemeMode mode;
        public MapThemePreset preset;
        public Color gradientTop = PaletteAsset.MapThemeDefaults.LagoonTop;
        public Color gradientBottom = PaletteAsset.MapThemeDefaults.LagoonBottom;
        public Color terrain = PaletteAsset.MapThemeDefaults.LagoonTerrain;
        public Color prop = PaletteAsset.MapThemeDefaults.LagoonProp;
        public Color highlight = PaletteAsset.MapThemeDefaults.LagoonHighlight;
        [Range(.25f, 3f)] public float gradientCurve = 1f;

        public ArenaTheme Resolve(PaletteAsset palette, int legacyIndex)
        {
            if (mode == MapThemeMode.Legacy) return palette.Theme(legacyIndex);
            ArenaTheme theme = mode == MapThemeMode.Preset
                ? PaletteAsset.MapThemeDefaults.Preset(preset)
                : new ArenaTheme("Map_Theme", gradientTop, terrain, prop)
                {
                    backgroundGradient = true,
                    gradientTop = gradientTop,
                    gradientBottom = gradientBottom,
                    gradientCurve = Mathf.Clamp(gradientCurve, .25f, 3f),
                    highlight = highlight
                };
            // Existing renderers cache by name. Include exact float color/curve content so
            // editing a saved theme invalidates its view cache without changing map identity.
            var hash = Hasher.Create();
            AddColor(ref hash, theme.gradientTop); AddColor(ref hash, theme.gradientBottom);
            AddColor(ref hash, theme.terrain); AddColor(ref hash, theme.prop);
            AddColor(ref hash, theme.highlight); hash.AddFloat(theme.gradientCurve);
            theme.name = "Map_Theme_" + hash.Hash.ToString("X8");
            return theme;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (!Enum.IsDefined(typeof(MapThemeMode), mode)) errors.Add("맵 테마 방식을 다시 선택해주세요.");
            if (mode == MapThemeMode.Preset && !Enum.IsDefined(typeof(MapThemePreset), preset))
                errors.Add("맵 색상 프리셋을 다시 선택해주세요.");
            if (mode != MapThemeMode.Custom) return errors;
            if (!ValidColor(gradientTop) || !ValidColor(gradientBottom) || !ValidColor(terrain)
                || !ValidColor(prop) || !ValidColor(highlight)) errors.Add("맵 색상은 유효한 0~1 범위여야 합니다.");
            if (float.IsNaN(gradientCurve) || float.IsInfinity(gradientCurve) || gradientCurve < .25f || gradientCurve > 3f)
                errors.Add("그라데이션 곡선은 0.25~3 범위여야 합니다.");
            return errors;
        }

        static bool ValidColor(Color color) => ValidChannel(color.r) && ValidChannel(color.g)
            && ValidChannel(color.b) && ValidChannel(color.a);
        static bool ValidChannel(float value) => !float.IsNaN(value) && value >= 0 && value <= 1;
        static void AddColor(ref Hasher hash, Color color)
        { hash.AddFloat(color.r); hash.AddFloat(color.g); hash.AddFloat(color.b); hash.AddFloat(color.a); }
    }
}
