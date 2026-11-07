using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>UI fixtures own an empty runtime scene; a preceding Sandbox test must not leak its camera/audio.</summary>
    internal static class PlayModeSceneScope
    {
        static readonly string artifactRoot=System.IO.Path.Combine("Captures","Automated-"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+System.Guid.NewGuid().ToString("N"));
        public static string CapturePath(string filename)
        {
            System.IO.Directory.CreateDirectory(artifactRoot);
            return System.IO.Path.Combine(artifactRoot,filename);
        }
        public static System.IDisposable SuspendPointerInput() => new PointerScope();
        sealed class PointerScope : System.IDisposable
        {
            readonly UnityEngine.EventSystems.BaseInputModule module;
            readonly bool wasEnabled;
            public PointerScope()
            {
                module = UnityEngine.EventSystems.EventSystem.current?.currentInputModule;
                wasEnabled = module != null && module.enabled;
                if (module != null) module.enabled = false;
            }
            public void Dispose() { if (module != null) module.enabled = wasEnabled; }
        }
        public static IEnumerator OpenEmpty()
        {
            var previous = new Scene[SceneManager.sceneCount];
            for (int i = 0; i < previous.Length; i++) previous[i] = SceneManager.GetSceneAt(i);
            var scene = SceneManager.CreateScene("UI fixture " + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(scene);
            foreach (var old in previous)
                if (old.IsValid() && old.isLoaded &&
                    (old.name == "Main" || old.name == "Sandbox" || old.name.StartsWith("UI fixture ")))
                    yield return SceneManager.UnloadSceneAsync(old);
            yield return null;
        }
    }
}
