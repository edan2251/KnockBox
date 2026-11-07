using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP16 2부: 부분 붕괴(CP16-2), 올라서도 무너지지 않음(CP16-3), 상자 장면의 대칭(CP16-5), 상자 피해 상한.
    /// 장면은 PropStackTests의 6단 혼합 기둥(가운데 발판 x 0, 처음엔 잠든 채 놓임 = 맵과 같음).
    /// 상자 번호 0~5(아래 → 위): 넷째 상자(번호 3, 1.4 × 1.8, 중심 y 0.3)가 "중간 상자".
    /// </summary>
    public class PropCollapseTests
    {
        const int Middle = 3;
        /// <summary>
        /// 깨어난 기둥은 부드러운 접촉 6층이 합쳐 0.07쯤 내려앉는다(관문 측정 0.07~0.09). "무너지지 않음"은 이보다 크게,
        /// 무너짐(맨 위가 1.5 이상 내려옴)보다 훨씬 작게 잡는다.
        /// </summary>
        const float SagTolerance = 0.12f;

        /// <summary>거울 짝의 회전 차이. 직사각형은 180° 돌려도 같은 모양이라 2배각(cos 2θ, sin 2θ)으로 비교한다(거울상은 sin 2θ의 부호가 바뀜).</summary>
        public static float MirrorRotationError(Vec2 a, Vec2 b)
        {
            float ca = a.X * a.X - a.Y * a.Y, sa = 2f * a.X * a.Y;
            float cb = b.X * b.X - b.Y * b.Y, sb = 2f * b.X * b.Y;
            return System.Math.Max(System.Math.Abs(ca - cb), System.Math.Abs(sa + sb));
        }

        static SimWorld Pillar(float lungeSpeed, int side, out Vec2[] start)
        {
            var config = PropStackTests.Config();
            var arena = PropStackTests.PillarLab(PropStackTests.Mixed6);
            // 왼쪽(side −1) 또는 오른쪽(+1)에서 중간 상자 높이로 날아오는 돌진. 상대는 반대편 바닥 쪽.
            var w = TestWorlds.CreateAt(new Vec2(side * 2.4f, 0.3f), new Vec2(-side * 15f, -8.5f), config, arena);
            start = new Vec2[w.State.Props.Count];
            for (int i = 0; i < start.Length; i++)
                start[i] = PropState.At(w.State.Props, i).Position;
            var s = w.State;
            s.Fighter0.Velocity = new Vec2(-side * lungeSpeed, 0f);
            s.Fighter0.SetAction(ActionState.Lunge);
            s.Fighter0.Grounded = false;
            w.State = s;
            return w;
        }

        static string Describe(SimWorld w, Vec2[] start)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < start.Length; i++)
            {
                ref readonly var b = ref PropState.At(w.State.Props, i);
                sb.Append($" [{i}] d=({b.Position.X - start[i].X:F3},{b.Position.Y - start[i].Y:F3}) sin {b.Rotation.Y:F3}");
            }
            return sb.ToString();
        }

        // CP16-2 ----------------------------------------------------------------------

        /// <summary>
        /// 기준 위력(24) 이상으로 중간 상자를 치면 그 상자가 빠지고 위 상자들이 무너지며, 아래 상자와 받침(발판)은 남는다.
        /// 약한 돌진(18, 가장 약한 돌진)은 무너뜨리지 못한다.
        /// </summary>
        [TestCase(34f, true)]
        [TestCase(26f, true)]
        [TestCase(22f, false)]
        [TestCase(18f, false)]
        public void MiddleBox_Strike_CollapsesOnlyAbove_WhenStrongEnough(float speed, bool collapse)
        {
            var w = Pillar(speed, -1, out var start);
            float power = 0f;
            bool knockout = false;
            for (int t = 0; t < 240; t++)
            {
                w.Step(default, default);
                for (int e = 0; e < w.Events.Count; e++)
                {
                    if (w.Events[e].Type == SimEventType.PropStrike)
                        power = System.Math.Max(power, w.Events[e].Strength);
                    knockout |= w.Events[e].Type == SimEventType.BoxKnockout;
                }
            }
            string report = $"[Knockbox][CP16-2] 돌진 {speed}: 위력 {power:F1}, 쳐냄 {knockout},{Describe(w, start)}";
            TestContext.WriteLine(report);
            Assert.Greater(power, 0f, "돌진이 상자를 쳐야 함");
            // 아래 상자 셋(받침 포함)은 거의 그대로.
            for (int i = 0; i < Middle; i++)
            {
                ref readonly var b = ref PropState.At(w.State.Props, i);
                Assert.Less(System.Math.Abs(b.Position.X - start[i].X), 0.1f, $"아래 상자 {i} 가로 " + report);
                Assert.Less(System.Math.Abs(b.Position.Y - start[i].Y), SagTolerance, $"아래 상자 {i} 세로 " + report);
                Assert.Less(System.Math.Abs(b.Rotation.Y), 0.05f, $"아래 상자 {i} 기울기 " + report);
            }
            ref readonly var middle = ref PropState.At(w.State.Props, Middle);
            ref readonly var top = ref PropState.At(w.State.Props, 5);
            if (collapse)
            {
                Assert.IsTrue(knockout, report);
                Assert.Greater(middle.Position.X - start[Middle].X, 1.5f, "중간 상자가 기둥에서 빠짐 " + report);
                Assert.Greater(start[5].Y - top.Position.Y, 1.5f, "맨 위 상자가 무너져 내려옴 " + report);
            }
            else
            {
                Assert.IsFalse(knockout, report);
                Assert.Less(System.Math.Abs(middle.Position.X - start[Middle].X), 0.3f, "중간 상자는 조금만 밀림 " + report);
                Assert.Less(System.Math.Abs(top.Position.Y - start[5].Y), SagTolerance, "맨 위 상자는 그대로(내려앉음만) " + report);
            }
        }

        // CP16-3 ----------------------------------------------------------------------

        /// <summary>캐릭터가 기둥 위로 떨어져 서고, 제자리 점프를 되풀이하고, 위에서 좌우로 조금씩 움직여도 기둥은 무너지지 않는다.</summary>
        [Test]
        public void FighterStandingAndJumpingOnTop_DoesNotCollapse()
        {
            var config = PropStackTests.Config();
            var arena = PropStackTests.PillarLab(PropStackTests.Mixed6);
            var w = TestWorlds.CreateAt(new Vec2(0f, 7f), new Vec2(15f, -8.5f), config, arena);
            var start = new Vec2[w.State.Props.Count];
            for (int i = 0; i < start.Length; i++)
                start[i] = PropState.At(w.State.Props, i).Position;
            int onTop = 0;
            for (int t = 0; t < 900; t++)
            {
                // 0~299: 가만히, 300~599: 40틱마다 점프, 600~899: 위 상자(폭 1.6) 안에서 좌우로.
                var input = default(InputFrame);
                if (t >= 300 && t < 600 && t % 40 < 3)
                    input = new InputFrame(0, 0, InputButtons.Jump);
                else if (t >= 600)
                {
                    // 위 상자 안에서 왔다 갔다(가운데에서 0.3 넘게 벗어나면 되돌아온다).
                    float x = w.State.Fighter0.Position.X;
                    int move = x > 0.3f ? -1 : x < -0.3f ? 1 : ((t / 10) & 1) == 0 ? 1 : -1;
                    input = new InputFrame(move, 0, InputButtons.None);
                }
                w.Step(input, default);
                if (w.State.Fighter0.OnProp == 6)
                    onTop++;
            }
            string report = $"[Knockbox][CP16-3] 맨 위 상자 위에 선 틱 {onTop}/900,{Describe(w, start)}";
            TestContext.WriteLine(report);
            Assert.Greater(onTop, 600, "맨 위 상자 위에 서 있어야 함 " + report);
            for (int i = 0; i < start.Length; i++)
            {
                ref readonly var b = ref PropState.At(w.State.Props, i);
                Assert.Less(System.Math.Abs(b.Position.X - start[i].X), 0.05f, $"상자 {i} 가로 " + report);
                Assert.Less(System.Math.Abs(b.Position.Y - start[i].Y), SagTolerance, $"상자 {i} 세로 " + report);
                Assert.Less(System.Math.Abs(b.Rotation.Y), 0.02f, $"상자 {i} 기울기 " + report);
            }
        }

        // CP16-5 ----------------------------------------------------------------------

        /// <summary>왼쪽과 오른쪽에서 같은 세기로 친 붕괴가 거울상으로 진행한다(상자 6개, 120틱).</summary>
        [Test]
        public void MirroredStrikes_GiveMirroredCollapse()
        {
            var a = Pillar(30f, -1, out _);
            var b = Pillar(30f, 1, out _);
            float worst = 0f;
            for (int t = 0; t < 120; t++)
            {
                a.Step(default, default);
                b.Step(default, default);
                for (int i = 0; i < a.State.Props.Count; i++)
                {
                    ref readonly var p = ref PropState.At(a.State.Props, i);
                    ref readonly var q = ref PropState.At(b.State.Props, i);
                    worst = System.Math.Max(worst, System.Math.Abs(p.Position.X + q.Position.X));
                    worst = System.Math.Max(worst, System.Math.Abs(p.Position.Y - q.Position.Y));
                    worst = System.Math.Max(worst, MirrorRotationError(p.Rotation, q.Rotation));
                }
            }
            TestContext.WriteLine($"[Knockbox][CP16-5] 거울 붕괴 120틱 최대 오차 {worst:F5}");
            Assert.Less(worst, 1e-3f);
        }

        // 상자 피해 상한 ------------------------------------------------------------------

        /// <summary>같은 캐릭터가 60틱 안에 여러 상자에 맞아도 상자 피해 합은 18을 넘지 않고, 창이 지나면 다시 맞는다.</summary>
        [Test]
        public void BoxDamage_IsCappedPerWindow()
        {
            var config = PropStackTests.Config();
            var props = new System.Collections.Generic.List<PropDef>();
            for (int k = 0; k < 4; k++)
                props.Add(new PropDef { Kind = PropKind.LooseBox, Group = k, Center = new Vec2(-6f - 2.5f * k, 2f), HalfSize = new Vec2(1f, 1f), AnchorProp = -1 });
            var arena = BoundaryTests.Open().WithProps(props.ToArray());
            var w = TestWorlds.CreateAt(new Vec2(0f, -5.5f), new Vec2(15f, -8.5f), config, arena);
            int total = 0, hits = 0;
            for (int round = 0; round < 2; round++)
            {
                // 무거운 상자 넷을 차례로(4틱 간격) 캐릭터에게 날린다.
                for (int t = 0; t < 70; t++)
                {
                    if (t % 4 == 0 && t / 4 < 4)
                    {
                        var s = w.State;
                        ref var box = ref PropState.Get(ref s.Props, t / 4);
                        box.Position = new Vec2(w.State.Fighter0.Position.X - 1.6f, w.State.Fighter0.Position.Y);
                        box.Velocity = new Vec2(25f, 0f);
                        box.AngularVelocity = 0f;
                        box.SleepTicks = 0;
                        s.Fighter0.Health = 100;
                        w.State = s;
                    }
                    w.Step(default, default);
                    for (int e = 0; e < w.Events.Count; e++)
                    {
                        if (w.Events[e].Type == SimEventType.BoxHit && w.Events[e].Fighter == 0)
                        {
                            total += (int)w.Events[e].Strength;
                            hits++;
                        }
                    }
                }
                TestContext.WriteLine($"[Knockbox][CP16] 상자 피해 상한: {round + 1}번째 창까지 맞음 {hits}회, 합 {total}");
                Assert.LessOrEqual(total, config.Combat.Props.BoxDamageCap * (round + 1));
                Assert.Greater(total, config.Combat.Props.BoxDamageCap * round, "창이 지나면 다시 맞음");
            }
        }

        // CP16-7 ----------------------------------------------------------------------

        /// <summary>
        /// 캡처 시나리오 showcase_cp16_collapse(PropLab, 기둥 몸 1~6): 공중 돌진이 넷째 상자(몸 4)만 쳐내고(24틱 근처, 쳐냄 한 번),
        /// 위 두 상자는 그 높이(1.6)만큼 내려앉고, 아래 셋은 그대로 남는다. 그 뒤 4초 안에 기둥 상자는 모두 잠든다.
        /// </summary>
        [Test]
        public void CollapseShowcase_KnocksOutOneBox()
        {
            var def = ScenarioJson.LoadByName("showcase_cp16_collapse");
            var config = ScenarioConfig.Create(def);
            var arena = ArenaFiles.LoadByName(def.Arena);
            var p1 = ScenarioInputs.Build(def.P1, def.Ticks);
            var p2 = ScenarioInputs.Build(def.P2, def.Ticks);
            var w = new SimWorld(config, arena, ScenarioInputs.CreateInitialState(def, config, arena));
            var start = new Vec2[w.State.Props.Count];
            for (int i = 0; i < start.Length; i++)
                start[i] = PropState.At(w.State.Props, i).Position;
            int knockouts = 0, knockoutTick = -1;
            float power = 0f;
            for (int t = 0; t < def.Ticks; t++)
            {
                w.Step(p1[t], p2[t]);
                for (int k = 0; k < w.Events.Count; k++)
                {
                    if (w.Events[k].Type != SimEventType.BoxKnockout)
                        continue;
                    knockouts++;
                    knockoutTick = w.State.Tick;
                    power = w.Events[k].Strength;
                }
            }
            for (int t = 0; t < 240; t++)
                w.Step(default, default);
            string report = $"[Knockbox][CP16-7] {def.Name}: 쳐냄 {knockouts}회(틱 {knockoutTick}, 위력 {power:F1}),{Describe(w, start)}";
            TestContext.WriteLine(report);
            Assert.AreEqual(1, knockouts, report);
            Assert.That(knockoutTick, Is.InRange(22, 26), "캡처 a_approach(22)~c_out(26) 사이 " + report);
            for (int i = 1; i <= 3; i++)
            {
                Assert.Less(System.Math.Abs(PropState.At(w.State.Props, i).Position.X - start[i].X), 0.1f, $"아래 상자 몸 {i} " + report);
                Assert.Less(System.Math.Abs(PropState.At(w.State.Props, i).Position.Y - start[i].Y), SagTolerance, $"아래 상자 몸 {i} " + report);
            }
            Assert.Greater(System.Math.Abs(PropState.At(w.State.Props, 4).Position.X - start[4].X), 1.5f, "넷째 상자가 빠짐 " + report);
            for (int i = 5; i <= 6; i++)
                Assert.AreEqual(start[i].Y - 1.6f, PropState.At(w.State.Props, i).Position.Y, 0.3f, $"위 상자 몸 {i}가 빠진 상자 높이만큼 내려옴 " + report);
            for (int i = 1; i <= 6; i++)
                Assert.IsTrue(PropPhysics.Asleep(PropState.At(w.State.Props, i), config.Combat.Props), $"몸 {i} 잠듦 " + report);
        }
    }
}
