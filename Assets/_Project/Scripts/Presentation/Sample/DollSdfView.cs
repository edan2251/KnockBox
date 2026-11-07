using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 외형 후보 R3(8절, S2): 부위를 거리장(SDF)으로 합쳐 한 덩어리로 그린다(셰이더 Knockbox/SdfDoll).
    /// 사용자 피드백(배 모양, 앞에 달린 어깨, 얇고 곧은 팔, 보자기 같은 다리)을 반영한 모양:
    /// 납작한 둥근 사각형 머리와 둥근 사각형 몸통(목에서만 살짝 녹음), 다리 사이 틈이 보이는 짧은 두 다리,
    /// 몸통 위쪽 가운데(옆모습 어깨 자리)에서 나오는 굵고 부드럽게 휘는 팔과 벙어리장갑 손.
    /// 외곽선은 전체 실루엣에만 있고, 앞 팔·뒤쪽 부위의 구분은 명암으로 한다. 눈은 머리에 스프링으로 매달려 출렁인다.
    /// R3 전용 어깨에서 손 목표까지 팔을 다시 푼다(공통 뼈대의 손 목표를 그대로 쓴다).
    /// </summary>
    public sealed class DollSdfView : MonoBehaviour
    {
        const int MaxParts = 16;
        const float QuadHalf = 1.6f;
        const float ArmUpper = 0.29f;
        const float ArmLower = 0.29f;
        const float EyeSpring = 220f;
        const float EyeDamping = 9f;
        const float EyeMaxOffset = 0.05f;

        enum Group { Torso = 0, Front = 1, Back = 2, Legs = 3, Head = 4 }

        Transform root;
        Material material;
        Mesh quad;
        readonly Vector4[] a = new Vector4[MaxParts];
        readonly Vector4[] r = new Vector4[MaxParts];
        int count;
        SpriteRenderer eye0, eye1, eyeInk0, eyeInk1, pupil0, pupil1;
        readonly Vector2[] eyePos = new Vector2[2];
        readonly Vector2[] eyeVel = new Vector2[2];
        bool eyesReady;
        float time;

        public bool ShaderFound { get; private set; }

        /// <summary>R3 팔로 다시 푼 앞 손 위치(인형 좌표). 주먹 판정 확인용.</summary>
        public Vector2 FrontHand { get; private set; }

        public void Setup(ShapeLibrary shapes, Color fill, Color shade, Color ink, Color eye)
        {
            root = new GameObject("R3_Root").transform;
            root.SetParent(transform, false);
            var shader = Shader.Find("Knockbox/SdfDoll");
            ShaderFound = shader != null;
            if (!ShaderFound)
            {
                Debug.LogWarning("[Knockbox] SdfDoll 셰이더를 찾지 못해 R3를 그리지 않음");
                return;
            }
            quad = new Mesh { name = "SdfQuad" };
            quad.vertices = new[]
            {
                new Vector3(-QuadHalf, -QuadHalf, 0f), new Vector3(QuadHalf, -QuadHalf, 0f),
                new Vector3(QuadHalf, QuadHalf, 0f), new Vector3(-QuadHalf, QuadHalf, 0f),
            };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.bounds = new Bounds(Vector3.zero, new Vector3(QuadHalf * 2f, QuadHalf * 2f, 1f));
            var go = new GameObject("R3_Body");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            material = new Material(shader) { name = "R3" };
            material.SetColor("_Fill", fill);
            material.SetColor("_Ink", ink);
            renderer.sharedMaterial = material;
            renderer.sortingLayerName = SortingLayers.Fighter;
            eyeInk0 = ShapeLibrary.Create(root, "EyeInk0", shapes.Circle, SortingLayers.Fighter, 4, ink);
            eyeInk1 = ShapeLibrary.Create(root, "EyeInk1", shapes.Circle, SortingLayers.Fighter, 4, ink);
            eye0 = ShapeLibrary.Create(root, "Eye0", shapes.Circle, SortingLayers.Fighter, 5, eye);
            eye1 = ShapeLibrary.Create(root, "Eye1", shapes.Circle, SortingLayers.Fighter, 5, eye);
            pupil0 = ShapeLibrary.Create(root, "Pupil0", shapes.Circle, SortingLayers.Fighter, 6, ink);
            pupil1 = ShapeLibrary.Create(root, "Pupil1", shapes.Circle, SortingLayers.Fighter, 6, ink);
        }

        void Capsule(Group g, Vector2 p, Vector2 q, float ra, float rb)
        {
            if (count >= MaxParts)
                return;
            a[count] = new Vector4(p.x, p.y, q.x, q.y);
            r[count] = new Vector4(ra, rb, (float)g, 0f);
            count++;
        }

        void RoundBox(Group g, Vector2 center, Vector2 half, float radius, float angleDegrees)
        {
            if (count >= MaxParts)
                return;
            a[count] = new Vector4(center.x, center.y, half.x, half.y);
            r[count] = new Vector4(radius, angleDegrees * Mathf.Deg2Rad, (float)g, 1f);
            count++;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad), v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad));
        }

        /// <summary>어깨 → 팔꿈치 → 손을 부드러운 곡선(2차 베지에, 팔꿈치 쪽으로 당김)으로 이어 네 마디로 그리고, 끝에 벙어리장갑 손.</summary>
        void Arm(Group g, Vector2 shoulder, Vector2 elbow, Vector2 hand)
        {
            var control = elbow * 2f - (shoulder + hand) * 0.5f;
            var previous = shoulder;
            const int segments = 4;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var point = (1f - t) * (1f - t) * shoulder + 2f * (1f - t) * t * control + t * t * hand;
                float r0 = Mathf.Lerp(0.135f, 0.11f, (i - 1) / (float)segments);
                float r1 = Mathf.Lerp(0.135f, 0.11f, t);
                Capsule(g, previous, point, r0, r1);
                previous = point;
            }
            var along = (hand - control).normalized;
            Capsule(g, hand - along * 0.02f, hand + along * 0.05f, 0.13f, 0.12f);
        }

        public void Render(in DollSkeleton k, float worldAngle, float dt)
        {
            if (!ShaderFound)
                return;
            time += dt;
            root.localRotation = Quaternion.Euler(0f, 0f, worldAngle);
            count = 0;
            float angle = k.Angle;
            var torsoCenter = k.Torso;
            var scale = k.Scale;
            Vector2 Local(Vector2 v) => torsoCenter + Rotate(new Vector2(v.x * scale.x, v.y * scale.y), angle);

            // 몸통: 아래로 퍼지지 않는 둥근 사각형. 다리가 보이게 엉덩이 아래는 비운다.
            RoundBox(Group.Torso, Local(new Vector2(0f, 0.03f)), new Vector2(0.37f * scale.x, 0.22f * scale.y), 0.17f, angle);
            // 머리: 조금 납작한 둥근 사각형, 목에서만 몸통과 살짝 녹는다.
            var head = Local(new Vector2(0.03f, 0.44f));
            RoundBox(Group.Head, head, new Vector2(0.32f, 0.21f), 0.15f, angle);
            // 다리: 짧은 두 다리와 앞으로 나온 발, 다리 사이 틈이 보이게 굵기를 제한.
            var hip0 = Local(new Vector2(0.13f, -0.12f));
            var hip1 = Local(new Vector2(-0.13f, -0.12f));
            Capsule(Group.Legs, hip0, k.Foot0 + new Vector2(0f, 0.02f), 0.12f, 0.105f);
            Capsule(Group.Legs, hip1, k.Foot1 + new Vector2(0f, 0.02f), 0.12f, 0.105f);
            Capsule(Group.Legs, k.Foot0 + new Vector2(-0.01f, 0.02f), k.Foot0 + new Vector2(0.08f, 0.02f), 0.1f, 0.09f);
            Capsule(Group.Legs, k.Foot1 + new Vector2(-0.01f, 0.02f), k.Foot1 + new Vector2(0.08f, 0.02f), 0.1f, 0.09f);

            // 팔: 사람처럼 몸통 위쪽의 어깨 자리(옆모습이라 가운데 가까이, 앞 팔이 조금 앞).
            var shoulder0 = Local(new Vector2(0.07f, 0.16f));
            var shoulder1 = Local(new Vector2(-0.07f, 0.16f));
            float bend = -Mathf.Sign(Mathf.Cos(angle * Mathf.Deg2Rad) + 0.01f);
            var elbow0 = TwoBoneIK.Solve(shoulder0, k.Hand0, ArmUpper, ArmLower, bend, out var hand0);
            var elbow1 = TwoBoneIK.Solve(shoulder1, k.Hand1, ArmUpper, ArmLower, bend, out var hand1);
            FrontHand = hand0;
            Arm(Group.Front, shoulder0, elbow0, hand0);
            Arm(Group.Back, shoulder1, elbow1, hand1);

            material.SetVectorArray("_A", a);
            material.SetVectorArray("_R", r);
            material.SetFloat("_Count", count);
            // 빛은 화면 왼쪽 위에서(인형이 돌아도 화면 기준).
            var light = Rotate(new Vector2(-0.3f, 0.95f), -worldAngle);
            material.SetVector("_Light", new Vector4(light.x, light.y, 0f, 0f));

            // 눈: 머리 앞쪽에 매달린 자리를 스프링으로 따라가며 출렁인다(젤리 느낌). 쉴 때도 살짝 떨린다.
            var forward = Rotate(Vector2.right, angle);
            var up = Rotate(Vector2.up, angle);
            var anchor0 = head + forward * 0.17f + up * 0.03f;
            var anchor1 = head + forward * 0.01f + up * 0.03f;
            Jiggle(0, anchor0, dt, 0f);
            Jiggle(1, anchor1, dt, 1.7f);
            eyesReady = true;
            Eye(eyeInk0, eye0, pupil0, 0, forward);
            Eye(eyeInk1, eye1, pupil1, 1, forward);
        }

        void Jiggle(int i, Vector2 anchor, float dt, float phase)
        {
            if (!eyesReady)
            {
                eyePos[i] = anchor;
                eyeVel[i] = Vector2.zero;
                return;
            }
            dt = Mathf.Clamp(dt, 0f, 1f / 30f);
            var idle = new Vector2(Mathf.Sin(time * 5.3f + phase), Mathf.Sin(time * 4.1f + phase * 1.3f)) * 0.004f;
            eyeVel[i] += ((anchor + idle - eyePos[i]) * EyeSpring - eyeVel[i] * EyeDamping) * dt;
            eyePos[i] += eyeVel[i] * dt;
            var offset = eyePos[i] - anchor;
            if (offset.magnitude > EyeMaxOffset)
                eyePos[i] = anchor + offset.normalized * EyeMaxOffset;
        }

        void Eye(SpriteRenderer ink, SpriteRenderer white, SpriteRenderer pupil, int i, Vector2 forward)
        {
            var at = eyePos[i];
            // 움직이는 쪽으로 살짝 늘어난다.
            float stretch = Mathf.Clamp01(eyeVel[i].magnitude * 0.25f) * 0.25f;
            float w = 0.095f * (1f - stretch * 0.5f), h = 0.115f * (1f + stretch);
            Place(ink, at, w + 0.03f, h + 0.03f);
            Place(white, at, w, h);
            Place(pupil, at + forward * 0.018f + eyeVel[i] * -0.004f, w * 0.42f, h * 0.42f);
        }

        static void Place(SpriteRenderer sr, Vector2 at, float w, float h)
        {
            sr.transform.localPosition = new Vector3(at.x, at.y, 0f);
            ShapeLibrary.SetSize(sr, w, h);
        }

        void OnDestroy()
        {
            if (quad != null)
                Destroy(quad);
            if (material != null)
                Destroy(material);
        }
    }
}
