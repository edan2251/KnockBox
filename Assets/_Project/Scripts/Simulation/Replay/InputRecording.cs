using System;
using System.Collections.Generic;
using System.IO;

namespace Knockbox.Simulation
{
    /// <summary>
    /// 경기 입력 기록(01-architecture.md 6절). 양쪽 입력, 맵, 모드, 설정 체크섬, 시작 상태를 만드는 값,
    /// 시작·최종 체크섬을 담는다. 재생 결과의 최종 체크섬이 기록 시점과 같아야 한다.
    /// 맵 순환 경기(버전 3)는 맵 이름을 쉼표로 이어 Arena에, 순환 상태를 Rotation에 담는다.
    /// </summary>
    public sealed class InputRecording
    {
        public const uint Magic = 0x5052424B; // "KBRP"
        /// <summary>4: CR-003(CP18). Mode 칸에 규칙(RuleSet.Format)을 쓰고 상태에 폼·에너지가 들어갔다. 3 이하 기록은 설정 체크섬이 달라 재생할 수 없다.</summary>
        /// <summary>8: 좌클릭 탭/홀드와 무기 투척. 프레임은 5부터 같은 4바이트다. 구 조작·무기 규칙은 설정 체크섬으로 구분한다.</summary>
        public const ushort FormatVersion = 8;
        const ushort WeaponsVersion = 7;
        const ushort ChargeVersion = 6;
        const ushort AimVersion = 5;
        const ushort NoAimVersion = 4;
        public const char ArenaSeparator = ',';

        readonly List<InputFrame> p1 = new List<InputFrame>();
        readonly List<InputFrame> p2 = new List<InputFrame>();

        public uint ConfigChecksum;
        public uint ArenaChecksum;
        public uint Seed;
        public Vec2 Spawn0;
        public Vec2 Spawn1;
        public MatchPhase StartPhase;
        public string Arena = "";
        public string Mode = "";
        public ArenaRotation Rotation;
        public uint InitialChecksum;
        public bool HasFinalChecksum;
        public uint FinalChecksum;

        public int Count => p1.Count;
        public InputFrame GetP1(int index) => p1[index];
        public InputFrame GetP2(int index) => p2[index];

        public static InputRecording Begin(SimConfig config, ArenaDef arena, uint seed, Vec2 spawn0, Vec2 spawn1, MatchPhase startPhase, string mode) =>
            Begin(config, new[] { arena }, default, seed, spawn0, spawn1, startPhase, mode);

        public static InputRecording Begin(SimConfig config, ArenaDef[] arenas, in ArenaRotation rotation, uint seed, Vec2 spawn0, Vec2 spawn1, MatchPhase startPhase, string mode)
        {
            var names = new string[arenas.Length];
            for (int i = 0; i < arenas.Length; i++)
                names[i] = arenas[i].Name ?? "";
            var rec = new InputRecording
            {
                ConfigChecksum = config.ComputeChecksum(),
                ArenaChecksum = ComputeArenaChecksum(arenas),
                Seed = seed,
                Spawn0 = spawn0,
                Spawn1 = spawn1,
                StartPhase = startPhase,
                Arena = string.Join(ArenaSeparator.ToString(), names),
                Mode = mode ?? "",
                Rotation = rotation,
            };
            rec.InitialChecksum = rec.BuildState(config, arenas).ComputeChecksum();
            return rec;
        }

        /// <summary>기록에 담긴 맵 이름들(순환 순서 그대로).</summary>
        public string[] ArenaNames => Arena.Split(ArenaSeparator);

        /// <summary>맵 하나면 그 맵의 체크섬, 순환이면 맵 체크섬들을 이은 해시.</summary>
        public static uint ComputeArenaChecksum(ArenaDef[] arenas)
        {
            if (arenas.Length == 1)
                return arenas[0].ComputeChecksum();
            var h = Hasher.Create();
            h.AddInt(arenas.Length);
            for (int i = 0; i < arenas.Length; i++)
                h.AddUInt(arenas[i].ComputeChecksum());
            return h.Hash;
        }

        /// <summary>검사 없이 기록의 시작 상태를 만든다(설정이 달라도 재생해 보는 디버그 재생용).</summary>
        public SimState BuildState(SimConfig config, ArenaDef[] arenas)
        {
            var state = SimState.CreateRound(config, arenas[Rotation.Current], Seed, Spawn0, Spawn1, StartPhase);
            state.Match.Rotation = Rotation;
            return state;
        }

        public void Append(in InputFrame a, in InputFrame b)
        {
            p1.Add(a);
            p2.Add(b);
        }

        public void Finish(uint finalChecksum)
        {
            FinalChecksum = finalChecksum;
            HasFinalChecksum = true;
        }

        /// <summary>기록의 시작 상태를 다시 만든다. 설정이나 시작 상태가 기록과 다르면 예외.</summary>
        public SimState CreateInitialState(SimConfig config, ArenaDef arena) => CreateInitialState(config, new[] { arena });

        public SimState CreateInitialState(SimConfig config, ArenaDef[] arenas)
        {
            uint configChecksum = config.ComputeChecksum();
            if (configChecksum != ConfigChecksum)
                throw new InvalidOperationException($"설정 체크섬이 기록과 다름: {ConfigChecksum:X8} → {configChecksum:X8}");
            uint arenaChecksum = ComputeArenaChecksum(arenas);
            if (arenaChecksum != ArenaChecksum)
                throw new InvalidOperationException($"맵 체크섬이 기록과 다름({Arena} → {arenas[0].Name}…): {ArenaChecksum:X8} → {arenaChecksum:X8}");
            var state = BuildState(config, arenas);
            uint initial = state.ComputeChecksum();
            if (initial != InitialChecksum)
                throw new InvalidOperationException($"시작 상태 체크섬이 기록과 다름: {InitialChecksum:X8} → {initial:X8}");
            return state;
        }

        /// <summary>새 SimWorld에서 처음부터 끝까지 재생하고 최종 체크섬을 돌려준다.</summary>
        public uint Replay(SimConfig config, ArenaDef arena) => Replay(config, new[] { arena });

        public uint Replay(SimConfig config, ArenaDef[] arenas)
        {
            var world = new SimWorld(config, arenas, CreateInitialState(config, arenas));
            for (int i = 0; i < p1.Count; i++)
                world.Step(p1[i], p2[i]);
            return world.ComputeChecksum();
        }

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(ConfigChecksum);
                w.Write(ArenaChecksum);
                w.Write(Seed);
                w.Write(BitConverter.SingleToInt32Bits(Spawn0.X));
                w.Write(BitConverter.SingleToInt32Bits(Spawn0.Y));
                w.Write(BitConverter.SingleToInt32Bits(Spawn1.X));
                w.Write(BitConverter.SingleToInt32Bits(Spawn1.Y));
                w.Write((byte)StartPhase);
                w.Write(Arena);
                w.Write(Mode);
                w.Write(InitialChecksum);
                w.Write(HasFinalChecksum);
                w.Write(FinalChecksum);
                w.Write(Rotation.Seed);
                w.Write(Rotation.Order);
                w.Write(Rotation.Count);
                w.Write(Rotation.Slot);
                w.Write(Rotation.Cycle);
                w.Write(p1.Count);
                var buffer = new byte[InputFrame.Size * 2];
                for (int i = 0; i < p1.Count; i++)
                {
                    p1[i].Write(buffer, 0);
                    p2[i].Write(buffer, InputFrame.Size);
                    w.Write(buffer);
                }
            }
            return stream.ToArray();
        }

        public static InputRecording FromBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic)
                throw new InvalidDataException("입력 기록 파일이 아님");
            ushort version = r.ReadUInt16();
            if (version != FormatVersion && version != WeaponsVersion && version != ChargeVersion && version != AimVersion && version != NoAimVersion)
                throw new InvalidDataException(version < NoAimVersion
                    ? $"CR-003(폼 전투) 전의 입력 기록(버전 {version})은 호환되지 않음"
                    : $"지원하지 않는 입력 기록 버전: {version}");

            var rec = new InputRecording
            {
                ConfigChecksum = r.ReadUInt32(),
                ArenaChecksum = r.ReadUInt32(),
                Seed = r.ReadUInt32(),
                Spawn0 = new Vec2(BitConverter.Int32BitsToSingle(r.ReadInt32()), BitConverter.Int32BitsToSingle(r.ReadInt32())),
                Spawn1 = new Vec2(BitConverter.Int32BitsToSingle(r.ReadInt32()), BitConverter.Int32BitsToSingle(r.ReadInt32())),
                StartPhase = (MatchPhase)r.ReadByte(),
                Arena = r.ReadString(),
                Mode = r.ReadString(),
                InitialChecksum = r.ReadUInt32(),
                HasFinalChecksum = r.ReadBoolean(),
                FinalChecksum = r.ReadUInt32(),
            };
            {
                rec.Rotation = new ArenaRotation
                {
                    Seed = r.ReadUInt32(),
                    Order = r.ReadUInt32(),
                    Count = r.ReadInt32(),
                    Slot = r.ReadInt32(),
                    Cycle = r.ReadInt32(),
                };
            }
            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"잘못된 프레임 수: {count}");
            bool aim = version >= AimVersion;
            int size = aim ? InputFrame.Size : InputFrame.LegacySize;
            for (int i = 0; i < count; i++)
            {
                byte[] buffer = r.ReadBytes(size * 2);
                if (buffer.Length != size * 2)
                    throw new InvalidDataException("입력 기록이 중간에 끊김");
                // 구기록에는 없던 비트를 새 차징 입력으로 조용히 재해석하지 않는다.
                if (version < ChargeVersion && ((buffer[2] | buffer[size + 2]) & (byte)InputButtons.Charge) != 0)
                    throw new InvalidDataException($"입력 기록 버전 {version}에 지원하지 않는 Charge 버튼이 있음");
                rec.Append(
                    aim ? InputFrame.Read(buffer, 0) : InputFrame.ReadLegacy(buffer, 0),
                    aim ? InputFrame.Read(buffer, size) : InputFrame.ReadLegacy(buffer, size));
            }
            return rec;
        }
    }
}
