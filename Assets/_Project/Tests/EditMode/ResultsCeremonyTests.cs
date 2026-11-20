using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 결과 발표(2026-11-20): 결과 공간 전용 잠금, 처음부터 자기 자리(승자 시상대), 점수 숫자 발판(실제 물리, 깎은 조각 모양 접촉)의
    /// 판정 경계와 결과 글자 틀·시상대 첫 조형(맵 제작기가 읽는 정점 데이터).
    /// </summary>
    public class ResultsCeremonyTests
    {
        const string StagePath = "Assets/_Project/Data/Maps/Results/Results_Stage.asset";
        const string GlyphPath = "Assets/_Project/Data/Maps/Results/Results_Glyphs.asset";
        static MapDefinition Map(string path) => AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
        static ArenaDef MatchArena => ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);

        static SimConfig Config(int ceremony, int release = 0, bool online = true)
        {
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: false, combinedAttackInput: true, throwEnabled: true, revisedBalance: true);
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default), combat, RuleSet.Brawl);
            var config = CardCollectionRules.Apply(original, resultsSpace: true, resultsCeremonyTicks: ceremony, resultsPropReleaseTick: release);
            var match = config.Match;
            match.RematchBothHold = online;
            match.IntroCountdownTicks = 6; match.CountdownTicks = 6; match.KoTicks = 6;
            return new SimConfig(config.Movement, match, config.Combat, config.Rules, config.Augments);
        }

        static SimWorld World(SimConfig config, ArenaDef results)
        {
            var arenas = new[] { MatchArena, results };
            return new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 7u, MatchPhase.Countdown));
        }

        static InputFrame Ready => new InputFrame(0, 0, SimWorld.ReadyButton);

        /// <summary>winner가 마지막 세트의 마지막 라운드를 이겨 결과 공간에 들어가는 지점까지.</summary>
        static void FinishMatch(SimWorld w, int winner, int loserSets)
        {
            for (int i = 0; i < 100 && w.State.Match.Phase != MatchPhase.Fighting; i++) w.Step(default, default);
            ref var m = ref w.State.Match;
            if (winner == 0) { m.SetScore0 = w.Config.Match.SetsToWinMatch - 1; m.Score0 = w.Config.Match.RoundsToWinSet - 1; m.SetScore1 = loserSets; w.State.Fighter1.Health = 0; }
            else { m.SetScore1 = w.Config.Match.SetsToWinMatch - 1; m.Score1 = w.Config.Match.RoundsToWinSet - 1; m.SetScore0 = loserSets; w.State.Fighter0.Health = 0; }
            for (int i = 0; i < 200 && w.State.Match.Phase != MatchPhase.MatchEnd; i++) w.Step(default, default);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Ceremony_WinnerStandsOnPodiumFromTheStart_LoserBeside_LockedUntilCeremonyEnds(int winner)
        {
            var results = Map(StagePath).ToArena();
            var config = StageConfig(Map(StagePath));
            var w = World(config, results);
            FinishMatch(w, winner, 2);
            Assert.AreEqual(winner, SimWorld.ResultsWinner(w.State.Match));
            Assert.AreEqual(CardCollectionRules.ResultsCeremonyTicks, w.FreeSpaceUnlockTick);
            var win = w.State.GetFighter(winner); var lose = w.State.GetFighter(1 - winner);
            Assert.AreEqual(results.SpawnFoot0.X, win.Position.X, 1e-4f, "실제 승자가 시상대 위");
            Assert.AreEqual(results.SpawnFoot0.Y + config.HalfBody, win.Position.Y, 1e-3f, "처음부터 시상대 윗면에 서 있다(하늘 낙하는 화면 연출)");
            Assert.AreEqual(results.SpawnFoot1.X, lose.Position.X, 1e-4f, "패자는 옆 바닥");
            Assert.AreEqual(results.SpawnFoot1.Y + config.HalfBody, lose.Position.Y, 1e-3f);
            // 잠금 동안 움직이거나 준비해도 아무 일 없다. 풀린 뒤 둘 다 준비하면 같은 방 새 경기.
            var push = new InputFrame(127, 0, InputButtons.Attack | InputButtons.Jump | SimWorld.ReadyButton);
            while (w.State.Match.PhaseTick < w.FreeSpaceUnlockTick - 1) w.Step(push, push);
            Assert.AreEqual(MatchPhase.MatchEnd, w.State.Match.Phase, "발표 동안 준비를 받지 않는다");
            Assert.AreEqual(results.SpawnFoot0.X, w.State.GetFighter(winner).Position.X, 1e-3f, "발표 동안 조작이 잠긴다");
            Assert.AreEqual(2, w.State.Match.GetSetScore(1 - winner), "점수는 그대로");
            w.Step(Ready, Ready);
            w.Step(Ready, Ready);
            Assert.AreEqual(MatchPhase.Countdown, w.State.Match.Phase, "조작이 풀리면 둘 다 준비해 새 경기");
            Assert.AreEqual(0, w.State.Match.SetScore0 + w.State.Match.SetScore1, "새 경기는 점수 0부터");
        }

        [Test]
        public void NoCeremony_KeepsEarlierResultsRules_AndConfigHash()
        {
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default));
            uint before = CardCollectionRules.Apply(original, resultsSpace: true, lobbySpace: true, vsIntro: false, lobbyExit: true).ComputeChecksum();
            Assert.AreEqual(before, CardCollectionRules.Apply(original, resultsSpace: true, lobbySpace: true, vsIntro: false, lobbyExit: true,
                resultsCeremonyTicks: 0, resultsPropReleaseTick: 0).ComputeChecksum(), "끈 설정의 해시(그 전 기록)는 그대로");
            Assert.AreNotEqual(before, CardCollectionRules.Apply(original, resultsSpace: true, lobbySpace: true, vsIntro: false, lobbyExit: true,
                resultsCeremonyTicks: 192).ComputeChecksum());
            Assert.AreEqual(CardCollectionRules.Apply(original).ComputeChecksum(),
                CardCollectionRules.Apply(original, resultsCeremonyTicks: 192, resultsPropReleaseTick: 146).ComputeChecksum(), "결과 공간이 없으면 쓰지 않는다");
            // 끈 설정: 패자는 그 전처럼 위에서 떨어지고 120틱에 풀린다.
            var w = World(Config(0, Map(StagePath).ResultsStage.signDropTick), Map(StagePath).ToArena());
            FinishMatch(w, 0, 1);
            Assert.AreEqual(w.Config.Match.ResultsUnlockTick, w.FreeSpaceUnlockTick);
            Assert.Greater(w.State.Fighter1.Position.Y, w.ResultsArena.SpawnFoot1.Y + w.Config.HalfBody + 1f, "그 전 규칙: 패자는 자리 위에서 떨어진다");
        }

        static int Body(ArenaDef arena, string id)
        {
            for (int b = 0; b < arena.DynamicPropCount; b++)
                if (arena.GetProp(arena.DynamicProp(b)).Id == id) return b;
            Assert.Fail("몸 없음: " + id);
            return -1;
        }

        static SimConfig StageConfig(MapDefinition stage) =>
            Config(CardCollectionRules.ResultsCeremonyTicks, stage.ResultsStage.signDropTick);

        /// <summary>P2가 5 : 2로 이겨 결과 공간에 들어가고 숫자가 걸려 거의 멈출 때까지(조작이 풀린 뒤). 두 월드는 같은 입력.</summary>
        static (SimWorld a, SimWorld b) SettledScore(MapDefinition stage, int extraTicks = 120)
        {
            var results = stage.ToArena();
            var config = StageConfig(stage);
            var a = World(config, results); var b = World(config, results);
            FinishMatch(a, 1, 2); FinishMatch(b, 1, 2);
            while (a.State.Match.PhaseTick < a.FreeSpaceUnlockTick + extraTicks)
            {
                a.Step(default, default); b.Step(default, default);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "같은 입력의 두 시뮬레이션 tick " + a.State.Match.PhaseTick);
            }
            return (a, b);
        }

        [Test]
        public void ScoreDigits_OnlyScoreBodiesRemain_WaitAboveScreen_FallAndCatchOnRopes_AndBothPeersMatch()
        {
            var stage = Map(StagePath);
            CollectionAssert.IsEmpty(stage.Validate());
            var results = stage.ToArena();
            Assert.IsTrue(results.HasScoreBodies);
            var config = StageConfig(stage);
            int release = config.Match.ResultsPropReleaseTick;
            Assert.AreEqual(stage.ResultsStage.signDropTick, release);
            var a = World(config, results); var b = World(config, results);
            FinishMatch(a, 1, 2); FinishMatch(b, 1, 2);
            Assert.AreEqual(2, a.State.Match.SetScore0); Assert.AreEqual(5, a.State.Match.SetScore1);
            int left = Body(results, "score-p1-2"), right = Body(results, "score-p2-5");
            for (int body = 0; body < results.DynamicPropCount; body++)
            {
                var def = results.GetProp(results.DynamicProp(body));
                bool keep = def.ScoreSlot == 0 || body == left || body == right;
                Assert.AreEqual(!keep, a.State.Props.IsDestroyed(body), def.Id + ": 점수와 맞는 숫자·콜론만 남는다");
            }
            float startY = PropState.At(in a.State.Props, right).Position.Y;
            Assert.Greater(startY - results.GetProp(results.DynamicProp(right)).HalfSize.Y, results.HalfHeight, "화면 위 밖에서 기다린다");
            while (a.State.Match.PhaseTick < release - 1) { a.Step(default, default); b.Step(default, default); }
            Assert.AreEqual(startY, PropState.At(in a.State.Props, right).Position.Y, 1e-5f, "글자 낙하 틱 전에는 움직이지 않는다");
            float lowest = startY, previousSpeed = 0, biggestDrop = 0;
            for (int t = 0; t < 240; t++)
            {
                a.Step(default, default); b.Step(default, default);
                var body = PropState.At(in a.State.Props, right);
                lowest = Mathf.Min(lowest, body.Position.Y);
                float speed = -body.Velocity.Y;
                biggestDrop = Mathf.Max(biggestDrop, previousSpeed - speed);
                previousSpeed = speed;
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "같은 입력의 두 시뮬레이션 tick " + t);
            }
            var s = stage.ResultsStage;
            float rest = s.scoreBaselineY + s.scoreHeight * .5f;
            var settled = PropState.At(in a.State.Props, right);
            float dot = PropState.At(in a.State.Props, Body(results, "score-dot-0")).Position.Y;
            TestContext.WriteLine($"[Knockbox][score-digits] 5 시작 y {startY:0.00}, 최저 y {lowest:0.000}(쉬는 자리 {rest:0.00}), 한 틱 최대 감속 {biggestDrop:0.00}, 240틱 뒤 y {settled.Position.Y:0.000} 기울기 {Mathf.Atan2(settled.Rotation.Y, settled.Rotation.X) * Mathf.Rad2Deg:0.00}°, 아래 콜론 점 y {dot:0.00}");
            Assert.Less(lowest, startY - 5f, "중력으로 떨어진다");
            Assert.Greater(lowest, rest - .6f, "줄이 팽팽해지면 쉬는 자리 근처에서 멈춘다");
            Assert.Greater(biggestDrop, 5f, "줄에 걸리는 순간 낙하 속도가 크게 꺾인다('툭')");
            Assert.AreEqual(rest, settled.Position.Y, .35f, "쉬는 자리에 매달린다");
            Assert.Less(Mathf.Abs(settled.Position.X - (float)results.GetProp(results.DynamicProp(right)).Center.X), .5f, "제 자리 아래");
        }

        [Test]
        public void Fighter_StandsOnTheInnerBarOfFive_InsideItsUpperCounter_AndRidesIt()
        {
            var stage = Map(StagePath);
            var (w, _) = SettledScore(stage);
            var arena = w.Arena;
            int five = Body(arena, "score-p2-5");
            var def = arena.GetProp(arena.DynamicProp(five));
            float k = stage.ResultsStage.scoreHeight / ResultGlyphLibrary.UnitHeight;
            // 5의 가운데 가로획 윗면(글자 단위 y 1.73) 위, 위 칸 안쪽(x 1.3, 오른쪽이 열린 칸).
            library(stage).TryGet("5", out var glyph);
            float midTop = 0; for (float y = 1.5f; y < 2.2f; y += .005f) if (Covered(glyph, new Vector2(1.3f, y))) midTop = y;
            var body = PropState.At(in w.State.Props, five);
            var local = new Vec2((1.3f - glyph.Width * .5f) * k, (midTop + .005f - 1.5f) * k + w.Config.HalfBody + .05f);
            ref var f = ref w.State.Fighter1;
            f.Position = body.ToWorld(local);
            f.Velocity = Vec2.Zero;
            float startAngle = Mathf.Atan2(body.Rotation.Y, body.Rotation.X);
            bool stood = false;
            for (int t = 0; t < 90; t++)
            {
                w.Step(default, default);
                if (w.State.Fighter1.OnProp == five + 1) stood = true;
            }
            var now = PropState.At(in w.State.Props, five);
            var feet = now.ToLocal(w.State.Fighter1.Position - new Vec2(0, w.Config.HalfBody));
            float feetGlyphY = feet.Y / k + 1.5f;
            TestContext.WriteLine($"[Knockbox][score-digits] 5 안쪽 가로획: 발 높이(글자 단위) {feetGlyphY:0.000}, 가로획 윗면 {midTop:0.000}, OnProp {w.State.Fighter1.OnProp}, 5 기울기 변화 {(Mathf.Atan2(now.Rotation.Y, now.Rotation.X) - startAngle) * Mathf.Rad2Deg:0.00}°");
            Assert.IsTrue(stood, "5의 가운데 가로획 위에 선다");
            Assert.AreEqual(five + 1, w.State.Fighter1.OnProp, "계속 서 있다");
            Assert.IsTrue(w.State.Fighter1.Grounded);
            Assert.AreEqual(midTop, feetGlyphY, .08f, "숫자 상자 윗면이 아니라 안쪽 가로획 위");
        }

        [Test]
        public void Fighter_JumpsFromFiveToTwo_LandsOnIt_AndTheDigitSwings_BothPeersMatch()
        {
            var stage = Map(StagePath);
            var (a, b) = SettledScore(stage);
            var arena = a.Arena;
            int five = Body(arena, "score-p2-5"), two = Body(arena, "score-p1-2");
            var fiveDef = arena.GetProp(arena.DynamicProp(five));
            var top = PropState.At(in a.State.Props, five).ToWorld(new Vec2(-fiveDef.HalfSize.X + .6f, fiveDef.HalfSize.Y + a.Config.HalfBody + .05f));
            foreach (var w in new[] { a, b }) { w.State.Fighter1.Position = top; w.State.Fighter1.Velocity = Vec2.Zero; }
            for (int t = 0; t < 20; t++) { a.Step(default, default); b.Step(default, default); }
            Assert.AreEqual(five + 1, a.State.Fighter1.OnProp, "5의 윗면에 서 있다");
            float twoAngle = Mathf.Atan2(PropState.At(in a.State.Props, two).Rotation.Y, PropState.At(in a.State.Props, two).Rotation.X);
            float targetX = PropState.At(in a.State.Props, two).Position.X;
            bool landed = false; int landedTick = -1; float maxSwing = 0;
            for (int t = 0; t < 150; t++)
            {
                var f = a.State.Fighter1;
                sbyte move = (sbyte)(f.Position.X > targetX + .3f && !landed ? -127 : 0);
                var buttons = t < 6 ? InputButtons.Jump : InputButtons.None;
                var input = new InputFrame(move, 0, buttons);
                a.Step(default, input); b.Step(default, input);
                Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "같은 입력의 두 시뮬레이션 tick " + t);
                if (!landed && a.State.Fighter1.OnProp == two + 1) { landed = true; landedTick = t; }
                var body = PropState.At(in a.State.Props, two);
                maxSwing = Mathf.Max(maxSwing, Mathf.Abs(Mathf.Atan2(body.Rotation.Y, body.Rotation.X) - twoAngle) * Mathf.Rad2Deg);
            }
            TestContext.WriteLine($"[Knockbox][score-digits] 5 → 2 점프: 착지 tick {landedTick}, 2의 최대 기울기 변화 {maxSwing:0.00}°, 끝 OnProp {a.State.Fighter1.OnProp}");
            Assert.IsTrue(landed, "옆 숫자(2)로 건너가 그 위에 선다");
            Assert.Greater(maxSwing, .3f, "올라탄 숫자가 함께 흔들린다");
        }

        static ResultGlyphLibrary library(MapDefinition stage) => new ResultGlyphLibrary(stage.ResultGlyphs);

        [Test]
        public void GlyphLibrary_HasEveryGlyph_ConvexPieces_AndReadableCounters()
        {
            var source = Map(GlyphPath);
            Assert.IsNotNull(source);
            CollectionAssert.IsEmpty(source.Validate());
            var library = new ResultGlyphLibrary(source);
            foreach (var key in ResultGlyphLibrary.Keys)
            {
                Assert.IsTrue(library.TryGet(key, out var glyph), key);
                Assert.Greater(glyph.Pieces.Count, 0, key);
                foreach (var piece in glyph.Pieces)
                    Assert.DoesNotThrow(() => new ConvexPolygon(piece.Select(p => new Vec2(p.x, p.y)).ToArray()), key + " 조각은 반시계 볼록");
            }
            Assert.AreEqual(ResultGlyphLibrary.Keys.Length, library.Count, "결과 화면이 쓰는 숫자 0~5와 콜론만 있다");
            // 1의 깃발과 기둥이 만나는 윗부분에 깎인 홈이 없다(2026-11-20 깨져 보이던 부분).
            library.TryGet("1", out var one);
            Assert.IsTrue(Covered(one, new Vector2(.73f, 2.98f)), "1 윗부분 홈 없음");
            // 숫자 발판: 5의 위·아래 안쪽 칸 높이가 결과 화면 크기에서 선수 몸(0.9)보다 넉넉하다.
            library.TryGet("5", out var five);
            float scale = Map(StagePath).ResultsStage.scoreHeight / ResultGlyphLibrary.UnitHeight;
            float upperCounter = Gap(five, 1.3f, 1.5f, 3f), lowerCounter = Gap(five, .7f, 0f, 1.5f);
            Assert.Greater(upperCounter * scale, .9f + .2f, "5 위 칸");
            Assert.Greater(lowerCounter * scale, .9f + .2f, "5 아래 칸");
        }

        /// <summary>x에서 y0~y1 사이의 가장 긴 빈 구간(글자 단위).</summary>
        static float Gap(ResultGlyphLibrary.Glyph glyph, float x, float y0, float y1)
        {
            float best = 0, run = 0;
            for (float y = y0; y <= y1; y += .01f)
            {
                run = Covered(glyph, new Vector2(x, y)) ? 0 : run + .01f;
                best = Mathf.Max(best, run);
            }
            return best;
        }

        static bool Covered(ResultGlyphLibrary.Glyph glyph, Vector2 point)
        {
            foreach (var piece in glyph.Pieces)
                if (new ConvexPolygon(piece.Select(p => new Vec2(p.x, p.y)).ToArray()).Contains(new Vec2(point.x, point.y))) return true;
            return false;
        }

        [Test]
        public void ResultsStage_IsFlatFloorAndPodium_WinnerSpotOnTop()
        {
            var stage = Map(StagePath);
            CollectionAssert.IsEmpty(stage.Validate());
            Assert.AreEqual(MapUsage.Results, stage.Usage);
            Assert.AreEqual(2, stage.BoxCount, "평평한 바닥과 시상대만(천장·벽·계단 없음)");
            var podium = Enumerable.Range(0, stage.BoxCount).Select(stage.Box).Single(b => b.id == stage.PodiumBoxId);
            Assert.IsTrue(podium.IsPolygon, "모서리를 깎은 볼록 시상대");
            Assert.AreEqual(podium.maxY, stage.Spawn(0).y, 1e-4f);
            Assert.Greater(podium.maxX - podium.minX, 3f, "승자 양옆에 여유");
            Assert.Greater(podium.maxY - podium.minY, 1.5f, "패자 바닥과의 높이 차이");
            Assert.IsNotNull(stage.ResultGlyphs, "결과 글자 틀 연결");
            Assert.AreEqual(14, stage.PhysicalBoxCount, "양쪽 숫자 0~5와 콜론 두 점");
            Assert.AreEqual(12 + 2 * 12, stage.RopeCount, "숫자마다 위로 줄 하나, 콜론 점마다 양쪽 숫자 후보로 줄 하나씩");
        }
    }
}
