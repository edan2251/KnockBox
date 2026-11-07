using System.Collections.Generic;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Knockbox.Tests.EditMode
{
    /// <summary>로컬 슬롯의 실제 마우스 입력, 점선, YOU 색 및 연습 복귀를 외부 연결 없이 검증한다.</summary>
    public class LocalPlayerPresentationTests
    {
        readonly List<LockstepSession> sessions = new List<LockstepSession>();
        readonly List<ITransport> spareTransports = new List<ITransport>();
        GameObject root, temporaryCamera;
        MatchRunner runner;
        AimLineView aimLine;
        OnlineMatch online;
        PaletteAsset palette;
        ShapeLibrary shapes;
        Camera camera;
        Mouse mouse, previousMouse;
        Keyboard keyboard, previousKeyboard;
        bool previousRunInBackground;
        Vector2 cursorScreen, cursorWorld;

        [SetUp]
        public void SetUp()
        {
            previousRunInBackground = Application.runInBackground;
            previousMouse = Mouse.current;
            previousKeyboard = Keyboard.current;
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse.MakeCurrent();
            keyboard.MakeCurrent();

            camera = Camera.main;
            if (camera == null)
            {
                temporaryCamera = new GameObject("LocalPlayerTestCamera") { hideFlags = HideFlags.HideAndDontSave };
                temporaryCamera.tag = "MainCamera";
                camera = temporaryCamera.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 10f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
            }
            cursorScreen = camera.pixelRect.center;
            cursorWorld = camera.ScreenToWorldPoint(new Vector3(cursorScreen.x, cursorScreen.y, -camera.transform.position.z));
            SetMouse(false, false);

            // 비활성 임시 객체: 게임/온라인 Start, 기존 씬 재생 및 파일 기록을 시작하지 않는다.
            root = new GameObject("LocalPlayerPresentationTest") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            runner = root.AddComponent<MatchRunner>();
            runner.BrawlExperiment = true;
            var config = new SimConfig(MovementParams.Default, BrawlParams.Match(MatchParams.Default),
                BrawlParams.Combat(CombatParams.Default), RuleSet.Brawl);
            var arena = ArenaFiles.LoadByName(MatchRunner.BrawlArenaName);
            var initial = SimState.CreateRound(config, arena, 1u,
                new Vec2(cursorWorld.x - 6f, cursorWorld.y), new Vec2(cursorWorld.x + 6f, cursorWorld.y), MatchPhase.Fighting);
            runner.Begin(config, arena, initial);
            runner.Running = false;
            runner.World.State.Fighter0.Position = new Vec2(cursorWorld.x - 4f, cursorWorld.y);
            runner.World.State.Fighter1.Position = new Vec2(cursorWorld.x + 4f, cursorWorld.y);

            palette = ScriptableObject.CreateInstance<PaletteAsset>();
            // 기본 팔레트와 다른 색으로 고정 RGB가 아닌 현재 팔레트를 쓰는지도 검사한다.
            palette.p1Body = new Color(0.1f, 0.7f, 0.2f, 1f);
            palette.p2Body = new Color(0.8f, 0.2f, 0.6f, 1f);
            shapes = ScriptableObject.CreateInstance<ShapeLibrary>();
            var presenter = root.AddComponent<MatchPresenter>();
            presenter.Palette = palette;
            online = root.AddComponent<OnlineMatch>();
            // SendMessage는 다른 컴포넌트의 Awake까지 호출하므로 대상 메서드만 초기화한다.
            typeof(OnlineMatch).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(online, null);
            var lineObject = new GameObject("AimLine");
            lineObject.transform.SetParent(root.transform, false);
            aimLine = lineObject.AddComponent<AimLineView>();
            aimLine.Setup(palette, shapes);
        }

        [TearDown]
        public void TearDown()
        {
            if (runner != null)
                runner.Lockstep = null;
            foreach (var session in sessions)
                session.Dispose();
            sessions.Clear();
            foreach (var transport in spareTransports)
                transport.Dispose();
            spareTransports.Clear();
            if (root != null) Object.DestroyImmediate(root);
            if (temporaryCamera != null) Object.DestroyImmediate(temporaryCamera);
            if (palette != null) Object.DestroyImmediate(palette);
            if (shapes != null) Object.DestroyImmediate(shapes);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            Application.runInBackground = previousRunInBackground;
        }

        void SetMouse(bool attack, bool grab)
        {
            var state = new MouseState { position = cursorScreen }
                .WithButton(MouseButton.Left, attack).WithButton(MouseButton.Right, grab);
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            mouse.MakeCurrent();
            keyboard.MakeCurrent();
        }

        LockstepSession LocalSession(bool host)
        {
            MemoryTransport.Pair(out var local, out var peer);
            spareTransports.Add(peer);
            var session = new LockstepSession(local, host, 1u, 2u, 3u, 0);
            sessions.Add(session);
            return session;
        }

        [TestCase(0, 1f)]
        [TestCase(1, -1f)]
        public void MouseInput_UsesOwnFighter_WhenCursorIsBetweenPlayers(int slot, float directionX)
        {
            SetMouse(true, true);
            var input = new MouseAimInputSource(() => runner.World, slot).Poll(0);
            Assert.AreEqual(InputButtons.Attack | InputButtons.Grab, input.Buttons);
            Assert.IsTrue(AimDirections.TryGet(input.Aim, out var direction));
            Assert.That(direction.X, Is.EqualTo(directionX).Within(0.0001f));
            Assert.That(direction.Y, Is.EqualTo(0f).Within(0.0001f));
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void AimLineAndYou_UseLocalSlotPositionAndPalette(bool host, int slot)
        {
            runner.Lockstep = LocalSession(host);
            Assert.AreEqual(slot, runner.LocalPlayerSlot);
            uint before = runner.World.ComputeChecksum();
            aimLine.Render(runner, 0.5f);
            AssertDots(slot);
            Assert.AreEqual(palette.FighterBody(slot), online.LocalPlayerColor);
            Assert.AreEqual(before, runner.World.ComputeChecksum(), "표시 갱신은 경기 상태를 바꾸지 않는다");
        }

        void AssertDots(int slot)
        {
            var dots = aimLine.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.AreEqual(6, dots.Length);
            float directionX = slot == 0 ? 1f : -1f;
            // 이전 위치 ±6, 현재 위치 ±4의 중점에서 각각 커서 쪽으로 그려야 한다.
            var center = cursorWorld + new Vector2(slot == 0 ? -5f : 5f, 0f);
            var color = palette.FighterBody(slot);
            for (int i = 0; i < dots.Length; i++)
            {
                var expected = center + Vector2.right * directionX * (0.75f + 0.32f * i);
                Assert.That(Vector2.Distance(expected, dots[i].transform.localPosition), Is.LessThan(0.0001f), $"점 {i} 위치");
                Assert.That(dots[i].color.r, Is.EqualTo(color.r).Within(0.0001f), $"점 {i} R");
                Assert.That(dots[i].color.g, Is.EqualTo(color.g).Within(0.0001f), $"점 {i} G");
                Assert.That(dots[i].color.b, Is.EqualTo(color.b).Within(0.0001f), $"점 {i} B");
                Assert.That(dots[i].color.a, Is.EqualTo(Mathf.Lerp(0.9f, 0.35f, i / 5f)).Within(0.0001f), $"점 {i} alpha");
            }
        }

        [Test]
        public void ClearingGuestLockstep_RestoresPracticeP1AimAndYouColor()
        {
            runner.Lockstep = LocalSession(false);
            aimLine.Render(runner, 0.5f);
            AssertDots(1);
            Assert.AreEqual(palette.p2Body, online.LocalPlayerColor);

            // EndSession이 연습 경기로 돌아갈 때 수행하는 슬롯 연결 해제.
            runner.Lockstep = null;
            Assert.AreEqual(0, runner.LocalPlayerSlot);
            aimLine.Render(runner, 0.5f);
            AssertDots(0);
            Assert.AreEqual(palette.p1Body, online.LocalPlayerColor);

            runner.Lockstep = LocalSession(true);
            aimLine.Render(runner, 0.5f);
            AssertDots(0);
        }

        [Test]
        public void MouseAim_TravelsThroughLockstepInCorrectPlayerSlots()
        {
            SetMouse(true, false);
            var p1 = new MouseAimInputSource(() => runner.World, 0).Poll(0);
            SetMouse(false, true);
            var p2 = new MouseAimInputSource(() => runner.World, 1).Poll(0);
            Assert.AreNotEqual(p1.Aim, p2.Aim, "동일 커서가 두 선수에게 반대 방향이어야 한다");
            MemoryTransport.Pair(out var a, out var b);
            var host = new LockstepSession(a, true, 1u, 2u, 3u, 0);
            var guest = new LockstepSession(b, false, 1u, 2u, 0u, 0);
            sessions.Add(host);
            sessions.Add(guest);
            double now = 0;
            for (int i = 0; i < 30 && (host.State != SessionState.Running || guest.State != SessionState.Running); i++)
            {
                now += 1.0 / 60;
                guest.Poll(now);
                host.Poll(now);
            }
            Assert.AreEqual(SessionState.Running, host.State);
            Assert.AreEqual(SessionState.Running, guest.State);
            host.Begin(0, new ConstantInput(p1));
            guest.Begin(0, new ConstantInput(p2));
            bool hostReady = false, guestReady = false;
            InputFrame hp1 = default, hp2 = default, gp1 = default, gp2 = default;
            for (int i = 0; i < 30 && !(hostReady && guestReady); i++)
            {
                now += 1.0 / 60;
                host.Poll(now);
                guest.Poll(now);
                if (!hostReady) hostReady = host.TryGetInputs(0, out hp1, out hp2);
                if (!guestReady) guestReady = guest.TryGetInputs(0, out gp1, out gp2);
            }
            Assert.IsTrue(hostReady && guestReady, "두 끝에서 입력이 확정되어야 한다");
            Assert.AreEqual(p1, hp1);
            Assert.AreEqual(p2, hp2);
            Assert.AreEqual(p1, gp1);
            Assert.AreEqual(p2, gp2);
            var worldA = new SimWorld(runner.World.Config, runner.World.Arena, runner.World.State);
            var worldB = new SimWorld(runner.World.Config, runner.World.Arena, runner.World.State);
            worldA.Step(hp1, hp2);
            worldB.Step(gp1, gp2);
            Assert.AreEqual(worldA.ComputeChecksum(), worldB.ComputeChecksum());
        }

        [Test]
        public void AimLine_IsHiddenOutsideBrawlExperiment()
        {
            runner.BrawlExperiment = false;
            aimLine.Render(runner, 0.5f);
            Assert.IsFalse(aimLine.gameObject.activeSelf);
        }

        sealed class ConstantInput : IInputSource
        {
            readonly InputFrame frame;
            public ConstantInput(InputFrame frame) => this.frame = frame;
            public InputFrame Poll(int tick) => frame;
        }
    }
}
