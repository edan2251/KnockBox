using System.Linq;
using Knockbox.EditorTools;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>ALL-3 규칙 점검과 CP1-5(점검기가 실제로 위반을 잡는지).</summary>
    public class SimulationRulesTests
    {
        [Test]
        public void SimulationSources_FollowRules()
        {
            var violations = SimRulesChecker.CheckFolder(SimRulesChecker.SimulationFolder);
            Assert.That(violations, Is.Empty, "시뮬레이션 규칙 위반:\n" + string.Join("\n", violations));
        }

        [TestCase("using UnityEngine;", "UnityEngine")]
        [TestCase("var r = new System.Random(1);", "System.Random")]
        [TestCase("var r = new Random(1);", "System.Random")]
        [TestCase("long t = DateTime.Now.Ticks;", "DateTime")]
        [TestCase("var sw = Stopwatch.StartNew();", "Stopwatch")]
        [TestCase("int n = Environment.ProcessorCount;", "Environment")]
        [TestCase("using System.Threading;", "스레드·비동기")]
        [TestCase("async void Run() { await Task.Yield(); }", "스레드·비동기")]
        [TestCase("var d = new Dictionary<int, int>();", "Dictionary")]
        [TestCase("var h = new HashSet<int>();", "HashSet")]
        [TestCase("using System.Linq;", "LINQ")]
        [TestCase("float s = (float)Math.Sin(x);", "삼각함수")]
        [TestCase("float c = MathF.Cos(x);", "삼각함수")]
        [TestCase("float a = (float)Math.Atan2(y, x);", "삼각함수")]
        [TestCase("float t = MathF.Tan(x);", "삼각함수")]
        [TestCase("float r = MathF.Sqrt(x);", "SimMath 밖의 Math 호출")]
        [TestCase("static int counter;", SimRulesChecker.StaticFieldRule)]
        [TestCase("public static float Scale = 2f;", SimRulesChecker.StaticFieldRule)]
        [TestCase("static readonly int[] Table = new int[4];", SimRulesChecker.StaticFieldRule)]
        [TestCase("static readonly List<int> Items = new List<int>();", SimRulesChecker.StaticFieldRule)]
        [TestCase("static Vec2 last = new Vec2(0f, 0f);", SimRulesChecker.StaticFieldRule)]
        [TestCase("public static int Count { get; set; }", SimRulesChecker.StaticFieldRule)]
        [TestCase("public static event System.Action Changed;", SimRulesChecker.StaticFieldRule)]
        public void Checker_DetectsForbiddenPattern(string line, string rule)
        {
            var violations = SimRulesChecker.CheckSource(line, "Probe.cs");
            Assert.That(violations.Select(v => v.Rule), Has.Member(rule), line);
        }

        [TestCase("public const float Dt = 1f / 60f;")]
        [TestCase("public static class SimMath")]
        [TestCase("public static float Abs(float x) => x < 0f ? -x : x;")]
        [TestCase("public static Vec2 Zero => default;")]
        [TestCase("static readonly Vec2 Up = new Vec2(0f, 1f);")]
        [TestCase("float s = SimMath.Sqrt(x);")]
        [TestCase("// static 필드는 쓰지 않는다")]
        [TestCase("using static Knockbox.Simulation.SimMath;")]
        public void Checker_AllowsLegalCode(string line)
        {
            var violations = SimRulesChecker.CheckSource(line, "Probe.cs");
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void MathGateway_MayCallMathButNotTrig()
        {
            Assert.That(SimRulesChecker.CheckSource("public static float Sqrt(float x) => MathF.Sqrt(x);", "SimMath.cs"), Is.Empty);
            Assert.That(SimRulesChecker.CheckSource("float s = MathF.Sin(x);", "SimMath.cs").Select(v => v.Rule), Has.Member("삼각함수"));
        }
    }
}
