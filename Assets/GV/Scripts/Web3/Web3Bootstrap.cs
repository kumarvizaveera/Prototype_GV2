using UnityEngine;
using UnityEngine.SceneManagement;
using Thirdweb.Unity;

namespace GV.Web3
{
    /// <summary>
    /// Bootstrap script that ensures Web3 systems are ready before anything else.
    ///
    /// What this does:
    /// - Checks that ThirdwebManager exists (the Thirdweb SDK needs this to work)
    /// - Checks that Web3Manager exists
    /// - Optionally loads the main menu scene after setup
    ///
    /// How it works:
    /// This script goes in a "Bootstrap" scene — a tiny scene that loads first.
    /// Its only job is to make sure the persistent managers exist, then move on
    /// to the actual menu. Think of it like a loading screen that checks everything
    /// is plugged in before the game starts.
    ///
    /// Setup:
    /// 1. Create a new empty scene called "Bootstrap" (File > New Scene > Empty)
    /// 2. Add an empty GameObject, attach this script
    /// 3. Make sure ThirdwebManager prefab is also in this scene
    /// 4. Make sure a Web3Manager object is also in this scene
    /// 5. Set "Bootstrap" as Scene 0 in Build Settings (so it loads first)
    /// 6. Set nextSceneName to your main menu scene name
    /// </summary>
    public class Web3Bootstrap : MonoBehaviour
    {
        [Header("Scene Flow")]
        [Tooltip("The scene to load after bootstrap is done. Leave empty to stay in this scene.")]
        [SerializeField] private string nextSceneName = "";

        [Tooltip("On dedicated server, skip the menu and load this scene directly (where NetworkManager lives).")]
        [SerializeField] private string gameplaySceneName = "";

        [Tooltip("If true, loads the next scene automatically. If false, waits for manual trigger.")]
        [SerializeField] private bool autoLoadNextScene = true;

        [Header("Dedicated Server")]
        [Tooltip("NetworkManager prefab — used by RoomManager to create per-room instances on dedicated server. " +
                 "Also used by clients in the menu scene.")]
        [SerializeField] private GameObject networkManagerPrefab;

        [Tooltip("RoomManager prefab to instantiate on dedicated server. " +
                 "Manages multiple rooms, each with its own NetworkManager + Fusion session.")]
        [SerializeField] private GameObject roomManagerPrefab;

        /// <summary>
        /// Checks if this build should run as a dedicated server.
        /// Same logic as ServerBootstrap and NetworkManager.
        /// </summary>
        private bool IsDedicatedServer()
        {
            #if UNITY_SERVER
            return true;
            #endif

            string[] args = System.Environment.GetCommandLineArgs();
            Debug.Log($"[Web3Bootstrap] Command line args: {string.Join(", ", args)}");
            foreach (string arg in args)
            {
                if (arg.ToLower() == "-server" || arg.ToLower() == "--server")
                {
                    Debug.Log($"[Web3Bootstrap] Found server flag: {arg}");
                    return true;
                }
            }
            return false;
        }

        private void Start()
        {
            // Verify ThirdwebManager is present
            if (ThirdwebManager.Instance == null)
            {
                Debug.LogError(
                    "[Web3Bootstrap] ThirdwebManager not found! " +
                    "Drag the ThirdwebManager prefab (Assets/Thirdweb/Runtime/Unity/Prefabs/) into this scene " +
                    "and set your ClientId in the Inspector."
                );
            }
            else
            {
                Debug.Log("[Web3Bootstrap] ThirdwebManager found and ready.");
            }

            // Verify Web3Manager is present
            if (Web3Manager.Instance == null)
            {
                Debug.LogWarning(
                    "[Web3Bootstrap] Web3Manager not found! " +
                    "Auto-creating a Web3Manager GameObject with default settings."
                );
                GameObject web3ManagerObj = new GameObject("Web3Manager");
                web3ManagerObj.AddComponent<Web3Manager>();
            }
            else
            {
                Debug.Log("[Web3Bootstrap] Web3Manager found and ready.");
            }

            // --- Dedicated Server: instantiate RoomManager, skip menu, load gameplay ---
            // RoomManager handles creating per-room NetworkManagers on demand via HTTP API.
            // No single NetworkManager is auto-started — rooms are created when clients request them.
            if (IsDedicatedServer())
            {
                // Instantiate RoomManager (it will DontDestroyOnLoad itself)
                if (GV.Network.RoomManager.Instance == null)
                {
                    if (roomManagerPrefab != null)
                    {
                        Debug.Log("[Web3Bootstrap] DEDICATED SERVER — instantiating RoomManager");
                        var rmGO = Instantiate(roomManagerPrefab);

                        // Pass the NetworkManager prefab reference to RoomManager
                        // so it can create per-room instances
                        // (This is set in the Inspector on the RoomManager prefab)
                    }
                    else
                    {
                        Debug.LogError("[Web3Bootstrap] DEDICATED SERVER — roomManagerPrefab not assigned! " +
                            "Create a RoomManager prefab and assign it in the Web3Bootstrap Inspector.");
                    }
                }

                string serverScene = !string.IsNullOrEmpty(gameplaySceneName) ? gameplaySceneName : nextSceneName;
                Debug.Log($"[Web3Bootstrap] DEDICATED SERVER — skipping menu, loading gameplay scene: {serverScene}");
                SceneManager.LoadScene(serverScene);
                return;
            }

            // Load the menu scene (where wallet UI + room UI live)
            if (autoLoadNextScene && !string.IsNullOrEmpty(nextSceneName))
            {
                Debug.Log($"[Web3Bootstrap] Loading menu scene: {nextSceneName}");
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }
}
