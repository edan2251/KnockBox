using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knockbox.Game
{
    /// <summary>
    /// CR-004 단계 S(8절) 캐릭터 외형 샘플 씬: R1(부위 메시 + 관절 구), R2(IK 체인 튜브 + 스프링 몸통), R3(SDF로 녹여 합친 콩 모양, S2)를
    /// 나란히 두고 같은 포즈를 보여 준다.
    /// 포즈는 화면 왼쪽 버튼이나 숫자키(1~0), 판정 오버레이(실험 경기 판정 상자 0.72와 주먹 판정 원)는 O 키나 버튼으로 켜고 끈다.
    /// 오버레이를 켜면 몸통 폭 비율(판정 폭의 1~1.25배)과 주먹을 뻗은 순간 주먹 끝이 판정 원 안에 있는지를 함께 표시한다.
    /// 시뮬레이션과 무관한 연출 샘플이다.
    /// </summary>
    public sealed class CharacterSampleScene : MonoBehaviour
    {
        [SerializeField] PaletteAsset palette;
        [SerializeField] ShapeLibrary shapes;
        [SerializeField] float spacing = 2.3f;
        const int Count = 3;

        static readonly DollPoseKind[] Kinds = (DollPoseKind[])System.Enum.GetValues(typeof(DollPoseKind));

        DollPoseKind kind = DollPoseKind.Idle;
        float time;
        bool overlay = true;
        DollPartsView r1;
        DollTubeView r2;
        DollSdfView r3;
        readonly Transform[] roots = new Transform[Count];
        readonly SpriteRenderer[] grounds = new SpriteRenderer[Count];
        readonly SpriteRenderer[][] boxLines = new SpriteRenderer[Count][];
        readonly SpriteRenderer[][] rings = new SpriteRenderer[Count][];
        readonly TextMesh[] status = new TextMesh[Count];
        float half;
        AttackDef punch;
        string fistResult = "-";
        bool fistOk;

        void Awake()
        {
            if (palette == null)
                palette = ScriptableObject.CreateInstance<PaletteAsset>();
            if (shapes == null)
                shapes = ScriptableObject.CreateInstance<ShapeLibrary>();
            shapes.Activate();
            half = BrawlParams.Movement(MovementParams.Default).BodySize * 0.5f;
            punch = BrawlParams.Combat(CombatParams.Default).Body.Punch;
            var cam = Camera.main;
            if (cam != null)
                cam.backgroundColor = palette.bgBase;

            string[] titles = { "R1  parts + joint balls", "R2  IK tubes + spring body", "R3  SDF smooth blend" };
            for (int i = 0; i < Count; i++)
            {
                var root = new GameObject("R" + (i + 1)).transform;
                root.SetParent(transform, false);
                root.localPosition = new Vector3((i - (Count - 1) * 0.5f) * spacing, 0f, 0f);
                roots[i] = root;
                grounds[i] = ShapeLibrary.Create(root, "Ground", shapes.Square, SortingLayers.Terrain, 0, palette.Theme(2).terrain);
                Label(root, titles[i], new Vector2(0f, 1.25f), 0.06f, palette.flash);
                status[i] = Label(root, "", new Vector2(0f, -0.95f), 0.045f, palette.uiSub);
                boxLines[i] = new SpriteRenderer[4];
                for (int l = 0; l < 4; l++)
                    boxLines[i][l] = ShapeLibrary.Create(root, "HitboxLine", shapes.Square, SortingLayers.UI, 50, palette.flash);
                rings[i] = new SpriteRenderer[2];
                for (int r = 0; r < 2; r++)
                    rings[i][r] = ShapeLibrary.Create(root, "PunchCircle", shapes.Ring, SortingLayers.UI, 51, palette.hit);
            }
            r1 = new GameObject("R1_Doll").AddComponent<DollPartsView>();
            r1.transform.SetParent(roots[0], false);
            r1.Setup(shapes, palette.p1Body, palette.p1Shade, palette.ink);
            r2 = new GameObject("R2_Doll").AddComponent<DollTubeView>();
            r2.transform.SetParent(roots[1], false);
            r2.Setup(palette.p1Body, palette.p1Shade, palette.ink);
            r3 = new GameObject("R3_Doll").AddComponent<DollSdfView>();
            r3.transform.SetParent(roots[2], false);
            r3.Setup(shapes, palette.p1Body, palette.p1Shade, palette.ink, palette.flash);
        }

        /// <summary>R3는 어깨 위치가 달라 팔을 따로 푼다. 주먹을 끝까지 뻗은 순간 그 손이 판정 원 경로 안인지.</summary>
        public bool R3FistChecked { get; private set; }
        public bool R3FistOk { get; private set; }

        void CheckR3Fist(in DollPose pose)
        {
            if (!pose.Punching || pose.PunchProgress < 0.99f)
                return;
            R3FistChecked = true;
            R3FistOk = InsidePath(r3.FrontHand, punch.Circle0, pose.AimDegrees) || InsidePath(r3.FrontHand, punch.Circle1, pose.AimDegrees);
        }

        /// <summary>R3 셰이더를 찾았는지(빌드·테스트 확인용).</summary>
        public bool SdfReady => r3 != null && r3.ShaderFound;

        /// <summary>i번째 인형(0 = R1)의 판정 상자 중심 월드 위치.</summary>
        public Vector3 DollPosition(int i) => roots[i].position;

        TextMesh Label(Transform parent, string text, Vector2 at, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            var t = go.AddComponent<TextMesh>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = t.font.material;
            mr.sortingLayerName = SortingLayers.UI;
            mr.sortingOrder = 60;
            t.fontSize = 64;
            t.characterSize = size;
            t.anchor = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = text;
            return t;
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null)
            {
                Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0 };
                for (int i = 0; i < keys.Length && i < Kinds.Length; i++)
                {
                    if (k[keys[i]].wasPressedThisFrame)
                        SetPose(Kinds[i]);
                }
                if (k.oKey.wasPressedThisFrame)
                    overlay = !overlay;
            }
            time += Time.deltaTime;
            var pose = DollPoses.Evaluate(kind, time, punch.Circle0.End.X);
            var skeleton = DollSkeleton.Solve(pose);
            r1.Render(skeleton, pose.WorldAngle);
            r2.Render(skeleton, pose.WorldAngle, Time.deltaTime);
            r3.Render(skeleton, pose.WorldAngle, Time.deltaTime);
            CheckR3Fist(pose);
            CheckFist(pose, skeleton);
            for (int i = 0; i < Count; i++)
                RenderOverlay(i, pose);
        }

        /// <summary>판정 오버레이가 보이는지(판정 상자 선).</summary>
        public bool OverlayShown => boxLines[0] != null && boxLines[0][0].enabled;
        public bool Overlay
        {
            get => overlay;
            set => overlay = value;
        }
        /// <summary>지금 포즈에서 주먹을 끝까지 뻗은 순간을 한 번 이상 확인했는지와 그 결과.</summary>
        public bool FistChecked => fistResult != "-";
        public bool FistOk => fistOk;
        public float TorsoRatio => DollSkeleton.TorsoWidth / (half * 2f);

        public void SetPose(DollPoseKind next)
        {
            kind = next;
            time = 0f;
            fistResult = "-";
            fistOk = false;
            R3FistChecked = false;
            R3FistOk = false;
        }

        /// <summary>주먹을 끝까지 뻗은(활성) 순간 손 중심이 두 판정 원이 지나는 길(시작 → 끝) 안에 있는지.</summary>
        void CheckFist(in DollPose pose, in DollSkeleton k)
        {
            if (!pose.Punching || pose.PunchProgress < 0.99f)
                return;
            float a = pose.AimDegrees;
            bool inside = InsidePath(k.Hand0, punch.Circle0, a) || InsidePath(k.Hand0, punch.Circle1, a);
            fistOk = inside;
            fistResult = inside ? "fist in punch circle: OK" : "fist in punch circle: OUTSIDE";
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r));
        }

        static bool InsidePath(Vector2 p, HitCircle c, float aim)
        {
            var a = Rotate(new Vector2(c.Start.X, c.Start.Y), aim);
            var b = Rotate(new Vector2(c.End.X, c.End.Y), aim);
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude <= c.Radius;
        }

        void RenderOverlay(int i, in DollPose pose)
        {
            bool ceiling = pose.WorldAngle == 180f;
            bool thrown = kind == DollPoseKind.Thrown || kind == DollPoseKind.Held || kind == DollPoseKind.Lunge;
            grounds[i].enabled = !thrown;
            grounds[i].transform.localPosition = new Vector3(0f, ceiling ? half + 0.15f : -half - 0.15f, 0f);
            ShapeLibrary.SetSize(grounds[i], spacing * 0.9f, 0.3f);

            const float line = 0.025f;
            float size = half * 2f;
            var lines = boxLines[i];
            for (int l = 0; l < 4; l++)
                lines[l].enabled = overlay;
            Place(lines[0], new Vector2(0f, half), size + line, line);
            Place(lines[1], new Vector2(0f, -half), size + line, line);
            Place(lines[2], new Vector2(half, 0f), line, size + line);
            Place(lines[3], new Vector2(-half, 0f), line, size + line);

            bool showCircles = overlay && pose.Punching && pose.PunchProgress >= 0.99f;
            for (int r = 0; r < 2; r++)
            {
                var c = r == 0 ? punch.Circle0 : punch.Circle1;
                rings[i][r].enabled = showCircles;
                var at = Rotate(new Vector2(c.End.X, c.End.Y), pose.AimDegrees);
                rings[i][r].transform.localPosition = new Vector3(at.x, at.y, 0f);
                ShapeLibrary.SetSize(rings[i][r], c.Radius * 2f, c.Radius * 2f);
            }

            float ratio = DollSkeleton.TorsoWidth / size;
            bool widthOk = ratio >= 1f && ratio <= 1.25f;
            status[i].gameObject.SetActive(overlay);
            status[i].text = $"torso {DollSkeleton.TorsoWidth:0.00} / hitbox {size:0.00} = {ratio:0.00} {(widthOk ? "OK" : "OUT")}\n{fistResult}";
            status[i].color = widthOk && (fistResult == "-" || fistOk) ? palette.uiSub : palette.hit;
        }

        static void Place(SpriteRenderer sr, Vector2 at, float w, float h)
        {
            sr.transform.localPosition = new Vector3(at.x, at.y, 0f);
            ShapeLibrary.SetSize(sr, w, h);
        }

        void OnGUI()
        {
            const int w = 150, h = 28;
            int y = 10;
            GUI.Label(new Rect(10, y, 300, h), "Pose (1-0)   Overlay (O)");
            y += h;
            for (int i = 0; i < Kinds.Length; i++)
            {
                string label = $"{(i + 1) % 10}  {Kinds[i]}";
                if (GUI.Toggle(new Rect(10, y, w, h), kind == Kinds[i], label, "Button") && kind != Kinds[i])
                    SetPose(Kinds[i]);
                y += h + 2;
            }
            y += 6;
            overlay = GUI.Toggle(new Rect(10, y, w, h), overlay, overlay ? "Overlay ON" : "Overlay OFF", "Button");
        }
    }
}
