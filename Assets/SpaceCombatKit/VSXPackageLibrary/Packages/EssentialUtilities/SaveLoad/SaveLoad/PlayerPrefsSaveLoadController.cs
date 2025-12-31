using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace VSX.Utilities
{
    /// <summary>
    /// Save and load data from Player Prefs in string format.
    /// </summary>
    public class PlayerPrefsSaveLoadController : SaveLoadController
    {
        [Tooltip("The name of the Player Prefs key to save to and load from.")]
        [SerializeField]
        protected string key = "SavedData";


        /// <summary>
        /// Save data in string format.
        /// </summary>
        /// <param name="saveData">The data to save.</param>
        public override void Save(string saveData)
        {
            PlayerPrefs.SetString(key, saveData);
            if (debug)
            {
                Debug.Log("Saved data to PlayerPrefs with key " + key);
                Debug.Log("Saved data: " + saveData);
            }
        }


        /// <summary>
        /// Load data into string format.
        /// </summary>
        /// <returns>The loaded data.</returns>
        public override string Load()
        {
            if (string.IsNullOrEmpty(PlayerPrefs.GetString(key)))
            {
                if (debug) Debug.Log("Failed to load data, key " + key + " does not exist in PlayerPrefs.");
                return "";
            }

            string dataString = PlayerPrefs.GetString(key);

            if (debug)
            {
                Debug.Log("Loaded data from PlayerPrefs with key " + key);
                Debug.Log("Loaded data: " + dataString);
            }

            return dataString;
        }


        /// <summary>
        /// Delete the saved data.
        /// </summary>
        public override void DeleteSaveData()
        {
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                Debug.Log("Removed saved data from PlayerPrefs with key " + key);
            }
        }
    }
}

