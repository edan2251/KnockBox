using System;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 경기에 쓸 맵 해석: 제작기 시험 맵 → 등록 목록(MapCatalog) → 옛 txt/json 맵.
    /// 직렬화 필드(arenaFiles, arenaMetaFiles, mapCatalog, selectedMap, arenaName)는 MatchRunner.cs에 있다.
    /// </summary>
    public sealed partial class MatchRunner
    {
        // 시험 정의는 실행 중에만 연결하며 Main과 GameMaps에 저장하지 않는다.
        Maps.MapDefinition authoringPreview;
        public bool IsAuthoringPreview => authoringPreview != null;

#if UNITY_EDITOR
        /// <summary>저장한 정의 하나만 시험한다. 등록 목록과 Main의 저장 참조는 바꾸지 않는다.</summary>
        public void ConfigureAuthoringPreview(Maps.MapDefinition definition)
        {
            if (definition == null) throw new InvalidOperationException("시험할 맵 정의를 찾을 수 없습니다.");
            if (Lockstep != null || OnlineRematch) throw new InvalidOperationException("맵 시험은 로컬 연습에서만 가능합니다.");
            var errors = definition.Validate(CreateConfig().Movement.BodySize);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            authoringPreview = definition;
        }
#endif

        /// <summary>로컬 시작과 온라인 핸드셰이크가 같은 검증을 거쳐 경기별 맵 스냅샷을 만든다.</summary>
        public ArenaDef[] PrepareMatchArenas(SimConfig config)
        {
            if (IsAuthoringPreview)
            {
                var errors = authoringPreview.Validate(config.Movement.BodySize);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                return new[] { authoringPreview.ToArena() };
            }
            // 등록 순환은 CreateRotation이 같은 몸 크기로 모든 맵을 검증한다.
            if (arenaName == RegisteredMapsName && mapCatalog != null)
                return mapCatalog.CreateRotation(config.Movement.BodySize);
            var arenas = ResolveArenas(arenaName);
            foreach (var arena in arenas)
            {
                var map = FindDefinition(arena.Name);
                if (map == null) continue;
                var errors = map.Validate(config.Movement.BodySize);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            }
            return arenas;
        }

        /// <summary>RotationName이면 순환 맵 전부(이름순), 아니면 그 맵 하나.</summary>
        public ArenaDef[] ResolveArenas(string name)
        {
            if (name == RegisteredMapsName)
            {
                if(mapCatalog==null)throw new InvalidOperationException("게임 맵 등록 목록이 연결되지 않았습니다.");
                return mapCatalog.CreateRotation();
            }
            if (name != RotationName)
                return new[] { ResolveArena(name) };
            string[] names = RotationNames();
            if (names.Length == 0)
                throw new InvalidOperationException("순환에 들어간 맵이 없음(짝 파일의 \"rotation\": true)");
            var arenas = new ArenaDef[names.Length];
            for (int i = 0; i < names.Length; i++)
                arenas[i] = ResolveArena(names[i]);
            return arenas;
        }

        /// <summary>짝 파일에서 "rotation": true인 맵 이름(이름순).</summary>
        public string[] RotationNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (string name in MetaNames())
            {
                if (GetMeta(name).Rotation)
                    names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        /// <summary>F2 순환: Rotation → 순환 맵 → 나머지 맵(v1 테스트 고정 자료).</summary>
        public string[] ArenaChoices()
        {
            var choices = new System.Collections.Generic.List<string> { RotationName };
            if(mapCatalog!=null && mapCatalog.Maps.Count>0)choices.Add(RegisteredMapsName);
            choices.AddRange(RotationNames());
            var rest = new System.Collections.Generic.List<string>();
            if (arenaFiles != null)
            {
                foreach (var file in arenaFiles)
                {
                    if (file != null && !choices.Contains(file.name))
                        rest.Add(file.name);
                }
            }
            rest.Sort(StringComparer.Ordinal);
            choices.AddRange(rest);
            if(mapCatalog!=null)foreach(var map in mapCatalog.Maps)
                if(map!=null&&!choices.Contains(map.MatchName))choices.Add(map.MatchName);
            return choices.ToArray();
        }

        readonly System.Collections.Generic.Dictionary<string, ArenaMeta> metaCache = new System.Collections.Generic.Dictionary<string, ArenaMeta>();

        /// <summary>맵 짝 파일. 없으면 ArenaMeta.Default.</summary>
        public ArenaMeta GetMeta(string name)
        {
            var definition = FindDefinition(name);
            return definition != null ? definition.Metadata() : GetLegacyMeta(name);
        }

        /// <summary>새 맵 정의 찾기: 제작기 시험 맵 → 저장된 정의. 옛 txt/json 맵이면 null.</summary>
        Maps.MapDefinition FindDefinition(string name)
        {
            if (IsAuthoringPreview && name == authoringPreview.MatchName) return authoringPreview;
            return FindSavedDefinition(name);
        }

        /// <summary>등록 목록 → 등록 목록이 비었을 때 쓰는 selectedMap.</summary>
        Maps.MapDefinition FindSavedDefinition(string name)
        {
            var definition = mapCatalog != null ? mapCatalog.Find(name) : null;
            if (definition == null && selectedMap != null && selectedMap.MatchName == name) definition = selectedMap;
            return definition;
        }

        ArenaMeta GetLegacyMeta(string name)
        {
            if (metaCache.TryGetValue(name, out var cached))
                return cached;
            ArenaMeta meta = null;
            if (arenaMetaFiles != null)
            {
                foreach (var file in arenaMetaFiles)
                {
                    if (file != null && file.name == name)
                        meta = ArenaMeta.Parse(file.text, name + ".json");
                }
            }
#if UNITY_EDITOR
            if (meta == null)
            {
                string path = System.IO.Path.Combine(ArenaFiles.Folder, name + ".json");
                if (System.IO.File.Exists(path))
                    meta = ArenaMeta.Parse(System.IO.File.ReadAllText(path), name + ".json");
            }
#endif
            meta ??= ArenaMeta.Default;
            metaCache[name] = meta;
            return meta;
        }

        System.Collections.Generic.IEnumerable<string> MetaNames()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            if (arenaMetaFiles != null)
            {
                foreach (var file in arenaMetaFiles)
                {
                    if (file != null && seen.Add(file.name))
                        yield return file.name;
                }
            }
#if UNITY_EDITOR
            foreach (string name in ArenaFiles.ListMetaNames())
            {
                if (seen.Add(name))
                    yield return name;
            }
#endif
        }

        /// <summary>등록된 맵 텍스트에서 찾고, 에디터에서는 없으면 디스크에서 읽는다.</summary>
        public ArenaDef ResolveArena(string name)
        {
            var definition = FindDefinition(name);
            return definition != null ? definition.ToMatchArena() : ResolveRecordedArena(name);
        }

        /// <summary>
        /// 기록 재생용: 같은 이름이면 옛 txt/json을 등록 목록보다 먼저 쓴다(옛 기록의 원래 맵).
        /// 새 경기(ResolveArena)는 등록 목록이 먼저다. 이 순서 차이는 리플레이 호환을 위해 유지한다.
        /// </summary>
        public ArenaDef ResolveRecordedArena(string name)
        {
            if (IsAuthoringPreview && name == authoringPreview.MatchName) return authoringPreview.ToArena();
            if (arenaFiles != null)
            {
                foreach (var file in arenaFiles)
                {
                    if (file != null && file.name == name)
                        return ArenaFiles.WithMeta(ArenaFiles.Parse(file.text, name), GetLegacyMeta(name));
                }
            }
            var definition = FindSavedDefinition(name);
            if (definition != null) return definition.ToArena();
#if UNITY_EDITOR
            return ArenaFiles.LoadByName(name);
#else
            throw new InvalidOperationException($"MatchRunner에 등록되지 않은 맵: {name}");
#endif
        }
    }
}
