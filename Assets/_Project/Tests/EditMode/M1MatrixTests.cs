using System.IO;
using System.Text;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP20 측정(평소 묶음에서는 돌지 않는다. 이름으로 실행, D-048).
    /// CP20-6 봇 매트릭스: 에너지 켬·끔 × 폼 쌍 3×3 × 맵 6 × 시드 6 × 2분. 불변식 위반 0, 라운드가 끝나지 않는 경기 0이 합격 조건이고
    /// 라운드 길이·폼 쌍별 승률·지형 축 데미지 비중은 기록만 한다. 결과는 Logs/m1-matrix.csv와 Logs/m1-summary.txt.
    /// 성능(CP18-8, CP19-7, CP20): Step·롤백 8틱·GC를 Logs/m1-perf.txt에 기록만 한다.
    /// </summary>
    [Explicit("측정: 이름으로 실행")]
    [Category("Measure")]
    [Category(TestCategories.LegacyCombat)]
    public class M1MatrixTests
    {
        const int MatchTicks = 7200;
        static readonly FighterForm[] FormsList = { FighterForm.Scissors, FighterForm.Rock, FighterForm.Paper };

        static SimConfig Config(FighterForm p1, FighterForm p2, bool energy) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default,
                new RuleSet { Switches = new FeatureSwitches { EnergyEnabled = energy } }.WithForms(p1, p2));

        struct Totals
        {
            public int Matches, Rounds, StuckMatches, Violations, FightingTicks, WallSplats, Wins0, Wins1, Draws;
            public double Damage, Boundary, Prop, Box, Spike, Terrain;
            public string FirstViolation;

            public void Add(string what)
            {
                if (Violations++ == 0)
                    FirstViolation = what;
            }
        }

        static void RunMatch(FighterForm f0, FighterForm f1, bool energy, string map, uint seed, ref Totals v)
        {
            var config = Config(f0, f1, energy);
            var arena = ArenaFiles.LoadByName(map);
            var world = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            var p1 = new BotInputSource(() => world, 0, 300u + seed * 17u);
            var p2 = new BotInputSource(() => world, 1, 700u + seed * 31u);
            string where = $"{f0}/{f1} e{(energy ? 1 : 0)} {map} s{seed}";
            int overlapRun = 0, rounds = 0;
            v.Matches++;
            for (int t = 0; t < MatchTicks; t++)
            {
                int h0 = world.State.Fighter0.Health, h1 = world.State.Fighter1.Health;
                int tick = world.State.Tick;
                world.Step(p1.Poll(tick), p2.Poll(tick));
                if (world.Events.Dropped > 0)
                    v.Add($"{where} t{t}: 이벤트 버림");
                var bounded = new float[2];
                var propped = new float[2];
                var boxed = new float[2];
                var spiked = new float[2];
                var known = new float[2];
                for (int i = 0; i < world.Events.Count; i++)
                {
                    var e = world.Events[i];
                    switch (e.Type)
                    {
                        case SimEventType.OutOfBounds: v.Add($"{where} t{t}: 경기장 이탈"); break;
                        case SimEventType.WallSplat: v.WallSplats++; break;
                        case SimEventType.BoundaryHit: bounded[e.Fighter] += e.Strength; break;
                        case SimEventType.PropHit: propped[e.Fighter] += e.Strength; break;
                        case SimEventType.BoxHit: boxed[e.Fighter] += e.Strength; break;
                        case SimEventType.Spike: spiked[e.Fighter] += e.Strength; break;
                        case SimEventType.Hit:
                        case SimEventType.GuardHit:
                        case SimEventType.Throw:
                            known[e.Fighter] += e.Strength;
                            break;
                        case SimEventType.RoundEnd:
                            rounds++;
                            if (e.Fighter == 0) v.Wins0++;
                            else if (e.Fighter == 1) v.Wins1++;
                            else v.Draws++;
                            break;
                    }
                }
                int[] drops = { h0 - world.State.Fighter0.Health, h1 - world.State.Fighter1.Health };
                for (int f = 0; f < 2; f++)
                {
                    if (drops[f] <= 0)
                        continue;
                    double rest = drops[f];
                    v.Damage += rest;
                    double b = System.Math.Min(rest, bounded[f]); v.Boundary += b; rest -= b;
                    double s = System.Math.Min(rest, spiked[f]); v.Spike += s; rest -= s;
                    double pr = System.Math.Min(rest, propped[f]); v.Prop += pr; rest -= pr;
                    double bx = System.Math.Min(rest, boxed[f]); v.Box += bx; rest -= bx;
                    rest -= System.Math.Min(rest, known[f]);
                    v.Terrain += rest;
                }
                ref readonly var st = ref world.State;
                if (st.Match.Phase != MatchPhase.Fighting)
                {
                    overlapRun = 0;
                    continue;
                }
                v.FightingTicks++;
                Check(st.Fighter0, 0, config, arena, where, t, ref v);
                Check(st.Fighter1, 1, config, arena, where, t, ref v);
                for (int i = 0; i < ProjectileState.Capacity; i++)
                {
                    var p = ProjectileState.At(st.Projectiles, i);
                    if (p.Active && (float.IsNaN(p.Position.X) || float.IsNaN(p.Position.Y)
                                     || SimMath.Abs(p.Position.X) > arena.HalfWidth + 1f || SimMath.Abs(p.Position.Y) > arena.HalfHeight + 1f))
                        v.Add($"{where} t{t}: 투사체 {i} 이상 위치 {p.Position}");
                }
                float dx = SimMath.Abs(st.Fighter0.Position.X - st.Fighter1.Position.X);
                float dy = SimMath.Abs(st.Fighter0.Position.Y - st.Fighter1.Position.Y);
                overlapRun = dx < 0.6f && dy < 0.6f ? overlapRun + 1 : 0;
                if (overlapRun == 60)
                    v.Add($"{where} t{t}: 두 몸이 1초 넘게 겹침");
            }
            v.Rounds += rounds;
            // 2분 동안 라운드가 하나도 끝나지 않은 경기는 기록한다. 합격 판정은 조합 단위(CP20-6 "라운드가 끝나지 않는 조합 없음").
            if (rounds == 0)
                v.StuckMatches++;
        }

        static void Check(in FighterState f, int index, SimConfig config, ArenaDef arena, string where, int t, ref Totals v)
        {
            if (float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y) || float.IsInfinity(f.Velocity.X) || float.IsInfinity(f.Velocity.Y)
                || float.IsNaN(f.Velocity.X) || float.IsNaN(f.Velocity.Y))
                v.Add($"{where} t{t}: P{index + 1} NaN");
            if (TerrainCollision.OverlapsSolid(f.Position, config.HalfBody - 0.03f, arena))
                v.Add($"{where} t{t}: P{index + 1} 지형 속 {f.Position} {f.Action}");
            bool free = f.Action <= ActionState.Dash || f.Action == ActionState.Guard || f.Action == ActionState.KO;
            if (!free && f.ActionTick == 300)
                v.Add($"{where} t{t}: P{index + 1} {f.Action}에 300틱 갇힘");
            if (f.HitstopTicks > 24)
                v.Add($"{where} t{t}: P{index + 1} 히트스탑 {f.HitstopTicks}");
            if (f.Energy < 0 || f.Energy > config.Combat.Energy.Max)
                v.Add($"{where} t{t}: P{index + 1} 에너지 {f.Energy}");
        }

        [Test]
        public void BotMatrix_NoInvariantViolations_EveryRoundEnds()
        {
            var csv = new StringBuilder("energy,p1,p2,map,matches,rounds,avg_round_s,wins_p1,wins_p2,draws,stuck,violations,wallsplat_per_min,boundary,prop,box,spike,terrain,first_violation\n");
            var summary = new StringBuilder();
            int violations = 0, stuck = 0, stuckCombos = 0;
            string first = null;
            var byEnergy = new Totals[2];
            var byMapOff = new Totals[MapV2Tests.NewMaps.Length];
            foreach (bool energy in new[] { true, false })
            {
                foreach (var f0 in FormsList)
                {
                    foreach (var f1 in FormsList)
                    {
                        for (int m = 0; m < MapV2Tests.NewMaps.Length; m++)
                        {
                            string map = MapV2Tests.NewMaps[m];
                            var v = new Totals();
                            for (uint seed = 1; seed <= 6; seed++)
                                RunMatch(f0, f1, energy, map, seed, ref v);
                            violations += v.Violations;
                            stuck += v.StuckMatches;
                            if (v.Rounds == 0)
                                stuckCombos++;
                            first ??= v.FirstViolation;
                            Merge(ref byEnergy[energy ? 0 : 1], v);
                            if (energy)
                                Merge(ref byMapOff[m], v);
                            float avg = v.Rounds > 0 ? v.FightingTicks / 60f / v.Rounds : float.NaN;
                            csv.Append($"{(energy ? 1 : 0)},{f0},{f1},{map},{v.Matches},{v.Rounds},{avg:F1},{v.Wins0},{v.Wins1},{v.Draws},{v.StuckMatches},{v.Violations},"
                                       + $"{v.WallSplats / (v.FightingTicks / 3600f):F1},{Share(v.Boundary, v.Damage)},{Share(v.Prop, v.Damage)},{Share(v.Box, v.Damage)},{Share(v.Spike, v.Damage)},{Share(v.Terrain, v.Damage)},\"{v.FirstViolation}\"\n");
                        }
                    }
                }
            }
            summary.AppendLine($"M1 봇 매트릭스: 경기 {byEnergy[0].Matches + byEnergy[1].Matches}, 불변식 위반 {violations}, 라운드가 끝나지 않은 조합 {stuckCombos}, 2분 동안 라운드가 끝나지 않은 경기 {stuck}");
            if (first != null)
                summary.AppendLine($"첫 위반: {first}");
            for (int e = 0; e < 2; e++)
            {
                var v = byEnergy[e];
                summary.AppendLine($"에너지 {(e == 0 ? "켬" : "끔")}: 라운드 {v.Rounds}, 평균 {v.FightingTicks / 60f / System.Math.Max(1, v.Rounds):F1}초, 벽꽝/분 {v.WallSplats / (v.FightingTicks / 3600f):F1}, "
                                   + $"경계 {Share(v.Boundary, v.Damage)}, 구조물 {Share(v.Prop, v.Damage)}, 상자 {Share(v.Box, v.Damage)}, 가시 {Share(v.Spike, v.Damage)}, 벽꽝 등 지형 {Share(v.Terrain, v.Damage)}");
            }
            summary.AppendLine("CP20-7 맵별(에너지 켬, 가드·잡기 끔, 폼 쌍 9개 합):");
            for (int m = 0; m < byMapOff.Length; m++)
            {
                var v = byMapOff[m];
                summary.AppendLine($"  {MapV2Tests.NewMaps[m]}: 라운드 평균 {v.FightingTicks / 60f / System.Math.Max(1, v.Rounds):F1}초, 벽꽝/분 {v.WallSplats / (v.FightingTicks / 3600f):F1}, "
                                   + $"경계 {Share(v.Boundary, v.Damage)}, 구조물 {Share(v.Prop, v.Damage)}, 상자 {Share(v.Box, v.Damage)}, 가시 {Share(v.Spike, v.Damage)}, 벽꽝 등 지형 {Share(v.Terrain, v.Damage)}");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/m1-matrix.csv", csv.ToString());
            File.WriteAllText("Logs/m1-summary.txt", summary.ToString());
            TestContext.WriteLine(summary.ToString());
            Assert.AreEqual(0, violations, first);
            Assert.AreEqual(0, stuckCombos, "라운드가 끝나지 않는 조합");
        }

        static string Share(double part, double total) => total > 0 ? $"{part / total:P0}".Replace(" ", "") : "0%";

        static void Merge(ref Totals into, in Totals v)
        {
            into.Matches += v.Matches;
            into.Rounds += v.Rounds;
            into.FightingTicks += v.FightingTicks;
            into.WallSplats += v.WallSplats;
            into.Damage += v.Damage;
            into.Boundary += v.Boundary;
            into.Prop += v.Prop;
            into.Box += v.Box;
            into.Spike += v.Spike;
            into.Terrain += v.Terrain;
        }

        /// <summary>CP18-8·CP19-7·CP20 성능 기록(합격 기준 없음, D-048): 세 폼이 섞인 봇 경기와 투사체 풀이 가득 찬 장면.</summary>
        [Test]
        public void Performance_Record()
        {
            var report = new StringBuilder();
            foreach (var (name, f0, f1, map) in new[]
                     {
                         ("Rock/Paper Totems", FighterForm.Rock, FighterForm.Paper, "Totems"),
                         ("Scissors/Rock Ruins", FighterForm.Scissors, FighterForm.Rock, "Ruins"),
                         ("Paper/Scissors Chimes", FighterForm.Paper, FighterForm.Scissors, "Chimes"),
                     })
                report.AppendLine(Measure(name, Config(f0, f1, true), ArenaFiles.LoadByName(map), false));
            // 투사체 풀 가득: 에너지를 끄고 느리고 오래 사는 투사체를 계속 쏜다.
            var combat = CombatParams.Default;
            combat.Rock.Speed = 0.5f;
            combat.Rock.LifetimeTicks = 100000;
            var full = new SimConfig(MovementParams.Default, MatchParams.Default, combat,
                new RuleSet { Switches = new FeatureSwitches() }.WithForms(FighterForm.Rock, FighterForm.Rock));
            // 지형 없는 경계 경기장에서 위로 천천히: 파이터마다 6칸이 차고 그 뒤로는 가장 오래된 것을 바꿔 쏜다.
            report.AppendLine(Measure("투사체 풀 가득(Rock/Rock, 빈 경계 경기장)", full, BoundaryTests.Open(), true));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/m1-perf.txt", report.ToString());
            TestContext.WriteLine(report.ToString());
        }

        static string Measure(string name, SimConfig config, ArenaDef arena, bool spam)
        {
            const int ticks = 3600;
            var w = TestWorlds.Create(MatchPhase.Fighting, 5, config, arena);
            var bot1 = new BotInputSource(() => w, 0, 77u);
            var bot2 = new BotInputSource(() => w, 1, 99u);
            var in1 = new InputFrame[ticks];
            var in2 = new InputFrame[ticks];
            var history = new SimState[ticks];
            double stepMs = 0, worst = 0;
            long allocated = 0;
            int fullTicks = 0;
            var watch = new System.Diagnostics.Stopwatch();
            for (int t = 0; t < ticks; t++)
            {
                int tick = w.State.Tick;
                in1[t] = spam ? new InputFrame(0, 1, t % 30 == 0 ? InputButtons.Attack : InputButtons.None) : bot1.Poll(tick);
                in2[t] = spam ? new InputFrame(0, 1, t % 30 == 0 ? InputButtons.Attack : InputButtons.None) : bot2.Poll(tick);
                history[t] = w.State;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                w.Step(in1[t], in2[t]);
                watch.Stop();
                allocated += System.GC.GetAllocatedBytesForCurrentThread() - before;
                stepMs += watch.Elapsed.TotalMilliseconds;
                if (w.State.Projectiles.CountActive(-1) == ProjectileState.Capacity)
                    fullTicks++;
                if (t >= 8 && t % 60 == 0)
                {
                    var keep = w.State;
                    double best = double.MaxValue;
                    for (int r = 0; r < 3; r++)
                    {
                        watch.Restart();
                        w.State = history[t - 7];
                        for (int k = t - 7; k <= t; k++)
                            w.Step(in1[k], in2[k]);
                        watch.Stop();
                        best = System.Math.Min(best, watch.Elapsed.TotalMilliseconds);
                    }
                    worst = System.Math.Max(worst, best);
                    Assert.AreEqual(keep.ComputeChecksum(), w.State.ComputeChecksum(), $"{name} 틱 {t}: 되감기");
                }
            }
            return $"{name}: Step 평균 {stepMs / ticks:F4} ms, 롤백 8틱 최대 {worst:F3} ms, Step 할당 {allocated} B, 풀이 가득 찬 틱 {fullTicks}/{ticks}";
        }
    }
}
