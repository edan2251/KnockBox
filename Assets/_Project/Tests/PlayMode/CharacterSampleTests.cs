using System.Collections;
using Knockbox.Game;
using Knockbox.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CR-004 단계 S(8절): 외형 샘플이 콘솔 오류 없이 모든 포즈를 그리고, 판정 오버레이가 켜지며, 몸통 폭이 판정 폭의 1~1.25배이고,
    /// 네 방향 주먹을 끝까지 뻗은 순간 주먹 끝이 주먹 판정 원 안에 있는지 숫자로 확인한다(화면 촬영 없음).
    /// </summary>
    public class CharacterSampleTests
    {
        GameObject cameraObject;
        CharacterSampleScene sample;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            cameraObject = new GameObject("Main_Camera") { tag = "MainCamera" };
            var cam = cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.1f;
            cam.transform.position = new Vector3(0f, 0.3f, -10f);
            sample = new GameObject("Character_Sample").AddComponent<CharacterSampleScene>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(sample.gameObject);
            Object.Destroy(cameraObject);
            yield return null;
        }

        /// <summary>
        /// 세 후보가 실제로 그려지는지: 렌더 텍스처에 그린 뒤 각 인형 몸통 가운데 픽셀이 배경색이 아니라 몸 색(노랑)에 가까운지 숫자로 본다.
        /// R3는 셰이더(Knockbox/SdfDoll)가 2D 렌더러에서 그려지는지도 함께 확인된다.
        /// </summary>
        [UnityTest]
        public IEnumerator AllThreeCandidates_DrawBodyColorAtTorso()
        {
            Assert.IsTrue(sample.SdfReady, "R3 셰이더를 찾음");
            sample.Overlay = false;
            sample.SetPose(DollPoseKind.Idle);
            yield return null;
            yield return null;
            var cam = Camera.main;
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            var background = cam.backgroundColor;
            for (int i = 0; i < 3; i++)
            {
                // 몸통 가운데에서 조금 비켜 잰다(부채꼴 메시의 중심 꼭짓점이 픽셀 경계에 걸리지 않게).
                var screen = cam.WorldToViewportPoint(sample.DollPosition(i) + new Vector3(-0.15f, 0.02f, 0f));
                var c = tex.GetPixel((int)(screen.x * rt.width), (int)(screen.y * rt.height));
                float fromBackground = Mathf.Abs(c.r - background.r) + Mathf.Abs(c.g - background.g) + Mathf.Abs(c.b - background.b);
                TestContext.WriteLine($"[CR-004 S] R{i + 1} 몸통 픽셀 {c}");
                Assert.Greater(fromBackground, 0.5f, $"R{i + 1}: 몸통 자리에 무언가 그려짐");
                Assert.Greater(c.r, 0.6f, $"R{i + 1}: 몸 색(노랑) {c}");
                Assert.Greater(c.g, 0.5f, $"R{i + 1}: 몸 색(노랑) {c}");
            }
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }

        [UnityTest]
        public IEnumerator AllPoses_RenderWithoutErrors_OverlayShown_FistInsideCircle()
        {
            Assert.IsTrue(sample.OverlayShown, "판정 오버레이가 기본으로 켜짐");
            Assert.That(sample.TorsoRatio, Is.InRange(1f, 1.25f), "보이는 몸통 폭 = 판정 폭의 1~1.25배");
            foreach (DollPoseKind kind in System.Enum.GetValues(typeof(DollPoseKind)))
            {
                sample.SetPose(kind);
                float until = Time.time + DollPoses.PunchCycle * 1.2f;
                while (Time.time < until)
                    yield return null;
                bool punch = kind == DollPoseKind.PunchForward || kind == DollPoseKind.PunchUp || kind == DollPoseKind.PunchDown || kind == DollPoseKind.PunchDiagonal;
                if (punch)
                {
                    Assert.IsTrue(sample.FistChecked, $"{kind}: 주먹을 끝까지 뻗은 순간을 확인함");
                    Assert.IsTrue(sample.FistOk, $"{kind}: 주먹 끝이 판정 원 안");
                    Assert.IsTrue(sample.R3FistChecked && sample.R3FistOk, $"{kind}: R3 주먹 끝이 판정 원 안");
                }
            }
            sample.Overlay = false;
            yield return null;
            Assert.IsFalse(sample.OverlayShown, "끄면 숨김");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
