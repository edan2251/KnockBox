using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 구조물 강체 하나의 상태(09-props-physics.md 3절). 회전은 (cos, sin) 쌍으로 저장하고, 삼각함수를 쓰지 않는다.
    /// </summary>
    [Serializable]
    public struct PropBody
    {
        public Vec2 Position;
        /// <summary>회전 (cos, sin). 각속도로 돌린 뒤 정규화한다.</summary>
        public Vec2 Rotation;
        public Vec2 Velocity;
        public float AngularVelocity;
        /// <summary>마지막으로 친 파이터(0, 1, 없으면 -1). 나중의 카드 시스템과 통계용.</summary>
        public int LastToucher;
        /// <summary>이 구조물이 파이터 0·1을 다시 맞힐 수 있을 때까지 남은 틱(쌍마다 20틱).</summary>
        public int HitCooldown0;
        public int HitCooldown1;
        /// <summary>파이터 0·1의 돌진이 이 구조물을 다시 칠 수 있을 때까지 남은 틱(돌진 한 번에 한 번).</summary>
        public int StrikeCooldown0;
        public int StrikeCooldown1;
        /// <summary>연결(줄·축·경첩)이 끊어졌으면 1(CP15). 끊어진 몸은 자유롭게 떨어진다.</summary>
        public int Broken;
        /// <summary>거의 움직이지 않은 연속 틱 수. PropParams.SleepTicks 이상이면 잠들어 적분을 건너뛴다(결정론적, 충격을 받으면 0).</summary>
        public int SleepTicks;
        /// <summary>기둥에서 빠지는 중인 상자의 남은 틱(CP16 부분 붕괴). 0보다 크면 다른 상자와의 마찰이 0.</summary>
        public int LooseTicks;
        /// <summary>지난 틱 접촉의 충격량(웜 스타트, CP16). 되감아 다시 계산해도 같은 결과가 나오도록 상태에 둔다(09 3절).</summary>
        public PropWarm Warm;

        public readonly Vec2 ToWorld(Vec2 local) => Position + Rotate(Rotation, local);
        public readonly Vec2 ToLocal(Vec2 world) => InverseRotate(Rotation, world - Position);
        /// <summary>몸 위 한 점(월드)의 속도.</summary>
        public readonly Vec2 PointVelocity(Vec2 world) => Velocity + Cross(AngularVelocity, world - Position);

        public static Vec2 Rotate(Vec2 q, Vec2 v) => new Vec2(q.X * v.X - q.Y * v.Y, q.Y * v.X + q.X * v.Y);
        public static Vec2 InverseRotate(Vec2 q, Vec2 v) => new Vec2(q.X * v.X + q.Y * v.Y, -q.Y * v.X + q.X * v.Y);
        public static Vec2 Cross(float w, Vec2 r) => new Vec2(-w * r.Y, w * r.X);
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public readonly int GetHitCooldown(int fighter) => fighter == 0 ? HitCooldown0 : HitCooldown1;
        public readonly int GetStrikeCooldown(int fighter) => fighter == 0 ? StrikeCooldown0 : StrikeCooldown1;

        public void SetStrikeCooldown(int fighter, int ticks)
        {
            if (fighter == 0)
                StrikeCooldown0 = ticks;
            else
                StrikeCooldown1 = ticks;
        }

        public void SetHitCooldown(int fighter, int ticks)
        {
            if (fighter == 0)
                HitCooldown0 = ticks;
            else
                HitCooldown1 = ticks;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value("Position.X", Position.X);
            v.Value("Position.Y", Position.Y);
            v.Value("Rotation.X", Rotation.X);
            v.Value("Rotation.Y", Rotation.Y);
            v.Value("Velocity.X", Velocity.X);
            v.Value("Velocity.Y", Velocity.Y);
            v.Value(nameof(AngularVelocity), AngularVelocity);
            v.Value(nameof(LastToucher), LastToucher);
            v.Value(nameof(HitCooldown0), HitCooldown0);
            v.Value(nameof(HitCooldown1), HitCooldown1);
            v.Value(nameof(StrikeCooldown0), StrikeCooldown0);
            v.Value(nameof(StrikeCooldown1), StrikeCooldown1);
            v.Value(nameof(Broken), Broken);
            v.Value(nameof(SleepTicks), SleepTicks);
            v.Value(nameof(LooseTicks), LooseTicks);
            v.Begin(nameof(Warm));
            Warm.Visit(ref v);
            v.End();
        }
    }

    /// <summary>
    /// 구조물 강체 전부(고정 용량 48). 배열 대신 필드를 펼쳐 두어 SimState가 값 타입으로 남는다:
    /// 대입이 곧 스냅숏이고 Step에서 할당이 없다(09 3절). 이 파일은 스크립트로 만들었다(필드와 switch만 반복).
    /// Count개만 쓰며, 순서는 ArenaDef의 강체 구조물 순서와 같다.
    /// </summary>
    [Serializable]
    public struct PropState
    {
        public const int Capacity = 48;

        public int Count;

        public PropBody B00;
        public PropBody B01;
        public PropBody B02;
        public PropBody B03;
        public PropBody B04;
        public PropBody B05;
        public PropBody B06;
        public PropBody B07;
        public PropBody B08;
        public PropBody B09;
        public PropBody B10;
        public PropBody B11;
        public PropBody B12;
        public PropBody B13;
        public PropBody B14;
        public PropBody B15;
        public PropBody B16;
        public PropBody B17;
        public PropBody B18;
        public PropBody B19;
        public PropBody B20;
        public PropBody B21;
        public PropBody B22;
        public PropBody B23;
        public PropBody B24;
        public PropBody B25;
        public PropBody B26;
        public PropBody B27;
        public PropBody B28;
        public PropBody B29;
        public PropBody B30;
        public PropBody B31;
        public PropBody B32;
        public PropBody B33;
        public PropBody B34;
        public PropBody B35;
        public PropBody B36;
        public PropBody B37;
        public PropBody B38;
        public PropBody B39;
        public PropBody B40;
        public PropBody B41;
        public PropBody B42;
        public PropBody B43;
        public PropBody B44;
        public PropBody B45;
        public PropBody B46;
        public PropBody B47;

        public static ref PropBody Get(ref PropState s, int index)
        {
            switch (index)
            {
                case 0: return ref s.B00;
                case 1: return ref s.B01;
                case 2: return ref s.B02;
                case 3: return ref s.B03;
                case 4: return ref s.B04;
                case 5: return ref s.B05;
                case 6: return ref s.B06;
                case 7: return ref s.B07;
                case 8: return ref s.B08;
                case 9: return ref s.B09;
                case 10: return ref s.B10;
                case 11: return ref s.B11;
                case 12: return ref s.B12;
                case 13: return ref s.B13;
                case 14: return ref s.B14;
                case 15: return ref s.B15;
                case 16: return ref s.B16;
                case 17: return ref s.B17;
                case 18: return ref s.B18;
                case 19: return ref s.B19;
                case 20: return ref s.B20;
                case 21: return ref s.B21;
                case 22: return ref s.B22;
                case 23: return ref s.B23;
                case 24: return ref s.B24;
                case 25: return ref s.B25;
                case 26: return ref s.B26;
                case 27: return ref s.B27;
                case 28: return ref s.B28;
                case 29: return ref s.B29;
                case 30: return ref s.B30;
                case 31: return ref s.B31;
                case 32: return ref s.B32;
                case 33: return ref s.B33;
                case 34: return ref s.B34;
                case 35: return ref s.B35;
                case 36: return ref s.B36;
                case 37: return ref s.B37;
                case 38: return ref s.B38;
                case 39: return ref s.B39;
                case 40: return ref s.B40;
                case 41: return ref s.B41;
                case 42: return ref s.B42;
                case 43: return ref s.B43;
                case 44: return ref s.B44;
                case 45: return ref s.B45;
                case 46: return ref s.B46;
                case 47: return ref s.B47;
                default: throw new IndexOutOfRangeException($"구조물 번호 {index}(용량 {Capacity})");
            }
        }

        public static ref readonly PropBody At(in PropState s, int index)
        {
            switch (index)
            {
                case 0: return ref s.B00;
                case 1: return ref s.B01;
                case 2: return ref s.B02;
                case 3: return ref s.B03;
                case 4: return ref s.B04;
                case 5: return ref s.B05;
                case 6: return ref s.B06;
                case 7: return ref s.B07;
                case 8: return ref s.B08;
                case 9: return ref s.B09;
                case 10: return ref s.B10;
                case 11: return ref s.B11;
                case 12: return ref s.B12;
                case 13: return ref s.B13;
                case 14: return ref s.B14;
                case 15: return ref s.B15;
                case 16: return ref s.B16;
                case 17: return ref s.B17;
                case 18: return ref s.B18;
                case 19: return ref s.B19;
                case 20: return ref s.B20;
                case 21: return ref s.B21;
                case 22: return ref s.B22;
                case 23: return ref s.B23;
                case 24: return ref s.B24;
                case 25: return ref s.B25;
                case 26: return ref s.B26;
                case 27: return ref s.B27;
                case 28: return ref s.B28;
                case 29: return ref s.B29;
                case 30: return ref s.B30;
                case 31: return ref s.B31;
                case 32: return ref s.B32;
                case 33: return ref s.B33;
                case 34: return ref s.B34;
                case 35: return ref s.B35;
                case 36: return ref s.B36;
                case 37: return ref s.B37;
                case 38: return ref s.B38;
                case 39: return ref s.B39;
                case 40: return ref s.B40;
                case 41: return ref s.B41;
                case 42: return ref s.B42;
                case 43: return ref s.B43;
                case 44: return ref s.B44;
                case 45: return ref s.B45;
                case 46: return ref s.B46;
                case 47: return ref s.B47;
                default: throw new IndexOutOfRangeException($"구조물 번호 {index}(용량 {Capacity})");
            }
        }

        static string Name(int index)
        {
            switch (index)
            {
                case 0: return "B0";
                case 1: return "B1";
                case 2: return "B2";
                case 3: return "B3";
                case 4: return "B4";
                case 5: return "B5";
                case 6: return "B6";
                case 7: return "B7";
                case 8: return "B8";
                case 9: return "B9";
                case 10: return "B10";
                case 11: return "B11";
                case 12: return "B12";
                case 13: return "B13";
                case 14: return "B14";
                case 15: return "B15";
                case 16: return "B16";
                case 17: return "B17";
                case 18: return "B18";
                case 19: return "B19";
                case 20: return "B20";
                case 21: return "B21";
                case 22: return "B22";
                case 23: return "B23";
                case 24: return "B24";
                case 25: return "B25";
                case 26: return "B26";
                case 27: return "B27";
                case 28: return "B28";
                case 29: return "B29";
                case 30: return "B30";
                case 31: return "B31";
                case 32: return "B32";
                case 33: return "B33";
                case 34: return "B34";
                case 35: return "B35";
                case 36: return "B36";
                case 37: return "B37";
                case 38: return "B38";
                case 39: return "B39";
                case 40: return "B40";
                case 41: return "B41";
                case 42: return "B42";
                case 43: return "B43";
                case 44: return "B44";
                case 45: return "B45";
                case 46: return "B46";
                case 47: return "B47";
                default: return "B?";
            }
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            // 용량 전부를 방문한다(쓰지 않는 칸은 0이지만, 상태의 모든 필드가 체크섬·StateDiff에 들어가야 한다).
            v.Value(nameof(Count), Count);
            for (int i = 0; i < Capacity; i++)
            {
                v.Begin(Name(i));
                At(in this, i).Visit(ref v);
                v.End();
            }
        }
    }
}
