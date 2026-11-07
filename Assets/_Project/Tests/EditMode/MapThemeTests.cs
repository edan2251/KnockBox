using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MapThemeTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void LegacyMapThemeKeepsExistingColorsAndGradientFormula(int index)
        {
            var palette = ScriptableObject.CreateInstance<PaletteAsset>();
            try
            {
                var expected = palette.Theme(index);
                var meta = new ArenaMeta { Theme = index };
                Assert.AreEqual(expected, meta.ResolveTheme(palette));
                meta.MapTheme = new MapThemeSettings();
                Assert.AreEqual(expected, meta.ResolveTheme(palette));
                Assert.AreEqual(expected.light, meta.ResolveTheme(palette).BackgroundAt(.4f));
                Assert.AreEqual(palette.mote, meta.ResolveTheme(palette).Mote(palette));
                Assert.AreEqual(palette.energySoft, meta.ResolveTheme(palette).Highlight(palette));
                Assert.AreEqual(3, palette.ThemeCount);
            }
            finally { Object.DestroyImmediate(palette); }
        }

        [TestCase(MapThemePreset.Lagoon)] [TestCase(MapThemePreset.Violet)]
        [TestCase(MapThemePreset.Sunset)] [TestCase(MapThemePreset.Forest)]
        public void PresetsHaveDistinctGradientEndsAndReadableTerrain(MapThemePreset preset)
        {
            var palette = ScriptableObject.CreateInstance<PaletteAsset>();
            try
            {
                var settings = new MapThemeSettings { mode = MapThemeMode.Preset, preset = preset };
                var theme = settings.Resolve(palette, 2);
                Assert.IsEmpty(settings.Validate()); Assert.IsTrue(theme.backgroundGradient);
                Assert.AreNotEqual(theme.gradientTop, theme.gradientBottom);
                Assert.AreEqual(theme.gradientTop, theme.BackgroundAt(1));
                Assert.AreEqual(theme.gradientBottom, theme.BackgroundAt(0));
                Assert.Greater(theme.terrain.grayscale, theme.prop.grayscale);
                Assert.Greater(theme.terrain.grayscale, theme.gradientTop.grayscale);
                Assert.AreEqual(theme.highlight, theme.Mote(palette));
            }
            finally { Object.DestroyImmediate(palette); }
        }

        [Test]
        public void ImportOwnsAnIndependentCopyOfAllThemeSettings()
        {
            var palette=ScriptableObject.CreateInstance<PaletteAsset>();
            var first=ScriptableObject.CreateInstance<MapDefinition>();
            var second=ScriptableObject.CreateInstance<MapDefinition>();
            var theme=new MapThemeSettings{mode=MapThemeMode.Custom,preset=MapThemePreset.Forest,
                gradientTop=palette.p1Shade,gradientBottom=palette.p2Shade,terrain=palette.p1Body,
                prop=palette.p2Body,highlight=palette.energySoft,gradientCurve=1.75f};
            var meta=new ArenaMeta{Theme=2,MapTheme=theme};
            try
            {
                var arena=ArenaFiles.LoadByName("Brawl");
                first.Import(arena,meta,"first","First");second.Import(arena,first.Metadata(),"second","Second");
                Assert.AreEqual(JsonUtility.ToJson(theme),JsonUtility.ToJson(first.Metadata().MapTheme));
                Assert.AreEqual(JsonUtility.ToJson(theme),JsonUtility.ToJson(second.Metadata().MapTheme));
                Assert.AreNotSame(theme,first.Metadata().MapTheme);
                Assert.AreNotSame(first.Metadata().MapTheme,second.Metadata().MapTheme);
                second.Metadata().MapTheme.gradientCurve=.5f;
                second.Metadata().MapTheme.terrain=palette.p2Shade;
                Assert.AreEqual(1.75f,first.Metadata().MapTheme.gradientCurve);
                Assert.AreEqual(palette.p1Body,first.Metadata().MapTheme.terrain);
                Assert.AreEqual(1.75f,theme.gradientCurve);
                Assert.AreEqual(arena.ComputeGeometryChecksum(),second.ToArena().ComputeGeometryChecksum());
            }
            finally{Object.DestroyImmediate(first);Object.DestroyImmediate(second);Object.DestroyImmediate(palette);}
        }

        [Test]
        public void SerializedMapColorsReopenAndInvalidateVisualCacheWithoutChangingArenaHash()
        {
            var palette = ScriptableObject.CreateInstance<PaletteAsset>();
            var source = AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/BrawlOriginal.asset");
            Assert.IsNotNull(source);
            var copy = Object.Instantiate(source);
            var reopened = ScriptableObject.CreateInstance<MapDefinition>();
            try
            {
                var before = copy.Metadata().ResolveTheme(palette);
                var arena = copy.ToArena(); uint legacy = arena.ComputeChecksum(), geometry = arena.ComputeGeometryChecksum();
                var settings = copy.Metadata().MapTheme;
                settings.mode = MapThemeMode.Custom; settings.gradientCurve = 1.4f;
                settings.gradientBottom = palette.p1Shade;
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(copy), reopened);
                var changed = reopened.Metadata().ResolveTheme(palette);
                Assert.AreEqual(palette.p1Shade, changed.gradientBottom);
                Assert.AreEqual(1.4f, changed.gradientCurve);
                Assert.AreNotEqual(before.name, changed.name, "Color edits must rebuild cached terrain/background.");
                Assert.AreEqual(legacy, reopened.ToArena().ComputeChecksum());
                Assert.AreEqual(geometry, reopened.ToArena().ComputeGeometryChecksum());
                reopened.Metadata().MapTheme.gradientCurve = float.NaN; Assert.IsNotEmpty(reopened.Validate());
            }
            finally { Object.DestroyImmediate(palette); Object.DestroyImmediate(copy); Object.DestroyImmediate(reopened); }
        }
    }
}
