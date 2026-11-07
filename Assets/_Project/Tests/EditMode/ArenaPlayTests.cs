using System.Collections.Generic;
using System.Text;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP8-1: 04-arena.md 5절 맵 검증(파서 검증과 Box 기둥은 CP2 테스트가 맡는다).
    /// 도달 가능성은 사람이 쓸 만한 입력 덩어리(걷기, 점프 + 공중 조작 + 공중 점프·대시, 발판 내려가기, 벽 사이 오르기)를
    /// 착지 상태마다 모두 시도하는 너비 우선 탐색으로 확인한다. 상대 파이터는 자기 시작 위치에 가만히 있다.
    /// CP8-2: 가시 규칙.
    /// </summary>
    public class ArenaPlayTests
    {
        const float FloorY = -7.55f;

        static SimConfig Config(int maxHealth = 100)
        {
            var combat = CombatParams.Default;
            combat.MaxHealth = maxHealth;
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Swing);
        }

        static bool Has(SimWorld w, SimEventType type, int fighter)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && w.Events[i].Fighter == fighter)
                    return true;
            }
            return false;
        }

        // 도달 가능성 ----------------------------------------------------------

        /// <summary>입력 덩어리 하나: 틱 → 입력. Length 뒤로는 빈 입력.</summary>
        sealed class Macro
        {
            public string Name;
            public int Length;
            public System.Func<int, InputFrame> Input;
        }

        static List<Macro> Macros()
        {
            var list = new List<Macro>();
            foreach (int d in new[] { -1, 1 })
            {
                foreach (int n in new[] { 8, 20, 45 })
                    list.Add(new Macro { Name = $"walk{d}x{n}", Length = n, Input = t => new InputFrame(d, 0, InputButtons.None) });
                // 벽 사이 오르기: 방향을 16틱마다 바꾸며 점프.
                list.Add(new Macro
                {
                    Name = $"climb{d}",
                    Length = 96,
                    Input = t => new InputFrame((t / 16) % 2 == 0 ? d : -d, 0, t % 16 < 12 ? InputButtons.Jump : InputButtons.None),
                });
            }
            foreach (int d in new[] { -1, 0, 1 })
            {
                list.Add(new Macro { Name = $"drop{d}", Length = 30, Input = t => t == 0 ? new InputFrame(0, -1, InputButtons.Jump) : new InputFrame(d, 0, InputButtons.None) });
                foreach (int pre in new[] { 0, 12 })
                foreach (int air in new[] { -1, 0, 1 })
                foreach (bool airJump in new[] { false, true })
                foreach (int dash in new[] { 0, 1 })
                {
                    int p = pre, a = air, ds = dash;
                    bool aj = airJump;
                    list.Add(new Macro
                    {
                        Name = $"jump d{d} pre{p} air{a} aj{aj} dash{ds}",
                        Length = p + 70,
                        Input = t =>
                        {
                            if (t < p)
                                return new InputFrame(d, 0, InputButtons.None);
                            int k = t - p;
                            var buttons = InputButtons.None;
                            if (k < 20)
                                buttons |= InputButtons.Jump;
                            if (aj && k >= 24 && k < 40)
                                buttons |= InputButtons.Jump;
                            int y = 0;
                            if (ds == 1 && k == 14)
                            {
                                buttons |= InputButtons.Dash;
                                y = 1;
                            }
                            return new InputFrame(k < 2 ? d : a, y, buttons);
                        },
                    });
                }
            }
            return list;
        }

        /// <summary>서 있을 수 있는 발판: 단방향 발판, 튕김 패드, 그리고 위가 비어 있는 단단한 지형(맨 위 테두리 제외). 가시는 제외.</summary>
        static List<int> PlatformBoxes(ArenaDef arena, float half)
        {
            var list = new List<int>();
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                if (b.Kind == TerrainKind.Spike || b.MaxY >= arena.HalfHeight - 0.5f)
                    continue;
                if (!b.IsSolidBlock)
                {
                    list.Add(i);
                    continue;
                }
                for (float x = b.MinX + half; x <= b.MaxX - half + 1e-3f; x += 0.25f)
                {
                    if (FreeAt(arena, x, b.MaxY + half + 0.01f, half))
                    {
                        list.Add(i);
                        break;
                    }
                }
            }
            return list;
        }

        static bool FreeAt(ArenaDef arena, float x, float y, float half)
        {
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                if (b.Kind == TerrainKind.OneWay)
                    continue;
                if (x + half > b.MinX + 1e-3f && x - half < b.MaxX - 1e-3f && y + half > b.MinY + 1e-3f && y - half < b.MaxY - 1e-3f)
                    return false;
            }
            // 강체 구조물(CP14부터)은 상자가 아니지만 놓인 자리는 차 있다(시소가 덮은 받침 윗면은 설 곳이 아님).
            for (int i = 0; i < arena.DynamicPropCount; i++)
            {
                var p = arena.GetProp(arena.DynamicProp(i));
                if (x + half > p.MinX + 1e-3f && x - half < p.MaxX - 1e-3f && y + half > p.MinY + 1e-3f && y - half < p.MaxY - 1e-3f)
                    return false;
            }
            return true;
        }

        static FighterState Get(in SimState s, int k) => k == 0 ? s.Fighter0 : s.Fighter1;

        /// <summary>
        /// 입력 덩어리를 실행하고, 땅에 서서 멈추면 그 상태를 돌려준다(못 서면 false).
        /// 튕김 패드는 위에 설 수 없으므로(닿으면 튕김) 닿은 패드를 touched에 더한다.
        /// </summary>
        static bool Run(SimConfig config, ArenaDef arena, in SimState start, int fighter, Macro macro, HashSet<int> touched, out SimState settled)
        {
            var w = new SimWorld(config, arena, start);
            for (int t = 0; t < macro.Length + 150; t++)
            {
                if (t >= macro.Length)
                {
                    var f = Get(w.State, fighter);
                    // 구조물 강체 위에 선 것도 착지로 센다(CP14, 그 위에서 다음 입력 덩어리를 시작).
                    bool onProp = f.GroundBox == FighterState.OnPropBox;
                    if (f.Grounded && (f.GroundBox >= 0 || onProp) && f.InMovementState && (onProp || SimMath.Abs(f.Velocity.X) < 0.05f))
                    {
                        settled = w.State;
                        return true;
                    }
                }
                var input = t < macro.Length ? macro.Input(t) : default;
                if (fighter == 0)
                    w.Step(input, default);
                else
                    w.Step(default, input);
                if (Has(w, SimEventType.PadBounce, fighter))
                    touched.Add(PadUnder(arena, Get(w.State, fighter).Position.X));
            }
            settled = default;
            return false;
        }

        static int PadUnder(ArenaDef arena, float x)
        {
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                if (b.Kind == TerrainKind.Bounce && x > b.MinX - 0.5f && x < b.MaxX + 0.5f)
                    return i;
            }
            return -1;
        }

        static HashSet<int> Explore(ArenaDef arena, int fighter, in SimState start, out int states)
        {
            var config = Config(100000);
            var macros = Macros();
            var reached = new HashSet<int>();
            var seen = new HashSet<long>();
            var queue = new Queue<SimState>();
            queue.Enqueue(start);
            states = 0;
            while (queue.Count > 0 && states < 160)
            {
                var s = queue.Dequeue();
                states++;
                foreach (var macro in macros)
                {
                    if (!Run(config, arena, s, fighter, macro, reached, out var settled))
                        continue;
                    var f = Get(settled, fighter);
                    if (f.GroundBox >= 0)
                        reached.Add(f.GroundBox);
                    long key = ((long)(f.GroundBox == FighterState.OnPropBox ? 1000 + f.OnProp : f.GroundBox) << 32) | (uint)SimMath.RoundToInt(f.Position.X / 1.5f);
                    if (seen.Add(key))
                        queue.Enqueue(settled);
                }
            }
            return reached;
        }

        [TestCase("Box", 0)]
        [TestCase("Box", 1)]
        [TestCase("Towers", 0)]
        [TestCase("Towers", 1)]
        [TestCase("Pit", 0)]
        [TestCase("Pit", 1)]
        [TestCase("Chimes", 0)]
        [TestCase("Chimes", 1)]
        [TestCase("Gantry", 0)]
        [TestCase("Gantry", 1)]
        [TestCase("Mobile", 0)]
        [TestCase("Mobile", 1)]
        [TestCase("Ruins", 0)]
        [TestCase("Ruins", 1)]
        [TestCase("Scales", 0)]
        [TestCase("Scales", 1)]
        [TestCase("Totems", 0)]
        [TestCase("Totems", 1)]
        // 전체 EditMode 시간의 약 90%(맵 v2 한 건에 36~160초). 개발 중에는 빼고 CP 끝에 돌린다(CP17-2).
        [Category(TestCategories.Slow)]
        [Category(TestCategories.LegacyCombat)]
        public void EveryPlatform_ReachableFromSpawn(string map, int fighter)
        {
            var arena = ArenaFiles.LoadByName(map);
            var config = Config(100000);
            var start = SimState.CreateRound(config, arena, 1, MatchPhase.Fighting);
            var reached = Explore(arena, fighter, start, out int states);
            var targets = PlatformBoxes(arena, config.HalfBody);
            var missing = new StringBuilder();
            foreach (int box in targets)
            {
                if (!reached.Contains(box))
                    missing.AppendLine(arena.GetBox(box).ToString());
            }
            string report = $"[Knockbox][CP8-1] {map} P{fighter + 1}: 발판 {targets.Count}개 중 {targets.Count - (missing.Length == 0 ? 0 : missing.ToString().Split('\n').Length - 1)}개 도달, 탐색한 착지 상태 {states}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(string.Empty, missing.ToString(), $"{map} P{fighter + 1}에서 닿지 못한 발판");
        }

        [Test]
        public void Towers_BouncePadLeadsIntoMiddleRoom()
        {
            // 패드(높이 0.5 돌기) 위로 짧게 뛰어올라 튕기고, 방 바닥보다 높이 올라간 뒤 오른쪽으로 조금 움직여
            // 두 탑 사이 가운데 방의 바닥(높이 -3) 위에 선다.
            var arena = ArenaFiles.LoadByName("Towers");
            var w = TestWorlds.CreateAt(new Vec2(-4f, FloorY), new Vec2(12.5f, FloorY), Config(), arena);
            bool padded = false, inside = false;
            int steer = -1;
            for (int t = 0; t < 180 && !inside; t++)
            {
                var f0 = w.State.Fighter0;
                int x = 0;
                if (!padded)
                    x = t < 12 ? 1 : 0;
                else if (f0.Position.Y > -2.4f && steer < 0)
                    steer = t;
                if (steer >= 0 && t - steer < 14)
                    x = 1;
                var buttons = !padded && t < 6 ? InputButtons.Jump : InputButtons.None;
                w.Step(new InputFrame(x, 0, buttons), default);
                padded |= Has(w, SimEventType.PadBounce, 0);
                var f = w.State.Fighter0;
                inside = padded && f.Grounded && f.Position.X > -5f && f.Position.X < 5f && f.Position.Y > -3f;
            }
            Assert.IsTrue(padded, "튕김 패드에 닿아야 함");
            Assert.IsTrue(inside, "튕겨서 가운데 방 안에 서야 함");
        }

        [Test]
        public void Pit_RecoverFromSpikesToPlateau()
        {
            // 가시에 떨어진 상태에서 시작해 조작만으로 양쪽 대지(높이 -1의 단단한 지형) 중 하나에 선다.
            var arena = ArenaFiles.LoadByName("Pit");
            var config = Config(100000);
            var start = SimState.CreateRound(config, arena, 1, new Vec2(-6f, -5.5f), new Vec2(12.5f, -0.55f), MatchPhase.Fighting);
            var w = new SimWorld(config, arena, start);
            bool spiked = false;
            for (int t = 0; t < 30 && !spiked; t++)
            {
                w.Step(default, default);
                spiked = Has(w, SimEventType.Spike, 0);
            }
            Assert.IsTrue(spiked, "가시에 떨어짐");
            var reached = Explore(arena, 0, w.State, out _);
            bool plateau = false;
            foreach (int box in reached)
            {
                var b = arena.GetBox(box);
                plateau |= b.Kind == TerrainKind.Solid && SimMath.Abs(b.MaxY - (-1f)) < 1e-3f;
            }
            Assert.IsTrue(plateau, "가시 구덩이에서 대지 위로 돌아올 수 있어야 함");
        }

        // CP8-2 ---------------------------------------------------------------

        [Test]
        public void Spike_DamagesBouncesStunsAndRestoresAirMoves()
        {
            var arena = ArenaFiles.LoadByName("Pit");
            var w = TestWorlds.CreateAt(new Vec2(-6f, -5.5f), new Vec2(12.5f, -0.55f), Config(), arena);
            w.State.Fighter0.AirJumpsLeft = 0;
            w.State.Fighter0.DashCharges = 0;
            int hitTick = -1;
            for (int t = 1; t <= 30 && hitTick < 0; t++)
            {
                w.Step(default, default);
                if (Has(w, SimEventType.Spike, 0))
                    hitTick = t;
            }
            Assert.Greater(hitTick, 0, "가시에 닿음");
            var f = w.State.Fighter0;
            var c = CombatParams.Default;
            Assert.AreEqual(100 - c.SpikeDamage, f.Health, "데미지 15");
            Assert.AreEqual(c.SpikeBounceSpeed, f.Velocity.Y, 1e-4f, "vy = 20");
            Assert.AreEqual(ActionState.Hitstun, f.Action);
            Assert.AreEqual(c.SpikeHitstun, f.StunTicks, "경직 20틱");
            Assert.AreEqual(c.SpikeImmunityTicks, f.SpikeImmunityTicks, "면역 45틱");
            Assert.AreEqual(MovementParams.Default.AirJumps, f.AirJumpsLeft, "공중 점프 회복");
            Assert.AreEqual(MovementParams.Default.DashCharges, f.DashCharges, "대시 회복");

            // 면역 동안 다시 내려와도(바닥처럼 섬) 데미지가 없고, 면역이 끝나는 틱에 다시 닿는다.
            int second = -1;
            for (int t = 1; t <= c.SpikeImmunityTicks && second < 0; t++)
            {
                w.Step(default, default);
                if (Has(w, SimEventType.Spike, 0))
                    second = t;
            }
            Assert.AreEqual(c.SpikeImmunityTicks, second, "면역 45틱 뒤에 다시 가시");
            Assert.AreEqual(100 - 2 * c.SpikeDamage, w.State.Fighter0.Health);
        }

        [Test]
        public void Spike_IsMirrored()
        {
            var arena = ArenaFiles.LoadByName("Pit");
            var normal = TestWorlds.CreateAt(new Vec2(-6f, -5.5f), new Vec2(12.5f, -0.55f), Config(), arena);
            var mirrored = TestWorlds.CreateAt(new Vec2(-12.5f, -0.55f), new Vec2(6f, -5.5f), Config(), arena);
            for (int t = 0; t < 180; t++)
            {
                var input = new InputFrame(t % 40 < 20 ? 1 : -1, 0, t % 30 == 10 ? InputButtons.Jump : InputButtons.None);
                normal.Step(input, default);
                mirrored.Step(default, SymmetryTests.Mirror(input));
                SymmetryTests.AssertMirrored(normal.State.Fighter0, mirrored.State.Fighter1, t + 1, "가시 위");
                Assert.AreEqual(normal.State.Fighter0.SpikeImmunityTicks, mirrored.State.Fighter1.SpikeImmunityTicks);
            }
        }
    }
}
