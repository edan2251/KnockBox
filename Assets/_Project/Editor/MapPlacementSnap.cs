using System.Collections.Generic;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>선택한 배치를 움직였을 때만 적용한다. 씬 열기/저장은 좌표를 반올림하지 않는다.</summary>
    [InitializeOnLoad]
    public static class MapPlacementSnap
    {
        struct Pose { public Vector3 position, scale; }
        static readonly Dictionary<int, Pose> previous = new Dictionary<int, Pose>();
        static MapPlacementSnap() { EditorApplication.update += FollowSelection; Undo.undoRedoPerformed += ResetTracking; }
        public static float Grid(float value, float step) => Mathf.Round(value / Mathf.Max(.001f, step)) * Mathf.Max(.001f, step);

        public static Vector3 SnapPosition(Vector3 p, float step)
        { return new Vector3(Grid(p.x,step),Grid(p.y,step),p.z); }

        // Upright fighters spawn with a horizontal foot. Only a safe top edge can support it.
        public static bool TrySnapFoot(MapAuthoring authoring, Vector3 position, out Vector3 snapped)
        {
            snapped=position;float best=authoring.spawnSnapDistance;bool found=false;
            const float halfBody=.36f;
            var handles=authoring.terrainRoot.GetComponentsInChildren<MapBoxHandle>();
            foreach(var handle in handles)
            {
                var box=handle.Read();
                if(box.kind==TerrainKind.Spike || box.maxX-box.minX<halfBody*2)continue;
                var candidate=new Vector3(Mathf.Clamp(position.x,box.minX+halfBody,box.maxX-halfBody),box.maxY,0);
                float distance=Vector3.Distance(position,candidate);
                if(distance>best)continue;
                bool blocked=false;
                foreach(var other in handles)
                {
                    var b=other.Read();
                    if(b.kind==TerrainKind.OneWay)continue;
                    if(candidate.x+halfBody>b.minX+.0001f && candidate.x-halfBody<b.maxX-.0001f && candidate.y+.72f>b.minY+.0001f && candidate.y<b.maxY-.0001f){blocked=true;break;}
                }
                if(blocked)continue;
                best=distance;snapped=candidate;found=true;
            }
            return found;
        }
        public static void Apply(MapAuthoring a, Transform t)
        {
            bool spawn=t==a.spawn0||t==a.spawn1;
            var box=t.GetComponent<MapBoxHandle>();
            if(!spawn && (box==null||t.parent!=a.terrainRoot))return;
            Vector3 p=t.localPosition,s=t.localScale;
            if(a.gridSnap)
            {
                p=SnapPosition(p,a.gridStep);
                if(!spawn){s.x=Mathf.Max(.001f,Grid(s.x,a.gridStep));s.y=Mathf.Max(.001f,Grid(s.y,a.gridStep));}
            }
            if(spawn && a.spawnEdgeSnap && TrySnapFoot(a,t.parent.TransformPoint(p),out var foot))p=t.parent.InverseTransformPoint(foot);
            if(p.Equals(t.localPosition)&&s.Equals(t.localScale))return;
            Undo.RecordObject(t,"맵 배치 스냅");t.localPosition=p;t.localScale=s;
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
        static void FollowSelection()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            if(Selection.transforms.Length==0){previous.Clear();return;}
            var seen=new HashSet<int>();
            foreach(var t in Selection.transforms)
            {
                var a=t.GetComponentInParent<MapAuthoring>();
                if(a==null||a.definition==null||a.LoadedId!=a.definition.Id)continue;
                int id=t.GetInstanceID();seen.Add(id);
                if(previous.TryGetValue(id,out var p) && (!p.position.Equals(t.localPosition)||!p.scale.Equals(t.localScale)))Apply(a,t);
                previous[id]=new Pose{position=t.localPosition,scale=t.localScale};
            }
            var stale=new List<int>();foreach(var id in previous.Keys)if(!seen.Contains(id))stale.Add(id);
            foreach(var id in stale)previous.Remove(id);
        }
        public static void ResetTracking() => previous.Clear();
        public static void DrawGrid(MapAuthoring a)
        {
            if(a==null||a.definition==null||!a.gridSnap)return;
            float step=Mathf.Max(.1f,a.gridStep);if(float.IsNaN(step)||float.IsInfinity(step))return;
            float width=a.definition.Columns*.5f,height=a.definition.Rows*.5f;
            var old=Handles.color;Handles.color=new Color(1,1,1,.12f);
            for(float x=-width;x<=width;x+=step)Handles.DrawLine(new Vector3(x,-height),new Vector3(x,height));
            for(float y=-height;y<=height;y+=step)Handles.DrawLine(new Vector3(-width,y),new Vector3(width,y));
            Handles.color=old;
        }
    }
}
