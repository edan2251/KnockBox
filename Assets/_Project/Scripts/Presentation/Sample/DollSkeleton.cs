using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// CR-004 단계 S(8절) 외형 샘플의 공통 뼈대. 원점 = 판정 상자 중심(실험 경기 몸 0.72), +x = 바라보는 쪽.
    /// 통통한 인형 비율: 몸통 타원 0.8 × 0.56(판정 폭 0.72의 1.11배), 머리 지름 0.5, 팔 0.26 + 0.26, 다리 0.15 + 0.15.
    /// 손발 목표는 포즈가 정하고 팔꿈치·무릎은 기존 TwoBoneIK가 푼다. 두 후보(R1, R2)가 같은 뼈대를 그린다.
    /// </summary>
    public struct DollSkeleton
    {
        public const float TorsoWidth = 0.8f;
        public const float TorsoHeight = 0.56f;
        public const float HeadRadius = 0.25f;
        public const float ArmUpper = 0.26f;
        public const float ArmLower = 0.26f;
        public const float LegUpper = 0.15f;
        public const float LegLower = 0.15f;
        public const float ArmThickness = 0.2f;
        public const float LegThickness = 0.24f;
        public const float HandRadius = 0.12f;
        public const float FootWidth = 0.26f;

        public Vector2 Torso;
        public float Angle;
        public Vector2 Scale;
        public Vector2 Head;
        public Vector2 Shoulder0, Shoulder1, Elbow0, Elbow1, Hand0, Hand1;
        public Vector2 Hip0, Hip1, Knee0, Knee1, Foot0, Foot1;

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>포즈 → 관절 위치. 0번이 앞(화면 쪽, 바라보는 쪽 팔·다리), 1번이 뒤.</summary>
        public static DollSkeleton Solve(in DollPose pose)
        {
            var k = new DollSkeleton { Torso = pose.Torso, Angle = pose.Angle, Scale = pose.Scale };
            var torso = pose.Torso;
            var scale = pose.Scale;
            float angle = pose.Angle;
            Vector2 Local(Vector2 v) => torso + Rotate(new Vector2(v.x * scale.x, v.y * scale.y), angle);
            k.Head = Local(new Vector2(0.02f, 0.47f));
            k.Shoulder0 = Local(new Vector2(0.26f, 0.12f));
            k.Shoulder1 = Local(new Vector2(-0.22f, 0.12f));
            k.Hip0 = Local(new Vector2(0.14f, -0.18f));
            k.Hip1 = Local(new Vector2(-0.14f, -0.18f));
            // 팔꿈치는 아래(손이 위면 바깥), 무릎은 앞으로 나온다.
            float armBend = -Mathf.Sign(Mathf.Cos(pose.Angle * Mathf.Deg2Rad) + 0.01f);
            k.Elbow0 = TwoBoneIK.Solve(k.Shoulder0, pose.Hand0, ArmUpper, ArmLower, armBend, out k.Hand0);
            k.Elbow1 = TwoBoneIK.Solve(k.Shoulder1, pose.Hand1, ArmUpper, ArmLower, armBend, out k.Hand1);
            k.Knee0 = TwoBoneIK.Solve(k.Hip0, pose.Foot0, LegUpper, LegLower, 1f, out k.Foot0);
            k.Knee1 = TwoBoneIK.Solve(k.Hip1, pose.Foot1, LegUpper, LegLower, 1f, out k.Foot1);
            return k;
        }
    }

    /// <summary>뼈대 목표: 몸통 중심·각도·늘어남과 손발 목표(원점 = 판정 상자 중심).</summary>
    public struct DollPose
    {
        public Vector2 Torso;
        public float Angle;
        public Vector2 Scale;
        public Vector2 Hand0, Hand1, Foot0, Foot1;
        /// <summary>주먹 포즈면 조준(도)과 활성 여부(판정 원 겹쳐 그리기·주먹 끝 확인용).</summary>
        public bool Punching;
        public float AimDegrees;
        public float PunchProgress;
        /// <summary>천장 자세 등에서 그림 전체를 뒤집는 각도(도).</summary>
        public float WorldAngle;
    }

    public enum DollPoseKind
    {
        Idle,
        Run,
        PunchForward,
        PunchUp,
        PunchDown,
        PunchDiagonal,
        Ceiling,
        Lunge,
        Held,
        Thrown,
    }

    /// <summary>샘플 포즈(8절 S1: 달리기, 위·아래·대각선 주먹, 천장 자세, 박치기, 잡힘, 던져짐). 시간 t(초)에 대한 함수.</summary>
    public static class DollPoses
    {
        public const float Floor = -0.36f;
        /// <summary>주먹 한 번의 길이(초): 당기기 → 뻗기 → 유지 → 거두기.</summary>
        public const float PunchCycle = 0.7f;

        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        static DollPose Base() => new DollPose
        {
            Torso = new Vector2(0f, 0.02f),
            Scale = Vector2.one,
            Hand0 = new Vector2(0.36f, -0.12f),
            Hand1 = new Vector2(-0.3f, -0.12f),
            Foot0 = new Vector2(0.12f, Floor),
            Foot1 = new Vector2(-0.12f, Floor),
        };

        public static DollPose Evaluate(DollPoseKind kind, float t, float punchReach)
        {
            var p = Base();
            switch (kind)
            {
                case DollPoseKind.Idle:
                    p.Torso.y += Mathf.Sin(t * 3f) * 0.012f;
                    p.Scale = new Vector2(1f + Mathf.Sin(t * 3f) * 0.015f, 1f - Mathf.Sin(t * 3f) * 0.015f);
                    break;
                case DollPoseKind.Run:
                {
                    float ph = t * Mathf.PI * 2f * 2.6f;
                    p.Angle = -8f;
                    p.Torso.y += Mathf.Abs(Mathf.Sin(ph)) * 0.05f;
                    p.Foot0 = new Vector2(Mathf.Sin(ph) * 0.26f, Floor + Mathf.Max(0f, Mathf.Cos(ph)) * 0.12f);
                    p.Foot1 = new Vector2(-Mathf.Sin(ph) * 0.26f, Floor + Mathf.Max(0f, -Mathf.Cos(ph)) * 0.12f);
                    p.Hand0 = new Vector2(0.2f - Mathf.Sin(ph) * 0.2f, -0.02f);
                    p.Hand1 = new Vector2(-0.1f + Mathf.Sin(ph) * 0.2f, -0.02f);
                    break;
                }
                case DollPoseKind.PunchForward: Punch(ref p, 0f, t, punchReach); break;
                case DollPoseKind.PunchUp: Punch(ref p, 90f, t, punchReach); break;
                case DollPoseKind.PunchDown: Punch(ref p, -90f, t, punchReach); break;
                case DollPoseKind.PunchDiagonal: Punch(ref p, 45f, t, punchReach); break;
                case DollPoseKind.Ceiling:
                    // 천장에 매달린 자세: 그림 전체를 뒤집고(발이 위), 팔은 아래(뒤집힌 좌표에서 위)로 늘어뜨린다.
                    p.WorldAngle = 180f;
                    p.Torso.y += Mathf.Sin(t * 2f) * 0.01f;
                    p.Hand0 = new Vector2(0.3f, 0.45f + Mathf.Sin(t * 2f) * 0.03f);
                    p.Hand1 = new Vector2(-0.2f, 0.4f);
                    break;
                case DollPoseKind.Lunge:
                    // 박치기: 진행 방향으로 늘어나고 앞으로 숙이며, 두 팔을 앞으로 뻗고 다리는 뒤로 끌린다.
                    p.Angle = -18f;
                    p.Scale = new Vector2(1.18f, 0.86f);
                    p.Hand0 = new Vector2(0.75f, 0.12f);
                    p.Hand1 = new Vector2(0.68f, -0.02f);
                    p.Foot0 = new Vector2(-0.3f, -0.28f);
                    p.Foot1 = new Vector2(-0.42f, -0.2f);
                    break;
                case DollPoseKind.Held:
                    // 잡힘: 들려서 축 늘어짐. 팔다리가 아래로 흔들린다.
                    p.Torso = new Vector2(0f, 0.1f);
                    p.Angle = 14f + Mathf.Sin(t * 4f) * 4f;
                    p.Hand0 = new Vector2(0.24f, -0.45f + Mathf.Sin(t * 4f) * 0.03f);
                    p.Hand1 = new Vector2(-0.18f, -0.42f);
                    p.Foot0 = new Vector2(0.1f, -0.42f);
                    p.Foot1 = new Vector2(-0.08f, -0.4f);
                    break;
                case DollPoseKind.Thrown:
                    // 던져짐: 빙글빙글 돌며 팔다리를 벌린다.
                    p.WorldAngle = -t * 540f;
                    p.Hand0 = new Vector2(0.55f, 0.35f);
                    p.Hand1 = new Vector2(-0.5f, 0.35f);
                    p.Foot0 = new Vector2(0.28f, -0.32f);
                    p.Foot1 = new Vector2(-0.28f, -0.3f);
                    break;
            }
            return p;
        }

        /// <summary>주먹: 당김(30%) → 뻗음(활성, 판정 원 끝 = 조준 × reach) → 유지 → 거둠. 몸통은 조준 쪽으로 기운다.</summary>
        static void Punch(ref DollPose p, float aim, float t, float reach)
        {
            float u = Mathf.Repeat(t, PunchCycle) / PunchCycle;
            float extend = u < 0.3f ? -u / 0.3f * 0.4f : u < 0.4f ? Mathf.Lerp(-0.4f, 1f, (u - 0.3f) / 0.1f) : u < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (u - 0.7f) / 0.3f);
            p.Punching = true;
            p.AimDegrees = aim;
            p.PunchProgress = extend;
            p.Angle = Mathf.Clamp(aim, -60f, 60f) * 0.35f * Mathf.Max(0f, extend);
            var d = Dir(aim);
            var rest = new Vector2(0.3f, 0.02f);
            p.Hand0 = extend >= 0f ? Vector2.Lerp(rest, d * reach, extend) : rest - d * 0.18f * -extend;
            p.Hand1 = new Vector2(0.18f, 0.12f);
            if (aim < -45f)
                p.Foot0 = new Vector2(0.18f, Floor);
        }
    }
}
