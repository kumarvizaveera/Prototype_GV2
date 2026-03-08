using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A no-op INetworkSceneManager that satisfies Fusion's requirement for a scene manager
/// but does absolutely nothing. This prevents Fusion from auto-creating a
/// NetworkSceneManagerDefault (which would auto-sync scenes and bypass our manual
/// LOAD/COUNTDOWN/START_MATCH flow in the dedicated server room-based architecture).
///
/// Key behavior:
/// - OnSceneInfoChanged returns TRUE → tells Fusion "I'm handling it" (but does nothing)
/// - LoadScene/UnloadScene return completed operations (no actual loading)
/// - This keeps the client on whatever scene it's currently on until WE tell it to load
/// </summary>
public class NoOpSceneManager : Fusion.Behaviour, INetworkSceneManager
{
    private NetworkRunner _runner;

    public bool IsBusy => false;

    public Scene MainRunnerScene => SceneManager.GetActiveScene();

    public void Initialize(NetworkRunner runner)
    {
        _runner = runner;
        Debug.Log("[NoOpSceneManager] Initialized — scene sync is DISABLED for this runner.");
    }

    public void Shutdown()
    {
        Debug.LogWarning($"[NoOpSceneManager] Shutdown called! Stack trace:\n{System.Environment.StackTrace}");
        _runner = null;
    }

    /// <summary>
    /// Return TRUE to tell Fusion "I'm handling scene changes myself."
    /// Since we do nothing, the scene stays exactly where it is.
    /// Our manual LOAD commands (via ReliableData) handle scene transitions.
    /// </summary>
    public bool OnSceneInfoChanged(NetworkSceneInfo sceneInfo, NetworkSceneInfoChangeSource changeSource)
    {
        string sceneRefs = "";
        for (int i = 0; i < sceneInfo.SceneCount; i++)
            sceneRefs += $" [{i}]=idx{sceneInfo.GetSceneRef(i).AsIndex}";
        Debug.Log($"[NoOpSceneManager] OnSceneInfoChanged (source={changeSource}, sceneCount={sceneInfo.SceneCount}{sceneRefs}) — IGNORING (manual scene control).");
        return true; // "I'll handle it" — but we intentionally do nothing
    }

    public NetworkSceneAsyncOp LoadScene(SceneRef sceneRef, NetworkLoadSceneParameters parameters)
    {
        Debug.Log($"[NoOpSceneManager] LoadScene({sceneRef}) called — IGNORING.");
        // Return a completed/default operation so Fusion doesn't hang
        return default;
    }

    public NetworkSceneAsyncOp UnloadScene(SceneRef sceneRef)
    {
        Debug.Log($"[NoOpSceneManager] UnloadScene({sceneRef}) called — IGNORING.");
        return default;
    }

    public bool IsRunnerScene(Scene scene)
    {
        return scene == SceneManager.GetActiveScene();
    }

    public bool TryGetPhysicsScene2D(out PhysicsScene2D scene2D)
    {
        scene2D = default;
        return false;
    }

    public bool TryGetPhysicsScene3D(out PhysicsScene scene3D)
    {
        scene3D = MainRunnerScene.GetPhysicsScene();
        return true;
    }

    public void MakeDontDestroyOnLoad(GameObject obj)
    {
        Object.DontDestroyOnLoad(obj);
    }

    public bool MoveGameObjectToScene(GameObject gameObject, SceneRef sceneRef)
    {
        // No scene isolation needed — everything stays in the active scene
        return false;
    }

    public SceneRef GetSceneRef(string sceneNameOrPath)
    {
        var buildIndex = SceneUtility.GetBuildIndexByScenePath(sceneNameOrPath);
        if (buildIndex >= 0)
            return SceneRef.FromIndex(buildIndex);
        return SceneRef.None;
    }

    public SceneRef GetSceneRef(GameObject gameObject)
    {
        if (gameObject == null) return default;
        return GetSceneRef(gameObject.scene.path);
    }
}
