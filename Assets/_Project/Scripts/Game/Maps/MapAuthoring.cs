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
        public Transform physicsRoot;
        public Transform ropeRoot;
        public Knockbox.Presentation.ShapeLibrary shapes;
        public Knockbox.Presentation.PaletteAsset palette;
        [Header("배치 스냅")]
        public bool gridSnap = true;
        [Min(.001f)] public float gridStep = .25f;
        public bool boxTopSnap = true;
        [Min(.01f)] public float boxTopSnapDistance = .6f;
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
            var boxes=terrainRoot.GetComponentsInChildren<MapBoxHandle>(true);var orders=new HashSet<int>();bool invalidOrder=false;
            foreach(Transform child in terrainRoot)
                if(child.GetComponent<MapBoxHandle>()==null)errors.Add("등록되지 않은 지형: "+child.name+" — 사각 지형 추가 버튼을 사용해주세요.");
            foreach(var box in boxes)
            {
                if(box.transform.parent!=terrainRoot)errors.Add("도형은 지형 묶음 바로 아래에 배치해주세요.");
                if(!orders.Add(box.Order)||box.Order<0||box.Order>=boxes.Length)invalidOrder=true;
                if(Quaternion.Angle(box.transform.localRotation,Quaternion.identity)>.001f||box.transform.localPosition.z!=0||box.transform.localScale.z!=1)errors.Add("회전 없이 XY 평면에 배치해주세요: "+box.name);
            }
            if(invalidOrder)errors.Add("지형 번호에 빈칸 또는 중복이 있습니다. 도형 순서 정리를 눌러주세요.");
            if(physicsRoot==null && definition.PhysicalBoxCount>0)errors.Add("물리 상자 묶음이 없습니다. 맵을 다시 불러와주세요.");
            if(physicsRoot!=null)
            {
                if(physicsRoot==terrainRoot||physicsRoot.position!=Vector3.zero||physicsRoot.rotation!=Quaternion.identity||physicsRoot.lossyScale!=Vector3.one)
                    errors.Add("물리 상자 묶음은 별도 위치 0·회전 0·크기 1을 유지해주세요.");
                var handles=physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true);var physicalOrders=new HashSet<int>();bool invalidPhysicalOrder=false;
                foreach(Transform child in physicsRoot)if(child.GetComponent<MapPhysicalBoxHandle>()==null)errors.Add("등록되지 않은 물리 상자: "+child.name);
                foreach(var box in handles)
                {
                    if(box.transform.parent!=physicsRoot)errors.Add("물리 상자는 묶음 바로 아래에 배치해주세요.");
                    if(!physicalOrders.Add(box.Order)||box.Order<0||box.Order>=handles.Length)invalidPhysicalOrder=true;
                    if(Quaternion.Angle(box.transform.localRotation,Quaternion.identity)>.001f||box.transform.localPosition.z!=0||box.transform.localScale.z!=1)
                        errors.Add("물리 상자는 회전 없이 XY 평면에 배치해주세요: "+box.name);
                }
                if(invalidPhysicalOrder)errors.Add("물리 상자 번호에 빈칸 또는 중복이 있습니다. 도형 순서 정리를 눌러주세요.");
            }
            if(ropeRoot==null&&definition.RopeCount>0)errors.Add("로프 묶음이 없습니다. 맵을 다시 불러와주세요.");
            if(ropeRoot!=null)
            {
                if(ropeRoot==physicsRoot||ropeRoot==terrainRoot||ropeRoot.position!=Vector3.zero||ropeRoot.rotation!=Quaternion.identity||ropeRoot.lossyScale!=Vector3.one)
                    errors.Add("로프는 별도 위치 0·회전 0·크기 1 묶음을 사용해주세요.");
                var ropes=ropeRoot.GetComponentsInChildren<MapRopeHandle>(true);var ropeOrders=new HashSet<int>();
                foreach(Transform child in ropeRoot)if(child.GetComponent<MapRopeHandle>()==null)errors.Add("등록되지 않은 로프 오브젝트: "+child.name);
                foreach(var rope in ropes)
                {
                    if(rope.transform.parent!=ropeRoot)errors.Add("로프는 묶음 바로 아래에 배치해주세요.");
                    if(!ropeOrders.Add(rope.Order)||rope.Order<0||rope.Order>=ropes.Length)
                        errors.Add("로프 번호에 빈칸 또는 중복이 있습니다. 도형 순서 정리를 눌러주세요.");
                    if(rope.Target==null||rope.Target.transform.parent!=physicsRoot)errors.Add("로프의 연결 상자를 선택하거나 해당 로프를 삭제해주세요: "+rope.name);
                    if(rope.Graph && rope.SourceIsBody && (rope.Source==null||rope.Source.transform.parent!=physicsRoot))
                        errors.Add("로프 시작 상자를 지정하거나 시작점을 고정점으로 바꿔주세요: "+rope.name);
                    if(rope.transform.rotation!=Quaternion.identity||rope.transform.localScale!=Vector3.one||rope.transform.position.z!=0)
                        errors.Add("로프 고정점은 회전·크기 변경 없이 XY로 이동해주세요: "+rope.name);
                }
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
        public MapPhysicalBox[] ReadOrderedPhysicalBoxes()
        {
            var errors=ValidatePlacement();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            if(physicsRoot==null)return Array.Empty<MapPhysicalBox>();
            var handles=physicsRoot.GetComponentsInChildren<MapPhysicalBoxHandle>(true);var boxes=new MapPhysicalBox[handles.Length];
            foreach(var handle in handles)boxes[handle.Order]=handle.Read();return boxes;
        }
        public MapRope[] ReadOrderedRopes()
        {
            var errors=ValidatePlacement();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            if(ropeRoot==null)return Array.Empty<MapRope>();
            var handles=ropeRoot.GetComponentsInChildren<MapRopeHandle>(true);var ropes=new MapRope[handles.Length];
            foreach(var h in handles)ropes[h.Order]=h.Read();return ropes;
        }
#if UNITY_EDITOR
        public void MarkLoaded(){loadedId=definition.Id;loadedGeometry=definition.ToArena().ComputeGeometryChecksum();}
#endif
    }
}
