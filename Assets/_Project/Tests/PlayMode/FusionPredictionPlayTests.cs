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
            Assert.AreEqual(0, FusionPredictionPolicy.DelayTicks);
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
                Assert.AreEqual(FusionPredictionPolicy.DelayTicks, host.Session.DelayTicks);
                Assert.AreEqual(FusionPredictionPolicy.DelayTicks, client.Session.DelayTicks);
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

        /// <summary>
        /// 2026-11-17 P2 끊김 회귀: 빠른 연결(-netsim 없이)에서 입력 지연이 왕복보다 길면 Client 예측 시각이 받은 Host 상태 뒤에 묶여
        /// 상태가 올 때만 2~3틱씩 진행했다(표시 시간 정지 약 60%). 표시 시간(틱+보간 비율)이 프레임마다 이어지고
        /// 예측이 받은 Host 상태보다 앞서는지 본다. 경기 안 히트스톱은 틱이 계속 가므로 이 지표와 섞이지 않는다.
        /// </summary>
        [UnityTest, Explicit("Uses the locally configured Photon app and two real NetworkRunners.")]
        public IEnumerator RealHostClient_FastLink_ClientDisplayTimeAdvancesEveryFrame()
        {
            if (ReplayCheck.StringArg("-netsim") != null) Assert.Ignore("빠른 연결 조건 검사: -netsim 없이 실행");
            yield return PlayModeSceneScope.OpenEmpty();
            var a = new GameObject("DisplayTest_Host");
            var b = new GameObject("DisplayTest_Client");
            FusionTransport hostWire = null, clientWire = null;
            try
            {
                var hr = a.AddComponent<MatchRunner>(); hr.BrawlExperiment = true; hr.FrontendEnabled = true;
                var cr = b.AddComponent<MatchRunner>(); cr.BrawlExperiment = true; cr.FrontendEnabled = true;
                var host = a.AddComponent<OnlineMatch>(); var client = b.AddComponent<OnlineMatch>();
                var hosting = FusionConnection.HostAsync();
                yield return Await(hosting, 45);
                var room = hosting.GetAwaiter().GetResult(); hostWire = room.transport;
                host.StartSession(hostWire, true, FusionPredictionPolicy.DelayTicks);
                var joining = FusionConnection.JoinAsync(room.code);
                yield return Await(joining, 45);
                clientWire = joining.GetAwaiter().GetResult();
                client.StartSession(clientWire, false, FusionPredictionPolicy.DelayTicks);
                double deadline = Time.realtimeSinceStartupAsDouble + 60;
                while ((client.Prediction == null || client.Prediction.Journal.World.State.Tick < 120) && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.IsNotNull(client.Prediction, "Prediction did not start.");
                var cp = client.Prediction;
                var times = new List<double>(); var frames = new List<double>();
                double ahead = 0;
                double end = Time.realtimeSinceStartupAsDouble + 5;
                while (Time.realtimeSinceStartupAsDouble < end)
                {
                    yield return new WaitForEndOfFrame();
                    Assert.IsFalse(client.Ended, client.Session?.EndDetail);
                    times.Add(cr.PreviousState.Tick + cr.RenderAlpha * (cr.World.State.Tick - cr.PreviousState.Tick));
                    frames.Add(Time.unscaledDeltaTime * 1000);
                    ahead += cp.PredictedTick - cp.LatestServerTick;
                }
                var summary = FrameTraceSummary.FromRenderTimes(times, frames);
                double meanAhead = ahead / times.Count;
                TestContext.WriteLine($"delay={FusionPredictionPolicy.DelayTicks} rtt={client.NetworkRttSeconds * 1000:0}ms client {summary}, predicted-received={meanAhead:0.00} ticks");
                Assert.Greater(summary.Frames, 100);
                Assert.Less(summary.StallRatio, 0.05, "Client display time must not wait for each host snapshot.");
                Assert.AreEqual(0, summary.Backwards);
                Assert.Greater(meanAhead, 0.5, "Client must predict ahead of the latest received host state.");
            }
            finally
            {
                UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                hostWire?.Dispose(); clientWire?.Dispose();
            }
            double cleanup = Time.realtimeSinceStartupAsDouble + 10;
            while (UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Length > 0
                && Time.realtimeSinceStartupAsDouble < cleanup) yield return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 로비 입장 A안(2026-11-17): 방장의 혼자 로비 월드를 다시 만들지 않고(MatchBegan 없음 = 맵 재구성·암전 없음)
        /// 착지해 서 있던 P1 위치를 공유 로비 시작으로 이어 가며 P2만 떨어진다. 두 확정 기록이 같고 v10으로 재생된다.
        /// </summary>
        [UnityTest, Explicit("Uses the locally configured Photon app and two real NetworkRunners.")]
        public IEnumerator RealHostClient_LobbyJoin_ContinuesHostSoloLobbyWithoutRebuild()
        {
            yield return PlayModeSceneScope.OpenEmpty();
            var lobbyMap = UnityEditor.AssetDatabase.LoadAssetAtPath<Knockbox.Game.Maps.MapDefinition>("Assets/_Project/Data/Maps/Lobby/Lobby_Stage.asset");
            Assert.IsNotNull(lobbyMap);
            var field = typeof(MatchRunner).GetField("lobbyMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var a = new GameObject("LobbyJoin_Host");
            var b = new GameObject("LobbyJoin_Client");
            FusionTransport hostWire = null, clientWire = null;
            try
            {
                var hr = a.AddComponent<MatchRunner>(); hr.BrawlExperiment = true; hr.FrontendEnabled = true; field.SetValue(hr, lobbyMap);
                var cr = b.AddComponent<MatchRunner>(); cr.BrawlExperiment = true; cr.FrontendEnabled = true; field.SetValue(cr, lobbyMap);
                var host = a.AddComponent<OnlineMatch>(); var client = b.AddComponent<OnlineMatch>();
                var hosting = FusionConnection.HostAsync();
                yield return Await(hosting, 45);
                var room = hosting.GetAwaiter().GetResult(); hostWire = room.transport;
                host.StartSession(hostWire, true, FusionPredictionPolicy.DelayTicks);
                Assert.IsTrue(hr.InSoloLobby, "방장은 상대를 기다리며 혼자 로비에서 움직인다");
                var soloWorld = hr.World;
                int began = 0; hr.MatchBegan += _ => began++;
                double landed = Time.realtimeSinceStartupAsDouble + 3;
                while (Time.realtimeSinceStartupAsDouble < landed) yield return null;
                Assert.IsFalse(soloWorld.State.Fighter0.Arriving, "P1이 혼자 로비에 착지함");
                var standing = soloWorld.State.Fighter0.Position;
                var joining = FusionConnection.JoinAsync(room.code);
                yield return Await(joining, 45);
                clientWire = joining.GetAwaiter().GetResult();
                client.StartSession(clientWire, false, FusionPredictionPolicy.DelayTicks);
                double deadline = Time.realtimeSinceStartupAsDouble + 60;
                while ((client.Prediction == null || client.Prediction.Journal.World.State.Tick < 180) && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    Assert.IsFalse(host.Ended, host.Session?.EndDetail);
                    Assert.IsFalse(client.Ended, client.Session?.EndDetail);
                    yield return null;
                }
                var hp = host.Prediction; var cp = client.Prediction;
                Assert.IsNotNull(cp); Assert.GreaterOrEqual(cp.Journal.World.State.Tick, 180);
                Assert.AreEqual(0, began, "방장 월드를 다시 시작하지 않는다(맵 재구성·암전 없음)");
                Assert.AreSame(soloWorld, hr.World);
                Assert.IsTrue(hp.Journal.Carry.Has, "서 있던 P1을 이어 간다");
                Assert.Less(Mathf.Abs(hp.Journal.Carry.Position.X - standing.X) + Mathf.Abs(hp.Journal.Carry.Position.Y - standing.Y), 0.01f);
                Assert.AreEqual(hp.Journal.Carry, cp.Journal.Carry, "손님도 같은 시작 상태");
                Assert.AreEqual(-1, cr.AbsentSlot, "손님 화면의 P1 숨김은 첫 공유 상태 뒤 풀린다");
                int count = Mathf.Min(hp.Journal.Recording.Count, cp.Journal.Recording.Count);
                for (int i = 0; i < count; i++)
                {
                    Assert.AreEqual(hp.Journal.Recording.GetP1(i), cp.Journal.Recording.GetP1(i), "P1 tick " + i);
                    Assert.AreEqual(hp.Journal.Recording.GetP2(i), cp.Journal.Recording.GetP2(i), "P2 tick " + i);
                }
                var record = InputRecording.FromBytes(cp.Journal.Recording.ToBytes());
                Assert.AreEqual(InputRecording.LobbyCarryVersion, record.WrittenVersion);
                var maps = new ArenaDef[cp.Journal.World.ArenaCount];
                for (int i = 0; i < maps.Length; i++) maps[i] = cp.Journal.World.GetArena(i);
                Assert.AreEqual(cp.Journal.World.ComputeChecksum(), record.Replay(cp.Journal.World.Config, maps));
                TestContext.WriteLine($"carry pos=({hp.Journal.Carry.Position.X:0.00},{hp.Journal.Carry.Position.Y:0.00}) standing=({standing.X:0.00},{standing.Y:0.00}) confirmed={record.Count}");
            }
            finally
            {
                UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                hostWire?.Dispose(); clientWire?.Dispose();
            }
            double cleanup = Time.realtimeSinceStartupAsDouble + 10;
            while (UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Length > 0
                && Time.realtimeSinceStartupAsDouble < cleanup) yield return null;
        }
#endif

        [UnityTest, Explicit("Uses the locally configured Photon app and two real NetworkRunners.")]
        public IEnumerator RealHostClient_CardHoverTravelsBothWays_WithoutTouchingConfirmedRecording()
        {
            Assert.IsTrue(AutoPlayDebug.HasArg("-onlinebot"), "Pass -onlinebot.");
            yield return PlayModeSceneScope.OpenEmpty();
            var a = new GameObject("HoverTest_Host");
            var b = new GameObject("HoverTest_Client");
            FusionTransport hostWire = null, clientWire = null;
            try
            {
                var hr = a.AddComponent<MatchRunner>(); hr.BrawlExperiment = true; hr.FrontendEnabled = true;
                var cr = b.AddComponent<MatchRunner>(); cr.BrawlExperiment = true; cr.FrontendEnabled = true;
                var host = a.AddComponent<OnlineMatch>(); var client = b.AddComponent<OnlineMatch>();
                var hosting = FusionConnection.HostAsync();
                yield return Await(hosting, 45);
                var room = hosting.GetAwaiter().GetResult(); hostWire = room.transport;
                host.StartSession(hostWire, true, FusionPredictionPolicy.DelayTicks);
                var joining = FusionConnection.JoinAsync(room.code);
                yield return Await(joining, 45);
                clientWire = joining.GetAwaiter().GetResult();
                client.StartSession(clientWire, false, FusionPredictionPolicy.DelayTicks);
                double deadline = Time.realtimeSinceStartupAsDouble + 60;
                while ((client.Prediction == null || client.Prediction.Journal.World.State.Tick < 120) && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.IsNotNull(host.CardHover, "Fusion 경기에는 호버 전송이 있다");
                Assert.IsNotNull(client.CardHover);
                // 호스트(패자) → 클라이언트(승자), 이어서 반대 방향. 실제로는 고르는 쪽만 보낸다.
                for (int direction = 0; direction < 2; direction++)
                {
                    var from = direction == 0 ? host.CardHover : client.CardHover;
                    var to = direction == 0 ? client.CardHover : host.CardHover;
                    int round = 40 + direction, option = 2 - direction;
                    double until = Time.realtimeSinceStartupAsDouble + 1.5;
                    bool arrived = false;
                    while (Time.realtimeSinceStartupAsDouble < until && !arrived)
                    {
                        double now = Time.realtimeSinceStartupAsDouble;
                        from.Publish(now, round, option);
                        arrived = to.RemoteOption(now, round) == option;
                        yield return null;
                    }
                    from.StopPublishing();
                    Assert.IsTrue(arrived, "호버가 상대에게 도착해야 함(방향 " + direction + ")");
                    TestContext.WriteLine($"hover direction {direction}: sent={from.Sent}, received={to.Received}, dropped={to.Dropped}");
                }
                Assert.IsFalse(host.Ended, host.Session?.EndDetail);
                Assert.IsFalse(client.Ended, client.Session?.EndDetail);
                // 호버는 확정 기록에 들어가지 않는다: 양쪽 확정 입력이 같고 체크섬이 그대로 재생된다.
                var hp = host.Prediction; var cp = client.Prediction;
                int count = Mathf.Min(hp.Journal.Recording.Count, cp.Journal.Recording.Count);
                for (int i = 0; i < count; i++)
                {
                    Assert.AreEqual(hp.Journal.Recording.GetP1(i), cp.Journal.Recording.GetP1(i), "P1 tick " + i);
                    Assert.AreEqual(hp.Journal.Recording.GetP2(i), cp.Journal.Recording.GetP2(i), "P2 tick " + i);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                hostWire?.Dispose(); clientWire?.Dispose();
            }
            double cleanup = Time.realtimeSinceStartupAsDouble + 10;
            while (UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None).Length > 0
                && Time.realtimeSinceStartupAsDouble < cleanup) yield return null;
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
