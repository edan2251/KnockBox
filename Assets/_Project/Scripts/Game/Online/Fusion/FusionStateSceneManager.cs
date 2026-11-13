using System;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// The network prefab contains only deterministic data, no Unity physics or scene objects.
    /// Keep it in the already loaded scene without transferring ownership of Main/UI to Fusion.
    /// Default scene management waits for a network scene in multi-peer tests; this contract has none.
    /// </summary>
    public sealed class FusionStateSceneManager : INetworkSceneManager
    {
        readonly Scene scene;
        public FusionStateSceneManager(Scene scene) => this.scene = scene;
        public bool IsBusy => false;
        public Scene MainRunnerScene => scene;
        public void Initialize(NetworkRunner runner) { }
        public void Shutdown() { } // Runner releases its spawned prefab; Main stays loaded.
        public bool IsRunnerScene(Scene candidate) => candidate == scene;
        public bool TryGetPhysicsScene2D(out PhysicsScene2D physics) { physics = scene.GetPhysicsScene2D(); return physics.IsValid(); }
        public bool TryGetPhysicsScene3D(out PhysicsScene physics) { physics = scene.GetPhysicsScene(); return physics.IsValid(); }
        public void MakeDontDestroyOnLoad(GameObject obj) => UnityEngine.Object.DontDestroyOnLoad(obj);
        public bool MoveGameObjectToScene(GameObject obj, SceneRef target)
        {
            if (target.IsValid) throw new NotSupportedException("전투 상태 프리팹은 네트워크 씬을 사용하지 않습니다.");
            SceneManager.MoveGameObjectToScene(obj, scene);
            return true;
        }
        public SceneRef GetSceneRef(GameObject obj) => default;
        public SceneRef GetSceneRef(string nameOrPath) => default;
        public bool OnSceneInfoChanged(NetworkSceneInfo info, NetworkSceneInfoChangeSource source)
        {
            if (info.SceneCount != 0) throw new NotSupportedException("현재 Fusion 경기는 기존 씬을 유지하며 씬 로딩을 동기화하지 않습니다.");
            return true;
        }
        public NetworkSceneAsyncOp LoadScene(SceneRef sceneRef, NetworkLoadSceneParameters parameters) => throw new NotSupportedException();
        public NetworkSceneAsyncOp UnloadScene(SceneRef sceneRef) => throw new NotSupportedException();
    }
}
