using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    [CreateAssetMenu(menuName="Knockbox/맵/등록 목록")]
    public sealed class MapCatalog : ScriptableObject
    {
        [SerializeField] List<MapDefinition> maps=new List<MapDefinition>();
        public IReadOnlyList<MapDefinition> Maps=>maps;
        public List<string> Validate(float bodySize=.72f)
        {
            var errors=new List<string>();var ids=new HashSet<string>(StringComparer.Ordinal);
            var names=new HashSet<string>(StringComparer.Ordinal);
            if(maps.Count>ArenaRotation.MaxArenas)errors.Add("현재 라운드 순환은 최대 "+ArenaRotation.MaxArenas+"개 맵을 지원합니다. 등록 목록을 줄여주세요.");
            foreach(var map in maps)
            {
                if(map==null){errors.Add("등록 목록에 비어 있는 맵이 있습니다.");continue;}
                if(!ids.Add(map.Id)||!names.Add(map.MatchName))errors.Add("맵 내부 ID 또는 이름이 중복됩니다: "+map.Id);
                if(map.MatchName.IndexOf(InputRecording.ArenaSeparator)>=0)errors.Add("맵 내부 ID에 쉼표를 넣을 수 없습니다. 입력 기록의 맵 구분자입니다: "+map.Id);
                errors.AddRange(map.Validate(bodySize));
            }
            return errors;
        }
        /// <summary>Inspector 등록 순서와 무관한 고정 ID 순서. 경기 시작 시 정의를 판정 스냅샷으로 만든다.</summary>
        public ArenaDef[] CreateRotation(float bodySize=.72f)
        {
            var errors=Validate(bodySize);
            if(maps.Count==0)errors.Add("등록된 맵이 없습니다. 게임 맵 목록에 맵을 등록해주세요.");
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            var ordered=new List<MapDefinition>(maps);
            ordered.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            var arenas=new ArenaDef[ordered.Count];
            for(int i=0;i<arenas.Length;i++)
            {
                arenas[i]=ordered[i].ToArena();
                if(ordered[i].PreservedBaseline&&arenas[i].ComputeChecksum()!=0x483E0206u)
                    throw new InvalidOperationException("보존된 Brawl 원본이 바뀌었습니다. 시험용 사본을 사용해주세요.");
            }
            return arenas;
        }
        public MapDefinition Find(string matchName)
        {
            MapDefinition found=null;
            foreach(var map in maps)if(map!=null && map.MatchName==matchName)
            {
                if(found!=null)throw new InvalidOperationException("중복 등록된 맵: "+matchName);
                found=map;
            }
            return found;
        }
#if UNITY_EDITOR
        public void Register(MapDefinition map)
        {
            if(map==null)throw new ArgumentNullException(nameof(map));
            foreach(var existing in maps)
            {
                if(existing==map)return;
                if(existing!=null&&(existing.Id==map.Id||existing.MatchName==map.MatchName))throw new InvalidOperationException("맵 내부 ID 또는 이름이 중복됩니다: "+map.Id);
            }
            if(maps.Count>=ArenaRotation.MaxArenas)throw new InvalidOperationException("현재 라운드 순환은 최대 "+ArenaRotation.MaxArenas+"개 맵을 지원합니다.");
            if(map.MatchName.IndexOf(InputRecording.ArenaSeparator)>=0)throw new InvalidOperationException("맵 내부 ID에는 쉼표를 넣을 수 없습니다.");
            if(map.Validate().Count!=0)throw new InvalidOperationException(string.Join("\n",map.Validate()));
            maps.Add(map);
        }
#endif
    }
}
