using System;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knockbox.Game
{
    /// <summary>
    /// CR-004 실험 조작: WASD, Space 점프, 좌클릭 탭 주먹/무기·홀드 박치기. Shift는 비워 둔다.
    /// 우클릭 = 잡기(단계 C). 조준은 파이터 몸 중심에서 커서까지의 방향을 64방향으로 양자화해 입력에 담는다.
    /// </summary>
    public sealed class MouseAimInputSource : IInputSource
    {
        readonly KeyboardInputSource keyboard = new KeyboardInputSource(KeyboardLayout.Left);
        readonly Func<SimWorld> world;
        readonly int fighter;

        public MouseAimInputSource(Func<SimWorld> world, int fighter)
        {
            this.world = world;
            this.fighter = fighter;
        }

        public InputFrame Poll(int tick)
        {
            var frame = keyboard.Poll(tick);
            var buttons = frame.Buttons;
            // 현재 Brawl은 좌클릭으로 탭/홀드를 구분한다. 공용 키보드의 Shift 대시는 전달하지 않는다.
            buttons &= ~(InputButtons.Dash | InputButtons.Charge);
            var mouse = Mouse.current;
            if (mouse == null)
                return new InputFrame(frame.MoveX, frame.MoveY, buttons, frame.Aim);
            bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (mouse.leftButton.isPressed && !overUi)
                buttons |= InputButtons.Attack;
            if (mouse.rightButton.isPressed && !overUi)
                buttons |= InputButtons.Grab;
            byte aim = 0;
            var w = world();
            var cam = Camera.main;
            if (w != null && cam != null)
            {
                var p = w.State.GetFighter(fighter).Position;
                aim = AimInput.FromScreen(cam, mouse.position.ReadValue(), new Vector2(p.X, p.Y));
            }
            return new InputFrame(frame.MoveX, frame.MoveY, buttons, aim);
        }
    }

    /// <summary>Game 계층의 조준 양자화(시뮬레이션은 번호만 받는다).</summary>
    public static class AimInput
    {
        /// <summary>방향 → 조준 값(1~64). 길이가 거의 0이면 0(없음).</summary>
        public static byte Quantize(Vector2 delta)
        {
            if (delta.sqrMagnitude < 1e-6f)
                return 0;
            float step = 2f * Mathf.PI / AimDirections.Count;
            int index = Mathf.RoundToInt(Mathf.Atan2(delta.y, delta.x) / step);
            return AimDirections.Encode(index);
        }

        /// <summary>화면 좌표의 커서 → 월드 원점(origin)에서 본 조준 값.</summary>
        public static byte FromScreen(Camera cam, Vector2 screen, Vector2 origin)
        {
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            return Quantize((Vector2)world - origin);
        }
    }
}
