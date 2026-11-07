using System;
using System.IO;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    public static class MapAuthoringOperations
    {
        public const string MainPath="Assets/_Project/Scenes/Main.unity";
        public const string ScenePath="Assets/_Project/Scenes/MapAuthoring.unity";
        public const string Folder="Assets/_Project/Data/Maps";
        public const string CatalogPath=Folder+"/GameMaps.asset";
        public static void Open(MapAuthoring a)
        {
            MapPlacementSnap.ResetTracking();
            if(a.definition==null||a.terrainRoot==null||a.spawn0==null||a.spawn1==null||a.shapes==null||a.palette==null)throw new InvalidOperationException("맵 제작 참조가 비어 있습니다.");
            if(a.definition.Validate().Count>0)throw new InvalidOperationException(string.Join("\n",a.definition.Validate()));
            var terrainColor=a.definition.Metadata().ResolveTheme(a.palette).terrain;
            // Only the explicitly owned authoring draft is replaced; no asset or existing game scene is removed.
            foreach(var h in a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true))Undo.DestroyObjectImmediate(h.gameObject);
            a.shapes.Activate();
            for(int i=0;i<a.definition.BoxCount;i++)
            {
                var b=a.definition.Box(i);
                var sr=ShapeLibrary.Create(a.terrainRoot,i.ToString(),a.shapes.Square,SortingLayers.Terrain,0,terrainColor);
                var handle=Undo.AddComponent<MapBoxHandle>(sr.gameObject);handle.Initialize(b,i);
                Undo.RegisterCreatedObjectUndo(sr.gameObject,"맵 배치 불러오기");
            }
            Undo.RecordObjects(new UnityEngine.Object[]{a,a.spawn0,a.spawn1},"맵 불러오기");
            a.spawn0.position=a.definition.Spawn(0);a.spawn1.position=a.definition.Spawn(1);a.MarkLoaded();
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
        }
        public static MapDefinition Draft(MapAuthoring a)
        {
            var ordered=a.ReadOrderedBoxes();
            var trial=UnityEngine.Object.Instantiate(a.definition);
            try
            {
                trial.MakeEditableCopy(a.definition.Id,a.definition.DisplayName);
                trial.SaveGeometry(ordered,a.spawn0.position,a.spawn1.position);
                return trial;
            }
            catch { UnityEngine.Object.DestroyImmediate(trial); throw; }
        }
        public static MapDefinition DuplicateDraft(MapAuthoring a,string title)
        {
            var copy=Draft(a);
            try
            {
                var errors=copy.Validate();
                if(errors.Count>0)throw new InvalidOperationException("현재 배치를 사본으로 저장할 수 없습니다.\n"+string.Join("\n",errors));
                copy.MakeEditableCopy(Guid.NewGuid().ToString("N"),title);
                return copy;
            }
            catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
        }
        public static bool RefreshPreviewColors(MapAuthoring a)
        {
            if(a==null||a.definition==null||a.palette==null||a.terrainRoot==null||a.LoadedId!=a.definition.Id)return false;
            var settings=a.definition.Metadata().MapTheme;
            if(settings!=null&&settings.Validate().Count>0)return false;
            var color=a.definition.Metadata().ResolveTheme(a.palette).terrain;
            bool changed=false;
            foreach(var handle in a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true))
            {
                var renderer=handle.GetComponent<SpriteRenderer>();
                if(renderer==null||renderer.color==color)continue;
                // Derived preview only: do not add undo records or change the geometry/definition.
                renderer.color=color;changed=true;
            }
            return changed;
        }
        public static void RefreshLoadedThemePreviews(MapDefinition definition)
        {
            if(definition==null||EditorApplication.isPlayingOrWillChangePlaymode)return;
            bool changed=false;
            foreach(var a in Resources.FindObjectsOfTypeAll<MapAuthoring>())
                if(a.gameObject.scene.IsValid()&&a.gameObject.scene.isLoaded&&a.definition==definition)
                    changed|=RefreshPreviewColors(a);
            if(changed)SceneView.RepaintAll();
        }
        public static bool HasUnsavedPlacement(MapAuthoring a)
        {
            if(a.definition==null||string.IsNullOrEmpty(a.LoadedId))return false;
            if(EditorUtility.IsDirty(a.definition))return true;
            MapDefinition draft=null;
            try
            {
                draft=Draft(a);
                return draft.ToArena().ComputeGeometryChecksum()!=a.LoadedGeometry
                    ||a.definition.ToArena().ComputeGeometryChecksum()!=a.LoadedGeometry;
            }
            catch{return true;}
            finally{if(draft!=null)UnityEngine.Object.DestroyImmediate(draft);}
        }
        public static void Save(MapAuthoring a)
        {
            if(a.definition.PreservedBaseline)throw new InvalidOperationException("기존 Brawl은 보호됩니다. 새 사본을 만들어주세요.");
            var draft=Draft(a);
            try
            {
                var errors=draft.Validate();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
                Undo.RecordObject(a.definition,"맵 저장");
                a.definition.SaveGeometry(a.ReadOrderedBoxes(),a.spawn0.position,a.spawn1.position);
                EditorUtility.SetDirty(a.definition);AssetDatabase.SaveAssetIfDirty(a.definition);a.MarkLoaded();
                // Reload exact float baselines for unchanged subsequent saves.
                foreach(var handle in a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true))handle.Initialize(a.definition.Box(handle.Order),handle.Order);
                EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
            }
            finally{UnityEngine.Object.DestroyImmediate(draft);}
        }
        public static void Register(MapAuthoring a)
        {
            if(HasUnsavedPlacement(a))throw new InvalidOperationException("배치를 먼저 저장한 뒤 등록해주세요.");
            Undo.RecordObject(a.catalog,"맵 등록");a.catalog.Register(a.definition);
            EditorUtility.SetDirty(a.catalog);AssetDatabase.SaveAssetIfDirty(a.catalog);
        }
        public static void AddBox(MapAuthoring a)
        {
            if(a.definition==null||a.terrainRoot==null)throw new InvalidOperationException("먼저 맵을 불러와주세요.");
            int count=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true).Length;
            var box=new MapBox{id="box_"+Guid.NewGuid().ToString("N"),kind=Knockbox.Simulation.TerrainKind.Solid,minX=-1,maxX=1,minY=-.5f,maxY=.5f};
            a.shapes.Activate();var sr=ShapeLibrary.Create(a.terrainRoot,count.ToString(),a.shapes.Square,SortingLayers.Terrain,0,a.definition.Metadata().ResolveTheme(a.palette).terrain);
            sr.gameObject.AddComponent<MapBoxHandle>().Initialize(box,count);
            Undo.RegisterCreatedObjectUndo(sr.gameObject,"사각 지형 추가");Selection.activeGameObject=sr.gameObject;
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
        }
        public static void SnapSelected(MapAuthoring a)
        {
            foreach(var t in Selection.transforms)
            {
                MapPlacementSnap.Apply(a,t);
            }
        }
        public static void Test(MapAuthoring a)
        {
            if(HasUnsavedPlacement(a))throw new InvalidOperationException("시험 전에 맵을 저장해주세요. 미저장 배치는 게임에 반영되지 않습니다.");
            if(a.catalog.Find(a.definition.MatchName)!=a.definition)throw new InvalidOperationException("먼저 게임 맵 목록에 등록해주세요.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            SessionState.SetString("Knockbox.MapAuthoring.TestMap",AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a.definition)));
            SessionState.SetString("Knockbox.MapAuthoring.PreviousStart",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath);
            EditorApplication.isPlaying=true;
        }
        public static void Duplicate(MapAuthoring a)
        {
            string path=EditorUtility.SaveFilePanelInProject("맵 사본 저장","Brawl_Trial","asset","새 맵 정의를 저장합니다.",Folder);
            if(string.IsNullOrEmpty(path))return;
            string stem=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(path),@"[^\p{L}\p{N}_]","_");
            path=Path.GetDirectoryName(path).Replace('\\','/')+"/"+stem+".asset";
            if(File.Exists(path))throw new InvalidOperationException("같은 이름의 맵이 있습니다. 다른 이름을 입력해주세요.");
            var copy=DuplicateDraft(a,Path.GetFileNameWithoutExtension(path));
            try
            {
                AssetDatabase.CreateAsset(copy,path);
                if(!AssetDatabase.Contains(copy))throw new IOException("사본 저장에 실패했습니다. 현재 배치는 그대로 유지됩니다.");
                // The saved copy already contains the visible draft. Keep the same scene objects
                // instead of opening the old definition and discarding unsaved placement.
                Undo.RecordObject(a,"맵 사본 선택");a.definition=copy;a.MarkLoaded();
                EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
            }
            finally { if(!AssetDatabase.Contains(copy))UnityEngine.Object.DestroyImmediate(copy); }
        }
    }

    [InitializeOnLoad]
    static class MapTestReturn
    {
        static MapTestReturn(){EditorApplication.playModeStateChanged+=Changed;}
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode||string.IsNullOrEmpty(SessionState.GetString("Knockbox.MapAuthoring.TestMap","")))return;
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("Knockbox.MapAuthoring.PreviousStart",""));
            SessionState.EraseString("Knockbox.MapAuthoring.TestMap");SessionState.EraseString("Knockbox.MapAuthoring.PreviousStart");
        }
    }
    [CustomEditor(typeof(MapAuthoring))]
    public sealed class MapAuthoringInspector : Editor
    {
        string message;
        bool references;
        void OnEnable(){Undo.undoRedoPerformed+=RefreshPreview;RefreshPreview();}
        void OnDisable()=>Undo.undoRedoPerformed-=RefreshPreview;
        void RefreshPreview()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(MapAuthoringOperations.RefreshPreviewColors(target as MapAuthoring))SceneView.RepaintAll();
            Repaint();
        }
        public override void OnInspectorGUI()
        {
            var a=(MapAuthoring)target;serializedObject.Update();
            Field("definition","편집할 맵");Field("catalog","등록 목록");
            EditorGUILayout.Space();Field("gridSnap","격자 스냅");Field("gridStep","격자 간격");
            Field("spawnEdgeSnap","발 위치를 지형 윗면에 맞춤");Field("spawnSnapDistance","윗면 인식 거리");
            serializedObject.ApplyModifiedProperties();
            references=EditorGUILayout.Foldout(references,"연결된 오브젝트");
            if(references)
            {
                Field("terrainRoot","지형 묶음");Field("spawn0","P1 시작 위치");Field("spawn1","P2 시작 위치");
                Field("shapes","기본 도형");Field("palette","공통 색상");serializedObject.ApplyModifiedProperties();
            }
            EditorGUILayout.HelpBox("선택한 도형을 움직이거나 크기를 바꾸면 격자에 맞춥니다. 기본 간격은 0.25입니다. 발 위치는 몸이 지형에 겹치지 않는 가까운 윗면에 붙습니다. 스냅을 끄면 자유롭게 배치합니다.",MessageType.Info);
            EditorGUILayout.HelpBox("맵 정의가 게임의 원본입니다. 배치 후 아래 저장 버튼을 누르세요. 번호는 판정 순서이며 긴 내부 ID는 이름에 표시하지 않습니다.",MessageType.None);
            if(MapAuthoringOperations.HasUnsavedPlacement(a))EditorGUILayout.HelpBox("미저장 배치 또는 원본 변경이 있습니다.",MessageType.Warning);
            foreach(var error in a.ValidatePlacement())EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("맵 불러오기") && (!MapAuthoringOperations.HasUnsavedPlacement(a)||EditorUtility.DisplayDialog("미저장 배치","씬의 미저장 배치를 버리고 정의를 다시 여시겠어요?","열기","취소")))Run(()=>MapAuthoringOperations.Open(a));
            if(GUILayout.Button("새 사본 만들기"))Run(()=>MapAuthoringOperations.Duplicate(a));
            if(GUILayout.Button("사각 지형 추가"))Run(()=>MapAuthoringOperations.AddBox(a));
            using(new EditorGUI.DisabledScope(a.definition==null||a.definition.PreservedBaseline))if(GUILayout.Button("맵 저장"))Run(()=>MapAuthoringOperations.Save(a));
            if(GUILayout.Button("게임 맵 목록에 등록"))Run(()=>MapAuthoringOperations.Register(a));
            if(GUILayout.Button("선택한 배치에 스냅 적용"))Run(()=>MapAuthoringOperations.SnapSelected(a));
            if(GUILayout.Button("선택 지형 좌우 대칭 복사"))
            {
                try{message=MapMirrorTools.CopySelection(a).Message;}
                catch(Exception e){message=e.Message;}
            }
            if(GUILayout.Button("저장한 맵으로 시험"))Run(()=>MapAuthoringOperations.Test(a));
            if(GUILayout.Button("등록된 맵 목록 보기"))Selection.activeObject=a.catalog;
            if(a.catalog!=null)foreach(var map in a.catalog.Maps)if(map!=null)EditorGUILayout.ObjectField(map.DisplayName,map,typeof(MapDefinition),false);
            EditorGUILayout.HelpBox("Main의 새 경기는 등록된 맵(최대 8개)을 사용합니다. 2개 이상이면 직전 라운드와 다른 맵을 고릅니다. 저장한 맵 시험은 선택한 맵만 사용합니다.",MessageType.Info);
            if(a.definition!=null)
            {
                EditorGUILayout.Space();EditorGUILayout.LabelField("현재 맵 테마",EditorStyles.boldLabel);
                var settings=new SerializedObject(a.definition);settings.Update();
                using(new EditorGUI.DisabledScope(a.definition.PreservedBaseline))
                {
                    EditorGUILayout.PropertyField(settings.FindProperty("mapTheme"),new GUIContent("테마"),true);
                    if(settings.FindProperty("mapTheme").FindPropertyRelative("mode").enumValueIndex==0)
                        EditorGUILayout.PropertyField(settings.FindProperty("theme"),new GUIContent("기존 테마 (0 민트 / 1 사이언 / 2 슬레이트)"));
                    settings.ApplyModifiedProperties();
                    MapAuthoringOperations.RefreshPreviewColors(a);
                }
                EditorGUILayout.HelpBox("테마 수정도 맵 저장 버튼으로 저장합니다. 배경 그라데이션은 테마 미리보기와 실제 경기에서 확인하세요.",MessageType.None);
            }
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void Field(string name,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(name),new GUIContent(label));
        void OnSceneGUI()=>MapPlacementSnap.DrawGrid((MapAuthoring)target);
        void Run(Action action){try{action();message="완료";}catch(Exception e){message=e.Message;}}
    }
    [CustomEditor(typeof(MapDefinition))]
    public sealed class MapDefinitionInspector : Editor
    {
        void OnEnable(){Undo.undoRedoPerformed+=RefreshPreviews;RefreshPreviews();}
        void OnDisable()=>Undo.undoRedoPerformed-=RefreshPreviews;
        void RefreshPreviews(){MapAuthoringOperations.RefreshLoadedThemePreviews(target as MapDefinition);Repaint();}
        public override void OnInspectorGUI()
        {
            var d=(MapDefinition)target;
            serializedObject.Update();
            using(new EditorGUI.DisabledScope(d.PreservedBaseline))
            {
                Field("displayName","맵 이름");Field("columns","맵 너비");Field("rows","맵 높이");
                Field("boundary","경계 반동");Field("spawn0","P1 발 위치");Field("spawn1","P2 발 위치");
                Field("mapTheme","맵 테마");Field("theme","기존 테마 번호");Field("light","광원 위치");
                Field("ceiling","천장 표시");Field("shadow","그림자 농도");
            }
            serializedObject.ApplyModifiedProperties();
            MapAuthoringOperations.RefreshLoadedThemePreviews(d);
            EditorGUILayout.HelpBox("지형은 MapAuthoring 씬에서 편집하세요. 내부 ID와 판정 순서는 유지합니다.",MessageType.Info);
            if(d.PreservedBaseline)EditorGUILayout.HelpBox("기존 Brawl 기준 에셋입니다. MapAuthoring에서 사본을 편집하세요.",MessageType.Info);
            foreach(var error in d.Validate())EditorGUILayout.HelpBox(error,MessageType.Error);
            if(d.Validate().Count==0){var a=d.ToArena();EditorGUILayout.LabelField("기존 맵 검증값",a.ComputeChecksum().ToString("X8"));EditorGUILayout.LabelField("편집용 지형 검증값",a.ComputeGeometryChecksum().ToString("X8"));}
        }
        void Field(string name,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(name),new GUIContent(label),true);
    }
    [CustomEditor(typeof(MapBoxHandle)),CanEditMultipleObjects]
    public sealed class MapBoxInspector : Editor
    {
        string message;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using(new EditorGUI.DisabledScope(targets.Length>1))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("order"),new GUIContent("지형 번호"));
            serializedObject.ApplyModifiedProperties();
            var box=(MapBoxHandle)target;var a=box.GetComponentInParent<MapAuthoring>();
            EditorGUILayout.HelpBox("Transform 위치는 중심, Scale X/Y는 폭·높이입니다. 번호를 바꾸면 중복이나 빈 번호가 없도록 맞추세요.",MessageType.Info);
            if(a==null)return;var so=new SerializedObject(a);so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("gridSnap"),new GUIContent("격자 스냅"));
            EditorGUILayout.PropertyField(so.FindProperty("gridStep"),new GUIContent("격자 간격"));so.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("맵 중심(x=0)을 기준으로 반대편에 복사합니다. 원본은 남고 높이·크기는 같습니다. 이미 같은 지형이 있으면 건너뜁니다.",MessageType.None);
            if(GUILayout.Button("선택 지형 좌우 대칭 복사"))
            {
                try{message=MapMirrorTools.CopySelection(a).Message;}
                catch(Exception e){message=e.Message;}
            }
            if(GUILayout.Button("맵 제작 설정 보기"))Selection.activeGameObject=a.gameObject;
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void OnSceneGUI()=>MapPlacementSnap.DrawGrid(((MapBoxHandle)target).GetComponentInParent<MapAuthoring>());
    }
    [CustomEditor(typeof(MapCatalog))]
    public sealed class MapCatalogInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();EditorGUILayout.PropertyField(serializedObject.FindProperty("maps"),new GUIContent("등록된 맵"),true);serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("등록 목록은 다음 경기부터 적용하며 내부 ID 순서로 고정합니다. 1개면 같은 맵, 2~8개면 직전 라운드와 다른 맵을 고릅니다. 같은 지형의 다른 사본은 화면이 같아 보일 수 있습니다. 8개 초과는 아직 지원하지 않습니다.",MessageType.Info);
            foreach(var error in ((MapCatalog)target).Validate())EditorGUILayout.HelpBox(error,MessageType.Error);
        }
    }
}
