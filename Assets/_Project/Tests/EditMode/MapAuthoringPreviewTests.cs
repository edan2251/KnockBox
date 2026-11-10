using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class MapAuthoringPreviewTests
    {
        readonly List<Object> owned = new List<Object>();
        UnityEngine.SceneManagement.Scene scene;
        GameObject root;
        MatchRunner runner;
        MapDefinition saved;
        string savedPath;
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [OneTimeSetUp]
        public void SaveUnregisteredFixture()
        {
            string folder = "Assets/_Project/LocalOnly/Map_CP1_Validation/Run_" + Guid.NewGuid().ToString("N");
            MaintenanceSceneMigration.EnsureFolder(folder);
            savedPath = folder + "/Unregistered_Trial.asset";
            saved = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(MapAuthoringOperations.Folder + "/BrawlOriginal.asset"));
            saved.MakeEditableCopy(Guid.NewGuid().ToString("N"), "미등록_시험");
            saved.Metadata().MapTheme.mode = MapThemeMode.Preset;
            saved.Metadata().MapTheme.preset = MapThemePreset.Sunset;
            AssetDatabase.CreateAsset(saved, savedPath);
            AssetDatabase.SaveAssetIfDirty(saved);
        }

        [SetUp]
        public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Map_CP1_Probe");
            root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            runner = root.AddComponent<MatchRunner>();
            runner.BrawlExperiment = true;
            runner.Rules = RuleSet.Brawl;
            runner.Tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
            runner.CombatTuning = AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset");
        }

        [TearDown]
        public void Teardown()
        {
            runner.StopRecording();
            EditorSceneManager.ClosePreviewScene(scene);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        MapAuthoring Draft(MapDefinition definition)
        {
            var go = new GameObject("Authoring"); go.transform.SetParent(root.transform, false);
            var a = go.AddComponent<MapAuthoring>(); a.definition = definition;
            a.terrainRoot = new GameObject("Terrain").transform; a.terrainRoot.SetParent(go.transform, false);
            a.spawn0 = new GameObject("P1").transform; a.spawn0.SetParent(go.transform, false);
            a.spawn1 = new GameObject("P2").transform; a.spawn1.SetParent(go.transform, false);
            a.spawn0.position = definition.Spawn(0); a.spawn1.position = definition.Spawn(1);
            for (int i = 0; i < definition.BoxCount; i++)
            {
                var box = new GameObject(i.ToString()); box.transform.SetParent(a.terrainRoot, false);
                box.AddComponent<MapBoxHandle>().Initialize(definition.Box(i), i);
            }
            a.MarkLoaded(); return a;
        }

        [Test]
        public void SavedUnregisteredDefinition_StartsAloneWithItsTheme_LeavesCatalogAndMainSelectionUntouched()
        {
            var baseline = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapAuthoringOperations.Folder + "/BrawlOriginal.asset");
            var catalog = Own(ScriptableObject.CreateInstance<MapCatalog>()); catalog.Register(baseline);
            Set(runner, "mapCatalog", catalog); Set(runner, "selectedMap", baseline);
            runner.ArenaName = MatchRunner.RegisteredMapsName;
            uint catalogHash = OnlineMapManifest.Compute(catalog.CreateRotation());
            var a = Draft(AssetDatabase.LoadAssetAtPath<MapDefinition>(savedPath)); // No catalog on the authoring object.
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(savedPath), MapAuthoringOperations.SavedTrialGuid(a));
            runner.ConfigureAuthoringPreview(a.definition);
            runner.StartNewMatch();
            Assert.IsTrue(runner.IsAuthoringPreview);
            Assert.AreEqual(1, runner.World.ArenaCount);
            Assert.AreEqual(saved.MatchName, runner.World.Arena.Name);
            Assert.AreEqual(saved.ToArena().ComputeGeometryChecksum(), runner.World.Arena.ComputeGeometryChecksum());
            Assert.AreEqual(MapThemePreset.Sunset, runner.GetMeta(saved.MatchName).MapTheme.preset);
            Assert.AreEqual(saved.ToArena().SpawnFoot0, runner.World.Arena.SpawnFoot0);
            Assert.AreEqual(MatchRunner.RegisteredMapsName, runner.ArenaName);
            Assert.AreSame(baseline, typeof(MatchRunner).GetField("selectedMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner));
            Assert.AreEqual(catalogHash, OnlineMapManifest.Compute(catalog.CreateRotation()));
            Assert.AreEqual(1, catalog.Maps.Count);
            Assert.IsNull(catalog.Find(saved.MatchName));
            runner.StartNewMatch();
            Assert.AreEqual(saved.MatchName, runner.World.Arena.Name, "새 연습도 같은 시험 맵");
        }

        [Test]
        public void SavedTrialRejectsUnsavedPlacementUnloadedDefinitionAndTransientAssets()
        {
            var a = Draft(saved);
            a.terrainRoot.GetChild(0).position += new Vector3(.25f, 0, 0);
            Assert.Throws<InvalidOperationException>(() => MapAuthoringOperations.SavedTrialGuid(a));
            a.definition = null;
            Assert.Throws<InvalidOperationException>(() => MapAuthoringOperations.SavedTrialGuid(a));
            var transient = Own(Object.Instantiate(saved));
            var b = Draft(transient);
            Assert.Throws<InvalidOperationException>(() => MapAuthoringOperations.SavedTrialGuid(b));
            b.definition = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapAuthoringOperations.Folder + "/BrawlOriginal.asset");
            Assert.Throws<InvalidOperationException>(() => MapAuthoringOperations.SavedTrialGuid(b));
        }

        [Test]
        public void PreviewDefinitionWinsOverSameIdInCatalog_WithoutMutatingEither()
        {
            var changed = Own(Object.Instantiate(saved));
            var boxes = Enumerable.Range(0, changed.BoxCount).Select(changed.Box).ToArray();
            boxes[0].minY -= .25f; // Different geometry, intentionally same identity.
            changed.SaveGeometry(boxes, changed.Spawn(0), changed.Spawn(1));
            var catalog = Own(ScriptableObject.CreateInstance<MapCatalog>()); catalog.Register(saved);
            Set(runner, "mapCatalog", catalog);
            runner.ConfigureAuthoringPreview(changed);
            var arena = runner.PrepareMatchArenas(runner.CreateConfig()).Single();
            Assert.AreEqual(changed.ToArena().ComputeGeometryChecksum(), arena.ComputeGeometryChecksum());
            Assert.AreNotEqual(saved.ToArena().ComputeGeometryChecksum(), arena.ComputeGeometryChecksum());
            Assert.AreSame(saved, catalog.Find(saved.MatchName));
        }

        [TestCase(false)] [TestCase(true)]
        public void EditorTrialRestoresPreviousStartSettingAndClearsSession(bool previousStartSet)
        {
            Assert.IsFalse(MapTestReturn.Active, "사용자가 진행 중인 시험은 건드리지 않음");
            var previous = EditorSceneManager.playModeStartScene;
            var expected = previousStartSet ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MapAuthoringOperations.ScenePath) : null;
            try
            {
                EditorSceneManager.playModeStartScene = expected;
                MapTestReturn.Prepare(AssetDatabase.AssetPathToGUID(savedPath));
                Assert.IsTrue(MapTestReturn.Active);
                Assert.AreEqual(MapAuthoringOperations.MainPath, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                Assert.Throws<InvalidOperationException>(() => MapTestReturn.Prepare(AssetDatabase.AssetPathToGUID(savedPath)));
                MapTestReturn.Restore();
                Assert.IsFalse(MapTestReturn.Active);
                Assert.AreSame(expected, EditorSceneManager.playModeStartScene);
                MapTestReturn.Restore(); // Repeated cleanup must not clobber a later setting.
                Assert.AreSame(expected, EditorSceneManager.playModeStartScene);
                Assert.Throws<InvalidOperationException>(() => MapTestReturn.Prepare("missing"));
                Assert.IsFalse(MapTestReturn.Active);
            }
            finally { MapTestReturn.Restore(); EditorSceneManager.playModeStartScene = previous; }
        }

        [Test]
        public void PreviewBlocksOnlineBeforeChangingPracticeState()
        {
            runner.ConfigureAuthoringPreview(saved); runner.Running = true; runner.RandomizeSeed = true;
            var online = root.AddComponent<OnlineMatch>(); Set(online, "runner", runner);
            var lobby = root.AddComponent<OnlineLobby>(); Set(lobby, "runner", runner); Set(lobby, "online", online);
            lobby.CreateRoom(); lobby.Join(); // Must return before any network request.
            Assert.IsFalse(lobby.Busy); StringAssert.Contains("로컬", lobby.StatusMessage);
            MemoryTransport.Pair(out var local, out var peer);
            try { Assert.Throws<InvalidOperationException>(() => online.StartSession(local, true, 3)); }
            finally { local.Dispose(); peer.Dispose(); }
            Assert.IsTrue(runner.Running); Assert.IsTrue(runner.RandomizeSeed);
            Assert.IsFalse(runner.OnlineRematch); Assert.IsFalse(online.Active);
            Assert.Throws<InvalidOperationException>(() => runner.StartPreparedOnlineMatch(runner.CreateConfig(), new[] { saved.ToArena() }, 1));
        }

        [TestCase(false)] [TestCase(true)]
        public void CurrentUnarmedMatch_PreservesControlsAndRoundTripReplayEveryTick(bool online)
        {
            runner.FrontendEnabled = true; runner.OnlineRematch = online;
            var config = runner.CreateConfig();
            Assert.IsFalse(config.Combat.Weapons.Enabled); Assert.IsFalse(config.Combat.Weapons.ThrowEnabled);
            Assert.IsTrue(config.Combat.Body.CombinedAttackInput); Assert.IsFalse(config.Combat.Body.SeparateChargeInput);
            Assert.AreEqual(12, config.Combat.Body.MinLungeChargeTicks);
            var arenas = new[] { saved.ToArena() };
            var initial = SimState.CreateMatch(config, arenas, 77, MatchPhase.Fighting);
            var world = new SimWorld(config, arenas, initial);
            var sync = new SyncTest<SimWorld>(world);
            var rec = InputRecording.Begin(config, arenas, initial.Match.Rotation, initial.Rng.State,
                initial.Fighter0.Position, initial.Fighter1.Position, initial.Match.Phase, config.Rules.Format());
            var hashes = new List<uint>();
            int ticks = BrawlWeaponParams.Default.SpawnDelayTicks + BrawlWeaponParams.Default.WarningTicks + BrawlWeaponParams.Default.FallTicks + 180;
            for (int t = 0; t < ticks; t++)
            {
                var input = new InputFrame(0, 0, t % 60 < 30 ? InputButtons.Attack : InputButtons.None, 1);
                rec.Append(input, default);
                Assert.IsTrue(sync.Advance(input, default));
                Assert.IsFalse(world.State.Weapon.Enabled);
                Assert.Zero(world.State.Projectiles.CountActive(0) + world.State.Projectiles.CountActive(1));
                hashes.Add(world.ComputeChecksum());
            }
            rec.Finish(world.ComputeChecksum());
            var restored = InputRecording.FromBytes(rec.ToBytes());
            var found = runner.FindRecordedConfig(restored);
            Assert.IsNotNull(found); Assert.AreEqual(config.ComputeChecksum(), found.ComputeChecksum());
            var replay = new SimWorld(found, arenas, restored.CreateInitialState(found, arenas));
            for (int t = 0; t < restored.Count; t++)
            {
                replay.Step(restored.GetP1(t), restored.GetP2(t));
                Assert.AreEqual(hashes[t], replay.ComputeChecksum(), "재생 틱 " + t);
            }
            Assert.AreEqual(restored.FinalChecksum, replay.ComputeChecksum());
        }
    }
}