using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Knockbox.Game
{
    public enum KeyboardLayout
    {
        /// <summary>WASD, Space 점프, F 공격, Left Shift 대시, E 가드, Q 잡기.</summary>
        Left,
        /// <summary>방향키, Numpad 0 점프, 1 공격, 2 대시, 3 가드, 4 잡기.</summary>
        Right,
    }

    /// <summary>키보드 입력(06-match-and-tools.md 2절). 좌우나 상하가 동시에 눌리면 상쇄한다.</summary>
    public sealed class KeyboardInputSource : IInputSource
    {
        readonly KeyboardLayout layout;

        public KeyboardInputSource(KeyboardLayout layout)
        {
            this.layout = layout;
        }

        public InputFrame Poll(int tick)
        {
            var k = Keyboard.current;
            if (k == null)
                return default;
            bool left = layout == KeyboardLayout.Left;
            int x = (Held(left ? k.dKey : k.rightArrowKey) ? 1 : 0) - (Held(left ? k.aKey : k.leftArrowKey) ? 1 : 0);
            int y = (Held(left ? k.wKey : k.upArrowKey) ? 1 : 0) - (Held(left ? k.sKey : k.downArrowKey) ? 1 : 0);
            var buttons = InputButtons.None;
            if (Held(left ? k.spaceKey : k.numpad0Key)) buttons |= InputButtons.Jump;
            if (Held(left ? k.fKey : k.numpad1Key)) buttons |= InputButtons.Attack;
            if (Held(left ? k.leftShiftKey : k.numpad2Key)) buttons |= InputButtons.Dash;
            if (Held(left ? k.eKey : k.numpad3Key)) buttons |= InputButtons.Guard;
            if (Held(left ? k.qKey : k.numpad4Key)) buttons |= InputButtons.Grab;
            if (Held(left ? k.rKey : k.numpad5Key)) buttons |= InputButtons.SwitchForm;
            if (Held(left ? k.dKey : k.rightArrowKey) && Held(left ? k.aKey : k.leftArrowKey)) buttons |= InputButtons.BothHorizontal;
            return new InputFrame(x, y, buttons);
        }

        static bool Held(ButtonControl key) => key != null && key.isPressed;
    }

    /// <summary>
    /// 게임패드 입력. 왼쪽 스틱(데드존 0.35)과 방향 패드를 8방향으로 양자화한다.
    /// A 점프, X 공격, B·RT 대시, LB·LT 가드, Y 잡기.
    /// </summary>
    public sealed class GamepadInputSource : IInputSource
    {
        public const float DeadZone = 0.35f;
        readonly int padIndex;

        public GamepadInputSource(int padIndex)
        {
            this.padIndex = padIndex;
        }

        public static bool Available(int padIndex) => Gamepad.all.Count > padIndex;

        public InputFrame Poll(int tick)
        {
            if (Gamepad.all.Count <= padIndex)
                return default;
            var pad = Gamepad.all[padIndex];
            Vector2 stick = pad.leftStick.ReadValue();
            int x = Quantize(stick.x), y = Quantize(stick.y);
            if (pad.dpad.right.isPressed) x = 1;
            if (pad.dpad.left.isPressed) x = -1;
            if (pad.dpad.up.isPressed) y = 1;
            if (pad.dpad.down.isPressed) y = -1;
            var buttons = InputButtons.None;
            if (pad.buttonSouth.isPressed) buttons |= InputButtons.Jump;
            if (pad.buttonWest.isPressed) buttons |= InputButtons.Attack;
            if (pad.buttonEast.isPressed || pad.rightTrigger.isPressed) buttons |= InputButtons.Dash;
            if (pad.leftShoulder.isPressed || pad.leftTrigger.isPressed) buttons |= InputButtons.Guard;
            if (pad.buttonNorth.isPressed) buttons |= InputButtons.Grab;
            if (pad.rightShoulder.isPressed) buttons |= InputButtons.SwitchForm;
            return new InputFrame(x, y, buttons);
        }

        /// <summary>
        /// 축 하나를 -1, 0, 1로. 데드존 밖에서 각 축을 따로 자르면 8방향이 된다
        /// (대각선은 두 축이 모두 데드존을 넘을 때).
        /// </summary>
        public static int Quantize(float axis) => axis > DeadZone ? 1 : axis < -DeadZone ? -1 : 0;
    }
}
