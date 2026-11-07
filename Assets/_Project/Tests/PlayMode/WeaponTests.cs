using System.Collections;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CP6-7(수치): 모드 B 세 기술의 활성 틱마다, 그린 무기 끝이 시뮬레이션의 마지막 판정 원 중심과 일치한다.
    /// 일시정지 상태(보간 비율 1)에서 한 틱씩 진행하며 연출을 그린 직후 비교한다.
    /// </summary>
    public class WeaponTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator WeaponTip_MatchesOuterHitCircle_EveryActiveTick()
        {
            root = new GameObject("Match");
            var runner = root.AddComponent<MatchRunner>();
            var presenter = root.AddComponent<MatchPresenter>();
            yield return null;

            var cases = new[]
            {
                (name: "forward", y: -7.55f, input: new InputFrame(0, 0, InputButtons.Attack), id: AttackIds.SwingForward),
                (name: "up", y: -7.55f, input: new InputFrame(0, 1, InputButtons.Attack), id: AttackIds.SwingUp),
                (name: "down", y: 3f, input: new InputFrame(0, -1, InputButtons.Attack), id: AttackIds.SwingDown),
            };
            float worst = 0f;
            int compared = 0;
            foreach (var c in cases)
            {
                var config = new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, CombatMode.Swing);
                var arena = runner.ResolveArena("Box");
                runner.Begin(config, arena, SimState.CreateRound(config, arena, 1, new Vec2(-6f, c.y), new Vec2(10f, -7.55f), MatchPhase.Fighting));
                runner.Running = false;
                runner.Player1Input = new FrameArrayInputSource(new[] { c.input });
                runner.Player2Input = new IdleInputSource();
                runner.StepOnce();
                Assert.AreEqual(c.id, runner.World.State.Fighter0.AttackId, c.name);
                var attack = runner.World.Config.GetAttack(c.id);
                for (int t = 0; t < attack.TotalTicks; t++)
                {
                    presenter.RenderFrame();
                    var f = runner.World.State.Fighter0;
                    if (FighterCombat.IsHitboxActive(f, runner.World.Config))
                    {
                        var center = FighterCombat.CircleCenter(f, attack, attack.Circle2);
                        var tip = presenter.Fighter(0).WeaponTip;
                        float error = Vector2.Distance(tip, new Vector2(center.X, center.Y));
                        worst = Mathf.Max(worst, error);
                        compared++;
                        Assert.Less(error, 0.01f, $"{c.name} 활성 {f.ActionTick - attack.StartupTicks}틱: 무기 끝 {tip} / 판정 원 {center}");
                    }
                    runner.StepOnce();
                    yield return null;
                }
            }
            string report = $"[Knockbox][CP6-7] 세 기술 활성 {compared}틱: 무기 끝과 마지막 판정 원 중심의 최대 거리 {worst:F4}";
            Debug.Log(report);
            TestContext.WriteLine(report);
            Assert.AreEqual(13, compared, "활성 틱 4 + 4 + 5");
        }
    }
}
