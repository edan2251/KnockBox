using System;
using System.IO;
using Knockbox.Presentation;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// 도형 스프라이트를 코드로 만든다(05-presentation.md 1절). 부호 거리 함수로 가장자리를 부드럽게 칠하고,
    /// 같은 입력이면 항상 같은 PNG가 나온다. 정렬 레이어도 여기서 맞춘다.
    /// </summary>
    public static class SpriteFactory
    {
        public const string SpritesFolder = "Assets/_Project/Art/Sprites";
        public const string LibraryPath = "Assets/_Project/Art/ShapeLibrary.asset";
        const float PixelsPerUnit = 200f;
        const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        [MenuItem("Knockbox/Art/Generate Sprites")]
        public static void Generate()
        {
            EnsureSortingLayers();
            Directory.CreateDirectory(SpritesFolder);

            // 9-슬라이스 도형: 반경(유닛) × 200px. 텍스처는 반경의 두 배 + 여유.
            var body = Rounded("rounded_r12", 96, 96, 0.12f);
            var outline = Rounded("rounded_r18", 96, 96, 0.18f);
            var limb = Rounded("capsule_r06", 48, 24, 0.06f);
            var eye = Rounded("capsule_r05", 40, 20, 0.05f);
            var circle = Shape("circle", 128, (x, y) => Length(x - 64f, y - 64f) - 63f);
            var triangle = Shape("triangle", 128, TriangleDistance);
            var ring = Shape("ring", 128, (x, y) => Math.Abs(Length(x - 64f, y - 64f) - 54f) - 9f);
            var square = Shape("square", 4, (x, y) => -1f, filter: FilterMode.Point);

            AssetDatabase.Refresh();
            ConfigureSliced(body, 0.12f);
            ConfigureSliced(outline, 0.18f);
            ConfigureSliced(limb, 0.06f);
            ConfigureSliced(eye, 0.05f);
            ConfigureSimple(circle, 128f);
            ConfigureSimple(triangle, 128f);
            ConfigureSimple(ring, 128f);
            ConfigureSimple(square, 4f, FilterMode.Point);

            var library = AssetDatabase.LoadAssetAtPath<ShapeLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ShapeLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.roundedBody = LoadSprite(body);
            library.roundedOutline = LoadSprite(outline);
            library.limbCapsule = LoadSprite(limb);
            library.eyeCapsule = LoadSprite(eye);
            library.circle = LoadSprite(circle);
            library.triangle = LoadSprite(triangle);
            library.ring = LoadSprite(ring);
            library.square = LoadSprite(square);
            library.unlitMaterial = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Knockbox] 도형 스프라이트 생성: {SpritesFolder}, {LibraryPath}");
        }

        static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        /// <summary>밑변이 아래, 꼭짓점이 위 가운데인 삼각형. 세 변까지의 부호 거리 중 최댓값(볼록 다각형 근사).</summary>
        static float TriangleDistance(float x, float y)
        {
            const float size = 128f, margin = 1f;
            float left = margin, right = size - margin, bottom = margin, top = size - margin;
            float cx = size * 0.5f;
            float d = bottom - y;
            d = Math.Max(d, EdgeDistance(x, y, left, bottom, cx, top));
            d = Math.Max(d, EdgeDistance(x, y, cx, top, right, bottom));
            return d;
        }

        /// <summary>a → b 직선의 왼쪽(시계 방향 꼭짓점 순서에서 바깥)이 양수인 부호 거리.</summary>
        static float EdgeDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax, ey = by - ay;
            float len = Length(ex, ey);
            return ((y - ay) * ex - (x - ax) * ey) / len;
        }

        static string Rounded(string name, int width, int height, float radiusUnits)
        {
            float r = radiusUnits * PixelsPerUnit;
            float hx = width * 0.5f, hy = height * 0.5f;
            return Write(name, width, height, (x, y) =>
            {
                float qx = Math.Abs(x - hx) - (hx - r - 0.5f);
                float qy = Math.Abs(y - hy) - (hy - r - 0.5f);
                float outside = Length(Math.Max(qx, 0f), Math.Max(qy, 0f));
                float inside = Math.Min(Math.Max(qx, qy), 0f);
                return outside + inside - r;
            }, FilterMode.Bilinear);
        }

        static string Shape(string name, int size, Func<float, float, float> distance, FilterMode filter = FilterMode.Bilinear) =>
            Write(name, size, size, distance, filter);

        /// <summary>픽셀 중심에서의 부호 거리로 알파를 칠한 흰 PNG.</summary>
        static string Write(string name, int width, int height, Func<float, float, float> distance, FilterMode filter)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = distance(x + 0.5f, y + 0.5f);
                    float a = Mathf.Clamp01(0.5f - d);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = $"{SpritesFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            return path;
        }

        static void ConfigureSliced(string path, float radiusUnits)
        {
            int border = Mathf.RoundToInt(radiusUnits * PixelsPerUnit) + 1;
            Configure(path, PixelsPerUnit, FilterMode.Bilinear, new Vector4(border, border, border, border));
        }

        static void ConfigureSimple(string path, float pixelsPerUnit, FilterMode filter = FilterMode.Bilinear) =>
            Configure(path, pixelsPerUnit, filter, Vector4.zero);

        static void Configure(string path, float pixelsPerUnit, FilterMode filter, Vector4 border)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = filter;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        /// <summary>정렬 레이어를 명세 순서대로 맞춘다. 없는 것만 추가하고, ID는 이름에서 정해 diff를 안정시킨다.</summary>
        [MenuItem("Knockbox/Art/Ensure Sorting Layers")]
        public static void EnsureSortingLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("m_SortingLayers");
            bool changed = false;
            foreach (string name in SortingLayers.All)
            {
                bool exists = false;
                for (int i = 0; i < layers.arraySize; i++)
                    exists |= layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name;
                if (exists)
                    continue;
                layers.InsertArrayElementAtIndex(layers.arraySize);
                var entry = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                entry.FindPropertyRelative("name").stringValue = name;
                entry.FindPropertyRelative("uniqueID").uintValue = StableId(name);
                entry.FindPropertyRelative("locked").boolValue = false;
                changed = true;
            }
            if (changed)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("[Knockbox] 정렬 레이어 추가");
            }
        }

        static uint StableId(string name)
        {
            uint h = 2166136261u;
            foreach (char c in name)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h == 0 ? 1u : h;
        }
    }
}
