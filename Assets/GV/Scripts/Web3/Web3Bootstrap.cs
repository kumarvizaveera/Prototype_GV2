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
        [Tooltip("NetworkManager prefab to instantiate on dedicated server (since we skip the menu scene where it normally lives).")]
        [SerializeField] private GameObject networkManagerPrefab;

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

            // --- Dedicated Server: skip menu, go straight to gameplay scene ---
            // The server has no player to click "Enter Battle," so we jump directly
            // to the gameplay scene. But NetworkManager normally lives in the menu scene,
            // so we must instantiate it here before loading gameplay.
            if (IsDedicatedServer())
            {
                // Ensure NetworkManager exists — it normally lives in the menu scene
                // which we're skipping. Instantiate the prefab so it gets DontDestroyOnLoad'd.
                if (GV.Network.NetworkManager.Instance == null)
                {
                    if (networkManagerPrefab != null)
                    {
                        Debug.Log("[Web3Bootstrap] DEDICATED SERVER — instantiating NetworkManager prefab (skipping menu scene)");
                        Instantiate(networkManagerPrefab);
                    }
                    else
                    {
                        Debug.LogError("[Web3Bootstrap] DEDICATED SERVER — networkManagerPrefab not assigned! " +
                            "Drag 'Assets/GV/Prefabs_GV/Network/Network Manager' into the Web3Bootstrap Inspector. " +
                            "Without this, the server cannot start Fusion networking.");
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
