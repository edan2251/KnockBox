using System;
using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>
    /// 결과 글자 틀(2026-11-20). 글자 틀 맵(Results_Glyphs)의 고정 지형 조각 중 ID가 "glyph-{글자}-{번호}"인 것을 글자별로 모은다.
    /// 조각은 맵 제작기의 정점 선택·두점자르기로 만드는 볼록 다각형(또는 사각형)이며, 모든 글자는 바닥선 y = BaselineY, 높이 UnitHeight(3) 기준으로 그린다.
    /// 글자의 왼쪽 끝은 그 글자 조각들의 가장 왼쪽 x이고, 폭(진행)은 조각들의 좌우 범위다. 화면 전용이며 판정과 무관하다.
    /// </summary>
    public sealed class ResultGlyphLibrary
    {
        public const string Prefix = "glyph-";
        public const float BaselineY = -2f, UnitHeight = 3f;
        /// <summary>글자 키: 숫자 0~5와 colon(:). 결과 화면은 세트 점수만 매단다(2026-11-20 사용자 확정, 이전의 P·승 글자는 뺐다).</summary>
        public static readonly string[] Keys = { "0", "1", "2", "3", "4", "5", "colon" };

        public sealed class Glyph
        {
            /// <summary>조각들의 꼭짓점(글자 왼쪽 끝·바닥선 기준, 높이 3 단위, 반시계).</summary>
            public readonly List<Vector2[]> Pieces = new List<Vector2[]>();
            public float Width;
        }

        readonly Dictionary<string, Glyph> glyphs = new Dictionary<string, Glyph>(StringComparer.Ordinal);
        public MapDefinition Source { get; }
        public int SourceRevision { get; }

        public ResultGlyphLibrary(MapDefinition source)
        {
            Source = source;
            SourceRevision = source != null ? source.Revision : -1;
            if (source == null) return;
            var raw = new Dictionary<string, List<Vector2[]>>(StringComparer.Ordinal);
            for (int i = 0; i < source.BoxCount; i++)
            {
                var b = source.Box(i);
                if (b.id == null || !b.id.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                string rest = b.id.Substring(Prefix.Length);
                int dash = rest.LastIndexOf('-');
                string key = dash > 0 ? rest.Substring(0, dash) : rest;
                var points = b.IsPolygon ? (Vector2[])b.vertices.Clone() : new[]
                {
                    new Vector2(b.minX, b.minY), new Vector2(b.maxX, b.minY), new Vector2(b.maxX, b.maxY), new Vector2(b.minX, b.maxY)
                };
                if (!raw.TryGetValue(key, out var list)) raw[key] = list = new List<Vector2[]>();
                list.Add(points);
            }
            foreach (var pair in raw)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var piece in pair.Value)
                    foreach (var p in piece) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
                var glyph = new Glyph { Width = maxX - minX };
                foreach (var piece in pair.Value)
                {
                    var local = new Vector2[piece.Length];
                    for (int k = 0; k < piece.Length; k++) local[k] = new Vector2(piece[k].x - minX, piece[k].y - BaselineY);
                    glyph.Pieces.Add(local);
                }
                glyphs[pair.Key] = glyph;
            }
        }

        public bool TryGet(string key, out Glyph glyph) => glyphs.TryGetValue(key, out glyph);
        public bool Has(string key) => glyphs.ContainsKey(key);
        public int Count => glyphs.Count;
    }
}
