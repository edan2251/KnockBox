using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP15 구조물 물리 2단계(09-props-physics.md): 경첩·시소, 연결 구조, 구조물끼리 충돌, 끊어짐, 맵 6개 대칭.</summary>
    public class PropStage2Tests
    {
        const int Hinge = 0, Seesaw = 1, Hammer = 2, Target = 3, Dropper = 4;

        static SimConfig Config() =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, CombatMode.Charge, true);

        /// <summary>
        /// 경계 튕김 빈 맵(가운데 발판 윗면 y −6) + 경첩 발판(축 (−11.5, 2)) + 시소(축 (10, −3), 8 × 0.5)
        /// + 망치(줄 고정점 (0, 8), 1.2 × 1.2) + 과녁(망치 오른쪽, 0.8 × 0.8, 끊어지는 충격 breakImpulse) + 시소 위 낙하 블록.
        /// </summary>
        static ArenaDef Lab()
        {
            var props = new[]
            {
                new PropDef { Kind = PropKind.Hinged, Group = 0, Center = new Vec2(-10f, 2f), HalfSize = new Vec2(1.5f, 0.25f), HasAnchor = true, Anchor = new Vec2(-11.5f, 2f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Seesaw, Group = 1, Center = new Vec2(10f, -3f), HalfSize = new Vec2(4f, 0.25f), HasAnchor = true, Anchor = new Vec2(10f, -3f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Hanging, Group = 2, Center = new Vec2(0f, 3f), HalfSize = new Vec2(0.6f, 0.6f), HasAnchor = true, Anchor = new Vec2(0f, 8f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Hanging, Group = 3, Center = new Vec2(2f, 3f), HalfSize = new Vec2(0.4f, 0.4f), HasAnchor = true, Anchor = new Vec2(2f, 8f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Hanging, Group = 4, Center = new Vec2(10f, 1f), HalfSize = new Vec2(0.4f, 0.4f), HasAnchor = true, Anchor = new Vec2(10f, 6f), AnchorProp = -1 },
            };
            return BoundaryTests.Open().WithProps(props);
        }

        /// <summary>끊어짐 실험: 망치(번호 0)와 끊어지는 줄의 과녁(번호 1)만 있는 빈 맵. 과녁은 날아가 가운데 발판이나 경계 바닥에 떨어진다.</summary>
        static ArenaDef BreakLab(float breakImpulse)
        {
            var props = new[]
            {
                new PropDef { Kind = PropKind.Hanging, Group = 0, Center = new Vec2(0f, 3f), HalfSize = new Vec2(0.6f, 0.6f), HasAnchor = true, Anchor = new Vec2(0f, 8f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Hanging, Group = 1, Center = new Vec2(2f, 3f), HalfSize = new Vec2(0.4f, 0.4f), HasAnchor = true, Anchor = new Vec2(2f, 8f), AnchorProp = -1, BreakImpulse = breakImpulse },
            };
            return BoundaryTests.Open().WithProps(props);
        }

        /// <summary>
        /// 세 마디 사슬(고정점 (0, 8)) 끝에 가로 틀(6 × 0.7)이 달리고, 틀 아래에 블록 하나가 매달린 연결 구조. 줄 길이 0.95~1.1(맵의 연결 구조는 1.5 이상).
        /// 줄이 아주 짧으면(0.2) 위치 보정이 남기는 절대 오차(약 0.02) 때문에 비율 오차가 10%까지 커진다(S-133).
        /// </summary>
        static ArenaDef ChainLab()
        {
            var props = new[]
            {
                new PropDef { Kind = PropKind.Linked, Group = 0, Center = new Vec2(0f, 6.4f), HalfSize = new Vec2(0.25f, 0.5f), HasAnchor = true, Anchor = new Vec2(0f, 8f), AnchorProp = -1 },
                new PropDef { Kind = PropKind.Linked, Group = 0, Center = new Vec2(0f, 4.4f), HalfSize = new Vec2(0.25f, 0.5f), HasAnchor = true, AnchorProp = 0 },
                new PropDef { Kind = PropKind.Linked, Group = 0, Center = new Vec2(0f, 2.4f), HalfSize = new Vec2(0.25f, 0.5f), HasAnchor = true, AnchorProp = 1 },
                new PropDef { Kind = PropKind.Linked, Group = 0, Center = new Vec2(0f, 0.6f), HalfSize = new Vec2(3f, 0.35f), HasAnchor = true, AnchorProp = 2 },
                new PropDef { Kind = PropKind.Linked, Group = 0, Center = new Vec2(0f, -1.2f), HalfSize = new Vec2(0.5f, 0.5f), HasAnchor = true, AnchorProp = 3 },
            };
            return BoundaryTests.Open().WithProps(props);
        }

        static SimWorld World(ArenaDef arena) =>
            TestWorlds.CreateAt(new Vec2(-14f, -8f), new Vec2(14f, -8f), Config(), arena);

        static void Push(SimWorld w, int body, Vec2 velocity, float angular)
        {
            var s = w.State;
            ref var b = ref PropState.Get(ref s.Props, body);
            b.Velocity = velocity;
            b.AngularVelocity = angular;
            b.SleepTicks = 0;
            w.State = s;
        }

        static void Release(SimWorld w, int body, Vec2 velocity)
        {
            var s = w.State;
            ref var b = ref PropState.Get(ref s.Props, body);
            b.Broken = 1;
            b.Velocity = velocity;
            b.SleepTicks = 0;
            w.State = s;
        }

        static PropBody At(SimWorld w, int i) => PropState.At(w.State.Props, i);

        static Vec2 Half(ArenaDef arena, int body) => arena.GetProp(arena.DynamicProp(body)).HalfSize;

        static double KineticEnergy(SimWorld w, ArenaDef arena)
        {
            double e = 0;
            var c = w.Config.Combat.Props;
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                var b = At(w, i);
                var def = arena.GetProp(arena.DynamicProp(i));
                float m = PropPhysics.Mass(def, c);
                e += 0.5 * m * b.Velocity.LengthSquared + 0.5 * PropPhysics.Inertia(def, m) * b.AngularVelocity * b.AngularVelocity;
            }
            return e;
        }

        static float RopeError(SimWorld w, ArenaDef arena, int i)
        {
            var joint = arena.GetJoint(i);
            var b = At(w, i);
            if (joint.Kind != PropJointKind.Rope || b.Broken != 0)
                return 0f;
            var anchor = joint.OtherBody >= 0 ? At(w, joint.OtherBody).ToWorld(joint.OtherLocal) : joint.WorldAnchor;
            var d = b.ToWorld(joint.LocalAnchor) - anchor;
            float over = SimMath.Sqrt(d.LengthSquared) - joint.Length;
            return over > 0f ? over / joint.Length : 0f;
        }

        /// <summary>회전 직사각형 두 개의 겹침 깊이(분리축 네 개 중 가장 얕은 겹침, 떨어져 있으면 0). 시뮬레이션 코드와 따로 잰다.</summary>
        static float Overlap(in PropBody a, Vec2 ha, in PropBody b, Vec2 hb)
        {
            var axes = new[] { a.Rotation, new Vec2(-a.Rotation.Y, a.Rotation.X), b.Rotation, new Vec2(-b.Rotation.Y, b.Rotation.X) };
            float best = float.MaxValue;
            var d = b.Position - a.Position;
            foreach (var n in axes)
            {
                float ra = ha.X * System.Math.Abs(Vec2.Dot(n, a.Rotation)) + ha.Y * System.Math.Abs(Vec2.Dot(n, new Vec2(-a.Rotation.Y, a.Rotation.X)));
                float rb = hb.X * System.Math.Abs(Vec2.Dot(n, b.Rotation)) + hb.Y * System.Math.Abs(Vec2.Dot(n, new Vec2(-b.Rotation.Y, b.Rotation.X)));
                float o = ra + rb - System.Math.Abs(Vec2.Dot(n, d));
                if (o <= 0f)
                    return 0f;
                best = System.Math.Min(best, o);
            }
            return best;
        }

        static float Overlap(SimWorld w, ArenaDef arena, int i, int j) => Overlap(At(w, i), Half(arena, i), At(w, j), Half(arena, j));

        static int CountEvents(SimWorld w, SimEventType type)
        {
            int n = 0;
            for (int k = 0; k < w.Events.Count; k++)
            {
                if (w.Events[k].Type == type)
                    n++;
            }
            return n;
        }

        static void AssertFinite(in PropBody b, string what)
        {
            Assert.IsFalse(float.IsNaN(b.Position.X) || float.IsNaN(b.Position.Y) || float.IsInfinity(b.Position.X) || float.IsInfinity(b.Position.Y)
                           || float.IsNaN(b.AngularVelocity) || float.IsInfinity(b.AngularVelocity), what);
        }

        // CP15-1 ----------------------------------------------------------------------

        /// <summary>경첩 발판과 시소를 세게 돌려도 각도 한계를 넘지 않고, 3600틱 뒤 수평으로 돌아와 잠든다. 운동 에너지는 처음 값을 넘지 않는다.</summary>
        [Test]
        public void HingeAndSeesaw_RespectLimits_AndRestoreLevel()
        {
            var arena = Lab();
            var w = World(arena);
            var c = w.Config.Combat.Props;
            Push(w, Hinge, Vec2.Zero, -25f);
            Push(w, Seesaw, Vec2.Zero, 12f);
            double first = KineticEnergy(w, arena);
            float worstHinge = 0f, worstSeesaw = 0f, worstPivot = 0f;
            bool hitHingeLimit = false, hitSeesawLimit = false;
            for (int t = 0; t < 3600; t++)
            {
                w.Step(default, default);
                Assert.LessOrEqual(KineticEnergy(w, arena), first + 1e-3, $"틱 {t}: 운동 에너지가 처음 값을 넘음");
                var h = At(w, Hinge);
                var s = At(w, Seesaw);
                AssertFinite(h, "경첩");
                AssertFinite(s, "시소");
                worstHinge = System.Math.Max(worstHinge, System.Math.Abs(h.Rotation.Y));
                worstSeesaw = System.Math.Max(worstSeesaw, System.Math.Abs(s.Rotation.Y));
                hitHingeLimit |= System.Math.Abs(h.Rotation.Y) > c.HingeLimitSin - 0.02f;
                hitSeesawLimit |= System.Math.Abs(s.Rotation.Y) > c.SeesawLimitSin - 0.02f;
                var hp = h.ToWorld(arena.GetJoint(Hinge).LocalAnchor) - arena.GetJoint(Hinge).WorldAnchor;
                var sp = s.ToWorld(arena.GetJoint(Seesaw).LocalAnchor) - arena.GetJoint(Seesaw).WorldAnchor;
                worstPivot = System.Math.Max(worstPivot, System.Math.Max(SimMath.Sqrt(hp.LengthSquared), SimMath.Sqrt(sp.LengthSquared)));
            }
            var hEnd = At(w, Hinge);
            var sEnd = At(w, Seesaw);
            TestContext.WriteLine($"[Knockbox][CP15-1] 경첩 최대 sin {worstHinge:F3}(한계 {c.HingeLimitSin}), 시소 최대 sin {worstSeesaw:F3}(한계 {c.SeesawLimitSin}), "
                                  + $"축 최대 벗어남 {worstPivot:F4}, 끝 sin 경첩 {hEnd.Rotation.Y:F4} 시소 {sEnd.Rotation.Y:F4}");
            Assert.IsTrue(hitHingeLimit && hitSeesawLimit, "세게 돌리면 한계까지 간다");
            Assert.LessOrEqual(worstHinge, c.HingeLimitSin + 0.03f, "경첩 각도 한계");
            Assert.LessOrEqual(worstSeesaw, c.SeesawLimitSin + 0.03f, "시소 각도 한계");
            Assert.Less(worstPivot, 0.05f, "축에서 벗어나지 않음");
            Assert.Less(System.Math.Abs(hEnd.Rotation.Y), 0.01f, "경첩 발판이 수평으로 돌아옴");
            Assert.Less(System.Math.Abs(sEnd.Rotation.Y), 0.01f, "시소가 수평으로 돌아옴");
            Assert.IsTrue(PropPhysics.Asleep(hEnd, c) && PropPhysics.Asleep(sEnd, c), "멈춘 뒤 잠듦");
        }

        /// <summary>
        /// 연결 구조(사슬 + 틀 + 매달린 블록)의 틀과 블록을 가장 센 돌진의 충격(위력 34 × 배율)으로 서로 반대로 쳐도
        /// 3600틱 동안 에너지가 늘지 않고, 줄 길이 오차 2% 이하, 마지막 1초에 떨지 않는다.
        /// </summary>
        [Test]
        public void LinkedStructure_StaysStableFor3600Ticks()
        {
            var arena = ChainLab();
            var w = World(arena);
            var c = w.Config.Combat.Props;
            var config = w.Config.Combat;
            float power = config.Charge.LungeMaxSpeed * config.Charge.LungeAggression * c.LungeImpulseScale;
            Push(w, 3, new Vec2(power / PropPhysics.Mass(arena.GetProp(arena.DynamicProp(3)), c), 0f), 0f);
            Push(w, 4, new Vec2(-power / PropPhysics.Mass(arena.GetProp(arena.DynamicProp(4)), c), 0f), 0f);
            double first = KineticEnergy(w, arena);
            float worstRope = 0f, lastSpin = 0f, worstSpeed = 0f;
            for (int t = 0; t < 3600; t++)
            {
                w.Step(default, default);
                Assert.LessOrEqual(KineticEnergy(w, arena), first + 1e-3, $"틱 {t}: 운동 에너지가 처음 값을 넘음");
                for (int i = 0; i < w.State.Props.Count; i++)
                {
                    var b = At(w, i);
                    AssertFinite(b, $"틱 {t} 부품 {i}");
                    worstRope = System.Math.Max(worstRope, RopeError(w, arena, i));
                    worstSpeed = System.Math.Max(worstSpeed, SimMath.Sqrt(b.Velocity.LengthSquared));
                    if (t >= 3540)
                        lastSpin = System.Math.Max(lastSpin, System.Math.Abs(b.AngularVelocity));
                }
            }
            TestContext.WriteLine($"[Knockbox][CP15-1] 연결 구조(충격 {power:F1}): 처음 에너지 {first:F1}, 줄 길이 최대 오차 {worstRope:P2}, 최대 속도 {worstSpeed:F1}, 마지막 1초 최대 각속도 {lastSpin:F4}");
            Assert.LessOrEqual(worstRope, 0.02f, "줄 길이 오차 2% 이하");
            Assert.Less(lastSpin, c.SleepAngularSpeed, "마지막 1초에 떨지 않음");
        }

        /// <summary>맵 6개의 강체 구조물(1·2단계 모두)을 흔든 뒤 3600틱: 이상값 0, 줄 길이 오차 2% 이하, 마지막 1초에 떨지 않고 모두 잠듦.</summary>
        [TestCaseSource(typeof(MapV2Tests), nameof(MapV2Tests.NewMaps))]
        public void MapStage2Props_StayStableFor3600Ticks(string name)
        {
            var arena = ArenaFiles.LoadByName(name);
            var config = Config();
            var w = new SimWorld(config, arena, SimState.CreateRound(config, arena, 1, MatchPhase.Fighting));
            var c = config.Combat.Props;
            int kicked = 0, stage2 = 0;
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                if (PropDef.Stage(arena.GetProp(arena.DynamicProp(i)).Kind) == 2)
                    stage2++;
                Push(w, i, new Vec2(i % 2 == 0 ? 6f : -6f, 2f), i % 2 == 0 ? 3f : -3f);
                kicked++;
            }
            float worstRope = 0f, lastSpin = 0f;
            for (int t = 0; t < 3600; t++)
            {
                w.Step(default, default);
                for (int i = 0; i < w.State.Props.Count; i++)
                {
                    var b = At(w, i);
                    AssertFinite(b, $"{name} 틱 {t} 구조물 {i}");
                    worstRope = System.Math.Max(worstRope, RopeError(w, arena, i));
                    if (t >= 3540)
                        lastSpin = System.Math.Max(lastSpin, System.Math.Abs(b.AngularVelocity));
                }
            }
            int awake = 0;
            var awakeList = new System.Text.StringBuilder();
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                var b = At(w, i);
                if (PropPhysics.Asleep(b, c))
                    continue;
                awake++;
                awakeList.Append($" [{i} {arena.GetProp(arena.DynamicProp(i)).Kind}{(b.Broken != 0 ? " 끊어짐" : "")} p {b.Position} ω {b.AngularVelocity:F3} v {b.Velocity}]");
            }
            TestContext.WriteLine($"[Knockbox][CP15-1] {name}: 구조물 {kicked}개(2단계 {stage2}) 흔듦, 줄 길이 최대 오차 {worstRope:P2}, 마지막 1초 최대 각속도 {lastSpin:F4}, 끝에 깨어 있음 {awake}{awakeList}");
            Assert.Greater(kicked, 0, "맵마다 강체 구조물이 있음");
            Assert.LessOrEqual(worstRope, 0.02f, "줄 길이 오차 2% 이하");
            Assert.Less(lastSpin, c.SleepAngularSpeed, "마지막 1초에 떨지 않음");
            Assert.AreEqual(0, awake, "1분 뒤 모두 잠듦");
        }

        // CP15-2 ----------------------------------------------------------------------

        /// <summary>망치가 과녁을 여러 속도로 친다: 틱 끝 겹침 0.05 이하, 망치가 과녁을 지나쳐 나가지 않고(터널링 없음), 과녁이 밀려난다.</summary>
        [TestCase(5f)]
        [TestCase(20f)]
        [TestCase(45f)]
        [TestCase(80f)]
        public void HammerHitsTarget_NoDeepOverlap_NoTunneling(float speed)
        {
            var arena = Lab();
            var w = World(arena);
            Push(w, Hammer, new Vec2(speed, 0f), 0f);
            float worstOverlap = 0f, targetMoved = 0f;
            for (int t = 0; t < 240; t++)
            {
                w.Step(default, default);
                var h = At(w, Hammer);
                var g = At(w, Target);
                worstOverlap = System.Math.Max(worstOverlap, Overlap(w, arena, Hammer, Target));
                // 부딪히는 동안(처음 0.5초) 망치는 늘 과녁 왼쪽. 그 뒤에는 진자가 흔들려 순서가 바뀔 수 있다.
                if (t < 30)
                    Assert.Less(h.Position.X, g.Position.X, $"틱 {t}: 망치가 과녁을 지나침");
                targetMoved = System.Math.Max(targetMoved, g.Position.X - 2f);
            }
            TestContext.WriteLine($"[Knockbox][CP15-2] 망치 {speed}: 최대 겹침 {worstOverlap:F4}, 과녁 최대 밀림 {targetMoved:F2}");
            Assert.LessOrEqual(worstOverlap, 0.05f, "겹침 0.05 이하");
            Assert.Greater(targetMoved, 0.1f, "과녁이 밀려남");
        }

        /// <summary>시소 위에 옆으로 미끄러지며 떨어진 블록이 마찰로 멈춰 시소 위에 남고, 둘 다 잠든다. 겹침 0.05 이하.</summary>
        [Test]
        public void BlockLandingOnSeesaw_StopsByFriction()
        {
            var arena = Lab();
            var w = World(arena);
            var c = w.Config.Combat.Props;
            Release(w, Dropper, new Vec2(3f, 0f));
            float worstOverlap = 0f, slideAtSecond3 = 0f;
            for (int t = 0; t < 600; t++)
            {
                w.Step(default, default);
                worstOverlap = System.Math.Max(worstOverlap, Overlap(w, arena, Dropper, Seesaw));
                if (t == 180)
                {
                    var d = At(w, Dropper);
                    var s = At(w, Seesaw);
                    var rel = d.Velocity - s.PointVelocity(d.Position);
                    slideAtSecond3 = SimMath.Sqrt(rel.LengthSquared);
                }
            }
            var block = At(w, Dropper);
            var seesaw = At(w, Seesaw);
            var local = seesaw.ToLocal(block.Position);
            TestContext.WriteLine($"[Knockbox][CP15-2] 시소 위 블록: 최대 겹침 {worstOverlap:F4}, 3초 상대 속도 {slideAtSecond3:F4}, 시소 좌표 {local}, 시소 sin {seesaw.Rotation.Y:F3}, "
                                  + $"끝 블록 v {block.Velocity} ω {block.AngularVelocity:F4} 잠 {block.SleepTicks}, 시소 v {seesaw.Velocity} ω {seesaw.AngularVelocity:F4} 잠 {seesaw.SleepTicks}");
            Assert.LessOrEqual(worstOverlap, 0.05f, "겹침 0.05 이하");
            Assert.Less(slideAtSecond3, 0.1f, "마찰로 미끄러짐이 멈춤");
            Assert.Less(System.Math.Abs(local.X), Half(arena, Seesaw).X, "시소 위에 남음");
            Assert.Greater(local.Y, 0f, "시소 윗면 위");
            Assert.IsTrue(PropPhysics.Asleep(block, c) && PropPhysics.Asleep(seesaw, c), "둘 다 잠듦");
        }

        // CP15-3 ----------------------------------------------------------------------

        /// <summary>
        /// 같은 과녁을 여러 속도로 친다: 한 틱 충격 합이 기준 이상인 경우에만 끊어지고(이벤트 하나), 약하게 치면 남는다.
        /// 끊어진 과녁은 자유 낙하해 가운데 발판에 겹침 0.05 이하로 내려앉아 잠든다.
        /// </summary>
        [Test]
        public void Break_OnlyAtOrAboveThreshold_ThenFallsAndLands()
        {
            const float threshold = 6f;
            var c = Config().Combat.Props;
            bool sawIntact = false, sawBroken = false;
            const int hammer = 0, target = 1;
            foreach (float speed in new[] { 2f, 4f, 8f, 16f, 30f })
            {
                var arena = BreakLab(threshold);
                var w = World(arena);
                Push(w, hammer, new Vec2(speed, 0f), 0f);
                float peak = 0f;
                int breakTick = -1, breakEvents = 0;
                float worstOverlap = 0f;
                for (int t = 0; t < 600; t++)
                {
                    w.Step(default, default);
                    breakEvents += CountEvents(w, SimEventType.JointBreak);
                    if (breakTick < 0)
                    {
                        float impulse = w.PropScratch.TickImpulse[target];
                        peak = System.Math.Max(peak, impulse);
                        if (At(w, target).Broken != 0)
                        {
                            breakTick = t;
                            Assert.GreaterOrEqual(impulse, threshold, $"속도 {speed}: 기준 미만 충격 {impulse:F2}에 끊어짐");
                        }
                    }
                    else
                    {
                        worstOverlap = System.Math.Max(worstOverlap, Overlap(w, arena, hammer, target));
                    }
                }
                var g = At(w, target);
                TestContext.WriteLine($"[Knockbox][CP15-3] 과녁 기준 {threshold}, 망치 {speed}: 끊어지기 전 최대 충격 {peak:F2}, "
                                      + (breakTick >= 0 ? $"틱 {breakTick}에 끊어짐, 끝 위치 {g.Position}, {(PropPhysics.Asleep(g, c) ? "잠듦" : "움직임")}" : "남음"));
                if (breakTick < 0)
                {
                    sawIntact = true;
                    Assert.Less(peak, threshold, $"속도 {speed}: 기준 이상인데 안 끊어짐");
                    Assert.AreEqual(0, breakEvents);
                    Assert.LessOrEqual(RopeError(w, arena, target), 0.02f, "남은 줄 길이");
                    continue;
                }
                sawBroken = true;
                Assert.AreEqual(1, breakEvents, "끊어짐 이벤트 하나");
                Assert.LessOrEqual(worstOverlap, 0.05f, "끊어진 뒤 망치와 겹침 0.05 이하");
                // 가운데 발판(x −6~6, 윗면 −6) 위 또는 경계 바닥(−9) 위에 내려앉음.
                float floor = System.Math.Abs(g.Position.X) < 6f ? -6f : -9f;
                float bottom = g.Position.Y - Half(arena, target).Y * (System.Math.Abs(g.Rotation.X) + System.Math.Abs(g.Rotation.Y));
                Assert.AreEqual(floor, bottom, 0.05f, $"속도 {speed}: 바닥에 정상 충돌(파고들지 않고 떠 있지 않음)");
                Assert.IsTrue(PropPhysics.Asleep(g, c), "내려앉은 뒤 잠듦");
                Assert.LessOrEqual(RopeError(w, arena, hammer), 0.02f, "망치 줄은 그대로");
            }
            Assert.IsTrue(sawIntact && sawBroken, "약한 치기는 남고 센 치기는 끊어짐");
        }

        /// <summary>기준이 0인 줄은 아무리 세게 쳐도 끊어지지 않는다.</summary>
        [Test]
        public void UnbreakableRope_NeverBreaks()
        {
            var arena = Lab();
            var w = World(arena);
            Push(w, Hammer, new Vec2(80f, 0f), 0f);
            for (int t = 0; t < 300; t++)
            {
                w.Step(default, default);
                Assert.AreEqual(0, CountEvents(w, SimEventType.JointBreak));
            }
            Assert.AreEqual(0, At(w, Target).Broken);
        }

        // CP15-4 ----------------------------------------------------------------------

        static int MirrorOf(ArenaDef arena, int body, int count)
        {
            var p = arena.GetProp(arena.DynamicProp(body));
            for (int j = 0; j < count; j++)
            {
                var q = arena.GetProp(arena.DynamicProp(j));
                if (System.Math.Abs(q.Center.X + p.Center.X) < 1e-4f && System.Math.Abs(q.Center.Y - p.Center.Y) < 1e-4f
                    && q.HalfSize.Equals(p.HalfSize) && q.Kind == p.Kind && System.Math.Abs(q.BreakImpulse - p.BreakImpulse) < 1e-4f)
                    return j;
            }
            return -1;
        }

        /// <summary>같은 묶음에 가운데(자기 자신이 거울 짝) 강체가 있는지. 그런 연결 구조는 몸 번호 순서로 풀려 좌우가 조금 어긋난다.</summary>
        static bool TiedToCenter(ArenaDef arena, int[] mirror, int body)
        {
            int group = arena.GetProp(arena.DynamicProp(body)).Group;
            for (int k = 0; k < mirror.Length; k++)
            {
                if (mirror[k] == k && arena.GetProp(arena.DynamicProp(k)).Group == group)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 맵 6개: 강체마다 거울 짝이 있고(가운데 것은 자기 자신), 짝끼리 거울로 흔들면 3초 동안 거울처럼 움직인다.
        /// 가운데 구조물과 거기 이어진 부품은 건드리지 않는다(풀이 순서 때문에 한쪽으로 기울 수 있음).
        /// </summary>
        [TestCaseSource(typeof(MapV2Tests), nameof(MapV2Tests.NewMaps))]
        public void MirroredKicks_GiveMirroredProps(string name)
        {
            var arena = ArenaFiles.LoadByName(name);
            var config = Config();
            var w = new SimWorld(config, arena, SimState.CreateRound(config, arena, 1, MatchPhase.Countdown));
            int n = w.State.Props.Count;
            var mirror = new int[n];
            int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                mirror[i] = MirrorOf(arena, i, n);
                Assert.GreaterOrEqual(mirror[i], 0, $"{name} 구조물 {i}({arena.GetProp(arena.DynamicProp(i))})의 거울 짝이 없음");
            }
            for (int i = 0; i < n; i++)
            {
                int j = mirror[i];
                if (j <= i || TiedToCenter(arena, mirror, i))
                    continue;
                // 바깥쪽으로 찬다(CP16, S-143): 양쪽에서 가운데 구조물(자기 자신이 짝)에 동시에 부딪히면 번호 순서 풀이가 한쪽을 먼저 처리해 어긋난다.
                float outward = At(w, i).Position.X < 0f ? -1f : 1f;
                Push(w, i, new Vec2(5f * outward, 3f), 2f * outward);
                Push(w, j, new Vec2(-5f * outward, 3f), -2f * outward);
                pairs++;
            }
            float worst = 0f;
            for (int t = 0; t < 180; t++)
            {
                w.Step(default, default);
                for (int i = 0; i < n; i++)
                {
                    var a = At(w, i);
                    var b = At(w, mirror[i]);
                    worst = System.Math.Max(worst, System.Math.Abs(a.Position.X + b.Position.X));
                    worst = System.Math.Max(worst, System.Math.Abs(a.Position.Y - b.Position.Y));
                    worst = System.Math.Max(worst, PropCollapseTests.MirrorRotationError(a.Rotation, b.Rotation));
                    Assert.AreEqual(a.Broken, b.Broken, $"{name} 틱 {t} 구조물 {i}");
                }
            }
            TestContext.WriteLine($"[Knockbox][CP15-4] {name}: 강체 {n}개, 거울 짝 {pairs}쌍, 3초 최대 거울 오차 {worst:F5}");
            Assert.Less(worst, 1e-3f, $"{name} 거울 오차");
        }
            // CP15-6 ----------------------------------------------------------------------

        /// <summary>
        /// 캡처 시나리오 showcase_cp15_chain: 돌진이 연결 구조의 아래 블록을 쳐 줄이 끊어지고(이벤트 하나),
        /// 끊어진 블록은 시나리오 뒤 4초 안에 발판(윗면 −5) 또는 경계 바닥(−9)에 내려앉아 잠든다.
        /// </summary>
        [Test]
        public void ChainShowcase_BreaksOnStrike_ThenBlockLands()
        {
            var def = ScenarioJson.LoadByName("showcase_cp15_chain");
            var config = ScenarioConfig.Create(def);
            var arena = ArenaFiles.LoadByName(def.Arena);
            var p1 = ScenarioInputs.Build(def.P1, def.Ticks);
            var p2 = ScenarioInputs.Build(def.P2, def.Ticks);
            var w = new SimWorld(config, arena, ScenarioInputs.CreateInitialState(def, config, arena));
            int breakTick = -1, breaks = 0, broken = -1;
            for (int t = 0; t < def.Ticks; t++)
            {
                w.Step(p1[t], p2[t]);
                for (int k = 0; k < w.Events.Count; k++)
                {
                    if (w.Events[k].Type != SimEventType.JointBreak)
                        continue;
                    breaks++;
                    if (breakTick < 0)
                        breakTick = w.State.Tick;
                }
            }
            for (int t = 0; t < 240; t++)
                w.Step(default, default);
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                if (At(w, i).Broken != 0)
                    broken = i;
            }
            TestContext.WriteLine($"[Knockbox][CP15-6] {def.Name}: 끊어진 틱 {breakTick}(캡처 b_break 26), 끊어짐 {breaks}회, 끝 위치 {(broken >= 0 ? At(w, broken).Position.ToString() : "-")}");
            Assert.AreEqual(1, breaks, "끊어짐 한 번");
            Assert.That(breakTick, Is.InRange(20, 26), "캡처 a_strike(21)~b_break(26) 사이에 끊어짐");
            var g = At(w, broken);
            float bottom = g.Position.Y - Half(arena, broken).Y * System.Math.Abs(g.Rotation.X) - Half(arena, broken).X * System.Math.Abs(g.Rotation.Y);
            float floor = System.Math.Abs(g.Position.X) < 10f ? -5f : -9f;
            Assert.AreEqual(floor, bottom, 0.05f, "끊어진 블록이 발판이나 경계 바닥에 내려앉음");
            Assert.IsTrue(PropPhysics.Asleep(g, config.Combat.Props), "내려앉은 뒤 잠듦");
        }
    }
}
