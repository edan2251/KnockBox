using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 파이터 그리기(05-presentation.md 1~4절): 둥근 몸 + 잉크 외곽선 + 평면 그림자, 눈, 팔다리,
    /// 스프링 몸 변형과 기울기, 대시 잔상. 직전 상태와 현재 상태를 보간해서 그린다.
    /// 전투: 흰색 번쩍임, 히트스탑 중 떨림(이때는 보간하지 않고 몸 변형도 멈춘다), 피격 경직 중 구르는 회전,
    /// 찡그린 눈, 공격 단계에 맞춘 주먹 팔, 가드 때 몸 앞에 교차한 팔, 패링 구간의 밝은 외곽선.
    /// 모드 A: 충전은 납작해지며 떨리고 단계마다 외곽선이 밝아지며 눈이 가늘어진다. 돌진은 위력에 비례하는 잔상을 남기고,
    /// 위력 6 이상이면 hazard 외곽선, 팔을 진행 방향으로 뻗는다.
    /// 모드 B: 앞손이 연출 전용 무기를 쥔다. 활성 틱에는 손잡이를 첫 판정 원, 무기 끝을 마지막 판정 원 중심에 맞춰
    /// 무기 끝의 궤적이 시뮬레이션 판정과 일치한다. 시작·후딜은 휴식 자세와 첫·끝 키프레임 사이를 보간한다.
    /// 잡기: 두 팔을 잡기 상자 쪽으로 뻗고, 붙잡는 동안 손이 상대 몸에 붙는다. 던져진 쪽은 구르며 잔상을 남긴다.
    /// 시간은 시뮬레이션 시간으로 받는다(dt). 그래서 캡처와 실제 플레이가 같은 모습이 된다.
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        /// <summary>몸 그림 중심이 충돌 상자 중심보다 이만큼 위에 있다. 그 아래로 다리가 보인다.</summary>
        public const float VisualLift = 0.2f;
        const float BodySize = 1f;
        /// <summary>몸 그림을 숨겼는지(3D 시험 외형이 대신 그림). 잔상도 켜지 않는다.</summary>
        bool bodyHidden;
        /// <summary>그림을 돌리는 중력 각도(도). 시뮬레이션 GravityDir × 90을 부드럽게 따라간다.</summary>
        float gravityAngle;
        const float OutlineSize = 1.12f;
        const float MaxTiltDegrees = 8f;
        /// <summary>벽에 닿아 있을 때 몸 그림을 벽에서 떼는 거리(그림 1.12와 충돌 0.9의 차이만큼).</summary>
        const float WallClearance = 0.11f;
        // 착지 찌그러짐이 3프레임쯤에 처음 1로 돌아오고 8프레임 안팎에 거의 가라앉는 값.
        const float SpringStiffness = 900f;
        const float SpringDamping = 21f;
        const int GhostCount = 12;
        const int DashGhosts = 3;
        const float GhostLife = 0.25f;
        const float GhostAlpha = 0.4f;
        const float GhostInterval = 0.045f;
        /// <summary>돌진 잔상 간격과 길이(수명 = 기본 + 위력 × 비율).</summary>
        const float TrailInterval = 0.025f;
        const float TrailLifeBase = 0.06f;
        const float TrailLifePerPower = 0.011f;
        /// <summary>충전 2·3단계가 시작되는 충전 틱 비율.</summary>
        const float ChargeStage2 = 1f / 3f;
        const float WeaponOutline = 0.17f;
        const float WeaponFill = 0.09f;
        /// <summary>휴식 자세 무기 길이(손잡이 뒤 0.12 포함 전 길이는 손 → 끝 + 0.12).</summary>
        const float WeaponRestLength = 0.72f;
        const float WeaponHandle = 0.12f;
        /// <summary>피격 경직 중 회전 속도(도/초)를 속력에 곱하는 값.</summary>
        const float SpinPerSpeed = 38f;

        int index;
        PaletteAsset palette;
        [SerializeField] Transform pose;
        [SerializeField] SpriteRenderer body;
        [SerializeField] SpriteRenderer glow;
        const float GlowSize = 2.4f;
        const float BodyGlowAlpha = 0.16f;
        [SerializeField] SpriteRenderer outline;
        [SerializeField] SpriteRenderer[] eyes = new SpriteRenderer[2];
        [SerializeField] SpriteRenderer[] ghosts = new SpriteRenderer[GhostCount];
        readonly float[] ghostAge = new float[GhostCount];
        readonly float[] ghostLife = new float[GhostCount];
        [SerializeField] LimbRig rig;
        [SerializeField] SpriteRenderer weaponOutline;
        [SerializeField] SpriteRenderer weaponFill;
        Vector2 weaponTip;
        bool weaponVisible;

        Vector2 scale = Vector2.one;
        Vector2 scaleVelocity;
        bool holdImpulseFrame;
        float tilt;
        float wallShift;
        float blinkIn;
        float blinkLeft;
        float ghostEmitLeft;
        float ghostEmitTimer;
        int ghostNext;
        FxRandom random;
        Vector2 eyeSize;
        float flashLeft;
        float outlineFlashLeft;
        float shake;
        float spin;
        float wobbleTime;
        float trailTimer;
        float celebrateTime;
        bool shattered;

        public LimbRig Rig => rig;
        /// <summary>그린 무기 끝(모드 B). 활성 틱에는 마지막 판정 원 중심과 같다.</summary>
        public Vector2 WeaponTip => weaponTip;
        public bool WeaponVisible => weaponVisible;
        public Vector2 BodyScale => scale;
        public SpriteRenderer Body => body;
        /// <summary>몸 그림의 월드 중심(떨림·변형 포함).</summary>
        public Vector2 BodyCenter => body.transform.position;

        /// <summary>KO 파편으로 깨진 동안 몸과 팔다리를 숨긴다.</summary>
        public void SetShattered(bool value)
        {
            shattered = value;
            pose.gameObject.SetActive(!value);
            rig.gameObject.SetActive(!value);
            weaponOutline.enabled = weaponFill.enabled = weaponVisible && !value;
        }

        public bool Shattered => shattered;

        /// <summary>이긴 쪽이 통통 튀며 기뻐한다(KO 연출, 연출 전용 오프셋).</summary>
        public bool Celebrating { get; set; }

        /// <summary>몸 전체 흰색 번쩍임(초).</summary>
        public void Flash(float duration) => flashLeft = Mathf.Max(flashLeft, duration);

        /// <summary>외곽선만 번쩍임(클래시).</summary>
        public void FlashOutline(float duration) => outlineFlashLeft = Mathf.Max(outlineFlashLeft, duration);

        public void Setup(int fighter, PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            index = fighter;
            palette = paletteAsset;
            random = new FxRandom(0x9E37u + (uint)fighter * 7919u);
            blinkIn = random.Range(1.5f, 3.5f);

            if (pose == null)
            {
            pose = new GameObject("Pose").transform;
            pose.SetParent(transform, false);
            pose.localPosition = new Vector3(0f, VisualLift - BodySize * 0.5f, 0f);

            // 몸 둘레의 은은한 빛 번짐(08 5절 "캐릭터 몸의 밝은 부분 정도만 살짝"): 지형 뒤가 아니라 팔다리 뒤에 둔다.
            glow = ShapeLibrary.Create(pose, "Glow", shapes.SoftCircle, SortingLayers.LimbBack, -20, palette.FighterBody(fighter));
            glow.transform.localPosition = new Vector3(0f, BodySize * 0.5f, 0f);
            ShapeLibrary.SetSize(glow, GlowSize, GlowSize);
            outline = ShapeLibrary.CreateSliced(pose, "Outline", shapes.RoundedOutline, SortingLayers.Fighter, 0, palette.ink);
            outline.transform.localPosition = new Vector3(0f, BodySize * 0.5f, 0f);
            ShapeLibrary.SetSize(outline, OutlineSize, OutlineSize);
            body = ShapeLibrary.CreateSliced(pose, "Body", shapes.RoundedBody, SortingLayers.Fighter, 1, palette.FighterBody(fighter));
            body.transform.localPosition = new Vector3(0f, BodySize * 0.5f, 0f);
            ShapeLibrary.SetSize(body, BodySize, BodySize);

            // 모양으로도 구분: P1은 둥근 점, P2는 가로로 긴 캡슐.
            eyeSize = fighter == 0 ? new Vector2(0.16f, 0.16f) : new Vector2(0.24f, 0.1f);
            for (int i = 0; i < 2; i++)
            {
                eyes[i] = fighter == 0
                    ? ShapeLibrary.Create(pose, $"Eye{i}", shapes.Circle, SortingLayers.Fighter, 2, palette.ink)
                    : ShapeLibrary.CreateSliced(pose, $"Eye{i}", shapes.EyeCapsule, SortingLayers.Fighter, 2, palette.ink);
                ShapeLibrary.SetSize(eyes[i], eyeSize.x, eyeSize.y);
            }

            }
            eyeSize = fighter == 0 ? new Vector2(0.16f, 0.16f) : new Vector2(0.24f, 0.1f);
            glow.sprite = Application.isPlaying ? shapes.SoftCircle : shapes.Circle;
            for (int i = 0; i < GhostCount; i++)
            {
                // 잔상은 팔다리보다도 뒤에 그린다.
                if(ghosts[i] == null) ghosts[i] = ShapeLibrary.CreateSliced(transform.parent, $"Ghost{fighter}_{i}", shapes.RoundedBody, SortingLayers.LimbBack, -10, palette.FighterBody(fighter));
                ShapeLibrary.SetSize(ghosts[i], BodySize, BodySize);
                ghosts[i].enabled = false;
                ghostAge[i] = ghostLife[i] = GhostLife;
            }

            if(rig == null) rig = new GameObject("Limbs").AddComponent<LimbRig>();
            rig.transform.SetParent(transform, false);
            rig.Setup(shapes, palette.ink);

            if(weaponOutline == null) weaponOutline = ShapeLibrary.CreateSliced(transform, "WeaponOutline", shapes.LimbCapsule, SortingLayers.Weapon, 0, palette.ink);
            if(weaponFill == null) weaponFill = ShapeLibrary.CreateSliced(transform, "WeaponFill", shapes.LimbCapsule, SortingLayers.Weapon, 1, palette.FighterShade(fighter));
            weaponOutline.enabled = weaponFill.enabled = false;
        }

        /// <summary>몸 그림(스프라이트·팔다리)을 보이거나 숨긴다. 3D 시험 외형이 이 파이터를 대신 그릴 때 숨긴다(CR-004).</summary>
        public void SetBodyVisible(bool visible)
        {
            bodyHidden = !visible;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }

        /// <summary>순간 이동 뒤(라운드·시나리오 시작) 변형과 발 위치를 처음 상태로.</summary>
        public void ResetPose()
        {
            scale = Vector2.one;
            scaleVelocity = Vector2.zero;
            tilt = 0f;
            wallShift = 0f;
            ghostEmitLeft = 0f;
            flashLeft = 0f;
            outlineFlashLeft = 0f;
            shake = 0f;
            spin = 0f;
            trailTimer = 0f;
            celebrateTime = 0f;
            Celebrating = false;
            SetShattered(false);
            for (int i = 0; i < GhostCount; i++)
            {
                ghostAge[i] = ghostLife[i] = GhostLife;
                ghosts[i].enabled = false;
            }
            rig.ResetPose();
        }

        public void OnEvent(in SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Land:
                    // 착지: 가로 1.3 × 세로 0.7에서 튕기며 복원(세게 떨어질수록 조금 더).
                    float impact = Mathf.Clamp01(e.Strength / 30f);
                    scale = new Vector2(Mathf.Lerp(1.12f, 1.3f, impact), Mathf.Lerp(0.88f, 0.7f, impact));
                    scaleVelocity = Vector2.zero;
                    holdImpulseFrame = true;
                    break;
                case SimEventType.Jump:
                case SimEventType.AirJump:
                case SimEventType.WallJump:
                case SimEventType.PadBounce:
                    scale = new Vector2(0.8f, 1.25f);
                    scaleVelocity = Vector2.zero;
                    holdImpulseFrame = true;
                    break;
                case SimEventType.Dash:
                    ghostEmitLeft = DashGhosts;
                    ghostEmitTimer = 0f;
                    break;
                case SimEventType.LungeStart:
                    scale = new Vector2(1.25f, 0.82f);
                    scaleVelocity = Vector2.zero;
                    holdImpulseFrame = true;
                    trailTimer = 0f;
                    break;
                case SimEventType.Hit:
                    // 맞은 쪽: 4프레임 흰색, 히트스탑 동안 데미지에 비례해 떨림, 넉백 방향으로 찌그러짐.
                    Flash(4f / 60f);
                    shake = Mathf.Max(shake, Mathf.Min(0.035f + e.Strength * 0.004f, 0.12f));
                    Impulse(e.Direction, 0.25f);
                    break;
                case SimEventType.ConnectorBreak:
                    // 연결 상자를 깬 쪽이 충격 방향으로 짧게 눌렸다 복귀한다. 위치·공격 틱은 그대로다.
                    Impulse(e.Direction, -.18f);
                    FlashOutline(4f / 60f);
                    break;
                case SimEventType.GuardHit:
                    shake = Mathf.Max(shake, 0.03f);
                    Impulse(e.Direction, 0.12f);
                    break;
                case SimEventType.WallSplat:
                    // 부딪힌 면에 납작하게 붙는다.
                    Flash(4f / 60f);
                    shake = Mathf.Max(shake, 0.09f);
                    Impulse(new Vec2(e.Direction.Y, e.Direction.X), 0.35f);
                    break;
                case SimEventType.KO:
                    Flash(6f / 60f);
                    shake = Mathf.Max(shake, 0.12f);
                    break;
                case SimEventType.Grab:
                    // 손이 붙는 순간 짧은 번쩍임(붙잡힌 쪽).
                    Flash(2f / 60f);
                    break;
                case SimEventType.Spike:
                    Flash(3f / 60f);
                    shake = Mathf.Max(shake, 0.06f);
                    Impulse(new Vec2(0f, 1f), 0.25f);
                    break;
                case SimEventType.Throw:
                    Impulse(e.Direction, 0.3f);
                    trailTimer = 0f;
                    break;
            }
        }

        /// <summary>direction 쪽으로 늘어나고 옆으로 줄어드는 변형을 이번 프레임에 그대로 보여 준다.</summary>
        void Impulse(Vec2 direction, float amount)
        {
            bool horizontal = Mathf.Abs(direction.X) >= Mathf.Abs(direction.Y);
            scale = horizontal ? new Vector2(1f + amount, 1f - amount * 0.8f) : new Vector2(1f - amount * 0.8f, 1f + amount);
            scaleVelocity = Vector2.zero;
            holdImpulseFrame = true;
        }

        public void Render(in FighterState previous, in FighterState current, float alpha, float dt, ArenaDef arena, SimConfig config)
        {
            // 히트스탑 중에는 보간하지 않고 멈춘 순간을 그대로 보여 준다.
            bool frozen = current.HitstopTicks > 0;
            if (frozen)
                alpha = 1f;
            else
                shake = 0f;
            var position = new Vector2(
                Mathf.LerpUnclamped(previous.Position.X, current.Position.X, alpha),
                Mathf.LerpUnclamped(previous.Position.Y, current.Position.Y, alpha));
            transform.localPosition = new Vector3(position.x, position.y, 0f);

            if (!frozen)
            {
                UpdateSquash(current, dt, config);
                UpdateTilt(current, dt, config);
                UpdateSpin(current, dt);
            }
            // 벽에 붙으면 몸 그림만 살짝 떼어 외곽선이 벽에 묻히지 않게 한다(시뮬레이션 위치는 그대로).
            float wallTarget = current.WallSide != 0 && current.Action != ActionState.Hitstun ? -current.WallSide * WallClearance : 0f;
            wallShift = Mathf.Lerp(wallShift, wallTarget, 1f - Mathf.Exp(-30f * dt));
            // 히트스탑 떨림, 또는 충전이 쌓일수록 커지는 떨림.
            float tremble = shake;
            if (!frozen && Charging(current, config) && dt > 0f)
                tremble = 0.008f + 0.022f * ChargeProgress(current, config);
            var jitter = tremble > 0f ? new Vector2(random.Range(-1f, 1f), random.Range(-1f, 1f)) * tremble : Vector2.zero;
            // 이긴 쪽 기쁨: 제자리에서 통통 튄다.
            float hop = 0f;
            if (Celebrating)
            {
                celebrateTime += dt;
                hop = Mathf.Abs(Mathf.Sin(celebrateTime * 9f)) * 0.45f;
            }
            // 몸 크기(CR-004: 0.72면 0.8배)와 중력 방향(거미식 표면 이동: 발이 붙은 면 쪽)으로 그림 전체를 맞춘다.
            float size = config.Movement.BodySize / 0.9f;
            float up = current.GravityDir * 90f;
            gravityAngle = Mathf.LerpAngle(gravityAngle, up, frozen ? 0f : 1f - Mathf.Exp(-25f * dt));
            var foot = Quaternion.Euler(0f, 0f, gravityAngle) * new Vector3(wallShift + jitter.x, (VisualLift - BodySize * 0.5f + jitter.y + hop) * size, 0f);
            pose.localPosition = foot;
            pose.localScale = new Vector3(scale.x * size, scale.y * size, 1f);
            pose.localRotation = Quaternion.Euler(0f, 0f, gravityAngle + tilt + spin);

            UpdateColors(current, dt, config);
            UpdateEyes(current, dt);
            UpdateGhosts(current, dt, config);

            Vector2 hip0 = pose.TransformPoint(new Vector3(-0.17f, 0.04f, 0f));
            Vector2 hip1 = pose.TransformPoint(new Vector3(0.17f, 0.04f, 0f));
            Vector2 shoulder0 = pose.TransformPoint(new Vector3(-0.46f, 0.5f, 0f));
            Vector2 shoulder1 = pose.TransformPoint(new Vector3(0.46f, 0.5f, 0f));
            bool swing = current.Form == FighterForm.Paper && current.Action != ActionState.KO;
            Vector2 grip = default, tip = default;
            bool tipFixed = swing && SwingWeaponPose(current, position, alpha, config, out grip, out tip);
            if (tipFixed)
                rig.SetHands(grip, (current.Facing >= 0 ? shoulder0 : shoulder1) + new Vector2(-current.Facing * 0.2f, -0.25f), LimbRig.ArmLayers.Natural);
            else
                UpdateHandPose(current, position, shoulder0, shoulder1, alpha, config);
            rig.UpdateRig(hip0, hip1, shoulder0, shoulder1, position, current, arena, config.HalfBody, dt);
            UpdateWeapon(current, swing, tipFixed, tip);
        }

        /// <summary>
        /// 모드 B 공격 중 무기 자세(손잡이 = 첫 판정 원, 끝 = 마지막 판정 원). 공격 중이 아니면 false.
        /// 틱 진행도 t는 직전 틱(ActionTick - 1)과 현재 틱 사이를 보간 비율로 잇는다(alpha = 1이면 현재 틱과 정확히 같다).
        /// </summary>
        bool SwingWeaponPose(in FighterState f, Vector2 position, float alpha, SimConfig config, out Vector2 grip, out Vector2 tip)
        {
            grip = tip = default;
            if (f.Action != ActionState.Attack || f.AttackId == FighterState.NoAttack)
                return false;
            ref readonly var attack = ref config.GetAttack(f.AttackId);
            int facing = f.Facing >= 0 ? 1 : -1;
            float t = Mathf.Max(0f, f.ActionTick - 1 + alpha);
            int firstActive = attack.StartupTicks;
            int lastActive = attack.StartupTicks + attack.ActiveTicks - 1;
            var c0 = attack.Circle0;
            var c2 = attack.Circle2;
            Vector2 restGrip = new Vector2(0.62f, -0.05f);
            Vector2 restTip = restGrip + WeaponRestDirection * WeaponRestLength;
            Vector2 localGrip, localTip;
            if (t < firstActive)
            {
                // 시작: 휴식 자세에서 첫 키프레임으로 들어 올린다(뒤로 젖힘).
                float k = Smooth(t / firstActive);
                localGrip = Vector2.Lerp(restGrip, V(c0.Start), k);
                localTip = Vector2.Lerp(restTip, V(c2.Start), k);
            }
            else if (t <= lastActive)
            {
                float k = attack.ActiveTicks > 1 ? (t - firstActive) / (attack.ActiveTicks - 1) : 0f;
                localGrip = V(c0.At(k));
                localTip = V(c2.At(k));
            }
            else
            {
                // 후딜: 마지막 키프레임에서 휴식 자세로 돌아온다.
                float k = Smooth((t - lastActive) / Mathf.Max(attack.RecoveryTicks, 1));
                localGrip = Vector2.Lerp(V(c0.End), restGrip, k);
                localTip = Vector2.Lerp(V(c2.End), restTip, k);
            }
            grip = position + new Vector2(localGrip.x * facing, localGrip.y);
            tip = position + new Vector2(localTip.x * facing, localTip.y);
            return true;
        }

        static Vector2 V(Vec2 v) => new Vector2(v.X, v.Y);

        /// <summary>휴식 자세의 무기 방향(앞 위로 세워 든다. 아래로 늘어뜨리면 바닥을 뚫는다).</summary>
        static readonly Vector2 WeaponRestDirection = new Vector2(0.35f, 0.94f).normalized;

        /// <summary>무기를 그린 손에서 끝까지 그린다. 공격 중이 아니면 손에서 앞 위로 세워 든다(가드 중에는 거의 수직).</summary>
        void UpdateWeapon(in FighterState f, bool visible, bool tipFixed, Vector2 tip)
        {
            weaponVisible = visible;
            weaponOutline.enabled = weaponFill.enabled = visible && !shattered;
            if (!visible)
                return;
            int facing = f.Facing >= 0 ? 1 : -1;
            Vector2 hand = rig.DrawnHand(facing > 0 ? 1 : 0);
            if (!tipFixed)
            {
                var up = f.IsGuarding ? new Vector2(facing * 0.12f, 1f).normalized : new Vector2(WeaponRestDirection.x * facing, WeaponRestDirection.y);
                tip = hand + up * WeaponRestLength;
            }
            weaponTip = tip;
            Vector2 d = tip - hand;
            float length = d.magnitude;
            Vector2 dir = length > 1e-4f ? d / length : new Vector2(facing, 0f);
            Vector2 butt = hand - dir * WeaponHandle;
            PlaceStick(weaponOutline, butt, tip, WeaponOutline);
            PlaceStick(weaponFill, butt + dir * 0.03f, tip - dir * 0.03f, WeaponFill);
        }

        static void PlaceStick(SpriteRenderer sr, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 d = b - a;
            sr.transform.position = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.size = new Vector2(d.magnitude + thickness, thickness);
        }

        static float ChargeProgress(in FighterState f, SimConfig config) =>
            Mathf.Clamp01(FighterCombat.ChargeTicks(f, config) / (float)config.Combat.Charge.FullChargeTicks);

        /// <summary>충전 연출을 보일지. 몸 폼은 실제 돌진 가능한 최소 충전에 닿으면 눌리고 색이 바뀐다.</summary>
        static bool Charging(in FighterState f, SimConfig config) =>
            f.Action == ActionState.Charge && (f.Form != FighterForm.Body || FighterCombat.IsLungeReady(f, config));

        /// <summary>충전 단계 1~3: 1/3 전, 최대 충전 전, 최대 충전.</summary>
        public static int ChargeStage(in FighterState f, SimConfig config)
        {
            float k = ChargeProgress(f, config);
            return k >= 1f ? 3 : k >= ChargeStage2 ? 2 : 1;
        }

        void UpdateColors(in FighterState f, float dt, SimConfig config)
        {
            body.color = flashLeft > 0f ? palette.flash : f.Action == ActionState.KO ? palette.FighterShade(index) : palette.FighterBody(index);
            // 우선순위: 번쩍임 > 패링 구간(밝은 외곽선) > 위험(위력 6 이상) > 충전 단계 > 기본.
            Color line;
            float power = Mathf.Sqrt(f.Velocity.X * f.Velocity.X + f.Velocity.Y * f.Velocity.Y) * FighterCombat.Aggression(f, config);
            if (outlineFlashLeft > 0f)
                line = palette.flash;
            else if (f.ParryTicks > 0)
                line = palette.energy;
            else if (power >= config.Combat.Charge.PowerThreshold)
                line = palette.hit;
            else if (Charging(f, config))
            {
                int stage = ChargeStage(f, config);
                // 08 5절: 1단계 energySoft → 2단계 energy → 3단계(최대 충전)는 흰색과 energy 사이로 깜박인다.
                line = stage == 1 ? palette.energySoft : stage == 2 ? palette.energy : (f.ActionTick / 4) % 2 == 0 ? palette.flash : palette.energy;
            }
            else
                line = palette.ink;
            outline.color = line;
            // 글로: 평소 몸 색, 번쩍일 때 흰색, 충전 2·3단계는 에너지 색으로 조금 더 밝게.
            if (flashLeft > 0f)
                glow.color = PaletteAsset.WithAlpha(palette.flash, 0.4f);
            else if (Charging(f, config) && ChargeStage(f, config) >= 2)
                glow.color = PaletteAsset.WithAlpha(palette.energy, 0.3f);
            else
                glow.color = PaletteAsset.WithAlpha(palette.FighterBody(index), f.Action == ActionState.KO ? 0f : BodyGlowAlpha);
            flashLeft = Mathf.Max(0f, flashLeft - dt);
            outlineFlashLeft = Mathf.Max(0f, outlineFlashLeft - dt);
        }

        /// <summary>
        /// 공격: 시작 동안 앞손을 뒤로 젖히고, 활성 동안 판정 원을 향해 뻗고, 후딜 동안 제자리로 돌아온다.
        /// 가드: 두 팔을 몸 앞에 교차한다.
        /// </summary>
        void UpdateHandPose(in FighterState f, Vector2 position, Vector2 shoulder0, Vector2 shoulder1, float alpha, SimConfig config)
        {
            int facing = f.Facing >= 0 ? 1 : -1;
            Vector2 front = facing > 0 ? shoulder1 : shoulder0;
            Vector2 back = facing > 0 ? shoulder0 : shoulder1;
            if (f.IsGuarding)
            {
                var chest = (shoulder0 + shoulder1) * 0.5f;
                rig.SetHands(chest + new Vector2(facing * 0.55f, -0.02f), chest + new Vector2(facing * 0.5f, -0.24f), LimbRig.ArmLayers.BothFront);
                return;
            }
            if (f.Action == ActionState.Climb || f.Action == ActionState.Mantle)
            {
                // 기어오르기: 두 손으로 벽 위쪽을 번갈아 짚는다. 모서리 오르기: 두 손으로 윗면을 짚고 몸을 끌어올린다(CR-004).
                bool mantle = f.Action == ActionState.Mantle;
                bool swap = !mantle && (f.ActionTick / 8) % 2 == 1;
                var high = new Vector2(facing * 0.42f, mantle ? 0.55f : 0.5f);
                var low = new Vector2(facing * 0.42f, mantle ? 0.5f : 0.15f);
                rig.SetHands(front + (swap ? low : high), back + (swap ? high : low), LimbRig.ArmLayers.BothFront);
                return;
            }
            if (f.Action == ActionState.Charge)
            {
                // 충전: 팔을 뒤로 젖힌다(충전이 쌓일수록 더).
                float charge = ChargeProgress(f, config);
                rig.SetHands(front + new Vector2(-facing * (0.2f + 0.14f * charge), -0.1f), back + new Vector2(-facing * (0.28f + 0.1f * charge), -0.12f), LimbRig.ArmLayers.BothBack);
                return;
            }
            if (f.Action == ActionState.Grab)
            {
                // 잡기: 두 팔을 잡기 상자 쪽으로 뻗는다(시작 동안 점점, 헛잡기 후딜 동안 거둬들임).
                ref readonly var g = ref config.Combat.Grab;
                float gt = Mathf.Max(0f, f.ActionTick - 1 + alpha);
                float reach = gt < g.StartupTicks ? Smooth(gt / g.StartupTicks)
                    : gt < g.StartupTicks + g.ActiveTicks ? 1f
                    : 1f - Smooth((gt - g.StartupTicks - g.ActiveTicks) / g.WhiffRecoveryTicks);
                var center = FighterCombat.GrabBoxCenter(f, config) - f.Position;
                var box = position + new Vector2(center.X, center.Y + 0.1f);
                var restFront = front + new Vector2(facing * 0.16f, -0.38f);
                var restBack = back + new Vector2(-facing * 0.16f, -0.38f);
                rig.SetHands(Vector2.Lerp(restFront, box + new Vector2(0.05f * facing, 0.18f), reach),
                    Vector2.Lerp(restBack, box + new Vector2(0f, -0.12f), reach), LimbRig.ArmLayers.BothFront);
                return;
            }
            if (f.Action == ActionState.Holding)
            {
                // 붙잡기: 두 손을 상대 몸 가까운 쪽에 붙인다(상대는 몸 앞 0.8).
                ref readonly var g = ref config.Combat.Grab;
                var victimEdge = position + new Vector2((g.HoldOffset - config.HalfBody + 0.02f) * facing, 0.1f);
                rig.SetHands(victimEdge + new Vector2(0f, 0.2f), victimEdge + new Vector2(0f, -0.15f), LimbRig.ArmLayers.BothFront);
                return;
            }
            if (f.Action == ActionState.Held)
            {
                // 붙잡힘: 팔을 위로 버둥거린다.
                float wave = Mathf.Sin(wobbleTime * 24f) * 0.08f;
                rig.SetHands(front + new Vector2(facing * 0.1f, 0.3f + wave), back + new Vector2(-facing * 0.1f, 0.3f - wave), LimbRig.ArmLayers.Natural);
                return;
            }
            if (f.Action == ActionState.Lunge)
            {
                // 돌진: 두 팔을 진행 방향으로 뻗는다.
                var v = new Vector2(f.Velocity.X, f.Velocity.Y);
                var dir = v.sqrMagnitude > 1e-4f ? v.normalized : new Vector2(facing, 0f);
                rig.SetHands(front + dir * 0.55f, back + dir * 0.5f + new Vector2(0f, -0.08f), LimbRig.ArmLayers.Natural);
                return;
            }
            if (f.Action != ActionState.Attack || f.AttackId == FighterState.NoAttack)
            {
                rig.ClearHands();
                return;
            }
            ref readonly var attack = ref config.GetAttack(f.AttackId);
            var c = attack.Circle0;
            // 직전 틱과 현재 틱 사이(alpha = 1이면 현재 틱).
            float t = Mathf.Max(0f, f.ActionTick - 1 + alpha);
            var rest = front + new Vector2(facing * 0.16f, -0.38f);
            var wind = front + new Vector2(-facing * 0.14f, -0.14f);
            int last = attack.StartupTicks + attack.ActiveTicks;
            float k = Mathf.Clamp01((Mathf.Min(t, last - 1) - attack.StartupTicks) / Mathf.Max(attack.ActiveTicks - 1, 1));
            var offset = Vector2.Lerp(new Vector2(c.Start.X, c.Start.Y), new Vector2(c.End.X, c.End.Y), k);
            // 조준 공격(CR-004 주먹)은 조준 방향 기준, 그 외는 바라보는 쪽으로 뒤집는다.
            var world = FighterCombat.AttackOffset(f, new Vec2(offset.x, offset.y));
            var strike = position + new Vector2(world.X, world.Y);
            Vector2 hand;
            if (t < attack.StartupTicks)
                hand = Vector2.Lerp(rest, wind, Smooth(t / attack.StartupTicks));
            else if (t < last)
                hand = strike;
            else
                hand = Vector2.Lerp(strike, rest, Smooth((t - last) / Mathf.Max(attack.RecoveryTicks, 1)));
            rig.SetHands(hand, back + new Vector2(-facing * 0.22f, -0.22f), LimbRig.ArmLayers.Natural);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>피격 경직 중에는 속도에 비례해 구르듯 회전한다. 끝나면 가까운 바로 선 자세로 돌아온다.</summary>
        void UpdateSpin(in FighterState f, float dt)
        {
            if (f.IsLaunched)
            {
                float speed = Mathf.Sqrt(f.Velocity.X * f.Velocity.X + f.Velocity.Y * f.Velocity.Y);
                float direction = f.Velocity.X > 0.01f ? -1f : f.Velocity.X < -0.01f ? 1f : -f.Facing;
                spin += direction * speed * SpinPerSpeed * dt;
                return;
            }
            spin = Mathf.Repeat(spin + 180f, 360f) - 180f;
            spin = Mathf.Lerp(spin, 0f, 1f - Mathf.Exp(-16f * dt));
        }

        void UpdateSquash(in FighterState f, float dt, SimConfig config)
        {
            // 이동 속도에 따라 진행 방향으로 최대 1.25 늘어난다.
            float vx = Mathf.Abs(f.Velocity.X), vy = Mathf.Abs(f.Velocity.Y);
            float stretch = 1f + 0.25f * Mathf.Clamp01((Mathf.Max(vx, vy) - 6f) / 20f);
            var target = vx >= vy ? new Vector2(stretch, 1f / stretch) : new Vector2(1f / stretch, stretch);
            if (Charging(f, config))
            {
                // 충전: 납작해진다.
                float k = ChargeProgress(f, config);
                target = new Vector2(1f + 0.16f * k, 1f - 0.2f * k);
            }

            // 사건이 일어난 프레임에는 찌그러진 모양을 그대로 보여 준다.
            if (holdImpulseFrame)
            {
                holdImpulseFrame = false;
                return;
            }
            // 스프링-댐퍼. 큰 dt에서도 안정하게 잘게 나눠 적분한다.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 240f)), 1, 16);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                var accel = (target - scale) * SpringStiffness - scaleVelocity * SpringDamping;
                scaleVelocity += accel * h;
                scale += scaleVelocity * h;
            }
        }

        void UpdateTilt(in FighterState f, float dt, SimConfig config)
        {
            float maxRunSpeed = config.Movement.MaxRunSpeed;
            wobbleTime += dt;
            float target;
            switch (f.Action)
            {
                case ActionState.Charge:
                    target = f.Facing * 6f;
                    break;
                case ActionState.Lunge:
                    target = -Mathf.Clamp(f.Velocity.X / config.Combat.Charge.LungeMaxSpeed, -1f, 1f) * 14f;
                    break;
                case ActionState.WallSlide:
                    target = 0f;
                    break;
                case ActionState.Stagger:
                    // 비틀거림: 좌우로 크게 흔들린다.
                    target = Mathf.Sin(wobbleTime * 19f) * 14f;
                    break;
                case ActionState.Recoil:
                    // 클래시 반동: 상대 반대쪽으로 젖힌다.
                    target = f.Facing * 12f;
                    break;
                case ActionState.Attack:
                    target = -f.Facing * 7f;
                    break;
                default:
                    target = -Mathf.Clamp(f.Velocity.X / maxRunSpeed, -1f, 1f) * MaxTiltDegrees;
                    break;
            }
            tilt = Mathf.Lerp(tilt, target, 1f - Mathf.Exp(-12f * dt));
        }

        void UpdateEyes(in FighterState f, float dt)
        {
            blinkIn -= dt;
            if (blinkIn <= 0f)
            {
                blinkLeft = 0.1f;
                blinkIn = random.Range(2.2f, 4f);
            }
            blinkLeft = Mathf.Max(0f, blinkLeft - dt);
            float open = blinkLeft > 0f ? 0.15f : 1f;
            float wide = 1f;
            // 맞았을 때는 찡그린 납작한 선, 가드 중에는 가늘게.
            if (f.IsLaunched || f.Action == ActionState.KO || f.Action == ActionState.Stagger || f.Action == ActionState.Held)
            {
                open = 0.035f / eyeSize.y;
                wide = 1.35f;
            }
            else if (f.IsGuarding)
            {
                open = Mathf.Min(open, 0.6f);
            }
            else if (f.Action == ActionState.Charge)
            {
                open = Mathf.Min(open, 0.45f);
            }

            // 바라보는 쪽과 속도 쪽을 본다.
            float lookX = f.Facing * 0.08f + Mathf.Clamp(f.Velocity.X * 0.004f, -0.04f, 0.04f);
            float lookY = Mathf.Clamp(f.Velocity.Y * 0.004f, -0.06f, 0.06f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -0.17f : 0.17f;
                eyes[i].transform.localPosition = new Vector3(side + lookX, 0.6f + lookY, 0f);
                ShapeLibrary.SetSize(eyes[i], eyeSize.x * wide, eyeSize.y * open);
            }
        }

        void EmitGhost(float life)
        {
            var ghost = ghosts[ghostNext];
            ghost.transform.position = body.transform.position;
            ghost.transform.rotation = pose.rotation;
            ShapeLibrary.SetSize(ghost, BodySize * scale.x, BodySize * scale.y);
            ghost.enabled = !bodyHidden;
            ghostAge[ghostNext] = 0f;
            ghostLife[ghostNext] = life;
            ghostNext = (ghostNext + 1) % GhostCount;
        }

        void UpdateGhosts(in FighterState f, float dt, SimConfig config)
        {
            // 돌진: 위력에 비례하는 길이의 잔상. 던져진 쪽: 빠르게 나는 동안 잔상(시간이 흐를 때만).
            float speedNow = Mathf.Sqrt(f.Velocity.X * f.Velocity.X + f.Velocity.Y * f.Velocity.Y);
            bool thrownTrail = f.Action == ActionState.Thrown && speedNow > 8f;
            if ((f.Action == ActionState.Lunge || thrownTrail) && f.HitstopTicks == 0 && dt > 0f)
            {
                trailTimer -= dt;
                if (trailTimer <= 0f)
                {
                    float power = f.Action == ActionState.Lunge ? speedNow * FighterCombat.Aggression(f, config) : speedNow * 0.6f;
                    EmitGhost(TrailLifeBase + power * TrailLifePerPower);
                    trailTimer = TrailInterval;
                }
            }
            if (ghostEmitLeft > 0f)
            {
                ghostEmitTimer -= dt;
                if (ghostEmitTimer <= 0f)
                {
                    EmitGhost(GhostLife);
                    ghostEmitLeft -= 1f;
                    ghostEmitTimer = GhostInterval;
                }
            }
            var color = palette.FighterBody(index);
            for (int i = 0; i < GhostCount; i++)
            {
                if (!ghosts[i].enabled)
                    continue;
                ghostAge[i] += dt;
                if (ghostAge[i] >= ghostLife[i])
                {
                    ghosts[i].enabled = false;
                    continue;
                }
                color.a = GhostAlpha * (1f - ghostAge[i] / ghostLife[i]);
                ghosts[i].color = color;
            }
        }
    }
}
