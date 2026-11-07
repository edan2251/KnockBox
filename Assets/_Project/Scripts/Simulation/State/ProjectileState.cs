using System;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 투사체 하나(10-forms-and-growth.md 4.3, CP19). 비활성 칸은 기본값으로 지워 둔다(체크섬이 칸 내용과 무관하게 같도록).
    /// </summary>
    [Serializable]
    public struct Projectile
    {
        public bool Active;
        /// <summary>지금 소유자(0, 1). 반사되면 반사한 파이터로 바뀐다.</summary>
        public int Owner;
        public Vec2 Position;
        public Vec2 Velocity;
        public bool Reflectable;
        public bool PierceFighter;
        public bool PierceProp;
        /// <summary>지금까지 반사된 횟수(연속 반사 상한용).</summary>
        public int Reflections;
        /// <summary>이미 맞힌 파이터 비트(1 &lt;&lt; 번호). 관통 투사체가 같은 대상을 두 번 맞히지 않게.</summary>
        public int HitMask;
        public int Age;
        /// <summary>발사한 틱. 풀이 차면 가장 오래된(작은) 것부터 지운다.</summary>
        public int SpawnTick;

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Value(nameof(Active), Active);
            v.Value(nameof(Owner), Owner);
            v.Value("Position.X", Position.X);
            v.Value("Position.Y", Position.Y);
            v.Value("Velocity.X", Velocity.X);
            v.Value("Velocity.Y", Velocity.Y);
            v.Value(nameof(Reflectable), Reflectable);
            v.Value(nameof(PierceFighter), PierceFighter);
            v.Value(nameof(PierceProp), PierceProp);
            v.Value(nameof(Reflections), Reflections);
            v.Value(nameof(HitMask), HitMask);
            v.Value(nameof(Age), Age);
            v.Value(nameof(SpawnTick), SpawnTick);
        }
    }

    /// <summary>
    /// 투사체 풀: 파이터마다 PerFighter칸(파이터 0은 0~5, 1은 6~11)의 고정 용량 값 타입.
    /// 대입이 곧 스냅숏이고 Step에서 할당이 없다. 이 파일은 스크립트로 만들었다(필드와 switch만 반복).
    /// </summary>
    [Serializable]
    public struct ProjectileState
    {
        public const int PerFighter = 6;
        public const int Capacity = PerFighter * 2;

        public Projectile P00;
        public Projectile P01;
        public Projectile P02;
        public Projectile P03;
        public Projectile P04;
        public Projectile P05;
        public Projectile P06;
        public Projectile P07;
        public Projectile P08;
        public Projectile P09;
        public Projectile P10;
        public Projectile P11;

        public static ref Projectile Get(ref ProjectileState s, int index)
        {
            switch (index)
            {
                case 0: return ref s.P00;
                case 1: return ref s.P01;
                case 2: return ref s.P02;
                case 3: return ref s.P03;
                case 4: return ref s.P04;
                case 5: return ref s.P05;
                case 6: return ref s.P06;
                case 7: return ref s.P07;
                case 8: return ref s.P08;
                case 9: return ref s.P09;
                case 10: return ref s.P10;
                case 11: return ref s.P11;
                default: throw new IndexOutOfRangeException($"투사체 번호 {index}(용량 {Capacity})");
            }
        }

        public static ref readonly Projectile At(in ProjectileState s, int index)
        {
            switch (index)
            {
                case 0: return ref s.P00;
                case 1: return ref s.P01;
                case 2: return ref s.P02;
                case 3: return ref s.P03;
                case 4: return ref s.P04;
                case 5: return ref s.P05;
                case 6: return ref s.P06;
                case 7: return ref s.P07;
                case 8: return ref s.P08;
                case 9: return ref s.P09;
                case 10: return ref s.P10;
                case 11: return ref s.P11;
                default: throw new IndexOutOfRangeException($"투사체 번호 {index}(용량 {Capacity})");
            }
        }

        public readonly int CountActive(int owner)
        {
            int n = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ref readonly var p = ref At(this, i);
                if (p.Active && (owner < 0 || p.Owner == owner))
                    n++;
            }
            return n;
        }

        public readonly void Visit<T>(ref T v) where T : struct, IStateVisitor
        {
            v.Begin("P00");
            P00.Visit(ref v);
            v.End();
            v.Begin("P01");
            P01.Visit(ref v);
            v.End();
            v.Begin("P02");
            P02.Visit(ref v);
            v.End();
            v.Begin("P03");
            P03.Visit(ref v);
            v.End();
            v.Begin("P04");
            P04.Visit(ref v);
            v.End();
            v.Begin("P05");
            P05.Visit(ref v);
            v.End();
            v.Begin("P06");
            P06.Visit(ref v);
            v.End();
            v.Begin("P07");
            P07.Visit(ref v);
            v.End();
            v.Begin("P08");
            P08.Visit(ref v);
            v.End();
            v.Begin("P09");
            P09.Visit(ref v);
            v.End();
            v.Begin("P10");
            P10.Visit(ref v);
            v.End();
            v.Begin("P11");
            P11.Visit(ref v);
            v.End();
        }
    }
}
