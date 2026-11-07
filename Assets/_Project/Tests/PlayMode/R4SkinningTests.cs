#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CR-004 단계 S2: R4 자동 생성 결과(Art/Samples/R4Limb.png, 메뉴 "R4 Skinning Probe")가 실제로 뼈를 따라 변형되는지.
    /// 스프라이트의 뼈로 트랜스폼을 만들어 SpriteSkin에 연결하고, 위쪽 뼈를 돌린 뒤 변형된 정점이 쉴 때와 달라지는지 숫자로 본다.
    /// </summary>
    public class R4SkinningTests
    {
        [UnityTest]
        public IEnumerator GeneratedSprite_DeformsWithBones()
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Samples/R4Limb.png");
            Assert.IsNotNull(sprite, "R4 스프라이트(메뉴 R4 Skinning Probe로 생성)");
            var bones = sprite.GetBones();
            Assert.AreEqual(3, bones.Length);
            Assert.IsTrue(sprite.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight));

            var go = new GameObject("R4 Test");
            go.AddComponent<SpriteRenderer>().sprite = sprite;
            var skin = go.AddComponent<SpriteSkin>();
            skin.alwaysUpdate = true;
            var transforms = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var t = new GameObject(bones[i].name).transform;
                t.SetParent(i == 0 ? go.transform : transforms[bones[i].parentId], false);
                t.localPosition = bones[i].position;
                t.localRotation = bones[i].rotation;
                transforms[i] = t;
            }
            skin.SetRootBone(transforms[0]);
            skin.SetBoneTransforms(transforms);
            yield return null;
            yield return null;
            var rest = go.GetComponent<SpriteRenderer>().bounds;

            transforms[1].localRotation *= Quaternion.Euler(0f, 0f, 60f);
            transforms[2].localRotation *= Quaternion.Euler(0f, 0f, 60f);
            yield return null;
            yield return null;
            var bent = go.GetComponent<SpriteRenderer>().bounds;
            TestContext.WriteLine($"[CR-004 S2] R4 쉬는 경계 {rest.size}, 굽힌 경계 {bent.size}");
            Assert.Greater(Mathf.Abs(bent.size.x - rest.size.x) + Mathf.Abs(bent.size.y - rest.size.y), 0.05f, "뼈를 돌리면 모양이 바뀜");
            Object.Destroy(go);
        }
    }
}
#endif
