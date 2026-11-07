using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CR-004 대표 맵 Brawl(요청서 6절, D/E 피드백으로 T·ㄴ·┘ 지형). 사방 실제 벽(경계 튕김 없음), 프레임에 닿지 않는 내부 지형, 바닥 양쪽 대칭 스폰.
    /// 최대 속도(대시, 점프 + 공중 대시)로 벽과 지형을 들이받아도 통과하거나 프레임 밖으로 나가지 않는다.
    /// </summary>
    public class BrawlMapTests
    {
        const string Name = MatchRunner.BrawlArenaName;

        static SimConfig Config() => new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, RuleSet.Brawl);

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 1e-4f;

        [Test]
        public void Brawl_ParsesWithRealWallsMirroredAndFloorSpawns()
        {
            var arena = ArenaFiles.LoadByName(Name);
            Assert.AreEqual(36, arena.Columns);
            Assert.AreEqual(20, arena.Rows);
            Assert.IsFalse(arena.HasBoundary, "경계 튕김 대신 실제 벽");
            Assert.AreEqual(-arena.SpawnFoot0.X, arena.SpawnFoot1.X, 1e-5f);
            Assert.AreEqual(arena.SpawnFoot0.Y, arena.SpawnFoot1.Y, 1e-5f);
            Assert.AreEqual(-arena.HalfHeight + 1f, arena.SpawnFoot0.Y, 1e-5f, "스폰은 바닥 위");

            float frameX = arena.HalfWidth - 1f, frameY = arena.HalfHeight - 1f;
            float innerArea = 0f;
            int interior = 0;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                Assert.AreEqual(TerrainKind.Solid, b.Kind, $"{b}");
                bool mirrored = false;
                for (int j = 0; j < arena.BoxCount && !mirrored; j++)
                {
                    var m = arena.GetBox(j);
                    mirrored = m.Kind == b.Kind && Near(m.MinX, -b.MaxX) && Near(m.MaxX, -b.MinX) && Near(m.MinY, b.MinY) && Near(m.MaxY, b.MaxY);
                }
                Assert.IsTrue(mirrored, $"좌우 짝이 없는 상자 {b}");
                bool frame = b.MinX <= -frameX + 1e-4f || b.MaxX >= frameX - 1e-4f || b.MinY <= -frameY + 1e-4f || b.MaxY >= frameY - 1e-4f;
                if (frame)
                    continue;
                // 내부 지형은 프레임에 닿지 않게 띄운다(외곽 표면이 끊기지 않게).
                Assert.Greater(b.MinX, -frameX + 0.5f, $"{b}");
                Assert.Less(b.MaxX, frameX - 0.5f, $"{b}");
                Assert.Greater(b.MinY, -frameY + 0.5f, $"{b}");
                Assert.Less(b.MaxY, frameY - 0.5f, $"{b}");
                interior++;
                innerArea += (b.MaxX - b.MinX) * (b.MaxY - b.MinY);
            }
            float share = innerArea / (4f * frameX * frameY);
            TestContext.WriteLine($"[CR-004 A] 내부 지형 {interior}개, 면적 {share:P1}");
            Assert.AreEqual(6, interior, "T(가로·기둥), ㄴ(세로·가로), 대칭 ┘");
            Assert.That(share, Is.InRange(0.05f, 0.09f));
        }

        [Test]
        public void Brawl_MaxSpeedIntoWalls_NeverPassesOrLeaves()
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName(Name);
            float half = config.HalfBody;
            float frameX = arena.HalfWidth - 1f - half + 1e-3f;
            float frameY = arena.HalfHeight - 1f - half + 1e-3f;
            var p2 = new Vec2(arena.SpawnFoot1.X, arena.SpawnFoot1.Y + half);
            var rng = new SimRng(404);
            int runs = 0;

            for (int s = 0; s < 64; s++)
            {
                Vec2 start;
                do
                {
                    start = new Vec2((rng.NextFloat01() * 2f - 1f) * (frameX - 0.01f), (rng.NextFloat01() * 2f - 1f) * (frameY - 0.01f));
                }
                while (InsideSolid(arena, start, half, 0.05f) || SimMath.Length(start - p2) < 2f);

                for (int d = 0; d < 8; d++)
                {
                    int mx = d < 4 ? new[] { 1, -1, 0, 0 }[d] : d < 6 ? 1 : -1;
                    int my = d < 4 ? new[] { 0, 0, 1, -1 }[d] : d % 2 == 0 ? 1 : -1;
                    for (int pattern = 0; pattern < 2; pattern++)
                    {
                        var w = TestWorlds.CreateAt(start, p2, config, arena);
                        for (int t = 0; t < 40; t++)
                        {
                            // 패턴 0: 바로 대시. 패턴 1: 점프 뒤 공중 대시. 대시 뒤에도 같은 방향을 계속 누른다.
                            var buttons = pattern == 0 ? (t == 0 ? InputButtons.Dash : InputButtons.None)
                                : t == 0 ? InputButtons.Jump : t == 6 ? InputButtons.Dash : InputButtons.None;
                            w.Step(new InputFrame(mx, my, buttons), default);
                            var p = w.State.Fighter0.Position;
                            string where = $"방향 ({mx},{my}), 패턴 {pattern}, 시작 {start}, 틱 {t}: {p}";
                            Assert.IsTrue(System.Math.Abs(p.X) <= frameX && System.Math.Abs(p.Y) <= frameY, "프레임 밖 " + where);
                            Assert.IsFalse(InsideSolid(arena, p, half, -1e-3f), "지형 속 " + where);
                            for (int e = 0; e < w.Events.Count; e++)
                                Assert.AreNotEqual(SimEventType.OutOfBounds, w.Events[e].Type, where);
                        }
                        runs++;
                    }
                }
            }
            TestContext.WriteLine($"[CR-004 A] Brawl 벽 들이받기 {runs}가지");
            Assert.GreaterOrEqual(runs, 1000);
        }

        /// <summary>몸 상자(반 변 half + margin)가 고체 지형과 겹치는가.</summary>
        static bool InsideSolid(ArenaDef arena, Vec2 c, float half, float margin)
        {
            float h = half + margin;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                if (b.Kind == TerrainKind.Solid && c.X + h > b.MinX && c.X - h < b.MaxX && c.Y + h > b.MinY && c.Y - h < b.MaxY)
                    return true;
            }
            return false;
        }
    }
}
