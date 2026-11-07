using System;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP2-1: 맵 파서와 세 맵의 검증.</summary>
    public class ArenaTests
    {
        [TestCase("Box", 5, 3, 0, 0)]
        [TestCase("Towers", 8, 5, 1, 0)]
        [TestCase("Pit", 6, 4, 0, 1)]
        public void Map_ParsesAndMergesBoxes(string name, int solid, int oneWay, int bounce, int spike)
        {
            var arena = ArenaFiles.LoadByName(name);
            Assert.AreEqual(name, arena.Name);
            string counts = $"{name}: 전체 {arena.BoxCount} (단단 {arena.CountBoxes(TerrainKind.Solid)}, 단방향 {arena.CountBoxes(TerrainKind.OneWay)}, 튕김 {arena.CountBoxes(TerrainKind.Bounce)}, 가시 {arena.CountBoxes(TerrainKind.Spike)})";
            TestContext.WriteLine(counts);
            UnityEngine.Debug.Log("[Knockbox][CP2-1] " + counts);
            Assert.AreEqual(solid, arena.CountBoxes(TerrainKind.Solid), counts);
            Assert.AreEqual(oneWay, arena.CountBoxes(TerrainKind.OneWay), counts);
            Assert.AreEqual(bounce, arena.CountBoxes(TerrainKind.Bounce), counts);
            Assert.AreEqual(spike, arena.CountBoxes(TerrainKind.Spike), counts);
        }

        [Test]
        public void AllMapFiles_PassValidation()
        {
            var names = ArenaFiles.ListNames();
            CollectionAssert.IsSupersetOf(names, new[] { "Box", "Towers", "Pit" });
            foreach (string name in names)
                Assert.DoesNotThrow(() => ArenaFiles.LoadByName(name), name);
        }

        [Test]
        public void Box_GeometryMatchesSpec()
        {
            var box = TestWorlds.Box;
            Assert.AreEqual(new Vec2(-12.5f, -8f), box.SpawnFoot0);
            Assert.AreEqual(new Vec2(12.5f, -8f), box.SpawnFoot1);

            bool pillar = false, upper = false;
            for (int i = 0; i < box.BoxCount; i++)
            {
                var b = box.GetBox(i);
                pillar |= b.Kind == TerrainKind.Solid && b.MinX == -1f && b.MaxX == 1f && b.MinY == -8f && b.MaxY == -5f;
                upper |= b.Kind == TerrainKind.OneWay && b.MinX == -4f && b.MaxX == 4f && b.MinY == 3.5f && b.MaxY == 4f;
            }
            Assert.IsTrue(pillar, "가운데 기둥(-1..1, -8..-5)");
            Assert.IsTrue(upper, "위쪽 단방향 발판(-4..4, 3.5..4)");
        }

        [Test]
        public void HalfCellKinds_UseCorrectHalf()
        {
            var towers = ArenaFiles.LoadByName("Towers");
            var pit = ArenaFiles.LoadByName("Pit");
            for (int i = 0; i < towers.BoxCount; i++)
            {
                var b = towers.GetBox(i);
                if (b.Kind == TerrainKind.Bounce)
                    Assert.AreEqual((-8f, -7.5f), (b.MinY, b.MaxY), "튕김 패드는 칸의 아래쪽 절반");
            }
            for (int i = 0; i < pit.BoxCount; i++)
            {
                var b = pit.GetBox(i);
                if (b.Kind == TerrainKind.Spike)
                    Assert.AreEqual((-7f, -6.5f, -10f, 10f), (b.MinY, b.MaxY, b.MinX, b.MaxX), "가시는 칸의 아래쪽 절반");
            }
        }

        const string ValidGrid = @"
            ################################
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..............................#
            #..1........................2..#
            ################################";

        [Test]
        public void ValidGrid_Parses() => Assert.DoesNotThrow(() => TestWorlds.Grid("Valid", ValidGrid));

        [TestCase("#..1....", "#..1.....", "행 길이")]
        [TestCase("#..............................#", "#.1............................#", "'1'이 둘 이상")]
        [TestCase("#..1....", "#..X....", "알 수 없는 문자")]
        [TestCase("#..1....", "#.......", "'1'과 '2'")]
        [TestCase("################################", "###############.################", "테두리")]
        public void InvalidGrid_ReportsError(string find, string replace, string fragment)
        {
            string grid = ReplaceFirst(ValidGrid, find, replace);
            var e = Assert.Throws<FormatException>(() => TestWorlds.Grid("Bad", grid));
            StringAssert.Contains(fragment, e.Message);
        }

        [Test]
        public void WrongRowCount_ReportsError()
        {
            string grid = ReplaceFirst(ValidGrid, "#..............................#", "");
            var e = Assert.Throws<FormatException>(() => TestWorlds.Grid("Bad", grid));
            StringAssert.Contains("행 수", e.Message);
        }

        static string ReplaceFirst(string text, string find, string replace)
        {
            int i = text.IndexOf(find, StringComparison.Ordinal);
            Assert.GreaterOrEqual(i, 0, find);
            return text.Substring(0, i) + replace + text.Substring(i + find.Length);
        }
    }
}
