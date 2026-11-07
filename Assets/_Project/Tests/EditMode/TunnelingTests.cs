using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP2-8: 대시(26), 빠른 낙하(40), 튕김 패드(30) 최대 속도로 두께 0.5 벽을 8방향, 시작 위치 1000가지 이상에서
    /// 들이받아도 통과하지 않는다. 안쪽 4 × 4, 두께 0.5인 벽으로 둘러싼 우리 안에서 시험한다.
    /// </summary>
    public class TunnelingTests
    {
        const float Inner = 2f;
        const float Thickness = 0.5f;

        static ArenaDef Cage()
        {
            float o = Inner + Thickness;
            var boxes = new[]
            {
                new ArenaBox(TerrainKind.Solid, -o, -o, -Inner, o),
                new ArenaBox(TerrainKind.Solid, Inner, -o, o, o),
                new ArenaBox(TerrainKind.Solid, -o, -o, o, -Inner),
                new ArenaBox(TerrainKind.Solid, -o, Inner, o, o),
                // P2가 서 있을 받침(우리 밖, 멀리)
                new ArenaBox(TerrainKind.Solid, 8f, -6f, 12f, -5.5f),
            };
            return new ArenaDef("Cage", boxes, new Vec2(0f, -Inner), new Vec2(10f, -5.5f));
        }

        static readonly Vec2[] Directions =
        {
            new Vec2(1f, 0f), new Vec2(-1f, 0f), new Vec2(0f, 1f), new Vec2(0f, -1f),
            SimMath.Normalize(new Vec2(1f, 1f)), SimMath.Normalize(new Vec2(1f, -1f)),
            SimMath.Normalize(new Vec2(-1f, 1f)), SimMath.Normalize(new Vec2(-1f, -1f)),
        };

        static bool Inside(Vec2 p, float half) =>
            p.X >= -Inner + half - 1e-3f && p.X <= Inner - half + 1e-3f &&
            p.Y >= -Inner + half - 1e-3f && p.Y <= Inner - half + 1e-3f;

        [Test]
        public void Mover_NeverPassesHalfUnitWall()
        {
            var config = SimConfig.CreateDefault();
            var m = config.Movement;
            float half = config.HalfBody;
            var arena = Cage();
            var rng = new SimRng(2026);
            float[] speeds = { m.DashSpeed, m.FastFallMaxSpeed, m.BouncePadSpeed };
            int cases = 0;

            foreach (float speed in speeds)
            {
                foreach (var dir in Directions)
                {
                    for (int s = 0; s < 50; s++)
                    {
                        var position = new Vec2(
                            -Inner + half + rng.NextFloat01() * (2f * (Inner - half)),
                            -Inner + half + rng.NextFloat01() * (2f * (Inner - half)));
                        for (int t = 0; t < 12; t++)
                        {
                            var velocity = dir * speed;
                            var contacts = default(MoveContacts);
                            TerrainCollision.Move(ref position, ref velocity, half, arena, m.SubstepMaxDistance, FighterState.NoBox, ref contacts);
                            Assert.IsTrue(Inside(position, half), $"속도 {speed}, 방향 {dir}, 시작 {s}, 틱 {t}: 벽을 통과 {position}");
                        }
                        cases++;
                    }
                }
            }
            TestContext.WriteLine($"[CP2-8] 이동기 단위 {cases}가지");
            Assert.GreaterOrEqual(cases, 1000);
        }

        [Test]
        public void DashIntoWalls_FromThousandStarts_StaysInside()
        {
            var config = SimConfig.CreateDefault();
            float half = config.HalfBody;
            var arena = Cage();
            var rng = new SimRng(77);
            var p2 = new Vec2(10f, -5.05f);
            int runs = 0;

            for (int d = 0; d < 8; d++)
            {
                for (int s = 0; s < 125; s++)
                {
                    var start = new Vec2(
                        -Inner + half + rng.NextFloat01() * (2f * (Inner - half)),
                        -Inner + half + rng.NextFloat01() * (2f * (Inner - half)));
                    var w = TestWorlds.CreateAt(start, p2, config, arena);
                    int mx = d < 4 ? new[] { 1, -1, 0, 0 }[d] : d < 6 ? 1 : -1;
                    int my = d < 4 ? new[] { 0, 0, 1, -1 }[d] : d % 2 == 0 ? 1 : -1;
                    w.Step(new InputFrame(mx, my, InputButtons.Dash), default);
                    for (int t = 0; t < 14; t++)
                    {
                        // 대시 뒤에도 같은 방향을 누르고 빠른 낙하까지 섞는다.
                        w.Step(new InputFrame(mx, my, InputButtons.None), default);
                        Assert.IsTrue(Inside(w.State.Fighter0.Position, half), $"방향 ({mx},{my}), 시작 {start}, 틱 {t}: {w.State.Fighter0.Position}");
                    }
                    runs++;
                }
            }
            TestContext.WriteLine($"[CP2-8] 대시 종단 {runs}가지");
            Assert.GreaterOrEqual(runs, 1000);
        }
    }
}
