using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 외형 후보 R1(8절): 부위 메시 + 관절 구. 머리·몸통은 둥근 덩어리, 팔다리는 굵은 캡슐 마디, 어깨·팔꿈치·엉덩이·무릎에 둥근 관절 덩어리,
    /// 뭉툭한 손발. 모든 부위를 잉크 외곽선 한 겹 위에 몸 색으로 겹쳐 그려, 팔이 몸통 앞을 지나도 테두리로 구분된다(뒤 팔다리는 몸통 뒤 층).
    /// 코드로만 만든다(스프라이트는 기존 도형).
    /// </summary>
    public sealed class DollPartsView : MonoBehaviour
    {
        const float Outline = 0.05f;

        sealed class Part
        {
            public SpriteRenderer Ink;
            public SpriteRenderer Fill;
        }

        Part torso, head, eye0, eye1;
        readonly Part[] arm = new Part[4];
        readonly Part[] leg = new Part[4];
        readonly Part[] joints = new Part[8];
        readonly Part[] hands = new Part[2];
        readonly Part[] feet = new Part[2];
        Transform root;

        public void Setup(ShapeLibrary shapes, Color body, Color shade, Color ink)
        {
            root = new GameObject("R1_Root").transform;
            root.SetParent(transform, false);
            Part Make(string name, Sprite sprite, string layer, int order, Color fill)
            {
                return new Part
                {
                    Ink = ShapeLibrary.Create(root, name + " Ink", sprite, layer, order, ink),
                    Fill = ShapeLibrary.Create(root, name, sprite, layer, order + 1, fill),
                };
            }
            // 층: 뒤 팔다리(LimbBack) < 몸통·머리(Fighter) < 앞 팔다리(LimbFront).
            for (int i = 0; i < 2; i++)
            {
                bool front = i == 0;
                string layer = front ? SortingLayers.LimbFront : SortingLayers.LimbBack;
                var fill = front ? body : shade;
                // 정렬 순서는 부위마다 2씩(외곽선 n, 채움 n + 1): 다리(0~9) < 팔(10~19).
                leg[i * 2] = Make($"Leg{i} Upper", shapes.Circle, layer, 0, fill);
                joints[i * 4 + 2] = Make($"Hip{i}", shapes.Circle, layer, 2, fill);
                leg[i * 2 + 1] = Make($"Leg{i} Lower", shapes.Circle, layer, 4, fill);
                joints[i * 4 + 3] = Make($"Knee{i}", shapes.Circle, layer, 6, fill);
                feet[i] = Make($"Foot{i}", shapes.Circle, layer, 8, fill);
                arm[i * 2] = Make($"Arm{i} Upper", shapes.Circle, layer, 10, fill);
                joints[i * 4 + 0] = Make($"Shoulder{i}", shapes.Circle, layer, 12, fill);
                arm[i * 2 + 1] = Make($"Arm{i} Lower", shapes.Circle, layer, 14, fill);
                joints[i * 4 + 1] = Make($"Elbow{i}", shapes.Circle, layer, 16, fill);
                hands[i] = Make($"Hand{i}", shapes.Circle, layer, 18, fill);
            }
            torso = Make("Torso", shapes.Circle, SortingLayers.Fighter, 0, body);
            head = Make("Head", shapes.Circle, SortingLayers.Fighter, 2, body);
            eye0 = Make("Eye0", shapes.Circle, SortingLayers.Fighter, 6, ink);
            eye1 = Make("Eye1", shapes.Circle, SortingLayers.Fighter, 6, ink);
        }

        public void Render(in DollSkeleton k, float worldAngle)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, worldAngle);
            Ellipse(torso, k.Torso, DollSkeleton.TorsoWidth * k.Scale.x, DollSkeleton.TorsoHeight * k.Scale.y, k.Angle);
            Ellipse(head, k.Head, DollSkeleton.HeadRadius * 2f, DollSkeleton.HeadRadius * 2f, k.Angle);
            var eyeDir = Quaternion.Euler(0f, 0f, k.Angle) * new Vector3(1f, 0f, 0f);
            var eyeUp = Quaternion.Euler(0f, 0f, k.Angle) * new Vector3(0f, 1f, 0f);
            Dot(eye0, k.Head + (Vector2)(eyeDir * 0.12f + eyeUp * 0.04f), 0.07f);
            Dot(eye1, k.Head + (Vector2)(eyeDir * 0.0f + eyeUp * 0.04f), 0.07f);
            for (int i = 0; i < 2; i++)
            {
                var s = i == 0 ? k.Shoulder0 : k.Shoulder1;
                var e = i == 0 ? k.Elbow0 : k.Elbow1;
                var h = i == 0 ? k.Hand0 : k.Hand1;
                var hip = i == 0 ? k.Hip0 : k.Hip1;
                var kn = i == 0 ? k.Knee0 : k.Knee1;
                var f = i == 0 ? k.Foot0 : k.Foot1;
                Segment(arm[i * 2], s, e, DollSkeleton.ArmThickness);
                Segment(arm[i * 2 + 1], e, h, DollSkeleton.ArmThickness * 0.9f);
                Segment(leg[i * 2], hip, kn, DollSkeleton.LegThickness);
                Segment(leg[i * 2 + 1], kn, f, DollSkeleton.LegThickness * 0.92f);
                Dot(joints[i * 4 + 0], s, DollSkeleton.ArmThickness * 1.15f);
                Dot(joints[i * 4 + 1], e, DollSkeleton.ArmThickness * 1.05f);
                Dot(joints[i * 4 + 2], hip, DollSkeleton.LegThickness * 1.1f);
                Dot(joints[i * 4 + 3], kn, DollSkeleton.LegThickness * 1.05f);
                Dot(hands[i], h, DollSkeleton.HandRadius * 2f);
                Ellipse(feet[i], f + new Vector2(0.05f, 0.02f), DollSkeleton.FootWidth, DollSkeleton.LegThickness * 0.95f, k.Angle * 0.3f);
            }
        }

        static void Ellipse(Part p, Vector2 center, float w, float h, float angle)
        {
            Place(p.Ink, center, w + Outline * 2f, h + Outline * 2f, angle);
            Place(p.Fill, center, w, h, angle);
        }

        static void Dot(Part p, Vector2 center, float size) => Ellipse(p, center, size, size, 0f);

        /// <summary>a에서 b까지 굵기 w의 둥근 마디(늘린 원).</summary>
        static void Segment(Part p, Vector2 a, Vector2 b, float w)
        {
            var d = b - a;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Ellipse(p, (a + b) * 0.5f, d.magnitude + w, w, angle);
        }

        static void Place(SpriteRenderer sr, Vector2 center, float w, float h, float angle)
        {
            sr.transform.localPosition = new Vector3(center.x, center.y, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            ShapeLibrary.SetSize(sr, w, h);
        }
    }
}
