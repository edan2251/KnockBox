using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game.Maps;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    public enum MapPhysicalBoxPreset
    {
        [InspectorName("기본 상자")] Box,
        [InspectorName("긴 물리 기둥")] TallPillar,
        [InspectorName("공격으로 부서지는 연결 상자")] Connector,
    }

    public static class MapPhysicalBoxTools
    {
        public static Transform EnsureRoot(MapAuthoring a)
        {
            if(a.physicsRoot!=null)return a.physicsRoot;
            Undo.RecordObject(a,"물리 상자 묶음 연결");
            var go=new GameObject("Physical_Boxes");go.transform.SetParent(a.transform,false);
            Undo.RegisterCreatedObjectUndo(go,"물리 상자 묶음 생성");a.physicsRoot=go.transform;return go.transform;
        }
        public static MapPhysicalBoxHandle Create(MapAuthoring a,MapPhysicalBox box,int order)
        {
            a.shapes.Activate();
            var renderer=ShapeLibrary.Create(EnsureRoot(a),MapPhysicalBoxHandle.ObjectName(order),a.shapes.Square,SortingLayers.Terrain,3,
                PropView.BoxColor(a.definition.Metadata().ResolveTheme(a.palette).prop,order,box.playerPassThrough));
            var handle=renderer.gameObject.AddComponent<MapPhysicalBoxHandle>();handle.Initialize(box,order);
            ConnectorBoxVisual.Apply(renderer,a.shapes,box.connector,box.size);
            Undo.RegisterCreatedObjectUndo(renderer.gameObject,"물리 상자 배치");return handle;
        }
        public static void CheckEditing(MapAuthoring a,bool requireValidPlacement=true)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 편집해주세요.");
            if(a==null||a.definition==null||a.shapes==null||a.palette==null||a.LoadedId!=a.definition.Id)
                throw new InvalidOperationException("맵을 먼저 불러와주세요.");
            var errors=a.ValidatePlacement();if(requireValidPlacement&&errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        }
        /// <summary>같은 물리 몸체의 시작 크기. 긴 기둥은 플레이어 통과 설정만 추가한다.</summary>
        public static Vector2 PresetSize(MapPhysicalBoxPreset preset)
        {
            switch(preset)
            {
                case MapPhysicalBoxPreset.Connector: return new Vector2(.65f,.65f);
                case MapPhysicalBoxPreset.Box: return new Vector2(1.5f,1.5f);
                case MapPhysicalBoxPreset.TallPillar: return new Vector2(1.5f,4f);
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }
        public static MapPhysicalBoxHandle Add(MapAuthoring a,MapPhysicalBoxPreset preset=MapPhysicalBoxPreset.Box)
        {
            CheckEditing(a);
            int order=a.ReadOrderedPhysicalBoxes().Length;
            if(order>=PropState.Capacity)throw new InvalidOperationException("물리 상자는 최대 "+PropState.Capacity+"개입니다.");
            var size=PresetSize(preset);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName(preset==MapPhysicalBoxPreset.TallPillar?"긴 물리 기둥 추가":"물리 상자 추가");
            try
            {
                var h=Create(a,new MapPhysicalBox{id="prop_"+Guid.NewGuid().ToString("N"),center=Vector2.zero,size=size,
                    mass=preset==MapPhysicalBoxPreset.TallPillar?4f:size.x*size.y,
                    playerPassThrough=preset==MapPhysicalBoxPreset.TallPillar, connector=preset==MapPhysicalBoxPreset.Connector, connectorSpeedLoss=.15f},order);
                Undo.CollapseUndoOperations(group);Mark(a);Selection.activeGameObject=h.gameObject;return h;
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }
        public static MapPhysicalBoxHandle[] SelectionFor(MapAuthoring a)
        {
            var selected=Selection.gameObjects.SelectMany(go=>go.GetComponent<MapRopeHandle>() is MapRopeHandle rope
                ? MapRopeTools.BodiesOf(new[]{rope}) : new[]{go.GetComponent<MapPhysicalBoxHandle>()}).Distinct().ToArray();
            if(selected.Length==0||selected.Any(h=>h==null||h.transform.parent!=a.physicsRoot))
                throw new InvalidOperationException("같은 맵의 물리 상자를 선택해주세요.");
            return selected.OrderBy(h=>h.Order).ToArray();
        }
        public static List<MapPhysicalBoxHandle> Copy(MapAuthoring a,IEnumerable<MapPhysicalBoxHandle> selection,bool mirror)
        {
            CheckEditing(a);var sources=selection.Distinct().OrderBy(h=>h==null?-1:h.Order).ToArray();
            if(sources.Length==0||sources.Any(h=>h==null||h.transform.parent!=a.physicsRoot))throw new InvalidOperationException("같은 맵의 물리 상자를 선택해주세요.");
            var existing=a.ReadOrderedPhysicalBoxes().ToList();var pending=new List<MapPhysicalBox>();var ids=new Dictionary<string,string>();
            float height=sources.Max(h=>h.Read().Bounds.maxY)-sources.Min(h=>h.Read().Bounds.minY);
            foreach(var source in sources)
            {
                var box=source.Read();
                if(!MapDefinition.Finite(box.center.x)||!MapDefinition.Finite(box.center.y)||!MapDefinition.Finite(box.size.x)||!MapDefinition.Finite(box.size.y)
                    ||box.size.x<=0||box.size.y<=0||!MapDefinition.Finite(box.mass)||box.mass<=0)throw new InvalidOperationException("위치·크기·질량을 먼저 확인해주세요.");
                if(mirror)box.center.x=-box.center.x;else box.center.y+=height;
                if(mirror && existing.Concat(pending).Any(b=>b.center==box.center&&b.size==box.size))continue;
                string oldId=box.id;box.id="prop_"+Guid.NewGuid().ToString("N");ids.Add(oldId,box.id);pending.Add(box);
            }
            if(existing.Count+pending.Count>PropState.Capacity)throw new InvalidOperationException("물리 상자는 최대 "+PropState.Capacity+"개입니다.");
            var result=new List<MapPhysicalBoxHandle>();if(pending.Count==0)return result;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName(mirror?"물리 상자 좌우 대칭 복사":"물리 상자 위로 복제");
            try
            {
                foreach(var box in pending)result.Add(Create(a,box,existing.Count+result.Count));
                MapRopeTools.CopyForBoxes(a,ids,new Vector2(0,height),mirror);
                Undo.CollapseUndoOperations(group);MapPlacementSnap.ResetTracking();Mark(a);return result;
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }
        public static void CopySelected(MapAuthoring a,bool mirror)
        {
            var copies=Copy(a,SelectionFor(a),mirror);
            if(copies.Count>0)Selection.objects=copies.Select(h=>(UnityEngine.Object)h.gameObject).ToArray();
        }
        public static void DeleteSelected(MapAuthoring a)
        {
            CheckEditing(a,false);var chosen=SelectionFor(a);var remaining=a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true)
                .Except(chosen).OrderBy(h=>h.Order).ToArray();
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("선택 물리 상자 삭제");
            Selection.objects=Array.Empty<UnityEngine.Object>();
            MapRopeTools.RemoveForBoxes(a,chosen);
            foreach(var h in chosen)Undo.DestroyObjectImmediate(h.gameObject);
            for(int i=0;i<remaining.Length;i++){Undo.RecordObjects(new UnityEngine.Object[]{remaining[i],remaining[i].gameObject},"물리 상자 번호 갱신");remaining[i].SetOrder(i);}
            Undo.CollapseUndoOperations(group);MapPlacementSnap.ResetTracking();Mark(a);Selection.activeGameObject=a.gameObject;
        }
        static void Mark(MapAuthoring a)
        {if(!EditorSceneManager.IsPreviewScene(a.gameObject.scene))EditorSceneManager.MarkSceneDirty(a.gameObject.scene);}
    }

    [CustomEditor(typeof(MapPhysicalBoxHandle)),CanEditMultipleObjects]
    public sealed class MapPhysicalBoxInspector : Editor
    {
        string message;
        bool snapping;
        public override void OnInspectorGUI()
        {
            if(target==null||targets.Any(item=>item==null))return;
            var box=(MapPhysicalBoxHandle)target;var a=box.GetComponentInParent<MapAuthoring>();if(a==null)return;
            serializedObject.Update();MapInspectorLayout.Section("물리 오브젝트");
            using(new EditorGUI.DisabledScope(true))EditorGUILayout.PropertyField(serializedObject.FindProperty("order"),new GUIContent("자동 번호"));
            var pass=serializedObject.FindProperty("playerPassThrough");var connector=serializedObject.FindProperty("connector");
            EditorGUI.showMixedValue=pass.hasMultipleDifferentValues||connector.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();int role=EditorGUILayout.Popup("역할",connector.boolValue?2:pass.boolValue?1:0,new[]{"일반 물리 상자","플레이어 통과 기둥","공격 시 파괴되는 연결 상자"});
            if(EditorGUI.EndChangeCheck()){pass.boolValue=role==1;connector.boolValue=role==2;}
            EditorGUI.showMixedValue=false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mass"),new GUIContent("질량"));
            if(connector.boolValue)
            {
                var loss=serializedObject.FindProperty("connectorSpeedLoss");EditorGUI.showMixedValue=loss.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();float value=EditorGUILayout.Slider("관통 감속 (%)",loss.floatValue*100f,0,100);
                if(EditorGUI.EndChangeCheck())loss.floatValue=value*.01f;EditorGUI.showMixedValue=false;
                EditorGUILayout.HelpBox("밟기·점프로는 유지됩니다. 펀치·차징·던져진 플레이어에 파괴되며 붙어 있던 줄만 풀립니다.",MessageType.None);
            }
            else if(pass.boolValue)EditorGUILayout.HelpBox("플레이어만 통과합니다. 지형·다른 상자와는 충돌합니다.",MessageType.None);
            serializedObject.ApplyModifiedProperties();MapAuthoringOperations.RefreshPreviewColors(a);
            EditorGUILayout.HelpBox("Transform 위치 = 중심, Scale X/Y = 폭·높이. 크기나 역할을 바꿔도 질량은 유지합니다.",MessageType.None);
            MapInspectorLayout.Section("로프 연결");
            using(new EditorGUI.DisabledScope(targets.Length!=1))
                if(GUILayout.Button("고정점 → 이 상자 연결"))Run(()=>MapRopeTools.AddTo(a,box));
            using(new EditorGUI.DisabledScope(targets.Length!=2))
                if(GUILayout.Button("선택한 상자 2개 연결"))Run(()=>MapRopeTools.ConnectSelected(a));
            EditorGUILayout.HelpBox("상자끼리 연결하려면 Hierarchy에서 Ctrl로 두 개를 선택하세요. 줄의 양끝은 생성 후 Scene 뷰에서 움직입니다.",MessageType.None);
            MapInspectorLayout.Section("선택 오브젝트 편집");
            if(GUILayout.Button("위로 복제 · 연결 로프 포함"))Run(()=>MapPhysicalBoxTools.CopySelected(a,false));
            if(GUILayout.Button("좌우 대칭 복사 · 연결 로프 포함"))Run(()=>MapPhysicalBoxTools.CopySelected(a,true));
            if(GUILayout.Button("삭제 · 붙은 로프도 삭제")){Run(()=>MapPhysicalBoxTools.DeleteSelected(a));return;}
            if(GUILayout.Button("도형 순서 정리"))Run(()=>MapAuthoringOperations.RepairOrder(a));
            MapInspectorLayout.Snapping(a,ref snapping);MapInspectorLayout.Navigation(a);
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void Run(Action action){try{action();message="배치에 반영했습니다. 맵 저장으로 확정하세요.";}catch(Exception e){message=e.Message;}}
        void OnSceneGUI()
        {
            var box = target as MapPhysicalBoxHandle;
            if (box == null) return;
            MapPlacementSnap.DrawGrid(box.GetComponentInParent<MapAuthoring>());
        }
    }
}