using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>맵 중심 x=0의 반대쪽에 현재 배치의 사본을 만든다. 원본과 정의 에셋은 변경하지 않는다.</summary>
    public static class MapMirrorTools
    {
        public sealed class Result
        {
            public readonly List<MapBoxHandle> Created=new List<MapBoxHandle>();
            public int Skipped { get; internal set; }
            public string Message=>Created.Count+"개 대칭 복사 · 같은 위치의 지형 "+Skipped+"개 건너뜀. 맵 저장으로 반영하세요.";
        }

        public static Result CopySelection(MapAuthoring authoring)
        {
            var result=Copy(authoring,Selection.gameObjects.Select(go=>go.GetComponent<MapBoxHandle>()));
            if(result.Created.Count>0)Selection.objects=result.Created.Select(h=>(UnityEngine.Object)h.gameObject).ToArray();
            return result;
        }

        public static Result Copy(MapAuthoring authoring,IEnumerable<MapBoxHandle> selection)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("게임 실행을 마친 뒤 배치해주세요.");
            if(authoring==null)throw new InvalidOperationException("MapAuthoring의 지형을 선택해주세요.");
            var sources=selection?.Distinct().ToArray();
            if(sources==null||sources.Length==0)throw new InvalidOperationException("좌우 대칭 복사할 지형을 선택해주세요.");
            foreach(var source in sources)
                if(source==null||source.transform.parent!=authoring.terrainRoot)
                    throw new InvalidOperationException("같은 맵의 지형만 선택해주세요. 시작 위치나 다른 오브젝트는 복사하지 않습니다.");
            var errors=authoring.ValidatePlacement();
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));

            // Selection order must not change explicit terrain order. Prepare every candidate
            // before changing the scene, so invalid selection cannot leave a partial copy.
            sources=sources.OrderBy(h=>h.Order).ToArray();
            var existing=authoring.ReadOrderedBoxes().ToList();
            var pending=new List<(MapBoxHandle source,MapBox bounds)>();
            var result=new Result();
            foreach(var source in sources)
            {
                var box=source.Read();
                if(!MapDefinition.Finite(box.minX)||!MapDefinition.Finite(box.maxX)
                    ||!MapDefinition.Finite(box.minY)||!MapDefinition.Finite(box.maxY)
                    ||box.maxX<=box.minX||box.maxY<=box.minY)
                    throw new InvalidOperationException("위치와 크기가 올바른 지형만 복사할 수 있습니다: "+source.name);
                float min=box.minX;box.minX=-box.maxX;box.maxX=-min;
                if(existing.Any(other=>SameShape(other,box))){result.Skipped++;continue;}
                box.id="box_"+Guid.NewGuid().ToString("N");
                existing.Add(box);pending.Add((source,box));
            }
            if(pending.Count==0)return result;

            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("지형 좌우 대칭 복사");
            int order=existing.Count-pending.Count;
            try
            {
                foreach(var item in pending)
                {
                    var copy=UnityEngine.Object.Instantiate(item.source.gameObject,authoring.terrainRoot,false);
                    var handle=copy.GetComponent<MapBoxHandle>();handle.Initialize(item.bounds,order++);
                    Undo.RegisterCreatedObjectUndo(copy,"지형 좌우 대칭 복사");
                    result.Created.Add(handle);
                }
                Undo.CollapseUndoOperations(group);
                MapPlacementSnap.ResetTracking();
                if(!EditorSceneManager.IsPreviewScene(authoring.gameObject.scene))
                    EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
                return result;
            }
            catch { Undo.RevertAllDownToGroup(group);throw; }
        }

        static bool SameShape(MapBox a,MapBox b)=>a.kind==b.kind&&a.minX==b.minX&&a.maxX==b.maxX&&a.minY==b.minY&&a.maxY==b.maxY;
    }
}
