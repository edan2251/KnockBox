using System;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>보상 맵의 비석 하나: 맵 편집기 상자를 크기 조절·두점자르기한 볼록 다각형(맵 좌표, 반시계).</summary>
    [Serializable]
    public struct RewardStele
    {
        public string id;
        public Vector2[] vertices;

        public Rect Bounds
        {
            get
            {
                Vector2 min = vertices[0], max = vertices[0];
                foreach (var p in vertices) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        public RewardStele Copy() => new RewardStele { id = id, vertices = vertices != null ? (Vector2[])vertices.Clone() : null };

        /// <summary>맵 편집기 상자(MapBox) 형식. 상자 핸들·두점자르기 도구를 그대로 쓰기 위해 바꾼다.</summary>
        public MapBox ToBox()
        {
            var b = Bounds;
            return new MapBox
            {
                id = id, kind = Knockbox.Simulation.TerrainKind.Solid, minX = b.xMin, minY = b.yMin, maxX = b.xMax, maxY = b.yMax,
                vertices = (Vector2[])vertices.Clone(),
            };
        }

        public static RewardStele From(MapBox box) => new RewardStele
        {
            id = box.id,
            vertices = box.IsPolygon ? (Vector2[])box.vertices.Clone() : new[]
            {
                new Vector2(box.minX, box.minY), new Vector2(box.maxX, box.minY), new Vector2(box.maxX, box.maxY), new Vector2(box.minX, box.maxY),
            },
        };
    }

    /// <summary>
    /// 보상(카드 선택) 장면의 값. 보상 맵 파일에 함께 저장하고 MapAuthoring에서 조절한다. 화면 전용이며 판정·지형 체크섬에 들어가지 않는다.
    /// </summary>
    [Serializable]
    public sealed class RewardStageSettings
    {
        [Header("광원(패자 위 고정)")]
        [Tooltip("캐릭터 위 광원의 높이: 패자 발에서 잰 높이(유닛). 화면 위끝은 발에서 약 18.6 위")] public float lightHeight = 29f;
        [Tooltip("좌우 오프셋: 패자 x에서 광원까지(유닛). P2가 고르면 뒤집힌다")] public float lightOffsetX = 0f;
        [Header("원근")]
        [Range(0, .6f), Tooltip("가장 높은 비석(앞)보다 낮은 비석(뒤)일수록 꺼진 상태 색을 어둡게 하는 최대 정도")] public float depthDarken = .3f;
        [Header("점등")]
        [Min(.05f), Tooltip("호버한 비석이 아래에서 위로 다 켜지는 시간(초)")] public float litRiseSeconds = .38f;
        [Min(.05f), Tooltip("호버를 옮길 때 꺼지는 시간(초)")] public float litFallSeconds = .2f;
        [Min(.05f), Tooltip("다른 비석이 실루엣으로 어두워지는 시간(초)")] public float silhouetteSeconds = .25f;
        [Header("대기 중 패자")]
        [Range(0, 15), Tooltip("호버 쪽으로 몸을 기울이는 각도(도)")] public float leanDegrees = 5f;
        [Range(0, .08f), Tooltip("숨쉬기 크기(세로 비율)")] public float breath = .025f;
        [Header("진입 연출(솟아오름)")]
        [Min(0), Tooltip("선택 단계 시작부터 패자가 위에서 떨어지기 시작할 때까지(초)")] public float loserDropAt = .32f;
        [Min(.1f), Tooltip("패자가 떨어지는 시간(초)")] public float loserDropSeconds = .34f;
        [Min(0), Tooltip("선택 단계 시작부터 첫 비석이 땅속에서 울리기 시작할 때까지(초)")] public float riseAt = .76f;
        [Min(0), Tooltip("비석끼리 솟는 시작 간격(초). 패자에게 가까운 비석부터")] public float riseStagger = .15f;
        [Min(.05f), Tooltip("땅속에서 울리며 끝만 내미는 시간(초)")] public float rumbleSeconds = .12f;
        [Min(.15f), Tooltip("땅을 가르며 끝까지 솟는 시간(초). 처음에 세게 밀고 올라와 점점 느려지다 멈춘다")] public float riseSeconds = .45f;
        [Range(0, .3f), Tooltip("솟는 동안 좌우로 덜덜 떨리는 폭(유닛)")] public float grindJitter = .08f;
        [Range(0, .3f), Tooltip("솟는 동안 계속 주는 화면 흔들림(갈리는 박자마다)")] public float grindShake = .05f;
        [Range(0, 1), Tooltip("다 솟아 멈출 때의 화면 흔들림")] public float impactShake = .35f;
        [Min(.05f), Tooltip("멈춘 뒤 플레이어 색이 위에서 아래로 걷히며 어두운 카드 내용이 드러나는 시간(초). 비석은 플레이어 색으로 덮인 채 솟는다")] public float energyRecedeSeconds = .3f;
        [Min(0), Tooltip("마지막 비석이 멈춘 뒤 조명이 팍 켜지기까지(초). 이때 선택 중 표시가 나오고 고를 수 있다")] public float lightOnDelay = .12f;

        public RewardStageSettings Clone() => (RewardStageSettings)MemberwiseClone();
    }
}
