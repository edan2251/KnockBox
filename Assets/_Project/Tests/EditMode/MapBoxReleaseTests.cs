using System;
using System.Collections.Generic;
using System.Linq;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using SessionState = Knockbox.Game.Online.SessionState;

namespace Knockbox.Tests.EditMode
{
    public class MapBoxReleaseTests
    {
        public static SimConfig Config(bool smooth)
        {
            var old = MapPhysicalBoxReactionTests.CurrentConfig();
            var combat = old.Combat;
            combat.Props.SmoothBoxRelease = smooth;
            combat.Props.ReleasedBoxFrictionScale = .15f;
            return new SimConfig(old.Movement, old.Match, combat, old.Rules);
        }

        public static ArenaDef Arena(int count, float size)
        {
            var defs = new PropDef[count];
            for (int i = 0; i < count; i++)
                defs[i] = new PropDef { Kind = PropKind.LooseBox, Center = new Vec2(0, -8 + size * (.5f + i)),
                    HalfSize = new Vec2(size * .5f, size * .5f), Mass = size * size, Id = "response_" + i,
                    StartAwake = true, AnchorProp = -1 };
            return MapPhysicalBoxReactionTests.Lab(1).WithProps(defs);
        }

        public static SimWorld Settled(int count, float size, bool smooth)
        {
            var c = Config(smooth); var a = Arena(count, size);
            var w = new SimWorld(c, a, SimState.CreateRound(c, a, 77, a.SpawnFoot0, a.SpawnFoot1, MatchPhase.Fighting));
            for (int t = 0; t < 120; t++) w.Step(default, default);
            return w;
        }

        static void PlaceFighter(SimWorld w, float size, float speed, float height = 0)
        {
            w.State.Fighter0.Position = new Vec2(-size * .5f - w.Config.HalfBody - .055f, -8 + size * .5f + height);
            w.State.Fighter0.Velocity = new Vec2(speed, 0);
            w.State.Fighter0.SetAction(ActionState.Lunge);
            w.State.Fighter0.Grounded = false;
        }

        public static MapPhysicalBoxReactionTests.Observation Observe(int count, float size, float speed, bool smooth)
        {
            var w = Settled(count, size, smooth); var initial = w.State;
            if (speed > 0) PlaceFighter(w, size, speed);
            var r = new MapPhysicalBoxReactionTests.Observation { dx = new float[count], tilt = new float[count], early = new float[count] };
            var hash = Hasher.Create();
            for (int t = 0; t < 360; t++)
            {
                w.Step(default, default); hash.AddUInt(w.ComputeChecksum());
                for (int i = 0; i < count; i++)
                {
                    ref readonly var b = ref PropState.At(w.State.Props, i);
                    r.dx[i] = Math.Max(r.dx[i], Math.Abs(b.Position.X));
                    r.tilt[i] = Math.Max(r.tilt[i], Math.Abs(b.Rotation.Y));
                    r.loose = Math.Max(r.loose, b.LooseTicks);
                    Assert.IsFalse(float.IsNaN(b.Position.X + b.Position.Y + b.AngularVelocity));
                    Assert.IsFalse(float.IsInfinity(b.Position.X + b.Position.Y + b.AngularVelocity));
                }
                for (int i = 0; i < w.Events.Count; i++) if (w.Events[i].Type == SimEventType.BoxKnockout) r.knockouts++;
                if (t == 29) Array.Copy(r.dx, r.early, count);
            }
            r.drop = PropState.At(initial.Props, count - 1).Position.Y - PropState.At(w.State.Props, count - 1).Position.Y;
            r.hash = hash.Hash;
            return r;
        }

        [TestCase(1, 1.2f)] [TestCase(4, 1.2f)] [TestCase(1, 1.5f)] [TestCase(3, 1.5f)]
        public void RestWeakAndStrongReactions_RetainStableStackAndIndependentCollapse(int count, float size)
        {
            var rest = Observe(count, size, 0, true);
            var weak = Observe(count, size, 18, true);
            var strong = Observe(count, size, 34, true);
            var oldWeak = Observe(count, size, 18, false);
            var oldStrong = Observe(count, size, 34, false);
            TestContext.WriteLine($"boxes={count}, size={size}; rest: {rest.Description}\nweak: {weak.Description}\nstrong: {strong.Description}\nold strong: {oldStrong.Description}");
            Assert.Less(rest.dx.Max(), .02f); Assert.Less(rest.tilt.Max(), .01f);
            Assert.Zero(weak.loose); Assert.Zero(weak.knockouts);
            Assert.AreEqual(oldWeak.hash, weak.hash, "약한 충격의 기존 질량·마찰 반응을 보존한다.");
            Assert.Greater(strong.dx[0], weak.dx[0]); Assert.Greater(strong.loose, 0);
            Assert.Greater(strong.knockouts, 0);
            if (count > 1)
            {
                Assert.Greater(weak.early[1], .005f, "맞닿은 층으로 약한 충격이 전달된다.");
                Assert.Less(weak.drop, .12f);
                Assert.Greater(strong.drop, size * .65f, "받침이 빠지면 위 상자가 떨어진다.");
                Assert.Greater(strong.tilt.Skip(1).Max(), .05f);
            }
        }

        static PropBody DirectHit(float power, bool smooth)
        {
            var w = Settled(1, 1.5f, smooth);
            w.State.Fighter0.Position = new Vec2(-.75f - w.Config.HalfBody + .01f, -7.25f);
            w.State.Fighter0.SetAction(ActionState.Lunge);
            w.State.Fighter0.Velocity = new Vec2(power / FighterCombat.Aggression(w.State.Fighter0, w.Config), 0);
            var prev0 = w.State.Fighter0; var prev1 = w.State.Fighter1;
            PropInteraction.Resolve(ref w.State, w.Arena, w.Config, prev0, prev1, false, false, new SimEventBuffer(), new PropScratch());
            return PropState.At(w.State.Props, 0);
        }

        [TestCase(false)] [TestCase(true)]
        public void SlowSidePushReachesSleepingBox_StandingContactDoesNotWakeIt(bool smooth)
        {
            for (int face = 0; face < 3; face++)
            {
                bool top = face != 0;
                var w = Settled(1, 1.5f, smooth);
                ref var body = ref PropState.Get(ref w.State.Props, 0);
                float diagonal = SimMath.Sqrt(.5f);
                if (face == 2) body.Rotation = new Vec2(diagonal, diagonal);
                var outward = face == 2 ? new Vec2(-diagonal, diagonal) : top ? new Vec2(0, 1) : new Vec2(-1, 0);
                float fighterExtent = w.Config.HalfBody * (Math.Abs(outward.X) + Math.Abs(outward.Y));
                w.State.Fighter0.Position = body.Position + outward * (.75f + fighterExtent - .01f);
                w.State.Fighter0.Velocity = outward * -.5f;
                w.State.Fighter0.SetAction(ActionState.Idle);
                var p0 = w.State.Fighter0; var p1 = w.State.Fighter1;
                PropInteraction.Resolve(ref w.State, w.Arena, w.Config, p0, p1, false, false, new SimEventBuffer(), new PropScratch());
                ref readonly var result = ref PropState.At(w.State.Props, 0);
                TestContext.WriteLine($"smooth={smooth}, face={face}, vx={result.Velocity.X:F5}, sleep={result.SleepTicks}");
                if (smooth && !top) { Assert.Zero(result.SleepTicks); Assert.Greater(result.Velocity.X, .05f); }
                else Assert.IsTrue(PropPhysics.Asleep(result, w.Config.Combat.Props));
            }
        }

        [Test]
        public void StandingOnCurrentStack_DoesNotCauseJitterOrKeepItAwake()
        {
            var w = Settled(3, 1.5f, true);
            w.State.Fighter0.Position = PropState.At(w.State.Props, 2).Position + new Vec2(0, .75f + w.Config.HalfBody);
            w.State.Fighter0.Velocity = Vec2.Zero; w.State.Fighter0.OnProp = 3; w.State.Fighter0.Grounded = true;
            float drift = 0;
            for (int t = 0; t < 360; t++)
            {
                w.Step(default, default);
                for (int i = 0; i < 3; i++) drift = Math.Max(drift, Math.Abs(PropState.At(w.State.Props, i).Position.X));
            }
            Assert.Less(drift, .02f);
            for (int i = 0; i < 3; i++) Assert.IsTrue(PropPhysics.Asleep(PropState.At(w.State.Props, i), w.Config.Combat.Props));
        }

        [Test]
        public void KnockoutThresholdDoesNotSuddenlySetMinimumVelocity()
        {
            var before = DirectHit(23.99f, true); var after = DirectHit(24.01f, true);
            var oldBefore = DirectHit(23.99f, false); var oldAfter = DirectHit(24.01f, false);
            TestContext.WriteLine($"threshold: smooth {before.Velocity.X:F5} -> {after.Velocity.X:F5}; legacy {oldBefore.Velocity.X:F5} -> {oldAfter.Velocity.X:F5}");
            Assert.Greater(after.Velocity.X, before.Velocity.X);
            Assert.Less(after.Velocity.X - before.Velocity.X, .03f);
            Assert.Greater(oldAfter.Velocity.X - oldBefore.Velocity.X, 5f);
            Assert.Zero(after.LooseTicks);
            float previous = after.Velocity.X;
            for (float power = 25; power <= 36; power++)
            {
                var b = DirectHit(power, true);
                Assert.GreaterOrEqual(b.Velocity.X, previous); Assert.Less(b.Velocity.X - previous, 1f);
                previous = b.Velocity.X;
            }
            Assert.AreEqual(12f, DirectHit(36, true).Velocity.X, .0001f);
        }

        [Test]
        public void ReleasedContactRetainsFriction_RecoversGradually_AndKeepsGroundFriction()
        {
            var w = Settled(3, 1.5f, true); float previous = 0;
            for (int remaining = 20; remaining >= 0; remaining--)
            {
                var state = w.State.Props;
                for (int i = 0; i < state.Count; i++) PropState.Get(ref state, i).SleepTicks = 0;
                PropState.Get(ref state, 0).LooseTicks = remaining + 1; // Step decrements once.
                var scratch = new PropScratch();
                PropPhysics.Step(ref state, w.Arena, w.Config, scratch, 0, new SimEventBuffer());
                var contacts = scratch.Contacts.Take(scratch.ContactCount).ToArray();
                var box = contacts.Where(c => c.Other >= 0).Min(c => c.Friction);
                Assert.GreaterOrEqual(box, .7f * .15f - .000001f);
                Assert.GreaterOrEqual(box, previous);
                if (remaining < 20) Assert.Less(box - previous, .05f);
                foreach (var ground in contacts.Where(c => c.Other < 0)) Assert.AreEqual(.7f, ground.Friction);
                previous = box;
            }
            Assert.AreEqual(.7f, previous);
        }

        [Test]
        public void CurrentProfile_ReplayRollbackAndSeparatedMotionAreDeterministic()
        {
            var w = Settled(3, 1.5f, true); PlaceFighter(w, 1.5f, 34);
            var start = w.State; var replay = new SimWorld(w.Config, w.Arena, start);
            var sync = new SyncTest<SimWorld>(w);
            bool released = false;
            for (int t = 0; t < 480; t++)
            {
                Assert.IsTrue(sync.Advance(default, default), "rollback tick " + t);
                replay.Step(default, default);
                Assert.AreEqual(w.ComputeChecksum(), replay.ComputeChecksum(), "replay tick " + t);
                released |= PropState.At(w.State.Props, 0).LooseTicks > 0;
            }
            Assert.IsTrue(released);
            // Once contacts are gone, changing one body's speed cannot pull another with it.
            for (int i = 0; i < w.State.Props.Count; i++)
            {
                ref var b = ref PropState.Get(ref w.State.Props, i);
                b.Position = new Vec2(-6 + 6 * i, 3); b.Velocity = new Vec2(i - 1, 0);
                b.AngularVelocity = i + 1; b.SleepTicks = 0; b.Warm = default;
            }
            var independent = new SimWorld(w.Config, w.Arena, w.State);
            PropState.Get(ref independent.State.Props, 0).Velocity.X = -4;
            for (int t = 0; t < 15; t++)
            {
                w.Step(default, default); independent.Step(default, default);
                Assert.AreEqual(PropState.At(w.State.Props, 1), PropState.At(independent.State.Props, 1));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void RecordedConfigFindsOldAndNewProfile_EvenAfterCurrentToggleChanges(bool smooth)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Box_Response_Config_Probe"); root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            CombatTuning tuning = null;
            try
            {
                var runner = root.AddComponent<MatchRunner>(); runner.BrawlExperiment = true; runner.Rules = RuleSet.Brawl;
                runner.Tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>("Assets/_Project/Data/Tuning/MovementTuning.asset");
                tuning = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/_Project/Data/Tuning/CombatTuning.asset"));
                runner.CombatTuning = tuning;
                Assert.IsTrue(tuning.SmoothBoxRelease, "현재 에셋의 기본값이 새 경기 설정에 들어간다.");
                tuning.SmoothBoxRelease = smooth;
                var c = runner.PreviewConfig(false); Assert.AreEqual(smooth, c.Combat.Props.SmoothBoxRelease);
                var a = Arena(3, 1.5f); var w = new SimWorld(c, a, SimState.CreateRound(c, a, 77, a.SpawnFoot0, a.SpawnFoot1, MatchPhase.Fighting));
                var record = InputRecording.Begin(c, new[] { a }, w.State.Match.Rotation, 77, w.State.Fighter0.Position,
                    w.State.Fighter1.Position, w.State.Match.Phase, c.Rules.Format());
                for (int t = 0; t < 540; t++)
                {
                    var input = new InputFrame(t < 240 ? 1 : 0, 0, t >= 60 && t < 100 ? InputButtons.Attack : InputButtons.None, 1);
                    record.Append(input, default); w.Step(input, default);
                }
                record.Finish(w.ComputeChecksum());
                var loaded = InputRecording.FromBytes(record.ToBytes()); tuning.SmoothBoxRelease = !smooth;
                var found = runner.FindRecordedConfig(loaded); Assert.NotNull(found);
                Assert.AreEqual(c.ComputeChecksum(), found.ComputeChecksum());
                var replay = new SimWorld(found, a, loaded.CreateInitialState(found, a));
                for (int t = 0; t < loaded.Count; t++) replay.Step(loaded.GetP1(t), loaded.GetP2(t));
                Assert.AreEqual(loaded.FinalChecksum, replay.ComputeChecksum());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); if (tuning != null) Object.DestroyImmediate(tuning); }
        }

        [TestCase(false)] [TestCase(true)]
        public void OnlineSettingsAcceptMatchingProfileAndRejectDifferentOne(bool mismatch)
        {
            var c = Config(true); var a = Arena(3, 1.5f); uint map = OnlineMapManifest.Compute(new[] { a });
            MemoryTransport.Pair(out var ma, out var mb);
            var host = new LockstepSession(ma, true, c.ComputeChecksum(), map, 77, 3);
            var guest = new LockstepSession(mb, false, Config(!mismatch).ComputeChecksum(), map, 0, 0);
            try
            {
                for (int i = 0; i < 120; i++) { guest.Poll(i / 60d); host.Poll(i / 60d); }
                if (mismatch) Assert.AreNotEqual(SessionState.Running, guest.State);
                else
                {
                    Assert.AreEqual(SessionState.Running, host.State); Assert.AreEqual(SessionState.Running, guest.State);
                    var hostWorld = Settled(3, 1.5f, true); PlaceFighter(hostWorld, 1.5f, 34);
                    var guestWorld = new SimWorld(c, a, hostWorld.State);
                    var hostSums = new System.Collections.Generic.List<uint>();
                    var guestSums = new System.Collections.Generic.List<uint>();
                    host.Begin(hostWorld.State.Tick, new BrawlBot(() => hostWorld, 0, 0xB11u));
                    guest.Begin(guestWorld.State.Tick, new BrawlBot(() => guestWorld, 1, 0xB22u));
                    for (int frame = 120; frame < 720; frame++)
                    {
                        host.Poll(frame / 60d); guest.Poll(frame / 60d);
                        for (int i = 0; i < 2 && host.TryGetInputs(hostWorld.State.Tick, out var p0, out var p1); i++)
                        {
                            hostWorld.Step(p0, p1); uint sum = hostWorld.ComputeChecksum(); hostSums.Add(sum);
                            host.ReportChecksum(hostWorld.State.Tick, sum);
                        }
                        for (int i = 0; i < 2 && guest.TryGetInputs(guestWorld.State.Tick, out var p0, out var p1); i++)
                        {
                            guestWorld.Step(p0, p1); uint sum = guestWorld.ComputeChecksum(); guestSums.Add(sum);
                            guest.ReportChecksum(guestWorld.State.Tick, sum);
                        }
                    }
                    int count = Math.Min(hostSums.Count, guestSums.Count);
                    Assert.Greater(count, 450);
                    CollectionAssert.AreEqual(hostSums.Take(count), guestSums.Take(count));
                    Assert.AreEqual(SessionState.Running, host.State); Assert.AreEqual(SessionState.Running, guest.State);
                }
            }
            finally { ma.Dispose(); mb.Dispose(); }
        }
    }
}
