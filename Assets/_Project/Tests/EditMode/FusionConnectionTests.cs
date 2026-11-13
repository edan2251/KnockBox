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
            Assert.AreEqual("same-prediction1-snapshot8692-tick60-delay3", version);
            Assert.AreEqual(version, FusionPredictionPolicy.ConnectionVersion(" same "));
            Assert.AreNotEqual("same", version);
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay2", version);
            Assert.AreNotEqual("same-prediction1-snapshot8692-tick60-delay4", version);
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
            Assert.AreEqual(3, OnlineMatch.DefaultOnlineDelay);
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
