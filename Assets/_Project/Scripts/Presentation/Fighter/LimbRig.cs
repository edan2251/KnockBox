using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 팔다리(05-presentation.md 2절). 2마디 IK로 그린다.
    /// 다리: 지상에서는 발을 지면에 고정하고, 이상 위치에서 0.35 넘게 벗어나면 5프레임 동안 0.15 높이 호로 한 발씩 내딛는다.
    /// 공중에서는 몸 쪽으로 접고 속도 반대로 끌린다. 벽 미끄러짐에서는 벽에 댄다.
    /// 팔: 대기 때 살짝 흔들리고, 달릴 때 다리와 반대로 흔든다.
    /// 발 위치는 시뮬레이션의 지형 상자로 직접 계산한다.
    /// </summary>
    public sealed class LimbRig : MonoBehaviour
    {
        /// <summary>손 목표를 직접 정할 때 팔을 그릴 층.</summary>
        public enum ArmLayers
        {
            /// <summary>앞팔은 몸 앞, 뒷팔은 몸 뒤.</summary>
            Natural,
            /// <summary>두 팔 모두 몸 앞(가드 때 교차).</summary>
            BothFront,
            /// <summary>두 팔 모두 몸 뒤(충전 때 젖힌 팔이 얼굴을 가로지르지 않게).</summary>
            BothBack,
        }

        public const float LegUpper = 0.3f;
        public const float LegLower = 0.3f;
        public const float StepTrigger = 0.35f;
        public const float StepDuration = 5f / 60f;
        public const float StepHeight = 0.15f;
        const float LegThickness = 0.12f;
        const float FootSize = 0.17f;
        const float ArmUpper = 0.28f;
        const float ArmLower = 0.28f;
        const float ArmThickness = 0.11f;
        const float HandSize = 0.15f;
        const float StanceHalf = 0.16f;
        const float AirFollowRate = 25f;
        /// <summary>움직이는 중 뒤처진 발을 드는 거리. 0.35보다 작게 해서 두 발이 번갈아 딛게 한다.</summary>
        const float TrailingTrigger = 0.12f;
        /// <summary>다리 길이의 이 비율을 넘으면 디딘 발을 바로 든다.</summary>
        const float OverReach = 0.96f;

        [System.Serializable] struct Limb
        {
            public SpriteRenderer Upper;
            public SpriteRenderer Lower;
            public SpriteRenderer End;
        }

        [SerializeField] Limb[] legs = new Limb[2];
        [SerializeField] Limb[] arms = new Limb[2];
        readonly Vector2[] feet = new Vector2[2];
        readonly Vector2[] drawnFeet = new Vector2[2];
        readonly Vector2[] drawnKnees = new Vector2[2];
        readonly Vector2[] hands = new Vector2[2];
        readonly Vector2[] drawnHands = new Vector2[2];
        readonly Vector2[] swingFrom = new Vector2[2];
        readonly Vector2[] swingTo = new Vector2[2];
        readonly float[] swingT = { -1f, -1f };
        readonly float[] plantedGround = new float[2];
        bool planted;
        float runPhase;
        float idleTime;
        bool handOverride;
        ArmLayers armLayers;
        Vector2 overrideFront;
        Vector2 overrideBack;

        /// <summary>IK 목표점(발 중심).</summary>
        public Vector2 FootPosition(int side) => feet[side];
        /// <summary>실제로 그린 발 중심. 목표에 닿지 못하면 목표와 다르다.</summary>
        public Vector2 DrawnFoot(int side) => drawnFeet[side];
        public Vector2 DrawnKnee(int side) => drawnKnees[side];
        public float LegRadius => LegThickness * 0.5f;
        public bool FootPlanted(int side) => planted && swingT[side] < 0f;
        /// <summary>디딘 발이 서 있는 지면 높이.</summary>
        public float FootGroundY(int side) => plantedGround[side];
        public float FootRadius => FootSize * 0.5f;

        public void Setup(ShapeLibrary shapes, Color ink)
        {
            for (int i = 0; i < 2; i++)
            {
                if(legs[i].Upper == null) legs[i] = CreateLimb(shapes, ink, $"Leg{i}", FootSize);
                if(arms[i].Upper == null) arms[i] = CreateLimb(shapes, ink, $"Arm{i}", HandSize);
            }
        }

        Limb CreateLimb(ShapeLibrary shapes, Color ink, string name, float endSize)
        {
            var limb = new Limb
            {
                Upper = ShapeLibrary.CreateSliced(transform, name + "Upper", shapes.LimbCapsule, SortingLayers.LimbBack, 0, ink),
                Lower = ShapeLibrary.CreateSliced(transform, name + "Lower", shapes.LimbCapsule, SortingLayers.LimbBack, 0, ink),
                End = ShapeLibrary.Create(transform, name + "End", shapes.Circle, SortingLayers.LimbBack, 1, ink),
            };
            ShapeLibrary.SetSize(limb.End, endSize, endSize);
            return limb;
        }

        /// <summary>순간 이동(라운드 시작, 시나리오 시작) 뒤에는 발을 새로 디딘다.</summary>
        public void ResetPose()
        {
            planted = false;
            handOverride = false;
        }

        /// <summary>이번 프레임의 손 목표를 직접 정한다(공격, 가드, 충전, 돌진). front는 바라보는 쪽 팔.</summary>
        public void SetHands(Vector2 front, Vector2 back, ArmLayers layers)
        {
            handOverride = true;
            overrideFront = front;
            overrideBack = back;
            armLayers = layers;
        }

        public void ClearHands() => handOverride = false;

        public Vector2 Hand(int side) => hands[side];

        /// <summary>실제로 그린 손 중심(팔 길이가 모자라면 목표와 다르다). 무기 손잡이 위치로 쓴다.</summary>
        public Vector2 DrawnHand(int side) => drawnHands[side];

        /// <param name="hips">왼쪽(0)·오른쪽(1) 엉덩이 월드 위치.</param>
        /// <param name="shoulders">왼쪽(0)·오른쪽(1) 어깨 월드 위치.</param>
        public void UpdateRig(Vector2 hip0, Vector2 hip1, Vector2 shoulder0, Vector2 shoulder1, Vector2 center, in FighterState f, ArenaDef arena, float halfBody, float dt)
        {
            int facing = f.Facing >= 0 ? 1 : -1;
            float vx = f.Velocity.X;
            float vy = f.Velocity.Y;
            float footY = center.y - halfBody;
            idleTime += dt;

            if (f.Grounded)
                UpdateGroundFeet(center, hip0, hip1, footY, vx, arena, dt);
            else
                UpdateAirFeet(hip0, hip1, f, arena, dt);

            UpdateHands(shoulder0, shoulder1, f, facing, dt);

            // 앞쪽(바라보는 쪽) 팔다리는 몸 앞에, 뒤쪽은 몸 뒤에 그린다.
            int front = facing > 0 ? 1 : 0;
            // 벽 미끄러짐과 대시 중에는 다리를 몸 뒤로 보내 몸 아래·옆으로 나온 부분만 보이게 한다.
            bool legsBehind = f.Action == ActionState.WallSlide || f.Action == ActionState.Dash;
            for (int side = 0; side < 2; side++)
            {
                string layer = side == front ? SortingLayers.LimbFront : SortingLayers.LimbBack;
                Vector2 hip = side == 0 ? hip0 : hip1;
                Vector2 shoulder = side == 0 ? shoulder0 : shoulder1;
                // 무릎은 앞으로, 팔꿈치는 몸 바깥쪽으로 굽는다(몸 앞을 가로지르지 않게).
                var knee = TwoBoneIK.Solve(hip, feet[side], LegUpper, LegLower, facing, out var foot);
                // 발이 엉덩이보다 한참 뒤에 있으면 앞으로 굽힌 무릎이 아래로 꺾여 지면을 뚫는다. 그때는 반대로 굽힌다.
                if (GroundQuery.SurfaceBelow(arena, knee.x, hip.y, out float kneeGround) && knee.y < kneeGround + LegThickness * 0.5f)
                {
                    var other = TwoBoneIK.Solve(hip, feet[side], LegUpper, LegLower, -facing, out _);
                    if (other.y > knee.y)
                        knee = other;
                }
                drawnFeet[side] = foot;
                drawnKnees[side] = knee;
                Draw(legs[side], hip, knee, foot, LegThickness, legsBehind ? SortingLayers.LimbBack : layer);
                var elbow = TwoBoneIK.Solve(shoulder, hands[side], ArmUpper, ArmLower, side == 0 ? -1f : 1f, out var hand);
                drawnHands[side] = hand;
                string armLayer = !handOverride || armLayers == ArmLayers.Natural ? layer
                    : armLayers == ArmLayers.BothFront ? SortingLayers.LimbFront : SortingLayers.LimbBack;
                Draw(arms[side], shoulder, elbow, hand, ArmThickness, armLayer);
            }
        }

        void UpdateGroundFeet(Vector2 center, Vector2 hip0, Vector2 hip1, float footY, float vx, ArenaDef arena, float dt)
        {
            float lead = Mathf.Clamp(vx * 0.04f, -0.38f, 0.38f);
            if (!planted)
            {
                for (int side = 0; side < 2; side++)
                {
                    float x = center.x + (side == 0 ? -StanceHalf : StanceHalf);
                    plantedGround[side] = Ground(arena, x, footY);
                    feet[side] = new Vector2(x, plantedGround[side] + FootRadius);
                    swingT[side] = -1f;
                }
                planted = true;
                return;
            }

            for (int side = 0; side < 2; side++)
            {
                if (swingT[side] < 0f)
                    continue;
                swingT[side] += dt / StepDuration;
                float t = Mathf.Clamp01(swingT[side]);
                var p = Vector2.Lerp(swingFrom[side], swingTo[side], t);
                p.y += Mathf.Sin(t * Mathf.PI) * StepHeight;
                feet[side] = p;
                if (swingT[side] >= 1f)
                {
                    swingT[side] = -1f;
                    feet[side] = swingTo[side];
                    plantedGround[side] = swingTo[side].y - FootRadius;
                }
            }

            // 디딘 발이 다리 길이 끝에 닿으면 다른 발이 내딛는 중이어도 바로 든다(그대로 두면 IK가 발을 지면에서 띄운다).
            float reach = (LegUpper + LegLower) * OverReach;
            for (int side = 0; side < 2; side++)
            {
                Vector2 hip = side == 0 ? hip0 : hip1;
                if (swingT[side] < 0f && (feet[side] - hip).magnitude > reach)
                    StartStep(side, center, footY, vx, lead, arena);
            }

            // 평소에는 한 번에 한 발만, 이상 위치에서 가장 많이 벗어난 발부터 내딛는다.
            // 움직이는 중에는 뒤처진 발을 일찍 들어 두 발이 번갈아 딛게 한다.
            if (swingT[0] >= 0f || swingT[1] >= 0f)
                return;
            bool moving = Mathf.Abs(vx) > 1f;
            int worst = -1;
            float worstExcess = 0f;
            for (int side = 0; side < 2; side++)
            {
                float idealX = center.x + (side == 0 ? -StanceHalf : StanceHalf);
                float deviation = feet[side].x - idealX;
                bool behind = moving && Mathf.Sign(deviation) == -Mathf.Sign(vx);
                float excess = Mathf.Abs(deviation) - (behind ? TrailingTrigger : StepTrigger);
                if (excess > worstExcess)
                {
                    worstExcess = excess;
                    worst = side;
                }
            }
            if (worst >= 0)
                StartStep(worst, center, footY, vx, lead, arena);
        }

        void StartStep(int side, Vector2 center, float footY, float vx, float lead, ArenaDef arena)
        {
            // 내딛는 5프레임 동안 몸이 갈 거리만큼 앞에 딛어야 빨리 달릴 때도 발이 다리 길이 안에 남는다.
            float travel = vx * StepDuration;
            float targetX = center.x + (side == 0 ? -StanceHalf : StanceHalf) + travel + lead;
            swingFrom[side] = feet[side];
            // 발 원의 아래쪽이 지면에 닿게 중심을 반지름만큼 올린다.
            swingTo[side] = new Vector2(targetX, Ground(arena, targetX, footY) + FootRadius);
            swingT[side] = 0f;
        }

        static float Ground(ArenaDef arena, float x, float footY) =>
            GroundQuery.SurfaceBelow(arena, x, footY + 0.05f, out float y) && y > footY - 0.6f ? y : footY;

        void UpdateAirFeet(Vector2 hip0, Vector2 hip1, in FighterState f, ArenaDef arena, float dt)
        {
            planted = false;
            float follow = 1f - Mathf.Exp(-AirFollowRate * dt);
            for (int side = 0; side < 2; side++)
            {
                Vector2 hip = side == 0 ? hip0 : hip1;
                Vector2 target;
                if (f.Action == ActionState.WallSlide && f.WallSide != 0
                    && GroundQuery.WallSurface(arena, hip.x, hip.y, f.WallSide, out float wallX))
                {
                    target = new Vector2(wallX - f.WallSide * 0.02f, hip.y - 0.18f + side * 0.12f);
                }
                else
                {
                    // 몸 쪽으로 접고 속도 반대로 끌린다.
                    float dragX = Mathf.Clamp(-f.Velocity.X * 0.015f, -0.22f, 0.22f);
                    float dragY = Mathf.Clamp(-f.Velocity.Y * 0.004f, -0.06f, 0.1f);
                    target = hip + new Vector2(dragX + (side == 0 ? -0.04f : 0.04f), -0.3f + dragY);
                }
                feet[side] = Vector2.Lerp(feet[side], target, follow);
            }
        }

        void UpdateHands(Vector2 shoulder0, Vector2 shoulder1, in FighterState f, int facing, float dt)
        {
            float speed = Mathf.Abs(f.Velocity.X);
            runPhase += speed * dt * 2.2f;
            int front = facing > 0 ? 1 : 0;
            if (handOverride)
            {
                hands[front] = overrideFront;
                hands[1 - front] = overrideBack;
                return;
            }
            for (int side = 0; side < 2; side++)
            {
                Vector2 shoulder = side == 0 ? shoulder0 : shoulder1;
                float outward = side == 0 ? -1f : 1f;
                Vector2 offset;
                if (f.Action == ActionState.Dash)
                {
                    // 뒷팔은 진행 반대로 끌리고, 앞팔은 몸 옆에 붙인다(몸 앞을 가로지르지 않게).
                    offset = side == front
                        ? new Vector2(outward * 0.14f, -0.3f)
                        : new Vector2(-f.DashDirection.X * 0.4f + outward * 0.05f, -0.06f - f.DashDirection.Y * 0.2f);
                }
                else if (f.Action == ActionState.WallSlide)
                {
                    offset = new Vector2(f.WallSide * 0.36f, 0.12f + side * 0.08f);
                }
                else if (!f.Grounded)
                {
                    offset = new Vector2(outward * 0.3f, -0.1f + Mathf.Clamp(-f.Velocity.Y * 0.01f, -0.15f, 0.2f));
                }
                else if (speed > 0.5f)
                {
                    // 앞팔과 뒷팔이 반대 위상으로 흔든다. 손은 몸 바깥쪽에 둔다.
                    float swing = Mathf.Sin(runPhase + side * Mathf.PI);
                    offset = new Vector2(outward * 0.16f + facing * swing * 0.16f, -0.34f + Mathf.Abs(swing) * 0.06f);
                }
                else
                {
                    offset = new Vector2(outward * 0.16f, -0.38f + Mathf.Sin(idleTime * 2.4f + side * 1.3f) * 0.025f);
                }
                hands[side] = shoulder + offset;
            }
        }

        static void Draw(in Limb limb, Vector2 root, Vector2 joint, Vector2 end, float thickness, string layer)
        {
            Place(limb.Upper, root, joint, thickness, layer);
            Place(limb.Lower, joint, end, thickness, layer);
            limb.End.transform.position = new Vector3(end.x, end.y, 0f);
            limb.End.sortingLayerName = layer;
        }

        static void Place(SpriteRenderer sr, Vector2 a, Vector2 b, float thickness, string layer)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            sr.transform.position = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.size = new Vector2(length + thickness, thickness);
            sr.sortingLayerName = layer;
        }
    }
}
