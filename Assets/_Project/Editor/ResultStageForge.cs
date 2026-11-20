using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// 결과 발표(2026-11-20)의 첫 조형을 맵 제작기가 읽는 정점 데이터로 만든다.
    /// - Results_Glyphs: 결과 글자 틀(숫자 0~5, 콜론). 글자마다 볼록 조각 여러 개(ID "glyph-{글자}-{번호}"), 바닥선 y=-2·높이 3 기준.
    /// - Results_Stage: 평평한 바닥 + 가운데 시상대(옆변이 살짝 기울고 위 모서리를 깎은 볼록 도형). 천장·벽·계단 없음.
    ///   점수 숫자 발판(2026-11-20): 양쪽 점수 자리마다 숫자 0~5 물리 몸(글자 틀 조각 모양, 결과 공간에 들어올 때 점수와 맞는 것만 남음)을
    ///   화면 위 밖 고정점에 줄 하나로 매달고, 콜론 두 점은 각각 양쪽 숫자에 줄로 잇는다. 모두 화면 위 밖에서 기다리다 글자 낙하 틱에 떨어진다.
    /// 만든 뒤에는 MapAuthoring 씬에서 같은 정점 선택·두점자르기·이동으로 다듬는다. 다시 만들면 손으로 다듬은 모양을 덮어쓰므로 메뉴에서 확인을 받는다.
    /// </summary>
    public static class ResultStageForge
    {
        public const string Folder = "Assets/_Project/Data/Maps/Results";
        public const string StagePath = Folder + "/Results_Stage.asset";
        public const string GlyphPath = Folder + "/Results_Glyphs.asset";
        public const string BodyPrefix = "score-";

        // 글자 조형 값(높이 3 기준): 획 두께, 바깥 모서리를 깎는 길이, 가운데 가로획 높이.
        // 숫자 발판(2026-11-20)은 숫자 안쪽 칸(높이 0.81 × 배율)에 선수 몸(0.9)이 들어가야 해서 획을 0.58에서 0.46으로 가늘게 했다.
        const float S = .46f, C = .2f, Mid = 1.5f, H = 3f, W = 2f;
        // 시상대: 아래 폭 4.4, 위 폭 3.0(+모서리 깎임), 바닥에서 2.4 높이. 승자 자리는 윗면 가운데, 패자 자리는 오른쪽 바닥.
        const float FloorTop = -9f, PodiumHeight = 2.4f;
        static readonly Vector2 WinnerSpot = new Vector2(0, FloorTop + PodiumHeight), LoserSpot = new Vector2(5.6f, FloorTop);

        [MenuItem("Knockbox/결과 공간/첫 조형 다시 만들기 (글자 틀·시상대·점수 숫자 발판)")]
        static void RebuildMenu()
        {
            if (File.Exists(GlyphPath) && !EditorUtility.DisplayDialog("결과 공간 첫 조형", "글자 틀·결과 맵 시상대·결과 발표 값·점수 숫자 발판(물리 상자·로프)을 첫 조형으로 다시 만듭니다. 맵 제작기에서 다듬은 모양은 덮어써집니다.", "다시 만들기", "취소"))
                return;
            Build(true);
        }

        [MenuItem("Knockbox/결과 공간/점수 숫자 발판만 다시 배치 (결과 발표 값·글자 틀 반영)")]
        static void RelayoutMenu() => Build(false);

        [MenuItem("Knockbox/결과 공간/결과 화면 시험 (Main 연습, 첫 싸움 P1 승리)")]
        static void TestStage() => StartTrial(StagePath);

        static void StartTrial(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play를 끝낸 뒤 시험해주세요.");
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException(path + "를 찾을 수 없습니다.");
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            MapTestReturn.Prepare(guid);
            try { EditorApplication.isPlaying = true; }
            catch { MapTestReturn.Restore(); throw; }
        }

        /// <param name="overwrite">참이면 글자 틀·시상대·결과 발표 값까지 첫 조형으로 되돌린다. 거짓이면 다듬은 글자 틀·지형·결과 발표 값은 두고
        /// 그 값으로 점수 숫자 발판(물리 상자·로프)만 다시 배치한다.</param>
        public static string Build(bool overwrite)
        {
            var stage = AssetDatabase.LoadAssetAtPath<MapDefinition>(StagePath);
            if (stage == null) throw new InvalidOperationException(StagePath + "가 없습니다.");
            var glyphs = AssetDatabase.LoadAssetAtPath<MapDefinition>(GlyphPath);
            if (glyphs == null || overwrite) glyphs = WriteGlyphMap(stage, glyphs);
            if (overwrite) WriteStage(stage, glyphs);
            WriteScoreBodies(stage, glyphs);
            AssetDatabase.SaveAssets();
            return "결과 공간 첫 조형: 글자 " + new ResultGlyphLibrary(glyphs).Count + "개, 숫자 발판 몸 " + stage.PhysicalBoxCount + "개·로프 " + stage.RopeCount + "개";
        }

        // ---------------- 글자 ----------------

        /// <summary>바깥 모서리를 깎은 사각형(반시계). 깎는 길이가 0인 모서리는 직각이다.</summary>
        static Vector2[] Bar(float x0, float y0, float x1, float y1, float tl = 0, float tr = 0, float br = 0, float bl = 0)
        {
            var p = new List<Vector2>();
            p.Add(bl > 0 ? new Vector2(x0 + bl, y0) : new Vector2(x0, y0));
            if (br > 0) { p.Add(new Vector2(x1 - br, y0)); p.Add(new Vector2(x1, y0 + br)); } else p.Add(new Vector2(x1, y0));
            if (tr > 0) { p.Add(new Vector2(x1, y1 - tr)); p.Add(new Vector2(x1 - tr, y1)); } else p.Add(new Vector2(x1, y1));
            if (tl > 0) { p.Add(new Vector2(x0 + tl, y1)); p.Add(new Vector2(x0, y1 - tl)); } else p.Add(new Vector2(x0, y1));
            if (bl > 0) p.Add(new Vector2(x0, y0 + bl));
            return p.ToArray();
        }

        /// <summary>글자 키 → 볼록 조각(글자 왼쪽 끝·바닥선 기준, 높이 3).</summary>
        public static Dictionary<string, List<Vector2[]>> GlyphShapes()
        {
            float lo = Mid - S * .5f, hi = Mid + S * .5f;
            var d = new Dictionary<string, List<Vector2[]>>();
            d["0"] = new List<Vector2[]> { Bar(0, H - S, W, H, C, C), Bar(0, 0, W, S, 0, 0, C, C), Bar(0, S, S, H - S), Bar(W - S, S, W, H - S) };
            d["1"] = new List<Vector2[]>
            {
                // 깃발이 붙는 기둥 왼쪽 위는 깎지 않고, 깃발 윗변을 기둥 윗변(3)까지 올려 둘 사이에 홈이 생기지 않게 한다(2026-11-20 깨져 보이던 부분).
                Bar(.72f, 0, .72f + S, H, 0, C),
                new[] { new Vector2(.15f, 2.2f), new Vector2(.72f, 2.48f), new Vector2(.72f, 3f), new Vector2(.15f, 2.72f) },
                Bar(.22f, 0, 1.82f, .5f, 0, 0, .14f, .14f),
            };
            d["2"] = new List<Vector2[]> { Bar(0, H - S, W, H, C, C), Bar(W - S, hi, W, H - S), Bar(0, lo, W, hi), Bar(0, S, S, lo), Bar(0, 0, W, S, 0, 0, C, C) };
            d["3"] = new List<Vector2[]> { Bar(0, H - S, W, H, C, C), Bar(W - S, S, W, H - S), Bar(.48f, lo, W - S, hi), Bar(0, 0, W, S, 0, 0, C, C) };
            d["4"] = new List<Vector2[]> { Bar(0, lo, S, H, C), Bar(S, lo, W - S, hi), Bar(W - S, 0, W, H, 0, C, C) };
            d["5"] = new List<Vector2[]> { Bar(0, H - S, W, H, C, C), Bar(0, hi, S, H - S), Bar(0, lo, W, hi), Bar(W - S, S, W, lo), Bar(0, 0, W, S, 0, 0, C, C) };
            d["colon"] = new List<Vector2[]> { Bar(0, .5f, .62f, 1.12f, .15f, .15f, .15f, .15f), Bar(0, 1.88f, .62f, 2.5f, .15f, .15f, .15f, .15f) };
            return d;
        }

        static MapDefinition WriteGlyphMap(MapDefinition stage, MapDefinition existing)
        {
            var map = existing != null ? existing : UnityEngine.Object.Instantiate(stage);
            var so = new SerializedObject(map);
            so.FindProperty("stableId").stringValue = "result-glyphs";
            so.FindProperty("displayName").stringValue = "Result glyphs (결과 글자 틀)";
            so.FindProperty("boundary").boolValue = false;
            so.FindProperty("ceiling").boolValue = false;
            so.FindProperty("usage").enumValueIndex = (int)MapUsage.Match;
            so.FindProperty("podiumBoxId").stringValue = "";
            so.FindProperty("resultGlyphs").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            var boxes = new List<MapBox> { Rect("box-000", -18, -10, 18, -9) };
            var shapes = GlyphShapes();
            float x = -15.5f;
            foreach (string key in ResultGlyphLibrary.Keys)
            {
                var pieces = shapes[key];
                float width = 0;
                foreach (var piece in pieces) foreach (var p in piece) width = Mathf.Max(width, p.x);
                for (int i = 0; i < pieces.Count; i++)
                {
                    var moved = Array.ConvertAll(pieces[i], p => new Vector2(p.x + x, p.y + ResultGlyphLibrary.BaselineY));
                    boxes.Add(Polygon(ResultGlyphLibrary.Prefix + key + "-" + i, moved));
                }
                x += width + 1.2f;
            }
            map.SaveLayout(boxes.ToArray(), Array.Empty<MapPhysicalBox>(), Array.Empty<MapRope>(), new Vector2(-16.5f, -9), new Vector2(16.5f, -9));
            Check(map);
            if (existing == null) AssetDatabase.CreateAsset(map, GlyphPath);
            else EditorUtility.SetDirty(map);
            return map;
        }

        // ---------------- 시상대·결과 맵 ----------------

        /// <summary>시상대: 아래가 넓고 옆변이 살짝 기운 단상에 위 모서리를 비스듬히 깎았다(모놀리스 윗부분과 같은 깎임).</summary>
        public static Vector2[] PodiumShape() => new[]
        {
            new Vector2(-2.2f, FloorTop), new Vector2(2.2f, FloorTop), new Vector2(1.95f, FloorTop + PodiumHeight - .45f),
            new Vector2(1.5f, FloorTop + PodiumHeight), new Vector2(-1.5f, FloorTop + PodiumHeight), new Vector2(-1.95f, FloorTop + PodiumHeight - .45f),
        };

        static void WriteStage(MapDefinition stage, MapDefinition glyphs)
        {
            var boxes = new[] { Rect("box-000", -18, -10, 18, -9), Polygon("podium", PodiumShape()) };
            stage.SaveLayout(boxes, Array.Empty<MapPhysicalBox>(), Array.Empty<MapRope>(), WinnerSpot, LoserSpot);
            stage.SetResultsStage(new ResultsStageSettings());
            var so = new SerializedObject(stage);
            so.FindProperty("boundary").boolValue = false;
            so.FindProperty("ceiling").boolValue = false;
            so.FindProperty("resultGlyphs").objectReferenceValue = glyphs;
            so.ApplyModifiedPropertiesWithoutUndo();
            stage.SetUsage(MapUsage.Results, "podium");
            Check(stage);
            EditorUtility.SetDirty(stage);
        }

        /// <summary>
        /// 점수 숫자 발판: 왼쪽(P1)·오른쪽(P2) 자리마다 숫자 0~5 몸을 같은 자리에 겹쳐 둔다(결과 공간에 들어올 때 점수와 맞는 것만 남는다).
        /// 각 숫자는 윗변 가운데를 화면 위 밖 고정점에 줄 하나로 매단다. 콜론 두 점은 각각 왼쪽 숫자 오른쪽 옆·오른쪽 숫자 왼쪽 옆에 줄로 이어
        /// 두 숫자 사이에 처져 매달린다(선수는 지나간다). 모든 몸은 쉬는 자리보다 signDropHeight 위(화면 위 밖)에서 시작한다.
        /// </summary>
        static void WriteScoreBodies(MapDefinition stage, MapDefinition glyphs)
        {
            var s = stage.ResultsStage;
            var library = new ResultGlyphLibrary(glyphs);
            float k = s.scoreHeight / H, drop = s.signDropHeight;
            float colonHalf = .31f * k, gap = s.colonSpacing * k, slotHalf = W * k * .5f;
            float slotX = colonHalf + gap + slotHalf;
            float restCenter = s.scoreBaselineY + s.scoreHeight * .5f;
            var bodies = new List<MapPhysicalBox>();
            var ropes = new List<MapRope>();
            var digitIds = new[] { new List<string>(), new List<string>() };
            var digitHalf = new Dictionary<string, float>();
            var digitGlyph = new Dictionary<string, ResultGlyphLibrary.Glyph>();
            for (int side = 0; side < 2; side++)
            {
                float x = s.signCenterX + (side == 0 ? -slotX : slotX);
                for (int d = 0; d <= 5; d++)
                {
                    if (!library.TryGet(d.ToString(), out var g)) throw new InvalidOperationException("결과 글자 틀에 숫자 " + d + "가 없습니다.");
                    var size = new Vector2(g.Width * k, s.scoreHeight);
                    string id = BodyPrefix + "p" + (side + 1) + "-" + d;
                    bodies.Add(new MapPhysicalBox { id = id, center = new Vector2(x, restCenter + drop), size = size, mass = s.digitMass,
                        glyph = d.ToString(), scoreSlot = side + 1, scoreDigit = d, releaseSpeed = s.signDropSpeed });
                    // 줄은 숫자 가운데 세로줄에서 가장 높은 획 윗면에 붙인다(4처럼 윗변 가운데가 비어 있으면 그 아래 가로획). 상자 윗변 가운데에 붙이면 빈 곳에 매달려 보였다.
                    float top = (TopOnCenterLine(g.Pieces, g.Width * .5f) - H * .5f) * k;
                    ropes.Add(new MapRope { id = "rope-" + id, boxId = id, graph = true, sourceBoxId = "", anchor = new Vector2(x, s.ropeTopY),
                        localAnchor = new Vector2(0, top), length = s.ropeTopY - (restCenter + top), mode = RopeBreakMode.Unbreakable });
                    digitIds[side].Add(id);
                    digitHalf[id] = size.x * .5f;
                    digitGlyph[id] = g;
                }
            }
            if (!library.TryGet("colon", out var colon) || colon.Pieces.Count < 2) throw new InvalidOperationException("결과 글자 틀의 콜론은 점 두 조각이어야 합니다.");
            for (int dot = 0; dot < 2; dot++)
            {
                // 조각 번호가 아니라 높이로 아래 점·위 점을 고른다(글자 틀에서 조각 순서를 바꿔도 같은 자리).
                int piece = PieceByHeight(colon.Pieces, dot);
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                foreach (var p in colon.Pieces[piece]) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                var size = (max - min) * k;
                float restY = s.scoreBaselineY + (min.y + max.y) * .5f * k;
                string id = BodyPrefix + "dot-" + dot;
                bodies.Add(new MapPhysicalBox { id = id, center = new Vector2(s.signCenterX, restY + drop), size = size, mass = s.dotMass,
                    playerPassThrough = true, glyph = "colon:" + piece, releaseSpeed = s.signDropSpeed });
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1 : 1;
                    var dotLocal = new Vector2(sign * size.x * .5f, 0);
                    foreach (string digit in digitIds[side])
                    {
                        // 쉬는 자리에서 줄이 dotRopeSlack만큼 처져 점이 제 높이에 오도록 잡은 숫자 안쪽 옆면의 점을, 그 숫자의 실제 획 위 가장 가까운 점으로 옮긴다
                        // (3·5·2처럼 그 높이의 옆면이 비어 있으면 상자 옆면에 붙은 줄이 허공에 매달려 보였다). 줄 길이는 옮긴 점까지의 거리로 다시 잰다.
                        float digitX = s.signCenterX + sign * slotX, edge = digitX - sign * digitHalf[digit];
                        float span = Mathf.Abs(edge - (s.signCenterX + dotLocal.x));
                        float slack = Mathf.Max(1.001f, s.dotRopeSlack);
                        float sag = Mathf.Sqrt(span * slack * span * slack - span * span);
                        var g = digitGlyph[digit];
                        var ideal = new Vector2(side == 0 ? g.Width : 0f, (restY + sag - s.scoreBaselineY) / k);
                        var material = NearestMaterial(g.Pieces, ideal);
                        var digitLocal = new Vector2((material.x - g.Width * .5f) * k, (material.y - H * .5f) * k);
                        var restDigit = new Vector2(digitX, restCenter) + digitLocal;
                        var restDot = new Vector2(s.signCenterX + dotLocal.x, restY);
                        float length = Vector2.Distance(restDigit, restDot) * slack;
                        ropes.Add(new MapRope { id = "rope-" + id + "-" + digit, boxId = id, graph = true, sourceBoxId = digit,
                            sourceLocalAnchor = digitLocal, localAnchor = dotLocal,
                            anchor = Vector2.zero, length = length, mode = RopeBreakMode.Unbreakable });
                    }
                }
            }
            var boxes = new MapBox[stage.BoxCount];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = stage.Box(i);
            stage.SaveLayout(boxes, bodies.ToArray(), ropes.ToArray(), stage.Spawn(0), stage.Spawn(1));
            Check(stage);
            EditorUtility.SetDirty(stage);
        }

        /// <summary>세로줄 x에서 글자 조각들이 차지하는 가장 높은 y(글자 단위). 그 줄에 조각이 없으면 가장 높은 꼭짓점.</summary>
        static float TopOnCenterLine(List<Vector2[]> pieces, float x)
        {
            float best = float.MinValue, highest = float.MinValue;
            foreach (var piece in pieces)
                for (int i = 0; i < piece.Length; i++)
                {
                    var a = piece[i]; var b = piece[(i + 1) % piece.Length];
                    highest = Mathf.Max(highest, a.y);
                    if (x < Mathf.Min(a.x, b.x) - 1e-4f || x > Mathf.Max(a.x, b.x) + 1e-4f) continue;
                    float y = Mathf.Abs(b.x - a.x) < 1e-5f ? Mathf.Max(a.y, b.y) : Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
                    best = Mathf.Max(best, y);
                }
            return best > float.MinValue ? best : highest;
        }

        /// <summary>글자 조각 위(안쪽 또는 변)에서 p에 가장 가까운 점(글자 단위).</summary>
        static Vector2 NearestMaterial(List<Vector2[]> pieces, Vector2 p)
        {
            Vector2 nearest = p; float bestDistance = float.MaxValue;
            foreach (var piece in pieces)
            {
                bool inside = true;
                for (int i = 0; i < piece.Length; i++)
                {
                    var a = piece[i]; var b = piece[(i + 1) % piece.Length];
                    if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0) inside = false;
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                    var q = a + ab * t;
                    float d = (q - p).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; nearest = q; }
                }
                if (inside) return p;
            }
            return nearest;
        }

        static int PieceByHeight(List<Vector2[]> pieces, int rank)
        {
            var order = new List<int>();
            for (int i = 0; i < pieces.Count; i++) order.Add(i);
            order.Sort((a, b) => MeanY(pieces[a]).CompareTo(MeanY(pieces[b])));
            return order[rank];
        }

        static float MeanY(Vector2[] piece) { float y = 0; foreach (var p in piece) y += p.y; return y / piece.Length; }

        // ---------------- 공통 ----------------

        static MapBox Rect(string id, float minX, float minY, float maxX, float maxY) =>
            new MapBox { id = id, kind = TerrainKind.Solid, minX = minX, minY = minY, maxX = maxX, maxY = maxY, vertices = Array.Empty<Vector2>() };

        static MapBox Polygon(string id, Vector2[] points)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in points) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            var box = new MapBox { id = id, kind = TerrainKind.Solid, minX = minX, minY = minY, maxX = maxX, maxY = maxY, vertices = (Vector2[])points.Clone() };
            box.ToBox(); // 반시계 볼록이 아니면 여기서 알린다.
            return box;
        }

        static void Check(MapDefinition map)
        {
            var errors = map.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(map.name + ": " + string.Join("\n", errors));
        }
    }
}
