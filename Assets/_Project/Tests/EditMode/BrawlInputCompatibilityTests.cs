using System;
using System.IO;
using System.Text.RegularExpressions;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SessionState = Knockbox.Game.Online.SessionState;

namespace Knockbox.Tests.EditMode
{
    /// <summary>좌클릭 통합·무기 투척과 기존 v4/v5/v6/v7 기록·온라인 연결의 호환 경계를 검사한다.</summary>
    public class BrawlInputCompatibilityTests
    {
        GameObject root;
        MatchRunner runner;
        ArenaDef arena;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Brawl input compatibility");
            root.SetActive(false);
            runner = root.AddComponent<MatchRunner>();
            runner.Tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
            runner.CombatTuning = AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset");
            Assert.IsNotNull(runner.Tuning);
            Assert.IsNotNull(runner.CombatTuning);
            runner.BrawlExperiment = true;
            runner.Rules = RuleSet.Brawl;
            arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        SimConfig Config(bool separateCharge, bool online = false, bool weaponsEnabled = false,
            bool combinedAttackInput = false, bool throwEnabled = false, bool revisedBalance = false)
        {
            var match = BrawlParams.Match(runner.Tuning.Match);
            match.RematchBothHold = online;
            return new SimConfig(BrawlParams.Movement(runner.Tuning.Movement), match,
                BrawlParams.Combat(runner.CombatTuning.Combat, 0, separateCharge, weaponsEnabled, combinedAttackInput, throwEnabled,
                    revisedBalance), RuleSet.Brawl);
        }

        InputRecording Recording(SimConfig config) => InputRecording.Begin(config, new[] { arena }, default,
            7u, arena.SpawnFoot0, arena.SpawnFoot1, MatchPhase.Countdown, config.Rules.Format());

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void ConfigLookup_SelectsExactControlSchemeRematchAndWeapons(bool separateCharge, bool online, bool weaponsEnabled)
        {
            var config = Config(separateCharge, online, weaponsEnabled);
            Assert.AreNotEqual(config.ComputeChecksum(), Config(!separateCharge, online, weaponsEnabled).ComputeChecksum(),
                "조작 의미가 다르면 설정 체크섬도 달라야 한다");
            Assert.AreNotEqual(config.ComputeChecksum(), Config(separateCharge, online, !weaponsEnabled).ComputeChecksum(),
                "무기 규칙이 다르면 설정 체크섬도 달라야 한다");
            var found = runner.FindRecordedConfig(Recording(config));
            Assert.IsNotNull(found);
            Assert.AreEqual(separateCharge, found.Combat.Body.SeparateChargeInput);
            Assert.AreEqual(online, found.Match.RematchBothHold);
            Assert.AreEqual(weaponsEnabled, found.Combat.Weapons.Enabled);
            Assert.IsFalse(found.Combat.Body.CombinedAttackInput);
            Assert.IsFalse(found.Combat.Weapons.ThrowEnabled);
            Assert.AreEqual(config.ComputeChecksum(), found.ComputeChecksum());
            Assert.IsFalse(runner.CreateConfig().Combat.Body.SeparateChargeInput, "새 경기는 좌클릭 통합 규칙");
            Assert.IsTrue(runner.CreateConfig().Combat.Body.CombinedAttackInput);
            Assert.IsTrue(runner.CreateConfig().Combat.Weapons.Enabled, "새 경기는 무기 규칙");
            Assert.IsTrue(runner.CreateConfig().Combat.Weapons.ThrowEnabled, "새 경기는 무기 투척 규칙");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CurrentConfigLookup_DistinguishesCombinedInputThrowingAndOnline(bool online, bool throwing)
        {
            var config = Config(false, online, true, true, throwing);
            var found = runner.FindRecordedConfig(Recording(config));
            Assert.IsNotNull(found);
            Assert.IsTrue(found.Combat.Body.CombinedAttackInput);
            Assert.IsFalse(found.Combat.Body.SeparateChargeInput);
            Assert.AreEqual(throwing, found.Combat.Weapons.ThrowEnabled);
            Assert.AreEqual(online, found.Match.RematchBothHold);
            Assert.AreEqual(config.ComputeChecksum(), found.ComputeChecksum());
            Assert.AreNotEqual(config.ComputeChecksum(), Config(false, online, true, false, throwing).ComputeChecksum());
            Assert.AreNotEqual(config.ComputeChecksum(), Config(true, online, true, false, throwing).ComputeChecksum(),
                "Hasher는 이름을 무시하므로 구 Separate=true와 새 Combined=true가 같은 bool 한 바이트가 되면 안 된다");
            Assert.AreNotEqual(config.ComputeChecksum(), Config(false, online, true, true, !throwing).ComputeChecksum());
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void BalanceRevision_SelectsExactV8ConfigWithoutChangingFormat(bool online, bool revisedBalance)
        {
            var config = Config(false, online, true, true, true, revisedBalance);
            var found = runner.FindRecordedConfig(Recording(config));
            Assert.IsNotNull(found);
            Assert.AreEqual(config.ComputeChecksum(), found.ComputeChecksum());
            Assert.AreEqual(revisedBalance ? 12 : 0, found.Combat.Body.MinLungeChargeTicks);
            Assert.AreEqual(revisedBalance ? 22f : 16f, found.Combat.Weapons.ThrowSpeed);
            Assert.AreNotEqual(config.ComputeChecksum(), Config(false, online, true, true, true, !revisedBalance).ComputeChecksum());
            Assert.AreEqual(12, runner.CreateConfig().Combat.Body.MinLungeChargeTicks);
            Assert.AreEqual(22f, runner.CreateConfig().Combat.Weapons.ThrowSpeed);
            Assert.AreEqual(8, InputRecording.FormatVersion);
            Assert.AreEqual(9, LockstepSession.ProtocolVersion, "맵 목록/지형 검증 규격이 다른 이전 실행 파일과 접속을 분리한다");
        }

        [Test]
        public void CapturedV8BeforeBalance_ReplaysAllTicksWithItsOriginalRules()
        {
            const string prefix = "Tools/ReplayCheck/reference/brawl-v8-before-balance";
            var bytes = File.ReadAllBytes(prefix + ".kbrp");
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert.AreEqual("4C07FF89FA9890646CA2A503AEEA935E75F71765BB1CEDB8031664657102A7DF",
                    BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""));
            var rec = InputRecording.FromBytes(bytes);
            var config = runner.FindRecordedConfig(rec);
            Assert.IsNotNull(config);
            Assert.AreEqual(0xC7A71637u, config.ComputeChecksum());
            Assert.AreEqual(0, config.Combat.Body.MinLungeChargeTicks);
            Assert.AreEqual(16f, config.Combat.Weapons.ThrowSpeed);
            var lines = File.ReadAllLines(prefix + "-checksums.txt");
            var expected = new uint[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                expected[i] = uint.Parse(lines[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            var actual = ReplayTrace.Run(rec, config, new[] { arena }, null, null);
            Assert.AreEqual(701, actual.Length);
            Assert.AreEqual(-1, ReplayTrace.FirstMismatch(actual, expected));
            Assert.AreEqual(0xBD05880Eu, actual[actual.Length - 1]);
        }

        [TestCase(FighterForm.Body, FighterForm.Body, true)]
        [TestCase(FighterForm.Body, FighterForm.Scissors, false)]
        [TestCase(FighterForm.Scissors, FighterForm.Body, false)]
        public void CurrentConfig_EnablesWeaponsOnlyWhenBothPlayersUseBody(FighterForm p0, FighterForm p1, bool expected)
        {
            runner.Rules = RuleSet.Brawl.WithForms(p0, p1);
            Assert.AreEqual(expected, runner.CreateConfig().Combat.Weapons.Enabled);
            Assert.AreEqual(expected, runner.CreateConfig().Combat.Weapons.ThrowEnabled);
            Assert.AreEqual(expected, runner.CreateConfig().Combat.Body.CombinedAttackInput);
            runner.Rules = RuleSet.Legacy(CombatMode.Charge, true);
            Assert.IsFalse(runner.CreateConfig().Combat.Weapons.Enabled, "F1의 기존 폼에서는 무기를 생성하지 않는다");
        }

        [Test]
        public void CP4MainEditModeConfig_PreservesCapturedChecksumWithWeaponsDisabled()
        {
            // CP5 컴파일 전 수집한 Main 값은 StartNewMatch 전의 에디터 상태였다.
            // Brawl 튜닝과 별도 차징은 켜졌지만 Rules는 아직 기본 Scissors였다(실행 중 Body 규칙과 구분).
            runner.Rules = RuleSet.Default;
            var config = runner.CreateConfig();
            Assert.IsTrue(config.Combat.Body.SeparateChargeInput);
            Assert.IsFalse(config.Combat.Body.CombinedAttackInput);
            Assert.IsFalse(config.Combat.Weapons.Enabled);
            Assert.AreEqual(FighterForm.Scissors, config.Rules.StartForm);
            Assert.AreEqual(0xAF5F150Du, config.ComputeChecksum(), "실제 수집한 CP4 에디터 설정 체크섬을 보존한다");
        }

        [Test]
        public void ExistingV5Fixture_ReplaysEveryTickAgainstUnchangedReference()
        {
            var bytes = File.ReadAllBytes("Tools/ReplayCheck/brawl_sample.kbrp");
            Assert.AreEqual(5, BitConverter.ToUInt16(bytes, 4), "기존 v5 파일을 새 파일로 덮어쓰지 않는다");
            var recording = InputRecording.FromBytes(bytes);
            var config = runner.FindRecordedConfig(recording);
            Assert.IsNotNull(config, "실제 구기록의 튜닝과 좌클릭 탭/홀드 규칙을 찾아야 한다");
            Assert.IsFalse(config.Combat.Body.SeparateChargeInput);
            Assert.IsFalse(config.Combat.Body.CombinedAttackInput);
            Assert.IsFalse(config.Combat.Weapons.Enabled);
            Assert.AreEqual(0x1E671E55u, config.ComputeChecksum());
            var arenas = new ArenaDef[recording.ArenaNames.Length];
            for (int i = 0; i < arenas.Length; i++)
                arenas[i] = runner.ResolveArena(recording.ArenaNames[i]);
            // ReplayCheck가 쓰는 같은 재생·비교 함수를 사용하되 산출물은 쓰지 않는다.
            var expected = ReplayTrace.ParseChecksums(File.ReadAllText("Tools/ReplayCheck/reference/trace.txt"));
            var actual = ReplayTrace.Run(recording, config, arenas, null, null);
            Assert.AreEqual(recording.Count + 1, actual.Length);
            Assert.AreEqual(-1, ReplayTrace.FirstMismatch(actual, expected), "구기록의 시작 상태와 모든 틱이 기준과 같아야 한다");
            Assert.AreEqual(recording.FinalChecksum, actual[actual.Length - 1]);
        }

        [Test]
        public void V7WeaponsChargeAndAim_RoundTripInFourByteFrames()
        {
            var recording = Recording(Config(true, weaponsEnabled: true));
            var first = new InputFrame(1, -1, InputButtons.Charge | InputButtons.Jump, 63);
            var second = new InputFrame(-1, 1, InputButtons.Attack | InputButtons.Grab, 17);
            recording.Append(first, second);
            var bytes = recording.ToBytes();
            Assert.AreEqual(8, BitConverter.ToUInt16(bytes, 4));
            bytes[4] = 7; // v7의 동일한 헤더·4바이트 입력을 계속 읽는다. 조작·투척은 config hash로 구분한다.
            bytes[5] = 0;
            Assert.AreEqual(4, InputFrame.Size);
            var restored = InputRecording.FromBytes(bytes);
            Assert.AreEqual(first, restored.GetP1(0));
            Assert.AreEqual(second, restored.GetP2(0));
            Assert.IsTrue(runner.FindRecordedConfig(restored).Combat.Body.SeparateChargeInput);
            Assert.IsTrue(runner.FindRecordedConfig(restored).Combat.Weapons.Enabled);
            Assert.IsFalse(runner.FindRecordedConfig(restored).Combat.Weapons.ThrowEnabled);
        }

        [Test]
        public void V8CombinedAttackAndThrowing_RoundTripWithoutChangingFrameSize()
        {
            var config = Config(false, weaponsEnabled: true, combinedAttackInput: true, throwEnabled: true);
            var recording = Recording(config);
            var first = new InputFrame(1, -1, InputButtons.Attack | InputButtons.Jump, 63);
            var second = new InputFrame(-1, 1, InputButtons.Grab, 17);
            recording.Append(first, second);
            var bytes = recording.ToBytes();
            Assert.AreEqual(8, BitConverter.ToUInt16(bytes, 4));
            Assert.AreEqual(4, InputFrame.Size);
            var restored = InputRecording.FromBytes(bytes);
            Assert.AreEqual(first, restored.GetP1(0));
            Assert.AreEqual(second, restored.GetP2(0));
            var found = runner.FindRecordedConfig(restored);
            Assert.IsNotNull(found);
            Assert.IsTrue(found.Combat.Body.CombinedAttackInput);
            Assert.IsTrue(found.Combat.Weapons.ThrowEnabled);
        }

        [TestCase(6, 0x2FA7B5FDu, 0xF610901Bu, 0xDF28D399u, "CD2137BDFFD7FDEB67802141DC3FB98685A70EE3DEC2E87D2EA4E4F8D3CAD3EB")]
        [TestCase(7, 0xAF35129Eu, 0x595CAA6Au, 0xE2966174u, "EE6E807A3524CA37E2D5B1D11D39E3D6FCC848CEDEE9280354F92A8F1C721E05")]
        public void CapturedBeforeFeedbackFixtures_PreserveEveryV6AndV7Tick(int version, uint configHash, uint initialHash, uint finalHash, string fileHash)
        {
            // CP5 피드백 구현 전에 실행 중이던 어셈블리에서 수집했다. 현재 코드로 기대값을 다시 생성하지 않는다.
            string prefix = $"Tools/ReplayCheck/reference/brawl-v{version}-before-feedback";
            var bytes = File.ReadAllBytes(prefix + ".kbrp");
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert.AreEqual(fileHash, BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""));
            Assert.AreEqual(version, BitConverter.ToUInt16(bytes, 4));
            var recording = InputRecording.FromBytes(bytes);
            var combat = BrawlParams.Combat(CombatParams.Default, weaponsEnabled: version == 7);
            if (version == 7)
            {
                combat.Weapons.SpawnDelayTicks = 3;
                combat.Weapons.WarningTicks = 2;
                combat.Weapons.FallTicks = 3;
                combat.Weapons.DropTicks = 2;
            }
            var match = BrawlParams.Match(MatchParams.Default);
            match.CountdownTicks = 3;
            var config = new SimConfig(MovementParams.Default, match, combat, RuleSet.Brawl);
            var fixtureArena = new ArenaDef("Weapon test", new[]
            {
                new ArenaBox(TerrainKind.Solid, -16f, -1f, 16f, 0f),
                new ArenaBox(TerrainKind.Solid, -16f, 0f, -15f, 9f),
                new ArenaBox(TerrainKind.Solid, 15f, 0f, 16f, 9f),
            }, new Vec2(-6f, 0f), new Vec2(6f, 0f));
            Assert.AreEqual(configHash, config.ComputeChecksum());
            Assert.AreEqual(initialHash, recording.InitialChecksum);
            Assert.AreEqual(finalHash, recording.FinalChecksum);
            Assert.AreEqual(130, recording.Count);
            var lines = File.ReadAllLines(prefix + "-checksums.txt");
            var expected = new uint[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                expected[i] = uint.Parse(lines[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            var actual = ReplayTrace.Run(recording, config, new[] { fixtureArena }, null, null);
            Assert.AreEqual(131, actual.Length);
            Assert.AreEqual(-1, ReplayTrace.FirstMismatch(actual, expected));
            Assert.AreEqual(finalHash, actual[actual.Length - 1]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V6ChargeRecording_SelectsWeaponsDisabledAndReplays(bool online)
        {
            var config = Config(true, online, false);
            var recording = Recording(config);
            var world = new SimWorld(config, arena, recording.BuildState(config, new[] { arena }));
            var expected = new uint[121];
            expected[0] = world.ComputeChecksum();
            for (int i = 0; i < 120; i++)
            {
                var input = new InputFrame(1, 0, i % 60 < 24 ? InputButtons.Charge : InputButtons.None, 32);
                recording.Append(input, default);
                world.Step(input, default);
                expected[i + 1] = world.ComputeChecksum();
            }
            recording.Finish(world.ComputeChecksum());
            var bytes = recording.ToBytes();
            bytes[4] = 6; // CP4와 동일한 4바이트 프레임·헤더. 새 읽기가 Charge를 구버전 오류로 취급하면 실패한다.
            bytes[5] = 0;
            var restored = InputRecording.FromBytes(bytes);
            Assert.IsTrue(restored.GetP1(0).Held(InputButtons.Charge));
            var found = runner.FindRecordedConfig(restored);
            Assert.IsNotNull(found);
            Assert.IsTrue(found.Combat.Body.SeparateChargeInput);
            Assert.IsFalse(found.Combat.Weapons.Enabled);
            Assert.AreEqual(config.ComputeChecksum(), found.ComputeChecksum());
            var actual = ReplayTrace.Run(restored, found, new[] { arena }, null, null);
            Assert.AreEqual(-1, ReplayTrace.FirstMismatch(actual, expected));
        }

        [TestCase(4)]
        [TestCase(5)]
        public void LegacyFormats_KeepTheirFrameLayout_AndRejectNewChargeBit(int version)
        {
            var recording = Recording(Config(false));
            var first = new InputFrame(1, -1, InputButtons.Attack, 63);
            var second = new InputFrame(-1, 1, InputButtons.Dash | InputButtons.Jump, 17);
            recording.Append(first, second);
            var bytes = recording.ToBytes();
            int frameStart = bytes.Length - 2 * InputFrame.Size;
            if (version == 4)
            {
                // v4는 같은 헤더 뒤에 두 선수의 3바이트 프레임을 두었다.
                var noAim = new byte[bytes.Length - 2];
                Array.Copy(bytes, noAim, frameStart);
                Array.Copy(bytes, frameStart, noAim, frameStart, 3);
                Array.Copy(bytes, frameStart + 4, noAim, frameStart + 3, 3);
                bytes = noAim;
            }
            bytes[4] = (byte)version;
            bytes[5] = 0;
            var restored = InputRecording.FromBytes(bytes);
            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual(new InputFrame(first.MoveX, first.MoveY, first.Buttons, version == 5 ? first.Aim : (byte)0), restored.GetP1(0));
            Assert.AreEqual(new InputFrame(second.MoveX, second.MoveY, second.Buttons, version == 5 ? second.Aim : (byte)0), restored.GetP2(0));
            bytes[frameStart + 2] |= (byte)InputButtons.Charge;
            Assert.Throws<InvalidDataException>(() => InputRecording.FromBytes(bytes), "구버전의 미정의 비트를 새 차징으로 해석하지 않는다");
        }

        [Test]
        public void UnknownConfig_IsRejectedWithoutReplacingCurrentMatch()
        {
            var config = Config(true);
            var recording = Recording(config);
            runner.Begin(config, new[] { arena }, recording.BuildState(config, new[] { arena }));
            var before = runner.World;
            recording.ConfigChecksum ^= 1u;
            Assert.IsNull(runner.FindRecordedConfig(recording));
            LogAssert.Expect(LogType.Warning, new Regex("기록 재생 거절: 같은 튜닝·조작 규칙의 설정을 찾지 못함"));
            Assert.IsFalse(runner.PlayRecording(recording));
            Assert.AreSame(before, runner.World);
            Assert.IsNull(runner.Playback);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void OldProtocolPeer_IsRejectedBeforeAnyInputIsRead(int protocol)
        {
            Assert.AreEqual(9, LockstepSession.ProtocolVersion);
            MemoryTransport.Pair(out var local, out var oldPeer);
            using var session = new LockstepSession(local, true, 1u, 2u, 7u, 3);
            var header = new byte[6];
            Array.Copy(BitConverter.GetBytes(LockstepSession.Magic), header, 4);
            header[4] = (byte)protocol; // 기존 배포 빌드·CP4·CP5 첫 버전 프로토콜.
            header[5] = 1; // Hello. 버전 확인은 본문보다 먼저 해야 한다.
            oldPeer.Send(header, header.Length);
            session.Poll(0.1);
            Assert.AreEqual(SessionState.Ended, session.State);
            Assert.AreEqual(EndReason.Rejected, session.Reason);
            StringAssert.Contains("버전이 다름", session.EndDetail);
            oldPeer.Dispose();
        }
    }
}
