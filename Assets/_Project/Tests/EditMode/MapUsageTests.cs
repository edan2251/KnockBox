using System;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>맵 용도(2026-11-16 게임 흐름 CP4): 경기 목록 제외·로비/결과 공간 검증·시상대 지정·제작기 시험 연결·기존 맵 보존.</summary>
    public class MapUsageTests
    {
        const string ResultsPath = "Assets/_Project/Data/Maps/Results/Results_Stage.asset";
        const string LobbyPath = "Assets/_Project/Data/Maps/Lobby/Lobby_Stage.asset";

        static MapDefinition Copy(string path) => UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(path));

        static MapBox[] Boxes(MapDefinition d) => Enumerable.Range(0, d.BoxCount).Select(d.Box).ToArray();

        [Test]
        public void ShippedMaps_HaveTheirUsage_AndExistingMapsStayMatchOrReward()
        {
            Assert.AreEqual(MapUsage.Results, AssetDatabase.LoadAssetAtPath<MapDefinition>(ResultsPath).Usage);
            Assert.AreEqual(MapUsage.Lobby, AssetDatabase.LoadAssetAtPath<MapDefinition>(LobbyPath).Usage);
            Assert.AreEqual(MapUsage.Reward, AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/CardPick/CardPick_Stage.asset").Usage);
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>("Assets/_Project/Data/Maps/GameMaps.asset");
            Assert.IsTrue(catalog.Maps.All(m => m.Usage == MapUsage.Match));
            Assert.IsEmpty(catalog.Validate());
        }

        [Test]
        public void Catalog_RejectsNonMatchMaps_AtRegisterAndRuntimeValidation()
        {
            var catalog = ScriptableObject.CreateInstance<MapCatalog>();
            var results = Copy(ResultsPath);
            try
            {
                Assert.Throws<InvalidOperationException>(() => catalog.Register(results));
                // 직접 목록에 넣은 경우도 실행 단계 검증이 막는다.
                var so = new SerializedObject(catalog);
                var list = so.FindProperty("maps"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = results;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(catalog.Validate().Any(e => e.Contains("경기 맵이 아닌")));
                Assert.Throws<InvalidOperationException>(() => catalog.CreateRotation());
            }
            finally { UnityEngine.Object.DestroyImmediate(results); UnityEngine.Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void SpaceMap_RejectsBlockedDropAndWrongPodium_ButUsageIsNotInTheGeometryHash()
        {
            var results = Copy(ResultsPath);
            try
            {
                // 결과 맵의 점수 숫자 발판(2026-11-20)은 화면 위 밖에서 기다리는 결과 공간 전용 배치라 경기 용도로 바꾸면 범위 밖이 된다. 지형만 남기고 본다.
                results.SaveLayout(Boxes(results), Array.Empty<MapPhysicalBox>(), Array.Empty<MapRope>(), results.Spawn(0), results.Spawn(1));
                uint hash = results.ToArena().ComputeGeometryChecksum();
                results.SetUsage(MapUsage.Match, "");
                Assert.AreEqual(hash, results.ToArena().ComputeGeometryChecksum(), "용도는 판정 해시에 들어가지 않는다");
                results.SetUsage(MapUsage.Results, "box-004");
                // 패자 자리 바로 위를 막으면 떨어질 수 없다.
                var boxes = Boxes(results).ToList();
                var s1 = results.Spawn(1);
                boxes.Add(new MapBox { id = "block", kind = TerrainKind.Solid, minX = s1.x - 1, maxX = s1.x + 1, minY = s1.y + 6.5f, maxY = s1.y + 8f });
                results.SaveLayout(boxes.ToArray(), Array.Empty<MapPhysicalBox>(), Array.Empty<MapRope>(), results.Spawn(0), s1);
                Assert.IsTrue(results.Validate(.8f).Any(e => e.Contains("막혀")));
                // 승자 자리가 시상대 위가 아니면 거절한다.
                boxes.RemoveAt(boxes.Count - 1);
                results.SaveLayout(boxes.ToArray(), Array.Empty<MapPhysicalBox>(), Array.Empty<MapRope>(), new Vector2(-10, -9), s1);
                Assert.IsTrue(results.Validate(.8f).Any(e => e.Contains("시상대")));
                results.SetUsage(MapUsage.Results, "");
                Assert.IsEmpty(results.Validate(.8f), "시상대를 지정하지 않으면 자리 위치는 자유");
            }
            finally { UnityEngine.Object.DestroyImmediate(results); }
        }

        [Test]
        public void SetUsage_KeepsRewardMapsOnTheirOwnSetting()
        {
            var reward = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/CardPick/CardPick_Stage.asset"));
            try
            {
                Assert.Throws<InvalidOperationException>(() => reward.SetUsage(MapUsage.Lobby, ""));
                Assert.Throws<InvalidOperationException>(() => reward.SetUsage(MapUsage.Reward, ""));
                Assert.AreEqual(MapUsage.Reward, reward.Usage);
            }
            finally { UnityEngine.Object.DestroyImmediate(reward); }
        }

        [Test]
        public void SpaceMapPreview_SwapsOnlyThatMap_AndUsageMismatchIsRejected()
        {
            var go = new GameObject("usage_test_runner");
            try
            {
                go.SetActive(false);
                var runner = go.AddComponent<MatchRunner>();
                var so = new SerializedObject(runner);
                so.FindProperty("brawlExperiment").boolValue = true;
                so.FindProperty("startOnAwake").boolValue = false;
                so.FindProperty("mapCatalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapCatalog>("Assets/_Project/Data/Maps/GameMaps.asset");
                so.FindProperty("arenaName").stringValue = MatchRunner.RegisteredMapsName;
                so.FindProperty("resultsMap").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapDefinition>(LobbyPath);
                so.ApplyModifiedPropertiesWithoutUndo();
                runner.FrontendEnabled = true;
                Assert.Throws<InvalidOperationException>(() => runner.PrepareMatchArenas(runner.CreateConfig()), "로비 용도 맵을 결과 공간에 연결하면 거절");
                var results = AssetDatabase.LoadAssetAtPath<MapDefinition>(ResultsPath);
                runner.ConfigureAuthoringPreview(results);
                Assert.IsTrue(runner.IsAuthoringPreview);
                Assert.AreEqual(MapUsage.Results, runner.PreviewUsage);
                var arenas = runner.PrepareMatchArenas(runner.CreateConfig());
                Assert.AreEqual(results.MatchName, arenas[arenas.Length - 1].Name);
                Assert.AreEqual(runner.MapCatalog.Maps.Count + 1, arenas.Length, "경기 맵은 등록 목록 그대로");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
