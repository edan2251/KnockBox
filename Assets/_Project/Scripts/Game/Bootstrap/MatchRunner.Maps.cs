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
        // 로비·결과 공간 맵 시험(CP4): 경기 맵 목록은 그대로 두고 이 실행의 로비·결과 공간 맵만 바꾼다.
        Maps.MapDefinition spacePreview;
        public bool IsAuthoringPreview => authoringPreview != null || spacePreview != null;
        /// <summary>제작기 시험 중인 로비·결과 공간 맵의 용도(아니면 Match).</summary>
        public Maps.MapUsage PreviewUsage => spacePreview != null ? spacePreview.Usage : Maps.MapUsage.Match;

#if UNITY_EDITOR
        /// <summary>
        /// 저장한 정의 하나만 시험한다. 등록 목록과 Main의 저장 참조는 바꾸지 않는다.
        /// 로비·결과 공간 맵은 이 실행에서만 그 맵을 쓰고(Play 종료 시 원래 참조로 돌아감), 경기 맵은 등록 목록 그대로다.
        /// </summary>
        public void ConfigureAuthoringPreview(Maps.MapDefinition definition)
        {
            if (definition == null) throw new InvalidOperationException("시험할 맵 정의를 찾을 수 없습니다.");
            if (Lockstep != null || OnlineRematch) throw new InvalidOperationException("맵 시험은 로컬 연습에서만 가능합니다.");
            if (definition.Usage == Maps.MapUsage.Reward) throw new InvalidOperationException("보상 맵은 경기에서 세트를 진 뒤 확인합니다.");
            var errors = definition.Validate(CreateConfig().Movement.BodySize);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (definition.IsSpaceMap)
            {
                spacePreview = definition;
                if (definition.Usage == Maps.MapUsage.Results) resultsMap = definition;
                else lobbyMap = definition;
                return;
            }
            authoringPreview = definition;
        }
#endif

        /// <summary>
        /// 로컬 시작과 온라인 핸드셰이크가 같은 검증을 거쳐 경기별 맵 스냅샷을 만든다.
        /// 결과 공간 설정이면 경기 맵 뒤에 결과 공간 맵을 붙인다(온라인 맵 목록·기록에 같은 순서로 들어간다).
        /// </summary>
        public ArenaDef[] PrepareMatchArenas(SimConfig config)
        {
            var arenas = PrepareRotationArenas(config);
            if (!config.Match.ResultsSpace && !config.Match.LobbySpace) return arenas;
            var all = new System.Collections.Generic.List<ArenaDef>(arenas);
            if (config.Match.ResultsSpace) all.Add(PrepareSpaceArena(resultsMap, Maps.MapUsage.Results, "결과 공간", config));
            if (config.Match.LobbySpace) all.Add(PrepareSpaceArena(lobbyMap, Maps.MapUsage.Lobby, "로비", config));
            return all.ToArray();
        }

        /// <summary>결과 공간·로비 맵: 맞는 용도로 지정되고 물리 상자가 없는 맵 정의여야 한다(CP4).</summary>
        static ArenaDef PrepareSpaceArena(Maps.MapDefinition map, Maps.MapUsage usage, string label, SimConfig config)
        {
            if (map == null) throw new InvalidOperationException(label + " 맵이 연결되지 않았습니다.");
            if (map.Usage != usage) throw new InvalidOperationException(map.name + "의 맵 용도가 " + map.Usage + "입니다. MapAuthoring에서 " + label + " 용도로 지정해주세요.");
            var errors = map.Validate(config.Movement.BodySize);
            if (errors.Count > 0) throw new InvalidOperationException(map.name + ": " + string.Join("\n", errors));
            var arena = map.ToArena();
            // 결과 공간은 점수 숫자 발판(2026-11-20)의 물리 상자·로프를 허용한다. 로비는 그대로 금지.
            if (arena.PropCount != 0 && usage != Maps.MapUsage.Results) throw new InvalidOperationException(map.name + ": " + label + " 맵에는 물리 상자를 두지 않습니다.");
            return arena;
        }

        ArenaDef[] PrepareRotationArenas(SimConfig config)
        {
            if (authoringPreview != null)
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
            if (authoringPreview != null && name == authoringPreview.MatchName) return authoringPreview;
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
            if (authoringPreview != null && name == authoringPreview.MatchName) return authoringPreview.ToArena();
            if (resultsMap != null && name == resultsMap.MatchName) return resultsMap.ToArena();
            if (lobbyMap != null && name == lobbyMap.MatchName) return lobbyMap.ToArena();
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
