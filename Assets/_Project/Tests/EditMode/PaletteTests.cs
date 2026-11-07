using Knockbox.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP11-4와 08-visual-direction.md 2절의 색 규칙을 팔레트 값으로 확인한다.</summary>
    public class PaletteTests
    {
        static float Linear(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);
        static float Luminance(Color c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        static PaletteAsset Palette() => ScriptableObject.CreateInstance<PaletteAsset>();

        [Test]
        public void EveryTheme_StaticTerrainIsBrighterThanProps()
        {
            var palette = Palette();
            Assert.AreEqual(3, palette.ThemeCount);
            for (int i = 0; i < palette.ThemeCount; i++)
            {
                var theme = palette.Theme(i);
                float terrain = Luminance(theme.terrain), prop = Luminance(theme.prop);
                TestContext.WriteLine($"{theme.name}: 고정 지형 {terrain:F3}, 구조물 {prop:F3}");
                Assert.Greater(terrain, prop, theme.name);
            }
        }

        [Test]
        public void Players_StandOutOnBackground_AndDifferInGrayscale()
        {
            var palette = Palette();
            Assert.GreaterOrEqual(Contrast(palette.p1Body, palette.bgBase), 4.5f);
            Assert.GreaterOrEqual(Contrast(palette.p2Body, palette.bgBase), 3.5f);
            for (int i = 0; i < palette.ThemeCount; i++)
            {
                var light = palette.Theme(i).light;
                Assert.GreaterOrEqual(Contrast(palette.p1Body, light), 4.5f, $"P1 / {palette.Theme(i).name} 빛");
                Assert.GreaterOrEqual(Contrast(palette.p2Body, light), 3.5f, $"P2 / {palette.Theme(i).name} 빛");
            }
            Assert.GreaterOrEqual(Mathf.Abs(Luminance(palette.p1Body) - Luminance(palette.p2Body)), 0.25f);
        }

        [Test]
        public void TerrainColors_AreNotPlayerOrHitColors()
        {
            var palette = Palette();
            for (int i = 0; i < palette.ThemeCount; i++)
            {
                var theme = palette.Theme(i);
                foreach (var reserved in new[] { palette.p1Body, palette.p2Body, palette.hit })
                {
                    Assert.AreNotEqual(reserved, theme.terrain, theme.name);
                    Assert.AreNotEqual(reserved, theme.prop, theme.name);
                }
            }
        }
    }
}
