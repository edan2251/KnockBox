using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    public static class MapRopeTools
    {
        public const float DefaultBreakForce=600;
        public static Transform EnsureRoot(MapAuthoring a)
        {
            if(a.ropeRoot!=null)return a.ropeRoot;
            Undo.RecordObject(a,"로프 묶음 연결");var go=new GameObject("Ropes");go.transform.SetParent(a.transform,false);
            Undo.RegisterCreatedObjectUndo(go,"로프 묶음 생성");a.ropeRoot=go.transform;return go.transform;
        }
        public static MapRopeHandle[] Handles(MapAuthoring a)=>a.ropeRoot==null?Array.Empty<MapRopeHandle>():
            a.ropeRoot.GetComponentsInChildren<MapRopeHandle>(true).OrderBy(h=>h.Order).ToArray();
        public static MapRopeHandle Create(MapAuthoring a,MapRope rope,int order)
        {
            if(order>=GraphRope.Capacity)throw new InvalidOperationException("로프는 최대 96개입니다.");
            var target=a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true).SingleOrDefault(h=>h.Id==rope.boxId);
            if(target==null)throw new InvalidOperationException("로프의 연결 상자가 없습니다.");
            var go=new GameObject(MapRopeHandle.ObjectName(order));go.transform.SetParent(EnsureRoot(a),false);
            var h=go.AddComponent<MapRopeHandle>();h.Initialize(rope,order,target,string.IsNullOrEmpty(rope.sourceBoxId)?null:a.physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true).SingleOrDefault(b=>b.Id==rope.sourceBoxId));
            Undo.RegisterCreatedObjectUndo(go,"로프 배치");return h;
        }
        public static MapRopeHandle AddTo(MapAuthoring a,MapPhysicalBoxHandle box)
        {
            MapPhysicalBoxTools.CheckEditing(a);
            if(box==null||box.transform.parent!=a.physicsRoot)throw new InvalidOperationException("같은 맵의 물리 상자를 선택해주세요.");
            if(Handles(a).Length>=GraphRope.Capacity)throw new InvalidOperationException("로프는 최대 96개입니다.");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("상자에 로프 연결");
            var b=box.Read();var local=new Vector2(0,b.size.y*.5f);
            var h=Create(a,new MapRope{graph=true,id="rope_"+Guid.NewGuid().ToString("N"),boxId=b.id,localAnchor=local,
                anchor=b.center+local+Vector2.up*4,length=4,breakable=false,breakForce=DefaultBreakForce,
                mode=RopeBreakMode.Unbreakable,connectorPosition=.5f,connectorSize=.55f,connectorSpeedLoss=.15f},Handles(a).Length);
            Undo.CollapseUndoOperations(group);Mark(a);Selection.activeGameObject=h.gameObject;return h;
        }
        public static MapRopeHandle Connect(MapAuthoring a,MapPhysicalBoxHandle source,MapPhysicalBoxHandle target)
        {
            MapPhysicalBoxTools.CheckEditing(a);
            if(source==null||target==null||source==target||source.transform.parent!=a.physicsRoot||target.transform.parent!=a.physicsRoot)
                throw new InvalidOperationException("서로 다른 상자 두 개를 같은 맵에서 선택해주세요.");
            if(Handles(a).Length>=GraphRope.Capacity)throw new InvalidOperationException("로프는 최대 96개입니다.");
            var p=source.Read();var q=target.Read();var delta=q.center-p.center;
            bool vertical=Mathf.Abs(delta.y)>=Mathf.Abs(delta.x);float sign=(vertical?delta.y:delta.x)>=0?1:-1;
            var pa=vertical?new Vector2(0,p.size.y*.5f*sign):new Vector2(p.size.x*.5f*sign,0);
            var pb=vertical?new Vector2(0,-q.size.y*.5f*sign):new Vector2(-q.size.x*.5f*sign,0);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("두 상자 로프 연결");
            var h=Create(a,new MapRope{graph=true,id="rope_"+Guid.NewGuid().ToString("N"),boxId=q.id,sourceBoxId=p.id,
                anchor=p.center+pa,sourceLocalAnchor=pa,localAnchor=pb,length=Mathf.Max(.05f,Vector2.Distance(p.center+pa,q.center+pb)),mode=RopeBreakMode.Unbreakable},Handles(a).Length);
            Undo.CollapseUndoOperations(group);Mark(a);Selection.activeGameObject=h.gameObject;return h;
        }
        public static void ConnectSelected(MapAuthoring a)
        {
            var boxes=Selection.gameObjects.Select(g=>g.GetComponent<MapPhysicalBoxHandle>()).Where(b=>b!=null).Distinct().OrderBy(b=>b.Order).ToArray();
            if(boxes.Length!=2)throw new InvalidOperationException("Hierarchy에서 상자 두 개를 함께 선택해주세요.");
            Connect(a,boxes[0],boxes[1]);
        }
        public static void ConvertToGraph(MapRopeHandle h)
        {
            Undo.RecordObject(h,"일반 연결 로프로 전환");h.Graph=true;h.Mode=RopeBreakMode.Unbreakable;
            Mark(h.GetComponentInParent<MapAuthoring>());
        }
        public static void SetMode(IEnumerable<MapRopeHandle> selection,RopeBreakMode mode)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 편집해주세요.");
            if((int)mode<0||(int)mode>(int)RopeBreakMode.Connector)throw new ArgumentOutOfRangeException(nameof(mode));
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("로프 종류 변경");
            foreach(var h in selection.Where(x=>x!=null).Distinct())
            {
                Undo.RecordObject(h,"로프 종류 변경");h.Graph=false;h.Mode=mode;
                if(mode==RopeBreakMode.Connector && h.ConnectorSize<=0)
                {h.ConnectorPosition=.5f;h.ConnectorSize=.55f;h.ConnectorSpeedLoss=.15f;}
                Mark(h.GetComponentInParent<MapAuthoring>());
            }
            Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
        }
        public static IEnumerable<MapPhysicalBoxHandle> BodiesOf(IEnumerable<MapRopeHandle> ropes)
            =>ropes.SelectMany(r=>r.Graph&&r.SourceIsBody?new[]{r.Source,r.Target}:new[]{r.Target}).Where(b=>b!=null).Distinct();
        public static void CopyForBoxes(MapAuthoring a,Dictionary<string,string> ids,Vector2 offset,bool mirror)
        {
            var originals=Handles(a);int order=originals.Length;
            foreach(var h in originals)
            {
                var r=h.Read();if(!ids.TryGetValue(r.boxId,out var newId))continue;
                // Copy internal graph edges only when both body endpoints are selected. Never tie a new assembly back to an unselected original.
                if(r.graph&&!string.IsNullOrEmpty(r.sourceBoxId))
                {if(!ids.TryGetValue(r.sourceBoxId,out var sourceId))continue;r.sourceBoxId=sourceId;}
                r.id="rope_"+Guid.NewGuid().ToString("N");r.boxId=newId;
                if(mirror){r.anchor.x=-r.anchor.x;r.localAnchor.x=-r.localAnchor.x;r.sourceLocalAnchor.x=-r.sourceLocalAnchor.x;}else r.anchor+=offset;
                Create(a,r,order++);
            }
        }
        public static void RemoveForBoxes(MapAuthoring a,IEnumerable<MapPhysicalBoxHandle> boxes)
        {
            var selected=new HashSet<MapPhysicalBoxHandle>(boxes);
            var ids=new HashSet<string>(selected.Select(b=>b.Id));
            // A missing Inspector reference can still retain its saved body ID. Remove only that body's incident edges.
            DeleteCore(a,Handles(a).Where(r=>selected.Contains(r.Target)||r.Target==null&&ids.Contains(r.Read().boxId)
                ||r.Graph&&r.SourceIsBody&&(selected.Contains(r.Source)||r.Source==null&&ids.Contains(r.Read().sourceBoxId))).ToArray());
        }
        static void DeleteCore(MapAuthoring a,MapRopeHandle[] selected)
        {
            foreach(var h in selected)Undo.DestroyObjectImmediate(h.gameObject);
            var remaining=Handles(a);
            for(int i=0;i<remaining.Length;i++)
            {
                Undo.RecordObjects(new UnityEngine.Object[]{remaining[i],remaining[i].gameObject},"로프 번호 정리");
                remaining[i].SetOrder(i);
            }
        }
        public static void Delete(MapAuthoring a,IEnumerable<MapRopeHandle> selection)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 편집해주세요.");
            var chosen=selection.Distinct().ToArray();
            if(a==null||chosen.Any(h=>h==null||h.transform.parent!=a.ropeRoot))throw new InvalidOperationException("같은 맵의 로프를 선택해주세요.");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("로프 삭제");
            Selection.objects=Array.Empty<UnityEngine.Object>();DeleteCore(a,chosen);Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
            Mark(a);Selection.activeGameObject=a.gameObject;
        }
        public static bool NeedsLengthFit(MapRopeHandle h)
        {
            if(h==null||h.Target==null||!MapDefinition.Finite(h.Length))return false;
            float distance=Vector2.Distance(h.Anchor,h.Connection);
            return MapDefinition.Finite(distance)&&h.Length+0.0001f<Mathf.Max(.05f,distance);
        }
        public static int FitTooShort(MapAuthoring a)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 길이를 맞춰주세요.");
            if(a==null)throw new InvalidOperationException("맵 제작 설정을 선택해주세요.");
            var shortRopes=Handles(a).Where(h=>NeedsLengthFit(h)&&h.Target.transform.parent==a.physicsRoot).ToArray();
            if(shortRopes.Length==0)return 0;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("짧은 로프 길이 맞춤");
            foreach(var h in shortRopes)FitLength(h);
            Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
            Mark(a);return shortRopes.Length;
        }
        public static void FitLength(MapRopeHandle h)
        {
            Undo.RecordObject(h,"줄 길이 맞춤");h.Length=Mathf.Max(.05f,Vector2.Distance(h.Anchor,h.Connection));Mark(h.GetComponentInParent<MapAuthoring>());
        }
        public static void Mark(MapAuthoring a)
        {if(a!=null&&!EditorSceneManager.IsPreviewScene(a.gameObject.scene))EditorSceneManager.MarkSceneDirty(a.gameObject.scene);}
    }

    [CustomEditor(typeof(MapRopeHandle)),CanEditMultipleObjects]
    public sealed class MapRopeInspector : Editor
    {
        string message;
        public override void OnInspectorGUI()
        {
            if(target==null||targets.Any(t=>t==null))return;
            var h=(MapRopeHandle)target;var a=h.GetComponentInParent<MapAuthoring>();
            serializedObject.Update();
            using(new EditorGUI.DisabledScope(true))EditorGUILayout.PropertyField(serializedObject.FindProperty("order"),new GUIContent("로프 번호"));
            if(h.Graph)
            {
                EditorGUILayout.LabelField("일반 로프 · 끊어지지 않음");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceIsBody"),new GUIContent("시작점도 상자에 연결"));
                if(serializedObject.FindProperty("sourceIsBody").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceBox"),new GUIContent("시작 상자"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceLocalAnchor"),new GUIContent("시작 상자 안 연결점"));
                }
                EditorGUILayout.HelpBox("시작점도 상자에 연결을 끄면 Transform 위치에 고정됩니다. 한 상자에 여러 줄을 붙일 수 있습니다. 파괴 연결부는 별도 연결 상자로 배치하세요.",MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBox"),new GUIContent("끝 상자"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localAnchor"),new GUIContent("상자 안 연결점"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("length"),new GUIContent("줄 길이"));
            if(!h.Graph)
            {
                EditorGUILayout.HelpBox("이 줄은 이전 맵 호환용입니다. 과거 당김 파단·줄 위 연결부는 보존하며 새 제작에는 일반 로프와 별도 연결 상자를 사용합니다.",MessageType.Info);
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mode"),new GUIContent("과거 규칙"));
                    if(h.Mode==RopeBreakMode.LegacyTension)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("breakable"),new GUIContent("당김 파단 사용"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("breakForce"),new GUIContent("기록된 파단 강도"));
                    }
                }
                if(GUILayout.Button("일반 로프로 전환 · 과거 파단 규칙 제거") && EditorUtility.DisplayDialog("이전 로프 전환","당김 파단과 줄 위 연결부가 없어집니다. 과거 기록용 원본을 남기려면 먼저 맵 사본을 만드세요.","전환","취소"))
                {serializedObject.ApplyModifiedProperties();foreach(MapRopeHandle r in targets)MapRopeTools.ConvertToGraph(r);serializedObject.Update();}
            }
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Scene 뷰의 두 손잡이로 양끝을 옮깁니다. 상자 안 연결점은 중심 기준 좌표입니다. (0, 0)은 중심이며 끝에 붙이면 회전 반응이 달라집니다. 줄 길이는 두 점 거리 이상, 더 길면 느슨해집니다.",MessageType.Info);
            if(h.Target!=null)
            {
                EditorGUILayout.LabelField("현재 두 점 거리",Vector2.Distance(h.Anchor,h.Connection).ToString("0.###"));
                if(MapRopeTools.NeedsLengthFit(h))EditorGUILayout.HelpBox("줄이 현재 거리보다 짧아 맵을 저장할 수 없습니다. 아래 길이 맞추기를 눌러주세요.",MessageType.Warning);
            }
            if(a==null)return;
            if(GUILayout.Button("현재 거리로 줄 길이 맞추기"))Run(()=>{foreach(MapRopeHandle r in targets)MapRopeTools.FitLength(r);});
            if(GUILayout.Button("상자 윗면 중앙에 연결"))Run(()=>{
                foreach(MapRopeHandle r in targets){if(r.Target==null)continue;Undo.RecordObject(r,"연결점 맞춤");r.LocalAnchor=new Vector2(0,r.Target.Read().size.y*.5f);MapRopeTools.FitLength(r);}
            });
            EditorGUILayout.HelpBox("상자를 복사할 때 양끝 상자를 함께 선택한 줄과 고정점 줄을 복사합니다. 선택하지 않은 원본 상자로 이어지는 줄은 복사하지 않습니다.",MessageType.Info);
            if(GUILayout.Button("상자와 로프 함께 위로 복제"))Run(()=>MapPhysicalBoxTools.Copy(a,MapRopeTools.BodiesOf(targets.Cast<MapRopeHandle>()),false));
            if(GUILayout.Button("상자와 로프 함께 좌우 대칭"))Run(()=>MapPhysicalBoxTools.Copy(a,MapRopeTools.BodiesOf(targets.Cast<MapRopeHandle>()),true));
            if(GUILayout.Button("로프만 삭제 · 상자는 유지")){Run(()=>MapRopeTools.Delete(a,targets.Cast<MapRopeHandle>()));return;}
            if(GUILayout.Button("도형 순서 정리"))Run(()=>MapAuthoringOperations.RepairOrder(a));
            MapInspectorLayout.Navigation(a);
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void Run(Action action){try{action();message="배치에 반영했습니다. 맵 저장으로 확정하세요.";}catch(Exception e){message=e.Message;}}
        void OnSceneGUI()
        {
            var h=target as MapRopeHandle;if(h==null)return;var a=h.GetComponentInParent<MapAuthoring>();if(a==null)return;
            MapPlacementSnap.DrawGrid(a);EditorGUI.BeginChangeCheck();
            var anchor=Handles.PositionHandle(h.Anchor,Quaternion.identity);
            if(EditorGUI.EndChangeCheck())
            {
                if(a.gridSnap)anchor=MapPlacementSnap.SnapPosition(anchor,a.gridStep);anchor.z=0;
                if(h.Graph&&h.SourceIsBody&&h.Source!=null)
                {
                    Undo.RecordObject(h,"시작 상자 연결점 이동");var half=h.Source.Read().size*.5f;var local=(Vector2)anchor-(Vector2)h.Source.transform.position;
                    h.SourceLocalAnchor=new Vector2(Mathf.Clamp(local.x,-half.x,half.x),Mathf.Clamp(local.y,-half.y,half.y));
                }
                else {Undo.RecordObject(h.transform,"로프 고정점 이동");h.transform.position=anchor;}
                MapRopeTools.Mark(a);
            }
            Handles.Label(h.Anchor+Vector2.up*.2f,h.Graph&&h.SourceIsBody?"시작 상자 연결점":"고정점");
            if(h.Target==null)return;
            EditorGUI.BeginChangeCheck();var connection=Handles.PositionHandle(h.Connection,Quaternion.identity);
            if(EditorGUI.EndChangeCheck())
            {
                if(a.gridSnap)connection=MapPlacementSnap.SnapPosition(connection,a.gridStep);
                var local=(Vector2)(connection-h.Target.transform.position);var half=h.Target.Read().size*.5f;
                local=new Vector2(Mathf.Clamp(local.x,-half.x,half.x),Mathf.Clamp(local.y,-half.y,half.y));
                Undo.RecordObject(h,"로프 상자 연결점 이동");h.LocalAnchor=local;MapRopeTools.Mark(a);
            }
            Handles.Label(h.Connection+Vector2.up*.2f,"상자 연결점");
            if(!h.Graph && h.Mode==RopeBreakMode.Connector)Handles.Label(h.ConnectorPoint+Vector2.up*(h.ConnectorSize*.5f+.1f),"파괴 연결부");
        }
    }
}
