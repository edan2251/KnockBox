using UnityEngine;

namespace Knockbox.Game.UI
{
    /// <summary>모놀리스 배치·호버 상태 계산. 상태가 없는 순수 함수라 검사에서 그대로 확인한다.</summary>
    public static class CardPickLayout
    {
        /// <summary>
        /// 저장된 배치는 P1(왼쪽)이 고르는 모습이다. P2가 고르면 좌우를 뒤집어 패자 반대편에 놓는다.
        /// </summary>
        public static Vector3 Place(Vector3 authored, int picker) => picker == 1 ? new Vector3(-authored.x, authored.y, authored.z) : authored;

        /// <summary>솟는 순서: 높은(큰) 비석이 먼저, 높이가 같으면 패자에게 가까운 것, 그다음 칸 번호.</summary>
        public static bool RisesBefore(float heightA, float distanceA, int indexA, float heightB, float distanceB, int indexB) =>
            Mathf.Abs(heightA - heightB) > 1e-3f ? heightA > heightB
            : Mathf.Abs(distanceA - distanceB) > 1e-3f ? distanceA < distanceB : indexA < indexB;

        /// <summary>화면 왼쪽부터 선택지 0, 1, 2… 순서가 되도록 모놀리스마다 선택지 번호를 준다. 쓰지 않는 칸은 -1.</summary>
        public static void AssignOptions(float[] placedX, int optionCount, int[] options)
        {
            int used = Mathf.Min(optionCount, placedX.Length);
            for (int i = 0; i < options.Length; i++) options[i] = -1;
            for (int option = 0; option < used; option++)
            {
                int best = -1;
                for (int i = 0; i < used; i++)
                    if (options[i] < 0 && (best < 0 || placedX[i] < placedX[best])) best = i;
                options[best] = option;
            }
        }

        /// <summary>
        /// 선택 장면의 고정 광원: 고르는 패자의 바로 위(캐릭터와 같은 x). P2가 고르면 좌우 오프셋도 뒤집힌다.
        /// height는 패자 발 위치에서 잰 높이다.
        /// </summary>
        public static Vector2 FixedLight(Vector2 foot, int picker, float height, float offsetX) =>
            new Vector2(foot.x + (picker == 1 ? -offsetX : offsetX), foot.y + height);

        /// <summary>
        /// 보상 맵 비석(맵 좌표 꼭짓점)을 모놀리스 자리(아랫변 가운데)와 그 자리 기준 윤곽으로 나눈다.
        /// </summary>
        public static Vector2[] LocalOutline(Maps.RewardStele stele, out Vector2 origin)
        {
            var b = stele.Bounds;
            origin = new Vector2(b.center.x, b.yMin);
            var local = (Vector2[])stele.vertices.Clone();
            for (int k = 0; k < local.Length; k++) local[k] -= origin;
            return local;
        }

        /// <summary>패자가 비석 쪽을 보는 방향(+1 오른쪽). 저장 배치는 P1 기준이며 P2가 고르면 뒤집는다.</summary>
        public static int FacingToward(float standX, float targetX, int picker) => (targetX >= standX ? 1 : -1) * (picker == 1 ? -1 : 1);

        /// <summary>윤곽을 좌우로 뒤집는다(반시계 순서 유지). P2가 고를 때 쓴다.</summary>
        public static Vector2[] Mirror(Vector2[] local)
        {
            var result = new Vector2[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                var p = local[local.Length - 1 - i];
                result[i] = new Vector2(-p.x, p.y);
            }
            return result;
        }

        /// <summary>
        /// 상태 3단계의 목표값. 처음에는 모두 꺼짐(0, 0). 한 번이라도 호버한 뒤에는 호버하지 않은 모놀리스의 그림만 어둡다(silhouette 1).
        /// 호버한 것은 점등(lit 1). 확정 뒤에는 고른 것만 켜고 나머지는 어둡게 둔다.
        /// </summary>
        public static void HoverTargets(int index, int hovered, int chosen, bool everHovered, out float lit, out float silhouette)
        {
            if (chosen >= 0)
            {
                lit = index == chosen ? 1 : 0;
                silhouette = index == chosen ? 0 : 1;
                return;
            }
            lit = index == hovered ? 1 : 0;
            silhouette = everHovered && index != hovered ? 1 : 0;
        }
    }
}
