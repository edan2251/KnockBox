using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>무기 표시가 판정 좌표·실제 소유자를 사용하고 경기 상태를 바꾸지 않는지 검사한다.</summary>
    public class BrawlWeaponPresentationTests
    {
        GameObject root;
        BrawlWeaponView view;
        PaletteAsset palette;
        ShapeLibrary shapes;
        SimConfig config;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("BrawlWeaponPresentationTest") { hideFlags = HideFlags.HideAndDontSave };
            palette = ScriptableObject.CreateInstance<PaletteAsset>();
            palette.p1Body = new Color(0.1f, 0.7f, 0.2f, 1f);
            palette.p2Body = new Color(0.8f, 0.2f, 0.6f, 1f);
            shapes = ScriptableObject.CreateInstance<ShapeLibrary>();
            view = root.AddComponent<BrawlWeaponView>();
            view.Setup(palette, shapes);
            config = new SimConfig(MovementParams.Default, MatchParams.Default,
                BrawlParams.Combat(CombatParams.Default, weaponsEnabled: true), RuleSet.Brawl);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (palette != null) Object.DestroyImmediate(palette);
            if (shapes != null) Object.DestroyImmediate(shapes);
        }

        SimState State(BrawlWeaponPhase phase)
        {
            var state = new SimState
            {
                Weapon = new BrawlWeaponState
                {
                    Enabled = true,
                    Kind = BrawlWeaponKind.Pistol,
                    Phase = phase,
                    Owner = -1,
                    Ammo = 6,
                    Position = new Vec2(2f, 3f),
                    LandingPosition = new Vec2(2f, config.Combat.Weapons.GroundHeight),
                },
            };
            state.Match.Round = 1;
            state.Fighter0.Position = new Vec2(-3f, 1f);
            state.Fighter0.Facing = 1;
            state.Fighter1.Position = new Vec2(4f, 1f);
            state.Fighter1.Facing = -1;
            return state;
        }

        Transform Find(string path) => root.transform.Find(Knockbox.Presentation.SceneObjectName.Path(path));
        SpriteRenderer Sprite(string path) => Find(path).GetComponent<SpriteRenderer>();
        TextMesh Readout => Find("WeaponReadout").GetComponent<TextMesh>();

        static void PositionIs(Vector2 expected, Vector3 actual)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.0001f));
        }

        [Test]
        public void WarningMarksLandingFloor_AndFallingUsesAuthoritativeInterpolation()
        {
            var warning = State(BrawlWeaponPhase.Warning);
            view.Render(warning, warning, 0.5f, 0f, config);
            Assert.IsTrue(Sprite("LandingMarker").enabled);
            PositionIs(new Vector2(2f, 0f), Find("LandingMarker").localPosition);
            Assert.IsFalse(Find("WeaponModel").gameObject.activeSelf);
            Assert.AreEqual("INCOMING PISTOL", Readout.text);

            var previous = State(BrawlWeaponPhase.Falling);
            var current = previous;
            current.Weapon.Position = new Vec2(2f, 1f);
            uint checksum = current.ComputeChecksum();
            view.Render(previous, current, 0.25f, 0f, config);
            Assert.IsTrue(Find("WeaponModel").gameObject.activeSelf);
            PositionIs(new Vector2(2f, 2.5f), Find("WeaponModel").localPosition);
            PositionIs(new Vector2(2f, 0f), Find("LandingMarker").localPosition);
            Assert.AreEqual(checksum, current.ComputeChecksum(), "표시는 판정 상태를 바꾸지 않는다");
        }

        [Test]
        public void LandingOrDropTransition_DoesNotInterpolateFromPreviousHeldPosition()
        {
            var previous = State(BrawlWeaponPhase.Held);
            previous.Weapon.Owner = 1;
            previous.Weapon.Position = new Vec2(20f, 20f);
            var current = State(BrawlWeaponPhase.Ground);
            current.Weapon.Position = current.Weapon.LandingPosition;
            view.Render(previous, current, 0.1f, 0f, config);
            PositionIs(new Vector2(2f, config.Combat.Weapons.GroundHeight), Find("WeaponModel").localPosition);
            Assert.IsFalse(Sprite("LandingMarker").enabled);
            Assert.AreEqual("PISTOL  RMB PICK UP", Readout.text);
        }

        [Test]
        public void ThrownWeaponUsesSimPositionAndTickPose_AndOnlyGroundOffersPickup()
        {
            var previous = State(BrawlWeaponPhase.Thrown);
            previous.Weapon.ThrowEnabled = true;
            previous.Weapon.Thrower = 1;
            previous.Weapon.PhaseTick = 4;
            var current = previous;
            current.Weapon.PhaseTick = 5;
            current.Weapon.Position = new Vec2(6f, 5f);
            uint checksum = current.ComputeChecksum();
            view.Render(previous, current, 0.25f, 0.1f, config);
            PositionIs(new Vector2(3f, 3.5f), Find("WeaponModel").localPosition);
            Assert.IsTrue(Find("WeaponModel").gameObject.activeSelf);
            Assert.IsFalse(Sprite("LandingMarker").enabled, "투척은 스폰 예고 마커와 별개다");
            Assert.AreEqual(palette.p2Body, Sprite("WeaponModel/Pistol/Slide").color);
            Assert.AreEqual("PISTOL IN FLIGHT", Readout.text);
            var rotation = Find("WeaponModel").localRotation;
            view.Render(previous, current, 0.25f, 0.8f, config);
            Assert.AreEqual(rotation, Find("WeaponModel").localRotation, "회전은 실제 시간 아닌 상태 틱으로 정한다");
            Assert.AreEqual(checksum, current.ComputeChecksum());

            var grounded = current;
            grounded.Weapon.Phase = BrawlWeaponPhase.Ground;
            grounded.Weapon.Position = grounded.Weapon.LandingPosition;
            view.Render(current, grounded, 0.25f, 0f, config);
            PositionIs(new Vector2(2f, config.Combat.Weapons.GroundHeight), Find("WeaponModel").localPosition);
            Assert.AreEqual("PISTOL  RMB PICK UP", Readout.text);
            Assert.IsFalse(Sprite("LandingMarker").enabled);
            Assert.AreEqual(Quaternion.identity, Find("WeaponModel").localRotation);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void HeldReadoutUsesOwnerPaletteAndPersistentAmmo(int owner)
        {
            var state = State(BrawlWeaponPhase.Held);
            state.Weapon.Owner = owner;
            state.Weapon.Ammo = 3;
            uint checksum = state.ComputeChecksum();
            view.Render(state, state, 0.5f, 0f, config);
            var fighter = state.GetFighter(owner);
            PositionIs(new Vector2(fighter.Position.X + fighter.Facing * 0.5f, fighter.Position.Y),
                Find("WeaponModel").localPosition);
            Assert.AreEqual(palette.FighterBody(owner), Sprite("WeaponModel/Pistol/Slide").color);
            // TextMesh는 색을 8비트 Color32로 저장하므로 실제 저장 정밀도에 맞춰 비교한다.
            var expectedTextColor = (Color)(Color32)PaletteAsset.ForText(palette.FighterBody(owner));
            Assert.AreEqual(expectedTextColor, Readout.color);
            Assert.AreEqual("PISTOL 3/6", Readout.text);
            Assert.AreEqual(checksum, state.ComputeChecksum());

            state.Weapon.Ammo = 0;
            view.Render(state, state, 1f, 0f, config);
            Assert.AreEqual("PISTOL EMPTY", Readout.text);
            Assert.IsTrue(Find("WeaponModel/Pistol").gameObject.activeSelf);
            state.Weapon.Kind = BrawlWeaponKind.Pan;
            view.Render(state, state, 1f, 0f, config);
            Assert.AreEqual("PAN", Readout.text);
            Assert.IsTrue(Find("WeaponModel/Pan").gameObject.activeSelf);
            Assert.IsFalse(Find("WeaponModel/Pistol").gameObject.activeSelf);
        }

        [Test]
        public void ActivePanFaceFollowsActualHitCircle_AndHitstopKeepsPose()
        {
            var state = State(BrawlWeaponPhase.Held);
            state.Weapon.Kind = BrawlWeaponKind.Pan;
            state.Weapon.Owner = 1;
            state.Fighter1.Action = ActionState.Attack;
            state.Fighter1.AttackId = AttackIds.Pan;
            state.Fighter1.AttackDir = new Vec2(-0.6f, 0.8f);
            ref readonly var attack = ref config.Combat.Weapons.Pan;
            for (int tick = attack.StartupTicks; tick < attack.StartupTicks + attack.ActiveTicks; tick++)
            {
                state.Fighter1.ActionTick = tick;
                var expected = FighterCombat.CircleCenter(state.Fighter1, attack, attack.Circle0);
                view.Render(state, state, 0.25f, 0f, config);
                PositionIs(new Vector2(expected.X, expected.Y), Find("WeaponModel/Pan/Face").position);
                state.Fighter1.HitstopTicks = 5;
                view.Render(state, state, 0.75f, 0.1f, config);
                PositionIs(new Vector2(expected.X, expected.Y), Find("WeaponModel/Pan/Face").position);
            }
        }

        [Test]
        public void ShotLineEndsAtActualHitPoint_AndRoundClearLeavesNoTrace()
        {
            var state = State(BrawlWeaponPhase.Held);
            state.Weapon.Owner = 1;
            view.Render(state, state, 1f, 0f, config);
            view.OnEvent(new SimEvent(100, SimEventType.BrawlWeaponShot, 1,
                new Vec2(2f, 3f), new Vec2(-6f, 8f), 5f));
            var tracer = Sprite("ShotTracer");
            Assert.IsTrue(tracer.enabled);
            PositionIs(new Vector2(2f, 3f), Find("MuzzleFlash").localPosition);
            var halfWidth = tracer.sprite.bounds.size.x * 0.5f;
            PositionIs(new Vector2(2f, 3f), tracer.transform.TransformPoint(new Vector3(-halfWidth, 0f, 0f)));
            PositionIs(new Vector2(-4f, 11f), tracer.transform.TransformPoint(new Vector3(halfWidth, 0f, 0f)));
            Assert.AreEqual(palette.p2Body, tracer.color);

            state.Weapon.Phase = BrawlWeaponPhase.Finished;
            view.Render(state, state, 1f, 0f, config);
            Assert.IsFalse(tracer.enabled);
            Assert.IsFalse(Sprite("MuzzleFlash").enabled);
            Assert.IsFalse(Find("WeaponModel").gameObject.activeSelf);
            Assert.IsFalse(Readout.gameObject.activeSelf);
            view.OnEvent(new SimEvent(101, SimEventType.BrawlWeaponShot, 0, Vec2.Zero, new Vec2(1f, 0f), 4f));
            view.Clear();
            Assert.IsFalse(tracer.enabled, "새 라운드/경기 초기화도 이전 사격선을 지운다");
        }
    }
}
