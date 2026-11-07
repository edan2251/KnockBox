using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 3D 외형(CR-004 시험): 외형 프리팹 루트에 붙어 시뮬레이션 상태(위치·속도·바라보는 쪽·주먹 조준)를 읽어 모델을 보여 주기만 한다.
    /// 위치·판정·입력은 시뮬레이션이 정하고, 모델은 Root Motion이나 3D 물리를 쓰지 않는다. 모델마다 다른 것(뼈 연결, 크기·위치·방향 보정,
    /// 동작 세기)은 모두 이 Inspector 칸에 있어 모델을 바꿔도 전투 코드는 그대로다. 애니메이션 클립 없이 뼈를 절차적으로 돌린다:
    /// 쉬는 자세(팔 내리기), 달리기 다리·팔 흔들기, 공중 다리 굽히기, 몸 좌우 방향 전환(짧게 따라옴), 커서 방향 주먹(짧게 따라옴).
    /// </summary>
    public sealed class FighterModelRig : MonoBehaviour
    {
        [Header("모델")]
        [Tooltip("가져온 모델 인스턴스(크기 보정이 걸린 자식)")]
        public Transform model;
        [Tooltip("모델의 앞쪽 방향(이 루트 기준, 회전 0일 때). 만들 때 발 → 발끝 방향으로 잰다")]
        public Vector3 modelForward = Vector3.forward;

        [Header("뼈(모델마다 연결)")]
        public Transform chest;
        public Transform leftUpperArm, leftLowerArm, leftHand;
        public Transform rightUpperArm, rightLowerArm, rightHand;
        public Transform leftUpperLeg, leftLowerLeg;
        public Transform rightUpperLeg, rightLowerLeg;

        [Header("배치 보정")]
        [Tooltip("판정 상자 바닥 가운데에서 모델 발까지의 오프셋")]
        public Vector2 offset;
        [Tooltip("화면 깊이(+는 뒤). 다른 그림보다 뒤에 깊이를 써서 효과가 가려지지 않게")]
        public float depth = 1f;
        [Tooltip("오른쪽을 볼 때 앞쪽이 화면 쪽으로 기우는 각(도). 0이면 정확히 옆모습")]
        public float towardCamera = 25f;
        [Tooltip("방향 전환이 따라오는 시간(초)")]
        public float turnTime = 0.07f;

        [Header("동작")]
        [Tooltip("쉬는 자세에서 팔을 수직 아래에서 바깥으로 벌리는 각(도)")]
        public float armSpread = 12f;
        public float legSwing = 32f;
        public float armSwing = 24f;
        [Tooltip("최대 달리기 속도에서 한 걸음 주기(초)")]
        public float stridePeriod = 0.36f;
        public float airLegBend = 25f;
        [Tooltip("주먹 팔이 조준을 따라가는 시간(초)")]
        public float punchFollow = 0.04f;
        public float punchLean = 12f;

        Transform[] bones;
        Quaternion[] rest;
        float yaw;
        bool placed;
        float phase;
        float punchWeight;
        Vector3 punchDir = Vector3.right;

        void Awake()
        {
            bones = new[] { chest, leftUpperArm, leftLowerArm, rightUpperArm, rightLowerArm, leftUpperLeg, leftLowerLeg, rightUpperLeg, rightLowerLeg };
            rest = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                rest[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
        }

        /// <summary>모델 앞쪽이 +x(오른쪽)를 보게 하는 Y 회전(도), 화면 쪽으로 기울임 포함.</summary>
        float YawFor(int facing)
        {
            var f = new Vector3(modelForward.x, 0f, modelForward.z);
            float current = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            // 화면 오른쪽(+x)은 Y 회전 90°, 카메라 쪽(−z)으로 기울이면 더 큰 각.
            float target = facing >= 0 ? 90f + towardCamera : -90f - towardCamera;
            return target - current;
        }

        public void Render(in FighterState previous, in FighterState current, float alpha, float dt, SimConfig config)
        {
            if (bones == null)
                Awake();
            var p = Vector2.LerpUnclamped(new Vector2(previous.Position.X, previous.Position.Y), new Vector2(current.Position.X, current.Position.Y), alpha);
            transform.position = new Vector3(p.x + offset.x, p.y - config.HalfBody + offset.y, depth);

            // 바라보는 쪽: 시뮬레이션은 즉시 바뀌고 모델은 짧게 따라 돈다.
            float targetYaw = YawFor(current.Facing);
            yaw = placed ? Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-dt / Mathf.Max(turnTime, 1e-3f))) : targetYaw;
            placed = true;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    bones[i].localRotation = rest[i];
            }
            var forward = transform.TransformDirection(modelForward);
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.right;
            var lateral = Vector3.Cross(Vector3.up, forward);

            // 쉬는 자세: T자세 팔을 아래로 내린다(조금 바깥으로).
            RestArm(leftUpperArm, leftLowerArm, lateral);
            RestArm(rightUpperArm, rightLowerArm, lateral);

            // 달리기·공중.
            float maxRun = Mathf.Max(config.Movement.MaxRunSpeed, 1f);
            float speed = Mathf.Abs(current.Velocity.X);
            bool grounded = current.Grounded;
            float run = grounded ? Mathf.Clamp01(speed / maxRun) : 0f;
            phase += dt * run * Mathf.PI * 2f / Mathf.Max(stridePeriod, 0.05f);
            float swing = Mathf.Sin(phase) * run;
            Swing(leftUpperLeg, lateral, swing * legSwing);
            Swing(rightUpperLeg, lateral, -swing * legSwing);
            Swing(leftUpperArm, lateral, -swing * armSwing);
            Swing(rightUpperArm, lateral, swing * armSwing);
            if (!grounded)
            {
                Swing(leftUpperLeg, lateral, airLegBend);
                Swing(rightUpperLeg, lateral, airLegBend * 0.6f);
                Swing(leftLowerLeg, lateral, -airLegBend * 1.4f);
                Swing(rightLowerLeg, lateral, -airLegBend);
            }

            // 커서 방향 주먹: 시뮬레이션의 주먹 진행(선딜 → 활성 → 후딜)과 조준 방향(AttackDir)을 짧게 따라간다.
            float targetWeight = 0f;
            if (current.Action == ActionState.Attack && current.AttackId == AttackIds.Punch)
            {
                ref readonly var punch = ref config.GetAttack(current.AttackId);
                int k = current.ActionTick;
                int s = Mathf.Max(punch.StartupTicks, 1);
                targetWeight = k < s ? 0.35f + 0.65f * k / s
                    : k < s + punch.ActiveTicks ? 1f
                    : Mathf.Clamp01(1f - (k - s - punch.ActiveTicks) / (float)Mathf.Max(punch.RecoveryTicks, 1));
                var aim = new Vector3(current.AttackDir.X, current.AttackDir.Y, 0f);
                if (aim.sqrMagnitude > 1e-6f)
                    punchDir = aim.normalized;
            }
            float follow = 1f - Mathf.Exp(-dt / Mathf.Max(punchFollow, 1e-3f));
            punchWeight = Mathf.Lerp(punchWeight, targetWeight, follow);
            if (punchWeight > 0.01f)
            {
                // 화면 쪽(카메라에 가까운) 팔로 친다.
                bool rightNear = rightUpperArm != null && leftUpperArm != null && rightUpperArm.position.z < leftUpperArm.position.z;
                var upper = rightNear ? rightUpperArm : leftUpperArm;
                var lower = rightNear ? rightLowerArm : leftLowerArm;
                var hand = rightNear ? rightHand : leftHand;
                if (chest != null)
                {
                    float lean = Vector3.Dot(punchDir, Vector3.up) * punchLean * punchWeight;
                    chest.rotation = Quaternion.AngleAxis(-lean, lateral) * chest.rotation;
                }
                Point(upper, lower, punchDir, punchWeight);
                Point(lower, hand, punchDir, punchWeight);
            }
        }

        void RestArm(Transform upper, Transform lower, Vector3 lateral)
        {
            if (upper == null || lower == null || chest == null)
                return;
            float side = Mathf.Sign(Vector3.Dot(upper.position - chest.position, lateral));
            var target = Quaternion.AngleAxis(side * armSpread, Vector3.Cross(lateral, Vector3.up)) * Vector3.down;
            Point(upper, lower, target, 1f);
        }

        /// <summary>bone에서 child로 가는 방향을 dir 쪽으로 weight만큼 돌린다.</summary>
        static void Point(Transform bone, Transform child, Vector3 dir, float weight)
        {
            if (bone == null || child == null)
                return;
            var from = child.position - bone.position;
            if (from.sqrMagnitude < 1e-8f)
                return;
            var full = Quaternion.FromToRotation(from, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, full, weight) * bone.rotation;
        }

        static void Swing(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && Mathf.Abs(degrees) > 1e-3f)
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }
}
