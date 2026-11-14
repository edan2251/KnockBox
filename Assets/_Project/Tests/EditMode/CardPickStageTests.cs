using System.Collections.Generic;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game.Cards;
using Knockbox.Game.Maps;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>모놀리스 카드 선택 1단계: 카드 표시 목록 규칙, 미등록 선택 맵, 좌우 배치, 표면 템플릿.</summary>
    public class CardPickStageTests
    {
        const string CatalogPath = "Assets/_Project/Data/Cards/CardFaceCatalog.asset";
        const string StagePath = "Assets/_Project/Data/Maps/CardPick/CardPick_Stage.asset";
        const string GameMapsPath = "Assets/_Project/Data/Maps/GameMaps.asset";

        static SimConfig CardConfig()
        {
            var original = new SimConfig(BrawlParams.Movement(MovementParams.Default), BrawlParams.Match(MatchParams.Default),
                BrawlParams.Combat(CombatParams.Default, weaponsEnabled: true, combinedAttackInput: true, throwEnabled: true, revisedBalance: true), RuleSet.Brawl);
            return CardCollectionRules.Apply(original);
        }

        static CardFaceCatalog Catalog => AssetDatabase.LoadAssetAtPath<CardFaceCatalog>(CatalogPath);

        [Test]
        public void CardFaces_CoverEveryCurrentCard_WithinLineRules_AndMarkPlaceholderText()
        {
            var catalog = Catalog;
            Assert.NotNull(catalog);
            Assert.That(catalog.Validate(), Is.Empty);
            var config = CardConfig();
            Assert.AreEqual(CardCollectionRules.CardCount, config.Augments.Length);
            foreach (var card in config.Augments)
            {
                Assert.IsTrue(catalog.TryGet(card.Id, out var face), "표시 목록에 없는 카드: " + card.Id);
                Assert.AreEqual(card.Name, face.title, "모놀리스 이름은 기존 카드 이름과 같아야 함");
                Assert.IsTrue(face.placeholder, "효과가 없는 동안은 시험용 문구");
                StringAssert.Contains(CardFaceCatalog.PlaceholderMark, face.description);
                Assert.IsNull(face.iconSprite, "아이콘 아트 칸은 비워 둔다(도형 조합 사용)");
                Assert.LessOrEqual(face.DescriptionLines().Length, CardFaceCatalog.DescriptionMaxLines);
                Assert.That(face.DescriptionLines().Max(l => l.Length), Is.LessThanOrEqualTo(CardFaceCatalog.LineMaxChars));
            }
            var polarities = catalog.Faces.SelectMany(f => f.effects).Select(e => e.polarity).Distinct().ToArray();
            CollectionAssert.IsSubsetOf(new[] { CardLinePolarity.Positive, CardLinePolarity.Negative, CardLinePolarity.Neutral }, polarities,
                "레이아웃 시험 문구는 세 가지 색을 모두 보여 준다");
        }

        [Test]
        public void CardFaceRules_RejectLongLines_TooManyLines_AndUnmarkedPlaceholder()
        {
            var ok = new CardFace { cardId = 1, title = "이름", description = "임시 설명", placeholder = true,
                effects = new[] { new CardEffectLine { text = "줄", polarity = CardLinePolarity.Positive } } };
            Assert.That(CardFaceCatalog.Validate(ok), Is.Empty);
            var longLine = ok; longLine.description = "임시 " + new string('가', CardFaceCatalog.LineMaxChars);
            Assert.That(CardFaceCatalog.Validate(longLine), Is.Not.Empty);
            var manyLines = ok; manyLines.description = "임시\n둘\n셋\n넷";
            Assert.That(CardFaceCatalog.Validate(manyLines), Is.Not.Empty);
            var manyEffects = ok; manyEffects.effects = Enumerable.Repeat(new CardEffectLine { text = "줄" }, CardFaceCatalog.EffectMaxLines + 1).ToArray();
            Assert.That(CardFaceCatalog.Validate(manyEffects), Is.Not.Empty);
            var longTitle = ok; longTitle.title = new string('가', CardFaceCatalog.TitleMaxChars + 1);
            Assert.That(CardFaceCatalog.Validate(longTitle), Is.Not.Empty);
            var unmarked = ok; unmarked.description = "설명";
            Assert.That(CardFaceCatalog.Validate(unmarked), Is.Not.Empty);
            var seen = new System.Collections.Generic.HashSet<int>();
            Assert.That(CardFaceCatalog.Validate(ok, seen), Is.Empty);
            Assert.That(CardFaceCatalog.Validate(ok, seen), Is.Not.Empty, "카드 Id 중복");
        }

        [Test]
        public void StageMap_IsUnregisteredRewardMap_FloorOnlyTerrain_ThreeSteles_AndLoserStand()
        {
            var stage = AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath);
            Assert.NotNull(stage);
            Assert.IsTrue(stage.IsRewardMap);
            Assert.That(stage.Validate(), Is.Empty);
            var arena = stage.ToArena();
            Assert.AreEqual(0, arena.PropCount);
            var maps = AssetDatabase.LoadAssetAtPath<MapCatalog>(GameMapsPath);
            Assert.IsNull(maps.Find(stage.MatchName), "선택 맵은 GameMaps에 등록하지 않는다");
            Assert.IsFalse(maps.Maps.Contains(stage));
            Assert.IsFalse(maps.CreateRotation().Any(a => a.Name == stage.MatchName), "맵 순환·온라인 목록에 들어가지 않는다");
            // 지형은 바닥 하나뿐이고, 비석은 별도 목록(맵 편집기 상자를 크기 조절·두점자르기한 볼록 다각형)이다.
            Assert.AreEqual(1, arena.BoxCount, "비석은 지형(ToArena)에 들어가지 않는다");
            Assert.AreEqual(MapDefinition.RewardSteleCount, stage.SteleCount);
            var steles = Enumerable.Range(0, stage.SteleCount).Select(stage.Stele).ToArray();
            var heights = new List<float>();
            for (int i = 0; i < steles.Length; i++)
            {
                var outline = steles[i].vertices; var b = steles[i].Bounds;
                if (i > 0) Assert.Less(steles[i - 1].Bounds.center.x, b.center.x, "왼쪽부터 선택지 순서로 저장");
                Assert.DoesNotThrow(() => new ConvexPolygon(outline.Select(p => new Vec2(p.x, p.y)).ToArray()));
                float bottomWidth = HalfSpan(outline, b.yMin + .01f), topWidth = HalfSpan(outline, b.yMax - 1.5f);
                Assert.Less(topWidth, bottomWidth - .2f, "옆변이 위로 갈수록 안쪽으로 기우는 사다리꼴");
                Assert.AreEqual(2, outline.Count(p => Mathf.Abs(p.y - b.yMin) < 1e-4f), "바닥에 평평하게 선다");
                Assert.AreEqual(-9f, b.yMin, 1e-4f, "바닥 위에 선다");
                heights.Add(b.height);
            }
            Assert.That(heights[1], Is.GreaterThan(heights[0]).And.GreaterThan(heights[2]), "가운데가 가장 높다");
            Assert.That(heights.Min() / 19.4f, Is.GreaterThan(.74f)); Assert.That(heights.Max() / 19.4f, Is.LessThan(.86f));
            Assert.AreNotEqual(steles[1].Bounds.xMin - steles[0].Bounds.xMax, steles[2].Bounds.xMin - steles[1].Bounds.xMax, "간격에 작은 차이");
            // 패자 자리(P1 배치)는 비석 반대편. P2가 고르면 좌우를 뒤집는다.
            Assert.IsTrue(steles.All(st => st.Bounds.xMin > stage.LoserStand.x + 8), "비석은 패자의 반대편");
            Assert.AreEqual(1, CardPickLayout.FacingToward(stage.LoserStand.x, steles[1].Bounds.center.x, 0), "P1은 오른쪽 비석을 본다");
            Assert.AreEqual(-1, CardPickLayout.FacingToward(stage.LoserStand.x, steles[1].Bounds.center.x, 1));
            Assert.NotNull(stage.Reward);
            Assert.AreEqual(.3f, stage.Reward.depthDarken, 1e-5f);
        }

        [Test]
        public void RewardData_StaysOutOfGeometryChecksum_AndValidationRejectsBadSteles()
        {
            var stage = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath));
            try
            {
                uint before = stage.ToArena().ComputeChecksum();
                var steles = Enumerable.Range(0, stage.SteleCount).Select(stage.Stele).ToArray();
                var moved = steles.Select(st => st.Copy()).ToArray();
                for (int k = 0; k < moved[2].vertices.Length; k++) moved[2].vertices[k].y = moved[2].vertices[k].y > -8 ? moved[2].vertices[k].y + 1 : moved[2].vertices[k].y;
                stage.SaveReward(true, moved, stage.LoserStand);
                stage.Reward.depthDarken = .5f;
                Assert.That(stage.Validate(), Is.Empty);
                Assert.AreEqual(before, stage.ToArena().ComputeChecksum(), "비석·장면 값은 지형 체크섬에 들어가지 않는다");
                stage.SaveReward(true, steles.Take(2).ToArray(), stage.LoserStand);
                Assert.That(stage.Validate(), Has.Some.Contains("3개"));
                var floating = steles.Select(st => st.Copy()).ToArray();
                for (int k = 0; k < floating[1].vertices.Length; k++) floating[1].vertices[k].y += .5f;
                stage.SaveReward(true, floating, stage.LoserStand);
                Assert.That(stage.Validate(), Has.Some.Contains("바닥"));
                var overlap = steles.Select(st => st.Copy()).ToArray();
                for (int k = 0; k < overlap[1].vertices.Length; k++) overlap[1].vertices[k].x -= 4;
                stage.SaveReward(true, overlap, stage.LoserStand);
                Assert.That(stage.Validate(), Has.Some.Contains("다른 비석"));
                stage.SaveReward(true, steles, new Vector2(steles[0].Bounds.center.x, -9));
                Assert.That(stage.Validate(), Has.Some.Contains("패자 자리"));
                stage.SaveReward(false, steles, stage.LoserStand);
                Assert.That(stage.Validate(), Is.Empty, "보상 맵이 아니면 비석 규칙을 보지 않는다");
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [Test]
        public void MapAuthoring_LoadsStelesAsBoxHandles_DraftsRewardEdits_AndPreviewsMonoliths()
        {
            // 맵 편집 동작은 Undo 기록을 남기므로, 열린 씬이 아니라 버리는 새 씬에서 검사한다(검사 실행기가 끝나면 원래 씬을 되돌린다).
            // 맵 편집 동작은 Undo 기록을 남긴다. 씬을 바꾸지 않고 만든 뒤, 끝에 그 오브젝트들의 Undo 기록을 지우고 직접 지운다
            // (Undo 기록이 남으면 검사 실행기가 끝에 Undo를 되돌릴 때 열린 씬에 되살아난다).
            var definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath));
            MapAuthoring a = null;
            var palette = ScriptableObject.CreateInstance<PaletteAsset>();
            try
            {
                a = new GameObject("Reward_Authoring_Probe").AddComponent<MapAuthoring>();
                a.definition = definition; a.palette = palette;
                a.shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
                a.terrainRoot = new GameObject("Terrain").transform; a.terrainRoot.SetParent(a.transform, false);
                a.spawn0 = new GameObject("P1").transform; a.spawn0.SetParent(a.transform, false);
                a.spawn1 = new GameObject("P2").transform; a.spawn1.SetParent(a.transform, false);
                MapAuthoringOperations.Open(a);
                Assert.IsTrue(a.LoadedReward);
                Assert.AreEqual(1, a.terrainRoot.GetComponentsInChildren<MapBoxHandle>().Length, "지형은 바닥만");
                var handles = a.RewardHandles();
                Assert.AreEqual(3, handles.Length);
                Assert.IsTrue(handles.All(h => h.IsRewardStele && h.name.StartsWith("Reward_Stele_")));
                Assert.AreEqual(definition.LoserStand, (Vector2)a.rewardStand.position);
                Assert.That(a.ValidatePlacement(), Is.Empty);
                var draft = MapAuthoringOperations.Draft(a);
                try { Assert.IsTrue(draft.SameReward(definition), "그대로 저장하면 비석 값이 정확히 같다"); }
                finally { Object.DestroyImmediate(draft); }
                // 비석을 옮기면 맵 저장 대상이 되지만 지형 체크섬은 그대로다.
                handles.First(h => h.Order == 1).transform.localPosition += new Vector3(.25f, 0, 0);
                draft = MapAuthoringOperations.Draft(a);
                try
                {
                    Assert.IsFalse(draft.SameReward(definition));
                    Assert.That(draft.Validate(), Is.Empty);
                    Assert.AreEqual(definition.ToArena().ComputeChecksum(), draft.ToArena().ComputeChecksum());
                }
                finally { Object.DestroyImmediate(draft); }
                MapRewardPreview.Refresh(a, true);
                Assert.AreEqual(3, MapRewardPreview.Count(a));
                for (int i = 0; i < 3; i++)
                {
                    var view = MapRewardPreview.View(a, i);
                    Assert.AreEqual(HideFlags.HideAndDontSave, view.gameObject.hideFlags, "미리보기는 저장하지 않는다");
                    Assert.IsTrue(view.UsesAuthoredOutline);
                    Assert.IsNotEmpty(view.Title.text, "샘플 카드");
                    Assert.Greater(view.transform.position.x, definition.LoserStand.x + 8, "P1 배치: 비석은 패자 오른쪽");
                }
                a.rewardPreviewMirror = true; a.rewardPreviewHover = 1;
                MapRewardPreview.Refresh(a, false);
                Assert.IsTrue(Enumerable.Range(0, 3).All(i => MapRewardPreview.View(a, i).transform.position.x < -definition.LoserStand.x - 8), "P2 반전 미리보기");
                MapRewardPreview.ClearAll();
                Assert.AreEqual(0, MapRewardPreview.Count(a));
            }
            finally
            {
                MapRewardPreview.ClearAll();
                if (a != null)
                {
                    foreach (var t in a.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (var c in t.GetComponents<Component>()) if (c != null) Undo.ClearUndo(c);
                        Undo.ClearUndo(t.gameObject);
                    }
                    Object.DestroyImmediate(a.gameObject);
                }
                Object.DestroyImmediate(definition); Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void Entrance_TallestFirst_CoveredInPlayerColor_LightFadesThenSnapsOn_AndSettlesBeforeAutoPick()
        {
            var settings = new RewardStageSettings();
            var e = new CardPickEntrance(settings);
            const float height = 15f;
            // 솟는 순서: 큰(높은) 비석이 먼저, 높이가 같으면 패자에게 가까운 것.
            Assert.IsTrue(CardPickLayout.RisesBefore(16, 20, 1, 15, 5, 0), "높은 비석이 먼저");
            Assert.IsTrue(CardPickLayout.RisesBefore(15, 5, 2, 15, 20, 0), "높이가 같으면 가까운 것");
            Assert.IsFalse(CardPickLayout.RisesBefore(15, 5, 0, 15.5f, 30, 1));
            // 시간표: 패자 착지 → 비석 차례(0이 먼저) → 쾅 → 각자 바로 색이 걷힘. 마지막 비석이 선 뒤 조명·선택 중 표시·조작 가능.
            Assert.Less(e.LoserLandAt, e.RumbleAt(0));
            for (int k = 0; k < 3; k++)
            {
                Assert.Less(e.RumbleAt(k), e.EmergeAt(k)); Assert.Less(e.EmergeAt(k), e.LandAt(k)); Assert.Less(e.LandAt(k), e.RevealedAt(k));
                if (k > 0) Assert.Less(e.LandAt(k - 1), e.LandAt(k), "차례로 조금씩 어긋나게");
            }
            Assert.Greater(e.LightAt(3), e.LandAt(2), "조명은 비석이 다 선 뒤에 켜진다");
            Assert.AreEqual(e.LightAt(3), e.SettledAt(3)); Assert.AreEqual(e.LightAt(3), e.BannerAt(3));
            Assert.Less(e.SettledAt(3), 120f / SimConfig.TickRate, "연습 상대 자동 선택(120틱)보다 먼저 조작 가능");
            Assert.Greater(e.GrindBeats, 3, "솟는 동안 여러 번 갈린다");
            // 조명: 직전 KO 퇴장에서 이미 꺼졌으므로 처음부터 어둡고(켜졌다 꺼지는 깜빡임 없음), 비석이 다 선 뒤 한 번에 켜진다.
            Assert.AreEqual(0, e.Light(0, 3, out float noPulse), 1e-4f, "선택 맵은 어두운 채 나타난다"); Assert.Less(noPulse, 0);
            // 퇴장: 배경이 보일 때 조명이 먼저 서서히 꺼진다(이징, 중간 값이 있다).
            Assert.AreEqual(1, CardPickEntrance.ExitLight(0), 1e-4f);
            Assert.That(CardPickEntrance.ExitLight(.27f), Is.GreaterThan(.1f).And.LessThan(.9f));
            Assert.AreEqual(0, CardPickEntrance.ExitLight(.6f), 1e-4f);
            // 일반 라운드: KO 퇴장 중 서서히 꺼지고, 라운드 사이·재구성 동안 꺼져 있다가 GO에서 켜진다.
            Assert.AreEqual(1, FrontendView.RoundLight(MatchPhase.KO, true, true, 0, false), 1e-4f);
            Assert.That(FrontendView.RoundLight(MatchPhase.KO, true, true, .27f, false), Is.GreaterThan(.1f).And.LessThan(.9f));
            Assert.AreEqual(0, FrontendView.RoundLight(MatchPhase.KO, true, true, .6f, false), 1e-4f, "옛 배경이 다 꺼지기 전에 조명이 먼저 꺼진다");
            Assert.AreEqual(0, FrontendView.RoundLight(MatchPhase.RoundTransition, true, true, 1, false));
            Assert.AreEqual(0, FrontendView.RoundLight(MatchPhase.Countdown, true, false, 0, false), "재구성 동안 꺼짐(새 맵이 켜진 채 나타나지 않음)");
            Assert.AreEqual(1, FrontendView.RoundLight(MatchPhase.Countdown, true, true, 0, false), "GO");
            Assert.AreEqual(1, FrontendView.RoundLight(MatchPhase.KO, true, true, .8f, true), "기존 카드 화면은 배경과 조명을 유지");
            Assert.AreEqual(0, e.Light(e.LightAt(3) - .01f, 3, out _), 1e-4f, "비석이 다 설 때까지 어둡다");
            Assert.AreEqual(1, e.Light(e.LightAt(3), 3, out float pulse), 1e-4f, "팍 켜진다"); Assert.AreEqual(0, pulse, 1e-4f);
            // 등장부터 플레이어 색으로 다 덮여 있고 내용은 비어 있다.
            for (float t = e.RumbleAt(0); t < e.LandAt(0); t += .01f)
            {
                var pose = e.Monolith(t, 0, height);
                Assert.AreEqual(1, pose.energy, "솟는 동안 플레이어 색으로 다 덮여 있다");
                Assert.IsTrue(float.IsPositiveInfinity(pose.contentFront));
            }
            Assert.Less(e.Monolith(0, 0, height).offsetY + height, 0, "시작 전에는 땅속");
            Assert.Less(e.Monolith(e.RumbleAt(2) - .01f, 2, height).offsetY + height, 0, "차례가 오기 전에는 숨김");
            // 솟음: 처음에 세게(감속 이징), 떨림, 바닥 위로 튀지 않음.
            float start = e.Monolith(e.EmergeAt(0) + .001f, 0, height).offsetY;
            float third = e.Monolith(Mathf.Lerp(e.EmergeAt(0), e.LandAt(0), .33f), 0, height).offsetY;
            Assert.Greater((third - start) / -start, .5f);
            float maxJitter = 0, maxOffset = float.MinValue;
            for (float t = e.EmergeAt(0); t < e.LandAt(0); t += .004f)
            {
                var pose = e.Monolith(t, 0, height);
                maxJitter = Mathf.Max(maxJitter, Mathf.Abs(pose.jitterX)); maxOffset = Mathf.Max(maxOffset, pose.offsetY);
            }
            Assert.Greater(maxJitter, .02f, "갈리며 떨린다");
            Assert.LessOrEqual(maxOffset, .001f, "무거운 돌: 바닥 위로 튀어 오르지 않는다");
            float sink = 0;
            for (float t = e.LandAt(0); t < e.LandAt(0) + .2f; t += .004f) { var pose = e.Monolith(t, 0, height); sink = Mathf.Min(sink, pose.offsetY); Assert.LessOrEqual(pose.offsetY, 1e-4f); }
            Assert.Less(sink, -.03f, "쾅: 살짝 주저앉는다");
            // 멈추자마자 위에서 아래로 걷히며 걷힌 자리부터 내용이 보인다.
            var receding = e.Monolith(Mathf.Lerp(e.LandAt(0), e.RevealedAt(0), .5f), 0, height);
            Assert.That(receding.energy, Is.GreaterThan(0).And.LessThan(1));
            Assert.AreEqual(receding.energy * height, receding.contentFront, 1e-3f);
            var rest = e.Monolith(e.FinishedAt(3), 0, height);
            Assert.AreEqual(0, rest.offsetY, 1e-4f); Assert.AreEqual(0, rest.jitterX, 1e-4f); Assert.AreEqual(0, rest.energy);
            Assert.IsTrue(float.IsNegativeInfinity(rest.contentFront));
            // 패자는 떨어지기 전 숨김, 떨어질수록 빨라진다(가속 이징), 착지 뒤 바닥.
            Assert.IsFalse(e.Loser(settings.loserDropAt - .01f, out _, out _));
            e.Loser(Mathf.Lerp(settings.loserDropAt, e.LoserLandAt, .3f), out float h1, out float v1);
            e.Loser(Mathf.Lerp(settings.loserDropAt, e.LoserLandAt, .8f), out float h2, out float v2);
            Assert.Greater(h1, h2); Assert.Less(v2, v1);
            Assert.IsTrue(e.Loser(e.LoserLandAt, out float h3, out _)); Assert.AreEqual(0, h3, 1e-4f);
            // 선택 중 표시는 조명과 함께 오버슈트하며 튀어나온다.
            Assert.AreEqual(Vector2.zero, e.Banner(e.BannerAt(3) - .01f, 3));
            float biggest = 0;
            for (float t = e.BannerAt(3); t < e.BannerAt(3) + .5f; t += .005f) biggest = Mathf.Max(biggest, e.Banner(t, 3).x);
            Assert.Greater(biggest, 1.05f);
            Assert.AreEqual(1, e.Banner(e.BannerAt(3) + 1, 3).y, 1e-4f);
            Assert.AreEqual(1, FrontendMotion.OutBack(1), 1e-5f); Assert.Greater(FrontendMotion.OutBack(.8f), 1);
        }

        [Test]
        public void Confirm_ClickSurgeFill_SlingshotOntoTop_ReboundJump_PerSteleTiming_SinkAndLand()
        {
            Assert.AreEqual(144, CardPickStageTestsHold());
            float available = CardCollectionRules.LongPickHoldTicks / (float)SimConfig.TickRate - CardPickStage.ExitSeconds;
            var stage = AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath);
            var stand = stage.LoserStand; const float floor = -9;
            float previousFlight = 0;
            // 보상 맵의 비석 셋(가까운 것부터) 모두: 퇴장 전에 끝나고, 멀수록 오래 날며, 재도약 꼭대기는 화면 안이다.
            for (int i = 0; i < stage.SteleCount; i++)
            {
                var bounds = stage.Stele(i).Bounds;
                var top = new Vector2(bounds.center.x, bounds.yMax);
                var c = new CardPickConfirm(available, stand, top, floor, bounds.height);
                string who = "비석 " + (i + 1);
                Assert.Greater(c.Scale, .95f, who + ": 서두르지 않도록 거의 줄이지 않는다");
                Assert.LessOrEqual(c.AbsorbedTime, available + 1e-3f, who + ": 흡수까지 퇴장 전에 끝난다");
                float flight = c.TopLandTime - c.LaunchTime;
                Assert.GreaterOrEqual(flight, .5f * c.Scale - 1e-4f); Assert.Greater(flight, previousFlight, who + ": 멀수록 오래 난다");
                previousFlight = flight;
                Assert.LessOrEqual(c.HopTime - c.TopLandTime, .06f, who + ": 꼭대기에 닿자마자 반동으로 다시 뛴다");
                Assert.LessOrEqual(c.HopApex, CardPickConfirm.HopLimit + 1e-4f, who + ": 재도약 꼭대기는 화면 안");
                Assert.GreaterOrEqual(c.HopApex - top.y, CardPickConfirm.HopMinHeight - 1e-4f, who + ": 재도약이 보일 만큼은 오른다");
                // 클릭 반응: 호버 빛이 빠지고, 첫 박동으로 절반 넘게 → 살짝 걸려 내려앉음 → 두 번째 박동으로 0.3초 안에 꽉 참 → 번쩍.
                Assert.AreEqual(1, c.ChosenLit(0), 1e-4f); Assert.AreEqual(0, c.ChosenLit(CardPickConfirm.LitFadeSeconds * c.Scale), 1e-4f, who + ": 클릭 직후 호버 빛이 빠진다");
                Assert.AreEqual(0, c.Cover(0), 1e-4f);
                float surge = c.Cover(CardPickConfirm.SurgeEnd * c.Scale), caught = c.Cover(CardPickConfirm.CatchEnd * c.Scale);
                Assert.Greater(surge, .5f, who + ": 첫 박동으로 빠르게 절반 넘게");
                Assert.Less(caught, surge, who + ": 살짝 걸려 내려앉는다(쫀득)");
                Assert.AreEqual(1, c.Cover(c.FilledTime), 1e-4f, who + ": 0.3초 안에 꽉 찬다");
                Assert.LessOrEqual(c.FilledTime, .31f); Assert.Less(c.FilledTime, c.LaunchTime, who + ": 발사 전에 이미 꽉 찬다");
                Assert.AreEqual(0, c.FillFlash(c.FilledTime - .01f)); Assert.Greater(c.FillFlash(c.FilledTime + .05f), .5f, who + ": 꽉 찬 순간 번쩍");
                Assert.AreEqual(0, c.FillFlash(c.FilledTime + .3f));
                float tremble = 0; for (float t = 0; t < c.FilledTime; t += .004f) tremble = Mathf.Max(tremble, Mathf.Abs(c.Tremble(t)));
                Assert.Greater(tremble, .03f, who + ": 클릭 반응으로 비석이 떤다"); Assert.AreEqual(0, c.Tremble(c.FilledTime + .1f), who + ": 꽉 차면 떨림이 멎는다");
                // 비행: 실제 포물선(위로 솟는 속도가 줄어듦, 옆으로 일정), 처음만 돌진 잔상.
                float[] vy = new float[4]; float[] xs = new float[4]; bool[] lunges = new bool[4];
                for (int k = 0; k < 4; k++)
                {
                    c.Fighter(Mathf.Lerp(c.LaunchTime, c.TopLandTime, (k + .5f) / 4), out var f, out var v, out float ch, out bool air, out bool lu);
                    Assert.IsTrue(air); Assert.Less(ch, 0); vy[k] = v.y; xs[k] = f.x; lunges[k] = lu;
                }
                for (int k = 1; k < 4; k++) Assert.Less(vy[k], vy[k - 1], who + ": 중력");
                Assert.AreEqual(xs[1] - xs[0], xs[3] - xs[2], .02f, who + ": 옆으로 일정한 속도");
                Assert.IsTrue(lunges[0]); Assert.IsFalse(lunges[3], who + ": 잔상은 끝까지 남지 않는다");
                // 닿음 → 반동 재도약(충전 없음) → 내리찍기 → 쾅 → 슉 → 바닥 착지.
                c.Fighter(Mathf.Lerp(c.TopLandTime, c.HopTime, .5f), out var foot, out _, out float charge, out bool airborne, out _);
                Assert.AreEqual(top, foot); Assert.Less(charge, 0, who + ": 꼭대기에서는 충전하지 않는다");
                c.Fighter(c.HopTime + .03f, out foot, out var hop, out charge, out airborne, out bool lunge);
                Assert.IsTrue(airborne); Assert.IsFalse(lunge); Assert.Greater(hop.y, 5); Assert.AreEqual(top.x, foot.x, 1e-4f);
                c.Fighter(c.ImpactTime, out foot, out _, out _, out airborne, out _);
                Assert.IsFalse(airborne); Assert.AreEqual(top, foot, who + ": 쾅");
                Assert.AreEqual(c.Base(c.ImpactTime), c.Base(c.ImpactTime + c.HitstopSeconds * .9f), 1e-5f);
                Assert.LessOrEqual(c.Sink(c.FloorTime), -bounds.height - 1, who + ": 바닥 끝까지 박힌다");
                c.Fighter(c.FloorTime + .05f, out foot, out _, out _, out airborne, out _);
                Assert.AreEqual(floor, foot.y, 1e-4f, who + ": 바닥 착지");
                var hole = new Vector2(top.x, floor + .15f); var body = new Vector2(top.x, floor + .36f);
                Assert.IsFalse(c.Dot(0, c.FloorTime - .01f, hole, body, out _, out _));
                Assert.IsTrue(c.Dot(0, c.FloorTime + .05f, hole, body, out var burst, out _)); Assert.Greater(burst.y, hole.y + .3f);
                Assert.IsFalse(c.Dot(CardPickConfirm.DotCount - 1, c.AbsorbedTime + .01f, hole, body, out _, out _));
            }
            // 확정 대기가 짧은 모드(42틱)는 전체를 줄여 퇴장 전에 끝낸다.
            float shortHold = 42f / SimConfig.TickRate, shortExit = Mathf.Min(CardPickStage.ExitSeconds, shortHold * .5f);
            var quick = new CardPickConfirm(shortHold - shortExit, stand, new Vector2(13.5f, 6.5f), floor, 15.5f);
            Assert.Less(quick.Scale, 1);
            Assert.LessOrEqual(quick.AbsorbedTime, shortHold - shortExit + 1e-3f);
        }

        static int CardPickStageTestsHold() => CardConfig().Match.PickHoldTicks;

        [Test]
        public void FighterActionPose_MakesStatesTheGameDrawsAsLeftClickChargeAndLunge()
        {
            var config = CardConfig();
            var stand = new FighterState { Position = new Vec2(-13, -8.64f), Facing = 1, Grounded = true };
            // 충전: 경기 규칙상 돌진 가능한 충전이며, 진행도에 따라 충전 단계(외곽선 색·떨림)가 오른다.
            var start = FighterActionPose.Charge(stand, 0, config);
            var full = FighterActionPose.Charge(stand, 1, config);
            Assert.AreEqual(ActionState.Charge, start.Action);
            Assert.IsTrue(FighterCombat.IsLungeReady(start, config), "처음부터 충전 연출이 보이는 상태");
            Assert.AreEqual(3, FighterView.ChargeStage(full, config), "끝에서 최대 충전(깜박임)");
            Assert.Less(FighterView.ChargeStage(start, config), 3);
            Assert.AreEqual(stand.Position, full.Position); Assert.AreEqual(0, full.Velocity.X);
            // 돌진: 속도 방향을 보고, 돌진 공격성(잔상·위험색 계산)이 있다.
            var lunge = FighterActionPose.Lunge(stand, new Vector2(-30, 20));
            Assert.AreEqual(ActionState.Lunge, lunge.Action); Assert.AreEqual(-1, lunge.Facing); Assert.IsFalse(lunge.Grounded);
            Assert.Greater(FighterCombat.Aggression(lunge, config), 0);
            var e = FighterActionPose.LungeStart(0, lunge, Vector2.left, 30);
            Assert.AreEqual(SimEventType.LungeStart, e.Type); Assert.AreEqual(lunge.Position, e.Position);
        }

        [Test]
        public void Banner_TimerAlwaysVisible_PopsEachSecond_AndTurnsRedAndShakesWhenUrgent()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CardPick/Pick_Banner.prefab");
            var banner = Object.Instantiate(prefab).GetComponent<CardPickBanner>();
            try
            {
                var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
                shapes.Activate();
                banner.Build(shapes, FrontendTheme.Load());
                var yellow = new Color(1, .83f, .4f);
                // 초가 막 바뀐 순간은 크게, 조금 뒤에는 원래 크기로(감속 이징).
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 12, 11.99f, 0);
                Assert.IsTrue(banner.TimerVisible); Assert.AreEqual("12", banner.Timer.text);
                Assert.Greater(banner.TimerScale.x, 1.2f);
                Assert.AreEqual(yellow, banner.TimerColor, "여유 있을 때는 플레이어 색");
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 12, 11.5f, .3f);
                Assert.AreEqual(1, banner.TimerScale.x, .02f);
                Assert.AreEqual(0, banner.Urgency(12));
                // 급할 때(5초 이하): 빨강, 흔들림, 더 크게 튄다. 1초에 가까울수록 급하다.
                Assert.Greater(banner.Urgency(1), banner.Urgency(5)); Assert.Greater(banner.Urgency(5), 0);
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 3, 2.6f, .4f);
                var red = banner.TimerColor;
                Assert.Greater(red.r, red.g + .3f, "급하면 빨강");
                var a = banner.Timer.rectTransform.anchoredPosition;
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 3, 2.6f, .43f);
                Assert.AreEqual(a, banner.Timer.rectTransform.anchoredPosition, "급해도 숫자 자리는 흔들리지 않는다");
                Assert.AreEqual(banner.Label.font, banner.Timer.font, "남은 초는 라벨과 같은 글꼴");
                // 급할 때는 심장 박동처럼 초가 바뀐 뒤 0.2초쯤 한 번 더 뛴다(여유 있을 때는 없음).
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 3, 3 - .27f, 0);
                float urgentBeat = banner.TimerScale.x;
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 12, 12 - .27f, 0);
                float calmBeat = banner.TimerScale.x;
                Assert.Greater(urgentBeat, calmBeat + .05f, "급할 때만 두 번째 박동");
                // 표시 전체가 떠다닌다(정지해 있지 않음).
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 12, 11.5f, 0);
                float y0 = banner.transform.localPosition.y;
                banner.Show(0, Vector2.zero, yellow, "P1 선택 중", 12, 11.5f, .37f);
                Assert.Greater(Mathf.Abs(banner.transform.localPosition.y - y0), .05f, "위아래로 떠다닌다");
                banner.Show(0, Vector2.zero, yellow, "한 방 획득", -1);
                Assert.IsFalse(banner.TimerVisible);
            }
            finally { Object.DestroyImmediate(banner.gameObject); }
        }

        [Test]
        public void MonolithView_EntranceEnergyUsesPlayerColor_AndHidesContentUntilRevealed()
        {
            var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            shapes.Activate();
            var go = new GameObject("Monolith_entrance_test");
            try
            {
                var view = go.AddComponent<MonolithView>();
                view.Build(shapes, FrontendTheme.Load());
                Assert.IsTrue(Catalog.TryGet(CardCollectionRules.FirstCardId, out var face));
                view.Bind(face, shapes);
                var purple = new Color(.8f, .3f, 1f);
                // 빈 비석: 내용(이름·아이콘)이 보이지 않는다.
                view.SetEntrance(0, purple, float.PositiveInfinity);
                view.Present(0, 0, false);
                Assert.AreEqual(0, view.TitleColor.a, 1e-4f); Assert.AreEqual(0, view.ContentVisible(view.IconContentY));
                // 기운이 차면 발광이 플레이어 색이고, 아직 내용은 비어 있다.
                view.SetEntrance(1, purple, float.PositiveInfinity);
                view.Present(0, 0, false);
                Assert.That(view.GlowHeight, Is.GreaterThan(view.Height * .95f));
                Assert.AreEqual(purple.r, view.BodyGlowColor.r, 1e-3f); Assert.AreEqual(purple.b, view.BodyGlowColor.b, 1e-3f);
                Assert.AreEqual(0, view.TitleColor.a, 1e-4f);
                // 걷히는 중: 경계 위(아이콘)는 보이고 아래(패널)는 아직 안 보인다.
                float front = (view.PanelTop + view.IconContentY) * .5f;
                view.SetEntrance(front / view.Height, purple, front);
                view.Present(0, 1, false);
                Assert.Greater(view.ContentVisible(view.IconContentY), .9f); Assert.Less(view.ContentVisible(view.FaceBottom), .1f);
                // 다 드러나면 어두운(실루엣) 상태로 보인다.
                view.SetEntrance(0, purple, float.NegativeInfinity);
                view.Present(0, 1, false);
                Assert.AreEqual(1, view.TitleColor.a, 1e-4f);
                Assert.Less(view.TitleColor.r + view.TitleColor.g + view.TitleColor.b, 1.5f, "처음부터 어두운 카드 내용");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static float HalfSpan(Vector2[] poly, float y)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                if ((p.y - y) * (q.y - y) > 0 || Mathf.Abs(q.y - p.y) < 1e-6f) continue;
                float x = p.x + (q.x - p.x) * ((y - p.y) / (q.y - p.y));
                min = Mathf.Min(min, x); max = Mathf.Max(max, x);
            }
            return max - min;
        }

        [Test]
        public void MonolithView_UsesEditorOutline_ForBodyFaceHitAndShadow()
        {
            var stage = AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath);
            var stele = stage.Stele(0);
            var local = CardPickLayout.LocalOutline(stele, out var origin);
            var bounds = stele.Bounds;
            Assert.AreEqual(bounds.yMin, origin.y, 1e-5f, "자리는 아랫변 가운데");
            var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            shapes.Activate();
            var go = new GameObject("Monolith_outline_test");
            try
            {
                var view = go.AddComponent<MonolithView>();
                view.Build(shapes, FrontendTheme.Load());
                view.SetOutline(local);
                Assert.IsTrue(view.UsesAuthoredOutline);
                Assert.AreEqual(bounds.width, view.Width, 1e-3f);
                Assert.AreEqual(bounds.height, view.Height, 1e-3f);
                CollectionAssert.AreEqual(local, view.Outline, "그림자 투영도 편집기 윤곽을 쓴다");
                Assert.That(view.HitArea.rect.height / MonolithView.CanvasUnitsPerWorld, Is.GreaterThanOrEqualTo(view.Height));
                Assert.That(view.PanelSlack, Is.GreaterThan(.2f));
                // 카드 면은 윤곽을 안쪽으로 줄인 모양이라 몸체 안에 있다.
                var body = new ConvexPolygon(view.Outline.Select(p => new Vec2(p.x, p.y)).ToArray());
                var face = MonolithView.ClipY(MonolithView.Offset(view.Outline, -.38f), view.FaceBottom, view.FaceTop);
                foreach (var p in face) Assert.IsTrue(body.Contains(new Vec2(p.x, p.y)));
                var mirrored = CardPickLayout.Mirror(local);
                Assert.DoesNotThrow(() => new ConvexPolygon(mirrored.Select(p => new Vec2(p.x, p.y)).ToArray()), "P2용 좌우 반전도 반시계 볼록 다각형");
                view.SetOutline(null);
                Assert.IsFalse(view.UsesAuthoredOutline, "윤곽을 비우면 기본 비석으로 돌아간다");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0)] [TestCase(1)]
        public void Layout_PutsMonolithsOppositePicker_AndKeepsLeftToRightOptionOrder(int picker)
        {
            var authored = new[] { new Vector3(2.6f, -9, 0), new Vector3(8.4f, -9, 0), new Vector3(14.2f, -9, 0) };
            var placed = authored.Select(p => CardPickLayout.Place(p, picker).x).ToArray();
            Assert.IsTrue(placed.All(x => picker == 0 ? x > 0 : x < 0), "모놀리스는 패자의 반대편");
            var options = new int[3];
            CardPickLayout.AssignOptions(placed, 3, options);
            CollectionAssert.AreEqual(picker == 0 ? new[] { 0, 1, 2 } : new[] { 2, 1, 0 }, options);
            CardPickLayout.AssignOptions(placed, 2, options);
            Assert.AreEqual(-1, options[2], "선택지보다 많은 모놀리스는 쓰지 않는다");
        }

        [Test]
        public void MonolithView_FillsTemplateFromCardData_AndColorsLinesByPolarity()
        {
            var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            shapes.Activate();
            var go = new GameObject("Monolith_test");
            try
            {
                var view = go.AddComponent<MonolithView>();
                view.Build(shapes, FrontendTheme.Load());
                Assert.IsTrue(view.Built);
                Catalog.TryGet(CardCollectionRules.FirstCardId, out var face);
                view.Bind(face, shapes);
                Assert.AreEqual(face.title, view.Title.text);
                Assert.AreEqual(face.description, view.Description.text);
                for (int i = 0; i < CardFaceCatalog.EffectMaxLines; i++)
                {
                    bool used = i < face.effects.Length;
                    Assert.AreEqual(used, view.Effect(i).gameObject.activeSelf);
                    if (used) Assert.AreEqual(MonolithView.LineColor(face.effects[i].polarity), view.Effect(i).color);
                }
                Assert.AreNotEqual(MonolithView.LineColor(CardLinePolarity.Positive), MonolithView.LineColor(CardLinePolarity.Negative));
                Assert.IsTrue(view.IconPartVisible(0)); Assert.IsFalse(view.IconSpriteVisible);
                face.iconSprite = shapes.Circle;
                view.Bind(face, shapes);
                Assert.IsTrue(view.IconSpriteVisible); Assert.IsFalse(view.IconPartVisible(0), "아트가 있으면 도형 아이콘 대신 아트를 쓴다");
                Assert.AreEqual(RenderMode.WorldSpace, view.Canvas.renderMode);
                // 글자는 표면 틀 안에 있다(이름 띠 안의 이름, 패널 안의 설명).
                var name = view.Title.rectTransform;
                Assert.That(name.anchoredPosition.y / MonolithView.CanvasUnitsPerWorld, Is.InRange(view.BandBottom, view.BandTop));
                Assert.That(view.Description.rectTransform.anchoredPosition.y / MonolithView.CanvasUnitsPerWorld, Is.InRange(view.FaceBottom, view.PanelTop));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0f, 0f)] [TestCase(.035f, .95f)] [TestCase(.2f, 2f)] [TestCase(.1f, .3f)]
        public void Outline_IsFlatBottomChamferedTopConvex_AndFaceSharesBodyEdgeSlope(float taper, float chamfer)
        {
            const float width = 6.2f, height = 15.4f;
            var body = MonolithView.Band(width, height, taper, 0, 0, height, 0, chamfer);
            Assert.DoesNotThrow(() => new ConvexPolygon(body.Select(p => new Vec2(p.x, p.y)).ToArray()), "그림자 투영용 볼록 다각형");
            var bodyPolygon = new ConvexPolygon(body.Select(p => new Vec2(p.x, p.y)).ToArray());
            // 비석: 바닥에 평평하게 서고(아래 모서리 그대로) 위 두 모서리만 깎는다.
            Assert.AreEqual(2, body.Count(p => Mathf.Abs(p.y) < 1e-4f), "아랫변은 꼭짓점 둘(깎지 않음)");
            Assert.That(body.Where(p => Mathf.Abs(p.y) < 1e-4f).Select(p => Mathf.Abs(p.x)), Is.All.EqualTo(width * .5f).Within(1e-4f));
            Assert.AreEqual(chamfer > 0 ? 6 : 4, body.Length, "위 두 모서리를 깎으면 꼭짓점 여섯");
            var face = MonolithView.Band(width, height, taper, .38f, .7f, height - 1.35f, .5f, .5f);
            Assert.DoesNotThrow(() => new ConvexPolygon(face.Select(p => new Vec2(p.x, p.y)).ToArray()));
            foreach (var p in face) Assert.IsTrue(bodyPolygon.Contains(new Vec2(p.x, p.y)), "카드 면은 몸체 안에 새겨져 있다");
            // 카드 면의 옆변과 몸체의 옆변은 평행하다(한 덩어리로 좁아진다).
            var bodyEdge = new Vector2(-taper * width, height);
            var faceEdge = face[3] - face[2];
            Assert.That(Mathf.Abs(faceEdge.x * bodyEdge.y - faceEdge.y * bodyEdge.x) / (faceEdge.magnitude * bodyEdge.magnitude), Is.LessThan(1e-4f));
        }

        [Test]
        public void MonolithPrefab_PanelHoldsMaxLinesWithPadding_AndCatalogLinesFitWidth()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CardPick/Monolith.prefab");
            Assert.NotNull(prefab);
            var view = Object.Instantiate(prefab).GetComponent<MonolithView>();
            try
            {
                Assert.IsTrue(view.Built, "프리팹에 부품이 저장되어 있다");
                Assert.That(view.PanelSlack, Is.GreaterThan(.2f), "설명 3줄·효과 3줄과 위아래 여백이 패널 안에 들어간다");
                Assert.That((view.FaceTop - view.FaceBottom) / view.Height, Is.GreaterThan(.8f), "카드 면이 비석 몸체의 대부분을 차지한다");
                Assert.That(view.Height - view.FaceTop, Is.LessThan(1.6f), "면 위로 남는 빈 몸체는 작다");
                var hitRect = view.HitArea;
                Assert.That(hitRect.rect.height / MonolithView.CanvasUnitsPerWorld, Is.GreaterThanOrEqualTo(view.Height), "호버·클릭 영역은 윗부분 포함 비석 전체");
                // 9 글자 크기 이상: 1080p에서 화면 세로 약 19.4유닛 → 1유닛 ≈ 55px. 설명·효과 글자 0.40유닛 ≈ 22px.
                Assert.That(view.Description.fontSize / MonolithView.CanvasUnitsPerWorld * 1080f / 19.4f, Is.GreaterThanOrEqualTo(18f));
                var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
                shapes.Activate();
                view.Build(shapes, FrontendTheme.Load());
                foreach (var face in Catalog.Faces)
                {
                    view.Bind(face, shapes);
                    for (int i = 0; i < face.effects.Length; i++)
                        Assert.That(view.Effect(i).preferredWidth * view.Effect(i).rectTransform.localScale.x,
                            Is.LessThanOrEqualTo(view.Effect(i).rectTransform.rect.width + .5f), face.title + " 효과 줄이 패널 폭을 넘음");
                }
                // 규칙 최대 길이의 줄도 줄여서 넘치지 않는다.
                var longest = new CardFace { cardId = 1, title = new string('가', CardFaceCatalog.TitleMaxChars), description = "임시",
                    effects = new[] { new CardEffectLine { text = new string('가', CardFaceCatalog.LineMaxChars) } } };
                view.Bind(longest, shapes);
                Assert.That(view.Effect(0).preferredWidth * view.Effect(0).rectTransform.localScale.x, Is.LessThanOrEqualTo(view.Effect(0).rectTransform.rect.width + .5f));
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void HoverTargets_FollowThreeStates_ThenChosenStaysLit()
        {
            void Expect(int index, int hovered, int chosen, bool ever, float lit, float silhouette)
            {
                CardPickLayout.HoverTargets(index, hovered, chosen, ever, out float l, out float s);
                Assert.AreEqual(lit, l, $"lit {index}/{hovered}/{chosen}/{ever}");
                Assert.AreEqual(silhouette, s, $"silhouette {index}/{hovered}/{chosen}/{ever}");
            }
            Expect(0, -1, -1, false, 0, 0); // 처음: 모두 꺼짐
            Expect(1, 1, -1, true, 1, 0);   // 호버한 것만 점등
            Expect(0, 1, -1, true, 0, 1);   // 나머지는 그림만 어둡게
            Expect(0, -1, -1, true, 0, 1);  // 호버를 뗀 뒤에도 꺼진 상태로 돌아가지 않는다
            Expect(2, 0, 2, true, 1, 0);    // 확정한 것은 켜 둔다
            Expect(0, 0, 2, true, 0, 1);
        }

        [Test]
        public void Depth_DarkensShorterSteles_TallestStays_AndLitSteleStaysBright()
        {
            Assert.AreEqual(0, CardPickStage.DepthFor(16, 16, 15, .3f), 1e-5f, "가장 높은(앞) 비석은 그대로");
            Assert.AreEqual(.3f, CardPickStage.DepthFor(15, 16, 15, .3f), 1e-5f, "가장 낮은(뒤) 비석이 가장 어둡다");
            Assert.AreEqual(.15f, CardPickStage.DepthFor(15.5f, 16, 15, .3f), 1e-5f, "사이는 높이 비율");
            Assert.AreEqual(0, CardPickStage.DepthFor(15, 15, 15, .3f), 1e-5f, "높이가 같으면 보정 없음");
            var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            shapes.Activate();
            var go = new GameObject("Monolith_depth_test");
            try
            {
                var view = go.AddComponent<MonolithView>();
                view.Build(shapes, FrontendTheme.Load());
                Catalog.TryGet(CardCollectionRules.FirstCardId, out var face);
                view.Bind(face, shapes);
                float Sum(Color c) => c.r + c.g + c.b;
                view.SetDepth(0); view.Present(0, 0, false); var near = view.BodyColor;
                view.SetDepth(.3f); view.Present(0, 0, false); var far = view.BodyColor;
                Assert.Less(Sum(far), Sum(near) * .85f, "뒤 비석은 꺼진 상태에서 어둡다");
                view.Present(1, 0, false);
                Assert.AreEqual(Sum(view.FillColor), Sum(PaletteAsset.MonolithDefaults.IconFill), 1e-4f, "켜지면 원근 보정이 빠져 앞 비석과 같은 밝기로 켜진다");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0)] [TestCase(1)]
        public void FixedLight_StandsAbovePicker_AndMirrorsOffsetForP2(int picker)
        {
            var foot = new Vector2(picker == 0 ? -13.5f : 13.5f, -9);
            var light = CardPickLayout.FixedLight(foot, picker, 29, 0);
            Assert.AreEqual(foot.x, light.x, 1e-5f, "광원은 고르는 패자와 같은 x");
            Assert.AreEqual(20, light.y, 1e-5f);
            var shifted = CardPickLayout.FixedLight(foot, picker, 29, 2);
            Assert.AreEqual(foot.x + (picker == 1 ? -2 : 2), shifted.x, 1e-5f, "P2가 고르면 좌우 오프셋도 뒤집힌다");
        }
    }
}
