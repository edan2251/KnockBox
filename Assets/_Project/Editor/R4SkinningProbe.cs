using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// CR-004 단계 S2: 외형 후보 R4(2D 스키닝) 자동화 가능성 확인. 사람 손 없이 코드만으로
    /// ① 팔다리 모양 스프라이트 이미지 생성 → ② 스프라이트로 가져오기 → ③ 뼈 3개 → ④ 메시(격자)·가중치·외곽선 → ⑤ 적용·재가져오기 →
    /// ⑥ SpriteSkin과 뼈 트랜스폼 연결까지 하고, 단계마다 결과를 로그 한 줄로 남긴다. 결과 오브젝트는 외형 샘플 씬에 "R4_Probe"로 둔다.
    /// </summary>
    public static class R4SkinningProbe
    {
        public const string Folder = "Assets/_Project/Art/Samples";
        public const string TexturePath = Folder + "/R4Limb.png";
        const int Width = 48;
        const int Height = 160;
        const float PixelsPerUnit = 200f;

        [MenuItem("Knockbox/Setup/R4 Skinning Probe")]
        public static void Run()
        {
            var log = new StringBuilder("[Knockbox][R4] ");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Step(log, "image", CreateImage);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.spritePivot = new Vector2(0.5f, 0f);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                log.Append("import ok | ");

                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
                dp.InitSpriteEditorDataProvider();
                var rects = dp.GetSpriteRects();
                var id = rects[0].spriteID;

                // ③ 뼈 3개: 아래에서 위로 이어진 사슬(스프라이트 픽셀 좌표, 위쪽을 가리킴).
                float segment = (Height - 16) / 3f;
                var bones = new List<SpriteBone>
                {
                    new SpriteBone { name = "b0", position = new Vector3(Width * 0.5f, 8f, 0f), rotation = Quaternion.Euler(0f, 0f, 90f), length = segment, parentId = -1 },
                    new SpriteBone { name = "b1", position = new Vector3(segment, 0f, 0f), rotation = Quaternion.identity, length = segment, parentId = 0 },
                    new SpriteBone { name = "b2", position = new Vector3(segment, 0f, 0f), rotation = Quaternion.identity, length = segment, parentId = 1 },
                };
                dp.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, bones);
                log.Append("bones 3 | ");

                // ④ 격자 메시와 높이에 따른 자동 가중치(이웃 두 뼈 사이를 선형으로 섞음), 외곽선.
                const int cols = 3, rows = 9;
                var vertices = new Vertex2DMetaData[cols * rows];
                for (int r = 0; r < rows; r++)
                {
                    float y = Mathf.Lerp(0f, Height, r / (rows - 1f));
                    for (int c = 0; c < cols; c++)
                    {
                        float x = Mathf.Lerp(0f, Width, c / (cols - 1f));
                        vertices[r * cols + c] = new Vertex2DMetaData { position = new Vector2(x, y), boneWeight = Weight(y, segment) };
                    }
                }
                var indices = new List<int>();
                for (int r = 0; r < rows - 1; r++)
                {
                    for (int c = 0; c < cols - 1; c++)
                    {
                        int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                        indices.AddRange(new[] { a, d, b, b, d, e });
                    }
                }
                var edges = new List<Vector2Int>();
                for (int c = 0; c < cols - 1; c++)
                {
                    edges.Add(new Vector2Int(c, c + 1));
                    edges.Add(new Vector2Int((rows - 1) * cols + c, (rows - 1) * cols + c + 1));
                }
                for (int r = 0; r < rows - 1; r++)
                {
                    edges.Add(new Vector2Int(r * cols, (r + 1) * cols));
                    edges.Add(new Vector2Int(r * cols + cols - 1, (r + 1) * cols + cols - 1));
                }
                var mesh = dp.GetDataProvider<ISpriteMeshDataProvider>();
                mesh.SetVertices(id, vertices);
                mesh.SetIndices(id, indices.ToArray());
                mesh.SetEdges(id, edges.ToArray());
                log.Append($"mesh {vertices.Length}v {indices.Count / 3}t | ");

                dp.Apply();
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
                int boneCount = sprite != null ? sprite.GetBones().Length : -1;
                bool weights = sprite != null && sprite.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight);
                log.Append($"apply ok, sprite bones {boneCount}, weights {weights} | ");

                // ⑥ 씬 오브젝트: SpriteRenderer + SpriteSkin, 뼈 트랜스폼 생성·연결.
                var old = GameObject.Find(Knockbox.Presentation.SceneObjectName.Path("R4_Probe"));
                if (old != null)
                    Object.DestroyImmediate(old);
                var go = new GameObject("R4_Probe");
                go.transform.position = new Vector3(3.7f, -1.2f, 0f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(1f, 0.831f, 0.4f);
                renderer.sortingLayerName = "Fighter";
                var skin = go.AddComponent<SpriteSkin>();
                var transforms = new Transform[3];
                var spriteBones = sprite.GetBones();
                for (int i = 0; i < spriteBones.Length; i++)
                {
                    var t = new GameObject(spriteBones[i].name).transform;
                    t.SetParent(i == 0 ? go.transform : transforms[spriteBones[i].parentId], false);
                    t.localPosition = spriteBones[i].position;
                    t.localRotation = spriteBones[i].rotation;
                    transforms[i] = t;
                }
                skin.SetRootBone(transforms[0]);
                skin.SetBoneTransforms(transforms);
                go.AddComponent<Knockbox.Game.R4LimbWiggle>();
                log.Append($"SpriteSkin bound {skin.boneTransforms.Length} | ");
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
                log.Append($"OK in {watch.ElapsedMilliseconds} ms");
                Debug.Log(log.ToString());
            }
            catch (System.Exception e)
            {
                log.Append("FAILED: " + e.GetType().Name + ": " + e.Message);
                Debug.LogError(log.ToString());
            }
        }

        static void Step(StringBuilder log, string name, System.Action action)
        {
            action();
            log.Append(name + " ok | ");
        }

        /// <summary>높이 y(픽셀)의 가중치: 뼈 i 구간 가운데는 뼈 i만, 구간 경계 근처에서는 이웃 뼈와 섞는다.</summary>
        static BoneWeight Weight(float y, float segment)
        {
            float u = Mathf.Clamp((y - 8f) / segment - 0.5f, 0f, 2f);
            int a = Mathf.Min(1, Mathf.FloorToInt(u));
            float t = Mathf.Clamp01(u - a);
            return new BoneWeight { boneIndex0 = a, weight0 = 1f - t, boneIndex1 = a + 1, weight1 = t };
        }

        /// <summary>둥근 끝의 굵은 팔다리 모양(흰색, 바깥 투명).</summary>
        static void CreateImage()
        {
            Directory.CreateDirectory(Folder);
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            float r = Width * 0.5f - 2f;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float cy = Mathf.Clamp(y + 0.5f, r + 2f, Height - r - 2f);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(Width * 0.5f, cy)) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);
        }
    }
}
