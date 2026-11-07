using System.IO;
using System.Linq;
using Knockbox.Presentation;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// CR-004 3D 시험 외형: 로컬 전용 폴더의 모델(FBX)로 외형 프리팹과 설정을 만든다(에디터 메뉴).
    /// 가져오기 설정(Generic 리그, 애니메이션·재질 가져오지 않음), 무광 재질, 크기·발 위치 맞춤, 앞쪽 방향 측정,
    /// 뼈 연결(이름으로 찾음, 못 찾으면 비워 두고 Inspector에서 직접 연결)을 한 번에 한다. 뼈 이름은 이 메뉴와 프리팹에만 있고 전투 코드에는 없다.
    /// 다른 모델은 ModelFolder의 파일과 아래 이름 후보만 바꾸면 된다.
    /// </summary>
    public static class TestFighterModelBuilder
    {
        const string ModelFolder = "Assets/_Project/LocalOnly/FighterModels/GangBeastTest";
        const string ModelPath = ModelFolder + "/GangBeastTest.fbx";
        const string TexturePath = ModelFolder + "/GangBeastTest_BaseColor.png";
        const string MaterialPath = ModelFolder + "/GangBeastTest.mat";
        const string PrefabPath = ModelFolder + "/GangBeastTest_Fighter.prefab";
        /// <summary>판정 상자(0.72)에 비해 보이는 키. 2D 인형처럼 머리가 판정 위로 조금 나온다.</summary>
        const float TargetHeight = 1.15f;

        [MenuItem("Knockbox/Setup/Build Test Fighter Model (Local)")]
        public static void Build()
        {
            if (!File.Exists(ModelPath))
            {
                Debug.LogError($"[Knockbox] 시험 모델이 없음: {ModelPath}");
                return;
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();

            var shader = Shader.Find("Knockbox/ModelUnlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            EditorUtility.SetDirty(material);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("GangBeastTest_Fighter");
            var rig = root.AddComponent<FighterModelRig>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);
            rig.model = model.transform;

            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var r in renderers)
            {
                r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                r.sortingLayerName = SortingLayers.Fighter;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            // 크기: 보이는 키를 TargetHeight로, 발바닥이 루트 높이 0에, 가운데가 x·z 0에.
            var bounds = Bounds(renderers);
            float scale = TargetHeight / Mathf.Max(bounds.size.y, 1e-4f);
            model.transform.localScale *= scale;
            bounds = Bounds(renderers);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            Transform Find(params string[] names)
            {
                foreach (string n in names)
                {
                    var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith(n));
                    if (t != null)
                        return t;
                }
                Debug.LogWarning($"[Knockbox] 뼈를 찾지 못함: {string.Join("/", names)} (Inspector에서 직접 연결)");
                return null;
            }
            rig.chest = Find("Spine2", "Chest");
            rig.leftUpperArm = Find("LeftArm", "LeftUpperArm");
            rig.leftLowerArm = Find("LeftForeArm", "LeftLowerArm");
            rig.leftHand = Find("LeftHand");
            rig.rightUpperArm = Find("RightArm", "RightUpperArm");
            rig.rightLowerArm = Find("RightForeArm", "RightLowerArm");
            rig.rightHand = Find("RightHand");
            rig.leftUpperLeg = Find("LeftUpLeg", "LeftUpperLeg");
            rig.leftLowerLeg = Find("LeftLeg", "LeftLowerLeg");
            rig.rightUpperLeg = Find("RightUpLeg", "RightUpperLeg");
            rig.rightLowerLeg = Find("RightLeg", "RightLowerLeg");

            // 앞쪽 방향: 발 → 발끝(루트 기준).
            var foot = Find("LeftFoot");
            var toe = Find("LeftToeBase", "LeftToes");
            if (foot != null && toe != null)
            {
                var f = root.transform.InverseTransformDirection(toe.position - foot.position);
                f.y = 0f;
                if (f.sqrMagnitude > 1e-8f)
                    rig.modelForward = f.normalized;
            }

            int linked = CountBones(rig);
            string sample = string.Join(", ", model.GetComponentsInChildren<Transform>(true).Take(6).Select(t => t.name));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var settings = AssetDatabase.LoadAssetAtPath<FighterModelSettings>(FighterModelSettings.LocalPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<FighterModelSettings>();
                AssetDatabase.CreateAsset(settings, FighterModelSettings.LocalPath);
            }
            settings.prefab = prefab;
            settings.fighter = 0;
            settings.use = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Knockbox] 3D 시험 외형 프리팹: {PrefabPath}, 원래 키 {bounds.size.y / scale:0.000} → {TargetHeight}, 배율 {scale:0.0000}, 앞쪽 {prefab.GetComponent<FighterModelRig>().modelForward}, 뼈 연결 {linked}/11, 이름 예: {sample}");
        }

        static int CountBones(FighterModelRig r) => new Transform[]
        {
            r.chest, r.leftUpperArm, r.leftLowerArm, r.leftHand, r.rightUpperArm, r.rightLowerArm, r.rightHand,
            r.leftUpperLeg, r.leftLowerLeg, r.rightUpperLeg, r.rightLowerLeg,
        }.Count(t => t != null);

        static Bounds Bounds(SkinnedMeshRenderer[] renderers)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);
            return b;
        }
    }
}
