using UnityEngine;

namespace GV.Network
{
    /// <summary>
    /// Server Bootstrap — placed in the first scene that loads.
    /// When running as a dedicated server (detected via command-line args or UNITY_SERVER define),
    /// this script:
    /// 1. Disables audio (server has no speakers)
    /// 2. Sets target framerate to match the Fusion tick rate (saves CPU)
    /// 3. Disables unnecessary rendering features
    /// 4. Logs server startup info
    ///
    /// On regular clients/hosts, this script does nothing.
    /// </summary>
    public class ServerBootstrap : MonoBehaviour
    {
        [Header("Server Settings")]
        [Tooltip("Target framerate for dedicated server. Should match or exceed Fusion tick rate (default 60).")]
        [SerializeField] private int serverTargetFramerate = 60;

        private void Awake()
        {
            // Only activate on dedicated server
            if (!IsDedicatedServer()) return;

            Debug.Log("=== GV2 DEDICATED SERVER STARTING ===");
            Debug.Log($"[ServerBootstrap] Unity version: {Application.unityVersion}");
            Debug.Log($"[ServerBootstrap] Platform: {Application.platform}");
            Debug.Log($"[ServerBootstrap] Command line: {System.Environment.CommandLine}");

            // 1. Cap framerate to save CPU (no need for 300+ FPS on a server)
            Application.targetFrameRate = serverTargetFramerate;
            Debug.Log($"[ServerBootstrap] Target framerate set to {serverTargetFramerate}");

            // 2. Disable audio entirely
            AudioListener.volume = 0f;
            AudioListener.pause = true;
            Debug.Log("[ServerBootstrap] Audio disabled");

            // 3. Background mode — server should never pause
            Application.runInBackground = true;

            // 4. Disable screen sleep
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // 5. Disable VSync (irrelevant for headless)
            QualitySettings.vSyncCount = 0;

            Debug.Log("=== GV2 DEDICATED SERVER READY ===");
        }

        /// <summary>
        /// Checks if this build should run as a dedicated server.
        /// Mirrors the same logic as NetworkManager.ServerMode.Auto.
        /// </summary>
        private bool IsDedicatedServer()
        {
            #if UNITY_SERVER
            return true;
            #endif

            string[] args = System.Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg.ToLower() == "-server" || arg.ToLower() == "--server")
                    return true;
            }
            return false;
        }
    }
}
