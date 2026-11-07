using System.Collections.Generic;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// CP0 골격 설정을 코드로 재현한다. 메뉴는 여러 번 실행해도 결과가 같다.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string MainScenePath = ScenesFolder + "/Main.unity";
        public const string SandboxScenePath = ScenesFolder + "/Sandbox.unity";
        const string UrpAssetPath = "Assets/Settings/UniversalRP.asset";

        // 05-presentation.md: 직교 크기 9(세로 18 유닛), 배경은 팔레트의 bgBase.
        const float CameraOrthoSize = 9f;

        public const string TuningFolder = "Assets/_Project/Data/Tuning";
        public const string MovementTuningPath = TuningFolder + "/MovementTuning.asset";
        public const string CombatTuningPath = TuningFolder + "/CombatTuning.asset";
        public const string CombatTuningFastPath = TuningFolder + "/CombatTuningFast.asset";
        public const string ArtFolder = "Assets/_Project/Art";
        public const string PalettePath = ArtFolder + "/Palette.asset";
        public const string ArenasFolder = "Assets/_Project/Data/Arenas";
        public const string SfxFolder = "Assets/_Project/Audio/Sfx";
        public const string SfxLibraryPath = "Assets/_Project/Audio/SfxLibrary.asset";
        public const string MatchObjectName = "Match";
        public const string ScenarioObjectName = "Scenario";

        [MenuItem("Knockbox/Setup/Run All")]
        public static void RunAll()
        {
            ApplyProjectSettings();
            CreateMissingTuningAssets();
            if (AssetDatabase.LoadAssetAtPath<ShapeLibrary>(SpriteFactory.LibraryPath) == null)
                SpriteFactory.Generate();
            else
                SpriteFactory.EnsureSortingLayers();
            EnsureSfxLibrary();
            CreateMissingScenes();
            EnsureSceneObjects();
        }

        [MenuItem("Knockbox/Setup/Create Missing Tuning Assets")]
        public static void CreateMissingTuningAssets()
        {
            CreateAssetIfMissing<MovementTuning>(MovementTuningPath);
            CreateAssetIfMissing<CombatTuning>(CombatTuningPath);
            if (AssetDatabase.LoadAssetAtPath<CombatTuning>(CombatTuningFastPath) == null)
            {
                var fast = ScriptableObject.CreateInstance<CombatTuning>();
                fast.PresetName = "fast";
                fast.Combat = CombatPresets.Fast;
                AssetDatabase.CreateAsset(fast, CombatTuningFastPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Knockbox] 에셋 생성: {CombatTuningFastPath}");
            }
            CreateAssetIfMissing<PaletteAsset>(PalettePath);
            UpgradePalette();
        }

        /// <summary>
        /// 팔레트 토큰 구성이 바뀌면(CR-001: 08-visual-direction.md) 기존 에셋을 새 기본값으로 덮는다. GUID는 그대로라 씬 참조가 유지된다.
        /// </summary>
        [MenuItem("Knockbox/Setup/Upgrade Palette")]
        public static void UpgradePalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<PaletteAsset>(PalettePath);
            if (palette == null || palette.version >= PaletteAsset.CurrentVersion)
                return;
            var fresh = ScriptableObject.CreateInstance<PaletteAsset>();
            EditorUtility.CopySerialized(fresh, palette);
            Object.DestroyImmediate(fresh);
            palette.version = PaletteAsset.CurrentVersion;
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Knockbox] 팔레트를 버전 {PaletteAsset.CurrentVersion}으로 바꿈: {PalettePath}");
        }

        static void CreateAssetIfMissing<T>(string path) where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
                return;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Knockbox] 에셋 생성: {path}");
        }

        /// <summary>SfxLibrary 에셋을 만들고 Tools/sfx_synth.py가 만든 WAV를 이름으로 연결한다.</summary>
        [MenuItem("Knockbox/Setup/Ensure Sfx Library")]
        public static void EnsureSfxLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(SfxLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SfxLibrary>();
                AssetDatabase.CreateAsset(library, SfxLibraryPath);
            }
            var so = new SerializedObject(library);
            bool changed = false;
            var field = so.GetIterator();
            field.NextVisible(true);
            while (field.NextVisible(false))
            {
                if (field.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxFolder}/{field.name}.wav");
                if (clip != null && field.objectReferenceValue != clip)
                {
                    field.objectReferenceValue = clip;
                    changed = true;
                }
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>Data/Arenas의 맵 텍스트(.txt) 또는 짝 파일(.json)을 이름순으로.</summary>
        static TextAsset[] LoadArenaFiles(string extension = ".txt")
        {
            var list = new List<TextAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { ArenasFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(extension))
                    list.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }

        /// <summary>
        /// Main에는 MatchRunner(자동 시작), Sandbox에는 MatchRunner(수동 시작)와 ScenarioPlayer를 둔다.
        /// 오브젝트가 이미 있으면 빠진 컴포넌트와 비어 있는 참조만 채운다. 끝나면 Main 씬을 연다.
        /// </summary>
        [MenuItem("Knockbox/Setup/Ensure Scene Objects")]
        public static void EnsureSceneObjects()
        {
            if (AnyOpenSceneDirty())
                return;
            EnsureSceneObjects(SandboxScenePath, startOnAwake: false, withScenarioPlayer: true, localPlay: false);
            EnsureSceneObjects(MainScenePath, startOnAwake: true, withScenarioPlayer: false, localPlay: true);
        }

        static void EnsureSceneObjects(string scenePath, bool startOnAwake, bool withScenarioPlayer, bool localPlay)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<MatchPresenter>() is { AuthoredOwners: true })
            {
                Debug.Log("[Knockbox] Authored scene keeps its Inspector values and references: " + scenePath);
                return;
            }
            bool changed = false;

            var runner = Object.FindFirstObjectByType<MatchRunner>();
            if (runner == null)
            {
                runner = new GameObject(MatchObjectName).AddComponent<MatchRunner>();
                var so = new SerializedObject(runner);
                so.FindProperty("movementTuning").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MovementTuning>(MovementTuningPath);
                so.FindProperty("startOnAwake").boolValue = startOnAwake;
                so.FindProperty("useLocalDevices").boolValue = localPlay;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
            var runnerFlags = new SerializedObject(runner);
            if (runnerFlags.FindProperty("useLocalDevices").boolValue != localPlay)
            {
                runnerFlags.FindProperty("useLocalDevices").boolValue = localPlay;
                runnerFlags.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
            // 직접 플레이하는 Main 씬은 시험 공격 폼 대신 가위 폼으로 시작한다(CR-003).
            var formProperty = runnerFlags.FindProperty("rules.StartForm");
            if (localPlay && formProperty.enumValueIndex == (int)FighterForm.Test)
            {
                formProperty.enumValueIndex = (int)FighterForm.Scissors;
                runnerFlags.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            var runnerSo = new SerializedObject(runner);
            changed |= FillArray(runnerSo, "arenaFiles", LoadArenaFiles(".txt"));
            changed |= FillArray(runnerSo, "arenaMetaFiles", LoadArenaFiles(".json"));
            // 직접 플레이하는 Main 씬은 CR-004 실험 경기(Brawl 맵 고정, P2 샌드백). 맵·규칙은 MatchRunner.Awake가 정한다.
            if (localPlay)
            {
                var brawl = runnerSo.FindProperty("brawlExperiment");
                if (!brawl.boolValue)
                {
                    brawl.boolValue = true;
                    runnerSo.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            changed |= FillReference(runner, "combatTuning", AssetDatabase.LoadAssetAtPath<CombatTuning>(CombatTuningPath));
            var presets = new SerializedObject(runner);
            var presetList = presets.FindProperty("combatPresets");
            if (presetList.arraySize != 2)
            {
                presetList.arraySize = 2;
                presetList.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatTuning>(CombatTuningPath);
                presetList.GetArrayElementAtIndex(1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatTuning>(CombatTuningFastPath);
                presets.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            var presenter = runner.GetComponent<MatchPresenter>();
            if (presenter == null)
            {
                presenter = runner.gameObject.AddComponent<MatchPresenter>();
                changed = true;
            }
            changed |= FillReference(presenter, "palette", AssetDatabase.LoadAssetAtPath<PaletteAsset>(PalettePath));
            changed |= FillReference(presenter, "shapes", AssetDatabase.LoadAssetAtPath<ShapeLibrary>(SpriteFactory.LibraryPath));
            changed |= FillReference(presenter, "sfx", AssetDatabase.LoadAssetAtPath<SfxLibrary>(SfxLibraryPath));

            if (localPlay && runner.GetComponent<DebugHotkeys>() == null)
            {
                runner.gameObject.AddComponent<DebugHotkeys>();
                changed = true;
            }
            var mainCamera = Object.FindFirstObjectByType<Camera>();
            if (mainCamera != null && mainCamera.GetComponent<CameraRig>() == null)
            {
                mainCamera.gameObject.AddComponent<CameraRig>();
                changed = true;
            }

            if (withScenarioPlayer && Object.FindFirstObjectByType<ScenarioPlayer>() == null)
            {
                var player = new GameObject(ScenarioObjectName).AddComponent<ScenarioPlayer>();
                var so = new SerializedObject(player);
                so.FindProperty("runner").objectReferenceValue = runner;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[Knockbox] 씬 오브젝트 추가: {scenePath}");
            }
        }

        /// <summary>배열 속성을 목록과 똑같이 맞춘다. 바꿨으면 true.</summary>
        static bool FillArray(SerializedObject so, string property, Object[] values)
        {
            var array = so.FindProperty(property);
            bool differ = array.arraySize != values.Length;
            for (int i = 0; !differ && i < values.Length; i++)
                differ = array.GetArrayElementAtIndex(i).objectReferenceValue != values[i];
            if (!differ)
                return false;
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>비어 있는 참조만 채운다. 바꿨으면 true.</summary>
        static bool FillReference(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop.objectReferenceValue != null || value == null)
                return false;
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static bool AnyOpenSceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogError($"[Knockbox] 저장하지 않은 씬이 열려 있어 멈춤: {open.name}");
                    return true;
                }
            }
            return false;
        }

        [MenuItem("Knockbox/Setup/Apply Project Settings")]
        public static void ApplyProjectSettings()
        {
            // 에디터가 포커스를 잃어도 플레이 모드 검증이 멈추지 않게 한다.
            PlayerSettings.runInBackground = true;

            // 품질 단계별 지정이 비어도 URP로 떨어지게 기본 파이프라인도 지정한다.
            var urp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                Debug.LogError($"[Knockbox] URP 에셋을 찾을 수 없음: {UrpAssetPath}");
                return;
            }
            if (GraphicsSettings.defaultRenderPipeline != urp)
                GraphicsSettings.defaultRenderPipeline = urp;

            AssetDatabase.SaveAssets();
            Debug.Log("[Knockbox] 프로젝트 설정 적용: runInBackground=true, defaultRenderPipeline=UniversalRP");
        }

        /// <summary>
        /// 없는 씬만 만든다. 이미 있는 씬은 건드리지 않는다(사람이 편집했을 수 있으므로).
        /// 다른 씬의 조명과 섞이지 않게 단일 모드로 만들기 때문에, 저장 안 된 씬이 열려 있으면 멈춘다.
        /// 끝나면 Main 씬을 연다.
        /// </summary>
        [MenuItem("Knockbox/Setup/Create Missing Scenes")]
        public static void CreateMissingScenes()
        {
            if (AnyOpenSceneDirty())
                return;

            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");

            CreateSceneIfMissing(MainScenePath);
            CreateSceneIfMissing(SandboxScenePath);
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        static void CreateSceneIfMissing(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();
            CreateGlobalLight();
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Knockbox] 씬 생성: {path}");
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main_Camera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0f, -10f);

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var palette = AssetDatabase.LoadAssetAtPath<PaletteAsset>(PalettePath);
            cam.backgroundColor = palette != null ? palette.bgBase : ScriptableObject.CreateInstance<PaletteAsset>().bgBase;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;

            go.AddComponent<UniversalAdditionalCameraData>();
            go.AddComponent<AudioListener>();
        }

        static void CreateGlobalLight()
        {
            var go = new GameObject("Global_Light_2D");
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
        }

        /// <summary>빌드 씬 목록을 Main, Sandbox 순서로 맞춘다. 다른 씬은 목록에서만 뺀다(파일은 그대로).</summary>
        static void RegisterBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainScenePath, true),
                new EditorBuildSettingsScene(SandboxScenePath, true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
