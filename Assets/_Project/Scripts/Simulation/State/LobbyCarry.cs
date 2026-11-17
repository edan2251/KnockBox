using System;
using System.IO;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 방장이 혼자 로비에서 움직이던 P1을 공유 로비의 시작 상태로 이어 가는 값(2026-11-17 로비 입장 A안).
    /// 위치·속도·방향만 옮기고 동작·버튼·준비는 새로 시작한다. 로비 맵에는 물리 상자·로프가 없어 소품은 옮기지 않는다.
    /// 시작 상태·확정 입력 첫 묶음·입력 기록(v10)이 같은 값을 써서 두 PC와 리플레이가 같은 시작 상태를 만든다.
    /// </summary>
    public struct LobbyCarry : IEquatable<LobbyCarry>
    {
        /// <summary>바이트 크기: 있음(1) + 위치·속도 float 4개(16) + 방향(1).</summary>
        public const int Size = 18;
        /// <summary>네트워크·기록에서 받아들이는 속도 한계(단위/초). 실제 로비 이동보다 넉넉하다.</summary>
        public const float MaxSpeed = 500f;

        public bool Has;
        public Vec2 Position;
        public Vec2 Velocity;
        public sbyte Facing;

        /// <summary>혼자 로비의 P1을 이어 갈 수 있으면 그 값을, 아니면(낙하 중·KO·맵 밖 등) 빈 값을 돌려준다(빈 값이면 P1도 다시 떨어진다).</summary>
        public static LobbyCarry Capture(SimConfig config, ArenaDef lobby, in FighterState f)
        {
            if (lobby == null || f.Arriving || f.Action == ActionState.KO || f.HitstopTicks > 600) return default;
            var carry = new LobbyCarry { Has = true, Position = f.Position, Velocity = f.Velocity, Facing = f.Facing < 0 ? (sbyte)-1 : (sbyte)1 };
            return carry.IsValid(config, lobby) ? carry : default;
        }

        /// <summary>값이 로비 맵 안의 유한한 위치·속도이고 방향이 ±1인지. 없음(Has 거짓)은 늘 유효하다.</summary>
        public readonly bool IsValid(SimConfig config, ArenaDef lobby)
        {
            if (!Has) return Position.X == 0 && Position.Y == 0 && Velocity.X == 0 && Velocity.Y == 0 && Facing == 0;
            if (lobby == null || (Facing != 1 && Facing != -1)) return false;
            if (!Finite(Position.X) || !Finite(Position.Y) || !Finite(Velocity.X) || !Finite(Velocity.Y)) return false;
            float limitX = lobby.BoundaryHalfWidth - config.HalfBody, limitY = lobby.BoundaryHalfHeight - config.HalfBody;
            return SimMath.Abs(Position.X) <= limitX && SimMath.Abs(Position.Y) <= limitY
                && SimMath.Abs(Velocity.X) <= MaxSpeed && SimMath.Abs(Velocity.Y) <= MaxSpeed;
        }

        /// <summary>로비 시작 상태(SimState.CreateMatch Lobby)의 P1을 이 값으로 바꾼다. P2는 그대로 떨어진다.</summary>
        public readonly void Apply(SimConfig config, ArenaDef[] arenas, ref SimState state)
        {
            if (!Has) return;
            int index = SimState.LobbyArenaIndex(config, arenas);
            if (index < 0 || state.Match.Phase != MatchPhase.Lobby)
                throw new InvalidOperationException("로비 이어 가기는 로비 시작 상태에만 적용합니다.");
            if (!IsValid(config, arenas[index]))
                throw new InvalidOperationException("로비 이어 가기 값이 로비 맵 범위를 벗어났습니다.");
            var keep = state.Fighter0;
            var f = SimState.CreateFighter(config, arenas[index], Position, Facing, 0);
            SimState.KeepMatchPart(ref f, keep);
            f.PrevButtons = keep.PrevButtons;
            f.Velocity = Velocity;
            f.Arriving = false;
            state.Fighter0 = f;
        }

        public readonly void Write(byte[] b, int o)
        {
            b[o] = Has ? (byte)1 : (byte)0;
            WriteInt(b, o + 1, BitConverter.SingleToInt32Bits(Position.X));
            WriteInt(b, o + 5, BitConverter.SingleToInt32Bits(Position.Y));
            WriteInt(b, o + 9, BitConverter.SingleToInt32Bits(Velocity.X));
            WriteInt(b, o + 13, BitConverter.SingleToInt32Bits(Velocity.Y));
            b[o + 17] = unchecked((byte)Facing);
        }

        public static LobbyCarry Read(byte[] b, int o)
        {
            if (b[o] > 1) throw new InvalidDataException("로비 이어 가기 표시가 잘못됨");
            return new LobbyCarry
            {
                Has = b[o] == 1,
                Position = new Vec2(BitConverter.Int32BitsToSingle(ReadInt(b, o + 1)), BitConverter.Int32BitsToSingle(ReadInt(b, o + 5))),
                Velocity = new Vec2(BitConverter.Int32BitsToSingle(ReadInt(b, o + 9)), BitConverter.Int32BitsToSingle(ReadInt(b, o + 13))),
                Facing = unchecked((sbyte)b[o + 17]),
            };
        }

        public readonly bool Equals(LobbyCarry other) => Has == other.Has && Facing == other.Facing
            && BitConverter.SingleToInt32Bits(Position.X) == BitConverter.SingleToInt32Bits(other.Position.X)
            && BitConverter.SingleToInt32Bits(Position.Y) == BitConverter.SingleToInt32Bits(other.Position.Y)
            && BitConverter.SingleToInt32Bits(Velocity.X) == BitConverter.SingleToInt32Bits(other.Velocity.X)
            && BitConverter.SingleToInt32Bits(Velocity.Y) == BitConverter.SingleToInt32Bits(other.Velocity.Y);
        public override readonly bool Equals(object obj) => obj is LobbyCarry other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Has, Position.X, Position.Y, Velocity.X, Velocity.Y, Facing);

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static void WriteInt(byte[] b, int o, int value) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(value >> (8 * i)); }
        static int ReadInt(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    }
}
