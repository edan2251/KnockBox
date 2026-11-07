using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>정렬 레이어 이름(05-presentation.md 1절, 뒤 → 앞).</summary>
    public static class SortingLayers
    {
        public const string Background = "Background";
        public const string Shadow = "Shadow";
        public const string Terrain = "Terrain";
        public const string LimbBack = "LimbBack";
        public const string Fighter = "Fighter";
        public const string LimbFront = "LimbFront";
        public const string Weapon = "Weapon";
        public const string Fx = "Fx";
        public const string UI = "UI";

        public static readonly string[] All = { Background, Shadow, Terrain, LimbBack, Fighter, LimbFront, Weapon, Fx, UI };
    }

    /// <summary>
    /// 에디터 SpriteFactory가 코드로 만든 도형 스프라이트 묶음. 외부 이미지 에셋을 쓰지 않는다.
    /// 둥근 사각형과 캡슐은 9-슬라이스라 SpriteRenderer.size로 크기를 바꿔도 모서리 반경이 유지된다.
    /// 비어 있으면 실행 중에 만든 흰 사각형으로 대신한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Shape Library", fileName = "ShapeLibrary")]
    public sealed class ShapeLibrary : ScriptableObject
    {
        [Tooltip("한 변 1유닛 사각형")] public Sprite square;
        [Tooltip("모서리 반경 0.12, 9-슬라이스")] public Sprite roundedBody;
        [Tooltip("모서리 반경 0.18, 9-슬라이스(몸 외곽선)")] public Sprite roundedOutline;
        [Tooltip("반지름 0.06 캡슐, 9-슬라이스(팔다리 마디)")] public Sprite limbCapsule;
        [Tooltip("반지름 0.05 캡슐, 9-슬라이스(P2 눈)")] public Sprite eyeCapsule;
        [Tooltip("지름 1 원")] public Sprite circle;
        [Tooltip("밑변 1, 높이 1 삼각형(위를 향함)")] public Sprite triangle;
        [Tooltip("지름 1 고리")] public Sprite ring;
        [Tooltip("조명 없이 색을 그대로 그리는 스프라이트 재질(URP Sprite-Unlit-Default)")] public Material unlitMaterial;

        const string UnlitShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        static Material activeMaterial;

        /// <summary>
        /// 연출 스프라이트는 모두 무광 재질로 그린다(플랫한 화면, 2D 조명의 정렬 레이어 설정과 무관).
        /// 경기 연출을 만들기 전에 한 번 부른다.
        /// </summary>
        public void Activate()
        {
            activeMaterial = unlitMaterial;
            if (activeMaterial == null)
            {
                var shader = Shader.Find(UnlitShader);
                if (shader != null)
                    activeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        /// <summary>메시 렌더러(빛 원뿔, 그림자)도 스프라이트와 같은 무광 재질로 그린다.</summary>
        public static Material ActiveMaterial => activeMaterial;

        public Sprite Square => square != null ? square : ShapeSprites.Square;
        public Sprite RoundedBody => roundedBody != null ? roundedBody : Square;
        public Sprite RoundedOutline => roundedOutline != null ? roundedOutline : Square;
        public Sprite LimbCapsule => limbCapsule != null ? limbCapsule : Square;
        public Sprite EyeCapsule => eyeCapsule != null ? eyeCapsule : Square;
        public Sprite Circle => circle != null ? circle : Square;
        public Sprite Triangle => triangle != null ? triangle : Square;
        public Sprite Ring => ring != null ? ring : Square;
        /// <summary>글로용 부드러운 원(실행 중 생성).</summary>
        public Sprite SoftCircle => ShapeSprites.SoftCircle;

        /// <summary>9-슬라이스 스프라이트를 쓰는 렌더러. 크기는 size로 정한다.</summary>
        public static SpriteRenderer CreateSliced(Transform parent, string name, Sprite sprite, string layer, int order, Color color)
        {
            var sr = Create(parent, name, sprite, layer, order, color);
            if (sprite != null && sprite.border != Vector4.zero)
                sr.drawMode = SpriteDrawMode.Sliced;
            return sr;
        }

        public static SpriteRenderer Create(Transform parent, string name, Sprite sprite, string layer, int order, Color color)
        {
            var go = new GameObject(SceneObjectName.Normalize(name));
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (activeMaterial != null)
                sr.sharedMaterial = activeMaterial;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            sr.color = color;
            return sr;
        }

        /// <summary>9-슬라이스면 size를, 아니면 크기 비율로 scale을 맞춘다.</summary>
        public static void SetSize(SpriteRenderer sr, float width, float height)
        {
            if (sr.drawMode == SpriteDrawMode.Sliced)
            {
                sr.size = new Vector2(width, height);
                sr.transform.localScale = Vector3.one;
            }
            else
            {
                var bounds = sr.sprite.bounds.size;
                sr.transform.localScale = new Vector3(width / bounds.x, height / bounds.y, 1f);
            }
        }
    }
}
