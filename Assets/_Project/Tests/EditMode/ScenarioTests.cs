using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP1-6과 공용 expect 러너: expect가 있는 모든 시나리오를 실행한다.</summary>
    public class ScenarioTests
    {
        public static IEnumerable<string> ScenariosWithExpects()
        {
            foreach (string path in ScenarioJson.ListFiles())
            {
                if (ScenarioJson.LoadFile(path).Expects.Length > 0)
                    yield return Path.GetFileNameWithoutExtension(path);
            }
        }

        [TestCaseSource(nameof(ScenariosWithExpects))]
        public void Scenario_ExpectsPass(string name)
        {
            var def = ScenarioJson.LoadByName(name);
            var result = ScenarioRun.Run(def, ScenarioConfig.Create(def), ArenaFiles.LoadByName(def.Arena));
            Assert.AreEqual(def.Ticks, result.TicksRun);
            Assert.IsTrue(result.Passed, $"{name} 실패 {result.Failures.Count}건:\n" + string.Join("\n", result.Failures));
        }

        [Test]
        public void TuningOverride_ChangesConfig()
        {
            const string json = @"{ ""ticks"": 10, ""tuning"": {
                ""Combat.MaxHealth"": 30, ""Combat.TestAttack.KnockbackStrength"": 20,
                ""Match.RoundsToWin"": 2, ""Movement.DashJumpCancelEnabled"": false } }";
            var config = ScenarioConfig.Create(ScenarioJson.Parse(json));
            Assert.AreEqual(30, config.Combat.MaxHealth);
            Assert.AreEqual(20f, config.Combat.TestAttack.KnockbackStrength);
            Assert.AreEqual(2, config.Match.RoundsToWin);
            Assert.IsFalse(config.Movement.DashJumpCancelEnabled);
            Assert.AreEqual(MovementParams.Default.MaxRunSpeed, config.Movement.MaxRunSpeed, "덮어쓰지 않은 값은 그대로");
        }

        [Test]
        public void SampleScenario_LoadsAndEvaluatesExpects()
        {
            var def = ScenarioJson.LoadByName("sample_walk_jump");
            Assert.AreEqual("sample_walk_jump", def.Name);
            Assert.AreEqual(90, def.Ticks);
            Assert.AreEqual(2, def.P1.Length);
            Assert.Greater(def.Expects.Length, 0);

            var result = ScenarioRun.Run(def, SimConfig.CreateDefault(), TestWorlds.Box);
            Assert.AreEqual(def.Expects.Length, result.ExpectCount);
            Assert.IsTrue(result.Passed, string.Join("\n", result.Failures));
        }

        [Test]
        public void WrongExpect_IsReportedAsFailure()
        {
            const string json = @"{
              ""name"": ""wrong"", ""ticks"": 30,
              ""p1"": [ { ""from"": 0, ""to"": 29, ""hold"": [""Right""] } ],
              ""expect"": [
                { ""tick"": 20, ""fighter"": 0, ""field"": ""vel.x"", ""min"": 50 },
                { ""tick"": 20, ""fighter"": 0, ""field"": ""action"", ""equals"": ""Airborne"" },
                { ""tick"": 20, ""fighter"": 0, ""field"": ""grounded"", ""equals"": true }
              ]
            }";
            var result = ScenarioRun.Run(ScenarioJson.Parse(json), SimConfig.CreateDefault(), TestWorlds.Box);
            Assert.IsFalse(result.Passed);
            Assert.AreEqual(2, result.Failures.Count, string.Join("\n", result.Failures));
            StringAssert.Contains("vel.x", result.Failures[0]);
            StringAssert.Contains("action", result.Failures[1]);
        }

        [Test]
        public void ExpectOutsideScenarioLength_IsReported()
        {
            const string json = @"{ ""ticks"": 10, ""expect"": [ { ""tick"": 11, ""field"": ""pos.x"", ""min"": 0 } ] }";
            var result = ScenarioRun.Run(ScenarioJson.Parse(json), SimConfig.CreateDefault(), TestWorlds.Box);
            Assert.IsFalse(result.Passed);
            StringAssert.Contains("길이", result.Failures[0]);
        }

        [TestCase(@"{ ""ticks"": 10, ""p1"": [ { ""from"": 0, ""to"": 5, ""hold"": [""Sideways""] } ] }", "Sideways")]
        [TestCase(@"{ ""ticks"": 10, ""expect"": [ { ""tick"": 1, ""field"": ""hp.max"", ""min"": 0 } ] }", "hp.max")]
        [TestCase(@"{ ""ticks"": 10, ""p1"": [ { ""from"": 0, ""at"": 3, ""hold"": [""Left""] } ] }", "하나만")]
        [TestCase(@"{ ""ticks"": 0 }", "ticks")]
        [TestCase(@"{ ""ticks"": 10, ""mode"": ""Kick"" }", "Kick")]
        [TestCase(@"{ ""ticks"": 10, ""tuning"": { ""Combat.NoSuchField"": 1 } }", "NoSuchField")]
        [TestCase(@"{ ""ticks"": 10, ""tuning"": { ""Combat.MaxHealth"": 1.5 } }", "정수")]
        [TestCase(@"{ ""ticks"": 10, }", "JSON")]
        public void InvalidScenario_ReportsReadableError(string json, string fragment)
        {
            var e = Assert.Throws<FormatException>(() => ScenarioJson.Parse(json));
            StringAssert.Contains(fragment, e.Message);
        }

        [Test]
        public void HoldAndPress_ExpandToExpectedTicks()
        {
            var entries = new[]
            {
                ScenarioInput.Hold(2, 4, ScenarioDirections.Right, InputButtons.None),
                ScenarioInput.Press(3, 2, ScenarioDirections.None, InputButtons.Jump),
                ScenarioInput.Hold(4, 4, ScenarioDirections.Left, InputButtons.None),
            };
            var frames = ScenarioInputs.Build(entries, 7);
            Assert.AreEqual(new InputFrame(0, 0, InputButtons.None), frames[1]);
            Assert.AreEqual(new InputFrame(1, 0, InputButtons.None), frames[2]);
            Assert.AreEqual(new InputFrame(1, 0, InputButtons.Jump), frames[3]);
            Assert.AreEqual(new InputFrame(0, 0, InputButtons.Jump), frames[4], "같은 틱의 좌우는 상쇄");
            Assert.AreEqual(new InputFrame(0, 0, InputButtons.None), frames[5]);
        }

        [Test]
        public void ChargeAndAttackTokens_ExpandIndependentlyForHoldAndPress()
        {
            const string json = @"{
              ""ticks"": 8,
              ""p1"": [
                { ""from"": 2, ""to"": 4, ""hold"": [""Charge""] },
                { ""at"": 3, ""press"": [""Attack""] }
              ],
              ""p2"": [
                { ""at"": 1, ""holdFor"": 2, ""press"": [""Charge""] },
                { ""from"": 4, ""to"": 6, ""hold"": [""Attack""] }
              ]
            }";
            var def = ScenarioJson.Parse(json);
            var p1 = ScenarioInputs.Build(def.P1, def.Ticks);
            var p2 = ScenarioInputs.Build(def.P2, def.Ticks);
            var expected1 = new[] { InputButtons.None, InputButtons.None, InputButtons.Charge,
                InputButtons.Charge | InputButtons.Attack, InputButtons.Charge, InputButtons.None, InputButtons.None, InputButtons.None };
            var expected2 = new[] { InputButtons.None, InputButtons.Charge, InputButtons.Charge,
                InputButtons.None, InputButtons.Attack, InputButtons.Attack, InputButtons.Attack, InputButtons.None };
            for (int tick = 0; tick < def.Ticks; tick++)
            {
                Assert.AreEqual(new InputFrame(0, 0, expected1[tick]), p1[tick], $"P1 틱 {tick}");
                Assert.AreEqual(new InputFrame(0, 0, expected2[tick]), p2[tick], $"P2 틱 {tick}");
            }
        }

        [Test]
        public void ShowcaseScenarios_Exist()
        {
            int count = 0;
            foreach (string path in ScenarioJson.ListFiles())
            {
                if (Path.GetFileName(path).StartsWith("showcase_", StringComparison.Ordinal))
                    count++;
            }
            Assert.Greater(count, 0);
        }
    }
}
