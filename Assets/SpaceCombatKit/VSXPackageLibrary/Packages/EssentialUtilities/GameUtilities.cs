using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace VSX.Utilities
{
    /// <summary>
    /// Common game utility functions that can be triggered by events.
    /// </summary>
    public class GameUtilities : MonoBehaviour
    {
        /// <summary>
        /// Quit the application.
        /// </summary>
        public virtual void ApplicationQuit()
        {
            Application.Quit();
        }


        /// <summary>
        /// Load a scene.
        /// </summary>
        /// <param name="sceneName">The scene name.</param>
        public virtual void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }


        /// <summary>
        /// Load a scene.
        /// </summary>
        /// <param name="sceneIndex">The scene build index.</param>
        public virtual void LoadScene(int sceneIndex)
        {
            SceneManager.LoadScene(sceneIndex);
        }


        /// <summary>
        /// Reload the currently active scene.
        /// </summary>
        public virtual void ReloadActiveScene()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}

