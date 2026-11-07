using UnityEngine.TestTools;
#if UNITY_EDITOR
using System;
using System.IO;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#endif

namespace Knockbox.Tests.PlayMode
{
    /// <summary>Self-contained persistence fixtures. Copies Main; never saves over the team's scene/assets.</summary>
    public sealed class AuthoredMainProbeSetup : IPrebuildSetup
    {
        public const string UIKey="Knockbox.Tests.AuthoredUIProbe", MapKey="Knockbox.Tests.AuthoredMapProbe", HashKey="Knockbox.Tests.AuthoredMapHash";
        public void Setup()
        {
#if UNITY_EDITOR
            string folder="Assets/_Project/LocalOnly/MaintenanceValidation/Play-"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var active=SceneManager.GetActiveScene();
            string ui=folder+"/MainUIProbe.unity";
            CopyMain(ui);var scene=EditorSceneManager.OpenScene(ui,OpenSceneMode.Additive);
            try
            {
                var view=Find<FrontendController>(scene).View;var logo=view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby/Menu/Logo")).GetComponent<Text>();
                logo.rectTransform.anchoredPosition+=new Vector2(37,-19);logo.rectTransform.sizeDelta+=new Vector2(22,11);
                logo.fontSize=91;logo.color=view.Theme.selectedLeft;
                logo.rectTransform.localScale=Vector3.one*1.1f;logo.rectTransform.localRotation=Quaternion.Euler(0,0,2);
                PrefabUtility.RecordPrefabInstancePropertyModifications(logo);PrefabUtility.RecordPrefabInstancePropertyModifications(logo.rectTransform);
                EditorSceneManager.SaveScene(scene);
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
            var source=AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/BrawlTrial.asset");
            var map=UnityEngine.Object.Instantiate(source);map.MakeEditableCopy("probe-"+Guid.NewGuid().ToString("N"),"Edited saved map probe");
            map.Metadata().MapTheme.mode=Knockbox.Presentation.MapThemeMode.Preset;
            map.Metadata().MapTheme.preset=Knockbox.Presentation.MapThemePreset.Violet;
            var boxes=new MapBox[map.BoxCount];for(int i=0;i<boxes.Length;i++)boxes[i]=map.Box(i);
            boxes[1].minX+=.125f;boxes[1].maxX+=.125f;boxes[1].minY+=.25f;boxes[1].maxY+=.25f;
            map.SaveGeometry(boxes,map.Spawn(0),map.Spawn(1));AssetDatabase.CreateAsset(map,folder+"/Map.asset");
            var catalog=ScriptableObject.CreateInstance<MapCatalog>();catalog.Register(map);AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
            string mapScene=folder+"/MainMapProbe.unity";CopyMain(mapScene);scene=EditorSceneManager.OpenScene(mapScene,OpenSceneMode.Additive);
            try
            {
                var so=new SerializedObject(Find<MatchRunner>(scene));so.FindProperty("mapCatalog").objectReferenceValue=catalog;
                so.FindProperty("selectedMap").objectReferenceValue=map;so.ApplyModifiedPropertiesWithoutUndo();EditorSceneManager.SaveScene(scene);
                SessionState.SetString(HashKey,map.ToArena().ComputeChecksum().ToString("X8"));
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
            if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);
            SessionState.SetString(UIKey,ui);SessionState.SetString(MapKey,mapScene);AssetDatabase.SaveAssets();
#endif
        }
#if UNITY_EDITOR
        static T Find<T>(Scene scene) where T:Component
        {foreach(var root in scene.GetRootGameObjects()){var c=root.GetComponentInChildren<T>(true);if(c!=null)return c;}throw new InvalidOperationException("Missing "+typeof(T).Name);}
        static void CopyMain(string path)
        {File.Copy("Assets/_Project/Scenes/Main.unity",path,false);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);}
#endif
    }
}
