using System.Collections.Generic;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP16 1부(관문): 쌓인 상자의 접촉 해법(09-props-physics.md 3·8절). 맵 배치·부분 붕괴 규칙·상자 피해보다 먼저 이 장면들로 확인한다.
    /// 장면은 경계 튕김 빈 맵(가운데 발판 윗면 y −6, x −6~6)과 테스트 안에서 만든 상자 기둥.
    /// </summary>
    public class PropStackTests
    {
        /// <summary>폭이 다른 상자를 섞은 6단(높이 10, 경기장 높이의 50%). 넓은 상자를 중간에 끼운 실루엣(09 4절 표).</summary>
        public static readonly Vec2[] Mixed6 =
        {
            new Vec2(2.0f, 2.0f), new Vec2(1.4f, 2.4f), new Vec2(3.2f, 1.0f), new Vec2(1.4f, 1.8f), new Vec2(2.0f, 1.2f), new Vec2(1.6f, 1.6f),
        };

        public static readonly Vec2[] Uniform6 =
        {
            new Vec2(1.6f, 1.6f), new Vec2(1.6f, 1.6f), new Vec2(1.6f, 1.6f), new Vec2(1.6f, 1.6f), new Vec2(1.6f, 1.6f), new Vec2(1.6f, 1.6f),
        };

        public static SimConfig Config(int sleepTicks = -1)
        {
            var combat = CombatParams.Default;
            if (sleepTicks > 0)
                combat.Props.SleepTicks = sleepTicks;
            return new SimConfig(MovementParams.Default, MatchParams.Default, combat, CombatMode.Charge, true);
        }

        /// <summary>받침 윗면 가운데(base)에서 위로 쌓은 기둥(맵 파일의 "pillar"와 같은 배치).</summary>
        public static void AddPillar(List<PropDef> props, Vec2 basePoint, Vec2[] sizes, int group)
        {
            float y = basePoint.Y;
            foreach (var size in sizes)
            {
                props.Add(new PropDef { Kind = PropKind.BoxPillar, Group = group, Center = new Vec2(basePoint.X, y + size.Y * 0.5f), HalfSize = size * 0.5f, AnchorProp = -1 });
                y += size.Y;
            }
        }

        public static ArenaDef PillarLab(Vec2[] sizes)
        {
            var props = new List<PropDef>();
            AddPillar(props, new Vec2(0f, -6f), sizes, 0);
            return BoundaryTests.Open().WithProps(props.ToArray(), 3);
        }

        /// <summary>
        /// 상자 40개: 바닥(경계 y −9)의 기둥 4개 × 6단(x ±8, ±12.5), 발판 위 기둥 1개 × 6단(x 0), 공중에서 떨어지는 흩어진 상자 10개(발판 위 둘씩 쌓임, 바닥 둘).
        /// </summary>
        public static ArenaDef FortyBoxLab()
        {
            var props = new List<PropDef>();
            var small = new Vec2[] { new Vec2(1.6f, 1.2f), new Vec2(1.2f, 1.6f), new Vec2(2.0f, 0.8f), new Vec2(1.2f, 1.2f), new Vec2(1.4f, 1.0f), new Vec2(1.0f, 1.0f) };
            AddPillar(props, new Vec2(-12.5f, -9f), small, 0);
            AddPillar(props, new Vec2(-8f, -9f), small, 1);
            AddPillar(props, new Vec2(0f, -6f), Mixed6, 2);
            AddPillar(props, new Vec2(8f, -9f), small, 3);
            AddPillar(props, new Vec2(12.5f, -9f), small, 4);
            var drops = new[] { new Vec2(-3.8f, 6f), new Vec2(-2.6f, 6f), new Vec2(2.6f, 6f), new Vec2(3.8f, 6f), new Vec2(-3.8f, 7.6f),
                new Vec2(-2.6f, 7.6f), new Vec2(2.6f, 7.6f), new Vec2(3.8f, 7.6f), new Vec2(-10.25f, 0f), new Vec2(10.25f, 0f) };
            for (int k = 0; k < drops.Length; k++)
                props.Add(new PropDef { Kind = PropKind.LooseBox, Group = 5 + k, Center = drops[k], HalfSize = new Vec2(0.5f, 0.5f), AnchorProp = -1 });
            return BoundaryTests.Open().WithProps(props.ToArray(), 3);
        }

        /// <summary>파이터는 가운데 발판 양 끝에 가만히 선다(경계 바닥에 서 있으면 경계 튕김 피해로 2000틱쯤 KO가 나 라운드가 다시 시작된다).</summary>
        public static SimWorld World(ArenaDef arena, SimConfig config) =>
            TestWorlds.CreateAt(new Vec2(-5.4f, -5.5f), new Vec2(5.4f, -5.5f), config, arena);

        public static void WakeAll(SimWorld w)
        {
            var s = w.State;
            for (int i = 0; i < s.Props.Count; i++)
                PropState.Get(ref s.Props, i).SleepTicks = 0;
            w.State = s;
        }

        public static void Push(SimWorld w, int body, Vec2 velocity, float angular)
        {
            var s = w.State;
            ref var b = ref PropState.Get(ref s.Props, body);
            b.Velocity = velocity;
            b.AngularVelocity = angular;
            b.SleepTicks = 0;
            w.State = s;
        }

        static float Energy(SimWorld w)
        {
            ref readonly var c = ref w.Config.Combat.Props;
            float e = 0f;
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                ref readonly var b = ref PropState.At(w.State.Props, i);
                ref readonly var def = ref w.Arena.GetProp(w.Arena.DynamicProp(i));
                float m = PropPhysics.Mass(def, c);
                e += 0.5f * m * b.Velocity.LengthSquared + 0.5f * PropPhysics.Inertia(def, m) * b.AngularVelocity * b.AngularVelocity + m * c.Gravity * b.Position.Y;
            }
            return e;
        }

        /// <summary>바로 위아래 상자의 세로 겹침(축 정렬에 가까운 기둥 전용).</summary>
        static float MaxStackOverlap(SimWorld w, int count)
        {
            float worst = 0f;
            for (int i = 0; i + 1 < count; i++)
            {
                ref readonly var lo = ref PropState.At(w.State.Props, i);
                ref readonly var hi = ref PropState.At(w.State.Props, i + 1);
                float top = lo.Position.Y + w.Arena.GetProp(w.Arena.DynamicProp(i)).HalfSize.Y;
                float bottom = hi.Position.Y - w.Arena.GetProp(w.Arena.DynamicProp(i + 1)).HalfSize.Y;
                worst = System.Math.Max(worst, top - bottom);
            }
            ref readonly var first = ref PropState.At(w.State.Props, 0);
            worst = System.Math.Max(worst, -6f - (first.Position.Y - w.Arena.GetProp(w.Arena.DynamicProp(0)).HalfSize.Y));
            return worst;
        }

        static void AssertFinite(SimWorld w)
        {
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                ref readonly var b = ref PropState.At(w.State.Props, i);
                Assert.IsFalse(float.IsNaN(b.Position.X + b.Position.Y + b.Velocity.X + b.Velocity.Y + b.AngularVelocity + b.Rotation.X + b.Rotation.Y)
                               || float.IsInfinity(b.Position.X + b.Position.Y + b.Velocity.X + b.Velocity.Y + b.AngularVelocity), $"상자 {i} 이상값");
            }
        }

        // CP16-1 ----------------------------------------------------------------------

        /// <summary>
        /// 6단 기둥(깨어 있는 채로 시작)이 3600틱 동안 서 있고 맨 위 상자의 가로 흔들림 0.02 이하, 겹침 0.05 이하, 에너지가 처음 값을 넘지 않는다.
        /// 잠들기를 끈 설정(sleepOff)은 해법만으로 버티는지 본다. 잠들기를 켠 설정은 1초 안에 모두 잠들고, 1800틱에 다시 깨워도 1초 안에 다시 잠든다.
        /// </summary>
        [TestCase("mixed", false)]
        [TestCase("mixed", true)]
        [TestCase("uniform", false)]
        [TestCase("uniform", true)]
        public void SixBoxPillar_StandsFor3600Ticks(string shape, bool sleepOff)
        {
            var sizes = shape == "mixed" ? Mixed6 : Uniform6;
            var config = Config(sleepOff ? 1_000_000 : -1);
            var w = World(PillarLab(sizes), config);
            WakeAll(w);
            var top = PropState.At(w.State.Props, 5).Position;
            float e0 = Energy(w);
            float sway = 0f, overlap = 0f, tilt = 0f, maxEnergyGain = float.MinValue, jitter = 0f;
            int asleepAt = -1, reAsleepAt = -1;
            var none = new InputFrame();
            for (int t = 1; t <= 3600; t++)
            {
                if (t == 1800 && !sleepOff)
                    WakeAll(w);
                w.Step(none, none);
                AssertFinite(w);
                ref readonly var b = ref PropState.At(w.State.Props, 5);
                sway = System.Math.Max(sway, System.Math.Abs(b.Position.X - top.X));
                overlap = System.Math.Max(overlap, MaxStackOverlap(w, 6));
                maxEnergyGain = System.Math.Max(maxEnergyGain, Energy(w) - e0);
                bool allAsleep = true;
                for (int i = 0; i < 6; i++)
                {
                    ref readonly var p = ref PropState.At(w.State.Props, i);
                    tilt = System.Math.Max(tilt, System.Math.Abs(p.Rotation.Y));
                    if (t > 60)
                        jitter = System.Math.Max(jitter, System.Math.Abs(p.AngularVelocity));
                    allAsleep &= PropPhysics.Asleep(p, config.Combat.Props);
                }
                if (allAsleep && asleepAt < 0)
                    asleepAt = t;
                if (allAsleep && t > 1800 && reAsleepAt < 0)
                    reAsleepAt = t - 1800;
            }
            float drop = top.Y - PropState.At(w.State.Props, 5).Position.Y;
            string report = $"[Knockbox][CP16-1] {shape} sleepOff={sleepOff}: 맨 위 가로 흔들림 {sway:F5}, 내려앉음 {drop:F4}, 겹침 최대 {overlap:F4}, 기울기 sin 최대 {tilt:F5}, " +
                            $"1초 뒤 각속도 최대 {jitter:F4}, 에너지 증가 최대 {maxEnergyGain:F4}(처음 {e0:F1}), 모두 잠든 틱 {asleepAt}, 다시 깨운 뒤 잠든 틱 {reAsleepAt}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.LessOrEqual(sway, 0.02f, report);
            Assert.LessOrEqual(overlap, 0.05f, report);
            Assert.LessOrEqual(maxEnergyGain, 1e-3f * System.Math.Abs(e0), report);
            Assert.LessOrEqual(jitter, 0.05f, report);
            if (!sleepOff)
            {
                Assert.That(asleepAt, Is.InRange(1, 60), report);
                Assert.That(reAsleepAt, Is.InRange(1, 60), report);
            }
        }

        // CP16-4 ----------------------------------------------------------------------

        /// <summary>붕괴 장면: 기둥(깨어 있는 채, 1초 쉰 뒤) 셋째 상자(넓은 상자)를 옆으로 세게 친다.</summary>
        static SimWorld CollapseScene(SimConfig config)
        {
            var w = World(PillarLab(Mixed6), config);
            WakeAll(w);
            var none = new InputFrame();
            for (int t = 0; t < 60; t++)
                w.Step(none, none);
            Push(w, 2, new Vec2(40f, 2f), -4f);
            return w;
        }

        /// <summary>상자 붕괴를 포함한 결정론(같은 입력 두 번), 저장·복원(붕괴 도중), SyncTest(8틱 되감기, 붕괴 전체).</summary>
        [Test]
        public void Collapse_IsDeterministic_SaveRestore_SyncTest()
        {
            var config = Config();
            var a = CollapseScene(config);
            var b = CollapseScene(config);
            TestWorlds.RandomInputs(23, 900, out var r1, out var r2);
            for (int t = 0; t < 900; t++)
            {
                a.Step(r1[t], r2[t]);
                b.Step(r1[t], r2[t]);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"틱 {t}: 같은 입력 두 번");
            }
            float topDrop = 4f - (PropState.At(a.State.Props, 5).Position.Y + 0.8f);
            Assert.Greater(topDrop, 2f, "맨 위 상자가 무너져 내려와야 함");

            // 저장·복원: 무너지는 도중(10틱 뒤)의 상태를 저장했다가 되돌려 같은 입력을 다시 준다.
            var c = CollapseScene(config);
            for (int t = 0; t < 10; t++)
                c.Step(r1[t], r2[t]);
            var saved = c.State;
            for (int t = 10; t < 400; t++)
                c.Step(r1[t], r2[t]);
            uint after = c.ComputeChecksum();
            c.State = saved;
            for (int t = 10; t < 400; t++)
                c.Step(r1[t], r2[t]);
            Assert.AreEqual(after, c.ComputeChecksum(), "저장·복원");

            var d = CollapseScene(config);
            var sync = new SyncTest<SimWorld>(d, 8);
            int awake = 0;
            for (int t = 0; t < 900; t++)
            {
                if (!sync.Advance(r1[t], r2[t]))
                    Assert.Fail(sync.LastMismatch.ToString());
                for (int i = 0; i < d.State.Props.Count; i++)
                {
                    if (!PropPhysics.Asleep(PropState.At(d.State.Props, i), config.Combat.Props))
                        awake++;
                }
            }
            TestContext.WriteLine($"[Knockbox][CP16-4] 붕괴: 결정론·저장복원·SyncTest(8틱 900틱) 통과, 맨 위 상자 내려온 높이 {topDrop:F2}, 깨어 있던 상자-틱 {awake}");
        }

        /// <summary>웜 스타트 충격량과 잠든 상태가 상태(체크섬·StateDiff)에 들어 있고, 실제로 다음 틱 결과를 바꾼다.</summary>
        [Test]
        public void WarmStartAndSleep_AreInSnapshot()
        {
            var config = Config(1_000_000);
            var w = World(PillarLab(Mixed6), config);
            WakeAll(w);
            var none = new InputFrame();
            for (int t = 0; t < 120; t++)
                w.Step(none, none);
            var settled = w.State;
            ref readonly var bottom = ref PropState.At(settled.Props, 0);
            Assert.AreNotEqual(0, PropWarm.At(bottom.Warm, 0).Id, "바닥 상자에 지난 틱 접촉 충격량이 저장돼 있어야 함");
            Assert.Greater(PropWarm.At(bottom.Warm, 0).Normal, 0f);

            // 체크섬과 StateDiff에 들어 있음.
            var changed = settled;
            PropWarm.Get(ref PropState.Get(ref changed.Props, 0).Warm, 0).Normal += 0.5f;
            Assert.AreNotEqual(settled.ComputeChecksum(), changed.ComputeChecksum(), "웜 스타트 충격량이 체크섬에 들어감");
            StringAssert.Contains("Warm", StateDiff.Describe(settled, changed));
            var slept = settled;
            PropState.Get(ref slept.Props, 3).SleepTicks = 999;
            Assert.AreNotEqual(settled.ComputeChecksum(), slept.ComputeChecksum(), "잠들기 틱이 체크섬에 들어감");
            StringAssert.Contains("SleepTicks", StateDiff.Describe(settled, slept));

            // 웜 스타트 정보를 지우고 다시 계산하면 결과가 달라진다(빠지면 되감은 결과가 처음과 달라짐).
            var cleared = settled;
            for (int i = 0; i < cleared.Props.Count; i++)
                PropState.Get(ref cleared.Props, i).Warm = default;
            w.State = settled;
            w.Step(none, none);
            uint withWarm = w.ComputeChecksum();
            w.State = cleared;
            w.Step(none, none);
            Assert.AreNotEqual(withWarm, w.ComputeChecksum(), "웜 스타트가 다음 틱 결과에 쓰임");
        }

        // CP16-6 (관문 부분) ----------------------------------------------------------

        /// <summary>
        /// 상자 40개: 흩어진 상자 10개가 떨어지는 동안 기둥 하나를 쳐서 무너뜨리고(동시에 깨어 있는 상자 16개 근처), 600틱에 다른 기둥을 무너뜨리는 1200틱(파이터는 가만히). Step 평균 0.25 ms 이하, 롤백 8틱(복원 + 다시 계산) 2 ms 이하,
        /// Step 할당 0. 동시에 깨어 있던 상자 수의 최대를 기록한다(09 8절은 16개 이하를 가정). 모두 깨운 최악 장면은 기록만.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FortyBoxes_StepAndRollback_WithinBudget(bool allAwake)
        {
            var config = Config();
            var w = World(FortyBoxLab(), config);
            Assert.AreEqual(40, w.State.Props.Count);
            for (int i = 30; i < 40; i++)
                Push(w, i, Vec2.Zero, 0f);
            if (allAwake)
                WakeAll(w);
            const int ticks = 1200;
            var in1 = new InputFrame[ticks];
            var in2 = new InputFrame[ticks];
            var history = new SimState[ticks];
            double stepMs = 0, worstRollback = 0;
            long allocated = 0;
            int maxAwake = 0;
            var watch = new System.Diagnostics.Stopwatch();
            // 처음 한 번은 JIT 때문에 느리다: 같은 장면으로 미리 한 번 돌린다.
            var warm = World(FortyBoxLab(), config);
            WakeAll(warm);
            for (int t = 0; t < 30; t++)
                warm.Step(in1[t], in2[t]);
            for (int t = 0; t < ticks; t++)
            {
                if (t == 20)
                    Push(w, 2, new Vec2(-40f, 2f), 4f);
                if (t == 600)
                    Push(w, 26, new Vec2(40f, 2f), -4f);
                if (allAwake && t % 120 == 0)
                    WakeAll(w);
                history[t] = w.State;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                w.Step(in1[t], in2[t]);
                watch.Stop();
                allocated += System.GC.GetAllocatedBytesForCurrentThread() - before;
                stepMs += watch.Elapsed.TotalMilliseconds;
                int awake = 0;
                for (int i = 0; i < w.State.Props.Count; i++)
                {
                    if (!PropPhysics.Asleep(PropState.At(w.State.Props, i), config.Combat.Props))
                        awake++;
                }
                maxAwake = System.Math.Max(maxAwake, awake);
                if (t >= 8 && t % 20 == 0 && !(t >= 20 && t < 28) && !(t >= 600 && t < 608) && !(allAwake && t % 120 < 8))
                {
                    // 같은 되감기를 세 번 재 가장 짧은 값을 쓴다(에디터의 순간적인 끊김이 아니라 계산 비용을 잰다).
                    var keep = w.State;
                    double best = double.MaxValue;
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        watch.Restart();
                        w.State = history[t - 7];
                        for (int k = t - 7; k <= t; k++)
                            w.Step(in1[k], in2[k]);
                        watch.Stop();
                        best = System.Math.Min(best, watch.Elapsed.TotalMilliseconds);
                    }
                    worstRollback = System.Math.Max(worstRollback, best);
                    Assert.AreEqual(keep.ComputeChecksum(), w.State.ComputeChecksum(), $"틱 {t}: 되감아 다시 계산한 결과");
                }
            }
            double average = stepMs / ticks;
            string report = $"[Knockbox][CP16-6] 상자 40개{(allAwake ? " 모두 깨움" : "")}: 동시에 깨어 있던 최대 {maxAwake}, Step 평균 {average:F4} ms, 롤백 8틱 최대 {worstRollback:F3} ms, Step 할당 {allocated} B";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, allocated, report);
            if (allAwake)
                return;
            Assert.LessOrEqual(average, 0.25, report);
            Assert.LessOrEqual(worstRollback, 2.0, report);
        }
    }
}
