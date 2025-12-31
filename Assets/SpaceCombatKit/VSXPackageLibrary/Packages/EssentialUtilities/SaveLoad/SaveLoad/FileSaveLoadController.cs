using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


namespace VSX.Utilities
{
    /// <summary>
    /// Save and load data from a file (the persistent data path) in string format.
    /// </summary>
    public class FileSaveLoadController : SaveLoadController
    {
        [Tooltip("The name of the file to save to or load from.")]
        [SerializeField]
        protected string fileName = "SavedSettings";


        /// <summary>
        /// Save data in string format.
        /// </summary>
        /// <param name="saveData">The data to save.</param>
        public override void Save(string saveData)
        {
            File.WriteAllText(GetFilePath(), saveData);
            if (debug)
            {
                Debug.Log("Saved setting to file at persistent data path " + GetFilePath());
                Debug.Log("Saved setting data: " + saveData);
            }
        }


        /// <summary>
        /// Load data into string format.
        /// </summary>
        /// <returns>The loaded data.</returns>
        public override string Load()
        {

            if (!File.Exists(GetFilePath()))
            {
                if (debug) Debug.Log("Failed to load setting, file at " + GetFilePath() + " does not exist.");
                return "";
            }

            string dataString = File.ReadAllText(GetFilePath());

            if (debug)
            {
                Debug.Log("Loaded setting file at path " + GetFilePath());
                Debug.Log("Loaded setting data: " + dataString);
            }

            return dataString;
        }


        /// <summary>
        /// Get the full file path to save to.
        /// </summary>
        /// <returns>The file path.</returns>
        public virtual string GetFilePath()
        {
            return (Application.persistentDataPath + "/" + fileName + ".json");
        }


        /// <summary>
        /// Delete the saved data.
        /// </summary>
        public override void DeleteSaveData()
        {
            bool exists = File.Exists(GetFilePath());
            if (exists)
            {
                File.Delete(GetFilePath());
                if (debug) Debug.Log("Deleted setting file at path " + GetFilePath());
            }
            else
            {
                if (debug) Debug.Log("Failed to delete setting, file not found at path " + GetFilePath());
            }
        }
    }
}
