using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>라운드 무기 상태를 읽어 그리는 임시 외형. 낙하 위치와 발사선은 시뮬레이션 값을 그대로 사용한다.</summary>
    public sealed class BrawlWeaponView : MonoBehaviour
    {
        const float ShotSeconds = 0.14f;
        [SerializeField] Transform model;
        [SerializeField] Transform pan;
        [SerializeField] Transform pistol;
        [SerializeField] SpriteRenderer panFace;
        [SerializeField] SpriteRenderer pistolFace;
        [SerializeField] SpriteRenderer landing;
        [SerializeField] SpriteRenderer tracer;
        [SerializeField] SpriteRenderer muzzle;
        [SerializeField] TextMesh readout;
        [SerializeField] MeshRenderer readoutRenderer;
        PaletteAsset palette;
        float shotLeft;
        BrawlWeaponKind lastKind;
        BrawlWeaponPhase lastPhase;
        int lastOwner = -2;
        int lastAmmo = -1;
        int lastCapacity = -1;

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            if(model != null) { Clear(); return; }
            model = Child(transform, "WeaponModel");
            pan = Child(model, "Pan");
            Part(pan, "HandleOutline", shapes.Square, new Vector2(-0.18f, 0f), new Vector2(0.38f, 0.09f), palette.ink, 0);
            Part(pan, "Handle", shapes.Square, new Vector2(-0.18f, 0f), new Vector2(0.32f, 0.045f), palette.uiSub, 1);
            Part(pan, "Rim", shapes.Circle, new Vector2(0.1f, 0f), new Vector2(0.3f, 0.3f), palette.ink, 2);
            panFace = Part(pan, "Face", shapes.Circle, new Vector2(0.1f, 0f), new Vector2(0.24f, 0.24f), palette.uiSub, 3);
            Part(pan, "Inner", shapes.Circle, new Vector2(0.1f, 0f), new Vector2(0.13f, 0.13f), palette.ink, 4);

            pistol = Child(model, "Pistol");
            Part(pistol, "Grip", shapes.Square, new Vector2(-0.1f, -0.045f), new Vector2(0.13f, 0.2f), palette.ink, 0);
            Part(pistol, "Barrel", shapes.Square, new Vector2(0.18f, 0.065f), new Vector2(0.26f, 0.1f), palette.ink, 1);
            Part(pistol, "SlideOutline", shapes.Square, new Vector2(0f, 0.065f), new Vector2(0.4f, 0.17f), palette.ink, 2);
            pistolFace = Part(pistol, "Slide", shapes.Square, new Vector2(0f, 0.07f), new Vector2(0.34f, 0.11f), palette.uiSub, 3);

            landing = ShapeLibrary.Create(transform, "LandingMarker", shapes.Ring, SortingLayers.UI, 0, palette.energy);
            ShapeLibrary.SetSize(landing, 0.9f, 0.18f);
            tracer = ShapeLibrary.Create(transform, "ShotTracer", shapes.Square, SortingLayers.Fx, 10, palette.flash);
            muzzle = ShapeLibrary.Create(transform, "MuzzleFlash", shapes.Circle, SortingLayers.Fx, 11, palette.flash);
            ShapeLibrary.SetSize(muzzle, 0.18f, 0.18f);

            var textObject = Child(transform, "WeaponReadout").gameObject;
            readout = textObject.AddComponent<TextMesh>();
            readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var renderer = textObject.GetComponent<MeshRenderer>();
            readoutRenderer = renderer;
            renderer.sharedMaterial = readout.font.material;
            renderer.sortingLayerName = SortingLayers.UI;
            renderer.sortingOrder = 41;
            readout.fontSize = 40;
            readout.characterSize = 0.1f;
            readout.anchor = TextAnchor.MiddleCenter;
            readout.alignment = TextAlignment.Center;
            readout.fontStyle = FontStyle.Bold;
            Clear();
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(SceneObjectName.Normalize(name)).transform;
            child.SetParent(parent, false);
            return child;
        }

        static SpriteRenderer Part(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color, int order)
        {
            var part = ShapeLibrary.Create(parent, name, sprite, SortingLayers.Weapon, order, color);
            part.transform.localPosition = position;
            ShapeLibrary.SetSize(part, size.x, size.y);
            return part;
        }

        public void Clear()
        {
            model.gameObject.SetActive(false);
            landing.enabled = tracer.enabled = muzzle.enabled = false;
            readout.gameObject.SetActive(false);
            shotLeft = 0f;
            lastOwner = -2;
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Type != SimEventType.BrawlWeaponShot)
                return;
            // Direction은 단위 방향이 아닌 실제 명중 끝점까지의 변위다.
            var start = V(e.Position);
            var delta = V(e.Direction);
            tracer.transform.localPosition = start + delta * 0.5f;
            tracer.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            ShapeLibrary.SetSize(tracer, delta.magnitude, 0.045f);
            muzzle.transform.localPosition = start;
            tracer.color = palette.FighterBody(e.Fighter);
            muzzle.color = palette.flash;
            shotLeft = ShotSeconds;
            tracer.enabled = muzzle.enabled = true;
        }

        public void Render(in SimState previous, in SimState current, float alpha, float dt, SimConfig config)
        {
            ref readonly var weapon = ref current.Weapon;
            bool warning = weapon.Phase == BrawlWeaponPhase.Warning;
            bool falling = weapon.Phase == BrawlWeaponPhase.Falling;
            bool thrown = weapon.Phase == BrawlWeaponPhase.Thrown;
            bool grounded = weapon.Phase == BrawlWeaponPhase.Ground;
            bool held = weapon.Phase == BrawlWeaponPhase.Held && weapon.Owner >= 0 && weapon.Owner < 2;
            if (!weapon.Enabled || weapon.Kind == BrawlWeaponKind.None || (!warning && !falling && !thrown && !grounded && !held))
            {
                Clear();
                return;
            }

            shotLeft = Mathf.Max(0f, shotLeft - dt);
            tracer.enabled = muzzle.enabled = shotLeft > 0f;
            if (shotLeft > 0f)
            {
                tracer.color = PaletteAsset.WithAlpha(tracer.color, shotLeft / ShotSeconds);
                muzzle.color = PaletteAsset.WithAlpha(palette.flash, shotLeft / ShotSeconds);
            }

            landing.enabled = warning || falling;
            landing.transform.localPosition = V(weapon.LandingPosition) - new Vector2(0f, config.Combat.Weapons.GroundHeight);
            landing.color = PaletteAsset.WithAlpha(palette.energy, warning ? 0.9f : 0.6f);
            model.gameObject.SetActive(!warning);
            pan.gameObject.SetActive(weapon.Kind == BrawlWeaponKind.Pan);
            pistol.gameObject.SetActive(weapon.Kind == BrawlWeaponKind.Pistol);

            bool same = previous.Weapon.Enabled && previous.Weapon.Kind == weapon.Kind
                && previous.Weapon.Phase == weapon.Phase && previous.Weapon.Owner == weapon.Owner
                && previous.Match.Round == current.Match.Round;
            Vector2 position = same ? Vector2.Lerp(V(previous.Weapon.Position), V(weapon.Position), alpha) : V(weapon.Position);
            Vector2 direction = Vector2.right;
            Vector2 labelPosition = warning || falling ? V(weapon.LandingPosition) + new Vector2(0f, 0.45f) : position + new Vector2(0f, 0.5f);
            var color = thrown && weapon.Thrower >= 0 && weapon.Thrower < 2
                ? palette.FighterBody(weapon.Thrower) : palette.uiSub;
            if (held)
            {
                var fighter = current.GetFighter(weapon.Owner);
                var before = previous.GetFighter(weapon.Owner);
                var body = same ? Vector2.Lerp(V(before.Position), V(fighter.Position), alpha) : V(fighter.Position);
                direction = fighter.Action == ActionState.Attack && fighter.AttackDir.LengthSquared > 0f
                    ? V(fighter.AttackDir).normalized : new Vector2(fighter.Facing < 0 ? -1f : 1f, 0f);
                position = body + direction * 0.5f;
                if (weapon.Kind == BrawlWeaponKind.Pan && fighter.Action == ActionState.Attack && fighter.AttackId == AttackIds.Pan)
                {
                    ref readonly var attack = ref config.Combat.Weapons.Pan;
                    int first = attack.StartupTicks;
                    int last = first + attack.ActiveTicks - 1;
                    var pose = fighter;
                    pose.ActionTick = Mathf.Clamp(fighter.ActionTick, first, last);
                    // 모델 원점에서 팬 중심까지의 0.1을 빼면 활성 틱의 외형 중심이 실제 판정 중심에 놓인다.
                    var strikePosition = V(FighterCombat.CircleCenter(pose, attack, attack.Circle0)) - direction * 0.1f;
                    float reach = fighter.ActionTick < first
                        ? Mathf.Clamp01((fighter.ActionTick + 1f) / Mathf.Max(1, first))
                        : fighter.ActionTick > last
                            ? 1f - Mathf.Clamp01((fighter.ActionTick - last) / (float)Mathf.Max(1, attack.RecoveryTicks))
                            : 1f;
                    // 시간 적분 없이 ActionTick으로만 자세를 정해 히트스탑에서도 같은 자세를 유지한다.
                    position = Vector2.Lerp(V(fighter.Position) + direction * 0.5f, strikePosition, reach);
                }
                // 기존 폼 아이콘/체력 바 위에 별도 줄을 둔다.
                labelPosition = body + new Vector2(0f, 2.15f);
                color = palette.FighterBody(weapon.Owner);
            }
            model.localPosition = position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (thrown)
            {
                // 비행 위치/충돌은 시뮬레이션 값 그대로 쓰고 회전만 표시한다. 재생·일시정지도 같은 자세다.
                float flightTick = same ? Mathf.Lerp(previous.Weapon.PhaseTick, weapon.PhaseTick, alpha) : weapon.PhaseTick;
                angle = flightTick * 18f;
            }
            model.localRotation = Quaternion.Euler(0f, 0f, angle);
            panFace.color = pistolFace.color = color;
            readout.gameObject.SetActive(true);
            readout.transform.localPosition = labelPosition;
            readout.color = PaletteAsset.ForText(held || thrown ? color : palette.flash);
            if (lastKind != weapon.Kind || lastPhase != weapon.Phase || lastOwner != weapon.Owner
                || lastAmmo != weapon.Ammo || lastCapacity != config.Combat.Weapons.PistolAmmo)
            {
                string name = weapon.Kind == BrawlWeaponKind.Pan ? "PAN" : "PISTOL";
                readout.text = warning || falling ? "INCOMING " + name
                    : grounded ? name + "  RMB PICK UP"
                    : thrown ? name + " IN FLIGHT"
                    : weapon.Kind == BrawlWeaponKind.Pan ? "PAN"
                    : weapon.Ammo == 0 ? "PISTOL EMPTY"
                    : $"PISTOL {weapon.Ammo}/{config.Combat.Weapons.PistolAmmo}";
                lastKind = weapon.Kind;
                lastPhase = weapon.Phase;
                lastOwner = weapon.Owner;
                lastAmmo = weapon.Ammo;
                lastCapacity = config.Combat.Weapons.PistolAmmo;
            }
            // 벽 가까이의 투척/보유 안내도 화면 안에 둔다. 무기 자체의 판정·표시 위치는 건드리지 않는다.
            var camera = Camera.main;
            if (camera != null && camera.orthographic)
            {
                float halfWidth = camera.orthographicSize * camera.aspect;
                float margin = readoutRenderer.bounds.extents.x + 0.15f;
                if (margin < halfWidth)
                {
                    var textPosition = readout.transform.position;
                    textPosition.x = Mathf.Clamp(textPosition.x, camera.transform.position.x - halfWidth + margin,
                        camera.transform.position.x + halfWidth - margin);
                    readout.transform.position = textPosition;
                }
            }
        }

        static Vector2 V(Vec2 value) => new Vector2(value.X, value.Y);
    }
}
