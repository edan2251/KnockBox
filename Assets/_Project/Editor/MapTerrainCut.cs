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
    /// <summary>Editor-only half-plane clipping. The definition remains unchanged until explicit map save.</summary>
    [InitializeOnLoad]
    public static class MapTerrainCut
    {
        static MapBoxHandle active;
        static Vector2? first,second;
        static bool keepLeft=true;
        static string warning;
        static MapTerrainCut(){Selection.selectionChanged+=CheckSelection;Undo.undoRedoPerformed+=Cancel;}
        public static bool IsActive(MapBoxHandle handle)=>active==handle&&handle!=null;
        static void CheckSelection(){if(active!=null&&Selection.activeGameObject!=active.gameObject)Cancel();}
        public static void Begin(MapBoxHandle handle)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("시험을 끝낸 뒤 절단해주세요.");
            if(handle==null||handle.Kind!=TerrainKind.Solid)throw new InvalidOperationException("일반 고정 지형 하나를 선택해주세요.");
            active=handle;first=second=null;keepLeft=true;warning=null;SceneView.RepaintAll();
        }
        public static void Cancel(){active=null;first=second=null;warning=null;SceneView.RepaintAll();}
        public static Vector2[] Boundary(MapBox b)=>b.IsPolygon?(Vector2[])b.vertices.Clone():new[]{new Vector2(b.minX,b.minY),new Vector2(b.maxX,b.minY),new Vector2(b.maxX,b.maxY),new Vector2(b.minX,b.maxY)};
        public static Vector2 ClosestBoundary(MapBox b,Vector2 point)
        {
            var vertices=Boundary(b);Vector2 nearest=vertices[0];float distance=float.MaxValue;
            for(int i=0;i<vertices.Length;i++)
            {
                var a=vertices[i];var edge=vertices[(i+1)%vertices.Length]-a;float t=Mathf.Clamp01(Vector2.Dot(point-a,edge)/edge.sqrMagnitude);
                var p=a+edge*t;float d=(p-point).sqrMagnitude;if(d<distance){nearest=p;distance=d;}
            }
            return nearest;
        }
        public static MapBox Preview(MapBox box,Vector2 a,Vector2 b,bool left)
        {
            if(box.kind!=TerrainKind.Solid)throw new InvalidOperationException("일반 고정 지형만 절단할 수 있습니다.");
            if((ClosestBoundary(box,a)-a).sqrMagnitude>1e-7f||(ClosestBoundary(box,b)-b).sqrMagnitude>1e-7f)
                throw new InvalidOperationException("두 점은 도형 경계 위에 있어야 합니다.");
            if((b-a).sqrMagnitude<1e-6f)throw new InvalidOperationException("서로 다른 두 점을 선택해주세요.");
            var source=Boundary(box);var result=new List<Vector2>();var direction=b-a;
            Func<Vector2,float> distance=p=>(left?1:-1)*(direction.x*(p.y-a.y)-direction.y*(p.x-a.x));
            for(int i=0;i<source.Length;i++)
            {
                var p=source[i];var q=source[(i+1)%source.Length];float dp=distance(p),dq=distance(q);
                bool pin=dp>=-1e-6f,qin=dq>=-1e-6f;
                if(pin)result.Add(p);
                if(pin!=qin)result.Add(p+(q-p)*(dp/(dp-dq)));
            }
            // Remove duplicate / collinear vertices; retain all untouched float bits.
            bool changed=true;
            while(changed&&result.Count>=3)
            {
                changed=false;
                for(int i=0;i<result.Count;i++)
                {
                    var prev=result[(i+result.Count-1)%result.Count];var p=result[i];var next=result[(i+1)%result.Count];
                    if((p-prev).sqrMagnitude<1e-8f||Mathf.Abs((p.x-prev.x)*(next.y-p.y)-(p.y-prev.y)*(next.x-p.x))<1e-7f)
                    {result.RemoveAt(i);changed=true;break;}
                }
            }
            if(result.Count<3||result.Count>ConvexPolygon.MaxVertices)throw new InvalidOperationException("남길 영역이 너무 작거나 꼭짓점이 32개를 넘습니다.");
            float area=0;for(int i=0;i<result.Count;i++)area+=result[i].x*result[(i+1)%result.Count].y-result[i].y*result[(i+1)%result.Count].x;
            if(area<.0001f)throw new InvalidOperationException("남길 영역이 너무 작습니다.");
            int start=0;for(int i=1;i<result.Count;i++)if(result[i].x<result[start].x||result[i].x==result[start].x&&result[i].y<result[start].y)start=i;
            var vertices=new Vector2[result.Count];for(int i=0;i<vertices.Length;i++)vertices[i]=result[(i+start)%result.Count];
            box.vertices=vertices;box.minX=vertices.Min(p=>p.x);box.maxX=vertices.Max(p=>p.x);box.minY=vertices.Min(p=>p.y);box.maxY=vertices.Max(p=>p.y);
            box.ToBox();return box;
        }
        public static void Apply(MapBoxHandle handle,Vector2 a,Vector2 b,bool left)
        {
            var next=Preview(handle.Read(),a,b,left);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("지형 두 점 절단");
            try
            {
                Undo.RecordObjects(new UnityEngine.Object[]{handle,handle.transform,handle.GetComponent<SpriteRenderer>()},"지형 두 점 절단");
                var visual=handle.GetComponent<ConvexTerrainSprite>();
                if(visual==null)Undo.AddComponent<ConvexTerrainSprite>(handle.gameObject);else Undo.RecordObject(visual,"지형 두 점 절단");
                handle.Initialize(next,handle.Order);
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
                MapPlacementSnap.ResetTracking();
                if(!EditorSceneManager.IsPreviewScene(handle.gameObject.scene))EditorSceneManager.MarkSceneDirty(handle.gameObject.scene);
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
        }
        public static void Inspector(MapBoxHandle handle)
        {
            if(!IsActive(handle))
            {
                using(new EditorGUI.DisabledScope(handle.Kind!=TerrainKind.Solid||EditorApplication.isPlaying))
                    if(GUILayout.Button("두 점 절단 시작"))Begin(handle);
                return;
            }
            EditorGUILayout.HelpBox("Scene 뷰의 경계에서 두 점을 클릭하세요. 꼭짓점·변 중간 가까이는 자동으로 맞춥니다. 초록 영역을 남기고 빨강 영역을 제거합니다. Esc: 취소",MessageType.Info);
            if(GUILayout.Button("남길 쪽 바꾸기")){keepLeft=!keepLeft;SceneView.RepaintAll();}
            if(GUILayout.Button("두 점 다시 지정")){first=second=null;warning=null;SceneView.RepaintAll();}
            MapBox preview=default;bool valid=false;
            if(first.HasValue&&second.HasValue)try{preview=Preview(handle.Read(),first.Value,second.Value,keepLeft);valid=true;warning=null;}catch(Exception e){warning=e.Message;}
            using(new EditorGUI.DisabledScope(!valid))
                if(GUILayout.Button("절단 적용")){Apply(handle,first.Value,second.Value,keepLeft);first=second=null;warning=null;}
            if(GUILayout.Button("절단 취소"))Cancel();
            if(!string.IsNullOrEmpty(warning))EditorGUILayout.HelpBox(warning,MessageType.Warning);
        }
        public static void SceneGUI(MapBoxHandle handle)
        {
            if(!IsActive(handle))return;var e=Event.current;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){Cancel();e.Use();return;}
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var box=handle.Read();var boundary=Boundary(box);
            if(first.HasValue&&second.HasValue)
            {
                DrawRegion(box,first.Value,second.Value,keepLeft,new Color(.1f,1,.3f,.22f));
                DrawRegion(box,first.Value,second.Value,!keepLeft,new Color(1,.15f,.1f,.38f));
                Handles.color=Color.white;Handles.DrawAAPolyLine(3,first.Value,second.Value);
            }
            var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if(Mathf.Abs(ray.direction.z)<1e-6f)return;
            Vector2 mouse=ray.GetPoint(-ray.origin.z/ray.direction.z);Vector2 nearest=ClosestBoundary(box,mouse);
            float nearPixels=Vector2.Distance(HandleUtility.WorldToGUIPoint(nearest),e.mousePosition);
            foreach(var v in boundary)
                if(Vector2.Distance(HandleUtility.WorldToGUIPoint(v),e.mousePosition)<12){nearest=v;nearPixels=0;break;}
            if(nearPixels>0)for(int i=0;i<boundary.Length;i++)
            {
                var mid=(boundary[i]+boundary[(i+1)%boundary.Length])*.5f;
                if(Vector2.Distance(HandleUtility.WorldToGUIPoint(mid),e.mousePosition)<10){nearest=mid;nearPixels=0;break;}
            }
            Handles.color=Color.cyan;
            if(nearPixels<16)Handles.DrawSolidDisc(nearest,Vector3.forward,HandleUtility.GetHandleSize(nearest)*.045f);
            if(first.HasValue)Handles.DrawSolidDisc(first.Value,Vector3.forward,HandleUtility.GetHandleSize(first.Value)*.045f);
            if(second.HasValue)Handles.DrawSolidDisc(second.Value,Vector3.forward,HandleUtility.GetHandleSize(second.Value)*.045f);
            if(e.type==EventType.MouseDown&&e.button==0&&!e.alt&&nearPixels<16&&!second.HasValue)
            {if(!first.HasValue)first=nearest;else second=nearest;e.Use();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();}
            if(e.type==EventType.MouseMove)SceneView.RepaintAll();
        }
        static void DrawRegion(MapBox box,Vector2 a,Vector2 b,bool left,Color color)
        {
            try{var v=Preview(box,a,b,left).vertices;Handles.color=color;Handles.DrawAAConvexPolygon(Array.ConvertAll(v,p=>(Vector3)p));}
            catch(ArgumentException){}catch(InvalidOperationException){}
        }
    }
}
