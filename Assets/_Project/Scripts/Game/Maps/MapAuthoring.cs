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
        [Header("보상 맵(카드 선택)")]
        [Tooltip("비석 상자 묶음. 보상 맵을 불러오면 자동으로 만듭니다.")] public Transform rewardRoot;
        [Tooltip("패자 자리(발 위치) 표시. 보상 맵을 불러오면 자동으로 만듭니다.")] public Transform rewardStand;
        [Tooltip("비석 위에 실제 모놀리스 모습을 겹쳐 보여 줍니다(저장되지 않음).")] public bool rewardPreview = true;
        [Range(-1, 2), Tooltip("미리보기에서 켤 선택지(화면 왼쪽부터 0, 1, 2). -1이면 모두 꺼짐")] public int rewardPreviewHover = -1;
        [Tooltip("P2가 고를 때처럼 좌우를 뒤집어 봅니다.")] public bool rewardPreviewMirror;
        [SerializeField] string loadedId;
        [SerializeField] uint loadedGeometry;
        [SerializeField] bool loadedReward;
        public string LoadedId=>loadedId;
        public uint LoadedGeometry=>loadedGeometry;
        /// <summary>지금 씬에 보상 맵 비석·패자 자리를 불러왔는지. 이때만 맵 저장이 비석을 함께 저장한다.</summary>
        public bool LoadedReward=>loadedReward;
        public MapBoxHandle[] RewardHandles()=>rewardRoot==null?Array.Empty<MapBoxHandle>():rewardRoot.GetComponentsInChildren<MapBoxHandle>(true);
        /// <summary>비석 상자 핸들을 보상 맵 비석으로 읽는다. 순서는 저장할 때 화면 왼쪽부터 정한다.</summary>
        public RewardStele[] ReadSteles()
        {
            var handles=RewardHandles();var steles=new RewardStele[handles.Length];
            for(int i=0;i<handles.Length;i++)steles[i]=RewardStele.From(handles[i].Read());
            return steles;
        }
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
            if(loadedReward)
            {
                if(rewardRoot==null||rewardStand==null)errors.Add("보상 맵 비석 묶음·패자 자리가 없습니다. 맵을 다시 불러와주세요.");
                else
                {
                    if(rewardRoot==terrainRoot||rewardRoot==physicsRoot||rewardRoot==ropeRoot||rewardRoot.IsChildOf(terrainRoot)
                        ||rewardRoot.position!=Vector3.zero||rewardRoot.rotation!=Quaternion.identity||rewardRoot.lossyScale!=Vector3.one)
                        errors.Add("비석 묶음은 지형과 별도로 위치 0·회전 0·크기 1을 유지해주세요.");
                    foreach(Transform child in rewardRoot)if(child.GetComponent<MapBoxHandle>()==null)errors.Add("등록되지 않은 비석: "+child.name+" — 비석 추가 버튼을 사용해주세요.");
                    foreach(var h in RewardHandles())
                        if(h.transform.parent!=rewardRoot||Quaternion.Angle(h.transform.localRotation,Quaternion.identity)>.001f||h.transform.localPosition.z!=0||h.transform.localScale.z!=1)
                            errors.Add("비석은 비석 묶음 바로 아래, 회전 없이 XY 평면에 배치해주세요: "+h.name);
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
        public void MarkRewardLoaded(bool loaded)=>loadedReward=loaded;
#endif
    }
}
