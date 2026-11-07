using Knockbox.Game;
using Knockbox.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>CR-004 단계 S(8절): 캐릭터 외형 샘플 씬을 코드로 만든다(카메라 + CharacterSampleScene). 다시 실행하면 덮어쓴다.</summary>
    public static class CharacterSampleMenu
    {
        public const string ScenePath = ProjectSetup.ScenesFolder + "/CharacterSample.unity";

        [MenuItem("Knockbox/Setup/Create Character Sample Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var palette = AssetDatabase.LoadAssetAtPath<PaletteAsset>(ProjectSetup.PalettePath);

            var camera = new GameObject("Main_Camera");
            camera.tag = "MainCamera";
            var cam = camera.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            if (palette != null)
                cam.backgroundColor = palette.bgBase;
            camera.transform.position = new Vector3(-0.5f, 0.3f, -10f);

            var sample = new GameObject("Character_Sample").AddComponent<CharacterSampleScene>();
            var so = new SerializedObject(sample);
            so.FindProperty("palette").objectReferenceValue = palette;
            so.FindProperty("shapes").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ShapeLibrary>(SpriteFactory.LibraryPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Knockbox] 외형 샘플 씬: {ScenePath}");
        }
    }
}
