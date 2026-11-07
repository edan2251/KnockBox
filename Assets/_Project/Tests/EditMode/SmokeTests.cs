using Knockbox.EditorTools;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP0 골격이 제자리에 있는지 확인한다. 이후 CP에서도 회귀 검사로 계속 돈다.</summary>
    public class SmokeTests
    {
        [Test]
        public void RuntimeAssemblies_AreCompiledWithExpectedNames()
        {
            Assert.AreEqual(SimulationAssembly.Name, typeof(SimulationAssembly).Assembly.GetName().Name);
            Assert.AreEqual(PresentationAssembly.Name, typeof(PresentationAssembly).Assembly.GetName().Name);
            Assert.AreEqual(GameAssembly.Name, typeof(GameAssembly).Assembly.GetName().Name);
        }

        [Test]
        public void BuildScenes_AreMainThenSandbox()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2));
            Assert.AreEqual(ProjectSetup.MainScenePath, scenes[0].path);
            Assert.AreEqual(ProjectSetup.SandboxScenePath, scenes[1].path);
            Assert.IsTrue(scenes[0].enabled && scenes[1].enabled);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.MainScenePath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.SandboxScenePath));
        }

        [Test]
        public void ActivePipeline_IsUrpWith2DRenderer()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(urp, "활성 렌더 파이프라인이 URP가 아님");

            var rendererList = new SerializedObject(urp).FindProperty("m_RendererDataList");
            var defaultIndex = new SerializedObject(urp).FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = rendererList.GetArrayElementAtIndex(defaultIndex).objectReferenceValue;
            Assert.IsInstanceOf<Renderer2DData>(renderer, "기본 렌더러가 2D Renderer가 아님");
        }

        [Test]
        public void PlayerSettings_RunInBackgroundIsOn()
        {
            Assert.IsTrue(PlayerSettings.runInBackground);
        }
    }
}
