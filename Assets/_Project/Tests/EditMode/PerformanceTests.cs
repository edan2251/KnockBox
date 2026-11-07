using System.Diagnostics;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP1-8: Step 평균 시간과 롤백 8틱 재시뮬레이션 시간(01-architecture.md 7절 예산).</summary>
    public class PerformanceTests
    {
        const double StepBudgetMs = 0.25;
        const double Rollback8BudgetMs = 2.0;

        [Test]
        public void StepAndRollback_StayWithinBudget()
        {
            const int steps = 20000;
            const int rollbacks = 2000;
            TestWorlds.RandomInputs(55, steps, out var p1, out var p2);
            var world = TestWorlds.Create();

            for (int t = 0; t < 600; t++)
                world.Step(p1[t], p2[t]); // 예열

            var sw = Stopwatch.StartNew();
            for (int t = 0; t < steps; t++)
                world.Step(p1[t], p2[t]);
            sw.Stop();
            double stepMs = sw.Elapsed.TotalMilliseconds / steps;

            // 롤백 1회 = 8틱 전 상태로 복원 + 8틱 재진행 + 체크섬.
            var snapshot = world.State;
            uint sink = 0;
            sw.Restart();
            for (int r = 0; r < rollbacks; r++)
            {
                world.State = snapshot;
                for (int t = 0; t < 8; t++)
                    world.Step(p1[t], p2[t]);
                sink ^= world.ComputeChecksum();
            }
            sw.Stop();
            double rollbackMs = sw.Elapsed.TotalMilliseconds / rollbacks;

            string report = $"[Knockbox][CP1-8] Step 평균 {stepMs * 1000:F2} µs ({stepMs:F5} ms, 예산 {StepBudgetMs} ms), " +
                            $"롤백 8틱 {rollbackMs * 1000:F2} µs ({rollbackMs:F5} ms, 예산 {Rollback8BudgetMs} ms), sink {sink:X8}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);

            Assert.Less(stepMs, StepBudgetMs);
            Assert.Less(rollbackMs, Rollback8BudgetMs);
        }
    }
}
