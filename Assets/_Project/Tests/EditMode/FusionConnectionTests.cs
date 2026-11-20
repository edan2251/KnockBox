using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Knockbox.Game.Online;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>계정/실제 통신 없이 접속 진입의 부작용 방지를 검사한다.</summary>
    public class FusionConnectionTests
    {
        [Test]
        public void DefaultPredictionVersion_IsTrimmedAndIsolatesEarlierComparisonBuilds()
        {
            string version = FusionPredictionPolicy.ConnectionVersion("same");
            Assert.AreEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2-exit72-results3-name1", version);
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2-exit72-results3", version, "닉네임 RPC가 없는 빌드와 섞이지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2-exit72-results2", version, "숫자 낙하 시작 속도가 없던 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2-exit72-results1", version, "결과 점수가 계산 연출이던(숫자 발판 없음) 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2-exit72", version, "결과 발표(결과 공간 전용 잠금) 전 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry2", version, "준비 확정 뒤 바로 1라운드로 가던(로비 퇴장 전환 없음) 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144-carry1", version, "이어 간 P1이 재구성 대기에 묶이던 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay0-hover1-hold144", version, "확정 입력 묶음 v1(로비 이어 가기 없음) 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8700-tick60-delay3-hover1-hold144", version, "입력 지연 3틱 빌드와는 접속하지 않는다(SDK 시간 설정이 다름)");
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay3-hover1-hold144", version, "결과 공간 상태(Arriving)가 없는 이전 빌드와는 접속하지 않는다");
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay3", version, "카드 호버 RPC가 없는 이전 빌드와는 접속하지 않는다");
            Assert.AreEqual(version, FusionPredictionPolicy.ConnectionVersion(" same "));
            Assert.AreNotEqual("same", version);
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay2", version);
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay4", version);
        }

        /// <summary>
        /// 닉네임(2026-11-21): 입력·저장값·상대가 보낸 값 모두 같은 정리를 거친다. 서식 태그·줄바꿈·이모지는 빼고, 공백은 하나로, 최대 10글자.
        /// 비어 있으면 자리 이름(P1/P2)으로 보인다.
        /// </summary>
        [TestCase("  철수  ", "철수")]
        [TestCase("<b>굵게</b>", "b굵게b")]
        [TestCase("두   칸\n이름", "두 칸 이름")]
        [TestCase("가나다라마바사아자차카타", "가나다라마바사아자차")]
        [TestCase("Player_1!", "Player_1!")]
        [TestCase("😀웃음", "웃음")]
        [TestCase("   ", "")]
        [TestCase(null, "")]
        public void PlayerNickname_IsCleanedForDisplayAndNetwork(string raw, string expected)
        {
            Assert.AreEqual(expected, Knockbox.Game.GameSettings.CleanNickname(raw));
            Assert.LessOrEqual(Knockbox.Game.GameSettings.CleanNickname(raw).Length, Knockbox.Game.GameSettings.NicknameMaxLength);
        }

        [Test]
        public void EmptyNickname_ShowsSlotName()
        {
            Assert.AreEqual("P1", Knockbox.Game.GameSettings.DisplayName("", 0));
            Assert.AreEqual("P2", Knockbox.Game.GameSettings.DisplayName(null, 1));
            Assert.AreEqual("철수", Knockbox.Game.GameSettings.DisplayName("철수", 1));
        }

        [TestCase(null)] [TestCase("")] [TestCase(" ")]
        public void MissingConnectionVersion_IsRejected(string version)
            => Assert.Throws<FusionSetupException>(() => FusionPredictionPolicy.ConnectionVersion(version));

        [Test]
        public void ConnectionSettings_KeepRegionAndBaseVersionSeparateFromTiming()
        {
            var policy = Resources.Load<FusionConnectionSettings>(FusionConnectionSettings.ResourcePath);
            Assert.IsNotNull(policy);
            Assert.AreEqual(FusionConnectionSettings.DefaultRegion, policy.Region);
            Assert.AreEqual(FusionConnectionSettings.DefaultAppVersion, policy.AppVersion);
            Assert.AreEqual(FusionPredictionPolicy.DelayTicks, OnlineMatch.DefaultOnlineDelay);
            Assert.AreEqual(9, LockstepSession.ProtocolVersion);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LockstepRpc_ExecutesSdkGuardAndClearsDispatchFlag(bool incoming)
        {
            // Execute the woven method: merely finding its invoker misses invalid internal calls.
            bool previous = Fusion.NetworkBehaviourUtils.InvokeRpc;
            try
            {
                Fusion.NetworkBehaviourUtils.InvokeRpc = incoming;
                var error = Assert.Throws<ArgumentNullException>(() =>
                    FusionConnection.Rpc_LockstepFragment(null, Fusion.PlayerRef.None, new byte[] { 1 }));
                Assert.AreEqual("runner", error.ParamName);
                Assert.IsFalse(Fusion.NetworkBehaviourUtils.InvokeRpc);
            }
            finally { Fusion.NetworkBehaviourUtils.InvokeRpc = previous; }
        }

        [Test]
        public void LockstepRpc_HasSdkGeneratedNetworkInvoker()
        {
            // C# compilation alone succeeds even if Knockbox.Game is omitted from AssembliesToWeave.
            var methods = typeof(FusionConnection).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsTrue(methods.Any(method =>
                method.Name.StartsWith(nameof(FusionConnection.Rpc_LockstepFragment) + "@Invoker", StringComparison.Ordinal)
                && method.IsDefined(typeof(Fusion.NetworkRpcWeavedInvokerAttribute), false)),
                "Add Knockbox.Game to Fusion NetworkProjectConfig.AssembliesToWeave and recompile.");
        }

        [Test]
        public void AlreadyCancelledHost_DoesNotCreateRunner()
        {
            var before = GameObject.Find("FusionConnection_Runtime");
            var task = FusionConnection.HostAsync(new CancellationToken(true));
            Assert.IsTrue(task.IsCanceled);
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
            Assert.AreSame(before, GameObject.Find("FusionConnection_Runtime"));
        }

        [Test]
        public void AlreadyCancelledJoin_DoesNotReadSettingsOrCreateRunner()
        {
            var before = GameObject.Find("FusionConnection_Runtime");
            var task = FusionConnection.JoinAsync("ABCDEFGH", new CancellationToken(true));
            Assert.IsTrue(task.IsCanceled);
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
            Assert.AreSame(before, GameObject.Find("FusionConnection_Runtime"));
        }

        [Test]
        public void InvalidJoinCode_IsRejectedBeforeSettingsOrRunnerCreation()
        {
            var before = GameObject.Find("FusionConnection_Runtime");
            Assert.ThrowsAsync<FusionSetupException>(async () =>
                await FusionConnection.JoinAsync("BAD"));
            Assert.AreSame(before, GameObject.Find("FusionConnection_Runtime"));
        }
    }
}
