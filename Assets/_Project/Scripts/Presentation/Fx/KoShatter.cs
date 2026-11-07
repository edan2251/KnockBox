using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Knockbox.Presentation
{
    /// <summary>
    /// KO 파편(05-presentation.md 4·5절): 진 쪽 몸이 4~6조각으로 깨져 Unity 2D 물리로 경기장을 튕겨 다닌다.
    /// 연출 물리는 별도 물리 씬(LocalPhysicsMode.Physics2D)에 두고 시뮬레이션 시간으로 직접 진행한다.
    /// 그래서 프로젝트의 물리 설정이나 기본 물리 씬과 섞이지 않고(연출 물리 전용 레이어 역할), 캡처도 재현된다.
    /// 시뮬레이션 상태는 읽지도 쓰지도 않는다(맵 상자 목록으로 충돌체만 만든다).
    /// </summary>
    public sealed class KoShatter : MonoBehaviour
    {
        const int PieceCount = 6;
        const float OutlinePad = 0.1f;
        const float GravityScale = 5f;
        const float MaxStep = 1f / 120f;
        const int DustPerPiece = 4;

        struct Piece
        {
            public GameObject Root;
            public Rigidbody2D Body;
            public SpriteRenderer Fill;
            public SpriteRenderer Outline;
            public Vector2 Size;
            public bool Triangle;
        }

        readonly Piece[] pieces = new Piece[PieceCount];
        readonly Vector3[] dissolveStarts = new Vector3[PieceCount];
        readonly SpriteRenderer[] dissolveDust = new SpriteRenderer[PieceCount * DustPerPiece];
        readonly Vector2[] dustDrift = new Vector2[PieceCount * DustPerPiece];
        readonly float[] dustSizes = new float[PieceCount * DustPerPiece];
        Color pieceColor, dustColor;
        Scene scene;
        PhysicsScene2D physics;
        GameObject arenaRoot;
        PaletteAsset palette;
        ShapeLibrary shapes;
        FxRandom random = new FxRandom(0x5A7Eu);
        bool active;

        /// <summary>끄면 KO 파편을 만들지 않는다(연출 물리를 켜고 끈 비교, CP8-5).</summary>
        public bool PhysicsEnabled { get; set; } = true;
        public bool Active => active;
        public bool Exiting { get; private set; }
        public int PieceCountActive { get; private set; }
        public int DissolveDustCount { get; private set; }
        public SpriteRenderer PieceVisual(int index) => active && index >= 0 && index < PieceCount ? pieces[index].Fill : null;
        public SpriteRenderer DustVisual(int index) => index >= 0 && index < dissolveDust.Length ? dissolveDust[index] : null;

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapeLibrary)
        {
            palette = paletteAsset;
            shapes = shapeLibrary;
            scene = SceneManager.CreateScene("Knockbox Fx Physics " + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            var material = new PhysicsMaterial2D("KoPiece") { bounciness = 0.45f, friction = 0.4f };
            for (int i = 0; i < PieceCount; i++)
            {
                bool triangle = i >= 4;
                var root = new GameObject($"KoPiece{i}");
                SceneManager.MoveGameObjectToScene(root, scene);
                var outline = ShapeLibrary.Create(root.transform, "Outline", triangle ? shapes.Triangle : shapes.Square, SortingLayers.Fighter, 5, palette.ink);
                var fill = ShapeLibrary.Create(root.transform, "Fill", triangle ? shapes.Triangle : shapes.Square, SortingLayers.Fighter, 6, palette.p1Body);
                var body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = GravityScale;
                body.sharedMaterial = material;
                body.interpolation = RigidbodyInterpolation2D.None;
                Collider2D collider;
                if (triangle)
                {
                    var poly = root.AddComponent<PolygonCollider2D>();
                    poly.points = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0f, 0.5f) };
                    collider = poly;
                }
                else
                {
                    collider = root.AddComponent<BoxCollider2D>();
                }
                collider.sharedMaterial = material;
                root.SetActive(false);
                pieces[i] = new Piece { Root = root, Body = body, Fill = fill, Outline = outline, Triangle = triangle };
            }
            // KO 전용 풀. 일반 착지 먼지/타격 조각의 모양이나 수명에는 영향을 주지 않는다.
            for (int i = 0; i < dissolveDust.Length; i++)
            {
                dissolveDust[i] = ShapeLibrary.Create(transform, "KO dissolve dust " + i, shapes.Circle, SortingLayers.Fx, 0, palette.uiSub);
                dissolveDust[i].enabled = false;
            }
        }

        void OnDestroy()
        {
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>맵의 상자 목록으로 연출 물리용 정적 충돌체를 만든다(단방향 발판도 막는 상자로 단순화).</summary>
        public void BuildArena(ArenaDef arena)
        {
            if (arenaRoot != null)
                Destroy(arenaRoot);
            arenaRoot = new GameObject("KoArena");
            SceneManager.MoveGameObjectToScene(arenaRoot, scene);
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var b = ref arena.GetBox(i);
                var go = new GameObject($"Box{i}");
                go.transform.SetParent(arenaRoot.transform, false);
                go.transform.position = new Vector3((b.MinX + b.MaxX) * 0.5f, (b.MinY + b.MaxY) * 0.5f, 0f);
                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(b.MaxX - b.MinX, b.MaxY - b.MinY);
            }
            Clear();
        }

        /// <summary>
        /// 몸을 조각낸다. center는 그려진 몸 중심, push는 날아가던 방향(없으면 위로).
        /// 4개는 사각 조각(몸의 네 귀퉁이), 2개는 삼각 조각.
        /// </summary>
        public void Shatter(Vector2 center, Color color, Vector2 push)
        {
            if (!PhysicsEnabled)
                return;
            Clear();
            active = true;
            Exiting = false;
            pieceColor = color;
            dustColor = Color.Lerp(color, palette.uiSub, .2f);
            int count = 0;
            var dir = push.sqrMagnitude > 1e-4f ? push.normalized : Vector2.up;
            for (int i = 0; i < PieceCount; i++)
            {
                ref var p = ref pieces[i];
                float size = p.Triangle ? random.Range(0.32f, 0.42f) : random.Range(0.4f, 0.5f);
                p.Size = new Vector2(size, size);
                var offset = p.Triangle
                    ? new Vector2(i == 4 ? -0.1f : 0.1f, 0f)
                    : new Vector2((i % 2 == 0 ? -1f : 1f) * 0.25f, (i < 2 ? 1f : -1f) * 0.25f);
                p.Root.transform.position = center + offset;
                p.Root.transform.rotation = Quaternion.Euler(0f, 0f, random.Range(-20f, 20f));
                ShapeLibrary.SetSize(p.Fill, size, size);
                ShapeLibrary.SetSize(p.Outline, size + OutlinePad, size + OutlinePad);
                p.Fill.color = color;
                p.Outline.color = palette.ink;
                if (p.Root.TryGetComponent<BoxCollider2D>(out var box))
                    box.size = new Vector2(size, size);
                else if (p.Root.TryGetComponent<PolygonCollider2D>(out var poly))
                    poly.points = new[] { new Vector2(-0.5f, -0.5f) * size, new Vector2(0.5f, -0.5f) * size, new Vector2(0f, 0.5f) * size };
                p.Root.SetActive(true);
                p.Body.simulated = true;
                // 조각마다 몸 중심에서 바깥쪽 + 날아가던 방향으로.
                var outward = offset.sqrMagnitude > 1e-4f ? offset.normalized : Random2();
                p.Body.linearVelocity = (outward * random.Range(5f, 9f)) + dir * random.Range(4f, 8f) + Vector2.up * random.Range(4f, 8f);
                p.Body.angularVelocity = random.Range(-720f, 720f);
                count++;
            }
            PieceCountActive = count;
        }

        Vector2 Random2()
        {
            float a = random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        public void Clear()
        {
            active = false;
            Exiting = false;
            PieceCountActive = 0;
            DissolveDustCount = 0;
            foreach (var dust in dissolveDust) if (dust != null) dust.enabled = false;
            for (int i = 0; i < PieceCount; i++)
            {
                if (pieces[i].Root != null)
                    pieces[i].Root.SetActive(false);
            }
        }

        /// <summary>충분히 낙하한 뒤 지형 퇴장 시점에 제자리에서 작은 먼지로 소멸한다. 화면 밖 목표점은 없다.</summary>
        public void PresentDissolve(float progress)
        {
            if (!active || progress <= 0) return;
            if (!Exiting)
            {
                Exiting = true;
                for (int i = 0; i < PieceCount; i++)
                {
                    var p = pieces[i];
                    dissolveStarts[i] = p.Root.transform.position;
                    // 흩어지기 직전의 실제 위치에 머문다. 사라지는 동안 충돌체/중력은 더 이상 필요하지 않다.
                    p.Body.simulated = false;
                    for (int d = 0; d < DustPerPiece; d++)
                    {
                        int j = i * DustPerPiece + d;
                        dustDrift[j] = new Vector2((d % 2 == 0 ? -1 : 1) * random.Range(.18f, .50f), random.Range(.08f, .35f));
                        dustSizes[j] = random.Range(.09f, .15f);
                    }
                }
            }
            float t = Mathf.Clamp01(progress);
            PieceCountActive = DissolveDustCount = 0;
            for (int i = 0; i < PieceCount; i++)
            {
                float delay = i * .012f;
                float remaining = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.02f + delay, .42f + delay, t));
                ref var piece = ref pieces[i];
                ShapeLibrary.SetSize(piece.Fill, piece.Size.x * remaining, piece.Size.y * remaining);
                ShapeLibrary.SetSize(piece.Outline, (piece.Size.x + OutlinePad) * remaining, (piece.Size.y + OutlinePad) * remaining);
                piece.Fill.color = PaletteAsset.WithAlpha(pieceColor, remaining);
                piece.Outline.color = PaletteAsset.WithAlpha(palette.ink, remaining);
                piece.Root.SetActive(remaining > 0);
                if (remaining > 0) PieceCountActive++;
                float dustTime = Mathf.InverseLerp(.08f + delay, .93f + delay, t);
                float drift = 1 - Mathf.Pow(1 - dustTime, 3);
                float opacity = .75f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .18f, dustTime))
                    * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.20f, 1, dustTime)));
                for (int d = 0; d < DustPerPiece; d++)
                {
                    int j = i * DustPerPiece + d;
                    var dust = dissolveDust[j];
                    dust.transform.position = dissolveStarts[i] + (Vector3)(dustDrift[j] * drift);
                    float size = dustSizes[j] * (1 - .7f * drift);
                    ShapeLibrary.SetSize(dust, size, size);
                    dust.color = PaletteAsset.WithAlpha(dustColor, opacity);
                    dust.enabled = opacity > .001f;
                    if (dust.enabled) DissolveDustCount++;
                }
            }
            if (t >= 1) Clear();
        }

        /// <summary>연출 시간으로 연출 물리를 진행한다(시간이 멈추면 조각도 멈춘다).</summary>
        public void Tick(float dt)
        {
            if (!active || Exiting || dt <= 0f)
                return;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxStep), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
                physics.Simulate(h);
        }
    }
}
