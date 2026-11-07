using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 맵 짝 파일 `Data/Arenas/<이름>.json`(맵 형식 v2, 04-arena.md 6절). 연출과 경기 순환만 쓴다.
    /// 시뮬레이션은 격자(`<이름>.txt`)와 구조물만 쓴다. 구조물 목록은 CP13부터 채운다.
    /// <code>{ "theme": "A", "light": [0, 25], "rotation": true, "props": [ ... ] }</code>
    /// 구조물 항목(09-props-physics.md 5절, 좌표는 경기장 중심 원점·위가 +y, 크기는 가로·세로 전체 길이):
    /// <code>
    /// { "kind": "hanging", "pos": [x, y], "size": [w, h], "anchor": [ax, ay], "mirror": true }   줄 고정점
    /// { "kind": "spinner", "pos": [x, y], "size": [w, h] }                                     축은 가운데(또는 "anchor")
    /// { "kind": "hinged",  "pos": [x, y], "size": [w, h], "anchor": [ax, ay] }                  경첩 축
    /// { "kind": "seesaw",  "pos": [x, y], "size": [w, h] }                                     축은 가운데(또는 "anchor")
    /// { "kind": "linked",  "parts": [ { "pos", "size", "anchor" } | { "pos", "size", "anchorPart": i } ... ] }
    /// { "kind": "pillar",  "base": [x, y], "boxes": [[w, h], ...] }                            받침 윗면 가운데, 아래에서 위로
    /// { "kind": "box",     "pos": [x, y], "size": [w, h] }
    /// </code>
    /// "mirror": true면 x를 뒤집은 짝을 하나 더 만든다(맵은 좌우 대칭). 항목 하나(와 그 짝)가 각각 묶음 하나다.
    /// </summary>
    public sealed class ArenaMeta
    {
        /// <summary>0 A 민트, 1 B 사이언, 2 C 슬레이트(D-022). 짝 파일이 없으면 0.</summary>
        public int Theme;
        /// <summary>새 정의 에셋의 선택형 맵 색상. 없으면 기존 JSON 테마를 그대로 사용한다.</summary>
        public Knockbox.Presentation.MapThemeSettings MapTheme;
        public Knockbox.Presentation.ArenaTheme ResolveTheme(Knockbox.Presentation.PaletteAsset palette) =>
            MapTheme != null ? MapTheme.Resolve(palette, Theme) : palette.Theme(Theme);
        /// <summary>주 광원 위치. 없으면 연출 기본값(경기장 위쪽 끝에서 경기장 높이의 3/4 위).</summary>
        public bool HasLight;
        public Vector2 Light;
        /// <summary>경기 순환(라운드마다 맵 교체)에 들어가는 맵인지. v1 맵(테스트 고정 자료)은 false.</summary>
        public bool Rotation;
        /// <summary>참이면 맨 위 테두리 줄(천장)도 그린다(CR-004: 천장에 붙는 실험 맵). 기본은 그리지 않는다(08 4·7절).</summary>
        public bool Ceiling;
        /// <summary>그림자 세기(그림자 속 밝기 = 빛 × (1 − 세기)). 기본 BackgroundView.ShadowStrength(0.65).</summary>
        public float Shadow = Knockbox.Presentation.BackgroundView.ShadowStrength;
        /// <summary>구조물 목록(묶음 번호 포함). 비어 있으면 구조물 없음.</summary>
        public PropDef[] Props = new PropDef[0];

        public static readonly ArenaMeta Default = new ArenaMeta();

        public static ArenaMeta Parse(string json, string sourceName)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                throw new FormatException($"{sourceName}: 맵 짝 파일은 JSON 객체여야 함");
            var meta = new ArenaMeta();
            if (root.TryGetValue("theme", out object theme) && theme != null)
            {
                int index = ScenarioJson.ParseTheme(theme as string ?? throw new FormatException($"{sourceName}: \"theme\"은 문자열"));
                meta.Theme = Mathf.Max(0, index);
            }
            if (root.TryGetValue("light", out object light) && light != null)
            {
                if (!(light is List<object> xy) || xy.Count != 2 || !(xy[0] is double x) || !(xy[1] is double y))
                    throw new FormatException($"{sourceName}: \"light\"는 [x, y] 두 숫자");
                meta.HasLight = true;
                meta.Light = new Vector2((float)x, (float)y);
            }
            if (root.TryGetValue("rotation", out object rotation) && rotation != null)
                meta.Rotation = rotation is bool b ? b : throw new FormatException($"{sourceName}: \"rotation\"은 참/거짓");
            if (root.TryGetValue("shadow", out object shadow) && shadow != null)
                meta.Shadow = shadow is double d ? Mathf.Clamp01((float)d) : throw new FormatException($"{sourceName}: \"shadow\"는 0~1 숫자");
            if (root.TryGetValue("ceiling", out object ceiling) && ceiling != null)
                meta.Ceiling = ceiling is bool c ? c : throw new FormatException($"{sourceName}: \"ceiling\"은 참/거짓");
            if (root.TryGetValue("props", out object props) && props != null)
            {
                if (!(props is List<object> list))
                    throw new FormatException($"{sourceName}: \"props\"는 배열");
                meta.Props = ParseProps(list, sourceName);
            }
            return meta;
        }

        static PropDef[] ParseProps(List<object> list, string source)
        {
            var output = new List<PropDef>();
            int group = 0;
            for (int i = 0; i < list.Count; i++)
            {
                string where = $"{source}: props[{i}]";
                if (!(list[i] is Dictionary<string, object> entry))
                    throw new FormatException($"{where}는 객체");
                bool mirror = entry.TryGetValue("mirror", out object m) && m is bool mb && mb;
                AddEntry(entry, where, group++, false, output);
                if (mirror)
                    AddEntry(entry, where, group++, true, output);
            }
            return output.ToArray();
        }

        static void AddEntry(Dictionary<string, object> entry, string where, int group, bool flip, List<PropDef> output)
        {
            string kind = entry.TryGetValue("kind", out object k) ? k as string : null;
            switch (kind)
            {
                case "hanging":
                    output.Add(Body(entry, where, PropKind.Hanging, group, flip, true, false));
                    break;
                case "spinner":
                    output.Add(Body(entry, where, PropKind.Spinner, group, flip, false, true));
                    break;
                case "hinged":
                    output.Add(Body(entry, where, PropKind.Hinged, group, flip, true, false));
                    break;
                case "seesaw":
                    output.Add(Body(entry, where, PropKind.Seesaw, group, flip, false, true));
                    break;
                case "box":
                    output.Add(Body(entry, where, PropKind.LooseBox, group, flip, false, false));
                    break;
                case "linked":
                {
                    if (!entry.TryGetValue("parts", out object partsValue) || !(partsValue is List<object> parts) || parts.Count < 2)
                        throw new FormatException($"{where}: \"parts\"는 2개 이상의 배열");
                    int first = output.Count;
                    for (int p = 0; p < parts.Count; p++)
                    {
                        if (!(parts[p] is Dictionary<string, object> part))
                            throw new FormatException($"{where}.parts[{p}]는 객체");
                        var body = Body(part, $"{where}.parts[{p}]", PropKind.Linked, group, flip, false, false);
                        if (part.TryGetValue("anchorPart", out object ap) && ap is double apd)
                        {
                            int index = (int)apd;
                            if (index < 0 || index >= p)
                                throw new FormatException($"{where}.parts[{p}]: \"anchorPart\"는 앞쪽 부품 번호");
                            var parent = output[first + index];
                            body.HasAnchor = true;
                            body.AnchorProp = first + index;
                            body.Anchor = new Vec2(parent.Center.X, parent.MinY);
                        }
                        else if (!body.HasAnchor)
                        {
                            throw new FormatException($"{where}.parts[{p}]: \"anchor\" 또는 \"anchorPart\"가 필요함");
                        }
                        output.Add(body);
                    }
                    break;
                }
                case "pillar":
                {
                    var b = Vec(entry, "base", where, flip);
                    if (!entry.TryGetValue("boxes", out object boxesValue) || !(boxesValue is List<object> boxes) || boxes.Count < 2 || boxes.Count > 6)
                        throw new FormatException($"{where}: \"boxes\"는 상자 2~6개");
                    float y = b.Y;
                    for (int j = 0; j < boxes.Count; j++)
                    {
                        var size = AsVec(boxes[j], $"{where}.boxes[{j}]");
                        output.Add(new PropDef
                        {
                            Kind = PropKind.BoxPillar,
                            Group = group,
                            Center = new Vec2(b.X, y + size.Y * 0.5f),
                            HalfSize = size * 0.5f,
                            AnchorProp = -1,
                        });
                        y += size.Y;
                    }
                    break;
                }
                default:
                    throw new FormatException($"{where}: 알 수 없는 \"kind\" '{kind}'(hanging, spinner, hinged, seesaw, linked, pillar, box)");
            }
        }

        /// <param name="needAnchor">"anchor"가 꼭 있어야 하는 종류(줄 고정점, 경첩).</param>
        /// <param name="centerAxis">"anchor"가 없으면 가운데를 축으로(회전 블록, 시소).</param>
        static PropDef Body(Dictionary<string, object> entry, string where, PropKind kind, int group, bool flip, bool needAnchor, bool centerAxis)
        {
            var body = new PropDef
            {
                Kind = kind,
                Group = group,
                Center = Vec(entry, "pos", where, flip),
                HalfSize = AsVec(entry.TryGetValue("size", out object size) ? size : null, where + ".size") * 0.5f,
                AnchorProp = -1,
            };
            if (body.HalfSize.X <= 0f || body.HalfSize.Y <= 0f)
                throw new FormatException($"{where}: 크기는 양수");
            if (entry.TryGetValue("break", out object brk) && brk != null)
                body.BreakImpulse = brk is double bd && bd > 0 ? (float)bd : throw new FormatException($"{where}: \"break\"는 양수");
            if (entry.ContainsKey("anchor"))
            {
                body.HasAnchor = true;
                body.Anchor = Vec(entry, "anchor", where, flip);
            }
            else if (needAnchor)
            {
                throw new FormatException($"{where}: \"anchor\"가 필요함");
            }
            else if (centerAxis)
            {
                body.HasAnchor = true;
                body.Anchor = body.Center;
            }
            return body;
        }

        static Vec2 Vec(Dictionary<string, object> entry, string key, string where, bool flip)
        {
            if (!entry.TryGetValue(key, out object value))
                throw new FormatException($"{where}: \"{key}\"가 필요함");
            var v = AsVec(value, $"{where}.{key}");
            return flip ? new Vec2(-v.X, v.Y) : v;
        }

        static Vec2 AsVec(object value, string where)
        {
            if (!(value is List<object> xy) || xy.Count != 2 || !(xy[0] is double x) || !(xy[1] is double y))
                throw new FormatException($"{where}: [x, y] 두 숫자");
            return new Vec2((float)x, (float)y);
        }
    }
}
