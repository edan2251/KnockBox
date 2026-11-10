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
            if(Selection.gameObjects.Any(go=>go.transform.IsChildOf(a.terrainRoot)
                ||a.physicsRoot!=null&&go.transform.IsChildOf(a.physicsRoot)||a.ropeRoot!=null&&go.transform.IsChildOf(a.ropeRoot)))
                Selection.activeGameObject=a.gameObject;
            // Only the explicitly owned authoring draft is replaced; no asset or existing game scene is removed.
            if(a.ropeRoot!=null)foreach(var h in a.ropeRoot.GetComponentsInChildren<MapRopeHandle>(true))Undo.DestroyObjectImmediate(h.gameObject);
            foreach(var h in a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true))Undo.DestroyObjectImmediate(h.gameObject);
            if(a.physicsRoot!=null)foreach(var h in a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true))Undo.DestroyObjectImmediate(h.gameObject);
            a.shapes.Activate();
            for(int i=0;i<a.definition.BoxCount;i++)
            {
                var b=a.definition.Box(i);
                var sr=ShapeLibrary.Create(a.terrainRoot,MapBoxHandle.ObjectName(i),a.shapes.Square,SortingLayers.Terrain,0,terrainColor);
                var handle=Undo.AddComponent<MapBoxHandle>(sr.gameObject);handle.Initialize(b,i);
                Undo.RegisterCreatedObjectUndo(sr.gameObject,"맵 배치 불러오기");
            }
            for(int i=0;i<a.definition.PhysicalBoxCount;i++)MapPhysicalBoxTools.Create(a,a.definition.PhysicalBox(i),i);
            for(int i=0;i<a.definition.RopeCount;i++)MapRopeTools.Create(a,a.definition.Rope(i),i);
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
                trial.SaveLayout(ordered,a.ReadOrderedPhysicalBoxes(),a.ReadOrderedRopes(),a.spawn0.position,a.spawn1.position);
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
            if(a.physicsRoot!=null)foreach(var h in a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true))
            {
                var renderer=h.GetComponent<SpriteRenderer>();var tint=h.Connector?PaletteAsset.ConnectorFillColor:PropView.BoxColor(a.definition.Metadata().ResolveTheme(a.palette).prop,h.Order,h.PlayerPassThrough);
                if(renderer==null)continue;
                ConnectorBoxVisual.Apply(renderer,a.shapes,h.Connector,h.Read().size);
                if(renderer.color==tint)continue;renderer.color=tint;changed=true;
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
        public static bool HasUnsavedPlacement(MapAuthoring a)=>!string.IsNullOrEmpty(PendingSaveReason(a));
        public static string PendingSaveReason(MapAuthoring a)
        {
            string reason=PendingSaveCheck(a);
            // 보호된 맵에는 맵 저장 버튼이 꺼지므로, 저장하라는 대신 사본으로 남기는 방법을 안내한다.
            if(reason!=null&&!reason.StartsWith("아래 배치 오류",StringComparison.Ordinal)&&ProtectedReason(a.definition)!=null)
                return "보호된 기준 맵이라 맵 저장이 꺼져 있습니다. '현재 배치를 새 사본으로 저장'을 누르면 지금 배치가 새 맵 파일로 저장됩니다.";
            return reason;
        }
        static string PendingSaveCheck(MapAuthoring a)
        {
            if(a==null||a.definition==null||string.IsNullOrEmpty(a.LoadedId))return null;
            MapDefinition draft=null;
            try
            {
                draft=Draft(a);
                var errors=draft.Validate();
                if(errors.Count>0)return "아래 배치 오류를 고친 뒤 맵 저장을 눌러주세요.\n"+string.Join("\n",errors);
                if(draft.ToArena().ComputeGeometryChecksum()!=a.LoadedGeometry)
                    return "현재 배치가 아직 맵에 저장되지 않았습니다. 맵 저장을 눌러주세요.";
                if(a.definition.ToArena().ComputeGeometryChecksum()!=a.LoadedGeometry)
                    return "원본 맵 설정이 바뀌었습니다. 현재 배치를 유지하려면 맵 저장, 원본을 다시 열려면 맵 불러오기를 사용하세요.";
                return EditorUtility.IsDirty(a.definition)?"맵 설정 변경이 아직 저장되지 않았습니다. 맵 저장을 눌러주세요.":null;
            }
            catch(Exception e){return "저장 상태를 확인할 수 없습니다.\n"+e.Message;}
            finally{if(draft!=null)UnityEngine.Object.DestroyImmediate(draft);}
        }
        /// <summary>Validation 맵은 회귀 검사·직접 검수의 기준 원본이다. 작업은 사본에서 한다.</summary>
        public const string ReferenceFolder=Folder+"/Validation/";
        /// <summary>제작기의 맵 저장을 거절할 이유. 다른 Unity 편집이나 외부 파일 수정까지 막지는 않는다.</summary>
        public static string ProtectedReason(MapDefinition d)
        {
            if(d==null)return null;
            if(d.PreservedBaseline)return "기존 Brawl은 보호됩니다. 새 사본을 만들어주세요.";
            return AssetDatabase.GetAssetPath(d).StartsWith(ReferenceFolder,StringComparison.Ordinal)
                ?"Validation 폴더의 기준 맵은 맵 저장으로 덮어쓰지 않습니다. '현재 배치를 새 사본으로 저장'으로 작업용 사본을 만드세요.":null;
        }
        public static void Save(MapAuthoring a)
        {
            string protectedReason=ProtectedReason(a.definition);
            if(protectedReason!=null)throw new InvalidOperationException(protectedReason);
            var draft=Draft(a);
            try
            {
                var errors=draft.Validate();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
                Undo.RecordObject(a.definition,"맵 저장");
                a.definition.SaveLayout(a.ReadOrderedBoxes(),a.ReadOrderedPhysicalBoxes(),a.ReadOrderedRopes(),a.spawn0.position,a.spawn1.position);
                EditorUtility.SetDirty(a.definition);AssetDatabase.SaveAssetIfDirty(a.definition);a.MarkLoaded();
                // Reload exact float baselines for unchanged subsequent saves.
                foreach(var handle in a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true))handle.Initialize(a.definition.Box(handle.Order),handle.Order);
                if(a.physicsRoot!=null)foreach(var handle in a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true))handle.Initialize(a.definition.PhysicalBox(handle.Order),handle.Order);
                foreach(var handle in MapRopeTools.Handles(a))handle.Initialize(a.definition.Rope(handle.Order),handle.Order,handle.Target,handle.Source);
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
            a.shapes.Activate();var sr=ShapeLibrary.Create(a.terrainRoot,MapBoxHandle.ObjectName(count),a.shapes.Square,SortingLayers.Terrain,0,a.definition.Metadata().ResolveTheme(a.palette).terrain);
            sr.gameObject.AddComponent<MapBoxHandle>().Initialize(box,count);
            Undo.RegisterCreatedObjectUndo(sr.gameObject,"사각 지형 추가");Selection.activeGameObject=sr.gameObject;
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
        }
        /// <summary>삭제 후 번호의 빈칸을 메운다. 기존 판정 순서와 ID/배치 원본은 보존한다.</summary>
        public static int RepairOrder(MapAuthoring a)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 순서를 정리해주세요.");
            if(a==null||a.terrainRoot==null)throw new InvalidOperationException("맵의 지형 묶음 참조가 필요합니다.");
            if(a.physicsRoot==a.terrainRoot)throw new InvalidOperationException("지형과 물리 상자는 별도 묶음을 사용해주세요.");
            var ropes=MapRopeTools.Handles(a).OrderBy(h=>h.Order).ThenBy(h=>h.transform.GetSiblingIndex()).ToArray();
            if(ropes.Any(h=>h.transform.parent!=a.ropeRoot)||a.ropeRoot!=null&&(a.ropeRoot==a.physicsRoot||a.ropeRoot==a.terrainRoot))throw new InvalidOperationException("로프는 별도 묶음 바로 아래에 두세요.");
            var terrain=a.terrainRoot.GetComponentsInChildren<MapBoxHandle>(true);
            var physics=a.physicsRoot==null?Array.Empty<MapPhysicalBoxHandle>():a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true);
            // Do not silently reparent malformed drafts or reset transforms via Initialize.
            if(terrain.Any(h=>h.transform.parent!=a.terrainRoot)||physics.Any(h=>h.transform.parent!=a.physicsRoot))
                throw new InvalidOperationException("도형을 각 지형/물리 상자 묶음 바로 아래로 옮긴 뒤 정리해주세요.");
            terrain=terrain.OrderBy(h=>h.Order).ThenBy(h=>h.transform.GetSiblingIndex()).ToArray();
            physics=physics.OrderBy(h=>h.Order).ThenBy(h=>h.transform.GetSiblingIndex()).ToArray();
            int changed=0;
            for(int i=0;i<terrain.Length;i++)if(terrain[i].Order!=i||terrain[i].name!=MapBoxHandle.ObjectName(i))changed++;
            for(int i=0;i<physics.Length;i++)if(physics[i].Order!=i||physics[i].name!=MapPhysicalBoxHandle.ObjectName(i))changed++;
            for(int i=0;i<ropes.Length;i++)if(ropes[i].Order!=i||ropes[i].name!=MapRopeHandle.ObjectName(i))changed++;
            if(changed==0)return 0;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("도형 순서 정리");
            try
            {
                for(int i=0;i<terrain.Length;i++)
                {
                    var h=terrain[i];if(h.Order==i&&h.name==MapBoxHandle.ObjectName(i))continue;
                    Undo.RecordObjects(new UnityEngine.Object[]{h,h.gameObject},"도형 순서 정리");
                    h.SetOrder(i);RecordPrefab(h);
                }
                for(int i=0;i<physics.Length;i++)
                {
                    var h=physics[i];if(h.Order==i&&h.name==MapPhysicalBoxHandle.ObjectName(i))continue;
                    Undo.RecordObjects(new UnityEngine.Object[]{h,h.gameObject},"도형 순서 정리");
                    h.SetOrder(i);RecordPrefab(h);
                }
                for(int i=0;i<ropes.Length;i++){var h=ropes[i];if(h.Order==i&&h.name==MapRopeHandle.ObjectName(i))continue;Undo.RecordObjects(new UnityEngine.Object[]{h,h.gameObject},"로프 순서 정리");h.SetOrder(i);RecordPrefab(h);}
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
                MapPlacementSnap.ResetTracking();
                if(!EditorSceneManager.IsPreviewScene(a.gameObject.scene))EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
                return changed;
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }
        static void RecordPrefab(Component handle)
        {
            if(!PrefabUtility.IsPartOfPrefabInstance(handle))return;
            PrefabUtility.RecordPrefabInstancePropertyModifications(handle);
            PrefabUtility.RecordPrefabInstancePropertyModifications(handle.gameObject);
        }

        public static void SnapSelected(MapAuthoring a)
        {
            foreach(var t in Selection.transforms)
            {
                MapPlacementSnap.Apply(a,t);
            }
        }
        public static string SavedTrialGuid(MapAuthoring a)
        {
            if(a==null || a.definition==null || a.LoadedId!=a.definition.Id)
                throw new InvalidOperationException("시험할 맵 정의를 먼저 불러와주세요.");
            string pending=PendingSaveReason(a);
            if(!string.IsNullOrEmpty(pending))throw new InvalidOperationException(pending);
            var errors=a.definition.Validate();
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            string path=AssetDatabase.GetAssetPath(a.definition);
            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("맵을 정의 에셋으로 저장한 뒤 시험해주세요.");
            return AssetDatabase.AssetPathToGUID(path);
        }
        public static void Test(MapAuthoring a)
        {
            string guid=SavedTrialGuid(a);
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            MapTestReturn.Prepare(guid);
            try { EditorApplication.isPlaying=true; }
            catch { MapTestReturn.Restore(); throw; }
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
    public static class MapTestReturn
    {
        const string MapKey="Knockbox.MapAuthoring.TestMap", PreviousKey="Knockbox.MapAuthoring.PreviousStart";
        public static bool Active=>!string.IsNullOrEmpty(SessionState.GetString(MapKey,""));
        static MapTestReturn(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Prepare(string guid)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || Active)
                throw new InvalidOperationException("진행 중인 맵 시험을 먼저 종료해주세요.");
            var definition=AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if(definition==null)throw new InvalidOperationException("저장한 시험 맵을 찾을 수 없습니다.");
            var main=AssetDatabase.LoadAssetAtPath<SceneAsset>(MapAuthoringOperations.MainPath);
            if(main==null)throw new InvalidOperationException("Main 씬을 찾을 수 없습니다.");
            SessionState.SetString(PreviousKey,AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(MapKey,guid);
            EditorSceneManager.playModeStartScene=main;
        }
        public static void Restore()
        {
            if(!Active)return;
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousKey,""));
            SessionState.EraseString(MapKey);SessionState.EraseString(PreviousKey);
        }
        static void Changed(PlayModeStateChange state)
        {
            // Unity가 편집하던 씬 구성을 복원한다. 씬을 별도로 덮어 열지 않는다.
            if(state==PlayModeStateChange.EnteredEditMode)Restore();
        }
        [MenuItem("Knockbox/맵/시험 종료 및 편집 복귀")]
        static void ReturnToEditing()=>EditorApplication.isPlaying=false;
        [MenuItem("Knockbox/맵/시험 종료 및 편집 복귀",true)]
        static bool CanReturn()=>EditorApplication.isPlaying && Active;
    }
    [CustomEditor(typeof(MapAuthoring))]
    public sealed class MapAuthoringInspector : Editor
    {
        string message;
        MessageType messageType;
        bool references, theme, registration, snapping=true;
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
            if(target==null)return;
            var a=(MapAuthoring)target;serializedObject.Update();
            MapInspectorLayout.Section("1. 맵 파일");
            Field("definition","편집할 맵");serializedObject.ApplyModifiedProperties();
            using(new EditorGUI.DisabledScope(a.definition==null))
            {
                if(GUILayout.Button("맵 불러오기") && (!MapAuthoringOperations.HasUnsavedPlacement(a)||EditorUtility.DisplayDialog("미저장 배치","씬의 미저장 배치를 버리고 정의를 다시 여시겠어요?","열기","취소")))
                    Run(()=>MapAuthoringOperations.Open(a),"맵을 불러왔습니다.");
                if(GUILayout.Button("현재 배치를 새 사본으로 저장"))Run(()=>MapAuthoringOperations.Duplicate(a),"사본 저장 절차를 마쳤습니다. 편집할 맵을 확인하세요.");
            }
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,messageType);
            bool loaded=a.definition!=null&&a.LoadedId==a.definition.Id;
            if(!loaded)EditorGUILayout.HelpBox("맵을 선택한 뒤 맵 불러오기를 누르세요.",MessageType.Info);
            var errors=a.ValidatePlacement();
            string pending=errors.Count==0?MapAuthoringOperations.PendingSaveReason(a):"배치 오류를 먼저 확인해주세요.";
            if(errors.Count==0&&!string.IsNullOrEmpty(pending))EditorGUILayout.HelpBox(pending,MessageType.Warning);
            foreach(var error in errors)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("도형 순서 정리"))Run(()=>MapAuthoringOperations.RepairOrder(a),"빈 번호·중복 번호를 정리했습니다. 맵 저장으로 반영하세요.");
            if(MapRopeTools.Handles(a).Any(MapRopeTools.NeedsLengthFit))
                if(GUILayout.Button("짧은 로프만 길이 맞추기"))Run(()=>MapRopeTools.FitTooShort(a),"부족한 줄 길이를 맞췄습니다. 맵 저장으로 반영하세요.");

            MapInspectorLayout.Section("2. 오브젝트 추가");
            using(new EditorGUI.DisabledScope(!loaded))
            {
                if(GUILayout.Button("고정 사각 지형 추가"))Run(()=>MapAuthoringOperations.AddBox(a),"고정 지형을 추가했습니다.");
                if(GUILayout.Button("물리 상자 추가"))Run(()=>MapPhysicalBoxTools.Add(a),"물리 상자를 추가했습니다.");
                if(GUILayout.Button("긴 기둥 추가 · 플레이어 통과"))Run(()=>MapPhysicalBoxTools.Add(a,MapPhysicalBoxPreset.TallPillar),"긴 기둥을 추가했습니다.");
                if(GUILayout.Button("연결 상자 추가 · 공격 시 파괴"))Run(()=>MapPhysicalBoxTools.Add(a,MapPhysicalBoxPreset.Connector),"연결 상자를 추가했습니다.");
            }
            EditorGUILayout.HelpBox("로프: 상자 하나를 선택해 고정점 연결, 두 개를 선택해 상자끼리 연결합니다. 지형·상자를 선택하면 전용 편집 버튼이 나타납니다.",MessageType.None);
            MapInspectorLayout.Snapping(a,ref snapping);

            MapInspectorLayout.Section("3. 저장과 로컬 시험");
            string protectedReason=MapAuthoringOperations.ProtectedReason(a.definition);
            if(loaded&&protectedReason!=null)EditorGUILayout.HelpBox(protectedReason,MessageType.Info);
            using(new EditorGUI.DisabledScope(!loaded||protectedReason!=null))
                if(GUILayout.Button("맵 저장"))Run(()=>MapAuthoringOperations.Save(a),"맵 정의에 저장했습니다. 등록 없이 시험할 수 있습니다.");
            using(new EditorGUI.DisabledScope(!loaded||!string.IsNullOrEmpty(pending)))
                if(GUILayout.Button("저장한 맵으로 시험"))Run(()=>MapAuthoringOperations.Test(a),"Main 연습을 시작합니다. Play를 중지하면 제작기로 돌아옵니다.");
            EditorGUILayout.HelpBox("씬 저장(Ctrl+S)과 맵 저장은 별개입니다. 시험은 GameMaps 등록 없이 이 맵만 사용합니다. Play 중지로 편집에 복귀합니다.",MessageType.None);
            registration=EditorGUILayout.Foldout(registration,"정식 경기 목록 · 별도 등록",true);
            if(registration)
            {
                serializedObject.Update();Field("catalog","GameMaps 목록");serializedObject.ApplyModifiedProperties();
                using(new EditorGUI.DisabledScope(!loaded||!string.IsNullOrEmpty(pending)||a.catalog==null))
                    if(GUILayout.Button("저장한 맵을 GameMaps에 등록"))Run(()=>MapAuthoringOperations.Register(a),"게임 맵 목록에 등록했습니다.");
                using(new EditorGUI.DisabledScope(a.catalog==null))if(GUILayout.Button("등록 목록 열기"))Selection.activeObject=a.catalog;
                if(a.catalog!=null)foreach(var map in a.catalog.Maps)if(map!=null)EditorGUILayout.ObjectField(map.DisplayName,map,typeof(MapDefinition),false);
                EditorGUILayout.HelpBox("최대 8개. 두 맵 이상이면 직전 라운드와 다른 맵을 고릅니다. 목록은 다음 경기부터 적용합니다.",MessageType.None);
            }
            theme=EditorGUILayout.Foldout(theme,"맵 테마",true);
            if(theme&&a.definition!=null)
            {
                var settings=new SerializedObject(a.definition);settings.Update();
                using(new EditorGUI.DisabledScope(a.definition.PreservedBaseline))
                {
                    EditorGUILayout.PropertyField(settings.FindProperty("mapTheme"),new GUIContent("테마"),true);
                    if(settings.FindProperty("mapTheme").FindPropertyRelative("mode").enumValueIndex==0)
                        EditorGUILayout.PropertyField(settings.FindProperty("theme"),new GUIContent("기존 테마 (0 민트 / 1 사이언 / 2 슬레이트)"));
                    settings.ApplyModifiedProperties();MapAuthoringOperations.RefreshPreviewColors(a);
                }
                EditorGUILayout.HelpBox("테마 변경 후에도 맵 저장을 누르세요. 기둥·연결 상자 속·로프 색은 공통 고정값입니다.",MessageType.None);
            }
            references=EditorGUILayout.Foldout(references,"씬 연결 참조 · 고급",true);
            if(references)
            {
                serializedObject.Update();Field("ropeRoot","로프 묶음");Field("physicsRoot","물리 상자 묶음");Field("terrainRoot","지형 묶음");
                Field("spawn0","P1 시작 위치");Field("spawn1","P2 시작 위치");Field("shapes","기본 도형");Field("palette","공통 색상");serializedObject.ApplyModifiedProperties();
            }
            MapInspectorLayout.Guide();
        }
        void Field(string name,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(name),new GUIContent(label));
        void OnSceneGUI()=>MapPlacementSnap.DrawGrid((MapAuthoring)target);
        void Run(Action action,string done){try{action();message=done;messageType=MessageType.Info;}catch(Exception e){message=e.Message;messageType=MessageType.Error;}Repaint();}
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
            if(target==null||targets.Any(item=>item==null))return;
            serializedObject.Update();
            using(new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("order"),new GUIContent("지형 번호"));
            serializedObject.ApplyModifiedProperties();
            var box=(MapBoxHandle)target;var a=box.GetComponentInParent<MapAuthoring>();
            if(targets.Length==1)MapTerrainCut.Inspector(box);
            EditorGUILayout.HelpBox("Transform 위치는 중심, Scale X/Y는 폭·높이입니다. 직접 삭제한 뒤에는 도형 순서 정리로 빈 번호를 메울 수 있습니다. 번호는 자동 관리합니다.",MessageType.Info);
            if(a==null)return;
            if(GUILayout.Button("도형 순서 정리"))
            {try{MapAuthoringOperations.RepairOrder(a);message="번호를 정리했습니다. 맵 저장으로 반영하세요.";}catch(Exception e){message=e.Message;}}
            var so=new SerializedObject(a);so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("gridSnap"),new GUIContent("격자 스냅"));
            EditorGUILayout.PropertyField(so.FindProperty("gridStep"),new GUIContent("격자 간격"));so.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("맵 중심(x=0)을 기준으로 반대편에 복사합니다. 원본은 남고 높이·크기는 같습니다. 이미 같은 지형이 있으면 건너뜁니다.",MessageType.None);
            if(GUILayout.Button("선택 지형 좌우 대칭 복사"))
            {
                try{message=MapMirrorTools.CopySelection(a).Message;}
                catch(Exception e){message=e.Message;}
            }
            MapInspectorLayout.Navigation(a);
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void OnSceneGUI(){if(target is MapBoxHandle box&&box!=null){MapPlacementSnap.DrawGrid(box.GetComponentInParent<MapAuthoring>());MapTerrainCut.SceneGUI(box);}}
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
    internal static class MapInspectorLayout
    {
        public static void Section(string text){EditorGUILayout.Space(6);EditorGUILayout.LabelField(text,EditorStyles.boldLabel);}
        public static void Navigation(MapAuthoring a)
        {
            EditorGUILayout.Space(6);
            if(GUILayout.Button("맵 제작기로 · 저장 / 시험 / 추가"))Selection.activeGameObject=a.gameObject;
        }
        public static void Snapping(MapAuthoring a,ref bool open)
        {
            open=EditorGUILayout.Foldout(open,"배치 스냅",true);if(!open)return;
            var so=new SerializedObject(a);so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("gridSnap"),new GUIContent("격자 맞춤"));
            if(so.FindProperty("gridSnap").boolValue)EditorGUILayout.PropertyField(so.FindProperty("gridStep"),new GUIContent("격자 간격"));
            EditorGUILayout.PropertyField(so.FindProperty("boxTopSnap"),new GUIContent("상자 밑면을 가까운 윗면에 맞춤"));
            if(so.FindProperty("boxTopSnap").boolValue)EditorGUILayout.PropertyField(so.FindProperty("boxTopSnapDistance"),new GUIContent("상자 스냅 거리"));
            EditorGUILayout.PropertyField(so.FindProperty("spawnEdgeSnap"),new GUIContent("캐릭터 발을 지형 윗면에 맞춤"));
            if(so.FindProperty("spawnEdgeSnap").boolValue)EditorGUILayout.PropertyField(so.FindProperty("spawnSnapDistance"),new GUIContent("발 스냅 거리"));
            so.ApplyModifiedProperties();
        }
        public static void Guide()
        {
            if(GUILayout.Button("맵 제작 사용 안내"))EditorUtility.OpenWithDefaultApp(Path.GetFullPath("docs/team/MAP_AUTHORING.md"));
        }
    }

}
