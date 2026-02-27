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
        [SerializeField] private string nextSceneName = "SCK_MainMenu";

        [Tooltip("If true, loads the next scene automatically. If false, waits for manual trigger.")]
        [SerializeField] private bool autoLoadNextScene = true;

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
                Debug.LogError(
                    "[Web3Bootstrap] Web3Manager not found! " +
                    "Create an empty GameObject in this scene and add the Web3Manager component."
                );
            }
            else
            {
                Debug.Log("[Web3Bootstrap] Web3Manager found and ready.");
            }

            // Move to the main menu
            if (autoLoadNextScene && !string.IsNullOrEmpty(nextSceneName))
            {
                Debug.Log($"[Web3Bootstrap] Loading next scene: {nextSceneName}");
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }
}
