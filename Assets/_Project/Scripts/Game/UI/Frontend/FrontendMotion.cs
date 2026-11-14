using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>공통 연출 곡선. 경기 진행 시각은 시뮬레이션의 PhaseTick에서 받는다.</summary>
    public static class FrontendMotion
    {
        public static float Range(float value, float start, float end) => Mathf.InverseLerp(start, end, value);
        public static float Out(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float In(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float Smooth(float t) => Mathf.SmoothStep(0, 1, t);
        public static float Pop(float t)
        {
            t = Mathf.Clamp01(t) - 1;
            return 1 + 2.2f * t * t * t + 1.2f * t * t;
        }
        /// <summary>목표를 살짝 지나쳤다 돌아오는 감속(오버슈트). overshoot 1.7이면 약 10% 넘친다.</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1;
            return 1 + (overshoot + 1) * t * t * t + overshoot * t * t;
        }
        public static float InQuad(float t) { t = Mathf.Clamp01(t); return t * t; }
        public static float OutQuad(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t); }
        public static Vector2 Arc(Vector2 a, Vector2 b, float t, float height)
            => Vector2.Lerp(a, b, t) + Vector2.up * (4 * height * t * (1 - t));
    }

}
