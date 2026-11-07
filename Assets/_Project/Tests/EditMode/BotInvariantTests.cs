using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP10-1·2: CP9 QA F-1(천장 순간이동) 회귀와 봇 경기 불변식 점검.
    /// 불변식: 경기장 이탈(OutOfBounds) 없음, NaN·무한대 없음, 몸이 막는 지형 속에 들어가지 않음, 연속 명중 4회 미만,
    /// 행동 하나에 300틱 넘게 갇히지 않음, 1초 넘게 겹치지 않음, 붙잡힘에는 붙잡은 쪽이 있음, 히트스탑 24틱 이하, 이벤트 버퍼 넘침 없음.
    /// </summary>
    public class BotInvariantTests
    {
        const int MatchTicks = 7200;

        static SimConfig Config(CombatMode mode, bool grab) =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, mode, grab);

        // F-1 원인 단위 테스트 ---------------------------------------------------

        [Test]
        public void StackedFighter_RisingFromBelow_IsNotPushedIntoCeiling()
        {
            var config = Config(CombatMode.Charge, true);
            var arena = ArenaFiles.LoadByName("Box");
            float half = config.HalfBody;
            float size = config.Movement.BodySize;
            float ceiling = arena.HalfHeight - 1f;
            var prevA = new FighterState { Position = new Vec2(3f, ceiling - half - 0.05f) };
            var prevB = new FighterState { Position = new Vec2(3f, prevA.Position.Y - size) };
            var a = prevA;
            var b = prevB;
            // 아래 파이터가 위로 돌진해 위 파이터와 0.4만큼 겹쳤다.
            b.Position.Y += 0.5f;
            b.Velocity = new Vec2(0f, 25f);
            FighterPush.Resolve(ref a, ref b, prevA, prevB, config, arena);

            Assert.LessOrEqual(a.Position.Y + half, ceiling + 1e-3f, "위 파이터가 천장 속으로 들어가면 안 됨");
            Assert.AreEqual(3f, a.Position.X, 1e-5f, "가로로 밀려나면 안 됨");
            Assert.AreEqual(a.Position.Y - size, b.Position.Y, 1e-4f, "아래 파이터는 위 파이터 바로 밑으로 내려옴");
            Assert.LessOrEqual(b.Velocity.Y, 0f, "아래 파이터의 위로 가던 속도는 멈춤");
            Assert.IsFalse(TerrainCollision.OverlapsSolid(a.Position, half - 1e-3f, arena));
            Assert.IsFalse(TerrainCollision.OverlapsSolid(b.Position, half - 1e-3f, arena));
        }

        [Test]
        public void ResolveX_DoesNotPushOutOfBoxEnteredVertically()
        {
            var arena = ArenaFiles.LoadByName("Box");
            const float half = 0.45f;
            float ceiling = arena.HalfHeight - 1f;
            // 천장 상자(가로 32)에 위로 0.2 파고든 상태에서 오른쪽으로 0.1 움직였다.
            var position = new Vec2(5.1f, ceiling - half + 0.2f);
            bool hit = TerrainCollision.ResolveX(ref position, 1, half, arena, 5f);
            Assert.IsFalse(hit);
            Assert.AreEqual(5.1f, position.X, 1e-5f, "천장 상자의 반대쪽 끝으로 순간이동하면 안 됨");

            // 옆벽에 가로로 들어간 경우는 그대로 막는다.
            float wall = arena.HalfWidth - 1f;
            var side = new Vec2(wall - half + 0.1f, 0f);
            Assert.IsTrue(TerrainCollision.ResolveX(ref side, 1, half, arena, wall - half - 0.05f));
            Assert.AreEqual(wall - half, side.X, 1e-5f);
        }

        // 불변식 점검 ------------------------------------------------------------

        struct Violations
        {
            public int Count;
            public string First;
            public int Rounds;
            public int FightingTicks;

            public void Add(string what)
            {
                if (Count++ == 0)
                    First = what;
            }
        }

        /// <summary>모드별 봇 두 개로 2분 경기를 돌리며 불변식을 검사한다(QA 점검과 같은 시드 규칙).</summary>
        static void RunMatch(CombatMode mode, bool grab, string map, uint seed, ref Violations v)
        {
            var config = Config(mode, grab);
            var arena = ArenaFiles.LoadByName(map);
            var world = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
            var p1 = new BotInputSource(() => world, 0, 300u + seed * 17u);
            var p2 = new BotInputSource(() => world, 1, 700u + seed * 31u);
            int overlapRun = 0;
            string where = $"{mode} grab {grab} {map} seed {seed}";
            for (int t = 0; t < MatchTicks; t++)
            {
                int tick = world.State.Tick;
                world.Step(p1.Poll(tick), p2.Poll(tick));
                if (world.Events.Dropped > 0)
                    v.Add($"{where} t{t}: 이벤트 {world.Events.Dropped}개 버림");
                for (int i = 0; i < world.Events.Count; i++)
                {
                    var e = world.Events[i];
                    if (e.Type == SimEventType.OutOfBounds)
                        v.Add($"{where} t{t}: 경기장 이탈 {e}");
                    else if (e.Type == SimEventType.RoundEnd)
                        v.Rounds++;
                }
                ref readonly var s = ref world.State;
                if (s.Match.Phase != MatchPhase.Fighting)
                {
                    overlapRun = 0;
                    continue;
                }
                v.FightingTicks++;
                Check(s.Fighter0, s.Fighter1, 0, config, arena, where, t, ref v);
                Check(s.Fighter1, s.Fighter0, 1, config, arena, where, t, ref v);
                bool held = s.Fighter0.Action == ActionState.Held || s.Fighter1.Action == ActionState.Held;
                float dx = SimMath.Abs(s.Fighter0.Position.X - s.Fighter1.Position.X);
                float dy = SimMath.Abs(s.Fighter0.Position.Y - s.Fighter1.Position.Y);
                overlapRun = !held && dx < 0.6f && dy < 0.6f ? overlapRun + 1 : 0;
                if (overlapRun == 60)
                    v.Add($"{where} t{t}: 두 몸이 1초 넘게 겹침");
            }
        }

        static void Check(in FighterState f, in FighterState other, int index, SimConfig config, ArenaDef arena, string where, int t, ref Violations v)
        {
            if (float.IsNaN(f.Position.X) || float.IsNaN(f.Position.Y) || float.IsInfinity(f.Position.X) || float.IsInfinity(f.Position.Y)
                || float.IsNaN(f.Velocity.X) || float.IsNaN(f.Velocity.Y) || float.IsInfinity(f.Velocity.X) || float.IsInfinity(f.Velocity.Y))
                v.Add($"{where} t{t}: P{index + 1} NaN·무한대 {f.Position} {f.Velocity}");
            if (TerrainCollision.OverlapsSolid(f.Position, config.HalfBody - 0.03f, arena))
                v.Add($"{where} t{t}: P{index + 1} 지형 속 {f.Position} {f.Action}");
            if (f.ComboHits >= 4)
                v.Add($"{where} t{t}: P{index + 1} 연속 명중 {f.ComboHits}");
            bool free = f.Action == ActionState.Idle || f.Action == ActionState.Run || f.Action == ActionState.Airborne
                        || f.Action == ActionState.WallSlide || f.Action == ActionState.Guard || f.Action == ActionState.KO;
            if (!free && f.ActionTick == 300)
                v.Add($"{where} t{t}: P{index + 1} {f.Action}에 300틱 갇힘");
            if (f.Action == ActionState.Held && other.Action != ActionState.Holding)
                v.Add($"{where} t{t}: P{index + 1} 붙잡힘인데 상대가 붙잡지 않음({other.Action})");
            if (f.HitstopTicks > 24)
                v.Add($"{where} t{t}: P{index + 1} 히트스탑 {f.HitstopTicks}");
        }

        /// <summary>CP10-1: QA에서 순간이동이 재현된 경기(기본 · 모드 A · 잡기 켬 · Pit · 시드 4, 5274틱 부근)를 끝까지.</summary>
        [Test]
        public void QaReproductionMatch_NoOutOfBounds()
        {
            var v = new Violations();
            RunMatch(CombatMode.Charge, true, "Pit", 4, ref v);
            Assert.AreEqual(0, v.Count, v.First);
        }

        /// <summary>CP10-2: 모드 A × 잡기 2 × 맵 3 × 시드 6 × 2분. CP12: 맵 v2 6개(잡기 켬).</summary>
        [TestCase(false, "Box")]
        [TestCase(false, "Towers")]
        [TestCase(false, "Pit")]
        [TestCase(true, "Box")]
        [TestCase(true, "Towers")]
        [TestCase(true, "Pit")]
        [TestCase(true, "Chimes")]
        [TestCase(true, "Gantry")]
        [TestCase(true, "Mobile")]
        [TestCase(true, "Ruins")]
        [TestCase(true, "Scales")]
        [TestCase(true, "Totems")]
        public void ChargeBotMatches_HaveNoInvariantViolations(bool grab, string map)
        {
            var v = new Violations();
            for (uint seed = 1; seed <= 6; seed++)
                RunMatch(CombatMode.Charge, grab, map, seed, ref v);
            string report = $"[Knockbox][CP10-2] A 잡기 {(grab ? "켬" : "끔")} {map}: 경기 6, 라운드 {v.Rounds}, 평균 {(v.Rounds > 0 ? v.FightingTicks / 60f / v.Rounds : 0f):F1}초, 위반 {v.Count}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, v.Count, v.First);
        }
    }
}
