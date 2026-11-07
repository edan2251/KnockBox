using Knockbox.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Knockbox.Tests.EditMode
{
    public class PostFxOwnershipTests
    {
        [Test]
        public void RuntimePulseUsesEditedSettings_WithoutChangingSharedComponents()
        {
            var source=ScriptableObject.CreateInstance<VolumeProfile>();
            var chromatic=source.Add<ChromaticAberration>(true);chromatic.intensity.Override(.12f);
            var vignette=source.Add<Vignette>(true);vignette.intensity.Override(.31f);
            var go=new GameObject("PostFX ownership");
            var volume=go.AddComponent<Volume>();var fx=go.AddComponent<PostFx>();
            var serialized=new SerializedObject(fx);
            serialized.FindProperty("baseProfile").objectReferenceValue=source;
            serialized.FindProperty("volume").objectReferenceValue=volume;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                fx.Setup(null);fx.Pulse(.7f,.2f);fx.Tick(.05f);
                Assert.AreEqual(.12f,chromatic.intensity.value);
                Assert.AreEqual(.525f,fx.Aberration,.00001f);
                Assert.True(volume.sharedProfile.TryGet<Vignette>(out var runtime));
                Assert.AreNotSame(vignette,runtime);Assert.AreEqual(.31f,runtime.intensity.value);
                fx.Setup(null);Assert.AreEqual(0,fx.Aberration);Assert.AreEqual(.12f,chromatic.intensity.value);
                // EditMode does not deliver play-lifetime callbacks for a component that
                // never entered Play. Exercise the same cleanup callback explicitly.
                typeof(PostFx).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(fx,null);
                Object.DestroyImmediate(fx);
                Assert.AreSame(source,volume.sharedProfile);Assert.AreEqual(.31f,vignette.intensity.value);
            }
            finally
            {
                Object.DestroyImmediate(go);
                foreach(var component in source.components)Object.DestroyImmediate(component);
                source.components.Clear();Object.DestroyImmediate(source);
            }
        }
    }
}
