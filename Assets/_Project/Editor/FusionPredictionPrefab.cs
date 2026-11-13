using System.IO;
using Fusion;
using Fusion.Editor;
using Knockbox.Game.Online;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>Creates only the SDK combat-state prefab. Never regenerates Main or its authored UI.</summary>
    public static class FusionPredictionPrefab
    {
        public const string Path = "Assets/_Project/Resources/FusionPredictionWorld.prefab";

        public static void Ensure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
            {
                if (File.Exists(Path)) throw new System.InvalidOperationException("예측 프리팹을 읽을 수 없습니다.");
                var go = new GameObject("FusionPredictionWorld");
                try
                {
                    go.AddComponent<NetworkObject>();
                    go.AddComponent<FusionPredictionWorld>();
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, Path);
                    // Baking needs the persistent prefab GUID; an unsaved temporary object has no sort key.
                    new NetworkObjectBakerEditTime().Bake(prefab);
                    AssetDatabase.SetLabels(prefab, new[] { NetworkProjectConfigImporter.FusionPrefabTag });
                }
                finally { Object.DestroyImmediate(go); }
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
                NetworkProjectConfigImporter.RebuildPrefabHash();
                AssetDatabase.Refresh();
                NetworkProjectConfig.UnloadGlobal();
            }
            if (prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<FusionPredictionWorld>() == null)
                throw new System.InvalidOperationException("예측 프리팹 구성 요소가 없습니다.");
            FusionWorldSnapshot.ValidateLayout();
            Debug.Log($"[Knockbox][prediction] 프리팹 준비: 상태 {FusionWorldSnapshot.ActualByteCount}바이트");
        }
    }
}
