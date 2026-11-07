using System.Collections.Generic;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP19: 투사체 풀과 바위 폼(10-forms-and-growth.md 4.3·4.4). 수치 기대값은 RockParams·EnergyParams에서 계산한다.
    /// </summary>
    [Category(TestCategories.LegacyCombat)]
    public class ProjectileTests
    {
        const float FloorY = -7.55f;
        static ArenaDef flat;
        static ArenaDef Flat => flat ??= MovementTests.Build("RockFlat", (c, r) => '\0');

        static SimConfig RockConfig(bool energy = true, System.Func<CombatParams, CombatParams> change = null)
        {
            var combat = CombatParams.Default;
            if (change != null)
                combat = change(combat);
            var rules = new RuleSet
            {
                StartForm = FighterForm.Rock,
                Switches = new FeatureSwitches { EnergyEnabled = energy },
            };
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, rules);
        }

        static SimWorld World(SimConfig config, float x0 = -3f, float x1 = 3f, ArenaDef arena = null, float y0 = FloorY, float y1 = FloorY) =>
            TestWorlds.CreateAt(new Vec2(x0, y0), new Vec2(x1, y1), config, arena ?? Flat);

        static InputFrame In(int x, int y, InputButtons b) => new InputFrame(x, y, b);

        static int Count(SimWorld w, SimEventType type, int fighter = -2)
        {
            int n = 0;
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == type && (fighter == -2 || w.Events[i].Fighter == fighter))
                    n++;
            }
            return n;
        }

        static bool EndedWith(SimWorld w, ProjectileEndReason reason)
        {
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Type == SimEventType.ProjectileEnd && (int)w.Events[i].Strength == (int)reason)
                    return true;
            }
            return false;
        }

        static void Inject(SimWorld w, int slot, int owner, Vec2 position, Vec2 velocity, bool pierceFighter = false, bool pierceProp = false)
        {
            ProjectileState.Get(ref w.State.Projectiles, slot) = new Projectile
            {
                Active = true,
                Owner = owner,
                Position = position,
                Velocity = velocity,
                Reflectable = true,
                PierceFighter = pierceFighter,
                PierceProp = pierceProp,
                SpawnTick = w.State.Tick,
            };
        }

        /// <summary>
        /// 경계가 있는 36 × 20 경기장. 가운데에 두께 0.5의 세로 판(kind), 파이터는 멀리 양쪽 바닥 위.
        /// spinnerPlate면 같은 자리에 축에 매달린 강체 판(상자 대신의 움직이는 구조물)을 둔다.
        /// </summary>
        static ArenaDef PlateArena(TerrainKind kind, bool horizontal, bool spinnerPlate = false)
        {
            var boxes = new List<ArenaBox>
            {
                new ArenaBox(TerrainKind.Solid, -17f, -9f, -13f, -8f),
                new ArenaBox(TerrainKind.Solid, 13f, -9f, 17f, -8f),
            };
            if (!spinnerPlate)
            {
                boxes.Add(horizontal
                    ? new ArenaBox(kind, -5f, -0.25f, 5f, 0.25f)
                    : new ArenaBox(kind, -0.25f, -5f, 0.25f, 5f));
            }
            var arena = new ArenaDef("Plate", 36, 20, true, boxes.ToArray(), new Vec2(-15f, -8f), new Vec2(15f, -8f));
            if (!spinnerPlate)
                return arena;
            var half = horizontal ? new Vec2(5f, 0.25f) : new Vec2(0.25f, 5f);
            return arena.WithProps(new[]
            {
                new PropDef { Kind = PropKind.Spinner, Group = 0, Center = Vec2.Zero, HalfSize = half, HasAnchor = true, Anchor = Vec2.Zero, AnchorProp = -1 },
            });
        }

        // CP19-1 ----------------------------------------------------------------

        [Test]
        public void PoolFull_ReplacesOldest_WithoutCorruption()
        {
            // 느리고 오래 사는 투사체로 한도를 넘겨 쏜다(에너지 끔).
            var config = RockConfig(false, c => { c.Rock.Speed = 0.5f; c.Rock.LifetimeTicks = 100000; return c; });
            var w = World(config, -3f, 12f);
            int shots = ProjectileState.PerFighter + 4, replaced = 0, fired = 0;
            var spawnTicks = new List<int>();
            for (int k = 0; k < shots; k++)
            {
                for (int t = 0; t <= config.Combat.Rock.TotalTicks; t++)
                {
                    w.Step(t == 0 ? In(-1, 1, InputButtons.Attack) : In(-1, 1, InputButtons.None), default);
                    replaced += Count(w, SimEventType.ProjectileEnd, 0);
                    fired += Count(w, SimEventType.ProjectileFire, 0);
                    if (Count(w, SimEventType.ProjectileFire, 0) > 0)
                        spawnTicks.Add(w.State.Tick);
                }
                Assert.LessOrEqual(w.State.Projectiles.CountActive(0), ProjectileState.PerFighter);
                Assert.AreEqual(0, w.State.Projectiles.CountActive(1), "상대 칸을 건드리지 않음");
            }
            Assert.AreEqual(shots, fired);
            Assert.AreEqual(shots - ProjectileState.PerFighter, replaced, "넘친 만큼 가장 오래된 것을 지움");
            // 남은 것은 가장 최근 PerFighter개.
            var alive = new List<int>();
            for (int i = 0; i < ProjectileState.Capacity; i++)
            {
                var p = ProjectileState.At(w.State.Projectiles, i);
                if (p.Active)
                    alive.Add(p.SpawnTick);
            }
            alive.Sort();
            CollectionAssert.AreEqual(spawnTicks.GetRange(shots - ProjectileState.PerFighter, ProjectileState.PerFighter), alive);
        }

        // CP19-2 ----------------------------------------------------------------

        [Test]
        public void ProjectileScene_Deterministic_SaveRestore_SyncTest_Replay()
        {
            const int ticks = 3600;
            const uint seed = 191;
            var config = RockConfig();
            var arena = TestWorlds.Box;
            TestWorlds.RandomInputs(seed, ticks, out var p1, out var p2);

            int fires = 0;
            uint Run(bool count)
            {
                var w = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
                for (int t = 0; t < ticks; t++)
                {
                    w.Step(p1[t], p2[t]);
                    if (count)
                        fires += Count(w, SimEventType.ProjectileFire);
                }
                return w.ComputeChecksum();
            }
            uint first = Run(true);
            Assert.Greater(fires, 10, "투사체가 날아다니는 장면");
            Assert.AreEqual(first, Run(false));

            var a = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            for (int t = 0; t < ticks / 2; t++)
                a.Step(p1[t], p2[t]);
            var saved = a.State;
            for (int t = ticks / 2; t < ticks; t++)
                a.Step(p1[t], p2[t]);
            a.State = saved;
            for (int t = ticks / 2; t < ticks; t++)
                a.Step(p1[t], p2[t]);
            Assert.AreEqual(first, a.ComputeChecksum(), "저장·복원");

            var sync = new SyncTest<SimWorld>(TestWorlds.Create(MatchPhase.Fighting, seed, config, arena));
            for (int t = 0; t < ticks; t++)
                sync.Advance(p1[t], p2[t]);
            Assert.AreEqual(0, sync.MismatchCount, "SyncTest");

            var start = TestWorlds.Create(MatchPhase.Fighting, seed, config, arena);
            var recording = InputRecording.Begin(config, arena, seed, start.State.Fighter0.Position, start.State.Fighter1.Position,
                MatchPhase.Fighting, config.Rules.Format());
            for (int t = 0; t < ticks; t++)
                recording.Append(p1[t], p2[t]);
            Assert.AreEqual(first, InputRecording.FromBytes(recording.ToBytes()).Replay(config, arena), "입력 기록 재생");
        }

        // CP19-3 ----------------------------------------------------------------

        [TestCase(TerrainKind.Solid, false, false)]
        [TestCase(TerrainKind.OneWay, true, false)]
        [TestCase(TerrainKind.Prop, false, false)]
        [TestCase(TerrainKind.Solid, false, true)]
        public void FastProjectile_NeverTunnelsThroughHalfUnitPlate(TerrainKind kind, bool horizontal, bool spinnerPlate)
        {
            var config = RockConfig();
            float r = config.Combat.Rock.Radius;
            const float speed = 240f; // 틱당 4 유닛: 판(0.5) + 지름보다 훨씬 길다.
            var arena = PlateArena(kind, horizontal, spinnerPlate);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int deg = -60; deg <= 60; deg += 15)
                {
                    // 판의 법선 축을 따라 바깥(side)에서 판 쪽으로, 판과 비스듬히.
                    float rad = deg * 0.017453292f;
                    float along = (float)System.Math.Sin(rad), across = (float)System.Math.Cos(rad);
                    var normal = horizontal ? new Vec2(0f, side) : new Vec2(side, 0f);
                    var tangent = horizontal ? new Vec2(1f, 0f) : new Vec2(0f, 1f);
                    var dir = normal * -across + tangent * along;
                    var start = normal * 2f + tangent * (-along * 2f / across);
                    var w = World(config, -15f, 15f, arena, -7.55f, -7.55f);
                    Inject(w, 0, 0, start, dir * speed);
                    bool ended = false;
                    for (int t = 0; t < 10 && !ended; t++)
                    {
                        w.Step(default, default);
                        var p = ProjectileState.At(w.State.Projectiles, 0);
                        var pos = ended ? Vec2.Zero : p.Position;
                        float depth = Vec2.Dot(pos, normal);
                        if (p.Active)
                            Assert.Greater(depth, 0.25f, $"{kind} {side} {deg}°: 판을 지나감({pos})");
                        ended = !p.Active;
                    }
                    Assert.IsTrue(ended, $"{kind} {side} {deg}°: 판에 막힘");
                    bool prop = kind == TerrainKind.Prop || spinnerPlate;
                    Assert.IsTrue(EndedWith(w, prop ? ProjectileEndReason.Prop : ProjectileEndReason.Terrain), $"{kind} {side} {deg}°: 막힌 이유");
                }
            }
        }

        // CP19-4 ----------------------------------------------------------------

        [Test]
        public void Boundary_EndsProjectile()
        {
            var w = World(RockConfig(), -15f, 15f, PlateArena(TerrainKind.Solid, false), -7.55f, -7.55f);
            Inject(w, 0, 0, new Vec2(-8f, 6f), new Vec2(0f, 30f));
            bool ended = false;
            for (int t = 0; t < 60 && !ended; t++)
            {
                w.Step(default, default);
                ended = EndedWith(w, ProjectileEndReason.Boundary);
            }
            Assert.IsTrue(ended);
            Assert.AreEqual(0, w.State.Projectiles.CountActive(-1));
        }

        [Test]
        public void PierceProp_PassesPropsAndBoxes_ButTerrainBlocks()
        {
            foreach (bool spinnerPlate in new[] { false, true })
            {
                var arena = PlateArena(TerrainKind.Prop, false, spinnerPlate);
                foreach (bool pierce in new[] { false, true })
                {
                    var w = World(RockConfig(), -15f, 15f, arena, -7.55f, -7.55f);
                    Inject(w, 0, 0, new Vec2(-4f, 2f), new Vec2(30f, 0f), pierceProp: pierce);
                    var reasons = new List<int>();
                    for (int t = 0; t < 120 && w.State.Projectiles.CountActive(-1) > 0; t++)
                    {
                        w.Step(default, default);
                        for (int i = 0; i < w.Events.Count; i++)
                        {
                            if (w.Events[i].Type == SimEventType.ProjectileEnd)
                                reasons.Add((int)w.Events[i].Strength);
                        }
                    }
                    var expected = pierce ? ProjectileEndReason.Boundary : ProjectileEndReason.Prop;
                    CollectionAssert.AreEqual(new[] { (int)expected }, reasons, $"spinner {spinnerPlate}, pierce {pierce}");
                }
            }
            // 관통이어도 지형은 막는다.
            var solid = World(RockConfig(), -15f, 15f, PlateArena(TerrainKind.Solid, false), -7.55f, -7.55f);
            Inject(solid, 0, 0, new Vec2(-4f, 2f), new Vec2(30f, 0f), pierceProp: true);
            bool blocked = false;
            for (int t = 0; t < 30 && !blocked; t++)
            {
                solid.Step(default, default);
                blocked = EndedWith(solid, ProjectileEndReason.Terrain);
            }
            Assert.IsTrue(blocked);
        }

        [Test]
        public void PierceFighter_FromData_HitsOnceAndContinues_TerrainStillBlocks()
        {
            foreach (bool pierce in new[] { false, true })
            {
                var config = RockConfig(false, c => { c.Rock.PierceFighter = pierce; return c; });
                // 오른쪽 벽 앞에 선 P2를 P1이 쏜다.
                var w = World(config, -6f, 0f);
                int hits = 0, fires = 0;
                var ends = new List<int>();
                for (int t = 0; t < 120; t++)
                {
                    w.Step(t == 0 ? In(1, 0, InputButtons.Attack) : default, default);
                    hits += Count(w, SimEventType.Hit, 1);
                    fires += Count(w, SimEventType.ProjectileFire, 0);
                    for (int i = 0; i < w.Events.Count; i++)
                    {
                        if (w.Events[i].Type == SimEventType.ProjectileEnd)
                            ends.Add((int)w.Events[i].Strength);
                    }
                }
                Assert.AreEqual(1, fires);
                Assert.AreEqual(1, hits, $"pierce {pierce}: 한 번만 맞힘");
                Assert.AreEqual(config.Combat.MaxHealth - config.Combat.Rock.Damage, w.State.Fighter1.Health);
                var expected = pierce ? ProjectileEndReason.Terrain : ProjectileEndReason.Fighter;
                CollectionAssert.AreEqual(new[] { (int)expected }, ends, $"pierce {pierce}");
            }
        }

        // CP19-5 ----------------------------------------------------------------

        [Test]
        public void RockEnergy_CheckAtStart_SpendOnceAtFire()
        {
            var config = RockConfig();
            ref readonly var e = ref config.Combat.Energy;
            ref readonly var rock = ref config.Combat.Rock;

            var poor = World(config);
            poor.State.Fighter0.Energy = e.ShotCost - 1;
            poor.State.Fighter0.EnergyRegenDelay = e.RegenDelayTicks;
            poor.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreNotEqual(ActionState.Shoot, poor.State.Fighter0.Action, "비용 미만이면 시작하지 않음");

            var w = World(config);
            int before = w.State.Fighter0.Energy;
            int fires = 0;
            for (int t = 0; t <= rock.TotalTicks; t++)
            {
                w.Step(t == 0 ? In(0, 0, InputButtons.Attack) : default, default);
                fires += Count(w, SimEventType.ProjectileFire, 0);
                if (fires == 0)
                    Assert.AreEqual(before, w.State.Fighter0.Energy, $"틱 {t}: 선딜 중 변화 없음");
                else
                    Assert.AreEqual(before - e.ShotCost, w.State.Fighter0.Energy, $"틱 {t}: 발사할 때 한 번");
            }
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void RockStartupInterruptedByHit_SpendsNothing_NoShot()
        {
            var config = RockConfig();
            var w = World(config, -2f, 2f);
            int before = w.State.Fighter0.Energy;
            int p1Fires = 0;
            bool hit = false;
            for (int t = 0; t < 40; t++)
            {
                // P2가 먼저 쏘고, P1은 P2의 투사체가 닿기 직전에 선딜을 시작한다.
                var p2 = t == 0 ? In(-1, 0, InputButtons.Attack) : default;
                var p1 = t == config.Combat.Rock.StartupTicks ? In(1, 0, InputButtons.Attack) : default;
                w.Step(p1, p2);
                p1Fires += Count(w, SimEventType.ProjectileFire, 0);
                hit |= Count(w, SimEventType.Hit, 0) > 0;
            }
            Assert.IsTrue(hit, "P1이 선딜 중 맞음");
            Assert.AreEqual(0, p1Fires, "끊긴 선딜은 발사하지 않음");
            Assert.AreEqual(before, w.State.Fighter0.Energy, "소비 없음");
        }

        [Test]
        public void RockShortAtFire_CancelsAndLeavesEvent()
        {
            var config = RockConfig();
            int cost = config.Combat.Energy.ShotCost;
            var w = World(config);
            w.Step(In(0, 0, InputButtons.Attack), default);
            Assert.AreEqual(ActionState.Shoot, w.State.Fighter0.Action);
            w.State.Fighter0.Energy = cost - 1;
            int fires = 0, shorts = 0;
            for (int t = 0; t < config.Combat.Rock.TotalTicks; t++)
            {
                w.Step(default, default);
                fires += Count(w, SimEventType.ProjectileFire, 0);
                shorts += Count(w, SimEventType.EnergyShort, 0);
            }
            Assert.AreEqual(0, fires);
            Assert.AreEqual(1, shorts);
            Assert.LessOrEqual(w.State.Fighter0.Energy, cost - 1 + config.Combat.Rock.TotalTicks * config.Combat.Energy.RegenPerTick, "소비 없음(회복만)");
            Assert.AreEqual(0, w.State.Projectiles.CountActive(-1));
        }

        // CP19-6 ----------------------------------------------------------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        public void MirroredShots_GiveMirroredResults(int moveY)
        {
            var config = RockConfig();
            var w = World(config, -4f, 4f, TestWorlds.Box);
            for (int t = 0; t < 150; t++)
            {
                var press = t == 0 || t == 40 ? InputButtons.Attack : InputButtons.None;
                w.Step(In(1, moveY, press), In(-1, moveY, press));
                var a = w.State.Fighter0;
                var b = w.State.Fighter1;
                Assert.AreEqual(a.Position.X, -b.Position.X, 1e-4f, $"틱 {t}: 거울 x");
                Assert.AreEqual(a.Position.Y, b.Position.Y, 1e-4f, $"틱 {t}: y");
                Assert.AreEqual(a.Health, b.Health, $"틱 {t}: 체력");
                Assert.AreEqual(a.Energy, b.Energy, $"틱 {t}: 에너지");
                for (int i = 0; i < ProjectileState.PerFighter; i++)
                {
                    var p = ProjectileState.At(w.State.Projectiles, i);
                    var q = ProjectileState.At(w.State.Projectiles, i + ProjectileState.PerFighter);
                    Assert.AreEqual(p.Active, q.Active, $"틱 {t}: 칸 {i}");
                    if (p.Active)
                        Assert.AreEqual(p.Position.X, -q.Position.X, 1e-4f, $"틱 {t}: 투사체 {i}");
                }
            }
        }
    }
}
