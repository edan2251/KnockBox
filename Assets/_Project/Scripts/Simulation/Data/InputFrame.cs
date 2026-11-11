using System;

namespace Knockbox.Simulation
{
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Jump = 1 << 0,
        Attack = 1 << 1,
        Dash = 1 << 2,
        Guard = 1 << 3,
        Grab = 1 << 4,
        /// <summary>폼 시대의 폼 전환 비트. 동작은 없지만 기록 형식과 키보드 입력(R)이 그대로 싣는다(2026-11-11 폼 전투 제거, 2단계에서 정리).</summary>
        SwitchForm = 1 << 5,
        /// <summary>
        /// 좌우 키가 동시에 눌려 있음(CR-004 7절). 이때 MoveX는 상쇄되어 0이므로 "아무것도 안 누름"과 구분하려고 따로 싣는다.
        /// 버튼이 아니라 상태 표시라 눌림/뗌 계산에는 쓰지 않는다.
        /// </summary>
        BothHorizontal = 1 << 6,
        /// <summary>Brawl 조작 개정: 기본 펀치(Attack)와 분리한 박치기 준비·차징 버튼.</summary>
        Charge = 1 << 7,
        All = Jump | Attack | Dash | Guard | Grab | SwitchForm | BothHorizontal | Charge,
    }

    /// <summary>
    /// 한 틱의 한 플레이어 입력. 방향은 Game 계층이 8방향으로 양자화해서 넘긴다.
    /// 조준(CR-004)은 마우스 커서 방향을 64방향으로 양자화한 값이다: 0이면 없음(이동 방향·바라보는 쪽을 쓴다), 1~64는 방향 번호 + 1.
    /// "이번 틱에 눌렀다/뗐다"는 시뮬레이션이 직전 버튼 값과 비교해서 계산한다.
    /// </summary>
    [Serializable]
    public struct InputFrame : IEquatable<InputFrame>
    {
        public sbyte MoveX;
        public sbyte MoveY;
        public InputButtons Buttons;
        /// <summary>0 없음, 1~64 = AimDirections 번호 + 1.</summary>
        public byte Aim;

        public InputFrame(int moveX, int moveY, InputButtons buttons)
            : this(moveX, moveY, buttons, 0)
        {
        }

        public InputFrame(int moveX, int moveY, InputButtons buttons, byte aim)
        {
            MoveX = (sbyte)SimMath.Clamp(moveX, -1, 1);
            MoveY = (sbyte)SimMath.Clamp(moveY, -1, 1);
            Buttons = buttons & InputButtons.All;
            Aim = aim <= AimDirections.Count ? aim : (byte)0;
        }

        public bool HasAim => Aim != 0;

        public bool Held(InputButtons button) => (Buttons & button) != 0;

        public static InputButtons Pressed(InputButtons current, InputButtons previous) => current & ~previous;
        public static InputButtons Released(InputButtons current, InputButtons previous) => previous & ~current;

        /// <summary>기록 파일용 바이트 수(기록 형식 5부터 조준 포함 4바이트, 4 이하는 3바이트).</summary>
        public const int Size = 4;
        public const int LegacySize = 3;

        /// <summary>기록 파일용 4바이트 표현.</summary>
        public void Write(byte[] buffer, int offset)
        {
            buffer[offset] = unchecked((byte)MoveX);
            buffer[offset + 1] = unchecked((byte)MoveY);
            buffer[offset + 2] = (byte)Buttons;
            buffer[offset + 3] = Aim;
        }

        public static InputFrame Read(byte[] buffer, int offset) =>
            new InputFrame(unchecked((sbyte)buffer[offset]), unchecked((sbyte)buffer[offset + 1]), (InputButtons)buffer[offset + 2], buffer[offset + 3]);

        /// <summary>조준이 없던 기록 형식(4 이하)의 3바이트 표현.</summary>
        public static InputFrame ReadLegacy(byte[] buffer, int offset) =>
            new InputFrame(unchecked((sbyte)buffer[offset]), unchecked((sbyte)buffer[offset + 1]), (InputButtons)buffer[offset + 2]);

        public bool Equals(InputFrame other) => MoveX == other.MoveX && MoveY == other.MoveY && Buttons == other.Buttons && Aim == other.Aim;
        public override bool Equals(object obj) => obj is InputFrame other && Equals(other);
        public override int GetHashCode() => (MoveX + 1) | ((MoveY + 1) << 2) | ((int)Buttons << 4) | (Aim << 12);
        public static bool operator ==(InputFrame a, InputFrame b) => a.Equals(b);
        public static bool operator !=(InputFrame a, InputFrame b) => !a.Equals(b);
        public override string ToString() => Aim == 0 ? $"({MoveX},{MoveY}) {Buttons}" : $"({MoveX},{MoveY}) {Buttons} aim{Aim - 1}";
    }

    /// <summary>
    /// 64방향 조준(CR-004 5.1). 번호 k는 오른쪽(0)에서 반시계로 5.625°씩. 좌우로 뒤집은 방향은 (32 - k) mod 64.
    /// 단위 벡터는 1사분면 코사인 표(float 상수)를 사분면마다 부호·축만 바꿔 쓰므로 좌우·상하 대칭이 정확하다(삼각함수 호출 없음).
    /// </summary>
    public static class AimDirections
    {
        public const int Count = 64;
        const int Quarter = Count / 4;

        public static byte Encode(int index) => (byte)(((index % Count) + Count) % Count + 1);

        public static int Index(byte aim) => aim - 1;

        /// <summary>좌우로 뒤집은 조준. 없으면 없음.</summary>
        public static byte Mirror(byte aim) => aim == 0 ? (byte)0 : Encode(Quarter * 2 - Index(aim));

        /// <summary>조준 단위 벡터. 조준이 없으면 false.</summary>
        public static bool TryGet(byte aim, out Vec2 direction)
        {
            if (aim == 0 || aim > Count)
            {
                direction = default;
                return false;
            }
            direction = Direction(aim - 1);
            return true;
        }

        public static Vec2 Direction(int index)
        {
            int k = ((index % Count) + Count) % Count;
            int r = k % Quarter;
            float c = QuarterCos(r), s = QuarterCos(Quarter - r);
            switch (k / Quarter)
            {
                case 0: return new Vec2(c, s);
                case 1: return new Vec2(-s, c);
                case 2: return new Vec2(-c, -s);
                default: return new Vec2(s, -c);
            }
        }

        /// <summary>cos(r × 5.625°), r = 0~16.</summary>
        static float QuarterCos(int r)
        {
            switch (r)
            {
                case 0: return 1f;
                case 1: return 0.9951847f;
                case 2: return 0.98078525f;
                case 3: return 0.95694035f;
                case 4: return 0.9238795f;
                case 5: return 0.8819213f;
                case 6: return 0.8314696f;
                case 7: return 0.77301043f;
                case 8: return 0.70710677f;
                case 9: return 0.6343933f;
                case 10: return 0.55557024f;
                case 11: return 0.47139674f;
                case 12: return 0.38268343f;
                case 13: return 0.29028466f;
                case 14: return 0.19509032f;
                case 15: return 0.09801714f;
                default: return 0f;
            }
        }
    }
}
