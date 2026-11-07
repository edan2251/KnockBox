using System;
using System.IO;
using System.Linq;
using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Knockbox.Tests.EditMode
{
    public class MapAuthoringTests
    {
        const string Baseline=MapAuthoringOperations.Folder+"/BrawlOriginal.asset";
        const string Trial=MapAuthoringOperations.Folder+"/BrawlTrial.asset";
        static string Scratch()
        {
            string p="Assets/_Project/LocalOnly/MaintenanceValidation/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
            MaintenanceSceneMigration.EnsureFolder(p);return p;
        }
        static MapAuthoring IsolatedDraft(MapDefinition definition,PaletteAsset palette)
        {
            var a=new GameObject("Map_Authoring_Review_Probe").AddComponent<MapAuthoring>();
            a.definition=definition;a.palette=palette;
            a.terrainRoot=new GameObject("Terrain").transform;a.terrainRoot.SetParent(a.transform,false);
            a.spawn0=new GameObject("P1").transform;a.spawn0.SetParent(a.transform,false);
            a.spawn1=new GameObject("P2").transform;a.spawn1.SetParent(a.transform,false);
            a.spawn0.position=definition.Spawn(0);a.spawn1.position=definition.Spawn(1);a.MarkLoaded();
            for(int i=0;i<definition.BoxCount;i++)
            {
                var go=new GameObject(i.ToString());go.transform.SetParent(a.terrainRoot,false);
                go.AddComponent<SpriteRenderer>();go.AddComponent<MapBoxHandle>().Initialize(definition.Box(i),i);
            }
            return a;
        }
        [Test]
        public void DuplicateDraftPreservesUnsavedGeometrySpawnsThemeAndExistingObjects()
        {
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var source=Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(Baseline));
            var palette=ScriptableObject.CreateInstance<PaletteAsset>();MapDefinition copy=null;
            try
            {
                var a=IsolatedDraft(source,palette);var original=source.ToArena();
                var handle=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>().First(h=>h.Order==1);
                int instance=handle.GetInstanceID();handle.transform.localPosition+=new Vector3(.125f,.25f,0);
                a.spawn0.position+=new Vector3(.125f,0,0);
                source.Metadata().MapTheme.mode=MapThemeMode.Preset;source.Metadata().MapTheme.preset=MapThemePreset.Sunset;
                var expected=a.ReadOrderedBoxes();var position=handle.transform.localPosition;
                copy=MapAuthoringOperations.DuplicateDraft(a,"Unsaved_Copy");
                Assert.IsEmpty(copy.Validate());Assert.AreNotEqual(source.Id,copy.Id);Assert.IsFalse(copy.PreservedBaseline);
                Assert.AreEqual(expected.Length,copy.BoxCount);
                for(int i=0;i<expected.Length;i++)Assert.AreEqual(expected[i],copy.Box(i),"draft box "+i);
                Assert.AreEqual((Vector2)a.spawn0.position,copy.Spawn(0));
                Assert.AreEqual(source.Metadata().ResolveTheme(palette),copy.Metadata().ResolveTheme(palette));
                Assert.AreEqual(original.ComputeGeometryChecksum(),source.ToArena().ComputeGeometryChecksum());
                Assert.AreSame(source,a.definition);Assert.AreEqual(instance,handle.GetInstanceID());
                Assert.AreEqual(position,handle.transform.localPosition);
                a.spawn0.position=new Vector3(200,200,0);
                Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.DuplicateDraft(a,"Invalid_Copy"));
                Assert.AreEqual(new Vector3(200,200,0),a.spawn0.position);
                Assert.AreSame(source,a.definition);Assert.AreEqual(instance,handle.GetInstanceID());
            }
            finally
            {
                if(copy!=null)Object.DestroyImmediate(copy);Object.DestroyImmediate(source);Object.DestroyImmediate(palette);
                EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid()&&previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
        }
        [Test]
        public void ThemePreviewRefreshUsesCurrentSettingsWithoutChangingDraftOrOtherRenderers()
        {
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var source=Object.Instantiate(AssetDatabase.LoadAssetAtPath<MapDefinition>(Baseline));
            var palette=ScriptableObject.CreateInstance<PaletteAsset>();
            try
            {
                var a=IsolatedDraft(source,palette);var handle=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>()[0];
                var before=a.ReadOrderedBoxes();uint loaded=a.LoadedGeometry;var renderer=handle.GetComponent<SpriteRenderer>();
                var decoration=new GameObject("Decoration").AddComponent<SpriteRenderer>();decoration.transform.SetParent(handle.transform,false);
                decoration.color=palette.p1Body;
                var theme=source.Metadata().MapTheme;theme.mode=MapThemeMode.Custom;theme.terrain=palette.p1Shade;
                Assert.IsTrue(MapAuthoringOperations.RefreshPreviewColors(a));Assert.AreEqual(palette.p1Shade,renderer.color);
                Assert.IsFalse(MapAuthoringOperations.RefreshPreviewColors(a));
                theme.terrain=palette.p2Shade;Assert.IsTrue(MapAuthoringOperations.RefreshPreviewColors(a));
                Assert.AreEqual(palette.p2Shade,renderer.color);
                // This is the state restored by Undo: refresh must not keep its last color cached.
                theme.terrain=palette.p1Shade;Assert.IsTrue(MapAuthoringOperations.RefreshPreviewColors(a));
                Assert.AreEqual(palette.p1Shade,renderer.color);Assert.AreEqual(palette.p1Body,decoration.color);
                Assert.AreEqual(loaded,a.LoadedGeometry);CollectionAssert.AreEqual(before,a.ReadOrderedBoxes());
            }
            finally
            {
                Object.DestroyImmediate(source);Object.DestroyImmediate(palette);EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid()&&previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
        }
        [Test]
        public void ImportedBrawlPreservesEveryOrderedBoxSpawnAndBothHashes()
        {
            var source=ArenaFiles.LoadByName("Brawl");
            foreach(string path in new[]{Baseline,Trial})
            {
                var definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);Assert.NotNull(definition);
                var actual=definition.ToArena();Assert.AreEqual(source.ComputeChecksum(),actual.ComputeChecksum());
                Assert.AreEqual(source.ComputeGeometryChecksum(),actual.ComputeGeometryChecksum());
                Assert.AreEqual(source.BoxCount,actual.BoxCount);Assert.AreEqual(source.SpawnFoot0,actual.SpawnFoot0);Assert.AreEqual(source.SpawnFoot1,actual.SpawnFoot1);
                for(int i=0;i<source.BoxCount;i++)Assert.AreEqual(source.GetBox(i),actual.GetBox(i),"ordered box "+i);
            }
        }
        [Test]
        public void UnchangedAuthoringRoundTrip_IsExact_AndOrderDoesNotFollowHierarchy()
        {
            EditorSceneManager.OpenScene(MapAuthoringOperations.ScenePath,OpenSceneMode.Single);
            var a=Object.FindFirstObjectByType<MapAuthoring>();Assert.NotNull(a);
            var before=a.definition.ToArena();var handles=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>();
            handles[0].transform.SetAsLastSibling();
            var draft=MapAuthoringOperations.Draft(a);
            try
            {
                Assert.AreEqual(before.ComputeChecksum(),draft.ToArena().ComputeChecksum());
                Assert.AreEqual(before.ComputeGeometryChecksum(),draft.ToArena().ComputeGeometryChecksum());
                Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
                for(int i=0;i<before.BoxCount;i++)Assert.AreEqual(before.GetBox(i),draft.ToArena().GetBox(i));
            }
            finally{Object.DestroyImmediate(draft);EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);}
        }
        [Test]
        public void EditedCopy_SaveReopenRegisterAndRunnerLoad_UsesSameDefinition()
        {
            EditorSceneManager.OpenScene(MapAuthoringOperations.ScenePath,OpenSceneMode.Single);
            var a=Object.FindFirstObjectByType<MapAuthoring>();string folder=Scratch();
            var copy=Object.Instantiate(a.definition);copy.MakeEditableCopy("test-"+Guid.NewGuid().ToString("N"),"Persistence test");
            AssetDatabase.CreateAsset(copy,folder+"/Map.asset");var catalog=ScriptableObject.CreateInstance<MapCatalog>();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
            a.definition=copy;a.catalog=catalog;MapAuthoringOperations.Open(a);
            a.terrainRoot.GetComponentsInChildren<MapBoxHandle>().First(h=>h.Order==1).transform.localPosition+=new Vector3(.125f,.25f,0);
            Assert.IsTrue(MapAuthoringOperations.HasUnsavedPlacement(a));
            Assert.Throws<InvalidOperationException>(()=>MapAuthoringOperations.Register(a));
            MapAuthoringOperations.Save(a);MapAuthoringOperations.Register(a);
            uint expected=a.definition.ToArena().ComputeChecksum();string mapName=a.definition.MatchName;
            EditorSceneManager.SaveScene(a.gameObject.scene,folder+"/AuthoringProbe.unity");
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            EditorSceneManager.OpenScene(folder+"/AuthoringProbe.unity",OpenSceneMode.Single);
            a=Object.FindFirstObjectByType<MapAuthoring>();Assert.IsFalse(MapAuthoringOperations.HasUnsavedPlacement(a));
            Assert.AreEqual(expected,a.definition.ToArena().ComputeChecksum());Assert.AreSame(a.definition,a.catalog.Find(mapName));
            var go=new GameObject("Definition runner probe");go.SetActive(false);var runner=go.AddComponent<MatchRunner>();
            MaintenanceSceneMigration.Set(runner,"mapCatalog",a.catalog);runner.BrawlExperiment=true;runner.Rules=RuleSet.Brawl;runner.ArenaName=mapName;runner.StartNewMatch();
            Assert.AreEqual(expected,runner.World.Arena.ComputeChecksum());
            Object.DestroyImmediate(go);EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
        }
        [Test]
        public void AuthoringRejectsRotationDuplicateOrderInvalidSpawnAndProtectedOverwrite()
        {
            EditorSceneManager.OpenScene(MapAuthoringOperations.ScenePath,OpenSceneMode.Single);var a=Object.FindFirstObjectByType<MapAuthoring>();
            var handle=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>()[0];handle.transform.localRotation=Quaternion.Euler(0,0,5);
            Assert.That(a.ValidatePlacement().Any(e=>e.Contains("회전")));handle.transform.localRotation=Quaternion.identity;
            var duplicate=Object.Instantiate(handle.gameObject,a.terrainRoot);Assert.That(a.ValidatePlacement().Any(e=>e.Contains("순서")));Object.DestroyImmediate(duplicate);
            var unknown=new GameObject("Unregistered decorative cube");unknown.transform.SetParent(a.terrainRoot,false);
            Assert.That(a.ValidatePlacement().Any(e=>e.Contains("등록되지 않은 지형")));Object.DestroyImmediate(unknown);
            a.spawn0.position=new Vector3(200,200,0);var draft=MapAuthoringOperations.Draft(a);Assert.That(draft.Validate().Any(e=>e.Contains("시작 위치")));Object.DestroyImmediate(draft);
            var original=AssetDatabase.LoadAssetAtPath<MapDefinition>(Baseline);Assert.Throws<InvalidOperationException>(()=>original.SaveGeometry(new MapBox[0],default,default));
            var catalog=ScriptableObject.CreateInstance<MapCatalog>();var clone=Object.Instantiate(original);
            catalog.Register(original);Assert.Throws<InvalidOperationException>(()=>catalog.Register(clone));Object.DestroyImmediate(clone);Object.DestroyImmediate(catalog);
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
        }
        [Test]
        public void GeneratedMainTerrain_RejectsAccidentalVisualOnlyTransformChanges()
        {
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var presenter=Object.FindFirstObjectByType<MatchPresenter>();var view=Object.FindFirstObjectByType<ArenaView>();
            var box=view.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Terrain_box_1_Solid");
            var renderer=box.GetComponentInChildren<SpriteRenderer>();var expected=renderer.bounds;
            view.CaptureDefinitionVisuals();box.localPosition+=new Vector3(3,2,0);box.localScale*=2;box.localRotation=Quaternion.Euler(0,0,15);
            var definition=ArenaFiles.LoadByName("Brawl");
            view.Build(definition,presenter.Palette.Theme(2),presenter.Palette,presenter.Shapes,true);
            Assert.AreEqual(expected.min,renderer.bounds.min);Assert.AreEqual(expected.max,renderer.bounds.max);
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
        }
        [Test]
        public void MainHasSerializedOwnersAndSameNestedUIPrefabs_NoMissingScripts()
        {
            var scene=EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var presenter=Object.FindFirstObjectByType<MatchPresenter>();Assert.True(presenter.AuthoredOwners);
            var controller=Object.FindFirstObjectByType<FrontendController>();Assert.NotNull(controller.View);Assert.True(controller.View.IsAuthored);
            Assert.NotNull(controller.View.Canvas);Assert.NotNull(controller.View.Theme);Assert.NotNull(controller.View.Points.Root);
            Assert.True(PrefabUtility.IsPartOfPrefabInstance(controller.View));
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))Assert.Zero(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),t.name);
            var p=new SerializedObject(presenter);
            foreach(string field in new[]{"runner","background","arenaView","fighter0","fighter1","cameraRig","hud","sfxPlayer","brawlWeaponView","projectileView","koShatter","postFx"})
                Assert.NotNull(p.FindProperty(field).objectReferenceValue,field);
            var runner=presenter.GetComponent<MatchRunner>();Assert.AreEqual(0x483E0206u,runner.ResolveArena("Brawl").ComputeChecksum());
            Assert.AreEqual(0x483E0206u,runner.ResolveRecordedArena("Brawl").ComputeChecksum());
        }
        [Test]
        public void SaveReopenedUIProbe_RecordsActualSceneOverridesForRuntimeTest()
        {
            var scene=EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var view=Object.FindFirstObjectByType<FrontendController>().View;
            var logo=view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")).GetComponent<Text>();
            logo.rectTransform.anchoredPosition+=new Vector2(37,-19);logo.rectTransform.sizeDelta+=new Vector2(22,11);
            logo.fontSize=91;logo.color=view.Theme.selectedLeft;
            PrefabUtility.RecordPrefabInstancePropertyModifications(logo);PrefabUtility.RecordPrefabInstancePropertyModifications(logo.rectTransform);
            string path=Scratch()+"/MainUIProbe.unity";EditorSceneManager.SaveScene(scene,path);
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var saved=Object.FindFirstObjectByType<FrontendController>().View.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")).GetComponent<Text>();
            Assert.AreEqual(91,saved.fontSize);Assert.AreEqual(new Vector2(37,-19),saved.rectTransform.anchoredPosition);
            string evidence=Environment.GetEnvironmentVariable("KNOCKBOX_BASELINE_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence))File.WriteAllText(Path.Combine(evidence,"ui-probe-scene.txt"),path);
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
        }
    }
}
