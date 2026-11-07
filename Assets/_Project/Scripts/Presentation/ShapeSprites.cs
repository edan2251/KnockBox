using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 실행 중에 만드는 단순 도형 스프라이트. CP3에서 에디터 SpriteFactory가 만든 에셋으로 바꾼다.
    /// </summary>
    public static class ShapeSprites
    {
        static Sprite square;
        static Sprite softCircle;

        /// <summary>
        /// 가운데가 불투명하고 가장자리로 부드럽게 사라지는 흰 원(지름 1유닛). 블룸 대신 쓰는 글로(08 5절)용 마스크.
        /// </summary>
        public static Sprite SoftCircle
        {
            get
            {
                if (softCircle != null)
                    return softCircle;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "KnockboxSoftCircle",
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float r2 = Mathf.Clamp01(dx * dx + dy * dy);
                        float a = (1f - r2) * (1f - r2);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                softCircle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                softCircle.hideFlags = HideFlags.HideAndDontSave;
                softCircle.name = "KnockboxSoftCircle";
                return softCircle;
            }
        }

        /// <summary>한 변 1유닛, 가운데 기준의 흰 사각형.</summary>
        public static Sprite Square
        {
            get
            {
                if (square != null)
                    return square;
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                square.hideFlags = HideFlags.HideAndDontSave;
                square.name = "KnockboxSquare";
                return square;
            }
        }

        /// <summary>
        /// 배경 질감용 저주파 노이즈 값(0~1, 08 4절): 큰 얼룩 + 붓질 같은 가로로 긴 결. Mathf.PerlinNoise라 실행마다 같다.
        /// </summary>
        public static float[] NoiseValues(int width, int height, float scale)
        {
            var values = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float n = Mathf.PerlinNoise(x * scale + 17.3f, y * scale + 41.9f) * 0.7f
                              + Mathf.PerlinNoise(x * scale * 3.1f + 5.1f, y * scale * 9.7f + 2.7f) * 0.3f;
                    values[y * width + x] = Mathf.Clamp01(n);
                }
            }
            return values;
        }
    }
}
