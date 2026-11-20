using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>물리 상자의 원본 배치. 회전/속도/휴면은 경기 중 PropState에서만 관리한다.</summary>
    [Serializable]
    public struct MapPhysicalBox : IEquatable<MapPhysicalBox>
    {
        public string id;
        public Vector2 center;
        public Vector2 size;
        public float mass;
        public bool playerPassThrough;
        public bool connector;
        public float connectorSpeedLoss;
        [Tooltip("결과 공간 숫자 발판(2026-11-20): 결과 글자 틀의 글자 키(0~5, colon). 'colon:0'처럼 쓰면 그 글자의 조각 하나만. 비우면 보통 상자")]
        public string glyph;
        [Tooltip("결과 공간 점수 몸: 0 없음, 1 = P1 세트 점수, 2 = P2 세트 점수")]
        public int scoreSlot;
        [Tooltip("이 몸이 나타내는 점수(결과 공간에 들어올 때 점수와 다르면 사라진다)")]
        public int scoreDigit;
        [Tooltip("결과 공간에 들어올 때 주는 아래 방향 시작 속도(유닛/초). 0이면 정지에서 떨어진다")]
        public float releaseSpeed;
        public MapBox Bounds => new MapBox { id=id, kind=TerrainKind.Solid,
            minX=center.x-size.x*.5f, maxX=center.x+size.x*.5f,
            minY=center.y-size.y*.5f, maxY=center.y+size.y*.5f };
        public bool HasGlyph => !string.IsNullOrEmpty(glyph);

        /// <summary>필드별 비교. 글자 키는 코드에서 만든 null과 에셋에서 읽은 ""를 같게 본다(2026-11-20 숫자 발판 필드 추가).</summary>
        public bool Equals(MapPhysicalBox o) => id == o.id && center.Equals(o.center) && size.Equals(o.size) && mass.Equals(o.mass)
            && playerPassThrough == o.playerPassThrough && connector == o.connector && connectorSpeedLoss.Equals(o.connectorSpeedLoss)
            && (glyph ?? "") == (o.glyph ?? "") && scoreSlot == o.scoreSlot && scoreDigit == o.scoreDigit && releaseSpeed.Equals(o.releaseSpeed);
        public override bool Equals(object obj) => obj is MapPhysicalBox o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(id, center, size, mass, playerPassThrough, connector, connectorSpeedLoss, HashCode.Combine(glyph ?? "", scoreSlot, scoreDigit, releaseSpeed));
        public PropDef ToProp(int order) => ToProp(order, null);

        /// <summary>글자 몸이면 결과 글자 틀(library)의 조각을 상자 크기에 맞춰(높이 기준 같은 비율, 가운데 원점) 선수와 닿는 모양으로 만든다.</summary>
        public PropDef ToProp(int order, ResultGlyphLibrary library)
        {
            var prop = new PropDef { Id=id, Kind=PropKind.LooseBox, Group=order,
                Center=new Vec2(center.x,center.y), HalfSize=new Vec2(size.x*.5f,size.y*.5f),
                Mass=mass, StartAwake=true, PlayerPassThrough=playerPassThrough, AnchorProp=-1, IsConnector=connector, ConnectorSpeedLoss=connectorSpeedLoss,
                ScoreSlot=(byte)Mathf.Clamp(scoreSlot,0,2), ScoreDigit=scoreDigit, ReleaseSpeed=Mathf.Max(0f,releaseSpeed) };
            if (HasGlyph && library != null && TryGlyphPieces(library, glyph, out var pieces))
                prop.Shape = FitPieces(pieces, size);
            return prop;
        }

        /// <summary>글자 키("5", "colon:1")의 조각들(글자 틀 좌표).</summary>
        public static bool TryGlyphPieces(ResultGlyphLibrary library, string key, out List<Vector2[]> pieces)
        {
            pieces = null;
            int colon = key.IndexOf(':');
            string name = colon > 0 ? key.Substring(0, colon) : key;
            if (!library.TryGet(name, out var g)) return false;
            if (colon < 0) { pieces = g.Pieces; return true; }
            if (!int.TryParse(key.Substring(colon + 1), out int index) || index < 0 || index >= g.Pieces.Count) return false;
            pieces = new List<Vector2[]> { g.Pieces[index] };
            return true;
        }

        /// <summary>조각들을 감싸는 사각형을 size에 맞춘다(높이 비율로 같은 배율, 가운데 원점). 화면도 같은 값을 그린다.</summary>
        public static ConvexPolygon[] FitPieces(List<Vector2[]> pieces, Vector2 size)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            foreach (var piece in pieces) foreach (var p in piece) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var center = (min + max) * .5f;
            float scale = size.y / Mathf.Max(1e-4f, max.y - min.y);
            var shape = new ConvexPolygon[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                var points = new Vec2[pieces[i].Length];
                for (int k = 0; k < points.Length; k++) { var q = (pieces[i][k] - center) * scale; points[k] = new Vec2(q.x, q.y); }
                shape[i] = new ConvexPolygon(points);
            }
            return shape;
        }
    }
}
