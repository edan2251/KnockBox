using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>Opt-in real SDK/Photon test. Run with -onlinebot, -netsim 90,3 and the locally configured App ID.</summary>
    public sealed class FusionPredictionPlayTests
    {
        [UnityTest, Explicit("Uses the locally configured Photon app and two real NetworkRunners.")]
        public IEnumerator RealHostClient_ResimulatesAndExportsMatchingAuthoritativeReplay()
        {
            Assert.AreEqual(3, FusionPredictionPolicy.DelayTicks);
            Assert.IsTrue(AutoPlayDebug.HasArg("-onlinebot"), "Pass -onlinebot.");
            yield return PlayModeSceneScope.OpenEmpty();
            var a = new GameObject("PredictionTest_Host");
            var b = new GameObject("PredictionTest_Client");
            var sceneSentinel = new GameObject("PredictionTest_SceneSentinel");
            OnlineMatch host = null, client = null;
            FusionTransport hostWire = null, clientWire = null;
            try
            {
                var hr = a.AddComponent<MatchRunner>(); hr.BrawlExperiment = true; hr.FrontendEnabled = true;
                var cr = b.AddComponent<MatchRunner>(); cr.BrawlExperiment = true; cr.FrontendEnabled = true;
                host = a.AddComponent<OnlineMatch>(); client = b.AddComponent<OnlineMatch>();
                var hosting = FusionConnection.HostAsync();
                yield return Await(hosting, 45);
                var room = hosting.GetAwaiter().GetResult(); hostWire = room.transport;
                host.StartSession(hostWire, true, FusionPredictionPolicy.DelayTicks);
                var joining = FusionConnection.JoinAsync(room.code);
                yield return Await(joining, 45);
                clientWire = joining.GetAwaiter().GetResult();
                client.StartSession(clientWire, false, FusionPredictionPolicy.DelayTicks);
                var ticks = new HashSet<int>();
                cr.Ticked += r => Assert.IsTrue(ticks.Add(r.EventWorld.State.Tick), "Confirmed tick/effect emitted twice.");
                double deadline = Time.realtimeSinceStartupAsDouble + 60;
                bool startupObserved = false;
                while ((client.Prediction == null || client.Prediction.Journal.World.State.Tick < 900)
                    && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    if (!startupObserved && Time.realtimeSinceStartupAsDouble > deadline - 57)
                    {
                        startupObserved = true;
                        TestContext.WriteLine($"Control host tx/rx={hostWire.SentFragments}/{hostWire.ReceivedFragments}, " +
                            $"client tx/rx={clientWire.SentFragments}/{clientWire.ReceivedFragments}, " +
                            $"hostPackets={host.Session.PacketsSent}/{host.Session.PacketsReceived}, " +
                            $"clientPackets={client.Session.PacketsSent}/{client.Session.PacketsReceived}");
                        foreach (var network in UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
                            TestContext.WriteLine($"SDK server={network.IsServer} tick={network.Tick} active={network.isActiveAndEnabled} local={network.LocalPlayer} sceneBusy={network.SceneManager.IsBusy}");
                        foreach (var world in UnityEngine.Object.FindObjectsByType<FusionPredictionWorld>(FindObjectsSortMode.None))
                            TestContext.WriteLine($"World server={world.Runner.IsServer} inputAuthority={world.HasInputAuthority} " +
                                $"stateAuthority={world.HasStateAuthority} simulated={world.Object.IsInSimulation} skipResim={world.IsResimulationSkipped}");
                    }
                    Assert.IsFalse(host.Ended, host.Session?.EndDetail);
                    Assert.IsFalse(client.Ended, client.Session?.EndDetail);
                    yield return null;
                }
                Assert.IsNotNull(host.Prediction); Assert.IsNotNull(client.Prediction);
                var hp = host.Prediction; var cp = client.Prediction;
                Assert.GreaterOrEqual(cp.Journal.World.State.Tick, 900, "Native simulation did not advance.");
                Assert.Greater(cp.ResimulationTicks, 0, "Must observe the SDK resimulation stage, not a custom rewind.");
                Assert.Greater(cp.CorrectedTicks, 0, "Changed remote input should correct a prediction.");
                Assert.Greater(cp.Journal.BatchesVerified, 100);
                Assert.AreEqual(cp.Journal.Recording.Count, ticks.Count);
                Assert.Greater(cp.LocalInputSamples, 100);
                Assert.Greater(hp.LocalInputSamples, 100);
                Assert.AreEqual(3, host.Session.DelayTicks);
                Assert.AreEqual(3, client.Session.DelayTicks);
                Assert.Greater(host.NetworkRttSeconds, 0, "HUD needs the SDK RTT, not the control heartbeat RTT.");
                Assert.Greater(client.NetworkRttSeconds, 0);
                for (int i = 0; i < cp.Journal.Recording.Count; i++)
                {
                    Assert.AreEqual(hp.Journal.Recording.GetP1(i), cp.Journal.Recording.GetP1(i), "P1 tick " + i);
                    Assert.AreEqual(hp.Journal.Recording.GetP2(i), cp.Journal.Recording.GetP2(i), "P2 tick " + i);
                }
                var record = InputRecording.FromBytes(cp.Journal.Recording.ToBytes());
                var maps = new ArenaDef[cp.Journal.World.ArenaCount];
                for (int i = 0; i < maps.Length; i++) maps[i] = cp.Journal.World.GetArena(i);
                Assert.AreEqual(cp.Journal.World.ComputeChecksum(), record.Replay(cp.Journal.World.Config, maps));
                TestContext.WriteLine($"Native Fusion delay={FusionPredictionPolicy.DelayTicks}, confirmed={record.Count}, " +
                    $"forward={cp.ForwardTicks}, resim={cp.ResimulationTicks}, corrected={cp.CorrectedTicks}, " +
                    $"hostLocalAgeMs={hp.LocalInputAgeTotal * 1000 / hp.LocalInputSamples:F2}, " +
                    $"clientLocalAgeMs={cp.LocalInputAgeTotal * 1000 / cp.LocalInputSamples:F2}, " +
                    $"hostMissingRemote={hp.MissingRemoteInputs}, batches={cp.Journal.BatchesVerified}");
            }
            finally
            {
                UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                hostWire?.Dispose(); clientWire?.Dispose();
            }
            double cleanup = Time.realtimeSinceStartupAsDouble + 10;
            while (UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Length > 0
                && Time.realtimeSinceStartupAsDouble < cleanup) yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Length,
                "Connection shutdown must release both SDK runners.");
            Assert.IsTrue(sceneSentinel != null && sceneSentinel.scene.isLoaded, "Disconnect must preserve the existing UI scene.");
            UnityEngine.Object.Destroy(sceneSentinel);
        }

        static IEnumerator Await(Task task, int seconds)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Assert.IsTrue(task.IsCompleted, "Photon operation timed out.");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }
    }
}
