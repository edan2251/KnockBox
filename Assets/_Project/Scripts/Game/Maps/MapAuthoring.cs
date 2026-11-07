using System;
using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>Scene authoring references. Saving/registration/test launch are explicit editor commands.</summary>
    public sealed class MapAuthoring : MonoBehaviour
    {
        public MapDefinition definition;
        public MapCatalog catalog;
        public Transform terrainRoot,spawn0,spawn1;
        public Knockbox.Presentation.ShapeLibrary shapes;
        public Knockbox.Presentation.PaletteAsset palette;
        [Header("배치 스냅")]
        public bool gridSnap = true;
        [Min(.001f)] public float gridStep = .25f;
        public bool spawnEdgeSnap = true;
        [Min(.01f)] public float spawnSnapDistance = .6f;
        [SerializeField] string loadedId;
        [SerializeField] uint loadedGeometry;
        public string LoadedId=>loadedId;
        public uint LoadedGeometry=>loadedGeometry;
        public List<string> ValidatePlacement()
        {
            var errors=new List<string>();
            if(definition==null||terrainRoot==null||spawn0==null||spawn1==null){errors.Add("맵 정의·지형·양쪽 시작 위치 참조가 필요합니다.");return errors;}
            if(terrainRoot.position!=Vector3.zero||terrainRoot.rotation!=Quaternion.identity||terrainRoot.lossyScale!=Vector3.one)errors.Add("지형 묶음은 위치 0·회전 0·크기 1을 유지해주세요.");
            var boxes=terrainRoot.GetComponentsInChildren<MapBoxHandle>(true);var orders=new HashSet<int>();
            foreach(Transform child in terrainRoot)
                if(child.GetComponent<MapBoxHandle>()==null)errors.Add("등록되지 않은 지형: "+child.name+" — 사각 지형 추가 버튼을 사용해주세요.");
            foreach(var box in boxes)
            {
                if(box.transform.parent!=terrainRoot)errors.Add("도형은 지형 묶음 바로 아래에 배치해주세요.");
                if(!orders.Add(box.Order)||box.Order<0||box.Order>=boxes.Length)errors.Add("도형 순서가 중복되거나 빠졌습니다: "+box.Order);
                if(Quaternion.Angle(box.transform.localRotation,Quaternion.identity)>.001f||box.transform.localPosition.z!=0||box.transform.localScale.z!=1)errors.Add("회전 없이 XY 평면에 배치해주세요: "+box.name);
            }
            if(definition.Id!=loadedId)errors.Add("선택한 맵과 현재 배치가 다릅니다. 맵 불러오기를 눌러주세요.");
            return errors;
        }
        public MapBox[] ReadOrderedBoxes()
        {
            var errors=ValidatePlacement();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            var handles=terrainRoot.GetComponentsInChildren<MapBoxHandle>(true);var boxes=new MapBox[handles.Length];
            foreach(var handle in handles)boxes[handle.Order]=handle.Read();
            return boxes;
        }
#if UNITY_EDITOR
        public void MarkLoaded(){loadedId=definition.Id;loadedGeometry=definition.ToArena().ComputeGeometryChecksum();}
#endif
    }
}
